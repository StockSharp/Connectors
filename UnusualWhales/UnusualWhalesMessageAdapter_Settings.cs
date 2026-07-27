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
        Description = "Official Unusual Whales production API root.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public Uri Address { get; set; } =
        new("https://api.unusualwhales.com/");

    /// <summary>Maximum candles requested per subscription.</summary>
    [Display(
        Name = "Candle limit",
        Description = "Maximum candles requested from the OHLC endpoint.",
        GroupName = "Limits",
        Order = 2)]
    public int CandleLimit { get; set; } = 2500;

    /// <summary>Maximum news headlines returned per subscription.</summary>
    [Display(
        Name = "News limit",
        Description = "Maximum news headlines returned per subscription.",
        GroupName = "Limits",
        Order = 3)]
    public int NewsLimit { get; set; } = 500;

    /// <summary>Maximum pages requested for news history.</summary>
    [Display(
        Name = "Page limit",
        Description = "Safety limit for paginated news requests.",
        GroupName = "Limits",
        Order = 4)]
    public int MaxPages { get; set; } = 10;

    /// <summary>Maximum rows requested for a custom dataset.</summary>
    [Display(
        Name = "Dataset limit",
        Description = "Maximum rows requested for a custom REST dataset.",
        GroupName = "Limits",
        Order = 5)]
    public int DatasetLimit { get; set; } = 500;

    /// <summary>Whether the official unusual-flow preset is applied.</summary>
    [Display(
        Name = "Unusual flow only",
        Description = "Apply the official unusual options-flow preset.",
        GroupName = "Filters",
        Order = 6)]
    public bool UnusualFlowOnly { get; set; } = true;

    /// <summary>Whether market tide includes only OTM options.</summary>
    [Display(
        Name = "OTM market tide",
        Description = "Request only out-of-the-money market-tide activity.",
        GroupName = "Filters",
        Order = 7)]
    public bool OtmMarketTide { get; set; }

    /// <summary>Whether market tide is aggregated into five-minute rows.</summary>
    [Display(
        Name = "Five-minute market tide",
        Description = "Request five-minute instead of one-minute market-tide rows.",
        GroupName = "Filters",
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
