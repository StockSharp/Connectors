namespace StockSharp.FinamTrade;

public partial class FinamTradeMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage message, CancellationToken cancellationToken)
	{
		var accountId = GetRequiredAccountId(message.PortfolioName);
		var condition = message.Condition as FinamTradeOrderCondition;
		var orderType = message.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Market or OrderTypes.Limit or
			OrderTypes.Conditional))
			throw new NotSupportedException(
				"Finam supports market, limit, stop and stop-limit orders.");
		if (message.Volume <= 0)
			throw new InvalidOperationException(
				"Finam order quantity must be positive.");
		if (orderType == OrderTypes.Limit && message.Price <= 0)
			throw new InvalidOperationException(
				"Finam limit order price must be positive.");
		if (orderType == OrderTypes.Conditional &&
			condition?.StopPrice is not > 0)
			throw new InvalidOperationException(
				"Finam conditional order requires a positive stop price.");

		var clientOrderId = message.TransactionId.ToString(
			CultureInfo.InvariantCulture);
		var nativeType = orderType switch
		{
			OrderTypes.Market => "ORDER_TYPE_MARKET",
			OrderTypes.Limit => "ORDER_TYPE_LIMIT",
			OrderTypes.Conditional when message.Price > 0 =>
				"ORDER_TYPE_STOP_LIMIT",
			OrderTypes.Conditional => "ORDER_TYPE_STOP",
			_ => throw new ArgumentOutOfRangeException(nameof(message.OrderType)),
		};
		var request = new FinamOrderRequest
		{
			Symbol = message.SecurityId.ToNativeSymbol(),
			Quantity = message.Volume.ToNativeDecimal(),
			Side = message.Side.ToNative(),
			Type = nativeType,
			TimeInForce = (condition?.TimeInForce ??
				FinamTimeInForces.Day).ToNative(),
			LimitPrice = nativeType is "ORDER_TYPE_LIMIT" or
				"ORDER_TYPE_STOP_LIMIT"
					? message.Price.ToNativeDecimal() : null,
			StopPrice = condition?.StopPrice?.ToNativeDecimal(),
			StopCondition = condition?.StopCondition.ToNative(),
			ClientOrderId = clientOrderId,
			Comment = message.Comment,
		};

		var response = await _rest.PlaceOrder(accountId, request,
			cancellationToken);
		_orderTransactions[clientOrderId] = message.TransactionId;
		if (!response?.OrderId.IsEmpty() == true)
		{
			_orderTransactions[response.OrderId] = message.TransactionId;
			_trackedOrders.Add(response.OrderId);
		}

		if (response is not null)
			await SendOrder(response, message.TransactionId, cancellationToken);
		else
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				OriginalTransactionId = message.TransactionId,
				TransactionId = message.TransactionId,
				OrderStringId = clientOrderId,
				PortfolioName = accountId,
				SecurityId = message.SecurityId,
				Side = message.Side,
				OrderType = orderType,
				OrderPrice = message.Price,
				OrderVolume = message.Volume,
				Balance = message.Volume,
				OrderState = OrderStates.Pending,
				ServerTime = DateTime.UtcNow,
				Condition = condition,
			}, cancellationToken);
		}
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

		var response = await _rest.CancelOrder(
			GetRequiredAccountId(message.PortfolioName), orderId,
			cancellationToken);
		_trackedOrders.Add(orderId);
		if (response is not null)
			await SendOrder(response, message.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId,
			cancellationToken);

		var accountId = GetRequiredAccountId(message.PortfolioName);
		var native = new FinamSocketSubscription(
			"ACCOUNT", null, null, accountId);
		if (!message.IsSubscribe)
		{
			if (_portfolioSubscriptionId == message.OriginalTransactionId)
			{
				_portfolioSubscriptionId = 0;
				await _socket.Unsubscribe(native, cancellationToken);
			}
			return;
		}

		_resolvedAccountId = accountId;
		await SendPortfolioSnapshot(message.TransactionId, cancellationToken);
		if (!message.IsHistoryOnly())
		{
			_portfolioSubscriptionId = message.TransactionId;
			await _socket.Subscribe(native, cancellationToken);
		}
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId,
			cancellationToken);

		var accountId = GetRequiredAccountId(message.PortfolioName);
		var ordersSubscription = new FinamSocketSubscription(
			"ORDERS", null, null, accountId);
		var tradesSubscription = new FinamSocketSubscription(
			"TRADES", null, null, accountId);
		if (!message.IsSubscribe)
		{
			if (_orderStatusSubscriptionId == message.OriginalTransactionId)
			{
				_orderStatusSubscriptionId = 0;
				_orderStatusFilter = null;
				await _socket.Unsubscribe(ordersSubscription, cancellationToken);
				await _socket.Unsubscribe(tradesSubscription, cancellationToken);
			}
			return;
		}

		_resolvedAccountId = accountId;
		if (!message.OrderStringId.IsEmpty())
		{
			_trackedOrders.Add(message.OrderStringId);
			await SendOrder(
				await _rest.GetOrder(accountId, message.OrderStringId,
					cancellationToken),
				message.TransactionId, cancellationToken);
		}
		else
		{
			_orderStatusFilter = (OrderStatusMessage)message.Clone();
			await SendOrderSnapshot(message.TransactionId, cancellationToken);
		}

		if (!message.IsHistoryOnly())
		{
			_orderStatusSubscriptionId = message.TransactionId;
			await _socket.Subscribe(ordersSubscription, cancellationToken);
			await _socket.Subscribe(tradesSubscription, cancellationToken);
		}
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshot(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var account = await _rest.GetAccount(
			GetRequiredAccountId(), cancellationToken);
		if (account is not null)
			await SendAccount(account, originalTransactionId, cancellationToken);
	}

	private async ValueTask ProcessAccount(FinamAccount account,
		CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId != 0)
			await SendAccount(account, _portfolioSubscriptionId,
				cancellationToken);
	}

	private async ValueTask SendAccount(FinamAccount account,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (account is null)
			return;

		var accountId = account.AccountId.IsEmpty(_resolvedAccountId);
		if (accountId.IsEmpty())
			return;
		_resolvedAccountId = accountId;

		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = accountId,
			BoardCode = BoardCodes.Finam,
			Currency = CurrencyTypes.RUB,
		}, cancellationToken);

		foreach (var position in account.Positions ?? [])
		{
			if (position?.Symbol.IsEmpty() != false)
				continue;

			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = accountId,
				SecurityId = position.Symbol.ToSecurityId(),
				ServerTime = DateTime.UtcNow,
			}
			.TryAdd(PositionChangeTypes.CurrentValue,
				position.Quantity.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.AveragePrice,
				position.AveragePrice.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.CurrentPrice,
				position.CurrentPrice.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.VariationMargin,
				position.MaintenanceMargin.ToDecimal())
			.TryAdd(PositionChangeTypes.RealizedPnL,
				position.DailyPnl.ToDecimal())
			.TryAdd(PositionChangeTypes.UnrealizedPnL,
				position.UnrealizedPnl.ToDecimal()), cancellationToken);
		}

		foreach (var money in account.Cash ?? [])
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = accountId,
				SecurityId = SecurityId.Money,
				ServerTime = DateTime.UtcNow,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, money.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.Currency,
				money.CurrencyCode.ToCurrency()), cancellationToken);
		}
	}

	private async ValueTask SendOrderSnapshot(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var filter = _orderStatusFilter;
		if (filter is null)
			return;

		var accountId = GetRequiredAccountId(filter.PortfolioName);
		var orders = (await _rest.GetOrders(accountId,
			cancellationToken))?.Orders ?? [];
		var sequence = orders.AsEnumerable();
		if (filter.From is not null)
			sequence = sequence.Where(o => GetOrderTime(o) >= filter.From);
		if (filter.To is not null)
			sequence = sequence.Where(o => GetOrderTime(o) <= filter.To);
		if (filter.Side is Sides side)
			sequence = sequence.Where(o => o.Order?.Side.ToSide() == side);
		if (filter.States?.Length > 0)
			sequence = sequence.Where(o =>
				filter.States.Contains(o.Status.ToOrderState()));

		var securityIds = filter.SecurityIds ?? [];
		if (filter.SecurityId != default)
			securityIds = securityIds.Append(filter.SecurityId).ToArray();
		if (securityIds.Length > 0)
		{
			var symbols = securityIds.Select(s => s.ToNativeSymbol())
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			sequence = sequence.Where(o =>
				o.Order is not null && symbols.Contains(o.Order.Symbol));
		}

		sequence = sequence.Skip((int)Math.Min(
			Math.Max(0, filter.Skip ?? 0), int.MaxValue));
		if (filter.Count is long count)
			sequence = sequence.Take((int)Math.Min(count, int.MaxValue));

		foreach (var order in sequence)
			await SendOrder(order, originalTransactionId, cancellationToken);

		var trades = await _rest.GetTrades(accountId, filter.From, filter.To,
			filter.Count is long tradeCount
				? (int)Math.Min(tradeCount, 1000) : 1000,
			cancellationToken);

		foreach (var trade in trades?.Trades ?? [])
			await SendAccountTrade(trade, originalTransactionId,
				cancellationToken);
	}

	private async ValueTask ProcessOrder(FinamOrderState order,
		CancellationToken cancellationToken)
	{
		if (order is null)
			return;
		var originalTransactionId = _orderStatusSubscriptionId;
		if (originalTransactionId == 0)
			originalTransactionId = FindOrderTransaction(
				order.OrderId, order.Order?.ClientOrderId);
		await SendOrder(order, originalTransactionId, cancellationToken);
	}

	private ValueTask SendOrder(FinamOrderState state,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (state?.Order is null)
			return default;

		var order = state.Order;
		var transactionId = FindOrderTransaction(
			state.OrderId, order.ClientOrderId);
		if (!state.OrderId.IsEmpty() && transactionId != 0)
			_orderTransactions[state.OrderId] = transactionId;

		var orderState = state.Status.ToOrderState();
		var signatureKey = $"{state.OrderId}:{originalTransactionId}";
		var signature =
			$"{state.Status}:{state.ExecutedQuantity?.Value}:" +
			$"{state.RemainingQuantity?.Value}:{state.WithdrawAt:O}";
		if (!state.OrderId.IsEmpty() &&
			_orderSignatures.TryGetValue(signatureKey, out var previous) &&
			previous == signature)
			return default;
		if (!state.OrderId.IsEmpty())
			_orderSignatures[signatureKey] = signature;

		if (!state.OrderId.IsEmpty())
		{
			if (orderState is OrderStates.Active or OrderStates.Pending)
				_trackedOrders.Add(state.OrderId);
			else
				_trackedOrders.Remove(state.OrderId);
		}

		var condition = order.Type is "ORDER_TYPE_STOP" or
			"ORDER_TYPE_STOP_LIMIT"
				? new FinamTradeOrderCondition
				{
					StopPrice = order.StopPrice.ToDecimal(),
					StopCondition = order.StopCondition
						.EqualsIgnoreCase("STOP_CONDITION_LAST_DOWN")
							? FinamStopConditions.LastDown
							: FinamStopConditions.LastUp,
				}
				: null;
		var serverTime = GetOrderTime(state);

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId,
			TransactionId = transactionId,
			OrderStringId = state.OrderId.IsEmpty(order.ClientOrderId),
			PortfolioName = order.AccountId.IsEmpty(_resolvedAccountId),
			SecurityId = order.Symbol.ToSecurityId(),
			Side = order.Side.ToSide(),
			OrderType = order.Type.ToOrderType(),
			OrderPrice = order.LimitPrice.ToDecimal() ??
				order.StopPrice.ToDecimal() ?? 0,
			OrderVolume = state.InitialQuantity.ToDecimal() ??
				order.Quantity.ToDecimal(),
			Balance = state.RemainingQuantity.ToDecimal(),
			OrderState = orderState,
			ServerTime = serverTime,
			Condition = condition,
			Error = orderState == OrderStates.Failed
				? new InvalidOperationException(
					$"Finam rejected order {state.OrderId}: {state.Status}.")
				: null,
		}, cancellationToken);
	}

	private static DateTime GetOrderTime(FinamOrderState order)
		=> order?.WithdrawAt ?? order?.AcceptAt ?? order?.TransactAt ??
			DateTime.UtcNow;

	private async ValueTask ProcessAccountTrade(FinamAccountTrade trade,
		CancellationToken cancellationToken)
	{
		if (_orderStatusSubscriptionId != 0)
			await SendAccountTrade(trade, _orderStatusSubscriptionId,
				cancellationToken);
	}

	private ValueTask SendAccountTrade(FinamAccountTrade trade,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (trade?.TradeId.IsEmpty() != false)
			return default;
		var tradeKey = $"{originalTransactionId}:{trade.TradeId}";
		if (_seenTrades.Contains(tradeKey))
			return default;
		_seenTrades.Add(tradeKey);

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originalTransactionId,
			OrderStringId = trade.OrderId,
			TradeStringId = trade.TradeId,
			PortfolioName = trade.AccountId.IsEmpty(_resolvedAccountId),
			SecurityId = trade.Symbol.ToSecurityId(),
			Side = trade.Side.ToSide(),
			TradePrice = trade.Price.ToDecimal(),
			TradeVolume = trade.Size.ToDecimal(),
			ServerTime = trade.Timestamp == default
				? DateTime.UtcNow : trade.Timestamp,
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
}
