namespace StockSharp.WazirX;

/// <summary>
/// The message adapter for the WazirX spot exchange.
/// </summary>
[MediaIcon(Media.MediaNames.wazirx)]
[Doc("topics/api/connectors/crypto_exchanges/wazirx.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.WazirXKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[OrderCondition(typeof(WazirXOrderCondition))]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks |
	MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles |
	MessageAdapterCategories.History |
	MessageAdapterCategories.Transactions)]
public partial class WazirXMessageAdapter : MessageAdapter,
	IKeySecretAdapter
{
	private const string _defaultRestEndpoint =
		"https://api.wazirx.com";
	private const string _defaultWebSocketEndpoint =
		"wss://stream.wazirx.com/stream";
	private long _receiveWindow = 5000;
	private TimeSpan _privatePollingInterval =
		TimeSpan.FromSeconds(30);

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>
	/// REST API root endpoint.
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
	/// WebSocket API endpoint.
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
	/// Validity window for signed requests, in milliseconds.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TimeOutKey,
		Description = LocalizedStrings.TimeOutKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public long ReceiveWindow
	{
		get => _receiveWindow;
		set
		{
			if (value is <= 0 or > 60000)
				throw new ArgumentOutOfRangeException(
					nameof(value), value,
					LocalizedStrings.InvalidValue);
			_receiveWindow = value;
		}
	}

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
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(2),
		TimeSpan.FromHours(4),
		TimeSpan.FromHours(6),
		TimeSpan.FromHours(12),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
	];

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint)
			.Set(nameof(ReceiveWindow), ReceiveWindow)
			.Set(nameof(PrivatePollingInterval),
				PrivatePollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		RestEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(RestEndpoint), RestEndpoint),
			_defaultRestEndpoint,
			"https");
		WebSocketEndpoint = NormalizeEndpoint(
			storage.GetValue(
				nameof(WebSocketEndpoint), WebSocketEndpoint),
			_defaultWebSocketEndpoint,
			"wss");
		ReceiveWindow = storage.GetValue(
			nameof(ReceiveWindow), ReceiveWindow);
		PrivatePollingInterval = storage.GetValue(
			nameof(PrivatePollingInterval),
			PrivatePollingInterval);
		if (PrivatePollingInterval <= TimeSpan.Zero)
			PrivatePollingInterval = TimeSpan.FromSeconds(30);
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

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": Key={Key.ToId()}";
}
