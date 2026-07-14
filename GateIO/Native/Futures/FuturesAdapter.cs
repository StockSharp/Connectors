namespace StockSharp.GateIO.Native.Futures;

using System.Runtime.CompilerServices;

using StockSharp.GateIO.Native.Futures.Model;

class FuturesAdapter : NativeAdapter
{
	private readonly HttpClient _httpClient;
	private readonly SocketClient _socketClient;
	private readonly SynchronizedDictionary<string, long> _candleTransIds = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly SynchronizedDictionary<string, BookInfo<OrderBook>> _bookInfos = new(StringComparer.InvariantCultureIgnoreCase);
	private long _userTradesTransId;
	private long _balancesTransId;

	public FuturesAdapter(GateIOMessageAdapter adapter, Authenticator authenticator)
		: base(authenticator, adapter.TransactionIdGenerator, BoardCodes.GateIOPerpetual, SecurityTypes.Future)
	{
		const string coin = "usdt";

		_httpClient = new(adapter, coin, authenticator) { Parent = this };
		_socketClient = new(adapter, coin, authenticator, adapter.ReConnectionSettings.WorkingTime) { Parent = this };

		SubscribePusherClient();
	}

	protected override void DisposeManaged()
	{
		UnsubscribePusherClient();

		_httpClient.Dispose();
		_socketClient.Dispose();

		base.DisposeManaged();
	}

	private void SubscribePusherClient()
	{
		_socketClient.StateChanged += SendOutConnectionStateAsync;
		_socketClient.Error += SessionOnPusherError;
		_socketClient.TickersReceived += SessionOnTickersReceived;
		_socketClient.TradesReceived += SessionOnTradesReceived;
		_socketClient.BookTickerReceived += SessionOnBookTickerReceived;
		_socketClient.OrderBookReceived += SessionOnOrderBookReceived;
		_socketClient.CandlesReceived += SessionOnCandlesReceived;
		_socketClient.PositionsReceived += SessionOnPositionsReceived;
		_socketClient.OrdersReceived += SessionOnOrdersReceived;
		_socketClient.UserTradesReceived += SessionOnUserTradesReceived;
		_socketClient.BalancesReceived += SessionOnBalancesReceived;
		_socketClient.OrderResponseReceived += SessionOnOrderResponseReceived;
	}

	private void UnsubscribePusherClient()
	{
		_socketClient.StateChanged -= SendOutConnectionStateAsync;
		_socketClient.Error -= SessionOnPusherError;
		_socketClient.TickersReceived -= SessionOnTickersReceived;
		_socketClient.TradesReceived -= SessionOnTradesReceived;
		_socketClient.BookTickerReceived -= SessionOnBookTickerReceived;
		_socketClient.OrderBookReceived -= SessionOnOrderBookReceived;
		_socketClient.CandlesReceived -= SessionOnCandlesReceived;
		_socketClient.PositionsReceived -= SessionOnPositionsReceived;
		_socketClient.OrdersReceived -= SessionOnOrdersReceived;
		_socketClient.UserTradesReceived -= SessionOnUserTradesReceived;
		_socketClient.BalancesReceived -= SessionOnBalancesReceived;
		_socketClient.OrderResponseReceived -= SessionOnOrderResponseReceived;
	}

	public override ValueTask ConnectAsync(CancellationToken cancellationToken)
		=> _socketClient.Connect(cancellationToken);

	public override void Disconnect()
		=> _socketClient.Disconnect();

	public override ValueTask Time(TimeMessage timeMsg, CancellationToken cancellationToken)
		=> _socketClient.Ping(cancellationToken);

	public override async IAsyncEnumerable<SecurityMessage> SecurityLookup(SecurityLookupMessage lookupMsg, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		foreach (var symbol in await _httpClient.GetSymbols(cancellationToken))
		{
			yield return new SecurityMessage
			{
				SecurityId = symbol.Name.ToStockSharp(BoardCode),
				SecurityType = SecType,
				OriginalTransactionId = lookupMsg.TransactionId,
				PriceStep = symbol.OrderPriceRound?.ToDecimal(),
				VolumeStep = symbol.OrderSizeMin?.ToDecimal(),
				MaxVolume = symbol.OrderSizeMax?.ToDecimal(),
			};
		}
	}

	public override async ValueTask PortfolioLookup(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		if (lookupMsg == null)
			throw new ArgumentNullException(nameof(lookupMsg));

		if (!lookupMsg.IsSubscribe)
		{
			await _socketClient.UnsubscribeBalances(_balancesTransId, cancellationToken);
			await _socketClient.UnsubscribePositions(lookupMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var balance = await _httpClient.GetBalance(cancellationToken);

		if (balance is not null)
			await ProcessBalancesAsync(lookupMsg.TransactionId, [balance], cancellationToken);

		var positions = await _httpClient.GetPositions(cancellationToken);

		if (positions is not null)
			await ProcessPositionsAsync(lookupMsg.TransactionId, positions, cancellationToken);

		if (!lookupMsg.IsHistoryOnly())
		{
			await _socketClient.SubscribePositions(lookupMsg.TransactionId, cancellationToken);
			await _socketClient.SubscribeBalances(_balancesTransId = GetNextId(), cancellationToken);
		}
	}

	public override async ValueTask OrderStatus(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		if (statusMsg == null)
			throw new ArgumentNullException(nameof(statusMsg));

		if (!statusMsg.IsSubscribe)
		{
			await _socketClient.UnsubscribeUserTrades(_userTradesTransId, cancellationToken);
			await _socketClient.UnsubscribeOrders(statusMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var orders = await _httpClient.GetOpenOrders(cancellationToken);

		await ProcessOrdersAsync(statusMsg.TransactionId, orders, cancellationToken);

		if (!statusMsg.IsHistoryOnly())
		{
			await _socketClient.SubscribeOrders(statusMsg.TransactionId, cancellationToken);
			await _socketClient.SubscribeUserTrades(_userTradesTransId = GetNextId(), cancellationToken);
		}
	}

	public override async ValueTask Level1(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToSymbol();

		if (mdMsg.IsSubscribe)
		{
			await _socketClient.SubscribeTicker(mdMsg.TransactionId, symbol, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await _socketClient.UnsubscribeTicker(mdMsg.OriginalTransactionId, symbol, cancellationToken);
		}
	}

	public override async ValueTask OrderBook(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToSymbol();

		const string interval = "100ms";

		var depth = mdMsg.MaxDepth ?? 5;
		var level = depth.ToString();

		if (mdMsg.IsSubscribe)
		{
			_bookInfos.Add(symbol, new(depth));
			await _socketClient.SubscribeOrderBook(mdMsg.TransactionId, symbol, level, interval, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_bookInfos.Remove(symbol);
			await _socketClient.UnsubscribeOrderBook(mdMsg.OriginalTransactionId, symbol, level, interval, cancellationToken);
		}
	}

	public override async ValueTask Ticks(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToSymbol();

		if (mdMsg.IsSubscribe)
		{
			await _socketClient.SubscribeTrades(mdMsg.TransactionId, symbol, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await _socketClient.UnsubscribeTrades(mdMsg.OriginalTransactionId, symbol, cancellationToken);
		}
	}

	public override async ValueTask TFCandles(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToSymbol();
		var tf = mdMsg.GetTimeFrame().ToNative();

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.From is DateTime from)
			{
				var to = mdMsg.To ?? DateTime.UtcNow;
				var left = mdMsg.Count ?? long.MaxValue;

				var last = from;

				const int maxCount = 1000;

				while (from < to)
				{
					var needBreak = true;
					var candles = await _httpClient.GetCandles(symbol, tf, (long)last.ToUnix(), null, maxCount, cancellationToken);

					foreach (var candle in candles.OrderBy(c => c.Time))
					{
						var time = candle.Time;

						if (time <= from)
							continue;

						if (time > to)
						{
							needBreak = true;
							break;
						}

						await SendOutMessageAsync(new TimeFrameCandleMessage
						{
							OpenPrice = (decimal)candle.Open,
							ClosePrice = (decimal)candle.Close,
							HighPrice = (decimal)candle.High,
							LowPrice = (decimal)candle.Low,
							TotalVolume = (decimal)candle.Volume,
							OpenTime = time,
							State = CandleStates.Finished,
							OriginalTransactionId = mdMsg.TransactionId,
						}, cancellationToken);

						if (--left <= 0)
						{
							needBreak = true;
							break;
						}

						needBreak = false;
						last = candle.Time;
					}

					if (needBreak || candles.Length < maxCount)
						break;
				}
			}

			if (!mdMsg.IsHistoryOnly())
			{
				_candleTransIds[$"{tf}_{symbol}"] = mdMsg.TransactionId;
				await _socketClient.SubscribeCandles(mdMsg.TransactionId, symbol, tf, cancellationToken);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await _socketClient.UnsubscribeCandles(mdMsg.OriginalTransactionId, symbol, tf, cancellationToken);
		}
	}

	public override ValueTask RegisterOrder(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var symbol = regMsg.SecurityId.ToSymbol();

		switch (regMsg.OrderType)
		{
			case null:
			case OrderTypes.Limit:
			case OrderTypes.Market:
				break;
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		return _socketClient.OrderPlace(regMsg.TransactionId.ToRequestId(),
			symbol, regMsg.Side == Sides.Buy ? regMsg.Volume : -regMsg.Volume, regMsg.Price, regMsg.TimeInForce.ToNative(regMsg.PostOnly),
			regMsg.VisibleVolume, regMsg.PositionEffect?.ToNative(), cancellationToken);
	}

	public override ValueTask CancelOrder(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		if (cancelMsg.OrderId == null)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		return _socketClient.OrderCancel(cancelMsg.TransactionId.ToRequestId(), cancelMsg.SecurityId.ToSymbol(), cancelMsg.OrderId.Value, cancellationToken);
	}

	public override ValueTask CancelGroupOrder(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
		=> _socketClient.OrderCancelAll(cancelMsg.TransactionId.ToRequestId(), cancelMsg.SecurityId.ToSymbol(), cancelMsg.Side?.ToBidAsk(), cancellationToken);

	public override ValueTask ReplaceOrder(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		if (replaceMsg.OldOrderId == null)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(replaceMsg.OriginalTransactionId));

		return _socketClient.OrderAmend(replaceMsg.TransactionId.ToRequestId(),
			replaceMsg.SecurityId.ToSymbol(), replaceMsg.OldOrderId.Value,
			replaceMsg.Side == Sides.Buy ? replaceMsg.Volume : -replaceMsg.Volume, replaceMsg.Price, cancellationToken);
	}

	private async ValueTask ProcessPositionsAsync(long transId, IEnumerable<Position> positions, CancellationToken cancellationToken)
	{
		foreach (var position in positions)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				SecurityId = position.Contract.ToStockSharp(BoardCode),
				PortfolioName = PortfolioName,
				ServerTime = position.UpdateTime ?? CurrentTime,
				OriginalTransactionId = transId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, position.Size.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.AveragePrice, position.EntryPrice?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.RealizedPnL, position.RealisedPnl?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealisedPnl?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.Leverage, position.Leverage?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.Commission, position.PnlFee?.ToDecimal(), true)
			, cancellationToken);
		}
	}

	private ValueTask SessionOnPositionsReceived(IEnumerable<Position> positions, CancellationToken cancellationToken)
	{
		return ProcessPositionsAsync(0, positions, cancellationToken);
	}

	private ValueTask SessionOnOrdersReceived(IEnumerable<Order> orders, CancellationToken cancellationToken)
	{
		return ProcessOrdersAsync(0, orders, cancellationToken);
	}

	private async ValueTask SessionOnUserTradesReceived(IEnumerable<UserTrade> trades, CancellationToken cancellationToken)
	{
		foreach (var trade in trades)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				ServerTime = trade.CreateTime,
				TradeId = trade.Id,
				TradePrice = (decimal)trade.Price,
				TradeVolume = (decimal)trade.Size,
				OrderId = trade.OrderId,
				IsMarketMaker = trade.Role == "maker",
				Commission = trade.Fee?.ToDecimal(),
			}, cancellationToken);
		}
	}

	private async ValueTask ProcessBalancesAsync(long transId, IEnumerable<Balance> balances, CancellationToken cancellationToken)
	{
		foreach (var balance in balances)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = PortfolioName,
				SecurityId = balance.Currency.ToStockSharp(BoardCode),
				ServerTime = balance.Timestamp ?? CurrentTime,
				OriginalTransactionId = transId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, (decimal?)balance.Available, true)
			.TryAdd(PositionChangeTypes.BlockedValue, (decimal?)(balance.Locked ?? balance.Freeze), true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, (decimal?)balance.UnrealisedPnl, true)
			, cancellationToken);
		}
	}

	private ValueTask SessionOnBalancesReceived(IEnumerable<Balance> balances, CancellationToken cancellationToken)
	{
		return ProcessBalancesAsync(0, balances, cancellationToken);
	}

	private ValueTask SessionOnBookTickerReceived(JToken token, CancellationToken cancellationToken)
	{
		dynamic ticker = token;

		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ((string)ticker.s).ToStockSharp(BoardCode),
			ServerTime = ((long)ticker.t).FromUnix(false),
		}
		.TryAdd(Level1Fields.BestBidPrice, (decimal?)(double?)ticker.b)
		.TryAdd(Level1Fields.BestBidVolume, (decimal?)(double?)ticker.B)
		.TryAdd(Level1Fields.BestAskPrice, (decimal?)(double?)ticker.a)
		.TryAdd(Level1Fields.BestAskVolume, (decimal?)(double?)ticker.A)
		, cancellationToken);
	}

	private async ValueTask SessionOnTickersReceived(IEnumerable<Ticker> tickers, CancellationToken cancellationToken)
	{
		foreach (var ticker in tickers)
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = ticker.Contract.ToStockSharp(BoardCode),
				ServerTime = CurrentTime,
			}
			.TryAdd(Level1Fields.LastTradePrice, (decimal?)ticker.Last)
			.TryAdd(Level1Fields.Change, (decimal?)ticker.ChangePercentage)
			.TryAdd(Level1Fields.HighPrice, (decimal?)ticker.High24h)
			.TryAdd(Level1Fields.LowPrice, (decimal?)ticker.Low24h)
			.TryAdd(Level1Fields.Volume, (decimal?)ticker.Volume24h)
			.TryAdd(Level1Fields.OpenInterest, (decimal?)ticker.OpenInterest)
			.TryAdd(Level1Fields.HighBidPrice, (decimal?)ticker.HighestBid)
			.TryAdd(Level1Fields.LowAskPrice, (decimal?)ticker.LowestAsk)
			, cancellationToken);
		}
	}

	private async ValueTask SessionOnTradesReceived(IEnumerable<Trade> trades, CancellationToken cancellationToken)
	{
		foreach (var trade in trades)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = trade.Contract.ToStockSharp(BoardCode),
				ServerTime = trade.CreateTime,
				TradeId = trade.Id,
				TradePrice = trade.Price?.ToDecimal(),
				TradeVolume = trade.Size?.ToDecimal()?.Abs(),
				OriginSide = trade.Size > 0 ? Sides.Buy : Sides.Sell,
				IsMarketMaker = trade.IsMaker,
			}, cancellationToken);
		}
	}

	private async ValueTask SessionOnOrderBookReceived(OrderBook book, long firstId, long lastId, CancellationToken cancellationToken)
	{
		if (!_bookInfos.TryGetValue(book.Contract, out var info))
			return;

		bool canIncrement;

		using (info.Sync.EnterScope())
		{
			if (info.NextId > firstId)
				return;

			if (info.IsRestoring)
			{
				info.AddIncrement(firstId, lastId, book);
				return;
			}

			canIncrement = info.NextId == firstId;

			if (canIncrement)
			{
				info.NextId = lastId + 1;
			}
			else
			{
				info.AddIncrement(firstId, lastId, book);
				info.IsRestoring = true;
			}
		}

		async ValueTask sendIncAsync(OrderBook inc)
		{
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				ServerTime = inc.Time,
				SecurityId = inc.Contract.ToStockSharp(BoardCode),
				Bids = inc.Bids.Select(e => new QuoteChange((decimal)e.Price, (decimal)e.Amount)).ToArray(),
				Asks = inc.Asks.Select(e => new QuoteChange((decimal)e.Price, (decimal)e.Amount)).ToArray(),
				State = QuoteChangeStates.Increment,
			}, cancellationToken);
		}

		if (canIncrement)
			await sendIncAsync(book);
		else
		{
			this.AddDebugLog("getting snapshot for {0}", book.Contract);

			try
			{
				var snapshot = await _httpClient.GetOrderBook(book.Contract, info.Depth, cancellationToken);

				this.AddDebugLog("got snapshot for {0}", book.Contract);

				await SendOutMessageAsync(new QuoteChangeMessage
				{
					ServerTime = snapshot.Time,
					SecurityId = book.Contract.ToStockSharp(BoardCode),
					Bids = snapshot.Bids.Select(e => new QuoteChange((decimal)e.Price, (decimal)e.Amount)).ToArray(),
					Asks = snapshot.Asks.Select(e => new QuoteChange((decimal)e.Price, (decimal)e.Amount)).ToArray(),
					State = QuoteChangeStates.SnapshotComplete,
				}, cancellationToken);

				var nextId = snapshot.Id + 1;

				(long firstId, long lastId, OrderBook book)[] increments;

				using (info.Sync.EnterScope())
					increments = info.Increments.CopyAndClear();

				foreach (var (fId, lId, inc) in increments.OrderBy(i => i.firstId))
				{
					if (nextId > fId)
						continue;
					else if (nextId < fId)
						break;

					nextId = lId + 1;

					await sendIncAsync(inc);
				}

				using (info.Sync.EnterScope())
				{
					info.NextId = nextId;
					info.FinishRestore();
				}
			}
			catch (Exception ex)
			{
				this.AddErrorLog(ex);

				using (info.Sync.EnterScope())
					info.FinishRestore();
			}
		}
	}

	private async ValueTask SessionOnCandlesReceived(IEnumerable<Candle> candles, CancellationToken cancellationToken)
	{
		foreach (var candle in candles)
		{
			if (!_candleTransIds.TryGetValue(candle.Name, out var transId))
				continue;

			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OpenPrice = (decimal)candle.Open,
				ClosePrice = (decimal)candle.Close,
				HighPrice = (decimal)candle.High,
				LowPrice = (decimal)candle.Low,
				TotalVolume = (decimal)candle.Volume,
				OpenTime = candle.Time,
				State = CandleStates.Active,
				OriginalTransactionId = transId,
			}, cancellationToken);
		}
	}

	private ValueTask SessionOnOrderResponseReceived(OrderResponse response, JToken error, string requestId, CancellationToken cancellationToken)
	{
		if (error is not null && requestId.TryToTransId(out var transId))
		{
			return SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				ServerTime = CurrentTime,
				OriginalTransactionId = transId,
				Error = new InvalidOperationException(error.ToString()),
			}, cancellationToken);
		}

		return default;
	}

	private ValueTask SessionOnPusherError(Exception error, CancellationToken cancellationToken)
	{
		return SendOutErrorAsync(error, cancellationToken);
	}

	private async ValueTask ProcessOrdersAsync(long originTransId, IEnumerable<Order> orders, CancellationToken cancellationToken)
	{
		foreach (var order in orders)
		{
			if (!order.Text.TryToTransId(out var transId))
				continue;

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = order.Contract.ToStockSharp(BoardCode),
				ServerTime = originTransId > 0 ? order.CreateTime : CurrentTime,
				TransactionId = originTransId > 0 ? transId : 0,
				OriginalTransactionId = originTransId > 0 ? originTransId : transId,
				TimeInForce = order.Tif.ToTimeInForce(out var postOnly),
				PostOnly = postOnly,
				PositionEffect = order.ReduceOnly == true ? OrderPositionEffects.CloseOnly : null,
				OrderId = order.Id,
				OrderVolume = order.Size?.ToDecimal()?.Abs(),
				Balance = order.Left?.ToDecimal()?.Abs(),
				Side = order.Size > 0 ? Sides.Buy : Sides.Sell,
				OrderPrice = order.Price?.ToDecimal() ?? 0,
				PortfolioName = PortfolioName,
				OrderState = order.Status.ToOrderState(),
				VisibleVolume = order.Iceberg?.ToDecimal(),
			}, cancellationToken);
		}
	}
}