namespace StockSharp.Dnse;

/// <summary>
/// The message adapter for DNSE LightSpeed OpenAPI.
/// </summary>
[MediaIcon(Media.MediaNames.dnse)]
[Doc("topics/api/connectors/stock_market/dnse.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.DnseKey,
    Description = LocalizedStrings.StockConnectorKey,
    GroupName = LocalizedStrings.VietnamKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.Asia |
    MessageAdapterCategories.Free |
    MessageAdapterCategories.RealTime |
    MessageAdapterCategories.History |
    MessageAdapterCategories.Candles |
    MessageAdapterCategories.Transactions |
    MessageAdapterCategories.Ticks |
    MessageAdapterCategories.Level1 |
    MessageAdapterCategories.MarketDepth |
    MessageAdapterCategories.Stock)]
[OrderCondition(typeof(DnseOrderCondition))]
public partial class DnseMessageAdapter :
    MessageAdapter, IKeySecretAdapter
{
    private static readonly Uri _defaultRestAddress =
        new("https://openapi.dnse.com.vn/");
    private static readonly Uri _defaultWebSocketAddress =
        new("wss://ws-openapi.dnse.com.vn/v1/stream?encoding=json");

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.KeyKey,
        Description = "API key created in DNSE Entrade X.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Key { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SecretKey,
        Description = "API secret displayed once when the DNSE API key is created.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public SecureString Secret { get; set; }

    /// <summary>
    /// Eight-hour token required for order-changing operations.
    /// </summary>
    [Display(
        Name = "Trading token",
        Description = "Eight-hour DNSE token obtained by Email OTP or Smart OTP. Read-only access works without it.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    [BasicSetting]
    public SecureString TradingToken { get; set; }

    /// <summary>Second-factor method used to obtain a trading token.</summary>
    [Display(
        Name = "OTP type",
        Description = "Second-factor method registered for the DNSE account.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    public DnseOtpTypes OtpType { get; set; } = DnseOtpTypes.Email;

    /// <summary>
    /// One-time passcode used once during connection to obtain a trading
    /// token.
    /// </summary>
    [Display(
        Name = "One-time password",
        Description = "Email or Smart OTP used once to obtain a trading token.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 4)]
    public SecureString OneTimePassword { get; set; }

    /// <summary>
    /// Request an Email OTP on the next connection when no token or OTP was
    /// supplied.
    /// </summary>
    [Display(
        Name = "Request Email OTP",
        Description = "Send an Email OTP during the next connection, then reconnect with the received code.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 5)]
    public bool RequestEmailOtpOnConnect { get; set; }

    /// <summary>
    /// Brokerage sub-account number. An empty value selects the first stock
    /// account returned by DNSE.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AccountKey,
        Description = "DNSE sub-account number. Empty selects the first stock account.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 6)]
    [BasicSetting]
    public string Account { get; set; }

    /// <summary>
    /// Default loan-package ID for orders. A per-order condition overrides
    /// this value.
    /// </summary>
    [Display(
        Name = "Loan package ID",
        Description = "Default package returned by the DNSE loan-packages endpoint and required for an order.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 7)]
    public int DefaultLoanPackageId { get; set; }

    /// <summary>Default DNSE trading-board identifier.</summary>
    [Display(
        Name = "Trading board",
        Description = "Default DNSE board identifier, normally G1 for round-lot trading.",
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 8)]
    public string DefaultBoardId { get; set; } = "G1";

    /// <summary>
    /// Multiplier converting DNSE stock market-data prices to order-price
    /// units.
    /// </summary>
    [Display(
        Name = "Price multiplier",
        Description = "DNSE stock feeds quote prices in thousands of VND while orders use VND.",
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 9)]
    public decimal MarketDataPriceMultiplier { get; set; } = 1000m;

    /// <summary>Interval for refreshing orders and account data.</summary>
    [Display(
        Name = "Account polling interval",
        Description = "REST fallback interval for orders, balances, and positions.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 10)]
    public TimeSpan AccountPollingInterval { get; set; } =
        TimeSpan.FromSeconds(15);

    /// <summary>
    /// Maximum number of securities emitted by an unrestricted lookup.
    /// </summary>
    [Display(
        Name = "Lookup limit",
        Description = "Maximum instruments emitted by one unrestricted lookup.",
        GroupName = "Limits",
        Order = 11)]
    public int LookupLimit { get; set; } = 5000;

    /// <summary>DNSE REST API version header.</summary>
    [Display(
        Name = "API version",
        Description = "DNSE REST API version sent in the version header.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 12)]
    public string ApiVersion { get; set; } = "2026-05-07";

    /// <summary>
    /// Date header included in the HMAC signature. The official SDK uses
    /// <c>Date</c>.
    /// </summary>
    [Display(
        Name = "Signature date header",
        Description = "HTTP date-header name included in the DNSE HMAC signature.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 13)]
    public string DateHeaderName { get; set; } = "Date";

    /// <summary>REST API address.</summary>
    [Display(
        Name = "REST address",
        Description = "DNSE LightSpeed REST API server root.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 14)]
    public Uri RestAddress { get; set; } = _defaultRestAddress;

    /// <summary>WebSocket API address.</summary>
    [Display(
        Name = "WebSocket address",
        Description = "DNSE LightSpeed real-time stream address.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 15)]
    public Uri WebSocketAddress { get; set; } =
        _defaultWebSocketAddress;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Key), Key)
            .Set(nameof(Secret), Secret)
            .Set(nameof(TradingToken), TradingToken)
            .Set(nameof(OtpType), OtpType)
            .Set(nameof(OneTimePassword), OneTimePassword)
            .Set(nameof(RequestEmailOtpOnConnect), RequestEmailOtpOnConnect)
            .Set(nameof(Account), Account)
            .Set(nameof(DefaultLoanPackageId), DefaultLoanPackageId)
            .Set(nameof(DefaultBoardId), DefaultBoardId)
            .Set(nameof(MarketDataPriceMultiplier), MarketDataPriceMultiplier)
            .Set(nameof(AccountPollingInterval), AccountPollingInterval)
            .Set(nameof(LookupLimit), LookupLimit)
            .Set(nameof(ApiVersion), ApiVersion)
            .Set(nameof(DateHeaderName), DateHeaderName)
            .Set(nameof(RestAddress), RestAddress)
            .Set(nameof(WebSocketAddress), WebSocketAddress);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Key = storage.GetValue<SecureString>(nameof(Key));
        Secret = storage.GetValue<SecureString>(nameof(Secret));
        TradingToken =
            storage.GetValue<SecureString>(nameof(TradingToken));
        OtpType = storage.GetValue(nameof(OtpType), OtpType);
        OneTimePassword =
            storage.GetValue<SecureString>(nameof(OneTimePassword));
        RequestEmailOtpOnConnect = storage.GetValue(
            nameof(RequestEmailOtpOnConnect),
            RequestEmailOtpOnConnect);
        Account = storage.GetValue(nameof(Account), Account);
        DefaultLoanPackageId = storage.GetValue(
            nameof(DefaultLoanPackageId),
            DefaultLoanPackageId);
        DefaultBoardId = storage.GetValue(
            nameof(DefaultBoardId), DefaultBoardId);
        MarketDataPriceMultiplier = storage.GetValue(
            nameof(MarketDataPriceMultiplier),
            MarketDataPriceMultiplier);
        AccountPollingInterval = storage.GetValue(
            nameof(AccountPollingInterval),
            AccountPollingInterval);
        LookupLimit =
            storage.GetValue(nameof(LookupLimit), LookupLimit);
        ApiVersion =
            storage.GetValue(nameof(ApiVersion), ApiVersion);
        DateHeaderName = storage.GetValue(
            nameof(DateHeaderName), DateHeaderName);
        RestAddress =
            storage.GetValue(nameof(RestAddress), RestAddress);
        WebSocketAddress = storage.GetValue(
            nameof(WebSocketAddress), WebSocketAddress);
    }
}
