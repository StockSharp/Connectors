namespace StockSharp.ChoiceFinX;

public partial class ChoiceFinXMessageAdapter
{
    private ChoiceFinXRestClient _restClient;
    private ChoiceFinXSocketClient _socketClient;
    private string _portfolioName;
    private DateTime _nextPolling;
    private DateTime _nextHeartbeat;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="ChoiceFinXMessageAdapter"/> class.
    /// </summary>
    public ChoiceFinXMessageAdapter(
        IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        HeartbeatInterval = TimeSpan.FromSeconds(1);
        ReConnectionSettings.TimeOutInterval =
            TimeSpan.FromMinutes(2);

        this.AddMarketDataSupport();
        this.AddTransactionalSupport();
        this.RemoveSupportedMessage(
            MessageTypes.OrderGroupCancel);
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedMarketDataType(DataType.Ticks);
        this.AddSupportedMarketDataType(
            DataType.MarketDepth);
        this.AddSupportedCandleTimeFrames(AllTimeFrames);
    }

    /// <summary>
    /// Candle time frames supported by Choice FinX.
    /// </summary>
    public static IEnumerable<TimeSpan> AllTimeFrames =>
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromDays(1),
    ];

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(
        DataType dataType)
        => dataType == DataType.Transactions ||
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
    [
        "NSE",
        "NFO",
        "BSE",
        "BFO",
        "MCX",
        "NCDEX",
        "CDS",
        "BCD",
        "ICEX",
    ];

    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(
        ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        if (_restClient != null)
        {
            throw new InvalidOperationException(
                LocalizedStrings.NotDisconnectPrevTime);
        }
        if (Token.IsEmpty())
        {
            throw new InvalidOperationException(
                "Choice FinX API key or Session ID is required.");
        }
        if (PollingInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Choice FinX polling interval must be positive.");
        }
        if (PriceDivisor <= 0)
        {
            throw new InvalidOperationException(
                "Choice FinX price divisor must be positive.");
        }

        _restClient = new(
            Address,
            Token.UnSecure(),
            AuthorizationHeader,
            AuthorizationScheme,
            VendorId,
            VendorKey.IsEmpty()
                ? null
                : VendorKey.UnSecure(),
            ReConnectionSettings.ReAttemptCount + 1,
            PriceDivisor)
        {
            Parent = this,
        };

        try
        {
            _portfolioName = PortfolioName;
            var profile = await _restClient.GetProfile(
                cancellationToken);
            if (_portfolioName.IsEmpty())
            {
                _portfolioName = profile?.GetText(
                    "UserId", "UserID", "UCC",
                    "ClientCode", "ClientId");
            }
            _portfolioName =
                _portfolioName.IsEmpty("CHOICE_FINX");

            if (!WebSocketToken.IsEmpty() &&
                this.IsTransactional())
            {
                _socketClient = new(
                    WebSocketAddress,
                    WebSocketToken.UnSecure(),
                    ReConnectionSettings.ReAttemptCount,
                    ReConnectionSettings.WorkingTime)
                {
                    Parent = this,
                };
                _socketClient.OrderReceived +=
                    OnSocketOrderReceived;
                _socketClient.TradeReceived +=
                    OnSocketTradeReceived;
                _socketClient.MarketStatusReceived +=
                    OnMarketStatusReceived;
                _socketClient.Error += SendOutErrorAsync;
                _socketClient.StateChanged +=
                    SendOutConnectionStateAsync;
                await _socketClient.Connect(
                    cancellationToken);
            }

            _nextPolling =
                CurrentTime + PollingInterval;
            _nextHeartbeat =
                CurrentTime + TimeSpan.FromSeconds(20);
            await base.ConnectAsync(
                connectMsg, cancellationToken);
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
            {
                await _socketClient.Disconnect(
                    cancellationToken);
            }
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
            await _socketClient.SendHeartbeat(
                cancellationToken);
            _nextHeartbeat =
                CurrentTime + TimeSpan.FromSeconds(20);
        }

        if (_restClient != null &&
            CurrentTime >= _nextPolling)
        {
            _nextPolling = CurrentTime + PollingInterval;
            await PollMarketData(cancellationToken);
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
        _lastTicks.Clear();
        _orderTransactions.Clear();
        _transactionOrders.Clear();
        _orderFingerprints.Clear();
        _tradeIds.Clear();
        _orderStatusSubscriptionId = 0;
        _portfolioSubscriptionId = 0;
        _portfolioName = null;
        _nextPolling = default;
        _nextHeartbeat = default;
        await base.ResetAsync(resetMsg, cancellationToken);
    }

    private void DisposeClients()
    {
        if (_socketClient != null)
        {
            _socketClient.OrderReceived -=
                OnSocketOrderReceived;
            _socketClient.TradeReceived -=
                OnSocketTradeReceived;
            _socketClient.MarketStatusReceived -=
                OnMarketStatusReceived;
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
