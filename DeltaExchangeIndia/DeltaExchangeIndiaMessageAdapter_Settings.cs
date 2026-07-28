namespace StockSharp.DeltaExchangeIndia;

/// <summary>
/// The message adapter for Delta Exchange India derivatives.
/// </summary>
[MediaIcon(Media.MediaNames.deltaexchangeindia)]
[Doc("topics/api/connectors/crypto_exchanges/delta_exchange_india.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.DeltaExchangeIndiaKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[OrderCondition(typeof(DeltaExchangeIndiaOrderCondition))]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks |
	MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles |
	MessageAdapterCategories.History |
	MessageAdapterCategories.Transactions)]
public partial class DeltaExchangeIndiaMessageAdapter :
	MessageAdapter,
	IKeySecretAdapter
{
	private const string _defaultRestEndpoint =
		"https://api.india.delta.exchange";
	private const string _defaultPublicWebSocketEndpoint =
		"wss://public-socket.india.delta.exchange";
	private const string _defaultPrivateWebSocketEndpoint =
		"wss://socket.india.delta.exchange";
	private TimeSpan _privatePollingInterval =
		TimeSpan.FromSeconds(10);

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
	/// Public WebSocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PublicKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 0)]
	[BasicSetting]
	public string PublicWebSocketEndpoint { get; set; } =
		_defaultPublicWebSocketEndpoint;

	/// <summary>
	/// Private WebSocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 1)]
	[BasicSetting]
	public string PrivateWebSocketEndpoint { get; set; } =
		_defaultPrivateWebSocketEndpoint;

	/// <summary>
	/// Interval for private REST reconciliation.
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
	/// Supported candle time frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames
		=> DeltaExchangeIndiaExtensions.TimeFrames;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(PublicWebSocketEndpoint),
				PublicWebSocketEndpoint)
			.Set(nameof(PrivateWebSocketEndpoint),
				PrivateWebSocketEndpoint)
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
		PublicWebSocketEndpoint = NormalizeEndpoint(
			storage.GetValue(
				nameof(PublicWebSocketEndpoint),
				PublicWebSocketEndpoint),
			_defaultPublicWebSocketEndpoint,
			"wss");
		PrivateWebSocketEndpoint = NormalizeEndpoint(
			storage.GetValue(
				nameof(PrivateWebSocketEndpoint),
				PrivateWebSocketEndpoint),
			_defaultPrivateWebSocketEndpoint,
			"wss");
		PrivatePollingInterval = storage.GetValue(
			nameof(PrivatePollingInterval),
			PrivatePollingInterval);
		if (PrivatePollingInterval <= TimeSpan.Zero)
			PrivatePollingInterval = TimeSpan.FromSeconds(10);
	}

	private static string NormalizeEndpoint(
		string endpoint,
		string fallback,
		string scheme)
	{
		endpoint = endpoint.IsEmpty()
			? fallback
			: endpoint.Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint =
				$"{scheme}://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": Key={Key.ToId()}";
}
