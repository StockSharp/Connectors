namespace StockSharp.ZondaCrypto;

public partial class ZondaCryptoMessageAdapter
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
				"zondacrypto API key and secret must be " +
					"configured together.");
		if (PrivatePollingInterval <= TimeSpan.Zero)
			throw new InvalidOperationException(
				"zondacrypto polling interval must be positive.");

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
			var tickers = await RestClient.GetTickersAsync(
				cancellationToken);
			if (tickers is not { Length: > 0 })
				throw new InvalidDataException(
					"zondacrypto returned no spot markets.");
			RegisterMarkets(tickers);

			_wsClient = new(
				WebSocketEndpoint,
				Key,
				Secret,
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
		if (_wsClient is not null &&
			CurrentTime - _lastHeartbeat >=
				TimeSpan.FromSeconds(25))
		{
			await WsClient.SendHeartbeatAsync(cancellationToken);
			_lastHeartbeat = CurrentTime;
		}
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
		ZondaCryptoWsMessage message,
		CancellationToken cancellationToken)
	{
		if (message is null ||
			!message.Action.EqualsIgnoreCase("push"))
			return;
		if (message.Ticker is not null)
			await ProcessTickerMessageAsync(
				message.Ticker, cancellationToken);
		if (message.BookChanges.Length > 0)
			await ProcessBookMessageAsync(
				message, cancellationToken);
		foreach (var trade in message.Trades)
			await ProcessTradeMessageAsync(
				trade, cancellationToken);
		if (message.Wallet is not null &&
			_portfolioSubscriptionId != 0)
			await SendBalanceAsync(
				message.Wallet,
				_portfolioSubscriptionId,
				cancellationToken);
		if (message.Offer is not null &&
			_orderStatusSubscriptionId != 0)
			await SendOrderAsync(
				message.Offer,
				_orderStatusSubscriptionId,
				cancellationToken);
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
		ZondaCryptoWsClient client)
	{
		client.MessageReceived -= OnWebSocketMessageAsync;
		client.Error -= OnWebSocketErrorAsync;
		client.StateChanged -= OnWebSocketStateAsync;
	}
}
