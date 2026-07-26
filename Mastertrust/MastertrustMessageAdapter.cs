namespace StockSharp.Mastertrust;

public partial class MastertrustMessageAdapter
{
    private MastertrustRestClient _restClient;
    private MastertrustSocketClient _socketClient;
    private string _resolvedPortfolioName;
    private DateTime _lastPortfolioRefresh;
    private DateTime _lastOrderRefresh;
    private DateTime _lastSocketHeartbeat;

    /// <summary>
    /// Initializes a new instance of the <see cref="MastertrustMessageAdapter"/>.
    /// </summary>
    public MastertrustMessageAdapter(IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        HeartbeatInterval = TimeSpan.FromSeconds(3);
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
        ["NSE", "BSE", "NFO", "BFO", "MCX", "CDS", "BCD"];

    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(
        ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        if (_restClient != null)
            throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
        if (ReconnectAttempts < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ReconnectAttempts),
                ReconnectAttempts,
                "Reconnect attempts cannot be negative.");
        }
        if (PollingInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PollingInterval),
                PollingInterval,
                "Polling interval must be positive.");
        }

        ClientId.ThrowIfEmpty(nameof(ClientId));
        if (Token.IsEmpty())
        {
            OAuthClientId.ThrowIfEmpty(nameof(OAuthClientId));
            OAuthClientSecret.ThrowIfEmpty(nameof(OAuthClientSecret));
            AuthorizationCode.ThrowIfEmpty(nameof(AuthorizationCode));
            _ = RedirectUri ?? throw new ArgumentNullException(nameof(RedirectUri));
        }

        _restClient = new(
            ClientId,
            OAuthClientId,
            OAuthClientSecret,
            Token,
            RedirectUri,
            Address,
            MasterAddress)
        {
            Parent = this,
        };

        try
        {
            var login = await _restClient.Authenticate(
                AuthorizationCode,
                cancellationToken);
            Token = login.AccessToken.Secure();

            var profile = await _restClient.GetProfile(cancellationToken);
            _resolvedPortfolioName = PortfolioName
                .IsEmpty(profile?.ClientId)
                .IsEmpty(ClientId);

            if (this.IsMarketData() || this.IsTransactional())
            {
                _socketClient = new(
                    ClientId,
                    _restClient.AccessToken,
                    ReconnectAttempts,
                    ReConnectionSettings.WorkingTime,
                    WebSocketAddress)
                {
                    Parent = this,
                };
                _socketClient.MarketDataReceived += OnMarketDataReceived;
                _socketClient.UpdateReceived += OnSocketUpdate;
                _socketClient.Error += SendOutErrorAsync;
                _socketClient.StateChanged += SendOutConnectionStateAsync;
                await _socketClient.Connect(cancellationToken);
                _lastSocketHeartbeat = CurrentTime;
            }

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
        if (_socketClient != null &&
            CurrentTime - _lastSocketHeartbeat >= TimeSpan.FromSeconds(9))
        {
            await _socketClient.SendHeartbeat(cancellationToken);
            _lastSocketHeartbeat = CurrentTime;
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
        DisposeClients();

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
        _lastOrderRefresh = default;
        _lastSocketHeartbeat = default;

        await base.ResetAsync(resetMsg, cancellationToken);
    }

    private void DisposeClients()
    {
        if (_socketClient != null)
        {
            _socketClient.MarketDataReceived -= OnMarketDataReceived;
            _socketClient.UpdateReceived -= OnSocketUpdate;
            _socketClient.Error -= SendOutErrorAsync;
            _socketClient.StateChanged -= SendOutConnectionStateAsync;
            _socketClient.Dispose();
            _socketClient = null;
        }

        _restClient?.Dispose();
        _restClient = null;
    }
}
