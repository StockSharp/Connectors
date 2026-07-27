namespace StockSharp.Definedge;

public partial class DefinedgeMessageAdapter
{
    private DefinedgeRestClient _restClient;
    private DefinedgeSocketClient _socketClient;
    private DateTime _nextHeartbeat;
    private DateTime _nextPolling;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="DefinedgeMessageAdapter"/> class.
    /// </summary>
    public DefinedgeMessageAdapter(IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        HeartbeatInterval = TimeSpan.FromSeconds(1);
        ReConnectionSettings.TimeOutInterval =
            TimeSpan.FromMinutes(2);

        this.AddMarketDataSupport();
        this.AddTransactionalSupport();
        this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedMarketDataType(DataType.Ticks);
        this.AddSupportedMarketDataType(DataType.MarketDepth);
        this.AddSupportedCandleTimeFrames(AllTimeFrames);
    }

    /// <summary>
    /// Candle time frames supported by Definedge.
    /// </summary>
    public static IEnumerable<TimeSpan> AllTimeFrames
        => [TimeSpan.FromMinutes(1), TimeSpan.FromDays(1)];

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities ||
            dataType == DataType.Transactions ||
            dataType == DataType.PositionChanges ||
            base.IsAllDownloadingSupported(dataType);

    /// <inheritdoc />
    public override bool IsSupportCandlesUpdates(
        MarketDataMessage subscription) => false;

    /// <inheritdoc />
    public override bool IsReplaceCommandEditCurrent => true;

    /// <inheritdoc />
    public override bool IsSupportTransactionLog => true;

    /// <inheritdoc />
    public override IEnumerable<int> SupportedOrderBookDepths
    { get; } = [5];

    /// <inheritdoc />
    public override string[] AssociatedBoards { get; } =
        ["NSE", "BSE", "NFO", "BFO", "CDS", "MCX"];

    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(
        ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        if (_restClient != null)
            throw new InvalidOperationException(
                LocalizedStrings.NotDisconnectPrevTime);
        if (PollingInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Definedge polling interval must be positive.");
        }

        if (Token.IsEmpty())
        {
            if (Key.IsEmpty() || Secret.IsEmpty() ||
                OneTimePassword.IsEmpty())
            {
                throw new InvalidOperationException(
                    "API token, API secret, and current OTP are required to create a Definedge session.");
            }

            var session = await DefinedgeRestClient.Login(
                LoginAddress,
                Key.UnSecure(),
                Secret.UnSecure(),
                OneTimePassword.UnSecure(),
                cancellationToken);
            UserId = session.UserId;
            AccountId = session.AccountId;
            Token = session.ApiSessionKey.Secure();
            WebSocketToken = session.WebSocketToken.Secure();
        }

        UserId.ThrowIfEmpty(nameof(UserId));
        AccountId.ThrowIfEmpty(nameof(AccountId));
        Token.ThrowIfEmpty(nameof(Token));
        AlgoId.ThrowIfEmpty(nameof(AlgoId));

        _restClient = new(
            Address,
            HistoryAddress,
            InstrumentMasterAddress,
            Token.UnSecure(),
            ReConnectionSettings.ReAttemptCount + 1)
        {
            Parent = this,
        };

        try
        {
            if (this.IsTransactional())
                await _restClient.GetLimits(cancellationToken);

            if (!WebSocketToken.IsEmpty() &&
                (this.IsMarketData() || this.IsTransactional()))
            {
                _socketClient = new(
                    WebSocketAddress,
                    UserId,
                    AccountId,
                    WebSocketToken.UnSecure(),
                    this.IsTransactional(),
                    ReConnectionSettings.ReAttemptCount,
                    ReConnectionSettings.WorkingTime)
                {
                    Parent = this,
                };
                _socketClient.MarketDataReceived +=
                    OnMarketDataReceived;
                _socketClient.OrderReceived += OnOrderReceived;
                _socketClient.Error += SendOutErrorAsync;
                _socketClient.StateChanged +=
                    SendOutConnectionStateAsync;
                await _socketClient.Connect(cancellationToken);
            }

            _nextHeartbeat = CurrentTime +
                TimeSpan.FromSeconds(45);
            _nextPolling = CurrentTime + PollingInterval;
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
        if (_restClient == null)
        {
            throw new InvalidOperationException(
                LocalizedStrings.ConnectionNotOk);
        }

        try
        {
            if (_socketClient != null)
                await _socketClient.Disconnect(cancellationToken);
            await base.DisconnectAsync(
                disconnectMsg, cancellationToken);
        }
        finally
        {
            DisposeClients();
        }
    }

    /// <inheritdoc />
    protected override async ValueTask TimeAsync(
        TimeMessage timeMsg,
        CancellationToken cancellationToken)
    {
        if (_socketClient != null &&
            CurrentTime >= _nextHeartbeat)
        {
            await _socketClient.SendHeartbeat(cancellationToken);
            _nextHeartbeat = CurrentTime +
                TimeSpan.FromSeconds(45);
        }

        if (_restClient != null && CurrentTime >= _nextPolling)
        {
            _nextPolling = CurrentTime + PollingInterval;
            if (_orderStatusSubscriptionId != 0)
            {
                await SendOrderSnapshot(
                    _orderStatusSubscriptionId,
                    false,
                    cancellationToken);
            }
            if (_portfolioSubscriptionId != 0)
            {
                await SendPortfolioSnapshot(
                    _portfolioSubscriptionId,
                    cancellationToken);
            }
        }

        await base.TimeAsync(timeMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ResetAsync(
        ResetMessage resetMsg,
        CancellationToken cancellationToken)
    {
        DisposeClients();
        _marketSubscriptions.Clear();
        _securityIds.Clear();
        _instruments.Clear();
        _marketStates.Clear();
        _lastTicks.Clear();
        _orderTransactions.Clear();
        _transactionOrders.Clear();
        _tradeIds.Clear();
        _orderStatusSubscriptionId = 0;
        _portfolioSubscriptionId = 0;
        _nextHeartbeat = default;
        _nextPolling = default;
        await base.ResetAsync(resetMsg, cancellationToken);
    }

    private DefinedgeSocketClient SocketClient =>
        _socketClient ?? throw new InvalidOperationException(
            WebSocketToken.IsEmpty()
                ? "Definedge WebSocket token is required for live subscriptions."
                : LocalizedStrings.ConnectionNotOk);

    private void DisposeClients()
    {
        if (_socketClient != null)
        {
            _socketClient.MarketDataReceived -=
                OnMarketDataReceived;
            _socketClient.OrderReceived -= OnOrderReceived;
            _socketClient.Error -= SendOutErrorAsync;
            _socketClient.StateChanged -=
                SendOutConnectionStateAsync;
            _socketClient.Dispose();
            _socketClient = null;
        }

        _restClient?.Dispose();
        _restClient = null;
    }
}
