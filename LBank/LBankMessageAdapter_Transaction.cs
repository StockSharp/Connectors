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

				var (withdrawId, fee) = await _httpClient.WithdrawAsync(
					currency,
					regMsg.Volume,
					condition.WithdrawInfo,
					cancellationToken);

				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OrderId = withdrawId,
					ServerTime = CurrentTime,
					OriginalTransactionId = regMsg.TransactionId,
					OrderState = OrderStates.Done,
					HasOrderInfo = true,
					Commission = fee,
				}, cancellationToken);

				return;
			}

			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		var isMarket = regMsg.OrderType == OrderTypes.Market;
		var nativeType = regMsg.Side.ToNative() + (isMarket ? "_market" : string.Empty);
		var price = isMarket ? (decimal?)null : regMsg.Price;

		var orderId = await _httpClient.RegisterOrderAsync(
			regMsg.TransactionId,
			currency,
			nativeType,
			price,
			regMsg.Volume,
			cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OrderStringId = orderId,
			ServerTime = CurrentTime,
			OriginalTransactionId = regMsg.TransactionId,
			OrderState = OrderStates.Active,
			Balance = isMarket ? null : regMsg.Volume,
			HasOrderInfo = true,
		}, cancellationToken);

		await PortfolioLookupAsync(null, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		if (cancelMsg.OrderStringId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		await _httpClient.CancelOrderAsync(
			cancelMsg.SecurityId.ToCurrency(),
			cancelMsg.OrderStringId,
			cancellationToken);

		await PortfolioLookupAsync(null, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage message, CancellationToken cancellationToken)
	{
		if (message != null)
		{
			await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);

			if (!message.IsSubscribe)
			{
				await _pusherClient.SubscribeBalances(false, _authKey, cancellationToken);
				return;
			}

			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = PortfolioName,
				BoardCode = BoardCodes.LBank,
				OriginalTransactionId = message.TransactionId,
			}, cancellationToken);
		}

		var account = await _httpClient.GetUserInfoAsync(cancellationToken);

		foreach (var balance in account?.Balances ?? [])
			await ProcessBalanceAsync(balance.Asset, balance.Free, balance.Locked, CurrentTime, cancellationToken);

		_lastTimeBalanceCheck = CurrentTime;

		if (message == null)
			return;

		if (!message.IsHistoryOnly())
			await _pusherClient.SubscribeBalances(true, _authKey, cancellationToken);

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage message, CancellationToken cancellationToken)
	{
		if (message == null)
			return;

		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);

		if (!message.IsSubscribe)
		{
			await _pusherClient.SubscribeOrders(false, _authKey, cancellationToken);
			return;
		}

		var symbols = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

		if (!message.SecurityId.SecurityCode.IsEmpty())
			symbols.Add(message.SecurityId.ToCurrency());

		foreach (var securityId in message.SecurityIds)
		{
			if (!securityId.SecurityCode.IsEmpty())
				symbols.Add(securityId.ToCurrency());
		}

		foreach (var symbol in symbols)
		{
			for (var pageNumber = 1; ; pageNumber++)
			{
				var page = await _httpClient.GetOrdersAsync(symbol, pageNumber, cancellationToken);
				var orders = page?.Orders ?? [];

				foreach (var order in orders)
					await ProcessOrderAsync(order, message.TransactionId, cancellationToken);

				if (orders.Length == 0 || pageNumber >= page.TotalPages)
					break;
			}
		}

		if (!message.IsHistoryOnly())
			await _pusherClient.SubscribeOrders(true, _authKey, cancellationToken);

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private ValueTask ProcessOrderAsync(Order order, long originalTransactionId, CancellationToken cancellationToken)
	{
		if (!long.TryParse(order.CustomerId, out var transactionId))
			transactionId = TransactionIdGenerator.GetNextId();

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = order.CreatedTimestamp == default ? CurrentTime : order.CreatedTimestamp,
			SecurityId = order.Symbol.ToStockSharp(),
			TransactionId = transactionId,
			OriginalTransactionId = originalTransactionId,
			OrderStringId = order.Id,
			OrderVolume = order.Volume,
			Balance = order.GetBalance(),
			Side = order.Type.ToSide(),
			OrderPrice = order.Price ?? 0,
			AveragePrice = order.AvgPrice,
			PortfolioName = PortfolioName,
			OrderState = order.Status.ToOrderState(),
			OrderType = order.Type.ToOrderType(),
		}, cancellationToken);
	}

	private ValueTask SessionOnOrderUpdated(string pair, DateTime time, SocketOrder order, CancellationToken cancellationToken)
	{
		long.TryParse(order.CustomerId, out var transactionId);

		var volume = order.OrderAmount > 0 ? order.OrderAmount : order.Amount;
		var balance = order.RemainingAmount > 0 || order.Status is 0 or 1
			? order.RemainingAmount
			: Math.Max(0, volume - order.AccumulatedAmount);
		var symbol = order.Symbol.IsEmpty() ? pair : order.Symbol;

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = order.UpdateTime == default ? time : order.UpdateTime,
			SecurityId = symbol.ToStockSharp(),
			OriginalTransactionId = transactionId,
			OrderStringId = order.Id,
			OrderVolume = volume,
			Balance = balance,
			Side = order.Type.ToSide(),
			OrderPrice = order.OrderPrice ?? order.Price ?? 0,
			AveragePrice = order.AveragePrice,
			PortfolioName = PortfolioName,
			OrderState = order.Status.ToOrderState(),
			OrderType = order.Type.ToOrderType(),
		}, cancellationToken);
	}

	private ValueTask SessionOnBalanceUpdated(SocketBalance balance, CancellationToken cancellationToken)
		=> ProcessBalanceAsync(
			balance.Asset,
			balance.Free,
			balance.Locked,
			balance.Timestamp == default ? CurrentTime : balance.Timestamp,
			cancellationToken);

	private ValueTask ProcessBalanceAsync(
		string asset,
		decimal free,
		decimal locked,
		DateTime serverTime,
		CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = PortfolioName,
			SecurityId = asset.ToStockSharp(),
			ServerTime = serverTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, free, true)
		.TryAdd(PositionChangeTypes.BlockedValue, locked, true), cancellationToken);
	}
}
