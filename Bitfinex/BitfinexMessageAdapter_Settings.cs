namespace StockSharp.Bitfinex;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// The message adapter for <see cref="Bitfinex"/>.
/// </summary>
[MediaIcon(Media.MediaNames.bitfinex)]
[Doc("topics/api/connectors/crypto_exchanges/bitfinex.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BitfinexKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions | MessageAdapterCategories.OrderLog)]
public partial class BitfinexMessageAdapter : MessageAdapter, IKeySecretAdapter
{
	private const string _defaultRestEndpoint = "https://api.bitfinex.com";
	private const string _defaultPrivateWebSocketEndpoint = "wss://api.bitfinex.com/ws/2";
	private const string _defaultPublicWebSocketEndpoint = "wss://api-pub.bitfinex.com/ws/2";

	/// <summary>
	/// Possible time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => Native.Extensions.TimeFrames.Keys.ToArray();

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

	/// <summary>
	/// REST API endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestEndpointKey,
		Description = LocalizedStrings.BitfinexRestApiEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>
	/// Private WebSocket API endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateWebSocketEndpointKey,
		Description = LocalizedStrings.BitfinexAuthenticatedWebSocketEndpointDescKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey)]
	[BasicSetting]
	public string PrivateWebSocketEndpoint { get; set; } = _defaultPrivateWebSocketEndpoint;

	/// <summary>
	/// Public WebSocket API endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PublicWebSocketEndpointKey,
		Description = LocalizedStrings.BitfinexPublicWebSocketEndpointDescKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey)]
	[BasicSetting]
	public string PublicWebSocketEndpoint { get; set; } = _defaultPublicWebSocketEndpoint;

	/// <summary>
	/// Cancel On Disconnect.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CancelOnDisconnectKey,
		Description = LocalizedStrings.CancelOnDisconnectKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.SessionKey,
		Order = 2)]
	public bool CancelOnDisconnect { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage.SetValue(nameof(Key), Key);
		storage.SetValue(nameof(Secret), Secret);
		storage.SetValue(nameof(CancelOnDisconnect), CancelOnDisconnect);
		storage.SetValue(nameof(RestEndpoint), RestEndpoint);
		storage.SetValue(nameof(PrivateWebSocketEndpoint), PrivateWebSocketEndpoint);
		storage.SetValue(nameof(PublicWebSocketEndpoint), PublicWebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		CancelOnDisconnect = storage.GetValue<bool>(nameof(CancelOnDisconnect));
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
		PrivateWebSocketEndpoint = storage.GetValue(nameof(PrivateWebSocketEndpoint), PrivateWebSocketEndpoint);
		PublicWebSocketEndpoint = storage.GetValue(nameof(PublicWebSocketEndpoint), PublicWebSocketEndpoint);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return base.ToString() + ": " + LocalizedStrings.Key + " = " + Key.ToId();
	}
}
