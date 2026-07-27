namespace StockSharp.GuruFocus;

/// <summary>Message adapter for the GuruFocus Data REST API.</summary>
[MediaIcon(Media.MediaNames.gurufocus)]
[Doc("topics/api/connectors/stock_market/gurufocus.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.GuruFocusKey,
    Description = LocalizedStrings.MarketDataConnectorKey,
    GroupName = LocalizedStrings.MarketDataKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.US |
    MessageAdapterCategories.Europe |
    MessageAdapterCategories.Asia |
    MessageAdapterCategories.Paid |
    MessageAdapterCategories.History |
    MessageAdapterCategories.RealTime |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.Candles |
    MessageAdapterCategories.Level1 |
    MessageAdapterCategories.News)]
public partial class GuruFocusMessageAdapter :
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
        Description = LocalizedStrings.OfficialGuruFocusDataApiProductionRootDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public Uri Address { get; set; } =
        new("https://api.gurufocus.com/data/");

    /// <summary>Region used for stock-list lookup.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.RegionCodeKey,
        Description = LocalizedStrings.StockListRegionUAEBCOFSOrIDescKey,
        GroupName = LocalizedStrings.FiltersKey,
        Order = 2)]
    public string RegionCode { get; set; } = "U";

    /// <summary>Records requested per paginated API call.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PageSizeKey,
        Description = LocalizedStrings.RecordsPerStockEtfNewsOrInsiderPageDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 3)]
    public int PageSize { get; set; } = 100;

    /// <summary>Maximum pages visited during a security lookup.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.LookupPageLimitKey,
        Description = LocalizedStrings.SafetyLimitForPaginatedStockAndEtfLookupsDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 4)]
    public int MaxLookupPages { get; set; } = 1000;

    /// <summary>Maximum records requested for a custom dataset.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DatasetLimitKey,
        Description = LocalizedStrings.MaximumRecordsRequestedForPaginatedCustomDatasetsDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 5)]
    public int DatasetLimit { get; set; } = 100;

    /// <summary>Maximum news articles requested per subscription.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.NewsLimitKey,
        Description = LocalizedStrings.MaximumStockNewsOrMarketHeadlineRecordsDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 6)]
    public int NewsLimit { get; set; } = 200;

    /// <summary>Optional SEC filing form filter.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SecFormTypeKey,
        Description = LocalizedStrings.OptionalSecFormTypeSuchAs10KOr10QDescKey,
        GroupName = LocalizedStrings.FiltersKey,
        Order = 7)]
    public string FilingFormType { get; set; }

    /// <summary>Optional GuruFocus guru-trade action filter.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.GuruTradeActionsKey,
        Description = LocalizedStrings.CommaSeparatedBuySellAddAndReduceActionsDescKey,
        GroupName = LocalizedStrings.FiltersKey,
        Order = 8)]
    public string GuruTradeActions { get; set; }

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Token), Token)
            .Set(nameof(Address), Address)
            .Set(nameof(RegionCode), RegionCode)
            .Set(nameof(PageSize), PageSize)
            .Set(nameof(MaxLookupPages), MaxLookupPages)
            .Set(nameof(DatasetLimit), DatasetLimit)
            .Set(nameof(NewsLimit), NewsLimit)
            .Set(nameof(FilingFormType), FilingFormType)
            .Set(nameof(GuruTradeActions), GuruTradeActions);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Token = storage.GetValue<SecureString>(nameof(Token));
        Address = storage.GetValue(nameof(Address), Address);
        RegionCode = storage.GetValue(
            nameof(RegionCode), RegionCode);
        PageSize = storage.GetValue(nameof(PageSize), PageSize);
        MaxLookupPages = storage.GetValue(
            nameof(MaxLookupPages), MaxLookupPages);
        DatasetLimit = storage.GetValue(
            nameof(DatasetLimit), DatasetLimit);
        NewsLimit = storage.GetValue(
            nameof(NewsLimit), NewsLimit);
        FilingFormType = storage.GetValue<string>(
            nameof(FilingFormType));
        GuruTradeActions = storage.GetValue<string>(
            nameof(GuruTradeActions));
    }
}
