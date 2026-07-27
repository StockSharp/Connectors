namespace StockSharp.FivePaisa;

/// <summary>The message adapter for the 5paisa Xstream API.</summary>
[MediaIcon(Media.MediaNames.fivepaisa)]
[Doc("topics/api/connectors/stock_market/fivepaisa.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.FivePaisaKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.History | MessageAdapterCategories.Candles |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.FX)]
[OrderCondition(typeof(FivePaisaOrderCondition))]
public partial class FivePaisaMessageAdapter : MessageAdapter, ITokenAdapter
{
	private const string _defaultRestEndpoint = "https://Openapi.5paisa.com/VendorsAPI/Service1.svc/";
	private const string _defaultHistoryEndpoint = "https://openapi.5paisa.com/";
	private const string _defaultFeedWebSocketEndpoint = "wss://openfeed.5paisa.com/feeds/api/chat";
	private const string _defaultFeedWebSocketAEndpoint = "wss://aopenfeed.5paisa.com/feeds/api/chat";
	private const string _defaultFeedWebSocketBEndpoint = "wss://bopenfeed.5paisa.com/feeds/api/chat";
	private const string _defaultDepthWebSocketEndpoint = "wss://gateway.5paisa.com/openapi/20depth";

	private static readonly TimeSpan[] _timeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
	];

	/// <summary>Possible time-frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => _timeFrames;

	/// <summary>Application key issued by 5paisa.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FivePaisaAppKeyKey,
		Description = LocalizedStrings.FivePaisaAppKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string AppKey { get; set; }

	/// <summary>5paisa demat account client code.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ClientCodeKey,
		Description = LocalizedStrings.FivePaisaClientCodeDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public string ClientCode { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Default order product.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FivePaisaDefaultProductKey,
		Description = LocalizedStrings.FivePaisaDefaultProductDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public FivePaisaProducts DefaultProduct { get; set; } = FivePaisaProducts.Intraday;

	/// <summary>Algorithm identifier registered with the exchange.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FivePaisaAlgoIdKey,
		Description = LocalizedStrings.FivePaisaAlgoIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	public long AlgoId { get; set; }

	/// <summary>Maximum number of streaming reconnect attempts.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FivePaisaReconnectAttemptsKey,
		Description = LocalizedStrings.FivePaisaReconnectAttemptsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public int ReconnectAttempts { get; set; } = 10;

	/// <summary>REST API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestEndpointKey,
		Description = LocalizedStrings.RestApiEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>Historical data endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.HistoryEndpointKey,
		Description = LocalizedStrings.HistoricalDataEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string HistoryEndpoint { get; set; } = _defaultHistoryEndpoint;

	/// <summary>Default routed market-feed WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FeedWebSocketEndpointKey,
		Description = LocalizedStrings.DefaultRoutedMarketFeedWebSocketEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string FeedWebSocketEndpoint { get; set; } = _defaultFeedWebSocketEndpoint;

	/// <summary>Market-feed WebSocket endpoint for redirect server A.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FeedWebSocketAEndpointKey,
		Description = LocalizedStrings.MarketFeedWebSocketEndpointForRedirectServerADescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string FeedWebSocketAEndpoint { get; set; } = _defaultFeedWebSocketAEndpoint;

	/// <summary>Market-feed WebSocket endpoint for redirect server B.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FeedWebSocketBEndpointKey,
		Description = LocalizedStrings.MarketFeedWebSocketEndpointForRedirectServerBDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string FeedWebSocketBEndpoint { get; set; } = _defaultFeedWebSocketBEndpoint;

	/// <summary>Market depth WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DepthWebSocketEndpointKey,
		Description = LocalizedStrings.MarketDepthWebSocketEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string DepthWebSocketEndpoint { get; set; } = _defaultDepthWebSocketEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(AppKey), AppKey)
			.Set(nameof(ClientCode), ClientCode)
			.Set(nameof(Token), Token)
			.Set(nameof(DefaultProduct), DefaultProduct)
			.Set(nameof(AlgoId), AlgoId)
			.Set(nameof(ReconnectAttempts), ReconnectAttempts)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(HistoryEndpoint), HistoryEndpoint)
			.Set(nameof(FeedWebSocketEndpoint), FeedWebSocketEndpoint)
			.Set(nameof(FeedWebSocketAEndpoint), FeedWebSocketAEndpoint)
			.Set(nameof(FeedWebSocketBEndpoint), FeedWebSocketBEndpoint)
			.Set(nameof(DepthWebSocketEndpoint), DepthWebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		AppKey = storage.GetValue<string>(nameof(AppKey));
		ClientCode = storage.GetValue<string>(nameof(ClientCode));
		Token = storage.GetValue<SecureString>(nameof(Token));
		DefaultProduct = storage.GetValue(nameof(DefaultProduct), DefaultProduct);
		AlgoId = storage.GetValue(nameof(AlgoId), AlgoId);
		ReconnectAttempts = storage.GetValue(nameof(ReconnectAttempts), ReconnectAttempts);
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
		HistoryEndpoint = storage.GetValue(nameof(HistoryEndpoint), HistoryEndpoint);
		FeedWebSocketEndpoint = storage.GetValue(nameof(FeedWebSocketEndpoint), FeedWebSocketEndpoint);
		FeedWebSocketAEndpoint = storage.GetValue(nameof(FeedWebSocketAEndpoint), FeedWebSocketAEndpoint);
		FeedWebSocketBEndpoint = storage.GetValue(nameof(FeedWebSocketBEndpoint), FeedWebSocketBEndpoint);
		DepthWebSocketEndpoint = storage.GetValue(nameof(DepthWebSocketEndpoint), DepthWebSocketEndpoint);
	}
}
