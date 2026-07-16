namespace StockSharp.CQG.Native;

sealed class CqgWebSocketClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly Logon _logon;
	private readonly int _reconnectAttempts;
	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly object _sync = new();
	private CancellationTokenSource _cancellation;
	private ClientWebSocket _socket;
	private Task _runTask;
	private TaskCompletionSource _firstConnection;

	public CqgWebSocketClient(string endpoint, Logon logon, int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint));
		_logon = logon ?? throw new ArgumentNullException(nameof(logon));
		_reconnectAttempts = Math.Max(1, reconnectAttempts);
	}

	public event Func<ServerMsg, CancellationToken, ValueTask> MessageReceived;
	public event Func<CancellationToken, ValueTask> Connected;
	public event Func<Exception, ValueTask> Error;

	public DateTime BaseTime { get; private set; }

	public async Task Connect(CancellationToken cancellationToken)
	{
		lock (_sync)
		{
			if (_runTask != null)
				throw new InvalidOperationException("CQG WebSocket is already running.");
			_cancellation = new();
			_firstConnection = new(TaskCreationOptions.RunContinuationsAsynchronously);
			_runTask = Run(_cancellation.Token);
		}
		await _firstConnection.Task.WaitAsync(cancellationToken);
	}

	public async Task Disconnect()
	{
		Task runTask;
		ClientWebSocket socket;
		lock (_sync)
		{
			runTask = _runTask;
			socket = _socket;
			_cancellation?.Cancel();
		}
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
		if (runTask != null)
			await runTask;
		lock (_sync)
		{
			_runTask = null;
			_socket = null;
			_cancellation?.Dispose();
			_cancellation = null;
		}
	}

	public Task Send(ClientMsg message, CancellationToken cancellationToken)
		=> Send(message, GetSocket(), cancellationToken);

	private ClientWebSocket GetSocket()
	{
		lock (_sync)
			return _socket?.State == WebSocketState.Open
				? _socket
				: throw new InvalidOperationException("CQG WebSocket is not connected.");
	}

	private async Task Run(CancellationToken cancellationToken)
	{
		var attempt = 0;
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				using var socket = new ClientWebSocket();
				socket.Options.KeepAliveInterval = TimeSpan.Zero;
				lock (_sync)
					_socket = socket;
				await socket.ConnectAsync(new Uri(_endpoint), cancellationToken);
				await Send(new ClientMsg { Logon = _logon.Clone() }, socket, cancellationToken);
				await ReceiveLogon(socket, cancellationToken);
				attempt = 0;
				if (Connected != null)
					await Connected(cancellationToken);
				_firstConnection.TrySetResult();
				await ReceiveLoop(socket, cancellationToken);
				if (!cancellationToken.IsCancellationRequested)
					throw new WebSocketException("CQG WebSocket closed unexpectedly.");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception ex)
			{
				if (!_firstConnection.Task.IsCompleted && ++attempt >= _reconnectAttempts)
				{
					_firstConnection.TrySetException(ex);
					break;
				}
				if (Error != null)
					await Error(ex);
				var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Max(0, attempt - 1))));
				try
				{
					await Task.Delay(delay, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					break;
				}
			}
		}
		_firstConnection.TrySetCanceled(cancellationToken);
	}

	private async Task ReceiveLogon(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		while (true)
		{
			var message = await Receive(socket, cancellationToken)
				?? throw new WebSocketException("CQG closed the connection during logon.");
			if (message.Ping != null)
				await SendPong(message.Ping, socket, cancellationToken);
			if (message.LogonResult == null)
				continue;
			var result = message.LogonResult;
			if (result.ResultCode != 0)
			{
				var redirect = result.RedirectUrl.IsEmpty() ? string.Empty : $" Redirect: {result.RedirectUrl}.";
				throw new InvalidOperationException($"CQG logon failed ({result.ResultCode}): {result.TextMessage}.{redirect}");
			}
			if (!DateTime.TryParse(result.BaseTime, CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var baseTime))
				throw new InvalidOperationException($"CQG returned an invalid base time '{result.BaseTime}'.");
			BaseTime = baseTime;
			return;
		}
	}

	private async Task ReceiveLoop(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
		{
			var message = await Receive(socket, cancellationToken);
			if (message == null)
				return;
			if (message.Ping != null)
				await SendPong(message.Ping, socket, cancellationToken);
			if (message.LoggedOff != null)
				throw new InvalidOperationException($"CQG logged the session off: {message.LoggedOff.TextMessage}");
			if (MessageReceived != null)
				await MessageReceived(message, cancellationToken);
		}
	}

	private Task SendPong(Ping ping, ClientWebSocket socket, CancellationToken cancellationToken)
		=> Send(new ClientMsg
		{
			Pong = new()
			{
				Token = ping.Token,
				PingUtcTime = ping.PingUtcTime,
				PongUtcTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
			},
		}, socket, cancellationToken);

	private async Task Send(ClientMsg message, ClientWebSocket socket, CancellationToken cancellationToken)
	{
		var data = message.ToByteArray();
		await _sendLock.WaitAsync(cancellationToken);
		try
		{
			await socket.SendAsync(data, WebSocketMessageType.Binary, true, cancellationToken);
		}
		finally
		{
			_sendLock.Release();
		}
	}

	private static async Task<ServerMsg> Receive(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		using var stream = new MemoryStream();
		var buffer = new byte[64 * 1024];
		while (true)
		{
			var result = await socket.ReceiveAsync(buffer, cancellationToken);
			if (result.MessageType == WebSocketMessageType.Close)
				return null;
			if (result.MessageType != WebSocketMessageType.Binary)
				throw new InvalidOperationException("CQG Web API returned a non-binary WebSocket message.");
			stream.Write(buffer, 0, result.Count);
			if (stream.Length > 32 * 1024 * 1024)
				throw new InvalidOperationException("CQG WebSocket message exceeded the 32 MB safety limit.");
			if (result.EndOfMessage)
				return ServerMsg.Parser.ParseFrom(stream.ToArray());
		}
	}

	protected override void DisposeManaged()
	{
		_cancellation?.Cancel();
		_socket?.Dispose();
		_sendLock.Dispose();
		_cancellation?.Dispose();
		base.DisposeManaged();
	}
}
