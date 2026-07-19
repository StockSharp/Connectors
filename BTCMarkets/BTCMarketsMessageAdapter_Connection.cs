namespace StockSharp.BTCMarkets;

public partial class BTCMarketsMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        _ = connectMsg;
        if (_restClient is not null)
            throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

        ClearState();
        _restClient = new(RestEndpoint, Key, Secret) { Parent = this };
        await SendOutConnectionStateAsync(ConnectionStates.Connecting,
            cancellationToken);
        try
        {
            await RestClient.SynchronizeClockAsync(cancellationToken);
            RegisterMarkets(await RestClient.GetMarketsAsync(cancellationToken));
            _socketClient = CreateSocketClient();
            await SocketClient.ConnectAsync(cancellationToken);
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
    protected override ValueTask TimeAsync(TimeMessage timeMsg,
        CancellationToken cancellationToken)
    {
        _ = timeMsg;
        _ = cancellationToken;
        return default;
    }

    private BTCMarketsSocketClient CreateSocketClient()
    {
        var client = new BTCMarketsSocketClient(WebSocketEndpoint, Key, Secret,
            RestClient.GetTimestamp, ReConnectionSettings.WorkingTime,
            ReConnectionSettings.ReAttemptCount)
        {
            Parent = this,
        };
        client.TickReceived += OnSocketTickAsync;
        client.TradeReceived += OnSocketTradeAsync;
        client.OrderBookReceived += OnSocketOrderBookAsync;
        client.OrderChanged += OnSocketOrderChangedAsync;
        client.FundChanged += OnSocketFundChangedAsync;
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

        long[] portfolios;
        (long Id, OrderSubscription Subscription)[] orders;
        using (_sync.EnterScope())
        {
            portfolios = [.. _portfolioSubscriptions];
            orders = [.. _orderSubscriptions.Select(static pair =>
                (pair.Key, pair.Value))];
        }
        foreach (var transactionId in portfolios)
            await SendPortfolioSnapshotAsync(transactionId, true,
                cancellationToken);
        foreach (var item in orders)
            await SendOrderSnapshotAsync(item.Id, item.Subscription, null, null,
                1000, true, cancellationToken);
        await SendOutConnectionStateAsync(state, cancellationToken);
    }

    private void MarkBooksUninitialized()
    {
        using (_sync.EnterScope())
            foreach (var book in _books.Values)
            {
                book.IsSnapshotReady = false;
                book.IsRefreshPending = false;
                book.Bids.Clear();
                book.Asks.Clear();
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
