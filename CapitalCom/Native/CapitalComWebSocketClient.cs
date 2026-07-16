namespace StockSharp.CapitalCom.Native;

internal readonly record struct CapitalComCandleSubscription(string Epic, string Resolution);

internal sealed class CapitalComWebSocketClient : BaseLogReceiver
{
	private readonly WebSocketClient _client;
	private readonly CapitalComSession _session;
	private readonly object _sync = new();
	private readonly HashSet<string> _marketEpics = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<CapitalComCandleSubscription> _candleSubscriptions = [];
	private long _correlationId;

	public CapitalComWebSocketClient(CapitalComSession session, int reconnectAttempts, WorkingTime workingTime)
	{
		_session = session ?? throw new ArgumentNullException(nameof(session));
		_client = new(
			session.StreamingUrl.ThrowIfEmpty(nameof(session.StreamingUrl)),
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
		_client.PostConnect += RestoreSubscriptions;
	}

	public override string Name => nameof(CapitalCom) + "_WebSocket";

	public event Func<CapitalComQuote, CancellationToken, ValueTask> QuoteReceived;
	public event Func<CapitalComOhlc, CancellationToken, ValueTask> OhlcReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public ValueTask ConnectAsync(CancellationToken cancellationToken)
		=> _client.ConnectAsync(cancellationToken);

	public void Disconnect() => _client.Disconnect();

	public ValueTask Ping(CancellationToken cancellationToken)
		=> SendRequest("ping", (CapitalComEmptyPayload)null, cancellationToken);

	public async ValueTask SubscribeMarket(string epic, CancellationToken cancellationToken)
	{
		epic.ThrowIfEmpty(nameof(epic));
		lock (_sync)
		{
			EnsureCapacity(epic);
			if (!_marketEpics.Add(epic))
				return;
		}

		try
		{
			await SendMarketRequest("marketData.subscribe", [epic], cancellationToken);
		}
		catch
		{
			lock (_sync)
				_marketEpics.Remove(epic);
			throw;
		}
	}

	public async ValueTask UnsubscribeMarket(string epic, CancellationToken cancellationToken)
	{
		lock (_sync)
		{
			if (!_marketEpics.Remove(epic))
				return;
		}
		await SendMarketRequest("marketData.unsubscribe", [epic], cancellationToken);
	}

	public async ValueTask SubscribeCandles(string epic, string resolution, CancellationToken cancellationToken)
	{
		epic.ThrowIfEmpty(nameof(epic));
		resolution.ThrowIfEmpty(nameof(resolution));
		var subscription = new CapitalComCandleSubscription(epic, resolution);
		lock (_sync)
		{
			EnsureCapacity(epic);
			if (!_candleSubscriptions.Add(subscription))
				return;
		}

		try
		{
			await SendOhlcSubscribe([epic], [resolution], cancellationToken);
		}
		catch
		{
			lock (_sync)
				_candleSubscriptions.Remove(subscription);
			throw;
		}
	}

	public async ValueTask UnsubscribeCandles(string epic, string resolution, CancellationToken cancellationToken)
	{
		lock (_sync)
		{
			if (!_candleSubscriptions.Remove(new(epic, resolution)))
				return;
		}
		await SendRequest("OHLCMarketData.unsubscribe", new CapitalComOhlcUnsubscribePayload
		{
			Epics = [epic],
			Resolutions = [resolution],
		}, cancellationToken);
	}

	private async ValueTask RestoreSubscriptions(bool isReconnect, CancellationToken cancellationToken)
	{
		string[] marketEpics;
		CapitalComCandleSubscription[] candles;
		lock (_sync)
		{
			marketEpics = [.. _marketEpics];
			candles = [.. _candleSubscriptions];
		}

		if (marketEpics.Length > 0)
			await SendMarketRequest("marketData.subscribe", marketEpics, cancellationToken);

		foreach (var resolution in candles.Select(c => c.Resolution).Distinct(StringComparer.OrdinalIgnoreCase))
		{
			var epics = candles.Where(c => c.Resolution.EqualsIgnoreCase(resolution))
				.Select(c => c.Epic).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
			if (epics.Length > 0)
				await SendOhlcSubscribe(epics, [resolution], cancellationToken);
		}
	}

	private async ValueTask Process(WebSocketMessage message, CancellationToken cancellationToken)
	{
		var raw = message.AsString();
		if (raw.IsEmpty())
			return;

		var header = JsonConvert.DeserializeObject<CapitalComSocketHeader>(raw)
			?? throw new InvalidDataException("Capital.com returned an invalid WebSocket message.");

		if (!header.Status.IsEmpty() && !header.Status.EqualsIgnoreCase("OK"))
		{
			if (Error is { } errorHandler)
			{
				await errorHandler(new InvalidOperationException(
					$"Capital.com WebSocket request '{header.Destination}' failed with status '{header.Status}'."),
					cancellationToken);
			}
			return;
		}

		switch (header.Destination?.ToLowerInvariant())
		{
			case "quote":
			{
				var quote = JsonConvert.DeserializeObject<CapitalComSocketMessage<CapitalComQuote>>(raw)?.Payload;
				if (quote != null && QuoteReceived is { } quoteHandler)
					await quoteHandler(quote, cancellationToken);
				break;
			}
			case "ohlc.event":
			{
				var ohlc = JsonConvert.DeserializeObject<CapitalComSocketMessage<CapitalComOhlc>>(raw)?.Payload;
				if (ohlc != null && OhlcReceived is { } ohlcHandler)
					await ohlcHandler(ohlc, cancellationToken);
				break;
			}
		}
	}

	private ValueTask SendMarketRequest(string destination, string[] epics, CancellationToken cancellationToken)
		=> SendRequest(destination, new CapitalComMarketDataPayload { Epics = epics }, cancellationToken);

	private ValueTask SendOhlcSubscribe(string[] epics, string[] resolutions,
		CancellationToken cancellationToken)
		=> SendRequest("OHLCMarketData.subscribe", new CapitalComOhlcSubscribePayload
		{
			Epics = epics,
			Resolutions = resolutions,
		}, cancellationToken);

	private ValueTask SendRequest<TPayload>(string destination, TPayload payload,
		CancellationToken cancellationToken)
		=> _client.SendAsync(JsonConvert.SerializeObject(new CapitalComSocketRequest<TPayload>
		{
			Destination = destination,
			CorrelationId = Interlocked.Increment(ref _correlationId).ToString(CultureInfo.InvariantCulture),
			Cst = _session.Cst,
			SecurityToken = _session.SecurityToken,
			Payload = payload,
		}), cancellationToken);

	private void EnsureCapacity(string epic)
	{
		if (_marketEpics.Contains(epic) || _candleSubscriptions.Any(c => c.Epic.EqualsIgnoreCase(epic)))
			return;

		var count = _marketEpics.Concat(_candleSubscriptions.Select(c => c.Epic))
			.Distinct(StringComparer.OrdinalIgnoreCase).Count();
		if (count >= 40)
			throw new InvalidOperationException(
				"Capital.com WebSocket allows subscriptions to at most 40 instruments.");
	}

	protected override void DisposeManaged()
	{
		_client.PostConnect -= RestoreSubscriptions;
		_client.Dispose();
		base.DisposeManaged();
	}
}
