namespace StockSharp.Longbridge.Native;

sealed class LongbridgePacket
{
	public byte Type { get; init; }
	public byte Command { get; init; }
	public uint RequestId { get; init; }
	public byte Status { get; init; }
	public byte[] Body { get; init; }
}

sealed class LongbridgeSocketClient : BaseLogReceiver
{
	private const int _maxFrameSize = 16 * 1024 * 1024;
	private readonly Uri _socketUri;
	private readonly Func<CancellationToken, Task<string>> _otpProvider;
	private readonly CancellationTokenSource _lifetime = new();
	private readonly TaskCompletionSource _initialConnection = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly SemaphoreSlim _sendGate = new(1, 1);
	private readonly ConcurrentDictionary<uint, TaskCompletionSource<LongbridgePacket>> _pending = new();
	private TaskCompletionSource<ClientWebSocket> _ready = NewReadySource();
	private ClientWebSocket _socket;
	private Task _runTask;
	private int _requestId;

	public LongbridgeSocketClient(string url, Func<CancellationToken, Task<string>> otpProvider)
	{
		var builder = new UriBuilder(url.ThrowIfEmpty(nameof(url)));
		builder.Query = "version=1&codec=1&platform=9";
		_socketUri = builder.Uri;
		_otpProvider = otpProvider ?? throw new ArgumentNullException(nameof(otpProvider));
	}

	public event Func<CancellationToken, ValueTask> Connected;
	public event Func<LongbridgePacket, CancellationToken, ValueTask> PushReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	public async Task Connect(CancellationToken cancellationToken)
	{
		if (_runTask != null)
			throw new InvalidOperationException("Longbridge socket is already running.");
		_runTask = Run(_lifetime.Token);
		await _initialConnection.Task.WaitAsync(cancellationToken);
	}

	public async Task<TResponse> Request<TResponse>(byte command, ProtobufMessage body, MessageParser<TResponse> parser,
		CancellationToken cancellationToken)
		where TResponse : IMessage<TResponse>
	{
		var packet = await Request(command, body, cancellationToken);
		return parser.ParseFrom(packet.Body);
	}

	public async Task Disconnect()
	{
		_lifetime.Cancel();
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

	private async Task<LongbridgePacket> Request(byte command, ProtobufMessage body, CancellationToken cancellationToken)
	{
		var socket = await _ready.Task.WaitAsync(cancellationToken);
		var requestId = unchecked((uint)Interlocked.Increment(ref _requestId));
		if (requestId == 0)
			requestId = unchecked((uint)Interlocked.Increment(ref _requestId));
		var source = new TaskCompletionSource<LongbridgePacket>(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!_pending.TryAdd(requestId, source))
			throw new InvalidOperationException($"Duplicate Longbridge request id {requestId}.");
		try
		{
			var payload = body?.ToByteArray() ?? [];
			var frame = new byte[11 + payload.Length];
			frame[0] = 1;
			frame[1] = command;
			BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(2, 4), requestId);
			BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(6, 2), 10_000);
			WriteLength(frame.AsSpan(8, 3), payload.Length);
			payload.CopyTo(frame, 11);
			await _sendGate.WaitAsync(cancellationToken);
			try
			{
				await socket.SendAsync(frame, WebSocketMessageType.Binary, true, cancellationToken);
			}
			finally
			{
				_sendGate.Release();
			}
			var response = await source.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
			if (response.Status != 0)
			{
				var error = LongbridgeControlError.Parser.ParseFrom(response.Body);
				throw new InvalidOperationException($"Longbridge socket error {response.Status}/" +
					$"{error.Code.ToString(CultureInfo.InvariantCulture)}: {error.Msg}");
			}
			return response;
		}
		finally
		{
			_pending.TryRemove(requestId, out _);
		}
	}

	private async Task Run(CancellationToken cancellationToken)
	{
		var attempts = 0;
		var connectedOnce = false;
		while (!cancellationToken.IsCancellationRequested)
		{
			var ready = _ready;
			try
			{
				using var socket = new ClientWebSocket();
				using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				_socket = socket;
				await socket.ConnectAsync(_socketUri, connection.Token);
				var receiveTask = Receive(socket, connection.Token);
				ready.TrySetResult(socket);
				var otp = (await _otpProvider(connection.Token)).ThrowIfEmpty("OTP");
				await Request((byte)LongbridgeControlCommand.CmdAuth, new AuthRequest { Token = otp },
					AuthResponse.Parser, connection.Token);
				attempts = 0;
				connectedOnce = true;
				if (Connected != null)
					await Connected(connection.Token);
				_initialConnection.TrySetResult();
				var heartbeatTask = Heartbeat(connection.Token);
				var completed = await Task.WhenAny(receiveTask, heartbeatTask);
				if (completed == heartbeatTask)
					await heartbeatTask;
				connection.Cancel();
				try
				{
					await heartbeatTask;
				}
				catch (OperationCanceledException) when (connection.IsCancellationRequested)
				{
				}
				if (!cancellationToken.IsCancellationRequested)
					throw new WebSocketException("Longbridge socket closed unexpectedly.");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception ex)
			{
				attempts++;
				ready.TrySetException(ex);
				if (ReferenceEquals(_ready, ready))
					_ready = NewReadySource();
				FailPending(ex);
				if (Error != null)
					await Error(ex, CancellationToken.None);
				if (!connectedOnce && attempts >= 5)
				{
					_initialConnection.TrySetException(ex);
					break;
				}
				await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempts, 5))), cancellationToken);
			}
			finally
			{
				_socket = null;
			}
		}
		if (!_initialConnection.Task.IsCompleted)
			_initialConnection.TrySetCanceled(cancellationToken);
		FailPending(new OperationCanceledException("Longbridge socket stopped."));
	}

	private async Task Heartbeat(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested && _socket?.State == WebSocketState.Open)
		{
			await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken);
			await Request((byte)LongbridgeControlCommand.CmdHeartbeat, new LongbridgeHeartbeat
			{
				Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
			}, LongbridgeHeartbeat.Parser, cancellationToken);
		}
	}

	private async Task Receive(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		var buffer = new byte[64 * 1024];
		while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
		{
			using var stream = new MemoryStream();
			WebSocketReceiveResult result;
			do
			{
				result = await socket.ReceiveAsync(buffer, cancellationToken);
				if (result.MessageType == WebSocketMessageType.Close)
					return;
				if (result.MessageType != WebSocketMessageType.Binary)
					throw new InvalidDataException($"Unexpected Longbridge WebSocket message type {result.MessageType}.");
				stream.Write(buffer, 0, result.Count);
				if (stream.Length > _maxFrameSize)
					throw new InvalidDataException($"Longbridge frame exceeds {_maxFrameSize} bytes.");
			}
			while (!result.EndOfMessage);
			await ProcessFrame(stream.ToArray(), cancellationToken);
		}
	}

	private async ValueTask ProcessFrame(byte[] frame, CancellationToken cancellationToken)
	{
		if (frame.Length < 5)
			throw new InvalidDataException("Longbridge frame is shorter than its packet header.");
		var flags = frame[0];
		var type = (byte)(flags & 0x0f);
		var command = frame[1];
		var index = 2;
		uint requestId = 0;
		byte status = 0;
		if (type is 1 or 2)
		{
			if (frame.Length < (type == 1 ? 11 : 10))
				throw new InvalidDataException("Longbridge request/response header is incomplete.");
			requestId = BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(index, 4));
			index += 4;
			if (type == 1)
				index += 2;
			else
				status = frame[index++];
		}
		var bodyLength = ReadLength(frame.AsSpan(index, 3));
		index += 3;
		if (bodyLength < 0 || frame.Length - index < bodyLength)
			throw new InvalidDataException("Longbridge packet declares an invalid body length.");
		var body = frame.AsSpan(index, bodyLength).ToArray();
		if ((flags & 0x20) != 0)
			body = Decompress(body);
		var packet = new LongbridgePacket
		{
			Type = type,
			Command = command,
			RequestId = requestId,
			Status = status,
			Body = body,
		};
		if (type == 2)
		{
			if (_pending.TryGetValue(requestId, out var source))
				source.TrySetResult(packet);
			return;
		}
		if (type == 3 && PushReceived != null)
			await PushReceived(packet, cancellationToken);
	}

	private void FailPending(Exception error)
	{
		foreach (var source in _pending.Values)
			source.TrySetException(error);
		_pending.Clear();
	}

	private static TaskCompletionSource<ClientWebSocket> NewReadySource()
		=> new(TaskCreationOptions.RunContinuationsAsynchronously);

	private static void WriteLength(Span<byte> target, int length)
	{
		if (length is < 0 or > 0xFFFFFF)
			throw new ArgumentOutOfRangeException(nameof(length));
		target[0] = (byte)(length >> 16);
		target[1] = (byte)(length >> 8);
		target[2] = (byte)length;
	}

	private static int ReadLength(ReadOnlySpan<byte> source)
		=> source[0] << 16 | source[1] << 8 | source[2];

	private static byte[] Decompress(byte[] body)
	{
		using var input = new MemoryStream(body);
		using var gzip = new GZipStream(input, CompressionMode.Decompress);
		using var output = new MemoryStream();
		gzip.CopyTo(output);
		return output.ToArray();
	}

	protected override void DisposeManaged()
	{
		Disconnect().GetAwaiter().GetResult();
		_lifetime.Dispose();
		_sendGate.Dispose();
		base.DisposeManaged();
	}
}
