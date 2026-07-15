namespace StockSharp.Upstox.Native;

sealed class UpstoxMarketDataClient : BaseLogReceiver
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly WebSocketClient _client;
	private readonly SynchronizedDictionary<string, UpstoxFeedModes> _subscriptions = new(StringComparer.OrdinalIgnoreCase);

	public UpstoxMarketDataClient(string url, int reconnectAttempts, WorkingTime workingTime)
	{
		_client = new(
			url.ThrowIfEmpty(nameof(url)),
			(state, token) => StateChanged is { } stateHandler ? stateHandler(state, token) : default,
			(error, token) => Error is { } errorHandler ? errorHandler(error, token) : default,
			Process,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = reconnectAttempts,
			WorkingTime = workingTime,
			DisableAutoResend = true,
		};
		_client.PostConnect += OnPostConnect;
	}

	public override string Name => nameof(Upstox) + "_" + nameof(UpstoxMarketDataClient);

	public event Func<string, Feed, long, CancellationToken, ValueTask> FeedReceived;
	public event Func<MarketInfo, long, CancellationToken, ValueTask> MarketInfoReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client.PostConnect -= OnPostConnect;
		_client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask Connect(CancellationToken cancellationToken) => _client.ConnectAsync(cancellationToken);
	public void Disconnect() => _client.Disconnect();

	private async ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
	{
		foreach (var group in _subscriptions.ToArray().GroupBy(p => p.Value))
			await Send("sub", group.Key, [.. group.Select(p => p.Key)], cancellationToken);
	}

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		if (message.Memory.IsEmpty)
			return;

		var response = FeedResponse.Parser.ParseFrom(message.Memory.ToArray());
		if (response.MarketInfo != null && MarketInfoReceived is { } marketHandler)
			await marketHandler(response.MarketInfo, response.CurrentTs, cancellationToken);

		if (FeedReceived is not { } feedHandler)
			return;

		foreach (var pair in response.Feeds)
			await feedHandler(pair.Key, pair.Value, response.CurrentTs, cancellationToken);
	}

	public async ValueTask Subscribe(string instrumentKey, UpstoxFeedModes mode, CancellationToken cancellationToken)
	{
		if (_subscriptions.TryGetValue(instrumentKey, out var currentMode))
		{
			if (currentMode == mode)
				return;
			_subscriptions[instrumentKey] = mode;
			await Send("change_mode", mode, [instrumentKey], cancellationToken);
			return;
		}

		_subscriptions.Add(instrumentKey, mode);
		await Send("sub", mode, [instrumentKey], cancellationToken);
	}

	public async ValueTask Unsubscribe(string instrumentKey, CancellationToken cancellationToken)
	{
		if (!_subscriptions.Remove(instrumentKey))
			return;
		await Send("unsub", null, [instrumentKey], cancellationToken);
	}

	private ValueTask Send(string method, UpstoxFeedModes? mode, string[] instrumentKeys, CancellationToken cancellationToken)
	{
		var request = new UpstoxFeedRequest
		{
			Guid = System.Guid.NewGuid().ToString("N"),
			Method = method,
			Data = new()
			{
				Mode = mode?.ToNative(),
				InstrumentKeys = instrumentKeys,
			},
		};
		var payload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request, _jsonSettings));
		return _client.SendAsync(payload, WebSocketMessageType.Binary, cancellationToken);
	}
}
