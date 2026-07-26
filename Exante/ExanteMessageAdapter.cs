namespace StockSharp.Exante;

public partial class ExanteMessageAdapter
{
    private sealed class MarketSubscription
    {
        public long TransactionId { get; init; }
        public SecurityId SecurityId { get; init; }
        public DataType DataType { get; init; }
        public CancellationTokenSource Cancellation { get; init; }
        public Task Task { get; set; }
    }

    private readonly CachedSynchronizedDictionary<long, MarketSubscription>
        _marketSubscriptions = [];
    private readonly SynchronizedDictionary<string, ExanteSymbol>
        _symbols = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, ExanteOrder>
        _orders = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, long>
        _orderTransactions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, string>
        _orderFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedSet<string> _seenTrades =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _privateStreamSync = new(1, 1);
    private ExanteRestClient _rest;
    private ExanteAccount[] _accounts = [];
    private CancellationTokenSource _connectionCts;
    private CancellationTokenSource _privateStreamCts;
    private Task _orderStreamTask;
    private Task _privateTradeStreamTask;
    private long _portfolioSubscriptionId;
    private string _portfolioNameFilter;
    private long _orderStatusSubscriptionId;
    private OrderStatusMessage _orderStatusFilter;
    private DateTime _lastPortfolioPoll;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="ExanteMessageAdapter"/> class.
    /// </summary>
    /// <param name="transactionIdGenerator">
    /// Transaction identifier generator.
    /// </param>
    public ExanteMessageAdapter(IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        HeartbeatInterval = TimeSpan.FromSeconds(1);
        this.AddMarketDataSupport();
        this.AddTransactionalSupport();
        this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedMarketDataType(DataType.MarketDepth);
        this.AddSupportedMarketDataType(DataType.Ticks);
        this.AddSupportedCandleTimeFrames(ExanteExtensions.TimeFrames);
    }

    /// <summary>
    /// Time frames supported by the EXANTE OHLC endpoint.
    /// </summary>
    public static IEnumerable<TimeSpan> AllTimeFrames
        => ExanteExtensions.TimeFrames;

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
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
        ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        if (_rest is not null)
            throw new InvalidOperationException(
                LocalizedStrings.NotDisconnectPrevTime);
        if (PollingInterval < TimeSpan.FromSeconds(1))
            throw new InvalidOperationException(
                "EXANTE polling interval must be at least one second.");
        if (SummaryCurrency.IsEmpty() ||
            SummaryCurrency.Length != 3)
            throw new InvalidOperationException(
                "EXANTE summary currency must be a three-letter ISO code.");
        if (MaxMarketDepth is < 1 or > 1000)
            throw new InvalidOperationException(
                "EXANTE maximum market depth must be from 1 to 1000.");
        if (HistoryRequestSize is < 1 or > 10000)
            throw new InvalidOperationException(
                "EXANTE history request size must be from 1 to 10000.");

        _rest = new(IsDemo ? DemoAddress : LiveAddress,
            Key, Secret,
            Math.Max(1, ReConnectionSettings.ReAttemptCount))
        {
            Parent = this,
        };
        _connectionCts = new();

        try
        {
            _accounts = await _rest.GetAccounts(cancellationToken);
            _lastPortfolioPoll = DateTime.UtcNow;
            await base.ConnectAsync(connectMsg, cancellationToken);
        }
        catch
        {
            await StopStreams();
            DisposeClient();
            throw;
        }
    }

    /// <inheritdoc />
    protected override async ValueTask DisconnectAsync(
        DisconnectMessage disconnectMsg,
        CancellationToken cancellationToken)
    {
        if (_rest is null)
            throw new InvalidOperationException(
                LocalizedStrings.ConnectionNotOk);
        try
        {
            await StopStreams();
            await base.DisconnectAsync(disconnectMsg, cancellationToken);
        }
        finally
        {
            DisposeClient();
        }
    }

    /// <inheritdoc />
    protected override async ValueTask ResetAsync(ResetMessage resetMsg,
        CancellationToken cancellationToken)
    {
        await StopStreams();
        DisposeClient();
        _accounts = [];
        _symbols.Clear();
        _orders.Clear();
        _orderTransactions.Clear();
        _orderFingerprints.Clear();
        _seenTrades.Clear();
        _portfolioSubscriptionId = 0;
        _portfolioNameFilter = null;
        _orderStatusSubscriptionId = 0;
        _orderStatusFilter = null;
        _lastPortfolioPoll = default;
        await base.ResetAsync(resetMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask TimeAsync(TimeMessage timeMsg,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (_rest is not null &&
            _portfolioSubscriptionId != 0 &&
            now - _lastPortfolioPoll >= PollingInterval)
        {
            _lastPortfolioPoll = now;
            try
            {
                await SendPortfolioSnapshot(
                    _portfolioSubscriptionId,
                    _portfolioNameFilter, cancellationToken);
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

    private async Task EnsurePrivateStreams(
        CancellationToken cancellationToken)
    {
        await _privateStreamSync.WaitAsync(cancellationToken);
        try
        {
            if (_orderStreamTask is not null)
                return;

            _privateStreamCts =
                CancellationTokenSource.CreateLinkedTokenSource(
                    _connectionCts?.Token ?? CancellationToken.None);
            var token = _privateStreamCts.Token;
            _orderStreamTask = Rest.RunOrderStream(
                ProcessOrderStream,
                SendStreamError, token);
            _privateTradeStreamTask = Rest.RunPrivateTradeStream(
                ProcessPrivateTradeStream,
                SendStreamError, token);
        }
        finally
        {
            _privateStreamSync.Release();
        }
    }

    private async ValueTask AddMarketSubscription(
        MarketDataMessage message, DataType dataType,
        Func<CancellationToken, Task> run,
        CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(
            _connectionCts?.Token ?? CancellationToken.None);
        var subscription = new MarketSubscription
        {
            TransactionId = message.TransactionId,
            SecurityId = message.SecurityId,
            DataType = dataType,
            Cancellation = source,
        };
        _marketSubscriptions.Add(
            message.TransactionId, subscription);
        try
        {
            subscription.Task = RunMarketStream(
                run, source.Token);
            await Task.Yield();
        }
        catch
        {
            _marketSubscriptions.Remove(message.TransactionId);
            source.Dispose();
            throw;
        }
    }

    private async Task RunMarketStream(
        Func<CancellationToken, Task> run,
        CancellationToken cancellationToken)
    {
        try
        {
            await run(cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            await SendOutErrorAsync(error, CancellationToken.None);
        }
    }

    private async ValueTask RemoveMarketSubscription(
        long transactionId)
    {
        if (!_marketSubscriptions.TryGetAndRemove(
            transactionId, out var subscription))
            return;
        subscription.Cancellation.Cancel();
        try
        {
            if (subscription.Task is not null)
                await subscription.Task;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            subscription.Cancellation.Dispose();
        }
    }

    private async Task StopStreams()
    {
        _connectionCts?.Cancel();
        foreach (var subscription in
            _marketSubscriptions.CachedValues)
            subscription.Cancellation.Cancel();
        _privateStreamCts?.Cancel();

        var tasks = _marketSubscriptions.CachedValues
            .Select(s => s.Task)
            .Where(t => t is not null)
            .Concat(new Task[]
            {
                _orderStreamTask,
                _privateTradeStreamTask,
            }.Where(t => t is not null))
            .ToArray();
        if (tasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
            }
        }

        foreach (var subscription in
            _marketSubscriptions.CachedValues)
            subscription.Cancellation.Dispose();
        _marketSubscriptions.Clear();
        _privateStreamCts?.Dispose();
        _privateStreamCts = null;
        _orderStreamTask = null;
        _privateTradeStreamTask = null;
        _connectionCts?.Dispose();
        _connectionCts = null;
    }

    private ValueTask SendStreamError(Exception error,
        CancellationToken cancellationToken)
        => SendOutErrorAsync(error, cancellationToken);

    private ExanteRestClient Rest => _rest ??
        throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private void CacheSymbol(ExanteSymbol symbol,
        params string[] aliases)
    {
        if (symbol is null)
            return;
        foreach (var alias in aliases.Append(symbol.SymbolId)
            .Append(symbol.Ticker)
            .Where(a => !a.IsEmpty()))
            _symbols[alias] = symbol;
    }

    private async Task<ExanteSymbol> GetSymbol(
        SecurityId securityId,
        CancellationToken cancellationToken)
    {
        var native = securityId.ToNativeSymbol();
        if (_symbols.TryGetValue(native, out var symbol))
            return symbol;
        symbol = await Rest.GetSymbol(native, cancellationToken);
        CacheSymbol(symbol, native);
        return symbol;
    }

    private void DisposeClient()
    {
        _rest?.Dispose();
        _rest = null;
    }

    /// <inheritdoc />
    protected override void DisposeManaged()
    {
        _connectionCts?.Cancel();
        _privateStreamCts?.Cancel();
        DisposeClient();
        _privateStreamSync.Dispose();
        base.DisposeManaged();
    }
}
