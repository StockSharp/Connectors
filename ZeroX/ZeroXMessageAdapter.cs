namespace StockSharp.ZeroX;

public partial class ZeroXMessageAdapter
{
	private sealed class Level1Subscription
	{
		public ZeroXMarket Market { get; init; }
	}

	private sealed class TrackedSwap
	{
		public long TransactionId { get; init; }
		public string TransactionHash { get; init; }
		public ZeroXMarket Market { get; init; }
		public Sides Side { get; init; }
		public ZeroXToken SourceToken { get; init; }
		public ZeroXToken DestinationToken { get; init; }
		public BigInteger SourceAmount { get; init; }
		public decimal RequestedVolume { get; init; }
		public decimal Volume { get; set; }
		public decimal Price { get; set; }
		public DateTime SubmittedTime { get; init; }
		public DateTime ExecutionTime { get; set; }
		public OrderStates State { get; set; }
		public bool IsTradeSent { get; set; }
		public ZeroXRpcReceipt Receipt { get; set; }
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
	private readonly Dictionary<string, ZeroXMarket> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, ZeroXToken> _tokens =
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
	private ZeroXRpcClient _rpcClient;
	private ZeroXHttpClient _httpClient;
	private DateTime _nextMarketPoll;
	private DateTime _nextPrivatePoll;

	/// <summary>Initializes the adapter.</summary>
	public ZeroXMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override string[] AssociatedBoards => [BoardCodes.ZeroX];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.ZeroX) ||
			securityId.IsAssociated(BoardCodes.ZeroX);

	private ZeroXRpcClient RpcClient => _rpcClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private ZeroXHttpClient HttpClient => _httpClient ?? throw new
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
				"An EVM private key is required for 0x swaps.");
	}

	private ZeroXMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not 0x.");
		var code = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId)).Trim().ToUpperInvariant();
		using (_sync.EnterScope())
			return _markets.TryGetValue(code, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown 0x market '{code}'.");
	}

	private string GetPortfolioName()
		=> $"0x_{Chain}_{RpcClient.WalletAddress[2..10]}";

	private void ValidatePortfolio(string portfolioName)
	{
		if (!RpcClient.IsWalletConfigured)
			throw new InvalidOperationException(
				"An EVM wallet address is required for portfolio data.");
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(GetPortfolioName()))
			throw new InvalidOperationException(
				$"Unknown 0x portfolio '{portfolioName}'.");
	}

	private async ValueTask<ZeroXQuote> GetQuoteAsync(ZeroXToken source,
		ZeroXToken destination, BigInteger amount,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(destination);
		if (amount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		var response = await HttpClient.GetPriceAsync(source.Address,
			destination.Address, amount,
			RpcClient.IsWalletConfigured ? RpcClient.WalletAddress : null,
			cancellationToken);
		ValidateQuote(response, source, destination, amount, false);
		var output = response.BuyAmount.ParseInteger();
		if (output <= 0)
			throw new InvalidDataException(
				"0x API returned a non-positive quote amount.");
		return new()
		{
			InputAmount = amount,
			OutputAmount = output,
		};
	}

	private static void ValidateQuote(ZeroXQuoteResponse response,
		ZeroXToken source, ZeroXToken destination, BigInteger requestedAmount,
		bool requireTransaction)
	{
		if (response is null || !response.IsLiquidityAvailable)
			throw new InvalidDataException(
				"0x API returned no liquidity for the requested swap.");
		if (!response.SellToken.NormalizeAddress().EqualsIgnoreCase(
				source.Address) ||
			!response.BuyToken.NormalizeAddress().EqualsIgnoreCase(
				destination.Address) ||
			response.SellAmount.ParseInteger() != requestedAmount ||
			response.BuyAmount.ParseInteger() <= 0)
			throw new InvalidDataException(
				"0x API quote does not match the requested token pair or " +
				"amount.");
		if (response.Issues?.InvalidSources?.Length > 0)
			throw new InvalidDataException(
				"0x API reported invalid excluded liquidity sources.");
		if (requireTransaction && response.Transaction is null)
			throw new InvalidDataException(
				"0x API returned no swap transaction.");
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		base.DisposeManaged();
	}
}
