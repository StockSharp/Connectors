namespace StockSharp.Nubra;

public partial class NubraMessageAdapter
{
	private readonly SynchronizedDictionary<long, long> _orderTransactions = [];
	private readonly SynchronizedDictionary<long, long> _transactionOrders = [];
	private readonly SynchronizedDictionary<long, decimal> _tradeQuantities = [];
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsurePortfolio(regMsg.PortfolioName);
		var instrument = await ResolveInstrument(
			regMsg.SecurityId,
			cancellationToken);
		var condition = regMsg.Condition as NubraOrderCondition;
		var product = condition?.Product ?? DefaultProduct;
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		var payload = CreateOrderPayload(
			instrument.RefId,
			regMsg.Volume,
			regMsg.Side,
			product,
			orderType,
			regMsg.Price,
			regMsg.TimeInForce,
			condition?.TriggerPrice,
			condition?.StrategyTag);
		var order = await _restClient.PlaceOrder(
			payload,
			cancellationToken);
		if (order.IntentOrderId <= 0)
		{
			throw new InvalidDataException(
				"Nubra create-order response returned no intentOrderId.");
		}

		RememberOrder(order.IntentOrderId, regMsg.TransactionId);
		await SendOutMessageAsync(
			new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				OriginalTransactionId = regMsg.TransactionId,
				OrderStringId = order.IntentOrderId.ToString(
					CultureInfo.InvariantCulture),
				SecurityId = regMsg.SecurityId,
				PortfolioName = _resolvedPortfolioName,
				OrderType = orderType,
				Side = regMsg.Side,
				TimeInForce = regMsg.TimeInForce ??
					(orderType == OrderTypes.Market
						? TimeInForce.CancelBalance
						: TimeInForce.PutInQueue),
				OrderPrice = regMsg.Price,
				OrderVolume = regMsg.Volume,
				Balance = regMsg.Volume,
				OrderState = OrderStates.Pending,
				ServerTime = GetOrderTime(order),
				Condition = CreateCondition(
					product,
					condition?.TriggerPrice,
					condition?.StrategyTag),
			},
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		EnsurePortfolio(replaceMsg.PortfolioName);
		var current = await ResolveOrder(
			replaceMsg.OldOrderStringId,
			replaceMsg.OriginalTransactionId,
			cancellationToken);
		var condition = replaceMsg.Condition as NubraOrderCondition;
		var product = condition?.Product ??
			current.DeliveryType.ToProduct();
		var orderType = replaceMsg.OrderType ?? current.ToOrderType();
		var quantity = ToWholeQuantity(
			replaceMsg.Volume,
			nameof(replaceMsg.Volume));
		ValidateOrder(
			orderType,
			replaceMsg.Price,
			replaceMsg.TimeInForce,
			condition?.TriggerPrice);

		var payload = new JObject
		{
			["orderId"] = current.IntentOrderId,
			["qty"] = quantity,
			["deliveryType"] = product.ToNative(),
			["priceType"] = ToPriceType(orderType, replaceMsg.Price),
			["validityType"] = ToValidity(
				orderType,
				replaceMsg.TimeInForce),
			["executionMode"] = "ENTRY",
		};
		if (replaceMsg.Price > 0)
		{
			payload["entryPrice"] = replaceMsg.Price.ToNativePrice(
				nameof(replaceMsg.Price));
		}
		AddTrigger(
			payload,
			replaceMsg.Side,
			condition?.TriggerPrice);
		await _restClient.ModifyOrder(payload, cancellationToken);
		RememberOrder(current.IntentOrderId, replaceMsg.TransactionId);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePortfolio(cancelMsg.PortfolioName);
		var current = await ResolveOrder(
			cancelMsg.OrderStringId,
			cancelMsg.OriginalTransactionId,
			cancellationToken);
		await _restClient.CancelOrder(
			current.IntentOrderId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			statusMsg.TransactionId,
			cancellationToken);

		if (!statusMsg.IsSubscribe)
		{
			if (_orderStatusSubscriptionId ==
				statusMsg.OriginalTransactionId)
				_orderStatusSubscriptionId = 0;
			return;
		}

		EnsurePortfolio(statusMsg.PortfolioName);
		await SendOrderSnapshot(
			statusMsg.TransactionId,
			true,
			cancellationToken,
			statusMsg.From,
			statusMsg.To,
			statusMsg.Count);
		_lastOrderRefresh = CurrentTime;

		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(
				statusMsg.TransactionId,
				cancellationToken);
		}
		else
		{
			_orderStatusSubscriptionId = statusMsg.TransactionId;
			await SendSubscriptionResultAsync(
				statusMsg,
				cancellationToken);
		}
	}

	private async ValueTask SendOrderSnapshot(
		long originalTransactionId,
		bool isLookup,
		CancellationToken cancellationToken,
		DateTime? from = null,
		DateTime? to = null,
		long? count = null)
	{
		var left = count ?? long.MaxValue;

		foreach (var order in (await _restClient.GetOrders(cancellationToken))
			.Where(order => order != null)
			.OrderBy(GetOrderTime))
		{
			var time = GetOrderTime(order);
			if (from is DateTime fromTime &&
				time < fromTime.ToUniversalTime())
				continue;
			if (to is DateTime toTime &&
				time > toTime.ToUniversalTime())
				continue;
			await ProcessOrder(
				order,
				originalTransactionId,
				isLookup,
				cancellationToken);
			if (--left <= 0)
				break;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			lookupMsg.TransactionId,
			cancellationToken);

		if (!lookupMsg.IsSubscribe)
		{
			if (_portfolioSubscriptionId ==
				lookupMsg.OriginalTransactionId)
				_portfolioSubscriptionId = 0;
			return;
		}

		EnsurePortfolio(lookupMsg.PortfolioName);
		await SendOutMessageAsync(
			new PortfolioMessage
			{
				OriginalTransactionId = lookupMsg.TransactionId,
				PortfolioName = _resolvedPortfolioName,
				BoardCode = "NSE",
			},
			cancellationToken);
		await SendPortfolioSnapshot(
			lookupMsg.TransactionId,
			cancellationToken);
		_lastPortfolioRefresh = CurrentTime;

		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(
				lookupMsg.TransactionId,
				cancellationToken);
		}
		else
		{
			_portfolioSubscriptionId = lookupMsg.TransactionId;
			await SendSubscriptionResultAsync(
				lookupMsg,
				cancellationToken);
		}
	}

	private async ValueTask SendPortfolioSnapshot(
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var funds = (await _restClient.GetFunds(cancellationToken)).Funds;
		if (funds != null)
		{
			await SendOutMessageAsync(
				new PositionChangeMessage
				{
					OriginalTransactionId = originalTransactionId,
					PortfolioName = _resolvedPortfolioName,
					SecurityId = SecurityId.Money,
					ServerTime = CurrentTime,
				}
				.TryAdd(
					PositionChangeTypes.BeginValue,
					funds.StartOfDayFunds.ToPrice(),
					true)
				.TryAdd(
					PositionChangeTypes.CurrentValue,
					funds.AvailableMargin.ToPrice(),
					true)
				.TryAdd(
					PositionChangeTypes.BlockedValue,
					funds.BlockedMargin.ToPrice(),
					true),
				cancellationToken);
		}

		var positions = (await _restClient.GetPositions(cancellationToken))
			.Portfolio?.Positions ?? [];

		foreach (var position in positions)
		{
			await SendOutMessageAsync(
				new PositionChangeMessage
				{
					OriginalTransactionId = originalTransactionId,
					PortfolioName = _resolvedPortfolioName,
					SecurityId = await GetSecurityId(
						position.RefId,
						position.Exchange,
						position.Symbol,
						cancellationToken),
					ServerTime = CurrentTime,
				}
				.TryAdd(
					PositionChangeTypes.CurrentValue,
					position.NetQuantity,
					true)
				.TryAdd(
					PositionChangeTypes.AveragePrice,
					position.AveragePrice > 0
						? position.AveragePrice.ToPrice()
						: null,
					true)
				.TryAdd(
					PositionChangeTypes.CurrentPrice,
					position.LastPrice > 0
						? position.LastPrice.ToPrice()
						: null,
					true)
				.TryAdd(
					PositionChangeTypes.UnrealizedPnL,
					position.PnL.ToPrice(),
					true),
				cancellationToken);
		}

		var holdings = (await _restClient.GetHoldings(cancellationToken))
			.Portfolio?.Holdings ?? [];

		foreach (var holding in holdings)
		{
			await SendOutMessageAsync(
				new PositionChangeMessage
				{
					OriginalTransactionId = originalTransactionId,
					PortfolioName = _resolvedPortfolioName,
					SecurityId = await GetSecurityId(
						holding.RefId,
						holding.Exchange,
						holding.Symbol,
						cancellationToken),
					ServerTime = CurrentTime,
				}
				.TryAdd(
					PositionChangeTypes.CurrentValue,
					holding.Quantity + holding.T1Quantity,
					true)
				.TryAdd(
					PositionChangeTypes.BlockedValue,
					holding.PledgedQuantity,
					true)
				.TryAdd(
					PositionChangeTypes.AveragePrice,
					holding.AveragePrice > 0
						? holding.AveragePrice.ToPrice()
						: null,
					true)
				.TryAdd(
					PositionChangeTypes.CurrentPrice,
					holding.LastPrice > 0
						? holding.LastPrice.ToPrice()
						: null,
					true)
				.TryAdd(
					PositionChangeTypes.UnrealizedPnL,
					holding.PnL.ToPrice(),
					true),
				cancellationToken);
		}
	}

	private async ValueTask ProcessOrder(
		NubraOrder order,
		long originId,
		bool isLookup,
		CancellationToken cancellationToken)
	{
		if (order?.IntentOrderId <= 0)
			return;

		_orderTransactions.TryGetValue(
			order.IntentOrderId,
			out var transactionId);
		RememberOrder(order.IntentOrderId, transactionId);
		var state = order.Status.ToOrderState();
		var quantity = order.EffectiveQuantity();
		var balance = Math.Max(0m, quantity - order.FilledQuantity);
		var condition = CreateCondition(
			order.DeliveryType.ToProduct(),
			null,
			null);
		await SendOutMessageAsync(
			new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				OriginalTransactionId = isLookup
					? originId
					: transactionId != 0
						? transactionId
						: _orderStatusSubscriptionId,
				TransactionId = isLookup ? transactionId : 0,
				OrderStringId = order.IntentOrderId.ToString(
					CultureInfo.InvariantCulture),
				SecurityId = await GetSecurityId(
					order.RefId,
					order.Exchange.IsEmpty(order.RefData?.Exchange),
					order.RefData?.DisplayName.IsEmpty(order.RefData?.Asset),
					cancellationToken),
				PortfolioName = _resolvedPortfolioName,
				OrderType = order.ToOrderType(),
				Side = order.Side.ToSide(),
				TimeInForce = order.ValidityType.ToTimeInForce(),
				OrderPrice = order.EffectiveOrderPrice().ToPrice(),
				OrderVolume = quantity,
				Balance = balance,
				AveragePrice = order.FilledPrice > 0
					? order.FilledPrice.ToPrice()
					: null,
				OrderState = state,
				ServerTime = GetOrderTime(order),
				Condition = condition,
				Error = state == OrderStates.Failed
					? new InvalidOperationException(
						order.RejectionReason
							.IsEmpty(order.ErrorMessage)
							.IsEmpty(order.Error)
							.IsEmpty($"Nubra order status: {order.Status}."))
					: null,
			},
			cancellationToken);

		_tradeQuantities.TryGetValue(
			order.IntentOrderId,
			out var previousFilled);
		if (order.FilledQuantity <= previousFilled ||
			order.FilledPrice <= 0)
			return;
		_tradeQuantities[order.IntentOrderId] = order.FilledQuantity;
		var fillVolume = order.FilledQuantity - previousFilled;
		await SendOutMessageAsync(
			new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = isLookup
					? originId
					: transactionId != 0
						? transactionId
						: _orderStatusSubscriptionId,
				TransactionId = isLookup ? transactionId : 0,
				OrderStringId = order.IntentOrderId.ToString(
					CultureInfo.InvariantCulture),
				TradeStringId =
					$"{order.IntentOrderId}:{order.FilledQuantity.ToString(CultureInfo.InvariantCulture)}",
				SecurityId = await GetSecurityId(
					order.RefId,
					order.Exchange.IsEmpty(order.RefData?.Exchange),
					order.RefData?.DisplayName.IsEmpty(order.RefData?.Asset),
					cancellationToken),
				PortfolioName = _resolvedPortfolioName,
				Side = order.Side.ToSide(),
				TradePrice = order.FilledPrice.ToPrice(),
				TradeVolume = fillVolume,
				ServerTime = GetOrderTime(order),
			},
			cancellationToken);
	}

	private async Task<NubraOrder> ResolveOrder(
		string orderId,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		long nativeId = 0;
		if (!orderId.IsEmpty())
		{
			long.TryParse(
				orderId,
				NumberStyles.Integer,
				CultureInfo.InvariantCulture,
				out nativeId);
		}
		if (nativeId == 0)
			_transactionOrders.TryGetValue(originalTransactionId, out nativeId);
		if (nativeId == 0)
		{
			throw new InvalidOperationException(
				LocalizedStrings.OrderNoExchangeId.Put(
					originalTransactionId));
		}

		var order = (await _restClient.GetOrders(cancellationToken))
			.FirstOrDefault(item => item.IntentOrderId == nativeId);
		return order ??
			throw new InvalidOperationException(
				$"Nubra order '{nativeId}' was not found in the current order book.");
	}

	private async Task<SecurityId> GetSecurityId(
		long refId,
		string exchange,
		string symbol,
		CancellationToken cancellationToken)
	{
		if (refId > 0)
		{
			if (_securityIds.TryGetValue(refId, out var cached))
				return cached;
			var instrument = await _restClient.GetInstrument(
				refId,
				_referenceDate,
				cancellationToken);
			if (instrument != null)
			{
				var securityId = instrument.ToSecurityId();
				RememberInstrument(instrument, securityId);
				return securityId;
			}
		}

		return new()
		{
			SecurityCode = symbol,
			BoardCode = exchange,
			Native = refId > 0
				? refId.ToString(CultureInfo.InvariantCulture)
				: null,
		};
	}

	private void RememberOrder(long orderId, long transactionId)
	{
		if (orderId <= 0 || transactionId == 0)
			return;
		_orderTransactions[orderId] = transactionId;
		_transactionOrders[transactionId] = orderId;
	}

	private void EnsurePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(_resolvedPortfolioName))
			throw new InvalidOperationException(
				LocalizedStrings.AccountNotFound);
	}

	internal static JObject CreateOrderPayload(
		long refId,
		decimal volume,
		Sides side,
		NubraProducts product,
		OrderTypes orderType,
		decimal price,
		TimeInForce? timeInForce,
		decimal? triggerPrice,
		string strategyTag)
	{
		if (refId <= 0)
			throw new ArgumentOutOfRangeException(nameof(refId));
		var quantity = ToWholeQuantity(volume, nameof(volume));
		ValidateOrder(orderType, price, timeInForce, triggerPrice);
		if (!strategyTag.IsEmpty() &&
			strategyTag.Contains('_', StringComparison.Ordinal))
		{
			throw new ArgumentException(
				"Nubra strategy tags cannot contain underscores.",
				nameof(strategyTag));
		}

		var payload = new JObject
		{
			["refId"] = refId,
			["qty"] = quantity,
			["side"] = side.ToNative(),
			["deliveryType"] = product.ToNative(),
			["priceType"] = ToPriceType(orderType, price),
			["validityType"] = ToValidity(orderType, timeInForce),
			["isMultiLeg"] = false,
			["executionMode"] = "ENTRY",
		};
		if (price > 0)
			payload["entryPrice"] = price.ToNativePrice(nameof(price));
		if (!strategyTag.IsEmpty())
			payload["stratTags"] = new JArray(strategyTag);
		AddTrigger(payload, side, triggerPrice);
		return payload;
	}

	private static void AddTrigger(
		JObject payload,
		Sides side,
		decimal? triggerPrice)
	{
		if (triggerPrice is not > 0)
			return;
		var comparison = side == Sides.Buy ? "atOrAbove" : "atOrBelow";
		payload["entryConfig"] = new JObject
		{
			["triggers"] = new JObject
			{
				["ltp"] = new JObject
				{
					[comparison] = new JObject
					{
						["value"] = triggerPrice.Value.ToNativePrice(
							nameof(triggerPrice)),
					},
				},
			},
		};
	}

	private static void ValidateOrder(
		OrderTypes orderType,
		decimal price,
		TimeInForce? timeInForce,
		decimal? triggerPrice)
	{
		if (orderType is not OrderTypes.Limit and
			not OrderTypes.Market and
			not OrderTypes.Conditional)
		{
			throw new ArgumentOutOfRangeException(
				nameof(orderType),
				orderType,
				"Nubra supports market, limit, and trigger-entry orders.");
		}
		if (timeInForce == TimeInForce.MatchOrCancel)
			throw new NotSupportedException(
				"Nubra REST API V3 does not expose fill-or-kill orders.");
		if (orderType == OrderTypes.Limit && price <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(price),
				price,
				"A positive price is required for a Nubra limit order.");
		}
		if (orderType == OrderTypes.Conditional &&
			triggerPrice is not > 0)
		{
			throw new InvalidOperationException(
				"A positive trigger price is required for a Nubra trigger-entry order.");
		}
	}

	private static long ToWholeQuantity(
		decimal value,
		string parameterName)
	{
		if (value <= 0 ||
			value != decimal.Truncate(value) ||
			value > long.MaxValue)
		{
			throw new ArgumentOutOfRangeException(
				parameterName,
				value,
				"Nubra quantities must be positive whole numbers within Int64 range.");
		}
		return decimal.ToInt64(value);
	}

	private static string ToPriceType(
		OrderTypes orderType,
		decimal price)
		=> orderType == OrderTypes.Market ||
			orderType == OrderTypes.Conditional && price <= 0
				? "MARKET"
				: "LIMIT";

	private static string ToValidity(
		OrderTypes orderType,
		TimeInForce? timeInForce)
		=> orderType == OrderTypes.Market ||
			orderType == OrderTypes.Conditional &&
			timeInForce == TimeInForce.CancelBalance ||
			timeInForce == TimeInForce.CancelBalance
				? "IOC"
				: "DAY";

	private static NubraOrderCondition CreateCondition(
		NubraProducts product,
		decimal? triggerPrice,
		string strategyTag)
		=> new()
		{
			Product = product,
			TriggerPrice = triggerPrice,
			StrategyTag = strategyTag,
		};

	private DateTime GetOrderTime(NubraOrder order)
		=> order.Timestamps?.UpdatedAt.ToNubraTime() ??
			order.Timestamps?.FilledAt.ToNubraTime() ??
			order.Timestamps?.CancelledAt.ToNubraTime() ??
			order.Timestamps?.RejectedAt.ToNubraTime() ??
			order.Timestamps?.SentAt.ToNubraTime() ??
			order.Timestamps?.CreatedAt.ToNubraTime() ??
			CurrentTime;
}
