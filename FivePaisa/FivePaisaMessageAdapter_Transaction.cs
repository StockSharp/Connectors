namespace StockSharp.FivePaisa;

public partial class FivePaisaMessageAdapter
{
	private readonly SynchronizedDictionary<string, long> _orderTransactions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, string> _transactionOrders = [];
	private readonly SynchronizedDictionary<string, string> _exchangeOrderIds = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _tradeIds = new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var condition = regMsg.Condition as FivePaisaOrderCondition;
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not OrderTypes.Limit and not OrderTypes.Market and not OrderTypes.Conditional)
			throw new ArgumentOutOfRangeException(nameof(regMsg.OrderType), orderType, "5paisa supports market, limit, and stop-loss orders.");
		if (orderType == OrderTypes.Conditional && condition?.TriggerPrice is not > 0)
			throw new InvalidOperationException("A positive trigger price is required for a 5paisa stop-loss order.");

		var instrumentKey = regMsg.SecurityId.ToInstrumentKey();
		var (exchange, exchangeType, scripCode) = instrumentKey.ParseInstrumentKey();
		var instrument = await GetInstrument(regMsg.SecurityId, cancellationToken);
		var product = condition?.Product ?? DefaultProduct;
		var result = await _restClient.PlaceOrder(new FivePaisaOrderRequest
		{
			Exchange = exchange,
			ExchangeType = exchangeType,
			ScripCode = scripCode,
			ScripData = instrument?.ScripData ?? string.Empty,
			Price = orderType == OrderTypes.Market ? 0 : regMsg.Price,
			OrderType = regMsg.Side.ToNative(),
			Quantity = regMsg.Volume.To<long>(),
			DisclosedQuantity = condition?.DisclosedVolume?.To<long>() ?? 0,
			StopLossPrice = condition?.TriggerPrice ?? 0,
			IsIntraday = product.ToIsIntraday(),
			OrderValidity = regMsg.TimeInForce.ToNative(),
			AfterMarket = condition?.IsAfterMarket == true ? "Y" : "N",
			RemoteOrderId = regMsg.TransactionId.ToString(CultureInfo.InvariantCulture),
			AlgoId = AlgoId,
		}, cancellationToken);

		var orderId = RememberOrder(result.ExchangeOrderId, result.BrokerOrderId, result.RemoteOrderId, regMsg.TransactionId);
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
			TimeInForce = regMsg.TimeInForce,
			OrderPrice = regMsg.Price,
			OrderVolume = regMsg.Volume,
			Balance = regMsg.Volume,
			OrderState = OrderStates.Active,
			ServerTime = result.Time.ToFivePaisaTime() ?? CurrentTime,
			Condition = condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		var exchangeOrderId = await ResolveExchangeOrderId(replaceMsg.OldOrderStringId, replaceMsg.OriginalTransactionId, cancellationToken);
		var condition = replaceMsg.Condition as FivePaisaOrderCondition;
		if ((replaceMsg.OrderType ?? OrderTypes.Limit) == OrderTypes.Conditional && condition?.TriggerPrice is not > 0)
			throw new InvalidOperationException("A positive trigger price is required to modify a 5paisa stop-loss order.");

		await _restClient.ModifyOrder(new FivePaisaModifyOrderRequest
		{
			ExchangeOrderId = exchangeOrderId,
			Price = replaceMsg.OrderType == OrderTypes.Market ? 0 : replaceMsg.Price,
			Quantity = replaceMsg.Volume.To<long>(),
			StopLossPrice = condition?.TriggerPrice ?? 0,
			DisclosedQuantity = condition?.DisclosedVolume?.To<long>() ?? 0,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var exchangeOrderId = await ResolveExchangeOrderId(cancelMsg.OrderStringId, cancelMsg.OriginalTransactionId, cancellationToken);
		await _restClient.CancelOrder(exchangeOrderId, cancellationToken);
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
			PortfolioName = ClientCode,
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
		var margins = await _restClient.GetMargin(cancellationToken);
		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = ClientCode,
			SecurityId = SecurityId.Money,
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.BeginValue, margins.Sum(m => m.LedgerBalance), true)
		.TryAdd(PositionChangeTypes.CurrentValue, margins.Sum(m => m.NetAvailableMargin), true)
		.TryAdd(PositionChangeTypes.BlockedValue, margins.Sum(m => m.MarginUtilized), true), cancellationToken);

		foreach (var position in await _restClient.GetPositions(cancellationToken))
		{
			if (position == null || position.ScripCode <= 0 || position.Exchange.IsEmpty() || position.ExchangeType.IsEmpty())
				continue;
			var securityId = position.Exchange.ToSecurityId(position.ExchangeType, position.ScripCode, position.ScripName);
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = ClientCode,
				SecurityId = securityId,
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, position.NetQuantity, true)
			.TryAdd(PositionChangeTypes.BeginValue, position.BeginningQuantity, true)
			.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice, true)
			.TryAdd(PositionChangeTypes.CurrentPrice, position.LastPrice, true)
			.TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedPnL, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnL, true), cancellationToken);
		}

		foreach (var holding in await _restClient.GetHoldings(cancellationToken))
		{
			var securityId = GetHoldingSecurityId(holding);
			if (securityId == default)
				continue;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = ClientCode,
				SecurityId = securityId,
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, holding.Quantity, true)
			.TryAdd(PositionChangeTypes.AveragePrice, holding.AveragePrice, true)
			.TryAdd(PositionChangeTypes.CurrentPrice, holding.CurrentPrice, true), cancellationToken);
		}
	}

	private async ValueTask ProcessOrder(FivePaisaOrder order, long originId, bool isLookup, CancellationToken cancellationToken)
	{
		if (order == null || order.ScripCode <= 0)
			return;

		var transactionId = ParseTransactionId(order.RemoteOrderId);
		var orderId = RememberOrder(order.ExchangeOrderId, order.BrokerOrderId, order.RemoteOrderId, transactionId);
		var state = order.OrderStatus.ToOrderState();
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = isLookup ? originId : transactionId == 0 ? _orderStatusSubscriptionId : transactionId,
			TransactionId = isLookup ? transactionId : 0,
			OrderStringId = orderId,
			SecurityId = order.Exchange.ToSecurityId(order.ExchangeType, order.ScripCode, order.ScripName),
			PortfolioName = ClientCode,
			OrderType = order.AtMarket.ToOrderType(order.WithStopLoss),
			Side = order.BuySell.ToSide(),
			TimeInForce = order.OrderValidity.ToTimeInForce(),
			OrderPrice = order.Rate,
			OrderVolume = order.Quantity,
			Balance = order.PendingQuantity,
			AveragePrice = order.AveragePrice,
			OrderState = state,
			ServerTime = order.ExchangeOrderTime.ToFivePaisaTime() ?? order.BrokerOrderTime.ToFivePaisaTime() ?? CurrentTime,
			Condition = new FivePaisaOrderCondition
			{
				Product = order.Product.ToProduct(),
				TriggerPrice = order.StopLossTriggerRate > 0 ? order.StopLossTriggerRate : null,
			},
			Error = state == OrderStates.Failed ? new InvalidOperationException(order.Reason.IsEmpty("5paisa rejected the order.")) : null,
		}, cancellationToken);
	}

	private ValueTask ProcessTrade(FivePaisaTrade trade, long originId, CancellationToken cancellationToken)
	{
		if (trade == null || trade.ExchangeTradeId.IsEmpty() || !_tradeIds.TryAdd(trade.ExchangeTradeId))
			return default;
		var transactionId = _orderTransactions.TryGetValue2(trade.ExchangeOrderId) ?? 0;
		var orderId = GetStableOrderId(trade.ExchangeOrderId, 0);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originId,
			TransactionId = transactionId,
			OrderStringId = orderId,
			TradeStringId = trade.ExchangeTradeId,
			SecurityId = trade.Exchange.ToSecurityId(trade.ExchangeType, trade.ScripCode, trade.ScripName),
			PortfolioName = ClientCode,
			Side = trade.BuySell.ToSide(),
			TradePrice = trade.Rate,
			TradeVolume = trade.Quantity,
			ServerTime = trade.ExchangeTradeTime.ToFivePaisaTime() ?? CurrentTime,
		}, cancellationToken);
	}

	private async ValueTask OnOrderReceived(FivePaisaOrderUpdate update, CancellationToken cancellationToken)
	{
		if (update == null || update.ScripCode <= 0)
			return;

		var transactionId = ParseTransactionId(update.RemoteOrderId);
		var orderId = RememberOrder(update.ExchangeOrderId, update.BrokerOrderId, update.RemoteOrderId, transactionId);
		if (_orderStatusSubscriptionId != 0 || transactionId != 0)
		{
			var state = update.RequestStatus == 0 ? update.Status.ToOrderState() : OrderStates.Failed;
			var quantity = update.OrderQuantity > 0 ? update.OrderQuantity : update.Quantity;
			var price = update.OrderPrice > 0 ? update.OrderPrice : update.Price;
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				OriginalTransactionId = transactionId == 0 ? _orderStatusSubscriptionId : transactionId,
				OrderStringId = orderId,
				SecurityId = update.Exchange.ToSecurityId(update.ExchangeType, update.ScripCode, update.Symbol),
				PortfolioName = ClientCode,
				OrderType = update.AtMarket.ToOrderType(update.StopLossTriggerRate > 0 ? "Y" : "N"),
				Side = update.BuySell.ToSide(),
				OrderPrice = price,
				OrderVolume = quantity,
				Balance = update.PendingQuantity,
				AveragePrice = update.TotalTradedQuantity > 0 ? update.Price : null,
				OrderState = state,
				ServerTime = update.ExchangeOrderTime.ToFivePaisaTime() ?? CurrentTime,
				Condition = new FivePaisaOrderCondition
				{
					Product = update.Product.ToProduct(),
					TriggerPrice = update.StopLossTriggerRate > 0 ? update.StopLossTriggerRate : null,
				},
				Error = state == OrderStates.Failed ? new InvalidOperationException(update.Remark.IsEmpty("5paisa rejected the order.")) : null,
			}, cancellationToken);
		}

		if (!update.RequestType.EqualsIgnoreCase("T") || update.ExchangeTradeId.IsEmpty() || !_tradeIds.TryAdd(update.ExchangeTradeId))
			return;
		var tradeVolume = update.TradedQuantity > 0 ? update.TradedQuantity : update.Quantity;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = transactionId == 0 ? _orderStatusSubscriptionId : transactionId,
			OrderStringId = orderId,
			TradeStringId = update.ExchangeTradeId,
			SecurityId = update.Exchange.ToSecurityId(update.ExchangeType, update.ScripCode, update.Symbol),
			PortfolioName = ClientCode,
			Side = update.BuySell.ToSide(),
			TradePrice = update.Price > 0 ? update.Price : update.OrderPrice,
			TradeVolume = tradeVolume,
			ServerTime = update.ExchangeTradeTime.ToFivePaisaTime() ?? CurrentTime,
		}, cancellationToken);
	}

	private string RememberOrder(string exchangeOrderId, long brokerOrderId, string remoteOrderId, long fallbackTransactionId)
	{
		var transactionId = ParseTransactionId(remoteOrderId);
		if (transactionId == 0)
			transactionId = fallbackTransactionId;
		var brokerId = brokerOrderId > 0 ? brokerOrderId.ToString(CultureInfo.InvariantCulture) : null;
		var hasExchangeId = HasExchangeOrderId(exchangeOrderId);

		string orderId = null;
		if (transactionId != 0)
			_transactionOrders.TryGetValue(transactionId, out orderId);
		orderId ??= hasExchangeId ? exchangeOrderId : brokerId;
		if (orderId.IsEmpty())
			throw new InvalidOperationException("5paisa did not return an order identifier.");

		if (transactionId != 0)
		{
			_transactionOrders[transactionId] = orderId;
			_orderTransactions[orderId] = transactionId;
			if (hasExchangeId)
				_orderTransactions[exchangeOrderId] = transactionId;
			if (!brokerId.IsEmpty())
				_orderTransactions[brokerId] = transactionId;
		}
		if (hasExchangeId)
		{
			_exchangeOrderIds[orderId] = exchangeOrderId;
			if (!brokerId.IsEmpty())
				_exchangeOrderIds[brokerId] = exchangeOrderId;
		}
		return orderId;
	}

	private string GetStableOrderId(string exchangeOrderId, long brokerOrderId)
	{
		var transactionId = _orderTransactions.TryGetValue2(exchangeOrderId) ?? 0;
		if (transactionId != 0 && _transactionOrders.TryGetValue(transactionId, out var orderId))
			return orderId;
		return HasExchangeOrderId(exchangeOrderId)
			? exchangeOrderId
			: brokerOrderId > 0 ? brokerOrderId.ToString(CultureInfo.InvariantCulture) : exchangeOrderId;
	}

	private async ValueTask<string> ResolveExchangeOrderId(string orderId, long originalTransactionId, CancellationToken cancellationToken)
	{
		if (orderId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(originalTransactionId));
		if (_exchangeOrderIds.TryGetValue(orderId, out var exchangeOrderId) && HasExchangeOrderId(exchangeOrderId))
			return exchangeOrderId;

		foreach (var order in await _restClient.GetOrders(cancellationToken))
		{
			var transactionId = ParseTransactionId(order.RemoteOrderId);
			var stableId = RememberOrder(order.ExchangeOrderId, order.BrokerOrderId, order.RemoteOrderId, transactionId);
			if ((stableId.EqualsIgnoreCase(orderId) || order.ExchangeOrderId.EqualsIgnoreCase(orderId) ||
				order.BrokerOrderId.ToString(CultureInfo.InvariantCulture).EqualsIgnoreCase(orderId) || transactionId == originalTransactionId) &&
				HasExchangeOrderId(order.ExchangeOrderId))
				return order.ExchangeOrderId;
		}

		if (HasExchangeOrderId(orderId))
			return orderId;
		throw new InvalidOperationException($"The 5paisa exchange order ID for '{orderId}' is not available yet.");
	}

	private long ParseTransactionId(string remoteOrderId)
		=> long.TryParse(remoteOrderId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var transactionId) ? transactionId : 0;

	private static bool HasExchangeOrderId(string orderId)
		=> !orderId.IsEmpty() && orderId != "0";

	private static SecurityId GetHoldingSecurityId(FivePaisaHolding holding)
	{
		if (holding == null)
			return default;
		if (!holding.Exchange.IsEmpty() && !holding.ExchangeType.IsEmpty())
		{
			var code = holding.Exchange.EqualsIgnoreCase("B") ? holding.BseCode : holding.NseCode;
			if (code > 0)
				return holding.Exchange.ToSecurityId(holding.ExchangeType, code, holding.Symbol.IsEmpty(holding.FullName));
		}
		if (holding.NseCode > 0)
			return "N".ToSecurityId("C", holding.NseCode, holding.Symbol.IsEmpty(holding.FullName));
		if (holding.BseCode > 0)
			return "B".ToSecurityId("C", holding.BseCode, holding.Symbol.IsEmpty(holding.FullName));
		return default;
	}
}
