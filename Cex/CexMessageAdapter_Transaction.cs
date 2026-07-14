namespace StockSharp.Cex;

public partial class CexMessageAdapter
{
	private string PortfolioName => nameof(Cex) + "_" + Key.ToId();

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		//var currency = regMsg.SecurityId.ToCcy();

		switch (regMsg.OrderType)
		{
			case null:
			case OrderTypes.Limit:
				//case OrderTypes.Market:
				break;
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		_msgsByTransId.Add(regMsg.TransactionId, Tuple.Create(regMsg.Type, regMsg.SecurityId));
		await _pusherClient.PlaceOrderAsync(regMsg.SecurityId.ToCcy(), regMsg.Side.ToNative(), regMsg.Price, regMsg.Volume, regMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		if (replaceMsg.OldOrderId == null)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(replaceMsg.TransactionId));

		_msgsByTransId.Add(replaceMsg.TransactionId, Tuple.Create(replaceMsg.Type, replaceMsg.SecurityId));
		await _pusherClient.CancelOrderAsync(replaceMsg.OldOrderId.Value, replaceMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		if (cancelMsg.OrderId == null)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		_msgsByTransId.Add(cancelMsg.TransactionId, Tuple.Create(cancelMsg.Type, cancelMsg.SecurityId));
		await _pusherClient.CancelOrderAsync(cancelMsg.OrderId.Value, cancelMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		
		if (message == null)
			throw new ArgumentNullException(nameof(message));

		if (!message.IsSubscribe)
			return;

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = PortfolioName,
			BoardCode = BoardCodes.Cex,
			OriginalTransactionId = message.TransactionId
		}, cancellationToken);

		await SendSubscriptionResultAsync(message, cancellationToken);

		_msgsByTransId.Add(message.TransactionId, Tuple.Create(message.Type, default(SecurityId)));
		await _pusherClient.RequestBalanceAsync(message.TransactionId, cancellationToken);
	}

	///// <inheritdoc />
	//protected override async ValueTask OrderStatusAsync(OrderStatusMessage message, CancellationToken cancellationToken)
	//{
	//	_msgsByTransId.Add(message.TransactionId, Tuple.Create(message.Type, message.SecurityId));
	//	await _pusherClient.RequestOpenOrdersAsync(message.TransactionId, cancellationToken);
	//}

	private async ValueTask SessionOnBalancesReceived(long transactionId, DateTime time, IDictionary<string, RefPair<decimal, decimal>> balances, CancellationToken cancellationToken)
	{
		var currencyInOrders = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

		foreach (var pair in balances)
		{
			var currency = pair.Key;

			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = PortfolioName,
				SecurityId = new SecurityId
				{
					SecurityCode = currency,
					BoardCode = BoardCodes.Cex,
				},
				ServerTime = time,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, pair.Value.First, true)
			.TryAdd(PositionChangeTypes.BlockedValue, pair.Value.Second, true), cancellationToken);

			if (pair.Value.Second != 0)
				currencyInOrders.Add(currency);
		}

		if (currencyInOrders.Count > 0)
		{
			var symbols = (await _httpClient.GetSymbolsAsync(cancellationToken)).Where(s => currencyInOrders.Contains(s.Symbol1) || currencyInOrders.Contains(s.Symbol2)).ToArray();

			foreach (var symbol in symbols)
			{
				//_msgsByTransId.Add(message.TransactionId, Tuple.Create(message.Type, message.SecurityId));
				await _pusherClient.RequestOpenOrdersAsync(new[] { symbol.Symbol1, symbol.Symbol2 }, TransactionIdGenerator.GetNextId(), cancellationToken);
			}
		}
	}

	private ValueTask SessionOnBalanceReceived(string currency, decimal balance, bool isOrder, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = PortfolioName,
			SecurityId = new SecurityId
			{
				SecurityCode = currency,
				BoardCode = BoardCodes.Cex,
			},
			ServerTime = CurrentTime,
		}
		.TryAdd(isOrder ? PositionChangeTypes.CurrentValue : PositionChangeTypes.BlockedValue, balance, true), cancellationToken);
	}

	private async ValueTask SessionOnOpenOrdersReceived(long transactionId, IEnumerable<Order> orders, CancellationToken cancellationToken)
	{
		if (!_msgsByTransId.TryGetValue(transactionId, out var tuple))
			return;

		foreach (var order in orders)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				ServerTime = order.Time ?? CurrentTime,
				SecurityId = tuple.Item2,
				TransactionId = TransactionIdGenerator.GetNextId(),
				OriginalTransactionId = transactionId,
				OrderId = order.Id,
				OrderVolume = order.Amount,
				Balance = order.Pending ?? order.Remains,
				Side = order.Type.ToSide(),
				OrderPrice = order.Price ?? 0,
				PortfolioName = PortfolioName,
				OrderState = OrderStates.Active,
			}, cancellationToken);
		}
	}

	private ValueTask SessionOnOrderPlaced(long transactionId, Order order, CancellationToken cancellationToken)
	{
		if (!_msgsByTransId.TryGetValue(transactionId, out var tuple))
			return default;

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = order.Time ?? CurrentTime,
			SecurityId = tuple.Item2,
			OriginalTransactionId = transactionId,
			OrderId = order.Id,
			OrderVolume = order.Amount,
			Balance = order.Pending ?? order.Remains,
			Side = order.Type.ToSide(),
			OrderPrice = order.Price ?? 0,
			PortfolioName = PortfolioName,
			OrderState = OrderStates.Active,
		}, cancellationToken);
	}

	private ValueTask SessionOnOrderReplaced(long transactionId, Order order, CancellationToken cancellationToken)
	{
		return SessionOnOrderPlaced(transactionId, order, cancellationToken);
	}

	private ValueTask SessionOnOrderCanceled(long transactionId, long orderId, DateTime time, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = time,
			OriginalTransactionId = transactionId,
			OrderId = orderId,
			OrderState = OrderStates.Done,
		}, cancellationToken);
	}

	private ValueTask SessionOnNewTransaction(Transaction transaction, CancellationToken cancellationToken)
	{
		if (transaction.BuyOrderId == null || transaction.SellOrderId == null)
			return default;

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			ServerTime = transaction.Time,
			//SecurityId = secId,
			//OriginalTransactionId = transactionId,
			OrderId = transaction.OrderId,
			TradeId = transaction.Id,
			TradePrice = transaction.Price,
			TradeVolume = transaction.Amount,
			Commission = transaction.FeeAmount,
		}, cancellationToken);
	}

	private ValueTask SessionOnOrderChanged(Order order, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = order.Time ?? CurrentTime,
			OrderId = order.Id,
			OrderVolume = order.Amount,
			Balance = order.Pending ?? order.Remains,
			Side = order.Type.ToSide(),
			OrderPrice = order.Price ?? 0,
			PortfolioName = PortfolioName,
			OrderState = order.Remains == 0 ? OrderStates.Done : OrderStates.Active,
		}, cancellationToken);
	}
}
