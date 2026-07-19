namespace StockSharp.OSL;

public partial class OSLMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        if (_restClient is not null)
            throw new InvalidOperationException(
                LocalizedStrings.NotDisconnectPrevTime);

        ClearState();
        _restClient = new(RestEndpoint, Key, Secret, Passphrase)
        {
            Parent = this,
        };
        await SendOutConnectionStateAsync(ConnectionStates.Connecting,
            cancellationToken);
        try
        {
            RegisterSymbols(await RestClient.GetSymbolsAsync(null,
                cancellationToken));
            using (_sync.EnterScope())
                if (_symbols.Count == 0)
                    throw new InvalidDataException(
                        "OSL returned no SPOT instruments.");

            _publicSocket = CreateSocketClient(PublicWsEndpoint,
                OSLSocketKinds.Public);
            _publicSocket.TickerReceived += OnSocketTickerAsync;
            _publicSocket.BookReceived += OnSocketBookAsync;
            _publicSocket.TradeReceived += OnSocketTradeAsync;
            _publicSocket.StateChanged += OnPublicSocketStateAsync;

            _candleSocket = CreateSocketClient(CandleWsEndpoint,
                OSLSocketKinds.Candles);
            _candleSocket.CandleReceived += OnSocketCandleAsync;

            await PublicSocket.ConnectAsync(cancellationToken);
            await CandleSocket.ConnectAsync(cancellationToken);

            if (RestClient.IsPrivateAvailable)
            {
                _privateSocket = CreateSocketClient(PrivateWsEndpoint,
                    OSLSocketKinds.Private);
                _privateSocket.OrderReceived += OnSocketOrderAsync;
                _privateSocket.FillReceived += OnSocketFillAsync;
                _privateSocket.AssetsReceived += OnSocketAssetsAsync;
                await PrivateSocket.ConnectAsync(cancellationToken);
            }

            connectMsg.SessionId = RestClient.IsPrivateAvailable
                ? $"OSL {Key.ToId()}"
                : "OSL public";
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
            if (_publicSocket is not null &&
                CurrentTime - _lastPing >= TimeSpan.FromSeconds(25))
            {
                _lastPing = CurrentTime;
                ping = true;
            }
        if (ping)
        {
            await RunSafelyAsync(PublicSocket.PingAsync, cancellationToken);
            await RunSafelyAsync(CandleSocket.PingAsync, cancellationToken);
            if (_privateSocket is not null)
                await RunSafelyAsync(PrivateSocket.PingAsync,
                    cancellationToken);
        }
        await base.TimeAsync(timeMsg, cancellationToken);
    }

    private OSLSocketClient CreateSocketClient(string endpoint,
        OSLSocketKinds kind)
    {
        var client = new OSLSocketClient(endpoint, kind, Key, Secret,
            Passphrase, ReConnectionSettings.WorkingTime,
            ReConnectionSettings.ReAttemptCount)
        {
            Parent = this,
        };
        client.Error += OnSocketErrorAsync;
        return client;
    }

    private ValueTask OnSocketErrorAsync(Exception error,
        CancellationToken cancellationToken)
        => SendOutErrorAsync(error, cancellationToken);

    private async ValueTask OnPublicSocketStateAsync(ConnectionStates state,
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
        var clients = new[] { _privateSocket, _candleSocket, _publicSocket };
        _privateSocket = null;
        _candleSocket = null;
        _publicSocket = null;
        foreach (var client in clients.Where(static value => value is not null))
        {
            try
            {
                await client.DisconnectAsync(cancellationToken);
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                await SendOutErrorAsync(error, cancellationToken);
            }
            client.Dispose();
        }
        _restClient?.Dispose();
        _restClient = null;
        ClearState();
    }

    private void DisposeClients()
    {
        _privateSocket?.Dispose();
        _privateSocket = null;
        _candleSocket?.Dispose();
        _candleSocket = null;
        _publicSocket?.Dispose();
        _publicSocket = null;
        _restClient?.Dispose();
        _restClient = null;
        ClearState();
    }
}
