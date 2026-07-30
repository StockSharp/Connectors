namespace StockSharp.Samco;

public partial class SamcoMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsureConnected();
		EnsurePortfolio(regMsg.PortfolioName);
		var instrument = await ResolveInstrumentAsync(regMsg.SecurityId,
			cancellationToken);
		var condition = regMsg.Condition as SamcoOrderCondition;
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		var product = condition?.Product ??
			(IsDerivative(instrument)
				? SamcoProducts.NRML
				: SamcoProducts.CNC);
		var request = CreateOrderRequest(instrument, regMsg.Side,
			orderType, regMsg.Price, regMsg.Volume,
			regMsg.TimeInForce, product, condition);
		var response = await RestClient.PlaceOrderAsync(request,
			cancellationToken);
		var orderId = response.String(
			"orderNumber", "ordernumber")
				.ThrowIfEmpty("orderNumber");
		RememberOrder(orderId, regMsg.TransactionId);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			OrderStringId = orderId,
			SecurityId = ToSecurityId(instrument),
			PortfolioName = _portfolioName,
			OrderType = orderType,
			Side = regMsg.Side,
			TimeInForce = regMsg.TimeInForce,
			OrderPrice = regMsg.Price,
			OrderVolume = regMsg.Volume,
			Balance = regMsg.Volume,
			OrderState = response.String("exchangeOrderStatus")
				.ToSamcoOrderState(),
			ServerTime = response.Get("serverTime")
				.ToSamcoTime(CurrentTime).UtcDateTime,
			Condition = condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		EnsureConnected();
		EnsurePortfolio(replaceMsg.PortfolioName);
		var orderId = ResolveOrderId(replaceMsg.OldOrderStringId,
			replaceMsg.OriginalTransactionId);
		var instrument = await ResolveInstrumentAsync(
			replaceMsg.SecurityId, cancellationToken);
		var condition = replaceMsg.Condition as SamcoOrderCondition;
		var orderType = replaceMsg.OrderType ?? OrderTypes.Limit;
		var request = new JObject
		{
			["orderType"] = ToNativeOrderType(orderType,
				condition?.TriggerPrice, replaceMsg.Price),
			["quantity"] = ToNativeQuantity(replaceMsg.Volume),
			["disclosedQuantity"] =
				condition?.DisclosedVolume is > 0
					? ToNativeQuantity(
						condition.DisclosedVolume.Value)
					: "0",
			["orderValidity"] = ToNativeValidity(
				replaceMsg.TimeInForce),
			["price"] = FormatPrice(replaceMsg.Price),
			["triggerPrice"] =
				FormatPrice(condition?.TriggerPrice ?? 0),
		};
		var response = await RestClient.ModifyOrderAsync(orderId,
			request, cancellationToken);
		var resultingId = response.String(
			"orderNumber", "ordernumber").IsEmpty(orderId);
		RememberOrder(resultingId, replaceMsg.TransactionId);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = replaceMsg.TransactionId,
			OrderStringId = resultingId,
			SecurityId = ToSecurityId(instrument),
			PortfolioName = _portfolioName,
			OrderType = orderType,
			Side = replaceMsg.Side,
			TimeInForce = replaceMsg.TimeInForce,
			OrderPrice = replaceMsg.Price,
			OrderVolume = replaceMsg.Volume,
			Balance = replaceMsg.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = response.Get("serverTime")
				.ToSamcoTime(CurrentTime).UtcDateTime,
			Condition = condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsureConnected();
		EnsurePortfolio(cancelMsg.PortfolioName);
		await RestClient.CancelOrderAsync(
			ResolveOrderId(cancelMsg.OrderStringId,
				cancelMsg.OriginalTransactionId),
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId,
			cancellationToken);

		EnsureConnected();
		if (!statusMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_orderSubscriptions.Remove(
					statusMsg.OriginalTransactionId);
			return;
		}
		EnsurePortfolio(statusMsg.PortfolioName);
		await SendOrdersSnapshotAsync(statusMsg.TransactionId,
			statusMsg.From, statusMsg.To, statusMsg.Count,
			cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(statusMsg,
				cancellationToken);
			await SendSubscriptionFinishedAsync(
				statusMsg.TransactionId, cancellationToken);
			return;
		}

		using (_sync.EnterScope())
			_orderSubscriptions.Add(statusMsg.TransactionId);
		await SendSubscriptionResultAsync(statusMsg,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);

		EnsureConnected();
		if (!lookupMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_portfolioSubscriptions.Remove(
					lookupMsg.OriginalTransactionId);
			return;
		}
		EnsurePortfolio(lookupMsg.PortfolioName);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId,
			cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(lookupMsg,
				cancellationToken);
			await SendSubscriptionFinishedAsync(
				lookupMsg.TransactionId, cancellationToken);
			return;
		}

		using (_sync.EnterScope())
			_portfolioSubscriptions.Add(lookupMsg.TransactionId);
		await SendSubscriptionResultAsync(lookupMsg,
			cancellationToken);
	}

	private async ValueTask SendOrdersSnapshotAsync(long target,
		DateTime? from, DateTime? to, long? count,
		CancellationToken cancellationToken)
	{
		var orders = (await RestClient.GetOrdersAsync(
				cancellationToken))
			.ToSamcoObjects("orderBookDetails")
			.Select(static value => value.ToSamcoOrder())
			.Where(static value => !value.OrderId.IsEmpty())
			.OrderBy(static value => value.Time)
			.Where(value =>
				(from is null ||
					value.Time.UtcDateTime >=
						from.Value.ToUniversalTime()) &&
				(to is null ||
					value.Time.UtcDateTime <=
						to.Value.ToUniversalTime()))
			.Take(count is > 0
				? (int)Math.Min(count.Value, int.MaxValue)
				: int.MaxValue)
			.ToArray();

		foreach (var order in orders)
			await SendOrderAsync(order, target, true,
				cancellationToken);

		var trades = (await RestClient.GetTradesAsync(
				cancellationToken))
			.ToSamcoObjects("tradeBookDetails")
			.Select(static value => value.ToSamcoTrade())
			.Where(value =>
				(from is null ||
					value.Time.UtcDateTime >=
						from.Value.ToUniversalTime()) &&
				(to is null ||
					value.Time.UtcDateTime <=
						to.Value.ToUniversalTime()))
			.ToArray();

		foreach (var trade in trades)
			await SendTradeAsync(trade, target, true,
				cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		CancellationToken cancellationToken)
	{
		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = target,
			PortfolioName = _portfolioName,
			BoardCode = BoardCodes.Nse,
		}, cancellationToken);

		var limits = await RestClient.GetLimitsAsync(cancellationToken);
		var equity = limits.Get("equityLimit") as JObject;
		var commodity = limits.Get("commodityLimit") as JObject;
		decimal Sum(string name)
			=> (equity?.Decimal(name) ?? 0) +
				(commodity?.Decimal(name) ?? 0);
		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = target,
			PortfolioName = _portfolioName,
			SecurityId = SecurityId.Money,
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue,
			Sum("netAvailableMargin"), true)
		.TryAdd(PositionChangeTypes.BlockedValue,
			Sum("marginUsed"), true), cancellationToken);

		foreach (var holding in (await RestClient.GetHoldingsAsync(
				cancellationToken)).ToSamcoObjects("holdingDetails"))
			await SendHoldingAsync(holding, target,
				cancellationToken);

		foreach (var position in (await RestClient.GetPositionsAsync(
				"NET", cancellationToken))
				.ToSamcoObjects("positionDetails"))
			await SendPositionAsync(position, target,
				cancellationToken);
	}

	private ValueTask SendHoldingAsync(JObject value, long target,
		CancellationToken cancellationToken)
	{
		var instrument = CreateInstrumentRef(
			value.String("exchange").IsEmpty("NSE"),
			value.String("symbol", "symbolCode", "listingId",
				"isin"),
			value.String("tradingSymbol", "symbolName"));
		if (instrument.SymbolCode.IsEmpty())
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = target,
			PortfolioName = _portfolioName,
			SecurityId = ToSecurityId(instrument),
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue,
			value.Decimal("sellableQuantity", "holdingsQuantity",
				"calculatedNetQuantity"), true)
		.TryAdd(PositionChangeTypes.BlockedValue,
			value.Decimal("collateralQuantity"), true)
		.TryAdd(PositionChangeTypes.AveragePrice,
			value.Decimal("averagePrice"), true)
		.TryAdd(PositionChangeTypes.CurrentPrice,
			value.Decimal("lastTradedPrice",
				"markToMarketPrice"), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL,
			value.Decimal("totalMarketToMarketPrice"), true),
			cancellationToken);
	}

	private ValueTask SendPositionAsync(JObject value, long target,
		CancellationToken cancellationToken)
	{
		var instrument = CreateInstrumentRef(
			value.String("exchange").IsEmpty("NSE"),
			value.String("symbol", "symbolCode", "listingId"),
			value.String("tradingSymbol", "symbolName"));
		if (instrument.SymbolCode.IsEmpty())
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = target,
			PortfolioName = _portfolioName,
			SecurityId = ToSecurityId(instrument),
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue,
			value.Decimal("calculatedNetQuantity",
				"netQuantity"), true)
		.TryAdd(PositionChangeTypes.AveragePrice,
			value.Decimal("averagePrice"), true)
		.TryAdd(PositionChangeTypes.CurrentPrice,
			value.Decimal("lastTradedPrice",
				"markToMarketPrice"), true)
		.TryAdd(PositionChangeTypes.RealizedPnL,
			value.Decimal("realizedGainAndLoss"), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL,
			value.Decimal("unrealizedGainAndLoss"), true),
			cancellationToken);
	}

	private async ValueTask PollOrdersAsync(
		CancellationToken cancellationToken)
	{
		long[] targets;
		using (_sync.EnterScope())
			targets = [.. _orderSubscriptions];
		if (targets.Length == 0)
			return;
		var orders = (await RestClient.GetOrdersAsync(
				cancellationToken))
			.ToSamcoObjects("orderBookDetails")
			.Select(static value => value.ToSamcoOrder())
			.ToArray();
		var trades = (await RestClient.GetTradesAsync(
				cancellationToken))
			.ToSamcoObjects("tradeBookDetails")
			.Select(static value => value.ToSamcoTrade())
			.ToArray();

		foreach (var target in targets)
		{
			foreach (var order in orders)
				await SendOrderAsync(order, target, false,
					cancellationToken);

			foreach (var trade in trades)
				await SendTradeAsync(trade, target, false,
					cancellationToken);
		}
	}

	private async ValueTask PollPortfoliosAsync(
		CancellationToken cancellationToken)
	{
		long[] targets;
		using (_sync.EnterScope())
			targets = [.. _portfolioSubscriptions];

		foreach (var target in targets)
			await SendPortfolioSnapshotAsync(target,
				cancellationToken);
	}

	private async ValueTask SendOrderAsync(SamcoOrder order,
		long target, bool isLookup,
		CancellationToken cancellationToken)
	{
		if (order is null || order.OrderId.IsEmpty())
			return;
		long transactionId;
		using (_sync.EnterScope())
			_orderTransactions.TryGetValue(order.OrderId,
				out transactionId);
		var instrument = CreateInstrumentRef(order.Exchange,
			order.SymbolCode, order.Symbol);
		var state = order.Status.ToSamcoOrderState();
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = target,
			TransactionId = isLookup ? transactionId : 0,
			OrderStringId = order.OrderId,
			SecurityId = ToSecurityId(instrument),
			PortfolioName = _portfolioName,
			OrderType = order.OrderType.ToSamcoOrderType(),
			Side = order.Side,
			TimeInForce = order.Validity.ToSamcoTimeInForce(),
			OrderPrice = order.Price,
			OrderVolume = order.Volume,
			Balance = order.Balance,
			AveragePrice = order.AveragePrice > 0
				? order.AveragePrice
				: null,
			OrderState = state,
			ServerTime = order.Time.UtcDateTime,
			Condition = ToOrderCondition(order),
			Error = state == OrderStates.Failed
				? new InvalidOperationException(order.Text.IsEmpty(
					$"Samco order status: {order.Status}."))
				: null,
		}, cancellationToken);
	}

	private ValueTask SendTradeAsync(SamcoTrade trade, long target,
		bool isLookup, CancellationToken cancellationToken)
	{
		if (trade is null || trade.Id.IsEmpty())
			return default;
		using (_sync.EnterScope())
		{
			if (!_tradeIds.Add($"{target}:{trade.Id}"))
				return default;
		}
		long transactionId;
		using (_sync.EnterScope())
			_orderTransactions.TryGetValue(trade.OrderId,
				out transactionId);
		var instrument = CreateInstrumentRef(trade.Exchange,
			trade.SymbolCode, trade.Symbol);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = target,
			TransactionId = isLookup ? transactionId : 0,
			OrderStringId = trade.OrderId,
			TradeStringId = trade.Id,
			SecurityId = ToSecurityId(instrument),
			PortfolioName = _portfolioName,
			Side = trade.Side,
			TradePrice = trade.Price,
			TradeVolume = trade.Volume,
			ServerTime = trade.Time.UtcDateTime,
		}, cancellationToken);
	}

	private void RememberOrder(string orderId, long transactionId)
	{
		if (orderId.IsEmpty() || transactionId == 0)
			return;
		using (_sync.EnterScope())
		{
			_orderTransactions[orderId] = transactionId;
			_transactionOrders[transactionId] = orderId;
		}
	}

	private string ResolveOrderId(string orderId,
		long originalTransactionId)
	{
		if (!orderId.IsEmpty())
			return orderId;
		using (_sync.EnterScope())
			if (_transactionOrders.TryGetValue(originalTransactionId,
				out orderId))
				return orderId;
		throw new InvalidOperationException(
			LocalizedStrings.OrderNoExchangeId.Put(
				originalTransactionId));
	}

	private void EnsurePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(_portfolioName))
			throw new InvalidOperationException(
				LocalizedStrings.AccountNotFound);
	}

	private SamcoInstrumentRef CreateInstrumentRef(string exchange,
		string symbolCode, string symbol)
	{
		exchange = exchange.IsEmpty("NSE").Trim().ToUpperInvariant();
		using (_sync.EnterScope())
		{
			if (!symbolCode.IsEmpty() &&
				_instrumentsByNative.TryGetValue(symbolCode,
					out var native))
				return native;
			if (!symbol.IsEmpty() &&
				_instrumentsBySymbol.TryGetValue(
					SymbolKey(exchange, symbol), out var known))
				return known;
		}
		symbolCode = symbolCode.IsEmpty(symbol);
		return new(exchange, symbolCode, symbol.IsEmpty(symbolCode),
			symbol.IsEmpty(symbolCode), 1, null);
	}

	private bool IsDerivative(SamcoInstrumentRef instrument)
		=> instrument.Exchange is "NFO" or "BFO" or "CDS" or
			"MCX" or "MFO" ||
			instrument.Instrument?.Contains("FUT",
				StringComparison.OrdinalIgnoreCase) == true ||
			instrument.Instrument?.Contains("OPT",
				StringComparison.OrdinalIgnoreCase) == true;

	private static JObject CreateOrderRequest(
		SamcoInstrumentRef instrument, Sides side,
		OrderTypes orderType, decimal price, decimal volume,
		TimeInForce? timeInForce, SamcoProducts product,
		SamcoOrderCondition condition)
	{
		if (orderType == OrderTypes.Limit && price <= 0)
			throw new ArgumentOutOfRangeException(nameof(price), price,
				"A positive limit price is required.");
		var trigger = condition?.TriggerPrice ?? 0;
		if (orderType == OrderTypes.Conditional && trigger <= 0)
			throw new InvalidOperationException(
				"A positive trigger price is required for a Samco " +
					"stop-loss order.");
		var quantity = ToNativeQuantity(volume);
		var disclosed = condition?.DisclosedVolume is > 0
			? ToNativeQuantity(condition.DisclosedVolume.Value)
			: "0";
		if (decimal.Parse(disclosed,
				CultureInfo.InvariantCulture) > volume)
			throw new ArgumentOutOfRangeException(
				nameof(condition.DisclosedVolume),
				"Disclosed volume cannot exceed order volume.");
		return new()
		{
			["symbolName"] = instrument.OrderSymbol,
			["exchange"] = instrument.Exchange,
			["transactionType"] =
				side == Sides.Buy ? "BUY" : "SELL",
			["orderType"] = ToNativeOrderType(orderType,
				trigger, price),
			["quantity"] = quantity,
			["disclosedQuantity"] = disclosed,
			["orderValidity"] = ToNativeValidity(timeInForce),
			["productType"] = product.ToString(),
			["afterMarketOrderFlag"] =
				condition?.AfterMarketOrder == true ? "YES" : "NO",
			["price"] = FormatPrice(price),
			["triggerPrice"] = FormatPrice(trigger),
		};
	}

	private static string ToNativeOrderType(OrderTypes orderType,
		decimal? triggerPrice, decimal price)
		=> orderType switch
		{
			OrderTypes.Market => "MKT",
			OrderTypes.Limit => "L",
			OrderTypes.Conditional when triggerPrice is > 0 =>
				price > 0 ? "SL" : "SL-M",
			OrderTypes.Conditional =>
				throw new InvalidOperationException(
					"A positive trigger price is required for a " +
						"Samco stop-loss order."),
			_ => throw new ArgumentOutOfRangeException(
				nameof(orderType), orderType,
				"Samco supports market, limit, and stop-loss orders."),
		};

	private static string ToNativeValidity(TimeInForce? value)
		=> value switch
		{
			null or TimeInForce.PutInQueue => "DAY",
			TimeInForce.CancelBalance => "IOC",
			TimeInForce.MatchOrCancel =>
				throw new NotSupportedException(
					"Samco does not expose fill-or-kill validity."),
			_ => throw new ArgumentOutOfRangeException(nameof(value),
				value, null),
		};

	private static string ToNativeQuantity(decimal value)
	{
		if (value <= 0 || value != decimal.Truncate(value) ||
			value > long.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(value), value,
				"Samco quantity must be a positive whole Int64 value.");
		return decimal.ToInt64(value).ToString(
			CultureInfo.InvariantCulture);
	}

	private static string FormatPrice(decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	private static SamcoOrderCondition ToOrderCondition(
		SamcoOrder order)
		=> new()
		{
			Product = Enum.TryParse<SamcoProducts>(
				order.Product, true, out var product)
					? product
					: null,
			TriggerPrice = order.TriggerPrice > 0
				? order.TriggerPrice
				: null,
		};
}
