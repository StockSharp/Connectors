namespace StockSharp.Mastertrust;

/// <summary>
/// The message adapter for Mastertrust Trade API.
/// </summary>
[MediaIcon(Media.MediaNames.mastertrust)]
[Doc("topics/api/connectors/stock_market/mastertrust.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.MastertrustKey,
    Description = LocalizedStrings.StockConnectorKey,
    GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.Asia |
    MessageAdapterCategories.Transactions |
    MessageAdapterCategories.RealTime |
    MessageAdapterCategories.Ticks |
    MessageAdapterCategories.Level1 |
    MessageAdapterCategories.MarketDepth |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.Futures |
    MessageAdapterCategories.Options |
    MessageAdapterCategories.FX |
    MessageAdapterCategories.Commodities)]
[OrderCondition(typeof(MastertrustOrderCondition))]
public partial class MastertrustMessageAdapter : MessageAdapter, ITokenAdapter
{
    private static readonly Uri _defaultAddress =
        new("https://masterswift-beta.mastertrust.co.in/");
    private static readonly Uri _defaultMasterAddress =
        new("https://masterswift-beta.mastertrust.co.in/api/v1/contract/Compact?info=download");
    private static readonly Uri _defaultWebSocketAddress =
        new("wss://masterswift-beta.mastertrust.co.in/ws/v1/feeds");
    private static readonly Uri _defaultRedirectUri =
        new("http://127.0.0.1");

    /// <summary>
    /// Mastertrust trading account identifier.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TradingClientIdKey,
        Description = LocalizedStrings.MastertrustTradingAccountIdentifierUsedByPortfolioOrderAndStreamingAPIsDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public string ClientId { get; set; }

    /// <summary>
    /// OAuth2 client identifier created in the Mastertrust developer portal.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.OAuthClientIdKey,
        Description = LocalizedStrings.OAuth2ClientIdentifierCreatedInThePublicMastertrustDeveloperPortalDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public string OAuthClientId { get; set; }

    /// <summary>
    /// OAuth2 client secret issued by Mastertrust.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.OAuthClientSecretKey,
        Description = LocalizedStrings.OAuth2ClientSecretIssuedForTheSelectedMastertrustApplicationDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    [BasicSetting]
    public SecureString OAuthClientSecret { get; set; }

    /// <summary>
    /// Authorization code returned to the configured redirect URI.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AuthorizationCodeKey,
        Description = LocalizedStrings.DailyOAuth2AuthorizationCodeReturnedByTheMastertrustAuthorizationEndpointDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    [BasicSetting]
    public SecureString AuthorizationCode { get; set; }

    /// <summary>
    /// OAuth2 redirect URI registered for the application.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.RedirectUriKey,
        Description = LocalizedStrings.OAuth2RedirectUriRegisteredInTheMastertrustDeveloperPortalDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 4)]
    [BasicSetting]
    public Uri RedirectUri { get; set; } = _defaultRedirectUri;

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TokenKey,
        Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 5)]
    [BasicSetting]
    public SecureString Token { get; set; }

    /// <summary>
    /// Portfolio name emitted by the connector.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PortfolioNameLabelKey,
        Description = LocalizedStrings.PortfolioNameWhenEmptyTheMastertrustTradingClientIdIsUsedDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 6)]
    public string PortfolioName { get; set; }

    /// <summary>
    /// Default product used for new orders.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DefaultProductKey,
        Description = LocalizedStrings.DefaultMastertrustOrderProductDescKey,
        GroupName = LocalizedStrings.OrderKey,
        Order = 7)]
    public MastertrustProducts DefaultProduct { get; set; } =
        MastertrustProducts.Normal;

    /// <summary>
    /// Maximum number of streaming reconnect attempts.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ReconnectAttemptsLabelKey,
        Description = LocalizedStrings.MaximumNumberOfAttemptsToReconnectTheMastertrustWebSocketDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 8)]
    public int ReconnectAttempts { get; set; } = 10;

    /// <summary>
    /// REST and OAuth2 root address.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.RestAddressKey,
        Description = LocalizedStrings.MastertrustTradeApiRestAndOAuth2RootAddressDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 9)]
    public Uri Address { get; set; } = _defaultAddress;

    /// <summary>
    /// Public daily instrument-master archive address.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.MasterAddressKey,
        Description = LocalizedStrings.PublicMastertrustDailyInstrumentMasterZipAddressDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 10)]
    public Uri MasterAddress { get; set; } = _defaultMasterAddress;

    /// <summary>
    /// Live market-data and account-update WebSocket address.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.WebSocketAddressKey,
        Description = LocalizedStrings.MastertrustLiveDataAndAccountUpdateWebSocketAddressDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 11)]
    public Uri WebSocketAddress { get; set; } = _defaultWebSocketAddress;

    /// <summary>
    /// Interval for account and order snapshot refreshes.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.IntervalKey,
        Description = LocalizedStrings.IntervalKey + LocalizedStrings.Dot,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 12)]
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(ClientId), ClientId)
            .Set(nameof(OAuthClientId), OAuthClientId)
            .Set(nameof(OAuthClientSecret), OAuthClientSecret)
            .Set(nameof(AuthorizationCode), AuthorizationCode)
            .Set(nameof(RedirectUri), RedirectUri)
            .Set(nameof(Token), Token)
            .Set(nameof(PortfolioName), PortfolioName)
            .Set(nameof(DefaultProduct), DefaultProduct)
            .Set(nameof(ReconnectAttempts), ReconnectAttempts)
            .Set(nameof(Address), Address)
            .Set(nameof(MasterAddress), MasterAddress)
            .Set(nameof(WebSocketAddress), WebSocketAddress)
            .Set(nameof(PollingInterval), PollingInterval);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        ClientId = storage.GetValue<string>(nameof(ClientId));
        OAuthClientId = storage.GetValue<string>(nameof(OAuthClientId));
        OAuthClientSecret = storage.GetValue<SecureString>(nameof(OAuthClientSecret));
        AuthorizationCode = storage.GetValue<SecureString>(nameof(AuthorizationCode));
        RedirectUri = storage.GetValue(nameof(RedirectUri), RedirectUri);
        Token = storage.GetValue<SecureString>(nameof(Token));
        PortfolioName = storage.GetValue<string>(nameof(PortfolioName));
        DefaultProduct = storage.GetValue(nameof(DefaultProduct), DefaultProduct);
        ReconnectAttempts = storage.GetValue(nameof(ReconnectAttempts), ReconnectAttempts);
        Address = storage.GetValue(nameof(Address), Address);
        MasterAddress = storage.GetValue(nameof(MasterAddress), MasterAddress);
        WebSocketAddress = storage.GetValue(nameof(WebSocketAddress), WebSocketAddress);
        PollingInterval = storage.GetValue(nameof(PollingInterval), PollingInterval);
    }
}
