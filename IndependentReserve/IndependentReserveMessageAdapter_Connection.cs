namespace StockSharp.IndependentReserve;

public partial class IndependentReserveMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null || _socketClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		if (Key.IsEmpty() != Secret.IsEmpty())
			throw new InvalidOperationException(
				"Independent Reserve API key and secret must be configured together.");

		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_restClient = new(RestEndpoint, Key, Secret)
			{
				Parent = this,
			};
			var primaryCurrencies = await RestClient.GetCurrenciesAsync(
				cancellationToken);
			var secondaryCurrencies = await RestClient.GetSecondaryCurrenciesAsync(
				cancellationToken);
			RegisterMarkets(primaryCurrencies, secondaryCurrencies);
			using (_sync.EnterScope())
				if (_markets.Count == 0)
					throw new InvalidDataException(
						"Independent Reserve returned no tradeable markets.");

			_socketClient = new(WebSocketEndpoint,
				ReConnectionSettings.WorkingTime,
				ReConnectionSettings.ReAttemptCount)
			{
				Parent = this,
			};
			_socketClient.MessageReceived += OnSocketMessageAsync;
			_socketClient.Error += OnSocketErrorAsync;
			_socketClient.StateChanged += OnSocketStateAsync;
			await _socketClient.ConnectAsync(cancellationToken);

			if (RestClient.IsCredentialsAvailable)
			{
				string[] accountChannels;
				using (_sync.EnterScope())
				{
					foreach (var primary in _primaryMarkets.Keys)
					{
						_accountChannels.Add(GetTickerChannel(primary));
						_accountChannels.Add(GetOrderBookChannel(primary));
					}
					accountChannels = [.. _accountChannels];
				}
				await SocketClient.SubscribeAsync(accountChannels,
					cancellationToken);
				StartPrivatePolling();
			}

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
	protected override async ValueTask DisconnectAsync(
		DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
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

	private async ValueTask OnSocketMessageAsync(
		IndependentReserveSocketEnvelope envelope,
		CancellationToken cancellationToken)
	{
		if (envelope is null)
			return;
		switch (envelope.Event)
		{
			case IndependentReserveSocketEvents.NewOrder:
			case IndependentReserveSocketEvents.OrderChanged:
			case IndependentReserveSocketEvents.OrderCanceled:
				await OnBookEventAsync(envelope, cancellationToken);
				break;
			case IndependentReserveSocketEvents.Trade:
				await OnTradeEventAsync(envelope, cancellationToken);
				break;
		}
		if (RestClient.IsCredentialsAvailable)
			await OnOwnSocketEventAsync(envelope, cancellationToken);
	}

	private ValueTask OnSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Reconnecting)
			MarkBooksUninitialized();
		if (state == ConnectionStates.Failed)
		{
			await SendOutConnectionStateAsync(ConnectionStates.Failed,
				cancellationToken);
			return;
		}
		if (state != ConnectionStates.Restored)
			return;

		await RefreshBooksAsync(cancellationToken);
		if (RestClient.IsCredentialsAvailable)
			await RefreshPrivateSnapshotsAsync(cancellationToken);
		await SendOutConnectionStateAsync(ConnectionStates.Restored,
			cancellationToken);
	}

	private void StartPrivatePolling()
	{
		_pollingCancellation = new();
		_pollingTask = RunPrivatePollingAsync(_pollingCancellation.Token);
	}

	private async Task RunPrivatePollingAsync(
		CancellationToken cancellationToken)
	{
		try
		{
			while (true)
			{
				await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
				try
				{
					await RefreshPrivateSnapshotsAsync(cancellationToken);
				}
				catch (Exception error) when (
					!cancellationToken.IsCancellationRequested)
				{
					await SendOutErrorAsync(error, cancellationToken);
				}
			}
		}
		catch (OperationCanceledException) when (
			cancellationToken.IsCancellationRequested)
		{
		}
	}

	private async ValueTask StopPrivatePollingAsync()
	{
		var cancellation = _pollingCancellation;
		_pollingCancellation = null;
		var task = _pollingTask;
		_pollingTask = null;
		if (cancellation is null)
			return;
		cancellation.Cancel();
		try
		{
			if (task is not null)
				await task;
		}
		finally
		{
			cancellation.Dispose();
		}
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		await StopPrivatePollingAsync();
		var socket = _socketClient;
		_socketClient = null;
		var rest = _restClient;
		_restClient = null;
		if (socket is not null)
		{
			try
			{
				await socket.DisconnectAsync(cancellationToken);
			}
			catch (Exception error)
			{
				if (!cancellationToken.IsCancellationRequested)
					await SendOutErrorAsync(error, cancellationToken);
			}
			finally
			{
				socket.Dispose();
			}
		}
		rest?.Dispose();
		ClearState();
	}

	private void DisposeClients()
	{
		_pollingCancellation?.Cancel();
		_pollingCancellation?.Dispose();
		_pollingCancellation = null;
		_pollingTask = null;
		_socketClient?.Dispose();
		_socketClient = null;
		_restClient?.Dispose();
		_restClient = null;
		ClearState();
	}
}
