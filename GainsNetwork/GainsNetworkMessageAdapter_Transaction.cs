namespace StockSharp.GainsNetwork;

public partial class GainsNetworkMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(regMsg.PortfolioName);
		var market = GetMarket(regMsg.SecurityId);
		var condition = regMsg.Condition as GainsNetworkOrderCondition ?? new()
		{
			Leverage = DefaultLeverage,
		};
		if (condition.IsClosePosition)
		{
			await ClosePositionAsync(regMsg, market, condition,
				cancellationToken);
			return;
		}

		if (Variables.TradingState != GainsTradingStates.Activated)
			throw new InvalidOperationException(
				"Gains Network is not accepting new trades.");
		if (!market.IsEnabled)
			throw new InvalidOperationException(
				"Gains market '" + market.Symbol + "' is disabled.");

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Market or OrderTypes.Limit or
			OrderTypes.Conditional))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		if (condition.IsStopOrder && orderType == OrderTypes.Market)
			throw new InvalidOperationException(
				"Gains stop entry cannot use market execution.");

		var isStop = orderType == OrderTypes.Conditional ||
			condition.IsStopOrder;
		var collateral = GetCollateral(condition.CollateralSymbol.IsEmpty()
			? DefaultCollateral
			: condition.CollateralSymbol);
		var collateralAmount = regMsg.Volume;
		if (collateralAmount <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Volume),
				collateralAmount, "Gains order volume is collateral and must be " +
				"positive.");
		ValidateLeverage(market, condition.Leverage);
		var collateralPrice = collateral.Prices?.CollateralPriceUsd > 0
			? collateral.Prices.CollateralPriceUsd
			: 1m;
		var positionSizeUsd = collateralAmount * collateralPrice *
			condition.Leverage;
		if (market.MinimumPositionSizeUsd > 0 &&
			positionSizeUsd < market.MinimumPositionSizeUsd)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Volume),
				collateralAmount, "Gains leveraged position value for " +
				market.Symbol + " must be at least " +
				market.MinimumPositionSizeUsd + " USD.");

		var current = GetPrice(market.PairIndex);
		if (current?.MarkPrice is not > 0)
		{
			await RefreshChartsAsync(cancellationToken);
			current = GetPrice(market.PairIndex);
		}
		if (current?.MarkPrice is not > 0)
			throw new InvalidDataException("Gains returned no current price for " +
				market.Symbol + ".");
		if (orderType == OrderTypes.Market && !market.IsMarketOpen)
			throw new InvalidOperationException(
				"Gains market '" + market.Symbol + "' is currently closed.");

		var price = orderType == OrderTypes.Market
			? current.MarkPrice
			: regMsg.Price;
		if (price <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Price));
		if (orderType != OrderTypes.Market)
			ValidateEntryPrice(regMsg.Side, price, current.MarkPrice, isStop);
		ValidateProtectionPrices(regMsg.Side, price,
			condition.TakeProfitPrice, condition.StopLossPrice);

		await EnsureCollateralAsync(collateral, collateralAmount,
			cancellationToken);
		var tradeType = orderType == OrderTypes.Market
			? GainsTradeTypes.Market
			: isStop ? GainsTradeTypes.Stop : GainsTradeTypes.Limit;
		var receipt = await RpcClient.SendAndWaitAsync(
			RpcClient.CreateOpenTradeTransaction(market.PairIndex, regMsg.Side,
				collateral.CollateralIndex,
				collateralAmount.ToBaseUnits(collateral.Config.Decimals,
					nameof(regMsg.Volume)),
				price.ToBaseUnits(10, nameof(regMsg.Price)),
				condition.Leverage.ToBaseUnits(3,
					nameof(condition.Leverage)),
				condition.TakeProfitPrice.ToBaseUnits(10,
					nameof(condition.TakeProfitPrice)),
				condition.StopLossPrice.ToBaseUnits(10,
					nameof(condition.StopLossPrice)), tradeType,
				SlippagePercentage.ToBaseUnits(3,
					nameof(SlippagePercentage)), ReferrerAddress),
			TransactionTimeout, cancellationToken);
		var time = await RpcClient.GetReceiptTimeAsync(receipt,
			cancellationToken);
		var state = OrderStates.Pending;
		int orderIndex;
		string orderStringId;
		if (tradeType == GainsTradeTypes.Market)
		{
			var initiated = RpcClient.TryGetMarketOrderEvent(receipt) ??
				throw new InvalidDataException(
					"Gains market-order receipt contains no initiation event.");
			if (initiated.PairIndex != market.PairIndex)
				throw new InvalidDataException(
					"Gains market-order event references a different market.");
			orderIndex = initiated.OrderIndex;
			orderStringId = "market:" + orderIndex.ToString(
				CultureInfo.InvariantCulture);
		}
		else
		{
			var placed = RpcClient.TryGetOpenOrderEvent(receipt) ??
				throw new InvalidDataException(
					"Gains open-order receipt contains no placement event.");
			if (placed.PairIndex != market.PairIndex)
				throw new InvalidDataException(
					"Gains open-order event references a different market.");
			orderIndex = placed.OrderIndex;
			orderStringId = orderIndex.ToString(CultureInfo.InvariantCulture);
			state = OrderStates.Active;
		}

		condition.CollateralSymbol = collateral.Symbol;
		condition.IsStopOrder = isStop;
		condition.TradeIndex = orderIndex;
		TrackOrder(orderIndex, regMsg.TransactionId);
		UpdateServerTime(time);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = regMsg.Side,
			OrderVolume = collateralAmount,
			Balance = collateralAmount,
			OrderPrice = price,
			OrderType = tradeType == GainsTradeTypes.Stop
				? OrderTypes.Conditional
				: orderType,
			OrderState = state,
			OrderStringId = orderStringId,
			TransactionId = regMsg.TransactionId,
			OriginalTransactionId = regMsg.TransactionId,
			Commission = GainsNetworkRpcClient.GetCommission(receipt),
			CommissionCurrency = _deployment.NativeSymbol,
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
		var orderIndex = ResolveOrderIndex(replaceMsg.OldOrderStringId,
			replaceMsg.OldOrderId, replaceMsg.UserOrderId);
		var trades = await ApiClient.GetOpenTradesAsync(RpcClient.WalletAddress,
			cancellationToken);
		var existing = (trades ?? []).FirstOrDefault(item => item?.Trade is not
			null && item.Trade.IsOpen && GetTradeIndex(item.Trade) == orderIndex &&
			GetTradeType(item.Trade) != GainsTradeTypes.Market) ??
			throw new InvalidOperationException("Gains open order '" + orderIndex +
				"' is no longer active.");
		var trade = existing.Trade;
		var pairIndex = GetPairIndex(trade);
		var market = GetMarket(pairIndex) ?? throw new InvalidDataException(
			"Gains account references unknown pair " + pairIndex + ".");
		if (!replaceMsg.SecurityId.SecurityCode.IsEmpty() &&
			GetMarket(replaceMsg.SecurityId).PairIndex != pairIndex)
			throw new InvalidOperationException(
				"Replacement security does not match the Gains order.");

		var collateral = GetCollateral(GetCollateralIndex(trade));
		var currentVolume = GetCollateralAmount(trade, collateral);
		if (replaceMsg.Volume > 0 && replaceMsg.Volume != currentVolume)
			throw new NotSupportedException(
				"Gains cannot change pending-order collateral in place.");
		var price = replaceMsg.Price;
		if (price <= 0)
			throw new ArgumentOutOfRangeException(nameof(replaceMsg.Price));
		var tradeType = GetTradeType(trade);
		var supplied = replaceMsg.Condition as GainsNetworkOrderCondition;
		if (supplied is not null && supplied.IsStopOrder !=
			(tradeType == GainsTradeTypes.Stop))
			throw new NotSupportedException(
				"Gains cannot change a limit entry into a stop entry in place.");
		if (supplied is not null && !supplied.CollateralSymbol.IsEmpty() &&
			!supplied.CollateralSymbol.Equals(collateral.Symbol,
				StringComparison.OrdinalIgnoreCase))
			throw new NotSupportedException(
				"Gains cannot change pending-order collateral token in place.");
		var takeProfit = supplied is null
			? trade.TakeProfit.ParseScaled(10, "take profit")
			: supplied.TakeProfitPrice;
		var stopLoss = supplied is null
			? trade.StopLoss.ParseScaled(10, "stop loss")
			: supplied.StopLossPrice;
		var side = trade.IsLong ? Sides.Buy : Sides.Sell;
		var current = GetPrice(pairIndex);
		if (current?.MarkPrice is > 0)
			ValidateEntryPrice(side, price, current.MarkPrice,
				tradeType == GainsTradeTypes.Stop);
		ValidateProtectionPrices(side, price, takeProfit, stopLoss);

		var receipt = await RpcClient.SendAndWaitAsync(
			RpcClient.CreateUpdateOpenOrderTransaction(orderIndex,
				price.ToBaseUnits(10, nameof(replaceMsg.Price)),
				takeProfit.ToBaseUnits(10, nameof(takeProfit)),
				stopLoss.ToBaseUnits(10, nameof(stopLoss)),
				SlippagePercentage.ToBaseUnits(3,
					nameof(SlippagePercentage))),
			TransactionTimeout, cancellationToken);
		var time = await RpcClient.GetReceiptTimeAsync(receipt,
			cancellationToken);
		var condition = supplied ?? CreateOrderCondition(existing);
		condition.CollateralSymbol = collateral.Symbol;
		condition.IsStopOrder = tradeType == GainsTradeTypes.Stop;
		condition.TradeIndex = orderIndex;
		TrackOrder(orderIndex, replaceMsg.TransactionId);
		UpdateServerTime(time);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = side,
			OrderVolume = currentVolume,
			Balance = currentVolume,
			OrderPrice = price,
			OrderType = tradeType == GainsTradeTypes.Stop
				? OrderTypes.Conditional
				: OrderTypes.Limit,
			OrderState = OrderStates.Active,
			OrderStringId = orderIndex.ToString(CultureInfo.InvariantCulture),
			TransactionId = replaceMsg.TransactionId,
			OriginalTransactionId = replaceMsg.TransactionId,
			Commission = GainsNetworkRpcClient.GetCommission(receipt),
			CommissionCurrency = _deployment.NativeSymbol,
			TimeInForce = TimeInForce.PutInQueue,
			PositionEffect = OrderPositionEffects.OpenOnly,
			Condition = condition.Clone(),
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		var orderIndex = ResolveOrderIndex(cancelMsg.OrderStringId,
			cancelMsg.OrderId, cancelMsg.UserOrderId);
		var trades = await ApiClient.GetOpenTradesAsync(RpcClient.WalletAddress,
			cancellationToken);
		var order = (trades ?? []).FirstOrDefault(item => item?.Trade is not null &&
			item.Trade.IsOpen && GetTradeIndex(item.Trade) == orderIndex &&
			GetTradeType(item.Trade) != GainsTradeTypes.Market) ??
			throw new InvalidOperationException("Gains open order '" + orderIndex +
				"' is no longer active.");
		var receipt = await RpcClient.SendAndWaitAsync(
			RpcClient.CreateCancelOpenOrderTransaction(orderIndex),
			TransactionTimeout, cancellationToken);
		var time = await RpcClient.GetReceiptTimeAsync(receipt,
			cancellationToken);
		var market = GetMarket(GetPairIndex(order.Trade)) ??
			throw new InvalidDataException(
				"Gains cancellation references an unknown market.");
		using (_sync.EnterScope())
			_knownOrders.Remove(orderIndex);
		UpdateServerTime(time);
		await SendOutMessageAsync(CreateCancelledOrderMessage(order, market,
			time, cancelMsg.TransactionId,
			GainsNetworkRpcClient.GetCommission(receipt)), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"Gains group cancellation does not close positions.");
		var trades = await ApiClient.GetOpenTradesAsync(RpcClient.WalletAddress,
			cancellationToken);
		var pairIndex = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? (int?)null
			: GetMarket(cancelMsg.SecurityId).PairIndex;
		var orders = (trades ?? []).Where(static item => item?.Trade is not null &&
			item.Trade.IsOpen)
			.Where(item => GetTradeType(item.Trade) != GainsTradeTypes.Market)
			.Where(item => pairIndex is null ||
				GetPairIndex(item.Trade) == pairIndex)
			.Where(item => cancelMsg.Side is null ||
				(item.Trade.IsLong ? Sides.Buy : Sides.Sell) == cancelMsg.Side)
			.Where(item => cancelMsg.IsStop is null ||
				(GetTradeType(item.Trade) == GainsTradeTypes.Stop) ==
					cancelMsg.IsStop.Value)
			.ToArray();
		foreach (var order in orders)
		{
			var orderIndex = GetTradeIndex(order.Trade);
			var receipt = await RpcClient.SendAndWaitAsync(
				RpcClient.CreateCancelOpenOrderTransaction(orderIndex),
				TransactionTimeout, cancellationToken);
			var time = await RpcClient.GetReceiptTimeAsync(receipt,
				cancellationToken);
			var market = GetMarket(GetPairIndex(order.Trade));
			if (market is null)
				continue;
			using (_sync.EnterScope())
				_knownOrders.Remove(orderIndex);
			await SendOutMessageAsync(CreateCancelledOrderMessage(order, market,
				time, cancelMsg.TransactionId,
				GainsNetworkRpcClient.GetCommission(receipt)), cancellationToken);
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
			BoardCode = BoardCodes.GainsNetwork,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		var tradesTask = ApiClient.GetOpenTradesAsync(RpcClient.WalletAddress,
			cancellationToken).AsTask();
		var variablesTask = ApiClient.GetUserVariablesAsync(
			RpcClient.WalletAddress, cancellationToken).AsTask();
		await Task.WhenAll(tradesTask, variablesTask);
		await SendPortfolioSnapshotAsync(await tradesTask, await variablesTask,
			lookupMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId,
				cancellationToken);
			return;
		}
		_portfolioSubscriptionId = lookupMsg.TransactionId;
		using (_sync.EnterScope())
			_nextAccountRefresh = CurrentTime + AccountRefreshInterval;
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
		var pair = statusMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(statusMsg.SecurityId).Symbol;
		var tradesTask = ApiClient.GetOpenTradesAsync(RpcClient.WalletAddress,
			cancellationToken).AsTask();
		var historyTask = ApiClient.GetHistoryAsync(RpcClient.WalletAddress,
			_deployment.ChainId, HistoryLimit, statusMsg.From, statusMsg.To, pair,
			cancellationToken).AsTask();
		await Task.WhenAll(tradesTask, historyTask);
		await SendOrderSnapshotAsync(await tradesTask, await historyTask,
			statusMsg, false, cancellationToken);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}
		_orderStatusSubscriptionId = statusMsg.TransactionId;
		using (_sync.EnterScope())
			_nextAccountRefresh = CurrentTime + AccountRefreshInterval;
	}

	private async ValueTask ClosePositionAsync(OrderRegisterMessage regMsg,
		GainsMarket market, GainsNetworkOrderCondition condition,
		CancellationToken cancellationToken)
	{
		if ((regMsg.OrderType ?? OrderTypes.Market) != OrderTypes.Market)
			throw new NotSupportedException(
				"Gains positions are closed with market execution.");
		var trades = await ApiClient.GetOpenTradesAsync(RpcClient.WalletAddress,
			cancellationToken);
		var positions = (trades ?? []).Where(static item => item?.Trade is not
			null && item.Trade.IsOpen &&
			GetTradeType(item.Trade) == GainsTradeTypes.Market)
			.Where(item => GetPairIndex(item.Trade) == market.PairIndex)
			.ToArray();
		GainsTradeContainer position;
		if (condition.TradeIndex is int requestedIndex)
			position = positions.FirstOrDefault(item =>
				GetTradeIndex(item.Trade) == requestedIndex);
		else
			position = positions.Length == 1 ? positions[0] : null;
		if (position is null)
			throw new InvalidOperationException(condition.TradeIndex is null
				? "Gains close-position request requires TradeIndex when the " +
					"market does not have exactly one open position."
				: "Gains position '" + condition.TradeIndex +
					"' is no longer open on " + market.Symbol + ".");
		var trade = position.Trade;
		var orderIndex = GetTradeIndex(trade);
		var expectedSide = trade.IsLong ? Sides.Sell : Sides.Buy;
		if (regMsg.Side != expectedSide)
			throw new InvalidOperationException(
				"Gains close-position side must oppose the open position.");
		var collateral = GetCollateral(GetCollateralIndex(trade));
		var collateralAmount = GetCollateralAmount(trade, collateral);
		if (regMsg.Volume > 0 && regMsg.Volume != collateralAmount)
			throw new NotSupportedException(
				"The current Gains contract operation closes the entire position.");
		var current = GetPrice(market.PairIndex);
		if (current?.MarkPrice is not > 0)
		{
			await RefreshChartsAsync(cancellationToken);
			current = GetPrice(market.PairIndex);
		}
		if (current?.MarkPrice is not > 0)
			throw new InvalidDataException("Gains returned no current price for " +
				market.Symbol + ".");
		var receipt = await RpcClient.SendAndWaitAsync(
			RpcClient.CreateCloseTradeTransaction(orderIndex,
				current.MarkPrice.ToBaseUnits(10, "current price")),
			TransactionTimeout, cancellationToken);
		var time = await RpcClient.GetReceiptTimeAsync(receipt,
			cancellationToken);
		var initiated = RpcClient.TryGetMarketOrderEvent(receipt) ??
			throw new InvalidDataException(
				"Gains close receipt contains no market-order event.");
		if (initiated.PairIndex != market.PairIndex)
			throw new InvalidDataException(
				"Gains close event references a different market.");
		condition.CollateralSymbol = collateral.Symbol;
		condition.TradeIndex = orderIndex;
		TrackOrder(orderIndex, regMsg.TransactionId);
		UpdateServerTime(time);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = regMsg.Side,
			OrderVolume = collateralAmount,
			Balance = collateralAmount,
			OrderPrice = current.MarkPrice,
			OrderType = OrderTypes.Market,
			OrderState = OrderStates.Pending,
			OrderStringId = "close:" + initiated.OrderIndex.ToString(
				CultureInfo.InvariantCulture),
			TransactionId = regMsg.TransactionId,
			OriginalTransactionId = regMsg.TransactionId,
			Commission = GainsNetworkRpcClient.GetCommission(receipt),
			CommissionCurrency = _deployment.NativeSymbol,
			TimeInForce = TimeInForce.CancelBalance,
			PositionEffect = OrderPositionEffects.CloseOnly,
			Condition = condition.Clone(),
		}, cancellationToken);
	}

	private async ValueTask EnsureCollateralAsync(GainsCollateral collateral,
		decimal amount, CancellationToken cancellationToken)
	{
		var decimals = collateral.Config?.Decimals ?? throw new
			InvalidDataException("Gains collateral has no decimal configuration.");
		var required = amount.ToBaseUnits(decimals, nameof(amount));
		var balanceTask = RpcClient.GetTokenBalanceAsync(collateral.Address,
			cancellationToken).AsTask();
		var allowanceTask = RpcClient.GetTokenAllowanceAsync(collateral.Address,
			cancellationToken).AsTask();
		await Task.WhenAll(balanceTask, allowanceTask);
		var balance = await balanceTask;
		if (balance < required)
			throw new InvalidOperationException(
				"Gains wallet has insufficient " + collateral.Symbol +
				" balance. Required " + amount + ", available " +
				balance.FromBaseUnits(decimals) + ".");
		if (await allowanceTask >= required)
			return;
		if (!IsAutoApprove)
			throw new InvalidOperationException(
				"Gains diamond allowance for " + collateral.Symbol +
				" is insufficient.");
		var approval = BigInteger.Max(required,
			ApprovalAmount.ToBaseUnits(decimals, nameof(ApprovalAmount)));
		_ = await RpcClient.SendAndWaitAsync(
			RpcClient.CreateApprovalTransaction(collateral.Address, approval),
			TransactionTimeout, cancellationToken);
	}

	private async ValueTask PollAccountAsync(
		CancellationToken cancellationToken)
	{
		var portfolioId = _portfolioSubscriptionId;
		var orderId = _orderStatusSubscriptionId;
		if (portfolioId == 0 && orderId == 0)
			return;
		var tradesTask = ApiClient.GetOpenTradesAsync(RpcClient.WalletAddress,
			cancellationToken).AsTask();
		Task<GainsUserTradingVariables> variablesTask = null;
		Task<GainsHistoryItem[]> historyTask = null;
		if (portfolioId != 0)
			variablesTask = ApiClient.GetUserVariablesAsync(
				RpcClient.WalletAddress, cancellationToken).AsTask();
		if (orderId != 0)
			historyTask = ApiClient.GetHistoryAsync(RpcClient.WalletAddress,
				_deployment.ChainId, HistoryLimit.Min(250), null, null, null,
				cancellationToken).AsTask();
		var tasks = new List<Task> { tradesTask };
		if (variablesTask is not null)
			tasks.Add(variablesTask);
		if (historyTask is not null)
			tasks.Add(historyTask);
		await Task.WhenAll(tasks);
		var trades = await tradesTask;
		if (variablesTask is not null)
			await SendPortfolioSnapshotAsync(trades, await variablesTask,
				portfolioId, cancellationToken);
		if (historyTask is not null)
			await SendOrderSnapshotAsync(trades, await historyTask,
				new OrderStatusMessage
				{
					TransactionId = orderId,
					IsSubscribe = true,
					PortfolioName = _portfolioName,
				}, true, cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshotAsync(
		GainsTradeContainer[] trades, GainsUserTradingVariables userVariables,
		long transactionId, CancellationToken cancellationToken)
	{
		trades ??= [];
		userVariables ??= new();
		var nativeTask = RpcClient.GetNativeBalanceAsync(cancellationToken).AsTask();
		var pricesTask = RefreshChartsAsync(cancellationToken).AsTask();
		await Task.WhenAll(nativeTask, pricesTask);
		var time = DateTime.UtcNow;
		UpdateServerTime(time);
		var userCollaterals = userVariables.Collaterals ?? [];
		foreach (var collateral in Variables.Collaterals ?? [])
		{
			if (collateral is null || !collateral.IsActive)
				continue;
			var offset = collateral.CollateralIndex - 1;
			if (offset < 0 || offset >= userCollaterals.Length ||
				userCollaterals[offset] is null)
				continue;
			var accountCollateral = userCollaterals[offset];
			var decimals = accountCollateral.Decimals;
			var value = accountCollateral.Balance.ParseInteger(
				"collateral balance").FromBaseUnits(decimals);
			await SendOutMessageAsync(CreateBalanceMessage(collateral.Symbol,
				value, transactionId, time), cancellationToken);
		}
		await SendOutMessageAsync(CreateBalanceMessage(_deployment.NativeSymbol,
			(await nativeTask).FromBaseUnits(18), transactionId, time),
			cancellationToken);

		var currentPositions = new HashSet<string>(
			StringComparer.OrdinalIgnoreCase);
		foreach (var container in trades.Where(static item => item?.Trade is not
			null && item.Trade.IsOpen &&
			GetTradeType(item.Trade) == GainsTradeTypes.Market))
		{
			var trade = container.Trade;
			var market = GetMarket(GetPairIndex(trade));
			if (market is null)
				continue;
			var index = GetTradeIndex(trade);
			var key = PositionKey(market.PairIndex, index);
			currentPositions.Add(key);
			var collateral = GetCollateral(GetCollateralIndex(trade));
			var collateralAmount = GetCollateralAmount(trade, collateral);
			var leverage = trade.Leverage.ParseScaled(3, "position leverage");
			var openPrice = trade.OpenPrice.ParseScaled(10,
				"position open price");
			var collateralPrice = collateral.Prices?.CollateralPriceUsd > 0
				? collateral.Prices.CollateralPriceUsd
				: 1m;
			var quantity = trade.PositionSizeToken.IsEmpty()
				? 0m
				: trade.PositionSizeToken.ParseScaled(18,
					"position token size");
			if (quantity <= 0 && openPrice > 0)
				quantity = collateralAmount * collateralPrice * leverage /
					openPrice;
			var currentPrice = GetPrice(market.PairIndex)?.MarkPrice;
			decimal? unrealized = null;
			if (currentPrice is > 0 && openPrice > 0)
				unrealized = quantity * (currentPrice.Value - openPrice) *
					(trade.IsLong ? 1m : -1m) / collateralPrice;
			decimal? realized = null;
			if (container.TradeFeesData?.RealizedPnlCollateral.IsEmpty() == false)
				realized = container.TradeFeesData.RealizedPnlCollateral.ParseScaled(
					collateral.Config.Decimals, "realized PnL");
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = market.ToStockSharp(),
				ClientCode = "trade:" + index.ToString(
					CultureInfo.InvariantCulture),
				DepoName = collateral.Symbol,
				ServerTime = time,
				OriginalTransactionId = transactionId,
				Side = trade.IsLong ? Sides.Buy : Sides.Sell,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, quantity, true)
			.TryAdd(PositionChangeTypes.BlockedValue, collateralAmount, true)
			.TryAdd(PositionChangeTypes.AveragePrice, openPrice, true)
			.TryAdd(PositionChangeTypes.CurrentPrice, currentPrice, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, unrealized, true)
			.TryAdd(PositionChangeTypes.RealizedPnL, realized, true)
			.TryAdd(PositionChangeTypes.Leverage, leverage, true),
				cancellationToken);
		}

		string[] removed;
		using (_sync.EnterScope())
		{
			removed = [.. _knownPositions.Where(key =>
				!currentPositions.Contains(key))];
			_knownPositions.Clear();
			_knownPositions.UnionWith(currentPositions);
		}
		foreach (var key in removed)
		{
			var (pairIndex, tradeIndex) = ParsePositionKey(key);
			var market = GetMarket(pairIndex);
			if (market is null)
				continue;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = market.ToStockSharp(),
				ClientCode = "trade:" + tradeIndex.ToString(
					CultureInfo.InvariantCulture),
				ServerTime = time,
				OriginalTransactionId = transactionId,
			}.TryAdd(PositionChangeTypes.CurrentValue, 0m, true),
				cancellationToken);
		}
	}

	private async ValueTask SendOrderSnapshotAsync(
		GainsTradeContainer[] trades, GainsHistoryItem[] history,
		OrderStatusMessage statusMsg, bool isIncremental,
		CancellationToken cancellationToken)
	{
		trades ??= [];
		history ??= [];
		var entries = new List<(ExecutionMessage Order, ExecutionMessage Fill)>();
		var pending = trades.Where(static item => item?.Trade is not null &&
			item.Trade.IsOpen &&
			GetTradeType(item.Trade) != GainsTradeTypes.Market).ToArray();
		foreach (var item in pending)
		{
			var market = GetMarket(GetPairIndex(item.Trade));
			if (market is not null)
				entries.Add((CreatePendingOrderMessage(item, market,
					statusMsg.TransactionId), null));
		}

		var historyIndices = new HashSet<int>();
		foreach (var item in history.Where(static item => item is not null &&
			IsExecutionAction(item.Action)))
		{
			var market = GetMarket(item.Pair);
			if (market is null)
				continue;
			if (item.TradeIndex is int tradeIndex)
				historyIndices.Add(tradeIndex);
			var shouldSend = true;
			using (_sync.EnterScope())
			{
				if (isIncremental && _seenHistory.Contains(item.Id))
					shouldSend = false;
				_seenHistory.Add(item.Id);
			}
			if (!shouldSend)
				continue;
			var order = CreateHistoryOrderMessage(item, market,
				statusMsg.TransactionId);
			entries.Add((order, CreateHistoryFillMessage(item, market,
				order.OrderStringId, statusMsg.TransactionId)));
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

		GainsTradeContainer[] removed;
		using (_sync.EnterScope())
		{
			var current = pending.ToDictionary(item =>
				GetTradeIndex(item.Trade), static item => item);
			removed = [.. _knownOrders.Where(pair =>
				!current.ContainsKey(pair.Key) &&
				!historyIndices.Contains(pair.Key))
				.Select(static pair => pair.Value)];
			_knownOrders.Clear();
			foreach (var pair in current)
				_knownOrders.Add(pair.Key, pair.Value);
		}
		foreach (var item in removed)
		{
			var market = GetMarket(GetPairIndex(item.Trade));
			if (market is null)
				continue;
			var message = CreateCancelledOrderMessage(item, market,
				DateTime.UtcNow, statusMsg.TransactionId, null);
			if (IsSecurityMatch(message.SecurityId, statusMsg) &&
				IsOrderMatch(message, statusMsg))
				await SendOutMessageAsync(message, cancellationToken);
		}
	}

	private ExecutionMessage CreatePendingOrderMessage(
		GainsTradeContainer container, GainsMarket market,
		long originalTransactionId)
	{
		var trade = container.Trade;
		var index = GetTradeIndex(trade);
		var collateral = GetCollateral(GetCollateralIndex(trade));
		var volume = GetCollateralAmount(trade, collateral);
		var type = GetTradeType(trade);
		var time = container.TradeInfo?.LastOpenInterestUpdateTime > 0
			? container.TradeInfo.LastOpenInterestUpdateTime.FromUnix().EnsureUtc()
			: ServerTime;
		return new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = trade.IsLong ? Sides.Buy : Sides.Sell,
			OrderVolume = volume,
			Balance = volume,
			OrderPrice = trade.OpenPrice.ParseScaled(10, "order price"),
			OrderType = type == GainsTradeTypes.Stop
				? OrderTypes.Conditional
				: OrderTypes.Limit,
			OrderState = OrderStates.Active,
			OrderStringId = index.ToString(CultureInfo.InvariantCulture),
			TransactionId = GetOriginalTransactionId(index),
			OriginalTransactionId = originalTransactionId,
			TimeInForce = TimeInForce.PutInQueue,
			PositionEffect = OrderPositionEffects.OpenOnly,
			Condition = CreateOrderCondition(container),
		};
	}

	private ExecutionMessage CreateCancelledOrderMessage(
		GainsTradeContainer container, GainsMarket market, DateTime time,
		long originalTransactionId, decimal? commission)
	{
		var message = CreatePendingOrderMessage(container, market,
			originalTransactionId);
		message.ServerTime = time.EnsureUtc();
		message.Balance = 0m;
		message.OrderState = OrderStates.Done;
		message.Commission = commission;
		message.CommissionCurrency = commission is null
			? null
			: _deployment.NativeSymbol;
		return message;
	}

	private ExecutionMessage CreateHistoryOrderMessage(GainsHistoryItem item,
		GainsMarket market, long originalTransactionId)
	{
		var isOpen = IsOpenAction(item.Action);
		var side = item.IsLong ? Sides.Buy : Sides.Sell;
		if (!isOpen)
			side = side == Sides.Buy ? Sides.Sell : Sides.Buy;
		var collateral = GetHistoryCollateral(item);
		var volume = GetHistoryCollateralAmount(item);
		var orderType = GetHistoryOrderType(item.Action);
		var tradeIndex = item.TradeIndex ?? 0;
		var orderId = GetHistoryOrderId(item, isOpen, orderType);
		var commission = GetHistoryCommission(item);
		return new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = item.Date.ParseTime("history time"),
			PortfolioName = _portfolioName,
			Side = side,
			OrderVolume = volume,
			Balance = 0m,
			OrderPrice = item.Price,
			AveragePrice = item.Price,
			OrderType = orderType,
			OrderState = OrderStates.Done,
			OrderStringId = orderId,
			TransactionId = GetOriginalTransactionId(tradeIndex),
			OriginalTransactionId = originalTransactionId,
			Commission = commission,
			CommissionCurrency = commission is null ? null : collateral.Symbol,
			TimeInForce = orderType == OrderTypes.Market
				? TimeInForce.CancelBalance
				: TimeInForce.PutInQueue,
			PositionEffect = isOpen
				? OrderPositionEffects.OpenOnly
				: OrderPositionEffects.CloseOnly,
			Condition = new GainsNetworkOrderCondition
			{
				Leverage = item.Leverage > 0 ? item.Leverage : 1m,
				CollateralSymbol = collateral.Symbol,
				TradeIndex = item.TradeIndex,
				IsStopOrder = item.Action.EndsWith("SL",
					StringComparison.OrdinalIgnoreCase),
				IsClosePosition = !isOpen,
			},
		};
	}

	private ExecutionMessage CreateHistoryFillMessage(GainsHistoryItem item,
		GainsMarket market, string orderId, long originalTransactionId)
	{
		var isOpen = IsOpenAction(item.Action);
		var side = item.IsLong ? Sides.Buy : Sides.Sell;
		if (!isOpen)
			side = side == Sides.Buy ? Sides.Sell : Sides.Buy;
		var collateral = GetHistoryCollateral(item);
		var commission = GetHistoryCommission(item);
		return new()
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = market.ToStockSharp(),
			ServerTime = item.Date.ParseTime("history time"),
			PortfolioName = _portfolioName,
			Side = side,
			OrderStringId = orderId,
			TradeStringId = "history:" + item.Id.ToString(
				CultureInfo.InvariantCulture),
			TradePrice = item.Price,
			TradeVolume = item.Price > 0 ? item.Size.Abs() / item.Price : 0m,
			Commission = commission,
			CommissionCurrency = commission is null ? null : collateral.Symbol,
			PnL = item.NetPnl,
			OriginalTransactionId = originalTransactionId,
			PositionEffect = isOpen
				? OrderPositionEffects.OpenOnly
				: OrderPositionEffects.CloseOnly,
		};
	}

	private GainsNetworkOrderCondition CreateOrderCondition(
		GainsTradeContainer container)
	{
		var trade = container.Trade;
		var collateral = GetCollateral(GetCollateralIndex(trade));
		return new()
		{
			Leverage = trade.Leverage.ParseScaled(3, "order leverage"),
			CollateralSymbol = collateral.Symbol,
			TakeProfitPrice = trade.TakeProfit.ParseScaled(10, "take profit"),
			StopLossPrice = trade.StopLoss.ParseScaled(10, "stop loss"),
			IsStopOrder = GetTradeType(trade) == GainsTradeTypes.Stop,
			TradeIndex = GetTradeIndex(trade),
		};
	}

	private PositionChangeMessage CreateBalanceMessage(string symbol,
		decimal value, long transactionId, DateTime time)
		=> new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = new()
			{
				SecurityCode = symbol,
				BoardCode = BoardCodes.GainsNetwork,
			},
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}.TryAdd(PositionChangeTypes.CurrentValue, value, true);

	private GainsCollateral GetHistoryCollateral(GainsHistoryItem item)
		=> item.CollateralIndex is int index
			? GetCollateral(index)
			: GetCollateral(DefaultCollateral);

	private decimal GetHistoryCollateralAmount(GainsHistoryItem item)
	{
		var collateralPrice = item.CollateralPriceUsd is > 0
			? item.CollateralPriceUsd.Value
			: 1m;
		return item.Leverage > 0
			? item.Size.Abs() / item.Leverage / collateralPrice
			: item.Size.Abs();
	}

	private static decimal? GetHistoryCommission(GainsHistoryItem item)
	{
		if (item.Pnl is not decimal gross || item.NetPnl is not decimal net)
			return null;
		var commission = gross - net;
		return commission > 0 ? commission : null;
	}

	private static string GetHistoryOrderId(GainsHistoryItem item,
		bool isOpen, OrderTypes orderType)
	{
		var index = item.TradeIndex?.ToString(CultureInfo.InvariantCulture) ??
			item.Id.ToString(CultureInfo.InvariantCulture);
		if (isOpen && orderType == OrderTypes.Limit)
			return index;
		if (isOpen)
			return "market:" + index;
		return "close:" + index + ":" +
			item.Id.ToString(CultureInfo.InvariantCulture);
	}

	private static OrderTypes GetHistoryOrderType(string action)
	{
		if (action?.EndsWith("Market",
			StringComparison.OrdinalIgnoreCase) == true)
			return OrderTypes.Market;
		if (action?.EndsWith("Limit",
			StringComparison.OrdinalIgnoreCase) == true)
			return OrderTypes.Limit;
		return OrderTypes.Conditional;
	}

	private static bool IsExecutionAction(string action)
		=> IsOpenAction(action) || action?.StartsWith("TradeClosed",
			StringComparison.OrdinalIgnoreCase) == true;

	private static bool IsOpenAction(string action)
		=> action?.StartsWith("TradeOpened",
			StringComparison.OrdinalIgnoreCase) == true;

	private static void ValidateLeverage(GainsMarket market, decimal leverage)
	{
		if (leverage < market.MinimumLeverage || leverage > market.MaximumLeverage)
			throw new ArgumentOutOfRangeException(nameof(leverage), leverage,
				"Gains leverage for " + market.Symbol + " must be between " +
				market.MinimumLeverage + " and " + market.MaximumLeverage + ".");
	}

	private static void ValidateEntryPrice(Sides side, decimal entryPrice,
		decimal currentPrice, bool isStop)
	{
		if (entryPrice <= 0 || currentPrice <= 0)
			throw new ArgumentOutOfRangeException(nameof(entryPrice));
		var isBeyondMarket = side == Sides.Buy
			? entryPrice > currentPrice
			: entryPrice < currentPrice;
		if (isStop != isBeyondMarket)
			throw new ArgumentOutOfRangeException(nameof(entryPrice), entryPrice,
				isStop
					? "Gains stop-entry price must be beyond the current market " +
						"in the trade direction."
					: "Gains limit-entry price must improve on the current market.");
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

	private static int ResolveOrderIndex(string orderStringId, long? orderId,
		string userOrderId)
	{
		if (orderId is long numeric)
		{
			if (numeric is < 0 or > int.MaxValue)
				throw new ArgumentOutOfRangeException(nameof(orderId));
			return (int)numeric;
		}
		var value = (orderStringId.IsEmpty() ? userOrderId : orderStringId)
			.ThrowIfEmpty(nameof(orderStringId)).Trim();
		if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
			out var index) || index < 0)
			throw new InvalidOperationException(
				"Gains pending orders use numeric trade indexes.");
		return index;
	}

	private static int GetTradeIndex(GainsTrade trade)
		=> trade?.Index.ParseIndex("trade index") ?? throw new
			InvalidDataException("Gains returned an empty trade.");

	private static int GetPairIndex(GainsTrade trade)
		=> trade?.PairIndex.ParseIndex("pair index") ?? throw new
			InvalidDataException("Gains returned an empty trade.");

	private static int GetCollateralIndex(GainsTrade trade)
		=> trade?.CollateralIndex.ParseIndex("collateral index") ?? throw new
			InvalidDataException("Gains returned an empty trade.");

	private static GainsTradeTypes GetTradeType(GainsTrade trade)
	{
		if (trade is null)
			throw new InvalidDataException("Gains returned an empty trade.");
		if (!Enum.IsDefined(trade.TradeType))
			throw new InvalidDataException(
				"Gains returned unsupported trade type " + trade.TradeType + ".");
		return trade.TradeType;
	}

	private static decimal GetCollateralAmount(GainsTrade trade,
		GainsCollateral collateral)
		=> trade.CollateralAmount.ParseScaled(collateral.Config.Decimals,
			"collateral amount");

	private static string PositionKey(int pairIndex, int tradeIndex)
		=> pairIndex.ToString(CultureInfo.InvariantCulture) + ":" +
			tradeIndex.ToString(CultureInfo.InvariantCulture);

	private static (int PairIndex, int TradeIndex) ParsePositionKey(string key)
	{
		var parts = key?.Split(':');
		if (parts?.Length != 2 ||
			!int.TryParse(parts[0], NumberStyles.None,
				CultureInfo.InvariantCulture, out var pairIndex) || pairIndex < 0 ||
			!int.TryParse(parts[1], NumberStyles.None,
				CultureInfo.InvariantCulture, out var tradeIndex) || tradeIndex < 0)
			throw new InvalidDataException(
				"Invalid Gains position identifier '" + key + "'.");
		return (pairIndex, tradeIndex);
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
		if (filter.OrderId is long orderId &&
			(!int.TryParse(order.OrderStringId, NumberStyles.None,
				CultureInfo.InvariantCulture, out var index) || index != orderId))
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
			order.ServerTime < from.EnsureUtc())
			return false;
		if (filter.To is DateTime to && order.ServerTime > to.EnsureUtc())
			return false;
		return true;
	}
}
