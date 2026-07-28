namespace StockSharp.KyberSwap;

public partial class KyberSwapMessageAdapter
{
	private sealed class Level1Subscription
	{
		public KyberSwapMarket Market { get; init; }
	}

	private sealed class TrackedSwap
	{
		public long TransactionId { get; init; }
		public string TransactionHash { get; init; }
		public KyberSwapMarket Market { get; init; }
		public Sides Side { get; init; }
		public KyberSwapToken SourceToken { get; init; }
		public KyberSwapToken DestinationToken { get; init; }
		public BigInteger SourceAmount { get; init; }
		public decimal RequestedVolume { get; init; }
		public decimal Volume { get; set; }
		public decimal Price { get; set; }
		public DateTime SubmittedTime { get; init; }
		public DateTime ExecutionTime { get; set; }
		public OrderStates State { get; set; }
		public bool IsTradeSent { get; set; }
		public KyberSwapRpcReceipt Receipt { get; set; }
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

	private readonly record struct BalanceFingerprint(decimal Current,
		decimal Blocked);
	private readonly record struct OrderFingerprint(OrderStates State,
		bool IsTradeSent);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, KyberSwapMarket> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, KyberSwapToken> _tokens =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, Level1Subscription>
		_level1Subscriptions = [];
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderSubscription> _orderSubscriptions =
		[];
	private readonly Dictionary<string, TrackedSwap> _trackedSwaps =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, BalanceFingerprint>
		_balanceFingerprints = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, OrderFingerprint>
		_orderFingerprints = new(StringComparer.OrdinalIgnoreCase);
	private KyberSwapRpcClient _rpcClient;
	private KyberSwapHttpClient _httpClient;
	private DateTime _nextMarketPoll;
	private DateTime _nextPrivatePoll;

	/// <summary>Initializes the adapter.</summary>
	public KyberSwapMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities ||
			dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.KyberSwap];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.KyberSwap) ||
			securityId.IsAssociated(BoardCodes.KyberSwap);

	private KyberSwapRpcClient RpcClient => _rpcClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private KyberSwapHttpClient HttpClient => _httpClient ?? throw new
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
				"An EVM private key is required for KyberSwap swaps.");
	}

	private KyberSwapMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not KyberSwap.");
		var code = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId)).Trim().ToUpperInvariant();
		using (_sync.EnterScope())
			return _markets.TryGetValue(code, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown KyberSwap market '{code}'.");
	}

	private string GetPortfolioName()
		=> $"KyberSwap_{Chain}_{RpcClient.WalletAddress[2..10]}";

	private void ValidatePortfolio(string portfolioName)
	{
		if (!RpcClient.IsWalletConfigured)
			throw new InvalidOperationException(
				"An EVM wallet address is required for portfolio data.");
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(GetPortfolioName()))
			throw new InvalidOperationException(
				$"Unknown KyberSwap portfolio '{portfolioName}'.");
	}

	private async ValueTask<KyberSwapQuote> GetQuoteAsync(KyberSwapToken source,
		KyberSwapToken destination, BigInteger amount,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(destination);
		if (amount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		var response = await HttpClient.GetRouteAsync(source.Address,
			destination.Address, amount,
			RpcClient.IsWalletConfigured ? RpcClient.WalletAddress : null,
			cancellationToken);
		var output = ValidateRoute(response, source, destination, amount);
		return new()
		{
			InputAmount = amount,
			OutputAmount = output,
		};
	}

	internal static BigInteger ValidateRoute(KyberSwapRouteData response,
		KyberSwapToken source, KyberSwapToken destination,
		BigInteger requestedAmount)
	{
		if (response?.RouteSummary is not { } summary)
			throw new InvalidDataException(
				"KyberSwap API returned no route summary.");
		if (!ReadSummaryString(summary, "tokenIn").NormalizeAddress()
				.EqualsIgnoreCase(
				source.Address) ||
			!ReadSummaryString(summary, "tokenOut").NormalizeAddress()
				.EqualsIgnoreCase(
				destination.Address) ||
			ReadSummaryString(summary, "amountIn").ParseInteger() !=
				requestedAmount)
			throw new InvalidDataException(
				"KyberSwap route does not match the requested token pair or " +
					"amount.");
		var output = ReadSummaryString(summary, "amountOut").ParseInteger();
		var gas = ReadSummaryString(summary, "gas").ParseInteger();
		if (output <= 0 || gas <= 0)
			throw new InvalidDataException(
				"KyberSwap returned a non-positive output amount or gas " +
					"estimate.");
		_ = response.RouterAddress.NormalizeAddress();
		if (ReadSummaryString(summary, "routeID").IsEmpty() ||
			summary["route"] is not JArray { Count: > 0 })
			throw new InvalidDataException(
				"KyberSwap route has no identity or liquidity path.");
		return output;
	}

	internal static string ReadSummaryString(JObject summary, string name)
	{
		ArgumentNullException.ThrowIfNull(summary);
		var value = summary.Value<string>(
			name.ThrowIfEmpty(nameof(name)))?.Trim();
		return value.IsEmpty()
			? throw new InvalidDataException(
				$"KyberSwap route summary has no '{name}' value.")
			: value;
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		base.DisposeManaged();
	}
}
