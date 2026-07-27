namespace StockSharp.B3Up2Data;

/// <summary>Message adapter for B3 UP2DATA Cloud.</summary>
[MediaIcon(Media.MediaNames.b3up2data)]
[Doc("topics/api/connectors/stock_market/b3_up2data.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.B3Up2DataKey,
    Description = LocalizedStrings.MarketDataConnectorKey,
    GroupName = LocalizedStrings.MarketDataKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.Paid |
    MessageAdapterCategories.History |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.Candles |
    MessageAdapterCategories.Level1)]
public partial class B3Up2DataMessageAdapter :
    MessageAdapter,
    IKeySecretAdapter,
    IAddressAdapter<Uri>
{
    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.KeyKey,
        Description = LocalizedStrings.ClientIdFromTheB3Up2dataCloudAccessKitDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Key { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SecretKey,
        Description = LocalizedStrings.ClientSecretFromTheB3Up2dataCloudAccessKitDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public SecureString Secret { get; set; }

    /// <summary>Path to the contracted PFX certificate.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.CertificatePathKey,
        Description = LocalizedStrings.PathToThePfxCertificateSuppliedByB3DescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    [BasicSetting]
    public string CertificatePath { get; set; }

    /// <summary>Password of the contracted PFX certificate.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.CertificatePasswordKey,
        Description = LocalizedStrings.PasswordOfThePfxCertificateSuppliedByB3DescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    [BasicSetting]
    public SecureString CertificatePassword { get; set; }

    /// <summary>
    /// Optional existing Azure Blob SAS URI.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SasUriKey,
        Description = LocalizedStrings.OptionalExistingUp2dataAzureBlobContainerSasUriWhenSetApiCredentialsAndCertificateAreNotUsedDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 4)]
    public SecureString SasUri { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = LocalizedStrings.OfficialB3Up2dataCloudApiRootDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 5)]
    [BasicSetting]
    public Uri Address { get; set; } =
        new("https://up2data.b3.com.br/");

    /// <summary>Name fragment of the contracted SAS channel.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SasChannelKey,
        Description = LocalizedStrings.CaseInsensitiveNameFragmentUsedToSelectAContractedSasChannelDescKey,
        GroupName = LocalizedStrings.CloudKey,
        Order = 6)]
    public string ChannelName { get; set; } = "EOD";

    /// <summary>Preferred file format.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.FileFormatKey,
        Description = LocalizedStrings.Up2dataFileFormatNormalSecuritiesLevel1AndCandlesRequireCsvAllFormatsAreAvailableAsRawFilesDescKey,
        GroupName = LocalizedStrings.CloudKey,
        Order = 7)]
    public B3Up2DataFileFormats FileFormat { get; set; } =
        B3Up2DataFileFormats.Csv;

    /// <summary>Prefix used by the raw blob-catalog data type.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.BlobPrefixKey,
        Description = LocalizedStrings.OptionalAzureBlobPrefixUsedByTheRawBlobCatalogSubscriptionDescKey,
        GroupName = LocalizedStrings.CloudKey,
        Order = 8)]
    public string BlobPrefix { get; set; }

    /// <summary>Maximum date lookback.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.LookbackDaysKey,
        Description = LocalizedStrings.MaximumNumberOfCalendarDaysSearchedForTheLatestAvailableUp2dataFileDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 9)]
    public int LookbackDays { get; set; } = 30;

    /// <summary>Azure List Blobs page size.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ListPageSizeKey,
        Description = LocalizedStrings.MaximumAzureBlobEntriesRequestedPerPageDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 10)]
    public int PageSize { get; set; } = 5000;

    /// <summary>Maximum Azure List Blobs pages.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ListPageLimitKey,
        Description = LocalizedStrings.SafetyLimitForPaginatedAzureBlobListingsDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 11)]
    public int MaxPages { get; set; } = 100;

    /// <summary>Maximum raw files returned by default.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.RawFileLimitKey,
        Description = LocalizedStrings.DefaultMaximumNumberOfRawUp2dataFilesReturnedPerSubscriptionDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 12)]
    public int MaxRawFiles { get; set; } = 100;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Key), Key)
            .Set(nameof(Secret), Secret)
            .Set(nameof(CertificatePath), CertificatePath)
            .Set(nameof(CertificatePassword), CertificatePassword)
            .Set(nameof(SasUri), SasUri)
            .Set(nameof(Address), Address)
            .Set(nameof(ChannelName), ChannelName)
            .Set(nameof(FileFormat), FileFormat)
            .Set(nameof(BlobPrefix), BlobPrefix)
            .Set(nameof(LookbackDays), LookbackDays)
            .Set(nameof(PageSize), PageSize)
            .Set(nameof(MaxPages), MaxPages)
            .Set(nameof(MaxRawFiles), MaxRawFiles);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Key = storage.GetValue<SecureString>(nameof(Key));
        Secret = storage.GetValue<SecureString>(nameof(Secret));
        CertificatePath = storage.GetValue<string>(
            nameof(CertificatePath));
        CertificatePassword = storage.GetValue<SecureString>(
            nameof(CertificatePassword));
        SasUri = storage.GetValue<SecureString>(nameof(SasUri));
        Address = storage.GetValue(nameof(Address), Address);
        ChannelName = storage.GetValue(
            nameof(ChannelName), ChannelName);
        FileFormat = storage.GetValue(
            nameof(FileFormat), FileFormat);
        BlobPrefix = storage.GetValue<string>(nameof(BlobPrefix));
        LookbackDays = storage.GetValue(
            nameof(LookbackDays), LookbackDays);
        PageSize = storage.GetValue(
            nameof(PageSize), PageSize);
        MaxPages = storage.GetValue(
            nameof(MaxPages), MaxPages);
        MaxRawFiles = storage.GetValue(
            nameof(MaxRawFiles), MaxRawFiles);
    }
}
