namespace StockSharp.KrxOpenApi;

/// <summary>
/// Message adapter for Korea Exchange Open API daily datasets.
/// </summary>
[MediaIcon(Media.MediaNames.krx_open_api)]
[Doc("topics/api/connectors/stock_market/krx_open_api.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.KrxOpenApiKey,
    Description = LocalizedStrings.MarketDataConnectorKey,
    GroupName = LocalizedStrings.KoreaExchangeKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.Free |
    MessageAdapterCategories.Asia |
    MessageAdapterCategories.History |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.Level1 |
    MessageAdapterCategories.Candles)]
public partial class KrxOpenApiMessageAdapter :
    MessageAdapter,
    ITokenAdapter,
    IDemoAdapter,
    IAddressAdapter<Uri>
{
    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TokenKey,
        Description = "KRX Open API authentication key sent in the AUTH_KEY header.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Token { get; set; }

    /// <inheritdoc />
    [Display(
        Name = "Sample service",
        Description = "Use the official KRX sample endpoint instead of the approved production service.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public bool IsDemo { get; set; }

    /// <summary>KRX daily dataset exposed by the adapter.</summary>
    [Display(
        Name = "Dataset",
        Description = "KRX market or instrument family used for security lookup and daily history.",
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 2)]
    [BasicSetting]
    public KrxDataSets DataSet { get; set; } =
        KrxDataSets.KospiStocks;

    /// <summary>
    /// Optional local Korean reference date for lookup and latest-value requests.
    /// </summary>
    [Display(
        Name = "Reference date",
        Description = "Optional Korean market date. Empty uses the previous Korean day, or 2020-04-14 for sample service.",
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 3)]
    public DateTime? ReferenceDate { get; set; }

    /// <summary>Maximum days searched backwards for a current snapshot.</summary>
    [Display(
        Name = "Latest search days",
        Description = "Maximum calendar days searched backwards to find the latest non-empty KRX trading date.",
        GroupName = "Limits",
        Order = 4)]
    public int LatestSearchDays { get; set; } = 14;

    /// <summary>Maximum API calls made by one history request.</summary>
    [Display(
        Name = "Maximum requests",
        Description = "Maximum daily KRX endpoints called by one lookup or history subscription.",
        GroupName = "Limits",
        Order = 5)]
    public int MaxRequests { get; set; } = 370;

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = "KRX production Open API root.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 6)]
    public Uri Address { get; set; } =
        new("https://data-dbg.krx.co.kr/svc/apis/");

    /// <summary>Official KRX sample API root.</summary>
    [Display(
        Name = "Sample address",
        Description = "Official KRX sample Open API root.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 7)]
    public Uri SampleAddress { get; set; } =
        new("https://data-dbg.krx.co.kr/svc/sample/apis/");

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Token), Token)
            .Set(nameof(IsDemo), IsDemo)
            .Set(nameof(DataSet), DataSet)
            .Set(nameof(ReferenceDate), ReferenceDate)
            .Set(nameof(LatestSearchDays), LatestSearchDays)
            .Set(nameof(MaxRequests), MaxRequests)
            .Set(nameof(Address), Address)
            .Set(nameof(SampleAddress), SampleAddress);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Token = storage.GetValue<SecureString>(nameof(Token));
        IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
        DataSet = storage.GetValue(nameof(DataSet), DataSet);
        ReferenceDate = storage.GetValue<DateTime?>(
            nameof(ReferenceDate));
        LatestSearchDays = storage.GetValue(
            nameof(LatestSearchDays), LatestSearchDays);
        MaxRequests = storage.GetValue(
            nameof(MaxRequests), MaxRequests);
        Address = storage.GetValue(nameof(Address), Address);
        SampleAddress = storage.GetValue(
            nameof(SampleAddress), SampleAddress);
    }
}
