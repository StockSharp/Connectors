namespace StockSharp.Zoomex;

public partial class ZoomexMessageAdapter
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
            foreach (var category in Sections.Select(
                static section => section.ToNative()))
                await LoadProductsAsync(category, cancellationToken);
            using (_sync.EnterScope())
                if (_products.Count == 0)
                    throw new InvalidDataException(
                        "Zoomex returned no active instruments.");

            foreach (var category in Sections.Select(
                static section => section.ToNative()))
            {
                var socket = CreatePublicSocket(category);
                using (_sync.EnterScope())
                    _publicSockets.Add(category, socket);
                await socket.ConnectAsync(cancellationToken);
            }

            if (RestClient.IsPrivateAvailable)
            {
                _privateSocket = CreatePrivateSocket();
                await _privateSocket.ConnectAsync(cancellationToken);
                foreach (var topic in new[]
                    { "order", "execution", "position", "wallet" })
                    await _privateSocket.SubscribeAsync(topic,
                        cancellationToken);
            }

            _nextPing = CurrentTime + TimeSpan.FromSeconds(20);
            connectMsg.SessionId = RestClient.IsPrivateAvailable
                ? $"Zoomex {Key.ToId()}"
                : "Zoomex public";
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
        ZoomexSocketClient[] sockets = [];
        var shouldPing = false;
        using (_sync.EnterScope())
        {
            if (_restClient is not null && CurrentTime >= _nextPing)
            {
                _nextPing = CurrentTime + TimeSpan.FromSeconds(20);
                sockets = [.. _publicSockets.Values];
                if (_privateSocket is not null)
                    sockets = [.. sockets, _privateSocket];
                shouldPing = true;
            }
        }
        if (shouldPing)
            foreach (var socket in sockets)
                await RunSafelyAsync(socket.PingAsync, cancellationToken);
        await base.TimeAsync(timeMsg, cancellationToken);
    }

    private async ValueTask LoadProductsAsync(ZoomexCategories category,
        CancellationToken cancellationToken)
    {
        var cursor = default(string);
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        for (var pageNumber = 0; pageNumber < 100; pageNumber++)
        {
            var page = await RestClient.GetProductsAsync(category, cursor,
                1000, cancellationToken);
            foreach (var product in page?.Items ?? [])
            {
                if (product?.Symbol.IsEmpty() != false ||
                    product.Status != ZoomexProductStatuses.Trading)
                    continue;
                product.Category = category;
                using (_sync.EnterScope())
                    _products[new(category,
                        product.Symbol.NormalizeSymbol())] = product;
            }
            var next = page?.NextPageCursor;
            if (next.IsEmpty() || !seenCursors.Add(next))
                break;
            cursor = next;
        }
    }

    private ZoomexSocketClient CreatePublicSocket(ZoomexCategories category)
    {
        var socket = new ZoomexSocketClient(
            $"{WebSocketEndpoint}/v5/public/{category.ToNative()}", category,
            default, default, ReConnectionSettings.WorkingTime,
            ReConnectionSettings.ReAttemptCount)
        {
            Parent = this,
        };
        socket.TickerReceived += OnSocketTickerAsync;
        socket.BookReceived += OnSocketBookAsync;
        socket.PublicTradesReceived += OnSocketTradesAsync;
        socket.CandlesReceived += OnSocketCandlesAsync;
        socket.Error += OnSocketErrorAsync;
        socket.StateChanged += OnSocketStateAsync;
        return socket;
    }

    private ZoomexSocketClient CreatePrivateSocket()
    {
        var socket = new ZoomexSocketClient(
            $"{WebSocketEndpoint}/v3/private", null, Key, Secret,
            ReConnectionSettings.WorkingTime,
            ReConnectionSettings.ReAttemptCount)
        {
            Parent = this,
        };
        socket.OrdersReceived += OnSocketOrdersAsync;
        socket.ExecutionsReceived += OnSocketExecutionsAsync;
        socket.PositionsReceived += OnSocketPositionsAsync;
        socket.WalletsReceived += OnSocketWalletsAsync;
        socket.Error += OnSocketErrorAsync;
        socket.StateChanged += OnSocketStateAsync;
        return socket;
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
        ZoomexSocketClient[] sockets;
        using (_sync.EnterScope())
        {
            sockets = [.. _publicSockets.Values];
            _publicSockets.Clear();
        }
        if (_privateSocket is { } privateSocket)
        {
            _privateSocket = null;
            sockets = [.. sockets, privateSocket];
        }
        foreach (var socket in sockets)
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
        foreach (var socket in _publicSockets.Values)
            socket.Dispose();
        _publicSockets.Clear();
        _privateSocket?.Dispose();
        _privateSocket = null;
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
            _tickSubscriptions.Clear();
            _depthSubscriptions.Clear();
            _candleSubscriptions.Clear();
            _books.Clear();
            _portfolioSubscriptions.Clear();
            _orderSubscriptions.Clear();
            _trackedOrders.Clear();
            _seenPublicTrades.Clear();
            _publicTradeDeliveryOrder.Clear();
            _seenExecutions.Clear();
            _executionDeliveryOrder.Clear();
            _orderFingerprints.Clear();
            _balanceFingerprints.Clear();
            _positionFingerprints.Clear();
            _nextPing = default;
        }
    }
}
