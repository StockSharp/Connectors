namespace StockSharp.Etoro;

/// <summary>The message adapter for the eToro Public API.</summary>
[MediaIcon(Media.MediaNames.etoro)]
[Doc("topics/api/connectors/stock_market/etoro.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.EtoroKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Free |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles | MessageAdapterCategories.History |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures | MessageAdapterCategories.FX |
	MessageAdapterCategories.Crypto)]
[OrderCondition(typeof(EtoroOrderCondition))]
public partial class EtoroMessageAdapter : MessageAdapter, IDemoAdapter
{
	private const string _defaultRestEndpoint = "https://public-api.etoro.com/";
	private const string _defaultWebSocketEndpoint = "wss://ws.etoro.com/ws";

	/// <summary>Public API key created in eToro Trading settings.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.EtoroApiKeyKey,
		Description = LocalizedStrings.EtoroApiKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString PublicApiKey { get; set; }

	/// <summary>User-specific key created in eToro Trading settings.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.EtoroUserKeyKey,
		Description = LocalizedStrings.EtoroUserKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString UserKey { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public bool IsDemo { get; set; } = true;

	/// <summary>REST API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestEndpointKey,
		Description = LocalizedStrings.RestApiEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketEndpointKey,
		Description = LocalizedStrings.WebSocketEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string WebSocketEndpoint { get; set; } = _defaultWebSocketEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(PublicApiKey), PublicApiKey)
			.Set(nameof(UserKey), UserKey)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		PublicApiKey = storage.GetValue<SecureString>(nameof(PublicApiKey));
		UserKey = storage.GetValue<SecureString>(nameof(UserKey));
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
		WebSocketEndpoint = storage.GetValue(nameof(WebSocketEndpoint), WebSocketEndpoint);
	}
}
