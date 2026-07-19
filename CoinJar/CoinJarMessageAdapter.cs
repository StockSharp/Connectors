namespace StockSharp.CoinJar;

public partial class CoinJarMessageAdapter
{
    private class MarketSubscription
    {
        public string ProductId { get; init; }
    }

    private sealed class DepthSubscription : MarketSubscription
    {
        public int Depth { get; init; }
        public Dictionary<decimal, decimal> Bids { get; } = [];
        public Dictionary<decimal, decimal> Asks { get; } = [];
        public bool IsInitialized { get; set; }
    }

    private sealed class CandleSubscription : MarketSubscription
    {
        public TimeSpan TimeFrame { get; init; }
        public CoinJarCandle Current { get; set; }
    }

    private sealed class OrderSubscription
    {
        public long? OrderId { get; init; }
        public string ProductId { get; init; }
        public Sides? Side { get; init; }
    }

    private sealed class TrackedOrder
    {
        public long TransactionId { get; init; }
        public long ExchangeOrderId { get; set; }
        public string ProductId { get; init; }
        public string Reference { get; init; }
        public Sides Side { get; init; }
        public OrderTypes OrderType { get; init; }
        public decimal Volume { get; init; }
        public decimal Price { get; init; }
        public bool IsPostOnly { get; init; }
        public CoinJarOrderCondition Condition { get; init; }
    }

    private sealed class BookState
    {
        public SortedDictionary<decimal, decimal> Bids { get; } =
            new(Comparer<decimal>.Create(static (left, right) =>
                right.CompareTo(left)));
        public SortedDictionary<decimal, decimal> Asks { get; } = [];
        public bool IsInitialized { get; set; }
        public bool IsSnapshotRequested { get; set; }
    }

    private readonly record struct StreamKey(CoinJarSocketTopics Topic,
        string ProductId);
    private readonly record struct BalanceFingerprint(decimal Balance,
        decimal Available, decimal Hold);
    private readonly record struct OrderFingerprint(CoinJarOrderStatuses Status,
        decimal Filled, decimal Size, decimal? Price);

    private readonly Lock _sync = new();
    private readonly Dictionary<string, CoinJarProduct> _products =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, MarketSubscription> _level1Subscriptions =
        [];
    private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
    private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
    private readonly Dictionary<long, CandleSubscription> _candleSubscriptions =
        [];
    private readonly Dictionary<StreamKey, int> _streamReferences = [];
    private readonly Dictionary<string, BookState> _books =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, TrackedOrder> _trackedOrders = [];
    private readonly Dictionary<string, TrackedOrder> _trackedOrderReferences =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenPublicTrades =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenAccountTrades =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<long> _portfolioSubscriptions = [];
    private readonly Dictionary<long, OrderSubscription> _orderSubscriptions = [];
    private readonly Dictionary<string, BalanceFingerprint>
        _balanceFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OrderFingerprint> _orderFingerprints =
        new(StringComparer.OrdinalIgnoreCase);
    private int _privateReferences;
    private CoinJarRestClient _restClient;
    private CoinJarSocketClient _socketClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="CoinJarMessageAdapter"/>
    /// class.
    /// </summary>
    public CoinJarMessageAdapter(IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        HeartbeatInterval = TimeSpan.FromSeconds(10);
        this.AddMarketDataSupport();
        this.AddTransactionalSupport();
        this.AddSupportedMarketDataType(DataType.Ticks);
        this.AddSupportedMarketDataType(DataType.MarketDepth);
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedCandleTimeFrames(AllTimeFrames);
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities || dataType == DataType.Transactions ||
            dataType == DataType.PositionChanges ||
            base.IsAllDownloadingSupported(dataType);

    /// <inheritdoc />
    public override bool IsSupportOrderBookIncrements => true;

    /// <inheritdoc />
    public override bool IsReplaceCommandEditCurrent => false;

    /// <inheritdoc />
    public override bool IsSupportTransactionLog => true;

    /// <inheritdoc />
    public override string[] AssociatedBoards => [BoardCodes.CoinJar];

    /// <inheritdoc />
    protected override bool ValidateSecurityId(SecurityId securityId)
        => securityId.BoardCode.IsEmpty() ||
            securityId.BoardCode.EqualsIgnoreCase(BoardCodes.CoinJar) ||
            securityId.IsAssociated(BoardCodes.CoinJar);

    private CoinJarRestClient RestClient
        => _restClient ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private CoinJarSocketClient SocketClient
        => _socketClient ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private void EnsureConnected()
    {
        if (_restClient is null || _socketClient is null)
            throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
    }

    private void EnsurePrivateReady()
    {
        EnsureConnected();
        if (!RestClient.IsCredentialsAvailable)
            throw new InvalidOperationException(
                "A CoinJar Exchange API token is required for private operations.");
    }

    private string GetPortfolioName() => $"CoinJar_{Token.ToId()}";

    private void ValidatePortfolio(string portfolioName)
    {
        if (!portfolioName.IsEmpty() &&
            !portfolioName.EqualsIgnoreCase(GetPortfolioName()))
            throw new InvalidOperationException(
                $"Unknown CoinJar portfolio '{portfolioName}'.");
    }

    private void RegisterProducts(IEnumerable<CoinJarProduct> products)
    {
        using (_sync.EnterScope())
        {
            _products.Clear();
            foreach (var product in products ?? [])
                if (product?.Id.IsEmpty() == false &&
                    product.BaseCurrency?.Code.IsEmpty() == false &&
                    product.CounterCurrency?.Code.IsEmpty() == false)
                    _products[product.Id.NormalizeProduct()] = product;
        }
    }

    private CoinJarProduct GetProduct(SecurityId securityId)
    {
        if (!securityId.BoardCode.IsEmpty() &&
            !securityId.BoardCode.EqualsIgnoreCase(BoardCodes.CoinJar) &&
            !securityId.IsAssociated(BoardCodes.CoinJar))
            throw new InvalidOperationException(
                $"Security board '{securityId.BoardCode}' is not CoinJar.");
        return GetProduct(securityId.SecurityCode);
    }

    private CoinJarProduct GetProduct(string productId)
    {
        productId = productId.NormalizeProduct();
        using (_sync.EnterScope())
            return _products.TryGetValue(productId, out var product)
                ? product
                : throw new InvalidOperationException(
                    $"Unknown CoinJar product '{productId}'.");
    }

    private async ValueTask AcquireStreamAsync(CoinJarSocketTopics topic,
        string productId, CancellationToken cancellationToken)
    {
        var key = new StreamKey(topic, productId.NormalizeProduct());
        using (_sync.EnterScope())
        {
            if (_streamReferences.TryGetValue(key, out var count))
            {
                _streamReferences[key] = count + 1;
                return;
            }
            _streamReferences.Add(key, 1);
        }
        try
        {
            await SocketClient.SubscribeAsync(topic, key.ProductId,
                cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _streamReferences.Remove(key);
            throw;
        }
    }

    private async ValueTask ReleaseStreamAsync(CoinJarSocketTopics topic,
        string productId, CancellationToken cancellationToken)
    {
        var key = new StreamKey(topic, productId.NormalizeProduct());
        var unsubscribe = false;
        using (_sync.EnterScope())
        {
            if (!_streamReferences.TryGetValue(key, out var count))
                return;
            if (count > 1)
                _streamReferences[key] = count - 1;
            else
            {
                _streamReferences.Remove(key);
                unsubscribe = true;
            }
        }
        if (unsubscribe && _socketClient is not null)
            await _socketClient.UnsubscribeAsync(topic, key.ProductId,
                cancellationToken);
    }

    private async ValueTask AcquirePrivateAsync(
        CancellationToken cancellationToken)
    {
        using (_sync.EnterScope())
            if (_privateReferences++ > 0)
                return;
        try
        {
            await SocketClient.SubscribePrivateAsync(cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _privateReferences = 0;
            throw;
        }
    }

    private async ValueTask ReleasePrivateAsync(
        CancellationToken cancellationToken)
    {
        var unsubscribe = false;
        using (_sync.EnterScope())
        {
            if (_privateReferences <= 0)
                return;
            unsubscribe = --_privateReferences == 0;
        }
        if (unsubscribe && _socketClient is not null)
            await _socketClient.UnsubscribePrivateAsync(cancellationToken);
    }

    private void TrackOrder(TrackedOrder order)
    {
        if (order is null)
            return;
        using (_sync.EnterScope())
        {
            if (order.ExchangeOrderId > 0)
                _trackedOrders[order.ExchangeOrderId] = order;
            if (!order.Reference.IsEmpty())
                _trackedOrderReferences[order.Reference] = order;
        }
    }

    private TrackedOrder GetTrackedOrder(long orderId, string reference = null)
    {
        using (_sync.EnterScope())
        {
            if (orderId > 0 && _trackedOrders.TryGetValue(orderId, out var order))
                return order;
            return !reference.IsEmpty() &&
                _trackedOrderReferences.TryGetValue(reference, out order)
                    ? order
                    : null;
        }
    }

    private bool AddPublicTrade(long tradeId, long transactionId)
    {
        if (tradeId <= 0)
            return false;
        using (_sync.EnterScope())
        {
            if (_seenPublicTrades.Count > 100000)
                _seenPublicTrades.Clear();
            return _seenPublicTrades.Add($"{transactionId}:{tradeId}");
        }
    }

    private bool AddAccountTrade(long tradeId, long transactionId)
    {
        if (tradeId <= 0)
            return false;
        using (_sync.EnterScope())
        {
            if (_seenAccountTrades.Count > 100000)
                _seenAccountTrades.Clear();
            return _seenAccountTrades.Add($"{transactionId}:{tradeId}");
        }
    }

    private void ClearState()
    {
        using (_sync.EnterScope())
        {
            _products.Clear();
            _level1Subscriptions.Clear();
            _depthSubscriptions.Clear();
            _tickSubscriptions.Clear();
            _candleSubscriptions.Clear();
            _streamReferences.Clear();
            _books.Clear();
            _trackedOrders.Clear();
            _trackedOrderReferences.Clear();
            _seenPublicTrades.Clear();
            _seenAccountTrades.Clear();
            _portfolioSubscriptions.Clear();
            _orderSubscriptions.Clear();
            _balanceFingerprints.Clear();
            _orderFingerprints.Clear();
            _privateReferences = 0;
        }
    }

    private static long ResolveOrderId(long? numericOrderId,
        string stringOrderId, string operation)
    {
        if (numericOrderId is > 0)
            return numericOrderId.Value;
        if (!stringOrderId.IsEmpty() && long.TryParse(stringOrderId,
            NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
            parsed > 0)
            return parsed;
        throw new InvalidOperationException(
            $"CoinJar {operation} requires a numeric exchange order ID.");
    }

    /// <inheritdoc />
    protected override void DisposeManaged()
    {
        DisposeClients();
        base.DisposeManaged();
    }
}
