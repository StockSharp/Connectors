namespace StockSharp.InvertirOnline;

public partial class InvertirOnlineMessageAdapter
{
    private InvertirOnlineRestClient _rest;
    private string _portfolioName;
    private DateTime _lastMarketPoll;
    private DateTime _lastOrderPoll;
    private DateTime _lastPortfolioPoll;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="InvertirOnlineMessageAdapter"/> class.
    /// </summary>
    public InvertirOnlineMessageAdapter(IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        HeartbeatInterval = TimeSpan.FromSeconds(1);
        ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

        this.AddMarketDataSupport();
        this.AddTransactionalSupport();
        this.RemoveSupportedMessage(MessageTypes.OrderReplace);
        this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedMarketDataType(DataType.MarketDepth);
        this.AddSupportedMarketDataType(TimeSpan.FromDays(1).TimeFrame());
    }

    /// <summary>Supported candle time frames.</summary>
    public static IEnumerable<TimeSpan> AllTimeFrames =>
        InvertirOnlineExtensions.TimeFrames;

    /// <inheritdoc />
    public override string[] AssociatedBoards { get; } =
        ["BCBA", "NYSE", "NASDAQ", "AMEX", "ROFX"];

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities ||
            dataType == DataType.Transactions ||
            dataType == DataType.PositionChanges ||
            base.IsAllDownloadingSupported(dataType);

    /// <inheritdoc />
    public override bool IsSupportCandlesUpdates(
        MarketDataMessage subscription)
        => true;

    /// <inheritdoc />
    public override bool IsSupportTransactionLog => true;

    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(
        ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        if (_rest != null)
        {
            throw new InvalidOperationException(
                LocalizedStrings.NotDisconnectPrevTime);
        }
        if (MarketDataPollingInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MarketDataPollingInterval),
                MarketDataPollingInterval,
                "Market data polling interval must be positive.");
        }
        if (AccountPollingInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AccountPollingInterval),
                AccountPollingInterval,
                "Account polling interval must be positive.");
        }
        if (LookupLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(LookupLimit),
                LookupLimit,
                "Lookup limit must be positive.");
        }

        _rest = new(
            IsDemo ? SandboxRestAddress : RestAddress,
            Login,
            Password,
            Token,
            RefreshToken,
            Math.Max(0, ReConnectionSettings.ReAttemptCount))
        {
            Parent = this,
        };

        try
        {
            await _rest.GetAccessToken(cancellationToken);
            var account = await _rest.GetAccountState(cancellationToken);
            _portfolioName = PortfolioName.IsEmpty(
                account?.Accounts?
                    .FirstOrDefault(
                        item => item?.Number.IsEmpty() == false)
                    ?.Number
                    .IsEmpty(Login)
                    .IsEmpty("IOL"));
            CaptureTokens();
            _lastMarketPoll =
                _lastOrderPoll =
                _lastPortfolioPoll =
                CurrentTime;
            await base.ConnectAsync(connectMsg, cancellationToken);
        }
        catch
        {
            DisposeClient();
            throw;
        }
    }

    /// <inheritdoc />
    protected override async ValueTask DisconnectAsync(
        DisconnectMessage disconnectMsg,
        CancellationToken cancellationToken)
    {
        if (_rest == null)
        {
            throw new InvalidOperationException(
                LocalizedStrings.ConnectionNotOk);
        }

        try
        {
            CaptureTokens();
            await base.DisconnectAsync(disconnectMsg, cancellationToken);
        }
        finally
        {
            DisposeClient();
        }
    }

    /// <inheritdoc />
    protected override async ValueTask TimeAsync(
        TimeMessage timeMsg,
        CancellationToken cancellationToken)
    {
        if (_marketSubscriptions.Count > 0 &&
            CurrentTime - _lastMarketPoll >= MarketDataPollingInterval)
        {
            _lastMarketPoll = CurrentTime;
            try
            {
                await PollMarketData(cancellationToken);
            }
            catch (Exception error) when (
                error is not OperationCanceledException ||
                    !cancellationToken.IsCancellationRequested)
            {
                await SendOutErrorAsync(error, cancellationToken);
            }
        }

        if ((_orderStatusSubscriptionId != 0 ||
            _trackedOrders.Count > 0) &&
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

        CaptureTokens();
        await base.TimeAsync(timeMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ResetAsync(
        ResetMessage resetMsg,
        CancellationToken cancellationToken)
    {
        DisposeClient();
        _marketSubscriptions.Clear();
        _instruments.Clear();
        _securityIds.Clear();
        _orderTransactions.Clear();
        _transactionOrders.Clear();
        _orderSides.Clear();
        _orderSecurities.Clear();
        _executedQuantities.Clear();
        _tradeIds.Clear();
        _trackedOrders.Clear();
        _orderStatusSubscriptionId = 0;
        _portfolioSubscriptionId = 0;
        _portfolioName = null;
        _lastMarketPoll = default;
        _lastOrderPoll = default;
        _lastPortfolioPoll = default;
        await base.ResetAsync(resetMsg, cancellationToken);
    }

    private void CaptureTokens()
    {
        if (_rest is null)
            return;
        if (!_rest.AccessToken.IsEmpty())
            Token = _rest.AccessToken.Secure();
        if (!_rest.RefreshToken.IsEmpty())
            RefreshToken = _rest.RefreshToken.Secure();
    }

    private void DisposeClient()
    {
        if (_rest is null)
            return;
        CaptureTokens();
        _rest.Dispose();
        _rest = null;
    }
}
