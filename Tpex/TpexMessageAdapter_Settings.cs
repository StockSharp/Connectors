namespace StockSharp.Tpex;

/// <summary>
/// Message adapter for Taipei Exchange public market data.
/// </summary>
[MediaIcon(Media.MediaNames.tpex)]
[Doc("topics/api/connectors/stock_market/tpex.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.TpexKey,
    Description = LocalizedStrings.MarketDataConnectorKey,
    GroupName = LocalizedStrings.TaiwanStockExchangeKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.Free |
    MessageAdapterCategories.Asia |
    MessageAdapterCategories.History |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.Level1 |
    MessageAdapterCategories.Candles)]
public partial class TpexMessageAdapter :
    MessageAdapter,
    IAddressAdapter<Uri>
{
    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = "Taipei Exchange website and OpenAPI root.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 0)]
    [BasicSetting]
    public Uri Address { get; set; } =
        new("https://www.tpex.org.tw/");

    /// <summary>TPEx equity market selection.</summary>
    [Display(
        Name = "Market",
        Description = "Mainboard, Emerging Stock Board, or both equity markets.",
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 1)]
    [BasicSetting]
    public TpexMarkets Market { get; set; } =
        TpexMarkets.Mainboard;

    /// <summary>Include warrants and other listed derivatives.</summary>
    [Display(
        Name = "Include listed derivatives",
        Description = "Include Mainboard warrants and other non-equity listings. Funds and ETFs remain included when disabled.",
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 2)]
    public bool IncludeListedDerivatives { get; set; }

    /// <summary>Load current Mainboard valuation ratios.</summary>
    [Display(
        Name = "Include valuations",
        Description = "Load current Mainboard price-to-earnings, dividend-yield, and price-to-book ratios.",
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 3)]
    public bool IncludeValuations { get; set; } = true;

    /// <summary>Duration for which the current snapshot is reused.</summary>
    [Display(
        Name = "Cache timeout",
        Description = "Duration for which downloaded TPEx current data is reused. Zero disables caching.",
        GroupName = "Limits",
        Order = 4)]
    public TimeSpan CacheTimeout { get; set; } =
        TimeSpan.FromMinutes(5);

    /// <summary>Maximum calendar months in one history request.</summary>
    [Display(
        Name = "Maximum history months",
        Description = "Maximum number of monthly public-history requests issued for one subscription.",
        GroupName = "Limits",
        Order = 5)]
    public int MaxHistoryMonths { get; set; } = 120;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Address), Address)
            .Set(nameof(Market), Market)
            .Set(
                nameof(IncludeListedDerivatives),
                IncludeListedDerivatives)
            .Set(nameof(IncludeValuations), IncludeValuations)
            .Set(nameof(CacheTimeout), CacheTimeout)
            .Set(nameof(MaxHistoryMonths), MaxHistoryMonths);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Address = storage.GetValue(nameof(Address), Address);
        Market = storage.GetValue(nameof(Market), Market);
        IncludeListedDerivatives = storage.GetValue(
            nameof(IncludeListedDerivatives),
            IncludeListedDerivatives);
        IncludeValuations = storage.GetValue(
            nameof(IncludeValuations), IncludeValuations);
        CacheTimeout = storage.GetValue(
            nameof(CacheTimeout), CacheTimeout);
        MaxHistoryMonths = storage.GetValue(
            nameof(MaxHistoryMonths), MaxHistoryMonths);
    }
}
