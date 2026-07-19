namespace StockSharp.Coinhako;

public partial class CoinhakoMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        if (_restClient is not null)
            throw new InvalidOperationException(
                LocalizedStrings.NotDisconnectPrevTime);
        if (Key.IsEmpty() != Secret.IsEmpty())
            throw new InvalidOperationException(
                "Coinhako public and private API keys must be configured together.");

        ClearState();
        var client = new CoinhakoRestClient(RestEndpoint, Key, Secret)
        {
            Parent = this,
        };
        _restClient = client;
        try
        {
            var prices = new List<CoinhakoSpotPrice>();
            foreach (var currency in GetCounterCurrencies())
                prices.AddRange(await client.GetSpotsAsync(null, currency,
                    cancellationToken) ?? []);
            RegisterMarkets(prices);
            if (GetMarkets().Length == 0)
                throw new InvalidDataException(
                    "Coinhako returned no supported spot markets.");
            connectMsg.SessionId = client.IsCredentialsAvailable
                ? $"Coinhako {Key.ToId()}"
                : "Coinhako public";
            await base.ConnectAsync(connectMsg, cancellationToken);
        }
        catch
        {
            DisposeClient();
            throw;
        }
    }

    /// <inheritdoc />
    protected override async ValueTask DisconnectAsync(
        DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
    {
        EnsureConnected();
        DisposeClient();
        await base.DisconnectAsync(disconnectMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ResetAsync(ResetMessage resetMsg,
        CancellationToken cancellationToken)
    {
        DisposeClient();
        await base.ResetAsync(resetMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask TimeAsync(TimeMessage timeMsg,
        CancellationToken cancellationToken)
    {
        var now = CurrentTime;
        bool refreshLevel1;
        bool refreshOrders;
        bool refreshPortfolio;
        using (_sync.EnterScope())
        {
            refreshLevel1 = _restClient is not null &&
                _level1Subscriptions.Count > 0 &&
                now - _lastLevel1Refresh >= PollingInterval;
            refreshOrders = _restClient is not null &&
                _orderSubscriptions.Count > 0 &&
                now - _lastOrderRefresh >= PollingInterval;
            refreshPortfolio = _restClient is not null &&
                _portfolioSubscriptions.Count > 0 &&
                now - _lastPortfolioRefresh >= PollingInterval;
            if (refreshLevel1)
                _lastLevel1Refresh = now;
            if (refreshOrders)
                _lastOrderRefresh = now;
            if (refreshPortfolio)
                _lastPortfolioRefresh = now;
        }

        if (refreshLevel1)
            await RunPollingAsync(RefreshLevel1Async, cancellationToken);
        if (refreshOrders)
            await RunPollingAsync(RefreshOrderSubscriptionsAsync,
                cancellationToken);
        if (refreshPortfolio)
            await RunPollingAsync(RefreshPortfolioSubscriptionsAsync,
                cancellationToken);

        await base.TimeAsync(timeMsg, cancellationToken);
    }

    private async ValueTask RunPollingAsync(
        Func<CancellationToken, ValueTask> action,
        CancellationToken cancellationToken)
    {
        try
        {
            await action(cancellationToken);
        }
        catch (Exception error)
        {
            await SendOutErrorAsync(error, cancellationToken);
        }
    }

    private void DisposeClient()
    {
        _restClient?.Dispose();
        _restClient = null;
        ClearState();
    }
}
