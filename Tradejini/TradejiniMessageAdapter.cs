namespace StockSharp.Tradejini;

public partial class TradejiniMessageAdapter
{
    private TradejiniRestClient _restClient;
    private string _resolvedPortfolioName;
    private DateTime _lastPortfolioRefresh;
    private DateTime _lastOrderRefresh;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="TradejiniMessageAdapter"/> class.
    /// </summary>
    public TradejiniMessageAdapter(IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        HeartbeatInterval = TimeSpan.FromSeconds(5);

        this.AddMarketDataSupport();
        this.AddTransactionalSupport();
        this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
        this.AddSupportedCandleTimeFrames(
            TradejiniExtensions.TimeFrames.Keys);
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
    public override string[] AssociatedBoards { get; } =
        ["NSE", "BSE", "NFO", "BFO", "CDS", "MCX"];

    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(
        ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        if (_restClient != null)
            throw new InvalidOperationException(
                LocalizedStrings.NotDisconnectPrevTime);
        if (PollingInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PollingInterval),
                PollingInterval,
                "Polling interval must be positive.");
        }

        ApiKey.ThrowIfEmpty(nameof(ApiKey));
        if (Token.IsEmpty())
        {
            Password.ThrowIfEmpty(nameof(Password));
            TwoFactorCode.ThrowIfEmpty(nameof(TwoFactorCode));
        }

        _restClient = new(
            ApiKey,
            Token,
            Address)
        {
            Parent = this,
        };

        try
        {
            var login = await _restClient.Authenticate(
                Password,
                TwoFactorCode,
                TwoFactorType,
                cancellationToken);
            Token = login.AccessToken.Secure();

            var profile = await _restClient.GetProfile(cancellationToken);
            _resolvedPortfolioName = PortfolioName
                .IsEmpty(profile?.UserId)
                .IsEmpty("Tradejini");

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
        if (_restClient == null)
            throw new InvalidOperationException(
                LocalizedStrings.ConnectionNotOk);

        try
        {
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
        DisposeClient();
        _orderTransactions.Clear();
        _transactionOrders.Clear();
        _tradeIds.Clear();
        _orderStatusSubscriptionId = 0;
        _portfolioSubscriptionId = 0;
        _resolvedPortfolioName = null;
        _lastPortfolioRefresh = default;
        _lastOrderRefresh = default;

        await base.ResetAsync(resetMsg, cancellationToken);
    }

    private void DisposeClient()
    {
        _restClient?.Dispose();
        _restClient = null;
    }
}
