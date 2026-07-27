namespace StockSharp.Primary;

public partial class PrimaryMessageAdapter
{
    private PrimaryRestClient _rest;
    private PrimarySocketClient _socket;
    private DateTime _lastOrderPoll;
    private DateTime _lastPortfolioPoll;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="PrimaryMessageAdapter"/> class.
    /// </summary>
    public PrimaryMessageAdapter(IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        HeartbeatInterval = TimeSpan.FromSeconds(1);
        ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

        this.AddMarketDataSupport();
        this.AddTransactionalSupport();
        this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
        this.AddSupportedMarketDataType(DataType.Ticks);
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedMarketDataType(DataType.MarketDepth);
    }

    /// <inheritdoc />
    public override string[] AssociatedBoards { get; } =
        ["ROFEX", "BYMA"];

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities ||
            dataType == DataType.Ticks ||
            dataType == DataType.Transactions ||
            dataType == DataType.PositionChanges ||
            base.IsAllDownloadingSupported(dataType);

    /// <inheritdoc />
    public override bool IsReplaceCommandEditCurrent => true;

    /// <inheritdoc />
    public override bool IsSupportTransactionLog => true;

    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(
        ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        if (_rest is not null || _socket is not null)
        {
            throw new InvalidOperationException(
                LocalizedStrings.NotDisconnectPrevTime);
        }
        if (AccountPollingInterval < TimeSpan.FromSeconds(5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(AccountPollingInterval),
                AccountPollingInterval,
                "Primary account polling interval must be at least five seconds.");
        }
        if (LookupLimit is < 1 or > 100000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(LookupLimit),
                LookupLimit,
                "Primary lookup limit must be between 1 and 100000.");
        }
        if (MarketDataLevel is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MarketDataLevel),
                MarketDataLevel,
                "Primary market-data level must be between 1 and 5.");
        }
        DefaultMarket.ThrowIfEmpty(nameof(DefaultMarket));

        _rest = new(
            IsDemo ? SandboxRestAddress : RestAddress,
            Login,
            Password,
            Token)
        {
            Parent = this,
        };

        try
        {
            await _rest.Authenticate(cancellationToken);
            CaptureToken();

            _socket = new(
                IsDemo
                    ? SandboxWebSocketAddress
                    : WebSocketAddress,
                _rest,
                MarketDataLevel,
                Math.Max(1, ReConnectionSettings.ReAttemptCount),
                ReConnectionSettings.WorkingTime)
            {
                Parent = this,
            };
            _socket.MarketDataReceived += ProcessMarketUpdate;
            _socket.OrderReceived += ProcessOrderUpdate;
            _socket.Error += SendOutErrorAsync;
            _socket.StateChanged += SendOutConnectionStateAsync;
            await _socket.Connect(cancellationToken);

            if (!Account.IsEmpty())
            {
                await _socket.SubscribeOrders(
                    Account, cancellationToken);
            }

            connectMsg.SessionId = IsDemo
                ? "Primary/reMarkets"
                : "Primary/live";
            _lastOrderPoll = _lastPortfolioPoll = CurrentTime;
            await base.ConnectAsync(connectMsg, cancellationToken);
        }
        catch
        {
            await DisposeClients(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    protected override async ValueTask DisconnectAsync(
        DisconnectMessage disconnectMsg,
        CancellationToken cancellationToken)
    {
        if (_rest is null)
        {
            throw new InvalidOperationException(
                LocalizedStrings.ConnectionNotOk);
        }

        try
        {
            if (_socket is not null)
                await _socket.Disconnect(cancellationToken);
            CaptureToken();
            await base.DisconnectAsync(
                disconnectMsg, cancellationToken);
        }
        finally
        {
            await DisposeClients(cancellationToken);
        }
    }

    /// <inheritdoc />
    protected override async ValueTask TimeAsync(
        TimeMessage timeMsg,
        CancellationToken cancellationToken)
    {
        if (_orderStatusSubscriptionId != 0 &&
            CurrentTime - _lastOrderPoll >= AccountPollingInterval)
        {
            _lastOrderPoll = CurrentTime;
            try
            {
                await SendOrderSnapshot(
                    _orderStatusSubscriptionId,
                    false,
                    null,
                    null,
                    null,
                    cancellationToken);
            }
            catch (Exception error) when (
                error is not OperationCanceledException ||
                    !cancellationToken.IsCancellationRequested)
            {
                await SendOutErrorAsync(error, cancellationToken);
            }
        }

        if (_portfolioSubscriptionId != 0 &&
            CurrentTime - _lastPortfolioPoll >= AccountPollingInterval)
        {
            _lastPortfolioPoll = CurrentTime;
            try
            {
                await SendPortfolioSnapshot(
                    _portfolioSubscriptionId, cancellationToken);
            }
            catch (Exception error) when (
                error is not OperationCanceledException ||
                    !cancellationToken.IsCancellationRequested)
            {
                await SendOutErrorAsync(error, cancellationToken);
            }
        }

        CaptureToken();
        await base.TimeAsync(timeMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ResetAsync(
        ResetMessage resetMsg,
        CancellationToken cancellationToken)
    {
        await DisposeClients(cancellationToken);
        _marketSubscriptions.Clear();
        _instruments.Clear();
        _securityIds.Clear();
        _seenMarketTrades.Clear();
        _orderTransactions.Clear();
        _transactionOrders.Clear();
        _orderReferences.Clear();
        _exchangeOrderClients.Clear();
        _orderSides.Clear();
        _orderSecurities.Clear();
        _executedQuantities.Clear();
        _tradeIds.Clear();
        _orderStatusSubscriptionId = 0;
        _portfolioSubscriptionId = 0;
        _lastOrderPoll = default;
        _lastPortfolioPoll = default;
        await base.ResetAsync(resetMsg, cancellationToken);
    }

    private void CaptureToken()
    {
        if (_rest?.Token.IsEmpty() == false)
            Token = _rest.Token.Secure();
    }

    private async ValueTask DisposeClients(
        CancellationToken cancellationToken)
    {
        if (_socket is not null)
        {
            _socket.MarketDataReceived -= ProcessMarketUpdate;
            _socket.OrderReceived -= ProcessOrderUpdate;
            _socket.Error -= SendOutErrorAsync;
            _socket.StateChanged -= SendOutConnectionStateAsync;
            try
            {
                await _socket.Disconnect(cancellationToken);
            }
            catch (Exception error) when (
                error is OperationCanceledException or IOException or
                    WebSocketException)
            {
                this.AddVerboseLog(
                    "Primary WebSocket cleanup: {0}", error.Message);
            }
            _socket.Dispose();
            _socket = null;
        }

        if (_rest is not null)
        {
            CaptureToken();
            _rest.Dispose();
            _rest = null;
        }
    }
}
