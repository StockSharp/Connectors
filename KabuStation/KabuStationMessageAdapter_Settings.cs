namespace StockSharp.KabuStation;

/// <summary>The message adapter for Mitsubishi UFJ eSmart kabu Station API.</summary>
[MediaIcon(Media.MediaNames.kabustation)]
[Doc("topics/api/connectors/stock_market/kabu_station.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.KabuStationKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.JapanKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Stock | MessageAdapterCategories.Futures |
	MessageAdapterCategories.Options)]
[OrderCondition(typeof(KabuStationOrderCondition))]
public partial class KabuStationMessageAdapter : MessageAdapter, IDemoAdapter
{
	/// <summary>API password configured in kabu Station.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.KabuStationPasswordDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString ApiPassword { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public bool IsDemo { get; set; } = true;

	/// <summary>Default account type used for orders.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccountKey,
		Description = LocalizedStrings.KabuStationAccountTypeDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 2)]
	public KabuStationAccountTypes DefaultAccountType { get; set; } = KabuStationAccountTypes.Specified;

	/// <summary>Default route for Tokyo-listed stock orders.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ExchangeKey,
		Description = LocalizedStrings.KabuStationOrderExchangeDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 3)]
	public KabuStationExchanges DefaultStockOrderExchange { get; set; } = KabuStationExchanges.Sor;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(ApiPassword), ApiPassword)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(DefaultAccountType), DefaultAccountType)
			.Set(nameof(DefaultStockOrderExchange), DefaultStockOrderExchange);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		ApiPassword = storage.GetValue<SecureString>(nameof(ApiPassword));
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
		DefaultAccountType = storage.GetValue(nameof(DefaultAccountType), DefaultAccountType);
		DefaultStockOrderExchange = storage.GetValue(nameof(DefaultStockOrderExchange), DefaultStockOrderExchange);
	}
}
