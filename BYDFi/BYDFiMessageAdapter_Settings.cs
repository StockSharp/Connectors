namespace StockSharp.BYDFi;

/// <summary>
/// The message adapter for BYDFi perpetual futures.
/// </summary>
[MediaIcon(Media.MediaNames.bydfi)]
[Doc("topics/api/connectors/crypto_exchanges/bydfi.html")]
[Display(ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.BYDFiKey,
    Description = LocalizedStrings.CryptoConnectorKey,
    GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
    MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
    MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
    MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles |
    MessageAdapterCategories.History |
    MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(BYDFiOrderCondition))]
public partial class BYDFiMessageAdapter : MessageAdapter, IKeySecretAdapter
{
    private const string _defaultRestEndpoint =
        "https://api.bydfi.com/api";
    private const string _defaultWebSocketEndpoint =
        "wss://stream.bydfi.com/v1/public/fapi";

    /// <summary>
    /// Supported historical and realtime candle time frames.
    /// </summary>
    public static IEnumerable<TimeSpan> AllTimeFrames =>
        BYDFiExtensions.TimeFrames;

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

    private string _wallet = "W001";

    /// <summary>
    /// Futures sub-wallet code.
    /// </summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PortfolioKey,
        Description = LocalizedStrings.PortfolioKey,
        GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
    [BasicSetting]
    public string Wallet
    {
        get => _wallet;
        set => _wallet = value.ThrowIfEmpty(nameof(value)).Trim()
            .ToUpperInvariant();
    }

    private TimeSpan _pollingInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// REST polling interval for trades and private snapshots.
    /// </summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.IntervalKey,
        Description = LocalizedStrings.IntervalKey,
        GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
    public TimeSpan PollingInterval
    {
        get => _pollingInterval;
        set
        {
            if (value < TimeSpan.FromMilliseconds(250))
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "BYDFi polling interval cannot be less than 250 ms.");
            _pollingInterval = value;
        }
    }

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
    /// Public futures WebSocket endpoint.
    /// </summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.WebSocketKey,
        Description = LocalizedStrings.WsEndpointKey,
        GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 0)]
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
            .Set(nameof(Wallet), Wallet)
            .Set(nameof(PollingInterval), PollingInterval)
            .Set(nameof(RestEndpoint), RestEndpoint)
            .Set(nameof(WebSocketEndpoint), WebSocketEndpoint);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Key = storage.GetValue<SecureString>(nameof(Key));
        Secret = storage.GetValue<SecureString>(nameof(Secret));
        Wallet = storage.GetValue(nameof(Wallet), Wallet);
        PollingInterval = storage.GetValue(nameof(PollingInterval),
            PollingInterval);
        RestEndpoint = NormalizeEndpoint(storage.GetValue(
            nameof(RestEndpoint), RestEndpoint), _defaultRestEndpoint,
            "https");
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
        => base.ToString() + $": Key={Key.ToId()}, Wallet={Wallet}";
}
