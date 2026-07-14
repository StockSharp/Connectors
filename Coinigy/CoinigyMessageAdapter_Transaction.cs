namespace StockSharp.Coinigy;

public partial class CoinigyMessageAdapter
{
	//private string PortfolioName => nameof(Coinigy) + "_" + Key.ToId();

	private readonly PairSet<string, int> _accountIds = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly SynchronizedDictionary<long, RefPair<long, decimal>> _orderInfo = new();

	private string GetPortfolioName(int authId)
	{
		if (_accountIds.TryGetKey(authId, out var portfolioName))
			return portfolioName;

		throw new ArgumentException(authId.ToString());
	}

	private int GetAccountId(string portfolioName)
	{
		if (_accountIds.TryGetValue(portfolioName, out var accountId))
			return accountId;

		throw new ArgumentException(portfolioName);
	}

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		regMsg.SecurityId.ToCurrency(out var baseCurr, out var quoteCurr, out var exchange);

		switch (regMsg.OrderType)
		{
			case null:
			case OrderTypes.Limit:
			case OrderTypes.Market:
				break;
			case OrderTypes.Conditional:
				break;
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		var isMarket = regMsg.OrderType == OrderTypes.Market;
		var price = regMsg.OrderType == OrderTypes.Market ? (decimal?)null : regMsg.Price;

		var type = isMarket ? "market" : "limit";

		if (regMsg.OrderType == OrderTypes.Conditional)
		{
			type = "stop" + char.ToUpper(type[0]) + type.Substring(1);
		}

		var condition = (CoinigyOrderCondition)regMsg.Condition;

		var order = await _httpClient.RegisterOrderAsync(GetAccountId(regMsg.PortfolioName), type, baseCurr, quoteCurr,
			regMsg.Side.ToNative(), price, regMsg.Volume, false, condition.ConditionalPrice, cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OrderId = order.Id,
			OrderStringId = order.ForeignOrderId,
			ServerTime = CurrentTime,
			OriginalTransactionId = regMsg.TransactionId,
			OrderState = isMarket ? OrderStates.Done : OrderStates.Active,
			Balance = isMarket ? 0 : null,
			HasOrderInfo = true,
		}, cancellationToken);

		if (!isMarket)
			_orderInfo.Add(order.Id, RefTuple.Create(regMsg.TransactionId, regMsg.Volume));

		await PortfolioLookupAsync(null, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		if (cancelMsg.OrderId == null)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		await _httpClient.CancelOrderAsync(GetAccountId(cancelMsg.PortfolioName), cancelMsg.OrderId.Value, cancellationToken);

		await OrderStatusAsync(null, cancellationToken);
		await PortfolioLookupAsync(null, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage message, CancellationToken cancellationToken)
	{
		if (message != null)
		{
			await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);

			if (!message.IsSubscribe)
				return;

			//await SendOutMessageAsync(new PortfolioMessage
			//{
			//	PortfolioName = PortfolioName,
			//	BoardCode = ExchangeBoard.Coinigy.Code,
			//	OriginalTransactionId = message.TransactionId
			//}, cancellationToken);

			var accounts = await _httpClient.GetAccountsAsync(cancellationToken);

			foreach (var account in accounts)
			{
				await SendOutMessageAsync(new PortfolioMessage
				{
					PortfolioName = account.AuthNickname,
					BoardCode = BoardCodes.Coinigy,
					OriginalTransactionId = message.TransactionId
				}, cancellationToken);

				_accountIds[account.AuthNickname] = account.AuthId;
			}

			await SendSubscriptionResultAsync(message, cancellationToken);
		}

		foreach (var pair in _accountIds)
		{
			var balances = await _httpClient.GetBalancesAsync(pair.Value, cancellationToken);

			foreach (var balance in balances)
			{
				await SendOutMessageAsync(new PositionChangeMessage
				{
					PortfolioName = pair.Key,
					SecurityId = new SecurityId
					{
						SecurityCode = balance.BalanceCurrCode,
						BoardCode = BoardCodes.Coinigy,
					},
					ServerTime = CurrentTime,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, balance.BalanceAmountAvailable?.ToDecimal(), true)
				.TryAdd(PositionChangeTypes.BlockedValue, balance.BalanceAmountHeld?.ToDecimal(), true), cancellationToken);
			}
		}

		_lastTimeBalanceCheck = CurrentTime;
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage message, CancellationToken cancellationToken)
	{
		if (message == null)
		{
			var portfolioRefresh = false;

			var uuids = _orderInfo.Keys.ToHashSet();

			var orders = await _httpClient.GetOpenOrdersAsync(cancellationToken);

			foreach (var order in orders)
			{
				var info = _orderInfo.TryGetValue(order.Id);
				var balance = order.GetBalance();

				if (info == null)
				{
					var transId = TransactionIdGenerator.GetNextId();

					_orderInfo.Add(order.Id, RefTuple.Create(transId, balance));
					await ProcessOrderAsync(order, transId, 0, cancellationToken);

					portfolioRefresh = true;
				}
				else
				{
					uuids.Remove(order.Id);

					if (balance == info.Second)
						continue;

					info.Second = balance;
					await ProcessOrderAsync(order, 0, info.First, cancellationToken);

					portfolioRefresh = true;
				}
			}

			foreach (var uuid in uuids)
			{
				var info = _orderInfo.GetAndRemove(uuid);
				var order = await _httpClient.GetOrderInfoAsync(uuid, cancellationToken);

				await ProcessOrderAsync(order, 0, info.First, cancellationToken);

				portfolioRefresh = true;
			}

			if (portfolioRefresh)
				await PortfolioLookupAsync(null, cancellationToken);
		}
		else
		{
			await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);

			if (!message.IsSubscribe)
				return;

			var orders = await _httpClient.GetOpenOrdersAsync(cancellationToken);

			foreach (var order in orders)
			{
				var transId = TransactionIdGenerator.GetNextId();
				_orderInfo.Add(order.Id, RefTuple.Create(transId, order.GetBalance()));
				await ProcessOrderAsync(order, transId, message.TransactionId, cancellationToken);
			}

			await SendSubscriptionResultAsync(message, cancellationToken);
		}
	}

	private ValueTask ProcessOrderAsync(Order order, long transId, long origTransId, CancellationToken cancellationToken)
	{
		var orderType = order.OrderPriceType.ToOrderType();

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = transId != 0 ? order.Time : CurrentTime,
			SecurityId = order.Market.ToStockSharp(order.Exchange),
			TransactionId = transId,
			OriginalTransactionId = origTransId,
			OrderId = order.Id,
			OrderBoardId = order.ForeignOrderId,
			OrderVolume = order.Quantity?.ToDecimal(),
			Balance = order.GetBalance(),
			Side = order.Type.ToSide(),
			OrderPrice = order.LimitPrice?.ToDecimal() ?? 0,
			PortfolioName = GetPortfolioName(order.AuthId),
			OrderState = order.Status.ToOrderState(),
			OrderType = orderType,
			Condition = orderType == OrderTypes.Conditional ? new CoinigyOrderCondition
			{
				ConditionalPrice = order.StopPrice?.ToDecimal(),
			} : null,
		}, cancellationToken);
	}
}