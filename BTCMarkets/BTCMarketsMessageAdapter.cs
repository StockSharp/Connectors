namespace StockSharp.BTCMarkets;

public partial class BTCMarketsMessageAdapter
{
    private class MarketSubscription
    {
        public string MarketId { get; init; }
    }

    private sealed class DepthSubscription : MarketSubscription
    {
        public int Depth { get; init; }
    }

    private sealed class CandleSubscription : MarketSubscription
    {
        public TimeSpan TimeFrame { get; init; }
        public BTCMarketsCandle Current { get; set; }
    }

    private sealed class OrderSubscription
    {
        public string MarketId { get; init; }
        public string OrderId { get; init; }
        public Sides? Side { get; init; }
    }

    private sealed class TrackedOrder
    {
        public long TransactionId { get; init; }
        public string MarketId { get; init; }
        public string ExchangeOrderId { get; set; }
        public string ClientOrderId { get; init; }
        public Sides Side { get; init; }
        public OrderTypes OrderType { get; init; }
        public decimal Volume { get; init; }
        public decimal Price { get; init; }
        public BTCMarketsOrderCondition Condition { get; init; }
    }

    private sealed class BookState
    {
        public SortedDictionary<decimal, BTCMarketsBookLevel> Bids { get; } =
            new(Comparer<decimal>.Create(static (left, right) =>
                right.CompareTo(left)));
        public SortedDictionary<decimal, BTCMarketsBookLevel> Asks { get; } = [];
        public bool IsSnapshotReady { get; set; }
        public bool IsRefreshPending { get; set; }
        public long SnapshotId { get; set; }
    }

    private readonly record struct StreamKey(BTCMarketsSocketChannels Channel,
        string MarketId);
    private readonly record struct BalanceFingerprint(decimal Balance,
        decimal Available, decimal Locked);
    private readonly record struct OrderFingerprint(BTCMarketsOrderStatuses? Status,
        decimal? OpenAmount, decimal? Amount, decimal? Price);

    private readonly Lock _sync = new();
    private readonly Dictionary<string, BTCMarketsMarket> _markets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
    private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
    private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
    private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
    private readonly Dictionary<StreamKey, int> _streamReferences = [];
    private readonly Dictionary<BTCMarketsSocketChannels, int>
        _privateStreamReferences = [];
    private readonly Dictionary<string, BookState> _books =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TrackedOrder> _trackedOrders =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _transactionsByClientOrderId =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenPublicTrades =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenAccountTrades =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<long> _portfolioSubscriptions = [];
    private readonly Dictionary<long, OrderSubscription> _orderSubscriptions = [];
    private readonly Dictionary<string, BalanceFingerprint> _balanceFingerprints =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OrderFingerprint> _orderFingerprints =
        new(StringComparer.OrdinalIgnoreCase);
    private BTCMarketsRestClient _restClient;
    private BTCMarketsSocketClient _socketClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="BTCMarketsMessageAdapter"/>
    /// class.
    /// </summary>
    public BTCMarketsMessageAdapter(IdGenerator transactionIdGenerator)
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
    public override bool IsReplaceCommandEditCurrent => true;

    /// <inheritdoc />
    public override bool IsSupportTransactionLog => true;

    /// <inheritdoc />
    public override string[] AssociatedBoards => [BoardCodes.BTCMarkets];

    /// <inheritdoc />
    protected override bool ValidateSecurityId(SecurityId securityId)
        => securityId.BoardCode.IsEmpty() ||
            securityId.BoardCode.EqualsIgnoreCase(BoardCodes.BTCMarkets) ||
            securityId.IsAssociated(BoardCodes.BTCMarkets);

    private BTCMarketsRestClient RestClient
        => _restClient ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private BTCMarketsSocketClient SocketClient
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
                "BTC Markets API key and secret are required for private operations.");
    }

    private string GetPortfolioName() => $"BTCMarkets_{Key.ToId()}";

    private void ValidatePortfolio(string portfolioName)
    {
        if (!portfolioName.IsEmpty() &&
            !portfolioName.EqualsIgnoreCase(GetPortfolioName()))
            throw new InvalidOperationException(
                $"Unknown BTC Markets portfolio '{portfolioName}'.");
    }

    private void RegisterMarkets(IEnumerable<BTCMarketsMarket> markets)
    {
        using (_sync.EnterScope())
        {
            _markets.Clear();
            foreach (var market in markets ?? [])
                if (market?.MarketId.IsEmpty() == false &&
                    !market.BaseAsset.IsEmpty() && !market.QuoteAsset.IsEmpty())
                    _markets[market.MarketId.NormalizeMarket()] = market;
        }
    }

    private BTCMarketsMarket GetMarket(SecurityId securityId)
    {
        if (!securityId.BoardCode.IsEmpty() &&
            !securityId.BoardCode.EqualsIgnoreCase(BoardCodes.BTCMarkets) &&
            !securityId.IsAssociated(BoardCodes.BTCMarkets))
            throw new InvalidOperationException(
                $"Security board '{securityId.BoardCode}' is not BTC Markets.");
        return GetMarket(securityId.SecurityCode);
    }

    private BTCMarketsMarket GetMarket(string marketId)
    {
        marketId = marketId.NormalizeMarket();
        using (_sync.EnterScope())
            return _markets.TryGetValue(marketId, out var market)
                ? market
                : throw new InvalidOperationException(
                    $"Unknown BTC Markets market '{marketId}'.");
    }

    private string[] GetAllMarkets()
    {
        using (_sync.EnterScope())
            return [.. _markets.Keys.OrderBy(static value => value,
                StringComparer.OrdinalIgnoreCase)];
    }

    private async ValueTask AcquireStreamAsync(BTCMarketsSocketChannels channel,
        string marketId, CancellationToken cancellationToken)
    {
        var key = new StreamKey(channel, marketId.NormalizeMarket());
        bool subscribe;
        using (_sync.EnterScope())
        {
            if (_streamReferences.TryGetValue(key, out var count))
            {
                _streamReferences[key] = count + 1;
                return;
            }
            _streamReferences.Add(key, 1);
            subscribe = true;
        }
        if (subscribe)
            await SocketClient.SubscribeAsync(channel, key.MarketId,
                cancellationToken);
    }

    private async ValueTask ReleaseStreamAsync(BTCMarketsSocketChannels channel,
        string marketId, CancellationToken cancellationToken)
    {
        var key = new StreamKey(channel, marketId.NormalizeMarket());
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
            await _socketClient.UnsubscribeAsync(channel, key.MarketId,
                cancellationToken);
    }

    private async ValueTask AcquirePrivateStreamAsync(
        BTCMarketsSocketChannels channel, CancellationToken cancellationToken)
    {
        using (_sync.EnterScope())
        {
            if (_privateStreamReferences.TryGetValue(channel, out var count))
            {
                _privateStreamReferences[channel] = count + 1;
                return;
            }
            _privateStreamReferences.Add(channel, 1);
        }
        await SocketClient.SubscribePrivateAsync(channel, cancellationToken);
    }

    private async ValueTask ReleasePrivateStreamAsync(
        BTCMarketsSocketChannels channel, CancellationToken cancellationToken)
    {
        var unsubscribe = false;
        using (_sync.EnterScope())
        {
            if (!_privateStreamReferences.TryGetValue(channel, out var count))
                return;
            if (count > 1)
                _privateStreamReferences[channel] = count - 1;
            else
            {
                _privateStreamReferences.Remove(channel);
                unsubscribe = true;
            }
        }
        if (unsubscribe && _socketClient is not null)
            await _socketClient.UnsubscribePrivateAsync(channel,
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
                : 0;
    }

    private bool AddPublicTrade(string tradeId, long transactionId)
    {
        if (tradeId.IsEmpty())
            return true;
        using (_sync.EnterScope())
        {
            if (_seenPublicTrades.Count > 100000)
                _seenPublicTrades.Clear();
            return _seenPublicTrades.Add($"{transactionId}:{tradeId}");
        }
    }

    private bool AddAccountTrade(string tradeId, long transactionId)
    {
        if (tradeId.IsEmpty())
            return true;
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
            _markets.Clear();
            _level1Subscriptions.Clear();
            _depthSubscriptions.Clear();
            _tickSubscriptions.Clear();
            _candleSubscriptions.Clear();
            _streamReferences.Clear();
            _privateStreamReferences.Clear();
            _books.Clear();
            _trackedOrders.Clear();
            _transactionsByClientOrderId.Clear();
            _seenPublicTrades.Clear();
            _seenAccountTrades.Clear();
            _portfolioSubscriptions.Clear();
            _orderSubscriptions.Clear();
            _balanceFingerprints.Clear();
            _orderFingerprints.Clear();
        }
    }

    private static string ResolveOrderIdentifier(long? numericOrderId,
        string stringOrderId, string operation)
    {
        if (!stringOrderId.IsEmpty())
            return stringOrderId.Trim();
        if (numericOrderId is > 0)
            return numericOrderId.Value.ToString(CultureInfo.InvariantCulture);
        throw new InvalidOperationException(
            $"BTC Markets {operation} requires an exchange order ID.");
    }

    /// <inheritdoc />
    protected override void DisposeManaged()
    {
        DisposeClients();
        base.DisposeManaged();
    }
}
