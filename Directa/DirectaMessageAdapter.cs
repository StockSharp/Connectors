namespace StockSharp.Directa;

public partial class DirectaMessageAdapter
{
    private sealed class MarketSubscription
    {
        public long TransactionId { get; init; }
        public SecurityId SecurityId { get; init; }
        public string Ticker { get; init; }
        public DataType DataType { get; init; }
        public int? MaxDepth { get; init; }
    }

    private sealed class CommandBlock(
        string beginMarker, string endMarker,
        int? emptyErrorCode)
    {
        private readonly List<string> _lines = [];
        private bool _started;

        public TaskCompletionSource<string[]> Completion
        { get; } = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Observe(string line)
        {
            if (line.StartsWith(
                "ERR;", StringComparison.OrdinalIgnoreCase))
            {
                var parts = DirectaProtocol.Split(line);
                var code = parts.Length > 2 &&
                    int.TryParse(
                        parts[^1],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var parsed)
                            ? parsed : 0;
                if (code == emptyErrorCode)
                    Completion.TrySetResult([]);
                else
                {
                    Completion.TrySetException(
                        new InvalidOperationException(
                            DirectaProtocol.GetError(code)));
                }
                return true;
            }

            if (!_started)
            {
                if (line.StartsWith(
                    beginMarker,
                    StringComparison.OrdinalIgnoreCase))
                {
                    _started = true;
                    return true;
                }
                return false;
            }

            if (line.StartsWith(
                endMarker,
                StringComparison.OrdinalIgnoreCase))
            {
                Completion.TrySetResult(_lines.ToArray());
                return true;
            }

            _lines.Add(line);
            return false;
        }
    }

    private sealed class TrackedOrder
    {
        public string Ticker { get; init; }
        public decimal Quantity { get; set; }
        public string Operation { get; set; }
    }

    private readonly CachedSynchronizedDictionary<
        long, MarketSubscription> _marketSubscriptions = [];
    private readonly SynchronizedDictionary<
        string, DirectaSecurity> _securities =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<
        string, string> _sentDataCodes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<
        string, BookState> _books =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<
        string, long> _orderTransactions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<
        string, TrackedOrder> _trackedOrders =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<
        string, string> _orderFingerprints =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedSet<string> _seenTrades =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _dataSync = new(1, 1);
    private readonly SemaphoreSlim _subscriptionSync =
        new(1, 1);
    private readonly SemaphoreSlim _historySync = new(1, 1);
    private readonly SemaphoreSlim _blockSync = new(1, 1);
    private DirectaLineClient _trading;
    private DirectaLineClient _data;
    private DirectaHistoryClient _history;
    private CommandBlock _commandBlock;
    private TimeZoneInfo _timeZone;
    private long _portfolioSubscriptionId;
    private long _portfolioSnapshotId;
    private string _portfolioSubscriptionFilter;
    private string _portfolioSnapshotFilter;
    private string _portfolioName;
    private long _orderStatusSubscriptionId;
    private long _orderSnapshotId;
    private OrderStatusMessage _orderStatusFilter;
    private OrderStatusMessage _orderSnapshotFilter;
    private long _orderSnapshotSkip;
    private long _orderSnapshotLeft;
    private int _disconnectSignaled;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="DirectaMessageAdapter"/> class.
    /// </summary>
    public DirectaMessageAdapter(
        IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        HeartbeatInterval = TimeSpan.FromSeconds(1);
        this.AddMarketDataSupport();
        this.AddTransactionalSupport();
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedMarketDataType(
            DataType.MarketDepth);
        this.AddSupportedMarketDataType(DataType.Ticks);
        this.AddSupportedCandleTimeFrames(
            DirectaProtocol.TimeFrames);
    }

    /// <summary>
    /// Candle time frames supported by Darwin history.
    /// </summary>
    public static IEnumerable<TimeSpan> AllTimeFrames
        => DirectaProtocol.TimeFrames;

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
    public override bool IsReplaceCommandEditCurrent => true;

    /// <inheritdoc />
    public override bool IsSupportTransactionLog => true;

    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(
        ConnectMessage message,
        CancellationToken cancellationToken)
    {
        if (_trading is not null)
        {
            throw new InvalidOperationException(
                LocalizedStrings.NotDisconnectPrevTime);
        }
        if (RequestTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Directa request timeout must be positive.");
        }
        if (MaxMarketDepth is < 1 or > 20)
        {
            throw new InvalidOperationException(
                "Directa maximum market depth must be from 1 to 20.");
        }

        _timeZone =
            DirectaProtocol.ResolveTimeZone(TimeZoneId);
        _disconnectSignaled = 0;
        _trading = new(Address, "Directa_Trading")
        {
            Parent = this,
        };
        _trading.LineReceived += ProcessTradingLine;
        _trading.Error += ProcessTradingError;
        try
        {
            await _trading.Connect(cancellationToken);
            await _trading.Send(
                "FLOWPOINT TRUE", cancellationToken);
            await _trading.Send(
                "UPDATEORDER TRUE", cancellationToken);
            await _trading.Send(
                "PRICEEXE TRUE", cancellationToken);
            await _trading.Send(
                "LOGCMD TRUE", cancellationToken);
            await base.ConnectAsync(
                message, cancellationToken);
        }
        catch
        {
            DisposeClients();
            throw;
        }
    }

    /// <inheritdoc />
    protected override async ValueTask DisconnectAsync(
        DisconnectMessage message,
        CancellationToken cancellationToken)
    {
        if (_trading is null)
        {
            throw new InvalidOperationException(
                LocalizedStrings.ConnectionNotOk);
        }

        try
        {
            if (_data is not null)
                await _data.Disconnect(cancellationToken);
            if (_history is not null)
                await _history.Disconnect(cancellationToken);
            await _trading.Disconnect(cancellationToken);
            await base.DisconnectAsync(
                message, cancellationToken);
        }
        finally
        {
            DisposeClients();
        }
    }

    /// <inheritdoc />
    protected override async ValueTask ResetAsync(
        ResetMessage message,
        CancellationToken cancellationToken)
    {
        DisposeClients();
        _marketSubscriptions.Clear();
        _securities.Clear();
        _sentDataCodes.Clear();
        _books.Clear();
        _orderTransactions.Clear();
        _trackedOrders.Clear();
        _orderFingerprints.Clear();
        _seenTrades.Clear();
        _commandBlock?.Completion.TrySetCanceled(
            cancellationToken);
        _commandBlock = null;
        _portfolioSubscriptionId = 0;
        _portfolioSnapshotId = 0;
        _portfolioSubscriptionFilter = null;
        _portfolioSnapshotFilter = null;
        _portfolioName = null;
        _orderStatusSubscriptionId = 0;
        _orderSnapshotId = 0;
        _orderStatusFilter = null;
        _orderSnapshotFilter = null;
        _orderSnapshotSkip = 0;
        _orderSnapshotLeft = 0;
        _timeZone = null;
        await base.ResetAsync(message, cancellationToken);
    }

    private async Task EnsureData(
        CancellationToken cancellationToken)
    {
        if (_data is not null)
            return;

        await _dataSync.WaitAsync(cancellationToken);
        try
        {
            if (_data is not null)
                return;
            var client = new DirectaLineClient(
                DataAddress, "Directa_Datafeed")
            {
                Parent = this,
            };
            client.LineReceived += ProcessDataLine;
            client.Error += ProcessDataError;
            try
            {
                await client.Connect(cancellationToken);
                _data = client;
                _sentDataCodes.Clear();
                await RestoreDataSubscriptions(
                    cancellationToken);
            }
            catch
            {
                client.LineReceived -= ProcessDataLine;
                client.Error -= ProcessDataError;
                client.Dispose();
                throw;
            }
        }
        finally
        {
            _dataSync.Release();
        }
    }

    private async Task<DirectaHistoryClient> EnsureHistory(
        CancellationToken cancellationToken)
    {
        if (_history is not null)
            return _history;

        await _historySync.WaitAsync(cancellationToken);
        try
        {
            if (_history is not null)
                return _history;
            var client = new DirectaHistoryClient(
                HistoryAddress, RequestTimeout)
            {
                Parent = this,
            };
            client.Error += ProcessHistoryError;
            try
            {
                await client.Connect(cancellationToken);
                _history = client;
                return client;
            }
            catch
            {
                client.Error -= ProcessHistoryError;
                client.Dispose();
                throw;
            }
        }
        finally
        {
            _historySync.Release();
        }
    }

    private async Task<string[]> RequestBlock(
        string command, string beginMarker,
        string endMarker, int? emptyErrorCode,
        CancellationToken cancellationToken)
    {
        await _blockSync.WaitAsync(cancellationToken);
        try
        {
            var block = new CommandBlock(
                beginMarker, endMarker, emptyErrorCode);
            _commandBlock = block;
            await Trading.Send(
                command, cancellationToken);
            return await block.Completion.Task.WaitAsync(
                RequestTimeout, cancellationToken);
        }
        finally
        {
            _commandBlock = null;
            _blockSync.Release();
        }
    }

    private async ValueTask ProcessTradingLine(
        string line,
        CancellationToken cancellationToken)
    {
        if (_commandBlock?.Observe(line) == true)
            return;
        await ProcessTradingMessage(
            line, cancellationToken);
    }

    private ValueTask ProcessTradingError(
        Exception error,
        CancellationToken cancellationToken)
    {
        _commandBlock?.Completion.TrySetException(error);
        return Interlocked.Exchange(
            ref _disconnectSignaled, 1) == 0
                ? SendOutDisconnectMessageAsync(
                    error, CancellationToken.None)
                : default;
    }

    private ValueTask ProcessDataError(
        Exception error,
        CancellationToken cancellationToken)
        => SendOutErrorAsync(error, cancellationToken);

    private ValueTask ProcessHistoryError(
        Exception error,
        CancellationToken cancellationToken)
        => SendOutErrorAsync(error, cancellationToken);

    private DirectaLineClient Trading => _trading ??
        throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private DirectaLineClient Data => _data ??
        throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private void DisposeClients()
    {
        if (_data is not null)
        {
            _data.LineReceived -= ProcessDataLine;
            _data.Error -= ProcessDataError;
            _data.Dispose();
            _data = null;
        }
        if (_history is not null)
        {
            _history.Error -= ProcessHistoryError;
            _history.Dispose();
            _history = null;
        }
        if (_trading is not null)
        {
            _trading.LineReceived -= ProcessTradingLine;
            _trading.Error -= ProcessTradingError;
            _trading.Dispose();
            _trading = null;
        }
    }

    /// <inheritdoc />
    protected override void DisposeManaged()
    {
        DisposeClients();
        _dataSync.Dispose();
        _subscriptionSync.Dispose();
        _historySync.Dispose();
        _blockSync.Dispose();
        base.DisposeManaged();
    }
}
