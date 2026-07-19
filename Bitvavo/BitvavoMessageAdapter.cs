namespace StockSharp.Bitvavo;

public partial class BitvavoMessageAdapter
{
	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _depthProcessing = new(1, 1);
	private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<string, int> _streamReferences =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, BitvavoMarket> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, long> _transactionByClientOrderId =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenFillIds = new(StringComparer.OrdinalIgnoreCase);
	private BitvavoRestClient _restClient;
	private BitvavoWsClient _wsClient;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;

	private class MarketSubscription
	{
		public string Market { get; init; }
	}

	private sealed class DepthSubscription : MarketSubscription
	{
		public int Depth { get; init; }
		public bool IsSnapshotReady { get; set; }
		public long LastNonce { get; set; }
		public List<BitvavoOrderBook> Pending { get; } = [];
	}

	private sealed class CandleSubscription : MarketSubscription
	{
		public TimeSpan TimeFrame { get; init; }
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="BitvavoMessageAdapter"/>.
	/// </summary>
	public BitvavoMessageAdapter(IdGenerator transactionIdGenerator)
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
		=> subscription is not null && BitvavoExtensions.StreamingTimeFrames
			.Contains(subscription.GetTimeFrame());

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Bitvavo];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty()
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Bitvavo)
			|| securityId.IsAssociated(BoardCodes.Bitvavo);

	private BitvavoRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private BitvavoWsClient WsClient
		=> _wsClient ?? throw new InvalidOperationException(
			"Bitvavo WebSocket is not connected.");

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
				"Bitvavo WebSocket is unavailable for this connection.");
	}

	private void EnsurePrivateReady()
	{
		EnsureRealtimeReady();
		if (!RestClient.IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Bitvavo API key and secret are required for private operations.");
	}

	private string GetMarket(SecurityId securityId)
	{
		var market = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode))
			.Trim().Replace('_', '-').Replace('/', '-').ToUpperInvariant();
		using (_sync.EnterScope())
		{
			if (_markets.ContainsKey(market))
				return market;
			var compact = market.Replace("-", string.Empty, StringComparison.Ordinal);
			var candidates = _markets.Keys
				.Where(value => value.Replace("-", string.Empty, StringComparison.Ordinal)
					.EqualsIgnoreCase(compact))
				.Take(2).ToArray();
			if (candidates.Length == 1)
				return candidates[0];
		}
		throw new InvalidOperationException($"Unknown Bitvavo market '{market}'.");
	}

	private static string GetPortfolioName(SecureString key)
		=> $"Bitvavo_{(key.IsEmpty() ? "Public" : key.ToId())}";

	private string GetPortfolioName() => GetPortfolioName(Key);

	private string CreateClientOrderId(long transactionId, string userOrderId)
	{
		var clientOrderId = Guid.TryParse(userOrderId, out var parsed)
			? parsed.ToString("D")
			: Guid.NewGuid().ToString("D");
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
				out var transactionId)
				? transactionId
				: 0;
	}

	private static int NormalizeDepth(int? requested)
		=> (requested ?? 100).Min(1000).Max(1);

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

	private static bool ReleaseReference(IDictionary<string, int> references, string stream)
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

	private string[] GetAllMarkets()
	{
		using (_sync.EnterScope())
			return [.. _markets.Keys.OrderBy(static market => market,
				StringComparer.OrdinalIgnoreCase)];
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_streamReferences.Clear();
			_markets.Clear();
			_transactionByClientOrderId.Clear();
			_seenFillIds.Clear();
		}
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		_depthProcessing.Dispose();
		base.DisposeManaged();
	}
}
