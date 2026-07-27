namespace StockSharp.EuronextWebServices;

/// <summary>
/// Message adapter for Euronext Cash Markets Web Services.
/// </summary>
[MediaIcon(Media.MediaNames.euronext_web_services)]
[Doc("topics/api/connectors/stock_market/euronext_web_services.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.EuronextWebServicesKey,
    Description = LocalizedStrings.MarketDataConnectorKey,
    GroupName = LocalizedStrings.EuropeanKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.Paid |
    MessageAdapterCategories.Europe |
    MessageAdapterCategories.History |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.Candles |
    MessageAdapterCategories.Level1 |
    MessageAdapterCategories.MarketDepth |
    MessageAdapterCategories.Ticks)]
public partial class EuronextWebServicesMessageAdapter :
    MessageAdapter,
    ITokenAdapter,
    IAddressAdapter<Uri>
{
    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TokenKey,
        Description = LocalizedStrings.AuthenticationKeySuppliedWithTheSubscribedEuronextWebServicesProductsDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Token { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = LocalizedStrings.OfficialEuronextWebServicesGatewayApiRootDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 1)]
    public Uri Address { get; set; } =
        new("https://gateway.euronext.com/api/");

    /// <summary>Requested real-time or delayed data quality.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SessionQualityKey,
        Description = LocalizedStrings.RequestRealTimeOrDelayedMarketDataAccordingToTheSubscribedLicenseDescKey,
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 2)]
    public EuronextSessionQualities SessionQuality { get; set; } =
        EuronextSessionQualities.Delayed;

    /// <summary>Number of trading sessions requested for intraday data.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.IntradayDepthKey,
        Description = LocalizedStrings.OneRequestsTheCurrentTradingSessionTwoAlsoRequestsThePreviousSessionDescKey,
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 3)]
    public int IntradayDepth { get; set; } = 1;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Token), Token)
            .Set(nameof(Address), Address)
            .Set(nameof(SessionQuality), SessionQuality)
            .Set(nameof(IntradayDepth), IntradayDepth);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Token = storage.GetValue<SecureString>(nameof(Token));
        Address = storage.GetValue(nameof(Address), Address);
        SessionQuality = storage.GetValue(
            nameof(SessionQuality), SessionQuality);
        IntradayDepth = storage.GetValue(
            nameof(IntradayDepth), IntradayDepth);
    }
}
