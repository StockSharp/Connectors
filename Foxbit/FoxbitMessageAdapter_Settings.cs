namespace StockSharp.Foxbit;

/// <summary>
/// The message adapter for Foxbit.
/// </summary>
[MediaIcon(Media.MediaNames.foxbit)]
[Doc("topics/api/connectors/crypto_exchanges/foxbit.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.FoxbitKey,
    Description = LocalizedStrings.CryptoConnectorKey,
    GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
    MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
    MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
    MessageAdapterCategories.Level1 | MessageAdapterCategories.History |
    MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(FoxbitOrderCondition))]
public partial class FoxbitMessageAdapter : MessageAdapter, IKeySecretAdapter
{
    private const string _defaultRestEndpoint = "https://api.foxbit.com.br";
    private const string _defaultWebSocketEndpoint =
        "wss://api.foxbit.com.br/ws/v3/public";
    private TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Supported historical candle time frames.
    /// </summary>
    public static IEnumerable<TimeSpan> AllTimeFrames =>
        FoxbitExtensions.TimeFrames;

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

    /// <summary>
    /// Interval between private REST refreshes.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.IntervalKey,
        Description = LocalizedStrings.IntervalDataUpdatesKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 0)]
    [BasicSetting]
    public TimeSpan PollingInterval
    {
        get => _pollingInterval;
        set => _pollingInterval = value < TimeSpan.FromSeconds(5)
            ? TimeSpan.FromSeconds(5)
            : value;
    }

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Key), Key)
            .Set(nameof(Secret), Secret)
            .Set(nameof(RestEndpoint), RestEndpoint)
            .Set(nameof(WebSocketEndpoint), WebSocketEndpoint)
            .Set(nameof(PollingInterval), PollingInterval);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Key = storage.GetValue<SecureString>(nameof(Key));
        Secret = storage.GetValue<SecureString>(nameof(Secret));
        RestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(RestEndpoint),
            RestEndpoint), _defaultRestEndpoint, "https");
        WebSocketEndpoint = NormalizeEndpoint(storage.GetValue(
            nameof(WebSocketEndpoint), WebSocketEndpoint),
            _defaultWebSocketEndpoint, "wss");
        PollingInterval = storage.GetValue(nameof(PollingInterval),
            PollingInterval);
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
