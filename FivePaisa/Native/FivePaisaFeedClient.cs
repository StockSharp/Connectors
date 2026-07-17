namespace StockSharp.FivePaisa.Native;

sealed class FivePaisaFeedClient : BaseLogReceiver
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly WebSocketClient _client;
	private readonly string _clientCode;
	private readonly SynchronizedSet<string> _subscriptions = new(StringComparer.OrdinalIgnoreCase);

	public FivePaisaFeedClient(string clientCode, string token, int reconnectAttempts, WorkingTime workingTime)
	{
		_clientCode = clientCode.ThrowIfEmpty(nameof(clientCode));
		token.ThrowIfEmpty(nameof(token));
		var host = GetFeedHost(token);
		var url = $"wss://{host}/feeds/api/chat?Value1={Uri.EscapeDataString(token)}|{Uri.EscapeDataString(clientCode)}";

		_client = new(
			url,
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
		_client.PostConnect += OnPostConnect;
	}

	public override string Name => nameof(FivePaisa) + "_" + nameof(FivePaisaFeedClient);

	public event Func<FivePaisaMarketUpdate, CancellationToken, ValueTask> MarketDataReceived;
	public event Func<FivePaisaOrderUpdate, CancellationToken, ValueTask> OrderReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client.PostConnect -= OnPostConnect;
		_client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask Connect(CancellationToken cancellationToken) => _client.ConnectAsync(cancellationToken);
	public ValueTask Disconnect(CancellationToken cancellationToken) => _client.DisconnectAsync(cancellationToken);

	public async ValueTask Subscribe(string instrumentKey, CancellationToken cancellationToken)
	{
		if (!_subscriptions.TryAdd(instrumentKey))
			return;
		await SendMarketSubscription(true, [instrumentKey], cancellationToken);
	}

	public async ValueTask Unsubscribe(string instrumentKey, CancellationToken cancellationToken)
	{
		if (!_subscriptions.Remove(instrumentKey))
			return;
		await SendMarketSubscription(false, [instrumentKey], cancellationToken);
	}

	private async ValueTask OnPostConnect(bool reconnect, CancellationToken cancellationToken)
	{
		await _client.SendAsync(JsonConvert.SerializeObject(new FivePaisaFeedRequest
		{
			Method = "OrderTradeConfirmations",
			Operation = "Subscribe",
			ClientCode = _clientCode,
		}, _jsonSettings), cancellationToken);

		var subscriptions = _subscriptions.ToArray();
		for (var i = 0; i < subscriptions.Length; i += 100)
			await SendMarketSubscription(true, subscriptions.Skip(i).Take(100).ToArray(), cancellationToken);
	}

	private ValueTask SendMarketSubscription(bool subscribe, string[] instrumentKeys, CancellationToken cancellationToken)
	{
		var instruments = instrumentKeys.Select(key =>
		{
			var (exchange, exchangeType, scripCode) = key.ParseInstrumentKey();
			return new FivePaisaFeedInstrument
			{
				Exchange = exchange,
				ExchangeType = exchangeType,
				ScripCode = scripCode,
			};
		}).ToArray();

		return _client.SendAsync(JsonConvert.SerializeObject(new FivePaisaFeedRequest
		{
			Method = "MarketFeedV3",
			Operation = subscribe ? "Subscribe" : "Unsubscribe",
			ClientCode = _clientCode,
			Instruments = instruments,
		}, _jsonSettings), cancellationToken);
	}

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var text = message.AsString()?.Trim();
		if (text.IsEmpty())
			return;

		if (text[0] == '[')
		{
			var updates = JsonConvert.DeserializeObject<FivePaisaMarketUpdate[]>(text)
				?? throw new InvalidOperationException("5paisa returned an empty market-feed message.");
			if (MarketDataReceived is not { } handler)
				return;
			foreach (var update in updates)
			{
				if (update?.Token > 0)
					await handler(update, cancellationToken);
			}
			return;
		}

		if (text[0] != '{')
			throw new InvalidDataException($"Unexpected 5paisa WebSocket message: {text}");

		var order = JsonConvert.DeserializeObject<FivePaisaOrderUpdate>(text)
			?? throw new InvalidOperationException("5paisa returned an empty order-stream message.");
		if (!order.RequestType.IsEmpty() && OrderReceived is { } orderHandler)
			await orderHandler(order, cancellationToken);
	}

	private static string GetFeedHost(string token)
	{
		try
		{
			var parts = token.Split('.');
			if (parts.Length < 2)
				return "openfeed.5paisa.com";
			var payload = parts[1].Replace('-', '+').Replace('_', '/');
			payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
			var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
			var redirect = JsonConvert.DeserializeObject<FivePaisaTokenPayload>(json)?.RedirectServer?.ToUpperInvariant();
			return redirect switch
			{
				"A" => "aopenfeed.5paisa.com",
				"B" => "bopenfeed.5paisa.com",
				_ => "openfeed.5paisa.com",
			};
		}
		catch (Exception ex) when (ex is FormatException or JsonException)
		{
			return "openfeed.5paisa.com";
		}
	}
}
