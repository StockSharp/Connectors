namespace StockSharp.TossSecurities;

public partial class TossSecuritiesMessageAdapter
{
    private sealed class MarketSubscription
    {
        public long TransactionId { get; init; }
        public SecurityId SecurityId { get; init; }
        public DataType DataType { get; init; }
        public TimeSpan? TimeFrame { get; init; }
        public int? MaxDepth { get; init; }
    }

    private sealed class TrackedOrder
    {
        public long AccountSequence { get; init; }
        public bool IsConditional { get; init; }
    }

    private TossRestClient _restClient;
    private TossAccount[] _accounts = [];
    private readonly CachedSynchronizedDictionary<long, MarketSubscription>
        _marketSubscriptions = [];
    private readonly SynchronizedDictionary<string, string>
        _marketSignatures = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedSet<string> _seenPublicTrades =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, string>
        _orderSignatures = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, (decimal filled,
        decimal average, decimal commission)> _orderFills =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, long>
        _orderTransactions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, Sides>
        _conditionalSides = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, TrackedOrder>
        _trackedOrders = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, long>
        _accountByPortfolio = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<long, string>
        _portfolioByAccount = [];
    private long _resolvedAccountSequence;
    private long _orderStatusSubscriptionId;
    private long _portfolioSubscriptionId;
    private OrderStatusMessage _orderStatusFilter;
    private DateTimeOffset _lastMarketPoll;
    private DateTimeOffset _lastAccountPoll;
    private DateTimeOffset _lastOrderRefresh;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="TossSecuritiesMessageAdapter"/> class.
    /// </summary>
    /// <param name="transactionIdGenerator">
    /// Transaction identifier generator.
    /// </param>
    public TossSecuritiesMessageAdapter(
        IdGenerator transactionIdGenerator)
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
        [TimeSpan.FromMinutes(1), TimeSpan.FromDays(1)];

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType.IsTFCandles ||
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
    protected override async ValueTask ConnectAsync(
        ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        if (_restClient is not null)
        {
            throw new InvalidOperationException(
                LocalizedStrings.NotDisconnectPrevTime);
        }
        if (PollingInterval < TimeSpan.FromSeconds(1))
        {
            throw new InvalidOperationException(
                "Toss Securities market polling interval must be at least one second.");
        }
        if (AccountPollingInterval < TimeSpan.FromSeconds(1))
        {
            throw new InvalidOperationException(
                "Toss Securities account polling interval must be at least one second.");
        }

        _restClient = new(RestAddress) { Parent = this };
        try
        {
            await _restClient.Authenticate(
                Key, Secret, cancellationToken);
            _accounts = await _restClient.GetAccounts(cancellationToken);
            InitializeAccounts();
            _lastMarketPoll = _lastAccountPoll =
                DateTimeOffset.UtcNow;
            await base.ConnectAsync(connectMsg, cancellationToken);
        }
        catch
        {
            DisposeClient();
            throw;
        }
    }

    /// <inheritdoc />
    protected override async ValueTask DisconnectAsync(
        DisconnectMessage disconnectMsg,
        CancellationToken cancellationToken)
    {
        if (_restClient is null)
        {
            throw new InvalidOperationException(
                LocalizedStrings.ConnectionNotOk);
        }

        try
        {
            await base.DisconnectAsync(disconnectMsg, cancellationToken);
        }
        finally
        {
            DisposeClient();
        }
    }

    /// <inheritdoc />
    protected override async ValueTask ResetAsync(
        ResetMessage resetMsg,
        CancellationToken cancellationToken)
    {
        DisposeClient();
        _marketSubscriptions.Clear();
        _marketSignatures.Clear();
        _seenPublicTrades.Clear();
        _orderSignatures.Clear();
        _orderFills.Clear();
        _orderTransactions.Clear();
        _conditionalSides.Clear();
        _trackedOrders.Clear();
        _accountByPortfolio.Clear();
        _portfolioByAccount.Clear();
        _accounts = [];
        _resolvedAccountSequence = 0;
        _orderStatusSubscriptionId = 0;
        _portfolioSubscriptionId = 0;
        _orderStatusFilter = null;
        _lastMarketPoll = default;
        _lastAccountPoll = default;
        _lastOrderRefresh = default;
        await base.ResetAsync(resetMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask TimeAsync(
        TimeMessage timeMsg,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (_restClient is not null &&
            now - _lastMarketPoll >= PollingInterval)
        {
            _lastMarketPoll = now;
            try
            {
                await PollMarketData(cancellationToken);
            }
            catch (Exception error) when (
                error is not OperationCanceledException ||
                !cancellationToken.IsCancellationRequested)
            {
                await SendOutErrorAsync(error, cancellationToken);
            }
        }

        if (_restClient is not null &&
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
                        true,
                        cancellationToken);
                }
                if (_portfolioSubscriptionId != 0)
                {
                    await SendPortfolioSnapshot(
                        _portfolioSubscriptionId,
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
        _accountByPortfolio.Clear();
        _portfolioByAccount.Clear();

        var selected = AccountSequence > 0
            ? _accounts.FirstOrDefault(
                account => account.AccountSequence == AccountSequence)
            : _accounts.FirstOrDefault();
        if (AccountSequence > 0 &&
            _accounts.Length > 0 &&
            selected is null)
        {
            throw new InvalidOperationException(
                $"Toss Securities account sequence {AccountSequence} was not returned by the API.");
        }

        _resolvedAccountSequence =
            selected?.AccountSequence ?? AccountSequence;
        foreach (var account in _accounts)
        {
            var name =
                account.AccountSequence == _resolvedAccountSequence &&
                !PortfolioName.IsEmpty()
                    ? PortfolioName
                    : account.AccountNo.IsEmpty(
                        $"TOSS-{account.AccountSequence}");
            _portfolioByAccount[account.AccountSequence] = name;
            _accountByPortfolio[name] = account.AccountSequence;
            if (!account.AccountNo.IsEmpty())
                _accountByPortfolio[account.AccountNo] =
                    account.AccountSequence;
        }

        if (_resolvedAccountSequence > 0 &&
            !_portfolioByAccount.ContainsKey(_resolvedAccountSequence))
        {
            var name = PortfolioName.IsEmpty(
                $"TOSS-{_resolvedAccountSequence}");
            _portfolioByAccount[_resolvedAccountSequence] = name;
            _accountByPortfolio[name] = _resolvedAccountSequence;
        }
    }

    private long ResolveAccountSequence(string portfolioName)
    {
        if (!portfolioName.IsEmpty() &&
            _accountByPortfolio.TryGetValue(
                portfolioName, out var accountSequence))
            return accountSequence;
        if (_resolvedAccountSequence > 0)
            return _resolvedAccountSequence;

        throw new InvalidOperationException(
            "No Toss Securities brokerage account is available.");
    }

    private string ResolvePortfolioName(long accountSequence)
        => _portfolioByAccount.TryGetValue(
            accountSequence, out var portfolioName)
                ? portfolioName
                : PortfolioName.IsEmpty($"TOSS-{accountSequence}");

    private void DisposeClient()
    {
        _restClient?.Dispose();
        _restClient = null;
    }
}
