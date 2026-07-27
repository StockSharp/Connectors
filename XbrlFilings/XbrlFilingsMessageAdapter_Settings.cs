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
        Description = LocalizedStrings.OfficialFilingsXbrlOrgJsonApiRootDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 0)]
    [BasicSetting]
    public Uri Address { get; set; } =
        new("https://filings.xbrl.org/api/");

    /// <summary>Public filing-content address.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PublicAddressKey,
        Description = LocalizedStrings.OfficialPublicRootUsedForFilingViewerReportPackageAndXbrlJsonLinksDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 1)]
    public Uri PublicAddress { get; set; } =
        new("https://filings.xbrl.org/");

    /// <summary>Optional ISO 3166-1 alpha-2 filing-country filter.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.CountryKey,
        Description = LocalizedStrings.OptionalTwoLetterFilingCountryForExampleGbFrDeOrUaDescKey,
        GroupName = LocalizedStrings.FiltersKey,
        Order = 2)]
    public string Country { get; set; }

    /// <summary>Number of resources requested per API page.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PageSizeKey,
        Description = LocalizedStrings.NumberOfJsonApiResourcesRequestedPerPageDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 3)]
    public int PageSize { get; set; } = 100;

    /// <summary>Maximum pages downloaded by one subscription.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.MaximumPagesKey,
        Description = LocalizedStrings.MaximumJsonApiPagesDownloadedByOneLookupOrNewsSubscriptionDescKey,
        GroupName = LocalizedStrings.LimitsKey,
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
