namespace StockSharp.GmoCoin;

/// <summary>
/// The message adapter for GMO Coin.
/// </summary>
[MediaIcon(Media.MediaNames.gmocoin)]
[Doc("topics/api/connectors/crypto_exchanges/gmo_coin.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.GmoCoinKey,
    Description = LocalizedStrings.CryptoConnectorKey,
    GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
    MessageAdapterCategories.Free | MessageAdapterCategories.Ticks |
    MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Level1 |
    MessageAdapterCategories.History | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(GmoCoinOrderCondition))]
public partial class GmoCoinMessageAdapter : MessageAdapter, IKeySecretAdapter
{
    private const string _defaultRestEndpoint = "https://api.coin.z.com";
    private const string _defaultPublicWebSocketEndpoint =
        "wss://api.coin.z.com/ws/public/v1";
    private const string _defaultPrivateWebSocketEndpoint =
        "wss://api.coin.z.com/ws/private/v1";

    /// <summary>
    /// Supported historical candle time frames.
    /// </summary>
    public static IEnumerable<TimeSpan> AllTimeFrames =>
        GmoCoinExtensions.TimeFrames;

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
    /// Public WebSocket endpoint.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.WebSocketKey,
        Description = LocalizedStrings.WsEndpointKey,
        GroupName = LocalizedStrings.WebSocketAddressesKey,
        Order = 0)]
    [BasicSetting]
    public string PublicWebSocketEndpoint { get; set; } =
        _defaultPublicWebSocketEndpoint;

    /// <summary>
    /// Private WebSocket endpoint before the access-token suffix.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.WebSocketKey,
        Description = LocalizedStrings.WsEndpointKey,
        GroupName = LocalizedStrings.WebSocketAddressesKey,
        Order = 1)]
    [BasicSetting]
    public string PrivateWebSocketEndpoint { get; set; } =
        _defaultPrivateWebSocketEndpoint;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Key), Key)
            .Set(nameof(Secret), Secret)
            .Set(nameof(RestEndpoint), RestEndpoint)
            .Set(nameof(PublicWebSocketEndpoint), PublicWebSocketEndpoint)
            .Set(nameof(PrivateWebSocketEndpoint), PrivateWebSocketEndpoint);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Key = storage.GetValue<SecureString>(nameof(Key));
        Secret = storage.GetValue<SecureString>(nameof(Secret));
        RestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(RestEndpoint),
            RestEndpoint), _defaultRestEndpoint, "https");
        PublicWebSocketEndpoint = NormalizeEndpoint(storage.GetValue(
            nameof(PublicWebSocketEndpoint), PublicWebSocketEndpoint),
            _defaultPublicWebSocketEndpoint, "wss");
        PrivateWebSocketEndpoint = NormalizeEndpoint(storage.GetValue(
            nameof(PrivateWebSocketEndpoint), PrivateWebSocketEndpoint),
            _defaultPrivateWebSocketEndpoint, "wss");
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
