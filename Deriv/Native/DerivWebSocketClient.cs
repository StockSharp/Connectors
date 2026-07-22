namespace StockSharp.Deriv.Native;

sealed class DerivWebSocketClient : BaseLogReceiver
{
	private const int _maxMessageSize = 4 * 1024 * 1024;

	private sealed class PendingRequest
	{
		public TaskCompletionSource<DerivResponse> Completion { get; init; }
		public string SubscriptionKey { get; init; }
	}

	private sealed class Subscription
	{
		public string Key { get; init; }
		public JObject Request { get; init; }
		public bool IsEstablished { get; set; }
		public long RequestId { get; set; }
		public string WireId { get; set; }
	}

	private readonly Func<CancellationToken, ValueTask<string>> _endpointProvider;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly Dictionary<long, PendingRequest> _pending = [];
	private readonly Dictionary<string, Subscription> _subscriptions =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _wireSubscriptions =
		new(StringComparer.Ordinal);
	private readonly Dictionary<long, string> _wireRequestIds = [];
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		Culture = CultureInfo.InvariantCulture,
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
	};
	private ClientWebSocket _socket;
	private CancellationTokenSource _lifetime;
	private Task _receiveTask;
	private long _requestId;
	private bool _isDisconnecting;

	public DerivWebSocketClient(
		Func<CancellationToken, ValueTask<string>> endpointProvider,
		int reconnectAttempts)
	{
		_endpointProvider = endpointProvider ?? throw new ArgumentNullException(
			nameof(endpointProvider));
		_reconnectAttempts = Math.Max(0, reconnectAttempts);
	}

	public override string Name => nameof(Deriv) + "_WebSocket";

	public bool IsConnected
	{
		get
		{
			using (_sync.EnterScope())
				return _socket?.State == WebSocketState.Open;
		}
	}

	public event Func<string, DerivResponse, CancellationToken, ValueTask>
		SubscriptionReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
		{
			if (_lifetime is not null)
				throw new InvalidOperationException("Deriv WebSocket is already initialized.");
		}

		var socket = await OpenSocketAsync(cancellationToken);
		var lifetime = new CancellationTokenSource();
		using (_sync.EnterScope())
		{
			_socket = socket;
			_lifetime = lifetime;
			_isDisconnecting = false;
			_receiveTask = ReceiveLoopAsync(socket, lifetime.Token);
		}
	}

	public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
	{
		ClientWebSocket socket;
		CancellationTokenSource lifetime;
		Task receiveTask;
		using (_sync.EnterScope())
		{
			_isDisconnecting = true;
			socket = _socket;
			lifetime = _lifetime;
			receiveTask = _receiveTask;
			_socket = null;
			_lifetime = null;
			_receiveTask = null;
		}

		if (lifetime is null)
			return;

		lifetime.Cancel();
		try
		{
			if (socket?.State == WebSocketState.Open)
				await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,
					"disconnect", cancellationToken);
		}
		catch (Exception error) when (error is WebSocketException or OperationCanceledException)
		{
		}
		finally
		{
			socket?.Abort();
		}

		if (receiveTask is not null)
		{
			try
			{
				await receiveTask.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
			}
			catch (Exception error) when (error is OperationCanceledException or TimeoutException)
			{
			}
		}

		lifetime.Dispose();
		FailPending(new OperationCanceledException("Deriv WebSocket disconnected."));
		using (_sync.EnterScope())
		{
			_wireSubscriptions.Clear();
			_wireRequestIds.Clear();
			foreach (var subscription in _subscriptions.Values)
			{
				subscription.RequestId = 0;
				subscription.WireId = null;
			}
		}
	}

	public ValueTask<DerivResponse> RequestAsync(JObject request,
		CancellationToken cancellationToken)
		=> SendRequestAsync(request, null, cancellationToken);

	public async ValueTask<DerivResponse> SubscribeAsync(string key, JObject request,
		bool waitForInitial, CancellationToken cancellationToken)
	{
		key.ThrowIfEmpty(nameof(key));
		if (request is null)
			throw new ArgumentNullException(nameof(request));

		var subscription = new Subscription
		{
			Key = key,
			Request = (JObject)request.DeepClone(),
		};
		using (_sync.EnterScope())
		{
			if (!_subscriptions.TryAdd(key, subscription))
				throw new InvalidOperationException(
					$"Deriv subscription '{key}' is already registered.");
		}

		try
		{
			if (!waitForInitial)
			{
				await SendLazySubscriptionAsync(subscription, cancellationToken);
				return null;
			}

			var response = await SendRequestAsync(subscription.Request, key,
				cancellationToken);
			return response;
		}
		catch
		{
			RemoveSubscription(key);
			throw;
		}
	}

	public async ValueTask UnsubscribeAsync(string key, CancellationToken cancellationToken)
	{
		var subscription = RemoveSubscription(key.ThrowIfEmpty(nameof(key)));
		if (subscription is null || !IsConnected)
			return;

		if (!subscription.WireId.IsEmpty())
		{
			await RequestAsync(new JObject { ["forget"] = subscription.WireId },
				cancellationToken);
			return;
		}

		var streamType = GetStreamType(subscription.Request);
		if (streamType.EqualsIgnoreCase("transaction"))
			await RequestAsync(new JObject { ["forget_all"] = streamType },
				cancellationToken);
	}

	public void DropSubscription(string key)
		=> RemoveSubscription(key.ThrowIfEmpty(nameof(key)));

	public async ValueTask PingAsync(CancellationToken cancellationToken)
	{
		var response = await RequestAsync(new JObject { ["ping"] = 1 }, cancellationToken);
		if (!response.MessageType.EqualsIgnoreCase("ping"))
			throw new InvalidDataException("Deriv returned an invalid ping response.");
	}

	private async ValueTask<DerivResponse> SendRequestAsync(JObject source,
		string subscriptionKey, CancellationToken cancellationToken)
	{
		if (source is null)
			throw new ArgumentNullException(nameof(source));

		var request = (JObject)source.DeepClone();
		var requestId = Interlocked.Increment(ref _requestId);
		request["req_id"] = requestId;
		var completion = new TaskCompletionSource<DerivResponse>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		using (_sync.EnterScope())
		{
			_pending.Add(requestId, new()
			{
				Completion = completion,
				SubscriptionKey = subscriptionKey,
			});
		}

		try
		{
			await SendAsync(request, cancellationToken);
			return await completion.Task.WaitAsync(TimeSpan.FromSeconds(20),
				cancellationToken);
		}
		finally
		{
			using (_sync.EnterScope())
				_pending.Remove(requestId);
		}
	}

	private async ValueTask SendLazySubscriptionAsync(Subscription subscription,
		CancellationToken cancellationToken)
	{
		var request = (JObject)subscription.Request.DeepClone();
		var requestId = Interlocked.Increment(ref _requestId);
		request["req_id"] = requestId;
		using (_sync.EnterScope())
		{
			subscription.RequestId = requestId;
			subscription.IsEstablished = true;
			_wireRequestIds[requestId] = subscription.Key;
		}
		await SendAsync(request, cancellationToken);
	}

	private async ValueTask SendAsync(JObject request,
		CancellationToken cancellationToken)
	{
		ClientWebSocket socket;
		using (_sync.EnterScope())
			socket = _socket;
		if (socket?.State != WebSocketState.Open)
			throw new InvalidOperationException("Deriv WebSocket is not connected.");

		var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request,
			Formatting.None, _jsonSettings));
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			if (socket.State != WebSocketState.Open)
				throw new InvalidOperationException("Deriv WebSocket is not connected.");
			await socket.SendAsync(new ArraySegment<byte>(bytes),
				WebSocketMessageType.Text, true, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async Task ReceiveLoopAsync(ClientWebSocket initialSocket,
		CancellationToken cancellationToken)
	{
		var socket = initialSocket;
		while (!cancellationToken.IsCancellationRequested)
		{
			Exception failure = null;
			try
			{
				while (!cancellationToken.IsCancellationRequested &&
					socket.State == WebSocketState.Open)
				{
					var payload = await ReceiveTextAsync(socket, cancellationToken);
					await ProcessAsync(payload, cancellationToken);
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception error) when (error is WebSocketException or IOException or
				InvalidDataException)
			{
				failure = error;
			}

			if (cancellationToken.IsCancellationRequested || IsDisconnecting())
				break;

			failure ??= new WebSocketException("Deriv WebSocket closed unexpectedly.");
			FailPending(failure);
			await RaiseErrorAsync(failure, cancellationToken);
			socket.Dispose();
			ClearWireState(socket);

			var restored = false;
			for (var attempt = 1; attempt <= _reconnectAttempts &&
				!cancellationToken.IsCancellationRequested; attempt++)
			{
				try
				{
					await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, attempt * 2)),
						cancellationToken);
					socket = await OpenSocketAsync(cancellationToken);
					using (_sync.EnterScope())
						_socket = socket;
					await RestoreSubscriptionsAsync(cancellationToken);
					restored = true;
					if (StateChanged is { } restoredHandler)
						await restoredHandler(ConnectionStates.Restored, cancellationToken);
					break;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					break;
				}
				catch (Exception error) when (error is WebSocketException or HttpRequestException or
					InvalidOperationException or ArgumentException)
				{
					await RaiseErrorAsync(error, cancellationToken);
					ClearWireState(socket);
					socket?.Dispose();
				}
			}

			if (restored)
				continue;

			if (!cancellationToken.IsCancellationRequested && StateChanged is { } failedHandler)
				await failedHandler(ConnectionStates.Failed, cancellationToken);
			break;
		}

		socket?.Dispose();
	}

	private async ValueTask<ClientWebSocket> OpenSocketAsync(
		CancellationToken cancellationToken)
	{
		var endpoint = (await _endpointProvider(cancellationToken)).ThrowIfEmpty(
			nameof(_endpointProvider));
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("ws" or "wss"))
			throw new InvalidOperationException(
				"Deriv WebSocket endpoint must be an absolute WS or WSS URI.");

		var socket = new ClientWebSocket();
		socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
		socket.Options.SetRequestHeader("User-Agent", "StockSharp-Deriv-Connector/1.0");
		try
		{
			await socket.ConnectAsync(uri, cancellationToken);
			this.AddInfoLog("Deriv WebSocket session established.");
			return socket;
		}
		catch
		{
			socket.Dispose();
			throw;
		}
	}

	private async ValueTask RestoreSubscriptionsAsync(
		CancellationToken cancellationToken)
	{
		Subscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _subscriptions.Values.Where(static item => item.IsEstablished)];

		foreach (var subscription in subscriptions)
			await SendLazySubscriptionAsync(subscription, cancellationToken);

		this.AddInfoLog("Deriv restored {0} WebSocket subscriptions.",
			subscriptions.Length);
	}

	private async ValueTask ProcessAsync(string payload,
		CancellationToken cancellationToken)
	{
		var response = DerivResponse.Parse(payload);
		PendingRequest pending = null;
		string subscriptionKey = null;
		using (_sync.EnterScope())
		{
			if (response.RequestId != 0 && _pending.TryGetAndRemove(
				response.RequestId, out pending))
			{
				subscriptionKey = pending.SubscriptionKey;
			}
			else if (!response.SubscriptionId.IsEmpty())
			{
				_wireSubscriptions.TryGetValue(response.SubscriptionId,
					out subscriptionKey);
			}
			else if (response.RequestId != 0)
			{
				_wireRequestIds.TryGetValue(response.RequestId,
					out subscriptionKey);
			}

			if (!subscriptionKey.IsEmpty() &&
				_subscriptions.TryGetValue(subscriptionKey, out var subscription))
			{
				subscription.IsEstablished = true;
				subscription.RequestId = response.RequestId;
				_wireRequestIds[response.RequestId] = subscriptionKey;
				if (!response.SubscriptionId.IsEmpty())
				{
					if (!subscription.WireId.IsEmpty())
						_wireSubscriptions.Remove(subscription.WireId);
					subscription.WireId = response.SubscriptionId;
					_wireSubscriptions[response.SubscriptionId] = subscriptionKey;
				}
			}
		}

		if (response.IsError)
		{
			var error = response.CreateException();
			if (pending?.Completion is not null)
				pending.Completion.TrySetException(error);
			else
				await RaiseErrorAsync(error, cancellationToken);
			return;
		}

		if (pending?.Completion is not null)
		{
			pending.Completion.TrySetResult(response);
			return;
		}

		if (!subscriptionKey.IsEmpty() && SubscriptionReceived is { } handler)
		{
			try
			{
				await handler(subscriptionKey, response, cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await RaiseErrorAsync(error, cancellationToken);
			}
		}
	}

	private static async ValueTask<string> ReceiveTextAsync(ClientWebSocket socket,
		CancellationToken cancellationToken)
	{
		var buffer = new byte[16 * 1024];
		using var stream = new MemoryStream();
		while (true)
		{
			var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer),
				cancellationToken);
			if (result.MessageType == WebSocketMessageType.Close)
				throw new WebSocketException(
					$"Deriv WebSocket closed: {result.CloseStatus} {result.CloseStatusDescription}");
			if (result.MessageType != WebSocketMessageType.Text)
				throw new InvalidDataException(
					$"Unexpected Deriv WebSocket message type {result.MessageType}.");
			stream.Write(buffer, 0, result.Count);
			if (stream.Length > _maxMessageSize)
				throw new InvalidDataException("Deriv WebSocket message exceeds 4 MiB.");
			if (result.EndOfMessage)
				return Encoding.UTF8.GetString(stream.GetBuffer(), 0,
					checked((int)stream.Length));
		}
	}

	private Subscription RemoveSubscription(string key)
	{
		using (_sync.EnterScope())
		{
			if (!_subscriptions.TryGetAndRemove(key, out var subscription))
				return null;
			if (subscription.RequestId != 0)
				_wireRequestIds.Remove(subscription.RequestId);
			if (!subscription.WireId.IsEmpty())
				_wireSubscriptions.Remove(subscription.WireId);
			return subscription;
		}
	}

	private void ClearWireState(ClientWebSocket source)
	{
		using (_sync.EnterScope())
		{
			if (ReferenceEquals(_socket, source))
				_socket = null;
			_wireSubscriptions.Clear();
			_wireRequestIds.Clear();
			foreach (var subscription in _subscriptions.Values)
			{
				subscription.RequestId = 0;
				subscription.WireId = null;
			}
		}
	}

	private void FailPending(Exception error)
	{
		TaskCompletionSource<DerivResponse>[] completions;
		using (_sync.EnterScope())
		{
			completions = [.. _pending.Values
				.Select(static item => item.Completion)
				.Where(static item => item is not null)];
			_pending.Clear();
		}
		foreach (var completion in completions)
			completion.TrySetException(error);
	}

	private bool IsDisconnecting()
	{
		using (_sync.EnterScope())
			return _isDisconnecting;
	}

	private async ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
	{
		this.AddErrorLog(error);
		if (Error is { } handler)
			await handler(error, cancellationToken);
	}

	private static string GetStreamType(JObject request)
		=> request.Properties().Select(static property => property.Name)
			.FirstOrDefault(static name => name is not "subscribe" and not "req_id" and
				not "passthrough");

	protected override void DisposeManaged()
	{
		using (_sync.EnterScope())
		{
			_isDisconnecting = true;
			_lifetime?.Cancel();
			_socket?.Abort();
			_socket?.Dispose();
			_socket = null;
		}
		_sendSync.Dispose();
		base.DisposeManaged();
	}
}
