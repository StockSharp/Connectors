namespace StockSharp.TradingTechnologies;

using Native;

partial class TradingTechnologiesMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage message, CancellationToken cancellationToken)
	{
		var stopPrice = (message.Condition as TradingTechnologiesOrderCondition)?.StopPrice;
		var orderType = (message.OrderType ?? OrderTypes.Limit, stopPrice) switch
		{
			(OrderTypes.Market, null) => "Market",
			(OrderTypes.Market, _) or (OrderTypes.Conditional, _) => "Stop",
			(OrderTypes.Limit, null) => "Limit",
			(OrderTypes.Limit, _) => "StopLimit",
			_ => throw new ArgumentOutOfRangeException(nameof(message.OrderType), message.OrderType, LocalizedStrings.InvalidValue),
		};

		var siteOrderKey = await EnsureClient().PlaceOrderAsync(new TradingTechnologiesOrderRequest
		{
			TransactionId = message.TransactionId,
			InstrumentId = message.SecurityId.GetNativeId(),
			Symbol = message.SecurityId.SecurityCode,
			Market = message.SecurityId.BoardCode,
			Account = message.PortfolioName,
			Side = message.Side == Sides.Buy ? "Buy" : "Sell",
			OrderType = orderType,
			TimeInForce = (message.TimeInForce ?? TimeInForce.PutInQueue) switch
			{
				TimeInForce.CancelBalance => "ImmediateOrCancel",
				TimeInForce.MatchOrCancel => "FillOrKill",
				_ when message.TillDate != null => "GoodTillDate",
				_ => "Day",
			},
			PositionEffect = message.PositionEffect switch
			{
				OrderPositionEffects.OpenOnly => "Open",
				OrderPositionEffects.CloseOnly => "Close",
				_ => null,
			},
			Volume = message.Volume,
			Price = orderType is "Limit" or "StopLimit" ? message.Price : null,
			StopPrice = stopPrice,
			TillDate = message.TillDate,
		}, cancellationToken);

		_orderTransactions[siteOrderKey] = message.TransactionId;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = message.TransactionId,
			OrderStringId = siteOrderKey,
			PortfolioName = message.PortfolioName,
			SecurityId = message.SecurityId,
			Side = message.Side,
			OrderType = message.OrderType,
			OrderPrice = message.Price,
			OrderVolume = message.Volume,
			Balance = message.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = DateTime.UtcNow,
			Condition = message.Condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage message, CancellationToken cancellationToken)
	{
		var siteOrderKey = message.OldOrderStringId
			?? throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(message.OriginalTransactionId));
		var stopPrice = (message.Condition as TradingTechnologiesOrderCondition)?.StopPrice;
		await EnsureClient().ReplaceOrderAsync(
			siteOrderKey,
			message.TransactionId,
			message.Volume,
			message.Price,
			stopPrice,
			cancellationToken);
		_orderTransactions[siteOrderKey] = message.TransactionId;
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(OrderCancelMessage message, CancellationToken cancellationToken)
	{
		var siteOrderKey = message.OrderStringId
			?? throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(message.OriginalTransactionId));
		return EnsureClient().CancelOrderAsync(siteOrderKey, message.TransactionId, cancellationToken);
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

		_portfolioSubscriptionId = message.TransactionId;
		var accounts = await EnsureClient().GetAccountsAsync(cancellationToken);
		foreach (var account in accounts)
		{
			if (!message.PortfolioName.IsEmpty() && !account.Name.EqualsIgnoreCase(message.PortfolioName))
				continue;
			await SendPortfolioAsync(account, message.TransactionId, cancellationToken);
		}

		foreach (var position in await EnsureClient().GetPositionsAsync(cancellationToken))
		{
			if (!message.PortfolioName.IsEmpty() && !position.Account.Name.EqualsIgnoreCase(message.PortfolioName))
				continue;
			await SendPositionAsync(position, message.TransactionId, cancellationToken);
		}

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			if (_orderStatusSubscriptionId == message.OriginalTransactionId)
				_orderStatusSubscriptionId = 0;
			return;
		}

		_orderStatusSubscriptionId = message.TransactionId;
		foreach (var order in await EnsureClient().GetOrdersAsync(cancellationToken))
		{
			if (!message.PortfolioName.IsEmpty() && !order.Account.EqualsIgnoreCase(message.PortfolioName))
				continue;
			await SendOrderAsync(order, message.TransactionId, cancellationToken);
		}

		foreach (var fill in await EnsureClient().GetFillsAsync(cancellationToken))
		{
			if (!message.PortfolioName.IsEmpty() && !fill.Account.EqualsIgnoreCase(message.PortfolioName))
				continue;
			await SendFillAsync(fill, message.TransactionId, cancellationToken);
		}

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private ValueTask ProcessOrderAsync(TradingTechnologiesOrder order, CancellationToken cancellationToken)
		=> SendOrderAsync(order, 0, cancellationToken);

	private async ValueTask SendOrderAsync(
		TradingTechnologiesOrder order,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (order == null || order.SiteOrderKey.IsEmpty())
			return;

		var transactionId = order.TransactionId;
		if (transactionId == 0)
			transactionId = _orderTransactions.TryGetValue(order.SiteOrderKey, out var storedTransactionId)
				? storedTransactionId
				: 0;
		else
			_orderTransactions[order.SiteOrderKey] = transactionId;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId == 0 ? transactionId : originalTransactionId,
			TransactionId = originalTransactionId == 0 ? 0 : transactionId,
			OrderStringId = order.SiteOrderKey,
			OrderBoardId = order.ExchangeOrderId,
			PortfolioName = order.Account,
			SecurityId = ResolveSecurityId(order.Instrument),
			Side = order.Side.ToSide(),
			OrderType = order.OrderType.ToOrderType(),
			OrderPrice = order.Price ?? 0,
			OrderVolume = order.Volume,
			Balance = order.Balance,
			OrderState = order.Status.ToOrderState(),
			TimeInForce = order.TimeInForce.ToTimeInForce(),
			PositionEffect = order.PositionEffect.ToPositionEffect(),
			ServerTime = order.ServerTime,
			Condition = order.StopPrice is decimal stopPrice
				? new TradingTechnologiesOrderCondition { StopPrice = stopPrice }
				: null,
			Error = order.Error.IsEmpty() ? null : new InvalidOperationException(order.Error),
		}, cancellationToken);
	}

	private ValueTask ProcessFillAsync(TradingTechnologiesFill fill, CancellationToken cancellationToken)
		=> SendFillAsync(fill, 0, cancellationToken);

	private async ValueTask SendFillAsync(
		TradingTechnologiesFill fill,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (fill == null || fill.FillId.IsEmpty())
			return;
		if (originalTransactionId == 0 && !_fills.TryAdd(fill.FillId, 0))
			return;
		_fills.TryAdd(fill.FillId, 0);

		var transactionId = _orderTransactions.TryGetValue(fill.SiteOrderKey, out var storedTransactionId)
			? storedTransactionId
			: 0;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originalTransactionId == 0 ? transactionId : originalTransactionId,
			OrderStringId = fill.SiteOrderKey,
			TradeStringId = fill.FillId,
			PortfolioName = fill.Account,
			SecurityId = ResolveSecurityId(fill.Instrument),
			Side = fill.Side.ToSide(),
			TradePrice = fill.Price,
			TradeVolume = fill.Volume,
			ServerTime = fill.ServerTime,
		}, cancellationToken);
	}

	private ValueTask ProcessPositionAsync(TradingTechnologiesPosition position, CancellationToken cancellationToken)
		=> SendPositionAsync(position, _portfolioSubscriptionId, cancellationToken);

	private async ValueTask SendPositionAsync(
		TradingTechnologiesPosition position,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (position?.Account == null || position.Instrument == null)
			return;

		await SendPortfolioAsync(position.Account, originalTransactionId, cancellationToken);
		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = position.Account.Name,
			SecurityId = ResolveSecurityId(position.Instrument),
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, position.CurrentValue, true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnL, true)
		.TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedPnL, true), cancellationToken);
	}

	private ValueTask SendPortfolioAsync(
		TradingTechnologiesAccount account,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (account == null || account.Name.IsEmpty())
			return default;
		if (originalTransactionId == 0 && !_announcedPortfolios.TryAdd(account.Name, 0))
			return default;
		_announcedPortfolios.TryAdd(account.Name, 0);
		return SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = account.Name,
			BoardCode = BoardCodes.TradingTechnologies,
		}, cancellationToken);
	}
}
