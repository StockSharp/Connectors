namespace StockSharp.HashKey;

public partial class HashKeyMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null || _publicWsClient is not null ||
			_privateWsClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Key.IsEmpty() != Secret.IsEmpty())
			throw new InvalidOperationException(
				"HashKey API key and secret must be configured together.");

		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_restClient = new(GetRestEndpoint(), Key, Secret) { Parent = this };
			_ = await RestClient.GetTimeAsync(cancellationToken);
			RegisterMarkets(await RestClient.GetExchangeInfoAsync(new(),
				cancellationToken));

			_publicWsClient = CreatePublicWebSocketClient();
			await _publicWsClient.ConnectAsync(cancellationToken);

			if (RestClient.IsCredentialsAvailable)
			{
				var response = await RestClient.CreateListenKeyAsync(cancellationToken);
				_listenKey = response?.ListenKey.ThrowIfEmpty(nameof(response.ListenKey));
				_lastListenKeyRenewal = DateTime.UtcNow;
				_privateWsClient = CreatePrivateWebSocketClient(_listenKey);
				await _privateWsClient.ConnectAsync(cancellationToken);
			}

			await SendOutConnectionStateAsync(ConnectionStates.Connected,
				cancellationToken);
		}
		catch
		{
			await DisposeClientsAsync(false, cancellationToken);
			await SendOutConnectionStateAsync(ConnectionStates.Disconnected,
				cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		_ = disconnectMsg;
		EnsureConnected();
		await SendOutConnectionStateAsync(ConnectionStates.Disconnecting,
			cancellationToken);
		await DisposeClientsAsync(true, cancellationToken);
		await SendOutConnectionStateAsync(ConnectionStates.Disconnected,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		await DisposeClientsAsync(false, cancellationToken);
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		_ = timeMsg;
		if (_publicWsClient is not null)
			await _publicWsClient.PingAsync(cancellationToken);
		if (_privateWsClient is not null && !_listenKey.IsEmpty() &&
			DateTime.UtcNow - _lastListenKeyRenewal >= TimeSpan.FromMinutes(25))
		{
			_ = await RestClient.KeepListenKeyAsync(_listenKey, cancellationToken);
			_lastListenKeyRenewal = DateTime.UtcNow;
		}
	}

	private HashKeyWsClient CreatePublicWebSocketClient()
	{
		var client = new HashKeyWsClient(GetPublicWebSocketEndpoint(), false,
			ReConnectionSettings.WorkingTime, ReConnectionSettings.ReAttemptCount)
		{
			Parent = this,
		};
		client.BookTickerReceived += OnBookTickerAsync;
		client.RealtimeReceived += OnRealtimeAsync;
		client.DepthReceived += OnDepthAsync;
		client.TradeReceived += OnTradeAsync;
		client.KlineReceived += OnKlineAsync;
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnWebSocketStateAsync;
		return client;
	}

	private HashKeyWsClient CreatePrivateWebSocketClient(string listenKey)
	{
		var endpoint = GetPrivateWebSocketEndpoint().TrimEnd('/') + "/" +
			Uri.EscapeDataString(listenKey);
		var client = new HashKeyWsClient(endpoint, true,
			ReConnectionSettings.WorkingTime, ReConnectionSettings.ReAttemptCount)
		{
			Parent = this,
		};
		client.AccountReceived += OnAccountUpdateAsync;
		client.OrderReceived += OnOrderUpdateAsync;
		client.TicketReceived += OnTicketAsync;
		client.PositionReceived += OnPositionUpdateAsync;
		client.Error += OnWebSocketErrorAsync;
		client.StateChanged += OnWebSocketStateAsync;
		return client;
	}

	private ValueTask OnWebSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnWebSocketStateAsync(HashKeyWsClient client,
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
			await SendOutConnectionStateAsync(ConnectionStates.Failed,
				cancellationToken);
			return;
		}
		if (!notifyRestored)
			return;

		if (_portfolioSubscriptionId != 0 && RestClient.IsCredentialsAvailable)
			await SendPortfolioSnapshotAsync(_portfolioSubscriptionId,
				cancellationToken);
		if (_orderStatusSubscriptionId != 0 && RestClient.IsCredentialsAvailable)
			await SendOrderSnapshotAsync(_orderStatusSubscriptionId, null, null, null,
				1000, cancellationToken);
		await SendOutConnectionStateAsync(ConnectionStates.Restored,
			cancellationToken);
	}

	private async ValueTask DisposeClientsAsync(bool deleteListenKey,
		CancellationToken cancellationToken)
	{
		await DisposeWebSocketAsync(_privateWsClient, cancellationToken);
		_privateWsClient = null;
		await DisposeWebSocketAsync(_publicWsClient, cancellationToken);
		_publicWsClient = null;

		if (deleteListenKey && _restClient is not null && !_listenKey.IsEmpty())
		{
			try
			{
				_ = await _restClient.DeleteListenKeyAsync(_listenKey,
					cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
		}
		_listenKey = null;
		_lastListenKeyRenewal = default;
		_restClient?.Dispose();
		_restClient = null;
		ClearState();
	}

	private async ValueTask DisposeWebSocketAsync(HashKeyWsClient client,
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
		finally
		{
			client.Dispose();
		}
	}

	private void DisposeClients()
	{
		_privateWsClient?.Dispose();
		_privateWsClient = null;
		_publicWsClient?.Dispose();
		_publicWsClient = null;
		_restClient?.Dispose();
		_restClient = null;
		_listenKey = null;
		_lastListenKeyRenewal = default;
		ClearState();
	}
}
