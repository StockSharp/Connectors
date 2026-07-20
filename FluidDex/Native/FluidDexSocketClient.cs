namespace StockSharp.FluidDex.Native;

sealed class FluidDexSocketClient : BaseLogReceiver
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

	public FluidDexSocketClient(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			!(_endpoint.Scheme.EqualsIgnoreCase("ws") ||
				_endpoint.Scheme.EqualsIgnoreCase("wss")))
			throw new ArgumentException(
				"EVM WebSocket endpoint must be an absolute WS or WSS URI.",
				nameof(endpoint));
	}

	public override string Name => "FluidDex_WebSocket";

	public event Action<FluidDexRpcLog> LogReceived;

	public async ValueTask ConnectAsync(IEnumerable<FluidDexMarket> markets,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(markets);
		if (_socket is not null)
			throw new InvalidOperationException(
				"The Fluid DEX WebSocket is already connected.");
		var socket = new ClientWebSocket();
		socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
		socket.Options.SetRequestHeader("User-Agent",
			"StockSharp-FluidDex-Connector/1.0");
		var lifetime = new CancellationTokenSource();
		try
		{
			await socket.ConnectAsync(_endpoint, cancellationToken);
			_socket = socket;
			_lifetime = lifetime;
			_receiveTask = ReceiveLoopAsync(lifetime.Token);
			foreach (var market in markets
				.GroupBy(static market => market.PoolId,
					StringComparer.OrdinalIgnoreCase)
				.Select(static group => group.First()))
				await SubscribeAsync(market, cancellationToken);
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
		FailPending(new ObjectDisposedException(nameof(FluidDexSocketClient)));
		_sendGate.Dispose();
		_socket = null;
		_lifetime = null;
		_receiveTask = null;
		base.DisposeManaged();
	}

	private async ValueTask SubscribeAsync(FluidDexMarket market,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		var id = Interlocked.Increment(ref _requestId);
		var completion = new TaskCompletionSource<string>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		using (_sync.EnterScope())
			_pending.Add(id, completion);
		var payload = JsonConvert.SerializeObject(new FluidDexSocketRequest
		{
			Id = id,
			Parameters = new()
			{
				Filter = new()
				{
					Address = market.PoolId,
					Topics = [FluidDexExtensions.SwapTopic],
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
					"Fluid DEX returned an empty WebSocket subscription id.");
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
			"The Fluid DEX WebSocket is not connected.");
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
				FluidDexSocketMessage message;
				try
				{
					message = JsonConvert.DeserializeObject<
						FluidDexSocketMessage>(payload, _jsonSettings);
				}
				catch (JsonException error)
				{
					throw new InvalidDataException(
						"Fluid DEX WebSocket returned an unexpected payload.",
						error);
				}
				if (message is null)
					throw new InvalidDataException(
						"Fluid DEX WebSocket returned an empty payload.");
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
					message.Parameters?.Result is FluidDexRpcLog log)
				{
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
			this.AddWarningLog(
				"Fluid DEX WebSocket receive loop stopped: {0}",
				error.Message);
			FailPending(error);
		}
	}

	private async ValueTask<string> ReceiveAsync(
		CancellationToken cancellationToken)
	{
		var socket = _socket ?? throw new InvalidOperationException(
			"The Fluid DEX WebSocket is not connected.");
		using var target = new MemoryStream();
		var buffer = new byte[8192];
		while (true)
		{
			var result = await socket.ReceiveAsync(buffer, cancellationToken);
			if (result.MessageType == WebSocketMessageType.Close)
				throw new WebSocketException(
					$"Fluid DEX WebSocket closed with status " +
					$"'{socket.CloseStatus}'.");
			if (result.MessageType != WebSocketMessageType.Text)
				throw new InvalidDataException(
					"Fluid DEX WebSocket returned a non-text message.");
			if (target.Length + result.Count > _maximumMessageBytes)
				throw new InvalidDataException(
					"Fluid DEX WebSocket message exceeds 1 MiB.");
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
