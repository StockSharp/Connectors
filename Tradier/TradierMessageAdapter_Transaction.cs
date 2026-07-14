namespace StockSharp.Tradier;

public partial class TradierMessageAdapter
{
	private readonly SynchronizedDictionary<long, decimal> _orderBalances = [];

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var stopPrice = ((TradierOrderCondition)regMsg.Condition)?.StopPrice;

		var isMarket = regMsg.OrderType == OrderTypes.Market;
		var price = isMarket ? (decimal?)null : regMsg.Price;
		var isOption = regMsg.SecurityType == SecurityTypes.Option;

		_orderBalances.Add(regMsg.TransactionId, regMsg.Volume);

		/*var orderId = */await _httpClient.RegisterOrder(regMsg.PortfolioName, regMsg.SecurityType.ToClass(),
			isOption ? regMsg.GetUnderlyingCode() : regMsg.SecurityId.SecurityCode, regMsg.TimeInForce.ToDuration(regMsg.TillDate),
			regMsg.Side.ToNative(), price, regMsg.Volume, regMsg.OrderType.ToNative(stopPrice),
			stopPrice, isOption ? regMsg.SecurityId.SecurityCode : null, regMsg.TransactionId.To<string>(), cancellationToken);

	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		if (cancelMsg.OrderId == null)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		await _httpClient.CancelOrder(cancelMsg.PortfolioName, cancelMsg.OrderId.Value, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		if (replaceMsg.OldOrderId == null)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(replaceMsg.OriginalTransactionId));

		var stopPrice = ((TradierOrderCondition)replaceMsg.Condition)?.StopPrice;

		var isMarket = replaceMsg.OrderType == OrderTypes.Market;
		var price = isMarket ? (decimal?)null : replaceMsg.Price;

		await _httpClient.ChangeOrder(replaceMsg.PortfolioName, replaceMsg.OldOrderId.Value,
			replaceMsg.TimeInForce.ToDuration(replaceMsg.TillDate), price, replaceMsg.OrderType.ToNative(stopPrice), stopPrice, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		if (!lookupMsg.IsSubscribe)
			return;

		var accounts = await _httpClient.GetAccounts(cancellationToken);

		foreach (var account in accounts)
		{
			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = account.Id,
				BoardCode = BoardCodes.Tradier,
				OriginalTransactionId = lookupMsg.TransactionId
			}, cancellationToken);

			foreach (var position in await _httpClient.GetPositions(account.Id, cancellationToken))
			{
				await SendOutMessageAsync(new PositionChangeMessage
				{
					PortfolioName = account.Id,
					SecurityId = position.Symbol.ToStockSharp(),
					ServerTime = CurrentTime,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, position.Quantity.ToDecimal(), true)
				.TryAdd(PositionChangeTypes.CurrentPrice, position.CostBasis?.ToDecimal(), true), cancellationToken);
			}

			var (balance, _) = await _httpClient.GetBalances(account.Id, cancellationToken);

			if (balance is null)
				continue;

			await SendOutMessageAsync(new PositionChangeMessage
			{
				SecurityId = SecurityId.Money,
				PortfolioName = account.Id,
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.BlockedValue, balance.PendingCash?.ToDecimal())
			.TryAdd(PositionChangeTypes.CurrentValue, balance.TotalCash?.ToDecimal())
			.TryAdd(PositionChangeTypes.UnrealizedPnL, balance.OpenPl?.ToDecimal())
			.TryAdd(PositionChangeTypes.RealizedPnL, balance.ClosePl?.ToDecimal())
			, cancellationToken);
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);

		if (!statusMsg.IsSubscribe)
			return;

		var accounts = await _httpClient.GetAccounts(cancellationToken);

		foreach (var account in accounts)
		{
			var orders = await _httpClient.GetOrders(account.Id, cancellationToken);

			foreach (var order in orders)
			{
				await ProcessOrder(account.Id, order, statusMsg.TransactionId, cancellationToken);
			}
		}

		if (!statusMsg.IsHistoryOnly())
			await _accClient.Subscribe(statusMsg.TransactionId, cancellationToken);

		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private ValueTask ProcessOrder(string accountId, Order order, long origTransId, CancellationToken cancellationToken)
	{
		if (!long.TryParse(order.Tag, out var transId))
			return default;

		if (order.Legs?.Length > 0)
			return default;

		var state = order.Status.ToOrderState();
		var type = order.Type.ToOrderType();
		var isLookup = origTransId != 0;

		if (isLookup)
		{
			if ((order.Quantity - order.ExecQuantity)?.ToDecimal() is decimal balance)
				_orderBalances[transId] = balance;
		}
		else
		{
			if (order.LastFillQuantity?.ToDecimal() is decimal lastQty && lastQty > 0 && _orderBalances.TryGetValue(transId, out var balance))
			{
				balance -= lastQty;

				if (balance < 0)
					this.AddWarningLog("Order {0} has negative balance.", transId);
				else
					_orderBalances[transId] = lastQty;
			}
		}

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = isLookup ? order.CreateDate : (order.TransactionDate ?? CurrentTime),
			SecurityId = (order.OptionSymbol.IsEmpty() ? order.Symbol : order.OptionSymbol).ToStockSharp(),
			TransactionId = isLookup ? transId : 0,
			OriginalTransactionId = isLookup ? origTransId : transId,
			OrderId = order.Id,
			OrderVolume = order.Quantity?.ToDecimal()?.DefaultAsNull(),
			OrderType = type,
			Balance = _orderBalances.TryGetValue2(transId),
			Side = order.Side.ToSide() ?? default,
			TimeInForce = order.Duration.ToTimeInForce(out var tillDate),
			ExpiryDate = tillDate,
			OrderPrice = order.Price.ToDecimal() ?? default,
			PortfolioName = accountId,
			AveragePrice = order.AvgFillPrice?.ToDecimal()?.DefaultAsNull(),
			Condition = type == OrderTypes.Conditional ? new TradierOrderCondition
			{
				StopPrice = order.StopPrice?.ToDecimal(),
			}: null,
			OrderState = state,
			Error = state == OrderStates.Failed ? new InvalidOperationException(order.ReasonDescription) : null,
			TradePrice = isLookup ? null : order.LastFillPrice?.ToDecimal()?.DefaultAsNull(),
			TradeVolume = isLookup ? null : order.LastFillQuantity?.ToDecimal()?.DefaultAsNull(),
		}, cancellationToken);
	}

	private ValueTask SessionOnOrderReceived(Order order, CancellationToken cancellationToken)
	{
		return ProcessOrder(order.Account, order, 0, cancellationToken);
	}
}