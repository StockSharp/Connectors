namespace StockSharp.StonFi;

public partial class StonFiMessageAdapter
{
	private sealed class Level1Subscription
	{
		public StonMarket Market { get; init; }
	}

	private sealed class TickSubscription
	{
		public StonMarket Market { get; init; }
		public DateTime? From { get; init; }
		public DateTime? To { get; init; }
		public int Maximum { get; init; }
		public int Delivered { get; set; }
	}

	private sealed class CandleSubscription
	{
		public StonMarket Market { get; init; }
		public TimeSpan TimeFrame { get; init; }
		public DateTime? From { get; init; }
		public DateTime? To { get; init; }
		public int Maximum { get; init; }
		public int Delivered { get; set; }
		public StonCandle CurrentCandle { get; set; }
	}

	private sealed class TrackedSwap
	{
		public long TransactionId { get; init; }
		public ulong QueryId { get; init; }
		public string ExternalMessageHash { get; init; }
		public string TransactionHash { get; set; }
		public StonMarket Market { get; init; }
		public StonSwapSimulation Quote { get; init; }
		public Sides Side { get; init; }
		public decimal RequestedVolume { get; init; }
		public decimal Volume { get; init; }
		public decimal Price { get; init; }
		public decimal? Commission { get; init; }
		public string CommissionCurrency { get; init; }
		public DateTime SubmittedTime { get; init; }
		public DateTime ExecutionTime { get; set; }
		public OrderStates State { get; set; }
		public bool IsTradeSent { get; set; }
	}

	private sealed class OrderSubscription
	{
		public string TransactionHash { get; init; }
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
	private readonly record struct Level1Fingerprint(decimal Bid,
		decimal Ask, decimal Last);
	private readonly record struct BalanceFingerprint(decimal Current);
	private readonly record struct OrderFingerprint(OrderStates State,
		bool IsTradeSent);
	private const int _maximumDeliveryKeys = 100_000;

	private readonly Lock _sync = new();
	private readonly Dictionary<string, StonMarket> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, StonMarket> _marketsByPool =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, Level1Subscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, TickSubscription>
		_tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription>
		_candleSubscriptions = [];
	private readonly HashSet<DeliveryKey> _seenMarketData = [];
	private readonly Queue<DeliveryKey> _deliveryOrder = [];
	private readonly Dictionary<long, Level1Fingerprint>
		_level1Fingerprints = [];
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<string, BalanceFingerprint>
		_balanceFingerprints = new(StringComparer.Ordinal);
	private readonly Dictionary<long, OrderSubscription>
		_orderSubscriptions = [];
	private readonly Dictionary<string, OrderFingerprint>
		_orderFingerprints = new(StringComparer.Ordinal);
	private readonly Dictionary<string, TrackedSwap> _trackedSwaps =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, StonTrade> _lastTrades =
		new(StringComparer.OrdinalIgnoreCase);
	private StonFiRestClient _restClient;
	private StonTonClient _tonClient;
	private int _lastEventBlock;
	private DateTime _nextMarketPoll;
	private DateTime _nextPrivatePoll;

	/// <summary>Initializes the adapter.</summary>
	public StonFiMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.Level1);
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
	public override string[] AssociatedBoards => [BoardCodes.StonFi];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.StonFi) ||
			securityId.IsAssociated(BoardCodes.StonFi);

	private StonFiRestClient RestClient => _restClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private StonTonClient TonClient => _tonClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_restClient is null || _tonClient is null)
			throw new InvalidOperationException(
				LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureTradingReady()
	{
		EnsureConnected();
		if (!TonClient.IsSigningAvailable)
			throw new InvalidOperationException(
				"A 24-word TON mnemonic is required for STON.fi trading.");
	}

	private StonMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not STON.fi.");
		var code = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId)).Trim();
		using (_sync.EnterScope())
			return _markets.TryGetValue(code, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown STON.fi market '{code}'.");
	}

	private string GetPortfolioName()
	{
		if (!TonClient.IsWalletConfigured)
			throw new InvalidOperationException(
				"A STON.fi wallet address is required for portfolio data.");
		return $"StonFi_{TonClient.WalletAddress[2..10]}";
	}

	private void ValidatePortfolio(string portfolioName)
	{
		var expected = GetPortfolioName();
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(expected))
			throw new InvalidOperationException(
				$"Unknown STON.fi portfolio '{portfolioName}'.");
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		base.DisposeManaged();
	}
}
