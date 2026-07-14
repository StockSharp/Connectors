namespace StockSharp.Yobit;

using Nito.AsyncEx;

partial class YobitMessageAdapter
{
	private string PortfolioName => nameof(Yobit) + "_" + Key.ToId();

	private readonly Dictionary<long, RefTriple<long, decimal, string>> _orderInfo = [];
	private readonly HashSet<string> _activeSymbols = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly HashSet<string> _allSymbols = new(StringComparer.InvariantCultureIgnoreCase);

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var currency = regMsg.SecurityId.ToCurrency();

		switch (regMsg.OrderType)
		{
			case null:
			case OrderTypes.Limit:
				//case OrderTypes.Market:
				break;
			case OrderTypes.Conditional:
			{
				var condition = (YobitOrderCondition)regMsg.Condition;

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

		var orderId = await _httpClient.RegisterOrderAsync(currency, regMsg.Side.ToNative(), regMsg.Price, regMsg.Volume, cancellationToken);

		_orderInfo.Add(orderId, RefTuple.Create(regMsg.TransactionId, regMsg.Volume, currency));

		_activeSymbols.Add(currency);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OrderId = orderId,
			ServerTime = CurrentTime,
			OriginalTransactionId = regMsg.TransactionId,
			OrderState = OrderStates.Active,
			HasOrderInfo = true,
		}, cancellationToken);

		await OrderStatusAsync(null, cancellationToken);
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
				BoardCode = BoardCodes.Yobit,
				OriginalTransactionId = message.TransactionId
			}, cancellationToken);
		}

		if (_allSymbols.Count == 0)
		{
			_allSymbols.AddRange((await _httpClient.GetSymbolsAsync(cancellationToken)).Select(p => p.Key));
		}

		var funds = await _httpClient.GetFundsAsync(cancellationToken);

		foreach (var pair in funds)
		{
			var currency = pair.Key;
			var fund = pair.Value;

			if (fund.Second != 0 && fund.Second != fund.First)
			{
				// currency -> symbol
				_activeSymbols.AddRange(_allSymbols.Where(symbol => symbol.StartsWithIgnoreCase(currency + "_") || symbol.EndsWithIgnoreCase(currency + "_")));
			}

			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = PortfolioName,
				SecurityId = new SecurityId
				{
					SecurityCode = currency,
					BoardCode = BoardCodes.Yobit,
				},
				ServerTime = CurrentTime,
				OriginalTransactionId = message?.TransactionId ?? 0
			}
			.TryAdd(PositionChangeTypes.CurrentValue, fund.Second, true)
			.TryAdd(PositionChangeTypes.BlockedValue, fund.Second == 0 ? fund.First : fund.Second - fund.First, true), cancellationToken);
		}

		if (message != null)
		{
			await SendSubscriptionResultAsync(message, cancellationToken);
		}

		_lastTimeBalanceCheck = CurrentTime;
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		
		if (message == null)
		{
			if (_orderInfo.Count == 0)
				return;

			var portfolioRefresh = false;

			var opened = await GetActiveOrdersAsync(cancellationToken);

			var uuids = _orderInfo.Keys.ToHashSet();

			foreach (var pair in opened)
			{
				var orderId = pair.Key;
				//var order = pair.Value;
				var order = await _httpClient.GetOrderInfoAsync(orderId, cancellationToken);
				var balance = order.GetBalance();

				uuids.Remove(orderId);

				var info = _orderInfo.TryGetValue(orderId);

				if (info == null)
				{
					var transId = TransactionIdGenerator.GetNextId();
					//var set = CreateTradesSet();
					_orderInfo.Add(orderId, RefTuple.Create(transId, balance, order.Symbol));

					_activeSymbols.Add(order.Symbol);

					await ProcessOrderAsync(orderId, order, transId, 0, balance, order.Status.ToOrderState(), cancellationToken);
					//ProcessTrades(order.Trades, set, transId);

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
						OrderId = orderId,
						OriginalTransactionId = info.First,
						ServerTime = CurrentTime,
						Balance = balance,
					}, cancellationToken);

					//ProcessTrades(order.Trades, info.Third, info.Second);

					portfolioRefresh = true;
				}
			}

			foreach (var orderId in uuids)
			{
				var info = _orderInfo.GetAndRemove(orderId);

				var order = await _httpClient.GetOrderInfoAsync(orderId, cancellationToken);

				_activeSymbols.Remove(order.Symbol);

				await ProcessOrderAsync(orderId, order, 0, info.First, order.GetBalance(), order.Status.ToOrderState(), cancellationToken);
				//ProcessTrades(order.Trades, info.Third, transId);

				portfolioRefresh = true;
			}

			if (portfolioRefresh)
				await PortfolioLookupAsync(null, cancellationToken);
		}
		else
		{
			if (!message.IsSubscribe)
				return;

			var orders = await GetActiveOrdersAsync(cancellationToken);

			foreach (var pair in orders)
			{
				var transId = TransactionIdGenerator.GetNextId();

				var orderId = pair.Key;
				var order = await _httpClient.GetOrderInfoAsync(orderId, cancellationToken);
				var balance = order.GetBalance();

				_orderInfo.Add(pair.Key, RefTuple.Create(transId, balance, order.Symbol));

				await ProcessOrderAsync(orderId, order, transId, message.TransactionId, balance, OrderStates.Active, cancellationToken);
			}

			await SendSubscriptionResultAsync(message, cancellationToken);
		}
	}

	private async Task<IDictionary<long, Order>> GetActiveOrdersAsync(CancellationToken cancellationToken)
	{
		var tasks = _activeSymbols.Select(s => _httpClient.GetActiveOrdersAsync(s, cancellationToken));
		var results = await tasks.WhenAll();
		return results.SelectMany(r => r).ToDictionary();
	}

	private ValueTask ProcessOrderAsync(long orderId, Order order, long transId, long origTransId, decimal balance, OrderStates state, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = transId != 0 ? order.CreatedAt : CurrentTime,
			SecurityId = order.Symbol.ToStockSharp(),
			TransactionId = transId,
			OriginalTransactionId = origTransId,
			OrderId = orderId,
			OrderVolume = order.Amount,
			Balance = balance,
			Side = order.Type.ToSide(),
			OrderPrice = order.Price,
			PortfolioName = PortfolioName,
			OrderState = state,
		}, cancellationToken);
	}
}
