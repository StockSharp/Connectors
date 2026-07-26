namespace StockSharp.Rupeezy;

/// <summary>
/// The message adapter for Rupeezy Vortex.
/// </summary>
[MediaIcon(Media.MediaNames.rupeezy)]
[Doc("topics/api/connectors/stock_market/rupeezy.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.RupeezyKey,
    Description = LocalizedStrings.StockConnectorKey,
    GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.Asia |
    MessageAdapterCategories.Transactions |
    MessageAdapterCategories.RealTime |
    MessageAdapterCategories.History |
    MessageAdapterCategories.Ticks |
    MessageAdapterCategories.Level1 |
    MessageAdapterCategories.MarketDepth |
    MessageAdapterCategories.Candles |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.Futures |
    MessageAdapterCategories.Options |
    MessageAdapterCategories.FX |
    MessageAdapterCategories.Commodities)]
[OrderCondition(typeof(RupeezyOrderCondition))]
public partial class RupeezyMessageAdapter : MessageAdapter, ITokenAdapter
{
    private static readonly Uri _defaultAddress = new("https://vortex-api.rupeezy.in/v2/");
    private static readonly Uri _defaultMasterAddress = new("https://static.rupeezy.in/master.csv");
    private static readonly Uri _defaultWebSocketAddress = new("wss://wire.rupeezy.in/ws");

    /// <summary>
    /// Application identifier created in the Vortex developer portal.
    /// </summary>
    [Display(
        Name = "Application ID",
        Description = "Application identifier created in the Rupeezy Vortex developer portal.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public string ApplicationId { get; set; }

    /// <summary>
    /// API key created in the Vortex developer portal.
    /// </summary>
    [Display(
        Name = "API key",
        Description = "API key created in the Rupeezy Vortex developer portal.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public SecureString ApiKey { get; set; }

    /// <summary>
    /// Authorization code returned by the Rupeezy SSO callback.
    /// </summary>
    [Display(
        Name = "Authorization code",
        Description = "Authorization code returned in the auth parameter of the Rupeezy SSO callback.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    [BasicSetting]
    public SecureString AuthCode { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TokenKey,
        Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    [BasicSetting]
    public SecureString Token { get; set; }

    /// <summary>
    /// Portfolio name emitted by the connector.
    /// </summary>
    [Display(
        Name = "Portfolio name",
        Description = "Portfolio name. When empty, the Rupeezy user or application identifier is used.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 4)]
    public string PortfolioName { get; set; }

    /// <summary>
    /// Default product used for new orders.
    /// </summary>
    [Display(
        Name = "Default product",
        Description = "Default Rupeezy order product.",
        GroupName = LocalizedStrings.OrderKey,
        Order = 5)]
    public RupeezyProducts DefaultProduct { get; set; } = RupeezyProducts.Delivery;

    /// <summary>
    /// Maximum number of streaming reconnect attempts.
    /// </summary>
    [Display(
        Name = "Reconnect attempts",
        Description = "Maximum number of attempts to reconnect the Rupeezy WebSocket.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 6)]
    public int ReconnectAttempts { get; set; } = 10;

    /// <summary>
    /// REST API root address.
    /// </summary>
    [Display(
        Name = "REST address",
        Description = "Rupeezy Vortex REST API root address.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 7)]
    public Uri Address { get; set; } = _defaultAddress;

    /// <summary>
    /// Public daily instrument-master address.
    /// </summary>
    [Display(
        Name = "Master address",
        Description = "Public Rupeezy daily instrument-master CSV address.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 8)]
    public Uri MasterAddress { get; set; } = _defaultMasterAddress;

    /// <summary>
    /// Market-data and order-update WebSocket address.
    /// </summary>
    [Display(
        Name = "WebSocket address",
        Description = "Rupeezy live-data WebSocket address.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 9)]
    public Uri WebSocketAddress { get; set; } = _defaultWebSocketAddress;

    /// <summary>
    /// Interval for account and order snapshot refreshes.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.IntervalKey,
        Description = LocalizedStrings.IntervalKey + LocalizedStrings.Dot,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 10)]
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(ApplicationId), ApplicationId)
            .Set(nameof(ApiKey), ApiKey)
            .Set(nameof(AuthCode), AuthCode)
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
        ApplicationId = storage.GetValue<string>(nameof(ApplicationId));
        ApiKey = storage.GetValue<SecureString>(nameof(ApiKey));
        AuthCode = storage.GetValue<SecureString>(nameof(AuthCode));
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
