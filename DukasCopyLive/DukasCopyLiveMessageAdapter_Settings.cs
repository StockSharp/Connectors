namespace StockSharp.DukasCopyLive;

/// <summary>The message adapter for Dukascopy through the official JForex SDK.</summary>
[MediaIcon(Media.MediaNames.dukascopy)]
[Doc("topics/api/connectors/forex/dukascopy_live.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.DukasCopyLiveKey,
	Description = LocalizedStrings.ForexConnectorKey,
	GroupName = LocalizedStrings.ForexKey)]
[MessageAdapterCategory(MessageAdapterCategories.FX | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.History | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Ticks |
	MessageAdapterCategories.Candles | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(DukasCopyLiveOrderCondition))]
public partial class DukasCopyLiveMessageAdapter : MessageAdapter, ILoginPasswordAdapter, IDemoAdapter
{
	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LoginKey,
		Description = LocalizedStrings.LoginDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string Login { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.PasswordDescKey,
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

	/// <summary>Loopback TCP port used by the local JForex bridge.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PortKey,
		Description = LocalizedStrings.ServerDescriptionKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public int BridgePort { get; set; } = 27431;

	/// <summary>Optional path to the executable bridge JAR. Empty means an externally managed bridge.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PathKey,
		Description = LocalizedStrings.PathDllDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	[BasicSetting]
	public string BridgeJarPath { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Login), Login)
			.Set(nameof(Password), Password)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(BridgePort), BridgePort)
			.Set(nameof(BridgeJarPath), BridgeJarPath);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Login = storage.GetValue<string>(nameof(Login));

		if (Login.IsEmpty())
			Login = storage.GetValue<string>("UserName");

		Password = storage.GetValue<SecureString>(nameof(Password));
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
		BridgePort = storage.GetValue(nameof(BridgePort), BridgePort);
		BridgeJarPath = storage.GetValue<string>(nameof(BridgeJarPath));
	}
}
