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
        Description = LocalizedStrings.KrxOpenApiAuthenticationKeySentInTheAuthKeyHeaderDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Token { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SampleServiceKey,
        Description = LocalizedStrings.UseTheOfficialKrxSampleEndpointInsteadOfTheApprovedProductionServiceDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public bool IsDemo { get; set; }

    /// <summary>KRX daily dataset exposed by the adapter.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DatasetKey,
        Description = LocalizedStrings.KrxMarketOrInstrumentFamilyUsedForSecurityLookupAndDailyHistoryDescKey,
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 2)]
    [BasicSetting]
    public KrxDataSets DataSet { get; set; } =
        KrxDataSets.KospiStocks;

    /// <summary>
    /// Optional local Korean reference date for lookup and latest-value requests.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ReferenceDateKey,
        Description = LocalizedStrings.OptionalKoreanMarketDateEmptyUsesThePreviousKoreanDayOr20200414ForSampleServiceDescKey,
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 3)]
    public DateTime? ReferenceDate { get; set; }

    /// <summary>Maximum days searched backwards for a current snapshot.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.LatestSearchDaysKey,
        Description = LocalizedStrings.MaximumCalendarDaysSearchedBackwardsToFindTheLatestNonEmptyKrxTradingDateDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 4)]
    public int LatestSearchDays { get; set; } = 14;

    /// <summary>Maximum API calls made by one history request.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.MaximumRequestsKey,
        Description = LocalizedStrings.MaximumDailyKrxEndpointsCalledByOneLookupOrHistorySubscriptionDescKey,
        GroupName = LocalizedStrings.LimitsKey,
        Order = 5)]
    public int MaxRequests { get; set; } = 370;

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = LocalizedStrings.KrxProductionOpenApiRootDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 6)]
    public Uri Address { get; set; } =
        new("https://data-dbg.krx.co.kr/svc/apis/");

    /// <summary>Official KRX sample API root.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SampleAddressKey,
        Description = LocalizedStrings.OfficialKrxSampleOpenApiRootDescKey,
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
