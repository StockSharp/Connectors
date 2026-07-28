namespace StockSharp.CoinSwitch;

/// <summary>
/// The message adapter for CoinSwitch PRO spot, perpetual futures
/// and options APIs.
/// </summary>
[MediaIcon(Media.MediaNames.coinswitch)]
[Doc("topics/api/connectors/crypto_exchanges/coinswitch.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CoinSwitchKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Candles |
	MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Ticks |
	MessageAdapterCategories.History |
	MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Futures |
	MessageAdapterCategories.Options)]
[OrderCondition(typeof(CoinSwitchOrderCondition))]
public partial class CoinSwitchMessageAdapter : MessageAdapter,
	IKeySecretAdapter
{
	private const string _defaultRestEndpoint =
		"https://coinswitch.co";
	private const string _defaultHftEndpoint =
		"https://dma.coinswitch.co";
	private const string _defaultWebSocketEndpoint =
		"wss://ws.coinswitch.co";

	/// <summary>
	/// Supported candle time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames
		=> CoinSwitchExtensions.TimeFrames;

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>
	/// API product surface.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TypeKey,
		Description = LocalizedStrings.TypeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public CoinSwitchProductTypes ProductType { get; set; }

	/// <summary>
	/// Spot liquidity venue used by CoinSwitch PRO.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ExchangeKey,
		Description = LocalizedStrings.ExchangeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public string SpotExchange { get; set; } = "coinswitchx";

	/// <summary>
	/// REST API base endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestAddressKey,
		Description = LocalizedStrings.RestEndpointKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string RestEndpoint { get; set; } =
		_defaultRestEndpoint;

	/// <summary>
	/// HFT API base endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.RestEndpointKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	[BasicSetting]
	public string HftEndpoint { get; set; } =
		_defaultHftEndpoint;

	/// <summary>
	/// Public Socket.IO endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketAddressKey,
		Description = LocalizedStrings.WebSocketEndpointKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	[BasicSetting]
	public string WebSocketEndpoint { get; set; } =
		_defaultWebSocketEndpoint;

	/// <summary>
	/// REST polling interval for private state and options data.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PollingIntervalKey,
		Description = LocalizedStrings.PollingIntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public TimeSpan PollingInterval { get; set; } =
		TimeSpan.FromSeconds(10);

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(ProductType), ProductType)
			.Set(nameof(SpotExchange), SpotExchange)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(HftEndpoint), HftEndpoint)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint)
			.Set(nameof(PollingInterval), PollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		ProductType = storage.GetValue(
			nameof(ProductType), ProductType);
		SpotExchange = NormalizeExchange(storage.GetValue(
			nameof(SpotExchange), SpotExchange));
		RestEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(RestEndpoint), RestEndpoint),
			_defaultRestEndpoint,
			"https");
		HftEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(HftEndpoint), HftEndpoint),
			_defaultHftEndpoint,
			"https");
		WebSocketEndpoint = NormalizeEndpoint(
			storage.GetValue(
				nameof(WebSocketEndpoint), WebSocketEndpoint),
			_defaultWebSocketEndpoint,
			"wss");
		PollingInterval = storage.GetValue(
			nameof(PollingInterval), PollingInterval);
	}

	private static string NormalizeEndpoint(
		string endpoint,
		string fallback,
		string scheme)
	{
		endpoint = endpoint.IsEmpty() ? fallback : endpoint.Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"{scheme}://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}

	private static string NormalizeExchange(string exchange)
	{
		exchange = exchange.IsEmpty()
			? "coinswitchx"
			: exchange.Trim().ToLowerInvariant();
		if (exchange is not ("coinswitchx" or "c2c1" or "c2c2"))
			throw new ArgumentOutOfRangeException(
				nameof(exchange),
				exchange,
				"CoinSwitch spot exchange must be coinswitchx, " +
					"c2c1 or c2c2.");
		return exchange;
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() +
			$": Product={ProductType}, Key={Key.ToId()}";
}
