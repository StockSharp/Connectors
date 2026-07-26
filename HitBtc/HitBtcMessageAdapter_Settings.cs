namespace StockSharp.HitBtc;

/// <summary>
/// The message adapter for <see cref="HitBtc"/>.
/// </summary>
[MediaIcon(Media.MediaNames.hitbtc)]
[Doc("topics/api/connectors/crypto_exchanges/hitbtc.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.HitBtcKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(HitBtcOrderCondition))]
public partial class HitBtcMessageAdapter : MessageAdapter, IKeySecretAdapter
{
	private const string _legacyRestEndpoint = "https://api.hitbtc.com/api";
	private const string _legacyWebSocketEndpoint = "wss://api.hitbtc.com/api/2/ws";
	private const string _defaultRestEndpoint = "https://api.hitbtc.com/api/3";
	private const string _defaultWebSocketEndpoint = "wss://api.hitbtc.com/api/3/ws/public";
	private const string _defaultTradingWebSocketEndpoint = "wss://api.hitbtc.com/api/3/ws/trading";

	/// <summary>
	/// Possible time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => Extensions.TimeFrames.Keys.ToArray();

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey + LocalizedStrings.Dot,
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

	/// <summary>REST API endpoint.</summary>
	[Display(
		Name = "REST endpoint",
		Description = "REST API endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>WebSocket endpoint.</summary>
	[Display(
		Name = "WebSocket endpoint",
		Description = "WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string WebSocketEndpoint { get; set; } = _defaultWebSocketEndpoint;

	/// <summary>Trading WebSocket endpoint.</summary>
	[Display(
		Name = "Trading WebSocket endpoint",
		Description = "Trading WebSocket endpoint.",
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string TradingWebSocketEndpoint { get; set; } = _defaultTradingWebSocketEndpoint;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage.SetValue(nameof(Key), Key);
		storage.SetValue(nameof(Secret), Secret);
		storage.SetValue(nameof(RestEndpoint), RestEndpoint);
		storage.SetValue(nameof(WebSocketEndpoint), WebSocketEndpoint);
		storage.SetValue(nameof(TradingWebSocketEndpoint), TradingWebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
		WebSocketEndpoint = storage.GetValue(nameof(WebSocketEndpoint), WebSocketEndpoint);
		TradingWebSocketEndpoint = storage.GetValue(nameof(TradingWebSocketEndpoint), TradingWebSocketEndpoint);

		if (RestEndpoint.EqualsIgnoreCase(_legacyRestEndpoint))
			RestEndpoint = _defaultRestEndpoint;

		if (WebSocketEndpoint.EqualsIgnoreCase(_legacyWebSocketEndpoint))
			WebSocketEndpoint = _defaultWebSocketEndpoint;
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return base.ToString() + ": " + LocalizedStrings.Key + " = " + Key.ToId();
	}
}
