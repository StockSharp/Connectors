namespace StockSharp.GmoCoin;

public partial class GmoCoinMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        _ = connectMsg;
        if (_restClient is not null || _publicSocketClient is not null ||
            _privateSocketClient is not null)
            throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
        if (Key.IsEmpty() != Secret.IsEmpty())
            throw new InvalidOperationException(
                "GMO Coin API key and secret must be configured together.");

        ClearState();
        await SendOutConnectionStateAsync(ConnectionStates.Connecting,
            cancellationToken);
        try
        {
            _restClient = new(RestEndpoint, Key, Secret)
            {
                Parent = this,
            };
            var status = await RestClient.GetStatusAsync(cancellationToken);
            _serviceStatus = status?.Status ?? GmoCoinServiceStatuses.Maintenance;
            var symbols = await RestClient.GetSymbolsAsync(cancellationToken);
            if (symbols is not { Length: > 0 })
                throw new InvalidDataException(
                    "GMO Coin returned no trade rules.");
            RegisterMarkets(symbols);

            _publicSocketClient = new(PublicWebSocketEndpoint, RestClient, null,
                false, ReConnectionSettings.WorkingTime,
                ReConnectionSettings.ReAttemptCount)
            {
                Parent = this,
            };
            _publicSocketClient.TickerReceived += OnSocketTickerAsync;
            _publicSocketClient.OrderBookReceived += OnSocketOrderBookAsync;
            _publicSocketClient.TradeReceived += OnSocketTradeAsync;
            _publicSocketClient.Error += OnSocketErrorAsync;
            _publicSocketClient.StateChanged += OnPublicSocketStateAsync;
            await _publicSocketClient.ConnectAsync(cancellationToken);

            if (RestClient.IsCredentialsAvailable)
            {
                _webSocketToken = await RestClient.CreateWebSocketTokenAsync(
                    cancellationToken);
                if (_webSocketToken.IsEmpty())
                    throw new InvalidDataException(
                        "GMO Coin returned an empty private WebSocket token.");
                _privateSocketClient = new(
                    $"{PrivateWebSocketEndpoint.TrimEnd('/')}/{_webSocketToken}",
                    RestClient, _webSocketToken, true,
                    ReConnectionSettings.WorkingTime,
                    ReConnectionSettings.ReAttemptCount)
                {
                    Parent = this,
                };
                _privateSocketClient.ExecutionReceived += OnExecutionEventAsync;
                _privateSocketClient.OrderReceived += OnOrderEventAsync;
                _privateSocketClient.PositionReceived += OnPositionEventAsync;
                _privateSocketClient.PositionSummaryReceived +=
                    OnPositionSummaryEventAsync;
                _privateSocketClient.Error += OnSocketErrorAsync;
                _privateSocketClient.StateChanged += OnPrivateSocketStateAsync;
                await _privateSocketClient.ConnectAsync(cancellationToken);
            }

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

    private ValueTask OnSocketErrorAsync(Exception error,
        CancellationToken cancellationToken)
        => SendOutErrorAsync(error, cancellationToken);

    private async ValueTask OnPublicSocketStateAsync(ConnectionStates state,
        CancellationToken cancellationToken)
    {
        if (state == ConnectionStates.Failed)
        {
            await SendOutConnectionStateAsync(ConnectionStates.Failed,
                cancellationToken);
            return;
        }
        if (state == ConnectionStates.Restored)
            await SendOutConnectionStateAsync(ConnectionStates.Restored,
                cancellationToken);
    }

    private async ValueTask OnPrivateSocketStateAsync(ConnectionStates state,
        CancellationToken cancellationToken)
    {
        if (state == ConnectionStates.Failed)
        {
            await SendOutConnectionStateAsync(ConnectionStates.Failed,
                cancellationToken);
            return;
        }
        if (state == ConnectionStates.Restored)
            await RefreshPrivateSnapshotsAsync(cancellationToken);
    }

    private async ValueTask RefreshPrivateSnapshotsAsync(
        CancellationToken cancellationToken)
    {
        if (_restClient?.IsCredentialsAvailable != true)
            return;
        long[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _portfolioSubscriptions];
        foreach (var subscriptionId in subscriptions)
            await SendPortfolioSnapshotAsync(subscriptionId, cancellationToken);
    }

    private async ValueTask DisposeClientsAsync(
        CancellationToken cancellationToken)
    {
        var privateSocket = _privateSocketClient;
        _privateSocketClient = null;
        var publicSocket = _publicSocketClient;
        _publicSocketClient = null;
        var restClient = _restClient;
        var token = _webSocketToken;
        _webSocketToken = null;

        await DisposeSocketAsync(privateSocket, cancellationToken);
        await DisposeSocketAsync(publicSocket, cancellationToken);

        if (restClient is not null && !token.IsEmpty())
        {
            try
            {
                await restClient.DeleteWebSocketTokenAsync(token,
                    cancellationToken);
            }
            catch (Exception error)
            {
                if (!cancellationToken.IsCancellationRequested)
                    await SendOutErrorAsync(error, cancellationToken);
            }
        }

        _restClient = null;
        restClient?.Dispose();
        ClearState();
    }

    private async ValueTask DisposeSocketAsync(GmoCoinSocketClient socket,
        CancellationToken cancellationToken)
    {
        if (socket is null)
            return;
        try
        {
            await socket.DisconnectAsync(cancellationToken);
        }
        catch (Exception error)
        {
            if (!cancellationToken.IsCancellationRequested)
                await SendOutErrorAsync(error, cancellationToken);
        }
        finally
        {
            socket.Dispose();
        }
    }

    private void DisposeClients()
    {
        _privateSocketClient?.Dispose();
        _privateSocketClient = null;
        _publicSocketClient?.Dispose();
        _publicSocketClient = null;
        _restClient?.Dispose();
        _restClient = null;
        _webSocketToken = null;
        ClearState();
    }
}
