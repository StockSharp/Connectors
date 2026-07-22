namespace StockSharp.Coinhako;

/// <summary>
/// The message adapter for the Coinhako Public API.
/// </summary>
[MediaIcon(Media.MediaNames.coinhako)]
[Doc("topics/api/connectors/crypto_exchanges/coinhako.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.CoinhakoKey,
    Description = LocalizedStrings.CryptoConnectorKey,
    GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
    MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
    MessageAdapterCategories.Level1 | MessageAdapterCategories.History |
    MessageAdapterCategories.Transactions)]
public partial class CoinhakoMessageAdapter : MessageAdapter, IKeySecretAdapter
{
    private const string _defaultEndpoint = "https://www.coinhako.com";
    private const string _defaultCounterCurrencies =
        "SGD,USD,USDT,USDC,USDS,XSGD,TNSGD,TNUSD";
    private TimeSpan _pollingInterval = TimeSpan.FromSeconds(10);

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

    /// <summary>
    /// Coinhako Public API endpoint.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = LocalizedStrings.ServerAddressKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 0)]
    [BasicSetting]
    public string RestEndpoint { get; set; } = _defaultEndpoint;

    /// <summary>
    /// Comma-separated counter currencies used for market discovery.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.CurrencyKey,
        Description = LocalizedStrings.CurrencyDescKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 0)]
    [BasicSetting]
    public string CounterCurrencies { get; set; } =
        _defaultCounterCurrencies;

    /// <summary>
    /// Interval between REST refreshes.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.IntervalKey,
        Description = LocalizedStrings.IntervalDataUpdatesKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 1)]
    [BasicSetting]
    public TimeSpan PollingInterval
    {
        get => _pollingInterval;
        set => _pollingInterval = value < TimeSpan.FromSeconds(5)
            ? TimeSpan.FromSeconds(5)
            : value;
    }

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Key), Key)
            .Set(nameof(Secret), Secret)
            .Set(nameof(RestEndpoint), RestEndpoint)
            .Set(nameof(CounterCurrencies), CounterCurrencies)
            .Set(nameof(PollingInterval), PollingInterval);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Key = storage.GetValue<SecureString>(nameof(Key));
        Secret = storage.GetValue<SecureString>(nameof(Secret));
        RestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(RestEndpoint),
            RestEndpoint));
        CounterCurrencies = storage.GetValue(nameof(CounterCurrencies),
            CounterCurrencies);
        PollingInterval = storage.GetValue(nameof(PollingInterval),
            PollingInterval);
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        endpoint = endpoint.IsEmpty() ? _defaultEndpoint : endpoint.Trim();
        if (!endpoint.Contains("://", StringComparison.Ordinal))
            endpoint = $"https://{endpoint.TrimStart('/')}";
        return endpoint.TrimEnd('/');
    }

    /// <inheritdoc />
    public override string ToString()
        => base.ToString() + $": Key={Key.ToId()}";
}
