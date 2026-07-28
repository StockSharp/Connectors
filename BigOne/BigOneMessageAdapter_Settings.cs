namespace StockSharp.BigOne;

/// <summary>
/// The message adapter for BigONE spot and contract markets.
/// </summary>
[MediaIcon(Media.MediaNames.bigone)]
[Doc("topics/api/connectors/crypto_exchanges/bigone.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BigOneKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles |
	MessageAdapterCategories.History |
	MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(BigOneOrderCondition))]
public partial class BigOneMessageAdapter : MessageAdapter,
	IKeySecretAdapter
{
	private const string _defaultSpotRestEndpoint =
		"https://api.big.one/api/v3";
	private const string _defaultSpotWebSocketEndpoint =
		"wss://api.big.one/ws/v2";
	private const string _defaultContractRestEndpoint =
		"https://api.big.one/api/contract/v2";
	private const string _defaultContractWebSocketEndpoint =
		"wss://api.big.one/ws/contract/v2";
	private const string _defaultContractPrivateWebSocketEndpoint =
		"wss://api.big.one/ws/contract/v2/stream";

	/// <summary>
	/// Supported candle time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames
		=> BigOneExtensions.TimeFrames;

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
	/// Spot REST API endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public string SpotRestEndpoint { get; set; } =
		_defaultSpotRestEndpoint;

	/// <summary>
	/// Spot WebSocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 0)]
	[BasicSetting]
	public string SpotWebSocketEndpoint { get; set; } =
		_defaultSpotWebSocketEndpoint;

	/// <summary>
	/// Contract REST API endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	[BasicSetting]
	public string ContractRestEndpoint { get; set; } =
		_defaultContractRestEndpoint;

	/// <summary>
	/// Public contract WebSocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PublicKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 1)]
	[BasicSetting]
	public string ContractWebSocketEndpoint { get; set; } =
		_defaultContractWebSocketEndpoint;

	/// <summary>
	/// Private contract WebSocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 2)]
	[BasicSetting]
	public string ContractPrivateWebSocketEndpoint { get; set; } =
		_defaultContractPrivateWebSocketEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(SpotRestEndpoint), SpotRestEndpoint)
			.Set(nameof(SpotWebSocketEndpoint),
				SpotWebSocketEndpoint)
			.Set(nameof(ContractRestEndpoint), ContractRestEndpoint)
			.Set(nameof(ContractWebSocketEndpoint),
				ContractWebSocketEndpoint)
			.Set(nameof(ContractPrivateWebSocketEndpoint),
				ContractPrivateWebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		SpotRestEndpoint = NormalizeEndpoint(
			storage.GetValue(
				nameof(SpotRestEndpoint), SpotRestEndpoint),
			_defaultSpotRestEndpoint, "https");
		SpotWebSocketEndpoint = NormalizeEndpoint(
			storage.GetValue(
				nameof(SpotWebSocketEndpoint),
				SpotWebSocketEndpoint),
			_defaultSpotWebSocketEndpoint, "wss");
		ContractRestEndpoint = NormalizeEndpoint(
			storage.GetValue(
				nameof(ContractRestEndpoint), ContractRestEndpoint),
			_defaultContractRestEndpoint, "https");
		ContractWebSocketEndpoint = NormalizeEndpoint(
			storage.GetValue(
				nameof(ContractWebSocketEndpoint),
				ContractWebSocketEndpoint),
			_defaultContractWebSocketEndpoint, "wss");
		ContractPrivateWebSocketEndpoint = NormalizeEndpoint(
			storage.GetValue(
				nameof(ContractPrivateWebSocketEndpoint),
				ContractPrivateWebSocketEndpoint),
			_defaultContractPrivateWebSocketEndpoint, "wss");
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
