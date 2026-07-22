namespace StockSharp.Ostium;

/// <summary>The message adapter for Ostium perpetual markets on Arbitrum.</summary>
[MediaIcon(Media.MediaNames.ostium)]
[Doc("topics/api/connectors/crypto_exchanges/ostium.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.OstiumKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles)]
[OrderCondition(typeof(OstiumOrderCondition))]
public partial class OstiumMessageAdapter : MessageAdapter
{
	private const string _defaultBuilderEndpoint =
		"https://builder.ostium.io";
	private const string _defaultPriceStreamEndpoint =
		"wss://builder.ostium.io/v1/prices/stream";

	/// <summary>Ostium deployment environment.</summary>
	[Display(
		Name = "Environment",
		Description = "Ostium network environment.",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public OstiumEnvironments Environment { get; set; }

	/// <summary>Optional JSON-RPC endpoint override.</summary>
	[Display(
		Name = "JSON-RPC",
		Description = "Optional Arbitrum JSON-RPC endpoint override.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	public string RpcEndpoint { get; set; }

	/// <summary>Official Ostium Builder API endpoint.</summary>
	[Display(
		Name = "Builder API",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	public string BuilderEndpoint { get; set; } = _defaultBuilderEndpoint;

	/// <summary>Official Ostium price-stream endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
		Description = "Official Ostium realtime price stream.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 3)]
	public string PriceStreamEndpoint { get; set; } =
		_defaultPriceStreamEndpoint;

	/// <summary>Optional Ostium subgraph endpoint override.</summary>
	[Display(
		Name = "Subgraph",
		Description = "Optional official Ostium subgraph endpoint override.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 4)]
	public string SubgraphEndpoint { get; set; }

	/// <summary>Optional EVM wallet for read-only account access.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Optional EVM private key used to sign transactions.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	private decimal _defaultLeverage = 10m;

	/// <summary>Leverage used when no Ostium condition is supplied.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LeverageKey,
		GroupName = LocalizedStrings.TransactionKey,
		Order = 7)]
	public decimal DefaultLeverage
	{
		get => _defaultLeverage;
		set => _defaultLeverage = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Default leverage must be positive.");
	}

	private int _slippageBps = 25;

	/// <summary>Maximum execution slippage in basis points.</summary>
	[Display(
		Name = "Slippage (bps)",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 8)]
	public int SlippageBps
	{
		get => _slippageBps;
		set => _slippageBps = value is >= 0 and <= 5000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Ostium slippage must be between zero and 5000 basis points.");
	}

	/// <summary>Automatically approve USDC when allowance is insufficient.</summary>
	[Display(
		Name = "Auto approve USDC",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 9)]
	public bool IsAutoApprove { get; set; } = true;

	private decimal _approvalAmount = 100000m;

	/// <summary>USDC allowance requested by automatic approval.</summary>
	[Display(
		Name = "USDC approval",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 10)]
	public decimal ApprovalAmount
	{
		get => _approvalAmount;
		set => _approvalAmount = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"USDC approval amount must be positive.");
	}

	private TimeSpan _transactionTimeout = TimeSpan.FromMinutes(2);

	/// <summary>Maximum time to wait for an Arbitrum transaction receipt.</summary>
	[Display(
		Name = "Transaction timeout",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 11)]
	public TimeSpan TransactionTimeout
	{
		get => _transactionTimeout;
		set => _transactionTimeout = value >= TimeSpan.FromSeconds(10) &&
			value <= TimeSpan.FromMinutes(15)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Transaction timeout must be between 10 seconds and 15 minutes.");
	}

	private TimeSpan _accountRefreshInterval = TimeSpan.FromSeconds(10);

	/// <summary>Polling interval for Ostium account data.</summary>
	[Display(
		Name = "Account refresh",
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 12)]
	public TimeSpan AccountRefreshInterval
	{
		get => _accountRefreshInterval;
		set => _accountRefreshInterval = value >= TimeSpan.FromSeconds(2) &&
			value <= TimeSpan.FromMinutes(5)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Account refresh must be between two seconds and five minutes.");
	}

	private int _historyLimit = 1000;

	/// <summary>Maximum account or candle records per request.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.HistoryKey,
		Order = 13)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is >= 1 and <= 5000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Ostium history limit must be between one and 5000.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Environment), Environment)
			.Set(nameof(RpcEndpoint), RpcEndpoint)
			.Set(nameof(BuilderEndpoint), BuilderEndpoint)
			.Set(nameof(PriceStreamEndpoint), PriceStreamEndpoint)
			.Set(nameof(SubgraphEndpoint), SubgraphEndpoint)
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(DefaultLeverage), DefaultLeverage)
			.Set(nameof(SlippageBps), SlippageBps)
			.Set(nameof(IsAutoApprove), IsAutoApprove)
			.Set(nameof(ApprovalAmount), ApprovalAmount)
			.Set(nameof(TransactionTimeout), TransactionTimeout)
			.Set(nameof(AccountRefreshInterval), AccountRefreshInterval)
			.Set(nameof(HistoryLimit), HistoryLimit);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Environment = storage.GetValue(nameof(Environment), Environment);
		RpcEndpoint = NormalizeOptionalEndpoint(storage.GetValue<string>(
			nameof(RpcEndpoint)), false, nameof(RpcEndpoint));
		BuilderEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(BuilderEndpoint), BuilderEndpoint), false,
			nameof(BuilderEndpoint));
		PriceStreamEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(PriceStreamEndpoint), PriceStreamEndpoint), true,
			nameof(PriceStreamEndpoint));
		SubgraphEndpoint = NormalizeOptionalEndpoint(storage.GetValue<string>(
			nameof(SubgraphEndpoint)), false, nameof(SubgraphEndpoint));
		WalletAddress = storage.GetValue<string>(nameof(WalletAddress));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		DefaultLeverage = storage.GetValue(nameof(DefaultLeverage),
			DefaultLeverage);
		SlippageBps = storage.GetValue(nameof(SlippageBps), SlippageBps);
		IsAutoApprove = storage.GetValue(nameof(IsAutoApprove), IsAutoApprove);
		ApprovalAmount = storage.GetValue(nameof(ApprovalAmount), ApprovalAmount);
		TransactionTimeout = storage.GetValue(nameof(TransactionTimeout),
			TransactionTimeout);
		AccountRefreshInterval = storage.GetValue(
			nameof(AccountRefreshInterval), AccountRefreshInterval);
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
	}

	/// <inheritdoc />
	public override IMessageAdapter Clone()
		=> new OstiumMessageAdapter(TransactionIdGenerator)
		{
			Environment = Environment,
			RpcEndpoint = RpcEndpoint,
			BuilderEndpoint = BuilderEndpoint,
			PriceStreamEndpoint = PriceStreamEndpoint,
			SubgraphEndpoint = SubgraphEndpoint,
			WalletAddress = WalletAddress,
			PrivateKey = PrivateKey,
			DefaultLeverage = DefaultLeverage,
			SlippageBps = SlippageBps,
			IsAutoApprove = IsAutoApprove,
			ApprovalAmount = ApprovalAmount,
			TransactionTimeout = TransactionTimeout,
			AccountRefreshInterval = AccountRefreshInterval,
			HistoryLimit = HistoryLimit,
		};

	private static string NormalizeOptionalEndpoint(string endpoint,
		bool isWebSocket, string parameterName)
		=> endpoint.IsEmpty()
			? null
			: NormalizeEndpoint(endpoint, isWebSocket, parameterName);

	private static string NormalizeEndpoint(string endpoint, bool isWebSocket,
		string parameterName)
	{
		endpoint = endpoint.ThrowIfEmpty(parameterName).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = (isWebSocket ? "wss://" : "https://") +
				endpoint.TrimStart('/');
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			(isWebSocket
				? uri.Scheme is not ("ws" or "wss")
				: uri.Scheme is not ("http" or "https")))
			throw new ArgumentException(isWebSocket
				? "Ostium WebSocket endpoint must use WS or WSS."
				: "Ostium endpoint must use HTTP or HTTPS.", parameterName);
		return endpoint;
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + (WalletAddress.IsEmpty() && PrivateKey.IsEmpty()
			? ": Public"
			: PrivateKey.IsEmpty() ? ": Read-only" : ": Trading");
}
