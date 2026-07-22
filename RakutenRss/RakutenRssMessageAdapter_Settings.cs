namespace StockSharp.RakutenRss;

/// <summary>The message adapter for Rakuten Securities MARKETSPEED II RSS.</summary>
[SupportedOSPlatform("windows")]
[MediaIcon(Media.MediaNames.rakuten_rss)]
[Doc("topics/api/connectors/stock_market/rakuten_rss.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.RakutenRssKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.JapanKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia | MessageAdapterCategories.Stock |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Candles | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(RakutenRssOrderCondition))]
public partial class RakutenRssMessageAdapter : MessageAdapter
{
	/// <summary>Portfolio name exposed to StockSharp.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PortfolioKey,
		Description = LocalizedStrings.RakutenRssPortfolioDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string PortfolioName { get; set; } = "Rakuten";

	/// <summary>Show the private Excel automation window.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RakutenRssExcelVisibleKey,
		Description = LocalizedStrings.RakutenRssExcelVisibleDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	public bool IsExcelVisible { get; set; }

	/// <summary>Maximum rows read from account and execution tables.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RakutenRssMaxRowsKey,
		Description = LocalizedStrings.RakutenRssMaxRowsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	public int MaxTableRows { get; set; } = 1000;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(PortfolioName), PortfolioName)
			.Set(nameof(IsExcelVisible), IsExcelVisible)
			.Set(nameof(MaxTableRows), MaxTableRows);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		PortfolioName = storage.GetValue(nameof(PortfolioName), PortfolioName);
		IsExcelVisible = storage.GetValue(nameof(IsExcelVisible), IsExcelVisible);
		MaxTableRows = storage.GetValue(nameof(MaxTableRows), MaxTableRows);
	}
}
