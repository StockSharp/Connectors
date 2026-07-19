namespace StockSharp.Luno;

public partial class LunoMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		ValidatePortfolio(regMsg.PortfolioName);
		var market = GetMarket(regMsg.SecurityId);
		var condition = regMsg.Condition as LunoOrderCondition ?? new();
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		var volume = regMsg.Volume.Abs();
		if (regMsg.VisibleVolume is > 0 && regMsg.VisibleVolume != volume)
			throw new NotSupportedException(
				"Luno does not document iceberg orders.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException("Luno does not support GTD orders.");
		if (condition.QuoteAmount is <= 0)
			throw new InvalidOperationException(
				"Luno quote amount must be positive.");
		if (condition.QuoteAmount is not null &&
			(orderType != OrderTypes.Market || regMsg.Side != Sides.Buy))
			throw new InvalidOperationException(
				"Luno quote amount is valid for market buy orders only.");

		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var customerOrderId = CreateCustomerOrderId(regMsg.TransactionId,
			regMsg.UserOrderId);
		LunoIdResponse result;
		switch (orderType)
		{
			case OrderTypes.Limit:
			case OrderTypes.Conditional:
				ValidateVolume(volume, market);
				ValidatePrice(regMsg.Price, market, "limit price");
				var timeInForce = regMsg.TimeInForce.ToLuno();
				if (regMsg.PostOnly == true &&
					timeInForce != LunoTimeInForce.GoodTillCancelled)
					throw new InvalidOperationException(
						"A Luno post-only order must use GTC time-in-force.");
				decimal? triggerPrice = null;
				LunoStopDirections? direction = null;
				if (orderType == OrderTypes.Conditional)
				{
					if (regMsg.PostOnly == true)
						throw new InvalidOperationException(
							"A Luno stop-limit order cannot be post-only.");
					triggerPrice = condition.TriggerPrice ??
						throw new InvalidOperationException(
							"Luno stop-limit orders require a trigger price.");
					ValidatePrice(triggerPrice.Value, market, "trigger price");
					direction = GetStopDirection(regMsg.Side,
						condition.IsTakeProfit);
				}
				result = await RestClient.PlaceLimitOrderAsync(new()
				{
					Pair = market.Symbol,
					Side = regMsg.Side.ToLunoLimit(),
					TimeInForce = timeInForce,
					IsPostOnly = regMsg.PostOnly == true,
					Volume = volume,
					Price = regMsg.Price,
					StopPrice = triggerPrice,
					StopDirection = direction,
					Timestamp = timestamp,
					TimeToLive = 10000,
					ClientOrderId = customerOrderId,
				}, cancellationToken);
				break;
			case OrderTypes.Market:
				if (regMsg.PostOnly == true)
					throw new InvalidOperationException(
						"A market order cannot be post-only.");
				if (regMsg.TimeInForce is TimeInForce.CancelBalance or
					TimeInForce.MatchOrCancel)
					throw new NotSupportedException(
						"Luno market orders do not accept time-in-force.");
				if (regMsg.Side == Sides.Buy && condition.QuoteAmount is null)
					throw new InvalidOperationException(
						"Luno market buy orders require QuoteAmount in LunoOrderCondition.");
				if (regMsg.Side == Sides.Sell)
					ValidateVolume(volume, market);
				result = await RestClient.PlaceMarketOrderAsync(new()
				{
					Pair = market.Symbol,
					Side = regMsg.Side.ToLuno(),
					CounterVolume = regMsg.Side == Sides.Buy
						? condition.QuoteAmount
						: null,
					BaseVolume = regMsg.Side == Sides.Sell ? volume : null,
					Timestamp = timestamp,
					TimeToLive = 10000,
					ClientOrderId = customerOrderId,
				}, cancellationToken);
				break;
			default:
				throw new NotSupportedException(
					LocalizedStrings.OrderUnsupportedType.Put(orderType,
						regMsg.TransactionId));
		}

		if (result?.OrderId.IsEmpty() != false)
			throw new InvalidDataException(
				"Luno accepted an order without returning its identifier.");
		var tracked = new TrackedOrder
		{
			TransactionId = regMsg.TransactionId,
			Symbol = market.Symbol,
			ExchangeOrderId = result.OrderId,
			CustomerOrderId = customerOrderId,
			Side = regMsg.Side,
			OrderType = orderType,
			Volume = volume,
			Price = regMsg.Price,
			IsPostOnly = regMsg.PostOnly == true,
			TimeInForce = regMsg.TimeInForce,
			Condition = condition.Clone() as LunoOrderCondition,
		};
		TrackOrder(tracked, result.OrderId, customerOrderId,
			regMsg.TransactionId.ToString(CultureInfo.InvariantCulture));
		await SendTrackedOrderAsync(tracked, OrderStates.Active, volume,
			regMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		var orderId = ResolveOrderIdentifier(cancelMsg.OrderId,
			cancelMsg.OrderStringId, "cancellation");
		var result = await RestClient.CancelOrderAsync(new()
		{
			OrderId = orderId,
		}, cancellationToken);
		if (result?.IsSuccess != true)
			throw new InvalidOperationException(
				$"Luno did not cancel order '{orderId}'.");
		await SendCanceledOrderAsync(orderId, cancelMsg.TransactionId,
			cancelMsg.SecurityId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"Luno spot order cancellation does not close positions.");
		var symbol = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(cancelMsg.SecurityId).Symbol;
		long? createdBefore = null;
		while (true)
		{
			var orders = await RestClient.GetOrdersAsync(new()
			{
				Pair = symbol,
				IsClosed = false,
				CreatedBefore = createdBefore,
				Limit = 1000,
			}, cancellationToken);
			foreach (var order in orders.Where(order => order is not null &&
				(cancelMsg.Side is null || order.Side.ToStockSharp() ==
					cancelMsg.Side) &&
				(cancelMsg.IsStop is null ||
					(order.Type == LunoOrderTypes.StopLimit) ==
					cancelMsg.IsStop.Value)))
			{
				var result = await RestClient.CancelOrderAsync(new()
				{
					OrderId = order.OrderId,
				}, cancellationToken);
				if (result?.IsSuccess == true)
					await SendCanceledOrderAsync(order.OrderId,
						cancelMsg.TransactionId, order.Pair.ToStockSharp(),
						cancellationToken);
			}

			if (orders.Length < 1000)
				break;
			var next = orders.Where(static order =>
				order is not null && order.CreationTimestamp > 0)
				.Select(static order => order.CreationTimestamp)
				.DefaultIfEmpty().Min();
			if (next <= 0 || next == createdBefore)
				break;
			createdBefore = next;
		}
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
			BoardCode = BoardCodes.Luno,
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
		var symbol = statusMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(statusMsg.SecurityId).Symbol;
		string orderId = null;
		if (statusMsg.HasOrderId())
			orderId = ResolveOrderIdentifier(statusMsg.OrderId,
				statusMsg.OrderStringId, "lookup");
		var tracked = GetTrackedOrder(orderId);
		symbol ??= tracked?.Symbol;
		var maximum = (statusMsg.Count ?? 100).Min(1000).Max(1).To<int>();
		await SendOrderSnapshotAsync(statusMsg.TransactionId, symbol, orderId,
			statusMsg.Side, statusMsg.From, statusMsg.To, maximum, true,
			cancellationToken);
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
				Symbol = symbol,
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
			var order = await RestClient.GetOrderAsync(orderId, cancellationToken);
			await SendOrderAsync(order, transactionId, force, cancellationToken);
			return;
		}

		var sent = 0;
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var tradePairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (!symbol.IsEmpty())
			tradePairs.Add(symbol);
		foreach (var isClosed in new[] { false, true })
		{
			if (sent >= maximum)
				break;
			var orders = await RestClient.GetOrdersAsync(new()
			{
				Pair = symbol,
				IsClosed = isClosed,
				CreatedBefore = to is null
					? null
					: new DateTimeOffset(to.Value.ToUniversalTime())
						.ToUnixTimeMilliseconds(),
				Limit = maximum - sent,
			}, cancellationToken);
			foreach (var pair in orders.Where(static order =>
				order?.Pair.IsEmpty() == false).Select(static order =>
					order.Pair.NormalizeSymbol()))
				tradePairs.Add(pair);
			foreach (var order in orders.Where(order => order is not null &&
				seen.Add(order.OrderId) &&
				(side is null || order.Side.ToStockSharp() == side) &&
				IsInRange(order.CreationTimestamp, from, to)))
			{
				await SendOrderAsync(order, transactionId, force,
					cancellationToken);
				if (++sent >= maximum)
					break;
			}
		}

		var tradesSent = 0;
		foreach (var pair in tradePairs.OrderBy(static value => value,
			StringComparer.OrdinalIgnoreCase))
		{
			if (tradesSent >= maximum)
				break;
			var trades = await RestClient.GetUserTradesAsync(new()
			{
				Pair = pair,
				Since = from is null
					? null
					: new DateTimeOffset(from.Value.ToUniversalTime())
						.ToUnixTimeMilliseconds(),
				Before = to is null
					? null
					: new DateTimeOffset(to.Value.ToUniversalTime())
						.ToUnixTimeMilliseconds(),
				IsDescending = false,
				Limit = maximum - tradesSent,
			}, cancellationToken);
			foreach (var trade in trades.Where(trade => trade is not null &&
				(side is null || trade.Type.ToStockSharp() == side)))
			{
				await SendUserTradeAsync(trade, transactionId, cancellationToken);
				if (++tradesSent >= maximum)
					break;
			}
		}
	}

	private async ValueTask SendPortfolioSnapshotAsync(long transactionId,
		bool force, CancellationToken cancellationToken)
	{
		var balances = await RestClient.GetBalancesAsync(cancellationToken);
		foreach (var balance in balances)
			await SendBalanceAsync(balance, transactionId, force,
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

	private ValueTask SendBalanceAsync(LunoBalance balance, long transactionId,
		bool force, CancellationToken cancellationToken)
	{
		if (balance?.Asset.IsEmpty() != false)
			return default;
		if (!balance.AccountId.IsEmpty())
			using (_sync.EnterScope())
				_accountAssets[balance.AccountId] = balance.Asset.ToUpperInvariant();
		return SendBalanceAsync(balance.Asset, balance.Balance, balance.Reserved,
			balance.Unconfirmed, CurrentTime, transactionId, force,
			cancellationToken);
	}

	private ValueTask SendBalanceAsync(string asset, decimal balance,
		decimal reserved, decimal unconfirmed, DateTime timestamp,
		long transactionId, bool force, CancellationToken cancellationToken)
	{
		if (asset.IsEmpty())
			return default;
		var fingerprint = new BalanceFingerprint(balance, reserved, unconfirmed);
		var key = $"{transactionId}:{asset}";
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
				SecurityCode = asset.ToUpperInvariant(),
				BoardCode = BoardCodes.Luno,
			},
			ServerTime = timestamp,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, balance, true)
		.TryAdd(PositionChangeTypes.BlockedValue, reserved, true),
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

	private ValueTask SendOrderAsync(LunoOrder order, long transactionId,
		bool force, CancellationToken cancellationToken)
	{
		if (order?.OrderId.IsEmpty() != false || order.Pair.IsEmpty())
			return default;
		var volume = order.LimitVolume > 0
			? order.LimitVolume
			: order.BaseFilled;
		var balance = order.Status.ToStockSharp() == OrderStates.Active
			? (volume - order.BaseFilled).Max(0m)
			: 0m;
		var fingerprint = new OrderFingerprint(order.Status, order.BaseFilled,
			volume, order.CompletedTimestamp);
		if (!ShouldSendOrder(order.OrderId, transactionId, fingerprint, force))
			return default;
		var tracked = GetTrackedOrder(order.OrderId) ??
			GetTrackedOrder(order.ClientOrderId) ?? CreateTrackedOrder(order);
		TrackOrder(tracked, order.OrderId, order.ClientOrderId);
		var market = GetMarket(order.Pair);
		decimal? commission = order.CounterFee != 0
			? order.CounterFee
			: order.BaseFee != 0 ? order.BaseFee : null;
		var commissionCurrency = order.CounterFee != 0
			? market.QuoteAsset
			: order.BaseFee != 0 ? market.BaseAsset : null;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Pair.ToStockSharp(),
			ServerTime = (order.CompletedTimestamp > 0
				? order.CompletedTimestamp
				: order.CreationTimestamp).ToLunoTime(CurrentTime),
			PortfolioName = GetPortfolioName(),
			Side = order.Side.ToStockSharp(),
			OrderVolume = volume,
			Balance = balance,
			OrderPrice = order.LimitPrice,
			AveragePrice = order.BaseFilled > 0
				? order.CounterFilled / order.BaseFilled
				: null,
			OrderType = order.Type.ToStockSharp(),
			OrderState = order.Status.ToStockSharp(),
			OrderStringId = order.OrderId,
			UserOrderId = order.ClientOrderId,
			TransactionId = tracked.TransactionId,
			OriginalTransactionId = transactionId,
			TimeInForce = order.TimeInForce?.ToStockSharp(),
			Condition = CreateCondition(order),
			Commission = commission,
			CommissionCurrency = commissionCurrency,
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

	private TrackedOrder CreateTrackedOrder(LunoOrder order)
	{
		var transactionId = long.TryParse(order.ClientOrderId, NumberStyles.None,
			CultureInfo.InvariantCulture, out var parsed)
			? parsed
			: 0L;
		return new()
		{
			TransactionId = transactionId,
			Symbol = order.Pair.NormalizeSymbol(),
			ExchangeOrderId = order.OrderId,
			CustomerOrderId = order.ClientOrderId,
			Side = order.Side.ToStockSharp(),
			OrderType = order.Type.ToStockSharp(),
			Volume = order.LimitVolume,
			Price = order.LimitPrice,
			TimeInForce = order.TimeInForce?.ToStockSharp(),
			Condition = CreateCondition(order),
		};
	}

	private ValueTask SendCanceledOrderAsync(string orderId,
		long transactionId, SecurityId securityId,
		CancellationToken cancellationToken)
	{
		var tracked = GetTrackedOrder(orderId);
		if (tracked is not null)
			return SendTrackedOrderAsync(tracked, OrderStates.Done, 0m,
				transactionId, cancellationToken);
		if (securityId.SecurityCode.IsEmpty())
			return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = GetMarket(securityId).Symbol.ToStockSharp(),
			ServerTime = CurrentTime,
			PortfolioName = GetPortfolioName(),
			OrderStringId = orderId,
			OrderState = OrderStates.Done,
			Balance = 0m,
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}

	private ValueTask SendUserTradeAsync(LunoUserTrade trade,
		long transactionId, CancellationToken cancellationToken)
	{
		if (trade?.Pair.IsEmpty() != false || trade.OrderId.IsEmpty() ||
			trade.Price <= 0 || trade.Volume <= 0 ||
			!AddAccountTrade(trade.Sequence.ToString(CultureInfo.InvariantCulture),
				transactionId))
			return default;
		var tracked = GetTrackedOrder(trade.OrderId) ??
			GetTrackedOrder(trade.ClientOrderId);
		var market = GetMarket(trade.Pair);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = trade.Timestamp.ToLunoTime(CurrentTime),
			PortfolioName = GetPortfolioName(),
			Side = trade.Type.ToStockSharp(),
			OrderStringId = trade.OrderId,
			UserOrderId = trade.ClientOrderId,
			TradeId = trade.Sequence > 0 ? trade.Sequence : null,
			TradePrice = trade.Price,
			TradeVolume = trade.Volume,
			Commission = trade.CounterFee != 0
				? trade.CounterFee
				: trade.BaseFee != 0 ? trade.BaseFee : null,
			CommissionCurrency = trade.CounterFee != 0
				? market.QuoteAsset
				: trade.BaseFee != 0 ? market.BaseAsset : null,
			TransactionId = tracked?.TransactionId ?? 0,
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}

	private async ValueTask OnUserStreamUpdateAsync(
		LunoUserStreamEnvelope update, CancellationToken cancellationToken)
	{
		switch (update?.Type)
		{
			case LunoUserEventTypes.OrderStatus:
				await OnUserOrderStatusAsync(update.OrderStatusUpdate,
					cancellationToken);
				break;
			case LunoUserEventTypes.OrderFill:
				await OnUserOrderFillAsync(update.OrderFillUpdate,
					update.Timestamp.ToLunoTime(CurrentTime), cancellationToken);
				break;
			case LunoUserEventTypes.Balance:
				await OnUserBalanceAsync(update.BalanceUpdate,
					update.Timestamp.ToLunoTime(CurrentTime), cancellationToken);
				break;
		}
	}

	private async ValueTask OnUserOrderStatusAsync(
		LunoUserOrderStatusUpdate update, CancellationToken cancellationToken)
	{
		if (update?.OrderId.IsEmpty() != false)
			return;
		var order = await RestClient.GetOrderAsync(update.OrderId,
			cancellationToken);
		KeyValuePair<long, OrderSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _orderSubscriptions.Where(pair =>
				MatchesOrderSubscription(pair.Value, order.Pair, order.OrderId,
					order.Side.ToStockSharp()))];
		foreach (var subscription in subscriptions)
			await SendOrderAsync(order, subscription.Key, false,
				cancellationToken);
	}

	private async ValueTask OnUserOrderFillAsync(LunoUserOrderFillUpdate update,
		DateTime timestamp, CancellationToken cancellationToken)
	{
		if (update?.OrderId.IsEmpty() != false || update.MarketId.IsEmpty() ||
			update.BaseDelta <= 0 || update.CounterDelta <= 0)
			return;
		var order = await RestClient.GetOrderAsync(update.OrderId,
			cancellationToken);
		KeyValuePair<long, OrderSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _orderSubscriptions.Where(pair =>
				MatchesOrderSubscription(pair.Value, order.Pair, order.OrderId,
					order.Side.ToStockSharp()))];
		var market = GetMarket(order.Pair);
		foreach (var subscription in subscriptions)
		{
			await SendOrderAsync(order, subscription.Key, false,
				cancellationToken);
			var identity = $"{update.OrderId}:{timestamp.Ticks}:" +
				$"{update.BaseFill.ToWire()}:{update.CounterFill.ToWire()}";
			if (!AddAccountTrade(identity, subscription.Key))
				continue;
			var tracked = GetTrackedOrder(order.OrderId) ??
				GetTrackedOrder(order.ClientOrderId);
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				SecurityId = market.Symbol.ToStockSharp(),
				ServerTime = timestamp,
				PortfolioName = GetPortfolioName(),
				Side = order.Side.ToStockSharp(),
				OrderStringId = order.OrderId,
				UserOrderId = order.ClientOrderId,
				TradeStringId = identity,
				TradePrice = update.CounterDelta / update.BaseDelta,
				TradeVolume = update.BaseDelta,
				Commission = update.CounterFeeDelta != 0
					? update.CounterFeeDelta
					: update.BaseFeeDelta != 0 ? update.BaseFeeDelta : null,
				CommissionCurrency = update.CounterFeeDelta != 0
					? market.QuoteAsset
					: update.BaseFeeDelta != 0 ? market.BaseAsset : null,
				TransactionId = tracked?.TransactionId ?? 0,
				OriginalTransactionId = subscription.Key,
			}, cancellationToken);
		}
	}

	private async ValueTask OnUserBalanceAsync(LunoUserBalanceUpdate update,
		DateTime timestamp, CancellationToken cancellationToken)
	{
		if (update is null || update.AccountId <= 0)
			return;
		string asset;
		using (_sync.EnterScope())
			_accountAssets.TryGetValue(update.AccountId.ToString(
				CultureInfo.InvariantCulture), out asset);
		if (asset.IsEmpty())
		{
			var balances = await RestClient.GetBalancesAsync(cancellationToken);
			foreach (var balance in balances)
				if (!balance.AccountId.IsEmpty())
					using (_sync.EnterScope())
						_accountAssets[balance.AccountId] =
							balance.Asset.ToUpperInvariant();
			using (_sync.EnterScope())
				_accountAssets.TryGetValue(update.AccountId.ToString(
					CultureInfo.InvariantCulture), out asset);
		}
		if (asset.IsEmpty())
			return;
		long[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _portfolioSubscriptions];
		var reserved = (update.Balance - update.Available).Max(0m);
		foreach (var subscription in subscriptions)
			await SendBalanceAsync(asset, update.Balance, reserved, 0m, timestamp,
				subscription, false, cancellationToken);
	}

	private static bool MatchesOrderSubscription(OrderSubscription subscription,
		string symbol, string orderId, Sides side)
		=> (subscription.Symbol.IsEmpty() ||
			subscription.Symbol.EqualsIgnoreCase(symbol)) &&
			(subscription.OrderId.IsEmpty() ||
				subscription.OrderId.EqualsIgnoreCase(orderId)) &&
			(subscription.Side is null || subscription.Side == side);

	private static bool IsInRange(long timestamp, DateTime? from, DateTime? to)
	{
		var time = timestamp.ToLunoTime(DateTime.MinValue);
		return (from is null || time >= from.Value.ToUniversalTime()) &&
			(to is null || time <= to.Value.ToUniversalTime());
	}

	private static LunoOrderCondition CreateCondition(LunoOrder order)
	{
		if (order.Type != LunoOrderTypes.StopLimit)
			return null;
		var isTakeProfit = order.StopDirection switch
		{
			LunoStopDirections.Below when order.Side == LunoSides.Buy => true,
			LunoStopDirections.Above when order.Side == LunoSides.Sell => true,
			_ => false,
		};
		return new()
		{
			TriggerPrice = order.StopPrice,
			IsTakeProfit = isTakeProfit,
		};
	}

	private static LunoStopDirections GetStopDirection(Sides side,
		bool isTakeProfit)
		=> (side, isTakeProfit) switch
		{
			(Sides.Buy, false) or (Sides.Sell, true) =>
				LunoStopDirections.Above,
			_ => LunoStopDirections.Below,
		};

	private static string CreateCustomerOrderId(long transactionId,
		string userOrderId)
	{
		var value = userOrderId.IsEmpty()
			? transactionId.ToString(CultureInfo.InvariantCulture)
			: userOrderId.Trim();
		if (value.Length > 255)
			throw new InvalidOperationException(
				"Luno client order IDs must not exceed 255 characters.");
		return value;
	}

	private static void ValidateVolume(decimal volume, MarketDefinition market)
	{
		if (volume <= 0)
			throw new InvalidOperationException(
				"Luno order volume must be positive.");
		ValidateRange(volume, market.MinimumVolume, market.MaximumVolume,
			"volume", market.Symbol);
		ValidateStep(volume, market.VolumeStep, "volume", market.Symbol);
	}

	private static void ValidatePrice(decimal price, MarketDefinition market,
		string name)
	{
		if (price <= 0)
			throw new InvalidOperationException(
				$"Luno {name} must be positive for '{market.Symbol}'.");
		ValidateRange(price, market.MinimumPrice, market.MaximumPrice, name,
			market.Symbol);
		ValidateStep(price, market.PriceStep, name, market.Symbol);
	}

	private static void ValidateRange(decimal value, decimal minimum,
		decimal maximum, string name, string symbol)
	{
		if (minimum > 0 && value < minimum)
			throw new InvalidOperationException(
				$"Luno {name} must be at least {minimum} for '{symbol}'.");
		if (maximum > 0 && value > maximum)
			throw new InvalidOperationException(
				$"Luno {name} must not exceed {maximum} for '{symbol}'.");
	}

	private static void ValidateStep(decimal value, decimal step, string name,
		string symbol)
	{
		if (step > 0 && value % step != 0)
			throw new InvalidOperationException(
				$"Luno {name} must be aligned to step {step} for '{symbol}'.");
	}

	private async ValueTask CompleteOrderStatusAsync(OrderStatusMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
