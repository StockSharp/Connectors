namespace StockSharp.Deepcoin.Native;

readonly record struct DeepcoinWsSubscriptionKey(DeepcoinWsTopics Topic,
	string InstrumentId, TimeSpan TimeFrame);

sealed class DeepcoinPublicWsClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly DeepcoinProductTypes _productType;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly Dictionary<DeepcoinWsSubscriptionKey, long> _subscriptions = [];
	private readonly Dictionary<string, string> _wireSymbols = new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;
	private long _localNumber;

	public DeepcoinPublicWsClient(string endpoint, DeepcoinProductTypes productType,
		WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint));
		_productType = productType;
		_workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => nameof(Deepcoin) + "_" + _productType + "_PublicWs";

	public event Func<DeepcoinProductTypes, string, DeepcoinWsTicker, CancellationToken, ValueTask> TickerReceived;
	public event Func<DeepcoinProductTypes, string, DeepcoinWsBook, QuoteChangeStates, long, CancellationToken, ValueTask> BookReceived;
	public event Func<DeepcoinProductTypes, string, DeepcoinWsTrade, CancellationToken, ValueTask> TradeReceived;
	public event Func<DeepcoinProductTypes, string, DeepcoinWsCandleIntervals, DeepcoinCandle, CancellationToken, ValueTask> CandleReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<DeepcoinProductTypes, ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException("Deepcoin public WebSocket is already initialized.");
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

	public ValueTask SubscribeTickerAsync(string instrumentId,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(DeepcoinWsTopics.Market, instrumentId, default), true,
			cancellationToken);

	public ValueTask UnsubscribeTickerAsync(string instrumentId,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(DeepcoinWsTopics.Market, instrumentId, default), false,
			cancellationToken);

	public ValueTask SubscribeBookAsync(string instrumentId,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(DeepcoinWsTopics.Book, instrumentId, default), true,
			cancellationToken);

	public ValueTask UnsubscribeBookAsync(string instrumentId,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(DeepcoinWsTopics.Book, instrumentId, default), false,
			cancellationToken);

	public ValueTask SubscribeTradesAsync(string instrumentId,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(DeepcoinWsTopics.Trade, instrumentId, default), true,
			cancellationToken);

	public ValueTask UnsubscribeTradesAsync(string instrumentId,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(DeepcoinWsTopics.Trade, instrumentId, default), false,
			cancellationToken);

	public ValueTask SubscribeCandlesAsync(string instrumentId, TimeSpan timeFrame,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(DeepcoinWsTopics.Kline, instrumentId, timeFrame), true,
			cancellationToken);

	public ValueTask UnsubscribeCandlesAsync(string instrumentId, TimeSpan timeFrame,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(new(DeepcoinWsTopics.Kline, instrumentId, timeFrame), false,
			cancellationToken);

	public ValueTask PingAsync(CancellationToken cancellationToken)
		=> _client is { IsConnected: true } client
			? SendAsync(client, "ping", cancellationToken)
			: default;

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
			"StockSharp-Deepcoin-Connector/2.0");
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

	private async ValueTask OnStateChangedAsync(WebSocketClient client, ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			KeyValuePair<DeepcoinWsSubscriptionKey, long>[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _subscriptions];
			foreach (var subscription in subscriptions)
				await SendSubscriptionAsync(client, subscription.Key, subscription.Value, true,
					cancellationToken);
		}

		if (StateChanged is { } handler)
			await handler(_productType, state, cancellationToken);
	}

	private async ValueTask ChangeSubscriptionAsync(DeepcoinWsSubscriptionKey subscription,
		bool isSubscribe, CancellationToken cancellationToken)
	{
		long localNumber;
		using (_sync.EnterScope())
		{
			if (isSubscribe)
			{
				if (_subscriptions.ContainsKey(subscription))
					return;
				localNumber = Interlocked.Increment(ref _localNumber);
				_subscriptions.Add(subscription, localNumber);
				_wireSymbols[subscription.InstrumentId.ToDeepcoinWsSymbol(_productType)] =
					subscription.InstrumentId;
			}
			else
			{
				if (!_subscriptions.Remove(subscription, out localNumber))
					return;
				var wireSymbol = subscription.InstrumentId.ToDeepcoinWsSymbol(_productType);
				if (!_subscriptions.Keys.Any(item => item.InstrumentId.EqualsIgnoreCase(subscription.InstrumentId)))
					_wireSymbols.Remove(wireSymbol);
			}
		}

		if (_client?.IsConnected == true)
			await SendSubscriptionAsync(_client, subscription, localNumber, isSubscribe,
				cancellationToken);
	}

	private ValueTask SendSubscriptionAsync(WebSocketClient client,
		DeepcoinWsSubscriptionKey subscription, long localNumber, bool isSubscribe,
		CancellationToken cancellationToken)
		=> SendAsync(client, new DeepcoinWsSubscriptionRequest
		{
			Action = isSubscribe
				? DeepcoinWsSubscriptionActions.Subscribe
				: DeepcoinWsSubscriptionActions.Unsubscribe,
			Symbol = subscription.InstrumentId.ToDeepcoinWsSymbol(_productType),
			LocalNumber = localNumber,
			ResumeNumber = -1,
			Topic = subscription.Topic,
			Count = subscription.Topic == DeepcoinWsTopics.Kline ? 1 : null,
			Period = subscription.Topic == DeepcoinWsTopics.Kline
				? subscription.TimeFrame.ToDeepcoinWsInterval()
				: null,
		}, cancellationToken);

	private async ValueTask SendAsync(WebSocketClient client, string payload,
		CancellationToken cancellationToken)
	{
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(payload, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask SendAsync<TPayload>(WebSocketClient client, TPayload payload,
		CancellationToken cancellationToken)
	{
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(payload, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask OnProcessAsync(WebSocketClient client, WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		_ = client;
		var payload = message.AsString();
		if (payload.IsEmpty() || payload.EqualsIgnoreCase("pong"))
			return;

		try
		{
			var header = Deserialize<DeepcoinPublicHeader>(payload);
			switch (header.Action)
			{
				case DeepcoinPublicActions.Subscription:
					var subscription = Deserialize<DeepcoinWsSubscriptionEnvelope>(payload);
					if (!subscription.Message.EqualsIgnoreCase("Success"))
						throw new InvalidOperationException(
							$"Deepcoin WebSocket subscription failed: {subscription.Message}".Trim());
					break;
				case DeepcoinPublicActions.Market:
					await ProcessMarketAsync(payload, cancellationToken);
					break;
				case DeepcoinPublicActions.Trade:
					await ProcessTradesAsync(payload, cancellationToken);
					break;
				case DeepcoinPublicActions.Book:
					await ProcessBookAsync(payload, cancellationToken);
					break;
				case DeepcoinPublicActions.Kline:
					await ProcessCandlesAsync(payload, cancellationToken);
					break;
				default:
					throw new InvalidDataException("Deepcoin WebSocket returned an unknown action.");
			}
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or
			InvalidOperationException or FormatException or OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask ProcessMarketAsync(string payload,
		CancellationToken cancellationToken)
	{
		var envelope = Deserialize<DeepcoinWsMarketEnvelope>(payload);
		if (TickerReceived is not { } handler)
			return;
		foreach (var ticker in envelope.Data ?? [])
		{
			var instrumentId = ResolveInstrument(ticker.InstrumentId);
			if (!instrumentId.IsEmpty())
				await handler(_productType, instrumentId, ticker, cancellationToken);
		}
	}

	private async ValueTask ProcessTradesAsync(string payload,
		CancellationToken cancellationToken)
	{
		var envelope = Deserialize<DeepcoinWsTradeEnvelope>(payload);
		var instrumentId = ResolveInstrument(envelope.InstrumentId);
		if (instrumentId.IsEmpty() || TradeReceived is not { } handler)
			return;
		foreach (var trade in envelope.Data ?? [])
			await handler(_productType, instrumentId, trade, cancellationToken);
	}

	private async ValueTask ProcessBookAsync(string payload,
		CancellationToken cancellationToken)
	{
		var envelope = Deserialize<DeepcoinWsBookEnvelope>(payload);
		var instrumentId = ResolveInstrument(envelope.InstrumentId);
		if (instrumentId.IsEmpty() || envelope.Data is null || BookReceived is not { } handler)
			return;
		await handler(_productType, instrumentId, envelope.Data,
			envelope.UpdateType == DeepcoinBookUpdateTypes.Full
				? QuoteChangeStates.SnapshotComplete
				: QuoteChangeStates.Increment,
			envelope.Timestamp, cancellationToken);
	}

	private async ValueTask ProcessCandlesAsync(string payload,
		CancellationToken cancellationToken)
	{
		var envelope = Deserialize<DeepcoinWsCandleEnvelope>(payload);
		var instrumentId = ResolveInstrument(envelope.InstrumentId);
		if (instrumentId.IsEmpty() || CandleReceived is not { } handler)
			return;
		foreach (var candle in envelope.Data ?? [])
			await handler(_productType, instrumentId, envelope.Period, candle, cancellationToken);
	}

	private string ResolveInstrument(string wireSymbol)
	{
		if (wireSymbol.IsEmpty())
			return null;
		using (_sync.EnterScope())
			return _wireSymbols.TryGetValue(wireSymbol, out var instrumentId)
				? instrumentId
				: null;
	}

	private TMessage Deserialize<TMessage>(string payload)
	{
		try
		{
			var result = JsonConvert.DeserializeObject<TMessage>(payload, _jsonSettings);
			if (result is null)
				throw new InvalidDataException("Deepcoin WebSocket returned an empty message.");
			return result;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException("Deepcoin WebSocket returned malformed JSON.", error);
		}
	}

	private ValueTask RaiseErrorAsync(Exception error, CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;
}
