namespace StockSharp.Zoomex;

public partial class ZoomexMessageAdapter
{
    private const int _maximumDeliveryKeys = 100_000;

    private class MarketSubscription
    {
        public ZoomexCategories Category { get; init; }
        public string Symbol { get; init; }
    }

    private sealed class DepthSubscription : MarketSubscription
    {
        public int Depth { get; init; }
        public string Topic { get; init; }
    }

    private sealed class CandleSubscription : MarketSubscription
    {
        public TimeSpan TimeFrame { get; init; }
    }

    private sealed class OrderSubscription
    {
        public ZoomexCategories[] Categories { get; init; }
        public string Symbol { get; init; }
        public string OrderId { get; init; }
        public string OrderLinkId { get; init; }
        public Sides? Side { get; init; }
        public DateTime? From { get; init; }
        public DateTime? To { get; init; }
        public int Maximum { get; init; }
    }

    private sealed class TrackedOrder
    {
        public long TransactionId { get; init; }
        public ZoomexCategories Category { get; init; }
        public string Symbol { get; init; }
        public string OrderId { get; set; }
        public string OrderLinkId { get; init; }
        public Sides Side { get; init; }
        public OrderTypes OrderType { get; init; }
        public decimal Volume { get; init; }
        public decimal Price { get; init; }
        public ZoomexOrderCondition Condition { get; init; }
        public OrderStates State { get; set; }
    }

    private sealed class OrderBookState
    {
        public SortedDictionary<decimal, decimal> Bids { get; } =
            new(Comparer<decimal>.Create(static (left, right) =>
                right.CompareTo(left)));
        public SortedDictionary<decimal, decimal> Asks { get; } = [];
        public long Sequence { get; set; }
    }

    private readonly record struct ProductKey(ZoomexCategories Category,
        string Symbol);
    private readonly record struct TradeDeliveryKey(long SubscriptionId,
        string TradeId);
    private readonly record struct ExecutionDeliveryKey(long SubscriptionId,
        string ExecutionId);
    private readonly record struct OrderFingerprint(ZoomexOrderStatuses Status,
        decimal? Filled, decimal? Balance, decimal? Price, long UpdateTime);
    private readonly record struct BalanceFingerprint(decimal Current,
        decimal Blocked, decimal? Unrealized, decimal? Realized);
    private readonly record struct PositionFingerprint(decimal Current,
        decimal? Average, decimal? Mark, decimal? Unrealized,
        decimal? Liquidation);

    private readonly Lock _sync = new();
    private readonly Dictionary<ProductKey, ZoomexProduct> _products = [];
    private readonly Dictionary<long, MarketSubscription>
        _level1Subscriptions = [];
    private readonly Dictionary<long, MarketSubscription>
        _tickSubscriptions = [];
    private readonly Dictionary<long, DepthSubscription>
        _depthSubscriptions = [];
    private readonly Dictionary<long, CandleSubscription>
        _candleSubscriptions = [];
    private readonly Dictionary<string, OrderBookState> _books =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<long> _portfolioSubscriptions = [];
    private readonly Dictionary<long, OrderSubscription>
        _orderSubscriptions = [];
    private readonly Dictionary<string, TrackedOrder> _trackedOrders =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<TradeDeliveryKey> _seenPublicTrades = [];
    private readonly Queue<TradeDeliveryKey> _publicTradeDeliveryOrder = [];
    private readonly HashSet<ExecutionDeliveryKey> _seenExecutions = [];
    private readonly Queue<ExecutionDeliveryKey> _executionDeliveryOrder = [];
    private readonly Dictionary<string, OrderFingerprint> _orderFingerprints =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BalanceFingerprint>
        _balanceFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PositionFingerprint>
        _positionFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ZoomexCategories, ZoomexSocketClient>
        _publicSockets = [];
    private ZoomexRestClient _restClient;
    private ZoomexSocketClient _privateSocket;
    private DateTime _nextPing;

    /// <summary>
    /// Initializes a new instance of the <see cref="ZoomexMessageAdapter"/>.
    /// </summary>
    public ZoomexMessageAdapter(IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        HeartbeatInterval = TimeSpan.FromSeconds(1);
        this.AddMarketDataSupport();
        this.AddTransactionalSupport();
        this.AddSupportedMarketDataType(DataType.Ticks);
        this.AddSupportedMarketDataType(DataType.MarketDepth);
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedCandleTimeFrames(AllTimeFrames);
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities ||
            dataType == DataType.Transactions ||
            dataType == DataType.PositionChanges ||
            base.IsAllDownloadingSupported(dataType);

    /// <inheritdoc />
    public override bool IsSupportCandlesUpdates(MarketDataMessage subscription)
        => true;

    /// <inheritdoc />
    public override bool IsSupportTransactionLog => true;

    /// <inheritdoc />
    public override IEnumerable<int> SupportedOrderBookDepths { get; } =
        [1, 50, 200, 1000];

    /// <inheritdoc />
    public override string[] AssociatedBoards =>
    [
        BoardCodes.Zoomex,
        BoardCodes.ZoomexLinear,
        BoardCodes.ZoomexInverse,
    ];

    /// <inheritdoc />
    protected override bool ValidateSecurityId(SecurityId securityId)
        => securityId.BoardCode.IsEmpty() ||
            AssociatedBoards.Any(board =>
                securityId.BoardCode.EqualsIgnoreCase(board) ||
                securityId.IsAssociated(board));

    private ZoomexRestClient RestClient => _restClient ?? throw new
        InvalidOperationException(LocalizedStrings.ConnectionNotOk);

    private void EnsureConnected()
    {
        if (_restClient is null || _publicSockets.Count == 0)
            throw new InvalidOperationException(
                LocalizedStrings.ConnectionNotOk);
    }

    private void EnsurePrivateReady()
    {
        EnsureConnected();
        if (!RestClient.IsPrivateAvailable || _privateSocket is null)
            throw new InvalidOperationException(
                "Zoomex API key and secret are required for private operations.");
    }

    private ZoomexSocketClient GetSocket(ZoomexCategories category)
    {
        using (_sync.EnterScope())
            return _publicSockets.TryGetValue(category, out var socket)
                ? socket
                : throw new InvalidOperationException(
                    $"Zoomex section '{category}' is disabled.");
    }

    private ZoomexProduct GetProduct(SecurityId securityId)
    {
        if (!ValidateSecurityId(securityId))
            throw new InvalidOperationException(
                $"Security board '{securityId.BoardCode}' is not Zoomex.");
        var symbol = securityId.SecurityCode.NormalizeSymbol();
        using (_sync.EnterScope())
        {
            if (!securityId.BoardCode.IsEmpty())
            {
                var category = securityId.BoardCode.ToZoomexCategories();
                if (_products.TryGetValue(new(category, symbol),
                    out var product))
                    return product;
            }
            var matches = _products.Where(pair =>
                pair.Key.Symbol.EqualsIgnoreCase(symbol)).Select(
                static pair => pair.Value).Take(2).ToArray();
            return matches.Length switch
            {
                1 => matches[0],
                0 => throw new InvalidOperationException(
                    $"Unknown Zoomex instrument '{symbol}'."),
                _ => throw new InvalidOperationException(
                    $"Zoomex instrument '{symbol}' exists in multiple " +
                    "sections; specify its board code."),
            };
        }
    }

    private string GetPortfolioName()
        => $"Zoomex_{Key.ToId()}_{AccountType}";

    private void ValidatePortfolio(string portfolioName)
    {
        if (!portfolioName.IsEmpty() &&
            !portfolioName.EqualsIgnoreCase(GetPortfolioName()))
            throw new InvalidOperationException(
                $"Unknown Zoomex portfolio '{portfolioName}'.");
    }

    private static int NormalizeDepth(int? requested)
    {
        var depth = requested ?? 50;
        return depth <= 1 ? 1 : depth <= 50 ? 50
            : depth <= 200 ? 200 : 1000;
    }

    private static bool TryRemember<T>(HashSet<T> values, Queue<T> order,
        T value)
    {
        if (!values.Add(value))
            return false;
        order.Enqueue(value);
        while (order.Count > _maximumDeliveryKeys)
            values.Remove(order.Dequeue());
        return true;
    }

    /// <inheritdoc />
    protected override void DisposeManaged()
    {
        DisposeClients();
        base.DisposeManaged();
    }
}
