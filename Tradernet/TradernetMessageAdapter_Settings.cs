namespace StockSharp.Tradernet;

/// <summary>
/// The message adapter for Tradernet public trading API.
/// </summary>
[MediaIcon(Media.MediaNames.tradernet)]
[Doc("topics/api/connectors/europe/tradernet.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.TradernetKey,
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
    MessageAdapterCategories.Options)]
public partial class TradernetMessageAdapter :
    MessageAdapter, IKeySecretAdapter
{
    private static readonly Uri _defaultAddress =
        new("https://tradernet.com/api/");
    private static readonly Uri _defaultWebSocketAddress =
        new("wss://wss.tradernet.com/");

    /// <inheritdoc />
    [Display(
        Name = "Public API key",
        Description = "Public key generated in the Tradernet account.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Key { get; set; }

    /// <inheritdoc />
    [Display(
        Name = "Private API key",
        Description = "Private key used to sign Tradernet API requests.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public SecureString Secret { get; set; }

    /// <summary>
    /// REST API root address.
    /// </summary>
    [Display(
        Name = "API address",
        Description = "Tradernet signed public API root address.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 2)]
    public Uri Address { get; set; } = _defaultAddress;

    /// <summary>
    /// WebSocket server address.
    /// </summary>
    [Display(
        Name = "WebSocket address",
        Description = "Tradernet realtime WebSocket address.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 3)]
    public Uri WebSocketAddress { get; set; } =
        _defaultWebSocketAddress;

    /// <summary>
    /// Interval for REST portfolio and order snapshots.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.IntervalKey,
        Description = LocalizedStrings.IntervalKey +
            LocalizedStrings.Dot,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 4)]
    [BasicSetting]
    public TimeSpan PollingInterval { get; set; } =
        TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum market depth emitted by the connector.
    /// </summary>
    [Display(
        Name = "Maximum market depth",
        Description = "Maximum number of bid and ask levels emitted from Tradernet.",
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 5)]
    public int MaxMarketDepth { get; set; } = 20;

    /// <summary>
    /// Page size used for the securities directory.
    /// </summary>
    [Display(
        Name = "Securities page size",
        Description = "Number of securities requested per directory page.",
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 6)]
    public int SecuritiesPageSize { get; set; } = 100;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Key), Key)
            .Set(nameof(Secret), Secret)
            .Set(nameof(Address), Address)
            .Set(nameof(WebSocketAddress), WebSocketAddress)
            .Set(nameof(PollingInterval), PollingInterval)
            .Set(nameof(MaxMarketDepth), MaxMarketDepth)
            .Set(nameof(SecuritiesPageSize), SecuritiesPageSize);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Key = storage.GetValue<SecureString>(nameof(Key));
        Secret = storage.GetValue<SecureString>(nameof(Secret));
        Address = storage.GetValue(nameof(Address), Address);
        WebSocketAddress = storage.GetValue(
            nameof(WebSocketAddress), WebSocketAddress);
        PollingInterval = storage.GetValue(
            nameof(PollingInterval), PollingInterval);
        MaxMarketDepth = storage.GetValue(
            nameof(MaxMarketDepth), MaxMarketDepth);
        SecuritiesPageSize = storage.GetValue(
            nameof(SecuritiesPageSize), SecuritiesPageSize);
    }
}
