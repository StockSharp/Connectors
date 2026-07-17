namespace StockSharp.Usmart;

/// <summary>The message adapter for the official uSMART OpenAPI.</summary>
[MediaIcon(Media.MediaNames.usmart)]
[Doc("topics/api/connectors/stock_market/usmart.html")]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.UsmartKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.SingaporeExchangeKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia | MessageAdapterCategories.Stock |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Paid |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Ticks |
	MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Candles |
	MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(UsmartOrderCondition))]
public partial class UsmartMessageAdapter : MessageAdapter, IDemoAdapter
{
	/// <summary>Authentication token issued by uSMART.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.UsmartAccessTokenKey,
		Description = LocalizedStrings.UsmartAccessTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public SecureString AccessToken { get; set; }

	/// <summary>Channel identifier assigned by uSMART.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.UsmartChannelKey,
		Description = LocalizedStrings.UsmartChannelDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public string ChannelId { get; set; }

	/// <summary>PEM-encoded RSA private key assigned to the channel.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.UsmartPrivateKeyKey,
		Description = LocalizedStrings.UsmartPrivateKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	/// <summary>Fund account represented as the StockSharp portfolio.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.UsmartFundAccountKey,
		Description = LocalizedStrings.UsmartFundAccountDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	[BasicSetting]
	public string FundAccount { get; set; }

	/// <summary>Already-encrypted optional trading password.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.UsmartTradePasswordKey,
		Description = LocalizedStrings.UsmartTradePasswordDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	public SecureString EncryptedTradePassword { get; set; }

	/// <summary>Use the official UAT endpoints.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.UsmartDemoKey,
		Description = LocalizedStrings.UsmartDemoDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
	public bool IsDemo { get; set; }

	/// <summary>Default native quote market.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.UsmartMarketKey,
		Description = LocalizedStrings.UsmartMarketDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 6)]
	public string DefaultMarket { get; set; } = "hk";

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(AccessToken), AccessToken)
			.Set(nameof(ChannelId), ChannelId)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(FundAccount), FundAccount)
			.Set(nameof(EncryptedTradePassword), EncryptedTradePassword)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(DefaultMarket), DefaultMarket);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		AccessToken = storage.GetValue<SecureString>(nameof(AccessToken));
		ChannelId = storage.GetValue<string>(nameof(ChannelId));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		FundAccount = storage.GetValue<string>(nameof(FundAccount));
		EncryptedTradePassword = storage.GetValue<SecureString>(nameof(EncryptedTradePassword));
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
		DefaultMarket = storage.GetValue(nameof(DefaultMarket), DefaultMarket);
	}
}
