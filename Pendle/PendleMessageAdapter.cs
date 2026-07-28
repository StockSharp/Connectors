namespace StockSharp.Pendle;

public partial class PendleMessageAdapter
{
	private sealed class Level1Subscription
	{
		public PendleSecurity Security { get; init; }
	}

	private sealed class CandleSubscription
	{
		public PendleSecurity Security { get; init; }
		public TimeSpan TimeFrame { get; init; }
		public DateTime? To { get; init; }
		public int Maximum { get; init; }
		public int Delivered { get; set; }
	}

	private sealed class TrackedSwap
	{
		public long TransactionId { get; init; }
		public string TransactionHash { get; init; }
		public PendleSecurity Security { get; init; }
		public Sides Side { get; init; }
		public PendleToken SourceToken { get; init; }
		public PendleToken DestinationToken { get; init; }
		public BigInteger SourceAmount { get; init; }
		public BigInteger ExpectedDestinationAmount { get; init; }
		public decimal RequestedVolume { get; init; }
		public decimal Volume { get; set; }
		public decimal Price { get; set; }
		public DateTime SubmittedTime { get; init; }
		public DateTime ExecutionTime { get; set; }
		public OrderStates State { get; set; }
		public bool IsTradeSent { get; set; }
		public PendleRpcReceipt Receipt { get; set; }
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
	private readonly record struct Level1Fingerprint(decimal Bid, decimal Ask,
		decimal ImpliedApy);
	private readonly record struct BalanceFingerprint(decimal Current,
		decimal Blocked);
	private readonly record struct OrderFingerprint(OrderStates State,
		bool IsTradeSent);

	private const int _maximumDeliveryKeys = 20000;
	private readonly Lock _sync = new();
	private readonly Dictionary<string, PendleMarket> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, PendleSecurity> _securities =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, PendleToken> _tokens =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, Level1Subscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, CandleSubscription>
		_candleSubscriptions = [];
	private readonly HashSet<DeliveryKey> _seenMarketData = [];
	private readonly Queue<DeliveryKey> _marketDataDeliveryOrder = [];
	private readonly Dictionary<long, Level1Fingerprint>
		_level1Fingerprints = [];
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderSubscription> _orderSubscriptions =
		[];
	private readonly Dictionary<string, TrackedSwap> _trackedSwaps =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, BalanceFingerprint>
		_balanceFingerprints = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, OrderFingerprint>
		_orderFingerprints = new(StringComparer.OrdinalIgnoreCase);
	private PendleRpcClient _rpcClient;
	private PendleHttpClient _httpClient;
	private DateTime _nextMarketPoll;
	private DateTime _nextPrivatePoll;

	/// <summary>Supported Pendle historical intervals.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
	[
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
	];

	/// <summary>Initializes the adapter.</summary>
	public PendleMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
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
	public override string[] AssociatedBoards => [BoardCodes.Pendle];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Pendle) ||
			securityId.IsAssociated(BoardCodes.Pendle);

	private PendleRpcClient RpcClient => _rpcClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private PendleHttpClient HttpClient => _httpClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_rpcClient is null || _httpClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureTradingReady()
	{
		EnsureConnected();
		if (!RpcClient.IsSigningAvailable)
			throw new InvalidOperationException(
				"An EVM private key is required for Pendle swaps.");
	}

	private PendleSecurity GetSecurity(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not Pendle.");
		var code = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId)).Trim().ToUpperInvariant();
		using (_sync.EnterScope())
			return _securities.TryGetValue(code, out var security)
				? security
				: throw new InvalidOperationException(
					$"Unknown Pendle security '{code}'.");
	}

	private string GetPortfolioName()
		=> $"Pendle_{Chain}_{RpcClient.WalletAddress[2..10]}";

	private void ValidatePortfolio(string portfolioName)
	{
		if (!RpcClient.IsWalletConfigured)
			throw new InvalidOperationException(
				"An EVM wallet address is required for Pendle portfolio data.");
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(GetPortfolioName()))
			throw new InvalidOperationException(
				$"Unknown Pendle portfolio '{portfolioName}'.");
	}

	internal static PendleLevel1 ValidatePrices(PendleSecurity security,
		PendlePricesResponse response)
	{
		ArgumentNullException.ThrowIfNull(security);
		ArgumentNullException.ThrowIfNull(response);
		if (!response.UnderlyingToken.NormalizeAddress().EqualsIgnoreCase(
			security.Market.UnderlyingToken.Address))
			throw new InvalidDataException(
				"Pendle quote uses an unexpected underlying token.");
		var toUnderlying = security.Kind == PendleAssetKinds.Principal
			? response.PrincipalToUnderlyingRate
			: response.YieldToUnderlyingRate;
		var fromUnderlying = security.Kind == PendleAssetKinds.Principal
			? response.UnderlyingToPrincipalRate
			: response.UnderlyingToYieldRate;
		if (toUnderlying is not > 0 || fromUnderlying is not > 0)
			throw new InvalidDataException(
				"Pendle returned no executable PT/YT quote.");
		var bid = toUnderlying.Value;
		var ask = 1m / fromUnderlying.Value;
		if (bid <= 0 || ask <= 0)
			throw new InvalidDataException(
				"Pendle returned a non-positive PT/YT quote.");
		return new()
		{
			Bid = bid,
			Ask = ask,
			ImpliedApy = response.ImpliedApy,
		};
	}

	private async ValueTask<PendleLevel1> GetLevel1Async(
		PendleSecurity security, CancellationToken cancellationToken)
		=> ValidatePrices(security, await HttpClient.GetPricesAsync(
			security.Market.Address, cancellationToken));

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		base.DisposeManaged();
	}
}
