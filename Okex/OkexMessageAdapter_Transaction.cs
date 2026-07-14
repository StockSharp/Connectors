namespace StockSharp.Okex;

public partial class OkexMessageAdapter
{
	private string PortfolioName => nameof(Okex) + "_" + Key.ToId();

	private readonly SynchronizedSet<long> _processedTrades = [];
	private readonly SynchronizedDictionary<string, long> _transIdByOrdId = [];
	private readonly SynchronizedList<(IOkexTransaction trans, long transId, long origTransId)> _transactionsBuffer = [];
	private bool? _ordersOnline;

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken token)
	{
		var instrumentId = regMsg.SecurityId.ToNative();
		var condition = (OkexOrderCondition)regMsg.Condition;

		switch (regMsg.OrderType)
		{
			case null:
			case OrderTypes.Limit:
			case OrderTypes.Market:
				break;
			case OrderTypes.Conditional:
			{
				if (!condition.IsWithdraw)
					throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));

				var withdrawId = await _httpClient.WithdrawAsync(instrumentId, AdminPassword, regMsg.Volume, condition.WithdrawInfo, token);

				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OrderId = withdrawId,
					ServerTime = CurrentTime,
					OriginalTransactionId = regMsg.TransactionId,
					OrderState = OrderStates.Done,
					HasOrderInfo = true,
				}, token);

				return;
			}
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		var isMarket = regMsg.OrderType == OrderTypes.Market;
		var price = isMarket ? (decimal?)null : regMsg.Price;

		await _privatePusherClient.PlaceOrder(
			ToClientOrderId(regMsg.TransactionId), instrumentId, regMsg.Side.ToNative(), price,
			regMsg.Volume, price.GetNativeOrderType(regMsg.PostOnly, regMsg.TimeInForce, condition?.MatchPrice),
			regMsg.PositionEffect?.ToNative(), regMsg.MarginMode.ToNative(regMsg.SecurityType == SecurityTypes.CryptoCurrency, condition?.Leading ?? false), token);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		var instrumentId = replaceMsg.SecurityId.ToNative();
		return _privatePusherClient.AmendOrder(ToClientOrderId(replaceMsg.OriginalTransactionId), instrumentId, ToClientOrderId(replaceMsg.TransactionId), replaceMsg.Price.DefaultAsNull(), replaceMsg.Volume.DefaultAsNull(), cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken token)
	{
		var instrumentId = cancelMsg.SecurityId.ToNative();
		return _privatePusherClient.CancelOrder(instrumentId, ToClientOrderId(cancelMsg.TransactionId), ToClientOrderId(cancelMsg.OriginalTransactionId), token);
	}

	private bool CheckPosMode(string posMode)
	{
		if(posMode.EqualsIgnoreCase("net"))
			return true;

		var err = $"Unexpected account position mode '{posMode}', only 'net' mode is supported.";
		this.AddErrorLog(err);

		return false;
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage message, CancellationToken token)
	{
		if (message == null)
			throw new ArgumentNullException(nameof(message));

		await SendSubscriptionReplyAsync(message.TransactionId, token);

		if (!message.IsSubscribe)
		{
			await _privatePusherClient.UnsubscribePortfolio(message.OriginalTransactionId, token);
			return;
		}

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = PortfolioName,
			BoardCode = BoardCodes.Okex,
			OriginalTransactionId = message.TransactionId
		}, token);

		var positions = await _httpClient.GetPositionsAsync(token);

		foreach (var pos in positions)
		{
			if(!CheckPosMode(pos.PosSide))
				continue;

			//var inst = await GetInstrumentAsync(pos.InstrumentId.ToStockSharp(), token, false);
			//if (inst == null)
			//{
			//	this.AddErrorLog("error processing position for instrument '{0}', pos: {1}", pos.InstrumentId, pos);
			//	continue;
			//}

			var posValue = pos.Position ?? 0m;

			//if (pos.InstType.ToSecurityType() == Native.Extensions.Margin)
			//{
			//	if(!pos.PosCcy.EqualsIgnoreCase(inst.BaseCurrency))
			//		posValue = -Math.Abs(posValue);
			//}

			await SendOutMessageAsync(new PositionChangeMessage
			{
				SecurityId = pos.InstrumentId.ToStockSharp(),
				PortfolioName = PortfolioName,
				ServerTime = pos.UpdatedAt ?? pos.CreateAt ?? CurrentTime,
			}
			.TryAdd(PositionChangeTypes.AveragePrice, pos.AveragePrice, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, pos.UnrealizedPnL, true)
			.TryAdd(PositionChangeTypes.CurrentValue, posValue, true)
			.TryAdd(PositionChangeTypes.Leverage, pos.Leverage.To<decimal?>()), token);
		}

		if (!message.IsHistoryOnly())
			await _privatePusherClient.SubscribePortfolio(message.TransactionId, token);

		await SendSubscriptionResultAsync(message, token);
	}

	private const string _clientOrderIdPrefix = "SS";

	private static string ToClientOrderId(long transactionId) => _clientOrderIdPrefix + transactionId;

	private long GetOrCreateTransactionId(OkexOrder order) => GetOrCreateTransactionId(order.ClientOrderId, order.Id);

	private long GetOrCreateTransactionId(string clientOrderId, string orderId)
	{
		if (clientOrderId != null && clientOrderId.StartsWith(_clientOrderIdPrefix) && long.TryParse(clientOrderId.Substring(2), out var transId))
			return transId;

		using (_transIdByOrdId.EnterScope())
		{
			if(_transIdByOrdId.TryGetValue(orderId, out transId))
				return transId;

			transId = TransactionIdGenerator.GetNextId();
			_transIdByOrdId[orderId] = transId;
		}

		return transId;
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage message, CancellationToken token)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, token);

		if (!message.IsSubscribe)
		{
			await _privatePusherClient.UnsubscribeOrders(message.OriginalTransactionId, token);
			return;
		}

		if (_ordersOnline == false)
			throw new InvalidOperationException("order status request is in progress");

		_ordersOnline = false;

		var count = message.Count == null ? null : (int?)message.Count.Value.Min(int.MaxValue);

		this.AddVerboseLog("requesting recent orders/trades");

		var recentOrdersTask  = _httpClient.GetRecentOrdersAsync(count ?? RecentOrdersRequestLimit, token);
		var openOrdersTask    = _httpClient.GetOpenOrdersAsync(token);
		var fillsTask         = _httpClient.GetFillsAsync(count ?? RecentTradesRequestLimit, token);

		await Task.WhenAll(recentOrdersTask, openOrdersTask, fillsTask);
		var recentOrders = await recentOrdersTask;
		var openOrders   = await openOrdersTask;
		var fills        = await fillsTask;
		this.AddVerboseLog($"all recent orders/trades requests are completed. got {recentOrders.Length} recent orders, {openOrders.Length} active orders, {fills.Length} recent trades");

		foreach (var o in recentOrders)
			await ProcessOkexTransaction(o, GetOrCreateTransactionId(o), message.TransactionId, token);
		foreach (var o in openOrders)
			await ProcessOkexTransaction(o, GetOrCreateTransactionId(o), message.TransactionId, token);
		foreach (var f in fills)
			await ProcessOkexTransaction(f, 0, message.TransactionId, token);

		_ordersOnline = true;
		await FlushTransactions(token);
		this.AddVerboseLog("flush orders complete");

		if (!message.IsHistoryOnly())
			await _privatePusherClient.SubscribeOrders(message.TransactionId, token);

		await SendSubscriptionResultAsync(message, token);
	}

	private async ValueTask FlushTransactions(CancellationToken cancellationToken)
	{
		var sorted = _transactionsBuffer.OrderBy((tuple1, tuple2) =>
		{
			var t1 = tuple1.trans.Time;
			var t2 = tuple2.trans.Time;

			return
				t1 == t2   ? 0 :
				t1 == null ? 1 :
				t2 == null ? -1 :
				t1 < t2    ? 1 : -1;
		});

		foreach (var t in sorted)
			await ProcessOkexTransaction(t.trans, t.transId, t.origTransId, cancellationToken);
		_transactionsBuffer.Clear();
	}

	private async ValueTask ProcessOkexTransaction(IOkexTransaction trans, long transId, long origTransId, CancellationToken cancellationToken)
	{
		if (_ordersOnline != true)
		{
			_transactionsBuffer.Add((trans, transId, origTransId));
			return;
		}

		switch (trans)
		{
			case OkexOrder order:
				await ProcessOrder(order, transId, origTransId, cancellationToken);
				break;

			case OwnTrade trade:
				await ProcessOwnTrade(trade, origTransId, cancellationToken);
				break;

			default:
				throw new ArgumentOutOfRangeException(nameof(trans));
		}
	}

	private ValueTask ProcessOwnTrade(OwnTrade trade, long origTransId, CancellationToken cancellationToken)
	{
		if (!_processedTrades.TryAdd(trade.TradeId))
			return default;

		return SendOutMessageAsync(new ExecutionMessage
		{
			SecurityId = trade.InstrumentId.ToStockSharp(),
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = origTransId,
			ServerTime = trade.Time ?? CurrentTime,
			OrderStringId = trade.OrderId,
			TradeId = trade.TradeId,
			TradePrice = trade.Price,
			TradeVolume = trade.Size,
			Commission = trade.Fee,
			CommissionCurrency = trade.FeeCurrency,
			OriginSide = trade.Side?.ToSide(),
		}, cancellationToken);
	}

	private async ValueTask ProcessOrder(OkexOrder order, long transId, long origTransId, CancellationToken cancellationToken)
	{
		if (order.InstrumentId?.ToStockSharp() is not SecurityId secId)
			return;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = order.Timestamp ?? order.CreatedAt ?? CurrentTime,
			SecurityId = secId,
			TransactionId = transId,
			OriginalTransactionId = origTransId,
			OrderStringId = order.Id,
			OrderVolume = order.GetSize(),
			Balance = order.GetBalance(),
			Side = order.Side.ToSide() ?? Sides.Buy,
			OrderType = order.OrderType.ToOrderType(),
			OrderPrice = order.Price ?? 0,
			PortfolioName = PortfolioName,
			OrderState = order.State.ToOrderState(),
			MarginMode = order.TdMode.ToMarginMode(),
			Commission = order.Fee,
			AveragePrice = order.AveragePrice,
			TimeInForce = order.OrderType.ToTimeInForce(out var postOnly),
			PostOnly = postOnly,
			PositionEffect = order.ReduceOnly?.ToPositionEffect(),
			Condition = new OkexOrderCondition { Leverage = order.Leverage }
		}, cancellationToken);

		OwnTrade TryGetTrade()
		{
			if (order.LastFillPrice == null || order.LastFillSize == null || order.LastTradeId == null)
				return null;

			return new()
			{
				InstType = order.InstType,
				InstrumentId = order.InstrumentId,
				TradeId = order.LastTradeId.Value,
				OrderId = order.Id,
				ClientOrderId = order.ClientOrderId,
				Price = order.LastFillPrice.Value,
				Size = order.LastFillSize.Value,
				Fee = order.LastFillFee ?? 0m,
				FeeCurrency = order.LastFillFeeCcy,
				Time = order.LastFillTime
			};
		}

		var trade = TryGetTrade();

		if(trade != null)
			await ProcessOwnTrade(trade, GetOrCreateTransactionId(trade.ClientOrderId, trade.OrderId), cancellationToken);
	}

	private async ValueTask SessionOnOrderChanged(OkexOrder order, Exception error, CancellationToken cancellationToken)
	{
		var originTransId = GetOrCreateTransactionId(order);

		if (error is null)
			await ProcessOkexTransaction(order, 0, originTransId, cancellationToken);
		else
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				Error = error,
				OriginalTransactionId = originTransId
			}, cancellationToken);
		}
	}

	private ValueTask SessionOnPositionChanged(OkexPosition pos, CancellationToken cancellationToken)
	{
		var secId = pos.InstrumentId.ToStockSharp();
		var time = pos.UpdatedAt ?? pos.CreateAt ?? CurrentTime;

		return SendOutMessageAsync(new PositionChangeMessage
		{
			SecurityId = secId,
			PortfolioName = PortfolioName,
			ServerTime = time,
		}
		.TryAdd(PositionChangeTypes.AveragePrice,  pos.AveragePrice)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, pos.UnrealizedPnL, true)
		.TryAdd(PositionChangeTypes.CurrentValue,  pos.Position, true)
		.TryAdd(PositionChangeTypes.LiquidationPrice,  pos.EstimatedLiquidationPrice)
		.TryAdd(PositionChangeTypes.Leverage,      pos.Leverage), cancellationToken);
	}

	private async ValueTask SessionOnAccountChanged(OkexAccount acc, CancellationToken cancellationToken)
	{
		await SendOutMessageAsync(new PositionChangeMessage
		{
			SecurityId = SecurityId.Money,
			PortfolioName = PortfolioName,
			ServerTime = acc.UTime
		}
		.TryAdd(PositionChangeTypes.CurrentValue, acc.TotalEquityUsd, true)
		.TryAdd(PositionChangeTypes.Currency,     CurrencyTypes.USD), cancellationToken);

		if(acc.Details == null)
			return;

		foreach (var coin in acc.Details)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				SecurityId = coin.Currency.ToStockSharp(),
				PortfolioName = PortfolioName,
				ServerTime = coin.UTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue,  coin.Equity, true)
			.TryAdd(PositionChangeTypes.BeginValue,    coin.CashBalance, true)
			.TryAdd(PositionChangeTypes.BlockedValue,  coin.FrozenBalance, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, coin.UnrealizedPnL)
			.TryAdd(PositionChangeTypes.CurrentPrice,  coin.CoinUsdPrice), cancellationToken);
		}
	}
}
