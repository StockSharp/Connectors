namespace StockSharp.Dhan;

public partial class DhanMessageAdapter
{
	private readonly SynchronizedDictionary<string, long> _orderTransactions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, decimal> _orderFills = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _tradeIds = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _foreverOrders = new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var condition = regMsg.Condition as DhanOrderCondition;
		if (condition?.IsForever == true)
		{
			await RegisterForeverOrder(regMsg, condition, cancellationToken);
			return;
		}

		var (_, securityId) = regMsg.SecurityId.ToInstrumentKey().ParseInstrumentKey();
		var triggerPrice = condition?.TriggerPrice;
		var result = await _restClient.PlaceOrder(new DhanOrderRequest
		{
			ClientId = ClientId,
			CorrelationId = regMsg.TransactionId.ToString(CultureInfo.InvariantCulture),
			TransactionType = regMsg.Side.ToNative(),
			ExchangeSegment = regMsg.SecurityId.BoardCode,
			ProductType = (condition?.Product ?? DefaultProduct).ToNative(),
			OrderType = (regMsg.OrderType ?? OrderTypes.Limit).ToNative(triggerPrice),
			Validity = regMsg.TimeInForce.ToNative(),
			SecurityId = securityId,
			Quantity = regMsg.Volume.To<long>(),
			DisclosedQuantity = condition?.DisclosedVolume?.To<long>() ?? 0,
			Price = regMsg.OrderType == OrderTypes.Market ? 0 : regMsg.Price,
			TriggerPrice = triggerPrice ?? 0,
			AfterMarketOrder = condition?.AfterMarket == true,
			AfterMarketTime = condition?.AfterMarket == true ? condition.AfterMarketTime.ToNative() : null,
			BracketProfit = condition?.BracketProfit,
			BracketStopLoss = condition?.BracketStopLoss,
		}, cancellationToken);

		await ConfirmRegistration(regMsg, result, condition, false, cancellationToken);
	}

	private async ValueTask RegisterForeverOrder(OrderRegisterMessage regMsg, DhanOrderCondition condition, CancellationToken cancellationToken)
	{
		var product = condition.Product ?? DefaultProduct;
		if (product is not DhanProducts.Delivery and not DhanProducts.MarginTradingFacility)
			throw new InvalidOperationException("Dhan Forever orders support only CNC and MTF products.");
		if (condition.TriggerPrice is not > 0)
			throw new InvalidOperationException("A positive trigger price is required for a Dhan Forever order.");
		if (condition.ForeverFlag == DhanForeverOrderFlags.OneCancelsOther &&
			(condition.SecondPrice == null || condition.SecondTriggerPrice == null || condition.SecondVolume is not > 0))
			throw new InvalidOperationException("Dhan Forever OCO orders require second-leg price, trigger price, and volume.");

		var (_, securityId) = regMsg.SecurityId.ToInstrumentKey().ParseInstrumentKey();
		var result = await _restClient.PlaceForeverOrder(new DhanForeverOrderRequest
		{
			ClientId = ClientId,
			CorrelationId = regMsg.TransactionId.ToString(CultureInfo.InvariantCulture),
			OrderFlag = condition.ForeverFlag.ToNative(),
			TransactionType = regMsg.Side.ToNative(),
			ExchangeSegment = regMsg.SecurityId.BoardCode,
			ProductType = product.ToNative(),
			OrderType = (regMsg.OrderType ?? OrderTypes.Limit).ToNative(null),
			Validity = regMsg.TimeInForce.ToNative(),
			SecurityId = securityId,
			Quantity = regMsg.Volume.To<long>(),
			DisclosedQuantity = condition.DisclosedVolume?.To<long>() ?? 0,
			Price = regMsg.OrderType == OrderTypes.Market ? 0 : regMsg.Price,
			TriggerPrice = condition.TriggerPrice.Value,
			SecondPrice = condition.ForeverFlag == DhanForeverOrderFlags.OneCancelsOther ? condition.SecondPrice : null,
			SecondTriggerPrice = condition.ForeverFlag == DhanForeverOrderFlags.OneCancelsOther ? condition.SecondTriggerPrice : null,
			SecondQuantity = condition.ForeverFlag == DhanForeverOrderFlags.OneCancelsOther ? condition.SecondVolume?.To<long>() : null,
		}, cancellationToken);

		await ConfirmRegistration(regMsg, result, condition, true, cancellationToken);
	}

	private async ValueTask ConfirmRegistration(OrderRegisterMessage regMsg, DhanOrderResult result,
		DhanOrderCondition condition, bool isForever, CancellationToken cancellationToken)
	{
		var orderId = result?.OrderId;
		if (orderId.IsEmpty())
			throw new InvalidOperationException("Dhan did not return an order identifier.");

		_orderTransactions[orderId] = regMsg.TransactionId;
		_orderFills[orderId] = 0;
		if (isForever)
			_foreverOrders.Add(orderId);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			OrderStringId = orderId,
			SecurityId = regMsg.SecurityId,
			PortfolioName = ClientId,
			OrderType = regMsg.OrderType,
			Side = regMsg.Side,
			OrderPrice = regMsg.Price,
			OrderVolume = regMsg.Volume,
			Balance = regMsg.Volume,
			OrderState = result.OrderStatus.ToOrderState(),
			ServerTime = CurrentTime,
			Condition = condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		var orderId = replaceMsg.OldOrderStringId;
		if (orderId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(replaceMsg.OriginalTransactionId));

		var condition = replaceMsg.Condition as DhanOrderCondition;
		var triggerPrice = condition?.TriggerPrice;
		if (condition?.IsForever == true || _foreverOrders.Contains(orderId))
		{
			await _restClient.ModifyForeverOrder(orderId, new DhanForeverModifyRequest
			{
				ClientId = ClientId,
				OrderId = orderId,
				OrderFlag = (condition?.ForeverFlag ?? DhanForeverOrderFlags.Single).ToNative(),
				OrderType = (replaceMsg.OrderType ?? OrderTypes.Limit).ToNative(null),
				LegName = condition?.Leg.ToNative(),
				Quantity = replaceMsg.Volume.To<long>(),
				Price = replaceMsg.OrderType == OrderTypes.Market ? 0 : replaceMsg.Price,
				DisclosedQuantity = condition?.DisclosedVolume?.To<long>() ?? 0,
				TriggerPrice = triggerPrice ?? 0,
				Validity = replaceMsg.TimeInForce.ToNative(),
			}, cancellationToken);
			return;
		}

		await _restClient.ModifyOrder(orderId, new DhanModifyOrderRequest
		{
			ClientId = ClientId,
			OrderId = orderId,
			OrderType = (replaceMsg.OrderType ?? OrderTypes.Limit).ToNative(triggerPrice),
			LegName = condition?.Leg.ToNative(),
			Quantity = replaceMsg.Volume.To<long>(),
			Price = replaceMsg.OrderType == OrderTypes.Market ? 0 : replaceMsg.Price,
			DisclosedQuantity = condition?.DisclosedVolume?.To<long>() ?? 0,
			TriggerPrice = triggerPrice ?? 0,
			Validity = replaceMsg.TimeInForce.ToNative(),
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var orderId = cancelMsg.OrderStringId;
		if (orderId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		if ((cancelMsg.Condition as DhanOrderCondition)?.IsForever == true || _foreverOrders.Contains(orderId))
			await _restClient.CancelForeverOrder(orderId, cancellationToken);
		else
			await _restClient.CancelOrder(orderId, cancellationToken);
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

		foreach (var order in await _restClient.GetOrders(cancellationToken))
			await ProcessOrder(order, statusMsg.TransactionId, true, cancellationToken);

		foreach (var order in await _restClient.GetForeverOrders(cancellationToken))
			await ProcessForeverOrder(order, statusMsg.TransactionId, cancellationToken);

		foreach (var trade in await _restClient.GetTrades(cancellationToken))
			await ProcessTrade(trade, statusMsg.TransactionId, cancellationToken);

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

		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = lookupMsg.TransactionId,
			PortfolioName = ClientId,
			BoardCode = "NSE_EQ",
		}, cancellationToken);

		await SendPortfolioSnapshot(cancellationToken);
		if (!lookupMsg.IsHistoryOnly())
			_portfolioSubscriptionId = lookupMsg.TransactionId;
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshot(CancellationToken cancellationToken)
	{
		var funds = await _restClient.GetFunds(cancellationToken);
		if (funds != null)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = ClientId,
				SecurityId = SecurityId.Money,
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.BeginValue, funds.StartOfDayLimit, true)
			.TryAdd(PositionChangeTypes.CurrentValue, funds.AvailableBalance, true)
			.TryAdd(PositionChangeTypes.BlockedValue, funds.UtilizedAmount + funds.BlockedPayoutAmount, true), cancellationToken);
		}

		foreach (var position in await _restClient.GetPositions(cancellationToken))
		{
			if (position == null || position.ExchangeSegment.IsEmpty())
				continue;

			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = ClientId,
				SecurityId = CreateSecurityId(position.ExchangeSegment, position.SecurityId, position.TradingSymbol),
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, position.NetQuantity, true)
			.TryAdd(PositionChangeTypes.AveragePrice, position.CostPrice, true)
			.TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedProfit, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedProfit, true), cancellationToken);
		}

		foreach (var holding in await _restClient.GetHoldings(cancellationToken))
		{
			if (holding == null)
				continue;

			var instrument = await _restClient.GetEquityInstrument(holding.SecurityId, holding.Isin, holding.Exchange, cancellationToken);
			var securityId = instrument?.ToSecurityId() ?? CreateSecurityId("NSE_EQ", holding.SecurityId, holding.TradingSymbol);
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = ClientId,
				SecurityId = securityId,
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, holding.TotalQuantity, true)
			.TryAdd(PositionChangeTypes.AveragePrice, holding.AverageCostPrice, true)
			.TryAdd(PositionChangeTypes.CurrentPrice, holding.LastTradedPrice, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL,
				holding.LastTradedPrice is decimal lastPrice ? (lastPrice - holding.AverageCostPrice) * holding.TotalQuantity : null, true), cancellationToken);
		}
	}

	private async ValueTask ProcessOrder(DhanOrder order, long originId, bool isLookup, CancellationToken cancellationToken)
	{
		if (order == null || order.OrderId.IsEmpty() || order.ExchangeSegment.IsEmpty())
			return;

		var transactionId = ParseTransactionId(order.CorrelationId, order.OrderId);
		var state = order.OrderStatus.ToOrderState();
		var serverTime = order.ExchangeTime.ToDhanTime() ?? order.UpdateTime.ToDhanTime() ?? order.CreateTime.ToDhanTime() ?? CurrentTime;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = isLookup ? originId : transactionId,
			TransactionId = isLookup ? transactionId : 0,
			OrderStringId = order.OrderId,
			SecurityId = CreateSecurityId(order.ExchangeSegment, order.SecurityId, order.TradingSymbol),
			PortfolioName = ClientId,
			OrderType = order.OrderType.ToOrderType(),
			Side = order.TransactionType.ToSide(),
			TimeInForce = order.Validity.ToTimeInForce(),
			OrderPrice = order.Price,
			OrderVolume = order.Quantity,
			Balance = order.RemainingQuantity,
			AveragePrice = order.AverageTradedPrice,
			OrderState = state,
			ServerTime = serverTime,
			Condition = new DhanOrderCondition
			{
				Product = order.ProductType.ToProduct(),
				TriggerPrice = order.TriggerPrice > 0 ? order.TriggerPrice : null,
				DisclosedVolume = order.DisclosedQuantity,
				AfterMarket = order.AfterMarketOrder,
				BracketProfit = order.BracketProfit > 0 ? order.BracketProfit : null,
				BracketStopLoss = order.BracketStopLoss > 0 ? order.BracketStopLoss : null,
				Leg = order.LegName.ToOrderLeg(),
			},
			Error = state == OrderStates.Failed ? new InvalidOperationException(order.ErrorDescription ?? order.ErrorCode) : null,
		}, cancellationToken);

		_orderFills[order.OrderId] = order.FilledQuantity;
	}

	private async ValueTask ProcessForeverOrder(DhanForeverOrder order, long originId, CancellationToken cancellationToken)
	{
		if (order == null || order.OrderId.IsEmpty() || order.ExchangeSegment.IsEmpty())
			return;

		_foreverOrders.Add(order.OrderId);
		var transactionId = ParseTransactionId(order.CorrelationId, order.OrderId);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originId,
			TransactionId = transactionId,
			OrderStringId = order.OrderId,
			SecurityId = CreateSecurityId(order.ExchangeSegment, order.SecurityId, order.TradingSymbol),
			PortfolioName = ClientId,
			OrderType = OrderTypes.Conditional,
			Side = order.TransactionType.ToSide(),
			OrderPrice = order.Price,
			OrderVolume = order.Quantity,
			Balance = order.Quantity,
			OrderState = order.OrderStatus.ToOrderState(),
			ServerTime = order.UpdateTime.ToDhanTime() ?? order.CreateTime.ToDhanTime() ?? CurrentTime,
			Condition = new DhanOrderCondition
			{
				Product = order.ProductType.ToProduct(),
				TriggerPrice = order.TriggerPrice,
				Leg = order.LegName.ToOrderLeg(),
				IsForever = true,
				ForeverFlag = order.OrderFlag.ToForeverFlag(),
			},
		}, cancellationToken);
	}

	private ValueTask ProcessTrade(DhanTrade trade, long originId, CancellationToken cancellationToken)
	{
		if (trade == null || trade.ExchangeTradeId.IsEmpty() || !_tradeIds.TryAdd(trade.ExchangeTradeId))
			return default;

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originId,
			OrderStringId = trade.OrderId,
			TradeStringId = trade.ExchangeTradeId,
			SecurityId = CreateSecurityId(trade.ExchangeSegment, trade.SecurityId, trade.TradingSymbol),
			PortfolioName = ClientId,
			Side = trade.TransactionType.ToSide(),
			TradePrice = trade.TradedPrice,
			TradeVolume = trade.TradedQuantity,
			ServerTime = trade.ExchangeTime.ToDhanTime() ?? CurrentTime,
		}, cancellationToken);
	}

	private async ValueTask OnOrderReceived(DhanOrderUpdateData order, CancellationToken cancellationToken)
	{
		if (_orderStatusSubscriptionId == 0 || order == null || order.OrderId.IsEmpty())
			return;

		var boardCode = order.Exchange.ToBoardCode(order.Segment);
		var transactionId = ParseTransactionId(order.CorrelationId, order.OrderId);
		var state = order.Status.ToOrderState();
		var serverTime = order.ExchangeTime.ToDhanTime() ?? order.UpdateTime.ToDhanTime() ?? order.OrderTime.ToDhanTime() ?? CurrentTime;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = transactionId == 0 ? _orderStatusSubscriptionId : transactionId,
			OrderStringId = order.OrderId,
			SecurityId = CreateSecurityId(boardCode, order.SecurityId, order.Symbol),
			PortfolioName = ClientId,
			OrderType = order.OrderType.ToOrderType(),
			Side = order.TransactionType.ToSide(),
			TimeInForce = order.Validity.ToTimeInForce(),
			OrderPrice = order.Price,
			OrderVolume = order.Quantity,
			Balance = order.RemainingQuantity,
			AveragePrice = order.AverageTradedPrice,
			OrderState = state,
			ServerTime = serverTime,
			Condition = new DhanOrderCondition
			{
				Product = (order.ProductName ?? order.Product).ToProduct(),
				TriggerPrice = order.TriggerPrice > 0 ? order.TriggerPrice : null,
				DisclosedVolume = order.DisclosedQuantity,
				AfterMarket = order.AfterMarketFlag == "1",
				Leg = order.LegNumber switch
				{
					2 => DhanOrderLegs.StopLoss,
					3 => DhanOrderLegs.Target,
					_ => DhanOrderLegs.Entry,
				},
			},
			Error = state == OrderStates.Failed ? new InvalidOperationException(order.Reason) : null,
		}, cancellationToken);

		if (order.TradedQuantity <= 0)
			return;

		var previousFilled = _orderFills.TryGetValue2(order.OrderId);
		if (order.TradedQuantity <= previousFilled)
			return;
		_orderFills[order.OrderId] = order.TradedQuantity;

		var tradeId = $"{order.OrderId}:{order.TradedQuantity.ToString(CultureInfo.InvariantCulture)}";
		if (!_tradeIds.TryAdd(tradeId))
			return;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = transactionId == 0 ? _orderStatusSubscriptionId : transactionId,
			OrderStringId = order.OrderId,
			TradeStringId = tradeId,
			SecurityId = CreateSecurityId(boardCode, order.SecurityId, order.Symbol),
			PortfolioName = ClientId,
			Side = order.TransactionType.ToSide(),
			TradePrice = order.AverageTradedPrice > 0 ? order.AverageTradedPrice : order.TradedPrice,
			TradeVolume = order.TradedQuantity - previousFilled,
			ServerTime = serverTime,
		}, cancellationToken);
	}

	private long ParseTransactionId(string correlationId, string orderId)
	{
		if (long.TryParse(correlationId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var transactionId))
			_orderTransactions[orderId] = transactionId;
		else
			_orderTransactions.TryGetValue(orderId, out transactionId);
		return transactionId;
	}

	private static SecurityId CreateSecurityId(string boardCode, string securityId, string symbol)
		=> new()
		{
			SecurityCode = symbol.IsEmpty() ? securityId : symbol,
			BoardCode = boardCode,
			Native = boardCode.ToInstrumentKey(securityId),
		};
}
