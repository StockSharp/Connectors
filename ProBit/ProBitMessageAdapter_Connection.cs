namespace StockSharp.ProBit;

public partial class ProBitMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		ClearState();
		_restClient = new(RestEndpoint, AuthEndpoint, Key, Secret) { Parent = this };
		_portfolioName = GetPortfolioName(Key);
		await SendOutConnectionStateAsync(ConnectionStates.Connecting, cancellationToken);
		try
		{
			_webSocketClient = CreateWebSocketClient();
			await _webSocketClient.ConnectAsync(cancellationToken);
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
	protected override ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		_ = timeMsg;
		_ = cancellationToken;
		return default;
	}

	private ProBitWsClient CreateWebSocketClient()
	{
		var client = new ProBitWsClient(WebSocketEndpoint,
			RestClient.IsCredentialsAvailable ? RestClient.GetAccessTokenAsync : null,
			ReConnectionSettings.WorkingTime) { Parent = this };
		client.MarketDataReceived += OnMarketDataAsync;
		client.BalanceReceived += OnBalanceAsync;
		client.OpenOrdersReceived += OnOpenOrdersAsync;
		client.OrderHistoryReceived += OnOrderHistoryAsync;
		client.TradeHistoryReceived += OnTradeHistoryAsync;
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnWebSocketStateChangedAsync;
		return client;
	}

	private async ValueTask DisposeClientsAsync(CancellationToken cancellationToken)
	{
		var webSocket = _webSocketClient;
		_webSocketClient = null;
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
			webSocket.Dispose();
		}

		_restClient?.Dispose();
		_restClient = null;
		_portfolioName = null;
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
			_books.Clear();
			_streamTradeCursors.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_accountTradeIds.Clear();
		}
	}

	private ValueTask OnWebSocketErrorAsync(Exception error, CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnWebSocketStateChangedAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state is not ConnectionStates.Restored || !RestClient.IsCredentialsAvailable)
			return;
		long[] portfolioSubscriptions;
		KeyValuePair<long, OrderSubscription>[] orderSubscriptions;
		using (_sync.EnterScope())
		{
			portfolioSubscriptions = [.. _portfolioSubscriptions];
			orderSubscriptions = [.. _orderSubscriptions];
		}
		foreach (var subscriptionId in portfolioSubscriptions)
			await SendPortfolioSnapshotAsync(subscriptionId, cancellationToken);
		foreach (var pair in orderSubscriptions)
			await SendOrderSnapshotAsync(pair.Key, pair.Value, null, null, 1000,
				cancellationToken);
	}
}
