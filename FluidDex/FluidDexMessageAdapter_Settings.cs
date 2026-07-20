namespace StockSharp.FluidDex;

/// <summary>Fluid DEX deployment chains.</summary>
public enum FluidDexChains
{
	/// <summary>Ethereum Mainnet.</summary>
	Ethereum = 1,
	/// <summary>BNB Smart Chain.</summary>
	BnbSmartChain = 56,
	/// <summary>Polygon PoS.</summary>
	Polygon = 137,
	/// <summary>Base.</summary>
	Base = 8453,
	/// <summary>Plasma.</summary>
	Plasma = 9745,
	/// <summary>Arbitrum One.</summary>
	Arbitrum = 42161,
}

/// <summary>The message adapter for Fluid DEX T1 pools.</summary>
[MediaIcon(Media.MediaNames.fluid_dex)]
[Doc("topics/api/connectors/crypto_exchanges/fluid_dex.html")]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.FluidDexKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Candles |
	MessageAdapterCategories.History | MessageAdapterCategories.Transactions)]
public partial class FluidDexMessageAdapter : MessageAdapter
{
	private const string _defaultFactoryAddress =
		"0x91716C4EDA1Fb55e84Bf8b4c7085f84285c19085";
	private const string _defaultResolverAddress =
		"0x05Bd8269A20C472b148246De20E6852091BF16Ff";

	/// <summary>Supported candle intervals.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
		FluidDexExtensions.TimeFrames;

	/// <summary>Fluid DEX deployment chain.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BoardKey,
		Description = LocalizedStrings.BoardKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public FluidDexChains Chain { get; set; } = FluidDexChains.Ethereum;

	/// <summary>Public wallet address used for balances.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		Description = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Optional private key used to sign on-chain transactions.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	/// <summary>HTTP JSON-RPC endpoint for the selected chain.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey, Order = 0)]
	[BasicSetting]
	public string RpcEndpoint { get; set; }

	/// <summary>WebSocket JSON-RPC endpoint for real-time swap logs.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey, Order = 1)]
	[BasicSetting]
	public string WebSocketEndpoint { get; set; }

	/// <summary>Official Fluid DEX factory contract.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.AddressKey,
		GroupName = LocalizedStrings.AddressesKey, Order = 2)]
	public string FactoryAddress { get; set; } = _defaultFactoryAddress;

	/// <summary>Official Fluid DEX reserves resolver contract.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.AddressKey,
		GroupName = LocalizedStrings.AddressesKey, Order = 3)]
	public string ResolverAddress { get; set; } = _defaultResolverAddress;

	/// <summary>
	/// Optional semicolon-separated pool definitions in
	/// <c>pool|base token|quote token|security code</c> format.
	/// Empty value enables factory discovery.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecuritiesKey,
		Description = LocalizedStrings.SecuritiesKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	public string Pools { get; set; }

	private int _maximumDiscoveredPools = 200;

	/// <summary>Maximum number of pools loaded from the factory.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	public int MaximumDiscoveredPools
	{
		get => _maximumDiscoveredPools;
		set => _maximumDiscoveredPools = value is >= 1 and <= 2000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Pool discovery limit must be between 1 and 2000.");
	}

	private int _historyBlockRange = 5_000;

	/// <summary>Maximum block range requested by one log query.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
	public int HistoryBlockRange
	{
		get => _historyBlockRange;
		set => _historyBlockRange = value is >= 1 and <= 50_000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"History block range must be between 1 and 50000.");
	}

	private int _historyBlockCount = 250_000;

	/// <summary>Recent blocks searched when history has no start time.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 6)]
	public int HistoryBlockCount
	{
		get => _historyBlockCount;
		set => _historyBlockCount = value is >= 1 and <= 10_000_000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"History block count must be between 1 and 10000000.");
	}

	private decimal _probeVolume = 1m;

	/// <summary>Base-token amount used for executable quote probes.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.VolumeKey,
		Description = LocalizedStrings.VolumeKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 7)]
	public decimal ProbeVolume
	{
		get => _probeVolume;
		set => _probeVolume = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Quote probe volume must be positive.");
	}

	private int _depthLevelCount = 10;

	/// <summary>Number of executable quote levels per side.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 8)]
	public int DepthLevelCount
	{
		get => _depthLevelCount;
		set => _depthLevelCount = value is >= 1 and <= 50
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Depth level count must be between 1 and 50.");
	}

	private decimal _slippageTolerance = 0.5m;

	/// <summary>Swap slippage tolerance in percent.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SlippageKey,
		Description = LocalizedStrings.SlippageKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 9)]
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

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

	/// <summary>Fallback polling interval for quotes, logs, and receipts.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 10)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval = value >= TimeSpan.FromSeconds(1)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Polling interval cannot be less than one second.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Chain), Chain)
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(RpcEndpoint), RpcEndpoint)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint)
			.Set(nameof(FactoryAddress), FactoryAddress)
			.Set(nameof(ResolverAddress), ResolverAddress)
			.Set(nameof(Pools), Pools)
			.Set(nameof(MaximumDiscoveredPools), MaximumDiscoveredPools)
			.Set(nameof(HistoryBlockRange), HistoryBlockRange)
			.Set(nameof(HistoryBlockCount), HistoryBlockCount)
			.Set(nameof(ProbeVolume), ProbeVolume)
			.Set(nameof(DepthLevelCount), DepthLevelCount)
			.Set(nameof(SlippageTolerance), SlippageTolerance)
			.Set(nameof(PollingInterval), PollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Chain = storage.GetValue(nameof(Chain), Chain);
		WalletAddress = storage.GetValue<string>(nameof(WalletAddress));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		RpcEndpoint = NormalizeEndpoint(storage.GetValue<string>(
			nameof(RpcEndpoint)), "https");
		WebSocketEndpoint = NormalizeEndpoint(storage.GetValue<string>(
			nameof(WebSocketEndpoint)), "wss");
		FactoryAddress = storage.GetValue(nameof(FactoryAddress),
			FactoryAddress);
		ResolverAddress = storage.GetValue(nameof(ResolverAddress),
			ResolverAddress);
		Pools = storage.GetValue<string>(nameof(Pools));
		MaximumDiscoveredPools = storage.GetValue(
			nameof(MaximumDiscoveredPools), MaximumDiscoveredPools);
		HistoryBlockRange = storage.GetValue(nameof(HistoryBlockRange),
			HistoryBlockRange);
		HistoryBlockCount = storage.GetValue(nameof(HistoryBlockCount),
			HistoryBlockCount);
		ProbeVolume = storage.GetValue(nameof(ProbeVolume), ProbeVolume);
		DepthLevelCount = storage.GetValue(nameof(DepthLevelCount),
			DepthLevelCount);
		SlippageTolerance = storage.GetValue(nameof(SlippageTolerance),
			SlippageTolerance);
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
	}

	private static string NormalizeEndpoint(string endpoint, string scheme)
	{
		endpoint = endpoint?.Trim();
		if (endpoint.IsEmpty())
			return endpoint;
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"{scheme}://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": Chain={Chain}, Wallet={WalletAddress}";
}
