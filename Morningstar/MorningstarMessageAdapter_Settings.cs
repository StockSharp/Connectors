namespace StockSharp.Morningstar;

/// <summary>The message adapter for Morningstar Direct Web Services.</summary>
[MediaIcon(Media.MediaNames.morningstar)]
[Doc("topics/api/connectors/stock_market/morningstar.html")]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MorningstarDwsKey,
	Description = LocalizedStrings.MarketDataConnectorKey, GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Europe |
	MessageAdapterCategories.Asia | MessageAdapterCategories.History |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles | MessageAdapterCategories.Paid)]
public partial class MorningstarMessageAdapter : MessageAdapter, ILoginPasswordAdapter
{
	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LoginKey,
		Description = LocalizedStrings.LoginKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public string Login { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.SecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString Password { get; set; }

	/// <summary>Morningstar regional API endpoint.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.RegionKey,
		Description = LocalizedStrings.RegionKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public MorningstarRegions Region { get; set; }

	/// <summary>Entitled universe source used for security lookup.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SourceKey,
		Description = LocalizedStrings.SourceKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey, Order = 3)]
	public MorningstarInvestmentSources InvestmentSource { get; set; }

	/// <summary>Optional custom universe configured for the Morningstar account.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.NameKey,
		Description = LocalizedStrings.NameKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey, Order = 4)]
	public string Universe { get; set; }

	/// <summary>Identifier type for direct time-series requests.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SecurityIdKey,
		Description = LocalizedStrings.SecurityIdKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey, Order = 5)]
	public MorningstarIdentifierTypes IdentifierType { get; set; }

	/// <summary>Output ISO currency or BASE for the investment's base currency.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CurrencyKey,
		Description = LocalizedStrings.CurrencyKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey, Order = 6)]
	public string Currency { get; set; } = "BASE";

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Login), Login)
			.Set(nameof(Password), Password)
			.Set(nameof(Region), Region)
			.Set(nameof(InvestmentSource), InvestmentSource)
			.Set(nameof(Universe), Universe)
			.Set(nameof(IdentifierType), IdentifierType)
			.Set(nameof(Currency), Currency);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Login = storage.GetValue<string>(nameof(Login));
		Password = storage.GetValue<SecureString>(nameof(Password));
		Region = storage.GetValue(nameof(Region), Region);
		InvestmentSource = storage.GetValue(nameof(InvestmentSource), InvestmentSource);
		Universe = storage.GetValue<string>(nameof(Universe));
		IdentifierType = storage.GetValue(nameof(IdentifierType), IdentifierType);
		Currency = storage.GetValue(nameof(Currency), Currency);
	}
}
