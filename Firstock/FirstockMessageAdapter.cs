namespace StockSharp.Firstock;

public partial class FirstockMessageAdapter
{
    private static readonly TimeSpan[] _timeFrames =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(3),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromMinutes(60),
        TimeSpan.FromDays(1),
    ];

    private FirstockRestClient _restClient;
    private FirstockSocketClient _socketClient;
    private string _resolvedPortfolioName;
    private DateTime _lastPortfolioRefresh;

    /// <summary>
    /// Supported candle time frames.
    /// </summary>
    public static IEnumerable<TimeSpan> AllTimeFrames => _timeFrames;

    /// <summary>
    /// Initializes a new instance of the <see cref="FirstockMessageAdapter"/>.
    /// </summary>
    public FirstockMessageAdapter(IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        HeartbeatInterval = TimeSpan.FromSeconds(10);
        ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

        this.AddMarketDataSupport();
        this.AddTransactionalSupport();
        this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);

        this.AddSupportedMarketDataType(DataType.Ticks);
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedMarketDataType(DataType.MarketDepth);
        this.AddSupportedCandleTimeFrames(AllTimeFrames);
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities ||
            dataType == DataType.Transactions ||
            dataType == DataType.PositionChanges ||
            base.IsAllDownloadingSupported(dataType);

    /// <inheritdoc />
    public override bool IsReplaceCommandEditCurrent => true;

    /// <inheritdoc />
    public override bool IsSupportTransactionLog => true;

    /// <inheritdoc />
    public override IEnumerable<int> SupportedOrderBookDepths { get; } = [5];

    /// <inheritdoc />
    public override string[] AssociatedBoards { get; } = ["NSE", "BSE", "NFO", "BFO"];

    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(
        ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        if (_restClient != null)
            throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
        if (ReconnectAttempts < 0)
            throw new ArgumentOutOfRangeException(
                nameof(ReconnectAttempts), ReconnectAttempts, "Reconnect attempts cannot be negative.");
        if (PriceDivisor <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(PriceDivisor), PriceDivisor, "Streaming price divisor must be positive.");
        if (MarketProtection <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(MarketProtection), MarketProtection, "Market protection must be positive.");
        if (PollingInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(PollingInterval), PollingInterval, "Polling interval must be positive.");

        UserId.ThrowIfEmpty(nameof(UserId));
        _restClient = new(UserId, Token, Address, SymbolsAddress) { Parent = this };

        try
        {
            var login = await _restClient.Authenticate(
                Password,
                OneTimePassword,
                VendorCode,
                ApiKey,
                cancellationToken);
            Token = login.SessionToken.Secure();
            _resolvedPortfolioName = PortfolioName
                .IsEmpty(login.AccountId)
                .IsEmpty(UserId);

            if (this.IsTransactional())
                await _restClient.GetLimits(cancellationToken);

            if (this.IsMarketData() || this.IsTransactional())
            {
                _socketClient = new(
                    UserId,
                    _restClient.SessionToken,
                    ReconnectAttempts,
                    ReConnectionSettings.WorkingTime,
                    WebSocketAddress)
                {
                    Parent = this,
                };
                _socketClient.MarketDataReceived += OnMarketDataReceived;
                if (this.IsTransactional())
                {
                    _socketClient.OrderReceived += OnOrderReceived;
                    _socketClient.PositionReceived += OnPositionReceived;
                }
                _socketClient.Error += SendOutErrorAsync;
                _socketClient.StateChanged += SendOutConnectionStateAsync;
                await _socketClient.Connect(cancellationToken);
            }

            await base.ConnectAsync(connectMsg, cancellationToken);
        }
        catch
        {
            await DisposeClients();
            throw;
        }
    }

    /// <inheritdoc />
    protected override async ValueTask DisconnectAsync(
        DisconnectMessage disconnectMsg,
        CancellationToken cancellationToken)
    {
        if (_restClient == null)
            throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

        if (_socketClient != null)
            await _socketClient.Disconnect(cancellationToken);
        await base.DisconnectAsync(disconnectMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask TimeAsync(
        TimeMessage timeMsg,
        CancellationToken cancellationToken)
    {
        if (_portfolioSubscriptionId != 0 &&
            CurrentTime - _lastPortfolioRefresh >= PollingInterval)
        {
            await SendPortfolioSnapshot(_portfolioSubscriptionId, cancellationToken);
            _lastPortfolioRefresh = CurrentTime;
        }

        await base.TimeAsync(timeMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ResetAsync(
        ResetMessage resetMsg,
        CancellationToken cancellationToken)
    {
        await DisposeClients();

        _marketSubscriptions.Clear();
        _securityIds.Clear();
        _instruments.Clear();
        _lastTicks.Clear();
        _orderTransactions.Clear();
        _transactionOrders.Clear();
        _tradeIds.Clear();
        _orderStatusSubscriptionId = 0;
        _portfolioSubscriptionId = 0;
        _resolvedPortfolioName = null;
        _lastPortfolioRefresh = default;

        await base.ResetAsync(resetMsg, cancellationToken);
    }

    private ValueTask OnPositionReceived(
        FirstockPosition position,
        CancellationToken cancellationToken)
    {
        _lastPortfolioRefresh = default;
        return default;
    }

    private ValueTask DisposeClients()
    {
        if (_socketClient != null)
        {
            _socketClient.MarketDataReceived -= OnMarketDataReceived;
            _socketClient.OrderReceived -= OnOrderReceived;
            _socketClient.PositionReceived -= OnPositionReceived;
            _socketClient.Error -= SendOutErrorAsync;
            _socketClient.StateChanged -= SendOutConnectionStateAsync;
            _socketClient.Dispose();
            _socketClient = null;
        }

        _restClient?.Dispose();
        _restClient = null;
        return default;
    }
}
