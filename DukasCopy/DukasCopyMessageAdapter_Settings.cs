namespace StockSharp.DukasCopy;

/// <summary>The message adapter for Dukascopy through the official JForex SDK.</summary>
[MediaIcon(Media.MediaNames.dukascopy)]
[Doc("topics/api/connectors/forex/dukascopy.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.DukasCopyKey,
	Description = LocalizedStrings.ForexConnectorKey,
	GroupName = LocalizedStrings.ForexKey)]
[MessageAdapterCategory(MessageAdapterCategories.FX | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.History | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Ticks |
	MessageAdapterCategories.Candles | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(DukasCopyOrderCondition))]
public partial class DukasCopyMessageAdapter : MessageAdapter, IDemoAdapter
{
	/// <summary>JForex account user name.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LoginKey,
		Description = LocalizedStrings.LoginDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public string UserName { get; set; }

	/// <summary>JForex account password.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.PasswordDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString Password { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public bool IsDemo { get; set; } = true;

	/// <summary>Loopback TCP port used by the local JForex bridge.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PortKey,
		Description = LocalizedStrings.ServerDescriptionKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	[BasicSetting]
	public int BridgePort { get; set; } = 27431;

	/// <summary>Optional path to the executable bridge JAR. Empty means an externally managed bridge.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PathKey,
		Description = LocalizedStrings.PathDllDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	[BasicSetting]
	public string BridgeJarPath { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(UserName), UserName)
			.Set(nameof(Password), Password)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(BridgePort), BridgePort)
			.Set(nameof(BridgeJarPath), BridgeJarPath);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		UserName = storage.GetValue<string>(nameof(UserName));
		Password = storage.GetValue<SecureString>(nameof(Password));
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
		BridgePort = storage.GetValue(nameof(BridgePort), BridgePort);
		BridgeJarPath = storage.GetValue<string>(nameof(BridgeJarPath));
	}
}
