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
        Description = "Official Marketaux production API root.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public Uri Address { get; set; } =
        new("https://api.marketaux.com/");

    /// <summary>Comma-separated news language codes.</summary>
    [Display(
        Name = "Languages",
        Description = "Comma-separated language codes used for news and analytics.",
        GroupName = "Filters",
        Order = 2)]
    public string Languages { get; set; } = "en";

    /// <summary>Comma-separated entity types.</summary>
    [Display(
        Name = "Entity types",
        Description = "Comma-separated Marketaux entity types included in requests.",
        GroupName = "Filters",
        Order = 3)]
    public string EntityTypes { get; set; } =
        "equity,etf,mutualfund";

    /// <summary>Optional comma-separated country codes.</summary>
    [Display(
        Name = "Countries",
        Description = "Optional comma-separated ISO country codes.",
        GroupName = "Filters",
        Order = 4)]
    public string Countries { get; set; }

    /// <summary>Whether articles must contain identified entities.</summary>
    [Display(
        Name = "Require entities",
        Description = "Return only articles with identified market entities.",
        GroupName = "Filters",
        Order = 5)]
    public bool MustHaveEntities { get; set; } = true;

    /// <summary>Whether similar articles are grouped.</summary>
    [Display(
        Name = "Group similar",
        Description = "Group similar news articles into one result.",
        GroupName = "Filters",
        Order = 6)]
    public bool GroupSimilar { get; set; } = true;

    /// <summary>Rows requested per news page.</summary>
    [Display(
        Name = "News page size",
        Description = "Articles requested per page, subject to the account plan.",
        GroupName = "Limits",
        Order = 7)]
    public int NewsPageSize { get; set; } = 50;

    /// <summary>Maximum API pages requested per subscription.</summary>
    [Display(
        Name = "Page limit",
        Description = "Safety limit for paginated entity and news requests.",
        GroupName = "Limits",
        Order = 8)]
    public int MaxPages { get; set; } = 20;

    /// <summary>Maximum rows requested for an analytics dataset.</summary>
    [Display(
        Name = "Dataset limit",
        Description = "Maximum rows requested from an analytics endpoint.",
        GroupName = "Limits",
        Order = 9)]
    public int DatasetLimit { get; set; } = 100;

    /// <summary>Sentiment time-series interval.</summary>
    [Display(
        Name = "Sentiment interval",
        Description = "Aggregation interval for sentiment time-series requests.",
        GroupName = "Analytics",
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
