namespace StockSharp.Bitget.Native.Spot;

using System.Runtime.CompilerServices;

using StockSharp.Bitget.Native.Spot.Model;

class SpotAdapter : NativeAdapter
{
	private readonly HttpClient _httpClient;
	private readonly SocketClient _socketClient;
	private readonly SynchronizedDictionary<string, long> _candleTransIds = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly SynchronizedDictionary<string, BookInfo<OrderBook>> _bookInfos = new(StringComparer.InvariantCultureIgnoreCase);

	public SpotAdapter(BitgetMessageAdapter adapter, Authenticator authenticator)
		: base(authenticator, adapter.TransactionIdGenerator, BoardCodes.Bitget, SecurityTypes.CryptoCurrency)
	{
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
		_socketClient.StateChanged += SessionOnStateChanged;
		_socketClient.Error += SessionOnPusherError;
		_socketClient.TickerReceived += SessionOnTickerReceived;
		_socketClient.TradeReceived += SessionOnTradeReceived;
		_socketClient.OrderBookReceived += SessionOnOrderBookReceived;
		_socketClient.CandleReceived += SessionOnCandleReceived;
		_socketClient.BookTickerReceived += SessionOnBookTickerReceived;
		_socketClient.BalancesReceived += SessionOnBalancesReceived;
		_socketClient.OrdersReceived += SessionOnOrdersReceived;
		_socketClient.UserTradesReceived += SessionOnUserTradesReceived;
	}

	private void UnsubscribePusherClient()
	{
		_socketClient.StateChanged -= SessionOnStateChanged;
		_socketClient.Error -= SessionOnPusherError;
		_socketClient.TickerReceived -= SessionOnTickerReceived;
		_socketClient.TradeReceived -= SessionOnTradeReceived;
		_socketClient.OrderBookReceived -= SessionOnOrderBookReceived;
		_socketClient.CandleReceived -= SessionOnCandleReceived;
		_socketClient.BookTickerReceived -= SessionOnBookTickerReceived;
		_socketClient.BalancesReceived -= SessionOnBalancesReceived;
		_socketClient.OrdersReceived -= SessionOnOrdersReceived;
		_socketClient.UserTradesReceived -= SessionOnUserTradesReceived;
	}

	public override ValueTask ConnectAsync(CancellationToken cancellationToken)
		=> _socketClient.Connect(cancellationToken);

	public override void Disconnect()
		=> _socketClient.Disconnect();

	public override async IAsyncEnumerable<SecurityMessage> SecurityLookup(SecurityLookupMessage lookupMsg, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		foreach (var symbol in await _httpClient.GetSymbols(cancellationToken))
		{
			yield return new SecurityMessage
			{
				SecurityId = symbol.Id.ToStockSharp(BoardCode),
				VolumeStep = symbol.MinTradeAmount?.ToDecimal(),
				MaxVolume = symbol.MaxTradeAmount?.ToDecimal(),
				OriginalTransactionId = lookupMsg.TransactionId,
				Decimals = symbol.QuantityScale,
				PriceStep = symbol.PriceScale?.GetPriceStep(),
				SecurityType = SecType,
			}.TryFillUnderlyingId(symbol.BaseCoin);
		}
	}

	public override async ValueTask Level1(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToSymbol();

		if (mdMsg.IsSubscribe)
		{
			await _socketClient.SubscribeBookTicker(symbol, cancellationToken);
			await _socketClient.SubscribeTicker(symbol, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await _socketClient.UnsubscribeBookTicker(symbol, cancellationToken);
			await _socketClient.UnsubscribeTicker(symbol, cancellationToken);
		}
	}

	public override async ValueTask OrderBook(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToSymbol();
		var depth = mdMsg.MaxDepth ?? 15;

		if (mdMsg.IsSubscribe)
		{
			_bookInfos.Add(symbol, new(depth));
			await _socketClient.SubscribeOrderBook(symbol, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_bookInfos.Remove(symbol);
			await _socketClient.UnsubscribeOrderBook(symbol, cancellationToken);
		}
	}

	public override async ValueTask Ticks(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToSymbol();

		if (mdMsg.IsSubscribe)
		{
			await _socketClient.SubscribeTrades(symbol, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await _socketClient.UnsubscribeTrades(symbol, cancellationToken);
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

				var startTime = (long)from.ToUnix(false);
				var endTime = (long)to.ToUnix(false);

				const int maxCount = 1000;

				while (from < to)
				{
					var needBreak = true;
					var candles = await _httpClient.GetCandles(symbol, tf, startTime, endTime, maxCount, cancellationToken);

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
							OpenPrice = (decimal)(candle.Open ?? 0),
							ClosePrice = (decimal)(candle.Close ?? 0),
							HighPrice = (decimal)(candle.High ?? 0),
							LowPrice = (decimal)(candle.Low ?? 0),
							TotalVolume = (decimal)(candle.BaseVolume ?? 0),
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
						startTime = (long)candle.Time.ToUnix(false);
					}

					if (needBreak || candles.Length < maxCount)
						break;
				}
			}

			if (!mdMsg.IsHistoryOnly())
			{
				_candleTransIds[$"{tf}_{symbol}"] = mdMsg.TransactionId;
				await _socketClient.SubscribeCandles(symbol, tf, cancellationToken);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await _socketClient.UnsubscribeCandles(symbol, tf, cancellationToken);
		}
	}

	public override async ValueTask RegisterOrder(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var symbol = regMsg.SecurityId.ToSymbol();
		var condition = (BitgetOrderCondition)regMsg.Condition;

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

				if (condition.IsWithdraw)
					await _httpClient.Withdraw(symbol, regMsg.Volume, condition.WithdrawInfo, cancellationToken);

				break;
			}
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		await _httpClient.PlaceOrder(regMsg, cancellationToken);
	}

	public override ValueTask CancelOrder(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		if (cancelMsg.OrderId == null)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		return _httpClient.CancelOrder(cancelMsg, cancellationToken).AsValueTask();
	}

	public override async ValueTask CancelGroupOrder(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var orders = await _httpClient.GetOpenOrders(cancellationToken);

		var items = orders
			.Select(o => new {
				orderId = o.OrderId.ToString(),
				symbol = o.Symbol,
				clientOid = o.ClientOid
			})
			.ToArray();

		if (items.Length > 0)
			await _httpClient.BatchCancelOrders(items, cancellationToken);
	}

	public override ValueTask ReplaceOrder(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		if (replaceMsg.OldOrderId == null)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(replaceMsg.OriginalTransactionId));

		return _httpClient.AmendOrder(replaceMsg, cancellationToken).AsValueTask();
	}

	public override async ValueTask PortfolioLookup(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		if (lookupMsg == null)
			throw new ArgumentNullException(nameof(lookupMsg));

		if (!lookupMsg.IsSubscribe)
		{
			await _socketClient.UnsubscribeBalance(cancellationToken);
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
			await _socketClient.SubscribeBalance(cancellationToken);
	}

	public override async ValueTask OrderStatus(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		if (statusMsg == null)
			throw new ArgumentNullException(nameof(statusMsg));

		if (!statusMsg.IsSubscribe)
		{
			await _socketClient.UnsubscribeUserTrades(cancellationToken);
			await _socketClient.UnsubscribeOrders(cancellationToken);
			return;
		}

		var orders = await _httpClient.GetOpenOrders(cancellationToken);

		await ProcessOrdersAsync(statusMsg.TransactionId, orders, cancellationToken);

		if (!statusMsg.IsHistoryOnly())
		{
			await _socketClient.SubscribeOrders(cancellationToken);
			await _socketClient.SubscribeUserTrades(cancellationToken);
		}
	}

	private async ValueTask ProcessOrdersAsync(long originTransId, IEnumerable<Order> orders, CancellationToken cancellationToken)
	{
		foreach (var order in orders)
		{
			if (!order.ClientOid.TryToTransId(out var transId))
				continue;

			var symbol = order.InstId ?? order.Symbol;

			if (symbol.IsEmpty())
				continue;

			var balance = order.Size.HasValue
				? (order.Size.Value - (order.BaseVolume ?? 0d)).ToDecimal()
				: (decimal?)null;

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = symbol.ToStockSharp(BoardCode),
				ServerTime = originTransId > 0 ? order.CreateTime : (order.UpdateTime ?? order.CreateTime),
				TransactionId = originTransId > 0 ? transId : 0,
				OriginalTransactionId = originTransId > 0 ? originTransId : transId,
				OrderType = order.OrderType.ToOrderType(),
				OrderId = order.OrderId,
				OrderVolume = order.Size?.ToDecimal(),
				Balance = balance,
				Side = order.Side.ToSide(),
				OrderPrice = order.Price?.ToDecimal() ?? 0,
				PortfolioName = PortfolioName,
				OrderState = order.Status.ToOrderState(),
			}, cancellationToken);
		}
	}

	private async ValueTask ProcessBalancesAsync(long transId, IEnumerable<Balance> balances, CancellationToken cancellationToken)
	{
		foreach (var balance in balances)
		{
			var blocked = balance.Frozen.HasValue || balance.Locked.HasValue
				? (decimal?)((balance.Frozen ?? 0d) + (balance.Locked ?? 0d))
				: null;

			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = PortfolioName,
				SecurityId = balance.Coin.ToStockSharp(BoardCode),
				ServerTime = balance.UpdateTime ?? CurrentTime,
				OriginalTransactionId = transId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, (decimal?)balance.Available, true)
			.TryAdd(PositionChangeTypes.BlockedValue, blocked, true), cancellationToken);
		}
	}

	private ValueTask SessionOnStateChanged(ConnectionStates state, CancellationToken cancellationToken)
	{
		return SendOutConnectionStateAsync(state, cancellationToken);
	}

	private async ValueTask SessionOnBalancesReceived(IEnumerable<Balance> balances, CancellationToken cancellationToken)
	{
		await ProcessBalancesAsync(0, balances, cancellationToken);
	}

	private async ValueTask SessionOnOrdersReceived(IEnumerable<Order> orders, CancellationToken cancellationToken)
	{
		await ProcessOrdersAsync(0, orders, cancellationToken);
	}

	private async ValueTask SessionOnUserTradesReceived(IEnumerable<UserTrade> trades, CancellationToken cancellationToken)
	{
		foreach (var trade in trades)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				ServerTime = trade.CreateTime,
				TradeId = trade.TradeId,
				TradePrice = (decimal)(trade.PriceAvg ?? 0),
				TradeVolume = (decimal)(trade.Size ?? 0),
				OrderId = trade.OrderId,
				OriginSide = trade.Side.ToSide(),
				Commission = trade.Fees?.ToDecimal(),
				CommissionCurrency = trade.FeeCurrency,
			}, cancellationToken);
		}
	}

	private ValueTask SessionOnTickerReceived(Ticker ticker, CancellationToken cancellationToken)
	{
		if (ticker.Symbol.IsEmpty())
			return default;

		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ticker.Symbol.ToStockSharp(BoardCode),
			ServerTime = ticker.Timestamp,
		}
		.TryAdd(Level1Fields.LastTradePrice, (decimal?)ticker.LastPrice)
		.TryAdd(Level1Fields.Change, (decimal?)ticker.Change)
		.TryAdd(Level1Fields.HighPrice, (decimal?)ticker.High24h)
		.TryAdd(Level1Fields.LowPrice, (decimal?)ticker.Low24h)
		.TryAdd(Level1Fields.Volume, (decimal?)ticker.BaseVolume)
		.TryAdd(Level1Fields.HighBidPrice, (decimal?)ticker.BidPrice)
		.TryAdd(Level1Fields.LowAskPrice, (decimal?)ticker.AskPrice)
		, cancellationToken);
	}

	private ValueTask SessionOnTradeReceived(Trade trade, CancellationToken cancellationToken)
	{
		if (trade.Symbol.IsEmpty())
			return default;

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = trade.Symbol.ToStockSharp(BoardCode),
			ServerTime = trade.Timestamp,
			TradeId = trade.TradeId,
			TradePrice = trade.Price?.ToDecimal(),
			TradeVolume = trade.Size?.ToDecimal(),
			OriginSide = trade.Side.ToSide(),
		}, cancellationToken);
	}

	private ValueTask SessionOnOrderBookReceived(OrderBook book, CancellationToken cancellationToken)
	{
		if (book.Symbol.IsEmpty())
			return default;

		return SendOutMessageAsync(new QuoteChangeMessage
		{
			ServerTime = book.Timestamp,
			SecurityId = book.Symbol.ToStockSharp(BoardCode),
			Bids = book.Bids?.Select(e => new QuoteChange((decimal)(e.Price ?? 0), (decimal)(e.Size ?? 0))).ToArray() ?? [],
			Asks = book.Asks?.Select(e => new QuoteChange((decimal)(e.Price ?? 0), (decimal)(e.Size ?? 0))).ToArray() ?? [],
			State = QuoteChangeStates.SnapshotComplete,
		}, cancellationToken);
	}

	private ValueTask SessionOnCandleReceived(Candle candle, CancellationToken cancellationToken)
	{
		if (candle.Symbol.IsEmpty() || candle.Granularity.IsEmpty())
			return default;

		if (!_candleTransIds.TryGetValue($"{candle.Granularity}_{candle.Symbol}", out var transId))
			return default;

		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OpenPrice = (decimal)(candle.Open ?? 0),
			ClosePrice = (decimal)(candle.Close ?? 0),
			HighPrice = (decimal)(candle.High ?? 0),
			LowPrice = (decimal)(candle.Low ?? 0),
			TotalVolume = (decimal)(candle.BaseVolume ?? 0),
			OpenTime = candle.Timestamp,
			State = CandleStates.Active,
			OriginalTransactionId = transId,
		}, cancellationToken);
	}

	private ValueTask SessionOnBookTickerReceived(JToken token, CancellationToken cancellationToken)
	{
		var instId = (string)token["instId"];

		if (instId.IsEmpty())
			return default;

		var ts = token["ts"]?.Value<long?>();
		var bid = token["bids"]?[0];
		var ask = token["asks"]?[0];

		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = instId.ToStockSharp(BoardCode),
			ServerTime = ts?.FromUnix(false) ?? CurrentTime,
		}
		.TryAdd(Level1Fields.BestBidPrice, bid?[0]?.Value<decimal?>())
		.TryAdd(Level1Fields.BestBidVolume, bid?[1]?.Value<decimal?>())
		.TryAdd(Level1Fields.BestAskPrice, ask?[0]?.Value<decimal?>())
		.TryAdd(Level1Fields.BestAskVolume, ask?[1]?.Value<decimal?>())
		, cancellationToken);
	}

	private ValueTask SessionOnPusherError(Exception error, CancellationToken cancellationToken)
	{
		return SendOutErrorAsync(error, cancellationToken);
	}
}
