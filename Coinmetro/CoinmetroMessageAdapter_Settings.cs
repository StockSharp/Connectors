namespace StockSharp.Coinmetro;

/// <summary>
/// The message adapter for the Coinmetro spot exchange.
/// </summary>
[MediaIcon(Media.MediaNames.coinmetro)]
[Doc("topics/api/connectors/crypto_exchanges/coinmetro.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CoinmetroKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks |
	MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles |
	MessageAdapterCategories.History |
	MessageAdapterCategories.Transactions)]
public partial class CoinmetroMessageAdapter : MessageAdapter,
	ITokenAdapter,
	IDemoAdapter
{
	private const string _defaultRestEndpoint =
		"https://api.coinmetro.com";
	private const string _defaultWebSocketEndpoint =
		"wss://api.coinmetro.com/ws";
	private const string _defaultDemoRestEndpoint =
		"https://api.coinmetro.com/open";
	private const string _defaultDemoWebSocketEndpoint =
		"wss://api.coinmetro.com/open/ws";
	private TimeSpan _privatePollingInterval =
		TimeSpan.FromMinutes(1);

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.AccessTokenKey,
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

	/// <summary>
	/// Live REST API root endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string RestEndpoint { get; set; } =
		_defaultRestEndpoint;

	/// <summary>
	/// Live WebSocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 0)]
	[BasicSetting]
	public string WebSocketEndpoint { get; set; } =
		_defaultWebSocketEndpoint;

	/// <summary>
	/// Demo REST API root endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	[BasicSetting]
	public string DemoRestEndpoint { get; set; } =
		_defaultDemoRestEndpoint;

	/// <summary>
	/// Demo WebSocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 1)]
	[BasicSetting]
	public string DemoWebSocketEndpoint { get; set; } =
		_defaultDemoWebSocketEndpoint;

	/// <summary>
	/// Private REST reconciliation interval.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalDataUpdatesKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	[BasicSetting]
	public TimeSpan PrivatePollingInterval
	{
		get => _privatePollingInterval;
		set => _privatePollingInterval = value;
	}

	/// <summary>
	/// Supported historical candle time frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(4),
		TimeSpan.FromDays(1),
	];

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint)
			.Set(nameof(DemoRestEndpoint), DemoRestEndpoint)
			.Set(nameof(DemoWebSocketEndpoint),
				DemoWebSocketEndpoint)
			.Set(nameof(PrivatePollingInterval),
				PrivatePollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
		RestEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(RestEndpoint), RestEndpoint),
			_defaultRestEndpoint,
			"https");
		WebSocketEndpoint = NormalizeEndpoint(
			storage.GetValue(
				nameof(WebSocketEndpoint), WebSocketEndpoint),
			_defaultWebSocketEndpoint,
			"wss");
		DemoRestEndpoint = NormalizeEndpoint(
			storage.GetValue(
				nameof(DemoRestEndpoint), DemoRestEndpoint),
			_defaultDemoRestEndpoint,
			"https");
		DemoWebSocketEndpoint = NormalizeEndpoint(
			storage.GetValue(
				nameof(DemoWebSocketEndpoint),
				DemoWebSocketEndpoint),
			_defaultDemoWebSocketEndpoint,
			"wss");
		PrivatePollingInterval = storage.GetValue(
			nameof(PrivatePollingInterval),
			PrivatePollingInterval);
		if (PrivatePollingInterval <= TimeSpan.Zero)
			PrivatePollingInterval = TimeSpan.FromMinutes(1);
	}

	private static string NormalizeEndpoint(
		string endpoint,
		string fallback,
		string scheme)
	{
		endpoint = endpoint.IsEmpty() ? fallback : endpoint.Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"{scheme}://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}

	private string ActiveRestEndpoint
		=> IsDemo ? DemoRestEndpoint : RestEndpoint;

	private string ActiveWebSocketEndpoint
		=> IsDemo
			? DemoWebSocketEndpoint
			: WebSocketEndpoint;

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() +
			$": Token={Token.ToId()}, Demo={IsDemo}";
}
