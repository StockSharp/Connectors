namespace StockSharp.Coincall;

/// <summary>
/// The message adapter for Coincall options and futures.
/// </summary>
[MediaIcon(Media.MediaNames.coincall)]
[Doc("topics/api/connectors/crypto_exchanges/coincall.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CoincallKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[OrderCondition(typeof(CoincallOrderCondition))]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks |
	MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles |
	MessageAdapterCategories.History |
	MessageAdapterCategories.Transactions)]
public partial class CoincallMessageAdapter :
	MessageAdapter,
	IKeySecretAdapter
{
	private const string _defaultRestEndpoint =
		"https://api.coincall.com";
	private const string _defaultOptionsWebSocketEndpoint =
		"wss://ws.coincall.com/options";
	private const string _defaultFuturesWebSocketEndpoint =
		"wss://ws.coincall.com/futures";
	private TimeSpan _requestValidityWindow =
		TimeSpan.FromSeconds(5);
	private TimeSpan _privatePollingInterval =
		TimeSpan.FromSeconds(10);

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
	/// Derivatives product.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TypeKey,
		Description = LocalizedStrings.TypeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public CoincallProductTypes ProductType { get; set; } =
		CoincallProductTypes.Options;

	/// <summary>
	/// REST API root endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string RestEndpoint { get; set; } =
		_defaultRestEndpoint;

	/// <summary>
	/// Options WebSocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OptionsKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 0)]
	[BasicSetting]
	public string OptionsWebSocketEndpoint { get; set; } =
		_defaultOptionsWebSocketEndpoint;

	/// <summary>
	/// Futures WebSocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FuturesKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 1)]
	[BasicSetting]
	public string FuturesWebSocketEndpoint { get; set; } =
		_defaultFuturesWebSocketEndpoint;

	/// <summary>
	/// Maximum accepted REST request age.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RequestTimeoutKey,
		Description = LocalizedStrings.RequestTimeoutKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	[BasicSetting]
	public TimeSpan RequestValidityWindow
	{
		get => _requestValidityWindow;
		set => _requestValidityWindow = value;
	}

	/// <summary>
	/// Private REST reconciliation interval.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalDataUpdatesKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 1)]
	[BasicSetting]
	public TimeSpan PrivatePollingInterval
	{
		get => _privatePollingInterval;
		set => _privatePollingInterval = value;
	}

	/// <summary>
	/// Supported candle time frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames
		=> CoincallExtensions.TimeFrames;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(ProductType), ProductType)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(OptionsWebSocketEndpoint),
				OptionsWebSocketEndpoint)
			.Set(nameof(FuturesWebSocketEndpoint),
				FuturesWebSocketEndpoint)
			.Set(nameof(RequestValidityWindow),
				RequestValidityWindow)
			.Set(nameof(PrivatePollingInterval),
				PrivatePollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		ProductType = storage.GetValue(
			nameof(ProductType), ProductType);
		RestEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(RestEndpoint), RestEndpoint),
			_defaultRestEndpoint,
			"https");
		OptionsWebSocketEndpoint = NormalizeEndpoint(
			storage.GetValue(
				nameof(OptionsWebSocketEndpoint),
				OptionsWebSocketEndpoint),
			_defaultOptionsWebSocketEndpoint,
			"wss");
		FuturesWebSocketEndpoint = NormalizeEndpoint(
			storage.GetValue(
				nameof(FuturesWebSocketEndpoint),
				FuturesWebSocketEndpoint),
			_defaultFuturesWebSocketEndpoint,
			"wss");
		RequestValidityWindow = storage.GetValue(
			nameof(RequestValidityWindow),
			RequestValidityWindow);
		if (RequestValidityWindow <= TimeSpan.Zero)
			RequestValidityWindow = TimeSpan.FromSeconds(5);
		PrivatePollingInterval = storage.GetValue(
			nameof(PrivatePollingInterval),
			PrivatePollingInterval);
		if (PrivatePollingInterval <= TimeSpan.Zero)
			PrivatePollingInterval = TimeSpan.FromSeconds(10);
	}

	private static string NormalizeEndpoint(
		string endpoint,
		string fallback,
		string scheme)
	{
		endpoint = endpoint.IsEmpty()
			? fallback
			: endpoint.Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint =
				$"{scheme}://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() +
			$": Product={ProductType}, Key={Key.ToId()}";
}
