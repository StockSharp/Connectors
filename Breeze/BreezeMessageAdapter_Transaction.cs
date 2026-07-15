namespace StockSharp.Breeze;

public partial class BreezeMessageAdapter
{
	private readonly SynchronizedDictionary<string, long> _orderTransactions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, decimal> _orderFills = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _tradeIds = new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		if (regMsg.OrderType == OrderTypes.Market)
			throw new NotSupportedException("ICICI Direct does not permit market orders through Breeze API. Submit an explicit limit price.");
		var instrument = await _restClient.GetInstrument(regMsg.SecurityId, cancellationToken);
		var condition = regMsg.Condition as BreezeOrderCondition;
		var product = condition?.Product ?? instrument.ToProduct();
		ValidateProduct(instrument, product);
		var result = await _restClient.PlaceOrder(new BreezeOrderRequest
		{
			StockCode = instrument.StockCode,
			ExchangeCode = instrument.ToExchangeCode(),
			Product = product.ToNative(),
			Action = regMsg.Side.ToNative(),
			OrderType = condition?.TriggerPrice is > 0 ? "stoploss" : "limit",
			Quantity = Format(regMsg.Volume),
			Price = Format(regMsg.Price),
			Validity = regMsg.TimeInForce.ToNative(),
			StopLoss = FormatNullable(condition?.TriggerPrice),
			DisclosedQuantity = FormatNullable(condition?.DisclosedVolume),
			ExpiryDate = FormatExpiry(instrument.ExpiryDate),
			Right = ToRight(instrument),
			StrikePrice = FormatNullable(instrument.StrikePrice),
			UserRemark = condition?.UserRemark.IsEmpty() == false ? condition.UserRemark : regMsg.TransactionId.ToString(CultureInfo.InvariantCulture),
		}, cancellationToken);

		var orderId = result?.OrderId;
		if (orderId.IsEmpty())
			throw new InvalidOperationException($"Breeze did not return an order identifier. {result?.Message}");
		_orderTransactions[orderId] = regMsg.TransactionId;
		_orderFills[orderId] = 0;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			OrderStringId = orderId,
			SecurityId = regMsg.SecurityId,
			PortfolioName = _portfolioName,
			OrderType = condition?.TriggerPrice is > 0 ? OrderTypes.Conditional : OrderTypes.Limit,
			Side = regMsg.Side,
			TimeInForce = regMsg.TimeInForce,
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
		if (replaceMsg.OrderType == OrderTypes.Market)
			throw new NotSupportedException("ICICI Direct does not permit market orders through Breeze API.");
		var orderId = replaceMsg.OldOrderStringId;
		if (orderId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(replaceMsg.OriginalTransactionId));
		var instrument = await _restClient.GetInstrument(replaceMsg.SecurityId, cancellationToken);
		var condition = replaceMsg.Condition as BreezeOrderCondition;
		await _restClient.ModifyOrder(new BreezeModifyOrderRequest
		{
			OrderId = orderId,
			ExchangeCode = instrument.ToExchangeCode(),
			OrderType = condition?.TriggerPrice is > 0 ? "stoploss" : "limit",
			Quantity = Format(replaceMsg.Volume),
			Price = Format(replaceMsg.Price),
			Validity = replaceMsg.TimeInForce.ToNative(),
			StopLoss = FormatNullable(condition?.TriggerPrice),
			DisclosedQuantity = FormatNullable(condition?.DisclosedVolume),
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var orderId = cancelMsg.OrderStringId;
		if (orderId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));
		var exchange = cancelMsg.SecurityId.BoardCode.EqualsIgnoreCase("NFO") ? "NFO" : "NSE";
		await _restClient.CancelOrder(exchange, orderId, cancellationToken);
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

		var to = DateTime.UtcNow;
		var from = to.AddDays(-30);
		foreach (var exchange in new[] { "NSE", "NFO" })
		{
			foreach (var order in await _restClient.GetOrders(exchange, from, to, cancellationToken))
				await ProcessOrder(order, statusMsg.TransactionId, true, cancellationToken);
			foreach (var trade in await _restClient.GetTrades(exchange, from, to, cancellationToken))
				await ProcessTrade(trade, statusMsg.TransactionId, cancellationToken);
		}

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
			PortfolioName = _portfolioName,
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
		if (funds != null)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = _portfolioName,
				SecurityId = SecurityId.Money,
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.BeginValue, funds.TotalBankBalance, true)
			.TryAdd(PositionChangeTypes.CurrentValue, funds.UnallocatedBalance + funds.AllocatedEquity + funds.AllocatedFno, true)
			.TryAdd(PositionChangeTypes.BlockedValue, funds.BlockedEquity + funds.BlockedFno, true), cancellationToken);
		}

		foreach (var position in await _restClient.GetPositions(cancellationToken))
		{
			if (position?.StockCode.IsEmpty() != false)
				continue;
			var instrument = await _restClient.FindInstrument(position.ExchangeCode, position.StockCode, position.ProductType,
				position.ExpiryDate, position.Right, position.StrikePrice, cancellationToken);
			var quantity = position.Action.EqualsIgnoreCase("sell") ? -position.Quantity : position.Quantity;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = _portfolioName,
				SecurityId = instrument.ToSecurityId(),
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, quantity, true)
			.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice, true)
			.TryAdd(PositionChangeTypes.CurrentPrice, position.LastPrice, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, position.ProfitLoss, true), cancellationToken);
		}

		foreach (var exchange in new[] { "NSE", "NFO" })
		{
			foreach (var holding in await _restClient.GetHoldings(exchange, cancellationToken))
			{
				if (holding?.StockCode.IsEmpty() != false)
					continue;
				var instrument = await _restClient.FindInstrument(holding.ExchangeCode.IsEmpty() ? exchange : holding.ExchangeCode,
					holding.StockCode, holding.ProductType, holding.ExpiryDate, holding.Right, holding.StrikePrice, cancellationToken);
				var quantity = holding.Action.EqualsIgnoreCase("sell") ? -holding.Quantity : holding.Quantity;
				await SendOutMessageAsync(new PositionChangeMessage
				{
					OriginalTransactionId = originalTransactionId,
					PortfolioName = _portfolioName,
					SecurityId = instrument.ToSecurityId(),
					ServerTime = CurrentTime,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, quantity, true)
				.TryAdd(PositionChangeTypes.AveragePrice, holding.AveragePrice, true)
				.TryAdd(PositionChangeTypes.CurrentPrice, holding.LastPrice, true)
				.TryAdd(PositionChangeTypes.RealizedPnL, holding.RealizedProfit, true)
				.TryAdd(PositionChangeTypes.UnrealizedPnL, holding.UnrealizedProfit, true), cancellationToken);
			}
		}
	}

	private async ValueTask ProcessOrder(BreezeOrder order, long originId, bool isLookup, CancellationToken cancellationToken)
	{
		if (order?.OrderId.IsEmpty() != false || order.StockCode.IsEmpty())
			return;
		var instrument = await _restClient.FindInstrument(order.ExchangeCode, order.StockCode, order.ProductType,
			order.ExpiryDate, order.Right, order.StrikePrice, cancellationToken);
		var transactionId = ParseTransactionId(order.UserRemark, order.OrderId);
		var state = order.Status.ToOrderState();
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = isLookup ? originId : transactionId == 0 ? _orderStatusSubscriptionId : transactionId,
			TransactionId = isLookup ? transactionId : 0,
			OrderStringId = order.OrderId,
			SecurityId = instrument.ToSecurityId(),
			PortfolioName = _portfolioName,
			OrderType = order.StopLoss > 0 || order.OrderType.EqualsIgnoreCase("stoploss") ? OrderTypes.Conditional : OrderTypes.Limit,
			Side = order.Action.ToSide(),
			TimeInForce = order.Validity.ToTimeInForce(),
			OrderPrice = order.Price,
			OrderVolume = order.Quantity,
			Balance = order.PendingQuantity,
			AveragePrice = order.AveragePrice,
			OrderState = state,
			ServerTime = order.OrderDateTime.ParseBreezeTime() ?? CurrentTime,
			Condition = new BreezeOrderCondition
			{
				Product = ParseProduct(order.ProductType),
				TriggerPrice = order.StopLoss > 0 ? order.StopLoss : null,
				DisclosedVolume = order.DisclosedQuantity > 0 ? order.DisclosedQuantity : null,
				UserRemark = order.UserRemark,
			},
			Error = state == OrderStates.Failed ? new InvalidOperationException(order.Status) : null,
		}, cancellationToken);
		_orderFills[order.OrderId] = Math.Max(0, order.Quantity - order.PendingQuantity - order.CancelledQuantity);
	}

	private async ValueTask ProcessTrade(BreezeTrade trade, long originId, CancellationToken cancellationToken)
	{
		var tradeId = trade?.TradeId.IsEmpty() == false ? trade.TradeId : trade?.ExchangeTradeId;
		if (tradeId.IsEmpty() && trade?.OrderId.IsEmpty() == false)
			tradeId = string.Join(":", trade.OrderId, trade.Action, trade.Quantity.ToString(CultureInfo.InvariantCulture),
				trade.AverageCost.ToString(CultureInfo.InvariantCulture), trade.TradeDate);
		if (tradeId.IsEmpty() || !_tradeIds.TryAdd(tradeId) || trade.StockCode.IsEmpty())
			return;
		var instrument = await _restClient.FindInstrument(trade.ExchangeCode, trade.StockCode, trade.ProductType,
			trade.ExpiryDate, trade.Right, trade.StrikePrice, cancellationToken);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originId,
			OrderStringId = trade.OrderId,
			TradeStringId = tradeId,
			SecurityId = instrument.ToSecurityId(),
			PortfolioName = _portfolioName,
			Side = trade.Action.ToSide(),
			TradePrice = trade.TradedPrice > 0 ? trade.TradedPrice : trade.ExecutionPrice > 0 ? trade.ExecutionPrice : trade.AverageCost > 0 ? trade.AverageCost : trade.AveragePrice,
			TradeVolume = trade.TradedQuantity > 0 ? trade.TradedQuantity : trade.ExecutedQuantity > 0 ? trade.ExecutedQuantity : trade.Quantity,
			ServerTime = (trade.ExchangeTradeTime.IsEmpty() ? trade.TradeDate : trade.ExchangeTradeTime).ParseBreezeTime() ?? CurrentTime,
		}, cancellationToken);
	}

	private async ValueTask OnOrderReceived(BreezeOrderUpdate update, CancellationToken cancellationToken)
	{
		if (update?.OrderId.IsEmpty() != false || update.StockCode.IsEmpty())
			return;
		var transactionId = ParseTransactionId(null, update.OrderId);
		if (_orderStatusSubscriptionId == 0 && transactionId == 0)
			return;
		var exchange = update.Product == BreezeProducts.Cash ? "NSE" : "NFO";
		var instrument = await _restClient.FindInstrument(exchange, update.StockCode, update.Product.ToNative(),
			update.ExpiryDate?.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
			update.OptionType == OptionTypes.Call ? "call" : update.OptionType == OptionTypes.Put ? "put" : null,
			update.StrikePrice, cancellationToken);
		var originId = transactionId == 0 ? _orderStatusSubscriptionId : transactionId;
		var state = update.Status.ToOrderState();
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originId,
			OrderStringId = update.OrderId,
			SecurityId = instrument.ToSecurityId(),
			PortfolioName = _portfolioName,
			OrderType = update.TriggerPrice > 0 ? OrderTypes.Conditional : OrderTypes.Limit,
			Side = update.Side,
			TimeInForce = update.Validity.ToTimeInForce(),
			OrderPrice = update.Price,
			OrderVolume = update.Quantity,
			Balance = Math.Max(0, update.Quantity - update.ExecutedQuantity - update.CancelledQuantity),
			AveragePrice = update.AveragePrice,
			OrderState = state,
			ServerTime = update.OrderTime ?? CurrentTime,
			Condition = new BreezeOrderCondition { Product = update.Product, TriggerPrice = update.TriggerPrice > 0 ? update.TriggerPrice : null },
			Error = state == OrderStates.Failed ? new InvalidOperationException(update.Message.IsEmpty() ? update.Status : update.Message) : null,
		}, cancellationToken);

		_orderFills.TryGetValue(update.OrderId, out var previousFill);
		if (update.ExecutedQuantity > previousFill)
		{
			var volume = update.ExecutedQuantity - previousFill;
			var tradeId = $"{update.OrderId}:{update.ExecutedQuantity.ToString(CultureInfo.InvariantCulture)}";
			if (_tradeIds.TryAdd(tradeId))
			{
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OriginalTransactionId = originId,
					OrderStringId = update.OrderId,
					TradeStringId = tradeId,
					SecurityId = instrument.ToSecurityId(),
					PortfolioName = _portfolioName,
					Side = update.Side,
					TradePrice = update.AveragePrice > 0 ? update.AveragePrice : update.Price,
					TradeVolume = volume,
					ServerTime = update.TradeTime ?? update.OrderTime ?? CurrentTime,
				}, cancellationToken);
			}
		}
		_orderFills[update.OrderId] = update.ExecutedQuantity;
	}

	private long ParseTransactionId(string remark, string orderId)
	{
		if (long.TryParse(remark, NumberStyles.Integer, CultureInfo.InvariantCulture, out var transactionId))
			_orderTransactions[orderId] = transactionId;
		else
			_orderTransactions.TryGetValue(orderId, out transactionId);
		return transactionId;
	}

	private static void ValidateProduct(BreezeInstrument instrument, BreezeProducts product)
	{
		if (product != instrument.ToProduct())
			throw new InvalidOperationException($"Breeze product '{product}' does not match {instrument.Kind} instrument '{instrument.StockCode}'.");
	}

	private static BreezeProducts ParseProduct(string value)
		=> value.EqualsIgnoreCase("futures") || value.EqualsIgnoreCase("f") ? BreezeProducts.Futures
			: value.EqualsIgnoreCase("options") || value.EqualsIgnoreCase("o") ? BreezeProducts.Options : BreezeProducts.Cash;

	private static string ToRight(BreezeInstrument instrument)
		=> instrument.Kind == BreezeInstrumentKinds.Option ? instrument.OptionType == OptionTypes.Call ? "call" : "put" : null;

	private static string FormatExpiry(DateTime? value)
		=> value?.ToString("yyyy-MM-ddT00:00:00.000Z", CultureInfo.InvariantCulture);

	private static string FormatNullable(decimal? value)
		=> value?.ToString(CultureInfo.InvariantCulture);

	private static string Format(decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);
}
