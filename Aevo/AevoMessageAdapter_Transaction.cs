namespace StockSharp.Aevo;

public partial class AevoMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		ValidatePortfolio(regMsg.PortfolioName);
		EnsureTradingReady();
		await PlaceOrderAsync(GetMarket(regMsg.SecurityId),
			regMsg.TransactionId, regMsg.Side, regMsg.Volume.Abs(), regMsg.Price,
			regMsg.OrderType ?? OrderTypes.Limit, regMsg.TimeInForce,
			regMsg.PostOnly == true, regMsg.PositionEffect,
			regMsg.Condition as AevoOrderCondition ?? new(), null,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		ValidatePortfolio(replaceMsg.PortfolioName);
		EnsureTradingReady();
		var oldOrderId = ResolveOrderId(replaceMsg.OldOrderId,
			replaceMsg.OldOrderStringId);
		await PlaceOrderAsync(GetMarket(replaceMsg.SecurityId),
			replaceMsg.TransactionId, replaceMsg.Side,
			replaceMsg.Volume.Abs(), replaceMsg.Price,
			replaceMsg.OrderType ?? OrderTypes.Limit, replaceMsg.TimeInForce,
			replaceMsg.PostOnly == true, replaceMsg.PositionEffect,
			replaceMsg.Condition as AevoOrderCondition ?? new(), oldOrderId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		ValidatePortfolio(cancelMsg.PortfolioName);
		EnsureAccountReady();
		var orderId = ResolveOrderId(cancelMsg.OrderId,
			cancelMsg.OrderStringId);
		var response = await RestClient.CancelOrderAsync(orderId,
			cancellationToken);
		var cancelledId = response?.OrderId.NormalizeOrderId() ??
			throw new InvalidDataException(
				"Aevo accepted a cancellation without returning an order ID.");
		if (!cancelledId.Equals(orderId, StringComparison.Ordinal))
			throw new InvalidDataException(
				"Aevo returned a different cancelled order ID.");
		await SendCancelledOrderAsync(cancelledId, cancelMsg.SecurityId,
			cancelMsg.TransactionId, cancellationToken);
		SchedulePrivatePoll();
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		ValidatePortfolio(cancelMsg.PortfolioName);
		EnsureAccountReady();
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"Aevo bulk cancellation does not close positions.");

		var hasSymbol = !cancelMsg.SecurityId.SecurityCode.IsEmpty();
		if (!hasSymbol && cancelMsg.Side is null && cancelMsg.IsStop is null)
		{
			var result = await RestClient.CancelAllAsync(new(), cancellationToken);
			if (result?.IsSuccess != true)
				throw new AevoApiException(
					"Aevo did not confirm the bulk cancellation.");
			SchedulePrivatePoll();
			return;
		}

		var symbol = hasSymbol
			? GetMarket(cancelMsg.SecurityId).InstrumentName
			: null;
		var orders = await RestClient.GetOpenOrdersAsync(cancellationToken);
		foreach (var order in (orders ?? [])
			.Where(static order => order?.OrderId.IsEmpty() == false)
			.Where(order => symbol.IsEmpty() || order.InstrumentName.Equals(
				symbol, StringComparison.Ordinal))
			.Where(order => cancelMsg.Side is null ||
				order.Side.ToStockSharpSide() == cancelMsg.Side)
			.Where(order => cancelMsg.IsStop is null ||
				!order.Stop.IsEmpty() == cancelMsg.IsStop.Value))
			await RestClient.CancelOrderAsync(order.OrderId, cancellationToken);
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
			var unsubscribe = false;
			using (_sync.EnterScope())
				if (_portfolioSubscriptions.Remove(
					lookupMsg.OriginalTransactionId))
					unsubscribe = ReleaseReference(_channelReferences,
						"positions");
			if (unsubscribe)
				await SocketClient.UnsubscribeAsync("positions",
					cancellationToken);
			return;
		}

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = PortfolioName,
			BoardCode = BoardCodes.Aevo,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		var accountTask = RestClient.GetAccountAsync(cancellationToken).AsTask();
		var portfolioTask = RestClient.GetPortfolioAsync(cancellationToken)
			.AsTask();
		await Task.WhenAll(accountTask, portfolioTask);
		await SendPortfolioSnapshotAsync(await accountTask, await portfolioTask,
			lookupMsg.TransactionId, true, cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId,
				cancellationToken);
			return;
		}

		var subscribe = false;
		using (_sync.EnterScope())
		{
			_portfolioSubscriptions.Add(lookupMsg.TransactionId);
			subscribe = AddReference(_channelReferences, "positions");
		}
		try
		{
			if (subscribe)
				await SocketClient.SubscribeAsync("positions", true,
					cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_portfolioSubscriptions.Remove(lookupMsg.TransactionId);
				ReleaseReference(_channelReferences, "positions");
			}
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
		var openTask = RestClient.GetOpenOrdersAsync(cancellationToken).AsTask();
		var historyTask = RestClient.GetOrderHistoryAsync(HistoryLimit,
			cancellationToken).AsTask();
		var tradesTask = RestClient.GetTradeHistoryAsync(HistoryLimit,
			cancellationToken).AsTask();
		await Task.WhenAll(openTask, historyTask, tradesTask);
		await SendOrderSnapshotAsync(await openTask,
			(await historyTask)?.Orders, (await tradesTask)?.Trades,
			subscription, statusMsg.TransactionId, true, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}

		await AddOrderSubscriptionAsync(statusMsg.TransactionId, subscription,
			cancellationToken);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private async ValueTask PlaceOrderAsync(AevoInstrument market,
		long transactionId, Sides side, decimal volume, decimal price,
		OrderTypes orderType, TimeInForce? timeInForce, bool isPostOnly,
		OrderPositionEffects? positionEffect, AevoOrderCondition condition,
		string replacedOrderId, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		ArgumentNullException.ThrowIfNull(condition);
		if (!market.IsActive)
			throw new InvalidOperationException(
				$"Aevo instrument '{market.InstrumentName}' is not active.");
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		if (timeInForce == TimeInForce.MatchOrCancel)
			throw new NotSupportedException(
				"Aevo does not expose fill-or-kill orders through this API.");
		if (volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(volume));
		var volumeStep = market.AmountStep.ParseAevoDecimal("amount step");
		if (volumeStep <= 0 || volume % volumeStep != 0)
			throw new ArgumentOutOfRangeException(nameof(volume), volume,
				$"Aevo order volume must be a multiple of {volumeStep}.");
		_ = volume.ToWire(nameof(volume));

		if (orderType == OrderTypes.Limit)
		{
			var priceStep = market.PriceStep.ParseAevoDecimal("price step");
			if (price <= 0 || priceStep <= 0 || price % priceStep != 0)
				throw new ArgumentOutOfRangeException(nameof(price), price,
					$"Aevo order price must be a positive multiple of {priceStep}.");
			_ = price.ToWire(nameof(price));
		}
		else
		{
			price = 0m;
			timeInForce = TimeInForce.CancelBalance;
		}
		if (isPostOnly && (orderType == OrderTypes.Market ||
			timeInForce == TimeInForce.CancelBalance))
			throw new InvalidOperationException(
				"An Aevo immediate-or-cancel order cannot be post-only.");

		condition.IsReduceOnly |=
			positionEffect == OrderPositionEffects.CloseOnly;
		ValidateNotional(market, volume, orderType == OrderTypes.Limit
			? price
			: market.MarkPrice.TryParseAevoDecimal());
		var request = Signer.CreateOrder(_account, market, side, price, volume,
			orderType, timeInForce, isPostOnly, condition.IsReduceOnly,
			condition.IsMmp);
		var order = replacedOrderId.IsEmpty()
			? await RestClient.CreateOrderAsync(request, cancellationToken)
			: await RestClient.ReplaceOrderAsync(replacedOrderId, request,
				cancellationToken);
		if (order?.OrderId.IsEmpty() != false)
			throw new InvalidDataException(
				"Aevo accepted an order without returning an order ID.");
		_ = order.OrderId.NormalizeOrderId();
		await SendOutMessageAsync(CreateOrderMessage(order, transactionId,
			transactionId), cancellationToken);
		SchedulePrivatePoll();
	}

	private static void ValidateNotional(AevoInstrument market, decimal volume,
		decimal? referencePrice)
	{
		if (referencePrice is not > 0)
			return;
		var notional = volume * referencePrice.Value;
		var minimum = market.MinimumOrderValue.TryParseAevoDecimal();
		var maximum = market.MaximumOrderValue.TryParseAevoDecimal() ??
			market.MaximumNotionalValue.TryParseAevoDecimal();
		if (minimum is decimal min && notional < min)
			throw new ArgumentOutOfRangeException(nameof(volume), volume,
				$"Aevo order notional must be at least {min}.");
		if (maximum is decimal max && max > 0 && notional > max)
			throw new ArgumentOutOfRangeException(nameof(volume), volume,
				$"Aevo order notional cannot exceed {max}.");
	}

	private async ValueTask AddOrderSubscriptionAsync(long transactionId,
		OrderSubscription subscription, CancellationToken cancellationToken)
	{
		var subscribeOrders = false;
		var subscribeFills = false;
		using (_sync.EnterScope())
		{
			_orderSubscriptions.Add(transactionId, subscription);
			subscribeOrders = AddReference(_channelReferences, "orders");
			subscribeFills = AddReference(_channelReferences, "fills");
		}
		var ordersSubscribed = false;
		try
		{
			if (subscribeOrders)
			{
				await SocketClient.SubscribeAsync("orders", true,
					cancellationToken);
				ordersSubscribed = true;
			}
			if (subscribeFills)
				await SocketClient.SubscribeAsync("fills", true,
					cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_orderSubscriptions.Remove(transactionId);
				ReleaseReference(_channelReferences, "orders");
				ReleaseReference(_channelReferences, "fills");
			}
			if (ordersSubscribed)
				await SocketClient.UnsubscribeAsync("orders", cancellationToken);
			throw;
		}
	}

	private async ValueTask RemoveOrderSubscriptionAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		var unsubscribeOrders = false;
		var unsubscribeFills = false;
		using (_sync.EnterScope())
			if (_orderSubscriptions.Remove(transactionId))
			{
				unsubscribeOrders = ReleaseReference(_channelReferences,
					"orders");
				unsubscribeFills = ReleaseReference(_channelReferences,
					"fills");
			}
		if (unsubscribeOrders)
			await SocketClient.UnsubscribeAsync("orders", cancellationToken);
		if (unsubscribeFills)
			await SocketClient.UnsubscribeAsync("fills", cancellationToken);
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
		if (portfolios.Length == 0 && orders.Length == 0)
			return;

		Task<AevoAccount> accountTask = null;
		Task<AevoPortfolio> portfolioTask = null;
		Task<AevoOrder[]> openTask = null;
		Task<AevoOrderHistoryResponse> historyTask = null;
		Task<AevoPrivateTradesResponse> tradesTask = null;
		var tasks = new List<Task>(5);
		if (portfolios.Length > 0)
		{
			accountTask = RestClient.GetAccountAsync(cancellationToken).AsTask();
			portfolioTask = RestClient.GetPortfolioAsync(cancellationToken)
				.AsTask();
			tasks.Add(accountTask);
			tasks.Add(portfolioTask);
		}
		if (orders.Length > 0)
		{
			openTask = RestClient.GetOpenOrdersAsync(cancellationToken).AsTask();
			historyTask = RestClient.GetOrderHistoryAsync(HistoryLimit,
				cancellationToken).AsTask();
			tradesTask = RestClient.GetTradeHistoryAsync(HistoryLimit,
				cancellationToken).AsTask();
			tasks.Add(openTask);
			tasks.Add(historyTask);
			tasks.Add(tradesTask);
		}
		await Task.WhenAll(tasks);
		foreach (var transactionId in portfolios)
			await SendPortfolioSnapshotAsync(await accountTask,
				await portfolioTask, transactionId, false, cancellationToken);
		foreach (var (transactionId, subscription) in orders)
			await SendOrderSnapshotAsync(await openTask,
				(await historyTask)?.Orders, (await tradesTask)?.Trades,
				subscription, transactionId, false, cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshotAsync(AevoAccount account,
		AevoPortfolio portfolio, long transactionId, bool isForce,
		CancellationToken cancellationToken)
	{
		if (account is null)
			throw new InvalidDataException("Aevo returned no account snapshot.");
		var fingerprint = new AccountFingerprint(
			account.Equity.TryParseAevoDecimal() ?? 0m,
			account.AvailableBalance.TryParseAevoDecimal() ?? 0m,
			account.AvailableMargin.TryParseAevoDecimal() ?? 0m,
			account.Balance.TryParseAevoDecimal() ?? 0m,
			account.InitialMargin.TryParseAevoDecimal() ?? 0m,
			account.MaintenanceMargin.TryParseAevoDecimal() ?? 0m,
			portfolio?.Pnl.TryParseAevoDecimal() ?? 0m,
			portfolio?.RealizedPnl.TryParseAevoDecimal() ?? 0m,
			portfolio?.UserMargin?.Used.TryParseAevoDecimal() ?? 0m);
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
				DepoName = _account,
				ServerTime = ServerTime,
				OriginalTransactionId = transactionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, fingerprint.Equity, true)
			.TryAdd(PositionChangeTypes.BlockedValue,
				fingerprint.InitialMargin.Max(fingerprint.UsedMargin), true)
			.TryAdd(PositionChangeTypes.VariationMargin,
				fingerprint.MaintenanceMargin, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL,
				fingerprint.Pnl - fingerprint.RealizedPnl, true)
			.TryAdd(PositionChangeTypes.RealizedPnL,
				fingerprint.RealizedPnl, true), cancellationToken);

		foreach (var collateral in account.Collaterals ?? [])
			await SendCollateralAsync(collateral, transactionId, isForce,
				cancellationToken);
		foreach (var position in account.Positions ?? [])
			await SendPositionAsync(position, transactionId, ServerTime, isForce,
				cancellationToken);
	}

	private async ValueTask SendCollateralAsync(AevoCollateral collateral,
		long transactionId, bool isForce,
		CancellationToken cancellationToken)
	{
		if (collateral?.Asset.IsEmpty() != false)
			return;
		var current = collateral.Balance.TryParseAevoDecimal() ?? 0m;
		var available = collateral.AvailableBalance.TryParseAevoDecimal() ??
			current;
		var fingerprint = new CollateralFingerprint(current,
			(current - available).Max(0m),
			collateral.UnrealizedPnl.TryParseAevoDecimal() ?? 0m);
		var key = transactionId.ToString(CultureInfo.InvariantCulture) + ":" +
			collateral.Asset;
		var changed = isForce;
		using (_sync.EnterScope())
		{
			changed |= !_collateralFingerprints.TryGetValue(key,
				out var previous) || previous != fingerprint;
			_collateralFingerprints[key] = fingerprint;
		}
		if (!changed)
			return;
		await SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = PortfolioName,
			SecurityId = new()
			{
				SecurityCode = collateral.Asset.ToUpperInvariant(),
				BoardCode = BoardCodes.Aevo,
			},
			DepoName = _account,
			ServerTime = ServerTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, fingerprint.Current, true)
		.TryAdd(PositionChangeTypes.BlockedValue, fingerprint.Blocked, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL,
			fingerprint.UnrealizedPnl, true), cancellationToken);
	}

	private async ValueTask SendPositionAsync(AevoPosition position,
		long transactionId, DateTime serverTime, bool isForce,
		CancellationToken cancellationToken)
	{
		if (position?.InstrumentName.IsEmpty() != false)
			return;
		var amount = position.Amount.ParseAevoDecimal("position amount");
		var side = position.Side.IsEmpty()
			? amount < 0 ? Sides.Sell : Sides.Buy
			: position.Side.ToStockSharpSide();
		var fingerprint = new PositionFingerprint(amount.Abs(),
			position.AverageEntryPrice.TryParseAevoDecimal() ?? 0m,
			position.UnrealizedPnl.TryParseAevoDecimal() ?? 0m,
			position.LiquidationPrice.TryParseAevoDecimal() ?? 0m,
			position.Leverage.TryParseAevoDecimal() ?? 0m, side);
		var key = transactionId.ToString(CultureInfo.InvariantCulture) + ":" +
			position.InstrumentName;
		var changed = isForce;
		using (_sync.EnterScope())
		{
			changed |= !_positionFingerprints.TryGetValue(key,
				out var previous) || previous != fingerprint;
			_positionFingerprints[key] = fingerprint;
		}
		if (!changed)
			return;
		await SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = PortfolioName,
			SecurityId = position.InstrumentName.ToStockSharp(),
			DepoName = _account,
			ServerTime = serverTime,
			OriginalTransactionId = transactionId,
			Side = amount == 0 ? null : side,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, fingerprint.Current, true)
		.TryAdd(PositionChangeTypes.AveragePrice,
			fingerprint.AveragePrice, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL,
			fingerprint.UnrealizedPnl, true)
		.TryAdd(PositionChangeTypes.LiquidationPrice,
			fingerprint.LiquidationPrice, true)
		.TryAdd(PositionChangeTypes.Leverage, fingerprint.Leverage, true),
			cancellationToken);
	}

	private async ValueTask OnPositionsAsync(AevoPositionsData data,
		CancellationToken cancellationToken)
	{
		if (data is null)
			return;
		var time = data.Timestamp.IsEmpty()
			? ServerTime
			: data.Timestamp.FromAevoNanoseconds();
		UpdateServerTime(time);
		long[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _portfolioSubscriptions];
		foreach (var transactionId in subscriptions)
			foreach (var position in data.Positions ?? [])
				await SendPositionAsync(position, transactionId, time, false,
					cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(AevoOrder[] openOrders,
		AevoOrder[] history, AevoTrade[] trades,
		OrderSubscription subscription, long transactionId, bool isForce,
		CancellationToken cancellationToken)
	{
		var orders = (openOrders ?? []).Concat(history ?? [])
			.Where(order => IsOrderMatch(order, subscription))
			.GroupBy(static order => order.OrderId,
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.OrderByDescending(GetOrderTime).First())
			.OrderBy(GetOrderTime)
			.Skip(subscription.Skip)
			.Take(subscription.Limit)
			.ToArray();
		foreach (var order in orders)
			await SendOrderAsync(order, transactionId, isForce,
				cancellationToken);

		foreach (var trade in (trades ?? [])
			.Where(trade => IsTradeMatch(trade, subscription))
			.OrderBy(GetTradeTime)
			.Skip(subscription.Skip)
			.Take(subscription.Limit))
			await SendAccountTradeAsync(trade, subscription, transactionId,
				cancellationToken);
	}

	private async ValueTask OnOrdersAsync(AevoOrdersData data,
		CancellationToken cancellationToken)
	{
		if (data is null)
			return;
		if (!data.Timestamp.IsEmpty())
			UpdateServerTime(data.Timestamp.FromAevoNanoseconds());
		KeyValuePair<long, OrderSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _orderSubscriptions];
		foreach (var order in data.Orders ?? [])
			foreach (var (transactionId, subscription) in subscriptions)
				if (IsOrderMatch(order, subscription))
					await SendOrderAsync(order, transactionId, false,
						cancellationToken);
	}

	private async ValueTask OnFillAsync(AevoFillData data,
		CancellationToken cancellationToken)
	{
		if (data?.Fill is not { } fill)
			return;
		if (!data.Timestamp.IsEmpty())
			UpdateServerTime(data.Timestamp.FromAevoNanoseconds());
		KeyValuePair<long, OrderSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _orderSubscriptions];
		foreach (var (transactionId, subscription) in subscriptions)
			if (IsTradeMatch(fill, subscription))
				await SendAccountTradeAsync(fill, subscription, transactionId,
					cancellationToken);
	}

	private async ValueTask SendOrderAsync(AevoOrder order,
		long transactionId, bool isForce,
		CancellationToken cancellationToken)
	{
		var message = CreateOrderMessage(order, transactionId, 0);
		var fingerprint = new OrderFingerprint(message.OrderState ??
			OrderStates.Pending, message.OrderPrice, message.OrderVolume ?? 0m,
			message.Balance ?? 0m);
		var key = OrderKey(transactionId, message.OrderStringId);
		var changed = isForce;
		using (_sync.EnterScope())
		{
			changed |= !_orderFingerprints.TryGetValue(key,
				out var previous) || previous != fingerprint;
			_orderFingerprints[key] = fingerprint;
		}
		if (changed)
			await SendOutMessageAsync(message, cancellationToken);
	}

	private ExecutionMessage CreateOrderMessage(AevoOrder order,
		long originalTransactionId, long transactionId)
	{
		ArgumentNullException.ThrowIfNull(order);
		var volume = order.Amount.ParseAevoDecimal("order amount");
		var filled = order.Filled.TryParseAevoDecimal() ?? 0m;
		var time = GetOrderTime(order);
		if (time == DateTime.MinValue)
			time = ServerTime;
		else
			UpdateServerTime(time);
		var condition = new AevoOrderCondition
		{
			IsReduceOnly = order.IsReduceOnly,
			IsMmp = order.IsMmp,
		};
		return new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.InstrumentName.ToStockSharp(),
			ServerTime = time,
			PortfolioName = PortfolioName,
			DepoName = _account,
			Side = order.Side.ToStockSharpSide(),
			OrderVolume = volume,
			Balance = (volume - filled).Max(0m),
			OrderPrice = order.Price.TryParseAevoDecimal() ?? 0m,
			AveragePrice = order.AveragePrice.TryParseAevoDecimal(),
			OrderType = ToOrderType(order.OrderType),
			OrderState = order.Status.ToStockSharpState(),
			OrderStringId = order.OrderId.NormalizeOrderId(),
			TransactionId = transactionId,
			OriginalTransactionId = originalTransactionId,
			TimeInForce = ToTimeInForce(order.TimeInForce, order.OrderType),
			PostOnly = order.IsPostOnly,
			PositionEffect = order.IsReduceOnly
				? OrderPositionEffects.CloseOnly
				: null,
			Condition = condition,
		};
	}

	private async ValueTask SendAccountTradeAsync(AevoTrade trade,
		OrderSubscription subscription, long transactionId,
		CancellationToken cancellationToken)
	{
		if (!IsTradeMatch(trade, subscription))
			return;
		var seenKey = transactionId.ToString(CultureInfo.InvariantCulture) +
			":" + trade.TradeId;
		using (_sync.EnterScope())
			if (!_seenAccountTrades.Add(seenKey))
				return;
		var time = GetTradeTime(trade);
		if (time == DateTime.MinValue)
			time = ServerTime;
		else
			UpdateServerTime(time);
		var volumeText = trade.Filled.IsEmpty()
			? trade.Amount
			: trade.Filled;
		string commissionCurrency;
		using (_sync.EnterScope())
			commissionCurrency = _markets.TryGetValue(trade.InstrumentName,
				out var market) && !market.QuoteAsset.IsEmpty()
					? market.QuoteAsset
					: "USDC";
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = trade.InstrumentName.ToStockSharp(),
			ServerTime = time,
			PortfolioName = PortfolioName,
			DepoName = _account,
			Side = trade.Side.ToStockSharpSide(),
			OrderStringId = trade.OrderId.NormalizeOrderId(),
			TradeStringId = trade.TradeId.ThrowIfEmpty(nameof(trade.TradeId)),
			TradePrice = trade.Price.ParseAevoDecimal("account trade price"),
			TradeVolume = volumeText.ParseAevoDecimal("account trade amount"),
			Commission = trade.Fees.TryParseAevoDecimal(),
			CommissionCurrency = commissionCurrency,
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}

	private ValueTask SendCancelledOrderAsync(string orderId,
		SecurityId securityId, long transactionId,
		CancellationToken cancellationToken)
	{
		if (securityId.BoardCode.IsEmpty() &&
			!securityId.SecurityCode.IsEmpty())
			securityId = securityId.SecurityCode.ToStockSharp();
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = securityId,
			ServerTime = ServerTime,
			PortfolioName = PortfolioName,
			DepoName = _account,
			OrderStringId = orderId,
			OrderState = OrderStates.Done,
			TransactionId = transactionId,
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}

	private OrderSubscription CreateOrderSubscription(
		OrderStatusMessage message)
	{
		if (!message.UserOrderId.IsEmpty())
			throw new NotSupportedException(
				"Aevo orders do not expose client order IDs.");
		if (message.OrderId is > 0 && message.OrderStringId.IsEmpty())
			throw new NotSupportedException(
				"Aevo order lookup requires the exchange hash in OrderStringId.");
		var from = message.From?.ToUniversalTime();
		var to = message.To?.ToUniversalTime();
		if (from is DateTime start && to is DateTime end && start > end)
			throw new ArgumentOutOfRangeException(nameof(message),
				"Aevo order history start time cannot be later than end time.");
		return new()
		{
			Symbol = message.SecurityId.SecurityCode.IsEmpty()
				? null
				: GetMarket(message.SecurityId).InstrumentName,
			OrderId = message.OrderStringId.IsEmpty()
				? null
				: message.OrderStringId.NormalizeOrderId(),
			Side = message.Side,
			States = message.States ?? [],
			From = from,
			To = to,
			Skip = Math.Max(0, message.Skip ?? 0).To<int>(),
			Limit = (message.Count ?? HistoryLimit).Min(HistoryLimit).Max(1)
				.To<int>(),
		};
	}

	private static bool IsOrderMatch(AevoOrder order,
		OrderSubscription subscription)
	{
		if (order?.OrderId.IsEmpty() != false ||
			order.InstrumentName.IsEmpty())
			return false;
		if (!subscription.Symbol.IsEmpty() &&
			!order.InstrumentName.Equals(subscription.Symbol,
				StringComparison.Ordinal))
			return false;
		if (!subscription.OrderId.IsEmpty() &&
			!order.OrderId.Equals(subscription.OrderId,
				StringComparison.OrdinalIgnoreCase))
			return false;
		if (subscription.Side is Sides side &&
			order.Side.ToStockSharpSide() != side)
			return false;
		var state = order.Status.ToStockSharpState();
		if (subscription.States.Length > 0 &&
			!subscription.States.Contains(state))
			return false;
		return IsInRange(GetOrderTime(order), subscription.From,
			subscription.To);
	}

	private static bool IsTradeMatch(AevoTrade trade,
		OrderSubscription subscription)
	{
		if (trade?.TradeId.IsEmpty() != false || trade.OrderId.IsEmpty() ||
			trade.InstrumentName.IsEmpty() || trade.Side.IsEmpty() ||
			trade.Price.IsEmpty() ||
			(trade.Amount.IsEmpty() && trade.Filled.IsEmpty()))
			return false;
		if (!trade.TradeType.IsEmpty() &&
			!trade.TradeType.EqualsIgnoreCase("trade"))
			return false;
		if (!subscription.Symbol.IsEmpty() &&
			!trade.InstrumentName.Equals(subscription.Symbol,
				StringComparison.Ordinal))
			return false;
		if (!subscription.OrderId.IsEmpty() &&
			!trade.OrderId.Equals(subscription.OrderId,
				StringComparison.OrdinalIgnoreCase))
			return false;
		if (subscription.Side is Sides side &&
			trade.Side.ToStockSharpSide() != side)
			return false;
		var status = trade.OrderStatus.IsEmpty()
			? trade.TradeStatus
			: trade.OrderStatus;
		if (subscription.States.Length > 0 &&
			!subscription.States.Contains(status.ToStockSharpState()))
			return false;
		return IsInRange(GetTradeTime(trade), subscription.From,
			subscription.To);
	}

	private static bool IsInRange(DateTime time, DateTime? from, DateTime? to)
		=> (from is null || time >= from.Value) &&
			(to is null || time <= to.Value);

	private static DateTime GetOrderTime(AevoOrder order)
	{
		var value = order?.Timestamp.IsEmpty() == false
			? order.Timestamp
			: order?.CreatedTimestamp;
		return value.IsEmpty()
			? DateTime.MinValue
			: value.FromAevoNanoseconds();
	}

	private static DateTime GetTradeTime(AevoTrade trade)
		=> trade?.CreatedTimestamp.IsEmpty() == false
			? trade.CreatedTimestamp.FromAevoNanoseconds()
			: DateTime.MinValue;

	private static OrderTypes ToOrderType(string value)
		=> value?.ToLowerInvariant() switch
		{
			"market" => OrderTypes.Market,
			"limit" or null or "" => OrderTypes.Limit,
			_ => throw new InvalidDataException(
				$"Aevo returned unknown order type '{value}'."),
		};

	private static TimeInForce ToTimeInForce(string value, string orderType)
		=> value?.ToUpperInvariant() switch
		{
			"IOC" => TimeInForce.CancelBalance,
			"GTC" => TimeInForce.PutInQueue,
			null or "" when orderType.EqualsIgnoreCase("market") =>
				TimeInForce.CancelBalance,
			null or "" => TimeInForce.PutInQueue,
			_ => throw new InvalidDataException(
				$"Aevo returned unknown time-in-force '{value}'."),
		};

	private void ValidatePortfolio(string portfolioName)
	{
		EnsureAccountReady();
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(PortfolioName))
			throw new InvalidOperationException(
				$"Unknown Aevo portfolio '{portfolioName}'.");
	}

	private static string ResolveOrderId(long? orderId, string stringId)
	{
		if (!stringId.IsEmpty())
			return stringId.NormalizeOrderId();
		if (orderId is > 0)
			throw new NotSupportedException(
				"Aevo order operations require the exchange hash in OrderStringId.");
		throw new InvalidOperationException(
			"Aevo order operations require an exchange order ID.");
	}

	private void SchedulePrivatePoll()
	{
		using (_sync.EnterScope())
			_nextPrivatePoll = default;
	}

	private static string OrderKey(long transactionId, string orderId)
		=> transactionId.ToString(CultureInfo.InvariantCulture) + ":" +
			orderId;
}
