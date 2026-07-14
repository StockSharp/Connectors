namespace StockSharp.TradeOgre;

partial class TradeOgreMessageAdapter
{
	private string PortfolioName => nameof(TradeOgre) + "_" + Key.ToId();

	private readonly SynchronizedDictionary<string, RefTriple<long, decimal, string>> _orderInfo = new(StringComparer.InvariantCultureIgnoreCase);

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
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		var isMarket = regMsg.OrderType == OrderTypes.Market;
		var price = regMsg.OrderType == OrderTypes.Market ? (decimal?)null : regMsg.Price;

		var orderId = await _httpClient.RegisterOrderAsync(currency, regMsg.Side, price, regMsg.Volume, cancellationToken);

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
			_orderInfo.Add(orderId, RefTuple.Create(regMsg.TransactionId, regMsg.Volume, currency));

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
	protected override async ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		await _httpClient.CancelOrderAsync("all", cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			ServerTime = CurrentTime,
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = cancelMsg.TransactionId,
		}, cancellationToken);
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
				BoardCode = BoardCodes.TradeOgre,
				OriginalTransactionId = message.TransactionId
			}, cancellationToken);

			await SendSubscriptionResultAsync(message, cancellationToken);
		}

		var currentTime = CurrentTime;
		var balances = await _httpClient.GetBalancesAsync(cancellationToken);

		foreach (var pair in balances)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = PortfolioName,
				SecurityId = new SecurityId
				{
					SecurityCode = pair.Key,
					BoardCode = BoardCodes.TradeOgre,
				},
				ServerTime = currentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, pair.Value.ToDecimal(), true), cancellationToken);
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

			var uuids = _orderInfo.Keys.ToHashSet();

			var orders = await _httpClient.GetOrdersAsync(null, cancellationToken);

			foreach (var order in orders)
			{
				var info = _orderInfo.TryGetValue(order.Id);
				var orderDetails = await _httpClient.GetOrderAsync(order.Id, cancellationToken);
				var balance = orderDetails.GetBalance();

				if (info == null)
				{
					var transId = TransactionIdGenerator.GetNextId();

					_orderInfo.Add(order.Id, RefTuple.Create(transId, balance, order.Market));
					await ProcessOrderAsync(orderDetails, transId, 0, balance != 0, cancellationToken);

					portfolioRefresh = true;
				}
				else
				{
					uuids.Remove(order.Id);

					if (balance == info.Second)
						continue;

					info.Second = balance;
					await ProcessOrderAsync(orderDetails, 0, info.First, true, cancellationToken);

					portfolioRefresh = true;
				}
			}

			foreach (var uuid in uuids)
			{
				var info = _orderInfo.GetAndRemove(uuid);
				var orderDetails = await _httpClient.GetOrderAsync(uuid, cancellationToken);

				await ProcessOrderAsync(orderDetails, 0, info.First, false, cancellationToken);

				portfolioRefresh = true;
			}

			if (portfolioRefresh)
				await PortfolioLookupAsync(null, cancellationToken);
		}
		else
		{
			if (!message.IsSubscribe)
				return;

			var orders = await _httpClient.GetOrdersAsync(null, cancellationToken);

			foreach (var order in orders)
			{
				var orderDetails = await _httpClient.GetOrderAsync(order.Id, cancellationToken);
				var transId = TransactionIdGenerator.GetNextId();

				_orderInfo.Add(order.Id, RefTuple.Create(transId, orderDetails.GetBalance(), order.Market));
				await ProcessOrderAsync(orderDetails, transId, message.TransactionId, true, cancellationToken);
			}

			await SendSubscriptionResultAsync(message, cancellationToken);
		}
	}

	private ValueTask ProcessOrderAsync(Order order, long transId, long origTransId, bool isActive, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = transId != 0 ? order.Time : CurrentTime,
			SecurityId = order.Market.ToStockSharp(),
			TransactionId = transId,
			OriginalTransactionId = origTransId,
			OrderStringId = order.Id,
			OrderVolume = order.Quantity.ToDecimal(),
			Balance = order.GetBalance(),
			Side = order.Type.ToSide(),
			OrderPrice = order.Price?.ToDecimal() ?? 0,
			PortfolioName = PortfolioName,
			OrderState = isActive ? OrderStates.Active : OrderStates.Done,
		}, cancellationToken);
	}
}
