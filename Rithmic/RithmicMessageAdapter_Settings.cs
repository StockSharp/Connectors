namespace StockSharp.Rithmic;

using System.ComponentModel.DataAnnotations;
using System.Security;

using Ecng.ComponentModel;
using Ecng.Serialization;

/// <summary>
/// The message adapter for Rithmic Protocol (protobuf/WebSocket).
/// </summary>
[MediaIcon(Media.MediaNames.rithmic)]
[Doc("topics/api/connectors/stock_market/rithmic.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.RithmicKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Transactions | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.Ticks)]
public partial class RithmicMessageAdapter : MessageAdapter, ILoginPasswordAdapter
{
	/// <summary>
	/// User login.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LoginKey,
		Description = LocalizedStrings.LoginKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string Login { get; set; }

	/// <summary>
	/// User password.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.PasswordKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Password { get; set; }

	/// <summary>
	/// Rithmic system name.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SystemKey,
		Description = LocalizedStrings.SystemKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public string SystemName { get; set; }

	/// <summary>
	/// Server address (wss://...).
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.AddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public string ServerAddress { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage
			.Set(nameof(Login), Login)
			.Set(nameof(Password), Password)
			.Set(nameof(SystemName), SystemName)
			.Set(nameof(ServerAddress), ServerAddress);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Login = storage.GetValue<string>(nameof(Login));
		Password = storage.GetValue<SecureString>(nameof(Password));
		SystemName = storage.GetValue<string>(nameof(SystemName));
		ServerAddress = storage.GetValue<string>(nameof(ServerAddress));
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": {Login} @ {SystemName}";
}
