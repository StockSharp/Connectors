namespace StockSharp.Foxbit;

public partial class FoxbitMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        if (_restClient is not null)
            throw new InvalidOperationException(
                LocalizedStrings.NotDisconnectPrevTime);

        ClearState();
        _restClient = new(RestEndpoint, Key, Secret) { Parent = this };
        await SendOutConnectionStateAsync(ConnectionStates.Connecting,
            cancellationToken);
        try
        {
            await RestClient.SynchronizeClockAsync(cancellationToken);
            RegisterMarkets(await RestClient.GetMarketsAsync(cancellationToken));
            using (_sync.EnterScope())
                if (_markets.Count == 0)
                    throw new InvalidDataException(
                        "Foxbit returned no spot markets.");
            _socketClient = CreateSocketClient();
            await SocketClient.ConnectAsync(cancellationToken);
            connectMsg.SessionId = RestClient.IsCredentialsAvailable
                ? $"Foxbit {Key.ToId()}"
                : "Foxbit public";
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
        var now = CurrentTime;
        bool ping;
        bool refreshOrders;
        bool refreshPortfolio;
        using (_sync.EnterScope())
        {
            ping = _socketClient is not null &&
                now - _lastPing >= TimeSpan.FromSeconds(20);
            refreshOrders = _restClient is not null &&
                _orderSubscriptions.Count > 0 &&
                now - _lastOrderRefresh >= PollingInterval;
            refreshPortfolio = _restClient is not null &&
                _portfolioSubscriptions.Count > 0 &&
                now - _lastPortfolioRefresh >= PollingInterval;
            if (ping)
                _lastPing = now;
            if (refreshOrders)
                _lastOrderRefresh = now;
            if (refreshPortfolio)
                _lastPortfolioRefresh = now;
        }

        if (ping)
            await RunPollingAsync(SocketClient.PingAsync, cancellationToken);
        if (refreshOrders)
            await RunPollingAsync(RefreshOrderSubscriptionsAsync,
                cancellationToken);
        if (refreshPortfolio)
            await RunPollingAsync(RefreshPortfolioSubscriptionsAsync,
                cancellationToken);
        await base.TimeAsync(timeMsg, cancellationToken);
    }

    private FoxbitSocketClient CreateSocketClient()
    {
        var client = new FoxbitSocketClient(WebSocketEndpoint,
            ReConnectionSettings.WorkingTime,
            ReConnectionSettings.ReAttemptCount)
        {
            Parent = this,
        };
        client.TickerReceived += OnSocketTickerAsync;
        client.TradesReceived += OnSocketTradesAsync;
        client.BookSnapshotReceived += OnSocketBookSnapshotAsync;
        client.BookUpdateReceived += OnSocketBookUpdateAsync;
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
        if (state == ConnectionStates.Failed)
        {
            await SendOutConnectionStateAsync(state, cancellationToken);
            return;
        }
        if (state != ConnectionStates.Restored)
            return;
        await RunPollingAsync(RefreshOrderSubscriptionsAsync,
            cancellationToken);
        await RunPollingAsync(RefreshPortfolioSubscriptionsAsync,
            cancellationToken);
        await SendOutConnectionStateAsync(state, cancellationToken);
    }

    private void MarkBooksUninitialized()
    {
        using (_sync.EnterScope())
            foreach (var book in _books.Values)
            {
                book.IsSnapshotReady = false;
                book.IsRefreshPending = false;
                book.SequenceId = 0;
            }
    }

    private async ValueTask RunPollingAsync(
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
