namespace StockSharp.PaytmMoney;

public partial class PaytmMoneyMessageAdapter
{
    private PaytmMoneyRestClient _restClient;
    private PaytmMoneyWebSocketClient _marketClient;
    private string _portfolioName;
    private DateTime _nextPollingTime;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="PaytmMoneyMessageAdapter"/> class.
    /// </summary>
    public PaytmMoneyMessageAdapter(
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
        this.AddSupportedMarketDataType(DataType.MarketDepth);
        this.AddSupportedCandleTimeFrames(AllTimeFrames);
    }

    /// <summary>
    /// Candle time frames supported by Paytm Money.
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
    [
        "NSE_EQ",
        "NSE_FNO",
        "NSE_IDX",
        "BSE_EQ",
        "BSE_FNO",
        "BSE_IDX",
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
        if (PollingInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Paytm Money polling interval must be positive.");
        }

        _restClient = new(
            Address,
            SecurityMasterFile,
            Token.IsEmpty() ? null : Token.UnSecure(),
            ReadAccessToken.IsEmpty()
                ? null
                : ReadAccessToken.UnSecure(),
            ReConnectionSettings.ReAttemptCount + 1)
        {
            Parent = this,
        };

        try
        {
            if (!RequestToken.IsEmpty())
            {
                if (Key.IsEmpty() || Secret.IsEmpty())
                {
                    throw new InvalidOperationException(
                        "API key and secret are required to exchange a Paytm Money request token.");
                }

                var tokens = await _restClient.GenerateSession(
                    Key.UnSecure(),
                    Secret.UnSecure(),
                    RequestToken.UnSecure(),
                    cancellationToken);
                if (!tokens.AccessToken.IsEmpty())
                    Token = tokens.AccessToken.Secure();
                if (!tokens.ReadAccessToken.IsEmpty())
                {
                    ReadAccessToken =
                        tokens.ReadAccessToken.Secure();
                }
                if (!tokens.PublicAccessToken.IsEmpty())
                {
                    PublicAccessToken =
                        tokens.PublicAccessToken.Secure();
                }
            }

            _portfolioName = PortfolioName;
            if (this.IsTransactional())
            {
                if (Token.IsEmpty())
                {
                    throw new InvalidOperationException(
                        "Paytm Money trading access token is not specified.");
                }
                var user = await _restClient.GetUser(
                    cancellationToken);
                if (_portfolioName.IsEmpty() && user != null)
                {
                    _portfolioName = user.UserId.ToString(
                        CultureInfo.InvariantCulture);
                }
            }
            _portfolioName =
                _portfolioName.IsEmpty("PAYTM_MONEY");

            if (this.IsMarketData() &&
                !PublicAccessToken.IsEmpty())
            {
                _marketClient = new(
                    WebSocketAddress,
                    PublicAccessToken.UnSecure(),
                    ReConnectionSettings.ReAttemptCount,
                    ReConnectionSettings.WorkingTime)
                {
                    Parent = this,
                };
                _marketClient.TickReceived += OnTickReceived;
                _marketClient.StateChanged +=
                    SendOutConnectionStateAsync;
                _marketClient.Error += SendOutErrorAsync;
                await _marketClient.Connect(cancellationToken);
            }

            _nextPollingTime = CurrentTime + PollingInterval;
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
            if (_marketClient != null)
                await _marketClient.Disconnect(cancellationToken);
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
        if (_restClient != null &&
            CurrentTime >= _nextPollingTime)
        {
            _nextPollingTime = CurrentTime + PollingInterval;
            if (_orderStatusSubscriptionId != 0)
            {
                await SendOrderSnapshot(
                    _orderStatusSubscriptionId,
                    false,
                    cancellationToken);
            }
            if (_portfolioSubscriptionId != 0)
                await SendPortfolioSnapshot(cancellationToken);
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
        _orderFills.Clear();
        _orderCache.Clear();
        _orderFingerprints.Clear();
        _tradeIds.Clear();
        _orderStatusSubscriptionId = 0;
        _portfolioSubscriptionId = 0;
        _portfolioName = null;
        _nextPollingTime = default;
        await base.ResetAsync(resetMsg, cancellationToken);
    }

    private PaytmMoneyWebSocketClient MarketClient
        => _marketClient ??
            throw new InvalidOperationException(
                PublicAccessToken.IsEmpty()
                    ? "Paytm Money public access token is required for live market data."
                    : LocalizedStrings.ConnectionNotOk);

    private void DisposeClients()
    {
        if (_marketClient != null)
        {
            _marketClient.TickReceived -= OnTickReceived;
            _marketClient.StateChanged -=
                SendOutConnectionStateAsync;
            _marketClient.Error -= SendOutErrorAsync;
            _marketClient.Dispose();
            _marketClient = null;
        }

        _restClient?.Dispose();
        _restClient = null;
    }
}
