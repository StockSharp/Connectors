namespace StockSharp.IG;

/// <summary>The message adapter for IG Markets REST and streaming APIs.</summary>
[MediaIcon(Media.MediaNames.ig)]
[Doc("topics/api/connectors/stock_market/ig.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.IgMarketsKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.RealTime | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Candles | MessageAdapterCategories.History | MessageAdapterCategories.Stock |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options)]
[OrderCondition(typeof(IgOrderCondition))]
public partial class IgMessageAdapter : MessageAdapter, ILoginPasswordAdapter
{
	/// <summary>IG application API key.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.IgApiKeyKey,
		Description = LocalizedStrings.IgApiKeyDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public string ApiKey { get; set; }

	/// <summary>IG login identifier.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LoginKey,
		Description = LocalizedStrings.LoginDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public string Login { get; set; }

	/// <summary>IG account password.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.PasswordDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public SecureString Password { get; set; }

	/// <summary>Optional IG account identifier.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.IgAccountKey,
		Description = LocalizedStrings.IgAccountDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	public string AccountId { get; set; }

	/// <summary>IG API environment.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.IgEnvironmentKey,
		Description = LocalizedStrings.IgEnvironmentDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	public IgEnvironments Environment { get; set; } = IgEnvironments.Demo;

	/// <summary>Encrypt the password with IG's current RSA key before login.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.IgEncryptPasswordKey,
		Description = LocalizedStrings.IgEncryptPasswordDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
	public bool EncryptPassword { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(ApiKey), ApiKey)
			.Set(nameof(Login), Login)
			.Set(nameof(Password), Password)
			.Set(nameof(AccountId), AccountId)
			.Set(nameof(Environment), Environment)
			.Set(nameof(EncryptPassword), EncryptPassword);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		ApiKey = storage.GetValue<string>(nameof(ApiKey));
		Login = storage.GetValue<string>(nameof(Login));
		Password = storage.GetValue<SecureString>(nameof(Password));
		AccountId = storage.GetValue<string>(nameof(AccountId));
		Environment = storage.GetValue(nameof(Environment), Environment);
		EncryptPassword = storage.GetValue(nameof(EncryptPassword), EncryptPassword);
	}
}
