namespace StockSharp.CoinsPh;

public partial class CoinsPhMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var market = GetMarket(regMsg.SecurityId);
		var request = CreateOrderRequest(market, regMsg.TransactionId,
			regMsg.UserOrderId, regMsg.Side, regMsg.OrderType, regMsg.Volume,
			regMsg.Price, regMsg.TimeInForce, regMsg.PostOnly, regMsg.VisibleVolume,
			regMsg.TillDate is not null, regMsg.Condition);
		var order = await RestClient.PlaceOrderAsync(request, cancellationToken);
		if (order?.OrderId <= 0)
			throw new InvalidDataException(
				"Coins.ph accepted an order without returning its identifier.");

		var tracked = CreateTrackedOrder(regMsg.TransactionId, market,
			regMsg.Side, regMsg.OrderType ?? OrderTypes.Limit, regMsg.Volume.Abs(),
			regMsg.Price, request.ClientOrderId,
			regMsg.Condition as CoinsPhOrderCondition);
		TrackOrder(order.OrderId.ToString(CultureInfo.InvariantCulture), tracked);
		await SendOrderAsync(order, regMsg.TransactionId, tracked,
			cancellationToken);
		await SendFillsAsync(order, tracked, regMsg.TransactionId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var oldIdentity = ResolveOrderId(replaceMsg.OldOrderId,
			replaceMsg.OldOrderStringId, "replacement");
		var previous = GetTrackedOrder(oldIdentity.Key);
		var market = previous is not null
			? GetMarket(previous.Symbol)
			: GetMarket(replaceMsg.SecurityId);
		var request = CreateOrderRequest(market, replaceMsg.TransactionId,
			replaceMsg.UserOrderId, replaceMsg.Side, replaceMsg.OrderType,
			replaceMsg.Volume, replaceMsg.Price, replaceMsg.TimeInForce,
			replaceMsg.PostOnly, replaceMsg.VisibleVolume,
			replaceMsg.TillDate is not null, replaceMsg.Condition);
		var result = await RestClient.CancelReplaceAsync(new()
		{
			Symbol = request.Symbol,
			Side = request.Side,
			Type = request.Type,
			TimeInForce = request.TimeInForce,
			Quantity = request.Quantity,
			QuoteOrderQuantity = request.QuoteOrderQuantity,
			Price = request.Price,
			ClientOrderId = request.ClientOrderId,
			CancelOrderId = oldIdentity.NumericId,
			CancelClientOrderId = oldIdentity.ClientId,
			StopPrice = request.StopPrice,
		}, cancellationToken);
		if (result?.CanceledOrder is not null)
			await SendOrderAsync(result.CanceledOrder, replaceMsg.TransactionId,
				previous, cancellationToken);
		if (result?.NewOrder?.OrderId <= 0)
			throw new InvalidDataException(
				"Coins.ph cancel-replace returned no replacement order.");

		var tracked = CreateTrackedOrder(replaceMsg.TransactionId, market,
			replaceMsg.Side, replaceMsg.OrderType ?? OrderTypes.Limit,
			replaceMsg.Volume.Abs(), replaceMsg.Price, request.ClientOrderId,
			replaceMsg.Condition as CoinsPhOrderCondition);
		TrackOrder(result.NewOrder.OrderId.ToString(CultureInfo.InvariantCulture),
			tracked);
		await SendOrderAsync(result.NewOrder, replaceMsg.TransactionId, tracked,
			cancellationToken);
		await SendFillsAsync(result.NewOrder, tracked, replaceMsg.TransactionId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var identity = ResolveOrderId(cancelMsg.OrderId, cancelMsg.OrderStringId,
			"cancellation");
		var tracked = GetTrackedOrder(identity.Key);
		var order = await RestClient.CancelOrderAsync(new()
		{
			OrderId = identity.NumericId,
			ClientOrderId = identity.ClientId,
		}, cancellationToken);
		await SendOrderAsync(order, cancelMsg.TransactionId, tracked,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"Coins.ph spot bulk cancellation cannot close positions.");

		var symbol = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(cancelMsg.SecurityId).Symbol;
		var openOrders = await RestClient.GetOpenOrdersAsync(new()
		{
			Symbol = symbol,
		}, cancellationToken);
		var selected = (openOrders ?? []).Where(order => order is not null &&
			(cancelMsg.Side is null || order.Side.ToStockSharp() == cancelMsg.Side))
			.ToArray();

		if (cancelMsg.Side is null)
		{
			foreach (var group in selected.GroupBy(static order => order.Symbol,
				StringComparer.OrdinalIgnoreCase))
			{
				var canceled = await RestClient.CancelAllAsync(new()
				{
					Symbol = group.Key,
				}, cancellationToken);
				foreach (var order in canceled ?? [])
					await SendOrderAsync(order, cancelMsg.TransactionId,
						GetTrackedOrder(order.OrderId.ToString(
							CultureInfo.InvariantCulture)), cancellationToken);
			}
			return;
		}

		foreach (var order in selected)
		{
			var canceled = await RestClient.CancelOrderAsync(new()
			{
				OrderId = order.OrderId,
			}, cancellationToken);
			await SendOrderAsync(canceled, cancelMsg.TransactionId,
				GetTrackedOrder(order.OrderId.ToString(CultureInfo.InvariantCulture)),
				cancellationToken);
		}
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
				BoardCode = BoardCodes.CoinsPh,
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

		var symbol = statusMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(statusMsg.SecurityId).Symbol;
		OrderIdentity? identity = statusMsg.HasOrderId()
			? ResolveOrderId(statusMsg.OrderId, statusMsg.OrderStringId, "lookup")
			: null;
		var tracked = identity is OrderIdentity orderIdentity
			? GetTrackedOrder(orderIdentity.Key)
			: null;
		if (symbol.IsEmpty() && tracked is not null)
			symbol = tracked.Symbol;
		var maximum = (statusMsg.Count ?? 500).Min(1000).Max(1).To<int>();

		if (identity is OrderIdentity requested)
		{
			var result = await RestClient.GetOrderAsync(new()
			{
				OrderId = requested.NumericId,
				ClientOrderId = requested.ClientId,
			}, cancellationToken);
			var order = result?.Orders?.FirstOrDefault();
			if (order is null)
				throw new InvalidDataException(
					"Coins.ph returned no matching order.");
			await SendOrderAsync(order, statusMsg.TransactionId, tracked,
				cancellationToken);
		}
		else
		{
			var sentOrders = new HashSet<long>();
			var openOrders = await RestClient.GetOpenOrdersAsync(new()
			{
				Symbol = symbol,
			}, cancellationToken);
			foreach (var order in (openOrders ?? []).Where(order => order is not null &&
				(statusMsg.Side is null ||
					order.Side.ToStockSharp() == statusMsg.Side)).TakeLast(maximum))
			{
				sentOrders.Add(order.OrderId);
				await SendOrderAsync(order, statusMsg.TransactionId,
					GetTrackedOrder(order.OrderId.ToString(CultureInfo.InvariantCulture)),
					cancellationToken);
			}

			if (!symbol.IsEmpty())
			{
				var history = await RestClient.GetOrderHistoryAsync(new()
				{
					Symbol = symbol,
					StartTime = statusMsg.From?.ToUniversalTime().ToMilliseconds(),
					EndTime = statusMsg.To?.ToUniversalTime().ToMilliseconds(),
					Limit = maximum,
				}, cancellationToken);
				foreach (var order in (history ?? []).Where(order => order is not null &&
					!sentOrders.Contains(order.OrderId) &&
					(statusMsg.Side is null ||
						order.Side.ToStockSharp() == statusMsg.Side)).TakeLast(maximum))
					await SendOrderAsync(order, statusMsg.TransactionId,
						GetTrackedOrder(order.OrderId.ToString(
							CultureInfo.InvariantCulture)), cancellationToken);

				var trades = await RestClient.GetMyTradesAsync(new()
				{
					Symbol = symbol,
					StartTime = statusMsg.From?.ToUniversalTime().ToMilliseconds(),
					EndTime = statusMsg.To?.ToUniversalTime().ToMilliseconds(),
					Limit = maximum,
				}, cancellationToken);
				foreach (var trade in trades ?? [])
					if (statusMsg.Side is null ||
						trade.IsBuyer == (statusMsg.Side == Sides.Buy))
						await SendAccountTradeAsync(trade,
							statusMsg.TransactionId, cancellationToken);
			}
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
				Symbol = symbol,
				OrderId = identity?.Key,
				Side = statusMsg.Side,
			};
	}

	private CoinsPhOrderRequest CreateOrderRequest(MarketDefinition market,
		long transactionId, string userOrderId, Sides side,
		OrderTypes? requestedOrderType, decimal requestedVolume, decimal price,
		TimeInForce? timeInForce, bool? isPostOnly, decimal? visibleVolume,
		bool hasTillDate, OrderCondition orderCondition)
	{
		if (!market.IsTrading)
			throw new InvalidOperationException(
				$"Coins.ph market '{market.Symbol}' is not active.");
		var orderType = requestedOrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market or
			OrderTypes.Conditional))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, transactionId));
		var volume = requestedVolume.Abs();
		var condition = orderCondition as CoinsPhOrderCondition;
		if (condition?.IsWithdraw == true)
			throw new NotSupportedException(
				"Coins.ph wallet withdrawals are not exposed by this connector.");
		if (visibleVolume is > 0 && visibleVolume != volume)
			throw new NotSupportedException(
				"Coins.ph does not document iceberg orders.");
		if (hasTillDate)
			throw new NotSupportedException(
				"Coins.ph does not support GTD orders.");

		CoinsPhOrderTypes nativeType;
		decimal? stopPrice = null;
		if (orderType == OrderTypes.Limit)
		{
			if (price <= 0)
				throw new InvalidOperationException(
					"Coins.ph limit orders require a positive price.");
			nativeType = isPostOnly == true
				? CoinsPhOrderTypes.LimitMaker
				: CoinsPhOrderTypes.Limit;
		}
		else if (orderType == OrderTypes.Market)
		{
			if (isPostOnly == true)
				throw new InvalidOperationException(
					"A market order cannot be post-only.");
			if (timeInForce is not null)
				throw new InvalidOperationException(
					"Coins.ph market orders do not accept time-in-force.");
			nativeType = CoinsPhOrderTypes.Market;
		}
		else
		{
			if (condition?.StopPrice is not > 0)
				throw new InvalidOperationException(
					"Coins.ph conditional orders require a positive stop price.");
			if (isPostOnly == true)
				throw new InvalidOperationException(
					"Coins.ph conditional orders cannot be post-only.");
			stopPrice = condition.StopPrice;
			nativeType = (condition.Type, price > 0) switch
			{
				(CoinsPhConditionalOrderTypes.StopLoss, false) =>
					CoinsPhOrderTypes.StopLoss,
				(CoinsPhConditionalOrderTypes.StopLoss, true) =>
					CoinsPhOrderTypes.StopLossLimit,
				(CoinsPhConditionalOrderTypes.TakeProfit, false) =>
					CoinsPhOrderTypes.TakeProfit,
				_ => CoinsPhOrderTypes.TakeProfitLimit,
			};
			if (price <= 0 && timeInForce is not null)
				throw new InvalidOperationException(
					"Coins.ph market-trigger orders do not accept time-in-force.");
		}

		if (!market.OrderTypes.Contains(nativeType))
			throw new NotSupportedException(
				$"Coins.ph does not support {nativeType} for '{market.Symbol}'.");
		if (price > 0)
		{
			ValidateRange(price, market.MinimumPrice, market.MaximumPrice,
				"price", market.Symbol);
			ValidateStep(price, market.PriceStep, "price", market.Symbol);
		}

		var quoteAmount = condition?.QuoteAmount;
		decimal? quantity;
		decimal? quoteOrderQuantity;
		if (nativeType is CoinsPhOrderTypes.Market)
		{
			quantity = quoteAmount is > 0 ? null : volume;
			quoteOrderQuantity = quoteAmount is > 0 ? quoteAmount : null;
		}
		else if (nativeType is CoinsPhOrderTypes.StopLoss or
			CoinsPhOrderTypes.TakeProfit)
		{
			if (side == Sides.Buy)
			{
				if (quoteAmount is not > 0)
					throw new InvalidOperationException(
						"Coins.ph market-trigger buy orders require " +
						"CoinsPhOrderCondition.QuoteAmount.");
				quantity = null;
				quoteOrderQuantity = quoteAmount;
			}
			else
			{
				quantity = volume;
				quoteOrderQuantity = null;
			}
		}
		else
		{
			quantity = volume;
			quoteOrderQuantity = null;
		}

		if (quantity is decimal baseQuantity)
		{
			if (baseQuantity <= 0)
				throw new InvalidOperationException(
					"Coins.ph order volume must be positive.");
			ValidateRange(baseQuantity, market.MinimumQuantity,
				market.MaximumQuantity, "volume", market.Symbol);
			ValidateStep(baseQuantity, market.QuantityStep, "volume",
				market.Symbol);
		}
		if (quoteOrderQuantity is decimal quote && quote <= 0)
			throw new InvalidOperationException(
				"Coins.ph quote amount must be positive.");

		var estimatedNotional = quoteOrderQuantity ??
			(price > 0 && quantity is decimal qty ? price * qty : 0m);
		if (estimatedNotional > 0)
		{
			ValidateRange(estimatedNotional, market.MinimumNotional,
				market.MaximumNotional, "notional", market.Symbol);
		}

		var usesTimeInForce = nativeType is CoinsPhOrderTypes.Limit or
			CoinsPhOrderTypes.StopLossLimit or CoinsPhOrderTypes.TakeProfitLimit;
		return new()
		{
			Symbol = market.Symbol,
			Side = side.ToCoinsPh(),
			Type = nativeType,
			TimeInForce = usesTimeInForce ? timeInForce.ToCoinsPh() : null,
			Quantity = quantity,
			QuoteOrderQuantity = quoteOrderQuantity,
			Price = price > 0 ? price : null,
			ClientOrderId = CoinsPhExtensions.CreateClientId(transactionId,
				userOrderId),
			StopPrice = stopPrice,
		};
	}

	private static void ValidateRange(decimal value, decimal minimum,
		decimal maximum, string name, string symbol)
	{
		if (minimum > 0 && value < minimum)
			throw new InvalidOperationException(
				$"Coins.ph {name} must be at least {minimum} for '{symbol}'.");
		if (maximum > 0 && value > maximum)
			throw new InvalidOperationException(
				$"Coins.ph {name} must not exceed {maximum} for '{symbol}'.");
	}

	private static void ValidateStep(decimal value, decimal step, string name,
		string symbol)
	{
		if (step > 0 && value % step != 0)
			throw new InvalidOperationException(
				$"Coins.ph {name} must be aligned to step {step} for '{symbol}'.");
	}

	private static TrackedOrder CreateTrackedOrder(long transactionId,
		MarketDefinition market, Sides side, OrderTypes orderType, decimal volume,
		decimal price, string clientOrderId, CoinsPhOrderCondition condition)
		=> new()
		{
			TransactionId = transactionId,
			Symbol = market.Symbol,
			ClientOrderId = clientOrderId,
			Side = side,
			OrderType = orderType,
			Volume = volume,
			Price = price,
			Condition = condition?.Clone() as CoinsPhOrderCondition,
		};

	private async ValueTask SendPortfolioSnapshotAsync(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var account = await RestClient.GetAccountAsync(cancellationToken);
		foreach (var balance in account?.Balances ?? [])
			await SendBalanceAsync(balance.Asset, balance.Available, balance.Locked,
				originalTransactionId,
				account.UpdateTime.FromMilliseconds(CurrentTime), cancellationToken);
	}

	private ValueTask SendBalanceAsync(string asset, decimal available,
		decimal locked, long originalTransactionId, DateTime serverTime,
		CancellationToken cancellationToken)
	{
		if (asset.IsEmpty())
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(),
			SecurityId = asset.ToStockSharp(),
			ServerTime = serverTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, available, true)
		.TryAdd(PositionChangeTypes.BlockedValue, locked, true),
			cancellationToken);
	}

	private async ValueTask SendOrderAsync(CoinsPhOrder order,
		long originalTransactionId, TrackedOrder tracked,
		CancellationToken cancellationToken)
	{
		if (order?.OrderId <= 0)
			return;
		var orderId = order.OrderId.ToString(CultureInfo.InvariantCulture);
		var symbol = order.Symbol.IsEmpty() ? tracked?.Symbol : order.Symbol;
		if (symbol.IsEmpty())
			return;
		var market = GetMarket(symbol);
		tracked ??= GetTrackedOrder(orderId) ?? new TrackedOrder
		{
			TransactionId = CoinsPhExtensions.ParseTransactionId(order.ClientOrderId),
			Symbol = market.Symbol,
			ClientOrderId = order.ClientOrderId,
			Side = order.Side.ToStockSharp(),
			OrderType = order.Type.ToStockSharp(),
			Volume = order.OriginalQuantity,
			Price = order.Price,
			Condition = CreateCondition(order.Type, order.StopPrice,
				order.OriginalQuoteOrderQuantity),
		};
		TrackOrder(orderId, tracked);
		var state = order.Status.ToStockSharp();
		if (state is OrderStates.Done or OrderStates.Failed)
			using (_sync.EnterScope())
				_activeOrderIds.Remove(orderId);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = (order.UpdatedTime > 0
				? order.UpdatedTime
				: order.TransactionTime > 0
					? order.TransactionTime
					: order.CreatedTime).FromMilliseconds(CurrentTime),
			PortfolioName = GetPortfolioName(),
			Side = order.Side.ToStockSharp(),
			OrderVolume = order.OriginalQuantity,
			Balance = (order.OriginalQuantity - order.ExecutedQuantity).Max(0m),
			OrderPrice = order.Price,
			OrderType = order.Type.ToStockSharp(),
			OrderState = state,
			OrderId = order.OrderId,
			TransactionId = tracked.TransactionId,
			OriginalTransactionId = originalTransactionId,
			TimeInForce = order.TimeInForce.ToStockSharp(),
			PostOnly = order.Type == CoinsPhOrderTypes.LimitMaker,
			Condition = tracked.Condition ?? CreateCondition(order.Type,
				order.StopPrice, order.OriginalQuoteOrderQuantity),
			Commission = order.Fills?.Sum(static fill => fill.Commission) is > 0m
				? order.Fills.Sum(static fill => fill.Commission)
				: null,
			Error = state == OrderStates.Failed
				? new InvalidOperationException("Coins.ph rejected the order.")
				: null,
		}, cancellationToken);
	}

	private async ValueTask SendFillsAsync(CoinsPhOrder order,
		TrackedOrder tracked, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		foreach (var fill in order?.Fills ?? [])
		{
			if (fill is null || fill.TradeId.IsEmpty() || fill.Price <= 0 ||
				fill.Quantity <= 0 || !AddAccountTrade(fill.TradeId))
				continue;
			var message = new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				SecurityId = tracked.Symbol.ToStockSharp(),
				ServerTime = order.TransactionTime.FromMilliseconds(CurrentTime),
				PortfolioName = GetPortfolioName(),
				Side = tracked.Side,
				OrderId = order.OrderId,
				TradePrice = fill.Price,
				TradeVolume = fill.Quantity,
				Commission = fill.Commission > 0 ? fill.Commission : null,
				TransactionId = tracked.TransactionId,
				OriginalTransactionId = originalTransactionId,
			};
			if (long.TryParse(fill.TradeId, NumberStyles.None,
				CultureInfo.InvariantCulture, out var tradeId))
				message.TradeId = tradeId;
			else
				message.TradeStringId = fill.TradeId;
			await SendOutMessageAsync(message, cancellationToken);
		}
	}

	private ValueTask SendAccountTradeAsync(CoinsPhAccountTrade trade,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (trade is null || trade.TradeId <= 0 || trade.OrderId <= 0 ||
			trade.Symbol.IsEmpty() || !AddAccountTrade(
				trade.TradeId.ToString(CultureInfo.InvariantCulture)))
			return default;
		var market = GetMarket(trade.Symbol);
		var tracked = GetTrackedOrder(
			trade.OrderId.ToString(CultureInfo.InvariantCulture));
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = trade.Timestamp.FromMilliseconds(CurrentTime),
			PortfolioName = GetPortfolioName(),
			Side = trade.IsBuyer ? Sides.Buy : Sides.Sell,
			OrderId = trade.OrderId,
			TradeId = trade.TradeId,
			TradePrice = trade.Price,
			TradeVolume = trade.Quantity,
			Commission = trade.Commission > 0 ? trade.Commission : null,
			IsMarketMaker = trade.IsMaker,
			TransactionId = tracked?.TransactionId ?? 0,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private async ValueTask OnPrivateMessageAsync(CoinsPhUserStreamMessage message,
		CancellationToken cancellationToken)
	{
		if (message is null || message.Event.IsEmpty())
			return;
		switch (message.Event)
		{
			case "outboundAccountPosition":
				await OnPrivateBalancesAsync(message, cancellationToken);
				break;
			case "balanceUpdate":
				await RefreshPrivateSnapshotsAsync(cancellationToken);
				break;
			case "executionReport":
				await OnPrivateExecutionAsync(message, cancellationToken);
				break;
		}
	}

	private async ValueTask OnPrivateBalancesAsync(CoinsPhUserStreamMessage message,
		CancellationToken cancellationToken)
	{
		long[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _portfolioSubscriptions];
		var serverTime = (message.AccountUpdateTime > 0
			? message.AccountUpdateTime
			: message.EventTime).FromMilliseconds(CurrentTime);
		foreach (var subscriptionId in subscriptions)
			foreach (var balance in message.Balances ?? [])
				await SendBalanceAsync(balance.Asset, balance.Available,
					balance.Locked, subscriptionId, serverTime, cancellationToken);
	}

	private async ValueTask OnPrivateExecutionAsync(
		CoinsPhUserStreamMessage update, CancellationToken cancellationToken)
	{
		if (update.OrderId <= 0 || update.Symbol.IsEmpty())
			return;
		var market = GetMarket(update.Symbol);
		var orderId = update.OrderId.ToString(CultureInfo.InvariantCulture);
		var side = update.Side.ToStockSharp();
		var tracked = GetTrackedOrder(orderId) ?? new TrackedOrder
		{
			TransactionId = CoinsPhExtensions.ParseTransactionId(
				update.ClientOrderId),
			Symbol = market.Symbol,
			ClientOrderId = update.ClientOrderId,
			Side = side,
			OrderType = update.OrderType.ToStockSharp(),
			Volume = update.OriginalQuantity,
			Price = update.Price,
			Condition = CreateCondition(update.OrderType, update.StopPrice,
				update.QuoteOrderQuantity),
		};
		TrackOrder(orderId, tracked);
		var state = update.OrderStatus.ToStockSharp();
		if (state is OrderStates.Done or OrderStates.Failed)
			using (_sync.EnterScope())
				_activeOrderIds.Remove(orderId);

		KeyValuePair<long, OrderSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _orderSubscriptions.Where(pair =>
				MatchesOrderSubscription(pair.Value, market.Symbol, orderId, side))];
		var serverTime = (update.TransactionTime > 0
			? update.TransactionTime
			: update.EventTime).FromMilliseconds(CurrentTime);
		foreach (var pair in subscriptions)
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = market.Symbol.ToStockSharp(),
				ServerTime = serverTime,
				PortfolioName = GetPortfolioName(),
				Side = side,
				OrderVolume = update.OriginalQuantity,
				Balance = (update.OriginalQuantity -
					update.CumulativeExecutedQuantity).Max(0m),
				OrderPrice = update.Price,
				OrderType = update.OrderType.ToStockSharp(),
				OrderState = state,
				OrderId = update.OrderId,
				TransactionId = tracked.TransactionId,
				OriginalTransactionId = pair.Key,
				TimeInForce = update.TimeInForce.ToStockSharp(),
				PostOnly = update.OrderType == CoinsPhOrderTypes.LimitMaker,
				Condition = tracked.Condition,
				Commission = update.Commission > 0
					? update.Commission
					: null,
				Error = state == OrderStates.Failed
					? new InvalidOperationException(
						$"Coins.ph rejected the order: {update.RejectReason}")
					: null,
			}, cancellationToken);

		if (update.ExecutionType != CoinsPhExecutionTypes.Trade ||
			update.TradeId <= 0 || update.LastExecutedPrice <= 0 ||
			update.LastExecutedQuantity <= 0 || !AddAccountTrade(
				update.TradeId.ToString(CultureInfo.InvariantCulture)))
			return;
		foreach (var pair in subscriptions)
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				SecurityId = market.Symbol.ToStockSharp(),
				ServerTime = serverTime,
				PortfolioName = GetPortfolioName(),
				Side = side,
				OrderId = update.OrderId,
				TradeId = update.TradeId,
				TradePrice = update.LastExecutedPrice,
				TradeVolume = update.LastExecutedQuantity,
				Commission = update.Commission > 0
					? update.Commission
					: null,
				IsMarketMaker = update.IsMaker,
				TransactionId = tracked.TransactionId,
				OriginalTransactionId = pair.Key,
			}, cancellationToken);
	}

	private static bool MatchesOrderSubscription(OrderSubscription subscription,
		string symbol, string orderId, Sides side)
		=> (subscription.Symbol.IsEmpty() ||
			subscription.Symbol.EqualsIgnoreCase(symbol)) &&
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
