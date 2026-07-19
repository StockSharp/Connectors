namespace StockSharp.Bitkub;

public partial class BitkubMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var market = GetMarket(regMsg.SecurityId);
		if (!market.IsTrading)
			throw new InvalidOperationException(
				$"Bitkub market '{market.Symbol}' is not active.");
		if (regMsg.Side == Sides.Buy && market.IsBuyFrozen ||
			regMsg.Side == Sides.Sell && market.IsSellFrozen)
			throw new InvalidOperationException(
				$"Bitkub has frozen {regMsg.Side.ToString().ToLowerInvariant()} orders " +
				$"for '{market.Symbol}'.");

		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException(
				"Bitkub order volume must be positive.");
		if (regMsg.VisibleVolume is > 0 && regMsg.VisibleVolume != volume)
			throw new NotSupportedException(
				"Bitkub does not document iceberg orders.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException(
				"Bitkub does not document GTD orders.");
		if (regMsg.TimeInForce is not null and not TimeInForce.PutInQueue)
			throw new NotSupportedException(
				"Bitkub public API supports GTC orders only.");

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
			throw new InvalidOperationException(
				"Bitkub limit orders require a positive price.");
		if (orderType == OrderTypes.Market && regMsg.PostOnly == true)
			throw new InvalidOperationException(
				"A market order cannot be post-only.");

		var condition = regMsg.Condition as BitkubOrderCondition;
		decimal amount;
		if (regMsg.Side == Sides.Sell)
			amount = volume;
		else if (orderType == OrderTypes.Limit)
			amount = volume * regMsg.Price;
		else
			amount = condition?.QuoteAmount ?? throw new InvalidOperationException(
				"Bitkub market buy orders require BitkubOrderCondition.QuoteAmount.");
		if (amount <= 0)
			throw new InvalidOperationException(
				"Bitkub order amount must be positive.");
		if (market.MinimumQuoteSize > 0 && regMsg.Side == Sides.Buy &&
			amount < market.MinimumQuoteSize)
			throw new InvalidOperationException(
				$"Bitkub requires at least {market.MinimumQuoteSize} " +
				$"{market.QuoteAsset} for '{market.Symbol}'.");

		var clientId = BitkubExtensions.CreateClientId(regMsg.TransactionId,
			regMsg.UserOrderId);
		var result = await RestClient.PlaceOrderAsync(regMsg.Side.ToBitkub(), new()
		{
			Symbol = market.Symbol.ToWireSymbol(),
			Amount = amount,
			Rate = orderType == OrderTypes.Limit ? regMsg.Price : 0m,
			Type = orderType == OrderTypes.Market
				? BitkubOrderTypes.Market
				: BitkubOrderTypes.Limit,
			ClientId = clientId,
			IsPostOnly = regMsg.PostOnly == true ? true : null,
		}, cancellationToken);
		if (result?.OrderId.IsEmpty() != false)
			throw new InvalidDataException(
				"Bitkub accepted an order without returning its identifier.");

		var tracked = new TrackedOrder
		{
			TransactionId = regMsg.TransactionId,
			Symbol = market.Symbol,
			ClientId = clientId,
			Side = regMsg.Side,
			OrderType = orderType,
			Volume = volume,
			Price = regMsg.Price,
			IsPostOnly = regMsg.PostOnly == true,
		};
		TrackOrder(result.OrderId, tracked);
		await SendTrackedOrderAsync(result.OrderId, tracked, OrderStates.Active,
			volume, regMsg.TransactionId, result.Timestamp.FromBitkubTimestamp(),
			cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
		=> throw new NotSupportedException(
			"Bitkub does not expose atomic order replacement.");

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var orderId = ResolveOrderId(cancelMsg.OrderId, cancelMsg.OrderStringId,
			"cancellation");
		var tracked = GetTrackedOrder(orderId);
		var market = tracked is not null
			? GetMarket(tracked.Symbol)
			: GetMarket(cancelMsg.SecurityId);
		var side = tracked?.Side ?? cancelMsg.Side ?? throw new InvalidOperationException(
			"Bitkub cancellation requires the order side.");
		if (market.IsCancelFrozen)
			throw new InvalidOperationException(
				$"Bitkub has frozen cancellations for '{market.Symbol}'.");

		await RestClient.CancelOrderAsync(new()
		{
			Symbol = market.Symbol.ToLegacyCancelSymbol(),
			OrderId = orderId,
			Side = side.ToBitkub(),
		}, cancellationToken);
		using (_sync.EnterScope())
			_activeOrderIds.Remove(orderId);

		if (tracked is not null)
			await SendTrackedOrderAsync(orderId, tracked, OrderStates.Done, 0m,
				cancelMsg.TransactionId, CurrentTime, cancellationToken);
		else
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = market.Symbol.ToStockSharp(),
				ServerTime = CurrentTime,
				PortfolioName = GetPortfolioName(),
				Side = side,
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
				"Bitkub spot bulk cancellation cannot close positions.");

		var orders = new List<(string id, string symbol, Sides side)>();
		if (!cancelMsg.SecurityId.SecurityCode.IsEmpty())
		{
			var market = GetMarket(cancelMsg.SecurityId);
			foreach (var order in await RestClient.GetOpenOrdersAsync(market.Symbol,
				cancellationToken))
				if (order?.OrderId.IsEmpty() == false &&
					(cancelMsg.Side is null ||
					order.Side.ToStockSharp() == cancelMsg.Side))
					orders.Add((order.OrderId, market.Symbol,
						order.Side.ToStockSharp()));
		}
		else
		{
			using (_sync.EnterScope())
				foreach (var pair in _trackedOrders)
					if (_activeOrderIds.Contains(pair.Key) &&
						(cancelMsg.Side is null || pair.Value.Side == cancelMsg.Side))
						orders.Add((pair.Key, pair.Value.Symbol, pair.Value.Side));
		}

		foreach (var order in orders)
		{
			await RestClient.CancelOrderAsync(new()
			{
				Symbol = order.symbol.ToLegacyCancelSymbol(),
				OrderId = order.id,
				Side = order.side.ToBitkub(),
			}, cancellationToken);
			using (_sync.EnterScope())
				_activeOrderIds.Remove(order.id);
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
				BoardCode = BoardCodes.Bitkub,
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
		var orderId = statusMsg.HasOrderId()
			? ResolveOrderId(statusMsg.OrderId, statusMsg.OrderStringId, "lookup")
			: null;
		var tracked = GetTrackedOrder(orderId);
		if (symbol.IsEmpty() && tracked is not null)
			symbol = tracked.Symbol;
		var side = statusMsg.Side ?? tracked?.Side;
		var maximum = (statusMsg.Count ?? 100).Min(1000).Max(1).To<int>();

		if (!orderId.IsEmpty())
		{
			if (symbol.IsEmpty() || side is null)
				throw new InvalidOperationException(
					"Bitkub order lookup requires the symbol and side.");
			var info = await RestClient.GetOrderInfoAsync(symbol, orderId,
				side.Value.ToBitkub(), cancellationToken);
			await SendOrderInfoAsync(symbol, side.Value, info,
				statusMsg.TransactionId, cancellationToken);
		}
		else if (!symbol.IsEmpty())
		{
			await SendSymbolOrderSnapshotAsync(symbol, statusMsg, maximum,
				cancellationToken);
		}
		else
		{
			KeyValuePair<string, TrackedOrder>[] active;
			using (_sync.EnterScope())
				active = [.. _trackedOrders.Where(pair =>
					_activeOrderIds.Contains(pair.Key))];
			foreach (var pair in active.TakeLast(maximum))
				await SendTrackedOrderAsync(pair.Key, pair.Value, OrderStates.Active,
					pair.Value.Volume, statusMsg.TransactionId, CurrentTime,
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
				Symbol = symbol,
				OrderId = orderId,
				Side = statusMsg.Side,
			};
	}

	private async ValueTask SendPortfolioSnapshotAsync(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		foreach (var balance in await RestClient.GetBalancesAsync(cancellationToken))
			await SendBalanceAsync(balance, originalTransactionId, cancellationToken);
	}

	private async ValueTask SendSymbolOrderSnapshotAsync(string symbol,
		OrderStatusMessage filter, int maximum,
		CancellationToken cancellationToken)
	{
		foreach (var order in (await RestClient.GetOpenOrdersAsync(symbol,
			cancellationToken)).Where(order => order is not null &&
			(filter.Side is null || order.Side.ToStockSharp() == filter.Side))
			.OrderBy(static order => order.Timestamp).TakeLast(maximum))
			await SendOpenOrderAsync(symbol, order, filter.TransactionId,
				cancellationToken);

		var left = maximum;
		string cursor = null;
		do
		{
			var response = await RestClient.GetOrderHistoryAsync(symbol,
				left.Min(100), cursor, filter.From, filter.To, cancellationToken);
			foreach (var trade in (response.Result ?? []).Where(trade =>
				trade is not null &&
				(filter.Side is null || trade.Side.ToStockSharp() == filter.Side))
				.OrderBy(static trade => trade.Timestamp))
			{
				await SendHistoryTradeAsync(symbol, trade, filter.TransactionId,
					cancellationToken);
				if (--left <= 0)
					break;
			}
			cursor = response.Pagination?.HasNext == true
				? response.Pagination.Cursor
				: null;
		}
		while (left > 0 && !cursor.IsEmpty());
	}

	private ValueTask SendBalanceAsync(BitkubBalance balance,
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
		.TryAdd(PositionChangeTypes.BlockedValue, balance.Reserved, true),
			cancellationToken);
	}

	private ValueTask SendOpenOrderAsync(string symbol, BitkubOpenOrder order,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		var side = order.Side.ToStockSharp();
		var volume = ToBaseVolume(side, order.Amount, order.Rate, order.Receive);
		var tracked = GetTrackedOrder(order.OrderId) ?? new TrackedOrder
		{
			TransactionId = BitkubExtensions.ParseTransactionId(order.ClientId),
			Symbol = symbol,
			ClientId = order.ClientId,
			Side = side,
			OrderType = order.Type.ToStockSharp(),
			Volume = volume,
			Price = order.Rate,
		};
		TrackOrder(order.OrderId, tracked);
		return SendTrackedOrderAsync(order.OrderId, tracked, OrderStates.Active,
			volume, originalTransactionId, order.Timestamp.FromBitkubTimestamp(),
			cancellationToken);
	}

	private async ValueTask SendOrderInfoAsync(string symbol, Sides side,
		BitkubOrderInfo info, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (info?.OrderId.IsEmpty() != false)
			return;
		var tracked = GetTrackedOrder(info.OrderId);
		var volume = tracked?.Volume ??
			ToBaseVolume(side, info.Amount, info.Rate, 0m);
		var balance = ToBaseVolume(side, info.Remaining, info.Rate, 0m);
		var timestamp = (info.History ?? []).LastOrDefault()?.Timestamp ?? 0;
		tracked ??= new()
		{
			TransactionId = BitkubExtensions.ParseTransactionId(info.ClientId),
			Symbol = symbol,
			ClientId = info.ClientId,
			Side = side,
			OrderType = OrderTypes.Limit,
			Volume = volume,
			Price = info.Rate,
			IsPostOnly = info.IsPostOnly,
		};
		TrackOrder(info.OrderId, tracked);
		var state = info.Status.ToStockSharp();
		if (state is OrderStates.Done or OrderStates.Failed)
			using (_sync.EnterScope())
				_activeOrderIds.Remove(info.OrderId);
		await SendTrackedOrderAsync(info.OrderId, tracked, state, balance,
			originalTransactionId, timestamp.FromBitkubTimestamp(), cancellationToken);

		foreach (var trade in info.History ?? [])
		{
			if (trade?.TransactionId.IsEmpty() != false ||
				!AddPrivateTrade(trade.TransactionId))
				continue;
			var tradeVolume = ToBaseVolume(side, trade.Amount, trade.Rate, 0m);
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				SecurityId = symbol.ToStockSharp(),
				ServerTime = trade.Timestamp.FromBitkubTimestamp(),
				PortfolioName = GetPortfolioName(),
				Side = side,
				OrderStringId = info.OrderId,
				TradeStringId = trade.TransactionId,
				TradePrice = trade.Rate,
				TradeVolume = tradeVolume,
				Commission = trade.Fee == 0 ? null : trade.Fee.Abs(),
				TransactionId = tracked.TransactionId,
				OriginalTransactionId = originalTransactionId,
			}, cancellationToken);
		}
	}

	private ValueTask SendHistoryTradeAsync(string symbol,
		BitkubOrderHistoryItem trade, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (trade?.TransactionId.IsEmpty() != false ||
			!AddPrivateTrade(trade.TransactionId))
			return default;
		var side = trade.Side.ToStockSharp();
		var tracked = GetTrackedOrder(trade.OrderId);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = symbol.ToStockSharp(),
			ServerTime = trade.Timestamp.FromBitkubTimestamp(),
			PortfolioName = GetPortfolioName(),
			Side = side,
			OrderStringId = trade.OrderId,
			TradeStringId = trade.TransactionId,
			TradePrice = trade.Rate,
			TradeVolume = ToBaseVolume(side, trade.Amount, trade.Rate, 0m),
			Commission = trade.Fee == 0 ? null : trade.Fee.Abs(),
			IsMarketMaker = trade.IsMaker,
			TransactionId = tracked?.TransactionId ??
				BitkubExtensions.ParseTransactionId(trade.ClientId),
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private ValueTask SendTrackedOrderAsync(string orderId, TrackedOrder tracked,
		OrderStates state, decimal balance, long originalTransactionId,
		DateTime serverTime, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = tracked.Symbol.ToStockSharp(),
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
			TimeInForce = TimeInForce.PutInQueue,
			PostOnly = tracked.IsPostOnly,
		}, cancellationToken);

	private async ValueTask OnPrivateOrderUpdatedAsync(BitkubOrderUpdate update,
		CancellationToken cancellationToken)
	{
		if (update?.OrderId.IsEmpty() != false || update.Symbol.IsEmpty())
			return;
		var symbol = update.Symbol.NormalizeSymbol();
		var market = GetMarket(symbol);
		var side = update.Side.ToStockSharp();
		var price = update.Price ?? update.AverageFilledPrice ?? 0m;
		var tracked = GetTrackedOrder(update.OrderId);
		var volume = tracked?.Volume ?? ToBaseAmount(market, side,
			update.OrderCurrency, update.OrderAmount, price,
			update.ReceivedCurrency, update.ReceivedAmount);
		var executed = ToBaseAmount(market, side, update.ExecutedCurrency,
			update.ExecutedAmount, update.AverageFilledPrice ?? price,
			update.ReceivedCurrency, update.ReceivedAmount);
		tracked ??= new()
		{
			TransactionId = BitkubExtensions.ParseTransactionId(update.ClientId),
			Symbol = symbol,
			ClientId = update.ClientId,
			Side = side,
			OrderType = update.Type.ToStockSharp(),
			Volume = volume,
			Price = price,
			IsPostOnly = update.IsPostOnly,
		};
		TrackOrder(update.OrderId, tracked);
		var state = update.Status.ToStockSharp();
		if (state is OrderStates.Done or OrderStates.Failed)
			using (_sync.EnterScope())
				_activeOrderIds.Remove(update.OrderId);

		KeyValuePair<long, OrderSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _orderSubscriptions.Where(pair =>
				MatchesOrderSubscription(pair.Value, symbol, update.OrderId, side))];
		foreach (var pair in subscriptions)
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = symbol.ToStockSharp(),
				ServerTime = (update.UpdatedAt ?? update.CreatedAt)
					.FromBitkubTimestamp(),
				PortfolioName = GetPortfolioName(),
				Side = side,
				OrderVolume = volume,
				Balance = (volume - executed).Max(0m),
				OrderPrice = price,
				OrderType = update.Type.ToStockSharp(),
				OrderState = state,
				OrderStringId = update.OrderId,
				TransactionId = tracked.TransactionId,
				OriginalTransactionId = pair.Key,
				PostOnly = update.IsPostOnly,
				Condition = update.StopPrice is decimal stopPrice
					? new BitkubOrderCondition { StopPrice = stopPrice }
					: null,
				Error = state == OrderStates.Failed
					? new InvalidOperationException("Bitkub rejected the order.")
					: null,
			}, cancellationToken);
	}

	private async ValueTask OnPrivateMatchUpdatedAsync(BitkubMatchUpdate update,
		CancellationToken cancellationToken)
	{
		if (update?.TransactionId.IsEmpty() != false || update.Symbol.IsEmpty() ||
			!AddPrivateTrade(update.TransactionId))
			return;
		var symbol = update.Symbol.NormalizeSymbol();
		var market = GetMarket(symbol);
		var side = update.Side.ToStockSharp();
		var tracked = GetTrackedOrder(update.OrderId);
		var volume = ToBaseAmount(market, side, update.ExecutedCurrency,
			update.ExecutedAmount, update.Price, update.ReceivedCurrency,
			update.ReceivedAmount);

		KeyValuePair<long, OrderSubscription>[] subscriptions;
		long[] portfolioSubscriptions;
		using (_sync.EnterScope())
		{
			subscriptions = [.. _orderSubscriptions.Where(pair =>
				MatchesOrderSubscription(pair.Value, symbol, update.OrderId, side))];
			portfolioSubscriptions = [.. _portfolioSubscriptions];
		}
		foreach (var pair in subscriptions)
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				SecurityId = symbol.ToStockSharp(),
				ServerTime = update.Timestamp.FromBitkubTimestamp(),
				PortfolioName = GetPortfolioName(),
				Side = side,
				OrderStringId = update.OrderId,
				TradeStringId = update.TransactionId,
				TradePrice = update.Price,
				TradeVolume = volume,
				Commission = update.NetFeePaid == 0
					? null
					: update.NetFeePaid.Abs(),
				IsMarketMaker = update.IsMaker,
				TransactionId = tracked?.TransactionId ??
					BitkubExtensions.ParseTransactionId(update.ClientId),
				OriginalTransactionId = pair.Key,
			}, cancellationToken);

		if (portfolioSubscriptions.Length > 0)
			await RefreshBalancesAsync(portfolioSubscriptions, cancellationToken);
	}

	private async ValueTask RefreshBalancesAsync(long[] subscriptions,
		CancellationToken cancellationToken)
	{
		if (DateTime.UtcNow - _lastBalanceRefresh < TimeSpan.FromSeconds(1) ||
			!await _balanceRefreshSync.WaitAsync(0, cancellationToken))
			return;
		try
		{
			_lastBalanceRefresh = DateTime.UtcNow;
			var balances = await RestClient.GetBalancesAsync(cancellationToken);
			foreach (var subscription in subscriptions)
				foreach (var balance in balances)
					await SendBalanceAsync(balance, subscription, cancellationToken);
		}
		finally
		{
			_balanceRefreshSync.Release();
		}
	}

	private static decimal ToBaseVolume(Sides side, decimal amount,
		decimal price, decimal received)
		=> side == Sides.Sell
			? amount.Abs()
			: price > 0
				? (amount / price).Abs()
				: received.Abs();

	private static decimal ToBaseAmount(MarketDefinition market, Sides side,
		string amountCurrency, decimal amount, decimal price,
		string receivedCurrency, decimal receivedAmount)
	{
		if (amountCurrency.EqualsIgnoreCase(market.BaseAsset))
			return amount.Abs();
		if (amountCurrency.EqualsIgnoreCase(market.QuoteAsset) && price > 0)
			return (amount / price).Abs();
		if (receivedCurrency.EqualsIgnoreCase(market.BaseAsset))
			return receivedAmount.Abs();
		return side == Sides.Sell ? amount.Abs() : 0m;
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
