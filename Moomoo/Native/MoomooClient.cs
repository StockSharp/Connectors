namespace StockSharp.Moomoo.Native;

sealed class MoomooClient : Disposable
{
	private enum PushTypes
	{
		BasicQuote,
		OrderBook,
		Ticker,
		Candle,
		Order,
		Fill,
		Error,
	}

	private readonly record struct PushMessage(PushTypes Type, object Value);

	private static readonly Lock _apiLock = new();
	private static int _apiRefCount;

	private readonly MMAPI_Qot _quote = new();
	private readonly MMAPI_Trd _trade = new();
	private readonly Lock _pendingLock = new();
	private readonly Dictionary<uint, TaskCompletionSource<object>> _pending = [];
	private readonly Channel<PushMessage> _pushes = Channel.CreateUnbounded<PushMessage>(new() { SingleReader = true });
	private readonly CancellationTokenSource _pushCts = new();
	private readonly Task _pushTask;
	private TaskCompletionSource<bool> _quoteConnected;
	private TaskCompletionSource<bool> _tradeConnected;

	public MoomooClient()
	{
		using (_apiLock.EnterScope())
		{
			if (_apiRefCount++ == 0)
				MMAPI.Init();
		}

		_quote.SetConnCallback(CreateProxy<MMSPI_Conn>(ProcessConnectionCallback));
		_quote.SetQotCallback(CreateProxy<MMSPI_Qot>(ProcessQuoteCallback));
		_trade.SetConnCallback(CreateProxy<MMSPI_Conn>(ProcessConnectionCallback));
		_trade.SetTrdCallback(CreateProxy<MMSPI_Trd>(ProcessTradeCallback));
		_quote.SetClientInfo("StockSharp", 1);
		_trade.SetClientInfo("StockSharp", 1);
		_pushTask = ProcessPushes(_pushCts.Token);
	}

	public Func<QotUpdateBasicQot.Response, CancellationToken, ValueTask> BasicQuoteHandler { get; set; }
	public Func<QotUpdateOrderBook.Response, CancellationToken, ValueTask> OrderBookHandler { get; set; }
	public Func<QotUpdateTicker.Response, CancellationToken, ValueTask> TickerHandler { get; set; }
	public Func<QotUpdateKL.Response, CancellationToken, ValueTask> CandleHandler { get; set; }
	public Func<TrdUpdateOrder.Response, CancellationToken, ValueTask> OrderHandler { get; set; }
	public Func<TrdUpdateOrderFill.Response, CancellationToken, ValueTask> FillHandler { get; set; }
	public Func<Exception, CancellationToken, ValueTask> ErrorHandler { get; set; }

	public async Task Connect(string host, int port, CancellationToken cancellationToken)
	{
		_quoteConnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
		_tradeConnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!_quote.InitConnect(host, checked((ushort)port), false))
			throw new InvalidOperationException("Moomoo quote connection could not be started.");
		if (!_trade.InitConnect(host, checked((ushort)port), false))
			throw new InvalidOperationException("Moomoo trade connection could not be started.");

		await _quoteConnected.Task.WaitAsync(cancellationToken);
		await _tradeConnected.Task.WaitAsync(cancellationToken);
	}

	public async Task UnlockTrade(SecureString password, CancellationToken cancellationToken)
	{
		if (password.IsEmpty())
			return;
		var bytes = MD5.HashData(Encoding.UTF8.GetBytes(password.UnSecure()));
		var hash = Convert.ToHexString(bytes).ToLowerInvariant();
		var c2s = TrdUnlockTrade.C2S.CreateBuilder().SetUnlock(true).SetPwdMD5(hash).Build();
		var response = await Send<TrdUnlockTrade.Response>(() => _trade.UnlockTrade(TrdUnlockTrade.Request.CreateBuilder().SetC2S(c2s).Build()), cancellationToken);
		Validate(response.RetType, response.RetMsg);
	}

	public async Task<QotCommon.SecurityStaticInfo[]> GetSecurities(QotCommon.SecurityType securityType, CancellationToken cancellationToken)
	{
		var c2s = QotGetStaticInfo.C2S.CreateBuilder()
			.SetMarket((int)QotCommon.QotMarket.QotMarket_US_Security)
			.SetSecType((int)securityType)
			.Build();
		var response = await Send<QotGetStaticInfo.Response>(() => _quote.GetStaticInfo(QotGetStaticInfo.Request.CreateBuilder().SetC2S(c2s).Build()), cancellationToken);
		Validate(response.RetType, response.RetMsg);
		return response.HasS2C ? response.S2C.StaticInfoListList.ToArray() : [];
	}

	public async Task Subscribe(string code, IEnumerable<QotCommon.SubType> subTypes, bool isSubscribe, bool isExtended, CancellationToken cancellationToken)
	{
		var c2s = QotSub.C2S.CreateBuilder()
			.AddSecurityList(CreateSecurity(code))
			.AddRangeSubTypeList(subTypes.Select(t => (int)t))
			.SetIsSubOrUnSub(isSubscribe)
			.SetIsRegOrUnRegPush(isSubscribe)
			.SetIsFirstPush(isSubscribe)
			.SetExtendedTime(isExtended)
			.SetSession((int)Common.Session.Session_ALL)
			.Build();
		var response = await Send<QotSub.Response>(() => _quote.Sub(QotSub.Request.CreateBuilder().SetC2S(c2s).Build()), cancellationToken);
		Validate(response.RetType, response.RetMsg);
	}

	public async Task<QotCommon.KLine[]> GetCandles(string code, QotCommon.KLType candleType, DateTime from, DateTime to, bool isExtended, CancellationToken cancellationToken)
	{
		var result = new List<QotCommon.KLine>();
		ByteString nextKey = null;
		do
		{
			var builder = QotRequestHistoryKL.C2S.CreateBuilder()
				.SetRehabType((int)QotCommon.RehabType.RehabType_Forward)
				.SetKlType((int)candleType)
				.SetSecurity(CreateSecurity(code))
				.SetBeginTime(from.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
				.SetEndTime(to.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
				.SetMaxAckKLNum(1000)
				.SetExtendedTime(isExtended)
				.SetSession((int)Common.Session.Session_ALL);
			if (nextKey is not null)
				builder.SetNextReqKey(nextKey);

			var response = await Send<QotRequestHistoryKL.Response>(() => _quote.RequestHistoryKL(QotRequestHistoryKL.Request.CreateBuilder().SetC2S(builder.Build()).Build()), cancellationToken);
			Validate(response.RetType, response.RetMsg);
			if (!response.HasS2C)
				break;
			result.AddRange(response.S2C.KlListList);
			nextKey = response.S2C.HasNextReqKey && response.S2C.NextReqKey.Length > 0 ? response.S2C.NextReqKey : null;
		}
		while (nextKey is not null);
		return [.. result];
	}

	public async Task<TrdCommon.TrdAcc[]> GetAccounts(CancellationToken cancellationToken)
	{
		var c2s = TrdGetAccList.C2S.CreateBuilder().SetTrdCategory((int)TrdCommon.TrdCategory.TrdCategory_Security).SetNeedGeneralSecAccount(true).Build();
		var response = await Send<TrdGetAccList.Response>(() => _trade.GetAccList(TrdGetAccList.Request.CreateBuilder().SetC2S(c2s).Build()), cancellationToken);
		Validate(response.RetType, response.RetMsg);
		return response.HasS2C ? response.S2C.AccListList.ToArray() : [];
	}

	public async Task SubscribeAccounts(IEnumerable<ulong> accountIds, CancellationToken cancellationToken)
	{
		var c2s = TrdSubAccPush.C2S.CreateBuilder().AddRangeAccIDList(accountIds).Build();
		var response = await Send<TrdSubAccPush.Response>(() => _trade.SubAccPush(TrdSubAccPush.Request.CreateBuilder().SetC2S(c2s).Build()), cancellationToken);
		Validate(response.RetType, response.RetMsg);
	}

	public async Task<TrdCommon.Funds> GetFunds(TrdCommon.TrdAcc account, CancellationToken cancellationToken)
	{
		var c2s = TrdGetFunds.C2S.CreateBuilder().SetHeader(CreateHeader(account)).SetRefreshCache(true).Build();
		var response = await Send<TrdGetFunds.Response>(() => _trade.GetFunds(TrdGetFunds.Request.CreateBuilder().SetC2S(c2s).Build()), cancellationToken);
		Validate(response.RetType, response.RetMsg);
		return response.HasS2C && response.S2C.HasFunds ? response.S2C.Funds : null;
	}

	public async Task<TrdCommon.Position[]> GetPositions(TrdCommon.TrdAcc account, CancellationToken cancellationToken)
	{
		var c2s = TrdGetPositionList.C2S.CreateBuilder().SetHeader(CreateHeader(account)).SetRefreshCache(true).Build();
		var response = await Send<TrdGetPositionList.Response>(() => _trade.GetPositionList(TrdGetPositionList.Request.CreateBuilder().SetC2S(c2s).Build()), cancellationToken);
		Validate(response.RetType, response.RetMsg);
		return response.HasS2C ? response.S2C.PositionListList.ToArray() : [];
	}

	public async Task<TrdCommon.Order[]> GetOrders(TrdCommon.TrdAcc account, bool isHistory, DateTime? from, DateTime? to, CancellationToken cancellationToken)
	{
		if (!isHistory)
		{
			var c2s = TrdGetOrderList.C2S.CreateBuilder().SetHeader(CreateHeader(account)).SetRefreshCache(true).Build();
			var response = await Send<TrdGetOrderList.Response>(() => _trade.GetOrderList(TrdGetOrderList.Request.CreateBuilder().SetC2S(c2s).Build()), cancellationToken);
			Validate(response.RetType, response.RetMsg);
			return response.HasS2C ? response.S2C.OrderListList.ToArray() : [];
		}

		var history = TrdGetHistoryOrderList.C2S.CreateBuilder().SetHeader(CreateHeader(account));
		var filter = CreateFilter(from, to);
		if (filter is not null)
			history.SetFilterConditions(filter);
		var historyResponse = await Send<TrdGetHistoryOrderList.Response>(() => _trade.GetHistoryOrderList(TrdGetHistoryOrderList.Request.CreateBuilder().SetC2S(history.Build()).Build()), cancellationToken);
		Validate(historyResponse.RetType, historyResponse.RetMsg);
		return historyResponse.HasS2C ? historyResponse.S2C.OrderListList.ToArray() : [];
	}

	public async Task<TrdCommon.OrderFill[]> GetFills(TrdCommon.TrdAcc account, bool isHistory, DateTime? from, DateTime? to, CancellationToken cancellationToken)
	{
		if (!isHistory)
		{
			var c2s = TrdGetOrderFillList.C2S.CreateBuilder().SetHeader(CreateHeader(account)).SetRefreshCache(true).Build();
			var response = await Send<TrdGetOrderFillList.Response>(() => _trade.GetOrderFillList(TrdGetOrderFillList.Request.CreateBuilder().SetC2S(c2s).Build()), cancellationToken);
			Validate(response.RetType, response.RetMsg);
			return response.HasS2C ? response.S2C.OrderFillListList.ToArray() : [];
		}

		var history = TrdGetHistoryOrderFillList.C2S.CreateBuilder().SetHeader(CreateHeader(account));
		var filter = CreateFilter(from, to);
		if (filter is not null)
			history.SetFilterConditions(filter);
		var historyResponse = await Send<TrdGetHistoryOrderFillList.Response>(() => _trade.GetHistoryOrderFillList(TrdGetHistoryOrderFillList.Request.CreateBuilder().SetC2S(history.Build()).Build()), cancellationToken);
		Validate(historyResponse.RetType, historyResponse.RetMsg);
		return historyResponse.HasS2C ? historyResponse.S2C.OrderFillListList.ToArray() : [];
	}

	public async Task<string> PlaceOrder(TrdCommon.TrdAcc account, TrdCommon.TrdSide side, TrdCommon.OrderType orderType, string code, decimal quantity, decimal price, decimal? stopPrice, TrdCommon.TimeInForce timeInForce, DateTime? expiry, Common.Session session, string remark, CancellationToken cancellationToken)
	{
		var builder = TrdPlaceOrder.C2S.CreateBuilder()
			.SetPacketID(_trade.NextPacketID())
			.SetHeader(CreateHeader(account))
			.SetTrdSide((int)side)
			.SetOrderType((int)orderType)
			.SetCode(code)
			.SetQty((double)quantity)
			.SetPrice((double)price)
			.SetSecMarket((int)TrdCommon.TrdSecMarket.TrdSecMarket_US)
			.SetRemark(remark)
			.SetTimeInForce((int)timeInForce)
			.SetFillOutsideRTH(session != Common.Session.Session_RTH)
			.SetSession((int)session);
		if (stopPrice is decimal stop)
			builder.SetAuxPrice((double)stop);
		if (expiry is DateTime expiryDate)
			builder.SetExpireTime(expiryDate.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

		var response = await Send<TrdPlaceOrder.Response>(() => _trade.PlaceOrder(TrdPlaceOrder.Request.CreateBuilder().SetC2S(builder.Build()).Build()), cancellationToken);
		Validate(response.RetType, response.RetMsg);
		if (!response.HasS2C)
			throw new InvalidOperationException("Moomoo did not return an order identifier.");
		return response.S2C.OrderIDEx.IsEmpty(response.S2C.OrderID.ToString(CultureInfo.InvariantCulture));
	}

	public async Task<string> ModifyOrder(TrdCommon.TrdAcc account, string orderId, TrdCommon.ModifyOrderOp operation, decimal quantity, decimal price, decimal? stopPrice, CancellationToken cancellationToken)
	{
		var builder = TrdModifyOrder.C2S.CreateBuilder()
			.SetPacketID(_trade.NextPacketID())
			.SetHeader(CreateHeader(account))
			.SetModifyOrderOp((int)operation)
			.SetOrderIDEx(orderId)
			.SetTrdMarket((int)TrdCommon.TrdMarket.TrdMarket_US)
			.SetQty((double)quantity)
			.SetPrice((double)price);
		if (stopPrice is decimal stop)
			builder.SetAuxPrice((double)stop);
		var response = await Send<TrdModifyOrder.Response>(() => _trade.ModifyOrder(TrdModifyOrder.Request.CreateBuilder().SetC2S(builder.Build()).Build()), cancellationToken);
		Validate(response.RetType, response.RetMsg);
		if (!response.HasS2C)
			return orderId;
		return response.S2C.OrderIDEx.IsEmpty(response.S2C.OrderID.ToString(CultureInfo.InvariantCulture));
	}

	private static QotCommon.Security CreateSecurity(string code)
		=> QotCommon.Security.CreateBuilder().SetMarket((int)QotCommon.QotMarket.QotMarket_US_Security).SetCode(code).Build();

	private static TrdCommon.TrdHeader CreateHeader(TrdCommon.TrdAcc account)
		=> TrdCommon.TrdHeader.CreateBuilder()
			.SetTrdEnv(account.TrdEnv)
			.SetAccID(account.AccID)
			.SetTrdMarket((int)TrdCommon.TrdMarket.TrdMarket_US)
			.Build();

	private static TrdCommon.TrdFilterConditions CreateFilter(DateTime? from, DateTime? to)
	{
		if (from is null && to is null)
			return null;
		var builder = TrdCommon.TrdFilterConditions.CreateBuilder().SetFilterMarket((int)TrdCommon.TrdMarket.TrdMarket_US);
		if (from is DateTime start)
			builder.SetBeginTime(start.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
		if (to is DateTime end)
			builder.SetEndTime(end.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
		return builder.Build();
	}

	private async Task<TResponse> Send<TResponse>(Func<uint> sender, CancellationToken cancellationToken)
	{
		var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
		uint serial;
		using (_pendingLock.EnterScope())
		{
			serial = sender();
			if (serial == 0)
				throw new InvalidOperationException("Moomoo OpenD rejected the request before sending it.");
			_pending.Add(serial, completion);
		}

		using var registration = cancellationToken.Register(() => CancelRequest(serial, completion, cancellationToken));
		return (TResponse)await completion.Task;
	}

	private void CancelRequest(uint serial, TaskCompletionSource<object> completion, CancellationToken cancellationToken)
	{
		using (_pendingLock.EnterScope())
			_pending.Remove(serial);
		completion.TrySetCanceled(cancellationToken);
	}

	private void CompleteRequest(uint serial, object response)
	{
		TaskCompletionSource<object> completion;
		using (_pendingLock.EnterScope())
		{
			if (!_pending.Remove(serial, out completion))
				return;
		}
		completion.TrySetResult(response);
	}

	private void ProcessConnectionCallback(string method, object[] args)
	{
		var connection = (MMAPI_Conn)args[0];
		var errorCode = (long)args[1];
		if (method == nameof(MMSPI_Conn.OnInitConnect))
		{
			var completion = ReferenceEquals(connection, _quote) ? _quoteConnected : _tradeConnected;
			if (errorCode == 0)
				completion.TrySetResult(true);
			else
				completion.TrySetException(new InvalidOperationException(((string)args[2]).IsEmpty($"Moomoo OpenD connection failed with code {errorCode}.")));
		}
		else if (method == nameof(MMSPI_Conn.OnDisconnect))
		{
			var error = new InvalidOperationException($"Moomoo OpenD connection closed with code {errorCode}.");
			var completion = ReferenceEquals(connection, _quote) ? _quoteConnected : _tradeConnected;
			if (completion is null || !completion.TrySetException(error))
				_pushes.Writer.TryWrite(new(PushTypes.Error, error));
		}
	}

	private void ProcessQuoteCallback(string method, object[] args)
	{
		var serial = (uint)args[1];
		var response = args[2];
		if (method == nameof(MMSPI_Qot.OnReply_UpdateBasicQot))
			_pushes.Writer.TryWrite(new(PushTypes.BasicQuote, response));
		else if (method == nameof(MMSPI_Qot.OnReply_UpdateOrderBook))
			_pushes.Writer.TryWrite(new(PushTypes.OrderBook, response));
		else if (method == nameof(MMSPI_Qot.OnReply_UpdateTicker))
			_pushes.Writer.TryWrite(new(PushTypes.Ticker, response));
		else if (method == nameof(MMSPI_Qot.OnReply_UpdateKL))
			_pushes.Writer.TryWrite(new(PushTypes.Candle, response));
		else
			CompleteRequest(serial, response);
	}

	private void ProcessTradeCallback(string method, object[] args)
	{
		var serial = (uint)args[1];
		var response = args[2];
		if (method == nameof(MMSPI_Trd.OnReply_UpdateOrder))
			_pushes.Writer.TryWrite(new(PushTypes.Order, response));
		else if (method == nameof(MMSPI_Trd.OnReply_UpdateOrderFill))
			_pushes.Writer.TryWrite(new(PushTypes.Fill, response));
		else
			CompleteRequest(serial, response);
	}

	private async Task ProcessPushes(CancellationToken cancellationToken)
	{
		await foreach (var push in _pushes.Reader.ReadAllAsync(cancellationToken))
		{
			try
			{
				switch (push.Type)
				{
					case PushTypes.BasicQuote when BasicQuoteHandler is not null:
						await BasicQuoteHandler((QotUpdateBasicQot.Response)push.Value, cancellationToken);
						break;
					case PushTypes.OrderBook when OrderBookHandler is not null:
						await OrderBookHandler((QotUpdateOrderBook.Response)push.Value, cancellationToken);
						break;
					case PushTypes.Ticker when TickerHandler is not null:
						await TickerHandler((QotUpdateTicker.Response)push.Value, cancellationToken);
						break;
					case PushTypes.Candle when CandleHandler is not null:
						await CandleHandler((QotUpdateKL.Response)push.Value, cancellationToken);
						break;
					case PushTypes.Order when OrderHandler is not null:
						await OrderHandler((TrdUpdateOrder.Response)push.Value, cancellationToken);
						break;
					case PushTypes.Fill when FillHandler is not null:
						await FillHandler((TrdUpdateOrderFill.Response)push.Value, cancellationToken);
						break;
					case PushTypes.Error when ErrorHandler is not null:
						await ErrorHandler((Exception)push.Value, cancellationToken);
						break;
				}
			}
			catch (Exception ex) when (ex is not OperationCanceledException && ErrorHandler is not null)
			{
				await ErrorHandler(ex, cancellationToken);
			}
		}
	}

	private static T CreateProxy<T>(Action<string, object[]> handler)
		where T : class
	{
		var proxy = DispatchProxy.Create<T, MoomooSpiProxy>();
		((MoomooSpiProxy)(object)proxy).Handler = handler;
		return proxy;
	}

	private static void Validate(int result, string message)
	{
		if (result != 0)
			throw new InvalidOperationException(message.IsEmpty($"Moomoo OpenD returned error {result}."));
	}

	protected override void DisposeManaged()
	{
		_pushes.Writer.TryComplete();
		_pushCts.Cancel();
		try
		{
			_pushTask.GetAwaiter().GetResult();
		}
		catch (OperationCanceledException)
		{
		}
		_quote.Close();
		_trade.Close();
		_quote.Dispose();
		_trade.Dispose();
		_pushCts.Dispose();
		using (_apiLock.EnterScope())
		{
			if (--_apiRefCount == 0)
				MMAPI.UnInit();
		}
		base.DisposeManaged();
	}
}

class MoomooSpiProxy : DispatchProxy
{
	public Action<string, object[]> Handler { get; set; }

	protected override object Invoke(MethodInfo targetMethod, object[] args)
	{
		Handler(targetMethod.Name, args);
		return null;
	}
}
