namespace StockSharp.Chainflip;

public partial class ChainflipMessageAdapter
{
	private sealed class Level1Subscription
	{
		public ChainflipMarket Market { get; init; }
	}

	private sealed class DepthSubscription
	{
		public ChainflipMarket Market { get; init; }
		public int Depth { get; init; }
	}

	private sealed class TickSubscription
	{
		public ChainflipMarket Market { get; init; }
		public DateTime? To { get; init; }
		public int Maximum { get; init; }
		public int Delivered { get; set; }
	}

	private sealed class TrackedSwap
	{
		public long TransactionId { get; init; }
		public string TransactionHash { get; init; }
		public ChainflipMarket Market { get; init; }
		public ChainflipAsset SourceAsset { get; init; }
		public ChainflipAsset DestinationAsset { get; init; }
		public Sides Side { get; init; }
		public BigInteger SourceAmount { get; init; }
		public BigInteger ExpectedDestinationAmount { get; init; }
		public decimal RequestedVolume { get; init; }
		public decimal Volume { get; set; }
		public decimal Price { get; set; }
		public DateTime SubmittedTime { get; init; }
		public DateTime ExecutionTime { get; set; }
		public OrderStates State { get; set; }
		public bool IsTradeSent { get; set; }
		public ChainflipEvmReceipt Receipt { get; set; }
		public ChainflipSwapStatus Status { get; set; }
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

	private const int _maximumDeliveryKeys = 100_000;
	private readonly Lock _sync = new();
	private readonly Dictionary<string, ChainflipMarket> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, ChainflipMarket> _marketsByKey =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, Level1Subscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription>
		_depthSubscriptions = [];
	private readonly Dictionary<long, TickSubscription> _tickSubscriptions =
		[];
	private readonly HashSet<DeliveryKey> _seenMarketData = [];
	private readonly Queue<DeliveryKey> _marketDataDeliveryOrder = [];
	private readonly Dictionary<long, Level1Fingerprint>
		_level1Fingerprints = [];
	private readonly Dictionary<long, string> _depthFingerprints = [];
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderSubscription>
		_orderSubscriptions = [];
	private readonly Dictionary<string, TrackedSwap> _trackedSwaps =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, BalanceFingerprint>
		_balanceFingerprints = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, OrderFingerprint>
		_orderFingerprints = new(StringComparer.OrdinalIgnoreCase);
	private ChainflipStateClient _stateClient;
	private ChainflipHttpClient _httpClient;
	private ChainflipEvmClient _ethereumClient;
	private ChainflipEvmClient _arbitrumClient;
	private long _lastFillBlock;
	private DateTime _nextMarketPoll;
	private DateTime _nextPrivatePoll;

	/// <summary>Initializes the adapter.</summary>
	public ChainflipMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities ||
			dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Chainflip];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Chainflip) ||
			securityId.IsAssociated(BoardCodes.Chainflip);

	private ChainflipStateClient StateClient => _stateClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private ChainflipHttpClient HttpClient => _httpClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private bool HasWalletConfiguration =>
		!WalletAddress.IsEmpty() || !PrivateKey.IsEmpty();

	private void EnsureConnected()
	{
		if (_stateClient is null || _httpClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private ChainflipMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not Chainflip.");
		var code = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId))
			.Trim().ToUpperInvariant();
		using (_sync.EnterScope())
			return _markets.TryGetValue(code, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown Chainflip market '{code}'.");
	}

	private ChainflipEvmClient GetEvmClient(string chain)
		=> chain?.Trim().ToUpperInvariant() switch
		{
			"ETHEREUM" => _ethereumClient,
			"ARBITRUM" => _arbitrumClient,
			_ => null,
		};

	private ChainflipEvmClient EnsureTradingReady(ChainflipAsset source)
	{
		EnsureConnected();
		ArgumentNullException.ThrowIfNull(source);
		if (!source.IsEvm)
			throw new NotSupportedException(
				$"Chainflip order execution can sign Ethereum and Arbitrum " +
					$"sources; '{source.Chain}' requires an external wallet.");
		var client = GetEvmClient(source.Chain) ?? throw new
			InvalidOperationException(
				"Configure an EVM wallet address and private key for " +
					"Chainflip swaps.");
		if (!client.IsSigningAvailable)
			throw new InvalidOperationException(
				"An EVM private key is required for Chainflip vault swaps.");
		return client;
	}

	private string GetPortfolioName()
	{
		var client = _ethereumClient ?? _arbitrumClient;
		if (client?.IsWalletConfigured != true)
			throw new InvalidOperationException(
				"An EVM wallet address is required for Chainflip portfolio " +
					"data.");
		return $"Chainflip_{client.WalletAddress[2..10]}";
	}

	private void ValidatePortfolio(string portfolioName)
	{
		var expected = GetPortfolioName();
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(expected))
			throw new InvalidOperationException(
				$"Unknown Chainflip portfolio '{portfolioName}'.");
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		base.DisposeManaged();
	}
}
