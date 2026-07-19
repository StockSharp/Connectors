namespace StockSharp.Indodax;

/// <summary>
/// The message adapter for Indodax.
/// </summary>
[MediaIcon(Media.MediaNames.indodax)]
[Doc("topics/api/connectors/crypto_exchanges/indodax.html")]
[Display(ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.IndodaxKey,
    Description = LocalizedStrings.CryptoConnectorKey,
    GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
    MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
    MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
    MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles |
    MessageAdapterCategories.History | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(IndodaxOrderCondition))]
public partial class IndodaxMessageAdapter : MessageAdapter, IKeySecretAdapter
{
    private const string _defaultRestEndpoint = "https://indodax.com";
    private const string _defaultHistoryEndpoint = "https://tapi.indodax.com";
    private const string _defaultMarketDataWebSocketEndpoint =
        "wss://ws3.indodax.com/ws/";
    private const string _defaultPrivateWebSocketEndpoint =
        "wss://pws.indodax.com/ws/?cf_ws_frame_ping_pong=true";

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
    /// Public REST and TAPI endpoint.
    /// </summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = LocalizedStrings.ServerAddressKey,
        GroupName = LocalizedStrings.AddressesKey, Order = 0)]
    [BasicSetting]
    public string RestEndpoint { get; set; } = _defaultRestEndpoint;

    /// <summary>
    /// Trade API v2 history endpoint.
    /// </summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.HistoryKey,
        Description = LocalizedStrings.ServerAddressKey,
        GroupName = LocalizedStrings.AddressesKey, Order = 1)]
    [BasicSetting]
    public string HistoryEndpoint { get; set; } = _defaultHistoryEndpoint;

    /// <summary>
    /// Market Data WebSocket endpoint.
    /// </summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.MarketDataKey,
        Description = LocalizedStrings.WsEndpointKey,
        GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 0)]
    [BasicSetting]
    public string MarketDataWebSocketEndpoint { get; set; } =
        _defaultMarketDataWebSocketEndpoint;

    /// <summary>
    /// Private WebSocket endpoint.
    /// </summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PrivateKey,
        Description = LocalizedStrings.WsEndpointKey,
        GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 1)]
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
            .Set(nameof(HistoryEndpoint), HistoryEndpoint)
            .Set(nameof(MarketDataWebSocketEndpoint),
                MarketDataWebSocketEndpoint)
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
        HistoryEndpoint = NormalizeEndpoint(storage.GetValue(
            nameof(HistoryEndpoint), HistoryEndpoint), _defaultHistoryEndpoint,
            "https");
        MarketDataWebSocketEndpoint = NormalizeEndpoint(storage.GetValue(
            nameof(MarketDataWebSocketEndpoint), MarketDataWebSocketEndpoint),
            _defaultMarketDataWebSocketEndpoint, "wss", false);
        PrivateWebSocketEndpoint = NormalizeEndpoint(storage.GetValue(
            nameof(PrivateWebSocketEndpoint), PrivateWebSocketEndpoint),
            _defaultPrivateWebSocketEndpoint, "wss", false);
    }

    private static string NormalizeEndpoint(string endpoint, string fallback,
        string scheme, bool trimTrailingSlash = true)
    {
        endpoint = endpoint.IsEmpty() ? fallback : endpoint.Trim();
        if (!endpoint.Contains("://", StringComparison.Ordinal))
            endpoint = $"{scheme}://{endpoint.TrimStart('/')}";
        return trimTrailingSlash ? endpoint.TrimEnd('/') : endpoint;
    }

    /// <inheritdoc />
    public override string ToString()
        => base.ToString() + $": Key={Key.ToId()}";
}
