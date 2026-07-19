namespace StockSharp.BYDFi;

public partial class BYDFiMessageAdapter
{
    private class MarketSubscription
    {
        public string Symbol { get; init; }
    }

    private sealed class DepthSubscription : MarketSubscription
    {
        public int Depth { get; init; }
    }

    private sealed class TickSubscription : MarketSubscription
    {
        public string LastTradeId { get; set; }
    }

    private sealed class CandleSubscription : MarketSubscription
    {
        public TimeSpan TimeFrame { get; init; }
    }

    private sealed class OrderSubscription
    {
        public string Symbol { get; init; }
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
        public string OrderId { get; set; }
        public string ClientOrderId { get; init; }
        public string Symbol { get; init; }
        public Sides Side { get; init; }
        public OrderTypes OrderType { get; init; }
        public decimal Volume { get; init; }
        public BYDFiOrderCondition Condition { get; init; }
        public OrderStates State { get; set; }
    }

    private readonly record struct TradeDeliveryKey(long SubscriptionId,
        string TradeId);
    private readonly record struct OrderFingerprint(string Status,
        decimal? Filled, decimal? Price, decimal? AveragePrice,
        long UpdateTime);
    private readonly record struct BalanceFingerprint(decimal Current,
        decimal Available, decimal Frozen);
    private readonly record struct PositionFingerprint(decimal Current,
        decimal? AveragePrice, decimal? UnrealizedPnl,
        decimal? LiquidationPrice);

    private readonly Lock _sync = new();
    private readonly Dictionary<string, BYDFiProduct> _products =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, MarketSubscription>
        _level1Subscriptions = [];
    private readonly Dictionary<long, DepthSubscription>
        _depthSubscriptions = [];
    private readonly Dictionary<long, TickSubscription>
        _tickSubscriptions = [];
    private readonly Dictionary<long, CandleSubscription>
        _candleSubscriptions = [];
    private readonly HashSet<long> _portfolioSubscriptions = [];
    private readonly Dictionary<long, OrderSubscription>
        _orderSubscriptions = [];
    private readonly Dictionary<string, TrackedOrder> _trackedOrders =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _transactionSymbols =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<TradeDeliveryKey> _seenAccountTrades = [];
    private readonly HashSet<TradeDeliveryKey> _seenPublicTrades = [];
    private readonly Dictionary<string, OrderFingerprint> _orderFingerprints =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BalanceFingerprint>
        _balanceFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PositionFingerprint>
        _positionFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private BYDFiRestClient _restClient;
    private BYDFiWebSocketClient _socketClient;
    private DateTime _nextMarketPoll;
    private DateTime _nextPrivatePoll;

    /// <summary>
    /// Initializes a new instance of the <see cref="BYDFiMessageAdapter"/>.
    /// </summary>
    public BYDFiMessageAdapter(IdGenerator transactionIdGenerator)
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
            dataType == DataType.Transactions ||
            dataType == DataType.PositionChanges ||
            base.IsAllDownloadingSupported(dataType);

    /// <inheritdoc />
    public override bool IsSupportCandlesUpdates(MarketDataMessage subscription)
        => true;

    /// <inheritdoc />
    public override bool IsSupportTransactionLog => true;

    /// <inheritdoc />
    public override string[] AssociatedBoards => [BoardCodes.BYDFi];

    /// <inheritdoc />
    protected override bool ValidateSecurityId(SecurityId securityId)
        => securityId.BoardCode.IsEmpty() ||
            securityId.BoardCode.EqualsIgnoreCase(BoardCodes.BYDFi) ||
            securityId.IsAssociated(BoardCodes.BYDFi);

    private BYDFiRestClient RestClient => _restClient ?? throw new
        InvalidOperationException(LocalizedStrings.ConnectionNotOk);

    private BYDFiWebSocketClient SocketClient => _socketClient ?? throw new
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
                "BYDFi API key and secret are required for private operations.");
    }

    private string GetPortfolioName() => $"BYDFi_{Key.ToId()}_{Wallet}";

    private void ValidatePortfolio(string portfolioName)
    {
        if (!portfolioName.IsEmpty() &&
            !portfolioName.EqualsIgnoreCase(GetPortfolioName()))
            throw new InvalidOperationException(
                $"Unknown BYDFi portfolio '{portfolioName}'.");
    }

    private BYDFiProduct GetProduct(SecurityId securityId)
    {
        if (!ValidateSecurityId(securityId))
            throw new InvalidOperationException(
                $"Security board '{securityId.BoardCode}' is not BYDFi.");
        var symbol = securityId.SecurityCode.NormalizeSymbol();
        using (_sync.EnterScope())
            return _products.TryGetValue(symbol, out var product)
                ? product
                : throw new InvalidOperationException(
                    $"Unknown BYDFi instrument '{symbol}'.");
    }

    private void RegisterProducts(IEnumerable<BYDFiProduct> products)
    {
        using (_sync.EnterScope())
        {
            _products.Clear();
            foreach (var product in products ?? [])
                if (product?.Symbol.IsEmpty() == false &&
                    product.Status.EqualsIgnoreCase("NORMAL"))
                    _products[product.Symbol.NormalizeSymbol()] = product;
        }
    }

    private static string CreateClientOrderId(long transactionId,
        string userOrderId)
    {
        if (!userOrderId.IsEmpty() && userOrderId.Length <= 32 &&
            userOrderId.All(static ch => char.IsLetterOrDigit(ch) ||
                ch is '-' or '_'))
            return userOrderId;
        return $"ss{transactionId.ToString(CultureInfo.InvariantCulture)}";
    }

    private static long ParseTransactionId(string clientOrderId)
        => clientOrderId?.StartsWith("ss", StringComparison.OrdinalIgnoreCase)
            == true && long.TryParse(clientOrderId.AsSpan(2),
                NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                ? id
                : 0;

    private static int NormalizeStreamDepth(int? requested)
    {
        var depth = requested ?? 50;
        return depth <= 10 ? 10 : depth <= 50 ? 50 : 100;
    }

    /// <inheritdoc />
    protected override void DisposeManaged()
    {
        DisposeClients();
        base.DisposeManaged();
    }
}
