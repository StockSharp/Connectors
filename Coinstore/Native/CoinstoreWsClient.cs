namespace StockSharp.Coinstore.Native;

sealed class CoinstoreWsClient : BaseLogReceiver
{
	private readonly record struct Subscription(string Channel);

	private readonly string _endpoint;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<Subscription> _desiredSubscriptions = [];
	private readonly HashSet<Subscription> _serverSubscriptions = [];
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private WebSocketClient _client;
	private long _requestId;

	public CoinstoreWsClient(string endpoint,
		WorkingTime workingTime, int reconnectAttempts)
		: this(endpoint, null, null, workingTime, reconnectAttempts)
	{
	}

	public CoinstoreWsClient(string endpoint,
		SecureString key, SecureString secret,
		WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		_ = key;
		_ = secret;
		_workingTime = workingTime ??
			throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "COINSTORE_WS";

	public event Func<CoinstoreTicker,
		CancellationToken, ValueTask> TickerReceived;
	public event Func<CoinstoreOrderBook,
		CancellationToken, ValueTask> OrderBookReceived;
	public event Func<CoinstoreTradePush,
		CancellationToken, ValueTask> TradesReceived;
	public event Func<CoinstoreKlineEvent,
		CancellationToken, ValueTask> KlineReceived;
	public event Func<Exception,
		CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates,
		CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_client = null;
		_sendSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(
		CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"Coinstore WebSocket is already initialized.");
		_client = CreateClient();
		try
		{
			await _client.ConnectAsync(cancellationToken);
		}
		catch
		{
			await DisconnectAsync(cancellationToken);
			throw;
		}
	}

	public async ValueTask DisconnectAsync(
		CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		try
		{
			if (client?.IsConnected == true)
				await client.DisconnectAsync(cancellationToken);
		}
		finally
		{
			client?.Dispose();
			using (_sync.EnterScope())
				_serverSubscriptions.Clear();
		}
	}

	public ValueTask SubscribeTickerAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new($"{NormalizeSymbol(symbol)}@ticker"),
			true, cancellationToken);

	public ValueTask UnsubscribeTickerAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new($"{NormalizeSymbol(symbol)}@ticker"),
			false, cancellationToken);

	public ValueTask SubscribeOrderBookAsync(string symbol, int depth,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new($"{NormalizeSymbol(symbol)}@depth@" +
				CoinstoreRestClient.NormalizeDepth(depth)),
			true, cancellationToken);

	public ValueTask UnsubscribeOrderBookAsync(string symbol, int depth,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new($"{NormalizeSymbol(symbol)}@depth@" +
				CoinstoreRestClient.NormalizeDepth(depth)),
			false, cancellationToken);

	public ValueTask SubscribeTradesAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new($"{NormalizeSymbol(symbol)}@trade"),
			true, cancellationToken);

	public ValueTask UnsubscribeTradesAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(
			new($"{NormalizeSymbol(symbol)}@trade"),
			false, cancellationToken);

	public ValueTask SubscribeKlineAsync(string symbol,
		string resolution, CancellationToken cancellationToken)
	{
		_ = resolution.ToCoinstoreTimeFrame();
		return ChangeSubscriptionAsync(
			new($"{NormalizeSymbol(symbol)}@kline@{resolution}"),
			true, cancellationToken);
	}

	public ValueTask UnsubscribeKlineAsync(string symbol,
		string resolution, CancellationToken cancellationToken)
	{
		_ = resolution.ToCoinstoreTimeFrame();
		return ChangeSubscriptionAsync(
			new($"{NormalizeSymbol(symbol)}@kline@{resolution}"),
			false, cancellationToken);
	}

	private WebSocketClient CreateClient()
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			_endpoint,
			(state, token) => OnStateChangedAsync(state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(_, message, token) =>
				OnProcessAsync(message, token),
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			Indent = false,
			SendSettings = new JsonSerializerSettings
			{
				DateParseHandling = DateParseHandling.None,
				NullValueHandling = NullValueHandling.Ignore,
				Formatting = Formatting.None,
				Culture = CultureInfo.InvariantCulture,
			},
		};
		client.InitAsync += (socket, _) =>
		{
			socket.Options.SetRequestHeader(
				"User-Agent",
				"StockSharp-Coinstore-Connector/1.0");
			return default;
		};
		return client;
	}

	private async ValueTask OnStateChangedAsync(
		ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			Subscription[] subscriptions;
			using (_sync.EnterScope())
			{
				_serverSubscriptions.Clear();
				subscriptions = [.. _desiredSubscriptions];
				_serverSubscriptions.AddRange(subscriptions);
			}
			foreach (var subscription in subscriptions)
				await SendSubscriptionAsync(
					subscription, true, cancellationToken);
		}
		if (StateChanged is { } handler)
			await handler.InvokeAsync(state, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(
		Subscription subscription, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		var client = _client ??
			throw new InvalidOperationException(
				"Coinstore WebSocket is disconnected.");
		var send = false;
		using (_sync.EnterScope())
		{
			if (isSubscribe)
			{
				_desiredSubscriptions.Add(subscription);
				send = client.IsConnected &&
					_serverSubscriptions.Add(subscription);
			}
			else
			{
				_desiredSubscriptions.Remove(subscription);
				send = client.IsConnected &&
					_serverSubscriptions.Remove(subscription);
			}
		}
		if (!send)
			return;
		try
		{
			await SendSubscriptionAsync(
				subscription, isSubscribe, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (isSubscribe)
					_serverSubscriptions.Remove(subscription);
				else
					_serverSubscriptions.Add(subscription);
			}
			throw;
		}
	}

	private ValueTask SendSubscriptionAsync(
		Subscription subscription, bool isSubscribe,
		CancellationToken cancellationToken)
		=> SendAsync(new
		{
			op = isSubscribe ? "SUB" : "UNSUB",
			channel = new[] { subscription.Channel },
			id = Interlocked.Increment(ref _requestId),
		}, cancellationToken);

	private async ValueTask SendAsync(object body,
		CancellationToken cancellationToken)
	{
		var client = _client ??
			throw new InvalidOperationException(
				"Coinstore WebSocket is disconnected.");
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(body, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask OnProcessAsync(
		WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var envelope = DeserializeMessage(payload);
			switch (envelope.Type?.ToLowerInvariant())
			{
				case "ticker":
					if (TickerReceived is { } tickerHandler)
						await tickerHandler.InvokeAsync(new()
						{
							Symbol = envelope.Symbol,
							InstrumentId = envelope.InstrumentId,
							Open = envelope.Open,
							High = envelope.High,
							Low = envelope.Low,
							Close = envelope.Close,
							Volume = envelope.Volume,
							Amount = envelope.Amount,
							Bid = envelope.Bid,
							BidSize = envelope.BidSize,
							Ask = envelope.Ask,
							AskSize = envelope.AskSize,
						}, cancellationToken);
					break;

				case "depth":
					if (OrderBookReceived is { } bookHandler)
						await bookHandler.InvokeAsync(new()
						{
							Channel = envelope.Channel,
							Symbol = envelope.Symbol,
							InstrumentId = envelope.InstrumentId,
							Level = envelope.Level,
							LastPrice = envelope.LastPrice,
							Asks = envelope.Asks,
							Bids = envelope.Bids,
						}, cancellationToken);
					break;

				case "trade":
					await ProcessTradesAsync(
						payload, envelope, cancellationToken);
					break;

				case "kline":
					if (KlineReceived is { } klineHandler)
						await klineHandler.InvokeAsync(new()
						{
							Market = envelope.Symbol,
							Kline = new()
							{
								StartTime = envelope.StartTime,
								EndTime = envelope.EndTime,
								Resolution = envelope.Interval,
								Open = envelope.Open ?? 0,
								High = envelope.High ?? 0,
								Low = envelope.Low ?? 0,
								Close = envelope.Close ?? 0,
								Volume = envelope.Volume ?? 0,
								IsFinished =
									DateTime.UtcNow.ToCoinstoreSeconds() >
									envelope.EndTime,
							},
						}, cancellationToken);
					break;
			}
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or InvalidOperationException or
			FormatException or OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask ProcessTradesAsync(
		string payload, CoinstoreWsMessage envelope,
		CancellationToken cancellationToken)
	{
		if (TradesReceived is not { } handler)
			return;
		var trades = envelope.Data;
		if (trades is not { Length: > 0 })
		{
			var trade = JsonConvert.DeserializeObject<CoinstoreTrade>(
				payload);
			trades = trade is null ? [] : [trade];
		}
		var pair = trades.FirstOrDefault(
			static trade => !trade.Pair.IsEmpty())?.Pair;
		await handler.InvokeAsync(new()
		{
			Pair = pair,
			EventId = envelope.Sequence.ToString(
				CultureInfo.InvariantCulture),
			Data = trades,
		}, cancellationToken);
	}

	internal static CoinstoreWsMessage DeserializeMessage(
		string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<CoinstoreWsMessage>(
				payload.ThrowIfEmpty(nameof(payload)),
				new JsonSerializerSettings
				{
					DateParseHandling = DateParseHandling.None,
					NullValueHandling = NullValueHandling.Ignore,
					Culture = CultureInfo.InvariantCulture,
				}) ?? throw new InvalidDataException(
					"Coinstore WebSocket returned an empty message.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Coinstore WebSocket returned malformed JSON.",
				error);
		}
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler
			? handler.InvokeAsync(error, cancellationToken)
			: default;

	private static string NormalizeSymbol(string symbol)
		=> (symbol.ThrowIfEmpty(nameof(symbol)).Contains(
			'/', StringComparison.Ordinal)
				? symbol.ToCoinstoreSymbol()
				: symbol.Trim())
			.ToLowerInvariant();
}
