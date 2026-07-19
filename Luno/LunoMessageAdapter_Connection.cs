namespace StockSharp.Luno;

public partial class LunoMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;
		if (_restClient is not null || _userSocketClient is not null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		if (Key.IsEmpty() != Secret.IsEmpty())
			throw new InvalidOperationException(
				"Luno API key and secret must be configured together.");

		ClearState();
		await SendOutConnectionStateAsync(ConnectionStates.Connecting,
			cancellationToken);
		try
		{
			_restClient = new(RestEndpoint, Key, Secret)
			{
				Parent = this,
			};
			var markets = await RestClient.GetMarketsAsync(cancellationToken);
			RegisterMarkets(markets);
			using (_sync.EnterScope())
				if (_markets.Count == 0)
					throw new InvalidDataException(
						"Luno returned no markets.");

			if (RestClient.IsCredentialsAvailable)
			{
				_userSocketClient = new(WebSocketEndpoint, Key, Secret,
					ReConnectionSettings.WorkingTime,
					ReConnectionSettings.ReAttemptCount)
				{
					Parent = this,
				};
				_userSocketClient.UpdateReceived += OnUserStreamUpdateAsync;
				_userSocketClient.Error += OnSocketErrorAsync;
				_userSocketClient.StateChanged += OnUserSocketStateAsync;
				await _userSocketClient.ConnectAsync(cancellationToken);
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

	private async ValueTask AcquireMarketStreamAsync(string symbol,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		symbol = symbol.NormalizeSymbol();
		await _marketStreamGate.WaitAsync(cancellationToken);
		try
		{
			using (_sync.EnterScope())
			{
				if (_marketStreams.TryGetValue(symbol, out var existing))
				{
					existing.References++;
					return;
				}
			}

			var client = new LunoMarketSocketClient(WebSocketEndpoint, symbol,
				Key, Secret, ReConnectionSettings.WorkingTime,
				ReConnectionSettings.ReAttemptCount)
			{
				Parent = this,
			};
			client.StateReceived += OnMarketStreamStateAsync;
			client.Error += OnSocketErrorAsync;
			try
			{
				await client.ConnectAsync(cancellationToken);
				using (_sync.EnterScope())
					_marketStreams.Add(symbol, new()
					{
						Client = client,
						References = 1,
					});
			}
			catch
			{
				client.Dispose();
				throw;
			}
		}
		finally
		{
			_marketStreamGate.Release();
		}
	}

	private async ValueTask ReleaseMarketStreamAsync(string symbol,
		CancellationToken cancellationToken)
	{
		if (symbol.IsEmpty())
			return;
		symbol = symbol.NormalizeSymbol();
		await _marketStreamGate.WaitAsync(cancellationToken);
		try
		{
			LunoMarketSocketClient client = null;
			using (_sync.EnterScope())
			{
				if (!_marketStreams.TryGetValue(symbol, out var holder))
					return;
				if (--holder.References > 0)
					return;
				_marketStreams.Remove(symbol);
				client = holder.Client;
			}
			await DisposeMarketSocketAsync(client, cancellationToken);
		}
		finally
		{
			_marketStreamGate.Release();
		}
	}

	private ValueTask OnSocketErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private async ValueTask OnUserSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Failed)
		{
			await SendOutConnectionStateAsync(ConnectionStates.Failed,
				cancellationToken);
			return;
		}
		if (state == ConnectionStates.Restored)
		{
			await RefreshPrivateSnapshotsAsync(cancellationToken);
			await SendOutConnectionStateAsync(ConnectionStates.Restored,
				cancellationToken);
		}
	}

	private async ValueTask DisposeClientsAsync(
		CancellationToken cancellationToken)
	{
		LunoMarketSocketClient[] marketSockets;
		using (_sync.EnterScope())
		{
			marketSockets = [.. _marketStreams.Values.Select(static holder =>
				holder.Client)];
			_marketStreams.Clear();
		}
		foreach (var socket in marketSockets)
			await DisposeMarketSocketAsync(socket, cancellationToken);

		var userSocket = _userSocketClient;
		_userSocketClient = null;
		if (userSocket is not null)
		{
			try
			{
				await userSocket.DisconnectAsync(cancellationToken);
			}
			catch (Exception error)
			{
				if (!cancellationToken.IsCancellationRequested)
					await SendOutErrorAsync(error, cancellationToken);
			}
			finally
			{
				userSocket.Dispose();
			}
		}

		var rest = _restClient;
		_restClient = null;
		rest?.Dispose();
		ClearState();
	}

	private async ValueTask DisposeMarketSocketAsync(
		LunoMarketSocketClient socket, CancellationToken cancellationToken)
	{
		if (socket is null)
			return;
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

	private void DisposeClients()
	{
		LunoMarketSocketClient[] marketSockets;
		using (_sync.EnterScope())
		{
			marketSockets = [.. _marketStreams.Values.Select(static holder =>
				holder.Client)];
			_marketStreams.Clear();
		}
		foreach (var socket in marketSockets)
			socket.Dispose();
		_userSocketClient?.Dispose();
		_userSocketClient = null;
		_restClient?.Dispose();
		_restClient = null;
		ClearState();
	}
}
