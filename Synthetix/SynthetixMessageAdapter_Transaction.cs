namespace StockSharp.Synthetix;

public partial class SynthetixMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		ValidatePortfolio(regMsg.PortfolioName);
		EnsureTradingReady();
		await PlaceOrderAsync(GetMarket(regMsg.SecurityId), regMsg.TransactionId,
			regMsg.Side, regMsg.Volume.Abs(), regMsg.Price,
			regMsg.OrderType ?? OrderTypes.Limit, regMsg.TimeInForce,
			regMsg.PostOnly == true, regMsg.PositionEffect,
			regMsg.Condition as SynthetixOrderCondition ?? new(), regMsg.TillDate,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		ValidatePortfolio(replaceMsg.PortfolioName);
		EnsureTradingReady();
		var market = GetMarket(replaceMsg.SecurityId);
		var order = await ResolveOpenOrderAsync(replaceMsg.OldOrderStringId,
			replaceMsg.OldOrderId, market.Symbol, cancellationToken);
		var volume = replaceMsg.Volume.Abs();
		var price = replaceMsg.Price;
		ValidateOrderValues(market, volume, price,
			order.Type.ToStockSharpOrderType() == OrderTypes.Market);
		var condition = replaceMsg.Condition as SynthetixOrderCondition;
		var triggerPrice = condition?.TriggerPrice?.ToSynthetixDecimal() ??
			order.TriggerPrice ?? string.Empty;
		var response = await ApiClient.ModifyOrderAsync(new()
		{
			SubAccountId = SubAccountId,
			OrderId = GetVenueOrderId(order),
			Price = price > 0 ? price.ToSynthetixDecimal() : order.Price,
			Quantity = volume.ToSynthetixDecimal(),
			TriggerPrice = triggerPrice,
		}, Signer, cancellationToken);
		ValidateOperation(response, "modify order");
		var orderId = response.Order?.VenueId ?? response.OrderId ??
			GetVenueOrderId(order);
		TrackOrder(orderId, response.Order?.ClientId ?? order.Order?.ClientId,
			replaceMsg.TransactionId);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.Symbol.ToSynthetixSecurityId(),
			ServerTime = GetOperationTime(response),
			PortfolioName = PortfolioName,
			DepoName = SubAccountId,
			Side = replaceMsg.Side,
			OrderVolume = volume,
			Balance = volume,
			OrderPrice = price,
			OrderType = order.Type.ToStockSharpOrderType(),
			OrderState = OrderStates.Active,
			OrderStringId = orderId,
			TransactionId = replaceMsg.TransactionId,
			OriginalTransactionId = replaceMsg.TransactionId,
			Condition = condition,
		}, cancellationToken);
		SchedulePrivatePoll();
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		ValidatePortfolio(cancelMsg.PortfolioName);
		EnsureTradingReady();
		var symbol = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(cancelMsg.SecurityId).Symbol;
		var order = await ResolveOpenOrderAsync(cancelMsg.OrderStringId,
			cancelMsg.OrderId, symbol, cancellationToken);
		var orderId = GetVenueOrderId(order);
		var response = await ApiClient.CancelOrdersAsync(new()
		{
			SubAccountId = SubAccountId,
			OrderIds = [orderId],
		}, Signer, cancellationToken);
		ValidateOperation(response, "cancel order");
		await SendCancelledOrderAsync(orderId, order.Symbol,
			cancelMsg.TransactionId, cancellationToken);
		SchedulePrivatePoll();
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		ValidatePortfolio(cancelMsg.PortfolioName);
		EnsureTradingReady();
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"Synthetix bulk cancellation does not close positions.");
		var symbol = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(cancelMsg.SecurityId).Symbol;
		if (cancelMsg.Side is null && cancelMsg.IsStop is null)
		{
			var results = await ApiClient.CancelAllOrdersAsync(new()
			{
				SubAccountId = SubAccountId,
				Symbols = [symbol ?? "*"],
			}, Signer, cancellationToken);
			var errors = (results ?? [])
				.Where(static result => !result.Message.IsEmpty())
				.Select(static result => result.Message)
				.ToArray();
			if (errors.Length > 0)
				throw new InvalidOperationException(
					"Synthetix cancel-all: " + string.Join("; ", errors));
		}
		else
		{
			var orders = (await ApiClient.GetOpenOrdersAsync(SubAccountId,
				symbol, HistoryLimit, Signer, cancellationToken) ?? [])
				.Where(static order => order is not null)
				.Where(order => cancelMsg.Side is null ||
					order.Side.ToStockSharpSide() == cancelMsg.Side)
				.Where(order => cancelMsg.IsStop is null ||
					(order.Type.ToStockSharpOrderType() ==
						OrderTypes.Conditional) == cancelMsg.IsStop)
				.ToArray();
			foreach (var chunk in orders.Chunk(50))
			{
				var response = await ApiClient.CancelOrdersAsync(new()
				{
					SubAccountId = SubAccountId,
					OrderIds = [.. chunk.Select(GetVenueOrderId)],
				}, Signer, cancellationToken);
				ValidateOperation(response, "cancel orders");
			}
		}
		SchedulePrivatePoll();
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureAccountReady();
		ValidatePortfolio(lookupMsg.PortfolioName);
		if (!lookupMsg.IsSubscribe)
		{
			await RemovePortfolioSubscriptionAsync(
				lookupMsg.OriginalTransactionId, cancellationToken);
			return;
		}
		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = PortfolioName,
			BoardCode = BoardCodes.Synthetix,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId, true,
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
		try
		{
			await UpdatePrivateSubscriptionAsync(cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_portfolioSubscriptions.Remove(lookupMsg.TransactionId);
			throw;
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId,
			cancellationToken);
		EnsureAccountReady();
		ValidatePortfolio(statusMsg.PortfolioName);
		if (!statusMsg.IsSubscribe)
		{
			await RemoveOrderSubscriptionAsync(statusMsg.OriginalTransactionId,
				cancellationToken);
			return;
		}
		if (statusMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}
		var subscription = CreateOrderSubscription(statusMsg);
		await SendOrderSnapshotAsync(subscription, statusMsg.TransactionId, true,
			cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_orderSubscriptions.Add(statusMsg.TransactionId, subscription);
		try
		{
			await UpdatePrivateSubscriptionAsync(cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_orderSubscriptions.Remove(statusMsg.TransactionId);
			throw;
		}
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private async ValueTask PlaceOrderAsync(SynthetixMarket market,
		long transactionId, Sides side, decimal volume, decimal price,
		OrderTypes orderType, TimeInForce? timeInForce, bool isPostOnly,
		OrderPositionEffects? positionEffect, SynthetixOrderCondition condition,
		DateTime? tillDate, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		ArgumentNullException.ThrowIfNull(condition);
		if (!market.IsOpen || market.IsCloseOnly &&
			positionEffect != OrderPositionEffects.CloseOnly &&
			!condition.IsReduceOnly)
			throw new InvalidOperationException(
				$"Synthetix market '{market.Symbol}' is not open for new positions.");
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market or
			OrderTypes.Conditional))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		var isConditional = orderType == OrderTypes.Conditional;
		var isMarket = orderType == OrderTypes.Market ||
			isConditional && condition.IsTriggerMarket;
		ValidateOrderValues(market, volume, price, isMarket);
		if (isPostOnly && (isMarket || isConditional))
			throw new InvalidOperationException(
				"A Synthetix market or conditional order cannot be post-only.");
		condition.IsReduceOnly |=
			positionEffect == OrderPositionEffects.CloseOnly;
		if (isConditional && condition.TriggerPrice is not > 0)
			throw new InvalidOperationException(
				"A Synthetix conditional order requires a trigger price.");
		if (condition.TriggerPrice is decimal trigger)
			ValidatePriceStep(market, trigger, nameof(condition.TriggerPrice));

		string apiOrderType;
		long? expiresAt = null;
		if (isConditional)
			apiOrderType = condition.IsTakeProfit ? "triggerTp" : "triggerSl";
		else if (isMarket)
			apiOrderType = "market";
		else if (timeInForce == TimeInForce.CancelBalance)
			apiOrderType = "limitIoc";
		else if (timeInForce == TimeInForce.MatchOrCancel)
			throw new NotSupportedException(
				"Synthetix does not expose fill-or-kill orders.");
		else if (tillDate is DateTime expiry)
		{
			expiry = expiry.ToUniversalTime();
			var now = DateTime.UtcNow;
			if (expiry < now + TimeSpan.FromSeconds(10) ||
				expiry > now + TimeSpan.FromHours(24))
				throw new ArgumentOutOfRangeException(nameof(tillDate), tillDate,
					"Synthetix GTD expiry must be 10 seconds to 24 hours away.");
			apiOrderType = "limitGtd";
			expiresAt = checked((long)(expiry - DateTime.UnixEpoch).TotalSeconds);
		}
		else
			apiOrderType = "limitGtc";

		var clientOrderId = CreateClientOrderId(transactionId);
		var wire = new SynthetixPlaceOrder
		{
			Symbol = market.Symbol,
			Side = side.ToSynthetixSide(),
			OrderType = apiOrderType,
			Price = isMarket ? string.Empty : price.ToSynthetixDecimal(),
			TriggerPrice = condition.TriggerPrice?.ToSynthetixDecimal() ??
				string.Empty,
			Quantity = volume.ToSynthetixDecimal(),
			IsReduceOnly = condition.IsReduceOnly,
			IsPostOnly = isPostOnly,
			IsTriggerMarket = isConditional && condition.IsTriggerMarket,
			IsClosePosition = isConditional && condition.IsClosePosition,
			ClientOrderId = clientOrderId,
			TriggerPriceType = isConditional
				? condition.TriggerPriceType == SynthetixTriggerPriceTypes.Last
					? "last"
					: "mark"
				: null,
			ExpiresAt = expiresAt,
		};
		var response = await ApiClient.PlaceOrdersAsync(new()
		{
			SubAccountId = SubAccountId,
			Orders = [wire],
		}, Signer, cancellationToken);
		ValidateOperation(response, "place order");
		var status = response.Statuses?[0];
		var result = status?.Result;
		var orderId = result?.Order?.VenueId ?? result?.Id ??
			throw new InvalidDataException(
				"Synthetix place response contains no order ID.");
		TrackOrder(orderId, result.Order?.ClientId ?? clientOrderId,
			transactionId);
		var state = status?.Resting is not null
			? OrderStates.Active
			: status?.Filled is not null || status?.Canceled is not null ||
				status?.Cancelled is not null
				? OrderStates.Done
				: OrderStates.Pending;
		var balance = state == OrderStates.Done ? 0m : volume;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.Symbol.ToSynthetixSecurityId(),
			ServerTime = GetOperationTime(response),
			PortfolioName = PortfolioName,
			DepoName = SubAccountId,
			Side = side,
			OrderVolume = volume,
			Balance = balance,
			OrderPrice = isMarket ? 0m : price,
			OrderType = orderType,
			OrderState = state,
			OrderStringId = orderId,
			TransactionId = transactionId,
			OriginalTransactionId = transactionId,
			TimeInForce = timeInForce ?? TimeInForce.PutInQueue,
			ExpiryDate = expiresAt is long expirySeconds
				? DateTime.UnixEpoch.AddSeconds(expirySeconds)
				: null,
			PositionEffect = condition.IsReduceOnly
				? OrderPositionEffects.CloseOnly
				: null,
			PostOnly = isPostOnly,
			Condition = condition,
		}, cancellationToken);
		SchedulePrivatePoll();
	}

	private async ValueTask PollPrivateAsync(
		CancellationToken cancellationToken)
	{
		long[] portfolios;
		KeyValuePair<long, OrderSubscription>[] orders;
		using (_sync.EnterScope())
		{
			portfolios = [.. _portfolioSubscriptions];
			orders = [.. _orderSubscriptions];
		}
		foreach (var transactionId in portfolios)
			await SendPortfolioSnapshotAsync(transactionId, false,
				cancellationToken);
		foreach (var (transactionId, subscription) in orders)
			await SendOrderSnapshotAsync(subscription, transactionId, false,
				cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshotAsync(long transactionId,
		bool isForce, CancellationToken cancellationToken)
	{
		var accountTask = ApiClient.GetSubAccountAsync(SubAccountId, Signer,
			cancellationToken).AsTask();
		var positionsTask = ApiClient.GetPositionsAsync(SubAccountId, null,
			HistoryLimit, Signer, cancellationToken).AsTask();
		await Task.WhenAll(accountTask, positionsTask);
		var account = await accountTask ?? throw new InvalidDataException(
			"Synthetix returned no subaccount snapshot.");
		var summary = account.MarginSummary ?? new();
		var time = ApiClient.ServerTime;
		UpdateServerTime(time);
		var fingerprint = new AccountFingerprint(
			summary.AccountValue.TryParseSynthetixDecimal() ?? 0m,
			summary.AvailableMargin.TryParseSynthetixDecimal() ?? 0m,
			summary.InitialMargin.TryParseSynthetixDecimal() ?? 0m,
			summary.MaintenanceMargin.TryParseSynthetixDecimal() ?? 0m,
			summary.TotalUnrealizedPnl.TryParseSynthetixDecimal() ?? 0m,
			summary.Debt.TryParseSynthetixDecimal() ?? 0m);
		var changed = isForce;
		using (_sync.EnterScope())
		{
			changed |= !_accountFingerprints.TryGetValue(transactionId,
				out var previous) || previous != fingerprint;
			_accountFingerprints[transactionId] = fingerprint;
		}
		if (changed)
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = PortfolioName,
				SecurityId = SecurityId.Money,
				DepoName = SubAccountId,
				ServerTime = time,
				OriginalTransactionId = transactionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue,
				fingerprint.Current, true)
			.TryAdd(PositionChangeTypes.BlockedValue,
				fingerprint.InitialMargin, true)
			.TryAdd(PositionChangeTypes.VariationMargin,
				fingerprint.MaintenanceMargin, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL,
				fingerprint.UnrealizedPnl, true), cancellationToken);

		foreach (var collateral in account.Collaterals ?? [])
			await SendCollateralAsync(collateral, transactionId, isForce,
				cancellationToken);
		foreach (var position in await positionsTask ?? [])
			await SendPositionAsync(position, transactionId, isForce,
				cancellationToken);
	}

	private async ValueTask SendCollateralAsync(SynthetixCollateral collateral,
		long transactionId, bool isForce,
		CancellationToken cancellationToken)
	{
		if (collateral?.Symbol.IsEmpty() != false)
			return;
		var current = collateral.Quantity.TryParseSynthetixDecimal() ?? 0m;
		var available = collateral.Withdrawable.TryParseSynthetixDecimal() ?? 0m;
		var fingerprint = new CollateralFingerprint(current, available,
			(current - available).Max(0m));
		var key = transactionId.ToString(CultureInfo.InvariantCulture) + ":" +
			collateral.Symbol;
		var changed = isForce;
		using (_sync.EnterScope())
		{
			changed |= !_collateralFingerprints.TryGetValue(key,
				out var previous) || previous != fingerprint;
			_collateralFingerprints[key] = fingerprint;
		}
		if (!changed)
			return;
		var time = collateral.CalculatedAt > 0
			? collateral.CalculatedAt.FromSynthetixMilliseconds()
			: ApiClient.ServerTime;
		UpdateServerTime(time);
		await SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = PortfolioName,
			SecurityId = new()
			{
				SecurityCode = collateral.Symbol,
				BoardCode = BoardCodes.Synthetix,
			},
			DepoName = SubAccountId,
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, fingerprint.Current, true)
		.TryAdd(PositionChangeTypes.BlockedValue, fingerprint.Blocked, true),
			cancellationToken);
	}

	private async ValueTask SendPositionAsync(SynthetixPosition position,
		long transactionId, bool isForce,
		CancellationToken cancellationToken)
	{
		if (position?.Symbol.IsEmpty() != false || position.Side.IsEmpty())
			return;
		var side = position.Side.ToStockSharpSide();
		var fingerprint = new PositionFingerprint(
			position.Quantity.TryParseSynthetixDecimal()?.Abs() ?? 0m,
			position.EntryPrice.TryParseSynthetixDecimal() ?? 0m,
			position.RealizedPnl.TryParseSynthetixDecimal() ?? 0m,
			position.UnrealizedPnl.TryParseSynthetixDecimal() ?? 0m,
			position.LiquidationPrice.TryParseSynthetixDecimal() ?? 0m,
			position.UsedMargin.TryParseSynthetixDecimal() ?? 0m, side);
		var key = transactionId.ToString(CultureInfo.InvariantCulture) + ":" +
			position.Symbol;
		var changed = isForce;
		using (_sync.EnterScope())
		{
			changed |= !_positionFingerprints.TryGetValue(key,
				out var previous) || previous != fingerprint;
			_positionFingerprints[key] = fingerprint;
		}
		if (!changed)
			return;
		var value = position.UpdatedAt > 0
			? position.UpdatedAt
			: position.CreatedAt;
		var time = value > 0
			? value.FromSynthetixMilliseconds()
			: ApiClient.ServerTime;
		UpdateServerTime(time);
		await SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = PortfolioName,
			SecurityId = position.Symbol.ToSynthetixSecurityId(),
			DepoName = SubAccountId,
			ServerTime = time,
			OriginalTransactionId = transactionId,
			Side = side,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, fingerprint.Current, true)
		.TryAdd(PositionChangeTypes.AveragePrice,
			fingerprint.AveragePrice, true)
		.TryAdd(PositionChangeTypes.RealizedPnL,
			fingerprint.RealizedPnl, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL,
			fingerprint.UnrealizedPnl, true)
		.TryAdd(PositionChangeTypes.LiquidationPrice,
			fingerprint.LiquidationPrice, true)
		.TryAdd(PositionChangeTypes.BlockedValue,
			fingerprint.UsedMargin, true), cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(
		OrderSubscription subscription, long transactionId, bool isForce,
		CancellationToken cancellationToken)
	{
		var openTask = ApiClient.GetOpenOrdersAsync(SubAccountId,
			subscription.Symbol, subscription.Limit, Signer,
			cancellationToken).AsTask();
		var historyTask = ApiClient.GetOrderHistoryAsync(SubAccountId,
			subscription.Symbol, NormalizeHistoryFrom(subscription.From,
				subscription.To), subscription.To, subscription.Limit, Signer,
			cancellationToken).AsTask();
		var tradesTask = ApiClient.GetTradesAsync(SubAccountId,
			subscription.Symbol, subscription.OrderId,
			NormalizeHistoryFrom(subscription.From, subscription.To),
			subscription.To, subscription.Limit, Signer,
			cancellationToken).AsTask();
		await Task.WhenAll(openTask, historyTask, tradesTask);
		var orders = (await openTask ?? []).Concat(await historyTask ?? [])
			.Where(static order => order is not null)
			.Where(order => IsOrderMatch(order, subscription))
			.GroupBy(GetVenueOrderId, StringComparer.Ordinal)
			.Select(static group => group.OrderByDescending(GetOrderTime).First())
			.OrderBy(GetOrderTime)
			.Skip(subscription.Skip)
			.Take(subscription.Limit)
			.ToArray();
		foreach (var order in orders)
			await SendOrderAsync(order, transactionId, isForce,
				cancellationToken);
		foreach (var trade in (await tradesTask)?.Trades ?? [])
			await SendAccountTradeAsync(trade, subscription, transactionId,
				cancellationToken);
	}

	private async ValueTask SendOrderAsync(SynthetixOrder order,
		long originalTransactionId, bool isForce,
		CancellationToken cancellationToken)
	{
		if (order?.Symbol.IsEmpty() != false)
			return;
		var message = CreateOrderMessage(order, originalTransactionId);
		var fingerprint = new OrderFingerprint(message.OrderState ??
			OrderStates.Pending, message.OrderPrice, message.OrderVolume ?? 0m,
			message.Balance ?? 0m);
		var key = originalTransactionId.ToString(CultureInfo.InvariantCulture) +
			":" + GetVenueOrderId(order);
		var changed = isForce;
		using (_sync.EnterScope())
		{
			changed |= !_orderFingerprints.TryGetValue(key, out var previous) ||
				previous != fingerprint;
			_orderFingerprints[key] = fingerprint;
		}
		if (changed)
			await SendOutMessageAsync(message, cancellationToken);
	}

	private ExecutionMessage CreateOrderMessage(SynthetixOrder order,
		long originalTransactionId)
	{
		var volume = order.Quantity.TryParseSynthetixDecimal() ?? 0m;
		var filled = order.FilledQuantity.TryParseSynthetixDecimal() ?? 0m;
		var timeValue = GetOrderTime(order);
		var time = timeValue > 0
			? timeValue.FromSynthetixMilliseconds()
			: ApiClient.ServerTime;
		UpdateServerTime(time);
		var clientId = order.Order?.ClientId;
		var venueId = GetVenueOrderId(order);
		TrackOrder(venueId, clientId, 0);
		var condition = new SynthetixOrderCondition
		{
			TriggerPrice = order.TriggerPrice.TryParseSynthetixDecimal(),
			IsTakeProfit = order.Type?.Contains("take",
				StringComparison.OrdinalIgnoreCase) == true,
			IsTriggerMarket = order.Type?.Contains("market",
				StringComparison.OrdinalIgnoreCase) == true,
			IsReduceOnly = order.IsReduceOnly,
			IsClosePosition = order.IsClosePosition,
			TriggerPriceType = order.TriggerPriceType.EqualsIgnoreCase("last")
				? SynthetixTriggerPriceTypes.Last
				: SynthetixTriggerPriceTypes.Mark,
		};
		return new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToSynthetixSecurityId(),
			ServerTime = time,
			PortfolioName = PortfolioName,
			DepoName = SubAccountId,
			Side = order.Side.ToStockSharpSide(),
			OrderVolume = volume,
			Balance = (volume - filled).Max(0m),
			OrderPrice = order.Price.TryParseSynthetixDecimal() ?? 0m,
			OrderType = order.Type.ToStockSharpOrderType(),
			OrderState = order.Status.ToStockSharpOrderState(),
			OrderStringId = venueId,
			TransactionId = GetTransactionId(venueId, clientId),
			OriginalTransactionId = originalTransactionId,
			TimeInForce = order.TimeInForce?.ToUpperInvariant() switch
			{
				"IOC" => TimeInForce.CancelBalance,
				_ => TimeInForce.PutInQueue,
			},
			ExpiryDate = order.ExpiresAt > 0
				? order.ExpiresAt.FromSynthetixMilliseconds()
				: null,
			PositionEffect = order.IsReduceOnly
				? OrderPositionEffects.CloseOnly
				: null,
			PostOnly = order.IsPostOnly,
			Condition = condition,
		};
	}

	private async ValueTask SendAccountTradeAsync(SynthetixAccountTrade trade,
		OrderSubscription subscription, long transactionId,
		CancellationToken cancellationToken)
	{
		if (trade?.Symbol.IsEmpty() != false || trade.TradeId.IsEmpty() ||
			trade.Timestamp <= 0 || !IsTradeMatch(trade, subscription))
			return;
		var seenKey = transactionId.ToString(CultureInfo.InvariantCulture) +
			":" + trade.TradeId;
		using (_sync.EnterScope())
		{
			if (_seenAccountTrades.Count >= 50000)
				_seenAccountTrades.Clear();
			if (!_seenAccountTrades.Add(seenKey))
				return;
		}
		var time = trade.Timestamp.FromSynthetixMilliseconds();
		UpdateServerTime(time);
		var venueId = trade.Order?.VenueId ?? trade.OrderId;
		var clientId = trade.Order?.ClientId;
		TrackOrder(venueId, clientId, 0);
		long? numericTradeId = long.TryParse(trade.TradeId,
			NumberStyles.Integer, CultureInfo.InvariantCulture,
			out var parsedTradeId) ? parsedTradeId : null;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = trade.Symbol.ToSynthetixSecurityId(),
			ServerTime = time,
			PortfolioName = PortfolioName,
			DepoName = SubAccountId,
			Side = trade.Side.ToStockSharpSide(),
			OrderStringId = venueId,
			TradeId = numericTradeId,
			TradeStringId = trade.TradeId,
			TradePrice = trade.Price.ParseSynthetixDecimal("account trade price"),
			TradeVolume = trade.Quantity.ParseSynthetixDecimal(
				"account trade volume"),
			Commission = trade.Fee.TryParseSynthetixDecimal(),
			CommissionCurrency = "USDT",
			TransactionId = GetTransactionId(venueId, clientId),
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}

	private async ValueTask OnPrivateUpdateAsync(
		SynthetixSocketNotification<SynthetixPrivateUpdate> message,
		CancellationToken cancellationToken)
	{
		var update = message?.Data;
		if (update is null)
			return;
		SchedulePrivatePoll();
		var position = ToPrivatePosition(update);
		if (position is not null)
		{
			long[] portfolios;
			using (_sync.EnterScope())
				portfolios = [.. _portfolioSubscriptions];
			foreach (var transactionId in portfolios)
				await SendPositionAsync(position, transactionId, false,
					cancellationToken);
		}
		switch (update.EventType)
		{
			case "orderPlaced":
			case "orderFilled":
			case "orderPartiallyFilled":
			case "orderCancelled":
			case "orderModified":
			case "orderRejected":
				await OnPrivateOrderAsync(update, cancellationToken);
				break;
			case "trade":
			case "liquidation":
				await OnPrivateTradeAsync(update, cancellationToken);
				break;
			case "marginUpdate":
				await OnPrivateMarginAsync(update, cancellationToken);
				break;
		}
	}

	private async ValueTask OnPrivateOrderAsync(SynthetixPrivateUpdate update,
		CancellationToken cancellationToken)
	{
		if (update.Symbol.IsEmpty())
			return;
		var status = update.Status;
		if (status.IsEmpty())
			status = update.EventType switch
			{
				"orderPlaced" or "orderModified" => "placed",
				"orderPartiallyFilled" => "partially filled",
				"orderFilled" => "filled",
				"orderCancelled" => "cancelled",
				"orderRejected" => "rejected",
				_ => "unknown",
			};
		var order = new SynthetixOrder
		{
			Order = update.Order ?? new()
			{
				VenueId = update.OrderId,
				ClientId = update.ClientOrderId,
			},
			OrderId = update.OrderId,
			Symbol = update.Symbol,
			Side = update.Side,
			Type = update.OrderType,
			Status = status,
			Quantity = update.Quantity,
			Price = update.Price,
			FilledQuantity = update.FilledQuantity,
			TriggerPrice = update.TriggerPrice,
			TriggerPriceType = update.TriggerPriceType,
			CreatedTime = update.CreatedAt,
			UpdatedTime = update.UpdatedAt > 0 ? update.UpdatedAt :
				update.CancelledAt > 0 ? update.CancelledAt :
				update.PlacedAt > 0 ? update.PlacedAt : update.Timestamp,
			IsReduceOnly = update.IsReduceOnly,
			IsPostOnly = update.IsPostOnly,
			IsClosePosition = update.IsClosePosition,
			ExpiresAt = update.ExpiresAt,
		};
		KeyValuePair<long, OrderSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _orderSubscriptions];
		foreach (var (transactionId, subscription) in subscriptions)
			if (IsOrderMatch(order, subscription))
				await SendOrderAsync(order, transactionId, false,
					cancellationToken);
	}

	private async ValueTask OnPrivateTradeAsync(SynthetixPrivateUpdate update,
		CancellationToken cancellationToken)
	{
		if (update.Symbol.IsEmpty() || update.TradeId.IsEmpty())
			return;
		var trade = new SynthetixAccountTrade
		{
			TradeId = update.TradeId,
			Order = update.Order ?? new()
			{
				VenueId = update.OrderId,
				ClientId = update.ClientOrderId,
			},
			OrderId = update.OrderId,
			Symbol = update.Symbol,
			Side = update.Side,
			Direction = update.Direction,
			OrderType = update.OrderType,
			Price = update.Price,
			Quantity = update.Quantity,
			RealizedPnl = update.RealizedPnl,
			Fee = update.Fee,
			FeeRate = update.FeeRate,
			MarkPrice = update.MarkPrice,
			EntryPrice = update.EntryPrice,
			Timestamp = update.TradedAt > 0
				? update.TradedAt
				: update.Timestamp,
			IsMaker = update.IsMaker,
			IsReduceOnly = update.IsReduceOnly,
			IsPostOnly = update.IsPostOnly,
		};
		KeyValuePair<long, OrderSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _orderSubscriptions];
		foreach (var (transactionId, subscription) in subscriptions)
			await SendAccountTradeAsync(trade, subscription, transactionId,
				cancellationToken);
	}

	private async ValueTask OnPrivateMarginAsync(SynthetixPrivateUpdate update,
		CancellationToken cancellationToken)
	{
		long[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _portfolioSubscriptions];
		if (subscriptions.Length == 0)
			return;
		var time = update.Timestamp > 0
			? update.Timestamp.FromSynthetixMilliseconds()
			: ServerTime;
		UpdateServerTime(time);
		var fingerprint = new AccountFingerprint(
			update.AccountValue.TryParseSynthetixDecimal() ?? 0m,
			update.AvailableMargin.TryParseSynthetixDecimal() ?? 0m,
			update.InitialMargin.TryParseSynthetixDecimal() ?? 0m,
			update.MaintenanceMargin.TryParseSynthetixDecimal() ?? 0m,
			update.TotalUnrealizedPnl.TryParseSynthetixDecimal() ?? 0m,
			update.Debt.TryParseSynthetixDecimal() ?? 0m);
		foreach (var transactionId in subscriptions)
		{
			var changed = false;
			using (_sync.EnterScope())
			{
				changed = !_accountFingerprints.TryGetValue(transactionId,
					out var previous) || previous != fingerprint;
				_accountFingerprints[transactionId] = fingerprint;
			}
			if (!changed)
				continue;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = PortfolioName,
				SecurityId = SecurityId.Money,
				DepoName = SubAccountId,
				ServerTime = time,
				OriginalTransactionId = transactionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue,
				fingerprint.Current, true)
			.TryAdd(PositionChangeTypes.BlockedValue,
				fingerprint.InitialMargin, true)
			.TryAdd(PositionChangeTypes.VariationMargin,
				fingerprint.MaintenanceMargin, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL,
				fingerprint.UnrealizedPnl, true), cancellationToken);
		}
	}

	private static SynthetixPosition ToPrivatePosition(
		SynthetixPrivateUpdate update)
	{
		var position = update?.Position;
		if (position is null)
			return null;
		var symbol = position.Symbol.IsEmpty()
			? update.Symbol
			: position.Symbol;
		var quantity = position.Quantity.IsEmpty()
			? position.Size
			: position.Quantity;
		if (symbol.IsEmpty() || position.Side.IsEmpty() || quantity.IsEmpty())
			return null;
		return new()
		{
			Symbol = symbol,
			Side = position.Side,
			Quantity = quantity,
			EntryPrice = position.EntryPrice,
			RealizedPnl = update.RealizedPnl,
			UnrealizedPnl = position.UnrealizedPnl.IsEmpty()
				? position.UnrealizedPnlAlternative
				: position.UnrealizedPnl,
			UsedMargin = position.InitialMargin,
			MaintenanceMargin = position.MaintenanceMargin,
			LiquidationPrice = position.LiquidationPrice,
			NetFunding = position.NetFunding,
			Status = "open",
			UpdatedAt = update.TradedAt > 0
				? update.TradedAt
				: update.Timestamp,
		};
	}

	private async ValueTask RemovePortfolioSubscriptionAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
		{
			_portfolioSubscriptions.Remove(transactionId);
			_accountFingerprints.Remove(transactionId);
			var prefix = transactionId.ToString(CultureInfo.InvariantCulture) +
				":";
			foreach (var key in _collateralFingerprints.Keys.Where(key =>
				key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
				_collateralFingerprints.Remove(key);
			foreach (var key in _positionFingerprints.Keys.Where(key =>
				key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
				_positionFingerprints.Remove(key);
		}
		await UpdatePrivateSubscriptionAsync(cancellationToken);
	}

	private async ValueTask RemoveOrderSubscriptionAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
		{
			_orderSubscriptions.Remove(transactionId);
			var prefix = transactionId.ToString(CultureInfo.InvariantCulture) +
				":";
			foreach (var key in _orderFingerprints.Keys.Where(key =>
				key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
				_orderFingerprints.Remove(key);
			foreach (var key in _seenAccountTrades.Where(key =>
				key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
				_seenAccountTrades.Remove(key);
		}
		await UpdatePrivateSubscriptionAsync(cancellationToken);
	}

	private async ValueTask UpdatePrivateSubscriptionAsync(
		CancellationToken cancellationToken)
	{
		bool desired;
		bool changed;
		using (_sync.EnterScope())
		{
			desired = _portfolioSubscriptions.Count > 0 ||
				_orderSubscriptions.Count > 0;
			changed = desired != _isPrivateSocketSubscribed;
			if (changed)
				_isPrivateSocketSubscribed = desired;
		}
		if (!changed)
			return;
		try
		{
			if (desired)
				await SocketClient.SubscribePrivateAsync(cancellationToken);
			else
				await SocketClient.UnsubscribePrivateAsync(cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_isPrivateSocketSubscribed = !desired;
			throw;
		}
	}

	private void ValidatePortfolio(string portfolioName)
	{
		EnsureAccountReady();
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(PortfolioName))
			throw new InvalidOperationException(
				$"Unknown Synthetix portfolio '{portfolioName}'.");
	}

	private async ValueTask<SynthetixOrder> ResolveOpenOrderAsync(
		string stringId, long? numericId, string symbol,
		CancellationToken cancellationToken)
	{
		var requested = ResolveOrderId(stringId, numericId);
		var order = (await ApiClient.GetOpenOrdersAsync(SubAccountId, symbol,
			HistoryLimit, Signer, cancellationToken) ?? []).FirstOrDefault(item =>
			item is not null && (GetVenueOrderId(item).Equals(requested,
				StringComparison.Ordinal) || item.Order?.ClientId.Equals(requested,
					StringComparison.OrdinalIgnoreCase) == true));
		if (order is null)
			throw new InvalidOperationException(
				$"Synthetix open order '{requested}' was not found.");
		return order;
	}

	private ValueTask SendCancelledOrderAsync(string orderId, string symbol,
		long transactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToSynthetixSecurityId(),
			ServerTime = ApiClient.ServerTime,
			PortfolioName = PortfolioName,
			DepoName = SubAccountId,
			OrderStringId = orderId,
			OrderState = OrderStates.Done,
			TransactionId = transactionId,
			OriginalTransactionId = transactionId,
		}, cancellationToken);

	private void TrackOrder(string venueId, string clientId,
		long transactionId)
	{
		using (_sync.EnterScope())
		{
			if (transactionId <= 0 && !clientId.IsEmpty())
				_transactionByClientOrder.TryGetValue(clientId,
					out transactionId);
			if (transactionId > 0)
			{
				if (!clientId.IsEmpty())
					_transactionByClientOrder[clientId] = transactionId;
				if (!venueId.IsEmpty())
					_transactionByVenueOrder[venueId] = transactionId;
			}
		}
	}

	private long GetTransactionId(string venueId, string clientId)
	{
		using (_sync.EnterScope())
		{
			if (!venueId.IsEmpty() && _transactionByVenueOrder.TryGetValue(
				venueId, out var transactionId))
				return transactionId;
			if (!clientId.IsEmpty() && _transactionByClientOrder.TryGetValue(
				clientId, out transactionId))
				return transactionId;
		}
		return 0;
	}

	private void SchedulePrivatePoll()
	{
		using (_sync.EnterScope())
			_nextPrivatePoll = default;
	}

	private static void ValidateOrderValues(SynthetixMarket market,
		decimal volume, decimal price, bool isMarket)
	{
		if (volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(volume));
		var volumeStep = market.OrderSizeIncrement.ParseSynthetixDecimal(
			"volume step");
		if (volumeStep <= 0 || volume % volumeStep != 0)
			throw new ArgumentOutOfRangeException(nameof(volume), volume,
				$"Synthetix order volume must be a multiple of {volumeStep}.");
		var minimum = market.MinimumOrderSize.TryParseSynthetixDecimal();
		var maximum = (isMarket
			? market.MaximumMarketOrderSize
			: market.MaximumLimitOrderSize).TryParseSynthetixDecimal();
		if (minimum is decimal min && volume < min ||
			maximum is decimal max && max > 0 && volume > max)
			throw new ArgumentOutOfRangeException(nameof(volume), volume,
				"Synthetix order volume is outside the market limits.");
		if (!isMarket)
			ValidatePriceStep(market, price, nameof(price));
	}

	private static void ValidatePriceStep(SynthetixMarket market,
		decimal price, string parameterName)
	{
		var step = market.PriceIncrement.ParseSynthetixDecimal("price step");
		if (price <= 0 || step <= 0 || price % step != 0)
			throw new ArgumentOutOfRangeException(parameterName, price,
				$"Synthetix price must be a positive multiple of {step}.");
	}

	private static void ValidateOperation(SynthetixOperationResponse response,
		string operation)
	{
		if (response is null)
			throw new InvalidDataException(
				$"Synthetix {operation} returned no response.");
		if (!response.Error.IsEmpty() ||
			response.Status.EqualsIgnoreCase("rejected"))
			throw new InvalidOperationException("Synthetix " + operation +
				(response.ErrorCode.IsEmpty() ? ": " : " " +
					response.ErrorCode + ": ") +
				(response.Error ?? "request rejected"));
		var errors = (response.Statuses ?? [])
			.Where(static status => !status.Error.IsEmpty())
			.Select(status => (status.ErrorCode.IsEmpty()
				? string.Empty
				: status.ErrorCode + ": ") + status.Error)
			.ToArray();
		if (errors.Length > 0)
			throw new InvalidOperationException("Synthetix " + operation +
				": " + string.Join("; ", errors));
	}

	private DateTime GetOperationTime(SynthetixOperationResponse response)
	{
		var time = response?.Timestamp > 0
			? response.Timestamp.FromSynthetixMilliseconds()
			: ApiClient.ServerTime;
		UpdateServerTime(time);
		return time;
	}

	private OrderSubscription CreateOrderSubscription(OrderStatusMessage message)
		=> new()
		{
			Symbol = message.SecurityId.SecurityCode.IsEmpty()
				? null
				: GetMarket(message.SecurityId).Symbol,
			OrderId = message.OrderStringId.IsEmpty()
				? message.OrderId?.ToString(CultureInfo.InvariantCulture)
				: message.OrderStringId,
			Side = message.Side,
			States = message.States ?? [],
			From = message.From?.ToUniversalTime(),
			To = message.To?.ToUniversalTime(),
			Skip = Math.Max(0, message.Skip ?? 0).To<int>(),
			Limit = (message.Count ?? HistoryLimit).Min(HistoryLimit).Max(1)
				.To<int>(),
		};

	private static bool IsOrderMatch(SynthetixOrder order,
		OrderSubscription filter)
		=> (filter.Symbol.IsEmpty() || order.Symbol.Equals(filter.Symbol,
				StringComparison.Ordinal)) &&
			(filter.OrderId.IsEmpty() || GetVenueOrderId(order).Equals(
				filter.OrderId, StringComparison.Ordinal) || order.Order?.ClientId
					.Equals(filter.OrderId, StringComparison.OrdinalIgnoreCase) == true) &&
			(filter.Side is null || order.Side.ToStockSharpSide() == filter.Side) &&
			(filter.States.Length == 0 ||
				filter.States.Contains(order.Status.ToStockSharpOrderState()));

	private static bool IsTradeMatch(SynthetixAccountTrade trade,
		OrderSubscription filter)
		=> (filter.Symbol.IsEmpty() || filter.Symbol.Equals(trade.Symbol,
				StringComparison.Ordinal)) &&
			(filter.OrderId.IsEmpty() || filter.OrderId.Equals(
				trade.Order?.VenueId ?? trade.OrderId, StringComparison.Ordinal) ||
				filter.OrderId.Equals(trade.Order?.ClientId,
					StringComparison.OrdinalIgnoreCase)) &&
			(filter.Side is null || trade.Side.ToStockSharpSide() == filter.Side) &&
			(filter.From is null || trade.Timestamp > 0 &&
				trade.Timestamp.FromSynthetixMilliseconds() >= filter.From) &&
			(filter.To is null || trade.Timestamp > 0 &&
				trade.Timestamp.FromSynthetixMilliseconds() <= filter.To);

	private static string GetVenueOrderId(SynthetixOrder order)
		=> (order?.Order?.VenueId ?? order?.OrderId).ThrowIfEmpty(
			"Synthetix venue order ID");

	private static long GetOrderTime(SynthetixOrder order)
		=> order.UpdatedTime > 0 ? order.UpdatedTime :
			order.UpdateTime > 0 ? order.UpdateTime : order.CreatedTime;

	private static string ResolveOrderId(string stringId, long? numericId)
	{
		if (!stringId.IsEmpty())
			return stringId.Trim();
		if (numericId is > 0)
			return numericId.Value.ToString(CultureInfo.InvariantCulture);
		throw new InvalidOperationException(
			"Synthetix cancellation requires an order or client order ID.");
	}

	private static string CreateClientOrderId(long transactionId)
	{
		var source = Encoding.UTF8.GetBytes("StockSharp:" +
			transactionId.ToString(CultureInfo.InvariantCulture));
		var hash = SHA256.HashData(source);
		return "0x" + Convert.ToHexString(hash.AsSpan(0, 16))
			.ToLowerInvariant();
	}

	private static DateTime? NormalizeHistoryFrom(DateTime? from, DateTime? to)
	{
		if (from is null)
			return null;
		var end = (to ?? DateTime.UtcNow).ToUniversalTime();
		var start = from.Value.ToUniversalTime();
		return start < end - TimeSpan.FromDays(7)
			? end - TimeSpan.FromDays(7)
			: start;
	}
}
