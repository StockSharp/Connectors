namespace StockSharp.Backpack;

public partial class BackpackMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		ClearState();
		_restClient = new(RestEndpoint, Key, Secret) { Parent = this };
		await SendOutConnectionStateAsync(ConnectionStates.Connecting, cancellationToken);
		try
		{
			var markets = await RestClient.GetMarketsAsync(cancellationToken);
			RegisterMarkets(markets);

			_wsClient = CreateWebSocketClient();
			await _wsClient.ConnectAsync(cancellationToken);
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
	protected override ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		_ = timeMsg;
		_ = cancellationToken;
		return default;
	}

	private BackpackWsClient CreateWebSocketClient()
	{
		var client = new BackpackWsClient(WebSocketEndpoint, RestClient,
			ReConnectionSettings.WorkingTime, ReConnectionSettings.ReAttemptCount)
		{
			Parent = this,
		};
		client.BookTickerReceived += OnBookTickerAsync;
		client.DepthReceived += OnDepthAsync;
		client.TradeReceived += OnTradeAsync;
		client.TickerReceived += OnTickerAsync;
		client.KlineReceived += OnKlineAsync;
		client.MarkPriceReceived += OnMarkPriceAsync;
		client.OrderReceived += OnOrderUpdateAsync;
		client.PositionReceived += OnPositionUpdateAsync;
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnWebSocketStateAsync;
		return client;
	}

	private async ValueTask DisposeClientsAsync(CancellationToken cancellationToken)
	{
		var wsClient = _wsClient;
		_wsClient = null;
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

		_restClient?.Dispose();
		_restClient = null;
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
		ClearState();
	}

	private ValueTask OnWebSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnWebSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Failed)
		{
			await SendOutConnectionStateAsync(state, cancellationToken);
			return;
		}
		if (state != ConnectionStates.Restored)
			return;

		await ResynchronizeAllDepthsAsync(cancellationToken);
		if (_portfolioSubscriptionId != 0 && RestClient.IsCredentialsAvailable)
			await SendPortfolioSnapshotAsync(_portfolioSubscriptionId, cancellationToken);
		if (_orderStatusSubscriptionId != 0 && RestClient.IsCredentialsAvailable)
			await SendOrderSnapshotAsync(_orderStatusSubscriptionId, null, null, null, 1000,
				cancellationToken);
		await SendOutConnectionStateAsync(state, cancellationToken);
	}

	private void RegisterMarkets(IEnumerable<BackpackMarket> markets)
	{
		using (_sync.EnterScope())
			foreach (var market in markets ?? [])
				if (market?.Symbol.IsEmpty() == false && market.IsVisible &&
					market.MarketType is BackpackMarketTypes.Spot or BackpackMarketTypes.Perpetual)
					_markets[market.Symbol] = market;
	}
}
