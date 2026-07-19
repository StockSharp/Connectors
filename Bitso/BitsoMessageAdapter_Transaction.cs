namespace StockSharp.Bitso;

public partial class BitsoMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var market = GetMarket(regMsg.SecurityId);
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException("Bitso order volume must be positive.");
		if (regMsg.VisibleVolume is > 0 && regMsg.VisibleVolume != volume)
			throw new NotSupportedException("Bitso does not document iceberg orders.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException("Bitso does not document GTD orders.");

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market or
			OrderTypes.Conditional))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		var condition = regMsg.Condition as BitsoOrderCondition ?? new();
		var isConditional = orderType == OrderTypes.Conditional ||
			condition.StopPrice is not null;
		if (isConditional && condition.StopPrice is not > 0)
			throw new InvalidOperationException(
				"Bitso conditional orders require a positive stop price.");
		if (condition.SlippageTolerance is < 0 or > 100)
			throw new InvalidOperationException(
				"Bitso slippage tolerance must be between 0 and 100 percent.");

		var nativeType = orderType == OrderTypes.Market ||
			isConditional && regMsg.Price <= 0
			? BitsoOrderTypes.Market
			: BitsoOrderTypes.Limit;
		if (nativeType == BitsoOrderTypes.Limit && regMsg.Price <= 0)
			throw new InvalidOperationException(
				"Bitso limit orders require a positive price.");
		if (nativeType == BitsoOrderTypes.Market && regMsg.PostOnly == true)
			throw new InvalidOperationException("A market order cannot be post-only.");

		var originId = BitsoExtensions.CreateOriginId(regMsg.TransactionId,
			regMsg.UserOrderId);
		var result = await RestClient.PlaceOrderAsync(new()
		{
			Book = market.Book,
			Major = volume.ToWire(),
			OriginId = originId,
			Price = nativeType == BitsoOrderTypes.Limit
				? regMsg.Price.ToWire()
				: null,
			Side = regMsg.Side.ToBitso(),
			Stop = condition.StopPrice?.ToWire(),
			TimeInForce = nativeType == BitsoOrderTypes.Limit
				? regMsg.TimeInForce.ToBitso(regMsg.PostOnly == true)
				: null,
			Type = nativeType,
			SlippageTolerance = nativeType == BitsoOrderTypes.Market
				? condition.SlippageTolerance
				: null,
		}, cancellationToken);
		if (result?.OrderId.IsEmpty() != false)
			throw new InvalidDataException(
				"Bitso accepted an order without returning its identifier.");

		var tracked = new TrackedOrder
		{
			TransactionId = regMsg.TransactionId,
			Book = market.Book,
			OriginId = originId,
			Side = regMsg.Side,
			OrderType = isConditional ? OrderTypes.Conditional : orderType,
			Volume = volume,
			Price = regMsg.Price,
			StopPrice = condition.StopPrice,
			TimeInForce = regMsg.TimeInForce,
			IsPostOnly = regMsg.PostOnly == true,
		};
		TrackOrder(result.OrderId, tracked);
		await SendTrackedOrderAsync(result.OrderId, tracked, OrderStates.Active,
			volume, regMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var orderId = ResolveOrderId(replaceMsg.OldOrderId,
			replaceMsg.OldOrderStringId, "replacement");
		var previous = GetTrackedOrder(orderId);
		if (previous?.OrderType == OrderTypes.Market ||
			replaceMsg.OrderType == OrderTypes.Market)
			throw new NotSupportedException(
				"Bitso can modify active limit and stop orders only.");
		var volume = replaceMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException(
				"Bitso replacement volume must be positive.");
		if (replaceMsg.Price <= 0)
			throw new InvalidOperationException(
				"Bitso replacement price must be positive.");
		var condition = replaceMsg.Condition as BitsoOrderCondition;
		if (condition?.StopPrice is <= 0)
			throw new InvalidOperationException("Bitso stop price must be positive.");
		var market = previous is not null
			? GetMarket(previous.Book.ToStockSharp())
			: GetMarket(replaceMsg.SecurityId);

		var result = await RestClient.ModifyOrderAsync(orderId, new()
		{
			Major = volume.ToWire(),
			Price = replaceMsg.Price.ToWire(),
			Stop = condition?.StopPrice?.ToWire(),
		}, cancellationToken);
		var replacementId = result?.OrderId.IsEmpty(orderId);
		var tracked = new TrackedOrder
		{
			TransactionId = replaceMsg.TransactionId,
			Book = market.Book,
			OriginId = previous?.OriginId,
			Side = previous?.Side ?? replaceMsg.Side,
			OrderType = condition?.StopPrice is not null ||
				previous?.OrderType == OrderTypes.Conditional
				? OrderTypes.Conditional
				: OrderTypes.Limit,
			Volume = volume,
			Price = replaceMsg.Price,
			StopPrice = condition?.StopPrice ?? previous?.StopPrice,
			TimeInForce = replaceMsg.TimeInForce ?? previous?.TimeInForce,
			IsPostOnly = replaceMsg.PostOnly ?? previous?.IsPostOnly ?? false,
		};
		TrackOrder(replacementId, tracked);
		await SendTrackedOrderAsync(replacementId, tracked, OrderStates.Active,
			volume, replaceMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var orderId = ResolveOrderId(cancelMsg.OrderId, cancelMsg.OrderStringId,
			"cancellation");
		var result = await RestClient.CancelOrderAsync(orderId, cancellationToken);
		if (result is null)
			throw new InvalidDataException("Bitso returned no cancellation result.");
		TrackedOrder tracked;
		using (_sync.EnterScope())
		{
			_knownActiveOrderIds.Remove(orderId);
			tracked = _trackedOrders.TryGetValue(orderId, out var value) ? value : null;
		}
		if (tracked is not null)
		{
			await SendTrackedOrderAsync(orderId, tracked, OrderStates.Done, 0m,
				cancelMsg.TransactionId, cancellationToken);
			return;
		}
		if (!cancelMsg.SecurityId.SecurityCode.IsEmpty())
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = GetMarket(cancelMsg.SecurityId).Book.ToStockSharp(),
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
				"Bitso spot bulk cancellation cannot close positions.");
		var book = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(cancelMsg.SecurityId).Book;
		var orders = await RestClient.GetOpenOrdersAsync(new()
		{
			Book = book,
			Limit = 500,
		}, cancellationToken);
		var ids = (orders ?? []).Where(order =>
			order?.OrderId.IsEmpty() == false &&
			(cancelMsg.Side is null || order.Side.ToStockSharp() == cancelMsg.Side))
			.Select(static order => order.OrderId).ToArray();
		foreach (var batch in ids.Chunk(50))
			_ = await RestClient.CancelOrdersAsync(batch, cancellationToken);
		using (_sync.EnterScope())
			foreach (var id in ids)
				_knownActiveOrderIds.Remove(id);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsurePrivateReady();
		if (!lookupMsg.IsSubscribe)
		{
			_portfolioSubscriptionId = 0;
			return;
		}
		var portfolio = GetPortfolioName();
		if (lookupMsg.PortfolioName.IsEmpty() ||
			lookupMsg.PortfolioName.EqualsIgnoreCase(portfolio))
		{
			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = portfolio,
				BoardCode = BoardCodes.Bitso,
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
		_portfolioSubscriptionId = lookupMsg.TransactionId;
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		EnsurePrivateReady();
		if (!statusMsg.IsSubscribe)
		{
			_orderStatusSubscriptionId = 0;
			return;
		}
		if (statusMsg.Count is <= 0)
		{
			await CompleteOrderStatusAsync(statusMsg, cancellationToken);
			return;
		}

		var book = statusMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(statusMsg.SecurityId).Book;
		var orderId = statusMsg.HasOrderId()
			? ResolveOrderId(statusMsg.OrderId, statusMsg.OrderStringId, "lookup")
			: null;
		var maximum = (statusMsg.Count ?? 500).Min(500).Max(1).To<int>();
		await SendOrderSnapshotAsync(statusMsg.TransactionId, book, orderId,
			statusMsg.From, statusMsg.To, maximum, statusMsg, cancellationToken);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}
		_orderStatusSubscriptionId = statusMsg.TransactionId;
	}

	private async ValueTask SendPortfolioSnapshotAsync(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var balances = await RestClient.GetBalancesAsync(cancellationToken);
		foreach (var balance in balances?.Items ?? [])
			await SendBalanceAsync(balance, originalTransactionId, cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(long originalTransactionId,
		string book, string orderId, DateTime? from, DateTime? to, int maximum,
		OrderStatusMessage filter, CancellationToken cancellationToken)
	{
		var orders = orderId.IsEmpty()
			? await RestClient.GetOpenOrdersAsync(new()
			{
				Book = book,
				Limit = maximum,
			}, cancellationToken)
			: await RestClient.GetOrdersAsync([orderId], cancellationToken);
		foreach (var order in (orders ?? []).Where(order =>
			(orderId.IsEmpty() || order.OrderId.EqualsIgnoreCase(orderId)) &&
			MatchesOrder(order, filter, from, to))
			.OrderBy(GetOrderTime).TakeLast(maximum))
			await SendOrderAsync(order, originalTransactionId, null,
				cancellationToken);

		var trades = orderId.IsEmpty()
			? await RestClient.GetUserTradesAsync(new()
			{
				Book = book,
				Limit = maximum.Min(100),
			}, cancellationToken)
			: await RestClient.GetOrderTradesAsync(orderId, cancellationToken);
		foreach (var trade in (trades ?? []).Where(trade =>
			(orderId.IsEmpty() || trade.OrderId.EqualsIgnoreCase(orderId)) &&
			MatchesTrade(trade, filter, from, to))
			.OrderBy(GetTradeTime).TakeLast(maximum))
			await SendTradeAsync(trade, originalTransactionId, false,
				cancellationToken);
	}

	private async ValueTask PollOrderUpdatesAsync(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var orders = await RestClient.GetOpenOrdersAsync(new() { Limit = 500 },
			cancellationToken) ?? [];
		var currentIds = orders.Where(static order =>
			order?.OrderId.IsEmpty() == false).Select(static order => order.OrderId)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		string[] removed;
		using (_sync.EnterScope())
		{
			removed = orders.Length < 500
				? [.. _knownActiveOrderIds.Where(id => !currentIds.Contains(id))]
				: [];
			if (orders.Length < 500)
				_knownActiveOrderIds.Clear();
			_knownActiveOrderIds.AddRange(currentIds);
		}
		foreach (var order in orders.OrderBy(GetOrderTime))
			await SendOrderAsync(order, originalTransactionId, null,
				cancellationToken);
		foreach (var orderId in removed)
		{
			var tracked = GetTrackedOrder(orderId);
			if (tracked is not null)
				await SendTrackedOrderAsync(orderId, tracked, OrderStates.Done, 0m,
					originalTransactionId, cancellationToken);
		}

		var trades = await RestClient.GetUserTradesAsync(new() { Limit = 100 },
			cancellationToken);
		foreach (var trade in (trades ?? []).OrderBy(GetTradeTime))
			await SendTradeAsync(trade, originalTransactionId, true,
				cancellationToken);
	}

	private ValueTask SendBalanceAsync(BitsoBalance balance,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (balance?.Currency.IsEmpty() != false)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(),
			SecurityId = balance.Currency.ToStockSharp(),
			ServerTime = CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, balance.Total, true)
		.TryAdd(PositionChangeTypes.BlockedValue, balance.Locked, true),
			cancellationToken);
	}

	private ValueTask SendOrderAsync(BitsoOrder order, long originalTransactionId,
		OrderStates? forcedState, CancellationToken cancellationToken)
	{
		if (order?.OrderId.IsEmpty() != false || order.Book.IsEmpty())
			return default;
		var tracked = GetTrackedOrder(order.OrderId);
		if (tracked is null)
		{
			tracked = new()
			{
				TransactionId = BitsoExtensions.ParseTransactionId(order.OriginId),
				Book = order.Book.NormalizeBook(),
				OriginId = order.OriginId,
				Side = order.Side.ToStockSharp(),
				OrderType = order.Type.ToStockSharp(order.StopPrice is > 0),
				Volume = order.OriginalAmount,
				Price = order.Price,
				StopPrice = order.StopPrice,
				TimeInForce = order.TimeInForce.ToStockSharp(),
				IsPostOnly = order.TimeInForce == BitsoTimeInForces.PostOnly,
			};
			TrackOrder(order.OrderId, tracked);
		}
		var state = forcedState ?? order.Status.ToStockSharp();
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Book.NormalizeBook().ToStockSharp(),
			ServerTime = GetOrderTime(order),
			PortfolioName = GetPortfolioName(),
			Side = tracked.Side,
			OrderVolume = order.OriginalAmount > 0
				? order.OriginalAmount
				: tracked.Volume,
			Balance = state == OrderStates.Done ? 0m : order.UnfilledAmount,
			OrderPrice = order.Price != 0 ? order.Price : tracked.Price,
			OrderType = tracked.OrderType,
			OrderState = state,
			OrderStringId = order.OrderId,
			TransactionId = tracked.TransactionId,
			OriginalTransactionId = originalTransactionId,
			TimeInForce = tracked.TimeInForce,
			PostOnly = tracked.IsPostOnly,
			Condition = tracked.StopPrice is decimal stopPrice
				? new BitsoOrderCondition { StopPrice = stopPrice }
				: null,
		}, cancellationToken);
	}

	private ValueTask SendTrackedOrderAsync(string orderId, TrackedOrder tracked,
		OrderStates state, decimal balance, long originalTransactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = tracked.Book.ToStockSharp(),
			ServerTime = CurrentTime,
			PortfolioName = GetPortfolioName(),
			Side = tracked.Side,
			OrderVolume = tracked.Volume,
			Balance = balance,
			OrderPrice = tracked.Price,
			OrderType = tracked.OrderType,
			OrderState = state,
			OrderStringId = orderId,
			TransactionId = tracked.TransactionId,
			OriginalTransactionId = originalTransactionId,
			TimeInForce = tracked.TimeInForce,
			PostOnly = tracked.IsPostOnly,
			Condition = tracked.StopPrice is decimal stopPrice
				? new BitsoOrderCondition { StopPrice = stopPrice }
				: null,
		}, cancellationToken);

	private async ValueTask SendTradeAsync(BitsoUserTrade trade,
		long originalTransactionId, bool onlyNew,
		CancellationToken cancellationToken)
	{
		if (trade?.TradeId.IsEmpty() != false || trade.Book.IsEmpty())
			return;
		var added = AddTrade(trade.TradeId);
		if (onlyNew && !added)
			return;
		var tracked = GetTrackedOrder(trade.OrderId);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = trade.Book.NormalizeBook().ToStockSharp(),
			ServerTime = GetTradeTime(trade),
			PortfolioName = GetPortfolioName(),
			Side = trade.Side.ToStockSharp(),
			OrderStringId = trade.OrderId,
			TradeStringId = trade.TradeId,
			TradePrice = trade.Price,
			TradeVolume = trade.Major.Abs(),
			Commission = trade.FeesAmount == 0 ? null : trade.FeesAmount.Abs(),
			CommissionCurrency = trade.FeesCurrency,
			IsMarketMaker = trade.MakerSide == trade.Side,
			TransactionId = tracked?.TransactionId ??
				BitsoExtensions.ParseTransactionId(trade.OriginId),
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);

		if (!added || tracked is null)
			return;
		var filled = AddFilledVolume(trade.OrderId, trade.Major.Abs());
		var isDone = tracked.OrderType == OrderTypes.Market ||
			filled >= tracked.Volume;
		if (!isDone)
			return;
		var notify = false;
		using (_sync.EnterScope())
			notify = _knownActiveOrderIds.Remove(trade.OrderId);
		if (notify)
			await SendTrackedOrderAsync(trade.OrderId, tracked, OrderStates.Done, 0m,
				originalTransactionId, cancellationToken);
	}

	private DateTime GetOrderTime(BitsoOrder order)
		=> order.UpdatedAt.IsEmpty(order.CreatedAt).ToUtcDateTime(CurrentTime);

	private DateTime GetTradeTime(BitsoUserTrade trade)
		=> trade.CreatedAt.ToUtcDateTime(CurrentTime);

	private bool MatchesOrder(BitsoOrder order, OrderStatusMessage filter,
		DateTime? from, DateTime? to)
		=> order is not null && !order.OrderId.IsEmpty() && !order.Book.IsEmpty() &&
			MatchesFilter(order.Book, order.Side.ToStockSharp(),
				order.Status.ToStockSharp(), order.OriginalAmount, GetOrderTime(order),
				filter, from, to);

	private bool MatchesTrade(BitsoUserTrade trade, OrderStatusMessage filter,
		DateTime? from, DateTime? to)
		=> trade is not null && !trade.TradeId.IsEmpty() && !trade.Book.IsEmpty() &&
			MatchesFilter(trade.Book, trade.Side.ToStockSharp(), null, null,
				GetTradeTime(trade), filter, from, to);

	private bool MatchesFilter(string book, Sides side, OrderStates? state,
		decimal? volume, DateTime time, OrderStatusMessage filter, DateTime? from,
		DateTime? to)
	{
		if (from is DateTime fromTime && time < fromTime.ToUniversalTime() ||
			to is DateTime toTime && time > toTime.ToUniversalTime())
			return false;
		if (filter is null)
			return true;
		if (filter.Side is Sides requestedSide && requestedSide != side)
			return false;
		if (state is OrderStates actualState && filter.States.Length > 0 &&
			!filter.States.Contains(actualState))
			return false;
		if (filter.Volume is decimal requestedVolume &&
			volume is decimal actualVolume && requestedVolume != actualVolume)
			return false;
		if (!filter.PortfolioName.IsEmpty() &&
			!filter.PortfolioName.EqualsIgnoreCase(GetPortfolioName()))
			return false;
		var requested = new List<SecurityId>();
		if (!filter.SecurityId.SecurityCode.IsEmpty())
			requested.Add(filter.SecurityId);
		requested.AddRange(filter.SecurityIds.Where(static id =>
			!id.SecurityCode.IsEmpty()));
		return requested.Count == 0 || requested.Any(id =>
			(id.BoardCode.IsEmpty() ||
				id.BoardCode.EqualsIgnoreCase(BoardCodes.Bitso)) &&
			id.SecurityCode.NormalizeBook().EqualsIgnoreCase(book.NormalizeBook()));
	}

	private async ValueTask CompleteOrderStatusAsync(OrderStatusMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
