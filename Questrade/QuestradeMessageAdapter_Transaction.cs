namespace StockSharp.Questrade;

public partial class QuestradeMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var symbol = await ResolveSymbol(regMsg.SecurityId, cancellationToken);
		var account = ResolveAccount(regMsg.PortfolioName);
		var request = CreateOrderRequest(account.Number, symbol.SymbolId, regMsg.Volume, regMsg.Side,
			regMsg.OrderType ?? OrderTypes.Limit, regMsg.Price, regMsg.TimeInForce, regMsg.TillDate,
			regMsg.Condition as QuestradeOrderCondition);
		var result = await _client.PlaceOrder(account.Number, request, cancellationToken);
		var orderId = result.OrderId != 0 ? result.OrderId : result.Orders?.FirstOrDefault()?.Id ?? 0;
		if (orderId <= 0)
			throw new InvalidOperationException("Questrade order placement returned no order id.");
		_orderTransactions[orderId] = regMsg.TransactionId;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			OrderId = orderId,
			OrderStringId = orderId.ToString(CultureInfo.InvariantCulture),
			SecurityId = regMsg.SecurityId,
			PortfolioName = account.Number,
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
		var oldOrderId = GetOrderId(replaceMsg.OldOrderId, replaceMsg.OldOrderStringId,
			replaceMsg.OriginalTransactionId);
		var symbol = await ResolveSymbol(replaceMsg.SecurityId, cancellationToken);
		var account = ResolveAccount(replaceMsg.PortfolioName);
		var request = CreateOrderRequest(account.Number, symbol.SymbolId, replaceMsg.Volume, replaceMsg.Side,
			replaceMsg.OrderType ?? OrderTypes.Limit, replaceMsg.Price, replaceMsg.TimeInForce, replaceMsg.TillDate,
			replaceMsg.Condition as QuestradeOrderCondition);
		var result = await _client.ReplaceOrder(account.Number, oldOrderId, request, cancellationToken);
		var newOrderId = result.OrderId != 0 ? result.OrderId : result.Orders?.FirstOrDefault()?.Id ?? oldOrderId;
		_orderTransactions[oldOrderId] = replaceMsg.TransactionId;
		_orderTransactions[newOrderId] = replaceMsg.TransactionId;
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var orderId = GetOrderId(cancelMsg.OrderId, cancelMsg.OrderStringId, cancelMsg.OriginalTransactionId);
		var account = ResolveAccount(cancelMsg.PortfolioName);
		var result = await _client.CancelOrder(account.Number, orderId, cancellationToken);
		if (result.OrderId != 0 && result.OrderId != orderId)
			throw new InvalidOperationException("Questrade cancellation returned an unexpected order id.");
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
		var accounts = statusMsg.PortfolioName.IsEmpty()
			? _accounts
			: [ResolveAccount(statusMsg.PortfolioName)];
		foreach (var account in accounts)
		{
			foreach (var order in (await _client.GetOrders(account.Number, statusMsg.From, statusMsg.To, cancellationToken)).Orders ?? [])
				await ProcessOrder(order, account.Number, statusMsg.TransactionId, cancellationToken);
			foreach (var execution in (await _client.GetExecutions(account.Number, statusMsg.From, statusMsg.To, cancellationToken)).Executions ?? [])
				await ProcessExecution(execution, account.Number, statusMsg.TransactionId, cancellationToken);
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
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		if (!lookupMsg.IsSubscribe)
			return;
		var accounts = lookupMsg.PortfolioName.IsEmpty()
			? _accounts
			: [ResolveAccount(lookupMsg.PortfolioName)];
		foreach (var account in accounts)
		{
			await SendOutMessageAsync(new PortfolioMessage
			{
				OriginalTransactionId = lookupMsg.TransactionId,
				PortfolioName = account.Number,
				BoardCode = "QUESTRADE",
			}, cancellationToken);
			var balances = await _client.GetBalances(account.Number, cancellationToken);
			var combined = (balances.CombinedBalances ?? []).FirstOrDefault(b => b.Currency.EqualsIgnoreCase("CAD"))
				?? (balances.CombinedBalances ?? []).FirstOrDefault();
			if (combined != null)
				await ProcessCombinedBalance(account.Number, combined, lookupMsg.TransactionId, cancellationToken);
			foreach (var balance in balances.PerCurrencyBalances ?? [])
				await ProcessBalance(account.Number, balance, lookupMsg.TransactionId, cancellationToken);
			foreach (var position in (await _client.GetPositions(account.Number, cancellationToken)).Positions ?? [])
				await ProcessPosition(account.Number, position, lookupMsg.TransactionId, cancellationToken);
		}
		await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
	}

	private static QuestradeOrderRequest CreateOrderRequest(string account, long symbolId, decimal volume, Sides side,
		OrderTypes orderType, decimal price, TimeInForce? timeInForce, DateTimeOffset? tillDate,
		QuestradeOrderCondition condition)
	{
		if (volume <= 0 || volume != decimal.Truncate(volume))
			throw new InvalidOperationException("Questrade order quantity must be a positive whole number.");
		var nativeType = orderType.ToNativeOrderType(price, condition);
		decimal? limitPrice = nativeType is "Limit" or "StopLimit" ? price : null;
		decimal? stopPrice = nativeType is "Stop" or "StopLimit" ? condition?.StopPrice : null;
		if (limitPrice is <= 0)
			throw new InvalidOperationException($"Questrade {nativeType} orders require a positive limit price.");
		if (stopPrice is <= 0)
			throw new InvalidOperationException($"Questrade {nativeType} orders require a positive stop price.");
		if (condition?.IcebergQuantity is <= 0)
			throw new InvalidOperationException("Questrade iceberg quantity must be positive.");
		if (condition?.IcebergQuantity > volume)
			throw new InvalidOperationException("Questrade iceberg quantity cannot exceed order quantity.");
		return new()
		{
			AccountNumber = account,
			SymbolId = symbolId,
			Quantity = checked((long)volume),
			IcebergQuantity = condition?.IcebergQuantity is { } iceberg ? checked((long)iceberg) : null,
			LimitPrice = limitPrice,
			StopPrice = stopPrice,
			IsAllOrNone = condition?.AllOrNone == true,
			IsAnonymous = condition?.Anonymous == true,
			OrderType = nativeType,
			TimeInForce = timeInForce.ToNativeDuration(tillDate, condition),
			GoodTillDate = tillDate?.ToUniversalTime(),
			Action = side.ToNativeSide(condition),
			PrimaryRoute = condition?.PrimaryRoute.IsEmpty("AUTO") ?? "AUTO",
			SecondaryRoute = condition?.SecondaryRoute.IsEmpty("AUTO") ?? "AUTO",
		};
	}

	private async ValueTask ProcessOrder(QuestradeOrder order, string account, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (order == null || order.Id <= 0)
			return;
		_orderTransactions.TryGetValue(order.Id, out var transactionId);
		var originId = originalTransactionId != 0 ? originalTransactionId :
			transactionId != 0 ? transactionId : _orderStatusSubscriptionId;
		if (originId == 0)
			return;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originId,
			TransactionId = originalTransactionId == 0 ? 0 : transactionId,
			OrderId = order.Id,
			OrderStringId = order.Id.ToString(CultureInfo.InvariantCulture),
			SecurityId = GetSecurityId(order.SymbolId, order.Symbol),
			PortfolioName = account,
			Side = order.Side.ToSide(),
			OrderType = order.OrderType.ToOrderType(),
			OrderPrice = order.LimitPrice ?? 0,
			OrderVolume = order.TotalQuantity,
			Balance = order.OpenQuantity,
			AveragePrice = order.AverageExecutionPrice,
			OrderState = order.State.ToOrderState(),
			ServerTime = (order.UpdateTime ?? order.CreationTime ?? DateTimeOffset.UtcNow).UtcDateTime,
			TimeInForce = order.TimeInForce.ToTimeInForce(),
			ExpiryDate = order.GoodTillDate?.UtcDateTime,
			Commission = order.CommissionCharged,
			Condition = ToOrderCondition(order),
			Error = order.State.IsFailed() ? new InvalidOperationException(
				order.ClientReason.IsEmpty($"Questrade order {order.State}.")) : null,
		}, cancellationToken);
	}

	private async ValueTask ProcessExecution(QuestradeExecution execution, string account, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (execution == null || execution.Id <= 0)
			return;
		_orderTransactions.TryGetValue(execution.OrderId, out var transactionId);
		var originId = originalTransactionId != 0 ? originalTransactionId :
			transactionId != 0 ? transactionId : _orderStatusSubscriptionId;
		if (originId == 0)
			return;
		if (!_executions.TryAdd(execution.Id))
			return;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originId,
			OrderId = execution.OrderId,
			OrderStringId = execution.OrderId.ToString(CultureInfo.InvariantCulture),
			TradeId = execution.Id,
			TradeStringId = execution.ExchangeExecutionId.IsEmpty(execution.Id.ToString(CultureInfo.InvariantCulture)),
			SecurityId = GetSecurityId(execution.SymbolId, execution.Symbol),
			PortfolioName = account,
			Side = execution.Side.ToSide(),
			TradePrice = execution.Price,
			TradeVolume = execution.Quantity,
			Commission = execution.TotalCommission(),
			ServerTime = execution.Timestamp == default ? DateTime.UtcNow : execution.Timestamp.UtcDateTime,
		}, cancellationToken);
	}

	private ValueTask ProcessBalance(string account, QuestradeBalance balance, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (balance == null || balance.Currency.IsEmpty())
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = account,
			SecurityId = new() { SecurityCode = balance.Currency, BoardCode = "QUESTRADE" },
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, balance.Cash, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, balance.MarketValue, true)
		.TryAdd(PositionChangeTypes.BuyOrdersMargin, balance.BuyingPower, true)
		.TryAdd(PositionChangeTypes.BlockedValue, balance.MaintenanceExcess, true)
		.TryAdd(PositionChangeTypes.Currency,
			Enum.TryParse<CurrencyTypes>(balance.Currency, true, out var currency) ? currency : null), cancellationToken);
	}

	private ValueTask ProcessCombinedBalance(string account, QuestradeBalance balance, long originalTransactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = account,
			SecurityId = SecurityId.Money,
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.BeginValue, balance.Cash, true)
		.TryAdd(PositionChangeTypes.CurrentValue, balance.TotalEquity, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, balance.MarketValue, true)
		.TryAdd(PositionChangeTypes.BuyOrdersMargin, balance.BuyingPower, true)
		.TryAdd(PositionChangeTypes.BlockedValue, balance.MaintenanceExcess, true)
		.TryAdd(PositionChangeTypes.Currency,
			Enum.TryParse<CurrencyTypes>(balance.Currency, true, out var currency) ? currency : null), cancellationToken);

	private ValueTask ProcessPosition(string account, QuestradePosition position, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (position == null || position.SymbolId <= 0)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = account,
			SecurityId = GetSecurityId(position.SymbolId, position.Symbol),
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, position.OpenQuantity, true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.AverageEntryPrice, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, position.CurrentPrice, true)
		.TryAdd(PositionChangeTypes.RealizedPnL, position.ClosedPnL, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.OpenPnL, true), cancellationToken);
	}

	private SecurityId GetSecurityId(long symbolId, string symbol)
		=> _symbols.TryGetValue(symbolId, out var details)
			? details.ToSecurityId()
			: new()
			{
				SecurityCode = symbol.IsEmpty(symbolId.ToString(CultureInfo.InvariantCulture)),
				BoardCode = "QUESTRADE",
				Native = symbolId,
			};

	private static QuestradeOrderCondition ToOrderCondition(QuestradeOrder order)
	{
		var duration = Enum.TryParse<QuestradeOrderDurations>(order.TimeInForce, true, out var parsed)
			? parsed : QuestradeOrderDurations.Day;
		return new()
		{
			StopPrice = order.StopPrice,
			Duration = duration,
			NativeSide = order.Side.ToNativeSide(),
			IcebergQuantity = order.IcebergQuantity,
			AllOrNone = order.IsAllOrNone,
			Anonymous = order.IsAnonymous,
			PrimaryRoute = order.PrimaryRoute,
			SecondaryRoute = order.SecondaryRoute,
		};
	}

	private static long GetOrderId(long? numericId, string stringId, long transactionId)
	{
		if (numericId is > 0)
			return numericId.Value;
		if (long.TryParse(stringId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
			return parsed;
		throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(transactionId));
	}
}
