namespace StockSharp.Huobi.Native.Usdt;

using StockSharp.Huobi.Native.Usdt.Model;

class UsdtAdapter(HuobiMessageAdapter parent, Authenticator authenticator, string domain) : NativeAdapter(parent, authenticator, domain)
{
	private HttpClient _httpClient;
	private PusherClient _pusherClient;

	//private readonly SynchronizedDictionary<string, (string, ContractTypes)> _contractInfo = new(StringComparer.InvariantCultureIgnoreCase);
	private static readonly string[] _symbols = ["BTC-USDT", "ETH-USDT"];

    private void SubscribePusherClient()
	{
		_pusherClient.StateChanged += SendOutConnectionStateAsync;
		_pusherClient.Error += SessionOnPusherError;
		_pusherClient.Ping += SessionOnPing;
		_pusherClient.SubscriptionResponse += SessionOnSubscriptionResponse;

		_pusherClient.TickerChanged += SessionOnTickerChanged;
		_pusherClient.BestChanged += SessionOnBestChanged;
		_pusherClient.OrderBookChanged += SessionOnOrderBookChanged;
		_pusherClient.NewTrades += SessionOnNewTrades;
		_pusherClient.NewCandle += SessionOnNewCandle;
		_pusherClient.CandlesReceived += SessionOnCandlesReceived;
		_pusherClient.OrderChanged += SessionOnOrderChanged;
		_pusherClient.BalancesChanged += SessionOnBalancesChanged;
		_pusherClient.PositionsChanged += SessionOnPositionsChanged;
	}

	private void UnsubscribePusherClient()
	{
		_pusherClient.StateChanged -= SendOutConnectionStateAsync;
		_pusherClient.Error -= SessionOnPusherError;
		_pusherClient.Ping -= SessionOnPing;
		_pusherClient.SubscriptionResponse -= SessionOnSubscriptionResponse;

		_pusherClient.TickerChanged -= SessionOnTickerChanged;
		_pusherClient.BestChanged -= SessionOnBestChanged;
		_pusherClient.OrderBookChanged -= SessionOnOrderBookChanged;
		_pusherClient.NewTrades -= SessionOnNewTrades;
		_pusherClient.NewCandle -= SessionOnNewCandle;
		_pusherClient.CandlesReceived -= SessionOnCandlesReceived;
		_pusherClient.OrderChanged -= SessionOnOrderChanged;
		_pusherClient.BalancesChanged -= SessionOnBalancesChanged;
		_pusherClient.PositionsChanged -= SessionOnPositionsChanged;
	}

	protected override async ValueTask OnResetAsync(CancellationToken cancellationToken)
	{
		if (_httpClient != null)
		{
			try
			{
				_httpClient.Dispose();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			_httpClient = null;
		}

		if (_pusherClient != null)
		{
			try
			{
				UnsubscribePusherClient();
				_pusherClient.Dispose();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			_pusherClient = null;
		}
	}

	public override async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_httpClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		if (_pusherClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_httpClient = new HttpClient(Authenticator, Domain) { Parent = Parent };
		_pusherClient = new PusherClient(Authenticator, Domain, Parent.ReConnectionSettings.WorkingTime) { Parent = Parent };

		SubscribePusherClient();
		await _pusherClient.ConnectAsync(Parent.IsMarketData(), Parent.IsTransactional(), cancellationToken);
	}

	public override ValueTask DisconnectAsync(CancellationToken cancellationToken)
	{
		if (_httpClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		if (_pusherClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		_httpClient.Dispose();
		_httpClient = null;

		_pusherClient.Disconnect();
		return default;
	}

	public override async ValueTask SuspendedAsync(ProcessSuspendedMessage message, CancellationToken cancellationToken)
	{
		var tuple = (Tuple<SecurityId, int>)message.Arg;
		await _pusherClient.RequestOrderBook(tuple.Item1, tuple.Item2, AddExtraRequest(), cancellationToken);
	}

	public override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		if (!timeMsg.OriginalTransactionId.IsEmpty())
			await _pusherClient.Pong(timeMsg.OriginalTransactionId, cancellationToken);
	}

	public override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		var secTypes = lookupMsg.GetSecurityTypes();

		foreach (var contract in await _httpClient.GetContractsAsync(cancellationToken))
		{
			var secMsg = new SecurityMessage
			{
				SecurityId = contract.ContractCode.ToStockSharp(true),
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityType = SecurityTypes.Future,
				UnderlyingSecurityType = SecurityTypes.Swap,
				PriceStep = (decimal)contract.PriceTick,
				Multiplier = (decimal)contract.ContractSize,
				ExpiryDate = contract.DeliveryDate.TryToDateTime("yyyyMMdd"),
				SettlementDate = contract.SettlementDate,
				IssueDate = contract.CreateDate.TryToDateTime("yyyyMMdd"),
			}.TryFillUnderlyingId(contract.Asset.ToUpperInvariant());

			if (secMsg.IsMatch(lookupMsg, secTypes))
				await SendOutMessageAsync(secMsg, cancellationToken);

			//_contractInfo[contract.ContractCode] = (contract.Asset, ToContractType(contract.ContractType));
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	public override async ValueTask MarketDataAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;
		var securityId = mdMsg.SecurityId;

		const int maxCount = 2000;

		if (mdMsg.DataType2 == DataType.Level1)
		{
			await SendSubscriptionReplyAsync(transId, cancellationToken);

			if (mdMsg.IsSubscribe)
			{
				await _pusherClient.SubscribeTicker(securityId, transId, cancellationToken);
				await _pusherClient.SubscribeBest(securityId, AddExtraRequest(), cancellationToken);

				await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			}
			else
			{
				await _pusherClient.UnSubscribeTicker(securityId, transId, cancellationToken);
				await _pusherClient.UnSubscribeBest(securityId, AddExtraRequest(), cancellationToken);
			}
		}
		else if (mdMsg.DataType2 == DataType.MarketDepth)
		{
			await SendSubscriptionReplyAsync(transId, cancellationToken);

			var depth = mdMsg.MaxDepth ?? 20;

			if (mdMsg.IsSubscribe)
			{
				await _pusherClient.SubscribeOrderBook(securityId, depth, transId, cancellationToken);

				await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			}
			else
				await _pusherClient.UnSubscribeOrderBook(securityId, depth, transId, cancellationToken);
		}
		else if (mdMsg.DataType2 == DataType.Ticks)
		{
			await SendSubscriptionReplyAsync(transId, cancellationToken);

			if (mdMsg.IsSubscribe)
			{
				if (mdMsg.To != null)
				{
					var trades = await _httpClient.GetTradesAsync(securityId.SecurityCode, (int?)mdMsg.Count ?? maxCount, cancellationToken);

					foreach (var tick in trades.OrderBy(t => t.Timestamp))
					{
						await ProcessTickAsync(securityId, tick, tick.TradeId, transId, cancellationToken);
					}
				}
				else
					await _pusherClient.SubscribeTrades(securityId, transId, cancellationToken);

				await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			}
			else
				await _pusherClient.UnSubscribeTrades(securityId, transId, cancellationToken);
		}
		else if (mdMsg.DataType2.IsTFCandles)
		{
			await SendSubscriptionReplyAsync(transId, cancellationToken);

			var tf = mdMsg.GetTimeFrame();
			var tfName = tf.ToNative();

			if (mdMsg.IsSubscribe)
			{
				if (mdMsg.To != null)
					await _pusherClient.RequestCandles(securityId, tf, transId, (long?)mdMsg.From?.ToUnix(), (long?)mdMsg.To?.ToUnix(), cancellationToken);
				else
				{
					await _pusherClient.SubscribeCandles(securityId, tf, transId, cancellationToken);
					await SendSubscriptionResultAsync(mdMsg, cancellationToken);
				}
			}
			else
				await _pusherClient.UnSubscribeCandles(securityId, tf, transId, cancellationToken);
		}
		else
		{
			await SendSubscriptionNotSupportedAsync(transId, cancellationToken);
		}
	}

	public override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var symbol = regMsg.SecurityId.ToSymbol();
		var condition = (HuobiOrderCondition)regMsg.Condition;

		switch (regMsg.OrderType)
		{
			case null:
			case OrderTypes.Limit:
			case OrderTypes.Market:
				break;
			case OrderTypes.Conditional:
			{
				if (!condition.IsWithdraw)
				{
					break;
					//throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
				}

				throw new NotSupportedException("Withdraw doesn't support.");
			}
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		var isMarket = regMsg.OrderType == OrderTypes.Market;
		var price = isMarket ? (decimal?)null : regMsg.Price;

		await _httpClient.RegisterOrderAsync(regMsg.TransactionId, symbol, regMsg.PositionEffect == OrderPositionEffects.CloseOnly, regMsg.TimeInForce.ToFutureNative(condition?.Opponent, regMsg.PostOnly, condition?.Optimal), regMsg.Side.ToNative(), price, regMsg.Volume, regMsg.Leverage ?? 10, condition?.Offset?.ToOffset() ?? "open", cancellationToken);
	}

	public override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		await _httpClient.CancelOrderAsync(cancelMsg.OriginalTransactionId, cancelMsg.SecurityId.SecurityCode, cancellationToken);
	}

	public override async ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		await _httpClient.BatchCancelOrderAsync(cancelMsg.GetUnderlyingCode(), cancelMsg.SecurityId.SecurityCode, cancelMsg.Side?.ToNative(), cancellationToken);
	}

	public override async ValueTask OrderStatusAsync(OrderStatusMessage message, CancellationToken cancellationToken)
	{
		if (!message.IsSubscribe)
		{
			await _pusherClient.UnSubscribeOrders(message.TransactionId, cancellationToken);
		}

		foreach (var symbol in _symbols)
		{
			var orders = await _httpClient.GetOpenOrdersAsync(symbol, size: 50, cancellationToken: cancellationToken);

			foreach (var order in orders)
			{
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					HasOrderInfo = true,
					ServerTime = order.CreatedAt,
					SecurityId = order.ContractCode.ToStockSharp(true),
					TransactionId = order.ClientOrderId,
					OriginalTransactionId = message.TransactionId,
					OrderId = order.OrderId,
					OrderVolume = (decimal)order.Volume,
					Balance = (decimal?)(order.Volume - order.TradeVolume),
					OrderType = order.OrderPriceType.ToFutureOrderType(out var tif, out var opponent, out var postOnly, out var optimal),
					Side = order.Direction.ToSide(),
					TimeInForce = tif,
					OrderPrice = (decimal?)order.Price ?? 0,
					PortfolioName = PortfolioName,
					OrderState = order.Status.ToFuturesOrderState(),
					Commission = (decimal?)order.Fee,
					CommissionCurrency = order.FeeAsset,
					AveragePrice = (decimal?)order.TradeAvgPrice,
					Leverage = order.LeverRate,
					PostOnly = postOnly,
					Condition = new HuobiOrderCondition
					{
						Offset = order.Offset.ToOpenClose(),
						Opponent = opponent,
						Optimal = optimal,
					}
				}, cancellationToken);

				await ProcessOwnTradesAsync([], order.Trades, order.ClientOrderId, cancellationToken);
			}
		}

		if (message.To == null)
			await _pusherClient.SubscribeOrders(message.TransactionId, cancellationToken);

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	public override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage message, CancellationToken cancellationToken)
	{
		if (!message.IsSubscribe)
		{
			await _pusherClient.UnSubscribeAccounts(message.TransactionId, cancellationToken);
		}

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = PortfolioName,
			BoardCode = BoardCodes.Huobi,
			OriginalTransactionId = message.TransactionId
		}, cancellationToken);

		var now = DateTime.UtcNow;

		var balances = await _httpClient.GetBalancesAsync(cancellationToken);
		foreach (var balance in balances)
		{
			await ProcessBalanceAsync(now, balance, cancellationToken);
		}

		var positions = await _httpClient.GetPositionsAsync(cancellationToken);
		foreach (var position in positions)
		{
			await ProcessPositionAsync(now, position, cancellationToken);
		}

		if (message.To == null)
			await _pusherClient.SubscribeAccounts(message.TransactionId, cancellationToken);

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private ValueTask ProcessCandleAsync(SecurityId securityId, TimeSpan tf, Ohlc candle, long originalTransactionId, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = securityId,
			TypedArg = tf,
			OpenPrice = (decimal?)candle.Open ?? 0,
			ClosePrice = (decimal?)candle.Close ?? 0,
			HighPrice = (decimal?)candle.High ?? 0,
			LowPrice = (decimal?)candle.Low ?? 0,
			TotalVolume = (decimal?)candle.Amount ?? 0,
			OpenTime = candle.OpenTime,
			TotalTicks = candle.Count,
			State = CandleStates.Finished,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private ValueTask ProcessTickAsync(SecurityId securityId, TradeBase tick, long id, long originalTransactionId, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = securityId,

			TradeId = id,

			TradePrice = (decimal?)tick.Price,
			TradeVolume = (decimal?)tick.Amount,
			ServerTime = tick.Timestamp,
			OriginSide = tick.Direction.ToSide(),
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private async ValueTask SessionOnCandlesReceived(SecurityId securityId, TimeSpan timeFrame, long transactionId, Ohlc[] candles, CancellationToken cancellationToken)
	{
		foreach (var candle in candles)
		{
			await ProcessCandleAsync(securityId, timeFrame, candle, transactionId, cancellationToken);
		}

		await SendSubscriptionFinishedAsync(transactionId, cancellationToken);
	}

	private ValueTask SessionOnNewCandle(DateTime timestamp, SecurityId securityId, TimeSpan timeFrame, long transactionId, Ohlc candle, CancellationToken cancellationToken)
	{
		return ProcessCandleAsync(securityId, timeFrame, candle, transactionId, cancellationToken);
	}

	private async ValueTask SessionOnNewTrades(DateTime timestamp, SecurityId securityId, SocketTrade[] trades, CancellationToken cancellationToken)
	{
		foreach (var tick in trades)
		{
			await ProcessTickAsync(securityId, tick, tick.TradeId, 0, cancellationToken);
		}
	}

	private ValueTask SessionOnOrderBookChanged(DateTime timestamp, SecurityId securityId, OrderBook book, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = securityId,
			Bids = book.Bids?.Select(p => new QuoteChange((decimal)p.Price, (decimal)p.Size)).ToArray() ?? [],
			Asks = book.Asks?.Select(p => new QuoteChange((decimal)p.Price, (decimal)p.Size)).ToArray() ?? [],
			ServerTime = book.Timestamp,
			State = book.Event == "snapshot" ? QuoteChangeStates.SnapshotComplete : QuoteChangeStates.Increment,
		}, cancellationToken);
	}

	private ValueTask SessionOnBestChanged(DateTime timestamp, SecurityId securityId, Best l1, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = securityId,
			ServerTime = l1.Timestamp,
		}
		.TryAdd(Level1Fields.BestBidPrice, (decimal?)l1.Bid?.Price)
		.TryAdd(Level1Fields.BestBidVolume, (decimal?)l1.Bid?.Size)
		.TryAdd(Level1Fields.BestAskPrice, (decimal?)l1.Ask?.Price)
		.TryAdd(Level1Fields.BestAskVolume, (decimal?)l1.Ask?.Size)
		, cancellationToken);
	}

	private ValueTask SessionOnTickerChanged(DateTime timestamp, SecurityId securityId, Ticker l1, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = securityId,
			ServerTime = timestamp,
		}
		.TryAdd(Level1Fields.OpenPrice, (decimal?)l1.Open)
		.TryAdd(Level1Fields.HighPrice, (decimal?)l1.High)
		.TryAdd(Level1Fields.LowPrice, (decimal?)l1.Low)
		.TryAdd(Level1Fields.ClosePrice, (decimal?)l1.Close)
		.TryAdd(Level1Fields.Volume, (decimal?)l1.Amount)
		.TryAdd(Level1Fields.TradesCount, l1.Count)
		, cancellationToken);
	}

	private async ValueTask ProcessOwnTradesAsync(HashSet<long> tradeIds, IEnumerable<OwnTrade> trades, long origTransId, CancellationToken cancellationToken)
	{
		foreach (var trade in trades)
		{
			if (!tradeIds.Add(trade.TradeId))
				continue;

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				ServerTime = trade.CreatedAt,
				OriginalTransactionId = origTransId,
				TradeId = trade.TradeId,
				TradePrice = (decimal?)trade.Price,
				TradeVolume = (decimal?)trade.Volume,
				Commission = (decimal?)trade.Fee,
				Initiator = trade.Role.ToInitiator(),
			}, cancellationToken);
		}
	}

	private async ValueTask SessionOnOrderChanged(DateTime timestamp, SocketOrder order, CancellationToken cancellationToken)
	{
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = order.CanceledAt ?? timestamp,
			OriginalTransactionId = order.ClientOrderId,
			OrderId = order.OrderId,
			Balance = (decimal?)(order.Volume - order.TradeVolume),
			OrderState = order.Status.ToFuturesOrderState(),
			Commission = (decimal?)order.Fee,
			CommissionCurrency = order.FeeAsset,
			PnL = (decimal?)(order.RealProfit ?? order.Profit),
			AveragePrice = (decimal?)order.TradeAvgPrice,
		}, cancellationToken);

		if (order.Trades?.Length > 0)
		{
			foreach (var trade in order.Trades)
			{
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					TradeStringId = trade.Id,
					ServerTime = trade.CreatedAt,
					OriginalTransactionId = order.ClientOrderId,
					TradePrice = (decimal)trade.Price,
					TradeVolume = (decimal)trade.Volume,
					Commission = (decimal?)trade.Fee,
					CommissionCurrency = trade.FeeAsset,
					PnL = (decimal?)(trade.RealProfit ?? trade.Profit),
					IsMarketMaker = trade.Role.ToInitiator(),
				}, cancellationToken);
			}
		}
	}

	private async ValueTask SessionOnPositionsChanged(DateTime timestamp, IEnumerable<Position> positions, CancellationToken cancellationToken)
	{
		foreach (var position in positions)
		{
			await ProcessPositionAsync(timestamp, position, cancellationToken);
		}
	}

	private ValueTask ProcessPositionAsync(DateTime timestamp, Position position, CancellationToken cancellationToken)
	{
		var side = position.Direction.ToSide();

		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = PortfolioName,
			SecurityId = position.ContractCode.ToStockSharp(true),
			ServerTime = timestamp,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, (decimal?)position.Available * (side == Sides.Sell ? -1 : 1), true)
		.TryAdd(PositionChangeTypes.BlockedValue, (decimal?)position.Frozen, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, (decimal?)position.CostOpen, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, (decimal?)position.ProfitUnreal, true)
		.TryAdd(PositionChangeTypes.RealizedPnL, (decimal?)position.Profit, true)
		.TryAdd(PositionChangeTypes.VariationMargin, (decimal?)position.PositionMargin, true)
		.TryAdd(PositionChangeTypes.Leverage, (decimal?)position.LeverRate, true)
		, cancellationToken);
	}

	private async ValueTask SessionOnBalancesChanged(DateTime timestamp, IEnumerable<Balance> balances, CancellationToken cancellationToken)
	{
		foreach (var balance in balances)
		{
			await ProcessBalanceAsync(timestamp, balance, cancellationToken);
		}
	}

	private ValueTask ProcessBalanceAsync(DateTime timestamp, Balance balance, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = PortfolioName,
			SecurityId = balance.Asset.ToStockSharp(true),
			ServerTime = timestamp,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, (decimal?)balance.MarginAvailable, true)
		.TryAdd(PositionChangeTypes.BlockedValue, (decimal?)balance.MarginFrozen, true)
		.TryAdd(PositionChangeTypes.AveragePrice, (decimal?)balance.LiquidationPrice, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, (decimal?)balance.ProfitUnreal, true)
		.TryAdd(PositionChangeTypes.RealizedPnL, (decimal?)balance.ProfitReal, true)
		.TryAdd(PositionChangeTypes.VariationMargin, (decimal?)balance.MarginAvailable, true)
		.TryAdd(PositionChangeTypes.Leverage, (decimal?)balance.LeverRate, true)
		, cancellationToken);
	}
}