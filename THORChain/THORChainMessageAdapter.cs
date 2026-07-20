namespace StockSharp.THORChain;

public partial class THORChainMessageAdapter
{
	private const int _maximumDeliveryKeys = 100_000;

	private sealed class Level1Subscription
	{
		public THORChainMarket Market { get; init; }
	}

	private sealed class TickSubscription
	{
		public THORChainMarket Market { get; init; }
		public DateTime? From { get; init; }
		public DateTime? To { get; init; }
		public DateTime LastTime { get; set; }
		public int Maximum { get; init; }
		public int Delivered { get; set; }
	}

	private sealed class CandleSubscription
	{
		public THORChainMarket Market { get; init; }
		public TimeSpan TimeFrame { get; init; }
		public DateTime? To { get; init; }
		public DateTime LastTime { get; set; }
		public int Maximum { get; init; }
		public int Delivered { get; set; }
	}

	private sealed class TrackedSwap
	{
		public long TransactionId { get; init; }
		public string TransactionHash { get; init; }
		public THORChainMarket Market { get; init; }
		public decimal Volume { get; init; }
		public decimal Price { get; set; }
		public decimal QuoteVolume { get; set; }
		public decimal? Commission { get; init; }
		public string DestinationAddress { get; init; }
		public string Memo { get; init; }
		public DateTime SubmittedTime { get; init; }
		public OrderStates State { get; set; }
		public bool IsTradeSent { get; set; }
		public string FailureReason { get; set; }
		public THORChainAction Action { get; set; }
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
		decimal Ask, decimal Volume24Hours);
	private readonly record struct CandleFingerprint(decimal Open,
		decimal High, decimal Low, decimal Close, decimal Volume,
		int TradeCount);
	private readonly record struct BalanceFingerprint(decimal Current,
		decimal Blocked);
	private readonly record struct OrderFingerprint(OrderStates State,
		bool IsTradeSent);

	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _transactionGate = new(1, 1);
	private readonly Dictionary<string, THORChainMarket> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, THORChainMarket> _marketsByAsset =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, Level1Subscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, TickSubscription> _tickSubscriptions =
		[];
	private readonly Dictionary<long, CandleSubscription>
		_candleSubscriptions = [];
	private readonly HashSet<DeliveryKey> _seenTrades = [];
	private readonly Queue<DeliveryKey> _tradeDeliveryOrder = [];
	private readonly Dictionary<string, Level1Fingerprint>
		_level1Fingerprints = new(StringComparer.Ordinal);
	private readonly Dictionary<string, CandleFingerprint>
		_candleFingerprints = new(StringComparer.Ordinal);
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderSubscription>
		_orderSubscriptions = [];
	private readonly Dictionary<string, TrackedSwap> _trackedSwaps =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, BalanceFingerprint>
		_balanceFingerprints = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, OrderFingerprint>
		_orderFingerprints = new(StringComparer.OrdinalIgnoreCase);
	private THORChainApiClient _apiClient;
	private THORChainSigner _signer;
	private string _chainId;
	private DateTime _nextMarketPoll;
	private DateTime _nextPrivatePoll;

	/// <summary>Initializes the adapter.</summary>
	public THORChainMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription)
		=> true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.THORChain];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.THORChain) ||
			securityId.IsAssociated(BoardCodes.THORChain);

	private THORChainApiClient ApiClient => _apiClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private THORChainSigner Signer => _signer ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_apiClient is null || _signer is null)
			throw new InvalidOperationException(
				LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureTradingReady()
	{
		EnsureConnected();
		if (!Signer.IsSigningAvailable)
			throw new InvalidOperationException(
				"A THORChain private key is required for RUNE swaps.");
	}

	private THORChainMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not THORChain.");
		var code = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId))
			.Trim().ToUpperInvariant();
		using (_sync.EnterScope())
			return _markets.TryGetValue(code, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown THORChain market '{code}'.");
	}

	private string GetPortfolioName()
		=> $"THORChain_{Signer.WalletAddress[..8]}";

	private void ValidatePortfolio(string portfolioName)
	{
		if (!Signer.IsWalletAvailable)
			throw new InvalidOperationException(
				"A THORChain wallet address is required for portfolio data.");
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(GetPortfolioName()))
			throw new InvalidOperationException(
				$"Unknown THORChain portfolio '{portfolioName}'.");
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		_transactionGate.Dispose();
		base.DisposeManaged();
	}
}
