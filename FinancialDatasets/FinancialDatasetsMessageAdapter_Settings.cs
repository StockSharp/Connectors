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
        Description = "Official Financial Datasets production API root.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public Uri Address { get; set; } =
        new("https://api.financialdatasets.ai/");

    /// <summary>Whether inactive tickers are excluded from bulk lookup.</summary>
    [Display(
        Name = "Active tickers only",
        Description = "Use the actively traded price-snapshot ticker universe for bulk lookup.",
        GroupName = "Filters",
        Order = 2)]
    public bool ActiveOnly { get; set; } = true;

    /// <summary>Default reporting period for financial datasets.</summary>
    [Display(
        Name = "Financial period",
        Description = "Reporting period used for statements and financial metrics.",
        GroupName = "Financial data",
        Order = 3)]
    public FinancialDatasetsPeriods FinancialPeriod { get; set; } =
        FinancialDatasetsPeriods.Annual;

    /// <summary>Maximum records requested for a custom dataset.</summary>
    [Display(
        Name = "Dataset limit",
        Description = "Maximum records requested from a custom financial, filing, or ownership dataset.",
        GroupName = "Limits",
        Order = 4)]
    public int DataLimit { get; set; } = 100;

    /// <summary>Maximum news articles per request.</summary>
    [Display(
        Name = "News limit",
        Description = "Maximum news articles requested; the API currently permits up to ten.",
        GroupName = "Limits",
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
