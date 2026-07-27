namespace StockSharp.Ppi;

/// <summary>
/// The message adapter for Portfolio Personal Inversiones API.
/// </summary>
[MediaIcon(Media.MediaNames.ppi)]
[Doc("topics/api/connectors/stock_market/ppi.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.PpiKey,
    Description = LocalizedStrings.StockConnectorKey,
    GroupName = LocalizedStrings.ArgentinaKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.US |
    MessageAdapterCategories.Free |
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
    MessageAdapterCategories.Commodities)]
[OrderCondition(typeof(PpiOrderCondition))]
public partial class PpiMessageAdapter :
    MessageAdapter, IKeySecretAdapter, ITokenAdapter, IDemoAdapter
{
    private static readonly Uri _productionRestAddress =
        new("https://clientapi.portfoliopersonal.com/api/");
    private static readonly Uri _sandboxRestAddress =
        new("https://clientapisandbox.portfoliopersonal.com/api/");
    private static readonly Uri _productionRealtimeAddress =
        new("https://realtimeclientapi.portfoliopersonal.com/");
    private static readonly Uri _sandboxRealtimeAddress =
        new("https://realtimeclientapi-sandbox.portfoliopersonal.com/");

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.KeyKey,
        Description = "API key generated in PPI service management.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Key { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SecretKey,
        Description = "API secret generated together with the PPI API key.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public SecureString Secret { get; set; }

    /// <summary>Authorized PPI API client identifier.</summary>
    [Display(
        Name = "Authorized client",
        Description = "AuthorizedClient header supplied by PPI.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    [BasicSetting]
    public string AuthorizedClient { get; set; } = "API_CLI_PYTHON";

    /// <summary>PPI application client key.</summary>
    [Display(
        Name = "Client key",
        Description = "ClientKey header supplied by PPI. Empty uses the official SDK default for the selected environment.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    [BasicSetting]
    public SecureString ClientKey { get; set; }

    /// <inheritdoc />
    [Display(
        Name = "Access token",
        Description = "Existing bearer token. Empty logs in with the API key and secret.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 4)]
    public SecureString Token { get; set; }

    /// <summary>Token used to renew an expired bearer session.</summary>
    [Display(
        Name = "Refresh token",
        Description = "Existing PPI refresh token. It is updated after authentication.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 5)]
    public SecureString RefreshToken { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DemoKey,
        Description = "Use the PPI sandbox environment.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 6)]
    [BasicSetting]
    public bool IsDemo { get; set; }

    /// <summary>
    /// Trading account number. An empty value selects the first account
    /// returned by PPI.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AccountKey,
        Description = "PPI account number. Empty selects the first available account.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 7)]
    [BasicSetting]
    public string Account { get; set; }

    /// <summary>Default market used when a security has no native metadata.</summary>
    [Display(
        Name = "Default market",
        Description = "Default PPI market code, normally BYMA.",
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 8)]
    public string DefaultMarket { get; set; } = "BYMA";

    /// <summary>
    /// Default instrument type used when a security has no native metadata.
    /// </summary>
    [Display(
        Name = "Default instrument type",
        Description = "Default PPI instrument type, normally ACCIONES.",
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 9)]
    public string DefaultInstrumentType { get; set; } = "ACCIONES";

    /// <summary>Default settlement used for data and new orders.</summary>
    [Display(
        Name = "Default settlement",
        Description = "Default PPI settlement code, for example A-24HS or INMEDIATA.",
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 10)]
    public string DefaultSettlement { get; set; } = "A-24HS";

    /// <summary>REST fallback interval for account data and orders.</summary>
    [Display(
        Name = "Account polling interval",
        Description = "REST fallback interval for orders, balances, and positions.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 11)]
    public TimeSpan AccountPollingInterval { get; set; } =
        TimeSpan.FromSeconds(15);

    /// <summary>
    /// Maximum number of securities emitted by an unrestricted lookup.
    /// </summary>
    [Display(
        Name = "Lookup limit",
        Description = "Maximum instruments emitted by one unrestricted lookup.",
        GroupName = "Limits",
        Order = 12)]
    public int LookupLimit { get; set; } = 5000;

    /// <summary>Production REST API address.</summary>
    [Display(
        Name = "REST address",
        Description = "PPI production REST API root.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 13)]
    public Uri RestAddress { get; set; } = _productionRestAddress;

    /// <summary>Sandbox REST API address.</summary>
    [Display(
        Name = "Sandbox REST address",
        Description = "PPI sandbox REST API root.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 14)]
    public Uri SandboxRestAddress { get; set; } = _sandboxRestAddress;

    /// <summary>Production SignalR address.</summary>
    [Display(
        Name = "Realtime address",
        Description = "PPI production SignalR server root.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 15)]
    public Uri RealtimeAddress { get; set; } = _productionRealtimeAddress;

    /// <summary>Sandbox SignalR address.</summary>
    [Display(
        Name = "Sandbox realtime address",
        Description = "PPI sandbox SignalR server root.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 16)]
    public Uri SandboxRealtimeAddress { get; set; } =
        _sandboxRealtimeAddress;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Key), Key)
            .Set(nameof(Secret), Secret)
            .Set(nameof(AuthorizedClient), AuthorizedClient)
            .Set(nameof(ClientKey), ClientKey)
            .Set(nameof(Token), Token)
            .Set(nameof(RefreshToken), RefreshToken)
            .Set(nameof(IsDemo), IsDemo)
            .Set(nameof(Account), Account)
            .Set(nameof(DefaultMarket), DefaultMarket)
            .Set(nameof(DefaultInstrumentType), DefaultInstrumentType)
            .Set(nameof(DefaultSettlement), DefaultSettlement)
            .Set(nameof(AccountPollingInterval), AccountPollingInterval)
            .Set(nameof(LookupLimit), LookupLimit)
            .Set(nameof(RestAddress), RestAddress)
            .Set(nameof(SandboxRestAddress), SandboxRestAddress)
            .Set(nameof(RealtimeAddress), RealtimeAddress)
            .Set(nameof(SandboxRealtimeAddress), SandboxRealtimeAddress);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Key = storage.GetValue<SecureString>(nameof(Key));
        Secret = storage.GetValue<SecureString>(nameof(Secret));
        AuthorizedClient = storage.GetValue(
            nameof(AuthorizedClient), AuthorizedClient);
        ClientKey = storage.GetValue<SecureString>(nameof(ClientKey));
        Token = storage.GetValue<SecureString>(nameof(Token));
        RefreshToken = storage.GetValue<SecureString>(nameof(RefreshToken));
        IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
        Account = storage.GetValue(nameof(Account), Account);
        DefaultMarket = storage.GetValue(
            nameof(DefaultMarket), DefaultMarket);
        DefaultInstrumentType = storage.GetValue(
            nameof(DefaultInstrumentType), DefaultInstrumentType);
        DefaultSettlement = storage.GetValue(
            nameof(DefaultSettlement), DefaultSettlement);
        AccountPollingInterval = storage.GetValue(
            nameof(AccountPollingInterval), AccountPollingInterval);
        LookupLimit = storage.GetValue(nameof(LookupLimit), LookupLimit);
        RestAddress = storage.GetValue(nameof(RestAddress), RestAddress);
        SandboxRestAddress = storage.GetValue(
            nameof(SandboxRestAddress), SandboxRestAddress);
        RealtimeAddress = storage.GetValue(
            nameof(RealtimeAddress), RealtimeAddress);
        SandboxRealtimeAddress = storage.GetValue(
            nameof(SandboxRealtimeAddress), SandboxRealtimeAddress);
    }
}
