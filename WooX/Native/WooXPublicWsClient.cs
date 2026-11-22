namespace StockSharp.WooX.Native;

readonly record struct WooXWsSubscriptionKey(WooXWsTopics Topic, string Symbol,
	TimeSpan TimeFrame);

sealed class WooXPublicWsClient : BaseLogReceiver
{
	private const int _orderBookDepth = 200;
	private readonly string _endpoint;
	private readonly WooXRestClient _restClient;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly Dictionary<WooXWsSubscriptionKey, string> _subscriptions = [];
	private readonly Dictionary<string, OrderBook> _books = new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;
	private long _localId;

	public WooXPublicWsClient(string endpoint, WooXRestClient restClient, WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint));
		_restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
		_workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => nameof(WooX) + "_PublicWs";

	public event Func<WooXWsTicker, long, CancellationToken, ValueTask> TickerReceived;
	public event Func<WooXWsBestBidOffer, long, CancellationToken, ValueTask> BestBidOfferReceived;
	public event Func<WooXWsBook, long, CancellationToken, ValueTask> BookReceived;
	public event Func<WooXWsTrade, long, CancellationToken, ValueTask> TradeReceived;
	public event Func<WooXWsCandle, long, CancellationToken, ValueTask> CandleReceived;
	public event Func<WooXWsTopics, WooXWsReferencePrice, long, CancellationToken, ValueTask> ReferencePriceReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException("WOO X public WebSocket is already initialized.");
		var client = _client = CreateClient();
		try
		{
			await client.ConnectAsync(cancellationToken);
		}
		catch
		{
			await DisposeClientAsync(cancellationToken);
			throw;
		}
	}

	public ValueTask DisconnectAsync(CancellationToken cancellationToken)
		=> DisposeClientAsync(cancellationToken);

	public ValueTask SubscribeTickerAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(WooXWsTopics.Ticker, symbol, default), true,
			cancellationToken);

	public ValueTask UnsubscribeTickerAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(WooXWsTopics.Ticker, symbol, default), false,
			cancellationToken);

	public ValueTask SubscribeBestBidOfferAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(WooXWsTopics.BestBidOffer, symbol, default), true,
			cancellationToken);

	public ValueTask UnsubscribeBestBidOfferAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(WooXWsTopics.BestBidOffer, symbol, default), false,
			cancellationToken);

	public ValueTask SubscribeBookAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(WooXWsTopics.OrderBook, symbol, default), true,
			cancellationToken);

	public ValueTask UnsubscribeBookAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(WooXWsTopics.OrderBook, symbol, default), false,
			cancellationToken);

	public ValueTask SubscribeTradesAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(WooXWsTopics.Trade, symbol, default), true,
			cancellationToken);

	public ValueTask UnsubscribeTradesAsync(string symbol, CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(WooXWsTopics.Trade, symbol, default), false,
			cancellationToken);

	public ValueTask SubscribeCandlesAsync(string symbol, TimeSpan timeFrame,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(WooXWsTopics.Candle, symbol, timeFrame), true,
			cancellationToken);

	public ValueTask UnsubscribeCandlesAsync(string symbol, TimeSpan timeFrame,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(WooXWsTopics.Candle, symbol, timeFrame), false,
			cancellationToken);

	public ValueTask SubscribeIndexPriceAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(WooXWsTopics.IndexPrice, symbol, default), true,
			cancellationToken);

	public ValueTask UnsubscribeIndexPriceAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(WooXWsTopics.IndexPrice, symbol, default), false,
			cancellationToken);

	public ValueTask SubscribeMarkPriceAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(WooXWsTopics.MarkPrice, symbol, default), true,
			cancellationToken);

	public ValueTask UnsubscribeMarkPriceAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(WooXWsTopics.MarkPrice, symbol, default), false,
			cancellationToken);

	public ValueTask PingAsync(CancellationToken cancellationToken)
		=> _client is { IsConnected: true } client
			? SendAsync(client, new WooXWsHeartbeat { Command = "PING", Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, cancellationToken)
			: default;

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
		client.InitAsync += (socket, _) =>
		{
			socket.Options.SetRequestHeader("User-Agent",
			"StockSharp-WooX-Connector/1.0");
			return default;
		};
		return client;
	}

	private async ValueTask DisposeClientAsync(CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		if (client is null)
			return;
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

	private async ValueTask OnStateChangedAsync(WebSocketClient client, ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			KeyValuePair<WooXWsSubscriptionKey, string>[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _subscriptions];
			foreach (var subscription in subscriptions)
				await SendSubscriptionAsync(client, subscription.Key, subscription.Value, true,
					cancellationToken);
		}
		if (StateChanged is { } handler)
			await handler.InvokeAsync(state, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(WooXWsSubscriptionKey subscription,
		bool isSubscribe, CancellationToken cancellationToken)
	{
		string id;
		using (_sync.EnterScope())
		{
			if (isSubscribe)
			{
				if (_subscriptions.ContainsKey(subscription))
					return;
				id = Interlocked.Increment(ref _localId).ToString(CultureInfo.InvariantCulture);
				_subscriptions.Add(subscription, id);
				if (subscription.Topic == WooXWsTopics.OrderBook)
					_books[subscription.Symbol] = new();
			}
			else if (!_subscriptions.Remove(subscription, out id))
				return;
			else if (subscription.Topic == WooXWsTopics.OrderBook)
				_books.Remove(subscription.Symbol);
		}
		if (_client?.IsConnected == true)
			await SendSubscriptionAsync(_client, subscription, id, isSubscribe,
				cancellationToken);
	}

	private ValueTask SendSubscriptionAsync(WebSocketClient client,
		WooXWsSubscriptionKey subscription, string id, bool isSubscribe,
		CancellationToken cancellationToken)
		=> SendAsync(client, new WooXWsCommand
		{
			Id = id,
			Parameters = [GetTopic(subscription)],
			Command = isSubscribe ? "SUBSCRIBE" : "UN_SUBSCRIBE",
		}, cancellationToken);

	private static string GetTopic(WooXWsSubscriptionKey subscription)
		=> subscription.Topic switch
		{
			WooXWsTopics.Ticker => "ticker@" + subscription.Symbol,
			WooXWsTopics.BestBidOffer => "bbo@" + subscription.Symbol,
			WooXWsTopics.OrderBook => $"orderbookupdate@{subscription.Symbol}@{_orderBookDepth}",
			WooXWsTopics.Trade => "trade@" + subscription.Symbol,
			WooXWsTopics.Candle => $"kline@{subscription.Symbol}@" +
				subscription.TimeFrame.ToWooXWsInterval(),
			WooXWsTopics.IndexPrice => "indexprice@" + subscription.Symbol,
			WooXWsTopics.MarkPrice => "markprice@" + subscription.Symbol,
			_ => throw new ArgumentOutOfRangeException(nameof(subscription), subscription, null),
		};

	private async ValueTask SendAsync<TPayload>(WebSocketClient client, TPayload payload,
		CancellationToken cancellationToken)
	{
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(payload, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask OnProcessAsync(WebSocketClient client, WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var header = Deserialize<WooXWsHeader>(payload);
			if (header.Command.EqualsIgnoreCase("PING"))
			{
				await SendAsync(client, new WooXWsHeartbeat { Command = "PONG", Timestamp = header.Timestamp }, cancellationToken);
				return;
			}
			if (!header.Command.IsEmpty())
			{
				if (header.IsSuccess == false)
					throw new InvalidOperationException(
						$"WOO X WebSocket request failed: {header.Message}".Trim());
				return;
			}
			var topic = header.Topic?.Split('@', 2)[0]?.ToLowerInvariant();
			if (topic == "ticker")
			{
				var envelope = Deserialize<WooXWsEnvelope<WooXWsTicker>>(payload);
				if (envelope.Data is not null && TickerReceived is { } tickerHandler)
					await tickerHandler.InvokeAsync(envelope.Data, envelope.Timestamp, cancellationToken);
			}
			else if (topic == "bbo")
			{
				var envelope = Deserialize<WooXWsEnvelope<WooXWsBestBidOffer>>(payload);
				if (envelope.Data is not null && BestBidOfferReceived is { } bboHandler)
					await bboHandler.InvokeAsync(envelope.Data, envelope.Timestamp, cancellationToken);
			}
			else if (topic == "orderbookupdate")
			{
				var envelope = Deserialize<WooXWsEnvelope<WooXWsBook>>(payload);
				if (envelope.Data is not null)
					await ApplyOrderBookUpdateAsync(envelope.Data, cancellationToken);
			}
			else if (topic == "trade")
			{
				var envelope = Deserialize<WooXWsEnvelope<WooXWsTrade>>(payload);
				if (envelope.Data is not null && TradeReceived is { } tradeHandler)
					await tradeHandler.InvokeAsync(envelope.Data, envelope.Timestamp, cancellationToken);
			}
			else if (topic == "kline")
			{
				var envelope = Deserialize<WooXWsEnvelope<WooXWsCandle>>(payload);
				if (envelope.Data is not null && CandleReceived is { } candleHandler)
					await candleHandler.InvokeAsync(envelope.Data, envelope.Timestamp, cancellationToken);
			}
			else if (topic is "indexprice" or "markprice")
			{
				var envelope = Deserialize<WooXWsEnvelope<WooXWsReferencePrice>>(payload);
				if (envelope.Data is not null && ReferencePriceReceived is { } priceHandler)
					await priceHandler.InvokeAsync(topic == "indexprice"
						? WooXWsTopics.IndexPrice
						: WooXWsTopics.MarkPrice,
						envelope.Data, envelope.Timestamp, cancellationToken);
			}
			else
				throw new InvalidDataException($"Unknown WOO X WebSocket topic '{header.Topic}'.");
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or HttpRequestException or
			InvalidOperationException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask ApplyOrderBookUpdateAsync(WooXWsBook update, CancellationToken cancellationToken)
	{
		if (update.Symbol.IsEmpty() || update.Timestamp <= 0 || update.PreviousTimestamp <= 0)
			throw new InvalidDataException("WOO X order book update is missing its symbol or sequence timestamps.");

		OrderBook book;
		using (_sync.EnterScope())
			book = _books.GetValueOrDefault(update.Symbol);
		if (book is null || update.Timestamp <= book.Timestamp)
			return;

		if (book.Timestamp != update.PreviousTimestamp)
		{
			var snapshot = await _restClient.GetOrderBookSnapshotAsync(update.Symbol, _orderBookDepth, cancellationToken);
			if (snapshot.Data is null || snapshot.Timestamp <= 0)
				throw new InvalidDataException("WOO X order book snapshot is missing its levels or timestamp.");
			if (snapshot.Timestamp < update.PreviousTimestamp)
				return;
			book.Reset(snapshot.Data, (long)snapshot.Timestamp);
		}

		if (book.Timestamp == update.PreviousTimestamp)
			book.Apply(update);

		using (_sync.EnterScope())
			if (book != _books.GetValueOrDefault(update.Symbol))
				return;

		if (BookReceived is { } handler)
			await handler.InvokeAsync(book.ToSnapshot(update.Symbol), book.Timestamp, cancellationToken);
	}

	private sealed class OrderBook
	{
		private readonly SortedDictionary<decimal, WooXPriceLevel> _bids = new(Comparer<decimal>.Create((left, right) => right.CompareTo(left)));
		private readonly SortedDictionary<decimal, WooXPriceLevel> _asks = new();

		public long Timestamp { get; private set; }

		public void Reset(WooXOrderBookData snapshot, long timestamp)
		{
			_bids.Clear();
			_asks.Clear();
			ApplySide(_bids, (snapshot.Bids ?? []).Select(static level => new WooXPriceLevel { Price = level.Price, Quantity = level.Quantity }));
			ApplySide(_asks, (snapshot.Asks ?? []).Select(static level => new WooXPriceLevel { Price = level.Price, Quantity = level.Quantity }));
			Timestamp = timestamp;
		}

		public void Apply(WooXWsBook update)
		{
			ApplySide(_bids, update.Bids);
			ApplySide(_asks, update.Asks);
			Timestamp = update.Timestamp;
		}

		public WooXWsBook ToSnapshot(string symbol) => new()
		{
			Symbol = symbol,
			Bids = [.. _bids.Values],
			Asks = [.. _asks.Values],
		};

		private static void ApplySide(SortedDictionary<decimal, WooXPriceLevel> side, IEnumerable<WooXPriceLevel> update)
		{
			foreach (var level in update ?? [])
			{
				if (level.Quantity == 0)
					side.Remove(level.Price);
				else
					side[level.Price] = level;
			}

			foreach (var price in side.Keys.Skip(_orderBookDepth).ToArray())
				side.Remove(price);
		}
	}

	private TData Deserialize<TData>(string payload)
		=> JsonConvert.DeserializeObject<TData>(payload, _jsonSettings)
			?? throw new InvalidDataException("WOO X WebSocket returned an empty message.");

	private ValueTask RaiseErrorAsync(Exception error, CancellationToken cancellationToken)
		=> Error is { } handler ? handler.InvokeAsync(error, cancellationToken) : default;
}
