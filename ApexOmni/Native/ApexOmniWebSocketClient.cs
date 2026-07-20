namespace StockSharp.ApexOmni.Native;

sealed class ApexOmniWebSocketClient : BaseLogReceiver
{
	private readonly record struct Subscription(
		ApexOmniWebSocketOperations Operation, string Topic);

	private sealed class PendingRequest
	{
		public TaskCompletionSource<bool> Completion { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
	}

	private sealed class BookState
	{
		private readonly SortedDictionary<decimal, decimal> _bids =
			new(Comparer<decimal>.Create(static (left, right) =>
				right.CompareTo(left)));
		private readonly SortedDictionary<decimal, decimal> _asks = [];

		public long UpdateId { get; private set; }
		private bool _isSnapshotAvailable;

		public ApexOmniOrderBook Apply(ApexOmniOrderBook update,
			ApexOmniWebSocketTypes type, int depth)
		{
			ArgumentNullException.ThrowIfNull(update);
			if (type == ApexOmniWebSocketTypes.Snapshot)
			{
				_bids.Clear();
				_asks.Clear();
				_isSnapshotAvailable = true;
			}
			else if (!_isSnapshotAvailable || update.UpdateId != UpdateId + 1)
			{
				throw new InvalidDataException(
					$"ApeX Omni order-book sequence gap: expected " +
					$"{UpdateId + 1}, received {update.UpdateId}.");
			}
			ApplyLevels(_bids, update.Bids);
			ApplyLevels(_asks, update.Asks);
			UpdateId = update.UpdateId;
			return new()
			{
				Symbol = update.Symbol,
				UpdateId = update.UpdateId,
				Bids = ToLevels(_bids, depth),
				Asks = ToLevels(_asks, depth),
			};
		}

		private static void ApplyLevels(
			SortedDictionary<decimal, decimal> side,
			ApexOmniBookLevel[] levels)
		{
			foreach (var level in levels ?? [])
			{
				var price = level.Price.ParseRequiredDecimal("book price");
				var size = level.Size.ParseRequiredDecimal("book size");
				if (size == 0)
					side.Remove(price);
				else if (size > 0)
					side[price] = size;
				else
					throw new InvalidDataException(
						"ApeX Omni returned a negative book size.");
			}
		}

		private static ApexOmniBookLevel[] ToLevels(
			SortedDictionary<decimal, decimal> side, int depth)
			=> [.. side.Take(depth).Select(static pair =>
				new ApexOmniBookLevel
				{
					Price = pair.Key.ToWire(),
					Size = pair.Value.ToWire(),
				})];
	}

	private readonly string _endpoint;
	private readonly ApexOmniRestClient _authentication;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<string> _subscriptions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<Subscription, PendingRequest> _pending = [];
	private readonly Dictionary<string, BookState> _books =
		new(StringComparer.OrdinalIgnoreCase);
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

	public ApexOmniWebSocketClient(string endpoint,
		ApexOmniRestClient authentication, WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim().TrimEnd('/');
		_authentication = authentication;
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
		if (_authentication is not null && !_authentication.IsAuthenticated)
			throw new ArgumentException(
				"ApeX Omni private WebSocket requires API credentials.",
				nameof(authentication));
	}

	public override string Name => _authentication is null
		? "APEXOMNI_PublicWs"
		: "APEXOMNI_PrivateWs";

	public event Func<string, ApexOmniTicker, long, CancellationToken, ValueTask>
		TickerReceived;
	public event Func<string, ApexOmniOrderBook, long, CancellationToken,
		ValueTask> BookReceived;
	public event Func<string, ApexOmniTrade, long, CancellationToken, ValueTask>
		TradeReceived;
	public event Func<string, ApexOmniWebSocketCandle, long, CancellationToken,
		ValueTask> CandleReceived;
	public event Func<ApexOmniPrivateFeed, CancellationToken, ValueTask>
		PrivateReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

	protected override void DisposeManaged()
	{
		FailPending(new ObjectDisposedException(nameof(ApexOmniWebSocketClient)));
		_client?.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"ApeX Omni WebSocket is already initialized.");
		var client = _client = CreateClient();
		try
		{
			await client.ConnectAsync(cancellationToken);
			if (_authentication is not null)
				await AuthenticateAsync(client, true, cancellationToken);
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
			"ApeX Omni WebSocket disconnected."));
		try
		{
			if (client.IsConnected)
				await client.DisconnectAsync(cancellationToken);
		}
		finally
		{
			client.Dispose();
			using (_sync.EnterScope())
				_books.Clear();
		}
	}

	public ValueTask SubscribeAsync(string topic,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(topic, true, cancellationToken);

	public ValueTask UnsubscribeAsync(string topic,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(topic, false, cancellationToken);

	public async ValueTask SendPingAsync(CancellationToken cancellationToken)
	{
		var client = _client;
		if (client?.IsConnected != true)
			return;
		await SendAsync(client, new()
		{
			Operation = ApexOmniWebSocketOperations.Ping,
			Arguments = [GetTimestamp().ToString(CultureInfo.InvariantCulture)],
		}, cancellationToken);
	}

	private WebSocketClient CreateClient()
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			CreateConnectionEndpoint(),
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
			"User-Agent", "StockSharp-ApeX-Omni-Connector/1.0");
		return client;
	}

	private string CreateConnectionEndpoint()
		=> _endpoint + (_authentication is null
			? "/realtime_public"
			: "/realtime_private") + "?v=2&timestamp=" +
			GetTimestamp().ToString(CultureInfo.InvariantCulture);

	private long GetTimestamp()
		=> _authentication?.CurrentTimestamp ??
			DateTime.UtcNow.ToUnixMilliseconds();

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			using (_sync.EnterScope())
				_books.Clear();
			if (_authentication is not null)
				await AuthenticateAsync(client, false, cancellationToken);
			string[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _subscriptions];
			foreach (var topic in subscriptions)
				await SendSubscriptionAsync(client, topic, true, false,
					cancellationToken);
		}
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask AuthenticateAsync(WebSocketClient client,
		bool waitResponse, CancellationToken cancellationToken)
	{
		var timestamp = _authentication.CurrentTimestamp;
		var login = _authentication.CreateWebSocketLogin(timestamp);
		var inner = JsonConvert.SerializeObject(login, _jsonSettings);
		await SendRequestAsync(client,
			new(ApexOmniWebSocketOperations.Login, string.Empty),
			new()
			{
				Operation = ApexOmniWebSocketOperations.Login,
				Arguments = [inner],
			}, waitResponse, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(string topic,
		bool isSubscribe, CancellationToken cancellationToken)
	{
		topic = topic.ThrowIfEmpty(nameof(topic));
		bool changed;
		using (_sync.EnterScope())
		{
			changed = isSubscribe
				? _subscriptions.Add(topic)
				: _subscriptions.Remove(topic);
			if (!isSubscribe)
				_books.Remove(topic);
		}
		if (!changed)
			return;
		var client = _client;
		if (client?.IsConnected != true)
			return;
		try
		{
			await SendSubscriptionAsync(client, topic, isSubscribe, true,
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (isSubscribe)
					_subscriptions.Remove(topic);
				else
					_subscriptions.Add(topic);
			}
			throw;
		}
	}

	private ValueTask SendSubscriptionAsync(WebSocketClient client,
		string topic, bool isSubscribe, bool waitResponse,
		CancellationToken cancellationToken)
	{
		var operation = isSubscribe
			? ApexOmniWebSocketOperations.Subscribe
			: ApexOmniWebSocketOperations.Unsubscribe;
		return SendRequestAsync(client, new(operation, topic), new()
		{
			Operation = operation,
			Arguments = [topic],
		}, waitResponse, cancellationToken);
	}

	private async ValueTask SendRequestAsync(WebSocketClient client,
		Subscription subscription, ApexOmniWebSocketRequest request,
		bool waitResponse, CancellationToken cancellationToken)
	{
		PendingRequest pending = null;
		if (waitResponse)
		{
			pending = new();
			using (_sync.EnterScope())
			{
				if (!_pending.TryAdd(subscription, pending))
					throw new InvalidOperationException(
						$"ApeX Omni request '{subscription.Operation}' for " +
						$"'{subscription.Topic}' is already pending.");
			}
		}
		try
		{
			await SendAsync(client, request, cancellationToken);
			if (pending is not null)
				await pending.Completion.Task.WaitAsync(TimeSpan.FromSeconds(10),
					cancellationToken);
		}
		finally
		{
			if (pending is not null)
				using (_sync.EnterScope())
					_pending.Remove(subscription);
		}
	}

	private async ValueTask SendAsync(WebSocketClient client,
		ApexOmniWebSocketRequest request, CancellationToken cancellationToken)
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
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var header = Deserialize<ApexOmniWebSocketHeader>(payload);
			if (header.Operation == ApexOmniWebSocketOperations.Ping)
			{
				await SendAsync(client, new()
				{
					Operation = ApexOmniWebSocketOperations.Pong,
					Arguments = header.Arguments ??
						[GetTimestamp().ToString(CultureInfo.InvariantCulture)],
				}, cancellationToken);
				return;
			}
			if (header.Operation == ApexOmniWebSocketOperations.Pong)
				return;
			if (header.IsSuccess is not null)
			{
				ProcessResponse(header);
				return;
			}
			if (!header.Topic.IsEmpty())
			{
				await ProcessPublicFeedAsync(payload, header, cancellationToken);
				return;
			}
			if (_authentication is not null)
			{
				var privateFeed = Deserialize<ApexOmniPrivateFeed>(payload);
				if (!privateFeed.HasData())
					throw new InvalidDataException(
						"ApeX Omni WebSocket returned an unrecognized private message.");
				if (PrivateReceived is { } privateHandler)
					await privateHandler(privateFeed, cancellationToken);
				return;
			}
			throw new InvalidDataException(
				"ApeX Omni WebSocket returned an unrecognized message.");
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or InvalidOperationException or FormatException or
			OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask ProcessPublicFeedAsync(string payload,
		ApexOmniWebSocketHeader header,
		CancellationToken cancellationToken)
	{
		if (header.Topic.StartsWith("orderBook",
			StringComparison.OrdinalIgnoreCase))
		{
			var feed = Deserialize<ApexOmniWebSocketFeed<ApexOmniOrderBook>>(
				payload);
			BookState state;
			using (_sync.EnterScope())
			{
				if (!_books.TryGetValue(feed.Topic, out state))
					_books.Add(feed.Topic, state = new());
			}
			ApexOmniOrderBook book;
			try
			{
				book = state.Apply(feed.Data, feed.Type,
					GetDepthFromTopic(feed.Topic));
			}
			catch (InvalidDataException error)
			{
				await RaiseErrorAsync(error, cancellationToken);
				await ResubscribeAsync(feed.Topic, cancellationToken);
				return;
			}
			if (BookReceived is { } bookHandler)
				await bookHandler(feed.Topic, book, feed.Timestamp,
					cancellationToken);
			return;
		}
		if (header.Topic.StartsWith("recentlyTrade.",
			StringComparison.OrdinalIgnoreCase))
		{
			var feed = Deserialize<ApexOmniWebSocketFeed<ApexOmniTrade[]>>(
				payload);
			if (TradeReceived is { } tradeHandler)
				foreach (var trade in feed.Data ?? [])
					await tradeHandler(feed.Topic, trade, feed.Timestamp,
						cancellationToken);
			return;
		}
		if (header.Topic.StartsWith("instrumentInfo.",
			StringComparison.OrdinalIgnoreCase))
		{
			var feed = Deserialize<ApexOmniWebSocketFeed<ApexOmniTicker>>(
				payload);
			if (TickerReceived is { } tickerHandler)
				await tickerHandler(feed.Topic, feed.Data, feed.Timestamp,
					cancellationToken);
			return;
		}
		if (header.Topic.StartsWith("candle.",
			StringComparison.OrdinalIgnoreCase))
		{
			var feed = Deserialize<ApexOmniWebSocketFeed<
				ApexOmniWebSocketCandle[]>>(payload);
			if (CandleReceived is { } candleHandler)
				foreach (var candle in feed.Data ?? [])
					await candleHandler(feed.Topic, candle, feed.Timestamp,
						cancellationToken);
			return;
		}
		if (_authentication is not null &&
			header.Topic.EqualsIgnoreCase("ws_zk_accounts_v3"))
		{
			if (PrivateReceived is { } privateHandler)
				await privateHandler(Deserialize<ApexOmniPrivateFeed>(payload),
					cancellationToken);
			return;
		}
		throw new InvalidDataException(
			$"Unsupported ApeX Omni WebSocket topic '{header.Topic}'.");
	}

	private void ProcessResponse(ApexOmniWebSocketHeader response)
	{
		var operation = response.Request?.Operation ??
			ApexOmniWebSocketOperations.Login;
		var topics = response.Request?.Arguments;
		if (topics is null || topics.Length == 0)
			topics = [string.Empty];
		foreach (var topic in topics)
		{
			var key = new Subscription(operation, topic);
			PendingRequest pending;
			using (_sync.EnterScope())
				_pending.TryGetValue(key, out pending);
			if (pending is null)
				continue;
			if (response.IsSuccess == true)
				pending.Completion.TrySetResult(true);
			else
				pending.Completion.TrySetException(new InvalidOperationException(
					$"ApeX Omni WebSocket rejected {operation} for '{topic}': " +
					response.ReturnMessage));
		}
	}

	private async ValueTask ResubscribeAsync(string topic,
		CancellationToken cancellationToken)
	{
		var client = _client;
		bool desired;
		using (_sync.EnterScope())
		{
			desired = _subscriptions.Contains(topic);
			_books.Remove(topic);
		}
		if (!desired || client?.IsConnected != true)
			return;
		await SendSubscriptionAsync(client, topic, false, false,
			cancellationToken);
		await SendSubscriptionAsync(client, topic, true, false,
			cancellationToken);
	}

	private static int GetDepthFromTopic(string topic)
	{
		const string prefix = "orderBook";
		var separator = topic.IndexOf('.', prefix.Length);
		if (separator <= prefix.Length || !int.TryParse(
			topic.AsSpan(prefix.Length, separator - prefix.Length),
			NumberStyles.None, CultureInfo.InvariantCulture, out var depth) ||
			depth is not (25 or 200))
			throw new InvalidDataException(
				$"Invalid ApeX Omni order-book topic '{topic}'.");
		return depth;
	}

	private TMessage Deserialize<TMessage>(string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<TMessage>(payload, _jsonSettings)
				?? throw new InvalidDataException(
					"ApeX Omni WebSocket returned an empty message.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"ApeX Omni WebSocket returned malformed JSON.", error);
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
}
