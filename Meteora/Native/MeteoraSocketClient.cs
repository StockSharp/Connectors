namespace StockSharp.Meteora.Native;

sealed class MeteoraSocketClient : BaseLogReceiver
{
	private const int _maximumMessageLength = 8 * 1024 * 1024;

	private sealed class PendingSubscription
	{
		public string PoolAddress { get; init; }
		public TaskCompletionSource<long> Completion { get; init; }
	}

	private readonly Uri _endpoint;
	private readonly Func<string, string[], ValueTask> _logsHandler;
	private readonly Func<Exception, ValueTask> _errorHandler;
	private readonly Lock _sync = new();
	private readonly Dictionary<long, PendingSubscription> _pending = [];
	private readonly Dictionary<long, string> _subscriptions = [];
	private ClientWebSocket _socket;
	private CancellationTokenSource _lifetime;
	private Task _receiveTask;
	private long _requestId;
	private bool _isDisposed;

	public MeteoraSocketClient(string endpoint,
		Func<string, string[], ValueTask> logsHandler,
		Func<Exception, ValueTask> errorHandler)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"wss://{endpoint.TrimStart('/')}";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			_endpoint.Scheme is not ("ws" or "wss"))
			throw new ArgumentException(
				"Solana streaming endpoint must use WS or WSS.",
				nameof(endpoint));
		_logsHandler = logsHandler ?? throw new ArgumentNullException(
			nameof(logsHandler));
		_errorHandler = errorHandler ?? throw new ArgumentNullException(
			nameof(errorHandler));
	}

	public bool IsConnected => _socket?.State == WebSocketState.Open;

	public override string Name => "Meteora_WebSocket";

	public async ValueTask ConnectAsync(IEnumerable<string> poolAddresses,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		if (_socket is not null)
			throw new InvalidOperationException(
				"The Meteora WebSocket is already initialized.");
		var pools = (poolAddresses ?? []).Select(static address =>
			address.NormalizePublicKey()).Distinct(StringComparer.Ordinal)
			.ToArray();
		if (pools.Length == 0)
			throw new ArgumentException(
				"At least one Meteora pool is required for streaming.",
				nameof(poolAddresses));

		_socket = new();
		_socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
		_lifetime = new();
		await _socket.ConnectAsync(_endpoint, cancellationToken);
		_receiveTask = ReceiveLoopAsync(_lifetime.Token);

		var confirmations = new List<Task<long>>(pools.Length);
		foreach (var pool in pools)
		{
			var id = Interlocked.Increment(ref _requestId);
			var completion = new TaskCompletionSource<long>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			using (_sync.EnterScope())
				_pending.Add(id, new()
				{
					PoolAddress = pool,
					Completion = completion,
				});
			confirmations.Add(completion.Task);
			await SendAsync(new MeteoraSocketRequest<MeteoraSocketParameters>
			{
				Id = id,
				Method = "logsSubscribe",
				Parameters = new MeteoraSocketLogsParameters
				{
					Filter = new()
					{
						Mentions = [pool],
					},
					Config = new(),
				},
			}, cancellationToken);
		}
		await Task.WhenAll(confirmations).WaitAsync(cancellationToken);
	}

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		_lifetime?.Cancel();
		_socket?.Abort();
		_socket?.Dispose();
		_lifetime?.Dispose();
		using (_sync.EnterScope())
		{
			foreach (var pending in _pending.Values)
				pending.Completion.TrySetCanceled();
			_pending.Clear();
			_subscriptions.Clear();
		}
		base.DisposeManaged();
	}

	private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
	{
		var buffer = new byte[64 * 1024];
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				using var stream = new MemoryStream();
				WebSocketReceiveResult result;
				do
				{
					result = await _socket.ReceiveAsync(
						new ArraySegment<byte>(buffer), cancellationToken);
					if (result.MessageType == WebSocketMessageType.Close)
						throw new WebSocketException(
							"Solana streaming endpoint closed the connection.");
					if (stream.Length + result.Count > _maximumMessageLength)
						throw new InvalidDataException(
							"Solana streaming message exceeds the safety limit.");
					stream.Write(buffer, 0, result.Count);
				}
				while (!result.EndOfMessage);
				if (result.MessageType != WebSocketMessageType.Text)
					continue;
				var message = JsonConvert.DeserializeObject<MeteoraSocketMessage>(
					Encoding.UTF8.GetString(stream.GetBuffer(), 0,
						checked((int)stream.Length)));
				if (message is not null)
					await HandleAsync(message);
			}
		}
		catch (Exception error) when (error is not OperationCanceledException &&
			!cancellationToken.IsCancellationRequested && !_isDisposed)
		{
			FailPending(error);
			await _errorHandler(error);
		}
	}

	private async ValueTask HandleAsync(MeteoraSocketMessage message)
	{
		if (message.Id is long id)
		{
			PendingSubscription pending;
			using (_sync.EnterScope())
			{
				if (!_pending.Remove(id, out pending))
					return;
				if (message.Result is long subscription)
					_subscriptions[subscription] = pending.PoolAddress;
			}
			if (message.Error is not null)
				pending.Completion.TrySetException(
					new InvalidOperationException(
						$"Solana logsSubscribe failed ({message.Error.Code}): " +
						$"{message.Error.Message}"));
			else if (message.Result is long subscription)
				pending.Completion.TrySetResult(subscription);
			else
				pending.Completion.TrySetException(
					new InvalidDataException(
						"Solana logsSubscribe returned no subscription ID."));
			return;
		}

		if (!message.Method.Equals("logsNotification",
			StringComparison.Ordinal) ||
			message.Parameters?.Result?.Value is not { Error: null } value ||
			value.Signature.IsEmpty())
			return;
		string poolAddress;
		using (_sync.EnterScope())
			if (!_subscriptions.TryGetValue(message.Parameters.Subscription,
				out poolAddress))
				return;
		await _logsHandler(value.Signature, value.Logs ?? []);
	}

	private async ValueTask SendAsync<TParameters>(
		MeteoraSocketRequest<TParameters> request,
		CancellationToken cancellationToken)
		where TParameters : MeteoraSocketParameters
	{
		var data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request,
			new JsonSerializerSettings
			{
				NullValueHandling = NullValueHandling.Ignore,
			}));
		await _socket.SendAsync(new ArraySegment<byte>(data),
			WebSocketMessageType.Text, true, cancellationToken);
	}

	private void FailPending(Exception error)
	{
		PendingSubscription[] pending;
		using (_sync.EnterScope())
		{
			pending = [.. _pending.Values];
			_pending.Clear();
		}
		foreach (var item in pending)
			item.Completion.TrySetException(error);
	}
}
