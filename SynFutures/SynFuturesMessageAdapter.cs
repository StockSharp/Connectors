namespace StockSharp.SynFutures;

public partial class SynFuturesMessageAdapter
{
	private class MarketSubscription
	{
		public long TransactionId { get; init; }
		public SynFuturesMarket Market { get; init; }
	}

	private sealed class DepthSubscription : MarketSubscription
	{
		public int Depth { get; init; }
	}

	private sealed class TickSubscription : MarketSubscription
	{
		public int Count { get; init; }
		public bool IsHistoryOnly { get; init; }
		public HashSet<string> SeenTrades { get; } =
			new(StringComparer.OrdinalIgnoreCase);
	}

	private sealed class CandleSubscription : MarketSubscription
	{
		public TimeSpan TimeFrame { get; init; }
		public DateTime LastOpenTime { get; set; }
		public DateTime NextPollTime { get; set; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, SynFuturesMarket> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, SynFuturesMarket> _marketsByPair =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, TickSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription>
		_candleSubscriptions = [];
	private readonly Dictionary<string, int> _channelReferences =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, long> _transactionByOrder =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, SynFuturesOpenOrder> _knownOrders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _knownPositions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim _accountRefreshGate = new(1, 1);
	private SynFuturesApiClient _apiClient;
	private SynFuturesRpcClient _rpcClient;
	private SynFuturesSocketClient _socketClient;
	private string _portfolioName;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private DateTime _lastAccountRefresh;
	private DateTime _serverTime;
	private bool _isPortfolioStreamSubscribed;

	/// <summary>Supported candle time-frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(4),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
	];

	/// <summary>Initializes a new instance.</summary>
	public SynFuturesMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override string[] AssociatedBoards => [BoardCodes.SynFutures];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.SynFutures) ||
			securityId.IsAssociated(BoardCodes.SynFutures);

	private SynFuturesApiClient ApiClient => _apiClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private SynFuturesRpcClient RpcClient => _rpcClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private SynFuturesSocketClient SocketClient => _socketClient ?? throw new
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
		time = time.EnsureUtc();
		using (_sync.EnterScope())
			if (time > _serverTime)
				_serverTime = time;
	}

	private SynFuturesMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				"Security board '" + securityId.BoardCode +
				"' is not SynFutures.");
		var symbol = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim();
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol, out var market)
				? market
				: throw new InvalidOperationException(
					"Unknown SynFutures market '" + symbol + "'.");
	}

	private SynFuturesMarket GetMarket(string instrument, uint expiry)
	{
		if (instrument.IsEmpty())
			return null;
		var key = PairKey(instrument, expiry);
		using (_sync.EnterScope())
			return _marketsByPair.TryGetValue(key, out var market) ? market : null;
	}

	private SynFuturesMarket[] GetMarkets()
	{
		using (_sync.EnterScope())
			return [.. _markets.Values.OrderBy(static market => market.Symbol,
				StringComparer.Ordinal)];
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
				"A SynFutures wallet address is required for account data.");
	}

	private void EnsureTradingReady()
	{
		EnsureAccountReady();
		if (!RpcClient.IsSigningAvailable)
			throw new InvalidOperationException(
				"An EVM private key is required for SynFutures transactions.");
	}

	private void ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() &&
			!portfolioName.Equals(_portfolioName,
				StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"Unknown SynFutures portfolio '" + portfolioName + "'.");
	}

	private static string PairKey(string instrument, uint expiry)
		=> instrument.NormalizeAddress() + "_" + expiry.ToString(
			CultureInfo.InvariantCulture);

	private static string MarketChannel(SynFuturesMarket market)
		=> "instrument:" + PairKey(market.InstrumentAddress, market.Expiry);

	private static string DepthChannel(SynFuturesMarket market)
		=> "orderBook:" + PairKey(market.InstrumentAddress, market.Expiry);

	private static string TradesChannel(SynFuturesMarket market)
		=> "trades:" + PairKey(market.InstrumentAddress, market.Expiry);

	private static string KlineChannel(SynFuturesMarket market,
		TimeSpan timeFrame)
		=> "kline:" + PairKey(market.InstrumentAddress, market.Expiry) + ":" +
			timeFrame.ToApiInterval();

	private static bool AddReference(Dictionary<string, int> references,
		string channel)
	{
		references.TryGetValue(channel, out var count);
		references[channel] = count + 1;
		return count == 0;
	}

	private static bool ReleaseReference(Dictionary<string, int> references,
		string channel)
	{
		if (!references.TryGetValue(channel, out var count))
			return false;
		if (count > 1)
		{
			references[channel] = count - 1;
			return false;
		}
		references.Remove(channel);
		return true;
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_marketsByPair.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_channelReferences.Clear();
			_transactionByOrder.Clear();
			_knownOrders.Clear();
			_knownPositions.Clear();
			_serverTime = default;
		}
		_portfolioName = null;
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
		_lastAccountRefresh = default;
		_isPortfolioStreamSubscribed = false;
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClientsAsync(default).AsTask().GetAwaiter().GetResult();
		_accountRefreshGate.Dispose();
		base.DisposeManaged();
	}
}
