namespace StockSharp.BingX.Native.Spot;

using System.Runtime.CompilerServices;

using StockSharp.BingX.Native.Spot.Model;

class SpotAdapter : NativeAdapter
{
	private readonly HttpClient _httpClient;
	private readonly SocketClient _socketClient;
	private readonly SynchronizedDictionary<string, long> _candleTransIds = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly SynchronizedDictionary<string, BookInfo<OrderBook>> _bookInfos = new(StringComparer.InvariantCultureIgnoreCase);

	public SpotAdapter(BingXMessageAdapter adapter, Authenticator authenticator)
		: base(authenticator, adapter.TransactionIdGenerator, BoardCodes.BingX, SecurityTypes.CryptoCurrency)
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
		_socketClient.StateChanged += SendOutConnectionStateAsync;
		_socketClient.Error += SendOutErrorAsync;
		_socketClient.TickerReceived += SessionOnTickerReceived;
		_socketClient.TradeReceived += SessionOnTradeReceived;
		_socketClient.OrderBookReceived += SessionOnOrderBookReceived;
		_socketClient.CandleReceived += SessionOnCandleReceived;
		_socketClient.BalancesReceived += SessionOnBalancesReceived;
		_socketClient.OrdersReceived += SessionOnOrdersReceived;
		_socketClient.UserTradesReceived += SessionOnUserTradesReceived;
	}

	private void UnsubscribePusherClient()
	{
		_socketClient.StateChanged -= SendOutConnectionStateAsync;
		_socketClient.Error -= SendOutErrorAsync;
		_socketClient.TickerReceived -= SessionOnTickerReceived;
		_socketClient.TradeReceived -= SessionOnTradeReceived;
		_socketClient.OrderBookReceived -= SessionOnOrderBookReceived;
		_socketClient.CandleReceived -= SessionOnCandleReceived;
		_socketClient.BalancesReceived -= SessionOnBalancesReceived;
		_socketClient.OrdersReceived -= SessionOnOrdersReceived;
		_socketClient.UserTradesReceived -= SessionOnUserTradesReceived;
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
			var lotFilter = symbol.Filters?.FirstOrDefault(f => f.FilterType == "LOT_SIZE");
			var priceFilter = symbol.Filters?.FirstOrDefault(f => f.FilterType == "PRICE_FILTER");
			var notionalFilter = symbol.Filters?.FirstOrDefault(f => f.FilterType == "MIN_NOTIONAL");

			yield return new SecurityMessage
			{
				SecurityId = symbol.Id.ToStockSharp(BoardCode),
				MinVolume = lotFilter?.MinQty?.ToDecimal(),
				VolumeStep = lotFilter?.StepSize?.ToDecimal(),
				MaxVolume = lotFilter?.MaxQty?.ToDecimal(),
				PriceStep = priceFilter?.TickSize?.ToDecimal(),
				OriginalTransactionId = lookupMsg.TransactionId,
				Decimals = symbol.BaseAssetPrecision,
				SecurityType = SecType,
			}.TryFillUnderlyingId(symbol.BaseAsset);
		}
	}

	public override async ValueTask Level1(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToSymbol();

		if (mdMsg.IsSubscribe)
		{
			await _socketClient.SubscribeTicker(symbol, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await _socketClient.UnsubscribeTicker(symbol, cancellationToken);
		}
	}

	public override async ValueTask OrderBook(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToSymbol();
		var depth = mdMsg.MaxDepth ?? 20;
		var level = depth <= 5 ? "5" : depth <= 10 ? "10" : "20";

		if (mdMsg.IsSubscribe)
		{
			_bookInfos.Add(symbol, new(depth));
			await _socketClient.SubscribeOrderBook(symbol, level, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_bookInfos.Remove(symbol);
			await _socketClient.UnsubscribeOrderBook(symbol, level, cancellationToken);
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

				while (from < to && left > 0)
				{
					var candles = await _httpClient.GetCandles(symbol, tf, startTime, endTime, maxCount, cancellationToken);

					if (candles == null || candles.Length == 0)
						break;

					foreach (var candle in candles.OrderBy(c => c.OpenTime))
					{
						var time = candle.OpenTime;

						if (time <= from)
							continue;

						if (time > to)
							break;

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
							break;

						startTime = (long)candle.CloseTime.ToUnix(false) + 1;
					}

					if (candles.Length < maxCount)
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
		var condition = (BingXOrderCondition)regMsg.Condition;

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

				throw new NotSupportedException("Withdraw not implemented for BingX");
			}
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		var response = await _httpClient.PlaceOrder(
			symbol,
			regMsg.Side.ToNative(),
			regMsg.OrderType.ToNative(),
			regMsg.Volume,
			regMsg.OrderType == OrderTypes.Market ? null : regMsg.Price,
			regMsg.TimeInForce.ToNative(),
			regMsg.TransactionId.ToRequestId(),
			cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = regMsg.SecurityId,
			ServerTime = response.TransactTime,
			TransactionId = regMsg.TransactionId,
			OriginalTransactionId = regMsg.TransactionId,
			OrderId = response.OrderId,
			OrderState = response.Status.ToOrderState(),
			OrderPrice = response.Price?.ToDecimal() ?? 0,
			OrderVolume = response.OriginalQuantity?.ToDecimal(),
			Balance = (response.OriginalQuantity - response.ExecutedQuantity)?.ToDecimal(),
			PortfolioName = PortfolioName,
			Side = regMsg.Side,
			OrderType = regMsg.OrderType,
		}, cancellationToken);
	}

	public override async ValueTask CancelOrder(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		if (cancelMsg.OrderId == null)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		var response = await _httpClient.CancelOrder(
			cancelMsg.SecurityId.ToSymbol(),
			cancelMsg.OrderId,
			null,
			cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = cancelMsg.SecurityId,
			ServerTime = response.TransactTime,
			TransactionId = cancelMsg.TransactionId,
			OriginalTransactionId = cancelMsg.OriginalTransactionId,
			OrderId = response.OrderId,
			OrderState = response.Status.ToOrderState(),
			PortfolioName = PortfolioName,
		}, cancellationToken);
	}

	public override async ValueTask CancelGroupOrder(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var responses = await _httpClient.CancelAllOrders(cancelMsg.SecurityId.ToSymbol(), cancellationToken);

		foreach (var response in responses)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = cancelMsg.SecurityId,
				ServerTime = response.TransactTime,
				TransactionId = cancelMsg.TransactionId,
				OriginalTransactionId = cancelMsg.OriginalTransactionId,
				OrderId = response.OrderId,
				OrderState = response.Status.ToOrderState(),
				PortfolioName = PortfolioName,
			}, cancellationToken);
		}
	}

	public override ValueTask ReplaceOrder(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		throw new NotSupportedException("Order replacement not supported by BingX");
	}

	public override async ValueTask PortfolioLookup(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		if (lookupMsg == null)
			throw new ArgumentNullException(nameof(lookupMsg));

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = PortfolioName,
			BoardCode = BoardCode,
			OriginalTransactionId = lookupMsg.TransactionId
		}, cancellationToken);

		var balances = await _httpClient.GetBalance(cancellationToken);

		if (balances is not null)
			await ProcessBalances(lookupMsg.TransactionId, balances, cancellationToken);
	}

	public override async ValueTask OrderStatus(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		if (statusMsg == null)
			throw new ArgumentNullException(nameof(statusMsg));

		var orders = await _httpClient.GetOpenOrders(null, cancellationToken);
		await ProcessOrders(statusMsg.TransactionId, orders, cancellationToken);
	}

	private async ValueTask ProcessOrders(long originTransId, IEnumerable<Order> orders, CancellationToken cancellationToken)
	{
		foreach (var order in orders)
		{
			if (!order.ClientOrderId.TryToTransId(out var transId))
				continue;

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = order.Symbol.ToStockSharp(BoardCode),
				ServerTime = originTransId > 0 ? order.Time : order.UpdateTime,
				TransactionId = originTransId > 0 ? transId : 0,
				OriginalTransactionId = originTransId > 0 ? originTransId : transId,
				OrderType = order.Type.ToOrderType(),
				OrderId = order.OrderId,
				OrderVolume = order.OriginalQuantity?.ToDecimal(),
				Balance = (order.OriginalQuantity - order.ExecutedQuantity)?.ToDecimal(),
				Side = order.Side.ToSide(),
				OrderPrice = order.Price?.ToDecimal() ?? 0,
				PortfolioName = PortfolioName,
				OrderState = order.Status.ToOrderState(),
				TimeInForce = order.TimeInForce?.ToTimeInForce(),
			}, cancellationToken);
		}
	}

	private async ValueTask ProcessBalances(long transId, IEnumerable<Balance> balances, CancellationToken cancellationToken)
	{
		foreach (var balance in balances)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = PortfolioName,
				SecurityId = balance.Asset.ToStockSharp(BoardCode),
				ServerTime = CurrentTime,
				OriginalTransactionId = transId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, balance.Free?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.BlockedValue, balance.Locked?.ToDecimal(), true), cancellationToken);
		}
	}

	private ValueTask SessionOnBalancesReceived(IEnumerable<Balance> balances, CancellationToken cancellationToken)
	{
		return ProcessBalances(0, balances, cancellationToken);
	}

	private ValueTask SessionOnOrdersReceived(IEnumerable<Order> orders, CancellationToken cancellationToken)
	{
		return ProcessOrders(0, orders, cancellationToken);
	}

	private async ValueTask SessionOnUserTradesReceived(IEnumerable<UserTrade> trades, CancellationToken cancellationToken)
	{
		foreach (var trade in trades)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				ServerTime = trade.TradeTime,
				TradeId = trade.TradeId,
				TradePrice = trade.Price?.ToDecimal(),
				TradeVolume = trade.Quantity?.ToDecimal(),
				OrderId = trade.OrderId,
				OriginSide = !trade.IsMaker ? Sides.Buy : Sides.Sell,
				Commission = trade.Commission?.ToDecimal(),
				CommissionCurrency = trade.CommissionAsset,
			}, cancellationToken);
		}
	}

	private ValueTask SessionOnTickerReceived(Ticker ticker, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ticker.Symbol.ToStockSharp(BoardCode),
			ServerTime = ticker.EventTime,
		}
		.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice?.ToDecimal())
		.TryAdd(Level1Fields.Change, ticker.PriceChangePercent?.ToDecimal())
		.TryAdd(Level1Fields.HighPrice, ticker.HighPrice?.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, ticker.LowPrice?.ToDecimal())
		.TryAdd(Level1Fields.Volume, ticker.Volume?.ToDecimal())
		.TryAdd(Level1Fields.BestBidPrice, ticker.BestBidPrice?.ToDecimal())
		.TryAdd(Level1Fields.BestBidVolume, ticker.BestBidQuantity?.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, ticker.BestAskPrice?.ToDecimal())
		.TryAdd(Level1Fields.BestAskVolume, ticker.BestAskQuantity?.ToDecimal())
		, cancellationToken);
	}

	private ValueTask SessionOnTradeReceived(Trade trade, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = trade.Symbol.ToStockSharp(BoardCode),
			ServerTime = trade.TradeTime,
			TradeId = trade.TradeId,
			TradePrice = trade.Price?.ToDecimal(),
			TradeVolume = trade.Quantity?.ToDecimal(),
			OriginSide = trade.IsBuyerMaker ? Sides.Sell : Sides.Buy,
		}, cancellationToken);
	}

	private ValueTask SessionOnOrderBookReceived(OrderBook book, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			ServerTime = book.EventTime,
			SecurityId = book.Symbol.ToStockSharp(BoardCode),
			Bids = book.Bids?.Select(e => new QuoteChange((decimal)e[0], (decimal)e[1])).ToArray(),
			Asks = book.Asks?.Select(e => new QuoteChange((decimal)e[0], (decimal)e[1])).ToArray(),
			State = QuoteChangeStates.SnapshotComplete,
		}, cancellationToken);
	}

	private ValueTask SessionOnCandleReceived(Candle candle, CancellationToken cancellationToken)
	{
		if (!_candleTransIds.TryGetValue($"{candle.Interval}_{candle.Symbol}", out var transId))
			return default;

		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OpenPrice = (decimal)candle.Open,
			ClosePrice = (decimal)candle.Close,
			HighPrice = (decimal)candle.High,
			LowPrice = (decimal)candle.Low,
			TotalVolume = (decimal)candle.Volume,
			OpenTime = candle.OpenTime,
			State = candle.IsClosed ? CandleStates.Finished : CandleStates.Active,
			OriginalTransactionId = transId,
		}, cancellationToken);
	}
}
