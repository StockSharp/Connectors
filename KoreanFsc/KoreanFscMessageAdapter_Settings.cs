namespace StockSharp.KoreanFsc;

/// <summary>
/// Message adapter for Korean FSC public daily securities prices.
/// </summary>
[MediaIcon(Media.MediaNames.korean_fsc)]
[Doc("topics/api/connectors/stock_market/korean_fsc.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.KoreanFscKey,
    Description = LocalizedStrings.MarketDataConnectorKey,
    GroupName = LocalizedStrings.KoreaExchangeKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.Free |
    MessageAdapterCategories.Asia |
    MessageAdapterCategories.History |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.Level1 |
    MessageAdapterCategories.Candles)]
public partial class KoreanFscMessageAdapter :
    MessageAdapter,
    ITokenAdapter,
    IAddressAdapter<Uri>
{
    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TokenKey,
        Description = "Decoded service key issued by the Korean Public Data Portal.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Token { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = "Korean FSC stock-securities service root.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 1)]
    public Uri Address { get; set; } =
        new("https://apis.data.go.kr/1160100/service/GetStockSecuritiesInfoService/");

    /// <summary>Public FSC price dataset.</summary>
    [Display(
        Name = "Dataset",
        Description = "Stocks, income securities, or one of the two preemptive-right datasets.",
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 2)]
    [BasicSetting]
    public KoreanFscDataSets DataSet { get; set; } =
        KoreanFscDataSets.Stocks;

    /// <summary>Optional KRX listing-market filter.</summary>
    [Display(
        Name = "Market",
        Description = "Optional KOSPI, KOSDAQ, or KONEX filter where the selected dataset supports it.",
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 3)]
    public KoreanFscMarkets Market { get; set; } =
        KoreanFscMarkets.All;

    /// <summary>Optional local Korean reference date.</summary>
    [Display(
        Name = "Reference date",
        Description = "Optional Korean date used for security lookup and latest snapshots.",
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 4)]
    public DateTime? ReferenceDate { get; set; }

    /// <summary>Maximum days searched backwards for recent data.</summary>
    [Display(
        Name = "Latest search days",
        Description = "Maximum calendar days searched backwards for the latest published trading date.",
        GroupName = "Limits",
        Order = 5)]
    public int LatestSearchDays { get; set; } = 14;

    /// <summary>Number of API records requested per page.</summary>
    [Display(
        Name = "Page size",
        Description = "Number of Korean FSC records requested per API page.",
        GroupName = "Limits",
        Order = 6)]
    public int PageSize { get; set; } = 1000;

    /// <summary>Maximum API pages read by one request.</summary>
    [Display(
        Name = "Maximum pages",
        Description = "Maximum Korean FSC pages downloaded by one lookup or history subscription.",
        GroupName = "Limits",
        Order = 7)]
    public int MaxPages { get; set; } = 100;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Token), Token)
            .Set(nameof(Address), Address)
            .Set(nameof(DataSet), DataSet)
            .Set(nameof(Market), Market)
            .Set(nameof(ReferenceDate), ReferenceDate)
            .Set(nameof(LatestSearchDays), LatestSearchDays)
            .Set(nameof(PageSize), PageSize)
            .Set(nameof(MaxPages), MaxPages);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Token = storage.GetValue<SecureString>(nameof(Token));
        Address = storage.GetValue(nameof(Address), Address);
        DataSet = storage.GetValue(nameof(DataSet), DataSet);
        Market = storage.GetValue(nameof(Market), Market);
        ReferenceDate = storage.GetValue<DateTime?>(
            nameof(ReferenceDate));
        LatestSearchDays = storage.GetValue(
            nameof(LatestSearchDays), LatestSearchDays);
        PageSize = storage.GetValue(nameof(PageSize), PageSize);
        MaxPages = storage.GetValue(nameof(MaxPages), MaxPages);
    }
}
