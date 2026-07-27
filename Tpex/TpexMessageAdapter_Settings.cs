namespace StockSharp.Tpex;

/// <summary>
/// Message adapter for Taipei Exchange public market data.
/// </summary>
[MediaIcon(Media.MediaNames.tpex)]
[Doc("topics/api/connectors/stock_market/tpex.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.TpexKey,
    Description = LocalizedStrings.MarketDataConnectorKey,
    GroupName = LocalizedStrings.TaiwanStockExchangeKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.Free |
    MessageAdapterCategories.Asia |
    MessageAdapterCategories.History |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.Level1 |
    MessageAdapterCategories.Candles)]
public partial class TpexMessageAdapter :
    MessageAdapter,
    IAddressAdapter<Uri>
{
    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = LocalizedStrings.TaipeiExchangeWebsiteAndOpenAPIRootDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 0)]
    [BasicSetting]
    public Uri Address { get; set; } =
        new("https://www.tpex.org.tw/");

    /// <summary>TPEx equity market selection.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.MarketKey,
        Description = LocalizedStrings.MainboardEmergingStockBoardOrBothEquityMarketsDescKey,
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 1)]
    [BasicSetting]
    public TpexMarkets Market { get; set; } =
        TpexMarkets.Mainboard;

    /// <summary>Include warrants and other listed derivatives.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.IncludeListedDerivativesKey,
        Description = LocalizedStrings.IncludeMainboardWarrantsAndOtherNonEquityListingsFundsAndETFsRemainIncludedWhenDisabledDescKey,
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 2)]
    public bool IncludeListedDerivatives { get; set; }

    /// <summary>Load current Mainboard valuation ratios.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.IncludeValuationsKey,
        Description = LocalizedStrings.LoadCurrentMainboardPriceToEarningsDividendYieldAndPriceToBookRatiosDescKey,
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 3)]
    public bool IncludeValuations { get; set; } = true;

    /// <summary>Duration for which the current snapshot is reused.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.CacheTimeoutKey,
        Description = LocalizedStrings.DurationForWhichDownloadedTPExCurrentDataIsReusedZeroDisablesCachingDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 4)]
    public TimeSpan CacheTimeout { get; set; } =
        TimeSpan.FromMinutes(5);

    /// <summary>Maximum calendar months in one history request.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.MaximumHistoryMonthsKey,
        Description = LocalizedStrings.MaximumNumberOfMonthlyPublicHistoryRequestsIssuedForOneSubscriptionDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 5)]
    public int MaxHistoryMonths { get; set; } = 120;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Address), Address)
            .Set(nameof(Market), Market)
            .Set(
                nameof(IncludeListedDerivatives),
                IncludeListedDerivatives)
            .Set(nameof(IncludeValuations), IncludeValuations)
            .Set(nameof(CacheTimeout), CacheTimeout)
            .Set(nameof(MaxHistoryMonths), MaxHistoryMonths);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Address = storage.GetValue(nameof(Address), Address);
        Market = storage.GetValue(nameof(Market), Market);
        IncludeListedDerivatives = storage.GetValue(
            nameof(IncludeListedDerivatives),
            IncludeListedDerivatives);
        IncludeValuations = storage.GetValue(
            nameof(IncludeValuations), IncludeValuations);
        CacheTimeout = storage.GetValue(
            nameof(CacheTimeout), CacheTimeout);
        MaxHistoryMonths = storage.GetValue(
            nameof(MaxHistoryMonths), MaxHistoryMonths);
    }
}
