namespace StockSharp.CowProtocol;

/// <summary>Supported CoW Protocol production networks.</summary>
public enum CowProtocolChains
{
    /// <summary>Ethereum Mainnet.</summary>
    Ethereum = 1,
    /// <summary>BNB Smart Chain.</summary>
    Bnb = 56,
    /// <summary>Gnosis Chain.</summary>
    Gnosis = 100,
    /// <summary>Polygon PoS.</summary>
    Polygon = 137,
    /// <summary>Base.</summary>
    Base = 8453,
    /// <summary>Arbitrum One.</summary>
    Arbitrum = 42161,
    /// <summary>Avalanche C-Chain.</summary>
    Avalanche = 43114,
}

/// <summary>The message adapter for CoW Protocol batch auctions.</summary>
[MediaIcon(Media.MediaNames.cow_protocol)]
[Doc("topics/api/connectors/crypto_exchanges/cow_protocol.html")]
[Display(ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.CowProtocolKey,
    Description = LocalizedStrings.CryptoConnectorKey,
    GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
    MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
    MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
    MessageAdapterCategories.Candles | MessageAdapterCategories.History |
    MessageAdapterCategories.Transactions)]
public partial class CowProtocolMessageAdapter : MessageAdapter
{
    /// <summary>Supported candle intervals.</summary>
    public static IEnumerable<TimeSpan> AllTimeFrames =>
        CowProtocolExtensions.TimeFrames;

    /// <summary>Production network.</summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.BoardKey,
        Description = LocalizedStrings.BoardKey,
        GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
    [BasicSetting]
    public CowProtocolChains Chain { get; set; } =
        CowProtocolChains.Ethereum;

    /// <summary>Public wallet address used for quotes and balances.</summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.WalletAddressKey,
        Description = LocalizedStrings.WalletAddressKey,
        GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
    [BasicSetting]
    public string WalletAddress { get; set; }

    /// <summary>Optional private key used to sign orders and approvals.</summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PrivateKey,
        Description = LocalizedStrings.PrivateKey,
        GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
    [BasicSetting]
    public SecureString PrivateKey { get; set; }

    /// <summary>Optional custom CoW Protocol Order Book API endpoint.</summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = LocalizedStrings.ServerAddressKey,
        GroupName = LocalizedStrings.AddressesKey, Order = 0)]
    [BasicSetting]
    public string ApiEndpoint { get; set; }

    /// <summary>Optional custom EVM HTTP JSON-RPC endpoint.</summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = LocalizedStrings.ServerAddressKey,
        GroupName = LocalizedStrings.AddressesKey, Order = 1)]
    [BasicSetting]
    public string RpcEndpoint { get; set; }

    /// <summary>
    /// Semicolon-separated market definitions in
    /// base-token|quote-token|security-code format. The security code is
    /// optional.
    /// </summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SecuritiesKey,
        Description = LocalizedStrings.SecuritiesKey,
        GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
    public string Markets { get; set; }

    private int _historyBlockRange = 2_000;

    /// <summary>Maximum block range requested by one log query.</summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.CountKey,
        Description = LocalizedStrings.CountKey,
        GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
    public int HistoryBlockRange
    {
        get => _historyBlockRange;
        set => _historyBlockRange = value is >= 1 and <= 50_000
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value,
                "History block range must be between 1 and 50000.");
    }

    private int _historyBlockCount = 50_000;

    /// <summary>
    /// Number of recent blocks searched when history has no start time.
    /// </summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.CountKey,
        Description = LocalizedStrings.CountKey,
        GroupName = LocalizedStrings.ConnectionKey, Order = 5)]
    public int HistoryBlockCount
    {
        get => _historyBlockCount;
        set => _historyBlockCount = value is >= 1 and <= 10_000_000
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value,
                "History block count must be between 1 and 10000000.");
    }

    private decimal _probeVolume = 0.01m;

    /// <summary>Base-token amount used for bid and ask quote probes.</summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.VolumeKey,
        Description = LocalizedStrings.VolumeKey,
        GroupName = LocalizedStrings.ConnectionKey, Order = 6)]
    public decimal ProbeVolume
    {
        get => _probeVolume;
        set => _probeVolume = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value,
                "Quote probe volume must be positive.");
    }

    private TimeSpan _orderValidity = TimeSpan.FromMinutes(5);

    /// <summary>Default lifetime for submitted orders.</summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TimeKey,
        Description = LocalizedStrings.TimeKey,
        GroupName = LocalizedStrings.ConnectionKey, Order = 7)]
    public TimeSpan OrderValidity
    {
        get => _orderValidity;
        set => _orderValidity = value >= TimeSpan.FromMinutes(1) &&
            value <= TimeSpan.FromDays(1)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value,
                "Order validity must be between one minute and one day.");
    }

    private TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

    /// <summary>Polling interval for quotes, trades, balances, and orders.</summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.IntervalKey,
        Description = LocalizedStrings.IntervalKey,
        GroupName = LocalizedStrings.ConnectionKey, Order = 8)]
    public TimeSpan PollingInterval
    {
        get => _pollingInterval;
        set => _pollingInterval = value >= TimeSpan.FromSeconds(1)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value,
                "Polling interval cannot be less than one second.");
    }

    /// <summary>Automatically approve the VaultRelayer when required.</summary>
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AutoKey,
        Description = LocalizedStrings.AutoKey,
        GroupName = LocalizedStrings.ConnectionKey, Order = 9)]
    public bool IsAutoApprove { get; set; } = true;

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Chain), Chain)
            .Set(nameof(WalletAddress), WalletAddress)
            .Set(nameof(PrivateKey), PrivateKey)
            .Set(nameof(ApiEndpoint), ApiEndpoint)
            .Set(nameof(RpcEndpoint), RpcEndpoint)
            .Set(nameof(Markets), Markets)
            .Set(nameof(HistoryBlockRange), HistoryBlockRange)
            .Set(nameof(HistoryBlockCount), HistoryBlockCount)
            .Set(nameof(ProbeVolume), ProbeVolume)
            .Set(nameof(OrderValidity), OrderValidity)
            .Set(nameof(PollingInterval), PollingInterval)
            .Set(nameof(IsAutoApprove), IsAutoApprove);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Chain = storage.GetValue(nameof(Chain), Chain);
        if (!System.Enum.IsDefined(Chain))
            throw new InvalidDataException(
                $"Unsupported CoW Protocol chain '{Chain}'.");
        WalletAddress = storage.GetValue<string>(nameof(WalletAddress));
        PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
        ApiEndpoint = NormalizeEndpoint(storage.GetValue<string>(
            nameof(ApiEndpoint)));
        RpcEndpoint = NormalizeEndpoint(storage.GetValue<string>(
            nameof(RpcEndpoint)));
        Markets = storage.GetValue<string>(nameof(Markets));
        HistoryBlockRange = storage.GetValue(nameof(HistoryBlockRange),
            HistoryBlockRange);
        HistoryBlockCount = storage.GetValue(nameof(HistoryBlockCount),
            HistoryBlockCount);
        ProbeVolume = storage.GetValue(nameof(ProbeVolume), ProbeVolume);
        OrderValidity = storage.GetValue(nameof(OrderValidity), OrderValidity);
        PollingInterval = storage.GetValue(nameof(PollingInterval),
            PollingInterval);
        IsAutoApprove = storage.GetValue(nameof(IsAutoApprove),
            IsAutoApprove);
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        endpoint = endpoint?.Trim();
        if (endpoint.IsEmpty())
            return null;
        if (!endpoint.Contains("://", StringComparison.Ordinal))
            endpoint = $"https://{endpoint.TrimStart('/')}";
        return endpoint.TrimEnd('/');
    }

    /// <inheritdoc />
    public override string ToString()
        => base.ToString() + $": {Chain}, Wallet={WalletAddress}";
}
