namespace StockSharp.PhillipPoems;

public partial class PhillipPoemsMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		ResolvePortfolio(regMsg.PortfolioName);
		var quantity = GetOrderQuantity(regMsg.Volume, nameof(regMsg.Volume));
		var condition = regMsg.Condition as PhillipPoemsOrderCondition ?? new();
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not OrderTypes.Limit and not OrderTypes.Conditional)
			throw new NotSupportedException(
				$"Phillip POEMS supports limit, stop-limit, and limit-if-touched stock orders, not {orderType}.");
		if (regMsg.Price <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Price), regMsg.Price,
				"Phillip POEMS requires a positive limit price.");
		if (orderType == OrderTypes.Conditional && condition.StopPrice is not > 0)
			throw new ArgumentOutOfRangeException(nameof(condition.StopPrice), condition.StopPrice,
				"Phillip POEMS requires a positive trigger price for conditional orders.");
		if (condition.IsShortSale && regMsg.Side != Sides.Sell)
			throw new InvalidOperationException(
				"The Phillip POEMS short-sell action can be used only with the sell side.");
		if (regMsg.TimeInForce is TimeInForce.CancelBalance or TimeInForce.MatchOrCancel)
			throw new NotSupportedException(
				"The documented Phillip POEMS stock API does not expose IOC or FOK validity.");
		if (!Enum.IsDefined(condition.PaymentMode))
			throw new ArgumentOutOfRangeException(nameof(condition.PaymentMode),
				condition.PaymentMode, "Unknown Phillip POEMS payment mode.");

		var counter = await ResolveCounter(regMsg.SecurityId, cancellationToken);
		if (!counter.Product.IsEmpty() && !counter.Product.EqualsIgnoreCase("ST"))
			throw new NotSupportedException(
				$"The documented Phillip POEMS trading endpoints accept stock counters, not '{counter.Product}'.");
		var exchange = counter.Exchange.IsEmpty(regMsg.SecurityId.BoardCode)
			.IsEmpty(DefaultExchange);
		var request = new PoemsOrderRequest
		{
			CounterId = counter.CounterId,
			Action = condition.IsShortSale ? PoemsOrderActions.ShortSell
				: regMsg.Side == Sides.Sell ? PoemsOrderActions.Sell : PoemsOrderActions.Buy,
			OrderType = orderType == OrderTypes.Conditional
				? condition.IsLimitIfTouched ? PoemsOrderTypes.LimitIfTouched
					: PoemsOrderTypes.StopLimit
				: PoemsOrderTypes.Limit,
			LimitPrice = regMsg.Price,
			TriggerPrice = orderType == OrderTypes.Conditional ? condition.StopPrice : null,
			Quantity = quantity,
			SettlementCurrency = condition.SettlementCurrency
				.IsEmpty(DefaultSettlementCurrency).ToUpperInvariant(),
			Payment = condition.PaymentMode,
			TriggerPriceType = orderType == OrderTypes.Conditional
				? PoemsTriggerPriceTypes.LastDone : null,
			GoodTillDate = regMsg.TillDate?.ToExchangeLocal(exchange)
				.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
		};
		var encryptedPin = EncryptedPin?.UnSecure();
		var validation = await _client.ValidateOrder(request, encryptedPin, cancellationToken);
		if (validation.AuthToken.IsEmpty())
		{
			if (validation.IsTwoFactorRequired)
				throw new NotSupportedException(
					"Phillip POEMS requires an interactive two-factor confirmation for this order.");
			if (validation.IsPasswordRequired && encryptedPin.IsEmpty())
				throw new InvalidOperationException(
					"Phillip POEMS requires EncryptedPin for this order.");
			throw new InvalidOperationException(
				"Phillip POEMS validated the order without returning the required auth token.");
		}

		var submitted = await _client.SubmitOrder(request, validation.AuthToken,
			cancellationToken);
		var orderId = submitted.OrderNo?.ToString(CultureInfo.InvariantCulture);
		if (orderId.IsEmpty())
			throw new InvalidOperationException(
				"Phillip POEMS accepted the order without returning an order number.");

		var submittedTime = CurrentTime.ToExchangeLocal(exchange)
			.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
		var order = new PoemsOrder
		{
			CounterId = counter.CounterId,
			Symbol = counter.Symbol.IsEmpty(regMsg.SecurityId.SecurityCode),
			Product = counter.Product.IsEmpty("ST"),
			Action = ((int)request.Action).ToString(CultureInfo.InvariantCulture),
			Status = "OR",
			SubmittedPrice = regMsg.Price.ToString(CultureInfo.InvariantCulture),
			SubmittedQuantity = quantity.ToString(CultureInfo.InvariantCulture),
			RemainingQuantity = quantity.ToString(CultureInfo.InvariantCulture),
			SubmittedTime = submittedTime,
			UpdatedTime = submittedTime,
			OrderNo = orderId,
			StopPrice = request.TriggerPrice?.ToString(CultureInfo.InvariantCulture),
			OrderType = request.OrderType switch
			{
				PoemsOrderTypes.StopLimit => "SLO",
				PoemsOrderTypes.LimitIfTouched => "LIT",
				_ => "LO",
			},
			Market = counter.Market.IsEmpty(DefaultMarket),
			Exchange = exchange,
			PaymentCurrency = request.SettlementCurrency,
		};
		_orders[orderId] = order;
		_orderTransactions[orderId] = regMsg.TransactionId;
		_transactionOrders[regMsg.TransactionId] = orderId;
		await ProcessOrder(order, regMsg.TransactionId, false, true, cancellationToken);
		_lastPoll = CurrentTime;
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		var accountNo = ResolvePortfolio(replaceMsg.PortfolioName);
		var orderId = GetOrderId(replaceMsg.OldOrderStringId,
			replaceMsg.OriginalTransactionId);
		var quantity = GetOrderQuantity(replaceMsg.Volume, nameof(replaceMsg.Volume));
		var order = await GetOrderForMutation(orderId, cancellationToken);
		var currentPrice = order?.SubmittedPrice.ToDecimalValue();
		if (replaceMsg.Price > 0 && (currentPrice == null || replaceMsg.Price != currentPrice))
			throw new NotSupportedException(
				"The documented Phillip POEMS amend endpoint changes quantity only; price replacement is not supported.");

		var counterId = order?.CounterId;
		if (counterId.IsEmpty())
			counterId = (await ResolveCounter(replaceMsg.SecurityId, cancellationToken)).CounterId;
		await _client.AmendOrder(orderId, new PoemsAmendOrderRequest
		{
			CounterId = counterId,
			Quantity = quantity,
		}, EncryptedPin?.UnSecure(), cancellationToken);

		if (order != null)
		{
			order.SubmittedQuantity = quantity.ToString(CultureInfo.InvariantCulture);
			var executed = order.ExecutedQuantity.ToDecimalValue() ?? 0;
			order.RemainingQuantity = Math.Max(0, quantity - executed)
				.ToString(CultureInfo.InvariantCulture);
			order.UpdatedTime = CurrentTime.ToExchangeLocal(
				order.Exchange.IsEmpty(DefaultExchange))
				.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
			_orders[orderId] = order;
		}
		_orderTransactions[orderId] = replaceMsg.TransactionId;
		_transactionOrders[replaceMsg.TransactionId] = orderId;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = replaceMsg.TransactionId,
			OrderStringId = orderId,
			PortfolioName = accountNo,
			SecurityId = order?.ToSecurityId(DefaultExchange) ?? replaceMsg.SecurityId,
			OrderPrice = currentPrice ?? replaceMsg.Price,
			OrderVolume = quantity,
			OrderState = OrderStates.Pending,
			ServerTime = CurrentTime,
		}, cancellationToken);
		_lastPoll = CurrentTime;
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		var accountNo = ResolvePortfolio(cancelMsg.PortfolioName);
		var orderId = GetOrderId(cancelMsg.OrderStringId,
			cancelMsg.OriginalTransactionId);
		var order = await GetOrderForMutation(orderId, cancellationToken);
		var counterId = order?.CounterId;
		if (counterId.IsEmpty())
			counterId = (await ResolveCounter(cancelMsg.SecurityId, cancellationToken)).CounterId;
		_cancelTransactions[orderId] = cancelMsg.TransactionId;
		try
		{
			await _client.CancelOrder(orderId, new PoemsCancelOrderRequest
			{
				CounterId = counterId,
			}, EncryptedPin?.UnSecure(), cancellationToken);
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				OriginalTransactionId = cancelMsg.TransactionId,
				OrderStringId = orderId,
				PortfolioName = accountNo,
				SecurityId = order?.ToSecurityId(DefaultExchange) ?? cancelMsg.SecurityId,
				OrderState = OrderStates.Pending,
				ServerTime = CurrentTime,
			}, cancellationToken);
			_lastPoll = CurrentTime;
		}
		catch
		{
			_cancelTransactions.Remove(orderId);
			throw;
		}
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

		ResolvePortfolio(statusMsg.PortfolioName);
		await SendOrderSnapshot(statusMsg.TransactionId, statusMsg, true, cancellationToken);
		if (statusMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
		else
		{
			_orderStatusSubscriptionId = statusMsg.TransactionId;
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		}
		_lastPoll = CurrentTime;
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

		ResolvePortfolio(lookupMsg.PortfolioName);
		await SendPortfolioSnapshot(lookupMsg.TransactionId, lookupMsg.PortfolioName, true,
			cancellationToken);
		if (lookupMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
		else
		{
			_portfolioSubscriptionId = lookupMsg.TransactionId;
			_portfolioFilter = lookupMsg.PortfolioName;
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		}
		_lastPoll = CurrentTime;
	}

	private async Task SendOrderSnapshot(long originalTransactionId, OrderStatusMessage filter,
		bool isLookup, CancellationToken cancellationToken)
	{
		ResolvePortfolio(filter?.PortfolioName);
		var response = await _client.GetTodayOrders(cancellationToken);
		var skip = Math.Max(0, filter?.Skip ?? 0);
		var left = Math.Max(0, filter?.Count ?? long.MaxValue);
		foreach (var order in (response.Orders ?? [])
			.Where(item => IsOrderMatch(item, filter))
			.OrderByDescending(GetOrderTime))
		{
			if (skip > 0)
			{
				skip--;
				continue;
			}
			if (left <= 0)
				break;
			_orders[order.OrderNo] = order;
			await ProcessOrder(order, originalTransactionId, isLookup, isLookup,
				cancellationToken);
			left--;
		}
	}

	private async ValueTask ProcessOrder(PoemsOrder order, long originalTransactionId,
		bool isLookup, bool isForced, CancellationToken cancellationToken)
	{
		if (order?.OrderNo.IsEmpty() != false)
			return;
		_orders[order.OrderNo] = order;
		var state = order.Status.ToOrderState();
		var volume = order.SubmittedQuantity.ToDecimalValue() ?? 0;
		var filled = order.ExecutedQuantity.ToDecimalValue() ?? 0;
		var balance = order.RemainingQuantity.ToDecimalValue() ??
			(state is OrderStates.Done or OrderStates.Failed ? 0 : Math.Max(0, volume - filled));
		var updateTime = GetOrderTime(order);
		var signature = $"{order.Status}|{volume.ToString(CultureInfo.InvariantCulture)}|" +
			$"{balance.ToString(CultureInfo.InvariantCulture)}|" +
			$"{filled.ToString(CultureInfo.InvariantCulture)}|{order.ExecutedPrice}|{updateTime:O}";
		if (!isForced && _orderSignatures.TryGetValue(order.OrderNo, out var previous) &&
			previous == signature)
			return;
		_orderSignatures[order.OrderNo] = signature;

		var transactionId = _orderTransactions.TryGetValue(order.OrderNo,
			out var knownTransactionId) ? knownTransactionId : 0;
		var originId = isLookup ? originalTransactionId
			: transactionId != 0 ? transactionId : originalTransactionId;
		if (!isLookup && IsCancelled(order.Status) &&
			_cancelTransactions.TryGetValue(order.OrderNo, out var cancelTransactionId))
			originId = cancelTransactionId;

		var orderType = order.OrderType.ToOrderType();
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originId,
			TransactionId = isLookup ? transactionId : 0,
			OrderStringId = order.OrderNo,
			PortfolioName = ResolvePortfolio(null),
			SecurityId = order.ToSecurityId(DefaultExchange),
			Side = order.Action.ToSide(),
			OrderType = orderType,
			OrderPrice = order.SubmittedPrice.ToDecimalValue() ?? 0,
			OrderVolume = volume > 0 ? volume : null,
			Balance = balance,
			OrderState = state,
			TimeInForce = TimeInForce.PutInQueue,
			AveragePrice = order.ExecutedPrice.ToDecimalValue(),
			ServerTime = updateTime,
			Condition = orderType == OrderTypes.Conditional
				? new PhillipPoemsOrderCondition
				{
					StopPrice = order.StopPrice.ToDecimalValue(),
					IsLimitIfTouched = order.OrderType.EqualsIgnoreCase("LIT"),
					SettlementCurrency = order.PaymentCurrency,
				}
				: null,
			Error = state == OrderStates.Failed
				? new InvalidOperationException(order.OrderStatusDescription.IsEmpty(
					$"Phillip POEMS order entered state {order.Status}."))
				: null,
		}, cancellationToken);

		await ProcessAggregateFill(order, originalTransactionId, isLookup,
			cancellationToken);
		if (state is OrderStates.Done or OrderStates.Failed)
			_cancelTransactions.Remove(order.OrderNo);
	}

	private async ValueTask ProcessAggregateFill(PoemsOrder order,
		long originalTransactionId, bool isLookup, CancellationToken cancellationToken)
	{
		var filled = order.ExecutedQuantity.ToDecimalValue() ?? 0;
		var previous = _filledQuantities.TryGetValue(order.OrderNo, out var value) ? value : 0;
		_filledQuantities[order.OrderNo] = Math.Max(previous, filled);
		var delta = isLookup ? filled : Math.Max(0, filled - previous);
		var price = order.ExecutedPrice.ToDecimalValue();
		if (delta <= 0 || price is not > 0)
			return;

		var transactionId = _orderTransactions.TryGetValue(order.OrderNo,
			out var knownTransactionId) ? knownTransactionId : 0;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = isLookup ? originalTransactionId
				: transactionId != 0 ? transactionId : originalTransactionId,
			OrderStringId = order.OrderNo,
			TradeStringId = $"{order.OrderNo}|{filled.ToString(CultureInfo.InvariantCulture)}",
			PortfolioName = ResolvePortfolio(null),
			SecurityId = order.ToSecurityId(DefaultExchange),
			Side = order.Action.ToSide(),
			TradePrice = price,
			TradeVolume = delta,
			ServerTime = order.ExecutedTime.IsEmpty(order.LatestUpdatedTime)
				.IsEmpty(order.UpdatedTime).ToUtc(order.Exchange.IsEmpty(DefaultExchange), CurrentTime),
		}, cancellationToken);
	}

	private async Task SendPortfolioSnapshot(long originalTransactionId, string portfolioName,
		bool isLookup, CancellationToken cancellationToken)
	{
		var accountNo = ResolvePortfolio(portfolioName);
		var accountType = AccountType.IsEmpty("V");
		var detailsTask = _client.GetAccountDetails(accountType, cancellationToken);
		var holdingsTask = _client.GetHoldings(accountType, cancellationToken);
		await Task.WhenAll(detailsTask, holdingsTask);
		var detailsResponse = await detailsTask;
		var holdingsResponse = await holdingsTask;
		var details = detailsResponse.AccountDetails ?? [];
		var holdings = holdingsResponse.Holdings ?? [];
		var currency = details.Select(item => item?.Currency.ToCurrency())
			.FirstOrDefault(item => item != null) ?? DefaultSettlementCurrency.ToCurrency();

		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = accountNo,
			BoardCode = DefaultExchange,
			Currency = currency,
		}, cancellationToken);
		var detailsTime = detailsResponse.LastUpdated.ToUtc(DefaultExchange, CurrentTime);
		foreach (var detail in details)
		{
			if (detail == null)
				continue;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = accountNo,
				SecurityId = SecurityId.Money,
				ServerTime = detailsTime,
			}
			.TryAdd(PositionChangeTypes.BeginValue, detail.OpeningBalance.ToDecimalValue())
			.TryAdd(PositionChangeTypes.CurrentValue, detail.AvailableBalance.ToDecimalValue(), true)
			.TryAdd(PositionChangeTypes.BlockedValue, detail.OutstandingAmount.ToDecimalValue())
			.TryAdd(PositionChangeTypes.BuyOrdersMargin, detail.CreditLimit.ToDecimalValue())
			.TryAdd(PositionChangeTypes.Currency, detail.Currency.ToCurrency()),
				cancellationToken);
		}

		var previousPositions = _positionIds.ToArray();
		_positionIds.Clear();
		var holdingsTime = holdingsResponse.LastUpdated.ToUtc(DefaultExchange, CurrentTime);
		foreach (var exchangeHoldings in holdings)
		{
			if (exchangeHoldings == null)
				continue;
			foreach (var currencyHoldings in exchangeHoldings.Currencies ?? [])
			{
				if (currencyHoldings == null)
					continue;
				foreach (var holding in currencyHoldings.Items ?? [])
				{
					if (holding == null)
						continue;
					var securityId = holding.ToSecurityId(
						exchangeHoldings.Exchange.IsEmpty(DefaultExchange));
					if (securityId.SecurityCode.IsEmpty())
						continue;
					var key = PhillipPoemsExtensions.GetSecurityKey(securityId, DefaultExchange);
					_positionIds[key] = securityId;
					CacheCounter(new PoemsCounter
					{
						CounterId = holding.CounterId,
						Symbol = holding.Symbol,
						Name = holding.Name,
						Market = holding.Market,
						Exchange = holding.Exchange.IsEmpty(exchangeHoldings.Exchange),
						Product = "ST",
					});
					await SendOutMessageAsync(new PositionChangeMessage
					{
						OriginalTransactionId = originalTransactionId,
						PortfolioName = accountNo,
						SecurityId = securityId,
						ServerTime = holdingsTime,
					}
					.TryAdd(PositionChangeTypes.CurrentValue, holding.Quantity.ToDecimalValue(), true)
					.TryAdd(PositionChangeTypes.AveragePrice, holding.AveragePrice.ToDecimalValue())
					.TryAdd(PositionChangeTypes.CurrentPrice, holding.ClosingPrice.ToDecimalValue())
					.TryAdd(PositionChangeTypes.UnrealizedPnL, holding.UnrealizedPnL.ToDecimalValue())
					.TryAdd(PositionChangeTypes.Currency,
						holding.Currency.IsEmpty(currencyHoldings.Currency).ToCurrency()),
						cancellationToken);
				}
			}
		}

		if (!isLookup)
		{
			foreach (var previous in previousPositions.Where(previous =>
				!_positionIds.ContainsKey(previous.Key)))
				await SendOutMessageAsync(new PositionChangeMessage
				{
					OriginalTransactionId = originalTransactionId,
					PortfolioName = accountNo,
					SecurityId = previous.Value,
					ServerTime = holdingsTime,
				}.TryAdd(PositionChangeTypes.CurrentValue, 0m, true), cancellationToken);
		}
	}

	private async Task<PoemsOrder> GetOrderForMutation(string orderId,
		CancellationToken cancellationToken)
	{
		if (_orders.TryGetValue(orderId, out var cached))
			return cached;
		var response = await _client.GetTodayOrders(cancellationToken);
		foreach (var item in response.Orders ?? [])
		{
			if (item?.OrderNo.IsEmpty() != false)
				continue;
			_orders[item.OrderNo] = item;
		}
		return _orders.TryGetValue(orderId, out cached) ? cached : null;
	}

	private string GetOrderId(string orderId, long originalTransactionId)
	{
		if (orderId.IsEmpty() &&
			_transactionOrders.TryGetValue(originalTransactionId, out var mappedOrderId))
			orderId = mappedOrderId;
		if (orderId.IsEmpty())
			throw new InvalidOperationException(
				LocalizedStrings.OrderNoExchangeId.Put(originalTransactionId));
		return orderId;
	}

	private static long GetOrderQuantity(decimal volume, string parameterName)
	{
		if (volume <= 0 || volume > long.MaxValue || decimal.Truncate(volume) != volume)
			throw new ArgumentOutOfRangeException(parameterName, volume,
				"Phillip POEMS requires a positive whole-number share quantity.");
		return decimal.ToInt64(volume);
	}

	private static bool IsOrderMatch(PoemsOrder order, OrderStatusMessage filter)
	{
		if (order?.OrderNo.IsEmpty() != false)
			return false;
		if (filter == null)
			return true;
		if (!filter.OrderStringId.IsEmpty() &&
			!filter.OrderStringId.EqualsIgnoreCase(order.OrderNo))
			return false;
		var securityId = order.ToSecurityId(PhillipPoemsExtensions.DefaultExchange);
		if (filter.SecurityId != default &&
			(!filter.SecurityId.SecurityCode.EqualsIgnoreCase(securityId.SecurityCode) ||
			 (!filter.SecurityId.BoardCode.IsEmpty() &&
			  !filter.SecurityId.BoardCode.ToNativeExchange()
				  .EqualsIgnoreCase(securityId.BoardCode.ToNativeExchange()))))
			return false;
		if (filter.SecurityIds.Length > 0 && !filter.SecurityIds.Any(id =>
			id.SecurityCode.EqualsIgnoreCase(securityId.SecurityCode) &&
			(id.BoardCode.IsEmpty() || id.BoardCode.ToNativeExchange()
				.EqualsIgnoreCase(securityId.BoardCode.ToNativeExchange()))))
			return false;
		if (filter.Side is Sides side && side != order.Action.ToSide())
			return false;
		var volume = order.SubmittedQuantity.ToDecimalValue() ?? 0;
		if (filter.Volume is decimal filterVolume && filterVolume != volume)
			return false;
		var state = order.Status.ToOrderState();
		if (filter.States.Length > 0 && !filter.States.Contains(state))
			return false;
		var time = GetOrderTime(order);
		if (filter.From is DateTime from && time < ToUtc(from))
			return false;
		return filter.To is not DateTime to || time <= ToUtc(to);
	}

	private static bool IsCancelled(string status)
		=> status?.ToUpperInvariant() is "WD" or "CN" or "CA" or "CANCELLED" or "CANCELED";

	private static DateTime GetOrderTime(PoemsOrder order)
	{
		var exchange = order?.Exchange.IsEmpty(PhillipPoemsExtensions.DefaultExchange);
		return order?.LatestUpdatedTime.IsEmpty(order?.UpdatedTime)
			.IsEmpty(order?.ExecutedTime).IsEmpty(order?.SubmittedTime)
			.ToUtc(exchange, DateTime.UtcNow) ?? DateTime.UtcNow;
	}

	private static DateTime ToUtc(DateTime time)
		=> time.Kind == DateTimeKind.Utc ? time : time.ToUniversalTime();
}
