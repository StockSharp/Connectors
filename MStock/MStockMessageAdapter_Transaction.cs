namespace StockSharp.MStock;

public partial class MStockMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsureConnected();
		EnsurePortfolio(regMsg.PortfolioName);
		var instrument = await ResolveInstrumentAsync(regMsg.SecurityId,
			cancellationToken);
		var condition = regMsg.Condition as MStockOrderCondition;
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		var quantity = ToNativeQuantity(regMsg.Volume);
		var disclosed = condition?.DisclosedVolume is > 0
			? ToNativeQuantity(condition.DisclosedVolume.Value)
			: 0;
		if (disclosed > quantity)
			throw new ArgumentOutOfRangeException(
				nameof(condition.DisclosedVolume),
				"Disclosed volume cannot exceed order volume.");

		var tag = (condition?.Tag).IsEmpty(
			regMsg.TransactionId.ToString(
				CultureInfo.InvariantCulture));
		if (tag.Length > 20)
			throw new ArgumentOutOfRangeException(nameof(condition.Tag),
				"m.Stock order tag cannot exceed 20 characters.");
		var product = condition?.Product ??
			(IsDerivative(instrument)
				? MStockProducts.CarryForward
				: MStockProducts.Delivery);
		var variety = ResolveVariety(orderType, condition);
		var request = CreateOrderRequest(instrument, regMsg.Side,
			orderType, regMsg.Price, regMsg.Volume, regMsg.TimeInForce,
			product, variety, condition, tag);

		var response = await RestClient.PlaceOrderAsync(request,
			cancellationToken);
		var orderId = response.String(
			"order_id", "orderid", "orderId")
				.ThrowIfEmpty("order_id");
		RememberOrder(orderId, regMsg.TransactionId);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			OrderStringId = orderId,
			SecurityId = ToSecurityId(instrument),
			PortfolioName = _portfolioName,
			OrderType = orderType,
			Side = regMsg.Side,
			TimeInForce = regMsg.TimeInForce,
			OrderPrice = regMsg.Price,
			OrderVolume = regMsg.Volume,
			Balance = regMsg.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = response.Get("requestTime", "timestamp")
				.ToMStockTime(CurrentTime).UtcDateTime,
			Condition = condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		EnsureConnected();
		EnsurePortfolio(replaceMsg.PortfolioName);
		var orderId = ResolveOrderId(replaceMsg.OldOrderStringId,
			replaceMsg.OriginalTransactionId);
		var instrument = await ResolveInstrumentAsync(
			replaceMsg.SecurityId, cancellationToken);
		var condition = replaceMsg.Condition as MStockOrderCondition;
		var orderType = replaceMsg.OrderType ?? OrderTypes.Limit;
		if (orderType == OrderTypes.Limit &&
			replaceMsg.Price <= 0)
			throw new ArgumentOutOfRangeException(
				nameof(replaceMsg.Price), replaceMsg.Price,
				"A positive limit price is required.");
		var product = condition?.Product ??
			(IsDerivative(instrument)
				? MStockProducts.CarryForward
				: MStockProducts.Delivery);
		var variety = ResolveVariety(orderType, condition);
		var request = new JObject
		{
			["variety"] = ToNativeVariety(variety),
			["orderid"] = orderId,
			["ordertype"] = ToNativeOrderType(orderType,
				condition?.TriggerPrice, replaceMsg.Price),
			["producttype"] = ToNativeProduct(product),
			["duration"] = ToNativeValidity(
				replaceMsg.TimeInForce),
			["price"] = orderType == OrderTypes.Market
				? 0
				: replaceMsg.Price,
			["quantity"] = ToNativeQuantity(replaceMsg.Volume),
			["tradingsymbol"] = instrument.TradingSymbol,
			["symboltoken"] = instrument.Token,
			["exchange"] = instrument.Exchange,
			["triggerprice"] = condition?.TriggerPrice ?? 0,
		};
		var response = await RestClient.ModifyOrderAsync(orderId,
			request, cancellationToken);
		var resultingId = response.String(
			"order_id", "orderid", "orderId").IsEmpty(orderId);
		RememberOrder(resultingId, replaceMsg.TransactionId);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = replaceMsg.TransactionId,
			OrderStringId = resultingId,
			SecurityId = ToSecurityId(instrument),
			PortfolioName = _portfolioName,
			OrderType = orderType,
			Side = replaceMsg.Side,
			TimeInForce = replaceMsg.TimeInForce,
			OrderPrice = replaceMsg.Price,
			OrderVolume = replaceMsg.Volume,
			Balance = replaceMsg.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = response.Get("requestTime", "timestamp")
				.ToMStockTime(CurrentTime).UtcDateTime,
			Condition = condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsureConnected();
		EnsurePortfolio(cancelMsg.PortfolioName);
		var orderId = ResolveOrderId(cancelMsg.OrderStringId,
			cancelMsg.OriginalTransactionId);
		var variety = (cancelMsg.Condition as MStockOrderCondition)
			?.Variety ?? MStockOrderVarieties.Normal;
		if (cancelMsg.OrderType == OrderTypes.Conditional &&
			variety == MStockOrderVarieties.Normal)
			variety = MStockOrderVarieties.StopLoss;
		await RestClient.CancelOrderAsync(orderId,
			ToNativeVariety(variety), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId,
			cancellationToken);

		EnsureConnected();
		if (!statusMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_orderSubscriptions.Remove(
					statusMsg.OriginalTransactionId);
			return;
		}

		EnsurePortfolio(statusMsg.PortfolioName);
		await SendOrdersSnapshotAsync(statusMsg.TransactionId,
			statusMsg.From, statusMsg.To, statusMsg.Count,
			cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(statusMsg,
				cancellationToken);
			await SendSubscriptionFinishedAsync(
				statusMsg.TransactionId, cancellationToken);
			return;
		}

		using (_sync.EnterScope())
			_orderSubscriptions.Add(statusMsg.TransactionId);
		await SendSubscriptionResultAsync(statusMsg,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);

		EnsureConnected();
		if (!lookupMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_portfolioSubscriptions.Remove(
					lookupMsg.OriginalTransactionId);
			return;
		}

		EnsurePortfolio(lookupMsg.PortfolioName);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId,
			cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(lookupMsg,
				cancellationToken);
			await SendSubscriptionFinishedAsync(
				lookupMsg.TransactionId, cancellationToken);
			return;
		}

		using (_sync.EnterScope())
			_portfolioSubscriptions.Add(lookupMsg.TransactionId);
		await SendSubscriptionResultAsync(lookupMsg,
			cancellationToken);
	}

	private async ValueTask SendOrdersSnapshotAsync(long target,
		DateTime? from, DateTime? to, long? count,
		CancellationToken cancellationToken)
	{
		var orders = (await RestClient.GetOrdersAsync(
				cancellationToken))
			.ToMStockObjects()
			.Select(static value => value.ToMStockOrder())
			.Where(static value => !value.OrderId.IsEmpty())
			.OrderBy(static value => value.Time)
			.Where(value =>
				(from is null ||
					value.Time.UtcDateTime >=
						from.Value.ToUniversalTime()) &&
				(to is null ||
					value.Time.UtcDateTime <=
						to.Value.ToUniversalTime()))
			.Take(count is > 0
				? (int)Math.Min(count.Value, int.MaxValue)
				: int.MaxValue)
			.ToArray();

		foreach (var order in orders)
			await SendOrderAsync(order, target, true,
				cancellationToken);

		var trades = (await RestClient.GetTradesAsync(from, to,
				cancellationToken))
			.ToMStockObjects()
			.Select(static value => value.ToMStockTrade())
			.Where(value =>
				(from is null ||
					value.Time.UtcDateTime >=
						from.Value.ToUniversalTime()) &&
				(to is null ||
					value.Time.UtcDateTime <=
						to.Value.ToUniversalTime()))
			.ToArray();

		foreach (var trade in trades)
			await SendTradeAsync(trade, target, true,
				cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		CancellationToken cancellationToken)
	{
		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = target,
			PortfolioName = _portfolioName,
			BoardCode = BoardCodes.Nse,
		}, cancellationToken);

		var funds = (await RestClient.GetFundsAsync(
			cancellationToken)).ToMStockObjects().FirstOrDefault();
		if (funds is not null)
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = target,
				PortfolioName = _portfolioName,
				SecurityId = SecurityId.Money,
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue,
				funds.Decimal("AVAILABLE_BALANCE",
					"availableBalance", "SUM_OF_ALL"), true)
			.TryAdd(PositionChangeTypes.BlockedValue,
				funds.Decimal("AMOUNT_UTILIZED",
					"amountUtilized", "MTF_UTILIZE"), true)
			.TryAdd(PositionChangeTypes.RealizedPnL,
				funds.Decimal("REALISED_PROFITS",
					"realisedProfits"), true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL,
				funds.Decimal("MTM_COMBINED",
					"mtmCombined"), true), cancellationToken);

		foreach (var holding in (await RestClient.GetHoldingsAsync(
				cancellationToken)).ToMStockObjects())
			await SendHoldingAsync(holding, target,
				cancellationToken);

		foreach (var position in (await RestClient.GetPositionsAsync(
				cancellationToken)).ToMStockObjects())
			await SendPositionAsync(position, target,
				cancellationToken);
	}

	private ValueTask SendHoldingAsync(JObject value, long target,
		CancellationToken cancellationToken)
	{
		var instrument = CreateInstrumentRef(
			value.String("exchange").IsEmpty("NSE"),
			value.String("symboltoken", "symbolToken", "isin"),
			value.String("tradingsymbol", "tradingSymbol",
				"symbol", "symbolname"));
		if (instrument.Token.IsEmpty())
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = target,
			PortfolioName = _portfolioName,
			SecurityId = ToSecurityId(instrument),
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue,
			value.Decimal("quantity", "totalquantity",
				"authorisedquantity", "realisedquantity"), true)
		.TryAdd(PositionChangeTypes.BlockedValue,
			value.Decimal("collateralquantity",
				"t1quantity"), true)
		.TryAdd(PositionChangeTypes.AveragePrice,
			value.Decimal("averageprice", "averagePrice"), true)
		.TryAdd(PositionChangeTypes.CurrentPrice,
			value.Decimal("ltp", "lastPrice"), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL,
			value.Decimal("pnl", "profitandloss"), true),
			cancellationToken);
	}

	private ValueTask SendPositionAsync(JObject value, long target,
		CancellationToken cancellationToken)
	{
		var instrument = CreateInstrumentRef(
			value.String("exchange").IsEmpty("NSE"),
			value.String("symboltoken", "symbolToken", "token"),
			value.String("tradingsymbol", "tradingSymbol",
				"symbol", "symbolname"));
		if (instrument.Token.IsEmpty())
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = target,
			PortfolioName = _portfolioName,
			SecurityId = ToSecurityId(instrument),
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue,
			value.Decimal("netqty", "netQuantity",
				"quantity"), true)
		.TryAdd(PositionChangeTypes.AveragePrice,
			value.Decimal("averageprice", "averagePrice",
				"netprice"), true)
		.TryAdd(PositionChangeTypes.CurrentPrice,
			value.Decimal("ltp", "lastPrice"), true)
		.TryAdd(PositionChangeTypes.RealizedPnL,
			value.Decimal("realised", "realizedPnl",
				"realisedPnl"), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL,
			value.Decimal("unrealised", "unrealizedPnl",
				"pnl"), true), cancellationToken);
	}

	private async ValueTask PollOrdersAsync(
		CancellationToken cancellationToken)
	{
		long[] targets;
		using (_sync.EnterScope())
			targets = [.. _orderSubscriptions];
		if (targets.Length == 0)
			return;

		var orders = (await RestClient.GetOrdersAsync(
				cancellationToken)).ToMStockObjects()
			.Select(static value => value.ToMStockOrder())
			.ToArray();
		var trades = (await RestClient.GetTradesAsync(null, null,
				cancellationToken)).ToMStockObjects()
			.Select(static value => value.ToMStockTrade())
			.ToArray();

		foreach (var target in targets)
		{
			foreach (var order in orders)
				await SendOrderAsync(order, target, false,
					cancellationToken);

			foreach (var trade in trades)
				await SendTradeAsync(trade, target, false,
					cancellationToken);
		}
	}

	private async ValueTask PollPortfoliosAsync(
		CancellationToken cancellationToken)
	{
		long[] targets;
		using (_sync.EnterScope())
			targets = [.. _portfolioSubscriptions];

		foreach (var target in targets)
			await SendPortfolioSnapshotAsync(target,
				cancellationToken);
	}

	private async ValueTask ProcessOrderStreamAsync(JObject value,
		CancellationToken cancellationToken)
	{
		var order = value.ToMStockOrder();
		long[] targets;
		using (_sync.EnterScope())
			targets = [.. _orderSubscriptions];

		foreach (var target in targets)
			await SendOrderAsync(order, target, false,
				cancellationToken);
	}

	private async ValueTask ProcessTradeStreamAsync(JObject value,
		CancellationToken cancellationToken)
	{
		var trade = value.ToMStockTrade();
		long[] targets;
		using (_sync.EnterScope())
			targets = [.. _orderSubscriptions];

		foreach (var target in targets)
			await SendTradeAsync(trade, target, false,
				cancellationToken);
	}

	private async ValueTask SendOrderAsync(MStockOrder order,
		long target, bool isLookup,
		CancellationToken cancellationToken)
	{
		if (order is null || order.OrderId.IsEmpty())
			return;
		long transactionId;
		if (long.TryParse(order.Tag, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out transactionId))
			RememberOrder(order.OrderId, transactionId);
		else
		{
			using (_sync.EnterScope())
				_orderTransactions.TryGetValue(order.OrderId,
					out transactionId);
		}
		var instrument = CreateInstrumentRef(order.Exchange,
			order.Token, order.Symbol);
		var state = order.Status.ToMStockOrderState();
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = target,
			TransactionId = isLookup ? transactionId : 0,
			OrderStringId = order.OrderId,
			SecurityId = ToSecurityId(instrument),
			PortfolioName = _portfolioName,
			OrderType = order.OrderType.ToMStockOrderType(),
			Side = order.Side,
			TimeInForce = order.Duration.ToMStockTimeInForce(),
			OrderPrice = order.Price,
			OrderVolume = order.Volume,
			Balance = order.Balance,
			AveragePrice = order.AveragePrice > 0
				? order.AveragePrice
				: null,
			OrderState = state,
			ServerTime = order.Time.UtcDateTime,
			Condition = ToOrderCondition(order),
			Error = state == OrderStates.Failed
				? new InvalidOperationException(order.Text.IsEmpty(
					$"m.Stock order status: {order.Status}."))
				: null,
		}, cancellationToken);
	}

	private ValueTask SendTradeAsync(MStockTrade trade, long target,
		bool isLookup, CancellationToken cancellationToken)
	{
		if (trade is null || trade.Id.IsEmpty())
			return default;
		using (_sync.EnterScope())
		{
			if (!_tradeIds.Add($"{target}:{trade.Id}"))
				return default;
		}
		long transactionId;
		using (_sync.EnterScope())
			_orderTransactions.TryGetValue(trade.OrderId,
				out transactionId);
		var instrument = CreateInstrumentRef(trade.Exchange,
			trade.Token, trade.Symbol);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = target,
			TransactionId = isLookup ? transactionId : 0,
			OrderStringId = trade.OrderId,
			TradeStringId = trade.Id,
			SecurityId = ToSecurityId(instrument),
			PortfolioName = _portfolioName,
			Side = trade.Side,
			TradePrice = trade.Price,
			TradeVolume = trade.Volume,
			ServerTime = trade.Time.UtcDateTime,
		}, cancellationToken);
	}

	private void RememberOrder(string orderId, long transactionId)
	{
		if (orderId.IsEmpty() || transactionId == 0)
			return;
		using (_sync.EnterScope())
		{
			_orderTransactions[orderId] = transactionId;
			_transactionOrders[transactionId] = orderId;
		}
	}

	private string ResolveOrderId(string orderId,
		long originalTransactionId)
	{
		if (!orderId.IsEmpty())
			return orderId;
		using (_sync.EnterScope())
			if (_transactionOrders.TryGetValue(originalTransactionId,
				out orderId))
				return orderId;
		throw new InvalidOperationException(
			LocalizedStrings.OrderNoExchangeId.Put(
				originalTransactionId));
	}

	private void EnsurePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(_portfolioName))
			throw new InvalidOperationException(
				LocalizedStrings.AccountNotFound);
	}

	private MStockInstrumentRef CreateInstrumentRef(string exchange,
		string token, string symbol)
	{
		exchange = exchange.IsEmpty("NSE").Trim().ToUpperInvariant();
		token = token.IsEmpty(symbol);
		if (token.IsEmpty())
			return default;
		var key = NativeKey(exchange, token);
		using (_sync.EnterScope())
			if (_instrumentsByNative.TryGetValue(key,
				out var known))
				return known;
		return new(exchange, token, symbol.IsEmpty(token),
			symbol.IsEmpty(token), 1);
	}

	private static JObject CreateOrderRequest(
		MStockInstrumentRef instrument, Sides side,
		OrderTypes orderType, decimal price, decimal volume,
		TimeInForce? timeInForce, MStockProducts product,
		MStockOrderVarieties variety, MStockOrderCondition condition,
		string tag)
	{
		if (orderType == OrderTypes.Limit && price <= 0)
			throw new ArgumentOutOfRangeException(nameof(price), price,
				"A positive limit price is required.");
		return new()
		{
			["variety"] = ToNativeVariety(variety),
			["tradingsymbol"] = instrument.TradingSymbol,
			["symboltoken"] = instrument.Token,
			["exchange"] = instrument.Exchange,
			["transactiontype"] = side == Sides.Buy
				? "BUY"
				: "SELL",
			["ordertype"] = ToNativeOrderType(orderType,
				condition?.TriggerPrice, price),
			["quantity"] = ToNativeQuantity(volume),
			["producttype"] = ToNativeProduct(product),
			["price"] = orderType == OrderTypes.Market ? 0 : price,
			["triggerprice"] = condition?.TriggerPrice ?? 0,
			["squareoff"] = condition?.SquareOff ?? 0,
			["stoploss"] = condition?.StopLoss ?? 0,
			["trailingStopLoss"] =
				condition?.TrailingStopLoss ?? 0,
			["disclosedquantity"] =
				condition?.DisclosedVolume is > 0
					? ToNativeQuantity(
						condition.DisclosedVolume.Value)
					: 0,
			["duration"] = ToNativeValidity(timeInForce),
			["ordertag"] = tag,
		};
	}

	private static MStockOrderVarieties ResolveVariety(
		OrderTypes orderType, MStockOrderCondition condition)
	{
		var variety = condition?.Variety ??
			MStockOrderVarieties.Normal;
		if (orderType == OrderTypes.Conditional &&
			variety == MStockOrderVarieties.Normal)
			return MStockOrderVarieties.StopLoss;
		return variety;
	}

	private static string ToNativeOrderType(OrderTypes orderType,
		decimal? triggerPrice, decimal price)
		=> orderType switch
		{
			OrderTypes.Market => "MARKET",
			OrderTypes.Limit => "LIMIT",
			OrderTypes.Conditional when triggerPrice is > 0 =>
				price > 0
					? "STOPLOSS_LIMIT"
					: "STOPLOSS_MARKET",
			OrderTypes.Conditional =>
				throw new InvalidOperationException(
					"A positive trigger price is required for an " +
						"m.Stock stop-loss order."),
			_ => throw new ArgumentOutOfRangeException(
				nameof(orderType), orderType,
				"m.Stock supports market, limit, and stop-loss " +
					"orders."),
		};

	private static string ToNativeProduct(MStockProducts value)
		=> value switch
		{
			MStockProducts.Delivery => "DELIVERY",
			MStockProducts.Intraday => "INTRADAY",
			MStockProducts.Margin => "MARGIN",
			MStockProducts.CarryForward => "CARRYFORWARD",
			_ => throw new ArgumentOutOfRangeException(nameof(value),
				value, null),
		};

	private static string ToNativeVariety(MStockOrderVarieties value)
		=> value switch
		{
			MStockOrderVarieties.Normal => "NORMAL",
			MStockOrderVarieties.AMO => "AMO",
			MStockOrderVarieties.StopLoss => "STOPLOSS",
			_ => throw new ArgumentOutOfRangeException(nameof(value),
				value, null),
		};

	private static string ToNativeValidity(TimeInForce? value)
		=> value switch
		{
			null or TimeInForce.PutInQueue => "DAY",
			TimeInForce.CancelBalance => "IOC",
			TimeInForce.MatchOrCancel =>
				throw new NotSupportedException(
					"m.Stock does not expose fill-or-kill validity."),
			_ => throw new ArgumentOutOfRangeException(nameof(value),
				value, null),
		};

	private static long ToNativeQuantity(decimal value)
	{
		if (value <= 0 || value != decimal.Truncate(value) ||
			value > long.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(value), value,
				"m.Stock quantity must be a positive whole Int64 " +
					"value.");
		return decimal.ToInt64(value);
	}

	private bool IsDerivative(MStockInstrumentRef instrument)
	{
		if (instrument.Exchange is "NFO" or "BFO" or "CDS")
			return true;
		using (_sync.EnterScope())
			return _instrumentDetails.TryGetValue(instrument.Key,
				out var details) &&
				details.ToSecurityType() is
					SecurityTypes.Future or SecurityTypes.Option;
	}

	private static MStockOrderCondition ToOrderCondition(
		MStockOrder order)
		=> new()
		{
			Product =
				order.Product?.Trim().ToUpperInvariant() switch
				{
					"INTRADAY" => MStockProducts.Intraday,
					"MARGIN" => MStockProducts.Margin,
					"CARRYFORWARD" =>
						MStockProducts.CarryForward,
					_ => MStockProducts.Delivery,
				},
			Variety =
				order.Variety?.Trim().ToUpperInvariant() switch
				{
					"AMO" => MStockOrderVarieties.AMO,
					"STOPLOSS" =>
						MStockOrderVarieties.StopLoss,
					_ => MStockOrderVarieties.Normal,
				},
			TriggerPrice = order.TriggerPrice > 0
				? order.TriggerPrice
				: null,
			Tag = order.Tag,
		};
}
