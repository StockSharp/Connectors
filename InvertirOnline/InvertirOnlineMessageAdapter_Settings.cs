namespace StockSharp.InvertirOnline;

/// <summary>The message adapter for the IOL InvertirOnline public API.</summary>
[MediaIcon(Media.MediaNames.invertironline)]
[Doc("topics/api/connectors/stock_market/invertironline.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.InvertirOnlineKey,
    Description = LocalizedStrings.StockConnectorKey,
    GroupName = LocalizedStrings.ArgentinaKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.US |
    MessageAdapterCategories.Free |
    MessageAdapterCategories.RealTime |
    MessageAdapterCategories.History |
    MessageAdapterCategories.Candles |
    MessageAdapterCategories.Transactions |
    MessageAdapterCategories.Level1 |
    MessageAdapterCategories.MarketDepth |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.Futures |
    MessageAdapterCategories.Options |
    MessageAdapterCategories.FX |
    MessageAdapterCategories.Commodities)]
[OrderCondition(typeof(InvertirOnlineOrderCondition))]
public partial class InvertirOnlineMessageAdapter :
    MessageAdapter, ILoginPasswordAdapter, ITokenAdapter, IDemoAdapter
{
    private static readonly Uri _productionAddress =
        new("https://api.invertironline.com/");
    private static readonly Uri _sandboxAddress =
        new("https://api.homo.invertironline.com/");

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.LoginKey,
        Description = LocalizedStrings.IolAccountUsernameDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public string Login { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PasswordKey,
        Description = LocalizedStrings.IolAccountPasswordDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public SecureString Password { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AccessTokenKey,
        Description = LocalizedStrings.ExistingIolBearerTokenEmptyAuthenticatesWithTheUsernameAndPasswordDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    public SecureString Token { get; set; }

    /// <summary>Token used to renew an expired bearer session.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.RefreshTokenKey,
        Description = LocalizedStrings.ExistingIolRefreshTokenItIsUpdatedAfterAuthenticationDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    public SecureString RefreshToken { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DemoKey,
        Description = LocalizedStrings.UseTheOfficialIolSandboxEnvironmentDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 4)]
    [BasicSetting]
    public bool IsDemo { get; set; } = true;

    /// <summary>
    /// Portfolio name. An empty value uses the account number returned by IOL.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PortfolioNameKey,
        Description = LocalizedStrings.PortfolioNameEmptyUsesTheFirstIolAccountNumberDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 5)]
    [BasicSetting]
    public string PortfolioName { get; set; }

    /// <summary>Country used when security metadata is absent.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DefaultCountryKey,
        Description = LocalizedStrings.IolCountryUsedForManuallyEnteredSecuritiesDescKey,
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 6)]
    public InvertirOnlineCountries DefaultCountry { get; set; } =
        InvertirOnlineCountries.Argentina;

    /// <summary>Market used when security metadata is absent.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DefaultMarketKey,
        Description = LocalizedStrings.IolMarketCodeUsedForManuallyEnteredSecuritiesDescKey,
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 7)]
    public string DefaultMarket { get; set; } = "BCBA";

    /// <summary>Instrument group used when security metadata is absent.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DefaultInstrumentTypeKey,
        Description = LocalizedStrings.IolQuoteGroupForExampleAccionesOrCedearsDescKey,
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 8)]
    public string DefaultInstrumentType { get; set; } = "acciones";

    /// <summary>Settlement used for quotes and new orders.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DefaultSettlementKey,
        Description = LocalizedStrings.IolSettlementTermUsedForQuotesAndNewOrdersDescKey,
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 9)]
    public InvertirOnlineSettlements DefaultSettlement { get; set; } =
        InvertirOnlineSettlements.T1;

    /// <summary>Request adjusted daily history.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AdjustedHistoryKey,
        Description = LocalizedStrings.RequestSplitAdjustedDailyPriceHistoryDescKey,
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 10)]
    public bool AdjustedHistory { get; set; } = true;

    /// <summary>Interval for grouped quote polling.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.MarketDataPollingIntervalKey,
        Description = LocalizedStrings.IntervalForGroupedQuoteRequestsIolIncludes25000ApiCallsPerMonthWithoutAnExtraChargeDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 11)]
    public TimeSpan MarketDataPollingInterval { get; set; } =
        TimeSpan.FromMinutes(2);

    /// <summary>Interval for order and portfolio polling.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AccountPollingIntervalKey,
        Description = LocalizedStrings.IntervalForOrderBalanceAndPositionRequestsDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 12)]
    public TimeSpan AccountPollingInterval { get; set; } =
        TimeSpan.FromMinutes(2);

    /// <summary>
    /// Maximum number of securities emitted by an unrestricted lookup.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.LookupLimitKey,
        Description = LocalizedStrings.MaximumInstrumentsEmittedByOneUnrestrictedLookupDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 13)]
    public int LookupLimit { get; set; } = 5000;

    /// <summary>Production API root.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.RestAddressKey,
        Description = LocalizedStrings.IolProductionApiRootDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 14)]
    public Uri RestAddress { get; set; } = _productionAddress;

    /// <summary>Official sandbox API root.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SandboxRestAddressKey,
        Description = LocalizedStrings.IolOfficialSandboxApiRootDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 15)]
    public Uri SandboxRestAddress { get; set; } = _sandboxAddress;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Login), Login)
            .Set(nameof(Password), Password)
            .Set(nameof(Token), Token)
            .Set(nameof(RefreshToken), RefreshToken)
            .Set(nameof(IsDemo), IsDemo)
            .Set(nameof(PortfolioName), PortfolioName)
            .Set(nameof(DefaultCountry), DefaultCountry)
            .Set(nameof(DefaultMarket), DefaultMarket)
            .Set(nameof(DefaultInstrumentType), DefaultInstrumentType)
            .Set(nameof(DefaultSettlement), DefaultSettlement)
            .Set(nameof(AdjustedHistory), AdjustedHistory)
            .Set(
                nameof(MarketDataPollingInterval),
                MarketDataPollingInterval)
            .Set(nameof(AccountPollingInterval), AccountPollingInterval)
            .Set(nameof(LookupLimit), LookupLimit)
            .Set(nameof(RestAddress), RestAddress)
            .Set(nameof(SandboxRestAddress), SandboxRestAddress);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Login = storage.GetValue<string>(nameof(Login));
        Password = storage.GetValue<SecureString>(nameof(Password));
        Token = storage.GetValue<SecureString>(nameof(Token));
        RefreshToken = storage.GetValue<SecureString>(nameof(RefreshToken));
        IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
        PortfolioName = storage.GetValue(
            nameof(PortfolioName), PortfolioName);
        DefaultCountry = storage.GetValue(
            nameof(DefaultCountry), DefaultCountry);
        DefaultMarket = storage.GetValue(
            nameof(DefaultMarket), DefaultMarket);
        DefaultInstrumentType = storage.GetValue(
            nameof(DefaultInstrumentType), DefaultInstrumentType);
        DefaultSettlement = storage.GetValue(
            nameof(DefaultSettlement), DefaultSettlement);
        AdjustedHistory = storage.GetValue(
            nameof(AdjustedHistory), AdjustedHistory);
        MarketDataPollingInterval = storage.GetValue(
            nameof(MarketDataPollingInterval),
            MarketDataPollingInterval);
        AccountPollingInterval = storage.GetValue(
            nameof(AccountPollingInterval), AccountPollingInterval);
        LookupLimit = storage.GetValue(nameof(LookupLimit), LookupLimit);
        RestAddress = storage.GetValue(nameof(RestAddress), RestAddress);
        SandboxRestAddress = storage.GetValue(
            nameof(SandboxRestAddress), SandboxRestAddress);
    }
}
