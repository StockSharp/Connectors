namespace StockSharp.OpenDart;

/// <summary>
/// Message adapter for the Korean Open DART public API.
/// </summary>
[MediaIcon(Media.MediaNames.open_dart)]
[Doc("topics/api/connectors/stock_market/open_dart.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.OpenDartKey,
    Description = LocalizedStrings.MarketDataConnectorKey,
    GroupName = LocalizedStrings.KoreaExchangeKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.Free |
    MessageAdapterCategories.Asia |
    MessageAdapterCategories.History |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.Level1 |
    MessageAdapterCategories.News)]
public partial class OpenDartMessageAdapter :
    MessageAdapter,
    ITokenAdapter,
    IAddressAdapter<Uri>
{
    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TokenKey,
        Description = "Open DART 40-character API authentication key.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Token { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = "Official English Open DART API root.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 1)]
    public Uri Address { get; set; } =
        new("https://engopendart.fss.or.kr/engapi/");

    /// <summary>Public disclosure-viewer address.</summary>
    [Display(
        Name = "Disclosure address",
        Description = "Public English DART disclosure viewer used for news links.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 2)]
    public Uri DisclosureAddress { get; set; } =
        new("https://englishdart.fss.or.kr/dsbh001/main.do");

    /// <summary>Top-level disclosure filter.</summary>
    [Display(
        Name = "Disclosure type",
        Description = "Open DART disclosure category used for news subscriptions.",
        GroupName = LocalizedStrings.NewsKey,
        Order = 3)]
    public OpenDartDisclosureTypes DisclosureType { get; set; } =
        OpenDartDisclosureTypes.All;

    /// <summary>Corporation-class filter.</summary>
    [Display(
        Name = "Corporation class",
        Description = "Optional KOSPI, KOSDAQ, KONEX, or other-company filter for disclosures.",
        GroupName = LocalizedStrings.NewsKey,
        Order = 4)]
    public OpenDartCorporationClasses CorporationClass { get; set; } =
        OpenDartCorporationClasses.All;

    /// <summary>Whether only final disclosure versions are requested.</summary>
    [Display(
        Name = "Final reports only",
        Description = "Exclude amended and superseded disclosure versions.",
        GroupName = LocalizedStrings.NewsKey,
        Order = 5)]
    public bool FinalReportsOnly { get; set; }

    /// <summary>
    /// Optional fixed fiscal year used for financial-indicator requests.
    /// </summary>
    [Display(
        Name = "Business year",
        Description = "Optional fiscal year for Level1 financial ratios. Empty searches backwards from the latest completed year.",
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 6)]
    public int? BusinessYear { get; set; }

    /// <summary>Periodic-report type used for financial indicators.</summary>
    [Display(
        Name = "Report type",
        Description = "Annual, first-quarter, semi-annual, or third-quarter report used for Level1 ratios.",
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 7)]
    public OpenDartReportTypes ReportType { get; set; } =
        OpenDartReportTypes.Annual;

    /// <summary>
    /// Maximum years searched or downloaded by one financial request.
    /// </summary>
    [Display(
        Name = "Financial search years",
        Description = "Maximum fiscal years searched or downloaded by one Level1 subscription.",
        GroupName = "Limits",
        Order = 8)]
    public int FinancialSearchYears { get; set; } = 4;

    /// <summary>Maximum disclosure pages downloaded per subscription.</summary>
    [Display(
        Name = "Maximum news pages",
        Description = "Maximum 100-item Open DART pages downloaded by one news subscription.",
        GroupName = "Limits",
        Order = 9)]
    public int MaxPages { get; set; } = 100;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Token), Token)
            .Set(nameof(Address), Address)
            .Set(nameof(DisclosureAddress), DisclosureAddress)
            .Set(nameof(DisclosureType), DisclosureType)
            .Set(nameof(CorporationClass), CorporationClass)
            .Set(nameof(FinalReportsOnly), FinalReportsOnly)
            .Set(nameof(BusinessYear), BusinessYear)
            .Set(nameof(ReportType), ReportType)
            .Set(nameof(FinancialSearchYears), FinancialSearchYears)
            .Set(nameof(MaxPages), MaxPages);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Token = storage.GetValue<SecureString>(nameof(Token));
        Address = storage.GetValue(nameof(Address), Address);
        DisclosureAddress = storage.GetValue(
            nameof(DisclosureAddress), DisclosureAddress);
        DisclosureType = storage.GetValue(
            nameof(DisclosureType), DisclosureType);
        CorporationClass = storage.GetValue(
            nameof(CorporationClass), CorporationClass);
        FinalReportsOnly = storage.GetValue(
            nameof(FinalReportsOnly), FinalReportsOnly);
        BusinessYear = storage.GetValue<int?>(
            nameof(BusinessYear));
        ReportType = storage.GetValue(
            nameof(ReportType), ReportType);
        FinancialSearchYears = storage.GetValue(
            nameof(FinancialSearchYears), FinancialSearchYears);
        MaxPages = storage.GetValue(nameof(MaxPages), MaxPages);
    }
}
