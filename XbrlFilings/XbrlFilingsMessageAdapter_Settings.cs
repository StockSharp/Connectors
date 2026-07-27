namespace StockSharp.XbrlFilings;

/// <summary>
/// Message adapter for the public filings.xbrl.org JSON:API.
/// </summary>
[MediaIcon(Media.MediaNames.xbrl_filings)]
[Doc("topics/api/connectors/stock_market/xbrl_filings.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.XbrlFilingsKey,
    Description = LocalizedStrings.MarketDataConnectorKey,
    GroupName = LocalizedStrings.EuropeanKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.Free |
    MessageAdapterCategories.Europe |
    MessageAdapterCategories.History |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.News)]
public partial class XbrlFilingsMessageAdapter :
    MessageAdapter,
    IAddressAdapter<Uri>
{
    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = "Official filings.xbrl.org JSON:API root.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 0)]
    [BasicSetting]
    public Uri Address { get; set; } =
        new("https://filings.xbrl.org/api/");

    /// <summary>Public filing-content address.</summary>
    [Display(
        Name = "Public address",
        Description = "Official public root used for filing viewer, report, package, and xBRL-JSON links.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 1)]
    public Uri PublicAddress { get; set; } =
        new("https://filings.xbrl.org/");

    /// <summary>Optional ISO 3166-1 alpha-2 filing-country filter.</summary>
    [Display(
        Name = "Country",
        Description = "Optional two-letter filing country, for example GB, FR, DE, or UA.",
        GroupName = "Filters",
        Order = 2)]
    public string Country { get; set; }

    /// <summary>Number of resources requested per API page.</summary>
    [Display(
        Name = "Page size",
        Description = "Number of JSON:API resources requested per page.",
        GroupName = "Limits",
        Order = 3)]
    public int PageSize { get; set; } = 100;

    /// <summary>Maximum pages downloaded by one subscription.</summary>
    [Display(
        Name = "Maximum pages",
        Description = "Maximum JSON:API pages downloaded by one lookup or news subscription.",
        GroupName = "Limits",
        Order = 4)]
    public int MaxPages { get; set; } = 20;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Address), Address)
            .Set(nameof(PublicAddress), PublicAddress)
            .Set(nameof(Country), Country)
            .Set(nameof(PageSize), PageSize)
            .Set(nameof(MaxPages), MaxPages);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Address = storage.GetValue(nameof(Address), Address);
        PublicAddress = storage.GetValue(
            nameof(PublicAddress), PublicAddress);
        Country = storage.GetValue<string>(nameof(Country));
        PageSize = storage.GetValue(nameof(PageSize), PageSize);
        MaxPages = storage.GetValue(nameof(MaxPages), MaxPages);
    }
}
