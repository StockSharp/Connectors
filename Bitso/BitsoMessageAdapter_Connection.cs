namespace StockSharp.Bitso;

public partial class BitsoMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null || _wsClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Key.IsEmpty() != Secret.IsEmpty())
			throw new InvalidOperationException(
				"Bitso API key and secret must be configured together.");

		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_restClient = new(GetRestEndpoint(), Key, Secret) { Parent = this };
			var books = await RestClient.GetBooksAsync(cancellationToken);
			if (books is not { Length: > 0 })
				throw new InvalidDataException("Bitso returned no available books.");
			RegisterMarkets(books);

			_wsClient = new(WebSocketEndpoint, ReConnectionSettings.WorkingTime,
				ReConnectionSettings.ReAttemptCount)
			{
				Parent = this,
			};
			_wsClient.TradeReceived += OnWebSocketTradeAsync;
			_wsClient.OrdersReceived += OnWebSocketOrdersAsync;
			_wsClient.Error += OnWebSocketErrorAsync;
			_wsClient.StateChanged += OnWebSocketStateAsync;
			await _wsClient.ConnectAsync(cancellationToken);

			await SendOutConnectionStateAsync(ConnectionStates.Connected,
				cancellationToken);
		}
		catch
		{
			await DisposeClientsAsync(cancellationToken);
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
		if (_restClient?.IsCredentialsAvailable != true ||
			(_portfolioSubscriptionId == 0 && _orderStatusSubscriptionId == 0) ||
			DateTime.UtcNow - _lastPrivatePoll < TimeSpan.FromSeconds(10) ||
			!await _privatePollSync.WaitAsync(0, cancellationToken))
			return;
		try
		{
			_lastPrivatePoll = DateTime.UtcNow;
			if (_portfolioSubscriptionId != 0)
				await SendPortfolioSnapshotAsync(_portfolioSubscriptionId,
					cancellationToken);
			if (_orderStatusSubscriptionId != 0)
				await PollOrderUpdatesAsync(_orderStatusSubscriptionId,
					cancellationToken);
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested)
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
			await SendOutConnectionStateAsync(ConnectionStates.Failed,
				cancellationToken);
			return;
		}
		if (state != ConnectionStates.Restored)
			return;

		if (_portfolioSubscriptionId != 0 && RestClient.IsCredentialsAvailable)
			await SendPortfolioSnapshotAsync(_portfolioSubscriptionId,
				cancellationToken);
		if (_orderStatusSubscriptionId != 0 && RestClient.IsCredentialsAvailable)
			await PollOrderUpdatesAsync(_orderStatusSubscriptionId,
				cancellationToken);
		await SendOutConnectionStateAsync(ConnectionStates.Restored,
			cancellationToken);
	}

	private async ValueTask DisposeClientsAsync(CancellationToken cancellationToken)
	{
		var wsClient = _wsClient;
		_wsClient = null;
		if (wsClient is not null)
		{
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
		_wsClient?.Dispose();
		_wsClient = null;
		_restClient?.Dispose();
		_restClient = null;
		ClearState();
	}
}
