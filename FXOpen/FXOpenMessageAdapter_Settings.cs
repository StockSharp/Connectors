namespace StockSharp.FXOpen;

/// <summary>The message adapter for FXOpen TickTrader.</summary>
[MediaIcon(Media.MediaNames.fxopen)]
[Doc("topics/api/connectors/forex/fxopen.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.FXOpenKey,
    Description = LocalizedStrings.ForexConnectorKey,
    GroupName = LocalizedStrings.ForexKey)]
[MessageAdapterCategory(MessageAdapterCategories.FX | MessageAdapterCategories.RealTime |
    MessageAdapterCategories.Free | MessageAdapterCategories.History | MessageAdapterCategories.Level1 |
    MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Candles |
    MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(FXOpenOrderCondition))]
public partial class FXOpenMessageAdapter : MessageAdapter, IKeySecretAdapter, IDemoAdapter,
    IAddressAdapter<string>
{
    private const string _liveRestAddress = "https://ttlivewebapi.fxopen.net";
    private const string _demoRestAddress = "https://marginalttdemowebapi.fxopen.net";
    private const string _liveFeedAddress = "wss://marginalttlivewebapi.fxopen.net/feed";
    private const string _demoFeedAddress = "wss://marginalttdemowebapi.fxopen.net/feed";
    private const string _liveTradeAddress = "wss://marginalttlivewebapi.fxopen.net/trade";
    private const string _demoTradeAddress = "wss://marginalttdemowebapi.fxopen.net/trade";

    /// <summary>Supported candle time-frames.</summary>
    public static IEnumerable<TimeSpan> AllTimeFrames => FXOpenExtensions.TimeFrames.Keys;

    /// <summary>Web API token identifier.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.IdKey,
        Description = LocalizedStrings.IdentifierKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public string WebApiId { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.KeyKey,
        Description = LocalizedStrings.KeyKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public SecureString Key { get; set; }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SecretKey,
        Description = LocalizedStrings.SecretDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    [BasicSetting]
    public SecureString Secret { get; set; }

    /// <summary>Optional one-time password required by the account policy.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.OneTimePasswordKey,
        Description = LocalizedStrings.OneTimePasswordDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    [BasicSetting]
    public SecureString OneTimePassword { get; set; }

    private bool _isDemo;

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DemoKey,
        Description = LocalizedStrings.DemoTradingConnectKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 4)]
    [BasicSetting]
    public bool IsDemo
    {
        get => _isDemo;
        set
        {
            if (_isDemo == value)
                return;
            var oldRest = _isDemo ? _demoRestAddress : _liveRestAddress;
            var oldFeed = _isDemo ? _demoFeedAddress : _liveFeedAddress;
            var oldTrade = _isDemo ? _demoTradeAddress : _liveTradeAddress;
            _isDemo = value;
            if (Address.IsEmpty() || Address.EqualsIgnoreCase(oldRest))
                Address = value ? _demoRestAddress : _liveRestAddress;
            if (FeedAddress.IsEmpty() || FeedAddress.EqualsIgnoreCase(oldFeed))
                FeedAddress = value ? _demoFeedAddress : _liveFeedAddress;
            if (TradeAddress.IsEmpty() || TradeAddress.EqualsIgnoreCase(oldTrade))
                TradeAddress = value ? _demoTradeAddress : _liveTradeAddress;
        }
    }

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = LocalizedStrings.ServerAddressKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 0)]
    [BasicSetting]
    public string Address { get; set; } = _liveRestAddress;

    /// <summary>Feed WebSocket endpoint.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.MarketDataKey,
        Description = LocalizedStrings.WsEndpointKey,
        GroupName = LocalizedStrings.WebSocketAddressesKey,
        Order = 0)]
    [BasicSetting]
    public string FeedAddress { get; set; } = _liveFeedAddress;

    /// <summary>Trade WebSocket endpoint.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TransactionsKey,
        Description = LocalizedStrings.WsEndpointKey,
        GroupName = LocalizedStrings.WebSocketAddressesKey,
        Order = 1)]
    [BasicSetting]
    public string TradeAddress { get; set; } = _liveTradeAddress;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(WebApiId), WebApiId)
            .Set(nameof(Key), Key)
            .Set(nameof(Secret), Secret)
            .Set(nameof(OneTimePassword), OneTimePassword)
            .Set(nameof(IsDemo), IsDemo)
            .Set(nameof(Address), Address)
            .Set(nameof(FeedAddress), FeedAddress)
            .Set(nameof(TradeAddress), TradeAddress);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        WebApiId = storage.GetValue<string>(nameof(WebApiId));
        Key = storage.GetValue<SecureString>(nameof(Key));
        Secret = storage.GetValue<SecureString>(nameof(Secret));
        OneTimePassword = storage.GetValue<SecureString>(nameof(OneTimePassword));
        IsDemo = storage.GetValue(nameof(IsDemo), IsDemo);
        Address = NormalizeAddress(storage.GetValue(nameof(Address), Address),
            IsDemo ? _demoRestAddress : _liveRestAddress, "https");
        FeedAddress = NormalizeAddress(storage.GetValue(nameof(FeedAddress), FeedAddress),
            IsDemo ? _demoFeedAddress : _liveFeedAddress, "wss");
        TradeAddress = NormalizeAddress(storage.GetValue(nameof(TradeAddress), TradeAddress),
            IsDemo ? _demoTradeAddress : _liveTradeAddress, "wss");
    }

    private static string NormalizeAddress(string address, string fallback, string scheme)
    {
        address = address.IsEmpty() ? fallback : address.Trim();
        if (!address.Contains("://", StringComparison.Ordinal))
            address = $"{scheme}://{address.TrimStart('/')}";
        return address.TrimEnd('/');
    }

    /// <inheritdoc />
    public override string ToString()
        => base.ToString() + $": ID={WebApiId}, Key={Key.ToId()}";
}
