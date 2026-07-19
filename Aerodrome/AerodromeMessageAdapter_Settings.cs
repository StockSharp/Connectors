namespace StockSharp.Aerodrome;

/// <summary>
/// The message adapter for Aerodrome classic and Slipstream pools on Base.
/// </summary>
[MediaIcon(Media.MediaNames.aerodrome)]
[Doc("topics/api/connectors/crypto_exchanges/aerodrome.html")]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.AerodromeKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles | MessageAdapterCategories.History |
	MessageAdapterCategories.Transactions)]
public partial class AerodromeMessageAdapter : MessageAdapter
{
	private const string _defaultRpcEndpoint = "https://mainnet.base.org";
	private const string _defaultWebSocketEndpoint = "wss://mainnet.base.org";
	private const string _defaultPools =
		"0xcdac0d6c6c59727a65f871236188350531885c43|" +
		"0x4200000000000000000000000000000000000006|" +
		"0x833589fcd6edb6e08f4c7c32d4f71b54bda02913|" +
		"WETH-USDC-VOLATILE;" +
		"0x7f670f78b17dec44d5ef68a48740b6f8849cc2e6|" +
		"0x940181a94a35a4569e4529a3cdfb74e38fd98631|" +
		"0x4200000000000000000000000000000000000006|" +
		"AERO-WETH-VOLATILE;" +
		"0xb2cc224c1c9fee385f8ad6a55b4d94e92359dc59|" +
		"0x4200000000000000000000000000000000000006|" +
		"0x833589fcd6edb6e08f4c7c32d4f71b54bda02913|" +
		"WETH-USDC-CL100";

	/// <summary>Supported candle intervals.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
		AerodromeExtensions.TimeFrames;

	/// <summary>Public wallet address used for balances.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		Description = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Optional private key used to sign on-chain transactions.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	/// <summary>Base HTTP JSON-RPC endpoint.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey, Order = 0)]
	[BasicSetting]
	public string RpcEndpoint { get; set; } = _defaultRpcEndpoint;

	/// <summary>Base WebSocket JSON-RPC endpoint.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey, Order = 1)]
	[BasicSetting]
	public string WebSocketEndpoint { get; set; } =
		_defaultWebSocketEndpoint;

	/// <summary>
	/// Semicolon-separated pool definitions. Each item is a pool address and
	/// may include base address, quote address, and security code.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecuritiesKey,
		Description = LocalizedStrings.SecuritiesKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	public string Pools { get; set; } = _defaultPools;

	private int _historyBlockRange = 5_000;

	/// <summary>Maximum block range requested by one log query.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
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
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		Description = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
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
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.VolumeKey,
		Description = LocalizedStrings.VolumeKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
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
		GroupName = LocalizedStrings.ConnectionKey, Order = 6)]
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
		GroupName = LocalizedStrings.ConnectionKey, Order = 7)]
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
		=> base.ToString() + $": Base, Wallet={WalletAddress}";
}
