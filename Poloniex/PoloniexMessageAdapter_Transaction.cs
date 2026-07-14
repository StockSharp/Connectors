namespace StockSharp.Poloniex;

partial class PoloniexMessageAdapter
{
	private string PortfolioName => nameof(Poloniex) + "_" + Key.ToId();

	private string GetPortfolioName(string wallet) => $"{PortfolioName}_{wallet}";

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		switch (regMsg.OrderType)
		{
			case null:
			case OrderTypes.Limit:
			case OrderTypes.Market:
				break;
			case OrderTypes.Conditional:
			{
				var condition = (PoloniexOrderCondition)regMsg.Condition;

				if (!condition.IsWithdraw)
					throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));

				await _httpClient.WithdrawAsync(regMsg.SecurityId.SecurityCode, regMsg.Volume, condition.WithdrawInfo, cancellationToken);

				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					ServerTime = CurrentTime,
					OriginalTransactionId = regMsg.TransactionId,
					OrderState = OrderStates.Done,
					HasOrderInfo = true,
				}, cancellationToken);
				return;
			}
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		var orderId = await _httpClient.NewOrderAsync(regMsg.TransactionId, regMsg.SecurityId.ToCurrency(), regMsg.Side.ToNative(), regMsg.Price, regMsg.Volume,
			regMsg.TimeInForce == TimeInForce.MatchOrCancel, regMsg.TimeInForce == TimeInForce.CancelBalance, regMsg.PostOnly, cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OrderId = orderId,
			ServerTime = CurrentTime,
			OriginalTransactionId = regMsg.TransactionId,
			OrderState = OrderStates.Active,
			HasOrderInfo = true,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		await _httpClient.CancelOrderAsync(cancelMsg.OriginalTransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		await _httpClient.CancelAllOrdersAsync(cancelMsg.SecurityId.ToCurrency(), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		if (replaceMsg.OldOrderId == null)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(replaceMsg.OriginalTransactionId));

		var volume = replaceMsg.Volume == 0 ? (decimal?)null : replaceMsg.Volume;
		await _httpClient.MoveOrderAsync(replaceMsg.TransactionId, replaceMsg.OldOrderId.Value, replaceMsg.Price, volume,
			replaceMsg.TimeInForce == TimeInForce.MatchOrCancel, replaceMsg.TimeInForce == TimeInForce.CancelBalance, replaceMsg.PostOnly, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		if (!lookupMsg.IsSubscribe)
			return;

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = PortfolioName,
			BoardCode = BoardCodes.Poloniex,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);

		var balances = await _httpClient.GetCompleteBalancesAsync(cancellationToken);

		foreach (var pair in balances)
		{
			var balance = pair.Value;

			var current = (decimal)balance.Available;
			var blocked = (decimal)balance.OnOrders;

			if (current == 0 && blocked == 0)
				continue;

			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = PortfolioName,
				SecurityId = new SecurityId
				{
					SecurityCode = pair.Key,
					BoardCode = BoardCodes.Poloniex,
				},
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, current, true)
			.TryAdd(PositionChangeTypes.BlockedValue, blocked, true), cancellationToken);
		}

		if (!lookupMsg.IsHistoryOnly())
			await _pusherClient.SubscribeAccount(cancellationToken);

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);

		if (!statusMsg.IsSubscribe)
			return;

		var orders = await _httpClient.GetOpenOrdersAsync(string.Empty, cancellationToken);

		foreach (var pair in orders)
		{
			var secId = pair.Key.ToStockSharp();

			foreach (var order in pair.Value)
			{
				var transId = order.ClientOrderId;

				if (transId == 0)
					transId = TransactionIdGenerator.GetNextId();

				var balance = (decimal)order.Amount;

				if (balance <= 0)
					continue;

				var volume = (decimal)order.StartingAmount;

				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					HasOrderInfo = true,
					ServerTime = CurrentTime,
					SecurityId = secId,
					TransactionId = transId,
					OriginalTransactionId = statusMsg.TransactionId,
					OrderId = order.OrderNumber,
					OrderVolume = volume,
					Balance = balance,
					Side = order.Type.ToSide(),
					OrderPrice = (decimal)order.Rate,
					PortfolioName = PortfolioName,
					OrderState = OrderStates.Active,
				}, cancellationToken);

				if (volume != balance)
				{
					var trades = await _httpClient.GetOrderTradesAsync(order.OrderNumber, cancellationToken);

					foreach (var trade in trades)
					{
						await SendOutMessageAsync(new ExecutionMessage
						{
							DataTypeEx = DataType.Transactions,
							ServerTime = trade.Date.ToDto(),
							OrderId = order.OrderNumber,
							TradeId = trade.TradeId,
							TradePrice = (decimal)trade.Rate,
							TradeVolume = (decimal)trade.Amount,
						}, cancellationToken);
					}
				}
			}
		}

		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private ValueTask SessionOnNewOwnTrade(SocketOwnTrade trade, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			ServerTime = trade.Date,
			OrderId = trade.OrderNumber,
			OriginalTransactionId = trade.ClientOrderId,
			TradeId = trade.TradeId,
			TradePrice = (decimal)trade.Rate,
			TradeVolume = (decimal)trade.Amount,
			Commission = (decimal)trade.FeeTotal,
		}, cancellationToken);
	}

	private ValueTask SessionOnOrderKilled(SocketOrderKill order, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = CurrentTime,
			OriginalTransactionId = order.ClientOrderId,
			OrderId = order.OrderNumber,
			OrderState = OrderStates.Done,
		}, cancellationToken);
	}

	private ValueTask SessionOnOrderChanged(SocketOrderUpdate order, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = CurrentTime,
			OriginalTransactionId = order.ClientOrderId,
			OrderId = order.OrderNumber,
			Balance = (decimal)order.NewAmount,
			OrderState = OrderStates.Active,
		}, cancellationToken);
	}

	private ValueTask SessionOnNewOrder(SocketOrderLimit order, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = order.Date,
			SecurityId = EnsureSecId(order.PairId),
			OriginalTransactionId = order.ClientOrderId,
			OrderId = order.OrderNumber,
			OrderVolume = (decimal)order.OriginalAmount,
			Balance = (decimal)order.Amount,
			Side = order.Type.ToSide(),
			OrderPrice = (decimal)order.Rate,
			OrderState = OrderStates.Active,
		}, cancellationToken);
	}

	private ValueTask SessionOnOrderPending(SocketOrderPending order, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = CurrentTime,
			SecurityId = EnsureSecId(order.PairId),
			OriginalTransactionId = order.ClientOrderId,
			OrderId = order.OrderNumber,
			OrderVolume = (decimal)order.Amount,
			Side = order.Type.ToSide(),
			OrderPrice = (decimal)order.Rate,
			OrderState = OrderStates.Pending,
		}, cancellationToken);
	}

	private ValueTask SessionOnBalanceChanged(SocketBalance balance, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(balance.Wallet),
			SecurityId = EnsureSecId(balance.CurrencyId, true),
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, (decimal)balance.Amount, true), cancellationToken);
	}
}