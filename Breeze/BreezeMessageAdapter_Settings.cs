namespace StockSharp.Breeze;

using System.ComponentModel.DataAnnotations;

/// <summary>The message adapter for ICICI Direct Breeze API.</summary>
[MediaIcon(Media.MediaNames.breeze)]
[Doc("topics/api/connectors/stock_market/breeze.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BreezeKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.History | MessageAdapterCategories.Candles |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures | MessageAdapterCategories.Options)]
[OrderCondition(typeof(BreezeOrderCondition))]
public partial class BreezeMessageAdapter : MessageAdapter, IKeySecretAdapter
{
	private const string _defaultRestEndpoint = "https://api.icicidirect.com/breezeapi/api/v1/";
	private const string _defaultHistoryEndpoint = "https://breezeapi.icicidirect.com/api/v2/historicalcharts";
	private const string _defaultInstrumentEndpoint = "https://directlink.icicidirect.com/MotherAppMaster/SecurityMaster.zip";
	private const string _defaultMarketWebSocketEndpoint = "wss://livestream.icicidirect.com/socket.io/?EIO=4&transport=websocket";
	private const string _defaultOhlcWebSocketEndpoint = "wss://breezeapi.icicidirect.com/ohlcvstream/?EIO=4&transport=websocket";
	private const string _defaultOrderWebSocketEndpoint = "wss://livefeeds.icicidirect.com/socket.io/?EIO=4&transport=websocket";

	private static readonly TimeSpan[] _timeFrames =
	[
		TimeSpan.FromSeconds(1),
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromDays(1),
	];

	/// <summary>Possible time-frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => _timeFrames;

	/// <summary>Breeze application key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.BreezeApiKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <summary>Breeze application secret.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.BreezeSecretKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>Daily API session generated after interactive login.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BreezeApiSessionKey,
		Description = LocalizedStrings.BreezeApiSessionDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString ApiSession { get; set; }

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

	/// <summary>Instrument master endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.InstrumentEndpointKey,
		Description = LocalizedStrings.InstrumentMasterEndpointDescKey,
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

	/// <summary>OHLC WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OhlcWebSocketEndpointKey,
		Description = LocalizedStrings.OhlcWebSocketEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string OhlcWebSocketEndpoint { get; set; } = _defaultOhlcWebSocketEndpoint;

	/// <summary>Order WebSocket endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OrderWebSocketEndpointKey,
		Description = LocalizedStrings.OrderWebSocketEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string OrderWebSocketEndpoint { get; set; } = _defaultOrderWebSocketEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(ApiSession), ApiSession)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(HistoryEndpoint), HistoryEndpoint)
			.Set(nameof(InstrumentEndpoint), InstrumentEndpoint)
			.Set(nameof(MarketWebSocketEndpoint), MarketWebSocketEndpoint)
			.Set(nameof(OhlcWebSocketEndpoint), OhlcWebSocketEndpoint)
			.Set(nameof(OrderWebSocketEndpoint), OrderWebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		ApiSession = storage.GetValue<SecureString>(nameof(ApiSession));
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
		HistoryEndpoint = storage.GetValue(nameof(HistoryEndpoint), HistoryEndpoint);
		InstrumentEndpoint = storage.GetValue(nameof(InstrumentEndpoint), InstrumentEndpoint);
		MarketWebSocketEndpoint = storage.GetValue(nameof(MarketWebSocketEndpoint), MarketWebSocketEndpoint);
		OhlcWebSocketEndpoint = storage.GetValue(nameof(OhlcWebSocketEndpoint), OhlcWebSocketEndpoint);
		OrderWebSocketEndpoint = storage.GetValue(nameof(OrderWebSocketEndpoint), OrderWebSocketEndpoint);
	}
}
