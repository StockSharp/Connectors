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
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.InteractiveAppKeyKey,
        Description = LocalizedStrings.InteractiveApiApplicationKeyCreatedInTheWisdomCapitalDeveloperDashboardDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Key { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.InteractiveAppSecretKey,
        Description = LocalizedStrings.InteractiveApiApplicationSecretDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public SecureString Secret { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.InteractiveTokenKey,
        Description = LocalizedStrings.ExistingInteractiveApiTokenWhenEmptyTheConnectorLogsInWithTheAppKeyAndSecretDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    public SecureString Token { get; set; }

    /// <summary>Interactive XTS user ID.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.InteractiveUserIdKey,
        Description = LocalizedStrings.XtsUserIdReturnedByInteractiveLoginRequiredWithAPreIssuedInteractiveTokenDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    public string UserId { get; set; }

    /// <summary>Market-data API application key.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.MarketDataAppKeyKey,
        Description = LocalizedStrings.SeparateMarketDataApplicationKeyCreatedInTheWisdomCapitalDeveloperDashboardDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 4)]
    [BasicSetting]
    public SecureString MarketDataKey { get; set; }

    /// <summary>Market-data API application secret.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.MarketDataAppSecretKey,
        Description = LocalizedStrings.SeparateMarketDataApplicationSecretDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 5)]
    [BasicSetting]
    public SecureString MarketDataSecret { get; set; }

    /// <summary>Existing market-data token.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.MarketDataTokenKey,
        Description = LocalizedStrings.ExistingMarketDataTokenWhenEmptyTheConnectorLogsInWithTheMarketDataAppKeyAndSecretDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 6)]
    public SecureString MarketDataToken { get; set; }

    /// <summary>Market-data XTS user ID.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.MarketDataUserIdKey,
        Description = LocalizedStrings.XtsUserIdReturnedByMarketDataLoginRequiredWithAPreIssuedMarketDataTokenDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 7)]
    public string MarketDataUserId { get; set; }

    /// <summary>Portfolio name emitted by the connector.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PortfolioNameLabelKey,
        Description = LocalizedStrings.PortfolioNameWhenEmptyTheInteractiveUserIdIsUsedDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 8)]
    public string PortfolioName { get; set; }

    /// <summary>Default product used for new orders.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DefaultProductKey,
        Description = LocalizedStrings.DefaultXtsProductUsedWhenAnOrderConditionDoesNotSpecifyOneDescKey,
        GroupName = LocalizedStrings.OrderKey,
        Order = 9)]
    public WisdomCapitalProducts DefaultProduct { get; set; } =
        WisdomCapitalProducts.Intraday;

    /// <summary>XTS application source.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SourceKey,
        Description = LocalizedStrings.ApplicationSourcePassedToBothXtsLoginEndpointsDescKey,
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
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ReconnectAttemptsLabelKey,
        Description = LocalizedStrings.MaximumNumberOfSocketIoReconnectAttemptsDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 12)]
    public int ReconnectAttempts { get; set; } = 10;

    /// <summary>Socket.IO Engine.IO protocol version.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.EngineIoVersionKey,
        Description = LocalizedStrings.EngineIoProtocolVersionUsedByTheWisdomCapitalSocketIoEndpointsDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 13)]
    public int EngineIoVersion { get; set; } = 4;

    /// <summary>REST and Socket.IO server root.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ApiAddressKey,
        Description = LocalizedStrings.WisdomCapitalXtsServerRootAddressDescKey,
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
