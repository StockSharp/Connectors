namespace StockSharp.Fyers;

public partial class FyersMessageAdapter
{
	private readonly SynchronizedDictionary<string, long> _orderTransactions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, decimal> _orderFills = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _tradeIds = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _gttOrders = new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var condition = regMsg.Condition as FyersOrderCondition;
		if (condition?.IsGtt == true)
		{
			await RegisterGttOrder(regMsg, condition, cancellationToken);
			return;
		}

		var triggerPrice = condition?.TriggerPrice;
		var result = await _restClient.PlaceOrder(new FyersOrderRequest
		{
			Symbol = regMsg.SecurityId.ToFyersSymbol(),
			Quantity = regMsg.Volume.To<long>(),
			Type = (regMsg.OrderType ?? OrderTypes.Limit).ToNative(triggerPrice),
			Side = regMsg.Side.ToNative(),
			Product = condition?.Product ?? DefaultProduct,
			LimitPrice = regMsg.OrderType == OrderTypes.Market ? 0 : regMsg.Price,
			StopPrice = triggerPrice ?? 0,
			DisclosedQuantity = condition?.DisclosedVolume?.To<long>() ?? 0,
			Validity = regMsg.TimeInForce.ToNative(),
			IsAfterMarket = condition?.IsAfterMarket == true,
			IsSliceOrder = condition?.IsSliceOrder == true,
			StopLoss = condition?.StopLoss ?? 0,
			TakeProfit = condition?.TakeProfit ?? 0,
			OrderTag = regMsg.TransactionId.ToString(CultureInfo.InvariantCulture),
		}, cancellationToken);

		await ConfirmRegistration(regMsg, result, condition, false, cancellationToken);
	}

	private async ValueTask RegisterGttOrder(OrderRegisterMessage regMsg, FyersOrderCondition condition, CancellationToken cancellationToken)
	{
		var product = condition.Product ?? DefaultProduct;
		if (product is not FyersProducts.Delivery and not FyersProducts.Margin and not FyersProducts.MarginTradingFacility)
			throw new InvalidOperationException("FYERS GTT orders support CNC, MARGIN, and MTF products.");
		if (condition.TriggerPrice is not > 0)
			throw new InvalidOperationException("A positive trigger price is required for a FYERS GTT order.");
		if (condition.SecondPrice != null && (condition.SecondTriggerPrice is not > 0 || condition.SecondVolume is not > 0))
			throw new InvalidOperationException("FYERS OCO GTT orders require second-leg price, trigger price, and volume.");

		var result = await _restClient.PlaceGttOrder(new FyersGttOrderRequest
		{
			Side = regMsg.Side.ToNative(),
			Symbol = regMsg.SecurityId.ToFyersSymbol(),
			Product = product,
			OrderInfo = CreateGttInfo(regMsg.Price, condition.TriggerPrice.Value, regMsg.Volume,
				condition.SecondPrice, condition.SecondTriggerPrice, condition.SecondVolume),
		}, cancellationToken);

		await ConfirmRegistration(regMsg, result, condition, true, cancellationToken);
	}

	private async ValueTask ConfirmRegistration(OrderRegisterMessage regMsg, FyersOrderResult result,
		FyersOrderCondition condition, bool isGtt, CancellationToken cancellationToken)
	{
		var orderId = result?.Id;
		if (orderId.IsEmpty())
			throw new InvalidOperationException("FYERS did not return an order identifier.");

		_orderTransactions[orderId] = regMsg.TransactionId;
		_orderFills[orderId] = 0;
		if (isGtt)
			_gttOrders.Add(orderId);

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
			OrderState = OrderStates.Active,
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

		var condition = replaceMsg.Condition as FyersOrderCondition;
		if (condition?.IsGtt == true || _gttOrders.Contains(orderId))
		{
			if (condition?.TriggerPrice is not > 0)
				throw new InvalidOperationException("A positive trigger price is required to modify a FYERS GTT order.");
			await _restClient.ModifyGttOrder(new FyersGttModifyRequest
			{
				Id = orderId,
				OrderInfo = CreateGttInfo(replaceMsg.Price, condition.TriggerPrice.Value, replaceMsg.Volume,
					condition.SecondPrice, condition.SecondTriggerPrice, condition.SecondVolume),
			}, cancellationToken);
			return;
		}

		var triggerPrice = condition?.TriggerPrice;
		await _restClient.ModifyOrder(new FyersModifyOrderRequest
		{
			Id = orderId,
			Type = (replaceMsg.OrderType ?? OrderTypes.Limit).ToNative(triggerPrice),
			LimitPrice = replaceMsg.OrderType == OrderTypes.Market ? 0 : replaceMsg.Price,
			StopPrice = triggerPrice ?? 0,
			Quantity = replaceMsg.Volume.To<long>(),
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var orderId = cancelMsg.OrderStringId;
		if (orderId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		if ((cancelMsg.Condition as FyersOrderCondition)?.IsGtt == true || _gttOrders.Contains(orderId))
			await _restClient.CancelGttOrder(orderId, cancellationToken);
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
			BoardCode = "NSE",
		}, cancellationToken);

		await SendPortfolioSnapshot(lookupMsg.TransactionId, cancellationToken);
		_lastPortfolioRefresh = CurrentTime;
		if (!lookupMsg.IsHistoryOnly())
			_portfolioSubscriptionId = lookupMsg.TransactionId;
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshot(long originalTransactionId, CancellationToken cancellationToken)
	{
		var funds = await _restClient.GetFunds(cancellationToken);
		decimal? findFund(string title)
			=> funds.FirstOrDefault(f => f.Title.EqualsIgnoreCase(title)) is { } fund ? fund.EquityAmount + fund.CommodityAmount : null;

		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = ClientId,
			SecurityId = SecurityId.Money,
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.BeginValue, findFund("Total Balance"), true)
		.TryAdd(PositionChangeTypes.CurrentValue, findFund("Available Balance") ?? findFund("Total Balance"), true)
		.TryAdd(PositionChangeTypes.BlockedValue, findFund("Utilized Amount"), true), cancellationToken);

		foreach (var position in await _restClient.GetPositions(cancellationToken))
		{
			if (position?.Symbol.IsEmpty() != false)
				continue;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = ClientId,
				SecurityId = position.Symbol.ToFyersSecurityId(position.Token),
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, position.NetQuantity, true)
			.TryAdd(PositionChangeTypes.AveragePrice, position.NetAverage, true)
			.TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedProfit, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedProfit, true), cancellationToken);
		}

		foreach (var holding in await _restClient.GetHoldings(cancellationToken))
		{
			if (holding?.Symbol.IsEmpty() != false)
				continue;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = ClientId,
				SecurityId = holding.Symbol.ToFyersSecurityId(holding.Token),
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, holding.RemainingQuantity + holding.T1Quantity, true)
			.TryAdd(PositionChangeTypes.AveragePrice, holding.CostPrice, true)
			.TryAdd(PositionChangeTypes.CurrentPrice, holding.LastPrice, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, holding.ProfitLoss, true), cancellationToken);
		}
	}

	private async ValueTask ProcessOrder(FyersOrder order, long originId, bool isLookup, CancellationToken cancellationToken)
	{
		if (order?.Id.IsEmpty() != false || order.Symbol.IsEmpty())
			return;

		var transactionId = ParseTransactionId(order.OrderTag, order.Id);
		var state = order.OrderStatus.ToOrderState();
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = isLookup ? originId : transactionId == 0 ? _orderStatusSubscriptionId : transactionId,
			TransactionId = isLookup ? transactionId : 0,
			OrderStringId = order.Id,
			SecurityId = order.Symbol.ToFyersSecurityId(order.Token),
			PortfolioName = ClientId,
			OrderType = order.Type.ToOrderType(),
			Side = order.Side.ToSide(),
			TimeInForce = order.Validity.ToTimeInForce(),
			OrderPrice = order.LimitPrice,
			OrderVolume = order.Quantity,
			Balance = order.RemainingQuantity,
			AveragePrice = order.TradedPrice,
			OrderState = state,
			ServerTime = order.OrderTime.ToFyersTime() ?? CurrentTime,
			Condition = new FyersOrderCondition
			{
				Product = order.Product,
				TriggerPrice = order.StopPrice > 0 ? order.StopPrice : null,
				IsAfterMarket = order.IsAfterMarket,
			},
			Error = state == OrderStates.Failed ? new InvalidOperationException(order.Message) : null,
		}, cancellationToken);
		_orderFills[order.Id] = order.FilledQuantity;
	}

	private ValueTask ProcessTrade(FyersTrade trade, long originId, CancellationToken cancellationToken)
	{
		if (trade?.TradeId.IsEmpty() != false || !_tradeIds.TryAdd(trade.TradeId))
			return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originId,
			OrderStringId = trade.OrderId,
			TradeStringId = trade.TradeId,
			SecurityId = trade.Symbol.ToFyersSecurityId(trade.Token),
			PortfolioName = ClientId,
			Side = trade.Side.ToSide(),
			TradePrice = trade.Price,
			TradeVolume = trade.Quantity,
			ServerTime = trade.TradeTime.ToFyersTime() ?? CurrentTime,
		}, cancellationToken);
	}

	private async ValueTask OnOrderReceived(FyersOrderStreamData order, CancellationToken cancellationToken)
	{
		if (order?.Id.IsEmpty() != false || order.Symbol.IsEmpty())
			return;

		var transactionId = ParseTransactionId(order.OrderTag, order.Id);
		if (_orderStatusSubscriptionId == 0 && transactionId == 0)
			return;
		var state = order.OrderStatus.ToOrderState();
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = transactionId == 0 ? _orderStatusSubscriptionId : transactionId,
			OrderStringId = order.Id,
			SecurityId = order.Symbol.ToFyersSecurityId(order.Token),
			PortfolioName = ClientId,
			OrderType = order.Type.ToOrderType(),
			Side = order.Side.ToSide(),
			TimeInForce = order.Validity.ToTimeInForce(),
			OrderPrice = order.LimitPrice,
			OrderVolume = order.Quantity,
			Balance = order.RemainingQuantity,
			AveragePrice = order.TradedPrice,
			OrderState = state,
			ServerTime = order.OrderTime.ToFyersTime() ?? CurrentTime,
			Condition = new FyersOrderCondition
			{
				Product = order.Product,
				TriggerPrice = order.StopPrice > 0 ? order.StopPrice : null,
				IsAfterMarket = order.IsAfterMarket,
			},
			Error = state == OrderStates.Failed ? new InvalidOperationException(order.Message) : null,
		}, cancellationToken);
		_orderFills[order.Id] = order.FilledQuantity;
	}

	private ValueTask OnTradeReceived(FyersTradeStreamData trade, CancellationToken cancellationToken)
	{
		if (trade?.TradeId.IsEmpty() != false)
			return default;
		var transactionId = ParseTransactionId(null, trade.OrderId);
		if (_orderStatusSubscriptionId == 0 && transactionId == 0)
			return default;
		if (!_tradeIds.TryAdd(trade.TradeId))
			return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = transactionId == 0 ? _orderStatusSubscriptionId : transactionId,
			OrderStringId = trade.OrderId,
			TradeStringId = trade.TradeId,
			SecurityId = trade.Symbol.ToFyersSecurityId(trade.Token),
			PortfolioName = ClientId,
			Side = trade.Side.ToSide(),
			TradePrice = trade.Price,
			TradeVolume = trade.Quantity,
			ServerTime = trade.TradeTime.ToFyersTime() ?? CurrentTime,
		}, cancellationToken);
	}

	private ValueTask OnPositionReceived(FyersPositionStreamData position, CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId == 0 || position?.Symbol.IsEmpty() != false)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = _portfolioSubscriptionId,
			PortfolioName = ClientId,
			SecurityId = position.Symbol.ToFyersSecurityId(position.Token),
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, position.NetQuantity, true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.NetAverage, true)
		.TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedProfit, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedProfit, true), cancellationToken);
	}

	private long ParseTransactionId(string orderTag, string orderId)
	{
		if (long.TryParse(orderTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var transactionId))
			_orderTransactions[orderId] = transactionId;
		else
			_orderTransactions.TryGetValue(orderId, out transactionId);
		return transactionId;
	}

	private static FyersGttOrderInfo CreateGttInfo(decimal price, decimal triggerPrice, decimal volume,
		decimal? secondPrice, decimal? secondTriggerPrice, decimal? secondVolume)
		=> new()
		{
			FirstLeg = new FyersGttLeg
			{
				Price = price,
				TriggerPrice = triggerPrice,
				Quantity = volume.To<long>(),
			},
			SecondLeg = secondPrice == null ? null : new FyersGttLeg
			{
				Price = secondPrice.Value,
				TriggerPrice = secondTriggerPrice ?? 0,
				Quantity = secondVolume?.To<long>() ?? 0,
			},
		};
}
