namespace StockSharp.Rain;

public partial class RainMessageAdapter
{
    private class MarketSubscription
    {
        public string Symbol { get; init; }
    }

    private sealed class CandleSubscription : MarketSubscription
    {
        public TimeSpan TimeFrame { get; init; }
    }

    private sealed class DepthSubscription : MarketSubscription
    {
        public int Depth { get; init; }
    }

    private sealed class OrderSubscription
    {
        public string ClientOrderId { get; init; }
        public string Symbol { get; init; }
        public Sides? Side { get; init; }
        public DateTime? From { get; init; }
        public DateTime? To { get; init; }
        public int Maximum { get; init; }
    }

    private sealed class TrackedOrder
    {
        public long TransactionId { get; init; }
        public string ClientOrderId { get; set; }
        public string Symbol { get; init; }
        public Sides Side { get; init; }
        public OrderTypes OrderType { get; init; }
        public decimal Volume { get; init; }
        public decimal Price { get; init; }
        public RainOrderCondition Condition { get; init; }
        public OrderStates State { get; set; }
    }

    private sealed class BookState
    {
        public bool IsSnapshotReady { get; set; }
        public bool IsRefreshPending { get; set; }
        public long Sequence { get; set; }
    }

    private readonly record struct StreamKey(RainSocketChannels Channel,
        string Selector);
    private readonly record struct BalanceFingerprint(decimal? Balance);
    private readonly record struct OrderFingerprint(RainOrderStatuses? Status,
        decimal? Quantity, decimal? Filled, decimal? Price,
        decimal? FilledPrice);
    private readonly record struct TradeDeliveryKey(long TargetId,
        string TradeId);

    private readonly Lock _sync = new();
    private readonly Dictionary<string, RainProduct> _products =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, MarketSubscription>
        _level1Subscriptions = [];
    private readonly Dictionary<long, DepthSubscription>
        _depthSubscriptions = [];
    private readonly Dictionary<long, MarketSubscription>
        _tickSubscriptions = [];
    private readonly Dictionary<long, CandleSubscription>
        _candleSubscriptions = [];
    private readonly Dictionary<StreamKey, int> _streamReferences = [];
    private readonly Dictionary<string, BookState> _books =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<long> _portfolioSubscriptions = [];
    private readonly Dictionary<long, OrderSubscription>
        _orderSubscriptions = [];
    private readonly Dictionary<string, TrackedOrder> _trackedOrders =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenPublicTrades =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<TradeDeliveryKey> _seenAccountTrades = [];
    private readonly Dictionary<string, BalanceFingerprint>
        _balanceFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OrderFingerprint>
        _orderFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private RainRestClient _restClient;
    private RainSocketClient _socketClient;
    private DateTime _lastPing;

    /// <summary>
    /// Initializes a new instance of the <see cref="RainMessageAdapter"/>
    /// class.
    /// </summary>
    public RainMessageAdapter(IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        HeartbeatInterval = TimeSpan.FromSeconds(1);
        this.AddMarketDataSupport();
        this.AddTransactionalSupport();
        this.AddSupportedMarketDataType(DataType.Ticks);
        this.AddSupportedMarketDataType(DataType.MarketDepth);
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedCandleTimeFrames(AllTimeFrames);
        this.RemoveSupportedMessage(MessageTypes.OrderReplace);
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities ||
            dataType == DataType.Transactions ||
            dataType == DataType.PositionChanges ||
            base.IsAllDownloadingSupported(dataType);

    /// <inheritdoc />
    public override bool IsSupportOrderBookIncrements => true;

    /// <inheritdoc />
    public override bool IsReplaceCommandEditCurrent => false;

    /// <inheritdoc />
    public override bool IsSupportTransactionLog => true;

    /// <inheritdoc />
    public override string[] AssociatedBoards => [BoardCodes.Rain];

    /// <inheritdoc />
    protected override bool ValidateSecurityId(SecurityId securityId)
        => securityId.BoardCode.IsEmpty() ||
            securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Rain) ||
            securityId.IsAssociated(BoardCodes.Rain);

    private RainRestClient RestClient
        => _restClient ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private RainSocketClient SocketClient
        => _socketClient ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private void EnsureConnected()
    {
        if (_restClient is null)
            throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
    }

    private void EnsurePrivateReady()
    {
        EnsureConnected();
        if (!RestClient.IsPrivateAvailable)
            throw new InvalidOperationException(
                "Rain API key, secret, and access token are required for private operations.");
    }

    private string GetPortfolioName() => $"Rain_{Key.ToId()}";

    private void ValidatePortfolio(string portfolioName)
    {
        if (!portfolioName.IsEmpty() &&
            !portfolioName.EqualsIgnoreCase(GetPortfolioName()))
            throw new InvalidOperationException(
                $"Unknown Rain portfolio '{portfolioName}'.");
    }

    private void RegisterProducts(IEnumerable<RainProduct> products)
    {
        using (_sync.EnterScope())
        {
            _products.Clear();
            foreach (var product in products ?? [])
                if (product?.Symbol.IsEmpty() == false &&
                    product.BaseCurrency?.Code.IsEmpty() == false &&
                    product.ReferenceCurrency?.Code.IsEmpty() == false)
                    _products[product.Symbol.NormalizeSymbol()] = product;
        }
    }

    private RainProduct GetProduct(SecurityId securityId)
    {
        if (!securityId.BoardCode.IsEmpty() &&
            !securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Rain) &&
            !securityId.IsAssociated(BoardCodes.Rain))
            throw new InvalidOperationException(
                $"Security board '{securityId.BoardCode}' is not Rain.");
        var symbol = securityId.SecurityCode.NormalizeSymbol();
        using (_sync.EnterScope())
            return _products.TryGetValue(symbol, out var product)
                ? product
                : throw new InvalidOperationException(
                    $"Unknown Rain product '{symbol}'.");
    }

    private async ValueTask AcquireStreamAsync(RainSocketChannels channel,
        string selector, CancellationToken cancellationToken)
    {
        var key = new StreamKey(channel, selector);
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
            await SocketClient.SubscribeAsync(channel, selector,
                cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _streamReferences.Remove(key);
            throw;
        }
    }

    private async ValueTask ReleaseStreamAsync(RainSocketChannels channel,
        string selector, CancellationToken cancellationToken)
    {
        var key = new StreamKey(channel, selector);
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
            await _socketClient.UnsubscribeAsync(channel, selector,
                cancellationToken);
    }

    private void TrackOrder(TrackedOrder tracked, params string[] identifiers)
    {
        if (tracked is null)
            return;
        using (_sync.EnterScope())
            foreach (var identifier in identifiers.Where(static value =>
                !value.IsEmpty()))
                _trackedOrders[identifier] = tracked;
    }

    private TrackedOrder GetTrackedOrder(string identifier)
    {
        if (identifier.IsEmpty())
            return null;
        using (_sync.EnterScope())
            return _trackedOrders.TryGetValue(identifier, out var tracked)
                ? tracked
                : null;
    }

    private TrackedOrder GetTrackedOrder(long transactionId)
    {
        using (_sync.EnterScope())
            return _trackedOrders.Values.FirstOrDefault(order =>
                order.TransactionId == transactionId);
    }

    private TrackedOrder MatchTrackedOrder(RainOrder order)
    {
        if (order is null)
            return null;
        var tracked = GetTrackedOrder(order.ClientOrderId);
        if (tracked is not null)
            return tracked;
        using (_sync.EnterScope())
            return _trackedOrders.Values.Distinct().FirstOrDefault(value =>
                value.ClientOrderId.IsEmpty() &&
                value.Symbol.EqualsIgnoreCase(order.Symbol) &&
                (order.Side is null ||
                    order.Side.Value.ToStockSharp() == value.Side) &&
                value.State != OrderStates.Done);
    }

    private bool AddPublicTrade(string tradeId, long targetId)
    {
        if (tradeId.IsEmpty())
            return true;
        using (_sync.EnterScope())
        {
            if (_seenPublicTrades.Count > 100000)
                _seenPublicTrades.Clear();
            return _seenPublicTrades.Add($"{targetId}:{tradeId}");
        }
    }

    private bool AddAccountTrade(string tradeId, long targetId)
    {
        if (tradeId.IsEmpty())
            return true;
        using (_sync.EnterScope())
        {
            if (_seenAccountTrades.Count > 100000)
                _seenAccountTrades.Clear();
            return _seenAccountTrades.Add(new(targetId, tradeId));
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
            _portfolioSubscriptions.Clear();
            _orderSubscriptions.Clear();
            _trackedOrders.Clear();
            _seenPublicTrades.Clear();
            _seenAccountTrades.Clear();
            _balanceFingerprints.Clear();
            _orderFingerprints.Clear();
            _lastPing = default;
        }
    }

    /// <inheritdoc />
    protected override void DisposeManaged()
    {
        DisposeClients();
        base.DisposeManaged();
    }
}
