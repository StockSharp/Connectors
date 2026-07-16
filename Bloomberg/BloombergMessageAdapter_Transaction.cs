namespace StockSharp.Bloomberg;

partial class BloombergMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage message, CancellationToken cancellationToken)
	{
		EnsureEmsxTrading();
		var amount = ToEmsxAmount(message.Volume);
		var stopPrice = (message.Condition as BloombergOrderCondition)?.StopPrice;
		var nativeType = message.OrderType.ToNative(stopPrice);
		ValidatePrices(nativeType, message.Price, stopPrice);
		var result = await _client.RegisterOrderAsync(new BloombergEmsxRegisterRequest
		{
			Symbol = message.SecurityId.SecurityCode.ThrowIfEmpty(nameof(message.SecurityId.SecurityCode)),
			Amount = amount,
			OrderType = nativeType,
			TimeInForce = message.TimeInForce.ToNative(message.TillDate),
			Side = message.Side.ToNative(message.PositionEffect),
			Broker = Broker,
			Account = message.PortfolioName,
			LimitPrice = nativeType is "LMT" or "STPLMT" ? message.Price : null,
			StopPrice = stopPrice,
			GoodTillDate = message.TillDate,
			OrderReference = message.TransactionId.ToString(CultureInfo.InvariantCulture),
		}, cancellationToken);

		if (result.Sequence <= 0)
			throw new InvalidOperationException(result.Message.IsEmpty("Bloomberg EMSX did not return an order sequence."));

		_orderTransactions[result.Sequence] = message.TransactionId;
		if (result.RouteId > 0)
			_routeIds[result.Sequence] = result.RouteId;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = message.TransactionId,
			TransactionId = message.TransactionId,
			OrderId = result.Sequence,
			OrderStringId = result.Sequence.ToString(CultureInfo.InvariantCulture),
			PortfolioName = message.PortfolioName,
			SecurityId = message.SecurityId,
			Side = message.Side,
			OrderType = message.OrderType,
			TimeInForce = message.TimeInForce,
			OrderPrice = message.Price,
			OrderVolume = message.Volume,
			Balance = message.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = DateTime.UtcNow,
			Condition = message.Condition,
			PositionEffect = message.PositionEffect,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage message, CancellationToken cancellationToken)
	{
		EnsureEmsxTrading();
		var sequence = ResolveSequence(message.OldOrderStringId, message.OldOrderId, message.OriginalTransactionId);
		var amount = ToEmsxAmount(message.Volume);
		var stopPrice = (message.Condition as BloombergOrderCondition)?.StopPrice;
		var nativeType = message.OrderType.ToNative(stopPrice);
		ValidatePrices(nativeType, message.Price, stopPrice);
		await _client.ReplaceOrderAsync(new BloombergEmsxReplaceRequest
		{
			Sequence = sequence,
			Amount = amount,
			OrderType = nativeType,
			TimeInForce = message.TimeInForce.ToNative(message.TillDate),
			LimitPrice = nativeType is "LMT" or "STPLMT" ? message.Price : null,
			StopPrice = stopPrice,
			GoodTillDate = message.TillDate,
		}, cancellationToken);

		_orderTransactions[sequence] = message.TransactionId;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = message.TransactionId,
			TransactionId = message.TransactionId,
			OrderId = sequence,
			OrderStringId = sequence.ToString(CultureInfo.InvariantCulture),
			PortfolioName = message.PortfolioName,
			SecurityId = message.SecurityId,
			Side = message.Side,
			OrderType = message.OrderType,
			TimeInForce = message.TimeInForce,
			OrderPrice = message.Price,
			OrderVolume = message.Volume,
			Balance = message.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = DateTime.UtcNow,
			Condition = message.Condition,
			PositionEffect = message.PositionEffect,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage message, CancellationToken cancellationToken)
	{
		EnsureEmsxTrading();
		var sequence = ResolveSequence(message.OrderStringId, message.OrderId, message.OriginalTransactionId);
		if (!_routeIds.TryGetValue(sequence, out var routeId) || routeId <= 0)
			throw new InvalidOperationException($"Bloomberg EMSX route identifier for order {sequence} is unavailable.");

		await _client.CancelOrderAsync(new BloombergEmsxCancelRequest
		{
			Sequence = sequence,
			RouteId = routeId,
		}, cancellationToken);
		_orderTransactions[sequence] = message.TransactionId;
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			if (_orderStatusSubscriptionId == message.OriginalTransactionId)
				_orderStatusSubscriptionId = 0;
			return;
		}

		EnsureEmsxTrading(false);
		_orderStatusSubscriptionId = message.TransactionId;
		foreach (var order in _orders.Values.OrderBy(order => order.Sequence))
			await SendEmsxOrderAsync(order, message.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			if (_portfolioSubscriptionId == message.OriginalTransactionId)
				_portfolioSubscriptionId = 0;
			return;
		}

		EnsureEmsxTrading(false);
		_portfolioSubscriptionId = message.TransactionId;
		foreach (var account in _orders.Values
			.Select(order => order.Account)
			.Where(account => !account.IsEmpty())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(account => account, StringComparer.OrdinalIgnoreCase))
		{
			_announcedPortfolios[account] = 0;
			await SendPortfolioAsync(account, message.TransactionId, cancellationToken);
		}
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private async ValueTask ProcessEmsxOrderAsync(BloombergEmsxOrderUpdate update, CancellationToken cancellationToken)
	{
		if (update.IsEndOfInitialPaint || update.Sequence <= 0)
			return;

		var effective = _orders.AddOrUpdate(
			update.Sequence,
			update,
			(_, previous) => MergeEmsxOrder(previous, update));
		if (effective.RouteId > 0)
			_routeIds[effective.Sequence] = effective.RouteId;

		var transactionId = _orderTransactions.TryGetValue(effective.Sequence, out var originalId) ? originalId : 0;
		var subscriptionId = _orderStatusSubscriptionId != 0 ? _orderStatusSubscriptionId : transactionId;
		await SendEmsxOrderAsync(effective, subscriptionId, cancellationToken);

		if (!effective.Account.IsEmpty() && _portfolioSubscriptionId != 0 && _announcedPortfolios.TryAdd(effective.Account, 0))
			await SendPortfolioAsync(effective.Account, _portfolioSubscriptionId, cancellationToken);

		var filled = effective.Filled ?? 0;
		var previousFilled = _filledQuantities.TryGetValue(effective.Sequence, out var previous) ? previous : 0;
		if (filled <= previousFilled)
			return;

		_filledQuantities[effective.Sequence] = filled;
		var delta = filled - previousFilled;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = subscriptionId,
			TransactionId = transactionId,
			OrderId = effective.Sequence,
			OrderStringId = effective.Sequence.ToString(CultureInfo.InvariantCulture),
			TradeId = effective.FillId == 0 ? null : effective.FillId,
			TradeStringId = $"{effective.Sequence}:{effective.RouteId}:{effective.FillId}:{effective.ApiSequence}",
			PortfolioName = effective.Account,
			SecurityId = ToSecurityId(effective.Symbol),
			Side = effective.Side.ToSide(),
			PositionEffect = effective.Side.ToPositionEffect(),
			TradePrice = effective.LastPrice ?? effective.AveragePrice,
			TradeVolume = delta,
			ServerTime = effective.ServerTime,
		}, cancellationToken);
	}

	private ValueTask SendEmsxOrderAsync(BloombergEmsxOrderUpdate order, long originalTransactionId, CancellationToken cancellationToken)
	{
		var state = order.Status.ToOrderState();
		var volume = order.Amount ?? 0;
		var balance = order.Remaining ?? Math.Max(0, volume - (order.Filled ?? 0));
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId,
			TransactionId = _orderTransactions.TryGetValue(order.Sequence, out var transactionId) ? transactionId : 0,
			OrderId = order.Sequence,
			OrderStringId = order.Sequence.ToString(CultureInfo.InvariantCulture),
			PortfolioName = order.Account,
			SecurityId = ToSecurityId(order.Symbol),
			Side = order.Side.ToSide(),
			OrderType = order.OrderType.ToOrderType(),
			TimeInForce = order.TimeInForce.ToTimeInForce(),
			PositionEffect = order.Side.ToPositionEffect(),
			OrderPrice = order.LimitPrice ?? 0,
			OrderVolume = volume,
			Balance = balance,
			OrderState = state,
			ServerTime = order.ServerTime,
			Condition = order.StopPrice == null ? null : new BloombergOrderCondition { StopPrice = order.StopPrice },
			Error = state == OrderStates.Failed ? new InvalidOperationException(order.Reason.IsEmpty(order.Status)) : null,
		}, cancellationToken);
	}

	private ValueTask SendPortfolioAsync(string account, long originalTransactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = account,
			BoardCode = "BLOOMBERG",
		}, cancellationToken);

	private static BloombergEmsxOrderUpdate MergeEmsxOrder(BloombergEmsxOrderUpdate previous, BloombergEmsxOrderUpdate current)
		=> new()
		{
			IsRoute = current.IsRoute,
			ApiSequence = current.ApiSequence == 0 ? previous.ApiSequence : current.ApiSequence,
			Sequence = current.Sequence,
			RouteId = current.RouteId == 0 ? previous.RouteId : current.RouteId,
			Symbol = current.Symbol.IsEmpty(previous.Symbol),
			Account = current.Account.IsEmpty(previous.Account),
			Broker = current.Broker.IsEmpty(previous.Broker),
			Side = current.Side.IsEmpty(previous.Side),
			OrderType = current.OrderType.IsEmpty(previous.OrderType),
			TimeInForce = current.TimeInForce.IsEmpty(previous.TimeInForce),
			Status = current.Status.IsEmpty(previous.Status),
			Reason = current.Reason.IsEmpty(previous.Reason),
			Amount = current.Amount ?? previous.Amount,
			Filled = current.Filled ?? previous.Filled,
			Remaining = current.Remaining ?? previous.Remaining,
			LimitPrice = current.LimitPrice ?? previous.LimitPrice,
			StopPrice = current.StopPrice ?? previous.StopPrice,
			AveragePrice = current.AveragePrice ?? previous.AveragePrice,
			FillId = current.FillId == 0 ? previous.FillId : current.FillId,
			LastPrice = current.LastPrice ?? previous.LastPrice,
			LastShares = current.LastShares ?? previous.LastShares,
			ServerTime = current.ServerTime,
		};

	private void EnsureEmsxTrading(bool requireBroker = true)
	{
		if (!IsEmsxEnabled)
			throw new InvalidOperationException("Bloomberg EMSX is disabled for this connection.");
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		if (requireBroker && Broker.IsEmpty())
			throw new InvalidOperationException("Bloomberg EMSX broker destination is not specified.");
	}

	private long ResolveSequence(string stringId, long? numericId, long originalTransactionId)
	{
		if (numericId is > 0)
			return numericId.Value;
		if (long.TryParse(stringId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
			return parsed;
		var order = _orderTransactions.FirstOrDefault(pair => pair.Value == originalTransactionId);
		if (order.Key > 0)
			return order.Key;
		throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(originalTransactionId));
	}

	private static long ToEmsxAmount(decimal volume)
	{
		if (volume <= 0 || volume != decimal.Truncate(volume) || volume > int.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(volume), volume, "Bloomberg EMSX amount must be a positive 32-bit whole number.");
		return (long)volume;
	}

	private static void ValidatePrices(string orderType, decimal limitPrice, decimal? stopPrice)
	{
		if (orderType is "LMT" or "STPLMT" && limitPrice <= 0)
			throw new ArgumentOutOfRangeException(nameof(limitPrice), limitPrice, "Bloomberg EMSX limit price must be positive.");
		if (orderType is "STP" or "STPLMT" && stopPrice is not > 0)
			throw new ArgumentOutOfRangeException(nameof(stopPrice), stopPrice, "Bloomberg EMSX stop price must be positive.");
	}

	private static SecurityId ToSecurityId(string symbol)
		=> symbol.IsEmpty()
			? default
			: new SecurityId { SecurityCode = symbol, BoardCode = "BLOOMBERG", Bloomberg = symbol };
}
