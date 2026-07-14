namespace StockSharp.BingX.Native.Futures;

using StockSharp.BingX.Native.Futures.Model;
using System.Runtime.CompilerServices;

class FuturesAdapter : NativeAdapter
{
	private readonly HttpClient _httpClient;
	private readonly SocketClient _socketClient;
	private readonly SynchronizedDictionary<string, long> _candleTransIds = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly SynchronizedDictionary<string, BookInfo<OrderBook>> _bookInfos = new(StringComparer.InvariantCultureIgnoreCase);

	public FuturesAdapter(BingXMessageAdapter adapter, Authenticator authenticator)
		: base(authenticator, adapter.TransactionIdGenerator, BoardCodes.BingXFut, SecurityTypes.Future)
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
		_socketClient.TickersReceived += SessionOnTickersReceived;
		_socketClient.TradesReceived += SessionOnTradesReceived;
		_socketClient.OrderBookReceived += SessionOnOrderBookReceived;
		_socketClient.CandlesReceived += SessionOnCandlesReceived;
		_socketClient.PositionsReceived += SessionOnPositionsReceived;
		_socketClient.OrdersReceived += SessionOnOrdersReceived;
		_socketClient.UserTradesReceived += SessionOnUserTradesReceived;
		_socketClient.BalancesReceived += SessionOnBalancesReceived;
	}

	private void UnsubscribePusherClient()
	{
		_socketClient.StateChanged -= SendOutConnectionStateAsync;
		_socketClient.Error -= SendOutErrorAsync;
		_socketClient.TickersReceived -= SessionOnTickersReceived;
		_socketClient.TradesReceived -= SessionOnTradesReceived;
		_socketClient.OrderBookReceived -= SessionOnOrderBookReceived;
		_socketClient.CandlesReceived -= SessionOnCandlesReceived;
		_socketClient.PositionsReceived -= SessionOnPositionsReceived;
		_socketClient.OrdersReceived -= SessionOnOrdersReceived;
		_socketClient.UserTradesReceived -= SessionOnUserTradesReceived;
		_socketClient.BalancesReceived -= SessionOnBalancesReceived;
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
				SecurityType = SecType,
				OriginalTransactionId = lookupMsg.TransactionId,
				PriceStep = Math.Pow(10, -symbol.PricePrecision).ToDecimal(),
				VolumeStep = symbol.TradeMinQuantity?.ToDecimal(),
				Decimals = symbol.QuantityPrecision,
				Multiplier = symbol.Size?.ToDecimal(),
			}.TryFillUnderlyingId(symbol.Asset);
		}
	}

	public override async ValueTask PortfolioLookup(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		if (lookupMsg == null)
			throw new ArgumentNullException(nameof(lookupMsg));

		if (!lookupMsg.IsSubscribe)
		{
			await _socketClient.UnsubscribePositions(cancellationToken);
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
			await ProcessBalances(lookupMsg.TransactionId, balances, cancellationToken);

		var positions = await _httpClient.GetPositions(cancellationToken);
		if (positions is not null)
			await ProcessPositions(lookupMsg.TransactionId, positions, cancellationToken);

		if (!lookupMsg.IsHistoryOnly())
		{
			await _socketClient.SubscribePositions(cancellationToken);
		}
	}

	public override async ValueTask OrderStatus(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		if (statusMsg == null)
			throw new ArgumentNullException(nameof(statusMsg));

		if (!statusMsg.IsSubscribe)
		{
			await _socketClient.UnsubscribeOrders(cancellationToken);
			return;
		}

		var orders = await _httpClient.GetOpenOrders(null, cancellationToken);
		await ProcessOrders(statusMsg.TransactionId, orders, cancellationToken);

		if (!statusMsg.IsHistoryOnly())
		{
			await _socketClient.SubscribeOrders(cancellationToken);
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

					foreach (var candle in candles.OrderBy(c => c.Time))
					{
						var time = candle.Time;

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

						startTime = (long)candle.Time.ToUnix(false) + 1;
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

		switch (regMsg.OrderType)
		{
			case null:
			case OrderTypes.Limit:
			case OrderTypes.Market:
				break;
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		var response = await _httpClient.PlaceOrder(
			symbol,
			regMsg.Side.ToNative(),
			regMsg.OrderType.ToNative(regMsg.Price),
			regMsg.Volume,
			regMsg.OrderType == OrderTypes.Market ? null : regMsg.Price,
			regMsg.TimeInForce.ToNative(),
			regMsg.TransactionId.ToRequestId(),
			regMsg.PositionEffect == OrderPositionEffects.CloseOnly,
			cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = regMsg.SecurityId,
			ServerTime = response.UpdateTime,
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
			PositionEffect = regMsg.PositionEffect,
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
			ServerTime = response.UpdateTime,
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
				ServerTime = response.UpdateTime,
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
		throw new NotSupportedException("Order replacement not supported by BingX Futures");
	}

	private async ValueTask ProcessPositions(long transId, IEnumerable<Position> positions, CancellationToken cancellationToken)
	{
		foreach (var position in positions)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				SecurityId = position.Symbol.ToStockSharp(BoardCode),
				PortfolioName = PortfolioName,
				ServerTime = position.UpdateTime ?? CurrentTime,
				OriginalTransactionId = transId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, position.PositionAmount.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.AveragePrice, position.EntryPrice?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedProfit?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.Leverage, position.Leverage?.ToDecimal(), true)
			, cancellationToken);
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
				ServerTime = balance.UpdateTime ?? CurrentTime,
				OriginalTransactionId = transId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, balance.AvailableBalance?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.BlockedValue, (balance.BalanceAmount - balance.AvailableBalance)?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, balance.CrossUnrealizedPnl?.ToDecimal(), true)
			, cancellationToken);
		}
	}

	private async ValueTask ProcessOrders(long originTransId, IEnumerable<Order> orders, CancellationToken cancellationToken)
	{
		foreach (var order in orders)
		{
			if (!order.ClientOrderId.TryToTransId(out var transId))
				transId = 0;

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = order.Symbol.ToStockSharp(BoardCode),
				ServerTime = order.UpdateTime != default ? order.UpdateTime : (order.Time != default ? order.Time : CurrentTime),
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
				PositionEffect = order.ReduceOnly ? OrderPositionEffects.CloseOnly : null,
			}, cancellationToken);
		}
	}

	private ValueTask SessionOnPositionsReceived(IEnumerable<Position> positions, CancellationToken cancellationToken)
	{
		return ProcessPositions(0, positions, cancellationToken);
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
				ServerTime = trade.Time,
				TradeId = trade.Id,
				TradePrice = trade.Price?.ToDecimal(),
				TradeVolume = trade.Quantity?.ToDecimal(),
				OrderId = trade.OrderId,
				OriginSide = trade.Side.ToSide(),
				IsMarketMaker = trade.IsMaker,
				Commission = trade.Commission?.ToDecimal(),
				CommissionCurrency = trade.CommissionAsset,
			}, cancellationToken);
		}
	}

	private async ValueTask SessionOnTickersReceived(IEnumerable<Ticker> tickers, CancellationToken cancellationToken)
	{
		foreach (var ticker in tickers)
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = ticker.Symbol.ToStockSharp(BoardCode),
				ServerTime = ticker.CloseTime,
			}
			.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice?.ToDecimal())
			.TryAdd(Level1Fields.Change, ticker.PriceChangePercent?.ToDecimal())
			.TryAdd(Level1Fields.HighPrice, ticker.HighPrice?.ToDecimal())
			.TryAdd(Level1Fields.LowPrice, ticker.LowPrice?.ToDecimal())
			.TryAdd(Level1Fields.Volume, ticker.Volume?.ToDecimal())
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
				SecurityId = trade.Symbol.ToStockSharp(BoardCode),
				ServerTime = trade.TradeTime,
				TradeId = trade.TradeId,
				TradePrice = trade.Price?.ToDecimal(),
				TradeVolume = trade.Quantity?.ToDecimal(),
				OriginSide = trade.IsBuyerMaker ? Sides.Sell : Sides.Buy,
			}, cancellationToken);
		}
	}

	private ValueTask SessionOnOrderBookReceived(OrderBook book, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			ServerTime = book.EventTime,
			SecurityId = book.Symbol.ToStockSharp(BoardCode),
			Bids = book.Bids?.Select(e => new QuoteChange((decimal)e.Price, (decimal)e.Size)).ToArray(),
			Asks = book.Asks?.Select(e => new QuoteChange((decimal)e.Price, (decimal)e.Size)).ToArray(),
			State = QuoteChangeStates.SnapshotComplete,
		}, cancellationToken);
	}

	private async ValueTask SessionOnCandlesReceived(IEnumerable<Candle> candles, CancellationToken cancellationToken)
	{
		foreach (var candle in candles)
		{
			if (!_candleTransIds.TryGetValue($"{candle.Interval}_{candle.Symbol}", out var transId))
				continue;

			await SendOutMessageAsync(new TimeFrameCandleMessage
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
}
