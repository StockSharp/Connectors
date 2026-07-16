namespace StockSharp.Saxo.Native;

sealed class SaxoStreamingClient : BaseLogReceiver
{
	private const int _maxFrameSize = 16 * 1024 * 1024;
	private readonly Uri _streamUri;
	private readonly Func<string> _accessTokenProvider;
	private readonly int _maxAttempts;
	private readonly CancellationTokenSource _cancellation = new();
	private readonly TaskCompletionSource _initialConnection = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private ClientWebSocket _socket;
	private Task _runTask;
	private ulong _lastMessageId;
	private bool _terminal;

	public SaxoStreamingClient(SaxoEnvironments environment, Func<string> accessTokenProvider, int maxAttempts)
	{
		_streamUri = environment == SaxoEnvironments.Simulation
			? new("wss://sim-streaming.saxobank.com/sim/oapi/streaming/ws/connect")
			: new("wss://live-streaming.saxobank.com/oapi/streaming/ws/connect");
		_accessTokenProvider = accessTokenProvider ?? throw new ArgumentNullException(nameof(accessTokenProvider));
		_maxAttempts = Math.Max(1, maxAttempts);
		ContextId = "Saxo" + Guid.NewGuid().ToString("N");
	}

	public override string Name => nameof(Saxo) + "_" + nameof(SaxoStreamingClient);
	public string ContextId { get; }

	public event Func<string, string, CancellationToken, ValueTask> PayloadReceived;
	public event Func<SaxoResetSubscriptions, CancellationToken, ValueTask> SubscriptionsReset;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		Disconnect().GetAwaiter().GetResult();
		_cancellation.Dispose();
		base.DisposeManaged();
	}

	public async Task Connect(CancellationToken cancellationToken)
	{
		if (_runTask != null)
			throw new InvalidOperationException("Saxo streaming is already running.");
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
		var attempts = 0;
		var wasConnected = false;
		while (!cancellationToken.IsCancellationRequested && !_terminal)
		{
			try
			{
				await Invoke(StateChanged, ConnectionStates.Connecting, cancellationToken);
				using var socket = new ClientWebSocket();
				_socket = socket;
				socket.Options.SetRequestHeader("Authorization", $"Bearer {_accessTokenProvider().ThrowIfEmpty("AccessToken")}");
				var suffix = $"?contextId={Uri.EscapeDataString(ContextId)}";
				if (_lastMessageId > 0)
					suffix += $"&messageid={_lastMessageId.ToString(CultureInfo.InvariantCulture)}";
				await socket.ConnectAsync(new Uri(_streamUri + suffix), cancellationToken);
				attempts = 0;
				wasConnected = true;
				_initialConnection.TrySetResult();
				await Invoke(StateChanged, ConnectionStates.Connected, cancellationToken);
				await Receive(socket, cancellationToken);
				if (!cancellationToken.IsCancellationRequested && !_terminal)
					throw new WebSocketException("Saxo streaming connection closed unexpectedly.");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception ex)
			{
				attempts++;
				await Invoke(Error, ex, cancellationToken);
				await Invoke(StateChanged, ConnectionStates.Disconnected, cancellationToken);
				if (!wasConnected && attempts >= _maxAttempts)
				{
					_initialConnection.TrySetException(ex);
					break;
				}
				if (wasConnected && attempts >= _maxAttempts)
					break;
				await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempts, 5))), cancellationToken);
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

	private async Task Receive(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		var buffer = new byte[64 * 1024];
		while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested && !_terminal)
		{
			using var frame = new MemoryStream();
			WebSocketReceiveResult result;
			do
			{
				result = await socket.ReceiveAsync(buffer, cancellationToken);
				if (result.MessageType == WebSocketMessageType.Close)
					return;
				if (result.MessageType != WebSocketMessageType.Binary)
					throw new InvalidDataException($"Unexpected Saxo streaming message type {result.MessageType}.");
				frame.Write(buffer, 0, result.Count);
				if (frame.Length > _maxFrameSize)
					throw new InvalidDataException($"Saxo streaming frame exceeds {_maxFrameSize} bytes.");
			}
			while (!result.EndOfMessage);

			await ParseFrame(frame.ToArray(), cancellationToken);
		}
	}

	private async ValueTask ParseFrame(byte[] frame, CancellationToken cancellationToken)
	{
		var index = 0;
		while (index < frame.Length)
		{
			if (frame.Length - index < 16)
				throw new InvalidDataException("Incomplete Saxo streaming message header.");
			var messageId = BinaryPrimitives.ReadUInt64LittleEndian(frame.AsSpan(index, 8));
			index += 10;
			var referenceLength = frame[index++];
			if (frame.Length - index < referenceLength + 5)
				throw new InvalidDataException("Invalid Saxo streaming reference identifier length.");
			var referenceId = Encoding.ASCII.GetString(frame, index, referenceLength);
			index += referenceLength;
			var format = frame[index++];
			var payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(index, 4));
			index += 4;
			if (payloadLength > int.MaxValue || frame.Length - index < payloadLength)
				throw new InvalidDataException("Invalid Saxo streaming payload length.");
			if (format != 0)
				throw new InvalidDataException($"Unexpected Saxo streaming payload format {format}; JSON was requested.");
			var payload = Encoding.UTF8.GetString(frame, index, (int)payloadLength);
			index += (int)payloadLength;
			_lastMessageId = messageId;

			if (referenceId.EqualsIgnoreCase("_heartbeat"))
			{
				var batches = JsonConvert.DeserializeObject<SaxoHeartbeatBatch[]>(payload);
				foreach (var heartbeat in (batches ?? []).SelectMany(b => b.Heartbeats ?? []))
				{
					if (heartbeat.Reason is "SubscriptionTemporarilyDisabled" or "SubscriptionPermanentlyDisabled")
					{
						await Invoke(Error, new InvalidOperationException($"Saxo subscription " +
							$"'{heartbeat.OriginatingReferenceId}' is {heartbeat.Reason}."), cancellationToken);
					}
				}
				continue;
			}
			if (referenceId.EqualsIgnoreCase("_resetsubscriptions"))
			{
				var reset = JsonConvert.DeserializeObject<SaxoResetSubscriptions>(payload);
				await Invoke(SubscriptionsReset, reset ?? new(), cancellationToken);
				continue;
			}
			if (referenceId.EqualsIgnoreCase("_disconnect"))
			{
				var disconnect = JsonConvert.DeserializeObject<SaxoDisconnectControl>(payload);
				_terminal = true;
				await Invoke(Error, new InvalidOperationException(
					$"Saxo streaming server disconnected the session: {disconnect?.Reason.IsEmpty("unspecified reason")}"), cancellationToken);
				continue;
			}
			await Invoke(PayloadReceived, referenceId, payload, cancellationToken);
		}
	}

	private static ValueTask Invoke<T>(Func<T, CancellationToken, ValueTask> handler, T value,
		CancellationToken cancellationToken)
		=> handler == null ? default : handler(value, cancellationToken);

	private static ValueTask Invoke<T1, T2>(Func<T1, T2, CancellationToken, ValueTask> handler, T1 value1, T2 value2,
		CancellationToken cancellationToken)
		=> handler == null ? default : handler(value1, value2, cancellationToken);
}
