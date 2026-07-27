namespace StockSharp.Nuvama;

/// <summary>The message adapter for Nuvama API Connect.</summary>
[MediaIcon(Media.MediaNames.nuvama)]
[Doc("topics/api/connectors/stock_market/nuvama.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.NuvamaKey,
    Description = LocalizedStrings.StockConnectorKey,
    GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.Asia |
    MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
    MessageAdapterCategories.Candles | MessageAdapterCategories.Transactions |
    MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
    MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Stock |
    MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
    MessageAdapterCategories.FX | MessageAdapterCategories.Commodities)]
[OrderCondition(typeof(NuvamaOrderCondition))]
public partial class NuvamaMessageAdapter : MessageAdapter,
    IKeySecretAdapter, ITokenAdapter
{
    private static readonly Uri _defaultRestAddress =
        new("https://nc.nuvamawealth.com/");
    private static readonly Uri _defaultInstrumentAddress =
        new("https://nc.nuvamawealth.com/app/toccontracts/instruments.zip");
    private static readonly Uri _defaultIpAddressService =
        new("https://api.ipify.org/?format=json");

    private static readonly TimeSpan[] _timeFrames =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(3),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(7),
        TimeSpan.FromDays(30),
    ];

    /// <summary>Possible candle time-frames.</summary>
    public static IEnumerable<TimeSpan> AllTimeFrames => _timeFrames;

    /// <inheritdoc />
    [Display(
        Name = "API key",
        Description = "Application key created in the Nuvama API Connect portal.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Key { get; set; }

    /// <inheritdoc />
    [Display(
        Name = "API secret",
        Description = "Application secret created in the Nuvama API Connect portal.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public SecureString Secret { get; set; }

    /// <summary>Request ID returned by the Nuvama OAuth redirect.</summary>
    [Display(
        Name = "Request ID",
        Description = "Request ID returned after login at the Nuvama API Connect authorization URL.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    [BasicSetting]
    public SecureString RequestId { get; set; }

    /// <summary>Vendor session returned by loginvendor.</summary>
    [Display(
        Name = "Vendor session",
        Description = "SourceToken returned by loginvendor. Supply it to reuse a direct session.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    public SecureString VendorToken { get; set; }

    /// <inheritdoc />
    [Display(
        Name = "Authorization",
        Description = "User Authorization token returned by logindata. Supply it to reuse a direct session.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 4)]
    public SecureString Token { get; set; }

    /// <summary>Rotating Nuvama application identifier key.</summary>
    [Display(
        Name = "AppIdKey",
        Description = "Rotating AppIdKey supplied by Nuvama. It is refreshed from every API response.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 5)]
    [BasicSetting]
    public SecureString AppIdKey { get; set; }

    /// <summary>Nuvama trading account identifier.</summary>
    [Display(
        Name = "Account ID",
        Description = "Trading account ID. It is populated automatically by logindata.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 6)]
    public string AccountId { get; set; }

    /// <summary>Nuvama user identifier.</summary>
    [Display(
        Name = "User ID",
        Description = "Nuvama user ID. It is populated automatically by logindata.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 7)]
    public string UserId { get; set; }

    /// <summary>Nuvama account type.</summary>
    [Display(
        Name = "Account type",
        Description = "Nuvama account type used by streaming, such as EQ, CO, or COMEQ.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 8)]
    public string AccountType { get; set; } = "EQ";

    /// <summary>Registered static public IP address.</summary>
    [Display(
        Name = "Static public IP",
        Description = "Static public IP registered with Nuvama. When empty, the configured IP lookup service is used.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 9)]
    [BasicSetting]
    public string PublicIpAddress { get; set; }

    /// <summary>Employee or dependent flag returned by logindata.</summary>
    [Display(
        Name = "Employee or dependent",
        Description = "Optional empOrDependent order field returned by logindata.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 10)]
    public string EmployeeOrDependent { get; set; }

    /// <summary>Portfolio name emitted by the connector.</summary>
    [Display(
        Name = "Portfolio name",
        Description = "Portfolio name. When empty, the Nuvama account ID is used.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 11)]
    public string PortfolioName { get; set; }

    /// <summary>Default product used for new orders.</summary>
    [Display(
        Name = "Default product",
        Description = "Default Nuvama product used for new orders.",
        GroupName = LocalizedStrings.OrderKey,
        Order = 12)]
    public NuvamaProducts DefaultProduct { get; set; } = NuvamaProducts.Cnc;

    /// <summary>Interval for order and portfolio snapshots.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.IntervalKey,
        Description = LocalizedStrings.IntervalKey + LocalizedStrings.Dot,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 13)]
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum number of streaming reconnect attempts.</summary>
    [Display(
        Name = "Reconnect attempts",
        Description = "Maximum number of attempts to reconnect the Nuvama JSON stream.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 14)]
    public int ReconnectAttempts { get; set; } = 10;

    /// <summary>REST API root address.</summary>
    [Display(
        Name = "REST address",
        Description = "Nuvama REST API root address.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 15)]
    public Uri RestAddress { get; set; } = _defaultRestAddress;

    /// <summary>Public instrument ZIP address.</summary>
    [Display(
        Name = "Instrument address",
        Description = "Nuvama public instruments.zip address.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 16)]
    public Uri InstrumentAddress { get; set; } = _defaultInstrumentAddress;

    /// <summary>Public IP lookup address.</summary>
    [Display(
        Name = "IP lookup address",
        Description = "Address used to discover the public IP when Static public IP is empty.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 17)]
    public Uri IpAddressService { get; set; } = _defaultIpAddressService;

    /// <summary>Streaming host.</summary>
    [Display(
        Name = "Streaming host",
        Description = "Nuvama newline-delimited JSON streaming host.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 18)]
    public string StreamHost { get; set; } = "ncst.nuvamawealth.com";

    /// <summary>Streaming TCP port.</summary>
    [Display(
        Name = "Streaming port",
        Description = "Nuvama newline-delimited JSON streaming TCP port.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 19)]
    public int StreamPort { get; set; } = 9443;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Key), Key)
            .Set(nameof(Secret), Secret)
            .Set(nameof(RequestId), RequestId)
            .Set(nameof(VendorToken), VendorToken)
            .Set(nameof(Token), Token)
            .Set(nameof(AppIdKey), AppIdKey)
            .Set(nameof(AccountId), AccountId)
            .Set(nameof(UserId), UserId)
            .Set(nameof(AccountType), AccountType)
            .Set(nameof(PublicIpAddress), PublicIpAddress)
            .Set(nameof(EmployeeOrDependent), EmployeeOrDependent)
            .Set(nameof(PortfolioName), PortfolioName)
            .Set(nameof(DefaultProduct), DefaultProduct)
            .Set(nameof(PollingInterval), PollingInterval)
            .Set(nameof(ReconnectAttempts), ReconnectAttempts)
            .Set(nameof(RestAddress), RestAddress)
            .Set(nameof(InstrumentAddress), InstrumentAddress)
            .Set(nameof(IpAddressService), IpAddressService)
            .Set(nameof(StreamHost), StreamHost)
            .Set(nameof(StreamPort), StreamPort);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Key = storage.GetValue<SecureString>(nameof(Key));
        Secret = storage.GetValue<SecureString>(nameof(Secret));
        RequestId = storage.GetValue<SecureString>(nameof(RequestId));
        VendorToken = storage.GetValue<SecureString>(nameof(VendorToken));
        Token = storage.GetValue<SecureString>(nameof(Token));
        AppIdKey = storage.GetValue<SecureString>(nameof(AppIdKey));
        AccountId = storage.GetValue<string>(nameof(AccountId));
        UserId = storage.GetValue<string>(nameof(UserId));
        AccountType = storage.GetValue(nameof(AccountType), AccountType);
        PublicIpAddress = storage.GetValue<string>(nameof(PublicIpAddress));
        EmployeeOrDependent =
            storage.GetValue<string>(nameof(EmployeeOrDependent));
        PortfolioName = storage.GetValue<string>(nameof(PortfolioName));
        DefaultProduct = storage.GetValue(nameof(DefaultProduct), DefaultProduct);
        PollingInterval =
            storage.GetValue(nameof(PollingInterval), PollingInterval);
        ReconnectAttempts =
            storage.GetValue(nameof(ReconnectAttempts), ReconnectAttempts);
        RestAddress = storage.GetValue(nameof(RestAddress), RestAddress);
        InstrumentAddress =
            storage.GetValue(nameof(InstrumentAddress), InstrumentAddress);
        IpAddressService =
            storage.GetValue(nameof(IpAddressService), IpAddressService);
        StreamHost = storage.GetValue(nameof(StreamHost), StreamHost);
        StreamPort = storage.GetValue(nameof(StreamPort), StreamPort);
    }
}
