namespace StockSharp.Korbit;

/// <summary>
/// The message adapter for Korbit.
/// </summary>
[MediaIcon(Media.MediaNames.korbit)]
[Doc("topics/api/connectors/crypto_exchanges/korbit.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.KorbitKey,
    Description = LocalizedStrings.CryptoConnectorKey,
    GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
    MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
    MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
    MessageAdapterCategories.Level1 | MessageAdapterCategories.History |
    MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(KorbitOrderCondition))]
public partial class KorbitMessageAdapter : MessageAdapter, IKeySecretAdapter
{
    private const string _defaultRestEndpoint = "https://api.korbit.co.kr";
    private const string _defaultPublicWebSocketEndpoint =
        "wss://ws-api.korbit.co.kr/v2/public";
    private const string _defaultPrivateWebSocketEndpoint =
        "wss://ws-api.korbit.co.kr/v2/private";

    /// <summary>
    /// Supported historical candle time frames.
    /// </summary>
    public static IEnumerable<TimeSpan> AllTimeFrames =>
        KorbitExtensions.TimeFrames;

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
    /// Account sequence number. The main account is <c>1</c>.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AccountKey,
        Description = LocalizedStrings.AccountKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    [BasicSetting]
    public int AccountSequence { get; set; } = 1;

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
    /// Private WebSocket endpoint.
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
            .Set(nameof(AccountSequence), AccountSequence)
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
        AccountSequence = storage.GetValue(nameof(AccountSequence),
            AccountSequence).Max(1);
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
        => base.ToString() + $": Key={Key.ToId()}, Account={AccountSequence}";
}
