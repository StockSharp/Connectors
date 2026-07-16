namespace StockSharp.CapitalCom;

/// <summary>The message adapter for the Capital.com REST and WebSocket APIs.</summary>
[MediaIcon(Media.MediaNames.capitalcom)]
[Doc("topics/api/connectors/stock_market/capitalcom.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CapitalComKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.EuropeanKey)]
[MessageAdapterCategory(MessageAdapterCategories.RealTime | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles | MessageAdapterCategories.History |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures | MessageAdapterCategories.FX)]
[OrderCondition(typeof(CapitalComOrderCondition))]
public partial class CapitalComMessageAdapter : MessageAdapter, IDemoAdapter
{
	/// <summary>API key generated in Capital.com API integrations.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CapitalComApiKeyKey,
		Description = LocalizedStrings.CapitalComApiKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public string ApiKey { get; set; }

	/// <summary>Capital.com login identifier.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LoginKey,
		Description = LocalizedStrings.CapitalComLoginDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public string Login { get; set; }

	/// <summary>API key custom password.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.CapitalComPasswordDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public SecureString Password { get; set; }

	/// <summary>Optional financial account identifier.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PortfolioNameKey,
		Description = LocalizedStrings.CapitalComAccountIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	public string AccountId { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	public bool IsDemo { get; set; } = true;

	/// <summary>Whether to encrypt the API password before session creation.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CapitalComPasswordEncryptionKey,
		Description = LocalizedStrings.CapitalComPasswordEncryptionDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
	public bool IsPasswordEncryptionEnabled { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(ApiKey), ApiKey)
			.Set(nameof(Login), Login)
			.Set(nameof(Password), Password)
			.Set(nameof(AccountId), AccountId)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(IsPasswordEncryptionEnabled), IsPasswordEncryptionEnabled);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		ApiKey = storage.GetValue<string>(nameof(ApiKey));
		Login = storage.GetValue<string>(nameof(Login));
		Password = storage.GetValue<SecureString>(nameof(Password));
		AccountId = storage.GetValue<string>(nameof(AccountId));
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
		IsPasswordEncryptionEnabled = storage.GetValue(nameof(IsPasswordEncryptionEnabled), IsPasswordEncryptionEnabled);
	}
}
