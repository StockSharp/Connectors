namespace StockSharp.Orca;

public partial class OrcaMessageAdapter
{
	private const int _maximumDeliveryKeys = 100_000;

	private sealed class Level1Subscription
	{
		public OrcaMarket Market { get; init; }
	}

	private sealed class TickSubscription
	{
		public OrcaMarket Market { get; init; }
		public DateTime? From { get; init; }
		public DateTime? To { get; init; }
		public DateTime LastTime { get; set; }
		public int Maximum { get; init; }
		public int Delivered { get; set; }
	}

	private sealed class CandleSubscription
	{
		public OrcaMarket Market { get; init; }
		public TimeSpan TimeFrame { get; init; }
		public DateTime? To { get; init; }
		public DateTime LastTime { get; set; }
		public int Maximum { get; init; }
		public int Delivered { get; set; }
	}

	private sealed class TrackedSwap
	{
		public long TransactionId { get; init; }
		public string Signature { get; init; }
		public OrcaMarket Market { get; init; }
		public Sides Side { get; init; }
		public decimal Volume { get; set; }
		public decimal Price { get; set; }
		public DateTime SubmittedTime { get; init; }
		public OrderStates State { get; set; }
		public bool IsTradeSent { get; set; }
		public OrcaTransactionReceipt Receipt { get; set; }
	}

	private sealed class OrderSubscription
	{
		public string Signature { get; init; }
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
	private readonly record struct CandleFingerprint(decimal Open,
		decimal High, decimal Low, decimal Close, decimal Volume,
		int TradeCount);
	private readonly record struct BalanceFingerprint(decimal Current,
		decimal Blocked);
	private readonly record struct OrderFingerprint(OrderStates State,
		bool IsTradeSent);
	private readonly record struct SwapExecution(decimal Price,
		decimal Volume);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, OrcaMarket> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, OrcaMarket> _marketsByPool =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, OrcaToken> _tokens =
		new(StringComparer.Ordinal);
	private readonly Dictionary<long, Level1Subscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, TickSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription>
		_candleSubscriptions = [];
	private readonly HashSet<DeliveryKey> _seenTrades = [];
	private readonly Queue<DeliveryKey> _tradeDeliveryOrder = [];
	private readonly Dictionary<string, CandleFingerprint>
		_candleFingerprints = new(StringComparer.Ordinal);
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderSubscription>
		_orderSubscriptions = [];
	private readonly Dictionary<string, TrackedSwap> _trackedSwaps =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, BalanceFingerprint>
		_balanceFingerprints = new(StringComparer.Ordinal);
	private readonly Dictionary<string, OrderFingerprint>
		_orderFingerprints = new(StringComparer.Ordinal);
	private OrcaApiClient _apiClient;
	private OrcaRpcClient _rpcClient;
	private OrcaSocketClient _socketClient;
	private DateTime _nextMarketPoll;
	private DateTime _nextPrivatePoll;
	private DateTime _nextSocketReconnect;

	/// <summary>Initializes the adapter.</summary>
	public OrcaMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override string[] AssociatedBoards => [BoardCodes.Orca];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Orca) ||
			securityId.IsAssociated(BoardCodes.Orca);

	private OrcaRpcClient RpcClient => _rpcClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_rpcClient is null)
			throw new InvalidOperationException(
				LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureTradingReady()
	{
		EnsureConnected();
		if (!RpcClient.IsSigningAvailable)
			throw new InvalidOperationException(
				"A Solana private key is required for Orca swaps.");
	}

	private OrcaMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not Orca.");
		var code = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId))
			.Trim().ToUpperInvariant();
		using (_sync.EnterScope())
			return _markets.TryGetValue(code, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown Orca market '{code}'.");
	}

	private string GetPortfolioName()
	{
		if (!RpcClient.IsWalletAvailable)
			throw new InvalidOperationException(
				"A Solana wallet address is required for portfolio data.");
		return $"Orca_{Cluster}_{RpcClient.WalletAddress[..8]}";
	}

	private void ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(GetPortfolioName()))
			throw new InvalidOperationException(
				$"Unknown Orca portfolio '{portfolioName}'.");
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		base.DisposeManaged();
	}
}
