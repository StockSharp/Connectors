namespace StockSharp.Directa;

/// <summary>
/// Message adapter for the Directa Darwin public socket API.
/// </summary>
[MediaIcon(Media.MediaNames.directa)]
[Doc("topics/api/connectors/europe/directa.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.DirectaKey,
    Description = LocalizedStrings.StockConnectorKey,
    GroupName = LocalizedStrings.EuropeanKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.Europe |
    MessageAdapterCategories.US |
    MessageAdapterCategories.Transactions |
    MessageAdapterCategories.RealTime |
    MessageAdapterCategories.History |
    MessageAdapterCategories.Candles |
    MessageAdapterCategories.Level1 |
    MessageAdapterCategories.MarketDepth |
    MessageAdapterCategories.Ticks |
    MessageAdapterCategories.Stock |
    MessageAdapterCategories.Futures |
    MessageAdapterCategories.Options |
    MessageAdapterCategories.FX)]
[OrderCondition(typeof(DirectaOrderCondition))]
public partial class DirectaMessageAdapter :
    MessageAdapter, IAddressAdapter<EndPoint>
{
    /// <summary>
    /// Default trading and portfolio endpoint.
    /// </summary>
    public static readonly EndPoint DefaultAddress =
        new IPEndPoint(IPAddress.Loopback, 10002);

    /// <summary>
    /// Default realtime datafeed endpoint.
    /// </summary>
    public static readonly EndPoint DefaultDataAddress =
        new IPEndPoint(IPAddress.Loopback, 10001);

    /// <summary>
    /// Default historical-data endpoint.
    /// </summary>
    public static readonly EndPoint DefaultHistoryAddress =
        new IPEndPoint(IPAddress.Loopback, 10003);

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TradingAddressKey,
        Description = LocalizedStrings.LocalDarwinTradingAndPortfolioSocketDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 0)]
    [BasicSetting]
    public EndPoint Address { get; set; } = DefaultAddress;

    /// <summary>
    /// Realtime market-data socket.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DatafeedAddressKey,
        Description = LocalizedStrings.LocalDarwinRealtimeDatafeedSocketDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 1)]
    [BasicSetting]
    public EndPoint DataAddress { get; set; } =
        DefaultDataAddress;

    /// <summary>
    /// Historical-data socket.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.HistoryAddressKey,
        Description = LocalizedStrings.LocalDarwinHistoricalCallsSocketDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 2)]
    [BasicSetting]
    public EndPoint HistoryAddress { get; set; } =
        DefaultHistoryAddress;

    /// <summary>
    /// Timeout for a block response from Darwin.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.RequestTimeoutKey,
        Description = LocalizedStrings.MaximumWaitForADarwinListOrHistoryResponseDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    public TimeSpan RequestTimeout { get; set; } =
        TimeSpan.FromMinutes(2);

    /// <summary>
    /// Automatically send CONFORD when Darwin requests confirmation.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AutoConfirmOrdersKey,
        Description = LocalizedStrings.AutomaticallyConfirmOrdersAlreadySubmittedThroughThisConnectorDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 4)]
    public bool AutoConfirmOrders { get; set; } = true;

    /// <summary>
    /// Maximum requested order-book depth.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.MaximumMarketDepthKey,
        Description = LocalizedStrings.MaximumDarwinOrderBookDepthFrom1To20DescKey,
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 5)]
    public int MaxMarketDepth { get; set; } = 20;

    /// <summary>
    /// Time zone used by Darwin timestamps without an offset.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DarwinTimeZoneKey,
        Description = LocalizedStrings.IanaOrWindowsTimeZoneIdentifierForSocketTimestampsDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 6)]
    public string TimeZoneId { get; set; } = "Europe/Rome";

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Address), Address?.To<string>())
            .Set(nameof(DataAddress),
                DataAddress?.To<string>())
            .Set(nameof(HistoryAddress),
                HistoryAddress?.To<string>())
            .Set(nameof(RequestTimeout), RequestTimeout)
            .Set(nameof(AutoConfirmOrders),
                AutoConfirmOrders)
            .Set(nameof(MaxMarketDepth), MaxMarketDepth)
            .Set(nameof(TimeZoneId), TimeZoneId);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Address = storage.GetValue<EndPoint>(
            nameof(Address)) ?? DefaultAddress;
        DataAddress = storage.GetValue<EndPoint>(
            nameof(DataAddress)) ?? DefaultDataAddress;
        HistoryAddress = storage.GetValue<EndPoint>(
            nameof(HistoryAddress)) ?? DefaultHistoryAddress;
        RequestTimeout = storage.GetValue(
            nameof(RequestTimeout), RequestTimeout);
        AutoConfirmOrders = storage.GetValue(
            nameof(AutoConfirmOrders), AutoConfirmOrders);
        MaxMarketDepth = storage.GetValue(
            nameof(MaxMarketDepth), MaxMarketDepth);
        TimeZoneId = storage.GetValue(
            nameof(TimeZoneId), TimeZoneId);
    }
}
