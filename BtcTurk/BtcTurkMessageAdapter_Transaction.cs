namespace StockSharp.BtcTurk;

public partial class BtcTurkMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var market = GetMarket(regMsg.SecurityId);
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException(
				"BtcTurk order volume must be positive.");
		if (regMsg.VisibleVolume is > 0 && regMsg.VisibleVolume != volume)
			throw new NotSupportedException(
				"BtcTurk does not document iceberg orders.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException(
				"BtcTurk does not document GTD orders.");
		if (regMsg.TimeInForce is not null)
			throw new NotSupportedException(
				"BtcTurk does not expose time-in-force selection.");
		if (regMsg.PostOnly == true)
			throw new NotSupportedException(
				"BtcTurk does not expose post-only order selection.");

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market or
			OrderTypes.Conditional))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		var condition = regMsg.Condition as BtcTurkOrderCondition;
		var isConditional = orderType == OrderTypes.Conditional ||
			condition?.TriggerPrice is not null;
		if (isConditional && condition?.TriggerPrice is not > 0)
			throw new InvalidOperationException(
				"BtcTurk stop orders require a positive trigger price.");
		if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
			throw new InvalidOperationException(
				"BtcTurk limit orders require a positive price.");

		var method = isConditional
			? regMsg.Price > 0
				? BtcTurkOrderMethods.StopLimit
				: BtcTurkOrderMethods.StopMarket
			: orderType == OrderTypes.Market
				? BtcTurkOrderMethods.Market
				: BtcTurkOrderMethods.Limit;
		var clientOrderId = BtcTurkExtensions.CreateClientOrderId(
			regMsg.TransactionId, regMsg.UserOrderId);
		var result = await RestClient.PlaceOrderAsync(new()
		{
			Quantity = volume.ToWire(),
			Price = method is BtcTurkOrderMethods.Market or
				BtcTurkOrderMethods.StopMarket
					? "0"
					: regMsg.Price.ToWire(),
			StopPrice = isConditional
				? condition.TriggerPrice.Value.ToWire()
				: "0",
			ClientOrderId = clientOrderId,
			Method = method,
			Side = regMsg.Side.ToBtcTurk(),
			PairSymbol = market.NativeSymbol,
		}, cancellationToken);
		if (result?.Id is not > 0)
			throw new InvalidDataException(
				"BtcTurk accepted an order without returning its identifier.");

		var tracked = new TrackedOrder
		{
			TransactionId = regMsg.TransactionId,
			SecurityCode = market.SecurityCode,
			ClientOrderId = clientOrderId,
			Side = regMsg.Side,
			OrderType = isConditional ? OrderTypes.Conditional : orderType,
			Volume = volume,
			Price = regMsg.Price,
			TriggerPrice = condition?.TriggerPrice,
		};
		TrackOrder(result.Id, tracked);
		await SendOrderAsync(result, regMsg.TransactionId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var orderId = ResolveOrderId(cancelMsg.OrderId,
			cancelMsg.OrderStringId, "cancellation");
		await RestClient.CancelOrderAsync(orderId, cancellationToken);
		using (_sync.EnterScope())
			_knownActiveOrderIds.Remove(orderId);
		try
		{
			var order = await RestClient.GetOrderAsync(orderId,
				cancellationToken);
			await SendOrderAsync(order, cancelMsg.TransactionId,
				cancellationToken);
		}
		catch (HttpRequestException)
		{
			var tracked = GetTrackedOrder(orderId);
			if (tracked is not null)
				await SendTrackedOrderAsync(orderId, tracked,
					OrderStates.Done, 0m, cancelMsg.TransactionId,
					cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"BtcTurk spot bulk cancellation cannot close positions.");
		var market = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(cancelMsg.SecurityId);
		BtcTurkOrder[] orders;
		if (market is null)
		{
			orders = await RestClient.GetOrdersAsync(new()
			{
				Count = 1000,
			}, cancellationToken) ?? [];
			orders = [.. orders.Where(static order =>
				order.Status is BtcTurkOrderStatuses.Untouched or
					BtcTurkOrderStatuses.Partial)];
		}
		else
		{
			orders = (await RestClient.GetOpenOrdersAsync(
				market.NativeSymbol, cancellationToken))?.Orders ?? [];
		}

		foreach (var order in orders.Where(order =>
			order?.Id > 0 &&
			(cancelMsg.Side is null ||
				order.Side.ToStockSharp() == cancelMsg.Side)))
		{
			await RestClient.CancelOrderAsync(order.Id, cancellationToken);
			using (_sync.EnterScope())
				_knownActiveOrderIds.Remove(order.Id);
			await SendTrackedOrNativeCancellationAsync(order,
				cancelMsg.TransactionId, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);

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
				BoardCode = BoardCodes.BtcTurk,
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
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId,
			cancellationToken);

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

		var market = statusMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(statusMsg.SecurityId);
		var orderId = statusMsg.HasOrderId()
			? ResolveOrderId(statusMsg.OrderId, statusMsg.OrderStringId,
				"lookup")
			: (long?)null;
		var maximum = (statusMsg.Count ?? 1000).Min(1000).Max(1).To<int>();
		await SendOrderSnapshotAsync(statusMsg.TransactionId, market, orderId,
			statusMsg.From, statusMsg.To, maximum, statusMsg,
			cancellationToken);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);

		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}

		_orderStatusSubscriptionId = statusMsg.TransactionId;
	}

	private async ValueTask SendPortfolioSnapshotAsync(
		long originalTransactionId, CancellationToken cancellationToken)
	{
		var balances = await RestClient.GetBalancesAsync(cancellationToken);

		foreach (var balance in balances ?? [])
			await SendBalanceAsync(balance, originalTransactionId,
				cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(
		long originalTransactionId, BtcTurkMarket market, long? orderId,
		DateTime? from, DateTime? to, int maximum,
		OrderStatusMessage filter, CancellationToken cancellationToken)
	{
		var orders = orderId is long id
			? [await RestClient.GetOrderAsync(id, cancellationToken)]
			: await RestClient.GetOrdersAsync(new()
			{
				PairSymbol = market?.NativeSymbol,
				From = from,
				To = to,
				Count = maximum,
			}, cancellationToken);

		foreach (var order in (orders ?? [])
			.Where(order => MatchesOrder(order, filter, from, to))
			.OrderBy(GetOrderTime).TakeLast(maximum))
			await SendOrderAsync(order, originalTransactionId,
				cancellationToken);

		var trades = await RestClient.GetTradesAsync(new()
		{
			OrderId = orderId,
			PairSymbol = market?.NativeSymbol,
			From = from,
			To = to,
		}, cancellationToken);

		foreach (var trade in (trades ?? [])
			.Where(trade => MatchesTrade(trade, filter, from, to))
			.OrderBy(GetTradeTime).TakeLast(maximum))
			await SendTradeAsync(trade, originalTransactionId, false,
				cancellationToken);
	}

	private async ValueTask PollOrderUpdatesAsync(
		long originalTransactionId, CancellationToken cancellationToken)
	{
		var recent = await RestClient.GetOrdersAsync(new()
		{
			Count = 1000,
		}, cancellationToken) ?? [];
		var active = recent.Where(static order =>
			order?.Id > 0 &&
			order.Status is BtcTurkOrderStatuses.Untouched or
				BtcTurkOrderStatuses.Partial).ToArray();
		var currentIds = active.Select(static order => order.Id).ToHashSet();
		long[] removed;
		using (_sync.EnterScope())
		{
			removed = recent.Length < 1000
				? [.. _knownActiveOrderIds.Where(id =>
					!currentIds.Contains(id))]
				: [];
			if (recent.Length < 1000)
				_knownActiveOrderIds.Clear();
			_knownActiveOrderIds.AddRange(currentIds);
		}

		foreach (var order in active.OrderBy(GetOrderTime))
			await SendOrderAsync(order, originalTransactionId,
				cancellationToken);

		foreach (var orderId in removed)
		{
			try
			{
				var order = await RestClient.GetOrderAsync(orderId,
					cancellationToken);
				await SendOrderAsync(order, originalTransactionId,
					cancellationToken);
			}
			catch (HttpRequestException)
			{
				var tracked = GetTrackedOrder(orderId);
				if (tracked is not null)
					await SendTrackedOrderAsync(orderId, tracked,
						OrderStates.Done, 0m, originalTransactionId,
						cancellationToken);
			}
		}

		var trades = await RestClient.GetTradesAsync(new(),
			cancellationToken);

		foreach (var trade in (trades ?? []).OrderBy(GetTradeTime))
			await SendTradeAsync(trade, originalTransactionId, true,
				cancellationToken);
	}

	private ValueTask SendBalanceAsync(BtcTurkBalance balance,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (balance?.Asset.IsEmpty() != false)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(),
			SecurityId = new()
			{
				SecurityCode = balance.Asset.ToUpperInvariant(),
				BoardCode = BoardCodes.BtcTurk,
			},
			ServerTime = balance.Timestamp > 0
				? balance.Timestamp.FromUnixMilliseconds()
				: CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, balance.Balance, true)
		.TryAdd(PositionChangeTypes.BlockedValue, balance.Locked, true),
			cancellationToken);
	}

	private ValueTask SendOrderAsync(BtcTurkOrder order,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (order?.Id is not > 0 || order.PairSymbol.IsEmpty())
			return default;
		var market = GetMarket(new SecurityId
		{
			SecurityCode = order.PairSymbol,
			BoardCode = BoardCodes.BtcTurk,
		});
		var tracked = GetTrackedOrder(order.Id);
		if (tracked is null)
		{
			tracked = new()
			{
				TransactionId =
					BtcTurkExtensions.ParseTransactionId(
						order.ClientOrderId),
				SecurityCode = market.SecurityCode,
				ClientOrderId = order.ClientOrderId,
				Side = order.Side.ToStockSharp(),
				OrderType = order.Method.ToStockSharp(),
				Volume = order.Amount,
				Price = order.Price,
				TriggerPrice = order.StopPrice > 0
					? order.StopPrice
					: null,
			};
			TrackOrder(order.Id, tracked);
		}
		var state = order.Status.ToStockSharp();
		if (state is OrderStates.Done or OrderStates.Failed)
		{
			using (_sync.EnterScope())
				_knownActiveOrderIds.Remove(order.Id);
		}
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.SecurityCode.ToStockSharp(),
			ServerTime = GetOrderTime(order),
			PortfolioName = GetPortfolioName(),
			Side = order.Side.ToStockSharp(),
			OrderVolume = order.Amount,
			Balance = order.LeftAmount,
			OrderPrice = order.Price,
			OrderType = order.Method.ToStockSharp(),
			OrderState = state,
			OrderId = order.Id,
			OrderStringId = order.Id.ToString(CultureInfo.InvariantCulture),
			TransactionId = tracked.TransactionId,
			OriginalTransactionId = originalTransactionId,
			Condition = order.StopPrice > 0
				? new BtcTurkOrderCondition
				{
					TriggerPrice = order.StopPrice,
				}
				: null,
		}, cancellationToken);
	}

	private ValueTask SendTrackedOrderAsync(long orderId,
		TrackedOrder tracked, OrderStates state, decimal balance,
		long originalTransactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = tracked.SecurityCode.ToStockSharp(),
			ServerTime = CurrentTime,
			PortfolioName = GetPortfolioName(),
			Side = tracked.Side,
			OrderVolume = tracked.Volume,
			Balance = balance,
			OrderPrice = tracked.Price,
			OrderType = tracked.OrderType,
			OrderState = state,
			OrderId = orderId,
			OrderStringId = orderId.ToString(CultureInfo.InvariantCulture),
			TransactionId = tracked.TransactionId,
			OriginalTransactionId = originalTransactionId,
			Condition = tracked.TriggerPrice is decimal triggerPrice
				? new BtcTurkOrderCondition
				{
					TriggerPrice = triggerPrice,
				}
				: null,
		}, cancellationToken);

	private async ValueTask SendTrackedOrNativeCancellationAsync(
		BtcTurkOrder order, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var tracked = GetTrackedOrder(order.Id);
		if (tracked is not null)
		{
			await SendTrackedOrderAsync(order.Id, tracked,
				OrderStates.Done, 0m, originalTransactionId,
				cancellationToken);
			return;
		}
		order.Status = BtcTurkOrderStatuses.Canceled;
		order.LeftAmountText = "0";
		await SendOrderAsync(order, originalTransactionId,
			cancellationToken);
	}

	private ValueTask SendTradeAsync(BtcTurkUserTrade trade,
		long originalTransactionId, bool onlyNew,
		CancellationToken cancellationToken)
	{
		if (trade?.Id is not > 0 || trade.OrderId <= 0 ||
			trade.Numerator.IsEmpty() || trade.Denominator.IsEmpty())
			return default;
		var added = AddTrade(trade.Id);
		if (onlyNew && !added)
			return default;
		var tracked = GetTrackedOrder(trade.OrderId);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = trade.SecurityCode.ToStockSharp(),
			ServerTime = GetTradeTime(trade),
			PortfolioName = GetPortfolioName(),
			Side = trade.Side.ToStockSharp(),
			OrderId = trade.OrderId,
			OrderStringId = trade.OrderId.ToString(
				CultureInfo.InvariantCulture),
			TradeId = trade.Id,
			TradeStringId = trade.Id.ToString(CultureInfo.InvariantCulture),
			TradePrice = trade.Price,
			TradeVolume = trade.Amount,
			Commission = trade.Fee,
			CommissionCurrency = trade.Denominator,
			TransactionId = tracked?.TransactionId ??
				BtcTurkExtensions.ParseTransactionId(trade.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private DateTime GetOrderTime(BtcTurkOrder order)
		=> order.Timestamp > 0
			? order.Timestamp.FromUnixMilliseconds()
			: CurrentTime;

	private DateTime GetTradeTime(BtcTurkUserTrade trade)
		=> trade.Timestamp > 0
			? trade.Timestamp.FromUnixMilliseconds()
			: CurrentTime;

	private bool MatchesOrder(BtcTurkOrder order, OrderStatusMessage filter,
		DateTime? from, DateTime? to)
		=> order is not null && order.Id > 0 &&
			!order.PairSymbol.IsEmpty() &&
			MatchesFilter(order.PairSymbol, order.Side.ToStockSharp(),
				order.Status.ToStockSharp(), order.Amount,
				GetOrderTime(order), filter, from, to);

	private bool MatchesTrade(BtcTurkUserTrade trade,
		OrderStatusMessage filter, DateTime? from, DateTime? to)
		=> trade is not null && trade.Id > 0 &&
			!trade.SecurityCode.IsEmpty() &&
			MatchesFilter(trade.SecurityCode, trade.Side.ToStockSharp(),
				null, null, GetTradeTime(trade), filter, from, to);

	private bool MatchesFilter(string symbol, Sides side,
		OrderStates? state, decimal? volume, DateTime time,
		OrderStatusMessage filter, DateTime? from, DateTime? to)
	{
		if (from is DateTime fromTime && time < fromTime.ToUtc() ||
			to is DateTime toTime && time > toTime.ToUtc())
			return false;
		if (filter is null)
			return true;
		if (filter.Side is Sides requestedSide && requestedSide != side)
			return false;
		if (state is OrderStates actualState && filter.States.Length > 0 &&
			!filter.States.Contains(actualState))
			return false;
		if (filter.Volume is decimal requestedVolume &&
			volume is decimal actualVolume &&
			requestedVolume != actualVolume)
			return false;
		if (!filter.PortfolioName.IsEmpty() &&
			!filter.PortfolioName.EqualsIgnoreCase(GetPortfolioName()))
			return false;
		var requested = new List<SecurityId>();
		if (!filter.SecurityId.SecurityCode.IsEmpty())
			requested.Add(filter.SecurityId);
		requested.AddRange(filter.SecurityIds.Where(static id =>
			!id.SecurityCode.IsEmpty()));
		if (requested.Count == 0)
			return true;
		var market = GetMarket(new SecurityId
		{
			SecurityCode = symbol,
			BoardCode = BoardCodes.BtcTurk,
		});
		return requested.Any(id =>
			(id.BoardCode.IsEmpty() ||
				id.BoardCode.EqualsIgnoreCase(BoardCodes.BtcTurk)) &&
			GetMarket(id).SecurityCode.EqualsIgnoreCase(
				market.SecurityCode));
	}

	private async ValueTask CompleteOrderStatusAsync(
		OrderStatusMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
