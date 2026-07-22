namespace StockSharp.Rain;

/// <summary>
/// The message adapter for Rain.
/// </summary>
[MediaIcon(Media.MediaNames.rain)]
[Doc("topics/api/connectors/crypto_exchanges/rain.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.RainKey,
    Description = LocalizedStrings.CryptoConnectorKey,
    GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
    MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
    MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
    MessageAdapterCategories.Level1 | MessageAdapterCategories.History |
    MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(RainOrderCondition))]
public partial class RainMessageAdapter : MessageAdapter, IKeySecretAdapter, ITokenAdapter
{
    private const string _defaultRestEndpoint = "https://api-bhr.rain.com";
    private const string _defaultWebSocketEndpoint =
        "wss://pro-api-bhr.rain.com/websocket";

    /// <summary>
    /// Supported historical candle time frames.
    /// </summary>
    public static IEnumerable<TimeSpan> AllTimeFrames =>
        RainExtensions.TimeFrames;

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
    /// Rain account access token.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TokenKey,
        Description = LocalizedStrings.AccessTokenKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    [BasicSetting]
    public SecureString Token { get; set; }

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
    /// WebSocket API endpoint.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.WebSocketKey,
        Description = LocalizedStrings.WsEndpointKey,
        GroupName = LocalizedStrings.WebSocketAddressesKey,
        Order = 0)]
    [BasicSetting]
    public string WebSocketEndpoint { get; set; } =
        _defaultWebSocketEndpoint;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Key), Key)
            .Set(nameof(Secret), Secret)
            .Set(nameof(Token), Token)
            .Set(nameof(RestEndpoint), RestEndpoint)
            .Set(nameof(WebSocketEndpoint), WebSocketEndpoint);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Key = storage.GetValue<SecureString>(nameof(Key));
        Secret = storage.GetValue<SecureString>(nameof(Secret));
        Token = storage.GetValue<SecureString>(nameof(Token));
        RestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(RestEndpoint),
            RestEndpoint), _defaultRestEndpoint, "https");
        WebSocketEndpoint = NormalizeEndpoint(storage.GetValue(
            nameof(WebSocketEndpoint), WebSocketEndpoint),
            _defaultWebSocketEndpoint, "wss");
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
