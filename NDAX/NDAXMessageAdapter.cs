namespace StockSharp.NDAX;

public partial class NDAXMessageAdapter
{
    private const int _webSocketDepth = 500;

    private class MarketSubscription
    {
        public int InstrumentId { get; init; }
        public string Symbol { get; init; }
    }

    private sealed class DepthSubscription : MarketSubscription
    {
        public int Depth { get; init; }
    }

    private sealed class CandleSubscription : MarketSubscription
    {
        public TimeSpan TimeFrame { get; init; }
    }

    private sealed class OrderSubscription
    {
        public long? OrderId { get; init; }
        public long? ClientOrderId { get; init; }
        public int? InstrumentId { get; init; }
        public Sides? Side { get; init; }
        public DateTime? From { get; init; }
        public DateTime? To { get; init; }
        public int Maximum { get; init; }
    }

    private sealed class TrackedOrder
    {
        public long TransactionId { get; init; }
        public long ClientOrderId { get; init; }
        public long OrderId { get; set; }
        public int InstrumentId { get; init; }
        public Sides Side { get; init; }
        public OrderTypes OrderType { get; init; }
        public decimal Volume { get; init; }
        public decimal Price { get; init; }
        public NDAXOrderCondition Condition { get; init; }
        public OrderStates State { get; set; }
    }

    private sealed class BookState
    {
        public bool IsSnapshotReady { get; set; }
        public bool IsRefreshPending { get; set; }
        public long Sequence { get; set; }
        public Dictionary<BookLevelKey, BookLevel> Levels { get; } = [];
    }

    private enum BookSequenceStates
    {
        Valid,
        Duplicate,
        Gap,
    }

    private readonly record struct BookLevel(decimal Volume, int OrderCount);

    private readonly record struct StreamKey(NdaxSubscriptionKinds Kind,
        int InstrumentId, int Parameter, long AccountId);
    private readonly record struct OrderFingerprint(string State,
        decimal Quantity, decimal Executed, decimal Price, decimal Average);
    private readonly record struct TradeDeliveryKey(long TargetId,
        long TradeId);
    private readonly record struct PositionFingerprint(decimal Amount,
        decimal Hold);

    private readonly Lock _sync = new();
    private readonly Dictionary<int, NdaxInstrument> _instrumentsById = [];
    private readonly Dictionary<string, NdaxInstrument> _instrumentsBySymbol =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, NdaxProduct> _products = [];
    private readonly Dictionary<long, MarketSubscription>
        _level1Subscriptions = [];
    private readonly Dictionary<long, DepthSubscription>
        _depthSubscriptions = [];
    private readonly Dictionary<long, MarketSubscription>
        _tickSubscriptions = [];
    private readonly Dictionary<long, CandleSubscription>
        _candleSubscriptions = [];
    private readonly Dictionary<StreamKey, int> _streamReferences = [];
    private readonly Dictionary<int, BookState> _books = [];
    private readonly HashSet<long> _portfolioSubscriptions = [];
    private readonly Dictionary<long, OrderSubscription>
        _orderSubscriptions = [];
    private readonly Dictionary<long, TrackedOrder> _ordersByClientId = [];
    private readonly Dictionary<long, TrackedOrder> _ordersById = [];
    private readonly HashSet<string> _publicTrades =
        new(StringComparer.Ordinal);
    private readonly HashSet<TradeDeliveryKey> _accountTrades = [];
    private readonly Dictionary<string, OrderFingerprint>
        _orderFingerprints = [];
    private readonly Dictionary<string, PositionFingerprint>
        _positionFingerprints = [];
    private NDAXRestClient _restClient;
    private NDAXSocketClient _socketClient;
    private DateTime _lastPing;

    /// <summary>
    /// Initializes a new instance of the <see cref="NDAXMessageAdapter"/>
    /// class.
    /// </summary>
    public NDAXMessageAdapter(IdGenerator transactionIdGenerator)
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
    public override string[] AssociatedBoards => [BoardCodes.NDAX];

    /// <inheritdoc />
    protected override bool ValidateSecurityId(SecurityId securityId)
        => securityId.BoardCode.IsEmpty() ||
            securityId.BoardCode.EqualsIgnoreCase(BoardCodes.NDAX) ||
            securityId.IsAssociated(BoardCodes.NDAX);

    private NDAXRestClient RestClient => _restClient ?? throw new
        InvalidOperationException(LocalizedStrings.ConnectionNotOk);

    private NDAXSocketClient SocketClient => _socketClient ?? throw new
        InvalidOperationException(LocalizedStrings.ConnectionNotOk);

    private long EffectiveAccountId => AccountId > 0
        ? AccountId
        : SocketClient.DefaultAccountId;

    private string PortfolioName => $"NDAX_{EffectiveAccountId}";

    private void EnsureConnected()
    {
        if (_restClient is null || _socketClient is null)
            throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
    }

    private void EnsurePrivateReady()
    {
        EnsureConnected();
        if (!SocketClient.IsAuthenticated || EffectiveAccountId <= 0)
            throw new InvalidOperationException(
                "NDAX API key, secret, user ID, and account are required for private operations.");
    }

    private void ValidatePortfolio(string portfolioName)
    {
        if (!portfolioName.IsEmpty() &&
            !portfolioName.EqualsIgnoreCase(PortfolioName))
            throw new InvalidOperationException(
                $"Unknown NDAX portfolio '{portfolioName}'.");
    }

    private void RegisterCatalog(IEnumerable<NdaxInstrument> instruments,
        IEnumerable<NdaxProduct> products)
    {
        using (_sync.EnterScope())
        {
            _instrumentsById.Clear();
            _instrumentsBySymbol.Clear();
            _products.Clear();
            foreach (var product in products ?? [])
                if (product is not null && product.ProductId > 0 &&
                    !product.Symbol.IsEmpty())
                    _products[product.ProductId] = product;
            foreach (var instrument in instruments ?? [])
                if (instrument is not null && instrument.InstrumentId > 0 &&
                    !instrument.Symbol.IsEmpty())
                {
                    _instrumentsById[instrument.InstrumentId] = instrument;
                    _instrumentsBySymbol[instrument.Symbol.NormalizeSymbol()] =
                        instrument;
                }
        }
    }

    private NdaxInstrument GetInstrument(SecurityId securityId)
    {
        if (!securityId.BoardCode.IsEmpty() &&
            !securityId.BoardCode.EqualsIgnoreCase(BoardCodes.NDAX) &&
            !securityId.IsAssociated(BoardCodes.NDAX))
            throw new InvalidOperationException(
                $"Security board '{securityId.BoardCode}' is not NDAX.");
        var symbol = securityId.SecurityCode.NormalizeSymbol();
        using (_sync.EnterScope())
            return _instrumentsBySymbol.TryGetValue(symbol, out var value)
                ? value
                : throw new InvalidOperationException(
                    $"Unknown NDAX instrument '{symbol}'.");
    }

    private NdaxInstrument GetInstrument(int instrumentId)
    {
        using (_sync.EnterScope())
            return _instrumentsById.TryGetValue(instrumentId, out var value)
                ? value
                : null;
    }

    private void TrackOrder(TrackedOrder order)
    {
        if (order is null)
            return;
        using (_sync.EnterScope())
        {
            if (order.ClientOrderId > 0)
                _ordersByClientId[order.ClientOrderId] = order;
            if (order.OrderId > 0)
                _ordersById[order.OrderId] = order;
        }
    }

    private TrackedOrder GetTrackedOrder(long? orderId, long? clientOrderId,
        long transactionId = 0)
    {
        using (_sync.EnterScope())
        {
            if (orderId is > 0 && _ordersById.TryGetValue(orderId.Value,
                out var byOrder))
                return byOrder;
            if (clientOrderId is > 0 && _ordersByClientId.TryGetValue(
                clientOrderId.Value, out var byClient))
                return byClient;
            return transactionId > 0
                ? _ordersByClientId.Values.FirstOrDefault(value =>
                    value.TransactionId == transactionId)
                : null;
        }
    }

    private async ValueTask AcquireStreamAsync(StreamKey key,
        CancellationToken cancellationToken)
    {
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
            await ChangeStreamAsync(key, true, cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _streamReferences.Remove(key);
            throw;
        }
    }

    private async ValueTask ReleaseStreamAsync(StreamKey key,
        CancellationToken cancellationToken)
    {
        var release = false;
        using (_sync.EnterScope())
        {
            if (!_streamReferences.TryGetValue(key, out var count))
                return;
            if (count > 1)
                _streamReferences[key] = count - 1;
            else
            {
                _streamReferences.Remove(key);
                release = true;
            }
        }
        if (release && _socketClient is not null)
            await ChangeStreamAsync(key, false, cancellationToken);
    }

    private ValueTask ChangeStreamAsync(StreamKey key, bool subscribe,
        CancellationToken cancellationToken)
        => (key.Kind, subscribe) switch
        {
            (NdaxSubscriptionKinds.Level1, true) =>
                SocketClient.SubscribeLevel1Async(key.InstrumentId,
                    cancellationToken),
            (NdaxSubscriptionKinds.Level1, false) =>
                SocketClient.UnsubscribeLevel1Async(key.InstrumentId,
                    cancellationToken),
            (NdaxSubscriptionKinds.Level2, true) =>
                SocketClient.SubscribeLevel2Async(key.InstrumentId,
                    _webSocketDepth, cancellationToken),
            (NdaxSubscriptionKinds.Level2, false) =>
                SocketClient.UnsubscribeLevel2Async(key.InstrumentId,
                    _webSocketDepth, cancellationToken),
            (NdaxSubscriptionKinds.Trades, true) =>
                SocketClient.SubscribeTradesAsync(key.InstrumentId, 0,
                    cancellationToken),
            (NdaxSubscriptionKinds.Trades, false) =>
                SocketClient.UnsubscribeTradesAsync(key.InstrumentId, 0,
                    cancellationToken),
            (NdaxSubscriptionKinds.Ticker, true) =>
                SocketClient.SubscribeTickerAsync(key.InstrumentId,
                    TimeSpan.FromSeconds(key.Parameter), 0,
                    cancellationToken),
            (NdaxSubscriptionKinds.Ticker, false) =>
                SocketClient.UnsubscribeTickerAsync(key.InstrumentId,
                    TimeSpan.FromSeconds(key.Parameter), cancellationToken),
            (NdaxSubscriptionKinds.AccountEvents, true) =>
                SocketClient.SubscribeAccountEventsAsync(key.AccountId,
                    cancellationToken),
            (NdaxSubscriptionKinds.AccountEvents, false) =>
                SocketClient.UnsubscribeAccountEventsAsync(key.AccountId,
                    cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
        };

    private void ClearState()
    {
        using (_sync.EnterScope())
        {
            _instrumentsById.Clear();
            _instrumentsBySymbol.Clear();
            _products.Clear();
            _level1Subscriptions.Clear();
            _depthSubscriptions.Clear();
            _tickSubscriptions.Clear();
            _candleSubscriptions.Clear();
            _streamReferences.Clear();
            _books.Clear();
            _portfolioSubscriptions.Clear();
            _orderSubscriptions.Clear();
            _ordersByClientId.Clear();
            _ordersById.Clear();
            _publicTrades.Clear();
            _accountTrades.Clear();
            _orderFingerprints.Clear();
            _positionFingerprints.Clear();
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
