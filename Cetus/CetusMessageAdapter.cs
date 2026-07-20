namespace StockSharp.Cetus;

public partial class CetusMessageAdapter
{
	private const int _maximumDeliveryKeys = 100_000;

	private sealed class Level1Subscription
	{
		public CetusMarket Market { get; init; }
	}

	private sealed class TickSubscription
	{
		public CetusMarket Market { get; init; }
		public DateTime? To { get; init; }
		public int Maximum { get; init; }
		public int Delivered { get; set; }
	}

	private sealed class TrackedSwap
	{
		public long TransactionId { get; init; }
		public string TransactionDigest { get; init; }
		public CetusMarket Market { get; init; }
		public Sides Side { get; init; }
		public decimal Volume { get; init; }
		public decimal Price { get; init; }
		public DateTime SubmittedTime { get; init; }
		public OrderStates State { get; init; }
		public CetusTransactionReceipt Receipt { get; init; }
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
	private readonly record struct Level1Fingerprint(decimal Bid,
		decimal Ask);
	private readonly record struct BalanceFingerprint(decimal Current,
		decimal Blocked);
	private readonly record struct OrderFingerprint(OrderStates State,
		bool IsTradeSent);
	private readonly record struct SwapExecution(decimal Price,
		decimal Volume, Sides Side);

	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _transactionGate = new(1, 1);
	private readonly Dictionary<string, CetusMarket> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, CetusMarket> _marketsByPool =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, CetusToken> _tokens =
		new(StringComparer.Ordinal);
	private readonly Dictionary<long, Level1Subscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, TickSubscription> _tickSubscriptions =
		[];
	private readonly HashSet<DeliveryKey> _seenTrades = [];
	private readonly Queue<DeliveryKey> _tradeDeliveryOrder = [];
	private readonly Dictionary<string, Level1Fingerprint>
		_level1Fingerprints = new(StringComparer.Ordinal);
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderSubscription>
		_orderSubscriptions = [];
	private readonly Dictionary<string, TrackedSwap> _trackedSwaps =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, BalanceFingerprint>
		_balanceFingerprints = new(StringComparer.Ordinal);
	private readonly Dictionary<string, OrderFingerprint>
		_orderFingerprints = new(StringComparer.Ordinal);
	private CetusApiClient _apiClient;
	private CetusSuiClient _suiClient;
	private CetusCheckpointClient _checkpointClient;
	private CetusSharedObject _globalConfig;
	private CetusSharedObject _clock;
	private string _chainId;
	private DateTime _nextMarketPoll;
	private DateTime _nextPrivatePoll;
	private DateTime _nextStreamReconnect;

	/// <summary>Initializes the adapter.</summary>
	public CetusMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.Level1);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities ||
			dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Cetus];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Cetus) ||
			securityId.IsAssociated(BoardCodes.Cetus);

	private CetusApiClient ApiClient => _apiClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private CetusSuiClient SuiClient => _suiClient ?? throw new
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
				"A Sui Ed25519 private key is required for Cetus swaps.");
	}

	private CetusMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not Cetus.");
		var code = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId))
			.NormalizeSecurityCode();
		using (_sync.EnterScope())
			return _markets.TryGetValue(code, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown Cetus market '{code}'.");
	}

	private string GetPortfolioName()
	{
		if (!SuiClient.IsWalletAvailable)
			throw new InvalidOperationException(
				"A Sui wallet address is required for portfolio data.");
		return $"Cetus_{SuiClient.WalletAddress[2..10]}";
	}

	private void ValidatePortfolio(string portfolioName)
	{
		if (!SuiClient.IsWalletAvailable)
			throw new InvalidOperationException(
				"A Sui wallet address is required for portfolio data.");
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(GetPortfolioName()))
			throw new InvalidOperationException(
				$"Unknown Cetus portfolio '{portfolioName}'.");
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		_transactionGate.Dispose();
		base.DisposeManaged();
	}
}
