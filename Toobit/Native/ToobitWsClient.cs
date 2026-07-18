namespace StockSharp.Toobit.Native;

sealed class ToobitWsClient : BaseLogReceiver
{
	private readonly WebSocketClient _client;
	private readonly bool _isUserStream;
	private readonly Dictionary<string, long> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly Lock _sync = new();
	private long _nextSubscriptionId;

	public ToobitWsClient(string endpoint, bool isUserStream, WorkingTime workingTime)
	{
		if (endpoint.IsEmpty())
			throw new ArgumentNullException(nameof(endpoint));

		_isUserStream = isUserStream;
		_client = new WebSocketClient(
			NormalizeEndpoint(endpoint, isUserStream),
			(state, token) => StateChanged?.Invoke(state, token) ?? default,
			(error, token) => RaiseErrorAsync(error, token),
			OnProcessAsync,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = 5,
			WorkingTime = workingTime,
			SendSettings = new()
			{
				NullValueHandling = NullValueHandling.Ignore,
			},
		};
	}

	public override string Name => nameof(Toobit) + "_" + (_isUserStream ? "UserWs" : "MarketWs");

	public event Func<ToobitWsEnvelope<ToobitWsTicker[]>, CancellationToken, ValueTask> TickerReceived;
	public event Func<ToobitWsEnvelope<ToobitWsDepth[]>, CancellationToken, ValueTask> DepthReceived;
	public event Func<ToobitWsEnvelope<ToobitWsTrade[]>, CancellationToken, ValueTask> TradeReceived;
	public event Func<ToobitWsEnvelope<ToobitWsCandle[]>, CancellationToken, ValueTask> CandleReceived;
	public event Func<ToobitUserBalanceEvent, CancellationToken, ValueTask> BalanceReceived;
	public event Func<ToobitUserPositionEvent, CancellationToken, ValueTask> PositionReceived;
	public event Func<ToobitUserOrderEvent, CancellationToken, ValueTask> OrderReceived;
	public event Func<ToobitUserTradeEvent, CancellationToken, ValueTask> UserTradeReceived;
	public event Func<ToobitListenKeyExpiry, CancellationToken, ValueTask> ListenKeyExpiring;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask ConnectAsync(CancellationToken cancellationToken)
		=> _client.ConnectAsync(cancellationToken);

	public ValueTask DisconnectAsync(CancellationToken cancellationToken)
		=> _client.DisconnectAsync(cancellationToken);

	public ValueTask PingAsync(CancellationToken cancellationToken)
		=> _client.SendAsync(new ToobitWsPing
		{
			Time = DateTime.UtcNow.ToUnixMilliseconds(),
		}, cancellationToken);

	public ValueTask SubscribeTickerAsync(string symbol, CancellationToken cancellationToken)
		=> SubscribeAsync(symbol, "realtimes", cancellationToken);

	public ValueTask UnsubscribeTickerAsync(string symbol, CancellationToken cancellationToken)
		=> UnsubscribeAsync(symbol, "realtimes", cancellationToken);

	public ValueTask SubscribeDepthAsync(string symbol, CancellationToken cancellationToken)
		=> SubscribeAsync(symbol, "depth", cancellationToken);

	public ValueTask UnsubscribeDepthAsync(string symbol, CancellationToken cancellationToken)
		=> UnsubscribeAsync(symbol, "depth", cancellationToken);

	public ValueTask SubscribeTradesAsync(string symbol, CancellationToken cancellationToken)
		=> SubscribeAsync(symbol, "trade", cancellationToken);

	public ValueTask UnsubscribeTradesAsync(string symbol, CancellationToken cancellationToken)
		=> UnsubscribeAsync(symbol, "trade", cancellationToken);

	public ValueTask SubscribeCandlesAsync(string symbol, TimeSpan timeFrame, CancellationToken cancellationToken)
		=> SubscribeAsync(symbol, $"kline_{timeFrame.ToNative()}", cancellationToken);

	public ValueTask UnsubscribeCandlesAsync(string symbol, TimeSpan timeFrame, CancellationToken cancellationToken)
		=> UnsubscribeAsync(symbol, $"kline_{timeFrame.ToNative()}", cancellationToken);

	private ValueTask SubscribeAsync(string symbol, string topic, CancellationToken cancellationToken)
	{
		var key = CreateKey(symbol, topic);
		long subscriptionId;

		using (_sync.EnterScope())
		{
			if (!_subscriptions.TryGetValue(key, out subscriptionId))
			{
				subscriptionId = Interlocked.Increment(ref _nextSubscriptionId);
				_subscriptions.Add(key, subscriptionId);
			}
		}

		return SendSubscriptionAsync(symbol, topic, ToobitWsEvents.Subscribe,
			subscriptionId, cancellationToken);
	}

	private ValueTask UnsubscribeAsync(string symbol, string topic, CancellationToken cancellationToken)
	{
		var key = CreateKey(symbol, topic);
		long subscriptionId = 0;

		using (_sync.EnterScope())
		{
			if (_subscriptions.Remove(key, out var current))
				subscriptionId = current;
		}

		return SendSubscriptionAsync(symbol, topic, ToobitWsEvents.Cancel,
			subscriptionId > 0 ? -subscriptionId : 0, cancellationToken);
	}

	private ValueTask SendSubscriptionAsync(string symbol, string topic, ToobitWsEvents action,
		long subscriptionId, CancellationToken cancellationToken)
		=> _client.SendAsync(new ToobitWsSubscriptionRequest
		{
			Symbol = symbol.ToUpperInvariant(),
			Topic = topic,
			Event = action,
			Parameters = new()
			{
				Limit = topic.StartsWith("kline_", StringComparison.OrdinalIgnoreCase) ? 1 : null,
				IsBinary = false,
			},
		}, cancellationToken, subscriptionId);

	private async ValueTask OnProcessAsync(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;

		try
		{
			if (_isUserStream && payload.AsSpan().TrimStart().StartsWith("["))
			{
				await ProcessUserEventsAsync(payload, cancellationToken);
				return;
			}

			var header = JsonConvert.DeserializeObject<ToobitWsHeader>(payload);
			if (header?.Ping is long ping)
			{
				await _client.SendAsync(new ToobitWsPong { Time = ping }, cancellationToken);
				return;
			}

			if (_isUserStream)
			{
				var expiry = JsonConvert.DeserializeObject<ToobitListenKeyExpiry>(payload);
				if (expiry?.EventType.EqualsIgnoreCase("listenKeyWillExpire") == true && ListenKeyExpiring is { } expiryHandler)
					await expiryHandler(expiry, cancellationToken);
				return;
			}

			if (header?.Code is int code && code is not 0 and not 200)
				throw new InvalidOperationException($"Toobit WebSocket error {code}: {header.Message}");

			switch (header?.Topic?.ToLowerInvariant())
			{
				case "realtimes":
					if (TickerReceived is { } tickerHandler)
						await tickerHandler(Deserialize<ToobitWsTicker>(payload), cancellationToken);
					break;

				case "depth":
				case "diffdepth":
					if (DepthReceived is { } depthHandler)
						await depthHandler(Deserialize<ToobitWsDepth>(payload), cancellationToken);
					break;

				case "trade":
					if (TradeReceived is { } tradeHandler)
						await tradeHandler(Deserialize<ToobitWsTrade>(payload), cancellationToken);
					break;

				case "kline":
					if (CandleReceived is { } candleHandler)
						await candleHandler(Deserialize<ToobitWsCandle>(payload), cancellationToken);
					break;
			}
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask ProcessUserEventsAsync(string payload, CancellationToken cancellationToken)
	{
		var headers = JsonConvert.DeserializeObject<ToobitUserEventHeader[]>(payload) ?? [];
		foreach (var eventName in headers.Select(static h => h.Event).Where(static e => !e.IsEmpty()).Distinct(StringComparer.OrdinalIgnoreCase))
		{
			switch (eventName.ToLowerInvariant())
			{
				case "outboundaccountinfo":
				case "outboundcontractaccountinfo":
					if (BalanceReceived is { } balanceHandler)
					{
						foreach (var update in JsonConvert.DeserializeObject<ToobitUserBalanceEvent[]>(payload) ?? [])
						{
							if (update.Event.EqualsIgnoreCase(eventName))
								await balanceHandler(update, cancellationToken);
						}
					}
					break;

				case "outboundcontractpositioninfo":
					if (PositionReceived is { } positionHandler)
					{
						foreach (var update in JsonConvert.DeserializeObject<ToobitUserPositionEvent[]>(payload) ?? [])
						{
							if (update.Event.EqualsIgnoreCase(eventName))
								await positionHandler(update, cancellationToken);
						}
					}
					break;

				case "executionreport":
				case "contractexecutionreport":
					if (OrderReceived is { } orderHandler)
					{
						foreach (var update in JsonConvert.DeserializeObject<ToobitUserOrderEvent[]>(payload) ?? [])
						{
							if (update.Event.EqualsIgnoreCase(eventName))
								await orderHandler(update, cancellationToken);
						}
					}
					break;

				case "ticketinfo":
					if (UserTradeReceived is { } tradeHandler)
					{
						foreach (var update in JsonConvert.DeserializeObject<ToobitUserTradeEvent[]>(payload) ?? [])
						{
							if (update.Event.EqualsIgnoreCase(eventName))
								await tradeHandler(update, cancellationToken);
						}
					}
					break;
			}
		}
	}

	private static ToobitWsEnvelope<TData[]> Deserialize<TData>(string payload)
		=> JsonConvert.DeserializeObject<ToobitWsEnvelope<TData[]>>(payload)
			?? throw new InvalidDataException("Toobit WebSocket returned an empty JSON value.");

	private async ValueTask RaiseErrorAsync(Exception error, CancellationToken cancellationToken)
	{
		this.AddErrorLog(error);
		if (Error is { } handler)
			await handler(error, cancellationToken);
	}

	private static string CreateKey(string symbol, string topic)
		=> symbol + "|" + topic;

	private static string NormalizeEndpoint(string endpoint, bool isUserStream)
	{
		endpoint = endpoint.Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"wss://{endpoint.TrimStart('/')}";

		var uri = new Uri(endpoint, UriKind.Absolute);
		if (!isUserStream && (uri.AbsolutePath.IsEmpty() || uri.AbsolutePath == "/"))
		{
			return new UriBuilder(uri)
			{
				Path = "/quote/ws/v1",
			}.Uri.ToString().TrimEnd('/');
		}

		return endpoint.TrimEnd('/');
	}
}
