namespace StockSharp.Yobit.Native;

using System.Globalization;

class PusherClient : BaseLogReceiver
{
	// to get readable name after obfuscation
	public override string Name => nameof(Yobit) + "_" + nameof(PusherClient);

	public event Func<string, Ticker, CancellationToken, ValueTask> TickerChanged;
	public event Func<string, Trade, CancellationToken, ValueTask> NewTrade;
	public event Func<string, OrderBook, CancellationToken, ValueTask> OrderBookChanged;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	private readonly WebSocketClient _client;
	private readonly Lock _sync = new();

	private const string _socketUrl = "wss://s.yobit.biz";
	private const string _realm = "restricted_realm";

	private const int _msgTypeHello = 1;
	private const int _msgTypeWelcome = 2;
	private const int _msgTypeGoodbye = 6;
	private const int _msgTypeError = 8;
	private const int _msgTypeSubscribe = 32;
	private const int _msgTypeSubscribed = 33;
	private const int _msgTypeUnSubscribe = 34;
	private const int _msgTypeEvent = 36;

	private const string _topicHeartbeat = "hb";
	private const string _topicTicker = "ticker";
	private const string _topicTrades = "trhist{0}";
	private const string _topicOrderBook = "ordlst{0}";

	private long _requestId = DateTime.UtcNow.Ticks;
	private long _tradeId;
	private bool _isSessionEstablished;

	private readonly Dictionary<string, int> _topicRefCounts = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<long, string> _requestToTopic = [];
	private readonly Dictionary<string, long> _topicToSubscription = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<long, string> _subscriptionToTopic = [];
	private readonly Dictionary<long, string> _pairIdToCurrency = [];
	private readonly Dictionary<string, OrderBookCache> _orderBooks = new(StringComparer.InvariantCultureIgnoreCase);

	private sealed class OrderBookCache
	{
		public object SyncRoot { get; } = new();
		public SortedDictionary<decimal, decimal> Bids { get; } = new(Comparer<decimal>.Create((x, y) => y.CompareTo(x)));
		public SortedDictionary<decimal, decimal> Asks { get; } = new();
	}

	public PusherClient(WorkingTime workingTime)
	{
		_client = new(
			_socketUrl,
			async (state, token) =>
			{
				await OnStateChangedAsync(state, token);

				if (StateChanged is { } handler)
					await handler(state, token);
			},
			async (error, token) =>
			{
				this.AddErrorLog(error);

				if (Error is { } handler)
					await handler(error, token);
			},
			OnProcess,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			WorkingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime)),
			DisableAutoResend = true,
			Indent = false,
		};

		_topicRefCounts[_topicHeartbeat] = 1;
	}

	protected override void DisposeManaged()
	{
		_client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		this.AddInfoLog(LocalizedStrings.Connecting);
		return _client.ConnectAsync(cancellationToken);
	}

	public void Disconnect()
	{
		this.AddInfoLog(LocalizedStrings.Disconnecting);
		_client.Disconnect();
	}

	private async ValueTask OnStateChangedAsync(ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state is ConnectionStates.Connected or ConnectionStates.Restored)
		{
			using (_sync.EnterScope())
			{
				_isSessionEstablished = false;
				_requestToTopic.Clear();
				_topicToSubscription.Clear();
				_subscriptionToTopic.Clear();
				_orderBooks.Clear();
			}

			await SendHelloAsync(cancellationToken);
		}
		else if (state is ConnectionStates.Disconnecting or ConnectionStates.Disconnected or ConnectionStates.Failed)
		{
			using (_sync.EnterScope())
				_isSessionEstablished = false;
		}
	}

	private ValueTask SendHelloAsync(CancellationToken cancellationToken)
	{
		return _client.SendAsync(new object[]
		{
			_msgTypeHello,
			_realm,
			new
			{
				roles = new
				{
					caller = new
					{
						features = new
						{
							caller_identification = true,
							progressive_call_results = true,
						},
					},
					callee = new
					{
						features = new
						{
							caller_identification = true,
							pattern_based_registration = true,
							shared_registration = true,
							progressive_call_results = true,
							registration_revocation = true,
						},
					},
					publisher = new
					{
						features = new
						{
							publisher_identification = true,
							subscriber_blackwhite_listing = true,
							publisher_exclusion = true,
						},
					},
					subscriber = new
					{
						features = new
						{
							publisher_identification = true,
							pattern_based_subscription = true,
							subscription_revocation = true,
						},
					},
				},
			},
		}, cancellationToken);
	}

	private async ValueTask OnProcess(WebSocketMessage msg, CancellationToken cancellationToken)
	{
		JArray frame;

		try
		{
			frame = msg.AsObject<JArray>();
		}
		catch
		{
			return;
		}

		if (frame is null || frame.Count == 0 || !TryGetLong(frame[0], out var msgType))
			return;

		switch (msgType)
		{
			case _msgTypeWelcome:
				await OnWelcomeAsync(cancellationToken);
				break;

			case _msgTypeSubscribed:
				OnSubscribed(frame);
				break;

			case _msgTypeEvent:
				await OnEventAsync(frame, cancellationToken);
				break;

			case _msgTypeError:
				await OnErrorAsync(frame, cancellationToken);
				break;

			case _msgTypeGoodbye:
				using (_sync.EnterScope())
					_isSessionEstablished = false;
				break;
		}
	}

	private async ValueTask OnWelcomeAsync(CancellationToken cancellationToken)
	{
		List<string> topics;

		using (_sync.EnterScope())
		{
			_isSessionEstablished = true;
			_requestToTopic.Clear();
			_topicToSubscription.Clear();
			_subscriptionToTopic.Clear();
			topics = _topicRefCounts.Where(p => p.Value > 0).Select(p => p.Key).ToList();
		}

		foreach (var topic in topics)
		{
			try
			{
				await SendSubscribeAsync(topic, cancellationToken);
			}
			catch (Exception ex)
			{
				this.AddErrorLog(ex);
			}
		}
	}

	private void OnSubscribed(JArray frame)
	{
		if (frame.Count < 3 || !TryGetLong(frame[1], out var requestId) || !TryGetLong(frame[2], out var subscriptionId))
			return;

		using (_sync.EnterScope())
		{
			if (!_requestToTopic.Remove(requestId, out var topic))
				return;

			_topicToSubscription[topic] = subscriptionId;
			_subscriptionToTopic[subscriptionId] = topic;
		}
	}

	private async ValueTask OnEventAsync(JArray frame, CancellationToken cancellationToken)
	{
		if (frame.Count < 4 || !TryGetLong(frame[1], out var subscriptionId))
			return;

		string topic;

		using (_sync.EnterScope())
		{
			if (!_subscriptionToTopic.TryGetValue(subscriptionId, out topic))
				return;
		}

		var args = frame[3];

		if (topic.EqualsIgnoreCase(_topicTicker))
			await ProcessTickerAsync(args, cancellationToken);
		else if (topic.StartsWithIgnoreCase("trhist"))
			await ProcessTradeAsync(args, cancellationToken);
		else if (topic.StartsWithIgnoreCase("ordlst"))
			await ProcessOrderBookAsync(args, cancellationToken);
	}

	private async ValueTask OnErrorAsync(JArray frame, CancellationToken cancellationToken)
	{
		var ex = new InvalidOperationException($"WAMP frame error: {frame.ToString(Formatting.None)}");
		this.AddErrorLog(ex);

		if (Error is { } handler)
			await handler(ex, cancellationToken);
	}

	private async ValueTask ProcessTickerAsync(JToken argsToken, CancellationToken cancellationToken)
	{
		if (!TryGetPayload(argsToken, out var payload) || payload.Count < 8)
			return;

		if (!TryGetString(payload.ElementAtOrDefault(2), out var baseCurrency) || !TryGetString(payload.ElementAtOrDefault(3), out var quoteCurrency))
			return;

		var currency = $"{baseCurrency}_{quoteCurrency}".ToLowerInvariant();

		var ticker = new Ticker
		{
			Last = GetDecimal(payload, 4),
			Buy = GetDecimal(payload, 6),
			Sell = GetDecimal(payload, 7),
			Volume = GetDecimal(payload, 9) ?? GetDecimal(payload, 8),
			Updated = DateTime.UtcNow,
		};

		if (TickerChanged is { } handler)
			await handler(currency, ticker, cancellationToken);
	}

	private async ValueTask ProcessTradeAsync(JToken argsToken, CancellationToken cancellationToken)
	{
		if (!TryGetPayload(argsToken, out var payload) || payload.Count < 8)
			return;

		if (!TryGetLong(payload.ElementAtOrDefault(0), out var pairId))
			return;

		if (!TryGetDecimal(payload.ElementAtOrDefault(4), out var price) || !TryGetDecimal(payload.ElementAtOrDefault(5), out var amount) || amount <= 0)
			return;

		var currency = ResolveCurrency(pairId, payload);

		if (currency.IsEmpty())
			return;

		var type = TryGetLong(payload.ElementAtOrDefault(1), out var sideCode) && sideCode == 2 ? "sell" : "buy";

		var trade = new Trade
		{
			Type = type,
			Price = price,
			Amount = amount,
			Id = Interlocked.Increment(ref _tradeId),
			Timestamp = TryGetUnixSeconds(payload.ElementAtOrDefault(7), out var timestamp) ? timestamp : DateTime.UtcNow,
		};

		if (NewTrade is { } handler)
			await handler(currency, trade, cancellationToken);
	}

	private async ValueTask ProcessOrderBookAsync(JToken argsToken, CancellationToken cancellationToken)
	{
		if (!TryGetPayload(argsToken, out var payload) || payload.Count < 5)
			return;

		if (!TryGetLong(payload.ElementAtOrDefault(0), out var pairId) || !TryGetLong(payload.ElementAtOrDefault(2), out var side) || !TryGetDecimal(payload.ElementAtOrDefault(3), out var price))
			return;

		var currency = ResolveCurrency(pairId, payload);

		if (currency.IsEmpty())
			return;

		var isAsk = side == 2;
		var isAddOrUpdate = TryGetDecimal(payload.ElementAtOrDefault(4), out var volume) && volume > 0;
		var orderBook = UpdateOrderBook(currency, isAsk, price, volume, isAddOrUpdate);

		if (OrderBookChanged is { } handler)
			await handler(currency, orderBook, cancellationToken);
	}

	private OrderBook UpdateOrderBook(string currency, bool isAsk, decimal price, decimal volume, bool isAddOrUpdate)
	{
		OrderBookCache cache;

		using (_sync.EnterScope())
		{
			if (!_orderBooks.TryGetValue(currency, out cache))
				_orderBooks[currency] = cache = new();
		}

		lock (cache.SyncRoot)
		{
			var side = isAsk ? cache.Asks : cache.Bids;

			if (isAddOrUpdate)
				side[price] = volume;
			else
				side.Remove(price);

			return new OrderBook
			{
				Bids = cache.Bids.Select(p => new OrderBookEntry { Price = p.Key, Size = p.Value }).ToArray(),
				Asks = cache.Asks.Select(p => new OrderBookEntry { Price = p.Key, Size = p.Value }).ToArray(),
			};
		}
	}

	private string ResolveCurrency(long pairId, JArray payload)
	{
		using (_sync.EnterScope())
		{
			if (_pairIdToCurrency.TryGetValue(pairId, out var currency))
				return currency;
		}

		if (TryGetString(payload.ElementAtOrDefault(2), out var baseCurrency) && TryGetString(payload.ElementAtOrDefault(3), out var quoteCurrency))
			return $"{baseCurrency}_{quoteCurrency}".ToLowerInvariant();

		return null;
	}

	private long NextRequestId() => Interlocked.Increment(ref _requestId);

	private async ValueTask SendSubscribeAsync(string topic, CancellationToken cancellationToken)
	{
		var requestId = NextRequestId();

		using (_sync.EnterScope())
			_requestToTopic[requestId] = topic;

		try
		{
			await _client.SendAsync(new object[] { _msgTypeSubscribe, requestId, new { }, topic }, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_requestToTopic.Remove(requestId);

			throw;
		}
	}

	private async ValueTask SendUnSubscribeAsync(string topic, long subscriptionId, CancellationToken cancellationToken)
	{
		var requestId = NextRequestId();

		await _client.SendAsync(new object[] { _msgTypeUnSubscribe, requestId, subscriptionId }, cancellationToken);

		using (_sync.EnterScope())
		{
			_topicToSubscription.Remove(topic);
			_subscriptionToTopic.Remove(subscriptionId);
		}
	}

	private async ValueTask SubscribeTopicAsync(string topic, CancellationToken cancellationToken)
	{
		if (topic.IsEmpty())
			throw new ArgumentNullException(nameof(topic));

		bool needSubscribe;

		using (_sync.EnterScope())
		{
			_topicRefCounts.TryGetValue(topic, out var count);
			count++;
			_topicRefCounts[topic] = count;
			needSubscribe = count == 1;
		}

		if (!needSubscribe || !_isSessionEstablished)
			return;

		await SendSubscribeAsync(topic, cancellationToken);
	}

	private async ValueTask UnSubscribeTopicAsync(string topic, CancellationToken cancellationToken)
	{
		if (topic.IsEmpty())
			throw new ArgumentNullException(nameof(topic));

		long subscriptionId = 0;
		bool needUnSubscribe = false;

		using (_sync.EnterScope())
		{
			if (!_topicRefCounts.TryGetValue(topic, out var count))
				return;

			count--;

			if (count <= 0)
			{
				_topicRefCounts.Remove(topic);
				needUnSubscribe = _topicToSubscription.TryGetValue(topic, out subscriptionId);
			}
			else
				_topicRefCounts[topic] = count;
		}

		if (!needUnSubscribe || !_isSessionEstablished)
			return;

		await SendUnSubscribeAsync(topic, subscriptionId, cancellationToken);
	}

	private static bool TryGetPayload(JToken token, out JArray payload)
	{
		payload = null;

		if (token is not JArray args || args.Count == 0)
			return false;

		var value = args[0];

		if (value is null || value.Type == JTokenType.Null)
			return false;

		if (value is JArray direct)
		{
			payload = direct;
			return true;
		}

		if (value.Type != JTokenType.String)
			return false;

		var data = value.Value<string>();

		if (data.IsEmpty())
			return false;

		try
		{
			payload = JArray.Parse(data);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryGetString(JToken token, out string value)
	{
		value = token?.Value<string>();
		return !value.IsEmpty();
	}

	private static bool TryGetLong(JToken token, out long value)
	{
		value = default;

		if (token is null || token.Type == JTokenType.Null)
			return false;

		if (token.Type == JTokenType.Integer)
		{
			value = token.Value<long>();
			return true;
		}

		return long.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
	}

	private static bool TryGetDecimal(JToken token, out decimal value)
	{
		value = default;

		if (token is null || token.Type == JTokenType.Null)
			return false;

		if (token.Type is JTokenType.Integer or JTokenType.Float)
		{
			value = token.Value<decimal>();
			return true;
		}

		return decimal.TryParse(token.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
	}

	private static decimal? GetDecimal(JArray payload, int index)
	{
		return TryGetDecimal(payload.ElementAtOrDefault(index), out var value) ? value : null;
	}

	private static bool TryGetUnixSeconds(JToken token, out DateTime value)
	{
		value = default;

		if (!TryGetLong(token, out var seconds))
			return false;

		value = seconds.FromUnix();
		return true;
	}

	public ValueTask SubscribeTickerAsync(CancellationToken cancellationToken)
	{
		return SubscribeTopicAsync(_topicTicker, cancellationToken);
	}

	public ValueTask UnSubscribeTickerAsync(CancellationToken cancellationToken)
	{
		return UnSubscribeTopicAsync(_topicTicker, cancellationToken);
	}

	public ValueTask SubscribeTradesAsync(long pairId, string currency, CancellationToken cancellationToken)
	{
		if (currency.IsEmpty())
			throw new ArgumentNullException(nameof(currency));

		using (_sync.EnterScope())
			_pairIdToCurrency[pairId] = currency;

		return SubscribeTopicAsync(_topicTrades.Put(pairId), cancellationToken);
	}

	public ValueTask UnSubscribeTradesAsync(long pairId, CancellationToken cancellationToken)
	{
		return UnSubscribeTopicAsync(_topicTrades.Put(pairId), cancellationToken);
	}

	public ValueTask SubscribeOrderBookAsync(long pairId, string currency, CancellationToken cancellationToken)
	{
		if (currency.IsEmpty())
			throw new ArgumentNullException(nameof(currency));

		using (_sync.EnterScope())
			_pairIdToCurrency[pairId] = currency;

		return SubscribeTopicAsync(_topicOrderBook.Put(pairId), cancellationToken);
	}

	public ValueTask UnSubscribeOrderBookAsync(long pairId, CancellationToken cancellationToken)
	{
		return UnSubscribeTopicAsync(_topicOrderBook.Put(pairId), cancellationToken);
	}
}