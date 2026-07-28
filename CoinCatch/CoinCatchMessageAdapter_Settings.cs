namespace StockSharp.CoinCatch;

/// <summary>
/// The message adapter for CoinCatch spot and futures markets.
/// </summary>
[MediaIcon(Media.MediaNames.coincatch)]
[Doc("topics/api/connectors/crypto_exchanges/coincatch.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CoinCatchKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles |
	MessageAdapterCategories.History |
	MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(CoinCatchOrderCondition))]
public partial class CoinCatchMessageAdapter : MessageAdapter,
	IKeySecretAdapter, IPassphraseAdapter
{
	private const string _defaultRestEndpoint =
		"https://api.coincatch.com";
	private const string _defaultPublicWebSocketEndpoint =
		"wss://ws.coincatch.com/public/v1/stream";
	private const string _defaultPrivateWebSocketEndpoint =
		"wss://ws.coincatch.com/private/v1/stream";

	/// <summary>
	/// Supported candle time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames
		=> CoinCatchExtensions.TimeFrames;

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

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PassphraseKey,
		Description = LocalizedStrings.PassphraseKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString Passphrase { get; set; }

	/// <summary>
	/// Market product.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TypeKey,
		Description = LocalizedStrings.TypeKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public CoinCatchProductTypes ProductType { get; set; } =
		CoinCatchProductTypes.Spot;

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
	/// Interval used to refresh private REST state.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	[BasicSetting]
	public TimeSpan PollingInterval { get; set; } =
		TimeSpan.FromSeconds(5);

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(Passphrase), Passphrase)
			.Set(nameof(ProductType), ProductType)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(PublicWebSocketEndpoint),
				PublicWebSocketEndpoint)
			.Set(nameof(PrivateWebSocketEndpoint),
				PrivateWebSocketEndpoint)
			.Set(nameof(PollingInterval), PollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		Passphrase = storage.GetValue<SecureString>(nameof(Passphrase));
		ProductType = storage.GetValue(
			nameof(ProductType), ProductType);
		RestEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(RestEndpoint), RestEndpoint),
			_defaultRestEndpoint, "https");
		PublicWebSocketEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(PublicWebSocketEndpoint),
				PublicWebSocketEndpoint),
			_defaultPublicWebSocketEndpoint, "wss");
		PrivateWebSocketEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(PrivateWebSocketEndpoint),
				PrivateWebSocketEndpoint),
			_defaultPrivateWebSocketEndpoint, "wss");
		PollingInterval = storage.GetValue(
			nameof(PollingInterval), PollingInterval);
		if (PollingInterval <= TimeSpan.Zero)
			PollingInterval = TimeSpan.FromSeconds(5);
	}

	private static string NormalizeEndpoint(string endpoint,
		string fallback, string scheme)
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
