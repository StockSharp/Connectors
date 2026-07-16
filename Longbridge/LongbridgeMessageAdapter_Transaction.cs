namespace StockSharp.Longbridge;

public partial class LongbridgeMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var condition = regMsg.Condition as LongbridgeOrderCondition;
		var nativeType = condition?.NativeOrderType ?? regMsg.OrderType switch
		{
			OrderTypes.Market => LongbridgeOrderTypes.Market,
			OrderTypes.Conditional => condition?.TriggerPrice > 0
				? LongbridgeOrderTypes.LimitIfTouched : LongbridgeOrderTypes.MarketIfTouched,
			_ => LongbridgeOrderTypes.Limit,
		};
		ValidateOrder(regMsg.Volume, regMsg.Price, nativeType, condition);
		var nativeTimeInForce = regMsg.TillDate != null
			? LongbridgeTimeInForces.GoodTillDate : condition?.NativeTimeInForce ?? LongbridgeTimeInForces.Day;
		var request = new LongbridgeSubmitOrderRequest
		{
			Symbol = regMsg.SecurityId.ToNativeSymbol(),
			OrderType = nativeType.ToNative(),
			Side = regMsg.Side == Sides.Buy ? "Buy" : "Sell",
			Quantity = Format(regMsg.Volume),
			Price = NeedsPrice(nativeType) ? Format(regMsg.Price) : null,
			TriggerPrice = condition?.TriggerPrice is > 0 ? Format(condition.TriggerPrice.Value) : null,
			LimitOffset = condition?.LimitOffset is > 0 ? Format(condition.LimitOffset.Value) : null,
			TrailingAmount = condition?.TrailingAmount is > 0 ? Format(condition.TrailingAmount.Value) : null,
			TrailingPercent = condition?.TrailingPercent is > 0 ? Format(condition.TrailingPercent.Value) : null,
			ExpireDate = regMsg.TillDate?.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
			OutsideRth = (condition?.OutsideRth ?? LongbridgeOutsideRths.RegularOnly).ToNative(),
			Remark = condition?.Remark,
			TimeInForce = nativeTimeInForce.ToNative(),
		};
		var response = await _restClient.SubmitOrder(request, cancellationToken);
		var orderId = response?.OrderId.ThrowIfEmpty("OrderId");
		_orderTransactions[orderId] = regMsg.TransactionId;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			OrderId = ParseLong(orderId),
			OrderStringId = orderId,
			SecurityId = regMsg.SecurityId,
			PortfolioName = Portfolio,
			Side = regMsg.Side,
			OrderType = regMsg.OrderType,
			OrderPrice = regMsg.Price,
			OrderVolume = regMsg.Volume,
			Balance = regMsg.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = DateTime.UtcNow,
			TimeInForce = regMsg.TimeInForce,
			ExpiryDate = regMsg.TillDate,
			Condition = regMsg.Condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		var orderId = GetOrderId(replaceMsg.OldOrderId, replaceMsg.OldOrderStringId, replaceMsg.OriginalTransactionId);
		var condition = replaceMsg.Condition as LongbridgeOrderCondition;
		if (replaceMsg.Volume <= 0)
			throw new InvalidOperationException("Longbridge replacement quantity must be positive.");
		await _restClient.ReplaceOrder(new LongbridgeReplaceOrderRequest
		{
			OrderId = orderId,
			Quantity = Format(replaceMsg.Volume),
			Price = replaceMsg.Price > 0 ? Format(replaceMsg.Price) : null,
			TriggerPrice = condition?.TriggerPrice is > 0 ? Format(condition.TriggerPrice.Value) : null,
			LimitOffset = condition?.LimitOffset is > 0 ? Format(condition.LimitOffset.Value) : null,
			TrailingAmount = condition?.TrailingAmount is > 0 ? Format(condition.TrailingAmount.Value) : null,
			TrailingPercent = condition?.TrailingPercent is > 0 ? Format(condition.TrailingPercent.Value) : null,
			Remark = condition?.Remark,
		}, cancellationToken);
		_orderTransactions[orderId] = replaceMsg.TransactionId;
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
		=> new(_restClient.CancelOrder(GetOrderId(cancelMsg.OrderId, cancelMsg.OrderStringId,
			cancelMsg.OriginalTransactionId), cancellationToken));

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
		var hasRange = statusMsg.From != null || statusMsg.To != null;
		var orders = hasRange
			? await _restClient.GetHistoryOrders(statusMsg.From, statusMsg.To, cancellationToken)
			: await _restClient.GetTodayOrders(cancellationToken);
		foreach (var order in orders?.Orders ?? [])
			await ProcessOrder(order, statusMsg.TransactionId, cancellationToken);
		var executions = hasRange
			? await _restClient.GetHistoryExecutions(statusMsg.From, statusMsg.To, cancellationToken)
			: await _restClient.GetTodayExecutions(cancellationToken);
		foreach (var execution in executions?.Trades ?? [])
			await ProcessExecution(execution, statusMsg.TransactionId, cancellationToken);
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
			return;
		if (!lookupMsg.PortfolioName.IsEmpty() && !lookupMsg.PortfolioName.EqualsIgnoreCase(Portfolio))
		{
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
			return;
		}
		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = lookupMsg.TransactionId,
			PortfolioName = Portfolio,
			BoardCode = "LONG",
		}, cancellationToken);
		foreach (var balance in (await _restClient.GetBalances(cancellationToken))?.List ?? [])
			await ProcessBalance(balance, lookupMsg.TransactionId, cancellationToken);
		foreach (var channel in (await _restClient.GetPositions(cancellationToken))?.List ?? [])
		{
			foreach (var position in channel.Positions ?? [])
				await ProcessPosition(position, lookupMsg.TransactionId, cancellationToken);
		}
		await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
	}

	private async ValueTask ProcessTradePacket(LongbridgePacket packet, CancellationToken cancellationToken)
	{
		if (packet.Command != (byte)LongbridgeTradeCommand.CmdNotify)
			return;
		var notification = LongbridgeTradeNotification.Parser.ParseFrom(packet.Body);
		if (notification.Data.IsEmpty)
			return;
		var envelope = JsonConvert.DeserializeObject<LongbridgeTradePushEnvelope>(notification.Data.ToStringUtf8());
		var order = envelope?.Data;
		if (order == null || order.OrderId.IsEmpty())
			return;
		_orderTransactions.TryGetValue(order.OrderId, out var transactionId);
		var originId = transactionId != 0 ? transactionId : _orderStatusSubscriptionId;
		if (originId == 0)
			return;
		await ProcessOrderPush(order, originId, cancellationToken);
		var lastShare = order.LastShare.ToNullableDecimal();
		var lastPrice = order.LastPrice.ToNullableDecimal();
		if (lastShare is > 0 && lastPrice is > 0)
		{
			var executionId = $"{order.OrderId}:{order.UpdatedAt}:{order.ExecutedQuantity}";
			if (_executions.TryAdd(executionId))
			{
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OriginalTransactionId = originId,
					OrderId = ParseLong(order.OrderId),
					OrderStringId = order.OrderId,
					TradeStringId = executionId,
					SecurityId = order.Symbol.ToSecurityId(),
					PortfolioName = Portfolio,
					Side = order.Side.ToSide(),
					TradePrice = lastPrice,
					TradeVolume = lastShare,
					ServerTime = order.UpdatedAt.ToUtcTime(),
				}, cancellationToken);
			}
		}
	}

	private ValueTask ProcessOrder(LongbridgeOrder order, long originalTransactionId, CancellationToken cancellationToken)
	{
		if (order == null || order.OrderId.IsEmpty())
			return default;
		_orderTransactions.TryGetValue(order.OrderId, out var transactionId);
		var condition = new LongbridgeOrderCondition
		{
			NativeOrderType = order.OrderType.ToNativeOrderType(),
			TriggerPrice = order.TriggerPrice.ToNullableDecimal(),
			LimitOffset = order.LimitOffset.ToNullableDecimal(),
			TrailingAmount = order.TrailingAmount.ToNullableDecimal(),
			TrailingPercent = order.TrailingPercent.ToNullableDecimal(),
			OutsideRth = order.OutsideRth.ToOutsideRth(),
			NativeTimeInForce = order.TimeInForce.ToNativeTimeInForce(),
			Remark = order.Remark,
		};
		var quantity = order.Quantity.ToDecimal();
		var executed = order.ExecutedQuantity.ToDecimal();
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId,
			TransactionId = transactionId,
			OrderId = ParseLong(order.OrderId),
			OrderStringId = order.OrderId,
			SecurityId = order.Symbol.ToSecurityId(),
			PortfolioName = Portfolio,
			Side = order.Side.ToSide(),
			OrderType = order.OrderType.ToOrderType(),
			OrderPrice = order.Price.ToDecimal(),
			OrderVolume = quantity,
			Balance = Math.Max(0, quantity - executed),
			AveragePrice = order.ExecutedPrice.ToNullableDecimal(),
			OrderState = order.Status.ToOrderState(),
			ServerTime = order.UpdatedAt.IsEmpty(order.SubmittedAt).IsEmpty(order.SubmittedAtLegacy).ToUtcTime(),
			ExpiryDate = DateTimeOffset.TryParse(order.ExpireDate, CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal, out var expiry) ? expiry.UtcDateTime : null,
			Condition = condition,
			Error = order.Status.IsFailed() ? new InvalidOperationException(order.Message.IsEmpty("Longbridge order rejected.")) : null,
		}, cancellationToken);
	}

	private ValueTask ProcessOrderPush(LongbridgeOrderPush order, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var quantity = order.Quantity.ToDecimal();
		var executed = order.ExecutedQuantity.ToDecimal();
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId,
			OrderId = ParseLong(order.OrderId),
			OrderStringId = order.OrderId,
			SecurityId = order.Symbol.ToSecurityId(),
			PortfolioName = Portfolio,
			Side = order.Side.ToSide(),
			OrderType = order.OrderType.ToOrderType(),
			OrderPrice = order.Price.ToDecimal(),
			OrderVolume = quantity,
			Balance = Math.Max(0, quantity - executed),
			AveragePrice = order.ExecutedPrice.ToNullableDecimal(),
			OrderState = order.Status.ToOrderState(),
			ServerTime = order.UpdatedAt.IsEmpty(order.SubmittedAt).ToUtcTime(),
			Error = order.Status.IsFailed() ? new InvalidOperationException(order.Message.IsEmpty("Longbridge order rejected.")) : null,
		}, cancellationToken);
	}

	private ValueTask ProcessExecution(LongbridgeExecution execution, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (execution == null || execution.TradeId.IsEmpty() || !_executions.TryAdd(execution.TradeId))
			return default;
		_orderTransactions.TryGetValue(execution.OrderId, out var transactionId);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originalTransactionId,
			TransactionId = transactionId,
			OrderId = ParseLong(execution.OrderId),
			OrderStringId = execution.OrderId,
			TradeId = ParseLong(execution.TradeId),
			TradeStringId = execution.TradeId,
			SecurityId = execution.Symbol.ToSecurityId(),
			PortfolioName = Portfolio,
			TradePrice = execution.Price.ToDecimal(),
			TradeVolume = execution.Quantity.ToDecimal(),
			ServerTime = execution.TradeDoneAt.ToUtcTime(),
		}, cancellationToken);
	}

	private ValueTask ProcessBalance(LongbridgeAccountBalance balance, long originalTransactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = Portfolio,
			SecurityId = SecurityId.Money,
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.BeginValue, balance.TotalCash.ToNullableDecimal(), true)
		.TryAdd(PositionChangeTypes.CurrentValue, balance.NetAssets.ToNullableDecimal(), true)
		.TryAdd(PositionChangeTypes.BuyOrdersMargin, balance.BuyPower.ToNullableDecimal(), true)
		.TryAdd(PositionChangeTypes.BlockedValue, balance.MaintenanceMargin.ToNullableDecimal(), true)
		.TryAdd(PositionChangeTypes.Currency,
			Enum.TryParse<CurrencyTypes>(balance.Currency, true, out var currency) ? currency : null), cancellationToken);

	private ValueTask ProcessPosition(LongbridgeStockPosition position, long originalTransactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = Portfolio,
			SecurityId = position.Symbol.ToSecurityId(),
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, position.Quantity.ToNullableDecimal(), true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.CostPrice.ToNullableDecimal(), true)
		.TryAdd(PositionChangeTypes.Currency,
			Enum.TryParse<CurrencyTypes>(position.Currency, true, out var currency) ? currency : null), cancellationToken);

	private static void ValidateOrder(decimal volume, decimal price, LongbridgeOrderTypes type,
		LongbridgeOrderCondition condition)
	{
		if (volume <= 0 || volume != decimal.Truncate(volume))
			throw new InvalidOperationException("Longbridge order quantity must be a positive whole number.");
		if (NeedsPrice(type) && price <= 0)
			throw new InvalidOperationException($"Longbridge {type} orders require a positive price.");
		if (type is LongbridgeOrderTypes.LimitIfTouched or LongbridgeOrderTypes.MarketIfTouched && condition?.TriggerPrice is not > 0)
			throw new InvalidOperationException($"Longbridge {type} orders require TriggerPrice.");
		if (type is LongbridgeOrderTypes.TrailingLimitAmount or LongbridgeOrderTypes.TrailingMarketAmount &&
			condition?.TrailingAmount is not > 0)
			throw new InvalidOperationException($"Longbridge {type} orders require TrailingAmount.");
		if (type is LongbridgeOrderTypes.TrailingLimitPercent or LongbridgeOrderTypes.TrailingMarketPercent &&
			condition?.TrailingPercent is not > 0)
			throw new InvalidOperationException($"Longbridge {type} orders require TrailingPercent.");
	}

	private static bool NeedsPrice(LongbridgeOrderTypes type)
		=> type is LongbridgeOrderTypes.Limit or LongbridgeOrderTypes.EnhancedLimit or
			LongbridgeOrderTypes.AtAuctionLimit or LongbridgeOrderTypes.OddLot or
			LongbridgeOrderTypes.LimitIfTouched or LongbridgeOrderTypes.TrailingLimitAmount or
			LongbridgeOrderTypes.TrailingLimitPercent;

	private static string GetOrderId(long? numericId, string stringId, long transactionId)
	{
		if (!stringId.IsEmpty())
			return stringId;
		if (numericId is > 0)
			return numericId.Value.ToString(CultureInfo.InvariantCulture);
		throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(transactionId));
	}

	private static long? ParseLong(string value)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;

	private static string Format(decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);
}
