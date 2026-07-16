namespace StockSharp.Bloomberg.Native;

internal sealed class BloombergSdkClient : IDisposable
{
	private const string _marketDataService = "//blp/mktdata";
	private const string _referenceDataService = "//blp/refdata";

	private const string _marketFields =
		"LAST_PRICE,LAST_TRADE,LAST_TRADE_SIZE,BID,BID_SIZE,ASK,ASK_SIZE,OPEN,HIGH,LOW,PREV_CLOSE_VAL_REALTIME,VOLUME,OPEN_INT,TRADE_UPDATE_STAMP_RT";

	private static readonly string[] _referenceFields =
	[
		"NAME", "SECURITY_TYP", "MARKET_SECTOR_DES", "EXCH_CODE", "CRNCY", "ID_BB_GLOBAL",
		"MIN_TICK_SIZE", "PX_TRADE_LOT_SIZE", "FUT_CONT_SIZE", "LAST_TRADEABLE_DT",
		"OPT_STRIKE_PX", "OPT_PUT_CALL", "UNDERLYING_SECURITY_DES",
	];

	private static readonly string[] _historyFields = ["PX_OPEN", "PX_HIGH", "PX_LOW", "PX_LAST", "VOLUME"];

	private static readonly string[] _emsxOrderFields =
	[
		"API_SEQ_NUM", "EMSX_ACCOUNT", "EMSX_AMOUNT", "EMSX_AVG_PRICE", "EMSX_BROKER",
		"EMSX_DATE", "EMSX_FILL_ID", "EMSX_FILLED", "EMSX_LAST_PRICE", "EMSX_LAST_SHARES",
		"EMSX_LIMIT_PRICE", "EMSX_ORDER_TYPE", "EMSX_REASON_DESC",
		"EMSX_REMAIN_BALANCE", "EMSX_ROUTE_ID", "EMSX_SEQUENCE", "EMSX_SIDE", "EMSX_STATUS",
		"EMSX_STOP_PRICE", "EMSX_TICKER", "EMSX_TIF", "EMSX_TIME_STAMP", "EMSX_WORKING",
	];

	private static readonly string[] _emsxRouteFields =
	[
		"API_SEQ_NUM", "EMSX_ACCOUNT", "EMSX_AMOUNT", "EMSX_AVG_PRICE", "EMSX_BROKER",
		"EMSX_FILL_ID", "EMSX_FILLED", "EMSX_LAST_PRICE", "EMSX_LAST_SHARES",
		"EMSX_LIMIT_PRICE", "EMSX_ORDER_TYPE", "EMSX_REASON_DESC",
		"EMSX_REMAIN_BALANCE", "EMSX_ROUTE_ID", "EMSX_SEQUENCE", "EMSX_STATUS",
		"EMSX_STOP_PRICE", "EMSX_TIF", "EMSX_TIME_STAMP", "EMSX_WORKING",
	];

	private enum BloombergSubscriptionKinds
	{
		MarketData,
		EmsxOrder,
		EmsxRoute,
	}

	private sealed class BloombergSubscriptionInfo
	{
		public BloombergSubscriptionKinds Kind { get; init; }
		public long ExternalId { get; init; }
	}

	private interface IRequestSink
	{
		void Process(object message);
		void Complete();
		void Fail(Exception error);
		void Cancel(CancellationToken cancellationToken);
	}

	private sealed class RequestSink<T> : IRequestSink
	{
		private readonly Func<object, IEnumerable<T>> _parser;
		private readonly List<T> _items = [];
		private readonly TaskCompletionSource<T[]> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public RequestSink(Func<object, IEnumerable<T>> parser)
		{
			_parser = parser ?? throw new ArgumentNullException(nameof(parser));
		}

		public Task<T[]> Task => _completion.Task;

		public void Process(object message)
			=> _items.AddRange(_parser(message));

		public void Complete()
			=> _completion.TrySetResult([.. _items]);

		public void Fail(Exception error)
			=> _completion.TrySetException(error);

		public void Cancel(CancellationToken cancellationToken)
			=> _completion.TrySetCanceled(cancellationToken);
	}

	private readonly string _sdkPath;
	private readonly string _host;
	private readonly int _port;
	private readonly bool _isEmsxEnabled;
	private readonly string _emsxService;
	private readonly ConcurrentDictionary<long, IRequestSink> _requests = [];
	private readonly ConcurrentDictionary<long, BloombergSubscriptionInfo> _subscriptions = [];
	private readonly ConcurrentDictionary<long, long> _marketCorrelations = [];

	private BloombergSdkBridge _bridge;
	private CancellationTokenSource _eventCancellation;
	private Task _eventLoop;
	private long _nextCorrelationId;
	private long _marketSequence;
	private int _isDisconnecting;

	public BloombergSdkClient(string sdkPath, string host, int port, bool isEmsxEnabled, string emsxService)
	{
		_sdkPath = sdkPath;
		_host = host.ThrowIfEmpty(nameof(host));
		_port = port;
		_isEmsxEnabled = isEmsxEnabled;
		_emsxService = emsxService.ThrowIfEmpty(nameof(emsxService));
	}

	public event Func<BloombergMarketUpdate, CancellationToken, ValueTask> MarketDataReceived;
	public event Func<BloombergEmsxOrderUpdate, CancellationToken, ValueTask> EmsxOrderReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<Exception, CancellationToken, ValueTask> ConnectionLost;

	public string Version => _bridge?.Version;

	public void Connect()
	{
		if (_bridge != null)
			throw new InvalidOperationException("The Bloomberg session is already connected.");

		Interlocked.Exchange(ref _isDisconnecting, 0);
		var bridge = new BloombergSdkBridge(_sdkPath, _host, _port);
		try
		{
			bridge.Start();
			bridge.OpenService(_marketDataService);
			bridge.OpenService(_referenceDataService);
			if (_isEmsxEnabled)
				bridge.OpenService(_emsxService);

			_bridge = bridge;
			_eventCancellation = new CancellationTokenSource();
			_eventLoop = Task.Run(() => ProcessEventsAsync(_eventCancellation.Token));

			if (_isEmsxEnabled)
				SubscribeEmsx();
		}
		catch
		{
			bridge.Dispose();
			_bridge = null;
			_eventCancellation?.Dispose();
			_eventCancellation = null;
			throw;
		}
	}

	public async ValueTask DisconnectAsync()
	{
		if (Interlocked.Exchange(ref _isDisconnecting, 1) != 0)
			return;

		var cancellation = _eventCancellation;
		var bridge = _bridge;
		var loop = _eventLoop;

		cancellation?.Cancel();
		bridge?.Dispose();

		if (loop != null)
		{
			try
			{
				await loop;
			}
			catch (OperationCanceledException)
			{
			}
		}

		FailPending(new InvalidOperationException("The Bloomberg session was disconnected."));
		_marketCorrelations.Clear();
		_subscriptions.Clear();
		_bridge = null;
		_eventLoop = null;
		_eventCancellation = null;
		cancellation?.Dispose();
	}

	public void SubscribeMarketData(long subscriptionId, string symbol)
	{
		var bridge = EnsureConnected();
		if (_marketCorrelations.ContainsKey(subscriptionId))
			throw new InvalidOperationException($"Bloomberg subscription {subscriptionId} already exists.");

		var correlationId = GetNextCorrelationId();
		var info = new BloombergSubscriptionInfo
		{
			Kind = BloombergSubscriptionKinds.MarketData,
			ExternalId = subscriptionId,
		};

		if (!_marketCorrelations.TryAdd(subscriptionId, correlationId) || !_subscriptions.TryAdd(correlationId, info))
			throw new InvalidOperationException($"Bloomberg subscription {subscriptionId} already exists.");

		try
		{
			bridge.Subscribe(symbol.ThrowIfEmpty(nameof(symbol)), _marketFields, string.Empty, correlationId);
		}
		catch
		{
			_marketCorrelations.TryRemove(subscriptionId, out _);
			_subscriptions.TryRemove(correlationId, out _);
			throw;
		}
	}

	public void UnsubscribeMarketData(long subscriptionId)
	{
		var bridge = EnsureConnected();
		if (!_marketCorrelations.TryRemove(subscriptionId, out var correlationId))
			return;
		_subscriptions.TryRemove(correlationId, out _);
		bridge.Unsubscribe(correlationId);
	}

	public Task<BloombergSecurityInfo[]> LookupSecurityAsync(string symbol, CancellationToken cancellationToken)
		=> SendRequestAsync(
			_referenceDataService,
			"ReferenceDataRequest",
			request =>
			{
				var securities = _bridge.GetElement(request, "securities");
				_bridge.AppendValue(securities, symbol.ThrowIfEmpty(nameof(symbol)));
				var fields = _bridge.GetElement(request, "fields");
				foreach (var field in _referenceFields)
					_bridge.AppendValue(fields, field);
			},
			ParseReferenceData,
			cancellationToken);

	public Task<BloombergHistoricalBar[]> GetHistoricalBarsAsync(
		string symbol,
		DateTime from,
		DateTime to,
		BloombergBarPeriods period,
		int interval,
		CancellationToken cancellationToken)
	{
		if (period == BloombergBarPeriods.Minute)
		{
			return SendRequestAsync(
				_referenceDataService,
				"IntradayBarRequest",
				request =>
				{
					_bridge.Set(request, "security", symbol.ThrowIfEmpty(nameof(symbol)));
					_bridge.Set(request, "eventType", "TRADE");
					_bridge.Set(request, "interval", interval);
					_bridge.Set(request, "startDateTime", EnsureUtc(from));
					_bridge.Set(request, "endDateTime", EnsureUtc(to));
				},
				ParseIntradayBars,
				cancellationToken);
		}

		return SendRequestAsync(
			_referenceDataService,
			"HistoricalDataRequest",
			request =>
			{
				var securities = _bridge.GetElement(request, "securities");
				_bridge.AppendValue(securities, symbol.ThrowIfEmpty(nameof(symbol)));
				var fields = _bridge.GetElement(request, "fields");
				foreach (var field in _historyFields)
					_bridge.AppendValue(fields, field);
				_bridge.Set(request, "startDate", EnsureUtc(from).ToString("yyyyMMdd", CultureInfo.InvariantCulture));
				_bridge.Set(request, "endDate", EnsureUtc(to).ToString("yyyyMMdd", CultureInfo.InvariantCulture));
				_bridge.Set(request, "periodicityAdjustment", "ACTUAL");
				_bridge.Set(request, "periodicitySelection", period.ToString().ToUpperInvariant());
			},
			ParseHistoricalBars,
			cancellationToken);
	}

	public async Task<BloombergEmsxResult> RegisterOrderAsync(BloombergEmsxRegisterRequest order, CancellationToken cancellationToken)
	{
		EnsureEmsx();
		var response = await SendRequestAsync(
			_emsxService,
			"CreateOrderAndRouteEx",
			request =>
			{
				_bridge.Set(request, "EMSX_TICKER", order.Symbol);
				_bridge.Set(request, "EMSX_AMOUNT", checked((int)order.Amount));
				_bridge.Set(request, "EMSX_ORDER_TYPE", order.OrderType);
				_bridge.Set(request, "EMSX_TIF", order.TimeInForce);
				_bridge.Set(request, "EMSX_HAND_INSTRUCTION", "ANY");
				_bridge.Set(request, "EMSX_SIDE", order.Side);
				_bridge.Set(request, "EMSX_BROKER", order.Broker);
				SetOptional(request, "EMSX_ACCOUNT", order.Account);
				SetOptional(request, "EMSX_LIMIT_PRICE", order.LimitPrice);
				SetOptional(request, "EMSX_STOP_PRICE", order.StopPrice);
				SetOptionalDate(request, "EMSX_GTD_DATE", order.GoodTillDate);
				SetOptional(request, "EMSX_ORDER_REF_ID", order.OrderReference);
			},
			ParseEmsxResult,
			cancellationToken);
		return RequireSingleResult(response, "CreateOrderAndRouteEx");
	}

	public async Task<BloombergEmsxResult> ReplaceOrderAsync(BloombergEmsxReplaceRequest order, CancellationToken cancellationToken)
	{
		EnsureEmsx();
		var response = await SendRequestAsync(
			_emsxService,
			"ModifyOrderEx",
			request =>
			{
				_bridge.Set(request, "EMSX_SEQUENCE", checked((int)order.Sequence));
				_bridge.Set(request, "EMSX_AMOUNT", checked((int)order.Amount));
				_bridge.Set(request, "EMSX_ORDER_TYPE", order.OrderType);
				_bridge.Set(request, "EMSX_TIF", order.TimeInForce);
				SetOptional(request, "EMSX_LIMIT_PRICE", order.LimitPrice);
				SetOptional(request, "EMSX_STOP_PRICE", order.StopPrice);
				SetOptionalDate(request, "EMSX_GTD_DATE", order.GoodTillDate);
			},
			ParseEmsxResult,
			cancellationToken);
		return RequireSingleResult(response, "ModifyOrderEx");
	}

	public async Task<BloombergEmsxResult> CancelOrderAsync(BloombergEmsxCancelRequest order, CancellationToken cancellationToken)
	{
		EnsureEmsx();
		var response = await SendRequestAsync(
			_emsxService,
			"CancelRoute",
			request =>
			{
				var routes = _bridge.GetElement(request, "ROUTES");
				var route = _bridge.AppendElement(routes);
				_bridge.SetElement(route, "EMSX_SEQUENCE", checked((int)order.Sequence));
				_bridge.SetElement(route, "EMSX_ROUTE_ID", checked((int)order.RouteId));
			},
			ParseEmsxResult,
			cancellationToken);
		return RequireSingleResult(response, "CancelRoute");
	}

	public void Dispose()
	{
		_eventCancellation?.Cancel();
		_bridge?.Dispose();
		_eventCancellation?.Dispose();
		_eventCancellation = null;
		_bridge = null;
	}

	private void SubscribeEmsx()
	{
		SubscribeEmsxTopic($"{_emsxService}/order?fields={string.Join(',', _emsxOrderFields)}", BloombergSubscriptionKinds.EmsxOrder);
		SubscribeEmsxTopic($"{_emsxService}/route?fields={string.Join(',', _emsxRouteFields)}", BloombergSubscriptionKinds.EmsxRoute);
	}

	private void SubscribeEmsxTopic(string topic, BloombergSubscriptionKinds kind)
	{
		var correlationId = GetNextCorrelationId();
		if (!_subscriptions.TryAdd(correlationId, new BloombergSubscriptionInfo { Kind = kind }))
			throw new InvalidOperationException("The Bloomberg EMSX correlation identifier is already in use.");
		try
		{
			_bridge.Subscribe(topic, correlationId);
		}
		catch
		{
			_subscriptions.TryRemove(correlationId, out _);
			throw;
		}
	}

	private async Task ProcessEventsAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				object eventData = null;
				try
				{
					eventData = _bridge.NextEvent(500);
					if (eventData != null)
						await ProcessEventAsync(eventData, cancellationToken);
				}
				finally
				{
					_bridge?.DisposeObject(eventData);
				}
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception error)
		{
			FailPending(error);
			if (Interlocked.CompareExchange(ref _isDisconnecting, 0, 0) == 0)
				await RaiseConnectionLostAsync(error);
		}
	}

	private async ValueTask ProcessEventAsync(object eventData, CancellationToken cancellationToken)
	{
		var eventType = _bridge.GetEventType(eventData)?.ToUpperInvariant();
		foreach (var message in _bridge.GetMessages(eventData))
		{
			switch (eventType)
			{
				case "PARTIAL_RESPONSE":
					ProcessResponse(message, false);
					break;
				case "RESPONSE":
					ProcessResponse(message, true);
					break;
				case "SUBSCRIPTION_DATA":
					await ProcessSubscriptionDataAsync(message, cancellationToken);
					break;
				case "SUBSCRIPTION_STATUS":
					await ProcessSubscriptionStatusAsync(message, cancellationToken);
					break;
				case "SESSION_STATUS":
				case "SERVICE_STATUS":
					await ProcessSessionStatusAsync(message, cancellationToken);
					break;
			}
		}
	}

	private void ProcessResponse(object message, bool isFinal)
	{
		var correlationId = _bridge.GetCorrelationId(message);
		if (!_requests.TryGetValue(correlationId, out var sink))
			return;

		try
		{
			ThrowResponseError(message);
			sink.Process(message);
			if (isFinal && _requests.TryRemove(correlationId, out sink))
				sink.Complete();
		}
		catch (Exception error)
		{
			_requests.TryRemove(correlationId, out _);
			sink.Fail(error);
		}
	}

	private async ValueTask ProcessSubscriptionDataAsync(object message, CancellationToken cancellationToken)
	{
		var correlationId = _bridge.GetCorrelationId(message);
		if (!_subscriptions.TryGetValue(correlationId, out var subscription))
			return;

		switch (subscription.Kind)
		{
			case BloombergSubscriptionKinds.MarketData:
				var marketHandler = MarketDataReceived;
				if (marketHandler != null)
					await marketHandler(ParseMarketData(message, subscription.ExternalId), cancellationToken);
				break;
			case BloombergSubscriptionKinds.EmsxOrder:
			case BloombergSubscriptionKinds.EmsxRoute:
				var update = ParseEmsxOrder(message, subscription.Kind == BloombergSubscriptionKinds.EmsxRoute);
				var orderHandler = EmsxOrderReceived;
				if (update != null && orderHandler != null)
					await orderHandler(update, cancellationToken);
				break;
		}
	}

	private async ValueTask ProcessSubscriptionStatusAsync(object message, CancellationToken cancellationToken)
	{
		var messageType = _bridge.GetMessageType(message)?.ToUpperInvariant();
		if (messageType is not ("SUBSCRIPTIONFAILURE" or "SUBSCRIPTIONTERMINATED"))
			return;

		var correlationId = _bridge.GetCorrelationId(message);
		if (!_subscriptions.TryRemove(correlationId, out var subscription))
			return;

		if (subscription.Kind == BloombergSubscriptionKinds.MarketData)
			_marketCorrelations.TryRemove(subscription.ExternalId, out _);
		await RaiseErrorAsync(CreateStatusError(message, messageType), cancellationToken);
	}

	private async ValueTask ProcessSessionStatusAsync(object message, CancellationToken cancellationToken)
	{
		var messageType = _bridge.GetMessageType(message)?.ToUpperInvariant();
		if (messageType is "SESSIONTERMINATED" or "SESSIONSTARTUPFAILURE")
		{
			var error = CreateStatusError(message, messageType);
			FailPending(error);
			await RaiseConnectionLostAsync(error);
		}
		else if (messageType is "SERVICEOPENFAILURE" or "SERVICEDOWN")
			await RaiseErrorAsync(CreateStatusError(message, messageType), cancellationToken);
	}

	private BloombergMarketUpdate ParseMarketData(object message, long subscriptionId)
		=> new()
		{
			SubscriptionId = subscriptionId,
			ServerTime = ReadMarketTime(message),
			LastPrice = GetDecimal(message, "LAST_PRICE", "LAST_TRADE"),
			LastSize = GetDecimal(message, "LAST_TRADE_SIZE"),
			BidPrice = GetDecimal(message, "BID"),
			BidSize = GetDecimal(message, "BID_SIZE"),
			AskPrice = GetDecimal(message, "ASK"),
			AskSize = GetDecimal(message, "ASK_SIZE"),
			OpenPrice = GetDecimal(message, "OPEN"),
			HighPrice = GetDecimal(message, "HIGH"),
			LowPrice = GetDecimal(message, "LOW"),
			ClosePrice = GetDecimal(message, "PREV_CLOSE_VAL_REALTIME"),
			Volume = GetDecimal(message, "VOLUME"),
			OpenInterest = GetDecimal(message, "OPEN_INT"),
			Sequence = Interlocked.Increment(ref _marketSequence),
		};

	private IEnumerable<BloombergSecurityInfo> ParseReferenceData(object message)
	{
		var securityData = GetRequiredElement(message, "securityData");
		for (var index = 0; index < _bridge.GetNumValues(securityData); index++)
		{
			var security = _bridge.GetValueAsElement(securityData, index);
			var error = ReadElementError(security, "securityError");
			var fieldData = _bridge.HasElement(security, "fieldData")
				? _bridge.GetElement(security, "fieldData")
				: null;
			yield return new BloombergSecurityInfo
			{
				Symbol = _bridge.TryGetString(security, "security"),
				Name = GetString(fieldData, "NAME"),
				SecurityType = GetString(fieldData, "SECURITY_TYP"),
				MarketSector = GetString(fieldData, "MARKET_SECTOR_DES"),
				Exchange = GetString(fieldData, "EXCH_CODE"),
				Currency = GetString(fieldData, "CRNCY"),
				GlobalId = GetString(fieldData, "ID_BB_GLOBAL"),
				PriceStep = GetDecimal(fieldData, "MIN_TICK_SIZE"),
				LotSize = GetDecimal(fieldData, "PX_TRADE_LOT_SIZE"),
				Multiplier = GetDecimal(fieldData, "FUT_CONT_SIZE"),
				ExpiryDate = GetDateTime(fieldData, "LAST_TRADEABLE_DT"),
				Strike = GetDecimal(fieldData, "OPT_STRIKE_PX"),
				PutCall = GetString(fieldData, "OPT_PUT_CALL"),
				Underlying = GetString(fieldData, "UNDERLYING_SECURITY_DES"),
				Error = error,
			};
		}
	}

	private IEnumerable<BloombergHistoricalBar> ParseHistoricalBars(object message)
	{
		var securityData = GetRequiredElement(message, "securityData");
		var securityError = ReadElementError(securityData, "securityError");
		if (!securityError.IsEmpty())
			throw new InvalidOperationException(securityError);
		if (!_bridge.HasElement(securityData, "fieldData"))
			yield break;

		var fieldData = _bridge.GetElement(securityData, "fieldData");
		for (var index = 0; index < _bridge.GetNumValues(fieldData); index++)
		{
			var bar = _bridge.GetValueAsElement(fieldData, index);
			var time = _bridge.TryGetDateTime(bar, "date");
			if (time == null)
				continue;
			yield return new BloombergHistoricalBar
			{
				Time = time.Value,
				Open = GetDecimal(bar, "PX_OPEN") ?? 0,
				High = GetDecimal(bar, "PX_HIGH") ?? 0,
				Low = GetDecimal(bar, "PX_LOW") ?? 0,
				Close = GetDecimal(bar, "PX_LAST") ?? 0,
				Volume = GetDecimal(bar, "VOLUME") ?? 0,
			};
		}
	}

	private IEnumerable<BloombergHistoricalBar> ParseIntradayBars(object message)
	{
		var barData = GetRequiredElement(message, "barData");
		if (!_bridge.HasElement(barData, "barTickData"))
			yield break;

		var bars = _bridge.GetElement(barData, "barTickData");
		for (var index = 0; index < _bridge.GetNumValues(bars); index++)
		{
			var bar = _bridge.GetValueAsElement(bars, index);
			var time = _bridge.TryGetDateTime(bar, "time");
			if (time == null)
				continue;
			yield return new BloombergHistoricalBar
			{
				Time = time.Value,
				Open = GetDecimal(bar, "open") ?? 0,
				High = GetDecimal(bar, "high") ?? 0,
				Low = GetDecimal(bar, "low") ?? 0,
				Close = GetDecimal(bar, "close") ?? 0,
				Volume = GetDecimal(bar, "volume") ?? 0,
				Events = (int?)_bridge.TryGetInt64(bar, "numEvents"),
			};
		}
	}

	private IEnumerable<BloombergEmsxResult> ParseEmsxResult(object message)
	{
		var errorCode = _bridge.TryGetInt64(message, "ERROR_CODE") ?? 0;
		var error = _bridge.TryGetString(message, "ERROR_MESSAGE");
		yield return new BloombergEmsxResult
		{
			Sequence = _bridge.TryGetInt64(message, "EMSX_SEQUENCE") ?? 0,
			RouteId = _bridge.TryGetInt64(message, "EMSX_ROUTE_ID") ?? 0,
			Status = (int)(_bridge.TryGetInt64(message, "STATUS") ?? errorCode),
			Message = _bridge.TryGetString(message, "MESSAGE"),
			Error = errorCode == 0 ? null : error.IsEmpty($"Bloomberg EMSX error {errorCode}.") ,
		};
	}

	private BloombergEmsxOrderUpdate ParseEmsxOrder(object message, bool isRoute)
	{
		var eventStatus = _bridge.TryGetInt64(message, "EVENT_STATUS") ?? 0;
		if (eventStatus == 1)
			return null;
		if (eventStatus == 11)
			return new BloombergEmsxOrderUpdate { IsEndOfInitialPaint = true, IsRoute = isRoute, ServerTime = DateTime.UtcNow };

		return new BloombergEmsxOrderUpdate
		{
			IsRoute = isRoute,
			ApiSequence = _bridge.TryGetInt64(message, "API_SEQ_NUM") ?? 0,
			Sequence = _bridge.TryGetInt64(message, "EMSX_SEQUENCE") ?? 0,
			RouteId = _bridge.TryGetInt64(message, "EMSX_ROUTE_ID") ?? 0,
			Symbol = _bridge.TryGetString(message, "EMSX_TICKER"),
			Account = _bridge.TryGetString(message, "EMSX_ACCOUNT"),
			Broker = _bridge.TryGetString(message, "EMSX_BROKER"),
			Side = _bridge.TryGetString(message, "EMSX_SIDE"),
			OrderType = _bridge.TryGetString(message, "EMSX_ORDER_TYPE"),
			TimeInForce = _bridge.TryGetString(message, "EMSX_TIF"),
			Status = _bridge.TryGetString(message, "EMSX_STATUS"),
			Reason = _bridge.TryGetString(message, "EMSX_REASON_DESC"),
			Amount = GetDecimal(message, "EMSX_AMOUNT"),
			Filled = GetDecimal(message, "EMSX_FILLED"),
			Remaining = GetDecimal(message, "EMSX_WORKING", "EMSX_REMAIN_BALANCE"),
			LimitPrice = GetDecimal(message, "EMSX_LIMIT_PRICE"),
			StopPrice = GetDecimal(message, "EMSX_STOP_PRICE"),
			AveragePrice = GetDecimal(message, "EMSX_AVG_PRICE"),
			FillId = _bridge.TryGetInt64(message, "EMSX_FILL_ID") ?? 0,
			LastPrice = GetDecimal(message, "EMSX_LAST_PRICE"),
			LastShares = GetDecimal(message, "EMSX_LAST_SHARES"),
			ServerTime = ParseEmsxTime(message),
		};
	}

	private Task<T[]> SendRequestAsync<T>(
		string service,
		string operation,
		Action<object> configure,
		Func<object, IEnumerable<T>> parser,
		CancellationToken cancellationToken)
	{
		var bridge = EnsureConnected();
		cancellationToken.ThrowIfCancellationRequested();
		var request = bridge.CreateRequest(service, operation);
		configure(request);

		var correlationId = GetNextCorrelationId();
		var sink = new RequestSink<T>(parser);
		if (!_requests.TryAdd(correlationId, sink))
			throw new InvalidOperationException("The Bloomberg request correlation identifier is already in use.");

		try
		{
			bridge.SendRequest(request, correlationId);
		}
		catch
		{
			_requests.TryRemove(correlationId, out _);
			throw;
		}

		var registration = cancellationToken.Register(() =>
		{
			if (_requests.TryRemove(correlationId, out var pending))
				pending.Cancel(cancellationToken);
		});
		return AwaitRequestAsync(sink.Task, registration, correlationId);
	}

	private async Task<T[]> AwaitRequestAsync<T>(Task<T[]> task, CancellationTokenRegistration registration, long correlationId)
	{
		try
		{
			return await task;
		}
		finally
		{
			registration.Dispose();
			_requests.TryRemove(correlationId, out _);
		}
	}

	private BloombergSdkBridge EnsureConnected()
	{
		if (_bridge == null || Interlocked.CompareExchange(ref _isDisconnecting, 0, 0) != 0)
			throw new InvalidOperationException("The Bloomberg session is not connected.");
		return _bridge;
	}

	private void EnsureEmsx()
	{
		EnsureConnected();
		if (!_isEmsxEnabled)
			throw new InvalidOperationException("Bloomberg EMSX is disabled for this connection.");
	}

	private long GetNextCorrelationId()
		=> Interlocked.Increment(ref _nextCorrelationId);

	private void SetOptional(object request, string name, string value)
	{
		if (!value.IsEmpty())
			_bridge.Set(request, name, value);
	}

	private void SetOptional(object request, string name, decimal? value)
	{
		if (value != null)
			_bridge.Set(request, name, value.Value);
	}

	private void SetOptionalDate(object request, string name, DateTime? value)
	{
		if (value != null)
			_bridge.Set(request, name, EnsureUtc(value.Value).ToString("yyyyMMdd", CultureInfo.InvariantCulture));
	}

	private static BloombergEmsxResult RequireSingleResult(BloombergEmsxResult[] response, string operation)
	{
		var result = response.FirstOrDefault()
			?? throw new InvalidOperationException($"Bloomberg EMSX returned no response for {operation}.");
		if (!result.Error.IsEmpty())
			throw new InvalidOperationException(result.Error);
		return result;
	}

	private object GetRequiredElement(object value, string name)
	{
		if (!_bridge.HasElement(value, name))
			throw new InvalidOperationException($"Bloomberg response has no '{name}' element.");
		return _bridge.GetElement(value, name);
	}

	private void ThrowResponseError(object message)
	{
		var error = ReadElementError(message, "responseError");
		if (!error.IsEmpty())
			throw new InvalidOperationException(error);
	}

	private string ReadElementError(object value, string name)
	{
		if (value == null || !_bridge.HasElement(value, name))
			return null;
		var error = _bridge.GetElement(value, name);
		return _bridge.TryGetString(error, "message")
			?? _bridge.TryGetString(error, "description")
			?? _bridge.GetText(error);
	}

	private Exception CreateStatusError(object message, string messageType)
	{
		var description = ReadElementError(message, "reason")
			?? _bridge.TryGetString(message, "description")
			?? _bridge.GetText(message);
		return new InvalidOperationException($"Bloomberg {messageType}: {description}");
	}

	private string GetString(object value, string name)
		=> value == null ? null : _bridge.TryGetString(value, name);

	private decimal? GetDecimal(object value, params string[] names)
	{
		if (value == null)
			return null;
		foreach (var name in names)
		{
			var result = _bridge.TryGetDecimal(value, name);
			if (result != null)
				return result;
		}
		return null;
	}

	private DateTime? GetDateTime(object value, string name)
		=> value == null ? null : _bridge.TryGetDateTime(value, name);

	private DateTime ReadMarketTime(object message)
	{
		var time = _bridge.TryGetDateTime(message, "TRADE_UPDATE_STAMP_RT");
		if (time == null)
			return DateTime.UtcNow;
		if (time.Value.Year <= 1)
			return DateTime.UtcNow.Date + time.Value.TimeOfDay;
		return EnsureUtc(time.Value);
	}

	private DateTime ParseEmsxTime(object message)
	{
		var date = _bridge.TryGetInt64(message, "EMSX_DATE");
		var timestamp = _bridge.TryGetInt64(message, "EMSX_TIME_STAMP");
		if (date == null || timestamp == null)
			return DateTime.UtcNow;

		var dateText = date.Value.ToString("D8", CultureInfo.InvariantCulture);
		var timeText = timestamp.Value.ToString(CultureInfo.InvariantCulture).PadLeft(6, '0');
		if (timeText.Length > 6)
			timeText = timeText[..6];
		return DateTime.TryParseExact(
			dateText + timeText,
			"yyyyMMddHHmmss",
			CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var result)
			? result
			: DateTime.UtcNow;
	}

	private static DateTime EnsureUtc(DateTime time)
		=> time.Kind switch
		{
			DateTimeKind.Utc => time,
			DateTimeKind.Local => time.ToUniversalTime(),
			_ => DateTime.SpecifyKind(time, DateTimeKind.Utc),
		};

	private void FailPending(Exception error)
	{
		foreach (var request in _requests.ToArray())
		{
			if (_requests.TryRemove(request.Key, out var sink))
				sink.Fail(error);
		}
	}

	private ValueTask RaiseErrorAsync(Exception error, CancellationToken cancellationToken)
		=> Error?.Invoke(error, cancellationToken) ?? default;

	private ValueTask RaiseConnectionLostAsync(Exception error)
		=> ConnectionLost?.Invoke(error, CancellationToken.None) ?? default;
}
