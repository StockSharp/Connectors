namespace StockSharp.Bcs;

public partial class BcsMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage message, CancellationToken cancellationToken)
	{
		if (message.OrderType is not (null or OrderTypes.Market or
			OrderTypes.Limit))
			throw new NotSupportedException(
				"BCS Trade API supports market and limit orders.");
		if (message.OrderType != OrderTypes.Market && message.Price <= 0)
			throw new InvalidOperationException(
				"BCS limit order price must be positive.");

		var clientOrderId = Guid.NewGuid().ToString();
		var response = await _rest.CreateOrder(new()
		{
			ClientOrderId = clientOrderId,
			Side = message.Side.ToNative(),
			OrderType = message.OrderType.ToNative(),
			OrderQuantity = ToQuantity(message.Volume),
			Ticker = message.SecurityId.SecurityCode,
			ClassCode = message.SecurityId.BoardCode,
			Price = message.OrderType == OrderTypes.Market
				? null : message.Price,
		}, cancellationToken);

		clientOrderId = response?.ClientOrderId.IsEmpty(clientOrderId) ??
			clientOrderId;
		_orderTransactions[clientOrderId] = message.TransactionId;
		_trackedClientOrders.Add(clientOrderId);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = message.TransactionId,
			TransactionId = message.TransactionId,
			OrderStringId = clientOrderId,
			PortfolioName = message.PortfolioName
				.IsEmpty(_resolvedPortfolioName),
			SecurityId = message.SecurityId,
			Side = message.Side,
			OrderType = message.OrderType ?? OrderTypes.Limit,
			OrderPrice = message.Price,
			OrderVolume = message.Volume,
			Balance = message.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = DateTime.UtcNow,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage message, CancellationToken cancellationToken)
	{
		var oldOrderId = message.OldOrderStringId;
		if (oldOrderId.IsEmpty() && message.OldOrderId is long numericId)
			oldOrderId = numericId.ToString(CultureInfo.InvariantCulture);
		if (oldOrderId.IsEmpty())
			throw new InvalidOperationException(
				LocalizedStrings.OrderNoExchangeId.Put(
					message.OriginalTransactionId));

		var clientOrderId = Guid.NewGuid().ToString();
		var response = await _rest.UpdateOrder(new()
		{
			OrderIdType = BcsRestClient.GetOrderIdType(oldOrderId),
			OrderId = oldOrderId,
			ClientOrderId = clientOrderId,
			OrderType = message.OrderType.ToNative(),
			OrderQuantity = ToQuantity(message.Volume),
			Price = message.OrderType == OrderTypes.Market
				? null : message.Price,
		}, cancellationToken);

		clientOrderId = response?.ClientOrderId.IsEmpty(clientOrderId) ??
			clientOrderId;
		_orderTransactions[clientOrderId] = message.TransactionId;
		_trackedClientOrders.Add(clientOrderId);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = message.TransactionId,
			TransactionId = message.TransactionId,
			OrderStringId = clientOrderId,
			PortfolioName = message.PortfolioName
				.IsEmpty(_resolvedPortfolioName),
			SecurityId = message.SecurityId,
			Side = message.Side,
			OrderType = message.OrderType ?? OrderTypes.Limit,
			OrderPrice = message.Price,
			OrderVolume = message.Volume,
			Balance = message.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = DateTime.UtcNow,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage message, CancellationToken cancellationToken)
	{
		var orderId = message.OrderStringId;
		if (orderId.IsEmpty() && message.OrderId is long numericId)
			orderId = numericId.ToString(CultureInfo.InvariantCulture);
		if (orderId.IsEmpty())
			throw new InvalidOperationException(
				LocalizedStrings.OrderNoExchangeId.Put(
					message.OriginalTransactionId));

		await _rest.CancelOrder(new()
		{
			OrderIdType = BcsRestClient.GetOrderIdType(orderId),
			OrderId = orderId,
			ClientOrderId = Guid.NewGuid().ToString(),
		}, cancellationToken);
		_trackedClientOrders.Add(orderId);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId,
			cancellationToken);
		if (!message.IsSubscribe)
		{
			if (_portfolioSubscriptionId == message.OriginalTransactionId)
				_portfolioSubscriptionId = 0;
			return;
		}

		await SendPortfolioSnapshot(message.TransactionId, cancellationToken);
		if (!message.IsHistoryOnly())
			_portfolioSubscriptionId = message.TransactionId;
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId,
			cancellationToken);
		if (!message.IsSubscribe)
		{
			if (_orderStatusSubscriptionId == message.OriginalTransactionId)
			{
				_orderStatusSubscriptionId = 0;
				_orderStatusFilter = null;
				_lastOrderSearch = default;
			}
			return;
		}

		if (!message.OrderStringId.IsEmpty())
		{
			_trackedClientOrders.Add(message.OrderStringId);
			await ProcessOrderStatus(
				await _rest.GetOrder(message.OrderStringId, cancellationToken),
				message.TransactionId, cancellationToken);
		}
		else
		{
			_orderStatusFilter = (OrderStatusMessage)message.Clone();
			_lastOrderSearch = default;
			await SendOrderSnapshot(message.TransactionId, cancellationToken);
		}

		if (!message.IsHistoryOnly())
			_orderStatusSubscriptionId = message.TransactionId;
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshot(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var positions = await _rest.GetPortfolio(cancellationToken) ?? [];
		var limits = await _rest.GetLimits(cancellationToken);
		var accounts = positions.Select(p => p.Account)
			.Where(a => !a.IsEmpty())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (accounts.Length == 0)
			accounts = [_resolvedPortfolioName.IsEmpty(PortfolioName)];
		else
			_resolvedPortfolioName = accounts[0];

		foreach (var account in accounts)
		{
			await SendOutMessageAsync(new PortfolioMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = account,
				BoardCode = "MOEX",
				Currency = CurrencyTypes.RUB,
			}, cancellationToken);

			foreach (var position in positions.Where(p =>
				p.Account.IsEmpty() || p.Account.EqualsIgnoreCase(account)))
			{
				if (position.Ticker.IsEmpty() ||
					position.Type.EqualsIgnoreCase("moneyLimit"))
					continue;

				await SendOutMessageAsync(new PositionChangeMessage
				{
					OriginalTransactionId = originalTransactionId,
					PortfolioName = account,
					SecurityId = position.Ticker.ToSecurityId(position.Board),
					ServerTime = DateTime.UtcNow,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, position.Quantity, true)
				.TryAdd(PositionChangeTypes.BlockedValue, position.Locked, true)
				.TryAdd(PositionChangeTypes.AveragePrice, position.BalancePrice,
					true)
				.TryAdd(PositionChangeTypes.CurrentPrice, position.CurrentPrice,
					true)
				.TryAdd(PositionChangeTypes.UnrealizedPnL,
					position.UnrealizedPnL)
				.TryAdd(PositionChangeTypes.RealizedPnL, position.DailyPnL)
				.TryAdd(PositionChangeTypes.Currency,
					position.Currency.ToCurrency()), cancellationToken);
			}

			foreach (var money in limits?.MoneyLimits ?? [])
			{
				await SendOutMessageAsync(new PositionChangeMessage
				{
					OriginalTransactionId = originalTransactionId,
					PortfolioName = account,
					SecurityId = SecurityId.Money,
					ServerTime = money.LoadDate ?? DateTime.UtcNow,
				}
				.TryAdd(PositionChangeTypes.CurrentValue,
					money.Quantity?.Value, true)
				.TryAdd(PositionChangeTypes.BlockedValue, money.Locked, true)
				.TryAdd(PositionChangeTypes.Currency,
					money.CurrencyCode.ToCurrency()), cancellationToken);
			}
		}
	}

	private async ValueTask SendOrderSnapshot(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var filter = _orderStatusFilter;
		if (filter is null)
			return;

		var now = DateTime.UtcNow;
		var from = filter.From?.ToUniversalTime();
		if (_lastOrderSearch != default)
		{
			var incrementalFrom = _lastOrderSearch.AddMinutes(-1);
			if (from is null || from < incrementalFrom)
				from = incrementalFrom;
		}

		var securityIds = filter.SecurityIds;
		if (filter.SecurityId != default)
			securityIds = securityIds.Append(filter.SecurityId).ToArray();
		var request = new BcsOrderSearchRequest
		{
			From = from,
			To = filter.To?.ToUniversalTime(),
			Side = filter.Side is Sides side
				? side == Sides.Buy ? 1 : 2 : null,
			Statuses = ToNativeStatuses(filter.States),
			Tickers = EmptyAsNull(securityIds.Select(s => s.SecurityCode)
				.Where(s => !s.IsEmpty()).Distinct().ToArray()),
			ClassCodes = EmptyAsNull(securityIds.Select(s => s.BoardCode)
				.Where(s => !s.IsEmpty()).Distinct().ToArray()),
		};

		const int pageSize = 100;
		var skip = Math.Max(0, filter.Skip ?? 0);
		var left = filter.Count ?? long.MaxValue;
		for (var page = 0; left > 0; page++)
		{
			var response = await _rest.SearchOrders(
				request, page, pageSize, cancellationToken);
			var records = response?.Records ?? [];
			foreach (var order in records)
			{
				if (skip > 0)
				{
					skip--;
					continue;
				}
				await ProcessOrder(order, originalTransactionId,
					cancellationToken);
				if (--left <= 0)
					break;
			}
			if (records.Length < pageSize ||
				page + 1 >= response?.TotalPages)
				break;
		}

		var tradeRequest = new BcsTradeSearchRequest
		{
			From = from,
			To = filter.To?.ToUniversalTime(),
			Side = filter.Side?.ToNative(),
			Tickers = request.Tickers,
			ClassCodes = request.ClassCodes,
		};
		for (var page = 0; ; page++)
		{
			var response = await _rest.SearchTrades(
				tradeRequest, page, pageSize, cancellationToken);
			var records = response?.Records ?? [];
			foreach (var trade in records)
				await ProcessOwnTrade(trade, originalTransactionId,
					cancellationToken);
			if (records.Length < pageSize ||
				page + 1 >= response?.TotalPages)
				break;
		}

		_lastOrderSearch = now;
	}

	private async ValueTask ProcessOrderStatus(BcsOrderStatusResponse response,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		var order = response?.Data;
		if (order is null)
			return;

		var transactionId = FindOrderTransaction(
			response.ClientOrderId,
			response.OriginalClientOrderId,
			order.OrderId);
		if (!order.OrderId.IsEmpty() && transactionId != 0)
			_orderTransactions[order.OrderId] = transactionId;

		var state = order.OrderStatus.ToOrderState();
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId == 0
				? transactionId : originalTransactionId,
			TransactionId = transactionId,
			OrderStringId = order.OrderId
				.IsEmpty(response.ClientOrderId),
			PortfolioName = _resolvedPortfolioName.IsEmpty(PortfolioName),
			SecurityId = order.Ticker.ToSecurityId(order.ClassCode),
			Side = order.Side.ToSide(),
			OrderType = order.OrderType.ToOrderType(),
			OrderPrice = order.Price,
			OrderVolume = order.OrderQuantity,
			Balance = order.RemainedQuantity,
			AveragePrice = order.AveragePrice,
			OrderState = state,
			ServerTime = order.TransactionTime == default
				? DateTime.UtcNow : order.TransactionTime,
			Error = state == OrderStates.Failed
				? new InvalidOperationException(
					order.RejectReason.IsEmpty("BCS rejected the order."))
				: null,
		}, cancellationToken);

		if (!order.ExecutionId.IsEmpty() && order.LastQuantity > 0 &&
			!_seenTrades.Contains(order.ExecutionId))
		{
			_seenTrades.Add(order.ExecutionId);
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = originalTransactionId == 0
					? transactionId : originalTransactionId,
				OrderStringId = order.OrderId,
				TradeStringId = order.ExecutionId,
				PortfolioName = _resolvedPortfolioName.IsEmpty(PortfolioName),
				SecurityId = order.Ticker.ToSecurityId(order.ClassCode),
				Side = order.Side.ToSide(),
				TradePrice = order.AveragePrice ?? order.Price,
				TradeVolume = order.LastQuantity,
				Commission = order.Commission,
				ServerTime = order.TransactionTime == default
					? DateTime.UtcNow : order.TransactionTime,
			}, cancellationToken);
		}

		if (state is OrderStates.Done or OrderStates.Failed)
		{
			_trackedClientOrders.Remove(response.ClientOrderId);
			_trackedClientOrders.Remove(response.OriginalClientOrderId);
			_trackedClientOrders.Remove(order.OrderId);
		}
	}

	private ValueTask ProcessOrder(BcsOrder order, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (order?.OrderId.IsEmpty() != false)
			return default;

		var state = order.OrderStatus.ToOrderState(order.RejectReason);
		var signature =
			$"{order.UpdateDateTime:O}:{order.OrderStatus}:{order.ExecutedQuantity}";
		if (_orderSignatures.TryGetValue(order.OrderId, out var previous) &&
			previous == signature)
			return default;
		_orderSignatures[order.OrderId] = signature;

		if (state is OrderStates.Active or OrderStates.Pending)
			_trackedClientOrders.Add(order.OrderId);
		else
			_trackedClientOrders.Remove(order.OrderId);

		var transactionId = FindOrderTransaction(order.OrderId);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId,
			TransactionId = transactionId,
			OrderId = order.OrderNum == 0 ? null : order.OrderNum,
			OrderStringId = order.OrderId,
			PortfolioName = _resolvedPortfolioName.IsEmpty(PortfolioName),
			SecurityId = order.Ticker.ToSecurityId(order.ClassCode),
			Side = order.Side == 1 ? Sides.Buy : Sides.Sell,
			OrderType = order.OrderType.ToOrderType(),
			OrderPrice = order.Price,
			OrderVolume = order.OrderQuantity,
			Balance = order.RemainedQuantity,
			AveragePrice = order.AveragePrice,
			OrderState = state,
			ServerTime = order.UpdateDateTime ?? order.OrderDateTime,
			Error = state == OrderStates.Failed
				? new InvalidOperationException(
					order.RejectReason.IsEmpty("BCS rejected the order."))
				: null,
		}, cancellationToken);
	}

	private ValueTask ProcessOwnTrade(BcsOwnTrade trade,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (trade is null || trade.TradeNum == 0)
			return default;
		var tradeId = trade.TradeNum.ToString(CultureInfo.InvariantCulture);
		if (_seenTrades.Contains(tradeId))
			return default;
		_seenTrades.Add(tradeId);

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originalTransactionId,
			OrderId = trade.OrderNum == 0 ? null : trade.OrderNum,
			TradeId = trade.TradeNum,
			PortfolioName = _resolvedPortfolioName.IsEmpty(PortfolioName),
			SecurityId = trade.Ticker.ToSecurityId(trade.ClassCode),
			Side = trade.Side.ToSide(),
			TradePrice = trade.Price,
			TradeVolume = trade.TradeQuantity,
			ServerTime = trade.TradeDateTime,
		}, cancellationToken);
	}

	private long FindOrderTransaction(params string[] ids)
	{
		foreach (var id in ids)
		{
			if (!id.IsEmpty() &&
				_orderTransactions.TryGetValue(id, out var transactionId))
				return transactionId;
		}
		return 0;
	}

	private static long ToQuantity(decimal volume)
	{
		if (volume <= 0 || volume != decimal.Truncate(volume) ||
			volume > long.MaxValue)
			throw new InvalidOperationException(
				"BCS order quantity must be a positive whole number of units.");
		return (long)volume;
	}

	private static int[] ToNativeStatuses(OrderStates[] states)
	{
		if (states is null || states.Length == 0)
			return null;
		var result = new HashSet<int>();
		foreach (var state in states)
		{
			switch (state)
			{
				case OrderStates.Active:
				case OrderStates.Pending:
					result.Add(3);
					break;
				case OrderStates.Done:
					result.Add(1);
					result.Add(2);
					break;
				case OrderStates.Failed:
					result.Add(1);
					break;
			}
		}
		return EmptyAsNull(result.ToArray());
	}

	private static T[] EmptyAsNull<T>(T[] values)
		=> values?.Length > 0 ? values : null;
}
