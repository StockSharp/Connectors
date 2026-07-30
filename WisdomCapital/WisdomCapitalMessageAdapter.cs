namespace StockSharp.WisdomCapital;

public partial class WisdomCapitalMessageAdapter
{
    private static readonly TimeSpan[] _timeFrames =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(3),
        TimeSpan.FromMinutes(4),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(7),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(3),
        TimeSpan.FromHours(4),
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(7),
        TimeSpan.FromDays(30),
    ];

    private WisdomCapitalRestClient _restClient;
    private WisdomCapitalSocketClient _marketSocket;
    private WisdomCapitalSocketClient _interactiveSocket;
    private string _resolvedPortfolioName;
    private DateTime _lastOrderRefresh;
    private DateTime _lastPortfolioRefresh;
    private bool _interactiveSocketOwnsConnectionState;

    /// <summary>Supported historical candle time frames.</summary>
    public static IEnumerable<TimeSpan> AllTimeFrames => _timeFrames;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="WisdomCapitalMessageAdapter"/>.
    /// </summary>
    public WisdomCapitalMessageAdapter(
        IdGenerator transactionIdGenerator)
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
    public override string[] AssociatedBoards { get; } =
        ["NSE", "NFO", "CDS", "BSE", "BFO", "MCX"];

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
        if (EngineIoVersion is < 3 or > 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(EngineIoVersion),
                EngineIoVersion,
                "Engine.IO version must be 3 or 4.");
        }
        Source.ThrowIfEmpty(nameof(Source));

        _restClient = new(RestAddress) { Parent = this };
        try
        {
            if (this.IsTransactional())
            {
                if (Token.IsEmpty())
                {
                    var auth = await _restClient.LoginInteractive(
                        Key,
                        Secret,
                        Source,
                        cancellationToken);
                    Token = auth.Token.Secure();
                    UserId = auth.UserId;
                }
                else
                {
                    UserId.ThrowIfEmpty(nameof(UserId));
                    _restClient.SetInteractiveToken(Token);
                }
                await _restClient.GetProfile(cancellationToken);
                _resolvedPortfolioName = PortfolioName
                    .IsEmpty(UserId)
                    .IsEmpty("WisdomCapital");
            }

            if (this.IsMarketData())
            {
                if (MarketDataToken.IsEmpty())
                {
                    var auth = await _restClient.LoginMarketData(
                        MarketDataKey,
                        MarketDataSecret,
                        Source,
                        cancellationToken);
                    MarketDataToken = auth.Token.Secure();
                    MarketDataUserId = auth.UserId;
                }
                else
                {
                    MarketDataUserId.ThrowIfEmpty(
                        nameof(MarketDataUserId));
                    _restClient.SetMarketDataToken(MarketDataToken);
                }
                _marketSocket = new(
                    RestAddress,
                    _restClient.MarketDataToken,
                    MarketDataUserId,
                    true,
                    EngineIoVersion,
                    ReconnectAttempts,
                    ReConnectionSettings.WorkingTime)
                {
                    Parent = this,
                };
                _marketSocket.MarketDataReceived +=
                    OnMarketDataReceived;
                _marketSocket.Ready += OnMarketSocketReady;
                _marketSocket.Error += SendOutErrorAsync;
                _marketSocket.StateChanged +=
                    SendOutConnectionStateAsync;
                await _marketSocket.Connect(cancellationToken);
            }

            if (this.IsTransactional())
            {
                _interactiveSocket = new(
                    RestAddress,
                    _restClient.InteractiveToken,
                    UserId,
                    false,
                    EngineIoVersion,
                    ReconnectAttempts,
                    ReConnectionSettings.WorkingTime)
                {
                    Parent = this,
                };
                _interactiveSocket.InteractiveEventReceived +=
                    OnInteractiveEventReceived;
                _interactiveSocket.Error += SendOutErrorAsync;
                _interactiveSocketOwnsConnectionState =
                    _marketSocket == null;
                if (_interactiveSocketOwnsConnectionState)
                {
                    _interactiveSocket.StateChanged +=
                        SendOutConnectionStateAsync;
                }
                await _interactiveSocket.Connect(cancellationToken);
            }

            _lastOrderRefresh = CurrentTime;
            _lastPortfolioRefresh = CurrentTime;
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
        if (_restClient == null)
        {
            throw new InvalidOperationException(
                LocalizedStrings.ConnectionNotOk);
        }
        try
        {
            if (_marketSocket != null)
                await _marketSocket.Disconnect(cancellationToken);
            if (_interactiveSocket != null)
                await _interactiveSocket.Disconnect(cancellationToken);
            await base.DisconnectAsync(disconnectMsg, cancellationToken);
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
        await DisposeClients(cancellationToken);
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
        _lastOrderRefresh = default;
        _lastPortfolioRefresh = default;
        await base.ResetAsync(resetMsg, cancellationToken);
    }

    private async ValueTask OnInteractiveEventReceived(
        string eventName,
        JToken payload,
        CancellationToken cancellationToken)
    {
        if (eventName.EqualsIgnoreCase("order"))
        {
            var order = payload.ToObject<WisdomOrder>();
            if (order != null)
            {
                await ProcessOrder(
                    order,
                    _orderStatusSubscriptionId,
                    false,
                    cancellationToken);
            }
            return;
        }
        if (eventName.EqualsIgnoreCase("trade"))
        {
            var trade = payload.ToObject<WisdomTrade>();
            if (trade != null)
            {
                await ProcessTrade(
                    trade,
                    _orderStatusSubscriptionId,
                    false,
                    cancellationToken);
            }
            return;
        }
        if (eventName.EqualsIgnoreCase("position") ||
            eventName.EqualsIgnoreCase("tradeConversion"))
            _lastPortfolioRefresh = default;
        if (eventName.EqualsIgnoreCase("logout"))
        {
            await SendOutErrorAsync(
                new InvalidOperationException(
                    "Wisdom Capital terminated the interactive session."),
                cancellationToken);
        }
    }

    private async ValueTask OnMarketSocketReady(
        CancellationToken cancellationToken)
    {
        foreach (var pair in _marketSubscriptions.ToArray())
        {
            if (!_instruments.TryGetValue(
                pair.Key,
                out var instrument))
                continue;

            foreach (var dataType in pair.Value.Keys.ToArray())
            {
                var messageCode = dataType == DataType.Ticks
                    ? 1512
                    : dataType == DataType.MarketDepth
                        ? 1502
                        : 1501;
                await _restClient.Subscribe(
                    instrument.ToReference(),
                    messageCode,
                    cancellationToken);
            }
        }
    }

    private async ValueTask DisposeClients(
        CancellationToken cancellationToken)
    {
        if (_marketSocket != null)
        {
            _marketSocket.MarketDataReceived -= OnMarketDataReceived;
            _marketSocket.Ready -= OnMarketSocketReady;
            _marketSocket.Error -= SendOutErrorAsync;
            _marketSocket.StateChanged -= SendOutConnectionStateAsync;
            _marketSocket.Dispose();
            _marketSocket = null;
        }
        if (_interactiveSocket != null)
        {
            _interactiveSocket.InteractiveEventReceived -=
                OnInteractiveEventReceived;
            _interactiveSocket.Error -= SendOutErrorAsync;
            if (_interactiveSocketOwnsConnectionState)
            {
                _interactiveSocket.StateChanged -=
                    SendOutConnectionStateAsync;
            }
            _interactiveSocket.Dispose();
            _interactiveSocket = null;
        }
        _interactiveSocketOwnsConnectionState = false;
        _restClient?.Dispose();
        _restClient = null;
        await ValueTask.CompletedTask;
    }
}
