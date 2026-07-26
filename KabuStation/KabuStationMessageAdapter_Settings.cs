namespace StockSharp.KabuStation;

/// <summary>The message adapter for Mitsubishi UFJ eSmart kabu Station API.</summary>
[MediaIcon(Media.MediaNames.kabustation)]
[Doc("topics/api/connectors/stock_market/kabu_station.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.KabuStationKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.JapanKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Stock | MessageAdapterCategories.Futures |
	MessageAdapterCategories.Options)]
[OrderCondition(typeof(KabuStationOrderCondition))]
public partial class KabuStationMessageAdapter : MessageAdapter, IDemoAdapter
{
	private const string _defaultRestEndpoint = "http://localhost:18080/kabusapi/";
	private const string _defaultDemoRestEndpoint = "http://localhost:18081/kabusapi/";
	private const string _defaultWebSocketEndpoint = "ws://localhost:18080/kabusapi/websocket";
	private const string _defaultDemoWebSocketEndpoint = "ws://localhost:18081/kabusapi/websocket";

	/// <summary>API password configured in kabu Station.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PasswordKey,
		Description = LocalizedStrings.KabuStationPasswordDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString ApiPassword { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public bool IsDemo { get; set; } = true;

	/// <summary>Default account type used for orders.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccountKey,
		Description = LocalizedStrings.KabuStationAccountTypeDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 2)]
	public KabuStationAccountTypes DefaultAccountType { get; set; } = KabuStationAccountTypes.Specified;

	/// <summary>Default route for Tokyo-listed stock orders.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ExchangeKey,
		Description = LocalizedStrings.KabuStationOrderExchangeDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 3)]
	public KabuStationExchanges DefaultStockOrderExchange { get; set; } = KabuStationExchanges.Sor;

	/// <summary>Production REST API endpoint.</summary>
	[Display(
		Name = "REST endpoint",
		Description = "Production REST API endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 4)]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>Demo REST API endpoint.</summary>
	[Display(
		Name = "Demo REST endpoint",
		Description = "Demo REST API endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 5)]
	public string DemoRestEndpoint { get; set; } = _defaultDemoRestEndpoint;

	/// <summary>Production WebSocket endpoint.</summary>
	[Display(
		Name = "WebSocket endpoint",
		Description = "Production WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 6)]
	public string WebSocketEndpoint { get; set; } = _defaultWebSocketEndpoint;

	/// <summary>Demo WebSocket endpoint.</summary>
	[Display(
		Name = "Demo WebSocket endpoint",
		Description = "Demo WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 7)]
	public string DemoWebSocketEndpoint { get; set; } = _defaultDemoWebSocketEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(ApiPassword), ApiPassword)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(DefaultAccountType), DefaultAccountType)
			.Set(nameof(DefaultStockOrderExchange), DefaultStockOrderExchange)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(DemoRestEndpoint), DemoRestEndpoint)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint)
			.Set(nameof(DemoWebSocketEndpoint), DemoWebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		ApiPassword = storage.GetValue<SecureString>(nameof(ApiPassword));
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
		DefaultAccountType = storage.GetValue(nameof(DefaultAccountType), DefaultAccountType);
		DefaultStockOrderExchange = storage.GetValue(nameof(DefaultStockOrderExchange), DefaultStockOrderExchange);
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
		DemoRestEndpoint = storage.GetValue(nameof(DemoRestEndpoint), DemoRestEndpoint);
		WebSocketEndpoint = storage.GetValue(nameof(WebSocketEndpoint), WebSocketEndpoint);
		DemoWebSocketEndpoint = storage.GetValue(nameof(DemoWebSocketEndpoint), DemoWebSocketEndpoint);
	}
}
