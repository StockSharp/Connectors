namespace StockSharp.KotakNeo;

using System.ComponentModel.DataAnnotations;

/// <summary>The message adapter for Kotak Neo Trade API v2.</summary>
[MediaIcon(Media.MediaNames.kotakneo)]
[Doc("topics/api/connectors/stock_market/kotak_neo.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.KotakNeoKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Stock | MessageAdapterCategories.Futures |
	MessageAdapterCategories.Options | MessageAdapterCategories.FX)]
[OrderCondition(typeof(KotakNeoOrderCondition))]
public partial class KotakNeoMessageAdapter : MessageAdapter
{
	/// <summary>Consumer token generated in Kotak Neo.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KotakNeoConsumerKeyKey,
		Description = LocalizedStrings.KotakNeoConsumerKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString ConsumerKey { get; set; }

	/// <summary>Registered mobile number including country code.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KotakNeoMobileNumberKey,
		Description = LocalizedStrings.KotakNeoMobileNumberDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public string MobileNumber { get; set; }

	/// <summary>Kotak Neo unique client code.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KotakNeoUserCodeKey,
		Description = LocalizedStrings.KotakNeoUserCodeDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public string UserCode { get; set; }

	/// <summary>Kotak Neo mobile PIN.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KotakNeoMpinKey,
		Description = LocalizedStrings.KotakNeoMpinDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public SecureString Mpin { get; set; }

	/// <summary>Base32 secret registered for Kotak Neo TOTP.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KotakNeoTotpSecretKey,
		Description = LocalizedStrings.KotakNeoTotpSecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	[BasicSetting]
	public SecureString TotpSecret { get; set; }

	/// <summary>Default order product.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KotakNeoDefaultProductKey,
		Description = LocalizedStrings.KotakNeoDefaultProductDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public KotakNeoProducts DefaultProduct { get; set; } = KotakNeoProducts.Intraday;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(ConsumerKey), ConsumerKey)
			.Set(nameof(MobileNumber), MobileNumber)
			.Set(nameof(UserCode), UserCode)
			.Set(nameof(Mpin), Mpin)
			.Set(nameof(TotpSecret), TotpSecret)
			.Set(nameof(DefaultProduct), DefaultProduct);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		ConsumerKey = storage.GetValue<SecureString>(nameof(ConsumerKey));
		MobileNumber = storage.GetValue<string>(nameof(MobileNumber));
		UserCode = storage.GetValue<string>(nameof(UserCode));
		Mpin = storage.GetValue<SecureString>(nameof(Mpin));
		TotpSecret = storage.GetValue<SecureString>(nameof(TotpSecret));
		DefaultProduct = storage.GetValue(nameof(DefaultProduct), DefaultProduct);
	}
}
