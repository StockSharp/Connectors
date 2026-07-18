namespace StockSharp.Backpack;

/// <summary>
/// The message adapter for Backpack Exchange.
/// </summary>
[MediaIcon(Media.MediaNames.backpack)]
[Doc("topics/api/connectors/crypto_exchanges/backpack.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BackpackKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles |
	MessageAdapterCategories.History | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(BackpackOrderCondition))]
public partial class BackpackMessageAdapter : MessageAdapter, IKeySecretAdapter
{
	private const string _defaultRestEndpoint = "https://api.backpack.exchange";
	private const string _defaultWsEndpoint = "wss://ws.backpack.exchange";

	/// <summary>
	/// Supported candle time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => BackpackExtensions.TimeFrames;

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey, GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SecretDescKey, GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>
	/// REST API endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey, GroupName = LocalizedStrings.AddressesKey, Order = 0)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>
	/// WebSocket endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey, GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 0)]
	[BasicSetting]
	public string WebSocketEndpoint { get; set; } = _defaultWsEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		RestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(RestEndpoint), RestEndpoint),
			_defaultRestEndpoint, "https");
		WebSocketEndpoint = NormalizeEndpoint(
			storage.GetValue(nameof(WebSocketEndpoint), WebSocketEndpoint),
			_defaultWsEndpoint, "wss");
	}

	private static string NormalizeEndpoint(string endpoint, string fallback, string scheme)
	{
		endpoint = endpoint.IsEmpty() ? fallback : endpoint.Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"{scheme}://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/');
	}

	/// <inheritdoc />
	public override string ToString() => base.ToString() + $": Key={Key.ToId()}";
}
