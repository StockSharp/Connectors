namespace StockSharp.Avantis;

public partial class AvantisMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(regMsg.PortfolioName);
		var market = GetMarket(regMsg.SecurityId);
		var condition = regMsg.Condition as AvantisOrderCondition ?? new()
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
		if (condition.IsZeroFee && orderType != OrderTypes.Market)
			throw new InvalidOperationException(
				"Avantis zero-fee execution supports market orders only.");
		if (condition.IsStopLimit && orderType != OrderTypes.Limit)
			throw new InvalidOperationException(
				"Avantis stop-limit execution requires a limit order.");

		var collateral = regMsg.Volume;
		if (collateral <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Volume),
				collateral, "Avantis order volume is USDC collateral and must be " +
				"positive.");
		ValidateLeverage(market, condition.Leverage, condition.IsZeroFee,
			collateral);
		var current = GetPrice(market.PairIndex) ??
			await GetPriceSnapshotAsync(market, cancellationToken);
		var price = orderType == OrderTypes.Market
			? current.Price
			: regMsg.Price;
		ValidateProtectionPrices(regMsg.Side, price,
			condition.TakeProfitPrice, condition.StopLossPrice);
		if (orderType == OrderTypes.Market && !market.IsOpen)
			throw new InvalidOperationException(
				"Avantis market '" + market.Symbol + "' is currently closed.");

		await EnsureCollateralAsync(collateral, cancellationToken);
		var openType = orderType == OrderTypes.Market
			? condition.IsZeroFee
				? AvantisOpenOrderTypes.MarketZeroFee
				: AvantisOpenOrderTypes.Market
			: condition.IsStopLimit
				? AvantisOpenOrderTypes.StopLimit
				: AvantisOpenOrderTypes.Limit;
		var executionFee = GetExecutionFee(condition);
		var transaction = RpcClient.CreateOpenTradeTransaction(
			market.PairIndex, regMsg.Side,
			collateral.ToBaseUnits(6, nameof(regMsg.Volume)),
			price.ToBaseUnits(10, nameof(regMsg.Price)),
			condition.Leverage.ToBaseUnits(10,
				nameof(condition.Leverage)),
			condition.TakeProfitPrice.ToBaseUnits(10,
				nameof(condition.TakeProfitPrice)),
			condition.StopLossPrice.ToBaseUnits(10,
				nameof(condition.StopLossPrice)), openType,
			Slippage.ToBaseUnits(10, nameof(Slippage)),
			executionFee.ToBaseUnits(18, nameof(ExecutionFee)));
		var receipt = await RpcClient.SendAndWaitAsync(transaction,
			TransactionTimeout, cancellationToken);
		var time = await RpcClient.GetReceiptTimeAsync(receipt,
			cancellationToken);
		var orderStringId = receipt.TransactionHash.NormalizeHash();
		if (orderType == OrderTypes.Limit)
		{
			var placed = RpcClient.TryGetLimitEvent(receipt, time) ??
				throw new InvalidDataException(
					"Avantis limit-order receipt contains no placement event.");
			if (placed.PairIndex != market.PairIndex)
				throw new InvalidDataException(
					"Avantis limit-order event references a different market.");
			orderStringId = AvantisExtensions.OrderKey(placed.PairIndex,
				placed.TradeIndex);
		}
		else if (RpcClient.TryGetMarketEvent(receipt, time) is { } initiated)
		{
			if (initiated.PairIndex != market.PairIndex)
				throw new InvalidDataException(
					"Avantis market-order event references a different market.");
			time = initiated.Time;
			orderStringId = "market:" + initiated.OrderId;
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
			OrderState = orderType == OrderTypes.Limit
				? OrderStates.Active
				: OrderStates.Pending,
			OrderStringId = orderStringId,
			TransactionId = regMsg.TransactionId,
			OriginalTransactionId = regMsg.TransactionId,
			Commission = AvantisRpcClient.GetCommission(receipt),
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
		var (pairIndex, tradeIndex) = AvantisExtensions.ParseOrderKey(orderId);
		var data = await ApiClient.GetUserDataAsync(RpcClient.WalletAddress,
			cancellationToken) ?? throw new InvalidDataException(
				"Avantis returned no account data.");
		var existing = (data.LimitOrders ?? []).FirstOrDefault(order =>
			order is not null && order.PairIndex == pairIndex &&
			order.Index == tradeIndex) ?? throw new InvalidOperationException(
				"Avantis limit order '" + orderId + "' is no longer active.");
		var market = GetMarket(pairIndex) ?? throw new InvalidDataException(
			"Avantis account references unknown pair " + pairIndex + ".");
		if (!replaceMsg.SecurityId.SecurityCode.IsEmpty() &&
			GetMarket(replaceMsg.SecurityId).PairIndex != pairIndex)
			throw new InvalidOperationException(
				"Replacement security does not match the Avantis order.");
		var currentCollateral = existing.Collateral.ParseScaled(6,
			"order collateral");
		if (replaceMsg.Volume > 0 && replaceMsg.Volume != currentCollateral)
			throw new NotSupportedException(
				"Avantis cannot change limit-order collateral in place.");
		var price = replaceMsg.Price;
		if (price <= 0)
			throw new ArgumentOutOfRangeException(nameof(replaceMsg.Price));
		var supplied = replaceMsg.Condition as AvantisOrderCondition;
		var takeProfit = supplied is null
			? existing.TakeProfit.ParseScaled(10, "take profit")
			: supplied.TakeProfitPrice;
		var stopLoss = supplied is null
			? existing.StopLoss.ParseScaled(10, "stop loss")
			: supplied.StopLossPrice;
		ValidateProtectionPrices(existing.IsBuy ? Sides.Buy : Sides.Sell,
			price, takeProfit, stopLoss);
		var transaction = RpcClient.CreateUpdateLimitTransaction(pairIndex,
			tradeIndex, price.ToBaseUnits(10, nameof(replaceMsg.Price)),
			Slippage.ToBaseUnits(10, nameof(Slippage)),
			takeProfit.ToBaseUnits(10, nameof(takeProfit)),
			stopLoss.ToBaseUnits(10, nameof(stopLoss)));
		var receipt = await RpcClient.SendAndWaitAsync(transaction,
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
			Side = existing.IsBuy ? Sides.Buy : Sides.Sell,
			OrderVolume = currentCollateral,
			Balance = currentCollateral,
			OrderPrice = price,
			OrderType = OrderTypes.Limit,
			OrderState = OrderStates.Active,
			OrderStringId = orderId,
			TransactionId = replaceMsg.TransactionId,
			OriginalTransactionId = replaceMsg.TransactionId,
			Commission = AvantisRpcClient.GetCommission(receipt),
			CommissionCurrency = "ETH",
			TimeInForce = TimeInForce.PutInQueue,
			PositionEffect = OrderPositionEffects.OpenOnly,
			Condition = supplied?.Clone(),
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
		var (pairIndex, tradeIndex) = AvantisExtensions.ParseOrderKey(orderId);
		var data = await ApiClient.GetUserDataAsync(RpcClient.WalletAddress,
			cancellationToken) ?? throw new InvalidDataException(
				"Avantis returned no account data.");
		var order = (data.LimitOrders ?? []).FirstOrDefault(item => item is not
			null && item.PairIndex == pairIndex && item.Index == tradeIndex) ??
			throw new InvalidOperationException(
				"Avantis limit order '" + orderId + "' is no longer active.");
		var receipt = await RpcClient.SendAndWaitAsync(
			RpcClient.CreateCancelLimitTransaction(pairIndex, tradeIndex),
			TransactionTimeout, cancellationToken);
		var time = await RpcClient.GetReceiptTimeAsync(receipt,
			cancellationToken);
		var market = GetMarket(pairIndex) ?? throw new InvalidDataException(
			"Avantis account references unknown pair " + pairIndex + ".");
		using (_sync.EnterScope())
			_knownLimitOrders.Remove(orderId);
		UpdateServerTime(time);
		await SendOutMessageAsync(CreateCancelledOrderMessage(order, market,
			orderId, time, cancelMsg.TransactionId,
			AvantisRpcClient.GetCommission(receipt)), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"Avantis group cancellation does not close positions.");
		var data = await ApiClient.GetUserDataAsync(RpcClient.WalletAddress,
			cancellationToken) ?? throw new InvalidDataException(
				"Avantis returned no account data.");
		var pairIndex = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? (int?)null
			: GetMarket(cancelMsg.SecurityId).PairIndex;
		var orders = (data.LimitOrders ?? []).Where(order => order is not null)
			.Where(order => pairIndex is null || order.PairIndex == pairIndex)
			.Where(order => cancelMsg.Side is null ||
				(order.IsBuy ? Sides.Buy : Sides.Sell) == cancelMsg.Side)
			.Where(order => cancelMsg.IsStop is null ||
				(order.LimitOrderType == AvantisOpenOrderTypes.StopLimit) ==
					cancelMsg.IsStop.Value)
			.ToArray();
		foreach (var order in orders)
		{
			var receipt = await RpcClient.SendAndWaitAsync(
				RpcClient.CreateCancelLimitTransaction(order.PairIndex,
					order.Index), TransactionTimeout, cancellationToken);
			var time = await RpcClient.GetReceiptTimeAsync(receipt,
				cancellationToken);
			var market = GetMarket(order.PairIndex);
			if (market is null)
				continue;
			var orderId = AvantisExtensions.OrderKey(order.PairIndex,
				order.Index);
			using (_sync.EnterScope())
				_knownLimitOrders.Remove(orderId);
			await SendOutMessageAsync(CreateCancelledOrderMessage(order, market,
				orderId, time, cancelMsg.TransactionId,
				AvantisRpcClient.GetCommission(receipt)), cancellationToken);
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
			BoardCode = BoardCodes.Avantis,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		var data = await ApiClient.GetUserDataAsync(RpcClient.WalletAddress,
			cancellationToken);
		await SendPortfolioSnapshotAsync(data, lookupMsg.TransactionId,
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
		var data = await ApiClient.GetUserDataAsync(RpcClient.WalletAddress,
			cancellationToken);
		await SendOrderSnapshotAsync(data, statusMsg, cancellationToken);
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
		AvantisMarket market, AvantisOrderCondition condition,
		CancellationToken cancellationToken)
	{
		if ((regMsg.OrderType ?? OrderTypes.Market) != OrderTypes.Market)
			throw new NotSupportedException(
				"Avantis positions are closed with market execution.");
		if (condition.PositionIndex is not int positionIndex ||
			positionIndex < 0)
			throw new InvalidOperationException(
				"Avantis close-position orders require PositionIndex.");
		var data = await ApiClient.GetUserDataAsync(RpcClient.WalletAddress,
			cancellationToken) ?? throw new InvalidDataException(
				"Avantis returned no account data.");
		var position = (data.Positions ?? []).FirstOrDefault(item => item is not
			null && item.PairIndex == market.PairIndex &&
			item.Index == positionIndex) ?? throw new InvalidOperationException(
				"Avantis position '" + market.PairIndex + ":" + positionIndex +
				"' is no longer open.");
		var expectedSide = position.IsBuy ? Sides.Sell : Sides.Buy;
		if (regMsg.Side != expectedSide)
			throw new InvalidOperationException(
				"Avantis close-position side must oppose the open position.");
		var currentCollateral = position.Collateral.ParseScaled(6,
			"position collateral");
		var collateral = regMsg.Volume > 0
			? regMsg.Volume
			: currentCollateral;
		if (collateral > currentCollateral)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Volume),
				collateral, "Close collateral exceeds the open position.");
		var executionFee = GetExecutionFee(condition);
		var receipt = await RpcClient.SendAndWaitAsync(
			RpcClient.CreateCloseTradeTransaction(market.PairIndex,
				positionIndex, collateral.ToBaseUnits(6, nameof(regMsg.Volume)),
				executionFee.ToBaseUnits(18, nameof(ExecutionFee))),
			TransactionTimeout, cancellationToken);
		var time = await RpcClient.GetReceiptTimeAsync(receipt,
			cancellationToken);
		var orderId = "close:" + receipt.TransactionHash.NormalizeHash();
		TrackOrder(orderId, regMsg.TransactionId);
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
			OrderPrice = GetPrice(market.PairIndex)?.Price ??
				position.OpenPrice.ParseScaled(10, "position open price"),
			OrderType = OrderTypes.Market,
			OrderState = OrderStates.Pending,
			OrderStringId = orderId,
			TransactionId = regMsg.TransactionId,
			OriginalTransactionId = regMsg.TransactionId,
			Commission = AvantisRpcClient.GetCommission(receipt),
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
				"Avantis wallet has insufficient USDC balance. Required " +
				collateral + " USDC, available " +
				balance.FromBaseUnits(6) + " USDC.");
		var allowance = await allowanceTask;
		if (allowance >= required)
			return;
		if (!IsAutoApprove)
			throw new InvalidOperationException(
				"Avantis TradingStorage USDC allowance is insufficient.");
		var approval = BigInteger.Max(required,
			ApprovalAmount.ToBaseUnits(6, nameof(ApprovalAmount)));
		_ = await RpcClient.SendAndWaitAsync(
			RpcClient.CreateApprovalTransaction(approval), TransactionTimeout,
			cancellationToken);
	}

	private async ValueTask RefreshAccountSubscriptionsAsync(
		CancellationToken cancellationToken)
	{
		var data = await ApiClient.GetUserDataAsync(RpcClient.WalletAddress,
			cancellationToken) ?? throw new InvalidDataException(
				"Avantis returned no account data.");
		if (_portfolioSubscriptionId != 0)
			await SendPortfolioSnapshotAsync(data, _portfolioSubscriptionId,
				cancellationToken);
		if (_orderStatusSubscriptionId != 0)
			await SendOrderSnapshotAsync(data, new OrderStatusMessage
			{
				TransactionId = _orderStatusSubscriptionId,
				IsSubscribe = true,
				PortfolioName = _portfolioName,
			}, cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshotAsync(AvantisUserData data,
		long transactionId, CancellationToken cancellationToken)
	{
		data ??= new();
		var ethTask = RpcClient.GetEthBalanceAsync(cancellationToken).AsTask();
		var usdcTask = RpcClient.GetUsdcBalanceAsync(cancellationToken).AsTask();
		await Task.WhenAll(ethTask, usdcTask);
		var time = DateTime.UtcNow;
		UpdateServerTime(time);
		await SendOutMessageAsync(CreateBalanceMessage("USDC",
			(await usdcTask).FromBaseUnits(6), transactionId, time),
			cancellationToken);
		await SendOutMessageAsync(CreateBalanceMessage("ETH",
			(await ethTask).FromBaseUnits(18), transactionId, time),
			cancellationToken);

		var currentMarkets = new HashSet<int>();
		foreach (var group in (data.Positions ?? [])
			.Where(static position => position is not null)
			.GroupBy(static position => position.PairIndex))
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
			var currentPrice = GetPrice(group.Key)?.Price;
			foreach (var position in group)
			{
				var collateral = position.Collateral.ParseScaled(6,
					"position collateral");
				var leverage = position.Leverage.ParseScaled(10,
					"position leverage");
				var openPrice = position.OpenPrice.ParseScaled(10,
					"position open price");
				if (openPrice <= 0)
					continue;
				var quantity = collateral * leverage / openPrice;
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

	private async ValueTask SendOrderSnapshotAsync(AvantisUserData data,
		OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		data ??= new();
		var messages = new List<ExecutionMessage>();
		foreach (var order in data.LimitOrders ?? [])
		{
			if (order is null)
				continue;
			var market = GetMarket(order.PairIndex);
			if (market is null)
				continue;
			var time = order.Block > 0
				? await RpcClient.GetBlockTimeAsync(order.Block,
					cancellationToken)
				: DateTime.UtcNow;
			messages.Add(CreateLimitOrderMessage(order, market, time,
				statusMsg.TransactionId));
		}
		foreach (var position in data.Positions ?? [])
		{
			if (position is null)
				continue;
			var market = GetMarket(position.PairIndex);
			if (market is null)
				continue;
			messages.Add(CreatePositionOrderMessage(position, market,
				statusMsg.TransactionId));
		}

		var selected = messages.Where(message =>
			IsSecurityMatch(message.SecurityId, statusMsg) &&
			IsOrderMatch(message, statusMsg))
			.OrderBy(static message => message.ServerTime)
			.Skip(Math.Max(0, statusMsg.Skip ?? 0).To<int>())
			.Take((statusMsg.Count ?? int.MaxValue).Min(int.MaxValue).To<int>())
			.ToArray();
		foreach (var message in selected)
		{
			UpdateServerTime(message.ServerTime);
			await SendOutMessageAsync(message, cancellationToken);
			if (message.OrderStringId?.StartsWith("position:",
				StringComparison.Ordinal) == true)
			{
				var position = (data.Positions ?? []).FirstOrDefault(item =>
					item is not null && "position:" +
					AvantisExtensions.OrderKey(item.PairIndex, item.Index) ==
						message.OrderStringId);
				if (position is not null)
					await SendOutMessageAsync(CreatePositionTradeMessage(position,
						GetMarket(position.PairIndex), statusMsg.TransactionId),
						cancellationToken);
			}
		}

		AvantisLimitOrder[] removed;
		using (_sync.EnterScope())
		{
			var current = (data.LimitOrders ?? []).Where(static order =>
				order is not null).ToDictionary(order =>
					AvantisExtensions.OrderKey(order.PairIndex, order.Index),
				static order => order, StringComparer.OrdinalIgnoreCase);
			removed = [.. _knownLimitOrders.Where(pair =>
				!current.ContainsKey(pair.Key)).Select(static pair => pair.Value)];
			_knownLimitOrders.Clear();
			foreach (var pair in current)
				_knownLimitOrders.Add(pair.Key, pair.Value);
		}
		foreach (var order in removed)
		{
			var market = GetMarket(order.PairIndex);
			if (market is null)
				continue;
			var orderId = AvantisExtensions.OrderKey(order.PairIndex,
				order.Index);
			await SendOutMessageAsync(CreateCancelledOrderMessage(order, market,
				orderId, DateTime.UtcNow, statusMsg.TransactionId, null),
				cancellationToken);
		}
	}

	private ExecutionMessage CreateLimitOrderMessage(AvantisLimitOrder order,
		AvantisMarket market, DateTime time, long originalTransactionId)
	{
		if (order.LimitOrderType is not (AvantisOpenOrderTypes.StopLimit or
			AvantisOpenOrderTypes.Limit))
			throw new InvalidDataException(
				"Avantis returned unsupported pending-order type '" +
				order.LimitOrderType + "'.");
		var orderId = AvantisExtensions.OrderKey(order.PairIndex, order.Index);
		var collateral = order.Collateral.ParseScaled(6, "order collateral");
		var condition = new AvantisOrderCondition
		{
			Leverage = order.Leverage.ParseScaled(10, "order leverage"),
			TakeProfitPrice = order.TakeProfit.ParseScaled(10, "take profit"),
			StopLossPrice = order.StopLoss.ParseScaled(10, "stop loss"),
			IsStopLimit = order.LimitOrderType ==
				AvantisOpenOrderTypes.StopLimit,
		};
		return new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = order.IsBuy ? Sides.Buy : Sides.Sell,
			OrderVolume = collateral,
			Balance = collateral,
			OrderPrice = order.Price.ParseScaled(10, "order price"),
			OrderType = OrderTypes.Limit,
			OrderState = OrderStates.Active,
			OrderStringId = orderId,
			TransactionId = GetOriginalTransactionId(orderId),
			OriginalTransactionId = originalTransactionId,
			TimeInForce = TimeInForce.PutInQueue,
			PositionEffect = OrderPositionEffects.OpenOnly,
			Condition = condition,
		};
	}

	private ExecutionMessage CreatePositionOrderMessage(
		AvantisPosition position, AvantisMarket market,
		long originalTransactionId)
	{
		var key = AvantisExtensions.OrderKey(position.PairIndex,
			position.Index);
		var collateral = position.Collateral.ParseScaled(6,
			"position collateral");
		return new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = position.OpenedAt > 0
				? position.OpenedAt.FromUnix()
				: DateTime.UtcNow,
			PortfolioName = _portfolioName,
			Side = position.IsBuy ? Sides.Buy : Sides.Sell,
			OrderVolume = collateral,
			Balance = 0m,
			OrderPrice = position.OpenPrice.ParseScaled(10,
				"position open price"),
			AveragePrice = position.OpenPrice.ParseScaled(10,
				"position open price"),
			OrderType = OrderTypes.Market,
			OrderState = OrderStates.Done,
			OrderStringId = "position:" + key,
			TransactionId = GetOriginalTransactionId("market:" + key),
			OriginalTransactionId = originalTransactionId,
			Commission = position.RolloverFee.TryParseScaled(6),
			CommissionCurrency = "USDC",
			TimeInForce = TimeInForce.CancelBalance,
			PositionEffect = OrderPositionEffects.OpenOnly,
			Condition = CreatePositionCondition(position),
		};
	}

	private ExecutionMessage CreatePositionTradeMessage(
		AvantisPosition position, AvantisMarket market,
		long originalTransactionId)
		=> new()
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = market.ToStockSharp(),
			ServerTime = position.OpenedAt > 0
				? position.OpenedAt.FromUnix()
				: DateTime.UtcNow,
			PortfolioName = _portfolioName,
			Side = position.IsBuy ? Sides.Buy : Sides.Sell,
			OrderStringId = "position:" + AvantisExtensions.OrderKey(
				position.PairIndex, position.Index),
			TradeStringId = "position:" + AvantisExtensions.OrderKey(
				position.PairIndex, position.Index),
			TradePrice = position.OpenPrice.ParseScaled(10,
				"position open price"),
			TradeVolume = position.Collateral.ParseScaled(6,
				"position collateral"),
			Commission = position.RolloverFee.TryParseScaled(6),
			CommissionCurrency = "USDC",
			OriginalTransactionId = originalTransactionId,
		};

	private ExecutionMessage CreateCancelledOrderMessage(
		AvantisLimitOrder order, AvantisMarket market, string orderId,
		DateTime time, long originalTransactionId, decimal? commission)
		=> new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = order.IsBuy ? Sides.Buy : Sides.Sell,
			OrderVolume = order.Collateral.ParseScaled(6, "order collateral"),
			Balance = 0m,
			OrderPrice = order.Price.ParseScaled(10, "order price"),
			OrderType = OrderTypes.Limit,
			OrderState = OrderStates.Done,
			OrderStringId = orderId,
			TransactionId = GetOriginalTransactionId(orderId),
			OriginalTransactionId = originalTransactionId,
			Commission = commission,
			CommissionCurrency = commission is null ? null : "ETH",
			TimeInForce = TimeInForce.PutInQueue,
			PositionEffect = OrderPositionEffects.OpenOnly,
		};

	private AvantisOrderCondition CreatePositionCondition(
		AvantisPosition position)
		=> new()
		{
			Leverage = position.Leverage.ParseScaled(10,
				"position leverage"),
			TakeProfitPrice = position.TakeProfit.ParseScaled(10,
				"take profit"),
			StopLossPrice = position.StopLoss.ParseScaled(10, "stop loss"),
			IsZeroFee = position.IsPnl,
			PositionIndex = position.Index,
		};

	private PositionChangeMessage CreateBalanceMessage(string symbol,
		decimal value, long transactionId, DateTime time)
		=> new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = new()
			{
				SecurityCode = symbol,
				BoardCode = BoardCodes.Avantis,
			},
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}.TryAdd(PositionChangeTypes.CurrentValue, value, true);

	private static void ValidateLeverage(AvantisMarket market,
		decimal leverage, bool isZeroFee, decimal collateral)
	{
		var minimum = isZeroFee && market.MinimumPnlLeverage > 0
			? market.MinimumPnlLeverage
			: market.MinimumLeverage;
		var maximum = isZeroFee && market.MaximumPnlLeverage > 0
			? market.MaximumPnlLeverage
			: market.MaximumLeverage;
		if (leverage < minimum || leverage > maximum)
			throw new ArgumentOutOfRangeException(nameof(leverage), leverage,
				"Avantis leverage for " + market.Symbol + " must be between " +
				minimum + " and " + maximum + ".");
		if (market.MinimumPositionValue > 0 &&
			collateral * leverage < market.MinimumPositionValue)
			throw new ArgumentOutOfRangeException(nameof(collateral), collateral,
				"Avantis leveraged position value must be at least " +
				market.MinimumPositionValue + " USDC.");
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

	private decimal GetExecutionFee(AvantisOrderCondition condition)
	{
		var value = condition.ExecutionFee ?? ExecutionFee;
		if (value is < 0 or > 1)
			throw new ArgumentOutOfRangeException(nameof(condition.ExecutionFee),
				value, "Avantis execution fee must be between zero and one ETH.");
		return value;
	}

	private static string ResolveOrderId(string orderStringId, long? orderId,
		string userOrderId)
	{
		if (orderId is not null)
			throw new InvalidOperationException(
				"Avantis orders use pair:index string identifiers.");
		return (orderStringId.IsEmpty() ? userOrderId : orderStringId)
			.ThrowIfEmpty(nameof(orderStringId)).Trim();
	}

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
			order.ServerTime < from.EnsureAvantisUtc())
			return false;
		if (filter.To is DateTime to &&
			order.ServerTime > to.EnsureAvantisUtc())
			return false;
		return true;
	}
}
