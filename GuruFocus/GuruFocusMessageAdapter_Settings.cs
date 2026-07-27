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
        Description = "Official GuruFocus Data API production root.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public Uri Address { get; set; } =
        new("https://api.gurufocus.com/data/");

    /// <summary>Region used for stock-list lookup.</summary>
    [Display(
        Name = "Region code",
        Description = "Stock-list region: U, A, E, B, C, O, F, S, or I.",
        GroupName = "Filters",
        Order = 2)]
    public string RegionCode { get; set; } = "U";

    /// <summary>Records requested per paginated API call.</summary>
    [Display(
        Name = "Page size",
        Description = "Records per stock, ETF, news, or insider page.",
        GroupName = "Limits",
        Order = 3)]
    public int PageSize { get; set; } = 100;

    /// <summary>Maximum pages visited during a security lookup.</summary>
    [Display(
        Name = "Lookup page limit",
        Description = "Safety limit for paginated stock and ETF lookups.",
        GroupName = "Limits",
        Order = 4)]
    public int MaxLookupPages { get; set; } = 1000;

    /// <summary>Maximum records requested for a custom dataset.</summary>
    [Display(
        Name = "Dataset limit",
        Description = "Maximum records requested for paginated custom datasets.",
        GroupName = "Limits",
        Order = 5)]
    public int DatasetLimit { get; set; } = 100;

    /// <summary>Maximum news articles requested per subscription.</summary>
    [Display(
        Name = "News limit",
        Description = "Maximum stock-news or market-headline records.",
        GroupName = "Limits",
        Order = 6)]
    public int NewsLimit { get; set; } = 200;

    /// <summary>Optional SEC filing form filter.</summary>
    [Display(
        Name = "SEC form type",
        Description = "Optional SEC form type such as 10-K or 10-Q.",
        GroupName = "Filters",
        Order = 7)]
    public string FilingFormType { get; set; }

    /// <summary>Optional GuruFocus guru-trade action filter.</summary>
    [Display(
        Name = "Guru trade actions",
        Description = "Comma-separated buy, sell, add, and reduce actions.",
        GroupName = "Filters",
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
