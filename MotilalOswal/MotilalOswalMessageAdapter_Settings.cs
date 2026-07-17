namespace StockSharp.MotilalOswal;

/// <summary>The message adapter for the Motilal Oswal MO API.</summary>
[MediaIcon(Media.MediaNames.motilal_oswal)]
[Doc("topics/api/connectors/stock_market/motilal_oswal.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.MotilalOswalKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.FX | MessageAdapterCategories.Commodities)]
[OrderCondition(typeof(MotilalOswalOrderCondition))]
public partial class MotilalOswalMessageAdapter : MessageAdapter, IKeySecretAdapter, ITokenAdapter, IDemoAdapter
{
	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.MotilalOswalApiKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.MotilalOswalApiSecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MotilalOswalAuthTokenKey,
		Description = LocalizedStrings.MotilalOswalAuthTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Access token returned by the MO API access-token endpoint.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MotilalOswalAccessTokenKey,
		Description = LocalizedStrings.MotilalOswalAccessTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	[BasicSetting]
	public SecureString AccessToken { get; set; }

	/// <summary>Trading account client code.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ClientCodeKey,
		Description = LocalizedStrings.MotilalOswalClientCodeDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	[BasicSetting]
	public string ClientCode { get; set; }

	/// <summary>Local IPv4 address included in API headers.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MotilalOswalLocalIpKey,
		Description = LocalizedStrings.MotilalOswalLocalIpDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
	public string LocalIp { get; set; } = "127.0.0.1";

	/// <summary>Public IPv4 address included in API headers.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MotilalOswalPublicIpKey,
		Description = LocalizedStrings.MotilalOswalPublicIpDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 6)]
	public string PublicIp { get; set; } = "127.0.0.1";

	/// <summary>MAC address included in API headers.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MotilalOswalMacAddressKey,
		Description = LocalizedStrings.MotilalOswalMacAddressDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 7)]
	public string MacAddress { get; set; } = "00:00:00:00:00:00";

	/// <summary>Client code or vendor short name included in API headers.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MotilalOswalVendorInfoKey,
		Description = LocalizedStrings.MotilalOswalVendorInfoDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 8)]
	public string VendorInfo { get; set; }

	/// <summary>Stable identifier of this application installation.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MotilalOswalInstalledAppIdKey,
		Description = LocalizedStrings.MotilalOswalInstalledAppIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 9)]
	public string InstalledAppId { get; set; } = Guid.NewGuid().ToString("D");

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 10)]
	[BasicSetting]
	public bool IsDemo { get; set; }

	/// <summary>Default order product.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MotilalOswalDefaultProductKey,
		Description = LocalizedStrings.MotilalOswalDefaultProductDescKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 11)]
	public MotilalOswalProducts DefaultProduct { get; set; } = MotilalOswalProducts.Normal;

	/// <summary>Exchange-registered algorithm identifier.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MotilalOswalAlgoIdKey,
		Description = LocalizedStrings.MotilalOswalAlgoIdDescKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 12)]
	public string AlgoId { get; set; }

	/// <summary>Maximum number of streaming reconnect attempts.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MotilalOswalReconnectAttemptsKey,
		Description = LocalizedStrings.MotilalOswalReconnectAttemptsDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 13)]
	public int ReconnectAttempts { get; set; } = 10;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(Token), Token)
			.Set(nameof(AccessToken), AccessToken)
			.Set(nameof(ClientCode), ClientCode)
			.Set(nameof(LocalIp), LocalIp)
			.Set(nameof(PublicIp), PublicIp)
			.Set(nameof(MacAddress), MacAddress)
			.Set(nameof(VendorInfo), VendorInfo)
			.Set(nameof(InstalledAppId), InstalledAppId)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(DefaultProduct), DefaultProduct)
			.Set(nameof(AlgoId), AlgoId)
			.Set(nameof(ReconnectAttempts), ReconnectAttempts);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		Token = storage.GetValue<SecureString>(nameof(Token));
		AccessToken = storage.GetValue<SecureString>(nameof(AccessToken));
		ClientCode = storage.GetValue<string>(nameof(ClientCode));
		LocalIp = storage.GetValue(nameof(LocalIp), LocalIp);
		PublicIp = storage.GetValue(nameof(PublicIp), PublicIp);
		MacAddress = storage.GetValue(nameof(MacAddress), MacAddress);
		VendorInfo = storage.GetValue<string>(nameof(VendorInfo));
		InstalledAppId = storage.GetValue(nameof(InstalledAppId), InstalledAppId);
		IsDemo = storage.GetValue<bool>(nameof(IsDemo));
		DefaultProduct = storage.GetValue(nameof(DefaultProduct), DefaultProduct);
		AlgoId = storage.GetValue<string>(nameof(AlgoId));
		ReconnectAttempts = storage.GetValue(nameof(ReconnectAttempts), ReconnectAttempts);
	}
}
