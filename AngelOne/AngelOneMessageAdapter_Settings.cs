namespace StockSharp.AngelOne;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// The message adapter for Angel One SmartAPI.
/// </summary>
[MediaIcon(Media.MediaNames.angelone)]
[Doc("topics/api/connectors/stock_market/angelone.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.AngelOneKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.History | MessageAdapterCategories.Candles |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.FX)]
[OrderCondition(typeof(AngelOneOrderCondition))]
public partial class AngelOneMessageAdapter : MessageAdapter, ILoginPasswordAdapter
{
	private static readonly TimeSpan[] _timeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(3),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
	];

	/// <summary>Possible time-frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => _timeFrames;

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LoginKey,
		Description = LocalizedStrings.AngelOneClientCodeDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string Login { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PinKey,
		Description = LocalizedStrings.AngelOnePinDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Password { get; set; }

	/// <summary>SmartAPI key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AngelOneApiKeyKey,
		Description = LocalizedStrings.AngelOneApiKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString ApiKey { get; set; }

	/// <summary>Base32 TOTP secret displayed below the Angel One QR code.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AngelOneTotpSecretKey,
		Description = LocalizedStrings.AngelOneTotpSecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public SecureString TotpSecret { get; set; }

	/// <summary>Client local IP address sent in SmartAPI headers.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AngelOneLocalIpKey,
		Description = LocalizedStrings.AngelOneLocalIpDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public string ClientLocalIp { get; set; } = "127.0.0.1";

	/// <summary>Registered static public IP address sent in SmartAPI headers.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AngelOnePublicIpKey,
		Description = LocalizedStrings.AngelOnePublicIpDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	[BasicSetting]
	public string ClientPublicIp { get; set; }

	/// <summary>Client MAC address sent in SmartAPI headers.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AngelOneMacAddressKey,
		Description = LocalizedStrings.AngelOneMacAddressDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public string MacAddress { get; set; } = "00:00:00:00:00:00";

	/// <summary>Default order product.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AngelOneDefaultProductKey,
		Description = LocalizedStrings.AngelOneDefaultProductDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	public AngelOneProducts DefaultProduct { get; set; } = AngelOneProducts.Delivery;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Login), Login)
			.Set(nameof(Password), Password)
			.Set(nameof(ApiKey), ApiKey)
			.Set(nameof(TotpSecret), TotpSecret)
			.Set(nameof(ClientLocalIp), ClientLocalIp)
			.Set(nameof(ClientPublicIp), ClientPublicIp)
			.Set(nameof(MacAddress), MacAddress)
			.Set(nameof(DefaultProduct), DefaultProduct);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Login = storage.GetValue<string>(nameof(Login));
		Password = storage.GetValue<SecureString>(nameof(Password));
		ApiKey = storage.GetValue<SecureString>(nameof(ApiKey));
		TotpSecret = storage.GetValue<SecureString>(nameof(TotpSecret));
		ClientLocalIp = storage.GetValue(nameof(ClientLocalIp), ClientLocalIp);
		ClientPublicIp = storage.GetValue(nameof(ClientPublicIp), ClientPublicIp);
		MacAddress = storage.GetValue(nameof(MacAddress), MacAddress);
		DefaultProduct = storage.GetValue(nameof(DefaultProduct), DefaultProduct);
	}
}
