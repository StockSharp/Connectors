namespace StockSharp.Lime;

public partial class LimeMessageAdapter
{
	private readonly SynchronizedDictionary<string, LimeAccount> _accounts = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, long> _orderTransactions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, string> _orderAccounts = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _trades = new(StringComparer.OrdinalIgnoreCase);

	private void ClearState()
	{
		_accounts.Clear();
		_orderTransactions.Clear();
		_orderAccounts.Clear();
		_trades.Clear();
	}

	private async Task<LimeAccount[]> EnsureAccounts(CancellationToken cancellationToken)
	{
		var accounts = await _httpClient.GetAccounts(cancellationToken) ?? [];
		foreach (var account in accounts)
			_accounts[account.AccountNumber] = account;
		return accounts;
	}

	private async Task<LimeAccount> ResolveAccount(string accountNumber, CancellationToken cancellationToken)
	{
		if (!accountNumber.IsEmpty() && _accounts.TryGetValue(accountNumber, out var account))
			return account;

		var accounts = await EnsureAccounts(cancellationToken);
		if (!accountNumber.IsEmpty())
			return accounts.FirstOrDefault(item => item.AccountNumber.EqualsIgnoreCase(accountNumber))
				?? throw new InvalidOperationException($"Lime account '{accountNumber}' was not found.");
		if (accounts.Length == 1)
			return accounts[0];
		throw new InvalidOperationException("A Lime account must be specified when the login owns multiple accounts.");
	}

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		if (regMsg.Volume != decimal.Truncate(regMsg.Volume) || regMsg.Volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg), regMsg.Volume, "Lime requires a positive integer order quantity.");

		var account = await ResolveAccount(regMsg.PortfolioName, cancellationToken);
		if (account.Restriction.EqualsIgnoreCase("disabled") || account.Restriction.EqualsIgnoreCase("closed"))
			throw new InvalidOperationException(account.RestrictionReason.IsEmpty($"Lime account '{account.AccountNumber}' is {account.Restriction}."));

		var orderType = (regMsg.OrderType ?? OrderTypes.Limit).ToNative();
		var response = await _httpClient.PlaceOrder(new LimeOrderRequest
		{
			AccountNumber = account.AccountNumber,
			Symbol = regMsg.SecurityId.SecurityCode,
			Quantity = regMsg.Volume,
			ClientOrderId = regMsg.TransactionId.ToString(CultureInfo.InvariantCulture),
			Price = orderType == LimeOrderTypes.Limit ? regMsg.Price : null,
			TimeInForce = (regMsg.TimeInForce ?? TimeInForce.PutInQueue).ToNative(),
			OrderType = orderType,
			Side = regMsg.Side.ToNative(),
		}, cancellationToken);

		if (response == null || !response.IsSuccess || response.Data.IsEmpty())
			throw new InvalidOperationException("Lime did not accept the order request.");

		_orderTransactions[response.Data] = regMsg.TransactionId;
		_orderAccounts[response.Data] = account.AccountNumber;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			TransactionId = regMsg.TransactionId,
			OrderStringId = response.Data,
			PortfolioName = account.AccountNumber,
			SecurityId = regMsg.SecurityId,
			Side = regMsg.Side,
			OrderState = OrderStates.Pending,
			OrderType = regMsg.OrderType,
			OrderPrice = regMsg.Price,
			OrderVolume = regMsg.Volume,
			Balance = regMsg.Volume,
			TimeInForce = regMsg.TimeInForce,
			ServerTime = DateTime.UtcNow,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var orderId = cancelMsg.OrderStringId;
		if (orderId.IsEmpty() && cancelMsg.OrderId is long numericOrderId)
			orderId = numericOrderId.ToString(CultureInfo.InvariantCulture);
		if (orderId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		var response = await _httpClient.CancelOrder(orderId, new LimeCancelOrderRequest(), cancellationToken);
		if (response == null || !response.IsSuccess)
			throw new InvalidOperationException($"Lime did not accept the cancellation request for order '{orderId}'.");

		var order = await _httpClient.GetOrder(orderId, cancellationToken);
		await ProcessOrder(order, cancelMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		if (!lookupMsg.IsSubscribe)
			return;

		foreach (var account in await EnsureAccounts(cancellationToken))
		{
			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = account.AccountNumber,
				BoardCode = _boardCode,
				OriginalTransactionId = lookupMsg.TransactionId,
			}, cancellationToken);
			await ProcessBalance(account, lookupMsg.TransactionId, cancellationToken);
			foreach (var position in await _httpClient.GetPositions(account.AccountNumber, cancellationToken) ?? [])
				await ProcessPosition(account.AccountNumber, position, lookupMsg.TransactionId, cancellationToken);

			if (!lookupMsg.IsHistoryOnly() && _accountSocket != null)
			{
				await _accountSocket.Subscribe(LimeFeedActions.SubscribeBalance, account.AccountNumber, cancellationToken);
				await _accountSocket.Subscribe(LimeFeedActions.SubscribePositions, account.AccountNumber, cancellationToken);
			}
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		if (!statusMsg.IsSubscribe)
			return;

		var accounts = statusMsg.PortfolioName.IsEmpty()
			? await EnsureAccounts(cancellationToken)
			: [await ResolveAccount(statusMsg.PortfolioName, cancellationToken)];

		foreach (var account in accounts)
		{
			foreach (var order in await _httpClient.GetActiveOrders(account.AccountNumber, cancellationToken) ?? [])
				await ProcessOrder(order, statusMsg.TransactionId, cancellationToken);

			var trades = await _httpClient.GetTrades(account.AccountNumber, DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), cancellationToken);
			foreach (var trade in trades?.Trades ?? [])
			{
				trade.AccountNumber = account.AccountNumber;
				await ProcessTrade(trade, statusMsg.TransactionId, cancellationToken);
			}

			if (!statusMsg.IsHistoryOnly() && _accountSocket != null)
			{
				await _accountSocket.Subscribe(LimeFeedActions.SubscribeOrders, account.AccountNumber, cancellationToken);
				await _accountSocket.Subscribe(LimeFeedActions.SubscribeTrades, account.AccountNumber, cancellationToken);
			}
		}

		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private ValueTask OnBalanceReceived(LimeAccount account, CancellationToken cancellationToken)
	{
		_accounts[account.AccountNumber] = account;
		return ProcessBalance(account, 0, cancellationToken);
	}

	private async ValueTask OnPositionsReceived(LimeAccountPositions accountPositions, CancellationToken cancellationToken)
	{
		foreach (var position in accountPositions.Positions ?? [])
			await ProcessPosition(accountPositions.Account, position, 0, cancellationToken);
	}

	private ValueTask OnOrderReceived(LimeOrder order, CancellationToken cancellationToken)
		=> ProcessOrder(order, 0, cancellationToken);

	private ValueTask OnTradeReceived(LimeTrade trade, CancellationToken cancellationToken)
		=> ProcessTrade(trade, 0, cancellationToken);

	private ValueTask ProcessBalance(LimeAccount account, long originalTransactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = account.AccountNumber,
			SecurityId = SecurityId.Money,
			OriginalTransactionId = originalTransactionId,
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, account.Cash, true)
		.TryAdd(PositionChangeTypes.BuyOrdersMargin, account.MarginBuyingPower, true)
		.TryAdd(PositionChangeTypes.BeginValue, account.AccountValueTotal, true)
		.TryAdd(PositionChangeTypes.BlockedValue, account.UnsettledCash, true)
		.TryAdd(PositionChangeTypes.Leverage, account.MarginType.EqualsIgnoreCase("marginx2") ? 2m : account.MarginType.EqualsIgnoreCase("marginx1") ? 1m : null)
		.TryAdd(PositionChangeTypes.Currency, CurrencyTypes.USD), cancellationToken);

	private ValueTask ProcessPosition(string accountNumber, LimePosition position, long originalTransactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = accountNumber,
			SecurityId = ToSecurityId(position.Symbol),
			OriginalTransactionId = originalTransactionId,
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, position.Quantity, true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.AverageOpenPrice, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, position.CurrentPrice, true), cancellationToken);

	private ValueTask ProcessOrder(LimeOrder order, long originalTransactionId, CancellationToken cancellationToken)
	{
		if (order == null || order.ClientId.IsEmpty())
			return default;

		if (!order.AccountNumber.IsEmpty())
			_orderAccounts[order.ClientId] = order.AccountNumber;
		var transactionId = _orderTransactions.TryGetValue(order.ClientId, out var knownTransactionId) ? knownTransactionId : 0;
		if (transactionId == 0 && long.TryParse(order.ClientOrderId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTransactionId))
			transactionId = parsedTransactionId;
		if (transactionId != 0)
			_orderTransactions[order.ClientId] = transactionId;

		var state = order.OrderStatus.ToOrderState();
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId == 0 ? transactionId : originalTransactionId,
			TransactionId = transactionId,
			OrderStringId = order.ClientId,
			PortfolioName = order.AccountNumber,
			SecurityId = ToSecurityId(order.Symbol),
			Side = order.OrderSide.ToSide(),
			OrderState = state,
			OrderType = order.OrderType.ToOrderType(),
			OrderPrice = order.Price,
			OrderVolume = order.Quantity,
			Balance = (order.Quantity - order.ExecutedQuantity).Max(0),
			TimeInForce = order.TimeInForce.ToTimeInForce(),
			ServerTime = (order.TransactionTimestamp ?? order.ExecutedTimestamp) is long timestamp ? timestamp.ToDateTime() : DateTime.UtcNow,
			Error = state == OrderStates.Failed ? new InvalidOperationException($"Lime order entered state {order.OrderStatus}.") : null,
		}, cancellationToken);
	}

	private ValueTask ProcessTrade(LimeTrade trade, long originalTransactionId, CancellationToken cancellationToken)
	{
		if (trade == null || trade.TradeId.IsEmpty())
			return default;
		var key = $"{trade.AccountNumber}:{trade.TradeId}";
		if (_trades.Contains(key))
			return default;
		_trades.Add(key);

		var timestamp = trade.TransactionTimestamp > 0 ? trade.TransactionTimestamp : trade.Timestamp;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originalTransactionId,
			TradeStringId = trade.TradeId,
			PortfolioName = trade.AccountNumber,
			SecurityId = ToSecurityId(trade.Symbol),
			Side = trade.Side.ToSide(),
			TradePrice = trade.Price,
			TradeVolume = Math.Abs(trade.Quantity),
			ServerTime = timestamp > 0 ? timestamp.ToDateTime() : DateTime.UtcNow,
		}, cancellationToken);
	}
}
