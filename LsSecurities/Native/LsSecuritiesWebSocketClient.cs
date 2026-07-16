namespace StockSharp.LsSecurities.Native;

internal sealed class LsSocketSubscription
{
	public string Code { get; init; }
	public string Key { get; init; }
	public bool IsPrivate { get; init; }
}

internal sealed class LsSecuritiesWebSocketClient : BaseLogReceiver
{
	private const int _maxMessageSize = 4 * 1024 * 1024;

	private readonly Uri _uri;
	private readonly Func<CancellationToken, Task<string>> _accessTokenProvider;
	private readonly int _maxAttempts;
	private readonly CancellationTokenSource _cancellation = new();
	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly TaskCompletionSource _initialConnection =
		new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly Dictionary<string, LsSocketSubscription> _subscriptions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly object _subscriptionsSync = new();
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};
	private ClientWebSocket _socket;
	private Task _runTask;

	public LsSecuritiesWebSocketClient(Func<CancellationToken, Task<string>> accessTokenProvider,
		bool isDemo, int maxAttempts)
	{
		_accessTokenProvider = accessTokenProvider ?? throw new ArgumentNullException(nameof(accessTokenProvider));
		_maxAttempts = Math.Max(1, maxAttempts);
		_uri = new(isDemo
			? "wss://openapi.ls-sec.co.kr:29443/websocket"
			: "wss://openapi.ls-sec.co.kr:9443/websocket");
	}

	public override string Name => nameof(LsSecurities) + "_WebSocket";

	public event Func<LsRealtimeTrade, CancellationToken, ValueTask> TradeReceived;
	public event Func<LsRealtimeDepth, CancellationToken, ValueTask> DepthReceived;
	public event Func<string, LsRealtimeOrder, CancellationToken, ValueTask> OrderReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public async Task Connect(CancellationToken cancellationToken)
	{
		if (_runTask != null)
			throw new InvalidOperationException("LS Securities WebSocket is already running.");
		_runTask = Run(_cancellation.Token);
		await _initialConnection.Task.WaitAsync(cancellationToken);
	}

	public Task Subscribe(string code, string key, bool isPrivate, CancellationToken cancellationToken)
		=> ChangeSubscription(code, key, isPrivate, true, cancellationToken);

	public Task Unsubscribe(string code, string key, bool isPrivate, CancellationToken cancellationToken)
		=> ChangeSubscription(code, key, isPrivate, false, cancellationToken);

	private async Task ChangeSubscription(string code, string key, bool isPrivate, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		code.ThrowIfEmpty(nameof(code));
		key ??= string.Empty;
		var id = GetSubscriptionId(code, key, isPrivate);
		lock (_subscriptionsSync)
		{
			if (isSubscribe)
				_subscriptions[id] = new() { Code = code, Key = key, IsPrivate = isPrivate };
			else if (!_subscriptions.Remove(id))
				return;
		}

		var socket = _socket;
		if (socket?.State == WebSocketState.Open)
			await SendSubscription(socket, code, key, isPrivate, isSubscribe, cancellationToken);
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
				await socket.ConnectAsync(_uri, cancellationToken);
				await RestoreSubscriptions(socket, cancellationToken);
				failures = 0;
				wasConnected = true;
				_initialConnection.TrySetResult();
				await Invoke(StateChanged, ConnectionStates.Connected, cancellationToken);
				await ReceiveLoop(socket, cancellationToken);
				if (!cancellationToken.IsCancellationRequested)
					throw new WebSocketException("LS Securities WebSocket closed unexpectedly.");
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
		LsSocketSubscription[] subscriptions;
		lock (_subscriptionsSync)
			subscriptions = [.. _subscriptions.Values];
		foreach (var subscription in subscriptions)
			await SendSubscription(socket, subscription.Code, subscription.Key, subscription.IsPrivate,
				true, cancellationToken);
	}

	private async Task ReceiveLoop(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
		{
			var (type, data) = await Receive(socket, cancellationToken);
			if (type != WebSocketMessageType.Text)
				continue;
			await ProcessText(Encoding.UTF8.GetString(data), cancellationToken);
		}
	}

	private async ValueTask ProcessText(string json, CancellationToken cancellationToken)
	{
		var discriminator = JsonConvert.DeserializeObject<LsSocketDiscriminator>(json, _jsonSettings);
		var header = discriminator?.Header;
		if (header == null)
			return;
		if (!header.ResponseCode.IsEmpty() && header.ResponseCode != "00000")
		{
			await Invoke(Error, new InvalidOperationException($"LS Securities WebSocket: " +
				$"{header.ResponseCode} {header.ResponseMessage}"), cancellationToken);
			return;
		}

		switch (header.Code)
		{
			case "US3":
			{
				var message = JsonConvert.DeserializeObject<LsSocketEnvelope<LsRealtimeTrade>>(json, _jsonSettings);
				if (message?.Body != null)
					await Invoke(TradeReceived, message.Body, cancellationToken);
				break;
			}
			case "UH1":
			{
				var message = JsonConvert.DeserializeObject<LsSocketEnvelope<LsRealtimeDepth>>(json, _jsonSettings);
				if (message?.Body != null)
					await Invoke(DepthReceived, message.Body, cancellationToken);
				break;
			}
			case "SC0":
			case "SC1":
			case "SC2":
			case "SC3":
			case "SC4":
			{
				var message = JsonConvert.DeserializeObject<LsSocketEnvelope<LsRealtimeOrder>>(json, _jsonSettings);
				if (message?.Body != null)
					await Invoke(OrderReceived, header.Code, message.Body, cancellationToken);
				break;
			}
		}
	}

	private async Task SendSubscription(ClientWebSocket socket, string code, string key, bool isPrivate,
		bool isSubscribe, CancellationToken cancellationToken)
		=> await Send(socket, new LsSocketRequest
		{
			Header = new()
			{
				Token = await _accessTokenProvider(cancellationToken),
				Type = isPrivate ? isSubscribe ? "1" : "2" : isSubscribe ? "3" : "4",
			},
			Body = new() { Code = code, Key = key },
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
				throw new WebSocketException($"LS Securities WebSocket closed: {result.CloseStatus} " +
					result.CloseStatusDescription);
			if (type != null && type != result.MessageType)
				throw new InvalidDataException("LS Securities WebSocket changed message type within a frame.");
			type = result.MessageType;
			stream.Write(buffer, 0, result.Count);
			if (stream.Length > _maxMessageSize)
				throw new InvalidDataException("LS Securities WebSocket message exceeds 4 MiB.");
			if (result.EndOfMessage)
				return (type.Value, stream.ToArray());
		}
	}

	private static string GetSubscriptionId(string code, string key, bool isPrivate)
		=> $"{(isPrivate ? 'P' : 'M')}:{code}:{key}";

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
