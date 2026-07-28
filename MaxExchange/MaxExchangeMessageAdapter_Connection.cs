namespace StockSharp.MaxExchange;

public partial class MaxExchangeMessageAdapter
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
		};
		if (configured.Any(static value => value) &&
			!configured.All(static value => value))
			throw new InvalidOperationException(
				"MAX Exchange API key and secret must be " +
					"configured together.");

		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_restClient = new(RestEndpoint, Key, Secret)
			{
				Parent = this,
			};
			_wsClient = new(
				WebSocketEndpoint,
				Key,
				Secret,
				ReConnectionSettings.WorkingTime,
				ReConnectionSettings.ReAttemptCount)
			{
				Parent = this,
			};
			_wsClient.TickerReceived += OnWebSocketTickerAsync;
			_wsClient.OrderBookReceived += OnWebSocketOrderBookAsync;
			_wsClient.TradesReceived += OnWebSocketTradesAsync;
			_wsClient.KlineReceived += OnWebSocketKlineAsync;
			_wsClient.Error += OnWebSocketErrorAsync;
			_wsClient.StateChanged += OnWebSocketStateAsync;
			await _wsClient.ConnectAsync(cancellationToken);

			MaxExchangeSymbol[] markets;
			try
			{
				markets = await RestClient.GetSymbolsAsync(
					cancellationToken);
			}
			catch (HttpRequestException error)
			{
				this.AddWarningLog(
					"MAX Exchange REST market lookup failed ({0}); " +
						"using the public market-status stream.",
					error.Message);
				markets = await WsClient.GetMarketsAsync(
					cancellationToken);
			}
			if (markets is not { Length: > 0 })
				throw new InvalidDataException(
					"MAX Exchange returned no spot markets.");
			RegisterMarkets(markets);

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
		await SendOutConnectionStateAsync(
			ConnectionStates.Disconnecting, cancellationToken);
		await DisposeClientsAsync(cancellationToken);
		await SendOutConnectionStateAsync(
			ConnectionStates.Disconnected, cancellationToken);
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
		if (_restClient?.IsCredentialsAvailable != true ||
			(_portfolioSubscriptionId == 0 &&
				_orderStatusSubscriptionId == 0) ||
			DateTime.UtcNow - _lastPrivatePoll <
				TimeSpan.FromSeconds(10) ||
			!await _privatePollSync.WaitAsync(0, cancellationToken))
			return;
		try
		{
			_lastPrivatePoll = DateTime.UtcNow;
			if (_portfolioSubscriptionId != 0)
				await SendPortfolioSnapshotAsync(
					_portfolioSubscriptionId, cancellationToken);
			if (_orderStatusSubscriptionId != 0)
				await PollOrderUpdatesAsync(
					_orderStatusSubscriptionId, cancellationToken);
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

	private ValueTask OnWebSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnWebSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Failed)
		{
			await SendOutConnectionStateAsync(
				ConnectionStates.Failed, cancellationToken);
			return;
		}
		if (state == ConnectionStates.Restored)
			await SendOutConnectionStateAsync(
				ConnectionStates.Restored, cancellationToken);
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

	private void UnsubscribeClientEvents(MaxExchangeWsClient client)
	{
		client.TickerReceived -= OnWebSocketTickerAsync;
		client.OrderBookReceived -= OnWebSocketOrderBookAsync;
		client.TradesReceived -= OnWebSocketTradesAsync;
		client.KlineReceived -= OnWebSocketKlineAsync;
		client.Error -= OnWebSocketErrorAsync;
		client.StateChanged -= OnWebSocketStateAsync;
	}
}
