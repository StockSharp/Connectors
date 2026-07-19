namespace StockSharp.Foxbit;

public partial class FoxbitMessageAdapter
{
    private class MarketSubscription
    {
        public string MarketSymbol { get; init; }
    }

    private sealed class DepthSubscription : MarketSubscription
    {
        public int Depth { get; init; }
    }

    private sealed class CandleSubscription : MarketSubscription
    {
        public TimeSpan TimeFrame { get; init; }
        public FoxbitCandle Current { get; set; }
    }

    private sealed class OrderSubscription
    {
        public string MarketSymbol { get; init; }
        public string OrderId { get; init; }
        public string ClientOrderId { get; init; }
        public Sides? Side { get; init; }
        public DateTime? From { get; init; }
        public DateTime? To { get; init; }
        public int Maximum { get; init; }
    }

    private sealed class TrackedOrder
    {
        public long TransactionId { get; init; }
        public string ExchangeOrderId { get; set; }
        public string ClientOrderId { get; init; }
        public string MarketSymbol { get; init; }
        public Sides Side { get; init; }
        public OrderTypes OrderType { get; init; }
        public decimal Volume { get; init; }
        public decimal Price { get; init; }
        public FoxbitOrderCondition Condition { get; init; }
    }

    private sealed class BookState
    {
        public bool IsSnapshotReady { get; set; }
        public bool IsRefreshPending { get; set; }
        public long SequenceId { get; set; }
    }

    private readonly record struct StreamKey(FoxbitSocketChannels Channel,
        string MarketSymbol);
    private readonly record struct BalanceFingerprint(decimal Balance,
        decimal Available, decimal Locked);
    private readonly record struct OrderFingerprint(FoxbitOrderStates? State,
        decimal? Quantity, decimal? Executed, decimal? Price,
        decimal? AveragePrice);
    private readonly record struct TradeDeliveryKey(long TargetId,
        string TradeId);

    private readonly Lock _sync = new();
    private readonly Dictionary<string, FoxbitMarket> _markets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, MarketSubscription>
        _level1Subscriptions = [];
    private readonly Dictionary<long, DepthSubscription> _depthSubscriptions =
        [];
    private readonly Dictionary<long, MarketSubscription> _tickSubscriptions =
        [];
    private readonly Dictionary<long, CandleSubscription>
        _candleSubscriptions = [];
    private readonly Dictionary<StreamKey, int> _streamReferences = [];
    private readonly Dictionary<string, BookState> _books =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<long> _portfolioSubscriptions = [];
    private readonly Dictionary<long, OrderSubscription> _orderSubscriptions =
        [];
    private readonly Dictionary<string, TrackedOrder> _trackedOrders =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _transactionsByClientOrderId =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenPublicTrades =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<TradeDeliveryKey> _seenAccountTrades = [];
    private readonly Dictionary<string, BalanceFingerprint>
        _balanceFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OrderFingerprint> _orderFingerprints =
        new(StringComparer.OrdinalIgnoreCase);
    private FoxbitRestClient _restClient;
    private FoxbitSocketClient _socketClient;
    private DateTime _lastOrderRefresh;
    private DateTime _lastPortfolioRefresh;
    private DateTime _lastPing;

    /// <summary>
    /// Initializes a new instance of the <see cref="FoxbitMessageAdapter"/>
    /// class.
    /// </summary>
    public FoxbitMessageAdapter(IdGenerator transactionIdGenerator)
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
    public override string[] AssociatedBoards => [BoardCodes.Foxbit];

    /// <inheritdoc />
    protected override bool ValidateSecurityId(SecurityId securityId)
        => securityId.BoardCode.IsEmpty() ||
            securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Foxbit) ||
            securityId.IsAssociated(BoardCodes.Foxbit);

    private FoxbitRestClient RestClient
        => _restClient ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private FoxbitSocketClient SocketClient
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
        if (!RestClient.IsCredentialsAvailable)
            throw new InvalidOperationException(
                "Foxbit API key and secret are required for private operations.");
    }

    private string GetPortfolioName() => $"Foxbit_{Key.ToId()}";

    private void ValidatePortfolio(string portfolioName)
    {
        if (!portfolioName.IsEmpty() &&
            !portfolioName.EqualsIgnoreCase(GetPortfolioName()))
            throw new InvalidOperationException(
                $"Unknown Foxbit portfolio '{portfolioName}'.");
    }

    private void RegisterMarkets(IEnumerable<FoxbitMarket> markets)
    {
        using (_sync.EnterScope())
        {
            _markets.Clear();
            foreach (var market in markets ?? [])
                if (market?.Symbol.IsEmpty() == false &&
                    market.Base?.Symbol.IsEmpty() == false &&
                    market.Quote?.Symbol.IsEmpty() == false)
                    _markets[market.Symbol.NormalizeMarket()] = market;
        }
    }

    private FoxbitMarket GetMarket(SecurityId securityId)
    {
        if (!securityId.BoardCode.IsEmpty() &&
            !securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Foxbit) &&
            !securityId.IsAssociated(BoardCodes.Foxbit))
            throw new InvalidOperationException(
                $"Security board '{securityId.BoardCode}' is not Foxbit.");
        return GetMarket(securityId.SecurityCode);
    }

    private FoxbitMarket GetMarket(string marketSymbol)
    {
        marketSymbol = marketSymbol.NormalizeMarket();
        using (_sync.EnterScope())
            return _markets.TryGetValue(marketSymbol, out var market)
                ? market
                : throw new InvalidOperationException(
                    $"Unknown Foxbit market '{marketSymbol}'.");
    }

    private async ValueTask AcquireStreamAsync(FoxbitSocketChannels channel,
        string marketSymbol, CancellationToken cancellationToken)
    {
        var key = new StreamKey(channel, marketSymbol.NormalizeMarket());
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
            await SocketClient.SubscribeAsync(channel, key.MarketSymbol,
                cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _streamReferences.Remove(key);
            throw;
        }
    }

    private async ValueTask ReleaseStreamAsync(FoxbitSocketChannels channel,
        string marketSymbol, CancellationToken cancellationToken)
    {
        var key = new StreamKey(channel, marketSymbol.NormalizeMarket());
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
            await _socketClient.UnsubscribeAsync(channel, key.MarketSymbol,
                cancellationToken);
    }

    private void TrackOrder(TrackedOrder order, params string[] identifiers)
    {
        if (order is null)
            return;
        using (_sync.EnterScope())
        {
            foreach (var identifier in identifiers.Where(static value =>
                !value.IsEmpty()))
                _trackedOrders[identifier] = order;
            if (!order.ExchangeOrderId.IsEmpty())
                _trackedOrders[order.ExchangeOrderId] = order;
            if (!order.ClientOrderId.IsEmpty())
            {
                _trackedOrders[order.ClientOrderId] = order;
                _transactionsByClientOrderId[order.ClientOrderId] =
                    order.TransactionId;
            }
        }
    }

    private TrackedOrder GetTrackedOrder(params string[] identifiers)
    {
        using (_sync.EnterScope())
            foreach (var identifier in identifiers)
                if (!identifier.IsEmpty() &&
                    _trackedOrders.TryGetValue(identifier, out var order))
                    return order;
        return null;
    }

    private long GetTransactionId(string clientOrderId)
    {
        if (clientOrderId.IsEmpty())
            return 0;
        using (_sync.EnterScope())
            return _transactionsByClientOrderId.TryGetValue(clientOrderId,
                out var transactionId)
                ? transactionId
                : long.TryParse(clientOrderId, NumberStyles.None,
                    CultureInfo.InvariantCulture, out transactionId)
                    ? transactionId
                    : 0;
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
            _markets.Clear();
            _level1Subscriptions.Clear();
            _depthSubscriptions.Clear();
            _tickSubscriptions.Clear();
            _candleSubscriptions.Clear();
            _streamReferences.Clear();
            _books.Clear();
            _portfolioSubscriptions.Clear();
            _orderSubscriptions.Clear();
            _trackedOrders.Clear();
            _transactionsByClientOrderId.Clear();
            _seenPublicTrades.Clear();
            _seenAccountTrades.Clear();
            _balanceFingerprints.Clear();
            _orderFingerprints.Clear();
            _lastOrderRefresh = default;
            _lastPortfolioRefresh = default;
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
