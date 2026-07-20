namespace StockSharp.Grvt.Native;

sealed class GrvtWebSocketClient : BaseLogReceiver
{
	private readonly record struct Subscription(string Stream, string Selector);

	private sealed class PendingRequest
	{
		public Subscription Subscription { get; init; }
		public bool IsSubscribe { get; init; }
		public TaskCompletionSource<bool> Completion { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
	}

	private readonly string _endpoint;
	private readonly string _cookie;
	private readonly string _accountId;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<Subscription> _subscriptions = [];
	private readonly Dictionary<uint, PendingRequest> _pendingRequests = [];
	private readonly Dictionary<Subscription, string> _sequences = [];
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
	private long _requestId;

	public GrvtWebSocketClient(string endpoint, string cookie, string accountId,
		WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		_cookie = cookie;
		_accountId = accountId;
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
		if (_cookie.IsEmpty() != _accountId.IsEmpty())
			throw new ArgumentException(
				"GRVT private WebSocket requires both cookie and account ID.");
	}

	public override string Name => _cookie.IsEmpty()
		? "GRVT_MarketWs"
		: "GRVT_TradingWs";

	public event Func<string, GrvtTicker, CancellationToken, ValueTask>
		TickerReceived;
	public event Func<string, GrvtOrderBook, CancellationToken, ValueTask>
		BookReceived;
	public event Func<string, GrvtTrade, CancellationToken, ValueTask>
		TradeReceived;
	public event Func<string, GrvtCandlestick, CancellationToken, ValueTask>
		CandlestickReceived;
	public event Func<string, GrvtOrder, CancellationToken, ValueTask>
		OrderReceived;
	public event Func<string, GrvtFill, CancellationToken, ValueTask>
		FillReceived;
	public event Func<string, GrvtPosition, CancellationToken, ValueTask>
		PositionReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		FailPending(new ObjectDisposedException(nameof(GrvtWebSocketClient)));
		_client?.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"GRVT WebSocket is already initialized.");
		var client = _client = CreateClient();
		try
		{
			await client.ConnectAsync(cancellationToken);
		}
		catch
		{
			await DisconnectAsync(cancellationToken);
			throw;
		}
	}

	public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		if (client is null)
			return;
		FailPending(new InvalidOperationException(
			"GRVT WebSocket disconnected."));
		try
		{
			if (client.IsConnected)
				await client.DisconnectAsync(cancellationToken);
		}
		finally
		{
			client.Dispose();
			using (_sync.EnterScope())
				_sequences.Clear();
		}
	}

	public ValueTask SubscribeAsync(string stream, string selector,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(stream.ThrowIfEmpty(nameof(stream)),
			selector.ThrowIfEmpty(nameof(selector))), true, cancellationToken);

	public ValueTask UnsubscribeAsync(string stream, string selector,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(stream.ThrowIfEmpty(nameof(stream)),
			selector.ThrowIfEmpty(nameof(selector))), false, cancellationToken);

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
		client.Init += InitializeSocket;
		return client;
	}

	private void InitializeSocket(ClientWebSocket socket)
	{
		socket.Options.SetRequestHeader("User-Agent",
			"StockSharp-GRVT-Connector/1.0");
		if (_cookie.IsEmpty())
			return;
		socket.Options.SetRequestHeader("Cookie", _cookie);
		socket.Options.SetRequestHeader("X-Grvt-Account-Id", _accountId);
	}

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			Subscription[] subscriptions;
			using (_sync.EnterScope())
			{
				_sequences.Clear();
				subscriptions = [.. _subscriptions];
			}
			foreach (var subscription in subscriptions)
				await SendRequestAsync(client, subscription, true, false,
					cancellationToken);
		}
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(Subscription subscription,
		bool isSubscribe, CancellationToken cancellationToken)
	{
		bool changed;
		using (_sync.EnterScope())
		{
			changed = isSubscribe
				? _subscriptions.Add(subscription)
				: _subscriptions.Remove(subscription);
			if (!isSubscribe)
				_sequences.Remove(subscription);
		}
		if (!changed)
			return;
		var client = _client;
		if (client?.IsConnected != true)
			return;
		try
		{
			await SendRequestAsync(client, subscription, isSubscribe, true,
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (isSubscribe)
					_subscriptions.Remove(subscription);
				else
					_subscriptions.Add(subscription);
			}
			throw;
		}
	}

	private async ValueTask SendRequestAsync(WebSocketClient client,
		Subscription subscription, bool isSubscribe, bool waitResponse,
		CancellationToken cancellationToken)
	{
		var id = NextRequestId();
		PendingRequest pending = null;
		if (waitResponse)
		{
			pending = new()
			{
				Subscription = subscription,
				IsSubscribe = isSubscribe,
			};
			using (_sync.EnterScope())
				_pendingRequests.Add(id, pending);
		}
		try
		{
			await _sendSync.WaitAsync(cancellationToken);
			try
			{
				await client.SendAsync(new GrvtWebSocketRequest
				{
					Method = isSubscribe ? "subscribe" : "unsubscribe",
					Parameters = new()
					{
						Stream = subscription.Stream,
						Selectors = [subscription.Selector],
					},
					Id = id,
				}, cancellationToken);
			}
			finally
			{
				_sendSync.Release();
			}
			if (pending is not null)
				await pending.Completion.Task.WaitAsync(TimeSpan.FromSeconds(10),
					cancellationToken);
		}
		finally
		{
			if (pending is not null)
				using (_sync.EnterScope())
					_pendingRequests.Remove(id);
		}
	}

	private uint NextRequestId()
	{
		var value = Interlocked.Increment(ref _requestId);
		return unchecked((uint)((value - 1) % uint.MaxValue + 1));
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
			var header = Deserialize<GrvtWebSocketHeader>(payload);
			if (header.Id is uint id)
			{
				ProcessResponse(id, payload);
				return;
			}
			if (header.Error is { } error)
				throw new InvalidOperationException(
					$"GRVT WebSocket error {error.Code}: {error.Message}");
			if (header.Stream.IsEmpty() || header.Selector.IsEmpty())
				throw new InvalidDataException(
					"GRVT WebSocket message has no stream selector.");

			var subscription = new Subscription(header.Stream, header.Selector);
			if (!AcceptSequence(subscription, header.SequenceNumber,
				header.PreviousSequenceNumber, out var sequenceError))
			{
				if (sequenceError is null)
					return;
				await RaiseErrorAsync(sequenceError, cancellationToken);
				await ResubscribeAsync(subscription, cancellationToken);
				return;
			}

			switch (header.Stream)
			{
				case "v1.ticker.s":
					await RaiseFeedAsync(payload, TickerReceived,
						cancellationToken);
					break;
				case "v1.book.s":
					await RaiseFeedAsync(payload, BookReceived,
						cancellationToken);
					break;
				case "v1.trade":
					await RaiseFeedAsync(payload, TradeReceived,
						cancellationToken);
					break;
				case "v1.candle":
					await RaiseFeedAsync(payload, CandlestickReceived,
						cancellationToken);
					break;
				case "v1.order":
					await RaiseFeedAsync(payload, OrderReceived,
						cancellationToken);
					break;
				case "v1.fill":
					await RaiseFeedAsync(payload, FillReceived,
						cancellationToken);
					break;
				case "v1.position":
					await RaiseFeedAsync(payload, PositionReceived,
						cancellationToken);
					break;
				default:
					throw new InvalidDataException(
						$"Unsupported GRVT WebSocket stream '{header.Stream}'.");
			}
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or InvalidOperationException or FormatException or
			OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private void ProcessResponse(uint id, string payload)
	{
		PendingRequest pending;
		using (_sync.EnterScope())
			_pendingRequests.TryGetValue(id, out pending);
		if (pending is null)
			return;
		try
		{
			var response = Deserialize<GrvtWebSocketResponse>(payload);
			if (response.Error is { } error)
				throw new InvalidOperationException(
					$"GRVT WebSocket error {error.Code}: {error.Message}");
			if (response.Result is null ||
				!response.Result.Stream.EqualsIgnoreCase(
					pending.Subscription.Stream))
				throw new InvalidDataException(
					"GRVT returned an invalid subscription response.");
			var selectors = pending.IsSubscribe
				? response.Result.Subscriptions
				: response.Result.Unsubscriptions;
			if (!(selectors ?? []).Any(selector =>
				selector.EqualsIgnoreCase(pending.Subscription.Selector)))
				throw new InvalidOperationException(
					$"GRVT did not {(pending.IsSubscribe ? "subscribe" : "unsubscribe")} " +
					$"'{pending.Subscription.Selector}'.");
			pending.Completion.TrySetResult(true);
		}
		catch (Exception error)
		{
			pending.Completion.TrySetException(error);
		}
	}

	private bool AcceptSequence(Subscription subscription, string sequence,
		string previous, out Exception error)
	{
		error = null;
		if (sequence.IsEmpty())
		{
			error = new InvalidDataException(
				$"GRVT stream '{subscription.Stream}' omitted its sequence number.");
			return false;
		}
		using (_sync.EnterScope())
		{
			if (!_sequences.TryGetValue(subscription, out var last))
			{
				_sequences[subscription] = sequence;
				return true;
			}
			if (sequence.EqualsIgnoreCase(last))
				return false;
			if (!previous.IsEmpty() && !previous.EqualsIgnoreCase(last))
			{
				_sequences.Remove(subscription);
				error = new InvalidDataException(
					$"GRVT sequence gap in '{subscription.Stream}' selector " +
					$"'{subscription.Selector}': expected previous '{last}', " +
					$"received '{previous}'.");
				return false;
			}
			_sequences[subscription] = sequence;
			return true;
		}
	}

	private async ValueTask ResubscribeAsync(Subscription subscription,
		CancellationToken cancellationToken)
	{
		var client = _client;
		bool desired;
		using (_sync.EnterScope())
			desired = _subscriptions.Contains(subscription);
		if (!desired || client?.IsConnected != true)
			return;
		await SendRequestAsync(client, subscription, false, false,
			cancellationToken);
		await SendRequestAsync(client, subscription, true, false,
			cancellationToken);
	}

	private async ValueTask RaiseFeedAsync<TFeed>(string payload,
		Func<string, TFeed, CancellationToken, ValueTask> handler,
		CancellationToken cancellationToken)
	{
		if (handler is null)
			return;
		var message = Deserialize<GrvtWebSocketFeed<TFeed>>(payload);
		if (message.Feed is null)
			throw new InvalidDataException("GRVT WebSocket returned an empty feed.");
		await handler(message.Selector, message.Feed, cancellationToken);
	}

	private TMessage Deserialize<TMessage>(string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<TMessage>(payload, _jsonSettings)
				?? throw new InvalidDataException(
					"GRVT WebSocket returned an empty message.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"GRVT WebSocket returned malformed JSON.", error);
		}
	}

	private void FailPending(Exception error)
	{
		PendingRequest[] pending;
		using (_sync.EnterScope())
		{
			pending = [.. _pendingRequests.Values];
			_pendingRequests.Clear();
		}
		foreach (var request in pending)
			request.Completion.TrySetException(error);
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;
}
