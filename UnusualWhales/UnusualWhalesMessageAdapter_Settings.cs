namespace StockSharp.UnusualWhales;

/// <summary>
/// Message adapter for the Unusual Whales REST API.
/// </summary>
[MediaIcon(Media.MediaNames.unusual_whales)]
[Doc("topics/api/connectors/stock_market/unusual_whales.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.UnusualWhalesKey,
    Description = LocalizedStrings.MarketDataConnectorKey,
    GroupName = LocalizedStrings.MarketDataKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.US |
    MessageAdapterCategories.Paid |
    MessageAdapterCategories.History |
    MessageAdapterCategories.RealTime |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.Options |
    MessageAdapterCategories.Candles |
    MessageAdapterCategories.Level1 |
    MessageAdapterCategories.News)]
public partial class UnusualWhalesMessageAdapter :
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
        Description = LocalizedStrings.OfficialUnusualWhalesProductionApiRootDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public Uri Address { get; set; } =
        new("https://api.unusualwhales.com/");

    /// <summary>Maximum candles requested per subscription.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.CandleLimitKey,
        Description = LocalizedStrings.MaximumCandlesRequestedFromTheOhlcEndpointDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 2)]
    public int CandleLimit { get; set; } = 2500;

    /// <summary>Maximum news headlines returned per subscription.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.NewsLimitKey,
        Description = LocalizedStrings.MaximumNewsHeadlinesReturnedPerSubscriptionDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 3)]
    public int NewsLimit { get; set; } = 500;

    /// <summary>Maximum pages requested for news history.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PageLimitKey,
        Description = LocalizedStrings.SafetyLimitForPaginatedNewsRequestsDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 4)]
    public int MaxPages { get; set; } = 10;

    /// <summary>Maximum rows requested for a custom dataset.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DatasetLimitKey,
        Description = LocalizedStrings.MaximumRowsRequestedForACustomRestDatasetDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 5)]
    public int DatasetLimit { get; set; } = 500;

    /// <summary>Whether the official unusual-flow preset is applied.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.UnusualFlowOnlyKey,
        Description = LocalizedStrings.ApplyTheOfficialUnusualOptionsFlowPresetDescKey,
        GroupName = LocalizedStrings.FiltersKey,
        Order = 6)]
    public bool UnusualFlowOnly { get; set; } = true;

    /// <summary>Whether market tide includes only OTM options.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.OtmMarketTideKey,
        Description = LocalizedStrings.RequestOnlyOutOfTheMoneyMarketTideActivityDescKey,
        GroupName = LocalizedStrings.FiltersKey,
        Order = 7)]
    public bool OtmMarketTide { get; set; }

    /// <summary>Whether market tide is aggregated into five-minute rows.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.FiveMinuteMarketTideKey,
        Description = LocalizedStrings.RequestFiveMinuteInsteadOfOneMinuteMarketTideRowsDescKey,
        GroupName = LocalizedStrings.FiltersKey,
        Order = 8)]
    public bool FiveMinuteMarketTide { get; set; }

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Token), Token)
            .Set(nameof(Address), Address)
            .Set(nameof(CandleLimit), CandleLimit)
            .Set(nameof(NewsLimit), NewsLimit)
            .Set(nameof(MaxPages), MaxPages)
            .Set(nameof(DatasetLimit), DatasetLimit)
            .Set(nameof(UnusualFlowOnly), UnusualFlowOnly)
            .Set(nameof(OtmMarketTide), OtmMarketTide)
            .Set(
                nameof(FiveMinuteMarketTide),
                FiveMinuteMarketTide);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Token = storage.GetValue<SecureString>(nameof(Token));
        Address = storage.GetValue(nameof(Address), Address);
        CandleLimit = storage.GetValue(
            nameof(CandleLimit), CandleLimit);
        NewsLimit = storage.GetValue(
            nameof(NewsLimit), NewsLimit);
        MaxPages = storage.GetValue(
            nameof(MaxPages), MaxPages);
        DatasetLimit = storage.GetValue(
            nameof(DatasetLimit), DatasetLimit);
        UnusualFlowOnly = storage.GetValue(
            nameof(UnusualFlowOnly), UnusualFlowOnly);
        OtmMarketTide = storage.GetValue(
            nameof(OtmMarketTide), OtmMarketTide);
        FiveMinuteMarketTide = storage.GetValue(
            nameof(FiveMinuteMarketTide),
            FiveMinuteMarketTide);
    }
}
