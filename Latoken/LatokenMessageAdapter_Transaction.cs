namespace StockSharp.LATOKEN;

partial class LatokenMessageAdapter
{
	private string PortfolioName => nameof(LATOKEN) + "_" + Key.ToId();

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
				var condition = (LatokenOrderCondition)regMsg.Condition;

				if (!condition.IsWithdraw)
					throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));

				var withdrawId = await _httpClient.WithdrawAsync(await GetCurrencyId(regMsg.SecurityId.SecurityCode, cancellationToken), regMsg.Volume, condition.WithdrawInfo, cancellationToken);

				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					ServerTime = CurrentTime,
					OriginalTransactionId = regMsg.TransactionId,
					OrderState = OrderStates.Done,
					HasOrderInfo = true,
					OrderStringId = withdrawId,
				}, cancellationToken);
				
				return;
			}
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		var (code, board) = await GetCurrenciesId(regMsg.SecurityId, cancellationToken);

		var price = regMsg.OrderType == OrderTypes.Market ? (decimal?)null : regMsg.Price;

		var orderId = await _httpClient.RegisterOrderAsync(regMsg.TransactionId, code, board, regMsg.Side.ToNative(), regMsg.TimeInForce.ToNative(), regMsg.OrderType.ToNative(), price, regMsg.Volume, cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OrderStringId = orderId,
			ServerTime = CurrentTime,
			OriginalTransactionId = regMsg.TransactionId,
			HasOrderInfo = true,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		if (cancelMsg.OrderStringId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		await _httpClient.CancelOrderAsync(cancelMsg.OrderStringId, cancellationToken);

		await OrderStatusAsync(null, cancellationToken);
		await PortfolioLookupAsync(null, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage message, CancellationToken cancellationToken)
	{
		if (message == null)
		{
			foreach (var balance in await _httpClient.GetBalancesAsync(cancellationToken))
			{
				await ProcessBalance(balance, cancellationToken);
			}
			return;
		}

		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);

		if (!message.IsSubscribe)
		{
			await _pusherClient.UnSubscribeAccounts(cancellationToken);
			return;
		}

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = PortfolioName,
			BoardCode = BoardCodes.Latoken,
			OriginalTransactionId = message.TransactionId
		}, cancellationToken);

		foreach (var balance in await _httpClient.GetBalancesAsync(cancellationToken))
		{
			await ProcessBalance(balance, cancellationToken);
		}

		if (!message.IsHistoryOnly())
			await _pusherClient.SubscribeAccounts(cancellationToken);

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage message, CancellationToken cancellationToken)
	{
		if (message == null)
		{
			foreach (var order in await _httpClient.GetOrdersAsync(cancellationToken))
			{
				await ProcessOrder(order, 0, cancellationToken);
			}

			return;
		}

		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);

		if (!message.IsSubscribe)
		{
			await _pusherClient.UnSubscribeOrders(cancellationToken);
			return;
		}

		var orders = await _httpClient.GetOrdersAsync(cancellationToken);

		foreach (var order in orders)
		{
			await ProcessOrder(order, message.TransactionId, cancellationToken);
		}

		if (!message.IsHistoryOnly())
			await _pusherClient.SubscribeOrders(cancellationToken);

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private ValueTask SessionOnBalanceChanged(Balance balance, CancellationToken cancellationToken)
	{
		return ProcessBalance(balance, cancellationToken);
	}

	private ValueTask SessionOnOrderChanged(Order order, CancellationToken cancellationToken)
	{
		return ProcessOrder(order, 0, cancellationToken);
	}

	private async ValueTask ProcessOrder(Order order, long origTransId, CancellationToken cancellationToken)
	{
		if (!long.TryParse(order.ClientOrderId, out var transId))
			transId = TransactionIdGenerator.GetNextId();

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = order.Timestamp,
			SecurityId = await GetSecurityId(order, cancellationToken),
			TransactionId = origTransId == 0 ? 0 : transId,
			OriginalTransactionId = origTransId == 0 ? transId : origTransId,
			OrderStringId = order.Id,
			OrderVolume = (decimal)order.Quantity,
			Balance = (decimal)(order.Quantity - order.Filled),
			Side = order.Side.ToSide(),
			TimeInForce = order.Condition.ToTif(),
			OrderPrice = (decimal)order.Price,
			PortfolioName = PortfolioName,
			OrderState = order.Status.ToOrderState(),
			OrderType = order.Type.ToOrderType(),
		}, cancellationToken);
	}

	private async ValueTask ProcessBalance(Balance balance, CancellationToken cancellationToken)
	{
		await SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = PortfolioName,
			SecurityId = (await GetCurrencyCode(balance.Currency, cancellationToken)).ToStockSharp(),
			ServerTime = balance.Timestamp,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, (decimal)balance.Available, true)
		.TryAdd(PositionChangeTypes.BlockedValue, (decimal)balance.Blocked, true), cancellationToken);
	}
}