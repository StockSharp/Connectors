namespace StockSharp.Zaif;

partial class ZaifMessageAdapter
{
	private string PortfolioName => nameof(Zaif) + "_" + Key.ToId();

	private readonly SynchronizedDictionary<long, RefTriple<long, decimal, string>> _orderInfo = new();

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
				var condition = (ZaifOrderCondition)regMsg.Condition;

				if (!condition.IsWithdraw)
					throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));

				await _httpClient.WithdrawAsync(currency, regMsg.Volume, condition.WithdrawInfo, cancellationToken);

				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					//OrderId = withdrawId,
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

		var price = regMsg.OrderType == OrderTypes.Market ? (decimal?)null : regMsg.Price;

		var orderId = await _httpClient.RegisterOrderAsync(currency, regMsg.Side.ToNative(), price, regMsg.Volume, null, cancellationToken);

		if (orderId > 0)
		{
			_orderInfo.Add(orderId, RefTuple.Create(regMsg.TransactionId, regMsg.Volume, currency));

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
		else
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				ServerTime = CurrentTime,
				OriginalTransactionId = regMsg.TransactionId,
				OrderState = OrderStates.Done,
				Balance = 0,
				HasOrderInfo = true,
			}, cancellationToken);
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
				BoardCode = BoardCodes.Zaif,
				OriginalTransactionId = message.TransactionId
			}, cancellationToken);

			await SendSubscriptionResultAsync(message, cancellationToken);
		}

		var account = await _httpClient.GetBalancesAsync(cancellationToken);

		foreach (var pair in account.Deposit)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = PortfolioName,
				SecurityId = new SecurityId
				{
					SecurityCode = pair.Key,
					BoardCode = BoardCodes.Zaif,
				},
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, pair.Value.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.BlockedValue, account.Funds.TryGetValue2(pair.Key)?.ToDecimal(), true), cancellationToken);
		}

		_lastTimeBalanceCheck = CurrentTime;
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		
		if (message == null)
		{
			var orders = await _httpClient.GetActiveOrdersAsync(null, cancellationToken);

			var uuids = _orderInfo.Keys.ToHashSet();

			foreach (var pair in orders)
			{
				if (!_orderInfo.TryGetAndRemove(pair.Key, out var info))
				{

				}
				else
				{

				}
			}
		}
		else
		{
			if (!message.IsSubscribe)
				return;

			var orders = await _httpClient.GetActiveOrdersAsync(null, cancellationToken);

			foreach (var pair in orders)
			{
				var transId = TransactionIdGenerator.GetNextId();
				await ProcessOrderAsync(pair.Key, pair.Value, transId, message.TransactionId, OrderStates.Active, cancellationToken);
			}

			await SendSubscriptionResultAsync(message, cancellationToken);
		}
	}

	private ValueTask ProcessOrderAsync(long orderId, Order order, long transId, long origTransId, OrderStates state, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = transId != 0 ? order.Time : CurrentTime,
			SecurityId = order.CurrencyPair.ToStockSharp(),
			TransactionId = transId,
			OriginalTransactionId = origTransId,
			OrderId = orderId,
			OrderVolume = order.Amount,
			//Balance = order.GetBalance(),
			Side = order.Type.ToSide() ?? Sides.Buy,
			OrderPrice = order.Price,
			PortfolioName = PortfolioName,
			OrderState = state,
		}, cancellationToken);
	}
}
