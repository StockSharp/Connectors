namespace StockSharp.Luno.Native;

sealed class LunoUserSocketClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly string _apiKey;
	private readonly string _apiSecret;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
		Converters = [new StringEnumConverter()],
	};
	private WebSocketClient _client;
	private CancellationTokenSource _heartbeatCancellation;
	private Task _heartbeatTask;

	public LunoUserSocketClient(string endpoint, SecureString key,
		SecureString secret, WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = CreateEndpoint(endpoint, "/api/1/userstream");
		_apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
		_apiSecret = secret.IsEmpty() ? null : secret.UnSecure().Trim();
		if (_apiKey.IsEmpty() || _apiSecret.IsEmpty())
			throw new ArgumentException(
				"Luno user streams require an API key and secret.");
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "Luno_User_WebSocket";

	public event Func<LunoUserStreamEnvelope, CancellationToken, ValueTask>
		UpdateReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_heartbeatCancellation?.Cancel();
		_heartbeatCancellation?.Dispose();
		_client?.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"Luno user WebSocket is already initialized.");
		var client = _client = CreateClient();
		try
		{
			await client.ConnectAsync(cancellationToken);
			StartHeartbeat();
		}
		catch
		{
			await DisposeClientAsync(cancellationToken);
			throw;
		}
	}

	public ValueTask DisconnectAsync(CancellationToken cancellationToken)
		=> DisposeClientAsync(cancellationToken);

	private WebSocketClient CreateClient()
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			_endpoint,
			(state, token) => RaiseStateChangedAsync(state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(socket, message, token) => OnProcessAsync(message, token),
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
		client.PostConnect += OnPostConnectAsync;
		return client;
	}

	private async ValueTask OnPostConnectAsync(bool isReconnect,
		CancellationToken cancellationToken)
	{
		_ = isReconnect;
		var payload = JsonConvert.SerializeObject(new LunoStreamCredentials
		{
			ApiKeyId = _apiKey,
			ApiKeySecret = _apiSecret,
		}, _jsonSettings);
		await _client.SendAsync(Encoding.UTF8.GetBytes(payload),
			WebSocketMessageType.Text, cancellationToken);
	}

	private async ValueTask OnProcessAsync(WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var update = JsonConvert.DeserializeObject<LunoUserStreamEnvelope>(
				payload, _jsonSettings) ?? throw new InvalidDataException(
					"Luno user stream returned an empty JSON value.");
			if (update.Type is not null && UpdateReceived is { } handler)
				await handler(update, cancellationToken);
		}
		catch (Exception error)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask DisposeClientAsync(
		CancellationToken cancellationToken)
	{
		await StopHeartbeatAsync();
		var client = _client;
		_client = null;
		if (client is null)
			return;
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

	private void StartHeartbeat()
	{
		_heartbeatCancellation = new();
		_heartbeatTask = RunHeartbeatAsync(_heartbeatCancellation.Token);
	}

	private async Task RunHeartbeatAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (true)
			{
				await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
				if (_client?.IsConnected == true)
					await _client.SendOpCode();
			}
		}
		catch (OperationCanceledException) when (
			cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception error)
		{
			await RaiseErrorAsync(error, CancellationToken.None);
		}
	}

	private async ValueTask StopHeartbeatAsync()
	{
		var cancellation = _heartbeatCancellation;
		_heartbeatCancellation = null;
		var task = _heartbeatTask;
		_heartbeatTask = null;
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

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;

	private ValueTask RaiseStateChangedAsync(ConnectionStates state,
		CancellationToken cancellationToken)
		=> StateChanged is { } handler
			? handler(state, cancellationToken)
			: default;

	private static string CreateEndpoint(string value, string path)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim().TrimEnd('/') + "/";
		if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
			!endpoint.Scheme.EqualsIgnoreCase("wss"))
			throw new ArgumentException(
				"Luno WebSocket endpoint must be an absolute WSS URI.",
				nameof(value));
		return new Uri(endpoint, path.TrimStart('/')).ToString();
	}
}
