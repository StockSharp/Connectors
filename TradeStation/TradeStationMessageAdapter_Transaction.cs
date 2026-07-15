namespace StockSharp.TradeStation;

partial class TradeStationMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage message, CancellationToken cancellationToken)
	{
		var stopPrice = (message.Condition as TradeStationOrderCondition)?.StopPrice;
		var orderType = (message.OrderType ?? OrderTypes.Limit).ToNative(stopPrice);
		var orderId = await _client.PlaceOrder(new TradeStationOrderRequest
		{
			AccountId = ResolveAccount(message.PortfolioName).AccountId,
			OrderConfirmId = message.TransactionId.ToString(CultureInfo.InvariantCulture),
			Symbol = message.SecurityId.SecurityCode,
			Quantity = message.Volume,
			OrderType = orderType,
			TradeAction = message.Side.ToNative(message.PositionEffect),
			LimitPrice = orderType is TradeStationOrderType.Limit or TradeStationOrderType.StopLimit ? message.Price : null,
			StopPrice = stopPrice,
			TimeInForce = new()
			{
				Duration = (message.TimeInForce ?? TimeInForce.PutInQueue).ToNative(message.TillDate),
				Expiration = message.TillDate?.ToUniversalTime(),
			},
			Route = DefaultRoute.IsEmpty("Intelligent"),
		}, cancellationToken);

		_orderTransactions[orderId] = message.TransactionId;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = message.TransactionId,
			OrderStringId = orderId,
			PortfolioName = message.PortfolioName,
			SecurityId = message.SecurityId,
			Side = message.Side,
			OrderType = message.OrderType,
			OrderPrice = message.Price,
			OrderVolume = message.Volume,
			Balance = message.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = DateTime.UtcNow,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage message, CancellationToken cancellationToken)
	{
		var orderId = message.OldOrderStringId ?? throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(message.OriginalTransactionId));
		var stopPrice = (message.Condition as TradeStationOrderCondition)?.StopPrice;
		var orderType = (message.OrderType ?? OrderTypes.Limit).ToNative(stopPrice);
		await _client.ReplaceOrder(orderId, new TradeStationOrderReplaceRequest
		{
			Quantity = message.Volume,
			OrderType = orderType,
			LimitPrice = orderType is TradeStationOrderType.Limit or TradeStationOrderType.StopLimit ? message.Price : null,
			StopPrice = stopPrice,
		}, cancellationToken);
		_orderTransactions[orderId] = message.TransactionId;
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(OrderCancelMessage message, CancellationToken cancellationToken)
	{
		var orderId = message.OrderStringId ?? throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(message.OriginalTransactionId));
		return _client.CancelOrder(orderId, cancellationToken).AsValueTask();
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
			return;

		if (_accounts.Length == 0)
			_accounts = (await _client.GetAccounts(cancellationToken))?.Accounts ?? [];

		var accountIds = _accounts.Select(a => a.AccountId).ToArray();
		var balances = accountIds.Length == 0 ? null : await _client.GetBalances(accountIds, cancellationToken);
		var positions = accountIds.Length == 0 ? null : await _client.GetPositions(accountIds, cancellationToken);

		foreach (var account in _accounts)
		{
			await SendOutMessageAsync(new PortfolioMessage
			{
				OriginalTransactionId = message.TransactionId,
				PortfolioName = account.AccountId,
				BoardCode = BoardCodes.Nasdaq,
				Currency = account.Currency.To<CurrencyTypes?>(),
			}, cancellationToken);

			if (balances?.Balances?.FirstOrDefault(b => b.AccountId.EqualsIgnoreCase(account.AccountId)) is { } balance)
				await ProcessBalance(balance, message.TransactionId, cancellationToken);
		}

		foreach (var position in positions?.Positions ?? [])
			await ProcessPosition(position, cancellationToken, message.TransactionId);

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
			return;

		var accountIds = message.PortfolioName.IsEmpty()
			? _accounts.Select(a => a.AccountId).ToArray()
			: [ResolveAccount(message.PortfolioName).AccountId];
		if (accountIds.Length > 0)
		{
			foreach (var order in (await _client.GetOrders(accountIds, cancellationToken))?.Orders ?? [])
				await ProcessOrder(order, cancellationToken, message.TransactionId);
		}

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private TradeStationAccount ResolveAccount(string portfolioName)
	{
		if (!portfolioName.IsEmpty())
			return _accounts.FirstOrDefault(a => a.AccountId.EqualsIgnoreCase(portfolioName))
				?? throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
		return _accounts.FirstOrDefault() ?? throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
	}

	private ValueTask ProcessBalance(TradeStationBalance balance, long originalTransactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = balance.AccountId,
			SecurityId = SecurityId.Money,
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, balance.CashBalance, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, balance.Equity, true)
		.TryAdd(PositionChangeTypes.BuyOrdersMargin, balance.BuyingPower, true)
		.TryAdd(PositionChangeTypes.RealizedPnL, balance.BalanceDetail?.RealizedProfitLoss)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, balance.BalanceDetail?.UnrealizedProfitLoss), cancellationToken);

	private ValueTask ProcessPosition(TradeStationPosition position, CancellationToken cancellationToken)
		=> ProcessPosition(position, cancellationToken, 0);

	private ValueTask ProcessPosition(TradeStationPosition position, CancellationToken cancellationToken, long originalTransactionId)
	{
		if (position is null)
			return default;
		if (!position.Error.IsEmpty())
			return SendOutErrorAsync(new InvalidOperationException(position.Message.IsEmpty(position.Error)), cancellationToken);
		if (position.PositionId.IsEmpty())
			return default;
		var quantity = position.IsDeleted ? 0 : position.LongShort == TradeStationPositionDirection.Short ? -position.Quantity.Abs() : position.Quantity;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = position.AccountId,
			SecurityId = new() { SecurityCode = position.Symbol, BoardCode = BoardCodes.Nasdaq },
			ServerTime = position.Timestamp == default ? DateTime.UtcNow : position.Timestamp.ToUtc(),
		}
		.TryAdd(PositionChangeTypes.CurrentValue, quantity, true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedProfitLoss)
		.TryAdd(PositionChangeTypes.VariationMargin, position.TodaysProfitLoss), cancellationToken);
	}

	private ValueTask ProcessOrder(TradeStationOrder order, CancellationToken cancellationToken)
		=> ProcessOrder(order, cancellationToken, 0);

	private async ValueTask ProcessOrder(TradeStationOrder order, CancellationToken cancellationToken, long originalTransactionId)
	{
		if (order is null)
			return;
		if (!order.Error.IsEmpty())
		{
			await SendOutErrorAsync(new InvalidOperationException(order.Message.IsEmpty(order.Error)), cancellationToken);
			return;
		}
		if (order.OrderId.IsEmpty())
			return;

		var leg = order.Legs?.FirstOrDefault();
		if (leg is null)
			return;
		var transactionId = _orderTransactions.TryGetValue2(order.OrderId) ?? 0;
		var serverTime = (order.ClosedDateTime ?? order.OpenedDateTime)?.ToUtc() ?? DateTime.UtcNow;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId == 0 ? transactionId : originalTransactionId,
			TransactionId = originalTransactionId == 0 ? 0 : transactionId,
			OrderStringId = order.OrderId,
			PortfolioName = order.AccountId,
			SecurityId = new() { SecurityCode = leg.Symbol, BoardCode = BoardCodes.Nasdaq },
			Side = leg.BuyOrSell.ToSide(),
			OrderType = order.OrderType.ToOrderType(),
			OrderPrice = order.LimitPrice ?? 0,
			OrderVolume = leg.QuantityOrdered,
			Balance = leg.QuantityRemaining,
			OrderState = order.Status.ToOrderState(),
			ServerTime = serverTime,
			Condition = order.StopPrice is decimal stopPrice ? new TradeStationOrderCondition { StopPrice = stopPrice } : null,
			Error = order.Status == TradeStationOrderStatus.Rejected ? new InvalidOperationException(order.RejectReason.IsEmpty(order.StatusDescription)) : null,
		}, cancellationToken);

		var previous = _executedQuantities.TryGetValue2(order.OrderId) ?? 0;
		if (leg.ExecQuantity > previous)
		{
			var delta = leg.ExecQuantity - previous;
			_executedQuantities[order.OrderId] = leg.ExecQuantity;
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = originalTransactionId == 0 ? transactionId : originalTransactionId,
				OrderStringId = order.OrderId,
				TradeStringId = $"{order.OrderId}:{leg.ExecQuantity.ToString(CultureInfo.InvariantCulture)}",
				PortfolioName = order.AccountId,
				SecurityId = new() { SecurityCode = leg.Symbol, BoardCode = BoardCodes.Nasdaq },
				Side = leg.BuyOrSell.ToSide(),
				TradePrice = leg.ExecutionPrice ?? order.FilledPrice,
				TradeVolume = delta,
				ServerTime = serverTime,
			}, cancellationToken);
		}
	}
}
