namespace StockSharp.Osmosis;

public partial class OsmosisMessageAdapter
{
	private const int _maximumDeliveryKeys = 100_000;

	private sealed class Level1Subscription
	{
		public OsmosisMarket Market { get; init; }
	}

	private sealed class TickSubscription
	{
		public OsmosisMarket Market { get; init; }
		public DateTime? To { get; init; }
		public int Maximum { get; init; }
		public int Delivered { get; set; }
	}

	private sealed class TrackedSwap
	{
		public long TransactionId { get; init; }
		public string TransactionHash { get; init; }
		public OsmosisMarket Market { get; init; }
		public Sides Side { get; init; }
		public decimal Volume { get; init; }
		public decimal Price { get; set; }
		public decimal QuoteVolume { get; set; }
		public decimal Commission { get; init; }
		public DateTime SubmittedTime { get; init; }
		public DateTime? CompletionTime { get; set; }
		public OrderStates State { get; set; }
		public bool IsTradeSent { get; set; }
		public string FailureReason { get; set; }
	}

	private sealed class OrderSubscription
	{
		public string TransactionHash { get; init; }
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

	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _transactionGate = new(1, 1);
	private readonly Dictionary<string, OsmosisMarket> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, OsmosisMarket> _marketsByPair =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, OsmosisToken> _tokens =
		new(StringComparer.Ordinal);
	private readonly Dictionary<long, Level1Subscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, TickSubscription> _tickSubscriptions = [];
	private readonly HashSet<DeliveryKey> _seenTrades = [];
	private readonly Queue<DeliveryKey> _tradeDeliveryOrder = [];
	private readonly Dictionary<string, Level1Fingerprint>
		_level1Fingerprints = new(StringComparer.Ordinal);
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderSubscription>
		_orderSubscriptions = [];
	private readonly Dictionary<string, TrackedSwap> _trackedSwaps =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, BalanceFingerprint>
		_balanceFingerprints = new(StringComparer.Ordinal);
	private readonly Dictionary<string, OrderFingerprint>
		_orderFingerprints = new(StringComparer.Ordinal);
	private readonly Dictionary<long, DateTime> _blockTimes = [];
	private readonly Queue<long> _blockTimeOrder = [];
	private OsmosisApiClient _apiClient;
	private OsmosisSocketClient _socketClient;
	private OsmosisSigner _signer;
	private string _chainId;
	private DateTime _nextMarketPoll;
	private DateTime _nextPrivatePoll;
	private DateTime _nextSocketReconnect;

	/// <summary>Initializes the adapter.</summary>
	public OsmosisMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override string[] AssociatedBoards => [BoardCodes.Osmosis];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Osmosis) ||
			securityId.IsAssociated(BoardCodes.Osmosis);

	private OsmosisApiClient ApiClient => _apiClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private OsmosisSigner Signer => _signer ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_apiClient is null || _signer is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureTradingReady()
	{
		EnsureConnected();
		if (!Signer.IsSigningAvailable)
			throw new InvalidOperationException(
				"An Osmosis private key is required for swaps.");
	}

	private OsmosisMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not Osmosis.");
		var code = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId))
			.Trim().ToUpperInvariant();
		using (_sync.EnterScope())
			return _markets.TryGetValue(code, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown Osmosis market '{code}'.");
	}

	private string GetPortfolioName()
	{
		if (!Signer.IsWalletAvailable)
			throw new InvalidOperationException(
				"An Osmosis wallet address is required for portfolio data.");
		return $"Osmosis_{Signer.WalletAddress[..8]}";
	}

	private void ValidatePortfolio(string portfolioName)
	{
		if (!Signer.IsWalletAvailable)
			throw new InvalidOperationException(
				"An Osmosis wallet address is required for portfolio data.");
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(GetPortfolioName()))
			throw new InvalidOperationException(
				$"Unknown Osmosis portfolio '{portfolioName}'.");
	}

	private static string PairKey(string first, string second)
		=> first + "\n" + second;

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		_transactionGate.Dispose();
		base.DisposeManaged();
	}
}
