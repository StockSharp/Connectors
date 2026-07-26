namespace StockSharp.Tradernet;

public partial class TradernetMessageAdapter
{
    private sealed class MarketSubscription
    {
        public long TransactionId { get; init; }
        public SecurityId SecurityId { get; init; }
        public string Ticker { get; init; }
        public DataType DataType { get; init; }
        public int? MaxDepth { get; init; }
    }

    private readonly CachedSynchronizedDictionary<
        long, MarketSubscription> _marketSubscriptions = [];
    private readonly SynchronizedDictionary<
        string, TradernetSecurity> _securities =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<
        string, BookState> _bookStates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<
        string, string> _lastPublicTrades =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<
        long, long> _orderTransactions = [];
    private readonly SynchronizedSet<long> _trackedOrders = [];
    private readonly SynchronizedDictionary<
        string, string> _orderFingerprints = [];
    private readonly SynchronizedSet<string> _seenTrades = [];
    private TradernetRestClient _rest;
    private TradernetSocketClient _socket;
    private long _portfolioSubscriptionId;
    private string _portfolioNameFilter;
    private long _orderStatusSubscriptionId;
    private OrderStatusMessage _orderStatusFilter;
    private string _portfolioName;
    private DateTime _lastPoll;
    private DateTime _lastPing;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="TradernetMessageAdapter"/> class.
    /// </summary>
    /// <param name="transactionIdGenerator">
    /// Transaction identifier generator.
    /// </param>
    public TradernetMessageAdapter(
        IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        HeartbeatInterval = TimeSpan.FromSeconds(1);
        this.AddMarketDataSupport();
        this.AddTransactionalSupport();
        this.RemoveSupportedMessage(
            MessageTypes.OrderReplace);
        this.RemoveSupportedMessage(
            MessageTypes.OrderGroupCancel);
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedMarketDataType(
            DataType.MarketDepth);
        this.AddSupportedMarketDataType(DataType.Ticks);
        this.AddSupportedCandleTimeFrames(
            TradernetExtensions.TimeFrames);
    }

    /// <summary>
    /// Candle time frames supported by Tradernet.
    /// </summary>
    public static IEnumerable<TimeSpan> AllTimeFrames
        => TradernetExtensions.TimeFrames;

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(
        DataType dataType)
        => dataType == DataType.Securities ||
            dataType == DataType.Ticks ||
            dataType.IsTFCandles ||
            dataType == DataType.PositionChanges ||
            dataType == DataType.Transactions ||
            base.IsAllDownloadingSupported(dataType);

    /// <inheritdoc />
    public override bool IsSupportCandlesUpdates(
        MarketDataMessage subscription) => false;

    /// <inheritdoc />
    public override bool IsSupportTransactionLog => true;

    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(
        ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        if (_rest is not null || _socket is not null)
        {
            throw new InvalidOperationException(
                LocalizedStrings.NotDisconnectPrevTime);
        }
        if (PollingInterval < TimeSpan.FromSeconds(1))
        {
            throw new InvalidOperationException(
                "Tradernet polling interval must be at least one second.");
        }
        if (MaxMarketDepth is < 1 or > 100)
        {
            throw new InvalidOperationException(
                "Tradernet maximum market depth must be from 1 to 100.");
        }
        if (SecuritiesPageSize is < 1 or > 1000)
        {
            throw new InvalidOperationException(
                "Tradernet securities page size must be from 1 to 1000.");
        }

        _rest = new(Address, Key, Secret,
            Math.Max(1,
                ReConnectionSettings.ReAttemptCount))
        {
            Parent = this,
        };

        try
        {
            var sidInfo =
                await _rest.GetSidInfo(cancellationToken);
            if (sidInfo?.Sid.IsEmpty() != false)
            {
                this.AddWarningLog(
                    "Tradernet returned no SID. Public realtime data " +
                    "will work; private updates will use REST polling.");
            }

            _socket = new(WebSocketAddress, sidInfo?.Sid,
                Math.Max(1,
                    ReConnectionSettings.ReAttemptCount),
                ReConnectionSettings.WorkingTime)
            {
                Parent = this,
            };
            _socket.QuoteReceived += ProcessQuote;
            _socket.BookReceived += ProcessBook;
            _socket.PortfolioReceived += ProcessPortfolio;
            _socket.OrdersReceived += ProcessOrders;
            _socket.Error += SendOutErrorAsync;
            _socket.StateChanged +=
                SendOutConnectionStateAsync;
            await _socket.ConnectAsync(cancellationToken);

            _lastPoll = _lastPing = DateTime.UtcNow;
            await base.ConnectAsync(
                connectMsg, cancellationToken);
        }
        catch
        {
            DisposeClients();
            throw;
        }
    }

    /// <inheritdoc />
    protected override async ValueTask DisconnectAsync(
        DisconnectMessage disconnectMsg,
        CancellationToken cancellationToken)
    {
        if (_rest is null)
        {
            throw new InvalidOperationException(
                LocalizedStrings.ConnectionNotOk);
        }

        try
        {
            if (_socket is not null)
                await _socket.DisconnectAsync(cancellationToken);
            await base.DisconnectAsync(
                disconnectMsg, cancellationToken);
        }
        finally
        {
            DisposeClients();
        }
    }

    /// <inheritdoc />
    protected override async ValueTask ResetAsync(
        ResetMessage resetMsg,
        CancellationToken cancellationToken)
    {
        DisposeClients();
        _marketSubscriptions.Clear();
        _securities.Clear();
        _bookStates.Clear();
        _lastPublicTrades.Clear();
        _orderTransactions.Clear();
        _trackedOrders.Clear();
        _orderFingerprints.Clear();
        _seenTrades.Clear();
        _portfolioSubscriptionId = 0;
        _portfolioNameFilter = null;
        _orderStatusSubscriptionId = 0;
        _orderStatusFilter = null;
        _portfolioName = null;
        _lastPoll = default;
        _lastPing = default;
        await base.ResetAsync(resetMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask TimeAsync(
        TimeMessage timeMsg,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (_socket is not null &&
            now - _lastPing >= TimeSpan.FromSeconds(30))
        {
            await _socket.Ping(cancellationToken);
            _lastPing = now;
        }

        if (_rest is not null &&
            now - _lastPoll >= PollingInterval)
        {
            _lastPoll = now;
            try
            {
                if (_portfolioSubscriptionId != 0)
                {
                    await SendPortfolioSnapshot(
                        _portfolioSubscriptionId,
                        _portfolioNameFilter,
                        cancellationToken);
                }
                if (_orderStatusSubscriptionId != 0)
                {
                    await SendOrderSnapshot(
                        _orderStatusSubscriptionId,
                        _orderStatusFilter,
                        cancellationToken);
                }
                await PollTrackedOrders(cancellationToken);
            }
            catch (Exception error) when (
                error is not OperationCanceledException ||
                !cancellationToken.IsCancellationRequested)
            {
                await SendOutErrorAsync(
                    error, cancellationToken);
            }
        }

        await base.TimeAsync(timeMsg, cancellationToken);
    }

    private async ValueTask AddMarketSubscription(
        MarketDataMessage message, DataType dataType,
        string ticker,
        CancellationToken cancellationToken)
    {
        var quoteFeed =
            dataType == DataType.Level1 ||
            dataType == DataType.Ticks;
        var first = !_marketSubscriptions.CachedValues.Any(
            subscription =>
                subscription.Ticker.EqualsIgnoreCase(ticker) &&
                (quoteFeed
                    ? subscription.DataType == DataType.Level1 ||
                      subscription.DataType == DataType.Ticks
                    : subscription.DataType == dataType));
        _marketSubscriptions.Add(message.TransactionId, new()
        {
            TransactionId = message.TransactionId,
            SecurityId = message.SecurityId,
            Ticker = ticker,
            DataType = dataType,
            MaxDepth = message.MaxDepth,
        });

        try
        {
            if (first && quoteFeed)
            {
                await Socket.SubscribeQuotes(
                    ticker, true, cancellationToken);
            }
            else if (first &&
                dataType == DataType.MarketDepth)
            {
                await Socket.SubscribeBook(
                    ticker, true, cancellationToken);
            }
        }
        catch
        {
            _marketSubscriptions.Remove(
                message.TransactionId);
            throw;
        }
    }

    private async ValueTask RemoveMarketSubscription(
        long transactionId,
        CancellationToken cancellationToken)
    {
        if (!_marketSubscriptions.TryGetAndRemove(
            transactionId, out var subscription))
            return;

        var quoteFeed =
            subscription.DataType == DataType.Level1 ||
            subscription.DataType == DataType.Ticks;
        var remains =
            _marketSubscriptions.CachedValues.Any(value =>
                value.Ticker.EqualsIgnoreCase(
                    subscription.Ticker) &&
                (quoteFeed
                    ? value.DataType == DataType.Level1 ||
                      value.DataType == DataType.Ticks
                    : value.DataType ==
                      subscription.DataType));
        if (!remains && quoteFeed)
        {
            await Socket.SubscribeQuotes(
                subscription.Ticker, false,
                cancellationToken);
        }
        else if (!remains &&
            subscription.DataType ==
                DataType.MarketDepth)
        {
            await Socket.SubscribeBook(
                subscription.Ticker, false,
                cancellationToken);
            _bookStates.Remove(subscription.Ticker);
        }
    }

    private MarketSubscription[] FindSubscriptions(
        string ticker, DataType dataType)
        => _marketSubscriptions.CachedValues
            .Where(subscription =>
                subscription.DataType == dataType &&
                subscription.Ticker.EqualsIgnoreCase(
                    ticker))
            .ToArray();

    private async Task<TradernetSecurity> GetSecurity(
        SecurityId securityId,
        CancellationToken cancellationToken)
    {
        var ticker = securityId.ToNativeTicker();
        if (_securities.TryGetValue(
            ticker, out var security))
            return security;

        var info = await Rest.GetSecurityInfo(
            ticker, cancellationToken);
        security = new()
        {
            Ticker = info?.Ticker.IsEmpty(ticker),
            ExchangeTicker = info?.ExchangeTicker,
            Name = info?.ShortName,
            Currency = info?.Currency,
            PriceStep = info?.MinStep,
            LotSize = info?.Lot,
        };
        CacheSecurity(security);
        return security;
    }

    private void CacheSecurity(
        TradernetSecurity security)
    {
        if (security?.Ticker.IsEmpty() != false)
            return;
        _securities[security.Ticker] = security;
    }

    private async ValueTask PollTrackedOrders(
        CancellationToken cancellationToken)
    {
        if (_trackedOrders.Count == 0)
            return;

        foreach (var order in
            await Rest.GetCurrentOrders(
                false, cancellationToken))
        {
            if (_trackedOrders.Contains(
                order.GetOrderId()))
            {
                await ProcessOrder(
                    order, 0, cancellationToken);
            }
        }
    }

    private TradernetRestClient Rest => _rest ??
        throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private TradernetSocketClient Socket => _socket ??
        throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private void DisposeClients()
    {
        if (_socket is not null)
        {
            _socket.QuoteReceived -= ProcessQuote;
            _socket.BookReceived -= ProcessBook;
            _socket.PortfolioReceived -= ProcessPortfolio;
            _socket.OrdersReceived -= ProcessOrders;
            _socket.Error -= SendOutErrorAsync;
            _socket.StateChanged -=
                SendOutConnectionStateAsync;
            _socket.Dispose();
            _socket = null;
        }
        _rest?.Dispose();
        _rest = null;
    }

    /// <inheritdoc />
    protected override void DisposeManaged()
    {
        DisposeClients();
        base.DisposeManaged();
    }
}
