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
        Description = LocalizedStrings.PrimaryApiUsernameDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public string Login { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PasswordKey,
        Description = LocalizedStrings.PrimaryApiPasswordDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public SecureString Password { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AccessTokenKey,
        Description = LocalizedStrings.ExistingXAuthTokenEmptyAuthenticatesWithUsernameAndPasswordDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    public SecureString Token { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DemoKey,
        Description = LocalizedStrings.UseTheFreeReMarketsSimulationEnvironmentDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    [BasicSetting]
    public bool IsDemo { get; set; } = true;

    /// <summary>Trading account identifier.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AccountKey,
        Description = LocalizedStrings.PrimaryTradingAccountRequiredForOrdersPositionsAndAccountReportsDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 4)]
    [BasicSetting]
    public string Account { get; set; }

    /// <summary>
    /// Default proprietary identifier used when an order is not known locally.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ProprietaryKey,
        Description = LocalizedStrings.PrimaryParticipantIdentifierEmptyUsesPbcpInReMarketsAndApiInProductionDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 5)]
    public string Proprietary { get; set; }

    /// <summary>Native market identifier for manually entered securities.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DefaultMarketKey,
        Description = LocalizedStrings.PrimaryNativeMarketIdentifierNormallyRofxEvenForRoutedExternalInstrumentsDescKey,
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 6)]
    public string DefaultMarket { get; set; } = "ROFX";

    /// <summary>WebSocket market-data throttling level.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.MarketDataLevelKey,
        Description = LocalizedStrings.PrimaryWebSocketUpdateLevelFrom1100MsTo56000MsDescKey,
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 7)]
    public int MarketDataLevel { get; set; } = 1;

    /// <summary>REST fallback interval for account data and orders.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AccountPollingIntervalKey,
        Description = LocalizedStrings.RestFallbackIntervalForOrdersBalancesAndPositionsDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 8)]
    public TimeSpan AccountPollingInterval { get; set; } =
        TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum number of securities emitted by an unrestricted lookup.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.LookupLimitKey,
        Description = LocalizedStrings.MaximumInstrumentsEmittedByOneUnrestrictedLookupDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 9)]
    public int LookupLimit { get; set; } = 10000;

    /// <summary>Production REST API address.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.RestAddressKey,
        Description = LocalizedStrings.PrimaryProductionRestApiRootABrokerSpecificXOMSAddressCanBeSuppliedDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 10)]
    public Uri RestAddress { get; set; } = _productionRestAddress;

    /// <summary>reMarkets REST API address.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SandboxRestAddressKey,
        Description = LocalizedStrings.PrimaryReMarketsRestApiRootDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 11)]
    public Uri SandboxRestAddress { get; set; } = _sandboxRestAddress;

    /// <summary>Production WebSocket API address.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.WebSocketAddressKey,
        Description = LocalizedStrings.PrimaryProductionWebSocketRootABrokerSpecificXOMSAddressCanBeSuppliedDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 12)]
    public Uri WebSocketAddress { get; set; } =
        _productionWebSocketAddress;

    /// <summary>reMarkets WebSocket API address.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SandboxWebSocketAddressKey,
        Description = LocalizedStrings.PrimaryReMarketsWebSocketRootDescKey,
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
