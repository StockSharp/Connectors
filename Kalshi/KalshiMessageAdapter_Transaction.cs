namespace StockSharp.Kalshi;

public partial class KalshiMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		ValidatePortfolio(regMsg.PortfolioName);
		EnsureAccountReady();
		var market = await GetMarketAsync(regMsg.SecurityId, cancellationToken);
		var condition = regMsg.Condition as KalshiOrderCondition ?? new();
		await PlaceOrderAsync(market, regMsg.TransactionId, regMsg.Side,
			regMsg.Volume.Abs(), regMsg.Price,
			regMsg.OrderType ?? OrderTypes.Limit, regMsg.TimeInForce,
			regMsg.TillDate, regMsg.PostOnly == true || condition.IsPostOnly,
			condition, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		ValidatePortfolio(replaceMsg.PortfolioName);
		EnsureAccountReady();
		if ((replaceMsg.OrderType ?? OrderTypes.Limit) != OrderTypes.Limit)
			throw new NotSupportedException(
				"Kalshi can amend only a resting limit order.");
		var market = await GetMarketAsync(replaceMsg.SecurityId,
			cancellationToken);
		var orderId = ResolveOrderId(replaceMsg.OldOrderStringId,
			replaceMsg.OldOrderId, replaceMsg.TransactionId);
		var current = await RestClient.GetOrderAsync(orderId, cancellationToken);
		var volume = replaceMsg.Volume.Abs();
		var filled = current.FilledVolume.ParseKalshiDecimal("filled order volume");
		if (volume < filled)
			throw new ArgumentOutOfRangeException(nameof(replaceMsg), volume,
				"Kalshi amended total volume cannot be below already filled volume.");
		if (!market.IsPriceValid(replaceMsg.Price))
			throw new ArgumentOutOfRangeException(nameof(replaceMsg),
				replaceMsg.Price, "Price is not valid for the Kalshi market's current price bands.");
		var clientOrderId = Guid.NewGuid().ToString("D");
		var response = await RestClient.AmendOrderAsync(orderId, Subaccount,
			new()
			{
				Ticker = market.Ticker,
				Side = replaceMsg.Side.ToKalshi(),
				Price = replaceMsg.Price.ToKalshiPrice(),
				Volume = volume.ToKalshiCount(),
				ClientOrderId = current.ClientOrderId,
				UpdatedClientOrderId = clientOrderId,
				ExchangeIndex = 0,
			}, cancellationToken);
		if (response?.OrderId.IsEmpty() != false)
			throw new InvalidDataException(
				"Kalshi did not return the amended order identifier.");
		TrackOrder(response.OrderId, replaceMsg.TransactionId);
		var remaining = response.RemainingVolume.TryParseKalshiDecimal() ??
			Math.Max(0m, volume - filled);
		var time = response.Timestamp.FromKalshiMilliseconds();
		UpdateServerTime(time);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = replaceMsg.Side,
			OrderPrice = replaceMsg.Price,
			OrderVolume = volume,
			Balance = remaining,
			OrderType = OrderTypes.Limit,
			OrderState = remaining > 0 ? OrderStates.Active : OrderStates.Done,
			OrderStringId = response.OrderId,
			TransactionId = replaceMsg.TransactionId,
			OriginalTransactionId = replaceMsg.TransactionId,
			TimeInForce = TimeInForce.PutInQueue,
		}, cancellationToken);
		SchedulePrivatePoll();
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		ValidatePortfolio(cancelMsg.PortfolioName);
		EnsureAccountReady();
		var orderId = ResolveOrderId(cancelMsg.OrderStringId, cancelMsg.OrderId,
			cancelMsg.TransactionId);
		var response = await RestClient.CancelOrderAsync(orderId, Subaccount,
			cancellationToken);
		if (response?.OrderId.IsEmpty() != false)
			throw new InvalidDataException(
				"Kalshi did not confirm the canceled order identifier.");
		var time = response.Timestamp.FromKalshiMilliseconds();
		UpdateServerTime(time);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = cancelMsg.SecurityId,
			ServerTime = time,
			PortfolioName = _portfolioName,
			OrderStringId = response.OrderId,
			OrderState = OrderStates.Done,
			Balance = 0m,
			TransactionId = GetOrderTransactionId(response.OrderId,
				cancelMsg.TransactionId),
			OriginalTransactionId = cancelMsg.TransactionId,
		}, cancellationToken);
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
				"Kalshi bulk cancellation does not close event-contract positions.");
		if (cancelMsg.IsStop == true)
			throw new NotSupportedException(
				"Kalshi Trade API does not expose stop orders.");
		var market = !cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? await GetMarketAsync(cancelMsg.SecurityId, cancellationToken)
			: null;
		var orders = await RestClient.GetOrdersAsync(Subaccount, HistoryLimit,
			cancellationToken);
		foreach (var order in orders
			.Where(static order => order?.OrderId.IsEmpty() == false &&
				order.Status == KalshiOrderStatuses.Resting)
			.Where(order => market is null || order.Ticker.Equals(market.Ticker,
				StringComparison.OrdinalIgnoreCase))
			.Where(order => cancelMsg.Side is null ||
				order.BookSide.ToStockSharp() == cancelMsg.Side))
			await RestClient.CancelOrderAsync(order.OrderId, Subaccount,
				cancellationToken);
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
			using (_sync.EnterScope())
				_portfolioSubscriptions.Remove(lookupMsg.OriginalTransactionId);
			return;
		}
		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = _portfolioName,
			BoardCode = BoardCodes.Kalshi,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId,
			cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_portfolioSubscriptions.Add(lookupMsg.TransactionId);
		await SocketClient.EnsureAccountSubscriptionsAsync(cancellationToken);
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
		if (statusMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}
		var filter = CreateOrderSubscription(statusMsg);
		var ordersTask = RestClient.GetOrdersAsync(Subaccount, HistoryLimit,
			cancellationToken).AsTask();
		var fillsTask = RestClient.GetFillsAsync(Subaccount, HistoryLimit,
			cancellationToken).AsTask();
		await Task.WhenAll(ordersTask, fillsTask);
		await SendOrderSnapshotAsync(await ordersTask, await fillsTask, filter,
			statusMsg.TransactionId, false, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_orderSubscriptions.Add(statusMsg.TransactionId, filter);
		await SocketClient.EnsureAccountSubscriptionsAsync(cancellationToken);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private async ValueTask PlaceOrderAsync(KalshiMarket market,
		long transactionId, Sides side, decimal volume, decimal price,
		OrderTypes orderType, TimeInForce? timeInForce, DateTime? tillDate,
		bool isPostOnly, KalshiOrderCondition condition,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		ArgumentNullException.ThrowIfNull(condition);
		if (!market.IsTrading())
			throw new InvalidOperationException(
				$"Kalshi market '{market.Ticker}' is not accepting orders.");
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		KalshiTimeInForces nativeTimeInForce;
		if (orderType == OrderTypes.Market)
		{
			if (isPostOnly)
				throw new InvalidOperationException(
					"A Kalshi market order cannot be post-only.");
			price = await CalculateMarketablePriceAsync(market, side, volume,
				cancellationToken);
			nativeTimeInForce = timeInForce == TimeInForce.MatchOrCancel
				? KalshiTimeInForces.FillOrKill
				: KalshiTimeInForces.ImmediateOrCancel;
			tillDate = null;
		}
		else
		{
			if (!market.IsPriceValid(price))
				throw new ArgumentOutOfRangeException(nameof(price), price,
					"Price is not valid for the Kalshi market's current price bands.");
			nativeTimeInForce = timeInForce switch
			{
				TimeInForce.MatchOrCancel => KalshiTimeInForces.FillOrKill,
				TimeInForce.CancelBalance => KalshiTimeInForces.ImmediateOrCancel,
				_ => KalshiTimeInForces.GoodTillCanceled,
			};
		}
		long? expiration = null;
		if (tillDate is DateTime expiry)
		{
			if (nativeTimeInForce != KalshiTimeInForces.GoodTillCanceled)
				throw new InvalidOperationException(
					"Kalshi expiration is supported only for queued limit orders.");
			expiry = expiry.EnsureUtc();
			if (expiry <= DateTime.UtcNow)
				throw new ArgumentOutOfRangeException(nameof(tillDate), tillDate,
					"Kalshi order expiration must be in the future.");
			expiration = expiry.ToKalshiSeconds();
		}
		var response = await RestClient.CreateOrderAsync(new()
		{
			Ticker = market.Ticker,
			ClientOrderId = Guid.NewGuid().ToString("D"),
			Side = side.ToKalshi(),
			Volume = volume.ToKalshiCount(),
			Price = price.ToKalshiPrice(),
			ExpirationTime = expiration,
			TimeInForce = nativeTimeInForce,
			IsPostOnly = isPostOnly,
			SelfTradePreventionType = condition.SelfTradePreventionType,
			IsCancelOnPause = condition.IsCancelOnPause,
			IsReduceOnly = condition.IsReduceOnly,
			Subaccount = Subaccount,
			OrderGroupId = condition.OrderGroupId,
			ExchangeIndex = 0,
		}, cancellationToken);
		if (response?.OrderId.IsEmpty() != false)
			throw new InvalidDataException(
				"Kalshi accepted the request without returning an order identifier.");
		TrackOrder(response.OrderId, transactionId);
		var remaining = response.RemainingVolume.ParseKalshiDecimal(
			"remaining order volume");
		var time = response.Timestamp.FromKalshiMilliseconds();
		UpdateServerTime(time);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = side,
			OrderPrice = price,
			OrderVolume = volume,
			Balance = remaining,
			OrderType = orderType,
			OrderState = remaining > 0 ? OrderStates.Active : OrderStates.Done,
			OrderStringId = response.OrderId,
			TransactionId = transactionId,
			OriginalTransactionId = transactionId,
			TimeInForce = nativeTimeInForce.ToStockSharp(),
			ExpiryDate = tillDate?.EnsureUtc(),
		}, cancellationToken);
		SchedulePrivatePoll();
	}

	private async ValueTask<decimal> CalculateMarketablePriceAsync(
		KalshiMarket market, Sides side, decimal volume,
		CancellationToken cancellationToken)
	{
		if (volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(volume));
		var book = await RestClient.GetOrderBookAsync(market.Ticker, MarketDepth,
			cancellationToken);
		var state = new KalshiBookState();
		state.ApplyRest(book, DateTime.UtcNow);
		var levels = side == Sides.Buy ? state.Asks : state.Bids;
		var remaining = volume;
		var boundary = 0m;
		foreach (var level in levels)
		{
			boundary = level.Key;
			remaining -= level.Value;
			if (remaining <= 0)
				break;
		}
		if (remaining > 0 || boundary <= 0)
			throw new InvalidOperationException(
				$"Kalshi has insufficient liquidity for {volume} contracts of " +
				market.Ticker + ".");
		return boundary;
	}

	private async ValueTask SendPortfolioSnapshotAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		var balanceTask = RestClient.GetBalanceAsync(Subaccount,
			cancellationToken).AsTask();
		var positionsTask = RestClient.GetPositionsAsync(Subaccount,
			HistoryLimit, cancellationToken).AsTask();
		await Task.WhenAll(balanceTask, positionsTask);
		var balance = await balanceTask;
		var time = balance.UpdatedTime.FromKalshiSeconds();
		UpdateServerTime(time);
		await SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = new()
			{
				SecurityCode = "USD",
				BoardCode = BoardCodes.Kalshi,
			},
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}.TryAdd(PositionChangeTypes.CurrentValue,
			balance.Available.ParseKalshiDecimal("account balance"), true),
			cancellationToken);

		var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var position in await positionsTask ?? [])
		{
			if (position?.Ticker.IsEmpty() != false)
				continue;
			var market = GetCachedMarket(position.Ticker) ??
				await GetMarketAsync(new SecurityId
				{
					SecurityCode = position.Ticker,
					BoardCode = BoardCodes.Kalshi,
				}, cancellationToken);
			current.Add(position.Ticker);
			var value = position.Position.ParseKalshiDecimal("position");
			var exposure = position.MarketExposure.TryParseKalshiDecimal();
			decimal? average = value == 0 || exposure is null
				? null
				: exposure.Value.Abs() / value.Abs();
			var positionTime = position.LastUpdatedTime.TryParseKalshiTime() ?? time;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = market.ToStockSharp(),
				ServerTime = positionTime,
				OriginalTransactionId = transactionId,
			}.TryAdd(PositionChangeTypes.CurrentValue, value, true)
			.TryAdd(PositionChangeTypes.AveragePrice, average, true)
			.TryAdd(PositionChangeTypes.CurrentPrice,
				market.LastPrice.TryParseKalshiDecimal(), true)
			.TryAdd(PositionChangeTypes.RealizedPnL,
				position.RealizedPnl.TryParseKalshiDecimal(), true),
				cancellationToken);
		}
		string[] removed;
		using (_sync.EnterScope())
		{
			removed = [.. _knownPositions.Where(ticker =>
				!current.Contains(ticker))];
			_knownPositions.Clear();
			_knownPositions.UnionWith(current);
		}
		foreach (var ticker in removed)
		{
			var market = GetCachedMarket(ticker);
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
		foreach (var transactionId in portfolios)
			await SendPortfolioSnapshotAsync(transactionId, cancellationToken);
		if (orders.Length == 0)
			return;
		var ordersTask = RestClient.GetOrdersAsync(Subaccount, HistoryLimit,
			cancellationToken).AsTask();
		var fillsTask = RestClient.GetFillsAsync(Subaccount, HistoryLimit,
			cancellationToken).AsTask();
		await Task.WhenAll(ordersTask, fillsTask);
		foreach (var pair in orders)
			await SendOrderSnapshotAsync(await ordersTask, await fillsTask,
				pair.Value, pair.Key, true, cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(KalshiOrder[] orders,
		KalshiFill[] fills, OrderSubscription filter, long transactionId,
		bool isIncremental, CancellationToken cancellationToken)
	{
		var messages = new List<ExecutionMessage>();
		foreach (var order in orders ?? [])
		{
			var message = await CreateOrderMessageAsync(order, transactionId,
				cancellationToken);
			if (message is not null && IsOrderMatch(message, filter))
				messages.Add(message);
		}
		foreach (var fill in fills ?? [])
		{
			if (fill?.FillId.IsEmpty() != false)
				continue;
			var shouldSend = true;
			using (_sync.EnterScope())
			{
				if (isIncremental && _seenFills.Contains(fill.FillId))
					shouldSend = false;
				_seenFills.Add(fill.FillId);
			}
			if (!shouldSend)
				continue;
			var message = await CreateFillMessageAsync(fill, transactionId,
				cancellationToken);
			if (message is not null && IsTradeMatch(message, filter))
				messages.Add(message);
		}
		foreach (var message in messages
			.OrderBy(static message => message.ServerTime)
			.Skip(filter.Skip).Take(filter.Limit))
			await SendOutMessageAsync(message, cancellationToken);
	}

	private async ValueTask<ExecutionMessage> CreateOrderMessageAsync(
		KalshiOrder order, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (order?.Ticker.IsEmpty() != false || order.OrderId.IsEmpty())
			return null;
		var market = GetCachedMarket(order.Ticker) ?? await GetMarketAsync(new()
		{
			SecurityCode = order.Ticker,
			BoardCode = BoardCodes.Kalshi,
		}, cancellationToken);
		var volume = order.InitialVolume.ParseKalshiDecimal("order volume");
		var remaining = order.RemainingVolume.ParseKalshiDecimal(
			"remaining order volume");
		var time = order.LastUpdateTime.TryParseKalshiTime() ??
			order.CreatedTime.TryParseKalshiTime() ?? DateTime.UtcNow;
		return new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = order.BookSide.ToStockSharp(),
			OrderPrice = order.YesPrice.ParseKalshiDecimal("order price"),
			OrderVolume = volume,
			Balance = remaining,
			OrderType = OrderTypes.Limit,
			OrderState = order.Status.ToStockSharp(),
			OrderStringId = order.OrderId,
			TransactionId = GetOrderTransactionId(order.OrderId,
				originalTransactionId),
			OriginalTransactionId = originalTransactionId,
			TimeInForce = TimeInForce.PutInQueue,
			ExpiryDate = order.ExpirationTime.TryParseKalshiTime(),
		};
	}

	private async ValueTask<ExecutionMessage> CreateFillMessageAsync(
		KalshiFill fill, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (fill?.Ticker.IsEmpty() != false || fill.FillId.IsEmpty())
			return null;
		var market = GetCachedMarket(fill.Ticker) ?? await GetMarketAsync(new()
		{
			SecurityCode = fill.Ticker,
			BoardCode = BoardCodes.Kalshi,
		}, cancellationToken);
		return new()
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = market.ToStockSharp(),
			ServerTime = fill.CreatedTime.TryParseKalshiTime() ??
				fill.Timestamp.FromKalshiSeconds(),
			PortfolioName = _portfolioName,
			Side = fill.BookSide.ToStockSharp(),
			OrderStringId = fill.OrderId,
			TradeStringId = fill.FillId,
			TradePrice = fill.YesPrice.ParseKalshiDecimal("fill price"),
			TradeVolume = fill.Volume.ParseKalshiDecimal("fill volume"),
			Commission = fill.Fee.TryParseKalshiDecimal(),
			OriginalTransactionId = originalTransactionId,
		};
	}

	private async ValueTask OnUserOrderAsync(KalshiSocketEvent item,
		CancellationToken cancellationToken)
	{
		var native = item.Message;
		if (native.Ticker.IsEmpty() || native.OrderId.IsEmpty() ||
			native.BookSide is not KalshiBookSides side ||
			native.OrderStatus is not KalshiOrderStatuses status)
			return;
		if (native.SubaccountNumber is int subaccount && subaccount != Subaccount)
			return;
		var market = GetCachedMarket(native.Ticker) ?? await GetMarketAsync(new()
		{
			SecurityCode = native.Ticker,
			BoardCode = BoardCodes.Kalshi,
		}, cancellationToken);
		var message = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = (native.LastUpdatedTime ?? native.CreatedTime ?? 0)
				.FromKalshiMilliseconds(),
			PortfolioName = _portfolioName,
			Side = side.ToStockSharp(),
			OrderPrice = native.YesPrice.ParseKalshiDecimal("order price"),
			OrderVolume = native.InitialVolume.ParseKalshiDecimal("order volume"),
			Balance = native.RemainingVolume.ParseKalshiDecimal(
				"remaining order volume"),
			OrderType = OrderTypes.Limit,
			OrderState = status.ToStockSharp(),
			OrderStringId = native.OrderId,
			TransactionId = GetOrderTransactionId(native.OrderId, 0),
			ExpiryDate = native.ExpirationTime?.FromKalshiMilliseconds(),
		};
		KeyValuePair<long, OrderSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _orderSubscriptions];
		foreach (var subscription in subscriptions)
			if (IsOrderMatch(message, subscription.Value))
			{
				message.OriginalTransactionId = subscription.Key;
				await SendOutMessageAsync(message.Clone(), cancellationToken);
			}
		SchedulePrivatePoll();
	}

	private async ValueTask OnUserFillAsync(KalshiSocketEvent item,
		CancellationToken cancellationToken)
	{
		var native = item.Message;
		if (native.TradeId.IsEmpty() || native.Ticker.IsEmpty() ||
			native.BookSide is not KalshiBookSides side)
			return;
		if (native.Subaccount is int subaccount && subaccount != Subaccount)
			return;
		using (_sync.EnterScope())
			if (!_seenFills.Add(native.TradeId))
				return;
		var market = GetCachedMarket(native.Ticker) ?? await GetMarketAsync(new()
		{
			SecurityCode = native.Ticker,
			BoardCode = BoardCodes.Kalshi,
		}, cancellationToken);
		var message = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = market.ToStockSharp(),
			ServerTime = (native.Timestamp ?? 0).FromKalshiMilliseconds(),
			PortfolioName = _portfolioName,
			Side = side.ToStockSharp(),
			OrderStringId = native.OrderId,
			TradeStringId = native.TradeId,
			TradePrice = native.YesPrice.ParseKalshiDecimal("fill price"),
			TradeVolume = native.TradeVolume.ParseKalshiDecimal("fill volume"),
			Commission = native.Fee.TryParseKalshiDecimal(),
		};
		KeyValuePair<long, OrderSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _orderSubscriptions];
		foreach (var subscription in subscriptions)
			if (IsTradeMatch(message, subscription.Value))
			{
				message.OriginalTransactionId = subscription.Key;
				await SendOutMessageAsync(message.Clone(), cancellationToken);
			}
		SchedulePrivatePoll();
	}

	private async ValueTask OnMarketPositionAsync(KalshiSocketEvent item,
		CancellationToken cancellationToken)
	{
		var native = item.Message;
		if (native.Ticker.IsEmpty())
			return;
		if (native.Subaccount is int subaccount && subaccount != Subaccount)
			return;
		long[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _portfolioSubscriptions];
		if (subscriptions.Length == 0)
			return;
		var market = GetCachedMarket(native.Ticker) ?? await GetMarketAsync(new()
		{
			SecurityCode = native.Ticker,
			BoardCode = BoardCodes.Kalshi,
		}, cancellationToken);
		var time = native.Timestamp?.FromKalshiMilliseconds() ?? DateTime.UtcNow;
		foreach (var transactionId in subscriptions)
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = market.ToStockSharp(),
				ServerTime = time,
				OriginalTransactionId = transactionId,
			}.TryAdd(PositionChangeTypes.CurrentValue,
				native.Position.TryParseKalshiDecimal(), true)
			.TryAdd(PositionChangeTypes.CurrentPrice,
				market.LastPrice.TryParseKalshiDecimal(), true)
			.TryAdd(PositionChangeTypes.RealizedPnL,
				native.RealizedPnl.TryParseKalshiDecimal(), true),
				cancellationToken);
	}

	private OrderSubscription CreateOrderSubscription(OrderStatusMessage message)
		=> new()
		{
			SecurityId = message.SecurityId,
			SecurityIds = message.SecurityIds,
			OrderId = message.OrderStringId,
			Side = message.Side,
			Volume = message.Volume,
			States = message.States,
			From = message.From?.EnsureUtc(),
			To = message.To?.EnsureUtc(),
			Skip = Math.Max(0, message.Skip ?? 0).To<int>(),
			Limit = (message.Count ?? HistoryLimit).Min(HistoryLimit).Max(1)
				.To<int>(),
		};

	private static string ResolveOrderId(string orderStringId, long? orderId,
		long transactionId)
	{
		if (!orderStringId.IsEmpty())
			return orderStringId.Trim();
		if (orderId is long numeric)
			return numeric.ToString(CultureInfo.InvariantCulture);
		throw new InvalidOperationException(
			"Kalshi order string ID is required for transaction " +
			transactionId.ToString(CultureInfo.InvariantCulture) + ".");
	}

	private static bool IsOrderMatch(ExecutionMessage message,
		OrderSubscription filter)
	{
		if (!IsSecurityMatch(message.SecurityId, filter.SecurityId))
			return false;
		if (filter.SecurityIds?.Length > 0 && !filter.SecurityIds.Any(
			securityId => IsSecurityMatch(message.SecurityId, securityId)))
			return false;
		if (!filter.OrderId.IsEmpty() && !filter.OrderId.Equals(
			message.OrderStringId, StringComparison.OrdinalIgnoreCase))
			return false;
		if (filter.Side is Sides side && message.Side != side)
			return false;
		if (filter.Volume is decimal volume && message.OrderVolume != volume)
			return false;
		if (filter.States?.Length > 0 &&
			(message.OrderState is not OrderStates state ||
				!filter.States.Contains(state)))
			return false;
		if (filter.From is DateTime from && message.ServerTime < from)
			return false;
		if (filter.To is DateTime to && message.ServerTime > to)
			return false;
		return true;
	}

	private static bool IsTradeMatch(ExecutionMessage message,
		OrderSubscription filter)
		=> IsSecurityMatch(message.SecurityId, filter.SecurityId) &&
			(filter.SecurityIds?.Length is not > 0 || filter.SecurityIds.Any(
				securityId => IsSecurityMatch(message.SecurityId, securityId))) &&
			(filter.OrderId.IsEmpty() || filter.OrderId.Equals(
				message.OrderStringId, StringComparison.OrdinalIgnoreCase)) &&
			(filter.Side is null || message.Side == filter.Side) &&
			(filter.Volume is null || message.TradeVolume == filter.Volume) &&
			(filter.States?.Length is not > 0 ||
				filter.States.Contains(OrderStates.Done)) &&
			(filter.From is null || message.ServerTime >= filter.From) &&
			(filter.To is null || message.ServerTime <= filter.To);

	private static bool IsSecurityMatch(SecurityId securityId,
		SecurityId filter)
		=> (filter.SecurityCode.IsEmpty() || filter.SecurityCode.Equals(
			securityId.SecurityCode, StringComparison.OrdinalIgnoreCase)) &&
			(filter.BoardCode.IsEmpty() || filter.BoardCode.Equals(
				securityId.BoardCode, StringComparison.OrdinalIgnoreCase));

	private void SchedulePrivatePoll()
	{
		using (_sync.EnterScope())
			_nextPrivatePoll = CurrentTime;
	}
}
