namespace StockSharp.FXOpen;

public partial class FXOpenMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        _ = connectMsg;
        if (_restClient is not null)
            throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

        ClearState();
        var hasAnyCredential = !WebApiId.IsEmpty() || !Key.IsEmpty() || !Secret.IsEmpty() ||
            !OneTimePassword.IsEmpty();
        if (hasAnyCredential && (WebApiId.IsEmpty() || Key.IsEmpty() || Secret.IsEmpty()))
            throw new InvalidOperationException(
                "FXOpen Web API ID, key, and secret must be configured together.");
        _restClient = new(Address, WebApiId, Key, Secret) { Parent = this };
        try
        {
            this.AddInfoLog("Connecting FXOpen ({0}) REST endpoint {1}.",
                IsDemo ? "demo" : "live", GetSafeEndpoint(Address));
            await SendOutConnectionStateAsync(ConnectionStates.Connecting, cancellationToken);
            foreach (var symbol in await _restClient.GetSymbolsAsync(cancellationToken))
            {
                if (!symbol.Symbol.IsEmpty())
                    _symbols[symbol.Symbol] = symbol;
            }
            this.AddInfoLog("FXOpen loaded {0} symbols.", _symbols.Count);

            if (_restClient.IsCredentialsAvailable)
            {
                _webSocketClient = CreateWebSocketClient();
                await _webSocketClient.ConnectAsync(cancellationToken);
                var account = await _restClient.GetAccountAsync(cancellationToken);
                _portfolioName = GetPortfolioName(account);
            }
            else
            {
                _portfolioName = "FXOpen_Public";
                this.AddInfoLog("FXOpen connected in public REST-only mode.");
            }

            await SendOutConnectionStateAsync(ConnectionStates.Connected, cancellationToken);
        }
        catch
        {
            await DisposeClientsAsync(CancellationToken.None);
            await SendOutConnectionStateAsync(ConnectionStates.Disconnected,
                CancellationToken.None);
            throw;
        }
    }

    /// <inheritdoc />
    protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
        CancellationToken cancellationToken)
    {
        _ = disconnectMsg;
        if (_restClient is null)
            throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
        await SendOutConnectionStateAsync(ConnectionStates.Disconnecting, cancellationToken);
        await DisposeClientsAsync(cancellationToken);
        await SendOutConnectionStateAsync(ConnectionStates.Disconnected, cancellationToken);
        this.AddInfoLog("FXOpen disconnected.");
    }

    /// <inheritdoc />
    protected override async ValueTask ResetAsync(ResetMessage resetMsg,
        CancellationToken cancellationToken)
    {
        await DisposeClientsAsync(cancellationToken);
        await base.ResetAsync(resetMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
    {
        _ = timeMsg;
        _ = cancellationToken;
        return default;
    }

    private FXOpenWebSocketClient CreateWebSocketClient()
    {
        var client = new FXOpenWebSocketClient(FeedAddress, TradeAddress, WebApiId, Key, Secret,
            OneTimePassword, ReConnectionSettings.WorkingTime,
            ReConnectionSettings.ReAttemptCount)
        { Parent = this };
        client.FeedReceived += ProcessFeed;
        client.BarReceived += ProcessBar;
        client.AccountReceived += ProcessAccount;
        client.ExecutionReceived += ProcessExecutionReport;
        client.Error += SendOutErrorAsync;
        client.StateChanged += ProcessWebSocketState;
        return client;
    }

    private async ValueTask ProcessWebSocketState(ConnectionStates state,
        CancellationToken cancellationToken)
    {
        if (state == ConnectionStates.Failed)
        {
            await SendOutConnectionStateAsync(state, cancellationToken);
            return;
        }
        if (state != ConnectionStates.Restored)
            return;

        try
        {
            if (_orderSubscriptionId != 0)
            {
                foreach (var trade in await RestClient.GetTradesAsync(cancellationToken))
                    await SendTrade(trade, _orderSubscriptionId, cancellationToken);
            }
            if (_portfolioSubscriptionId != 0)
                await SendPortfolioSnapshot(_portfolioSubscriptionId, false, cancellationToken);
        }
        catch (Exception error)
        {
            await SendOutErrorAsync(error, cancellationToken);
        }

        this.AddInfoLog("FXOpen WebSockets restored; active snapshots refreshed.");
        await SendOutConnectionStateAsync(state, cancellationToken);
    }

    private async ValueTask DisposeClientsAsync(CancellationToken cancellationToken)
    {
        var webSocket = _webSocketClient;
        _webSocketClient = null;
        try
        {
            if (webSocket is not null)
            {
                try
                {
                    await webSocket.DisconnectAsync(cancellationToken);
                }
                catch (Exception error) when (!cancellationToken.IsCancellationRequested)
                {
                    await SendOutErrorAsync(error, cancellationToken);
                }
            }
        }
        finally
        {
            webSocket?.Dispose();
            _restClient?.Dispose();
            _restClient = null;
            ClearState();
        }
    }

    private void ClearState()
    {
        using (_sync.EnterScope())
        {
            _symbols.Clear();
            _feedSubscriptions.Clear();
            _candleSubscriptions.Clear();
            _orderTransactions.Clear();
            _clientTransactions.Clear();
            _portfolioSubscriptionId = 0;
            _orderSubscriptionId = 0;
            _portfolioName = null;
        }
    }

    private static string GetPortfolioName(TickTraderAccount account)
        => account is null ? "FXOpen" : $"FXOpen_{account.Id}";

    private static string GetSafeEndpoint(string address)
    {
        if (address.IsEmpty())
            return "<empty>";
        var normalized = address.Contains("://", StringComparison.Ordinal)
            ? address : $"https://{address}";
        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            ? uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path,
                UriFormat.SafeUnescaped).TrimEnd('/')
            : "<invalid>";
    }
}
