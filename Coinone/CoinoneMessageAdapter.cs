namespace StockSharp.Coinone;

public partial class CoinoneMessageAdapter
{
    private enum StreamTypes
    {
        Ticker,
        OrderBook,
        Trade,
        Candle,
    }

    private sealed class MarketDefinition
    {
        public string Symbol { get; init; }
        public string QuoteCurrency { get; init; }
        public string TargetCurrency { get; init; }
        public decimal PriceStep { get; init; }
        public decimal QuantityStep { get; init; }
        public decimal MinimumQuantity { get; init; }
        public decimal MaximumQuantity { get; init; }
        public decimal MinimumPrice { get; init; }
        public decimal MaximumPrice { get; init; }
        public decimal MinimumOrderAmount { get; init; }
        public decimal MaximumOrderAmount { get; init; }
        public CoinoneMaintenanceStatuses MaintenanceStatus { get; init; }
        public CoinoneTradeStatuses TradeStatus { get; init; }
        public HashSet<CoinoneOrderTypes> OrderTypes { get; init; }
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
    }

    private sealed class OrderSubscription
    {
        public string Symbol { get; init; }
        public string OrderId { get; init; }
        public Sides? Side { get; init; }
    }

    private sealed class TrackedOrder
    {
        public long TransactionId { get; init; }
        public string Symbol { get; init; }
        public string ExchangeOrderId { get; set; }
        public string UserOrderId { get; init; }
        public Sides Side { get; init; }
        public OrderTypes OrderType { get; init; }
        public decimal Volume { get; init; }
        public decimal Price { get; init; }
        public bool? IsPostOnly { get; init; }
        public CoinoneOrderCondition Condition { get; init; }
    }

    private readonly record struct StreamKey(StreamTypes Type, string Symbol,
        TimeSpan TimeFrame);

    private readonly Lock _sync = new();
    private readonly Dictionary<string, MarketDefinition> _markets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
    private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
    private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
    private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
    private readonly Dictionary<StreamKey, int> _streamReferences = [];
    private readonly Dictionary<string, TrackedOrder> _trackedOrders =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenPublicTrades =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenAccountTrades =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<long> _portfolioSubscriptions = [];
    private readonly Dictionary<long, OrderSubscription> _orderSubscriptions = [];
    private CoinoneRestClient _restClient;
    private CoinoneSocketClient _publicSocketClient;
    private CoinoneSocketClient _privateSocketClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="CoinoneMessageAdapter"/> class.
    /// </summary>
    public CoinoneMessageAdapter(IdGenerator transactionIdGenerator)
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
    public override string[] AssociatedBoards => [BoardCodes.Coinone];

    /// <inheritdoc />
    protected override bool ValidateSecurityId(SecurityId securityId)
        => securityId.BoardCode.IsEmpty() ||
            securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Coinone) ||
            securityId.IsAssociated(BoardCodes.Coinone);

    private CoinoneRestClient RestClient
        => _restClient ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private CoinoneSocketClient PublicSocketClient
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
                "Coinone access token and secret are required for private operations.");
    }

    private void RegisterMarkets(IEnumerable<CoinoneMarket> markets)
    {
        using (_sync.EnterScope())
        {
            _markets.Clear();
            foreach (var market in markets ?? [])
            {
                if (market?.TargetCurrency.IsEmpty() != false ||
                    market.QuoteCurrency.IsEmpty())
                    continue;
                var definition = new MarketDefinition
                {
                    Symbol = CoinoneExtensions.ToSymbol(market.TargetCurrency,
                        market.QuoteCurrency),
                    QuoteCurrency = market.QuoteCurrency.NormalizeCurrency(),
                    TargetCurrency = market.TargetCurrency.NormalizeCurrency(),
                    PriceStep = market.PriceUnit,
                    QuantityStep = market.QuantityUnit,
                    MinimumQuantity = market.MinimumQuantity,
                    MaximumQuantity = market.MaximumQuantity,
                    MinimumPrice = market.MinimumPrice,
                    MaximumPrice = market.MaximumPrice,
                    MinimumOrderAmount = market.MinimumOrderAmount,
                    MaximumOrderAmount = market.MaximumOrderAmount,
                    MaintenanceStatus = market.MaintenanceStatus,
                    TradeStatus = market.TradeStatus,
                    OrderTypes = [.. market.OrderTypes ?? []],
                };
                _markets[definition.Symbol] = definition;
            }
        }
    }

    private MarketDefinition GetMarket(SecurityId securityId)
    {
        if (!securityId.BoardCode.IsEmpty() &&
            !securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Coinone) &&
            !securityId.IsAssociated(BoardCodes.Coinone))
            throw new InvalidOperationException(
                $"Security board '{securityId.BoardCode}' is not Coinone.");
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
                $"Unknown Coinone market '{symbol}'.");
        }
    }

    private MarketDefinition GetMarket(string quoteCurrency,
        string targetCurrency)
        => GetMarket(CoinoneExtensions.ToSymbol(targetCurrency, quoteCurrency));

    private static bool AddReference(IDictionary<StreamKey, int> references,
        StreamKey key)
    {
        if (references.TryGetValue(key, out var count))
        {
            references[key] = count + 1;
            return false;
        }
        references.Add(key, 1);
        return true;
    }

    private static bool ReleaseReference(IDictionary<StreamKey, int> references,
        StreamKey key)
    {
        if (!references.TryGetValue(key, out var count))
            return false;
        if (count > 1)
        {
            references[key] = count - 1;
            return false;
        }
        references.Remove(key);
        return true;
    }

    private string GetPortfolioName() => $"Coinone_{Key.ToId()}";

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
            if (!order.UserOrderId.IsEmpty())
                _trackedOrders[order.UserOrderId] = order;
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

    private bool AddPublicTrade(string symbol, string tradeId)
    {
        if (tradeId.IsEmpty())
            return false;
        using (_sync.EnterScope())
        {
            if (_seenPublicTrades.Count > 100000)
                _seenPublicTrades.Clear();
            return _seenPublicTrades.Add($"{symbol}:{tradeId}");
        }
    }

    private bool AddAccountTrade(string tradeId)
    {
        if (tradeId.IsEmpty())
            return false;
        using (_sync.EnterScope())
        {
            if (_seenAccountTrades.Count > 100000)
                _seenAccountTrades.Clear();
            return _seenAccountTrades.Add(tradeId);
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
            throw new InvalidOperationException(
                $"Coinone {operation} requires the UUID string order ID, not a numeric ID.");
        throw new InvalidOperationException(
            $"Coinone {operation} requires an exchange order ID.");
    }

    /// <inheritdoc />
    protected override void DisposeManaged()
    {
        DisposeClients();
        base.DisposeManaged();
    }
}
