namespace StockSharp.Korbit;

public partial class KorbitMessageAdapter
{
    private enum StreamTypes
    {
        Ticker,
        OrderBook,
        Trade,
    }

    private sealed class MarketDefinition
    {
        public string Symbol { get; init; }
        public string BaseCurrency { get; init; }
        public string QuoteCurrency { get; init; }
        public KorbitPairStatuses Status { get; init; }
        public KorbitTickSizePolicy TickSizePolicy { get; set; }
    }

    private class MarketSubscription
    {
        public string Symbol { get; init; }
    }

    private sealed class DepthSubscription : MarketSubscription
    {
        public int Depth { get; init; }
    }

    private sealed class CandleSubscription : MarketSubscription
    {
        public TimeSpan TimeFrame { get; init; }
        public KorbitCandle Current { get; set; }
    }

    private sealed class OrderSubscription
    {
        public string Symbol { get; init; }
        public string OrderIdentifier { get; init; }
        public Sides? Side { get; init; }
    }

    private sealed class TrackedOrder
    {
        public long TransactionId { get; init; }
        public string Symbol { get; init; }
        public long ExchangeOrderId { get; set; }
        public string ClientOrderId { get; init; }
        public Sides Side { get; init; }
        public OrderTypes OrderType { get; init; }
        public decimal Volume { get; init; }
        public decimal Price { get; init; }
        public TimeInForce TimeInForce { get; init; }
        public bool? IsPostOnly { get; init; }
        public KorbitOrderCondition Condition { get; init; }
    }

    private readonly record struct StreamKey(StreamTypes Type, string Symbol);

    private readonly Lock _sync = new();
    private readonly Dictionary<string, MarketDefinition> _markets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, MarketSubscription> _level1Subscriptions =
        [];
    private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
    private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
    private readonly Dictionary<long, CandleSubscription> _candleSubscriptions =
        [];
    private readonly Dictionary<StreamKey, int> _streamReferences = [];
    private readonly Dictionary<string, TrackedOrder> _trackedOrders =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, string> _clientOrderIds = [];
    private readonly HashSet<string> _seenPublicTrades =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenAccountTrades =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<long> _portfolioSubscriptions = [];
    private readonly Dictionary<long, OrderSubscription> _orderSubscriptions = [];
    private KorbitRestClient _restClient;
    private KorbitSocketClient _publicSocketClient;
    private KorbitSocketClient _privateSocketClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="KorbitMessageAdapter"/>
    /// class.
    /// </summary>
    public KorbitMessageAdapter(IdGenerator transactionIdGenerator)
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
    public override bool IsSupportOrderBookIncrements => false;

    /// <inheritdoc />
    public override bool IsReplaceCommandEditCurrent => false;

    /// <inheritdoc />
    public override bool IsSupportTransactionLog => true;

    /// <inheritdoc />
    public override string[] AssociatedBoards => [BoardCodes.Korbit];

    /// <inheritdoc />
    protected override bool ValidateSecurityId(SecurityId securityId)
        => securityId.BoardCode.IsEmpty() ||
            securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Korbit) ||
            securityId.IsAssociated(BoardCodes.Korbit);

    private KorbitRestClient RestClient
        => _restClient ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private KorbitSocketClient PublicSocketClient
        => _publicSocketClient ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private void EnsureConnected()
    {
        if (_restClient is null || _publicSocketClient is null)
            throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
    }

    private void EnsurePrivateReady()
    {
        EnsureConnected();
        if (!RestClient.IsCredentialsAvailable || _privateSocketClient is null)
            throw new InvalidOperationException(
                "Korbit API key and HMAC secret are required for private operations.");
    }

    private void RegisterMarkets(IEnumerable<KorbitTradingPair> markets)
    {
        using (_sync.EnterScope())
        {
            _markets.Clear();
            foreach (var market in markets ?? [])
            {
                if (market?.Symbol.IsEmpty() != false)
                    continue;
                var symbol = market.Symbol.NormalizeSymbol();
                var (BaseCurrency, QuoteCurrency) = symbol.SplitSymbol();
                _markets[symbol] = new()
                {
                    Symbol = symbol,
                    BaseCurrency = BaseCurrency,
                    QuoteCurrency = QuoteCurrency,
                    Status = market.Status,
                };
            }
        }
    }

    private MarketDefinition GetMarket(SecurityId securityId)
    {
        if (!securityId.BoardCode.IsEmpty() &&
            !securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Korbit) &&
            !securityId.IsAssociated(BoardCodes.Korbit))
            throw new InvalidOperationException(
                $"Security board '{securityId.BoardCode}' is not Korbit.");
        return GetMarket(securityId.SecurityCode);
    }

    private MarketDefinition GetMarket(string symbol)
    {
        symbol = symbol.NormalizeSymbol();
        using (_sync.EnterScope())
        {
            if (_markets.TryGetValue(symbol, out var market))
                return market;
            var compact = symbol.CompactSymbol();
            market = _markets.Values.FirstOrDefault(value =>
                value.Symbol.CompactSymbol().Equals(compact,
                    StringComparison.OrdinalIgnoreCase));
            return market ?? throw new InvalidOperationException(
                $"Unknown Korbit market '{symbol}'.");
        }
    }

    private async ValueTask<KorbitTickSizePolicy> GetTickSizePolicyAsync(
        MarketDefinition market, CancellationToken cancellationToken)
    {
        using (_sync.EnterScope())
            if (market.TickSizePolicy is not null)
                return market.TickSizePolicy;
        var response = await RestClient.GetTickSizePolicyAsync(new()
        {
            Symbol = market.Symbol,
        }, cancellationToken);
        var policy = response?.FirstOrDefault(value =>
            value?.Symbol.EqualsIgnoreCase(market.Symbol) == true) ??
            throw new InvalidDataException(
                $"Korbit returned no tick-size policy for '{market.Symbol}'.");
        using (_sync.EnterScope())
            return market.TickSizePolicy ??= policy;
    }

    private async ValueTask AcquireStreamAsync(StreamTypes type, string symbol,
        CancellationToken cancellationToken)
    {
        var key = new StreamKey(type, symbol.NormalizeSymbol());
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
            switch (type)
            {
                case StreamTypes.Ticker:
                    await PublicSocketClient.SubscribeTickerAsync(key.Symbol,
                        cancellationToken);
                    break;
                case StreamTypes.OrderBook:
                    await PublicSocketClient.SubscribeOrderBookAsync(key.Symbol,
                        null, cancellationToken);
                    break;
                case StreamTypes.Trade:
                    await PublicSocketClient.SubscribeTradesAsync(key.Symbol,
                        cancellationToken);
                    break;
            }
        }
        catch
        {
            using (_sync.EnterScope())
                _streamReferences.Remove(key);
            throw;
        }
    }

    private async ValueTask ReleaseStreamAsync(StreamTypes type, string symbol,
        CancellationToken cancellationToken)
    {
        var key = new StreamKey(type, symbol.NormalizeSymbol());
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
        if (!unsubscribe || _publicSocketClient is null)
            return;
        switch (type)
        {
            case StreamTypes.Ticker:
                await _publicSocketClient.UnsubscribeTickerAsync(key.Symbol,
                    cancellationToken);
                break;
            case StreamTypes.OrderBook:
                await _publicSocketClient.UnsubscribeOrderBookAsync(key.Symbol,
                    null, cancellationToken);
                break;
            case StreamTypes.Trade:
                await _publicSocketClient.UnsubscribeTradesAsync(key.Symbol,
                    cancellationToken);
                break;
        }
    }

    private string GetPortfolioName() =>
        $"Korbit_{Key.ToId()}_{AccountSequence}";

    private void TrackOrder(TrackedOrder order, params string[] identifiers)
    {
        if (order is null)
            return;
        using (_sync.EnterScope())
        {
            foreach (var identifier in identifiers.Where(static value =>
                !value.IsEmpty()))
                _trackedOrders[identifier] = order;
            if (order.ExchangeOrderId > 0)
                _trackedOrders[order.ExchangeOrderId.ToString(
                    CultureInfo.InvariantCulture)] = order;
            if (!order.ClientOrderId.IsEmpty())
                _trackedOrders[order.ClientOrderId] = order;
        }
    }

    private TrackedOrder GetTrackedOrder(string identifier)
    {
        if (identifier.IsEmpty())
            return null;
        using (_sync.EnterScope())
            return _trackedOrders.TryGetValue(identifier, out var order)
                ? order
                : null;
    }

    private string GetClientOrderId(long transactionId, string userOrderId)
    {
        using (_sync.EnterScope())
        {
            if (_clientOrderIds.TryGetValue(transactionId, out var existing))
                return existing;
            var isGenerated = userOrderId.IsEmpty();
            var value = isGenerated
                ? $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}"
                : userOrderId.Trim();
            if (isGenerated && value.Length > 36)
                value = value[..36];
            value = value.ValidateClientOrderId();
            _clientOrderIds.Add(transactionId, value);
            return value;
        }
    }

    private bool AddPublicTrade(string symbol, long tradeId)
    {
        if (tradeId <= 0)
            return false;
        using (_sync.EnterScope())
        {
            if (_seenPublicTrades.Count > 100000)
                _seenPublicTrades.Clear();
            return _seenPublicTrades.Add($"{symbol}:{tradeId}");
        }
    }

    private bool AddAccountTrade(string symbol, long tradeId)
    {
        if (tradeId <= 0)
            return false;
        using (_sync.EnterScope())
        {
            if (_seenAccountTrades.Count > 100000)
                _seenAccountTrades.Clear();
            return _seenAccountTrades.Add($"{symbol}:{tradeId}");
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
            _trackedOrders.Clear();
            _clientOrderIds.Clear();
            _seenPublicTrades.Clear();
            _seenAccountTrades.Clear();
            _portfolioSubscriptions.Clear();
            _orderSubscriptions.Clear();
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
            $"Korbit {operation} requires an exchange or client order ID.");
    }

    /// <inheritdoc />
    protected override void DisposeManaged()
    {
        DisposeClients();
        base.DisposeManaged();
    }
}
