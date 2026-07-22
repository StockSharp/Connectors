namespace StockSharp.LsSecurities;

/// <summary>The message adapter for the LS Securities Open API.</summary>
[MediaIcon(Media.MediaNames.lssecurities)]
[Doc("topics/api/connectors/stock_market/ls_securities.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.LsSecuritiesKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.KoreaExchangeKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia | MessageAdapterCategories.Free |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles |
	MessageAdapterCategories.History | MessageAdapterCategories.Stock)]
[OrderCondition(typeof(LsSecuritiesOrderCondition))]
public partial class LsSecuritiesMessageAdapter : MessageAdapter, IDemoAdapter, IKeySecretAdapter
{
	/// <summary>Application key issued by LS Securities.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.LsSecuritiesAppKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <summary>Application secret issued by LS Securities.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.LsSecuritiesAppSecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>Optional account label used as the StockSharp portfolio name.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LsSecuritiesAccountKey,
		Description = LocalizedStrings.LsSecuritiesAccountDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public string Account { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public bool IsDemo { get; set; } = true;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(Account), Account)
			.Set(nameof(IsDemo), IsDemo);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		Account = storage.GetValue<string>(nameof(Account));
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
	}
}
