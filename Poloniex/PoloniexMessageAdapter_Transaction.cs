namespace StockSharp.Poloniex;

partial class PoloniexMessageAdapter
{
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;

	private string PortfolioName => nameof(Poloniex) + "_" + Key.ToId();

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();

		switch (regMsg.OrderType)
		{
			case null:
			case OrderTypes.Limit:
			case OrderTypes.Market:
				break;
			case OrderTypes.Conditional:
				{
					if (regMsg.Condition is not PoloniexOrderCondition { IsWithdraw: true } condition)
						throw new NotSupportedException(
							LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType,
								regMsg.TransactionId));

					await _restClient.WithdrawAsync(regMsg.SecurityId.SecurityCode, regMsg.Volume,
						condition.WithdrawInfo, cancellationToken);

					await SendOutMessageAsync(new ExecutionMessage
					{
						DataTypeEx = DataType.Transactions,
						ServerTime = CurrentTime,
						OriginalTransactionId = regMsg.TransactionId,
						TransactionId = regMsg.TransactionId,
						OrderState = OrderStates.Done,
						HasOrderInfo = true,
					}, cancellationToken);
					return;
				}
			default:
				throw new NotSupportedException(
					LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType,
						regMsg.TransactionId));
		}

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
			throw new InvalidOperationException("Poloniex limit order price must be positive.");

		var orderId = await _restClient.NewOrderAsync(regMsg.TransactionId,
			regMsg.SecurityId.ToCurrency(), regMsg.Side, orderType, regMsg.Price, regMsg.Volume,
			regMsg.TimeInForce, regMsg.PostOnly, cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = regMsg.SecurityId,
			PortfolioName = PortfolioName,
			OrderId = orderId,
			OrderPrice = orderType == OrderTypes.Market ? 0m : regMsg.Price,
			OrderVolume = regMsg.Volume,
			Balance = regMsg.Volume,
			OrderType = orderType,
			Side = regMsg.Side,
			TimeInForce = regMsg.TimeInForce,
			PostOnly = regMsg.PostOnly,
			ServerTime = CurrentTime,
			TransactionId = regMsg.TransactionId,
			OriginalTransactionId = regMsg.TransactionId,
			OrderState = OrderStates.Active,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();

		if (cancelMsg.OrderId is long orderId)
			await _restClient.CancelOrderByIdAsync(orderId, cancellationToken);
		else
			await _restClient.CancelOrderByClientIdAsync(cancelMsg.OriginalTransactionId,
				cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = cancelMsg.SecurityId,
			PortfolioName = PortfolioName,
			OrderId = cancelMsg.OrderId,
			OrderState = OrderStates.Done,
			Balance = 0m,
			ServerTime = CurrentTime,
			OriginalTransactionId = cancelMsg.TransactionId,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		await _restClient.CancelAllOrdersAsync(
			cancelMsg.SecurityId.SecurityCode.IsEmpty()
				? null
				: cancelMsg.SecurityId.ToCurrency(),
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();

		if (replaceMsg.OldOrderId is not long oldOrderId)
			throw new InvalidOperationException(
				LocalizedStrings.OrderNoExchangeId.Put(replaceMsg.OriginalTransactionId));

		decimal? volume = replaceMsg.Volume == 0 ? null : replaceMsg.Volume;
		var orderId = await _restClient.ReplaceOrderAsync(replaceMsg.TransactionId, oldOrderId,
			replaceMsg.Price, volume, replaceMsg.TimeInForce, replaceMsg.PostOnly,
			cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = replaceMsg.SecurityId,
			PortfolioName = PortfolioName,
			OrderId = orderId,
			OrderPrice = replaceMsg.Price,
			OrderVolume = volume,
			Balance = volume,
			OrderType = OrderTypes.Limit,
			Side = replaceMsg.Side,
			TimeInForce = replaceMsg.TimeInForce,
			PostOnly = replaceMsg.PostOnly,
			ServerTime = CurrentTime,
			TransactionId = replaceMsg.TransactionId,
			OriginalTransactionId = replaceMsg.TransactionId,
			OrderState = OrderStates.Active,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsurePrivateReady();

		if (!lookupMsg.IsSubscribe)
		{
			_portfolioSubscriptionId = 0;
			if (_orderStatusSubscriptionId == 0)
				await _privateSocket.UnsubscribeAccountAsync(cancellationToken);
			return;
		}

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = PortfolioName,
			BoardCode = BoardCodes.Poloniex,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);

		await SendBalancesAsync(lookupMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);

		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
			return;
		}

		_portfolioSubscriptionId = lookupMsg.TransactionId;
		await _privateSocket.SubscribeAccountAsync(cancellationToken);
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
			if (_portfolioSubscriptionId == 0)
				await _privateSocket.UnsubscribeAccountAsync(cancellationToken);
			return;
		}

		var symbol = statusMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: statusMsg.SecurityId.ToCurrency();
		var orders = await _restClient.GetOpenOrdersAsync(symbol, cancellationToken) ?? [];

		foreach (var order in orders.OrderBy(static order => order.CreateTime))
		{
			await SendOrderAsync(order, statusMsg.TransactionId, cancellationToken);

			if (order.FilledQuantity <= 0)
				continue;

			foreach (var trade in await _restClient.GetOrderTradesAsync(order.Id,
				cancellationToken) ?? [])
				await SendOwnTradeAsync(trade, statusMsg.TransactionId, cancellationToken);
		}

		await SendSubscriptionResultAsync(statusMsg, cancellationToken);

		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
			return;
		}

		_orderStatusSubscriptionId = statusMsg.TransactionId;
		await _privateSocket.SubscribeAccountAsync(cancellationToken);
	}

	private async ValueTask SendBalancesAsync(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		foreach (var account in await _restClient.GetBalancesAsync(cancellationToken) ?? [])
		{
			foreach (var balance in account.Balances ?? [])
			{
				if (balance.Available == 0 && balance.Hold == 0)
					continue;

				await SendOutMessageAsync(new PositionChangeMessage
				{
					PortfolioName = PortfolioName,
					SecurityId = new SecurityId
					{
						SecurityCode = balance.Currency,
						BoardCode = BoardCodes.Poloniex,
					},
					ServerTime = CurrentTime,
					OriginalTransactionId = originalTransactionId,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, balance.Available, true)
				.TryAdd(PositionChangeTypes.BlockedValue, balance.Hold, true), cancellationToken);
			}
		}
	}

	private ValueTask SessionOnBalanceChanged(PoloniexBalanceUpdate balance,
		CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId == 0 || balance.Currency.IsEmpty())
			return default;

		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = PortfolioName,
			SecurityId = new SecurityId
			{
				SecurityCode = balance.Currency,
				BoardCode = BoardCodes.Poloniex,
			},
			ServerTime = (balance.ChangeTime != 0 ? balance.ChangeTime : balance.Timestamp)
				.FromUnix(false),
			OriginalTransactionId = _portfolioSubscriptionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, balance.Available, true)
		.TryAdd(PositionChangeTypes.BlockedValue, balance.Hold, true), cancellationToken);
	}

	private async ValueTask SessionOnOrderChanged(PoloniexOrderUpdate order,
		CancellationToken cancellationToken)
	{
		if (order.Symbol.IsEmpty())
			return;

		var transactionId = order.ClientOrderId.ToClientTransactionId();
		var originalTransactionId = transactionId ?? _orderStatusSubscriptionId;
		var balance = (order.Quantity - order.FilledQuantity).Max(0m);
		var orderState = order.State.ToOrderState();

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToStockSharp(),
			PortfolioName = PortfolioName,
			ServerTime = (order.Timestamp != 0 ? order.Timestamp : order.CreateTime)
				.FromUnix(false),
			OrderId = order.OrderId,
			OrderPrice = order.Price,
			OrderVolume = order.Quantity,
			Balance = balance,
			Side = order.Side.ToSide(),
			OrderType = order.Type.EqualsIgnoreCase("MARKET")
				? OrderTypes.Market
				: OrderTypes.Limit,
			OrderState = orderState,
			TransactionId = transactionId ?? 0,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);

		if (order.EventType.EqualsIgnoreCase("trade") && order.TradeId != 0 &&
			order.TradeQuantity > 0)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				SecurityId = order.Symbol.ToStockSharp(),
				PortfolioName = PortfolioName,
				ServerTime = (order.TradeTime != 0 ? order.TradeTime : order.Timestamp)
					.FromUnix(false),
				OrderId = order.OrderId,
				TradeId = order.TradeId,
				TradePrice = order.TradePrice,
				TradeVolume = order.TradeQuantity,
				Commission = order.TradeFee,
				OriginalTransactionId = originalTransactionId,
			}, cancellationToken);
		}
	}

	private ValueTask SendOrderAsync(PoloniexOrder order, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var transactionId = order.ClientOrderId.ToClientTransactionId() ??
			TransactionIdGenerator.GetNextId();
		var balance = (order.Quantity - order.FilledQuantity).Max(0m);

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToStockSharp(),
			PortfolioName = PortfolioName,
			ServerTime = (order.UpdateTime != 0 ? order.UpdateTime : order.CreateTime)
				.FromUnix(false),
			OrderId = order.Id,
			OrderVolume = order.Quantity,
			Balance = balance,
			Side = order.Side.ToSide(),
			OrderPrice = order.Price,
			OrderType = order.Type.EqualsIgnoreCase("MARKET")
				? OrderTypes.Market
				: OrderTypes.Limit,
			OrderState = order.State.ToOrderState(),
			TransactionId = transactionId,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private ValueTask SendOwnTradeAsync(PoloniexOwnTrade trade, long originalTransactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = trade.Symbol.ToStockSharp(),
			PortfolioName = PortfolioName,
			ServerTime = trade.CreateTime.FromUnix(false),
			OrderId = trade.OrderId,
			TradeId = trade.Id,
			TradePrice = trade.Price,
			TradeVolume = trade.Quantity,
			Commission = trade.FeeAmount,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);

	private void EnsurePrivateReady()
	{
		if (_restClient is null || _privateSocket is null || _authenticator?.CanSign != true)
			throw new InvalidOperationException(
				"Poloniex private API requires an active connection with API credentials.");
	}
}
