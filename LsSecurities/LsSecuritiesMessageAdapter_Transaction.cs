namespace StockSharp.LsSecurities;

public partial class LsSecuritiesMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsurePortfolio(regMsg.PortfolioName);
		ValidateQuantity(regMsg.Volume);
		var condition = regMsg.Condition as LsSecuritiesOrderCondition ?? new();
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		var priceType = orderType == OrderTypes.Market ? LsOrderPriceTypes.Market : condition.PriceType;
		ValidatePrice(priceType, regMsg.Price);
		var result = await GetRest().PlaceOrder(new()
		{
			Data = new()
			{
				SecurityCode = "A" + regMsg.SecurityId.SecurityCode.NormalizeCode(),
				Quantity = regMsg.Volume,
				Price = priceType == LsOrderPriceTypes.Market ? 0 : regMsg.Price,
				Side = regMsg.Side == Sides.Buy ? "2" : "1",
				PriceType = priceType.ToNative(),
				MarginTransactionCode = condition.MarginTransactionCode.IsEmpty("000"),
				LoanDate = condition.LoanDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? string.Empty,
				ConditionType = regMsg.TimeInForce.ToNative(),
				MemberNumber = condition.Market.ToNative(),
			},
		}, cancellationToken);
		if (result.OrderNumber <= 0)
			throw new InvalidDataException("LS Securities returned no order number.");

		_orders[result.OrderNumber] = new()
		{
			TransactionId = regMsg.TransactionId,
			SecurityId = regMsg.SecurityId,
			PortfolioName = PortfolioName,
			Side = regMsg.Side,
			OrderType = orderType,
			Price = regMsg.Price,
			Volume = regMsg.Volume,
			TimeInForce = regMsg.TimeInForce ?? TimeInForce.PutInQueue,
			Condition = condition,
		};
		await SendOrderMessage(result.OrderNumber, regMsg.TransactionId, regMsg.SecurityId, regMsg.Side,
			orderType, regMsg.Price, regMsg.Volume, regMsg.Volume, OrderStates.Pending,
			regMsg.TimeInForce ?? TimeInForce.PutInQueue, condition,
			result.Time.ToKoreaUtc(), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		EnsurePortfolio(replaceMsg.PortfolioName);
		ValidateQuantity(replaceMsg.Volume);
		var oldOrderNumber = GetOrderNumber(replaceMsg.OldOrderStringId, replaceMsg.OldOrderId,
			replaceMsg.OriginalTransactionId);
		_orders.TryGetValue(oldOrderNumber, out var oldTracker);
		var condition = replaceMsg.Condition as LsSecuritiesOrderCondition ?? oldTracker?.Condition ?? new();
		var orderType = replaceMsg.OrderType ?? oldTracker?.OrderType ?? OrderTypes.Limit;
		var priceType = orderType == OrderTypes.Market ? LsOrderPriceTypes.Market : condition.PriceType;
		ValidatePrice(priceType, replaceMsg.Price);
		var securityId = oldTracker?.SecurityId ?? replaceMsg.SecurityId;
		var result = await GetRest().ReplaceOrder(new()
		{
			Data = new()
			{
				OriginalOrderNumber = oldOrderNumber,
				SecurityCode = "A" + securityId.SecurityCode.NormalizeCode(),
				Quantity = replaceMsg.Volume,
				Price = priceType == LsOrderPriceTypes.Market ? 0 : replaceMsg.Price,
				PriceType = priceType.ToNative(),
				ConditionType = replaceMsg.TimeInForce.ToNative(),
			},
		}, cancellationToken);
		if (result.OrderNumber <= 0)
			throw new InvalidDataException("LS Securities returned no replacement order number.");

		var tracker = new OrderTracker
		{
			TransactionId = replaceMsg.TransactionId,
			SecurityId = securityId,
			PortfolioName = PortfolioName,
			Side = oldTracker?.Side ?? replaceMsg.Side,
			OrderType = orderType,
			Price = replaceMsg.Price,
			Volume = replaceMsg.Volume,
			TimeInForce = replaceMsg.TimeInForce ?? oldTracker?.TimeInForce ?? TimeInForce.PutInQueue,
			Condition = condition,
		};
		_orders[result.OrderNumber] = tracker;
		await SendOrderMessage(result.OrderNumber, replaceMsg.TransactionId, securityId, tracker.Side,
			orderType, replaceMsg.Price, replaceMsg.Volume, replaceMsg.Volume, OrderStates.Pending,
			tracker.TimeInForce, condition, result.Time.ToKoreaUtc(), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePortfolio(cancelMsg.PortfolioName);
		var orderNumber = GetOrderNumber(cancelMsg.OrderStringId, cancelMsg.OrderId,
			cancelMsg.OriginalTransactionId);
		_orders.TryGetValue(orderNumber, out var tracker);
		var securityId = tracker?.SecurityId ?? cancelMsg.SecurityId;
		await GetRest().CancelOrder(new()
		{
			Data = new()
			{
				OriginalOrderNumber = orderNumber,
				SecurityCode = "A" + securityId.SecurityCode.NormalizeCode(),
				Quantity = tracker?.Volume ?? 0,
			},
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		if (!statusMsg.IsSubscribe)
		{
			if (_orderStatusSubscriptionId == statusMsg.OriginalTransactionId)
				_orderStatusSubscriptionId = 0;
			return;
		}

		EnsurePortfolio(statusMsg.PortfolioName);
		var left = statusMsg.Count ?? long.MaxValue;
		foreach (var order in (await GetRest().GetOrders(cancellationToken)).OrderBy(o => o.Time))
		{
			var time = order.Time.ToKoreaUtc();
			if (statusMsg.From != null && time < statusMsg.From.Value.ToUniversalTime())
				continue;
			if (statusMsg.To != null && time > statusMsg.To.Value.ToUniversalTime())
				continue;
			await ProcessOrderSnapshot(order, statusMsg.TransactionId, cancellationToken);
			if (--left <= 0)
				break;
		}

		if (statusMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
		else
		{
			_orderStatusSubscriptionId = statusMsg.TransactionId;
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		if (!lookupMsg.IsSubscribe)
		{
			if (_portfolioSubscriptionId == lookupMsg.OriginalTransactionId)
				_portfolioSubscriptionId = 0;
			return;
		}

		EnsurePortfolio(lookupMsg.PortfolioName);
		await SendPortfolioSnapshot(lookupMsg.TransactionId, cancellationToken);
		if (lookupMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
		else
		{
			_portfolioSubscriptionId = lookupMsg.TransactionId;
			_lastPortfolioRefresh = DateTime.UtcNow;
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		}
	}

	private async ValueTask SendPortfolioSnapshot(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = PortfolioName,
			BoardCode = "KRX",
		}, cancellationToken);
		var snapshot = await GetRest().GetPortfolio(cancellationToken);
		if (snapshot.Summary != null)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = PortfolioName,
				SecurityId = SecurityId.Money,
				ServerTime = DateTime.UtcNow,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, snapshot.Summary.EstimatedAssets, true)
			.TryAdd(PositionChangeTypes.CurrentPrice, snapshot.Summary.EvaluationAmount, true)
			.TryAdd(PositionChangeTypes.RealizedPnL, snapshot.Summary.ProfitLoss, true), cancellationToken);
		}

		foreach (var position in snapshot.Positions ?? [])
		{
			if (position?.Code.IsEmpty() != false)
				continue;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = PortfolioName,
				SecurityId = ToSecurityId(position.Code),
				ServerTime = DateTime.UtcNow,
			}
			.Add(PositionChangeTypes.CurrentValue, position.Quantity)
			.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice, true)
			.TryAdd(PositionChangeTypes.CurrentPrice, position.CurrentPrice, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, position.ProfitLoss, true), cancellationToken);
		}
	}

	private async ValueTask ProcessOrderSnapshot(LsOrder order, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (order?.OrderNumber <= 0 || order.Code.IsEmpty())
			return;
		_orders.TryGetValue(order.OrderNumber, out var tracker);
		var securityId = tracker?.SecurityId ?? ToSecurityId(order.Code);
		var side = tracker?.Side ?? (order.Side?.Contains("매도", StringComparison.Ordinal) == true
			? Sides.Sell : Sides.Buy);
		var orderType = tracker?.OrderType ?? (order.PriceType == "03" ? OrderTypes.Market : OrderTypes.Limit);
		var timeInForce = tracker?.TimeInForce ?? TimeInForce.PutInQueue;
		var condition = tracker?.Condition ?? new LsSecuritiesOrderCondition();
		var state = order.Balance > 0 ? OrderStates.Active : OrderStates.Done;
		await SendOrderMessage(order.OrderNumber, originalTransactionId, securityId, side, orderType,
			order.OrderPrice, order.Quantity, order.Balance, state, timeInForce, condition,
			order.Time.ToKoreaUtc(), cancellationToken);

		if (order.FilledQuantity > 0 && order.ExecutionPrice > 0)
		{
			var tradeId = $"{order.OrderNumber}:{order.FilledQuantity}:{order.ExecutionPrice}";
			if (_reportedTrades.TryAdd(tradeId))
			{
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OriginalTransactionId = originalTransactionId,
					OrderId = order.OrderNumber,
					OrderStringId = order.OrderNumber.ToString(CultureInfo.InvariantCulture),
					TradeStringId = tradeId,
					SecurityId = securityId,
					PortfolioName = PortfolioName,
					Side = side,
					TradePrice = order.ExecutionPrice,
					TradeVolume = order.FilledQuantity,
					ServerTime = order.Time.ToKoreaUtc(),
				}, cancellationToken);
			}
		}
	}

	private async ValueTask ProcessRealtimeOrder(string eventCode, LsRealtimeOrder order,
		CancellationToken cancellationToken)
	{
		var orderNumber = order?.OrderNumber.ToLong() ?? 0;
		if (orderNumber <= 0)
			return;
		_orders.TryGetValue(orderNumber, out var tracker);
		if (tracker == null && order.OriginalOrderNumber.ToLong() is > 0 and var originalOrderNumber)
			_orders.TryGetValue(originalOrderNumber, out tracker);
		var originalId = tracker?.TransactionId is > 0 ? tracker.TransactionId : _orderStatusSubscriptionId;
		if (originalId == 0)
			return;

		var securityCode = order.ShortSecurityCode.IsEmpty(order.SecurityCode).NormalizeCode();
		var securityId = tracker?.SecurityId ?? ToSecurityId(securityCode);
		var side = tracker?.Side ?? (order.Side == "1" ? Sides.Sell : Sides.Buy);
		var orderType = tracker?.OrderType ?? (order.PriceType == "03" ? OrderTypes.Market : OrderTypes.Limit);
		var price = order.Price.ToDecimal();
		if (price == 0)
			price = order.OrderPrice.ToDecimal();
		var volume = order.OrderQuantity.ToDecimal();
		var balance = order.Balance.ToDecimal();
		var state = eventCode switch
		{
			"SC0" => OrderStates.Active,
			"SC1" when balance <= 0 => OrderStates.Done,
			"SC1" or "SC2" => OrderStates.Active,
			"SC3" => OrderStates.Done,
			"SC4" => OrderStates.Failed,
			_ => OrderStates.Active,
		};
		var serverTime = (eventCode == "SC0" ? order.OrderTime : order.ExecutionTime).ToKoreaUtc();
		await SendOrderMessage(orderNumber, originalId, securityId, side, orderType, price, volume,
			balance, state, tracker?.TimeInForce ?? order.ConditionType.ToTimeInForce(),
			tracker?.Condition, serverTime, cancellationToken);

		var executionQuantity = order.ExecutionQuantity.ToDecimal();
		var executionPrice = order.ExecutionPrice.ToDecimal();
		if (eventCode == "SC1" && executionQuantity > 0 && executionPrice > 0)
		{
			var tradeId = order.ExecutionNumber.IsEmpty($"{orderNumber}:{order.ExecutionTime}:{executionQuantity}");
			if (_reportedTrades.TryAdd(tradeId))
			{
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OriginalTransactionId = originalId,
					OrderId = orderNumber,
					OrderStringId = orderNumber.ToString(CultureInfo.InvariantCulture),
					TradeStringId = tradeId,
					SecurityId = securityId,
					PortfolioName = tracker?.PortfolioName ?? PortfolioName,
					Side = side,
					TradePrice = executionPrice,
					TradeVolume = executionQuantity,
					ServerTime = serverTime,
				}, cancellationToken);
			}
		}
		_lastPortfolioRefresh = default;
	}

	private ValueTask SendOrderMessage(long orderNumber, long originalTransactionId,
		SecurityId securityId, Sides side, OrderTypes orderType, decimal price, decimal volume,
		decimal balance, OrderStates state, TimeInForce timeInForce,
		LsSecuritiesOrderCondition condition, DateTime serverTime, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId,
			OrderId = orderNumber,
			OrderStringId = orderNumber.ToString(CultureInfo.InvariantCulture),
			SecurityId = securityId,
			PortfolioName = PortfolioName,
			Side = side,
			OrderType = orderType,
			OrderPrice = price,
			OrderVolume = volume,
			Balance = balance,
			OrderState = state,
			TimeInForce = timeInForce,
			Condition = condition,
			ServerTime = serverTime,
		}, cancellationToken);

	private static long GetOrderNumber(string stringId, long? numericId, long transactionId)
	{
		if (!stringId.IsEmpty() && long.TryParse(stringId, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
			return parsed;
		if (numericId is > 0)
			return numericId.Value;
		throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(transactionId));
	}

	private void EnsurePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() && !portfolioName.EqualsIgnoreCase(PortfolioName))
			throw new InvalidOperationException($"LS Securities portfolio '{portfolioName}' is not connected.");
	}

	private static void ValidateQuantity(decimal quantity)
	{
		if (quantity <= 0 || quantity != decimal.Truncate(quantity))
			throw new ArgumentOutOfRangeException(nameof(quantity), quantity,
				"LS Securities quantity must be a positive whole number.");
	}

	private static void ValidatePrice(LsOrderPriceTypes priceType, decimal price)
	{
		if (priceType != LsOrderPriceTypes.Market && price <= 0)
			throw new ArgumentOutOfRangeException(nameof(price), price,
				"LS Securities limit-style orders require a positive price.");
	}
}
