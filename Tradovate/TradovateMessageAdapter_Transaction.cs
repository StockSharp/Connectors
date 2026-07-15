namespace StockSharp.Tradovate;

public partial class TradovateMessageAdapter
{
	private readonly SynchronizedDictionary<long, TradovateAccount> _accounts = [];
	private readonly SynchronizedDictionary<long, TradovateOrder> _orders = [];
	private readonly SynchronizedDictionary<long, TradovateOrderVersion> _orderVersions = [];
	private readonly SynchronizedDictionary<long, long> _orderTransactions = [];
	private readonly SynchronizedDictionary<long, decimal> _orderBalances = [];
	private readonly SynchronizedSet<long> _fills = [];

	private void ClearState()
	{
		_contracts.Clear();
		_contractIds.Clear();
		_level1Subscriptions.Clear();
		_tickSubscriptions.Clear();
		_depthSubscriptions.Clear();
		_candleSubscriptions.Clear();
		_chartSubscriptions.Clear();
		_accounts.Clear();
		_orders.Clear();
		_orderVersions.Clear();
		_orderTransactions.Clear();
		_orderBalances.Clear();
		_fills.Clear();
	}

	private async Task<TradovateAccount[]> EnsureAccounts(CancellationToken cancellationToken)
	{
		var accounts = await _httpClient.GetAccounts(cancellationToken);
		foreach (var account in accounts)
			_accounts[account.Id] = account;
		return accounts;
	}

	private async Task<TradovateAccount> GetAccount(string portfolioName, CancellationToken cancellationToken)
	{
		if (_accounts.Values.FirstOrDefault(a => a.Name.EqualsIgnoreCase(portfolioName)) is { } account)
			return account;

		return (await EnsureAccounts(cancellationToken)).FirstOrDefault(a => a.Name.EqualsIgnoreCase(portfolioName))
			?? throw new InvalidOperationException($"Tradovate account '{portfolioName}' was not found.");
	}

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var account = await GetAccount(regMsg.PortfolioName, cancellationToken);
		var stopPrice = (regMsg.Condition as TradovateOrderCondition)?.StopPrice;
		var orderType = (regMsg.OrderType ?? OrderTypes.Limit).ToNative(stopPrice);
		var orderId = await _httpClient.PlaceOrder(new PlaceOrderRequest
		{
			AccountSpec = account.Name,
			AccountId = account.Id,
			ClOrdId = regMsg.TransactionId.ToString(CultureInfo.InvariantCulture),
			Action = regMsg.Side.ToNative(),
			Symbol = regMsg.SecurityId.SecurityCode,
			OrderQty = checked((int)regMsg.Volume),
			OrderType = orderType,
			Price = orderType is TradovateOrderTypes.Market or TradovateOrderTypes.Stop ? null : regMsg.Price,
			StopPrice = stopPrice,
			TimeInForce = (regMsg.TimeInForce ?? TimeInForce.PutInQueue).ToNative(),
			ExpireTime = regMsg.TillDate,
			IsAutomated = true,
		}, cancellationToken);

		_orderTransactions[orderId] = regMsg.TransactionId;
		_orderBalances[orderId] = regMsg.Volume;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			OrderId = orderId,
			OrderState = OrderStates.Pending,
			SecurityId = regMsg.SecurityId,
			PortfolioName = regMsg.PortfolioName,
			Side = regMsg.Side,
			OrderType = regMsg.OrderType,
			OrderPrice = regMsg.Price,
			OrderVolume = regMsg.Volume,
			Balance = regMsg.Volume,
			ServerTime = DateTime.UtcNow,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		if (replaceMsg.OldOrderId is not long orderId)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(replaceMsg.OriginalTransactionId));

		var stopPrice = (replaceMsg.Condition as TradovateOrderCondition)?.StopPrice;
		var orderType = (replaceMsg.OrderType ?? OrderTypes.Limit).ToNative(stopPrice);
		await _httpClient.ModifyOrder(new ModifyOrderRequest
		{
			OrderId = orderId,
			ClOrdId = replaceMsg.TransactionId.ToString(CultureInfo.InvariantCulture),
			OrderQty = checked((int)replaceMsg.Volume),
			OrderType = orderType,
			Price = orderType is TradovateOrderTypes.Market or TradovateOrderTypes.Stop ? null : replaceMsg.Price,
			StopPrice = stopPrice,
			TimeInForce = (replaceMsg.TimeInForce ?? TimeInForce.PutInQueue).ToNative(),
			ExpireTime = replaceMsg.TillDate,
			IsAutomated = true,
		}, cancellationToken);
		_orderTransactions[orderId] = replaceMsg.TransactionId;
		_orderBalances[orderId] = replaceMsg.Volume;
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		if (cancelMsg.OrderId is not long orderId)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		await _httpClient.CancelOrder(new CancelOrderRequest
		{
			OrderId = orderId,
			ClOrdId = cancelMsg.TransactionId.ToString(CultureInfo.InvariantCulture),
			IsAutomated = true,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		if (!lookupMsg.IsSubscribe)
			return;

		var accounts = await EnsureAccounts(cancellationToken);
		foreach (var account in accounts)
		{
			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = account.Name,
				BoardCode = _boardCode,
				OriginalTransactionId = lookupMsg.TransactionId,
			}, cancellationToken);
		}

		foreach (var position in await _httpClient.GetPositions(cancellationToken))
			await ProcessPosition(position, cancellationToken);
		foreach (var balance in await _httpClient.GetCashBalances(cancellationToken))
			await ProcessCashBalance(balance, cancellationToken);

		if (!lookupMsg.IsHistoryOnly())
			await _accountSocket.Synchronize(_httpClient.UserId, accounts.Select(a => a.Id), cancellationToken);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		if (!statusMsg.IsSubscribe)
			return;

		var accounts = await EnsureAccounts(cancellationToken);
		var versions = await _httpClient.GetOrderVersions(cancellationToken);
		foreach (var version in versions)
			_orderVersions[version.OrderId] = version;

		foreach (var order in await _httpClient.GetOrders(cancellationToken))
		{
			_orders[order.Id] = order;
			await ProcessOrder(order, statusMsg.TransactionId, cancellationToken);
		}

		foreach (var fill in await _httpClient.GetFills(cancellationToken))
			await ProcessFill(fill, statusMsg.TransactionId, cancellationToken);

		if (!statusMsg.IsHistoryOnly())
			await _accountSocket.Synchronize(_httpClient.UserId, accounts.Select(a => a.Id), cancellationToken);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private async ValueTask OnEntityReceived(string entityType, TradovateEntity entity, CancellationToken cancellationToken)
	{
		switch (entityType)
		{
			case "order":
				var order = entity.ToOrder();
				_orders[order.Id] = order;
				await ProcessOrder(order, 0, cancellationToken);
				break;
			case "orderVersion":
				var version = entity.ToOrderVersion();
				_orderVersions[version.OrderId] = version;
				if (_orders.TryGetValue(version.OrderId, out var versionedOrder))
					await ProcessOrder(versionedOrder, 0, cancellationToken);
				break;
			case "fill":
				await ProcessFill(entity.ToFill(), 0, cancellationToken);
				break;
			case "position":
				await ProcessPosition(entity.ToPosition(), cancellationToken);
				break;
			case "cashBalance":
				await ProcessCashBalance(entity.ToCashBalance(), cancellationToken);
				break;
		}
	}

	private async ValueTask ProcessOrder(TradovateOrder order, long originalTransactionId, CancellationToken cancellationToken)
	{
		_orderVersions.TryGetValue(order.Id, out var version);
		var transactionId = _orderTransactions.TryGetValue2(order.Id) ?? order.Id;
		if (version != null && !_orderBalances.ContainsKey(order.Id))
			_orderBalances[order.Id] = version.OrderQty;

		var state = order.OrdStatus.ToOrderState();
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId == 0 ? transactionId : originalTransactionId,
			TransactionId = originalTransactionId == 0 ? 0 : transactionId,
			OrderId = order.Id,
			ServerTime = order.Timestamp.ToUtc(),
			SecurityId = order.ContractId is long contractId ? await GetSecurityId(contractId, cancellationToken) : default,
			PortfolioName = _accounts.TryGetValue(order.AccountId, out var account) ? account.Name : order.AccountId.ToString(CultureInfo.InvariantCulture),
			Side = order.Action.ToSide(),
			OrderState = state,
			OrderType = version?.OrderType.ToOrderType(),
			OrderPrice = version?.Price ?? 0,
			OrderVolume = version?.OrderQty,
			Balance = _orderBalances.TryGetValue2(order.Id),
			TimeInForce = version?.TimeInForce.ToTimeInForce(),
			ExpiryDate = version?.ExpireTime.ToUtc(),
			Condition = version?.StopPrice is decimal stopPrice ? new TradovateOrderCondition { StopPrice = stopPrice } : null,
			Error = state == OrderStates.Failed ? new InvalidOperationException(version?.Text ?? order.OrdStatus.ToString()) : null,
		}, cancellationToken);
	}

	private async ValueTask ProcessFill(TradovateFill fill, long originalTransactionId, CancellationToken cancellationToken)
	{
		if (_fills.Contains(fill.Id))
			return;
		_fills.Add(fill.Id);

		var transactionId = _orderTransactions.TryGetValue2(fill.OrderId) ?? fill.OrderId;
		if (_orderBalances.TryGetValue(fill.OrderId, out var balance))
			_orderBalances[fill.OrderId] = (balance - fill.Qty).Max(0);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originalTransactionId == 0 ? transactionId : originalTransactionId,
			OrderId = fill.OrderId,
			TradeId = fill.Id,
			SecurityId = await GetSecurityId(fill.ContractId, cancellationToken),
			ServerTime = fill.Timestamp.ToUtc(),
			Side = fill.Action.ToSide(),
			TradePrice = fill.Price,
			TradeVolume = fill.Qty,
		}, cancellationToken);
	}

	private async ValueTask ProcessPosition(TradovatePosition position, CancellationToken cancellationToken)
	{
		await SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _accounts.TryGetValue(position.AccountId, out var account) ? account.Name : position.AccountId.ToString(CultureInfo.InvariantCulture),
			SecurityId = await GetSecurityId(position.ContractId, cancellationToken),
			ServerTime = position.Timestamp.ToUtc(),
		}
		.TryAdd(PositionChangeTypes.CurrentValue, position.NetPos, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, position.NetPrice, true), cancellationToken);
	}

	private ValueTask ProcessCashBalance(TradovateCashBalance balance, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _accounts.TryGetValue(balance.AccountId, out var account) ? account.Name : balance.AccountId.ToString(CultureInfo.InvariantCulture),
			SecurityId = SecurityId.Money,
			ServerTime = balance.Timestamp.ToUtc(),
		}
		.TryAdd(PositionChangeTypes.CurrentValue, balance.Amount, true)
		.TryAdd(PositionChangeTypes.RealizedPnL, balance.RealizedPnL), cancellationToken);
}
