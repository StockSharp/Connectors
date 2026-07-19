namespace StockSharp.BYDFi;

public partial class BYDFiMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        if (_restClient is not null)
            throw new InvalidOperationException(
                LocalizedStrings.NotDisconnectPrevTime);

        ClearState();
        _restClient = new(RestEndpoint, Key, Secret)
        {
            Parent = this,
        };
        await SendOutConnectionStateAsync(ConnectionStates.Connecting,
            cancellationToken);
        try
        {
            RegisterProducts(await RestClient.GetProductsAsync(
                cancellationToken));
            using (_sync.EnterScope())
                if (_products.Count == 0)
                    throw new InvalidDataException(
                        "BYDFi returned no active futures instruments.");

            _socketClient = new(WebSocketEndpoint,
                ReConnectionSettings.WorkingTime,
                ReConnectionSettings.ReAttemptCount)
            {
                Parent = this,
            };
            _socketClient.TickerReceived += OnSocketTickerAsync;
            _socketClient.RealTickerReceived += OnSocketRealTickerAsync;
            _socketClient.DepthReceived += OnSocketDepthAsync;
            _socketClient.KlineReceived += OnSocketKlineAsync;
            _socketClient.Error += OnSocketErrorAsync;
            _socketClient.StateChanged += OnSocketStateAsync;

            connectMsg.SessionId = RestClient.IsPrivateAvailable
                ? $"BYDFi {Key.ToId()} {Wallet}"
                : "BYDFi public";
            await SendOutConnectionStateAsync(ConnectionStates.Connected,
                cancellationToken);
        }
        catch
        {
            await DisposeClientsAsync(cancellationToken);
            await SendOutConnectionStateAsync(ConnectionStates.Disconnected,
                cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    protected override async ValueTask DisconnectAsync(
        DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
    {
        _ = disconnectMsg;
        EnsureConnected();
        await SendOutConnectionStateAsync(ConnectionStates.Disconnecting,
            cancellationToken);
        await DisposeClientsAsync(cancellationToken);
        await SendOutConnectionStateAsync(ConnectionStates.Disconnected,
            cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ResetAsync(ResetMessage resetMsg,
        CancellationToken cancellationToken)
    {
        await DisposeClientsAsync(cancellationToken);
        await base.ResetAsync(resetMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask TimeAsync(TimeMessage timeMsg,
        CancellationToken cancellationToken)
    {
        var pollMarket = false;
        var pollPrivate = false;
        using (_sync.EnterScope())
        {
            if (_restClient is not null && _tickSubscriptions.Count > 0 &&
                CurrentTime >= _nextMarketPoll)
            {
                _nextMarketPoll = CurrentTime + PollingInterval;
                pollMarket = true;
            }
            if (_restClient?.IsPrivateAvailable == true &&
                (_portfolioSubscriptions.Count > 0 ||
                    _orderSubscriptions.Count > 0 ||
                    _trackedOrders.Values.Any(static order =>
                        order.State == OrderStates.Active)) &&
                CurrentTime >= _nextPrivatePoll)
            {
                _nextPrivatePoll = CurrentTime + PollingInterval;
                pollPrivate = true;
            }
        }
        if (pollMarket)
            await RunSafelyAsync(PollPublicTradesAsync, cancellationToken);
        if (pollPrivate)
            await RunSafelyAsync(PollPrivateAsync, cancellationToken);
        await base.TimeAsync(timeMsg, cancellationToken);
    }

    private ValueTask OnSocketErrorAsync(Exception error,
        CancellationToken cancellationToken)
        => SendOutErrorAsync(error, cancellationToken);

    private async ValueTask OnSocketStateAsync(ConnectionStates state,
        CancellationToken cancellationToken)
    {
        if (state is ConnectionStates.Failed or ConnectionStates.Restored)
            await SendOutConnectionStateAsync(state, cancellationToken);
    }

    private async ValueTask RunSafelyAsync(
        Func<CancellationToken, ValueTask> action,
        CancellationToken cancellationToken)
    {
        try
        {
            await action(cancellationToken);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            await SendOutErrorAsync(error, cancellationToken);
        }
    }

    private async ValueTask DisposeClientsAsync(
        CancellationToken cancellationToken)
    {
        var socket = _socketClient;
        _socketClient = null;
        if (socket is not null)
        {
            try
            {
                await socket.DisconnectAsync(cancellationToken);
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                await SendOutErrorAsync(error, cancellationToken);
            }
            socket.Dispose();
        }
        _restClient?.Dispose();
        _restClient = null;
        ClearState();
    }

    private void DisposeClients()
    {
        _socketClient?.Dispose();
        _socketClient = null;
        _restClient?.Dispose();
        _restClient = null;
        ClearState();
    }

    private void ClearState()
    {
        using (_sync.EnterScope())
        {
            _products.Clear();
            _level1Subscriptions.Clear();
            _depthSubscriptions.Clear();
            _tickSubscriptions.Clear();
            _candleSubscriptions.Clear();
            _portfolioSubscriptions.Clear();
            _orderSubscriptions.Clear();
            _trackedOrders.Clear();
            _transactionSymbols.Clear();
            _seenAccountTrades.Clear();
            _seenPublicTrades.Clear();
            _orderFingerprints.Clear();
            _balanceFingerprints.Clear();
            _positionFingerprints.Clear();
            _nextMarketPoll = default;
            _nextPrivatePoll = default;
        }
    }
}
