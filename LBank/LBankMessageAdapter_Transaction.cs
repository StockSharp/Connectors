namespace StockSharp.LBank;

partial class LBankMessageAdapter
{
	private string PortfolioName => nameof(LBank) + "_" + Key.ToId();

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var currency = regMsg.SecurityId.ToCurrency();

		switch (regMsg.OrderType)
		{
			case null:
			case OrderTypes.Limit:
			case OrderTypes.Market:
				break;
			case OrderTypes.Conditional:
			{
				var condition = (LBankOrderCondition)regMsg.Condition;

				if (!condition.IsWithdraw)
					throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));

				var result = await _httpClient.WithdrawAsync(currency, regMsg.Volume, condition.WithdrawInfo, cancellationToken);

				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OrderId = result.Item1,
					ServerTime = CurrentTime,
					OriginalTransactionId = regMsg.TransactionId,
					OrderState = OrderStates.Done,
					HasOrderInfo = true,
					Commission = result.Item2,
				}, cancellationToken);

				return;
			}
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		var isMarket = regMsg.OrderType == OrderTypes.Market;
		var price = regMsg.OrderType == OrderTypes.Market ? (decimal?)null : regMsg.Price;

		var orderId = await _httpClient.RegisterOrderAsync(regMsg.TransactionId, currency, regMsg.Side.ToNative(), price, regMsg.Volume, cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OrderStringId = orderId,
			ServerTime = CurrentTime,
			OriginalTransactionId = regMsg.TransactionId,
			OrderState = isMarket ? OrderStates.Done : OrderStates.Active,
			Balance = isMarket ? 0 : null,
			HasOrderInfo = true,
		}, cancellationToken);

		await PortfolioLookupAsync(null, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		if (cancelMsg.OrderStringId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		await _httpClient.CancelOrderAsync(cancelMsg.SecurityId.ToCurrency(), cancelMsg.OrderStringId, cancellationToken);

		await PortfolioLookupAsync(null, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		
		if (message != null)
		{
			if (!message.IsSubscribe)
				return;

			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = PortfolioName,
				BoardCode = BoardCodes.LBank,
				OriginalTransactionId = message.TransactionId
			}, cancellationToken);

			await SendSubscriptionResultAsync(message, cancellationToken);
		}

		var balance = await _httpClient.GetUserInfoAsync(cancellationToken);

		var free = balance.Item2 ?? new Dictionary<string, double>();
		var freezed = balance.Item1 ?? new Dictionary<string, double>();

		foreach (var coin in freezed.Keys.Union(free.Keys))
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = PortfolioName,
				SecurityId = coin.ToStockSharp(),
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, free.TryGetValue2(coin)?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.BlockedValue, freezed.TryGetValue2(coin)?.ToDecimal(), true), cancellationToken);
		}

		_lastTimeBalanceCheck = CurrentTime;
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		
		if (message == null)
			throw new ArgumentNullException(nameof(message));

		if (!message.IsSubscribe)
		{
			await _pusherClient.SubscribeOrders(false, _authKey, cancellationToken);
			return;
		}

		foreach (var order in await _httpClient.GetOrdersAsync("all", 1, cancellationToken))
			await ProcessOrderAsync(order, message.TransactionId, cancellationToken);

		if (!message.IsHistoryOnly())
			await _pusherClient.SubscribeOrders(true, _authKey, cancellationToken);

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private ValueTask ProcessOrderAsync(Order order, long origTransId, CancellationToken cancellationToken)
	{
		if (!long.TryParse(order.CustomerId, out var transId))
			transId = TransactionIdGenerator.GetNextId();

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = order.CreatedTimestamp,
			SecurityId = order.Symbol.ToStockSharp(),
			TransactionId = transId,
			OriginalTransactionId = origTransId,
			OrderStringId = order.Id,
			OrderVolume = order.Volume.ToDecimal(),
			Balance = order.GetBalance(),
			Side = order.Type.ToSide(),
			OrderPrice = order.Price?.ToDecimal() ?? 0,
			PortfolioName = PortfolioName,
			OrderState = order.Status.ToOrderState(),
		}, cancellationToken);
	}

	private ValueTask SessionOnOrderUpdated(string pair, DateTime time, SocketOrder order, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = order.UpdateTime,
			SecurityId = pair.ToStockSharp(),
			//OriginalTransactionId = origTransId,
			OrderStringId = order.Id,
			OrderVolume = (decimal)order.Amount,
			//Balance = order.GetBalance(),
			//Side = order.Type.ToSide(),
			//OrderPrice = order.Price?.ToDecimal() ?? 0,
			PortfolioName = PortfolioName,
			OrderState = order.Status.ToOrderState(),
		}, cancellationToken);
	}
}
