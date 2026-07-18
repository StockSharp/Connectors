namespace StockSharp.Ourbit;

public partial class OurbitMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_spotRestClient is not null || _futuresRestClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting, cancellationToken);
		try
		{
			if (IsSectionEnabled(OurbitSections.Spot))
			{
				_spotRestClient = new(SpotRestEndpoint, Key, Secret, ReceiveWindow) { Parent = this };
				var exchangeInfo = await _spotRestClient.GetExchangeInfoAsync(cancellationToken);
				using (_sync.EnterScope())
				{
					foreach (var symbol in exchangeInfo?.Symbols ?? [])
					{
						if (symbol?.Symbol.IsEmpty() == false)
							_spotSymbols[symbol.Symbol] = symbol;
					}
				}
				_spotWsClient = CreateSpotWsClient();
				await _spotWsClient.ConnectAsync(cancellationToken);
			}

			if (IsSectionEnabled(OurbitSections.Futures))
			{
				_futuresRestClient = new(FuturesRestEndpoint, Key, Secret) { Parent = this };
				var products = await _futuresRestClient.GetProductsAsync(cancellationToken) ?? [];
				using (_sync.EnterScope())
				{
					foreach (var product in products)
					{
						if (product?.Symbol.IsEmpty() == false)
							_futuresProducts[product.Symbol] = product;
					}
				}
				_futuresWsClient = CreateFuturesWsClient();
				await _futuresWsClient.ConnectAsync(cancellationToken);
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
	protected override async ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		_ = timeMsg;
		if (_spotWsClient is not null)
			await _spotWsClient.PingAsync(cancellationToken);
		if (_futuresWsClient is not null)
			await _futuresWsClient.PingAsync(cancellationToken);
		if (!_spotListenKey.IsEmpty() && DateTime.UtcNow - _spotListenKeyTime >= TimeSpan.FromMinutes(20))
		{
			await SpotRestClient.KeepListenKeyAsync(_spotListenKey, cancellationToken);
			_spotListenKeyTime = DateTime.UtcNow;
		}
	}

	private OurbitSpotWsClient CreateSpotWsClient()
	{
		var client = new OurbitSpotWsClient(SpotWsEndpoint, ReConnectionSettings.WorkingTime,
			ReConnectionSettings.ReAttemptCount) { Parent = this };
		client.TickerReceived += OnSpotTickerAsync;
		client.DepthReceived += OnSpotDepthAsync;
		client.TradeReceived += OnSpotTradeAsync;
		client.CandleReceived += OnSpotCandleAsync;
		client.AccountReceived += OnSpotAccountAsync;
		client.OrderReceived += OnSpotOrderAsync;
		client.FillReceived += OnSpotFillAsync;
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnWebSocketStateAsync;
		return client;
	}

	private OurbitFuturesWsClient CreateFuturesWsClient()
	{
		var client = new OurbitFuturesWsClient(FuturesWsEndpoint, Key, Secret,
			ReConnectionSettings.WorkingTime, ReConnectionSettings.ReAttemptCount) { Parent = this };
		client.TickerReceived += OnFuturesTickerAsync;
		client.DepthReceived += OnFuturesDepthAsync;
		client.TradeReceived += OnFuturesTradeAsync;
		client.CandleReceived += OnFuturesCandleAsync;
		client.OrderReceived += OnFuturesOrderAsync;
		client.FillReceived += OnFuturesFillAsync;
		client.BalanceReceived += OnFuturesBalanceAsync;
		client.PositionReceived += OnFuturesPositionAsync;
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnWebSocketStateAsync;
		return client;
	}

	private async ValueTask EnsureSpotPrivateAsync(CancellationToken cancellationToken)
	{
		EnsurePrivateReady(OurbitSections.Spot);
		if (!_spotListenKey.IsEmpty())
			return;
		var result = await SpotRestClient.CreateListenKeyAsync(cancellationToken);
		_spotListenKey = result?.Value.ThrowIfEmpty("Ourbit spot listen key");
		_spotListenKeyTime = DateTime.UtcNow;
		await _spotWsClient.ConnectPrivateAsync(_spotListenKey, cancellationToken);
	}

	private async ValueTask RefreshPrivateSubscriptionsAsync(CancellationToken cancellationToken)
	{
		if (_spotRestClient?.IsCredentialsAvailable == true)
		{
			var hasPortfolio = _portfolioSubscriptionId != 0;
			var hasOrders = _orderStatusSubscriptionId != 0;
			if (hasPortfolio || hasOrders)
				await EnsureSpotPrivateAsync(cancellationToken);
			if (!_spotListenKey.IsEmpty())
			{
				await (hasPortfolio
					? _spotWsClient.SubscribePrivateAsync("spot@private.account.v3.api", cancellationToken)
					: _spotWsClient.UnsubscribePrivateAsync("spot@private.account.v3.api", cancellationToken));
				await (hasOrders
					? _spotWsClient.SubscribePrivateAsync("spot@private.orders.v3.api", cancellationToken)
					: _spotWsClient.UnsubscribePrivateAsync("spot@private.orders.v3.api", cancellationToken));
				await (hasOrders
					? _spotWsClient.SubscribePrivateAsync("spot@private.deals.v3.api", cancellationToken)
					: _spotWsClient.UnsubscribePrivateAsync("spot@private.deals.v3.api", cancellationToken));
			}
		}

		if (_futuresWsClient is not null && _futuresRestClient?.IsCredentialsAvailable == true)
			await _futuresWsClient.SetPrivateSubscriptionsAsync(
				orders: _orderStatusSubscriptionId != 0,
				fills: _orderStatusSubscriptionId != 0,
				positions: _portfolioSubscriptionId != 0,
				balances: _portfolioSubscriptionId != 0,
				cancellationToken);
	}

	private async ValueTask DisposeClientsAsync(CancellationToken cancellationToken)
	{
		var spotWs = _spotWsClient;
		var futuresWs = _futuresWsClient;
		_spotWsClient = null;
		_futuresWsClient = null;
		if (spotWs is not null)
		{
			try
			{
				await spotWs.DisconnectAsync(cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
			spotWs.Dispose();
		}
		if (futuresWs is not null)
		{
			try
			{
				await futuresWs.DisconnectAsync(cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
			futuresWs.Dispose();
		}

		if (!_spotListenKey.IsEmpty() && _spotRestClient?.IsCredentialsAvailable == true)
		{
			try
			{
				await _spotRestClient.CloseListenKeyAsync(_spotListenKey, cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				this.AddWarningLog("Unable to close Ourbit spot listen key: {0}", error.Message);
			}
		}

		_spotRestClient?.Dispose();
		_futuresRestClient?.Dispose();
		_spotRestClient = null;
		_futuresRestClient = null;
		_spotListenKey = null;
		_spotListenKeyTime = default;
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
		ClearState();
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_streamReferences.Clear();
			_spotSymbols.Clear();
			_futuresProducts.Clear();
			_futuresBooks.Clear();
		}
	}

	private ValueTask OnWebSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnWebSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			await SendOutConnectionStateAsync(ConnectionStates.Restored, cancellationToken);
			if (_portfolioSubscriptionId != 0)
				await SendPortfolioSnapshotAsync(_portfolioSubscriptionId, cancellationToken);
			if (_orderStatusSubscriptionId != 0)
				await SendOrderSnapshotAsync(_orderStatusSubscriptionId, null, null, null,
					cancellationToken);
		}
	}
}
