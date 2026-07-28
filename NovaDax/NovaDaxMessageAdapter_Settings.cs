namespace StockSharp.NovaDax;

/// <summary>
/// The message adapter for NovaDAX spot markets.
/// </summary>
[MediaIcon(Media.MediaNames.novadax)]
[Doc("topics/api/connectors/crypto_exchanges/novadax.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.NovaDaxKey,
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
[OrderCondition(typeof(NovaDaxOrderCondition))]
public partial class NovaDaxMessageAdapter : MessageAdapter,
	IKeySecretAdapter
{
	private const string _defaultRestEndpoint =
		"https://api.novadax.com";
	private const string _defaultWebSocketEndpoint =
		"wss://api.novadax.com";

	/// <summary>
	/// Supported candle time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames
		=> NovaDaxExtensions.TimeFrames;

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
	/// Optional NovaDAX sub-account identifier.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccountKey,
		Description = LocalizedStrings.AccountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public string AccountId { get; set; }

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
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>
	/// Socket.IO WebSocket root endpoint.
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
	/// Socket.IO Engine.IO protocol version.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.EngineIoVersionKey,
		Description = LocalizedStrings.ProtocolKey,
		GroupName = LocalizedStrings.WebSocketKey,
		Order = 1)]
	[BasicSetting]
	public int EngineIoVersion { get; set; } = 3;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(AccountId), AccountId)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint)
			.Set(nameof(EngineIoVersion), EngineIoVersion);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		AccountId = storage.GetValue<string>(nameof(AccountId));
		RestEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(RestEndpoint), RestEndpoint),
			_defaultRestEndpoint, "https");
		WebSocketEndpoint = NormalizeEndpoint(
			storage.GetValue(
				nameof(WebSocketEndpoint), WebSocketEndpoint),
			_defaultWebSocketEndpoint, "wss");
		EngineIoVersion = storage.GetValue(
			nameof(EngineIoVersion), EngineIoVersion);
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
		=> base.ToString() +
			$": Key={Key.ToId()}, Account={AccountId}";
}
