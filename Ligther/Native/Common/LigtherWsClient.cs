namespace StockSharp.Ligther.Native.Common;

sealed class LigtherWsClient : BaseLogReceiver
{
	private readonly WebSocketClient _client;
	private readonly Dictionary<string, long> _subscriptions = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Lock _sync = new();
	private long _nextSubscriptionId;

	public LigtherWsClient(string endpoint, WorkingTime workingTime, bool readOnly = false, int reconnectAttempts = 5)
	{
		endpoint = NormalizeEndpoint(endpoint, readOnly);

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

	public override string Name => nameof(Ligther) + "_" + nameof(LigtherWsClient);

	public event Func<JObject, CancellationToken, ValueTask> OrderBookReceived;
	public event Func<JObject, CancellationToken, ValueTask> TradeReceived;
	public event Func<JObject, CancellationToken, ValueTask> MarketStatsReceived;
	public event Func<JObject, CancellationToken, ValueTask> SpotMarketStatsReceived;
	public event Func<JObject, CancellationToken, ValueTask> PrivatePayloadReceived;
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

	public ValueTask SubscribeAsync(string channel, string authToken, CancellationToken cancellationToken)
		=> SendAsync("subscribe", channel, authToken, cancellationToken, GetOrCreateSubscriptionId(channel));

	public ValueTask UnsubscribeAsync(string channel, CancellationToken cancellationToken)
	{
		var subId = PopSubscriptionId(channel);
		return SendAsync("unsubscribe", channel, null, cancellationToken, subId > 0 ? -subId : default);
	}

	private ValueTask SendAsync(string type, string channel, string authToken, CancellationToken cancellationToken, long subId)
		=> _client.SendAsync(new
		{
			type,
			channel,
			auth = authToken.IsEmpty() ? null : authToken,
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
				await _client.SendAsync(new { type = "pong" }, cancellationToken, default);
				return;

			case "pong":
			case "subscribed":
			case "unsubscribed":
				return;

			case "error":
				if (Error is { } errorHandler)
					await errorHandler(new InvalidOperationException(obj.ToString(Formatting.None)), cancellationToken);
				return;
		}

		if (obj["error"] is not null)
		{
			if (Error is { } errorHandler)
				await errorHandler(new InvalidOperationException(obj.ToString(Formatting.None)), cancellationToken);

			return;
		}

		var channel = obj["channel"]?.Value<string>()?.ToLowerInvariant();

		if ((channel.StartsWithIgnoreCase("order_book:") || channel.StartsWithIgnoreCase("order_book/")) && OrderBookReceived is { } depthHandler)
		{
			await depthHandler(obj, cancellationToken);
			return;
		}

		if ((channel.StartsWithIgnoreCase("trade:") || channel.StartsWithIgnoreCase("trade/")) && TradeReceived is { } tradeHandler)
		{
			await tradeHandler(obj, cancellationToken);
			return;
		}

		if ((channel.StartsWithIgnoreCase("market_stats:") || channel.StartsWithIgnoreCase("market_stats/")) && MarketStatsReceived is { } marketStatsHandler)
		{
			await marketStatsHandler(obj, cancellationToken);
			return;
		}

		if ((channel.StartsWithIgnoreCase("spot_market_stats:") || channel.StartsWithIgnoreCase("spot_market_stats/")) && SpotMarketStatsReceived is { } spotMarketStatsHandler)
		{
			await spotMarketStatsHandler(obj, cancellationToken);
			return;
		}

		if (IsPrivateMessage(type, channel) && PrivatePayloadReceived is { } privateHandler)
			await privateHandler(obj, cancellationToken);
	}

	private static bool IsPrivateMessage(string type, string channel)
	{
		if (!channel.IsEmpty())
		{
			var normalized = channel.ToLowerInvariant();

			if (normalized.StartsWith("account_", StringComparison.Ordinal)
				|| normalized.StartsWith("user_stats:", StringComparison.Ordinal)
				|| normalized.StartsWith("pool_", StringComparison.Ordinal))
			{
				return true;
			}
		}

		if (type.IsEmpty())
			return false;

		return type.StartsWith("update/account", StringComparison.Ordinal)
			|| type.StartsWith("update/user_stats", StringComparison.Ordinal)
			|| type.StartsWith("subscribed/pool", StringComparison.Ordinal);
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

	private static string NormalizeEndpoint(string endpoint, bool readOnly)
	{
		if (endpoint.IsEmpty())
			throw new ArgumentNullException(nameof(endpoint));

		endpoint = endpoint.Trim();

		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"wss://{endpoint.TrimStart('/')}";

		if (readOnly && !endpoint.Contains("readonly=", StringComparison.InvariantCultureIgnoreCase))
			endpoint += endpoint.Contains('?') ? "&readonly=true" : "?readonly=true";

		return endpoint;
	}
}
