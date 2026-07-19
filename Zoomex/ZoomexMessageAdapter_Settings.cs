namespace StockSharp.Zoomex;

/// <summary>
/// Zoomex market section.
/// </summary>
[DataContract]
public enum ZoomexSections
{
    /// <summary>
    /// Spot markets.
    /// </summary>
    [EnumMember]
    Spot,

    /// <summary>
    /// USDT perpetual markets.
    /// </summary>
    [EnumMember]
    Linear,

    /// <summary>
    /// Inverse perpetual markets.
    /// </summary>
    [EnumMember]
    Inverse,
}

/// <summary>
/// Zoomex account type.
/// </summary>
[DataContract]
public enum ZoomexAccountTypes
{
    /// <summary>
    /// Unified trading account.
    /// </summary>
    [EnumMember]
    Unified,

    /// <summary>
    /// Classic derivatives account.
    /// </summary>
    [EnumMember]
    Contract,

    /// <summary>
    /// Classic Spot account.
    /// </summary>
    [EnumMember]
    Spot,
}

/// <summary>
/// The message adapter for Zoomex.
/// </summary>
[MediaIcon(Media.MediaNames.zoomex)]
[Doc("topics/api/connectors/crypto_exchanges/zoomex.html")]
[Display(ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.ZoomexKey,
    Description = LocalizedStrings.CryptoConnectorKey,
    GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
    MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
    MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
    MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles |
    MessageAdapterCategories.History |
    MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(ZoomexOrderCondition))]
public partial class ZoomexMessageAdapter : MessageAdapter,
    IKeySecretAdapter
{
    private const string _defaultRestEndpoint =
        "https://openapi.zoomex.com";
    private const string _defaultWebSocketEndpoint =
        "wss://stream.zoomex.com";

    /// <summary>
    /// Supported historical and realtime candle time frames.
    /// </summary>
    public static IEnumerable<TimeSpan> AllTimeFrames =>
        ZoomexExtensions.TimeFrames;

    /// <inheritdoc />
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.KeyKey,
        Description = LocalizedStrings.KeyKey,
        GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
    [BasicSetting]
    public SecureString Key { get; set; }

    /// <inheritdoc />
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SecretKey,
        Description = LocalizedStrings.SecretDescKey,
        GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
    [BasicSetting]
    public SecureString Secret { get; set; }

    private ZoomexSections[] _sections =
    [
        ZoomexSections.Spot,
        ZoomexSections.Linear,
        ZoomexSections.Inverse,
    ];

    /// <summary>
    /// Enabled Zoomex market sections.
    /// </summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TypeKey,
        Description = LocalizedStrings.TypeKey,
        GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
    [BasicSetting]
    public ZoomexSections[] Sections
    {
        get => _sections;
        set => _sections = ValidateValues(value, nameof(value));
    }

    /// <summary>
    /// Account type used for portfolio snapshots.
    /// </summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AccountKey,
        Description = LocalizedStrings.AccountKey,
        GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
    [BasicSetting]
    public ZoomexAccountTypes AccountType { get; set; } =
        ZoomexAccountTypes.Unified;

    /// <summary>
    /// REST API endpoint.
    /// </summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = LocalizedStrings.ServerAddressKey,
        GroupName = LocalizedStrings.AddressesKey, Order = 0)]
    [BasicSetting]
    public string RestEndpoint { get; set; } = _defaultRestEndpoint;

    /// <summary>
    /// WebSocket endpoint root.
    /// </summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.WebSocketKey,
        Description = LocalizedStrings.WsEndpointKey,
        GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 0)]
    [BasicSetting]
    public string WebSocketEndpoint { get; set; } =
        _defaultWebSocketEndpoint;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Key), Key)
            .Set(nameof(Secret), Secret)
            .Set(nameof(Sections), Sections)
            .Set(nameof(AccountType), AccountType)
            .Set(nameof(RestEndpoint), RestEndpoint)
            .Set(nameof(WebSocketEndpoint), WebSocketEndpoint);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Key = storage.GetValue<SecureString>(nameof(Key));
        Secret = storage.GetValue<SecureString>(nameof(Secret));
        Sections = storage.GetValue(nameof(Sections), Sections);
        AccountType = storage.GetValue(nameof(AccountType), AccountType);
        RestEndpoint = NormalizeEndpoint(storage.GetValue(
            nameof(RestEndpoint), RestEndpoint), _defaultRestEndpoint,
            "https");
        WebSocketEndpoint = NormalizeEndpoint(storage.GetValue(
            nameof(WebSocketEndpoint), WebSocketEndpoint),
            _defaultWebSocketEndpoint, "wss");
    }

    private static T[] ValidateValues<T>(T[] values, string parameterName)
        where T : struct, Enum
    {
        if (values is not { Length: > 0 })
            throw new ArgumentException(
                "At least one value must be selected.", parameterName);
        if (values.Any(value => !Enum.IsDefined(value)))
            throw new ArgumentOutOfRangeException(parameterName);
        return [.. values.Distinct()];
    }

    private static string NormalizeEndpoint(string endpoint, string fallback,
        string scheme)
    {
        endpoint = endpoint.IsEmpty() ? fallback : endpoint.Trim();
        if (!endpoint.Contains("://", StringComparison.Ordinal))
            endpoint = $"{scheme}://{endpoint.TrimStart('/')}";
        return endpoint.TrimEnd('/');
    }

    /// <inheritdoc />
    public override string ToString()
        => base.ToString() + $": Key={Key.ToId()}, " +
            $"Sections={Sections.Select(static value => value.ToString()).JoinComma()}";
}
