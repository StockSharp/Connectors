namespace StockSharp.NDAX;

public partial class NDAXMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        if (_restClient is not null)
            throw new InvalidOperationException(
                LocalizedStrings.NotDisconnectPrevTime);

        ClearState();
        _restClient = new(RestEndpoint) { Parent = this };
        await SendOutConnectionStateAsync(ConnectionStates.Connecting,
            cancellationToken);
        try
        {
            var instruments = await RestClient.GetInstrumentsAsync(OmsId,
                cancellationToken);
            var products = await RestClient.GetProductsAsync(OmsId,
                cancellationToken);
            RegisterCatalog(instruments, products);
            using (_sync.EnterScope())
                if (_instrumentsById.Count == 0)
                    throw new InvalidDataException(
                        "NDAX returned no instruments.");

            _socketClient = CreateSocketClient();
            await SocketClient.ConnectAsync(cancellationToken);
            if (SocketClient.IsAuthenticated && EffectiveAccountId <= 0)
                throw new InvalidDataException(
                    "NDAX authentication returned no trading account.");
            if (SocketClient.IsAuthenticated)
                await SocketClient.SubscribeAccountEventsAsync(
                    EffectiveAccountId, cancellationToken);
            connectMsg.SessionId = SocketClient.IsAuthenticated
                ? $"NDAX {UserId}/{EffectiveAccountId}"
                : "NDAX public";
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
        var ping = false;
        using (_sync.EnterScope())
            if (_socketClient is not null &&
                CurrentTime - _lastPing >= TimeSpan.FromSeconds(15))
            {
                _lastPing = CurrentTime;
                ping = true;
            }
        if (ping)
            await RunSafelyAsync(SocketClient.PingAsync, cancellationToken);
        await base.TimeAsync(timeMsg, cancellationToken);
    }

    private NDAXSocketClient CreateSocketClient()
    {
        var client = new NDAXSocketClient(WebSocketEndpoint,
            ReConnectionSettings.WorkingTime,
            ReConnectionSettings.ReAttemptCount, OmsId, Key, Secret, UserId)
        {
            Parent = this,
        };
        client.Level1Received += OnSocketLevel1Async;
        client.Level2Received += OnSocketLevel2Async;
        client.PublicTradesReceived += OnSocketTradesAsync;
        client.CandlesReceived += OnSocketCandlesAsync;
        client.PositionReceived += OnSocketPositionAsync;
        client.OrderReceived += OnSocketOrderAsync;
        client.AccountTradeReceived += OnSocketAccountTradeAsync;
        client.OrderRejected += OnSocketOrderRejectedAsync;
        client.Error += OnSocketErrorAsync;
        client.StateChanged += OnSocketStateAsync;
        return client;
    }

    private ValueTask OnSocketErrorAsync(Exception error,
        CancellationToken cancellationToken)
        => SendOutErrorAsync(error, cancellationToken);

    private async ValueTask OnSocketStateAsync(ConnectionStates state,
        CancellationToken cancellationToken)
    {
        if (state == ConnectionStates.Reconnecting)
        {
            MarkBooksUninitialized();
            return;
        }
        if (state is ConnectionStates.Failed or ConnectionStates.Restored)
            await SendOutConnectionStateAsync(state, cancellationToken);
    }

    private void MarkBooksUninitialized()
    {
        using (_sync.EnterScope())
        {
            foreach (var book in _books.Values)
            {
                book.IsSnapshotReady = false;
                book.IsRefreshPending = false;
                book.Sequence = 0;
            }
        }
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
}
