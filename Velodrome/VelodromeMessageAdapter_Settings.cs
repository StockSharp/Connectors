namespace StockSharp.Velodrome;

/// <summary>
/// The message adapter for Velodrome classic and Slipstream pools on Optimism.
/// </summary>
[MediaIcon(Media.MediaNames.velodrome)]
[Doc("topics/api/connectors/crypto_exchanges/velodrome.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.VelodromeKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles | MessageAdapterCategories.History |
	MessageAdapterCategories.Transactions)]
public partial class VelodromeMessageAdapter : MessageAdapter
{
	private const string _defaultRpcEndpoint = "https://mainnet.optimism.io";
	private const string _defaultWebSocketEndpoint =
		"wss://mainnet.optimism.io";
	private const string _defaultPools =
		"0xf4f2657ae744354baca871e56775e5083f7276ab|" +
		"0x4200000000000000000000000000000000000006|" +
		"0x0b2c639c533813f4aa9d7837caf62653d097ff85|" +
		"WETH-USDC-VOLATILE;" +
		"0x58e6433a6903886e440ddf519ecc573c4046a6b2|" +
		"0x9560e827af36c94d2ac33a39bce1fe78631088db|" +
		"0x4200000000000000000000000000000000000006|" +
		"VELO-WETH-VOLATILE;" +
		"0x9763639de2eed0ef6bc4dd3a2514526060047c8b|" +
		"0x4200000000000000000000000000000000000006|" +
		"0x0b2c639c533813f4aa9d7837caf62653d097ff85|" +
		"WETH-USDC-CL1";

	/// <summary>Supported candle intervals.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
		VelodromeExtensions.TimeFrames;

	/// <summary>Public wallet address used for balances.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		Description = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Optional private key used to sign on-chain transactions.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	/// <summary>Optimism HTTP JSON-RPC endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string RpcEndpoint { get; set; } = _defaultRpcEndpoint;

	/// <summary>Optimism WebSocket JSON-RPC endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	[BasicSetting]
	public string WebSocketEndpoint { get; set; } =
		_defaultWebSocketEndpoint;

	/// <summary>
	/// Semicolon-separated pool definitions. Each item is a pool address and
	/// may include base address, quote address, and security code.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecuritiesKey,
		Description = LocalizedStrings.SecuritiesKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	public string Pools { get; set; } = _defaultPools;

	private int _historyBlockRange = 5_000;

	/// <summary>Maximum block range requested by one log query.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public int HistoryBlockRange
	{
		get => _historyBlockRange;
		set => _historyBlockRange = value is >= 1 and <= 50_000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"History block range must be between 1 and 50000.");
	}

	private int _historyBlockCount = 250_000;

	/// <summary>
	/// Number of recent blocks searched when history has no start time.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public int HistoryBlockCount
	{
		get => _historyBlockCount;
		set => _historyBlockCount = value is >= 1 and <= 10_000_000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"History block count must be between 1 and 10000000.");
	}

	private decimal _probeVolume = 1m;

	/// <summary>Base-token amount used for bid and ask quote probes.</summary>
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
				"Quote probe volume must be positive.");
	}

	private decimal _slippageTolerance = 0.5m;

	/// <summary>Swap slippage tolerance in percent.</summary>
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

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

	/// <summary>Fallback polling interval for quotes, logs, and receipts.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
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
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(RpcEndpoint), RpcEndpoint)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint)
			.Set(nameof(Pools), Pools)
			.Set(nameof(HistoryBlockRange), HistoryBlockRange)
			.Set(nameof(HistoryBlockCount), HistoryBlockCount)
			.Set(nameof(ProbeVolume), ProbeVolume)
			.Set(nameof(SlippageTolerance), SlippageTolerance)
			.Set(nameof(PollingInterval), PollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		WalletAddress = storage.GetValue<string>(nameof(WalletAddress));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		RpcEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(RpcEndpoint), RpcEndpoint), "https");
		WebSocketEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(WebSocketEndpoint), WebSocketEndpoint), "wss");
		Pools = storage.GetValue(nameof(Pools), Pools);
		HistoryBlockRange = storage.GetValue(nameof(HistoryBlockRange),
			HistoryBlockRange);
		HistoryBlockCount = storage.GetValue(nameof(HistoryBlockCount),
			HistoryBlockCount);
		ProbeVolume = storage.GetValue(nameof(ProbeVolume), ProbeVolume);
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
		=> base.ToString() + $": Optimism, Wallet={WalletAddress}";
}
