namespace StockSharp.WooX;

public partial class WooXMessageAdapter
{
	private readonly Lock _sync = new();
	private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<StreamKey, int> _streamReferences = [];
	private readonly Dictionary<string, WooXSymbol> _symbols = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenFillIds = new(StringComparer.OrdinalIgnoreCase);
	private WooXRestClient _restClient;
	private WooXPublicWsClient _publicWsClient;
	private WooXPrivateWsClient _privateWsClient;
	private string _publicApplicationId;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private long _publicTradeId;
	private bool _symbolsLoaded;

	private class MarketSubscription
	{
		public string Symbol { get; init; }
	}

	private sealed class DepthSubscription : MarketSubscription
	{
		public int Depth { get; init; }
	}

	private sealed class CandleSubscription : MarketSubscription
	{
		public TimeSpan TimeFrame { get; init; }
	}

	private readonly record struct StreamKey(WooXWsTopics Topic, string Symbol,
		TimeSpan TimeFrame);

	/// <summary>
	/// Initializes a new instance of the <see cref="WooXMessageAdapter"/>.
	/// </summary>
	public WooXMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(10);
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
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => false;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.WooX, BoardCodes.WooXFutures];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty()
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.WooX)
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.WooXFutures)
			|| securityId.IsAssociated(BoardCodes.WooX)
			|| securityId.IsAssociated(BoardCodes.WooXFutures);

	private WooXRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private WooXPublicWsClient PublicWsClient
		=> _publicWsClient ?? throw new InvalidOperationException(
			"The WOO X public WebSocket is not connected.");

	private WooXPrivateWsClient PrivateWsClient
		=> _privateWsClient ?? throw new InvalidOperationException(
			"The WOO X private WebSocket is not connected.");

	private void EnsureConnected()
	{
		if (_restClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureRealtimeReady()
	{
		EnsureConnected();
		if (_publicWsClient is null)
			throw new InvalidOperationException(
				"WOO X public WebSocket is unavailable for this connection.");
	}

	private void EnsurePrivateReady()
	{
		EnsureConnected();
		if (!RestClient.IsCredentialsAvailable || _privateWsClient is null)
			throw new InvalidOperationException(
				"WOO X API key, secret, and application ID are required for private operations.");
	}

	private string GetSymbol(SecurityId securityId)
	{
		var code = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode))
			.Trim().Replace('-', '_').Replace('/', '_').ToUpperInvariant();
		if (code.StartsWith("SPOT_", StringComparison.Ordinal) ||
			code.StartsWith("PERP_", StringComparison.Ordinal))
			return code;
		if (securityId.BoardCode.EqualsIgnoreCase(BoardCodes.WooXFutures))
			return "PERP_" + code;
		if (securityId.BoardCode.EqualsIgnoreCase(BoardCodes.WooX))
			return "SPOT_" + code;

		using (_sync.EnterScope())
		{
			var candidates = _symbols.Keys.Where(symbol => symbol.EndsWith("_" + code,
				StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
			if (candidates.Length == 1)
				return candidates[0];
		}
		throw new InvalidOperationException(
			"SecurityId.BoardCode or a SPOT_/PERP_ symbol prefix is required by WOO X.");
	}

	private static string GetPortfolioName(SecureString key)
		=> $"WooX_{(key.IsEmpty() ? "Public" : key.ToId())}";

	private string GetPortfolioName() => GetPortfolioName(Key);

	private static long CreateClientOrderId(long transactionId, string userOrderId)
	{
		if (!userOrderId.IsEmpty() && long.TryParse(userOrderId, NumberStyles.None,
			CultureInfo.InvariantCulture, out var userId) && userId >= 0)
			return userId;
		if (transactionId < 0)
			throw new InvalidOperationException("WOO X requires a non-negative numeric client order ID.");
		return transactionId;
	}

	private static long ParseTransactionId(long clientOrderId) => clientOrderId > 0 ? clientOrderId : 0;

	private static int NormalizeDepth(int? depth)
		=> (depth ?? 100).Min(1000).Max(1);

	private static bool AddReference<TKey>(IDictionary<TKey, int> references, TKey key)
	{
		if (references.TryGetValue(key, out var count))
		{
			references[key] = count + 1;
			return false;
		}
		references.Add(key, 1);
		return true;
	}

	private static bool ReleaseReference<TKey>(IDictionary<TKey, int> references, TKey key)
	{
		if (!references.TryGetValue(key, out var count))
			return false;
		if (count > 1)
		{
			references[key] = count - 1;
			return false;
		}
		references.Remove(key);
		return true;
	}
}
