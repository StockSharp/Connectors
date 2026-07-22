namespace StockSharp.WooX;

/// <summary>
/// The message adapter for WOO X.
/// </summary>
[MediaIcon(Media.MediaNames.woox)]
[Doc("topics/api/connectors/crypto_exchanges/woox.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.WooXKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles |
	MessageAdapterCategories.History | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(WooXOrderCondition))]
public partial class WooXMessageAdapter : MessageAdapter, IKeySecretAdapter
{
	private const string _defaultRestEndpoint = "https://api.woox.io";
	private const string _defaultHistoricalRestEndpoint = "https://api-pub.woox.io";
	private const string _defaultPublicWsEndpoint = "wss://wss.woox.io/ws/stream";
	private const string _defaultPrivateWsEndpoint = "wss://wss.woox.io/v2/ws/private/stream";

	/// <summary>
	/// Supported candle time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => WooXExtensions.TimeFrames;

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
	/// WOO X application identifier used by WebSocket endpoints.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AppIdKey,
		Description = LocalizedStrings.AppIdKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public string ApplicationId { get; set; }

	/// <summary>
	/// REST API endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>
	/// Historical REST API endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.HistoryKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	[BasicSetting]
	public string HistoricalRestEndpoint { get; set; } = _defaultHistoricalRestEndpoint;

	/// <summary>
	/// Public WebSocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 0)]
	[BasicSetting]
	public string PublicWsEndpoint { get; set; } = _defaultPublicWsEndpoint;

	/// <summary>
	/// Private WebSocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 1)]
	[BasicSetting]
	public string PrivateWsEndpoint { get; set; } = _defaultPrivateWsEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(ApplicationId), ApplicationId)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(HistoricalRestEndpoint), HistoricalRestEndpoint)
			.Set(nameof(PublicWsEndpoint), PublicWsEndpoint)
			.Set(nameof(PrivateWsEndpoint), PrivateWsEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		ApplicationId = storage.GetValue<string>(nameof(ApplicationId));
		RestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(RestEndpoint), RestEndpoint),
			_defaultRestEndpoint, "https");
		HistoricalRestEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(HistoricalRestEndpoint), HistoricalRestEndpoint),
			_defaultHistoricalRestEndpoint, "https");
		PublicWsEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(PublicWsEndpoint), PublicWsEndpoint),
			_defaultPublicWsEndpoint, "wss");
		PrivateWsEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(PrivateWsEndpoint), PrivateWsEndpoint),
			_defaultPrivateWsEndpoint, "wss");
	}

	private static string NormalizeEndpoint(string endpoint, string fallback, string scheme)
	{
		endpoint = endpoint.IsEmpty() ? fallback : endpoint.Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"{scheme}://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": Key={Key.ToId()}, App={ApplicationId}";
}
