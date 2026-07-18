namespace StockSharp.ThetaData.Native;

sealed class ThetaDataWebSocketClient : BaseLogReceiver
{
	private const int _maxMessageSize = 4 * 1024 * 1024;

	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly Uri _uri;
	private readonly int _maxAttempts;
	private readonly CancellationTokenSource _cancellation = new();
	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly HashSet<ThetaStreamKey> _subscriptions = [];
	private readonly HashSet<ThetaStreamKey> _activeSubscriptions = [];
	private readonly Dictionary<long, TaskCompletionSource<string>> _pending = [];
	private readonly Lock _sync = new();

	private ClientWebSocket _socket;
	private Task _runTask;
	private TaskCompletionSource _ready = CreateCompletionSource();
	private long _requestId;

	public ThetaDataWebSocketClient(Uri uri, int maxAttempts)
	{
		_uri = uri ?? throw new ArgumentNullException(nameof(uri));
		_maxAttempts = Math.Max(1, maxAttempts);
	}

	public override string Name => "ThetaData_WebSocket";

	public event Func<ThetaStreamMessage, CancellationToken, ValueTask> DataReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	public async Task Subscribe(ThetaStreamKey key, CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
		{
			if (!_subscriptions.Add(key))
				return;
			_runTask ??= Run(_cancellation.Token);
		}

		try
		{
			await WaitUntilReady(cancellationToken);
			ClientWebSocket socket;
			using (_sync.EnterScope())
			{
				if (_activeSubscriptions.Contains(key))
					return;
				socket = _socket;
			}
			if (socket?.State != WebSocketState.Open)
				throw new WebSocketException("ThetaData WebSocket is not connected.");
			var response = await SendRequest(socket, key, true, cancellationToken);
			EnsureSubscribed(response, key);
			using (_sync.EnterScope())
				_activeSubscriptions.Add(key);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_subscriptions.Remove(key);
				_activeSubscriptions.Remove(key);
			}
			throw;
		}
	}

	public async Task Unsubscribe(ThetaStreamKey key, CancellationToken cancellationToken)
	{
		ClientWebSocket socket;
		var wasActive = false;
		using (_sync.EnterScope())
		{
			if (!_subscriptions.Remove(key))
				return;
			wasActive = _activeSubscriptions.Remove(key);
			socket = _socket;
		}
		if (!wasActive || socket?.State != WebSocketState.Open)
			return;
		try
		{
			var response = await SendRequest(socket, key, false, cancellationToken);
			if (response.EqualsIgnoreCase("ERROR") ||
				response.EqualsIgnoreCase("INVALID_PERMS"))
			{
				throw new InvalidOperationException(
					$"ThetaData stream unsubscribe failed: {response}.");
			}
		}
		catch (WebSocketException)
		{
		}
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

	private async Task WaitUntilReady(CancellationToken cancellationToken)
	{
		Task task;
		using (_sync.EnterScope())
			task = _ready.Task;
		await task.WaitAsync(cancellationToken);
	}

	private async Task Run(CancellationToken cancellationToken)
	{
		var failures = 0;
		while (!cancellationToken.IsCancellationRequested)
		{
			Task receiveTask = null;
			try
			{
				using var socket = new ClientWebSocket();
				socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
				await socket.ConnectAsync(_uri, cancellationToken);
				ThetaStreamKey[] subscriptions;
				using (_sync.EnterScope())
				{
					_socket = socket;
					_activeSubscriptions.Clear();
					subscriptions = [.. _subscriptions];
				}
				if (subscriptions.Length == 0)
					break;

				receiveTask = ReceiveLoop(socket, cancellationToken);
				foreach (var key in subscriptions)
				{
					try
					{
						var response = await SendRequest(socket, key, true, cancellationToken);
						EnsureSubscribed(response, key);
						using (_sync.EnterScope())
							_activeSubscriptions.Add(key);
					}
					catch (InvalidOperationException error)
					{
						using (_sync.EnterScope())
							_subscriptions.Remove(key);
						await Invoke(Error, error, cancellationToken);
					}
				}

				failures = 0;
				using (_sync.EnterScope())
					_ready.TrySetResult();
				await receiveTask;
				if (!cancellationToken.IsCancellationRequested)
					throw new WebSocketException("ThetaData WebSocket closed unexpectedly.");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception error)
			{
				failures++;
				FailPending(error);
				await Invoke(Error, error, cancellationToken);
				if (failures > _maxAttempts)
				{
					using (_sync.EnterScope())
					{
						if (_ready.Task.IsCompleted)
							_ready = CreateCompletionSource();
						_ready.TrySetException(error);
					}
					break;
				}
				await Task.Delay(TimeSpan.FromSeconds(Math.Min(30,
					1 << Math.Min(failures, 5))), cancellationToken);
			}
			finally
			{
				using (_sync.EnterScope())
				{
					_socket = null;
					_activeSubscriptions.Clear();
					if (_ready.Task.IsCompleted && failures <= _maxAttempts &&
						!cancellationToken.IsCancellationRequested)
						_ready = CreateCompletionSource();
				}
			}
		}

		if (!_ready.Task.IsCompleted)
			_ready.TrySetCanceled(cancellationToken);
	}

	private async Task ReceiveLoop(ClientWebSocket socket,
		CancellationToken cancellationToken)
	{
		while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
		{
			var content = await ReceiveText(socket, cancellationToken);
			ThetaStreamMessage message;
			try
			{
				message = JsonConvert.DeserializeObject<ThetaStreamMessage>(content,
					_jsonSettings) ?? throw new InvalidDataException(
						"Empty ThetaData WebSocket message.");
			}
			catch (JsonException error)
			{
				throw new InvalidDataException("Invalid ThetaData WebSocket message.", error);
			}

			var header = message.Header;
			if (header?.Type.EqualsIgnoreCase("REQ_RESPONSE") == true &&
				header.RequestId != null)
			{
				TaskCompletionSource<string> completion;
				using (_sync.EnterScope())
					_pending.TryGetValue(header.RequestId.Value, out completion);
				completion?.TrySetResult(header.Response);
				continue;
			}
			if (header?.Type.EqualsIgnoreCase("STATUS") == true)
				continue;
			if (message.Contract != null &&
				(message.Trade != null || message.Quote != null || message.Ohlc != null))
			{
				await Invoke(DataReceived, message, cancellationToken);
			}
		}
	}

	private async Task<string> SendRequest(ClientWebSocket socket, ThetaStreamKey key,
		bool isSubscribe, CancellationToken cancellationToken)
	{
		var id = Interlocked.Increment(ref _requestId);
		var completion = new TaskCompletionSource<string>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		using (_sync.EnterScope())
			_pending.Add(id, completion);
		try
		{
			await Send(socket, new ThetaStreamRequest
			{
				MessageType = "STREAM",
				SecurityType = key.Security.Market.ToNative().ToUpperInvariant(),
				RequestType = key.Type.ToNative(),
				IsAdd = isSubscribe,
				Id = id,
				Contract = key.Security.ToStreamContract(),
			}, cancellationToken);
			return await completion.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
		}
		finally
		{
			using (_sync.EnterScope())
				_pending.Remove(id);
		}
	}

	private async Task Send<T>(ClientWebSocket socket, T request,
		CancellationToken cancellationToken)
		where T : class
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

	private static async Task<string> ReceiveText(ClientWebSocket socket,
		CancellationToken cancellationToken)
	{
		var buffer = new byte[16 * 1024];
		using var stream = new MemoryStream();
		while (true)
		{
			var result = await socket.ReceiveAsync(buffer, cancellationToken);
			if (result.MessageType == WebSocketMessageType.Close)
				throw new WebSocketException($"ThetaData WebSocket closed: " +
					$"{result.CloseStatus} {result.CloseStatusDescription}");
			if (result.MessageType != WebSocketMessageType.Text)
				throw new InvalidDataException(
					$"Unexpected ThetaData WebSocket message type {result.MessageType}.");
			stream.Write(buffer, 0, result.Count);
			if (stream.Length > _maxMessageSize)
				throw new InvalidDataException(
					"ThetaData WebSocket message exceeds 4 MiB.");
			if (result.EndOfMessage)
				return Encoding.UTF8.GetString(stream.GetBuffer(), 0,
					checked((int)stream.Length));
		}
	}

	private void FailPending(Exception error)
	{
		TaskCompletionSource<string>[] pending;
		using (_sync.EnterScope())
			pending = [.. _pending.Values];
		foreach (var completion in pending)
			completion.TrySetException(error);
	}

	private static void EnsureSubscribed(string response, ThetaStreamKey key)
	{
		if (!response.EqualsIgnoreCase("SUBSCRIBED"))
		{
			throw new InvalidOperationException(
				$"ThetaData rejected {key.Security.Root} {key.Type} stream: " +
				response.IsEmpty("unknown response") + ".");
		}
	}

	private static TaskCompletionSource CreateCompletionSource()
		=> new(TaskCreationOptions.RunContinuationsAsynchronously);

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
