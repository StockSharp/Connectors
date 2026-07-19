namespace StockSharp.Tapbit;

public partial class TapbitMessageAdapter
{
    private const int _maximumDeliveryKeys = 100_000;

    private class MarketSubscription
    {
        public TapbitProductTypes ProductType { get; init; }
        public string Symbol { get; init; }
        public string StreamSymbol { get; init; }
    }

    private sealed class DepthSubscription : MarketSubscription
    {
        public int Depth { get; init; }
        public string Topic { get; init; }
    }

    private sealed class TickSubscription : MarketSubscription
    {
        public DateTime? From { get; init; }
        public DateTime? To { get; init; }
    }

    private sealed class CandleSubscription : MarketSubscription
    {
        public TimeSpan TimeFrame { get; init; }
        public DateTime? To { get; init; }
    }

    private sealed class OrderSubscription
    {
        public string Symbol { get; init; }
        public string OrderId { get; init; }
        public Sides? Side { get; init; }
        public decimal? Volume { get; init; }
        public OrderStates[] States { get; init; }
        public DateTime? From { get; init; }
        public DateTime? To { get; init; }
        public int Skip { get; init; }
        public int Maximum { get; init; }
    }

    private sealed class TrackedOrder
    {
        public long TransactionId { get; init; }
        public string OrderId { get; init; }
        public string Symbol { get; init; }
        public Sides Side { get; init; }
        public decimal Volume { get; init; }
        public decimal Price { get; init; }
        public OrderStates State { get; set; }
    }

    private sealed class OrderBookState
    {
        public SortedDictionary<decimal, decimal> Bids { get; } =
            new(Comparer<decimal>.Create(static (left, right) =>
                right.CompareTo(left)));
        public SortedDictionary<decimal, decimal> Asks { get; } = [];
        public long Version { get; set; }
        public bool IsInitialized { get; set; }
    }

    private readonly record struct ProductKey(TapbitProductTypes ProductType,
        string Symbol);
    private readonly record struct TradeDeliveryKey(long SubscriptionId,
        string Identity);
    private readonly record struct OrderFingerprint(TapbitOrderStatuses Status,
        decimal Filled, decimal? AveragePrice, long OrderTime);
    private readonly record struct BalanceFingerprint(decimal Current,
        decimal Available, decimal Blocked);
    private readonly record struct CandleFingerprint(decimal Open,
        decimal High, decimal Low, decimal Close, decimal Volume);

    private readonly Lock _sync = new();
    private readonly Dictionary<ProductKey, TapbitInstrument> _products = [];
    private readonly Dictionary<ProductKey, TapbitInstrument>
        _streamProducts = [];
    private readonly Dictionary<long, MarketSubscription>
        _level1Subscriptions = [];
    private readonly Dictionary<long, DepthSubscription>
        _depthSubscriptions = [];
    private readonly Dictionary<long, TickSubscription>
        _tickSubscriptions = [];
    private readonly Dictionary<long, CandleSubscription>
        _candleSubscriptions = [];
    private readonly Dictionary<string, OrderBookState> _books =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<long> _portfolioSubscriptions = [];
    private readonly Dictionary<long, OrderSubscription>
        _orderSubscriptions = [];
    private readonly Dictionary<string, TrackedOrder> _trackedOrders =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _transactionSymbols =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<TradeDeliveryKey> _seenPublicTrades = [];
    private readonly Queue<TradeDeliveryKey> _publicTradeDeliveryOrder = [];
    private readonly Dictionary<string, OrderFingerprint> _orderFingerprints =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BalanceFingerprint>
        _balanceFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CandleFingerprint>
        _candleFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private TapbitRestClient _restClient;
    private TapbitSocketClient _socketClient;
    private DateTime _nextMarketPoll;
    private DateTime _nextPrivatePoll;

    /// <summary>
    /// Initializes a new instance of the <see cref="TapbitMessageAdapter"/>.
    /// </summary>
    public TapbitMessageAdapter(IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        HeartbeatInterval = TimeSpan.FromSeconds(1);
        this.AddMarketDataSupport();
        this.AddTransactionalSupport();
        this.AddSupportedMarketDataType(DataType.Ticks);
        this.AddSupportedMarketDataType(DataType.MarketDepth);
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedCandleTimeFrames(AllTimeFrames);
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities ||
            dataType == DataType.PositionChanges ||
            base.IsAllDownloadingSupported(dataType);

    /// <inheritdoc />
    public override bool IsSupportCandlesUpdates(MarketDataMessage subscription)
        => true;

    /// <inheritdoc />
    public override IEnumerable<int> SupportedOrderBookDepths { get; } =
        [5, 10, 50, 100, 200];

    /// <inheritdoc />
    public override string[] AssociatedBoards =>
        [BoardCodes.Tapbit, BoardCodes.TapbitFutures];

    /// <inheritdoc />
    protected override bool ValidateSecurityId(SecurityId securityId)
        => securityId.BoardCode.IsEmpty() || AssociatedBoards.Any(board =>
            securityId.BoardCode.EqualsIgnoreCase(board) ||
            securityId.IsAssociated(board));

    private TapbitRestClient RestClient => _restClient ?? throw new
        InvalidOperationException(LocalizedStrings.ConnectionNotOk);

    private TapbitSocketClient SocketClient => _socketClient ?? throw new
        InvalidOperationException(LocalizedStrings.ConnectionNotOk);

    private void EnsureConnected()
    {
        if (_restClient is null || _socketClient is null)
            throw new InvalidOperationException(
                LocalizedStrings.ConnectionNotOk);
    }

    private void EnsurePrivateReady()
    {
        EnsureConnected();
        if (!RestClient.IsPrivateAvailable)
            throw new InvalidOperationException(
                "Tapbit API key and secret are required for private Spot " +
                "operations.");
    }

    private string GetPortfolioName() => $"Tapbit_{Key.ToId()}";

    private void ValidatePortfolio(string portfolioName)
    {
        if (!portfolioName.IsEmpty() &&
            !portfolioName.EqualsIgnoreCase(GetPortfolioName()))
            throw new InvalidOperationException(
                $"Unknown Tapbit portfolio '{portfolioName}'.");
    }

    private TapbitInstrument GetProduct(SecurityId securityId)
    {
        if (!ValidateSecurityId(securityId))
            throw new InvalidOperationException(
                $"Security board '{securityId.BoardCode}' is not Tapbit.");
        var symbol = securityId.SecurityCode.NormalizeTapbitSymbol();
        using (_sync.EnterScope())
        {
            if (!securityId.BoardCode.IsEmpty())
            {
                var productType = securityId.BoardCode
                    .ToTapbitProductType();
                if (_products.TryGetValue(new(productType, symbol),
                    out var product))
                    return product;
            }
            var matches = _products.Where(pair =>
                pair.Key.Symbol.EqualsIgnoreCase(symbol)).Select(
                static pair => pair.Value).Take(2).ToArray();
            return matches.Length switch
            {
                1 => matches[0],
                0 => throw new InvalidOperationException(
                    $"Unknown Tapbit instrument '{symbol}'."),
                _ => throw new InvalidOperationException(
                    $"Tapbit instrument '{symbol}' exists in multiple " +
                    "sections; specify its board code."),
            };
        }
    }

    private TapbitInstrument GetStreamProduct(
        TapbitProductTypes productType, string streamSymbol)
    {
        streamSymbol = streamSymbol.NormalizeTapbitSymbol();
        using (_sync.EnterScope())
            return _streamProducts.TryGetValue(new(productType, streamSymbol),
                out var product)
                ? product
                : null;
    }

    private void RegisterProducts(IEnumerable<TapbitInstrument> products)
    {
        using (_sync.EnterScope())
        {
            foreach (var product in products ?? [])
            {
                if (product?.Symbol.IsEmpty() != false ||
                    product.StreamSymbol.IsEmpty())
                    continue;
                _products[new(product.ProductType,
                    product.Symbol.NormalizeTapbitSymbol())] = product;
                _streamProducts[new(product.ProductType,
                    product.StreamSymbol.NormalizeTapbitSymbol())] = product;
            }
        }
    }

    private static int NormalizeStreamDepth(int? requested)
    {
        var depth = requested ?? 50;
        return depth <= 5 ? 5 : depth <= 10 ? 10 : depth <= 50 ? 50
            : depth <= 100 ? 100 : 200;
    }

    private static string GetTradeIdentity(TapbitPublicTrade trade,
        int occurrence)
        => string.Join(':',
            trade.Timestamp.ToString(CultureInfo.InvariantCulture),
            trade.Price.ToString(CultureInfo.InvariantCulture),
            trade.Volume.ToString(CultureInfo.InvariantCulture),
            trade.Side.ToString(),
            occurrence.ToString(CultureInfo.InvariantCulture));

    /// <inheritdoc />
    protected override void DisposeManaged()
    {
        DisposeClients();
        base.DisposeManaged();
    }
}
