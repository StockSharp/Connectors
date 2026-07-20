namespace StockSharp.Raydium.Native;

sealed class RaydiumSocketClient : BaseLogReceiver
{
	private const int _maximumMessageLength = 8 * 1024 * 1024;

	private sealed class PendingSubscription
	{
		public string AccountAddress { get; init; }
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

	public RaydiumSocketClient(string endpoint,
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

	public override string Name => "Raydium_WebSocket";

	public async ValueTask ConnectAsync(IEnumerable<string> accountAddresses,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		if (_socket is not null)
			throw new InvalidOperationException(
				"The Raydium WebSocket is already initialized.");
		var accounts = (accountAddresses ?? []).Select(static address =>
			address.NormalizePublicKey()).Distinct(StringComparer.Ordinal)
			.ToArray();
		if (accounts.Length == 0)
			throw new ArgumentException(
				"At least one Raydium pool is required for streaming.",
				nameof(accountAddresses));

		_socket = new();
		_socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
		_lifetime = new();
		await _socket.ConnectAsync(_endpoint, cancellationToken);
		_receiveTask = ReceiveLoopAsync(_lifetime.Token);

		var confirmations = new List<Task<long>>(accounts.Length);
		foreach (var account in accounts)
		{
			var id = Interlocked.Increment(ref _requestId);
			var completion = new TaskCompletionSource<long>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			using (_sync.EnterScope())
				_pending.Add(id, new()
				{
					AccountAddress = account,
					Completion = completion,
				});
			confirmations.Add(completion.Task);
			await SendAsync(new RaydiumSocketRequest<RaydiumSocketParameters>
			{
				Id = id,
				Method = "logsSubscribe",
				Parameters = new RaydiumSocketLogsParameters
				{
					Filter = new()
					{
						Mentions = [account],
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
				var message = JsonConvert.DeserializeObject<RaydiumSocketMessage>(
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

	private async ValueTask HandleAsync(RaydiumSocketMessage message)
	{
		if (message.Id is long id)
		{
			PendingSubscription pending;
			using (_sync.EnterScope())
			{
				if (!_pending.Remove(id, out pending))
					return;
				if (message.Result is long subscription)
					_subscriptions[subscription] = pending.AccountAddress;
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

		if (!string.Equals(message.Method, "logsNotification",
			StringComparison.Ordinal) ||
			message.Parameters?.Result?.Value is not { Error: null } value ||
			value.Signature.IsEmpty())
			return;
		using (_sync.EnterScope())
			if (!_subscriptions.ContainsKey(message.Parameters.Subscription))
				return;
		await _logsHandler(value.Signature, value.Logs ?? []);
	}

	private async ValueTask SendAsync<TParameters>(
		RaydiumSocketRequest<TParameters> request,
		CancellationToken cancellationToken)
		where TParameters : RaydiumSocketParameters
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
