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
        Description = "Client ID from the B3 UP2DATA Cloud access kit.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Key { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SecretKey,
        Description = "Client secret from the B3 UP2DATA Cloud access kit.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public SecureString Secret { get; set; }

    /// <summary>Path to the contracted PFX certificate.</summary>
    [Display(
        Name = "Certificate path",
        Description = "Path to the PFX certificate supplied by B3.",
        GroupName = "Connection",
        Order = 2)]
    [BasicSetting]
    public string CertificatePath { get; set; }

    /// <summary>Password of the contracted PFX certificate.</summary>
    [Display(
        Name = "Certificate password",
        Description = "Password of the PFX certificate supplied by B3.",
        GroupName = "Connection",
        Order = 3)]
    [BasicSetting]
    public SecureString CertificatePassword { get; set; }

    /// <summary>
    /// Optional existing Azure Blob SAS URI.
    /// </summary>
    [Display(
        Name = "SAS URI",
        Description = "Optional existing UP2DATA Azure Blob container SAS URI. When set, API credentials and certificate are not used.",
        GroupName = "Connection",
        Order = 4)]
    public SecureString SasUri { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = "Official B3 UP2DATA Cloud API root.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 5)]
    [BasicSetting]
    public Uri Address { get; set; } =
        new("https://up2data.b3.com.br/");

    /// <summary>Name fragment of the contracted SAS channel.</summary>
    [Display(
        Name = "SAS channel",
        Description = "Case-insensitive name fragment used to select a contracted SAS channel.",
        GroupName = "Cloud",
        Order = 6)]
    public string ChannelName { get; set; } = "EOD";

    /// <summary>Preferred file format.</summary>
    [Display(
        Name = "File format",
        Description = "UP2DATA file format. Normal securities, Level1, and candles require CSV; all formats are available as raw files.",
        GroupName = "Cloud",
        Order = 7)]
    public B3Up2DataFileFormats FileFormat { get; set; } =
        B3Up2DataFileFormats.Csv;

    /// <summary>Prefix used by the raw blob-catalog data type.</summary>
    [Display(
        Name = "Blob prefix",
        Description = "Optional Azure Blob prefix used by the raw blob-catalog subscription.",
        GroupName = "Cloud",
        Order = 8)]
    public string BlobPrefix { get; set; }

    /// <summary>Maximum date lookback.</summary>
    [Display(
        Name = "Lookback days",
        Description = "Maximum number of calendar days searched for the latest available UP2DATA file.",
        GroupName = "Limits",
        Order = 9)]
    public int LookbackDays { get; set; } = 30;

    /// <summary>Azure List Blobs page size.</summary>
    [Display(
        Name = "List page size",
        Description = "Maximum Azure Blob entries requested per page.",
        GroupName = "Limits",
        Order = 10)]
    public int PageSize { get; set; } = 5000;

    /// <summary>Maximum Azure List Blobs pages.</summary>
    [Display(
        Name = "List page limit",
        Description = "Safety limit for paginated Azure Blob listings.",
        GroupName = "Limits",
        Order = 11)]
    public int MaxPages { get; set; } = 100;

    /// <summary>Maximum raw files returned by default.</summary>
    [Display(
        Name = "Raw file limit",
        Description = "Default maximum number of raw UP2DATA files returned per subscription.",
        GroupName = "Limits",
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
