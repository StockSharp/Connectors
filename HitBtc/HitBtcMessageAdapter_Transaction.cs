namespace StockSharp.HitBtc;

partial class HitBtcMessageAdapter
{
	private string PortfolioName => nameof(HitBtc) + "_" + Key.ToId();

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var condition = (HitBtcOrderCondition)regMsg.Condition;

		switch (regMsg.OrderType)
		{
			case null:
			case OrderTypes.Limit:
			case OrderTypes.Market:
				break;
			case OrderTypes.Conditional:
			{
				if (!condition.IsWithdraw)
					break;

				var withdrawId = await _pusherClient.WithdrawAsync(regMsg.SecurityId.SecurityCode, regMsg.Volume, condition.WithdrawInfo, cancellationToken);

				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OrderStringId = withdrawId,
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

		var price = regMsg.OrderType == OrderTypes.Market ? (decimal?)null : regMsg.Price;

		await _pusherClient.PlaceOrderAsync(regMsg.TransactionId.ToClientOrderId(), regMsg.SecurityId.ToCurrency(), regMsg.Side.ToNative(),
			regMsg.OrderType.ToNative(condition?.StopPrice), price, regMsg.Volume, regMsg.TimeInForce.ToNative(regMsg.TillDate, regMsg.OrderType),
			condition?.StopPrice, regMsg.TillDate.EnsureToday(null), regMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		if (cancelMsg.OriginalTransactionId == 0)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.TransactionId));

		return _pusherClient.CancelOrderAsync(cancelMsg.OriginalTransactionId.ToClientOrderId(), cancelMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		return _pusherClient.ReplaceOrderAsync(replaceMsg.OriginalTransactionId.ToClientOrderId(),
			replaceMsg.TransactionId.ToClientOrderId(),
			replaceMsg.Price, replaceMsg.Volume, replaceMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage message, CancellationToken cancellationToken)
	{
		if (message == null)
			throw new ArgumentNullException(nameof(message));

		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);

		if (!message.IsSubscribe)
			return;

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = PortfolioName,
			BoardCode = BoardCodes.HitBtc,
			OriginalTransactionId = message.TransactionId,
		}, cancellationToken);

		if (!message.IsHistoryOnly())
			await _pusherClient.RequestBalanceAsync(message.TransactionId, cancellationToken);

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage message, CancellationToken cancellationToken)
	{
		if (message == null)
			throw new ArgumentNullException(nameof(message));

		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);

		if (!message.IsSubscribe)
			return;

		await _pusherClient.RequestActiveOrdersAsync(message.TransactionId, cancellationToken);

		if (!message.IsHistoryOnly())
			_pusherClient.SubscribeReports();

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private async ValueTask SessionOnBalanceChanged(long transactionId, Balance[] balances, CancellationToken cancellationToken)
	{
		foreach (var balance in balances)
		{
			//if (balance.Available == default && balance.Reserved == default)
			//	continue;

			//var secCode = balance.Currency;

			//if (!secCode.StartsWithIgnoreCase("USD") &&
			//    !secCode.StartsWithIgnoreCase("EUR"))
			//{
			//	secCode += "/USD";
			//}

			await SendOutMessageAsync(new PositionChangeMessage
			{
				ServerTime = CurrentTime,
				SecurityId = new SecurityId
				{
					SecurityCode = balance.Currency,
					BoardCode = BoardCodes.HitBtc,
				},
				PortfolioName = PortfolioName,
				OriginalTransactionId = transactionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, balance.Available.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.BlockedValue, balance.Reserved.ToDecimal(), true), cancellationToken);
		}
	}

	private async ValueTask SessionOnOrderChanged(long transactionId, Order order, CancellationToken cancellationToken)
	{
		if (transactionId == 0)
		{
			transactionId = order.ClientId.TryToTransactionId() ?? 0;

			if (transactionId == 0)
			{
				this.AddWarningLog("[OrderChanged] Non S# client order id.", order.ClientId);
				return;
			}
		}

		await ProcessOrderAsync(transactionId, order, 0, order.UpdatedAt ?? order.CreatedAt, cancellationToken);

		if (order.TradeId != null)
			await _pusherClient.RequestBalanceAsync(TransactionIdGenerator.GetNextId(), cancellationToken);
	}

	private async ValueTask SessionOnNewOrders(long originTransId, Order[] orders, CancellationToken cancellationToken)
	{
		foreach (var order in orders)
		{
			var transactionId = order.ClientId.TryToTransactionId();

			if (transactionId == null)
			{
				//transactionId = TransactionIdGenerator.GetNextId();
				this.AddWarningLog("[NewOrder] Non S# client order id.", order.ClientId);
				continue;
			}

			await ProcessOrderAsync(originTransId, order, transactionId.Value, order.CreatedAt, cancellationToken);
		}
	}

	private ValueTask ProcessOrderAsync(long originTransId, Order order, long transId, DateTime time, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = order.Symbol.ToStockSharp(),
			ServerTime = time,
			HasOrderInfo = true,
			//OrderId = order.Id,
			OrderStringId = order.ClientId,
			TransactionId = transId,
			OriginalTransactionId = originTransId,
			Balance = order.Quantity.ToDecimal() - (order.CumQuantity?.ToDecimal() ?? 0),
			OrderVolume = order.Quantity.ToDecimal(),
			OrderPrice = order.Price?.ToDecimal() ?? 0,
			OrderType = order.Type.ToOrderType(order.StopPrice, out var condition),
			Condition = condition,
			TimeInForce = order.TimeInForce.ToTimeInForce(),
			ExpiryDate = order.ExpireTime,
			PortfolioName = PortfolioName,
			OrderState = order.Status.ToOrderState(),
			Side = order.Side.ToSide(),

			TradeId = order.TradeId,
			TradePrice = order.TradePrice?.ToDecimal(),
			TradeVolume = order.TradeQuantity?.ToDecimal(),
			Commission = order.TradeFee?.ToDecimal(),
		}, cancellationToken);
	}

	private ValueTask SessionOnOrderError(long transactionId, string error, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = CurrentTime,
			OriginalTransactionId = transactionId,
			Error = new InvalidOperationException(error),
			OrderState = OrderStates.Failed,
		}, cancellationToken);
	}
}