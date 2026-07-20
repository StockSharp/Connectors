namespace StockSharp.Ostium;

public partial class OstiumMessageAdapter
{
	private sealed class CandleSubscription
	{
		public long TransactionId { get; init; }
		public OstiumMarket Market { get; init; }
		public TimeSpan TimeFrame { get; init; }
		public DateTime LastOpenTime { get; set; }
		public DateTime NextPollTime { get; set; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, OstiumMarket> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<int, OstiumMarket> _marketsByIndex = [];
	private readonly Dictionary<string, OstiumMarket> _marketsByPricePair =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, OstiumPrice> _prices =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, int> _level1Subscriptions = [];
	private readonly Dictionary<int, int> _priceReferences = [];
	private readonly Dictionary<long, CandleSubscription>
		_candleSubscriptions = [];
	private readonly Dictionary<string, long> _transactionByOrder =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, OstiumGraphLimit> _knownLimits =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<int> _knownPositionMarkets = [];
	private OstiumApiClient _apiClient;
	private OstiumRpcClient _rpcClient;
	private OstiumSocketClient _socketClient;
	private string _portfolioName;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private DateTime _lastAccountRefresh;
	private DateTime _serverTime;

	/// <summary>Supported candle time-frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(4),
		TimeSpan.FromDays(1),
	];

	/// <summary>Initializes a new instance.</summary>
	public OstiumMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities ||
			dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Ostium];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Ostium) ||
			securityId.IsAssociated(BoardCodes.Ostium);

	private OstiumApiClient ApiClient => _apiClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private OstiumRpcClient RpcClient => _rpcClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private OstiumSocketClient SocketClient => _socketClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private DateTime ServerTime
	{
		get
		{
			using (_sync.EnterScope())
				return _serverTime == default ? DateTime.UtcNow : _serverTime;
		}
	}

	private void UpdateServerTime(DateTime time)
	{
		time = time.EnsureOstiumUtc();
		using (_sync.EnterScope())
			if (time > _serverTime)
				_serverTime = time;
	}

	private OstiumMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				"Security board '" + securityId.BoardCode + "' is not Ostium.");
		var symbol = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim();
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol, out var market)
				? market
				: throw new InvalidOperationException(
					"Unknown Ostium market '" + symbol + "'.");
	}

	private OstiumMarket GetMarket(int pairIndex)
	{
		using (_sync.EnterScope())
			return _marketsByIndex.TryGetValue(pairIndex, out var market)
				? market
				: null;
	}

	private OstiumMarket GetMarketByPricePair(string pair)
	{
		if (pair.IsEmpty())
			return null;
		using (_sync.EnterScope())
			return _marketsByPricePair.TryGetValue(pair, out var market)
				? market
				: null;
	}

	private OstiumMarket[] GetMarkets()
	{
		using (_sync.EnterScope())
			return [.. _markets.Values.OrderBy(static market => market.PairIndex)];
	}

	private OstiumPrice GetPrice(OstiumMarket market)
	{
		using (_sync.EnterScope())
			return _prices.TryGetValue(market.PricePair, out var price)
				? price
				: null;
	}

	private void TrackOrder(string orderId, long transactionId)
	{
		if (orderId.IsEmpty() || transactionId == 0)
			return;
		using (_sync.EnterScope())
			_transactionByOrder[orderId] = transactionId;
	}

	private long GetOriginalTransactionId(string orderId)
	{
		using (_sync.EnterScope())
			return _transactionByOrder.TryGetValue(orderId, out var id)
				? id
				: _orderStatusSubscriptionId;
	}

	private void EnsureConnected()
	{
		if (_apiClient is null || _rpcClient is null || _socketClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureAccountReady()
	{
		EnsureConnected();
		if (!RpcClient.IsWalletConfigured)
			throw new InvalidOperationException(
				"An Ostium wallet address is required for account data.");
	}

	private void EnsureTradingReady()
	{
		EnsureAccountReady();
		if (!RpcClient.IsSigningAvailable)
			throw new InvalidOperationException(
				"An EVM private key is required for Ostium transactions.");
	}

	private void ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() &&
			!portfolioName.Equals(_portfolioName,
				StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"Unknown Ostium portfolio '" + portfolioName + "'.");
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_marketsByIndex.Clear();
			_marketsByPricePair.Clear();
			_prices.Clear();
			_level1Subscriptions.Clear();
			_priceReferences.Clear();
			_candleSubscriptions.Clear();
			_transactionByOrder.Clear();
			_knownLimits.Clear();
			_knownPositionMarkets.Clear();
			_serverTime = default;
		}
		_portfolioName = null;
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
		_lastAccountRefresh = default;
	}
}
