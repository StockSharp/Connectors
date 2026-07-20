namespace StockSharp.Avantis;

/// <summary>The message adapter for Avantis perpetual markets on Base.</summary>
[MediaIcon(Media.MediaNames.avantis)]
[Doc("topics/api/connectors/crypto_exchanges/avantis.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.AvantisKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.Level1)]
[OrderCondition(typeof(AvantisOrderCondition))]
public partial class AvantisMessageAdapter : MessageAdapter
{
	private const string _defaultRpcEndpoint = "https://mainnet.base.org";
	private const string _defaultMarketDataEndpoint =
		"https://socket-api-pub.avantisfi.com/socket-api/v1/data";
	private const string _defaultCoreApiEndpoint =
		"https://core.avantisfi.com/";
	private const string _defaultFeedEndpoint =
		"https://feed-v3.avantisfi.com/";
	private const string _defaultLazerEndpoint =
		"https://pyth-lazer-proxy-3.dourolabs.app/v1/stream";
	private const string _defaultHermesEndpoint =
		"wss://hermes.pyth.network/ws";

	/// <summary>Base Mainnet JSON-RPC endpoint.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey, Order = 0)]
	[BasicSetting]
	public string RpcEndpoint { get; set; } = _defaultRpcEndpoint;

	/// <summary>Official Avantis pair-metadata endpoint.</summary>
	[Display(Name = "Market metadata",
		Description = "Official Avantis pair and risk metadata endpoint.",
		GroupName = LocalizedStrings.AddressesKey, Order = 1)]
	public string MarketDataEndpoint { get; set; } =
		_defaultMarketDataEndpoint;

	/// <summary>Official Avantis account API endpoint.</summary>
	[Display(Name = "Core API",
		Description = "Official Avantis account-data endpoint.",
		GroupName = LocalizedStrings.AddressesKey, Order = 2)]
	public string CoreApiEndpoint { get; set; } = _defaultCoreApiEndpoint;

	/// <summary>Official Avantis price-update-data endpoint.</summary>
	[Display(Name = "Feed V3",
		Description = "Official Avantis oracle price endpoint.",
		GroupName = LocalizedStrings.AddressesKey, Order = 3)]
	public string FeedEndpoint { get; set; } = _defaultFeedEndpoint;

	/// <summary>Pyth Lazer realtime SSE endpoint used by Avantis.</summary>
	[Display(Name = "Pyth Lazer",
		Description = "Realtime Pyth Lazer SSE endpoint used by Avantis.",
		GroupName = LocalizedStrings.AddressesKey, Order = 4)]
	public string LazerEndpoint { get; set; } = _defaultLazerEndpoint;

	/// <summary>Pyth Hermes WebSocket fallback endpoint.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
		Description = "Pyth Hermes fallback for pairs without Lazer.",
		GroupName = LocalizedStrings.AddressesKey, Order = 5)]
	public string HermesEndpoint { get; set; } = _defaultHermesEndpoint;

	/// <summary>Optional EVM wallet for read-only account access.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 6)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Optional EVM private key used to sign Base transactions.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 7)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	private decimal _defaultLeverage = 10m;

	/// <summary>Leverage used when no Avantis condition is supplied.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LeverageKey,
		GroupName = LocalizedStrings.TransactionKey, Order = 8)]
	public decimal DefaultLeverage
	{
		get => _defaultLeverage;
		set => _defaultLeverage = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Default leverage must be positive.");
	}

	private decimal _slippage = 1m;

	/// <summary>Maximum opening-price slippage in percent.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SlippageKey,
		GroupName = LocalizedStrings.TransactionKey, Order = 9)]
	public decimal Slippage
	{
		get => _slippage;
		set => _slippage = value is >= 0 and <= 50
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Avantis slippage must be between zero and 50%.");
	}

	private decimal _executionFee = 0.00035m;

	/// <summary>Default keeper execution fee in ETH.</summary>
	[Display(Name = "Execution fee",
		GroupName = LocalizedStrings.TransactionKey, Order = 10)]
	public decimal ExecutionFee
	{
		get => _executionFee;
		set => _executionFee = value is >= 0 and <= 1
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Avantis execution fee must be between zero and one ETH.");
	}

	/// <summary>Automatically approve USDC when allowance is insufficient.</summary>
	[Display(Name = "Auto approve USDC",
		GroupName = LocalizedStrings.TransactionKey, Order = 11)]
	public bool IsAutoApprove { get; set; } = true;

	private decimal _approvalAmount = 100000m;

	/// <summary>USDC allowance requested by automatic approval.</summary>
	[Display(Name = "USDC approval",
		GroupName = LocalizedStrings.TransactionKey, Order = 12)]
	public decimal ApprovalAmount
	{
		get => _approvalAmount;
		set => _approvalAmount = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"USDC approval amount must be positive.");
	}

	private TimeSpan _transactionTimeout = TimeSpan.FromMinutes(2);

	/// <summary>Maximum time to wait for a Base transaction receipt.</summary>
	[Display(Name = "Transaction timeout",
		GroupName = LocalizedStrings.TransactionKey, Order = 13)]
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

	/// <summary>Polling interval for official Avantis account data.</summary>
	[Display(Name = "Account refresh",
		GroupName = LocalizedStrings.ConnectionKey, Order = 14)]
	public TimeSpan AccountRefreshInterval
	{
		get => _accountRefreshInterval;
		set => _accountRefreshInterval = value >= TimeSpan.FromSeconds(2) &&
			value <= TimeSpan.FromMinutes(5)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Account refresh must be between two seconds and five minutes.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(RpcEndpoint), RpcEndpoint)
			.Set(nameof(MarketDataEndpoint), MarketDataEndpoint)
			.Set(nameof(CoreApiEndpoint), CoreApiEndpoint)
			.Set(nameof(FeedEndpoint), FeedEndpoint)
			.Set(nameof(LazerEndpoint), LazerEndpoint)
			.Set(nameof(HermesEndpoint), HermesEndpoint)
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(DefaultLeverage), DefaultLeverage)
			.Set(nameof(Slippage), Slippage)
			.Set(nameof(ExecutionFee), ExecutionFee)
			.Set(nameof(IsAutoApprove), IsAutoApprove)
			.Set(nameof(ApprovalAmount), ApprovalAmount)
			.Set(nameof(TransactionTimeout), TransactionTimeout)
			.Set(nameof(AccountRefreshInterval), AccountRefreshInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		RpcEndpoint = NormalizeEndpoint(storage.GetValue(nameof(RpcEndpoint),
			RpcEndpoint), false, nameof(RpcEndpoint));
		MarketDataEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(MarketDataEndpoint), MarketDataEndpoint), false,
			nameof(MarketDataEndpoint));
		CoreApiEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(CoreApiEndpoint), CoreApiEndpoint), false,
			nameof(CoreApiEndpoint));
		FeedEndpoint = NormalizeEndpoint(storage.GetValue(nameof(FeedEndpoint),
			FeedEndpoint), false, nameof(FeedEndpoint));
		LazerEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(LazerEndpoint), LazerEndpoint), false,
			nameof(LazerEndpoint));
		HermesEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(HermesEndpoint), HermesEndpoint), true,
			nameof(HermesEndpoint));
		WalletAddress = storage.GetValue(nameof(WalletAddress), WalletAddress);
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		DefaultLeverage = storage.GetValue(nameof(DefaultLeverage),
			DefaultLeverage);
		Slippage = storage.GetValue(nameof(Slippage), Slippage);
		ExecutionFee = storage.GetValue(nameof(ExecutionFee), ExecutionFee);
		IsAutoApprove = storage.GetValue(nameof(IsAutoApprove), IsAutoApprove);
		ApprovalAmount = storage.GetValue(nameof(ApprovalAmount), ApprovalAmount);
		TransactionTimeout = storage.GetValue(nameof(TransactionTimeout),
			TransactionTimeout);
		AccountRefreshInterval = storage.GetValue(
			nameof(AccountRefreshInterval), AccountRefreshInterval);
	}

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
				? "Avantis WebSocket endpoint must use WS or WSS."
				: "Avantis endpoint must use HTTP or HTTPS.", parameterName);
		return endpoint;
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + (WalletAddress.IsEmpty() && PrivateKey.IsEmpty()
			? ": Public"
			: PrivateKey.IsEmpty() ? ": Read-only" : ": Trading");
}
