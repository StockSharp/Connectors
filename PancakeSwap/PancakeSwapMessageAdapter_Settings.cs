namespace StockSharp.PancakeSwap;

/// <summary>PancakeSwap EVM deployments supported by this connector.</summary>
public enum PancakeSwapChains
{
	/// <summary>Ethereum Mainnet.</summary>
	Ethereum = 1,
	/// <summary>BNB Smart Chain.</summary>
	BnbSmartChain = 56,
	/// <summary>BNB Smart Chain Testnet.</summary>
	BnbSmartChainTestnet = 97,
	/// <summary>Monad.</summary>
	Monad = 143,
	/// <summary>opBNB.</summary>
	OpBnb = 204,
	/// <summary>zkSync Era.</summary>
	ZkSync = 324,
	/// <summary>Robinhood Chain.</summary>
	RobinhoodChain = 4663,
	/// <summary>Base.</summary>
	Base = 8453,
	/// <summary>Arbitrum One.</summary>
	Arbitrum = 42161,
	/// <summary>Linea.</summary>
	Linea = 59144,
}

/// <summary>The message adapter for PancakeSwap v2 and v3 AMM markets.</summary>
[MediaIcon(Media.MediaNames.pancakeswap)]
[Doc("topics/api/connectors/crypto_exchanges/pancakeswap.html")]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.PancakeSwapKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles | MessageAdapterCategories.History |
	MessageAdapterCategories.Transactions)]
public partial class PancakeSwapMessageAdapter : MessageAdapter
{
	private const string _defaultV3SubgraphId =
		"Hv1GncLY5docZoGtXjo4kwbTvxm3MAhVZqBZE4sUT9eZ";
	private const string _defaultMarkets =
		"V3|0xbb4CdB9CBd36B01bD1cBaEBF2De08d9173bc095c|" +
		"0x55d398326f99059fF775485246999027B3197955|500;" +
		"V3|0x0E09FaBB73Bd3Ade0a17ECC321fD13a19e81cE82|" +
		"0xbb4CdB9CBd36B01bD1cBaEBF2De08d9173bc095c|2500;" +
		"V3|0x2170Ed0880ac9A755fd29B2688956BD959F933F8|" +
		"0xbb4CdB9CBd36B01bD1cBaEBF2De08d9173bc095c|500";

	/// <summary>Supported candle intervals.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
		PancakeSwapExtensions.TimeFrames;

	/// <summary>Optional The Graph gateway API key.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public SecureString GraphApiKey { get; set; }

	/// <summary>Public wallet address used for contract calls and balances.</summary>
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

	/// <summary>EVM chain.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BoardKey,
		Description = LocalizedStrings.BoardKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	[BasicSetting]
	public PancakeSwapChains Chain { get; set; } =
		PancakeSwapChains.BnbSmartChain;

	/// <summary>HTTP JSON-RPC endpoint for the selected chain.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey, Order = 0)]
	[BasicSetting]
	public string RpcEndpoint { get; set; }

	/// <summary>
	/// Optional v2 subgraph deployment ID or absolute HTTPS endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey, Order = 1)]
	public string V2Subgraph { get; set; }

	/// <summary>
	/// Optional v3 subgraph deployment ID or absolute HTTPS endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey, Order = 2)]
	public string V3Subgraph { get; set; } = _defaultV3SubgraphId;

	/// <summary>
	/// Semicolon-separated <c>version|base token|quote token|fee</c>
	/// definitions. The v2 fee must be zero.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecuritiesKey,
		Description = LocalizedStrings.SecuritiesKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	public string Markets { get; set; } = _defaultMarkets;

	private int _maximumDiscoveredPools = 100;

	/// <summary>Maximum number of top pools discovered per subgraph.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
	public int MaximumDiscoveredPools
	{
		get => _maximumDiscoveredPools;
		set => _maximumDiscoveredPools = value is >= 1 and <= 1000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Pool discovery limit must be between 1 and 1000.");
	}

	private decimal _probeVolume = 1m;

	/// <summary>Base-token amount used for bid and ask quote probes.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.VolumeKey,
		Description = LocalizedStrings.VolumeKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 6)]
	public decimal ProbeVolume
	{
		get => _probeVolume;
		set => _probeVolume = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Quote probe volume must be positive.");
	}

	private decimal _slippageTolerance = 0.5m;

	/// <summary>Swap slippage tolerance in percent.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SlippageKey,
		Description = LocalizedStrings.SlippageKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 7)]
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

	/// <summary>Polling interval for quotes, subgraphs, and receipts.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 8)]
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
			.Set(nameof(GraphApiKey), GraphApiKey)
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(Chain), Chain)
			.Set(nameof(RpcEndpoint), RpcEndpoint)
			.Set(nameof(V2Subgraph), V2Subgraph)
			.Set(nameof(V3Subgraph), V3Subgraph)
			.Set(nameof(Markets), Markets)
			.Set(nameof(MaximumDiscoveredPools), MaximumDiscoveredPools)
			.Set(nameof(ProbeVolume), ProbeVolume)
			.Set(nameof(SlippageTolerance), SlippageTolerance)
			.Set(nameof(PollingInterval), PollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		GraphApiKey = storage.GetValue<SecureString>(nameof(GraphApiKey));
		WalletAddress = storage.GetValue<string>(nameof(WalletAddress));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		Chain = storage.GetValue(nameof(Chain), Chain);
		RpcEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(RpcEndpoint), RpcEndpoint));
		V2Subgraph = storage.GetValue<string>(nameof(V2Subgraph));
		V3Subgraph = storage.GetValue(nameof(V3Subgraph), V3Subgraph);
		Markets = storage.GetValue(nameof(Markets), Markets);
		MaximumDiscoveredPools = storage.GetValue(
			nameof(MaximumDiscoveredPools), MaximumDiscoveredPools);
		ProbeVolume = storage.GetValue(nameof(ProbeVolume), ProbeVolume);
		SlippageTolerance = storage.GetValue(nameof(SlippageTolerance),
			SlippageTolerance);
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
	}

	private static string NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint?.Trim();
		if (endpoint.IsEmpty())
			return endpoint;
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"https://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": Chain={Chain}, Wallet={WalletAddress}";
}
