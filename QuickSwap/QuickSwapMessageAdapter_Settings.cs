namespace StockSharp.QuickSwap;

/// <summary>QuickSwap EVM deployments supported by this connector.</summary>
public enum QuickSwapChains
{
	/// <summary>Polygon PoS mainnet.</summary>
	Polygon = 137,
}

/// <summary>The message adapter for QuickSwap v2 and v3 AMM markets.</summary>
[MediaIcon(Media.MediaNames.quickswap)]
[Doc("topics/api/connectors/crypto_exchanges/quickswap.html")]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.QuickSwapKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles | MessageAdapterCategories.History |
	MessageAdapterCategories.Transactions)]
public partial class QuickSwapMessageAdapter : MessageAdapter
{
	private const string _defaultMarkets =
		"V3|0x0d500B1d8E8eF31E21C99d1Db9A6444d3ADf1270|" +
		"0x2791Bca1f2de4661ED88A30C99A7a9449Aa84174;" +
		"V3|0x7ceB23fD6bC0adD59E62ac25578270cFf1b9f619|" +
		"0x2791Bca1f2de4661ED88A30C99A7a9449Aa84174;" +
		"V2|0x8f3Cf7ad23Cd3CaDbD9735AFf958023239c6A063|" +
		"0x2791Bca1f2de4661ED88A30C99A7a9449Aa84174";

	/// <summary>Supported candle intervals.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
		QuickSwapExtensions.TimeFrames;

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

	private QuickSwapChains _chain = QuickSwapChains.Polygon;

	/// <summary>QuickSwap deployment chain.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BoardKey,
		Description = LocalizedStrings.BoardKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	[BasicSetting]
	public QuickSwapChains Chain
	{
		get => _chain;
		set => _chain = Enum.IsDefined(value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Unsupported QuickSwap chain.");
	}

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
	public string V3Subgraph { get; set; }

	/// <summary>
	/// Semicolon-separated <c>version|base token|quote token</c>
	/// definitions.
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
