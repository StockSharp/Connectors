namespace StockSharp.Drift;

public partial class DriftMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		ValidatePortfolio(regMsg.PortfolioName);
		var market = GetMarket(regMsg.SecurityId);
		EnsureTradingReady();
		await PlaceOrderAsync(market, regMsg.TransactionId, regMsg.Side,
			regMsg.Volume.Abs(), regMsg.Price,
			regMsg.OrderType ?? OrderTypes.Limit, regMsg.TimeInForce,
			regMsg.PostOnly == true, regMsg.PositionEffect,
			regMsg.Condition as DriftOrderCondition ?? new(),
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		ValidatePortfolio(replaceMsg.PortfolioName);
		EnsureTradingReady();
		var orderId = ResolveOrderId(replaceMsg.OldOrderId,
			replaceMsg.OldOrderStringId);
		await CancelOrderCoreAsync([orderId], cancellationToken);
		await SendCancelledOrderAsync(orderId, replaceMsg.SecurityId,
			replaceMsg.TransactionId, cancellationToken);
		var market = GetMarket(replaceMsg.SecurityId);
		await PlaceOrderAsync(market, replaceMsg.TransactionId, replaceMsg.Side,
			replaceMsg.Volume.Abs(), replaceMsg.Price,
			replaceMsg.OrderType ?? OrderTypes.Limit, replaceMsg.TimeInForce,
			replaceMsg.PostOnly == true, replaceMsg.PositionEffect,
			replaceMsg.Condition as DriftOrderCondition ?? new(),
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		ValidatePortfolio(cancelMsg.PortfolioName);
		EnsureTradingReady();
		var orderId = ResolveOrderId(cancelMsg.OrderId,
			cancelMsg.OrderStringId);
		await CancelOrderCoreAsync([orderId], cancellationToken);
		await SendCancelledOrderAsync(orderId, cancelMsg.SecurityId,
			cancelMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		ValidatePortfolio(cancelMsg.PortfolioName);
		EnsureTradingReady();
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"Drift bulk cancellation does not close positions.");
		if (!cancelMsg.SecurityId.SecurityCode.IsEmpty() ||
			cancelMsg.Side is not null || cancelMsg.IsStop is not null)
		{
			var user = await RestClient.GetUserAsync(_accountAddress,
				cancellationToken);
			var symbol = cancelMsg.SecurityId.SecurityCode.IsEmpty()
				? null
				: GetMarket(cancelMsg.SecurityId).Symbol;
			var ids = (user?.Orders ?? [])
				.Where(static order => order is not null && order.OrderId > 0)
				.Where(order => symbol.IsEmpty() || order.Symbol.Equals(symbol,
					StringComparison.Ordinal))
				.Where(order => cancelMsg.Side is null ||
					order.Direction.ToStockSharpDirection() == cancelMsg.Side)
				.Where(order => cancelMsg.IsStop is null ||
					!cancelMsg.IsStop.Value)
				.Select(static order => order.OrderId)
				.ToArray();
			if (ids.Length > 0)
				await CancelOrderCoreAsync(ids, cancellationToken);
		}
		else
			await CancelOrderCoreAsync(null, cancellationToken);
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
			using (_sync.EnterScope())
				_portfolioSubscriptions.Remove(lookupMsg.OriginalTransactionId);
			return;
		}
		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = PortfolioName,
			BoardCode = BoardCodes.Drift,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		var user = await RestClient.GetUserAsync(_accountAddress,
			cancellationToken);
		await SendPortfolioSnapshotAsync(user, lookupMsg.TransactionId, true,
			cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
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
		EnsureAccountReady();
		ValidatePortfolio(statusMsg.PortfolioName);
		if (!statusMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_orderSubscriptions.Remove(statusMsg.OriginalTransactionId);
			return;
		}
		var subscription = CreateOrderSubscription(statusMsg);
		var userTask = RestClient.GetUserAsync(_accountAddress,
			cancellationToken).AsTask();
		var tradesTask = RestClient.GetUserTradesAsync(_accountAddress,
			subscription.Limit, cancellationToken).AsTask();
		await Task.WhenAll(userTask, tradesTask);
		await SendOrderSnapshotAsync(await userTask, await tradesTask,
			subscription, statusMsg.TransactionId, true, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_orderSubscriptions.Add(statusMsg.TransactionId, subscription);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private async ValueTask PlaceOrderAsync(DriftMarket market,
		long transactionId, Sides side, decimal volume, decimal price,
		OrderTypes orderType, TimeInForce? timeInForce, bool isPostOnly,
		OrderPositionEffects? positionEffect, DriftOrderCondition condition,
		CancellationToken cancellationToken)
	{
		if (market.MarketType != DriftMarketTypes.Perpetual)
			throw new NotSupportedException(
				"The current Drift program accepts DLOB orders for perpetual " +
				"markets only.");
		var apiOrderType = orderType.ToDrift();
		if (volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(volume));
		var minimum = market.Limits?.Amount?.Minimum ?? market.GetVolumeStep();
		var maximum = market.Limits?.Amount?.Maximum;
		if (volume < minimum || maximum is decimal max && volume > max)
			throw new ArgumentOutOfRangeException(nameof(volume), volume,
				$"Drift order volume must be between {minimum} and " +
				$"{maximum?.ToString(CultureInfo.InvariantCulture) ?? "unlimited"}.");
		if (orderType == OrderTypes.Limit && price <= 0)
			throw new ArgumentOutOfRangeException(nameof(price), price,
				"A Drift limit order requires a positive price.");
		if (orderType == OrderTypes.Market && (isPostOnly || condition.IsPostOnly))
			throw new InvalidOperationException(
				"A Drift market order cannot be post-only.");
		if (timeInForce == TimeInForce.MatchOrCancel)
			throw new NotSupportedException(
				"Drift does not expose fill-or-kill orders through this API.");
		condition.IsReduceOnly |=
			positionEffect == OrderPositionEffects.CloseOnly;
		if (condition.Leverage is decimal leverage)
		{
			var minimumLeverage = market.Limits?.Leverage?.Minimum ?? 1m;
			var maximumLeverage = market.Limits?.Leverage?.Maximum;
			if (leverage < minimumLeverage ||
				maximumLeverage is decimal maxLeverage && leverage > maxLeverage)
				throw new ArgumentOutOfRangeException(nameof(condition.Leverage),
					leverage, "The requested Drift leverage is outside the " +
					"market limits.");
		}
		var prepared = await RestClient.PrepareOrderAsync(new()
		{
			AccountId = _accountAddress,
			Symbol = market.Symbol,
			Direction = side.ToDrift(),
			Amount = volume.ToDriftWire(),
			OrderType = apiOrderType,
			MarginMode = condition.MarginMode,
			Price = orderType == OrderTypes.Limit ? price.ToDriftWire() : null,
			IsReduceOnly = condition.IsReduceOnly,
			IsPostOnly = isPostOnly || condition.IsPostOnly,
			PositionMaximumLeverage = condition.Leverage,
			IsSimulationEnabled = IsSimulationEnabled,
		}, cancellationToken);
		var signature = await ExecutePreparedAsync(prepared, cancellationToken);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = ServerTime,
			PortfolioName = PortfolioName,
			DepoName = _accountAddress,
			Side = side,
			OrderVolume = volume,
			Balance = volume,
			OrderPrice = orderType == OrderTypes.Market ? 0m : price,
			OrderType = orderType,
			OrderState = OrderStates.Pending,
			OrderStringId = signature,
			TransactionId = transactionId,
			OriginalTransactionId = transactionId,
			TimeInForce = orderType == OrderTypes.Market
				? TimeInForce.CancelBalance
				: TimeInForce.PutInQueue,
			PositionEffect = condition.IsReduceOnly
				? OrderPositionEffects.CloseOnly
				: null,
			Condition = condition,
		}, cancellationToken);
		SchedulePrivatePoll();
	}

	private async ValueTask CancelOrderCoreAsync(long[] orderIds,
		CancellationToken cancellationToken)
	{
		var prepared = await RestClient.PrepareCancelAsync(new()
		{
			AccountId = _accountAddress,
			OrderIds = orderIds,
			IsSimulationEnabled = IsSimulationEnabled,
		}, cancellationToken);
		await ExecutePreparedAsync(prepared, cancellationToken);
		SchedulePrivatePoll();
	}

	private async ValueTask<string> ExecutePreparedAsync(
		DriftPreparedTransactionResponse prepared,
		CancellationToken cancellationToken)
	{
		if (prepared?.IsSuccess != true || prepared.Transaction.IsEmpty())
			throw new DriftApiException(prepared?.Message.IsEmpty() == false
				? prepared.Message
				: "Drift returned no prepared transaction.");
		var signed = Signer.SignTransaction(prepared.Transaction);
		var executed = await RestClient.ExecuteAsync(new()
		{
			SignedTransaction = signed,
			IsSimulationEnabled = IsSimulationEnabled,
		}, cancellationToken);
		if (executed?.IsSuccess != true ||
			executed.TransactionSignature.IsEmpty())
			throw new DriftApiException(executed?.Message.IsEmpty() == false
				? executed.Message
				: "Drift did not return a transaction signature.");
		return executed.TransactionSignature.NormalizeSignature();
	}

	private ValueTask SendCancelledOrderAsync(long orderId,
		SecurityId securityId, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = securityId,
			ServerTime = ServerTime,
			PortfolioName = PortfolioName,
			DepoName = _accountAddress,
			OrderId = orderId,
			OrderState = OrderStates.Done,
			TransactionId = transactionId,
			OriginalTransactionId = transactionId,
		}, cancellationToken);

	private async ValueTask PollPrivateAsync(
		CancellationToken cancellationToken)
	{
		long[] portfolios;
		KeyValuePair<long, OrderStatusSubscription>[] orders;
		using (_sync.EnterScope())
		{
			portfolios = [.. _portfolioSubscriptions];
			orders = [.. _orderSubscriptions];
		}
		if (portfolios.Length == 0 && orders.Length == 0)
			return;
		var userTask = RestClient.GetUserAsync(_accountAddress,
			cancellationToken).AsTask();
		Task<DriftTrade[]> tradesTask = null;
		if (orders.Length > 0)
			tradesTask = RestClient.GetUserTradesAsync(_accountAddress,
				Math.Min(20, HistoryLimit), cancellationToken).AsTask();
		if (tradesTask is null)
			await userTask;
		else
			await Task.WhenAll(userTask, tradesTask);
		var user = await userTask;
		foreach (var transactionId in portfolios)
			await SendPortfolioSnapshotAsync(user, transactionId, false,
				cancellationToken);
		var trades = tradesTask is null ? [] : await tradesTask;
		foreach (var (transactionId, subscription) in orders)
			await SendOrderSnapshotAsync(user, trades, subscription,
				transactionId, false, cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshotAsync(DriftUserResponse user,
		long transactionId, bool isForce, CancellationToken cancellationToken)
	{
		if (user?.Account is { } account)
		{
			var fingerprint = new AccountFingerprint(
				account.TotalCollateral.TryParseDriftDecimal() ?? 0m,
				account.FreeCollateral.TryParseDriftDecimal() ?? 0m,
				account.InitialMargin.TryParseDriftDecimal() ?? 0m,
				account.MaintenanceMargin.TryParseDriftDecimal() ?? 0m,
				account.Leverage.TryParseDriftDecimal() ?? 0m,
				account.Health.TryParseDriftDecimal() ?? 0m);
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
					DepoName = _accountAddress,
					ServerTime = ServerTime,
					OriginalTransactionId = transactionId,
				}
				.TryAdd(PositionChangeTypes.CurrentValue,
					fingerprint.TotalCollateral, true)
				.TryAdd(PositionChangeTypes.BlockedValue,
					(fingerprint.TotalCollateral -
						fingerprint.FreeCollateral).Max(0m), true)
				.TryAdd(PositionChangeTypes.Leverage,
					fingerprint.Leverage, true), cancellationToken);
		}

		foreach (var balance in user?.Balances ?? [])
		{
			if (balance?.Symbol.IsEmpty() != false)
				continue;
			var fingerprint = new BalanceFingerprint(
				balance.Balance.TryParseDriftDecimal() ?? 0m,
				balance.OpenOrders,
				balance.LiquidationPrice.TryParseDriftDecimal() ?? 0m);
			var key = transactionId.ToString(CultureInfo.InvariantCulture) +
				":" + balance.Symbol;
			var changed = isForce;
			using (_sync.EnterScope())
			{
				changed |= !_balanceFingerprints.TryGetValue(key,
					out var previous) || previous != fingerprint;
				_balanceFingerprints[key] = fingerprint;
			}
			if (changed)
				await SendOutMessageAsync(new PositionChangeMessage
				{
					PortfolioName = PortfolioName,
					SecurityId = balance.Symbol.ToDriftSecurityId(),
					DepoName = _accountAddress,
					ServerTime = ServerTime,
					OriginalTransactionId = transactionId,
				}
				.TryAdd(PositionChangeTypes.CurrentValue,
					fingerprint.Current, true)
				.TryAdd(PositionChangeTypes.LiquidationPrice,
					fingerprint.LiquidationPrice, true), cancellationToken);
		}

		foreach (var position in user?.Positions ?? [])
		{
			if (position?.Symbol.IsEmpty() != false)
				continue;
			var current = position.BaseAssetAmount.TryParseDriftDecimal() ?? 0m;
			var quote = position.QuoteEntryAmount.TryParseDriftDecimal() ?? 0m;
			var side = current >= 0 ? Sides.Buy : Sides.Sell;
			var average = current == 0 ? 0m : quote.Abs() / current.Abs();
			var fingerprint = new PositionFingerprint(current.Abs(), average,
				position.SettledPnl.TryParseDriftDecimal() ?? 0m,
				position.FeesAndFunding.TryParseDriftDecimal() ?? 0m,
				position.LiquidationPrice.TryParseDriftDecimal() ?? 0m, side);
			var key = transactionId.ToString(CultureInfo.InvariantCulture) +
				":" + position.Symbol;
			var changed = isForce;
			using (_sync.EnterScope())
			{
				changed |= !_positionFingerprints.TryGetValue(key,
					out var previous) || previous != fingerprint;
				_positionFingerprints[key] = fingerprint;
			}
			if (changed)
				await SendOutMessageAsync(new PositionChangeMessage
				{
					PortfolioName = PortfolioName,
					SecurityId = position.Symbol.ToDriftSecurityId(),
					DepoName = _accountAddress,
					ServerTime = ServerTime,
					OriginalTransactionId = transactionId,
					Side = side,
				}
				.TryAdd(PositionChangeTypes.CurrentValue,
					fingerprint.Current, true)
				.TryAdd(PositionChangeTypes.AveragePrice,
					fingerprint.AveragePrice, true)
				.TryAdd(PositionChangeTypes.RealizedPnL,
					fingerprint.SettledPnl, true)
				.TryAdd(PositionChangeTypes.LiquidationPrice,
					fingerprint.LiquidationPrice, true), cancellationToken);
		}
	}

	private async ValueTask SendOrderSnapshotAsync(DriftUserResponse user,
		DriftTrade[] trades, OrderStatusSubscription subscription,
		long transactionId, bool isForce, CancellationToken cancellationToken)
	{
		var orders = (user?.Orders ?? [])
			.Where(static order => order is not null && order.OrderId > 0)
			.Where(order => IsOrderMatch(order, subscription))
			.OrderBy(static order => order.OrderId)
			.Skip(subscription.Skip)
			.Take(subscription.Limit)
			.ToArray();
		var currentKeys = new HashSet<string>(StringComparer.Ordinal);
		foreach (var order in orders)
		{
			var key = OrderKey(transactionId, order.OrderId);
			currentKeys.Add(key);
			var message = CreateOrderMessage(order, transactionId);
			var fingerprint = new OrderFingerprint(message.OrderState ??
				OrderStates.Pending, message.OrderPrice,
				message.OrderVolume ?? 0m, message.Balance ?? 0m);
			var changed = isForce;
			using (_sync.EnterScope())
			{
				changed |= !_orderFingerprints.TryGetValue(key,
					out var previous) || previous != fingerprint;
				_orderFingerprints[key] = fingerprint;
				_knownOrders[key] = order;
			}
			if (changed)
				await SendOutMessageAsync(message, cancellationToken);
		}

		KeyValuePair<string, DriftOrder>[] missing;
		var prefix = transactionId.ToString(CultureInfo.InvariantCulture) + ":";
		using (_sync.EnterScope())
		{
			missing = [.. _knownOrders.Where(pair =>
				pair.Key.StartsWith(prefix, StringComparison.Ordinal) &&
				!currentKeys.Contains(pair.Key))];
			foreach (var (key, _) in missing)
			{
				_knownOrders.Remove(key);
				_orderFingerprints.Remove(key);
			}
		}
		foreach (var (_, order) in missing)
		{
			var message = CreateOrderMessage(order, transactionId);
			message.OrderState = OrderStates.Done;
			message.Balance = 0m;
			await SendOutMessageAsync(message, cancellationToken);
		}

		foreach (var trade in (trades ?? [])
			.Where(static trade => trade is not null && trade.Timestamp > 0)
			.OrderBy(static trade => trade.Timestamp))
			await SendAccountTradeAsync(trade, subscription, transactionId,
				cancellationToken);
	}

	private ExecutionMessage CreateOrderMessage(DriftOrder order,
		long originalTransactionId)
	{
		var volume = order.BaseAssetAmount.TryParseDriftDecimal() ?? 0m;
		var filled = order.BaseAssetAmountFilled.TryParseDriftDecimal() ?? 0m;
		var condition = new DriftOrderCondition
		{
			MarginMode = order.MarketType == DriftMarketTypes.Perpetual
				? DriftMarginModes.Cross
				: DriftMarginModes.Cross,
			IsReduceOnly = order.IsReduceOnly,
			IsPostOnly = order.IsPostOnly,
		};
		return new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToDriftSecurityId(),
			ServerTime = ServerTime,
			PortfolioName = PortfolioName,
			DepoName = _accountAddress,
			Side = order.Direction.ToStockSharpDirection(),
			OrderVolume = volume,
			Balance = (volume - filled).Max(0m),
			OrderPrice = order.Price.TryParseDriftDecimal() ?? 0m,
			OrderType = order.OrderType.ToStockSharpOrderType(),
			OrderState = order.Status.ToStockSharpOrderState(),
			OrderId = order.OrderId,
			TransactionId = order.OrderId,
			OriginalTransactionId = originalTransactionId,
			TimeInForce = order.OrderType.EqualsIgnoreCase("market")
				? TimeInForce.CancelBalance
				: TimeInForce.PutInQueue,
			PositionEffect = order.IsReduceOnly
				? OrderPositionEffects.CloseOnly
				: null,
			Condition = condition,
		};
	}

	private async ValueTask SendAccountTradeAsync(DriftTrade trade,
		OrderStatusSubscription subscription, long transactionId,
		CancellationToken cancellationToken)
	{
		var isTaker = string.Equals(trade.Taker, _accountAddress,
			StringComparison.Ordinal) || string.Equals(trade.User,
				_accountAddress, StringComparison.Ordinal) && trade.Maker.IsEmpty();
		var isMaker = string.Equals(trade.Maker, _accountAddress,
			StringComparison.Ordinal);
		if (!isTaker && !isMaker)
			return;
		var direction = isTaker
			? trade.TakerOrderDirection
			: trade.MakerOrderDirection;
		if (direction.IsEmpty())
			return;
		var side = direction.ToStockSharpDirection();
		var orderIdText = isTaker ? trade.TakerOrderId : trade.MakerOrderId;
		long? orderId = long.TryParse(orderIdText, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var parsedOrderId)
			? parsedOrderId
			: null;
		var symbol = trade.Symbol;
		if (symbol.IsEmpty())
			using (_sync.EnterScope())
				symbol = _markets.Values.FirstOrDefault(market =>
					market.MarketType == trade.MarketType &&
					market.MarketIndex == trade.MarketIndex)?.Symbol;
		if (symbol.IsEmpty() || !IsTradeMatch(symbol, orderId, side,
			subscription))
			return;
		var seenKey = transactionId.ToString(CultureInfo.InvariantCulture) +
			":" + trade.TransactionSignature + ":" +
			trade.TransactionIndex.ToString(CultureInfo.InvariantCulture);
		using (_sync.EnterScope())
			if (!_seenAccountTrades.Add(seenKey))
				return;
		var fee = (isTaker ? trade.TakerFee :
			trade.MakerFee ?? trade.MakerRebate).TryParseDriftDecimal();
		var time = trade.Timestamp.FromDriftSeconds();
		UpdateServerTime(time);
		long? tradeId = long.TryParse(trade.FillRecordId, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var parsedTradeId)
			? parsedTradeId
			: null;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = symbol.ToDriftSecurityId(),
			ServerTime = time,
			PortfolioName = PortfolioName,
			DepoName = _accountAddress,
			Side = side,
			OrderId = orderId,
			TradeId = tradeId,
			TradeStringId = trade.FillRecordId,
			TradePrice = trade.GetTradePrice(),
			TradeVolume = trade.BaseAssetAmountFilled.ParseDriftDecimal(
				"account trade volume"),
			Commission = fee,
			CommissionCurrency = GetMarket(symbol)?.QuoteAsset ?? "USDT",
			TransactionId = orderId ?? 0,
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}

	private OrderStatusSubscription CreateOrderSubscription(
		OrderStatusMessage message)
	{
		long? orderId = message.OrderId;
		if (orderId is null && !message.OrderStringId.IsEmpty())
			orderId = ResolveOrderId(null, message.OrderStringId);
		return new()
		{
			Symbol = message.SecurityId.SecurityCode.IsEmpty()
				? null
				: GetMarket(message.SecurityId).Symbol,
			OrderId = orderId,
			Side = message.Side,
			States = message.States ?? [],
			Skip = Math.Max(0, message.Skip ?? 0).To<int>(),
			Limit = (message.Count ?? HistoryLimit).Min(HistoryLimit).Max(1)
				.To<int>(),
		};
	}

	private static bool IsOrderMatch(DriftOrder order,
		OrderStatusSubscription filter)
	{
		if (!filter.Symbol.IsEmpty() && !order.Symbol.Equals(filter.Symbol,
			StringComparison.Ordinal))
			return false;
		if (filter.OrderId is long orderId && order.OrderId != orderId)
			return false;
		if (filter.Side is Sides side &&
			order.Direction.ToStockSharpDirection() != side)
			return false;
		return filter.States.Length == 0 ||
			filter.States.Contains(order.Status.ToStockSharpOrderState());
	}

	private static bool IsTradeMatch(string symbol, long? orderId, Sides side,
		OrderStatusSubscription filter)
		=> (filter.Symbol.IsEmpty() || filter.Symbol.Equals(symbol,
				StringComparison.Ordinal)) &&
			(filter.OrderId is null || filter.OrderId == orderId) &&
			(filter.Side is null || filter.Side == side);

	private void ValidatePortfolio(string portfolioName)
	{
		EnsureAccountReady();
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(PortfolioName))
			throw new InvalidOperationException(
				$"Unknown Drift portfolio '{portfolioName}'.");
	}

	private static long ResolveOrderId(long? orderId, string stringId)
	{
		if (orderId is > 0)
			return orderId.Value;
		if (long.TryParse(stringId, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
			return parsed;
		throw new InvalidOperationException(
			"Drift cancellation requires a numeric order ID.");
	}

	private void SchedulePrivatePoll()
	{
		using (_sync.EnterScope())
			_nextPrivatePoll = default;
	}

	private static string OrderKey(long transactionId, long orderId)
		=> transactionId.ToString(CultureInfo.InvariantCulture) + ":" +
			orderId.ToString(CultureInfo.InvariantCulture);
}
