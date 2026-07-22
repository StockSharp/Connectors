namespace StockSharp.CoinDCX;

/// <summary>
/// The message adapter for CoinDCX.
/// </summary>
[MediaIcon(Media.MediaNames.coindcx)]
[Doc("topics/api/connectors/crypto_exchanges/coindcx.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CoinDCXKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks |
	MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.History | MessageAdapterCategories.Transactions)]
public partial class CoinDCXMessageAdapter : MessageAdapter, IKeySecretAdapter
{
	private const string _defaultRestEndpoint = "https://api.coindcx.com";
	private const string _defaultPublicRestEndpoint = "https://public.coindcx.com";
	private const string _defaultWebSocketEndpoint = "wss://stream.coindcx.com";

	/// <summary>
	/// Supported candle time frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => CoinDCXExtensions.TimeFrames;

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
	/// Public market-data REST endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarketDataKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	[BasicSetting]
	public string PublicRestEndpoint { get; set; } = _defaultPublicRestEndpoint;

	/// <summary>
	/// WebSocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 0)]
	[BasicSetting]
	public string WebSocketEndpoint { get; set; } = _defaultWebSocketEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(PublicRestEndpoint), PublicRestEndpoint)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		RestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(RestEndpoint),
			RestEndpoint), _defaultRestEndpoint, "https");
		PublicRestEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(PublicRestEndpoint), PublicRestEndpoint),
			_defaultPublicRestEndpoint, "https");
		WebSocketEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(WebSocketEndpoint), WebSocketEndpoint),
			_defaultWebSocketEndpoint, "wss");
	}

	private static string NormalizeEndpoint(string endpoint, string fallback,
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
