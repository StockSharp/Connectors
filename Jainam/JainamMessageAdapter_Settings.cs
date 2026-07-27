namespace StockSharp.Jainam;

/// <summary>The message adapter for Jainam ProTrade Open API.</summary>
[MediaIcon(Media.MediaNames.jainam)]
[Doc("topics/api/connectors/stock_market/jainam.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.JainamKey,
    Description = LocalizedStrings.StockConnectorKey,
    GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia | MessageAdapterCategories.RealTime |
    MessageAdapterCategories.Transactions | MessageAdapterCategories.Ticks |
    MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
    MessageAdapterCategories.Stock | MessageAdapterCategories.Futures |
    MessageAdapterCategories.Options | MessageAdapterCategories.FX |
    MessageAdapterCategories.Commodities)]
[OrderCondition(typeof(JainamOrderCondition))]
public partial class JainamMessageAdapter : MessageAdapter, ITokenAdapter
{
    private static readonly Uri _defaultRestAddress = new("https://protrade.jainam.in/");
    private const string _defaultInstrumentAddress = "https://protrade.jainam.in/contract/json/";
    private const string _defaultWebSocketAddress = "wss://ws.jainam.in/NorenWSTP/";

    /// <summary>Jainam user identifier returned to the vendor redirect URL.</summary>
    [Display(
        Name = "User ID",
        Description = "Jainam user ID returned with the vendor authorization code.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public string UserId { get; set; }

    /// <summary>Application code created in the Jainam developer portal.</summary>
    [Display(
        Name = "App code",
        Description = "App code used in the Jainam user authorization URL.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public string AppCode { get; set; }

    /// <summary>Application secret created in the Jainam developer portal.</summary>
    [Display(
        Name = "API secret",
        Description = "API secret used with the user ID and authorization code to create a session.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    [BasicSetting]
    public SecureString ApiSecret { get; set; }

    /// <summary>Authorization code returned to the vendor redirect URL.</summary>
    [Display(
        Name = "Authorization code",
        Description = "Authorization code returned by Jainam after the user approves the vendor app.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    [BasicSetting]
    public SecureString AuthCode { get; set; }

    /// <inheritdoc />
    [Display(
        Name = "User session",
        Description = "Jainam userSession token. When supplied, the authorization code and API secret are not used.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 4)]
    [BasicSetting]
    public SecureString Token { get; set; }

    /// <summary>Portfolio name emitted by the connector.</summary>
    [Display(
        Name = "Portfolio name",
        Description = "Portfolio name. When empty, the Jainam client ID is used.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 5)]
    public string PortfolioName { get; set; }

    /// <summary>Default product used for new orders.</summary>
    [Display(
        Name = "Default product",
        Description = "Default Jainam product for new orders.",
        GroupName = LocalizedStrings.OrderKey,
        Order = 6)]
    public JainamProducts DefaultProduct { get; set; } = JainamProducts.LongTerm;

    /// <summary>Maximum number of streaming reconnect attempts.</summary>
    [Display(
        Name = "Reconnect attempts",
        Description = "Maximum number of attempts to reconnect the Jainam market WebSocket.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 7)]
    public int ReconnectAttempts { get; set; } = 10;

    /// <summary>Interval for order and portfolio snapshots.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.IntervalKey,
        Description = LocalizedStrings.IntervalKey + LocalizedStrings.Dot,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 8)]
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>REST API root address.</summary>
    [Display(
        Name = "REST address",
        Description = "Jainam ProTrade REST root address.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 9)]
    public Uri RestAddress { get; set; } = _defaultRestAddress;

    /// <summary>Public contract-master root address.</summary>
    [Display(
        Name = "Instrument address",
        Description = "Jainam public JSON contract-master root address.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 10)]
    public string InstrumentAddress { get; set; } = _defaultInstrumentAddress;

    /// <summary>Market-data WebSocket address.</summary>
    [Display(
        Name = "WebSocket address",
        Description = "Jainam Noren market-data WebSocket address.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 11)]
    public string WebSocketAddress { get; set; } = _defaultWebSocketAddress;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(UserId), UserId)
            .Set(nameof(AppCode), AppCode)
            .Set(nameof(ApiSecret), ApiSecret)
            .Set(nameof(AuthCode), AuthCode)
            .Set(nameof(Token), Token)
            .Set(nameof(PortfolioName), PortfolioName)
            .Set(nameof(DefaultProduct), DefaultProduct)
            .Set(nameof(ReconnectAttempts), ReconnectAttempts)
            .Set(nameof(PollingInterval), PollingInterval)
            .Set(nameof(RestAddress), RestAddress)
            .Set(nameof(InstrumentAddress), InstrumentAddress)
            .Set(nameof(WebSocketAddress), WebSocketAddress);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        UserId = storage.GetValue<string>(nameof(UserId));
        AppCode = storage.GetValue<string>(nameof(AppCode));
        ApiSecret = storage.GetValue<SecureString>(nameof(ApiSecret));
        AuthCode = storage.GetValue<SecureString>(nameof(AuthCode));
        Token = storage.GetValue<SecureString>(nameof(Token));
        PortfolioName = storage.GetValue<string>(nameof(PortfolioName));
        DefaultProduct = storage.GetValue(nameof(DefaultProduct), DefaultProduct);
        ReconnectAttempts = storage.GetValue(nameof(ReconnectAttempts), ReconnectAttempts);
        PollingInterval = storage.GetValue(nameof(PollingInterval), PollingInterval);
        RestAddress = storage.GetValue(nameof(RestAddress), RestAddress);
        InstrumentAddress = storage.GetValue(nameof(InstrumentAddress), InstrumentAddress);
        WebSocketAddress = storage.GetValue(nameof(WebSocketAddress), WebSocketAddress);
    }
}
