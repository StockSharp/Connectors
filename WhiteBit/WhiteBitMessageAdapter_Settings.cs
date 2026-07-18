namespace StockSharp.WhiteBit;

/// <summary>
/// WhiteBIT market sections.
/// </summary>
[DataContract]
[Serializable]
public enum WhiteBitSections
{
    /// <summary>
    /// Spot markets.
    /// </summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SpotKey)]
    Spot,

    /// <summary>
    /// Collateral spot markets.
    /// </summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MarginKey)]
    Margin,

    /// <summary>
    /// Perpetual futures markets.
    /// </summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.FuturesKey)]
    Futures,
}

/// <summary>
/// The message adapter for WhiteBIT.
/// </summary>
[MediaIcon(Media.MediaNames.whitebit)]
[Doc("topics/api/connectors/crypto_exchanges/whitebit.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.WhiteBITKey,
    Description = LocalizedStrings.CryptoConnectorKey,
    GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
    MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
    MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(WhiteBitOrderCondition))]
public partial class WhiteBitMessageAdapter : MessageAdapter, IKeySecretAdapter
{
    private const string _defaultRestEndpoint = "https://whitebit.com";
    private const string _defaultWsEndpoint = "wss://api.whitebit.com/ws";

    /// <summary>
    /// Supported candle time-frames.
    /// </summary>
    public static IEnumerable<TimeSpan> AllTimeFrames => [.. WhiteBitExtensions.TimeFrames.Keys];

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

    private IEnumerable<WhiteBitSections> _sections = Enumerator.GetValues<WhiteBitSections>();

    /// <summary>
    /// Enabled market sections.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SectionsKey,
        Description = LocalizedStrings.SectionsDescKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    [BasicSetting]
    [ItemsSource(typeof(WhiteBitSections))]
    public IEnumerable<WhiteBitSections> Sections
    {
        get => _sections;
        set
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));

            var sections = value.Distinct().ToArray();
            if (sections.Length == 0)
                throw new ArgumentOutOfRangeException(nameof(value));

            _sections = sections;
        }
    }

    /// <summary>
    /// REST API endpoint. Use <c>https://whitebit.eu</c> for an EU account.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = LocalizedStrings.AddressKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 0)]
    [BasicSetting]
    public string RestEndpoint { get; set; } = _defaultRestEndpoint;

    /// <summary>
    /// Public and private WebSocket endpoint.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.WebSocketKey,
        Description = LocalizedStrings.WsEndpointKey,
        GroupName = LocalizedStrings.WebSocketAddressesKey,
        Order = 0)]
    [BasicSetting]
    public string WsEndpoint { get; set; } = _defaultWsEndpoint;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);

        storage
            .Set(nameof(Key), Key)
            .Set(nameof(Secret), Secret)
            .Set(nameof(Sections), Sections.Select(static section => section.To<string>()).JoinComma())
            .Set(nameof(RestEndpoint), RestEndpoint)
            .Set(nameof(WsEndpoint), WsEndpoint);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);

        Key = storage.GetValue<SecureString>(nameof(Key));
        Secret = storage.GetValue<SecureString>(nameof(Secret));

        var sections = storage.GetValue<string>(nameof(Sections));
        Sections = sections.IsEmpty()
            ? Enumerator.GetValues<WhiteBitSections>()
            : [.. sections.SplitByComma().Select(static section => section.To<WhiteBitSections>())];

        RestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(RestEndpoint), RestEndpoint),
            _defaultRestEndpoint, "https");
        WsEndpoint = NormalizeEndpoint(storage.GetValue(nameof(WsEndpoint), WsEndpoint),
            _defaultWsEndpoint, "wss");
    }

    private static string NormalizeEndpoint(string endpoint, string fallback, string scheme)
    {
        endpoint = endpoint.IsEmpty() ? fallback : endpoint.Trim();
        if (!endpoint.Contains("://", StringComparison.Ordinal))
            endpoint = $"{scheme}://{endpoint.TrimStart('/')}";
        return endpoint.TrimEnd('/');
    }

    /// <inheritdoc />
    public override string ToString()
        => base.ToString() + $": Sections={Sections.Select(static section => section.To<string>()).JoinComma()}, Key={Key.ToId()}";
}
