namespace StockSharp.WisdomCapital;

/// <summary>The message adapter for Wisdom Capital Trading API.</summary>
[MediaIcon(Media.MediaNames.wisdomcapital)]
[Doc("topics/api/connectors/stock_market/wisdom_capital.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.WisdomCapitalKey,
    Description = LocalizedStrings.StockConnectorKey,
    GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.Asia |
    MessageAdapterCategories.RealTime |
    MessageAdapterCategories.History |
    MessageAdapterCategories.Candles |
    MessageAdapterCategories.Transactions |
    MessageAdapterCategories.Ticks |
    MessageAdapterCategories.Level1 |
    MessageAdapterCategories.MarketDepth |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.Futures |
    MessageAdapterCategories.Options |
    MessageAdapterCategories.FX |
    MessageAdapterCategories.Commodities)]
[OrderCondition(typeof(WisdomCapitalOrderCondition))]
public partial class WisdomCapitalMessageAdapter : MessageAdapter,
    IKeySecretAdapter, ITokenAdapter
{
    private static readonly Uri _defaultRestAddress =
        new("https://trade.wisdomcapital.in/");

    /// <inheritdoc />
    [Display(
        Name = "Interactive app key",
        Description = "Interactive API application key created in the Wisdom Capital developer dashboard.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Key { get; set; }

    /// <inheritdoc />
    [Display(
        Name = "Interactive app secret",
        Description = "Interactive API application secret.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public SecureString Secret { get; set; }

    /// <inheritdoc />
    [Display(
        Name = "Interactive token",
        Description = "Existing interactive API token. When empty, the connector logs in with the app key and secret.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    public SecureString Token { get; set; }

    /// <summary>Interactive XTS user ID.</summary>
    [Display(
        Name = "Interactive user ID",
        Description = "XTS user ID returned by interactive login. Required with a pre-issued interactive token.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    public string UserId { get; set; }

    /// <summary>Market-data API application key.</summary>
    [Display(
        Name = "Market-data app key",
        Description = "Separate market-data application key created in the Wisdom Capital developer dashboard.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 4)]
    [BasicSetting]
    public SecureString MarketDataKey { get; set; }

    /// <summary>Market-data API application secret.</summary>
    [Display(
        Name = "Market-data app secret",
        Description = "Separate market-data application secret.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 5)]
    [BasicSetting]
    public SecureString MarketDataSecret { get; set; }

    /// <summary>Existing market-data token.</summary>
    [Display(
        Name = "Market-data token",
        Description = "Existing market-data token. When empty, the connector logs in with the market-data app key and secret.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 6)]
    public SecureString MarketDataToken { get; set; }

    /// <summary>Market-data XTS user ID.</summary>
    [Display(
        Name = "Market-data user ID",
        Description = "XTS user ID returned by market-data login. Required with a pre-issued market-data token.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 7)]
    public string MarketDataUserId { get; set; }

    /// <summary>Portfolio name emitted by the connector.</summary>
    [Display(
        Name = "Portfolio name",
        Description = "Portfolio name. When empty, the interactive user ID is used.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 8)]
    public string PortfolioName { get; set; }

    /// <summary>Default product used for new orders.</summary>
    [Display(
        Name = "Default product",
        Description = "Default XTS product used when an order condition does not specify one.",
        GroupName = LocalizedStrings.OrderKey,
        Order = 9)]
    public WisdomCapitalProducts DefaultProduct { get; set; } =
        WisdomCapitalProducts.Intraday;

    /// <summary>XTS application source.</summary>
    [Display(
        Name = "Source",
        Description = "Application source passed to both XTS login endpoints.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 10)]
    public string Source { get; set; } = "WebAPI";

    /// <summary>Interval for transaction and portfolio refreshes.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.IntervalKey,
        Description = LocalizedStrings.IntervalKey + LocalizedStrings.Dot,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 11)]
    public TimeSpan PollingInterval { get; set; } =
        TimeSpan.FromSeconds(15);

    /// <summary>Maximum number of Socket.IO reconnect attempts.</summary>
    [Display(
        Name = "Reconnect attempts",
        Description = "Maximum number of Socket.IO reconnect attempts.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 12)]
    public int ReconnectAttempts { get; set; } = 10;

    /// <summary>Socket.IO Engine.IO protocol version.</summary>
    [Display(
        Name = "Engine.IO version",
        Description = "Engine.IO protocol version used by the Wisdom Capital Socket.IO endpoints.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 13)]
    public int EngineIoVersion { get; set; } = 4;

    /// <summary>REST and Socket.IO server root.</summary>
    [Display(
        Name = "API address",
        Description = "Wisdom Capital XTS server root address.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 14)]
    public Uri RestAddress { get; set; } = _defaultRestAddress;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Key), Key)
            .Set(nameof(Secret), Secret)
            .Set(nameof(Token), Token)
            .Set(nameof(UserId), UserId)
            .Set(nameof(MarketDataKey), MarketDataKey)
            .Set(nameof(MarketDataSecret), MarketDataSecret)
            .Set(nameof(MarketDataToken), MarketDataToken)
            .Set(nameof(MarketDataUserId), MarketDataUserId)
            .Set(nameof(PortfolioName), PortfolioName)
            .Set(nameof(DefaultProduct), DefaultProduct)
            .Set(nameof(Source), Source)
            .Set(nameof(PollingInterval), PollingInterval)
            .Set(nameof(ReconnectAttempts), ReconnectAttempts)
            .Set(nameof(EngineIoVersion), EngineIoVersion)
            .Set(nameof(RestAddress), RestAddress);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Key = storage.GetValue<SecureString>(nameof(Key));
        Secret = storage.GetValue<SecureString>(nameof(Secret));
        Token = storage.GetValue<SecureString>(nameof(Token));
        UserId = storage.GetValue<string>(nameof(UserId));
        MarketDataKey = storage.GetValue<SecureString>(
            nameof(MarketDataKey));
        MarketDataSecret = storage.GetValue<SecureString>(
            nameof(MarketDataSecret));
        MarketDataToken = storage.GetValue<SecureString>(
            nameof(MarketDataToken));
        MarketDataUserId = storage.GetValue<string>(
            nameof(MarketDataUserId));
        PortfolioName = storage.GetValue<string>(nameof(PortfolioName));
        DefaultProduct = storage.GetValue(
            nameof(DefaultProduct),
            DefaultProduct);
        Source = storage.GetValue(nameof(Source), Source);
        PollingInterval = storage.GetValue(
            nameof(PollingInterval),
            PollingInterval);
        ReconnectAttempts = storage.GetValue(
            nameof(ReconnectAttempts),
            ReconnectAttempts);
        EngineIoVersion = storage.GetValue(
            nameof(EngineIoVersion),
            EngineIoVersion);
        RestAddress = storage.GetValue(nameof(RestAddress), RestAddress);
    }
}
