namespace StockSharp.BTSE;

public partial class BTSEMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_spotRestClient is not null || _futuresRestClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting, cancellationToken);
		try
		{
			if (IsSectionEnabled(BTSESections.Spot))
			{
				_spotRestClient = new(SpotRestEndpoint, BTSESections.Spot, Key, Secret)
				{
					Parent = this,
				};
				RegisterMarkets(BTSESections.Spot,
					await _spotRestClient.GetMarketsAsync(null, cancellationToken));
				_spotWsClient = CreateWebSocketClient(SpotWebSocketEndpoint,
					BTSESections.Spot, false, _spotRestClient);
				_spotBookWsClient = CreateWebSocketClient(SpotOrderBookWebSocketEndpoint,
					BTSESections.Spot, true, _spotRestClient);
			}

			if (IsSectionEnabled(BTSESections.Futures))
			{
				_futuresRestClient = new(FuturesRestEndpoint, BTSESections.Futures, Key, Secret)
				{
					Parent = this,
				};
				RegisterMarkets(BTSESections.Futures,
					await _futuresRestClient.GetMarketsAsync(null, cancellationToken));
				_futuresWsClient = CreateWebSocketClient(FuturesWebSocketEndpoint,
					BTSESections.Futures, false, _futuresRestClient);
				_futuresBookWsClient = CreateWebSocketClient(
					FuturesOrderBookWebSocketEndpoint, BTSESections.Futures, true,
					_futuresRestClient);
			}

			await ConnectWebSocketsAsync(cancellationToken);
			await SendOutConnectionStateAsync(ConnectionStates.Connected, cancellationToken);
		}
		catch
		{
			await DisposeClientsAsync(cancellationToken);
			await SendOutConnectionStateAsync(ConnectionStates.Disconnected, cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		_ = disconnectMsg;
		EnsureConnected();
		await SendOutConnectionStateAsync(ConnectionStates.Disconnecting, cancellationToken);
		await DisposeClientsAsync(cancellationToken);
		await SendOutConnectionStateAsync(ConnectionStates.Disconnected, cancellationToken);
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
		if (_spotWsClient is not null)
			await _spotWsClient.PingAsync(cancellationToken);
		if (_spotBookWsClient is not null)
			await _spotBookWsClient.PingAsync(cancellationToken);
		if (_futuresWsClient is not null)
			await _futuresWsClient.PingAsync(cancellationToken);
		if (_futuresBookWsClient is not null)
			await _futuresBookWsClient.PingAsync(cancellationToken);
	}

	private BTSEWsClient CreateWebSocketClient(string endpoint, BTSESections section,
		bool isOrderBook, BTSERestClient restClient)
	{
		var client = new BTSEWsClient(endpoint, section, isOrderBook, restClient,
			ReConnectionSettings.WorkingTime, ReConnectionSettings.ReAttemptCount)
		{
			Parent = this,
		};
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnWebSocketStateAsync;
		if (isOrderBook)
			client.BookReceived += OnBookAsync;
		else
		{
			client.TradeReceived += OnTradeAsync;
			client.OrderReceived += OnOrderUpdateAsync;
			client.FillReceived += OnFillUpdateAsync;
			if (section == BTSESections.Futures)
				client.PositionReceived += OnPositionUpdateAsync;
		}
		return client;
	}

	private async ValueTask ConnectWebSocketsAsync(CancellationToken cancellationToken)
	{
		if (_spotWsClient is not null)
			await _spotWsClient.ConnectAsync(cancellationToken);
		if (_spotBookWsClient is not null)
			await _spotBookWsClient.ConnectAsync(cancellationToken);
		if (_futuresWsClient is not null)
			await _futuresWsClient.ConnectAsync(cancellationToken);
		if (_futuresBookWsClient is not null)
			await _futuresBookWsClient.ConnectAsync(cancellationToken);
	}

	private async ValueTask DisposeClientsAsync(CancellationToken cancellationToken)
	{
		await DisposeWebSocketAsync(_spotWsClient, cancellationToken);
		_spotWsClient = null;
		await DisposeWebSocketAsync(_spotBookWsClient, cancellationToken);
		_spotBookWsClient = null;
		await DisposeWebSocketAsync(_futuresWsClient, cancellationToken);
		_futuresWsClient = null;
		await DisposeWebSocketAsync(_futuresBookWsClient, cancellationToken);
		_futuresBookWsClient = null;

		_spotRestClient?.Dispose();
		_spotRestClient = null;
		_futuresRestClient?.Dispose();
		_futuresRestClient = null;
		ClearState();
	}

	private async ValueTask DisposeWebSocketAsync(BTSEWsClient client,
		CancellationToken cancellationToken)
	{
		if (client is null)
			return;
		try
		{
			await client.DisconnectAsync(cancellationToken);
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested)
		{
			await SendOutErrorAsync(error, cancellationToken);
		}
		client.Dispose();
	}

	private ValueTask OnWebSocketErrorAsync(BTSEWsClient client, Exception error,
		CancellationToken cancellationToken)
	{
		_ = client;
		return SendOutErrorAsync(error, cancellationToken);
	}

	private async ValueTask OnWebSocketStateAsync(BTSEWsClient client,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		bool notifyFailed = false;
		bool notifyRestored = false;
		using (_sync.EnterScope())
		{
			if (state == ConnectionStates.Failed)
			{
				if (_failedWsClients.Add(client) && _failedWsClients.Count == 1)
					notifyFailed = true;
			}
			else if (state == ConnectionStates.Restored &&
				_failedWsClients.Remove(client) && _failedWsClients.Count == 0)
				notifyRestored = true;
		}

		if (notifyFailed)
		{
			await SendOutConnectionStateAsync(ConnectionStates.Failed, cancellationToken);
			return;
		}
		if (!notifyRestored)
			return;

		if (_portfolioSubscriptionId != 0 && client.Section is BTSESections.Spot or
			BTSESections.Futures)
			await SendPortfolioSnapshotAsync(_portfolioSubscriptionId, cancellationToken);
		if (_orderStatusSubscriptionId != 0)
			await SendOrderSnapshotAsync(_orderStatusSubscriptionId, null, null, null,
				1000, cancellationToken);
		await SendOutConnectionStateAsync(ConnectionStates.Restored, cancellationToken);
	}
}
