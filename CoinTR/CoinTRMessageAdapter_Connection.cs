namespace StockSharp.CoinTR;

public partial class CoinTRMessageAdapter
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
				"CoinTR API key, secret and passphrase must be " +
					"configured together.");

		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_restClient = new(RestEndpoint, Key, Secret, Passphrase)
			{
				Parent = this,
			};
			var markets = await RestClient.GetSymbolsAsync(
				null, cancellationToken);
			if (markets is not { Length: > 0 })
				throw new InvalidDataException(
					"CoinTR returned no spot markets.");
			RegisterMarkets(markets);

			_wsClient = new(
				PublicWebSocketEndpoint,
				PrivateWebSocketEndpoint,
				Key,
				Secret,
				Passphrase,
				ReConnectionSettings.WorkingTime,
				ReConnectionSettings.ReAttemptCount)
			{
				Parent = this,
			};
			_wsClient.TickerReceived += OnWebSocketTickerAsync;
			_wsClient.OrderBookReceived += OnWebSocketOrderBookAsync;
			_wsClient.TradeReceived += OnWebSocketTradeAsync;
			_wsClient.CandleReceived += OnWebSocketCandleAsync;
			_wsClient.BalancesReceived += OnWebSocketBalancesAsync;
			_wsClient.OrdersReceived += OnWebSocketOrdersAsync;
			_wsClient.FillsReceived += OnWebSocketFillsAsync;
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
		if (_wsClient is not null)
			await _wsClient.SendHeartbeatAsync(cancellationToken);
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

	private void UnsubscribeClientEvents(CoinTRWsClient client)
	{
		client.TickerReceived -= OnWebSocketTickerAsync;
		client.OrderBookReceived -= OnWebSocketOrderBookAsync;
		client.TradeReceived -= OnWebSocketTradeAsync;
		client.CandleReceived -= OnWebSocketCandleAsync;
		client.BalancesReceived -= OnWebSocketBalancesAsync;
		client.OrdersReceived -= OnWebSocketOrdersAsync;
		client.FillsReceived -= OnWebSocketFillsAsync;
		client.Error -= OnWebSocketErrorAsync;
		client.StateChanged -= OnWebSocketStateAsync;
	}
}
