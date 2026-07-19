namespace StockSharp.CoinDCX;

public partial class CoinDCXMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var market = GetMarket(regMsg.SecurityId);
		if (!market.IsTrading)
			throw new InvalidOperationException(
				$"CoinDCX market '{market.Market}' is not active.");
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException(
				"CoinDCX order volume must be positive.");
		if (market.MinimumQuantity > 0 && volume < market.MinimumQuantity)
			throw new InvalidOperationException(
				$"CoinDCX requires at least {market.MinimumQuantity} " +
				$"{market.BaseAsset} for '{market.Market}'.");
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		var wireOrderType = orderType == OrderTypes.Market
			? CoinDCXOrderTypes.MarketOrder
			: CoinDCXOrderTypes.LimitOrder;
		if (!market.OrderTypes.Contains(wireOrderType))
			throw new NotSupportedException(
				$"CoinDCX market '{market.Market}' does not support {wireOrderType}.");
		if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
			throw new InvalidOperationException(
				"CoinDCX limit orders require a positive price.");
		if (market.MinimumNotional > 0 && orderType == OrderTypes.Limit &&
			regMsg.Price * volume < market.MinimumNotional)
			throw new InvalidOperationException(
				$"CoinDCX requires a minimum notional of {market.MinimumNotional} " +
				$"{market.QuoteAsset} for '{market.Market}'.");
		var maximum = orderType == OrderTypes.Market &&
			market.MaximumMarketQuantity > 0
				? market.MaximumMarketQuantity
				: market.MaximumQuantity;
		if (maximum > 0 && volume > maximum)
			throw new InvalidOperationException(
				$"CoinDCX permits at most {maximum} {market.BaseAsset} " +
				$"for this order type in '{market.Market}'.");
		if (regMsg.VisibleVolume is > 0 && regMsg.VisibleVolume != volume)
			throw new NotSupportedException(
				"CoinDCX does not document iceberg orders for this endpoint.");
		if (regMsg.PostOnly == true)
			throw new NotSupportedException(
				"CoinDCX does not document post-only spot orders for this endpoint.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException(
				"CoinDCX does not document GTD spot orders for this endpoint.");
		if (regMsg.TimeInForce is not null and not TimeInForce.PutInQueue)
			throw new NotSupportedException(
				"CoinDCX spot orders use the exchange default time in force.");

		var clientOrderId = CoinDCXExtensions.CreateClientId(regMsg.TransactionId,
			regMsg.UserOrderId);
		var response = await RestClient.PlaceOrderAsync(new()
		{
			Side = regMsg.Side.ToCoinDCX(),
			OrderType = wireOrderType,
			Market = market.Market,
			PricePerUnit = orderType == OrderTypes.Limit ? regMsg.Price : null,
			TotalQuantity = volume,
			ClientOrderId = clientOrderId,
		}, cancellationToken);
		var order = response?.Orders?.FirstOrDefault();
		if (order?.OrderId.IsEmpty() != false)
			throw new InvalidDataException(
				"CoinDCX accepted an order without returning its identifier.");

		var tracked = new TrackedOrder
		{
			TransactionId = regMsg.TransactionId,
			Market = market.Market,
			ClientOrderId = clientOrderId,
			Side = regMsg.Side,
			OrderType = orderType,
			Volume = volume,
			Price = regMsg.Price,
		};
		TrackOrder(order.OrderId, tracked);
		await SendOrderAsync(order, regMsg.TransactionId, tracked,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var orderId = ResolveOrderId(replaceMsg.OldOrderId,
			replaceMsg.OldOrderStringId, "replacement");
		var tracked = GetTrackedOrder(orderId) ?? throw new InvalidOperationException(
			"CoinDCX price editing requires the original order to be tracked.");
		if (tracked.OrderType != OrderTypes.Limit ||
			replaceMsg.OrderType == OrderTypes.Market)
			throw new NotSupportedException(
				"CoinDCX can edit the price of active limit orders only.");
		var volume = replaceMsg.Volume.Abs();
		if (volume != tracked.Volume)
			throw new NotSupportedException(
				"CoinDCX price editing cannot change the order volume.");
		if (replaceMsg.Price <= 0)
			throw new InvalidOperationException(
				"CoinDCX replacement price must be positive.");

		var order = await RestClient.EditOrderAsync(new()
		{
			OrderId = orderId,
			PricePerUnit = replaceMsg.Price,
		}, cancellationToken);
		tracked.Price = replaceMsg.Price;
		await SendOrderAsync(order, replaceMsg.TransactionId, tracked,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var orderId = ResolveOrderId(cancelMsg.OrderId, cancelMsg.OrderStringId,
			"cancellation");
		await RestClient.CancelOrderAsync(new() { OrderId = orderId },
			cancellationToken);
		TrackedOrder tracked;
		using (_sync.EnterScope())
		{
			_activeOrderIds.Remove(orderId);
			tracked = _trackedOrders.TryGetValue(orderId, out var value)
				? value
				: null;
		}
		if (tracked is not null)
		{
			await SendTrackedOrderAsync(orderId, tracked, OrderStates.Done, 0m,
				cancelMsg.TransactionId, CurrentTime, cancellationToken);
			return;
		}
		if (!cancelMsg.SecurityId.SecurityCode.IsEmpty())
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = GetMarket(cancelMsg.SecurityId).Market.ToStockSharp(),
				ServerTime = CurrentTime,
				PortfolioName = GetPortfolioName(),
				OrderStringId = orderId,
				OrderState = OrderStates.Done,
				Balance = 0m,
				OriginalTransactionId = cancelMsg.TransactionId,
			}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"CoinDCX spot bulk cancellation cannot close positions.");

		if (!cancelMsg.SecurityId.SecurityCode.IsEmpty())
		{
			var market = GetMarket(cancelMsg.SecurityId);
			await RestClient.CancelAllAsync(new()
			{
				Market = market.Market,
				Side = cancelMsg.Side?.ToCoinDCX(),
			}, cancellationToken);
			using (_sync.EnterScope())
				foreach (var orderId in _trackedOrders.Where(pair =>
					pair.Value.Market.EqualsIgnoreCase(market.Market) &&
					(cancelMsg.Side is null || pair.Value.Side == cancelMsg.Side))
					.Select(static pair => pair.Key).ToArray())
					_activeOrderIds.Remove(orderId);
			return;
		}

		(string market, Sides side)[] groups;
		using (_sync.EnterScope())
			groups = [.. _trackedOrders.Where(pair =>
				_activeOrderIds.Contains(pair.Key) &&
				(cancelMsg.Side is null || pair.Value.Side == cancelMsg.Side))
				.Select(static pair => (pair.Value.Market, pair.Value.Side)).Distinct()];
		foreach (var group in groups)
			await RestClient.CancelAllAsync(new()
			{
				Market = group.market,
				Side = group.side.ToCoinDCX(),
			}, cancellationToken);
		using (_sync.EnterScope())
			foreach (var orderId in _trackedOrders.Where(pair =>
				cancelMsg.Side is null || pair.Value.Side == cancelMsg.Side)
				.Select(static pair => pair.Key).ToArray())
				_activeOrderIds.Remove(orderId);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsurePrivateReady();
		if (!lookupMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_portfolioSubscriptions.Remove(lookupMsg.OriginalTransactionId);
			return;
		}

		var portfolio = GetPortfolioName();
		if (lookupMsg.PortfolioName.IsEmpty() ||
			lookupMsg.PortfolioName.EqualsIgnoreCase(portfolio))
		{
			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = portfolio,
				BoardCode = BoardCodes.CoinDCX,
				OriginalTransactionId = lookupMsg.TransactionId,
			}, cancellationToken);
			await SendPortfolioSnapshotAsync(lookupMsg.TransactionId,
				cancellationToken);
		}
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
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
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

		var market = statusMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(statusMsg.SecurityId).Market;
		var orderId = statusMsg.HasOrderId()
			? ResolveOrderId(statusMsg.OrderId, statusMsg.OrderStringId, "lookup")
			: null;
		var tracked = GetTrackedOrder(orderId);
		if (market.IsEmpty() && tracked is not null)
			market = tracked.Market;
		var maximum = (statusMsg.Count ?? 500).Min(5000).Max(1).To<int>();

		if (!orderId.IsEmpty())
		{
			var order = await RestClient.GetOrderAsync(new() { OrderId = orderId },
				cancellationToken);
			await SendOrderAsync(order, statusMsg.TransactionId, tracked,
				cancellationToken);
		}
		else
		{
			if (!market.IsEmpty())
			{
				var orders = await RestClient.GetActiveOrdersAsync(new()
				{
					Market = market,
					Side = statusMsg.Side?.ToCoinDCX(),
				}, cancellationToken);
				foreach (var order in (orders ?? []).TakeLast(maximum))
					await SendOrderAsync(order, statusMsg.TransactionId, null,
						cancellationToken);
			}
			else
			{
				KeyValuePair<string, TrackedOrder>[] active;
				using (_sync.EnterScope())
					active = [.. _trackedOrders.Where(pair =>
						_activeOrderIds.Contains(pair.Key) &&
						(statusMsg.Side is null || pair.Value.Side == statusMsg.Side))];
				foreach (var pair in active.TakeLast(maximum))
					await SendTrackedOrderAsync(pair.Key, pair.Value,
						OrderStates.Active, pair.Value.Volume,
						statusMsg.TransactionId, CurrentTime, cancellationToken);
			}

			var trades = await RestClient.GetAccountTradesAsync(new()
			{
				Limit = maximum,
				Sort = "asc",
				FromTimestamp = statusMsg.From?.ToUniversalTime().ToMilliseconds(),
				ToTimestamp = statusMsg.To?.ToUniversalTime().ToMilliseconds(),
				Symbol = market,
			}, cancellationToken);
			foreach (var trade in trades ?? [])
				if (statusMsg.Side is null || trade.Side.ToStockSharp() == statusMsg.Side)
					await SendAccountTradeAsync(trade, statusMsg.TransactionId,
						cancellationToken);
		}

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
				Market = market,
				OrderId = orderId,
				Side = statusMsg.Side,
			};
	}

	private async ValueTask SendPortfolioSnapshotAsync(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		foreach (var balance in await RestClient.GetBalancesAsync(cancellationToken))
			await SendBalanceAsync(balance.Currency, balance.Balance,
				balance.LockedBalance, originalTransactionId, cancellationToken);
	}

	private ValueTask SendBalanceAsync(string currency, decimal balance,
		decimal lockedBalance, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (currency.IsEmpty())
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(),
			SecurityId = currency.ToStockSharp(),
			ServerTime = CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, balance, true)
		.TryAdd(PositionChangeTypes.BlockedValue, lockedBalance, true),
			cancellationToken);
	}

	private async ValueTask SendOrderAsync(CoinDCXOrder order,
		long originalTransactionId, TrackedOrder tracked,
		CancellationToken cancellationToken)
	{
		if (order?.OrderId.IsEmpty() != false || order.Market.IsEmpty())
			return;
		var market = GetMarket(order.Market);
		tracked ??= GetTrackedOrder(order.OrderId) ?? new TrackedOrder
		{
			TransactionId = CoinDCXExtensions.ParseTransactionId(order.ClientOrderId),
			Market = market.Market,
			ClientOrderId = order.ClientOrderId,
			Side = order.Side.ToStockSharp(),
			OrderType = order.OrderType.ToStockSharp(),
			Volume = order.TotalQuantity,
			Price = order.PricePerUnit,
		};
		TrackOrder(order.OrderId, tracked);
		var state = order.Status.ToStockSharp();
		if (state is OrderStates.Done or OrderStates.Failed)
			using (_sync.EnterScope())
				_activeOrderIds.Remove(order.OrderId);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.Market.ToStockSharp(),
			ServerTime = order.UpdatedAt.ParseTimestamp(
				order.CreatedAt.ParseTimestamp(CurrentTime)),
			PortfolioName = GetPortfolioName(),
			Side = order.Side.ToStockSharp(),
			OrderVolume = order.TotalQuantity,
			Balance = order.RemainingQuantity.Max(0m),
			OrderPrice = order.PricePerUnit,
			OrderType = order.OrderType.ToStockSharp(),
			OrderState = state,
			OrderStringId = order.OrderId,
			TransactionId = tracked.TransactionId,
			OriginalTransactionId = originalTransactionId,
			Commission = order.FeeAmount == 0 ? null : order.FeeAmount.Abs(),
		}, cancellationToken);
	}

	private ValueTask SendTrackedOrderAsync(string orderId, TrackedOrder tracked,
		OrderStates state, decimal balance, long originalTransactionId,
		DateTime serverTime, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = tracked.Market.ToStockSharp(),
			ServerTime = serverTime,
			PortfolioName = GetPortfolioName(),
			Side = tracked.Side,
			OrderVolume = tracked.Volume,
			Balance = balance.Max(0m),
			OrderPrice = tracked.Price,
			OrderType = tracked.OrderType,
			OrderState = state,
			OrderStringId = orderId,
			TransactionId = tracked.TransactionId,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);

	private ValueTask SendAccountTradeAsync(CoinDCXAccountTrade trade,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (trade is null || trade.TradeId <= 0 || trade.OrderId.IsEmpty() ||
			trade.Symbol.IsEmpty() ||
			!AddAccountTrade(trade.TradeId.ToString(CultureInfo.InvariantCulture)))
			return default;
		var market = GetMarket(trade.Symbol);
		var tracked = GetTrackedOrder(trade.OrderId);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = market.Market.ToStockSharp(),
			ServerTime = trade.Timestamp.FromMilliseconds(CurrentTime),
			PortfolioName = GetPortfolioName(),
			Side = trade.Side.ToStockSharp(),
			OrderStringId = trade.OrderId,
			TradeId = trade.TradeId,
			TradePrice = trade.Price,
			TradeVolume = trade.Quantity,
			Commission = trade.FeeAmount == 0 ? null : trade.FeeAmount.Abs(),
			TransactionId = tracked?.TransactionId ?? 0,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private async ValueTask OnPrivateBalancesAsync(CoinDCXPrivateBalance[] balances,
		CancellationToken cancellationToken)
	{
		long[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _portfolioSubscriptions];
		foreach (var subscriptionId in subscriptions)
			foreach (var balance in balances ?? [])
				await SendBalanceAsync(balance.Currency, balance.Balance,
					balance.LockedBalance, subscriptionId, cancellationToken);
	}

	private async ValueTask OnPrivateOrdersAsync(CoinDCXPrivateOrder[] orders,
		CancellationToken cancellationToken)
	{
		foreach (var order in orders ?? [])
		{
			if (order?.OrderId.IsEmpty() != false || order.Market.IsEmpty())
				continue;
			var market = GetMarket(order.Market);
			var side = order.Side.ToStockSharp();
			var tracked = GetTrackedOrder(order.OrderId) ?? new TrackedOrder
			{
				TransactionId = CoinDCXExtensions.ParseTransactionId(
					order.ClientOrderId),
				Market = market.Market,
				ClientOrderId = order.ClientOrderId,
				Side = side,
				OrderType = order.OrderType.ToStockSharp(),
				Volume = order.TotalQuantity,
				Price = order.PricePerUnit,
			};
			TrackOrder(order.OrderId, tracked);
			var state = order.Status.ToStockSharp();
			if (state is OrderStates.Done or OrderStates.Failed)
				using (_sync.EnterScope())
					_activeOrderIds.Remove(order.OrderId);

			KeyValuePair<long, OrderSubscription>[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _orderSubscriptions.Where(pair =>
					MatchesOrderSubscription(pair.Value, market.Market,
						order.OrderId, side))];
			foreach (var pair in subscriptions)
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					HasOrderInfo = true,
					SecurityId = market.Market.ToStockSharp(),
					ServerTime = (order.UpdatedAt > 0
						? order.UpdatedAt
						: order.CreatedAt).FromMilliseconds(CurrentTime),
					PortfolioName = GetPortfolioName(),
					Side = side,
					OrderVolume = order.TotalQuantity,
					Balance = order.RemainingQuantity.Max(0m),
					OrderPrice = order.PricePerUnit,
					OrderType = order.OrderType.ToStockSharp(),
					OrderState = state,
					OrderStringId = order.OrderId,
					TransactionId = tracked.TransactionId,
					OriginalTransactionId = pair.Key,
					Commission = order.FeeAmount == 0
						? null
						: order.FeeAmount.Abs(),
					Error = state == OrderStates.Failed
						? new InvalidOperationException(
							"CoinDCX rejected the order.")
						: null,
				}, cancellationToken);
		}
	}

	private async ValueTask OnPrivateTradesAsync(CoinDCXPrivateTrade[] trades,
		CancellationToken cancellationToken)
	{
		foreach (var trade in trades ?? [])
		{
			if (trade?.TradeId.IsEmpty() != false || trade.OrderId.IsEmpty() ||
				trade.Market.IsEmpty() || !AddAccountTrade(trade.TradeId))
				continue;
			var market = ResolvePrivateMarket(trade.Market);
			var tracked = GetTrackedOrder(trade.OrderId);
			var side = tracked?.Side ?? (trade.IsBuyerMaker ? Sides.Sell : Sides.Buy);
			KeyValuePair<long, OrderSubscription>[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _orderSubscriptions.Where(pair =>
					MatchesOrderSubscription(pair.Value, market.Market,
						trade.OrderId, side))];
			foreach (var pair in subscriptions)
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					SecurityId = market.Market.ToStockSharp(),
					ServerTime = trade.Timestamp.FromMilliseconds(CurrentTime),
					PortfolioName = GetPortfolioName(),
					Side = side,
					OrderStringId = trade.OrderId,
					TradeStringId = trade.TradeId,
					TradePrice = trade.Price,
					TradeVolume = trade.Quantity,
					Commission = trade.Fee == 0 ? null : trade.Fee.Abs(),
					IsMarketMaker = trade.IsBuyerMaker,
					TransactionId = tracked?.TransactionId ??
						CoinDCXExtensions.ParseTransactionId(trade.ClientOrderId),
					OriginalTransactionId = pair.Key,
				}, cancellationToken);
		}
	}

	private MarketDefinition ResolvePrivateMarket(string value)
	{
		using (_sync.EnterScope())
		{
			if (_markets.TryGetValue(value, out var market))
				return market;
			if (_pairs.TryGetValue(value, out market))
				return market;
		}
		return value.Contains('-') ? GetMarketByPair(value) : GetMarket(value);
	}

	private static bool MatchesOrderSubscription(OrderSubscription subscription,
		string market, string orderId, Sides side)
		=> (subscription.Market.IsEmpty() ||
			subscription.Market.EqualsIgnoreCase(market)) &&
			(subscription.OrderId.IsEmpty() ||
			subscription.OrderId.EqualsIgnoreCase(orderId)) &&
			(subscription.Side is null || subscription.Side == side);

	private async ValueTask CompleteOrderStatusAsync(OrderStatusMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
