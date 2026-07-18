namespace StockSharp.WhiteBit;

public partial class WhiteBitMessageAdapter
{
    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        _ = connectMsg;

        if (this.IsTransactional())
        {
            if (Key.IsEmpty())
                throw new InvalidOperationException(LocalizedStrings.KeyNotSpecified);
            if (Secret.IsEmpty())
                throw new InvalidOperationException(LocalizedStrings.SecretNotSpecified);
        }

        if (_restClient is not null || _marketWsClient is not null || _userWsClient is not null)
            throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

        ClearSubscriptions();
        _restClient = new(RestEndpoint, Key, Secret) { Parent = this };
        _marketWsClient = new(WsEndpoint, false, null, ReConnectionSettings.WorkingTime)
        {
            Parent = this,
        };
        _marketWsClient.MarketReceived += OnMarketAsync;
        _marketWsClient.DepthReceived += OnDepthAsync;
        _marketWsClient.TradesReceived += OnTradesAsync;
        _marketWsClient.Error += OnWebSocketErrorAsync;
        _portfolioName = GetPortfolioName(Key);

        await SendOutConnectionStateAsync(ConnectionStates.Connecting, cancellationToken);
        try
        {
            await _marketWsClient.ConnectAsync(cancellationToken);

            if (!Key.IsEmpty() && !Secret.IsEmpty())
            {
                var markets = await RestClient.GetMarketsAsync(cancellationToken) ?? [];
                using (_sync.EnterScope())
                {
                    foreach (var market in markets.Where(static item => item?.Name.IsEmpty() == false))
                    {
                        var section = market.Type is WhiteBitMarketTypes.Futures or WhiteBitMarketTypes.TradFiFutures
                            ? WhiteBitSections.Futures
                            : WhiteBitSections.Spot;
                        _marketBoards[market.Name] = section.ToBoardCode();
                    }
                }

                _userWsClient = new(WsEndpoint, true, async token =>
                    (await RestClient.GetWebSocketTokenAsync(token)).Token,
                    ReConnectionSettings.WorkingTime)
                {
                    Parent = this,
                };
                _userWsClient.SetPrivateSymbols(markets
                    .Where(static market => market?.Name.IsEmpty() == false)
                    .Select(static market => market.Name));
                _userWsClient.SpotBalanceReceived += OnSpotBalancesAsync;
                _userWsClient.MarginBalanceReceived += OnMarginBalancesAsync;
                _userWsClient.PendingOrderReceived += OnPendingOrderAsync;
                _userWsClient.ExecutedOrderReceived += OnExecutedOrderAsync;
                _userWsClient.UserTradeReceived += OnUserTradeAsync;
                _userWsClient.PositionReceived += OnPositionsAsync;
                _userWsClient.Error += OnWebSocketErrorAsync;
                _userWsClient.StateChanged += OnUserWsStateChangedAsync;
                await _userWsClient.ConnectAsync(cancellationToken);
            }

            await SendOutConnectionStateAsync(ConnectionStates.Connected, cancellationToken);
        }
        catch
        {
            await DisposeClientsAsync(cancellationToken);
            await SendOutConnectionStateAsync(ConnectionStates.Disconnected, cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
        CancellationToken cancellationToken)
    {
        _ = disconnectMsg;
        EnsureConnected();

        await SendOutConnectionStateAsync(ConnectionStates.Disconnecting, cancellationToken);
        await DisposeClientsAsync(cancellationToken);
        await SendOutConnectionStateAsync(ConnectionStates.Disconnected, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ResetAsync(ResetMessage resetMsg,
        CancellationToken cancellationToken)
    {
        await DisposeClientsAsync(cancellationToken);
        await base.ResetAsync(resetMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
    {
        _ = timeMsg;
        if (_marketWsClient is not null)
            await _marketWsClient.PingAsync(cancellationToken);
        if (_userWsClient is not null)
            await _userWsClient.PingAsync(cancellationToken);
        WhiteBitWsClient[] candleClients;
        using (_sync.EnterScope())
            candleClients = [.. _candleWsClients.Values];
        foreach (var client in candleClients)
            await client.PingAsync(cancellationToken);
    }

    private async ValueTask DisposeClientsAsync(CancellationToken cancellationToken)
    {
        WhiteBitWsClient[] candleClients;
        using (_sync.EnterScope())
        {
            candleClients = [.. _candleWsClients.Values];
            _candleWsClients.Clear();
        }
        foreach (var client in candleClients)
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

        ClearSubscriptions();

        if (_userWsClient is not null)
        {
            _userWsClient.SpotBalanceReceived -= OnSpotBalancesAsync;
            _userWsClient.MarginBalanceReceived -= OnMarginBalancesAsync;
            _userWsClient.PendingOrderReceived -= OnPendingOrderAsync;
            _userWsClient.ExecutedOrderReceived -= OnExecutedOrderAsync;
            _userWsClient.UserTradeReceived -= OnUserTradeAsync;
            _userWsClient.PositionReceived -= OnPositionsAsync;
            _userWsClient.Error -= OnWebSocketErrorAsync;
            _userWsClient.StateChanged -= OnUserWsStateChangedAsync;
            try
            {
                await _userWsClient.DisconnectAsync(cancellationToken);
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                await SendOutErrorAsync(error, cancellationToken);
            }
            _userWsClient.Dispose();
            _userWsClient = null;
        }

        if (_marketWsClient is not null)
        {
            _marketWsClient.MarketReceived -= OnMarketAsync;
            _marketWsClient.DepthReceived -= OnDepthAsync;
            _marketWsClient.TradesReceived -= OnTradesAsync;
            _marketWsClient.Error -= OnWebSocketErrorAsync;
            try
            {
                await _marketWsClient.DisconnectAsync(cancellationToken);
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                await SendOutErrorAsync(error, cancellationToken);
            }
            _marketWsClient.Dispose();
            _marketWsClient = null;
        }

        _restClient?.Dispose();
        _restClient = null;
        _portfolioName = null;
        _portfolioSubscriptionId = 0;
        _orderStatusSubscriptionId = 0;
    }

    private void ClearSubscriptions()
    {
        using (_sync.EnterScope())
        {
            _level1Subscriptions.Clear();
            _depthSubscriptions.Clear();
            _tickSubscriptions.Clear();
            _candleSubscriptions.Clear();
            _candleWsClients.Clear();
            _marketReferences.Clear();
            _depthReferences.Clear();
            _tradeReferences.Clear();
            _candleReferences.Clear();
            _marketBoards.Clear();
            _orderBoards.Clear();
        }
    }

    private ValueTask OnWebSocketErrorAsync(Exception error, CancellationToken cancellationToken)
        => SendOutErrorAsync(error, cancellationToken);

    private async ValueTask OnUserWsStateChangedAsync(ConnectionStates state,
        CancellationToken cancellationToken)
    {
        if (state != ConnectionStates.Connected)
            return;

        try
        {
            if (_portfolioSubscriptionId != 0)
            {
                if (IsSectionEnabled(WhiteBitSections.Spot))
                {
                    foreach (var balance in await RestClient.GetSpotBalancesAsync(cancellationToken) ?? [])
                        await SendSpotBalanceAsync(balance, _portfolioSubscriptionId, cancellationToken);
                }

                if (IsSectionEnabled(WhiteBitSections.Margin) || IsSectionEnabled(WhiteBitSections.Futures))
                {
                    foreach (var balance in await RestClient.GetMarginBalancesAsync(cancellationToken) ?? [])
                        await SendMarginBalanceAsync(balance, _portfolioSubscriptionId, cancellationToken);
                    foreach (var position in await RestClient.GetPositionsAsync(null, cancellationToken) ?? [])
                        await SendPositionAsync(position, _portfolioSubscriptionId, cancellationToken);
                }
            }

            if (_orderStatusSubscriptionId != 0)
            {
                foreach (var order in await RestClient.GetOpenOrdersAsync(null, cancellationToken) ?? [])
                {
                    await SendOrderAsync(order, InferBoardCode(order, null), ParseTransactionId(order.ClientOrderId),
                        _orderStatusSubscriptionId, null, 0m, 0m, null, null, cancellationToken);
                }
                if (IsSectionEnabled(WhiteBitSections.Margin) || IsSectionEnabled(WhiteBitSections.Futures))
                {
                    foreach (var order in await RestClient.GetConditionalOrdersAsync(null, cancellationToken) ?? [])
                    {
                        await SendOrderAsync(order, InferBoardCode(order, null), ParseTransactionId(order.ClientOrderId),
                            _orderStatusSubscriptionId, OrderTypes.Conditional, 0m, 0m, null, null,
                            cancellationToken);
                    }
                }
                foreach (var trade in await RestClient.GetExecutedHistoryAsync(null, 100, cancellationToken) ?? [])
                    await SendUserTradeAsync(trade, _orderStatusSubscriptionId, cancellationToken);
            }
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            await SendOutErrorAsync(error, cancellationToken);
        }
    }
}
