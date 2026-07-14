namespace StockSharp.Bithumb;

public partial class BithumbMessageAdapter
{
	private readonly Dictionary<long, RefPair<decimal, long>> _orderInfo = [];
	//private string _account;
	private string PortfolioName => nameof(Bithumb) + "_" + Key.ToId();

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
				var condition = (BithumbOrderCondition)regMsg.Condition;

				if (!condition.IsWithdraw)
					throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));

				await _httpClient.WithdrawAsync(regMsg.SecurityId.SecurityCode, regMsg.Volume, condition.WithdrawInfo, cancellationToken);

				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
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
		var size = regMsg.Volume;

		var result = await _httpClient.RegisterOrderAsync(regMsg.SecurityId.ToSymbol(), regMsg.Side, price, size, cancellationToken);

		var orderId = result.Item1;

		_orderInfo.Add(orderId, RefTuple.Create(size, regMsg.TransactionId));

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OrderId = orderId,
			ServerTime = CurrentTime,
			OriginalTransactionId = regMsg.TransactionId,
			OrderState = OrderStates.Active,
			HasOrderInfo = true,
		}, cancellationToken);

		foreach (var trade in result.Item2)
		{
			size -= trade.Units;

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				OrderId = orderId,
				ServerTime = CurrentTime,
				OriginalTransactionId = regMsg.TransactionId,
				TradeId = trade.ContId,
				TradePrice = trade.Price,
				TradeVolume = trade.Units,
				Commission = trade.Fee,
			}, cancellationToken);
		}

		if (regMsg.OrderType == OrderTypes.Market || size == 0)
		{
			_orderInfo.Remove(orderId);

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				OrderId = orderId,
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

		await _httpClient.CancelOrderAsync(cancelMsg.Side.Value, cancelMsg.SecurityId.ToSymbol(), cancelMsg.OrderId.Value, cancellationToken);

		var info = _orderInfo.GetAndRemove(cancelMsg.OriginalTransactionId);

		await SendOutMessageAsync(new ExecutionMessage
		{
			ServerTime = CurrentTime,
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = cancelMsg.TransactionId,
			OrderState = OrderStates.Done,
			Balance = info.First,
			HasOrderInfo = true,
		}, cancellationToken);

		await PortfolioLookupAsync(null, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		
		var account = await _httpClient.GetAccountAsync(default, cancellationToken);

		if (message != null)
		{
			if (!message.IsSubscribe)
				return;

			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = PortfolioName,
				BoardCode = BoardCodes.Bithumb,
				OriginalTransactionId = message.TransactionId,
			}, cancellationToken);

			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = account.AccountId,
				BoardCode = BoardCodes.Bithumb,
				OriginalTransactionId = message.TransactionId,
			}, cancellationToken);

			await SendOutMessageAsync(new PositionChangeMessage
			{
				SecurityId = SecurityId.Money,
				PortfolioName = PortfolioName,
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CommissionTaker, account.TradeFee), cancellationToken);

			await SendSubscriptionResultAsync(message, cancellationToken);
		}

		var balances = await _httpClient.GetBalanceAsync("ALL", cancellationToken);

		var coins = balances.Keys.Where(k => k.StartsWithIgnoreCase("total_")).Select(k => k.Remove("total_", true)).ToArray();

		foreach (var coin in coins)
		{
			await ProcessPositionAsync(coin, balances.TryGetValue("total_" + coin), balances.TryGetValue("available_" + coin), balances.TryGetValue("in_use_" + coin), cancellationToken);
		}

		_lastTimeBalanceCheck = CurrentTime;
	}

	private ValueTask ProcessPositionAsync(string currency, decimal? total, decimal? available, decimal? isUse, CancellationToken cancellationToken)
	{
		if (total == null && available == null && isUse == null)
			return default;

		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = PortfolioName,
			SecurityId = new SecurityId
			{
				SecurityCode = currency.ToUpperInvariant(),
				BoardCode = BoardCodes.Bithumb,
			},
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.BeginValue, total?.RemoveTrailingZeros(), true)
		.TryAdd(PositionChangeTypes.CurrentValue, available?.RemoveTrailingZeros(), true)
		.TryAdd(PositionChangeTypes.BlockedValue, isUse?.RemoveTrailingZeros(), true), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		
		if (message == null)
		{
			var portfolioRefresh = false;

			var orders = await _httpClient.GetOrdersAsync(cancellationToken: cancellationToken);

			var orderIds = _orderInfo.Keys.ToHashSet();

			foreach (var order in orders)
			{
				orderIds.Remove(order.OrderId);

				var info = _orderInfo.TryGetValue(order.OrderId);

				if (info == null)
				{
					var transId = TransactionIdGenerator.GetNextId();

					_orderInfo.Add(order.OrderId, RefTuple.Create(order.UnitsRemaining ?? order.Units, transId));

					await ProcessOrderAsync(order, transId, 0, cancellationToken);

					portfolioRefresh = true;
				}
				else
				{
					var delta = info.First - order.UnitsRemaining ?? order.Units;

					if (delta == 0)
						continue;

					info.First = order.UnitsRemaining ?? order.Units;

					await SendOutMessageAsync(new ExecutionMessage
					{
						HasOrderInfo = true,
						DataTypeEx = DataType.Transactions,
						OrderId = order.OrderId,
						OriginalTransactionId = info.Second,
						ServerTime = CurrentTime,
						Balance = order.UnitsRemaining,
						//TradePrice = order.Limit,
						TradeStringId = Guid.NewGuid().ToString(),
						TradeVolume = delta,
					}, cancellationToken);

					portfolioRefresh = true;
				}
			}

			foreach (var uuid in orderIds)
			{
				//var order = _httpClient.GetOrder(uuid, 0);

				//var info = _orderInfo.GetAndRemove(uuid);

				//ProcessOrder(order, 0, info.Second);

				portfolioRefresh = true;
			}

			if (portfolioRefresh)
				await PortfolioLookupAsync(null, cancellationToken);
		}
		else
		{
			if (!message.IsSubscribe)
				return;

			var orders = await _httpClient.GetOrdersAsync(cancellationToken: cancellationToken);

			foreach (var order in orders)
			{
				var transId = TransactionIdGenerator.GetNextId();

				_orderInfo.Add(order.OrderId, RefTuple.Create(order.UnitsRemaining ?? order.Units, transId));

				await ProcessOrderAsync(order, transId, message.TransactionId, cancellationToken);
			}

			await SendSubscriptionResultAsync(message, cancellationToken);
		}
	}

	private ValueTask ProcessOrderAsync(Order order, long transId, long origTransId, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = (transId == 0 ? order.DateCompleted ?? order.OrderDate : order.OrderDate).FromUnix(),
			SecurityId = order.OrderCurrency.ToStockSharp(),
			TransactionId = transId,
			OriginalTransactionId = origTransId,
			OrderId = order.OrderId,
			OrderVolume = order.Units,
			Balance = order.UnitsRemaining,
			Side = order.Type.ToSide(),
			OrderPrice = order.Price,
			PortfolioName = PortfolioName,
			Commission = order.Fee,
			OrderState = order.DateCompleted == null ? OrderStates.Active : OrderStates.Done,
		}, cancellationToken);
	}
}
