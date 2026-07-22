namespace StockSharp.Deepcoin;

/// <summary>
/// The message adapter for Deepcoin.
/// </summary>
[MediaIcon(Media.MediaNames.deepcoin)]
[Doc("topics/api/connectors/crypto_exchanges/deepcoin.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.DeepcoinKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles |
	MessageAdapterCategories.History | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(DeepcoinOrderCondition))]
public partial class DeepcoinMessageAdapter : MessageAdapter, IKeySecretAdapter, IPassphraseAdapter
{
	private const string _defaultRestEndpoint = "https://api.deepcoin.com";
	private const string _defaultSpotWsEndpoint =
		"wss://stream.deepcoin.com/streamlet/trade/public/spot?platform=api&version=v2";
	private const string _defaultSwapWsEndpoint =
		"wss://stream.deepcoin.com/streamlet/trade/public/swap?platform=api&version=v2";
	private const string _defaultPrivateWsEndpoint = "wss://stream.deepcoin.com/v1/private";

	/// <summary>
	/// Supported candle time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => DeepcoinExtensions.TimeFrames;

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
	/// Spot public WebSocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 0)]
	[BasicSetting]
	public string SpotWsEndpoint { get; set; } = _defaultSpotWsEndpoint;

	/// <summary>
	/// Perpetual public WebSocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 1)]
	[BasicSetting]
	public string SwapWsEndpoint { get; set; } = _defaultSwapWsEndpoint;

	/// <summary>
	/// Private WebSocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 2)]
	[BasicSetting]
	public string PrivateWsEndpoint { get; set; } = _defaultPrivateWsEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(Passphrase), Passphrase)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(SpotWsEndpoint), SpotWsEndpoint)
			.Set(nameof(SwapWsEndpoint), SwapWsEndpoint)
			.Set(nameof(PrivateWsEndpoint), PrivateWsEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		Passphrase = storage.GetValue<SecureString>(nameof(Passphrase));
		RestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(RestEndpoint), RestEndpoint),
			_defaultRestEndpoint, "https");
		SpotWsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(SpotWsEndpoint), SpotWsEndpoint),
			_defaultSpotWsEndpoint, "wss");
		SwapWsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(SwapWsEndpoint), SwapWsEndpoint),
			_defaultSwapWsEndpoint, "wss");
		PrivateWsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(PrivateWsEndpoint), PrivateWsEndpoint),
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
		=> base.ToString() + $": Key={Key.ToId()}";
}
