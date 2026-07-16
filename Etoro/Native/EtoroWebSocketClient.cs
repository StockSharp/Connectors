namespace StockSharp.Etoro.Native;

sealed class EtoroWebSocketClient : BaseLogReceiver
{
	private static readonly Uri _uri = new("wss://ws.etoro.com/ws");
	private const int _maxMessageSize = 4 * 1024 * 1024;

	private readonly string _apiKey;
	private readonly string _userKey;
	private readonly int _maxAttempts;
	private readonly CancellationTokenSource _cancellation = new();
	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly TaskCompletionSource _initialConnection = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly HashSet<string> _topics = new(StringComparer.OrdinalIgnoreCase);
	private readonly object _topicsSync = new();
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.DateTime,
		DateTimeZoneHandling = DateTimeZoneHandling.Utc,
		NullValueHandling = NullValueHandling.Ignore,
		Converters = [new StringEnumConverter()],
	};

	private ClientWebSocket _socket;
	private Task _runTask;

	public EtoroWebSocketClient(string apiKey, string userKey, int maxAttempts)
	{
		_apiKey = apiKey.ThrowIfEmpty(nameof(apiKey));
		_userKey = userKey.ThrowIfEmpty(nameof(userKey));
		_maxAttempts = Math.Max(1, maxAttempts);
	}

	public override string Name => nameof(Etoro) + "_" + nameof(EtoroWebSocketClient);

	public event Func<EtoroSocketMessage, CancellationToken, ValueTask> MessageReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public async Task Connect(CancellationToken cancellationToken)
	{
		if (_runTask != null)
			throw new InvalidOperationException("eToro WebSocket is already running.");
		_runTask = Run(_cancellation.Token);
		await _initialConnection.Task.WaitAsync(cancellationToken);
	}

	public async Task Subscribe(IEnumerable<string> topics, bool isSnapshot, CancellationToken cancellationToken)
	{
		var added = new List<string>();
		lock (_topicsSync)
		{
			foreach (var topic in topics.Where(t => !t.IsEmpty()))
			{
				if (_topics.Add(topic))
					added.Add(topic);
			}
		}
		var socket = _socket;
		if (added.Count == 0 || socket?.State != WebSocketState.Open)
			return;

		try
		{
			await SendSubscription(socket, added.ToArray(), isSnapshot, cancellationToken);
		}
		catch
		{
			lock (_topicsSync)
			{
				foreach (var topic in added)
					_topics.Remove(topic);
			}
			throw;
		}
	}

	public async Task Unsubscribe(IEnumerable<string> topics, CancellationToken cancellationToken)
	{
		var removed = new List<string>();
		lock (_topicsSync)
		{
			foreach (var topic in topics.Where(t => !t.IsEmpty()))
			{
				if (_topics.Remove(topic))
					removed.Add(topic);
			}
		}
		var socket = _socket;
		if (removed.Count > 0 && socket?.State == WebSocketState.Open)
			await SendUnsubscription(socket, removed.ToArray(), cancellationToken);
	}

	public async Task Disconnect()
	{
		_cancellation.Cancel();
		var socket = _socket;
		if (socket?.State == WebSocketState.Open)
		{
			try
			{
				await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", CancellationToken.None);
			}
			catch (WebSocketException)
			{
			}
		}

		if (_runTask != null)
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

	private async Task Run(CancellationToken cancellationToken)
	{
		var failures = 0;
		var wasConnected = false;
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				await Invoke(StateChanged, ConnectionStates.Connecting, cancellationToken);
				using var socket = new ClientWebSocket();
				_socket = socket;
				socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
				await socket.ConnectAsync(_uri, cancellationToken);
				await Authenticate(socket, cancellationToken);
				await RestoreSubscriptions(socket, cancellationToken);

				failures = 0;
				wasConnected = true;
				_initialConnection.TrySetResult();
				await Invoke(StateChanged, ConnectionStates.Connected, cancellationToken);
				await ReceiveLoop(socket, cancellationToken);

				if (!cancellationToken.IsCancellationRequested)
					throw new WebSocketException("eToro WebSocket closed unexpectedly.");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception ex)
			{
				failures++;
				await Invoke(Error, ex, cancellationToken);
				await Invoke(StateChanged, ConnectionStates.Disconnected, cancellationToken);
				if (failures > _maxAttempts)
				{
					if (!wasConnected)
						_initialConnection.TrySetException(ex);
					break;
				}
				await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(failures, 5))), cancellationToken);
			}
			finally
			{
				_socket = null;
			}
		}

		if (!_initialConnection.Task.IsCompleted)
			_initialConnection.TrySetCanceled(cancellationToken);
		await Invoke(StateChanged, ConnectionStates.Disconnected, CancellationToken.None);
	}

	private async Task Authenticate(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		var id = Guid.NewGuid();
		await Send(socket, new EtoroSocketRequest<EtoroSocketAuthentication>
		{
			Id = id,
			Operation = EtoroSocketOperations.Authenticate,
			Data = new()
			{
				ApiKey = _apiKey,
				UserKey = _userKey,
			},
		}, cancellationToken);

		var envelope = Deserialize<EtoroSocketEnvelope>(await ReceiveText(socket, cancellationToken));
		if (envelope?.Id != id || envelope.IsSuccess != true || envelope.Operation != EtoroSocketOperations.Authenticate)
			throw new InvalidOperationException("eToro WebSocket authentication failed" +
				(envelope?.ErrorMessage.IsEmpty() == false ? $": {envelope.ErrorMessage}" : "."));
	}

	private async Task RestoreSubscriptions(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		string[] topics;
		lock (_topicsSync)
			topics = _topics.ToArray();
		if (topics.Length > 0)
			await SendSubscription(socket, topics, true, cancellationToken);
	}

	private async Task ReceiveLoop(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
		{
			var envelope = Deserialize<EtoroSocketEnvelope>(await ReceiveText(socket, cancellationToken));
			if (envelope == null)
				continue;
			if (envelope.IsSuccess == false)
			{
				await Invoke(Error, new InvalidOperationException("eToro WebSocket operation failed" +
					(envelope.ErrorCode.IsEmpty() ? string.Empty : $" ({envelope.ErrorCode})") +
					(envelope.ErrorMessage.IsEmpty() ? "." : $": {envelope.ErrorMessage}")), cancellationToken);
				continue;
			}
			foreach (var message in envelope.Messages ?? [])
				await Invoke(MessageReceived, message, cancellationToken);
		}
	}

	private Task SendSubscription(ClientWebSocket socket, string[] topics, bool isSnapshot,
		CancellationToken cancellationToken)
		=> Send(socket, new EtoroSocketRequest<EtoroSocketSubscription>
		{
			Id = Guid.NewGuid(),
			Operation = EtoroSocketOperations.Subscribe,
			Data = new()
			{
				Topics = topics,
				IsSnapshot = isSnapshot,
			},
		}, cancellationToken);

	private Task SendUnsubscription(ClientWebSocket socket, string[] topics, CancellationToken cancellationToken)
		=> Send(socket, new EtoroSocketRequest<EtoroSocketUnsubscription>
		{
			Id = Guid.NewGuid(),
			Operation = EtoroSocketOperations.Unsubscribe,
			Data = new() { Topics = topics },
		}, cancellationToken);

	private async Task Send<T>(ClientWebSocket socket, EtoroSocketRequest<T> request, CancellationToken cancellationToken)
	{
		var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request, _jsonSettings));
		await _sendLock.WaitAsync(cancellationToken);
		try
		{
			await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
		}
		finally
		{
			_sendLock.Release();
		}
	}

	private static async Task<string> ReceiveText(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		var buffer = new byte[16 * 1024];
		using var stream = new MemoryStream();
		while (true)
		{
			var result = await socket.ReceiveAsync(buffer, cancellationToken);
			if (result.MessageType == WebSocketMessageType.Close)
				throw new WebSocketException($"eToro WebSocket closed: {result.CloseStatus} {result.CloseStatusDescription}");
			if (result.MessageType != WebSocketMessageType.Text)
				throw new InvalidDataException($"Unexpected eToro WebSocket message type {result.MessageType}.");
			stream.Write(buffer, 0, result.Count);
			if (stream.Length > _maxMessageSize)
				throw new InvalidDataException("eToro WebSocket message exceeds 4 MiB.");
			if (result.EndOfMessage)
				return Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
		}
	}

	private T Deserialize<T>(string content)
		=> content.IsEmpty() ? default : JsonConvert.DeserializeObject<T>(content, _jsonSettings);

	private static ValueTask Invoke<T>(Func<T, CancellationToken, ValueTask> handler, T value,
		CancellationToken cancellationToken)
		=> handler == null ? default : handler(value, cancellationToken);

	protected override void DisposeManaged()
	{
		Disconnect().GetAwaiter().GetResult();
		_cancellation.Dispose();
		_sendLock.Dispose();
		base.DisposeManaged();
	}
}
