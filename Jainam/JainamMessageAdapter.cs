namespace StockSharp.Jainam;

public partial class JainamMessageAdapter
{
    private JainamRestClient _restClient;
    private JainamSocketClient _socketClient;
    private string _resolvedUserId;
    private string _resolvedPortfolioName;
    private bool _isSocketSessionCreated;
    private DateTime _lastHeartbeat;
    private DateTime _lastPortfolioRefresh;
    private DateTime _lastOrderRefresh;

    /// <summary>
    /// Initializes a new instance of the <see cref="JainamMessageAdapter"/> class.
    /// </summary>
    public JainamMessageAdapter(IdGenerator transactionIdGenerator)
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
    public override string[] AssociatedBoards { get; } =
        ["NSE", "BSE", "NFO", "BFO", "CDS", "BCD", "MCX"];

    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(
        ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        if (_restClient != null)
            throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
        if (PollingInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PollingInterval),
                PollingInterval,
                "Polling interval must be positive.");
        }
        if (ReconnectAttempts < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ReconnectAttempts),
                ReconnectAttempts,
                "Reconnect attempts cannot be negative.");
        }
        if (Token.IsEmpty())
        {
            UserId.ThrowIfEmpty(nameof(UserId));
            AuthCode.ThrowIfEmpty(nameof(AuthCode));
            ApiSecret.ThrowIfEmpty(nameof(ApiSecret));
        }

        _restClient = new(RestAddress, InstrumentAddress)
        {
            Parent = this,
        };

        try
        {
            var login = await _restClient.Authenticate(
                UserId,
                Token,
                AuthCode,
                ApiSecret,
                cancellationToken);
            Token = login.token.Secure();
            _resolvedUserId = login.userId;

            var profile = await _restClient.GetProfile(cancellationToken);
            _resolvedUserId = _resolvedUserId
                .IsEmpty(profile.ClientId)
                .IsEmpty(UserId);
            _resolvedPortfolioName = PortfolioName
                .IsEmpty(profile.ClientId)
                .IsEmpty(_resolvedUserId)
                .IsEmpty("Jainam");

            if (this.IsMarketData())
            {
                await _restClient.CreateSocketSession(
                    _resolvedUserId,
                    cancellationToken);
                _isSocketSessionCreated = true;
                _socketClient = new(
                    WebSocketAddress,
                    _resolvedUserId,
                    _restClient.SessionToken,
                    ReconnectAttempts,
                    ReConnectionSettings.WorkingTime)
                {
                    Parent = this,
                };
                _socketClient.MarketDataReceived += OnMarketDataReceived;
                _socketClient.Error += SendOutErrorAsync;
                _socketClient.StateChanged += SendOutConnectionStateAsync;
                await _socketClient.Connect(cancellationToken);
            }

            await base.ConnectAsync(connectMsg, cancellationToken);
        }
        catch
        {
            await DisposeClients(cancellationToken, true);
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

        try
        {
            if (_socketClient != null)
                await _socketClient.Disconnect(cancellationToken);
            if (_isSocketSessionCreated)
            {
                await _restClient.InvalidateSocketSession(
                    _resolvedUserId,
                    cancellationToken);
                _isSocketSessionCreated = false;
            }
            await base.DisconnectAsync(disconnectMsg, cancellationToken);
        }
        finally
        {
            await DisposeClients(cancellationToken, false);
        }
    }

    /// <inheritdoc />
    protected override async ValueTask TimeAsync(
        TimeMessage timeMsg,
        CancellationToken cancellationToken)
    {
        if (_socketClient != null &&
            CurrentTime - _lastHeartbeat >= TimeSpan.FromSeconds(40))
        {
            await _socketClient.SendHeartbeat(cancellationToken);
            _lastHeartbeat = CurrentTime;
        }

        if (_orderStatusSubscriptionId != 0 &&
            CurrentTime - _lastOrderRefresh >= PollingInterval)
        {
            await SendOrderSnapshot(
                _orderStatusSubscriptionId,
                false,
                cancellationToken);
            _lastOrderRefresh = CurrentTime;
        }

        if (_portfolioSubscriptionId != 0 &&
            CurrentTime - _lastPortfolioRefresh >= PollingInterval)
        {
            await SendPortfolioSnapshot(
                _portfolioSubscriptionId,
                cancellationToken);
            _lastPortfolioRefresh = CurrentTime;
        }

        await base.TimeAsync(timeMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ResetAsync(
        ResetMessage resetMsg,
        CancellationToken cancellationToken)
    {
        await DisposeClients(cancellationToken, true);

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
        _resolvedUserId = null;
        _resolvedPortfolioName = null;
        _isSocketSessionCreated = false;
        _lastHeartbeat = default;
        _lastPortfolioRefresh = default;
        _lastOrderRefresh = default;

        await base.ResetAsync(resetMsg, cancellationToken);
    }

    private async ValueTask DisposeClients(
        CancellationToken cancellationToken,
        bool invalidateSession)
    {
        if (_socketClient != null)
        {
            _socketClient.MarketDataReceived -= OnMarketDataReceived;
            _socketClient.Error -= SendOutErrorAsync;
            _socketClient.StateChanged -= SendOutConnectionStateAsync;
            _socketClient.Dispose();
            _socketClient = null;
        }

        if (invalidateSession &&
            _isSocketSessionCreated &&
            _restClient != null)
        {
            try
            {
                await _restClient.InvalidateSocketSession(
                    _resolvedUserId,
                    cancellationToken);
            }
            catch (Exception error)
            {
                this.AddWarningLog(
                    "Jainam WebSocket session cleanup failed: {0}",
                    error.Message);
            }
        }

        _isSocketSessionCreated = false;
        _restClient?.Dispose();
        _restClient = null;
    }
}
