namespace StockSharp.MetaApi.Native;

sealed class MetaApiStreamingClient : BaseLogReceiver
{
	private static TaskCompletionSource<bool> CreateSyncSource()
		=> new(TaskCreationOptions.RunContinuationsAsynchronously);

	private readonly WebSocketClient _client;
	private readonly string _accountId;
	private readonly string _token;
	private readonly string _clientId;
	private readonly Lock _sync = new();
	private readonly MetaApiSynchronizationState _synchronization = new();
	private readonly Dictionary<string, TaskCompletionSource<bool>> _requests =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, List<MetaApiMarketDataSubscription>> _subscriptions =
		new(StringComparer.OrdinalIgnoreCase);
	private TaskCompletionSource<bool> _synchronized = CreateSyncSource();
	private string _sessionId;
	private string _synchronizationId;
	private string _host;
	private bool _isNamespaceConnected;
	private bool _isCompletionReported;
	private int _pingInterval = 10000;
	private int _pingTimeout = 5000;
	private DateTime _lastPing;
	private DateTime _lastPong;
	private DateTime _lastSubscriptionRefresh;

	public MetaApiStreamingClient(Uri server, SecureString token, string accountId,
		int reconnectAttempts, WorkingTime workingTime)
	{
		_accountId = accountId.ThrowIfEmpty(nameof(accountId));
		_token = token?.UnSecure().ThrowIfEmpty(nameof(token));
		_clientId = Random.Shared.NextDouble().ToString("0.################",
			CultureInfo.InvariantCulture);
		var address = MetaApiSocketIoProtocol.CreateWebSocketUri(server, _token, _clientId);
		_client = new(
			address.ToString(),
			(state, cancellationToken) => StateChanged is { } stateHandler
				? stateHandler(state, cancellationToken) : default,
			(error, cancellationToken) => OnError(error, cancellationToken),
			ProcessAsync,
			SocketInfo,
			SocketError,
			SocketVerbose)
		{
			ReconnectAttempts = Math.Max(1, reconnectAttempts),
			WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime)),
			DisableAutoResend = true,
		};
		_client.Init += InitializeSocket;
		_client.PostConnect += OnPostConnectAsync;
	}

	public override string Name => nameof(MetaApi) + "_Streaming";

	public event Func<MetaApiSynchronizationPacket, CancellationToken, ValueTask>
		PacketReceived;
	public event Func<CancellationToken, ValueTask> SynchronizationStarted;
	public event Func<CancellationToken, ValueTask> Synchronized;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public async Task ConnectAndSynchronizeAsync(TimeSpan timeout,
		CancellationToken cancellationToken)
	{
		try
		{
			await _client.ConnectAsync(cancellationToken);
			Task synchronizationTask;
			using (_sync.EnterScope())
				synchronizationTask = _synchronized.Task;
			await synchronizationTask.WaitAsync(timeout, cancellationToken);
		}
		catch (Exception error)
		{
			var sanitized = Sanitize(error);
			if (ReferenceEquals(sanitized, error))
				throw;
			throw sanitized;
		}
	}

	public ValueTask DisconnectAsync(CancellationToken cancellationToken)
		=> _client.DisconnectAsync(cancellationToken);

	public async ValueTask PingAsync(CancellationToken cancellationToken)
	{
		bool isConnected;
		DateTime lastPing;
		int pingInterval;
		int pingTimeout;
		DateTime lastPong;
		using (_sync.EnterScope())
		{
			isConnected = _isNamespaceConnected;
			lastPing = _lastPing;
			lastPong = _lastPong;
			pingInterval = _pingInterval;
			pingTimeout = _pingTimeout;
		}
		if (!isConnected)
			return;

		var now = DateTime.UtcNow;
		if (lastPing > lastPong && now - lastPing > TimeSpan.FromMilliseconds(pingTimeout))
		{
			this.AddWarningLog("MetaApi Engine.IO pong timeout. Reconnecting the stream.");
			_client.Abort();
			return;
		}
		if (now - lastPing >=
			TimeSpan.FromMilliseconds(Math.Max(1000, pingInterval)))
		{
			await _client.SendAsync("2", cancellationToken);
			using (_sync.EnterScope())
				_lastPing = now;
		}
		await RefreshSubscriptionsIfRequiredAsync(now, cancellationToken);
	}

	public async Task SubscribeMarketDataAsync(string symbol,
		MetaApiMarketDataSubscription subscription,
		CancellationToken cancellationToken)
	{
		if (subscription is null)
			throw new ArgumentNullException(nameof(subscription));
		symbol.ThrowIfEmpty(nameof(symbol));
		bool added;
		using (_sync.EnterScope())
		{
			if (!_subscriptions.TryGetValue(symbol, out var subscriptions))
				_subscriptions.Add(symbol, subscriptions = []);
			added = !subscriptions.Any(item => SameSubscription(item, subscription));
			if (added)
				subscriptions.Add(subscription);
		}
		if (!added)
			return;

		try
		{
			await RequestAsync(new MetaApiMarketDataRequest("subscribeToMarketData")
			{
				Symbol = symbol,
				Subscriptions = [subscription],
			}, cancellationToken);
			this.AddDebugLog("MetaApi subscribed to {0} {1}{2}.", symbol,
				subscription.Type, subscription.Timeframe.IsEmpty()
					? string.Empty : $"/{subscription.Timeframe}");
			using (_sync.EnterScope())
				_lastSubscriptionRefresh = DateTime.UtcNow;
		}
		catch
		{
			RemoveSubscription(symbol, subscription);
			throw;
		}
	}

	public async Task UnsubscribeMarketDataAsync(string symbol,
		MetaApiMarketDataSubscription subscription,
		CancellationToken cancellationToken)
	{
		if (subscription is null)
			throw new ArgumentNullException(nameof(subscription));
		if (!RemoveSubscription(symbol, subscription))
			return;

		try
		{
			await RequestAsync(new MetaApiMarketDataRequest("unsubscribeFromMarketData")
			{
				Symbol = symbol,
				Subscriptions = [subscription],
			}, cancellationToken);
			this.AddDebugLog("MetaApi unsubscribed from {0} {1}{2}.", symbol,
				subscription.Type, subscription.Timeframe.IsEmpty()
					? string.Empty : $"/{subscription.Timeframe}");
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (!_subscriptions.TryGetValue(symbol, out var subscriptions))
					_subscriptions.Add(symbol, subscriptions = []);
				if (!subscriptions.Any(item => SameSubscription(item, subscription)))
					subscriptions.Add(subscription);
			}
			throw;
		}
	}

	private void InitializeSocket(ClientWebSocket socket)
	{
		socket.Options.SetRequestHeader("Client-Id", _clientId);
		socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
	}

	private ValueTask OnPostConnectAsync(bool isReconnect,
		CancellationToken cancellationToken)
	{
		TaskCompletionSource<bool>[] requests;
		using (_sync.EnterScope())
		{
			_sessionId = Guid.NewGuid().ToString("N");
			_synchronizationId = null;
			_host = null;
			_isNamespaceConnected = false;
			_isCompletionReported = false;
			_lastPing = DateTime.UtcNow;
			_lastPong = _lastPing;
			_lastSubscriptionRefresh = default;
			_synchronized = CreateSyncSource();
			requests = [.. _requests.Values];
			_requests.Clear();
		}
		foreach (var request in requests)
			request.TrySetException(new IOException(
				"MetaApi streaming connection was restarted."));
		this.AddInfoLog(isReconnect
			? "MetaApi stream transport restored; terminal state will be resynchronized."
			: "MetaApi stream transport connected; waiting for Engine.IO handshake.");
		return default;
	}

	private async ValueTask ProcessAsync(WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var frame = message.AsString();
		if (frame.IsEmpty())
			return;

		if (frame[0] == '0')
		{
			var handshake = MetaApiSocketIoProtocol.ParseHandshake(frame);
			using (_sync.EnterScope())
			{
				_pingInterval = handshake.PingInterval > 0
					? handshake.PingInterval : _pingInterval;
				_pingTimeout = handshake.PingTimeout > 0
					? handshake.PingTimeout : _pingTimeout;
			}
			await _client.SendAsync("40", cancellationToken);
			return;
		}

		switch (frame)
		{
			case "40":
				using (_sync.EnterScope())
					_isNamespaceConnected = true;
				await SendSubscribeAsync(cancellationToken);
				return;
			case "2":
				await _client.SendAsync("3", cancellationToken);
				return;
			case "3":
				using (_sync.EnterScope())
					_lastPong = DateTime.UtcNow;
				return;
			case "41":
				using (_sync.EnterScope())
					_isNamespaceConnected = false;
				return;
		}

		if (frame.StartsWith("44", StringComparison.Ordinal))
		{
			await OnError(new InvalidOperationException(
				"MetaApi Socket.IO namespace rejected the connection: " + frame[2..]),
				cancellationToken);
			return;
		}

		if (!MetaApiSocketIoProtocol.TryParseEvent(frame, out var socketEvent))
		{
			this.AddDebugLog("MetaApi ignored Engine.IO packet type {0}.", frame[0]);
			return;
		}

		switch (socketEvent.Name)
		{
			case "response":
			case "tradeResult":
				CompleteRequest(socketEvent.Response);
				break;
			case "processingError":
				await ProcessRequestErrorAsync(
					socketEvent.ProcessingError, cancellationToken);
				break;
			case "synchronization":
				await ProcessSynchronizationAsync(
					socketEvent.Synchronization, cancellationToken);
				break;
			default:
				this.AddDebugLog("MetaApi ignored Socket.IO event {0}.",
					socketEvent.Name);
				break;
		}
	}

	private ValueTask SendSubscribeAsync(CancellationToken cancellationToken)
	{
		string sessionId;
		using (_sync.EnterScope())
			sessionId = _sessionId;
		this.AddDebugLog("MetaApi subscribing to terminal events for account {0}.",
			_accountId);
		return SendRequestNoWaitAsync(new MetaApiSubscribeRequest
		{
			InstanceIndex = 0,
			SessionId = sessionId,
		}, cancellationToken);
	}

	private async ValueTask ProcessSynchronizationAsync(
		MetaApiSynchronizationPacket packet,
		CancellationToken cancellationToken)
	{
		if (!packet.AccountId.IsEmpty() &&
			!packet.AccountId.EqualsIgnoreCase(_accountId))
			return;

		var type = packet.Type;
		if (type == "authenticated")
		{
			string sessionId;
			using (_sync.EnterScope())
				sessionId = _sessionId;
			var packetSessionId = packet.SessionId;
			if (!packetSessionId.IsEmpty() && !packetSessionId.EqualsIgnoreCase(sessionId))
				return;
			await BeginSynchronizationAsync(packet.Host, cancellationToken);
			return;
		}

		string synchronizationId;
		using (_sync.EnterScope())
			synchronizationId = _synchronizationId;
		if (synchronizationId.IsEmpty())
			return;

		if (!_synchronization.TryAccept(packet))
		{
			if (_synchronization.RequiresResynchronization)
			{
				this.AddWarningLog(
					"MetaApi sequence gap detected. Rebuilding the terminal snapshot.");
				await BeginSynchronizationAsync(_host, cancellationToken);
			}
			return;
		}

		if (PacketReceived is { } packetHandler)
			await packetHandler(packet, cancellationToken);

		if (_synchronization.IsReady)
			await CompleteSynchronizationAsync(cancellationToken);
	}

	private async ValueTask BeginSynchronizationAsync(string host,
		CancellationToken cancellationToken)
	{
		var synchronizationId = Guid.NewGuid().ToString("N");
		using (_sync.EnterScope())
		{
			_synchronizationId = synchronizationId;
			_host = host;
			_isCompletionReported = false;
			if (_synchronized.Task.IsCompleted)
				_synchronized = CreateSyncSource();
		}
		_synchronization.Begin(synchronizationId);
		if (SynchronizationStarted is { } startedHandler)
			await startedHandler(cancellationToken);

		var start = DateTime.UtcNow;
		var request = new MetaApiSynchronizeRequest
		{
			RequestId = synchronizationId,
			Version = 2,
			InstanceIndex = 0,
			StartingHistoryOrderTime = start,
			StartingDealTime = start,
			Host = host,
		};
		this.AddInfoLog("MetaApi terminal synchronization {0} started.",
			synchronizationId);
		await SendRequestNoWaitAsync(request, cancellationToken);
	}

	private async ValueTask CompleteSynchronizationAsync(
		CancellationToken cancellationToken)
	{
		TaskCompletionSource<bool> completion;
		using (_sync.EnterScope())
		{
			if (_isCompletionReported)
				return;
			_isCompletionReported = true;
			completion = _synchronized;
		}

		await RestoreSubscriptionsAsync(cancellationToken);
		completion.TrySetResult(true);
		this.AddInfoLog("MetaApi terminal state synchronized for account {0}.",
			_accountId);
		if (Synchronized is { } synchronizedHandler)
			await synchronizedHandler(cancellationToken);
	}

	private async ValueTask RestoreSubscriptionsAsync(
		CancellationToken cancellationToken)
	{
		KeyValuePair<string, MetaApiMarketDataSubscription[]>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _subscriptions.Select(pair =>
				new KeyValuePair<string, MetaApiMarketDataSubscription[]>(pair.Key,
					[.. pair.Value]))];

		foreach (var pair in subscriptions)
		{
			if (pair.Value.Length == 0)
				continue;
			await SendRequestNoWaitAsync(
				new MetaApiMarketDataRequest("subscribeToMarketData")
			{
				Symbol = pair.Key,
				Subscriptions = pair.Value,
			}, cancellationToken);
		}
		if (subscriptions.Length > 0)
		{
			using (_sync.EnterScope())
				_lastSubscriptionRefresh = DateTime.UtcNow;
			this.AddDebugLog("MetaApi restored market data for {0} symbols.",
				subscriptions.Length);
		}
	}

	private async ValueTask RefreshSubscriptionsIfRequiredAsync(DateTime now,
		CancellationToken cancellationToken)
	{
		MetaApiMarketDataRefreshItem[] subscriptions;
		using (_sync.EnterScope())
		{
			if (!_isNamespaceConnected || !_synchronization.IsReady ||
				_subscriptions.Count == 0 ||
				now - _lastSubscriptionRefresh < TimeSpan.FromMinutes(5))
				return;
			_lastSubscriptionRefresh = now;
			subscriptions = [.. _subscriptions.Select(pair =>
				new MetaApiMarketDataRefreshItem
				{
					Symbol = pair.Key,
					Subscriptions = [.. pair.Value],
				})];
		}

		await RequestAsync(new MetaApiRefreshMarketDataRequest
		{
			Subscriptions = subscriptions,
		}, cancellationToken);
		this.AddDebugLog("MetaApi refreshed market data for {0} symbols.",
			subscriptions.Length);
	}

	private async Task RequestAsync(MetaApiRequest request,
		CancellationToken cancellationToken)
	{
		PrepareRequest(request);
		var completion = new TaskCompletionSource<bool>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		using (_sync.EnterScope())
			_requests.Add(request.RequestId, completion);
		try
		{
			await _client.SendAsync(
				MetaApiSocketIoProtocol.EncodeEvent("request", request),
				cancellationToken);
			await completion.Task.WaitAsync(TimeSpan.FromSeconds(60),
				cancellationToken);
		}
		finally
		{
			using (_sync.EnterScope())
				_requests.Remove(request.RequestId);
		}
	}

	private ValueTask SendRequestNoWaitAsync(MetaApiRequest request,
		CancellationToken cancellationToken)
	{
		PrepareRequest(request);
		return _client.SendAsync(
			MetaApiSocketIoProtocol.EncodeEvent("request", request),
			cancellationToken);
	}

	private void PrepareRequest(MetaApiRequest request)
	{
		if (request is null)
			throw new ArgumentNullException(nameof(request));
		request.RequestId = request.RequestId.IsEmpty(
			Guid.NewGuid().ToString("N"));
		request.AccountId = _accountId;
		request.Application = request.Application.IsEmpty("MetaApi");
		request.InstanceIndex ??= 0;
	}

	private void CompleteRequest(MetaApiResponse response)
	{
		if (response?.RequestId.IsEmpty() != false)
			return;
		TaskCompletionSource<bool> completion;
		using (_sync.EnterScope())
			_requests.TryGetValue(response.RequestId, out completion);
		completion?.TrySetResult(true);
	}

	private async ValueTask ProcessRequestErrorAsync(MetaApiProcessingError error,
		CancellationToken cancellationToken)
	{
		var exception = new MetaApiApiException(HttpStatusCode.BadRequest,
			error?.Error,
			error?.Message.IsEmpty("MetaApi streaming request failed."),
			error?.Metadata?.RecommendedRetryTime);
		TaskCompletionSource<bool> completion;
		if (error?.RequestId.IsEmpty() == false)
		{
			using (_sync.EnterScope())
				_requests.TryGetValue(error.RequestId, out completion);
		}
		else
			completion = null;
		if (completion is not null)
			completion.TrySetException(exception);
		else
			await OnError(exception, cancellationToken);
	}

	private bool RemoveSubscription(string symbol,
		MetaApiMarketDataSubscription subscription)
	{
		using (_sync.EnterScope())
		{
			if (!_subscriptions.TryGetValue(symbol, out var subscriptions))
				return false;
			var index = subscriptions.FindIndex(item => SameSubscription(item, subscription));
			if (index < 0)
				return false;
			subscriptions.RemoveAt(index);
			if (subscriptions.Count == 0)
				_subscriptions.Remove(symbol);
			return true;
		}
	}

	private static bool SameSubscription(MetaApiMarketDataSubscription left,
		MetaApiMarketDataSubscription right)
		=> left.Type.EqualsIgnoreCase(right.Type) &&
			left.Timeframe.EqualsIgnoreCase(right.Timeframe);

	private ValueTask OnError(Exception error, CancellationToken cancellationToken)
	{
		error = Sanitize(error);
		this.AddErrorLog(error);
		return Error is { } errorHandler
			? errorHandler(error, cancellationToken) : default;
	}

	private Exception Sanitize(Exception error)
	{
		if (error is null || error is OperationCanceledException ||
			!error.Message.Contains(_token, StringComparison.Ordinal))
			return error;
		return new InvalidOperationException(Sanitize(error.Message));
	}

	private string Sanitize(string value)
		=> value?.Replace(_token, "***", StringComparison.Ordinal);

	private object Sanitize(object arg)
		=> arg switch
		{
			string value => Sanitize(value),
			Uri value => Sanitize(value.ToString()),
			Exception error => Sanitize(error),
			object[] values => values.Select(Sanitize).ToArray(),
			_ => arg,
		};

	private void SocketInfo(string format, object arg)
		=> this.AddInfoLog(Sanitize(format), Sanitize(arg));

	private void SocketError(string format, object arg)
		=> this.AddErrorLog(Sanitize(format), Sanitize(arg));

	private void SocketVerbose(string format, object arg)
		=> this.AddVerboseLog(Sanitize(format), Sanitize(arg));

	protected override void DisposeManaged()
	{
		_client.Init -= InitializeSocket;
		_client.PostConnect -= OnPostConnectAsync;
		_client.Dispose();
		base.DisposeManaged();
	}
}
