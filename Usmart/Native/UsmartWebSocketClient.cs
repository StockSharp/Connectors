namespace StockSharp.Usmart.Native;

sealed class UsmartWebSocketClient : BaseLogReceiver
{
	private const int _maxMessageSize = 4 * 1024 * 1024;
	private readonly Uri _uri;
	private readonly string _accessToken;
	private readonly int _maxAttempts;
	private readonly CancellationTokenSource _cancellation = new();
	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly SemaphoreSlim _topicRateLock = new(1, 1);
	private readonly TaskCompletionSource _initialConnection =
		new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly HashSet<string> _topics = new(StringComparer.OrdinalIgnoreCase);
	private readonly object _topicsSync = new();
	private ClientWebSocket _socket;
	private Task _runTask;
	private long _requestId;
	private DateTime _topicWindow;
	private int _topicsInWindow;

	public UsmartWebSocketClient(bool isDemo, string accessToken, int maxAttempts)
	{
		_uri = new(isDemo ? "wss://open-hz-uat.yxzq.com/wss/v1"
			: "wss://open-hz.usmartsg.com:8443/wss/v1");
		_accessToken = accessToken.ThrowIfEmpty(nameof(accessToken));
		_maxAttempts = Math.Max(1, maxAttempts);
	}

	public override string Name => nameof(Usmart) + "_WebSocket";

	public event Func<string, UsmartQuote, CancellationToken, ValueTask> QuoteReceived;
	public event Func<string, UsmartTick, CancellationToken, ValueTask> TickReceived;
	public event Func<string, UsmartDepthLevel[], CancellationToken, ValueTask> DepthReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public async Task Connect(CancellationToken cancellationToken)
	{
		if (_runTask != null)
			throw new InvalidOperationException("uSMART WebSocket is already running.");
		_runTask = Run(_cancellation.Token);
		await _initialConnection.Task.WaitAsync(cancellationToken);
	}

	public async Task Subscribe(string topic, CancellationToken cancellationToken)
	{
		var added = false;
		lock (_topicsSync)
			added = _topics.Add(topic.ThrowIfEmpty(nameof(topic)));
		if (!added || _socket?.State != WebSocketState.Open)
			return;
		try
		{
			await SendTopics(_socket, UsmartSocketOperations.Subscribe, [topic], cancellationToken);
		}
		catch
		{
			lock (_topicsSync)
				_topics.Remove(topic);
			throw;
		}
	}

	public async Task Unsubscribe(string topic, CancellationToken cancellationToken)
	{
		var removed = false;
		lock (_topicsSync)
			removed = _topics.Remove(topic);
		if (removed && _socket?.State == WebSocketState.Open)
			await SendTopics(_socket, UsmartSocketOperations.Unsubscribe, [topic], cancellationToken);
	}

	public async Task Disconnect()
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
					throw new WebSocketException("uSMART WebSocket closed unexpectedly.");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception error)
			{
				failures++;
				await Invoke(Error, error, cancellationToken);
				await Invoke(StateChanged, ConnectionStates.Disconnected, cancellationToken);
				if (failures > _maxAttempts)
				{
					if (!wasConnected)
						_initialConnection.TrySetException(error);
					break;
				}
				await Task.Delay(TimeSpan.FromSeconds(Math.Min(30,
					1 << Math.Min(failures, 5))), cancellationToken);
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

	private async Task Authenticate(ClientWebSocket socket,
		CancellationToken cancellationToken)
	{
		var requestId = NextRequestId();
		await Send(socket, new UsmartSocketRequest
		{
			Operation = UsmartSocketOperations.Authenticate,
			Timestamp = GetUnixSeconds(),
			RequestId = requestId,
			AccessToken = _accessToken,
		}, cancellationToken);
		while (true)
		{
			var message = Deserialize(await ReceiveText(socket, cancellationToken));
			if (message.Operation == UsmartSocketOperations.Ping)
			{
				await SendPong(socket, message, cancellationToken);
				continue;
			}
			if (message.RequestId != requestId ||
				message.Operation != UsmartSocketOperations.Authenticate)
				continue;
			if (message.Code != 0)
				throw new InvalidOperationException(
					$"uSMART WebSocket authentication failed ({message.Code}): {message.Message}.");
			return;
		}
	}

	private async Task RestoreSubscriptions(ClientWebSocket socket,
		CancellationToken cancellationToken)
	{
		string[] topics;
		lock (_topicsSync)
			topics = _topics.ToArray();
		for (var index = 0; index < topics.Length; index += 10)
		{
			if (index > 0)
				await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
			await SendTopics(socket, UsmartSocketOperations.Subscribe,
				topics.Skip(index).Take(10).ToArray(), cancellationToken);
		}
	}

	private async Task ReceiveLoop(ClientWebSocket socket,
		CancellationToken cancellationToken)
	{
		while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
		{
			var message = Deserialize(await ReceiveText(socket, cancellationToken));
			if (message.Operation == UsmartSocketOperations.Ping)
			{
				await SendPong(socket, message, cancellationToken);
				continue;
			}
			if (message.Code is int code && code != 0)
			{
				await Invoke(Error, new InvalidOperationException(
					$"uSMART WebSocket error {code}: {message.Message.IsEmpty("Unknown error")}."),
					cancellationToken);
				continue;
			}
			if (message.Operation != UsmartSocketOperations.Update ||
				message.Topic.IsEmpty() || message.Data.IsEmpty())
				continue;
			await ProcessUpdate(message, cancellationToken);
		}
	}

	private async Task ProcessUpdate(UsmartSocketMessage message,
		CancellationToken cancellationToken)
	{
		var json = Decode(message.Data);
		if (message.Topic.StartsWith("rt.", StringComparison.OrdinalIgnoreCase))
		{
			var quote = JsonConvert.DeserializeObject<UsmartQuote>(json)
				?? throw new InvalidDataException("uSMART returned an invalid real-time quote update.");
			await Invoke(QuoteReceived, message.Topic, quote, cancellationToken);
		}
		else if (message.Topic.StartsWith("tk.", StringComparison.OrdinalIgnoreCase))
		{
			var tick = JsonConvert.DeserializeObject<UsmartTick>(json)
				?? throw new InvalidDataException("uSMART returned an invalid tick update.");
			await Invoke(TickReceived, message.Topic, tick, cancellationToken);
		}
		else if (message.Topic.StartsWith("ob.", StringComparison.OrdinalIgnoreCase))
		{
			var depth = JsonConvert.DeserializeObject<UsmartDepthLevel[]>(json)
				?? throw new InvalidDataException("uSMART returned an invalid order-book update.");
			await Invoke(DepthReceived, message.Topic, depth, cancellationToken);
		}
	}

	private Task SendPong(ClientWebSocket socket, UsmartSocketMessage ping,
		CancellationToken cancellationToken)
		=> Send(socket, new UsmartSocketRequest
		{
			Operation = UsmartSocketOperations.Pong,
			Timestamp = ping.Timestamp > 0 ? ping.Timestamp : GetUnixSeconds(),
			RequestId = ping.RequestId > 0 ? ping.RequestId : NextRequestId(),
		}, cancellationToken);

	private async Task SendTopics(ClientWebSocket socket, UsmartSocketOperations operation,
		string[] topics, CancellationToken cancellationToken)
	{
		if (topics.Length is < 1 or > 10)
			throw new ArgumentOutOfRangeException(nameof(topics), topics.Length,
				"uSMART accepts between one and ten topics per request.");
		await ThrottleTopics(topics.Length, cancellationToken);
		await Send(socket, new UsmartSocketRequest
		{
			Operation = operation,
			Timestamp = GetUnixSeconds(),
			RequestId = NextRequestId(),
			Topics = topics,
		}, cancellationToken);
	}

	private async Task ThrottleTopics(int count, CancellationToken cancellationToken)
	{
		await _topicRateLock.WaitAsync(cancellationToken);
		try
		{
			var now = DateTime.UtcNow;
			if (_topicWindow == default || now - _topicWindow >= TimeSpan.FromSeconds(1))
			{
				_topicWindow = now;
				_topicsInWindow = 0;
			}
			if (_topicsInWindow + count > 10)
			{
				await Task.Delay(TimeSpan.FromSeconds(1) - (now - _topicWindow),
					cancellationToken);
				_topicWindow = DateTime.UtcNow;
				_topicsInWindow = 0;
			}
			_topicsInWindow += count;
		}
		finally
		{
			_topicRateLock.Release();
		}
	}

	private async Task Send(ClientWebSocket socket, UsmartSocketRequest request,
		CancellationToken cancellationToken)
	{
		var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request,
			new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
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

	private static async Task<string> ReceiveText(ClientWebSocket socket,
		CancellationToken cancellationToken)
	{
		var buffer = new byte[16 * 1024];
		using var stream = new MemoryStream();
		while (true)
		{
			var result = await socket.ReceiveAsync(buffer, cancellationToken);
			if (result.MessageType == WebSocketMessageType.Close)
				throw new WebSocketException(
					$"uSMART WebSocket closed: {result.CloseStatus} {result.CloseStatusDescription}");
			if (result.MessageType != WebSocketMessageType.Text)
				throw new InvalidDataException(
					$"Unexpected uSMART WebSocket message type {result.MessageType}.");
			stream.Write(buffer, 0, result.Count);
			if (stream.Length > _maxMessageSize)
				throw new InvalidDataException("uSMART WebSocket message exceeds 4 MiB.");
			if (result.EndOfMessage)
				return Encoding.UTF8.GetString(stream.GetBuffer(), 0,
					checked((int)stream.Length));
		}
	}

	private static UsmartSocketMessage Deserialize(string content)
		=> JsonConvert.DeserializeObject<UsmartSocketMessage>(content)
			?? throw new InvalidDataException("uSMART returned an invalid WebSocket message.");

	private static string Decode(string content)
	{
		var normalized = content.Replace('-', '+').Replace('_', '/');
		normalized = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
		return Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
	}

	private long NextRequestId() => Interlocked.Increment(ref _requestId);

	private static long GetUnixSeconds()
		=> (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;

	private static ValueTask Invoke<T>(Func<T, CancellationToken, ValueTask> handler,
		T value, CancellationToken cancellationToken)
		=> handler == null ? default : handler(value, cancellationToken);

	private static ValueTask Invoke<T1, T2>(
		Func<T1, T2, CancellationToken, ValueTask> handler, T1 value1, T2 value2,
		CancellationToken cancellationToken)
		=> handler == null ? default : handler(value1, value2, cancellationToken);

	protected override void DisposeManaged()
	{
		Disconnect().GetAwaiter().GetResult();
		_cancellation.Dispose();
		_sendLock.Dispose();
		_topicRateLock.Dispose();
		base.DisposeManaged();
	}
}
