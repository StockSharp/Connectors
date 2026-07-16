namespace StockSharp.Zerodha.Native;

internal sealed class ZerodhaWebSocketClient : BaseLogReceiver
{
	private const int _maxMessageSize = 4 * 1024 * 1024;
	private const int _maxTokens = 3000;

	private readonly Uri _uri;
	private readonly int _maxAttempts;
	private readonly CancellationTokenSource _cancellation = new();
	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly TaskCompletionSource _initialConnection =
		new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly Dictionary<long, string> _subscriptions = [];
	private readonly object _subscriptionsSync = new();
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private ClientWebSocket _socket;
	private Task _runTask;

	public ZerodhaWebSocketClient(string apiKey, string accessToken, int maxAttempts)
	{
		apiKey.ThrowIfEmpty(nameof(apiKey));
		accessToken.ThrowIfEmpty(nameof(accessToken));
		_maxAttempts = Math.Max(1, maxAttempts);
		_uri = new($"wss://ws.kite.trade?api_key={Uri.EscapeDataString(apiKey)}&access_token=" +
			Uri.EscapeDataString(accessToken));
	}

	public override string Name => nameof(Zerodha) + "_" + nameof(ZerodhaWebSocketClient);

	public event Func<KiteTick, CancellationToken, ValueTask> TickReceived;
	public event Func<KiteOrder, CancellationToken, ValueTask> OrderReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public async Task Connect(CancellationToken cancellationToken)
	{
		if (_runTask != null)
			throw new InvalidOperationException("Zerodha WebSocket is already running.");
		_runTask = Run(_cancellation.Token);
		await _initialConnection.Task.WaitAsync(cancellationToken);
	}

	public async Task Subscribe(long token, string mode, CancellationToken cancellationToken)
	{
		bool isNew;
		lock (_subscriptionsSync)
		{
			if (!_subscriptions.ContainsKey(token) && _subscriptions.Count >= _maxTokens)
				throw new InvalidOperationException("Zerodha WebSocket supports at most 3000 instruments.");
			isNew = !_subscriptions.ContainsKey(token);
			_subscriptions[token] = mode;
		}

		var socket = _socket;
		if (socket?.State != WebSocketState.Open)
			return;
		if (isNew)
			await SendTokens(socket, KiteSocketActions.Subscribe, [token], cancellationToken);
		await SendMode(socket, mode, [token], cancellationToken);
	}

	public async Task Unsubscribe(long token, CancellationToken cancellationToken)
	{
		lock (_subscriptionsSync)
		{
			if (!_subscriptions.Remove(token))
				return;
		}
		var socket = _socket;
		if (socket?.State == WebSocketState.Open)
			await SendTokens(socket, KiteSocketActions.Unsubscribe, [token], cancellationToken);
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
				socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
				socket.Options.SetRequestHeader("X-Kite-Version", "3");
				await socket.ConnectAsync(_uri, cancellationToken);
				await RestoreSubscriptions(socket, cancellationToken);
				failures = 0;
				wasConnected = true;
				_initialConnection.TrySetResult();
				await Invoke(StateChanged, ConnectionStates.Connected, cancellationToken);
				await ReceiveLoop(socket, cancellationToken);
				if (!cancellationToken.IsCancellationRequested)
					throw new WebSocketException("Zerodha WebSocket closed unexpectedly.");
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

	private async Task RestoreSubscriptions(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		KeyValuePair<long, string>[] subscriptions;
		lock (_subscriptionsSync)
			subscriptions = [.. _subscriptions];
		if (subscriptions.Length == 0)
			return;

		await SendTokens(socket, KiteSocketActions.Subscribe,
			subscriptions.Select(p => p.Key).ToArray(), cancellationToken);
		foreach (var group in subscriptions.GroupBy(p => p.Value, StringComparer.OrdinalIgnoreCase))
			await SendMode(socket, group.Key, group.Select(p => p.Key).ToArray(), cancellationToken);
	}

	private async Task ReceiveLoop(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
		{
			var message = await Receive(socket, cancellationToken);
			if (message.Type == WebSocketMessageType.Binary)
			{
				if (message.Data.Length <= 1)
					continue;
				foreach (var tick in ZerodhaBinaryParser.Parse(message.Data))
					await Invoke(TickReceived, tick, cancellationToken);
			}
			else if (message.Type == WebSocketMessageType.Text)
			{
				await ProcessText(Encoding.UTF8.GetString(message.Data), cancellationToken);
			}
		}
	}

	private async ValueTask ProcessText(string json, CancellationToken cancellationToken)
	{
		var type = JsonConvert.DeserializeObject<KiteSocketDiscriminator>(json, _jsonSettings)?.Type;
		if (type.EqualsIgnoreCase("order"))
		{
			var envelope = JsonConvert.DeserializeObject<KiteSocketOrderEnvelope>(json, _jsonSettings);
			if (envelope?.Data != null)
				await Invoke(OrderReceived, envelope.Data, cancellationToken);
		}
		else if (type.EqualsIgnoreCase("error"))
		{
			var envelope = JsonConvert.DeserializeObject<KiteSocketTextEnvelope>(json, _jsonSettings);
			await Invoke(Error, new InvalidOperationException(
				envelope?.Data.IsEmpty("Unknown Zerodha WebSocket error.")), cancellationToken);
		}
	}

	private Task SendTokens(ClientWebSocket socket, string action, long[] tokens,
		CancellationToken cancellationToken)
		=> Send(socket, new KiteSocketTokenRequest { Action = action, Tokens = tokens }, cancellationToken);

	private Task SendMode(ClientWebSocket socket, string mode, long[] tokens,
		CancellationToken cancellationToken)
		=> Send(socket, new KiteSocketModeRequest
		{
			Action = KiteSocketActions.Mode,
			Value = new() { Mode = mode, Tokens = tokens },
		}, cancellationToken);

	private async Task Send<T>(ClientWebSocket socket, T request, CancellationToken cancellationToken)
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
				throw new WebSocketException($"Zerodha WebSocket closed: {result.CloseStatus} " +
					result.CloseStatusDescription);
			if (type != null && type != result.MessageType)
				throw new InvalidDataException("Zerodha WebSocket changed message type within a frame.");
			type = result.MessageType;
			stream.Write(buffer, 0, result.Count);
			if (stream.Length > _maxMessageSize)
				throw new InvalidDataException("Zerodha WebSocket message exceeds 4 MiB.");
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
