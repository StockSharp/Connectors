namespace StockSharp.PaytmMoney;

/// <summary>
/// The message adapter for Paytm Money Open API.
/// </summary>
[MediaIcon(Media.MediaNames.paytmmoney)]
[Doc("topics/api/connectors/stock_market/paytm_money.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.PaytmMoneyKey,
    Description = LocalizedStrings.StockConnectorKey,
    GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.Asia |
    MessageAdapterCategories.Free |
    MessageAdapterCategories.Transactions |
    MessageAdapterCategories.RealTime |
    MessageAdapterCategories.History |
    MessageAdapterCategories.Candles |
    MessageAdapterCategories.Level1 |
    MessageAdapterCategories.MarketDepth |
    MessageAdapterCategories.Ticks |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.Futures |
    MessageAdapterCategories.Options)]
[OrderCondition(typeof(PaytmMoneyOrderCondition))]
public partial class PaytmMoneyMessageAdapter :
    MessageAdapter, IKeySecretAdapter, ITokenAdapter
{
    private static readonly Uri _defaultAddress =
        new("https://developer.paytmmoney.com/");
    private static readonly Uri _defaultWebSocketAddress =
        new("wss://developer-ws.paytmmoney.com/broadcast/user/v1/data");

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ApiKeyKey,
        Description = LocalizedStrings.ApiKeyCreatedInThePaytmMoneyDeveloperPortalDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Key { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ApiSecretKey,
        Description = LocalizedStrings.ApiSecretCreatedInThePaytmMoneyDeveloperPortalDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public SecureString Secret { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AccessTokenKey,
        Description = LocalizedStrings.TradingAccessTokenReturnedByTheSessionEndpointDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    [BasicSetting]
    public SecureString Token { get; set; }

    /// <summary>
    /// Read-only access token.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ReadAccessTokenKey,
        Description = LocalizedStrings.ReadOnlyTokenUsedForAccountAndHistoricalDataDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    [BasicSetting]
    public SecureString ReadAccessToken { get; set; }

    /// <summary>
    /// Public market-data access token.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PublicAccessTokenKey,
        Description = LocalizedStrings.PublicTokenUsedByTheMarketDataWebSocketDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 4)]
    [BasicSetting]
    public SecureString PublicAccessToken { get; set; }

    /// <summary>
    /// One-time request token used to generate a session.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.RequestTokenKey,
        Description = LocalizedStrings.OneTimeTokenReturnedByTheBrowserLoginFlowDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 5)]
    public SecureString RequestToken { get; set; }

    /// <summary>
    /// Default product used for new orders.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DefaultProductKey,
        Description = LocalizedStrings.DefaultPaytmMoneyProductForNewOrdersDescKey,
        GroupName = LocalizedStrings.OrderKey,
        Order = 6)]
    public PaytmMoneyProducts DefaultProduct { get; set; } =
        PaytmMoneyProducts.Intraday;

    /// <summary>
    /// Portfolio name emitted by the connector.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PortfolioNameLabelKey,
        Description = LocalizedStrings.PortfolioNameWhenEmptyThePaytmMoneyUserIdIsUsedDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 7)]
    public string PortfolioName { get; set; }

    /// <summary>
    /// REST API root address.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ApiAddressKey,
        Description = LocalizedStrings.PaytmMoneyRestApiRootAddressDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 8)]
    public Uri Address { get; set; } = _defaultAddress;

    /// <summary>
    /// Market-data WebSocket address.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.WebSocketAddressKey,
        Description = LocalizedStrings.PaytmMoneyBroadcastWebSocketAddressDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 9)]
    public Uri WebSocketAddress { get; set; } =
        _defaultWebSocketAddress;

    /// <summary>
    /// Security master file name.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SecurityMasterFileKey,
        Description = LocalizedStrings.FileNameRequestedFromThePublicScripMasterEndpointDescKey,
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 10)]
    public string SecurityMasterFile { get; set; } =
        "security_master.csv";

    /// <summary>
    /// Interval for REST account and order snapshots.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.IntervalKey,
        Description = LocalizedStrings.IntervalKey +
            LocalizedStrings.Dot,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 11)]
    public TimeSpan PollingInterval { get; set; } =
        TimeSpan.FromSeconds(5);

    /// <summary>
    /// Create the browser login URL for a state value.
    /// </summary>
    public Uri GetLoginUri(string state)
    {
        var key = Key?.UnSecure().ThrowIfEmpty(nameof(Key));
        return new(
            $"https://login.paytmmoney.com/merchant-login?apiKey={Uri.EscapeDataString(key)}&state={Uri.EscapeDataString(state.ThrowIfEmpty(nameof(state)))}");
    }

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Key), Key)
            .Set(nameof(Secret), Secret)
            .Set(nameof(Token), Token)
            .Set(nameof(ReadAccessToken), ReadAccessToken)
            .Set(nameof(PublicAccessToken), PublicAccessToken)
            .Set(nameof(RequestToken), RequestToken)
            .Set(nameof(DefaultProduct), DefaultProduct)
            .Set(nameof(PortfolioName), PortfolioName)
            .Set(nameof(Address), Address)
            .Set(nameof(WebSocketAddress), WebSocketAddress)
            .Set(nameof(SecurityMasterFile), SecurityMasterFile)
            .Set(nameof(PollingInterval), PollingInterval);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Key = storage.GetValue<SecureString>(nameof(Key));
        Secret = storage.GetValue<SecureString>(nameof(Secret));
        Token = storage.GetValue<SecureString>(nameof(Token));
        ReadAccessToken =
            storage.GetValue<SecureString>(nameof(ReadAccessToken));
        PublicAccessToken =
            storage.GetValue<SecureString>(nameof(PublicAccessToken));
        RequestToken =
            storage.GetValue<SecureString>(nameof(RequestToken));
        DefaultProduct =
            storage.GetValue(nameof(DefaultProduct), DefaultProduct);
        PortfolioName =
            storage.GetValue<string>(nameof(PortfolioName));
        Address = storage.GetValue(nameof(Address), Address);
        WebSocketAddress =
            storage.GetValue(nameof(WebSocketAddress), WebSocketAddress);
        SecurityMasterFile =
            storage.GetValue(nameof(SecurityMasterFile), SecurityMasterFile);
        PollingInterval =
            storage.GetValue(nameof(PollingInterval), PollingInterval);
    }
}
