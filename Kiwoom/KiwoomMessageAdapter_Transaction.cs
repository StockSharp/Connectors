namespace StockSharp.Kiwoom;

public partial class KiwoomMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		ValidateOrder(regMsg.Volume, regMsg.Price, regMsg.OrderType);
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		var condition = regMsg.Condition as KiwoomOrderCondition ?? new();
		var security = ResolveSecurity(regMsg.SecurityId, condition.Market);
		var quantity = Format(regMsg.Volume);
		var price = orderType == OrderTypes.Market ? string.Empty : Format(regMsg.Price);
		string orderNumber;

		if (security.AssetClass == KiwoomAssetClasses.DomesticStock)
		{
			orderNumber = await _rest.PlaceDomesticOrder(new()
			{
				ExchangeType = security.ExchangeCode,
				SecurityCode = security.Code,
				Quantity = quantity,
				Price = price.IsEmpty("0"),
				TradeType = condition.ToDomesticTradeType(orderType),
				ConditionPrice = condition.StopPrice is > 0 ? Format(condition.StopPrice.Value) : string.Empty,
			}, regMsg.Side, cancellationToken);
		}
		else
		{
			orderNumber = await _rest.PlaceUsOrder(new()
			{
				ExchangeType = security.ExchangeCode,
				SecurityCode = security.Code,
				Quantity = quantity,
				Price = price,
				StopPrice = condition.StopPrice is > 0 ? Format(condition.StopPrice.Value) : string.Empty,
				TradeType = condition.ToUsTradeType(orderType, regMsg.Side),
			}, regMsg.Side, cancellationToken);
		}

		var tracker = new OrderTracker
		{
			TransactionId = regMsg.TransactionId,
			SecurityId = security.ToSecurityId(),
			Security = security,
			OrderNumber = orderNumber,
			Side = regMsg.Side,
			OrderType = orderType,
			Price = regMsg.Price,
			Volume = regMsg.Volume,
			Condition = condition,
		};
		_orders[orderNumber] = tracker;
		_orderFills[orderNumber] = 0;
		await SendOutMessageAsync(CreateOrderMessage(tracker, regMsg.TransactionId, OrderStates.Active,
			regMsg.Volume, DateTime.UtcNow), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		var oldOrderNumber = replaceMsg.OldOrderStringId.ThrowIfEmpty(nameof(replaceMsg.OldOrderStringId));
		_orders.TryGetValue(oldOrderNumber, out var previous);
		var orderType = replaceMsg.OrderType ?? previous?.OrderType ?? OrderTypes.Limit;
		ValidateOrder(replaceMsg.Volume, replaceMsg.Price, orderType);
		var condition = replaceMsg.Condition as KiwoomOrderCondition ?? previous?.Condition ?? new();
		var security = previous?.Security ?? ResolveSecurity(replaceMsg.SecurityId, condition.Market);
		string orderNumber;

		if (security.AssetClass == KiwoomAssetClasses.DomesticStock)
		{
			orderNumber = await _rest.ReplaceDomesticOrder(new()
			{
				ExchangeType = security.ExchangeCode,
				OriginalOrderNumber = oldOrderNumber,
				SecurityCode = security.Code,
				Quantity = Format(replaceMsg.Volume),
				Price = Format(replaceMsg.Price),
				ConditionPrice = condition.StopPrice is > 0 ? Format(condition.StopPrice.Value) : string.Empty,
			}, cancellationToken);
		}
		else
		{
			if (previous != null && replaceMsg.Volume != previous.Volume)
				throw new NotSupportedException("Kiwoom US replace API changes price only; cancel and re-register to change quantity.");
			orderNumber = await _rest.ReplaceUsOrder(new()
			{
				OriginalOrderNumber = oldOrderNumber,
				ExchangeType = security.ExchangeCode,
				SecurityCode = security.Code,
				Price = Format(replaceMsg.Price),
				StopPrice = condition.StopPrice is > 0 ? Format(condition.StopPrice.Value) : string.Empty,
			}, cancellationToken);
		}

		var tracker = new OrderTracker
		{
			TransactionId = replaceMsg.TransactionId,
			SecurityId = security.ToSecurityId(),
			Security = security,
			OrderNumber = orderNumber,
			Side = previous?.Side ?? replaceMsg.Side,
			OrderType = orderType,
			Price = replaceMsg.Price,
			Volume = replaceMsg.Volume,
			Condition = condition,
		};
		_orders[orderNumber] = tracker;
		_orderFills[orderNumber] = 0;
		await SendOutMessageAsync(CreateOrderMessage(tracker, replaceMsg.TransactionId, OrderStates.Active,
			replaceMsg.Volume, DateTime.UtcNow), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var orderNumber = cancelMsg.OrderStringId.ThrowIfEmpty(nameof(cancelMsg.OrderStringId));
		_orders.TryGetValue(orderNumber, out var tracker);
		var condition = cancelMsg.Condition as KiwoomOrderCondition ?? tracker?.Condition ?? new();
		var security = tracker?.Security ?? ResolveSecurity(cancelMsg.SecurityId, condition.Market);
		if (security.AssetClass == KiwoomAssetClasses.DomesticStock)
		{
			await _rest.CancelDomesticOrder(new()
			{
				ExchangeType = security.ExchangeCode,
				OriginalOrderNumber = orderNumber,
				SecurityCode = security.Code,
				Quantity = "0",
			}, cancellationToken);
		}
		else
		{
			await _rest.CancelUsOrder(new()
			{
				OriginalOrderNumber = orderNumber,
				ExchangeType = security.ExchangeCode,
				SecurityCode = security.Code,
			}, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		if (!statusMsg.IsSubscribe)
		{
			if (_orderStatusSubscriptionId == statusMsg.OriginalTransactionId)
				_orderStatusSubscriptionId = 0;
			return;
		}

		var from = statusMsg.From?.UtcKind() ?? DateTime.UtcNow.AddDays(-7);
		var to = statusMsg.To?.UtcKind() ?? DateTime.UtcNow;
		await SendOrderSnapshot(statusMsg.TransactionId, from, to, cancellationToken, statusMsg.Count);
		if (statusMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
		else
		{
			_orderStatusSubscriptionId = statusMsg.TransactionId;
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		if (!lookupMsg.IsSubscribe)
		{
			if (_portfolioSubscriptionId == lookupMsg.OriginalTransactionId)
				_portfolioSubscriptionId = 0;
			return;
		}

		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = lookupMsg.TransactionId,
			PortfolioName = PortfolioName,
			BoardCode = "KRX",
		}, cancellationToken);
		await SendPortfolioSnapshot(lookupMsg.TransactionId, cancellationToken);
		if (lookupMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
		else
		{
			_portfolioSubscriptionId = lookupMsg.TransactionId;
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		}
	}

	private async ValueTask SendPortfolioSnapshot(long originalTransactionId, CancellationToken cancellationToken)
	{
		foreach (var position in await _rest.GetPositions(cancellationToken))
			await SendPosition(position, originalTransactionId, cancellationToken);
	}

	private async ValueTask SendPosition(KiwoomPosition position, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var securityId = position.Security.ToSecurityId();
		_securityInfos[GetSecurityKey(securityId)] = position.Security;
		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = PortfolioName,
			SecurityId = securityId,
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, position.Quantity, true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, position.CurrentPrice, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnL, true)
		.TryAdd(PositionChangeTypes.Currency, position.Security.Currency), cancellationToken);
	}

	private async ValueTask SendOrderSnapshot(long originalTransactionId, DateTime from, DateTime to,
		CancellationToken cancellationToken, long? count = null)
	{
		IEnumerable<KiwoomOrderExecution> orders = await _rest.GetOrders(from, to, cancellationToken);
		if (count is > 0)
			orders = orders.OrderByDescending(order => order.Time).Take((int)Math.Min(count.Value, int.MaxValue));
		foreach (var order in orders.OrderBy(order => order.Time))
			await ProcessExecution(order, originalTransactionId, cancellationToken);
	}

	private async ValueTask ProcessExecution(KiwoomOrderExecution execution, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		_orders.TryGetValue(execution.OrderNumber, out var tracker);
		var securityId = tracker?.SecurityId ?? execution.Security.ToSecurityId();
		var volume = execution.OrderQuantity > 0 ? execution.OrderQuantity : tracker?.Volume ?? 0;
		_orderFills.TryGetValue(execution.OrderNumber, out var previousFill);
		var tradeId = execution.TradeNumber;
		decimal newFill;
		decimal cumulativeFill;
		if (!tradeId.IsEmpty())
		{
			var isNewTrade = _tradeIds.TryAdd(tradeId);
			newFill = isNewTrade ? execution.FilledQuantity : 0;
			cumulativeFill = previousFill + newFill;
		}
		else
		{
			cumulativeFill = Math.Max(previousFill, execution.FilledQuantity);
			newFill = Math.Max(0, cumulativeFill - previousFill);
			tradeId = $"{execution.OrderNumber}:{Format(cumulativeFill)}";
			if (newFill > 0 && !_tradeIds.TryAdd(tradeId))
				newFill = 0;
		}
		var balance = execution.Balance > 0 ? execution.Balance : Math.Max(0, volume - cumulativeFill);
		var state = GetOrderState(execution.Status, balance, cumulativeFill, volume);
		var origin = originalTransactionId != 0 ? originalTransactionId : tracker?.TransactionId ?? _orderStatusSubscriptionId;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = origin,
			TransactionId = originalTransactionId != 0 ? tracker?.TransactionId ?? 0 : 0,
			OrderStringId = execution.OrderNumber,
			SecurityId = securityId,
			PortfolioName = PortfolioName,
			Side = execution.Side,
			OrderType = tracker?.OrderType ?? OrderTypes.Limit,
			OrderPrice = execution.OrderPrice ?? tracker?.Price ?? 0,
			OrderVolume = volume,
			Balance = balance,
			AveragePrice = execution.AveragePrice,
			OrderState = state,
			ServerTime = execution.Time,
			Condition = tracker?.Condition,
			Error = state == OrderStates.Failed ? new InvalidOperationException($"Kiwoom order {execution.OrderNumber} was rejected.") : null,
		}, cancellationToken);

		if (newFill > 0 && execution.AveragePrice is > 0)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = origin,
				OrderStringId = execution.OrderNumber,
				TradeStringId = tradeId,
				SecurityId = securityId,
				PortfolioName = PortfolioName,
				Side = execution.Side,
				TradePrice = execution.AveragePrice,
				TradeVolume = newFill,
				ServerTime = execution.Time,
			}, cancellationToken);
		}
		_orderFills[execution.OrderNumber] = cumulativeFill;
	}

	private async ValueTask ProcessOrderNotice(KiwoomRealtimeMessage message, CancellationToken cancellationToken)
	{
		var values = message.Data.Values;
		var orderNumber = values.OrderNumber;
		if (orderNumber.IsEmpty())
			return;
		_orders.TryGetValue(orderNumber, out var tracker);
		var origin = tracker?.TransactionId ?? _orderStatusSubscriptionId;
		if (origin == 0)
			return;
		var security = tracker?.Security ?? ResolveRealtimeSecurity(message);
		var securityId = tracker?.SecurityId ?? security.ToSecurityId();
		var side = values.SideCode is "2" or "02" ? Sides.Buy : Sides.Sell;
		var volume = values.OrderQuantity.ToDecimal() ?? tracker?.Volume ?? 0;
		var fillQuantity = Math.Abs(values.FillQuantity.ToDecimal() ?? 0);
		var serverTime = string.Empty.ToKiwoomUtc(values.OrderTime, security);
		_orderFills.TryGetValue(orderNumber, out var previousFill);
		var tradeId = values.TradeNumber.IsEmpty($"{orderNumber}:{serverTime.Ticks}:{Format(fillQuantity)}");
		var isNewFill = fillQuantity > 0 && _tradeIds.TryAdd(tradeId);
		var cumulativeFill = previousFill + (isNewFill ? fillQuantity : 0);
		var balance = values.OrderBalance.ToDecimal() ?? Math.Max(0, volume - cumulativeFill);
		var state = GetOrderState(values.OrderStatus, balance, cumulativeFill, volume);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = origin,
			TransactionId = tracker?.TransactionId ?? 0,
			OrderStringId = orderNumber,
			SecurityId = securityId,
			PortfolioName = PortfolioName,
			Side = tracker?.Side ?? side,
			OrderType = tracker?.OrderType ?? OrderTypes.Limit,
			OrderPrice = values.OrderPrice.ToPrice() ?? tracker?.Price ?? 0,
			OrderVolume = volume,
			Balance = Math.Max(0, balance),
			AveragePrice = values.FillPrice.ToPrice(),
			OrderState = state,
			ServerTime = serverTime,
			Condition = tracker?.Condition,
			Error = state == OrderStates.Failed ? new InvalidOperationException($"Kiwoom order {orderNumber} was rejected.") : null,
		}, cancellationToken);

		if (isNewFill && values.FillPrice.ToPrice() is { } fillPrice)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = origin,
				OrderStringId = orderNumber,
				TradeStringId = tradeId,
				SecurityId = securityId,
				PortfolioName = PortfolioName,
				Side = tracker?.Side ?? side,
				TradePrice = fillPrice,
				TradeVolume = fillQuantity,
				ServerTime = serverTime,
			}, cancellationToken);
		}
		_orderFills[orderNumber] = cumulativeFill;
	}

	private ValueTask ProcessBalanceNotice(KiwoomRealtimeMessage message, CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId == 0)
			return default;
		var security = ResolveRealtimeSecurity(message);
		var values = message.Data.Values;
		return SendPosition(new KiwoomPosition(security, values.PositionQuantity.ToDecimal() ?? 0,
			values.AveragePrice.ToPrice(), values.LastPrice.ToPrice(), values.ProfitLoss.ToDecimal(), null),
			_portfolioSubscriptionId, cancellationToken);
	}

	private ExecutionMessage CreateOrderMessage(OrderTracker tracker, long originalTransactionId,
		OrderStates state, decimal balance, DateTime serverTime)
		=> new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId,
			TransactionId = tracker.TransactionId,
			OrderStringId = tracker.OrderNumber,
			SecurityId = tracker.SecurityId,
			PortfolioName = PortfolioName,
			Side = tracker.Side,
			OrderType = tracker.OrderType,
			OrderPrice = tracker.Price,
			OrderVolume = tracker.Volume,
			Balance = balance,
			OrderState = state,
			ServerTime = serverTime,
			Condition = tracker.Condition,
		};

	private static OrderStates GetOrderState(string status, decimal balance, decimal filled, decimal volume)
	{
		if (status?.Contains("거부", StringComparison.Ordinal) == true || status?.Contains("reject", StringComparison.OrdinalIgnoreCase) == true)
			return OrderStates.Failed;
		if (status?.Contains("취소", StringComparison.Ordinal) == true || volume > 0 && filled >= volume || volume > 0 && balance <= 0)
			return OrderStates.Done;
		return OrderStates.Active;
	}

	private static void ValidateOrder(decimal volume, decimal price, OrderTypes? orderType)
	{
		if (volume <= 0 || volume != decimal.Truncate(volume))
			throw new ArgumentOutOfRangeException(nameof(volume), volume, "Kiwoom stock order quantity must be a positive integer.");
		var type = orderType ?? OrderTypes.Limit;
		if (type is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException($"Kiwoom does not support StockSharp order type '{type}'.");
		if (type == OrderTypes.Limit && price <= 0)
			throw new ArgumentOutOfRangeException(nameof(price), price, "Kiwoom limit order price must be positive.");
	}

	private static string Format(decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);
}
