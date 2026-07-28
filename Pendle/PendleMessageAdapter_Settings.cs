namespace StockSharp.Pendle;

/// <summary>Networks currently supported by the Pendle API.</summary>
public enum PendleChains
{
	/// <summary>Ethereum Mainnet.</summary>
	Ethereum = 1,
	/// <summary>Optimism.</summary>
	Optimism = 10,
	/// <summary>BNB Smart Chain.</summary>
	Bnb = 56,
	/// <summary>Monad.</summary>
	Monad = 143,
	/// <summary>Sonic.</summary>
	Sonic = 146,
	/// <summary>HyperEVM.</summary>
	HyperEvm = 999,
	/// <summary>Mantle.</summary>
	Mantle = 5000,
	/// <summary>Base.</summary>
	Base = 8453,
	/// <summary>Plume.</summary>
	Plume = 9745,
	/// <summary>Arbitrum One.</summary>
	Arbitrum = 42161,
	/// <summary>Berachain.</summary>
	Berachain = 80094,
}

/// <summary>The message adapter for Pendle yield markets.</summary>
[MediaIcon(Media.MediaNames.pendle)]
[Doc("topics/api/connectors/crypto_exchanges/pendle.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.PendleKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles |
	MessageAdapterCategories.History |
	MessageAdapterCategories.Transactions)]
public partial class PendleMessageAdapter : MessageAdapter
{
	private const string _defaultApiEndpoint =
		"https://api-v2.pendle.finance/core";

	/// <summary>Pendle production network.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BoardKey,
		Description = LocalizedStrings.BoardKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public PendleChains Chain
	{
		get => _chain;
		set
		{
			if (!System.Enum.IsDefined(value))
				throw new ArgumentOutOfRangeException(nameof(value), value,
					"Unsupported Pendle chain.");
			var previousDefault = _chain.GetDefaultRpcEndpoint();
			_chain = value;
			if (RpcEndpoint.IsEmpty() ||
				RpcEndpoint.EqualsIgnoreCase(previousDefault))
				RpcEndpoint = value.GetDefaultRpcEndpoint();
		}
	}
	private PendleChains _chain = PendleChains.Ethereum;

	/// <summary>Optional public EVM wallet address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		Description = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Optional EVM private key used to sign swaps.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	/// <summary>Pendle REST API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string ApiEndpoint { get; set; } = _defaultApiEndpoint;

	/// <summary>EVM HTTP JSON-RPC endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	[BasicSetting]
	public string RpcEndpoint { get; set; } =
		PendleChains.Ethereum.GetDefaultRpcEndpoint();

	/// <summary>
	/// Optional semicolon-separated Pendle market contract addresses. When
	/// empty, all active markets on the selected network are loaded.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecuritiesKey,
		Description = LocalizedStrings.SecuritiesKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public string MarketAddresses { get; set; }

	private int _maxMarkets = 500;

	/// <summary>Maximum number of active markets loaded from the API.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public int MaxMarkets
	{
		get => _maxMarkets;
		set => _maxMarkets = value is > 0 and <= 5000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Pendle market limit must be between 1 and 5000.");
	}

	private decimal _probeVolume = 1m;

	/// <summary>Asset amount represented by each executable quote.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.VolumeKey,
		Description = LocalizedStrings.VolumeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public decimal ProbeVolume
	{
		get => _probeVolume;
		set => _probeVolume = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Pendle quote volume must be positive.");
	}

	private decimal _slippageTolerance = 0.5m;

	/// <summary>Maximum market-swap slippage in percent.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SlippageKey,
		Description = LocalizedStrings.SlippageKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public decimal SlippageTolerance
	{
		get => _slippageTolerance;
		set => _slippageTolerance = value is > 0 and <= 50 &&
			decimal.Round(value, 2) == value
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"Slippage tolerance must be greater than zero and no more " +
						"than 50 percent, with at most two decimal places.");
	}

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(15);

	/// <summary>Polling interval for market and private state.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval = value >= TimeSpan.FromSeconds(5)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Pendle polling interval cannot be less than five seconds.");
	}

	private int _historyLimit = 1000;

	/// <summary>Maximum number of historical points per subscription.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 8)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is > 0 and <= 10000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Pendle history limit must be between 1 and 10000.");
	}

	private TimeSpan _receiptTimeout = TimeSpan.FromMinutes(3);

	/// <summary>Maximum time to wait for approval transactions.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TimeKey,
		Description = LocalizedStrings.TimeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 9)]
	public TimeSpan ReceiptTimeout
	{
		get => _receiptTimeout;
		set => _receiptTimeout = value >= TimeSpan.FromSeconds(30) &&
			value <= TimeSpan.FromMinutes(15)
				? value
				: throw new ArgumentOutOfRangeException(nameof(value), value,
					"Receipt timeout must be between 30 seconds and 15 minutes.");
	}

	/// <summary>Automatically approve the Pendle router when required.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AutoKey,
		Description = LocalizedStrings.AutoKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 10)]
	public bool IsAutoApprove { get; set; } = true;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Chain), Chain)
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(ApiEndpoint), ApiEndpoint)
			.Set(nameof(RpcEndpoint), RpcEndpoint)
			.Set(nameof(MarketAddresses), MarketAddresses)
			.Set(nameof(MaxMarkets), MaxMarkets)
			.Set(nameof(ProbeVolume), ProbeVolume)
			.Set(nameof(SlippageTolerance), SlippageTolerance)
			.Set(nameof(PollingInterval), PollingInterval)
			.Set(nameof(HistoryLimit), HistoryLimit)
			.Set(nameof(ReceiptTimeout), ReceiptTimeout)
			.Set(nameof(IsAutoApprove), IsAutoApprove);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Chain = storage.GetValue(nameof(Chain), Chain);
		if (!System.Enum.IsDefined(Chain))
			throw new InvalidDataException(
				$"Unsupported Pendle chain '{Chain}'.");
		WalletAddress = storage.GetValue<string>(nameof(WalletAddress));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		ApiEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(ApiEndpoint), ApiEndpoint)) ?? _defaultApiEndpoint;
		RpcEndpoint = NormalizeEndpoint(storage.GetValue<string>(
			nameof(RpcEndpoint))) ?? Chain.GetDefaultRpcEndpoint();
		MarketAddresses = storage.GetValue<string>(nameof(MarketAddresses));
		MaxMarkets = storage.GetValue(nameof(MaxMarkets), MaxMarkets);
		ProbeVolume = storage.GetValue(nameof(ProbeVolume), ProbeVolume);
		SlippageTolerance = storage.GetValue(nameof(SlippageTolerance),
			SlippageTolerance);
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
		ReceiptTimeout = storage.GetValue(nameof(ReceiptTimeout),
			ReceiptTimeout);
		IsAutoApprove = storage.GetValue(nameof(IsAutoApprove),
			IsAutoApprove);
	}

	private static string NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint?.Trim();
		if (endpoint.IsEmpty())
			return null;
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"https://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": {Chain}, Wallet={WalletAddress}";
}
