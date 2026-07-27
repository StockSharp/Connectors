namespace StockSharp.TossSecurities;

/// <summary>
/// The message adapter for Toss Securities Open API.
/// </summary>
[MediaIcon(Media.MediaNames.tosssecurities)]
[Doc("topics/api/connectors/stock_market/toss_securities.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.TossSecuritiesKey,
    Description = LocalizedStrings.StockConnectorKey,
    GroupName = LocalizedStrings.KoreaExchangeKey)]
[MessageAdapterCategory(
    MessageAdapterCategories.Asia |
    MessageAdapterCategories.Free |
    MessageAdapterCategories.RealTime |
    MessageAdapterCategories.History |
    MessageAdapterCategories.Candles |
    MessageAdapterCategories.Transactions |
    MessageAdapterCategories.Ticks |
    MessageAdapterCategories.Level1 |
    MessageAdapterCategories.MarketDepth |
    MessageAdapterCategories.Stock)]
[OrderCondition(typeof(TossSecuritiesOrderCondition))]
public partial class TossSecuritiesMessageAdapter :
    MessageAdapter, IKeySecretAdapter
{
    private static readonly Uri _defaultRestAddress =
        new("https://openapi.tossinvest.com/");

    /// <summary>OAuth client ID created in Toss Securities WTS.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.KeyKey,
        Description = "OAuth client ID created in Toss Securities WTS.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Key { get; set; }

    /// <summary>OAuth client secret created in Toss Securities WTS.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SecretKey,
        Description = "OAuth client secret created in Toss Securities WTS.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public SecureString Secret { get; set; }

    /// <summary>
    /// Account sequence from <c>GET /api/v1/accounts</c>. Zero selects the
    /// first brokerage account.
    /// </summary>
    [Display(
        Name = "Account sequence",
        Description = "Account sequence returned by Toss Securities. Zero selects the first account.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    [BasicSetting]
    public long AccountSequence { get; set; }

    /// <summary>
    /// Optional portfolio alias for the selected account.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PortfolioNameKey,
        Description = LocalizedStrings.OrderPortfolioNameKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    public string PortfolioName { get; set; }

    /// <summary>Interval for polling REST market data.</summary>
    [Display(
        Name = "Market polling interval",
        Description = "Interval for polling live prices, order books, trades, and candles.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 4)]
    [BasicSetting]
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Interval for polling orders and portfolios.</summary>
    [Display(
        Name = "Account polling interval",
        Description = "Interval for polling order status, holdings, and buying power.",
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 5)]
    public TimeSpan AccountPollingInterval { get; set; } =
        TimeSpan.FromSeconds(10);

    /// <summary>Whether historical stock candles are adjusted.</summary>
    [Display(
        Name = "Adjusted candles",
        Description = "Request split- and dividend-adjusted stock candles.",
        GroupName = LocalizedStrings.MarketDataKey,
        Order = 6)]
    public bool AdjustedCandles { get; set; } = true;

    /// <summary>REST API server root.</summary>
    [Display(
        Name = "API address",
        Description = "Toss Securities Open API server root.",
        GroupName = LocalizedStrings.AddressesKey,
        Order = 7)]
    public Uri RestAddress { get; set; } = _defaultRestAddress;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Key), Key)
            .Set(nameof(Secret), Secret)
            .Set(nameof(AccountSequence), AccountSequence)
            .Set(nameof(PortfolioName), PortfolioName)
            .Set(nameof(PollingInterval), PollingInterval)
            .Set(nameof(AccountPollingInterval), AccountPollingInterval)
            .Set(nameof(AdjustedCandles), AdjustedCandles)
            .Set(nameof(RestAddress), RestAddress);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Key = storage.GetValue<SecureString>(nameof(Key));
        Secret = storage.GetValue<SecureString>(nameof(Secret));
        AccountSequence = storage.GetValue(
            nameof(AccountSequence), AccountSequence);
        PortfolioName = storage.GetValue(
            nameof(PortfolioName), PortfolioName);
        PollingInterval = storage.GetValue(
            nameof(PollingInterval), PollingInterval);
        AccountPollingInterval = storage.GetValue(
            nameof(AccountPollingInterval), AccountPollingInterval);
        AdjustedCandles = storage.GetValue(
            nameof(AdjustedCandles), AdjustedCandles);
        RestAddress = storage.GetValue(nameof(RestAddress), RestAddress);
    }
}
