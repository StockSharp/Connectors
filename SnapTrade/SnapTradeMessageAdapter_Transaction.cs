namespace StockSharp.SnapTrade;

public partial class SnapTradeMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		var accountId = ResolvePortfolio(regMsg.PortfolioName);
		var condition = regMsg.Condition as SnapTradeOrderCondition ?? new();
		if (condition.NotionalValue is not > 0 && regMsg.Volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Volume), regMsg.Volume,
				"SnapTrade requires a positive share quantity or notional value.");

		var symbol = await ResolveSymbol(regMsg.SecurityId.SecurityCode, cancellationToken);
		var securityType = symbol.Type?.Code.ToSecurityType();
		if (securityType is not SecurityTypes.Stock and not SecurityTypes.Etf)
			throw new NotSupportedException(
				$"This SnapTrade connector routes stock and ETF orders only, not '{symbol.Type?.Code}'.");

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		var nativeType = GetNativeOrderType(orderType, regMsg.Price, condition.StopPrice);
		ValidateOrder(regMsg.Price, condition, nativeType);
		var duration = GetNativeDuration(regMsg.TimeInForce, regMsg.TillDate,
			condition.IsGoodTillCanceled, true);
		if (condition.NotionalValue != null &&
			(nativeType != "Market" || duration != "Day"))
			throw new NotSupportedException(
				"SnapTrade notional orders require Market order type and Day duration.");

		var request = new SnapTradePlaceOrderRequest
		{
			AccountId = accountId,
			Action = regMsg.Side == Sides.Sell ? "SELL" : "BUY",
			Symbol = symbol.Symbol,
			OrderType = nativeType,
			TimeInForce = duration,
			TradingSession = condition.IsExtendedHours ? "EXTENDED" : "REGULAR",
			ExpiryDate = duration == "GTD" ? regMsg.TillDate?.NormalizeUtc() : null,
			Price = nativeType is "Limit" or "StopLimit" ? regMsg.Price : null,
			Stop = nativeType is "Stop" or "StopLimit" ? condition.StopPrice : null,
			Units = condition.NotionalValue == null ? regMsg.Volume : null,
			NotionalValue = condition.NotionalValue,
			ClientOrderId = Guid.NewGuid().ToString(),
		};
		var order = await _client.PlaceOrder(request, cancellationToken);
		if (order?.BrokerageOrderId.IsEmpty() != false)
			throw new InvalidOperationException(
				"SnapTrade accepted the order without returning a brokerage order ID.");
		CompleteOrder(order, request, symbol, CurrentTime);
		_orderTransactions[order.BrokerageOrderId] = regMsg.TransactionId;
		_transactionOrders[regMsg.TransactionId] = order.BrokerageOrderId;
		await ProcessOrder(order, regMsg.TransactionId, false, true, cancellationToken);
		_lastPoll = CurrentTime;
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		var accountId = ResolvePortfolio(replaceMsg.PortfolioName);
		var orderId = replaceMsg.OldOrderStringId;
		if (orderId.IsEmpty() &&
			_transactionOrders.TryGetValue(replaceMsg.OriginalTransactionId, out var mappedOrderId))
			orderId = mappedOrderId;
		if (orderId.IsEmpty())
			throw new InvalidOperationException(
				LocalizedStrings.OrderNoExchangeId.Put(replaceMsg.OriginalTransactionId));

		var condition = replaceMsg.Condition as SnapTradeOrderCondition ?? new();
		if (condition.NotionalValue != null)
			throw new NotSupportedException("SnapTrade order replacement does not accept notional value.");
		if (condition.IsExtendedHours)
			throw new NotSupportedException(
				"SnapTrade order replacement does not expose the trading-session field.");
		if (replaceMsg.Volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(replaceMsg.Volume), replaceMsg.Volume,
				"SnapTrade requires a positive replacement quantity.");

		var symbol = await ResolveSymbol(replaceMsg.SecurityId.SecurityCode, cancellationToken);
		var orderType = replaceMsg.OrderType ?? OrderTypes.Limit;
		var nativeType = GetNativeOrderType(orderType, replaceMsg.Price, condition.StopPrice);
		ValidateOrder(replaceMsg.Price, condition, nativeType);
		if (replaceMsg.TillDate != null)
			throw new NotSupportedException("SnapTrade replacement does not support GTD expiry.");
		var duration = GetNativeDuration(replaceMsg.TimeInForce, null,
			condition.IsGoodTillCanceled, false);
		var order = await _client.ReplaceOrder(accountId, new SnapTradeReplaceOrderRequest
		{
			BrokerageOrderId = orderId,
			Action = replaceMsg.Side == Sides.Sell ? "SELL" : "BUY",
			OrderType = nativeType,
			TimeInForce = duration,
			Price = nativeType is "Limit" or "StopLimit" ? replaceMsg.Price : null,
			Symbol = symbol.Symbol,
			Stop = nativeType is "Stop" or "StopLimit" ? condition.StopPrice : null,
			Units = replaceMsg.Volume,
		}, cancellationToken);
		if (order?.BrokerageOrderId.IsEmpty() != false)
			throw new InvalidOperationException(
				"SnapTrade accepted the replacement without returning a brokerage order ID.");
		var nativeRequest = new SnapTradePlaceOrderRequest
		{
			AccountId = accountId,
			Action = replaceMsg.Side == Sides.Sell ? "SELL" : "BUY",
			Symbol = symbol.Symbol,
			OrderType = nativeType,
			TimeInForce = duration,
			Price = nativeType is "Limit" or "StopLimit" ? replaceMsg.Price : null,
			Stop = nativeType is "Stop" or "StopLimit" ? condition.StopPrice : null,
			Units = replaceMsg.Volume,
		};
		CompleteOrder(order, nativeRequest, symbol, CurrentTime);
		_orderTransactions[order.BrokerageOrderId] = replaceMsg.TransactionId;
		_transactionOrders[replaceMsg.TransactionId] = order.BrokerageOrderId;
		await ProcessOrder(order, replaceMsg.TransactionId, false, true, cancellationToken);
		_lastPoll = CurrentTime;
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		var accountId = ResolvePortfolio(cancelMsg.PortfolioName);
		var orderId = cancelMsg.OrderStringId;
		if (orderId.IsEmpty() &&
			_transactionOrders.TryGetValue(cancelMsg.OriginalTransactionId, out var mappedOrderId))
			orderId = mappedOrderId;
		if (orderId.IsEmpty())
			throw new InvalidOperationException(
				LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		var result = await _client.CancelOrder(accountId,
			new() { BrokerageOrderId = orderId }, cancellationToken);
		if (result?.BrokerageOrderId.IsEmpty() != false)
			throw new InvalidOperationException(
				"SnapTrade accepted the cancellation without returning a brokerage order ID.");
		_cancelTransactions[orderId] = cancelMsg.TransactionId;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = cancelMsg.TransactionId,
			OrderStringId = orderId,
			PortfolioName = accountId,
			OrderState = OrderStates.Pending,
			ServerTime = CurrentTime,
		}, cancellationToken);
		_lastPoll = CurrentTime;
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

		ResolvePortfolio(statusMsg.PortfolioName);
		await SendOrderSnapshot(statusMsg.TransactionId, statusMsg, true, cancellationToken);
		if (statusMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
		else
		{
			_orderStatusSubscriptionId = statusMsg.TransactionId;
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		}
		_lastPoll = CurrentTime;
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		if (!lookupMsg.IsSubscribe)
		{
			if (_portfolioSubscriptionId == lookupMsg.OriginalTransactionId)
			{
				_portfolioSubscriptionId = 0;
				_portfolioFilter = null;
			}
			return;
		}

		ResolvePortfolio(lookupMsg.PortfolioName);
		await SendPortfolioSnapshot(lookupMsg.TransactionId, lookupMsg.PortfolioName, true,
			cancellationToken);
		if (lookupMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
		else
		{
			_portfolioSubscriptionId = lookupMsg.TransactionId;
			_portfolioFilter = lookupMsg.PortfolioName;
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		}
		_lastPoll = CurrentTime;
	}

	private async Task SendOrderSnapshot(long originalTransactionId, OrderStatusMessage filter,
		bool isLookup, CancellationToken cancellationToken)
	{
		var accountId = ResolvePortfolio(filter?.PortfolioName);
		SnapTradeOrder[] orders;
		if (isLookup)
			orders = await _client.GetOrders(accountId, "all", 90, cancellationToken) ?? [];
		else if (_orderPollCursor++ % 2 == 0)
			orders = (await _client.GetRecentOrders(accountId, cancellationToken))?.Orders ?? [];
		else
			orders = await _client.GetOrders(accountId, "open", 90, cancellationToken) ?? [];
		var skip = Math.Max(0, filter?.Skip ?? 0);
		var left = Math.Max(0, filter?.Count ?? long.MaxValue);
		foreach (var order in orders.Where(order => IsOrderMatch(order, filter))
			.OrderByDescending(GetOrderTime))
		{
			if (skip > 0)
			{
				skip--;
				continue;
			}
			if (left <= 0)
				break;
			await ProcessOrder(order, originalTransactionId, isLookup, isLookup,
				cancellationToken);
			left--;
		}
	}

	private async ValueTask ProcessOrder(SnapTradeOrder order, long originalTransactionId,
		bool isLookup, bool isForced, CancellationToken cancellationToken)
	{
		if (order?.BrokerageOrderId.IsEmpty() != false)
			return;
		var state = order.Status.ToOrderState();
		var volume = order.TotalQuantity ??
			(order.OpenQuantity ?? 0) + (order.FilledQuantity ?? 0) +
			(order.CanceledQuantity ?? 0);
		var balance = order.OpenQuantity ?? (state is OrderStates.Done or OrderStates.Failed
			? 0 : Math.Max(0, volume - (order.FilledQuantity ?? 0) -
				(order.CanceledQuantity ?? 0)));
		var updateTime = GetOrderTime(order);
		var signature = $"{order.Status}|{volume.ToString(CultureInfo.InvariantCulture)}|" +
			$"{balance.ToString(CultureInfo.InvariantCulture)}|" +
			$"{(order.FilledQuantity ?? 0).ToString(CultureInfo.InvariantCulture)}|" +
			$"{(order.ExecutionPrice ?? 0).ToString(CultureInfo.InvariantCulture)}|{updateTime:O}";
		if (!isForced && _orderSignatures.TryGetValue(order.BrokerageOrderId, out var previous) &&
			previous == signature)
			return;
		_orderSignatures[order.BrokerageOrderId] = signature;

		var transactionId = _orderTransactions.TryGetValue(order.BrokerageOrderId,
			out var knownTransactionId) ? knownTransactionId : 0;
		var originId = isLookup ? originalTransactionId
			: transactionId != 0 ? transactionId : originalTransactionId;
		if (!isLookup && state == OrderStates.Done &&
			order.Status.EqualsIgnoreCase("CANCELED") &&
			_cancelTransactions.TryGetValue(order.BrokerageOrderId, out var cancelTransactionId))
			originId = cancelTransactionId;

		var orderType = order.OrderType.ToOrderType();
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originId,
			TransactionId = isLookup ? transactionId : 0,
			OrderStringId = order.BrokerageOrderId,
			PortfolioName = ResolvePortfolio(null),
			SecurityId = order.ToSecurityId(),
			Side = order.Action.ToSide(),
			OrderType = orderType,
			OrderPrice = order.LimitPrice ?? 0,
			OrderVolume = volume == 0 ? null : volume,
			Balance = balance,
			OrderState = state,
			TimeInForce = order.TimeInForce.ToTimeInForce(),
			ExpiryDate = order.ExpiryDate?.NormalizeUtc(),
			ServerTime = updateTime,
			AveragePrice = order.ExecutionPrice,
			Condition = orderType == OrderTypes.Conditional
				? new SnapTradeOrderCondition { StopPrice = order.StopPrice } : null,
			Error = state == OrderStates.Failed
				? new InvalidOperationException($"SnapTrade order entered state {order.Status}.") : null,
		}, cancellationToken);

		await ProcessAggregateFill(order, originalTransactionId, isLookup, cancellationToken);
		if (state is OrderStates.Done or OrderStates.Failed)
			_cancelTransactions.Remove(order.BrokerageOrderId);
	}

	private async ValueTask ProcessAggregateFill(SnapTradeOrder order, long originalTransactionId,
		bool isLookup, CancellationToken cancellationToken)
	{
		var filled = order.FilledQuantity ?? 0;
		var previous = _filledQuantities.TryGetValue(order.BrokerageOrderId, out var value)
			? value : 0;
		_filledQuantities[order.BrokerageOrderId] = Math.Max(previous, filled);
		var delta = isLookup ? filled : Math.Max(0, filled - previous);
		if (delta <= 0 || order.ExecutionPrice is not > 0)
			return;
		var transactionId = _orderTransactions.TryGetValue(order.BrokerageOrderId,
			out var knownTransactionId) ? knownTransactionId : 0;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = isLookup ? originalTransactionId
				: transactionId != 0 ? transactionId : originalTransactionId,
			OrderStringId = order.BrokerageOrderId,
			PortfolioName = ResolvePortfolio(null),
			SecurityId = order.ToSecurityId(),
			Side = order.Action.ToSide(),
			TradePrice = order.ExecutionPrice,
			TradeVolume = delta,
			ServerTime = (order.TimeExecuted ?? order.TimeUpdated ?? order.TimePlaced ?? CurrentTime)
				.NormalizeUtc(),
		}, cancellationToken);
	}

	private async Task SendPortfolioSnapshot(long originalTransactionId, string portfolioName,
		bool isLookup, CancellationToken cancellationToken)
	{
		var accountId = ResolvePortfolio(portfolioName);
		var balances = await _client.GetBalances(accountId, cancellationToken) ?? [];
		var positionResponse = await _client.GetPositions(accountId, cancellationToken);
		var positions = positionResponse?.Results ?? [];
		var currency = _account.Balance?.Total?.Currency.ToCurrency();

		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = accountId,
			BoardCode = SnapTradeExtensions.BoardCode,
			Currency = currency,
		}, cancellationToken);
		foreach (var balance in balances)
		{
			if (balance == null)
				continue;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = accountId,
				SecurityId = SecurityId.Money,
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, balance.Cash, true)
			.TryAdd(PositionChangeTypes.BuyOrdersMargin, balance.BuyingPower)
			.TryAdd(PositionChangeTypes.Currency, balance.Currency?.Code.ToCurrency()),
				cancellationToken);
		}

		var previousPositions = _positionIds.ToArray();
		_positionIds.Clear();
		foreach (var position in positions)
		{
			if (position?.Instrument?.Symbol.IsEmpty() != false)
				continue;
			var securityId = position.Instrument.ToSecurityId();
			var key = GetPositionKey(securityId);
			_positionIds[key] = securityId;
			var pnl = position.Price is decimal price && position.CostBasis is decimal cost
				? (price - cost) * (position.Units ?? 0) : (decimal?)null;
			var serverTime = positionResponse?.DataFreshness?.AsOf ?? CurrentTime;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = accountId,
				SecurityId = securityId,
				ServerTime = serverTime.NormalizeUtc(),
			}
			.TryAdd(PositionChangeTypes.CurrentValue, position.Units, true)
			.TryAdd(PositionChangeTypes.AveragePrice, position.CostBasis)
			.TryAdd(PositionChangeTypes.CurrentPrice, position.Price)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, pnl)
			.TryAdd(PositionChangeTypes.Currency, position.Currency.ToCurrency()),
				cancellationToken);
		}
		if (!isLookup)
		{
			foreach (var previous in previousPositions.Where(previous =>
				!_positionIds.ContainsKey(previous.Key)))
				await SendOutMessageAsync(new PositionChangeMessage
				{
					OriginalTransactionId = originalTransactionId,
					PortfolioName = accountId,
					SecurityId = previous.Value,
					ServerTime = CurrentTime,
				}.TryAdd(PositionChangeTypes.CurrentValue, 0m, true), cancellationToken);
		}
	}

	private static bool IsOrderMatch(SnapTradeOrder order, OrderStatusMessage filter)
	{
		if (order?.BrokerageOrderId.IsEmpty() != false)
			return false;
		if (filter == null)
			return true;
		if (!filter.OrderStringId.IsEmpty() &&
			!filter.OrderStringId.EqualsIgnoreCase(order.BrokerageOrderId))
			return false;
		var securityId = order.ToSecurityId();
		if (filter.SecurityId != default &&
			!filter.SecurityId.SecurityCode.EqualsIgnoreCase(securityId.SecurityCode))
			return false;
		if (filter.SecurityIds.Length > 0 && !filter.SecurityIds.Any(id =>
			id.SecurityCode.EqualsIgnoreCase(securityId.SecurityCode)))
			return false;
		if (filter.Side is Sides side && side != order.Action.ToSide())
			return false;
		var volume = order.TotalQuantity ?? 0;
		if (filter.Volume is decimal filterVolume && volume != filterVolume)
			return false;
		var state = order.Status.ToOrderState();
		if (filter.States.Length > 0 && !filter.States.Contains(state))
			return false;
		var time = GetOrderTime(order);
		if (filter.From is DateTime from && time < from.NormalizeUtc())
			return false;
		return filter.To is not DateTime to || time <= to.NormalizeUtc();
	}

	private static string GetNativeOrderType(OrderTypes orderType, decimal price,
		decimal? stopPrice)
		=> orderType switch
		{
			OrderTypes.Market => "Market",
			OrderTypes.Limit => "Limit",
			OrderTypes.Conditional when stopPrice is > 0 && price > 0 => "StopLimit",
			OrderTypes.Conditional when stopPrice is > 0 => "Stop",
			_ => throw new NotSupportedException($"SnapTrade does not support {orderType} orders."),
		};

	private static void ValidateOrder(decimal price, SnapTradeOrderCondition condition,
		string nativeType)
	{
		if (nativeType is "Limit" or "StopLimit" && price <= 0)
			throw new ArgumentOutOfRangeException(nameof(price), price,
				"SnapTrade requires a positive limit price.");
		if (nativeType is "Stop" or "StopLimit" && condition.StopPrice is not > 0)
			throw new ArgumentOutOfRangeException(nameof(condition.StopPrice), condition.StopPrice,
				"SnapTrade requires a positive stop price.");
		if (condition.IsExtendedHours && nativeType != "Limit")
			throw new NotSupportedException(
				"SnapTrade extended-hours routing is supported only for limit orders.");
	}

	private static string GetNativeDuration(TimeInForce? timeInForce, DateTime? tillDate,
		bool isGoodTillCanceled, bool isGtdSupported)
	{
		if (tillDate != null)
		{
			if (!isGtdSupported)
				throw new NotSupportedException("This SnapTrade operation does not support GTD expiry.");
			return "GTD";
		}
		return timeInForce switch
		{
			TimeInForce.MatchOrCancel => "FOK",
			TimeInForce.CancelBalance => "IOC",
			_ when isGoodTillCanceled => "GTC",
			_ => "Day",
		};
	}

	private static DateTime GetOrderTime(SnapTradeOrder order)
		=> (order?.TimeUpdated ?? order?.TimeExecuted ?? order?.TimePlaced ?? DateTime.UtcNow)
			.NormalizeUtc();

	private static string GetPositionKey(SecurityId securityId)
		=> $"{securityId.SecurityCode}@{securityId.BoardCode}";

	private static void CompleteOrder(SnapTradeOrder order,
		SnapTradePlaceOrderRequest request, SnapTradeUniversalSymbol symbol, DateTime time)
	{
		order.Status = order.Status.IsEmpty("PENDING");
		order.UniversalSymbol ??= symbol;
		order.Action = order.Action.IsEmpty(request.Action);
		order.OrderType = order.OrderType.IsEmpty(request.OrderType);
		order.TimeInForce = order.TimeInForce.IsEmpty(request.TimeInForce);
		order.LimitPrice ??= request.Price;
		order.StopPrice ??= request.Stop;
		order.TotalQuantity ??= request.Units;
		order.OpenQuantity ??= request.Units;
		order.TimePlaced ??= time.NormalizeUtc();
		order.ExpiryDate ??= request.ExpiryDate;
	}
}
