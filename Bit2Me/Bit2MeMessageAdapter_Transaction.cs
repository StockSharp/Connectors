namespace StockSharp.Bit2Me;

public partial class Bit2MeMessageAdapter
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
				"Bit2Me order volume must be positive.");
		if (regMsg.VisibleVolume is > 0 && regMsg.VisibleVolume != volume)
			throw new NotSupportedException(
				"Bit2Me does not document iceberg orders.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException(
				"Bit2Me does not document GTD orders.");

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market or
			OrderTypes.Conditional))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		var condition = regMsg.Condition as Bit2MeOrderCondition;
		var isConditional = orderType == OrderTypes.Conditional ||
			condition?.TriggerPrice is not null;
		if (isConditional && condition?.TriggerPrice is not > 0)
			throw new InvalidOperationException(
				"Bit2Me stop-limit orders require a positive trigger price.");
		if (orderType != OrderTypes.Market && regMsg.Price <= 0)
			throw new InvalidOperationException(
				"Bit2Me limit orders require a positive price.");
		if (orderType == OrderTypes.Market && regMsg.PostOnly == true)
			throw new InvalidOperationException(
				"A market order cannot be post-only.");
		if (isConditional && regMsg.Price <= 0)
			throw new InvalidOperationException(
				"Bit2Me stop-limit orders require a positive limit price.");

		var nativeType = isConditional
			? Bit2MeOrderTypes.StopLimit
			: orderType == OrderTypes.Market
				? Bit2MeOrderTypes.Market
				: Bit2MeOrderTypes.Limit;
		var clientOrderId = Bit2MeExtensions.CreateClientOrderId(
			regMsg.TransactionId, regMsg.UserOrderId);
		var result = await RestClient.PlaceOrderAsync(new()
		{
			Symbol = market.Symbol,
			Side = regMsg.Side.ToBit2Me(),
			Amount = volume.ToWire(),
			OrderType = nativeType,
			Price = nativeType == Bit2MeOrderTypes.Market
				? null
				: regMsg.Price.ToWire(),
			StopPrice = nativeType == Bit2MeOrderTypes.StopLimit
				? condition.TriggerPrice.Value.ToWire()
				: null,
			ClientOrderId = clientOrderId,
			IsPostOnly = nativeType == Bit2MeOrderTypes.Limit
				? regMsg.PostOnly
				: null,
			TimeInForce = nativeType == Bit2MeOrderTypes.Market
				? null
				: regMsg.TimeInForce.ToBit2Me(),
		}, cancellationToken);
		if (result?.Id.IsEmpty() != false)
			throw new InvalidDataException(
				"Bit2Me accepted an order without returning its identifier.");

		var tracked = new TrackedOrder
		{
			TransactionId = regMsg.TransactionId,
			Symbol = market.Symbol,
			ClientOrderId = clientOrderId,
			Side = regMsg.Side,
			OrderType = isConditional ? OrderTypes.Conditional : orderType,
			Volume = volume,
			Price = regMsg.Price,
			TriggerPrice = condition?.TriggerPrice,
			TimeInForce = regMsg.TimeInForce,
			IsPostOnly = regMsg.PostOnly == true,
		};
		TrackOrder(result.Id, tracked);
		await SendOrderAsync(result, regMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var orderId = ResolveOrderId(cancelMsg.OrderId,
			cancelMsg.OrderStringId, "cancellation");
		var result = await RestClient.CancelOrderAsync(orderId,
			cancellationToken);
		if (result is null)
			throw new InvalidDataException(
				"Bit2Me returned no cancellation result.");
		using (_sync.EnterScope())
			_knownActiveOrderIds.Remove(orderId);
		await SendOrderAsync(result, cancelMsg.TransactionId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"Bit2Me spot bulk cancellation cannot close positions.");
		var symbol = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(cancelMsg.SecurityId).Symbol;
		var orders = await RestClient.GetOrdersAsync(new()
		{
			Symbol = symbol,
			StatusIn = "open,inactive",
			Limit = 100,
		}, cancellationToken);
		foreach (var order in (orders ?? []).Where(order =>
			order?.Id.IsEmpty() == false &&
			(cancelMsg.Side is null ||
				order.Side.ToStockSharp() == cancelMsg.Side)))
		{
			var cancelled = await RestClient.CancelOrderAsync(order.Id,
				cancellationToken);
			using (_sync.EnterScope())
				_knownActiveOrderIds.Remove(order.Id);
			if (cancelled is not null)
				await SendOrderAsync(cancelled, cancelMsg.TransactionId,
					cancellationToken);
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
				BoardCode = BoardCodes.Bit2Me,
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

		var symbol = statusMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(statusMsg.SecurityId).Symbol;
		var orderId = statusMsg.HasOrderId()
			? ResolveOrderId(statusMsg.OrderId, statusMsg.OrderStringId,
				"lookup")
			: null;
		var maximum = (statusMsg.Count ?? 100).Min(100).Max(1).To<int>();
		await SendOrderSnapshotAsync(statusMsg.TransactionId, symbol, orderId,
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
		long originalTransactionId, string symbol, string orderId,
		DateTime? from, DateTime? to, int maximum,
		OrderStatusMessage filter, CancellationToken cancellationToken)
	{
		var orders = orderId.IsEmpty()
			? await RestClient.GetOrdersAsync(new()
			{
				Symbol = symbol,
				StartTime = from,
				EndTime = to,
				Limit = maximum,
			}, cancellationToken)
			: [await RestClient.GetOrderAsync(orderId, cancellationToken)];
		foreach (var order in (orders ?? [])
			.Where(order => MatchesOrder(order, filter, from, to))
			.OrderBy(GetOrderTime).TakeLast(maximum))
			await SendOrderAsync(order, originalTransactionId,
				cancellationToken);

		var trades = orderId.IsEmpty()
			? await RestClient.GetTradesAsync(new()
			{
				Symbol = symbol,
				StartTime = from,
				EndTime = to,
				Limit = maximum.Min(50),
			}, cancellationToken)
			: await RestClient.GetOrderTradesAsync(orderId,
				cancellationToken);
		foreach (var trade in (trades ?? [])
			.Where(trade => MatchesTrade(trade, filter, from, to))
			.OrderBy(GetTradeTime).TakeLast(maximum))
			await SendTradeAsync(trade, originalTransactionId, false,
				cancellationToken);
	}

	private async ValueTask PollOrderUpdatesAsync(
		long originalTransactionId, CancellationToken cancellationToken)
	{
		var orders = await RestClient.GetOrdersAsync(new()
		{
			StatusIn = "open,inactive",
			Limit = 100,
		}, cancellationToken) ?? [];
		var currentIds = orders.Where(static order =>
			order?.Id.IsEmpty() == false).Select(static order => order.Id)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		string[] removed;
		using (_sync.EnterScope())
		{
			removed = orders.Length < 100
				? [.. _knownActiveOrderIds.Where(id =>
					!currentIds.Contains(id))]
				: [];
			if (orders.Length < 100)
				_knownActiveOrderIds.Clear();
			_knownActiveOrderIds.AddRange(currentIds);
		}
		foreach (var order in orders.OrderBy(GetOrderTime))
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

		var trades = await RestClient.GetTradesAsync(new()
		{
			Limit = 50,
		}, cancellationToken);
		foreach (var trade in (trades ?? []).OrderBy(GetTradeTime))
			await SendTradeAsync(trade, originalTransactionId, true,
				cancellationToken);
	}

	private ValueTask SendBalanceAsync(Bit2MeWallet balance,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (balance?.Currency.IsEmpty() != false)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(),
			SecurityId = new()
			{
				SecurityCode = balance.Currency.ToUpperInvariant(),
				BoardCode = BoardCodes.Bit2Me,
			},
			ServerTime = CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue,
			balance.Balance + balance.BlockedBalance, true)
		.TryAdd(PositionChangeTypes.BlockedValue, balance.BlockedBalance, true),
			cancellationToken);
	}

	private ValueTask SendOrderAsync(Bit2MeOrder order,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (order?.Id.IsEmpty() != false || order.Symbol.IsEmpty())
			return default;
		var tracked = GetTrackedOrder(order.Id);
		if (tracked is null)
		{
			tracked = new()
			{
				TransactionId =
					Bit2MeExtensions.ParseTransactionId(order.ClientOrderId),
				Symbol = order.Symbol.NormalizeSymbol(),
				ClientOrderId = order.ClientOrderId,
				Side = order.Side.ToStockSharp(),
				OrderType = order.OrderType.ToStockSharp(),
				Volume = order.EffectiveAmount,
				Price = order.Price,
				TriggerPrice = order.StopPrice,
				TimeInForce = order.TimeInForce.ToStockSharp(),
				IsPostOnly = order.IsPostOnly == true,
			};
			TrackOrder(order.Id, tracked);
		}
		var state = order.Status.ToStockSharp();
		if (state == OrderStates.Done)
		{
			using (_sync.EnterScope())
				_knownActiveOrderIds.Remove(order.Id);
		}
		var balance = state == OrderStates.Done
			? 0m
			: (order.EffectiveAmount - order.FilledAmount -
				order.DustAmount).Max(0m);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToStockSharp(),
			ServerTime = GetOrderTime(order),
			PortfolioName = GetPortfolioName(),
			Side = order.Side.ToStockSharp(),
			OrderVolume = order.EffectiveAmount,
			Balance = balance,
			OrderPrice = order.Price,
			OrderType = order.OrderType.ToStockSharp(),
			OrderState = state,
			OrderStringId = order.Id,
			TransactionId = tracked.TransactionId,
			OriginalTransactionId = originalTransactionId,
			TimeInForce = order.TimeInForce.ToStockSharp(),
			PostOnly = order.IsPostOnly == true,
			Condition = order.StopPrice is > 0
				? new Bit2MeOrderCondition
				{
					TriggerPrice = order.StopPrice,
				}
				: null,
		}, cancellationToken);
	}

	private ValueTask SendTrackedOrderAsync(string orderId,
		TrackedOrder tracked, OrderStates state, decimal balance,
		long originalTransactionId, CancellationToken cancellationToken)
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
			OrderStringId = orderId,
			TransactionId = tracked.TransactionId,
			OriginalTransactionId = originalTransactionId,
			TimeInForce = tracked.TimeInForce,
			PostOnly = tracked.IsPostOnly,
			Condition = tracked.TriggerPrice is decimal triggerPrice
				? new Bit2MeOrderCondition
				{
					TriggerPrice = triggerPrice,
				}
				: null,
		}, cancellationToken);

	private ValueTask SendTradeAsync(Bit2MeTrade trade,
		long originalTransactionId, bool onlyNew,
		CancellationToken cancellationToken)
	{
		if (trade?.Id.IsEmpty() != false || trade.Symbol.IsEmpty())
			return default;
		var added = AddTrade(trade.Id);
		if (onlyNew && !added)
			return default;
		var tracked = GetTrackedOrder(trade.OrderId);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = trade.Symbol.ToStockSharp(),
			ServerTime = GetTradeTime(trade),
			PortfolioName = GetPortfolioName(),
			Side = trade.Side.ToStockSharp(),
			OrderStringId = trade.OrderId,
			TradeStringId = trade.Id,
			TradePrice = trade.Price,
			TradeVolume = trade.Amount,
			Commission = trade.FeeAmount,
			CommissionCurrency = trade.FeeCurrency,
			IsMarketMaker = trade.IsMaker,
			TransactionId = tracked?.TransactionId ??
				Bit2MeExtensions.ParseTransactionId(trade.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private DateTime GetOrderTime(Bit2MeOrder order)
		=> order.UpdatedAt.IsEmpty(order.CreatedAt)
			.ToUtcDateTime(CurrentTime);

	private DateTime GetTradeTime(Bit2MeTrade trade)
		=> trade.CreatedAt.ToUtcDateTime(CurrentTime);

	private bool MatchesOrder(Bit2MeOrder order, OrderStatusMessage filter,
		DateTime? from, DateTime? to)
		=> order is not null && !order.Id.IsEmpty() &&
			!order.Symbol.IsEmpty() &&
			MatchesFilter(order.Symbol, order.Side.ToStockSharp(),
				order.Status.ToStockSharp(), order.EffectiveAmount,
				GetOrderTime(order), filter, from, to);

	private bool MatchesTrade(Bit2MeTrade trade, OrderStatusMessage filter,
		DateTime? from, DateTime? to)
		=> trade is not null && !trade.Id.IsEmpty() &&
			!trade.Symbol.IsEmpty() &&
			MatchesFilter(trade.Symbol, trade.Side.ToStockSharp(), null, null,
				GetTradeTime(trade), filter, from, to);

	private bool MatchesFilter(string symbol, Sides side,
		OrderStates? state, decimal? volume, DateTime time,
		OrderStatusMessage filter, DateTime? from, DateTime? to)
	{
		if (from is DateTime fromTime &&
			time < fromTime.ToUniversalTime() ||
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
		return requested.Count == 0 || requested.Any(id =>
			(id.BoardCode.IsEmpty() ||
				id.BoardCode.EqualsIgnoreCase(BoardCodes.Bit2Me)) &&
			id.SecurityCode.NormalizeSymbol().EqualsIgnoreCase(
				symbol.NormalizeSymbol()));
	}

	private async ValueTask CompleteOrderStatusAsync(
		OrderStatusMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
