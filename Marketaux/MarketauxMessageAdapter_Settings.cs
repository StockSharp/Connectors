namespace StockSharp.Marketaux;

/// <summary>Message adapter for the Marketaux REST API.</summary>
[MediaIcon(Media.MediaNames.marketaux)]
[Doc("topics/api/connectors/stock_market/marketaux.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.MarketauxKey,
    Description = LocalizedStrings.MarketDataConnectorKey,
    GroupName = LocalizedStrings.MarketDataKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.US |
    MessageAdapterCategories.Europe |
    MessageAdapterCategories.Asia |
    MessageAdapterCategories.Free |
    MessageAdapterCategories.Paid |
    MessageAdapterCategories.History |
    MessageAdapterCategories.RealTime |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.News)]
public partial class MarketauxMessageAdapter :
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
        Description = LocalizedStrings.OfficialMarketauxProductionApiRootDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public Uri Address { get; set; } =
        new("https://api.marketaux.com/");

    /// <summary>Comma-separated news language codes.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.LanguagesKey,
        Description = LocalizedStrings.CommaSeparatedLanguageCodesUsedForNewsAndAnalyticsDescKey,
        GroupName = LocalizedStrings.FiltersKey,
        Order = 2)]
    public string Languages { get; set; } = "en";

    /// <summary>Comma-separated entity types.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.EntityTypesKey,
        Description = LocalizedStrings.CommaSeparatedMarketauxEntityTypesIncludedInRequestsDescKey,
        GroupName = LocalizedStrings.FiltersKey,
        Order = 3)]
    public string EntityTypes { get; set; } =
        "equity,etf,mutualfund";

    /// <summary>Optional comma-separated country codes.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.CountriesKey,
        Description = LocalizedStrings.OptionalCommaSeparatedIsoCountryCodesDescKey,
        GroupName = LocalizedStrings.FiltersKey,
        Order = 4)]
    public string Countries { get; set; }

    /// <summary>Whether articles must contain identified entities.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.RequireEntitiesKey,
        Description = LocalizedStrings.ReturnOnlyArticlesWithIdentifiedMarketEntitiesDescKey,
        GroupName = LocalizedStrings.FiltersKey,
        Order = 5)]
    public bool MustHaveEntities { get; set; } = true;

    /// <summary>Whether similar articles are grouped.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.GroupSimilarKey,
        Description = LocalizedStrings.GroupSimilarNewsArticlesIntoOneResultDescKey,
        GroupName = LocalizedStrings.FiltersKey,
        Order = 6)]
    public bool GroupSimilar { get; set; } = true;

    /// <summary>Rows requested per news page.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.NewsPageSizeKey,
        Description = LocalizedStrings.ArticlesRequestedPerPageSubjectToTheAccountPlanDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 7)]
    public int NewsPageSize { get; set; } = 50;

    /// <summary>Maximum API pages requested per subscription.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PageLimitKey,
        Description = LocalizedStrings.SafetyLimitForPaginatedEntityAndNewsRequestsDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 8)]
    public int MaxPages { get; set; } = 20;

    /// <summary>Maximum rows requested for an analytics dataset.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DatasetLimitKey,
        Description = LocalizedStrings.MaximumRowsRequestedFromAnAnalyticsEndpointDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 9)]
    public int DatasetLimit { get; set; } = 100;

    /// <summary>Sentiment time-series interval.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SentimentIntervalKey,
        Description = LocalizedStrings.AggregationIntervalForSentimentTimeSeriesRequestsDescKey,
        GroupName = LocalizedStrings.AnalyticsKey,
        Order = 10)]
    public MarketauxIntervals SentimentInterval { get; set; } =
        MarketauxIntervals.Day;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Token), Token)
            .Set(nameof(Address), Address)
            .Set(nameof(Languages), Languages)
            .Set(nameof(EntityTypes), EntityTypes)
            .Set(nameof(Countries), Countries)
            .Set(nameof(MustHaveEntities), MustHaveEntities)
            .Set(nameof(GroupSimilar), GroupSimilar)
            .Set(nameof(NewsPageSize), NewsPageSize)
            .Set(nameof(MaxPages), MaxPages)
            .Set(nameof(DatasetLimit), DatasetLimit)
            .Set(
                nameof(SentimentInterval),
                SentimentInterval);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Token = storage.GetValue<SecureString>(nameof(Token));
        Address = storage.GetValue(nameof(Address), Address);
        Languages = storage.GetValue(
            nameof(Languages), Languages);
        EntityTypes = storage.GetValue(
            nameof(EntityTypes), EntityTypes);
        Countries = storage.GetValue<string>(nameof(Countries));
        MustHaveEntities = storage.GetValue(
            nameof(MustHaveEntities), MustHaveEntities);
        GroupSimilar = storage.GetValue(
            nameof(GroupSimilar), GroupSimilar);
        NewsPageSize = storage.GetValue(
            nameof(NewsPageSize), NewsPageSize);
        MaxPages = storage.GetValue(
            nameof(MaxPages), MaxPages);
        DatasetLimit = storage.GetValue(
            nameof(DatasetLimit), DatasetLimit);
        SentimentInterval = storage.GetValue(
            nameof(SentimentInterval), SentimentInterval);
    }
}
