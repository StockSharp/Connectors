namespace StockSharp.CoinCatch;

public partial class CoinCatchMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(
		ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null || _wsClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);

		var configured = new[]
		{
			!Key.IsEmpty(),
			!Secret.IsEmpty(),
			!Passphrase.IsEmpty(),
		};
		if (configured.Any(static value => value) &&
			!configured.All(static value => value))
			throw new InvalidOperationException(
				"CoinCatch API key, secret and passphrase must be " +
					"configured together.");

		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_restClient = new(
				RestEndpoint, ProductType, Key, Secret, Passphrase)
			{
				Parent = this,
			};
			var markets = await RestClient.GetSymbolsAsync(
				cancellationToken);
			if (markets is not { Length: > 0 })
				throw new InvalidDataException(
					"CoinCatch returned no markets for the selected " +
						"product.");
			RegisterMarkets(markets);

			_wsClient = new(
				PublicWebSocketEndpoint,
				ProductType,
				ReConnectionSettings.WorkingTime,
				ReConnectionSettings.ReAttemptCount)
			{
				Parent = this,
			};
			_wsClient.TickerReceived += OnWebSocketTickerAsync;
			_wsClient.OrderBookReceived += OnWebSocketOrderBookAsync;
			_wsClient.TradeReceived += OnWebSocketTradeAsync;
			_wsClient.CandleReceived += OnWebSocketCandleAsync;
			_wsClient.Error += OnWebSocketErrorAsync;
			_wsClient.StateChanged += OnWebSocketStateAsync;
			await _wsClient.ConnectAsync(cancellationToken);

			await SendOutConnectionStateAsync(ConnectionStates.Connected,
				cancellationToken);
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
		await SendOutConnectionStateAsync(ConnectionStates.Disconnecting,
			cancellationToken);
		await DisposeClientsAsync(cancellationToken);
		await SendOutConnectionStateAsync(ConnectionStates.Disconnected,
			cancellationToken);
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
		if (_wsClient is not null &&
			now - _lastWebSocketHeartbeat >= TimeSpan.FromSeconds(25))
		{
			await _wsClient.SendHeartbeatAsync(cancellationToken);
			_lastWebSocketHeartbeat = now;
		}
		if (_restClient?.IsCredentialsAvailable == true &&
			(_portfolioSubscriptionId != 0 ||
				_orderStatusSubscriptionId != 0) &&
			now - _lastPrivatePoll >= PollingInterval)
		{
			await PollPrivateStateAsync(cancellationToken);
			_lastPrivatePoll = now;
		}
	}

	private ValueTask OnWebSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnWebSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Failed)
		{
			await SendOutConnectionStateAsync(ConnectionStates.Failed,
				cancellationToken);
			return;
		}
		if (state == ConnectionStates.Restored)
			await SendOutConnectionStateAsync(ConnectionStates.Restored,
				cancellationToken);
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		var wsClient = _wsClient;
		_wsClient = null;
		if (wsClient is not null)
		{
			UnsubscribeClientEvents(wsClient);
			try
			{
				await wsClient.DisconnectAsync(cancellationToken);
			}
			catch (Exception error)
			{
				if (!cancellationToken.IsCancellationRequested)
					await SendOutErrorAsync(error, cancellationToken);
			}
			finally
			{
				wsClient.Dispose();
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

	private void UnsubscribeClientEvents(CoinCatchWsClient client)
	{
		client.TickerReceived -= OnWebSocketTickerAsync;
		client.OrderBookReceived -= OnWebSocketOrderBookAsync;
		client.TradeReceived -= OnWebSocketTradeAsync;
		client.CandleReceived -= OnWebSocketCandleAsync;
		client.Error -= OnWebSocketErrorAsync;
		client.StateChanged -= OnWebSocketStateAsync;
	}
}
