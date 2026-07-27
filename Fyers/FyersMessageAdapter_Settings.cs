namespace StockSharp.Fyers;

using System.ComponentModel.DataAnnotations;

/// <summary>The message adapter for FYERS API v3.</summary>
[MediaIcon(Media.MediaNames.fyers)]
[Doc("topics/api/connectors/stock_market/fyers.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.FyersKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.History | MessageAdapterCategories.Candles |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.FX)]
[OrderCondition(typeof(FyersOrderCondition))]
public partial class FyersMessageAdapter : MessageAdapter, ITokenAdapter
{
	private const string _defaultRestEndpoint = "https://api-t1.fyers.in/";
	private const string _defaultInstrumentEndpoint = "https://public.fyers.in/sym_details/";
	private const string _defaultMarketWebSocketEndpoint = "wss://socket.fyers.in/hsm/v1-5/prod";
	private const string _defaultOrderWebSocketEndpoint = "wss://socket.fyers.in/trade/v3";
	private const string _defaultTbtWebSocketEndpoint = "wss://rtsocket-api.fyers.in/versova";

	private static readonly TimeSpan[] _timeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(2),
		TimeSpan.FromMinutes(3),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(20),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(2),
		TimeSpan.FromHours(4),
		TimeSpan.FromDays(1),
	];

	/// <summary>Possible time-frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => _timeFrames;

	/// <summary>FYERS application identifier.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ClientCodeKey,
		Description = LocalizedStrings.FyersClientIdDescKey,
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
		Name = LocalizedStrings.FyersDefaultProductKey,
		Description = LocalizedStrings.FyersDefaultProductDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	public FyersProducts DefaultProduct { get; set; } = FyersProducts.Intraday;

	/// <summary>REST API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestEndpointKey,
		Description = LocalizedStrings.RestApiEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>Instrument master endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.InstrumentEndpointKey,
		Description = LocalizedStrings.BaseInstrumentMasterEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string InstrumentEndpoint { get; set; } = _defaultInstrumentEndpoint;

	/// <summary>Market data WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarketWebSocketEndpointKey,
		Description = LocalizedStrings.MarketDataWebSocketEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string MarketWebSocketEndpoint { get; set; } = _defaultMarketWebSocketEndpoint;

	/// <summary>Order WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OrderWebSocketEndpointKey,
		Description = LocalizedStrings.OrderWebSocketEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string OrderWebSocketEndpoint { get; set; } = _defaultOrderWebSocketEndpoint;

	/// <summary>Tick-by-tick WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TbtWebSocketEndpointKey,
		Description = LocalizedStrings.FallbackTickByTickWebSocketEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string TbtWebSocketEndpoint { get; set; } = _defaultTbtWebSocketEndpoint;

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
			.Set(nameof(TbtWebSocketEndpoint), TbtWebSocketEndpoint);
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
		TbtWebSocketEndpoint = storage.GetValue(nameof(TbtWebSocketEndpoint), TbtWebSocketEndpoint);
	}
}
