namespace StockSharp.Avantis;

public partial class AvantisMessageAdapter
{
	private readonly Lock _sync = new();
	private readonly Dictionary<string, AvantisMarket> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<int, AvantisMarket> _marketsByIndex = [];
	private readonly Dictionary<int, AvantisPriceUpdate> _prices = [];
	private readonly Dictionary<long, int> _level1Subscriptions = [];
	private readonly Dictionary<int, int> _feedReferences = [];
	private readonly Dictionary<string, long> _transactionByOrder =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, AvantisLimitOrder> _knownLimitOrders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<int> _knownPositionMarkets = [];
	private AvantisApiClient _apiClient;
	private AvantisRpcClient _rpcClient;
	private AvantisFeedClient _feedClient;
	private string _portfolioName;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private DateTime _lastAccountRefresh;
	private DateTime _serverTime;

	/// <summary>Initializes a new instance.</summary>
	public AvantisMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(5);
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
	public override string[] AssociatedBoards => [BoardCodes.Avantis];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Avantis) ||
			securityId.IsAssociated(BoardCodes.Avantis);

	private AvantisApiClient ApiClient => _apiClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private AvantisRpcClient RpcClient => _rpcClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private AvantisFeedClient FeedClient => _feedClient ?? throw new
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
		time = time.EnsureAvantisUtc();
		using (_sync.EnterScope())
			if (time > _serverTime)
				_serverTime = time;
	}

	private AvantisMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				"Security board '" + securityId.BoardCode + "' is not Avantis.");
		var symbol = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim();
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol, out var market)
				? market
				: throw new InvalidOperationException(
					"Unknown Avantis market '" + symbol + "'.");
	}

	private AvantisMarket GetMarket(int pairIndex)
	{
		using (_sync.EnterScope())
			return _marketsByIndex.TryGetValue(pairIndex, out var market)
				? market
				: null;
	}

	private AvantisMarket[] GetMarkets()
	{
		using (_sync.EnterScope())
			return [.. _markets.Values.OrderBy(static market => market.PairIndex)];
	}

	private AvantisPriceUpdate GetPrice(int pairIndex)
	{
		using (_sync.EnterScope())
			return _prices.TryGetValue(pairIndex, out var price) ? price : null;
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
		if (_apiClient is null || _rpcClient is null || _feedClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureAccountReady()
	{
		EnsureConnected();
		if (!RpcClient.IsWalletConfigured)
			throw new InvalidOperationException(
				"An Avantis wallet address is required for account data.");
	}

	private void EnsureTradingReady()
	{
		EnsureAccountReady();
		if (!RpcClient.IsSigningAvailable)
			throw new InvalidOperationException(
				"An EVM private key is required for Avantis transactions.");
	}

	private void ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() &&
			!portfolioName.Equals(_portfolioName,
				StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"Unknown Avantis portfolio '" + portfolioName + "'.");
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_marketsByIndex.Clear();
			_prices.Clear();
			_level1Subscriptions.Clear();
			_feedReferences.Clear();
			_transactionByOrder.Clear();
			_knownLimitOrders.Clear();
			_knownPositionMarkets.Clear();
			_serverTime = default;
		}
		_portfolioName = null;
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
		_lastAccountRefresh = default;
	}
}
