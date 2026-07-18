namespace StockSharp.Finnhub.Native;

sealed class FinnhubWebSocketClient : BaseLogReceiver
{
	private const int _maxMessageSize = 4 * 1024 * 1024;

	private readonly Uri _uri;
	private readonly int _maxAttempts;
	private readonly CancellationTokenSource _cancellation = new();
	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly TaskCompletionSource _initialConnection =
		new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly HashSet<string> _symbols = new(StringComparer.OrdinalIgnoreCase);
	private readonly object _symbolsSync = new();
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private ClientWebSocket _socket;
	private Task _runTask;

	public FinnhubWebSocketClient(Uri address, string token, int maxAttempts)
	{
		if (address == null)
			throw new ArgumentNullException(nameof(address));
		token.ThrowIfEmpty(nameof(token));
		_maxAttempts = Math.Max(1, maxAttempts);
		var builder = new UriBuilder(address);
		var prefix = builder.Query.TrimStart('?');
		builder.Query = (prefix.IsEmpty() ? string.Empty : prefix + "&") +
			"token=" + Uri.EscapeDataString(token);
		_uri = builder.Uri;
	}

	public override string Name => nameof(Finnhub) + "_" + nameof(FinnhubWebSocketClient);

	public event Func<FinnhubStreamTrade, CancellationToken, ValueTask> TradeReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public async Task Connect(CancellationToken cancellationToken)
	{
		if (_runTask != null)
			throw new InvalidOperationException("Finnhub WebSocket is already running.");
		_runTask = Run(_cancellation.Token);
		await _initialConnection.Task.WaitAsync(cancellationToken);
	}

	public async Task Subscribe(string symbol, CancellationToken cancellationToken)
	{
		symbol = symbol.ThrowIfEmpty(nameof(symbol));
		lock (_symbolsSync)
		{
			if (!_symbols.Add(symbol))
				return;
		}

		try
		{
			var socket = _socket;
			if (socket?.State == WebSocketState.Open)
				await SendSubscription(socket, symbol, true, cancellationToken);
		}
		catch
		{
			lock (_symbolsSync)
				_symbols.Remove(symbol);
			throw;
		}
	}

	public async Task Unsubscribe(string symbol, CancellationToken cancellationToken)
	{
		lock (_symbolsSync)
		{
			if (!_symbols.Remove(symbol))
				return;
		}

		var socket = _socket;
		if (socket?.State == WebSocketState.Open)
			await SendSubscription(socket, symbol, false, cancellationToken);
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
				await RestoreSubscriptions(socket, cancellationToken);

				failures = 0;
				wasConnected = true;
				_initialConnection.TrySetResult();
				await Invoke(StateChanged, ConnectionStates.Connected, cancellationToken);
				await ReceiveLoop(socket, cancellationToken);
				if (!cancellationToken.IsCancellationRequested)
					throw new WebSocketException("Finnhub WebSocket closed unexpectedly.");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception error)
			{
				failures++;
				await Invoke(Error, error, cancellationToken);
				await Invoke(StateChanged, ConnectionStates.Disconnected, cancellationToken);
				if (failures > _maxAttempts)
				{
					if (!wasConnected)
						_initialConnection.TrySetException(error);
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

	private async Task RestoreSubscriptions(ClientWebSocket socket,
		CancellationToken cancellationToken)
	{
		string[] symbols;
		lock (_symbolsSync)
			symbols = [.. _symbols];
		foreach (var symbol in symbols)
			await SendSubscription(socket, symbol, true, cancellationToken);
	}

	private async Task ReceiveLoop(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
		{
			var content = await ReceiveText(socket, cancellationToken);
			var envelope = JsonConvert.DeserializeObject<FinnhubStreamEnvelope>(content, _jsonSettings);
			if (envelope == null || envelope.Type.EqualsIgnoreCase("ping"))
				continue;
			if (envelope.Type.EqualsIgnoreCase("error"))
			{
				await Invoke(Error, new InvalidOperationException(
					envelope.Message.IsEmpty("Unknown Finnhub WebSocket error.")), cancellationToken);
				continue;
			}
			if (!envelope.Type.EqualsIgnoreCase("trade"))
				continue;

			foreach (var trade in envelope.Data ?? [])
			{
				if (trade != null)
					await Invoke(TradeReceived, trade, cancellationToken);
			}
		}
	}

	private Task SendSubscription(ClientWebSocket socket, string symbol, bool isSubscribe,
		CancellationToken cancellationToken)
		=> Send(socket, new FinnhubStreamRequest
		{
			Type = isSubscribe ? "subscribe" : "unsubscribe",
			Symbol = symbol,
		}, cancellationToken);

	private async Task Send(ClientWebSocket socket, FinnhubStreamRequest request,
		CancellationToken cancellationToken)
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
				throw new WebSocketException($"Finnhub WebSocket closed: {result.CloseStatus} " +
					result.CloseStatusDescription);
			if (result.MessageType != WebSocketMessageType.Text)
				throw new InvalidDataException(
					$"Unexpected Finnhub WebSocket message type {result.MessageType}.");
			stream.Write(buffer, 0, result.Count);
			if (stream.Length > _maxMessageSize)
				throw new InvalidDataException("Finnhub WebSocket message exceeds 4 MiB.");
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
