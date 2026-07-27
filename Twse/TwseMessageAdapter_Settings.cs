namespace StockSharp.Twse;

/// <summary>
/// Message adapter for the Taiwan Stock Exchange public OpenAPI.
/// </summary>
[MediaIcon(Media.MediaNames.twse)]
[Doc("topics/api/connectors/stock_market/twse_openapi.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.TwseOpenApiKey,
    Description = LocalizedStrings.MarketDataConnectorKey,
    GroupName = LocalizedStrings.TaiwanStockExchangeKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.Free |
    MessageAdapterCategories.Asia |
    MessageAdapterCategories.History |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.Level1 |
    MessageAdapterCategories.Candles)]
public partial class TwseMessageAdapter :
    MessageAdapter,
    IAddressAdapter<Uri>
{
    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = "Taiwan Stock Exchange OpenAPI v1 root.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 0)]
    [BasicSetting]
    public Uri Address { get; set; } =
        new("https://openapi.twse.com.tw/v1/");

    /// <summary>Load company and listed-fund profiles.</summary>
    [Display(
        Name = "Include profiles",
        Description = "Load listed-company and fund profiles for names, types, listing dates, and issue sizes.",
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 1)]
    public bool IncludeProfiles { get; set; } = true;

    /// <summary>Load daily valuation ratios.</summary>
    [Display(
        Name = "Include valuations",
        Description = "Load daily price-to-earnings, dividend-yield, and price-to-book ratios.",
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 2)]
    public bool IncludeValuations { get; set; } = true;

    /// <summary>Duration for which one public snapshot is reused.</summary>
    [Display(
        Name = "Cache timeout",
        Description = "Duration for which a downloaded TWSE daily snapshot is reused. Zero disables caching.",
        GroupName = "Limits",
        Order = 3)]
    public TimeSpan CacheTimeout { get; set; } =
        TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Address), Address)
            .Set(nameof(IncludeProfiles), IncludeProfiles)
            .Set(nameof(IncludeValuations), IncludeValuations)
            .Set(nameof(CacheTimeout), CacheTimeout);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Address = storage.GetValue(nameof(Address), Address);
        IncludeProfiles = storage.GetValue(
            nameof(IncludeProfiles), IncludeProfiles);
        IncludeValuations = storage.GetValue(
            nameof(IncludeValuations), IncludeValuations);
        CacheTimeout = storage.GetValue(
            nameof(CacheTimeout), CacheTimeout);
    }
}
