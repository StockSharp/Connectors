namespace StockSharp.CryptoCom;

public partial class CryptoComMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var parameters = CreateOrderParameters(regMsg);
		var isAdvanced = IsAdvanced(regMsg.OrderType, regMsg.Condition);
		var result = await RestClient.CreateOrderAsync(parameters, isAdvanced, cancellationToken);

		await SendRegisteredOrderAsync(result, regMsg.TransactionId, regMsg.SecurityId,
			regMsg.Side, regMsg.OrderType, regMsg.Price, regMsg.Volume.Abs(), regMsg.TimeInForce,
			regMsg.PostOnly, regMsg.PositionEffect, regMsg.Condition, parameters.ClientOrderId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		if (replaceMsg.OldOrderId is null && replaceMsg.OldOrderStringId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(replaceMsg.TransactionId));
		if (replaceMsg.Volume <= 0)
			throw new InvalidOperationException("Order volume must be positive.");
		if (replaceMsg.OrderType is not OrderTypes.Market && replaceMsg.Price <= 0)
			throw new InvalidOperationException("Order price must be positive.");

		var clientOrderId = CreateClientOrderId(replaceMsg.TransactionId, replaceMsg.UserOrderId);
		var result = await RestClient.AmendOrderAsync(new CryptoComAmendOrderParams
		{
			OrderId = replaceMsg.OldOrderId?.ToString(CultureInfo.InvariantCulture),
			OriginalClientOrderId = replaceMsg.OldOrderStringId,
			ClientOrderId = clientOrderId,
			NewPrice = replaceMsg.OrderType == OrderTypes.Market
				? null
				: replaceMsg.Price.ToString(CultureInfo.InvariantCulture),
			NewQuantity = replaceMsg.Volume.Abs().ToString(CultureInfo.InvariantCulture),
		}, IsAdvanced(replaceMsg.OrderType, replaceMsg.Condition), cancellationToken);

		await SendRegisteredOrderAsync(result, replaceMsg.TransactionId, replaceMsg.SecurityId,
			replaceMsg.Side, replaceMsg.OrderType, replaceMsg.Price, replaceMsg.Volume.Abs(),
			replaceMsg.TimeInForce, replaceMsg.PostOnly, replaceMsg.PositionEffect,
			replaceMsg.Condition, clientOrderId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		if (cancelMsg.OrderId is null && cancelMsg.OrderStringId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.TransactionId));

		var result = await RestClient.CancelOrderAsync(new CryptoComOrderIdentityParams
		{
			OrderId = cancelMsg.OrderId?.ToString(CultureInfo.InvariantCulture),
			ClientOrderId = cancelMsg.OrderStringId,
		}, IsAdvanced(cancelMsg.OrderType, cancelMsg.Condition), cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = cancelMsg.SecurityId,
			ServerTime = CurrentTime,
			PortfolioName = _portfolioName,
			OrderId = ParseLong(result?.OrderId) ?? cancelMsg.OrderId,
			OrderStringId = (result?.ClientOrderId).IsEmpty(cancelMsg.OrderStringId),
			OrderState = OrderStates.Done,
			Balance = 0m,
			OriginalTransactionId = cancelMsg.TransactionId,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException("Closing positions through group cancellation is not supported.");

		var symbol = cancelMsg.SecurityId.SecurityCode;
		var hasLocalFilters = cancelMsg.Side is not null || cancelMsg.SecurityTypes is { Length: > 0 };
		if (!hasLocalFilters)
		{
			if (cancelMsg.IsStop is not true)
			{
				await RestClient.CancelAllOrdersAsync(new CryptoComCancelAllParams
				{
					InstrumentName = symbol,
					Type = CryptoComCancelOrderTypes.Limit,
				}, false, cancellationToken);
			}

			if (cancelMsg.IsStop is not false)
			{
				await RestClient.CancelAllOrdersAsync(new CryptoComCancelAllParams
				{
					InstrumentName = symbol,
					Type = CryptoComCancelOrderTypes.Trigger,
				}, true, cancellationToken);
			}

			return;
		}

		var regular = cancelMsg.IsStop is true
			? []
			: await RestClient.GetOpenOrdersAsync(symbol, false, cancellationToken);
		var advanced = cancelMsg.IsStop is false
			? []
			: await RestClient.GetOpenOrdersAsync(symbol, true, cancellationToken);

		foreach (var item in regular.Select(static order => (order, advanced: false))
			.Concat(advanced.Select(static order => (order, advanced: true))))
		{
			var order = item.order;
			if (!IsOrderMatch(order, cancelMsg.Side, cancelMsg.SecurityTypes))
				continue;

			await RestClient.CancelOrderAsync(new CryptoComOrderIdentityParams
			{
				OrderId = order.OrderId,
				ClientOrderId = order.ClientOrderId,
			}, item.advanced, cancellationToken);

			await SendCanceledOrderAsync(order, cancelMsg.TransactionId, cancellationToken);
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

		EnsurePrivateReady();
		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = _portfolioName,
			BoardCode = BoardCodes.CryptoCom,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);

		foreach (var balance in await RestClient.GetBalancesAsync(cancellationToken))
			await SendBalanceAsync(balance, lookupMsg.TransactionId, cancellationToken);

		foreach (var position in await RestClient.GetPositionsAsync(
			lookupMsg.SecurityId?.SecurityCode, cancellationToken))
		{
			await SendPositionAsync(position, lookupMsg.TransactionId, cancellationToken);
		}

		_portfolioSubscriptionId = lookupMsg.TransactionId;
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
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

		EnsurePrivateReady();
		var symbol = statusMsg.SecurityId.SecurityCode;
		var limit = (statusMsg.Count ?? 100).Min(100).To<int>();
		var historyParameters = new CryptoComHistoryParams
		{
			InstrumentName = symbol,
			StartTime = ToUnixMillisecondsString(statusMsg.From),
			EndTime = ToUnixMillisecondsString(statusMsg.To),
			Limit = limit,
		};

		var regularOpen = await RestClient.GetOpenOrdersAsync(symbol, false, cancellationToken);
		var advancedOpen = await RestClient.GetOpenOrdersAsync(symbol, true, cancellationToken);
		var regularHistory = await RestClient.GetOrderHistoryAsync(historyParameters, false, cancellationToken);
		var advancedHistory = await RestClient.GetOrderHistoryAsync(historyParameters, true, cancellationToken);
		var orders = regularOpen.Concat(advancedOpen).Concat(regularHistory).Concat(advancedHistory)
			.Where(order => IsOrderMatch(order, statusMsg))
			.GroupBy(static order => order.OrderId.IsEmpty(order.ClientOrderId), StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.OrderByDescending(GetOrderTime).First())
			.OrderBy(GetOrderTime)
			.Skip((int)(statusMsg.Skip ?? 0).Min(int.MaxValue))
			.Take(limit)
			.ToArray();

		foreach (var order in orders)
			await SendOrderAsync(order, statusMsg.TransactionId, cancellationToken);

		var trades = await RestClient.GetUserTradesAsync(historyParameters, cancellationToken);
		foreach (var trade in trades
			.Where(trade => IsTradeMatch(trade, statusMsg))
			.OrderBy(GetTradeTime)
			.Take(limit))
		{
			await SendUserTradeAsync(trade, statusMsg.TransactionId, cancellationToken);
		}

		_orderStatusSubscriptionId = statusMsg.TransactionId;
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private CryptoComCreateOrderParams CreateOrderParameters(OrderRegisterMessage message)
	{
		if (message.Volume <= 0)
			throw new InvalidOperationException("Order volume must be positive.");
		if (message.VisibleVolume is not null)
			throw new NotSupportedException("Crypto.com Exchange does not support visible-volume orders.");
		if (message.TillDate is not null)
			throw new NotSupportedException("Crypto.com Exchange API does not support an absolute order expiration time.");

		var condition = message.Condition as CryptoComOrderCondition;
		var isAdvanced = IsAdvanced(message.OrderType, message.Condition);
		CryptoComOrderTypes type;
		string price = null;
		string referencePrice = null;
		CryptoComTriggerPriceTypesNative? referencePriceType = null;

		if (isAdvanced)
		{
			if (condition is null)
				throw new InvalidOperationException("Conditional order requires CryptoComOrderCondition.");
			if (condition.ActivationPrice is null or <= 0)
				throw new InvalidOperationException("Trigger price must be positive.");
			if (message.PostOnly == true && condition.ClosePositionPrice is null)
				throw new InvalidOperationException("A market trigger order cannot be post-only.");

			type = (condition.Type, condition.ClosePositionPrice) switch
			{
				(CryptoComOrderConditionTypes.StopLoss, null) => CryptoComOrderTypes.StopLoss,
				(CryptoComOrderConditionTypes.StopLoss, _) => CryptoComOrderTypes.StopLimit,
				(CryptoComOrderConditionTypes.TakeProfit, null) => CryptoComOrderTypes.TakeProfit,
				(CryptoComOrderConditionTypes.TakeProfit, _) => CryptoComOrderTypes.TakeProfitLimit,
				_ => throw new ArgumentOutOfRangeException(nameof(condition.Type), condition.Type, LocalizedStrings.InvalidValue),
			};
			price = condition.ClosePositionPrice?.ToString(CultureInfo.InvariantCulture);
			referencePrice = condition.ActivationPrice.Value.ToString(CultureInfo.InvariantCulture);
			referencePriceType = condition.TriggerPriceType.ToNative();
		}
		else
		{
			type = message.OrderType switch
			{
				null or OrderTypes.Limit => CryptoComOrderTypes.Limit,
				OrderTypes.Market => CryptoComOrderTypes.Market,
				_ => throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(message.OrderType, 0)),
			};
			if (type == CryptoComOrderTypes.Limit)
			{
				if (message.Price <= 0)
					throw new InvalidOperationException("Limit order price must be positive.");
				price = message.Price.ToString(CultureInfo.InvariantCulture);
			}
			else if (message.PostOnly == true)
				throw new InvalidOperationException("Market order cannot be post-only.");
		}

		var instructions = new List<CryptoComExecutionInstructions>();
		if (message.PostOnly == true)
			instructions.Add(CryptoComExecutionInstructions.PostOnly);
		if (message.PositionEffect == OrderPositionEffects.CloseOnly)
			instructions.Add(CryptoComExecutionInstructions.ReduceOnly);
		if (message.MarginMode == MarginModes.Isolated || condition?.IsolationId.IsEmpty() == false)
			instructions.Add(CryptoComExecutionInstructions.IsolatedMargin);

		return new CryptoComCreateOrderParams
		{
			InstrumentName = message.SecurityId.SecurityCode.ThrowIfEmpty(nameof(message.SecurityId.SecurityCode)),
			Side = message.Side.ToNative(),
			Type = type,
			Quantity = message.Volume.Abs().ToString(CultureInfo.InvariantCulture),
			Price = price,
			ClientOrderId = CreateClientOrderId(message.TransactionId, message.UserOrderId),
			TimeInForce = type is CryptoComOrderTypes.Market or CryptoComOrderTypes.StopLoss or CryptoComOrderTypes.TakeProfit
				? null
				: message.TimeInForce.ToNative(),
			ExecutionInstructions = instructions.Count == 0 ? null : [.. instructions],
			SpotMargin = IsSpotSecurity(message.SecurityId) && message.MarginMode is not null
				? CryptoComSpotMarginModes.Margin
				: null,
			IsolatedMarginAmount = condition?.IsolatedMarginAmount?.ToString(CultureInfo.InvariantCulture),
			IsolationId = condition?.IsolationId,
			Leverage = (condition?.Leverage ?? message.Leverage)?.ToString(CultureInfo.InvariantCulture),
			ReferencePrice = referencePrice,
			ReferencePriceType = referencePriceType,
		};
	}

	private ValueTask SendRegisteredOrderAsync(CryptoComOrderAck result, long transactionId,
		SecurityId securityId, Sides side, OrderTypes? orderType, decimal price, decimal volume,
		TimeInForce? timeInForce, bool? postOnly, OrderPositionEffects? positionEffect,
		OrderCondition condition, string clientOrderId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = securityId,
			ServerTime = CurrentTime,
			PortfolioName = _portfolioName,
			Side = side,
			OrderVolume = volume,
			Balance = volume,
			OrderPrice = condition is CryptoComOrderCondition nativeCondition
				? nativeCondition.ClosePositionPrice ?? 0m
				: orderType == OrderTypes.Market ? 0m : price,
			OrderType = orderType ?? OrderTypes.Limit,
			OrderState = OrderStates.Active,
			OrderId = ParseLong(result?.OrderId),
			OrderStringId = (result?.ClientOrderId).IsEmpty(clientOrderId),
			TransactionId = transactionId,
			OriginalTransactionId = transactionId,
			TimeInForce = timeInForce,
			PostOnly = postOnly,
			PositionEffect = positionEffect,
			Condition = condition,
		}, cancellationToken);

	private ValueTask SendCanceledOrderAsync(CryptoComOrder order, long originalTransactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = ToSecurityId(order.InstrumentName),
			ServerTime = CurrentTime,
			PortfolioName = _portfolioName,
			OrderId = ParseLong(order.OrderId),
			OrderStringId = order.ClientOrderId,
			OrderState = OrderStates.Done,
			Balance = 0m,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);

	private ValueTask SendOrderAsync(CryptoComOrder order, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (order?.InstrumentName.IsEmpty() != false)
			return default;

		var total = order.Quantity.ToDecimal();
		var filled = order.CumulativeQuantity.ToDecimal() ?? 0m;
		var orderType = ToOrderType(order, out var condition);
		var transactionId = ParseTransactionId(order.ClientOrderId);
		var state = order.Status.ToStockSharp();

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = ToSecurityId(order.InstrumentName),
			ServerTime = GetOrderTime(order),
			PortfolioName = _portfolioName,
			Side = order.Side.ToStockSharp(),
			OrderVolume = total,
			Balance = total is decimal volume ? (volume - filled).Max(0m) : null,
			OrderPrice = order.LimitPrice.ToDecimal() ?? 0m,
			OrderType = orderType,
			OrderState = state,
			OrderId = ParseLong(order.OrderId),
			OrderStringId = order.ClientOrderId,
			TransactionId = transactionId,
			OriginalTransactionId = originalTransactionId,
			TimeInForce = order.TimeInForce.ToStockSharp(),
			PostOnly = order.ExecutionInstructions?.Contains(CryptoComExecutionInstructions.PostOnly),
			PositionEffect = order.ExecutionInstructions?.Contains(CryptoComExecutionInstructions.ReduceOnly) == true
				? OrderPositionEffects.CloseOnly
				: OrderPositionEffects.Default,
			Condition = condition,
			Error = state == OrderStates.Failed
				? new InvalidOperationException(order.RejectReason.IsEmpty(order.Reason).IsEmpty("Order rejected by Crypto.com Exchange."))
				: null,
		}, cancellationToken);
	}

	private ValueTask SendUserTradeAsync(CryptoComUserTrade trade, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (trade?.InstrumentName.IsEmpty() != false)
			return default;

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = ToSecurityId(trade.InstrumentName),
			ServerTime = GetTradeTime(trade),
			PortfolioName = _portfolioName,
			Side = trade.Side.ToStockSharp(),
			OrderId = ParseLong(trade.OrderId),
			OrderStringId = trade.ClientOrderId,
			TradeId = ParseLong(trade.TradeId),
			TradeStringId = trade.TradeId,
			TradePrice = trade.Price.ToDecimal(),
			TradeVolume = trade.Quantity.ToDecimal(),
			Commission = trade.Fees.ToDecimal(),
			CommissionCurrency = trade.FeeInstrumentName,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private async ValueTask SendBalanceAsync(CryptoComBalance balance, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (balance is null)
			return;

		var current = balance.MarginBalance.ToDecimal() ?? balance.CashBalance.ToDecimal();
		var available = balance.AvailableBalance.ToDecimal();
		var securityId = balance.InstrumentName.IsEmpty()
			? default
			: balance.InstrumentName.ToStockSharp(BoardCodes.CryptoCom);

		await SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = securityId,
			ServerTime = CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, current, true)
		.TryAdd(PositionChangeTypes.BlockedValue,
			current is decimal total && available is decimal free ? (total - free).Max(0m) : null, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, balance.UnrealizedPnl.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.RealizedPnL, balance.RealizedPnl.ToDecimal(), true),
			cancellationToken);

		foreach (var collateral in balance.PositionBalances ?? [])
			await SendCollateralAsync(collateral, originalTransactionId, cancellationToken);

		foreach (var isolated in balance.IsolatedPositions ?? [])
			await SendBalanceAsync(isolated, originalTransactionId, cancellationToken);
	}

	private ValueTask SendCollateralAsync(CryptoComCollateralBalance collateral,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (collateral?.InstrumentName.IsEmpty() != false)
			return default;

		var current = collateral.Quantity.ToDecimal();
		var available = collateral.AvailableQuantity.ToDecimal();
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = collateral.InstrumentName.ToStockSharp(BoardCodes.CryptoCom),
			ServerTime = CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, current, true)
		.TryAdd(PositionChangeTypes.BlockedValue,
			collateral.ReservedQuantity.ToDecimal()
			?? (current is decimal total && available is decimal free ? (total - free).Max(0m) : null), true),
			cancellationToken);
	}

	private ValueTask SendPositionAsync(CryptoComPosition position, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (position?.InstrumentName.IsEmpty() != false)
			return default;

		var quantity = position.Quantity.ToDecimal();
		var cost = position.Cost.ToDecimal();
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = ToSecurityId(position.InstrumentName),
			ServerTime = position.UpdateTime is long time ? time.FromUnix(false) : CurrentTime,
			OriginalTransactionId = originalTransactionId,
			Side = quantity switch
			{
				> 0m => Sides.Buy,
				< 0m => Sides.Sell,
				_ => null,
			},
		}
		.TryAdd(PositionChangeTypes.CurrentValue, quantity, true)
		.TryAdd(PositionChangeTypes.AveragePrice,
			quantity is not null and not 0m && cost is decimal totalCost ? totalCost.Abs() / quantity.Value.Abs() : null, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, position.MarkPrice.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.OpenPositionPnl.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.RealizedPnL, position.SessionPnl.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.Leverage, position.Leverage.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.LiquidationPrice, position.LiquidationPrice.ToDecimal(), true),
			cancellationToken);
	}

	private static bool IsAdvanced(OrderTypes? orderType, OrderCondition condition)
		=> orderType == OrderTypes.Conditional || condition is CryptoComOrderCondition;

	private static bool IsSpotSecurity(SecurityId securityId)
		=> securityId.BoardCode.EqualsIgnoreCase(BoardCodes.CryptoCom)
			|| (securityId.BoardCode.IsEmpty() && IsSpotSymbol(securityId.SecurityCode));

	private static bool IsOrderMatch(CryptoComOrder order, Sides? side, SecurityTypes[] securityTypes)
	{
		if (order?.InstrumentName.IsEmpty() != false)
			return false;
		if (side is { } expectedSide && order.Side.ToStockSharp() != expectedSide)
			return false;
		if (securityTypes is not { Length: > 0 })
			return true;

		var type = IsSpotSymbol(order.InstrumentName)
			? SecurityTypes.CryptoCurrency
			: SecurityTypes.Future;
		return securityTypes.Contains(type);
	}

	private static bool IsOrderMatch(CryptoComOrder order, OrderStatusMessage filter)
	{
		if (order?.InstrumentName.IsEmpty() != false)
			return false;
		if (filter.OrderId is long orderId && ParseLong(order.OrderId) != orderId)
			return false;
		if (!filter.OrderStringId.IsEmpty() && !order.ClientOrderId.EqualsIgnoreCase(filter.OrderStringId))
			return false;
		if (filter.Side is { } side && order.Side.ToStockSharp() != side)
			return false;
		if (filter.States.Length > 0 && !filter.States.Contains(order.Status.ToStockSharp()))
			return false;

		var ids = filter.SecurityIds;
		if (filter.SecurityId != default && !order.InstrumentName.EqualsIgnoreCase(filter.SecurityId.SecurityCode))
			return false;
		return ids.Length == 0 || ids.Any(id => order.InstrumentName.EqualsIgnoreCase(id.SecurityCode));
	}

	private static bool IsTradeMatch(CryptoComUserTrade trade, OrderStatusMessage filter)
	{
		if (trade?.InstrumentName.IsEmpty() != false)
			return false;
		if (filter.OrderId is long orderId && ParseLong(trade.OrderId) != orderId)
			return false;
		if (!filter.OrderStringId.IsEmpty() && !trade.ClientOrderId.EqualsIgnoreCase(filter.OrderStringId))
			return false;
		if (filter.Side is { } side && trade.Side.ToStockSharp() != side)
			return false;
		if (filter.SecurityId != default && !trade.InstrumentName.EqualsIgnoreCase(filter.SecurityId.SecurityCode))
			return false;
		return filter.SecurityIds.Length == 0 ||
			filter.SecurityIds.Any(id => trade.InstrumentName.EqualsIgnoreCase(id.SecurityCode));
	}

	private static OrderTypes ToOrderType(CryptoComOrder order, out CryptoComOrderCondition condition)
	{
		condition = null;
		switch (order.OrderType)
		{
			case CryptoComOrderTypes.Market:
				return OrderTypes.Market;
			case CryptoComOrderTypes.Limit:
				return OrderTypes.Limit;
			case CryptoComOrderTypes.StopLoss:
			case CryptoComOrderTypes.StopLimit:
				condition = CreateCondition(order, CryptoComOrderConditionTypes.StopLoss);
				return OrderTypes.Conditional;
			case CryptoComOrderTypes.TakeProfit:
			case CryptoComOrderTypes.TakeProfitLimit:
				condition = CreateCondition(order, CryptoComOrderConditionTypes.TakeProfit);
				return OrderTypes.Conditional;
			default:
				throw new ArgumentOutOfRangeException(nameof(order.OrderType), order.OrderType, LocalizedStrings.InvalidValue);
		}
	}

	private static CryptoComOrderCondition CreateCondition(CryptoComOrder order,
		CryptoComOrderConditionTypes type)
		=> new()
		{
			Type = type,
			ActivationPrice = order.ReferencePrice.ToDecimal(),
			ClosePositionPrice = order.OrderType is CryptoComOrderTypes.StopLimit or CryptoComOrderTypes.TakeProfitLimit
				? order.LimitPrice.ToDecimal()
				: null,
			TriggerPriceType = order.ReferencePriceType?.ToStockSharp() ?? CryptoComTriggerPriceTypes.Mark,
			IsolationId = order.IsolationId,
		};

	private static DateTime GetOrderTime(CryptoComOrder order)
		=> (order.UpdateTime ?? order.TransactionTime ?? order.CreateTime) is long time
			? time.FromUnix(false)
			: DateTime.UtcNow;

	private static DateTime GetTradeTime(CryptoComUserTrade trade)
	{
		if (trade.TransactionTimeNanoseconds is long nanoseconds)
			return (nanoseconds / 1_000_000L).FromUnix(false);
		return (trade.TransactionTime ?? trade.CreateTime) is long time
			? time.FromUnix(false)
			: DateTime.UtcNow;
	}

	private static string ToUnixMillisecondsString(DateTime? time)
		=> time is { } value
			? value.ToUniversalTime().ToUnixMilliseconds().ToString(CultureInfo.InvariantCulture)
			: null;
}
