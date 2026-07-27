namespace StockSharp.Fugle;

/// <summary>The message adapter for Fugle Market Data API v1.0.</summary>
[MediaIcon(Media.MediaNames.fugle)]
[Doc("topics/api/connectors/stock_market/fugle.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.FugleKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.TaiwanStockExchangeKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.History | MessageAdapterCategories.Candles |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures | MessageAdapterCategories.Options)]
public partial class FugleMessageAdapter : MessageAdapter, ITokenAdapter
{
	private const string _defaultRestEndpoint = "https://api.fugle.tw/marketdata/v1.0/";
	private const string _defaultStockWebSocketEndpoint = "wss://api.fugle.tw/marketdata/v1.0/stock/streaming";
	private const string _defaultFuturesWebSocketEndpoint = "wss://api.fugle.tw/marketdata/v1.0/futopt/streaming";

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.FugleApiKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Maximum number of streaming reconnect attempts.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FugleReconnectAttemptsKey,
		Description = LocalizedStrings.FugleReconnectAttemptsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	public int ReconnectAttempts { get; set; } = 10;

	/// <summary>REST API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestEndpointKey,
		Description = LocalizedStrings.RestApiEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>Stock WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StockWebSocketEndpointKey,
		Description = LocalizedStrings.StockWebSocketEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string StockWebSocketEndpoint { get; set; } = _defaultStockWebSocketEndpoint;

	/// <summary>Futures and options WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FuturesWebSocketEndpointKey,
		Description = LocalizedStrings.FuturesAndOptionsWebSocketEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string FuturesWebSocketEndpoint { get; set; } = _defaultFuturesWebSocketEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(ReconnectAttempts), ReconnectAttempts)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(StockWebSocketEndpoint), StockWebSocketEndpoint)
			.Set(nameof(FuturesWebSocketEndpoint), FuturesWebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		ReconnectAttempts = storage.GetValue(nameof(ReconnectAttempts), ReconnectAttempts);
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
		StockWebSocketEndpoint = storage.GetValue(nameof(StockWebSocketEndpoint), StockWebSocketEndpoint);
		FuturesWebSocketEndpoint = storage.GetValue(nameof(FuturesWebSocketEndpoint), FuturesWebSocketEndpoint);
	}
}
