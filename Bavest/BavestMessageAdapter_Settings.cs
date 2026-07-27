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
        Description = LocalizedStrings.OfficialBavestProductionApiRootDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public Uri Address { get; set; } =
        new("https://api.bavest.co/");

    /// <summary>Optional target currency.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.CurrencyKey,
        Description = LocalizedStrings.OptionalTargetCurrencyUsedForQuoteCandleMetricAndScreenerRequestsDescKey,
        GroupName = LocalizedStrings.FiltersKey,
        Order = 2)]
    public string Currency { get; set; }

    /// <summary>Optional exchange for market-data requests.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ExchangeKey,
        Description = LocalizedStrings.OptionalExchangeIdentifierForQuoteAndCandleRequestsDescKey,
        GroupName = LocalizedStrings.FiltersKey,
        Order = 3)]
    public string Exchange { get; set; }

    /// <summary>Optional exchange code for security lists.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ExchangeCodeKey,
        Description = LocalizedStrings.OptionalExchangeShortCodeUsedForStockAndEtfListsDescKey,
        GroupName = LocalizedStrings.FiltersKey,
        Order = 4)]
    public string ExchangeCode { get; set; }

    /// <summary>Financial statement frequency.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.StatementFrequencyKey,
        Description = LocalizedStrings.FrequencyUsedForFinancialStatementDatasetsDescKey,
        GroupName = LocalizedStrings.FinancialsKey,
        Order = 5)]
    public BavestFinancialFrequencies FinancialFrequency { get; set; } =
        BavestFinancialFrequencies.Annual;

    /// <summary>Whether ETF metric lineage is requested.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.EtfMetricTraceKey,
        Description = LocalizedStrings.IncludeFormulaInputsAndLineageWithEtfMetricsDescKey,
        GroupName = LocalizedStrings.FinancialsKey,
        Order = 6)]
    public bool TraceEtfMetrics { get; set; }

    /// <summary>JSON array of Bavest screener conditions.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ScreenerQueryKey,
        Description = LocalizedStrings.JsonArrayOfBavestV2ScreenerFilterConditionsDescKey,
        GroupName = LocalizedStrings.ScreenerKey,
        Order = 7)]
    public string ScreenerQuery { get; set; } = "[]";

    /// <summary>Rows requested per listing request.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PageSizeKey,
        Description = LocalizedStrings.RowsRequestedPerStockEtfOrSearchPageDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 8)]
    public int PageSize { get; set; } = 1000;

    /// <summary>Maximum pages requested for a security lookup.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PageLimitKey,
        Description = LocalizedStrings.SafetyLimitForPaginatedSecurityLookupsDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 9)]
    public int MaxPages { get; set; } = 100;

    /// <summary>Maximum news articles returned per subscription.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.NewsLimitKey,
        Description = LocalizedStrings.MaximumNewsArticlesReturnedPerSubscriptionDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 10)]
    public int NewsLimit { get; set; } = 100;

    /// <summary>Maximum rows requested for a custom dataset.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DatasetLimitKey,
        Description = LocalizedStrings.MaximumRowsRequestedFromAPaginatedDatasetDescKey,
        GroupName = LocalizedStrings.LimitsKey,
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
