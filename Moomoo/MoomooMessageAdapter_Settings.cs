namespace StockSharp.Moomoo;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// The message adapter for Moomoo OpenAPI.
/// </summary>
[MediaIcon(Media.MediaNames.moomoo)]
[Doc("topics/api/connectors/stock_market/moomoo.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.MoomooKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Transactions | MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles |
	MessageAdapterCategories.Options | MessageAdapterCategories.Stock | MessageAdapterCategories.Crypto |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth)]
[OrderCondition(typeof(MoomooOrderCondition))]
public partial class MoomooMessageAdapter : MessageAdapter, IDemoAdapter
{
	/// <summary>
	/// Default Moomoo OpenD endpoint.
	/// </summary>
	public static readonly EndPoint DefaultAddress = new IPEndPoint(IPAddress.Loopback, 11111);

	/// <summary>
	/// Moomoo OpenD endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AddressKey, Description = LocalizedStrings.AddressKey + LocalizedStrings.Dot, GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public EndPoint Address { get; set; } = DefaultAddress;

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PasswordKey, Description = LocalizedStrings.PasswordKey + LocalizedStrings.Dot, GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString Password { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DemoKey, Description = LocalizedStrings.DemoKey + LocalizedStrings.Dot, GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public bool IsDemo { get; set; } = true;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Address), Address)
			.Set(nameof(Password), Password)
			.Set(nameof(IsDemo), IsDemo);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Address = storage.GetValue(nameof(Address), Address);
		Password = storage.GetValue<SecureString>(nameof(Password));
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
	}
}
