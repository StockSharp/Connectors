namespace StockSharp.TwelveData.Native;

sealed class TwelveDataWebSocketClient : BaseLogReceiver
{
	private const int _maxMessageSize = 4 * 1024 * 1024;

	private readonly Uri _uri;
	private readonly int _maxAttempts;
	private readonly CancellationTokenSource _cancellation = new();
	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly TaskCompletionSource _initialConnection =
		new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly Dictionary<string, TwelveDataSecurityKey> _subscriptions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly object _subscriptionsSync = new();
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private ClientWebSocket _socket;
	private Task _runTask;

	public TwelveDataWebSocketClient(Uri address, string token, int maxAttempts)
	{
		if (address == null)
			throw new ArgumentNullException(nameof(address));
		token.ThrowIfEmpty(nameof(token));
		_maxAttempts = Math.Max(1, maxAttempts);
		var builder = new UriBuilder(address);
		var prefix = builder.Query.TrimStart('?');
		builder.Query = (prefix.IsEmpty() ? string.Empty : prefix + "&") +
			"apikey=" + Uri.EscapeDataString(token);
		_uri = builder.Uri;
	}

	public override string Name => nameof(TwelveData) + "_" + nameof(TwelveDataWebSocketClient);

	public event Func<TwelveDataStreamMessage, CancellationToken, ValueTask> PriceReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public async Task Connect(CancellationToken cancellationToken)
	{
		if (_runTask != null)
			throw new InvalidOperationException("Twelve Data WebSocket is already running.");
		_runTask = Run(_cancellation.Token);
		await _initialConnection.Task.WaitAsync(cancellationToken);
	}

	public async Task Subscribe(TwelveDataSecurityKey key, CancellationToken cancellationToken)
	{
		var native = key.ToNative();
		lock (_subscriptionsSync)
		{
			if (!_subscriptions.TryAdd(native, key))
				return;
		}

		try
		{
			var socket = _socket;
			if (socket?.State == WebSocketState.Open)
				await SendSubscription(socket, [key], true, cancellationToken);
		}
		catch
		{
			lock (_subscriptionsSync)
				_subscriptions.Remove(native);
			throw;
		}
	}

	public async Task Unsubscribe(TwelveDataSecurityKey key, CancellationToken cancellationToken)
	{
		lock (_subscriptionsSync)
		{
			if (!_subscriptions.Remove(key.ToNative()))
				return;
		}

		var socket = _socket;
		if (socket?.State == WebSocketState.Open)
			await SendSubscription(socket, [key], false, cancellationToken);
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
				socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
				await socket.ConnectAsync(_uri, cancellationToken);
				await RestoreSubscriptions(socket, cancellationToken);

				failures = 0;
				wasConnected = true;
				_initialConnection.TrySetResult();
				await Invoke(StateChanged, ConnectionStates.Connected, cancellationToken);

				using var connectionCancellation =
					CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				var heartbeat = HeartbeatLoop(socket, connectionCancellation.Token);
				try
				{
					await ReceiveLoop(socket, cancellationToken);
				}
				finally
				{
					connectionCancellation.Cancel();
					try
					{
						await heartbeat;
					}
					catch (OperationCanceledException)
					{
					}
				}

				if (!cancellationToken.IsCancellationRequested)
					throw new WebSocketException("Twelve Data WebSocket closed unexpectedly.");
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
		TwelveDataSecurityKey[] subscriptions;
		lock (_subscriptionsSync)
			subscriptions = [.. _subscriptions.Values];
		if (subscriptions.Length > 0)
			await SendSubscription(socket, subscriptions, true, cancellationToken);
	}

	private async Task HeartbeatLoop(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
			if (socket.State != WebSocketState.Open)
				return;
			await Send(socket, new TwelveDataStreamRequest { Action = "heartbeat" },
				cancellationToken);
		}
	}

	private async Task ReceiveLoop(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
		{
			var content = await ReceiveText(socket, cancellationToken);
			TwelveDataStreamMessage message;
			try
			{
				message = JsonConvert.DeserializeObject<TwelveDataStreamMessage>(content, _jsonSettings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException("Invalid Twelve Data WebSocket message.", error);
			}

			if (message == null)
				continue;
			if (message.Event.EqualsIgnoreCase("price"))
			{
				await Invoke(PriceReceived, message, cancellationToken);
				continue;
			}
			if (message.Event.EqualsIgnoreCase("subscribe-status"))
			{
				if (message.Fails?.Length > 0)
				{
					var details = string.Join(", ", message.Fails.Select(item =>
						item?.Message.IsEmpty(item?.Symbol).IsEmpty("unknown symbol")));
					await Invoke(Error, new InvalidOperationException(
						$"Twelve Data rejected subscription: {details}."), cancellationToken);
				}
				else if (!message.Status.IsEmpty() && !message.Status.EqualsIgnoreCase("ok"))
				{
					await Invoke(Error, CreateStreamError(message), cancellationToken);
				}
				continue;
			}
			if (message.Event.EqualsIgnoreCase("error") || message.Code is >= 400)
				await Invoke(Error, CreateStreamError(message), cancellationToken);
		}
	}

	private async Task SendSubscription(ClientWebSocket socket, TwelveDataSecurityKey[] keys,
		bool isSubscribe, CancellationToken cancellationToken)
	{
		var action = isSubscribe ? "subscribe" : "unsubscribe";
		var simple = keys.Where(key => key.Exchange.IsEmpty() && key.MicCode.IsEmpty())
			.Select(key => key.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		if (simple.Length > 0)
		{
			await Send(socket, new TwelveDataSimpleStreamRequest
			{
				Action = action,
				Params = new() { Symbols = string.Join(',', simple) },
			}, cancellationToken);
		}

		var extended = keys.Where(key => !key.Exchange.IsEmpty() || !key.MicCode.IsEmpty())
			.GroupBy(key => key.ToNative(), StringComparer.OrdinalIgnoreCase)
			.Select(group => ToStreamSymbol(group.First())).ToArray();
		if (extended.Length > 0)
		{
			await Send(socket, new TwelveDataExtendedStreamRequest
			{
				Action = action,
				Params = new() { Symbols = extended },
			}, cancellationToken);
		}
	}

	private static TwelveDataStreamSymbol ToStreamSymbol(TwelveDataSecurityKey key)
		=> new()
		{
			Symbol = key.Symbol,
			Exchange = key.Exchange,
			MicCode = key.MicCode,
		};

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

	private static InvalidOperationException CreateStreamError(TwelveDataStreamMessage message)
	{
		var code = message.Code == null ? string.Empty : $" ({message.Code.Value})";
		return new($"Twelve Data WebSocket error{code}: " +
			message.Message.IsEmpty("unknown error"));
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
				throw new WebSocketException($"Twelve Data WebSocket closed: {result.CloseStatus} " +
					result.CloseStatusDescription);
			if (result.MessageType != WebSocketMessageType.Text)
				throw new InvalidDataException(
					$"Unexpected Twelve Data WebSocket message type {result.MessageType}.");
			stream.Write(buffer, 0, result.Count);
			if (stream.Length > _maxMessageSize)
				throw new InvalidDataException("Twelve Data WebSocket message exceeds 4 MiB.");
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
