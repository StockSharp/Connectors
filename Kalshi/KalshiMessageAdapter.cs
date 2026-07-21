namespace StockSharp.Kalshi;

/// <summary>The message adapter for Kalshi event contracts.</summary>
[MediaIcon(Media.MediaNames.kalshi)]
[Doc("topics/api/connectors/stock_market/kalshi.html")]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.KalshiKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Candles | MessageAdapterCategories.Options |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Ticks)]
[OrderCondition(typeof(KalshiOrderCondition))]
public partial class KalshiMessageAdapter : MessageAdapter, IDemoAdapter
{
	private class MarketSubscription
	{
		public long TransactionId { get; init; }
		public string Ticker { get; init; }
	}

	private sealed class DepthSubscription : MarketSubscription
	{
		public int Depth { get; init; }
	}

	private sealed class OrderSubscription
	{
		public SecurityId SecurityId { get; init; }
		public SecurityId[] SecurityIds { get; init; }
		public string OrderId { get; init; }
		public Sides? Side { get; init; }
		public decimal? Volume { get; init; }
		public OrderStates[] States { get; init; }
		public DateTime? From { get; init; }
		public DateTime? To { get; init; }
		public int Skip { get; init; }
		public int Limit { get; init; }
	}

	private static readonly TimeSpan[] _timeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(4),
		TimeSpan.FromDays(1),
	];

	private readonly Lock _sync = new();
	private readonly Dictionary<string, KalshiMarket> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, KalshiBookState> _books =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderSubscription> _orderSubscriptions = [];
	private readonly Dictionary<string, long> _transactionsByOrder =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenFills =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _knownPositions =
		new(StringComparer.OrdinalIgnoreCase);
	private KalshiAuthenticator _authenticator;
	private KalshiRestClient _restClient;
	private KalshiSocketClient _socketClient;
	private string _portfolioName;
	private DateTime _serverTime;
	private DateTime _nextPrivatePoll;

	/// <summary>Initializes a new instance.</summary>
	public KalshiMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(_timeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities ||
			dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription)
		=> false;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Kalshi];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Kalshi) ||
			securityId.IsAssociated(BoardCodes.Kalshi);

	private KalshiRestClient RestClient => _restClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private KalshiSocketClient SocketClient => _socketClient ?? throw new
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

	private void AddMarket(KalshiMarket market)
	{
		if (market?.Ticker.IsEmpty() != false)
			throw new InvalidDataException("Kalshi returned a market without a ticker.");
		using (_sync.EnterScope())
			_markets[market.Ticker] = market;
	}

	private KalshiMarket GetCachedMarket(string ticker)
	{
		if (ticker.IsEmpty())
			return null;
		using (_sync.EnterScope())
			return _markets.TryGetValue(ticker, out var market) ? market : null;
	}

	private async ValueTask<KalshiMarket> GetMarketAsync(SecurityId securityId,
		CancellationToken cancellationToken)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				"Security board '" + securityId.BoardCode + "' is not Kalshi.");
		var ticker = securityId.Native as string;
		if (ticker.IsEmpty())
			ticker = securityId.SecurityCode;
		ticker = ticker.ThrowIfEmpty(nameof(securityId.SecurityCode)).Trim();
		var cached = GetCachedMarket(ticker);
		if (cached is not null)
			return cached;
		var market = await RestClient.GetMarketAsync(ticker, cancellationToken);
		AddMarket(market);
		return market;
	}

	private void EnsureConnected()
	{
		if (_restClient is null || _socketClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureAccountReady()
	{
		EnsureConnected();
		if (_authenticator?.IsAvailable != true)
			throw new InvalidOperationException(
				"Kalshi API credentials are required for account operations.");
	}

	private void ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() && !portfolioName.Equals(_portfolioName,
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"Unknown Kalshi portfolio '" + portfolioName + "'.");
	}

	private long GetOrderTransactionId(string orderId, long fallback)
	{
		using (_sync.EnterScope())
			return !orderId.IsEmpty() &&
				_transactionsByOrder.TryGetValue(orderId, out var transactionId)
					? transactionId
					: fallback;
	}

	private void TrackOrder(string orderId, long transactionId)
	{
		if (orderId.IsEmpty() || transactionId == 0)
			return;
		using (_sync.EnterScope())
			_transactionsByOrder[orderId] = transactionId;
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_books.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_transactionsByOrder.Clear();
			_seenFills.Clear();
			_knownPositions.Clear();
			_serverTime = default;
			_nextPrivatePoll = default;
		}
		_portfolioName = null;
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClientsAsync(default).AsTask().GetAwaiter().GetResult();
		base.DisposeManaged();
	}
}
