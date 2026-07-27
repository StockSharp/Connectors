namespace StockSharp.QuiverQuant;

/// <summary>
/// Message adapter for the Quiver Quantitative REST API.
/// </summary>
[MediaIcon(Media.MediaNames.quiver_quant)]
[Doc("topics/api/connectors/stock_market/quiver_quant.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.QuiverQuantKey,
    Description = LocalizedStrings.MarketDataConnectorKey,
    GroupName = LocalizedStrings.MarketDataKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.US |
    MessageAdapterCategories.Paid |
    MessageAdapterCategories.History |
    MessageAdapterCategories.RealTime |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.News)]
public partial class QuiverQuantMessageAdapter :
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
        Description = LocalizedStrings.OfficialQuiverQuantitativeProductionApiRootDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public Uri Address { get; set; } =
        new("https://api.quiverquant.com/");

    /// <summary>Records requested per paginated API call.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PageSizeKey,
        Description = LocalizedStrings.RecordsRequestedPerPaginatedApiCallDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 2)]
    public int PageSize { get; set; } = 100;

    /// <summary>Maximum pages requested for news history.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PageLimitKey,
        Description = LocalizedStrings.SafetyLimitForPaginatedNewsRequestsDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 3)]
    public int MaxPages { get; set; } = 20;

    /// <summary>Maximum records requested for a custom dataset.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DatasetLimitKey,
        Description = LocalizedStrings.MaximumRecordsRequestedFromAPaginatedAlternativeDatasetDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 4)]
    public int DatasetLimit { get; set; } = 500;

    /// <summary>Maximum news articles requested per subscription.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.NewsLimitKey,
        Description = LocalizedStrings.MaximumQuiverNewsArticlesPerSubscriptionDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 5)]
    public int NewsLimit { get; set; } = 200;

    /// <summary>Whether insider results use Quiver's code filter.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.LimitInsiderCodesKey,
        Description = LocalizedStrings.RequestOnlyTheTransactionCodesSelectedByQuiverSInsiderFilterDescKey,
        GroupName = LocalizedStrings.FiltersKey,
        Order = 6)]
    public bool LimitInsiderCodes { get; set; }

    /// <summary>Whether only the most recent 13F changes are requested.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.MostRecent13fKey,
        Description = LocalizedStrings.RequestTheMostRecentInstitutionalHoldingChangesDescKey,
        GroupName = LocalizedStrings.FiltersKey,
        Order = 7)]
    public bool MostRecentInstitutional { get; set; } = true;

    /// <summary>Whether new funds are included in 13F changes.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.IncludeNewFundsKey,
        Description = LocalizedStrings.IncludeNewlyReportingFundsInInstitutionalHoldingChangesDescKey,
        GroupName = LocalizedStrings.FiltersKey,
        Order = 8)]
    public bool IncludeNewFunds { get; set; } = true;

    /// <summary>Optional election cycle for corporate donors.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DonorElectionCycleKey,
        Description = LocalizedStrings.OptionalFourDigitElectionCycleSuchAs2024DescKey,
        GroupName = LocalizedStrings.FiltersKey,
        Order = 9)]
    public string CorporateDonorCycle { get; set; }

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Token), Token)
            .Set(nameof(Address), Address)
            .Set(nameof(PageSize), PageSize)
            .Set(nameof(MaxPages), MaxPages)
            .Set(nameof(DatasetLimit), DatasetLimit)
            .Set(nameof(NewsLimit), NewsLimit)
            .Set(nameof(LimitInsiderCodes), LimitInsiderCodes)
            .Set(
                nameof(MostRecentInstitutional),
                MostRecentInstitutional)
            .Set(nameof(IncludeNewFunds), IncludeNewFunds)
            .Set(
                nameof(CorporateDonorCycle),
                CorporateDonorCycle);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Token = storage.GetValue<SecureString>(nameof(Token));
        Address = storage.GetValue(nameof(Address), Address);
        PageSize = storage.GetValue(nameof(PageSize), PageSize);
        MaxPages = storage.GetValue(nameof(MaxPages), MaxPages);
        DatasetLimit = storage.GetValue(
            nameof(DatasetLimit), DatasetLimit);
        NewsLimit = storage.GetValue(
            nameof(NewsLimit), NewsLimit);
        LimitInsiderCodes = storage.GetValue(
            nameof(LimitInsiderCodes), LimitInsiderCodes);
        MostRecentInstitutional = storage.GetValue(
            nameof(MostRecentInstitutional),
            MostRecentInstitutional);
        IncludeNewFunds = storage.GetValue(
            nameof(IncludeNewFunds), IncludeNewFunds);
        CorporateDonorCycle = storage.GetValue<string>(
            nameof(CorporateDonorCycle));
    }
}
