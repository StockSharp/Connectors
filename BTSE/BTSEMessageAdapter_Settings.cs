namespace StockSharp.BTSE;

/// <summary>
/// BTSE market sections.
/// </summary>
[DataContract]
[Serializable]
public enum BTSESections
{
	/// <summary>
	/// Spot market.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SpotKey)]
	Spot,

	/// <summary>
	/// Futures and perpetual markets.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FuturesKey)]
	Futures,
}

/// <summary>
/// The message adapter for BTSE.
/// </summary>
[MediaIcon(Media.MediaNames.btse)]
[Doc("topics/api/connectors/crypto_exchanges/btse.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BtseKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles |
	MessageAdapterCategories.History | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(BTSEOrderCondition))]
public partial class BTSEMessageAdapter : MessageAdapter, IKeySecretAdapter
{
	private const string _defaultSpotRestEndpoint = "https://api.btse.com/spot";
	private const string _defaultSpotWsEndpoint = "wss://ws.btse.com/ws/spot";
	private const string _defaultSpotOrderBookWsEndpoint = "wss://ws.btse.com/ws/oss/spot";
	private const string _defaultFuturesRestEndpoint = "https://api.btse.com/futures";
	private const string _defaultFuturesWsEndpoint = "wss://ws.btse.com/ws/futures";
	private const string _defaultFuturesOrderBookWsEndpoint = "wss://ws.btse.com/ws/oss/futures";

	/// <summary>
	/// Supported candle time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => BTSEExtensions.TimeFrames;

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey, GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SecretDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	private IEnumerable<BTSESections> _sections = Enumerator.GetValues<BTSESections>();

	/// <summary>
	/// Enabled market sections.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SectionsKey,
		Description = LocalizedStrings.SectionsDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	[ItemsSource(typeof(BTSESections))]
	public IEnumerable<BTSESections> Sections
	{
		get => _sections;
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			var sections = value.Distinct().ToArray();
			if (sections.Length == 0)
				throw new ArgumentOutOfRangeException(nameof(value));
			_sections = sections;
		}
	}

	/// <summary>
	/// Spot REST API endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey, GroupName = LocalizedStrings.AddressesKey, Order = 0)]
	[BasicSetting]
	public string SpotRestEndpoint { get; set; } = _defaultSpotRestEndpoint;

	/// <summary>
	/// Spot general WebSocket endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey, GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 0)]
	[BasicSetting]
	public string SpotWebSocketEndpoint { get; set; } = _defaultSpotWsEndpoint;

	/// <summary>
	/// Spot order-book WebSocket endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey, GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 1)]
	[BasicSetting]
	public string SpotOrderBookWebSocketEndpoint { get; set; } = _defaultSpotOrderBookWsEndpoint;

	/// <summary>
	/// Futures REST API endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey, GroupName = LocalizedStrings.AddressesKey, Order = 1)]
	[BasicSetting]
	public string FuturesRestEndpoint { get; set; } = _defaultFuturesRestEndpoint;

	/// <summary>
	/// Futures general WebSocket endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey, GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 2)]
	[BasicSetting]
	public string FuturesWebSocketEndpoint { get; set; } = _defaultFuturesWsEndpoint;

	/// <summary>
	/// Futures order-book WebSocket endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey, GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 3)]
	[BasicSetting]
	public string FuturesOrderBookWebSocketEndpoint { get; set; } = _defaultFuturesOrderBookWsEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(Sections), Sections.Select(static section => section.To<string>()).JoinComma())
			.Set(nameof(SpotRestEndpoint), SpotRestEndpoint)
			.Set(nameof(SpotWebSocketEndpoint), SpotWebSocketEndpoint)
			.Set(nameof(SpotOrderBookWebSocketEndpoint), SpotOrderBookWebSocketEndpoint)
			.Set(nameof(FuturesRestEndpoint), FuturesRestEndpoint)
			.Set(nameof(FuturesWebSocketEndpoint), FuturesWebSocketEndpoint)
			.Set(nameof(FuturesOrderBookWebSocketEndpoint), FuturesOrderBookWebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		var sections = storage.GetValue<string>(nameof(Sections));
		Sections = sections.IsEmpty()
			? Enumerator.GetValues<BTSESections>()
			: [.. sections.SplitByComma().Select(static section => section.To<BTSESections>())];
		SpotRestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(SpotRestEndpoint), SpotRestEndpoint),
			_defaultSpotRestEndpoint, "https");
		SpotWebSocketEndpoint = NormalizeEndpoint(storage.GetValue(nameof(SpotWebSocketEndpoint), SpotWebSocketEndpoint),
			_defaultSpotWsEndpoint, "wss");
		SpotOrderBookWebSocketEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(SpotOrderBookWebSocketEndpoint), SpotOrderBookWebSocketEndpoint),
			_defaultSpotOrderBookWsEndpoint, "wss");
		FuturesRestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(FuturesRestEndpoint), FuturesRestEndpoint),
			_defaultFuturesRestEndpoint, "https");
		FuturesWebSocketEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(FuturesWebSocketEndpoint), FuturesWebSocketEndpoint),
			_defaultFuturesWsEndpoint, "wss");
		FuturesOrderBookWebSocketEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(FuturesOrderBookWebSocketEndpoint), FuturesOrderBookWebSocketEndpoint),
			_defaultFuturesOrderBookWsEndpoint, "wss");
	}

	private static string NormalizeEndpoint(string endpoint, string fallback, string scheme)
	{
		endpoint = endpoint.IsEmpty() ? fallback : endpoint.Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"{scheme}://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": Sections={Sections.Select(static section => section.To<string>()).JoinComma()}, Key={Key.ToId()}";
}
