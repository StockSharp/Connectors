namespace StockSharp.ManifestTrade.Native;

sealed class ManifestTradeSocketClient : BaseLogReceiver
{
	private const int _maximumMessageLength = 16 * 1024 * 1024;

	private enum SubscriptionKinds
	{
		Account,
		Logs,
	}

	private sealed class PendingSubscription
	{
		public string MarketAddress { get; init; }
		public SubscriptionKinds Kind { get; init; }
		public TaskCompletionSource<long> Completion { get; init; }
	}

	private readonly record struct ActiveSubscription(string MarketAddress,
		SubscriptionKinds Kind);

	private readonly Uri _endpoint;
	private readonly Func<string, ManifestTradeRpcAccount, long, ValueTask>
		_accountHandler;
	private readonly Func<string, string[], ValueTask> _logsHandler;
	private readonly Func<Exception, ValueTask> _errorHandler;
	private readonly Lock _sync = new();
	private readonly Dictionary<long, PendingSubscription> _pending = [];
	private readonly Dictionary<long, ActiveSubscription> _subscriptions = [];
	private ClientWebSocket _socket;
	private CancellationTokenSource _lifetime;
	private Task _receiveTask;
	private long _requestId;
	private bool _isDisposed;

	public ManifestTradeSocketClient(string endpoint,
		Func<string, ManifestTradeRpcAccount, long, ValueTask> accountHandler,
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
		_accountHandler = accountHandler ?? throw new ArgumentNullException(
			nameof(accountHandler));
		_logsHandler = logsHandler ?? throw new ArgumentNullException(
			nameof(logsHandler));
		_errorHandler = errorHandler ?? throw new ArgumentNullException(
			nameof(errorHandler));
	}

	public bool IsConnected => _socket?.State == WebSocketState.Open;

	public override string Name => "ManifestTrade_WebSocket";

	public async ValueTask ConnectAsync(IEnumerable<string> marketAddresses,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		if (_socket is not null)
			throw new InvalidOperationException(
				"The Manifest Trade WebSocket is already initialized.");
		var markets = (marketAddresses ?? []).Select(static address =>
			address.NormalizePublicKey()).Distinct(StringComparer.Ordinal)
			.ToArray();
		if (markets.Length == 0)
			throw new ArgumentException(
				"At least one Manifest market is required for streaming.",
				nameof(marketAddresses));

		_socket = new();
		_socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
		_lifetime = new();
		await _socket.ConnectAsync(_endpoint, cancellationToken);
		_receiveTask = ReceiveLoopAsync(_lifetime.Token);
		var confirmations = new List<Task<long>>(markets.Length * 2);
		foreach (var market in markets)
		{
			confirmations.Add(await SubscribeAccountAsync(market,
				cancellationToken));
			confirmations.Add(await SubscribeLogsAsync(market,
				cancellationToken));
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

	private async ValueTask<Task<long>> SubscribeAccountAsync(string market,
		CancellationToken cancellationToken)
	{
		var pending = AddPending(market, SubscriptionKinds.Account);
		await SendAsync(new ManifestTradeSocketRequest<
			ManifestTradeSocketParameters>
		{
			Id = pending.Id,
			Method = "accountSubscribe",
			Parameters = new ManifestTradeSocketAccountParameters
			{
				Address = market,
				Config = new(),
			},
		}, cancellationToken);
		return pending.Completion;
	}

	private async ValueTask<Task<long>> SubscribeLogsAsync(string market,
		CancellationToken cancellationToken)
	{
		var pending = AddPending(market, SubscriptionKinds.Logs);
		await SendAsync(new ManifestTradeSocketRequest<
			ManifestTradeSocketParameters>
		{
			Id = pending.Id,
			Method = "logsSubscribe",
			Parameters = new ManifestTradeSocketLogsParameters
			{
				Filter = new() { Mentions = [market] },
				Config = new(),
			},
		}, cancellationToken);
		return pending.Completion;
	}

	private (long Id, Task<long> Completion) AddPending(string market,
		SubscriptionKinds kind)
	{
		var id = Interlocked.Increment(ref _requestId);
		var completion = new TaskCompletionSource<long>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		using (_sync.EnterScope())
			_pending.Add(id, new()
			{
				MarketAddress = market,
				Kind = kind,
				Completion = completion,
			});
		return (id, completion.Task);
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
				var json = Encoding.UTF8.GetString(stream.GetBuffer(), 0,
					checked((int)stream.Length));
				await HandleAsync(json);
			}
		}
		catch (Exception error) when (error is not OperationCanceledException &&
			!cancellationToken.IsCancellationRequested && !_isDisposed)
		{
			FailPending(error);
			await _errorHandler(error);
		}
	}

	private async ValueTask HandleAsync(string json)
	{
		ManifestTradeSocketEnvelope envelope;
		try
		{
			envelope = JsonConvert.DeserializeObject<
				ManifestTradeSocketEnvelope>(json);
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Solana WebSocket returned malformed JSON.", error);
		}
		if (envelope is null)
			return;
		if (envelope.Id is long id)
		{
			HandleConfirmation(id, envelope);
			return;
		}
		if (envelope.Method.Equals("logsNotification", StringComparison.Ordinal))
		{
			var message = JsonConvert.DeserializeObject<
				ManifestTradeSocketLogsMessage>(json);
			if (message?.Parameters?.Result?.Value is
				{ Error: null } value && !value.Signature.IsEmpty() &&
				TryGetSubscription(message.Parameters.Subscription,
					SubscriptionKinds.Logs, out _))
				await _logsHandler(value.Signature, value.Logs ?? []);
			return;
		}
		if (envelope.Method.Equals("accountNotification",
			StringComparison.Ordinal))
		{
			var message = JsonConvert.DeserializeObject<
				ManifestTradeSocketAccountMessage>(json);
			if (message?.Parameters?.Result?.Value is { } account &&
				TryGetSubscription(message.Parameters.Subscription,
					SubscriptionKinds.Account, out var market))
				await _accountHandler(market, account,
					message.Parameters.Result.Context?.Slot ?? 0);
		}
	}

	private void HandleConfirmation(long id,
		ManifestTradeSocketEnvelope envelope)
	{
		PendingSubscription pending;
		using (_sync.EnterScope())
		{
			if (!_pending.Remove(id, out pending))
				return;
			if (envelope.Result is long subscription)
				_subscriptions[subscription] = new(pending.MarketAddress,
					pending.Kind);
		}
		if (envelope.Error is not null)
			pending.Completion.TrySetException(new InvalidOperationException(
				$"Solana subscription failed ({envelope.Error.Code}): " +
				$"{envelope.Error.Message}"));
		else if (envelope.Result is long subscription)
			pending.Completion.TrySetResult(subscription);
		else
			pending.Completion.TrySetException(new InvalidDataException(
				"Solana subscription returned no subscription ID."));
	}

	private bool TryGetSubscription(long id, SubscriptionKinds kind,
		out string market)
	{
		using (_sync.EnterScope())
		{
			if (_subscriptions.TryGetValue(id, out var subscription) &&
				subscription.Kind == kind)
			{
				market = subscription.MarketAddress;
				return true;
			}
		}
		market = null;
		return false;
	}

	private async ValueTask SendAsync<TParameters>(
		ManifestTradeSocketRequest<TParameters> request,
		CancellationToken cancellationToken)
		where TParameters : ManifestTradeSocketParameters
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
