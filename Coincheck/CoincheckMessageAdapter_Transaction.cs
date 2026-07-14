namespace StockSharp.Coincheck;

partial class CoincheckMessageAdapter
{
	private readonly Dictionary<long, RefPair<long, decimal>> _orderInfo = [];
	private string PortfolioName => nameof(Coincheck) + "_" + Key.ToId();

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
				var condition = (CoincheckOrderCondition)regMsg.Condition;

				if (!condition.IsWithdraw)
					throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));

				var (withdrawId, fee) = await _httpClient.WithdrawAsync(regMsg.SecurityId.SecurityCode, regMsg.Volume, condition.WithdrawInfo, cancellationToken);

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

				await PortfolioLookupAsync(null, cancellationToken);
				return;
			}
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		var isMarket = regMsg.OrderType == OrderTypes.Market;
		var price = isMarket ? (decimal?)null : regMsg.Price;

		var result = await _httpClient.RegisterOrderAsync(regMsg.SecurityId.ToCurrency(), regMsg.Side, price, regMsg.Volume, null, regMsg.TransactionId, cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OrderId = result.Id,
			ServerTime = result.CreatedAt.ToDto(),
			OriginalTransactionId = regMsg.TransactionId,
			OrderState = isMarket ? OrderStates.Done : OrderStates.Active,
			Balance = isMarket ? 0 : null,
			HasOrderInfo = true,
		}, cancellationToken);

		if (isMarket)
		{
		}
		else
		{
			_orderInfo.Add(result.Id, RefTuple.Create(regMsg.TransactionId, regMsg.Volume));
		}

		await PortfolioLookupAsync(null, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		if (cancelMsg.OrderId == null)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		await _httpClient.CancelOrderAsync(cancelMsg.OrderId.Value, cancellationToken);

		await OrderStatusAsync(null, cancellationToken);
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
				BoardCode = BoardCodes.Coincheck,
				OriginalTransactionId = message.TransactionId
			}, cancellationToken);

			await SendSubscriptionResultAsync(message, cancellationToken);
		}

		var balance = await _httpClient.GetBalanceAsync(cancellationToken);

		await ProcessPositionAsync("btc", balance.Btc, balance.BtcReserved, cancellationToken);
		await ProcessPositionAsync("jpy", balance.Jpy, balance.JpyReserved, cancellationToken);

		_lastTimeBalanceCheck = CurrentTime;
	}

	private ValueTask ProcessPositionAsync(string currency, double currValue, double reserved, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new PositionChangeMessage
		{
			SecurityId = new SecurityId
			{
				SecurityCode = currency,
				BoardCode = BoardCodes.Coincheck,
			},
			PortfolioName = PortfolioName,
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, currValue.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.BlockedValue, reserved.ToDecimal(), true), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		
		if (message == null)
		{
			var portfolioRefresh = false;

			var orderIds = _orderInfo.Keys.ToHashSet();

			var orders = await _httpClient.GetOpenOrdersAsync(cancellationToken);

			foreach (var order in orders)
			{
				var info = _orderInfo.TryGetValue(order.Id);

				if (info == null)
				{
					var transId = TransactionIdGenerator.GetNextId();

					var amount = (decimal)(order.PendingAmount ?? order.PendingMarketBuyAmount);

					_orderInfo.Add(order.Id, RefTuple.Create(transId, amount));

					await ProcessOrderAsync(order, transId, 0, OrderStates.Active, cancellationToken);

					portfolioRefresh = true;
				}
				else
				{
					orderIds.Remove(order.Id);

					portfolioRefresh = true;
				}
			}

			foreach (var orderId in orderIds)
			{
				var info = _orderInfo.GetAndRemove(orderId);

				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					HasOrderInfo = true,
					OriginalTransactionId = info.First,
					Balance = 0,
					OrderState = OrderStates.Done,
				}, cancellationToken);

				portfolioRefresh = true;
			}

			if (portfolioRefresh)
				await PortfolioLookupAsync(null, cancellationToken);
		}
		else
		{
			if (!message.IsSubscribe)
				return;

			var orders = await _httpClient.GetOpenOrdersAsync(cancellationToken);

			foreach (var order in orders)
			{
				var transId = TransactionIdGenerator.GetNextId();

				var amount = (decimal)(order.PendingAmount ?? order.PendingMarketBuyAmount);

				_orderInfo.Add(order.Id, RefTuple.Create(transId, amount));

				await ProcessOrderAsync(order, transId, message.TransactionId, OrderStates.Active, cancellationToken);
			}

			await SendSubscriptionResultAsync(message, cancellationToken);
		}
	}

	private ValueTask ProcessOrderAsync(Order order, long transId, long origTransId, OrderStates state, CancellationToken cancellationToken)
	{
		var amount = order.PendingAmount ?? order.PendingMarketBuyAmount;

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = transId != 0 ? order.CreatedAt.ToDto() : CurrentTime,
			SecurityId = order.Pair.ToStockSharp(),
			TransactionId = transId,
			OriginalTransactionId = origTransId,
			OrderId = order.Id,
			OrderVolume = amount?.ToDecimal(),
			Balance = amount?.ToDecimal(),
			Side = order.Type.ToSide(),
			OrderPrice = order.Price?.ToDecimal() ?? 0,
			PortfolioName = PortfolioName,
			OrderState = state,
		}, cancellationToken);
	}
}
