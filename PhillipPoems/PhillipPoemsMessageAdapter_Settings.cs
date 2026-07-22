namespace StockSharp.PhillipPoems;

/// <summary>The message adapter for the official Phillip POEMS API Gateway.</summary>
[MediaIcon(Media.MediaNames.phillip_poems)]
[Doc("topics/api/connectors/stock_market/phillip_poems.html")]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PhillipPoemsKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.SingaporeExchangeKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia | MessageAdapterCategories.Stock |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Paid |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Ticks |
	MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(PhillipPoemsOrderCondition))]
public partial class PhillipPoemsMessageAdapter : MessageAdapter, IKeySecretAdapter, ITokenAdapter, IDemoAdapter
{
	private TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

	/// <summary>OAuth client identifier.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.PhillipPoemsClientIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <summary>OAuth client secret.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.PhillipPoemsClientSecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>Application API key.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PhillipPoemsApiKeyKey,
		Description = LocalizedStrings.PhillipPoemsApiKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public SecureString ApiKey { get; set; }

	/// <summary>OAuth access token.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.PhillipPoemsAccessTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>OAuth refresh token.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PhillipPoemsRefreshTokenKey,
		Description = LocalizedStrings.PhillipPoemsRefreshTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	[BasicSetting]
	public SecureString RefreshToken { get; set; }

	/// <summary>POEMS account number and StockSharp portfolio name.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PhillipPoemsAccountKey,
		Description = LocalizedStrings.PhillipPoemsAccountDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
	[BasicSetting]
	public string AccountNo { get; set; }

	/// <summary>Native stock account type.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PhillipPoemsAccountTypeKey,
		Description = LocalizedStrings.PhillipPoemsAccountTypeDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 6)]
	public string AccountType { get; set; } = "V";

	/// <summary>Session-specific PIN encrypted by Phillip's E2EE library.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PhillipPoemsEncryptedPinKey,
		Description = LocalizedStrings.PhillipPoemsEncryptedPinDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 7)]
	public SecureString EncryptedPin { get; set; }

	/// <summary>Use the POEMS sandbox API Gateway.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.PhillipPoemsDemoDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 8)]
	public bool IsDemo { get; set; }

	/// <summary>Default native market code.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PhillipPoemsMarketKey,
		Description = LocalizedStrings.PhillipPoemsMarketDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 9)]
	public string DefaultMarket { get; set; } = PhillipPoemsExtensions.DefaultMarket;

	/// <summary>Default native exchange code.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PhillipPoemsExchangeKey,
		Description = LocalizedStrings.PhillipPoemsExchangeDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 10)]
	public string DefaultExchange { get; set; } = PhillipPoemsExtensions.DefaultExchange;

	/// <summary>Default settlement currency.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PhillipPoemsCurrencyKey,
		Description = LocalizedStrings.PhillipPoemsCurrencyDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 11)]
	public string DefaultSettlementCurrency { get; set; } = PhillipPoemsExtensions.DefaultCurrency;

	/// <summary>Minimum interval between polling jobs.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PhillipPoemsPollingKey,
		Description = LocalizedStrings.PhillipPoemsPollingDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 12)]
	public TimeSpan PollingInterval
	{
		get => _pollingInterval;
		set => _pollingInterval = value < TimeSpan.FromSeconds(1)
			? TimeSpan.FromSeconds(1) : value;
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(ApiKey), ApiKey)
			.Set(nameof(Token), Token)
			.Set(nameof(RefreshToken), RefreshToken)
			.Set(nameof(AccountNo), AccountNo)
			.Set(nameof(AccountType), AccountType)
			.Set(nameof(EncryptedPin), EncryptedPin)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(DefaultMarket), DefaultMarket)
			.Set(nameof(DefaultExchange), DefaultExchange)
			.Set(nameof(DefaultSettlementCurrency), DefaultSettlementCurrency)
			.Set(nameof(PollingInterval), PollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		ApiKey = storage.GetValue<SecureString>(nameof(ApiKey));
		Token = storage.GetValue<SecureString>(nameof(Token));
		RefreshToken = storage.GetValue<SecureString>(nameof(RefreshToken));
		AccountNo = storage.GetValue<string>(nameof(AccountNo));
		AccountType = storage.GetValue(nameof(AccountType), AccountType);
		EncryptedPin = storage.GetValue<SecureString>(nameof(EncryptedPin));
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
		DefaultMarket = storage.GetValue(nameof(DefaultMarket), DefaultMarket);
		DefaultExchange = storage.GetValue(nameof(DefaultExchange), DefaultExchange);
		DefaultSettlementCurrency = storage.GetValue(nameof(DefaultSettlementCurrency),
			DefaultSettlementCurrency);
		PollingInterval = storage.GetValue(nameof(PollingInterval), PollingInterval);
	}
}
