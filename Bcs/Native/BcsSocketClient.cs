namespace StockSharp.Bcs.Native;

readonly record struct BcsMarketSubscription(
	int DataType,
	string Ticker,
	string ClassCode,
	string TimeFrame,
	int Depth);

sealed class BcsSocketClient : BaseLogReceiver
{
	private readonly WebSocketClient _client;
	private readonly Func<CancellationToken, Task<string>> _accessTokenProvider;
	private readonly SynchronizedSet<BcsMarketSubscription> _subscriptions = [];
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		ContractResolver = new CamelCasePropertyNamesContractResolver(),
		NullValueHandling = NullValueHandling.Ignore,
		DateTimeZoneHandling = DateTimeZoneHandling.Utc,
	};

	public BcsSocketClient(string endpoint,
		Func<CancellationToken, Task<string>> accessTokenProvider,
		int reconnectAttempts, WorkingTime workingTime)
	{
		_accessTokenProvider = accessTokenProvider ??
			throw new ArgumentNullException(nameof(accessTokenProvider));
		_client = new(
			endpoint.ThrowIfEmpty(nameof(endpoint)),
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

	public override string Name => "BCS_WebSocket";

	public event Func<BcsQuote, CancellationToken, ValueTask> QuoteReceived;
	public event Func<BcsOrderBook, CancellationToken, ValueTask> OrderBookReceived;
	public event Func<BcsTrade, CancellationToken, ValueTask> TradeReceived;
	public event Func<BcsCandle, CancellationToken, ValueTask> CandleReceived;
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

	public async ValueTask Subscribe(BcsMarketSubscription subscription,
		CancellationToken cancellationToken)
	{
		subscription = Validate(subscription);
		if (_subscriptions.Contains(subscription))
			return;

		var instrumentCount = _subscriptions
			.Select(s => (s.Ticker, s.ClassCode))
			.Append((subscription.Ticker, subscription.ClassCode))
			.Distinct()
			.Count();
		if (instrumentCount > 100)
			throw new InvalidOperationException(
				"BCS allows at most 100 instruments per WebSocket connection.");

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

	public async ValueTask Unsubscribe(BcsMarketSubscription subscription,
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
			$"Bearer {accessToken.ThrowIfEmpty(nameof(accessToken))}");
		socket.Options.SetRequestHeader("User-Agent", "StockSharp-BCS/1.0");
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

		BcsSocketHeader header;
		try
		{
			header = JsonConvert.DeserializeObject<BcsSocketHeader>(
				raw, _jsonSettings);
		}
		catch (JsonException error)
		{
			await RaiseError(new InvalidDataException(
				"BCS returned an invalid WebSocket message.", error),
				cancellationToken);
			return;
		}

		if (header is null)
			return;

		if (header.Errors?.Length > 0)
		{
			var text = header.Errors
				.Select(e => $"{e.Code}: {e.Message}".Trim())
				.Join("; ");
			await RaiseError(new InvalidOperationException(
				$"BCS WebSocket error: {text}"), cancellationToken);
			return;
		}

		switch (header.ResponseType)
		{
			case "Quotes":
				if (QuoteReceived is { } quoteHandler)
					await quoteHandler.InvokeAsync(
						JsonConvert.DeserializeObject<BcsQuote>(raw, _jsonSettings),
						cancellationToken);
				break;
			case "OrderBook":
				if (OrderBookReceived is { } bookHandler)
					await bookHandler.InvokeAsync(
						JsonConvert.DeserializeObject<BcsOrderBook>(raw, _jsonSettings),
						cancellationToken);
				break;
			case "LastTrades":
				if (TradeReceived is { } tradeHandler)
					await tradeHandler.InvokeAsync(
						JsonConvert.DeserializeObject<BcsTrade>(raw, _jsonSettings),
						cancellationToken);
				break;
			case "CandleStick":
				if (CandleReceived is { } candleHandler)
					await candleHandler.InvokeAsync(
						JsonConvert.DeserializeObject<BcsCandle>(raw, _jsonSettings),
						cancellationToken);
				break;
			case "QuotesSuccess":
			case "OrderBookSuccess":
			case "LastTradesSuccess":
			case "CandleStickSuccess":
				break;
			default:
				this.AddDebugLog("BCS ignored WebSocket response type '{0}'.",
					header.ResponseType);
				break;
		}
	}

	private ValueTask Send(BcsMarketSubscription subscription, bool subscribe,
		CancellationToken cancellationToken)
		=> _client.SendAsync(JsonConvert.SerializeObject(new BcsSocketRequest
		{
			SubscribeType = subscribe ? 0 : 1,
			DataType = subscription.DataType,
			Depth = subscription.DataType == 0 ? subscription.Depth : null,
			TimeFrame = subscription.DataType == 1
				? subscription.TimeFrame : null,
			Instruments =
			[
				new()
				{
					Ticker = subscription.Ticker,
					ClassCode = subscription.ClassCode,
				},
			],
		}, _jsonSettings), cancellationToken);

	private static BcsMarketSubscription Validate(
		BcsMarketSubscription subscription)
	{
		if (subscription.DataType is < 0 or > 3)
			throw new ArgumentOutOfRangeException(nameof(subscription));
		if (subscription.Ticker.IsEmpty())
			throw new ArgumentException("BCS ticker is required.",
				nameof(subscription));
		if (subscription.ClassCode.IsEmpty())
			throw new ArgumentException("BCS class code is required.",
				nameof(subscription));
		if (subscription.DataType == 1 && subscription.TimeFrame.IsEmpty())
			throw new ArgumentException("BCS candle time frame is required.",
				nameof(subscription));

		return subscription with
		{
			Ticker = subscription.Ticker.Trim().ToUpperInvariant(),
			ClassCode = subscription.ClassCode.Trim().ToUpperInvariant(),
			Depth = Math.Clamp(subscription.Depth, 1, 20),
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
