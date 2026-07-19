namespace StockSharp.HashKey.Native;

sealed class HashKeyWsClient : BaseLogReceiver
{
	private readonly record struct Subscription(HashKeyWsTopics Topic, string Symbol,
		string Interval);

	private readonly string _endpoint;
	private readonly bool _isPrivate;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<Subscription> _subscriptions = [];
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;

	public HashKeyWsClient(string endpoint, bool isPrivate, WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		_isPrivate = isPrivate;
		_workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => _isPrivate ? "HashKey_PrivateWs" : "HashKey_PublicWs";

	public event Func<string, HashKeyWsBookTicker, CancellationToken, ValueTask>
		BookTickerReceived;
	public event Func<string, HashKeyWsRealtime, CancellationToken, ValueTask>
		RealtimeReceived;
	public event Func<string, HashKeyWsDepth, CancellationToken, ValueTask> DepthReceived;
	public event Func<string, HashKeyWsTrade, CancellationToken, ValueTask> TradeReceived;
	public event Func<string, string, HashKeyWsKline, CancellationToken, ValueTask>
		KlineReceived;
	public event Func<HashKeyWsAccountUpdate, CancellationToken, ValueTask>
		AccountReceived;
	public event Func<HashKeyWsOrderUpdate, CancellationToken, ValueTask> OrderReceived;
	public event Func<HashKeyWsTicket, CancellationToken, ValueTask> TicketReceived;
	public event Func<HashKeyWsPosition, CancellationToken, ValueTask> PositionReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<HashKeyWsClient, ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException("HashKey WebSocket is already initialized.");
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

	public ValueTask SubscribeBookTickerAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(HashKeyWsTopics.BestBidOffer,
			symbol.ThrowIfEmpty(nameof(symbol)), null), true, cancellationToken);

	public ValueTask UnsubscribeBookTickerAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(HashKeyWsTopics.BestBidOffer,
			symbol.ThrowIfEmpty(nameof(symbol)), null), false, cancellationToken);

	public ValueTask SubscribeRealtimeAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(HashKeyWsTopics.Realtimes,
			symbol.ThrowIfEmpty(nameof(symbol)), null), true, cancellationToken);

	public ValueTask UnsubscribeRealtimeAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(HashKeyWsTopics.Realtimes,
			symbol.ThrowIfEmpty(nameof(symbol)), null), false, cancellationToken);

	public ValueTask SubscribeDepthAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(HashKeyWsTopics.Depth,
			symbol.ThrowIfEmpty(nameof(symbol)), null), true, cancellationToken);

	public ValueTask UnsubscribeDepthAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(HashKeyWsTopics.Depth,
			symbol.ThrowIfEmpty(nameof(symbol)), null), false, cancellationToken);

	public ValueTask SubscribeTradesAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(HashKeyWsTopics.Trade,
			symbol.ThrowIfEmpty(nameof(symbol)), null), true, cancellationToken);

	public ValueTask UnsubscribeTradesAsync(string symbol,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(HashKeyWsTopics.Trade,
			symbol.ThrowIfEmpty(nameof(symbol)), null), false, cancellationToken);

	public ValueTask SubscribeKlineAsync(string symbol, string interval,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(HashKeyWsTopics.Kline,
			symbol.ThrowIfEmpty(nameof(symbol)), interval.ThrowIfEmpty(nameof(interval))),
			true, cancellationToken);

	public ValueTask UnsubscribeKlineAsync(string symbol, string interval,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(HashKeyWsTopics.Kline,
			symbol.ThrowIfEmpty(nameof(symbol)), interval.ThrowIfEmpty(nameof(interval))),
			false, cancellationToken);

	public ValueTask PingAsync(CancellationToken cancellationToken)
	{
		if (_isPrivate || _client?.IsConnected != true)
			return default;
		return SendAsync(new HashKeyWsPingRequest
		{
			Timestamp = DateTime.UtcNow.ToMilliseconds(),
		}, cancellationToken);
	}

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
		client.Init += socket => socket.Options.SetRequestHeader("User-Agent",
			"StockSharp-HashKey-Connector/1.0");
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

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored && !_isPrivate)
		{
			Subscription[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _subscriptions];
			foreach (var subscription in subscriptions)
				await SendSubscriptionAsync(client, subscription, true,
					cancellationToken);
		}
		if (StateChanged is { } handler)
			await handler(this, state, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(Subscription subscription,
		bool isSubscribe, CancellationToken cancellationToken)
	{
		if (_isPrivate)
			throw new InvalidOperationException(
				"HashKey private WebSocket does not accept public subscriptions.");
		using (_sync.EnterScope())
			if (isSubscribe
				? !_subscriptions.Add(subscription)
				: !_subscriptions.Remove(subscription))
				return;
		if (_client?.IsConnected != true)
			return;
		try
		{
			await SendSubscriptionAsync(_client, subscription, isSubscribe,
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (isSubscribe)
					_subscriptions.Remove(subscription);
				else
					_subscriptions.Add(subscription);
			}
			throw;
		}
	}

	private ValueTask SendSubscriptionAsync(WebSocketClient client,
		Subscription subscription, bool isSubscribe,
		CancellationToken cancellationToken)
		=> SendAsync(client, new HashKeyWsSubscriptionRequest
		{
			Topic = subscription.Topic,
			Event = isSubscribe ? HashKeyWsEvents.Subscribe : HashKeyWsEvents.Cancel,
			Params = new()
			{
				Symbol = subscription.Symbol,
				KlineType = subscription.Interval,
			},
		}, cancellationToken);

	private ValueTask SendAsync<TRequest>(TRequest request,
		CancellationToken cancellationToken)
		=> SendAsync(_client, request, cancellationToken);

	private async ValueTask SendAsync<TRequest>(WebSocketClient client,
		TRequest request, CancellationToken cancellationToken)
	{
		if (client?.IsConnected != true)
			throw new InvalidOperationException("HashKey WebSocket is not connected.");
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(request, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask OnProcessAsync(WebSocketClient client,
		WebSocketMessage message, CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			if (_isPrivate)
				await ProcessPrivateAsync(client, payload, cancellationToken);
			else
				await ProcessPublicAsync(client, payload, cancellationToken);
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or
			InvalidOperationException or FormatException or OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask ProcessPublicAsync(WebSocketClient client, string payload,
		CancellationToken cancellationToken)
	{
		var header = Deserialize<HashKeyWsPublicHeader>(payload);
		if (header.Ping is long ping)
		{
			await SendAsync(client, new HashKeyWsPongRequest { Timestamp = ping },
				cancellationToken);
			return;
		}
		if (header.Pong is not null)
			return;
		if (header.Topic is not HashKeyWsTopics topic)
			throw new InvalidDataException("HashKey public WebSocket message has no topic.");

		switch (topic)
		{
			case HashKeyWsTopics.BestBidOffer:
			{
				var envelope = Deserialize<HashKeyWsEnvelope<HashKeyWsBookTicker>>(payload);
				if (envelope.Data is not null && BookTickerReceived is { } handler)
					await handler(envelope.Params?.Symbol.IsEmpty(envelope.Data.Symbol),
						envelope.Data, cancellationToken);
				break;
			}
			case HashKeyWsTopics.Realtimes:
			{
				var envelope = Deserialize<HashKeyWsEnvelope<HashKeyWsRealtime>>(payload);
				if (envelope.Data is not null && RealtimeReceived is { } handler)
					await handler(envelope.Params?.Symbol.IsEmpty(envelope.Data.Symbol),
						envelope.Data, cancellationToken);
				break;
			}
			case HashKeyWsTopics.Depth:
			{
				var envelope = Deserialize<HashKeyWsEnvelope<HashKeyWsDepth>>(payload);
				if (envelope.Data is not null && DepthReceived is { } handler)
					await handler(envelope.Params?.Symbol.IsEmpty(envelope.Data.Symbol),
						envelope.Data, cancellationToken);
				break;
			}
			case HashKeyWsTopics.Trade:
			{
				var envelope = Deserialize<HashKeyWsEnvelope<HashKeyWsTrade>>(payload);
				if (envelope.Data is not null && TradeReceived is { } handler)
					await handler(envelope.Params?.Symbol, envelope.Data,
						cancellationToken);
				break;
			}
			case HashKeyWsTopics.Kline:
			{
				var envelope = Deserialize<HashKeyWsEnvelope<HashKeyWsKline>>(payload);
				if (envelope.Data is not null && KlineReceived is { } handler)
					await handler(envelope.Params?.Symbol.IsEmpty(envelope.Data.Symbol),
						envelope.Params?.KlineType, envelope.Data, cancellationToken);
				break;
			}
			default:
				throw new ArgumentOutOfRangeException(nameof(topic), topic, null);
		}
	}

	private async ValueTask ProcessPrivateAsync(WebSocketClient client, string payload,
		CancellationToken cancellationToken)
	{
		if (payload.AsSpan().TrimStart().StartsWith("{", StringComparison.Ordinal))
		{
			var heartbeat = Deserialize<HashKeyWsPublicHeader>(payload);
			if (heartbeat.Ping is long ping)
			{
				await SendAsync(client, new HashKeyWsPongRequest { Timestamp = ping },
					cancellationToken);
				return;
			}
			throw new InvalidDataException(
				"HashKey private WebSocket returned an unknown object message.");
		}
		var headers = Deserialize<HashKeyPrivateEventHeader[]>(payload);
		if (headers.Length == 0)
			return;
		var eventType = headers[0].Event;
		if (headers.Any(header => header.Event != eventType))
			throw new InvalidDataException(
				"HashKey private WebSocket mixed event types in one batch.");
		switch (eventType)
		{
			case HashKeyPrivateEventTypes.SpotAccount:
			case HashKeyPrivateEventTypes.FuturesAccount:
			case HashKeyPrivateEventTypes.CustodyAccount:
			case HashKeyPrivateEventTypes.FiatAccount:
			case HashKeyPrivateEventTypes.OptionsAccount:
				if (AccountReceived is { } accountHandler)
					foreach (var update in Deserialize<HashKeyWsAccountUpdate[]>(payload))
						await accountHandler(update, cancellationToken);
				break;
			case HashKeyPrivateEventTypes.SpotOrder:
			case HashKeyPrivateEventTypes.FuturesOrder:
				if (OrderReceived is { } orderHandler)
					foreach (var update in Deserialize<HashKeyWsOrderUpdate[]>(payload))
						await orderHandler(update, cancellationToken);
				break;
			case HashKeyPrivateEventTypes.Ticket:
				if (TicketReceived is { } ticketHandler)
					foreach (var update in Deserialize<HashKeyWsTicket[]>(payload))
						await ticketHandler(update, cancellationToken);
				break;
			case HashKeyPrivateEventTypes.FuturesPosition:
				if (PositionReceived is { } positionHandler)
					foreach (var update in Deserialize<HashKeyWsPosition[]>(payload))
						await positionHandler(update, cancellationToken);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(eventType), eventType, null);
		}
	}

	private TMessage Deserialize<TMessage>(string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<TMessage>(payload, _jsonSettings)
				?? throw new InvalidDataException("HashKey WebSocket returned an empty message.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException("HashKey WebSocket returned malformed JSON.",
				error);
		}
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;
}
