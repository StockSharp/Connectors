namespace StockSharp.OrderlyNetwork;

public partial class OrderlyNetworkMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsureAccountReady();
		ValidatePortfolio(regMsg.PortfolioName);
		var symbol = GetSymbol(regMsg.SecurityId);
		var market = GetMarket(symbol);
		var volume = regMsg.Volume.Abs();
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		var isPostOnly = regMsg.PostOnly == true;
		if (orderType == OrderTypes.Market && isPostOnly)
			throw new InvalidOperationException(
				"An Orderly market order cannot be post-only.");
		var condition = regMsg.Condition as OrderlyNetworkOrderCondition ?? new();
		ValidateOrder(market, orderType, volume, regMsg.Price,
			condition.VisibleQuantity, condition.Slippage);
		var clientOrderId = CreateClientOrderId(regMsg.TransactionId,
			regMsg.UserOrderId);
		using (_sync.EnterScope())
			_transactionIds[clientOrderId] = regMsg.TransactionId;
		OrderlyNetworkOrderAcceptance result;
		try
		{
			result = await RestClient.PlaceOrderAsync(new()
			{
				Symbol = symbol,
				OrderType = orderType.ToOrderly(regMsg.TimeInForce, isPostOnly),
				Side = regMsg.Side.ToOrderly(),
				ClientOrderId = clientOrderId,
				Price = orderType == OrderTypes.Market ? null : regMsg.Price,
				Quantity = volume,
				VisibleQuantity = condition.VisibleQuantity,
				IsReduceOnly = condition.IsReduceOnly ||
					regMsg.PositionEffect == OrderPositionEffects.CloseOnly,
				Slippage = condition.Slippage,
			}, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_transactionIds.Remove(clientOrderId);
			throw;
		}
		if (result is null || result.OrderId <= 0)
			throw new InvalidDataException(
				"Orderly Network accepted the order without returning an order ID.");
		if (!result.ErrorMessage.IsEmpty() &&
			!result.ErrorMessage.EqualsIgnoreCase("none"))
			throw new InvalidOperationException(
				"Orderly Network rejected the order: " + result.ErrorMessage);
		UpdateServerTime(RestClient.ServerTime);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(),
			ServerTime = ServerTime,
			PortfolioName = PortfolioName,
			Side = regMsg.Side,
			OrderVolume = volume,
			Balance = volume,
			OrderPrice = orderType == OrderTypes.Market ? 0m : regMsg.Price,
			OrderType = orderType,
			OrderState = OrderStates.Active,
			OrderId = result.OrderId,
			OrderStringId = result.OrderId.ToString(CultureInfo.InvariantCulture),
			UserOrderId = clientOrderId,
			TransactionId = regMsg.TransactionId,
			OriginalTransactionId = regMsg.TransactionId,
			TimeInForce = regMsg.TimeInForce,
			PostOnly = isPostOnly,
			PositionEffect = condition.IsReduceOnly ||
				regMsg.PositionEffect == OrderPositionEffects.CloseOnly
					? OrderPositionEffects.CloseOnly
					: null,
			Condition = condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		EnsureAccountReady();
		ValidatePortfolio(replaceMsg.PortfolioName);
		var symbol = GetSymbol(replaceMsg.SecurityId);
		var market = GetMarket(symbol);
		var orderId = ResolveOrderId(replaceMsg.OldOrderId,
			replaceMsg.OldOrderStringId, "replacement");
		var orderType = replaceMsg.OrderType ?? OrderTypes.Limit;
		if (orderType != OrderTypes.Limit)
			throw new NotSupportedException(
				"Orderly Network can amend active limit orders only.");
		var volume = replaceMsg.Volume.Abs();
		var condition = replaceMsg.Condition as OrderlyNetworkOrderCondition ??
			new();
		ValidateOrder(market, orderType, volume, replaceMsg.Price,
			condition.VisibleQuantity, condition.Slippage);
		var result = await RestClient.EditOrderAsync(new()
		{
			OrderId = orderId.ToString(CultureInfo.InvariantCulture),
			Symbol = symbol,
			ClientOrderId = replaceMsg.UserOrderId,
			OrderType = orderType.ToOrderly(replaceMsg.TimeInForce,
				replaceMsg.PostOnly == true),
			Price = replaceMsg.Price,
			Quantity = volume,
			VisibleQuantity = condition.VisibleQuantity,
			IsReduceOnly = replaceMsg.Condition is OrderlyNetworkOrderCondition ||
				replaceMsg.PositionEffect is not null
					? condition.IsReduceOnly ||
						replaceMsg.PositionEffect == OrderPositionEffects.CloseOnly
					: null,
			Side = replaceMsg.Side.ToOrderly(),
		}, cancellationToken);
		if (result is null)
			throw new InvalidDataException(
				"Orderly Network returned no order-edit result.");
		UpdateServerTime(RestClient.ServerTime);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(),
			ServerTime = ServerTime,
			PortfolioName = PortfolioName,
			Side = replaceMsg.Side,
			OrderVolume = volume,
			Balance = volume,
			OrderPrice = replaceMsg.Price,
			OrderType = OrderTypes.Limit,
			OrderState = OrderStates.Active,
			OrderId = orderId,
			OrderStringId = orderId.ToString(CultureInfo.InvariantCulture),
			UserOrderId = replaceMsg.UserOrderId,
			TransactionId = replaceMsg.TransactionId,
			OriginalTransactionId = replaceMsg.TransactionId,
			TimeInForce = replaceMsg.TimeInForce,
			PostOnly = replaceMsg.PostOnly,
			PositionEffect = condition.IsReduceOnly ||
				replaceMsg.PositionEffect == OrderPositionEffects.CloseOnly
					? OrderPositionEffects.CloseOnly
					: null,
			Condition = condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureAccountReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		var symbol = GetSymbol(cancelMsg.SecurityId);
		long? orderId = cancelMsg.OrderId;
		if (orderId is null && !cancelMsg.OrderStringId.IsEmpty() &&
			long.TryParse(cancelMsg.OrderStringId, NumberStyles.None,
				CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
			orderId = parsed;
		if (orderId is long numeric)
			await RestClient.CancelOrderAsync(symbol, numeric, cancellationToken);
		else if (!cancelMsg.UserOrderId.IsEmpty())
			await RestClient.CancelClientOrderAsync(symbol,
				cancelMsg.UserOrderId.Trim(), cancellationToken);
		else
			throw new InvalidOperationException(
				"Orderly cancellation requires an order ID or client order ID.");
		UpdateServerTime(RestClient.ServerTime);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureAccountReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"Orderly bulk cancellation does not close positions.");
		if (cancelMsg.SecurityTypes is { Length: > 0 } &&
			!cancelMsg.SecurityTypes.Contains(SecurityTypes.Future))
			return;
		var symbol = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetSymbol(cancelMsg.SecurityId);
		if (cancelMsg.Side is null)
		{
			await RestClient.CancelOrdersAsync(symbol, cancellationToken);
			return;
		}
		var orders = await RestClient.GetOrdersAsync(symbol, null, null, 1,
			HistoryLimit, cancellationToken);
		foreach (var order in orders.Where(static item => item is not null &&
			item.OrderId > 0 && item.Status.ToStockSharp() == OrderStates.Active)
			.Where(item => item.Side.ToStockSharp() == cancelMsg.Side))
			await RestClient.CancelOrderAsync(order.Symbol, order.OrderId,
				cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureAccountReady();
		if (!lookupMsg.IsSubscribe)
		{
			bool isLast;
			using (_sync.EnterScope())
			{
				_portfolioSubscriptions.Remove(lookupMsg.OriginalTransactionId);
				isLast = _portfolioSubscriptions.Count == 0;
			}
			if (isLast && _privateSocket is not null)
			{
				await _privateSocket.UnsubscribeAsync("balance", cancellationToken);
				await _privateSocket.UnsubscribeAsync("position", cancellationToken);
			}
			return;
		}

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = PortfolioName,
			BoardCode = BoardCodes.OrderlyNetwork,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId,
			cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId,
				cancellationToken);
			return;
		}

		bool isFirst;
		using (_sync.EnterScope())
		{
			isFirst = _portfolioSubscriptions.Count == 0;
			_portfolioSubscriptions.Add(lookupMsg.TransactionId);
		}
		try
		{
			if (isFirst)
			{
				await PrivateSocket.SubscribeAsync("balance", cancellationToken);
				await PrivateSocket.SubscribeAsync("position", cancellationToken);
			}
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_portfolioSubscriptions.Remove(lookupMsg.TransactionId);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId,
			cancellationToken);
		EnsureAccountReady();
		if (!statusMsg.IsSubscribe)
		{
			bool isLast;
			using (_sync.EnterScope())
			{
				_orderSubscriptions.Remove(statusMsg.OriginalTransactionId);
				isLast = _orderSubscriptions.Count == 0;
			}
			if (isLast && _privateSocket is not null)
				await _privateSocket.UnsubscribeAsync("executionreport",
					cancellationToken);
			return;
		}

		var subscription = CreateOrderStatusSubscription(statusMsg);
		var limit = (statusMsg.Count ?? HistoryLimit).Min(HistoryLimit).Max(1)
			.To<int>();
		await SendOrderSnapshotAsync(subscription, statusMsg.TransactionId, limit,
			cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}

		bool isFirst;
		using (_sync.EnterScope())
		{
			isFirst = _orderSubscriptions.Count == 0;
			_orderSubscriptions.Add(statusMsg.TransactionId, subscription);
		}
		try
		{
			if (isFirst)
				await PrivateSocket.SubscribeAsync("executionreport",
					cancellationToken);
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_orderSubscriptions.Remove(statusMsg.TransactionId);
			throw;
		}
	}

	private async ValueTask SendPortfolioSnapshotAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		var holdingsTask = RestClient.GetHoldingsAsync(cancellationToken).AsTask();
		var positionsTask = RestClient.GetPositionsAsync(cancellationToken).AsTask();
		await Task.WhenAll(holdingsTask, positionsTask);
		UpdateServerTime(RestClient.ServerTime);
		foreach (var holding in holdingsTask.Result?.Holdings ?? [])
			await SendHoldingAsync(holding, transactionId, cancellationToken);
		await SendPositionSnapshotAsync(positionsTask.Result?.Rows,
			transactionId, cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(long transactionId,
		string symbol, DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
	{
		OrderStatusSubscription subscription;
		using (_sync.EnterScope())
			_orderSubscriptions.TryGetValue(transactionId, out subscription);
		subscription ??= new()
		{
			Symbols = symbol.IsEmpty() ? [] : [symbol],
			States = [],
			From = from?.EnsureOrderlyUtc(),
			To = to?.EnsureOrderlyUtc(),
		};
		await SendOrderSnapshotAsync(subscription, transactionId, limit,
			cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(
		OrderStatusSubscription subscription, long transactionId, int limit,
		CancellationToken cancellationToken)
	{
		var symbol = subscription.Symbols is { Length: 1 }
			? subscription.Symbols[0]
			: null;
		var ordersTask = RestClient.GetOrdersAsync(symbol, subscription.From,
			subscription.To, 1, limit, cancellationToken).AsTask();
		var tradesTask = RestClient.GetPrivateTradesAsync(symbol,
			subscription.From, subscription.To, 1, limit,
			cancellationToken).AsTask();
		await Task.WhenAll(ordersTask, tradesTask);
		UpdateServerTime(RestClient.ServerTime);
		foreach (var order in ordersTask.Result
			.Where(static item => item is not null)
			.Where(item => Matches(subscription, item))
			.OrderBy(GetOrderTime)
			.TakeLast(limit))
			await SendOrderAsync(order, transactionId, cancellationToken);
		foreach (var trade in tradesTask.Result
			.Where(static item => item is not null)
			.Where(item => Matches(subscription, item))
			.OrderBy(static item => item.Timestamp)
			.TakeLast(limit))
			await SendPrivateTradeAsync(trade, transactionId, false,
				cancellationToken);
	}

	private ValueTask SendHoldingAsync(OrderlyNetworkHolding holding,
		long transactionId, CancellationToken cancellationToken)
	{
		if (holding?.Token.IsEmpty() != false)
			return default;
		var time = holding.UpdatedTime > 0
			? holding.UpdatedTime.FromOrderlyMilliseconds()
			: ServerTime;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = PortfolioName,
			SecurityId = holding.Token.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, holding.Holding, true)
		.TryAdd(PositionChangeTypes.BlockedValue,
			holding.Frozen + holding.IsolatedOrderFrozen, true),
			cancellationToken);
	}

	private async ValueTask SendPositionSnapshotAsync(
		OrderlyNetworkPosition[] positions, long transactionId,
		CancellationToken cancellationToken)
	{
		var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var position in positions ?? [])
			if (position?.Symbol.IsEmpty() == false && position.Quantity != 0)
				current.Add(position.Symbol);
		string[] missing;
		using (_sync.EnterScope())
		{
			missing = [.. _knownPositions.Where(symbol => !current.Contains(symbol))];
			_knownPositions.Clear();
			_knownPositions.UnionWith(current);
		}
		foreach (var position in positions ?? [])
			await SendPositionAsync(position, transactionId, cancellationToken);
		foreach (var symbol in missing)
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = PortfolioName,
				SecurityId = symbol.ToStockSharp(),
				ServerTime = ServerTime,
				OriginalTransactionId = transactionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, 0m, true),
				cancellationToken);
	}

	private ValueTask SendPositionAsync(OrderlyNetworkPosition position,
		long transactionId, CancellationToken cancellationToken)
	{
		if (position?.Symbol.IsEmpty() != false)
			return default;
		var timeValue = position.UpdatedTime > 0
			? position.UpdatedTime
			: position.Timestamp;
		var time = timeValue > 0
			? timeValue.FromOrderlyMilliseconds()
			: ServerTime;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = PortfolioName,
			SecurityId = position.Symbol.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
			Side = position.Quantity == 0
				? null
				: position.Quantity > 0 ? Sides.Buy : Sides.Sell,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, position.Quantity.Abs(), true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, position.MarkPrice, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnL, true)
		.TryAdd(PositionChangeTypes.LiquidationPrice,
			position.LiquidationPrice, true), cancellationToken);
	}

	private ValueTask SendOrderAsync(OrderlyNetworkOrder order,
		long transactionId, CancellationToken cancellationToken)
	{
		if (order?.Symbol.IsEmpty() != false || order.OrderId <= 0)
			return default;
		var clientId = order.ClientOrderId;
		var localTransactionId = GetTransactionId(clientId);
		var volume = order.Quantity.Abs();
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToStockSharp(),
			ServerTime = GetOrderTime(order),
			PortfolioName = PortfolioName,
			Side = order.Side.ToStockSharp(),
			OrderVolume = volume,
			Balance = (volume - order.ExecutedQuantity.Abs()).Max(0m),
			OrderPrice = order.Price ?? 0m,
			AveragePrice = order.AveragePrice,
			OrderType = order.OrderType.ToStockSharp(),
			OrderState = order.Status.ToStockSharp(),
			OrderId = order.OrderId,
			OrderStringId = order.OrderId.ToString(CultureInfo.InvariantCulture),
			UserOrderId = clientId,
			TransactionId = localTransactionId,
			OriginalTransactionId = transactionId,
			TimeInForce = order.OrderType.ToTimeInForce(),
			PostOnly = order.OrderType == OrderlyNetworkOrderTypes.PostOnly,
			PositionEffect = order.IsReduceOnly == true
				? OrderPositionEffects.CloseOnly
				: null,
			Condition = new OrderlyNetworkOrderCondition
			{
				IsReduceOnly = order.IsReduceOnly ?? false,
				VisibleQuantity = order.VisibleQuantity,
			},
		}, cancellationToken);
	}

	private ValueTask SendPrivateTradeAsync(OrderlyNetworkPrivateTrade trade,
		long transactionId, bool isOnlyNew,
		CancellationToken cancellationToken)
	{
		if (trade?.Symbol.IsEmpty() != false || trade.Id <= 0)
			return default;
		var tradeId = trade.Id.ToString(CultureInfo.InvariantCulture);
		var isNew = TryAcceptTrade(tradeId);
		if (isOnlyNew && !isNew)
			return default;
		var clientTransactionId = 0L;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = trade.Symbol.ToStockSharp(),
			ServerTime = trade.Timestamp.FromOrderlyMilliseconds(),
			PortfolioName = PortfolioName,
			Side = trade.Side.ToStockSharp(),
			OrderId = trade.OrderId,
			OrderStringId = trade.OrderId.ToString(CultureInfo.InvariantCulture),
			TradeId = trade.Id,
			TradeStringId = tradeId,
			TradePrice = trade.Price,
			TradeVolume = trade.Quantity,
			Commission = trade.Fee,
			CommissionCurrency = trade.FeeAsset,
			TransactionId = clientTransactionId,
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}

	private async ValueTask OnBalancesAsync(
		OrderlyNetworkSocketEnvelope<OrderlyNetworkSocketBalancesData> envelope,
		CancellationToken cancellationToken)
	{
		if (envelope?.Data?.Balances is null)
			return;
		UpdateServerTime(envelope.Timestamp);
		long[] targets;
		using (_sync.EnterScope())
			targets = [.. _portfolioSubscriptions];
		foreach (var target in targets)
			foreach (var balance in envelope.Data.Balances.Entries ?? [])
				if (balance?.Asset.IsEmpty() == false)
					await SendOutMessageAsync(new PositionChangeMessage
					{
						PortfolioName = PortfolioName,
						SecurityId = balance.Asset.ToStockSharp(),
						ServerTime = envelope.Timestamp.FromOrderlyMilliseconds(),
						OriginalTransactionId = target,
					}
					.TryAdd(PositionChangeTypes.CurrentValue,
						balance.Holding, true)
					.TryAdd(PositionChangeTypes.BlockedValue,
						balance.Frozen + balance.IsolatedOrderFrozen, true),
						cancellationToken);
	}

	private async ValueTask OnPositionsAsync(
		OrderlyNetworkSocketEnvelope<OrderlyNetworkSocketPositionsData> envelope,
		CancellationToken cancellationToken)
	{
		if (envelope?.Data is null)
			return;
		UpdateServerTime(envelope.Timestamp);
		long[] targets;
		using (_sync.EnterScope())
			targets = [.. _portfolioSubscriptions];
		foreach (var target in targets)
			foreach (var position in envelope.Data.Positions ?? [])
				await SendSocketPositionAsync(position, target,
					envelope.Timestamp, cancellationToken);
	}

	private ValueTask SendSocketPositionAsync(
		OrderlyNetworkSocketPosition position, long transactionId,
		long envelopeTimestamp, CancellationToken cancellationToken)
	{
		if (position?.Symbol.IsEmpty() != false)
			return default;
		using (_sync.EnterScope())
			if (position.Quantity == 0)
				_knownPositions.Remove(position.Symbol);
			else
				_knownPositions.Add(position.Symbol);
		var timestamp = position.UpdatedTime > 0
			? position.UpdatedTime
			: position.Timestamp > 0 ? position.Timestamp : envelopeTimestamp;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = PortfolioName,
			SecurityId = position.Symbol.ToStockSharp(),
			ServerTime = timestamp.FromOrderlyMilliseconds(),
			OriginalTransactionId = transactionId,
			Side = position.Quantity == 0
				? null
				: position.Quantity > 0 ? Sides.Buy : Sides.Sell,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, position.Quantity.Abs(), true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, position.MarkPrice, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnL, true)
		.TryAdd(PositionChangeTypes.LiquidationPrice,
			position.LiquidationPrice, true), cancellationToken);
	}

	private async ValueTask OnExecutionAsync(
		OrderlyNetworkSocketEnvelope<OrderlyNetworkExecutionReport> envelope,
		CancellationToken cancellationToken)
	{
		var report = envelope?.Data;
		if (report?.Symbol.IsEmpty() != false || report.OrderId <= 0)
			return;
		UpdateServerTime(report.Timestamp > 0
			? report.Timestamp
			: envelope.Timestamp);
		var targets = new HashSet<long>();
		var transactionId = GetTransactionId(report.ClientOrderId);
		if (transactionId > 0)
			targets.Add(transactionId);
		using (_sync.EnterScope())
			foreach (var subscription in _orderSubscriptions)
				if (Matches(subscription.Value, report))
					targets.Add(subscription.Key);
		var tradeId = report.MatchId.IsEmpty()
			? report.TradeId.ToString(CultureInfo.InvariantCulture)
			: report.MatchId;
		var isNewTrade = report.TradeId > 0 && report.ExecutedQuantity > 0 &&
			TryAcceptTrade(tradeId);
		foreach (var target in targets)
		{
			await SendExecutionOrderAsync(report, target, cancellationToken);
			if (isNewTrade)
				await SendExecutionTradeAsync(report, tradeId, target,
					cancellationToken);
		}
	}

	private ValueTask SendExecutionOrderAsync(OrderlyNetworkExecutionReport report,
		long target, CancellationToken cancellationToken)
	{
		var transactionId = GetTransactionId(report.ClientOrderId);
		var timeValue = report.Timestamp > 0
			? report.Timestamp
			: ServerTime.ToOrderlyMilliseconds();
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = report.Symbol.ToStockSharp(),
			ServerTime = timeValue.FromOrderlyMilliseconds(),
			PortfolioName = PortfolioName,
			Side = report.Side.ToStockSharp(),
			OrderVolume = report.Quantity.Abs(),
			Balance = (report.Quantity.Abs() -
				report.TotalExecutedQuantity.Abs()).Max(0m),
			OrderPrice = report.Price ?? 0m,
			AveragePrice = report.AveragePrice,
			OrderType = report.OrderType.ToStockSharp(),
			OrderState = report.Status.ToStockSharp(),
			OrderId = report.OrderId,
			OrderStringId = report.OrderId.ToString(CultureInfo.InvariantCulture),
			UserOrderId = report.ClientOrderId,
			TransactionId = transactionId,
			OriginalTransactionId = target,
			TimeInForce = report.OrderType.ToTimeInForce(),
			PostOnly = report.OrderType == OrderlyNetworkOrderTypes.PostOnly,
			Error = report.Status == OrderlyNetworkOrderStatuses.Rejected
				? new InvalidOperationException(report.Reason.IsEmpty()
					? "Orderly Network rejected the order."
					: report.Reason)
				: null,
		}, cancellationToken);
	}

	private ValueTask SendExecutionTradeAsync(
		OrderlyNetworkExecutionReport report, string tradeId, long target,
		CancellationToken cancellationToken)
	{
		var timeValue = report.Timestamp > 0
			? report.Timestamp
			: ServerTime.ToOrderlyMilliseconds();
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = report.Symbol.ToStockSharp(),
			ServerTime = timeValue.FromOrderlyMilliseconds(),
			PortfolioName = PortfolioName,
			Side = report.Side.ToStockSharp(),
			OrderId = report.OrderId,
			OrderStringId = report.OrderId.ToString(CultureInfo.InvariantCulture),
			TradeId = report.TradeId,
			TradeStringId = tradeId,
			TradePrice = report.ExecutedPrice,
			TradeVolume = report.ExecutedQuantity.Abs(),
			Commission = report.Fee,
			CommissionCurrency = report.FeeAsset,
			TransactionId = GetTransactionId(report.ClientOrderId),
			OriginalTransactionId = target,
		}, cancellationToken);
	}

	private OrderStatusSubscription CreateOrderStatusSubscription(
		OrderStatusMessage message)
	{
		var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (!message.SecurityId.SecurityCode.IsEmpty())
			symbols.Add(GetSymbol(message.SecurityId));
		foreach (var securityId in message.SecurityIds)
			if (!securityId.SecurityCode.IsEmpty())
				symbols.Add(GetSymbol(securityId));
		long? orderId = message.OrderId;
		if (orderId is null && !message.OrderStringId.IsEmpty())
		{
			if (!long.TryParse(message.OrderStringId, NumberStyles.None,
				CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
				throw new InvalidOperationException(
					"Orderly order filter must be a positive numeric ID.");
			orderId = parsed;
		}
		return new()
		{
			Symbols = [.. symbols],
			OrderId = orderId,
			ClientOrderId = message.UserOrderId?.Trim(),
			Side = message.Side,
			Volume = message.Volume,
			States = message.States ?? [],
			From = message.From?.EnsureOrderlyUtc(),
			To = message.To?.EnsureOrderlyUtc(),
		};
	}

	private static bool Matches(OrderStatusSubscription subscription,
		OrderlyNetworkOrder order)
	{
		if (subscription.Symbols is { Length: > 0 } &&
			!subscription.Symbols.Contains(order.Symbol,
				StringComparer.OrdinalIgnoreCase))
			return false;
		if (subscription.OrderId is long orderId && order.OrderId != orderId)
			return false;
		if (!subscription.ClientOrderId.IsEmpty() &&
			!subscription.ClientOrderId.Equals(order.ClientOrderId,
				StringComparison.OrdinalIgnoreCase))
			return false;
		if (subscription.Side is Sides side &&
			order.Side.ToStockSharp() != side)
			return false;
		if (subscription.Volume is decimal volume &&
			order.Quantity.Abs() != volume.Abs())
			return false;
		if (subscription.States is { Length: > 0 } &&
			!subscription.States.Contains(order.Status.ToStockSharp()))
			return false;
		var time = GetOrderTime(order);
		return (subscription.From is null || time >= subscription.From) &&
			(subscription.To is null || time <= subscription.To);
	}

	private static bool Matches(OrderStatusSubscription subscription,
		OrderlyNetworkPrivateTrade trade)
	{
		if (subscription.Symbols is { Length: > 0 } &&
			!subscription.Symbols.Contains(trade.Symbol,
				StringComparer.OrdinalIgnoreCase))
			return false;
		if (subscription.OrderId is long orderId && trade.OrderId != orderId)
			return false;
		if (subscription.Side is Sides side &&
			trade.Side.ToStockSharp() != side)
			return false;
		var time = trade.Timestamp.FromOrderlyMilliseconds();
		return (subscription.From is null || time >= subscription.From) &&
			(subscription.To is null || time <= subscription.To);
	}

	private static bool Matches(OrderStatusSubscription subscription,
		OrderlyNetworkExecutionReport report)
	{
		if (subscription.Symbols is { Length: > 0 } &&
			!subscription.Symbols.Contains(report.Symbol,
				StringComparer.OrdinalIgnoreCase))
			return false;
		if (subscription.OrderId is long orderId && report.OrderId != orderId)
			return false;
		if (!subscription.ClientOrderId.IsEmpty() &&
			!subscription.ClientOrderId.Equals(report.ClientOrderId,
				StringComparison.OrdinalIgnoreCase))
			return false;
		if (subscription.Side is Sides side &&
			report.Side.ToStockSharp() != side)
			return false;
		if (subscription.Volume is decimal volume &&
			report.Quantity.Abs() != volume.Abs())
			return false;
		if (subscription.States is { Length: > 0 } &&
			!subscription.States.Contains(report.Status.ToStockSharp()))
			return false;
		var time = report.Timestamp > 0
			? report.Timestamp.FromOrderlyMilliseconds()
			: DateTime.UtcNow;
		return (subscription.From is null || time >= subscription.From) &&
			(subscription.To is null || time <= subscription.To);
	}

	private static DateTime GetOrderTime(OrderlyNetworkOrder order)
	{
		var value = order.UpdatedTime > 0
			? order.UpdatedTime
			: order.CreatedTime;
		return value > 0
			? value.FromOrderlyMilliseconds()
			: DateTime.UtcNow;
	}

	private static string CreateClientOrderId(long transactionId,
		string userOrderId)
	{
		var value = userOrderId.IsEmpty()
			? transactionId.ToString(CultureInfo.InvariantCulture)
			: userOrderId.Trim();
		if (value.Length is < 1 or > 36 || value[0] == '-')
			throw new InvalidOperationException(
				"Orderly client order ID must contain 1 to 36 characters and cannot start with a hyphen.");
		return value;
	}

	private static long ResolveOrderId(long? numericId, string stringId,
		string operation)
	{
		if (numericId is long value && value > 0)
			return value;
		if (!stringId.IsEmpty() && long.TryParse(stringId, NumberStyles.None,
			CultureInfo.InvariantCulture, out value) && value > 0)
			return value;
		throw new InvalidOperationException(
			$"Orderly {operation} requires a positive numeric order ID.");
	}

	private static void ValidateOrder(OrderlyNetworkSymbolInfo market,
		OrderTypes orderType, decimal volume, decimal price,
		decimal? visibleQuantity, decimal? slippage)
	{
		if (volume <= 0)
			throw new InvalidOperationException("Order volume must be positive.");
		if (market.MinimumBase is decimal minimum && volume < minimum)
			throw new InvalidOperationException(
				$"Orderly order volume must be at least {minimum.ToWire()}.");
		if (market.MaximumBase is decimal maximum && volume > maximum)
			throw new InvalidOperationException(
				$"Orderly order volume cannot exceed {maximum.ToWire()}.");
		if (market.VolumeStep is > 0 && !IsMultipleOf(volume,
			market.VolumeStep.Value))
			throw new InvalidOperationException(
				$"Orderly order volume must be a multiple of {market.VolumeStep.Value.ToWire()}.");
		if (orderType == OrderTypes.Limit)
		{
			if (price <= 0)
				throw new InvalidOperationException(
					"Limit order price must be positive.");
			if (market.PriceStep is > 0 && !IsMultipleOf(price,
				market.PriceStep.Value))
				throw new InvalidOperationException(
					$"Orderly order price must be a multiple of {market.PriceStep.Value.ToWire()}.");
			if (market.MinimumNotional is decimal minimumNotional &&
				price * volume < minimumNotional)
				throw new InvalidOperationException(
					$"Orderly order notional must be at least {minimumNotional.ToWire()}.");
		}
		if (visibleQuantity is decimal visible &&
			(visible < 0 || visible > volume))
			throw new InvalidOperationException(
				"Orderly visible quantity must be between zero and order volume.");
		if (slippage is decimal slippageValue && slippageValue < 0)
			throw new InvalidOperationException(
				"Orderly slippage cannot be negative.");
	}

	private static bool IsMultipleOf(decimal value, decimal step)
		=> step > 0 && value % step == 0;
}
