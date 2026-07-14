namespace StockSharp.Aster.Native.Common;

sealed class AsterWsClient : BaseLogReceiver
{
	private readonly WebSocketClient _client;
	private readonly Dictionary<string, long> _subscriptions = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Lock _sync = new();
	private long _nextRequestId;
	private long _nextSubscriptionId;

	public AsterWsClient(string endpoint, WorkingTime workingTime, int reconnectAttempts = 5)
	{
		endpoint = NormalizeEndpoint(endpoint);
		_nextRequestId = DateTime.UtcNow.Ticks;

		_client = new WebSocketClient(
			endpoint,
			(state, token) =>
			{
				if (StateChanged is { } handler)
					return handler(state, token);

				return default;
			},
			(error, token) =>
			{
				this.AddErrorLog(error);

				if (Error is { } handler)
					return handler(error, token);

				return default;
			},
			OnProcessAsync,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = reconnectAttempts,
			WorkingTime = workingTime,
			SendSettings = new()
			{
				NullValueHandling = NullValueHandling.Ignore,
			},
		};
	}

	public override string Name => nameof(Aster) + "_" + nameof(AsterWsClient);

	public event Func<JObject, CancellationToken, ValueTask> TickerReceived;
	public event Func<JObject, CancellationToken, ValueTask> DepthReceived;
	public event Func<JObject, CancellationToken, ValueTask> TradeReceived;
	public event Func<JObject, CancellationToken, ValueTask> CandleReceived;
	public event Func<JObject, CancellationToken, ValueTask> PrivateEventReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask ConnectAsync(CancellationToken cancellationToken)
		=> _client.ConnectAsync(cancellationToken);

	public void Disconnect()
		=> _client.Disconnect();

	public ValueTask SubscribeTickerAsync(string symbol, CancellationToken cancellationToken)
		=> SubscribeAsync($"{NormalizeSymbol(symbol)}@ticker", cancellationToken);

	public ValueTask UnsubscribeTickerAsync(string symbol, CancellationToken cancellationToken)
		=> UnsubscribeAsync($"{NormalizeSymbol(symbol)}@ticker", cancellationToken);

	public ValueTask SubscribeDepthAsync(string symbol, CancellationToken cancellationToken)
		=> SubscribeAsync($"{NormalizeSymbol(symbol)}@depth20@100ms", cancellationToken);

	public ValueTask UnsubscribeDepthAsync(string symbol, CancellationToken cancellationToken)
		=> UnsubscribeAsync($"{NormalizeSymbol(symbol)}@depth20@100ms", cancellationToken);

	public ValueTask SubscribeTradesAsync(string symbol, CancellationToken cancellationToken)
		=> SubscribeAsync($"{NormalizeSymbol(symbol)}@aggTrade", cancellationToken);

	public ValueTask UnsubscribeTradesAsync(string symbol, CancellationToken cancellationToken)
		=> UnsubscribeAsync($"{NormalizeSymbol(symbol)}@aggTrade", cancellationToken);

	public ValueTask SubscribeCandlesAsync(string symbol, TimeSpan timeFrame, CancellationToken cancellationToken)
		=> SubscribeAsync($"{NormalizeSymbol(symbol)}@kline_{timeFrame.ToNative()}", cancellationToken);

	public ValueTask UnsubscribeCandlesAsync(string symbol, TimeSpan timeFrame, CancellationToken cancellationToken)
		=> UnsubscribeAsync($"{NormalizeSymbol(symbol)}@kline_{timeFrame.ToNative()}", cancellationToken);

	private ValueTask SubscribeAsync(string stream, CancellationToken cancellationToken)
		=> SendAsync("SUBSCRIBE", stream, cancellationToken, GetOrCreateSubscriptionId(stream));

	private ValueTask UnsubscribeAsync(string stream, CancellationToken cancellationToken)
	{
		var subId = PopSubscriptionId(stream);
		return SendAsync("UNSUBSCRIBE", stream, cancellationToken, subId > 0 ? -subId : default);
	}

	private ValueTask SendAsync(string method, string stream, CancellationToken cancellationToken, long subId)
		=> _client.SendAsync(new
		{
			method,
			@params = new[] { stream },
			id = Interlocked.Increment(ref _nextRequestId),
		}, cancellationToken, subId);

	private async ValueTask OnProcessAsync(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var obj = message.AsObject() as JObject;

		if (obj is null)
			return;

		if (obj["ping"] is not null)
		{
			await _client.SendAsync(new
			{
				pong = obj["ping"]?.Value<long?>() ?? (long)DateTime.UtcNow.ToUnix(false),
			}, cancellationToken);

			return;
		}

		if (obj["stream"] is not null && obj["data"] is JObject dataObj)
			obj = dataObj;

		if (obj["result"] is not null)
			return;

		if (obj["code"] is not null || obj["error"] is not null)
		{
			var ex = new InvalidOperationException(obj.ToString(Formatting.None));

			if (Error is { } errorHandler)
				await errorHandler(ex, cancellationToken);

			return;
		}

		var evt = obj["e"]?.Value<string>()?.ToLowerInvariant();

		switch (evt)
		{
			case "24hrticker":
			case "ticker":
			case "bookticker":
				if (TickerReceived is { } tickerHandler)
					await tickerHandler(obj, cancellationToken);
				return;

			case "depthupdate":
				if (DepthReceived is { } depthHandler)
					await depthHandler(obj, cancellationToken);
				return;

			case "aggtrade":
				if (TradeReceived is { } tradeHandler)
					await tradeHandler(obj, cancellationToken);
				return;

			case "kline":
				if (CandleReceived is { } candleHandler)
					await candleHandler(obj, cancellationToken);
				return;

			case "executionreport":
			case "order_trade_update":
			case "outboundaccountposition":
			case "balanceupdate":
			case "account_update":
				if (PrivateEventReceived is { } privateHandler)
					await privateHandler(obj, cancellationToken);
				return;
		}

		if (obj["k"] is JObject && CandleReceived is { } fallbackCandleHandler)
		{
			await fallbackCandleHandler(obj, cancellationToken);
			return;
		}

		if (obj["b"] is not null && obj["a"] is not null && DepthReceived is { } fallbackDepthHandler)
		{
			await fallbackDepthHandler(obj, cancellationToken);
			return;
		}

		if ((obj["c"] is not null || obj["lastPrice"] is not null) && TickerReceived is { } fallbackTickerHandler)
			await fallbackTickerHandler(obj, cancellationToken);
	}

	private long GetOrCreateSubscriptionId(string stream)
	{
		using (_sync.EnterScope())
		{
			if (_subscriptions.TryGetValue(stream, out var subId))
				return subId;

			subId = Interlocked.Increment(ref _nextSubscriptionId);
			_subscriptions[stream] = subId;
			return subId;
		}
	}

	private long PopSubscriptionId(string stream)
	{
		using (_sync.EnterScope())
		{
			if (_subscriptions.TryGetValue(stream, out var subId))
			{
				_subscriptions.Remove(stream);
				return subId;
			}
		}

		return default;
	}

	private static string NormalizeEndpoint(string endpoint)
	{
		if (endpoint.IsEmpty())
			throw new ArgumentNullException(nameof(endpoint));

		endpoint = endpoint.Trim();

		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"wss://{endpoint.TrimStart('/')}";

		var uri = endpoint.To<Uri>();

		if (uri.AbsolutePath.IsEmpty() || uri.AbsolutePath == "/")
		{
			var builder = new UriBuilder(uri)
			{
				Path = "/ws",
			};

			return builder.Uri.ToString().TrimEnd('/');
		}

		return endpoint;
	}

	private static string NormalizeSymbol(string symbol)
	{
		if (symbol.IsEmpty())
			throw new ArgumentNullException(nameof(symbol));

		return symbol.ToLowerInvariant();
	}
}
