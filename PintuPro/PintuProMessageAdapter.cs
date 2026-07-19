namespace StockSharp.PintuPro;

public partial class PintuProMessageAdapter
{
    private enum StreamTypes
    {
        Book,
        Trade,
    }

    private sealed class MarketDefinition
    {
        public PintuProSymbol Reference { get; init; }
        public string Symbol => Reference.Symbol;
        public string BaseCurrency => Reference.BaseAsset;
        public string QuoteCurrency => Reference.QuoteAsset;
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
        public string OrderIdentifier { get; init; }
        public Sides? Side { get; init; }
    }

    private sealed class TrackedOrder
    {
        public long TransactionId { get; init; }
        public string Symbol { get; init; }
        public string ExchangeOrderId { get; set; }
        public string ClientOrderId { get; init; }
        public Sides Side { get; init; }
        public OrderTypes OrderType { get; init; }
        public decimal Volume { get; init; }
        public decimal Price { get; init; }
        public TimeInForce TimeInForce { get; init; }
        public bool IsPostOnly { get; init; }
        public decimal? QuoteAmount { get; init; }
    }

    private readonly record struct StreamKey(StreamTypes Type, string Symbol);

    private readonly Lock _sync = new();
    private readonly Dictionary<string, MarketDefinition> _markets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, MarketSubscription> _level1Subscriptions =
        [];
    private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
    private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
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
    private PintuProRestClient _restClient;
    private PintuProSocketClient _socketClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="PintuProMessageAdapter"/>
    /// class.
    /// </summary>
    public PintuProMessageAdapter(IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        HeartbeatInterval = TimeSpan.FromSeconds(10);
        this.AddMarketDataSupport();
        this.AddTransactionalSupport();
        this.AddSupportedMarketDataType(DataType.Ticks);
        this.AddSupportedMarketDataType(DataType.MarketDepth);
        this.AddSupportedMarketDataType(DataType.Level1);
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities ||
            dataType == DataType.Transactions ||
            dataType == DataType.PositionChanges ||
            base.IsAllDownloadingSupported(dataType);

    /// <inheritdoc />
    public override bool IsSupportOrderBookIncrements => false;

    /// <inheritdoc />
    public override bool IsReplaceCommandEditCurrent => false;

    /// <inheritdoc />
    public override bool IsSupportTransactionLog => true;

    /// <inheritdoc />
    public override string[] AssociatedBoards => [BoardCodes.PintuPro];

    /// <inheritdoc />
    protected override bool ValidateSecurityId(SecurityId securityId)
        => securityId.BoardCode.IsEmpty() ||
            securityId.BoardCode.EqualsIgnoreCase(BoardCodes.PintuPro) ||
            securityId.IsAssociated(BoardCodes.PintuPro);

    private PintuProRestClient RestClient
        => _restClient ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private PintuProSocketClient SocketClient
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
                "Pintu Pro API key and HMAC secret are required for private operations.");
    }

    private void RegisterMarkets(IEnumerable<PintuProSymbol> markets)
    {
        using (_sync.EnterScope())
        {
            _markets.Clear();
            foreach (var market in markets ?? [])
            {
                if (market?.Symbol.IsEmpty() != false)
                    continue;
                market.Symbol = market.Symbol.NormalizeSymbol();
                market.BaseAsset = market.BaseAsset.IsEmpty()
                    ? market.Symbol.SplitSymbol().BaseCurrency
                    : market.BaseAsset.NormalizeCurrency();
                market.QuoteAsset = market.QuoteAsset.IsEmpty()
                    ? market.Symbol.SplitSymbol().QuoteCurrency
                    : market.QuoteAsset.NormalizeCurrency();
                _markets[market.Symbol] = new() { Reference = market };
            }
        }
    }

    private MarketDefinition GetMarket(SecurityId securityId)
    {
        if (!securityId.BoardCode.IsEmpty() &&
            !securityId.BoardCode.EqualsIgnoreCase(BoardCodes.PintuPro) &&
            !securityId.IsAssociated(BoardCodes.PintuPro))
            throw new InvalidOperationException(
                $"Security board '{securityId.BoardCode}' is not Pintu Pro.");
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
                $"Unknown Pintu Pro market '{symbol}'.");
        }
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
            if (type == StreamTypes.Book)
                await SocketClient.SubscribeBookAsync(key.Symbol, true,
                    cancellationToken);
            else
                await SocketClient.SubscribeTradesAsync(key.Symbol, true,
                    cancellationToken);
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
        if (!unsubscribe || _socketClient is null)
            return;
        if (type == StreamTypes.Book)
            await _socketClient.SubscribeBookAsync(key.Symbol, false,
                cancellationToken);
        else
            await _socketClient.SubscribeTradesAsync(key.Symbol, false,
                cancellationToken);
    }

    private string GetPortfolioName() => $"PintuPro_{Key.ToId()}";

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
            var value = userOrderId.IsEmpty()
                ? $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}"
                : userOrderId.Trim();
            value = value.ValidateClientOrderId();
            _clientOrderIds.Add(transactionId, value);
            return value;
        }
    }

    private bool AddPublicTrade(string identifier)
    {
        if (identifier.IsEmpty())
            return false;
        using (_sync.EnterScope())
        {
            if (_seenPublicTrades.Count > 100000)
                _seenPublicTrades.Clear();
            return _seenPublicTrades.Add(identifier);
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
            $"Pintu Pro {operation} requires an exchange order ID.");
    }

    /// <inheritdoc />
    protected override void DisposeManaged()
    {
        DisposeClients();
        base.DisposeManaged();
    }
}
