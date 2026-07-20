namespace StockSharp.Bluefin;

public partial class BluefinMessageAdapter
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
			regMsg.Condition as BluefinOrderCondition ?? new(), regMsg.TillDate,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		ValidatePortfolio(replaceMsg.PortfolioName);
		EnsureTradingReady();
		var market = GetMarket(replaceMsg.SecurityId);
		var oldOrder = await ResolveOpenOrderAsync(
			replaceMsg.OldOrderStringId, replaceMsg.OldOrderId, market.Symbol,
			cancellationToken);
		await RestClient.CancelOrdersAsync(new()
		{
			Symbol = market.Symbol,
			OrderHashes = [oldOrder.OrderHash],
		}, cancellationToken);
		await PlaceOrderAsync(market, replaceMsg.TransactionId,
			replaceMsg.Side, replaceMsg.Volume.Abs(), replaceMsg.Price,
			replaceMsg.OrderType ?? OrderTypes.Limit, replaceMsg.TimeInForce,
			replaceMsg.PostOnly == true, replaceMsg.PositionEffect,
			replaceMsg.Condition as BluefinOrderCondition ?? new(),
			replaceMsg.TillDate, cancellationToken);
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
		await RestClient.CancelOrdersAsync(new()
		{
			Symbol = order.Symbol,
			OrderHashes = [order.OrderHash],
		}, cancellationToken);
		await SendCancelledOrderAsync(order.OrderHash, order.Symbol,
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
				"Bluefin bulk cancellation does not close positions.");
		var symbol = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(cancelMsg.SecurityId).Symbol;
		var orders = (await RestClient.GetOpenOrdersAsync(symbol,
			cancellationToken) ?? [])
			.Where(static order => order?.OrderHash.IsEmpty() == false)
			.Where(order => cancelMsg.Side is null ||
				order.Side.ToStockSharpSide() == cancelMsg.Side)
			.Where(order => cancelMsg.IsStop is null ||
				IsStopOrder(order.Type) == cancelMsg.IsStop.Value)
			.ToArray();
		foreach (var group in orders.GroupBy(static order => order.Symbol,
			StringComparer.Ordinal))
			foreach (var chunk in group.Chunk(10))
				await RestClient.CancelOrdersAsync(new()
				{
					Symbol = group.Key,
					OrderHashes = [.. chunk.Select(static order => order.OrderHash)],
				}, cancellationToken);
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
			BoardCode = BoardCodes.Bluefin,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		var account = await RestClient.GetAccountAsync(_accountAddress,
			cancellationToken);
		await SendPortfolioSnapshotAsync(account, lookupMsg.TransactionId, true,
			cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId,
				cancellationToken);
			return;
		}
		await AddPortfolioSubscriptionAsync(lookupMsg.TransactionId,
			cancellationToken);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId,
			cancellationToken);
		EnsureTradingReady();
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
		var openTask = RestClient.GetOpenOrdersAsync(subscription.Symbol,
			cancellationToken).AsTask();
		var tradesTask = RestClient.GetAccountTradesAsync(subscription.Symbol,
			subscription.From, subscription.To, subscription.Limit,
			cancellationToken).AsTask();
		await Task.WhenAll(openTask, tradesTask);
		await SendOrderSnapshotAsync(await openTask, await tradesTask,
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

	private async ValueTask PlaceOrderAsync(BluefinMarket market,
		long transactionId, Sides side, decimal volume, decimal price,
		OrderTypes orderType, TimeInForce? timeInForce, bool isPostOnly,
		OrderPositionEffects? positionEffect, BluefinOrderCondition condition,
		DateTime? tillDate, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		ArgumentNullException.ThrowIfNull(condition);
		if (!market.Status.EqualsIgnoreCase("ACTIVE") &&
			!market.Status.EqualsIgnoreCase("BETA"))
			throw new InvalidOperationException(
				$"Bluefin market '{market.Symbol}' is not active.");
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market or
			OrderTypes.Conditional))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		if (volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(volume));
		var volumeStep = market.StepSizeE9.ParseE9("volume step");
		if (volumeStep <= 0 || volume % volumeStep != 0)
			throw new ArgumentOutOfRangeException(nameof(volume), volume,
				$"Bluefin order volume must be a multiple of {volumeStep}.");
		var isMarket = orderType == OrderTypes.Market ||
			orderType == OrderTypes.Conditional && price <= 0;
		if (!isMarket)
		{
			var priceStep = market.TickSizeE9.ParseE9("price step");
			if (price <= 0 || priceStep <= 0 || price % priceStep != 0)
				throw new ArgumentOutOfRangeException(nameof(price), price,
					$"Bluefin order price must be a positive multiple of " +
					$"{priceStep}.");
		}
		else
			price = 0m;
		var minimum = market.MinimumOrderQuantityE9.TryParseE9();
		var maximum = (isMarket
			? market.MaximumMarketOrderQuantityE9
			: market.MaximumLimitOrderQuantityE9).TryParseE9();
		if (minimum is decimal min && volume < min ||
			maximum is decimal max && max > 0 && volume > max)
			throw new ArgumentOutOfRangeException(nameof(volume), volume,
				"Bluefin order volume is outside the market limits.");
		if (isPostOnly && isMarket)
			throw new InvalidOperationException(
				"A Bluefin market order cannot be post-only.");

		condition.IsReduceOnly |=
			positionEffect == OrderPositionEffects.CloseOnly;
		var trigger = condition.TriggerPrice;
		if (orderType == OrderTypes.Conditional && trigger is not > 0)
			throw new InvalidOperationException(
				"A Bluefin conditional order requires a trigger price.");
		if (trigger is decimal triggerPrice)
		{
			var priceStep = market.TickSizeE9.ParseE9("price step");
			if (triggerPrice <= 0 || triggerPrice % priceStep != 0)
				throw new ArgumentOutOfRangeException(nameof(condition.TriggerPrice),
					triggerPrice, "Bluefin trigger price must be a positive " +
					$"multiple of {priceStep}.");
		}

		var leverage = condition.Leverage ??
			market.DefaultLeverageE9.TryParseE9() ?? 1m;
		if (leverage < 0)
			throw new ArgumentOutOfRangeException(nameof(condition.Leverage));
		var signedAt = DateTime.UtcNow;
		var expiry = (tillDate ?? signedAt + OrderExpiry).ToUniversalTime();
		if (expiry <= signedAt || expiry > signedAt + TimeSpan.FromDays(30))
			throw new ArgumentOutOfRangeException(nameof(tillDate), tillDate,
				"Bluefin order expiry must be in the future and no more than " +
				"30 days away.");
		var fields = new BluefinCreateOrderSignedFields
		{
			Symbol = market.Symbol,
			AccountAddress = _accountAddress,
			PriceE9 = price.ToE9(nameof(price)),
			QuantityE9 = volume.ToE9(nameof(volume)),
			Side = side.ToBluefinSide(),
			LeverageE9 = leverage.ToE9(nameof(leverage)),
			IsIsolated = market.IsIsolatedOnly || condition.IsIsolated,
			Salt = NextSalt(),
			IdsId = _contractsConfig.IdsId,
			ExpiresAtMillis = expiry.ToBluefinMilliseconds(),
			SignedAtMillis = signedAt.ToBluefinMilliseconds(),
		};
		var apiType = GetOrderType(isMarket, trigger is not null,
			condition.IsTakeProfit);
		var request = new BluefinCreateOrderRequest
		{
			SignedFields = fields,
			Signature = Signer.SignOrder(fields),
			ClientOrderId = transactionId.ToString(CultureInfo.InvariantCulture),
			Type = apiType,
			IsReduceOnly = condition.IsReduceOnly,
			IsPostOnly = isPostOnly,
			TimeInForce = timeInForce.ToBluefinTimeInForce(
				isMarket ? OrderTypes.Market : OrderTypes.Limit),
			TriggerPriceE9 = trigger?.ToE9(nameof(condition.TriggerPrice)),
			SelfTradePreventionType = condition.SelfTradePrevention.ToBluefin(),
		};
		var response = await RestClient.CreateOrderAsync(request,
			cancellationToken);
		var orderId = response?.OrderHash.ThrowIfEmpty("Bluefin order hash");
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToBluefinSecurityId(),
			ServerTime = ServerTime,
			PortfolioName = PortfolioName,
			DepoName = _accountAddress,
			Side = side,
			OrderVolume = volume,
			Balance = volume,
			OrderPrice = price,
			OrderType = trigger is not null
				? OrderTypes.Conditional
				: isMarket ? OrderTypes.Market : OrderTypes.Limit,
			OrderState = OrderStates.Pending,
			OrderStringId = orderId,
			TransactionId = transactionId,
			OriginalTransactionId = transactionId,
			TimeInForce = timeInForce ?? (isMarket
				? TimeInForce.CancelBalance
				: TimeInForce.PutInQueue),
			ExpiryDate = expiry,
			PositionEffect = condition.IsReduceOnly
				? OrderPositionEffects.CloseOnly
				: null,
			Condition = condition,
		}, cancellationToken);
		SchedulePrivatePoll();
	}

	private async ValueTask AddPortfolioSubscriptionAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		var subscribe = false;
		using (_sync.EnterScope())
		{
			subscribe = _portfolioSubscriptions.Count == 0 &&
				_orderSubscriptions.Count == 0;
			_portfolioSubscriptions.Add(transactionId);
		}
		try
		{
			if (subscribe && !RestClient.AccessToken.IsEmpty())
				await SocketClient.SubscribeAccountAsync(cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_portfolioSubscriptions.Remove(transactionId);
			throw;
		}
	}

	private async ValueTask RemovePortfolioSubscriptionAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		var unsubscribe = false;
		using (_sync.EnterScope())
			if (_portfolioSubscriptions.Remove(transactionId))
				unsubscribe = _portfolioSubscriptions.Count == 0 &&
					_orderSubscriptions.Count == 0;
		if (unsubscribe && !RestClient.AccessToken.IsEmpty())
			await SocketClient.UnsubscribeAccountAsync(cancellationToken);
	}

	private async ValueTask AddOrderSubscriptionAsync(long transactionId,
		OrderSubscription subscription, CancellationToken cancellationToken)
	{
		var subscribe = false;
		using (_sync.EnterScope())
		{
			subscribe = _portfolioSubscriptions.Count == 0 &&
				_orderSubscriptions.Count == 0;
			_orderSubscriptions.Add(transactionId, subscription);
		}
		try
		{
			if (subscribe)
				await SocketClient.SubscribeAccountAsync(cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_orderSubscriptions.Remove(transactionId);
			throw;
		}
	}

	private async ValueTask RemoveOrderSubscriptionAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		var unsubscribe = false;
		using (_sync.EnterScope())
			if (_orderSubscriptions.Remove(transactionId))
				unsubscribe = _portfolioSubscriptions.Count == 0 &&
					_orderSubscriptions.Count == 0;
		if (unsubscribe)
			await SocketClient.UnsubscribeAccountAsync(cancellationToken);
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
		Task<BluefinAccount> accountTask = null;
		if (portfolios.Length > 0)
			accountTask = RestClient.GetAccountAsync(_accountAddress,
				cancellationToken).AsTask();
		if (accountTask is not null)
		{
			var account = await accountTask;
			foreach (var transactionId in portfolios)
				await SendPortfolioSnapshotAsync(account, transactionId, false,
					cancellationToken);
		}
		foreach (var (transactionId, subscription) in orders)
		{
			var openTask = RestClient.GetOpenOrdersAsync(subscription.Symbol,
				cancellationToken).AsTask();
			var tradesTask = RestClient.GetAccountTradesAsync(subscription.Symbol,
				subscription.From, subscription.To, subscription.Limit,
				cancellationToken).AsTask();
			await Task.WhenAll(openTask, tradesTask);
			await SendOrderSnapshotAsync(await openTask, await tradesTask,
				subscription, transactionId, false, cancellationToken);
		}
	}

	private async ValueTask SendPortfolioSnapshotAsync(BluefinAccount account,
		long transactionId, bool isForce, CancellationToken cancellationToken)
	{
		if (account is null)
			throw new InvalidDataException(
				"Bluefin returned no account snapshot.");
		var time = account.UpdatedAtMillis > 0
			? account.UpdatedAtMillis.FromBluefinMilliseconds()
			: ServerTime;
		UpdateServerTime(time);
		var current = account.TotalAccountValueE9.TryParseE9() ??
			account.CrossAccountValueE9.TryParseE9() ?? 0m;
		var available = account.MarginAvailableE9.TryParseE9() ?? 0m;
		var fingerprint = new AccountFingerprint(current, available,
			account.TotalOrderMarginRequiredE9.TryParseE9() ??
				account.CrossMarginRequiredE9.TryParseE9() ?? 0m,
			account.CrossMaintenanceMarginRequiredE9.TryParseE9() ?? 0m,
			account.TotalUnrealizedPnlE9.TryParseE9() ?? 0m,
			account.CrossLeverageE9.TryParseE9() ?? 0m);
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
				ServerTime = time,
				OriginalTransactionId = transactionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue,
				fingerprint.Current, true)
			.TryAdd(PositionChangeTypes.BlockedValue,
				fingerprint.Blocked, true)
			.TryAdd(PositionChangeTypes.VariationMargin,
				fingerprint.Maintenance, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL,
				fingerprint.UnrealizedPnl, true)
			.TryAdd(PositionChangeTypes.Leverage,
				fingerprint.Leverage, true), cancellationToken);

		foreach (var asset in account.Assets ?? [])
			await SendAssetAsync(asset, transactionId, isForce,
				cancellationToken);
		foreach (var position in account.Positions ?? [])
			await SendPositionAsync(position, transactionId, isForce,
				cancellationToken);
	}

	private async ValueTask SendAssetAsync(BluefinAccountAsset asset,
		long transactionId, bool isForce, CancellationToken cancellationToken)
	{
		if (asset?.Symbol.IsEmpty() != false)
			return;
		var fingerprint = new AssetFingerprint(
			asset.QuantityE9.TryParseE9() ?? 0m,
			asset.MaximumWithdrawQuantityE9.TryParseE9() ?? 0m);
		var key = transactionId.ToString(CultureInfo.InvariantCulture) + ":" +
			asset.Symbol;
		var changed = isForce;
		using (_sync.EnterScope())
		{
			changed |= !_assetFingerprints.TryGetValue(key, out var previous) ||
				previous != fingerprint;
			_assetFingerprints[key] = fingerprint;
		}
		if (!changed)
			return;
		var time = asset.UpdatedAtMillis > 0
			? asset.UpdatedAtMillis.FromBluefinMilliseconds()
			: ServerTime;
		UpdateServerTime(time);
		await SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = PortfolioName,
			SecurityId = new()
			{
				SecurityCode = asset.Symbol,
				BoardCode = BoardCodes.Bluefin,
			},
			DepoName = _accountAddress,
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue,
			fingerprint.Current, true), cancellationToken);
	}

	private async ValueTask SendPositionAsync(BluefinPosition position,
		long transactionId, bool isForce, CancellationToken cancellationToken)
	{
		if (position?.Symbol.IsEmpty() != false)
			return;
		var current = position.SizeE9.TryParseE9()?.Abs() ?? 0m;
		if (position.Side.EqualsIgnoreCase("UNSPECIFIED") && current == 0)
			return;
		var side = position.Side.ToStockSharpSide();
		var fingerprint = new PositionFingerprint(
			current,
			position.AverageEntryPriceE9.TryParseE9() ?? 0m,
			position.UnrealizedPnlE9.TryParseE9() ?? 0m,
			position.LiquidationPriceE9.TryParseE9() ?? 0m,
			position.ClientSetLeverageE9.TryParseE9() ?? 0m,
			side, position.IsIsolated);
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
		var time = position.UpdatedAtMillis > 0
			? position.UpdatedAtMillis.FromBluefinMilliseconds()
			: ServerTime;
		UpdateServerTime(time);
		await SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = PortfolioName,
			SecurityId = position.Symbol.ToBluefinSecurityId(),
			DepoName = _accountAddress,
			ServerTime = time,
			OriginalTransactionId = transactionId,
			Side = side,
		}
		.TryAdd(PositionChangeTypes.CurrentValue,
			fingerprint.Current, true)
		.TryAdd(PositionChangeTypes.AveragePrice,
			fingerprint.AveragePrice, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL,
			fingerprint.UnrealizedPnl, true)
		.TryAdd(PositionChangeTypes.LiquidationPrice,
			fingerprint.LiquidationPrice, true)
		.TryAdd(PositionChangeTypes.Leverage,
			fingerprint.Leverage, true), cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(BluefinOrder[] orders,
		BluefinTrade[] trades, OrderSubscription subscription,
		long transactionId, bool isForce,
		CancellationToken cancellationToken)
	{
		foreach (var order in (orders ?? [])
			.Where(static order => order?.OrderHash.IsEmpty() == false)
			.Where(order => IsOrderMatch(order, subscription))
			.OrderBy(static order => order.UpdatedAtMillis)
			.Skip(subscription.Skip)
			.Take(subscription.Limit))
		{
			var message = CreateOrderMessage(order, transactionId);
			var fingerprint = new OrderFingerprint(message.OrderState ??
				OrderStates.Pending, message.OrderPrice,
				message.OrderVolume ?? 0m, message.Balance ?? 0m);
			var key = transactionId.ToString(CultureInfo.InvariantCulture) +
				":" + order.OrderHash;
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
		foreach (var trade in (trades ?? [])
			.Where(static trade => trade is not null && trade.ExecutedAtMillis > 0)
			.OrderBy(static trade => trade.ExecutedAtMillis))
			await SendAccountTradeAsync(trade, subscription, transactionId,
				cancellationToken);
	}

	private ExecutionMessage CreateOrderMessage(BluefinOrder order,
		long originalTransactionId)
	{
		var volume = order.QuantityE9.TryParseE9() ?? 0m;
		var balance = order.RemainingQuantityE9.TryParseE9() ??
			(volume - (order.FilledQuantityE9.TryParseE9() ?? 0m)).Max(0m);
		var timeValue = order.UpdatedAtMillis > 0
			? order.UpdatedAtMillis
			: order.OrderTimeAtMillis > 0
				? order.OrderTimeAtMillis
				: order.CreatedAtMillis;
		var time = timeValue > 0
			? timeValue.FromBluefinMilliseconds()
			: ServerTime;
		UpdateServerTime(time);
		var condition = new BluefinOrderCondition
		{
			IsReduceOnly = order.IsReduceOnly,
			IsIsolated = order.IsIsolated,
			Leverage = order.LeverageE9.TryParseE9(),
			TriggerPrice = order.TriggerPriceE9.TryParseE9(),
			IsTakeProfit = order.Type?.StartsWith("TAKE_PROFIT_",
				StringComparison.OrdinalIgnoreCase) == true,
		};
		return new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToBluefinSecurityId(),
			ServerTime = time,
			PortfolioName = PortfolioName,
			DepoName = _accountAddress,
			Side = order.Side.ToStockSharpSide(),
			OrderVolume = volume,
			Balance = balance,
			OrderPrice = order.PriceE9.TryParseE9() ?? 0m,
			OrderType = order.Type.ToStockSharpOrderType(),
			OrderState = order.Status.ToStockSharpOrderState(),
			OrderStringId = order.OrderHash,
			TransactionId = ParseTransactionId(order.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
			TimeInForce = order.TimeInForce?.ToUpperInvariant() switch
			{
				"IOC" => TimeInForce.CancelBalance,
				"FOK" => TimeInForce.MatchOrCancel,
				_ => TimeInForce.PutInQueue,
			},
			ExpiryDate = order.ExpiresAtMillis > 0
				? order.ExpiresAtMillis.FromBluefinMilliseconds()
				: null,
			PositionEffect = order.IsReduceOnly
				? OrderPositionEffects.CloseOnly
				: null,
			Condition = condition,
		};
	}

	private async ValueTask SendAccountTradeAsync(BluefinTrade trade,
		OrderSubscription subscription, long transactionId,
		CancellationToken cancellationToken)
	{
		if (trade.Symbol.IsEmpty() || !IsTradeMatch(trade, subscription))
			return;
		var seenKey = transactionId.ToString(CultureInfo.InvariantCulture) +
			":" + trade.Id;
		using (_sync.EnterScope())
			if (!_seenAccountTrades.Add(seenKey))
				return;
		var time = trade.ExecutedAtMillis.FromBluefinMilliseconds();
		UpdateServerTime(time);
		long? tradeId = long.TryParse(trade.Id, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var parsedTradeId)
			? parsedTradeId
			: null;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = trade.Symbol.ToBluefinSecurityId(),
			ServerTime = time,
			PortfolioName = PortfolioName,
			DepoName = _accountAddress,
			Side = trade.Side.ToStockSharpSide(),
			OrderStringId = trade.OrderHash,
			TradeId = tradeId,
			TradeStringId = trade.Id,
			TradePrice = trade.PriceE9.ParseE9("account trade price"),
			TradeVolume = trade.QuantityE9.ParseE9("account trade volume"),
			Commission = trade.TradingFeeE9.TryParseE9(),
			CommissionCurrency = trade.TradingFeeAsset ?? "USDC",
			TransactionId = ParseTransactionId(trade.ClientOrderId),
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}

	private async ValueTask OnAccountMessageAsync(
		BluefinAccountStreamMessage message,
		CancellationToken cancellationToken)
	{
		if (message?.Payload is not { } payload)
			return;
		switch (message.Event)
		{
			case "AccountUpdate":
				await OnAccountUpdateAsync(payload, cancellationToken);
				break;
			case "AccountPositionUpdate":
				await OnPositionUpdateAsync(payload, cancellationToken);
				break;
			case "AccountOrderUpdate":
				await OnOrderUpdateAsync(payload, cancellationToken);
				break;
			case "AccountTradeUpdate":
			case "AccountAggregatedTradeUpdate":
				await OnAccountTradeUpdateAsync(payload.Trade, cancellationToken);
				break;
			case "AccountCommandFailureUpdate":
				await SendOutErrorAsync(new BluefinApiException(
					"Bluefin account command failed: " +
					(payload.Reason ?? payload.ReasonCode ??
						payload.FailedCommandType ?? "unknown reason")),
					cancellationToken);
				break;
		}
	}

	private async ValueTask OnAccountUpdateAsync(
		BluefinAccountStreamPayload payload,
		CancellationToken cancellationToken)
	{
		long[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _portfolioSubscriptions];
		var account = new BluefinAccount
		{
			AccountAddress = _accountAddress,
			CrossEffectiveBalanceE9 = payload.CrossEffectiveBalanceE9,
			CrossMarginRequiredE9 = payload.CrossMarginRequiredE9,
			TotalOrderMarginRequiredE9 = payload.TotalOrderMarginRequiredE9,
			MarginAvailableE9 = payload.MarginAvailableE9,
			TotalUnrealizedPnlE9 = payload.TotalUnrealizedPnlE9,
			CrossAccountValueE9 = payload.CrossAccountValueE9,
			TotalAccountValueE9 = payload.TotalAccountValueE9,
			UpdatedAtMillis = payload.UpdatedAtMillis,
			Assets = payload.Assets,
		};
		foreach (var transactionId in subscriptions)
			await SendPortfolioSnapshotAsync(account, transactionId, false,
				cancellationToken);
	}

	private async ValueTask OnPositionUpdateAsync(
		BluefinAccountStreamPayload payload,
		CancellationToken cancellationToken)
	{
		if (payload.Symbol.IsEmpty())
			return;
		long[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _portfolioSubscriptions];
		var position = new BluefinPosition
		{
			Symbol = payload.Symbol,
			AverageEntryPriceE9 = payload.AverageEntryPriceE9,
			ClientSetLeverageE9 = payload.ClientSetLeverageE9,
			LiquidationPriceE9 = payload.LiquidationPriceE9,
			MarkPriceE9 = payload.MarkPriceE9,
			NotionalValueE9 = payload.NotionalValueE9,
			SizeE9 = payload.SizeE9,
			UnrealizedPnlE9 = payload.UnrealizedPnlE9,
			Side = payload.Side,
			MarginRequiredE9 = payload.MarginRequiredE9,
			MaintenanceMarginE9 = payload.MaintenanceMarginE9,
			IsIsolated = payload.IsIsolated,
			IsolatedMarginE9 = payload.IsolatedMarginE9,
			UpdatedAtMillis = payload.UpdatedAtMillis,
		};
		foreach (var transactionId in subscriptions)
			await SendPositionAsync(position, transactionId, false,
				cancellationToken);
	}

	private async ValueTask OnOrderUpdateAsync(BluefinOrder order,
		CancellationToken cancellationToken)
	{
		if (order?.OrderHash.IsEmpty() != false)
			return;
		KeyValuePair<long, OrderSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _orderSubscriptions];
		foreach (var (transactionId, subscription) in subscriptions)
			if (IsOrderMatch(order, subscription))
				await SendOutMessageAsync(CreateOrderMessage(order, transactionId),
					cancellationToken);
	}

	private async ValueTask OnAccountTradeUpdateAsync(BluefinTrade trade,
		CancellationToken cancellationToken)
	{
		if (trade is null)
			return;
		KeyValuePair<long, OrderSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _orderSubscriptions];
		foreach (var (transactionId, subscription) in subscriptions)
			await SendAccountTradeAsync(trade, subscription, transactionId,
				cancellationToken);
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

	private static bool IsOrderMatch(BluefinOrder order,
		OrderSubscription filter)
		=> (filter.Symbol.IsEmpty() || order.Symbol.Equals(filter.Symbol,
				StringComparison.Ordinal)) &&
			(filter.OrderId.IsEmpty() || order.OrderHash.Equals(filter.OrderId,
				StringComparison.Ordinal) || order.ClientOrderId.Equals(
					filter.OrderId, StringComparison.Ordinal)) &&
			(filter.Side is null || order.Side.ToStockSharpSide() == filter.Side) &&
			(filter.States.Length == 0 ||
				filter.States.Contains(order.Status.ToStockSharpOrderState()));

	private static bool IsTradeMatch(BluefinTrade trade,
		OrderSubscription filter)
		=> (filter.Symbol.IsEmpty() || filter.Symbol.Equals(trade.Symbol,
				StringComparison.Ordinal)) &&
			(filter.OrderId.IsEmpty() || filter.OrderId.Equals(trade.OrderHash,
				StringComparison.Ordinal) || filter.OrderId.Equals(
					trade.ClientOrderId, StringComparison.Ordinal)) &&
			(filter.Side is null || trade.Side.ToStockSharpSide() == filter.Side) &&
			(filter.From is null || trade.ExecutedAtMillis > 0 &&
				trade.ExecutedAtMillis.FromBluefinMilliseconds() >= filter.From) &&
			(filter.To is null || trade.ExecutedAtMillis > 0 &&
				trade.ExecutedAtMillis.FromBluefinMilliseconds() <= filter.To);

	private void ValidatePortfolio(string portfolioName)
	{
		EnsureAccountReady();
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(PortfolioName))
			throw new InvalidOperationException(
				$"Unknown Bluefin portfolio '{portfolioName}'.");
	}

	private async ValueTask<BluefinOrder> ResolveOpenOrderAsync(string stringId,
		long? numericId, string symbol, CancellationToken cancellationToken)
	{
		var orderId = ResolveOrderId(stringId, numericId);
		var order = (await RestClient.GetOpenOrdersAsync(symbol,
			cancellationToken) ?? []).FirstOrDefault(item =>
				item?.OrderHash.Equals(orderId, StringComparison.Ordinal) == true ||
				item?.ClientOrderId.Equals(orderId,
					StringComparison.Ordinal) == true);
		if (order?.OrderHash.IsEmpty() != false || order.Symbol.IsEmpty())
			throw new InvalidOperationException(
				$"Bluefin open order '{orderId}' was not found.");
		return order;
	}

	private ValueTask SendCancelledOrderAsync(string orderId, string symbol,
		long transactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToBluefinSecurityId(),
			ServerTime = ServerTime,
			PortfolioName = PortfolioName,
			DepoName = _accountAddress,
			OrderStringId = orderId,
			OrderState = OrderStates.Done,
			TransactionId = transactionId,
			OriginalTransactionId = transactionId,
		}, cancellationToken);

	private string NextSalt()
	{
		var candidate = checked(DateTime.UtcNow.ToBluefinMilliseconds() +
			RandomNumberGenerator.GetInt32(1_000_000));
		using (_sync.EnterScope())
		{
			_lastSalt = Math.Max(candidate, _lastSalt + 1);
			return _lastSalt.ToString(CultureInfo.InvariantCulture);
		}
	}

	private void SchedulePrivatePoll()
	{
		using (_sync.EnterScope())
			_nextPrivatePoll = default;
	}

	private static string ResolveOrderId(string stringId, long? numericId)
	{
		if (!stringId.IsEmpty())
			return stringId.Trim();
		if (numericId is > 0)
			return numericId.Value.ToString(CultureInfo.InvariantCulture);
		throw new InvalidOperationException(
			"Bluefin cancellation requires an order hash or client order ID.");
	}

	private static long ParseTransactionId(string value)
		=> long.TryParse(value, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var parsed) && parsed > 0
			? parsed
			: 0;

	private static string GetOrderType(bool isMarket, bool isConditional,
		bool isTakeProfit)
	{
		if (!isConditional)
			return isMarket ? "MARKET" : "LIMIT";
		if (isTakeProfit)
			return isMarket ? "TAKE_PROFIT_MARKET" : "TAKE_PROFIT_LIMIT";
		return isMarket ? "STOP_MARKET" : "STOP_LIMIT";
	}

	private static bool IsStopOrder(string value)
		=> value?.Contains("STOP", StringComparison.OrdinalIgnoreCase) == true ||
			value?.Contains("TAKE_PROFIT", StringComparison.OrdinalIgnoreCase) ==
				true;
}

static class BluefinOrderConditionExtensions
{
	public static string ToBluefin(
		this BluefinSelfTradePreventionTypes value)
		=> value switch
		{
			BluefinSelfTradePreventionTypes.Unspecified => null,
			BluefinSelfTradePreventionTypes.Taker => "TAKER",
			BluefinSelfTradePreventionTypes.Maker => "MAKER",
			BluefinSelfTradePreventionTypes.Both => "BOTH",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
		};
}
