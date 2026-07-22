namespace StockSharp.VeloData;

public partial class VeloDataMessageAdapter
{
    /// <inheritdoc />
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TokenKey,
        Description = LocalizedStrings.TokenKey,
        GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
    [BasicSetting]
    public SecureString Token { get; set; }

    /// <summary>Velo Data market-data REST API v1 root.</summary>
    [Display(Name = "REST endpoint", GroupName = LocalizedStrings.AddressesKey,
        Order = 0)]
    [BasicSetting]
    public string ApiEndpoint { get; set; } = "https://api.velo.xyz/api/v1/";

    /// <summary>Velo Data news REST API root.</summary>
    [Display(Name = "News REST endpoint",
        GroupName = LocalizedStrings.AddressesKey, Order = 1)]
    [BasicSetting]
    public string NewsEndpoint { get; set; } = "https://api.velo.xyz/api/n/";

    /// <summary>Velo Data news WebSocket endpoint.</summary>
    [Display(Name = "News WebSocket endpoint",
        GroupName = LocalizedStrings.AddressesKey, Order = 2)]
    [BasicSetting]
    public string WebSocketEndpoint { get; set; } =
        "wss://api.velo.xyz/api/w/connect";

    /// <summary>Whether delisted futures and spot products are loaded.</summary>
    [Display(Name = "Include delisted",
        Description = "Load delisted futures and spot products in addition to active products.",
        GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
    public bool IsIncludeDelisted { get; set; }

    private TimeSpan _requestInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Minimum delay between REST requests.</summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.IntervalKey,
        Description = LocalizedStrings.IntervalKey,
        GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
    public TimeSpan RequestInterval
    {
        get => _requestInterval;
        set => _requestInterval = value >= TimeSpan.Zero &&
            value <= TimeSpan.FromMinutes(1)
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), value,
                    "Request interval must be between zero and one minute.");
    }

    private int _maximumItems = 25000;

    /// <summary>Maximum number of instruments returned by a lookup.</summary>
    [Display(Name = "Maximum items", GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    public int MaximumItems
    {
        get => _maximumItems;
        set => _maximumItems = value is >= 1 and <= 1000000
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value,
                "Maximum item count must be between one and 1000000.");
    }

    private int _historyLimit = 100000;

    /// <summary>Maximum number of historical records per subscription.</summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.CountKey,
        Description = LocalizedStrings.CountKey,
        GroupName = LocalizedStrings.HistoryKey, Order = 0)]
    public int HistoryLimit
    {
        get => _historyLimit;
        set => _historyLimit = value is >= 1 and <= 1000000
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value,
                "History limit must be between one and 1000000.");
    }

    private TimeSpan _historyLookback = TimeSpan.FromDays(365);

    /// <summary>Default range when a request has no start time.</summary>
    [Display(Name = "History lookback",
        Description = "Default range used when history has no start time.",
        GroupName = LocalizedStrings.HistoryKey, Order = 1)]
    public TimeSpan HistoryLookback
    {
        get => _historyLookback;
        set => _historyLookback = value >= TimeSpan.FromMinutes(1) &&
            value <= TimeSpan.FromDays(3650)
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), value,
                    "History lookback must be between one minute and ten years.");
    }

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Token), Token)
            .Set(nameof(ApiEndpoint), ApiEndpoint)
            .Set(nameof(NewsEndpoint), NewsEndpoint)
            .Set(nameof(WebSocketEndpoint), WebSocketEndpoint)
            .Set(nameof(IsIncludeDelisted), IsIncludeDelisted)
            .Set(nameof(RequestInterval), RequestInterval)
            .Set(nameof(MaximumItems), MaximumItems)
            .Set(nameof(HistoryLimit), HistoryLimit)
            .Set(nameof(HistoryLookback), HistoryLookback);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Token = storage.GetValue<SecureString>(nameof(Token));
        ApiEndpoint = storage.GetValue(nameof(ApiEndpoint), ApiEndpoint);
        NewsEndpoint = storage.GetValue(nameof(NewsEndpoint), NewsEndpoint);
        WebSocketEndpoint = storage.GetValue(nameof(WebSocketEndpoint),
            WebSocketEndpoint);
        IsIncludeDelisted = storage.GetValue(nameof(IsIncludeDelisted),
            IsIncludeDelisted);
        RequestInterval = storage.GetValue(nameof(RequestInterval), RequestInterval);
        MaximumItems = storage.GetValue(nameof(MaximumItems), MaximumItems);
        HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
        HistoryLookback = storage.GetValue(nameof(HistoryLookback), HistoryLookback);
    }

    /// <inheritdoc />
    public override IMessageAdapter Clone()
        => new VeloDataMessageAdapter(TransactionIdGenerator)
        {
            Token = Token,
            ApiEndpoint = ApiEndpoint,
            NewsEndpoint = NewsEndpoint,
            WebSocketEndpoint = WebSocketEndpoint,
            IsIncludeDelisted = IsIncludeDelisted,
            RequestInterval = RequestInterval,
            MaximumItems = MaximumItems,
            HistoryLimit = HistoryLimit,
            HistoryLookback = HistoryLookback,
        };
}
