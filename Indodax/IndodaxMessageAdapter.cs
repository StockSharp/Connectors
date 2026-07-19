namespace StockSharp.Indodax;

public partial class IndodaxMessageAdapter
{
    private enum StreamTypes
    {
        Book,
        Trades,
    }

    private sealed class MarketDefinition
    {
        public IndodaxPair Pair { get; init; }
        public string PairId => Pair.Id;
        public string TapiPair => Pair.TickerId;
        public string Symbol => Pair.Symbol;
        public string BaseCurrency => Pair.BaseCurrency;
        public string QuoteCurrency => Pair.QuoteCurrency;
    }

    private class MarketSubscription
    {
        public string PairId { get; init; }
    }

    private sealed class DepthSubscription : MarketSubscription
    {
        public int Depth { get; init; }
    }

    private sealed class OrderSubscription
    {
        public string PairId { get; init; }
        public string OrderIdentifier { get; init; }
        public Sides? Side { get; init; }
    }

    private sealed class TrackedOrder
    {
        public long TransactionId { get; init; }
        public string PairId { get; init; }
        public string ExchangeOrderId { get; set; }
        public string ClientOrderId { get; init; }
        public Sides Side { get; init; }
        public OrderTypes OrderType { get; init; }
        public decimal Volume { get; init; }
        public decimal Price { get; init; }
        public bool IsPostOnly { get; init; }
        public decimal? QuoteAmount { get; init; }
    }

    private readonly record struct StreamKey(StreamTypes Type, string PairId);

    private static readonly TimeSpan[] _timeFrames =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(4),
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(3),
        TimeSpan.FromDays(7),
    ];

    private readonly Lock _sync = new();
    private readonly Dictionary<string, MarketDefinition> _markets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, MarketSubscription> _level1Subscriptions =
        [];
    private readonly Dictionary<long, DepthSubscription> _depthSubscriptions =
        [];
    private readonly Dictionary<long, MarketSubscription> _tickSubscriptions =
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
    private readonly Dictionary<long, OrderSubscription> _orderSubscriptions =
        [];
    private IndodaxRestClient _restClient;
    private IndodaxSocketClient _socketClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="IndodaxMessageAdapter"/>
    /// class.
    /// </summary>
    public IndodaxMessageAdapter(IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        HeartbeatInterval = TimeSpan.FromSeconds(10);
        this.AddMarketDataSupport();
        this.AddTransactionalSupport();
        this.AddSupportedMarketDataType(DataType.Ticks);
        this.AddSupportedMarketDataType(DataType.MarketDepth);
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedCandleTimeFrames(_timeFrames);
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities ||
            dataType == DataType.PositionChanges ||
            base.IsAllDownloadingSupported(dataType);

    /// <inheritdoc />
    public override bool IsSupportOrderBookIncrements => false;

    /// <inheritdoc />
    public override bool IsReplaceCommandEditCurrent => false;

    /// <inheritdoc />
    public override bool IsSupportTransactionLog => true;

    /// <inheritdoc />
    public override string[] AssociatedBoards => [BoardCodes.Indodax];

    /// <inheritdoc />
    protected override bool ValidateSecurityId(SecurityId securityId)
        => securityId.BoardCode.IsEmpty() ||
            securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Indodax) ||
            securityId.IsAssociated(BoardCodes.Indodax);

    private IndodaxRestClient RestClient
        => _restClient ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private IndodaxSocketClient SocketClient
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
                "Indodax TAPI key and secret are required for private operations.");
    }

    private void RegisterMarkets(IEnumerable<IndodaxPair> markets)
    {
        using (_sync.EnterScope())
        {
            _markets.Clear();
            foreach (var pair in markets ?? [])
            {
                if (pair?.Id.IsEmpty() != false || pair.BaseCurrency.IsEmpty() ||
                    pair.QuoteCurrency.IsEmpty())
                    continue;
                pair.Id = pair.Id.NormalizePairId();
                pair.Symbol = pair.Symbol.IsEmpty()
                    ? pair.Id.ToUpperInvariant()
                    : pair.Symbol.Trim().ToUpperInvariant();
                pair.TickerId = pair.TickerId.IsEmpty()
                    ? $"{pair.BaseCurrency}_{pair.QuoteCurrency}".ToLowerInvariant()
                    : pair.TickerId.Trim().ToLowerInvariant();
                pair.BaseCurrency = pair.BaseCurrency.Trim().ToLowerInvariant();
                pair.QuoteCurrency = pair.QuoteCurrency.Trim().ToLowerInvariant();
                _markets[pair.Id] = new() { Pair = pair };
            }
        }
    }

    private MarketDefinition GetMarket(SecurityId securityId)
    {
        if (!securityId.BoardCode.IsEmpty() &&
            !securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Indodax) &&
            !securityId.IsAssociated(BoardCodes.Indodax))
            throw new InvalidOperationException(
                $"Security board '{securityId.BoardCode}' is not Indodax.");
        return GetMarket(securityId.SecurityCode);
    }

    private MarketDefinition GetMarket(string value)
    {
        var normalized = value.NormalizePairId();
        using (_sync.EnterScope())
        {
            if (_markets.TryGetValue(normalized, out var market))
                return market;
            market = _markets.Values.FirstOrDefault(candidate =>
                candidate.TapiPair.NormalizePairId().EqualsIgnoreCase(normalized) ||
                candidate.Symbol.NormalizePairId().EqualsIgnoreCase(normalized));
            return market ?? throw new InvalidOperationException(
                $"Unknown Indodax market '{value}'.");
        }
    }

    private async ValueTask AcquireStreamAsync(StreamTypes type, string pairId,
        CancellationToken cancellationToken)
    {
        var key = new StreamKey(type, pairId.NormalizePairId());
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
                await SocketClient.SubscribeBookAsync(key.PairId, true,
                    cancellationToken);
            else
                await SocketClient.SubscribeTradesAsync(key.PairId, true,
                    cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _streamReferences.Remove(key);
            throw;
        }
    }

    private async ValueTask ReleaseStreamAsync(StreamTypes type, string pairId,
        CancellationToken cancellationToken)
    {
        var key = new StreamKey(type, pairId.NormalizePairId());
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
            await _socketClient.SubscribeBookAsync(key.PairId, false,
                cancellationToken);
        else
            await _socketClient.SubscribeTradesAsync(key.PairId, false,
                cancellationToken);
    }

    private string GetPortfolioName() => $"Indodax_{Key.ToId()}";

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
                ? $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}"[..36]
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

    private bool AddAccountTrade(string identifier)
    {
        if (identifier.IsEmpty())
            return false;
        using (_sync.EnterScope())
        {
            if (_seenAccountTrades.Count > 100000)
                _seenAccountTrades.Clear();
            return _seenAccountTrades.Add(identifier);
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
            $"Indodax {operation} requires an exchange order ID.");
    }

    /// <inheritdoc />
    protected override void DisposeManaged()
    {
        DisposeClients();
        base.DisposeManaged();
    }
}
