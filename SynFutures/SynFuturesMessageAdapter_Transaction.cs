namespace StockSharp.SynFutures;

public partial class SynFuturesMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(regMsg.PortfolioName);
		if (!regMsg.UserOrderId.IsEmpty())
			throw new NotSupportedException(
				"SynFutures contracts do not accept client order identifiers.");
		var market = GetMarket(regMsg.SecurityId);
		var condition = regMsg.Condition as SynFuturesOrderCondition ?? new()
		{
			Leverage = DefaultLeverage,
		};
		ValidateLeverage(market, condition.Leverage);
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Market or OrderTypes.Limit))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		ValidateTimeInForce(orderType, regMsg.TimeInForce, regMsg.TillDate);
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Volume),
				regMsg.Volume, "SynFutures order volume must be positive.");

		var signedSize = volume.ToBaseUnits(18, nameof(regMsg.Volume)) *
			(regMsg.Side == Sides.Buy ? BigInteger.One : -BigInteger.One);
		await ValidatePositionEffectAsync(market, signedSize,
			regMsg.PositionEffect, cancellationToken);
		if (orderType == OrderTypes.Market)
			await RegisterMarketOrderAsync(regMsg, market, condition, signedSize,
				volume, cancellationToken);
		else
			await RegisterLimitOrderAsync(regMsg, market, condition, signedSize,
				volume, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(replaceMsg.PortfolioName);
		var orderId = ResolveOrderId(replaceMsg.OldOrderStringId,
			replaceMsg.OldOrderId, replaceMsg.UserOrderId);
		var parsed = SynFuturesExtensions.ParseOrderKey(orderId);
		var market = GetMarket(parsed.Instrument, parsed.Expiry) ??
			throw new InvalidOperationException(
				"Unknown SynFutures market in order '" + orderId + "'.");
		if (!replaceMsg.SecurityId.SecurityCode.IsEmpty() &&
			GetMarket(replaceMsg.SecurityId).InstrumentAddress !=
				market.InstrumentAddress)
			throw new InvalidOperationException(
				"Replacement security does not match the SynFutures order.");
		var portfolio = await ApiClient.GetPortfolioAsync(
			RpcClient.WalletAddress, cancellationToken);
		var existing = FindOpenOrder(portfolio, market, parsed.Tick,
			parsed.Nonce) ?? throw new InvalidOperationException(
				"SynFutures order '" + orderId + "' is no longer active.");
		var currentSize = existing.Size.ParseIntegerOrZero("order size");
		var side = currentSize >= 0 ? Sides.Buy : Sides.Sell;
		var volume = replaceMsg.Volume > 0
			? replaceMsg.Volume.Abs()
			: BigInteger.Abs(currentSize).FromBaseUnits(18);
		var price = replaceMsg.Price > 0
			? replaceMsg.Price
			: GetOrderPrice(existing);
		var condition = replaceMsg.Condition as SynFuturesOrderCondition ?? new()
		{
			Leverage = DefaultLeverage,
		};
		ValidateLeverage(market, condition.Leverage);

		var cancelReceipt = await RpcClient.SendAndWaitAsync(
			RpcClient.CreateCancelTransaction(market, [parsed.Tick],
				GetDeadline()), TransactionTimeout, cancellationToken);
		var cancelTime = await RpcClient.GetReceiptTimeAsync(cancelReceipt,
			cancellationToken);
		await SendOutMessageAsync(CreateCancelledOrderMessage(market, orderId,
			side, BigInteger.Abs(currentSize).FromBaseUnits(18),
			GetOrderPrice(existing), cancelTime, replaceMsg.TransactionId,
			SynFuturesRpcClient.GetCommission(cancelReceipt)), cancellationToken);

		var signedSize = volume.ToBaseUnits(18, nameof(replaceMsg.Volume)) *
			(side == Sides.Buy ? BigInteger.One : -BigInteger.One);
		var placed = await PlaceLimitAsync(market, signedSize, price,
			condition, cancellationToken);
		var newOrderId = SynFuturesExtensions.CreateOrderKey(
			market.InstrumentAddress, market.Expiry, placed.Event.Tick,
			placed.Event.Nonce);
		TrackOrder(newOrderId, replaceMsg.TransactionId);
		await SendOutMessageAsync(CreateActiveOrderMessage(market, newOrderId,
			side, volume, placed.Price, replaceMsg.TransactionId,
			placed.Time, condition,
			SynFuturesRpcClient.GetCommission(placed.Receipt)), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		var orderId = ResolveOrderId(cancelMsg.OrderStringId, cancelMsg.OrderId,
			cancelMsg.UserOrderId);
		var parsed = SynFuturesExtensions.ParseOrderKey(orderId);
		var market = GetMarket(parsed.Instrument, parsed.Expiry) ??
			throw new InvalidOperationException(
				"Unknown SynFutures market in order '" + orderId + "'.");
		var portfolio = await ApiClient.GetPortfolioAsync(
			RpcClient.WalletAddress, cancellationToken);
		var order = FindOpenOrder(portfolio, market, parsed.Tick,
			parsed.Nonce) ?? throw new InvalidOperationException(
				"SynFutures order '" + orderId + "' is no longer active.");
		var size = order.Size.ParseIntegerOrZero("order size");
		var receipt = await RpcClient.SendAndWaitAsync(
			RpcClient.CreateCancelTransaction(market, [parsed.Tick],
				GetDeadline()), TransactionTimeout, cancellationToken);
		var time = await RpcClient.GetReceiptTimeAsync(receipt,
			cancellationToken);
		using (_sync.EnterScope())
			_knownOrders.Remove(orderId);
		await SendOutMessageAsync(CreateCancelledOrderMessage(market, orderId,
			size >= 0 ? Sides.Buy : Sides.Sell,
			BigInteger.Abs(size).FromBaseUnits(18), GetOrderPrice(order), time,
			cancelMsg.TransactionId, SynFuturesRpcClient.GetCommission(receipt)),
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"SynFutures group cancellation does not close positions.");
		var portfolio = await ApiClient.GetPortfolioAsync(
			RpcClient.WalletAddress, cancellationToken);
		var marketFilter = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(cancelMsg.SecurityId);
		var entries = GetOpenOrders(portfolio)
			.Where(entry => marketFilter is null ||
				entry.Market.InstrumentAddress == marketFilter.InstrumentAddress)
			.Where(entry => cancelMsg.Side is null ||
				(entry.Order.Size.ParseIntegerOrZero() >= 0
					? Sides.Buy : Sides.Sell) == cancelMsg.Side)
			.ToArray();
		foreach (var group in entries.GroupBy(static entry =>
			PairKey(entry.Market.InstrumentAddress, entry.Market.Expiry)))
		{
			var market = group.First().Market;
			var tickGroups = group.GroupBy(static entry => entry.Order.Tick)
				.ToArray();
			for (var index = 0; index < tickGroups.Length; index += 8)
			{
				var batch = tickGroups.Skip(index).Take(8).ToArray();
				var ticks = batch.Select(static tickGroup => tickGroup.Key)
					.ToArray();
				var receipt = await RpcClient.SendAndWaitAsync(
					RpcClient.CreateCancelTransaction(market, ticks, GetDeadline()),
					TransactionTimeout, cancellationToken);
				var time = await RpcClient.GetReceiptTimeAsync(receipt,
					cancellationToken);
				var commission = SynFuturesRpcClient.GetCommission(receipt);
				foreach (var entry in batch.SelectMany(static tickGroup => tickGroup))
				{
					var size = entry.Order.Size.ParseIntegerOrZero("order size");
					var orderId = GetOrderId(market, entry.Order);
					await SendOutMessageAsync(CreateCancelledOrderMessage(market,
						orderId, size >= 0 ? Sides.Buy : Sides.Sell,
						BigInteger.Abs(size).FromBaseUnits(18),
						GetOrderPrice(entry.Order), time, cancelMsg.TransactionId,
						commission), cancellationToken);
				}
			}
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
			await UpdatePortfolioStreamAsync(cancellationToken);
			return;
		}
		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = _portfolioName,
			BoardCode = BoardCodes.SynFutures,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId,
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
		await UpdatePortfolioStreamAsync(cancellationToken);
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
			await UpdatePortfolioStreamAsync(cancellationToken);
			return;
		}
		await SendOrderSnapshotAsync(statusMsg, cancellationToken);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}
		_orderStatusSubscriptionId = statusMsg.TransactionId;
		_lastAccountRefresh = DateTime.UtcNow;
		await UpdatePortfolioStreamAsync(cancellationToken);
	}

	private async ValueTask RegisterMarketOrderAsync(OrderRegisterMessage regMsg,
		SynFuturesMarket market, SynFuturesOrderCondition condition,
		BigInteger signedSize, decimal volume,
		CancellationToken cancellationToken)
	{
		var quotation = await ApiClient.InquireAsync(market, signedSize,
			cancellationToken) ?? throw new InvalidDataException(
				"SynFutures returned no market-order quotation.");
		var entryNotional = quotation.EntryNotional.ParseInteger(
			"entry notional");
		var fee = quotation.Fee.ParseInteger("trade fee");
		var minimum = quotation.MinimumAmount.ParseInteger("minimum margin");
		var leverage = condition.Leverage.ToBaseUnits(18,
			nameof(condition.Leverage));
		var margin = condition.Margin is decimal explicitMargin
			? explicitMargin.ToBaseUnits(18, nameof(condition.Margin))
			: regMsg.PositionEffect == OrderPositionEffects.CloseOnly
				? BigInteger.Zero
				: BigInteger.Max(minimum,
					DivideUp(entryNotional * BigInteger.Pow(10, 18), leverage) +
					fee);
		await EnsureGateBalanceAsync(market, margin, cancellationToken);
		var executionPrice = entryNotional.FromBaseUnits(18) / volume;
		var factor = regMsg.Side == Sides.Buy
			? 1m + SlippageBps / 10000m
			: 1m - SlippageBps / 10000m;
		var limitTick = SynFuturesExtensions.PriceToTick(executionPrice * factor);
		if (regMsg.Side == Sides.Sell)
			limitTick++;
		var receipt = await RpcClient.SendAndWaitAsync(
			RpcClient.CreateTradeTransaction(market, signedSize, margin,
				limitTick, GetDeadline()), TransactionTimeout, cancellationToken);
		var time = await RpcClient.GetReceiptTimeAsync(receipt,
			cancellationToken);
		var trade = RpcClient.TryGetTradeEvent(receipt, market) ??
			throw new InvalidDataException(
				"SynFutures market-order receipt contains no Trade event.");
		if (trade.Expiry != market.Expiry)
			throw new InvalidDataException(
				"SynFutures Trade event references a different market expiry.");
		var orderId = "trade:" + receipt.TransactionHash.NormalizeHash();
		TrackOrder(orderId, regMsg.TransactionId);
		var fillSize = BigInteger.Abs(trade.Size);
		var fillVolume = fillSize > 0 ? fillSize.FromBaseUnits(18) : volume;
		var fillPrice = fillSize > 0 && trade.EntryNotional > 0
			? trade.EntryNotional.FromBaseUnits(18) / fillVolume
			: executionPrice;
		UpdateServerTime(time);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = regMsg.Side,
			OrderVolume = volume,
			Balance = 0m,
			OrderPrice = executionPrice,
			AveragePrice = fillPrice,
			OrderType = OrderTypes.Market,
			OrderState = OrderStates.Done,
			OrderStringId = orderId,
			TransactionId = regMsg.TransactionId,
			OriginalTransactionId = regMsg.TransactionId,
			Commission = SynFuturesRpcClient.GetCommission(receipt),
			CommissionCurrency = "ETH",
			TimeInForce = TimeInForce.CancelBalance,
			PositionEffect = regMsg.PositionEffect,
			Condition = condition.Clone(),
		}, cancellationToken);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = regMsg.Side,
			OrderStringId = orderId,
			TradeStringId = receipt.TransactionHash.NormalizeHash() + ":0",
			TradePrice = fillPrice,
			TradeVolume = fillVolume,
			Commission = fee.FromBaseUnits(18),
			CommissionCurrency = market.QuoteToken?.Symbol,
			OriginalTransactionId = regMsg.TransactionId,
			PositionEffect = regMsg.PositionEffect,
		}, cancellationToken);
	}

	private async ValueTask RegisterLimitOrderAsync(OrderRegisterMessage regMsg,
		SynFuturesMarket market, SynFuturesOrderCondition condition,
		BigInteger signedSize, decimal volume,
		CancellationToken cancellationToken)
	{
		if (regMsg.Price <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Price));
		var placed = await PlaceLimitAsync(market, signedSize, regMsg.Price,
			condition, cancellationToken);
		var orderId = SynFuturesExtensions.CreateOrderKey(
			market.InstrumentAddress, market.Expiry, placed.Event.Tick,
			placed.Event.Nonce);
		TrackOrder(orderId, regMsg.TransactionId);
		await SendOutMessageAsync(CreateActiveOrderMessage(market, orderId,
			regMsg.Side, volume, placed.Price, regMsg.TransactionId,
			placed.Time, condition,
			SynFuturesRpcClient.GetCommission(placed.Receipt)), cancellationToken);
	}

	private async ValueTask<(SynFuturesRpcReceipt Receipt,
		SynFuturesPlaceEvent Event, DateTime Time, decimal Price)> PlaceLimitAsync(
		SynFuturesMarket market, BigInteger signedSize, decimal requestedPrice,
		SynFuturesOrderCondition condition,
		CancellationToken cancellationToken)
	{
		var tick = SynFuturesExtensions.AlignOrderTick(
			SynFuturesExtensions.PriceToTick(requestedPrice));
		var price = SynFuturesExtensions.TickToPrice(tick);
		var fairPrice = market.FairPrice.ParseDecimal("fair price");
		var fairTick = SynFuturesExtensions.PriceToTick(fairPrice);
		if (signedSize > 0 && tick >= fairTick ||
			signedSize < 0 && tick <= fairTick)
			throw new ArgumentOutOfRangeException(nameof(requestedPrice),
				requestedPrice, signedSize > 0
					? "SynFutures buy limits must be below the current fair price."
					: "SynFutures sell limits must be above the current fair price.");
		var priceRaw = price.ToBaseUnits(18, nameof(requestedPrice));
		var markRaw = market.MarkPrice.ParseInteger("mark price");
		var leverage = condition.Leverage.ToBaseUnits(18,
			nameof(condition.Leverage));
		var notional = DivideUp(BigInteger.Max(priceRaw, markRaw) *
			BigInteger.Abs(signedSize), BigInteger.Pow(10, 18));
		var margin = condition.Margin is decimal explicitMargin
			? explicitMargin.ToBaseUnits(18, nameof(condition.Margin))
			: DivideUp(notional * BigInteger.Pow(10, 18), leverage);
		margin = DivideUp(margin * (10000 + SlippageBps), 10000);
		if (margin <= 0)
			throw new InvalidOperationException(
				"SynFutures limit-order margin must be positive.");
		await EnsureGateBalanceAsync(market, margin, cancellationToken);
		var receipt = await RpcClient.SendAndWaitAsync(
			RpcClient.CreatePlaceTransaction(market, signedSize, margin, tick,
				GetDeadline()), TransactionTimeout, cancellationToken);
		var time = await RpcClient.GetReceiptTimeAsync(receipt,
			cancellationToken);
		var placed = RpcClient.TryGetPlaceEvent(receipt, market) ??
			throw new InvalidDataException(
				"SynFutures limit-order receipt contains no Place event.");
		if (placed.Expiry != market.Expiry || placed.Tick != tick)
			throw new InvalidDataException(
				"SynFutures Place event does not match the submitted order.");
		return (receipt, placed, time, price);
	}

	private async ValueTask EnsureGateBalanceAsync(SynFuturesMarket market,
		BigInteger requiredWad, CancellationToken cancellationToken)
	{
		if (requiredWad <= 0)
			return;
		var balances = await ApiClient.GetGateBalancesAsync(
			RpcClient.WalletAddress, cancellationToken);
		var balance = (balances?.Portfolios ?? []).FirstOrDefault(item =>
			item is not null && item.QuoteAddress?.Equals(
				market.QuoteToken?.Address,
				StringComparison.OrdinalIgnoreCase) == true);
		var available = balance is null
			? BigInteger.Zero
			: NormalizeToWad(balance.Balance.ParseIntegerOrZero("gate balance"),
				balance.Decimals);
		if (available < requiredWad)
			throw new InvalidOperationException(
				"SynFutures Gate balance is insufficient. Required " +
				requiredWad.FromBaseUnits(18) + " " +
				(market.QuoteToken?.Symbol ?? "quote") + ", available " +
				available.FromBaseUnits(18) + ". Deposit collateral in the " +
				"SynFutures application first.");
	}

	private async ValueTask ValidatePositionEffectAsync(
		SynFuturesMarket market, BigInteger signedSize,
		OrderPositionEffects? effect, CancellationToken cancellationToken)
	{
		if (effect is null)
			return;
		var portfolio = await ApiClient.GetPortfolioAsync(
			RpcClient.WalletAddress, cancellationToken);
		var position = FindPortfolio(portfolio, market)?.Position;
		var current = position?.Size.ParseIntegerOrZero("position size") ??
			BigInteger.Zero;
		if (effect == OrderPositionEffects.CloseOnly)
		{
			if (current == 0 || current.Sign == signedSize.Sign ||
				BigInteger.Abs(signedSize) > BigInteger.Abs(current))
				throw new InvalidOperationException(
					"SynFutures close-only order must reduce the current " +
					"position without reversing it.");
		}
		else if (effect == OrderPositionEffects.OpenOnly && current != 0 &&
			current.Sign != signedSize.Sign)
			throw new InvalidOperationException(
				"SynFutures open-only order cannot reduce the current position.");
	}

	private async ValueTask RefreshAccountSubscriptionsAsync(
		CancellationToken cancellationToken)
	{
		if (!await _accountRefreshGate.WaitAsync(0, cancellationToken))
			return;
		try
		{
			if (_portfolioSubscriptionId != 0)
				await SendPortfolioSnapshotAsync(_portfolioSubscriptionId,
					cancellationToken);
			if (_orderStatusSubscriptionId != 0)
				await SendOrderSnapshotAsync(new OrderStatusMessage
				{
					TransactionId = _orderStatusSubscriptionId,
					IsSubscribe = true,
					PortfolioName = _portfolioName,
				}, cancellationToken);
		}
		finally
		{
			_accountRefreshGate.Release();
		}
	}

	private async ValueTask SendPortfolioSnapshotAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		var portfolioTask = ApiClient.GetPortfolioAsync(RpcClient.WalletAddress,
			cancellationToken).AsTask();
		var balancesTask = ApiClient.GetGateBalancesAsync(RpcClient.WalletAddress,
			cancellationToken).AsTask();
		var ethTask = RpcClient.GetEthBalanceAsync(cancellationToken).AsTask();
		await Task.WhenAll(portfolioTask, balancesTask, ethTask);
		var time = DateTime.UtcNow;
		UpdateServerTime(time);
		foreach (var balance in (await balancesTask)?.Portfolios ?? [])
		{
			if (balance?.Symbol.IsEmpty() != false || balance.Decimals is < 0 or > 28)
				continue;
			var current = balance.Balance.ParseIntegerOrZero("gate balance")
				.FromBaseUnits(balance.Decimals);
			var reserved = balance.ReservedBalance.ParseIntegerOrZero(
				"reserved balance").FromBaseUnits(balance.Decimals);
			if (current == 0 && reserved == 0)
				continue;
			await SendOutMessageAsync(CreateBalanceMessage(balance.Symbol,
				current, reserved, transactionId, time), cancellationToken);
		}
		await SendOutMessageAsync(CreateBalanceMessage("ETH",
			(await ethTask).FromBaseUnits(18), 0m, transactionId, time),
			cancellationToken);

		var currentPositions = new HashSet<string>(
			StringComparer.OrdinalIgnoreCase);
		foreach (var item in (await portfolioTask)?.Portfolios ?? [])
		{
			var market = GetMarket(item?.InstrumentAddress, item?.Expiry ?? 0);
			var position = item?.Position;
			if (market is null || position is null)
				continue;
			var sizeRaw = position.Size.ParseIntegerOrZero("position size");
			if (sizeRaw == 0)
				continue;
			var size = sizeRaw.FromBaseUnits(18);
			var entryPrice = position.EntryPrice.ParseIntegerOrZero(
				"entry price").FromBaseUnits(18);
			var currentPrice = market.MarkPrice.TryParseDecimal();
			var unrealized = currentPrice is decimal mark && entryPrice > 0
				? (mark - entryPrice) * size
				: (decimal?)null;
			var key = PairKey(market.InstrumentAddress, market.Expiry);
			currentPositions.Add(key);
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = market.ToStockSharp(),
				ServerTime = item.BlockInfo?.Timestamp > 0
					? item.BlockInfo.Timestamp.ToUtc()
					: time,
				OriginalTransactionId = transactionId,
				Side = size > 0 ? Sides.Buy : Sides.Sell,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, size.Abs(), true)
			.TryAdd(PositionChangeTypes.AveragePrice, entryPrice, true)
			.TryAdd(PositionChangeTypes.CurrentPrice, currentPrice, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, unrealized, true),
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
			var separator = key.LastIndexOf('_');
			if (separator <= 0 || !uint.TryParse(key[(separator + 1)..],
				NumberStyles.None, CultureInfo.InvariantCulture, out var expiry))
				continue;
			var market = GetMarket(key[..separator], expiry);
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

	private async ValueTask SendOrderSnapshotAsync(OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		var portfolioTask = ApiClient.GetPortfolioAsync(RpcClient.WalletAddress,
			cancellationToken).AsTask();
		var ordersTask = ApiClient.GetOrderHistoryAsync(RpcClient.WalletAddress,
			1, HistoryLimit, cancellationToken).AsTask();
		var tradesTask = ApiClient.GetTradeHistoryAsync(RpcClient.WalletAddress,
			1, HistoryLimit, cancellationToken).AsTask();
		await Task.WhenAll(portfolioTask, ordersTask, tradesTask);
		var entries = new List<ExecutionMessage>();
		var active = new Dictionary<string, SynFuturesOpenOrder>(
			StringComparer.OrdinalIgnoreCase);
		foreach (var entry in GetOpenOrders(await portfolioTask))
		{
			var message = CreateOpenOrderMessage(entry.Market, entry.Order,
				statusMsg.TransactionId);
			entries.Add(message);
			active[message.OrderStringId] = entry.Order;
		}
		foreach (var order in (await ordersTask)?.Items ?? [])
		{
			var market = GetMarket(order?.InstrumentAddress, order?.Expiry ?? 0);
			if (market is not null)
				entries.Add(CreateHistoryOrderMessage(market, order,
					statusMsg.TransactionId));
		}
		var selected = entries.Where(message =>
				IsSecurityMatch(message.SecurityId, statusMsg) &&
				IsOrderMatch(message, statusMsg))
			.OrderByDescending(static message => message.ServerTime)
			.Skip(Math.Max(0, statusMsg.Skip ?? 0).To<int>())
			.Take((statusMsg.Count ?? int.MaxValue).Min(int.MaxValue).To<int>())
			.OrderBy(static message => message.ServerTime)
			.ToArray();
		foreach (var message in selected)
			await SendOutMessageAsync(message, cancellationToken);
		foreach (var trade in ((await tradesTask)?.Items ?? [])
			.Where(static trade => trade is not null)
			.OrderBy(static trade => trade.Timestamp))
		{
			var market = GetMarket(trade.InstrumentAddress, trade.Expiry);
			if (market is null || !IsSecurityMatch(market.ToStockSharp(), statusMsg))
				continue;
			await SendOutMessageAsync(CreateAccountFillMessage(market, trade,
				statusMsg.TransactionId), cancellationToken);
		}

		(string OrderId, SynFuturesOpenOrder Order)[] removed;
		using (_sync.EnterScope())
		{
			removed = [.. _knownOrders.Where(pair =>
				!active.ContainsKey(pair.Key)).Select(static pair =>
					(pair.Key, pair.Value))];
			_knownOrders.Clear();
			foreach (var pair in active)
				_knownOrders.Add(pair.Key, pair.Value);
		}
		foreach (var removedOrder in removed)
		{
			var parsed = SynFuturesExtensions.ParseOrderKey(
				removedOrder.OrderId);
			var market = GetMarket(parsed.Instrument, parsed.Expiry);
			if (market is null)
				continue;
			var order = removedOrder.Order;
			var size = order.Size.ParseIntegerOrZero("order size");
			await SendOutMessageAsync(CreateCancelledOrderMessage(market,
				removedOrder.OrderId, size >= 0 ? Sides.Buy : Sides.Sell,
				BigInteger.Abs(size).FromBaseUnits(18), GetOrderPrice(order),
				DateTime.UtcNow, statusMsg.TransactionId, null), cancellationToken);
		}
	}

	private ExecutionMessage CreateOpenOrderMessage(SynFuturesMarket market,
		SynFuturesOpenOrder order, long originalTransactionId)
	{
		var size = order.Size.ParseIntegerOrZero("order size");
		var taken = BigInteger.Abs(order.Taken.ParseIntegerOrZero("order taken"));
		var volumeRaw = BigInteger.Abs(size);
		var orderId = GetOrderId(market, order);
		return new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = order.LastUpdateTime > 0
				? order.LastUpdateTime.ToUtc()
				: ServerTime,
			PortfolioName = _portfolioName,
			Side = size >= 0 ? Sides.Buy : Sides.Sell,
			OrderVolume = volumeRaw.FromBaseUnits(18),
			Balance = BigInteger.Max(BigInteger.Zero, volumeRaw - taken)
				.FromBaseUnits(18),
			OrderPrice = GetOrderPrice(order),
			OrderType = OrderTypes.Limit,
			OrderState = OrderStates.Active,
			OrderStringId = orderId,
			TransactionId = GetOriginalTransactionId(orderId),
			OriginalTransactionId = originalTransactionId,
			TimeInForce = TimeInForce.PutInQueue,
		};
	}

	private ExecutionMessage CreateHistoryOrderMessage(SynFuturesMarket market,
		SynFuturesOrderHistory order, long originalTransactionId)
	{
		var state = order.TypeName?.ToLowerInvariant() switch
		{
			"open" or "placed" or "pending" => OrderStates.Active,
			_ => OrderStates.Done,
		};
		var volume = order.Size.ParseDecimal("order size").Abs();
		var taken = order.TakenSize.TryParseDecimal()?.Abs() ?? 0m;
		var orderId = "history:" + order.Id.ThrowIfEmpty("order id");
		return new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = order.PlaceTimestamp.ToUtc(),
			PortfolioName = _portfolioName,
			Side = ParseSide(order.Side) ?? Sides.Buy,
			OrderVolume = volume,
			Balance = state == OrderStates.Active
				? (volume - taken).Max(0m)
				: 0m,
			OrderPrice = order.OrderPrice.ParseDecimal("order price"),
			AveragePrice = order.FillTimestamp > 0
				? order.OrderPrice.ParseDecimal("order price")
				: null,
			OrderType = OrderTypes.Limit,
			OrderState = state,
			OrderStringId = orderId,
			TransactionId = GetOriginalTransactionId(orderId),
			OriginalTransactionId = originalTransactionId,
			TimeInForce = TimeInForce.PutInQueue,
		};
	}

	private ExecutionMessage CreateAccountFillMessage(SynFuturesMarket market,
		SynFuturesTrade trade, long originalTransactionId)
		=> new()
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = market.ToStockSharp(),
			ServerTime = trade.Timestamp.ToUtc(),
			PortfolioName = _portfolioName,
			Side = ParseSide(trade.Side) ?? Sides.Buy,
			OrderStringId = "Limit".Equals(trade.Type,
				StringComparison.OrdinalIgnoreCase)
				? "history:" + trade.Id
				: "trade:" + trade.TransactionHash,
			TradeStringId = trade.Id,
			TradePrice = trade.Price.ParseDecimal("trade price"),
			TradeVolume = trade.Size.ParseDecimal("trade size").Abs(),
			Commission = (trade.TradeFee.TryParseDecimal() ?? 0m) +
				(trade.ProtocolFee.TryParseDecimal() ?? 0m),
			CommissionCurrency = market.QuoteToken?.Symbol,
			OriginalTransactionId = originalTransactionId,
		};

	private ExecutionMessage CreateActiveOrderMessage(SynFuturesMarket market,
		string orderId, Sides side, decimal volume, decimal price,
		long transactionId, DateTime time, SynFuturesOrderCondition condition,
		decimal? gasCommission)
		=> new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = side,
			OrderVolume = volume,
			Balance = volume,
			OrderPrice = price,
			OrderType = OrderTypes.Limit,
			OrderState = OrderStates.Active,
			OrderStringId = orderId,
			TransactionId = transactionId,
			OriginalTransactionId = transactionId,
			Commission = gasCommission,
			CommissionCurrency = gasCommission is null ? null : "ETH",
			TimeInForce = TimeInForce.PutInQueue,
			Condition = condition.Clone(),
		};

	private ExecutionMessage CreateCancelledOrderMessage(
		SynFuturesMarket market, string orderId, Sides side, decimal volume,
		decimal price, DateTime time, long originalTransactionId,
		decimal? gasCommission)
		=> new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = time.EnsureUtc(),
			PortfolioName = _portfolioName,
			Side = side,
			OrderVolume = volume,
			Balance = 0m,
			OrderPrice = price,
			OrderType = OrderTypes.Limit,
			OrderState = OrderStates.Done,
			OrderStringId = orderId,
			TransactionId = GetOriginalTransactionId(orderId),
			OriginalTransactionId = originalTransactionId,
			Commission = gasCommission,
			CommissionCurrency = gasCommission is null ? null : "ETH",
			TimeInForce = TimeInForce.PutInQueue,
		};

	private PositionChangeMessage CreateBalanceMessage(string symbol,
		decimal current, decimal blocked, long transactionId, DateTime time)
		=> new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = new()
			{
				SecurityCode = symbol,
				BoardCode = BoardCodes.SynFutures,
			},
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, current, true)
		.TryAdd(PositionChangeTypes.BlockedValue, blocked, true);

	private async ValueTask UpdatePortfolioStreamAsync(
		CancellationToken cancellationToken)
	{
		var shouldSubscribe = _portfolioSubscriptionId != 0 ||
			_orderStatusSubscriptionId != 0;
		if (shouldSubscribe == _isPortfolioStreamSubscribed)
			return;
		if (shouldSubscribe)
			await SocketClient.SubscribePortfolioAsync(RpcClient.WalletAddress,
				cancellationToken);
		else
			await SocketClient.UnsubscribePortfolioAsync(RpcClient.WalletAddress,
				cancellationToken);
		_isPortfolioStreamSubscribed = shouldSubscribe;
	}

	private SynFuturesPortfolio FindPortfolio(SynFuturesPortfolioData data,
		SynFuturesMarket market)
		=> (data?.Portfolios ?? []).FirstOrDefault(item => item is not null &&
			item.Expiry == market.Expiry && item.InstrumentAddress?.Equals(
				market.InstrumentAddress,
				StringComparison.OrdinalIgnoreCase) == true);

	private SynFuturesOpenOrder FindOpenOrder(SynFuturesPortfolioData data,
		SynFuturesMarket market, int tick, uint nonce)
		=> (FindPortfolio(data, market)?.Orders ?? []).FirstOrDefault(order =>
			order is not null && order.Tick == tick && order.Nonce == nonce);

	private IEnumerable<(SynFuturesMarket Market, SynFuturesOpenOrder Order)>
		GetOpenOrders(SynFuturesPortfolioData data)
	{
		foreach (var item in data?.Portfolios ?? [])
		{
			var market = GetMarket(item?.InstrumentAddress, item?.Expiry ?? 0);
			if (market is null)
				continue;
			foreach (var order in item.Orders ?? [])
				if (order is not null &&
					order.Size.ParseIntegerOrZero("order size") != 0)
					yield return (market, order);
		}
	}

	private static string GetOrderId(SynFuturesMarket market,
		SynFuturesOpenOrder order)
		=> SynFuturesExtensions.CreateOrderKey(market.InstrumentAddress,
			market.Expiry, order.Tick, order.Nonce);

	private static decimal GetOrderPrice(SynFuturesOpenOrder order)
	{
		var raw = order.LimitPrice.ParseIntegerOrZero("limit price");
		return raw > 0
			? raw.FromBaseUnits(18)
			: SynFuturesExtensions.TickToPrice(order.Tick);
	}

	private uint GetDeadline()
	{
		var seconds = DateTime.UtcNow.ToUnix() +
			(long)OrderDeadline.TotalSeconds;
		if (seconds <= 0 || seconds > uint.MaxValue)
			throw new InvalidOperationException(
				"SynFutures order deadline is outside uint32 range.");
		return (uint)seconds;
	}

	private static BigInteger DivideUp(BigInteger numerator,
		BigInteger denominator)
	{
		if (numerator < 0 || denominator <= 0)
			throw new ArgumentOutOfRangeException(nameof(numerator));
		return (numerator + denominator - 1) / denominator;
	}

	private static BigInteger NormalizeToWad(BigInteger value, int decimals)
	{
		if (decimals is < 0 or > 36)
			throw new InvalidDataException(
				"SynFutures returned unsupported token decimals '" + decimals + "'.");
		return decimals == 18
			? value
			: decimals < 18
				? value * BigInteger.Pow(10, 18 - decimals)
				: value / BigInteger.Pow(10, decimals - 18);
	}

	private static void ValidateLeverage(SynFuturesMarket market,
		decimal leverage)
	{
		if (leverage < 1 || market.MaximumLeverage > 0 &&
			leverage > market.MaximumLeverage)
			throw new ArgumentOutOfRangeException(nameof(leverage), leverage,
				"SynFutures leverage for " + market.Symbol +
				" must be at least one" + (market.MaximumLeverage > 0
					? " and at most " + market.MaximumLeverage
					: string.Empty) + ".");
	}

	private static void ValidateTimeInForce(OrderTypes orderType,
		TimeInForce? timeInForce, DateTime? tillDate)
	{
		if (tillDate is not null)
			throw new NotSupportedException(
				"SynFutures contracts do not expose expiring orders.");
		if (timeInForce is null)
			return;
		var expected = orderType == OrderTypes.Market
			? TimeInForce.CancelBalance
			: TimeInForce.PutInQueue;
		if (timeInForce != expected)
			throw new NotSupportedException(
				"SynFutures " + orderType + " orders require " + expected + ".");
	}

	private static string ResolveOrderId(string orderStringId, long? orderId,
		string userOrderId)
	{
		if (orderId is not null)
			throw new InvalidOperationException(
				"SynFutures orders use string identifiers.");
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
			securityId.SecurityCode.Equals(filter.SecurityCode,
				StringComparison.OrdinalIgnoreCase)) &&
			(filter.BoardCode.IsEmpty() ||
			securityId.BoardCode.Equals(filter.BoardCode,
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
			order.ServerTime < from.EnsureUtc())
			return false;
		if (filter.To is DateTime to && order.ServerTime > to.EnsureUtc())
			return false;
		return true;
	}
}
