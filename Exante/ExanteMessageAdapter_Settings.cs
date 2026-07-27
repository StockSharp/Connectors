namespace StockSharp.Exante;

/// <summary>
/// The message adapter for EXANTE HTTP API.
/// </summary>
[MediaIcon(Media.MediaNames.exante)]
[Doc("topics/api/connectors/europe/exante.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.ExanteKey,
    Description = LocalizedStrings.StockConnectorKey,
    GroupName = LocalizedStrings.EuropeanKey)]
[MessageAdapterCategory(MessageAdapterCategories.Europe |
    MessageAdapterCategories.US | MessageAdapterCategories.Asia |
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
    MessageAdapterCategories.FX |
    MessageAdapterCategories.Commodities |
    MessageAdapterCategories.Paid)]
public partial class ExanteMessageAdapter : MessageAdapter, IKeySecretAdapter
{
    private static readonly Uri _defaultLiveAddress =
        new("https://api-live.exante.eu/");
    private static readonly Uri _defaultDemoAddress =
        new("https://api-demo.exante.eu/");

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.HttpApiKeyKey,
        Description = LocalizedStrings.ExanteHttpApiKeyCreatedInApiManagementDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Key { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SecretKeyLabelKey,
        Description = LocalizedStrings.SecretPairedWithTheExanteHttpApiKeyDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public SecureString Secret { get; set; }

    /// <summary>
    /// Whether the demo HTTP API is used.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DemoKey,
        Description = LocalizedStrings.DemoTradingConnectKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    [BasicSetting]
    public bool IsDemo { get; set; } = true;

    /// <summary>
    /// Currency used for account summary conversion.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SummaryCurrencyKey,
        Description = LocalizedStrings.IsoCurrencyUsedByTheExanteAccountSummaryApiDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    [BasicSetting]
    public string SummaryCurrency { get; set; } = "EUR";

    /// <summary>
    /// Interval for polling account summaries.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.IntervalKey,
        Description = LocalizedStrings.IntervalKey + LocalizedStrings.Dot,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 4)]
    [BasicSetting]
    public TimeSpan PollingInterval { get; set; } =
        TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum market depth sent to StockSharp.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.MaximumMarketDepthKey,
        Description = LocalizedStrings.MaximumNumberOfBidAndAskLevelsEmittedFromTheExanteFeedDescKey,
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 5)]
    public int MaxMarketDepth { get; set; } = 20;

    /// <summary>
    /// Maximum records requested from a historical endpoint.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.HistoryRequestSizeKey,
        Description = LocalizedStrings.MaximumTicksOrCandlesRequestedInOneExanteHistoryCallDescKey,
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 6)]
    public int HistoryRequestSize { get; set; } = 1000;

    /// <summary>
    /// Live HTTP API address.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.LiveApiAddressKey,
        Description = LocalizedStrings.ExanteLiveHttpApiRootAddressDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 7)]
    public Uri LiveAddress { get; set; } = _defaultLiveAddress;

    /// <summary>
    /// Demo HTTP API address.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DemoApiAddressKey,
        Description = LocalizedStrings.ExanteDemoHttpApiRootAddressDescKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 8)]
    public Uri DemoAddress { get; set; } = _defaultDemoAddress;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Key), Key)
            .Set(nameof(Secret), Secret)
            .Set(nameof(IsDemo), IsDemo)
            .Set(nameof(SummaryCurrency), SummaryCurrency)
            .Set(nameof(PollingInterval), PollingInterval)
            .Set(nameof(MaxMarketDepth), MaxMarketDepth)
            .Set(nameof(HistoryRequestSize), HistoryRequestSize)
            .Set(nameof(LiveAddress), LiveAddress)
            .Set(nameof(DemoAddress), DemoAddress);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Key = storage.GetValue<SecureString>(nameof(Key));
        Secret = storage.GetValue<SecureString>(nameof(Secret));
        IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
        SummaryCurrency = storage.GetValue(
            nameof(SummaryCurrency), SummaryCurrency);
        PollingInterval = storage.GetValue(
            nameof(PollingInterval), PollingInterval);
        MaxMarketDepth = storage.GetValue(
            nameof(MaxMarketDepth), MaxMarketDepth);
        HistoryRequestSize = storage.GetValue(
            nameof(HistoryRequestSize), HistoryRequestSize);
        LiveAddress = storage.GetValue(nameof(LiveAddress), LiveAddress);
        DemoAddress = storage.GetValue(nameof(DemoAddress), DemoAddress);
    }
}
