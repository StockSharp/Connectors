namespace StockSharp.MaxExchange.Native;

sealed class MaxExchangeWsClient : BaseLogReceiver
{
	private readonly record struct Subscription(
		string Channel,
		string Market,
		int Depth,
		string Resolution);

	private sealed class BookState
	{
		public SortedDictionary<decimal, decimal> Bids { get; } = [];
		public SortedDictionary<decimal, decimal> Asks { get; } = [];
		public long Version { get; set; }
		public long LastUpdateId { get; set; }
	}

	private readonly string _endpoint;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly string _key;
	private readonly string _secret;
	private readonly Lock _sync = new();
	private readonly HashSet<Subscription> _desiredSubscriptions = [];
	private readonly HashSet<Subscription> _serverSubscriptions = [];
	private readonly Dictionary<string, BookState> _books =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;
	private TaskCompletionSource<MaxExchangeSymbol[]> _marketSnapshot =
		CreateMarketSignal();

	public MaxExchangeWsClient(string endpoint,
		WorkingTime workingTime, int reconnectAttempts)
		: this(endpoint, null, null, workingTime, reconnectAttempts)
	{
	}

	public MaxExchangeWsClient(string endpoint,
		SecureString key, SecureString secret,
		WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		_key = key?.UnSecure();
		_secret = secret?.UnSecure();
		_workingTime = workingTime ??
			throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "MAX_WS";

	public event Func<MaxExchangeTicker,
		CancellationToken, ValueTask> TickerReceived;
	public event Func<MaxExchangeOrderBook,
		CancellationToken, ValueTask> OrderBookReceived;
	public event Func<MaxExchangeTradePush,
		CancellationToken, ValueTask> TradesReceived;
	public event Func<MaxExchangeKlineEvent,
		CancellationToken, ValueTask> KlineReceived;
	public event Func<MaxExchangeSymbol[],
		CancellationToken, ValueTask> MarketsReceived;
	public event Func<Exception,
		CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates,
		CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_client = null;
		_sendSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(
		CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"MAX Exchange WebSocket is already initialized.");
		_client = CreateClient();
		_marketSnapshot = CreateMarketSignal();
		try
		{
			await _client.ConnectAsync(cancellationToken);
			if (HasCredentials)
				await AuthenticateAsync(cancellationToken);
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
		try
		{
			if (client?.IsConnected == true)
				await client.DisconnectAsync(cancellationToken);
		}
		finally
		{
			client?.Dispose();
			using (_sync.EnterScope())
			{
				_serverSubscriptions.Clear();
				_books.Clear();
			}
		}
	}

	public ValueTask SubscribeTickerAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(
			"ticker", NormalizeSymbol(symbol), 0, null),
			true, cancellationToken);

	public ValueTask UnsubscribeTickerAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(
			"ticker", NormalizeSymbol(symbol), 0, null),
			false, cancellationToken);

	public ValueTask SubscribeOrderBookAsync(string symbol, int depth,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(
			"book", NormalizeSymbol(symbol),
			MaxExchangeRestClient.NormalizeDepth(depth), null),
			true, cancellationToken);

	public ValueTask UnsubscribeOrderBookAsync(string symbol, int depth,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(
			"book", NormalizeSymbol(symbol),
			MaxExchangeRestClient.NormalizeDepth(depth), null),
			false, cancellationToken);

	public ValueTask SubscribeTradesAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(
			"trade", NormalizeSymbol(symbol), 0, null),
			true, cancellationToken);

	public ValueTask UnsubscribeTradesAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(
			"trade", NormalizeSymbol(symbol), 0, null),
			false, cancellationToken);

	public ValueTask SubscribeKlineAsync(string symbol,
		string resolution, CancellationToken cancellationToken)
	{
		_ = resolution.ToMaxExchangeTimeFrame();
		return ChangeSubscriptionAsync(new(
			"kline", NormalizeSymbol(symbol), 0, resolution),
			true, cancellationToken);
	}

	public ValueTask UnsubscribeKlineAsync(string symbol,
		string resolution, CancellationToken cancellationToken)
	{
		_ = resolution.ToMaxExchangeTimeFrame();
		return ChangeSubscriptionAsync(new(
			"kline", NormalizeSymbol(symbol), 0, resolution),
			false, cancellationToken);
	}

	public ValueTask SubscribeMarketStatusAsync(string market,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(
			"market_status", NormalizeSymbol(market), 0, null),
			true, cancellationToken);

	public ValueTask UnsubscribeMarketStatusAsync(string market,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(
			"market_status", NormalizeSymbol(market), 0, null),
			false, cancellationToken);

	public async ValueTask<MaxExchangeSymbol[]> GetMarketsAsync(
		CancellationToken cancellationToken)
	{
		await SubscribeMarketStatusAsync("all", cancellationToken);
		try
		{
			return await _marketSnapshot.Task.WaitAsync(
				TimeSpan.FromSeconds(20), cancellationToken);
		}
		finally
		{
			await UnsubscribeMarketStatusAsync(
				"all", cancellationToken);
		}
	}

	private WebSocketClient CreateClient()
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			_endpoint,
			(state, token) => OnStateChangedAsync(state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(_, message, token) =>
				OnProcessAsync(message, token),
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
		client.InitAsync += (socket, _) =>
		{
			socket.Options.SetRequestHeader(
				"User-Agent",
				"StockSharp-MAX-Exchange-Connector/1.0");
			return default;
		};
		return client;
	}

	private async ValueTask OnStateChangedAsync(
		ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			if (HasCredentials)
				await AuthenticateAsync(cancellationToken);
			Subscription[] subscriptions;
			using (_sync.EnterScope())
			{
				_serverSubscriptions.Clear();
				_books.Clear();
				subscriptions = [.. _desiredSubscriptions];
				_serverSubscriptions.AddRange(subscriptions);
			}

			foreach (var subscription in subscriptions)
			{
				try
				{
					await SendSubscriptionAsync(
						subscription, true, cancellationToken);
				}
				catch
				{
					using (_sync.EnterScope())
						_serverSubscriptions.Remove(subscription);
					throw;
				}
			}
		}
		if (StateChanged is { } handler)
			await handler.InvokeAsync(state, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(
		Subscription subscription, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		var client = _client ??
			throw new InvalidOperationException(
				"MAX Exchange WebSocket is disconnected.");
		var send = false;
		using (_sync.EnterScope())
		{
			if (isSubscribe)
			{
				_desiredSubscriptions.Add(subscription);
				send = client.IsConnected &&
					_serverSubscriptions.Add(subscription);
			}
			else
			{
				_desiredSubscriptions.Remove(subscription);
				send = client.IsConnected &&
					_serverSubscriptions.Remove(subscription);
				if (subscription.Channel == "book")
					_books.Remove(subscription.Market);
			}
		}
		if (!send)
			return;
		try
		{
			await SendSubscriptionAsync(
				subscription, isSubscribe, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (isSubscribe)
					_serverSubscriptions.Remove(subscription);
				else
					_serverSubscriptions.Add(subscription);
			}
			throw;
		}
	}

	private ValueTask SendSubscriptionAsync(
		Subscription subscription, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		var value = new Dictionary<string, object>(
			StringComparer.Ordinal)
		{
			["channel"] = subscription.Channel,
			["market"] = subscription.Market,
		};
		if (subscription.Depth > 0)
			value["depth"] = subscription.Depth;
		if (!subscription.Resolution.IsEmpty())
			value["resolution"] = subscription.Resolution;
		return SendAsync(new
		{
			action = isSubscribe ? "sub" : "unsub",
			subscriptions = new[] { value },
		}, cancellationToken);
	}

	private ValueTask AuthenticateAsync(
		CancellationToken cancellationToken)
	{
		var nonce = DateTime.UtcNow.ToMaxExchangeMilliseconds();
		return SendAsync(new
		{
			action = "auth",
			apiKey = _key,
			nonce,
			signature =
				MaxExchangeAuthenticator.CreateWebSocketSignature(
					_secret, nonce),
			filters = new[] { "order", "trade", "account" },
		}, cancellationToken);
	}

	private async ValueTask SendAsync(object body,
		CancellationToken cancellationToken)
	{
		var client = _client ??
			throw new InvalidOperationException(
				"MAX Exchange WebSocket is disconnected.");
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(body, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask OnProcessAsync(
		WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var root = JObject.Parse(payload);
			var eventName = root.Value<string>("e");
			var channel = root.Value<string>("c");
			if (eventName.EqualsIgnoreCase("error"))
			{
				var messages = root["E"]?.Values<string>().Join("; ");
				throw new InvalidDataException(
					$"MAX Exchange WebSocket error " +
					$"{root.Value<string>("co")}: {messages}");
			}
			switch (channel?.ToLowerInvariant())
			{
				case "book":
					await ProcessBookAsync(
						DeserializeMessage<MaxExchangeBookEvent>(
							payload),
						cancellationToken);
					break;

				case "trade":
					await ProcessTradesAsync(
						DeserializeMessage<MaxExchangeTradeEvent>(
							payload),
						cancellationToken);
					break;

				case "ticker":
					await ProcessTickerAsync(
						DeserializeMessage<MaxExchangeTickerEvent>(
							payload),
						cancellationToken);
					break;

				case "kline":
					if (KlineReceived is { } klineHandler)
						await klineHandler.InvokeAsync(
							DeserializeMessage<
								MaxExchangeKlineEvent>(payload),
							cancellationToken);
					break;

				case "market_status":
					await ProcessMarketsAsync(
						DeserializeMessage<
							MaxExchangeMarketStatusEvent>(payload),
						cancellationToken);
					break;
			}
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or InvalidOperationException or
			FormatException or OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask ProcessBookAsync(
		MaxExchangeBookEvent update,
		CancellationToken cancellationToken)
	{
		if (update?.Market.IsEmpty() != false)
			return;
		MaxExchangeOrderBook snapshot = null;
		var resubscribe = false;
		using (_sync.EnterScope())
		{
			if (update.Event.EqualsIgnoreCase("snapshot"))
			{
				var state = new BookState
				{
					Version = update.Version,
					LastUpdateId = update.LastUpdateId,
				};
				ApplyLevels(state.Bids, update.Bids);
				ApplyLevels(state.Asks, update.Asks);
				_books[update.Market] = state;
				snapshot = CreateBookSnapshot(
					update.Market, update.Timestamp, state);
			}
			else if (update.Event.EqualsIgnoreCase("update"))
			{
				if (_books.TryGetValue(
						update.Market, out var state) &&
					state.Version == update.Version &&
					update.FirstUpdateId <= state.LastUpdateId + 1 &&
					update.LastUpdateId >= state.LastUpdateId + 1)
				{
					ApplyLevels(state.Bids, update.Bids);
					ApplyLevels(state.Asks, update.Asks);
					state.LastUpdateId = update.LastUpdateId;
					snapshot = CreateBookSnapshot(
						update.Market, update.Timestamp, state);
				}
				else
				{
					_books.Remove(update.Market);
					resubscribe = true;
				}
			}
		}
		if (resubscribe)
		{
			Subscription[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _desiredSubscriptions.Where(
					item => item.Channel == "book" &&
						item.Market.EqualsIgnoreCase(update.Market))];

			foreach (var subscription in subscriptions)
			{
				await SendSubscriptionAsync(
					subscription, false, cancellationToken);
				await SendSubscriptionAsync(
					subscription, true, cancellationToken);
			}

			return;
		}
		if (snapshot is not null &&
			OrderBookReceived is { } handler)
			await handler.InvokeAsync(snapshot, cancellationToken);
	}

	private async ValueTask ProcessTradesAsync(
		MaxExchangeTradeEvent push,
		CancellationToken cancellationToken)
	{
		if (push?.Market.IsEmpty() != false ||
			TradesReceived is not { } handler)
			return;
		await handler.InvokeAsync(new()
		{
			Pair = push.Market,
			EventId = push.Timestamp.ToString(
				CultureInfo.InvariantCulture),
			Data = [.. (push.Trades ?? []).Select(
				static trade => new MaxExchangeTrade
				{
					Price = trade.Price,
					Amount = trade.Volume,
					Timestamp = trade.Timestamp,
				})],
		}, cancellationToken);
	}

	private async ValueTask ProcessTickerAsync(
		MaxExchangeTickerEvent push,
		CancellationToken cancellationToken)
	{
		if (push?.Ticker is null ||
			TickerReceived is not { } handler)
			return;
		var ticker = push.Ticker;
		await handler.InvokeAsync(new()
		{
			Pair = ticker.Market.IsEmpty(
				push.Market),
			At = push.Timestamp,
			OpenPrice = ticker.Open,
			HighPrice = ticker.High,
			LowPrice = ticker.Low,
			LastPrice = ticker.Close,
			Volume = ticker.Volume,
			VolumeInBtc = ticker.VolumeInBtc,
		}, cancellationToken);
	}

	private async ValueTask ProcessMarketsAsync(
		MaxExchangeMarketStatusEvent push,
		CancellationToken cancellationToken)
	{
		if (push?.Markets is not { Length: > 0 })
			return;
		MaxExchangeSymbol[] markets =
			[.. push.Markets.Select(
				static market => market.ToMarket())];
		_marketSnapshot.TrySetResult(markets);
		if (MarketsReceived is { } handler)
			await handler.InvokeAsync(markets, cancellationToken);
	}

	private MaxExchangeOrderBook CreateBookSnapshot(
		string market, long timestamp, BookState state)
	{
		var depth = 50;
		var subscription = _desiredSubscriptions
			.Where(item => item.Channel == "book" &&
				item.Market.EqualsIgnoreCase(market))
			.OrderByDescending(static item => item.Depth)
			.FirstOrDefault();
		if (subscription.Depth > 0)
			depth = subscription.Depth;
		return new()
		{
			Pair = market,
			Timestamp = timestamp,
			LastUpdateVersion = state.Version,
			LastUpdateId = state.LastUpdateId,
			Limit = depth,
			Bids = [.. state.Bids
				.OrderByDescending(static pair => pair.Key)
				.Take(depth)
				.Select(static pair =>
					new[] { pair.Key, pair.Value })],
			Asks = [.. state.Asks
				.OrderBy(static pair => pair.Key)
				.Take(depth)
				.Select(static pair =>
					new[] { pair.Key, pair.Value })],
		};
	}

	private static void ApplyLevels(
		IDictionary<decimal, decimal> target,
		IEnumerable<decimal[]> values)
	{
		foreach (var level in values ?? [])
		{
			if (level is not { Length: >= 2 } ||
				level[0] <= 0)
				continue;
			if (level[1] == 0)
				target.Remove(level[0]);
			else if (level[1] > 0)
				target[level[0]] = level[1];
		}
	}

	internal static TMessage DeserializeMessage<TMessage>(
		string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<TMessage>(
				payload.ThrowIfEmpty(nameof(payload)),
				new JsonSerializerSettings
				{
					DateParseHandling = DateParseHandling.None,
					NullValueHandling = NullValueHandling.Ignore,
					Culture = CultureInfo.InvariantCulture,
				}) ?? throw new InvalidDataException(
					"MAX Exchange WebSocket returned an empty message.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"MAX Exchange WebSocket returned malformed JSON.",
				error);
		}
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler
			? handler.InvokeAsync(error, cancellationToken)
			: default;

	private bool HasCredentials
		=> !_key.IsEmpty() && !_secret.IsEmpty();

	private static string NormalizeSymbol(string symbol)
		=> symbol.ThrowIfEmpty(nameof(symbol)).Trim().Contains(
			'/', StringComparison.Ordinal)
				? symbol.ToMaxExchangeSymbol()
				: symbol.Trim().ToLowerInvariant();

	private static TaskCompletionSource<MaxExchangeSymbol[]>
		CreateMarketSignal()
		=> new(TaskCreationOptions.RunContinuationsAsynchronously);
}
