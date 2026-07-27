namespace StockSharp.MasterLink;

/// <summary>
/// The message adapter for Taishin Nova API, formerly MasterLink Securities.
/// </summary>
[MediaIcon(Media.MediaNames.masterlink)]
[Doc("topics/api/connectors/stock_market/masterlink.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.MasterLinkKey,
    Description = LocalizedStrings.StockConnectorKey,
    GroupName = LocalizedStrings.TaiwanStockExchangeKey)]
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
[OrderCondition(typeof(MasterLinkOrderCondition))]
public partial class MasterLinkMessageAdapter :
    MessageAdapter, ILoginPasswordAdapter
{
    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.LoginKey,
        Description = LocalizedStrings.LoginDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public string Login { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PasswordKey,
        Description = LocalizedStrings.PasswordDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public SecureString Password { get; set; }

    /// <summary>PFX certificate path accepted by the official Nova SDK.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.CertificateKey,
        Description = "Path to the Taishin securities PFX certificate.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    [BasicSetting]
    public string CertificatePath { get; set; }

    /// <summary>PFX certificate password.</summary>
    [Display(
        Name = "Certificate password",
        Description = "Password for the Taishin securities PFX certificate.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    [BasicSetting]
    public SecureString CertificatePassword { get; set; }

    /// <summary>
    /// Optional full brokerage account returned by login. An empty value
    /// selects the first stock account.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AccountKey,
        Description = "Full brokerage account returned by Nova API. Empty selects the first stock account.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 4)]
    public string Account { get; set; }

    /// <summary>
    /// Run the broker's one-time API qualification operation after login.
    /// </summary>
    [Display(
        Name = "Register API access",
        Description = "Run the official one-time registerApiAuth operation after login.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 5)]
    public bool RegisterApiAuth { get; set; }

    /// <summary>Official Nova market-data endpoint mode.</summary>
    [Display(
        Name = "Market-data mode",
        Description = "Normal or low-latency Nova WebSocket endpoint.",
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 6)]
    public MasterLinkMarketDataModes MarketDataMode { get; set; } =
        MasterLinkMarketDataModes.Normal;

    /// <summary>Node.js executable path or command name.</summary>
    [Display(
        Name = "Node.js path",
        Description = "Path or command name of Node.js 16 or newer.",
        GroupName = "Gateway",
        Order = 7)]
    [BasicSetting]
    public string NodePath { get; set; } = "node";

    /// <summary>
    /// Directory containing the bundled gateway and installed official
    /// <c>taishin-sdk</c> package.
    /// </summary>
    [Display(
        Name = "Gateway directory",
        Description = "Directory containing masterlink_gateway.cjs, package.json, and node_modules/taishin-sdk.",
        GroupName = "Gateway",
        Order = 8)]
    [BasicSetting]
    public string GatewayDirectory { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "MasterLinkGateway");

    /// <summary>Request adjusted historical candles.</summary>
    [Display(
        Name = "Adjusted candles",
        Description = "Request split- and dividend-adjusted historical candles.",
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 9)]
    public bool AdjustedCandles { get; set; } = true;

    /// <summary>Interval for refreshing orders and account data.</summary>
    [Display(
        Name = "Account polling interval",
        Description = "Interval for refreshing order status, fills, cash, and positions.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 10)]
    public TimeSpan AccountPollingInterval { get; set; } =
        TimeSpan.FromSeconds(15);

    /// <summary>Maximum securities emitted by an unrestricted lookup.</summary>
    [Display(
        Name = "Lookup limit",
        Description = "Maximum number of Taiwan securities emitted by one unrestricted lookup.",
        GroupName = "Limits",
        Order = 11)]
    public int MaxLookupResults { get; set; } = 5000;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Login), Login)
            .Set(nameof(Password), Password)
            .Set(nameof(CertificatePath), CertificatePath)
            .Set(nameof(CertificatePassword), CertificatePassword)
            .Set(nameof(Account), Account)
            .Set(nameof(RegisterApiAuth), RegisterApiAuth)
            .Set(nameof(MarketDataMode), MarketDataMode)
            .Set(nameof(NodePath), NodePath)
            .Set(nameof(GatewayDirectory), GatewayDirectory)
            .Set(nameof(AdjustedCandles), AdjustedCandles)
            .Set(nameof(AccountPollingInterval), AccountPollingInterval)
            .Set(nameof(MaxLookupResults), MaxLookupResults);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Login = storage.GetValue<string>(nameof(Login));
        Password = storage.GetValue<SecureString>(nameof(Password));
        CertificatePath = storage.GetValue<string>(nameof(CertificatePath));
        CertificatePassword =
            storage.GetValue<SecureString>(nameof(CertificatePassword));
        Account = storage.GetValue<string>(nameof(Account));
        RegisterApiAuth = storage.GetValue(
            nameof(RegisterApiAuth), RegisterApiAuth);
        MarketDataMode = storage.GetValue(
            nameof(MarketDataMode), MarketDataMode);
        NodePath = storage.GetValue(nameof(NodePath), NodePath);
        GatewayDirectory = storage.GetValue(
            nameof(GatewayDirectory), GatewayDirectory);
        AdjustedCandles = storage.GetValue(
            nameof(AdjustedCandles), AdjustedCandles);
        AccountPollingInterval = storage.GetValue(
            nameof(AccountPollingInterval), AccountPollingInterval);
        MaxLookupResults = storage.GetValue(
            nameof(MaxLookupResults), MaxLookupResults);
    }
}
