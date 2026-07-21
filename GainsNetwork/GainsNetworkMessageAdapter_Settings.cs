namespace StockSharp.GainsNetwork;

public partial class GainsNetworkMessageAdapter
{
	private const string _defaultGlobalEndpoint =
		"https://backend-global.gains.trade";
	private const string _defaultPricingEndpoint =
		"https://backend-pricing.eu.gains.trade";
	private const string _defaultPriceSocketEndpoint =
		"wss://backend-pricing.eu.gains.trade/v4";

	/// <summary>Gains Network deployment.</summary>
	[Display(Name = "Environment", Description =
		"Gains Network deployment environment.",
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public GainsNetworkEnvironments Environment { get; set; } =
		GainsNetworkEnvironments.Arbitrum;

	/// <summary>Optional JSON-RPC endpoint override.</summary>
	[Display(Name = "JSON-RPC endpoint", Description =
		"Optional network JSON-RPC endpoint override.",
		GroupName = LocalizedStrings.AddressesKey, Order = 0)]
	public string RpcEndpoint { get; set; }

	/// <summary>Optional chain backend endpoint override.</summary>
	[Display(Name = "Backend endpoint", Description =
		"Optional chain-specific Gains backend endpoint override.",
		GroupName = LocalizedStrings.AddressesKey, Order = 1)]
	public string BackendEndpoint { get; set; }

	/// <summary>Global history backend endpoint.</summary>
	[Display(Name = "Global endpoint", Description =
		"Gains cross-chain history backend endpoint.",
		GroupName = LocalizedStrings.AddressesKey, Order = 2)]
	public string GlobalEndpoint { get; set; } = _defaultGlobalEndpoint;

	/// <summary>Pricing REST endpoint.</summary>
	[Display(Name = "Pricing endpoint", Description =
		"Gains current OHLC snapshot endpoint.",
		GroupName = LocalizedStrings.AddressesKey, Order = 3)]
	public string PricingEndpoint { get; set; } = _defaultPricingEndpoint;

	/// <summary>Live price WebSocket endpoint.</summary>
	[Display(Name = "Price WebSocket", Description =
		"Gains live mark and index price stream.",
		GroupName = LocalizedStrings.AddressesKey, Order = 4)]
	public string PriceSocketEndpoint { get; set; } =
		_defaultPriceSocketEndpoint;

	/// <summary>EVM wallet for read-only account access.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Optional EVM private key used to sign transactions.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	private string _defaultCollateral = "USDC";

	/// <summary>Default collateral token.</summary>
	[Display(Name = "Collateral", Description =
		"Default collateral token for new positions.",
		GroupName = LocalizedStrings.TransactionKey, Order = 3)]
	public string DefaultCollateral
	{
		get => _defaultCollateral;
		set => _defaultCollateral = value.ThrowIfEmpty(nameof(value)).Trim()
			.ToUpperInvariant();
	}

	private decimal _defaultLeverage = 10m;

	/// <summary>Default position leverage.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LeverageKey,
		GroupName = LocalizedStrings.TransactionKey, Order = 4)]
	public decimal DefaultLeverage
	{
		get => _defaultLeverage;
		set => _defaultLeverage = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Default leverage must be positive.");
	}

	private decimal _slippagePercentage = 1m;

	/// <summary>Maximum execution slippage, in percent.</summary>
	[Display(Name = "Slippage (%)", Description =
		"Maximum execution slippage in percentage points.",
		GroupName = LocalizedStrings.TransactionKey, Order = 5)]
	public decimal SlippagePercentage
	{
		get => _slippagePercentage;
		set => _slippagePercentage = value is >= 0 and <= 10
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Gains slippage must be between zero and 10 percent.");
	}

	/// <summary>Optional Gains referral address.</summary>
	[Display(Name = "Referrer", Description =
		"Optional EVM referral address included with new trades.",
		GroupName = LocalizedStrings.TransactionKey, Order = 6)]
	public string ReferrerAddress { get; set; }

	/// <summary>Automatically approve collateral when allowance is low.</summary>
	[Display(Name = "Auto approve", Description =
		"Automatically approve the Gains diamond to spend collateral.",
		GroupName = LocalizedStrings.TransactionKey, Order = 7)]
	public bool IsAutoApprove { get; set; } = true;

	private decimal _approvalAmount = 1_000_000m;

	/// <summary>Collateral allowance requested by automatic approval.</summary>
	[Display(Name = "Approval amount",
		GroupName = LocalizedStrings.TransactionKey, Order = 8)]
	public decimal ApprovalAmount
	{
		get => _approvalAmount;
		set => _approvalAmount = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Gains approval amount must be positive.");
	}

	private TimeSpan _transactionTimeout = TimeSpan.FromMinutes(2);

	/// <summary>Maximum transaction receipt wait time.</summary>
	[Display(Name = "Transaction timeout",
		GroupName = LocalizedStrings.TransactionKey, Order = 9)]
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

	/// <summary>Private account reconciliation interval.</summary>
	[Display(Name = "Account refresh",
		GroupName = LocalizedStrings.ConnectionKey, Order = 10)]
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

	/// <summary>Maximum history records per request.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.HistoryKey, Order = 11)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is >= 1 and <= 5000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Gains history limit must be between one and 5000.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Environment), Environment)
			.Set(nameof(RpcEndpoint), RpcEndpoint)
			.Set(nameof(BackendEndpoint), BackendEndpoint)
			.Set(nameof(GlobalEndpoint), GlobalEndpoint)
			.Set(nameof(PricingEndpoint), PricingEndpoint)
			.Set(nameof(PriceSocketEndpoint), PriceSocketEndpoint)
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(DefaultCollateral), DefaultCollateral)
			.Set(nameof(DefaultLeverage), DefaultLeverage)
			.Set(nameof(SlippagePercentage), SlippagePercentage)
			.Set(nameof(ReferrerAddress), ReferrerAddress)
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
		BackendEndpoint = NormalizeOptionalEndpoint(storage.GetValue<string>(
			nameof(BackendEndpoint)), false, nameof(BackendEndpoint));
		GlobalEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(GlobalEndpoint), GlobalEndpoint), false,
			nameof(GlobalEndpoint));
		PricingEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(PricingEndpoint), PricingEndpoint), false,
			nameof(PricingEndpoint));
		PriceSocketEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(PriceSocketEndpoint), PriceSocketEndpoint), true,
			nameof(PriceSocketEndpoint));
		WalletAddress = storage.GetValue<string>(nameof(WalletAddress));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		DefaultCollateral = storage.GetValue(nameof(DefaultCollateral),
			DefaultCollateral);
		DefaultLeverage = storage.GetValue(nameof(DefaultLeverage),
			DefaultLeverage);
		SlippagePercentage = storage.GetValue(nameof(SlippagePercentage),
			SlippagePercentage);
		ReferrerAddress = storage.GetValue<string>(nameof(ReferrerAddress));
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
		=> new GainsNetworkMessageAdapter(TransactionIdGenerator)
		{
			Environment = Environment,
			RpcEndpoint = RpcEndpoint,
			BackendEndpoint = BackendEndpoint,
			GlobalEndpoint = GlobalEndpoint,
			PricingEndpoint = PricingEndpoint,
			PriceSocketEndpoint = PriceSocketEndpoint,
			WalletAddress = WalletAddress,
			PrivateKey = PrivateKey,
			DefaultCollateral = DefaultCollateral,
			DefaultLeverage = DefaultLeverage,
			SlippagePercentage = SlippagePercentage,
			ReferrerAddress = ReferrerAddress,
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
				? "Gains WebSocket endpoint must use WS or WSS."
				: "Gains endpoint must use HTTP or HTTPS.", parameterName);
		return endpoint;
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + (WalletAddress.IsEmpty() && PrivateKey.IsEmpty()
			? ": Public"
			: PrivateKey.IsEmpty() ? ": Read-only" : ": Trading");
}
