namespace StockSharp.KotakNeo;

public partial class KotakNeoMessageAdapter
{
	private readonly SynchronizedDictionary<string, long> _orderTransactions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, decimal> _orderFills = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, KotakNeoOrder> _orderDetails = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _tradeIds = new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var condition = regMsg.Condition as KotakNeoOrderCondition;
		var product = condition?.Product ?? DefaultProduct;
		if (product == KotakNeoProducts.Bracket && (condition?.SquareOffValue is not > 0 || condition.StopLossValue is not > 0))
			throw new InvalidOperationException("Kotak Neo bracket orders require positive square-off and stop-loss values.");

		var instrumentKey = regMsg.SecurityId.ToInstrumentKey();
		var (exchangeSegment, token) = instrumentKey.ParseInstrumentKey();
		var instrument = await _restClient.GetInstrument(instrumentKey, cancellationToken)
			?? throw new InvalidOperationException($"Kotak Neo instrument '{instrumentKey}' was not found in the current scrip master.");
		var triggerPrice = condition?.TriggerPrice;
		var result = await _restClient.PlaceOrder(new KotakNeoOrderRequest
		{
			AfterMarket = condition?.AfterMarket == true ? "YES" : "NO",
			DisclosedQuantity = condition?.DisclosedVolume?.To<long>() ?? 0,
			ExchangeSegment = exchangeSegment,
			MarketProtection = condition?.MarketProtection ?? 0,
			Product = product.ToNative(),
			Price = regMsg.OrderType == OrderTypes.Market ? 0 : regMsg.Price,
			OrderType = (regMsg.OrderType ?? OrderTypes.Limit).ToNative(triggerPrice),
			Quantity = regMsg.Volume.To<long>(),
			Validity = regMsg.TimeInForce.ToNative(),
			TriggerPrice = triggerPrice ?? 0,
			TradingSymbol = instrument.TradingSymbol,
			TransactionType = regMsg.Side.ToNative(),
			Tag = condition?.Tag.IsEmpty() == false ? condition.Tag : regMsg.TransactionId.ToString(CultureInfo.InvariantCulture),
			Token = product == KotakNeoProducts.Bracket ? token : null,
			SquareOffType = product == KotakNeoProducts.Bracket ? "Absolute" : null,
			StopLossType = product == KotakNeoProducts.Bracket ? "Absolute" : null,
			StopLossValue = product == KotakNeoProducts.Bracket ? condition.StopLossValue : null,
			SquareOffValue = product == KotakNeoProducts.Bracket ? condition.SquareOffValue : null,
			LastTradedPrice = product == KotakNeoProducts.Bracket ? regMsg.Price : null,
			TrailingStopLoss = product == KotakNeoProducts.Bracket && condition.TrailingStopLossValue is > 0 ? "Y" : null,
			TrailingStopLossValue = product == KotakNeoProducts.Bracket ? condition.TrailingStopLossValue : null,
		}, cancellationToken);

		var orderId = EnsureOrderResult(result, "place order");
		_orderTransactions[orderId] = regMsg.TransactionId;
		_orderFills[orderId] = 0;
		_orderDetails[orderId] = new KotakNeoOrder
		{
			OrderId = orderId,
			ExchangeSegment = exchangeSegment,
			Token = token,
			TradingSymbol = instrument.TradingSymbol,
			Product = product.ToNative(),
			PriceType = (regMsg.OrderType ?? OrderTypes.Limit).ToNative(triggerPrice),
			TransactionType = regMsg.Side.ToNative(),
			Validity = regMsg.TimeInForce.ToNative(),
			Price = regMsg.Price,
			TriggerPrice = triggerPrice,
			Quantity = regMsg.Volume,
			UnfilledQuantity = regMsg.Volume,
			ClientOrderId = regMsg.TransactionId.ToString(CultureInfo.InvariantCulture),
		};

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			OrderStringId = orderId,
			SecurityId = regMsg.SecurityId,
			PortfolioName = UserCode,
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

		var previous = await GetOrder(orderId, cancellationToken);
		var condition = replaceMsg.Condition as KotakNeoOrderCondition;
		var triggerPrice = condition?.TriggerPrice ?? previous.TriggerPrice ?? 0;
		var product = condition?.Product is { } specifiedProduct ? specifiedProduct.ToNative() : previous.Product;
		var result = await _restClient.ModifyOrder(new KotakNeoModifyOrderRequest
		{
			Token = previous.Token,
			MarketProtection = condition?.MarketProtection ?? 0,
			Product = product,
			DisclosedQuantity = condition?.DisclosedVolume?.To<long>() ?? previous.DisclosedQuantity?.To<long>() ?? 0,
			Validity = replaceMsg.TimeInForce.ToNative(),
			TradingSymbol = previous.TradingSymbol,
			TransactionType = previous.TransactionType,
			Price = replaceMsg.OrderType == OrderTypes.Market ? 0 : replaceMsg.Price,
			OrderType = (replaceMsg.OrderType ?? previous.PriceType.ToOrderType()).ToNative(triggerPrice > 0 ? triggerPrice : null),
			FilledQuantity = previous.FilledQuantity?.To<long>() ?? 0,
			AfterMarket = condition?.AfterMarket == true ? "YES" : "NO",
			TriggerPrice = triggerPrice,
			Quantity = replaceMsg.Volume.To<long>(),
			OrderId = orderId,
			ExchangeSegment = previous.ExchangeSegment,
		}, cancellationToken);
		EnsureOrderResult(result, "modify order", orderId);
		_orderTransactions[orderId] = replaceMsg.TransactionId;
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var orderId = cancelMsg.OrderStringId;
		if (orderId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));
		var product = (cancelMsg.Condition as KotakNeoOrderCondition)?.Product
			?? (await GetOrder(orderId, cancellationToken)).Product.ToProduct();

		var result = await _restClient.CancelOrder(new KotakNeoCancelOrderRequest
		{
			OrderId = orderId,
			AfterMarket = (cancelMsg.Condition as KotakNeoOrderCondition)?.AfterMarket == true ? "YES" : "NO",
		}, product, cancellationToken);
		EnsureOrderResult(result, "cancel order", orderId);
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

		var orders = await _restClient.GetOrders(cancellationToken);
		EnsureResponse(orders, "order book");
		foreach (var order in orders.Data ?? [])
			await ProcessOrder(order, statusMsg.TransactionId, true, cancellationToken);

		var trades = await _restClient.GetTrades(cancellationToken);
		EnsureResponse(trades, "trade book");
		foreach (var trade in trades.Data ?? [])
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
			PortfolioName = UserCode,
			BoardCode = "NSE",
		}, cancellationToken);
		await SendPortfolioSnapshot(lookupMsg.TransactionId, cancellationToken);
		if (!lookupMsg.IsHistoryOnly())
			_portfolioSubscriptionId = lookupMsg.TransactionId;
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshot(long originalTransactionId, CancellationToken cancellationToken)
	{
		var limits = await _restClient.GetLimits(cancellationToken);
		if (limits != null)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = UserCode,
				SecurityId = SecurityId.Money,
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.BeginValue, limits.NotionalCash, true)
			.TryAdd(PositionChangeTypes.CurrentValue, limits.Net, true)
			.TryAdd(PositionChangeTypes.BlockedValue, limits.MarginUsed ?? limits.AmountUtilized, true), cancellationToken);
		}

		var positions = await _restClient.GetPositions(cancellationToken);
		EnsureResponse(positions, "positions");
		foreach (var position in positions.Data ?? [])
		{
			if (position == null || position.ExchangeSegment.IsEmpty())
				continue;
			var netQuantity = position.NetQuantity ??
				((position.CarryForwardBuyQuantity ?? 0) + (position.FilledBuyQuantity ?? 0) -
				(position.CarryForwardSellQuantity ?? 0) - (position.FilledSellQuantity ?? 0));
			var averagePrice = position.AveragePrice ?? CalculateAveragePrice(position, netQuantity);
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = UserCode,
				SecurityId = KotakNeoExtensions.CreateSecurityId(position.ExchangeSegment, position.Token, position.TradingSymbol),
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, netQuantity, true)
			.TryAdd(PositionChangeTypes.AveragePrice, averagePrice, true)
			.TryAdd(PositionChangeTypes.CurrentPrice, position.LastPrice, true)
			.TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedProfit, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedProfit, true), cancellationToken);
		}

		var holdings = await _restClient.GetHoldings(cancellationToken);
		EnsureResponse(holdings, "holdings");
		foreach (var holding in holdings.Data ?? [])
		{
			if (holding == null || holding.ExchangeSegment.IsEmpty())
				continue;
			var token = holding.ExchangeIdentifier.IsEmpty() ? holding.InstrumentToken : holding.ExchangeIdentifier;
			var instrument = await _restClient.GetInstrument(holding.ExchangeSegment, token, holding.DisplaySymbol, cancellationToken);
			var securityId = instrument?.ToSecurityId()
				?? KotakNeoExtensions.CreateSecurityId(holding.ExchangeSegment, token, holding.DisplaySymbol ?? holding.Symbol);
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = UserCode,
				SecurityId = securityId,
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, holding.Quantity, true)
			.TryAdd(PositionChangeTypes.AveragePrice, holding.AveragePrice, true)
			.TryAdd(PositionChangeTypes.CurrentPrice, holding.ClosingPrice, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL,
				holding.ClosingPrice is decimal last && holding.AveragePrice is decimal average && holding.Quantity is decimal quantity
					? (last - average) * quantity : null, true), cancellationToken);
		}
	}

	private async ValueTask ProcessOrder(KotakNeoOrder order, long originId, bool isLookup, CancellationToken cancellationToken)
	{
		if (order == null || order.OrderId.IsEmpty() || order.ExchangeSegment.IsEmpty())
			return;
		_orderDetails[order.OrderId] = order;
		var transactionId = ParseTransactionId(order.ClientOrderId, order.OrderId);
		var state = (order.OrderStatus ?? order.Status).ToOrderState();
		var serverTime = order.ExchangeTime.ToKotakTime() ?? order.UpdateTime.ToKotakTime() ?? order.OrderTime.ToKotakTime() ?? CurrentTime;
		var quantity = order.Quantity ?? 0;
		var filled = order.FilledQuantity ?? 0;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = isLookup ? originId : transactionId == 0 ? _orderStatusSubscriptionId : transactionId,
			TransactionId = isLookup ? transactionId : 0,
			OrderStringId = order.OrderId,
			SecurityId = KotakNeoExtensions.CreateSecurityId(order.ExchangeSegment, order.Token, order.TradingSymbol ?? order.Symbol),
			PortfolioName = UserCode,
			OrderType = order.PriceType.ToOrderType(),
			Side = order.TransactionType.ToSide(),
			TimeInForce = order.Validity.ToTimeInForce(),
			OrderPrice = order.Price ?? 0,
			OrderVolume = quantity,
			Balance = order.UnfilledQuantity ?? Math.Max(0, quantity - filled),
			AveragePrice = order.AveragePrice,
			OrderState = state,
			ServerTime = serverTime,
			Condition = new KotakNeoOrderCondition
			{
				Product = order.Product.ToProduct(),
				TriggerPrice = order.TriggerPrice is > 0 ? order.TriggerPrice : null,
				DisclosedVolume = order.DisclosedQuantity,
			},
			Error = state == OrderStates.Failed && !order.RejectReason.IsEmpty() ? new InvalidOperationException(order.RejectReason) : null,
		}, cancellationToken);
		_orderFills[order.OrderId] = filled;
	}

	private ValueTask ProcessTrade(KotakNeoTrade trade, long originId, CancellationToken cancellationToken)
	{
		if (trade == null || trade.TradeId.IsEmpty() || !_tradeIds.TryAdd(trade.TradeId))
			return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originId,
			OrderStringId = trade.OrderId,
			TradeStringId = trade.TradeId,
			SecurityId = KotakNeoExtensions.CreateSecurityId(trade.ExchangeSegment, trade.Token, trade.TradingSymbol ?? trade.Symbol),
			PortfolioName = UserCode,
			Side = trade.TransactionType.ToSide(),
			TradePrice = trade.Price,
			TradeVolume = trade.Quantity,
			ServerTime = trade.ExchangeTime.ToKotakTime() ?? trade.FillTime.ToKotakTime(trade.FillDate) ?? CurrentTime,
		}, cancellationToken);
	}

	private async ValueTask OnOrderReceived(KotakNeoOrder order, CancellationToken cancellationToken)
	{
		if (_orderStatusSubscriptionId == 0 || order == null || order.OrderId.IsEmpty())
			return;

		var previousFilled = _orderFills.TryGetValue2(order.OrderId);
		await ProcessOrder(order, _orderStatusSubscriptionId, false, cancellationToken);
		var currentFilled = order.FilledQuantity ?? 0;
		if (currentFilled <= previousFilled)
			return;

		var tradeId = $"{order.OrderId}:{currentFilled.ToString(CultureInfo.InvariantCulture)}";
		if (!_tradeIds.TryAdd(tradeId))
			return;
		var transactionId = ParseTransactionId(order.ClientOrderId, order.OrderId);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = transactionId == 0 ? _orderStatusSubscriptionId : transactionId,
			OrderStringId = order.OrderId,
			TradeStringId = tradeId,
			SecurityId = KotakNeoExtensions.CreateSecurityId(order.ExchangeSegment, order.Token, order.TradingSymbol ?? order.Symbol),
			PortfolioName = UserCode,
			Side = order.TransactionType.ToSide(),
			TradePrice = order.AveragePrice ?? order.Price,
			TradeVolume = currentFilled - previousFilled,
			ServerTime = order.ExchangeTime.ToKotakTime() ?? order.UpdateTime.ToKotakTime() ?? CurrentTime,
		}, cancellationToken);
	}

	private async Task<KotakNeoOrder> GetOrder(string orderId, CancellationToken cancellationToken)
	{
		if (_orderDetails.TryGetValue(orderId, out var order))
			return order;
		var response = await _restClient.GetOrders(cancellationToken);
		EnsureResponse(response, "order book");
		order = response.Data?.FirstOrDefault(o => o.OrderId.EqualsIgnoreCase(orderId));
		return order ?? throw new InvalidOperationException($"Kotak Neo order '{orderId}' was not found.");
	}

	private long ParseTransactionId(string clientOrderId, string orderId)
	{
		if (long.TryParse(clientOrderId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var transactionId))
			_orderTransactions[orderId] = transactionId;
		else
			_orderTransactions.TryGetValue(orderId, out transactionId);
		return transactionId;
	}

	private static decimal? CalculateAveragePrice(KotakNeoPosition position, decimal netQuantity)
	{
		if (netQuantity == 0)
			return null;
		var buyQuantity = (position.CarryForwardBuyQuantity ?? 0) + (position.FilledBuyQuantity ?? 0);
		var sellQuantity = (position.CarryForwardSellQuantity ?? 0) + (position.FilledSellQuantity ?? 0);
		if (netQuantity > 0 && buyQuantity > 0)
			return ((position.CarryForwardBuyAmount ?? 0) + (position.BuyAmount ?? 0)) / buyQuantity;
		if (netQuantity < 0 && sellQuantity > 0)
			return ((position.CarryForwardSellAmount ?? 0) + (position.SellAmount ?? 0)) / sellQuantity;
		return null;
	}

	private static string EnsureOrderResult(KotakNeoResponse<KotakNeoNoData> response, string operation, string fallbackOrderId = null)
	{
		EnsureResponse(response, operation);
		var orderId = response.OrderId.IsEmpty() ? fallbackOrderId : response.OrderId;
		if (orderId.IsEmpty())
			throw new InvalidOperationException($"Kotak Neo {operation} did not return an order identifier.");
		return orderId;
	}

	private static void EnsureResponse<T>(KotakNeoResponse<T> response, string operation)
	{
		if (response == null)
			throw new InvalidOperationException($"Kotak Neo {operation} returned an empty response.");
		if (response.StatusCode is > 0 and not 200 || response.Status.EqualsIgnoreCase("Not_Ok") || response.Status.EqualsIgnoreCase("NotOk"))
			throw new InvalidOperationException($"Kotak Neo {operation} failed: {response.ErrorMessage ?? response.Message ?? response.Status}.");
	}
}
