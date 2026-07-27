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
        Description = LocalizedStrings.DecodedServiceKeyIssuedByTheKoreanPublicDataPortalDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Token { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = LocalizedStrings.KoreanFscStockSecuritiesServiceRootDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 1)]
    public Uri Address { get; set; } =
        new("https://apis.data.go.kr/1160100/service/GetStockSecuritiesInfoService/");

    /// <summary>Public FSC price dataset.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DatasetKey,
        Description = LocalizedStrings.StocksIncomeSecuritiesOrOneOfTheTwoPreemptiveRightDatasetsDescKey,
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 2)]
    [BasicSetting]
    public KoreanFscDataSets DataSet { get; set; } =
        KoreanFscDataSets.Stocks;

    /// <summary>Optional KRX listing-market filter.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.MarketKey,
        Description = LocalizedStrings.OptionalKospiKosdaqOrKonexFilterWhereTheSelectedDatasetSupportsItDescKey,
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 3)]
    public KoreanFscMarkets Market { get; set; } =
        KoreanFscMarkets.All;

    /// <summary>Optional local Korean reference date.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ReferenceDateKey,
        Description = LocalizedStrings.OptionalKoreanDateUsedForSecurityLookupAndLatestSnapshotsDescKey,
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 4)]
    public DateTime? ReferenceDate { get; set; }

    /// <summary>Maximum days searched backwards for recent data.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.LatestSearchDaysKey,
        Description = LocalizedStrings.MaximumCalendarDaysSearchedBackwardsForTheLatestPublishedTradingDateDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 5)]
    public int LatestSearchDays { get; set; } = 14;

    /// <summary>Number of API records requested per page.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PageSizeKey,
        Description = LocalizedStrings.NumberOfKoreanFscRecordsRequestedPerApiPageDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 6)]
    public int PageSize { get; set; } = 1000;

    /// <summary>Maximum API pages read by one request.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.MaximumPagesKey,
        Description = LocalizedStrings.MaximumKoreanFscPagesDownloadedByOneLookupOrHistorySubscriptionDescKey,
        GroupName = LocalizedStrings.LimitsKey,
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
