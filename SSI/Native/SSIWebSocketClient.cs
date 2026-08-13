namespace StockSharp.SSI.Native;

sealed class SSIWebSocketClient : IAsyncDisposable
{
	private readonly Uri _endpoint;
	private readonly string _authorization;
	private readonly ClientWebSocket _socket = new();
	private readonly CancellationTokenSource _source = new();
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private Task _reader;
	private Task _pinger;
	private int _faulted;

	public SSIWebSocketClient(string endpoint, string tokenType,
		string accessToken)
	{
		_endpoint = new(endpoint.ThrowIfEmpty(nameof(endpoint)));
		if (_endpoint.Scheme is not "ws" and not "wss")
			throw new ArgumentException(
				"SSI streaming endpoint must use ws or wss.",
				nameof(endpoint));
		_authorization =
			$"{tokenType.ThrowIfEmpty(nameof(tokenType))} " +
			accessToken.ThrowIfEmpty(nameof(accessToken));
	}

	public bool IsConnected
		=> Volatile.Read(ref _faulted) == 0 &&
			_socket.State == WebSocketState.Open;

	public event Func<JObject, CancellationToken, ValueTask>
		MessageReceived;

	public event Func<Exception, CancellationToken, ValueTask> Error;

	public async ValueTask ConnectAsync(
		CancellationToken cancellationToken)
	{
		_socket.Options.SetRequestHeader("Authorization",
			_authorization);
		_socket.Options.SetRequestHeader("Accept", "application/json");
		_socket.Options.SetRequestHeader("Content-Type",
			"application/json");
		_socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
		await _socket.ConnectAsync(_endpoint, cancellationToken);
		Volatile.Write(ref _faulted, 0);
		_reader = ReadLoopAsync(_source.Token);
		_pinger = PingLoopAsync(_source.Token);
	}

	public ValueTask SubscribeAsync(string channel,
		IEnumerable<string> topics, CancellationToken cancellationToken)
		=> SendRequestAsync("subscribe", channel, topics,
			cancellationToken);

	public ValueTask UnsubscribeAsync(string channel,
		IEnumerable<string> topics, CancellationToken cancellationToken)
		=> SendRequestAsync("unsubscribe", channel, topics,
			cancellationToken);

	private ValueTask SendRequestAsync(string method, string channel,
		IEnumerable<string> topics,
		CancellationToken cancellationToken)
		=> SendAsync(CreateRequest(method, channel, topics),
			cancellationToken);

	internal static JObject CreateRequest(string method, string channel,
		IEnumerable<string> topics)
		=> new()
		{
			["method"] = method,
			["channel"] = channel,
			["topics"] = new JArray(topics),
		};

	private async ValueTask SendAsync(JObject message,
		CancellationToken cancellationToken)
	{
		var data = Encoding.UTF8.GetBytes(message.ToString(
			Formatting.None));
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await _socket.SendAsync(data, WebSocketMessageType.Text,
				true, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async Task ReadLoopAsync(
		CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				var message = await ReceiveAsync(cancellationToken);
				if (message is not null && MessageReceived is not null)
					await MessageReceived.InvokeAsync(message, cancellationToken);
			}
		}
		catch (OperationCanceledException)
			when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception error)
		{
			Volatile.Write(ref _faulted, 1);
			if (Error is not null)
				await Error.InvokeAsync(error, CancellationToken.None);
		}
	}

	private async ValueTask<JObject> ReceiveAsync(
		CancellationToken cancellationToken)
	{
		using var stream = new MemoryStream();
		while (true)
		{
			var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
			try
			{
				var result = await _socket.ReceiveAsync(
					buffer.AsMemory(), cancellationToken);
				if (result.MessageType == WebSocketMessageType.Close)
					throw new EndOfStreamException(
						"SSI WebSocket was closed.");
				if (result.MessageType != WebSocketMessageType.Text)
					continue;
				stream.Write(buffer, 0, result.Count);
				if (!result.EndOfMessage)
					continue;
				try
				{
					return JObject.Parse(Encoding.UTF8.GetString(
						stream.ToArray()));
				}
				catch (JsonException error)
				{
					throw new InvalidDataException(
						"SSI WebSocket returned invalid JSON.", error);
				}
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(buffer);
			}
		}
	}

	private async Task PingLoopAsync(
		CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				await TimeSpan.FromSeconds(20).Delay(cancellationToken);
				await SendRequestAsync("ping_pong", "HEARTBEAT", [],
					cancellationToken);
			}
		}
		catch (OperationCanceledException)
			when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception error)
		{
			Volatile.Write(ref _faulted, 1);
			if (Error is not null)
				await Error.InvokeAsync(error, CancellationToken.None);
		}
	}

	public async ValueTask DisposeAsync()
	{
		Volatile.Write(ref _faulted, 1);
		_source.Cancel();
		try
		{
			if (_socket.State == WebSocketState.Open)
				await _socket.CloseAsync(
					WebSocketCloseStatus.NormalClosure,
					"Disconnect", CancellationToken.None);
		}
		catch
		{
		}
		try
		{
			if (_reader is not null)
				await _reader;
			if (_pinger is not null)
				await _pinger;
		}
		catch
		{
		}
		_socket.Dispose();
		_source.Dispose();
		_sendSync.Dispose();
	}
}
