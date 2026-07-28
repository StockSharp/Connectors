namespace StockSharp.Coincall;

public partial class CoincallMessageAdapter
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
		if (RequestValidityWindow <= TimeSpan.Zero)
			throw new InvalidOperationException(
				"Coincall request validity window must be positive.");
		if (PrivatePollingInterval <= TimeSpan.Zero)
			throw new InvalidOperationException(
				"Coincall private polling interval must be positive.");
		RestEndpoint = NormalizeEndpoint(
			RestEndpoint, _defaultRestEndpoint, "https");
		OptionsWebSocketEndpoint = NormalizeEndpoint(
			OptionsWebSocketEndpoint,
			_defaultOptionsWebSocketEndpoint,
			"wss");
		FuturesWebSocketEndpoint = NormalizeEndpoint(
			FuturesWebSocketEndpoint,
			_defaultFuturesWebSocketEndpoint,
			"wss");
		ClearState();
		await SendOutConnectionStateAsync(
			ConnectionStates.Connecting, cancellationToken);
		try
		{
			_restClient = new(
				RestEndpoint,
				ProductType,
				Key,
				Secret,
				RequestValidityWindow)
			{
				Parent = this,
			};
			var instruments = await RestClient.GetInstrumentsAsync(
				cancellationToken);
			if (instruments is not { Length: > 0 })
				throw new InvalidDataException(
					"Coincall returned no instruments.");
			RegisterInstruments(instruments);

			if (RestClient.IsCredentialsAvailable)
			{
				var endpoint =
					ProductType == CoincallProductTypes.Options
						? OptionsWebSocketEndpoint
						: FuturesWebSocketEndpoint;
				_wsClient = new(
					endpoint,
					Key,
					Secret,
					ProductType,
					ReConnectionSettings.WorkingTime,
					ReConnectionSettings.ReAttemptCount)
				{
					Parent = this,
				};
				_wsClient.MessageReceived +=
					OnWebSocketMessageAsync;
				_wsClient.Error += OnWebSocketErrorAsync;
				_wsClient.StateChanged +=
					OnWebSocketStateAsync;
				await _wsClient.ConnectAsync(cancellationToken);
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
		if (_wsClient is not null &&
			CurrentTime - _lastHeartbeat >=
				TimeSpan.FromSeconds(3))
		{
			_lastHeartbeat = CurrentTime;
			await _wsClient.SendHeartbeatAsync(cancellationToken);
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
				await SendOrderSnapshotAsync(
					_orderStatusSubscriptionId,
					null,
					null,
					100,
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
		CoincallWsMessage message,
		CancellationToken cancellationToken)
	{
		foreach (var ticker in message?.Tickers ?? [])
			await ProcessTickerAsync(ticker, cancellationToken);
		if (message?.Book is not null)
			await ProcessBookAsync(message.Book, cancellationToken);
		foreach (var trade in message?.Trades ?? [])
			await ProcessTradeAsync(trade, cancellationToken);
		if (message?.Candle is not null)
			await ProcessCandleAsync(message.Candle, cancellationToken);
		if (_orderStatusSubscriptionId != 0)
		{
			foreach (var order in message?.Orders ?? [])
				await SendOrderAsync(
					order,
					_orderStatusSubscriptionId,
					cancellationToken);
			foreach (var fill in message?.Fills ?? [])
				await SendFillAsync(
					fill,
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

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		if (_wsClient is not null)
		{
			_wsClient.MessageReceived -=
				OnWebSocketMessageAsync;
			_wsClient.Error -= OnWebSocketErrorAsync;
			_wsClient.StateChanged -=
				OnWebSocketStateAsync;
			try
			{
				await _wsClient.DisconnectAsync(
					cancellationToken);
			}
			catch (Exception error)
			{
				if (!cancellationToken.IsCancellationRequested)
					await SendOutErrorAsync(
						error, cancellationToken);
			}
			finally
			{
				_wsClient.Dispose();
				_wsClient = null;
			}
		}
		_restClient?.Dispose();
		_restClient = null;
		ClearState();
	}

	private void DisposeClients()
	{
		if (_wsClient is not null)
		{
			_wsClient.MessageReceived -=
				OnWebSocketMessageAsync;
			_wsClient.Error -= OnWebSocketErrorAsync;
			_wsClient.StateChanged -=
				OnWebSocketStateAsync;
			_wsClient.Dispose();
			_wsClient = null;
		}
		_restClient?.Dispose();
		_restClient = null;
		ClearState();
	}
}
