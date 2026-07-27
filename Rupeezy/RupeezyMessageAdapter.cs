namespace StockSharp.Rupeezy;

public partial class RupeezyMessageAdapter
{
    private RupeezyRestClient _restClient;
    private RupeezySocketClient _socketClient;
    private string _resolvedPortfolioName;
    private DateTime _lastPortfolioRefresh;
    private DateTime _lastOrderRefresh;

    /// <summary>
    /// Initializes a new instance of the <see cref="RupeezyMessageAdapter"/>.
    /// </summary>
    public RupeezyMessageAdapter(IdGenerator transactionIdGenerator)
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
        this.AddSupportedCandleTimeFrames(RupeezyExtensions.TimeFrames.Keys);
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
        ["NSE", "BSE", "NFO", "BFO", "CDS", "MCX"];

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

        ApplicationId.ThrowIfEmpty(nameof(ApplicationId));
        ApiKey.ThrowIfEmpty(nameof(ApiKey));
        _restClient = new(
            ApplicationId,
            ApiKey,
            Token,
            Address,
            MasterAddress)
        {
            Parent = this,
        };

        try
        {
            var login = await _restClient.Authenticate(
                AuthCode,
                cancellationToken);
            Token = login.AccessToken.Secure();
            _resolvedPortfolioName = PortfolioName
                .IsEmpty(login.UserId)
                .IsEmpty(ApplicationId);

            if (this.IsMarketData() || this.IsTransactional())
            {
                _socketClient = new(
                    _restClient.AccessToken,
                    ReconnectAttempts,
                    ReConnectionSettings.WorkingTime,
                    WebSocketAddress)
                {
                    Parent = this,
                };
                _socketClient.TickReceived += OnMarketDataReceived;
                _socketClient.OrderReceived += OnSocketUpdate;
                _socketClient.Error += SendOutErrorAsync;
                _socketClient.StateChanged += SendOutConnectionStateAsync;
                await _socketClient.Connect(cancellationToken);
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
        _orderFills.Clear();
        _tradeIds.Clear();
        _orderStatusSubscriptionId = 0;
        _portfolioSubscriptionId = 0;
        _resolvedPortfolioName = null;
        _lastPortfolioRefresh = default;
        _lastOrderRefresh = default;

        await base.ResetAsync(resetMsg, cancellationToken);
    }

    private void DisposeClients()
    {
        if (_socketClient != null)
        {
            _socketClient.TickReceived -= OnMarketDataReceived;
            _socketClient.OrderReceived -= OnSocketUpdate;
            _socketClient.Error -= SendOutErrorAsync;
            _socketClient.StateChanged -= SendOutConnectionStateAsync;
            _socketClient.Dispose();
            _socketClient = null;
        }

        _restClient?.Dispose();
        _restClient = null;
    }
}
