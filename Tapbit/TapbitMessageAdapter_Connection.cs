namespace StockSharp.Tapbit;

public partial class TapbitMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        if (_restClient is not null)
            throw new InvalidOperationException(
                LocalizedStrings.NotDisconnectPrevTime);

        ClearState();
        _restClient = new(SpotRestEndpoint, FuturesRestEndpoint, Key, Secret)
        {
            Parent = this,
        };
        await SendOutConnectionStateAsync(ConnectionStates.Connecting,
            cancellationToken);
        try
        {
            var errors = new List<Exception>();
            if (Sections.HasFlag(TapbitSections.Spot))
            {
                try
                {
                    RegisterProducts(await RestClient.GetSpotProductsAsync(
                        cancellationToken));
                }
                catch (Exception error)
                {
                    errors.Add(error);
                    this.AddWarningLog(
                        "Tapbit Spot discovery failed: {0}", error.Message);
                }
            }
            if (Sections.HasFlag(TapbitSections.Futures))
            {
                try
                {
                    RegisterProducts(await RestClient.GetFuturesProductsAsync(
                        cancellationToken));
                }
                catch (Exception error)
                {
                    errors.Add(error);
                    this.AddWarningLog(
                        "Tapbit futures discovery failed: {0}",
                        error.Message);
                }
            }
            using (_sync.EnterScope())
                if (_products.Count == 0)
                    throw errors.Count == 1
                        ? errors[0]
                        : new AggregateException(
                            "Tapbit returned no instruments for the enabled " +
                            "sections.", errors);

            _socketClient = new(WebSocketEndpoint,
                ReConnectionSettings.WorkingTime,
                ReConnectionSettings.ReAttemptCount)
            {
                Parent = this,
            };
            _socketClient.TickerReceived += OnSocketTickerAsync;
            _socketClient.BookReceived += OnSocketBookAsync;
            _socketClient.Error += OnSocketErrorAsync;
            _socketClient.StateChanged += OnSocketStateAsync;
            await _socketClient.ConnectAsync(cancellationToken);

            connectMsg.SessionId = RestClient.IsPrivateAvailable
                ? $"Tapbit {Key.ToId()} {Sections}"
                : $"Tapbit public {Sections}";
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
            if (_restClient is not null &&
                (_tickSubscriptions.Count > 0 ||
                    _candleSubscriptions.Count > 0) &&
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
            await RunSafelyAsync(PollMarketAsync, cancellationToken);
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
            _streamProducts.Clear();
            _level1Subscriptions.Clear();
            _depthSubscriptions.Clear();
            _tickSubscriptions.Clear();
            _candleSubscriptions.Clear();
            _books.Clear();
            _portfolioSubscriptions.Clear();
            _orderSubscriptions.Clear();
            _trackedOrders.Clear();
            _transactionSymbols.Clear();
            _seenPublicTrades.Clear();
            _publicTradeDeliveryOrder.Clear();
            _orderFingerprints.Clear();
            _balanceFingerprints.Clear();
            _candleFingerprints.Clear();
            _nextMarketPoll = default;
            _nextPrivatePoll = default;
        }
    }
}
