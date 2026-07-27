namespace StockSharp.Ppi;

public partial class PpiMessageAdapter
{
    private PpiRestClient _rest;
    private PpiStreamingClient _stream;
    private string _accountNumber;
    private DateTime _lastOrderPoll;
    private DateTime _lastPortfolioPoll;

    /// <summary>
    /// Initializes a new instance of the <see cref="PpiMessageAdapter"/>.
    /// </summary>
    public PpiMessageAdapter(IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        HeartbeatInterval = TimeSpan.FromSeconds(1);
        ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

        this.AddMarketDataSupport();
        this.AddTransactionalSupport();
        this.RemoveSupportedMessage(MessageTypes.OrderReplace);
        this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
        this.AddSupportedMarketDataType(DataType.Ticks);
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedMarketDataType(DataType.MarketDepth);
        this.AddSupportedMarketDataType(TimeSpan.FromDays(1).TimeFrame());
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities ||
            dataType == DataType.Transactions ||
            dataType == DataType.PositionChanges ||
            base.IsAllDownloadingSupported(dataType);

    /// <inheritdoc />
    public override bool IsSupportTransactionLog => true;

    /// <summary>Supported candle time frames.</summary>
    public static IEnumerable<TimeSpan> AllTimeFrames =>
        PpiExtensions.TimeFrames;

    /// <inheritdoc />
    public override string[] AssociatedBoards { get; } =
        ["BYMA", "ROFEX", "NYSE", "NASDAQ", "OTC"];

    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(
        ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        if (_rest != null)
            throw new InvalidOperationException(
                LocalizedStrings.NotDisconnectPrevTime);
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

        var restAddress = IsDemo ? SandboxRestAddress : RestAddress;
        var realtimeAddress =
            IsDemo ? SandboxRealtimeAddress : RealtimeAddress;
        var clientKey = ClientKey.IsEmpty()
            ? IsDemo ? "ppPYTHONSb" : "pp19PythonApp12"
            : ClientKey.UnSecure();

        _rest = new(
            restAddress,
            Key,
            Secret,
            AuthorizedClient,
            clientKey,
            Token,
            RefreshToken)
        {
            Parent = this,
        };

        try
        {
            await _rest.GetAccessToken(cancellationToken);
            CaptureTokens();

            var accounts = await _rest.GetAccounts(cancellationToken);
            var account = Account.IsEmpty()
                ? accounts.FirstOrDefault()
                : accounts.FirstOrDefault(item =>
                    item.AccountNumber.EqualsIgnoreCase(Account) ||
                    item.ExternalId.EqualsIgnoreCase(Account));
            if (account?.AccountNumber.IsEmpty() != false)
            {
                throw new InvalidOperationException(
                    Account.IsEmpty()
                        ? "PPI returned no trading accounts."
                        : $"PPI account '{Account}' was not found.");
            }

            _accountNumber = account.AccountNumber;
            _stream = new(
                realtimeAddress,
                _rest,
                OnMarketUpdate,
                OnAccountUpdate,
                SendOutErrorAsync);

            if (this.IsTransactional())
            {
                await _stream.SubscribeAccount(
                    _accountNumber, cancellationToken);
            }

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
        if (_rest == null)
            throw new InvalidOperationException(
                LocalizedStrings.ConnectionNotOk);

        try
        {
            CaptureTokens();
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
            CurrentTime - _lastOrderPoll >= AccountPollingInterval)
        {
            await SendOrderSnapshot(
                _orderStatusSubscriptionId,
                false,
                null,
                null,
                null,
                cancellationToken);
            _lastOrderPoll = CurrentTime;
        }

        if (_portfolioSubscriptionId != 0 &&
            CurrentTime - _lastPortfolioPoll >= AccountPollingInterval)
        {
            await SendPortfolioSnapshot(
                _portfolioSubscriptionId, cancellationToken);
            _lastPortfolioPoll = CurrentTime;
        }

        CaptureTokens();
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
        _orderSides.Clear();
        _executedQuantities.Clear();
        _tradeIds.Clear();
        _orderStatusSubscriptionId = 0;
        _portfolioSubscriptionId = 0;
        _accountNumber = null;
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

    private async ValueTask DisposeClients(
        CancellationToken cancellationToken)
    {
        if (_stream != null)
        {
            try
            {
                await _stream.StopAsync(cancellationToken);
            }
            catch (Exception error) when (
                error is OperationCanceledException or IOException or
                    HttpRequestException)
            {
                this.AddVerboseLog(
                    "PPI realtime cleanup: {0}", error.Message);
            }
            _stream.Dispose();
            _stream = null;
        }

        if (_rest != null)
        {
            CaptureTokens();
            _rest.Dispose();
            _rest = null;
        }
    }
}
