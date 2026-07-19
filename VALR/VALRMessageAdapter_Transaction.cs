namespace StockSharp.VALR;

public partial class VALRMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		ValidatePortfolio(regMsg.PortfolioName);
		var market = GetMarket(regMsg.SecurityId);
		var condition = regMsg.Condition as VALROrderCondition ?? new();
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		var volume = regMsg.Volume.Abs();
		var quoteAmount = condition.QuoteAmount;
		if (regMsg.VisibleVolume is > 0 && regMsg.VisibleVolume != volume)
			throw new NotSupportedException(
				"VALR does not document iceberg orders.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException("VALR does not support GTD orders.");
		if (condition.IsMargin && !market.IsMarginAllowed && !market.IsFuture)
			throw new InvalidOperationException(
				$"VALR does not allow margin trading for '{market.Symbol}'.");
		if (condition.IsReduceOnly && !market.IsFuture)
			throw new InvalidOperationException(
				"VALR reduce-only is valid for perpetual futures only.");
		if (quoteAmount is <= 0)
			throw new InvalidOperationException(
				"VALR quote amount must be positive.");
		if (quoteAmount is not null && orderType != OrderTypes.Market)
			throw new InvalidOperationException(
				"VALR quote amount is valid for market orders only.");
		if (volume <= 0 && quoteAmount is null)
			throw new InvalidOperationException(
				"VALR order volume must be positive.");
		if (volume > 0)
		{
			ValidateRange(volume, market.MinimumVolume, market.MaximumVolume,
				"volume", market.Symbol);
			ValidateStep(volume, market.VolumeStep, "volume", market.Symbol);
		}
		if (quoteAmount is decimal cost)
			ValidateRange(cost, market.MinimumCost, market.MaximumCost,
				"quote amount", market.Symbol);

		var customerOrderId = CreateCustomerOrderId(regMsg.TransactionId,
			regMsg.UserOrderId);
		var timeInForce = regMsg.TimeInForce.ToVALR();
		VALRIdResponse result;
		switch (orderType)
		{
			case OrderTypes.Limit:
				ValidatePrice(regMsg.Price, market, "limit price");
				result = await RestClient.PlaceLimitOrderAsync(new()
				{
					Side = regMsg.Side.ToVALR(),
					Quantity = volume.ToWire(),
					Price = regMsg.Price.ToWire(),
					Pair = market.Symbol,
					IsPostOnly = regMsg.PostOnly == true,
					IsReduceOnly = condition.IsReduceOnly,
					CustomerOrderId = customerOrderId,
					TimeInForce = timeInForce,
					IsMargin = condition.IsMargin,
				}, cancellationToken);
				break;
			case OrderTypes.Market:
				if (regMsg.PostOnly == true)
					throw new InvalidOperationException(
						"A market order cannot be post-only.");
				result = await RestClient.PlaceMarketOrderAsync(new()
				{
					Side = regMsg.Side.ToVALR(),
					BaseAmount = quoteAmount is null ? volume.ToWire() : null,
					QuoteAmount = quoteAmount?.ToWire(),
					Pair = market.Symbol,
					CustomerOrderId = customerOrderId,
					IsMargin = condition.IsMargin,
					IsReduceOnly = condition.IsReduceOnly,
				}, cancellationToken);
				break;
			case OrderTypes.Conditional:
				if (regMsg.PostOnly == true)
					throw new InvalidOperationException(
						"A VALR stop-limit order cannot be post-only.");
				ValidatePrice(regMsg.Price, market, "limit price");
				if (condition.TriggerPrice is not decimal trigger)
					throw new InvalidOperationException(
						"VALR conditional orders require a trigger price.");
				ValidatePrice(trigger, market, "trigger price");
				result = await RestClient.PlaceStopLimitOrderAsync(new()
				{
					Side = regMsg.Side.ToVALR(),
					Quantity = volume.ToWire(),
					Price = regMsg.Price.ToWire(),
					StopPrice = trigger.ToWire(),
					Pair = market.Symbol,
					Type = condition.IsTakeProfit
						? VALRConditionalTypes.TakeProfitLimit
						: VALRConditionalTypes.StopLossLimit,
					CustomerOrderId = customerOrderId,
					TimeInForce = timeInForce,
					IsMargin = condition.IsMargin,
					IsReduceOnly = condition.IsReduceOnly,
				}, cancellationToken);
				break;
			default:
				throw new NotSupportedException(
					LocalizedStrings.OrderUnsupportedType.Put(orderType,
						regMsg.TransactionId));
		}

		if (result?.Id.IsEmpty() != false)
			throw new InvalidDataException(
				"VALR accepted an order without returning its identifier.");
		var tracked = new TrackedOrder
		{
			TransactionId = regMsg.TransactionId,
			Symbol = market.Symbol,
			ExchangeOrderId = result.Id,
			CustomerOrderId = customerOrderId,
			Side = regMsg.Side,
			OrderType = orderType,
			Volume = volume,
			Price = regMsg.Price,
			IsPostOnly = regMsg.PostOnly == true,
			TimeInForce = regMsg.TimeInForce,
			Condition = condition.Clone() as VALROrderCondition,
		};
		TrackOrder(tracked, result.Id, customerOrderId,
			regMsg.TransactionId.ToString(CultureInfo.InvariantCulture));
		await SendTrackedOrderAsync(tracked, OrderStates.Active, volume,
			regMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		ValidatePortfolio(replaceMsg.PortfolioName);
		var orderId = ResolveOrderIdentifier(replaceMsg.OldOrderId,
			replaceMsg.OldOrderStringId, "replacement");
		var previous = GetTrackedOrder(orderId);
		if (previous?.OrderType == OrderTypes.Conditional ||
			replaceMsg.OrderType == OrderTypes.Conditional)
			throw new NotSupportedException(
				"VALR conditional orders use a separate modification contract that StockSharp order replacement cannot represent safely.");
		if (previous?.OrderType == OrderTypes.Market ||
			replaceMsg.OrderType == OrderTypes.Market)
			throw new NotSupportedException(
				"VALR can modify active limit orders only.");
		var market = previous is not null
			? GetMarket(previous.Symbol)
			: GetMarket(replaceMsg.SecurityId);
		var volume = replaceMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException(
				"VALR replacement volume must be positive.");
		ValidateRange(volume, market.MinimumVolume, market.MaximumVolume,
			"volume", market.Symbol);
		ValidateStep(volume, market.VolumeStep, "volume", market.Symbol);
		ValidatePrice(replaceMsg.Price, market, "limit price");

		_ = await RestClient.ModifyOrderAsync(new()
		{
			OrderId = orderId,
			Pair = market.Symbol,
			ModifyMatchStrategy = VALRModifyMatchStrategies.RetainOriginal,
			NewPrice = replaceMsg.Price.ToWire(),
			NewTotalQuantity = volume.ToWire(),
			CustomerOrderId = CreateCustomerOrderId(replaceMsg.TransactionId,
				replaceMsg.UserOrderId),
		}, cancellationToken);
		var tracked = new TrackedOrder
		{
			TransactionId = replaceMsg.TransactionId,
			Symbol = market.Symbol,
			ExchangeOrderId = orderId,
			CustomerOrderId = replaceMsg.UserOrderId,
			Side = previous?.Side ?? replaceMsg.Side,
			OrderType = OrderTypes.Limit,
			Volume = volume,
			Price = replaceMsg.Price,
			IsPostOnly = replaceMsg.PostOnly ?? previous?.IsPostOnly ?? false,
			TimeInForce = replaceMsg.TimeInForce ?? previous?.TimeInForce,
			Condition = replaceMsg.Condition as VALROrderCondition,
		};
		TrackOrder(tracked, orderId, replaceMsg.UserOrderId);
		await SendTrackedOrderAsync(tracked, OrderStates.Active, volume,
			replaceMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		var orderId = ResolveOrderIdentifier(cancelMsg.OrderId,
			cancelMsg.OrderStringId, "cancellation");
		var tracked = GetTrackedOrder(orderId);
		var market = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? tracked is null
				? throw new InvalidOperationException(
					"VALR cancellation requires the order market when the order was not registered by this adapter.")
				: GetMarket(tracked.Symbol)
			: GetMarket(cancelMsg.SecurityId);
		_ = await RestClient.CancelOrderAsync(new()
		{
			OrderId = orderId,
			Pair = market.Symbol,
		}, cancellationToken);
		await SendCanceledOrderAsync(market, orderId, cancelMsg.TransactionId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"VALR bulk order cancellation does not close futures positions.");
		var symbol = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(cancelMsg.SecurityId).Symbol;
		var openOrders = await RestClient.GetOpenOrdersAsync(cancellationToken);
		var selected = (openOrders ?? []).Where(order => order is not null &&
			(symbol.IsEmpty() || order.CurrencyPair.EqualsIgnoreCase(symbol)) &&
			(cancelMsg.Side is null ||
				order.Side.ToStockSharp() == cancelMsg.Side) &&
			(cancelMsg.IsStop is null || IsConditional(order.Type) ==
				cancelMsg.IsStop.Value)).ToArray();

		if (cancelMsg.Side is null && cancelMsg.IsStop is null)
			_ = await RestClient.CancelAllOrdersAsync(symbol, cancellationToken);
		else
			foreach (var order in selected)
				_ = await RestClient.CancelOrderAsync(new()
				{
					OrderId = order.OrderId,
					Pair = order.CurrencyPair,
				}, cancellationToken);

		foreach (var order in selected)
			await SendCanceledOrderAsync(GetMarket(order.CurrencyPair),
				order.OrderId, cancelMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsurePrivateReady();
		if (!lookupMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_portfolioSubscriptions.Remove(lookupMsg.OriginalTransactionId);
			return;
		}
		ValidatePortfolio(lookupMsg.PortfolioName);
		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = GetPortfolioName(),
			BoardCode = BoardCodes.Valr,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId, true,
			cancellationToken);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_portfolioSubscriptions.Add(lookupMsg.TransactionId);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId,
			cancellationToken);
		EnsurePrivateReady();
		if (!statusMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_orderSubscriptions.Remove(statusMsg.OriginalTransactionId);
			return;
		}
		if (statusMsg.Count is <= 0)
		{
			await CompleteOrderStatusAsync(statusMsg, cancellationToken);
			return;
		}
		ValidatePortfolio(statusMsg.PortfolioName);
		var market = statusMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(statusMsg.SecurityId);
		string orderId = null;
		if (statusMsg.HasOrderId())
			orderId = ResolveOrderIdentifier(statusMsg.OrderId,
				statusMsg.OrderStringId, "lookup");
		var tracked = GetTrackedOrder(orderId);
		if (tracked is not null)
			market ??= GetMarket(tracked.Symbol);
		var maximum = (statusMsg.Count ?? 100).Min(100).Max(1).To<int>();
		await SendOrderSnapshotAsync(statusMsg.TransactionId, market?.Symbol,
			orderId, statusMsg.Side, statusMsg.From, statusMsg.To, maximum,
			true, cancellationToken);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_orderSubscriptions[statusMsg.TransactionId] = new()
			{
				Symbol = market?.Symbol,
				OrderId = orderId,
				Side = statusMsg.Side,
			};
	}

	private async ValueTask SendOrderSnapshotAsync(long transactionId,
		string symbol, string orderId, Sides? side, DateTime? from, DateTime? to,
		int maximum, bool force, CancellationToken cancellationToken)
	{
		if (!orderId.IsEmpty())
		{
			if (symbol.IsEmpty())
				throw new InvalidOperationException(
					"VALR order lookup requires the market when the order was not registered by this adapter.");
			VALROrderStatus order;
			try
			{
				order = await RestClient.GetActiveOrderAsync(symbol, orderId,
					cancellationToken);
			}
			catch (VALRApiException error) when (
				error.StatusCode == HttpStatusCode.NotFound)
			{
				order = await RestClient.GetHistoricalOrderAsync(orderId,
					cancellationToken);
			}
			await SendOrderStatusAsync(order, transactionId, force,
				cancellationToken);
			return;
		}

		var sent = 0;
		var openOrders = await RestClient.GetOpenOrdersAsync(cancellationToken);
		foreach (var order in (openOrders ?? []).Where(order => order is not null &&
			(symbol.IsEmpty() || order.CurrencyPair.EqualsIgnoreCase(symbol)) &&
			(side is null || order.Side.ToStockSharp() == side)).Take(maximum))
		{
			await SendOpenOrderAsync(order, transactionId, force,
				cancellationToken);
			sent++;
		}

		if (sent < maximum)
		{
			var history = await RestClient.GetOrderHistoryAsync(new()
			{
				Limit = maximum - sent,
				CurrencyPair = symbol,
				StartTime = from?.ToUniversalTime(),
				EndTime = to?.ToUniversalTime(),
				IsShowZeroVolumeCancels = true,
			}, cancellationToken);
			foreach (var order in (history ?? []).Where(order => order is not null &&
				(side is null || order.Side.ToStockSharp() == side)))
				await SendOrderStatusAsync(order, transactionId, force,
					cancellationToken);
		}

		var trades = await RestClient.GetAccountTradesAsync(new()
		{
			Limit = maximum,
			StartTime = from?.ToUniversalTime(),
			EndTime = to?.ToUniversalTime(),
		}, cancellationToken);
		foreach (var trade in (trades ?? []).Where(trade => trade is not null &&
			(symbol.IsEmpty() || trade.CurrencyPair.EqualsIgnoreCase(symbol)) &&
			(side is null || trade.Side.ToStockSharp() == side)))
			await SendAccountTradeAsync(trade, transactionId,
				cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshotAsync(long transactionId,
		bool force, CancellationToken cancellationToken)
	{
		var balances = await RestClient.GetBalancesAsync(cancellationToken);
		foreach (var balance in balances ?? [])
			await SendBalanceAsync(balance, transactionId, force,
				cancellationToken);
		var positions = await RestClient.GetOpenPositionsAsync(new()
		{
			Limit = 100,
		}, cancellationToken);
		foreach (var position in positions ?? [])
			await SendPositionAsync(position, transactionId, force,
				cancellationToken);
	}

	private async ValueTask RefreshPrivateSnapshotsAsync(
		CancellationToken cancellationToken)
	{
		long[] portfolioSubscriptions;
		KeyValuePair<long, OrderSubscription>[] orderSubscriptions;
		using (_sync.EnterScope())
		{
			portfolioSubscriptions = [.. _portfolioSubscriptions];
			orderSubscriptions = [.. _orderSubscriptions];
		}
		foreach (var subscription in portfolioSubscriptions)
			await SendPortfolioSnapshotAsync(subscription, false,
				cancellationToken);
		foreach (var subscription in orderSubscriptions)
			await SendOrderSnapshotAsync(subscription.Key,
				subscription.Value.Symbol, subscription.Value.OrderId,
				subscription.Value.Side, null, null, 100, false,
				cancellationToken);
	}

	private ValueTask SendBalanceAsync(VALRBalance balance,
		long transactionId, bool force, CancellationToken cancellationToken)
	{
		if (balance?.Currency.IsEmpty() != false)
			return default;
		return SendBalanceAsync(balance.Currency, balance.Available,
			balance.Reserved, balance.Total, balance.BorrowedAmount,
			balance.UpdatedAt, transactionId, force, cancellationToken);
	}

	private ValueTask SendBalanceAsync(string currency, decimal available,
		decimal reserved, decimal total, decimal borrowed, string updatedAt,
		long transactionId, bool force, CancellationToken cancellationToken)
	{
		if (currency.IsEmpty())
			return default;
		var fingerprint = new BalanceFingerprint(available, reserved, total,
			borrowed);
		var key = $"{transactionId}:{currency}";
		using (_sync.EnterScope())
		{
			if (!force && _balanceFingerprints.TryGetValue(key, out var previous) &&
				previous == fingerprint)
				return default;
			_balanceFingerprints[key] = fingerprint;
		}
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(),
			SecurityId = new SecurityId
			{
				SecurityCode = currency.ToUpperInvariant(),
				BoardCode = BoardCodes.Valr,
			},
			ServerTime = updatedAt.ToVALRTime(CurrentTime),
			OriginalTransactionId = transactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, available, true)
		.TryAdd(PositionChangeTypes.BlockedValue, reserved, true),
			cancellationToken);
	}

	private ValueTask SendPositionAsync(VALRPosition position,
		long transactionId, bool force, CancellationToken cancellationToken,
		decimal? currentValue = null)
	{
		if (position?.Pair.IsEmpty() != false || position.PositionId.IsEmpty())
			return default;
		var fingerprint = new PositionFingerprint(position.Side,
			currentValue ?? position.Quantity, position.AverageEntryPrice,
			position.RealizedPnL, position.UnrealizedPnL, position.UpdatedAt);
		var key = $"{transactionId}:{position.PositionId}";
		using (_sync.EnterScope())
		{
			if (!force && _positionFingerprints.TryGetValue(key,
				out var previous) && previous == fingerprint)
				return default;
			_positionFingerprints[key] = fingerprint;
		}
		var quantity = currentValue ?? (position.Side == VALRSides.Sell
			? -position.Quantity.Abs()
			: position.Quantity.Abs());
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(),
			SecurityId = position.Pair.ToStockSharp(),
			DepoName = position.PositionId,
			ServerTime = position.UpdatedAt.ToVALRTime(CurrentTime),
			OriginalTransactionId = transactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, quantity, true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.AverageEntryPrice,
			true)
		.TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedPnL, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnL, true)
		.TryAdd(PositionChangeTypes.Leverage,
			position.Leverage > 0 ? position.Leverage : null, true),
			cancellationToken);
	}

	private ValueTask SendTrackedOrderAsync(TrackedOrder tracked,
		OrderStates state, decimal balance, long originalTransactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = tracked.Symbol.ToStockSharp(),
			ServerTime = CurrentTime,
			PortfolioName = GetPortfolioName(),
			Side = tracked.Side,
			OrderVolume = tracked.Volume,
			Balance = balance,
			OrderPrice = tracked.Price,
			OrderType = tracked.OrderType,
			OrderState = state,
			OrderStringId = tracked.ExchangeOrderId,
			UserOrderId = tracked.CustomerOrderId,
			TransactionId = tracked.TransactionId,
			OriginalTransactionId = originalTransactionId,
			PostOnly = tracked.IsPostOnly,
			TimeInForce = tracked.TimeInForce,
			Condition = tracked.Condition,
		}, cancellationToken);

	private ValueTask SendOpenOrderAsync(VALROpenOrder order,
		long originalTransactionId, bool force,
		CancellationToken cancellationToken)
	{
		if (order?.OrderId.IsEmpty() != false || order.CurrencyPair.IsEmpty())
			return default;
		var remaining = order.RemainingQuantity > 0
			? order.RemainingQuantity
			: order.Quantity;
		var volume = order.OriginalQuantity > 0
			? order.OriginalQuantity
			: remaining;
		var fingerprint = new OrderFingerprint(order.Status, remaining,
			(volume - remaining).Max(0m), order.UpdatedAt);
		if (!ShouldSendOrder(order.OrderId, originalTransactionId, fingerprint,
			force))
			return default;
		var tracked = GetTrackedOrder(order.OrderId) ??
			CreateTrackedOrder(order.OrderId, order.CustomerOrderId,
				order.CurrencyPair, order.Side, order.Type, volume, order.Price,
				order.StopPrice, order.TimeInForce);
		TrackOrder(tracked, order.OrderId, order.CustomerOrderId);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.CurrencyPair.ToStockSharp(),
			ServerTime = order.UpdatedAt.ToVALRTime(CurrentTime),
			PortfolioName = GetPortfolioName(),
			Side = order.Side.ToStockSharp(),
			OrderVolume = volume,
			Balance = remaining,
			OrderPrice = order.Price,
			OrderType = order.Type.ToStockSharp(),
			OrderState = order.Status.ToStockSharp(),
			OrderStringId = order.OrderId,
			UserOrderId = order.CustomerOrderId,
			TransactionId = tracked.TransactionId,
			OriginalTransactionId = originalTransactionId,
			PostOnly = order.Type == VALROrderTypes.PostOnlyLimit,
			TimeInForce = order.TimeInForce?.ToStockSharp(),
			Condition = CreateCondition(order.Type, order.StopPrice,
				order.IsMargin, order.Type == VALROrderTypes.LimitReduceOnly),
		}, cancellationToken);
	}

	private ValueTask SendOrderStatusAsync(VALROrderStatus order,
		long originalTransactionId, bool force,
		CancellationToken cancellationToken)
	{
		if (order?.OrderId.IsEmpty() != false || order.CurrencyPair.IsEmpty())
			return default;
		var executed = order.TotalExecutedQuantity > 0
			? order.TotalExecutedQuantity
			: order.ExecutedQuantity;
		var fingerprint = new OrderFingerprint(order.Status,
			order.RemainingQuantity, executed, order.UpdatedAt);
		if (!ShouldSendOrder(order.OrderId, originalTransactionId, fingerprint,
			force))
			return default;
		var tracked = GetTrackedOrder(order.OrderId) ??
			CreateTrackedOrder(order.OrderId, order.CustomerOrderId,
				order.CurrencyPair, order.Side, order.Type,
				order.OriginalQuantity, order.OriginalPrice, order.StopPrice,
				order.TimeInForce);
		TrackOrder(tracked, order.OrderId, order.CustomerOrderId);
		var state = order.Status.ToStockSharp();
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.CurrencyPair.ToStockSharp(),
			ServerTime = order.UpdatedAt.ToVALRTime(CurrentTime),
			PortfolioName = GetPortfolioName(),
			Side = order.Side.ToStockSharp(),
			OrderVolume = order.OriginalQuantity,
			Balance = state == OrderStates.Active
				? order.RemainingQuantity
				: 0m,
			OrderPrice = order.OriginalPrice,
			AveragePrice = order.AveragePrice > 0
				? order.AveragePrice
				: order.ExecutedPrice > 0 ? order.ExecutedPrice : null,
			OrderType = order.Type.ToStockSharp(),
			OrderState = state,
			OrderStringId = order.OrderId,
			UserOrderId = order.CustomerOrderId,
			TransactionId = tracked.TransactionId,
			OriginalTransactionId = originalTransactionId,
			PostOnly = order.Type == VALROrderTypes.PostOnlyLimit,
			TimeInForce = order.TimeInForce?.ToStockSharp(),
			Condition = CreateCondition(order.Type, order.StopPrice, false,
				order.Type == VALROrderTypes.LimitReduceOnly),
			Commission = order.TotalFee > 0
				? order.TotalFee
				: order.ExecutedFee > 0 ? order.ExecutedFee : null,
			Error = state == OrderStates.Failed
				? new InvalidOperationException(
					order.FailedReason.IsEmpty()
						? "VALR rejected the order."
						: order.FailedReason)
				: null,
		}, cancellationToken);
	}

	private bool ShouldSendOrder(string orderId, long transactionId,
		OrderFingerprint fingerprint, bool force)
	{
		var key = $"{transactionId}:{orderId}";
		using (_sync.EnterScope())
		{
			if (!force && _orderFingerprints.TryGetValue(key, out var previous) &&
				previous == fingerprint)
				return false;
			_orderFingerprints[key] = fingerprint;
			return true;
		}
	}

	private TrackedOrder CreateTrackedOrder(string orderId,
		string customerOrderId, string symbol, VALRSides side,
		VALROrderTypes type, decimal volume, decimal price, decimal? stopPrice,
		VALRTimeInForce? timeInForce)
	{
		var transactionId = long.TryParse(customerOrderId, NumberStyles.None,
			CultureInfo.InvariantCulture, out var parsed)
			? parsed
			: 0L;
		return new TrackedOrder
		{
			TransactionId = transactionId,
			Symbol = symbol.NormalizeSymbol(),
			ExchangeOrderId = orderId,
			CustomerOrderId = customerOrderId,
			Side = side.ToStockSharp(),
			OrderType = type.ToStockSharp(),
			Volume = volume,
			Price = price,
			IsPostOnly = type == VALROrderTypes.PostOnlyLimit,
			TimeInForce = timeInForce?.ToStockSharp(),
			Condition = CreateCondition(type, stopPrice, false,
				type == VALROrderTypes.LimitReduceOnly),
		};
	}

	private ValueTask SendCanceledOrderAsync(MarketDefinition market,
		string orderId, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var tracked = GetTrackedOrder(orderId);
		if (tracked is not null)
			return SendTrackedOrderAsync(tracked, OrderStates.Done, 0m,
				originalTransactionId, cancellationToken);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = CurrentTime,
			PortfolioName = GetPortfolioName(),
			OrderStringId = orderId,
			OrderState = OrderStates.Done,
			Balance = 0m,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private ValueTask SendAccountTradeAsync(VALRAccountTrade trade,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (trade?.Id.IsEmpty() != false || trade.OrderId.IsEmpty() ||
			trade.CurrencyPair.IsEmpty() || trade.Price <= 0 ||
			trade.Quantity <= 0 ||
			!AddAccountTrade(trade.Id, originalTransactionId))
			return default;
		var tracked = GetTrackedOrder(trade.OrderId) ??
			GetTrackedOrder(trade.CustomerOrderId);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = trade.CurrencyPair.ToStockSharp(),
			ServerTime = trade.TradedAt.ToVALRTime(CurrentTime),
			PortfolioName = GetPortfolioName(),
			Side = trade.Side.ToStockSharp(),
			OrderStringId = trade.OrderId,
			UserOrderId = trade.CustomerOrderId,
			TradeId = trade.SequenceId > 0 ? trade.SequenceId : null,
			TradeStringId = trade.Id,
			TradePrice = trade.Price,
			TradeVolume = trade.Quantity,
			Commission = trade.Fee,
			TransactionId = tracked?.TransactionId ?? 0,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private async ValueTask OnBalanceEventAsync(VALRSocketBalance update,
		CancellationToken cancellationToken)
	{
		if (update?.Data?.Currency?.Symbol.IsEmpty() != false)
			return;
		long[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _portfolioSubscriptions];
		foreach (var subscription in subscriptions)
			await SendBalanceAsync(update.Data.Currency.Symbol,
				update.Data.Available, update.Data.Reserved, update.Data.Total,
				update.Data.BorrowedAmount, update.Data.UpdatedAt, subscription,
				false, cancellationToken);
	}

	private async ValueTask OnOpenOrdersEventAsync(VALRSocketOpenOrders update,
		CancellationToken cancellationToken)
	{
		KeyValuePair<long, OrderSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _orderSubscriptions];
		foreach (var order in update?.Data ?? [])
			foreach (var subscription in subscriptions.Where(pair =>
				MatchesOrderSubscription(pair.Value, order.CurrencyPair,
					order.OrderId, order.Side.ToStockSharp())))
				await SendOpenOrderAsync(order, subscription.Key, false,
					cancellationToken);
	}

	private async ValueTask OnOrderStatusEventAsync(
		VALRSocketOrderStatus update, CancellationToken cancellationToken)
	{
		if (update?.Data is null)
			return;
		var order = update.Data;
		KeyValuePair<long, OrderSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _orderSubscriptions.Where(pair =>
				MatchesOrderSubscription(pair.Value, order.CurrencyPair,
					order.OrderId, order.Side.ToStockSharp()))];
		foreach (var subscription in subscriptions)
			await SendOrderStatusAsync(order, subscription.Key, false,
				cancellationToken);
	}

	private async ValueTask OnAccountTradeEventAsync(
		VALRSocketAccountTrade update, CancellationToken cancellationToken)
	{
		if (update?.Data is null)
			return;
		var trade = update.Data;
		KeyValuePair<long, OrderSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _orderSubscriptions.Where(pair =>
				MatchesOrderSubscription(pair.Value, trade.CurrencyPair,
					trade.OrderId, trade.Side.ToStockSharp()))];
		foreach (var subscription in subscriptions)
			await SendAccountTradeAsync(trade, subscription.Key,
				cancellationToken);
	}

	private async ValueTask OnPositionEventAsync(VALRSocketPosition update,
		CancellationToken cancellationToken)
	{
		if (update?.Data is null)
			return;
		long[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _portfolioSubscriptions];
		foreach (var subscription in subscriptions)
			await SendPositionAsync(update.Data, subscription, false,
				cancellationToken);
	}

	private async ValueTask OnPositionClosedEventAsync(
		VALRSocketClosedPosition update, CancellationToken cancellationToken)
	{
		if (update?.Data?.Pair.IsEmpty() != false ||
			update.Data.PositionId.IsEmpty())
			return;
		long[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _portfolioSubscriptions];
		foreach (var subscription in subscriptions)
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = GetPortfolioName(),
				SecurityId = update.Data.Pair.ToStockSharp(),
				DepoName = update.Data.PositionId,
				ServerTime = CurrentTime,
				OriginalTransactionId = subscription,
			}.TryAdd(PositionChangeTypes.CurrentValue, 0m, true),
				cancellationToken);
	}

	private static bool MatchesOrderSubscription(OrderSubscription subscription,
		string symbol, string orderId, Sides side)
		=> (subscription.Symbol.IsEmpty() ||
			subscription.Symbol.EqualsIgnoreCase(symbol)) &&
			(subscription.OrderId.IsEmpty() ||
				subscription.OrderId.EqualsIgnoreCase(orderId)) &&
			(subscription.Side is null || subscription.Side == side);

	private static bool IsConditional(VALROrderTypes type)
		=> type is VALROrderTypes.StopLossLimit or
			VALROrderTypes.TakeProfitLimit;

	private static VALROrderCondition CreateCondition(VALROrderTypes type,
		decimal? triggerPrice, bool isMargin, bool isReduceOnly)
		=> IsConditional(type) || isMargin || isReduceOnly
			? new()
			{
				TriggerPrice = triggerPrice,
				IsTakeProfit = type == VALROrderTypes.TakeProfitLimit,
				IsMargin = isMargin,
				IsReduceOnly = isReduceOnly,
			}
			: null;

	private static string CreateCustomerOrderId(long transactionId,
		string userOrderId)
	{
		var value = userOrderId.IsEmpty()
			? transactionId.ToString(CultureInfo.InvariantCulture)
			: userOrderId.Trim();
		if (value.Length > 50 || value.Any(static character =>
			!char.IsLetterOrDigit(character) && character != '-'))
			throw new InvalidOperationException(
				"VALR customer order IDs may contain letters, digits, and hyphens only and must not exceed 50 characters.");
		return value;
	}

	private static void ValidatePrice(decimal price, MarketDefinition market,
		string name)
	{
		if (price <= 0)
			throw new InvalidOperationException(
				$"VALR {name} must be positive for '{market.Symbol}'.");
		ValidateStep(price, market.PriceStep, name, market.Symbol);
	}

	private static void ValidateRange(decimal value, decimal minimum,
		decimal maximum, string name, string symbol)
	{
		if (minimum > 0 && value < minimum)
			throw new InvalidOperationException(
				$"VALR {name} must be at least {minimum} for '{symbol}'.");
		if (maximum > 0 && value > maximum)
			throw new InvalidOperationException(
				$"VALR {name} must not exceed {maximum} for '{symbol}'.");
	}

	private static void ValidateStep(decimal value, decimal step, string name,
		string symbol)
	{
		if (step > 0 && value % step != 0)
			throw new InvalidOperationException(
				$"VALR {name} must be aligned to step {step} for '{symbol}'.");
	}

	private async ValueTask CompleteOrderStatusAsync(OrderStatusMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
