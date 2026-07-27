namespace StockSharp.Bigul;

public partial class BigulMessageAdapter
{
    private BigulRestClient _restClient;
    private BigulSocketClient _socketClient;
    private string _resolvedPortfolioName;
    private DateTime _lastPortfolioRefresh;
    private DateTime _lastOrderRefresh;
    private DateTime _lastSocketHeartbeat;

    /// <summary>
    /// Initializes a new instance of the <see cref="BigulMessageAdapter"/>.
    /// </summary>
    public BigulMessageAdapter(IdGenerator transactionIdGenerator)
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
        ["NSE", "BSE", "NFO", "BFO", "CDS", "MCX", "NCDEX"];

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
        if (MarketProtection < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MarketProtection),
                MarketProtection,
                "Market protection cannot be negative.");
        }
        if (PollingInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PollingInterval),
                PollingInterval,
                "Polling interval must be positive.");
        }

        ClientCode.ThrowIfEmpty(nameof(ClientCode));
        Source.ThrowIfEmpty(nameof(Source));
        _restClient = new(
            ClientCode,
            ApiKey,
            ApiSecret,
            Token,
            Source,
            Address,
            MasterAddress)
        {
            Parent = this,
        };

        try
        {
            var login = await _restClient.Authenticate(
                OneTimePassword,
                cancellationToken);
            Token = login.AccessToken.Secure();
            _resolvedPortfolioName = PortfolioName
                .IsEmpty(login.ClientCode)
                .IsEmpty(ClientCode);

            if (this.IsTransactional())
                await _restClient.GetLimits(cancellationToken);

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
                if (this.IsTransactional())
                {
                    _socketClient.OrderReceived += OnOrderReceived;
                    _socketClient.TradeReceived += OnTradeReceived;
                    _socketClient.PositionReceived += OnPositionReceived;
                }
                _socketClient.Error += SendOutErrorAsync;
                _socketClient.StateChanged += SendOutConnectionStateAsync;
                await _socketClient.Connect(cancellationToken);
            }

            _lastSocketHeartbeat = CurrentTime;
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
        if (_socketClient != null &&
            CurrentTime - _lastSocketHeartbeat >= HeartbeatInterval)
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
        await DisposeClients();

        _marketSubscriptions.Clear();
        _securityIds.Clear();
        _instruments.Clear();
        _lastTicks.Clear();
        _orderTransactions.Clear();
        _transactionOrders.Clear();
        _tradeIds.Clear();
        _afterMarketOrders.Clear();
        _orderStatusSubscriptionId = 0;
        _portfolioSubscriptionId = 0;
        _resolvedPortfolioName = null;
        _lastPortfolioRefresh = default;
        _lastOrderRefresh = default;
        _lastSocketHeartbeat = default;

        await base.ResetAsync(resetMsg, cancellationToken);
    }

    private async ValueTask OnPositionReceived(
        BigulPosition position,
        CancellationToken cancellationToken)
    {
        _lastPortfolioRefresh = default;
        if (_portfolioSubscriptionId != 0)
        {
            await ProcessPosition(
                position,
                _portfolioSubscriptionId,
                cancellationToken);
        }
    }

    private ValueTask DisposeClients()
    {
        if (_socketClient != null)
        {
            _socketClient.TickReceived -= OnMarketDataReceived;
            _socketClient.OrderReceived -= OnOrderReceived;
            _socketClient.TradeReceived -= OnTradeReceived;
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
