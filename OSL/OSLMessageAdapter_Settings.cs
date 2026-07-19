namespace StockSharp.OSL;

/// <summary>
/// The message adapter for OSL Global.
/// </summary>
[MediaIcon(Media.MediaNames.osl)]
[Doc("topics/api/connectors/crypto_exchanges/osl.html")]
[Display(ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.OSLKey,
    Description = LocalizedStrings.CryptoConnectorKey,
    GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
    MessageAdapterCategories.RealTime | MessageAdapterCategories.Paid |
    MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
    MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles |
    MessageAdapterCategories.History |
    MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(OSLOrderCondition))]
public partial class OSLMessageAdapter : MessageAdapter, IKeySecretAdapter,
    IPassphraseAdapter
{
    private const string _defaultRestEndpoint = "https://api.osl.com";
    private const string _defaultPublicWsEndpoint =
        "wss://stream-api.osl.com/v2/ws/public";
    private const string _defaultPrivateWsEndpoint =
        "wss://stream-api.osl.com/v2/ws/private";
    private const string _defaultCandleWsEndpoint =
        "wss://stream-api.osl.com/openapi/v1/ws";

    /// <summary>
    /// Supported historical and realtime candle time frames.
    /// </summary>
    public static IEnumerable<TimeSpan> AllTimeFrames =>
        OSLExtensions.TimeFrames;

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

    /// <inheritdoc />
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PassphraseKey,
        Description = LocalizedStrings.PassphraseKey,
        GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
    [BasicSetting]
    public SecureString Passphrase { get; set; }

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
    /// Public SPOT WebSocket endpoint.
    /// </summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.WebSocketKey,
        Description = LocalizedStrings.WsEndpointKey,
        GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 0)]
    [BasicSetting]
    public string PublicWsEndpoint { get; set; } =
        _defaultPublicWsEndpoint;

    /// <summary>
    /// Private SPOT WebSocket endpoint.
    /// </summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.WebSocketKey,
        Description = LocalizedStrings.WsEndpointKey,
        GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 1)]
    [BasicSetting]
    public string PrivateWsEndpoint { get; set; } =
        _defaultPrivateWsEndpoint;

    /// <summary>
    /// Public candlestick WebSocket endpoint.
    /// </summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.WebSocketKey,
        Description = LocalizedStrings.WsEndpointKey,
        GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 2)]
    [BasicSetting]
    public string CandleWsEndpoint { get; set; } =
        _defaultCandleWsEndpoint;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Key), Key)
            .Set(nameof(Secret), Secret)
            .Set(nameof(Passphrase), Passphrase)
            .Set(nameof(RestEndpoint), RestEndpoint)
            .Set(nameof(PublicWsEndpoint), PublicWsEndpoint)
            .Set(nameof(PrivateWsEndpoint), PrivateWsEndpoint)
            .Set(nameof(CandleWsEndpoint), CandleWsEndpoint);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Key = storage.GetValue<SecureString>(nameof(Key));
        Secret = storage.GetValue<SecureString>(nameof(Secret));
        Passphrase = storage.GetValue<SecureString>(nameof(Passphrase));
        RestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(RestEndpoint),
            RestEndpoint), _defaultRestEndpoint, "https");
        PublicWsEndpoint = NormalizeEndpoint(storage.GetValue(
            nameof(PublicWsEndpoint), PublicWsEndpoint),
            _defaultPublicWsEndpoint, "wss");
        PrivateWsEndpoint = NormalizeEndpoint(storage.GetValue(
            nameof(PrivateWsEndpoint), PrivateWsEndpoint),
            _defaultPrivateWsEndpoint, "wss");
        CandleWsEndpoint = NormalizeEndpoint(storage.GetValue(
            nameof(CandleWsEndpoint), CandleWsEndpoint),
            _defaultCandleWsEndpoint, "wss");
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
