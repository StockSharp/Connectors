namespace StockSharp.FinamTrade.Native;

sealed class FinamSocketClient : BaseLogReceiver
{
	private readonly WebSocketClient _client;
	private readonly Func<CancellationToken, Task<string>> _accessTokenProvider;
	private readonly SynchronizedSet<FinamSocketSubscription> _subscriptions = [];
	private readonly JsonSerializerSettings _jsonSettings =
		CreateJsonSettings();

	private static JsonSerializerSettings CreateJsonSettings()
		=> new()
		{
			ContractResolver = new DefaultContractResolver
			{
				NamingStrategy = new SnakeCaseNamingStrategy(),
			},
			NullValueHandling = NullValueHandling.Ignore,
			DateTimeZoneHandling = DateTimeZoneHandling.Utc,
		};

	public FinamSocketClient(string address,
		Func<CancellationToken, Task<string>> accessTokenProvider,
		int reconnectAttempts, WorkingTime workingTime)
	{
		if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("ws" or "wss"))
			throw new ArgumentException(
				"A valid Finam WebSocket address is required.", nameof(address));

		_accessTokenProvider = accessTokenProvider ??
			throw new ArgumentNullException(nameof(accessTokenProvider));
		_client = new(
			uri.ToString(),
			(state, token) => StateChanged is { } stateHandler
				? stateHandler.InvokeAsync(state, token) : default,
			(error, token) => Error is { } errorHandler
				? errorHandler.InvokeAsync(error, token) : default,
			Process,
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = Math.Max(1, reconnectAttempts),
			WorkingTime = workingTime,
			DisableAutoResend = true,
		};
		_client.InitAsync += OnInit;
		_client.PostConnect += RestoreSubscriptions;
	}

	public override string Name => "Finam_WebSocket";

	public event Func<FinamQuote, CancellationToken, ValueTask> QuoteReceived;
	public event Func<FinamStreamOrderBook, CancellationToken, ValueTask>
		OrderBookReceived;
	public event Func<FinamMarketTradesResponse, CancellationToken, ValueTask>
		MarketTradesReceived;
	public event Func<FinamBarsResponse, CancellationToken, ValueTask>
		BarsReceived;
	public event Func<FinamOrderState, CancellationToken, ValueTask> OrderReceived;
	public event Func<FinamAccountTrade, CancellationToken, ValueTask>
		AccountTradeReceived;
	public event Func<FinamAccount, CancellationToken, ValueTask> AccountReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public ValueTask ConnectAsync(CancellationToken cancellationToken)
		=> _client.ConnectAsync(cancellationToken);

	public ValueTask DisconnectAsync(CancellationToken cancellationToken)
		=> _client.DisconnectAsync(cancellationToken);

	public ValueTask Ping(CancellationToken cancellationToken)
	{
		_client.SendOpCode(0x9);
		return default;
	}

	public async ValueTask Subscribe(FinamSocketSubscription subscription,
		CancellationToken cancellationToken)
	{
		subscription = Validate(subscription);
		if (_subscriptions.Contains(subscription))
			return;

		_subscriptions.Add(subscription);
		try
		{
			await Send(subscription, true, cancellationToken);
		}
		catch
		{
			_subscriptions.Remove(subscription);
			throw;
		}
	}

	public async ValueTask Unsubscribe(FinamSocketSubscription subscription,
		CancellationToken cancellationToken)
	{
		subscription = Validate(subscription);
		if (!_subscriptions.Remove(subscription))
			return;
		await Send(subscription, false, cancellationToken);
	}

	private async ValueTask OnInit(ClientWebSocket socket,
		CancellationToken cancellationToken)
	{
		var accessToken = await _accessTokenProvider(cancellationToken);
		socket.Options.SetRequestHeader("Authorization",
			accessToken.ThrowIfEmpty(nameof(accessToken)));
		socket.Options.SetRequestHeader("User-Agent", "StockSharp-Finam/1.0");
	}

	private async ValueTask RestoreSubscriptions(bool isReconnect,
		CancellationToken cancellationToken)
	{
		if (!isReconnect)
			return;

		foreach (var subscription in _subscriptions.ToArray())
			await Send(subscription, true, cancellationToken);
	}

	private async ValueTask Process(WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var raw = message.AsString();
		if (raw.IsEmpty())
			return;

		FinamSocketEnvelope envelope;
		try
		{
			envelope = DeserializeEnvelope(raw);
		}
		catch (JsonException error)
		{
			await RaiseError(new InvalidDataException(
				"Finam returned an invalid WebSocket message.", error),
				cancellationToken);
			return;
		}

		if (envelope is null)
			return;

		if (envelope.Type.EqualsIgnoreCase("ERROR"))
		{
			var error = envelope.ErrorInfo;
			await RaiseError(new InvalidOperationException(
				$"Finam WebSocket error {error?.Code}: " +
				$"{error?.Type}: {error?.Message}".Trim()), cancellationToken);
			return;
		}

		if (envelope.Type.EqualsIgnoreCase("EVENT"))
		{
			this.AddDebugLog("Finam WebSocket event '{0}': {1}",
				envelope.EventInfo?.Event, envelope.EventInfo?.Reason);
			return;
		}

		if (!envelope.Type.EqualsIgnoreCase("DATA") ||
			envelope.Payload is null)
			return;

		var serializer = JsonSerializer.Create(_jsonSettings);
		switch (envelope.SubscriptionType?.ToUpperInvariant())
		{
			case "QUOTES":
				if (QuoteReceived is { } quoteHandler)
				{
					foreach (var quote in envelope.Payload
						.ToObject<FinamQuotePayload>(serializer)?.Quote ?? [])
						await quoteHandler.InvokeAsync(quote, cancellationToken);
				}
				break;

			case "ORDER_BOOK":
				if (OrderBookReceived is { } bookHandler)
				{
					foreach (var book in envelope.Payload
						.ToObject<FinamOrderBookPayload>(serializer)?.OrderBook ?? [])
						await bookHandler.InvokeAsync(book, cancellationToken);
				}
				break;

			case "INSTRUMENT_TRADES":
				if (MarketTradesReceived is { } marketTradesHandler)
				{
					var trades = envelope.Payload
						.ToObject<FinamMarketTradesResponse>(serializer);
					if (trades is not null)
						await marketTradesHandler.InvokeAsync(trades, cancellationToken);
				}
				break;

			case "BARS":
				if (BarsReceived is { } barsHandler)
				{
					var bars = envelope.Payload.ToObject<FinamBarsResponse>(
						serializer);
					if (bars is not null)
						await barsHandler.InvokeAsync(bars, cancellationToken);
				}
				break;

			case "ORDERS":
				if (OrderReceived is { } orderHandler)
				{
					foreach (var order in envelope.Payload
						.ToObject<FinamOrdersResponse>(serializer)?.Orders ?? [])
						await orderHandler.InvokeAsync(order, cancellationToken);
				}
				break;

			case "TRADES":
				if (AccountTradeReceived is { } accountTradeHandler)
				{
					foreach (var trade in envelope.Payload
						.ToObject<FinamAccountTradesResponse>(serializer)?.Trades ?? [])
						await accountTradeHandler.InvokeAsync(trade, cancellationToken);
				}
				break;

			case "ACCOUNT":
				if (AccountReceived is { } accountHandler)
				{
					var account = envelope.Payload.ToObject<FinamAccount>(
						serializer);
					if (account is not null)
						await accountHandler.InvokeAsync(account, cancellationToken);
				}
				break;

			default:
				this.AddDebugLog(
					"Finam ignored WebSocket subscription type '{0}'.",
					envelope.SubscriptionType);
				break;
		}
	}

	internal static FinamSocketEnvelope DeserializeEnvelope(string json)
		=> JsonConvert.DeserializeObject<FinamSocketEnvelope>(
			json.ThrowIfEmpty(nameof(json)), CreateJsonSettings());

	internal static FinamQuote[] DeserializeQuotes(string json)
	{
		var envelope = DeserializeEnvelope(json);
		if (envelope?.Payload is null)
			return [];

		return envelope.Payload.ToObject<FinamQuotePayload>(
			JsonSerializer.Create(CreateJsonSettings()))?.Quote ?? [];
	}

	private async ValueTask Send(FinamSocketSubscription subscription,
		bool subscribe, CancellationToken cancellationToken)
	{
		var token = await _accessTokenProvider(cancellationToken);
		object data = subscription.Type switch
		{
			"QUOTES" => new { symbols = new[] { subscription.Symbol } },
			"BARS" => new
			{
				symbol = subscription.Symbol,
				timeframe = subscription.TimeFrame,
			},
			"ORDERS" or "TRADES" or "ACCOUNT" =>
				new { account_id = subscription.AccountId },
			_ => new { symbol = subscription.Symbol },
		};

		await _client.SendAsync(JsonConvert.SerializeObject(
			new FinamSocketRequest
			{
				Action = subscribe ? "SUBSCRIBE" : "UNSUBSCRIBE",
				Type = subscription.Type,
				Data = data,
				Token = token,
			}, _jsonSettings), cancellationToken);
	}

	private static FinamSocketSubscription Validate(
		FinamSocketSubscription subscription)
	{
		var type = subscription.Type?.Trim().ToUpperInvariant();
		if (type is not ("QUOTES" or "ORDER_BOOK" or "INSTRUMENT_TRADES" or
			"BARS" or "ORDERS" or "TRADES" or "ACCOUNT"))
			throw new ArgumentOutOfRangeException(nameof(subscription),
				$"Unsupported Finam subscription type '{subscription.Type}'.");

		if (type is "ORDERS" or "TRADES" or "ACCOUNT")
		{
			if (subscription.AccountId.IsEmpty())
				throw new ArgumentException(
					"Finam account subscription requires an account ID.",
					nameof(subscription));
		}
		else if (subscription.Symbol.IsEmpty())
		{
			throw new ArgumentException(
				"Finam market-data subscription requires a symbol.",
				nameof(subscription));
		}

		if (type == "BARS" && subscription.TimeFrame.IsEmpty())
			throw new ArgumentException(
				"Finam bars subscription requires a time frame.",
				nameof(subscription));

		return subscription with
		{
			Type = type,
			Symbol = subscription.Symbol?.Trim().ToUpperInvariant(),
			TimeFrame = subscription.TimeFrame?.Trim().ToUpperInvariant(),
			AccountId = subscription.AccountId?.Trim(),
		};
	}

	private ValueTask RaiseError(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler.InvokeAsync(error, cancellationToken) : default;

	protected override void DisposeManaged()
	{
		_client.InitAsync -= OnInit;
		_client.PostConnect -= RestoreSubscriptions;
		_client.Dispose();
		base.DisposeManaged();
	}
}
