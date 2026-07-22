namespace StockSharp.ChainlinkDataStreams;

public partial class ChainlinkDataStreamsMessageAdapter
{
    private const string _defaultRestEndpoint =
        "https://api.dataengine.chain.link/";
    private const string _defaultWebSocketEndpoint =
        "wss://ws.dataengine.chain.link/";

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.KeyKey,
        Description = LocalizedStrings.KeyKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Key { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SecretKey,
        Description = LocalizedStrings.SecretDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public SecureString Secret { get; set; }

    /// <summary>Chainlink Data Streams REST API root.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = LocalizedStrings.ServerAddressKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 0)]
    [BasicSetting]
    public string RestEndpoint { get; set; } = _defaultRestEndpoint;

    /// <summary>Chainlink Data Streams WebSocket API root.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.WebSocketKey,
        Description = LocalizedStrings.WsEndpointKey,
        GroupName = LocalizedStrings.WebSocketAddressesKey,
        Order = 0)]
    [BasicSetting]
    public string WebSocketEndpoint { get; set; } =
        _defaultWebSocketEndpoint;

    /// <summary>Whether all origins advertised by Chainlink are used.</summary>
    [Display(
        Name = "High availability",
        Description = "Connect to all origins advertised by Chainlink and deduplicate reports.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    public bool IsHighAvailability { get; set; } = true;

    private TimeSpan _requestInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>Minimum delay between REST requests.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.IntervalKey,
        Description = LocalizedStrings.IntervalKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    public TimeSpan RequestInterval
    {
        get => _requestInterval;
        set => _requestInterval = value >= TimeSpan.Zero &&
            value <= TimeSpan.FromMinutes(1)
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), value,
                    "Request interval must be between zero and one minute.");
    }

    private int _maximumFeeds = 10000;

    /// <summary>Maximum number of entitled feeds cached at connection.</summary>
    [Display(
        Name = "Maximum feeds",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 4)]
    public int MaximumFeeds
    {
        get => _maximumFeeds;
        set => _maximumFeeds = value is >= 1 and <= 1000000
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value,
                "Maximum feed count must be between one and 1000000.");
    }

    private int _historyLimit = 10000;

    /// <summary>Maximum number of historical reports per subscription.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.CountKey,
        Description = LocalizedStrings.CountKey,
        GroupName = LocalizedStrings.HistoryKey,
        Order = 0)]
    public int HistoryLimit
    {
        get => _historyLimit;
        set => _historyLimit = value is >= 1 and <= 1000000
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value,
                "History limit must be between one and 1000000.");
    }

    private TimeSpan _historyLookback = TimeSpan.FromHours(3);

    /// <summary>Default range when historical data has no start time.</summary>
    [Display(
        Name = "History lookback",
        Description = "Default range used when history has no start time.",
        GroupName = LocalizedStrings.HistoryKey,
        Order = 1)]
    public TimeSpan HistoryLookback
    {
        get => _historyLookback;
        set => _historyLookback = value >= TimeSpan.FromSeconds(1) &&
            value <= TimeSpan.FromDays(3650)
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), value,
                    "History lookback must be between one second and ten years.");
    }

    private int _reportsPerPage = 1000;

    /// <summary>Maximum reports requested from one page.</summary>
    [Display(
        Name = "Reports per page",
        Description = "Maximum number of reports requested from one REST page.",
        GroupName = LocalizedStrings.HistoryKey,
        Order = 2)]
    public int ReportsPerPage
    {
        get => _reportsPerPage;
        set => _reportsPerPage = value is >= 1 and <= 10000
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value,
                "Reports per page must be between one and 10000.");
    }

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Key), Key)
            .Set(nameof(Secret), Secret)
            .Set(nameof(RestEndpoint), RestEndpoint)
            .Set(nameof(WebSocketEndpoint), WebSocketEndpoint)
            .Set(nameof(IsHighAvailability), IsHighAvailability)
            .Set(nameof(RequestInterval), RequestInterval)
            .Set(nameof(MaximumFeeds), MaximumFeeds)
            .Set(nameof(HistoryLimit), HistoryLimit)
            .Set(nameof(HistoryLookback), HistoryLookback)
            .Set(nameof(ReportsPerPage), ReportsPerPage);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Key = storage.GetValue<SecureString>(nameof(Key));
        Secret = storage.GetValue<SecureString>(nameof(Secret));
        RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
        WebSocketEndpoint = storage.GetValue(nameof(WebSocketEndpoint),
            WebSocketEndpoint);
        IsHighAvailability = storage.GetValue(nameof(IsHighAvailability),
            IsHighAvailability);
        RequestInterval = storage.GetValue(nameof(RequestInterval), RequestInterval);
        MaximumFeeds = storage.GetValue(nameof(MaximumFeeds), MaximumFeeds);
        HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
        HistoryLookback = storage.GetValue(nameof(HistoryLookback), HistoryLookback);
        ReportsPerPage = storage.GetValue(nameof(ReportsPerPage), ReportsPerPage);
    }

    /// <inheritdoc />
    public override IMessageAdapter Clone()
        => new ChainlinkDataStreamsMessageAdapter(TransactionIdGenerator)
        {
            Key = Key,
            Secret = Secret,
            RestEndpoint = RestEndpoint,
            WebSocketEndpoint = WebSocketEndpoint,
            IsHighAvailability = IsHighAvailability,
            RequestInterval = RequestInterval,
            MaximumFeeds = MaximumFeeds,
            HistoryLimit = HistoryLimit,
            HistoryLookback = HistoryLookback,
            ReportsPerPage = ReportsPerPage,
        };

    /// <inheritdoc />
    public override string ToString() => base.ToString() + $": Key={Key.ToId()}";
}
