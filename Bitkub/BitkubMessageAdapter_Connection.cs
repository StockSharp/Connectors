namespace StockSharp.Bitkub;

public partial class BitkubMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null || _publicWebSocketClient is not null ||
			_privateWebSocketClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Key.IsEmpty() != Secret.IsEmpty())
			throw new InvalidOperationException(
				"Bitkub API key and secret must be configured together.");

		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_restClient = new(RestEndpoint, Key, Secret) { Parent = this };
			await RestClient.SynchronizeTimeAsync(cancellationToken);
			var symbols = await RestClient.GetSymbolsAsync(cancellationToken);
			if (symbols.Length == 0)
				throw new InvalidDataException(
					"Bitkub returned no available symbols.");
			RegisterMarkets(symbols);

			_publicWebSocketClient = new(PublicWebSocketEndpoint,
				ReConnectionSettings.WorkingTime, ReConnectionSettings.ReAttemptCount)
			{
				Parent = this,
			};
			_publicWebSocketClient.TradesChanged += OnPublicTradesChangedAsync;
			_publicWebSocketClient.DepthChanged += OnPublicDepthChangedAsync;
			_publicWebSocketClient.TickerChanged += OnPublicTickerChangedAsync;
			_publicWebSocketClient.Error += OnWebSocketErrorAsync;

			if (RestClient.IsCredentialsAvailable)
			{
				_privateWebSocketClient = new(PrivateWebSocketEndpoint,
					ReConnectionSettings.WorkingTime,
					ReConnectionSettings.ReAttemptCount,
					RestClient.CreateWebSocketAuthentication)
				{
					Parent = this,
				};
				_privateWebSocketClient.OrderUpdated += OnPrivateOrderUpdatedAsync;
				_privateWebSocketClient.MatchUpdated += OnPrivateMatchUpdatedAsync;
				_privateWebSocketClient.Error += OnWebSocketErrorAsync;
				_privateWebSocketClient.StateChanged += OnPrivateStateChangedAsync;
				await _privateWebSocketClient.ConnectAsync(cancellationToken);
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

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		_ = timeMsg;
		if (_privateWebSocketClient is not null)
			await _privateWebSocketClient.ProcessTimeAsync(cancellationToken);
	}

	private ValueTask OnWebSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnPrivateStateChangedAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state is ConnectionStates.Failed or ConnectionStates.Restored)
			await SendOutConnectionStateAsync(state, cancellationToken);
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		var privateClient = _privateWebSocketClient;
		_privateWebSocketClient = null;
		if (privateClient is not null)
		{
			try
			{
				await privateClient.DisconnectAsync(cancellationToken);
			}
			catch (Exception error)
			{
				if (!cancellationToken.IsCancellationRequested)
					await SendOutErrorAsync(error, cancellationToken);
			}
			finally
			{
				privateClient.Dispose();
			}
		}

		var publicClient = _publicWebSocketClient;
		_publicWebSocketClient = null;
		if (publicClient is not null)
		{
			try
			{
				await publicClient.DisconnectAsync(cancellationToken);
			}
			catch (Exception error)
			{
				if (!cancellationToken.IsCancellationRequested)
					await SendOutErrorAsync(error, cancellationToken);
			}
			finally
			{
				publicClient.Dispose();
			}
		}

		_restClient?.Dispose();
		_restClient = null;
		ClearState();
	}

	private void DisposeClients()
	{
		_privateWebSocketClient?.Dispose();
		_privateWebSocketClient = null;
		_publicWebSocketClient?.Dispose();
		_publicWebSocketClient = null;
		_restClient?.Dispose();
		_restClient = null;
		ClearState();
	}
}
