namespace StockSharp.Oanda;

/// <summary>
/// The messages adapter for OANDA.
/// </summary>
[MediaIcon(Media.MediaNames.oanda)]
[Doc("topics/api/connectors/forex/oanda.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.OandaKey,
	Description = LocalizedStrings.ForexConnectorKey,
	GroupName = LocalizedStrings.ForexKey)]
[MessageAdapterCategory(MessageAdapterCategories.FX | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.History | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(OandaOrderCondition))]
public partial class OandaMessageAdapter : MessageAdapter, ITokenAdapter, IDemoAdapter
{
	private const string _defaultRestEndpoint = "https://api-fxtrade.oanda.com";
	private const string _defaultDemoRestEndpoint = "https://api-fxpractice.oanda.com";
	private const string _defaultStreamingEndpoint = "https://stream-fxtrade.oanda.com";
	private const string _defaultDemoStreamingEndpoint = "https://stream-fxpractice.oanda.com";

	/// <summary>
	/// Default value for <see cref="MessageAdapter.HeartbeatInterval"/>.
	/// </summary>
	public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(60);

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public bool IsDemo { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>
	/// Compression.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CompressionKey,
		Description = LocalizedStrings.CompressionKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	public bool UseCompression { get; set; }

	/// <summary>
	/// Write log messages only for transaction stream.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OnlyTransactionsKey,
		Description = LocalizedStrings.OnlyTransactionsLogKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public bool LogOnlyTransactions { get; set; }

	/// <summary>Production REST API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestEndpointKey,
		Description = LocalizedStrings.ProductionRestApiEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>Demo REST API endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoRestEndpointKey,
		Description = LocalizedStrings.DemoRestApiEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string DemoRestEndpoint { get; set; } = _defaultDemoRestEndpoint;

	/// <summary>Production streaming endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StreamingEndpointKey,
		Description = LocalizedStrings.ProductionStreamingEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string StreamingEndpoint { get; set; } = _defaultStreamingEndpoint;

	/// <summary>Demo streaming endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoStreamingEndpointKey,
		Description = LocalizedStrings.DemoStreamingEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string DemoStreamingEndpoint { get; set; } = _defaultDemoStreamingEndpoint;

	private static readonly HashSet<TimeSpan> _timeFrames = new(new[]
	{
		TimeSpan.FromSeconds(5),
		TimeSpan.FromSeconds(10),
		TimeSpan.FromSeconds(15),
		TimeSpan.FromSeconds(30),
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
		TimeSpan.FromHours(6),
		TimeSpan.FromHours(8),
		TimeSpan.FromHours(12),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromTicks(TimeHelper.TicksPerMonth)
	});

	/// <summary>
	/// Possible time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => _timeFrames;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(Token), Token)
			.Set(nameof(UseCompression), UseCompression)
			.Set(nameof(LogOnlyTransactions), LogOnlyTransactions)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(DemoRestEndpoint), DemoRestEndpoint)
			.Set(nameof(StreamingEndpoint), StreamingEndpoint)
			.Set(nameof(DemoStreamingEndpoint), DemoStreamingEndpoint)
			;
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		IsDemo = storage.GetValue<bool>(nameof(IsDemo));
		Token = storage.GetValue<SecureString>(nameof(Token));
		UseCompression = storage.GetValue<bool>(nameof(UseCompression));
		LogOnlyTransactions = storage.GetValue<bool>(nameof(LogOnlyTransactions));
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
		DemoRestEndpoint = storage.GetValue(nameof(DemoRestEndpoint), DemoRestEndpoint);
		StreamingEndpoint = storage.GetValue(nameof(StreamingEndpoint), StreamingEndpoint);
		DemoStreamingEndpoint = storage.GetValue(nameof(DemoStreamingEndpoint), DemoStreamingEndpoint);
	}
}
