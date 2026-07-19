namespace StockSharp.PancakeSwap;

public partial class PancakeSwapMessageAdapter
{
    private const int _maximumDeliveryKeys = 100_000;

    private sealed class Level1Subscription
    {
        public PancakeSwapMarket Market { get; init; }
    }

    private sealed class TickSubscription
    {
        public PancakeSwapMarket Market { get; init; }
        public DateTime? From { get; init; }
        public DateTime? To { get; init; }
        public DateTime LastTime { get; set; }
        public int Maximum { get; init; }
        public int Delivered { get; set; }
    }

    private sealed class CandleSubscription
    {
        public PancakeSwapMarket Market { get; init; }
        public TimeSpan TimeFrame { get; init; }
        public DateTime? To { get; init; }
        public DateTime LastTime { get; set; }
        public int Maximum { get; init; }
        public int Delivered { get; set; }
    }

    private sealed class TrackedSwap
    {
        public long TransactionId { get; init; }
        public string TransactionHash { get; init; }
        public PancakeSwapMarket Market { get; init; }
        public Sides Side { get; init; }
        public decimal Volume { get; set; }
        public decimal Price { get; set; }
        public DateTime SubmittedTime { get; init; }
        public OrderStates State { get; set; }
        public bool IsTradeSent { get; set; }
        public PancakeSwapRpcReceipt Receipt { get; set; }
    }

    private sealed class OrderSubscription
    {
        public string TransactionHash { get; init; }
        public SecurityId SecurityId { get; init; }
        public Sides? Side { get; init; }
        public decimal? Volume { get; init; }
        public OrderStates[] States { get; init; }
        public DateTime? From { get; init; }
        public DateTime? To { get; init; }
        public int Skip { get; init; }
        public int Maximum { get; init; }
    }

    private readonly record struct DeliveryKey(long SubscriptionId,
        string Identity);
    private readonly record struct CandleFingerprint(decimal Open,
        decimal High, decimal Low, decimal Close, decimal Volume,
        int TradeCount);
    private readonly record struct BalanceFingerprint(decimal Current,
        decimal Blocked);
    private readonly record struct OrderFingerprint(OrderStates State,
        bool IsTradeSent);
    private readonly record struct SwapExecution(decimal Price,
        decimal Volume);

    private readonly Lock _sync = new();
    private readonly Dictionary<string, PancakeSwapMarket> _markets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PancakeSwapToken> _tokens =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, Level1Subscription>
        _level1Subscriptions = [];
    private readonly Dictionary<long, TickSubscription> _tickSubscriptions =
        [];
    private readonly Dictionary<long, CandleSubscription>
        _candleSubscriptions = [];
    private readonly HashSet<DeliveryKey> _seenTrades = [];
    private readonly Queue<DeliveryKey> _tradeDeliveryOrder = [];
    private readonly Dictionary<string, CandleFingerprint>
        _candleFingerprints = new(StringComparer.Ordinal);
    private readonly HashSet<long> _portfolioSubscriptions = [];
    private readonly Dictionary<long, OrderSubscription> _orderSubscriptions =
        [];
    private readonly Dictionary<string, TrackedSwap> _trackedSwaps =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BalanceFingerprint>
        _balanceFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OrderFingerprint>
        _orderFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<PancakeSwapPoolVersions,
        PancakeSwapGraphClient> _graphClients = [];
    private PancakeSwapRpcClient _rpcClient;
    private DateTime _nextMarketPoll;
    private DateTime _nextPrivatePoll;

    /// <summary>Initializes the adapter.</summary>
    public PancakeSwapMessageAdapter(IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        HeartbeatInterval = TimeSpan.FromSeconds(1);
        this.AddMarketDataSupport();
        this.AddTransactionalSupport();
        this.AddSupportedMarketDataType(DataType.Ticks);
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedCandleTimeFrames(AllTimeFrames);
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities ||
            dataType == DataType.PositionChanges ||
            base.IsAllDownloadingSupported(dataType);

    /// <inheritdoc />
    public override bool IsSupportCandlesUpdates(MarketDataMessage subscription)
        => true;

    /// <inheritdoc />
    public override string[] AssociatedBoards => [BoardCodes.PancakeSwap];

    /// <inheritdoc />
    protected override bool ValidateSecurityId(SecurityId securityId)
        => securityId.BoardCode.IsEmpty() ||
            securityId.BoardCode.EqualsIgnoreCase(BoardCodes.PancakeSwap) ||
            securityId.IsAssociated(BoardCodes.PancakeSwap);

    private PancakeSwapRpcClient RpcClient => _rpcClient ?? throw new
        InvalidOperationException(LocalizedStrings.ConnectionNotOk);

    private PancakeSwapGraphClient GetGraphClient(
        PancakeSwapPoolVersions poolVersion)
    {
        using (_sync.EnterScope())
            return _graphClients.TryGetValue(poolVersion, out var client)
                ? client
                : throw new InvalidOperationException(
                    $"A compatible PancakeSwap {poolVersion} subgraph is " +
                    "required for swaps and candles.");
    }

    private void EnsureConnected()
    {
        if (_rpcClient is null)
            throw new InvalidOperationException(
                LocalizedStrings.ConnectionNotOk);
    }

    private void EnsureTradingReady()
    {
        EnsureConnected();
        if (!RpcClient.IsSigningAvailable)
            throw new InvalidOperationException(
                "An EVM private key is required for PancakeSwap swaps.");
    }

    private PancakeSwapMarket GetMarket(SecurityId securityId)
    {
        if (!ValidateSecurityId(securityId))
            throw new InvalidOperationException(
                $"Security board '{securityId.BoardCode}' is not PancakeSwap.");
        var code = securityId.SecurityCode.ThrowIfEmpty(
            nameof(securityId)).Trim().ToUpperInvariant();
        using (_sync.EnterScope())
            return _markets.TryGetValue(code, out var market)
                ? market
                : throw new InvalidOperationException(
                    $"Unknown PancakeSwap market '{code}'.");
    }

    private string GetPortfolioName()
        => $"PancakeSwap_{Chain}_{RpcClient.WalletAddress[2..10]}";

    private void ValidatePortfolio(string portfolioName)
    {
        if (!portfolioName.IsEmpty() &&
            !portfolioName.EqualsIgnoreCase(GetPortfolioName()))
            throw new InvalidOperationException(
                $"Unknown PancakeSwap portfolio '{portfolioName}'.");
    }

    /// <inheritdoc />
    protected override void DisposeManaged()
    {
        DisposeClients();
        base.DisposeManaged();
    }
}
