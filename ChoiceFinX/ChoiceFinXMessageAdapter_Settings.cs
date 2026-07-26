namespace StockSharp.ChoiceFinX;

/// <summary>
/// The message adapter for Choice FinX OpenAPI.
/// </summary>
[MediaIcon(Media.MediaNames.choicefinx)]
[Doc("topics/api/connectors/stock_market/choice_finx.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.ChoiceFinXKey,
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
    MessageAdapterCategories.Options |
    MessageAdapterCategories.FX |
    MessageAdapterCategories.Commodities)]
[OrderCondition(typeof(ChoiceFinXOrderCondition))]
public partial class ChoiceFinXMessageAdapter :
    MessageAdapter, ITokenAdapter
{
    private static readonly Uri _defaultAddress =
        new("https://finx.choiceindia.com/");
    private static readonly Uri _defaultWebSocketAddress =
        new("wss://finxsocket.choiceindia.com/ws/");

    /// <inheritdoc />
    [Display(
        Name = "API key or Session ID",
        Description = "Choice FinX API key or SessionId used in the Authorization header.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Token { get; set; }

    /// <summary>
    /// Header carrying the API key or session id.
    /// </summary>
    [Display(
        Name = "Authorization header",
        Description = "Header carrying the credential. Use Authorization for SessionId mode or Bearer when required by vendor credentials.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public string AuthorizationHeader { get; set; } =
        "Authorization";

    /// <summary>
    /// Authorization header scheme.
    /// </summary>
    [Display(
        Name = "Authorization scheme",
        Description = "Authorization prefix. SessionId is used by the public REST reference; leave empty for a raw API key.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    [BasicSetting]
    public string AuthorizationScheme { get; set; } =
        "SessionId";

    /// <summary>
    /// Vendor identifier issued by Choice.
    /// </summary>
    [Display(
        Name = "Vendor ID",
        Description = "Optional vendor identifier for an empanelled integration.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    public string VendorId { get; set; }

    /// <summary>
    /// Vendor key issued by Choice.
    /// </summary>
    [Display(
        Name = "Vendor key",
        Description = "Optional vendor secret for an empanelled integration.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 4)]
    public SecureString VendorKey { get; set; }

    /// <summary>
    /// JWT returned by the Choice FinX 2FA logon flow.
    /// </summary>
    [Display(
        Name = "WebSocket JWT",
        Description = "Optional JWT returned by logon and used by the interactive WebSocket.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 5)]
    public SecureString WebSocketToken { get; set; }

    /// <summary>
    /// Default product used for new orders.
    /// </summary>
    [Display(
        Name = "Default product",
        Description = "Default Choice FinX delivery or intraday product.",
        GroupName = LocalizedStrings.OrderKey,
        Order = 6)]
    public ChoiceFinXProducts DefaultProduct { get; set; } =
        ChoiceFinXProducts.Delivery;

    /// <summary>
    /// Portfolio name emitted by the connector.
    /// </summary>
    [Display(
        Name = "Portfolio name",
        Description = "Portfolio name. When empty, the user id returned by Choice FinX is used.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 7)]
    public string PortfolioName { get; set; }

    /// <summary>
    /// Native order mode type.
    /// </summary>
    [Display(
        Name = "Mode type",
        Description = "Optional Choice FinX ModeTyp value sent with order requests.",
        GroupName = LocalizedStrings.OrderKey,
        Order = 8)]
    public string ModeType { get; set; }

    /// <summary>
    /// Native order mode.
    /// </summary>
    [Display(
        Name = "Mode",
        Description = "Optional Choice FinX numeric Mode value sent with order requests.",
        GroupName = LocalizedStrings.OrderKey,
        Order = 9)]
    public int? Mode { get; set; }

    /// <summary>
    /// Device identifier sent with order requests.
    /// </summary>
    [Display(
        Name = "Device ID",
        Description = "Optional Choice FinX device identifier.",
        GroupName = LocalizedStrings.OrderKey,
        Order = 10)]
    public string DeviceId { get; set; }

    /// <summary>
    /// Price divisor used by native order fields and responses
    /// that omit their own divisor.
    /// </summary>
    [Display(
        Name = "Price divisor",
        Description = "Native price divisor. Choice FinX documents paise-based order prices, so the default is 100.",
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 11)]
    public decimal PriceDivisor { get; set; } = 100;

    /// <summary>
    /// REST API root address.
    /// </summary>
    [Display(
        Name = "REST address",
        Description = "Choice FinX REST API root address.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 12)]
    public Uri Address { get; set; } = _defaultAddress;

    /// <summary>
    /// Interactive WebSocket address.
    /// </summary>
    [Display(
        Name = "WebSocket address",
        Description = "Choice FinX interactive WebSocket address.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 13)]
    public Uri WebSocketAddress { get; set; } =
        _defaultWebSocketAddress;

    /// <summary>
    /// Interval for REST market, order, and portfolio snapshots.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.IntervalKey,
        Description = LocalizedStrings.IntervalKey +
            LocalizedStrings.Dot,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 14)]
    public TimeSpan PollingInterval { get; set; } =
        TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Token), Token)
            .Set(
                nameof(AuthorizationHeader),
                AuthorizationHeader)
            .Set(
                nameof(AuthorizationScheme),
                AuthorizationScheme)
            .Set(nameof(VendorId), VendorId)
            .Set(nameof(VendorKey), VendorKey)
            .Set(nameof(WebSocketToken), WebSocketToken)
            .Set(nameof(DefaultProduct), DefaultProduct)
            .Set(nameof(PortfolioName), PortfolioName)
            .Set(nameof(ModeType), ModeType)
            .Set(nameof(Mode), Mode)
            .Set(nameof(DeviceId), DeviceId)
            .Set(nameof(PriceDivisor), PriceDivisor)
            .Set(nameof(Address), Address)
            .Set(nameof(WebSocketAddress), WebSocketAddress)
            .Set(nameof(PollingInterval), PollingInterval);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Token = storage.GetValue<SecureString>(nameof(Token));
        AuthorizationHeader = storage.GetValue(
            nameof(AuthorizationHeader),
            AuthorizationHeader);
        AuthorizationScheme = storage.GetValue(
            nameof(AuthorizationScheme),
            AuthorizationScheme);
        VendorId = storage.GetValue<string>(nameof(VendorId));
        VendorKey =
            storage.GetValue<SecureString>(nameof(VendorKey));
        WebSocketToken = storage.GetValue<SecureString>(
            nameof(WebSocketToken));
        DefaultProduct = storage.GetValue(
            nameof(DefaultProduct), DefaultProduct);
        PortfolioName =
            storage.GetValue<string>(nameof(PortfolioName));
        ModeType =
            storage.GetValue(nameof(ModeType), ModeType);
        Mode = storage.GetValue(nameof(Mode), Mode);
        DeviceId =
            storage.GetValue<string>(nameof(DeviceId));
        PriceDivisor = storage.GetValue(
            nameof(PriceDivisor), PriceDivisor);
        Address = storage.GetValue(nameof(Address), Address);
        WebSocketAddress = storage.GetValue(
            nameof(WebSocketAddress), WebSocketAddress);
        PollingInterval = storage.GetValue(
            nameof(PollingInterval), PollingInterval);
    }
}
