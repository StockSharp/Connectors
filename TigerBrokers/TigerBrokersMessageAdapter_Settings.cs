namespace StockSharp.TigerBrokers;

/// <summary>The message adapter for Tiger Brokers OpenAPI.</summary>
[MediaIcon(Media.MediaNames.tigerbrokers)]
[Doc("topics/api/connectors/stock_market/tiger_brokers.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.TigerBrokersKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.ChinaKey)]
[MessageAdapterCategory(MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Candles | MessageAdapterCategories.Stock |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options | MessageAdapterCategories.FX)]
[OrderCondition(typeof(TigerBrokersOrderCondition))]
public partial class TigerBrokersMessageAdapter : MessageAdapter, ITokenAdapter
{
	/// <summary>Tiger OpenAPI developer identifier.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TigerBrokersTigerIdKey,
		Description = LocalizedStrings.TigerBrokersTigerIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string TigerId { get; set; }

	/// <summary>Default live or paper account identifier.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TigerBrokersAccountKey,
		Description = LocalizedStrings.TigerBrokersAccountDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public string Account { get; set; }

	/// <summary>Broker entity that issued the API credentials.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TigerBrokersLicenseKey,
		Description = LocalizedStrings.TigerBrokersLicenseDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public TigerLicenses License { get; set; } = TigerLicenses.Singapore;

	/// <summary>PKCS#8 RSA private key issued by Tiger OpenAPI.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TigerBrokersPrivateKeyKey,
		Description = LocalizedStrings.TigerBrokersPrivateKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	/// <summary>Optional token required by supported license entities, including TBHK.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TigerBrokersTokenKey,
		Description = LocalizedStrings.TigerBrokersTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public SecureString Token { get; set; }

	/// <summary>Whether the SDK should acquire the account's quote permission on connect.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TigerBrokersAutoGrabPermissionKey,
		Description = LocalizedStrings.TigerBrokersAutoGrabPermissionDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public bool AutoGrabPermission { get; set; } = true;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(TigerId), TigerId)
			.Set(nameof(Account), Account)
			.Set(nameof(License), License)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(Token), Token)
			.Set(nameof(AutoGrabPermission), AutoGrabPermission);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		TigerId = storage.GetValue<string>(nameof(TigerId));
		Account = storage.GetValue<string>(nameof(Account));
		License = storage.GetValue(nameof(License), License);
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		Token = storage.GetValue<SecureString>(nameof(Token));
		AutoGrabPermission = storage.GetValue(nameof(AutoGrabPermission), AutoGrabPermission);
	}
}
