namespace StockSharp.Longbridge;

/// <summary>The message adapter for Longbridge OpenAPI.</summary>
[MediaIcon(Media.MediaNames.longbridge)]
[Doc("topics/api/connectors/stock_market/longbridge.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.LongbridgeKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.ChinaKey)]
[MessageAdapterCategory(MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Candles | MessageAdapterCategories.History |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Options)]
[OrderCondition(typeof(LongbridgeOrderCondition))]
public partial class LongbridgeMessageAdapter : MessageAdapter
{
	/// <summary>Longbridge application key or OAuth client identifier.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LongbridgeAppKeyKey,
		Description = LocalizedStrings.LongbridgeAppKeyDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public string AppKey { get; set; }

	/// <summary>Legacy HMAC application secret. Leave empty for OAuth Bearer authentication.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LongbridgeAppSecretKey,
		Description = LocalizedStrings.LongbridgeAppSecretDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString AppSecret { get; set; }

	/// <summary>Longbridge access token or OAuth access token.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LongbridgeAccessTokenKey,
		Description = LocalizedStrings.LongbridgeAccessTokenDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public SecureString AccessToken { get; set; }

	/// <summary>Portfolio name published to StockSharp.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LongbridgePortfolioKey,
		Description = LocalizedStrings.LongbridgePortfolioDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	public string Portfolio { get; set; } = "Longbridge";

	/// <summary>REST API root.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LongbridgeApiUrlKey,
		Description = LocalizedStrings.LongbridgeApiUrlDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	public string ApiUrl { get; set; } = "https://openapi.longbridge.com";

	/// <summary>Quote WebSocket endpoint.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LongbridgeQuoteUrlKey,
		Description = LocalizedStrings.LongbridgeQuoteUrlDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
	public string QuoteUrl { get; set; } = "wss://openapi-quote.longbridge.com/v2";

	/// <summary>Trade WebSocket endpoint.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LongbridgeTradeUrlKey,
		Description = LocalizedStrings.LongbridgeTradeUrlDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 6)]
	public string TradeUrl { get; set; } = "wss://openapi-trade.longbridge.com/v2";

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(AppKey), AppKey)
			.Set(nameof(AppSecret), AppSecret)
			.Set(nameof(AccessToken), AccessToken)
			.Set(nameof(Portfolio), Portfolio)
			.Set(nameof(ApiUrl), ApiUrl)
			.Set(nameof(QuoteUrl), QuoteUrl)
			.Set(nameof(TradeUrl), TradeUrl);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		AppKey = storage.GetValue<string>(nameof(AppKey));
		AppSecret = storage.GetValue<SecureString>(nameof(AppSecret));
		AccessToken = storage.GetValue<SecureString>(nameof(AccessToken));
		Portfolio = storage.GetValue(nameof(Portfolio), Portfolio);
		ApiUrl = storage.GetValue(nameof(ApiUrl), ApiUrl);
		QuoteUrl = storage.GetValue(nameof(QuoteUrl), QuoteUrl);
		TradeUrl = storage.GetValue(nameof(TradeUrl), TradeUrl);
	}
}
