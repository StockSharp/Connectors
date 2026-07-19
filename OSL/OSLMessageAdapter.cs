namespace StockSharp.OSL;

public partial class OSLMessageAdapter
{
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
        public string OrderId { get; init; }
        public string ClientOrderId { get; init; }
        public string Symbol { get; init; }
        public Sides? Side { get; init; }
        public DateTime? From { get; init; }
        public DateTime? To { get; init; }
        public int Maximum { get; init; }
        public string[] StreamSymbols { get; set; }
    }

    private sealed class TrackedOrder
    {
        public long TransactionId { get; init; }
        public string OrderId { get; set; }
        public string ClientOrderId { get; set; }
        public string Symbol { get; init; }
        public Sides Side { get; init; }
        public OrderTypes OrderType { get; init; }
        public decimal Volume { get; init; }
        public decimal Price { get; init; }
        public OSLOrderCondition Condition { get; init; }
        public OrderStates State { get; set; }
    }

    private readonly record struct CandleStreamKey(string Symbol,
        TimeSpan TimeFrame);
    private readonly record struct BalanceFingerprint(decimal Available,
        decimal Frozen, decimal Locked);
    private readonly record struct OrderFingerprint(string Status,
        decimal? Quantity, decimal? Executed, decimal? Price,
        decimal? AveragePrice);
    private readonly record struct TradeDeliveryKey(long TargetId,
        string TradeId);

    private readonly Lock _sync = new();
    private readonly Dictionary<string, OSLSymbol> _symbols =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, MarketSubscription>
        _level1Subscriptions = [];
    private readonly Dictionary<long, DepthSubscription>
        _depthSubscriptions = [];
    private readonly Dictionary<long, MarketSubscription>
        _tickSubscriptions = [];
    private readonly Dictionary<long, CandleSubscription>
        _candleSubscriptions = [];
    private readonly Dictionary<OSLSubscriptionKey, int>
        _publicStreamReferences = [];
    private readonly Dictionary<OSLSubscriptionKey, int>
        _privateStreamReferences = [];
    private readonly Dictionary<CandleStreamKey, int>
        _candleStreamReferences = [];
    private readonly HashSet<long> _portfolioSubscriptions = [];
    private readonly Dictionary<long, OrderSubscription>
        _orderSubscriptions = [];
    private readonly Dictionary<string, TrackedOrder> _trackedOrders =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _transactionSymbols =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenPublicTrades =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<TradeDeliveryKey> _seenAccountTrades = [];
    private readonly Dictionary<string, BalanceFingerprint>
        _balanceFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OrderFingerprint>
        _orderFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private OSLRestClient _restClient;
    private OSLSocketClient _publicSocket;
    private OSLSocketClient _privateSocket;
    private OSLSocketClient _candleSocket;
    private bool _isTransactionFillSubscribed;
    private DateTime _lastPing;

    /// <summary>
    /// Initializes a new instance of the <see cref="OSLMessageAdapter"/>
    /// class.
    /// </summary>
    public OSLMessageAdapter(IdGenerator transactionIdGenerator)
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
    public override bool IsReplaceCommandEditCurrent => false;

    /// <inheritdoc />
    public override bool IsSupportTransactionLog => true;

    /// <inheritdoc />
    public override string[] AssociatedBoards => [BoardCodes.OSL];

    /// <inheritdoc />
    protected override bool ValidateSecurityId(SecurityId securityId)
        => securityId.BoardCode.IsEmpty() ||
            securityId.BoardCode.EqualsIgnoreCase(BoardCodes.OSL) ||
            securityId.IsAssociated(BoardCodes.OSL);

    private OSLRestClient RestClient => _restClient ?? throw new
        InvalidOperationException(LocalizedStrings.ConnectionNotOk);

    private OSLSocketClient PublicSocket => _publicSocket ?? throw new
        InvalidOperationException(LocalizedStrings.ConnectionNotOk);

    private OSLSocketClient PrivateSocket => _privateSocket ?? throw new
        InvalidOperationException(LocalizedStrings.ConnectionNotOk);

    private OSLSocketClient CandleSocket => _candleSocket ?? throw new
        InvalidOperationException(LocalizedStrings.ConnectionNotOk);

    private void EnsureConnected()
    {
        if (_restClient is null || _publicSocket is null ||
            _candleSocket is null)
            throw new InvalidOperationException(
                LocalizedStrings.ConnectionNotOk);
    }

    private void EnsurePrivateReady()
    {
        EnsureConnected();
        if (!RestClient.IsPrivateAvailable || _privateSocket is null)
            throw new InvalidOperationException(
                "OSL API key and secret are required for private operations.");
    }

    private string GetPortfolioName() => $"OSL_{Key.ToId()}";

    private void ValidatePortfolio(string portfolioName)
    {
        if (!portfolioName.IsEmpty() &&
            !portfolioName.EqualsIgnoreCase(GetPortfolioName()))
            throw new InvalidOperationException(
                $"Unknown OSL portfolio '{portfolioName}'.");
    }

    private void RegisterSymbols(IEnumerable<OSLSymbol> symbols)
    {
        using (_sync.EnterScope())
        {
            _symbols.Clear();
            foreach (var symbol in symbols ?? [])
                if (symbol?.Symbol.IsEmpty() == false &&
                    symbol.BaseCoin.IsEmpty() == false &&
                    symbol.QuoteCoin.IsEmpty() == false)
                    _symbols[symbol.Symbol.NormalizeSymbol()] = symbol;
        }
    }

    private OSLSymbol GetSymbol(SecurityId securityId)
    {
        if (!securityId.BoardCode.IsEmpty() &&
            !securityId.BoardCode.EqualsIgnoreCase(BoardCodes.OSL) &&
            !securityId.IsAssociated(BoardCodes.OSL))
            throw new InvalidOperationException(
                $"Security board '{securityId.BoardCode}' is not OSL.");
        var symbol = securityId.SecurityCode.NormalizeSymbol();
        using (_sync.EnterScope())
            return _symbols.TryGetValue(symbol, out var product)
                ? product
                : throw new InvalidOperationException(
                    $"Unknown OSL instrument '{symbol}'.");
    }

    private string[] GetOnlineSymbols()
    {
        using (_sync.EnterScope())
            return [.. _symbols.Values.Where(static value =>
                    value.Status.IsEmpty() ||
                    value.Status.EqualsIgnoreCase("online"))
                .Select(static value => value.Symbol.NormalizeSymbol())
                .OrderBy(static value => value,
                    StringComparer.OrdinalIgnoreCase)];
    }

    private async ValueTask AcquirePublicStreamAsync(
        OSLWsChannels channel, string selector,
        CancellationToken cancellationToken)
    {
        var key = new OSLSubscriptionKey(channel, selector.NormalizeSymbol());
        using (_sync.EnterScope())
        {
            if (_publicStreamReferences.TryGetValue(key, out var count))
            {
                _publicStreamReferences[key] = count + 1;
                return;
            }
            _publicStreamReferences.Add(key, 1);
        }
        try
        {
            await PublicSocket.SubscribeAsync(channel, key.Selector,
                cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _publicStreamReferences.Remove(key);
            throw;
        }
    }

    private async ValueTask ReleasePublicStreamAsync(OSLWsChannels channel,
        string selector, CancellationToken cancellationToken)
    {
        var key = new OSLSubscriptionKey(channel, selector.NormalizeSymbol());
        var release = false;
        using (_sync.EnterScope())
        {
            if (!_publicStreamReferences.TryGetValue(key, out var count))
                return;
            if (count > 1)
                _publicStreamReferences[key] = count - 1;
            else
            {
                _publicStreamReferences.Remove(key);
                release = true;
            }
        }
        if (release && _publicSocket is not null)
            await _publicSocket.UnsubscribeAsync(channel, key.Selector,
                cancellationToken);
    }

    private async ValueTask AcquirePrivateStreamAsync(
        OSLWsChannels channel, string selector,
        CancellationToken cancellationToken)
    {
        EnsurePrivateReady();
        selector = channel is OSLWsChannels.Fill or
            OSLWsChannels.SpotAssets
                ? "default"
                : selector.NormalizeSymbol();
        var key = new OSLSubscriptionKey(channel, selector);
        using (_sync.EnterScope())
        {
            if (_privateStreamReferences.TryGetValue(key, out var count))
            {
                _privateStreamReferences[key] = count + 1;
                return;
            }
            _privateStreamReferences.Add(key, 1);
        }
        try
        {
            await PrivateSocket.SubscribeAsync(channel, selector,
                cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _privateStreamReferences.Remove(key);
            throw;
        }
    }

    private async ValueTask ReleasePrivateStreamAsync(
        OSLWsChannels channel, string selector,
        CancellationToken cancellationToken)
    {
        selector = channel is OSLWsChannels.Fill or
            OSLWsChannels.SpotAssets
                ? "default"
                : selector.NormalizeSymbol();
        var key = new OSLSubscriptionKey(channel, selector);
        var release = false;
        using (_sync.EnterScope())
        {
            if (!_privateStreamReferences.TryGetValue(key, out var count))
                return;
            if (count > 1)
                _privateStreamReferences[key] = count - 1;
            else
            {
                _privateStreamReferences.Remove(key);
                release = true;
            }
        }
        if (release && _privateSocket is not null)
            await _privateSocket.UnsubscribeAsync(channel, selector,
                cancellationToken);
    }

    private async ValueTask AcquireCandleStreamAsync(string symbol,
        TimeSpan timeFrame, CancellationToken cancellationToken)
    {
        var key = new CandleStreamKey(symbol.NormalizeSymbol(), timeFrame);
        using (_sync.EnterScope())
        {
            if (_candleStreamReferences.TryGetValue(key, out var count))
            {
                _candleStreamReferences[key] = count + 1;
                return;
            }
            _candleStreamReferences.Add(key, 1);
        }
        try
        {
            await CandleSocket.SubscribeCandleAsync(key.Symbol,
                key.TimeFrame, cancellationToken);
        }
        catch
        {
            using (_sync.EnterScope())
                _candleStreamReferences.Remove(key);
            throw;
        }
    }

    private async ValueTask ReleaseCandleStreamAsync(string symbol,
        TimeSpan timeFrame, CancellationToken cancellationToken)
    {
        var key = new CandleStreamKey(symbol.NormalizeSymbol(), timeFrame);
        var release = false;
        using (_sync.EnterScope())
        {
            if (!_candleStreamReferences.TryGetValue(key, out var count))
                return;
            if (count > 1)
                _candleStreamReferences[key] = count - 1;
            else
            {
                _candleStreamReferences.Remove(key);
                release = true;
            }
        }
        if (release && _candleSocket is not null)
            await _candleSocket.UnsubscribeCandleAsync(key.Symbol,
                key.TimeFrame, cancellationToken);
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

    private TrackedOrder MatchTrackedOrder(OSLOrder order)
    {
        if (order is null)
            return null;
        var tracked = GetTrackedOrder(order.OrderId) ??
            GetTrackedOrder(order.EffectiveClientOrderId);
        if (tracked is not null)
            return tracked;
        using (_sync.EnterScope())
            return _trackedOrders.Values.Distinct().FirstOrDefault(value =>
                value.Symbol.EqualsIgnoreCase(order.EffectiveSymbol) &&
                (order.Side.IsEmpty() ||
                    order.Side.ToStockSharpSide() == value.Side) &&
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
            _symbols.Clear();
            _level1Subscriptions.Clear();
            _depthSubscriptions.Clear();
            _tickSubscriptions.Clear();
            _candleSubscriptions.Clear();
            _publicStreamReferences.Clear();
            _privateStreamReferences.Clear();
            _candleStreamReferences.Clear();
            _portfolioSubscriptions.Clear();
            _orderSubscriptions.Clear();
            _trackedOrders.Clear();
            _transactionSymbols.Clear();
            _seenPublicTrades.Clear();
            _seenAccountTrades.Clear();
            _balanceFingerprints.Clear();
            _orderFingerprints.Clear();
            _isTransactionFillSubscribed = false;
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
