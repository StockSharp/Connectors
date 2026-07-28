namespace StockSharp.Dexalot;

public partial class DexalotMessageAdapter
{
	private sealed class Level1Subscription
	{
		public DexalotPair Pair { get; init; }
	}

	private sealed class DepthSubscription
	{
		public DexalotPair Pair { get; init; }
		public int Depth { get; init; }
		public bool HistoryOnly { get; init; }
	}

	private sealed class TickSubscription
	{
		public DexalotPair Pair { get; init; }
		public DateTime? From { get; init; }
		public DateTime? To { get; init; }
		public int Maximum { get; init; }
		public int Delivered { get; set; }
		public bool HistoryOnly { get; init; }
	}

	private sealed class CandleSubscription
	{
		public DexalotPair Pair { get; init; }
		public TimeSpan TimeFrame { get; init; }
		public DateTime? From { get; init; }
		public DateTime? To { get; init; }
		public int Maximum { get; init; }
		public int Delivered { get; set; }
		public bool HistoryOnly { get; init; }
	}

	private sealed class TrackedOrder
	{
		public long TransactionId { get; init; }
		public string TransactionHash { get; set; }
		public string OrderId { get; set; }
		public string ClientOrderId { get; init; }
		public DexalotPair Pair { get; init; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public TimeInForce? TimeInForce { get; init; }
		public decimal Price { get; set; }
		public decimal Volume { get; set; }
		public decimal FilledVolume { get; set; }
		public decimal? Commission { get; set; }
		public string CommissionCurrency { get; set; }
		public DateTime Time { get; init; }
		public DateTime UpdateTime { get; set; }
		public OrderStates State { get; set; }
		public bool TradeSent { get; set; }
	}

	private sealed class OrderSubscription
	{
		public string OrderId { get; init; }
		public SecurityId SecurityId { get; init; }
		public Sides? Side { get; init; }
		public OrderStates[] States { get; init; }
		public DateTime? From { get; init; }
		public DateTime? To { get; init; }
		public int Skip { get; init; }
		public int Maximum { get; init; }
	}

	private readonly record struct DeliveryKey(long SubscriptionId,
		string Identity);
	private readonly record struct OrderFingerprint(OrderStates State,
		decimal FilledVolume);
	private const int _maximumDeliveryKeys = 100_000;

	private readonly Lock _sync = new();
	private readonly Dictionary<string, DexalotPair> _pairs =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, Level1Subscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription>
		_depthSubscriptions = [];
	private readonly Dictionary<long, TickSubscription>
		_tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription>
		_candleSubscriptions = [];
	private readonly Dictionary<string, int> _pairReferenceCounts =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, int> _chartReferenceCounts =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<DeliveryKey> _seenMarketData = [];
	private readonly Queue<DeliveryKey> _deliveryOrder = [];
	private readonly Queue<JObject> _socketMessages = [];
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderSubscription>
		_orderSubscriptions = [];
	private readonly Dictionary<string, TrackedOrder> _trackedOrders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, OrderFingerprint>
		_orderFingerprints = new(StringComparer.OrdinalIgnoreCase);
	private DexalotRestClient _restClient;
	private DexalotSocketClient _socketClient;
	private DexalotEvmClient _evmClient;
	private string _tradePairsAddress;
	private string _portfolioAddress;
	private DateTime _nextPrivatePoll;

	/// <summary>Initializes the adapter.</summary>
	public DexalotMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities ||
			dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(
		MarketDataMessage subscription)
		=> true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Dexalot];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Dexalot) ||
			securityId.IsAssociated(BoardCodes.Dexalot);

	private DexalotRestClient RestClient => _restClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private DexalotSocketClient SocketClient => _socketClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private DexalotEvmClient EvmClient => _evmClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_restClient is null || _socketClient is null ||
			_evmClient is null)
			throw new InvalidOperationException(
				LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureTradingReady()
	{
		EnsureConnected();
		if (!EvmClient.IsSigningAvailable)
			throw new InvalidOperationException(
				"A Dexalot private key is required for trading.");
	}

	private DexalotPair GetPair(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not Dexalot.");
		var code = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId)).Trim();
		using (_sync.EnterScope())
			return _pairs.TryGetValue(code, out var pair)
				? pair
				: throw new InvalidOperationException(
					$"Unknown Dexalot pair '{code}'.");
	}

	private string GetPortfolioName()
	{
		if (!EvmClient.IsWalletConfigured)
			throw new InvalidOperationException(
				"A Dexalot wallet address is required for portfolio data.");
		return $"Dexalot_{EvmClient.WalletAddress[2..10]}";
	}

	private void ValidatePortfolio(string portfolioName)
	{
		var expected = GetPortfolioName();
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(expected))
			throw new InvalidOperationException(
				$"Unknown Dexalot portfolio '{portfolioName}'.");
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		base.DisposeManaged();
	}
}
