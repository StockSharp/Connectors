namespace StockSharp.Fxcm;

/// <summary>The message adapter for the FXCM REST and Socket.IO APIs.</summary>
[MediaIcon(Media.MediaNames.fxcm)]
[Doc("topics/api/connectors/forex/fxcm.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.FxcmKey,
	Description = LocalizedStrings.ForexConnectorKey,
	GroupName = LocalizedStrings.ForexKey)]
[MessageAdapterCategory(MessageAdapterCategories.FX | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.History | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(FxcmOrderCondition))]
public partial class FxcmMessageAdapter : MessageAdapter, ITokenAdapter, IDemoAdapter
{
	private const string _defaultRestEndpoint = "https://api.fxcm.com/";
	private const string _defaultDemoRestEndpoint = "https://api-demo.fxcm.com/";
	private const string _defaultWebSocketEndpoint = "wss://api.fxcm.com/socket.io/";
	private const string _defaultDemoWebSocketEndpoint = "wss://api-demo.fxcm.com/socket.io/";

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccessTokenKey,
		Description = LocalizedStrings.FxcmAccessTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public bool IsDemo { get; set; } = true;

	/// <summary>Production REST API endpoint.</summary>
	[Display(
		Name = "REST endpoint",
		Description = "Production REST API endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>Demo REST API endpoint.</summary>
	[Display(
		Name = "Demo REST endpoint",
		Description = "Demo REST API endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string DemoRestEndpoint { get; set; } = _defaultDemoRestEndpoint;

	/// <summary>Production WebSocket endpoint.</summary>
	[Display(
		Name = "WebSocket endpoint",
		Description = "Production WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string WebSocketEndpoint { get; set; } = _defaultWebSocketEndpoint;

	/// <summary>Demo WebSocket endpoint.</summary>
	[Display(
		Name = "Demo WebSocket endpoint",
		Description = "Demo WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string DemoWebSocketEndpoint { get; set; } = _defaultDemoWebSocketEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(DemoRestEndpoint), DemoRestEndpoint)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint)
			.Set(nameof(DemoWebSocketEndpoint), DemoWebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
		DemoRestEndpoint = storage.GetValue(nameof(DemoRestEndpoint), DemoRestEndpoint);
		WebSocketEndpoint = storage.GetValue(nameof(WebSocketEndpoint), WebSocketEndpoint);
		DemoWebSocketEndpoint = storage.GetValue(nameof(DemoWebSocketEndpoint), DemoWebSocketEndpoint);
	}
}
