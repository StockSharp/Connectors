namespace StockSharp.MercadoBitcoin;

public partial class MercadoBitcoinMessageAdapter
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
                "Mercado Bitcoin client ID and secret must be configured together.");

        ClearState();
        await SendOutConnectionStateAsync(ConnectionStates.Connecting,
            cancellationToken);
        try
        {
            _restClient = new(RestEndpoint, Key, Secret)
            {
                Parent = this,
            };
            var symbols = await RestClient.GetSymbolsAsync(new(),
                cancellationToken);
            RegisterMarkets(symbols);
            using (_sync.EnterScope())
                if (_markets.Count == 0)
                    throw new InvalidDataException(
                        "Mercado Bitcoin returned no tradeable markets.");

            _socketClient = new(WebSocketEndpoint,
                ReConnectionSettings.WorkingTime,
                ReConnectionSettings.ReAttemptCount)
            {
                Parent = this,
            };
            _socketClient.TickerReceived += OnSocketTickerAsync;
            _socketClient.OrderBookReceived += OnSocketOrderBookAsync;
            _socketClient.TradeReceived += OnSocketTradeAsync;
            _socketClient.Error += OnSocketErrorAsync;
            _socketClient.StateChanged += OnSocketStateAsync;
            await _socketClient.ConnectAsync(cancellationToken);

            if (RestClient.IsCredentialsAvailable)
            {
                var accounts = await RestClient.GetAccountsAsync(cancellationToken);
                RegisterAccounts(accounts);
                using (_sync.EnterScope())
                {
                    if (_accounts.Count == 0)
                        throw new InvalidDataException(
                            "Mercado Bitcoin returned no accounts.");
                    if (!AccountId.IsEmpty() &&
                        !_accounts.ContainsKey(AccountId))
                        throw new InvalidOperationException(
                            $"Mercado Bitcoin account '{AccountId}' was not found.");
                }
                StartPrivatePolling();
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

    private async ValueTask OnSocketStateAsync(ConnectionStates state,
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

    private void StartPrivatePolling()
    {
        _pollingCancellation = new();
        _pollingTask = RunPrivatePollingAsync(_pollingCancellation.Token);
    }

    private async ValueTask StopPrivatePollingAsync()
    {
        var cancellation = _pollingCancellation;
        _pollingCancellation = null;
        var task = _pollingTask;
        _pollingTask = null;
        if (cancellation is null)
            return;
        cancellation.Cancel();
        try
        {
            if (task is not null)
                await task;
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private async ValueTask DisposeClientsAsync(
        CancellationToken cancellationToken)
    {
        await StopPrivatePollingAsync();
        var socket = _socketClient;
        _socketClient = null;
        var rest = _restClient;
        _restClient = null;
        if (socket is not null)
        {
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
        rest?.Dispose();
        ClearState();
    }

    private void DisposeClients()
    {
        _pollingCancellation?.Cancel();
        _pollingCancellation?.Dispose();
        _pollingCancellation = null;
        _pollingTask = null;
        _socketClient?.Dispose();
        _socketClient = null;
        _restClient?.Dispose();
        _restClient = null;
        ClearState();
    }
}
