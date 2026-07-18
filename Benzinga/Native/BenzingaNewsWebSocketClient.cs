namespace StockSharp.Benzinga.Native;

sealed class BenzingaNewsWebSocketClient : BaseLogReceiver
{
	private const int _maxMessageSize = 8 * 1024 * 1024;
	private const int _deduplicationWindow = 4096;

	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly Uri _uri;
	private readonly int _maxAttempts;
	private readonly TimeSpan _heartbeatInterval;
	private readonly CancellationTokenSource _cancellation = new();
	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly TaskCompletionSource _initialConnection =
		new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly Lock _seenSync = new();
	private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
	private readonly Queue<string> _seenOrder = new();

	private ClientWebSocket _socket;
	private Task _runTask;

	public BenzingaNewsWebSocketClient(Uri address, string token, string channels,
		int maxAttempts, TimeSpan heartbeatInterval)
	{
		if (address == null)
			throw new ArgumentNullException(nameof(address));
		token.ThrowIfEmpty(nameof(token));
		if (heartbeatInterval <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(heartbeatInterval));

		_maxAttempts = Math.Max(1, maxAttempts);
		_heartbeatInterval = heartbeatInterval;
		var builder = new UriBuilder(address);
		var prefix = builder.Query.TrimStart('?');
		var query = new StringBuilder(prefix);
		if (query.Length > 0)
			query.Append('&');
		query.Append("token=").Append(Uri.EscapeDataString(token));
		if (!channels.IsEmpty())
			query.Append("&channels=").Append(Uri.EscapeDataString(channels));
		builder.Query = query.ToString();
		_uri = builder.Uri;
	}

	public override string Name => nameof(Benzinga) + "_" + nameof(BenzingaNewsWebSocketClient);
	public bool IsStopped => _runTask?.IsCompleted == true;

	public event Func<BenzingaNewsStreamData, CancellationToken, ValueTask> NewsReceived;
	public event Func<Exception, bool, CancellationToken, ValueTask> Error;

	public async Task Connect(CancellationToken cancellationToken)
	{
		if (_runTask != null)
			throw new InvalidOperationException("Benzinga News WebSocket is already running.");
		_runTask = Run(_cancellation.Token);
		await _initialConnection.Task.WaitAsync(cancellationToken);
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
			catch (ObjectDisposedException)
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
				using var socket = new ClientWebSocket();
				_socket = socket;
				socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
				await socket.ConnectAsync(_uri, cancellationToken);

				failures = 0;
				wasConnected = true;
				_initialConnection.TrySetResult();
				await RunConnection(socket, cancellationToken);
				if (!cancellationToken.IsCancellationRequested)
					throw new WebSocketException("Benzinga News WebSocket closed unexpectedly.");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception error)
			{
				failures++;
				var isTerminal = failures >= _maxAttempts;
				await Invoke(Error, error, isTerminal, CancellationToken.None);
				if (isTerminal)
				{
					if (!wasConnected)
						_initialConnection.TrySetException(error);
					break;
				}
				await Task.Delay(
					TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(failures, 5))),
					cancellationToken);
			}
			finally
			{
				_socket = null;
			}
		}

		if (!_initialConnection.Task.IsCompleted)
			_initialConnection.TrySetCanceled(cancellationToken);
	}

	private async Task RunConnection(ClientWebSocket socket,
		CancellationToken cancellationToken)
	{
		using var connectionCancellation =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var receiveTask = ReceiveLoop(socket, connectionCancellation.Token);
		var heartbeatTask = HeartbeatLoop(socket, connectionCancellation.Token);
		var completed = await Task.WhenAny(receiveTask, heartbeatTask);
		connectionCancellation.Cancel();
		try
		{
			await completed;
		}
		finally
		{
			var other = ReferenceEquals(completed, receiveTask) ? heartbeatTask : receiveTask;
			try
			{
				await other;
			}
			catch (OperationCanceledException) when (connectionCancellation.IsCancellationRequested)
			{
			}
		}
	}

	private async Task ReceiveLoop(ClientWebSocket socket,
		CancellationToken cancellationToken)
	{
		while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
		{
			var content = await ReceiveText(socket, cancellationToken);
			if (content.EqualsIgnoreCase("pong") || content.EqualsIgnoreCase("ping"))
				continue;

			BenzingaNewsStreamEnvelope envelope;
			try
			{
				envelope = JsonConvert.DeserializeObject<BenzingaNewsStreamEnvelope>(
					content, _jsonSettings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException("Benzinga News WebSocket returned invalid JSON.", error);
			}

			if (envelope == null)
				continue;
			if (!envelope.ErrorMessage.IsEmpty())
			{
				throw new InvalidOperationException(
					$"Benzinga WebSocket error '{envelope.ErrorType}': {envelope.ErrorMessage}");
			}
			if (!envelope.ApiVersion.EqualsIgnoreCase("websocket/v1") ||
				!envelope.Kind.EqualsIgnoreCase("news") || envelope.Data?.Content == null)
			{
				continue;
			}
			if (!Remember(envelope.Id))
				continue;
			await Invoke(NewsReceived, envelope.Data, cancellationToken);
		}
	}

	private async Task HeartbeatLoop(ClientWebSocket socket,
		CancellationToken cancellationToken)
	{
		using var timer = new PeriodicTimer(_heartbeatInterval);
		while (await timer.WaitForNextTickAsync(cancellationToken))
			await SendText(socket, "ping", cancellationToken);
	}

	private bool Remember(string id)
	{
		if (id.IsEmpty())
			return true;
		using var scope = _seenSync.EnterScope();
		if (!_seen.Add(id))
			return false;
		_seenOrder.Enqueue(id);
		while (_seenOrder.Count > _deduplicationWindow)
			_seen.Remove(_seenOrder.Dequeue());
		return true;
	}

	private async Task SendText(ClientWebSocket socket, string content,
		CancellationToken cancellationToken)
	{
		var bytes = Encoding.UTF8.GetBytes(content);
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
			{
				throw new WebSocketException($"Benzinga News WebSocket closed: " +
					$"{result.CloseStatus} {result.CloseStatusDescription}");
			}
			if (result.MessageType != WebSocketMessageType.Text)
			{
				throw new InvalidDataException(
					$"Unexpected Benzinga WebSocket message type {result.MessageType}.");
			}
			stream.Write(buffer, 0, result.Count);
			if (stream.Length > _maxMessageSize)
				throw new InvalidDataException("Benzinga WebSocket message exceeds 8 MiB.");
			if (result.EndOfMessage)
				return Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
		}
	}

	private static ValueTask Invoke(Func<BenzingaNewsStreamData, CancellationToken, ValueTask> handler,
		BenzingaNewsStreamData value, CancellationToken cancellationToken)
		=> handler == null ? default : handler(value, cancellationToken);

	private static ValueTask Invoke(Func<Exception, bool, CancellationToken, ValueTask> handler,
		Exception error, bool isTerminal, CancellationToken cancellationToken)
		=> handler == null ? default : handler(error, isTerminal, cancellationToken);

	protected override void DisposeManaged()
	{
		Disconnect().GetAwaiter().GetResult();
		_cancellation.Dispose();
		_sendLock.Dispose();
		base.DisposeManaged();
	}
}
