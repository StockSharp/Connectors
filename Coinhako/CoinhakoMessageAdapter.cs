namespace StockSharp.Coinhako;

public partial class CoinhakoMessageAdapter
{
    private sealed class MarketDefinition
    {
        public string Symbol { get; init; }
        public string BaseCurrency { get; init; }
        public string CounterCurrency { get; init; }
        public CoinhakoSpotPrice Spot { get; set; }
    }

    private sealed class Level1Subscription
    {
        public string Symbol { get; init; }
    }

    private sealed class OrderSubscription
    {
        public string Symbol { get; init; }
        public long? OrderId { get; init; }
        public string ClientOrderId { get; init; }
        public Sides? Side { get; init; }
        public DateTime? From { get; init; }
        public DateTime? To { get; init; }
        public int Maximum { get; init; }
    }

    private sealed class TrackedOrder
    {
        public long TransactionId { get; init; }
        public long OrderId { get; set; }
        public string ClientOrderId { get; init; }
        public string Symbol { get; init; }
        public Sides Side { get; init; }
        public OrderTypes OrderType { get; init; }
        public decimal Volume { get; init; }
        public decimal Price { get; init; }
        public DateTime? ExpiryDate { get; init; }
    }

    private readonly record struct OrderDeliveryKey(long TargetId,
        long OrderId);

    private readonly Lock _sync = new();
    private readonly Dictionary<string, MarketDefinition> _markets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, Level1Subscription>
        _level1Subscriptions = [];
    private readonly HashSet<long> _portfolioSubscriptions = [];
    private readonly Dictionary<long, OrderSubscription> _orderSubscriptions =
        [];
    private readonly Dictionary<long, TrackedOrder> _ordersById = [];
    private readonly Dictionary<string, TrackedOrder> _ordersByClientId =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<OrderDeliveryKey, string> _orderSignatures = [];
    private readonly HashSet<OrderDeliveryKey> _reportedFills = [];
    private CoinhakoRestClient _restClient;
    private DateTime _lastLevel1Refresh;
    private DateTime _lastOrderRefresh;
    private DateTime _lastPortfolioRefresh;

    /// <summary>
    /// Initializes a new instance of the <see cref="CoinhakoMessageAdapter"/>
    /// class.
    /// </summary>
    public CoinhakoMessageAdapter(IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        HeartbeatInterval = TimeSpan.FromSeconds(1);
        this.AddMarketDataSupport();
        this.AddTransactionalSupport();
        this.AddSupportedMarketDataType(DataType.Level1);
        this.RemoveSupportedMessage(MessageTypes.OrderReplace);
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities ||
            dataType == DataType.PositionChanges ||
            dataType == DataType.Transactions ||
            base.IsAllDownloadingSupported(dataType);

    /// <inheritdoc />
    public override bool IsSupportOrderBookIncrements => false;

    /// <inheritdoc />
    public override bool IsReplaceCommandEditCurrent => false;

    /// <inheritdoc />
    public override bool IsSupportTransactionLog => true;

    /// <inheritdoc />
    public override string[] AssociatedBoards => [BoardCodes.Coinhako];

    /// <inheritdoc />
    protected override bool ValidateSecurityId(SecurityId securityId)
        => securityId.BoardCode.IsEmpty() ||
            securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Coinhako) ||
            securityId.IsAssociated(BoardCodes.Coinhako);

    private CoinhakoRestClient RestClient
        => _restClient ?? throw new InvalidOperationException(
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
                "Coinhako API public key and secp256k1 private key are required for account and trading operations.");
    }

    private string[] GetCounterCurrencies()
    {
        var currencies = (CounterCurrencies ?? string.Empty)
            .Split([',', ';', '|', ' ', '\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(static value => value.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (currencies.Length == 0)
            throw new InvalidOperationException(
                "At least one Coinhako counter currency must be configured.");
        foreach (var currency in currencies)
            if (currency.Length is < 2 or > 16 ||
                !currency.All(static character => char.IsLetterOrDigit(character)))
                throw new InvalidOperationException(
                    $"Invalid Coinhako counter currency '{currency}'.");
        return currencies;
    }

    private void RegisterMarkets(IEnumerable<CoinhakoSpotPrice> prices)
    {
        using (_sync.EnterScope())
        {
            _markets.Clear();
            foreach (var price in prices ?? [])
            {
                if (!TryCreateMarket(price, out var market))
                    continue;
                _markets[market.Symbol.ToCoinhakoSymbolKey()] = market;
            }
        }
    }

    private void UpdateMarkets(IEnumerable<CoinhakoSpotPrice> prices)
    {
        using (_sync.EnterScope())
            foreach (var price in prices ?? [])
            {
                if (!TryCreateMarket(price, out var incoming))
                    continue;
                var key = incoming.Symbol.ToCoinhakoSymbolKey();
                if (_markets.TryGetValue(key, out var market))
                    market.Spot = price;
                else
                    _markets[key] = incoming;
            }
    }

    private static bool TryCreateMarket(CoinhakoSpotPrice price,
        out MarketDefinition market)
    {
        market = null;
        if (price?.Symbol.IsEmpty() != false ||
            price.BuyPrice <= 0 && price.SellPrice <= 0)
            return false;
        var symbol = price.Symbol.NormalizeCoinhakoSymbol();
        var separator = symbol.LastIndexOf('-');
        if (separator <= 0 || separator >= symbol.Length - 1)
            return false;
        var baseCurrency = symbol[..separator];
        var counterCurrency = symbol[(separator + 1)..];
        if (baseCurrency.EqualsIgnoreCase(counterCurrency))
            return false;
        market = new()
        {
            Symbol = symbol,
            BaseCurrency = baseCurrency,
            CounterCurrency = counterCurrency,
            Spot = price,
        };
        return true;
    }

    private MarketDefinition GetMarket(SecurityId securityId)
    {
        if (!securityId.BoardCode.IsEmpty() &&
            !securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Coinhako) &&
            !securityId.IsAssociated(BoardCodes.Coinhako))
            throw new InvalidOperationException(
                $"Security board '{securityId.BoardCode}' is not Coinhako.");
        return GetMarket(securityId.SecurityCode);
    }

    private MarketDefinition GetMarket(string symbol)
    {
        var key = symbol.ToCoinhakoSymbolKey();
        using (_sync.EnterScope())
            return _markets.TryGetValue(key, out var market)
                ? market
                : throw new InvalidOperationException(
                    $"Unknown Coinhako market '{symbol}'.");
    }

    private MarketDefinition[] GetMarkets()
    {
        using (_sync.EnterScope())
            return [.. _markets.Values];
    }

    private string GetPortfolioName() => $"Coinhako_{Key.ToId()}";

    private void ValidatePortfolio(string portfolioName)
    {
        if (!portfolioName.IsEmpty() &&
            !portfolioName.EqualsIgnoreCase(GetPortfolioName()))
            throw new InvalidOperationException(
                $"Unknown Coinhako portfolio '{portfolioName}'.");
    }

    private void TrackOrder(TrackedOrder tracked)
    {
        if (tracked is null)
            return;
        using (_sync.EnterScope())
        {
            if (tracked.OrderId > 0)
                _ordersById[tracked.OrderId] = tracked;
            if (!tracked.ClientOrderId.IsEmpty())
                _ordersByClientId[tracked.ClientOrderId] = tracked;
        }
    }

    private TrackedOrder GetTrackedOrder(long orderId, string clientOrderId)
    {
        using (_sync.EnterScope())
        {
            if (orderId > 0 && _ordersById.TryGetValue(orderId, out var tracked))
                return tracked;
            return !clientOrderId.IsEmpty() &&
                _ordersByClientId.TryGetValue(clientOrderId, out tracked)
                    ? tracked
                    : null;
        }
    }

    private string GetClientOrderId(long transactionId, string userOrderId)
    {
        var value = userOrderId.IsEmpty()
            ? $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}"
            : userOrderId.Trim();
        if (value.Length > 128 || value.Any(char.IsControl))
            throw new InvalidOperationException(
                "Coinhako client order IDs must contain 1 through 128 printable characters.");
        return value;
    }

    private static long ParseTransactionId(string clientOrderId)
    {
        if (clientOrderId?.StartsWith("ss-",
            StringComparison.OrdinalIgnoreCase) != true)
            return 0;
        var end = clientOrderId.IndexOf('-', 3);
        var value = end < 0 ? clientOrderId.AsSpan(3) :
            clientOrderId.AsSpan(3, end - 3);
        return long.TryParse(value, NumberStyles.None,
            CultureInfo.InvariantCulture, out var transactionId)
                ? transactionId
                : 0;
    }

    private static long ResolveOrderId(long? orderId, string orderStringId,
        string operation)
    {
        if (orderId is > 0)
            return orderId.Value;
        if (long.TryParse(orderStringId, NumberStyles.None,
            CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
            return parsed;
        throw new InvalidOperationException(
            $"Coinhako {operation} requires a numeric exchange order ID.");
    }

    private void ClearState()
    {
        using (_sync.EnterScope())
        {
            _markets.Clear();
            _level1Subscriptions.Clear();
            _portfolioSubscriptions.Clear();
            _orderSubscriptions.Clear();
            _ordersById.Clear();
            _ordersByClientId.Clear();
            _orderSignatures.Clear();
            _reportedFills.Clear();
            _lastLevel1Refresh = default;
            _lastOrderRefresh = default;
            _lastPortfolioRefresh = default;
        }
    }

    /// <inheritdoc />
    protected override void DisposeManaged()
    {
        DisposeClient();
        base.DisposeManaged();
    }
}
