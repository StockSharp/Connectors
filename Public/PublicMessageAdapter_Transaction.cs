namespace StockSharp.Public;

partial class PublicMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage message, CancellationToken cancellationToken)
	{
		var account = ResolveAccount(message.PortfolioName);
		var condition = message.Condition as PublicOrderCondition;
		var orderType = message.OrderType.ToNative(condition?.StopPrice);
		var expiration = CreateExpiration(message.TillDate);
		var requestId = Guid.NewGuid().ToString();
		string orderId;

		if (condition?.Legs?.Length > 0)
		{
			if (orderType != PublicOrderTypes.Limit)
				throw new InvalidOperationException("Public.com multi-leg orders must be limit orders.");
			if (message.Volume != decimal.Truncate(message.Volume) || message.Volume <= 0)
				throw new InvalidOperationException("Public.com multi-leg order quantity must be a positive integer.");
			if (condition.Legs.Length is < 2 or > 6)
				throw new InvalidOperationException("Public.com multi-leg orders require from 2 to 6 legs.");
			if (condition.Legs.Count(leg => leg.SecurityType == SecurityTypes.Stock) > 1)
				throw new InvalidOperationException("Public.com multi-leg orders allow at most one equity leg.");
			if (condition.Legs.Any(leg => leg.SecurityType is not SecurityTypes.Stock and not SecurityTypes.Option))
				throw new InvalidOperationException("Public.com multi-leg orders support equity and option legs only.");
			if (condition.Legs.Any(leg => leg.RatioQuantity <= 0))
				throw new InvalidOperationException("Public.com multi-leg ratio quantities must be positive.");
			if (condition.Legs.Any(leg => leg.SecurityType == SecurityTypes.Option && leg.OpenCloseIndicator is null))
				throw new InvalidOperationException("Public.com option legs require an open or close indicator.");

			orderId = await _client.PlaceMultiLegOrder(account.AccountId, new()
			{
				OrderId = requestId,
				Quantity = Format(message.Volume),
				Type = PublicOrderTypes.Limit,
				LimitPrice = FormatPrice(message.Price),
				Expiration = expiration,
				IsUseMargin = condition.IsMarginEnabled,
				Legs = condition.Legs.Select(leg => new PublicOrderLegRequest
				{
					Instrument = new() { Symbol = leg.SecurityId.SecurityCode, Type = leg.SecurityType.ToNative(leg.SecurityId.SecurityCode) },
					Side = leg.Side.ToNative(),
					OpenCloseIndicator = leg.OpenCloseIndicator,
					RatioQuantity = leg.RatioQuantity,
				}).ToArray(),
			}, cancellationToken);
		}
		else
		{
			orderId = await _client.PlaceOrder(account.AccountId, new()
			{
				OrderId = requestId,
				Instrument = new() { Symbol = message.SecurityId.SecurityCode, Type = message.SecurityType.ToNative(message.SecurityId.SecurityCode) },
				Side = message.Side.ToNative(),
				Type = orderType,
				Expiration = expiration,
				Quantity = Format(message.Volume),
				LimitPrice = orderType is PublicOrderTypes.Limit or PublicOrderTypes.StopLimit ? FormatPrice(message.Price) : null,
				StopPrice = condition?.StopPrice is decimal stopPrice ? FormatPrice(stopPrice) : null,
				OpenCloseIndicator = condition?.OpenCloseIndicator ?? ToOpenClose(message.PositionEffect),
				EquityMarketSession = condition?.MarketSession,
				IsUseMargin = condition?.IsMarginEnabled,
			}, cancellationToken);
		}

		_orderTransactions[orderId] = message.TransactionId;
		_trackedOrders[orderId] = account.AccountId;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = message.TransactionId,
			OrderStringId = orderId,
			PortfolioName = account.AccountId,
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
		var oldOrderId = message.OldOrderStringId ?? throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(message.OriginalTransactionId));
		var account = ResolveAccount(message.PortfolioName);
		var condition = message.Condition as PublicOrderCondition;
		var orderType = message.OrderType.ToNative(condition?.StopPrice);
		var orderId = await _client.ReplaceOrder(account.AccountId, new()
		{
			OrderId = oldOrderId,
			RequestId = Guid.NewGuid().ToString(),
			Type = orderType,
			Expiration = CreateExpiration(message.TillDate),
			Quantity = Format(message.Volume),
			LimitPrice = orderType is PublicOrderTypes.Limit or PublicOrderTypes.StopLimit ? FormatPrice(message.Price) : null,
			StopPrice = condition?.StopPrice is decimal stopPrice ? FormatPrice(stopPrice) : null,
		}, cancellationToken);

		_orderTransactions[orderId] = message.TransactionId;
		_trackedOrders.Remove(oldOrderId);
		_trackedOrders[orderId] = account.AccountId;
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(OrderCancelMessage message, CancellationToken cancellationToken)
	{
		var orderId = message.OrderStringId ?? throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(message.OriginalTransactionId));
		return _client.CancelOrder(ResolveAccount(message.PortfolioName).AccountId, orderId, cancellationToken).AsValueTask();
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

		foreach (var account in _accounts)
			await ProcessPortfolio(await _client.GetPortfolio(account.AccountId, cancellationToken), message.TransactionId, cancellationToken);

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

		var accounts = message.PortfolioName.IsEmpty() ? _accounts : [ResolveAccount(message.PortfolioName)];
		foreach (var account in accounts)
		{
			var portfolio = await _client.GetPortfolio(account.AccountId, cancellationToken);
			foreach (var order in portfolio?.Orders ?? [])
				await ProcessOrder(account.AccountId, order, message.TransactionId, cancellationToken);
		}

		if (!message.IsHistoryOnly())
			_orderSubscriptionId = message.TransactionId;
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private PublicAccount ResolveAccount(string portfolioName)
	{
		if (!portfolioName.IsEmpty())
			return _accounts.FirstOrDefault(a => a.AccountId.EqualsIgnoreCase(portfolioName))
				?? throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
		return _accounts.FirstOrDefault() ?? throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
	}

	private async ValueTask ProcessPortfolio(PublicPortfolio portfolio, long originalTransactionId, CancellationToken cancellationToken)
	{
		if (portfolio?.AccountId.IsEmpty() != false)
			return;

		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = portfolio.AccountId,
			BoardCode = BoardCode,
			Currency = CurrencyTypes.USD,
		}, cancellationToken);

		var cash = portfolio.Equity?.Where(e => e.Type == PublicAssetTypes.Cash).Sum(e => e.Value);
		var total = portfolio.Equity?.Sum(e => e.Value);
		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = portfolio.AccountId,
			SecurityId = SecurityId.Money,
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, cash, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, total, true)
		.TryAdd(PositionChangeTypes.BuyOrdersMargin, portfolio.BuyingPower?.Value)
		.TryAdd(PositionChangeTypes.BeginValue, portfolio.BuyingPower?.CashOnly), cancellationToken);

		foreach (var position in portfolio.Positions ?? [])
			await ProcessPosition(portfolio.AccountId, position, originalTransactionId, cancellationToken);
	}

	private ValueTask ProcessPosition(string accountId, PublicPortfolioPosition position, long originalTransactionId, CancellationToken cancellationToken)
	{
		if (position?.Instrument?.Symbol.IsEmpty() != false)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = accountId,
			SecurityId = new() { SecurityCode = position.Instrument.Symbol, BoardCode = BoardCode },
			ServerTime = (position.LastPrice?.Timestamp ?? position.Gain?.Timestamp ?? position.CostBasis?.LastUpdate)?.ToUniversalTime() ?? DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, position.Quantity, true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.CostBasis?.UnitCost, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, position.LastPrice?.Value)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.Gain?.Value)
		.TryAdd(PositionChangeTypes.VariationMargin, position.DailyGain?.Value), cancellationToken);
	}

	private async ValueTask ProcessOrder(string accountId, PublicOrder order, long originalTransactionId, CancellationToken cancellationToken)
	{
		if (order?.OrderId.IsEmpty() != false)
			return;

		var leg = order.Legs?.FirstOrDefault();
		var instrument = order.Instrument ?? leg?.Instrument;
		if (instrument?.Symbol.IsEmpty() != false)
			return;

		var side = order.Side;
		var transactionId = _orderTransactions.TryGetValue2(order.OrderId) ?? 0;
		var volume = order.Quantity ?? 0;
		var filled = order.FilledQuantity ?? 0;
		var serverTime = (order.ClosedAt ?? order.CreatedAt)?.ToUniversalTime() ?? DateTime.UtcNow;
		var condition = order.StopPrice is not null || order.OpenCloseIndicator is not null
			? new PublicOrderCondition { StopPrice = order.StopPrice, OpenCloseIndicator = order.OpenCloseIndicator }
			: null;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId == 0 ? transactionId : originalTransactionId,
			TransactionId = originalTransactionId == 0 ? 0 : transactionId,
			OrderStringId = order.OrderId,
			PortfolioName = accountId,
			SecurityId = new() { SecurityCode = instrument.Symbol, BoardCode = BoardCode },
			Side = side.ToSide(),
			OrderType = order.Type.ToOrderType(),
			OrderPrice = order.LimitPrice ?? 0,
			OrderVolume = volume,
			Balance = volume - filled,
			OrderState = order.Status.ToOrderState(),
			TimeInForce = order.Expiration?.TimeInForce.ToTimeInForce(),
			ExpiryDate = order.Expiration?.ExpirationTime?.ToUniversalTime(),
			ServerTime = serverTime,
			Condition = condition,
			Error = order.Status == PublicOrderStatuses.Rejected ? new InvalidOperationException(order.RejectReason.IsEmpty("Order rejected by Public.com.")) : null,
		}, cancellationToken);

		var previous = _executedQuantities.TryGetValue2(order.OrderId) ?? 0;
		if (filled > previous)
		{
			_executedQuantities[order.OrderId] = filled;
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = originalTransactionId == 0 ? transactionId : originalTransactionId,
				OrderStringId = order.OrderId,
				TradeStringId = $"{order.OrderId}:{filled.ToString(CultureInfo.InvariantCulture)}",
				PortfolioName = accountId,
				SecurityId = new() { SecurityCode = instrument.Symbol, BoardCode = BoardCode },
				Side = side.ToSide(),
				TradePrice = order.AveragePrice,
				TradeVolume = filled - previous,
				ServerTime = serverTime,
			}, cancellationToken);
		}

		if (order.Status is PublicOrderStatuses.Filled or PublicOrderStatuses.Cancelled or PublicOrderStatuses.QueuedCancelled or PublicOrderStatuses.Rejected or PublicOrderStatuses.Expired or PublicOrderStatuses.Replaced)
			_trackedOrders.Remove(order.OrderId);
	}

	private static PublicOrderExpiration CreateExpiration(DateTime? tillDate)
	{
		if (tillDate is not DateTime value)
			return new() { TimeInForce = PublicTimeInForces.Day };
		value = value.ToUniversalTime();
		if (value > DateTime.UtcNow.AddDays(90))
			throw new InvalidOperationException("Public.com GTD expiration cannot be more than 90 days in the future.");
		return new() { TimeInForce = PublicTimeInForces.GoodTillDate, ExpirationTime = value };
	}

	private static PublicOpenCloseIndicators? ToOpenClose(OrderPositionEffects? effect)
		=> effect switch
		{
			OrderPositionEffects.OpenOnly => PublicOpenCloseIndicators.Open,
			OrderPositionEffects.CloseOnly => PublicOpenCloseIndicators.Close,
			_ => null,
		};

	private static string Format(decimal value)
		=> value.ToString("0.#####", CultureInfo.InvariantCulture);

	private static string FormatPrice(decimal value)
		=> value.ToString("0.00", CultureInfo.InvariantCulture);
}
