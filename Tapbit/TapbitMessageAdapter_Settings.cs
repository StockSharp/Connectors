namespace StockSharp.Tapbit;

/// <summary>
/// Tapbit market sections.
/// </summary>
[Flags]
public enum TapbitSections
{
    /// <summary>
    /// No market sections.
    /// </summary>
    None = 0,

    /// <summary>
    /// Spot markets.
    /// </summary>
    Spot = 1 << 0,

    /// <summary>
    /// USDT perpetual futures markets.
    /// </summary>
    Futures = 1 << 1,

    /// <summary>
    /// All documented markets.
    /// </summary>
    All = Spot | Futures,
}

/// <summary>
/// The message adapter for Tapbit Spot and USDT perpetual markets.
/// </summary>
[MediaIcon(Media.MediaNames.tapbit)]
[Doc("topics/api/connectors/crypto_exchanges/tapbit.html")]
[Display(ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.TapbitKey,
    Description = LocalizedStrings.CryptoConnectorKey,
    GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
    MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
    MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
    MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles |
    MessageAdapterCategories.History |
    MessageAdapterCategories.Transactions)]
public partial class TapbitMessageAdapter : MessageAdapter, IKeySecretAdapter
{
    private const string _defaultSpotRestEndpoint =
        "https://openapi.tapbit.com/spot-v2";
    private const string _defaultFuturesRestEndpoint =
        "https://openapi.tapbit.com/swap";
    private const string _defaultWebSocketEndpoint =
        "wss://ws-openapi.tapbit.com/stream/ws";

    /// <summary>
    /// Supported historical and realtime candle time frames.
    /// </summary>
    public static IEnumerable<TimeSpan> AllTimeFrames =>
        TapbitExtensions.TimeFrames;

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

    private TapbitSections _sections = TapbitSections.All;

    /// <summary>
    /// Enabled Tapbit market sections.
    /// </summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TypeKey,
        Description = LocalizedStrings.TypeKey,
        GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
    [BasicSetting]
    public TapbitSections Sections
    {
        get => _sections;
        set
        {
            if (value == 0 || (value & ~TapbitSections.All) != 0)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "At least one known Tapbit section must be enabled.");
            _sections = value;
        }
    }

    private TimeSpan _pollingInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// REST polling interval for public trades, candles, and private state.
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
            if (value < TimeSpan.FromSeconds(1))
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "Tapbit polling interval cannot be less than one second.");
            _pollingInterval = value;
        }
    }

    /// <summary>
    /// Spot V2 REST endpoint.
    /// </summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = LocalizedStrings.ServerAddressKey,
        GroupName = LocalizedStrings.AddressesKey, Order = 0)]
    [BasicSetting]
    public string SpotRestEndpoint { get; set; } = _defaultSpotRestEndpoint;

    /// <summary>
    /// USDT perpetual REST endpoint.
    /// </summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = LocalizedStrings.ServerAddressKey,
        GroupName = LocalizedStrings.AddressesKey, Order = 1)]
    [BasicSetting]
    public string FuturesRestEndpoint { get; set; } =
        _defaultFuturesRestEndpoint;

    /// <summary>
    /// Public WebSocket endpoint.
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
            .Set(nameof(Sections), Sections)
            .Set(nameof(PollingInterval), PollingInterval)
            .Set(nameof(SpotRestEndpoint), SpotRestEndpoint)
            .Set(nameof(FuturesRestEndpoint), FuturesRestEndpoint)
            .Set(nameof(WebSocketEndpoint), WebSocketEndpoint);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Key = storage.GetValue<SecureString>(nameof(Key));
        Secret = storage.GetValue<SecureString>(nameof(Secret));
        Sections = storage.GetValue(nameof(Sections), Sections);
        PollingInterval = storage.GetValue(nameof(PollingInterval),
            PollingInterval);
        SpotRestEndpoint = NormalizeEndpoint(storage.GetValue(
            nameof(SpotRestEndpoint), SpotRestEndpoint),
            _defaultSpotRestEndpoint, "https");
        FuturesRestEndpoint = NormalizeEndpoint(storage.GetValue(
            nameof(FuturesRestEndpoint), FuturesRestEndpoint),
            _defaultFuturesRestEndpoint, "https");
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
        => base.ToString() + $": Key={Key.ToId()}, Sections={Sections}";
}
