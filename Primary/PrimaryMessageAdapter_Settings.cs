namespace StockSharp.Primary;

/// <summary>
/// The message adapter for Primary REST and WebSocket APIs.
/// </summary>
[MediaIcon(Media.MediaNames.primary)]
[Doc("topics/api/connectors/stock_market/primary.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.PrimaryKey,
    Description = LocalizedStrings.StockConnectorKey,
    GroupName = LocalizedStrings.ArgentinaKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.Free |
    MessageAdapterCategories.RealTime |
    MessageAdapterCategories.History |
    MessageAdapterCategories.Transactions |
    MessageAdapterCategories.Ticks |
    MessageAdapterCategories.Level1 |
    MessageAdapterCategories.MarketDepth |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.Futures |
    MessageAdapterCategories.Options |
    MessageAdapterCategories.Commodities)]
[OrderCondition(typeof(PrimaryOrderCondition))]
public partial class PrimaryMessageAdapter :
    MessageAdapter, ILoginPasswordAdapter, ITokenAdapter, IDemoAdapter
{
    private static readonly Uri _productionRestAddress =
        new("https://api.primary.com.ar/");
    private static readonly Uri _sandboxRestAddress =
        new("https://api.remarkets.primary.com.ar/");
    private static readonly Uri _productionWebSocketAddress =
        new("wss://api.primary.com.ar/");
    private static readonly Uri _sandboxWebSocketAddress =
        new("wss://api.remarkets.primary.com.ar/");

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.LoginKey,
        Description = "Primary API username.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public string Login { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PasswordKey,
        Description = "Primary API password.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public SecureString Password { get; set; }

    /// <inheritdoc />
    [Display(
        Name = "Access token",
        Description = "Existing X-Auth-Token. Empty authenticates with username and password.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    public SecureString Token { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DemoKey,
        Description = "Use the free reMarkets simulation environment.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    [BasicSetting]
    public bool IsDemo { get; set; } = true;

    /// <summary>Trading account identifier.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AccountKey,
        Description = "Primary trading account. Required for orders, positions, and account reports.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 4)]
    [BasicSetting]
    public string Account { get; set; }

    /// <summary>
    /// Default proprietary identifier used when an order is not known locally.
    /// </summary>
    [Display(
        Name = "Proprietary",
        Description = "Primary participant identifier. Empty uses PBCP in reMarkets and api in production.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 5)]
    public string Proprietary { get; set; }

    /// <summary>Native market identifier for manually entered securities.</summary>
    [Display(
        Name = "Default market",
        Description = "Primary native market identifier, normally ROFX even for routed external instruments.",
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 6)]
    public string DefaultMarket { get; set; } = "ROFX";

    /// <summary>WebSocket market-data throttling level.</summary>
    [Display(
        Name = "Market data level",
        Description = "Primary WebSocket update level from 1 (100 ms) to 5 (6000 ms).",
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 7)]
    public int MarketDataLevel { get; set; } = 1;

    /// <summary>REST fallback interval for account data and orders.</summary>
    [Display(
        Name = "Account polling interval",
        Description = "REST fallback interval for orders, balances, and positions.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 8)]
    public TimeSpan AccountPollingInterval { get; set; } =
        TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum number of securities emitted by an unrestricted lookup.
    /// </summary>
    [Display(
        Name = "Lookup limit",
        Description = "Maximum instruments emitted by one unrestricted lookup.",
        GroupName = "Limits",
        Order = 9)]
    public int LookupLimit { get; set; } = 10000;

    /// <summary>Production REST API address.</summary>
    [Display(
        Name = "REST address",
        Description = "Primary production REST API root. A broker-specific xOMS address can be supplied.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 10)]
    public Uri RestAddress { get; set; } = _productionRestAddress;

    /// <summary>reMarkets REST API address.</summary>
    [Display(
        Name = "Sandbox REST address",
        Description = "Primary reMarkets REST API root.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 11)]
    public Uri SandboxRestAddress { get; set; } = _sandboxRestAddress;

    /// <summary>Production WebSocket API address.</summary>
    [Display(
        Name = "WebSocket address",
        Description = "Primary production WebSocket root. A broker-specific xOMS address can be supplied.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 12)]
    public Uri WebSocketAddress { get; set; } =
        _productionWebSocketAddress;

    /// <summary>reMarkets WebSocket API address.</summary>
    [Display(
        Name = "Sandbox WebSocket address",
        Description = "Primary reMarkets WebSocket root.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 13)]
    public Uri SandboxWebSocketAddress { get; set; } =
        _sandboxWebSocketAddress;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Login), Login)
            .Set(nameof(Password), Password)
            .Set(nameof(Token), Token)
            .Set(nameof(IsDemo), IsDemo)
            .Set(nameof(Account), Account)
            .Set(nameof(Proprietary), Proprietary)
            .Set(nameof(DefaultMarket), DefaultMarket)
            .Set(nameof(MarketDataLevel), MarketDataLevel)
            .Set(nameof(AccountPollingInterval), AccountPollingInterval)
            .Set(nameof(LookupLimit), LookupLimit)
            .Set(nameof(RestAddress), RestAddress)
            .Set(nameof(SandboxRestAddress), SandboxRestAddress)
            .Set(nameof(WebSocketAddress), WebSocketAddress)
            .Set(
                nameof(SandboxWebSocketAddress),
                SandboxWebSocketAddress);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Login = storage.GetValue<string>(nameof(Login));
        Password = storage.GetValue<SecureString>(nameof(Password));
        Token = storage.GetValue<SecureString>(nameof(Token));
        IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
        Account = storage.GetValue(nameof(Account), Account);
        Proprietary = storage.GetValue(
            nameof(Proprietary), Proprietary);
        DefaultMarket = storage.GetValue(
            nameof(DefaultMarket), DefaultMarket);
        MarketDataLevel = storage.GetValue(
            nameof(MarketDataLevel), MarketDataLevel);
        AccountPollingInterval = storage.GetValue(
            nameof(AccountPollingInterval), AccountPollingInterval);
        LookupLimit = storage.GetValue(nameof(LookupLimit), LookupLimit);
        RestAddress = storage.GetValue(nameof(RestAddress), RestAddress);
        SandboxRestAddress = storage.GetValue(
            nameof(SandboxRestAddress), SandboxRestAddress);
        WebSocketAddress = storage.GetValue(
            nameof(WebSocketAddress), WebSocketAddress);
        SandboxWebSocketAddress = storage.GetValue(
            nameof(SandboxWebSocketAddress),
            SandboxWebSocketAddress);
    }
}
