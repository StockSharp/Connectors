namespace StockSharp.TaseDataHub;

/// <summary>
/// Message adapter for the Tel Aviv Stock Exchange Data Hub API.
/// </summary>
[MediaIcon(Media.MediaNames.tase_data_hub)]
[Doc("topics/api/connectors/stock_market/tase_data_hub.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.TaseDataHubKey,
    Description = LocalizedStrings.MarketDataConnectorKey,
    GroupName = LocalizedStrings.TelAvivStockExchangeKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.Paid |
    MessageAdapterCategories.Asia |
    MessageAdapterCategories.History |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.Candles |
    MessageAdapterCategories.Level1)]
public partial class TaseDataHubMessageAdapter :
    MessageAdapter,
    IKeySecretAdapter,
    IAddressAdapter<Uri>
{
    /// <inheritdoc />
    [Display(
        Name = "OAuth client ID",
        Description = "Client ID generated for a TASE Data Hub application.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Key { get; set; }

    /// <inheritdoc />
    [Display(
        Name = "OAuth client secret",
        Description = "Client secret generated for the TASE Data Hub application.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public SecureString Secret { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = "Official TASE Data Hub gateway root.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 2)]
    public Uri Address { get; set; } =
        new("https://openapigw.tase.co.il/tase/prod/");

    /// <summary>OAuth2 scope assigned to the application.</summary>
    [Display(
        Name = "OAuth scope",
        Description = "OAuth2 application scope documented by the selected TASE products.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    public string Scope { get; set; } = "tase";

    /// <summary>Maximum calendar days searched for a security-list snapshot.</summary>
    [Display(
        Name = "Security lookup days",
        Description = "Maximum calendar days walked backwards to find the latest traded-securities list.",
        GroupName = "Limits",
        Order = 4)]
    public int SecurityLookupDays { get; set; } = 10;

    /// <summary>Duration for which security reference data is reused.</summary>
    [Display(
        Name = "Reference cache timeout",
        Description = "Duration for which the security list and type dictionary are reused.",
        GroupName = "Limits",
        Order = 5)]
    public TimeSpan ReferenceCacheTimeout { get; set; } =
        TimeSpan.FromHours(1);

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Key), Key)
            .Set(nameof(Secret), Secret)
            .Set(nameof(Address), Address)
            .Set(nameof(Scope), Scope)
            .Set(nameof(SecurityLookupDays), SecurityLookupDays)
            .Set(nameof(ReferenceCacheTimeout), ReferenceCacheTimeout);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Key = storage.GetValue<SecureString>(nameof(Key));
        Secret = storage.GetValue<SecureString>(nameof(Secret));
        Address = storage.GetValue(nameof(Address), Address);
        Scope = storage.GetValue(nameof(Scope), Scope);
        SecurityLookupDays = storage.GetValue(
            nameof(SecurityLookupDays), SecurityLookupDays);
        ReferenceCacheTimeout = storage.GetValue(
            nameof(ReferenceCacheTimeout), ReferenceCacheTimeout);
    }
}
