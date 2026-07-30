namespace StockSharp.HdfcSecurities;

public partial class HdfcMessageAdapter
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
		var condition = regMsg.Condition as HdfcOrderCondition;
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
			condition?.AfterMarket ?? false,
			condition?.ExternalReferenceNumber);
		var orderId = await _restClient.PlaceOrder(
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
					condition?.AfterMarket ?? false,
					condition?.ExternalReferenceNumber),
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
		var condition = replaceMsg.Condition as HdfcOrderCondition;
		var product = condition?.Product ?? current.Product.ToProduct();
		var orderType = replaceMsg.OrderType ?? current.OrderType.ToOrderType();
		var triggerPrice = condition?.TriggerPrice ??
			Positive(current.TriggerPrice);
		var afterMarket = condition?.AfterMarket ?? false;
		var payload = CreateModifyPayload(
			replaceMsg.Volume,
			product,
			orderType,
			replaceMsg.Price,
			replaceMsg.TimeInForce ?? current.Validity.ToTimeInForce(),
			triggerPrice,
			current.DisclosedQuantity,
			afterMarket);
		var orderId = await _restClient.ModifyOrder(
			current.OrderId,
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
		var margins = await _restClient.GetMargins(cancellationToken);
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
				margins.Total,
				true)
			.TryAdd(
				PositionChangeTypes.CurrentValue,
				margins.Available,
				true)
			.TryAdd(
				PositionChangeTypes.BlockedValue,
				margins.Utilized,
				true),
			cancellationToken);

		foreach (var position in await _restClient.GetPositions(
			cancellationToken))
		{
			if (position == null ||
				position.Exchange.IsEmpty() ||
				position.SecurityId.IsEmpty())
				continue;
			var averagePrice = position.NetQuantity < 0
				? position.AverageSellPrice
				: position.AverageBuyPrice;
			await SendOutMessageAsync(
				new PositionChangeMessage
				{
					OriginalTransactionId = originalTransactionId,
					PortfolioName = _resolvedPortfolioName,
					SecurityId = await GetSecurityId(
						position.Exchange,
						position.SecurityId,
						position.UnderlyingSymbol,
						null,
						cancellationToken),
					ServerTime = CurrentTime,
				}
				.TryAdd(
					PositionChangeTypes.CurrentValue,
					position.NetQuantity,
					true)
				.TryAdd(
					PositionChangeTypes.AveragePrice,
					Positive(averagePrice),
					true)
				.TryAdd(
					PositionChangeTypes.RealizedPnL,
					position.RealizedPnL,
					true),
				cancellationToken);
		}

		foreach (var holding in await _restClient.GetHoldings(
			cancellationToken))
		{
			if (holding == null ||
				holding.Exchange.IsEmpty() ||
				holding.SecurityId.IsEmpty() &&
				holding.CompanyName.IsEmpty() &&
				holding.Isin.IsEmpty())
				continue;
			await SendOutMessageAsync(
				new PositionChangeMessage
				{
					OriginalTransactionId = originalTransactionId,
					PortfolioName = _resolvedPortfolioName,
					SecurityId = await GetSecurityId(
						holding.Exchange,
						holding.SecurityId,
						holding.CompanyName,
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
					Positive(holding.AveragePrice),
					true)
				.TryAdd(
					PositionChangeTypes.CurrentPrice,
					Positive(holding.ClosePrice),
					true),
				cancellationToken);
		}
	}

	private async ValueTask ProcessOrder(
		HdfcOrder order,
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
		var balance = Math.Max(0, order.PendingQuantity);
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
				OrderBoardId = order.ExchangeOrderId,
				SecurityId = await GetSecurityId(
					order.Exchange,
					order.SecurityId,
					order.CompanyName,
					order.Isin,
					cancellationToken),
				PortfolioName = _resolvedPortfolioName,
				OrderType = order.OrderType.ToOrderType(),
				Side = order.TransactionType.ToSide(),
				TimeInForce = order.Validity.ToTimeInForce(),
				OrderPrice = order.Price,
				OrderVolume = order.Quantity,
				Balance = balance,
				AveragePrice = Positive(order.AveragePrice),
				OrderState = state,
				ServerTime = GetOrderTime(order),
				Condition = CreateCondition(
					order.Product.ToProduct(),
					Positive(order.TriggerPrice),
					false,
					ParseExternalReference(
						order.ExternalReferenceNumber)),
				Error = state == OrderStates.Failed
					? new InvalidOperationException(
						order.StatusMessage
							.IsEmpty(order.StatusMessageRaw)
							.IsEmpty(
								$"HDFC Securities order status: {order.Status}."))
					: null,
			},
			cancellationToken);
	}

	private async ValueTask ProcessTrade(
		HdfcTrade trade,
		long originId,
		bool isLookup,
		CancellationToken cancellationToken)
	{
		if (trade == null ||
			trade.OrderId.IsEmpty() ||
			trade.FilledQuantity <= 0)
			return;
		var tradeId = trade.TradeId.IsEmpty(
			$"{trade.OrderId}:{trade.FillTimestamp}:{trade.AveragePrice}:{trade.FilledQuantity}");
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
				OrderBoardId = trade.ExchangeOrderId,
				TradeStringId = tradeId,
				SecurityId = await GetSecurityId(
					trade.Exchange,
					trade.SecurityId,
					trade.CompanyName,
					trade.Isin,
					cancellationToken),
				PortfolioName = _resolvedPortfolioName,
				Side = trade.TransactionType.ToSide(),
				TradePrice = trade.AveragePrice,
				TradeVolume = trade.FilledQuantity,
				ServerTime = trade.FillTimestamp.ToHdfcTime(CurrentTime),
			},
			cancellationToken);
	}

	private async Task<HdfcOrder> ResolveOrder(
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
			$"HDFC Securities order '{orderId}' was not found in the current order book.");
	}

	private async Task<SecurityId> GetSecurityId(
		string exchange,
		string securityId,
		string symbol,
		string isin,
		CancellationToken cancellationToken)
	{
		if (!exchange.IsEmpty() && !securityId.IsEmpty())
		{
			var instrument = await _restClient.GetInstrument(
				exchange,
				securityId,
				cancellationToken);
			if (instrument != null)
			{
				var result = instrument.ToSecurityId();
				if (!isin.IsEmpty())
					result = result with { Isin = isin };
				RememberInstrument(
					instrument.ToStreamId(),
					result,
					instrument);
				return result;
			}
		}

		return new()
		{
			SecurityCode = symbol.IsEmpty(securityId).IsEmpty(isin),
			BoardCode = exchange,
			Native = !exchange.IsEmpty() && !securityId.IsEmpty()
				? HdfcExtensions.CreateInstrumentKey(exchange, securityId)
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
		HdfcInstrument instrument,
		decimal volume,
		Sides side,
		HdfcProducts product,
		OrderTypes orderType,
		decimal price,
		TimeInForce? timeInForce,
		decimal? triggerPrice,
		bool afterMarket,
		long? externalReferenceNumber)
	{
		ArgumentNullException.ThrowIfNull(instrument);
		var payload = CreateModifyPayload(
			volume,
			product,
			orderType,
			price,
			timeInForce,
			triggerPrice,
			0,
			afterMarket);
		payload["exchange"] = instrument.Exchange
			.ThrowIfEmpty(nameof(instrument.Exchange));
		payload["security_id"] = instrument.SecurityId
			.ThrowIfEmpty(nameof(instrument.SecurityId));
		payload["instrument_segment"] = instrument.InstrumentSegment
			.ThrowIfEmpty(nameof(instrument.InstrumentSegment));
		payload["transaction_type"] = side.ToNative();

		if (instrument.ToSecurityType() is
			SecurityTypes.Future or SecurityTypes.Option)
		{
			payload["underlying_symbol"] = instrument.UnderlyingSymbol
				.ThrowIfEmpty(nameof(instrument.UnderlyingSymbol));
			payload["expiry_date"] = instrument.ExpiryDate
				.ThrowIfEmpty(nameof(instrument.ExpiryDate));
		}
		if (instrument.ToSecurityType() == SecurityTypes.Option)
		{
			payload["strike_price"] = instrument.StrikePrice ??
				throw new InvalidOperationException(
					"HDFC Securities option has no strike price.");
			payload["option_type"] = instrument.OptionType
				.ThrowIfEmpty(nameof(instrument.OptionType));
		}
		if (externalReferenceNumber is { } externalReference)
		{
			if (externalReference <= 0)
			{
				throw new ArgumentOutOfRangeException(
					nameof(externalReferenceNumber),
					externalReference,
					"External reference must be a positive number.");
			}
			payload["external_reference_number"] = externalReference;
		}
		return payload;
	}

	internal static JObject CreateModifyPayload(
		decimal volume,
		HdfcProducts product,
		OrderTypes orderType,
		decimal price,
		TimeInForce? timeInForce,
		decimal? triggerPrice,
		decimal disclosedQuantity,
		bool afterMarket)
	{
		var quantity = ToWholeQuantity(volume, nameof(volume));
		if (disclosedQuantity < 0 ||
			disclosedQuantity != decimal.Truncate(disclosedQuantity) ||
			disclosedQuantity > quantity)
		{
			throw new ArgumentOutOfRangeException(
				nameof(disclosedQuantity),
				disclosedQuantity,
				"Disclosed quantity must be a whole number no greater than order quantity.");
		}
		if (timeInForce == TimeInForce.MatchOrCancel)
		{
			throw new NotSupportedException(
				"HDFC Securities does not expose fill-or-kill orders.");
		}
		if (orderType is not OrderTypes.Market and
			not OrderTypes.Limit and
			not OrderTypes.Conditional)
		{
			throw new ArgumentOutOfRangeException(
				nameof(orderType),
				orderType,
				"HDFC Securities supports market, limit, stop-limit, and stop-market orders.");
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
			["product"] = product.ToNative(),
			["quantity"] = quantity,
			["order_type"] = orderType switch
			{
				OrderTypes.Market => "MARKET",
				OrderTypes.Limit => "LIMIT",
				OrderTypes.Conditional when price > 0 => "SL",
				OrderTypes.Conditional => "SL-M",
				_ => throw new ArgumentOutOfRangeException(
					nameof(orderType)),
			},
			["price"] = orderType == OrderTypes.Market ? 0 : price,
			["trigger_price"] = triggerPrice ?? 0,
			["disclosed_quantity"] =
				decimal.ToInt64(disclosedQuantity),
			["validity"] = timeInForce == TimeInForce.CancelBalance
				? "IOC"
				: "DAY",
			["amo"] = afterMarket,
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
				"HDFC Securities quantities must be positive whole numbers within Int64 range.");
		}
		return decimal.ToInt64(value);
	}

	private static HdfcOrderCondition CreateCondition(
		HdfcProducts product,
		decimal? triggerPrice,
		bool afterMarket,
		long? externalReferenceNumber)
		=> new()
		{
			Product = product,
			TriggerPrice = triggerPrice,
			AfterMarket = afterMarket,
			ExternalReferenceNumber = externalReferenceNumber,
		};

	private static long? ParseExternalReference(string value)
		=> long.TryParse(
			value,
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out var reference)
				? reference
				: null;

	private DateTime GetOrderTime(HdfcOrder order)
		=> order.OrderTimestamp.ToHdfcTime(CurrentTime);
}
