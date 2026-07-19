namespace StockSharp.Bitkub.Native;

sealed class BitkubPrivateWebSocketClient : BaseLogReceiver
{
	private static readonly TimeSpan _pingInterval = TimeSpan.FromMinutes(4);
	private readonly string _endpoint;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Func<BitkubWebSocketAuthenticationData> _getAuthentication;
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;
	private TaskCompletionSource<bool> _authenticationCompletion;
	private DateTime _lastPing;

	public BitkubPrivateWebSocketClient(string endpoint, WorkingTime workingTime,
		int reconnectAttempts,
		Func<BitkubWebSocketAuthenticationData> getAuthentication)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		_workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
		_getAuthentication = getAuthentication ??
			throw new ArgumentNullException(nameof(getAuthentication));
	}

	public override string Name => "Bitkub_PrivateWs";

	public event Func<BitkubOrderUpdate, CancellationToken, ValueTask> OrderUpdated;
	public event Func<BitkubMatchUpdate, CancellationToken, ValueTask> MatchUpdated;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"Bitkub private WebSocket is already initialized.");
		var client = _client = CreateClient();
		try
		{
			await client.ConnectAsync(cancellationToken);
			await AuthenticateAsync(client, cancellationToken);
		}
		catch
		{
			await DisposeClientAsync(cancellationToken);
			throw;
		}
	}

	public ValueTask DisconnectAsync(CancellationToken cancellationToken)
		=> DisposeClientAsync(cancellationToken);

	public async ValueTask ProcessTimeAsync(CancellationToken cancellationToken)
	{
		var client = _client;
		if (client?.IsConnected != true ||
			DateTime.UtcNow - _lastPing < _pingInterval)
			return;
		await SendAsync(client, new BitkubWebSocketPingRequest(), cancellationToken);
		_lastPing = DateTime.UtcNow;
	}

	private WebSocketClient CreateClient()
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			_endpoint,
			(state, token) => OnStateChangedAsync(client, state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(socket, message, token) => OnProcessAsync(socket, message, token),
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			Indent = false,
			SendSettings = _jsonSettings,
		};
		client.Init += socket => socket.Options.SetRequestHeader("User-Agent",
			"dotnet-stocksharp-bitkub/1.0");
		return client;
	}

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			try
			{
				await AuthenticateAsync(client, cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await RaiseErrorAsync(error, cancellationToken);
				if (StateChanged is { } failedHandler)
					await failedHandler(ConnectionStates.Failed, cancellationToken);
				return;
			}
		}
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask AuthenticateAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
		var completion = new TaskCompletionSource<bool>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		_authenticationCompletion = completion;
		await SendAsync(client, new BitkubWebSocketAuthenticationRequest
		{
			Data = _getAuthentication(),
		}, cancellationToken);
		await completion.Task.WaitAsync(cancellationToken);
		_lastPing = DateTime.UtcNow;
	}

	private async ValueTask OnProcessAsync(WebSocketClient client,
		WebSocketMessage message, CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var header = Deserialize<BitkubPrivateWebSocketHeader>(payload);
			if (!header.Code.IsEmpty() && !header.Code.EqualsIgnoreCase("200"))
			{
				var error = new InvalidOperationException(
					$"Bitkub private WebSocket error {header.Code}: {header.Message}");
				if (header.Event == BitkubPrivateWebSocketEvents.Authenticate)
					_authenticationCompletion?.TrySetException(error);
				else
					await RaiseErrorAsync(error, cancellationToken);
				return;
			}

			switch (header.Event)
			{
				case BitkubPrivateWebSocketEvents.Authenticate:
					await SendAsync(client, new BitkubWebSocketSubscriptionRequest
					{
						Channel = BitkubPrivateWebSocketChannels.OrderUpdate,
					}, cancellationToken);
					await SendAsync(client, new BitkubWebSocketSubscriptionRequest
					{
						Channel = BitkubPrivateWebSocketChannels.MatchUpdate,
					}, cancellationToken);
					_authenticationCompletion?.TrySetResult(true);
					return;
				case BitkubPrivateWebSocketEvents.OrderUpdate:
				{
					var envelope = Deserialize<BitkubPrivateWebSocketEnvelope<
						BitkubOrderUpdate>>(payload);
					if (envelope.Data is not null && OrderUpdated is { } handler)
						await handler(envelope.Data, cancellationToken);
					return;
				}
				case BitkubPrivateWebSocketEvents.MatchUpdate:
				{
					var envelope = Deserialize<BitkubPrivateWebSocketEnvelope<
						BitkubMatchUpdate>>(payload);
					if (envelope.Data is not null && MatchUpdated is { } handler)
						await handler(envelope.Data, cancellationToken);
					return;
				}
				case BitkubPrivateWebSocketEvents.Subscribe:
				case BitkubPrivateWebSocketEvents.Unsubscribe:
				case BitkubPrivateWebSocketEvents.Ping:
					return;
				default:
					throw new InvalidDataException(
						"Bitkub private WebSocket returned an unsupported event.");
			}
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or InvalidOperationException or FormatException or
			OverflowException)
		{
			_authenticationCompletion?.TrySetException(error);
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask SendAsync<TRequest>(WebSocketClient client,
		TRequest request, CancellationToken cancellationToken)
	{
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(request, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private TMessage Deserialize<TMessage>(string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<TMessage>(payload, _jsonSettings)
				?? throw new InvalidDataException(
					"Bitkub private WebSocket returned an empty message.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Bitkub private WebSocket returned malformed JSON.", error);
		}
	}

	private async ValueTask DisposeClientAsync(
		CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		if (client is null)
			return;
		_authenticationCompletion?.TrySetCanceled();
		try
		{
			if (client.IsConnected)
				await client.DisconnectAsync(cancellationToken);
		}
		finally
		{
			client.Dispose();
		}
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;
}
