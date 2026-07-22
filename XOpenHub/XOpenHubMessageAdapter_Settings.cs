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

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage.Set(nameof(Login), Login).Set(nameof(Password), Password).Set(nameof(IsDemo), IsDemo);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Login = storage.GetValue<string>(nameof(Login));
		Password = storage.GetValue<SecureString>(nameof(Password));
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
	}
}
