namespace StockSharp.Ventura;

public partial class VenturaMessageAdapter
{
	private readonly SynchronizedDictionary<string, long> _orderTransactions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, string> _transactionOrders =
		[];
	private readonly SynchronizedSet<string> _tradeIds =
		new(StringComparer.OrdinalIgnoreCase);
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
		var condition = regMsg.Condition as VenturaOrderCondition;
		var product = condition?.Product ?? DefaultProduct;
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		var payload = CreateOrderPayload(
			instrument,
			regMsg.Volume,
			regMsg.Side,
			product,
			orderType,
			regMsg.Price,
			regMsg.TimeInForce,
			condition?.TriggerPrice,
			condition?.DisclosedVolume,
			condition?.AfterMarket ?? false);
		var orderId = await _restClient.PlaceOrder(
			product,
			payload,
			cancellationToken);

		RememberOrder(orderId, regMsg.TransactionId);
		await SendOutMessageAsync(
			new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				OriginalTransactionId = regMsg.TransactionId,
				OrderStringId = orderId,
				SecurityId = regMsg.SecurityId,
				PortfolioName = _resolvedPortfolioName,
				OrderType = orderType,
				Side = regMsg.Side,
				TimeInForce =
					regMsg.TimeInForce ?? TimeInForce.PutInQueue,
				OrderPrice = regMsg.Price,
				OrderVolume = regMsg.Volume,
				Balance = regMsg.Volume,
				OrderState = OrderStates.Pending,
				ServerTime = CurrentTime,
				Condition = CreateCondition(
					product,
					condition?.TriggerPrice,
					condition?.DisclosedVolume,
					condition?.AfterMarket ?? false,
					condition?.Remarks),
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
		var condition = replaceMsg.Condition as VenturaOrderCondition;
		var orderType = replaceMsg.OrderType ??
			current.OrderType.ToOrderType();
		var payload = CreateModifyPayload(
			current.OrderId,
			replaceMsg.Volume,
			orderType,
			replaceMsg.Price,
			replaceMsg.TimeInForce ?? current.Validity.ToTimeInForce(),
			condition?.TriggerPrice ?? Positive(current.TriggerPrice),
			condition?.DisclosedVolume ??
				Positive(current.DisclosedQuantityRemaining),
			condition?.Remarks);
		var orderId = await _restClient.ModifyOrder(
			payload,
			cancellationToken);
		RememberOrder(
			orderId.IsEmpty(current.OrderId),
			replaceMsg.TransactionId);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePortfolio(cancelMsg.PortfolioName);
		var order = await ResolveOrder(
			cancelMsg.OrderStringId,
			cancelMsg.OriginalTransactionId,
			cancellationToken);
		await _restClient.CancelOrder(
			order.OrderId,
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

		foreach (var order in (await _restClient.GetOrders(
			cancellationToken))
			.Where(order => order != null && !order.OrderId.IsEmpty())
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

		foreach (var trade in await _restClient.GetTrades(
			cancellationToken))
		{
			await ProcessTrade(
				trade,
				originalTransactionId,
				isLookup,
				cancellationToken);
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
		var funds = await _restClient.GetFunds(cancellationToken);
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
				Positive(funds.TotalMargin?.Total ?? 0m),
				true)
			.TryAdd(
				PositionChangeTypes.CurrentValue,
				funds.AvailableToTrade,
				true)
			.TryAdd(
				PositionChangeTypes.BlockedValue,
				funds.UtilizedMargin?.Total ?? 0m,
				true),
			cancellationToken);

		foreach (var position in await _restClient.GetPositions(
			cancellationToken))
		{
			if (position == null ||
				position.Symbol.IsEmpty() &&
				position.Token.IsEmpty())
				continue;
			var quantity = position.TotalQuantity;
			if (quantity > 0 && position.Action.ToSide() == Sides.Sell)
				quantity = -quantity;
			await SendOutMessageAsync(
				new PositionChangeMessage
				{
					OriginalTransactionId = originalTransactionId,
					PortfolioName = _resolvedPortfolioName,
					SecurityId = await GetSecurityId(
						position.Exchange,
						position.Token,
						position.Symbol,
						null,
						cancellationToken),
					ServerTime = CurrentTime,
				}
				.TryAdd(
					PositionChangeTypes.CurrentValue,
					quantity,
					true)
				.TryAdd(
					PositionChangeTypes.AveragePrice,
					Positive(position.AverageTradedPrice),
					true)
				.TryAdd(
					PositionChangeTypes.CurrentPrice,
					Positive(position.LastTradedPrice),
					true)
				.TryAdd(
					PositionChangeTypes.UnrealizedPnL,
					position.ProfitLoss,
					true),
				cancellationToken);
		}

		foreach (var holding in await _restClient.GetHoldings(
			cancellationToken))
		{
			if (holding == null ||
				holding.Symbol.IsEmpty() &&
				holding.Isin.IsEmpty())
				continue;
			await SendOutMessageAsync(
				new PositionChangeMessage
				{
					OriginalTransactionId = originalTransactionId,
					PortfolioName = _resolvedPortfolioName,
					SecurityId = await GetSecurityId(
						holding.Exchange,
						null,
						holding.Symbol,
						holding.Isin,
						cancellationToken),
					ServerTime = CurrentTime,
				}
				.TryAdd(
					PositionChangeTypes.CurrentValue,
					holding.Quantity,
					true)
				.TryAdd(
					PositionChangeTypes.AveragePrice,
					Positive(holding.AverageTradedPrice),
					true)
				.TryAdd(
					PositionChangeTypes.CurrentPrice,
					Positive(holding.LastTradedPrice),
					true)
				.TryAdd(
					PositionChangeTypes.UnrealizedPnL,
					holding.ProfitLoss,
					true),
				cancellationToken);
		}
	}

	private async ValueTask ProcessOrder(
		VenturaOrder order,
		long originId,
		bool isLookup,
		CancellationToken cancellationToken)
	{
		if (order?.OrderId.IsEmpty() != false)
			return;

		_orderTransactions.TryGetValue(
			order.OrderId,
			out var transactionId);
		RememberOrder(order.OrderId, transactionId);
		var state = order.Status.ToOrderState();
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
				OrderStringId = order.OrderId,
				SecurityId = await GetSecurityId(
					order.Exchange,
					order.Token,
					order.Symbol,
					null,
					cancellationToken),
				PortfolioName = _resolvedPortfolioName,
				OrderType = order.OrderType.ToOrderType(),
				Side = order.Action.ToSide(),
				TimeInForce = order.Validity.ToTimeInForce(),
				OrderPrice = order.Price,
				OrderVolume = order.TotalQuantity,
				Balance = Math.Max(0, order.PendingQuantity),
				AveragePrice = Positive(order.AverageTradedPrice),
				OrderState = state,
				ServerTime = GetOrderTime(order),
				Condition = CreateCondition(
					order.ProductType.ToProduct(),
					Positive(order.TriggerPrice),
					Positive(order.DisclosedQuantityRemaining),
					false,
					null),
				Error = state == OrderStates.Failed
					? new InvalidOperationException(
						order.Reason
							.IsEmpty(
								$"Ventura EaseAPI order status: {order.Status}."))
					: null,
			},
			cancellationToken);
	}

	private async ValueTask ProcessTrade(
		VenturaTrade trade,
		long originId,
		bool isLookup,
		CancellationToken cancellationToken)
	{
		if (trade == null ||
			trade.OrderId.IsEmpty() ||
			trade.Quantity <= 0)
			return;
		var tradeId = trade.TradeId.IsEmpty(
			$"{trade.OrderId}:{trade.FilledTimestamp}:{trade.AveragePrice}:{trade.Quantity}");
		if (!_tradeIds.TryAdd(tradeId))
			return;

		var transactionId =
			_orderTransactions.TryGetValue2(trade.OrderId) ?? 0;
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
				OrderStringId = trade.OrderId,
				TradeStringId = tradeId,
				SecurityId = await GetSecurityId(
					trade.Exchange,
					trade.Token,
					trade.Symbol,
					null,
					cancellationToken),
				PortfolioName = _resolvedPortfolioName,
				Side = trade.TransactionType.ToSide(),
				TradePrice = trade.AveragePrice,
				TradeVolume = trade.Quantity,
				ServerTime = trade.FilledTimestamp.ToVenturaTime(
					CurrentTime),
			},
			cancellationToken);
	}

	private async Task<VenturaOrder> ResolveOrder(
		string orderId,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (orderId.IsEmpty())
			_transactionOrders.TryGetValue(originalTransactionId, out orderId);
		if (orderId.IsEmpty())
		{
			throw new InvalidOperationException(
				LocalizedStrings.OrderNoExchangeId.Put(
					originalTransactionId));
		}

		foreach (var order in await _restClient.GetOrders(cancellationToken))
		{
			if (order?.OrderId.IsEmpty() != false)
				continue;
			_orderTransactions.TryGetValue(
				order.OrderId,
				out var transactionId);
			RememberOrder(order.OrderId, transactionId);
			if (order.OrderId.EqualsIgnoreCase(orderId) ||
				transactionId != 0 &&
					transactionId == originalTransactionId)
				return order;
		}

		throw new InvalidOperationException(
			$"Ventura EaseAPI order '{orderId}' was not found in the current order book.");
	}

	private async Task<SecurityId> GetSecurityId(
		string exchange,
		string token,
		string symbol,
		string isin,
		CancellationToken cancellationToken)
	{
		VenturaInstrument instrument = null;
		if (!token.IsEmpty())
		{
			instrument = await _restClient.GetInstrument(
				exchange,
				token,
				cancellationToken);
		}
		if (instrument == null && !symbol.IsEmpty())
		{
			instrument = await _restClient.FindInstrument(
				exchange,
				symbol,
				cancellationToken);
		}
		if (instrument != null)
		{
			var result = instrument.ToSecurityId();
			if (!isin.IsEmpty())
				result = result with { Isin = isin };
			RememberInstrument(
				instrument.ToStreamKey(false),
				result,
				instrument);
			return result;
		}

		return new()
		{
			SecurityCode = symbol.IsEmpty(token).IsEmpty(isin),
			BoardCode = exchange,
			Native = !exchange.IsEmpty() && !token.IsEmpty()
				? VenturaExtensions.CreateInstrumentKey(exchange, token)
				: null,
			Isin = isin,
		};
	}

	private void RememberOrder(string orderId, long transactionId)
	{
		if (orderId.IsEmpty() || transactionId == 0)
			return;
		_orderTransactions[orderId] = transactionId;
		_transactionOrders[transactionId] = orderId;
	}

	private void EnsurePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(_resolvedPortfolioName))
		{
			throw new InvalidOperationException(
				LocalizedStrings.AccountNotFound);
		}
	}

	internal static JObject CreateOrderPayload(
		VenturaInstrument instrument,
		decimal volume,
		Sides side,
		VenturaProducts product,
		OrderTypes orderType,
		decimal price,
		TimeInForce? timeInForce,
		decimal? triggerPrice,
		decimal? disclosedVolume,
		bool afterMarket)
	{
		ArgumentNullException.ThrowIfNull(instrument);
		if (instrument.ToSecurityType() == SecurityTypes.Index)
		{
			throw new NotSupportedException(
				"Ventura EaseAPI index instruments cannot be traded.");
		}
		if (!long.TryParse(
			instrument.ExchangeToken,
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out var instrumentId) ||
			instrumentId <= 0)
		{
			throw new InvalidOperationException(
				"Ventura EaseAPI trading instrument has no numeric exchange token.");
		}

		var payload = CreateMutableOrderFields(
			volume,
			orderType,
			price,
			timeInForce,
			triggerPrice,
			disclosedVolume);
		payload["instrument_id"] = instrumentId;
		payload["exchange"] = instrument.Exchange
			.ThrowIfEmpty(nameof(instrument.Exchange));
		payload["segment"] = instrument.ToOrderSegment();
		payload["transaction_type"] = side.ToNative();
		payload["product"] = product.ToNative();
		payload["off_market_flag"] = afterMarket ? 1 : 0;
		return payload;
	}

	internal static JObject CreateModifyPayload(
		string orderId,
		decimal volume,
		OrderTypes orderType,
		decimal price,
		TimeInForce? timeInForce,
		decimal? triggerPrice,
		decimal? disclosedVolume,
		string remarks)
	{
		var payload = CreateMutableOrderFields(
			volume,
			orderType,
			price,
			timeInForce,
			triggerPrice,
			disclosedVolume);
		payload["disc_quantity"] = payload["disclosed_quantity"];
		payload.Remove("disclosed_quantity");
		payload["remarks"] = remarks.IsEmpty(string.Empty);
		payload["order_no"] = orderId.ThrowIfEmpty(nameof(orderId));
		return payload;
	}

	private static JObject CreateMutableOrderFields(
		decimal volume,
		OrderTypes orderType,
		decimal price,
		TimeInForce? timeInForce,
		decimal? triggerPrice,
		decimal? disclosedVolume)
	{
		var quantity = ToWholeQuantity(volume, nameof(volume));
		var disclosed = disclosedVolume ?? 0m;
		if (disclosed < 0 ||
			disclosed != decimal.Truncate(disclosed) ||
			disclosed > quantity)
		{
			throw new ArgumentOutOfRangeException(
				nameof(disclosedVolume),
				disclosed,
				"Disclosed quantity must be a whole number no greater than order quantity.");
		}
		if (timeInForce == TimeInForce.MatchOrCancel)
		{
			throw new NotSupportedException(
				"Ventura EaseAPI does not expose fill-or-kill orders.");
		}
		if (orderType is not OrderTypes.Market and
			not OrderTypes.Limit and
			not OrderTypes.Conditional)
		{
			throw new ArgumentOutOfRangeException(
				nameof(orderType),
				orderType,
				"Ventura EaseAPI supports market, limit, stop-limit, and stop-market orders.");
		}
		if (orderType == OrderTypes.Limit && price <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(price),
				price,
				"A positive price is required for a limit order.");
		}
		if (orderType == OrderTypes.Conditional &&
			triggerPrice is not > 0)
		{
			throw new InvalidOperationException(
				"A positive trigger price is required for a stop order.");
		}

		return new()
		{
			["order_type"] = orderType switch
			{
				OrderTypes.Market => "MKT",
				OrderTypes.Limit => "LMT",
				OrderTypes.Conditional when price > 0 => "SL",
				OrderTypes.Conditional => "SLM",
				_ => throw new ArgumentOutOfRangeException(
					nameof(orderType)),
			},
			["quantity"] = quantity,
			["price"] = orderType == OrderTypes.Market ? 0 : price,
			["trigger_price"] = triggerPrice ?? 0,
			["disclosed_quantity"] = decimal.ToInt64(disclosed),
			["validity"] = timeInForce == TimeInForce.CancelBalance
				? "IOC"
				: "DAY",
		};
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
				"Ventura EaseAPI quantities must be positive whole numbers within Int64 range.");
		}
		return decimal.ToInt64(value);
	}

	private static VenturaOrderCondition CreateCondition(
		VenturaProducts product,
		decimal? triggerPrice,
		decimal? disclosedVolume,
		bool afterMarket,
		string remarks)
		=> new()
		{
			Product = product,
			TriggerPrice = triggerPrice,
			DisclosedVolume = disclosedVolume,
			AfterMarket = afterMarket,
			Remarks = remarks,
		};

	private DateTime GetOrderTime(VenturaOrder order)
		=> order.OrderDateTime.ToVenturaTime(CurrentTime);
}
