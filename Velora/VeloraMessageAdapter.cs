namespace StockSharp.Velora;

public partial class VeloraMessageAdapter
{
	private sealed class Level1Subscription
	{
		public VeloraMarket Market { get; init; }
	}

	private sealed class TrackedSwap
	{
		public long TransactionId { get; init; }
		public string TransactionHash { get; init; }
		public VeloraMarket Market { get; init; }
		public Sides Side { get; init; }
		public VeloraToken SourceToken { get; init; }
		public VeloraToken DestinationToken { get; init; }
		public BigInteger SourceAmount { get; init; }
		public decimal RequestedVolume { get; init; }
		public decimal Volume { get; set; }
		public decimal Price { get; set; }
		public DateTime SubmittedTime { get; init; }
		public DateTime ExecutionTime { get; set; }
		public OrderStates State { get; set; }
		public bool IsTradeSent { get; set; }
		public VeloraRpcReceipt Receipt { get; set; }
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
	private readonly Dictionary<string, VeloraMarket> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, VeloraToken> _tokens =
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
	private VeloraRpcClient _rpcClient;
	private VeloraHttpClient _httpClient;
	private DateTime _nextMarketPoll;
	private DateTime _nextPrivatePoll;

	/// <summary>Initializes the adapter.</summary>
	public VeloraMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override string[] AssociatedBoards => [BoardCodes.Velora];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Velora) ||
			securityId.IsAssociated(BoardCodes.Velora);

	private VeloraRpcClient RpcClient => _rpcClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private VeloraHttpClient HttpClient => _httpClient ?? throw new
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
				"An EVM private key is required for Velora swaps.");
	}

	private VeloraMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not Velora.");
		var code = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId)).Trim().ToUpperInvariant();
		using (_sync.EnterScope())
			return _markets.TryGetValue(code, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown Velora market '{code}'.");
	}

	private string GetPortfolioName()
		=> $"Velora_{Chain}_{RpcClient.WalletAddress[2..10]}";

	private void ValidatePortfolio(string portfolioName)
	{
		if (!RpcClient.IsWalletConfigured)
			throw new InvalidOperationException(
				"An EVM wallet address is required for portfolio data.");
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(GetPortfolioName()))
			throw new InvalidOperationException(
				$"Unknown Velora portfolio '{portfolioName}'.");
	}

	private async ValueTask<VeloraQuote> GetQuoteAsync(VeloraToken source,
		VeloraToken destination, BigInteger amount,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(destination);
		if (amount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		var route = await HttpClient.GetPriceAsync(source.Address,
			source.Decimals, destination.Address, destination.Decimals, amount,
			RpcClient.IsWalletConfigured ? RpcClient.WalletAddress : null,
			cancellationToken);
		var output = ValidatePriceRoute(route, source, destination, amount,
			Chain);
		return new()
		{
			InputAmount = amount,
			OutputAmount = output,
		};
	}

	internal static BigInteger ValidatePriceRoute(JObject route,
		VeloraToken source, VeloraToken destination, BigInteger requestedAmount,
		VeloraChains chain)
	{
		ArgumentNullException.ThrowIfNull(route);
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(destination);
		if (ReadRouteInteger(route, "network") != (int)chain ||
			!ReadRouteString(route, "srcToken").MatchesVeloraAddress(
				source.Address) ||
			!ReadRouteString(route, "destToken").MatchesVeloraAddress(
				destination.Address) ||
			ReadRouteInteger(route, "srcDecimals") != source.Decimals ||
			ReadRouteInteger(route, "destDecimals") != destination.Decimals ||
			ReadRouteString(route, "srcAmount").ParseInteger() !=
				requestedAmount)
			throw new InvalidDataException(
				"Velora price route does not match the requested network, " +
					"token pair, decimals, or amount.");
		var output = ReadRouteString(route, "destAmount").ParseInteger();
		if (output <= 0)
			throw new InvalidDataException(
				"Velora API returned a non-positive destination amount.");
		if (route["bestRoute"] is not JArray { Count: > 0 })
			throw new InvalidDataException(
				"Velora API returned no executable liquidity route.");
		if (route.Value<bool?>("maxImpactReached") == true)
			throw new InvalidDataException(
				"Velora API rejected the route because its price impact is " +
					"too high.");
		var version = ReadRouteString(route, "version");
		if (!version.EqualsIgnoreCase("6.2"))
			throw new InvalidDataException(
				$"Velora API returned unsupported router version '{version}'.");
		_ = GetRouteTarget(route);
		if (ReadRouteString(route, "contractMethod").IsEmpty() ||
			ReadRouteString(route, "hmac").IsEmpty())
			throw new InvalidDataException(
				"Velora price route has no contract method or integrity tag.");
		return output;
	}

	internal static string GetRouteTarget(JObject route)
	{
		ArgumentNullException.ThrowIfNull(route);
		var contract = ReadRouteString(route, "contractAddress")
			.NormalizeAddress();
		var proxy = ReadRouteString(route, "tokenTransferProxy")
			.NormalizeAddress();
		if (!contract.EqualsIgnoreCase(proxy))
			throw new InvalidDataException(
				"Velora v6.2 returned different router and allowance targets.");
		return contract;
	}

	internal static string ReadRouteString(JObject route, string name)
	{
		ArgumentNullException.ThrowIfNull(route);
		var value = route.Value<string>(
			name.ThrowIfEmpty(nameof(name)))?.Trim();
		return value.IsEmpty()
			? throw new InvalidDataException(
				$"Velora price route has no '{name}' value.")
			: value;
	}

	private static int ReadRouteInteger(JObject route, string name)
	{
		ArgumentNullException.ThrowIfNull(route);
		var token = route[name.ThrowIfEmpty(nameof(name))];
		if (token is null || !int.TryParse(token.ToString(
			Formatting.None).Trim('"'), NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var value))
			throw new InvalidDataException(
				$"Velora price route has an invalid '{name}' value.");
		return value;
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		base.DisposeManaged();
	}
}
