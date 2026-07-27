namespace StockSharp.Dnse;

public partial class DnseMessageAdapter
{
    private sealed class MarketSubscription
    {
        public long TransactionId { get; init; }
        public SecurityId SecurityId { get; init; }
        public DnseInstrumentKey Native { get; init; }
        public DataType DataType { get; init; }
        public TimeSpan? TimeFrame { get; init; }
        public int? MaxDepth { get; init; }
        public string[] Channels { get; init; }
    }

    private DnseRestClient _rest;
    private DnseSocketClient _socket;
    private DnseAccount[] _accounts = [];
    private string _selectedAccount;
    private readonly CachedSynchronizedDictionary<long, MarketSubscription>
        _marketSubscriptions = [];
    private readonly SynchronizedDictionary<string, DnseInstrumentKey>
        _securities = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, long>
        _orderTransactions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<long, string>
        _trackedOrders = [];
    private readonly SynchronizedDictionary<string, string>
        _orderSignatures = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedSet<string> _seenTrades =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, string>
        _positionSignatures = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _nativeReferenceSync = new();
    private readonly Dictionary<string, int> _nativeReferences =
        new(StringComparer.OrdinalIgnoreCase);
    private long _orderStatusSubscriptionId;
    private long _portfolioSubscriptionId;
    private OrderStatusMessage _orderStatusFilter;
    private string _portfolioFilter;
    private DateTime _lastPing;
    private DateTime _lastAccountPoll;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="DnseMessageAdapter"/> class.
    /// </summary>
    /// <param name="transactionIdGenerator">
    /// Transaction identifier generator.
    /// </param>
    public DnseMessageAdapter(IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        HeartbeatInterval = TimeSpan.FromSeconds(1);
        this.AddMarketDataSupport();
        this.AddTransactionalSupport();
        this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedMarketDataType(DataType.MarketDepth);
        this.AddSupportedMarketDataType(DataType.Ticks);
        this.AddSupportedCandleTimeFrames(AllTimeFrames);
    }

    /// <summary>Supported candle time frames.</summary>
    public static IEnumerable<TimeSpan> AllTimeFrames =>
        DnseExtensions.TimeFrames;

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities ||
            dataType.IsTFCandles ||
            dataType == DataType.Ticks ||
            dataType == DataType.Transactions ||
            dataType == DataType.PositionChanges ||
            base.IsAllDownloadingSupported(dataType);

    /// <inheritdoc />
    public override bool IsSupportCandlesUpdates(
        MarketDataMessage subscription)
        => true;

    /// <inheritdoc />
    public override bool IsReplaceCommandEditCurrent => true;

    /// <inheritdoc />
    public override bool IsSupportTransactionLog => true;

    /// <inheritdoc />
    public override IEnumerable<int> SupportedOrderBookDepths { get; } =
        [3];

    /// <inheritdoc />
    public override string[] AssociatedBoards { get; } =
        ["HOSE", "HNX", "UPCOM"];

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
        if (AccountPollingInterval < TimeSpan.FromSeconds(5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(AccountPollingInterval),
                AccountPollingInterval,
                "DNSE account polling interval must be at least five seconds.");
        }
        if (LookupLimit is < 1 or > 100000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(LookupLimit),
                LookupLimit,
                "DNSE lookup limit must be between 1 and 100000.");
        }
        if (MarketDataPriceMultiplier <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MarketDataPriceMultiplier),
                MarketDataPriceMultiplier,
                "DNSE market-data price multiplier must be positive.");
        }
        DefaultBoardId.ThrowIfEmpty(nameof(DefaultBoardId));

        _rest = new(
            RestAddress,
            Key,
            Secret,
            ApiVersion,
            DateHeaderName)
        {
            Parent = this,
        };

        try
        {
            _accounts = await _rest.GetAccounts(cancellationToken);
            InitializeAccounts();

            if (TradingToken.IsEmpty() &&
                !OneTimePassword.IsEmpty())
            {
                TradingToken = (await _rest.CreateTradingToken(
                    OtpType,
                    OneTimePassword,
                    cancellationToken)).Secure();
                OneTimePassword = null;
            }
            else if (TradingToken.IsEmpty() &&
                RequestEmailOtpOnConnect)
            {
                await _rest.SendEmailOtp(cancellationToken);
                RequestEmailOtpOnConnect = false;
                throw new InvalidOperationException(
                    "DNSE sent an Email OTP. Enter it in One-time password and reconnect.");
            }

            _socket = new(
                WebSocketAddress,
                Key,
                Secret,
                Math.Max(1, ReConnectionSettings.ReAttemptCount),
                ReConnectionSettings.WorkingTime)
            {
                Parent = this,
            };
            _socket.SecurityDefinitionReceived +=
                ProcessSecurityDefinition;
            _socket.TradeReceived += ProcessPublicTrade;
            _socket.QuoteReceived += ProcessQuote;
            _socket.CandleReceived += ProcessLiveCandle;
            _socket.OrderReceived += ProcessOrderEvent;
            _socket.PositionReceived += ProcessPositionEvent;
            _socket.AccountReceived += ProcessAccountEvent;
            _socket.Error += SendOutErrorAsync;
            _socket.StateChanged += SendOutConnectionStateAsync;

            var sessionId = await _socket.Connect(cancellationToken);
            await _socket.Subscribe(
                "order.STOCK.json", null, cancellationToken);
            await _socket.Subscribe(
                "position.STOCK.json", null, cancellationToken);
            await _socket.Subscribe(
                "account", null, cancellationToken);

            connectMsg.SessionId = $"{ApiVersion}/{sessionId}";
            _lastPing = _lastAccountPoll = DateTime.UtcNow;
            await base.ConnectAsync(connectMsg, cancellationToken);
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
                await _socket.Disconnect(cancellationToken);
            await base.DisconnectAsync(disconnectMsg, cancellationToken);
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
        _accounts = [];
        _selectedAccount = null;
        _marketSubscriptions.Clear();
        _securities.Clear();
        _orderTransactions.Clear();
        _trackedOrders.Clear();
        _orderSignatures.Clear();
        _seenTrades.Clear();
        _positionSignatures.Clear();
        lock (_nativeReferenceSync)
            _nativeReferences.Clear();
        _orderStatusSubscriptionId = 0;
        _portfolioSubscriptionId = 0;
        _orderStatusFilter = null;
        _portfolioFilter = null;
        _lastPing = default;
        _lastAccountPoll = default;
        await base.ResetAsync(resetMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask TimeAsync(
        TimeMessage timeMsg,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (_socket is not null &&
            now - _lastPing >= TimeSpan.FromSeconds(25))
        {
            _lastPing = now;
            await _socket.Ping(cancellationToken);
        }

        if (_rest is not null &&
            now - _lastAccountPoll >= AccountPollingInterval)
        {
            _lastAccountPoll = now;
            try
            {
                await PollTrackedOrders(cancellationToken);
                if (_orderStatusSubscriptionId != 0)
                {
                    await SendOrderSnapshot(
                        _orderStatusSubscriptionId,
                        cancellationToken);
                }
                if (_portfolioSubscriptionId != 0)
                {
                    await SendPortfolioSnapshot(
                        _portfolioSubscriptionId,
                        _portfolioFilter,
                        cancellationToken);
                }
            }
            catch (Exception error) when (
                error is not OperationCanceledException ||
                    !cancellationToken.IsCancellationRequested)
            {
                await SendOutErrorAsync(error, cancellationToken);
            }
        }

        await base.TimeAsync(timeMsg, cancellationToken);
    }

    private void InitializeAccounts()
    {
        var selected = Account.IsEmpty()
            ? _accounts.FirstOrDefault(account => account.DealAccount) ??
                _accounts.FirstOrDefault()
            : _accounts.FirstOrDefault(
                account => account.Id.EqualsIgnoreCase(Account));
        if (selected is null && !Account.IsEmpty())
        {
            if (_accounts.Length > 0)
            {
                throw new InvalidOperationException(
                    $"DNSE account '{Account}' was not returned by the API.");
            }
            selected = new()
            {
                Id = Account,
                DealAccount = true,
            };
            _accounts = [selected];
        }
        if (selected?.Id.IsEmpty() != false)
        {
            throw new InvalidOperationException(
                "DNSE returned no stock brokerage account.");
        }
        _selectedAccount = selected.Id;
    }

    private string ResolveAccount(string portfolioName)
    {
        if (!portfolioName.IsEmpty())
        {
            var account = _accounts.FirstOrDefault(
                item => item.Id.EqualsIgnoreCase(portfolioName));
            if (account is not null)
                return account.Id;
            throw new InvalidOperationException(
                $"DNSE account '{portfolioName}' is not available.");
        }
        return _selectedAccount.ThrowIfEmpty(nameof(_selectedAccount));
    }

    private string ResolvePortfolio(string accountNo)
        => accountNo.IsEmpty(_selectedAccount).IsEmpty("DNSE");

    private void CacheSecurity(DnseInstrumentKey native)
    {
        if (native.Symbol.IsEmpty())
            return;
        _securities[
            $"{native.Symbol.ToUpperInvariant()}|{native.BoardId.ToUpperInvariant()}"] =
            native;
        _securities[native.Symbol.ToUpperInvariant()] = native;
    }

    private DnseInstrumentKey ResolveSecurity(
        string symbol,
        string boardId = null,
        string marketId = null)
    {
        var key = boardId.IsEmpty()
            ? symbol?.ToUpperInvariant()
            : $"{symbol?.ToUpperInvariant()}|{boardId.ToUpperInvariant()}";
        if (!key.IsEmpty() &&
            _securities.TryGetValue(key, out var native))
        {
            return native;
        }

        native = new(
            marketId.IsEmpty("STO"),
            boardId.IsEmpty(DefaultBoardId).IsEmpty("G1"),
            "ST",
            symbol.ThrowIfEmpty(nameof(symbol)).ToUpperInvariant());
        CacheSecurity(native);
        return native;
    }

    private async Task<DnseInstrumentKey> ResolveSecurityAsync(
        string symbol,
        CancellationToken cancellationToken)
    {
        var key = symbol?.ToUpperInvariant();
        if (!key.IsEmpty() &&
            _securities.TryGetValue(key, out var cached))
        {
            return cached;
        }

        try
        {
            var page = await _rest.GetInstruments(
                symbol,
                null,
                null,
                10,
                1,
                cancellationToken);
            foreach (var instrument in page?.Data ?? [])
            {
                if (instrument?.Symbol.IsEmpty() != false)
                    continue;
                CacheSecurity(
                    instrument.ToNative(DefaultBoardId));
            }
            if (!key.IsEmpty() &&
                _securities.TryGetValue(key, out cached))
            {
                return cached;
            }
        }
        catch (Exception error) when (
            error is not OperationCanceledException ||
                !cancellationToken.IsCancellationRequested)
        {
            this.AddWarningLog(
                "DNSE could not resolve the exchange for '{0}': {1}",
                symbol,
                error.Message);
        }

        return ResolveSecurity(symbol);
    }

    private async ValueTask AddNativeReference(
        string channel,
        string symbol,
        CancellationToken cancellationToken)
    {
        var key = $"{channel}|{symbol}";
        var subscribe = false;
        lock (_nativeReferenceSync)
        {
            if (!_nativeReferences.TryGetValue(key, out var count))
            {
                _nativeReferences[key] = 1;
                subscribe = true;
            }
            else
                _nativeReferences[key] = count + 1;
        }
        if (subscribe)
            await _socket.Subscribe(channel, symbol, cancellationToken);
    }

    private async ValueTask RemoveNativeReference(
        string channel,
        string symbol,
        CancellationToken cancellationToken)
    {
        var key = $"{channel}|{symbol}";
        var unsubscribe = false;
        lock (_nativeReferenceSync)
        {
            if (!_nativeReferences.TryGetValue(key, out var count))
                return;
            if (count <= 1)
            {
                _nativeReferences.Remove(key);
                unsubscribe = true;
            }
            else
                _nativeReferences[key] = count - 1;
        }
        if (unsubscribe && _socket is not null)
            await _socket.Unsubscribe(channel, symbol, cancellationToken);
    }

    private async ValueTask PollTrackedOrders(
        CancellationToken cancellationToken)
    {
        foreach (var pair in _trackedOrders.ToArray())
        {
            try
            {
                var order = await _rest.GetOrder(
                    pair.Value, pair.Key, cancellationToken);
                await ProcessOrder(order, 0, cancellationToken);
            }
            catch (HttpRequestException error) when (
                error.StatusCode is HttpStatusCode.NotFound or
                    HttpStatusCode.Conflict)
            {
                // A newly accepted order may take a moment to become queryable.
            }
        }
    }

    private ValueTask ProcessOrderEvent(
        DnseOrder order,
        CancellationToken cancellationToken)
        => ProcessOrder(
            order,
            _orderStatusSubscriptionId,
            cancellationToken);

    private ValueTask ProcessPositionEvent(
        DnsePosition position,
        CancellationToken cancellationToken)
        => ProcessPosition(
            position,
            _portfolioSubscriptionId,
            cancellationToken);

    private ValueTask ProcessAccountEvent(
        DnseAccountUpdate account,
        CancellationToken cancellationToken)
        => ProcessAccount(
            account,
            _portfolioSubscriptionId,
            cancellationToken);

    private void DisposeClients()
    {
        if (_socket is not null)
        {
            _socket.SecurityDefinitionReceived -=
                ProcessSecurityDefinition;
            _socket.TradeReceived -= ProcessPublicTrade;
            _socket.QuoteReceived -= ProcessQuote;
            _socket.CandleReceived -= ProcessLiveCandle;
            _socket.OrderReceived -= ProcessOrderEvent;
            _socket.PositionReceived -= ProcessPositionEvent;
            _socket.AccountReceived -= ProcessAccountEvent;
            _socket.Error -= SendOutErrorAsync;
            _socket.StateChanged -= SendOutConnectionStateAsync;
            _socket.Dispose();
            _socket = null;
        }

        _rest?.Dispose();
        _rest = null;
    }
}
