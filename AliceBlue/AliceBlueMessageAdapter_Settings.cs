namespace StockSharp.AliceBlue;

/// <summary>The message adapter for Alice Blue ANT API.</summary>
[MediaIcon(Media.MediaNames.alice_blue)]
[Doc("topics/api/connectors/stock_market/alice_blue.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.AliceBlueKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Transactions | MessageAdapterCategories.History |
	MessageAdapterCategories.Candles | MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Stock | MessageAdapterCategories.Futures |
	MessageAdapterCategories.Options | MessageAdapterCategories.FX | MessageAdapterCategories.Commodities)]
[OrderCondition(typeof(AliceBlueOrderCondition))]
public partial class AliceBlueMessageAdapter : MessageAdapter, ITokenAdapter
{
	private const string _defaultRestEndpoint = "https://a3.aliceblueonline.com/";
	private const string _defaultInstrumentEndpoint = "https://v2api.aliceblueonline.com/restpy/static/contract_master/V2/";
	private const string _defaultMarketWebSocketEndpoint = "wss://ws1.aliceblueonline.com/NorenWS";
	private const string _defaultOrderWebSocketEndpoint = "wss://a3.aliceblueonline.com/open-api/order-notify/websocket";

	/// <summary>Alice Blue user identifier.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.UserIdKey,
		Description = LocalizedStrings.AliceBlueUserIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string UserId { get; set; }

	/// <summary>Alice Blue trading client identifier.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ClientCodeKey,
		Description = LocalizedStrings.AliceBlueClientIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	public string ClientId { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.AliceBlueSessionTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Stable application device identifier.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AliceBlueDeviceIdKey,
		Description = LocalizedStrings.AliceBlueDeviceIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");

	/// <summary>Default order product.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AliceBlueDefaultProductKey,
		Description = LocalizedStrings.AliceBlueDefaultProductDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 4)]
	public AliceBlueProducts DefaultProduct { get; set; } = AliceBlueProducts.LongTerm;

	/// <summary>Maximum number of streaming reconnect attempts.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AliceBlueReconnectAttemptsKey,
		Description = LocalizedStrings.AliceBlueReconnectAttemptsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public int ReconnectAttempts { get; set; } = 10;

	/// <summary>REST API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestEndpointKey,
		Description = LocalizedStrings.RestApiEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>Instrument master endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.InstrumentEndpointKey,
		Description = LocalizedStrings.InstrumentMasterEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string InstrumentEndpoint { get; set; } = _defaultInstrumentEndpoint;

	/// <summary>Market data WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarketWebSocketEndpointKey,
		Description = LocalizedStrings.MarketDataWebSocketEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string MarketWebSocketEndpoint { get; set; } = _defaultMarketWebSocketEndpoint;

	/// <summary>Order WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OrderWebSocketEndpointKey,
		Description = LocalizedStrings.OrderWebSocketEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string OrderWebSocketEndpoint { get; set; } = _defaultOrderWebSocketEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(UserId), UserId)
			.Set(nameof(ClientId), ClientId)
			.Set(nameof(Token), Token)
			.Set(nameof(DeviceId), DeviceId)
			.Set(nameof(DefaultProduct), DefaultProduct)
			.Set(nameof(ReconnectAttempts), ReconnectAttempts)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(InstrumentEndpoint), InstrumentEndpoint)
			.Set(nameof(MarketWebSocketEndpoint), MarketWebSocketEndpoint)
			.Set(nameof(OrderWebSocketEndpoint), OrderWebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		UserId = storage.GetValue<string>(nameof(UserId));
		ClientId = storage.GetValue<string>(nameof(ClientId));
		Token = storage.GetValue<SecureString>(nameof(Token));
		DeviceId = storage.GetValue(nameof(DeviceId), DeviceId);
		DefaultProduct = storage.GetValue(nameof(DefaultProduct), DefaultProduct);
		ReconnectAttempts = storage.GetValue(nameof(ReconnectAttempts), ReconnectAttempts);
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
		InstrumentEndpoint = storage.GetValue(nameof(InstrumentEndpoint), InstrumentEndpoint);
		MarketWebSocketEndpoint = storage.GetValue(nameof(MarketWebSocketEndpoint), MarketWebSocketEndpoint);
		OrderWebSocketEndpoint = storage.GetValue(nameof(OrderWebSocketEndpoint), OrderWebSocketEndpoint);
	}
}
