namespace StockSharp.Uniswap;

/// <summary>
/// Chains supported by the current Uniswap Trading API.
/// </summary>
public enum UniswapChains
{
    /// <summary>Ethereum Mainnet.</summary>
    Ethereum = 1,
    /// <summary>OP Mainnet.</summary>
    Optimism = 10,
    /// <summary>BNB Smart Chain.</summary>
    BnbSmartChain = 56,
    /// <summary>Unichain.</summary>
    Unichain = 130,
    /// <summary>Polygon.</summary>
    Polygon = 137,
    /// <summary>Monad.</summary>
    Monad = 143,
    /// <summary>X Layer.</summary>
    XLayer = 196,
    /// <summary>zkSync.</summary>
    ZkSync = 324,
    /// <summary>World Chain.</summary>
    WorldChain = 480,
    /// <summary>Soneium.</summary>
    Soneium = 1868,
    /// <summary>Tempo.</summary>
    Tempo = 4217,
    /// <summary>MegaETH.</summary>
    MegaEth = 4326,
    /// <summary>Robinhood Chain.</summary>
    RobinhoodChain = 4663,
    /// <summary>Arc.</summary>
    Arc = 5042,
    /// <summary>Base.</summary>
    Base = 8453,
    /// <summary>Monad Testnet.</summary>
    MonadTestnet = 10143,
    /// <summary>Arbitrum One.</summary>
    Arbitrum = 42161,
    /// <summary>Celo.</summary>
    Celo = 42220,
    /// <summary>Avalanche C-Chain.</summary>
    Avalanche = 43114,
    /// <summary>Ink.</summary>
    Ink = 57073,
    /// <summary>Linea.</summary>
    Linea = 59144,
    /// <summary>Blast.</summary>
    Blast = 81457,
    /// <summary>Zora.</summary>
    Zora = 7777777,
    /// <summary>Unichain Sepolia.</summary>
    UnichainSepolia = 1301,
    /// <summary>Base Sepolia.</summary>
    BaseSepolia = 84532,
    /// <summary>Ethereum Sepolia.</summary>
    EthereumSepolia = 11155111,
}

/// <summary>Universal Router API versions.</summary>
public enum UniswapRouterVersions
{
    /// <summary>Version 2.0.</summary>
    Version2_0,
    /// <summary>Version 2.1.1.</summary>
    Version2_1_1,
    /// <summary>Version 2.2.0.</summary>
    Version2_2_0,
}

/// <summary>
/// The message adapter for Uniswap AMM markets.
/// </summary>
[MediaIcon(Media.MediaNames.uniswap)]
[Doc("topics/api/connectors/crypto_exchanges/uniswap.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.UniswapKey,
    Description = LocalizedStrings.CryptoConnectorKey,
    GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
    MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
    MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 |
    MessageAdapterCategories.Candles | MessageAdapterCategories.History |
    MessageAdapterCategories.Transactions)]
public partial class UniswapMessageAdapter : MessageAdapter, ITokenAdapter
{
    private const string _defaultTradingEndpoint =
        "https://trade-api.gateway.uniswap.org/v1";
    private const string _defaultV3SubgraphId =
        "5zvR82QoaXYFyDEKLZ9t6v9adgnptxYpKpSbxtgVENFV";
    private const string _defaultMarkets =
        "0x88e6A0c2dDD26FEEb64F039a2c41296FcB3f5640|" +
        "0xC02aaA39b223FE8D0A0e5C4F27eAD9083C756Cc2|" +
        "0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48;" +
        "0xcbcdf9626bc03e24f779434178a73a0b4bad62ed|" +
        "0x2260FAC5E5542a773Aa44fBCfeDf7C193bc2C599|" +
        "0xC02aaA39b223FE8D0A0e5C4F27eAD9083C756Cc2;" +
        "0x1d42064fc4beb5f8aaf85f4617ae8b3b5b8bd801|" +
        "0x1f9840a85d5aF5bf1D1762F925BDADdC4201F984|" +
        "0xC02aaA39b223FE8D0A0e5C4F27eAD9083C756Cc2";

    /// <summary>Supported candle intervals.</summary>
    public static IEnumerable<TimeSpan> AllTimeFrames =>
        UniswapExtensions.TimeFrames;

    /// <inheritdoc />
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TokenKey,
        Description = LocalizedStrings.TokenKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 0)]
    [BasicSetting]
    public SecureString Token { get; set; }

    /// <summary>Optional The Graph gateway API key.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.KeyKey,
        Description = LocalizedStrings.KeyKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 1)]
    [BasicSetting]
    public SecureString GraphApiKey { get; set; }

    /// <summary>Public wallet address used for quote simulation and balances.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.WalletAddressKey,
        Description = LocalizedStrings.WalletAddressKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 2)]
    [BasicSetting]
    public string WalletAddress { get; set; }

    /// <summary>Optional private key used to sign on-chain transactions.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PrivateKey,
        Description = LocalizedStrings.PrivateKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 3)]
    [BasicSetting]
    public SecureString PrivateKey { get; set; }

    /// <summary>EVM chain.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.BoardKey,
        Description = LocalizedStrings.BoardKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 4)]
    [BasicSetting]
    public UniswapChains Chain { get; set; } = UniswapChains.Ethereum;

    /// <summary>Universal Router version used for quote and swap requests.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.VersionKey,
        Description = LocalizedStrings.VersionKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 5)]
    [BasicSetting]
    public UniswapRouterVersions RouterVersion { get; set; } =
        UniswapRouterVersions.Version2_0;

    /// <summary>HTTP JSON-RPC endpoint for the selected chain.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = LocalizedStrings.ServerAddressKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 0)]
    [BasicSetting]
    public string RpcEndpoint { get; set; }

    /// <summary>Uniswap Trading API endpoint.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AddressKey,
        Description = LocalizedStrings.ServerAddressKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 1)]
    [BasicSetting]
    public string TradingEndpoint { get; set; } = _defaultTradingEndpoint;

    /// <summary>The Graph subgraph deployment identifier.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.IdKey,
        Description = LocalizedStrings.IdKey,
        GroupName = LocalizedStrings.AddressesKey,
        Order = 2)]
    public string SubgraphId { get; set; } = _defaultV3SubgraphId;

    /// <summary>
    /// Semicolon-separated <c>pool|base token|quote token</c> definitions.
    /// </summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SecuritiesKey,
        Description = LocalizedStrings.SecuritiesKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 6)]
    public string Markets { get; set; } = _defaultMarkets;

    private int _maximumDiscoveredPools = 100;

    /// <summary>Maximum number of top v3 pools discovered through The Graph.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.CountKey,
        Description = LocalizedStrings.CountKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 7)]
    public int MaximumDiscoveredPools
    {
        get => _maximumDiscoveredPools;
        set => _maximumDiscoveredPools = value is >= 1 and <= 1000
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value,
                "Pool discovery limit must be between 1 and 1000.");
    }

    private decimal _probeVolume = 1m;

    /// <summary>Base-token amount used for bid and ask quote probes.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.VolumeKey,
        Description = LocalizedStrings.VolumeKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 8)]
    public decimal ProbeVolume
    {
        get => _probeVolume;
        set => _probeVolume = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value,
                "Quote probe volume must be positive.");
    }

    private decimal _slippageTolerance = 0.5m;

    /// <summary>Swap slippage tolerance in percent.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SlippageKey,
        Description = LocalizedStrings.SlippageKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 9)]
    public decimal SlippageTolerance
    {
        get => _slippageTolerance;
        set => _slippageTolerance = value is > 0 and <= 50 &&
            decimal.Round(value, 2) == value
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value,
                "Slippage tolerance must be greater than zero and no more " +
                "than 50 percent, with at most two decimal places.");
    }

    private TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

    /// <summary>Polling interval for quotes, subgraph data and receipts.</summary>
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.IntervalKey,
        Description = LocalizedStrings.IntervalKey,
        GroupName = LocalizedStrings.ConnectionKey,
        Order = 10)]
    public TimeSpan PollingInterval
    {
        get => _pollingInterval;
        set => _pollingInterval = value >= TimeSpan.FromSeconds(1)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value,
                "Polling interval cannot be less than one second.");
    }

    /// <inheritdoc />
    public override void Save(SettingsStorage storage)
    {
        base.Save(storage);
        storage
            .Set(nameof(Token), Token)
            .Set(nameof(GraphApiKey), GraphApiKey)
            .Set(nameof(WalletAddress), WalletAddress)
            .Set(nameof(PrivateKey), PrivateKey)
            .Set(nameof(Chain), Chain)
            .Set(nameof(RouterVersion), RouterVersion)
            .Set(nameof(RpcEndpoint), RpcEndpoint)
            .Set(nameof(TradingEndpoint), TradingEndpoint)
            .Set(nameof(SubgraphId), SubgraphId)
            .Set(nameof(Markets), Markets)
            .Set(nameof(MaximumDiscoveredPools), MaximumDiscoveredPools)
            .Set(nameof(ProbeVolume), ProbeVolume)
            .Set(nameof(SlippageTolerance), SlippageTolerance)
            .Set(nameof(PollingInterval), PollingInterval);
    }

    /// <inheritdoc />
    public override void Load(SettingsStorage storage)
    {
        base.Load(storage);
        Token = storage.GetValue<SecureString>(nameof(Token));
        GraphApiKey = storage.GetValue<SecureString>(nameof(GraphApiKey));
        WalletAddress = storage.GetValue<string>(nameof(WalletAddress));
        PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
        Chain = storage.GetValue(nameof(Chain), Chain);
        RouterVersion = storage.GetValue(nameof(RouterVersion),
            RouterVersion);
        RpcEndpoint = NormalizeEndpoint(storage.GetValue(
            nameof(RpcEndpoint), RpcEndpoint), null);
        TradingEndpoint = NormalizeEndpoint(storage.GetValue(
            nameof(TradingEndpoint), TradingEndpoint),
            _defaultTradingEndpoint);
        SubgraphId = storage.GetValue(nameof(SubgraphId), SubgraphId);
        Markets = storage.GetValue(nameof(Markets), Markets);
        MaximumDiscoveredPools = storage.GetValue(
            nameof(MaximumDiscoveredPools), MaximumDiscoveredPools);
        ProbeVolume = storage.GetValue(nameof(ProbeVolume), ProbeVolume);
        SlippageTolerance = storage.GetValue(nameof(SlippageTolerance),
            SlippageTolerance);
        PollingInterval = storage.GetValue(nameof(PollingInterval),
            PollingInterval);
    }

    private static string NormalizeEndpoint(string endpoint, string fallback)
    {
        endpoint = endpoint.IsEmpty() ? fallback : endpoint.Trim();
        if (endpoint.IsEmpty())
            return endpoint;
        if (!endpoint.Contains("://", StringComparison.Ordinal))
            endpoint = $"https://{endpoint.TrimStart('/')}";
        return endpoint.TrimEnd('/');
    }

    /// <inheritdoc />
    public override string ToString()
        => base.ToString() + $": Chain={Chain}, Wallet={WalletAddress}";
}
