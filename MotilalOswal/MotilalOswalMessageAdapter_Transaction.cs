namespace StockSharp.MotilalOswal;

public partial class MotilalOswalMessageAdapter
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
			throw new NotSupportedException("Motilal Oswal MO API does not expose fill-or-kill orders.");

		var condition = regMsg.Condition as MotilalOswalOrderCondition;
		var triggerPrice = condition?.TriggerPrice;
		if (orderType == OrderTypes.Conditional && triggerPrice is not > 0)
			throw new InvalidOperationException("A positive trigger price is required for a Motilal Oswal stop-loss order.");
		if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Price), regMsg.Price, "A positive limit price is required.");

		ValidateText(condition?.Tag, 10, nameof(condition.Tag));
		ValidateText(condition?.ParticipantCode, 20, nameof(condition.ParticipantCode));
		var quantity = ToQuantity(regMsg.Volume, nameof(regMsg.Volume), false);
		var disclosedQuantity = ToQuantity(condition?.DisclosedVolume ?? 0, nameof(condition.DisclosedVolume), true);
		if (disclosedQuantity > quantity)
			throw new ArgumentOutOfRangeException(nameof(condition.DisclosedVolume), disclosedQuantity, "Disclosed quantity cannot exceed order quantity.");

		var (exchange, scripCode) = regMsg.SecurityId.ToInstrumentKey().ParseInstrumentKey();
		var duration = GetDuration(regMsg.TimeInForce, regMsg.TillDate, condition?.Duration);
		var goodTillDate = condition?.GoodTillDate ?? regMsg.TillDate;
		ValidateGoodTillDate(duration, goodTillDate);
		var product = condition?.Product ?? DefaultProduct;
		var orderId = await _restClient.PlaceOrder(new MotilalOswalPlaceOrderRequest
		{
			ClientCode = ClientCode,
			Exchange = exchange,
			SymbolToken = scripCode,
			Side = regMsg.Side.ToNative(),
			OrderType = orderType.ToNative(),
			ProductType = product.ToNative(),
			Duration = duration.ToNative(),
			Price = orderType == OrderTypes.Market ? 0 : regMsg.Price,
			TriggerPrice = triggerPrice ?? 0,
			Quantity = quantity,
			DisclosedQuantity = disclosedQuantity,
			AfterMarket = condition?.IsAfterMarket == true ? "Y" : "N",
			GoodTillDate = duration == MotilalOswalOrderDurations.GoodTillDate ? goodTillDate.ToGoodTillDate() : string.Empty,
			AlgoId = AlgoId,
			Tag = condition?.Tag,
			ParticipantCode = condition?.ParticipantCode,
		}, cancellationToken);

		RememberOrder(orderId, regMsg.TransactionId);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			OrderStringId = orderId,
			SecurityId = regMsg.SecurityId,
			PortfolioName = ClientCode,
			OrderType = orderType,
			Side = regMsg.Side,
			TimeInForce = ToTimeInForce(duration),
			ExpiryDate = goodTillDate?.ToUniversalTime(),
			OrderPrice = regMsg.Price,
			OrderVolume = regMsg.Volume,
			Balance = regMsg.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = CurrentTime,
			Condition = CreateCondition(product, duration, triggerPrice, condition?.DisclosedVolume,
				condition?.IsAfterMarket == true, goodTillDate, condition?.Tag, condition?.ParticipantCode),
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		EnsurePortfolio(replaceMsg.PortfolioName);
		if (replaceMsg.TimeInForce == TimeInForce.MatchOrCancel)
			throw new NotSupportedException("Motilal Oswal MO API does not expose fill-or-kill orders.");

		var current = await ResolveOrder(replaceMsg.OldOrderStringId, replaceMsg.OriginalTransactionId, cancellationToken);
		var orderType = replaceMsg.OrderType ?? current.ToOrderType();
		ValidateOrderType(orderType);
		var condition = replaceMsg.Condition as MotilalOswalOrderCondition;
		var triggerPrice = condition?.TriggerPrice ?? (current.TriggerPrice > 0 ? current.TriggerPrice : null);
		if (orderType == OrderTypes.Conditional && triggerPrice is not > 0)
			throw new InvalidOperationException("A positive trigger price is required for a Motilal Oswal stop-loss order.");
		if (orderType == OrderTypes.Limit && replaceMsg.Price <= 0)
			throw new ArgumentOutOfRangeException(nameof(replaceMsg.Price), replaceMsg.Price, "A positive limit price is required.");
		if (current.LastModifiedTime.IsEmpty() || current.LastModifiedTime.Trim() == "0")
			throw new InvalidOperationException($"Motilal Oswal did not provide lastmodifiedtime for order '{current.UniqueOrderId}'.");

		var quantity = ToQuantity(replaceMsg.Volume, nameof(replaceMsg.Volume), false);
		var disclosedQuantity = ToQuantity(condition?.DisclosedVolume ?? current.DisclosedQuantity,
			nameof(condition.DisclosedVolume), true);
		if (disclosedQuantity > quantity)
			throw new ArgumentOutOfRangeException(nameof(condition.DisclosedVolume), disclosedQuantity, "Disclosed quantity cannot exceed order quantity.");

		var goodTillDate = condition?.GoodTillDate ?? replaceMsg.TillDate ?? current.GoodTillDate.ToMotilalTime();
		var duration = GetDuration(replaceMsg.TimeInForce, replaceMsg.TillDate,
			condition?.Duration ?? current.Duration.ToDuration());
		ValidateGoodTillDate(duration, goodTillDate);
		await _restClient.ModifyOrder(new MotilalOswalModifyOrderRequest
		{
			ClientCode = ClientCode,
			UniqueOrderId = current.UniqueOrderId,
			OrderType = orderType.ToNative(),
			Duration = duration.ToNative(),
			Price = orderType == OrderTypes.Market ? 0 : replaceMsg.Price,
			TriggerPrice = triggerPrice ?? 0,
			Quantity = quantity,
			DisclosedQuantity = disclosedQuantity,
			GoodTillDate = duration == MotilalOswalOrderDurations.GoodTillDate ? goodTillDate.ToGoodTillDate() : string.Empty,
			LastModifiedTime = current.LastModifiedTime,
			TradedQuantity = ToQuantity(current.TradedQuantityToday, nameof(current.TradedQuantityToday), true),
		}, cancellationToken);
		RememberOrder(current.UniqueOrderId, replaceMsg.TransactionId);
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

		if (!_orderTransactions.ContainsKey(orderId))
		{
			var current = await ResolveOrder(orderId, cancelMsg.OriginalTransactionId, cancellationToken);
			orderId = current.UniqueOrderId;
		}
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
			.Where(o => o != null)
			.OrderBy(o => GetOrderTime(o)))
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
			PortfolioName = ClientCode,
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
		var margins = await _restClient.GetMargin(cancellationToken);
		decimal? GetMarginAmount(int serialNumber)
			=> margins.FirstOrDefault(row => row?.SerialNumber == serialNumber)?.Amount;

		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = ClientCode,
			SecurityId = SecurityId.Money,
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.BeginValue, GetMarginAmount(200), true)
		.TryAdd(PositionChangeTypes.CurrentValue, GetMarginAmount(100), true)
		.TryAdd(PositionChangeTypes.BlockedValue, GetMarginAmount(300), true)
		.TryAdd(PositionChangeTypes.RealizedPnL, GetMarginAmount(600), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, GetMarginAmount(700), true), cancellationToken);

		foreach (var position in await _restClient.GetPositions(cancellationToken))
		{
			if (position == null || position.Exchange.IsEmpty() || position.SymbolToken <= 0)
				continue;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = ClientCode,
				SecurityId = position.Exchange.ToSecurityId(position.SymbolToken, position.Symbol),
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, position.GetNetQuantity(), true)
			.TryAdd(PositionChangeTypes.AveragePrice, position.GetAveragePrice(), true)
			.TryAdd(PositionChangeTypes.CurrentPrice, position.LastPrice, true)
			.TryAdd(PositionChangeTypes.RealizedPnL, position.ActualBookedProfitLoss != 0
				? position.ActualBookedProfitLoss : position.BookedProfitLoss, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, position.ActualMarkToMarket != 0
				? position.ActualMarkToMarket : position.MarkToMarket, true), cancellationToken);
		}

		foreach (var holding in await _restClient.GetHoldings(cancellationToken))
		{
			if (holding == null)
				continue;
			SecurityId securityId;
			if (holding.NseSymbolToken > 0)
				securityId = "NSE".ToSecurityId(holding.NseSymbolToken, holding.Name);
			else if (holding.BseScripCode > 0)
				securityId = "BSE".ToSecurityId(holding.BseScripCode, holding.Name);
			else
				continue;
			securityId.Isin = holding.Isin;

			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = ClientCode,
				SecurityId = securityId,
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, holding.DepositoryQuantity, true)
			.TryAdd(PositionChangeTypes.BlockedValue, holding.BlockedQuantity, true)
			.TryAdd(PositionChangeTypes.AveragePrice, holding.AveragePrice, true), cancellationToken);
		}
	}

	private async ValueTask ProcessOrder(MotilalOswalOrder order, long originId, bool isLookup,
		CancellationToken cancellationToken)
	{
		if (order == null || order.UniqueOrderId.IsEmpty() || order.Exchange.IsEmpty() || order.SymbolToken <= 0)
			return;

		_orderTransactions.TryGetValue(order.UniqueOrderId, out var transactionId);
		RememberOrder(order.UniqueOrderId, transactionId);
		var state = order.OrderStatus.ToOrderState();
		var duration = order.Duration.ToDuration();
		var serverTime = GetOrderTime(order);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = isLookup ? originId : transactionId != 0 ? transactionId : _orderStatusSubscriptionId,
			TransactionId = isLookup ? transactionId : 0,
			OrderStringId = order.UniqueOrderId,
			SecurityId = order.Exchange.ToSecurityId(order.SymbolToken, order.Symbol),
			PortfolioName = ClientCode,
			OrderType = order.ToOrderType(),
			Side = order.Side.ToSide(),
			TimeInForce = ToTimeInForce(duration),
			ExpiryDate = order.GoodTillDate.ToMotilalTime(),
			OrderPrice = order.Price,
			OrderVolume = order.OrderQuantity,
			Balance = order.RemainingQuantity,
			AveragePrice = order.AveragePrice > 0 ? order.AveragePrice : null,
			OrderState = state,
			ServerTime = serverTime,
			Condition = CreateCondition(order.ProductType.ToProduct(), duration,
				order.TriggerPrice > 0 ? order.TriggerPrice : null,
				order.DisclosedQuantity > 0 ? order.DisclosedQuantity : null,
				order.AfterMarket.EqualsIgnoreCase("Y"), order.GoodTillDate.ToMotilalTime(),
				order.Tag, order.ParticipantCode),
			Error = state == OrderStates.Failed
				? new InvalidOperationException(order.Error.IsEmpty($"Motilal Oswal order status: {order.OrderStatus}."))
				: null,
		}, cancellationToken);
	}

	private ValueTask ProcessTrade(MotilalOswalTrade trade, long originId, CancellationToken cancellationToken)
	{
		if (trade == null || trade.TradeNumber.IsEmpty() || trade.Exchange.IsEmpty() ||
			trade.SymbolToken <= 0 || !_tradeIds.TryAdd(trade.TradeNumber))
			return default;

		var orderId = trade.UniqueOrderId.IsEmpty(trade.OrderId);
		var transactionId = _orderTransactions.TryGetValue2(orderId) ?? 0;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originId != 0 ? originId : transactionId != 0 ? transactionId : _orderStatusSubscriptionId,
			TransactionId = originId != 0 ? transactionId : 0,
			OrderStringId = orderId,
			TradeStringId = trade.TradeNumber,
			SecurityId = trade.Exchange.ToSecurityId(trade.SymbolToken, trade.Symbol),
			PortfolioName = ClientCode,
			Side = trade.Side.ToSide(),
			TradePrice = trade.TradePrice,
			TradeVolume = trade.TradeQuantity,
			ServerTime = trade.TradeTime.ToMotilalTime() ?? CurrentTime,
		}, cancellationToken);
	}

	private ValueTask OnOrderUpdate(MotilalOswalTradeStreamEvent order, CancellationToken cancellationToken)
	{
		if (order == null || order.UniqueOrderId.IsEmpty())
			return default;
		var transactionId = _orderTransactions.TryGetValue2(order.UniqueOrderId) ?? 0;
		if (transactionId == 0 && _orderStatusSubscriptionId == 0)
			return default;
		return ProcessOrder(order, _orderStatusSubscriptionId, false, cancellationToken);
	}

	private ValueTask OnTradeUpdate(MotilalOswalTradeStreamEvent trade, CancellationToken cancellationToken)
	{
		if (trade == null)
			return default;
		return ProcessTrade(new MotilalOswalTrade
		{
			ClientId = trade.ClientId,
			Exchange = trade.Exchange,
			SymbolToken = trade.SymbolToken,
			ProductType = trade.ProductType,
			Symbol = trade.Symbol,
			InstrumentType = trade.InstrumentType,
			Series = trade.Series,
			StrikePrice = trade.StrikePrice,
			OptionType = trade.OptionType,
			ExpiryDate = trade.ExpiryDate,
			LotSize = trade.LotSize,
			Precision = trade.Precision,
			Multiplier = trade.Multiplier,
			TradePrice = trade.TradePrice,
			TradeQuantity = trade.TradeQuantity,
			TradeValue = trade.TradeValue,
			Side = trade.Side,
			OrderId = trade.OrderId,
			TradeNumber = trade.TradeNumber,
			TradeTime = trade.TradeTime,
			UniqueOrderId = trade.UniqueOrderId,
		}, 0, cancellationToken);
	}

	private async Task<MotilalOswalOrder> ResolveOrder(string orderId, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (orderId.IsEmpty())
			_transactionOrders.TryGetValue(originalTransactionId, out orderId);
		if (orderId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(originalTransactionId));

		foreach (var order in await _restClient.GetOrders(cancellationToken))
		{
			if (order == null || order.UniqueOrderId.IsEmpty())
				continue;
			_orderTransactions.TryGetValue(order.UniqueOrderId, out var transactionId);
			RememberOrder(order.UniqueOrderId, transactionId);
			if (order.UniqueOrderId.EqualsIgnoreCase(orderId) || order.OrderId.EqualsIgnoreCase(orderId) ||
				transactionId != 0 && transactionId == originalTransactionId)
				return order;
		}

		throw new InvalidOperationException($"Motilal Oswal order '{orderId}' was not found in the current order book.");
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
		if (!portfolioName.IsEmpty() && !portfolioName.EqualsIgnoreCase(ClientCode))
			throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
	}

	private static void ValidateOrderType(OrderTypes orderType)
	{
		if (orderType is not OrderTypes.Limit and not OrderTypes.Market and not OrderTypes.Conditional)
			throw new ArgumentOutOfRangeException(nameof(orderType), orderType,
				"Motilal Oswal supports market, limit, and stop-loss orders.");
	}

	private static long ToQuantity(decimal value, string parameterName, bool allowZero)
	{
		if (value < 0 || !allowZero && value == 0 || value != decimal.Truncate(value) || value > long.MaxValue)
			throw new ArgumentOutOfRangeException(parameterName, value, "Motilal Oswal quantities must be non-negative whole numbers within Int64 range.");
		return decimal.ToInt64(value);
	}

	private static void ValidateText(string value, int maximumLength, string parameterName)
	{
		if (value?.Length > maximumLength)
			throw new ArgumentOutOfRangeException(parameterName, value,
				$"Motilal Oswal limits this value to {maximumLength} characters.");
	}

	private static MotilalOswalOrderDurations GetDuration(TimeInForce? timeInForce, DateTime? tillDate,
		MotilalOswalOrderDurations? requested)
		=> tillDate != null ? MotilalOswalOrderDurations.GoodTillDate : timeInForce.ToDuration(requested);

	private static void ValidateGoodTillDate(MotilalOswalOrderDurations duration, DateTime? date)
	{
		if (duration == MotilalOswalOrderDurations.GoodTillDate && date == null)
			throw new InvalidOperationException("A good-till date is required for a Motilal Oswal GTD order.");
	}

	private static TimeInForce ToTimeInForce(MotilalOswalOrderDurations duration)
		=> duration == MotilalOswalOrderDurations.ImmediateOrCancel
			? TimeInForce.CancelBalance
			: TimeInForce.PutInQueue;

	private static MotilalOswalOrderCondition CreateCondition(MotilalOswalProducts product,
		MotilalOswalOrderDurations duration, decimal? triggerPrice, decimal? disclosedVolume,
		bool isAfterMarket, DateTime? goodTillDate, string tag, string participantCode)
		=> new()
		{
			Product = product,
			Duration = duration,
			TriggerPrice = triggerPrice,
			DisclosedVolume = disclosedVolume,
			IsAfterMarket = isAfterMarket,
			GoodTillDate = goodTillDate,
			Tag = tag,
			ParticipantCode = participantCode,
		};

	private DateTime GetOrderTime(MotilalOswalOrder order)
		=> order.LastModifiedTime.ToMotilalTime() ?? order.EntryTime.ToMotilalTime() ??
			order.RecordInsertTime.ToMotilalTime() ?? CurrentTime;
}
