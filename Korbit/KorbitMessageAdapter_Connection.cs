namespace StockSharp.Korbit;

public partial class KorbitMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        _ = connectMsg;
        if (_restClient is not null || _publicSocketClient is not null ||
            _privateSocketClient is not null)
            throw new InvalidOperationException(
                LocalizedStrings.NotDisconnectPrevTime);
        if (Key.IsEmpty() != Secret.IsEmpty())
            throw new InvalidOperationException(
                "Korbit API key and HMAC secret must be configured together.");
        if (AccountSequence <= 0)
            throw new InvalidOperationException(
                "Korbit account sequence must be positive.");

        ClearState();
        await SendOutConnectionStateAsync(ConnectionStates.Connecting,
            cancellationToken);
        try
        {
            _restClient = new(RestEndpoint, Key, Secret)
            {
                Parent = this,
            };
            await RestClient.SynchronizeTimeAsync(cancellationToken);
            var markets = await RestClient.GetTradingPairsAsync(
                cancellationToken);
            if (markets is not { Length: > 0 })
                throw new InvalidDataException(
                    "Korbit returned no trading-pair definitions.");
            RegisterMarkets(markets);

            _publicSocketClient = new(PublicWebSocketEndpoint, RestClient,
                false, null, AccountSequence,
                ReConnectionSettings.ReAttemptCount)
            {
                Parent = this,
            };
            _publicSocketClient.BookReceived += OnSocketBookAsync;
            _publicSocketClient.TickerReceived += OnSocketTickerAsync;
            _publicSocketClient.TradeReceived += OnSocketTradeAsync;
            _publicSocketClient.Error += OnSocketErrorAsync;
            _publicSocketClient.StateChanged += (state, token) =>
                OnSocketStateAsync(state, false, token);
            await _publicSocketClient.ConnectAsync(cancellationToken);

            if (RestClient.IsCredentialsAvailable)
            {
                string[] symbols;
                using (_sync.EnterScope())
                    symbols = [.. _markets.Values.Where(static market =>
                        market.Status == KorbitPairStatuses.Launched)
                        .Select(static market => market.Symbol)];
                _privateSocketClient = new(PrivateWebSocketEndpoint, RestClient,
                    true, symbols, AccountSequence,
                    ReConnectionSettings.ReAttemptCount)
                {
                    Parent = this,
                };
                _privateSocketClient.OrderReceived += OnMyOrderAsync;
                _privateSocketClient.AccountTradeReceived += OnMyTradeAsync;
                _privateSocketClient.AssetReceived += OnMyAssetAsync;
                _privateSocketClient.Error += OnSocketErrorAsync;
                _privateSocketClient.StateChanged += (state, token) =>
                    OnSocketStateAsync(state, true, token);
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

    private async ValueTask OnSocketStateAsync(ConnectionStates state,
        bool isPrivate, CancellationToken cancellationToken)
    {
        if (state == ConnectionStates.Failed)
        {
            await SendOutConnectionStateAsync(ConnectionStates.Failed,
                cancellationToken);
            return;
        }
        if (state != ConnectionStates.Restored)
            return;
        if (isPrivate)
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
        if (!RestClient.IsCredentialsAvailable)
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
        var privateClient = _privateSocketClient;
        _privateSocketClient = null;
        if (privateClient is not null)
        {
            try
            {
                await privateClient.DisconnectAsync(cancellationToken);
            }
            catch (Exception error)
            {
                if (!cancellationToken.IsCancellationRequested)
                    await SendOutErrorAsync(error, cancellationToken);
            }
            finally
            {
                privateClient.Dispose();
            }
        }

        var publicClient = _publicSocketClient;
        _publicSocketClient = null;
        if (publicClient is not null)
        {
            try
            {
                await publicClient.DisconnectAsync(cancellationToken);
            }
            catch (Exception error)
            {
                if (!cancellationToken.IsCancellationRequested)
                    await SendOutErrorAsync(error, cancellationToken);
            }
            finally
            {
                publicClient.Dispose();
            }
        }

        _restClient?.Dispose();
        _restClient = null;
        ClearState();
    }

    private void DisposeClients()
    {
        _privateSocketClient?.Dispose();
        _privateSocketClient = null;
        _publicSocketClient?.Dispose();
        _publicSocketClient = null;
        _restClient?.Dispose();
        _restClient = null;
        ClearState();
    }
}
