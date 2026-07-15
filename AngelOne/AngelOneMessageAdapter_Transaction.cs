namespace StockSharp.AngelOne;

public partial class AngelOneMessageAdapter
{
	private readonly SynchronizedDictionary<string, long> _orderTransactions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, decimal> _orderFills = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, AngelOneOrderVarieties> _orderVarieties = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _tradeIds = new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private string _portfolioName;

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var (_, token) = regMsg.SecurityId.ToInstrumentKey().ParseInstrumentKey();
		var condition = regMsg.Condition as AngelOneOrderCondition;
		var triggerPrice = condition?.TriggerPrice;
		var product = condition?.Product ?? DefaultProduct;
		var variety = condition?.Variety ?? (product == AngelOneProducts.Bracket
			? AngelOneOrderVarieties.Robo
			: triggerPrice is > 0 ? AngelOneOrderVarieties.StopLoss : AngelOneOrderVarieties.Normal);

		var result = await _restClient.PlaceOrder(new AngelOneOrderRequest
		{
			Variety = variety.ToNative(),
			TradingSymbol = regMsg.SecurityId.SecurityCode,
			SymbolToken = token,
			TransactionType = regMsg.Side.ToNative(),
			Exchange = regMsg.SecurityId.BoardCode,
			OrderType = (regMsg.OrderType ?? OrderTypes.Limit).ToNative(triggerPrice),
			ProductType = product.ToNative(),
			Duration = regMsg.TimeInForce.ToNative(),
			Price = regMsg.OrderType == OrderTypes.Market ? 0 : regMsg.Price,
			TriggerPrice = triggerPrice ?? 0,
			Quantity = regMsg.Volume.To<long>(),
			DisclosedQuantity = condition?.DisclosedVolume?.To<long>() ?? 0,
			SquareOff = condition?.SquareOff ?? 0,
			StopLoss = condition?.StopLoss ?? 0,
			TrailingStopLoss = condition?.TrailingStopLoss ?? 0,
			OrderTag = regMsg.TransactionId.ToString(CultureInfo.InvariantCulture),
			ScripConsent = condition?.ScripConsent == true ? "yes" : null,
		}, cancellationToken);

		var orderId = result?.OrderId;
		if (orderId.IsEmpty())
			throw new InvalidOperationException("Angel One did not return an order identifier.");
		_orderTransactions[orderId] = regMsg.TransactionId;
		_orderFills[orderId] = 0;
		_orderVarieties[orderId] = variety;

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

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		var orderId = replaceMsg.OldOrderStringId;
		if (orderId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(replaceMsg.OriginalTransactionId));

		var (_, token) = replaceMsg.SecurityId.ToInstrumentKey().ParseInstrumentKey();
		var condition = replaceMsg.Condition as AngelOneOrderCondition;
		var triggerPrice = condition?.TriggerPrice;
		var product = condition?.Product ?? DefaultProduct;
		var variety = condition?.Variety ?? _orderVarieties.TryGetValue2(orderId) ?? AngelOneOrderVarieties.Normal;

		await _restClient.ModifyOrder(new AngelOneOrderRequest
		{
			Variety = variety.ToNative(),
			OrderId = orderId,
			TradingSymbol = replaceMsg.SecurityId.SecurityCode,
			SymbolToken = token,
			Exchange = replaceMsg.SecurityId.BoardCode,
			OrderType = (replaceMsg.OrderType ?? OrderTypes.Limit).ToNative(triggerPrice),
			ProductType = product.ToNative(),
			Duration = replaceMsg.TimeInForce.ToNative(),
			Price = replaceMsg.OrderType == OrderTypes.Market ? 0 : replaceMsg.Price,
			TriggerPrice = triggerPrice ?? 0,
			Quantity = replaceMsg.Volume.To<long>(),
			DisclosedQuantity = condition?.DisclosedVolume?.To<long>() ?? 0,
			SquareOff = condition?.SquareOff ?? 0,
			StopLoss = condition?.StopLoss ?? 0,
			TrailingStopLoss = condition?.TrailingStopLoss ?? 0,
			OrderTag = replaceMsg.TransactionId.ToString(CultureInfo.InvariantCulture),
			ScripConsent = condition?.ScripConsent == true ? "yes" : null,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var orderId = cancelMsg.OrderStringId;
		if (orderId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		await _restClient.CancelOrder(orderId, _orderVarieties.TryGetValue2(orderId) ?? AngelOneOrderVarieties.Normal, cancellationToken);
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
			_portfolioName = (await _restClient.GetProfile(cancellationToken))?.ClientCode ?? Login;

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
		_portfolioName = profile.ClientCode ?? Login;

		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = lookupMsg.TransactionId,
			PortfolioName = _portfolioName,
			BoardCode = "NSE",
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
			var blocked = (funds.UtilizedDebits.ToDecimal() ?? 0) +
				(funds.UtilizedSpan.ToDecimal() ?? 0) +
				(funds.UtilizedOptionPremium.ToDecimal() ?? 0) +
				(funds.UtilizedHoldingSales.ToDecimal() ?? 0) +
				(funds.UtilizedExposure.ToDecimal() ?? 0) +
				(funds.UtilizedTurnover.ToDecimal() ?? 0) +
				(funds.UtilizedPayout.ToDecimal() ?? 0);

			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = SecurityId.Money,
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.BeginValue, funds.Net.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.CurrentValue, funds.AvailableCash.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.BlockedValue, blocked, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, funds.UnrealizedPnL.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.RealizedPnL, funds.RealizedPnL.ToDecimal(), true), cancellationToken);
		}

		foreach (var position in await _restClient.GetPositions(cancellationToken))
			await SendPosition(position, cancellationToken);

		foreach (var holding in await _restClient.GetHoldings(cancellationToken))
			await SendHolding(holding, cancellationToken);
	}

	private ValueTask SendPosition(AngelOnePosition position, CancellationToken cancellationToken)
	{
		if (position == null || position.Exchange.IsEmpty())
			return default;

		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = position.Exchange.CreateSecurityId(position.SymbolToken, position.TradingSymbol),
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, position.NetQuantity.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.AverageNetPrice.ToDecimal() ?? position.NetPrice.ToDecimal(), true), cancellationToken);
	}

	private ValueTask SendHolding(AngelOneHolding holding, CancellationToken cancellationToken)
	{
		if (holding == null || holding.Exchange.IsEmpty())
			return default;

		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = holding.Exchange.CreateSecurityId(holding.SymbolToken, holding.TradingSymbol),
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, holding.Quantity + holding.T1Quantity, true)
		.TryAdd(PositionChangeTypes.AveragePrice, holding.AveragePrice, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, holding.LastPrice, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, holding.ProfitAndLoss, true), cancellationToken);
	}

	private async ValueTask ProcessOrder(AngelOneOrder order, long originId, bool isLookup, CancellationToken cancellationToken)
	{
		if (order == null || order.OrderId.IsEmpty() || order.Exchange.IsEmpty())
			return;

		var transactionId = 0L;
		if (!long.TryParse(order.OrderTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out transactionId))
			_orderTransactions.TryGetValue(order.OrderId, out transactionId);
		else
			_orderTransactions[order.OrderId] = transactionId;

		var variety = order.Variety.ToVariety();
		_orderVarieties[order.OrderId] = variety;
		var state = (order.OrderStatus ?? order.Status).ToOrderState();
		var filled = order.FilledShares.ToDecimal() ?? 0;
		var quantity = order.Quantity.ToDecimal() ?? 0;
		var triggerPrice = order.TriggerPrice.ToDecimal();
		var serverTime = order.ExchangeUpdateTime.ToAngelTime() ?? order.ExchangeTime.ToAngelTime() ?? order.UpdateTime.ToAngelTime() ?? CurrentTime;
		var securityId = order.Exchange.CreateSecurityId(order.SymbolToken, order.TradingSymbol);

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
			TimeInForce = order.Duration.ToTimeInForce(),
			OrderPrice = order.Price.ToDecimal() ?? 0,
			OrderVolume = quantity,
			Balance = order.UnfilledShares.ToDecimal() ?? Math.Max(0, quantity - filled),
			AveragePrice = order.AveragePrice.ToDecimal(),
			OrderState = state,
			ServerTime = serverTime,
			Condition = new AngelOneOrderCondition
			{
				Product = order.ProductType.ToProduct(),
				Variety = variety,
				TriggerPrice = triggerPrice > 0 ? triggerPrice : null,
				DisclosedVolume = order.DisclosedQuantity.ToDecimal(),
			},
			Error = state == OrderStates.Failed ? new InvalidOperationException(order.Text ?? order.Status) : null,
		}, cancellationToken);

		if (isLookup)
		{
			_orderFills[order.OrderId] = filled;
			return;
		}

		if (filled <= 0)
			return;

		var previousFilled = _orderFills.TryGetValue2(order.OrderId);
		if (filled <= previousFilled)
			return;

		_orderFills[order.OrderId] = filled;
		var tradeId = order.FillId.IsEmpty() ? $"{order.OrderId}:{filled.ToString(CultureInfo.InvariantCulture)}" : order.FillId;
		if (!_tradeIds.TryAdd(tradeId))
			return;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = transactionId,
			OrderStringId = order.OrderId,
			TradeStringId = tradeId,
			SecurityId = securityId,
			PortfolioName = _portfolioName,
			Side = order.TransactionType.ToSide(),
			TradePrice = order.AveragePrice.ToDecimal(),
			TradeVolume = filled - previousFilled,
			ServerTime = order.FillTime.ToAngelTime(serverTime) ?? serverTime,
		}, cancellationToken);
	}

	private ValueTask ProcessTrade(AngelOneTrade trade, long originId, CancellationToken cancellationToken)
	{
		if (trade == null || trade.Exchange.IsEmpty() || trade.FillId.IsEmpty() || !_tradeIds.TryAdd(trade.FillId))
			return default;

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originId,
			OrderStringId = trade.OrderId,
			TradeStringId = trade.FillId,
			SecurityId = trade.Exchange.CreateSecurityId(trade.SymbolToken, trade.TradingSymbol),
			PortfolioName = _portfolioName,
			Side = trade.TransactionType.ToSide(),
			TradePrice = trade.FillPrice.ToDecimal(),
			TradeVolume = trade.FillSize.ToDecimal(),
			ServerTime = trade.FillTime.ToAngelTime(CurrentTime) ?? CurrentTime,
		}, cancellationToken);
	}

	private ValueTask OnOrderReceived(AngelOneOrder order, CancellationToken cancellationToken)
		=> _orderStatusSubscriptionId == 0
			? default
			: ProcessOrder(order, _orderStatusSubscriptionId, false, cancellationToken);
}
