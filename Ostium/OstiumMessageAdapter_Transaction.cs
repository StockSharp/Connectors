namespace StockSharp.Ostium;

public partial class OstiumMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(regMsg.PortfolioName);
		var market = GetMarket(regMsg.SecurityId);
		var condition = regMsg.Condition as OstiumOrderCondition ?? new()
		{
			Leverage = DefaultLeverage,
		};
		if (condition.IsClosePosition)
		{
			await ClosePositionAsync(regMsg, market, condition,
				cancellationToken);
			return;
		}

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		if (condition.IsStopOrder && orderType != OrderTypes.Limit)
			throw new InvalidOperationException(
				"Ostium stop execution requires a limit order.");

		var collateral = regMsg.Volume;
		ValidateCollateral(collateral);
		ValidateLeverage(market, condition.Leverage, condition.IsDayTrade);
		var current = orderType == OrderTypes.Market
			? await RefreshPriceAsync(market, cancellationToken)
			: GetPrice(market) ?? await RefreshPriceAsync(market,
				cancellationToken);
		var price = orderType == OrderTypes.Market
			? current.Mid
			: regMsg.Price;
		if (orderType == OrderTypes.Market && (!current.IsMarketOpen ||
			condition.IsDayTrade && current.IsDayTradingClosed))
			throw new InvalidOperationException(
				"Ostium market '" + market.Symbol + "' is currently closed.");
		ValidateProtectionPrices(regMsg.Side, price,
			condition.TakeProfitPrice, condition.StopLossPrice);

		await EnsureCollateralAsync(collateral, cancellationToken);
		var openType = orderType == OrderTypes.Market
			? OstiumOpenOrderTypes.Market
			: condition.IsStopOrder
				? OstiumOpenOrderTypes.Stop
				: OstiumOpenOrderTypes.Limit;
		var receipt = await RpcClient.SendAndWaitAsync(
			RpcClient.CreateOpenTradeTransaction(market.PairIndex, regMsg.Side,
				collateral.ToBaseUnits(6, nameof(regMsg.Volume)),
				price.ToBaseUnits(18, nameof(regMsg.Price)),
				condition.Leverage.ToBaseUnits(2,
					nameof(condition.Leverage)),
				condition.TakeProfitPrice.ToBaseUnits(18,
					nameof(condition.TakeProfitPrice)),
				condition.StopLossPrice.ToBaseUnits(18,
					nameof(condition.StopLossPrice)), openType,
				condition.IsDayTrade, orderType == OrderTypes.Market
					? new BigInteger(SlippageBps)
					: BigInteger.Zero),
			TransactionTimeout, cancellationToken);
		var time = await RpcClient.GetReceiptTimeAsync(receipt,
			cancellationToken);
		string orderStringId;
		var state = OrderStates.Pending;
		if (orderType == OrderTypes.Limit)
		{
			var placed = RpcClient.TryGetLimitEvent(receipt, time) ??
				throw new InvalidDataException(
					"Ostium limit-order receipt contains no placement event.");
			if (placed.PairIndex != market.PairIndex)
				throw new InvalidDataException(
					"Ostium limit-order event references a different market.");
			orderStringId = OstiumExtensions.OrderKey(placed.PairIndex,
				placed.PositionIndex);
			state = OrderStates.Active;
		}
		else
		{
			var initiated = RpcClient.TryGetMarketOpenEvent(receipt, time) ??
				throw new InvalidDataException(
					"Ostium market-order receipt contains no initiation event.");
			if (initiated.PairIndex != market.PairIndex)
				throw new InvalidDataException(
					"Ostium market-order event references a different market.");
			orderStringId = "market:" + initiated.OrderId;
			time = initiated.Time;
		}

		TrackOrder(orderStringId, regMsg.TransactionId);
		UpdateServerTime(time);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = regMsg.Side,
			OrderVolume = collateral,
			Balance = collateral,
			OrderPrice = price,
			OrderType = orderType,
			OrderState = state,
			OrderStringId = orderStringId,
			TransactionId = regMsg.TransactionId,
			OriginalTransactionId = regMsg.TransactionId,
			Commission = OstiumRpcClient.GetCommission(receipt),
			CommissionCurrency = "ETH",
			TimeInForce = orderType == OrderTypes.Market
				? TimeInForce.CancelBalance
				: TimeInForce.PutInQueue,
			PositionEffect = OrderPositionEffects.OpenOnly,
			Condition = condition.Clone(),
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(replaceMsg.PortfolioName);
		var orderId = ResolveOrderId(replaceMsg.OldOrderStringId,
			replaceMsg.OldOrderId, replaceMsg.UserOrderId);
		var (pairIndex, positionIndex) = OstiumExtensions.ParseOrderKey(orderId);
		var limits = await ApiClient.GetActiveLimitsAsync(
			RpcClient.WalletAddress, HistoryLimit, cancellationToken);
		var existing = limits.FirstOrDefault(limit => limit is not null &&
			GetPairIndex(limit.Pair) == pairIndex &&
			GetLimitIndex(limit) == positionIndex) ??
			throw new InvalidOperationException(
				"Ostium limit order '" + orderId + "' is no longer active.");
		var market = GetMarket(pairIndex) ?? throw new InvalidDataException(
			"Ostium account references unknown pair " + pairIndex + ".");
		if (!replaceMsg.SecurityId.SecurityCode.IsEmpty() &&
			GetMarket(replaceMsg.SecurityId).PairIndex != pairIndex)
			throw new InvalidOperationException(
				"Replacement security does not match the Ostium order.");
		var currentCollateral = existing.Collateral.ParseScaled(6,
			"order collateral");
		if (replaceMsg.Volume > 0 && replaceMsg.Volume != currentCollateral)
			throw new NotSupportedException(
				"Ostium cannot change limit-order collateral in place.");
		var price = replaceMsg.Price;
		if (price <= 0)
			throw new ArgumentOutOfRangeException(nameof(replaceMsg.Price));
		var supplied = replaceMsg.Condition as OstiumOrderCondition;
		var isStop = existing.LimitType.Equals("STOP",
			StringComparison.OrdinalIgnoreCase);
		if (supplied is not null && supplied.IsStopOrder != isStop)
			throw new NotSupportedException(
				"Ostium cannot change a limit order into a stop order in place.");
		var takeProfit = supplied is null
			? existing.TakeProfitPrice.ParseScaled(18, "take profit")
			: supplied.TakeProfitPrice;
		var stopLoss = supplied is null
			? existing.StopLossPrice.ParseScaled(18, "stop loss")
			: supplied.StopLossPrice;
		var side = existing.IsBuy ? Sides.Buy : Sides.Sell;
		ValidateProtectionPrices(side, price, takeProfit, stopLoss);
		var receipt = await RpcClient.SendAndWaitAsync(
			RpcClient.CreateUpdateLimitTransaction(pairIndex, positionIndex,
				price.ToBaseUnits(18, nameof(replaceMsg.Price)),
				takeProfit.ToBaseUnits(18, nameof(takeProfit)),
				stopLoss.ToBaseUnits(18, nameof(stopLoss))),
			TransactionTimeout, cancellationToken);
		var time = await RpcClient.GetReceiptTimeAsync(receipt,
			cancellationToken);
		TrackOrder(orderId, replaceMsg.TransactionId);
		UpdateServerTime(time);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = side,
			OrderVolume = currentCollateral,
			Balance = currentCollateral,
			OrderPrice = price,
			OrderType = OrderTypes.Limit,
			OrderState = OrderStates.Active,
			OrderStringId = orderId,
			TransactionId = replaceMsg.TransactionId,
			OriginalTransactionId = replaceMsg.TransactionId,
			Commission = OstiumRpcClient.GetCommission(receipt),
			CommissionCurrency = "ETH",
			TimeInForce = TimeInForce.PutInQueue,
			PositionEffect = OrderPositionEffects.OpenOnly,
			Condition = supplied?.Clone() ?? CreateLimitCondition(existing),
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		var orderId = ResolveOrderId(cancelMsg.OrderStringId,
			cancelMsg.OrderId, cancelMsg.UserOrderId);
		var (pairIndex, positionIndex) = OstiumExtensions.ParseOrderKey(orderId);
		var limits = await ApiClient.GetActiveLimitsAsync(
			RpcClient.WalletAddress, HistoryLimit, cancellationToken);
		var limit = limits.FirstOrDefault(item => item is not null &&
			GetPairIndex(item.Pair) == pairIndex &&
			GetLimitIndex(item) == positionIndex) ??
			throw new InvalidOperationException(
				"Ostium limit order '" + orderId + "' is no longer active.");
		var receipt = await RpcClient.SendAndWaitAsync(
			RpcClient.CreateCancelLimitTransaction(pairIndex, positionIndex),
			TransactionTimeout, cancellationToken);
		var time = await RpcClient.GetReceiptTimeAsync(receipt,
			cancellationToken);
		var market = GetMarket(pairIndex) ?? throw new InvalidDataException(
			"Ostium account references unknown pair " + pairIndex + ".");
		using (_sync.EnterScope())
			_knownLimits.Remove(orderId);
		UpdateServerTime(time);
		await SendOutMessageAsync(CreateCancelledLimitMessage(limit, market,
			orderId, time, cancelMsg.TransactionId,
			OstiumRpcClient.GetCommission(receipt)), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"Ostium group cancellation does not close positions.");
		var limits = await ApiClient.GetActiveLimitsAsync(
			RpcClient.WalletAddress, HistoryLimit, cancellationToken);
		var pairIndex = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? (int?)null
			: GetMarket(cancelMsg.SecurityId).PairIndex;
		foreach (var limit in limits.Where(static item => item is not null)
			.Where(item => pairIndex is null ||
				GetPairIndex(item.Pair) == pairIndex)
			.Where(item => cancelMsg.Side is null ||
				(item.IsBuy ? Sides.Buy : Sides.Sell) == cancelMsg.Side)
			.Where(item => cancelMsg.IsStop is null ||
				item.LimitType.Equals("STOP",
					StringComparison.OrdinalIgnoreCase) ==
					cancelMsg.IsStop.Value))
		{
			var itemPairIndex = GetPairIndex(limit.Pair);
			var positionIndex = GetLimitIndex(limit);
			var receipt = await RpcClient.SendAndWaitAsync(
				RpcClient.CreateCancelLimitTransaction(itemPairIndex,
					positionIndex), TransactionTimeout, cancellationToken);
			var time = await RpcClient.GetReceiptTimeAsync(receipt,
				cancellationToken);
			var market = GetMarket(itemPairIndex);
			if (market is null)
				continue;
			var orderId = OstiumExtensions.OrderKey(itemPairIndex,
				positionIndex);
			using (_sync.EnterScope())
				_knownLimits.Remove(orderId);
			await SendOutMessageAsync(CreateCancelledLimitMessage(limit, market,
				orderId, time, cancelMsg.TransactionId,
				OstiumRpcClient.GetCommission(receipt)), cancellationToken);
		}
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
			_portfolioSubscriptionId = 0;
			return;
		}
		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = _portfolioName,
			BoardCode = BoardCodes.Ostium,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		var trades = await ApiClient.GetOpenTradesAsync(RpcClient.WalletAddress,
			HistoryLimit, cancellationToken);
		await SendPortfolioSnapshotAsync(trades, lookupMsg.TransactionId,
			cancellationToken);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId,
				cancellationToken);
			return;
		}
		_portfolioSubscriptionId = lookupMsg.TransactionId;
		_lastAccountRefresh = DateTime.UtcNow;
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
			_orderStatusSubscriptionId = 0;
			return;
		}
		var limitsTask = ApiClient.GetActiveLimitsAsync(RpcClient.WalletAddress,
			HistoryLimit, cancellationToken).AsTask();
		var ordersTask = ApiClient.GetOrdersAsync(RpcClient.WalletAddress,
			HistoryLimit, cancellationToken).AsTask();
		await Task.WhenAll(limitsTask, ordersTask);
		await SendOrderSnapshotAsync(await limitsTask, await ordersTask,
			statusMsg, cancellationToken);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}
		_orderStatusSubscriptionId = statusMsg.TransactionId;
		_lastAccountRefresh = DateTime.UtcNow;
	}

	private async ValueTask ClosePositionAsync(OrderRegisterMessage regMsg,
		OstiumMarket market, OstiumOrderCondition condition,
		CancellationToken cancellationToken)
	{
		if ((regMsg.OrderType ?? OrderTypes.Market) != OrderTypes.Market)
			throw new NotSupportedException(
				"Ostium positions are closed with market execution.");
		if (condition.PositionIndex is not int positionIndex ||
			positionIndex is < 0 or > byte.MaxValue)
			throw new InvalidOperationException(
				"Ostium close-position orders require PositionIndex.");
		var trades = await ApiClient.GetOpenTradesAsync(RpcClient.WalletAddress,
			HistoryLimit, cancellationToken);
		var position = trades.FirstOrDefault(item => item is not null &&
			GetPairIndex(item.Pair) == market.PairIndex &&
			ParseIndex(item.Index, "position index") == positionIndex) ??
			throw new InvalidOperationException(
				"Ostium position '" + market.PairIndex + ":" + positionIndex +
				"' is no longer open.");
		var expectedSide = position.IsBuy ? Sides.Sell : Sides.Buy;
		if (regMsg.Side != expectedSide)
			throw new InvalidOperationException(
				"Ostium close-position side must oppose the open position.");
		var collateral = position.Collateral.ParseScaled(6,
			"position collateral");
		var closePercentage = condition.ClosePercentage ??
			(regMsg.Volume > 0 ? regMsg.Volume / collateral * 100m : 100m);
		closePercentage = decimal.Round(closePercentage, 2,
			MidpointRounding.AwayFromZero);
		if (closePercentage is <= 0 or > 100)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Volume),
				regMsg.Volume, "Close amount exceeds the open position.");
		var current = await RefreshPriceAsync(market, cancellationToken);
		if (!current.IsMarketOpen)
			throw new InvalidOperationException(
				"Ostium market '" + market.Symbol + "' is currently closed.");
		var receipt = await RpcClient.SendAndWaitAsync(
			RpcClient.CreateCloseTradeTransaction(market.PairIndex,
				positionIndex, closePercentage.ToBaseUnits(2,
					nameof(condition.ClosePercentage)),
				current.Mid.ToBaseUnits(18, "current price"),
				new BigInteger(SlippageBps)), TransactionTimeout,
			cancellationToken);
		var time = await RpcClient.GetReceiptTimeAsync(receipt,
			cancellationToken);
		var initiated = RpcClient.TryGetMarketCloseEvent(receipt, time) ??
			throw new InvalidDataException(
				"Ostium close receipt contains no initiation event.");
		if (initiated.PairIndex != market.PairIndex)
			throw new InvalidDataException(
				"Ostium close event references a different market.");
		var orderId = "close:" + initiated.OrderId;
		TrackOrder(orderId, regMsg.TransactionId);
		UpdateServerTime(time);
		var closedCollateral = collateral * closePercentage / 100m;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = regMsg.Side,
			OrderVolume = closedCollateral,
			Balance = closedCollateral,
			OrderPrice = current.Mid,
			OrderType = OrderTypes.Market,
			OrderState = OrderStates.Pending,
			OrderStringId = orderId,
			TransactionId = regMsg.TransactionId,
			OriginalTransactionId = regMsg.TransactionId,
			Commission = OstiumRpcClient.GetCommission(receipt),
			CommissionCurrency = "ETH",
			TimeInForce = TimeInForce.CancelBalance,
			PositionEffect = OrderPositionEffects.CloseOnly,
			Condition = condition.Clone(),
		}, cancellationToken);
	}

	private async ValueTask EnsureCollateralAsync(decimal collateral,
		CancellationToken cancellationToken)
	{
		var required = collateral.ToBaseUnits(6, nameof(collateral));
		var balanceTask = RpcClient.GetUsdcBalanceAsync(cancellationToken).AsTask();
		var allowanceTask = RpcClient.GetUsdcAllowanceAsync(
			cancellationToken).AsTask();
		await Task.WhenAll(balanceTask, allowanceTask);
		var balance = await balanceTask;
		if (balance < required)
			throw new InvalidOperationException(
				"Ostium wallet has insufficient USDC balance. Required " +
				collateral + " USDC, available " +
				balance.FromBaseUnits(6) + " USDC.");
		var allowance = await allowanceTask;
		if (allowance >= required)
			return;
		if (!IsAutoApprove)
			throw new InvalidOperationException(
				"Ostium TradingStorage USDC allowance is insufficient.");
		var approval = BigInteger.Max(required,
			ApprovalAmount.ToBaseUnits(6, nameof(ApprovalAmount)));
		_ = await RpcClient.SendAndWaitAsync(
			RpcClient.CreateApprovalTransaction(approval), TransactionTimeout,
			cancellationToken);
	}

	private async ValueTask<OstiumPrice> RefreshPriceAsync(OstiumMarket market,
		CancellationToken cancellationToken)
	{
		await RefreshPricesAsync(cancellationToken);
		return GetPrice(market) ?? throw new InvalidDataException(
			"Ostium returned no current price for " + market.Symbol + ".");
	}

	private async ValueTask RefreshPricesAsync(
		CancellationToken cancellationToken)
	{
		var response = await ApiClient.GetPricesAsync(cancellationToken);
		foreach (var price in response?.Prices ?? [])
			if (price is not null)
				StorePrice(price);
	}

	private async ValueTask RefreshAccountSubscriptionsAsync(
		CancellationToken cancellationToken)
	{
		Task<OstiumGraphTrade[]> tradesTask = null;
		Task<OstiumGraphLimit[]> limitsTask = null;
		Task<OstiumGraphOrder[]> ordersTask = null;
		if (_portfolioSubscriptionId != 0)
			tradesTask = ApiClient.GetOpenTradesAsync(RpcClient.WalletAddress,
				HistoryLimit, cancellationToken).AsTask();
		if (_orderStatusSubscriptionId != 0)
		{
			limitsTask = ApiClient.GetActiveLimitsAsync(RpcClient.WalletAddress,
				HistoryLimit, cancellationToken).AsTask();
			ordersTask = ApiClient.GetOrdersAsync(RpcClient.WalletAddress,
				HistoryLimit, cancellationToken).AsTask();
		}
		var tasks = new List<Task>(3);
		if (tradesTask is not null)
			tasks.Add(tradesTask);
		if (limitsTask is not null)
			tasks.Add(limitsTask);
		if (ordersTask is not null)
			tasks.Add(ordersTask);
		await Task.WhenAll(tasks);
		if (tradesTask is not null)
			await SendPortfolioSnapshotAsync(await tradesTask,
				_portfolioSubscriptionId, cancellationToken);
		if (limitsTask is not null && ordersTask is not null)
			await SendOrderSnapshotAsync(await limitsTask, await ordersTask,
				new OrderStatusMessage
				{
					TransactionId = _orderStatusSubscriptionId,
					IsSubscribe = true,
					PortfolioName = _portfolioName,
				}, cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshotAsync(
		OstiumGraphTrade[] trades, long transactionId,
		CancellationToken cancellationToken)
	{
		trades ??= [];
		var ethTask = RpcClient.GetEthBalanceAsync(cancellationToken).AsTask();
		var usdcTask = RpcClient.GetUsdcBalanceAsync(cancellationToken).AsTask();
		var pricesTask = RefreshPricesAsync(cancellationToken).AsTask();
		await Task.WhenAll(ethTask, usdcTask, pricesTask);
		var time = DateTime.UtcNow;
		UpdateServerTime(time);
		await SendOutMessageAsync(CreateBalanceMessage("USDC",
			(await usdcTask).FromBaseUnits(6), transactionId, time),
			cancellationToken);
		await SendOutMessageAsync(CreateBalanceMessage("ETH",
			(await ethTask).FromBaseUnits(18), transactionId, time),
			cancellationToken);

		var currentMarkets = new HashSet<int>();
		foreach (var group in trades.Where(static trade => trade is not null)
			.GroupBy(static trade => GetPairIndex(trade.Pair)))
		{
			var market = GetMarket(group.Key);
			if (market is null)
				continue;
			decimal signedQuantity = 0m;
			decimal weightedPrice = 0m;
			decimal absoluteQuantity = 0m;
			decimal unrealized = 0m;
			decimal weightedLeverage = 0m;
			decimal collateralTotal = 0m;
			var currentPrice = GetPrice(market)?.Mid;
			foreach (var position in group)
			{
				var collateral = position.Collateral.ParseScaled(6,
					"position collateral");
				var leverage = position.Leverage.ParseScaled(2,
					"position leverage");
				var openPrice = position.OpenPrice.ParseScaled(18,
					"position open price");
				var quantity = position.TradeNotional.ParseScaled(18,
					"position size");
				if (openPrice <= 0 || quantity <= 0)
					continue;
				var sign = position.IsBuy ? 1m : -1m;
				signedQuantity += sign * quantity;
				weightedPrice += openPrice * quantity;
				absoluteQuantity += quantity;
				weightedLeverage += leverage * collateral;
				collateralTotal += collateral;
				if (currentPrice is decimal price)
					unrealized += sign * quantity * (price - openPrice);
			}
			currentMarkets.Add(group.Key);
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = market.ToStockSharp(),
				ServerTime = time,
				OriginalTransactionId = transactionId,
				Side = signedQuantity > 0
					? Sides.Buy
					: signedQuantity < 0 ? Sides.Sell : null,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, signedQuantity.Abs(), true)
			.TryAdd(PositionChangeTypes.AveragePrice, absoluteQuantity > 0
				? weightedPrice / absoluteQuantity
				: null, true)
			.TryAdd(PositionChangeTypes.CurrentPrice, currentPrice, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, unrealized, true)
			.TryAdd(PositionChangeTypes.Leverage, collateralTotal > 0
				? weightedLeverage / collateralTotal
				: null, true), cancellationToken);
		}

		int[] missing;
		using (_sync.EnterScope())
		{
			missing = [.. _knownPositionMarkets.Where(pairIndex =>
				!currentMarkets.Contains(pairIndex))];
			_knownPositionMarkets.Clear();
			_knownPositionMarkets.UnionWith(currentMarkets);
		}
		foreach (var pairIndex in missing)
		{
			var market = GetMarket(pairIndex);
			if (market is null)
				continue;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = market.ToStockSharp(),
				ServerTime = time,
				OriginalTransactionId = transactionId,
			}.TryAdd(PositionChangeTypes.CurrentValue, 0m, true),
				cancellationToken);
		}
	}

	private async ValueTask SendOrderSnapshotAsync(OstiumGraphLimit[] limits,
		OstiumGraphOrder[] orders, OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		limits ??= [];
		orders ??= [];
		var entries = new List<(ExecutionMessage Order,
			ExecutionMessage Fill)>();
		foreach (var limit in limits.Where(static item => item is not null))
		{
			var market = GetMarket(GetPairIndex(limit.Pair));
			if (market is not null)
				entries.Add((CreateLimitOrderMessage(limit, market,
					statusMsg.TransactionId), null));
		}
		foreach (var order in orders.Where(static item => item is not null))
		{
			var market = GetMarket(GetPairIndex(order.Pair));
			if (market is null)
				continue;
			var orderMessage = CreateOrderMessage(order, market,
				statusMsg.TransactionId);
			var fill = !order.IsPending && !order.IsCancelled &&
				!order.ExecutedAt.IsEmpty()
				? CreateFillMessage(order, market, statusMsg.TransactionId)
				: null;
			entries.Add((orderMessage, fill));
		}

		var selected = entries.Where(entry =>
			IsSecurityMatch(entry.Order.SecurityId, statusMsg) &&
			IsOrderMatch(entry.Order, statusMsg))
			.OrderByDescending(static entry => entry.Order.ServerTime)
			.Skip(Math.Max(0, statusMsg.Skip ?? 0).To<int>())
			.Take((statusMsg.Count ?? int.MaxValue).Min(int.MaxValue).To<int>())
			.OrderBy(static entry => entry.Order.ServerTime)
			.ToArray();
		foreach (var entry in selected)
		{
			UpdateServerTime(entry.Order.ServerTime);
			await SendOutMessageAsync(entry.Order, cancellationToken);
			if (entry.Fill is not null)
				await SendOutMessageAsync(entry.Fill, cancellationToken);
		}

		OstiumGraphLimit[] removed;
		using (_sync.EnterScope())
		{
			var current = limits.ToDictionary(limit =>
				OstiumExtensions.OrderKey(GetPairIndex(limit.Pair),
					GetLimitIndex(limit)), static limit => limit,
				StringComparer.OrdinalIgnoreCase);
			removed = [.. _knownLimits.Where(pair =>
				!current.ContainsKey(pair.Key)).Select(static pair => pair.Value)];
			_knownLimits.Clear();
			foreach (var pair in current)
				_knownLimits.Add(pair.Key, pair.Value);
		}
		foreach (var limit in removed)
		{
			var pairIndex = GetPairIndex(limit.Pair);
			var market = GetMarket(pairIndex);
			if (market is null)
				continue;
			var orderId = OstiumExtensions.OrderKey(pairIndex,
				GetLimitIndex(limit));
			var message = CreateCancelledLimitMessage(limit, market, orderId,
				DateTime.UtcNow, statusMsg.TransactionId, null);
			if (IsSecurityMatch(message.SecurityId, statusMsg) &&
				IsOrderMatch(message, statusMsg))
				await SendOutMessageAsync(message, cancellationToken);
		}
	}

	private ExecutionMessage CreateLimitOrderMessage(OstiumGraphLimit limit,
		OstiumMarket market, long originalTransactionId)
	{
		var index = GetLimitIndex(limit);
		var orderId = OstiumExtensions.OrderKey(market.PairIndex, index);
		var time = ParseTime(limit.InitiatedAt, "limit initiation time");
		var collateral = limit.Collateral.ParseScaled(6, "order collateral");
		return new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = limit.IsBuy ? Sides.Buy : Sides.Sell,
			OrderVolume = collateral,
			Balance = collateral,
			OrderPrice = limit.OpenPrice.ParseScaled(18, "order price"),
			OrderType = OrderTypes.Limit,
			OrderState = OrderStates.Active,
			OrderStringId = orderId,
			TransactionId = GetOriginalTransactionId(orderId),
			OriginalTransactionId = originalTransactionId,
			TimeInForce = TimeInForce.PutInQueue,
			PositionEffect = OrderPositionEffects.OpenOnly,
			Condition = CreateLimitCondition(limit),
		};
	}

	private ExecutionMessage CreateOrderMessage(OstiumGraphOrder order,
		OstiumMarket market, long originalTransactionId)
	{
		var orderId = GetOrderStringId(order);
		var collateral = order.Collateral.ParseScaled(6, "order collateral");
		var price = GetOrderPrice(order);
		var state = order.IsPending ? OrderStates.Pending : OrderStates.Done;
		return new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = ParseTime(order.InitiatedAt, "order initiation time"),
			PortfolioName = _portfolioName,
			Side = order.IsBuy ? Sides.Buy : Sides.Sell,
			OrderVolume = collateral,
			Balance = order.IsPending ? collateral : 0m,
			OrderPrice = price,
			AveragePrice = !order.IsPending && !order.IsCancelled
				? price
				: null,
			OrderType = order.OrderType.Equals("Market",
				StringComparison.OrdinalIgnoreCase)
				? OrderTypes.Market
				: OrderTypes.Limit,
			OrderState = state,
			OrderStringId = orderId,
			TransactionId = GetOriginalTransactionId(orderId),
			OriginalTransactionId = originalTransactionId,
			Commission = !order.IsPending && !order.IsCancelled
				? GetProtocolCommission(order)
				: null,
			CommissionCurrency = !order.IsPending && !order.IsCancelled
				? "USDC"
				: null,
			TimeInForce = order.OrderType.Equals("Market",
				StringComparison.OrdinalIgnoreCase)
				? TimeInForce.CancelBalance
				: TimeInForce.PutInQueue,
			PositionEffect = IsOpenAction(order.OrderAction)
				? OrderPositionEffects.OpenOnly
				: OrderPositionEffects.CloseOnly,
			Condition = new OstiumOrderCondition
			{
				Leverage = ParseLeverage(order.Leverage),
			},
		};
	}

	private ExecutionMessage CreateFillMessage(OstiumGraphOrder order,
		OstiumMarket market, long originalTransactionId)
		=> new()
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = market.ToStockSharp(),
			ServerTime = ParseTime(order.ExecutedAt, "order execution time"),
			PortfolioName = _portfolioName,
			Side = order.IsBuy ? Sides.Buy : Sides.Sell,
			OrderStringId = GetOrderStringId(order),
			TradeStringId = "fill:" + order.Id,
			TradePrice = GetOrderPrice(order),
			TradeVolume = order.TradeNotional.ParseScaled(18, "fill size"),
			Commission = GetProtocolCommission(order),
			CommissionCurrency = "USDC",
			OriginalTransactionId = originalTransactionId,
			PositionEffect = IsOpenAction(order.OrderAction)
				? OrderPositionEffects.OpenOnly
				: OrderPositionEffects.CloseOnly,
		};

	private ExecutionMessage CreateCancelledLimitMessage(
		OstiumGraphLimit limit, OstiumMarket market, string orderId,
		DateTime time, long originalTransactionId, decimal? commission)
		=> new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = time.EnsureOstiumUtc(),
			PortfolioName = _portfolioName,
			Side = limit.IsBuy ? Sides.Buy : Sides.Sell,
			OrderVolume = limit.Collateral.ParseScaled(6, "order collateral"),
			Balance = 0m,
			OrderPrice = limit.OpenPrice.ParseScaled(18, "order price"),
			OrderType = OrderTypes.Limit,
			OrderState = OrderStates.Done,
			OrderStringId = orderId,
			TransactionId = GetOriginalTransactionId(orderId),
			OriginalTransactionId = originalTransactionId,
			Commission = commission,
			CommissionCurrency = commission is null ? null : "ETH",
			TimeInForce = TimeInForce.PutInQueue,
			PositionEffect = OrderPositionEffects.OpenOnly,
			Condition = CreateLimitCondition(limit),
		};

	private static OstiumOrderCondition CreateLimitCondition(
		OstiumGraphLimit limit)
		=> new()
		{
			Leverage = ParseLeverage(limit.Leverage),
			TakeProfitPrice = limit.TakeProfitPrice.ParseScaled(18,
				"take profit"),
			StopLossPrice = limit.StopLossPrice.ParseScaled(18, "stop loss"),
			IsStopOrder = limit.LimitType.Equals("STOP",
				StringComparison.OrdinalIgnoreCase),
		};

	private PositionChangeMessage CreateBalanceMessage(string symbol,
		decimal value, long transactionId, DateTime time)
		=> new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = new()
			{
				SecurityCode = symbol,
				BoardCode = BoardCodes.Ostium,
			},
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}.TryAdd(PositionChangeTypes.CurrentValue, value, true);

	private static void ValidateCollateral(decimal collateral)
	{
		if (collateral is < OstiumExtensions.MinimumCollateral or > 2_000_000m)
			throw new ArgumentOutOfRangeException(nameof(collateral), collateral,
				"Ostium collateral must be between 5 and 2000000 USDC.");
	}

	private static void ValidateLeverage(OstiumMarket market,
		decimal leverage, bool isDayTrade)
	{
		var maximum = !isDayTrade && market.OvernightMaximumLeverage > 0
			? market.OvernightMaximumLeverage
			: market.MaximumLeverage;
		if (leverage < 1 || maximum > 0 && leverage > maximum)
			throw new ArgumentOutOfRangeException(nameof(leverage), leverage,
				"Ostium leverage for " + market.Symbol +
				" must be at least one" + (maximum > 0
					? " and at most " + maximum
					: string.Empty) + ".");
	}

	private static void ValidateProtectionPrices(Sides side, decimal openPrice,
		decimal takeProfit, decimal stopLoss)
	{
		if (openPrice <= 0)
			throw new ArgumentOutOfRangeException(nameof(openPrice));
		if (takeProfit < 0)
			throw new ArgumentOutOfRangeException(nameof(takeProfit));
		if (stopLoss < 0)
			throw new ArgumentOutOfRangeException(nameof(stopLoss));
		if (takeProfit > 0 && (side == Sides.Buy && takeProfit <= openPrice ||
			side == Sides.Sell && takeProfit >= openPrice))
			throw new ArgumentOutOfRangeException(nameof(takeProfit), takeProfit,
				"Take-profit price must be favorable to the trade side.");
		if (stopLoss > 0 && (side == Sides.Buy && stopLoss >= openPrice ||
			side == Sides.Sell && stopLoss <= openPrice))
			throw new ArgumentOutOfRangeException(nameof(stopLoss), stopLoss,
				"Stop-loss price must be adverse to the trade side.");
	}

	private static string ResolveOrderId(string orderStringId, long? orderId,
		string userOrderId)
	{
		if (orderId is not null)
			throw new InvalidOperationException(
				"Ostium orders use pair:index string identifiers.");
		return (orderStringId.IsEmpty() ? userOrderId : orderStringId)
			.ThrowIfEmpty(nameof(orderStringId)).Trim();
	}

	private static int GetPairIndex(OstiumGraphPair pair)
		=> ParseIndex(pair?.Id, "pair index");

	private static int GetLimitIndex(OstiumGraphLimit limit)
	{
		var id = limit?.Id.ThrowIfEmpty("limit id");
		var separator = id.LastIndexOf('_');
		return separator >= 0 && separator < id.Length - 1
			? ParseIndex(id[(separator + 1)..], "limit index")
			: throw new InvalidDataException(
				"Ostium returned an invalid limit identifier '" + id + "'.");
	}

	private static int ParseIndex(string value, string name)
	{
		if (!int.TryParse(value, NumberStyles.None,
			CultureInfo.InvariantCulture, out var result) || result < 0)
			throw new InvalidDataException(
				"Ostium returned an invalid " + name + " '" + value + "'.");
		return result;
	}

	private static DateTime ParseTime(string value, string name)
	{
		if (!long.TryParse(value, NumberStyles.None,
			CultureInfo.InvariantCulture, out var seconds) || seconds <= 0)
			throw new InvalidDataException(
				"Ostium returned an invalid " + name + " '" + value + "'.");
		return seconds.FromUnix().EnsureOstiumUtc();
	}

	private static decimal GetOrderPrice(OstiumGraphOrder order)
	{
		var impact = order.PriceAfterImpact.TryParseScaled(18);
		return impact is > 0
			? impact.Value
			: order.Price.ParseScaled(18, "order price");
	}

	private static decimal GetProtocolCommission(OstiumGraphOrder order)
		=> (order.VaultFee.TryParseScaled(6) ?? 0m) +
			(order.DeveloperFee.TryParseScaled(6) ?? 0m) +
			(order.OracleFee.TryParseScaled(6) ?? 0m) +
			(order.RolloverFee.TryParseScaled(6) ?? 0m) +
			(order.LiquidationFee.TryParseScaled(6) ?? 0m) +
			(order.BuilderFee.TryParseScaled(6) ?? 0m);

	private static decimal ParseLeverage(string value)
	{
		var leverage = value.TryParseScaled(2) ?? 0m;
		return leverage > 0 ? leverage : 1m;
	}

	private static string GetOrderStringId(OstiumGraphOrder order)
		=> (IsOpenAction(order.OrderAction) ? "market:" : "close:") +
			order.Id.ThrowIfEmpty("order id");

	private static bool IsOpenAction(string action)
		=> action?.StartsWith("Open",
			StringComparison.OrdinalIgnoreCase) == true;

	private static bool IsSecurityMatch(SecurityId securityId,
		OrderStatusMessage filter)
	{
		if (!IsSecurityMatch(securityId, filter.SecurityId))
			return false;
		if (filter.SecurityIds.Length > 0 &&
			!filter.SecurityIds.Any(item => IsSecurityMatch(securityId, item)))
			return false;
		return true;
	}

	private static bool IsSecurityMatch(SecurityId securityId,
		SecurityId filter)
		=> (filter.SecurityCode.IsEmpty() ||
			filter.SecurityCode.Equals(securityId.SecurityCode,
				StringComparison.OrdinalIgnoreCase)) &&
			(filter.BoardCode.IsEmpty() ||
			filter.BoardCode.Equals(securityId.BoardCode,
				StringComparison.OrdinalIgnoreCase));

	private static bool IsOrderMatch(ExecutionMessage order,
		OrderStatusMessage filter)
	{
		if (filter.OrderId is not null)
			return false;
		if (!filter.OrderStringId.IsEmpty() &&
			!filter.OrderStringId.Equals(order.OrderStringId,
				StringComparison.OrdinalIgnoreCase))
			return false;
		if (filter.Side is Sides side && order.Side != side)
			return false;
		if (filter.Volume is decimal volume && order.OrderVolume != volume)
			return false;
		if (filter.States.Length > 0 &&
			(order.OrderState is not OrderStates state ||
				!filter.States.Contains(state)))
			return false;
		if (filter.From is DateTime from &&
			order.ServerTime < from.EnsureOstiumUtc())
			return false;
		if (filter.To is DateTime to &&
			order.ServerTime > to.EnsureOstiumUtc())
			return false;
		return true;
	}
}
