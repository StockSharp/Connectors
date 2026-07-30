namespace StockSharp.IIFL;

public partial class IIFLMessageAdapter
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
		var condition = regMsg.Condition as IIFLOrderCondition;
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		var nativeType = ToNativeOrderType(orderType,
			condition?.TriggerPrice, regMsg.Price);
		var quantity = ToNativeQuantity(regMsg.Volume, instrument);
		var disclosed = condition?.DisclosedVolume is > 0
			? ToNativeQuantity(condition.DisclosedVolume.Value,
				instrument)
			: 0;
		if (disclosed > quantity)
			throw new ArgumentOutOfRangeException(
				nameof(condition.DisclosedVolume),
				"Disclosed volume cannot exceed order volume.");

		var product = condition?.Product ??
			(IsDerivative(instrument)
				? IIFLProducts.Normal
				: IIFLProducts.Delivery);
		var request = new JObject
		{
			["exchange"] = instrument.Exchange,
			["instrumentId"] = instrument.InstrumentId,
			["transactionType"] = regMsg.Side == Sides.Buy
				? "BUY"
				: "SELL",
			["quantity"] = quantity,
			["product"] = ToNativeProduct(product),
			["orderComplexity"] = ToNativeComplexity(
				condition?.Complexity ??
					IIFLOrderComplexities.Regular),
			["orderType"] = nativeType,
			["validity"] = ToNativeValidity(regMsg.TimeInForce),
		};
		AddOrderParameters(request, orderType, regMsg.Price,
			condition, instrument);

		var response = await RestClient.PlaceOrderAsync(request,
			cancellationToken);
		var orderId = response.FindIIFLString(
			"brokerOrderId", "orderId")
			.ThrowIfEmpty("brokerOrderId");
		RememberOrder(orderId, regMsg.TransactionId);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			OrderStringId = orderId,
			SecurityId = regMsg.SecurityId,
			PortfolioName = _resolvedPortfolio,
			OrderType = orderType,
			Side = regMsg.Side,
			TimeInForce = regMsg.TimeInForce,
			OrderPrice = regMsg.Price,
			OrderVolume = regMsg.Volume,
			Balance = regMsg.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = response.FindIIFL(
				"requestTime", "timestamp")
				.ToIIFLTime(CurrentTime).UtcDateTime,
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
		var condition = replaceMsg.Condition as IIFLOrderCondition;
		var orderType = replaceMsg.OrderType ?? OrderTypes.Limit;
		var request = new JObject
		{
			["quantity"] = ToNativeQuantity(replaceMsg.Volume,
				instrument),
			["orderType"] = ToNativeOrderType(orderType,
				condition?.TriggerPrice, replaceMsg.Price),
			["validity"] = ToNativeValidity(replaceMsg.TimeInForce),
		};
		AddOrderParameters(request, orderType, replaceMsg.Price,
			condition, instrument);
		var response = await RestClient.ModifyOrderAsync(orderId,
			request, cancellationToken);
		var resultingId = response.FindIIFLString(
			"brokerOrderId", "orderId").IsEmpty(orderId);
		RememberOrder(resultingId, replaceMsg.TransactionId);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = replaceMsg.TransactionId,
			OrderStringId = resultingId,
			SecurityId = replaceMsg.SecurityId,
			PortfolioName = _resolvedPortfolio,
			OrderType = orderType,
			Side = replaceMsg.Side,
			TimeInForce = replaceMsg.TimeInForce,
			OrderPrice = replaceMsg.Price,
			OrderVolume = replaceMsg.Volume,
			Balance = replaceMsg.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = response.FindIIFL(
				"requestTime", "timestamp")
				.ToIIFLTime(CurrentTime).UtcDateTime,
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
		var orderId = ResolveOrderId(cancelMsg.OrderStringId,
			cancelMsg.OriginalTransactionId);
		await RestClient.CancelOrderAsync(orderId, cancellationToken);
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
			var unsubscribePrivate = false;
			using (_sync.EnterScope())
			{
				_orderSubscriptions.Remove(
					statusMsg.OriginalTransactionId);
				unsubscribePrivate =
					_orderSubscriptions.Count == 0;
			}
			if (unsubscribePrivate)
				await UnsubscribePrivateStreamAsync(
					cancellationToken);
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
		try
		{
			await SubscribePrivateStreamAsync(cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_orderSubscriptions.Remove(statusMsg.TransactionId);
			throw;
		}
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
			.ToIIFLObjects()
			.Select(static value => value.ToIIFLOrder())
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

		foreach (var trade in (await RestClient.GetTradesAsync(
				cancellationToken)).ToIIFLObjects()
			.Select(static value => value.ToIIFLTrade())
			.Where(value =>
				(from is null ||
					value.Time.UtcDateTime >=
						from.Value.ToUniversalTime()) &&
				(to is null ||
					value.Time.UtcDateTime <=
						to.Value.ToUniversalTime())))
			await SendTradeAsync(trade, target, true,
				cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		CancellationToken cancellationToken)
	{
		var profile = await RestClient.GetProfileAsync(
			cancellationToken);
		var profileValue = profile.UnwrapIIFLResult() as JObject ??
			profile.ToIIFLObjects().FirstOrDefault();
		var portfolio = profileValue?.FindIIFLString(
			"clientId", "userId", "clientCode");
		if (!portfolio.IsEmpty())
			_resolvedPortfolio = portfolio;

		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = target,
			PortfolioName = _resolvedPortfolio,
			BoardCode = BoardCodes.Nse,
		}, cancellationToken);

		var limits = await RestClient.GetLimitsAsync(
			cancellationToken);
		var limit = limits.UnwrapIIFLResult() as JObject ??
			limits.ToIIFLObjects().FirstOrDefault();
		if (limit is not null)
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = target,
				PortfolioName = _resolvedPortfolio,
				SecurityId = SecurityId.Money,
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue,
				limit.FindIIFLDecimal("availableBalance",
					"availableMargin", "netAvailableMargin",
					"cashAvailable", "net"), true)
			.TryAdd(PositionChangeTypes.BlockedValue,
				limit.FindIIFLDecimal("usedMargin",
					"utilizedMargin", "totalUtilizedMargin"),
				true), cancellationToken);

		foreach (var holding in (await RestClient.GetHoldingsAsync(
				cancellationToken)).ToIIFLObjects())
			await SendHoldingAsync(holding, target,
				cancellationToken);

		foreach (var position in (await RestClient.GetPositionsAsync(
				cancellationToken)).ToIIFLObjects())
			await SendPositionAsync(position, target,
				cancellationToken);
	}

	private ValueTask SendHoldingAsync(JObject value, long target,
		CancellationToken cancellationToken)
	{
		var exchange = value.FindIIFLString("exchange");
		var instrumentId = value.FindIIFLString("instrumentId");
		var symbol = value.FindIIFLString(
			"tradingSymbol", "symbol", "companyName");
		if (instrumentId.IsEmpty())
		{
			instrumentId = value.FindIIFLString(
				"nseInstrumentId", "nseToken");
			if (!instrumentId.IsEmpty())
				exchange = "NSEEQ";
			else
			{
				instrumentId = value.FindIIFLString(
					"bseInstrumentId", "bseToken");
				if (!instrumentId.IsEmpty())
					exchange = "BSEEQ";
			}
		}
		if (exchange.IsEmpty() || instrumentId.IsEmpty())
			return default;
		var instrument = CreateInstrumentRef(exchange, instrumentId,
			symbol);
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = target,
			PortfolioName = _resolvedPortfolio,
			SecurityId = ToSecurityId(instrument),
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue,
			value.FindIIFLDecimal("availableQuantity", "quantity",
				"totalQuantity", "dpQuantity"), true)
		.TryAdd(PositionChangeTypes.BlockedValue,
			value.FindIIFLDecimal("blockedQuantity",
				"pledgedQuantity"), true)
		.TryAdd(PositionChangeTypes.AveragePrice,
			value.FindIIFLDecimal("averagePrice",
				"averageBuyPrice"), true)
		.TryAdd(PositionChangeTypes.CurrentPrice,
			value.FindIIFLDecimal("ltp", "lastPrice"), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL,
			value.FindIIFLDecimal("pnl", "unrealizedPnl"), true),
			cancellationToken);
	}

	private ValueTask SendPositionAsync(JObject value, long target,
		CancellationToken cancellationToken)
	{
		var exchange = value.FindIIFLString(
			"exchange", "exchangeSegment");
		var instrumentId = value.FindIIFLString(
			"instrumentId", "token");
		if (exchange.IsEmpty() || instrumentId.IsEmpty())
			return default;
		var instrument = CreateInstrumentRef(exchange, instrumentId,
			value.FindIIFLString("tradingSymbol", "symbol"));
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = target,
			PortfolioName = _resolvedPortfolio,
			SecurityId = ToSecurityId(instrument),
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue,
			value.FindIIFLDecimal("netQuantity", "quantity"), true)
		.TryAdd(PositionChangeTypes.AveragePrice,
			value.FindIIFLDecimal("netAveragePrice",
				"averagePrice"), true)
		.TryAdd(PositionChangeTypes.CurrentPrice,
			value.FindIIFLDecimal("ltp", "lastPrice"), true)
		.TryAdd(PositionChangeTypes.RealizedPnL,
			value.FindIIFLDecimal("realizedPnl",
				"realizedProfitLoss"), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL,
			value.FindIIFLDecimal("unrealizedPnl", "pnl",
				"markToMarket"), true), cancellationToken);
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
				cancellationToken)).ToIIFLObjects()
			.Select(static value => value.ToIIFLOrder())
			.ToArray();
		var trades = (await RestClient.GetTradesAsync(
				cancellationToken)).ToIIFLObjects()
			.Select(static value => value.ToIIFLTrade())
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

	private async ValueTask ProcessOrderStreamAsync(string json,
		CancellationToken cancellationToken)
	{
		var value = ParseStreamJson(json);
		var orders = value.ToIIFLObjects()
			.Select(static item => item.ToIIFLOrder())
			.ToArray();
		long[] targets;
		using (_sync.EnterScope())
			targets = [.. _orderSubscriptions];

		foreach (var order in orders)
			foreach (var target in targets)
				await SendOrderAsync(order, target, false,
					cancellationToken);
	}

	private async ValueTask ProcessTradeStreamAsync(string json,
		CancellationToken cancellationToken)
	{
		var value = ParseStreamJson(json);
		var trades = value.ToIIFLObjects()
			.Select(static item => item.ToIIFLTrade())
			.ToArray();
		long[] targets;
		using (_sync.EnterScope())
			targets = [.. _orderSubscriptions];

		foreach (var trade in trades)
			foreach (var target in targets)
				await SendTradeAsync(trade, target, false,
					cancellationToken);
	}

	private async ValueTask SendOrderAsync(IIFLOrder order,
		long target, bool isLookup,
		CancellationToken cancellationToken)
	{
		if (order is null || order.OrderId.IsEmpty())
			return;
		long transactionId;
		if (long.TryParse(order.Tag, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out transactionId))
			RememberOrder(order.OrderId, transactionId);
		else
		{
			using (_sync.EnterScope())
				_orderTransactions.TryGetValue(order.OrderId,
					out transactionId);
		}
		var instrument = CreateInstrumentRef(order.Exchange,
			order.InstrumentId, order.Symbol);
		var state = order.Status.ToIIFLOrderState();
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = target,
			TransactionId = isLookup ? transactionId : 0,
			OrderStringId = order.OrderId,
			SecurityId = ToSecurityId(instrument),
			PortfolioName = _resolvedPortfolio,
			OrderType = order.Type.ToIIFLOrderType(),
			Side = order.Side,
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
				? new InvalidOperationException(order.Error.IsEmpty(
					$"IIFL order status: {order.Status}."))
				: null,
		}, cancellationToken);
	}

	private ValueTask SendTradeAsync(IIFLTrade trade, long target,
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
			trade.InstrumentId, trade.Symbol);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = target,
			TransactionId = isLookup ? transactionId : 0,
			OrderStringId = trade.OrderId,
			TradeStringId = trade.Id,
			SecurityId = ToSecurityId(instrument),
			PortfolioName = _resolvedPortfolio,
			Side = trade.Side,
			TradePrice = trade.Price,
			TradeVolume = trade.Volume,
			ServerTime = trade.Time.UtcDateTime,
		}, cancellationToken);
	}

	private async ValueTask SubscribePrivateStreamAsync(
		CancellationToken cancellationToken)
	{
		if (_mqttClient is null)
			return;
		using (_sync.EnterScope())
		{
			if (_privateStreamSubscribed)
				return;
			_privateStreamSubscribed = true;
		}
		try
		{
			await _mqttClient.SubscribeOrdersAsync(cancellationToken);
			await _mqttClient.SubscribeTradesAsync(cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_privateStreamSubscribed = false;
			throw;
		}
	}

	private async ValueTask UnsubscribePrivateStreamAsync(
		CancellationToken cancellationToken)
	{
		if (_mqttClient is null)
			return;
		using (_sync.EnterScope())
		{
			if (!_privateStreamSubscribed)
				return;
			_privateStreamSubscribed = false;
		}
		await _mqttClient.UnsubscribeOrdersAsync(cancellationToken);
		await _mqttClient.UnsubscribeTradesAsync(cancellationToken);
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
			!portfolioName.EqualsIgnoreCase(_resolvedPortfolio))
			throw new InvalidOperationException(
				LocalizedStrings.AccountNotFound);
	}

	private IIFLInstrumentRef CreateInstrumentRef(string exchange,
		string instrumentId, string symbol)
	{
		exchange = NormalizeExchange(exchange);
		if (instrumentId.IsEmpty())
			instrumentId = symbol;
		var key = NativeKey(exchange, instrumentId);
		using (_sync.EnterScope())
			if (_instrumentsByNative.TryGetValue(key,
				out var known))
				return known;
		return new(exchange, instrumentId,
			symbol.IsEmpty(instrumentId), exchange.ToBoardCode(), 1);
	}

	private static string NormalizeExchange(string exchange)
		=> exchange?.Trim().ToUpperInvariant() switch
		{
			null or "" => "NSEEQ",
			"NSE" => "NSEEQ",
			"BSE" => "BSEEQ",
			"NFO" => "NSEFO",
			"BFO" => "BSEFO",
			"CDS" => "NSECURR",
			"BCD" => "BSECURR",
			"MCX" => "MCXCOMM",
			"NCO" => "NSECOMM",
			var value => value,
		};

	private static JToken ParseStreamJson(string json)
	{
		try
		{
			return JToken.Parse(json.ThrowIfEmpty(nameof(json)));
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"IIFL stream returned invalid JSON.", error);
		}
	}

	private static IIFLOrderCondition ToOrderCondition(
		IIFLOrder order)
		=> new()
		{
			Product = order.Product?.Trim().ToUpperInvariant() switch
			{
				"INTRADAY" => IIFLProducts.Intraday,
				"DELIVERY" => IIFLProducts.Delivery,
				"BNPL" => IIFLProducts.BNPL,
				_ => IIFLProducts.Normal,
			},
			Complexity =
				order.Complexity?.Trim().ToUpperInvariant() switch
				{
					"AMO" => IIFLOrderComplexities.AMO,
					"BO" => IIFLOrderComplexities.BO,
					"CO" => IIFLOrderComplexities.CO,
					_ => IIFLOrderComplexities.Regular,
				},
			TriggerPrice = order.TriggerPrice > 0
				? order.TriggerPrice
				: null,
			Tag = order.Tag,
		};

	private static void AddOrderParameters(JObject request,
		OrderTypes orderType, decimal price,
		IIFLOrderCondition condition,
		IIFLInstrumentRef instrument)
	{
		if (orderType == OrderTypes.Limit && price <= 0)
			throw new ArgumentOutOfRangeException(nameof(price), price,
				"A positive limit price is required.");
		if (orderType != OrderTypes.Market && price > 0)
			request["price"] = price;
		if (condition?.TriggerPrice is > 0)
			request["slTriggerPrice"] = condition.TriggerPrice.Value;
		if (condition?.DisclosedVolume is > 0)
			request["disclosedQuantity"] = ToNativeQuantity(
				condition.DisclosedVolume.Value, instrument);
		if (condition?.StopLossLegPrice is > 0)
			request["slLegPrice"] =
				condition.StopLossLegPrice.Value;
		if (condition?.TargetLegPrice is > 0)
			request["targetLegPrice"] =
				condition.TargetLegPrice.Value;
		if (condition?.MarketProtectionPercent is not null)
			request["marketProtectionPercentage"] =
				condition.MarketProtectionPercent.Value;
		if (condition is not null && !condition.AlgoId.IsEmpty())
			request["algoId"] = condition.AlgoId;
		if (condition is not null && !condition.Tag.IsEmpty())
			request["orderTag"] = condition.Tag;
	}

	private static string ToNativeOrderType(OrderTypes orderType,
		decimal? triggerPrice, decimal price)
		=> orderType switch
		{
			OrderTypes.Market => "MARKET",
			OrderTypes.Limit => "LIMIT",
			OrderTypes.Conditional when triggerPrice is > 0 =>
				price > 0 ? "SL" : "SLM",
			OrderTypes.Conditional =>
				throw new InvalidOperationException(
					"A positive trigger price is required for an " +
						"IIFL stop-loss order."),
			_ => throw new ArgumentOutOfRangeException(
				nameof(orderType), orderType,
				"IIFL supports market, limit, and stop-loss orders."),
		};

	private static string ToNativeProduct(IIFLProducts value)
		=> value switch
		{
			IIFLProducts.Normal => "NORMAL",
			IIFLProducts.Intraday => "INTRADAY",
			IIFLProducts.Delivery => "DELIVERY",
			IIFLProducts.BNPL => "BNPL",
			_ => throw new ArgumentOutOfRangeException(nameof(value),
				value, null),
		};

	private static string ToNativeComplexity(
		IIFLOrderComplexities value)
		=> value switch
		{
			IIFLOrderComplexities.Regular => "REGULAR",
			IIFLOrderComplexities.AMO => "AMO",
			IIFLOrderComplexities.BO => "BO",
			IIFLOrderComplexities.CO => "CO",
			_ => throw new ArgumentOutOfRangeException(nameof(value),
				value, null),
		};

	private static string ToNativeValidity(TimeInForce? value)
		=> value switch
		{
			null or TimeInForce.PutInQueue => "DAY",
			TimeInForce.CancelBalance => "IOC",
			TimeInForce.MatchOrCancel =>
				throw new NotSupportedException(
					"IIFL does not expose fill-or-kill validity."),
			_ => throw new ArgumentOutOfRangeException(nameof(value),
				value, null),
		};

	private static long ToNativeQuantity(decimal value,
		IIFLInstrumentRef instrument)
	{
		if (value <= 0)
			throw new ArgumentOutOfRangeException(nameof(value), value,
				"IIFL quantity must be positive.");
		if (instrument.Exchange.Contains("COMM",
			StringComparison.OrdinalIgnoreCase))
		{
			if (instrument.LotSize <= 0 ||
				value % instrument.LotSize != 0)
				throw new ArgumentOutOfRangeException(nameof(value),
					value,
					"IIFL commodity quantity must be an exact " +
						"multiple of the contract lot size.");
			value /= instrument.LotSize;
		}
		if (value != decimal.Truncate(value) || value > long.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(value), value,
				"IIFL quantity must fit a whole Int64 value.");
		return decimal.ToInt64(value);
	}
}
