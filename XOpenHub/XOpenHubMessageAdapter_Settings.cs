namespace StockSharp.XOpenHub;

/// <summary>The message adapter for the X Open Hub xAPI protocol.</summary>
[MediaIcon(Media.MediaNames.xopenhub)]
[Doc("topics/api/connectors/forex/xopenhub.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.XOpenHubKey,
	Description = LocalizedStrings.ForexConnectorKey,
	GroupName = LocalizedStrings.ForexKey)]
[MessageAdapterCategory(MessageAdapterCategories.FX | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.History |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles |
	MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(XOpenHubOrderCondition))]
public partial class XOpenHubMessageAdapter : MessageAdapter, ILoginPasswordAdapter, IDemoAdapter
{
	private const string _defaultCommandEndpoint = "wss://ws.xapi.pro/real";
	private const string _defaultDemoCommandEndpoint = "wss://ws.xapi.pro/demo";
	private const string _defaultStreamEndpoint = "wss://ws.xapi.pro/realStream";
	private const string _defaultDemoStreamEndpoint = "wss://ws.xapi.pro/demoStream";

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LoginKey,
		Description = LocalizedStrings.LoginKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string Login { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.PasswordKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Password { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public bool IsDemo { get; set; } = true;

	/// <summary>Production command WebSocket endpoint.</summary>
	[Display(
		Name = "Command WebSocket endpoint",
		Description = "Production command WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 3)]
	public string CommandEndpoint { get; set; } = _defaultCommandEndpoint;

	/// <summary>Demo command WebSocket endpoint.</summary>
	[Display(
		Name = "Demo command WebSocket endpoint",
		Description = "Demo command WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 4)]
	public string DemoCommandEndpoint { get; set; } = _defaultDemoCommandEndpoint;

	/// <summary>Production stream WebSocket endpoint.</summary>
	[Display(
		Name = "Stream WebSocket endpoint",
		Description = "Production stream WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 5)]
	public string StreamEndpoint { get; set; } = _defaultStreamEndpoint;

	/// <summary>Demo stream WebSocket endpoint.</summary>
	[Display(
		Name = "Demo stream WebSocket endpoint",
		Description = "Demo stream WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 6)]
	public string DemoStreamEndpoint { get; set; } = _defaultDemoStreamEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Login), Login)
			.Set(nameof(Password), Password)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(CommandEndpoint), CommandEndpoint)
			.Set(nameof(DemoCommandEndpoint), DemoCommandEndpoint)
			.Set(nameof(StreamEndpoint), StreamEndpoint)
			.Set(nameof(DemoStreamEndpoint), DemoStreamEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Login = storage.GetValue<string>(nameof(Login));
		Password = storage.GetValue<SecureString>(nameof(Password));
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
		CommandEndpoint = storage.GetValue(nameof(CommandEndpoint), CommandEndpoint);
		DemoCommandEndpoint = storage.GetValue(nameof(DemoCommandEndpoint), DemoCommandEndpoint);
		StreamEndpoint = storage.GetValue(nameof(StreamEndpoint), StreamEndpoint);
		DemoStreamEndpoint = storage.GetValue(nameof(DemoStreamEndpoint), DemoStreamEndpoint);
	}
}
