namespace StockSharp.MiraeSharekhan;

public partial class MiraeSharekhanMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsurePortfolio(regMsg.PortfolioName);
		ValidateQuantity(regMsg.Volume);
		var condition = regMsg.Condition as MiraeSharekhanOrderCondition ?? new();
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		var request = await CreateOrderRequest(regMsg.SecurityId, regMsg.Side, regMsg.Volume,
			regMsg.Price, orderType, regMsg.TimeInForce, condition, "NEW", null, cancellationToken);
		var response = await GetRest().SubmitOrder(request, cancellationToken);
		var orderId = response.GetOrderId();
		if (orderId.IsEmpty())
			throw new InvalidDataException("Mirae Asset Sharekhan order placement returned no order id.");

		_orders[orderId] = new()
		{
			TransactionId = regMsg.TransactionId,
			Request = request,
			SecurityId = regMsg.SecurityId,
			OrderType = orderType,
		};
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			TransactionId = regMsg.TransactionId,
			OrderId = ToLongId(orderId),
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
		var tracker = await GetOrderTracker(orderId, cancellationToken);
		var condition = replaceMsg.Condition as MiraeSharekhanOrderCondition ??
			ToCondition(tracker.Request);
		var orderType = replaceMsg.OrderType ?? tracker.OrderType;
		var securityId = replaceMsg.SecurityId.SecurityCode.IsEmpty()
			? tracker.SecurityId : replaceMsg.SecurityId;
		var side = tracker.Request.TransactionType.ToSide();
		var request = await CreateOrderRequest(securityId, side, replaceMsg.Volume,
			replaceMsg.Price, orderType, replaceMsg.TimeInForce, condition, "MODIFY", orderId,
			cancellationToken);
		await GetRest().SubmitOrder(request, cancellationToken);
		tracker.Request = request;
		tracker.OrderType = orderType;
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePortfolio(cancelMsg.PortfolioName);
		var orderId = GetOrderId(cancelMsg.OrderStringId, cancelMsg.OrderId,
			cancelMsg.OriginalTransactionId);
		var tracker = await GetOrderTracker(orderId, cancellationToken);
		var request = CopyRequest(tracker.Request);
		request.OrderId = orderId;
		request.RequestType = "CANCEL";
		await GetRest().SubmitOrder(request, cancellationToken);
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
		foreach (var order in (await GetRest().GetOrders(CustomerId, cancellationToken))
			.OrderBy(order => order.GetTime()))
		{
			var time = order.GetTime();
			if (statusMsg.From != null && time < statusMsg.From.Value.ToUniversalTime())
				continue;
			if (statusMsg.To != null && time > statusMsg.To.Value.ToUniversalTime())
				continue;
			await ProcessOrder(order, statusMsg.TransactionId, true, cancellationToken);
			if (--left <= 0)
				break;
		}
		foreach (var trade in await GetRest().GetTrades(CustomerId, cancellationToken))
			await ProcessTrade(trade, statusMsg.TransactionId, cancellationToken);

		if (statusMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
		else
		{
			_orderStatusSubscriptionId = statusMsg.TransactionId;
			_lastOrderRefresh = DateTime.UtcNow;
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

	private async ValueTask RefreshOrderStatus(CancellationToken cancellationToken)
	{
		_lastOrderRefresh = DateTime.UtcNow;
		foreach (var order in await GetRest().GetOrders(CustomerId, cancellationToken))
			await ProcessOrder(order, _orderStatusSubscriptionId, false, cancellationToken);
		foreach (var trade in await GetRest().GetTrades(CustomerId, cancellationToken))
			await ProcessTrade(trade, _orderStatusSubscriptionId, cancellationToken);
	}

	private ValueTask SendPortfolioSnapshot(CancellationToken cancellationToken)
		=> SendPortfolioSnapshot(_portfolioSubscriptionId, cancellationToken);

	private async ValueTask SendPortfolioSnapshot(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		_lastPortfolioRefresh = DateTime.UtcNow;
		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = PortfolioName,
			BoardCode = "NC",
		}, cancellationToken);

		var funds = await GetRest().GetFunds("NC", CustomerId, cancellationToken);
		if (funds != null)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = PortfolioName,
				SecurityId = SecurityId.Money,
				ServerTime = DateTime.UtcNow,
			}
			.TryAdd(PositionChangeTypes.BeginValue, funds.OpeningBalance, true)
			.TryAdd(PositionChangeTypes.CurrentValue, funds.GetAvailable(), true)
			.TryAdd(PositionChangeTypes.BlockedValue, funds.GetBlocked(), true), cancellationToken);
		}

		foreach (var holding in await GetRest().GetHoldings(CustomerId, cancellationToken))
		{
			if (holding == null || holding.ScripCode <= 0)
				continue;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = PortfolioName,
				SecurityId = CreateSecurityId(holding.Exchange.IsEmpty("NC"), holding.ScripCode,
					holding.TradingSymbol),
				ServerTime = DateTime.UtcNow,
			}
			.Add(PositionChangeTypes.CurrentValue, holding.GetQuantity())
			.TryAdd(PositionChangeTypes.AveragePrice, holding.AveragePrice, true)
			.TryAdd(PositionChangeTypes.CurrentPrice, holding.LastPrice, true), cancellationToken);
		}

		foreach (var position in await GetRest().GetTrades(CustomerId, cancellationToken))
		{
			var quantity = position.NetQuantity ??
				((position.BuyQuantity ?? 0) - (position.SellQuantity ?? 0));
			if (position.ScripCode <= 0 || quantity == 0 && position.NetQuantity == null &&
				position.BuyQuantity == null && position.SellQuantity == null)
				continue;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = PortfolioName,
				SecurityId = CreateSecurityId(position.Exchange, position.ScripCode,
					position.TradingSymbol),
				ServerTime = position.GetTime() ?? DateTime.UtcNow,
			}
			.Add(PositionChangeTypes.CurrentValue, quantity)
			.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice, true)
			.TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedPnL, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnL, true), cancellationToken);
		}
	}

	private async ValueTask ProcessOrder(MiraeSharekhanOrder order, long originalTransactionId,
		bool isLookup, CancellationToken cancellationToken)
	{
		if (order?.OrderId.IsEmpty() != false || order.ScripCode <= 0)
			return;
		_orders.TryGetValue(order.OrderId, out var tracker);
		var securityId = tracker?.SecurityId ?? CreateSecurityId(order.Exchange, order.ScripCode,
			order.TradingSymbol);
		var state = order.GetStatus().ToOrderState();
		var filled = order.GetFilledQuantity();
		var condition = tracker == null ? ToCondition(order) : ToCondition(tracker.Request);
		var orderType = tracker?.OrderType ?? GetOrderType(order.Price, order.TriggerPrice);
		var side = tracker?.Request.TransactionType.ToSide() ?? order.TransactionType.ToSide();
		var origin = isLookup ? originalTransactionId : tracker?.TransactionId is > 0
			? tracker.TransactionId : originalTransactionId;
		if (tracker == null)
		{
			tracker = new()
			{
				TransactionId = 0,
				Request = CreateRequest(order),
				SecurityId = securityId,
				OrderType = orderType,
				ReportedFilled = filled,
			};
			_orders[order.OrderId] = tracker;
		}

		var serverTime = order.GetTime() ?? DateTime.UtcNow;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = origin,
			TransactionId = isLookup ? 0 : tracker.TransactionId,
			OrderId = ToLongId(order.OrderId),
			OrderStringId = order.OrderId,
			SecurityId = securityId,
			PortfolioName = PortfolioName,
			Side = side,
			OrderType = orderType,
			OrderPrice = order.Price,
			OrderVolume = order.Quantity,
			Balance = order.PendingQuantity ?? Math.Max(0, order.Quantity - filled),
			AveragePrice = order.AveragePrice,
			OrderState = state,
			ServerTime = serverTime,
			Condition = condition,
			Error = state == OrderStates.Failed
				? new InvalidOperationException(order.RejectionReason.IsEmpty(order.Message)
					.IsEmpty("Mirae Asset Sharekhan rejected the order."))
				: null,
		}, cancellationToken);

		var previousFilled = tracker.ReportedFilled;
		tracker.ReportedFilled = filled;
		if (isLookup || filled <= previousFilled)
			return;
		var tradeId = $"{order.OrderId}:{filled.ToString(CultureInfo.InvariantCulture)}";
		if (!_reportedTrades.TryAdd(tradeId))
			return;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = origin,
			OrderId = ToLongId(order.OrderId),
			OrderStringId = order.OrderId,
			TradeStringId = tradeId,
			SecurityId = securityId,
			PortfolioName = PortfolioName,
			Side = side,
			TradePrice = order.AveragePrice ?? order.Price,
			TradeVolume = filled - previousFilled,
			ServerTime = serverTime,
		}, cancellationToken);
	}

	private ValueTask ProcessTrade(MiraeSharekhanTrade trade, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var tradeId = trade?.GetTradeId();
		if (tradeId.IsEmpty() || trade.ScripCode <= 0 || !_reportedTrades.TryAdd(tradeId))
			return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originalTransactionId,
			OrderId = ToLongId(trade.OrderId),
			OrderStringId = trade.OrderId,
			TradeId = ToLongId(tradeId),
			TradeStringId = tradeId,
			SecurityId = CreateSecurityId(trade.Exchange, trade.ScripCode, trade.TradingSymbol),
			PortfolioName = PortfolioName,
			Side = trade.TransactionType.ToSide(),
			TradePrice = trade.GetPrice(),
			TradeVolume = trade.GetQuantity(),
			ServerTime = trade.GetTime() ?? DateTime.UtcNow,
		}, cancellationToken);
	}

	private async Task<OrderTracker> GetOrderTracker(string orderId,
		CancellationToken cancellationToken)
	{
		if (_orders.TryGetValue(orderId, out var tracker))
			return tracker;
		var order = (await GetRest().GetOrders(CustomerId, cancellationToken))
			.FirstOrDefault(item => item.OrderId.EqualsIgnoreCase(orderId))
			?? throw new InvalidOperationException($"Mirae Asset Sharekhan order '{orderId}' was not found.");
		tracker = new()
		{
			TransactionId = 0,
			Request = CreateRequest(order),
			SecurityId = CreateSecurityId(order.Exchange, order.ScripCode, order.TradingSymbol),
			OrderType = GetOrderType(order.Price, order.TriggerPrice),
			ReportedFilled = order.GetFilledQuantity(),
		};
		_orders[orderId] = tracker;
		return tracker;
	}

	private async Task<MiraeSharekhanOrderRequest> CreateOrderRequest(SecurityId securityId,
		Sides side, decimal volume, decimal price, OrderTypes orderType, TimeInForce? timeInForce,
		MiraeSharekhanOrderCondition condition, string requestType, string orderId,
		CancellationToken cancellationToken)
	{
		if (timeInForce is not null and not TimeInForce.PutInQueue)
			throw new NotSupportedException("Mirae Asset Sharekhan Trading API documents GFD validity only.");
		if (orderType == OrderTypes.Limit && price <= 0)
			throw new InvalidOperationException("Mirae Asset Sharekhan limit orders require a positive price.");
		if (orderType == OrderTypes.Conditional && condition.TriggerPrice is not > 0)
			throw new InvalidOperationException("Mirae Asset Sharekhan stop orders require a positive trigger price.");
		if (condition.DisclosedVolume is { } disclosed && (disclosed <= 0 || disclosed > volume))
			throw new InvalidOperationException("Disclosed volume must be positive and not exceed order volume.");

		var instrument = await ResolveInstrument(securityId, cancellationToken);
		var instrumentType = condition.InstrumentType ??
			instrument.InstrumentType.ToInstrumentType() ?? MiraeSharekhanInstrumentTypes.Equity;
		var expiry = condition.ExpiryDate ?? instrument.GetExpiryDate();
		var optionType = condition.OptionType ?? instrument.OptionType.ToOptionType();
		return new()
		{
			OrderId = orderId,
			CustomerId = CustomerId,
			ScripCode = instrument.GetScripCode(),
			TradingSymbol = instrument.GetSymbol(),
			Exchange = instrument.Exchange,
			TransactionType = side.ToNative(),
			Quantity = volume,
			DisclosedQuantity = condition.DisclosedVolume ?? 0,
			Price = orderType == OrderTypes.Market ? 0 : price,
			TriggerPrice = condition.TriggerPrice ?? 0,
			RmsCode = condition.RmsCode.IsEmpty("ANY"),
			AfterHour = condition.IsAfterHours ? "Y" : "N",
			ChannelUser = CustomerId,
			RequestType = requestType,
			ProductType = (condition.Product ?? DefaultProduct).ToNative(),
			InstrumentType = instrumentType.ToNative(),
			StrikePrice = condition.StrikePrice ?? instrument.StrikePrice ?? -1,
			Expiry = expiry?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
			OptionType = optionType switch
			{
				OptionTypes.Call => "CE",
				OptionTypes.Put => "PE",
				_ => "XX",
			},
		};
	}

	private static MiraeSharekhanOrderRequest CreateRequest(MiraeSharekhanOrder order)
		=> new()
		{
			OrderId = order.OrderId,
			CustomerId = order.CustomerId,
			ScripCode = order.ScripCode,
			TradingSymbol = order.TradingSymbol,
			Exchange = order.Exchange,
			TransactionType = order.TransactionType,
			Quantity = order.Quantity,
			DisclosedQuantity = order.DisclosedQuantity,
			Price = order.Price,
			TriggerPrice = order.TriggerPrice,
			RmsCode = "ANY",
			AfterHour = "N",
			ChannelUser = order.CustomerId,
			RequestType = "MODIFY",
			ProductType = order.ProductType.IsEmpty("INVESTMENT"),
			InstrumentType = order.InstrumentType.IsEmpty("EQ"),
			StrikePrice = order.StrikePrice ?? -1,
			Expiry = order.Expiry,
			OptionType = order.OptionType.IsEmpty("XX"),
		};

	private static MiraeSharekhanOrderRequest CopyRequest(MiraeSharekhanOrderRequest request)
		=> new()
		{
			OrderId = request.OrderId,
			CustomerId = request.CustomerId,
			ScripCode = request.ScripCode,
			TradingSymbol = request.TradingSymbol,
			Exchange = request.Exchange,
			TransactionType = request.TransactionType,
			Quantity = request.Quantity,
			DisclosedQuantity = request.DisclosedQuantity,
			Price = request.Price,
			TriggerPrice = request.TriggerPrice,
			RmsCode = request.RmsCode,
			AfterHour = request.AfterHour,
			OrderType = request.OrderType,
			ChannelUser = request.ChannelUser,
			Validity = request.Validity,
			RequestType = request.RequestType,
			ProductType = request.ProductType,
			InstrumentType = request.InstrumentType,
			StrikePrice = request.StrikePrice,
			Expiry = request.Expiry,
			OptionType = request.OptionType,
		};

	private static MiraeSharekhanOrderCondition ToCondition(MiraeSharekhanOrderRequest request)
		=> new()
		{
			Product = request.ProductType.ToProduct(),
			RmsCode = request.RmsCode,
			TriggerPrice = request.TriggerPrice > 0 ? request.TriggerPrice : null,
			DisclosedVolume = request.DisclosedQuantity > 0 ? request.DisclosedQuantity : null,
			IsAfterHours = request.AfterHour.EqualsIgnoreCase("Y"),
			InstrumentType = request.InstrumentType.ToInstrumentType(),
			StrikePrice = request.StrikePrice >= 0 ? request.StrikePrice : null,
			OptionType = request.OptionType.ToOptionType(),
			ExpiryDate = request.Expiry.ParseIndiaTime(),
		};

	private static MiraeSharekhanOrderCondition ToCondition(MiraeSharekhanOrder order)
		=> new()
		{
			Product = order.ProductType.ToProduct(),
			RmsCode = "ANY",
			TriggerPrice = order.TriggerPrice > 0 ? order.TriggerPrice : null,
			DisclosedVolume = order.DisclosedQuantity > 0 ? order.DisclosedQuantity : null,
			InstrumentType = order.InstrumentType.ToInstrumentType(),
			StrikePrice = order.StrikePrice is >= 0 ? order.StrikePrice : null,
			OptionType = order.OptionType.ToOptionType(),
			ExpiryDate = order.Expiry.ParseIndiaTime(),
		};

	private void EnsurePortfolio(string portfolioName)
	{
		CustomerId.ThrowIfEmpty(nameof(CustomerId));
		if (!portfolioName.IsEmpty() && !portfolioName.EqualsIgnoreCase(PortfolioName))
			throw new InvalidOperationException(
				$"Mirae Asset Sharekhan portfolio '{portfolioName}' is not connected.");
	}

	private static void ValidateQuantity(decimal quantity)
	{
		if (quantity <= 0 || decimal.Truncate(quantity) != quantity)
			throw new ArgumentOutOfRangeException(nameof(quantity), quantity,
				"Mirae Asset Sharekhan quantity must be a positive whole number.");
	}

	private static OrderTypes GetOrderType(decimal price, decimal triggerPrice)
		=> triggerPrice > 0 ? OrderTypes.Conditional : price > 0 ? OrderTypes.Limit : OrderTypes.Market;

	private static string GetOrderId(string stringId, long? numericId, long transactionId)
		=> stringId.IsEmpty(numericId?.ToString(CultureInfo.InvariantCulture))
			.ThrowIfEmpty(LocalizedStrings.OrderNoExchangeId.Put(transactionId));

	private static long? ToLongId(string value)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
}
