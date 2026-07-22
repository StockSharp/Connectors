namespace StockSharp.CoinJar;

/// <summary>
/// The message adapter for CoinJar Exchange.
/// </summary>
[MediaIcon(Media.MediaNames.coinjar)]
[Doc("topics/api/connectors/crypto_exchanges/coinjar.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.CoinJarKey,
    Description = LocalizedStrings.CryptoConnectorKey,
    GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
    MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
    MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
    MessageAdapterCategories.Level1 | MessageAdapterCategories.History |
    MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(CoinJarOrderCondition))]
public partial class CoinJarMessageAdapter : MessageAdapter, ITokenAdapter
{
    private const string _defaultTradingEndpoint =
        "https://api.exchange.coinjar.com";
    private const string _defaultDataEndpoint =
        "https://data.exchange.coinjar.com";
    private const string _defaultWebSocketEndpoint =
        "wss://feed.exchange.coinjar.com/socket/websocket";

    /// <summary>
    /// Supported historical candle time frames.
    /// </summary>
    public static IEnumerable<TimeSpan> AllTimeFrames =>
        CoinJarExtensions.TimeFrames;

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TokenKey,
        Description = LocalizedStrings.TokenKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Token { get; set; }

    /// <summary>
    /// Trading REST API endpoint.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = LocalizedStrings.ServerAddressKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 0)]
    [BasicSetting]
    public string TradingEndpoint { get; set; } = _defaultTradingEndpoint;

    /// <summary>
    /// Public market-data REST API endpoint.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.MarketDataKey,
        Description = LocalizedStrings.ServerAddressKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 1)]
    [BasicSetting]
    public string DataEndpoint { get; set; } = _defaultDataEndpoint;

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
    public string WebSocketEndpoint { get; set; } = _defaultWebSocketEndpoint;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Token), Token)
            .Set(nameof(TradingEndpoint), TradingEndpoint)
            .Set(nameof(DataEndpoint), DataEndpoint)
            .Set(nameof(WebSocketEndpoint), WebSocketEndpoint);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Token = storage.GetValue<SecureString>(nameof(Token));
        TradingEndpoint = NormalizeEndpoint(storage.GetValue(
            nameof(TradingEndpoint), TradingEndpoint), _defaultTradingEndpoint,
            "https");
        DataEndpoint = NormalizeEndpoint(storage.GetValue(nameof(DataEndpoint),
            DataEndpoint), _defaultDataEndpoint, "https");
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
        => base.ToString() + $": Token={Token.ToId()}";
}
