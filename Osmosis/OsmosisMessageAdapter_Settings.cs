namespace StockSharp.Osmosis;

/// <summary>The message adapter for Osmosis native swaps.</summary>
[MediaIcon(Media.MediaNames.osmosis)]
[Doc("topics/api/connectors/crypto_exchanges/osmosis.html")]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.OsmosisKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Transactions)]
public partial class OsmosisMessageAdapter : MessageAdapter
{
	private const string _defaultSqsEndpoint = "https://sqs.osmosis.zone";
	private const string _defaultLcdEndpoint = "https://lcd.osmosis.zone";
	private const string _defaultRpcEndpoint = "https://rpc.osmosis.zone";
	private const string _defaultStreamingEndpoint =
		"wss://rpc.osmosis.zone/websocket";
	private const string _defaultAssetListEndpoint =
		"https://raw.githubusercontent.com/osmosis-labs/assetlists/main/" +
		"osmosis-1/generated/frontend/assetlist.json";
	private const string _usdc =
		"ibc/498A0751C798A0D9A389AA3691123DADA57DAA4FE165D5C75894505B876BA6E4";
	private const string _atom =
		"ibc/27394FB092D2ECCD56123C74F36E4C1F926001CEADA9CA97EA622B25F41E5EB2";
	private const string _btc =
		"factory/osmo1z6r6qdknhgsc0zeracktgpcxf43j6sekq07nw8sxduc9lg0qjjlqfu25e3/" +
		"alloyed/allBTC";
	private const string _eth =
		"factory/osmo1k6c8jln7ejuqwtqmay3yvzrg3kueaczl96pk067ldg8u835w0yhsw27twm/" +
		"alloyed/allETH";
	private const string _defaultMarkets = "uosmo|" + _usdc + ";" +
		_atom + "|" + _usdc + ";" + _btc + "|" + _usdc + ";" +
		_eth + "|" + _usdc;

	/// <summary>Public Osmosis wallet address used for balances.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		Description = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Optional hexadecimal key used to sign native transactions.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	/// <summary>Osmosis SQS endpoint.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey, Order = 0)]
	[BasicSetting]
	public string SqsEndpoint { get; set; } = _defaultSqsEndpoint;

	/// <summary>Cosmos LCD endpoint.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey, Order = 1)]
	[BasicSetting]
	public string LcdEndpoint { get; set; } = _defaultLcdEndpoint;

	/// <summary>CometBFT HTTP RPC endpoint.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey, Order = 2)]
	[BasicSetting]
	public string RpcEndpoint { get; set; } = _defaultRpcEndpoint;

	/// <summary>CometBFT WebSocket endpoint.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey, Order = 3)]
	[BasicSetting]
	public string StreamingEndpoint { get; set; } = _defaultStreamingEndpoint;

	/// <summary>Official Osmosis asset-list endpoint.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey, Order = 4)]
	public string AssetListEndpoint { get; set; } = _defaultAssetListEndpoint;

	/// <summary>
	/// Semicolon-separated base-denom|quote-denom or entries with an optional
	/// third security-code field.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecuritiesKey,
		Description = LocalizedStrings.SecuritiesKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	public string Markets { get; set; } = _defaultMarkets;

	private decimal _probeVolume = 1m;

	/// <summary>Base-token amount used for executable Level1 quotes.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.VolumeKey,
		Description = LocalizedStrings.VolumeKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	public decimal ProbeVolume
	{
		get => _probeVolume;
		set => _probeVolume = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Quote probe volume must be positive.");
	}

	private decimal _slippageTolerance = 1m;

	/// <summary>Default swap slippage tolerance in percent.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SlippageKey,
		Description = LocalizedStrings.SlippageKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	public decimal SlippageTolerance
	{
		get => _slippageTolerance;
		set => _slippageTolerance = value is >= 0.01m and < 100m &&
			decimal.Round(value, 2) == value
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Slippage tolerance must be at least 0.01 and below 100 percent.");
	}

	private decimal _gasAdjustment = 1.3m;

	/// <summary>Multiplier applied to simulated gas use.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MultiplierKey,
		Description = LocalizedStrings.MultiplierKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
	public decimal GasAdjustment
	{
		get => _gasAdjustment;
		set => _gasAdjustment = value is >= 1m and <= 3m
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Gas adjustment must be between one and three.");
	}

	private decimal _baseFeeMultiplier = 1.1m;

	/// <summary>Multiplier applied to the current EIP base fee.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MultiplierKey,
		Description = LocalizedStrings.MultiplierKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 6)]
	public decimal BaseFeeMultiplier
	{
		get => _baseFeeMultiplier;
		set => _baseFeeMultiplier = value is >= 1m and <= 5m
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Base-fee multiplier must be between one and five.");
	}

	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

	/// <summary>Polling interval for quotes, balances, and receipts.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 7)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval = value >= TimeSpan.FromSeconds(2)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Polling interval cannot be less than two seconds.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(SqsEndpoint), SqsEndpoint)
			.Set(nameof(LcdEndpoint), LcdEndpoint)
			.Set(nameof(RpcEndpoint), RpcEndpoint)
			.Set(nameof(StreamingEndpoint), StreamingEndpoint)
			.Set(nameof(AssetListEndpoint), AssetListEndpoint)
			.Set(nameof(Markets), Markets)
			.Set(nameof(ProbeVolume), ProbeVolume)
			.Set(nameof(SlippageTolerance), SlippageTolerance)
			.Set(nameof(GasAdjustment), GasAdjustment)
			.Set(nameof(BaseFeeMultiplier), BaseFeeMultiplier)
			.Set(nameof(PollingInterval), PollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		WalletAddress = storage.GetValue<string>(nameof(WalletAddress));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		SqsEndpoint = storage.GetValue(nameof(SqsEndpoint), SqsEndpoint);
		LcdEndpoint = storage.GetValue(nameof(LcdEndpoint), LcdEndpoint);
		RpcEndpoint = storage.GetValue(nameof(RpcEndpoint), RpcEndpoint);
		StreamingEndpoint = storage.GetValue(nameof(StreamingEndpoint),
			StreamingEndpoint);
		AssetListEndpoint = storage.GetValue(nameof(AssetListEndpoint),
			AssetListEndpoint);
		Markets = storage.GetValue(nameof(Markets), Markets);
		ProbeVolume = storage.GetValue(nameof(ProbeVolume), ProbeVolume);
		SlippageTolerance = storage.GetValue(nameof(SlippageTolerance),
			SlippageTolerance);
		GasAdjustment = storage.GetValue(nameof(GasAdjustment), GasAdjustment);
		BaseFeeMultiplier = storage.GetValue(nameof(BaseFeeMultiplier),
			BaseFeeMultiplier);
		PollingInterval = storage.GetValue(nameof(PollingInterval),
			PollingInterval);
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": Wallet={WalletAddress}";
}
