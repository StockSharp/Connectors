namespace StockSharp.Buda;

public partial class BudaMessageAdapter
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
		var configured = new[]
		{
			!Key.IsEmpty(),
			!Secret.IsEmpty(),
		};
		if (configured.Any(static value => value) &&
			!configured.All(static value => value))
			throw new InvalidOperationException(
				"Buda.com API key and secret must be configured " +
					"together.");
		if (PrivatePollingInterval <= TimeSpan.Zero)
			throw new InvalidOperationException(
				"Buda.com polling interval must be positive.");

		RestEndpoint = NormalizeEndpoint(
			RestEndpoint, _defaultRestEndpoint, "https");
		WebSocketEndpoint = NormalizeEndpoint(
			WebSocketEndpoint, _defaultWebSocketEndpoint, "wss");
		ClearState();
		await SendOutConnectionStateAsync(
			ConnectionStates.Connecting, cancellationToken);
		try
		{
			_restClient = new(RestEndpoint, Key, Secret)
			{
				Parent = this,
			};
			var markets = await RestClient.GetMarketsAsync(
				cancellationToken);
			if (markets is not { Length: > 0 })
				throw new InvalidDataException(
					"Buda.com returned no spot markets.");
			RegisterMarkets(markets);
			if (RestClient.IsCredentialsAvailable)
				_pubSubKey = (await RestClient.GetAccountAsync(
					cancellationToken))?.PubSubKey;

			_wsClient = new(
				WebSocketEndpoint,
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
		if (_restClient is null ||
			CurrentTime - _lastPoll < PrivatePollingInterval ||
			!await _pollSync.WaitAsync(0, cancellationToken))
			return;
		try
		{
			_lastPoll = CurrentTime;
			await RefreshLevel1Async(cancellationToken);
			if (RestClient.IsCredentialsAvailable)
			{
				if (_portfolioSubscriptionId != 0)
					await SendPortfolioSnapshotAsync(
						_portfolioSubscriptionId,
						cancellationToken);
				if (_orderStatusSubscriptionId != 0)
					await PollOrdersAsync(
						_orderStatusSubscriptionId,
						cancellationToken);
			}
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
		BudaWsMessage message,
		CancellationToken cancellationToken)
	{
		switch (message?.Event)
		{
			case "trade-created":
				await ProcessTradeMessageAsync(
					message, cancellationToken);
				break;

			case "book-sync":
			case "book-changed":
				await ProcessBookMessageAsync(
					message, cancellationToken);
				break;

			case "balance-updated":
				if (_portfolioSubscriptionId != 0)
					await SendBalanceAsync(
						message.Balance,
						_portfolioSubscriptionId,
						cancellationToken);
				break;

			case "order-created":
			case "order-updated":
				if (_orderStatusSubscriptionId != 0)
					await SendOrderAsync(
						message.Order,
						_orderStatusSubscriptionId,
						cancellationToken);
				break;
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

	private void UnsubscribeClientEvents(BudaWsClient client)
	{
		client.MessageReceived -= OnWebSocketMessageAsync;
		client.Error -= OnWebSocketErrorAsync;
		client.StateChanged -= OnWebSocketStateAsync;
	}
}
