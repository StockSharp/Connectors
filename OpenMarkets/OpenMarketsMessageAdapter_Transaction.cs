namespace StockSharp.OpenMarkets;

partial class OpenMarketsMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage message,
		CancellationToken cancellationToken)
	{
		if (message.Volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(message.Volume), message.Volume,
				"OpenMarkets requires positive order volume.");
		var orderType = message.OrderType ?? OrderTypes.Limit;
		if (orderType is not OrderTypes.Limit and not OrderTypes.Market)
			throw new NotSupportedException($"OpenMarkets standard order endpoint does not support {orderType} orders.");

		await EnsureMultiplier(message.SecurityId, cancellationToken);
		var multiplier = GetMultiplier(message.SecurityId);
		var accountCode = ResolveAccount(message.PortfolioName);
		var result = await _client.PlaceOrder(new()
		{
			AccountCode = accountCode,
			SecurityCode = message.SecurityId.SecurityCode,
			Exchange = message.SecurityId.BoardCode.IsEmpty(DefaultExchange),
			Destination = DefaultDestination.ThrowIfEmpty(nameof(DefaultDestination)),
			Side = message.Side.ToNativeSide(message.Side == Sides.Sell &&
				message.PositionEffect == OrderPositionEffects.OpenOnly),
			OrderPrice = orderType == OrderTypes.Limit
				? message.Price.ToNativePrice(multiplier)
				: null,
			OrderVolume = message.Volume,
			Lifetime = message.TimeInForce.ToLifetime(message.TillDate),
			PricingInstruction = orderType.ToPricingInstruction(),
			ExpiryDateTime = message.TillDate?.ToUtc(),
			Notes = message.Comment,
			OrderGiver = OrderGiver,
			OrderTaker = OrderTaker,
		}, cancellationToken);
		if (result?.OrderNumber <= 0)
			throw new InvalidOperationException("OpenMarkets accepted the order without returning an order number.");

		_orderTransactions[result.OrderNumber] = message.TransactionId;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = message.TransactionId,
			OrderId = result.OrderNumber,
			PortfolioName = accountCode,
			SecurityId = message.SecurityId,
			Side = message.Side,
			OrderType = orderType,
			OrderPrice = message.Price,
			OrderVolume = message.Volume,
			Balance = message.Volume,
			OrderState = OrderStates.Pending,
			TimeInForce = message.TimeInForce,
			ExpiryDate = message.TillDate?.ToUtc(),
			ServerTime = CurrentTime,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage message,
		CancellationToken cancellationToken)
	{
		if (message.OldOrderId is not long orderNumber || orderNumber <= 0)
			throw new InvalidOperationException(
				LocalizedStrings.OrderNoExchangeId.Put(message.OriginalTransactionId));
		if (message.Volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(message.Volume), message.Volume,
				"OpenMarkets requires positive replacement volume.");

		var orderType = message.OrderType ?? OrderTypes.Limit;
		if (orderType is not OrderTypes.Limit and not OrderTypes.Market)
			throw new NotSupportedException($"OpenMarkets standard order endpoint does not support {orderType} orders.");
		ResolveAccount(message.PortfolioName);
		await EnsureMultiplier(message.SecurityId, cancellationToken);
		var multiplier = GetMultiplier(message.SecurityId);
		await _client.AmendOrder(orderNumber, new()
		{
			Side = message.Side.ToNativeSide(message.Side == Sides.Sell &&
				message.PositionEffect == OrderPositionEffects.OpenOnly),
			OrderPrice = orderType == OrderTypes.Limit
				? message.Price.ToNativePrice(multiplier)
				: null,
			OrderVolume = message.Volume,
			Lifetime = message.TimeInForce.ToLifetime(message.TillDate),
			PricingInstruction = orderType.ToPricingInstruction(),
			ExpiryDateTime = message.TillDate?.ToUtc(),
			Notes = message.Comment,
			OrderGiver = OrderGiver,
			OrderTaker = OrderTaker,
		}, cancellationToken);
		_orderTransactions[orderNumber] = message.TransactionId;
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage message,
		CancellationToken cancellationToken)
	{
		if (message.OrderId is not long orderNumber || orderNumber <= 0)
			throw new InvalidOperationException(
				LocalizedStrings.OrderNoExchangeId.Put(message.OriginalTransactionId));
		ResolveAccount(message.PortfolioName);
		await _client.CancelOrder(orderNumber, cancellationToken);
		_orderTransactions[orderNumber] = message.TransactionId;
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			_orderStatusSubscriptions.Remove(message.OriginalTransactionId);
			return;
		}

		var accounts = message.PortfolioName.IsEmpty()
			? _accounts.Select(account => account.AccountCode).ToArray()
			: [ResolveAccount(message.PortfolioName)];
		var from = (message.From ?? CurrentTime.AddDays(-3)).ToUtc();
		var to = (message.To ?? CurrentTime).ToUtc();
		foreach (var account in accounts)
		{
			foreach (var order in await _client.GetOrders(account, cancellationToken) ?? [])
				await ProcessOrder(order, message.TransactionId, cancellationToken);
			foreach (var trade in await _client.GetTrades(account, from, to, cancellationToken) ?? [])
				await ProcessTrade(trade, message.TransactionId, false, cancellationToken);
		}

		if (message.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
		else
		{
			_orderStatusSubscriptions[message.TransactionId] = message.PortfolioName;
			await _streams.EnsureOmsSubscriptions(true, true, false, false, cancellationToken);
			await SendSubscriptionResultAsync(message, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			_portfolioSubscriptions.Remove(message.OriginalTransactionId);
			return;
		}

		var accounts = message.PortfolioName.IsEmpty()
			? _accounts
			: [_accounts.First(account =>
				account.AccountCode.EqualsIgnoreCase(ResolveAccount(message.PortfolioName)))];
		foreach (var account in accounts)
		{
			await SendOutMessageAsync(new PortfolioMessage
			{
				OriginalTransactionId = message.TransactionId,
				PortfolioName = account.AccountCode,
				BoardCode = DefaultExchange,
			}, cancellationToken);
		}

		var accountCodes = accounts.Select(account => account.AccountCode)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var portfolioCodes = _portfolioLinks
			.Where(link => accountCodes.Contains(link.AccountCode))
			.Select(link => link.PortfolioCode)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (portfolioCodes.Length > 0)
		{
			var details = await _client.GetPortfolioDetails(portfolioCodes, cancellationToken) ?? [];
			var cash = await _client.GetPortfolioCash(portfolioCodes, cancellationToken) ?? [];
			var positions = await _client.GetPortfolioPositions(portfolioCodes, cancellationToken) ?? [];

			foreach (var balance in cash)
			{
				var portfolioCode = balance.PortfolioCode;
				if (portfolioCode.IsEmpty())
					portfolioCode = details.FirstOrDefault(detail =>
						detail.PortfolioCashCode.EqualsIgnoreCase(balance.PortfolioCashCode))?.PortfolioCode;
				var accountCode = ResolveAccountForPortfolio(portfolioCode, balance.AccountCode);
				if (!accountCode.IsEmpty() && accountCodes.Contains(accountCode))
					await ProcessCash(accountCode, balance, message.TransactionId, cancellationToken);
			}
			foreach (var position in positions)
			{
				var accountCode = ResolveAccountForPortfolio(position.PortfolioCode,
					position.AccountCode);
				if (!accountCode.IsEmpty() && accountCodes.Contains(accountCode))
					await ProcessPosition(accountCode, position, message.TransactionId,
						cancellationToken);
			}
		}

		if (message.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
		else
		{
			_portfolioSubscriptions[message.TransactionId] = message.PortfolioName;
			await _streams.EnsureOmsSubscriptions(false, false, true, true, cancellationToken);
			await SendSubscriptionResultAsync(message, cancellationToken);
		}
	}

	private async ValueTask ProcessStreamOrders(OpenMarketsStreamOrder[] orders,
		CancellationToken cancellationToken)
	{
		foreach (var order in orders ?? [])
		{
			if (!IsAccessibleAccount(order.AccountCode))
				continue;
			var originalTransactionId = ResolveOrderSubscription(order.OrderNumber,
				order.AccountCode);
			await ProcessOrder(new OpenMarketsOrder
			{
				OrderNumber = order.OrderNumber,
				AccountCode = order.AccountCode,
				SecurityCode = order.SecurityCode,
				Exchange = order.Exchange,
				Destination = order.Destination,
				PricingInstructions = order.PricingInstructions,
				OrderState = order.OrderState,
				LastAction = order.LastAction,
				ActionStatus = order.ActionStatus,
				OrderVolume = order.OrderVolume,
				OrderPrice = order.OrderPrice,
				RemainingVolume = order.RemainingVolume,
				DoneVolumeTotal = order.DoneVolumeTotal,
				AveragePrice = order.AveragePrice,
				Lifetime = order.Lifetime,
				Currency = order.Currency,
				ExpiryDateTime = order.ExpiryDateTime,
				StateDescription = order.StateDescription,
				CreateDateTime = order.CreateDateTime,
				UpdateDateTime = order.UpdateDateTime,
				DestinationOrderNumber = order.DestinationOrderNumber,
				Side = order.Side,
				PriceMultiplier = order.PriceMultiplier,
				PostTradeStatus = order.PostTradeStatus,
				Notes = order.Notes,
			}, originalTransactionId, cancellationToken);
		}
	}

	private async ValueTask ProcessStreamTrades(OpenMarketsStreamTrade[] trades,
		CancellationToken cancellationToken)
	{
		foreach (var trade in trades ?? [])
		{
			if (!IsAccessibleAccount(trade.AccountCode))
				continue;
			var originalTransactionId = ResolveOrderSubscription(trade.OrderNumber,
				trade.AccountCode);
			await ProcessTrade(new OpenMarketsTrade
			{
				TradeNumber = trade.TradeNumber,
				OrderNumber = trade.OrderNumber,
				AccountCode = trade.AccountCode,
				SecurityCode = trade.SecurityCode,
				Exchange = trade.Exchange,
				Destination = trade.Destination,
				TradeVolume = trade.TradeVolume,
				TradePrice = trade.TradePrice,
				TradeValue = trade.TradeValue,
				DestinationTradeNumber = trade.DestinationTradeNumber,
				Side = trade.BuyOrSell.IsEmpty(trade.SideCode),
				PriceMultiplier = trade.PriceMultiplier,
				TradeDateTimeGmt = trade.TradeDateTimeGmt,
				TradeDateTime = trade.ExchangeTradeDateTime,
			}, originalTransactionId, true, cancellationToken);
		}
	}

	private async ValueTask ProcessStreamPositions(OpenMarketsStreamPosition[] positions,
		CancellationToken cancellationToken)
	{
		foreach (var position in positions ?? [])
		{
			var accountCode = ResolveAccountForPortfolio(position.PortfolioCode,
				position.AccountCode);
			var subscriptionId = ResolvePortfolioSubscription(accountCode);
			if (subscriptionId == 0)
				continue;
			await ProcessPosition(accountCode, new OpenMarketsPortfolioPosition
			{
				AccountCode = position.AccountCode,
				PortfolioCode = position.PortfolioCode,
				SecurityCode = position.SecurityCode,
				Exchange = position.Exchange,
				UpdateDateTime = position.UpdateDateTime,
				VolumeStartOfDay = position.VolumeStartOfDay,
				AveragePriceStartOfDay = position.AveragePriceStartOfDay,
				AveragePrice = position.AveragePrice,
				BuyVolume = position.BuyVolume,
				SellVolume = position.SellVolume,
				AvailableVolume = position.AvailableVolume,
				MarketValue = position.MarketValue,
				TotalProfit = position.TotalProfit,
				TodayProfit = position.TodayProfit,
				ClosedProfit = position.ClosedProfit,
				ActualVolume = position.ActualVolume,
				ShortSellVolume = position.ShortSellVolume,
			}, subscriptionId, cancellationToken);
		}
	}

	private async ValueTask ProcessStreamCash(OpenMarketsStreamCash[] cash,
		CancellationToken cancellationToken)
	{
		foreach (var balance in cash ?? [])
		{
			var accountCode = ResolveAccountForPortfolio(balance.PortfolioCode,
				balance.AccountCode);
			var subscriptionId = ResolvePortfolioSubscription(accountCode);
			if (subscriptionId == 0)
				continue;
			await ProcessCash(accountCode, new OpenMarketsPortfolioCash
			{
				PortfolioCode = balance.PortfolioCode,
				AccountCode = balance.AccountCode,
				PortfolioCashCode = balance.PortfolioCashCode,
				CurrencyCode = balance.CurrencyCode,
				UpdateDateTime = balance.UpdateDateTime,
				CashBalance = balance.CashBalance,
				NetCash = balance.NetCash,
				InMarketBuyValue = balance.InMarketBuyValue,
				InMarketSellValue = balance.InMarketSellValue,
				NetUnsettledValueToday = balance.NetUnsettledValueToday,
				Glv = balance.Glv,
				FreeEquity = balance.FreeEquity,
				TotalInitialMargin = balance.TotalInitialMargin,
				TotalCfdRealisedProfit = balance.TotalCfdRealisedProfit,
				TotalCfdUnrealisedProfit = balance.TotalCfdUnrealisedProfit,
				TotalNonCfdMarketValue = balance.TotalNonCfdMarketValue,
				TrustBalance = balance.TrustBalance,
			}, subscriptionId, cancellationToken);
		}
	}

	private async ValueTask ProcessOrder(OpenMarketsOrder order, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (order == null || order.OrderNumber <= 0 || order.SecurityCode.IsEmpty())
			return;
		SetMultiplier(order.SecurityCode, order.Exchange, order.PriceMultiplier);
		var securityId = order.SecurityCode.ToSecurityId(order.Exchange);
		var multiplier = GetMultiplier(securityId);
		var transactionId = _orderTransactions.TryGetValue2(order.OrderNumber) ?? 0;
		var state = order.ToOrderState();
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId == 0 ? transactionId : originalTransactionId,
			TransactionId = transactionId,
			OrderId = order.OrderNumber,
			OrderStringId = order.DestinationOrderNumber,
			PortfolioName = order.AccountCode,
			SecurityId = securityId,
			Side = order.Side.ToSide(),
			PositionEffect = order.Side.EqualsIgnoreCase("Short")
				? OrderPositionEffects.OpenOnly
				: null,
			OrderType = order.PricingInstructions.ToOrderType(),
			OrderPrice = order.OrderPrice.FromNativePrice(multiplier) ?? 0,
			OrderVolume = order.OrderVolume,
			Balance = order.RemainingVolume,
			AveragePrice = order.AveragePrice.FromNativePrice(multiplier),
			OrderState = state,
			TimeInForce = order.Lifetime.ToTimeInForce(),
			ExpiryDate = order.ExpiryDateTime?.ToExchangeUtc(),
			ServerTime = (order.UpdateDateTime ?? order.CreateDateTime)?.ToExchangeUtc() ?? CurrentTime,
			Comment = order.Notes,
			Error = state == OrderStates.Failed
				? new InvalidOperationException(order.StateDescription.IsEmpty(order.ActionStatus))
				: null,
		}, cancellationToken);
	}

	private async ValueTask ProcessTrade(OpenMarketsTrade trade, long originalTransactionId,
		bool isLive, CancellationToken cancellationToken)
	{
		if (trade == null || trade.TradeNumber <= 0 || trade.SecurityCode.IsEmpty())
			return;
		if (isLive && !_reportedTrades.TryAdd(trade.TradeNumber))
			return;
		SetMultiplier(trade.SecurityCode, trade.Exchange, trade.PriceMultiplier);
		var securityId = trade.SecurityCode.ToSecurityId(trade.Exchange);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originalTransactionId,
			OrderId = trade.OrderNumber,
			TradeId = trade.TradeNumber,
			TradeStringId = trade.DestinationTradeNumber,
			PortfolioName = trade.AccountCode,
			SecurityId = securityId,
			Side = trade.Side.ToSide(),
			PositionEffect = trade.Side.EqualsIgnoreCase("Short")
				? OrderPositionEffects.OpenOnly
				: null,
			TradePrice = trade.TradePrice.FromNativePrice(GetMultiplier(securityId)),
			TradeVolume = trade.TradeVolume,
			ServerTime = trade.TradeDateTimeGmt?.ToUtc() ??
				trade.TradeDateTime?.ToExchangeUtc() ?? CurrentTime,
		}, cancellationToken);
	}

	private ValueTask ProcessCash(string accountCode, OpenMarketsPortfolioCash cash,
		long originalTransactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = accountCode,
			SecurityId = SecurityId.Money,
			ServerTime = cash.UpdateDateTime?.ToExchangeUtc() ?? CurrentTime,
		}
		.TryAdd(PositionChangeTypes.Currency, cash.CurrencyCode.To<CurrencyTypes?>())
		.TryAdd(PositionChangeTypes.CurrentValue, cash.CashBalance, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, cash.Glv ?? cash.TotalNonCfdMarketValue)
		.TryAdd(PositionChangeTypes.BuyOrdersMargin, cash.NetCash ?? cash.FreeEquity)
		.TryAdd(PositionChangeTypes.BlockedValue, cash.TotalInitialMargin)
		.TryAdd(PositionChangeTypes.RealizedPnL, cash.TotalCfdRealisedProfit)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, cash.TotalCfdUnrealisedProfit), cancellationToken);

	private async ValueTask ProcessPosition(string accountCode, OpenMarketsPortfolioPosition position,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		var securityId = position.SecurityCode.ToSecurityId(position.Exchange);
		await EnsureMultiplier(securityId, cancellationToken);
		var multiplier = GetMultiplier(securityId);
		var volume = position.ActualVolume ??
			(position.VolumeStartOfDay ?? 0) + (position.BuyVolume ?? 0) - (position.SellVolume ?? 0);
		var pricedVolume = position.AvailableVolume ?? volume;
		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = accountCode,
			SecurityId = securityId,
			ServerTime = position.UpdateDateTime?.ToExchangeUtc() ?? CurrentTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, volume, true)
		.TryAdd(PositionChangeTypes.AveragePrice,
			(position.AveragePrice ?? position.AveragePriceStartOfDay).FromNativePrice(multiplier))
		.TryAdd(PositionChangeTypes.CurrentPrice, pricedVolume == 0 || position.MarketValue == null
			? null
			: Math.Abs(position.MarketValue.Value / pricedVolume))
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.TotalProfit)
		.TryAdd(PositionChangeTypes.RealizedPnL, position.ClosedProfit)
		.TryAdd(PositionChangeTypes.VariationMargin, position.TodayProfit), cancellationToken);
	}

	private long ResolveOrderSubscription(long orderNumber, string accountCode)
	{
		var transactionId = _orderTransactions.TryGetValue2(orderNumber) ?? 0;
		if (transactionId != 0)
			return transactionId;
		return _orderStatusSubscriptions.FirstOrDefault(pair => pair.Value.IsEmpty() ||
			pair.Value.EqualsIgnoreCase(accountCode)).Key;
	}

	private long ResolvePortfolioSubscription(string accountCode)
		=> _portfolioSubscriptions.FirstOrDefault(pair => pair.Value.IsEmpty() ||
			pair.Value.EqualsIgnoreCase(accountCode)).Key;

	private bool IsAccessibleAccount(string accountCode)
		=> _accounts.Any(account => account.AccountCode.EqualsIgnoreCase(accountCode));
}
