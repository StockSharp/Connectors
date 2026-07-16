namespace StockSharp.Saxo;

public partial class SaxoMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var instrument = await ResolveInstrument(regMsg.SecurityId, cancellationToken);
		var condition = regMsg.Condition as SaxoOrderCondition;
		var request = CreateOrderRequest(instrument, regMsg.PortfolioName, regMsg.Volume, regMsg.Side,
			regMsg.OrderType ?? OrderTypes.Limit, regMsg.Price, regMsg.TimeInForce, regMsg.TillDate, condition,
			regMsg.TransactionId, null);
		var response = EnsureOrderResult(await _client.Rest.PlaceOrder(request, cancellationToken), "place order");
		var orderId = response.OrderId.IsEmpty(response.Orders?.FirstOrDefault()?.OrderId)
			.ThrowIfEmpty(nameof(SaxoOrderResult.OrderId));
		_orderTransactions[orderId] = regMsg.TransactionId;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			OrderId = ParseId(orderId),
			OrderStringId = orderId,
			SecurityId = regMsg.SecurityId,
			PortfolioName = request.AccountKey,
			Side = regMsg.Side,
			OrderType = regMsg.OrderType,
			OrderPrice = regMsg.Price,
			OrderVolume = regMsg.Volume,
			Balance = regMsg.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = DateTime.UtcNow,
			TimeInForce = regMsg.TimeInForce,
			ExpiryDate = regMsg.TillDate,
			Condition = condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		var orderId = replaceMsg.OldOrderStringId.IsEmpty(replaceMsg.OldOrderId?.ToString(CultureInfo.InvariantCulture));
		if (orderId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(replaceMsg.OriginalTransactionId));
		var instrument = await ResolveInstrument(replaceMsg.SecurityId, cancellationToken);
		var request = CreateOrderRequest(instrument, replaceMsg.PortfolioName, replaceMsg.Volume, replaceMsg.Side,
			replaceMsg.OrderType ?? OrderTypes.Limit, replaceMsg.Price, replaceMsg.TimeInForce, replaceMsg.TillDate,
			replaceMsg.Condition as SaxoOrderCondition, replaceMsg.TransactionId, orderId);
		EnsureOrderResult(await _client.Rest.ModifyOrder(request, cancellationToken), "modify order");
		_orderTransactions[orderId] = replaceMsg.TransactionId;
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var orderId = cancelMsg.OrderStringId.IsEmpty(cancelMsg.OrderId?.ToString(CultureInfo.InvariantCulture));
		if (orderId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));
		EnsureOrderResult(await _client.Rest.CancelOrder(cancelMsg.PortfolioName.IsEmpty(_client.Session.AccountKey),
			orderId, cancellationToken), "cancel order");
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		if (!statusMsg.IsSubscribe)
		{
			_orderStatusSubscriptionId = 0;
			await _client.UnsubscribeActivities(cancellationToken);
			return;
		}

		var accountKey = statusMsg.PortfolioName.IsEmpty(_client.Session.AccountKey);
		var orders = await _client.Rest.GetOpenOrders(accountKey, cancellationToken);
		foreach (var order in orders.Data ?? [])
			await ProcessOpenOrder(order, statusMsg.TransactionId, cancellationToken);
		var activities = await _client.Rest.GetOrderActivities(accountKey, statusMsg.From, statusMsg.To, cancellationToken);
		foreach (var activity in activities.Data ?? [])
			await ProcessActivity(activity, statusMsg.TransactionId, cancellationToken);

		if (!statusMsg.IsHistoryOnly())
		{
			_orderStatusSubscriptionId = statusMsg.TransactionId;
			await _client.SubscribeActivities(cancellationToken);
		}
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		if (!lookupMsg.IsSubscribe)
		{
			_portfolioSubscriptionId = 0;
			await _client.UnsubscribePortfolio(cancellationToken);
			return;
		}

		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = lookupMsg.TransactionId,
			PortfolioName = _client.Session.AccountKey,
			BoardCode = "SAXO",
		}, cancellationToken);
		_portfolioSubscriptionId = lookupMsg.TransactionId;
		await _client.SubscribePortfolio(cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await _client.UnsubscribePortfolio(cancellationToken);
			_portfolioSubscriptionId = 0;
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private SaxoOrderRequest CreateOrderRequest(SaxoInstrument instrument, string portfolioName, decimal volume, Sides side,
		OrderTypes orderType, decimal price, TimeInForce? timeInForce, DateTimeOffset? tillDate,
		SaxoOrderCondition condition, long transactionId, string orderId)
	{
		var nativeOrderType = orderType.ToSaxoOrderType(price, condition);
		var duration = timeInForce.ToSaxoDuration(tillDate, condition);
		var settings = instrument.SupportedOrderTypeSettings ?? [];
		var orderSetting = settings.FirstOrDefault(s => s.OrderType.EqualsIgnoreCase(nativeOrderType));
		if (orderSetting == null && orderType == OrderTypes.Conditional && !nativeOrderType.EqualsIgnoreCase("StopLimit"))
		{
			var alternatives = condition?.TrailingDistance is > 0
				? new[] { "TrailingStopIfTraded", "TrailingStop", "TrailingStopIfBid", "TrailingStopIfOffered" }
				: new[] { "StopIfTraded", "Stop", "StopIfBid", "StopIfOffered" };
			orderSetting = alternatives.Select(type => settings.FirstOrDefault(s => s.OrderType.EqualsIgnoreCase(type)))
				.FirstOrDefault(s => s != null);
			if (orderSetting != null)
				nativeOrderType = orderSetting.OrderType;
		}
		if (settings.Length > 0 && orderSetting == null)
			throw new InvalidOperationException($"Saxo order type '{nativeOrderType}' is not supported for " +
				$"{instrument.Symbol} ({instrument.AssetType}).");
		if (orderSetting?.DurationTypes?.Length > 0 &&
			!orderSetting.DurationTypes.Contains(duration.DurationType, StringComparer.OrdinalIgnoreCase))
			throw new InvalidOperationException($"Saxo duration '{duration.DurationType}' is not supported for {instrument.Symbol} ({instrument.AssetType}).");
		var externalReference = condition?.ExternalReference;
		if (externalReference.IsEmpty())
			externalReference = transactionId.ToString(CultureInfo.InvariantCulture);
		if (externalReference.Length > 50)
			throw new InvalidOperationException("Saxo ExternalReference cannot exceed 50 characters.");

		var request = new SaxoOrderRequest
		{
			AccountKey = portfolioName.IsEmpty(_client.Session.AccountKey),
			Amount = volume,
			AssetType = instrument.AssetType,
			BuySell = side.ToSaxoSide(),
			ExecuteAtTradingSession = (condition?.TradingSession ?? SaxoTradingSessions.Regular).ToString(),
			ExternalReference = externalReference,
			IsForceOpen = condition?.ForceOpen == true,
			ManualOrder = condition?.ManualOrder == true,
			OrderDuration = duration,
			OrderId = orderId,
			OrderType = nativeOrderType,
			Uic = instrument.Uic,
			TrailingStopDistanceToMarket = condition?.TrailingDistance,
			TrailingStopStep = condition?.TrailingStep,
		};

		if (nativeOrderType == "Limit")
			request.OrderPrice = price;
		else if (nativeOrderType == "StopLimit")
		{
			if (price <= 0 || condition?.StopPrice is not > 0)
				throw new InvalidOperationException("Saxo stop-limit orders require both a limit price and StopPrice.");
			request.OrderPrice = condition.StopPrice;
			request.StopLimitPrice = price;
		}
		else if (nativeOrderType.ContainsIgnoreCase("Stop"))
		{
			request.OrderPrice = condition?.StopPrice is > 0 ? condition.StopPrice : price > 0 ? price : null;
			if (request.OrderPrice is null)
				throw new InvalidOperationException($"Saxo {nativeOrderType} orders require a stop price.");
		}
		return request;
	}

	private static SaxoOrderResult EnsureOrderResult(SaxoOrderResult result, string operation)
	{
		var error = result?.ErrorInfo ?? result?.Orders?.Select(o => o.ErrorInfo).FirstOrDefault(e => e != null);
		if (result == null)
			throw new InvalidOperationException($"Saxo {operation} returned an empty response.");
		if (error != null)
			throw new InvalidOperationException($"Saxo {operation} failed ({error.ErrorCode}): {error.Message}");
		return result;
	}

	private async ValueTask ProcessOpenOrder(SaxoOpenOrder order, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (order == null || order.OrderId.IsEmpty())
			return;
		var transactionId = ParseTransactionId(order.ExternalReference, order.OrderId);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId,
			TransactionId = transactionId,
			OrderId = ParseId(order.OrderId),
			OrderStringId = order.OrderId,
			SecurityId = GetSecurityId(order.Uic, order.AssetType),
			PortfolioName = order.AccountKey.IsEmpty(_client.Session.AccountKey),
			Side = order.BuySell.ToSide(),
			OrderType = order.OpenOrderType.ToOrderType(),
			OrderPrice = order.OpenOrderType.EqualsIgnoreCase("StopLimit") ? order.StopLimitPrice ?? order.Price : order.Price,
			OrderVolume = order.Amount,
			Balance = Math.Max(0, order.Amount - order.FilledAmount),
			OrderState = order.Status.ToOrderState(),
			ServerTime = order.OrderTime.UtcKind(),
			TimeInForce = order.Duration.ToTimeInForce(),
			ExpiryDate = ParseExpiration(order.Duration),
			Condition = ToOrderCondition(order.OpenOrderType, order.Price, order.Duration, order.ExternalReference),
		}, cancellationToken);
	}

	private async ValueTask OnActivityReceived(SaxoActivity activity, CancellationToken cancellationToken)
	{
		if (_orderStatusSubscriptionId == 0)
			return;
		await ProcessActivity(activity, GetOriginId(activity.ExternalReference, activity.OrderId), cancellationToken);
	}

	private async ValueTask ProcessActivity(SaxoActivity activity, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (activity == null || activity.OrderId.IsEmpty())
			return;
		if (activity.SubStatus.EqualsIgnoreCase("Rejected") && !activity.Status.EqualsIgnoreCase("Placed"))
			return;
		var transactionId = ParseTransactionId(activity.ExternalReference, activity.OrderId);
		var volume = activity.Amount ?? activity.FilledAmount ?? activity.FillAmount ?? 0;
		var filled = activity.FilledAmount ?? activity.FillAmount ?? 0;
		var state = activity.Status.ToOrderState(activity.SubStatus);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId,
			TransactionId = transactionId,
			OrderId = ParseId(activity.OrderId),
			OrderStringId = activity.OrderId,
			SecurityId = GetSecurityId(activity.Uic, activity.AssetType),
			PortfolioName = activity.AccountKey.IsEmpty(_client.Session.AccountKey),
			Side = activity.BuySell.ToSide(),
			OrderType = activity.OrderType.ToOrderType(),
			OrderPrice = activity.OrderType.EqualsIgnoreCase("StopLimit")
				? activity.StopLimitPrice ?? activity.Price ?? 0 : activity.Price ?? 0,
			OrderVolume = volume,
			Balance = Math.Max(0, volume - filled),
			AveragePrice = activity.AveragePrice,
			OrderState = state,
			ServerTime = activity.ActivityTime.UtcKind(),
			TimeInForce = activity.Duration.ToTimeInForce(),
			ExpiryDate = ParseExpiration(activity.Duration),
			Condition = ToOrderCondition(activity.OrderType, activity.Price, activity.Duration, activity.ExternalReference),
			Error = state == OrderStates.Failed
				? new InvalidOperationException($"Saxo order {activity.Status}: {activity.SubStatus}") : null,
		}, cancellationToken);

		if (activity.FillAmount is > 0 && !activity.LogId.IsEmpty() && !_activityTrades.Contains(activity.LogId))
		{
			_activityTrades.Add(activity.LogId);
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = originalTransactionId,
				OrderId = ParseId(activity.OrderId),
				OrderStringId = activity.OrderId,
				TradeId = ParseId(activity.LogId),
				TradeStringId = activity.LogId,
				SecurityId = GetSecurityId(activity.Uic, activity.AssetType),
				PortfolioName = activity.AccountKey.IsEmpty(_client.Session.AccountKey),
				Side = activity.BuySell.ToSide(),
				TradePrice = activity.ExecutionPrice ?? activity.AveragePrice ?? activity.Price,
				TradeVolume = activity.FillAmount,
				ServerTime = activity.ActivityTime.UtcKind(),
			}, cancellationToken);
		}
	}

	private ValueTask OnBalanceReceived(SaxoBalance balance, CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId == 0 || balance == null)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = _portfolioSubscriptionId,
			PortfolioName = balance.AccountKey.IsEmpty(_client.Session.AccountKey),
			SecurityId = SecurityId.Money,
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.BeginValue, balance.CashBalance, true)
		.TryAdd(PositionChangeTypes.CurrentValue, balance.TotalValue ?? balance.NetEquityForMargin, true)
		.TryAdd(PositionChangeTypes.BlockedValue, balance.MarginUsedByCurrentPositions, true)
		.TryAdd(PositionChangeTypes.BuyOrdersMargin, balance.MarginAvailableForTrading, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, balance.UnrealizedMarginProfitLoss, true), cancellationToken);
	}

	private ValueTask OnPositionReceived(SaxoNetPosition position, CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId == 0 || position?.NetPositionBase == null)
			return default;
		var positionBase = position.NetPositionBase;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = _portfolioSubscriptionId,
			PortfolioName = _client.Session.AccountKey,
			SecurityId = GetSecurityId(positionBase.Uic, positionBase.AssetType),
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, positionBase.Amount, true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.NetPositionView?.AverageOpenPrice, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, position.NetPositionView?.CurrentPrice, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.NetPositionView?.ProfitLossOnTrade, true), cancellationToken);
	}

	private SecurityId GetSecurityId(long uic, string assetType)
		=> _instruments.TryGetValue(uic, out var instrument)
			? instrument.ToSecurityId()
			: new()
			{
				SecurityCode = uic.ToString(CultureInfo.InvariantCulture),
				BoardCode = "SAXO",
				Native = $"{uic.ToString(CultureInfo.InvariantCulture)}|{assetType}",
			};

	private long GetOriginId(string externalReference, string orderId)
	{
		var transactionId = ParseTransactionId(externalReference, orderId);
		return transactionId == 0 ? _orderStatusSubscriptionId : transactionId;
	}

	private long ParseTransactionId(string externalReference, string orderId)
	{
		if (long.TryParse(externalReference, NumberStyles.Integer, CultureInfo.InvariantCulture, out var transactionId))
			_orderTransactions[orderId] = transactionId;
		else
			_orderTransactions.TryGetValue(orderId, out transactionId);
		return transactionId;
	}

	private static long? ParseId(string value)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;

	private static DateTime? ParseExpiration(SaxoOrderDuration duration)
		=> DateTimeOffset.TryParse(duration?.ExpirationDateTime, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal, out var expiry) ? expiry.UtcDateTime : null;

	private static SaxoOrderCondition ToOrderCondition(string orderType, decimal? price, SaxoOrderDuration duration,
		string externalReference)
		=> new()
		{
			StopPrice = orderType?.ContainsIgnoreCase("Stop") == true ? price : null,
			Duration = duration?.DurationType?.ToUpperInvariant() switch
			{
				"GOODTILLCANCEL" => SaxoOrderDurations.GoodTillCancel,
				"IMMEDIATEORCANCEL" => SaxoOrderDurations.ImmediateOrCancel,
				"FILLORKILL" => SaxoOrderDurations.FillOrKill,
				_ => SaxoOrderDurations.Day,
			},
			ExternalReference = externalReference,
		};
}
