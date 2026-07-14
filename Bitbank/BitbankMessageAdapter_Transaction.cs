namespace StockSharp.Bitbank;

public partial class BitbankMessageAdapter
{
	private string PortfolioName => nameof(Bitbank) + "_" + Key.ToId();

	private string EncodeAccountId(string name)
	{
		return PortfolioName + "-" + name;
	}

	private string DecodeAccountId(string name)
	{
		return name.Remove(PortfolioName + "-", true);
	}

	private readonly Dictionary<long, (long transId, decimal bal, string symbol)> _orderInfo = [];


	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		if (lookupMsg != null)
		{
			await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

			if (!lookupMsg.IsSubscribe)
				return;

			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = PortfolioName,
				BoardCode = BoardCodes.Bitbank,
				OriginalTransactionId = lookupMsg.TransactionId
			}, cancellationToken);

			if (RequestWithdrawAccounts)
			{
				var coins = new[] { "btc", "xrp", "ltc", "eth", "mona", "bcc" };

				foreach (var coin in coins)
				{
					var accounts = await _httpClient.GetAccountsAsync(coin, cancellationToken);

					foreach (var account in accounts)
					{
						await SendOutMessageAsync(new PortfolioMessage
						{
							PortfolioName = EncodeAccountId(account.Uuid),
							OriginalTransactionId = lookupMsg.TransactionId
						}, cancellationToken);
					}
				}
			}

			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		}

		var balances = await _httpClient.GetBalancesAsync(cancellationToken);

		foreach (var balance in balances)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = PortfolioName,
				SecurityId = new SecurityId
				{
					SecurityCode = balance.Asset,
					BoardCode = BoardCodes.Bitbank,
				},
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, balance.FreeAmount?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.BlockedValue, balance.LockedAmount?.ToDecimal(), true), cancellationToken);
		}

		_lastTimeBalanceCheck = CurrentTime;
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		if (statusMsg == null)
		{
			var portfolioRefresh = false;

			var orderIds = _orderInfo.ToDictionary(p => p.Key, p => p.Value.symbol);

			foreach (var order in await _httpClient.GetActiveOrdersAsync(cancellationToken))
			{
				var balance = order.RemainingAmount?.ToDecimal() ?? 0;

				orderIds.Remove(order.Id);

				if (_orderInfo.TryGetValue(order.Id, out var info))
				{
					if (info.bal == balance)
						continue;

					await ProcessOrderAsync(order, 0, info.transId, cancellationToken);

					portfolioRefresh = true;
				}
				else
				{
					var transId = TransactionIdGenerator.GetNextId();

					_orderInfo.Add(order.Id, (transId, balance, order.Pair));

					await ProcessOrderAsync(order, transId, 0, cancellationToken);

					portfolioRefresh = true;

					if (!(order.ExecutedAmount > 0))
						continue;

					var trades = await _httpClient.GetOwnTradesAsync(orderId: order.Id, cancellationToken: cancellationToken);
					await ProcessTradesAsync(transId, trades, cancellationToken);
				}
			}

			foreach (var g in orderIds.GroupBy(p => p.Value))
			{
				var orders = await _httpClient.GetOrdersAsync(g.Key, [.. g.Select(p => p.Key)], cancellationToken);

				foreach (var order in orders)
				{
					var (transId, bal, _) = _orderInfo[order.Id];
					await ProcessOrderAsync(order, transId, 0, cancellationToken);

					var trades = await _httpClient.GetOwnTradesAsync(orderId: order.Id, cancellationToken: cancellationToken);
					await ProcessTradesAsync(transId, trades, cancellationToken);

					_orderInfo.Remove(order.Id);

					portfolioRefresh = true;
				}
			}

			foreach (var (orderId, _) in orderIds)
			{
				this.AddWarningLog("Unknown order: {0}", orderId);
				_orderInfo.Remove(orderId);
			}

			if (portfolioRefresh)
				await PortfolioLookupAsync(null, cancellationToken);
		}
		else
		{
			await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);

			if (!statusMsg.IsSubscribe)
				return;

			foreach (var order in await _httpClient.GetActiveOrdersAsync(cancellationToken))
			{
				var transId = TransactionIdGenerator.GetNextId();

				_orderInfo.Add(order.Id, (transId, order.RemainingAmount?.ToDecimal() ?? 0, order.Pair));

				await ProcessOrderAsync(order, transId, statusMsg.TransactionId, cancellationToken);

				if (!(order.ExecutedAmount > 0))
					continue;

				var trades = await _httpClient.GetOwnTradesAsync(orderId: order.Id, cancellationToken: cancellationToken);
				await ProcessTradesAsync(transId, trades, cancellationToken);
			}

			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		}
	}

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
				var condition = (BitbankOrderCondition)regMsg.Condition;

				if (!condition.IsWithdraw)
					throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));

				var withdrawId = await _httpClient.WithdrawAsync(symbol, DecodeAccountId(regMsg.PortfolioName), regMsg.Volume, condition.WithdrawInfo, cancellationToken);

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
		var price = isMarket ? (decimal?)null : regMsg.Price;

		var order = await _httpClient.RegisterOrderAsync(symbol, regMsg.Side.ToNative(), regMsg.OrderType.ToNative(), price, regMsg.Volume, cancellationToken);

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
			var trades = await _httpClient.GetOwnTradesAsync(orderId: order.Id, cancellationToken: cancellationToken);
			await ProcessTradesAsync(regMsg.TransactionId, trades, cancellationToken);
		}
		else
		{
			_orderInfo.Add(order.Id, (regMsg.TransactionId, regMsg.Volume, symbol));
		}

		await PortfolioLookupAsync(null, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		if (cancelMsg.OrderId == null)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		await _httpClient.CancelOrderAsync(cancelMsg.SecurityId.ToSymbol(), cancelMsg.OrderId.Value, cancellationToken);

		await OrderStatusAsync(null, cancellationToken);
		await PortfolioLookupAsync(null, cancellationToken);
	}

	private ValueTask ProcessOrderAsync(Order order, long transId, long origTransId, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = transId != 0 ? order.OrderedAt : CurrentTime,
			SecurityId = order.Pair.ToStockSharp(),
			TransactionId = transId,
			OriginalTransactionId = origTransId,
			OrderId = order.Id,
			OrderVolume = order.StartAmount?.ToDecimal(),
			Balance = order.RemainingAmount?.ToDecimal(),
			Side = order.Type.ToSide(),
			OrderPrice = order.Price?.ToDecimal() ?? 0,
			PortfolioName = PortfolioName,
			OrderType = order.Type.ToOrderType(),
			OrderState = order.Status.ToOrderState(),
		}, cancellationToken);
	}

	private async ValueTask ProcessTradesAsync(long originTransId, IEnumerable<OwnTrade> trades, CancellationToken cancellationToken)
	{
		foreach (var trade in trades)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				ServerTime = trade.ExecutedAt,
				OriginalTransactionId = originTransId,
				OrderId = trade.OrderId,
				TradeId = trade.Id,
				TradeVolume = trade.Amount?.ToDecimal(),
				TradePrice = trade.Price?.ToDecimal(),
				OriginSide = trade.Side.ToSide(),
				Commission = trade.FeeAmountBase?.ToDecimal()
			}, cancellationToken);
		}
	}
}
