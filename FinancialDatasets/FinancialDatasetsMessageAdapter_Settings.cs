namespace StockSharp.FinancialDatasets;

/// <summary>Message adapter for the Financial Datasets REST API.</summary>
[MediaIcon(Media.MediaNames.financial_datasets)]
[Doc("topics/api/connectors/stock_market/financial_datasets.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.FinancialDatasetsKey,
    Description = LocalizedStrings.MarketDataConnectorKey,
    GroupName = LocalizedStrings.MarketDataKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.US |
    MessageAdapterCategories.Paid |
    MessageAdapterCategories.History |
    MessageAdapterCategories.RealTime |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.Candles |
    MessageAdapterCategories.Level1 |
    MessageAdapterCategories.News)]
public partial class FinancialDatasetsMessageAdapter :
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
        Description = LocalizedStrings.OfficialFinancialDatasetsProductionApiRootDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public Uri Address { get; set; } =
        new("https://api.financialdatasets.ai/");

    /// <summary>Whether inactive tickers are excluded from bulk lookup.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ActiveTickersOnlyKey,
        Description = LocalizedStrings.UseTheActivelyTradedPriceSnapshotTickerUniverseForBulkLookupDescKey,
        GroupName = LocalizedStrings.FiltersKey,
        Order = 2)]
    public bool ActiveOnly { get; set; } = true;

    /// <summary>Default reporting period for financial datasets.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.FinancialPeriodKey,
        Description = LocalizedStrings.ReportingPeriodUsedForStatementsAndFinancialMetricsDescKey,
        GroupName = LocalizedStrings.FinancialDataKey,
        Order = 3)]
    public FinancialDatasetsPeriods FinancialPeriod { get; set; } =
        FinancialDatasetsPeriods.Annual;

    /// <summary>Maximum records requested for a custom dataset.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DatasetLimitKey,
        Description = LocalizedStrings.MaximumRecordsRequestedFromACustomFinancialFilingOrOwnershipDatasetDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 4)]
    public int DataLimit { get; set; } = 100;

    /// <summary>Maximum news articles per request.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.NewsLimitKey,
        Description = LocalizedStrings.MaximumNewsArticlesRequestedTheApiCurrentlyPermitsUpToTenDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 5)]
    public int NewsLimit { get; set; } = 10;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Token), Token)
            .Set(nameof(Address), Address)
            .Set(nameof(ActiveOnly), ActiveOnly)
            .Set(nameof(FinancialPeriod), FinancialPeriod)
            .Set(nameof(DataLimit), DataLimit)
            .Set(nameof(NewsLimit), NewsLimit);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Token = storage.GetValue<SecureString>(nameof(Token));
        Address = storage.GetValue(nameof(Address), Address);
        ActiveOnly = storage.GetValue(
            nameof(ActiveOnly), ActiveOnly);
        FinancialPeriod = storage.GetValue(
            nameof(FinancialPeriod), FinancialPeriod);
        DataLimit = storage.GetValue(
            nameof(DataLimit), DataLimit);
        NewsLimit = storage.GetValue(
            nameof(NewsLimit), NewsLimit);
    }
}
