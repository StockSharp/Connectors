namespace StockSharp.StockDataOrg;

/// <summary>Message adapter for the StockData.org REST API.</summary>
[MediaIcon(Media.MediaNames.stockdata_org)]
[Doc("topics/api/connectors/stock_market/stockdata_org.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.StockDataOrgKey,
    Description = LocalizedStrings.MarketDataConnectorKey,
    GroupName = LocalizedStrings.MarketDataKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.US |
    MessageAdapterCategories.Free |
    MessageAdapterCategories.Paid |
    MessageAdapterCategories.History |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.Candles |
    MessageAdapterCategories.Level1 |
    MessageAdapterCategories.News)]
public partial class StockDataOrgMessageAdapter :
    MessageAdapter,
    ITokenAdapter,
    IAddressAdapter<Uri>
{
    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TokenKey,
        Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Token { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = LocalizedStrings.OfficialStockDataOrgApiRootDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public Uri Address { get; set; } =
        new("https://api.stockdata.org/v1/");

    /// <summary>Whether pre-market and after-hours data is included.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ExtendedHoursKey,
        Description = LocalizedStrings.IncludePreMarketAndAfterHoursQuotesAndIntradayBarsDescKey,
        GroupName = LocalizedStrings.MarketDataLabelKey,
        Order = 2)]
    public bool ExtendedHours { get; set; }

    /// <summary>Whether the paid split-adjusted intraday endpoint is used.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AdjustedIntradayKey,
        Description = LocalizedStrings.UseTheSplitAdjustedIntradayEndpointAvailableOnStandardAndHigherPlansDescKey,
        GroupName = LocalizedStrings.MarketDataLabelKey,
        Order = 3)]
    public bool AdjustedIntraday { get; set; }

    /// <summary>Language filter for news requests.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.NewsLanguageKey,
        Description = LocalizedStrings.CommaSeparatedStockDataOrgLanguageCodesUsedToFilterNewsDescKey,
        GroupName = LocalizedStrings.NewsKey,
        Order = 4)]
    public string NewsLanguage { get; set; } = "en";

    /// <summary>Number of articles requested per news page.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.NewsPageSizeKey,
        Description = LocalizedStrings.NumberOfNewsArticlesRequestedPerPageSubjectToTheSubscriptionPlanDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 5)]
    public int NewsPageSize { get; set; } = 10;

    /// <summary>Maximum API pages or history chunks per request.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.MaximumRequestsKey,
        Description = LocalizedStrings.MaximumStockDataOrgPagesOrHistoryChunksDownloadedForOneSubscriptionDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 6)]
    public int MaxRequests { get; set; } = 100;

    /// <summary>Time zone used for quote timestamps without an offset.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.QuoteTimeZoneKey,
        Description = LocalizedStrings.TimeZoneUsedForIexQuoteTimestampsThatDoNotContainAnOffsetDescKey,
        GroupName = LocalizedStrings.MarketDataLabelKey,
        Order = 7)]
    public string QuoteTimeZoneId { get; set; } =
        "Eastern Standard Time";

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Token), Token)
            .Set(nameof(Address), Address)
            .Set(nameof(ExtendedHours), ExtendedHours)
            .Set(nameof(AdjustedIntraday), AdjustedIntraday)
            .Set(nameof(NewsLanguage), NewsLanguage)
            .Set(nameof(NewsPageSize), NewsPageSize)
            .Set(nameof(MaxRequests), MaxRequests)
            .Set(nameof(QuoteTimeZoneId), QuoteTimeZoneId);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Token = storage.GetValue<SecureString>(nameof(Token));
        Address = storage.GetValue(nameof(Address), Address);
        ExtendedHours = storage.GetValue(
            nameof(ExtendedHours), ExtendedHours);
        AdjustedIntraday = storage.GetValue(
            nameof(AdjustedIntraday), AdjustedIntraday);
        NewsLanguage = storage.GetValue(
            nameof(NewsLanguage), NewsLanguage);
        NewsPageSize = storage.GetValue(
            nameof(NewsPageSize), NewsPageSize);
        MaxRequests = storage.GetValue(
            nameof(MaxRequests), MaxRequests);
        QuoteTimeZoneId = storage.GetValue(
            nameof(QuoteTimeZoneId), QuoteTimeZoneId);
    }
}
