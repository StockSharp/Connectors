namespace StockSharp.Upstox;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// The message adapter for Upstox API V3.
/// </summary>
[MediaIcon(Media.MediaNames.upstox)]
[Doc("topics/api/connectors/stock_market/upstox.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.UpstoxKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.History | MessageAdapterCategories.Candles |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures | MessageAdapterCategories.Options | MessageAdapterCategories.FX)]
[OrderCondition(typeof(UpstoxOrderCondition))]
public partial class UpstoxMessageAdapter : MessageAdapter, ITokenAdapter, IDemoAdapter
{
	private const string _defaultRestEndpoint = "https://api.upstox.com";
	private const string _defaultInstrumentEndpoint = "https://assets.upstox.com/market-quote/instruments/exchange/complete.json.gz";
	private const string _defaultOrderEndpoint = "https://api-hft.upstox.com";
	private const string _defaultDemoOrderEndpoint = "https://api-sandbox.upstox.com";

	private static readonly TimeSpan[] _timeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(2),
		TimeSpan.FromMinutes(3),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(2),
		TimeSpan.FromHours(3),
		TimeSpan.FromHours(4),
		TimeSpan.FromHours(5),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromTicks(TimeHelper.TicksPerMonth),
	];

	/// <summary>Possible time-frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => _timeFrames;

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoModeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public bool IsDemo { get; set; }

	/// <summary>Default order product.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.UpstoxDefaultProductKey,
		Description = LocalizedStrings.UpstoxDefaultProductDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	public UpstoxProducts DefaultProduct { get; set; } = UpstoxProducts.Delivery;

	/// <summary>REST API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestEndpointKey,
		Description = LocalizedStrings.RestApiEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 3)]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>Instrument file endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.InstrumentEndpointKey,
		Description = LocalizedStrings.InstrumentFileEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 4)]
	public string InstrumentEndpoint { get; set; } = _defaultInstrumentEndpoint;

	/// <summary>Production order REST API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OrderEndpointKey,
		Description = LocalizedStrings.ProductionOrderRestApiEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 5)]
	public string OrderEndpoint { get; set; } = _defaultOrderEndpoint;

	/// <summary>Demo order REST API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoOrderEndpointKey,
		Description = LocalizedStrings.DemoOrderRestApiEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 6)]
	public string DemoOrderEndpoint { get; set; } = _defaultDemoOrderEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(DefaultProduct), DefaultProduct)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(InstrumentEndpoint), InstrumentEndpoint)
			.Set(nameof(OrderEndpoint), OrderEndpoint)
			.Set(nameof(DemoOrderEndpoint), DemoOrderEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		IsDemo = storage.GetValue<bool>(nameof(IsDemo));
		DefaultProduct = storage.GetValue(nameof(DefaultProduct), UpstoxProducts.Delivery);
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
		InstrumentEndpoint = storage.GetValue(nameof(InstrumentEndpoint), InstrumentEndpoint);
		OrderEndpoint = storage.GetValue(nameof(OrderEndpoint), OrderEndpoint);
		DemoOrderEndpoint = storage.GetValue(nameof(DemoOrderEndpoint), DemoOrderEndpoint);
	}
}
