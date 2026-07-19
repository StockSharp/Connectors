namespace StockSharp.Rain;

public partial class RainMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        if (_restClient is not null)
            throw new InvalidOperationException(
                LocalizedStrings.NotDisconnectPrevTime);

        ClearState();
        _restClient = new(RestEndpoint, Key, Secret, AccessToken)
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
                        "Rain returned no products.");
            _socketClient = CreateSocketClient();
            await SocketClient.ConnectAsync(cancellationToken);
            connectMsg.SessionId = RestClient.IsPrivateAvailable
                ? $"Rain {Key.ToId()}"
                : "Rain public";
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

    private RainSocketClient CreateSocketClient()
    {
        var client = new RainSocketClient(WebSocketEndpoint,
            ReConnectionSettings.WorkingTime,
            ReConnectionSettings.ReAttemptCount)
        {
            Parent = this,
        };
        client.BookReceived += OnSocketBookAsync;
        client.TradesReceived += OnSocketTradesAsync;
        client.CandleReceived += OnSocketCandleAsync;
        client.ProductSummaryReceived += OnSocketProductSummaryAsync;
        client.MarketSummaryReceived += OnSocketMarketSummaryAsync;
        client.AccountsReceived += OnSocketAccountsAsync;
        client.OrdersReceived += OnSocketOrdersAsync;
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
            foreach (var book in _books.Values)
            {
                book.IsSnapshotReady = false;
                book.IsRefreshPending = false;
                book.Sequence = 0;
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
        var socketClient = _socketClient;
        _socketClient = null;
        if (socketClient is not null)
        {
            try
            {
                await socketClient.DisconnectAsync(cancellationToken);
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                await SendOutErrorAsync(error, cancellationToken);
            }
            socketClient.Dispose();
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
