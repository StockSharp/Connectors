namespace StockSharp.Yuanta;

public partial class YuantaMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		var security = regMsg.SecurityId.ParseYuantaSecurity(regMsg.SecurityType);
		CacheSecurity(security);
		var securityType = security.SecurityType;
		if (securityType is not SecurityTypes.Stock and not SecurityTypes.Future and not SecurityTypes.Option)
			throw new NotSupportedException($"Yuanta cannot submit {securityType} orders through this adapter.");
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not OrderTypes.Limit and not OrderTypes.Market)
			throw new NotSupportedException("Yuanta supports limit and market orders through this adapter.");
		if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Price), regMsg.Price,
				"A positive price is required for a Yuanta limit order.");
		var condition = regMsg.Condition as YuantaOrderCondition ?? new();
		ValidateCondition(condition, securityType);
		var volume = ToOrderVolume(regMsg.Volume, securityType, nameof(regMsg.Volume));
		var timeInForce = regMsg.TimeInForce ?? TimeInForce.PutInQueue;
		var update = await _client.PlaceOrderAsync(new()
		{
			TransactionId = regMsg.TransactionId,
			Account = regMsg.PortfolioName,
			Market = security.Market,
			Symbol = security.Symbol,
			OrderSymbol = condition.OrderSymbol,
			SecurityType = securityType,
			Side = regMsg.Side,
			OrderType = orderType,
			TimeInForce = timeInForce,
			Price = regMsg.Price,
			Volume = volume,
			StockMarketType = condition.StockMarketType,
			StockOrderType = condition.StockOrderType,
			PositionEffect = condition.PositionEffect,
			FuturesPriceType = condition.FuturesPriceType,
			SettlementMonth = condition.SettlementMonth,
			OptionType = condition.OptionType,
			StrikePrice = condition.StrikePrice,
			IsDayTrade = condition.IsDayTrade,
			IsPreOrder = condition.IsPreOrder,
			UserTag = condition.UserTag,
		}, cancellationToken);

		var tracker = new OrderTracker
		{
			TransactionId = regMsg.TransactionId,
			SecurityId = regMsg.SecurityId,
			PortfolioName = update.Account.IsEmpty(regMsg.PortfolioName),
			SecurityType = securityType,
			Side = regMsg.Side,
			OrderType = orderType,
			TimeInForce = timeInForce,
			Condition = condition,
		};
		CacheOrderTracker(update.OrderId, tracker);
		_transactionOrders[regMsg.TransactionId] = update.OrderId;
		await ProcessOrder(update, regMsg.TransactionId, false, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		var orderId = ResolveOrderId(replaceMsg.OldOrderStringId, replaceMsg.OriginalTransactionId);
		_orders.TryGetValue(orderId, out var previous);
		var securityType = replaceMsg.SecurityType ?? previous?.SecurityType ?? SecurityTypes.Stock;
		var volume = replaceMsg.Volume > 0
			? ToOrderVolume(replaceMsg.Volume, securityType, nameof(replaceMsg.Volume))
			: (long?)null;
		var price = replaceMsg.Price > 0 ? replaceMsg.Price : (decimal?)null;
		if (price == null && volume == null)
			throw new InvalidOperationException("Yuanta replacement must change the price or quantity.");
		var update = await _client.ReplaceOrderAsync(orderId, replaceMsg.TransactionId,
			price, volume, cancellationToken);
		var tracker = new OrderTracker
		{
			TransactionId = replaceMsg.TransactionId,
			SecurityId = previous?.SecurityId ?? replaceMsg.SecurityId,
			PortfolioName = previous?.PortfolioName.IsEmpty(replaceMsg.PortfolioName),
			SecurityType = securityType,
			Side = previous?.Side ?? replaceMsg.Side,
			OrderType = replaceMsg.OrderType ?? previous?.OrderType ?? OrderTypes.Limit,
			TimeInForce = replaceMsg.TimeInForce ?? previous?.TimeInForce ?? TimeInForce.PutInQueue,
			Condition = replaceMsg.Condition as YuantaOrderCondition ?? previous?.Condition,
		};
		CacheOrderTracker(orderId, tracker);
		_transactionOrders[replaceMsg.TransactionId] = orderId;
		await ProcessOrder(update, replaceMsg.TransactionId, false, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		var orderId = ResolveOrderId(cancelMsg.OrderStringId, cancelMsg.OriginalTransactionId);
		var update = await _client.CancelOrderAsync(orderId, cancelMsg.TransactionId, cancellationToken);
		await ProcessOrder(update, cancelMsg.TransactionId, false, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		if (!statusMsg.IsSubscribe)
		{
			if (_orderStatusSubscriptionId == statusMsg.OriginalTransactionId)
				_orderStatusSubscriptionId = 0;
			return;
		}
		await SendOrderSnapshot(statusMsg.TransactionId, cancellationToken, statusMsg);
		if (statusMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
		else
		{
			_orderStatusSubscriptionId = statusMsg.TransactionId;
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		if (!lookupMsg.IsSubscribe)
		{
			if (_portfolioSubscriptionId == lookupMsg.OriginalTransactionId)
			{
				_portfolioSubscriptionId = 0;
				_portfolioFilter = null;
			}
			return;
		}
		await SendPortfolioSnapshot(lookupMsg.TransactionId, lookupMsg.PortfolioName, cancellationToken);
		if (lookupMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
		else
		{
			_portfolioSubscriptionId = lookupMsg.TransactionId;
			_portfolioFilter = lookupMsg.PortfolioName;
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		}
	}

	private ValueTask OnOrder(YuantaOrderUpdate update, CancellationToken cancellationToken)
		=> ProcessOrder(update, 0, false, cancellationToken);

	private ValueTask OnFill(YuantaOrderTrade trade, CancellationToken cancellationToken)
		=> ProcessFill(trade, 0, cancellationToken);

	private async ValueTask SendOrderSnapshot(long originId, CancellationToken cancellationToken,
		OrderStatusMessage filter = null)
	{
		var snapshot = await _client.GetOrderSnapshotAsync(cancellationToken);
		var left = filter?.Count ?? long.MaxValue;
		foreach (var update in snapshot.Orders.OrderBy(item => item.ServerTime))
		{
			if (!IsOrderMatch(update, filter))
				continue;
			await ProcessOrder(update, originId, true, cancellationToken);
			if (--left <= 0)
				break;
		}
		if (left > 0)
		{
			foreach (var trade in snapshot.Trades.OrderBy(item => item.ServerTime))
			{
				if (!IsTradeMatch(trade, filter))
					continue;
				await ProcessFill(trade, originId, cancellationToken);
				if (--left <= 0)
					break;
			}
		}
		_lastOrderRefresh = CurrentTime;
	}

	private async ValueTask SendPortfolioSnapshot(long originId, string portfolioName,
		CancellationToken cancellationToken)
	{
		var account = _client.Account ?? throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
		if (!portfolioName.IsEmpty() && !portfolioName.EqualsIgnoreCase(account.Account))
			throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
		var snapshot = await _client.GetPortfolioAsync(cancellationToken);
		var portfolio = snapshot.Portfolio;
		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = originId,
			PortfolioName = account.Account,
			BoardCode = account.IsFutures ? "TAIFEX" : "TWSE",
			Currency = portfolio.Currency.ToCurrency(),
		}, cancellationToken);

		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originId,
			PortfolioName = account.Account,
			SecurityId = SecurityId.Money,
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, portfolio.CurrentValue, true)
		.TryAdd(PositionChangeTypes.BlockedValue, portfolio.BlockedValue, true)
		.TryAdd(PositionChangeTypes.RealizedPnL, portfolio.RealizedPnL, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, portfolio.UnrealizedPnL, true), cancellationToken);

		foreach (var position in snapshot.Positions)
		{
			var security = ResolveSecurity(position.Market, position.Symbol, position.SecurityType);
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originId,
				PortfolioName = account.Account,
				SecurityId = security.ToSecurityId(),
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, position.CurrentValue, true)
			.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice, true)
			.TryAdd(PositionChangeTypes.CurrentPrice, position.CurrentPrice, true)
			.TryAdd(PositionChangeTypes.BlockedValue, position.BlockedValue, true), cancellationToken);
		}
		_lastPortfolioRefresh = CurrentTime;
	}

	private async ValueTask ProcessOrder(YuantaOrderUpdate update, long originId, bool isLookup,
		CancellationToken cancellationToken)
	{
		if (update?.OrderId.IsEmpty() != false)
			return;
		_orders.TryGetValue(update.OrderId, out var tracker);
		if (tracker != null)
			CacheOrderTracker(update.OrderId, tracker);
		var securityId = tracker?.SecurityId ?? ResolveSecurity(update.Market,
			update.Symbol, update.SecurityType).ToSecurityId();
		var messageOrigin = isLookup ? originId : originId != 0 ? originId :
			tracker?.TransactionId is > 0 ? tracker.TransactionId : _orderStatusSubscriptionId;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = messageOrigin,
			TransactionId = isLookup ? tracker?.TransactionId ?? update.TransactionId : 0,
			OrderStringId = update.OrderId,
			SecurityId = securityId,
			PortfolioName = tracker?.PortfolioName.IsEmpty(update.Account),
			OrderType = tracker?.OrderType ?? update.OrderType,
			Side = tracker?.Side ?? update.Side,
			TimeInForce = tracker?.TimeInForce ?? update.TimeInForce,
			OrderPrice = update.Price,
			OrderVolume = update.Volume,
			Balance = update.Balance,
			OrderState = update.State,
			ServerTime = update.ServerTime == default ? CurrentTime : update.ServerTime,
			Condition = tracker?.Condition ?? CreateCondition(update),
			Error = update.State == OrderStates.Failed || !update.Error.IsEmpty()
				? new InvalidOperationException(update.Error.IsEmpty("Yuanta rejected the order."))
				: null,
		}, cancellationToken);
	}

	private async ValueTask ProcessFill(YuantaOrderTrade trade, long originId,
		CancellationToken cancellationToken)
	{
		if (trade == null || trade.Symbol.IsEmpty())
			return;
		var tradeId = trade.TradeId.IsEmpty(
			$"{trade.OrderId}:{trade.ServerTime.Ticks}:{trade.Price}:{trade.Volume}");
		if (!_tradeIds.TryAdd($"{trade.OrderId}|{tradeId}"))
			return;
		_orders.TryGetValue(trade.OrderId, out var tracker);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originId != 0 ? originId :
				tracker?.TransactionId ?? _orderStatusSubscriptionId,
			OrderStringId = trade.OrderId,
			TradeStringId = tradeId,
			SecurityId = tracker?.SecurityId ?? ResolveSecurity(trade.Market,
				trade.Symbol, trade.SecurityType).ToSecurityId(),
			PortfolioName = tracker?.PortfolioName.IsEmpty(trade.Account),
			Side = tracker?.Side ?? trade.Side,
			TradePrice = trade.Price,
			TradeVolume = trade.Volume,
			ServerTime = trade.ServerTime == default ? CurrentTime : trade.ServerTime,
		}, cancellationToken);
	}

	private void CacheOrderTracker(string orderId, OrderTracker tracker)
	{
		if (!orderId.IsEmpty())
			_orders[orderId] = tracker;
	}

	private string ResolveOrderId(string orderId, long transactionId)
	{
		if (!orderId.IsEmpty())
			return orderId;
		if (_transactionOrders.TryGetValue(transactionId, out orderId) && !orderId.IsEmpty())
			return orderId;
		throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(transactionId));
	}

	private static bool IsOrderMatch(YuantaOrderUpdate update, OrderStatusMessage filter)
	{
		if (filter == null)
			return true;
		if (filter.From is DateTime from && update.ServerTime < NormalizeUtc(from))
			return false;
		if (filter.To is DateTime to && update.ServerTime > NormalizeUtc(to))
			return false;
		return filter.PortfolioName.IsEmpty() || update.Account.EqualsIgnoreCase(filter.PortfolioName);
	}

	private static bool IsTradeMatch(YuantaOrderTrade trade, OrderStatusMessage filter)
	{
		if (filter == null)
			return true;
		if (filter.From is DateTime from && trade.ServerTime < NormalizeUtc(from))
			return false;
		if (filter.To is DateTime to && trade.ServerTime > NormalizeUtc(to))
			return false;
		return filter.PortfolioName.IsEmpty() || trade.Account.EqualsIgnoreCase(filter.PortfolioName);
	}

	private static YuantaOrderCondition CreateCondition(YuantaOrderUpdate update)
		=> new()
		{
			FuturesPriceType = update.IsFutures
				? update.OrderType == OrderTypes.Market
					? YuantaFuturesPriceTypes.Market
					: YuantaFuturesPriceTypes.Limit
				: YuantaFuturesPriceTypes.Auto,
		};

	private static long ToOrderVolume(decimal volume, SecurityTypes securityType, string parameterName)
	{
		var max = securityType is SecurityTypes.Future or SecurityTypes.Option ? short.MaxValue : int.MaxValue;
		if (volume <= 0 || volume != decimal.Truncate(volume) || volume > max)
			throw new ArgumentOutOfRangeException(parameterName, volume,
				$"Yuanta order quantities must be positive whole numbers no greater than {max}.");
		return decimal.ToInt64(volume);
	}

	private static void ValidateCondition(YuantaOrderCondition condition, SecurityTypes securityType)
	{
		if (!condition.UserTag.IsEmpty() && (condition.UserTag.Length > 32 ||
			condition.UserTag.Any(character => !char.IsAsciiLetterOrDigit(character))))
			throw new ArgumentOutOfRangeException(nameof(condition.UserTag), condition.UserTag,
				"Yuanta BasketNo values must contain up to 32 ASCII letters or digits.");
		if (securityType is not SecurityTypes.Future and not SecurityTypes.Option)
			return;
		if (condition.SettlementMonth is < 200001 or > 999912 || condition.SettlementMonth % 100 is < 1 or > 12)
			throw new ArgumentOutOfRangeException(nameof(condition.SettlementMonth), condition.SettlementMonth,
				"Yuanta futures/options orders require SettlementMonth in YYYYMM format.");
		if (securityType == SecurityTypes.Option && (condition.OptionType == null || condition.StrikePrice <= 0))
			throw new InvalidOperationException(
				"Yuanta options orders require OptionType and a positive StrikePrice.");
	}
}
