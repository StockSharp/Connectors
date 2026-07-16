namespace StockSharp.LsegRealTime.Native;

internal sealed class LsegWebSocketConnection : IDisposable
{
	private const int _bufferSize = 64 * 1024;

	private static readonly JsonSerializerSettings _serializerSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly Func<int, LsegWireMessage, CancellationToken, ValueTask> _messageHandler;
	private readonly Func<int, Exception, ValueTask> _closedHandler;
	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private ClientWebSocket _socket;
	private CancellationTokenSource _cancellation;
	private Task _receiveLoop;
	private TaskCompletionSource<bool> _loginCompletion;
	private int _closedRaised;

	public LsegWebSocketConnection(
		int slot,
		Uri address,
		Func<int, LsegWireMessage, CancellationToken, ValueTask> messageHandler,
		Func<int, Exception, ValueTask> closedHandler)
	{
		Slot = slot;
		Address = address ?? throw new ArgumentNullException(nameof(address));
		_messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
		_closedHandler = closedHandler ?? throw new ArgumentNullException(nameof(closedHandler));
	}

	public int Slot { get; }
	public Uri Address { get; }
	public bool IsOpen => _socket?.State == WebSocketState.Open;

	public async Task ConnectAsync(LsegLoginRequest login, CancellationToken cancellationToken)
	{
		if (_socket != null)
			throw new InvalidOperationException("The LSEG WebSocket connection is already initialized.");

		Interlocked.Exchange(ref _closedRaised, 0);
		_cancellation = new CancellationTokenSource();
		_socket = new ClientWebSocket();
		_socket.Options.SetBuffer(_bufferSize, _bufferSize);
		_socket.Options.AddSubProtocol("tr_json2");
		_loginCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

		try
		{
			await _socket.ConnectAsync(Address, cancellationToken);
			_receiveLoop = Task.Run(() => ReceiveLoopAsync(_cancellation.Token));
			await SendAsync(login, cancellationToken);
			await _loginCompletion.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
		}
		catch
		{
			await DisconnectAsync();
			throw;
		}
	}

	public Task SendAsync(object message, CancellationToken cancellationToken)
	{
		if (!IsOpen)
			throw new InvalidOperationException("The LSEG WebSocket connection is not open.");
		var json = JsonConvert.SerializeObject(message, Formatting.None, _serializerSettings);
		return SendTextAsync(json, cancellationToken);
	}

	public async Task DisconnectAsync()
	{
		var cancellation = _cancellation;
		var socket = _socket;
		var receiveLoop = _receiveLoop;
		if (socket == null)
			return;

		cancellation?.Cancel();
		if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
		{
			try
			{
				await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", CancellationToken.None);
			}
			catch
			{
			}
		}

		if (receiveLoop != null && Task.CurrentId != receiveLoop.Id)
		{
			try
			{
				await receiveLoop;
			}
			catch (OperationCanceledException)
			{
			}
			catch
			{
			}
		}

		socket.Dispose();
		cancellation?.Dispose();
		_socket = null;
		_cancellation = null;
		_receiveLoop = null;
		_loginCompletion = null;
	}

	public void Dispose()
	{
		_cancellation?.Cancel();
		_socket?.Dispose();
		_cancellation?.Dispose();
		_sendLock.Dispose();
		_socket = null;
		_cancellation = null;
	}

	private async Task SendTextAsync(string json, CancellationToken cancellationToken)
	{
		var bytes = Encoding.UTF8.GetBytes(json);
		await _sendLock.WaitAsync(cancellationToken);
		try
		{
			await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
		}
		finally
		{
			_sendLock.Release();
		}
	}

	private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
	{
		Exception closeError = null;
		try
		{
			var buffer = new byte[_bufferSize];
			while (!cancellationToken.IsCancellationRequested && IsOpen)
			{
				using var content = new MemoryStream();
				WebSocketReceiveResult result;
				do
				{
					result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
					if (result.MessageType == WebSocketMessageType.Close)
						break;
					content.Write(buffer, 0, result.Count);
				}
				while (!result.EndOfMessage);

				if (result.MessageType == WebSocketMessageType.Close)
				{
					closeError = new WebSocketException($"LSEG closed the WebSocket: {result.CloseStatus} {result.CloseStatusDescription}");
					break;
				}
				if (result.MessageType != WebSocketMessageType.Text)
					continue;

				var json = Encoding.UTF8.GetString(content.GetBuffer(), 0, checked((int)content.Length));
				foreach (var message in DeserializeMessages(json))
					await ProcessMessageAsync(message, cancellationToken);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception error)
		{
			closeError = error;
		}
		finally
		{
			var error = closeError ?? new WebSocketException("The LSEG WebSocket connection was closed.");
			_loginCompletion?.TrySetException(error);
			if (!cancellationToken.IsCancellationRequested && Interlocked.Exchange(ref _closedRaised, 1) == 0)
				await _closedHandler(Slot, error);
		}
	}

	private async ValueTask ProcessMessageAsync(LsegWireMessage message, CancellationToken cancellationToken)
	{
		if (message == null)
			return;
		if (message.Type.EqualsIgnoreCase("Ping"))
		{
			await SendAsync(new LsegPongMessage(), cancellationToken);
			return;
		}

		if (message.Domain.EqualsIgnoreCase("Login"))
		{
			if (message.Type.EqualsIgnoreCase("Refresh") && (message.State == null || message.State.IsOpenAndOk))
				_loginCompletion?.TrySetResult(true);
			else if (message.Type.EqualsIgnoreCase("Status") || message.State?.Stream.EqualsIgnoreCase("Open") == false)
			{
				var error = CreateWireError(message, "LSEG login failed");
				if (_loginCompletion?.TrySetException(error) != true && Interlocked.Exchange(ref _closedRaised, 1) == 0)
				{
					_socket?.Abort();
					await _closedHandler(Slot, error);
				}
			}
			return;
		}

		await _messageHandler(Slot, message, cancellationToken);
	}

	private static LsegWireMessage[] DeserializeMessages(string json)
	{
		if (json.IsEmptyOrWhiteSpace())
			return [];
		var trimmed = json.TrimStart();
		return trimmed.StartsWith("[", StringComparison.Ordinal)
			? JsonConvert.DeserializeObject<LsegWireMessage[]>(json, _serializerSettings) ?? []
			: [JsonConvert.DeserializeObject<LsegWireMessage>(json, _serializerSettings)];
	}

	private static Exception CreateWireError(LsegWireMessage message, string prefix)
	{
		var detail = message.State?.Text.IsEmpty(message.Text)
			.IsEmpty(message.State?.Code)
			.IsEmpty("Unknown protocol error.");
		return new InvalidOperationException($"{prefix}: {detail}");
	}
}
