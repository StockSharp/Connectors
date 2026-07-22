namespace StockSharp.ProBit;

public partial class ProBitMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		ValidatePortfolio(regMsg.PortfolioName);

		var symbol = GetSymbol(regMsg.SecurityId);
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		if (regMsg.Volume <= 0)
			throw new InvalidOperationException("ProBit orders require a positive volume.");
		if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
			throw new InvalidOperationException("ProBit limit orders require a positive price.");
		if (regMsg.PostOnly == true)
			throw new NotSupportedException("ProBit does not document post-only orders.");
		if (regMsg.VisibleVolume is > 0 && regMsg.VisibleVolume != regMsg.Volume)
			throw new NotSupportedException("ProBit does not document iceberg orders.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException("ProBit does not document GTD orders.");

		string timeInForce;
		if (orderType == OrderTypes.Market)
		{
			if (regMsg.TimeInForce is not null and not TimeInForce.CancelBalance)
				throw new NotSupportedException("ProBit market orders use IOC execution.");
			timeInForce = "ioc";
		}
		else
		{
			timeInForce = regMsg.TimeInForce switch
			{
				null or TimeInForce.PutInQueue => "gtc",
				TimeInForce.CancelBalance => "ioc",
				_ => throw new NotSupportedException(
					"ProBit documents GTC and IOC execution only."),
			};
		}

		var condition = regMsg.Condition as ProBitOrderCondition;
		if (orderType == OrderTypes.Market && regMsg.Side == Sides.Buy &&
			condition?.QuoteAmount is not > 0)
			throw new InvalidOperationException(
				"A ProBit market buy requires QuoteAmount in ProBitOrderCondition.");

		var clientOrderId = CreateClientOrderId(regMsg.TransactionId, regMsg.UserOrderId);
		var order = await RestClient.PlaceOrderAsync(new()
		{
			MarketId = symbol,
			Type = orderType == OrderTypes.Limit ? "limit" : "market",
			Side = regMsg.Side == Sides.Buy ? "buy" : "sell",
			TimeInForce = timeInForce,
			ClientOrderId = clientOrderId,
			Quantity = orderType == OrderTypes.Market && regMsg.Side == Sides.Buy
				? null
				: regMsg.Volume.ToWire(),
			LimitPrice = orderType == OrderTypes.Limit ? regMsg.Price.ToWire() : null,
			Cost = orderType == OrderTypes.Market && regMsg.Side == Sides.Buy
				? condition.QuoteAmount.Value.ToWire()
				: null,
		}, cancellationToken);

		if (order?.Id.IsEmpty() != false)
			throw new InvalidDataException(
				"ProBit accepted the order without returning an order ID.");
		if (order.MarketId.IsEmpty())
			order.MarketId = symbol;
		if (order.ClientOrderId.IsEmpty())
			order.ClientOrderId = clientOrderId;
		if ((orderType == OrderTypes.Market && regMsg.Side == Sides.Buy) ||
			order.Quantity.IsEmpty())
			order.Quantity = regMsg.Volume.ToWire();
		if (order.LimitPrice.IsEmpty() && orderType == OrderTypes.Limit)
			order.LimitPrice = regMsg.Price.ToWire();
		if (order.Type.IsEmpty())
			order.Type = orderType == OrderTypes.Limit ? "limit" : "market";
		if (order.Side.IsEmpty())
			order.Side = regMsg.Side == Sides.Buy ? "buy" : "sell";
		if (order.TimeInForce.IsEmpty())
			order.TimeInForce = timeInForce;
		if (order.Status.IsEmpty())
			order.Status = "open";

		await SendOrderAsync(order, regMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
		=> throw new NotSupportedException(
			"ProBit does not expose in-place order modification.");

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		var symbol = GetSymbol(cancelMsg.SecurityId);
		var orderId = cancelMsg.OrderStringId;
		if (orderId.IsEmpty() && cancelMsg.OrderId is long numericOrderId)
			orderId = numericOrderId.ToString(CultureInfo.InvariantCulture);
		if (orderId.IsEmpty())
			throw new InvalidOperationException(
				"ProBit cancellation requires an exchange order ID.");

		var order = await RestClient.CancelOrderAsync(new()
		{
			MarketId = symbol,
			OrderId = orderId,
		}, cancellationToken);

		if (order is not null)
		{
			if (order.Id.IsEmpty())
				order.Id = orderId;
			if (order.MarketId.IsEmpty())
				order.MarketId = symbol;
			if (order.Status.IsEmpty())
				order.Status = "cancelled";
			await SendOrderAsync(order, cancelMsg.TransactionId, cancellationToken);
		}
		else
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = symbol.ToStockSharp(),
				ServerTime = CurrentTime,
				PortfolioName = _portfolioName,
				OrderStringId = orderId,
				OrderState = OrderStates.Done,
				Balance = 0m,
				OriginalTransactionId = cancelMsg.TransactionId,
			}, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException("ProBit spot trading has no positions to close.");
		if (cancelMsg.IsStop == true)
			return;
		if (cancelMsg.SecurityTypes is { Length: > 0 } &&
			!cancelMsg.SecurityTypes.Contains(SecurityTypes.CryptoCurrency))
			return;

		var symbol = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetSymbol(cancelMsg.SecurityId);
		foreach (var order in await RestClient.GetOpenOrdersAsync(symbol, cancellationToken))
		{
			if (order?.Id.IsEmpty() != false || order.MarketId.IsEmpty() ||
				cancelMsg.Side is not null && order.Side.ToStockSharpSide() != cancelMsg.Side)
				continue;
			await RestClient.CancelOrderAsync(new()
			{
				MarketId = order.MarketId,
				OrderId = order.Id,
			}, cancellationToken);
			order.Status = "cancelled";
			await SendOrderAsync(order, cancelMsg.TransactionId, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
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
			PortfolioName = _portfolioName,
			BoardCode = BoardCodes.ProBit,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
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

		ValidatePortfolio(statusMsg.PortfolioName);
		var symbol = statusMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetSymbol(statusMsg.SecurityId);
		var orderIdentifier = statusMsg.OrderStringId;
		if (orderIdentifier.IsEmpty() && statusMsg.OrderId is long numericOrderId)
			orderIdentifier = numericOrderId.ToString(CultureInfo.InvariantCulture);
		var subscription = new OrderSubscription
		{
			Symbol = symbol,
			OrderIdentifier = orderIdentifier,
			ClientOrderId = statusMsg.UserOrderId,
			Side = statusMsg.Side,
		};
		if ((!orderIdentifier.IsEmpty() || !statusMsg.UserOrderId.IsEmpty()) && symbol.IsEmpty())
			throw new InvalidOperationException(
				"ProBit order lookup by ID requires a security ID.");

		var limit = (statusMsg.Count ?? 1000).Min(1000).Max(1).To<int>();
		await SendOrderSnapshotAsync(statusMsg.TransactionId, subscription, statusMsg.From,
			statusMsg.To, limit, cancellationToken);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_orderSubscriptions[statusMsg.TransactionId] = subscription;
	}

	private async ValueTask SendPortfolioSnapshotAsync(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		foreach (var balance in await RestClient.GetBalancesAsync(cancellationToken))
			await SendBalanceAsync(balance, originalTransactionId, CurrentTime, cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(long originalTransactionId,
		OrderSubscription subscription, DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
	{
		if (!subscription.OrderIdentifier.IsEmpty() || !subscription.ClientOrderId.IsEmpty())
		{
			foreach (var order in await RestClient.GetOrdersAsync(subscription.Symbol,
				subscription.OrderIdentifier, subscription.ClientOrderId, cancellationToken))
				if (Matches(subscription, order))
					await SendOrderAsync(order, originalTransactionId, cancellationToken);
			return;
		}

		var orders = (await RestClient.GetOpenOrdersAsync(subscription.Symbol, cancellationToken))
			.Concat(await RestClient.GetOrderHistoryAsync(subscription.Symbol, from, to, limit,
				cancellationToken))
			.Where(order => Matches(subscription, order))
			.GroupBy(static order => order.Id, StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.OrderByDescending(order => order.Time.ToUtcTime()).First())
			.OrderByDescending(static order => order.Time.ToUtcTime())
			.Take(limit);
		foreach (var order in orders)
			await SendOrderAsync(order, originalTransactionId, cancellationToken);

		foreach (var trade in (await RestClient.GetTradeHistoryAsync(subscription.Symbol, from, to,
			limit, cancellationToken))
			.Where(trade => Matches(subscription, trade))
			.OrderByDescending(static trade => trade.Time.ToUtcTime())
			.Take(limit))
			await SendAccountTradeAsync(trade, originalTransactionId, cancellationToken, false);
	}

	private async ValueTask OnBalanceAsync(ProBitWsBalanceMessage message,
		CancellationToken cancellationToken)
	{
		long[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _portfolioSubscriptions];
		foreach (var subscriptionId in subscriptions)
			foreach (var balance in message?.Data ?? [])
				await SendBalanceAsync(balance, subscriptionId, CurrentTime, cancellationToken);
	}

	private ValueTask OnOpenOrdersAsync(ProBitWsOrderMessage message,
		CancellationToken cancellationToken)
		=> OnOrdersAsync(message, cancellationToken);

	private ValueTask OnOrderHistoryAsync(ProBitWsOrderMessage message,
		CancellationToken cancellationToken)
		=> OnOrdersAsync(message, cancellationToken);

	private async ValueTask OnOrdersAsync(ProBitWsOrderMessage message,
		CancellationToken cancellationToken)
	{
		foreach (var order in message?.Data ?? [])
		{
			var targets = GetOrderTargets(order);
			if (targets.Length == 0)
				targets = [0];
			foreach (var target in targets)
				await SendOrderAsync(order, target, cancellationToken);
		}
	}

	private async ValueTask OnTradeHistoryAsync(ProBitWsTradeMessage message,
		CancellationToken cancellationToken)
	{
		foreach (var trade in message?.Data ?? [])
		{
			if (trade?.Id.IsEmpty() != false || !AddAccountTrade(trade.Id))
				continue;
			var targets = GetOrderTargets(trade);
			if (targets.Length == 0)
				targets = [0];
			foreach (var target in targets)
				await SendAccountTradeAsync(trade, target, cancellationToken, false);
		}
	}

	private ValueTask SendBalanceAsync(ProBitBalance balance, long originalTransactionId,
		DateTime serverTime, CancellationToken cancellationToken)
	{
		if (balance?.CurrencyId.IsEmpty() != false)
			return default;
		var total = balance.Total.ToDecimal();
		var available = balance.Available.ToDecimal();
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = balance.CurrencyId.ToStockSharp(),
			ServerTime = serverTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, available, true)
		.TryAdd(PositionChangeTypes.BlockedValue,
			total is null || available is null ? null : (total.Value - available.Value).Max(0m), true),
			cancellationToken);
	}

	private ValueTask SendOrderAsync(ProBitOrder order, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (order?.Id.IsEmpty() != false || order.MarketId.IsEmpty())
			return default;
		var volume = order.Quantity.ToDecimal() ??
			(order.FilledQuantity.ToDecimal() ?? 0m) +
			(order.OpenQuantity.ToDecimal() ?? 0m) +
			(order.CancelledQuantity.ToDecimal() ?? 0m);
		var filled = order.FilledQuantity.ToDecimal() ?? 0m;
		var state = order.Status.ToStockSharpOrderState();
		var hasNumericId = long.TryParse(order.Id, NumberStyles.None,
			CultureInfo.InvariantCulture, out var numericOrderId);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.MarketId.ToStockSharp(),
			ServerTime = order.Time.IsEmpty() ? CurrentTime : order.Time.ToUtcTime(),
			PortfolioName = _portfolioName,
			Side = order.Side.ToStockSharpSide(),
			OrderVolume = volume,
			Balance = state == OrderStates.Active
				? order.OpenQuantity.ToDecimal() ?? (volume - filled).Max(0m)
				: 0m,
			OrderPrice = order.Type.EqualsIgnoreCase("market")
				? 0m
				: order.LimitPrice.ToDecimal() ?? 0m,
			AveragePrice = order.FilledCost.ToDecimal() is decimal cost && filled > 0
				? cost / filled
				: null,
			OrderType = order.Type.EqualsIgnoreCase("market")
				? OrderTypes.Market
				: OrderTypes.Limit,
			OrderState = state,
			OrderId = hasNumericId ? numericOrderId : null,
			OrderStringId = order.Id,
			UserOrderId = order.ClientOrderId,
			TransactionId = ParseTransactionId(order.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
			TimeInForce = order.Type.EqualsIgnoreCase("market") ||
				order.TimeInForce.EqualsIgnoreCase("ioc")
				? TimeInForce.CancelBalance
				: TimeInForce.PutInQueue,
			Error = state == OrderStates.Failed
				? new InvalidOperationException("ProBit rejected the order.")
				: null,
		}, cancellationToken);
	}

	private ValueTask SendAccountTradeAsync(ProBitTrade trade, long originalTransactionId,
		CancellationToken cancellationToken, bool addToDeduplication = true)
	{
		if (trade?.Id.IsEmpty() != false || trade.MarketId.IsEmpty())
			return default;
		if (addToDeduplication && !AddAccountTrade(trade.Id))
			return default;
		var hasNumericOrderId = long.TryParse(trade.OrderId, NumberStyles.None,
			CultureInfo.InvariantCulture, out var numericOrderId);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = trade.MarketId.ToStockSharp(),
			ServerTime = trade.Time.IsEmpty() ? CurrentTime : trade.Time.ToUtcTime(),
			PortfolioName = _portfolioName,
			Side = trade.Side.ToStockSharpSide(),
			OrderId = hasNumericOrderId ? numericOrderId : null,
			OrderStringId = trade.OrderId,
			TradeStringId = trade.Id,
			TradePrice = trade.Price.ToDecimal(),
			TradeVolume = trade.Quantity.ToDecimal(),
			Commission = trade.FeeAmount.ToDecimal(),
			CommissionCurrency = trade.FeeCurrencyId,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private bool AddAccountTrade(string tradeId)
	{
		using (_sync.EnterScope())
		{
			if (!_accountTradeIds.Add(tradeId))
				return false;
			if (_accountTradeIds.Count > 10000)
				_accountTradeIds.Clear();
			return true;
		}
	}

	private long[] GetOrderTargets(ProBitOrder order)
	{
		using (_sync.EnterScope())
			return [.. _orderSubscriptions
				.Where(pair => Matches(pair.Value, order))
				.Select(static pair => pair.Key)];
	}

	private long[] GetOrderTargets(ProBitTrade trade)
	{
		using (_sync.EnterScope())
			return [.. _orderSubscriptions
				.Where(pair => Matches(pair.Value, trade))
				.Select(static pair => pair.Key)];
	}

	private static bool Matches(OrderSubscription subscription, ProBitOrder order)
		=> order is not null &&
			(subscription.Symbol.IsEmpty() || subscription.Symbol.EqualsIgnoreCase(order.MarketId)) &&
			(subscription.OrderIdentifier.IsEmpty() ||
				subscription.OrderIdentifier.EqualsIgnoreCase(order.Id)) &&
			(subscription.ClientOrderId.IsEmpty() ||
				subscription.ClientOrderId.EqualsIgnoreCase(order.ClientOrderId)) &&
			(subscription.Side is null || subscription.Side == order.Side.ToStockSharpSide());

	private static bool Matches(OrderSubscription subscription, ProBitTrade trade)
		=> trade is not null &&
			(subscription.Symbol.IsEmpty() || subscription.Symbol.EqualsIgnoreCase(trade.MarketId)) &&
			(subscription.OrderIdentifier.IsEmpty() ||
				subscription.OrderIdentifier.EqualsIgnoreCase(trade.OrderId)) &&
			subscription.ClientOrderId.IsEmpty() &&
			(subscription.Side is null || subscription.Side == trade.Side.ToStockSharpSide());
}
