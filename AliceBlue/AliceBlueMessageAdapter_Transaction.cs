namespace StockSharp.AliceBlue;

public partial class AliceBlueMessageAdapter
{
	private readonly SynchronizedDictionary<string, long> _orderTransactions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, string> _transactionOrders = [];
	private readonly SynchronizedSet<string> _tradeIds = new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsurePortfolio(regMsg.PortfolioName);
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		ValidateOrderType(orderType);
		if (regMsg.TimeInForce == TimeInForce.MatchOrCancel)
			throw new NotSupportedException("Alice Blue does not expose fill-or-kill orders.");

		var condition = regMsg.Condition as AliceBlueOrderCondition;
		var triggerPrice = condition?.TriggerPrice;
		if (orderType == OrderTypes.Conditional && triggerPrice is not > 0)
			throw new InvalidOperationException("A positive trigger price is required for an Alice Blue stop order.");
		if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Price), regMsg.Price,
				"A positive limit price is required.");

		var quantity = ToQuantity(regMsg.Volume, nameof(regMsg.Volume), false);
		var disclosedQuantity = ToQuantity(condition?.DisclosedVolume ?? 0,
			nameof(condition.DisclosedVolume), true);
		if (disclosedQuantity > quantity)
			throw new ArgumentOutOfRangeException(nameof(condition.DisclosedVolume), disclosedQuantity,
				"Disclosed quantity cannot exceed order quantity.");

		var instrument = await GetInstrument(regMsg.SecurityId.ToInstrumentKey(), cancellationToken);
		var product = condition?.Product ?? DefaultProduct;
		var complexity = condition?.Complexity ?? AliceBlueOrderComplexities.Regular;
		var orderId = await _restClient.PlaceOrder(new AliceBlueOrderRequest
		{
			Exchange = instrument.Exchange,
			InstrumentId = instrument.Token,
			TransactionType = regMsg.Side.ToNative(),
			Quantity = quantity,
			Product = product.ToNative(),
			OrderComplexity = complexity.ToNative(),
			OrderType = orderType.ToNative(regMsg.Price),
			Validity = regMsg.TimeInForce.ToValidity(),
			Price = FormatPrice(orderType is OrderTypes.Market ? 0 : regMsg.Price),
			StopLossTriggerPrice = FormatOptional(triggerPrice),
			DisclosedQuantity = disclosedQuantity > 0
				? disclosedQuantity.ToString(CultureInfo.InvariantCulture)
				: null,
			StopLossLegPrice = FormatOptional(condition?.StopLossLegPrice),
			TargetLegPrice = FormatOptional(condition?.TargetLegPrice),
			TrailingStopLoss = FormatOptional(condition?.TrailingStopLoss),
			MarketProtectionPercent = FormatOptional(condition?.MarketProtectionPercent),
			DeviceId = DeviceId,
			AlgoId = condition?.AlgoId,
			OrderTag = condition?.OrderTag,
		}, cancellationToken);

		RememberOrder(orderId, regMsg.TransactionId);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			OrderStringId = orderId,
			SecurityId = regMsg.SecurityId,
			PortfolioName = _resolvedClientId,
			OrderType = orderType,
			Side = regMsg.Side,
			TimeInForce = regMsg.TimeInForce ?? TimeInForce.PutInQueue,
			OrderPrice = regMsg.Price,
			OrderVolume = regMsg.Volume,
			Balance = regMsg.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = CurrentTime,
			Condition = CreateCondition(product, complexity, triggerPrice, condition?.DisclosedVolume,
				condition?.StopLossLegPrice, condition?.TargetLegPrice, condition?.TrailingStopLoss,
				condition?.MarketProtectionPercent, condition?.AlgoId, condition?.OrderTag),
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		EnsurePortfolio(replaceMsg.PortfolioName);
		if (replaceMsg.TimeInForce == TimeInForce.MatchOrCancel)
			throw new NotSupportedException("Alice Blue does not expose fill-or-kill orders.");

		var current = await ResolveOrder(replaceMsg.OldOrderStringId, replaceMsg.OriginalTransactionId,
			cancellationToken);
		var orderType = replaceMsg.OrderType ?? current.OrderType.ToOrderType();
		ValidateOrderType(orderType);
		var condition = replaceMsg.Condition as AliceBlueOrderCondition;
		var triggerPrice = condition?.TriggerPrice ?? Positive(current.StopLossTriggerPrice);
		if (orderType == OrderTypes.Conditional && triggerPrice is not > 0)
			throw new InvalidOperationException("A positive trigger price is required for an Alice Blue stop order.");
		if (orderType == OrderTypes.Limit && replaceMsg.Price <= 0)
			throw new ArgumentOutOfRangeException(nameof(replaceMsg.Price), replaceMsg.Price,
				"A positive limit price is required.");

		var quantity = ToQuantity(replaceMsg.Volume, nameof(replaceMsg.Volume), false);
		var disclosedQuantity = ToQuantity(condition?.DisclosedVolume ?? current.DisclosedQuantity,
			nameof(condition.DisclosedVolume), true);
		if (disclosedQuantity > quantity)
			throw new ArgumentOutOfRangeException(nameof(condition.DisclosedVolume), disclosedQuantity,
				"Disclosed quantity cannot exceed order quantity.");

		await _restClient.ModifyOrder(new AliceBlueModifyOrderRequest
		{
			OrderId = current.OrderId,
			Quantity = quantity,
			OrderType = orderType.ToNative(replaceMsg.Price),
			StopLossTriggerPrice = FormatOptional(triggerPrice),
			Price = FormatPrice(orderType is OrderTypes.Market ? 0 : replaceMsg.Price),
			StopLossLegPrice = FormatOptional(condition?.StopLossLegPrice),
			TrailingStopLoss = FormatOptional(condition?.TrailingStopLoss ?? Positive(current.TrailingStopLoss)),
			TargetLegPrice = FormatOptional(condition?.TargetLegPrice),
			Validity = ((TimeInForce?)(replaceMsg.TimeInForce ?? current.Validity.ToTimeInForce())).ToValidity(),
			DisclosedQuantity = disclosedQuantity > 0
				? disclosedQuantity.ToString(CultureInfo.InvariantCulture)
				: null,
			MarketProtectionPercent = FormatOptional(condition?.MarketProtectionPercent ??
				Positive(current.MarketProtectionPercent)),
			DeviceId = DeviceId,
		}, cancellationToken);
		RememberOrder(current.OrderId, replaceMsg.TransactionId);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
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
		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = lookupMsg.TransactionId,
			PortfolioName = _resolvedClientId,
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

	private async ValueTask SendPortfolioSnapshot(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var limits = await _restClient.GetLimits(cancellationToken);
		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = _resolvedClientId,
			SecurityId = SecurityId.Money,
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.BeginValue, limits.OpeningCashLimit, true)
		.TryAdd(PositionChangeTypes.CurrentValue, limits.TradingLimit, true)
		.TryAdd(PositionChangeTypes.BlockedValue, limits.UtilizedMargin + limits.BlockedForPayout, true),
			cancellationToken);

		foreach (var position in await _restClient.GetPositions(cancellationToken))
		{
			if (position == null || position.Exchange.IsEmpty() || position.InstrumentId.IsEmpty())
				continue;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = _resolvedClientId,
				SecurityId = position.Exchange.ToSecurityId(position.InstrumentId, position.TradingSymbol),
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, position.NetQuantity, true)
			.TryAdd(PositionChangeTypes.AveragePrice, Positive(position.NetAveragePrice), true)
			.TryAdd(PositionChangeTypes.CurrentPrice, Positive(position.PreviousDayClose), true)
			.TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedPnL, true), cancellationToken);
		}

		foreach (var holding in await _restClient.GetHoldings(cancellationToken))
		{
			if (holding == null)
				continue;
			var exchange = !holding.NseInstrumentId.IsEmpty() ? "NSE" : "BSE";
			var instrumentId = exchange == "NSE" ? holding.NseInstrumentId : holding.BseInstrumentId;
			var tradingSymbol = exchange == "NSE" ? holding.NseTradingSymbol : holding.BseTradingSymbol;
			if (instrumentId.IsEmpty())
				continue;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = _resolvedClientId,
				SecurityId = exchange.ToSecurityId(instrumentId, tradingSymbol),
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, holding.TotalQuantity > 0
				? holding.TotalQuantity
				: holding.DpQuantity + holding.T1Quantity, true)
			.TryAdd(PositionChangeTypes.BlockedValue, holding.CollateralQuantity, true)
			.TryAdd(PositionChangeTypes.AveragePrice, Positive(holding.AverageTradedPrice), true)
			.TryAdd(PositionChangeTypes.CurrentPrice, Positive(holding.LastPrice), true), cancellationToken);
		}
	}

	private async ValueTask ProcessOrder(AliceBlueOrder order, long originId, bool isLookup,
		CancellationToken cancellationToken)
	{
		if (order == null || order.OrderId.IsEmpty())
			return;

		_orderTransactions.TryGetValue(order.OrderId, out var transactionId);
		RememberOrder(order.OrderId, transactionId);
		var state = order.OrderStatus.ToOrderState();
		var balance = Math.Max(0, order.Quantity - order.FilledQuantity - order.CancelledQuantity);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = isLookup ? originId : transactionId != 0 ? transactionId : _orderStatusSubscriptionId,
			TransactionId = isLookup ? transactionId : 0,
			OrderStringId = order.OrderId,
			SecurityId = await GetSecurityId(order.Exchange, order.InstrumentId, order.TradingSymbol,
				cancellationToken),
			PortfolioName = order.ClientId.IsEmpty(_resolvedClientId),
			OrderType = order.OrderType.ToOrderType(),
			Side = order.TransactionType.ToSide(),
			TimeInForce = order.Validity.ToTimeInForce(),
			OrderPrice = order.Price,
			OrderVolume = order.Quantity,
			Balance = balance,
			AveragePrice = Positive(order.AverageTradedPrice),
			OrderState = state,
			ServerTime = GetOrderTime(order),
			Condition = CreateCondition(order.Product.ToProduct(), order.OrderComplexity.ToComplexity(),
				Positive(order.StopLossTriggerPrice), Positive(order.DisclosedQuantity), null, null,
				Positive(order.TrailingStopLoss), Positive(order.MarketProtectionPercent), order.AlgoId,
				order.OrderTag),
			Error = state == OrderStates.Failed
				? new InvalidOperationException(order.RejectionReason.IsEmpty($"Alice Blue order status: {order.OrderStatus}."))
				: null,
		}, cancellationToken);
	}

	private async ValueTask ProcessTrade(AliceBlueTrade trade, long originId,
		CancellationToken cancellationToken)
	{
		if (trade == null || trade.OrderId.IsEmpty())
			return;
		var fillId = trade.TradeId.IsEmpty(
			$"{trade.OrderId}:{trade.FillTime}:{trade.TradedPrice}:{trade.FilledQuantity}");
		if (!_tradeIds.TryAdd(fillId))
			return;

		var transactionId = _orderTransactions.TryGetValue2(trade.OrderId) ?? 0;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originId != 0 ? originId : transactionId != 0
				? transactionId
				: _orderStatusSubscriptionId,
			TransactionId = originId != 0 ? transactionId : 0,
			OrderStringId = trade.OrderId,
			TradeStringId = fillId,
			SecurityId = await GetSecurityId(trade.Exchange, trade.InstrumentId, trade.TradingSymbol,
				cancellationToken),
			PortfolioName = trade.ClientId.IsEmpty(_resolvedClientId),
			Side = trade.TransactionType.ToSide(),
			TradePrice = trade.TradedPrice,
			TradeVolume = trade.FilledQuantity,
			ServerTime = trade.FillTime.ToAliceBlueTime() ?? trade.OrderTime.ToAliceBlueTime() ?? CurrentTime,
		}, cancellationToken);
	}

	private async ValueTask OnOrderReceived(AliceBlueOrderUpdate order,
		CancellationToken cancellationToken)
	{
		var transactionId = _orderTransactions.TryGetValue2(order.OrderId) ?? 0;
		if (transactionId != 0 || _orderStatusSubscriptionId != 0)
			await ProcessOrderUpdate(order, transactionId, cancellationToken);
		if (!order.FillId.IsEmpty() || order.ReportType.EqualsIgnoreCase("Fill"))
			await ProcessTradeUpdate(order, transactionId, cancellationToken);
	}

	private async ValueTask ProcessOrderUpdate(AliceBlueOrderUpdate order, long transactionId,
		CancellationToken cancellationToken)
	{
		var state = order.Status.ToOrderState(order.ReportType);
		var quantity = order.Quantity.ToDecimal();
		var filled = order.FilledQuantity.ToDecimal();
		var cancelled = order.CancelledQuantity.ToDecimal();
		RememberOrder(order.OrderId, transactionId);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = transactionId != 0 ? transactionId : _orderStatusSubscriptionId,
			OrderStringId = order.OrderId,
			SecurityId = await GetSecurityId(order.Exchange, null, order.TradingSymbol, cancellationToken),
			PortfolioName = order.AccountId.IsEmpty(_resolvedClientId),
			OrderType = order.OrderType.ToOrderType(),
			Side = order.TransactionType.ToSide(),
			TimeInForce = order.Validity.ToTimeInForce(),
			OrderPrice = order.Price.ToDecimal(),
			OrderVolume = quantity,
			Balance = Math.Max(0, quantity - filled - cancelled),
			AveragePrice = Positive(order.AveragePrice),
			OrderState = state,
			ServerTime = order.ExchangeTime.ToAliceBlueTime() ?? order.Time.ToAliceBlueTime() ?? CurrentTime,
			Condition = CreateCondition(order.EffectiveProduct.ToProduct(), AliceBlueOrderComplexities.Regular,
				Positive(order.TriggerPrice), Positive(order.DisclosedQuantity),
				Positive(order.StopLossLegPrice), Positive(order.TargetLegPrice),
				Positive(order.TrailingStopLoss), null, null, order.OrderTag),
			Error = state == OrderStates.Failed
				? new InvalidOperationException(order.RejectionReason.IsEmpty($"Alice Blue order status: {order.Status}."))
				: null,
		}, cancellationToken);
	}

	private async ValueTask ProcessTradeUpdate(AliceBlueOrderUpdate order, long transactionId,
		CancellationToken cancellationToken)
	{
		var fillId = order.FillId.IsEmpty(
			$"{order.OrderId}:{order.FillTime}:{order.FillPrice}:{order.FillQuantity}");
		if (!_tradeIds.TryAdd(fillId))
			return;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = transactionId != 0 ? transactionId : _orderStatusSubscriptionId,
			OrderStringId = order.OrderId,
			TradeStringId = fillId,
			SecurityId = await GetSecurityId(order.Exchange, null, order.TradingSymbol, cancellationToken),
			PortfolioName = order.AccountId.IsEmpty(_resolvedClientId),
			Side = order.TransactionType.ToSide(),
			TradePrice = order.FillPrice.ToDecimal(),
			TradeVolume = order.FillQuantity.ToDecimal(),
			ServerTime = order.FillTime.ToAliceBlueTime() ?? order.ExchangeTime.ToAliceBlueTime() ?? CurrentTime,
		}, cancellationToken);
	}

	private async Task<AliceBlueOrder> ResolveOrder(string orderId, long originalTransactionId,
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
		throw new InvalidOperationException($"Alice Blue order '{orderId}' was not found in the current order book.");
	}

	private async Task<SecurityId> GetSecurityId(string exchange, string instrumentId,
		string tradingSymbol, CancellationToken cancellationToken)
	{
		if (!exchange.IsEmpty() && !instrumentId.IsEmpty())
			return exchange.ToSecurityId(instrumentId, tradingSymbol);
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
		if (!portfolioName.IsEmpty() && !portfolioName.EqualsIgnoreCase(_resolvedClientId))
			throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
	}

	private static void ValidateOrderType(OrderTypes orderType)
	{
		if (orderType is not OrderTypes.Limit and not OrderTypes.Market and not OrderTypes.Conditional)
			throw new ArgumentOutOfRangeException(nameof(orderType), orderType,
				"Alice Blue supports market, limit, stop-limit, and stop-market orders.");
	}

	private static long ToQuantity(decimal value, string parameterName, bool allowZero)
	{
		if (value < 0 || !allowZero && value == 0 || value != decimal.Truncate(value) || value > long.MaxValue)
			throw new ArgumentOutOfRangeException(parameterName, value,
				"Alice Blue quantities must be non-negative whole numbers within Int64 range.");
		return decimal.ToInt64(value);
	}

	private static string FormatPrice(decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	private static string FormatOptional(decimal? value)
		=> value is > 0 ? FormatPrice(value.Value) : null;

	private static decimal? Positive(decimal? value)
		=> value is > 0 ? value : null;

	private static AliceBlueOrderCondition CreateCondition(AliceBlueProducts product,
		AliceBlueOrderComplexities complexity, decimal? triggerPrice, decimal? disclosedVolume,
		decimal? stopLossLegPrice, decimal? targetLegPrice, decimal? trailingStopLoss,
		decimal? marketProtectionPercent, string algoId, string orderTag)
		=> new()
		{
			Product = product,
			Complexity = complexity,
			TriggerPrice = triggerPrice,
			DisclosedVolume = disclosedVolume,
			StopLossLegPrice = stopLossLegPrice,
			TargetLegPrice = targetLegPrice,
			TrailingStopLoss = trailingStopLoss,
			MarketProtectionPercent = marketProtectionPercent,
			AlgoId = algoId,
			OrderTag = orderTag,
		};

	private DateTime GetOrderTime(AliceBlueOrder order)
		=> order.ExchangeTimestamp.ToAliceBlueTime() ?? order.ExchangeUpdateTime.ToAliceBlueTime() ??
			order.BrokerUpdateTime.ToAliceBlueTime() ?? order.OrderTime.ToAliceBlueTime() ?? CurrentTime;
}
