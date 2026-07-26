namespace StockSharp.Webull;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// The message adapter for Webull OpenAPI.
/// </summary>
[MediaIcon(Media.MediaNames.webull)]
[Doc("topics/api/connectors/stock_market/webull.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.WebullKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.RealTime | MessageAdapterCategories.Transactions | MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Stock)]
public partial class WebullMessageAdapter : MessageAdapter, IKeySecretAdapter, ITokenAdapter, IDemoAdapter
{
	private const string _defaultRestEndpoint = "https://api.webull.com/";
	private const string _defaultDemoRestEndpoint = "https://api.sandbox.webull.com/";
	private const string _defaultMarketDataHost = "data-api.webull.com";
	private const string _defaultDemoMarketDataHost = "data-api.sandbox.webull.com";
	private const string _defaultEventsEndpoint = "https://events-api.webull.com";
	private const string _defaultDemoEventsEndpoint = "https://events-api.sandbox.webull.com";

	/// <inheritdoc />
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <inheritdoc />
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>
	/// Access token.
	/// </summary>
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>
	/// Trading account identifier.
	/// </summary>
	[BasicSetting]
	public string Account { get; set; }

	/// <inheritdoc />
	[BasicSetting]
	public bool IsDemo { get; set; }

	/// <summary>Production REST API endpoint.</summary>
	[Display(
		Name = "REST endpoint",
		Description = "Production REST API endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 5)]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>Demo REST API endpoint.</summary>
	[Display(
		Name = "Demo REST endpoint",
		Description = "Demo REST API endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 6)]
	public string DemoRestEndpoint { get; set; } = _defaultDemoRestEndpoint;

	/// <summary>Production market data host.</summary>
	[Display(
		Name = "Market data host",
		Description = "Production market data host.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 7)]
	public string MarketDataHost { get; set; } = _defaultMarketDataHost;

	/// <summary>Demo market data host.</summary>
	[Display(
		Name = "Demo market data host",
		Description = "Demo market data host.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 8)]
	public string DemoMarketDataHost { get; set; } = _defaultDemoMarketDataHost;

	/// <summary>Market data port.</summary>
	[Display(
		Name = "Market data port",
		Description = "Market data port.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 9)]
	public int MarketDataPort { get; set; } = 1883;

	/// <summary>Production trade events endpoint.</summary>
	[Display(
		Name = "Events endpoint",
		Description = "Production trade events endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 10)]
	public string EventsEndpoint { get; set; } = _defaultEventsEndpoint;

	/// <summary>Demo trade events endpoint.</summary>
	[Display(
		Name = "Demo events endpoint",
		Description = "Demo trade events endpoint.",
		GroupName = LocalizedStrings.AddressesKey,
		Order = 11)]
	public string DemoEventsEndpoint { get; set; } = _defaultDemoEventsEndpoint;

	private Uri BaseAddress => new((IsDemo ? DemoRestEndpoint : RestEndpoint)
		.ThrowIfEmpty(IsDemo ? nameof(DemoRestEndpoint) : nameof(RestEndpoint)));

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(Token), Token)
			.Set(nameof(Account), Account)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(DemoRestEndpoint), DemoRestEndpoint)
			.Set(nameof(MarketDataHost), MarketDataHost)
			.Set(nameof(DemoMarketDataHost), DemoMarketDataHost)
			.Set(nameof(MarketDataPort), MarketDataPort)
			.Set(nameof(EventsEndpoint), EventsEndpoint)
			.Set(nameof(DemoEventsEndpoint), DemoEventsEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		Token = storage.GetValue<SecureString>(nameof(Token));
		Account = storage.GetValue<string>(nameof(Account));
		IsDemo = storage.GetValue<bool>(nameof(IsDemo));
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
		DemoRestEndpoint = storage.GetValue(nameof(DemoRestEndpoint), DemoRestEndpoint);
		MarketDataHost = storage.GetValue(nameof(MarketDataHost), MarketDataHost);
		DemoMarketDataHost = storage.GetValue(nameof(DemoMarketDataHost), DemoMarketDataHost);
		MarketDataPort = storage.GetValue(nameof(MarketDataPort), MarketDataPort);
		EventsEndpoint = storage.GetValue(nameof(EventsEndpoint), EventsEndpoint);
		DemoEventsEndpoint = storage.GetValue(nameof(DemoEventsEndpoint), DemoEventsEndpoint);
	}
}
