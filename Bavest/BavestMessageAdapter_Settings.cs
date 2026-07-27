namespace StockSharp.Bavest;

/// <summary>Message adapter for the Bavest REST API.</summary>
[MediaIcon(Media.MediaNames.bavest)]
[Doc("topics/api/connectors/stock_market/bavest.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.BavestKey,
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
public partial class BavestMessageAdapter :
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
        Description = "Official Bavest production API root.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public Uri Address { get; set; } =
        new("https://api.bavest.co/");

    /// <summary>Optional target currency.</summary>
    [Display(
        Name = "Currency",
        Description = "Optional target currency used for quote, candle, metric, and screener requests.",
        GroupName = "Filters",
        Order = 2)]
    public string Currency { get; set; }

    /// <summary>Optional exchange for market-data requests.</summary>
    [Display(
        Name = "Exchange",
        Description = "Optional exchange identifier for quote and candle requests.",
        GroupName = "Filters",
        Order = 3)]
    public string Exchange { get; set; }

    /// <summary>Optional exchange code for security lists.</summary>
    [Display(
        Name = "Exchange code",
        Description = "Optional exchange short code used for stock and ETF lists.",
        GroupName = "Filters",
        Order = 4)]
    public string ExchangeCode { get; set; }

    /// <summary>Financial statement frequency.</summary>
    [Display(
        Name = "Statement frequency",
        Description = "Frequency used for financial statement datasets.",
        GroupName = "Financials",
        Order = 5)]
    public BavestFinancialFrequencies FinancialFrequency { get; set; } =
        BavestFinancialFrequencies.Annual;

    /// <summary>Whether ETF metric lineage is requested.</summary>
    [Display(
        Name = "ETF metric trace",
        Description = "Include formula, inputs, and lineage with ETF metrics.",
        GroupName = "Financials",
        Order = 6)]
    public bool TraceEtfMetrics { get; set; }

    /// <summary>JSON array of Bavest screener conditions.</summary>
    [Display(
        Name = "Screener query",
        Description = "JSON array of Bavest v2 screener filter conditions.",
        GroupName = "Screener",
        Order = 7)]
    public string ScreenerQuery { get; set; } = "[]";

    /// <summary>Rows requested per listing request.</summary>
    [Display(
        Name = "Page size",
        Description = "Rows requested per stock, ETF, or search page.",
        GroupName = "Limits",
        Order = 8)]
    public int PageSize { get; set; } = 1000;

    /// <summary>Maximum pages requested for a security lookup.</summary>
    [Display(
        Name = "Page limit",
        Description = "Safety limit for paginated security lookups.",
        GroupName = "Limits",
        Order = 9)]
    public int MaxPages { get; set; } = 100;

    /// <summary>Maximum news articles returned per subscription.</summary>
    [Display(
        Name = "News limit",
        Description = "Maximum news articles returned per subscription.",
        GroupName = "Limits",
        Order = 10)]
    public int NewsLimit { get; set; } = 100;

    /// <summary>Maximum rows requested for a custom dataset.</summary>
    [Display(
        Name = "Dataset limit",
        Description = "Maximum rows requested from a paginated dataset.",
        GroupName = "Limits",
        Order = 11)]
    public int DatasetLimit { get; set; } = 1000;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Token), Token)
            .Set(nameof(Address), Address)
            .Set(nameof(Currency), Currency)
            .Set(nameof(Exchange), Exchange)
            .Set(nameof(ExchangeCode), ExchangeCode)
            .Set(
                nameof(FinancialFrequency),
                FinancialFrequency)
            .Set(nameof(TraceEtfMetrics), TraceEtfMetrics)
            .Set(nameof(ScreenerQuery), ScreenerQuery)
            .Set(nameof(PageSize), PageSize)
            .Set(nameof(MaxPages), MaxPages)
            .Set(nameof(NewsLimit), NewsLimit)
            .Set(nameof(DatasetLimit), DatasetLimit);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Token = storage.GetValue<SecureString>(nameof(Token));
        Address = storage.GetValue(nameof(Address), Address);
        Currency = storage.GetValue<string>(nameof(Currency));
        Exchange = storage.GetValue<string>(nameof(Exchange));
        ExchangeCode = storage.GetValue<string>(
            nameof(ExchangeCode));
        FinancialFrequency = storage.GetValue(
            nameof(FinancialFrequency), FinancialFrequency);
        TraceEtfMetrics = storage.GetValue(
            nameof(TraceEtfMetrics), TraceEtfMetrics);
        ScreenerQuery = storage.GetValue(
            nameof(ScreenerQuery), ScreenerQuery);
        PageSize = storage.GetValue(
            nameof(PageSize), PageSize);
        MaxPages = storage.GetValue(
            nameof(MaxPages), MaxPages);
        NewsLimit = storage.GetValue(
            nameof(NewsLimit), NewsLimit);
        DatasetLimit = storage.GetValue(
            nameof(DatasetLimit), DatasetLimit);
    }
}
