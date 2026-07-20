namespace StockSharp.OneInch;

public partial class OneInchMessageAdapter
{
	private sealed class Level1Subscription
	{
		public OneInchMarket Market { get; init; }
	}

	private sealed class TrackedSwap
	{
		public long TransactionId { get; init; }
		public string TransactionHash { get; init; }
		public OneInchMarket Market { get; init; }
		public Sides Side { get; init; }
		public OneInchToken SourceToken { get; init; }
		public OneInchToken DestinationToken { get; init; }
		public BigInteger SourceAmount { get; init; }
		public decimal RequestedVolume { get; init; }
		public decimal Volume { get; set; }
		public decimal Price { get; set; }
		public DateTime SubmittedTime { get; init; }
		public DateTime ExecutionTime { get; set; }
		public OrderStates State { get; set; }
		public bool IsTradeSent { get; set; }
		public OneInchRpcReceipt Receipt { get; set; }
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
	private readonly Dictionary<string, OneInchMarket> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, OneInchToken> _tokens =
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
	private OneInchRpcClient _rpcClient;
	private OneInchHttpClient _httpClient;
	private string _spender;
	private DateTime _nextMarketPoll;
	private DateTime _nextPrivatePoll;

	/// <summary>Initializes the adapter.</summary>
	public OneInchMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override string[] AssociatedBoards => [BoardCodes.OneInch];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.OneInch) ||
			securityId.IsAssociated(BoardCodes.OneInch);

	private OneInchRpcClient RpcClient => _rpcClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private OneInchHttpClient HttpClient => _httpClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private string Spender => _spender ?? throw new InvalidOperationException(
		LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_rpcClient is null || _httpClient is null || _spender.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureTradingReady()
	{
		EnsureConnected();
		if (!RpcClient.IsSigningAvailable)
			throw new InvalidOperationException(
				"An EVM private key is required for 1inch swaps.");
	}

	private OneInchMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not 1inch.");
		var code = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId)).Trim().ToUpperInvariant();
		using (_sync.EnterScope())
			return _markets.TryGetValue(code, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown 1inch market '{code}'.");
	}

	private string GetPortfolioName()
		=> $"1inch_{Chain}_{RpcClient.WalletAddress[2..10]}";

	private void ValidatePortfolio(string portfolioName)
	{
		if (!RpcClient.IsWalletConfigured)
			throw new InvalidOperationException(
				"An EVM wallet address is required for portfolio data.");
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(GetPortfolioName()))
			throw new InvalidOperationException(
				$"Unknown 1inch portfolio '{portfolioName}'.");
	}

	private async ValueTask<OneInchQuote> GetQuoteAsync(OneInchToken source,
		OneInchToken destination, BigInteger amount,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(destination);
		if (amount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		var response = await HttpClient.GetQuoteAsync(source.Address,
			destination.Address, amount, cancellationToken);
		ValidateTokenInfo(response?.SourceToken, source, "source");
		ValidateTokenInfo(response.DestinationToken, destination,
			"destination");
		var output = response.DestinationAmount.ParseInteger();
		if (output <= 0 || response.Gas is <= 0)
			throw new InvalidDataException(
				"1inch API returned invalid quote amounts or gas estimate.");
		return new()
		{
			InputAmount = amount,
			OutputAmount = output,
		};
	}

	private static void ValidateTokenInfo(OneInchTokenInfo info,
		OneInchToken token, string role)
	{
		if (info is null ||
			!info.Address.NormalizeAddress().EqualsIgnoreCase(token.Address) ||
			info.Decimals != token.Decimals || info.IsFeeOnTransfer == true)
			throw new InvalidDataException(
				$"1inch API returned invalid {role} token metadata.");
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		base.DisposeManaged();
	}
}
