namespace StockSharp.FinancialModelingPrep.Native;

sealed class FmpWebSocketClient : BaseLogReceiver
{
	private const int _maxMessageSize = 4 * 1024 * 1024;

	private readonly FmpStreamKinds _kind;
	private readonly Uri _uri;
	private readonly string _token;
	private readonly int _maxAttempts;
	private readonly CancellationTokenSource _cancellation = new();
	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly TaskCompletionSource _initialConnection =
		new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly HashSet<string> _subscriptions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _activeSubscriptions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly object _subscriptionsSync = new();
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private ClientWebSocket _socket;
	private Task _runTask;

	public FmpWebSocketClient(FmpStreamKinds kind, Uri address, string token, int maxAttempts)
	{
		_kind = kind;
		_uri = address ?? throw new ArgumentNullException(nameof(address));
		_token = token.ThrowIfEmpty(nameof(token));
		_maxAttempts = Math.Max(1, maxAttempts);
	}

	public override string Name => $"FMP_{_kind}_WebSocket";

	public event Func<FmpStreamKinds, FmpStreamMessage, CancellationToken, ValueTask>
		DataReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	public async Task Subscribe(string symbol, CancellationToken cancellationToken)
	{
		symbol = symbol.ThrowIfEmpty(nameof(symbol));
		lock (_subscriptionsSync)
		{
			if (!_subscriptions.Add(symbol))
				return;
			_runTask ??= Run(_cancellation.Token);
		}

		try
		{
			await _initialConnection.Task.WaitAsync(cancellationToken);
			var isActive = false;
			lock (_subscriptionsSync)
				isActive = _activeSubscriptions.Contains(symbol);
			if (!isActive)
			{
				var socket = _socket ?? throw new WebSocketException(
					"FMP WebSocket is not connected.");
				await SendSubscription(socket, [symbol], true, cancellationToken);
				lock (_subscriptionsSync)
					_activeSubscriptions.Add(symbol);
			}
		}
		catch
		{
			lock (_subscriptionsSync)
			{
				_subscriptions.Remove(symbol);
				_activeSubscriptions.Remove(symbol);
			}
			throw;
		}
	}

	public async Task Unsubscribe(string symbol, CancellationToken cancellationToken)
	{
		var wasActive = false;
		lock (_subscriptionsSync)
		{
			if (!_subscriptions.Remove(symbol))
				return;
			wasActive = _activeSubscriptions.Remove(symbol);
		}

		var socket = _socket;
		if (wasActive && socket?.State == WebSocketState.Open)
			await SendSubscription(socket, [symbol], false, cancellationToken);
	}

	public async Task Disconnect()
	{
		RequestStop();
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

	public void RequestStop() => _cancellation.Cancel();

	private async Task Run(CancellationToken cancellationToken)
	{
		var failures = 0;
		var wasConnected = false;
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				using var socket = new ClientWebSocket();
				_socket = socket;
				socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
				await socket.ConnectAsync(_uri, cancellationToken);
				await Authorize(socket, cancellationToken);

				string[] symbols;
				lock (_subscriptionsSync)
				{
					symbols = [.. _subscriptions];
					_activeSubscriptions.Clear();
				}
				if (symbols.Length == 0)
					break;
				await SendSubscription(socket, symbols, true, cancellationToken);
				lock (_subscriptionsSync)
					_activeSubscriptions.UnionWith(symbols);

				failures = 0;
				wasConnected = true;
				_initialConnection.TrySetResult();
				await ReceiveLoop(socket, cancellationToken);
				if (!cancellationToken.IsCancellationRequested)
					throw new WebSocketException("FMP WebSocket closed unexpectedly.");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception error)
			{
				failures++;
				await Invoke(Error, error, cancellationToken);
				if (failures > _maxAttempts)
				{
					if (!wasConnected)
						_initialConnection.TrySetException(error);
					break;
				}
				await Task.Delay(TimeSpan.FromSeconds(Math.Min(30,
					1 << Math.Min(failures, 5))), cancellationToken);
			}
			finally
			{
				_socket = null;
				lock (_subscriptionsSync)
					_activeSubscriptions.Clear();
			}
		}

		if (!_initialConnection.Task.IsCompleted)
			_initialConnection.TrySetCanceled(cancellationToken);
	}

	private async Task Authorize(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		await Send(socket, new FmpLoginRequest
		{
			Event = "login",
			Data = new() { ApiKey = _token },
		}, cancellationToken);
		var response = await ReceiveMessage(socket, cancellationToken);
		if (!response.Event.EqualsIgnoreCase("login") || response.Status != 200)
		{
			throw new InvalidOperationException("FMP WebSocket authorization failed" +
				(response.Status == null ? string.Empty : $" ({response.Status.Value})") +
				$": {response.Message.IsEmpty("unknown error")}");
		}
	}

	private async Task ReceiveLoop(ClientWebSocket socket,
		CancellationToken cancellationToken)
	{
		while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
		{
			var message = await ReceiveMessage(socket, cancellationToken);
			if (message.Event.EqualsIgnoreCase("error") ||
				message.Event.EqualsIgnoreCase("login_failed") || message.Status is >= 400)
			{
				throw new InvalidOperationException("FMP WebSocket error" +
					(message.Status == null ? string.Empty : $" ({message.Status.Value})") + ": " +
					message.Message.IsEmpty("unknown error"));
			}
			if (!message.Symbol.IsEmpty() && !message.Type.IsEmpty())
				await Invoke(DataReceived, _kind, message, cancellationToken);
		}
	}

	private async Task<FmpStreamMessage> ReceiveMessage(ClientWebSocket socket,
		CancellationToken cancellationToken)
	{
		var content = await ReceiveText(socket, cancellationToken);
		try
		{
			return JsonConvert.DeserializeObject<FmpStreamMessage>(content, _jsonSettings) ??
				throw new InvalidDataException("Empty FMP WebSocket message.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException("Invalid FMP WebSocket message.", error);
		}
	}

	private Task SendSubscription(ClientWebSocket socket, string[] symbols, bool isSubscribe,
		CancellationToken cancellationToken)
		=> Send(socket, new FmpSubscriptionRequest
		{
			Event = isSubscribe ? "subscribe" : "unsubscribe",
			Data = new() { Tickers = symbols },
		}, cancellationToken);

	private async Task Send<TRequest>(ClientWebSocket socket, TRequest request,
		CancellationToken cancellationToken)
		where TRequest : class
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
				throw new WebSocketException($"FMP WebSocket closed: {result.CloseStatus} " +
					result.CloseStatusDescription);
			if (result.MessageType != WebSocketMessageType.Text)
				throw new InvalidDataException(
					$"Unexpected FMP WebSocket message type {result.MessageType}.");
			stream.Write(buffer, 0, result.Count);
			if (stream.Length > _maxMessageSize)
				throw new InvalidDataException("FMP WebSocket message exceeds 4 MiB.");
			if (result.EndOfMessage)
				return Encoding.UTF8.GetString(stream.GetBuffer(), 0,
					checked((int)stream.Length));
		}
	}

	private static ValueTask Invoke<T>(Func<T, CancellationToken, ValueTask> handler, T value,
		CancellationToken cancellationToken)
		=> handler == null ? default : handler(value, cancellationToken);

	private static ValueTask Invoke<T1, T2>(Func<T1, T2, CancellationToken, ValueTask> handler,
		T1 value1, T2 value2, CancellationToken cancellationToken)
		=> handler == null ? default : handler(value1, value2, cancellationToken);

	protected override void DisposeManaged()
	{
		Disconnect().GetAwaiter().GetResult();
		_cancellation.Dispose();
		_sendLock.Dispose();
		base.DisposeManaged();
	}
}
