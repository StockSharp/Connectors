namespace StockSharp.Bithumb;

public partial class BithumbMessageAdapter
{
	private readonly Dictionary<string, RefPair<decimal, long>> _orderInfo =
		new(StringComparer.OrdinalIgnoreCase);

	private string PortfolioName => nameof(Bithumb) + "_" + Key.ToId();

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		switch (regMsg.OrderType)
		{
			case null:
			case OrderTypes.Limit:
			case OrderTypes.Market:
				break;

			case OrderTypes.Conditional:
			{
				var condition = (BithumbOrderCondition)regMsg.Condition;

				if (!condition.IsWithdraw)
					throw new NotSupportedException(
						LocalizedStrings.OrderUnsupportedType.Put(
							regMsg.OrderType, regMsg.TransactionId));

				var currency = regMsg.SecurityId.SecurityCode.Split('/')[0];
				var withdrawId = await _httpClient.WithdrawAsync(
					currency, regMsg.Volume,
					condition.WithdrawInfo, cancellationToken);

				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OrderStringId = withdrawId,
					ServerTime = CurrentTime,
					OriginalTransactionId = regMsg.TransactionId,
					OrderState = OrderStates.Done,
					HasOrderInfo = true,
				}, cancellationToken);

				await PortfolioLookupAsync(null, cancellationToken);
				return;
			}

			default:
				throw new NotSupportedException(
					LocalizedStrings.OrderUnsupportedType.Put(
						regMsg.OrderType, regMsg.TransactionId));
		}

		var price = regMsg.OrderType == OrderTypes.Market ? (decimal?)null : regMsg.Price;
		var orderId = await _httpClient.RegisterOrderAsync(
			regMsg.SecurityId.ToSymbol(), regMsg.Side, price, regMsg.Volume,
			regMsg.TransactionId.ToString(), cancellationToken);

		_orderInfo.Add(orderId, RefTuple.Create(regMsg.Volume, regMsg.TransactionId));

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OrderStringId = orderId,
			ServerTime = CurrentTime,
			OriginalTransactionId = regMsg.TransactionId,
			OrderState = OrderStates.Active,
			Balance = regMsg.Volume,
			HasOrderInfo = true,
		}, cancellationToken);

		await PortfolioLookupAsync(null, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		if (cancelMsg.OrderStringId.IsEmpty())
			throw new InvalidOperationException(
				LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		await _httpClient.CancelOrderAsync(cancelMsg.OrderStringId, cancellationToken);

		_orderInfo.TryGetValue(cancelMsg.OrderStringId, out var info);
		_orderInfo.Remove(cancelMsg.OrderStringId);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OrderStringId = cancelMsg.OrderStringId,
			ServerTime = CurrentTime,
			OriginalTransactionId = cancelMsg.TransactionId,
			OrderState = OrderStates.Done,
			Balance = info?.First,
			HasOrderInfo = true,
		}, cancellationToken);

		await PortfolioLookupAsync(null, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage message,
		CancellationToken cancellationToken)
	{
		if (message != null)
		{
			await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);

			if (!message.IsSubscribe)
				return;

			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = PortfolioName,
				BoardCode = BoardCodes.Bithumb,
				OriginalTransactionId = message.TransactionId,
			}, cancellationToken);
		}

		foreach (var balance in await _httpClient.GetBalancesAsync(cancellationToken))
		{
			var available = balance.Value.ToDecimal();
			var blocked = balance.Locked.ToDecimal();

			await SendOutMessageAsync(new PositionChangeMessage
			{
				SecurityId = new SecurityId
				{
					SecurityCode = balance.Currency,
					BoardCode = BoardCodes.Bithumb,
				},
				PortfolioName = PortfolioName,
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, available, true)
			.TryAdd(PositionChangeTypes.BlockedValue, blocked, true)
			.TryAdd(PositionChangeTypes.AveragePrice, balance.AveragePrice.ToDecimal(), true),
				cancellationToken);
		}

		_lastTimeBalanceCheck = CurrentTime;

		if (message != null)
			await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage message,
		CancellationToken cancellationToken)
	{
		if (message != null)
		{
			await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);

			if (!message.IsSubscribe)
				return;
		}

		var pendingOrders = await _httpClient.GetPendingOrdersAsync(cancellationToken);

		if (message != null)
		{
			foreach (var order in pendingOrders)
			{
				var transactionId = TransactionIdGenerator.GetNextId();
				var balance = order.RemainingVolume.ToDecimal() ?? 0;

				if (!_orderInfo.ContainsKey(order.Id))
					_orderInfo.Add(order.Id, RefTuple.Create(balance, transactionId));

				await ProcessOrderAsync(order, transactionId, message.TransactionId,
					cancellationToken);
			}

			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}

		var portfolioRefresh = false;
		var pendingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var order in pendingOrders)
		{
			pendingIds.Add(order.Id);

			var balance = order.RemainingVolume.ToDecimal() ?? 0;
			var info = _orderInfo.TryGetValue(order.Id);

			if (info is null)
			{
				var transactionId = TransactionIdGenerator.GetNextId();
				_orderInfo.Add(order.Id, RefTuple.Create(balance, transactionId));

				await ProcessOrderAsync(order, transactionId, 0, cancellationToken);
				portfolioRefresh = true;
				continue;
			}

			var delta = info.First - balance;

			if (delta <= 0)
				continue;

			info.First = balance;

			await ProcessOrderAsync(order, 0, info.Second, cancellationToken);
			await ProcessTradeAsync(order, info.Second, delta, cancellationToken);
			portfolioRefresh = true;
		}

		var completedIds = _orderInfo.Keys
			.Where(id => !pendingIds.Contains(id))
			.ToArray();

		foreach (var idBatch in completedIds.Chunk(100))
		{
			foreach (var order in await _httpClient.GetOrdersAsync(idBatch, cancellationToken))
			{
				var info = _orderInfo.TryGetValue(order.Id);

				if (info is null)
					continue;

				var balance = order.RemainingVolume.ToDecimal() ?? 0;
				var delta = info.First - balance;

				await ProcessOrderAsync(order, 0, info.Second, cancellationToken);

				if (delta > 0)
					await ProcessTradeAsync(order, info.Second, delta, cancellationToken);

				if (order.State.ToOrderState() == OrderStates.Done)
					_orderInfo.Remove(order.Id);
				else
					info.First = balance;

				portfolioRefresh = true;
			}
		}

		if (portfolioRefresh)
			await PortfolioLookupAsync(null, cancellationToken);
	}

	private ValueTask ProcessOrderAsync(Order order, long transactionId,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = order.CreatedAt == default ? CurrentTime : order.CreatedAt.UtcDateTime,
			SecurityId = order.Market.ToStockSharp(),
			TransactionId = transactionId,
			OriginalTransactionId = originalTransactionId,
			OrderStringId = order.Id,
			OrderVolume = order.Volume.ToDecimal(),
			Balance = order.RemainingVolume.ToDecimal(),
			Side = order.Side.ToSide(),
			OrderPrice = order.Price.ToDecimal() ?? 0,
			PortfolioName = PortfolioName,
			Commission = order.PaidFee.ToDecimal(),
			OrderState = order.State.ToOrderState(),
		}, cancellationToken);
	}

	private ValueTask ProcessTradeAsync(Order order, long originalTransactionId,
		decimal volume, CancellationToken cancellationToken)
	{
		var executedVolume = order.ExecutedVolume.ToDecimal();
		var executedFunds = order.ExecutedFunds.ToDecimal();
		var price = executedVolume > 0 && executedFunds != null
			? executedFunds.Value / executedVolume.Value
			: order.Price.ToDecimal() ?? 0;

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = order.Market.ToStockSharp(),
			OrderStringId = order.Id,
			OriginalTransactionId = originalTransactionId,
			TradeStringId = $"{order.Id}:{executedVolume}",
			TradePrice = price,
			TradeVolume = volume,
			ServerTime = CurrentTime,
			PortfolioName = PortfolioName,
		}, cancellationToken);
	}
}
