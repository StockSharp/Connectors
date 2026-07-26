namespace StockSharp.Bigul;

/// <summary>
/// The message adapter for Bigul Connect.
/// </summary>
[MediaIcon(Media.MediaNames.bigul)]
[Doc("topics/api/connectors/stock_market/bigul.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.BigulKey,
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
    MessageAdapterCategories.Options)]
[OrderCondition(typeof(BigulOrderCondition))]
public partial class BigulMessageAdapter : MessageAdapter, ITokenAdapter
{
    private static readonly Uri _defaultAddress = new("https://capi.bigul.co/api/v1/");
    private static readonly Uri _defaultMasterAddress =
        new("https://bigul.s3.ap-south-1.amazonaws.com/BigulMasters/Bigulmastercsv.zip");
    private static readonly Uri _defaultWebSocketAddress =
        new("wss://cbc.bigul.co/broadcast/socket");

    /// <summary>
    /// Bigul client code.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.UserIdKey,
        Description = LocalizedStrings.UserIdKey + LocalizedStrings.Dot,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public string ClientCode { get; set; }

    /// <summary>
    /// Application key generated in Bigul Connect.
    /// </summary>
    [Display(
        Name = "API key",
        Description = "Application key generated in Bigul Connect.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public SecureString ApiKey { get; set; }

    /// <summary>
    /// Application secret generated in Bigul Connect.
    /// </summary>
    [Display(
        Name = "API secret",
        Description = "Application secret generated in Bigul Connect.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    [BasicSetting]
    public SecureString ApiSecret { get; set; }

    /// <summary>
    /// Current six-digit authenticator value.
    /// </summary>
    [Display(
        Name = "TOTP",
        Description = "Current six-digit TOTP used by the Bigul client login endpoint.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    [BasicSetting]
    public SecureString OneTimePassword { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TokenKey,
        Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 4)]
    [BasicSetting]
    public SecureString Token { get; set; }

    /// <summary>
    /// Source code assigned to the application.
    /// </summary>
    [Display(
        Name = "Source",
        Description = "Source code assigned by Bigul Connect. The individual-client default is B2C.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 5)]
    [BasicSetting]
    public string Source { get; set; } = "B2C";

    /// <summary>
    /// Portfolio name emitted by the connector.
    /// </summary>
    [Display(
        Name = "Portfolio name",
        Description = "Portfolio name. When empty, the Bigul client code is used.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 6)]
    public string PortfolioName { get; set; }

    /// <summary>
    /// Default product used for new orders.
    /// </summary>
    [Display(
        Name = "Default product",
        Description = "Default Bigul delivery, intraday, or normal product.",
        GroupName = LocalizedStrings.OrderKey,
        Order = 7)]
    public BigulProducts DefaultProduct { get; set; } = BigulProducts.Delivery;

    /// <summary>
    /// Default market-protection value. Zero disables protection.
    /// </summary>
    [Display(
        Name = "Market protection",
        Description = "Default Bigul market-protection value. Zero disables protection.",
        GroupName = LocalizedStrings.OrderKey,
        Order = 8)]
    public decimal MarketProtection { get; set; }

    /// <summary>
    /// Maximum number of streaming reconnect attempts.
    /// </summary>
    [Display(
        Name = "Reconnect attempts",
        Description = "Maximum number of attempts to reconnect the Bigul WebSocket.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 9)]
    public int ReconnectAttempts { get; set; } = 10;

    /// <summary>
    /// REST API root address.
    /// </summary>
    [Display(
        Name = "REST address",
        Description = "Bigul Connect client REST API root address.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 10)]
    public Uri Address { get; set; } = _defaultAddress;

    /// <summary>
    /// Public daily instrument-master archive.
    /// </summary>
    [Display(
        Name = "Master address",
        Description = "Public Bigul daily instrument-master ZIP address.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 11)]
    public Uri MasterAddress { get; set; } = _defaultMasterAddress;

    /// <summary>
    /// Market-data WebSocket address.
    /// </summary>
    [Display(
        Name = "WebSocket address",
        Description = "Bigul market-data WebSocket address.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 12)]
    public Uri WebSocketAddress { get; set; } = _defaultWebSocketAddress;

    /// <summary>
    /// Interval for account and order snapshot refreshes.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.IntervalKey,
        Description = LocalizedStrings.IntervalKey + LocalizedStrings.Dot,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 13)]
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(ClientCode), ClientCode)
            .Set(nameof(ApiKey), ApiKey)
            .Set(nameof(ApiSecret), ApiSecret)
            .Set(nameof(OneTimePassword), OneTimePassword)
            .Set(nameof(Token), Token)
            .Set(nameof(Source), Source)
            .Set(nameof(PortfolioName), PortfolioName)
            .Set(nameof(DefaultProduct), DefaultProduct)
            .Set(nameof(MarketProtection), MarketProtection)
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
        ClientCode = storage.GetValue<string>(nameof(ClientCode));
        ApiKey = storage.GetValue<SecureString>(nameof(ApiKey));
        ApiSecret = storage.GetValue<SecureString>(nameof(ApiSecret));
        OneTimePassword = storage.GetValue<SecureString>(nameof(OneTimePassword));
        Token = storage.GetValue<SecureString>(nameof(Token));
        Source = storage.GetValue(nameof(Source), Source);
        PortfolioName = storage.GetValue<string>(nameof(PortfolioName));
        DefaultProduct = storage.GetValue(nameof(DefaultProduct), DefaultProduct);
        MarketProtection = storage.GetValue(nameof(MarketProtection), MarketProtection);
        ReconnectAttempts = storage.GetValue(nameof(ReconnectAttempts), ReconnectAttempts);
        Address = storage.GetValue(nameof(Address), Address);
        MasterAddress = storage.GetValue(nameof(MasterAddress), MasterAddress);
        WebSocketAddress = storage.GetValue(nameof(WebSocketAddress), WebSocketAddress);
        PollingInterval = storage.GetValue(nameof(PollingInterval), PollingInterval);
    }
}
