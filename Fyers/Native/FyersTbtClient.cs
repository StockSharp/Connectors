namespace StockSharp.Fyers.Native;

sealed class FyersTbtClient : BaseLogReceiver
{
	private const string _channel = "1";

	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly WebSocketClient _client;
	private readonly string _authorization;
	private readonly SynchronizedSet<string> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, FyersTbtBook> _books = new(StringComparer.OrdinalIgnoreCase);

	public FyersTbtClient(string url, string clientId, string token, int reconnectAttempts, WorkingTime workingTime)
	{
		_authorization = $"{clientId.ThrowIfEmpty(nameof(clientId))}:{token.ThrowIfEmpty(nameof(token))}";
		_client = new(
			url.ThrowIfEmpty(nameof(url)),
			(state, cancellationToken) => StateChanged is { } stateHandler ? stateHandler(state, cancellationToken) : default,
			(error, cancellationToken) => Error is { } errorHandler ? errorHandler(error, cancellationToken) : default,
			Process,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = reconnectAttempts,
			WorkingTime = workingTime,
			DisableAutoResend = true,
		};
		_client.Init += OnInit;
		_client.PostConnect += OnPostConnect;
	}

	public override string Name => nameof(Fyers) + "_" + nameof(FyersTbtClient);

	public event Func<FyersDepthUpdate, CancellationToken, ValueTask> DepthReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client.Init -= OnInit;
		_client.PostConnect -= OnPostConnect;
		_client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask Connect(CancellationToken cancellationToken) => _client.ConnectAsync(cancellationToken);
	public void Disconnect() => _client.Disconnect();
	public ValueTask SendHeartbeat(CancellationToken cancellationToken) => _client.SendAsync("ping", cancellationToken);

	public async ValueTask Subscribe(string symbol, CancellationToken cancellationToken)
	{
		if (!_subscriptions.TryAdd(symbol.ThrowIfEmpty(nameof(symbol))))
			return;
		await SendSubscription(true, [symbol], cancellationToken);
		await ResumeChannel(cancellationToken);
	}

	public async ValueTask Unsubscribe(string symbol, CancellationToken cancellationToken)
	{
		if (!_subscriptions.Remove(symbol))
			return;
		_books.Remove(symbol);
		await SendSubscription(false, [symbol], cancellationToken);
	}

	private void OnInit(ClientWebSocket socket)
		=> socket.Options.SetRequestHeader("authorization", _authorization);

	private async ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
	{
		_books.Clear();
		foreach (var symbols in _subscriptions.ToArray().Chunk(100))
			await SendSubscription(true, symbols, cancellationToken);
		await ResumeChannel(cancellationToken);
	}

	private ValueTask SendSubscription(bool isSubscribe, string[] symbols, CancellationToken cancellationToken)
		=> symbols.Length == 0 ? default : _client.SendAsync(JsonConvert.SerializeObject(new FyersTbtSubscriptionRequest
		{
			Type = 1,
			Data = new FyersTbtSubscriptionData
			{
				SubscriptionType = isSubscribe ? 1 : -1,
				Symbols = symbols,
				Mode = "depth",
				Channel = _channel,
			},
		}, _jsonSettings), cancellationToken);

	private ValueTask ResumeChannel(CancellationToken cancellationToken)
		=> _client.SendAsync(JsonConvert.SerializeObject(new FyersTbtChannelRequest
		{
			Type = 2,
			Data = new FyersTbtChannelData
			{
				ResumeChannels = [_channel],
				PauseChannels = [],
			},
		}, _jsonSettings), cancellationToken);

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var data = message.Memory;
		if (data.IsEmpty)
			return;
		if (data.Span[0] == (byte)'p')
			return;

		var packet = FyersProtoDecoder.Decode(data.Span);
		if (packet.IsError)
			throw new InvalidOperationException($"FYERS TBT stream error: {packet.Message}");
		if (DepthReceived is not { } handler)
			return;

		foreach (var feed in packet.Feeds)
		{
			if (feed?.Depth == null || feed.Symbol.IsEmpty() || !_subscriptions.Contains(feed.Symbol))
				continue;
			var book = _books.SafeAdd(feed.Symbol);
			if (packet.IsSnapshot || feed.IsSnapshot)
				Clear(book);
			Apply(book.Bids, feed.Depth.Bids);
			Apply(book.Asks, feed.Depth.Asks);

			var bids = book.Bids.Where(l => l.Price > 0 && l.Volume > 0).OrderByDescending(l => l.Price).ToArray();
			var asks = book.Asks.Where(l => l.Price > 0 && l.Volume > 0).OrderBy(l => l.Price).ToArray();
			if (bids.Length == 0 && asks.Length == 0)
				continue;

			await handler(new FyersDepthUpdate
			{
				Symbol = feed.Symbol,
				ServerTime = ToTime(feed.FeedTime),
				Bids = bids,
				Asks = asks,
			}, cancellationToken);
		}
	}

	private static void Apply(FyersDepthLevel[] destination, FyersTbtLevel[] source)
	{
		for (var i = 0; i < source.Length; i++)
		{
			var level = source[i];
			var position = i;
			if (position < 0 || position >= destination.Length)
				continue;
			var current = destination[position];
			if (level.Price != null)
				current.Price = level.Price.Value / 100m;
			if (level.Quantity != null)
				current.Volume = level.Quantity.Value;
			if (level.OrdersCount != null)
				current.OrdersCount = level.OrdersCount.Value > int.MaxValue ? int.MaxValue : (int)level.OrdersCount.Value;
		}
	}

	private static void Clear(FyersTbtBook book)
	{
		foreach (var level in book.Bids.Concat(book.Asks))
		{
			level.Price = 0;
			level.Volume = 0;
			level.OrdersCount = null;
		}
	}

	private static DateTime ToTime(ulong value)
	{
		if (value == 0)
			return DateTime.UtcNow;
		var timestamp = value > 10_000_000_000 ? value / 1000 : value;
		return timestamp > long.MaxValue ? DateTime.UtcNow : ((long)timestamp).FromUnix();
	}
}
