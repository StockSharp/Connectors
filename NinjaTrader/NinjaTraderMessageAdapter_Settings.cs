namespace StockSharp.NinjaTrader;

using System.ComponentModel.DataAnnotations;

using Ecng.ComponentModel;

/// <summary>
/// The message adapter for NinjaTrader API.
/// </summary>
[MediaIcon(Media.MediaNames.ninjatrader)]
[Doc("topics/api/connectors/stock_market/ninjatrader.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.NinjaTraderKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth)]
public partial class NinjaTraderMessageAdapter : MessageAdapter, ILoginPasswordAdapter, IDemoAdapter, IKeySecretAdapter
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

	/// <summary>
	/// API client identifier.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.ClientIdKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <summary>
	/// API client secret.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>
	/// Application identifier.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AppIdKey,
		Description = LocalizedStrings.AppIdKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	[BasicSetting]
	public string AppId { get; set; } = "StockSharp";

	/// <summary>
	/// Application version.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AppVersionKey,
		Description = LocalizedStrings.AppVersionKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	[BasicSetting]
	public string AppVersion { get; set; } = "1.0";

	/// <summary>
	/// Stable device identifier.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DeviceIdKey,
		Description = LocalizedStrings.DeviceIdKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	[BasicSetting]
	public string DeviceId { get; set; } = Guid.NewGuid().ToString();

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoModeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	[BasicSetting]
	public bool IsDemo { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Login), Login)
			.Set(nameof(Password), Password)
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(AppId), AppId)
			.Set(nameof(AppVersion), AppVersion)
			.Set(nameof(DeviceId), DeviceId)
			.Set(nameof(IsDemo), IsDemo);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Login = storage.GetValue<string>(nameof(Login));
		Password = storage.GetValue<SecureString>(nameof(Password));
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		AppId = storage.GetValue(nameof(AppId), AppId);
		AppVersion = storage.GetValue(nameof(AppVersion), AppVersion);
		DeviceId = storage.GetValue(nameof(DeviceId), DeviceId);
		IsDemo = storage.GetValue<bool>(nameof(IsDemo));
	}
}
