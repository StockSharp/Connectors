namespace StockSharp.DeepBook;

public partial class DeepBookMessageAdapter
{
	private const int _maximumDeliveryKeys = 100_000;

	private sealed class Level1Subscription
	{
		public DeepBookMarket Market { get; init; }
	}

	private sealed class DepthSubscription
	{
		public DeepBookMarket Market { get; init; }
		public int Depth { get; init; }
	}

	private sealed class TickSubscription
	{
		public DeepBookMarket Market { get; init; }
		public DateTime? To { get; init; }
		public int Maximum { get; init; }
		public int Delivered { get; set; }
		public DateTime? LastTime { get; set; }
	}

	private sealed class CandleSubscription
	{
		public DeepBookMarket Market { get; init; }
		public TimeSpan TimeFrame { get; init; }
		public DateTime? To { get; init; }
		public int Maximum { get; init; }
		public int Delivered { get; set; }
	}

	private sealed class TrackedSwap
	{
		public long TransactionId { get; init; }
		public string TransactionDigest { get; init; }
		public DeepBookMarket Market { get; init; }
		public Sides Side { get; init; }
		public decimal Volume { get; init; }
		public decimal Price { get; init; }
		public DateTime SubmittedTime { get; init; }
		public OrderStates State { get; init; }
		public DeepBookTransactionReceipt Receipt { get; init; }
	}

	private sealed class OrderSubscription
	{
		public string TransactionDigest { get; init; }
		public SecurityId SecurityId { get; init; }
		public Sides? Side { get; init; }
		public decimal? Volume { get; init; }
		public OrderStates[] States { get; init; }
		public DateTime? From { get; init; }
		public DateTime? To { get; init; }
		public int Skip { get; init; }
		public int Maximum { get; init; }
	}

	private readonly record struct DeliveryKey(long SubscriptionId,
		string Identity);
	private readonly record struct Level1Fingerprint(decimal BidPrice,
		decimal BidVolume, decimal AskPrice, decimal AskVolume);
	private readonly record struct BalanceFingerprint(decimal Current,
		decimal Blocked);
	private readonly record struct OrderFingerprint(OrderStates State,
		bool IsTradeSent);

	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _transactionGate = new(1, 1);
	private readonly Dictionary<string, DeepBookMarket> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, DeepBookMarket> _marketsByPool =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, DeepBookToken> _tokens =
		new(StringComparer.Ordinal);
	private readonly Dictionary<long, Level1Subscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription>
		_depthSubscriptions = [];
	private readonly Dictionary<long, TickSubscription> _tickSubscriptions =
		[];
	private readonly Dictionary<long, CandleSubscription>
		_candleSubscriptions = [];
	private readonly HashSet<DeliveryKey> _seenMarketData = [];
	private readonly Queue<DeliveryKey> _marketDataDeliveryOrder = [];
	private readonly Dictionary<long, Level1Fingerprint>
		_level1Fingerprints = [];
	private readonly Dictionary<long, string> _depthFingerprints = [];
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderSubscription>
		_orderSubscriptions = [];
	private readonly Dictionary<string, TrackedSwap> _trackedSwaps =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, BalanceFingerprint>
		_balanceFingerprints = new(StringComparer.Ordinal);
	private readonly Dictionary<string, OrderFingerprint>
		_orderFingerprints = new(StringComparer.Ordinal);
	private DeepBookApiClient _apiClient;
	private DeepBookSuiClient _suiClient;
	private DeepBookSharedObject _clock;
	private string _chainId;
	private DateTime _nextMarketPoll;
	private DateTime _nextPrivatePoll;

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

	/// <summary>Initializes the adapter.</summary>
	public DeepBookMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription)
		=> true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.DeepBook];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.DeepBook) ||
			securityId.IsAssociated(BoardCodes.DeepBook);

	private DeepBookApiClient ApiClient => _apiClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private DeepBookSuiClient SuiClient => _suiClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_apiClient is null || _suiClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureTradingReady()
	{
		EnsureConnected();
		if (!SuiClient.IsSigningAvailable)
			throw new InvalidOperationException(
				"A Sui Ed25519 private key is required for DeepBook swaps.");
	}

	private DeepBookMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not DeepBook.");
		var code = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId))
			.NormalizeSecurityCode();
		using (_sync.EnterScope())
			return _markets.TryGetValue(code, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown DeepBook market '{code}'.");
	}

	private string GetPortfolioName()
	{
		if (!SuiClient.IsWalletAvailable)
			throw new InvalidOperationException(
				"A Sui wallet address is required for portfolio data.");
		return $"DeepBook_{SuiClient.WalletAddress[2..10]}";
	}

	private void ValidatePortfolio(string portfolioName)
	{
		if (!SuiClient.IsWalletAvailable)
			throw new InvalidOperationException(
				"A Sui wallet address is required for portfolio data.");
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(GetPortfolioName()))
			throw new InvalidOperationException(
				$"Unknown DeepBook portfolio '{portfolioName}'.");
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		_transactionGate.Dispose();
		base.DisposeManaged();
	}
}
