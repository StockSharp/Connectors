namespace StockSharp.Fxcm.Native;

internal sealed class FxcmSocketClient : BaseLogReceiver
{
	private const int _maxMessageSize = 4 * 1024 * 1024;

	private readonly Uri _uri;
	private readonly int _maxAttempts;
	private readonly CancellationTokenSource _cancellation = new();
	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly TaskCompletionSource<string> _initialConnection =
		new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly JsonSerializer _serializer = JsonSerializer.Create(new()
	{
		DateParseHandling = DateParseHandling.DateTime,
		DateTimeZoneHandling = DateTimeZoneHandling.Utc,
		NullValueHandling = NullValueHandling.Ignore,
	});

	private ClientWebSocket _socket;
	private Task _runTask;

	public FxcmSocketClient(bool isDemo, string token, int maxAttempts)
	{
		token.ThrowIfEmpty(nameof(token));
		_maxAttempts = Math.Max(1, maxAttempts);
		var host = isDemo ? "api-demo.fxcm.com" : "api.fxcm.com";
		_uri = new($"wss://{host}/socket.io/?access_token={Uri.EscapeDataString(token)}&EIO=3&transport=websocket");
	}

	public override string Name => nameof(Fxcm) + "_" + nameof(FxcmSocketClient);

	public event Func<string, CancellationToken, ValueTask> SessionConnected;
	public event Func<FxcmPriceUpdate, CancellationToken, ValueTask> PriceReceived;
	public event Func<FxcmOrder, CancellationToken, ValueTask> OrderReceived;
	public event Func<FxcmPositionUpdate, CancellationToken, ValueTask> PositionReceived;
	public event Func<FxcmAccount, CancellationToken, ValueTask> AccountReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public async Task<string> Connect(CancellationToken cancellationToken)
	{
		if (_runTask != null)
			throw new InvalidOperationException("FXCM Socket.IO client is already running.");
		_runTask = Run(_cancellation.Token);
		return await _initialConnection.Task.WaitAsync(cancellationToken);
	}

	public async Task Disconnect()
	{
		_cancellation.Cancel();
		var socket = _socket;
		if (socket?.State == WebSocketState.Open)
		{
			try
			{
				await SendText(socket, "41", CancellationToken.None);
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
				await socket.ConnectAsync(_uri, cancellationToken);

				var sessionId = await Handshake(socket, cancellationToken);
				await Invoke(SessionConnected, sessionId, cancellationToken);
				failures = 0;
				wasConnected = true;
				_initialConnection.TrySetResult(sessionId);
				await Invoke(StateChanged, ConnectionStates.Connected, cancellationToken);
				await ReceiveLoop(socket, cancellationToken);

				if (!cancellationToken.IsCancellationRequested)
					throw new WebSocketException("FXCM Socket.IO connection closed unexpectedly.");
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

	private async Task<string> Handshake(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		var packet = await ReceiveText(socket, cancellationToken);
		if (!packet.StartsWith('0'))
			throw new InvalidDataException("FXCM Socket.IO did not return an Engine.IO handshake.");

		var open = JsonConvert.DeserializeObject<FxcmSocketOpen>(packet[1..]);
		if (open?.SessionId.IsEmpty() != false)
			throw new InvalidDataException("FXCM Socket.IO handshake contains no session id.");

		await SendText(socket, "40", cancellationToken);
		while (true)
		{
			packet = await ReceiveText(socket, cancellationToken);
			if (packet == "40" || packet.StartsWith("40{"))
				return open.SessionId;
			if (packet.StartsWith('2'))
			{
				await SendText(socket, "3" + packet[1..], cancellationToken);
				continue;
			}
			if (packet.StartsWith("44"))
				throw new InvalidOperationException("FXCM Socket.IO namespace connection failed: " + packet[2..]);
		}
	}

	private async Task ReceiveLoop(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
		{
			var frame = await ReceiveText(socket, cancellationToken);
			foreach (var packet in frame.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
			{
				if (packet.StartsWith('2'))
				{
					await SendText(socket, "3" + packet[1..], cancellationToken);
					continue;
				}
				if (packet.StartsWith("42"))
					await ProcessEvent(packet[2..], cancellationToken);
				else if (packet.StartsWith("44"))
					await Invoke(Error, new InvalidOperationException(
						"FXCM Socket.IO error: " + packet[2..]), cancellationToken);
			}
		}
	}

	private async ValueTask ProcessEvent(string json, CancellationToken cancellationToken)
	{
		using var stringReader = new StringReader(json);
		using var reader = new JsonTextReader(stringReader);
		if (!reader.Read() || reader.TokenType != JsonToken.StartArray || !reader.Read() ||
			reader.TokenType != JsonToken.String)
			throw new JsonSerializationException("FXCM Socket.IO event has an invalid envelope.");

		var eventName = (string)reader.Value;
		if (!reader.Read())
			throw new JsonSerializationException($"FXCM Socket.IO event '{eventName}' has no payload.");

		switch (eventName)
		{
			case FxcmModelNames.Order:
				await Invoke(OrderReceived, ReadPayload<FxcmOrder>(reader), cancellationToken);
				break;
			case FxcmModelNames.OpenPosition:
				await Invoke(PositionReceived, new()
				{
					Position = ReadPayload<FxcmPosition>(reader),
					IsClosed = false,
				}, cancellationToken);
				break;
			case FxcmModelNames.ClosedPosition:
				await Invoke(PositionReceived, new()
				{
					Position = ReadPayload<FxcmPosition>(reader),
					IsClosed = true,
				}, cancellationToken);
				break;
			case FxcmModelNames.Account:
				await Invoke(AccountReceived, ReadPayload<FxcmAccount>(reader), cancellationToken);
				break;
			default:
				var price = ReadPayload<FxcmPriceUpdate>(reader);
				if (price != null)
				{
					price.Symbol = price.Symbol.IsEmpty(eventName);
					await Invoke(PriceReceived, price, cancellationToken);
				}
				break;
		}
	}

	private T ReadPayload<T>(JsonReader reader)
	{
		if (reader.TokenType == JsonToken.String)
			return JsonConvert.DeserializeObject<T>((string)reader.Value);
		return _serializer.Deserialize<T>(reader);
	}

	private async Task SendText(ClientWebSocket socket, string text, CancellationToken cancellationToken)
	{
		var bytes = Encoding.UTF8.GetBytes(text);
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
				throw new WebSocketException($"FXCM Socket.IO closed: {result.CloseStatus} {result.CloseStatusDescription}");
			if (result.MessageType != WebSocketMessageType.Text)
				throw new InvalidDataException($"Unexpected FXCM Socket.IO message type {result.MessageType}.");
			stream.Write(buffer, 0, result.Count);
			if (stream.Length > _maxMessageSize)
				throw new InvalidDataException("FXCM Socket.IO message exceeds 4 MiB.");
			if (result.EndOfMessage)
				return Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
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
