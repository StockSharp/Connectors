namespace StockSharp.GmoCoin;

public partial class GmoCoinMessageAdapter
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
        public string BaseAsset { get; init; }
        public string QuoteAsset { get; init; }
        public bool IsMargin { get; init; }
        public decimal MinimumOrderSize { get; init; }
        public decimal MaximumOrderSize { get; init; }
        public decimal SizeStep { get; init; }
        public decimal TickSize { get; init; }
        public decimal TakerFee { get; init; }
        public decimal MakerFee { get; init; }
    }

    private class MarketSubscription
    {
        public string Symbol { get; init; }
    }

    private sealed class DepthSubscription : MarketSubscription
    {
        public int Depth { get; init; }
    }

    private sealed class OrderSubscription
    {
        public string Symbol { get; init; }
        public long? OrderId { get; init; }
        public Sides? Side { get; init; }
    }

    private sealed class TrackedOrder
    {
        public long TransactionId { get; init; }
        public string Symbol { get; init; }
        public long ExchangeOrderId { get; set; }
        public Sides Side { get; init; }
        public OrderTypes OrderType { get; init; }
        public decimal Volume { get; init; }
        public decimal Price { get; set; }
        public bool? IsPostOnly { get; init; }
        public TimeInForce? TimeInForce { get; init; }
        public GmoCoinOrderCondition Condition { get; init; }
    }

    private readonly record struct StreamKey(StreamTypes Type, string Symbol);

    private readonly Lock _sync = new();
    private readonly Dictionary<string, MarketDefinition> _markets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
    private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
    private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
    private readonly Dictionary<StreamKey, int> _streamReferences = [];
    private readonly Dictionary<string, TrackedOrder> _trackedOrders =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenPublicTrades =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<long> _seenAccountTrades = [];
    private readonly HashSet<long> _portfolioSubscriptions = [];
    private readonly Dictionary<long, OrderSubscription> _orderSubscriptions = [];
    private GmoCoinServiceStatuses _serviceStatus;
    private GmoCoinRestClient _restClient;
    private GmoCoinSocketClient _publicSocketClient;
    private GmoCoinSocketClient _privateSocketClient;
    private string _webSocketToken;

    /// <summary>
    /// Initializes a new instance of the <see cref="GmoCoinMessageAdapter"/> class.
    /// </summary>
    public GmoCoinMessageAdapter(IdGenerator transactionIdGenerator)
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
    public override bool IsReplaceCommandEditCurrent => true;

    /// <inheritdoc />
    public override bool IsSupportTransactionLog => true;

    /// <inheritdoc />
    public override string[] AssociatedBoards => [BoardCodes.GmoCoin];

    /// <inheritdoc />
    protected override bool ValidateSecurityId(SecurityId securityId)
        => securityId.BoardCode.IsEmpty() ||
            securityId.BoardCode.EqualsIgnoreCase(BoardCodes.GmoCoin) ||
            securityId.IsAssociated(BoardCodes.GmoCoin);

    private GmoCoinRestClient RestClient
        => _restClient ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private GmoCoinSocketClient PublicSocketClient
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
                "GMO Coin API key and secret are required for private operations.");
    }

    private void RegisterMarkets(IEnumerable<GmoCoinSymbol> symbols)
    {
        using (_sync.EnterScope())
        {
            _markets.Clear();
            foreach (var value in symbols ?? [])
            {
                if (value?.Symbol.IsEmpty() != false)
                    continue;
                var symbol = value.Symbol.NormalizeSymbol();
                var isMargin = symbol.EndsWith("_JPY",
                    StringComparison.OrdinalIgnoreCase);
                var definition = new MarketDefinition
                {
                    Symbol = symbol,
                    BaseAsset = isMargin ? symbol[..^4] : symbol,
                    QuoteAsset = "JPY",
                    IsMargin = isMargin,
                    MinimumOrderSize = value.MinimumOrderSize,
                    MaximumOrderSize = value.MaximumOrderSize,
                    SizeStep = value.SizeStep,
                    TickSize = value.TickSize,
                    TakerFee = value.TakerFee,
                    MakerFee = value.MakerFee,
                };
                _markets[definition.Symbol] = definition;
            }
        }
    }

    private MarketDefinition GetMarket(SecurityId securityId)
    {
        if (!securityId.BoardCode.IsEmpty() &&
            !securityId.BoardCode.EqualsIgnoreCase(BoardCodes.GmoCoin) &&
            !securityId.IsAssociated(BoardCodes.GmoCoin))
            throw new InvalidOperationException(
                $"Security board '{securityId.BoardCode}' is not GMO Coin.");
        return GetMarket(securityId.SecurityCode);
    }

    private MarketDefinition GetMarket(string symbol)
    {
        symbol = symbol.NormalizeSymbol();
        using (_sync.EnterScope())
            return _markets.TryGetValue(symbol, out var market)
                ? market
                : throw new InvalidOperationException(
                    $"Unknown GMO Coin market '{symbol}'.");
    }

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

    private string GetPortfolioName() => $"GmoCoin_{Key.ToId()}";

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
        }
    }

    private TrackedOrder GetTrackedOrder(long orderId)
        => orderId > 0 ? GetTrackedOrder(orderId.ToString(
            CultureInfo.InvariantCulture)) : null;

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

    private bool AddAccountTrade(long tradeId)
    {
        if (tradeId <= 0)
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
            _streamReferences.Clear();
            _trackedOrders.Clear();
            _seenPublicTrades.Clear();
            _seenAccountTrades.Clear();
            _portfolioSubscriptions.Clear();
            _orderSubscriptions.Clear();
        }
    }

    private static long ResolveOrderIdentifier(long? numericOrderId,
        string stringOrderId, string operation)
    {
        if (numericOrderId is > 0)
            return numericOrderId.Value;
        if (!stringOrderId.IsEmpty() && long.TryParse(stringOrderId,
            NumberStyles.None, CultureInfo.InvariantCulture, out var orderId) &&
            orderId > 0)
            return orderId;
        throw new InvalidOperationException(
            $"GMO Coin {operation} requires a numeric exchange order ID.");
    }

    /// <inheritdoc />
    protected override void DisposeManaged()
    {
        DisposeClients();
        base.DisposeManaged();
    }
}
