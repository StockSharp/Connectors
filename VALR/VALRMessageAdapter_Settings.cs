namespace StockSharp.VALR;

/// <summary>
/// The message adapter for VALR.
/// </summary>
[MediaIcon(Media.MediaNames.valr)]
[Doc("topics/api/connectors/crypto_exchanges/valr.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ValrKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.History |
	MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(VALROrderCondition))]
public partial class VALRMessageAdapter : MessageAdapter, IKeySecretAdapter
{
	private const string _defaultRestEndpoint = "https://api.valr.com";
	private const string _defaultTradeWebSocketEndpoint =
		"wss://api.valr.com/ws/trade";
	private const string _defaultAccountWebSocketEndpoint =
		"wss://api.valr.com/ws/account";

	/// <summary>
	/// Supported historical candle time frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
		VALRExtensions.TimeFrames;

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>
	/// Optional subaccount identifier. Required for a primary-account key to
	/// access margin or futures subaccounts.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AccountKey,
		Description = LocalizedStrings.AccountKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public string SubAccountId { get; set; }

	/// <summary>
	/// REST API endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey, Order = 0)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>
	/// Trade WebSocket endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 0)]
	[BasicSetting]
	public string TradeWebSocketEndpoint { get; set; } =
		_defaultTradeWebSocketEndpoint;

	/// <summary>
	/// Account WebSocket endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 1)]
	[BasicSetting]
	public string AccountWebSocketEndpoint { get; set; } =
		_defaultAccountWebSocketEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(SubAccountId), SubAccountId)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(TradeWebSocketEndpoint), TradeWebSocketEndpoint)
			.Set(nameof(AccountWebSocketEndpoint), AccountWebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		SubAccountId = storage.GetValue<string>(nameof(SubAccountId))?.Trim();
		RestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(RestEndpoint),
			RestEndpoint), _defaultRestEndpoint, "https");
		TradeWebSocketEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(TradeWebSocketEndpoint), TradeWebSocketEndpoint),
			_defaultTradeWebSocketEndpoint, "wss");
		AccountWebSocketEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(AccountWebSocketEndpoint), AccountWebSocketEndpoint),
			_defaultAccountWebSocketEndpoint, "wss");
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
		=> base.ToString() + $": Key={Key.ToId()}, Account={SubAccountId}";
}
