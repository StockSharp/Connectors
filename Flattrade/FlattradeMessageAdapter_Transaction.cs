namespace StockSharp.Flattrade;

public partial class FlattradeMessageAdapter
{
	private readonly SynchronizedDictionary<string, long> _orderTransactions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, string> _transactionOrders = [];
	private readonly SynchronizedSet<string> _tradeIds = new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsurePortfolio(regMsg.PortfolioName);
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		ValidateOrderType(orderType);
		if (regMsg.TimeInForce == TimeInForce.MatchOrCancel)
			throw new NotSupportedException("Flattrade does not expose fill-or-kill orders.");

		var condition = regMsg.Condition as FlattradeOrderCondition;
		var triggerPrice = condition?.TriggerPrice;
		if (orderType == OrderTypes.Conditional && triggerPrice is not > 0)
			throw new InvalidOperationException("A positive trigger price is required for a Flattrade stop order.");
		if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Price), regMsg.Price, "A positive limit price is required.");

		var quantity = ToQuantity(regMsg.Volume, nameof(regMsg.Volume), false);
		var disclosedQuantity = ToQuantity(condition?.DisclosedVolume ?? 0, nameof(condition.DisclosedVolume), true);
		if (disclosedQuantity > quantity)
			throw new ArgumentOutOfRangeException(nameof(condition.DisclosedVolume), disclosedQuantity,
				"Disclosed quantity cannot exceed order quantity.");

		var instrument = await GetInstrument(regMsg.SecurityId.ToInstrumentKey(), cancellationToken);
		var product = condition?.Product ?? DefaultProduct;
		var orderId = await _restClient.PlaceOrder(new FlattradePlaceOrderRequest
		{
			UserId = UserId,
			AccountId = _resolvedAccountId,
			Side = regMsg.Side.ToNative(),
			Product = product.ToNative(),
			Exchange = instrument.Exchange,
			TradingSymbol = instrument.TradingSymbol,
			Quantity = quantity.ToString(CultureInfo.InvariantCulture),
			DisclosedQuantity = disclosedQuantity.ToString(CultureInfo.InvariantCulture),
			PriceType = orderType.ToPriceType(regMsg.Price),
			Price = FormatPrice(orderType is OrderTypes.Market ? 0 : regMsg.Price),
			TriggerPrice = triggerPrice is > 0 ? FormatPrice(triggerPrice.Value) : null,
			Retention = regMsg.TimeInForce.ToRetention(),
			AfterMarket = condition?.IsAfterMarket == true ? "Yes" : null,
			Remarks = condition?.Remarks,
			StopLossPrice = FormatOptional(condition?.StopLossPrice),
			ProfitPrice = FormatOptional(condition?.ProfitPrice),
			TrailingPrice = FormatOptional(condition?.TrailingPrice),
		}, cancellationToken);

		RememberOrder(orderId, regMsg.TransactionId);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			OrderStringId = orderId,
			SecurityId = regMsg.SecurityId,
			PortfolioName = _resolvedAccountId,
			OrderType = orderType,
			Side = regMsg.Side,
			TimeInForce = regMsg.TimeInForce ?? TimeInForce.PutInQueue,
			OrderPrice = regMsg.Price,
			OrderVolume = regMsg.Volume,
			Balance = regMsg.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = CurrentTime,
			Condition = CreateCondition(product, triggerPrice, condition?.DisclosedVolume,
				condition?.IsAfterMarket == true, condition?.StopLossPrice, condition?.ProfitPrice,
				condition?.TrailingPrice, condition?.Remarks),
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		EnsurePortfolio(replaceMsg.PortfolioName);
		if (replaceMsg.TimeInForce == TimeInForce.MatchOrCancel)
			throw new NotSupportedException("Flattrade does not expose fill-or-kill orders.");

		var current = await ResolveOrder(replaceMsg.OldOrderStringId, replaceMsg.OriginalTransactionId, cancellationToken);
		var orderType = replaceMsg.OrderType ?? current.ToOrderType();
		ValidateOrderType(orderType);
		var condition = replaceMsg.Condition as FlattradeOrderCondition;
		var triggerPrice = condition?.TriggerPrice ?? Positive(current.TriggerPrice);
		if (orderType == OrderTypes.Conditional && triggerPrice is not > 0)
			throw new InvalidOperationException("A positive trigger price is required for a Flattrade stop order.");
		if (orderType == OrderTypes.Limit && replaceMsg.Price <= 0)
			throw new ArgumentOutOfRangeException(nameof(replaceMsg.Price), replaceMsg.Price, "A positive limit price is required.");

		var quantity = ToQuantity(replaceMsg.Volume, nameof(replaceMsg.Volume), false);
		var instrument = await ResolveInstrument(replaceMsg.SecurityId, current, cancellationToken);
		await _restClient.ModifyOrder(new FlattradeModifyOrderRequest
		{
			UserId = UserId,
			AccountId = _resolvedAccountId,
			OrderId = current.OrderId,
			Exchange = instrument.Exchange,
			TradingSymbol = instrument.TradingSymbol,
			Quantity = quantity.ToString(CultureInfo.InvariantCulture),
			PriceType = orderType.ToPriceType(replaceMsg.Price),
			Price = FormatPrice(orderType is OrderTypes.Market ? 0 : replaceMsg.Price),
			TriggerPrice = triggerPrice is > 0 ? FormatPrice(triggerPrice.Value) : null,
			StopLossPrice = FormatOptional(condition?.StopLossPrice ?? Positive(current.StopLossPrice)),
			ProfitPrice = FormatOptional(condition?.ProfitPrice ?? Positive(current.ProfitPrice)),
			TrailingPrice = FormatOptional(condition?.TrailingPrice ?? Positive(current.TrailingPrice)),
		}, cancellationToken);
		RememberOrder(current.OrderId, replaceMsg.TransactionId);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsurePortfolio(cancelMsg.PortfolioName);
		var orderId = cancelMsg.OrderStringId;
		if (orderId.IsEmpty() && _transactionOrders.TryGetValue(cancelMsg.OriginalTransactionId, out var remembered))
			orderId = remembered;
		if (orderId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));
		await _restClient.CancelOrder(orderId, cancellationToken);
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

		EnsurePortfolio(statusMsg.PortfolioName);
		var left = statusMsg.Count ?? long.MaxValue;
		foreach (var order in (await _restClient.GetOrders(cancellationToken))
			.Where(order => order != null)
			.OrderBy(GetOrderTime))
		{
			var time = GetOrderTime(order);
			if (statusMsg.From is DateTime from && time < from.ToUniversalTime())
				continue;
			if (statusMsg.To is DateTime to && time > to.ToUniversalTime())
				continue;
			await ProcessOrder(order, statusMsg.TransactionId, true, cancellationToken);
			if (--left <= 0)
				break;
		}

		foreach (var trade in await _restClient.GetTrades(cancellationToken))
			await ProcessTrade(trade, statusMsg.TransactionId, cancellationToken);

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

		EnsurePortfolio(lookupMsg.PortfolioName);
		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = lookupMsg.TransactionId,
			PortfolioName = _resolvedAccountId,
			BoardCode = "NSE",
		}, cancellationToken);
		await SendPortfolioSnapshot(lookupMsg.TransactionId, cancellationToken);
		_lastPortfolioRefresh = CurrentTime;

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
		var limits = await _restClient.GetLimits(cancellationToken);
		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = _resolvedAccountId,
			SecurityId = SecurityId.Money,
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.BeginValue, limits.Cash.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.CurrentValue, limits.Cash.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.BlockedValue, limits.MarginUsed.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.RealizedPnL, limits.RealizedPnL.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, limits.UnrealizedPnL.ToDecimal(), true), cancellationToken);

		foreach (var position in await _restClient.GetPositions(cancellationToken))
		{
			if (position == null || position.Exchange.IsEmpty() || position.Token.IsEmpty())
				continue;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = _resolvedAccountId,
				SecurityId = position.Exchange.ToSecurityId(position.Token, position.TradingSymbol),
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, position.NetQuantity.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.AveragePrice, Positive(position.NetAveragePrice), true)
			.TryAdd(PositionChangeTypes.CurrentPrice, Positive(position.LastPrice), true)
			.TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedPnL.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnL.ToDecimal(), true), cancellationToken);
		}

		foreach (var holding in await _restClient.GetHoldings(cancellationToken))
		{
			var instrument = holding?.Instruments?.FirstOrDefault(item =>
				item != null && !item.Exchange.IsEmpty() && !item.Token.IsEmpty());
			if (instrument == null)
				continue;
			var current = holding.HoldingQuantity.ToDecimal() + holding.BtstQuantity.ToDecimal();
			var blocked = holding.CollateralQuantity.ToDecimal() + holding.BtstCollateralQuantity.ToDecimal() +
				holding.UsedQuantity.ToDecimal();
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = _resolvedAccountId,
				SecurityId = instrument.Exchange.ToSecurityId(instrument.Token, instrument.TradingSymbol),
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, current, true)
			.TryAdd(PositionChangeTypes.BlockedValue, blocked, true)
			.TryAdd(PositionChangeTypes.AveragePrice, Positive(holding.UploadPrice), true), cancellationToken);
		}
	}

	private async ValueTask ProcessOrder(FlattradeOrder order, long originId, bool isLookup,
		CancellationToken cancellationToken)
	{
		if (order == null || order.OrderId.IsEmpty())
			return;

		_orderTransactions.TryGetValue(order.OrderId, out var transactionId);
		RememberOrder(order.OrderId, transactionId);
		var state = order.OrderStatus.ToOrderState(order.ReportType);
		var quantity = order.Quantity.ToDecimal();
		var filled = order.FilledQuantity.ToDecimal();
		var cancelled = order.CancelledQuantity.ToDecimal();
		var balance = Math.Max(0, quantity - filled - cancelled);
		var securityId = await GetSecurityId(order.Exchange, order.Token, order.TradingSymbol, cancellationToken);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = isLookup ? originId : transactionId != 0 ? transactionId : _orderStatusSubscriptionId,
			TransactionId = isLookup ? transactionId : 0,
			OrderStringId = order.OrderId,
			SecurityId = securityId,
			PortfolioName = order.AccountId.IsEmpty(_resolvedAccountId),
			OrderType = order.ToOrderType(),
			Side = order.Side.ToSide(),
			TimeInForce = order.Retention.ToTimeInForce(),
			OrderPrice = order.Price.ToDecimal(),
			OrderVolume = quantity,
			Balance = balance,
			AveragePrice = Positive(order.AveragePrice),
			OrderState = state,
			ServerTime = GetOrderTime(order),
			Condition = CreateCondition(order.Product.ToProduct(), Positive(order.TriggerPrice),
				Positive(order.DisclosedQuantity), order.AfterMarket.EqualsIgnoreCase("Yes"),
				Positive(order.StopLossPrice), Positive(order.ProfitPrice), Positive(order.TrailingPrice), order.Remarks),
			Error = state == OrderStates.Failed
				? new InvalidOperationException(order.RejectionReason.IsEmpty($"Flattrade order status: {order.OrderStatus}."))
				: null,
		}, cancellationToken);
	}

	private async ValueTask ProcessTrade(FlattradeOrder trade, long originId, CancellationToken cancellationToken)
	{
		if (trade == null || trade.OrderId.IsEmpty())
			return;
		var fillId = trade.FillId.IsEmpty(
			$"{trade.OrderId}:{trade.FillTime}:{trade.FillPrice}:{trade.FillQuantity}");
		if (!_tradeIds.TryAdd(fillId))
			return;

		var transactionId = _orderTransactions.TryGetValue2(trade.OrderId) ?? 0;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originId != 0 ? originId : transactionId != 0 ? transactionId : _orderStatusSubscriptionId,
			TransactionId = originId != 0 ? transactionId : 0,
			OrderStringId = trade.OrderId,
			TradeStringId = fillId,
			SecurityId = await GetSecurityId(trade.Exchange, trade.Token, trade.TradingSymbol, cancellationToken),
			PortfolioName = trade.AccountId.IsEmpty(_resolvedAccountId),
			Side = trade.Side.ToSide(),
			TradePrice = trade.FillPrice.ToDecimal(),
			TradeVolume = trade.FillQuantity.ToDecimal(),
			ServerTime = trade.FillTime.ToFlattradeTime() ?? GetOrderTime(trade),
		}, cancellationToken);
	}

	private async ValueTask OnOrderReceived(FlattradeOrder order, CancellationToken cancellationToken)
	{
		var transactionId = _orderTransactions.TryGetValue2(order.OrderId) ?? 0;
		if (transactionId != 0 || _orderStatusSubscriptionId != 0)
			await ProcessOrder(order, _orderStatusSubscriptionId, false, cancellationToken);
		if (!order.FillId.IsEmpty() || order.ReportType.EqualsIgnoreCase("Fill"))
			await ProcessTrade(order, 0, cancellationToken);
	}

	private async ValueTask OnPositionReceived(FlattradePosition position,
		CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId == 0 || position == null || position.Exchange.IsEmpty() ||
			position.Token.IsEmpty())
			return;

		var averagePrice = Positive(position.NetAveragePrice) ?? Positive(position.TotalBuyAveragePrice) ??
			Positive(position.BuyAveragePrice) ?? Positive(position.TotalSellAveragePrice) ??
			Positive(position.SellAveragePrice);
		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = _portfolioSubscriptionId,
			PortfolioName = position.AccountId.IsEmpty(_resolvedAccountId),
			SecurityId = await GetSecurityId(position.Exchange, position.Token, position.TradingSymbol,
				cancellationToken),
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, position.NetQuantity.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.AveragePrice, averagePrice, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, Positive(position.LastPrice), true)
		.TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedPnL.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnL.ToDecimal(), true),
			cancellationToken);
	}

	private async Task<FlattradeOrder> ResolveOrder(string orderId, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (orderId.IsEmpty())
			_transactionOrders.TryGetValue(originalTransactionId, out orderId);
		if (orderId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(originalTransactionId));

		foreach (var order in await _restClient.GetOrders(cancellationToken))
		{
			if (order == null || order.OrderId.IsEmpty())
				continue;
			_orderTransactions.TryGetValue(order.OrderId, out var transactionId);
			RememberOrder(order.OrderId, transactionId);
			if (order.OrderId.EqualsIgnoreCase(orderId) ||
				transactionId != 0 && transactionId == originalTransactionId)
				return order;
		}
		throw new InvalidOperationException($"Flattrade order '{orderId}' was not found in the current order book.");
	}

	private async Task<FlattradeInstrument> ResolveInstrument(SecurityId securityId, FlattradeOrder order,
		CancellationToken cancellationToken)
	{
		if (securityId.Native is string native && !native.IsEmpty())
			return await GetInstrument(native, cancellationToken);
		if (!order.Exchange.IsEmpty() && !order.Token.IsEmpty())
			return await GetInstrument(order.Exchange.ToInstrumentKey(order.Token), cancellationToken);
		return await _restClient.FindInstrument(order.Exchange, order.TradingSymbol, cancellationToken)
			?? throw new InvalidOperationException($"Flattrade instrument '{order.Exchange}|{order.TradingSymbol}' was not found.");
	}

	private async Task<SecurityId> GetSecurityId(string exchange, string token, string tradingSymbol,
		CancellationToken cancellationToken)
	{
		if (!exchange.IsEmpty() && !token.IsEmpty())
			return exchange.ToSecurityId(token, tradingSymbol);
		var instrument = await _restClient.FindInstrument(exchange, tradingSymbol, cancellationToken);
		if (instrument != null)
			return instrument.ToSecurityId();
		return new() { SecurityCode = tradingSymbol, BoardCode = exchange.ToBoardCode() };
	}

	private void RememberOrder(string orderId, long transactionId)
	{
		if (orderId.IsEmpty() || transactionId == 0)
			return;
		_orderTransactions[orderId] = transactionId;
		_transactionOrders[transactionId] = orderId;
	}

	private void EnsurePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() && !portfolioName.EqualsIgnoreCase(_resolvedAccountId))
			throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
	}

	private static void ValidateOrderType(OrderTypes orderType)
	{
		if (orderType is not OrderTypes.Limit and not OrderTypes.Market and not OrderTypes.Conditional)
			throw new ArgumentOutOfRangeException(nameof(orderType), orderType,
				"Flattrade supports market, limit, stop-limit, and stop-market orders.");
	}

	private static long ToQuantity(decimal value, string parameterName, bool allowZero)
	{
		if (value < 0 || !allowZero && value == 0 || value != decimal.Truncate(value) || value > long.MaxValue)
			throw new ArgumentOutOfRangeException(parameterName, value,
				"Flattrade quantities must be non-negative whole numbers within Int64 range.");
		return decimal.ToInt64(value);
	}

	private static string FormatPrice(decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	private static string FormatOptional(decimal? value)
		=> value is > 0 ? FormatPrice(value.Value) : null;

	private static FlattradeOrderCondition CreateCondition(FlattradeProducts product, decimal? triggerPrice,
		decimal? disclosedVolume, bool isAfterMarket, decimal? stopLossPrice, decimal? profitPrice,
		decimal? trailingPrice, string remarks)
		=> new()
		{
			Product = product,
			TriggerPrice = triggerPrice,
			DisclosedVolume = disclosedVolume,
			IsAfterMarket = isAfterMarket,
			StopLossPrice = stopLossPrice,
			ProfitPrice = profitPrice,
			TrailingPrice = trailingPrice,
			Remarks = remarks,
		};

	private DateTime GetOrderTime(FlattradeOrder order)
		=> order.ExchangeTime.ToFlattradeTime() ?? order.OrderEntryTime.ToFlattradeTime() ??
			order.NorenTime.ToFlattradeTime() ?? CurrentTime;
}
