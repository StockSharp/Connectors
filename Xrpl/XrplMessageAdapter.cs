namespace StockSharp.Xrpl;

public partial class XrplMessageAdapter
{
	private sealed class BookSubscription
	{
		public XrplMarket Market { get; init; }
		public int Depth { get; init; }
	}

	private sealed class Level1Subscription
	{
		public XrplMarket Market { get; init; }
	}

	private sealed class TickSubscription
	{
		public XrplMarket Market { get; init; }
		public DateTime? From { get; init; }
		public DateTime? To { get; init; }
		public int Maximum { get; init; }
		public int Delivered { get; set; }
	}

	private sealed class CandleSubscription
	{
		public XrplMarket Market { get; init; }
		public TimeSpan TimeFrame { get; init; }
		public DateTime? From { get; init; }
		public DateTime? To { get; init; }
		public int Maximum { get; init; }
		public int Delivered { get; set; }
		public XrplCandle Current { get; set; }
	}

	private sealed class TrackedOrder
	{
		public long TransactionId { get; init; }
		public string Hash { get; set; }
		public string CancelHash { get; set; }
		public uint OfferSequence { get; init; }
		public XrplMarket Market { get; init; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public TimeInForce? TimeInForce { get; init; }
		public decimal Price { get; init; }
		public decimal Volume { get; init; }
		public decimal Balance { get; set; }
		public decimal Commission { get; set; }
		public DateTime Time { get; init; }
		public DateTime UpdateTime { get; set; }
		public OrderStates State { get; set; }
		public bool IsTradeSent { get; set; }
		public string FailureReason { get; set; }
	}

	private sealed class OrderSubscription
	{
		public string Hash { get; init; }
		public SecurityId SecurityId { get; init; }
		public Sides? Side { get; init; }
		public OrderStates[] States { get; init; }
		public DateTime? From { get; init; }
		public DateTime? To { get; init; }
		public int Skip { get; init; }
		public int Maximum { get; init; }
	}

	private readonly record struct Level1Fingerprint(decimal Bid,
		decimal Ask, uint Ledger);
	private readonly record struct BookFingerprint(uint Ledger,
		decimal BestBid, decimal BestAsk);
	private readonly record struct BalanceFingerprint(decimal Current);
	private readonly record struct DeliveryKey(long SubscriptionId,
		string Identity);
	private const int _maximumDeliveryKeys = 100_000;

	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _transactionGate = new(1, 1);
	private readonly Dictionary<string, XrplMarket> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, BookSubscription>
		_bookSubscriptions = [];
	private readonly Dictionary<long, Level1Subscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, TickSubscription>
		_tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription>
		_candleSubscriptions = [];
	private readonly Dictionary<long, BookFingerprint> _bookFingerprints = [];
	private readonly Dictionary<long, Level1Fingerprint>
		_level1Fingerprints = [];
	private readonly HashSet<DeliveryKey> _seenMarketData = [];
	private readonly Queue<DeliveryKey> _deliveryOrder = [];
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<string, BalanceFingerprint>
		_balanceFingerprints = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, OrderSubscription>
		_orderSubscriptions = [];
	private readonly Dictionary<string, TrackedOrder> _trackedOrders =
		new(StringComparer.OrdinalIgnoreCase);
	private XrplRpcClient _rpcClient;
	private XrplSocketClient _socketClient;
	private XrplSigner _signer;
	private uint _latestLedger;
	private DateTime _nextMarketPoll;
	private DateTime _nextPrivatePoll;
	private DateTime _nextSocketReconnect;

	/// <summary>Initializes the adapter.</summary>
	public XrplMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
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
	public override string[] AssociatedBoards => [BoardCodes.Xrpl];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Xrpl) ||
			securityId.IsAssociated(BoardCodes.Xrpl);

	private XrplRpcClient RpcClient => _rpcClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private XrplSigner Signer => _signer ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_rpcClient is null || _signer is null)
			throw new InvalidOperationException(
				LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureTradingReady()
	{
		EnsureConnected();
		if (!Signer.IsSigningAvailable)
			throw new InvalidOperationException(
				"An XRPL family seed is required for trading.");
	}

	private XrplMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not XRPL.");
		var code = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId)).Trim();
		using (_sync.EnterScope())
			return _markets.TryGetValue(code, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown XRPL market '{code}'.");
	}

	private string GetPortfolioName()
	{
		if (!Signer.IsWalletAvailable)
			throw new InvalidOperationException(
				"An XRPL account is required for portfolio data.");
		return $"XRPL_{Signer.WalletAddress[..8]}";
	}

	private void ValidatePortfolio(string portfolioName)
	{
		var expected = GetPortfolioName();
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(expected))
			throw new InvalidOperationException(
				$"Unknown XRPL portfolio '{portfolioName}'.");
	}

	private static int GetSubscriptionMaximum(long? count)
		=> count is null
			? int.MaxValue
			: count.Value.Min(int.MaxValue).Max(0).To<int>();

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		_transactionGate.Dispose();
		base.DisposeManaged();
	}
}
