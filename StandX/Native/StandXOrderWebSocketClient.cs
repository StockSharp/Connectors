namespace StockSharp.StandX.Native;

sealed class StandXOrderWebSocketClient : BaseLogReceiver
{
	private sealed class PendingRequest
	{
		public TaskCompletionSource<StandXOperationResult> Completion { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
	}

	private readonly string _endpoint;
	private readonly StandXRestClient _authentication;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly Dictionary<string, PendingRequest> _pending =
		new(StringComparer.Ordinal);
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;

	public StandXOrderWebSocketClient(string endpoint,
		StandXRestClient authentication, WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		_authentication = authentication ?? throw new ArgumentNullException(
			nameof(authentication));
		if (!_authentication.IsAuthenticated)
			throw new ArgumentException(
				"StandX order WebSocket requires wallet authentication.",
				nameof(authentication));
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
		SessionId = Guid.NewGuid().ToString();
	}

	public override string Name => "STANDX_ORDER_WS";

	public string SessionId { get; }

	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"StandX order WebSocket is already initialized.");
		var client = _client = CreateClient();
		try
		{
			await client.ConnectAsync(cancellationToken);
			await AuthenticateAsync(client, cancellationToken);
		}
		catch
		{
			await DisconnectAsync(cancellationToken);
			throw;
		}
	}

	public async ValueTask DisconnectAsync(
		CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		if (client is null)
			return;
		FailPending(new InvalidOperationException(
			"StandX order WebSocket disconnected."));
		try
		{
			if (client.IsConnected)
				await client.DisconnectAsync(cancellationToken);
		}
		finally
		{
			client.Dispose();
		}
	}

	public ValueTask<StandXOperationResult> PlaceOrderAsync(
		StandXNewOrderRequest request, CancellationToken cancellationToken)
		=> SendSignedAsync(StandXOrderSocketMethods.NewOrder, request,
			cancellationToken);

	public ValueTask<StandXOperationResult> CancelOrderAsync(
		StandXCancelOrderRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (request.OrderId is null && request.ClientOrderId.IsEmpty())
			throw new ArgumentException(
				"StandX cancellation requires an order or client order ID.",
				nameof(request));
		return SendSignedAsync(StandXOrderSocketMethods.CancelOrder, request,
			cancellationToken);
	}

	private WebSocketClient CreateClient()
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			_endpoint,
			(state, token) => OnStateChangedAsync(client, state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(socket, message, token) => OnProcessAsync(socket, message, token),
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			Indent = false,
			SendSettings = _jsonSettings,
		};
		client.Init += static socket => socket.Options.SetRequestHeader(
			"User-Agent", "StockSharp-StandX-Connector/1.0");
		return client;
	}

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			FailPending(new InvalidOperationException(
				"StandX order WebSocket reconnected during a request."));
			await AuthenticateAsync(client, cancellationToken);
		}
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private ValueTask<StandXOperationResult> AuthenticateAsync(
		WebSocketClient client, CancellationToken cancellationToken)
		=> SendAsync(client, StandXOrderSocketMethods.Login,
			new StandXOrderSocketLogin
			{
				Token = _authentication.AccessToken,
			}, false, cancellationToken);

	private ValueTask<StandXOperationResult> SendSignedAsync<TRequest>(
		StandXOrderSocketMethods method, TRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var client = _client;
		if (client?.IsConnected != true)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		return SendAsync(client, method, request, true, cancellationToken);
	}

	private async ValueTask<StandXOperationResult> SendAsync<TRequest>(
		WebSocketClient client, StandXOrderSocketMethods method, TRequest request,
		bool isSigned, CancellationToken cancellationToken)
	{
		var parameters = JsonConvert.SerializeObject(request, _jsonSettings);
		var requestId = Guid.NewGuid().ToString();
		var pending = new PendingRequest();
		using (_sync.EnterScope())
			if (!_pending.TryAdd(requestId, pending))
				throw new InvalidOperationException(
					"Duplicate StandX WebSocket request ID.");
		try
		{
			await SendRawAsync(client, new()
			{
				SessionId = SessionId,
				RequestId = requestId,
				Method = method,
				Header = isSigned
					? _authentication.SignRequest(parameters, requestId)
					: null,
				Parameters = parameters,
			}, cancellationToken);
			var result = await pending.Completion.Task.WaitAsync(
				TimeSpan.FromSeconds(15), cancellationToken);
			if (result.Code != 0)
				throw new InvalidOperationException(
					$"StandX rejected {method} ({result.Code}): {result.Message}");
			return result;
		}
		finally
		{
			using (_sync.EnterScope())
				_pending.Remove(requestId);
		}
	}

	private async ValueTask SendRawAsync(WebSocketClient client,
		StandXOrderSocketRequest request,
		CancellationToken cancellationToken)
	{
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(request, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask OnProcessAsync(WebSocketClient client,
		WebSocketMessage message, CancellationToken cancellationToken)
	{
		_ = client;
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var result = JsonConvert.DeserializeObject<StandXOperationResult>(
				payload, _jsonSettings) ?? throw new InvalidDataException(
					"StandX order WebSocket returned an empty response.");
			if (result.RequestId.IsEmpty())
				throw new InvalidDataException(
					"StandX order WebSocket response has no request ID.");
			PendingRequest pending;
			using (_sync.EnterScope())
				_pending.TryGetValue(result.RequestId, out pending);
			if (pending is not null)
				pending.Completion.TrySetResult(result);
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or InvalidOperationException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private void FailPending(Exception error)
	{
		PendingRequest[] pending;
		using (_sync.EnterScope())
		{
			pending = [.. _pending.Values];
			_pending.Clear();
		}
		foreach (var request in pending)
			request.Completion.TrySetException(error);
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;

	protected override void DisposeManaged()
	{
		FailPending(new ObjectDisposedException(
			nameof(StandXOrderWebSocketClient)));
		_client?.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}
}
