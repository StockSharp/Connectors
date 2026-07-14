namespace StockSharp.Gopax;

partial class GopaxMessageAdapter
{
	private string PortfolioName => nameof(Gopax) + "_" + Key.ToId();

	private readonly HashSet<long> _orderIds = [];
	private readonly SynchronizedDictionary<long, RefPair<long, decimal>> _orderInfo = new();
	private long? _ownTradesLatestId;

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
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		var isMarket = regMsg.OrderType == OrderTypes.Market;
		var price = regMsg.OrderType == OrderTypes.Market ? (decimal?)null : regMsg.Price;

		var order = await _httpClient.RegisterOrderAsync(symbol, regMsg.OrderType.ToNative(), regMsg.Side.ToNative(), price, regMsg.Volume, cancellationToken);

		_orderIds.Add(order.Id);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OrderId = order.Id,
			ServerTime = CurrentTime,
			OriginalTransactionId = regMsg.TransactionId,
			OrderState = isMarket ? OrderStates.Done : OrderStates.Active,
			Balance = isMarket ? 0 : null,
			HasOrderInfo = true,
		}, cancellationToken);

		if (isMarket)
		{

		}
		else
			_orderInfo.Add(order.Id, RefTuple.Create(regMsg.TransactionId, regMsg.Volume));

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
				BoardCode = BoardCodes.Gopax,
				OriginalTransactionId = message.TransactionId
			}, cancellationToken);

			await SendSubscriptionResultAsync(message, cancellationToken);
		}

		foreach (var balance in await _httpClient.GetBalancesAsync(cancellationToken))
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = PortfolioName,
				SecurityId = balance.Asset.ToStockSharp(),
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, (decimal)balance.Avail, true)
			.TryAdd(PositionChangeTypes.BlockedValue, (decimal)balance.Hold, true), cancellationToken);
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
			var currTime = CurrentTime;

			var orders = await _httpClient.GetOrdersAsync(cancellationToken);

			var uuids = _orderInfo.Keys.ToHashSet();

			foreach (var o in orders)
			{
				uuids.Remove(o.Id);

				var order = await _httpClient.GetOrderInfoAsync(o.Id, cancellationToken);

				var info = _orderInfo.TryGetValue(order.Id);
				var balance = (decimal)order.Remaining;

				if (info == null)
				{
					if (balance == 0)
						continue;

					var transId = TransactionIdGenerator.GetNextId();

					await ProcessNewOrderAsync(order, transId, 0, OrderStates.Active, cancellationToken);

					portfolioRefresh = true;
				}
				else
				{
					var delta = info.Second - balance;

					if (delta == 0)
						continue;

					info.Second = balance;

					await SendOutMessageAsync(new ExecutionMessage
					{
						HasOrderInfo = true,
						DataTypeEx = DataType.Transactions,
						OrderId = order.Id,
						OriginalTransactionId = info.First,
						ServerTime = currTime,
						Balance = balance,
						OrderState = order.Status.ToOrderState() ?? OrderStates.Active,
					}, cancellationToken);

					portfolioRefresh = true;
				}
			}

			foreach (var uuid in uuids)
			{
				var order = await _httpClient.GetOrderInfoAsync(uuid, cancellationToken);

				var info = _orderInfo.GetAndRemove(uuid);

				await SendOutMessageAsync(new ExecutionMessage
				{
					HasOrderInfo = true,
					DataTypeEx = DataType.Transactions,
					OrderId = uuid,
					OriginalTransactionId = info.First,
					ServerTime = currTime,
					Balance = (decimal?)order.Remaining,
					OrderState = order.Status.ToOrderState(),
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

			foreach (var order in await _httpClient.GetOrdersAsync(cancellationToken))
			{
				await ProcessNewOrderAsync(order, TransactionIdGenerator.GetNextId(), message.OriginalTransactionId, OrderStates.Active, cancellationToken);
			}

			await SendSubscriptionResultAsync(message, cancellationToken);
		}

		await ProcessOwnTradesAsync(cancellationToken);
	}

	private async ValueTask ProcessOwnTradesAsync(CancellationToken cancellationToken)
	{
		var trades = await _httpClient.GetOwnTradesAsync(null, null, _ownTradesLatestId, null, null, cancellationToken);

		foreach (var trade in trades.OrderBy(t => t.Id))
		{
			if (_ownTradesLatestId == null || _ownTradesLatestId.Value < trade.Id)
				_ownTradesLatestId = trade.Id;

			if (!_orderIds.Contains(trade.OrderId))
				continue;

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				ServerTime = trade.Timestamp,
				SecurityId = trade.Symbol.ToStockSharp(),
				OrderId = trade.OrderId,
				TradeId = trade.Id,
				TradePrice = (decimal)trade.Price,
				TradeVolume = (decimal)trade.QuoteAmount,
				Commission = (decimal?)trade.Fee,
			}, cancellationToken);
		}
	}

	private ValueTask ProcessNewOrderAsync(Order order, long transId, long origTransId, OrderStates state, CancellationToken cancellationToken)
	{
		var balance = (decimal)order.Remaining;

		_orderIds.Add(order.Id);
		_orderInfo.Add(order.Id, RefTuple.Create(transId, balance));

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = order.CreatedTimestamp,
			SecurityId = order.Symbol.ToStockSharp(),
			TransactionId = transId,
			OriginalTransactionId = origTransId,
			OrderId = order.Id,
			OrderVolume = (decimal)order.Volume,
			Balance = balance,
			Side = order.Side.ToSide(),
			OrderType = order.Type.ToOrderType(),
			OrderPrice = (decimal)(order.Price ?? 0),
			PortfolioName = PortfolioName,
			OrderState = order.Status.ToOrderState() ?? state,
		}, cancellationToken);
	}
}
