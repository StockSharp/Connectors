namespace StockSharp.JpxTdnet;

/// <summary>
/// Message adapter for the JPX TDnet timely-disclosure API.
/// </summary>
[MediaIcon(Media.MediaNames.jpx_tdnet)]
[Doc("topics/api/connectors/stock_market/jpx_tdnet.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.JpxTdnetKey,
    Description = LocalizedStrings.MarketDataConnectorKey,
    GroupName = LocalizedStrings.JapanKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.Paid |
    MessageAdapterCategories.Asia |
    MessageAdapterCategories.History |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.News)]
public partial class JpxTdnetMessageAdapter :
    MessageAdapter,
    ITokenAdapter,
    IAddressAdapter<Uri>
{
    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TokenKey,
        Description = "JPX TDnet API access key.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Token { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = "JPX TDnet production API root or an official test-server root.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 1)]
    public Uri Address { get; set; } =
        new("https://api.arrowfront.jp/");

    /// <summary>Public TDnet disclosure viewer.</summary>
    [Display(
        Name = "Viewer address",
        Description = "Public TDnet disclosure viewer used for news links.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 2)]
    public Uri ViewerAddress { get; set; } =
        new("https://www.release.tdnet.info/inbs/I_main_00.html");

    /// <summary>Index result mode.</summary>
    [Display(
        Name = "Index mode",
        Description = "Current disclosures or revision/deletion histories. History mode excludes never-modified disclosures by TDnet design.",
        GroupName = LocalizedStrings.NewsKey,
        Order = 3)]
    public JpxTdnetIndexModes IndexMode { get; set; } =
        JpxTdnetIndexModes.Current;

    /// <summary>Default history length when only an end date is supplied.</summary>
    [Display(
        Name = "Default lookup days",
        Description = "Calendar days requested when a news subscription has an end date but no start date.",
        GroupName = "Limits",
        Order = 4)]
    public int DefaultLookupDays { get; set; } = 7;

    /// <summary>Maximum calendar days requested by one subscription.</summary>
    [Display(
        Name = "Maximum days",
        Description = "Maximum calendar days downloaded by one news subscription.",
        GroupName = "Limits",
        Order = 5)]
    public int MaxDays { get; set; } = 366;

    /// <summary>Recent calendar days used for all-security lookup.</summary>
    [Display(
        Name = "Security lookup days",
        Description = "Recent calendar days scanned to discover securities when no stock code is supplied.",
        GroupName = "Limits",
        Order = 6)]
    public int SecurityLookupDays { get; set; } = 31;

    /// <summary>Minimum delay between API requests.</summary>
    [Display(
        Name = "Request interval",
        Description = "Delay between TDnet API requests. JPX permits no more than one request per second.",
        GroupName = "Limits",
        Order = 7)]
    public TimeSpan RequestInterval { get; set; } =
        TimeSpan.FromSeconds(1);

    /// <summary>Maximum downloaded document size in megabytes.</summary>
    [Display(
        Name = "Maximum document size",
        Description = "Maximum decoded PDF or XBRL ZIP size in megabytes.",
        GroupName = "Limits",
        Order = 8)]
    public int MaxDocumentSizeMb { get; set; } = 100;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Token), Token)
            .Set(nameof(Address), Address)
            .Set(nameof(ViewerAddress), ViewerAddress)
            .Set(nameof(IndexMode), IndexMode)
            .Set(nameof(DefaultLookupDays), DefaultLookupDays)
            .Set(nameof(MaxDays), MaxDays)
            .Set(nameof(SecurityLookupDays), SecurityLookupDays)
            .Set(nameof(RequestInterval), RequestInterval)
            .Set(nameof(MaxDocumentSizeMb), MaxDocumentSizeMb);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Token = storage.GetValue<SecureString>(nameof(Token));
        Address = storage.GetValue(nameof(Address), Address);
        ViewerAddress = storage.GetValue(
            nameof(ViewerAddress), ViewerAddress);
        IndexMode = storage.GetValue(
            nameof(IndexMode), IndexMode);
        DefaultLookupDays = storage.GetValue(
            nameof(DefaultLookupDays), DefaultLookupDays);
        MaxDays = storage.GetValue(nameof(MaxDays), MaxDays);
        SecurityLookupDays = storage.GetValue(
            nameof(SecurityLookupDays), SecurityLookupDays);
        RequestInterval = storage.GetValue(
            nameof(RequestInterval), RequestInterval);
        MaxDocumentSizeMb = storage.GetValue(
            nameof(MaxDocumentSizeMb), MaxDocumentSizeMb);
    }
}
