namespace StockSharp.MercadoBitcoin;

public partial class MercadoBitcoinMessageAdapter
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
        public string StreamId { get; init; }
        public string Description { get; init; }
        public string BaseAsset { get; init; }
        public string QuoteAsset { get; init; }
        public decimal PriceStep { get; init; }
        public decimal VolumeStep { get; init; }
        public decimal MinimumPrice { get; init; }
        public decimal MaximumPrice { get; init; }
        public decimal MinimumVolume { get; init; }
        public decimal MaximumVolume { get; init; }
        public decimal MinimumCost { get; init; }
        public decimal MaximumCost { get; init; }
    }

    private sealed class AccountDefinition
    {
        public string Id { get; init; }
        public string Name { get; init; }
        public string Currency { get; init; }
        public string PortfolioName { get; init; }
    }

    private class MarketSubscription
    {
        public string Symbol { get; init; }
        public string StreamId { get; init; }
    }

    private sealed class DepthSubscription : MarketSubscription
    {
        public int Depth { get; init; }
    }

    private sealed class OrderSubscription
    {
        public string[] AccountIds { get; init; }
        public string Symbol { get; init; }
        public string OrderId { get; init; }
        public Sides? Side { get; init; }
    }

    private sealed class TrackedOrder
    {
        public long TransactionId { get; init; }
        public string AccountId { get; init; }
        public string Symbol { get; init; }
        public string ExchangeOrderId { get; set; }
        public Sides Side { get; init; }
        public OrderTypes OrderType { get; init; }
        public decimal Volume { get; init; }
        public decimal Price { get; init; }
        public bool? IsPostOnly { get; init; }
        public MercadoBitcoinOrderCondition Condition { get; init; }
    }

    private readonly record struct StreamKey(StreamTypes Type, string StreamId);
    private readonly record struct BalanceFingerprint(decimal Available,
        decimal OnHold, decimal Total);
    private readonly record struct OrderFingerprint(MercadoBitcoinOrderStatuses Status,
        decimal FilledQuantity, long UpdatedAt);

    private readonly Lock _sync = new();
    private readonly Dictionary<string, MarketDefinition> _markets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MarketDefinition> _streamMarkets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AccountDefinition> _accounts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
    private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
    private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
    private readonly Dictionary<StreamKey, int> _streamReferences = [];
    private readonly Dictionary<string, TrackedOrder> _trackedOrders =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenPublicTrades =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenAccountTrades =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, string[]> _portfolioSubscriptions = [];
    private readonly Dictionary<long, OrderSubscription> _orderSubscriptions = [];
    private readonly Dictionary<string, BalanceFingerprint> _balanceFingerprints =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OrderFingerprint> _orderFingerprints =
        new(StringComparer.OrdinalIgnoreCase);
    private MercadoBitcoinRestClient _restClient;
    private MercadoBitcoinSocketClient _socketClient;
    private CancellationTokenSource _pollingCancellation;
    private Task _pollingTask;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="MercadoBitcoinMessageAdapter"/> class.
    /// </summary>
    public MercadoBitcoinMessageAdapter(IdGenerator transactionIdGenerator)
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
    public override bool IsSupportTransactionLog => true;

    /// <inheritdoc />
    public override string[] AssociatedBoards => [BoardCodes.MercadoBitcoin];

    /// <inheritdoc />
    protected override bool ValidateSecurityId(SecurityId securityId)
        => securityId.BoardCode.IsEmpty() ||
            securityId.BoardCode.EqualsIgnoreCase(BoardCodes.MercadoBitcoin) ||
            securityId.IsAssociated(BoardCodes.MercadoBitcoin);

    private MercadoBitcoinRestClient RestClient
        => _restClient ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private MercadoBitcoinSocketClient SocketClient
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
        if (!RestClient.IsCredentialsAvailable || _accounts.Count == 0)
            throw new InvalidOperationException(
                "Mercado Bitcoin client ID and secret are required for private operations.");
    }

    private void RegisterMarkets(MercadoBitcoinSymbols values)
    {
        var symbols = values?.Symbols ?? [];
        using (_sync.EnterScope())
        {
            _markets.Clear();
            _streamMarkets.Clear();
            for (var i = 0; i < symbols.Length; i++)
            {
                var symbolValue = symbols[i];
                if (symbolValue.IsEmpty() ||
                    !values.IsExchangeListed.GetAt(i, true) ||
                    !values.IsExchangeTraded.GetAt(i, true))
                    continue;
                var symbol = symbolValue.NormalizeSymbol();
                var baseAsset = values.BaseCurrencies.GetAt(i);
                var quoteAsset = values.QuoteCurrencies.GetAt(i);
                if (baseAsset.IsEmpty() || quoteAsset.IsEmpty())
                {
                    var separator = symbol.LastIndexOf('-');
                    if (separator <= 0 || separator >= symbol.Length - 1)
                        continue;
                    baseAsset = symbol[..separator];
                    quoteAsset = symbol[(separator + 1)..];
                }
                baseAsset = baseAsset.ToUpperInvariant();
                quoteAsset = quoteAsset.ToUpperInvariant();
                var scale = values.PriceScales.GetAt(i);
                var movement = values.MinimumMovements.GetAt(i);
                var definition = new MarketDefinition
                {
                    Symbol = symbol,
                    StreamId = quoteAsset + baseAsset,
                    Description = values.Descriptions.GetAt(i),
                    BaseAsset = baseAsset,
                    QuoteAsset = quoteAsset,
                    PriceStep = scale > 0 && movement > 0
                        ? movement / scale
                        : 0m,
                    VolumeStep = values.RoundLots.GetAt(i),
                    MinimumPrice = values.MinimumPrices.GetAt(i),
                    MaximumPrice = values.MaximumPrices.GetAt(i),
                    MinimumVolume = values.MinimumVolumes.GetAt(i),
                    MaximumVolume = values.MaximumVolumes.GetAt(i),
                    MinimumCost = values.MinimumCosts.GetAt(i),
                    MaximumCost = values.MaximumCosts.GetAt(i),
                };
                _markets[definition.Symbol] = definition;
                _streamMarkets.TryAdd(definition.StreamId, definition);
            }
        }
    }

    private void RegisterAccounts(IEnumerable<MercadoBitcoinAccount> accounts)
    {
        using (_sync.EnterScope())
        {
            _accounts.Clear();
            foreach (var account in accounts ?? [])
            {
                if (account?.Id.IsEmpty() != false)
                    continue;
                var value = new AccountDefinition
                {
                    Id = account.Id.Trim(),
                    Name = account.Name,
                    Currency = account.Currency,
                    PortfolioName = $"MercadoBitcoin_{account.Id.Trim()}",
                };
                _accounts[value.Id] = value;
            }
        }
    }

    private MarketDefinition GetMarket(SecurityId securityId)
    {
        if (!securityId.BoardCode.IsEmpty() &&
            !securityId.BoardCode.EqualsIgnoreCase(BoardCodes.MercadoBitcoin) &&
            !securityId.IsAssociated(BoardCodes.MercadoBitcoin))
            throw new InvalidOperationException(
                $"Security board '{securityId.BoardCode}' is not Mercado Bitcoin.");
        return GetMarket(securityId.SecurityCode);
    }

    private MarketDefinition GetMarket(string symbol)
    {
        symbol = symbol.NormalizeSymbol();
        using (_sync.EnterScope())
            return _markets.TryGetValue(symbol, out var market)
                ? market
                : throw new InvalidOperationException(
                    $"Unknown Mercado Bitcoin market '{symbol}'.");
    }

    private MarketDefinition GetMarketByStreamId(string id)
    {
        id = id.ThrowIfEmpty(nameof(id)).Trim();
        using (_sync.EnterScope())
            return _streamMarkets.TryGetValue(id, out var market)
                ? market
                : throw new InvalidOperationException(
                    $"Unknown Mercado Bitcoin WebSocket market '{id}'.");
    }

    private AccountDefinition GetAccount(string portfolioName)
    {
        using (_sync.EnterScope())
        {
            if (!portfolioName.IsEmpty())
            {
                var account = _accounts.Values.FirstOrDefault(value =>
                    value.Id.EqualsIgnoreCase(portfolioName) ||
                    value.PortfolioName.EqualsIgnoreCase(portfolioName) ||
                    (!value.Name.IsEmpty() &&
                        value.Name.EqualsIgnoreCase(portfolioName)));
                if (account is not null)
                    return account;
            }
            if (!AccountId.IsEmpty() && _accounts.TryGetValue(AccountId,
                out var configured))
                return configured;
            if (_accounts.Count == 1)
                return _accounts.Values.First();
        }
        throw new InvalidOperationException(
            "Select a Mercado Bitcoin account through PortfolioName or AccountId.");
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

    private bool AddPublicTrade(string symbol, long tradeId,
        long originalTransactionId)
    {
        if (tradeId <= 0)
            return false;
        using (_sync.EnterScope())
        {
            if (_seenPublicTrades.Count > 100000)
                _seenPublicTrades.Clear();
            return _seenPublicTrades.Add(
                $"{symbol}:{tradeId}:{originalTransactionId}");
        }
    }

    private bool AddAccountTrade(string accountId, string tradeId,
        long originalTransactionId)
    {
        if (accountId.IsEmpty() || tradeId.IsEmpty())
            return false;
        using (_sync.EnterScope())
        {
            if (_seenAccountTrades.Count > 100000)
                _seenAccountTrades.Clear();
            return _seenAccountTrades.Add(
                $"{accountId}:{tradeId}:{originalTransactionId}");
        }
    }

    private void ClearState()
    {
        using (_sync.EnterScope())
        {
            _markets.Clear();
            _streamMarkets.Clear();
            _accounts.Clear();
            _level1Subscriptions.Clear();
            _depthSubscriptions.Clear();
            _tickSubscriptions.Clear();
            _streamReferences.Clear();
            _trackedOrders.Clear();
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
            $"Mercado Bitcoin {operation} requires an exchange order ID.");
    }

    /// <inheritdoc />
    protected override void DisposeManaged()
    {
        DisposeClients();
        base.DisposeManaged();
    }
}
