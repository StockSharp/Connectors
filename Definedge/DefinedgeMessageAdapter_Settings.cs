namespace StockSharp.Definedge;

/// <summary>
/// The message adapter for Definedge Securities API.
/// </summary>
[MediaIcon(Media.MediaNames.definedge)]
[Doc("topics/api/connectors/stock_market/definedge.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.DefinedgeKey,
    Description = LocalizedStrings.StockConnectorKey,
    GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.Asia |
    MessageAdapterCategories.Transactions |
    MessageAdapterCategories.RealTime |
    MessageAdapterCategories.History |
    MessageAdapterCategories.Candles |
    MessageAdapterCategories.Level1 |
    MessageAdapterCategories.MarketDepth |
    MessageAdapterCategories.Ticks |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.Futures |
    MessageAdapterCategories.Options |
    MessageAdapterCategories.FX |
    MessageAdapterCategories.Commodities)]
[OrderCondition(typeof(DefinedgeOrderCondition))]
public partial class DefinedgeMessageAdapter :
    MessageAdapter, IKeySecretAdapter, ITokenAdapter
{
    private static readonly Uri _defaultAddress =
        new("https://integrate.definedgesecurities.com/dart/v1/");
    private static readonly Uri _defaultLoginAddress =
        new("https://signin.definedgesecurities.com/auth/realms/debroking/dsbpkc/");
    private static readonly Uri _defaultHistoryAddress =
        new("https://data.definedgesecurities.com/sds/history/");
    private static readonly Uri _defaultMasterAddress =
        new("https://app.definedgesecurities.com/public/allmaster.zip");
    private static readonly Uri _defaultWebSocketAddress =
        new("wss://trade.definedgesecurities.com/NorenWSTRTP/");

    /// <inheritdoc />
    [Display(
        Name = "API token",
        Description = "API token created in the Definedge developer portal.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Key { get; set; }

    /// <inheritdoc />
    [Display(
        Name = "API secret",
        Description = "API secret created in the Definedge developer portal.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public SecureString Secret { get; set; }

    /// <inheritdoc />
    [Display(
        Name = "API session key",
        Description = "REST authorization key returned by Definedge login.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    [BasicSetting]
    public SecureString Token { get; set; }

    /// <summary>
    /// Streaming session token.
    /// </summary>
    [Display(
        Name = "WebSocket token",
        Description = "Streaming token returned by Definedge login.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    [BasicSetting]
    public SecureString WebSocketToken { get; set; }

    /// <summary>
    /// Current one-time password.
    /// </summary>
    [Display(
        Name = "One-time password",
        Description = "Current OTP or TOTP used only when creating a new API session.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 4)]
    public SecureString OneTimePassword { get; set; }

    /// <summary>
    /// Definedge user identifier.
    /// </summary>
    [Display(
        Name = "User ID",
        Description = "User identifier returned by Definedge login.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 5)]
    [BasicSetting]
    public string UserId { get; set; }

    /// <summary>
    /// Definedge trading account identifier.
    /// </summary>
    [Display(
        Name = "Account ID",
        Description = "Trading account identifier returned by Definedge login.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 6)]
    [BasicSetting]
    public string AccountId { get; set; }

    /// <summary>
    /// Default order product.
    /// </summary>
    [Display(
        Name = "Default product",
        Description = "Default Definedge product for new orders.",
        GroupName = LocalizedStrings.OrderKey,
        Order = 7)]
    public DefinedgeProducts DefaultProduct { get; set; } =
        DefinedgeProducts.Delivery;

    /// <summary>
    /// Algorithm identifier required by Definedge order entry.
    /// </summary>
    [Display(
        Name = "Algo ID",
        Description = "Registered algorithm ID, or the generic Definedge value 99999.",
        GroupName = LocalizedStrings.OrderKey,
        Order = 8)]
    public string AlgoId { get; set; } = "99999";

    /// <summary>
    /// REST API root address.
    /// </summary>
    [Display(
        Name = "REST address",
        Description = "Definedge REST API root address.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 9)]
    public Uri Address { get; set; } = _defaultAddress;

    /// <summary>
    /// Authentication API root address.
    /// </summary>
    [Display(
        Name = "Login address",
        Description = "Definedge authentication API root address.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 10)]
    public Uri LoginAddress { get; set; } = _defaultLoginAddress;

    /// <summary>
    /// Historical data API root address.
    /// </summary>
    [Display(
        Name = "History address",
        Description = "Definedge historical data API root address.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 11)]
    public Uri HistoryAddress { get; set; } = _defaultHistoryAddress;

    /// <summary>
    /// Public instrument master address.
    /// </summary>
    [Display(
        Name = "Instrument master address",
        Description = "Public Definedge all-market instrument archive.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 12)]
    public Uri InstrumentMasterAddress { get; set; } =
        _defaultMasterAddress;

    /// <summary>
    /// Streaming API address.
    /// </summary>
    [Display(
        Name = "WebSocket address",
        Description = "Definedge streaming WebSocket address.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 13)]
    public Uri WebSocketAddress { get; set; } =
        _defaultWebSocketAddress;

    /// <summary>
    /// Interval for REST account and order snapshots.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.IntervalKey,
        Description = LocalizedStrings.IntervalKey +
            LocalizedStrings.Dot,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 14)]
    public TimeSpan PollingInterval { get; set; } =
        TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Key), Key)
            .Set(nameof(Secret), Secret)
            .Set(nameof(Token), Token)
            .Set(nameof(WebSocketToken), WebSocketToken)
            .Set(nameof(OneTimePassword), OneTimePassword)
            .Set(nameof(UserId), UserId)
            .Set(nameof(AccountId), AccountId)
            .Set(nameof(DefaultProduct), DefaultProduct)
            .Set(nameof(AlgoId), AlgoId)
            .Set(nameof(Address), Address)
            .Set(nameof(LoginAddress), LoginAddress)
            .Set(nameof(HistoryAddress), HistoryAddress)
            .Set(nameof(InstrumentMasterAddress), InstrumentMasterAddress)
            .Set(nameof(WebSocketAddress), WebSocketAddress)
            .Set(nameof(PollingInterval), PollingInterval);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Key = storage.GetValue<SecureString>(nameof(Key));
        Secret = storage.GetValue<SecureString>(nameof(Secret));
        Token = storage.GetValue<SecureString>(nameof(Token));
        WebSocketToken =
            storage.GetValue<SecureString>(nameof(WebSocketToken));
        OneTimePassword =
            storage.GetValue<SecureString>(nameof(OneTimePassword));
        UserId = storage.GetValue<string>(nameof(UserId));
        AccountId = storage.GetValue<string>(nameof(AccountId));
        DefaultProduct =
            storage.GetValue(nameof(DefaultProduct), DefaultProduct);
        AlgoId = storage.GetValue(nameof(AlgoId), AlgoId);
        Address = storage.GetValue(nameof(Address), Address);
        LoginAddress =
            storage.GetValue(nameof(LoginAddress), LoginAddress);
        HistoryAddress =
            storage.GetValue(nameof(HistoryAddress), HistoryAddress);
        InstrumentMasterAddress = storage.GetValue(
            nameof(InstrumentMasterAddress),
            InstrumentMasterAddress);
        WebSocketAddress =
            storage.GetValue(nameof(WebSocketAddress), WebSocketAddress);
        PollingInterval =
            storage.GetValue(nameof(PollingInterval), PollingInterval);
    }
}
