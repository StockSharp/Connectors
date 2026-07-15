namespace StockSharp.Upstox;

public partial class UpstoxMessageAdapter
{
	private readonly SynchronizedDictionary<string, long> _orderTransactions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, decimal> _orderFills = new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private string _portfolioName;

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var condition = regMsg.Condition as UpstoxOrderCondition;
		var triggerPrice = condition?.TriggerPrice;
		var orderIds = await _restClient.PlaceOrder(new UpstoxPlaceOrderRequest
		{
			Quantity = regMsg.Volume.To<long>(),
			Product = (condition?.Product ?? DefaultProduct).ToNative(),
			Validity = regMsg.TimeInForce.ToNative(),
			Price = regMsg.OrderType == OrderTypes.Market ? 0 : regMsg.Price,
			Tag = regMsg.TransactionId.ToString(CultureInfo.InvariantCulture),
			Slice = condition?.AutoSlice == true,
			InstrumentToken = regMsg.SecurityId.ToInstrumentKey(),
			OrderType = (regMsg.OrderType ?? OrderTypes.Limit).ToNative(triggerPrice),
			TransactionType = regMsg.Side.ToNative(),
			DisclosedQuantity = condition?.DisclosedQuantity?.To<long>() ?? 0,
			TriggerPrice = triggerPrice ?? 0,
			IsAfterMarket = condition?.IsAfterMarket == true,
			MarketProtection = condition?.MarketProtection,
		}, cancellationToken);

		if (orderIds.Length == 0)
			throw new InvalidOperationException("Upstox did not return an order identifier.");

		foreach (var orderId in orderIds)
		{
			_orderTransactions[orderId] = regMsg.TransactionId;
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
				OrderState = OrderStates.Active,
				ServerTime = CurrentTime,
			}, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		var orderId = replaceMsg.OldOrderStringId;
		if (orderId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(replaceMsg.OriginalTransactionId));

		var condition = replaceMsg.Condition as UpstoxOrderCondition;
		var triggerPrice = condition?.TriggerPrice;
		await _restClient.ModifyOrder(new UpstoxModifyOrderRequest
		{
			Quantity = replaceMsg.Volume.To<long>(),
			Validity = replaceMsg.TimeInForce.ToNative(),
			Price = replaceMsg.OrderType == OrderTypes.Market ? 0 : replaceMsg.Price,
			OrderId = orderId,
			OrderType = (replaceMsg.OrderType ?? OrderTypes.Limit).ToNative(triggerPrice),
			DisclosedQuantity = condition?.DisclosedQuantity?.To<long>() ?? 0,
			TriggerPrice = triggerPrice ?? 0,
			MarketProtection = condition?.MarketProtection,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var orderId = cancelMsg.OrderStringId;
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
			_orderStatusSubscriptionId = 0;
			return;
		}

		if (_portfolioName.IsEmpty())
			_portfolioName = (await _restClient.GetProfile(cancellationToken))?.UserId;

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

		var profile = await _restClient.GetProfile(cancellationToken)
			?? throw new InvalidOperationException(LocalizedStrings.NoPortfoliosReceived);
		_portfolioName = profile.UserId;

		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = lookupMsg.TransactionId,
			PortfolioName = profile.UserId,
			BoardCode = "NSE_EQ",
		}, cancellationToken);

		var funds = await _restClient.GetFunds(cancellationToken);
		var available = (funds?.Equity?.AvailableMargin ?? 0) + (funds?.Commodity?.AvailableMargin ?? 0);
		var blocked = (funds?.Equity?.UsedMargin ?? 0) + (funds?.Commodity?.UsedMargin ?? 0);
		await SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = profile.UserId,
			SecurityId = SecurityId.Money,
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, available, true)
		.TryAdd(PositionChangeTypes.BlockedValue, blocked, true), cancellationToken);

		foreach (var position in await _restClient.GetPositions(cancellationToken))
			await SendPosition(position.InstrumentToken, position.TradingSymbol ?? position.TradingSymbolLegacy, position.Exchange, profile.UserId,
				position.Quantity, position.AveragePrice, position.LastPrice, position.PnL, position.UnrealizedPnL, position.RealizedPnL, cancellationToken);

		foreach (var holding in await _restClient.GetHoldings(cancellationToken))
			await SendPosition(holding.InstrumentToken, holding.TradingSymbol ?? holding.TradingSymbolLegacy, holding.Exchange, profile.UserId,
				holding.Quantity, holding.AveragePrice, holding.LastPrice, holding.PnL, holding.UnrealizedPnL, holding.RealizedPnL, cancellationToken);

		if (!lookupMsg.IsHistoryOnly())
			_portfolioSubscriptionId = lookupMsg.TransactionId;

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private ValueTask SendPosition(string instrumentKey, string tradingSymbol, string exchange, string portfolioName,
		decimal? quantity, decimal? averagePrice, decimal? lastPrice, decimal? pnl, decimal? unrealizedPnL, decimal? realizedPnL, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = portfolioName,
			SecurityId = instrumentKey.ToUpstoxSecurityId(tradingSymbol, exchange),
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, quantity, true)
		.TryAdd(PositionChangeTypes.AveragePrice, averagePrice, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, lastPrice, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, unrealizedPnL ?? pnl, true)
		.TryAdd(PositionChangeTypes.RealizedPnL, realizedPnL, true), cancellationToken);

	private async ValueTask ProcessOrder(UpstoxOrder order, long originId, bool isLookup, CancellationToken cancellationToken)
	{
		if (order == null || order.OrderId.IsEmpty())
			return;

		var transactionId = 0L;
		if (!long.TryParse(order.Tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out transactionId))
			_orderTransactions.TryGetValue(order.OrderId, out transactionId);
		else
			_orderTransactions[order.OrderId] = transactionId;

		var state = order.Status.ToOrderState();
		var triggerPrice = order.TriggerPrice > 0 ? order.TriggerPrice : null;
		var serverTime = order.ExchangeTimestamp.ToUpstoxTime() ?? order.OrderTimestamp.ToUpstoxTime() ?? CurrentTime;
		var filled = order.FilledQuantity ?? 0;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = isLookup ? originId : transactionId,
			TransactionId = isLookup ? transactionId : 0,
			OrderStringId = order.OrderId,
			SecurityId = order.InstrumentToken.ToUpstoxSecurityId(order.TradingSymbol ?? order.TradingSymbolLegacy, order.Exchange),
			PortfolioName = _portfolioName,
			OrderType = order.OrderType.ToOrderType(),
			Side = order.TransactionType.ToSide(),
			TimeInForce = order.Validity.ToTimeInForce(),
			OrderPrice = order.Price ?? 0,
			OrderVolume = order.Quantity,
			Balance = order.PendingQuantity ?? Math.Max(0, (order.Quantity ?? 0) - filled),
			AveragePrice = order.AveragePrice,
			OrderState = state,
			ServerTime = serverTime,
			Condition = triggerPrice is null ? null : new UpstoxOrderCondition
			{
				TriggerPrice = triggerPrice,
			},
			Error = state == OrderStates.Failed ? new InvalidOperationException(order.StatusMessage ?? order.Status) : null,
		}, cancellationToken);

		if (!isLookup && filled > 0)
		{
			var previousFilled = _orderFills.TryGetValue2(order.OrderId);
			if (filled > previousFilled)
			{
				_orderFills[order.OrderId] = filled;
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OriginalTransactionId = transactionId,
					OrderStringId = order.OrderId,
					TradeStringId = $"{order.OrderId}:{filled.ToString(CultureInfo.InvariantCulture)}",
					SecurityId = order.InstrumentToken.ToUpstoxSecurityId(order.TradingSymbol ?? order.TradingSymbolLegacy, order.Exchange),
					PortfolioName = _portfolioName,
					Side = order.TransactionType.ToSide(),
					TradePrice = order.AveragePrice,
					TradeVolume = filled - previousFilled,
					ServerTime = serverTime,
				}, cancellationToken);
			}
		}
	}

	private ValueTask ProcessTrade(UpstoxTrade trade, long originId, CancellationToken cancellationToken)
	{
		if (trade == null)
			return default;

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originId,
			OrderStringId = trade.OrderId,
			TradeStringId = trade.TradeId,
			SecurityId = trade.InstrumentToken.ToUpstoxSecurityId(trade.TradingSymbol ?? trade.TradingSymbolLegacy, trade.Exchange),
			PortfolioName = _portfolioName,
			Side = trade.TransactionType.ToSide(),
			TradePrice = trade.AveragePrice,
			TradeVolume = trade.Quantity,
			ServerTime = trade.ExchangeTimestamp.ToUpstoxTime() ?? trade.OrderTimestamp.ToUpstoxTime() ?? CurrentTime,
		}, cancellationToken);
	}

	private async ValueTask OnPortfolioUpdate(UpstoxPortfolioUpdate update, CancellationToken cancellationToken)
	{
		switch (update.UpdateType)
		{
			case "order" when _orderStatusSubscriptionId != 0:
				await ProcessOrder(new UpstoxOrder
				{
					Exchange = update.Exchange,
					Product = update.Product,
					Price = update.Price,
					Quantity = update.Quantity,
					Status = update.Status,
					Tag = update.Tag,
					InstrumentToken = update.InstrumentKey ?? update.InstrumentToken,
					TradingSymbol = update.TradingSymbol,
					OrderType = update.OrderType,
					Validity = update.Validity,
					TriggerPrice = update.TriggerPrice,
					TransactionType = update.TransactionType,
					AveragePrice = update.AveragePrice,
					FilledQuantity = update.FilledQuantity,
					PendingQuantity = update.PendingQuantity,
					StatusMessage = update.StatusMessage,
					ExchangeOrderId = update.ExchangeOrderId,
					OrderId = update.OrderId,
					OrderTimestamp = update.OrderTimestamp,
					ExchangeTimestamp = update.ExchangeTimestamp,
				}, _orderStatusSubscriptionId, false, cancellationToken);
				break;

			case "position" when _portfolioSubscriptionId != 0:
			case "holding" when _portfolioSubscriptionId != 0:
				await SendPosition(update.InstrumentKey ?? update.InstrumentToken, update.TradingSymbol, update.Exchange,
					update.UserId ?? _portfolioName, update.Quantity, update.AveragePrice, null, update.PnL, update.UnrealizedPnL, update.RealizedPnL, cancellationToken);
				break;
		}
	}
}
