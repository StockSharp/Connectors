namespace StockSharp.Indodax;

public partial class IndodaxMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        _ = connectMsg;
        if (_restClient is not null || _socketClient is not null)
            throw new InvalidOperationException(
                LocalizedStrings.NotDisconnectPrevTime);
        if (Key.IsEmpty() != Secret.IsEmpty())
            throw new InvalidOperationException(
                "Indodax TAPI key and secret must be configured together.");

        ClearState();
        await SendOutConnectionStateAsync(ConnectionStates.Connecting,
            cancellationToken);
        try
        {
            _restClient = new(RestEndpoint, HistoryEndpoint, Key, Secret)
            {
                Parent = this,
            };
            await _restClient.SynchronizeTimeAsync(cancellationToken);
            var pairs = await _restClient.GetPairsAsync(cancellationToken);
            if (pairs is not { Length: > 0 })
                throw new InvalidDataException(
                    "Indodax returned no market definitions.");
            RegisterMarkets(pairs);

            _socketClient = new(MarketDataWebSocketEndpoint,
                PrivateWebSocketEndpoint, _restClient,
                ReConnectionSettings.ReAttemptCount)
            {
                Parent = this,
            };
            _socketClient.BookReceived += OnSocketBookAsync;
            _socketClient.TradesReceived += OnSocketTradesAsync;
            _socketClient.PrivateEventsReceived += OnPrivateEventsAsync;
            _socketClient.Error += OnSocketErrorAsync;
            _socketClient.StateChanged += OnSocketStateAsync;
            await _socketClient.ConnectAsync(cancellationToken);
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

    private async ValueTask OnSocketStateAsync(ConnectionStates state,
        CancellationToken cancellationToken)
    {
        if (state == ConnectionStates.Failed)
        {
            await SendOutConnectionStateAsync(ConnectionStates.Failed,
                cancellationToken);
            return;
        }
        if (state != ConnectionStates.Restored)
            return;
        if (RestClient.IsCredentialsAvailable)
        {
            await RefreshPrivateSnapshotsAsync(cancellationToken);
            await RefreshOrderSnapshotsAsync(cancellationToken);
        }
        await SendOutConnectionStateAsync(ConnectionStates.Restored,
            cancellationToken);
    }

    private async ValueTask RefreshPrivateSnapshotsAsync(
        CancellationToken cancellationToken)
    {
        long[] subscriptions;
        using (_sync.EnterScope())
            subscriptions = [.. _portfolioSubscriptions];
        foreach (var subscriptionId in subscriptions)
            await SendPortfolioSnapshotAsync(subscriptionId, cancellationToken);
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
            catch (Exception error)
            {
                if (!cancellationToken.IsCancellationRequested)
                    await SendOutErrorAsync(error, cancellationToken);
            }
            finally
            {
                socketClient.Dispose();
            }
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
