namespace StockSharp.GateIO.Native.Spot;

using System.Runtime.CompilerServices;

using StockSharp.GateIO.Native.Spot.Model;

class SpotAdapter : NativeAdapter
{
	private readonly HttpClient _httpClient;
	private readonly SocketClient _socketClient;
	private readonly SynchronizedDictionary<string, long> _candleTransIds = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly SynchronizedDictionary<string, BookInfo<OrderBook>> _bookInfos = new(StringComparer.InvariantCultureIgnoreCase);
	private long _tickerTransId;
	private long _userTradesTransId;

	public SpotAdapter(GateIOMessageAdapter adapter, Authenticator authenticator)
		: base(authenticator, adapter.TransactionIdGenerator, BoardCodes.GateIO, SecurityTypes.CryptoCurrency)
	{
		if (adapter.IsDemo)
			throw new NotSupportedException("Demo for spot not supported.");

		_httpClient = new(adapter, authenticator) { Parent = this };
		_socketClient = new(adapter, authenticator, adapter.ReConnectionSettings.WorkingTime) { Parent = this };
	
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
		_socketClient.TickerReceived += SessionOnTickerReceived;
		_socketClient.TradeReceived += SessionOnTradeReceived;
		_socketClient.OrderBookReceived += SessionOnOrderBookReceived;
		_socketClient.CandleReceived += SessionOnCandleReceived;
		_socketClient.BookTickerReceived += SessionOnBookTickerReceived;
		_socketClient.BalancesReceived += SessionOnBalancesReceived;
		_socketClient.OrdersReceived += SessionOnOrdersReceived;
		_socketClient.UserTradesReceived += SessionOnUserTradesReceived;
		_socketClient.OrderResponseReceived += SessionOnOrderResponseReceived;
	}

	private void UnsubscribePusherClient()
	{
		_socketClient.StateChanged -= SendOutConnectionStateAsync;
		_socketClient.Error -= SessionOnPusherError;
		_socketClient.TickerReceived -= SessionOnTickerReceived;
		_socketClient.TradeReceived -= SessionOnTradeReceived;
		_socketClient.OrderBookReceived -= SessionOnOrderBookReceived;
		_socketClient.CandleReceived -= SessionOnCandleReceived;
		_socketClient.BookTickerReceived -= SessionOnBookTickerReceived;
		_socketClient.BalancesReceived -= SessionOnBalancesReceived;
		_socketClient.OrdersReceived -= SessionOnOrdersReceived;
		_socketClient.UserTradesReceived -= SessionOnUserTradesReceived;
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
				SecurityId = symbol.Id.ToStockSharp(BoardCode),
				MinVolume = symbol.MinBaseAmount?.ToDecimal(),
				OriginalTransactionId = lookupMsg.TransactionId,
				Decimals = symbol.AmountPrecision,
				SecurityType = SecType,
			}.TryFillUnderlyingId(symbol.Base);
		}
	}

	public override async ValueTask Level1(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToSymbol();

		if (mdMsg.IsSubscribe)
		{
			await _socketClient.SubscribeBookTicker(mdMsg.TransactionId, symbol, cancellationToken);
			await _socketClient.SubscribeTicker(_tickerTransId = GetNextId(), symbol, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await _socketClient.UnsubscribeBookTicker(mdMsg.OriginalTransactionId, symbol, cancellationToken);
			await _socketClient.UnsubscribeTicker(_tickerTransId, symbol, cancellationToken);
		}
	}

	public override async ValueTask OrderBook(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToSymbol();

		const string interval = "100ms";

		var depth = mdMsg.MaxDepth ?? 5;
		//var level = depth.ToString();

		if (mdMsg.IsSubscribe)
		{
			_bookInfos.Add(symbol, new(depth));
			await _socketClient.SubscribeOrderBook(mdMsg.TransactionId, symbol, interval, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_bookInfos.Remove(symbol);
			await _socketClient.UnsubscribeOrderBook(mdMsg.OriginalTransactionId, symbol, interval, cancellationToken);
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

				var last = (long)from.ToUnix();

				const int maxCount = 1000;

				while (from < to)
				{
					var needBreak = true;
					var candles = await _httpClient.GetCandles(symbol, tf, last, null, maxCount, cancellationToken);

					foreach (var candle in candles.OrderBy(c => c.Time))
					{
						var time = candle.Time.FromUnix();

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
		var condition = (GateIOOrderCondition)regMsg.Condition;

		switch (regMsg.OrderType)
		{
			case null:
			case OrderTypes.Limit:
			case OrderTypes.Market:
				break;
			case OrderTypes.Conditional:
			{
				if (condition is null)
					throw new InvalidOperationException("condition is null");

				if (!condition.IsWithdraw)
					break;

				return _httpClient.Withdraw(symbol, regMsg.Volume, condition.WithdrawInfo, cancellationToken);
			}
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		var price = regMsg.OrderType == OrderTypes.Market ? (decimal?)null : regMsg.Price;

		return _socketClient.OrderPlace(regMsg.TransactionId.ToRequestId(),
			symbol, regMsg.Side.ToNative(), regMsg.OrderType.ToNative(regMsg.PostOnly),
			regMsg.Volume, price, regMsg.TimeInForce.ToNative(null), regMsg.VisibleVolume,
			cancellationToken);
	}

	public override ValueTask CancelOrder(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		if (cancelMsg.OrderId == null)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		return _socketClient.OrderCancel(cancelMsg.TransactionId.ToRequestId(), cancelMsg.SecurityId.ToSymbol(), cancelMsg.OrderId.Value, cancellationToken);
	}

	public override ValueTask CancelGroupOrder(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
		=> _socketClient.OrderCancelAll(cancelMsg.TransactionId.ToRequestId(), cancelMsg.SecurityId.ToSymbol(), cancelMsg.Side?.ToNative(), cancellationToken);

	public override ValueTask ReplaceOrder(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		if (replaceMsg.OldOrderId == null)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(replaceMsg.OriginalTransactionId));

		return _socketClient.OrderAmend(replaceMsg.TransactionId.ToRequestId(), 
			replaceMsg.SecurityId.ToSymbol(), replaceMsg.OldOrderId.Value,
			replaceMsg.Volume, replaceMsg.Price, cancellationToken);
	}

	public override async ValueTask PortfolioLookup(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		if (lookupMsg == null)
			throw new ArgumentNullException(nameof(lookupMsg));

		if (!lookupMsg.IsSubscribe)
		{
			await _socketClient.UnsubscribeBalance(lookupMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = PortfolioName,
			BoardCode = BoardCode,
			OriginalTransactionId = lookupMsg.TransactionId
		}, cancellationToken);

		var balances = await _httpClient.GetBalance(cancellationToken);

		if (balances is not null)
			await ProcessBalancesAsync(lookupMsg.TransactionId, balances, cancellationToken);

		if (!lookupMsg.IsHistoryOnly())
			await _socketClient.SubscribeBalance(lookupMsg.TransactionId, cancellationToken);
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
				SecurityId = order.CurrencyPair.ToStockSharp(BoardCode),
				ServerTime = originTransId > 0 ? order.CreateTime : order.UpdateTime,
				TransactionId = originTransId > 0 ? transId : 0,
				OriginalTransactionId = originTransId > 0 ? originTransId : transId,
				OrderType = order.Type.ToOrderType(out var postOnly),
				PostOnly = postOnly,
				OrderId = order.Id,
				OrderVolume = order.Amount?.ToDecimal(),
				Balance = order.Left?.ToDecimal(),
				Side = order.Side.ToSide(),
				OrderPrice = order.Price?.ToDecimal() ?? 0,
				PortfolioName = PortfolioName,
				OrderState = order.Status.IsEmpty(order.FinishAs).ToOrderState(),
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
			.TryAdd(PositionChangeTypes.BlockedValue, (decimal?)(balance.Locked ?? balance.Freeze), true), cancellationToken);
		}
	}

	private ValueTask SessionOnBalancesReceived(IEnumerable<Balance> balances, CancellationToken cancellationToken)
	{
		return ProcessBalancesAsync(0, balances, cancellationToken);
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
				TradeVolume = (decimal)trade.Amount,
				OrderId = trade.OrderId,
				OriginSide = trade.Side.ToSide(),
				Commission = trade.Fee?.ToDecimal(),
				CommissionCurrency = trade.FeeCurrency,
			}, cancellationToken);
		}
	}

	private ValueTask SessionOnTickerReceived(Ticker ticker, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ticker.CurrencyPair.ToStockSharp(BoardCode),
			ServerTime = CurrentTime,
		}
		.TryAdd(Level1Fields.LastTradePrice, (decimal?)ticker.Last)
		.TryAdd(Level1Fields.Change, (decimal?)ticker.ChangePercentage)
		.TryAdd(Level1Fields.HighPrice, (decimal?)ticker.High24h)
		.TryAdd(Level1Fields.LowPrice, (decimal?)ticker.Low24h)
		.TryAdd(Level1Fields.Volume, (decimal?)ticker.BaseVolume)
		.TryAdd(Level1Fields.HighBidPrice, (decimal?)ticker.HighestBid)
		.TryAdd(Level1Fields.LowAskPrice, (decimal?)ticker.LowestAsk)
		, cancellationToken);
	}

	private ValueTask SessionOnTradeReceived(Trade trade, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = trade.CurrencyPair.ToStockSharp(BoardCode),
			ServerTime = trade.CreateTime,
			TradeId = trade.Id,
			TradePrice = trade.Price?.ToDecimal(),
			TradeVolume = trade.Amount?.ToDecimal(),
			OriginSide = trade.Side.ToSide(),
		}, cancellationToken);
	}

	private async ValueTask SessionOnOrderBookReceived(OrderBook book, long firstId, long lastId, CancellationToken cancellationToken)
	{
		if (!_bookInfos.TryGetValue(book.Symbol, out var info))
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
				SecurityId = inc.Symbol.ToStockSharp(BoardCode),
				Bids = inc.Bids.Select(e => new QuoteChange((decimal)e.Price, (decimal)e.Amount)).ToArray(),
				Asks = inc.Asks.Select(e => new QuoteChange((decimal)e.Price, (decimal)e.Amount)).ToArray(),
				State = QuoteChangeStates.Increment,
			}, cancellationToken);
		}

		if (canIncrement)
			await sendIncAsync(book);
		else
		{
			this.AddDebugLog("getting snapshot for {0}", book.Symbol);

			try
			{
				var snapshot = await _httpClient.GetOrderBook(book.Symbol, info.Depth, cancellationToken);

				this.AddDebugLog("got snapshot for {0}", book.Symbol);

				await SendOutMessageAsync(new QuoteChangeMessage
				{
					ServerTime = snapshot.Time,
					SecurityId = book.Symbol.ToStockSharp(BoardCode),
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

	private ValueTask SessionOnCandleReceived(Candle candle, CancellationToken cancellationToken)
	{
		if (!_candleTransIds.TryGetValue(candle.Name, out var transId))
			return default;

		return SendOutMessageAsync(new TimeFrameCandleMessage
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
}
