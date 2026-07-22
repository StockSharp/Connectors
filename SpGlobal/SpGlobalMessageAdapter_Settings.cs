namespace StockSharp.SpGlobal;

/// <summary>The message adapter for S&amp;P Global Commodity Insights API.</summary>
[MediaIcon(Media.MediaNames.spglobal)]
[Doc("topics/api/connectors/stock_market/sp_global_commodity_insights.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.SpGlobalCommodityInsightsKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Europe |
	MessageAdapterCategories.Asia | MessageAdapterCategories.History |
	MessageAdapterCategories.Commodities | MessageAdapterCategories.Futures |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Paid)]
public partial class SpGlobalMessageAdapter : MessageAdapter, ILoginPasswordAdapter, IAddressAdapter<Uri>
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
		Description = LocalizedStrings.SecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Password { get; set; }

	/// <summary>API base address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.AddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public Uri Address { get; set; } = new("https://api.ci.spglobal.com/");

	/// <summary>Optional Market Data Category filter for security lookup.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CategoryKey,
		Description = LocalizedStrings.CategoryKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 3)]
	public string MarketDataCategory { get; set; }

	/// <summary>Optional commodity filter for security lookup.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CommodityKey,
		Description = LocalizedStrings.CommodityKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 4)]
	public string Commodity { get; set; }

	/// <summary>Optional contract type filter for security lookup.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TypeKey,
		Description = LocalizedStrings.TypeKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 5)]
	public string ContractType { get; set; }

	/// <summary>Optional assessment frequency filter for security lookup.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TimeFrameKey,
		Description = LocalizedStrings.TimeFrameKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 6)]
	public string AssessmentFrequency { get; set; }

	/// <summary>Assessment bate code. The commonly used closing assessment is c.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CodeKey,
		Description = LocalizedStrings.CodeKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey,
		Order = 7)]
	public string Bate { get; set; } = "c";

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Login), Login)
			.Set(nameof(Password), Password)
			.Set(nameof(Address), Address)
			.Set(nameof(MarketDataCategory), MarketDataCategory)
			.Set(nameof(Commodity), Commodity)
			.Set(nameof(ContractType), ContractType)
			.Set(nameof(AssessmentFrequency), AssessmentFrequency)
			.Set(nameof(Bate), Bate);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Login = storage.GetValue<string>(nameof(Login));
		Password = storage.GetValue<SecureString>(nameof(Password));
		Address = storage.GetValue(nameof(Address), Address);
		MarketDataCategory = storage.GetValue<string>(nameof(MarketDataCategory));
		Commodity = storage.GetValue<string>(nameof(Commodity));
		ContractType = storage.GetValue<string>(nameof(ContractType));
		AssessmentFrequency = storage.GetValue<string>(nameof(AssessmentFrequency));
		Bate = storage.GetValue(nameof(Bate), Bate);
	}
}
