namespace StockSharp.AltCoinTrader;

public partial class AltCoinTraderMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(
		ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null ||
			_publicWsClient is not null ||
			_privateWsClient is not null)
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
				"AltCoinTrader API key and secret must be " +
					"configured together.");

		ClearState();
		await SendOutConnectionStateAsync(
			ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_restClient = new(
				RestEndpoint,
				Key,
				Secret)
			{
				Parent = this,
			};
			var markets = await RestClient.GetMarketsAsync(
				cancellationToken);
			if (markets is not { Length: > 0 })
				throw new InvalidDataException(
					"AltCoinTrader returned no spot markets.");
			RegisterMarkets(markets);

			_publicWsClient = new(
				WebSocketEndpoint,
				false,
				null,
				null,
				ReConnectionSettings.WorkingTime,
				ReConnectionSettings.ReAttemptCount)
			{
				Parent = this,
			};
			SubscribePublicClientEvents(_publicWsClient);
			await _publicWsClient.ConnectAsync(cancellationToken);

			if (RestClient.IsCredentialsAvailable)
			{
				_privateWsClient = new(
					WebSocketEndpoint,
					true,
					Key,
					Secret,
					ReConnectionSettings.WorkingTime,
					ReConnectionSettings.ReAttemptCount)
				{
					Parent = this,
				};
				SubscribePrivateClientEvents(_privateWsClient);
				await _privateWsClient.ConnectAsync(
					cancellationToken);
				await _privateWsClient.SubscribePrivateAsync(
					"orders", cancellationToken);
				await _privateWsClient.SubscribePrivateAsync(
					"fills", cancellationToken);
				await _privateWsClient.SubscribePrivateAsync(
					"balances", cancellationToken);
			}

			await SendOutConnectionStateAsync(
				ConnectionStates.Connected,
				cancellationToken);
		}
		catch
		{
			await DisposeClientsAsync(cancellationToken);
			await SendOutConnectionStateAsync(
				ConnectionStates.Disconnected,
				cancellationToken);
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
			ConnectionStates.Disconnecting,
			cancellationToken);
		await DisposeClientsAsync(cancellationToken);
		await SendOutConnectionStateAsync(
			ConnectionStates.Disconnected,
			cancellationToken);
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
			DateTime.UtcNow - _lastPrivatePoll <
				TimeSpan.FromSeconds(30) ||
			!await _privatePollSync.WaitAsync(
				0, cancellationToken))
			return;

		try
		{
			_lastPrivatePoll = DateTime.UtcNow;
			if (_portfolioSubscriptionId != 0)
				await SendPortfolioSnapshotAsync(
					_portfolioSubscriptionId,
					cancellationToken);
			if (_orderStatusSubscriptionId != 0)
				await PollPrivateStateAsync(
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
			_privatePollSync.Release();
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
		{
			await SendOutConnectionStateAsync(
				ConnectionStates.Failed,
				cancellationToken);
			return;
		}
		if (state == ConnectionStates.Restored)
			await SendOutConnectionStateAsync(
				ConnectionStates.Restored,
				cancellationToken);
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		var privateClient = _privateWsClient;
		_privateWsClient = null;
		if (privateClient is not null)
		{
			UnsubscribePrivateClientEvents(privateClient);
			await DisconnectClientAsync(
				privateClient, cancellationToken);
		}

		var publicClient = _publicWsClient;
		_publicWsClient = null;
		if (publicClient is not null)
		{
			UnsubscribePublicClientEvents(publicClient);
			await DisconnectClientAsync(
				publicClient, cancellationToken);
		}

		_restClient?.Dispose();
		_restClient = null;
		ClearState();
	}

	private void DisposeClients()
	{
		if (_privateWsClient is not null)
			UnsubscribePrivateClientEvents(_privateWsClient);
		_privateWsClient?.Dispose();
		_privateWsClient = null;

		if (_publicWsClient is not null)
			UnsubscribePublicClientEvents(_publicWsClient);
		_publicWsClient?.Dispose();
		_publicWsClient = null;

		_restClient?.Dispose();
		_restClient = null;
		ClearState();
	}

	private async ValueTask DisconnectClientAsync(
		AltCoinTraderWsClient client,
		CancellationToken cancellationToken)
	{
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

	private void SubscribePublicClientEvents(
		AltCoinTraderWsClient client)
	{
		client.TickerReceived += OnWebSocketTickerAsync;
		client.OrderBookReceived += OnWebSocketOrderBookAsync;
		client.TradesReceived += OnWebSocketTradesAsync;
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnWebSocketStateAsync;
	}

	private void UnsubscribePublicClientEvents(
		AltCoinTraderWsClient client)
	{
		client.TickerReceived -= OnWebSocketTickerAsync;
		client.OrderBookReceived -= OnWebSocketOrderBookAsync;
		client.TradesReceived -= OnWebSocketTradesAsync;
		client.Error -= OnWebSocketErrorAsync;
		client.StateChanged -= OnWebSocketStateAsync;
	}

	private void SubscribePrivateClientEvents(
		AltCoinTraderWsClient client)
	{
		client.OrderReceived += OnPrivateOrderAsync;
		client.FillReceived += OnPrivateFillAsync;
		client.BalancesReceived += OnPrivateBalancesAsync;
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnWebSocketStateAsync;
	}

	private void UnsubscribePrivateClientEvents(
		AltCoinTraderWsClient client)
	{
		client.OrderReceived -= OnPrivateOrderAsync;
		client.FillReceived -= OnPrivateFillAsync;
		client.BalancesReceived -= OnPrivateBalancesAsync;
		client.Error -= OnWebSocketErrorAsync;
		client.StateChanged -= OnWebSocketStateAsync;
	}
}
