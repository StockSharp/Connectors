namespace StockSharp.Gmx;

/// <summary>The message adapter for GMX V2 markets.</summary>
[MediaIcon(Media.MediaNames.gmx)]
[Doc("topics/api/connectors/crypto_exchanges/gmx.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.GmxKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Ticks |
	MessageAdapterCategories.Candles)]
[OrderCondition(typeof(GmxOrderCondition))]
public partial class GmxMessageAdapter : MessageAdapter
{
	private class MarketSubscription
	{
		public long TransactionId { get; init; }
		public string MarketAddress { get; init; }
	}

	private sealed class CandleSubscription : MarketSubscription
	{
		public TimeSpan TimeFrame { get; init; }
		public DateTime? LastOpenTime { get; set; }
	}

	private sealed class OrderStatusSubscription
	{
		public string[] Symbols { get; init; }
		public string OrderStringId { get; init; }
		public Sides? Side { get; init; }
		public decimal? Volume { get; init; }
		public OrderStates[] States { get; init; }
		public DateTime? From { get; init; }
		public DateTime? To { get; init; }
		public int Skip { get; init; }
		public int Limit { get; init; }
	}

	private enum GmxPendingOperations
	{
		Create,
		Edit,
		Cancel,
	}

	private sealed class PendingOrder
	{
		public string OrderId { get; init; }
		public string MarketAddress { get; init; }
		public Sides Side { get; init; }
		public decimal Volume { get; init; }
		public decimal Price { get; init; }
		public OrderTypes OrderType { get; init; }
		public OrderPositionEffects? PositionEffect { get; init; }
	}

	private sealed class PendingRequest
	{
		public string RequestId { get; init; }
		public long TransactionId { get; init; }
		public GmxPendingOperations Operation { get; init; }
		public string[] OrderIds { get; set; }
		public PendingOrder[] Orders { get; init; }
		public string MarketAddress { get; init; }
		public Sides Side { get; init; }
		public decimal Volume { get; init; }
		public decimal Price { get; init; }
		public OrderTypes OrderType { get; init; }
		public OrderPositionEffects? PositionEffect { get; init; }
		public string LastStatus { get; set; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, GmxMarket> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, GmxMarket> _marketsByAddress =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, GmxToken> _tokensByAddress =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, GmxToken> _tokensBySymbol =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderStatusSubscription>
		_orderSubscriptions = [];
	private readonly Dictionary<string, PendingRequest> _pendingRequests =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, long> _transactionByOrder =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenPublicTrades =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenAccountTrades =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _knownPositions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, GmxOrder> _knownOrders =
		new(StringComparer.OrdinalIgnoreCase);
	private GmxApiClient _apiClient;
	private GmxSigner _signer;
	private string _walletAddress;
	private string _portfolioName;
	private DateTime _lastMarketRefresh;
	private DateTime _lastTradeRefresh;
	private DateTime _lastCandleRefresh;
	private DateTime _lastAccountRefresh;
	private DateTime _lastStatusRefresh;
	private DateTime _serverTime;
	private bool _isPolling;

	/// <summary>Supported candle time-frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => GmxExtensions.TimeFrames;

	/// <summary>Initializes a new instance.</summary>
	public GmxMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities ||
			dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription)
		=> true;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Gmx];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Gmx) ||
			securityId.IsAssociated(BoardCodes.Gmx);

	private GmxApiClient ApiClient => _apiClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private GmxSigner Signer => _signer ?? throw new InvalidOperationException(
		"A GMX private key is required for transactions.");

	private DateTime ServerTime
	{
		get
		{
			using (_sync.EnterScope())
				return _serverTime == default ? DateTime.UtcNow : _serverTime;
		}
	}

	private void UpdateServerTime(DateTime value)
	{
		value = value.EnsureGmxUtc();
		using (_sync.EnterScope())
			if (value > _serverTime)
				_serverTime = value;
	}

	private GmxMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				"Security board '" + securityId.BoardCode + "' is not GMX.");
		return GetMarket(securityId.SecurityCode);
	}

	private GmxMarket GetMarket(string symbol)
	{
		symbol = symbol.ThrowIfEmpty(nameof(symbol)).Trim();
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol, out var market)
				? market
				: throw new InvalidOperationException(
					"Unknown GMX market '" + symbol + "'.");
	}

	private GmxMarket GetMarketByAddress(string address)
	{
		if (address.IsEmpty())
			return null;
		using (_sync.EnterScope())
			return _marketsByAddress.TryGetValue(address, out var market)
				? market
				: null;
	}

	private GmxMarket[] GetMarkets()
	{
		using (_sync.EnterScope())
			return [.. _markets.Values];
	}

	private GmxToken GetToken(string symbolOrAddress)
	{
		symbolOrAddress = symbolOrAddress.ThrowIfEmpty(nameof(symbolOrAddress))
			.Trim();
		using (_sync.EnterScope())
		{
			if (_tokensByAddress.TryGetValue(symbolOrAddress, out var token) ||
				_tokensBySymbol.TryGetValue(symbolOrAddress, out token))
				return token;
		}
		throw new InvalidOperationException(
			"Unknown GMX token '" + symbolOrAddress + "'.");
	}

	private void TrackOrder(string orderId, long transactionId)
	{
		if (orderId.IsEmpty() || transactionId <= 0)
			return;
		using (_sync.EnterScope())
			_transactionByOrder[orderId] = transactionId;
	}

	private long GetOriginalTransactionId(string orderId,
		long fallbackTransactionId)
	{
		using (_sync.EnterScope())
			return !orderId.IsEmpty() &&
				_transactionByOrder.TryGetValue(orderId, out var transactionId)
					? transactionId
					: fallbackTransactionId;
	}

	private void EnsureConnected()
	{
		if (_apiClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureAccountReady()
	{
		EnsureConnected();
		if (_walletAddress.IsEmpty())
			throw new InvalidOperationException(
				"A GMX wallet address is required for account data.");
	}

	private void EnsureTradingReady()
	{
		EnsureAccountReady();
		_ = Signer;
	}

	private void ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() &&
			!portfolioName.Equals(_portfolioName,
				StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"Unknown GMX portfolio '" + portfolioName + "'.");
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_marketsByAddress.Clear();
			_tokensByAddress.Clear();
			_tokensBySymbol.Clear();
			_level1Subscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_pendingRequests.Clear();
			_transactionByOrder.Clear();
			_seenPublicTrades.Clear();
			_seenAccountTrades.Clear();
			_knownPositions.Clear();
			_knownOrders.Clear();
			_serverTime = default;
			_isPolling = false;
		}
		_walletAddress = null;
		_portfolioName = null;
		_lastMarketRefresh = default;
		_lastTradeRefresh = default;
		_lastCandleRefresh = default;
		_lastAccountRefresh = default;
		_lastStatusRefresh = default;
	}
}
