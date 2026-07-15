namespace StockSharp.Robinhood;

partial class RobinhoodMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage message, CancellationToken cancellationToken)
	{
		var stopPrice = (message.Condition as RobinhoodOrderCondition)?.StopPrice;
		var type = ToNativeOrderType(message.OrderType ?? OrderTypes.Limit, stopPrice);
		var request = new RobinhoodOrderRequest
		{
			AccountNumber = ResolveAccount(message.PortfolioName).AccountNumber,
			Symbol = message.SecurityId.SecurityCode,
			Quantity = message.Volume.ToString(CultureInfo.InvariantCulture),
			Side = message.Side.ToNative(),
			Type = type,
			LimitPrice = type is RobinhoodOrderType.Limit or RobinhoodOrderType.StopLimit ? message.Price.ToString(CultureInfo.InvariantCulture) : null,
			StopPrice = stopPrice?.ToString(CultureInfo.InvariantCulture),
			TimeInForce = message.TillDate is null ? RobinhoodTimeInForce.GoodTillCanceled : RobinhoodTimeInForce.GoodForDay,
		};

		var review = await _client.ReviewOrder(request, cancellationToken);
		if (review is not null && !review.MarketDataDisclosure.IsEmpty())
			this.AddInfoLog("Robinhood order review: {0}", review.MarketDataDisclosure);
		var orderId = await _client.PlaceOrder(request, cancellationToken);
		_orderTransactions[orderId] = message.TransactionId;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = message.TransactionId,
			OrderStringId = orderId,
			PortfolioName = request.AccountNumber,
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
		{
			if (_portfolioSubscriptionId == message.OriginalTransactionId)
				_portfolioSubscriptionId = 0;
			return;
		}

		if (_accounts.Length == 0)
			_accounts = await _client.GetAccounts(cancellationToken) ?? [];

		foreach (var account in _accounts)
		{
			await ProcessPortfolio(account, message.TransactionId, cancellationToken);
			foreach (var position in await _client.GetPositions(account.AccountNumber, cancellationToken) ?? [])
				await ProcessPosition(account.AccountNumber, position, message.TransactionId, cancellationToken);
		}

		if (!message.IsHistoryOnly())
			_portfolioSubscriptionId = message.TransactionId;
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			if (_orderSubscriptionId == message.OriginalTransactionId)
				_orderSubscriptionId = 0;
			return;
		}

		var accounts = message.PortfolioName.IsEmpty()
			? _accounts
			: [ResolveAccount(message.PortfolioName)];
		foreach (var account in accounts)
		{
			foreach (var order in await _client.GetOrders(account.AccountNumber, cancellationToken) ?? [])
				await ProcessOrder(account.AccountNumber, order, message.TransactionId, cancellationToken);
		}

		if (!message.IsHistoryOnly())
			_orderSubscriptionId = message.TransactionId;
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private RobinhoodAccount ResolveAccount(string portfolioName)
	{
		if (!portfolioName.IsEmpty())
			return _accounts.FirstOrDefault(a => a.AccountNumber.EqualsIgnoreCase(portfolioName) || a.Id.EqualsIgnoreCase(portfolioName))
				?? throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
		return _accounts.FirstOrDefault(a => a.IsAgenticAllowed)
			?? throw new InvalidOperationException("Robinhood Agentic account was not found.");
	}

	private async ValueTask ProcessPortfolio(RobinhoodAccount account, long originalTransactionId, CancellationToken cancellationToken)
	{
		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = account.AccountNumber,
			BoardCode = BoardCodes.Nasdaq,
			Currency = CurrencyTypes.USD,
		}, cancellationToken);

		var portfolio = await _client.GetPortfolio(account.AccountNumber, cancellationToken);
		if (portfolio is null)
			return;

		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = account.AccountNumber,
			SecurityId = SecurityId.Money,
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, portfolio.Cash ?? portfolio.CashValue, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, portfolio.PortfolioValue ?? portfolio.TotalValue ?? portfolio.TotalEquity, true)
		.TryAdd(PositionChangeTypes.BuyOrdersMargin, portfolio.BuyingPower?.Value, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, portfolio.TotalReturn), cancellationToken);
	}

	private ValueTask ProcessPosition(string accountNumber, RobinhoodPosition position, long originalTransactionId, CancellationToken cancellationToken)
	{
		var symbol = position.Symbol.IsEmpty(position.Ticker);
		if (symbol.IsEmpty())
			return default;

		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = accountNumber,
			SecurityId = symbol.ToSecurityId(),
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, position.Quantity, true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.AverageBuyPrice, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, position.Value), cancellationToken);
	}

	private async ValueTask ProcessOrder(string accountNumber, RobinhoodOrder order, long originalTransactionId, CancellationToken cancellationToken)
	{
		var orderId = order.OrderId.IsEmpty(order.Id);
		if (orderId.IsEmpty() || order.Symbol.IsEmpty())
			return;

		var transactionId = _orderTransactions.TryGetValue2(orderId) ?? 0;
		var orderState = order.State ?? order.Status ?? RobinhoodOrderState.Confirmed;
		var filled = order.FilledQuantity ?? (orderState == RobinhoodOrderState.Filled ? order.Quantity : 0);
		var balance = (order.Quantity - filled).Max(0);
		var serverTime = order.UpdatedAt.IsEmpty(order.CreatedAt).ToUtcDateTime(DateTime.UtcNow);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId == 0 ? transactionId : originalTransactionId,
			TransactionId = originalTransactionId == 0 ? 0 : transactionId,
			OrderStringId = orderId,
			PortfolioName = accountNumber,
			SecurityId = order.Symbol.ToSecurityId(),
			Side = order.Side.ToSide(),
			OrderType = (order.Type ?? RobinhoodOrderType.Market).ToOrderType(),
			OrderPrice = order.Price ?? 0,
			OrderVolume = order.Quantity,
			Balance = balance,
			AveragePrice = order.AverageFillPrice,
			OrderState = orderState.ToOrderState(),
			TimeInForce = (order.TimeInForce ?? RobinhoodTimeInForce.GoodForDay).ToTimeInForce(),
			ServerTime = serverTime,
			Error = orderState is RobinhoodOrderState.Rejected or RobinhoodOrderState.Failed
				? new InvalidOperationException($"Robinhood order {orderId} was {orderState}.")
				: null,
		}, cancellationToken);

		var previous = _executedQuantities.TryGetValue2(orderId) ?? 0;
		if (filled <= previous)
			return;

		var delta = filled - previous;
		_executedQuantities[orderId] = filled;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originalTransactionId == 0 ? transactionId : originalTransactionId,
			OrderStringId = orderId,
			TradeStringId = $"{orderId}:{filled.ToString(CultureInfo.InvariantCulture)}",
			PortfolioName = accountNumber,
			SecurityId = order.Symbol.ToSecurityId(),
			Side = order.Side.ToSide(),
			TradePrice = order.AverageFillPrice,
			TradeVolume = delta,
			ServerTime = serverTime,
		}, cancellationToken);
	}

	private static RobinhoodOrderType ToNativeOrderType(OrderTypes type, decimal? stopPrice)
		=> type switch
		{
			OrderTypes.Market when stopPrice is not null => RobinhoodOrderType.StopMarket,
			OrderTypes.Limit when stopPrice is not null => RobinhoodOrderType.StopLimit,
			OrderTypes.Market => RobinhoodOrderType.Market,
			OrderTypes.Limit => RobinhoodOrderType.Limit,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};
}
