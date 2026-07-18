namespace StockSharp.Bitunix;

public partial class BitunixMessageAdapter
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
			if (IsSectionEnabled(BitunixSections.Spot))
			{
				_spotRestClient = new("Spot", SpotRestEndpoint, Key, Secret) { Parent = this };
				var pairs = await _spotRestClient.GetSpotPairsAsync(cancellationToken) ?? [];
				using (_sync.EnterScope())
				{
					foreach (var pair in pairs)
					{
						if (pair?.Symbol.IsEmpty() == false)
							_spotPairs[pair.Symbol.ToUpperInvariant()] = pair;
					}
				}
			}

			if (IsSectionEnabled(BitunixSections.Futures))
			{
				_futuresRestClient = new("Futures", FuturesRestEndpoint, Key, Secret)
				{
					Parent = this,
				};
				var products = await _futuresRestClient.GetFuturesProductsAsync(cancellationToken) ?? [];
				using (_sync.EnterScope())
				{
					foreach (var product in products)
					{
						if (product?.Symbol.IsEmpty() == false)
							_futuresProducts[product.Symbol.ToUpperInvariant()] = product;
					}
				}

				_futuresWsClient = CreateFuturesWsClient();
				await _futuresWsClient.ConnectAsync(cancellationToken);
			}

			_lastPollingTime = DateTime.UtcNow;
			_lastPingTime = DateTime.UtcNow;
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
		var now = DateTime.UtcNow;
		if (_futuresWsClient is not null && now - _lastPingTime >= TimeSpan.FromSeconds(3))
		{
			_lastPingTime = now;
			try
			{
				await _futuresWsClient.PingAsync(cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
		}

		if (_spotRestClient is null || now - _lastPollingTime < PollingInterval)
			return;

		_lastPollingTime = now;
		try
		{
			await PollSpotMarketDataAsync(cancellationToken);
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested)
		{
			await SendOutErrorAsync(error, cancellationToken);
		}

		if (_spotRestClient.IsCredentialsAvailable &&
			(_portfolioSubscriptionId != 0 || _orderStatusSubscriptionId != 0))
		{
			try
			{
				await PollSpotPrivateDataAsync(cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
		}
	}

	private BitunixFuturesWsClient CreateFuturesWsClient()
	{
		var client = new BitunixFuturesWsClient(FuturesPublicWsEndpoint,
			FuturesPrivateWsEndpoint, Key, Secret, ReConnectionSettings.WorkingTime,
			ReConnectionSettings.ReAttemptCount)
		{
			Parent = this,
		};
		client.TickerReceived += OnFuturesTickerAsync;
		client.PriceReceived += OnFuturesPriceAsync;
		client.DepthReceived += OnFuturesDepthAsync;
		client.TradeReceived += OnFuturesTradeAsync;
		client.CandleReceived += OnFuturesCandleAsync;
		client.OrderReceived += OnFuturesOrderAsync;
		client.BalanceReceived += OnFuturesBalanceAsync;
		client.PositionReceived += OnFuturesPositionAsync;
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnWebSocketStateAsync;
		return client;
	}

	private ValueTask RefreshPrivateSubscriptionsAsync(CancellationToken cancellationToken)
		=> _futuresWsClient is not null && _futuresRestClient?.IsCredentialsAvailable == true
			? _futuresWsClient.SetPrivateSubscriptionsAsync(
				orders: _orderStatusSubscriptionId != 0,
				positions: _portfolioSubscriptionId != 0,
				balances: _portfolioSubscriptionId != 0,
				cancellationToken)
			: default;

	private async ValueTask DisposeClientsAsync(CancellationToken cancellationToken)
	{
		var wsClient = _futuresWsClient;
		_futuresWsClient = null;
		if (wsClient is not null)
		{
			try
			{
				await wsClient.DisconnectAsync(cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
			wsClient.Dispose();
		}

		_spotRestClient?.Dispose();
		_futuresRestClient?.Dispose();
		_spotRestClient = null;
		_futuresRestClient = null;
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
		_lastPollingTime = default;
		_lastPingTime = default;
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
			_spotPairs.Clear();
			_futuresProducts.Clear();
			_spotOrderSymbols.Clear();
			_seenSpotFillIds.Clear();
			_seenFuturesFillIds.Clear();
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
			await RefreshPrivateSubscriptionsAsync(cancellationToken);
		}
	}
}
