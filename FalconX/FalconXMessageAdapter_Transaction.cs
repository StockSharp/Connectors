namespace StockSharp.FalconX;

public partial class FalconXMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsureConnected();
		ValidatePortfolio(regMsg.PortfolioName);
		var pair = GetPair(regMsg.SecurityId);
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Volume));
		if (regMsg.PostOnly == true)
			throw new NotSupportedException(
				"FalconX RFQ and order APIs do not expose post-only orders.");
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Market or OrderTypes.Limit))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		var condition = regMsg.Condition as FalconXOrderCondition ?? new();
		if (condition.SlippageBps is < 0)
			throw new ArgumentOutOfRangeException(
				nameof(condition.SlippageBps));
		if (condition.IsTwap)
		{
			await PlaceSocketOrderAsync(regMsg, pair, volume, orderType,
				condition, cancellationToken);
			return;
		}
		if (orderType == OrderTypes.Market ||
			regMsg.TimeInForce == TimeInForce.MatchOrCancel)
		{
			await PlaceRestOrderAsync(regMsg, pair, volume, orderType,
				condition, cancellationToken);
			return;
		}
		await PlaceSocketOrderAsync(regMsg, pair, volume, orderType,
			condition, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		EnsureConnected();
		ValidatePortfolio(replaceMsg.PortfolioName);
		if ((replaceMsg.OrderType ?? OrderTypes.Limit) != OrderTypes.Limit)
			throw new NotSupportedException(
				"FalconX can update only a resting limit order.");
		if (replaceMsg.TimeInForce is TimeInForce.MatchOrCancel or
			TimeInForce.CancelBalance)
			throw new NotSupportedException(
				"FalconX WebSocket updates support only GTC or GTX limit orders.");
		if (replaceMsg.Volume <= 0 || replaceMsg.Price <= 0)
			throw new ArgumentOutOfRangeException(nameof(replaceMsg));
		var pair = GetPair(replaceMsg.SecurityId);
		var orderId = ResolveOrderId(replaceMsg.OldOrderStringId,
			replaceMsg.OldOrderId, replaceMsg.TransactionId);
		var tracked = await GetOrderContextAsync(orderId, replaceMsg.SecurityId,
			cancellationToken);
		if (tracked.OrderType != FalconXOrderTypes.Limit)
			throw new NotSupportedException(
				"FalconX supports updates only for plain limit orders.");
		var expiry = ValidateExpiry(replaceMsg.TillDate);
		var clientOrderId = Guid.NewGuid().ToString("D");
		var requestId = Guid.NewGuid().ToString("D");
		var pending = new PendingOrderRequest
		{
			TransactionId = replaceMsg.TransactionId,
			OrderTransactionId = replaceMsg.TransactionId,
			SecurityId = pair.ToStockSharp(),
			PortfolioName = _portfolioName,
			Side = replaceMsg.Side,
			OrderType = OrderTypes.Limit,
			NativeOrderType = FalconXOrderTypes.Limit,
			Volume = replaceMsg.Volume.Abs(),
			Price = replaceMsg.Price,
			TimeInForce = TimeInForce.PutInQueue,
			ClientOrderId = clientOrderId,
		};
		AddPendingOrder(requestId, pending);
		try
		{
			await OrderSocket.SendOrderAsync(new()
			{
				Action = FalconXSocketActions.UpdateOrder,
				RequestId = requestId,
				OrderType = FalconXOrderTypes.Limit,
				OrderDetails = new()
				{
					OrderId = orderId,
					OriginalClientOrderId = tracked.ClientOrderId.ThrowIfEmpty(
						nameof(tracked.ClientOrderId)),
					ClientOrderId = clientOrderId,
					Quantity = replaceMsg.Volume.Abs(),
					QuantityToken = pair.BaseToken,
					LimitPrice = replaceMsg.Price,
					Expiry = expiry?.ToFalconXTime(),
				},
			}, cancellationToken);
		}
		catch
		{
			RemovePendingOrder(requestId);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureConnected();
		ValidatePortfolio(cancelMsg.PortfolioName);
		var orderId = ResolveOrderId(cancelMsg.OrderStringId, cancelMsg.OrderId,
			cancelMsg.TransactionId);
		var tracked = await GetOrderContextAsync(orderId, cancelMsg.SecurityId,
			cancellationToken);
		await CancelNativeOrderAsync(orderId, tracked, cancelMsg.TransactionId,
			cancelMsg.Side, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureConnected();
		ValidatePortfolio(cancelMsg.PortfolioName);
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"FalconX bulk cancellation cannot close portfolio positions.");
		if (cancelMsg.IsStop == true)
			throw new NotSupportedException(
				"FalconX does not expose stop orders in the documented API.");
		var to = DateTime.UtcNow;
		var orders = await RestClient.GetOrdersAsync(to.AddDays(-31), to,
			HistoryLimit, cancellationToken);
		foreach (var order in orders
			.Where(static order => order.Status.ToStockSharp() ==
				OrderStates.Active))
		{
			var message = CreateRestOrderMessage(order, 0, 0);
			if (!IsSecurityMatch(message.SecurityId, cancelMsg.SecurityId) ||
				(cancelMsg.Side is Sides side && message.Side != side))
				continue;
			var tracked = await GetOrderContextAsync(order.NativeId,
				message.SecurityId, cancellationToken);
			await CancelNativeOrderAsync(order.NativeId, tracked,
				cancelMsg.TransactionId, cancelMsg.Side, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		ValidatePortfolio(lookupMsg.PortfolioName);
		if (!lookupMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_portfolioSubscriptions.Remove(lookupMsg.OriginalTransactionId);
			return;
		}
		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = _portfolioName,
			BoardCode = BoardCodes.FalconX,
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
		using (_sync.EnterScope())
			_portfolioSubscriptions.Add(lookupMsg.TransactionId);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		ValidatePortfolio(statusMsg.PortfolioName);
		if (!statusMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_orderSubscriptions.Remove(statusMsg.OriginalTransactionId);
			return;
		}
		if (statusMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}
		var filter = CreateOrderSubscription(statusMsg);
		await SendOrderSnapshotAsync(filter, statusMsg.TransactionId,
			cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}
		await OrderSocket.EnsureConnectedAsync(cancellationToken);
		using (_sync.EnterScope())
			_orderSubscriptions.Add(statusMsg.TransactionId, filter);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private async ValueTask PlaceRestOrderAsync(OrderRegisterMessage message,
		FalconXTokenPair pair, decimal volume, OrderTypes orderType,
		FalconXOrderCondition condition, CancellationToken cancellationToken)
	{
		if (orderType == OrderTypes.Limit)
		{
			if (message.TimeInForce != TimeInForce.MatchOrCancel)
				throw new NotSupportedException(
					"FalconX REST limit orders require fill-or-kill time in force.");
			if (message.Price <= 0)
				throw new ArgumentOutOfRangeException(nameof(message.Price));
		}
		else if (message.TimeInForce is not null)
			throw new NotSupportedException(
				"FalconX REST market orders do not accept time in force.");
		await OrderSocket.EnsureConnectedAsync(cancellationToken);
		var clientOrderId = "ss:" + message.TransactionId.ToString(
			CultureInfo.InvariantCulture);
		var clientOrderUuid = Guid.NewGuid().ToString("D");
		var response = await RestClient.PlaceOrderAsync(new()
		{
			TokenPair = pair,
			Quantity = new()
			{
				Token = pair.BaseToken,
				Value = volume,
			},
			Side = message.Side.ToFalconX(),
			OrderType = orderType == OrderTypes.Market
				? FalconXOrderTypes.Market
				: FalconXOrderTypes.Limit,
			TimeInForce = orderType == OrderTypes.Limit
				? FalconXTimeInForces.FillOrKill
				: null,
			LimitPrice = orderType == OrderTypes.Limit ? message.Price : null,
			SlippageBps = orderType == OrderTypes.Limit
				? condition.SlippageBps
				: null,
			ClientOrderId = clientOrderId,
			ClientOrderUuid = clientOrderUuid,
		}, cancellationToken) ?? throw new InvalidDataException(
			"FalconX returned an empty order response.");
		if (response.NativeId.IsEmpty())
			throw new InvalidDataException(
				"FalconX accepted an order without an identifier.");
		TrackOrder(response.NativeId, response.ClientOrderId ?? clientOrderId,
			message.TransactionId, pair.ToStockSharp(), message.Side,
			response.OrderType ?? (orderType == OrderTypes.Market
				? FalconXOrderTypes.Market
				: FalconXOrderTypes.Limit));
		var orderMessage = CreateRestOrderMessage(response,
			message.TransactionId, message.TransactionId);
		if (orderMessage.OrderVolume is null)
			orderMessage.OrderVolume = volume;
		if (orderMessage.OrderPrice == 0 && orderType == OrderTypes.Limit)
			orderMessage.OrderPrice = message.Price;
		await SendOutMessageAsync(orderMessage, cancellationToken);
		await SendRestFillsAsync(response, message.TransactionId,
			cancellationToken);
		SchedulePoll();
	}

	private async ValueTask PlaceSocketOrderAsync(OrderRegisterMessage message,
		FalconXTokenPair pair, decimal volume, OrderTypes orderType,
		FalconXOrderCondition condition, CancellationToken cancellationToken)
	{
		if (!condition.IsTwap && orderType != OrderTypes.Limit)
			throw new NotSupportedException(
				"FalconX order WebSocket accepts plain limit or TWAP orders.");
		if (!condition.IsTwap &&
			message.TimeInForce == TimeInForce.CancelBalance)
			throw new NotSupportedException(
				"FalconX WebSocket does not document immediate-or-cancel orders.");
		if (orderType == OrderTypes.Limit && message.Price <= 0)
			throw new ArgumentOutOfRangeException(nameof(message.Price));
		var expiry = ValidateExpiry(message.TillDate);
		FalconXTimeInForces timeInForce;
		if (expiry is null)
			timeInForce = FalconXTimeInForces.GoodTillCanceled;
		else
			timeInForce = FalconXTimeInForces.GoodTillExpiry;
		if (condition.IsTwap && orderType == OrderTypes.Limit && expiry is null)
			throw new InvalidOperationException(
				"FalconX limit TWAP orders require an expiry (GTX).");
		int? durationMinutes = null;
		if (condition.IsTwap)
		{
			var totalMinutes = condition.TwapDuration.TotalMinutes;
			if (totalMinutes < 1 || totalMinutes > TimeSpan.FromDays(14).TotalMinutes ||
				totalMinutes != Math.Truncate(totalMinutes))
				throw new ArgumentOutOfRangeException(
					nameof(condition.TwapDuration),
					"FalconX TWAP duration must be whole minutes between one minute and 14 days.");
			durationMinutes = checked((int)totalMinutes);
			if (condition.TwapTransactionsCount is <= 0)
				throw new ArgumentOutOfRangeException(
					nameof(condition.TwapTransactionsCount));
		}
		var clientOrderId = Guid.NewGuid().ToString("D");
		var requestId = Guid.NewGuid().ToString("D");
		var nativeType = condition.IsTwap
			? FalconXOrderTypes.Twap
			: FalconXOrderTypes.Limit;
		AddPendingOrder(requestId, new()
		{
			TransactionId = message.TransactionId,
			OrderTransactionId = message.TransactionId,
			SecurityId = pair.ToStockSharp(),
			PortfolioName = _portfolioName,
			Side = message.Side,
			OrderType = orderType,
			NativeOrderType = nativeType,
			Volume = volume,
			Price = message.Price,
			TimeInForce = TimeInForce.PutInQueue,
			ClientOrderId = clientOrderId,
		});
		try
		{
			await OrderSocket.SendOrderAsync(new()
			{
				Action = FalconXSocketActions.CreateOrder,
				RequestId = requestId,
				OrderType = nativeType,
				OrderDetails = new()
				{
					ClientOrderId = clientOrderId,
					BaseToken = pair.BaseToken,
					QuoteToken = pair.QuoteToken,
					Quantity = volume,
					QuantityToken = pair.BaseToken,
					LimitPrice = orderType == OrderTypes.Limit
						? message.Price
						: null,
					Side = message.Side.ToFalconX(),
					TimeInForce = timeInForce,
					Expiry = expiry?.ToFalconXTime(),
					TransactionTimeMinutes = durationMinutes,
					TransactionsCount = condition.TwapTransactionsCount,
				},
			}, cancellationToken);
		}
		catch
		{
			RemovePendingOrder(requestId);
			throw;
		}
	}

	private async ValueTask CancelNativeOrderAsync(string orderId,
		TrackedOrder tracked, long transactionId, Sides? side,
		CancellationToken cancellationToken)
	{
		if (tracked.OrderType is not FalconXOrderTypes nativeOrderType)
			throw new InvalidDataException(
				"FalconX did not return the order type required for cancellation.");
		var clientOrderId = Guid.NewGuid().ToString("D");
		var requestId = Guid.NewGuid().ToString("D");
		AddPendingOrder(requestId, new()
		{
			TransactionId = transactionId,
			OrderTransactionId = tracked.TransactionId,
			SecurityId = tracked.SecurityId,
			PortfolioName = _portfolioName,
			Side = side ?? tracked.Side,
			OrderType = nativeOrderType.ToStockSharp(),
			NativeOrderType = nativeOrderType,
			ClientOrderId = clientOrderId,
		});
		try
		{
			await OrderSocket.SendOrderAsync(new()
			{
				Action = FalconXSocketActions.CancelOrder,
				RequestId = requestId,
				OrderType = nativeOrderType,
				OrderDetails = new()
				{
					OrderId = orderId,
					OriginalClientOrderId = tracked.ClientOrderId.ThrowIfEmpty(
						nameof(tracked.ClientOrderId)),
					ClientOrderId = clientOrderId,
				},
			}, cancellationToken);
		}
		catch
		{
			RemovePendingOrder(requestId);
			throw;
		}
	}

	private async ValueTask SendPortfolioSnapshotAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		var balances = await RestClient.GetPortfolioBalancesAsync(
			cancellationToken) ?? [];
		await SendBalancesAsync(balances, transactionId, DateTime.UtcNow,
			cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(OrderSubscription filter,
		long transactionId, CancellationToken cancellationToken)
	{
		FalconXRestOrder[] orders;
		if (!filter.OrderId.IsEmpty())
			orders = [await RestClient.GetOrderOrQuoteAsync(filter.OrderId,
				cancellationToken)];
		else
		{
			var to = filter.To ?? DateTime.UtcNow;
			var from = filter.From ?? to.AddDays(-31);
			if (to - from > TimeSpan.FromDays(31))
				throw new ArgumentOutOfRangeException(nameof(filter),
					"FalconX order-history ranges cannot exceed 31 days.");
			var fetchLimit = Math.Min(100, filter.Skip + filter.Limit);
			orders = await RestClient.GetOrdersAsync(from, to, fetchLimit,
				cancellationToken);
		}
		var messages = orders
			.Where(static order => order is not null)
			.Select(order => CreateRestOrderMessage(order, transactionId, 0))
			.Where(message => IsOrderMatch(message, filter))
			.Skip(filter.Skip)
			.Take(filter.Limit)
			.ToArray();
		foreach (var message in messages)
		{
			var native = orders.FirstOrDefault(order => order.NativeId.Equals(
				message.OrderStringId, StringComparison.OrdinalIgnoreCase));
			TrackOrder(message.OrderStringId,
				native?.ClientOrderId,
				message.TransactionId, message.SecurityId, message.Side,
				native?.OrderType ?? (message.OrderType == OrderTypes.Market
					? FalconXOrderTypes.Market
					: FalconXOrderTypes.Limit));
			await SendOutMessageAsync(message, cancellationToken);
			if (native is not null)
				await SendRestFillsAsync(native, transactionId, cancellationToken);
		}
	}

	private async ValueTask PollPrivateAsync(
		CancellationToken cancellationToken)
	{
		long[] portfolioSubscriptions;
		KeyValuePair<long, OrderSubscription>[] orderSubscriptions;
		using (_sync.EnterScope())
		{
			portfolioSubscriptions = [.. _portfolioSubscriptions];
			orderSubscriptions = [.. _orderSubscriptions];
		}
		if (portfolioSubscriptions.Length > 0)
		{
			var balances = await RestClient.GetPortfolioBalancesAsync(
				cancellationToken) ?? [];
			var time = DateTime.UtcNow;
			foreach (var transactionId in portfolioSubscriptions)
				await SendBalancesAsync(balances, transactionId, time,
					cancellationToken);
		}
		foreach (var (transactionId, filter) in orderSubscriptions)
			await SendOrderSnapshotAsync(filter, transactionId,
				cancellationToken);
	}

	private async ValueTask SendBalancesAsync(FalconXPortfolioBalance[] balances,
		long transactionId, DateTime time, CancellationToken cancellationToken)
	{
		foreach (var group in balances
			.Where(static balance => balance is not null &&
				!balance.Asset.IsEmpty())
			.GroupBy(static balance => balance.Asset,
				StringComparer.OrdinalIgnoreCase))
		{
			var price = group.Select(static balance => balance.Price)
				.FirstOrDefault(static value => value is not null);
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = new()
				{
					SecurityCode = group.Key.ToUpperInvariant(),
					BoardCode = BoardCodes.FalconX,
				},
				ServerTime = time,
				OriginalTransactionId = transactionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue,
				group.Sum(static balance => balance.NetBalance), true)
			.TryAdd(PositionChangeTypes.BlockedValue,
				group.Sum(static balance => balance.OpenOrders ?? 0m), true)
			.TryAdd(PositionChangeTypes.CurrentPrice, price, true),
				cancellationToken);
		}
	}

	private async ValueTask OnOrderReceivedAsync(
		FalconXSocketResponse<FalconXSocketOrderBody> response,
		CancellationToken cancellationToken)
	{
		var pending = GetPendingOrder(response.RequestId);
		var body = response.Body;
		if (body?.OrderId.IsEmpty() == false)
		{
			var securityId = body.TokenPair is null
				? pending?.SecurityId ?? GetTrackedOrder(body.OrderId)?.SecurityId ??
					default
				: body.TokenPair.ToStockSharp();
			TrackOrder(body.OrderId,
				body.ClientOrderId ?? pending?.ClientOrderId,
				pending?.OrderTransactionId ?? 0, securityId,
				body.Side?.ToStockSharp() ?? pending?.Side,
				body.OrderType ?? pending?.NativeOrderType);
		}
		if (response.Event is FalconXSocketEvents.CreateOrderAcknowledged or
			FalconXSocketEvents.UpdateOrderAcknowledged or
			FalconXSocketEvents.CancelOrderAcknowledged)
			return;
		var errorText = response.Error.GetMessage() ?? body?.Error.GetMessage();
		var isRejected = response.Event is
			FalconXSocketEvents.CreateOrderRejected or
			FalconXSocketEvents.UpdateOrderRejected or
			FalconXSocketEvents.CancelOrderRejected or
			FalconXSocketEvents.OrderRejected or
			FalconXSocketEvents.ErrorResponse ||
			response.Status is FalconXSocketStatuses.Error or
				FalconXSocketStatuses.Failure;
		var message = CreateSocketOrderMessage(response, pending, isRejected,
			errorText);
		if (message is not null)
		{
			if (pending is not null)
			{
				message.OriginalTransactionId = pending.TransactionId;
				await SendOutMessageAsync(message.Clone(), cancellationToken);
			}
			await SendOrderToSubscriptionsAsync(message, cancellationToken);
		}
		if (body?.Fills?.Length > 0)
			await SendSocketFillsAsync(body, pending, cancellationToken);
		if (pending is not null)
			RemovePendingOrder(response.RequestId);
		SchedulePoll();
	}

	private ExecutionMessage CreateSocketOrderMessage(
		FalconXSocketResponse<FalconXSocketOrderBody> response,
		PendingOrderRequest pending, bool isRejected, string errorText)
	{
		var body = response.Body;
		var tracked = GetTrackedOrder(body?.OrderId);
		var securityId = body?.TokenPair?.ToStockSharp() ??
			pending?.SecurityId ?? tracked?.SecurityId ?? default;
		if (securityId.SecurityCode.IsEmpty() && body?.OrderId.IsEmpty() != false)
			return null;
		var side = body?.Side?.ToStockSharp() ?? pending?.Side ?? tracked?.Side;
		if (side is null)
			return null;
		var volume = body?.Quantity?.Value ?? pending?.Volume;
		var filled = body?.ExecutedQuantity ?? body?.Fills?.Sum(static fill =>
			fill?.Quantity?.Value ?? 0m) ?? 0m;
		var state = isRejected
			? OrderStates.Failed
			: body?.OrderStatus?.ToStockSharp() ?? response.Event switch
			{
				FalconXSocketEvents.CancelOrderAccepted => OrderStates.Done,
				_ => OrderStates.Active,
			};
		var time = body?.Fills?
			.Select(static fill => fill?.ExecuteTime.TryParseFalconXTime())
			.Where(static value => value is not null)
			.Select(static value => value.Value)
			.DefaultIfEmpty(DateTime.UtcNow)
			.Max() ?? DateTime.UtcNow;
		UpdateServerTime(time);
		return new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = securityId,
			ServerTime = time,
			PortfolioName = pending?.PortfolioName ?? _portfolioName,
			Side = side.Value,
			OrderPrice = body?.LimitPrice ?? pending?.Price ?? 0m,
			OrderVolume = volume,
			Balance = volume is decimal total ? Math.Max(0m, total - filled) : null,
			OrderType = body?.OrderType?.ToStockSharp() ??
				pending?.NativeOrderType?.ToStockSharp() ??
				tracked?.OrderType?.ToStockSharp() ?? pending?.OrderType ??
				OrderTypes.Limit,
			OrderState = state,
			OrderStringId = body?.OrderId,
			TransactionId = pending?.OrderTransactionId ??
				tracked?.TransactionId ?? 0,
			TimeInForce = (body?.TimeInForce).ToStockSharp() ??
				pending?.TimeInForce,
			ExpiryDate = body?.ExpiryTime.TryParseFalconXTime(),
			Error = isRejected ? new InvalidOperationException(errorText ??
				"FalconX rejected the order request.") : null,
		};
	}

	private async ValueTask SendOrderToSubscriptionsAsync(
		ExecutionMessage message, CancellationToken cancellationToken)
	{
		KeyValuePair<long, OrderSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _orderSubscriptions];
		var sent = false;
		foreach (var (transactionId, filter) in subscriptions)
		{
			if (!IsOrderMatch(message, filter))
				continue;
			var clone = (ExecutionMessage)message.Clone();
			clone.OriginalTransactionId = transactionId;
			await SendOutMessageAsync(clone, cancellationToken);
			sent = true;
		}
		if (!sent && message.OriginalTransactionId == 0)
			await SendOutMessageAsync(message, cancellationToken);
	}

	private async ValueTask SendSocketFillsAsync(FalconXSocketOrderBody body,
		PendingOrderRequest pending, CancellationToken cancellationToken)
	{
		var tracked = GetTrackedOrder(body.OrderId);
		var securityId = body.TokenPair?.ToStockSharp() ?? pending?.SecurityId ??
			tracked?.SecurityId ?? default;
		var side = body.Side?.ToStockSharp() ?? pending?.Side ?? tracked?.Side;
		if (side is not Sides executionSide)
			throw new InvalidDataException(
				"FalconX returned a fill without an order side.");
		var targets = GetFillTargets(securityId, body.OrderId, side,
			pending?.TransactionId ?? 0);
		foreach (var fill in body.Fills.Where(static fill => fill is not null &&
			!fill.QuoteId.IsEmpty()))
		{
			var time = fill.ExecuteTime.TryParseFalconXTime() ?? DateTime.UtcNow;
			UpdateServerTime(time);
			foreach (var target in targets)
			{
				if (!MarkFill(target, fill.QuoteId))
					continue;
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					SecurityId = securityId,
					ServerTime = time,
					PortfolioName = _portfolioName,
					Side = executionSide,
					OrderStringId = body.OrderId,
					TradeStringId = fill.QuoteId,
					TradePrice = fill.Price,
					TradeVolume = fill.Quantity?.Value,
					OriginalTransactionId = target,
				}, cancellationToken);
			}
		}
	}

	private async ValueTask SendRestFillsAsync(FalconXRestOrder order,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		var securityId = order.TokenPair?.ToStockSharp() ?? default;
		var nativeSide = order.Side ?? order.SideRequested ?? order.SideExecuted;
		if (nativeSide is not FalconXSides value)
			throw new InvalidDataException(
				"FalconX returned a fill without an order side.");
		var side = value.ToStockSharp();
		if (order.Fills?.Length > 0)
		{
			foreach (var fill in order.Fills.Where(static fill => fill is not null))
			{
				var fillId = !fill.QuoteId.IsEmpty() ? fill.QuoteId :
					order.NativeId + ":" + (fill.FillNumber?.ToString(
						CultureInfo.InvariantCulture) ?? "fill");
				await SendRestFillAsync(order, securityId, side, fillId,
					fill.Price?.Value, fill.Quantity?.Value,
					(fill.FillTime ?? fill.ExecuteTime).TryParseFalconXTime(),
					originalTransactionId, cancellationToken);
			}
			return;
		}
		if (!order.OrderId.IsEmpty() || order.QuoteId.IsEmpty() ||
			(order.IsFilled != true && order.Status is not (
				FalconXOrderStatuses.Success or FalconXOrderStatuses.Filled)))
			return;
		var price = value == FalconXSides.Buy
			? order.BuyPrice
			: order.SellPrice;
		var volume = order.QuantityFilled?.Value ?? order.QuantityRequested?.Value;
		await SendRestFillAsync(order, securityId, side, order.QuoteId, price,
			volume, order.ExecuteTime.TryParseFalconXTime(),
			originalTransactionId, cancellationToken);
	}

	private async ValueTask SendRestFillAsync(FalconXRestOrder order,
		SecurityId securityId, Sides side, string fillId, decimal? price,
		decimal? volume, DateTime? time, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (fillId.IsEmpty() || price is not > 0 || volume is not > 0 ||
			!MarkFill(originalTransactionId, fillId))
			return;
		var serverTime = time ?? order.ExecuteTime.TryParseFalconXTime() ??
			DateTime.UtcNow;
		UpdateServerTime(serverTime);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = securityId,
			ServerTime = serverTime,
			PortfolioName = _portfolioName,
			Side = side,
			OrderStringId = order.NativeId,
			TradeStringId = fillId,
			TradePrice = price,
			TradeVolume = volume,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private ExecutionMessage CreateRestOrderMessage(FalconXRestOrder order,
		long originalTransactionId, long transactionId)
	{
		ArgumentNullException.ThrowIfNull(order);
		var side = order.Side ?? order.SideRequested ?? order.SideExecuted;
		if (side is not FalconXSides nativeSide)
			throw new InvalidDataException(
				"FalconX returned an order without a side.");
		var time = order.UpdateTime.TryParseFalconXTime() ??
			order.ExecuteTime.TryParseFalconXTime() ??
			order.CreateTime.TryParseFalconXTime() ??
			order.QuoteTime.TryParseFalconXTime() ?? DateTime.UtcNow;
		UpdateServerTime(time);
		var volume = order.QuantityRequested?.Value;
		var filled = order.QuantityFilled?.Value ?? order.Fills?.Sum(static fill =>
			fill?.Quantity?.Value ?? 0m) ?? (order.IsFilled == true ? volume : 0m);
		var state = order.Status.ToStockSharp();
		var errorText = order.Error.GetMessage() ?? order.RejectedReason;
		var price = order.LimitPrice ?? (nativeSide == FalconXSides.Buy
			? order.BuyPrice
			: order.SellPrice) ?? 0m;
		return new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.TokenPair?.ToStockSharp() ?? default,
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = nativeSide.ToStockSharp(),
			OrderPrice = price,
			OrderVolume = volume,
			Balance = volume is decimal total
				? Math.Max(0m, total - (filled ?? 0m))
				: null,
			OrderType = order.OrderType?.ToStockSharp() ?? OrderTypes.Limit,
			OrderState = state,
			OrderStringId = order.NativeId,
			TransactionId = transactionId,
			OriginalTransactionId = originalTransactionId,
			TimeInForce = order.TimeInForce.ToStockSharp(),
			ExpiryDate = order.ExpiryTime.TryParseFalconXTime(),
			Error = state == OrderStates.Failed
				? new InvalidOperationException(errorText ??
					"FalconX reported a failed order.")
				: null,
		};
	}

	private async ValueTask<TrackedOrder> GetOrderContextAsync(string orderId,
		SecurityId fallbackSecurityId, CancellationToken cancellationToken)
	{
		var tracked = GetTrackedOrder(orderId);
		if (tracked is not null && !tracked.ClientOrderId.IsEmpty() &&
			tracked.OrderType is not null)
			return tracked;
		var order = await RestClient.GetOrderAsync(orderId, cancellationToken) ??
			throw new InvalidDataException(
				"FalconX returned an empty order response.");
		var securityId = order.TokenPair?.ToStockSharp() ?? fallbackSecurityId;
		TrackOrder(orderId, order.ClientOrderId, tracked?.TransactionId ?? 0,
			securityId,
			(order.Side ?? order.SideRequested ?? order.SideExecuted)?.ToStockSharp(),
			order.OrderType ?? FalconXOrderTypes.Limit);
		return GetTrackedOrder(orderId);
	}

	private void AddPendingOrder(string requestId, PendingOrderRequest pending)
	{
		using (_sync.EnterScope())
			_pendingOrders.Add(requestId, pending);
	}

	private PendingOrderRequest GetPendingOrder(string requestId)
	{
		using (_sync.EnterScope())
			return !requestId.IsEmpty() && _pendingOrders.TryGetValue(requestId,
				out var pending) ? pending : null;
	}

	private void RemovePendingOrder(string requestId)
	{
		using (_sync.EnterScope())
			if (!requestId.IsEmpty())
				_pendingOrders.Remove(requestId);
	}

	private long[] GetFillTargets(SecurityId securityId, string orderId,
		Sides? side, long directTarget)
	{
		var targets = new HashSet<long>();
		if (directTarget != 0)
			targets.Add(directTarget);
		using (_sync.EnterScope())
			foreach (var (transactionId, filter) in _orderSubscriptions)
				if (IsTradeMatch(securityId, orderId, side, filter))
					targets.Add(transactionId);
		if (targets.Count == 0)
			targets.Add(0);
		return [.. targets];
	}

	private bool MarkFill(long transactionId, string fillId)
	{
		using (_sync.EnterScope())
			return _seenFills.Add(transactionId.ToString(
				CultureInfo.InvariantCulture) + "|" + fillId);
	}

	private OrderSubscription CreateOrderSubscription(OrderStatusMessage message)
		=> new()
		{
			SecurityId = message.SecurityId,
			SecurityIds = message.SecurityIds,
			OrderId = !message.OrderStringId.IsEmpty()
				? message.OrderStringId
				: message.OrderId?.ToString(CultureInfo.InvariantCulture),
			Side = message.Side,
			Volume = message.Volume,
			States = message.States,
			From = message.From?.EnsureUtc(),
			To = message.To?.EnsureUtc(),
			Skip = Math.Max(0L, message.Skip ?? 0).Min(100L).To<int>(),
			Limit = (message.Count ?? HistoryLimit).Min(HistoryLimit).Max(1)
				.To<int>(),
		};

	private static bool IsOrderMatch(ExecutionMessage message,
		OrderSubscription filter)
	{
		if (!IsSecurityMatch(message.SecurityId, filter.SecurityId))
			return false;
		if (filter.SecurityIds?.Length > 0 && !filter.SecurityIds.Any(
			securityId => IsSecurityMatch(message.SecurityId, securityId)))
			return false;
		if (!filter.OrderId.IsEmpty() && !filter.OrderId.Equals(
			message.OrderStringId, StringComparison.OrdinalIgnoreCase))
			return false;
		if (filter.Side is Sides side && message.Side != side)
			return false;
		if (filter.Volume is decimal volume && message.OrderVolume != volume)
			return false;
		if (filter.States?.Length > 0 &&
			(message.OrderState is not OrderStates state ||
				!filter.States.Contains(state)))
			return false;
		if (filter.From is DateTime from && message.ServerTime < from)
			return false;
		if (filter.To is DateTime to && message.ServerTime > to)
			return false;
		return true;
	}

	private static bool IsTradeMatch(SecurityId securityId, string orderId,
		Sides? side, OrderSubscription filter)
		=> IsSecurityMatch(securityId, filter.SecurityId) &&
			(filter.SecurityIds?.Length is not > 0 || filter.SecurityIds.Any(
				id => IsSecurityMatch(securityId, id))) &&
			(filter.OrderId.IsEmpty() || filter.OrderId.Equals(orderId,
				StringComparison.OrdinalIgnoreCase)) &&
			(filter.Side is null || filter.Side == side) &&
			(filter.States?.Length is not > 0 ||
				filter.States.Contains(OrderStates.Done));

	private static bool IsSecurityMatch(SecurityId securityId,
		SecurityId filter)
		=> (filter.SecurityCode.IsEmpty() || filter.SecurityCode.Equals(
			securityId.SecurityCode, StringComparison.OrdinalIgnoreCase)) &&
			(filter.BoardCode.IsEmpty() || filter.BoardCode.Equals(
				securityId.BoardCode, StringComparison.OrdinalIgnoreCase));

	private static string ResolveOrderId(string orderStringId, long? orderId,
		long transactionId)
	{
		if (!orderStringId.IsEmpty())
			return orderStringId.Trim();
		if (orderId is long numeric)
			return numeric.ToString(CultureInfo.InvariantCulture);
		throw new InvalidOperationException(
			"FalconX order string ID is required for transaction " +
			transactionId.ToString(CultureInfo.InvariantCulture) + ".");
	}

	private static DateTime? ValidateExpiry(DateTime? value)
	{
		if (value is not DateTime expiry)
			return null;
		expiry = expiry.EnsureUtc();
		var now = DateTime.UtcNow;
		if (expiry <= now || expiry > now.AddDays(14))
			throw new ArgumentOutOfRangeException(nameof(value), value,
				"FalconX GTX expiry must be in the future and at most 14 days away.");
		return expiry;
	}
}
