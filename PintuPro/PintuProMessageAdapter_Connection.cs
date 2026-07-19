namespace StockSharp.PintuPro;

public partial class PintuProMessageAdapter
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
                "Pintu Pro API key and HMAC secret must be configured together.");

        ClearState();
        await SendOutConnectionStateAsync(ConnectionStates.Connecting,
            cancellationToken);
        try
        {
            _restClient = new(RestEndpoint, Key, Secret) { Parent = this };
            var symbols = await RestClient.GetSymbolsAsync(cancellationToken);
            if (symbols?.Symbols is not { Length: > 0 })
                throw new InvalidDataException(
                    "Pintu Pro returned no symbol definitions.");
            RegisterMarkets(symbols.Symbols);

            _socketClient = new(WebSocketEndpoint, RestClient,
                ReConnectionSettings.ReAttemptCount)
            {
                Parent = this,
            };
            _socketClient.BookReceived += OnSocketBookAsync;
            _socketClient.PublicTradeReceived += OnSocketTradeAsync;
            _socketClient.OrderReceived += OnSocketOrderAsync;
            _socketClient.AccountTradeReceived += OnSocketAccountTradeAsync;
            _socketClient.BalanceReceived += OnSocketBalanceAsync;
            _socketClient.Error += OnSocketErrorAsync;
            _socketClient.StateChanged += OnSocketStateAsync;
            if (RestClient.IsCredentialsAvailable)
                await _socketClient.SetPrivateChannelsAsync(true,
                    cancellationToken);
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
