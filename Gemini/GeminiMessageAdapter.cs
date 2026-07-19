namespace StockSharp.Gemini;

public partial class GeminiMessageAdapter
{
	private static readonly Regex _clientOrderIdPattern = new(
		"^[:\\-_\\.#a-zA-Z0-9]{1,36}$", RegexOptions.Compiled |
		RegexOptions.CultureInvariant);
	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _depthProcessing = new(1, 1);
	private readonly HashSet<string> _symbols =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, GeminiSymbolDetails> _details =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
	private readonly Dictionary<string, DepthState> _depthStates =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, TickSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<string, int> _streamReferences =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, long> _transactionByClientOrderId =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<long> _seenFillIds = [];
	private readonly Dictionary<long, StreamOrderState> _streamOrders = [];
	private readonly Dictionary<string, PendingOrderState> _pendingOrders =
		new(StringComparer.OrdinalIgnoreCase);
	private GeminiRestClient _restClient;
	private GeminiWsClient _wsClient;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private bool _isTradingOrderStreamRequired;

	private class MarketSubscription
	{
		public string Symbol { get; init; }
	}

	private sealed class CandleSubscription : MarketSubscription
	{
		public TimeSpan TimeFrame { get; init; }
		public bool IsStreamReady { get; set; }
		public List<GeminiWsTrade> Pending { get; } = [];
		public bool IsInitialized { get; set; }
		public DateTime OpenTime { get; set; }
		public decimal Open { get; set; }
		public decimal High { get; set; }
		public decimal Low { get; set; }
		public decimal Close { get; set; }
		public decimal Volume { get; set; }
	}

	private sealed class TickSubscription : MarketSubscription
	{
		public bool IsStreamReady { get; set; }
		public List<GeminiWsTrade> Pending { get; } = [];
	}

	private sealed class DepthState
	{
		public string Symbol { get; init; }
		public long LastUpdateId { get; set; }
		public bool IsSnapshotReady { get; set; }
		public Dictionary<long, int> Subscribers { get; } = [];
		public SortedDictionary<decimal, decimal> Bids { get; } = [];
		public SortedDictionary<decimal, decimal> Asks { get; } = [];
	}

	private sealed class StreamOrderState
	{
		public long OrderId { get; init; }
		public string Symbol { get; set; }
		public string ClientOrderId { get; set; }
		public GeminiWsSides? Side { get; set; }
		public GeminiWsOrderTypes? OrderType { get; set; }
		public GeminiWsOrderStatuses Status { get; set; }
		public decimal Price { get; set; }
		public decimal StopPrice { get; set; }
		public decimal OriginalQuantity { get; set; }
		public decimal RemainingQuantity { get; set; }
		public TimeInForce? TimeInForce { get; set; }
		public bool? IsPostOnly { get; set; }
	}

	private sealed class PendingOrderState
	{
		public string Symbol { get; init; }
		public GeminiWsSides Side { get; init; }
		public GeminiWsOrderTypes OrderType { get; init; }
		public decimal Price { get; init; }
		public decimal StopPrice { get; init; }
		public decimal Volume { get; init; }
		public TimeInForce? TimeInForce { get; init; }
		public bool? IsPostOnly { get; init; }
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="GeminiMessageAdapter"/>.
	/// </summary>
	public GeminiMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(30);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription)
		=> subscription is not null && GeminiExtensions.TimeFrames
			.Contains(subscription.GetTimeFrame());

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Gemini];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty()
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Gemini)
			|| securityId.IsAssociated(BoardCodes.Gemini);

	private GeminiRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private GeminiWsClient WsClient
		=> _wsClient ?? throw new InvalidOperationException(
			"Gemini WebSocket is not connected.");

	private void EnsureConnected()
	{
		if (_restClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureRealtimeReady()
	{
		EnsureConnected();
		if (_wsClient is null)
			throw new InvalidOperationException(
				"Gemini WebSocket is unavailable for this connection.");
	}

	private void EnsurePrivateRestReady()
	{
		EnsureConnected();
		if (!RestClient.IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Gemini API key and secret are required for private operations.");
	}

	private void EnsurePrivateWebSocketReady()
	{
		EnsureRealtimeReady();
		if (!WsClient.IsPrivateAvailable)
			throw new InvalidOperationException(
				"Gemini WebSocket trading requires an account-scoped API key " +
				"whose name starts with 'account-'.");
	}

	private string GetSymbol(SecurityId securityId)
	{
		var symbol = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode))
			.Trim().Replace("_", string.Empty, StringComparison.Ordinal)
			.Replace("-", string.Empty, StringComparison.Ordinal)
			.Replace("/", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
		using (_sync.EnterScope())
			if (_symbols.Contains(symbol))
				return symbol;
		throw new InvalidOperationException($"Unknown Gemini symbol '{symbol}'.");
	}

	private static string GetPortfolioName(SecureString key, string account)
		=> $"Gemini_{(account.IsEmpty() ? key.IsEmpty() ? "Public" : key.ToId() : account)}";

	private string GetPortfolioName() => GetPortfolioName(Key, Account);

	private string CreateClientOrderId(long transactionId, string userOrderId)
	{
		var clientOrderId = !userOrderId.IsEmpty() &&
			_clientOrderIdPattern.IsMatch(userOrderId)
			? userOrderId
			: $"s-{transactionId}";
		using (_sync.EnterScope())
			_transactionByClientOrderId[clientOrderId] = transactionId;
		return clientOrderId;
	}

	private long GetTransactionId(string clientOrderId)
	{
		if (clientOrderId.IsEmpty())
			return 0;
		using (_sync.EnterScope())
			return _transactionByClientOrderId.TryGetValue(clientOrderId,
				out var transactionId) ? transactionId : 0;
	}

	private static int NormalizeDepth(int? requested)
		=> (requested ?? 100).Min(5000).Max(1);

	private static bool AddReference(IDictionary<string, int> references, string stream)
	{
		if (references.TryGetValue(stream, out var count))
		{
			references[stream] = count + 1;
			return false;
		}
		references.Add(stream, 1);
		return true;
	}

	private static bool ReleaseReference(IDictionary<string, int> references,
		string stream)
	{
		if (!references.TryGetValue(stream, out var count))
			return false;
		if (count > 1)
		{
			references[stream] = count - 1;
			return false;
		}
		references.Remove(stream);
		return true;
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_symbols.Clear();
			_details.Clear();
			_level1Subscriptions.Clear();
			_depthStates.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_streamReferences.Clear();
			_transactionByClientOrderId.Clear();
			_seenFillIds.Clear();
			_streamOrders.Clear();
			_pendingOrders.Clear();
			_isTradingOrderStreamRequired = false;
		}
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		_depthProcessing.Dispose();
		base.DisposeManaged();
	}
}
