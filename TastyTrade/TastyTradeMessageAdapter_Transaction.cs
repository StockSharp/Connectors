namespace StockSharp.TastyTrade;

partial class TastyTradeMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage message, CancellationToken cancellationToken)
	{
		var condition = message.Condition as TastyTradeOrderCondition;
		var instrumentType = message.SecurityType.ToNative(message.SecurityId.SecurityCode);
		var orderType = message.OrderType.ToNative(condition?.StopPrice);
		var legs = condition?.Legs?.Length > 0
			? condition.Legs.Select(leg => new TastyOrderRequestLeg
			{
				Action = leg.Side.ToNative(leg.PositionEffect, leg.SecurityType.ToNative(leg.SecurityId.SecurityCode)),
				InstrumentType = leg.SecurityType.ToNative(leg.SecurityId.SecurityCode),
				Quantity = leg.Volume,
				Symbol = leg.SecurityId.SecurityCode,
			}).ToArray()
			:
			[
				new()
				{
					Action = message.Side.ToNative(message.PositionEffect, instrumentType),
					InstrumentType = instrumentType,
					Quantity = message.Volume,
					Symbol = message.SecurityId.SecurityCode,
				},
			];
		var order = await _client.PlaceOrder(ResolveAccount(message.PortfolioName).AccountNumber, new TastyOrderRequest
		{
			OrderType = orderType,
			TimeInForce = message.TimeInForce.ToNative(message.TillDate, condition),
			GoodTillDate = message.TillDate?.ToUniversalTime(),
			Price = orderType is TastyOrderTypes.Limit or TastyOrderTypes.StopLimit or TastyOrderTypes.MarketableLimit ? message.Price : null,
			PriceEffect = condition?.Legs?.Length > 0
				? condition.IsCredit ? TastyPriceEffects.Credit : TastyPriceEffects.Debit
				: message.Side == Sides.Buy ? TastyPriceEffects.Debit : TastyPriceEffects.Credit,
			StopTrigger = condition?.StopPrice,
			ExternalIdentifier = message.TransactionId.ToString(CultureInfo.InvariantCulture),
			IsAutomatedSource = true,
			Source = "StockSharp",
			Legs = legs,
		}, cancellationToken);

		_orderTransactions[order.Id] = message.TransactionId;
		await ProcessOrder(order, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage message, CancellationToken cancellationToken)
	{
		var orderId = message.OldOrderStringId ?? throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(message.OriginalTransactionId));
		var condition = message.Condition as TastyTradeOrderCondition;
		var orderType = message.OrderType.ToNative(condition?.StopPrice);
		var order = await _client.ReplaceOrder(ResolveAccount(message.PortfolioName).AccountNumber, orderId, new TastyOrderReplaceRequest
		{
			OrderType = orderType,
			TimeInForce = message.TimeInForce.ToNative(message.TillDate, condition),
			GoodTillDate = message.TillDate?.ToUniversalTime(),
			Price = orderType is TastyOrderTypes.Limit or TastyOrderTypes.StopLimit or TastyOrderTypes.MarketableLimit ? message.Price : null,
			PriceEffect = message.Side == Sides.Buy ? TastyPriceEffects.Debit : TastyPriceEffects.Credit,
			StopTrigger = condition?.StopPrice,
			ExternalIdentifier = message.TransactionId.ToString(CultureInfo.InvariantCulture),
			IsAutomatedSource = true,
			Source = "StockSharp",
		}, cancellationToken);
		_orderTransactions[order.Id] = message.TransactionId;
		await ProcessOrder(order, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(OrderCancelMessage message, CancellationToken cancellationToken)
	{
		var orderId = message.OrderStringId ?? throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(message.OriginalTransactionId));
		return _client.CancelOrder(ResolveAccount(message.PortfolioName).AccountNumber, orderId, cancellationToken).AsValueTask();
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
			return;

		foreach (var account in _accounts)
		{
			await SendOutMessageAsync(new PortfolioMessage
			{
				OriginalTransactionId = message.TransactionId,
				PortfolioName = account.AccountNumber,
				BoardCode = "TASTYTRADE",
			}, cancellationToken);
			await ProcessBalance(await _client.GetBalance(account.AccountNumber, cancellationToken), cancellationToken, message.TransactionId);
			foreach (var position in await _client.GetPositions(account.AccountNumber, cancellationToken))
				await ProcessPosition(position, cancellationToken, message.TransactionId);
		}
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
			return;

		var accounts = message.PortfolioName.IsEmpty() ? _accounts : [ResolveAccount(message.PortfolioName)];
		foreach (var account in accounts)
			foreach (var order in await _client.GetOrders(account.AccountNumber, cancellationToken))
				await ProcessOrder(order, cancellationToken, message.TransactionId);
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private TastyAccount ResolveAccount(string portfolioName)
	{
		if (!portfolioName.IsEmpty())
			return _accounts.FirstOrDefault(a => a.AccountNumber.EqualsIgnoreCase(portfolioName))
				?? throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
		return _accounts.FirstOrDefault() ?? throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
	}

	private ValueTask ProcessBalance(TastyBalance balance, CancellationToken cancellationToken)
		=> ProcessBalance(balance, cancellationToken, 0);

	private ValueTask ProcessBalance(TastyBalance balance, CancellationToken cancellationToken, long originalTransactionId)
	{
		if (balance is null)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = balance.AccountNumber,
			SecurityId = SecurityId.Money,
			ServerTime = balance.UpdatedAt?.ToUniversalTime() ?? DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, balance.CashBalance, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, balance.NetLiquidatingValue, true)
		.TryAdd(PositionChangeTypes.BeginValue, balance.CashAvailableToWithdraw)
		.TryAdd(PositionChangeTypes.BuyOrdersMargin, balance.EquityBuyingPower ?? balance.DerivativeBuyingPower ?? balance.DayTradingBuyingPower)
		.TryAdd(PositionChangeTypes.BlockedValue, balance.MaintenanceRequirement), cancellationToken);
	}

	private ValueTask ProcessPosition(TastyPosition position, CancellationToken cancellationToken)
		=> ProcessPosition(position, cancellationToken, 0);

	private ValueTask ProcessPosition(TastyPosition position, CancellationToken cancellationToken, long originalTransactionId)
	{
		if (position is null || position.Symbol.IsEmpty())
			return default;
		var quantity = position.QuantityDirection == TastyQuantityDirections.Short ? -position.Quantity.Abs() : position.Quantity;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = position.AccountNumber,
			SecurityId = new() { SecurityCode = position.Symbol, BoardCode = "TASTYTRADE" },
			ServerTime = position.UpdatedAt?.ToUniversalTime() ?? DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, quantity, true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.AverageOpenPrice, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, position.ClosePrice)
		.TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedToday ?? position.RealizedDayGain), cancellationToken);
	}

	private ValueTask ProcessOrder(TastyOrder order, CancellationToken cancellationToken)
		=> ProcessOrder(order, cancellationToken, 0);

	private async ValueTask ProcessOrder(TastyOrder order, CancellationToken cancellationToken, long originalTransactionId)
	{
		if (order is null || order.Id.IsEmpty())
			return;
		var leg = order.Legs?.FirstOrDefault();
		if (leg is null)
			return;
		var transactionId = _orderTransactions.TryGetValue2(order.Id)
			?? (long.TryParse(order.ExternalIdentifier, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0);
		var serverTime = (order.UpdatedAt ?? order.TerminalAt ?? order.LiveAt ?? order.ReceivedAt)?.ToUniversalTime() ?? DateTime.UtcNow;
		var condition = order.StopTrigger is decimal stopPrice ? new TastyTradeOrderCondition { StopPrice = stopPrice } : null;
		if (condition is not null)
		{
			condition.IsExtendedHours = order.TimeInForce is TastyTimeInForces.Extended or TastyTimeInForces.GoodTillCancelledExtended;
			condition.IsOvernight = order.TimeInForce is TastyTimeInForces.ExtendedOvernight or TastyTimeInForces.GoodTillCancelledExtendedOvernight;
		}

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId == 0 ? transactionId : originalTransactionId,
			TransactionId = originalTransactionId == 0 ? 0 : transactionId,
			OrderStringId = order.Id,
			PortfolioName = order.AccountNumber,
			SecurityId = new() { SecurityCode = leg.Symbol, BoardCode = "TASTYTRADE" },
			Side = leg.Action.ToSide(),
			OrderType = order.OrderType.ToOrderType(),
			OrderPrice = order.Price ?? 0,
			OrderVolume = leg.Quantity,
			Balance = leg.RemainingQuantity,
			OrderState = order.Status.ToOrderState(),
			TimeInForce = order.TimeInForce.ToTimeInForce(),
			ExpiryDate = order.GoodTillDate,
			ServerTime = serverTime,
			Condition = condition,
			Error = order.Status == TastyOrderStatuses.Rejected ? new InvalidOperationException(order.RejectReason.IsEmpty("Order rejected by tastytrade.")) : null,
		}, cancellationToken);

		foreach (var orderLeg in order.Legs ?? [])
		{
			foreach (var fill in orderLeg.Fills ?? [])
			{
				var fillKey = $"{order.Id}:{fill.FillId}";
				if (_processedFills.Contains(fillKey))
					continue;
				_processedFills.Add(fillKey);
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OriginalTransactionId = originalTransactionId == 0 ? transactionId : originalTransactionId,
					OrderStringId = order.Id,
					TradeStringId = fill.FillId,
					PortfolioName = order.AccountNumber,
					SecurityId = new() { SecurityCode = orderLeg.Symbol, BoardCode = "TASTYTRADE" },
					Side = orderLeg.Action.ToSide(),
					TradePrice = fill.FillPrice,
					TradeVolume = fill.Quantity,
					ServerTime = fill.FilledAt.ToUniversalTime(),
				}, cancellationToken);
			}
		}
	}
}
