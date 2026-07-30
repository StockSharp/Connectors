namespace StockSharp.DeltaExchangeIndia;

public partial class DeltaExchangeIndiaMessageAdapter
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
		if (PrivatePollingInterval <= TimeSpan.Zero)
			throw new InvalidOperationException(
				"Delta Exchange India private polling interval " +
					"must be positive.");
		RestEndpoint = NormalizeEndpoint(
			RestEndpoint, _defaultRestEndpoint, "https");
		PublicWebSocketEndpoint = NormalizeEndpoint(
			PublicWebSocketEndpoint,
			_defaultPublicWebSocketEndpoint,
			"wss");
		PrivateWebSocketEndpoint = NormalizeEndpoint(
			PrivateWebSocketEndpoint,
			_defaultPrivateWebSocketEndpoint,
			"wss");
		ClearState();
		await SendOutConnectionStateAsync(
			ConnectionStates.Connecting, cancellationToken);
		try
		{
			_restClient = new(RestEndpoint, Key, Secret)
			{
				Parent = this,
			};
			var products = await RestClient.GetProductsAsync(
				cancellationToken);
			if (products is not { Length: > 0 })
				throw new InvalidDataException(
					"Delta Exchange India returned no live or " +
						"upcoming products.");
			RegisterProducts(products);

			_publicWsClient = new(
				PublicWebSocketEndpoint,
				null,
				null,
				false,
				ReConnectionSettings.WorkingTime,
				ReConnectionSettings.ReAttemptCount)
			{
				Parent = this,
			};
			SubscribeClientEvents(_publicWsClient);
			await _publicWsClient.ConnectAsync(cancellationToken);

			if (RestClient.IsCredentialsAvailable)
			{
				_privateWsClient = new(
					PrivateWebSocketEndpoint,
					Key,
					Secret,
					true,
					ReConnectionSettings.WorkingTime,
					ReConnectionSettings.ReAttemptCount)
				{
					Parent = this,
				};
				SubscribeClientEvents(_privateWsClient);
				await _privateWsClient.ConnectAsync(
					cancellationToken);
			}
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
		if (_publicWsClient is not null)
			await _publicWsClient.PingAsync(cancellationToken);
		if (_privateWsClient is not null)
			await _privateWsClient.PingAsync(cancellationToken);
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
					false,
					cancellationToken);
			if (_orderStatusSubscriptionId != 0)
				await SendOrderSnapshotAsync(
					_orderStatusSubscriptionId,
					null,
					null,
					null,
					false,
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
		DeltaWsMessage message,
		CancellationToken cancellationToken)
	{
		foreach (var ticker in message?.Tickers ?? [])
			await ProcessTickerAsync(ticker, cancellationToken);

		if (message?.Book is not null)
			await ProcessBookAsync(message.Book, cancellationToken);
		if (message?.Trade is not null)
			await ProcessTradeAsync(message.Trade, cancellationToken);
		if (message?.Candle is not null)
			await ProcessCandleAsync(message.Candle, cancellationToken);
		if (_orderStatusSubscriptionId != 0)
		{
			foreach (var order in message?.Orders ?? [])
				await SendOrderAsync(
					order,
					_orderStatusSubscriptionId,
					false,
					cancellationToken);

			if (message?.Fill is not null)
				await SendFillAsync(
					message.Fill,
					_orderStatusSubscriptionId,
					cancellationToken);
		}
		if (_portfolioSubscriptionId != 0)
			foreach (var position in message?.Positions ?? [])
				await SendPositionAsync(
					position,
					_portfolioSubscriptionId,
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

	private void SubscribeClientEvents(
		DeltaExchangeIndiaWsClient client)
	{
		client.MessageReceived += OnWebSocketMessageAsync;
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnWebSocketStateAsync;
	}

	private void UnsubscribeClientEvents(
		DeltaExchangeIndiaWsClient client)
	{
		client.MessageReceived -= OnWebSocketMessageAsync;
		client.Error -= OnWebSocketErrorAsync;
		client.StateChanged -= OnWebSocketStateAsync;
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		foreach (var client in new[]
		{
			_privateWsClient,
			_publicWsClient,
		})
		{
			if (client is null)
				continue;
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

		_privateWsClient = null;
		_publicWsClient = null;
		_restClient?.Dispose();
		_restClient = null;
		ClearState();
	}

	private void DisposeClients()
	{
		if (_privateWsClient is not null)
			UnsubscribeClientEvents(_privateWsClient);
		if (_publicWsClient is not null)
			UnsubscribeClientEvents(_publicWsClient);
		_privateWsClient?.Dispose();
		_privateWsClient = null;
		_publicWsClient?.Dispose();
		_publicWsClient = null;
		_restClient?.Dispose();
		_restClient = null;
		ClearState();
	}
}
