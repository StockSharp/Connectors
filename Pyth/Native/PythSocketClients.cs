namespace StockSharp.Pyth.Native;

sealed class PythSocketPool : BaseLogReceiver
{
	private readonly Lock _sync = new();
	private readonly Dictionary<long, PythSubscribeRequest> _subscriptions = [];
	private readonly HashSet<string> _terminalEndpoints =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly PythSocketConnection[] _connections;
	private bool _isConnected;
	private bool _isDisposed;

	public PythSocketPool(IEnumerable<string> endpoints, SecureString token,
		int maximumAttempts)
	{
		var addresses = (endpoints ?? [])
			.Select(static endpoint => endpoint?.Trim())
			.Where(static endpoint => !endpoint.IsEmpty())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (addresses.Length != 3)
			throw new ArgumentException(
				"Pyth requires three distinct WebSocket endpoints.", nameof(endpoints));
		_connections = addresses.Select(endpoint =>
			new PythSocketConnection(endpoint, token, maximumAttempts)
			{
				Parent = this,
			}).ToArray();
		foreach (var connection in _connections)
		{
			connection.Connected += OnConnectedAsync;
			connection.MessageReceived += OnMessageReceivedAsync;
			connection.Error += OnErrorAsync;
		}
	}

	public override string Name => "Pyth_WS_POOL";

	public event Func<PythSocketMessage, CancellationToken, ValueTask>
		MessageReceived;
	public event Func<Exception, bool, CancellationToken, ValueTask> Error;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		if (_isConnected)
			throw new InvalidOperationException("Pyth WebSocket pool is already running.");
		try
		{
			await Task.WhenAll(_connections.Select(connection =>
				connection.ConnectAsync(cancellationToken).AsTask()));
			_isConnected = true;
		}
		catch
		{
			await DisconnectAsync();
			throw;
		}
	}

	public async ValueTask SubscribeAsync(PythSubscribeRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (!_isConnected)
			throw new InvalidOperationException("Pyth WebSocket pool is not connected.");
		if (request.SubscriptionId <= 0 ||
			request.PriceFeedIds is not { Length: > 0 })
			throw new ArgumentException("Pyth subscription is incomplete.",
				nameof(request));
		using (_sync.EnterScope())
		{
			if (!_subscriptions.TryAdd(request.SubscriptionId, request))
				throw new InvalidOperationException(
					$"Pyth subscription {request.SubscriptionId} already exists.");
		}
		try
		{
			foreach (var connection in _connections)
				await connection.SendSubscribeAsync(request, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_subscriptions.Remove(request.SubscriptionId);
			await SendUnsubscribeAsync(request.SubscriptionId,
				CancellationToken.None);
			throw;
		}
	}

	public async ValueTask UnsubscribeAsync(long subscriptionId,
		CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
			_subscriptions.Remove(subscriptionId);
		await SendUnsubscribeAsync(subscriptionId, cancellationToken);
	}

	private async ValueTask SendUnsubscribeAsync(long subscriptionId,
		CancellationToken cancellationToken)
	{
		if (subscriptionId <= 0)
			return;
		var request = new PythUnsubscribeRequest
		{
			SubscriptionId = subscriptionId,
		};
		foreach (var connection in _connections)
			await connection.SendUnsubscribeAsync(request, cancellationToken);
	}

	private async ValueTask OnConnectedAsync(PythSocketConnection connection,
		CancellationToken cancellationToken)
	{
		PythSubscribeRequest[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _subscriptions.Values];
		foreach (var request in subscriptions)
			await connection.SendSubscribeAsync(request, cancellationToken);
	}

	private ValueTask OnMessageReceivedAsync(PythSocketMessage message,
		CancellationToken cancellationToken)
		=> MessageReceived is { } handler
			? handler(message, cancellationToken)
			: default;

	private ValueTask OnErrorAsync(PythSocketConnection connection,
		Exception error, bool isTerminal, CancellationToken cancellationToken)
	{
		var isPoolTerminal = false;
		if (isTerminal)
		{
			using (_sync.EnterScope())
			{
				_terminalEndpoints.Add(connection.Endpoint.AbsoluteUri);
				isPoolTerminal = _terminalEndpoints.Count == _connections.Length;
			}
		}
		return Error is { } handler
			? handler(error, isPoolTerminal, cancellationToken)
			: default;
	}

	public async ValueTask DisconnectAsync()
	{
		_isConnected = false;
		using (_sync.EnterScope())
		{
			_subscriptions.Clear();
			_terminalEndpoints.Clear();
		}
		foreach (var connection in _connections)
			await connection.DisconnectAsync();
	}

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		foreach (var connection in _connections)
		{
			connection.Connected -= OnConnectedAsync;
			connection.MessageReceived -= OnMessageReceivedAsync;
			connection.Error -= OnErrorAsync;
		}
		DisconnectAsync().AsTask().GetAwaiter().GetResult();
		foreach (var connection in _connections)
			connection.Dispose();
		base.DisposeManaged();
	}
}

sealed class PythSocketConnection : BaseLogReceiver
{
	private const int _maximumMessageLength = 8 * 1024 * 1024;
	private static readonly UTF8Encoding _strictUtf8 = new(false, true);
	private readonly string _authorization;
	private readonly int _maximumAttempts;
	private readonly CancellationTokenSource _cancellation = new();
	private readonly SemaphoreSlim _sendGate = new(1, 1);
	private readonly TaskCompletionSource _initialConnection =
		new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly JsonSerializerSettings _settings = new()
	{
		Culture = CultureInfo.InvariantCulture,
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		Formatting = Formatting.None,
		NullValueHandling = NullValueHandling.Ignore,
	};
	private ClientWebSocket _socket;
	private Task _runTask;

	public PythSocketConnection(string endpoint, SecureString token,
		int maximumAttempts)
	{
		Endpoint = ValidateEndpoint(endpoint);
		if (token.IsEmpty())
			throw new ArgumentNullException(nameof(token));
		var value = token.UnSecure().Trim();
		if (value.IsEmpty() || value.Length > 8192 || value.Any(char.IsControl))
			throw new ArgumentException("Pyth API token is empty or invalid.",
				nameof(token));
		_authorization = "Bearer " + value;
		_maximumAttempts = Math.Max(1, maximumAttempts);
	}

	public override string Name => "Pyth_WS_" + Endpoint.Host;

	public Uri Endpoint { get; }

	public event Func<PythSocketConnection, CancellationToken, ValueTask> Connected;
	public event Func<PythSocketMessage, CancellationToken, ValueTask>
		MessageReceived;
	public event Func<PythSocketConnection, Exception, bool, CancellationToken,
		ValueTask> Error;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_runTask is not null)
			throw new InvalidOperationException("Pyth WebSocket is already running.");
		_runTask = RunAsync(_cancellation.Token);
		await _initialConnection.Task.WaitAsync(cancellationToken);
	}

	public ValueTask SendSubscribeAsync(PythSubscribeRequest request,
		CancellationToken cancellationToken)
		=> SendAsync(JsonConvert.SerializeObject(request, _settings),
			cancellationToken);

	public ValueTask SendUnsubscribeAsync(PythUnsubscribeRequest request,
		CancellationToken cancellationToken)
		=> SendAsync(JsonConvert.SerializeObject(request, _settings),
			cancellationToken);

	private async ValueTask SendAsync(string content,
		CancellationToken cancellationToken)
	{
		var socket = _socket;
		if (socket?.State != WebSocketState.Open)
			return;
		var bytes = Encoding.UTF8.GetBytes(content);
		await _sendGate.WaitAsync(cancellationToken);
		try
		{
			socket = _socket;
			if (socket?.State == WebSocketState.Open)
				await socket.SendAsync(bytes, WebSocketMessageType.Text, true,
					cancellationToken);
		}
		finally
		{
			_sendGate.Release();
		}
	}

	private async Task RunAsync(CancellationToken cancellationToken)
	{
		var failures = 0;
		var isEverConnected = false;
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				using var socket = new ClientWebSocket();
				_socket = socket;
				socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
				socket.Options.SetRequestHeader("Authorization", _authorization);
				socket.Options.SetRequestHeader("User-Agent", "StockSharp-Pyth/1.0");
				await socket.ConnectAsync(Endpoint, cancellationToken);
				failures = 0;
				isEverConnected = true;
				_initialConnection.TrySetResult();
				if (Connected is { } connected)
					await connected(this, cancellationToken);
				await ReceiveAsync(socket, cancellationToken);
				if (!cancellationToken.IsCancellationRequested)
					throw new WebSocketException(
						$"Pyth WebSocket {Endpoint.Host} closed unexpectedly.");
			}
			catch (OperationCanceledException) when (
				cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception error)
			{
				failures++;
				var isTerminal = failures >= _maximumAttempts;
				if (Error is { } handler)
					await handler(this, Redact(error), isTerminal,
						CancellationToken.None);
				if (isTerminal)
				{
					if (!isEverConnected)
						_initialConnection.TrySetException(Redact(error));
					break;
				}
				await Task.Delay(TimeSpan.FromSeconds(
					Math.Min(30, 1 << Math.Min(failures, 5))), cancellationToken);
			}
			finally
			{
				_socket = null;
			}
		}
		if (!_initialConnection.Task.IsCompleted)
			_initialConnection.TrySetCanceled(cancellationToken);
	}

	private async Task ReceiveAsync(ClientWebSocket socket,
		CancellationToken cancellationToken)
	{
		while (socket.State == WebSocketState.Open &&
			!cancellationToken.IsCancellationRequested)
		{
			var content = await ReceiveTextAsync(socket, cancellationToken);
			PythSocketMessage message;
			try
			{
				message = JsonConvert.DeserializeObject<PythSocketMessage>(content,
					_settings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"Pyth WebSocket returned invalid JSON.", error);
			}
			if (message is null || message.Type == PythMessageTypes.Unknown)
				throw new InvalidDataException(
					"Pyth WebSocket returned an unknown message.");
			if (MessageReceived is { } handler)
				await handler(message, cancellationToken);
		}
	}

	private static async ValueTask<string> ReceiveTextAsync(
		ClientWebSocket socket, CancellationToken cancellationToken)
	{
		var chunk = new byte[16384];
		using var buffer = new MemoryStream();
		while (true)
		{
			var result = await socket.ReceiveAsync(chunk, cancellationToken);
			if (result.MessageType == WebSocketMessageType.Close)
				throw new WebSocketException(
					$"Pyth WebSocket closed: {result.CloseStatus} {result.CloseStatusDescription}");
			if (result.MessageType != WebSocketMessageType.Text)
				throw new InvalidDataException(
					$"Pyth WebSocket returned unexpected {result.MessageType} data.");
			if (buffer.Length + result.Count > _maximumMessageLength)
				throw new InvalidDataException(
					"Pyth WebSocket message exceeds 8 MiB.");
			buffer.Write(chunk, 0, result.Count);
			if (result.EndOfMessage)
			{
				try
				{
					return _strictUtf8.GetString(buffer.GetBuffer(), 0,
						checked((int)buffer.Length));
				}
				catch (DecoderFallbackException error)
				{
					throw new InvalidDataException(
						"Pyth WebSocket returned invalid UTF-8.", error);
				}
			}
		}
	}

	public async ValueTask DisconnectAsync()
	{
		_cancellation.Cancel();
		var socket = _socket;
		if (socket?.State == WebSocketState.Open)
		{
			try
			{
				await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,
					"Client disconnect", CancellationToken.None);
			}
			catch (Exception error) when (error is WebSocketException or
				ObjectDisposedException)
			{
			}
		}
		if (_runTask is not null)
		{
			try
			{
				await _runTask;
			}
			catch (OperationCanceledException)
			{
			}
		}
	}

	private static Uri ValidateEndpoint(string endpoint)
	{
		if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var uri) ||
			uri.Scheme != "wss" || uri.Host.IsEmpty() || !uri.UserInfo.IsEmpty() ||
			!uri.Query.IsEmpty() || !uri.Fragment.IsEmpty() ||
			!uri.AbsolutePath.EndsWith("/v1/stream",
				StringComparison.OrdinalIgnoreCase))
			throw new ArgumentException(
				"Pyth WebSocket endpoint must be an absolute WSS '/v1/stream' URI without credentials, query, or fragment.",
				nameof(endpoint));
		return uri;
	}

	private IOException Redact(Exception error)
	{
		var token = _authorization["Bearer ".Length..];
		return new IOException(error.Message.Replace(token, "***",
			StringComparison.Ordinal));
	}

	protected override void DisposeManaged()
	{
		DisconnectAsync().AsTask().GetAwaiter().GetResult();
		_cancellation.Dispose();
		_sendGate.Dispose();
		base.DisposeManaged();
	}
}
