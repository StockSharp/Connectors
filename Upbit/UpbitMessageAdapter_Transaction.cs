namespace StockSharp.Upbit;

partial class UpbitMessageAdapter
{
	private string PortfolioName => nameof(Upbit) + "_" + Key.ToId();

	private readonly SynchronizedDictionary<string, RefPair<long, decimal>> _orderInfo = new();

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var symbol = regMsg.SecurityId.ToSymbol();

		switch (regMsg.OrderType)
		{
			case null:
			case OrderTypes.Limit:
			case OrderTypes.Market:
				break;
			case OrderTypes.Conditional:
			{
				var condition = (UpbitOrderCondition)regMsg.Condition;

				if (!condition.IsWithdraw)
					throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));

				var withdrawId = await _httpClient.WithdrawAsync(symbol, regMsg.Volume, condition.WithdrawInfo, cancellationToken);

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
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		var isMarket = regMsg.OrderType == OrderTypes.Market;
		var price = regMsg.OrderType == OrderTypes.Market ? (decimal?)null : regMsg.Price;

		var orderId = await _httpClient.RegisterOrderAsync(symbol, regMsg.Side.ToNative(), price, regMsg.Volume, regMsg.TransactionId.To<string>(), cancellationToken);

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

		if (!isMarket)
			_orderInfo.Add(orderId, RefTuple.Create(regMsg.TransactionId, regMsg.Volume));

		await PortfolioLookupAsync(null, cancellationToken);
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
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		
		if (message != null)
		{
			if (!message.IsSubscribe)
				return;

			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = PortfolioName,
				BoardCode = BoardCodes.Upbit,
				OriginalTransactionId = message.TransactionId
			}, cancellationToken);

			await SendSubscriptionResultAsync(message, cancellationToken);
		}

		var balances = await _httpClient.GetBalancesAsync(cancellationToken);

		foreach (var balance in balances)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = PortfolioName,
				SecurityId = balance.Currency.ToStockSharp(),
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, (decimal?)balance.Value, true)
			.TryAdd(PositionChangeTypes.BlockedValue, (decimal?)balance.Locked, true)
			.TryAdd(PositionChangeTypes.AveragePrice, (decimal?)balance.AvgBuyPrice, true), cancellationToken);
		}

		_lastTimeBalanceCheck = CurrentTime;
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		
		if (message == null)
		{
			var portfolioRefresh = false;

			var orders = await _httpClient.GetOpenOrdersAsync(null, [], cancellationToken);

			var uuids = _orderInfo.Keys.ToIgnoreCaseSet();

			foreach (var order in orders)
			{
				uuids.Remove(order.Id);

				var balance = (decimal)order.RemainingVolume;

				var info = _orderInfo.TryGetValue(order.Id);

				if (info == null)
				{
					if (balance == 0)
						continue;

					var transId = TransactionIdGenerator.GetNextId();

					_orderInfo.Add(order.Id, RefTuple.Create(transId, balance));

					await ProcessOrderAsync(order, transId, 0, cancellationToken);
					portfolioRefresh = true;
				}
				else
				{
					var delta = info.Second - balance;

					if (delta == 0)
						continue;

					info.Second = balance;

					await ProcessOrderAsync(order, 0, info.First, cancellationToken);
					portfolioRefresh = true;
				}
			}

			if (uuids.Count > 0)
			{
				var doneOrders = await _httpClient.GetOpenOrdersAsync(null, [.. uuids], cancellationToken);

				foreach (var order in doneOrders)
				{
					await ProcessOrderAsync(order, 0, 0, cancellationToken);
				}

				foreach (var uuid in uuids)
					_orderInfo.Remove(uuid);

				portfolioRefresh = true;
			}

			if (portfolioRefresh)
				await PortfolioLookupAsync(null, cancellationToken);
		}
		else
		{
			if (!message.IsSubscribe)
				return;

			var orders = await _httpClient.GetOpenOrdersAsync(null, [], cancellationToken);

			foreach (var order in orders)
			{
				var transId = TransactionIdGenerator.GetNextId();
				_orderInfo.Add(order.Id, RefTuple.Create(transId, (decimal)order.RemainingVolume));
				await ProcessOrderAsync(order, transId, message.TransactionId, cancellationToken);
			}

			await SendSubscriptionResultAsync(message, cancellationToken);
		}
	}

	private async ValueTask ProcessOrderAsync(Order order, long transId, long origTransId, CancellationToken cancellationToken)
	{
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = transId != 0 ? order.CreatedAt : CurrentTime,
			SecurityId = order.Market.ToStockSharp(),
			TransactionId = transId,
			OriginalTransactionId = origTransId,
			OrderStringId = order.Id,
			OrderVolume = order.Volume.ToDecimal(),
			Balance = (decimal?)order.RemainingVolume,
			Side = order.Side.ToSide(),
			OrderPrice = order.Price?.ToDecimal() ?? 0,
			PortfolioName = PortfolioName,
			OrderState = order.State.ToOrderState(),
			Commission = (decimal?)order.PaidFee,
		}, cancellationToken);

		if (order.Trades != null)
		{
			foreach (var trade in order.Trades)
			{
				await SendOutMessageAsync(new ExecutionMessage
				{
					ServerTime = CurrentTime,
					TradeStringId = trade.Id,
					TradePrice = (decimal)trade.Price,
					TradeVolume = (decimal)trade.Volume,
					OriginSide = trade.Side.ToSide(),
				}, cancellationToken);
			}
		}
	}
}
