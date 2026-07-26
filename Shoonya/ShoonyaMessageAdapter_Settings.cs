namespace StockSharp.Shoonya;

/// <summary>The message adapter for Shoonya API.</summary>
[MediaIcon(Media.MediaNames.shoonya)]
[Doc("topics/api/connectors/stock_market/shoonya.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ShoonyaKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Transactions | MessageAdapterCategories.History |
	MessageAdapterCategories.Candles | MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Stock | MessageAdapterCategories.Futures |
	MessageAdapterCategories.Options | MessageAdapterCategories.FX | MessageAdapterCategories.Commodities)]
[OrderCondition(typeof(ShoonyaOrderCondition))]
public partial class ShoonyaMessageAdapter : MessageAdapter, ITokenAdapter
{
	private const string _defaultRestEndpoint = "https://api.shoonya.com/NorenWClientTP/";
	private const string _defaultInstrumentEndpointTemplate = "https://api.shoonya.com/{0}_symbols.txt.zip";
	private const string _defaultWebSocketEndpoint = "wss://api.shoonya.com/NorenWSTP/";

	/// <summary>Shoonya user identifier.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.UserIdKey,
		Description = LocalizedStrings.ShoonyaUserIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string UserId { get; set; }

	/// <summary>Trading account identifier.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccountKey,
		Description = LocalizedStrings.ShoonyaAccountIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public string AccountId { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.ShoonyaSessionTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Default order product.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ShoonyaDefaultProductKey,
		Description = LocalizedStrings.ShoonyaDefaultProductDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 3)]
	public ShoonyaProducts DefaultProduct { get; set; } = ShoonyaProducts.Delivery;

	/// <summary>Maximum number of streaming reconnect attempts.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ShoonyaReconnectAttemptsKey,
		Description = LocalizedStrings.ShoonyaReconnectAttemptsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public int ReconnectAttempts { get; set; } = 10;

	/// <summary>REST API endpoint.</summary>
	[Display(
		Name = "REST endpoint",
		Description = "REST API endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 5)]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>Instrument file endpoint template.</summary>
	[Display(
		Name = "Instrument endpoint template",
		Description = "Instrument file endpoint template.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 6)]
	public string InstrumentEndpointTemplate { get; set; } = _defaultInstrumentEndpointTemplate;

	/// <summary>WebSocket endpoint.</summary>
	[Display(
		Name = "WebSocket endpoint",
		Description = "WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 7)]
	public string WebSocketEndpoint { get; set; } = _defaultWebSocketEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(UserId), UserId)
			.Set(nameof(AccountId), AccountId)
			.Set(nameof(Token), Token)
			.Set(nameof(DefaultProduct), DefaultProduct)
			.Set(nameof(ReconnectAttempts), ReconnectAttempts)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(InstrumentEndpointTemplate), InstrumentEndpointTemplate)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		UserId = storage.GetValue<string>(nameof(UserId));
		AccountId = storage.GetValue<string>(nameof(AccountId));
		Token = storage.GetValue<SecureString>(nameof(Token));
		DefaultProduct = storage.GetValue(nameof(DefaultProduct), DefaultProduct);
		ReconnectAttempts = storage.GetValue(nameof(ReconnectAttempts), ReconnectAttempts);
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
		InstrumentEndpointTemplate = storage.GetValue(nameof(InstrumentEndpointTemplate), InstrumentEndpointTemplate);
		WebSocketEndpoint = storage.GetValue(nameof(WebSocketEndpoint), WebSocketEndpoint);
	}
}
