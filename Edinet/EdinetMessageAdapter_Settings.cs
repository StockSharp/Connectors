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
        Description = LocalizedStrings.FreeEdinetApiV2SubscriptionKeyDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Token { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = LocalizedStrings.OfficialEdinetApiV2RootDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 1)]
    public Uri Address { get; set; } =
        new("https://api.edinet-fsa.go.jp/api/v2/");

    /// <summary>Public English EDINET company-code archive.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.CompanyCodeAddressKey,
        Description = LocalizedStrings.OfficialPublicEnglishEdinetCompanyCodeZipArchiveDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 2)]
    public Uri CodeListAddress { get; set; } =
        new("https://disclosure2dl.edinet-fsa.go.jp/searchdocument/codelisteng/Edinetcode.zip");

    /// <summary>Public EDINET disclosure search page.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ViewerAddressKey,
        Description = LocalizedStrings.PublicEdinetDisclosureSearchPageUsedForNewsLinksDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 3)]
    public Uri ViewerAddress { get; set; } =
        new("https://disclosure2.edinet-fsa.go.jp/WEEE0030.aspx");

    /// <summary>Disclosure filter used for news subscriptions.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DisclosureTypeKey,
        Description = LocalizedStrings.OptionalEdinetDocumentTypeFilterDescKey,
        GroupName = LocalizedStrings.NewsKey,
        Order = 4)]
    public EdinetDisclosureTypes DisclosureType { get; set; } =
        EdinetDisclosureTypes.All;

    /// <summary>Whether only listed submitters are returned.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ListedCompaniesOnlyKey,
        Description = LocalizedStrings.ReturnOnlyCompaniesWithAJapaneseSecuritiesIdentificationCodeDescKey,
        GroupName = LocalizedStrings.SecuritiesKey,
        Order = 5)]
    public bool ListedOnly { get; set; } = true;

    /// <summary>Whether withdrawn document records are returned.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.IncludeWithdrawnKey,
        Description = LocalizedStrings.IncludeWithdrawnDocumentsAndWithdrawalRecordsDescKey,
        GroupName = LocalizedStrings.NewsKey,
        Order = 6)]
    public bool IncludeWithdrawn { get; set; }

    /// <summary>Whether unavailable document records are returned.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.IncludeUnavailableKey,
        Description = LocalizedStrings.IncludeHiddenRecordsAndRecordsWithoutDownloadableFilesDescKey,
        GroupName = LocalizedStrings.NewsKey,
        Order = 7)]
    public bool IncludeUnavailable { get; set; }

    /// <summary>Default history length when only an end date is supplied.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DefaultLookupDaysKey,
        Description = LocalizedStrings.CalendarDaysRequestedWhenANewsSubscriptionHasAnEndDateButNoStartDateDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 8)]
    public int DefaultLookupDays { get; set; } = 7;

    /// <summary>Maximum calendar days requested by one subscription.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.MaximumDaysKey,
        Description = LocalizedStrings.MaximumCalendarDaysDownloadedByOneNewsSubscriptionDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 9)]
    public int MaxDays { get; set; } = 366;

    /// <summary>Minimum delay between daily document-list requests.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.RequestIntervalKey,
        Description = LocalizedStrings.DelayBetweenConsecutiveEdinetDailyListRequestsDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 10)]
    public TimeSpan RequestInterval { get; set; } =
        TimeSpan.FromMilliseconds(500);

    /// <summary>Maximum downloaded document size in megabytes.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.MaximumDocumentSizeKey,
        Description = LocalizedStrings.MaximumPdfOrZipDocumentSizeInMegabytesDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 11)]
    public int MaxDocumentSizeMb { get; set; } = 100;

    /// <summary>Company-code list cache lifetime.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.CompanyCacheLifetimeKey,
        Description = LocalizedStrings.HowLongTheDownloadedEdinetCompanyCodeListRemainsCachedDescKey,
        GroupName = LocalizedStrings.LimitsKey,
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
