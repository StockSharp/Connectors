namespace StockSharp.Groww;

public partial class GrowwMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var security = ResolveSecurity(regMsg.SecurityId, regMsg.SecurityType);
		var condition = regMsg.Condition as GrowwOrderCondition;
		var triggerPrice = condition?.TriggerPrice;
		var result = await _rest.PlaceOrder(new GrowwPlaceOrderRequest
		{
			TradingSymbol = security.TradingSymbol,
			Quantity = regMsg.Volume.To<long>(),
			Price = regMsg.OrderType == OrderTypes.Market ? 0 : regMsg.Price,
			TriggerPrice = triggerPrice,
			Validity = condition?.Validity?.ToNative() ?? regMsg.TimeInForce.ToGrowwValidity(),
			Exchange = security.Exchange,
			Segment = security.Segment,
			Product = (condition?.Product ?? DefaultProduct).ToNative(),
			OrderType = (regMsg.OrderType ?? OrderTypes.Limit).ToGrowwOrderType(triggerPrice),
			TransactionType = regMsg.Side.ToNative(),
			OrderReferenceId = CreateOrderReference(regMsg.TransactionId),
		}, cancellationToken);

		var orderId = result.OrderId.ThrowIfEmpty(nameof(result.OrderId));
		_orders[orderId] = new()
		{
			TransactionId = regMsg.TransactionId,
			SecurityId = regMsg.SecurityId,
			Security = security,
			PortfolioName = regMsg.PortfolioName,
			Side = regMsg.Side,
			OrderType = regMsg.OrderType ?? OrderTypes.Limit,
			Price = regMsg.Price,
			Volume = regMsg.Volume,
		};
		_orderFills[orderId] = 0;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			OrderStringId = orderId,
			SecurityId = regMsg.SecurityId,
			PortfolioName = regMsg.PortfolioName,
			OrderType = regMsg.OrderType,
			Side = regMsg.Side,
			OrderPrice = regMsg.Price,
			OrderVolume = regMsg.Volume,
			Balance = regMsg.Volume,
			OrderState = result.Status.ToOrderState(),
			ServerTime = CurrentTime,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		var orderId = replaceMsg.OldOrderStringId;
		if (orderId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(replaceMsg.OriginalTransactionId));

		_orders.TryGetValue(orderId, out var tracker);
		var security = tracker?.Security ?? ResolveSecurity(replaceMsg.SecurityId, replaceMsg.SecurityType);
		var condition = replaceMsg.Condition as GrowwOrderCondition;
		var triggerPrice = condition?.TriggerPrice;
		await _rest.ModifyOrder(new GrowwModifyOrderRequest
		{
			Quantity = replaceMsg.Volume.To<long>(),
			Price = replaceMsg.OrderType == OrderTypes.Market ? 0 : replaceMsg.Price,
			TriggerPrice = triggerPrice,
			OrderId = orderId,
			OrderType = (replaceMsg.OrderType ?? OrderTypes.Limit).ToGrowwOrderType(triggerPrice),
			Segment = security.Segment,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var orderId = cancelMsg.OrderStringId;
		if (orderId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		_orders.TryGetValue(orderId, out var tracker);
		var segment = tracker?.Security.Segment ?? ResolveSecurity(cancelMsg.SecurityId, cancelMsg.SecurityType).Segment;
		await _rest.CancelOrder(new GrowwCancelOrderRequest
		{
			OrderId = orderId,
			Segment = segment,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		if (!statusMsg.IsSubscribe)
		{
			_orderStatusSubscriptionId = 0;
			return;
		}

		await EnsurePortfolioName(cancellationToken);
		await EnsureInstrumentCache(cancellationToken);
		await SendOrderSnapshot(statusMsg.TransactionId, true, cancellationToken);
		if (!statusMsg.IsHistoryOnly())
			_orderStatusSubscriptionId = statusMsg.TransactionId;
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		if (!lookupMsg.IsSubscribe)
		{
			_portfolioSubscriptionId = 0;
			return;
		}

		await EnsureInstrumentCache(cancellationToken);
		await SendPortfolioSnapshot(lookupMsg.TransactionId, cancellationToken);
		if (!lookupMsg.IsHistoryOnly())
			_portfolioSubscriptionId = lookupMsg.TransactionId;
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private async ValueTask EnsurePortfolioName(CancellationToken cancellationToken)
	{
		if (!_portfolioName.IsEmpty())
			return;
		var profile = await _rest.GetProfile(cancellationToken);
		_portfolioName = profile?.Ucc.IsEmpty(profile?.VendorUserId).IsEmpty(nameof(Groww));
	}

	private async ValueTask EnsureInstrumentCache(CancellationToken cancellationToken)
	{
		if (_isInstrumentCacheLoaded)
			return;
		foreach (var instrument in await _rest.GetInstruments(cancellationToken))
			CacheSecurity(GrowwSecurityInfo.FromInstrument(instrument));
		_isInstrumentCacheLoaded = true;
	}

	private async ValueTask SendOrderSnapshot(long originId, bool includeTrades, CancellationToken cancellationToken)
	{
		foreach (var order in await _rest.GetOrders(cancellationToken))
		{
			await ProcessOrder(order, originId, includeTrades, cancellationToken);
			if (!includeTrades || order.FilledQuantity is not > 0 || order.OrderId.IsEmpty() || order.Segment.IsEmpty())
				continue;
			foreach (var trade in await _rest.GetTrades(order.OrderId, order.Segment, cancellationToken))
				await ProcessTrade(trade, originId, cancellationToken);
		}
		_lastOrderRefresh = CurrentTime;
	}

	private async ValueTask SendPortfolioSnapshot(long originId, CancellationToken cancellationToken)
	{
		var profile = await _rest.GetProfile(cancellationToken)
			?? throw new InvalidOperationException(LocalizedStrings.NoPortfoliosReceived);
		_portfolioName = profile.Ucc.IsEmpty(profile.VendorUserId).IsEmpty(nameof(Groww));

		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = originId,
			PortfolioName = _portfolioName,
			BoardCode = profile.IsNseEnabled ? "NSE" : profile.IsBseEnabled ? "BSE" : "MCX",
		}, cancellationToken);

		var margin = await _rest.GetMargin(cancellationToken);
		await SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = SecurityId.Money,
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, margin?.ClearCash, true)
		.TryAdd(PositionChangeTypes.BlockedValue, margin?.NetMarginUsed, true), cancellationToken);

		var segments = profile.ActiveSegments is { Length: > 0 }
			? profile.ActiveSegments.Where(segment => segment is "CASH" or "FNO" or "COMMODITY")
			: ["CASH", "FNO", "COMMODITY"];
		foreach (var segment in segments.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			foreach (var position in await _rest.GetPositions(segment, cancellationToken))
			{
				var security = FindSecurity(position.Exchange, position.TradingSymbol, position.Isin, segment);
				await SendOutMessageAsync(new PositionChangeMessage
				{
					PortfolioName = _portfolioName,
					SecurityId = ToSecurityId(security),
					ServerTime = CurrentTime,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, position.Quantity, true)
				.TryAdd(PositionChangeTypes.AveragePrice, position.NetPrice, true)
				.TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedPnL, true), cancellationToken);
			}
		}

		foreach (var holding in await _rest.GetHoldings(cancellationToken))
		{
			var security = FindSecurity("NSE", holding.TradingSymbol, holding.Isin, "CASH");
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = ToSecurityId(security),
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, holding.Quantity, true)
			.TryAdd(PositionChangeTypes.AveragePrice, holding.AveragePrice, true)
			.TryAdd(PositionChangeTypes.BlockedValue, holding.PledgeQuantity, true), cancellationToken);
		}

		_lastPortfolioRefresh = CurrentTime;
	}

	private async ValueTask ProcessOrder(GrowwOrder order, long originId, bool isLookup, CancellationToken cancellationToken)
	{
		if (order?.OrderId.IsEmpty() != false)
			return;

		_orders.TryGetValue(order.OrderId, out var tracker);
		var transactionId = TryParseOrderReference(order.OrderReferenceId, out var parsedId) ? parsedId : 0;
		if (transactionId == 0 && tracker != null)
			transactionId = tracker.TransactionId;
		var security = tracker?.Security ?? FindSecurity(order.Exchange, order.TradingSymbol, null, order.Segment);
		var securityId = tracker?.SecurityId ?? ToSecurityId(security);
		var state = order.Status.ToOrderState();
		var serverTime = GrowwNativeExtensions.ParseIndiaTime(order.ExchangeTime)
			?? GrowwNativeExtensions.ParseIndiaTime(order.CreatedAt)
			?? GrowwNativeExtensions.ParseIndiaTime(order.TradeDate)
			?? CurrentTime;
		var filled = order.FilledQuantity ?? 0;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = isLookup ? originId : transactionId,
			TransactionId = isLookup ? transactionId : 0,
			OrderStringId = order.OrderId,
			SecurityId = securityId,
			PortfolioName = _portfolioName,
			OrderType = order.OrderType.ToOrderType(),
			Side = order.TransactionType.ToSide(),
			TimeInForce = order.Validity.ToTimeInForce(),
			OrderPrice = order.Price ?? 0,
			OrderVolume = order.Quantity,
			Balance = order.RemainingQuantity ?? Math.Max(0, (order.Quantity ?? 0) - filled),
			AveragePrice = order.AverageFillPrice,
			OrderState = state,
			ServerTime = serverTime,
			Condition = order.TriggerPrice is > 0 ? new GrowwOrderCondition { TriggerPrice = order.TriggerPrice } : null,
			Error = state == OrderStates.Failed ? new InvalidOperationException(order.Remark.IsEmpty(order.Status)) : null,
		}, cancellationToken);

		if (!isLookup)
			await SendAggregateFill(order.OrderId, transactionId, securityId, order.TransactionType.ToSide(), filled,
				order.AverageFillPrice, serverTime, cancellationToken);
	}

	private async ValueTask ProcessTrade(GrowwTrade trade, long originId, CancellationToken cancellationToken)
	{
		if (trade == null || trade.TradeId.IsEmpty())
			return;
		if (!_tradeIds.TryAdd(trade.TradeId))
			return;
		var security = FindSecurity(trade.Exchange, trade.TradingSymbol, trade.Isin, trade.Segment);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originId,
			OrderStringId = trade.OrderId,
			TradeStringId = trade.TradeId,
			SecurityId = ToSecurityId(security),
			PortfolioName = _portfolioName,
			Side = trade.TransactionType.ToSide(),
			TradePrice = trade.Price,
			TradeVolume = trade.Quantity,
			ServerTime = GrowwNativeExtensions.ParseIndiaTime(trade.TradeDateTime)
				?? GrowwNativeExtensions.ParseIndiaTime(trade.CreatedAt)
				?? CurrentTime,
		}, cancellationToken);
	}

	private async ValueTask ProcessOrderFeed(byte[] data, CancellationToken cancellationToken)
	{
		var update = OrderDetailsBroadCastDto.Parser.ParseFrom(data).OrderDetailUpdateDto;
		if (update == null || update.GrowwOrderId.IsEmpty())
			return;

		_orders.TryGetValue(update.GrowwOrderId, out var tracker);
		var exchange = ToExchange((int)update.Exchange);
		var segment = ToSegment((int)update.Segment);
		var security = tracker?.Security ?? FindSecurity(exchange, null, update.ContractId, segment);
		var securityId = tracker?.SecurityId ?? ToSecurityId(security);
		var transactionId = tracker?.TransactionId ?? 0;
		var status = ToStatus((int)update.OrderStatus);
		var state = status.ToOrderState();
		var serverTime = CurrentTime;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = transactionId != 0 ? transactionId : _orderStatusSubscriptionId,
			OrderStringId = update.GrowwOrderId,
			SecurityId = securityId,
			PortfolioName = tracker?.PortfolioName.IsEmpty(_portfolioName),
			OrderType = ToOrderType((int)update.OrderType),
			Side = (int)update.BuySell == 0 ? Sides.Buy : Sides.Sell,
			TimeInForce = (int)update.Duration == 0 ? TimeInForce.CancelBalance : TimeInForce.PutInQueue,
			OrderPrice = update.Price,
			OrderVolume = update.Qty,
			Balance = update.RemainingQty > 0 ? update.RemainingQty : Math.Max(0, update.Qty - update.FilledQty),
			AveragePrice = update.AvgFillPrice > 0 ? update.AvgFillPrice : null,
			OrderState = state,
			ServerTime = serverTime,
			Condition = update.TriggerPrice > 0 ? new GrowwOrderCondition { TriggerPrice = update.TriggerPrice } : null,
			Error = state == OrderStates.Failed ? new InvalidOperationException(update.Remark.IsEmpty(status)) : null,
		}, cancellationToken);

		await SendAggregateFill(update.GrowwOrderId, transactionId, securityId,
			(int)update.BuySell == 0 ? Sides.Buy : Sides.Sell, update.FilledQty,
			update.AvgFillPrice > 0 ? update.AvgFillPrice : null, serverTime, cancellationToken);
	}

	private async ValueTask SendAggregateFill(string orderId, long transactionId, SecurityId securityId, Sides side,
		decimal filled, decimal? averagePrice, DateTime serverTime, CancellationToken cancellationToken)
	{
		if (filled <= 0)
			return;
		var previous = _orderFills.TryGetValue2(orderId);
		if (filled <= previous)
			return;
		_orderFills[orderId] = filled;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = transactionId != 0 ? transactionId : _orderStatusSubscriptionId,
			OrderStringId = orderId,
			TradeStringId = $"{orderId}:{filled.ToString(CultureInfo.InvariantCulture)}",
			SecurityId = securityId,
			PortfolioName = _portfolioName,
			Side = side,
			TradePrice = averagePrice,
			TradeVolume = filled - previous,
			ServerTime = serverTime,
		}, cancellationToken);
	}

	private async ValueTask ProcessPositionFeed(byte[] data, CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId == 0)
			return;
		var update = PositionDetailProto.Parser.ParseFrom(data);
		if (update.PositionInfo == null)
			return;
		var serverTime = update.SymbolData?.TrTimeStamp > 0
			? GrowwNativeExtensions.FromFeedTimestamp(update.SymbolData.TrTimeStamp)
			: CurrentTime;

		await SendExchangePosition("BSE", update.PositionInfo.SymbolIsin, update.PositionInfo.BSE, serverTime, cancellationToken);
		await SendExchangePosition("NSE", update.PositionInfo.SymbolIsin, update.PositionInfo.NSE, serverTime, cancellationToken);
	}

	private ValueTask SendExchangePosition(string exchange, string contractId, GrowwExchangePositionProto position,
		DateTime serverTime, CancellationToken cancellationToken)
	{
		if (position == null || position.CreditQty == 0 && position.DebitQty == 0)
			return default;
		var security = FindSecurity(exchange, null, contractId, "FNO");
		var quantity = (decimal)(position.CreditQty - position.DebitQty);
		var averagePrice = quantity >= 0 ? position.CreditPrice : position.DebitPrice;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = ToSecurityId(security),
			ServerTime = serverTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, quantity, true)
		.TryAdd(PositionChangeTypes.AveragePrice, (decimal?)averagePrice, true), cancellationToken);
	}

	private static string CreateOrderReference(long transactionId)
		=> "SS-" + GrowwNativeExtensions.ToBase36(transactionId).PadLeft(8, '0');

	private static bool TryParseOrderReference(string reference, out long transactionId)
	{
		transactionId = 0;
		if (reference.IsEmpty() || !reference.StartsWith("SS-", StringComparison.OrdinalIgnoreCase))
			return false;
		try
		{
			foreach (var ch in reference.AsSpan(3))
			{
				var digit = ch is >= '0' and <= '9' ? ch - '0' : char.ToUpperInvariant(ch) is var upper and >= 'A' and <= 'Z' ? upper - 'A' + 10 : -1;
				if (digit < 0)
					return false;
				transactionId = checked(transactionId * 36 + digit);
			}
			return true;
		}
		catch (OverflowException)
		{
			transactionId = 0;
			return false;
		}
	}

	private static string ToExchange(int value) => value switch { 0 => "BSE", 1 => "NSE", 2 => "MCX", 3 => "MCXSX", 4 => "NCDEX", _ => "NSE" };
	private static string ToSegment(int value) => value switch { 0 => "CASH", 1 => "FNO", 2 => "CURRENCY", 3 => "COMMODITY", _ => "CASH" };
	private static string ToStatus(int value) => value switch
	{
		0 => "NEW", 1 => "ACKED", 2 => "TRIGGER_PENDING", 3 => "APPROVED", 4 => "REJECTED", 5 => "FAILED",
		6 => "EXECUTED", 7 => "DELIVERY_AWAITED", 8 => "CANCELLED", 9 => "CANCELLATION_REQUESTED",
		10 => "MODIFICATION_REQUESTED", 11 => "COMPLETED", _ => "NEW",
	};
	private static OrderTypes ToOrderType(int value) => value switch { 0 => OrderTypes.Market, 2 or 3 => OrderTypes.Conditional, _ => OrderTypes.Limit };
}
