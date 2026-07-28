namespace StockSharp.LCX;

public partial class LcxMessageAdapter
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
				"LCX private polling interval must be positive.");

		RestEndpoint = NormalizeEndpoint(
			RestEndpoint, _defaultRestEndpoint, "https");
		KlineEndpoint = NormalizeEndpoint(
			KlineEndpoint, _defaultKlineEndpoint, "https");
		WebSocketEndpoint = NormalizeEndpoint(
			WebSocketEndpoint,
			_defaultWebSocketEndpoint,
			"wss");
		ApiVersion = ApiVersion.IsEmpty(_defaultApiVersion).Trim();
		ClearState();
		await SendOutConnectionStateAsync(
			ConnectionStates.Connecting, cancellationToken);
		try
		{
			_restClient = new(
				RestEndpoint,
				KlineEndpoint,
				ApiVersion,
				Key,
				Secret)
			{
				Parent = this,
			};
			var markets = await RestClient.GetMarketsAsync(
				cancellationToken);
			var tickers = await RestClient.GetTickersAsync(
				cancellationToken);
			if (markets is not { Length: > 0 })
				throw new InvalidDataException(
					"LCX returned no spot markets.");
			RegisterMarkets(markets, tickers);

			_wsClient = new(
				WebSocketEndpoint,
				Key,
				Secret,
				ReConnectionSettings.WorkingTime,
				ReConnectionSettings.ReAttemptCount)
			{
				Parent = this,
			};
			_wsClient.MessageReceived +=
				OnWebSocketMessageAsync;
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
		if (_wsClient is not null)
			await _wsClient.PingAsync(cancellationToken);
		if (_restClient?.IsCredentialsAvailable != true ||
			(_portfolioSubscriptionId == 0 &&
				_orderStatusSubscriptionId == 0) ||
			CurrentTime - _lastPrivatePoll <
				PrivatePollingInterval ||
			!await _pollSync.WaitAsync(0, cancellationToken))
			return;
		try
		{
			var from = _lastPrivatePoll;
			_lastPrivatePoll = CurrentTime;
			if (_portfolioSubscriptionId != 0)
				await SendPortfolioSnapshotAsync(
					_portfolioSubscriptionId,
					cancellationToken);
			if (_orderStatusSubscriptionId != 0)
				await PollOrdersAsync(
					_orderStatusSubscriptionId,
					from,
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
		LcxWsMessage message,
		CancellationToken cancellationToken)
	{
		foreach (var ticker in message?.Tickers ?? [])
			await ProcessTickerAsync(
				ticker, cancellationToken);
		if (message?.Book is not null)
			await ProcessBookAsync(
				message.Book, cancellationToken);
		foreach (var trade in message?.Trades ?? [])
			await ProcessTradeAsync(
				trade, cancellationToken);
		if (_portfolioSubscriptionId != 0)
		{
			foreach (var balance in message?.Balances ?? [])
				await SendBalanceAsync(
					balance,
					_portfolioSubscriptionId,
					cancellationToken);
		}
		if (_orderStatusSubscriptionId != 0)
		{
			if (message?.Order is not null)
				await SendOrderAsync(
					message.Order,
					_orderStatusSubscriptionId,
					cancellationToken);
			if (message?.UserTrade is not null)
				await SendUserTradeAsync(
					message.UserTrade,
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

	private void UnsubscribeClientEvents(LcxWsClient client)
	{
		client.MessageReceived -= OnWebSocketMessageAsync;
		client.Error -= OnWebSocketErrorAsync;
		client.StateChanged -= OnWebSocketStateAsync;
	}
}
