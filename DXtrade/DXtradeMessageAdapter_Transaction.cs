namespace StockSharp.DXtrade;

public partial class DXtradeMessageAdapter
{
	private readonly SynchronizedSet<long> _processTransIds = [];
	private string _defaultAcc;

	private async ValueTask<string> EnsureGetAccount(CancellationToken cancellationToken)
	{
		if (_defaultAcc is null)
		{
			var portfolios = await _httpClient.GetPortfolio(cancellationToken);
			_defaultAcc ??= portfolios.FirstOrDefault()?.Account;
		}

		return _defaultAcc;
	}

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var condition = (DXtradeOrderCondition)regMsg.Condition;

		/*var response = */await _httpClient.PlaceOrder(
			regMsg.PortfolioName, regMsg.TransactionId.ToString(),
			regMsg.OrderType?.ToNative(),
			regMsg.SecurityId.SecurityCode, regMsg.Volume, regMsg.PositionEffect?.ToNative(),
			default, regMsg.Side.ToNative(), regMsg.Price,
			condition?.StopPrice, condition?.Offset, default,
			regMsg.TimeInForce.ToNative(regMsg.TillDate), default, regMsg.TillDate?.ToTimeStamp(),
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		var condition = (DXtradeOrderCondition)replaceMsg.Condition;

		await _httpClient.ModifyOrder(
			replaceMsg.PortfolioName, replaceMsg.TransactionId.ToString(),
			replaceMsg.SecurityId.ToNative(), replaceMsg.Volume,
			replaceMsg.Side.ToNative(), replaceMsg.Price, condition?.StopPrice,
			replaceMsg.TimeInForce.ToNative(replaceMsg.TillDate), replaceMsg.TillDate?.ToTimeStamp(),
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
		=> await _httpClient.CancelOrder(cancelMsg.PortfolioName, cancelMsg.OriginalTransactionId, cancellationToken);

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
		=> await _httpClient.CancelOrderGroup(cancelMsg.PortfolioName, default, default, cancellationToken);

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		if (!lookupMsg.IsSubscribe)
		{
			await _privateClient.UnsubscribePortfolio(lookupMsg.TransactionId, lookupMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var portfolios = await _httpClient.GetPortfolio(cancellationToken);

		foreach (var portfolio in portfolios)
		{
			_defaultAcc ??= portfolio.Account;
			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = portfolio.Account,
				OriginalTransactionId = lookupMsg.TransactionId,
			}, cancellationToken);
			await ProcessAccountPortfolio(lookupMsg.TransactionId, portfolio, cancellationToken);
		}

		if (!lookupMsg.IsHistoryOnly())
			await _privateClient.SubscribePortfolio(lookupMsg.TransactionId, new() { RequestType = "ALL" }, cancellationToken);

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);

		if (!statusMsg.IsSubscribe)
		{
			return;
		}

		var orders = await _httpClient.GetOpenOrders(cancellationToken);

		await ProcessOrders(statusMsg.TransactionId, orders, cancellationToken);

		if (!statusMsg.IsHistoryOnly())
		{
		}

		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private async ValueTask ProcessAccountPortfolio(long originTransId, AccountPortfolio portfolio, CancellationToken cancellationToken)
	{
		if (portfolio is null)
			throw new ArgumentNullException(nameof(portfolio));

		if (portfolio.Positions is not null)
			await ProcessPositions(originTransId, portfolio.Positions, cancellationToken);

		if (portfolio.Balances is not null)
			await ProcessBalances(originTransId, portfolio.Balances, cancellationToken);
	}

	private async ValueTask ProcessBalances(long transId, IEnumerable<Balance> balances, CancellationToken cancellationToken)
	{
		if (balances is null)
			throw new ArgumentNullException(nameof(balances));

		foreach (var balance in balances)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = balance.Account,
				SecurityId = SecurityId.Money,
				ServerTime = DateTime.UtcNow,
				OriginalTransactionId = transId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, balance.Value?.ToDecimal())
			, cancellationToken);
		}
	}

	private async ValueTask ProcessOrders(long origTransId, IEnumerable<Order> orders, CancellationToken cancellationToken)
	{
		if (orders is null)
			throw new ArgumentNullException(nameof(orders));

		foreach (var order in orders)
		{
			if (!long.TryParse(order.ClientOrderId, out var transId))
				continue;

			if (origTransId != 0 && !_processTransIds.TryAdd(transId))
			{
				// duplicate
				continue;
			}

			var leg = order.Legs?.FirstOrDefault();

			if (leg is null)
				continue;

			var orderType = order.Type.ToOrderType();
			var orderState = order.Status.ToOrderState();

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				ServerTime = origTransId != 0 ? order.IssueTime : order.TransactionTime,
				SecurityId = order.Instrument.ToStockSharp(),
				TransactionId = origTransId != 0 ? transId : 0,
				OriginalTransactionId = origTransId != 0 ? origTransId : transId,
				OrderId = order.OrderId,
				OrderStringId = order.OrderCode,
				OrderVolume = leg.Quantity?.ToDecimal()?.Abs(),
				Balance = leg.RemainingQuantity?.ToDecimal()?.Abs(),
				Side = order.Side?.ToSide() ?? default,
				OrderPrice = leg.Price?.ToDecimal() ?? 0,
				AveragePrice = leg.AveragePrice?.ToDecimal()?.DefaultAsNull(),
				TimeInForce = order.Tif.ToTimeInForce(),
				ExpiryDate = order.ExpireDate,
				OrderType = orderType,
				Condition = new DXtradeOrderCondition
				{
					Offset = order.PriceOffset?.ToDecimal(),
				},
				PositionEffect = leg.PositionEffect.ToPositionEffect(),
				PortfolioName = order.Account,
				OrderState = orderState,
				Error = orderState == OrderStates.Failed ? new InvalidOperationException(order.Executions?.FirstOrDefault(e => !e.RejectReason.IsEmpty())?.RejectReason) : null,
			}, cancellationToken);

			if (order.Executions is null)
				continue;

			foreach (var execution in order.Executions)
			{
				var price = execution.LastPrice?.ToDecimal()?.DefaultAsNull();

				if (price is null)
					continue;

				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					ServerTime = execution.TransactionTime,
					TradePrice = price,
					TradeVolume = execution.LastQuantity?.ToDecimal(),
					OriginalTransactionId = transId,
				}, cancellationToken);
			}
		}
	}

	private async ValueTask ProcessPositions(long transId, IEnumerable<Position> positions, CancellationToken cancellationToken)
	{
		if (positions is null)
			throw new ArgumentNullException(nameof(positions));

		foreach (var position in positions)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = position.Account,
				SecurityId = position.Symbol.ToStockSharp(),
				ServerTime = position.LastUpdateTime,
				OriginalTransactionId = transId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, position.Quantity?.ToDecimal())
			.TryAdd(PositionChangeTypes.AveragePrice, position.OpenPrice?.ToDecimal())
			, cancellationToken);
		}
	}

	private async ValueTask SessionOnPortfolioReceived(AccountPortfolio portfolio, CancellationToken cancellationToken)
	{
		if (portfolio is null)
			throw new ArgumentNullException(nameof(portfolio));

		await ProcessAccountPortfolio(0, portfolio, cancellationToken);

		if (portfolio.Orders is not null)
			await ProcessOrders(0, portfolio.Orders, cancellationToken);
	}
}