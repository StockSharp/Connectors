namespace StockSharp.Edinet;

/// <summary>
/// Message adapter for the Japanese EDINET API v2.
/// </summary>
[MediaIcon(Media.MediaNames.edinet)]
[Doc("topics/api/connectors/stock_market/edinet.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.EdinetKey,
    Description = LocalizedStrings.MarketDataConnectorKey,
    GroupName = LocalizedStrings.JapanKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.Free |
    MessageAdapterCategories.Asia |
    MessageAdapterCategories.History |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.News)]
public partial class EdinetMessageAdapter :
    MessageAdapter,
    ITokenAdapter,
    IAddressAdapter<Uri>
{
    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TokenKey,
        Description = "Free EDINET API v2 subscription key.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Token { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = "Official EDINET API v2 root.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 1)]
    public Uri Address { get; set; } =
        new("https://api.edinet-fsa.go.jp/api/v2/");

    /// <summary>Public English EDINET company-code archive.</summary>
    [Display(
        Name = "Company code address",
        Description = "Official public English EDINET company-code ZIP archive.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 2)]
    public Uri CodeListAddress { get; set; } =
        new("https://disclosure2dl.edinet-fsa.go.jp/searchdocument/codelisteng/Edinetcode.zip");

    /// <summary>Public EDINET disclosure search page.</summary>
    [Display(
        Name = "Viewer address",
        Description = "Public EDINET disclosure search page used for news links.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 3)]
    public Uri ViewerAddress { get; set; } =
        new("https://disclosure2.edinet-fsa.go.jp/WEEE0030.aspx");

    /// <summary>Disclosure filter used for news subscriptions.</summary>
    [Display(
        Name = "Disclosure type",
        Description = "Optional EDINET document-type filter.",
        GroupName = LocalizedStrings.NewsKey,
        Order = 4)]
    public EdinetDisclosureTypes DisclosureType { get; set; } =
        EdinetDisclosureTypes.All;

    /// <summary>Whether only listed submitters are returned.</summary>
    [Display(
        Name = "Listed companies only",
        Description = "Return only companies with a Japanese securities identification code.",
        GroupName = LocalizedStrings.SecuritiesKey,
        Order = 5)]
    public bool ListedOnly { get; set; } = true;

    /// <summary>Whether withdrawn document records are returned.</summary>
    [Display(
        Name = "Include withdrawn",
        Description = "Include withdrawn documents and withdrawal records.",
        GroupName = LocalizedStrings.NewsKey,
        Order = 6)]
    public bool IncludeWithdrawn { get; set; }

    /// <summary>Whether unavailable document records are returned.</summary>
    [Display(
        Name = "Include unavailable",
        Description = "Include hidden records and records without downloadable files.",
        GroupName = LocalizedStrings.NewsKey,
        Order = 7)]
    public bool IncludeUnavailable { get; set; }

    /// <summary>Default history length when only an end date is supplied.</summary>
    [Display(
        Name = "Default lookup days",
        Description = "Calendar days requested when a news subscription has an end date but no start date.",
        GroupName = "Limits",
        Order = 8)]
    public int DefaultLookupDays { get; set; } = 7;

    /// <summary>Maximum calendar days requested by one subscription.</summary>
    [Display(
        Name = "Maximum days",
        Description = "Maximum calendar days downloaded by one news subscription.",
        GroupName = "Limits",
        Order = 9)]
    public int MaxDays { get; set; } = 366;

    /// <summary>Minimum delay between daily document-list requests.</summary>
    [Display(
        Name = "Request interval",
        Description = "Delay between consecutive EDINET daily-list requests.",
        GroupName = "Limits",
        Order = 10)]
    public TimeSpan RequestInterval { get; set; } =
        TimeSpan.FromMilliseconds(500);

    /// <summary>Maximum downloaded document size in megabytes.</summary>
    [Display(
        Name = "Maximum document size",
        Description = "Maximum PDF or ZIP document size in megabytes.",
        GroupName = "Limits",
        Order = 11)]
    public int MaxDocumentSizeMb { get; set; } = 100;

    /// <summary>Company-code list cache lifetime.</summary>
    [Display(
        Name = "Company cache lifetime",
        Description = "How long the downloaded EDINET company-code list remains cached.",
        GroupName = "Limits",
        Order = 12)]
    public TimeSpan CodeListCacheTimeout { get; set; } =
        TimeSpan.FromDays(1);

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Token), Token)
            .Set(nameof(Address), Address)
            .Set(nameof(CodeListAddress), CodeListAddress)
            .Set(nameof(ViewerAddress), ViewerAddress)
            .Set(nameof(DisclosureType), DisclosureType)
            .Set(nameof(ListedOnly), ListedOnly)
            .Set(nameof(IncludeWithdrawn), IncludeWithdrawn)
            .Set(nameof(IncludeUnavailable), IncludeUnavailable)
            .Set(nameof(DefaultLookupDays), DefaultLookupDays)
            .Set(nameof(MaxDays), MaxDays)
            .Set(nameof(RequestInterval), RequestInterval)
            .Set(nameof(MaxDocumentSizeMb), MaxDocumentSizeMb)
            .Set(nameof(CodeListCacheTimeout), CodeListCacheTimeout);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Token = storage.GetValue<SecureString>(nameof(Token));
        Address = storage.GetValue(nameof(Address), Address);
        CodeListAddress = storage.GetValue(
            nameof(CodeListAddress), CodeListAddress);
        ViewerAddress = storage.GetValue(
            nameof(ViewerAddress), ViewerAddress);
        DisclosureType = storage.GetValue(
            nameof(DisclosureType), DisclosureType);
        ListedOnly = storage.GetValue(
            nameof(ListedOnly), ListedOnly);
        IncludeWithdrawn = storage.GetValue(
            nameof(IncludeWithdrawn), IncludeWithdrawn);
        IncludeUnavailable = storage.GetValue(
            nameof(IncludeUnavailable), IncludeUnavailable);
        DefaultLookupDays = storage.GetValue(
            nameof(DefaultLookupDays), DefaultLookupDays);
        MaxDays = storage.GetValue(nameof(MaxDays), MaxDays);
        RequestInterval = storage.GetValue(
            nameof(RequestInterval), RequestInterval);
        MaxDocumentSizeMb = storage.GetValue(
            nameof(MaxDocumentSizeMb), MaxDocumentSizeMb);
        CodeListCacheTimeout = storage.GetValue(
            nameof(CodeListCacheTimeout), CodeListCacheTimeout);
    }
}
