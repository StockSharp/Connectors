namespace StockSharp.Exmo;

partial class ExmoMessageAdapter
{
	private string PortfolioName => nameof(Exmo) + "_" + Key.ToId();

	private readonly SynchronizedDictionary<long, RefQuadruple<long, decimal, decimal, HashSet<long>>> _orderInfo = new();

	private const int _maxErrors = 10;
	private readonly SynchronizedDictionary<long, int> _errorOrders = new();

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
				var condition = (ExmoOrderCondition)regMsg.Condition;

				if (!condition.IsWithdraw)
					throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));

				var withdrawId = await _httpClient.WithdrawAsync(currency, regMsg.Volume, condition.WithdrawInfo, cancellationToken);

				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OrderId = withdrawId,
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
		var orderId = await _httpClient.RegisterOrderAsync(currency, regMsg.Side.ToNative(isMarket), regMsg.Price, regMsg.Volume, cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OrderId = orderId,
			ServerTime = CurrentTime,
			OriginalTransactionId = regMsg.TransactionId,
			OrderState = isMarket ? OrderStates.Done : OrderStates.Active,
			Balance = isMarket ? 0 : null,
			HasOrderInfo = true,
		}, cancellationToken);

		if (isMarket)
		{
			try
			{
				var trades = await _httpClient.GetOrderTradesAsync(orderId, cancellationToken);
				await ProcessOwnTrades(trades, regMsg.TransactionId, cancellationToken);
			}
			catch (Exception ex)
			{
				if (orderId > 0)
					_errorOrders.Add(orderId, 1);

				await SendOutErrorAsync(ex, cancellationToken);
			}
		}
		else
		{
			_orderInfo.Add(orderId, RefTuple.Create(regMsg.TransactionId, regMsg.Volume, regMsg.Volume, new HashSet<long>()));
		}

		await PortfolioLookupAsync(null, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		if (cancelMsg.OrderId == null)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		await _httpClient.CancelOrderAsync(cancelMsg.OrderId.Value, cancellationToken);

		if (_orderInfo.TryGetValue(cancelMsg.OrderId.Value, out var info))
		{
			await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

			var trades = Array.Empty<Trade>();

			try
			{
				trades = (await _httpClient.GetOrderTradesAsync(cancelMsg.OrderId.Value, cancellationToken)).ToArray();
			}
			catch
			{
			}

			_errorOrders.Remove(cancelMsg.OrderId.Value);
			_orderInfo.Remove(cancelMsg.OrderId.Value);

			await SendOutMessageAsync(new ExecutionMessage
			{
				ServerTime = CurrentTime,
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = info.First,
				OrderState = OrderStates.Done,
				Balance = info.Third - trades.Sum(t => t.Quantity),
				HasOrderInfo = true,
			}, cancellationToken);
		}
		else
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
				BoardCode = BoardCodes.Exmo,
				OriginalTransactionId = message.TransactionId
			}, cancellationToken);

			await SendSubscriptionResultAsync(message, cancellationToken);
		}

		var info = await _httpClient.GetBalancesAsync(cancellationToken);

		var dict = new Dictionary<string, RefPair<decimal, decimal>>();

		foreach (var balance in info.Item1)
		{
			dict.Add(balance.Key, RefTuple.Create(balance.Value, 0M));
		}

		foreach (var reserved in info.Item2)
		{
			dict.SafeAdd(reserved.Key, key => RefTuple.Create(0M, 0M)).Second = reserved.Value;
		}

		foreach (var pair in dict)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = PortfolioName,
				SecurityId = pair.Key.ToStockSharp(),
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, pair.Value.First, true)
			.TryAdd(PositionChangeTypes.BlockedValue, pair.Value.Second, true), cancellationToken);
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

			var ids = _orderInfo.Keys.ToHashSet();

			var orders = await _httpClient.GetOpenOrdersAsync(cancellationToken);

			foreach (var pair in orders)
			{
				var secId = pair.Key.ToStockSharp();

				foreach (var order in pair.Value)
				{
					ids.Remove(order.Id);

					if (_errorOrders.TryGetValue(order.Id, out var curr) && curr >= _maxErrors)
						continue;

					Trade[] trades;

					try
					{
						trades = (await _httpClient.GetOrderTradesAsync(order.Id, cancellationToken)).ToArray();
						_errorOrders.Remove(order.Id);
					}
					catch (Exception ex)
					{
						_errorOrders[order.Id] = ++curr;
						await SendOutErrorAsync(ex, cancellationToken);
						continue;
					}

					var balance = order.Quantity - trades.Sum(t => t.Quantity);

					if (_orderInfo.TryGetValue(order.Id, out var info))
					{
						var newTrades = trades.Where(t => !info.Fourth.Contains(t.Id)).ToArray();

						if (newTrades.Length == 0)
							continue;

						await SendOutMessageAsync(new ExecutionMessage
						{
							DataTypeEx = DataType.Transactions,
							HasOrderInfo = true,
							OriginalTransactionId = info.First,
							OrderState = balance > 0 ? OrderStates.Active : OrderStates.Done,
							Balance = balance,
						}, cancellationToken);

						foreach (var newTrade in newTrades)
						{
							await ProcessOwnTrade(newTrade, info.First, cancellationToken);
							info.Fourth.Add(newTrade.Id);
						}

						portfolioRefresh = true;
					}
					else
					{
						if (balance == 0)
							continue;

						var transId = TransactionIdGenerator.GetNextId();

						_orderInfo.Add(order.Id, RefTuple.Create(transId, balance, order.Quantity, trades.Select(t => t.Id).ToHashSet()));

						await ProcessOrder(secId, order, transId, 0, balance, OrderStates.Active, cancellationToken);
						await ProcessOwnTrades(trades, transId, cancellationToken);

						portfolioRefresh = true;
					}
				}
			}

			foreach (var id in ids)
			{
				Trade[] trades;

				try
				{
					trades = (await _httpClient.GetOrderTradesAsync(id, cancellationToken)).ToArray();
					_errorOrders.Remove(id);
				}
				catch (Exception ex)
				{
					if (!_errorOrders.TryGetValue(id, out var curr))
						_errorOrders.Add(id, 1);
					else
						_errorOrders[id] = ++curr;

					await SendOutErrorAsync(ex, cancellationToken);
					continue;
				}

				portfolioRefresh = true;

				var info = _orderInfo.GetAndRemove(id);

				var balance = info.Third - trades.Sum(t => t.Quantity);

				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					HasOrderInfo = true,
					OriginalTransactionId = info.First,
					OrderState = OrderStates.Done,
					Balance = balance,
				}, cancellationToken);

				await ProcessOwnTrades(trades, info.First, cancellationToken);
			}

			if (portfolioRefresh)
				await PortfolioLookupAsync(null, cancellationToken);
		}
		else
		{
			if (!message.IsSubscribe)
				return;

			var orders = await _httpClient.GetOpenOrdersAsync(cancellationToken);

			foreach (var pair in orders)
			{
				var secId = pair.Key.ToStockSharp();

				foreach (var order in pair.Value)
				{
					var transId = TransactionIdGenerator.GetNextId();

					try
					{
						var trades = (await _httpClient.GetOrderTradesAsync(order.Id, cancellationToken)).ToArray();

						var balance = order.Quantity - trades.Sum(t => t.Quantity);

						if (balance == 0)
							continue;

						_orderInfo.Add(order.Id, RefTuple.Create(transId, balance, order.Quantity, trades.Select(t => t.Id).ToHashSet()));

						await ProcessOrder(secId, order, transId, message.TransactionId, balance, OrderStates.Active, cancellationToken);
						await ProcessOwnTrades(trades, transId, cancellationToken);
					}
					catch (Exception ex)
					{
						_errorOrders[order.Id] = 1;
						await SendOutErrorAsync(ex, cancellationToken);
					}
				}
			}
		
			await SendSubscriptionResultAsync(message, cancellationToken);
		}
	}

	private ValueTask ProcessOrder(SecurityId secId, Order order, long transId, long origTransId, decimal balance, OrderStates state, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = transId != 0 ? order.Created : CurrentTime,
			SecurityId = secId,
			TransactionId = transId,
			OriginalTransactionId = origTransId,
			OrderId = order.Id,
			OrderVolume = order.Quantity,
			OrderType = order.Type.ToOrderType(out var side),
			Side = side,
			Balance = balance,
			OrderPrice = order.Price,
			PortfolioName = PortfolioName,
			OrderState = state,
		}, cancellationToken);
	}

	private async ValueTask ProcessOwnTrades(IEnumerable<Trade> trades, long origTransId, CancellationToken cancellationToken)
	{
		foreach (var trade in trades)
		{
			await ProcessOwnTrade(trade, origTransId, cancellationToken);
		}
	}

	private ValueTask ProcessOwnTrade(Trade trade, long origTransId, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = origTransId,
			ServerTime = trade.Time,
			TradeId = trade.Id,
			TradeVolume = trade.Quantity,
			OriginSide = trade.Type.ToSide(),
		}, cancellationToken);
	}
}
