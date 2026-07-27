namespace StockSharp.Zerodha;

/// <summary>The message adapter for Zerodha Kite Connect 3.</summary>
[MediaIcon(Media.MediaNames.zerodha)]
[Doc("topics/api/connectors/stock_market/zerodha.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ZerodhaKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.RealTime | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.History | MessageAdapterCategories.Candles | MessageAdapterCategories.Ticks |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Stock |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options | MessageAdapterCategories.FX)]
[OrderCondition(typeof(ZerodhaOrderCondition))]
public partial class ZerodhaMessageAdapter : MessageAdapter, ITokenAdapter, IKeySecretAdapter
{
	private const string _defaultRestEndpoint = "https://api.kite.trade/";
	private const string _defaultWebSocketEndpoint = "wss://ws.kite.trade";

	/// <summary>Kite Connect application API key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.ZerodhaApiKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <summary>Kite Connect application API secret.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.ZerodhaApiSecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	public SecureString Secret { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.ZerodhaAccessTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Short-lived request token returned by the Kite login redirect.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ZerodhaRequestTokenKey,
		Description = LocalizedStrings.ZerodhaRequestTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public SecureString RequestToken { get; set; }

	/// <summary>Default order product.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ZerodhaDefaultProductKey,
		Description = LocalizedStrings.ZerodhaDefaultProductDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public ZerodhaProducts DefaultProduct { get; set; } = ZerodhaProducts.Intraday;

	/// <summary>REST API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestEndpointKey,
		Description = LocalizedStrings.RestApiEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 5)]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketEndpointKey,
		Description = LocalizedStrings.WebSocketEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 6)]
	public string WebSocketEndpoint { get; set; } = _defaultWebSocketEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(Token), Token)
			.Set(nameof(RequestToken), RequestToken)
			.Set(nameof(DefaultProduct), DefaultProduct)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		Token = storage.GetValue<SecureString>(nameof(Token));
		RequestToken = storage.GetValue<SecureString>(nameof(RequestToken));
		DefaultProduct = storage.GetValue(nameof(DefaultProduct), DefaultProduct);
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
		WebSocketEndpoint = storage.GetValue(nameof(WebSocketEndpoint), WebSocketEndpoint);
	}
}
