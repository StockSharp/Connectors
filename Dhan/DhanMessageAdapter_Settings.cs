namespace StockSharp.Dhan;

using System.ComponentModel.DataAnnotations;

/// <summary>The message adapter for DhanHQ API v2.</summary>
[MediaIcon(Media.MediaNames.dhan)]
[Doc("topics/api/connectors/stock_market/dhan.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.DhanKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.History | MessageAdapterCategories.Candles |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.FX)]
[OrderCondition(typeof(DhanOrderCondition))]
public partial class DhanMessageAdapter : MessageAdapter, ITokenAdapter
{
	private const string _defaultRestEndpoint = "https://api.dhan.co/v2/";
	private const string _defaultInstrumentEndpoint = "https://images.dhan.co/api-data/api-scrip-master-detailed.csv";
	private const string _defaultMarketWebSocketEndpoint = "wss://api-feed.dhan.co";
	private const string _defaultOrderWebSocketEndpoint = "wss://api-order-update.dhan.co";
	private const string _defaultDepth20WebSocketEndpoint = "wss://depth-api-feed.dhan.co/twentydepth";
	private const string _defaultDepth200WebSocketEndpoint = "wss://full-depth-api.dhan.co/twohundreddepth";

	private static readonly TimeSpan[] _timeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(25),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
	];

	/// <summary>Possible time-frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => _timeFrames;

	/// <summary>Dhan client identifier.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ClientCodeKey,
		Description = LocalizedStrings.DhanClientIdDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public string ClientId { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Default order product.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DhanDefaultProductKey,
		Description = LocalizedStrings.DhanDefaultProductDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	public DhanProducts DefaultProduct { get; set; } = DhanProducts.Intraday;

	/// <summary>REST API endpoint.</summary>
	[Display(
		Name = "REST endpoint",
		Description = "REST API endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>Instrument master endpoint.</summary>
	[Display(
		Name = "Instrument endpoint",
		Description = "Instrument master endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string InstrumentEndpoint { get; set; } = _defaultInstrumentEndpoint;

	/// <summary>Market data WebSocket endpoint.</summary>
	[Display(
		Name = "Market WebSocket endpoint",
		Description = "Market data WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string MarketWebSocketEndpoint { get; set; } = _defaultMarketWebSocketEndpoint;

	/// <summary>Order WebSocket endpoint.</summary>
	[Display(
		Name = "Order WebSocket endpoint",
		Description = "Order WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string OrderWebSocketEndpoint { get; set; } = _defaultOrderWebSocketEndpoint;

	/// <summary>20-level depth WebSocket endpoint.</summary>
	[Display(
		Name = "Depth 20 WebSocket endpoint",
		Description = "20-level depth WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string Depth20WebSocketEndpoint { get; set; } = _defaultDepth20WebSocketEndpoint;

	/// <summary>200-level depth WebSocket endpoint.</summary>
	[Display(
		Name = "Depth 200 WebSocket endpoint",
		Description = "200-level depth WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string Depth200WebSocketEndpoint { get; set; } = _defaultDepth200WebSocketEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(ClientId), ClientId)
			.Set(nameof(Token), Token)
			.Set(nameof(DefaultProduct), DefaultProduct)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(InstrumentEndpoint), InstrumentEndpoint)
			.Set(nameof(MarketWebSocketEndpoint), MarketWebSocketEndpoint)
			.Set(nameof(OrderWebSocketEndpoint), OrderWebSocketEndpoint)
			.Set(nameof(Depth20WebSocketEndpoint), Depth20WebSocketEndpoint)
			.Set(nameof(Depth200WebSocketEndpoint), Depth200WebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		ClientId = storage.GetValue<string>(nameof(ClientId));
		Token = storage.GetValue<SecureString>(nameof(Token));
		DefaultProduct = storage.GetValue(nameof(DefaultProduct), DefaultProduct);
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
		InstrumentEndpoint = storage.GetValue(nameof(InstrumentEndpoint), InstrumentEndpoint);
		MarketWebSocketEndpoint = storage.GetValue(nameof(MarketWebSocketEndpoint), MarketWebSocketEndpoint);
		OrderWebSocketEndpoint = storage.GetValue(nameof(OrderWebSocketEndpoint), OrderWebSocketEndpoint);
		Depth20WebSocketEndpoint = storage.GetValue(nameof(Depth20WebSocketEndpoint), Depth20WebSocketEndpoint);
		Depth200WebSocketEndpoint = storage.GetValue(nameof(Depth200WebSocketEndpoint), Depth200WebSocketEndpoint);
	}
}
