namespace StockSharp.Zerodha;

public partial class ZerodhaMessageAdapter
{
	private sealed class PortfolioPosition
	{
		public SecurityId SecurityId { get; init; }
		public decimal Quantity { get; init; }
		public decimal AveragePrice { get; init; }
		public decimal CurrentPrice { get; init; }
		public decimal RealizedPnL { get; init; }
		public decimal UnrealizedPnL { get; init; }
	}

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsurePortfolio(regMsg.PortfolioName);
		ValidateQuantity(regMsg.Volume);
		var condition = regMsg.Condition as ZerodhaOrderCondition ?? new();
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		var nativeOrderType = GetNativeOrderType(orderType, regMsg.Price, condition.TriggerPrice);
		var validity = GetValidity(regMsg.TimeInForce, condition.ValidityTtl);
		ValidateCondition(condition, nativeOrderType, regMsg.Volume);
		var variety = condition.Variety.ToNative();
		var result = await GetRest().PlaceOrder(variety, new()
		{
			TradingSymbol = regMsg.SecurityId.SecurityCode.ThrowIfEmpty(nameof(regMsg.SecurityId.SecurityCode)),
			Exchange = regMsg.SecurityId.BoardCode.ThrowIfEmpty(nameof(regMsg.SecurityId.BoardCode)),
			TransactionType = regMsg.Side == Sides.Buy ? "BUY" : "SELL",
			OrderType = nativeOrderType,
			Quantity = regMsg.Volume,
			Product = (condition.Product ?? DefaultProduct).ToNative(),
			Price = nativeOrderType is "MARKET" or "SL-M" ? null : regMsg.Price,
			TriggerPrice = nativeOrderType is "SL" or "SL-M" ? condition.TriggerPrice : null,
			DisclosedQuantity = condition.DisclosedQuantity,
			Validity = validity,
			ValidityTtl = validity == "TTL" ? condition.ValidityTtl : null,
			MarketProtection = nativeOrderType is "MARKET" or "SL-M" ? condition.MarketProtection : null,
			IsAutoSlice = condition.IsAutoSlice ? true : null,
			Tag = regMsg.Comment.IsEmpty() ? null : regMsg.Comment[..Math.Min(20, regMsg.Comment.Length)],
		}, cancellationToken);
		var orderId = result?.OrderId;
		if (orderId.IsEmpty())
			throw new InvalidDataException("Zerodha order placement returned no order id.");

		_orders[orderId] = new()
		{
			TransactionId = regMsg.TransactionId,
			SecurityId = regMsg.SecurityId,
			PortfolioName = PortfolioName,
			Side = regMsg.Side,
			OrderType = orderType,
			Price = regMsg.Price,
			Volume = regMsg.Volume,
			Condition = condition,
		};
		_orderFillVolumes[orderId] = 0;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			TransactionId = regMsg.TransactionId,
			OrderId = orderId.ToLongId(),
			OrderStringId = orderId,
			SecurityId = regMsg.SecurityId,
			PortfolioName = PortfolioName,
			Side = regMsg.Side,
			OrderType = orderType,
			OrderPrice = regMsg.Price,
			OrderVolume = regMsg.Volume,
			Balance = regMsg.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = DateTime.UtcNow,
			TimeInForce = regMsg.TimeInForce,
			Condition = condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		EnsurePortfolio(replaceMsg.PortfolioName);
		ValidateQuantity(replaceMsg.Volume);
		var orderId = GetOrderId(replaceMsg.OldOrderStringId, replaceMsg.OldOrderId,
			replaceMsg.OriginalTransactionId);
		_orders.TryGetValue(orderId, out var tracker);
		var condition = replaceMsg.Condition as ZerodhaOrderCondition ?? tracker?.Condition ?? new();
		var orderType = replaceMsg.OrderType ?? tracker?.OrderType ?? OrderTypes.Limit;
		var nativeOrderType = GetNativeOrderType(orderType, replaceMsg.Price, condition.TriggerPrice);
		var validity = GetValidity(replaceMsg.TimeInForce, condition.ValidityTtl);
		ValidateCondition(condition, nativeOrderType, replaceMsg.Volume);
		var variety = (tracker?.Condition?.Variety ?? condition.Variety).ToNative();

		await GetRest().ModifyOrder(variety, orderId, new()
		{
			OrderType = nativeOrderType,
			Quantity = replaceMsg.Volume,
			Price = nativeOrderType is "MARKET" or "SL-M" ? null : replaceMsg.Price,
			TriggerPrice = nativeOrderType is "SL" or "SL-M" ? condition.TriggerPrice : null,
			DisclosedQuantity = condition.DisclosedQuantity,
			Validity = validity,
			ValidityTtl = validity == "TTL" ? condition.ValidityTtl : null,
		}, cancellationToken);

		if (tracker != null)
		{
			tracker.OrderType = orderType;
			tracker.Price = replaceMsg.Price;
			tracker.Volume = replaceMsg.Volume;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePortfolio(cancelMsg.PortfolioName);
		var orderId = GetOrderId(cancelMsg.OrderStringId, cancelMsg.OrderId,
			cancelMsg.OriginalTransactionId);
		_orders.TryGetValue(orderId, out var tracker);
		var variety = tracker?.Condition?.Variety.ToNative() ?? ZerodhaOrderVarieties.Regular.ToNative();
		await GetRest().CancelOrder(variety, orderId, cancellationToken);
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

		EnsurePortfolio(statusMsg.PortfolioName);
		var left = statusMsg.Count ?? long.MaxValue;
		foreach (var order in (await GetRest().GetOrders(cancellationToken))
			.OrderBy(o => o.OrderTimestamp.ParseKiteTime()))
		{
			var time = order.OrderTimestamp.ParseKiteTime();
			if (statusMsg.From != null && time < statusMsg.From.Value.ToUniversalTime())
				continue;
			if (statusMsg.To != null && time > statusMsg.To.Value.ToUniversalTime())
				continue;
			await ProcessOrder(order, statusMsg.TransactionId, true, cancellationToken);
			if (--left <= 0)
				break;
		}

		foreach (var trade in await GetRest().GetTrades(cancellationToken))
			await ProcessTrade(trade, statusMsg.TransactionId, cancellationToken);

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
				_portfolioSubscriptionId = 0;
			return;
		}

		EnsurePortfolio(lookupMsg.PortfolioName);
		await SendPortfolioSnapshot(lookupMsg.TransactionId, cancellationToken);
		if (lookupMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
		else
		{
			_portfolioSubscriptionId = lookupMsg.TransactionId;
			_lastPortfolioRefresh = DateTime.UtcNow;
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		}
	}

	private async ValueTask SendPortfolioSnapshot(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = PortfolioName,
			BoardCode = "NSE",
		}, cancellationToken);

		var margins = await GetRest().GetMargins(cancellationToken);
		var segments = new[] { margins?.Equity, margins?.Commodity }
			.Where(segment => segment?.IsEnabled == true).ToArray();
		if (segments.Length > 0)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = PortfolioName,
				SecurityId = SecurityId.Money,
				ServerTime = DateTime.UtcNow,
			}
			.TryAdd(PositionChangeTypes.BeginValue,
				segments.Sum(segment => segment.Available?.OpeningBalance ?? 0), true)
			.TryAdd(PositionChangeTypes.CurrentValue, segments.Sum(segment => segment.Net), true)
			.TryAdd(PositionChangeTypes.BlockedValue,
				segments.Sum(segment => segment.Utilised?.Debits ?? 0), true)
			.TryAdd(PositionChangeTypes.RealizedPnL,
				segments.Sum(segment => segment.Utilised?.RealizedMtm ?? 0), true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL,
				segments.Sum(segment => segment.Utilised?.UnrealizedMtm ?? 0), true), cancellationToken);
		}

		var snapshots = new List<PortfolioPosition>();
		foreach (var holding in await GetRest().GetHoldings(cancellationToken))
		{
			if (holding?.TradingSymbol.IsEmpty() != false || holding.Exchange.IsEmpty())
				continue;
			snapshots.Add(new()
			{
				SecurityId = holding.TradingSymbol.ToSecurityId(holding.Exchange, holding.InstrumentToken),
				Quantity = holding.Quantity + holding.T1Quantity,
				AveragePrice = holding.AveragePrice,
				CurrentPrice = holding.LastPrice,
				UnrealizedPnL = holding.PnL,
			});
		}
		foreach (var position in await GetRest().GetNetPositions(cancellationToken))
		{
			if (position?.TradingSymbol.IsEmpty() != false || position.Exchange.IsEmpty())
				continue;
			snapshots.Add(new()
			{
				SecurityId = position.TradingSymbol.ToSecurityId(position.Exchange, position.InstrumentToken),
				Quantity = position.Quantity,
				AveragePrice = position.AveragePrice,
				CurrentPrice = position.LastPrice,
				RealizedPnL = position.Realized,
				UnrealizedPnL = position.Unrealized,
			});
		}

		foreach (var group in snapshots.GroupBy(item =>
			$"{item.SecurityId.BoardCode}:{item.SecurityId.SecurityCode}", StringComparer.OrdinalIgnoreCase))
		{
			var items = group.ToArray();
			var absolute = items.Sum(item => Math.Abs(item.Quantity));
			var average = absolute == 0 ? (decimal?)null :
				items.Sum(item => Math.Abs(item.Quantity) * item.AveragePrice) / absolute;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = PortfolioName,
				SecurityId = items[0].SecurityId,
				ServerTime = DateTime.UtcNow,
			}
			.Add(PositionChangeTypes.CurrentValue, items.Sum(item => item.Quantity))
			.TryAdd(PositionChangeTypes.AveragePrice, average, true)
			.TryAdd(PositionChangeTypes.CurrentPrice,
				items.Select(item => item.CurrentPrice).FirstOrDefault(price => price > 0), true)
			.TryAdd(PositionChangeTypes.RealizedPnL, items.Sum(item => item.RealizedPnL), true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, items.Sum(item => item.UnrealizedPnL), true),
				cancellationToken);
		}
	}

	private ValueTask ProcessOrderUpdate(KiteOrder order, CancellationToken cancellationToken)
	{
		if (order?.OrderId.IsEmpty() != false)
			return default;
		if (_orderStatusSubscriptionId == 0 && !_orders.ContainsKey(order.OrderId))
			return default;
		_lastPortfolioRefresh = default;
		return ProcessOrder(order, 0, false, cancellationToken);
	}

	private async ValueTask ProcessOrder(KiteOrder order, long lookupTransactionId, bool isLookup,
		CancellationToken cancellationToken)
	{
		if (order?.OrderId.IsEmpty() != false || order.TradingSymbol.IsEmpty() || order.Exchange.IsEmpty())
			return;

		_orders.TryGetValue(order.OrderId, out var tracker);
		var originalId = isLookup
			? lookupTransactionId
			: tracker?.TransactionId is > 0 ? tracker.TransactionId : _orderStatusSubscriptionId;
		if (originalId == 0)
			return;
		var condition = tracker?.Condition ?? new ZerodhaOrderCondition
		{
			Product = order.Product.ToProduct(),
			Variety = order.Variety.ToVariety(),
			TriggerPrice = order.TriggerPrice > 0 ? order.TriggerPrice : null,
			DisclosedQuantity = order.DisclosedQuantity > 0 ? order.DisclosedQuantity : null,
			ValidityTtl = order.ValidityTtl,
			MarketProtection = order.MarketProtection,
		};
		var securityId = tracker?.SecurityId ?? order.TradingSymbol.ToSecurityId(order.Exchange,
			order.InstrumentToken);
		var side = tracker?.Side ?? (order.TransactionType.EqualsIgnoreCase("SELL") ? Sides.Sell : Sides.Buy);
		var orderType = tracker?.OrderType ?? order.OrderType.ToOrderType();
		var state = order.Status.ToOrderState();
		var serverTime = order.ExchangeUpdateTimestamp.ParseKiteTime() ??
			order.ExchangeTimestamp.ParseKiteTime() ?? order.OrderTimestamp.ParseKiteTime() ?? DateTime.UtcNow;

		if (tracker == null)
		{
			tracker = new()
			{
				TransactionId = 0,
				SecurityId = securityId,
				PortfolioName = PortfolioName,
				Side = side,
				OrderType = orderType,
				Price = order.Price,
				Volume = order.Quantity,
				Condition = condition,
			};
			_orders[order.OrderId] = tracker;
		}

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalId,
			TransactionId = isLookup ? 0 : tracker.TransactionId,
			OrderId = order.OrderId.ToLongId(),
			OrderStringId = order.OrderId,
			SecurityId = securityId,
			PortfolioName = PortfolioName,
			Side = side,
			OrderType = orderType,
			OrderPrice = order.Price,
			OrderVolume = order.Quantity,
			Balance = order.PendingQuantity,
			AveragePrice = order.AveragePrice > 0 ? order.AveragePrice : null,
			OrderState = state,
			ServerTime = serverTime,
			TimeInForce = order.Validity.ToTimeInForce(),
			Condition = condition,
			Error = state == OrderStates.Failed
				? new InvalidOperationException(order.StatusMessage.IsEmpty(order.RawStatusMessage)
					.IsEmpty("Zerodha rejected the order."))
				: null,
		}, cancellationToken);

		var previousFilled = _orderFillVolumes.TryGetValue2(order.OrderId);
		_orderFillVolumes[order.OrderId] = order.FilledQuantity;
		tracker.ReportedFilled = order.FilledQuantity;
		if (isLookup || order.FilledQuantity <= previousFilled)
			return;

		var fillId = $"{order.OrderId}:{order.FilledQuantity.ToString(CultureInfo.InvariantCulture)}";
		if (!_reportedTrades.TryAdd(fillId))
			return;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originalId,
			OrderId = order.OrderId.ToLongId(),
			OrderStringId = order.OrderId,
			TradeStringId = fillId,
			SecurityId = securityId,
			PortfolioName = PortfolioName,
			Side = side,
			TradePrice = order.AveragePrice,
			TradeVolume = order.FilledQuantity - previousFilled,
			ServerTime = serverTime,
		}, cancellationToken);
	}

	private ValueTask ProcessTrade(KiteTrade trade, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (trade?.TradeId.IsEmpty() != false || !_reportedTrades.TryAdd(trade.TradeId))
			return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originalTransactionId,
			OrderId = trade.OrderId.ToLongId(),
			OrderStringId = trade.OrderId,
			TradeId = trade.TradeId.ToLongId(),
			TradeStringId = trade.TradeId,
			SecurityId = trade.TradingSymbol.ToSecurityId(trade.Exchange, trade.InstrumentToken),
			PortfolioName = PortfolioName,
			Side = trade.TransactionType.EqualsIgnoreCase("SELL") ? Sides.Sell : Sides.Buy,
			TradePrice = trade.AveragePrice,
			TradeVolume = trade.Quantity,
			ServerTime = trade.ExchangeTimestamp.ParseKiteTime() ??
				trade.FillTimestamp.ParseKiteTime() ?? DateTime.UtcNow,
		}, cancellationToken);
	}

	private void EnsurePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() && !portfolioName.EqualsIgnoreCase(PortfolioName))
			throw new InvalidOperationException($"Zerodha portfolio '{portfolioName}' is not connected.");
	}

	private static void ValidateQuantity(decimal quantity)
	{
		if (quantity <= 0 || decimal.Truncate(quantity) != quantity)
			throw new ArgumentOutOfRangeException(nameof(quantity), quantity,
				"Zerodha quantity must be a positive whole number.");
	}

	private static void ValidateCondition(ZerodhaOrderCondition condition, string nativeOrderType,
		decimal quantity)
	{
		if (nativeOrderType is "SL" or "SL-M" && condition.TriggerPrice is not > 0)
			throw new InvalidOperationException("Zerodha stop orders require a positive trigger price.");
		if (condition.DisclosedQuantity is { } disclosedQuantity &&
			(disclosedQuantity <= 0 || disclosedQuantity > quantity))
			throw new InvalidOperationException("Zerodha disclosed quantity must be positive and not exceed order quantity.");
		if (condition.ValidityTtl is { } validityTtl && validityTtl <= 0)
			throw new InvalidOperationException("Zerodha TTL validity must be a positive number of minutes.");
		if (condition.MarketProtection is { } marketProtection &&
			(marketProtection < -1 || marketProtection > 100))
			throw new InvalidOperationException("Zerodha market protection must be -1 or between 0 and 100 percent.");
	}

	private static string GetNativeOrderType(OrderTypes orderType, decimal price, decimal? triggerPrice)
		=> orderType switch
		{
			OrderTypes.Market => "MARKET",
			OrderTypes.Limit when price > 0 => "LIMIT",
			OrderTypes.Limit => throw new InvalidOperationException("Zerodha limit orders require a positive price."),
			OrderTypes.Conditional when price > 0 && triggerPrice > 0 => "SL",
			OrderTypes.Conditional when triggerPrice > 0 => "SL-M",
			OrderTypes.Conditional => throw new InvalidOperationException(
				"Zerodha stop orders require a positive trigger price."),
			_ => throw new NotSupportedException($"Zerodha does not support StockSharp order type '{orderType}'."),
		};

	private static string GetValidity(TimeInForce? timeInForce, int? validityTtl)
	{
		if (validityTtl != null)
			return "TTL";
		return timeInForce switch
		{
			null or TimeInForce.PutInQueue => "DAY",
			TimeInForce.CancelBalance => "IOC",
			_ => throw new NotSupportedException($"Zerodha does not support time in force '{timeInForce}'."),
		};
	}

	private static string GetOrderId(string stringId, long? numericId, long transactionId)
		=> stringId.IsEmpty(numericId?.ToString(CultureInfo.InvariantCulture))
			.ThrowIfEmpty(LocalizedStrings.OrderNoExchangeId.Put(transactionId));
}
