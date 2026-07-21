namespace StockSharp.Balancer.Native;

sealed class BalancerSocketClient : BaseLogReceiver
{
	private const int _maximumMessageBytes = 1024 * 1024;
	private readonly Uri _endpoint;
	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _sendGate = new(1, 1);
	private readonly Dictionary<long, TaskCompletionSource<string>> _pending =
		[];
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private ClientWebSocket _socket;
	private CancellationTokenSource _lifetime;
	private Task _receiveTask;
	private long _requestId;

	public BalancerSocketClient(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			!(_endpoint.Scheme.EqualsIgnoreCase("ws") ||
				_endpoint.Scheme.EqualsIgnoreCase("wss")))
			throw new ArgumentException(
				"EVM WebSocket endpoint must be an absolute WS or WSS URI.",
				nameof(endpoint));
	}

	public override string Name => "Balancer_WebSocket";

	public bool IsConnected => _socket?.State == WebSocketState.Open &&
		_receiveTask?.IsCompleted == false;

	public event Action<BalancerRpcLog> LogReceived;

	public async ValueTask ConnectAsync(BalancerDeployment deployment,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(deployment);
		if (_socket is not null)
			throw new InvalidOperationException(
				"The Balancer WebSocket is already connected.");
		var socket = new ClientWebSocket();
		socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
		socket.Options.SetRequestHeader("User-Agent",
			"StockSharp-Balancer-Connector/1.0");
		var lifetime = new CancellationTokenSource();
		try
		{
			await socket.ConnectAsync(_endpoint, cancellationToken);
			_socket = socket;
			_lifetime = lifetime;
			_receiveTask = ReceiveLoopAsync(lifetime.Token);
			await SubscribeAsync(deployment, cancellationToken);
		}
		catch
		{
			lifetime.Cancel();
			lifetime.Dispose();
			socket.Abort();
			socket.Dispose();
			_socket = null;
			_lifetime = null;
			throw;
		}
	}

	protected override void DisposeManaged()
	{
		_lifetime?.Cancel();
		_socket?.Abort();
		_socket?.Dispose();
		_lifetime?.Dispose();
		FailPending(new ObjectDisposedException(nameof(BalancerSocketClient)));
		_sendGate.Dispose();
		_socket = null;
		_lifetime = null;
		_receiveTask = null;
		base.DisposeManaged();
	}

	private async ValueTask SubscribeAsync(BalancerDeployment deployment,
		CancellationToken cancellationToken)
	{
		var addresses = new[] { deployment.V2Vault, deployment.V3Vault }
			.Where(static address => !address.IsEmpty())
			.Select(static address => address.NormalizeAddress())
			.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		if (addresses.Length == 0)
			throw new InvalidOperationException(
				"The Balancer deployment has no Vault contracts.");
		var id = Interlocked.Increment(ref _requestId);
		var completion = new TaskCompletionSource<string>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		using (_sync.EnterScope())
			_pending.Add(id, completion);
		var payload = JsonConvert.SerializeObject(new BalancerSocketRequest
		{
			Id = id,
			Parameters = new()
			{
				Filter = new()
				{
					Addresses = addresses,
					Topics = [BalancerExtensions.SwapTopics],
				},
			},
		}, _jsonSettings);
		try
		{
			await SendAsync(payload, cancellationToken);
			var subscription = await completion.Task.WaitAsync(
				cancellationToken);
			if (subscription.IsEmpty())
				throw new InvalidDataException(
					"Balancer returned an empty WebSocket subscription id.");
		}
		catch
		{
			using (_sync.EnterScope())
				_pending.Remove(id);
			throw;
		}
	}

	private async ValueTask SendAsync(string payload,
		CancellationToken cancellationToken)
	{
		var socket = _socket ?? throw new InvalidOperationException(
			"The Balancer WebSocket is not connected.");
		var data = Encoding.UTF8.GetBytes(payload);
		await _sendGate.WaitAsync(cancellationToken);
		try
		{
			await socket.SendAsync(data, WebSocketMessageType.Text, true,
				cancellationToken);
		}
		finally
		{
			_sendGate.Release();
		}
	}

	private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				var payload = await ReceiveAsync(cancellationToken);
				BalancerSocketMessage message;
				try
				{
					message = JsonConvert.DeserializeObject<
						BalancerSocketMessage>(payload, _jsonSettings);
				}
				catch (JsonException error)
				{
					throw new InvalidDataException(
						"Balancer WebSocket returned an unexpected payload.",
						error);
				}
				if (message is null)
					throw new InvalidDataException(
						"Balancer WebSocket returned an empty payload.");
				if (message.Id is long id)
				{
					TaskCompletionSource<string> completion;
					using (_sync.EnterScope())
					{
						_pending.TryGetValue(id, out completion);
						_pending.Remove(id);
					}
					if (completion is null)
						continue;
					if (message.Error is not null)
						completion.TrySetException(
							new InvalidOperationException(
								$"WebSocket JSON-RPC {message.Error.Code}: " +
								(message.Error.Message ?? "request rejected")));
					else
						completion.TrySetResult(message.Result);
					continue;
				}
				if (message.Method.EqualsIgnoreCase("eth_subscription") &&
					message.Parameters?.Result is BalancerRpcLog log)
				{
					if (log.Topics is not { Length: > 0 } ||
						!BalancerExtensions.SwapTopics.Contains(log.Topics[0],
							StringComparer.OrdinalIgnoreCase))
						continue;
					try
					{
						LogReceived?.Invoke(log);
					}
					catch (Exception error)
					{
						this.AddErrorLog(error);
					}
				}
			}
		}
		catch (OperationCanceledException) when (
			cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception error)
		{
			_socket?.Abort();
			this.AddWarningLog(
				"Balancer WebSocket receive loop stopped: {0}",
				error.Message);
			FailPending(error);
		}
	}

	private async ValueTask<string> ReceiveAsync(
		CancellationToken cancellationToken)
	{
		var socket = _socket ?? throw new InvalidOperationException(
			"The Balancer WebSocket is not connected.");
		using var target = new MemoryStream();
		var buffer = new byte[8192];
		while (true)
		{
			var result = await socket.ReceiveAsync(buffer, cancellationToken);
			if (result.MessageType == WebSocketMessageType.Close)
				throw new WebSocketException(
					$"Balancer WebSocket closed with status " +
					$"'{socket.CloseStatus}'.");
			if (result.MessageType != WebSocketMessageType.Text)
				throw new InvalidDataException(
					"Balancer WebSocket returned a non-text message.");
			if (target.Length + result.Count > _maximumMessageBytes)
				throw new InvalidDataException(
					"Balancer WebSocket message exceeds 1 MiB.");
			target.Write(buffer, 0, result.Count);
			if (result.EndOfMessage)
				return Encoding.UTF8.GetString(target.ToArray());
		}
	}

	private void FailPending(Exception error)
	{
		TaskCompletionSource<string>[] pending;
		using (_sync.EnterScope())
		{
			pending = [.. _pending.Values];
			_pending.Clear();
		}
		foreach (var completion in pending)
			completion.TrySetException(error);
	}
}
