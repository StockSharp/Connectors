namespace StockSharp.MasterLink;

public partial class MasterLinkMessageAdapter
{
    private sealed class MarketSubscription
    {
        public long TransactionId { get; init; }
        public SecurityId SecurityId { get; init; }
        public DataType DataType { get; init; }
        public TimeSpan? TimeFrame { get; init; }
        public int? MaxDepth { get; init; }
    }

    private sealed class OrderTracker
    {
        public long TransactionId { get; init; }
        public SecurityId SecurityId { get; init; }
        public string PortfolioName { get; init; }
        public Sides Side { get; init; }
        public OrderTypes OrderType { get; init; }
        public TimeInForce TimeInForce { get; init; }
        public MasterLinkOrderCondition Condition { get; init; }
    }

    private sealed class LiveCandleState
    {
        public DateTime OpenTime { get; init; }
        public MasterLinkCandle Candle { get; init; }
        public MarketSubscription Subscription { get; init; }
    }

    private static readonly TimeSpan[] _timeFrames =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(3),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromMinutes(60),
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(7),
        TimeSpan.FromDays(30),
    ];

    private readonly SynchronizedDictionary<long, MarketSubscription>
        _marketSubscriptions = [];
    private readonly SynchronizedDictionary<string, MasterLinkSecurity>
        _securities = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<string, OrderTracker>
        _orders = new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<long, string>
        _transactionOrders = [];
    private readonly SynchronizedSet<string> _tradeIds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedDictionary<long, LiveCandleState>
        _liveCandles = [];
    private MasterLinkGatewayClient _client;
    private MasterLinkAccount _account;
    private MasterLinkAccount[] _accounts = [];
    private long _orderStatusSubscriptionId;
    private long _portfolioSubscriptionId;
    private string _portfolioFilter;
    private DateTime _lastHeartbeat;
    private DateTime _lastOrderRefresh;
    private DateTime _lastPortfolioRefresh;

    /// <summary>Supported candle time frames.</summary>
    public static IEnumerable<TimeSpan> AllTimeFrames => _timeFrames;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="MasterLinkMessageAdapter"/>.
    /// </summary>
    public MasterLinkMessageAdapter(IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        HeartbeatInterval = TimeSpan.FromSeconds(10);
        ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

        this.AddMarketDataSupport();
        this.AddTransactionalSupport();
        this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedMarketDataType(DataType.Ticks);
        this.AddSupportedMarketDataType(DataType.MarketDepth);
        this.AddSupportedCandleTimeFrames(AllTimeFrames);
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities ||
            dataType.IsTFCandles ||
            dataType == DataType.Transactions ||
            dataType == DataType.PositionChanges ||
            base.IsAllDownloadingSupported(dataType);

    /// <inheritdoc />
    public override bool IsReplaceCommandEditCurrent => true;

    /// <inheritdoc />
    public override bool IsSupportTransactionLog => true;

    /// <inheritdoc />
    public override bool IsSupportCandlesUpdates(
        MarketDataMessage subscription)
        => subscription.GetTimeFrame() ==
            TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    public override IEnumerable<int> SupportedOrderBookDepths { get; } =
        [5];

    /// <inheritdoc />
    public override string[] AssociatedBoards { get; } =
        ["TWSE", "TPEX", "TWEMERGING", "TWSEODD", "TPEXODD"];

    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(
        ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        if (_client != null)
        {
            throw new InvalidOperationException(
                LocalizedStrings.NotDisconnectPrevTime);
        }
        if (AccountPollingInterval < TimeSpan.FromSeconds(5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(AccountPollingInterval),
                AccountPollingInterval,
                "Account polling interval must be at least five seconds.");
        }
        if (MaxLookupResults is < 1 or > 100000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxLookupResults),
                MaxLookupResults,
                "Lookup limit must be between 1 and 100000.");
        }

        var client = new MasterLinkGatewayClient(
            NodePath, GatewayDirectory);
        client.MarketDataReceived += OnMarketData;
        client.OrderReceived += OnOrder;
        client.FillReceived += OnFill;
        client.Error += SendOutErrorAsync;
        client.Disconnected += OnConnectionLost;
        client.Log += OnGatewayLog;
        _client = client;
        try
        {
            var connection = await client.Connect(
                Login,
                Password,
                CertificatePath,
                CertificatePassword,
                Account,
                RegisterApiAuth,
                MarketDataMode,
                cancellationToken);
            _account = connection.Account;
            _accounts = connection.Accounts ?? [];
            connectMsg.SessionId =
                $"{connection.GatewayVersion}/{connection.SdkVersion}";
            _lastHeartbeat = _lastOrderRefresh =
                _lastPortfolioRefresh = CurrentTime;
            await base.ConnectAsync(connectMsg, cancellationToken);
        }
        catch
        {
            await DisposeClientAsync();
            throw;
        }
    }

    /// <inheritdoc />
    protected override async ValueTask DisconnectAsync(
        DisconnectMessage disconnectMsg,
        CancellationToken cancellationToken)
    {
        if (_client == null)
        {
            throw new InvalidOperationException(
                LocalizedStrings.ConnectionNotOk);
        }
        await DisposeClientAsync(cancellationToken);
        await base.DisconnectAsync(disconnectMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ResetAsync(
        ResetMessage resetMsg,
        CancellationToken cancellationToken)
    {
        await DisposeClientAsync();
        _marketSubscriptions.Clear();
        _securities.Clear();
        _orders.Clear();
        _transactionOrders.Clear();
        _tradeIds.Clear();
        _liveCandles.Clear();
        _account = null;
        _accounts = [];
        _orderStatusSubscriptionId = 0;
        _portfolioSubscriptionId = 0;
        _portfolioFilter = null;
        _lastHeartbeat = default;
        _lastOrderRefresh = default;
        _lastPortfolioRefresh = default;
        await base.ResetAsync(resetMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask TimeAsync(
        TimeMessage timeMsg,
        CancellationToken cancellationToken)
    {
        var now = CurrentTime;
        if (_client != null &&
            now - _lastHeartbeat >= TimeSpan.FromSeconds(20))
        {
            _lastHeartbeat = now;
            await _client.Ping(cancellationToken);
        }

        if (_client != null &&
            now - _lastOrderRefresh >= AccountPollingInterval)
        {
            _lastOrderRefresh = now;
            try
            {
                if (_orderStatusSubscriptionId != 0)
                {
                    await SendOrderSnapshot(
                        _orderStatusSubscriptionId,
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

        if (_client != null &&
            now - _lastPortfolioRefresh >= AccountPollingInterval)
        {
            _lastPortfolioRefresh = now;
            try
            {
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

    private ValueTask OnConnectionLost(
        Exception error,
        CancellationToken cancellationToken)
        => SendOutErrorAsync(error, cancellationToken);

    private ValueTask OnGatewayLog(
        int level,
        string message,
        CancellationToken cancellationToken)
    {
        if (!message.IsEmpty())
        {
            if (level >= 3)
                this.AddErrorLog(message);
            else
                this.AddWarningLog(message);
        }
        return default;
    }

    private async Task DisposeClientAsync(
        CancellationToken cancellationToken = default)
    {
        var client = _client;
        if (client == null)
            return;
        _client = null;
        client.MarketDataReceived -= OnMarketData;
        client.OrderReceived -= OnOrder;
        client.FillReceived -= OnFill;
        client.Error -= SendOutErrorAsync;
        client.Disconnected -= OnConnectionLost;
        client.Log -= OnGatewayLog;
        try
        {
            await client.Disconnect(cancellationToken);
        }
        finally
        {
            client.Dispose();
        }
    }

    private MasterLinkGatewayClient SafeClient()
        => _client ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private string PortfolioName
        => (_account?.Account).IsEmpty(Account).IsEmpty("MASTERLINK");

    private void CacheSecurity(MasterLinkSecurity security)
    {
        if (security?.Symbol.IsEmpty() != false)
            return;
        _securities[security.ToNativeKey()] = security;
        _securities[
            $"{security.Symbol.ToUpperInvariant()}|{(security.IsOddLot ? 1 : 0)}"] =
            security;
    }

    private MasterLinkSecurity ResolveSecurity(
        string symbol,
        string market = null,
        string marketType = null)
    {
        var odd = marketType.EqualsIgnoreCase("Odd") ||
            marketType.EqualsIgnoreCase("IntradayOdd");
        if (_securities.TryGetValue(
            $"{symbol?.ToUpperInvariant()}|{(odd ? 1 : 0)}",
            out var security))
        {
            return security;
        }

        var board = MasterLinkExtensions.ToBoardCode(
            market, marketType);
        security = new SecurityId
        {
            SecurityCode = symbol,
            BoardCode = board,
        }.ParseMasterLinkSecurity();
        CacheSecurity(security);
        return security;
    }
}
