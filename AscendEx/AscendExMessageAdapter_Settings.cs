namespace StockSharp.AscendEx;

/// <summary>
/// AscendEX spot account category.
/// </summary>
public enum AscendExSpotAccountTypes
{
	/// <summary>
	/// Cash spot account.
	/// </summary>
	Cash,

	/// <summary>
	/// Margin spot account.
	/// </summary>
	Margin,
}

/// <summary>
/// The message adapter for AscendEX spot, margin and futures markets.
/// </summary>
[MediaIcon(Media.MediaNames.ascendex)]
[Doc("topics/api/connectors/crypto_exchanges/ascendex.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.AscendExKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles |
	MessageAdapterCategories.History |
	MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(AscendExOrderCondition))]
public partial class AscendExMessageAdapter : MessageAdapter,
	IKeySecretAdapter
{
	private const string _defaultRestEndpoint =
		"https://ascendex.com";
	private const string _defaultSpotWebSocketEndpoint =
		"wss://ascendex.com/0/api/pro/v1/stream";
	private const string _defaultFuturesWebSocketEndpoint =
		"wss://ascendex.com/api/pro/v2/stream";

	/// <summary>
	/// Supported candle time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames
		=> AscendExExtensions.TimeFrames;

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
	/// Account group used in private API paths.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.GroupKey,
		Description = LocalizedStrings.GroupKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public int AccountGroup { get; set; }

	/// <summary>
	/// Spot account category used for private operations.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccountKey,
		Description = LocalizedStrings.AccountKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public AscendExSpotAccountTypes SpotAccountType { get; set; } =
		AscendExSpotAccountTypes.Cash;

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
	/// Spot WebSocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SpotKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 0)]
	[BasicSetting]
	public string SpotWebSocketEndpoint { get; set; } =
		_defaultSpotWebSocketEndpoint;

	/// <summary>
	/// Futures WebSocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FuturesKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey,
		Order = 1)]
	[BasicSetting]
	public string FuturesWebSocketEndpoint { get; set; } =
		_defaultFuturesWebSocketEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(AccountGroup), AccountGroup)
			.Set(nameof(SpotAccountType), SpotAccountType)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(SpotWebSocketEndpoint),
				SpotWebSocketEndpoint)
			.Set(nameof(FuturesWebSocketEndpoint),
				FuturesWebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		AccountGroup = storage.GetValue(
			nameof(AccountGroup), AccountGroup).Max(0);
		SpotAccountType = storage.GetValue(
			nameof(SpotAccountType), SpotAccountType);
		RestEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(RestEndpoint), RestEndpoint),
			_defaultRestEndpoint, "https");
		SpotWebSocketEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(SpotWebSocketEndpoint),
				SpotWebSocketEndpoint),
			_defaultSpotWebSocketEndpoint, "wss");
		FuturesWebSocketEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(FuturesWebSocketEndpoint),
				FuturesWebSocketEndpoint),
			_defaultFuturesWebSocketEndpoint, "wss");
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
		=> base.ToString() +
			$": Key={Key.ToId()}, Group={AccountGroup}";
}
