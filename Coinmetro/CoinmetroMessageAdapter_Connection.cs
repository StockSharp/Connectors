namespace StockSharp.Coinmetro;

public partial class CoinmetroMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(
		ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null || _wsClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		if (PrivatePollingInterval <= TimeSpan.Zero)
			throw new InvalidOperationException(
				"Coinmetro polling interval must be positive.");

		RestEndpoint = NormalizeEndpoint(
			RestEndpoint, _defaultRestEndpoint, "https");
		WebSocketEndpoint = NormalizeEndpoint(
			WebSocketEndpoint, _defaultWebSocketEndpoint, "wss");
		DemoRestEndpoint = NormalizeEndpoint(
			DemoRestEndpoint, _defaultDemoRestEndpoint, "https");
		DemoWebSocketEndpoint = NormalizeEndpoint(
			DemoWebSocketEndpoint,
			_defaultDemoWebSocketEndpoint,
			"wss");
		ClearState();
		await SendOutConnectionStateAsync(
			ConnectionStates.Connecting, cancellationToken);
		try
		{
			_restClient = new(ActiveRestEndpoint, Token)
			{
				Parent = this,
			};
			if (IsDemo && !RestClient.IsCredentialsAvailable)
				await RestClient.GetDemoTokenAsync(
					cancellationToken);
			var assets = await RestClient.GetAssetsAsync(
				cancellationToken);
			var specs = await RestClient.GetMarketSpecsAsync(
				cancellationToken);
			var tickers = await RestClient.GetTickersAsync(
				cancellationToken);
			var markets = CoinmetroRestClient.CreateMarkets(
				assets, specs, tickers);
			if (markets is not { Length: > 0 })
				throw new InvalidDataException(
					"Coinmetro returned no spot markets.");
			RegisterMarkets(markets, tickers);

			_wsClient = new(
				ActiveWebSocketEndpoint,
				RestClient.AccessToken,
				ReConnectionSettings.WorkingTime,
				ReConnectionSettings.ReAttemptCount)
			{
				Parent = this,
			};
			_wsClient.MessageReceived += OnWebSocketMessageAsync;
			_wsClient.Error += OnWebSocketErrorAsync;
			_wsClient.StateChanged += OnWebSocketStateAsync;
			await _wsClient.ConnectAsync(cancellationToken);
			await SendOutConnectionStateAsync(
				ConnectionStates.Connected, cancellationToken);
		}
		catch
		{
			await DisposeClientsAsync(cancellationToken);
			await SendOutConnectionStateAsync(
				ConnectionStates.Disconnected, cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(
		DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		_ = disconnectMsg;
		EnsureConnected();
		await SendOutConnectionStateAsync(
			ConnectionStates.Disconnecting, cancellationToken);
		await DisposeClientsAsync(cancellationToken);
		await SendOutConnectionStateAsync(
			ConnectionStates.Disconnected, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(
		ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		await DisposeClientsAsync(cancellationToken);
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(
		TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		_ = timeMsg;
		if (_restClient?.IsCredentialsAvailable != true ||
			(_portfolioSubscriptionId == 0 &&
				_orderStatusSubscriptionId == 0) ||
			CurrentTime - _lastPrivatePoll <
				PrivatePollingInterval ||
			!await _pollSync.WaitAsync(0, cancellationToken))
			return;
		try
		{
			_lastPrivatePoll = CurrentTime;
			if (_portfolioSubscriptionId != 0)
				await SendPortfolioSnapshotAsync(
					_portfolioSubscriptionId,
					cancellationToken);
			if (_orderStatusSubscriptionId != 0)
				await PollOrdersAsync(
					_orderStatusSubscriptionId,
					cancellationToken);
		}
		catch (Exception error) when (
			!cancellationToken.IsCancellationRequested)
		{
			await SendOutErrorAsync(error, cancellationToken);
		}
		finally
		{
			_pollSync.Release();
		}
	}

	private async ValueTask OnWebSocketMessageAsync(
		CoinmetroWsMessage message,
		CancellationToken cancellationToken)
	{
		if (message?.Tick is not null)
			await ProcessTickerAsync(
				message.Tick, cancellationToken);
		if (message?.BookUpdate is not null)
			await ProcessBookUpdateAsync(
				message.BookUpdate, cancellationToken);
		if (message?.WalletUpdate is not null &&
			_portfolioSubscriptionId != 0)
			await SendBalanceAsync(
				message.WalletUpdate,
				_portfolioSubscriptionId,
				cancellationToken);
		if (message?.OrderStatus is not null &&
			_orderStatusSubscriptionId != 0)
		{
			var order = CoinmetroRestClient.ParseOrder(
				message.OrderStatus, GetMarkets());
			if (order is not null)
				await SendOrderAsync(
					order,
					_orderStatusSubscriptionId,
					cancellationToken);
		}
	}

	private ValueTask OnWebSocketErrorAsync(
		Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnWebSocketStateAsync(
		ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Failed)
			await SendOutConnectionStateAsync(
				ConnectionStates.Failed, cancellationToken);
		else if (state == ConnectionStates.Restored)
			await SendOutConnectionStateAsync(
				ConnectionStates.Restored, cancellationToken);
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		var client = _wsClient;
		_wsClient = null;
		if (client is not null)
		{
			UnsubscribeClientEvents(client);
			try
			{
				await client.DisconnectAsync(cancellationToken);
			}
			catch (Exception error)
			{
				if (!cancellationToken.IsCancellationRequested)
					await SendOutErrorAsync(
						error, cancellationToken);
			}
			finally
			{
				client.Dispose();
			}
		}
		_restClient?.Dispose();
		_restClient = null;
		ClearState();
	}

	private void DisposeClients()
	{
		if (_wsClient is not null)
			UnsubscribeClientEvents(_wsClient);
		_wsClient?.Dispose();
		_wsClient = null;
		_restClient?.Dispose();
		_restClient = null;
		ClearState();
	}

	private void UnsubscribeClientEvents(
		CoinmetroWsClient client)
	{
		client.MessageReceived -= OnWebSocketMessageAsync;
		client.Error -= OnWebSocketErrorAsync;
		client.StateChanged -= OnWebSocketStateAsync;
	}
}
