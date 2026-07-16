namespace StockSharp.MiraeSharekhan.Native;

internal sealed class MiraeSharekhanWebSocketClient : BaseLogReceiver
{
	private const int _maxMessageSize = 4 * 1024 * 1024;
	private const int _maxSymbols = 1000;

	private readonly Uri _uri;
	private readonly int _maxAttempts;
	private readonly CancellationTokenSource _cancellation = new();
	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly TaskCompletionSource _initialConnection =
		new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly Dictionary<string, string> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly object _subscriptionsSync = new();
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private ClientWebSocket _socket;
	private Task _runTask;
	private bool _binaryWarningSent;

	public MiraeSharekhanWebSocketClient(string apiKey, string accessToken, int maxAttempts)
	{
		apiKey.ThrowIfEmpty(nameof(apiKey));
		accessToken.ThrowIfEmpty(nameof(accessToken));
		_maxAttempts = Math.Max(1, maxAttempts);
		_uri = new("wss://stream.sharekhan.com/skstream/api/stream?ACCESS_TOKEN=" +
			Uri.EscapeDataString(accessToken) + "&API_KEY=" + Uri.EscapeDataString(apiKey));
	}

	public override string Name => nameof(MiraeSharekhan) + "_" + nameof(MiraeSharekhanWebSocketClient);

	public event Func<MiraeSharekhanStreamMessage, CancellationToken, ValueTask> FeedReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public async Task Connect(CancellationToken cancellationToken)
	{
		if (_runTask != null)
			throw new InvalidOperationException("Mirae Asset Sharekhan WebSocket is already running.");
		_runTask = Run(_cancellation.Token);
		await _initialConnection.Task.WaitAsync(cancellationToken);
	}

	public async Task SetSubscription(string streamKey, string mode,
		CancellationToken cancellationToken)
	{
		streamKey.ThrowIfEmpty(nameof(streamKey));
		string previous;
		lock (_subscriptionsSync)
		{
			_subscriptions.TryGetValue(streamKey, out previous);
			if (mode == null)
				_subscriptions.Remove(streamKey);
			else
			{
				if (previous == null && _subscriptions.Count >= _maxSymbols)
					throw new InvalidOperationException(
						"Mirae Asset Sharekhan supports at most 1000 symbols per WebSocket connection.");
				_subscriptions[streamKey] = mode;
			}
		}

		if (previous.EqualsIgnoreCase(mode))
			return;
		var socket = _socket;
		if (socket?.State != WebSocketState.Open)
			return;
		if (!previous.IsEmpty())
			await SendFeed(socket, "unsubscribe", previous, [streamKey], cancellationToken);
		if (!mode.IsEmpty())
			await SendFeed(socket, "feed", mode, [streamKey], cancellationToken);
	}

	public async Task Disconnect()
	{
		_cancellation.Cancel();
		var socket = _socket;
		if (socket?.State == WebSocketState.Open)
		{
			try
			{
				await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect",
					CancellationToken.None);
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
				socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
				await socket.ConnectAsync(_uri, cancellationToken);
				await RestoreSubscriptions(socket, cancellationToken);
				failures = 0;
				wasConnected = true;
				_initialConnection.TrySetResult();
				await Invoke(StateChanged, ConnectionStates.Connected, cancellationToken);
				await RunConnected(socket, cancellationToken);
				if (!cancellationToken.IsCancellationRequested)
					throw new WebSocketException("Mirae Asset Sharekhan WebSocket closed unexpectedly.");
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
				await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(failures, 5))),
					cancellationToken);
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

	private async Task RunConnected(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var heartbeat = HeartbeatLoop(socket, linked.Token);
		try
		{
			await ReceiveLoop(socket, cancellationToken);
		}
		finally
		{
			linked.Cancel();
			try
			{
				await heartbeat;
			}
			catch (OperationCanceledException)
			{
			}
		}
	}

	private async Task HeartbeatLoop(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
		{
			await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
			if (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
				await SendRaw(socket, "ping", cancellationToken);
		}
	}

	private async Task RestoreSubscriptions(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		await Send(socket, new MiraeSharekhanSocketRequest
		{
			Action = "subscribe",
			Key = ["feed"],
			Value = [string.Empty],
		}, cancellationToken);

		KeyValuePair<string, string>[] subscriptions;
		lock (_subscriptionsSync)
			subscriptions = [.. _subscriptions];
		foreach (var group in subscriptions.GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase))
			await SendFeed(socket, "feed", group.Key, group.Select(pair => pair.Key).ToArray(),
				cancellationToken);
	}

	private async Task ReceiveLoop(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
		{
			var message = await Receive(socket, cancellationToken);
			if (message.Type == WebSocketMessageType.Text)
				await ProcessPayload(Encoding.UTF8.GetString(message.Data), cancellationToken);
			else if (message.Type == WebSocketMessageType.Binary)
			{
				var payload = Encoding.UTF8.GetString(message.Data);
				if (payload.TrimStart().StartsWith('{') || payload.TrimStart().StartsWith('['))
					await ProcessPayload(payload, cancellationToken);
				else if (!_binaryWarningSent)
				{
					_binaryWarningSent = true;
					await Invoke(Error, new InvalidDataException(
						"Sharekhan sent an undocumented binary market-data frame. The official SDK does not publish its decoder."),
						cancellationToken);
				}
			}
		}
	}

	private async ValueTask ProcessPayload(string payload, CancellationToken cancellationToken)
	{
		if (payload.IsEmpty() || payload.EqualsIgnoreCase("pong") || payload.EqualsIgnoreCase("ping"))
			return;
		try
		{
			if (payload.TrimStart().StartsWith("[", StringComparison.Ordinal))
			{
				foreach (var message in JsonConvert.DeserializeObject<MiraeSharekhanStreamMessage[]>(payload,
					_jsonSettings) ?? [])
					await Invoke(FeedReceived, message, cancellationToken);
				return;
			}

			var envelope = JsonConvert.DeserializeObject<MiraeSharekhanStreamEnvelope>(payload, _jsonSettings);
			if (envelope == null)
				return;
			if (!envelope.Error.IsEmpty())
			{
				await Invoke(Error, new InvalidOperationException(envelope.Error), cancellationToken);
				return;
			}
			if (!envelope.GetStreamKey().IsEmpty())
				await Invoke(FeedReceived, envelope, cancellationToken);
			foreach (var message in envelope.GetMessages())
				await Invoke(FeedReceived, message, cancellationToken);
		}
		catch (JsonException ex)
		{
			await Invoke(Error, new InvalidDataException(
				"Cannot parse a Mirae Asset Sharekhan WebSocket message.", ex), cancellationToken);
		}
	}

	private Task SendFeed(ClientWebSocket socket, string action, string mode, string[] streamKeys,
		CancellationToken cancellationToken)
		=> Send(socket, new MiraeSharekhanSocketRequest
		{
			Action = action,
			Key = [mode],
			Value = [string.Join(',', streamKeys)],
		}, cancellationToken);

	private Task Send<T>(ClientWebSocket socket, T request, CancellationToken cancellationToken)
		=> SendRaw(socket, JsonConvert.SerializeObject(request, _jsonSettings), cancellationToken);

	private async Task SendRaw(ClientWebSocket socket, string payload,
		CancellationToken cancellationToken)
	{
		var bytes = Encoding.UTF8.GetBytes(payload);
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

	private static async Task<(WebSocketMessageType Type, byte[] Data)> Receive(ClientWebSocket socket,
		CancellationToken cancellationToken)
	{
		var buffer = new byte[16 * 1024];
		using var stream = new MemoryStream();
		WebSocketMessageType? type = null;
		while (true)
		{
			var result = await socket.ReceiveAsync(buffer, cancellationToken);
			if (result.MessageType == WebSocketMessageType.Close)
				throw new WebSocketException($"Mirae Asset Sharekhan WebSocket closed: " +
					$"{result.CloseStatus} {result.CloseStatusDescription}");
			if (type != null && type != result.MessageType)
				throw new InvalidDataException("Mirae Asset Sharekhan changed message type within a frame.");
			type = result.MessageType;
			stream.Write(buffer, 0, result.Count);
			if (stream.Length > _maxMessageSize)
				throw new InvalidDataException("Mirae Asset Sharekhan WebSocket message exceeds 4 MiB.");
			if (result.EndOfMessage)
				return (type.Value, stream.ToArray());
		}
	}

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
