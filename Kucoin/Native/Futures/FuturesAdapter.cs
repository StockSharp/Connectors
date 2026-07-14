namespace StockSharp.Kucoin.Native.Futures;

using System.Runtime.CompilerServices;

using StockSharp.Kucoin.Native.Futures.Model;

class FuturesAdapter : BaseNativeAdapter
{
	private HttpClient _httpClient;
	private PublicSocketClient _publicClient;
	private PrivateSocketClient _privateClient;
	private ConnectionStateTracker _tracker;

	private readonly SynchronizedDictionary<string, long> _level2 = [];
	//private readonly SynchronizedDictionary<(SecurityId secId, TimeSpan tf), long> _candleTransIds = new();

	private long? _positionsSubId;

	protected string PortfolioName => nameof(Kucoin) + $"_{nameof(Futures)}_" + Adapter.Key.ToId();

	public FuturesAdapter(KucoinMessageAdapter adapter, Func<Message, CancellationToken, ValueTask> outMessage)
		: base(adapter, BoardCodes.KucoinFT, outMessage)
    {
	}

	private void SubscribePublicClient()
	{
		_publicClient.StateChanged += SendOutConnectionStateAsync;
		_publicClient.Error += OnError;
		_publicClient.TickerChanged += SessionOnTickerChanged;
		_publicClient.NewLevel2 += SessionOnNewLevel2;
		_publicClient.NewLevel2Snapshot += SessionOnNewLevel2Snapshot;
		_publicClient.NewMatch += SessionOnNewMatch;
		//_publicClient.NewCandle += SessionOnNewCandle;
	}

	private void UnsubscribePublicClient()
	{
		_publicClient.StateChanged -= SendOutConnectionStateAsync;
		_publicClient.Error -= OnError;
		_publicClient.TickerChanged -= SessionOnTickerChanged;
		_publicClient.NewLevel2 -= SessionOnNewLevel2;
		_publicClient.NewLevel2Snapshot -= SessionOnNewLevel2Snapshot;
		_publicClient.NewMatch -= SessionOnNewMatch;
		//_publicClient.NewCandle -= SessionOnNewCandle;
	}

	private void SubscribePrivateClient()
	{
		_privateClient.OrderChanged += SessionOnPusherOrderChanged;
		_privateClient.StopOrderChanged += SessionOnPusherStopOrderChanged;
		_privateClient.BalanceChanged += SessionOnPusherBalanceChanged;
		_privateClient.PositionChanged += SessionOnPositionChanged;
	}

	private void UnsubscribePrivateClient()
	{
		_privateClient.OrderChanged -= SessionOnPusherOrderChanged;
		_privateClient.StopOrderChanged -= SessionOnPusherStopOrderChanged;
		_privateClient.BalanceChanged -= SessionOnPusherBalanceChanged;
		_privateClient.PositionChanged -= SessionOnPositionChanged;
	}

	private ValueTask OnError(Exception error, CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	protected override void DisposeManaged()
	{
		if (_httpClient != null)
		{
			try
			{
				_httpClient.Dispose();
			}
			catch (Exception ex)
			{
				Adapter.AddErrorLog(ex);
			}

			_httpClient = null;
		}

		if (_publicClient != null)
		{
			try
			{
				UnsubscribePublicClient();
				_publicClient.Disconnect();
			}
			catch (Exception ex)
			{
				Adapter.AddErrorLog(ex);
			}

			_publicClient = null;
		}

		if (_privateClient != null)
		{
			try
			{
				UnsubscribePrivateClient();
				_privateClient.Disconnect();
			}
			catch (Exception ex)
			{
				Adapter.AddErrorLog(ex);
			}

			_privateClient = null;
		}

		if (_tracker is not null)
		{
			_tracker.StateChanged -= SendOutConnectionStateAsync;
			_tracker.Dispose();
			_tracker = null;
		}

		_level2.Clear();
		//_candleTransIds.Clear();

		_positionsSubId = default;

		base.DisposeManaged();
	}

	public override ValueTask TimeAsync(CancellationToken cancellationToken)
	{
		return new[]
		{
			_publicClient.Ping(cancellationToken),
			_privateClient.Ping(cancellationToken)
		}.WhenAll();
	}

	public override async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_httpClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		if (_publicClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_httpClient = new(Adapter.IsDemo, Adapter.FuturesAddress, new(Adapter.Key, Adapter.Secret, Adapter.Passphrase)) { Parent = Adapter };

		var publicInfo = await _httpClient.GetPublicInfo(cancellationToken);

		var publicEndpoint = publicInfo.InstanceServers.First(s => s.Protocol == "websocket");
		_publicClient = new(publicEndpoint.Endpoint, publicInfo.Token, TimeSpan.FromMilliseconds(publicEndpoint.PingInterval), Adapter.ReConnectionSettings.ReAttemptCount, Adapter.ReConnectionSettings.WorkingTime) { Parent = Adapter };
		SubscribePublicClient();

		_tracker = new();
		_tracker.StateChanged += SendOutConnectionStateAsync;

		_tracker.Add(_publicClient);

		if (Adapter.IsTransactional())
		{
			var privateInfo = await _httpClient.GetPrivateInfo(cancellationToken);

			var privateEndpoint = privateInfo.InstanceServers.First(s => s.Protocol == "websocket");
			_privateClient = new(privateEndpoint.Endpoint, privateInfo.Token, TimeSpan.FromMilliseconds(privateEndpoint.PingInterval), Adapter.ReConnectionSettings.ReAttemptCount, Adapter.ReConnectionSettings.WorkingTime) { Parent = Adapter };
			SubscribePrivateClient();

			_tracker.Add(_privateClient);
		}

		await _tracker.ConnectAsync(cancellationToken);
	}

	public override void Disconnect() => _tracker.Disconnect();

	public override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var symbol = GetSymbol(regMsg.SecurityId);
		var condition = (KucoinOrderCondition)regMsg.Condition;
		var transId = regMsg.TransactionId;

		switch (regMsg.OrderType)
		{
			case null:
			case OrderTypes.Limit:
			case OrderTypes.Market:
				break;
			case OrderTypes.Conditional:
			{
				if (!condition.IsWithdraw)
					break;

				throw new NotSupportedException();
			}
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, transId));
		}

		var price = regMsg.OrderType == OrderTypes.Market ? (decimal?)null : regMsg.Price;

		var orderId = await _httpClient.RegisterOrder(transId.To<string>(), regMsg.Side.ToNative(),
			symbol, regMsg.OrderType.ToNative(), regMsg.Comment, price, regMsg.Volume,
			regMsg.TimeInForce.ToNative(regMsg.TillDate, out var cancelAfter), cancelAfter,
			regMsg.PostOnly, regMsg.VisibleVolume, regMsg.MarginMode, cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OrderStringId = orderId,
			ServerTime = CurrentTime,
			OriginalTransactionId = transId,
			OrderState = OrderStates.Active,
			HasOrderInfo = true,
		}, cancellationToken);
	}

	public override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		await _httpClient.CancelOrder(cancelMsg.OriginalTransactionId.To<string>(), cancellationToken);
	}

	public override async ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		if (cancelMsg.OrderType is null || cancelMsg.OrderType != OrderTypes.Conditional)
			await _httpClient.CancelAllOrders(cancelMsg.SecurityId == default ? null : GetSymbol(cancelMsg.SecurityId), cancellationToken);

		if (cancelMsg.OrderType is null || cancelMsg.OrderType == OrderTypes.Conditional)
			await _httpClient.CancelAllStopOrders(cancelMsg.SecurityId == default ? null : GetSymbol(cancelMsg.SecurityId), cancellationToken);
	}

	public override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		var transId = statusMsg.TransactionId;

		if (!statusMsg.IsSubscribe)
		{
			await _privateClient.UnSubscribeOrders(statusMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		if (statusMsg.OrderType is null || statusMsg.OrderType != OrderTypes.Conditional)
		{
			var orders = await _httpClient.GetOrders(false, cancellationToken);

			foreach (var order in orders)
			{
				if (!long.TryParse(order.ClientOid, out var orderTransId))
					continue;

				await ProcessOrderAsync(order, orderTransId, transId, cancellationToken);
			}
		}

		if (statusMsg.OrderType is null || statusMsg.OrderType == OrderTypes.Conditional)
		{
			var orders = await _httpClient.GetOrders(true, cancellationToken);

			foreach (var order in orders)
			{
				if (!long.TryParse(order.ClientOid, out var orderTransId))
					continue;

				await ProcessOrderAsync(order, orderTransId, transId, cancellationToken);
			}
		}

		if (!statusMsg.IsHistoryOnly())
			await _privateClient.SubscribeOrders(transId, cancellationToken);
	}

	public override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		var transId = lookupMsg.TransactionId;

		if (!lookupMsg.IsSubscribe)
		{
			await _privateClient.UnSubscribeBalance(lookupMsg.OriginalTransactionId, cancellationToken);

			if (_positionsSubId is not null)
			{
				await _privateClient.UnSubscribePositions(_positionsSubId.Value, cancellationToken);
				_positionsSubId = default;
			}

			return;
		}

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = PortfolioName,
			BoardCode = BoardCode,
			OriginalTransactionId = transId
		}, cancellationToken);

		var positions = await _httpClient.GetPositions(default, cancellationToken);

		foreach (var position in positions)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				SecurityId = GetSecId(position.Symbol),
				PortfolioName = PortfolioName,
				ServerTime = position.CurrentTimestamp,
				OriginalTransactionId = transId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, position.CurrentQty?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.CurrentPrice, position.CurrentCost?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.AveragePrice, position.AvgEntryPrice?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.RealizedPnL, position.RealisedPnl?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealisedPnl?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.LiquidationPrice, position.LiquidationPrice?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.Leverage, position.RealLeverage?.ToDecimal(), true)
			, cancellationToken);
		}

		if (!lookupMsg.IsHistoryOnly())
		{
			await _privateClient.SubscribeBalance(transId, cancellationToken);

			_positionsSubId = Adapter.TransactionIdGenerator.GetNextId();
			await _privateClient.SubscribePositions(_positionsSubId.Value, cancellationToken);
		}
	}

	public override async IAsyncEnumerable<SecurityMessage> SecurityLookupAsync(SecurityLookupMessage lookupMsg, [EnumeratorCancellation]CancellationToken cancellationToken)
	{
		var contracts = await _httpClient.GetContracts(cancellationToken);

		foreach (var contract in contracts)
		{
			yield return new SecurityMessage
			{
				SecurityId = GetSecId(contract.Symbol),
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityType = SecurityTypes.Future,
				PriceStep = contract.TickSize?.ToDecimal(),
				VolumeStep = contract.LotSize?.ToDecimal(),
				//Multiplier = contract.Multiplier?.ToDecimal(),
				MaxVolume = contract.MaxOrderQty?.ToDecimal(),
			}.TryFillUnderlyingId(contract.BaseCurrency);
		}
	}

	public override async ValueTask TFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var symbol = GetSymbol(mdMsg.SecurityId);
		var transId = mdMsg.TransactionId;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		var tf = mdMsg.GetTimeFrame();

		if (!mdMsg.IsSubscribe)
			return;

		if (mdMsg.From is DateTime from)
		{
			var now = DateTime.UtcNow;
			var to = mdMsg.To ?? now;
			var left = mdMsg.Count ?? long.MaxValue;

			var candles = await _httpClient.GetCandles(symbol, (int)tf.TotalMinutes, (long)from.ToUnix(false), (long)to.ToUnix(false), cancellationToken);

			foreach (var candle in candles.OrderBy(c => c.Time))
			{
				var time = candle.Time.FromUnix(false);

				if (time < from)
					continue;

				if (time > to)
					break;

				await ProcessCandleAsync(candle, transId, cancellationToken);

				if (--left <= 0)
					break;
			}
		}

		//if (!mdMsg.IsHistoryOnly())
		//{
		//	_candleTransIds[(secId, tf)] = transId;
		//	await _publicClient.SubscribeCandles(transId, symbol, tfName, cancellationToken);
		//}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	public override async ValueTask TicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var symbol = GetSymbol(mdMsg.SecurityId);

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			await _publicClient.SubscribeMatches(mdMsg.TransactionId, symbol, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _publicClient.UnSubscribeMatches(mdMsg.TransactionId, mdMsg.OriginalTransactionId, symbol, cancellationToken);
	}

	public override async ValueTask Level1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var symbol = GetSymbol(mdMsg.SecurityId);

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			await _publicClient.SubscribeTicker(mdMsg.TransactionId, symbol, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _publicClient.UnSubscribeTicker(mdMsg.TransactionId, mdMsg.OriginalTransactionId, symbol, cancellationToken);
	}

	public override async ValueTask MarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var symbol = GetSymbol(mdMsg.SecurityId);

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			await _publicClient.SubscribeLevel2(mdMsg.TransactionId, symbol, mdMsg.MaxDepth, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _publicClient.UnSubscribeLevel2(mdMsg.TransactionId, mdMsg.OriginalTransactionId, symbol, mdMsg.MaxDepth, cancellationToken);
	}

	private ValueTask ProcessCandleAsync(Ohlc candle, long transactionId, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OpenPrice = candle.Open.ToDecimal() ?? 0,
			ClosePrice = candle.Close.ToDecimal() ?? 0,
			HighPrice = candle.High.ToDecimal() ?? 0,
			LowPrice = candle.Low.ToDecimal() ?? 0,
			TotalVolume = candle.Volume.ToDecimal() ?? 0,
			OpenTime = candle.Time.FromUnix(false),
			State = CandleStates.Finished,
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}

	private ValueTask ProcessLevel1Async(string symbol, Ticker ticker, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = GetSecId(symbol),
			ServerTime = ticker.Time,
		}
		.TryAdd(Level1Fields.LastTradePrice, ticker.Price?.ToDecimal())
		.TryAdd(Level1Fields.LastTradeVolume, ticker.Size?.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, ticker.BestAsk?.ToDecimal())
		.TryAdd(Level1Fields.BestBidPrice, ticker.BestBid?.ToDecimal())
		.TryAdd(Level1Fields.BestAskVolume, ticker.BestAskSize?.ToDecimal())
		.TryAdd(Level1Fields.BestBidVolume, ticker.BestBidSize?.ToDecimal()), cancellationToken);
	}

	private ValueTask SessionOnTickerChanged(string pair, Ticker ticker, CancellationToken cancellationToken)
	{
		return ProcessLevel1Async(pair, ticker, cancellationToken);
	}

	private async ValueTask SessionOnNewLevel2(SocketLevel2 socketLevel2, CancellationToken cancellationToken)
	{
		if (!_level2.TryGetValue(socketLevel2.Symbol, out var sequence))
		{
			var level2 = await _httpClient.GetLevel2(socketLevel2.Symbol, cancellationToken);

			sequence = level2.Sequence;

			_level2.Add(socketLevel2.Symbol, sequence);

			await SendOutMessageAsync(new QuoteChangeMessage
			{
				SecurityId = GetSecId(socketLevel2.Symbol),
				ServerTime = socketLevel2.Time,
				Bids = level2.Bids.Select(e => new QuoteChange((decimal)e.Price, (decimal)e.Size)).ToArray(),
				Asks = level2.Asks.Select(e => new QuoteChange((decimal)e.Price, (decimal)e.Size)).ToArray(),
				State = QuoteChangeStates.SnapshotComplete
			}, cancellationToken);
		}

		if (socketLevel2.SequenceEnd <= sequence)
			return;

		var bids = new List<SocketLevel2Entry>();
		var asks = new List<SocketLevel2Entry>();

		foreach (var q in socketLevel2.Changes.Bids)
		{
			if (q.Sequence <= sequence)
				continue;

			bids.Add(q);
		}

		foreach (var q in socketLevel2.Changes.Asks)
		{
			if (q.Sequence <= sequence)
				continue;

			asks.Add(q);
		}

		_level2[socketLevel2.Symbol] = socketLevel2.SequenceEnd;

		await SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = GetSecId(socketLevel2.Symbol),
			ServerTime = socketLevel2.Time,
			Bids = bids.Select(p => new QuoteChange((decimal)p.Price, (decimal)p.Size)).ToArray(),
			Asks = asks.Select(p => new QuoteChange((decimal)p.Price, (decimal)p.Size)).ToArray(),
			State = QuoteChangeStates.Increment,
		}, cancellationToken);
	}

	private ValueTask SessionOnNewLevel2Snapshot(SocketLevel2Snapshot snapshot, string symbol, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = GetSecId(symbol),
			ServerTime = snapshot.Time,
			Bids = snapshot.Bids.Select(p => new QuoteChange((decimal)p.Price, (decimal)p.Size)).ToArray(),
			Asks = snapshot.Asks.Select(p => new QuoteChange((decimal)p.Price, (decimal)p.Size)).ToArray(),
			State = QuoteChangeStates.SnapshotComplete,
		}, cancellationToken);
	}

	private ValueTask SessionOnNewMatch(SocketMatch match, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = GetSecId(match.Symbol),
			OriginSide = match.Side.ToSide(),
			TradePrice = match.Price?.ToDecimal(),
			TradeVolume = match.Size?.ToDecimal(),
			TradeStringId = match.TradeId,
			ServerTime = match.Time,
		}, cancellationToken);
	}

	//private ValueTask SessionOnNewCandle(string symbol, TimeSpan tf, Ohlc candle, CancellationToken token)
	//{
	//	var secId = symbol.ToStockSharp();

	//	if (_candleTransIds.TryGetValue((secId, tf), out var transId))
	//		ProcessCandle(candle, transId);

	//	return default;
	//}

	private ValueTask ProcessOrderAsync(Order order, long transId, long origTransId, CancellationToken cancellationToken)
	{
		var type = order.Type.ToOrderType();

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = transId != 0 ? order.CreatedAt : (order.UpdatedAt ?? CurrentTime),
			SecurityId = GetSecId(order.Symbol),
			TransactionId = transId,
			OriginalTransactionId = origTransId,
			OrderStringId = order.Id,
			OrderVolume = order.Size?.ToDecimal(),
			Balance = order.GetBalance(),
			VisibleVolume = order.VisibleSize?.ToDecimal(),
			Side = order.Side.ToSide(),
			OrderPrice = order.Price?.ToDecimal() ?? 0,
			PortfolioName = PortfolioName,
			OrderState = order.IsActive == true ? OrderStates.Active : OrderStates.Done,
			Commission = order.Fee?.ToDecimal(),
			CommissionCurrency = order.FeeCurrency,
			TimeInForce = order.TimeInForce.ToTimeInForce(order.CreatedAt, order.CancelAfter, out var tillDate),
			ExpiryDate = tillDate,
			OrderType = type,
			Comment = order.Remark,
			PostOnly = order.PostOnly,
			Condition = order.StopPrice == null ? null : new KucoinOrderCondition { StopPrice = order.StopPrice?.ToDecimal() },
		}, cancellationToken);
	}

	private ValueTask SessionOnPusherOrderChanged(SocketOrder order, CancellationToken cancellationToken)
	{
		if (!long.TryParse(order.ClientOid, out var transId))
			return default;

		var isTaker = order.Liquidity.IsEmpty() ? (bool?)null : order.Liquidity == "taker";

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = order.TimeStamp,
			SecurityId = GetSecId(order.Symbol),
			OriginalTransactionId = transId,
			OrderStringId = order.OrderId,
			OrderVolume = order.Size?.ToDecimal(),
			Balance = order.RemainSize?.ToDecimal(),
			OrderState = order.Status.ToOrderState(),

			TradeStringId = order.TradeId,
			TradePrice = order.MatchPrice?.ToDecimal(),
			TradeVolume = order.MatchSize?.ToDecimal(),

			OriginSide = isTaker is null ? null : (isTaker == true ? order.Side.ToSide() : order.Side.ToSide().Invert()),
		}, cancellationToken);
	}

	private ValueTask SessionOnPusherStopOrderChanged(SocketStopOrder order, CancellationToken cancellationToken)
	{
		// TODO
		return default;
	}

	private ValueTask SessionOnPusherBalanceChanged(Balance balance, CancellationToken cancellationToken)
	{
		return default;
	}

	private ValueTask SessionOnPositionChanged(Position position, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new PositionChangeMessage
		{
			SecurityId = GetSecId(position.Symbol),
			PortfolioName = PortfolioName,
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, position.CurrentQty?.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.CurrentPrice, position.CurrentCost?.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.AvgEntryPrice?.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.RealizedPnL, position.RealisedPnl?.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealisedPnl?.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.LiquidationPrice, position.LiquidationPrice?.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.Leverage, position.RealLeverage?.ToDecimal(), true)
		, cancellationToken);
	}
}
