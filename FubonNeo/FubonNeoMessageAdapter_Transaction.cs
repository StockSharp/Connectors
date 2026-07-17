namespace StockSharp.FubonNeo;

public partial class FubonNeoMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		var security = regMsg.SecurityId.ParseFubonSecurity(regMsg.SecurityType);
		CacheSecurity(security);
		var securityType = security.ToSecurityType();
		if (securityType is not SecurityTypes.Stock and not SecurityTypes.Warrant and
			not SecurityTypes.Future and not SecurityTypes.Option)
			throw new NotSupportedException($"Fubon cannot submit {securityType} orders.");
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not OrderTypes.Limit and not OrderTypes.Market)
			throw new NotSupportedException("Fubon supports limit and market orders through this adapter.");
		var condition = regMsg.Condition as FubonNeoOrderCondition ?? new();
		ValidateUserTag(condition.UserTag);
		var priceType = condition.PriceType == FubonNeoPriceTypes.Auto
			? orderType == OrderTypes.Market ? FubonNeoPriceTypes.Market : FubonNeoPriceTypes.Limit
			: condition.PriceType;
		if (priceType == FubonNeoPriceTypes.RangeMarket && security.Kind == FubonNeoAssetKinds.Stock)
			throw new NotSupportedException("Fubon range-market orders are available for futures/options only.");
		if (priceType == FubonNeoPriceTypes.Limit && regMsg.Price <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Price), regMsg.Price,
				"A positive price is required for a Fubon limit order.");
		var volume = ToOrderVolume(regMsg.Volume, nameof(regMsg.Volume));
		var timeInForce = regMsg.TimeInForce ?? TimeInForce.PutInQueue;
		var update = await _client.PlaceOrderAsync(new()
		{
			PortfolioName = regMsg.PortfolioName,
			Kind = security.Kind,
			SecurityType = securityType,
			Symbol = security.Symbol,
			Side = regMsg.Side,
			OrderType = orderType,
			TimeInForce = timeInForce,
			Price = regMsg.Price,
			Volume = volume,
			StockMarketType = condition.StockMarketType,
			StockOrderType = condition.StockOrderType,
			FuturesOrderType = condition.FuturesOrderType,
			PriceType = priceType,
			IsAfterHours = condition.IsAfterHours || security.IsAfterHours,
			UserTag = condition.UserTag,
		}, cancellationToken);

		var tracker = new OrderTracker
		{
			TransactionId = regMsg.TransactionId,
			SecurityId = regMsg.SecurityId,
			PortfolioName = update.PortfolioName.IsEmpty(regMsg.PortfolioName),
			Side = regMsg.Side,
			OrderType = orderType,
			TimeInForce = timeInForce,
			Condition = condition,
		};
		CacheOrderTracker(update, tracker);
		_transactionOrders[regMsg.TransactionId] = update.OrderId.IsEmpty(update.Sequence);
		await ProcessOrder(update, regMsg.TransactionId, false, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		var orderId = ResolveOrderId(replaceMsg.OldOrderStringId, replaceMsg.OriginalTransactionId);
		var volume = replaceMsg.Volume > 0 ? ToOrderVolume(replaceMsg.Volume, nameof(replaceMsg.Volume)) : (long?)null;
		var price = replaceMsg.Price > 0 ? replaceMsg.Price : (decimal?)null;
		if (price == null && volume == null)
			throw new InvalidOperationException("Fubon replacement must change the price or quantity.");
		var update = await _client.ReplaceOrderAsync(orderId, price, volume, cancellationToken);
		_orders.TryGetValue(orderId, out var previous);
		var tracker = new OrderTracker
		{
			TransactionId = replaceMsg.TransactionId,
			SecurityId = previous?.SecurityId ?? replaceMsg.SecurityId,
			PortfolioName = previous?.PortfolioName.IsEmpty(replaceMsg.PortfolioName),
			Side = previous?.Side ?? replaceMsg.Side,
			OrderType = replaceMsg.OrderType ?? previous?.OrderType ?? OrderTypes.Limit,
			TimeInForce = replaceMsg.TimeInForce ?? previous?.TimeInForce ?? TimeInForce.PutInQueue,
			Condition = replaceMsg.Condition as FubonNeoOrderCondition ?? previous?.Condition,
		};
		CacheOrderTracker(update, tracker);
		_transactionOrders[replaceMsg.TransactionId] = update.OrderId.IsEmpty(update.Sequence);
		await ProcessOrder(update, replaceMsg.TransactionId, false, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		var orderId = ResolveOrderId(cancelMsg.OrderStringId, cancelMsg.OriginalTransactionId);
		var update = await _client.CancelOrderAsync(orderId, cancellationToken);
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

	private ValueTask OnOrder(FubonNeoOrderUpdate update, CancellationToken cancellationToken)
		=> ProcessOrder(update, 0, false, cancellationToken);

	private ValueTask OnFill(FubonNeoFillUpdate fill, CancellationToken cancellationToken)
		=> ProcessFill(fill, 0, cancellationToken);

	private async ValueTask SendOrderSnapshot(long originId, CancellationToken cancellationToken,
		OrderStatusMessage filter = null)
	{
		var left = filter?.Count ?? long.MaxValue;
		foreach (var update in (await _client.GetOrdersAsync(cancellationToken))
			.OrderBy(item => FubonNeoExtensions.ParseFubonTradeTime(item.Date, item.LastTime)))
		{
			var time = FubonNeoExtensions.ParseFubonTradeTime(update.Date, update.LastTime) ?? CurrentTime;
			if (filter?.From is DateTime from && time < NormalizeUtc(from))
				continue;
			if (filter?.To is DateTime to && time > NormalizeUtc(to))
				continue;
			if (filter != null && !filter.PortfolioName.IsEmpty() &&
				!update.PortfolioName.EqualsIgnoreCase(filter.PortfolioName))
				continue;
			await ProcessOrder(update, originId, true, cancellationToken);
			if (--left <= 0)
				break;
		}

		if (left > 0)
		{
			foreach (var fill in await _client.GetFillsAsync(cancellationToken))
			{
				var time = FubonNeoExtensions.ParseFubonTradeTime(fill.Date, fill.Time) ?? CurrentTime;
				if (filter?.From is DateTime from && time < NormalizeUtc(from))
					continue;
				if (filter?.To is DateTime to && time > NormalizeUtc(to))
					continue;
				if (filter != null && !filter.PortfolioName.IsEmpty() &&
					!fill.PortfolioName.EqualsIgnoreCase(filter.PortfolioName))
					continue;
				await ProcessFill(fill, originId, cancellationToken);
				if (--left <= 0)
					break;
			}
		}
		_lastOrderRefresh = CurrentTime;
	}

	private async ValueTask SendPortfolioSnapshot(long originId, string portfolioName,
		CancellationToken cancellationToken)
	{
		var accounts = _client.Accounts.AsEnumerable();
		if (!portfolioName.IsEmpty())
			accounts = accounts.Where(item => item.PortfolioName.EqualsIgnoreCase(portfolioName));
		var selected = accounts.ToArray();
		if (!portfolioName.IsEmpty() && selected.Length == 0)
			throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
		foreach (var account in selected)
		{
			await SendOutMessageAsync(new PortfolioMessage
			{
				OriginalTransactionId = originId,
				PortfolioName = account.PortfolioName,
				BoardCode = account.IsFutures ? "TAIFEX" : "TWSE",
			}, cancellationToken);
		}

		foreach (var cash in await _client.GetCashAsync(portfolioName, cancellationToken))
		{
			var blocked = cash.BlockedValue ?? (cash.AvailableValue == null
				? null : Math.Max(0, cash.CurrentValue - cash.AvailableValue.Value));
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originId,
				PortfolioName = cash.PortfolioName,
				SecurityId = SecurityId.Money,
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, cash.CurrentValue, true)
			.TryAdd(PositionChangeTypes.BlockedValue, blocked, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, cash.UnrealizedPnL, true), cancellationToken);
		}

		foreach (var position in await _client.GetPositionsAsync(portfolioName, cancellationToken))
		{
			var security = ResolveSecurity(position.Symbol, position.IsFutures, position.IsOption ? 2 : null);
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originId,
				PortfolioName = position.PortfolioName,
				SecurityId = security.ToSecurityId(),
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, position.CurrentValue, true)
			.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice, true)
			.TryAdd(PositionChangeTypes.CurrentPrice, position.CurrentPrice, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnL, true)
			.TryAdd(PositionChangeTypes.BlockedValue, position.BlockedValue, true), cancellationToken);
		}
		_lastPortfolioRefresh = CurrentTime;
	}

	private async ValueTask ProcessOrder(FubonNeoOrderUpdate update, long originId, bool isLookup,
		CancellationToken cancellationToken)
	{
		if (update?.OrderId.IsEmpty() != false && update?.Sequence.IsEmpty() != false)
			return;
		var key = update.OrderId.IsEmpty(update.Sequence);
		_orders.TryGetValue(key, out var tracker);
		if (tracker == null && !update.Sequence.IsEmpty())
			_orders.TryGetValue(update.Sequence, out tracker);
		if (tracker != null)
			CacheOrderTracker(update, tracker);
		var securityId = tracker?.SecurityId ?? ResolveSecurity(update.Symbol, update.IsFutures, update.AssetType).ToSecurityId();
		var side = tracker?.Side ?? ToSide(update.Side);
		var state = ToOrderState(update.Status);
		var orderType = tracker?.OrderType ?? ToOrderType(update.PriceType);
		var timeInForce = tracker?.TimeInForce ?? ToTimeInForce(update.TimeInForce);
		var messageOrigin = isLookup ? originId : originId != 0 ? originId :
			tracker?.TransactionId is > 0 ? tracker.TransactionId : _orderStatusSubscriptionId;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = messageOrigin,
			TransactionId = isLookup ? tracker?.TransactionId ?? 0 : 0,
			OrderStringId = key,
			SecurityId = securityId,
			PortfolioName = tracker?.PortfolioName.IsEmpty(update.PortfolioName),
			OrderType = orderType,
			Side = side,
			TimeInForce = timeInForce,
			OrderPrice = update.Price ?? 0,
			OrderVolume = update.Volume,
			Balance = Math.Max(0, update.Volume - update.FilledVolume),
			AveragePrice = update.FilledVolume > 0 && update.FilledMoney != null
				? update.FilledMoney / update.FilledVolume
				: null,
			OrderState = state,
			ServerTime = FubonNeoExtensions.ParseFubonTradeTime(update.Date, update.LastTime) ?? CurrentTime,
			Condition = tracker?.Condition ?? CreateCondition(update),
			Error = state == OrderStates.Failed || !update.Error.IsEmpty()
				? new InvalidOperationException(update.Error.IsEmpty($"Fubon order status {update.Status}."))
				: null,
		}, cancellationToken);
	}

	private async ValueTask ProcessFill(FubonNeoFillUpdate fill, long originId,
		CancellationToken cancellationToken)
	{
		if (fill == null || fill.Symbol.IsEmpty())
			return;
		var fillId = fill.FillId.IsEmpty(
			$"{fill.OrderId}:{fill.Date}:{fill.Time}:{fill.Price}:{fill.Volume}");
		if (!_tradeIds.TryAdd($"{fill.OrderId}|{fillId}"))
			return;
		_orders.TryGetValue(fill.OrderId, out var tracker);
		if (tracker == null && !fill.Sequence.IsEmpty())
			_orders.TryGetValue(fill.Sequence, out tracker);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originId != 0 ? originId : tracker?.TransactionId ?? _orderStatusSubscriptionId,
			OrderStringId = fill.OrderId,
			TradeStringId = fillId,
			SecurityId = tracker?.SecurityId ?? ResolveSecurity(fill.Symbol, fill.IsFutures,
				fill.IsOption ? 2 : null).ToSecurityId(),
			PortfolioName = tracker?.PortfolioName.IsEmpty(fill.PortfolioName),
			Side = tracker?.Side ?? ToSide(fill.Side),
			TradePrice = fill.Price,
			TradeVolume = fill.Volume,
			ServerTime = FubonNeoExtensions.ParseFubonTradeTime(fill.Date, fill.Time) ?? CurrentTime,
		}, cancellationToken);
	}

	private void CacheOrderTracker(FubonNeoOrderUpdate update, OrderTracker tracker)
	{
		if (!update.OrderId.IsEmpty())
			_orders[update.OrderId] = tracker;
		if (!update.Sequence.IsEmpty())
			_orders[update.Sequence] = tracker;
	}

	private string ResolveOrderId(string orderId, long transactionId)
	{
		if (!orderId.IsEmpty())
			return orderId;
		if (_transactionOrders.TryGetValue(transactionId, out orderId) && !orderId.IsEmpty())
			return orderId;
		throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(transactionId));
	}

	private static FubonNeoOrderCondition CreateCondition(FubonNeoOrderUpdate update)
	{
		Enum.TryParse<FubonNeoStockMarketTypes>(update.MarketType, true, out var stockMarketType);
		Enum.TryParse<FubonNeoStockOrderTypes>(update.OrderType, true, out var stockOrderType);
		var futuresType = update.OrderType.EqualsIgnoreCase("FdayTrade")
			? FubonNeoFuturesOrderTypes.DayTrade
			: Enum.TryParse<FubonNeoFuturesOrderTypes>(update.OrderType, true, out var parsedFutures)
				? parsedFutures : FubonNeoFuturesOrderTypes.Auto;
		Enum.TryParse<FubonNeoPriceTypes>(update.PriceType, true, out var priceType);
		return new()
		{
			StockMarketType = update.MarketType.IsEmpty() ? FubonNeoStockMarketTypes.Common : stockMarketType,
			StockOrderType = update.IsFutures || update.OrderType.IsEmpty() ? FubonNeoStockOrderTypes.Stock : stockOrderType,
			FuturesOrderType = futuresType,
			PriceType = update.PriceType.IsEmpty() ? FubonNeoPriceTypes.Auto : priceType,
			IsAfterHours = update.MarketType?.EndsWith("Night", StringComparison.OrdinalIgnoreCase) == true,
			UserTag = update.UserTag,
		};
	}

	private static OrderStates ToOrderState(int? status)
		=> status switch
		{
			0 or 4 or 8 => OrderStates.Pending,
			10 => OrderStates.Active,
			30 or 40 or 50 => OrderStates.Done,
			9 or 90 => OrderStates.Failed,
			_ => OrderStates.Active,
		};

	private static Sides ToSide(string value)
		=> value.EqualsIgnoreCase("Sell") ? Sides.Sell : Sides.Buy;

	private static OrderTypes ToOrderType(string value)
		=> value.EqualsIgnoreCase("Market") || value.EqualsIgnoreCase("RangeMarket")
			? OrderTypes.Market : OrderTypes.Limit;

	private static TimeInForce ToTimeInForce(string value)
		=> value?.ToUpperInvariant() switch
		{
			"IOC" => TimeInForce.CancelBalance,
			"FOK" => TimeInForce.MatchOrCancel,
			_ => TimeInForce.PutInQueue,
		};

	private static long ToOrderVolume(decimal volume, string parameterName)
	{
		if (volume <= 0 || volume != decimal.Truncate(volume) || volume > long.MaxValue)
			throw new ArgumentOutOfRangeException(parameterName, volume,
				"Fubon order quantities must be positive whole numbers within Int64 range.");
		return decimal.ToInt64(volume);
	}

	private static void ValidateUserTag(string value)
	{
		if (value.IsEmpty())
			return;
		if (value.Length > 10 || value.Any(character =>
			character is not (>= 'A' and <= 'Z') and not (>= 'a' and <= 'z') and not (>= '0' and <= '9')))
			throw new ArgumentOutOfRangeException(nameof(value), value,
				"Fubon user-defined values must contain only ASCII letters and digits and may not exceed ten characters.");
	}
}
