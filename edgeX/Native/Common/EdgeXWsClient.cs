namespace StockSharp.EdgeX.Native.Common;

using System.Net.WebSockets;

sealed class EdgeXWsClient : BaseLogReceiver
{
	private readonly WebSocketClient _client;
	private readonly KeyValuePair<string, string>[] _headers;
	private readonly Dictionary<string, long> _subscriptions = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Lock _sync = new();
	private long _nextSubscriptionId;

	public EdgeXWsClient(string endpoint, WorkingTime workingTime, IDictionary<string, string> headers = null, int reconnectAttempts = 5)
	{
		endpoint = NormalizeEndpoint(endpoint);
		_headers = headers?.Where(static h => !h.Key.IsEmpty() && !h.Value.IsEmpty()).ToArray() ?? [];

		_client = new WebSocketClient(
			endpoint,
			(state, token) =>
			{
				if (StateChanged is { } handler)
					return handler.InvokeAsync(state, token);

				return default;
			},
			(error, token) =>
			{
				this.AddErrorLog(error);

				if (Error is { } handler)
					return handler.InvokeAsync(error, token);

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

		if (_headers.Length > 0)
			_client.InitAsync += OnInit;
	}

	public override string Name => nameof(EdgeX) + "_" + nameof(EdgeXWsClient);

	public event Func<string, JObject, CancellationToken, ValueTask> TickerReceived;
	public event Func<string, JObject, CancellationToken, ValueTask> DepthReceived;
	public event Func<string, JObject, CancellationToken, ValueTask> TradeReceived;
	public event Func<string, JObject, CancellationToken, ValueTask> CandleReceived;
	public event Func<string, JObject, CancellationToken, ValueTask> PrivatePayloadReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		if (_headers.Length > 0)
			_client.InitAsync -= OnInit;

		_client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask ConnectAsync(CancellationToken cancellationToken)
		=> _client.ConnectAsync(cancellationToken);

	public ValueTask DisconnectAsync(CancellationToken cancellationToken)
		=> _client.DisconnectAsync(cancellationToken);

	public ValueTask SubscribeAsync(string channel, CancellationToken cancellationToken)
		=> SendAsync("subscribe", channel, cancellationToken, GetOrCreateSubscriptionId(channel));

	public ValueTask UnsubscribeAsync(string channel, CancellationToken cancellationToken)
	{
		var subId = PopSubscriptionId(channel);
		return SendAsync("unsubscribe", channel, cancellationToken, subId > 0 ? -subId : default);
	}

	private ValueTask SendAsync(string type, string channel, CancellationToken cancellationToken, long subId)
		=> _client.SendAsync(new
		{
			type,
			channel,
		}, cancellationToken, subId);

	private async ValueTask OnProcessAsync(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var obj = message.AsObject() as JObject;

		if (obj is null)
			return;

		var type = obj["type"]?.Value<string>()?.ToLowerInvariant();

		switch (type)
		{
			case "ping":
				await _client.SendAsync(new { type = "pong", time = obj["time"]?.Value<string>() }, cancellationToken, default);
				return;

			case "pong":
			case "connected":
			case "subscribed":
			case "unsubscribed":
				return;

			case "error":
				if (Error is { } errorHandler)
					await errorHandler.InvokeAsync(new InvalidOperationException(obj.ToString(Formatting.None)), cancellationToken);
				return;
		}

		// data frames are named after the event they carry - quote-event for the public feed,
		// order-event, trade-event and so on for the private one - and only the public feed
		// carries a channel header, so the event name stands in for the private channels
		var channel = obj["channel"]?.Value<string>() ?? obj["topic"]?.Value<string>() ?? type;

		if (channel.IsEmpty())
			return;

		var content = obj["content"] as JObject ?? obj;
		var rows = EnumeratePayloadRows(content);
		var hasRows = false;

		foreach (var item in rows)
		{
			hasRows = true;

			if (channel.StartsWithIgnoreCase("ticker.") && TickerReceived is { } tickerHandler)
			{
				await tickerHandler.InvokeAsync(channel, item, cancellationToken);
				continue;
			}

			if (channel.StartsWithIgnoreCase("depth.") && DepthReceived is { } depthHandler)
			{
				await depthHandler.InvokeAsync(channel, item, cancellationToken);
				continue;
			}

			if ((channel.StartsWithIgnoreCase("trade.") || channel.StartsWithIgnoreCase("trades.")) && TradeReceived is { } tradeHandler)
			{
				await tradeHandler.InvokeAsync(channel, item, cancellationToken);
				continue;
			}

			if (channel.StartsWithIgnoreCase("kline.") && CandleReceived is { } candleHandler)
			{
				await candleHandler.InvokeAsync(channel, item, cancellationToken);
				continue;
			}

			if (IsPrivateChannel(channel) && PrivatePayloadReceived is { } privateHandler)
				await privateHandler.InvokeAsync(channel, item, cancellationToken);
		}

		if (!hasRows && IsPrivateChannel(channel) && PrivatePayloadReceived is { } fallbackPrivateHandler)
			await fallbackPrivateHandler.InvokeAsync(channel, content, cancellationToken);
	}

	private static IEnumerable<JObject> EnumeratePayloadRows(JObject content)
	{
		if (content is null)
			yield break;

		var data = content["data"] as JArray
			?? content["message"] as JArray
			?? content["payload"] as JArray;

		if (data is not null)
		{
			foreach (var item in data.OfType<JObject>())
				yield return item;

			yield break;
		}

		foreach (var key in new[] { "data", "message", "payload", "detail", "result" })
		{
			if (content[key] is JObject obj)
			{
				yield return obj;
				yield break;
			}
		}
	}

	private static bool IsPrivateChannel(string channel)
	{
		if (channel.IsEmpty())
			return false;

		var lower = channel.ToLowerInvariant();
		return lower is "order-event" or "trade-event" or "asset-event" or "position-event";
	}

	private ValueTask OnInit(ClientWebSocket socket, CancellationToken _)
	{
		foreach (var (key, value) in _headers)
			socket.Options.SetRequestHeader(key, value);

		return default;
	}

	private long GetOrCreateSubscriptionId(string channel)
	{
		using (_sync.EnterScope())
		{
			if (_subscriptions.TryGetValue(channel, out var subId))
				return subId;

			subId = Interlocked.Increment(ref _nextSubscriptionId);
			_subscriptions[channel] = subId;
			return subId;
		}
	}

	private long PopSubscriptionId(string channel)
	{
		using (_sync.EnterScope())
		{
			if (_subscriptions.TryGetValue(channel, out var subId))
			{
				_subscriptions.Remove(channel);
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

		return endpoint;
	}
}
