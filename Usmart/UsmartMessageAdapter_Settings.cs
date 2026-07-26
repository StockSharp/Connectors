namespace StockSharp.Usmart;

/// <summary>The message adapter for the official uSMART OpenAPI.</summary>
[MediaIcon(Media.MediaNames.usmart)]
[Doc("topics/api/connectors/stock_market/usmart.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.UsmartKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.SingaporeExchangeKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia | MessageAdapterCategories.Stock |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Paid |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Ticks |
	MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Candles |
	MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(UsmartOrderCondition))]
public partial class UsmartMessageAdapter : MessageAdapter, IDemoAdapter, ITokenAdapter
{
	private const string _defaultQuoteEndpoint = "https://open-hz.usmartsg.com:8443/";
	private const string _defaultDemoQuoteEndpoint = "https://open-hz-uat.yxzq.com/";
	private const string _defaultTradeEndpoint = "https://open-jy.yxzq.com/";
	private const string _defaultDemoTradeEndpoint = "http://open-jy-uat.yxzq.com/";
	private const string _defaultWebSocketEndpoint = "wss://open-hz.usmartsg.com:8443/wss/v1";
	private const string _defaultDemoWebSocketEndpoint = "wss://open-hz-uat.yxzq.com/wss/v1";

	/// <summary>Authentication token issued by uSMART.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.UsmartAccessTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Channel identifier assigned by uSMART.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.UsmartChannelKey,
		Description = LocalizedStrings.UsmartChannelDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public string ChannelId { get; set; }

	/// <summary>PEM-encoded RSA private key assigned to the channel.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.UsmartPrivateKeyKey,
		Description = LocalizedStrings.UsmartPrivateKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	/// <summary>Fund account represented as the StockSharp portfolio.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.UsmartFundAccountKey,
		Description = LocalizedStrings.UsmartFundAccountDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public string FundAccount { get; set; }

	/// <summary>Already-encrypted optional trading password.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.UsmartTradePasswordKey,
		Description = LocalizedStrings.UsmartTradePasswordDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public SecureString EncryptedTradePassword { get; set; }

	/// <summary>Use the official UAT endpoints.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.UsmartDemoDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public bool IsDemo { get; set; }

	/// <summary>Default native quote market.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.UsmartMarketKey,
		Description = LocalizedStrings.UsmartMarketDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	public string DefaultMarket { get; set; } = "hk";

	/// <summary>Production quote REST API endpoint.</summary>
	[Display(
		Name = "Quote endpoint",
		Description = "Production quote REST API endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 7)]
	public string QuoteEndpoint { get; set; } = _defaultQuoteEndpoint;

	/// <summary>Demo quote REST API endpoint.</summary>
	[Display(
		Name = "Demo quote endpoint",
		Description = "Demo quote REST API endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 8)]
	public string DemoQuoteEndpoint { get; set; } = _defaultDemoQuoteEndpoint;

	/// <summary>Production trading REST API endpoint.</summary>
	[Display(
		Name = "Trade endpoint",
		Description = "Production trading REST API endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 9)]
	public string TradeEndpoint { get; set; } = _defaultTradeEndpoint;

	/// <summary>Demo trading REST API endpoint.</summary>
	[Display(
		Name = "Demo trade endpoint",
		Description = "Demo trading REST API endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 10)]
	public string DemoTradeEndpoint { get; set; } = _defaultDemoTradeEndpoint;

	/// <summary>Production WebSocket endpoint.</summary>
	[Display(
		Name = "WebSocket endpoint",
		Description = "Production WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 11)]
	public string WebSocketEndpoint { get; set; } = _defaultWebSocketEndpoint;

	/// <summary>Demo WebSocket endpoint.</summary>
	[Display(
		Name = "Demo WebSocket endpoint",
		Description = "Demo WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 12)]
	public string DemoWebSocketEndpoint { get; set; } = _defaultDemoWebSocketEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(ChannelId), ChannelId)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(FundAccount), FundAccount)
			.Set(nameof(EncryptedTradePassword), EncryptedTradePassword)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(DefaultMarket), DefaultMarket)
			.Set(nameof(QuoteEndpoint), QuoteEndpoint)
			.Set(nameof(DemoQuoteEndpoint), DemoQuoteEndpoint)
			.Set(nameof(TradeEndpoint), TradeEndpoint)
			.Set(nameof(DemoTradeEndpoint), DemoTradeEndpoint)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint)
			.Set(nameof(DemoWebSocketEndpoint), DemoWebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		ChannelId = storage.GetValue<string>(nameof(ChannelId));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		FundAccount = storage.GetValue<string>(nameof(FundAccount));
		EncryptedTradePassword = storage.GetValue<SecureString>(nameof(EncryptedTradePassword));
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
		DefaultMarket = storage.GetValue(nameof(DefaultMarket), DefaultMarket);
		QuoteEndpoint = storage.GetValue(nameof(QuoteEndpoint), QuoteEndpoint);
		DemoQuoteEndpoint = storage.GetValue(nameof(DemoQuoteEndpoint), DemoQuoteEndpoint);
		TradeEndpoint = storage.GetValue(nameof(TradeEndpoint), TradeEndpoint);
		DemoTradeEndpoint = storage.GetValue(nameof(DemoTradeEndpoint), DemoTradeEndpoint);
		WebSocketEndpoint = storage.GetValue(nameof(WebSocketEndpoint), WebSocketEndpoint);
		DemoWebSocketEndpoint = storage.GetValue(nameof(DemoWebSocketEndpoint), DemoWebSocketEndpoint);
	}
}
