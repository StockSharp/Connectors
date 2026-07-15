namespace StockSharp.TradeZero;

public partial class TradeZeroMessageAdapter
{
	private readonly SynchronizedDictionary<string, TradeZeroAccount> _accounts = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, long> _orderTransactions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, string> _orderAccounts = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, decimal> _orderExecutions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, TradeZeroRoute[]> _routes = new(StringComparer.OrdinalIgnoreCase);

	private void ClearState()
	{
		_accounts.Clear();
		_orderTransactions.Clear();
		_orderAccounts.Clear();
		_orderExecutions.Clear();
		_routes.Clear();
	}

	private async Task<TradeZeroAccount[]> EnsureAccounts(CancellationToken cancellationToken)
	{
		var accounts = await _httpClient.GetAccounts(cancellationToken);
		foreach (var account in accounts)
			_accounts[account.Account] = account;
		return accounts;
	}

	private async Task<TradeZeroAccount> ResolveAccount(string accountId, CancellationToken cancellationToken)
	{
		if (!accountId.IsEmpty() && _accounts.TryGetValue(accountId, out var account))
			return account;

		var accounts = await EnsureAccounts(cancellationToken);
		if (!accountId.IsEmpty())
			return accounts.FirstOrDefault(item => item.Account.EqualsIgnoreCase(accountId))
				?? throw new InvalidOperationException($"TradeZero account '{accountId}' was not found.");
		if (accounts.Length == 1)
			return accounts[0];
		throw new InvalidOperationException("A TradeZero account must be specified when the API key owns multiple accounts.");
	}

	private TradeZeroOrderRequest CreateOrderRequest(OrderRegisterMessage message)
	{
		if (message.Volume != decimal.Truncate(message.Volume))
			throw new ArgumentOutOfRangeException(nameof(message), message.Volume, "TradeZero does not support fractional order quantities.");

		var condition = message.Condition as TradeZeroOrderCondition;
		var nativeType = message.OrderType.ToNative(condition?.StopPrice);
		return new()
		{
			ClientOrderId = message.TransactionId.ToString(CultureInfo.InvariantCulture),
			Symbol = message.SecurityId.SecurityCode,
			TimeInForce = message.TimeInForce.ToNative(),
			OrderType = nativeType,
			OrderQuantity = checked((int)message.Volume),
			SecurityType = message.SecurityType.ToNative(),
			Side = message.Side.ToNative(),
			OpenClose = message.PositionEffect == OrderPositionEffects.CloseOnly ? TradeZeroOpenCloseTypes.Close : TradeZeroOpenCloseTypes.Open,
			LimitPrice = nativeType is TradeZeroOrderTypes.Limit or TradeZeroOrderTypes.StopLimit ? message.Price : null,
			StopPrice = condition?.StopPrice,
			Route = condition?.Route.IsEmpty(DefaultRoute),
		};
	}

	private async Task ResolveRoute(TradeZeroAccount account, TradeZeroOrderRequest request, CancellationToken cancellationToken)
	{
		if (account.AccountType.EqualsIgnoreCase("Paper") && request.Route.IsEmpty())
			return;

		if (!_routes.TryGetValue(account.Account, out var routes))
		{
			routes = await _httpClient.GetRoutes(account.Account, cancellationToken);
			_routes[account.Account] = routes;
		}

		var route = routes.FirstOrDefault(item =>
			(request.Route.IsEmpty() || item.RouteName.EqualsIgnoreCase(request.Route)) &&
			(item.OrderTypes ?? []).Contains(request.OrderType) &&
			(item.SecurityTypes ?? []).Contains(request.SecurityType) &&
			(item.TimesInForce ?? []).Contains(request.TimeInForce));
		if (route == null)
			throw new InvalidOperationException(request.Route.IsEmpty()
				? $"TradeZero account '{account.Account}' has no route for {request.SecurityType}, {request.OrderType}, {request.TimeInForce}."
				: $"TradeZero route '{request.Route}' does not support {request.SecurityType}, {request.OrderType}, {request.TimeInForce}.");
		request.Route = route.RouteName;
	}

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var account = await ResolveAccount(regMsg.PortfolioName, cancellationToken);
		if (account.AccountStatus != TradeZeroAccountStatuses.Active)
			throw new InvalidOperationException($"TradeZero account '{account.Account}' is not active.");

		var request = CreateOrderRequest(regMsg);
		await ResolveRoute(account, request, cancellationToken);
		var order = await _httpClient.PlaceOrder(account.Account, request, cancellationToken);
		var clientOrderId = order.GetClientOrderId().IsEmpty(regMsg.TransactionId.ToString(CultureInfo.InvariantCulture));
		_orderTransactions[clientOrderId] = regMsg.TransactionId;
		_orderAccounts[clientOrderId] = account.Account;
		await ProcessOrder(order, regMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		var oldClientOrderId = replaceMsg.OldOrderStringId;
		if (oldClientOrderId.IsEmpty() && replaceMsg.OldOrderId is long oldOrderId)
			oldClientOrderId = oldOrderId.ToString(CultureInfo.InvariantCulture);
		if (oldClientOrderId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(replaceMsg.OriginalTransactionId));

		var account = await ResolveAccount(replaceMsg.PortfolioName, cancellationToken);
		var canceled = await _httpClient.CancelOrder(account.Account, oldClientOrderId, cancellationToken);
		for (var attempt = 0; canceled.GetStatus() is not TradeZeroOrderStatuses.Canceled && attempt < 10; attempt++)
		{
			await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
			canceled = await _httpClient.GetOrder(account.Account, oldClientOrderId, cancellationToken);
		}
		if (canceled.GetStatus() is not TradeZeroOrderStatuses.Canceled)
			throw new InvalidOperationException($"TradeZero order '{oldClientOrderId}' was not canceled before replacement.");

		await ProcessOrder(canceled, replaceMsg.OriginalTransactionId, cancellationToken);
		var request = CreateOrderRequest(replaceMsg);
		await ResolveRoute(account, request, cancellationToken);
		var replacement = await _httpClient.PlaceOrder(account.Account, request, cancellationToken);
		var newClientOrderId = replacement.GetClientOrderId().IsEmpty(replaceMsg.TransactionId.ToString(CultureInfo.InvariantCulture));
		_orderTransactions[newClientOrderId] = replaceMsg.TransactionId;
		_orderAccounts[newClientOrderId] = account.Account;
		await ProcessOrder(replacement, replaceMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var clientOrderId = cancelMsg.OrderStringId;
		if (clientOrderId.IsEmpty() && cancelMsg.OrderId is long orderId)
			clientOrderId = orderId.ToString(CultureInfo.InvariantCulture);
		if (clientOrderId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		var accountId = cancelMsg.PortfolioName;
		if (accountId.IsEmpty() && _orderAccounts.TryGetValue(clientOrderId, out var knownAccountId))
			accountId = knownAccountId;
		var account = await ResolveAccount(accountId, cancellationToken);
		var order = await _httpClient.CancelOrder(account.Account, clientOrderId, cancellationToken);
		await ProcessOrder(order, cancelMsg.TransactionId, cancellationToken);
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
				PortfolioName = account.Account,
				BoardCode = _boardCode,
				OriginalTransactionId = lookupMsg.TransactionId,
			}, cancellationToken);

			var pnl = await _httpClient.GetPnl(account.Account, cancellationToken);
			await ProcessBalance(account, pnl, lookupMsg.TransactionId, cancellationToken);
			foreach (var position in await _httpClient.GetPositions(account.Account, cancellationToken))
				await ProcessPosition(position, lookupMsg.TransactionId, cancellationToken);
			foreach (var positionPnl in pnl?.Pnl ?? pnl?.Positions ?? [])
				await ProcessPositionPnl(account.Account, positionPnl, lookupMsg.TransactionId, DateTime.UtcNow, cancellationToken);

			if (!lookupMsg.IsHistoryOnly())
			{
				await _portfolioSocket.Subscribe(account.Account, cancellationToken);
				await _pnlSocket.Subscribe(account.Account, cancellationToken);
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
			foreach (var order in await _httpClient.GetOrders(account.Account, cancellationToken))
				await ProcessOrder(order, statusMsg.TransactionId, cancellationToken);

			if (!statusMsg.IsHistoryOnly())
				await _portfolioSocket.Subscribe(account.Account, cancellationToken);
		}

		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private async ValueTask OnPortfolioReceived(TradeZeroPortfolioMessage message, CancellationToken cancellationToken)
	{
		if (message.Action != TradeZeroSocketActions.Update)
			return;

		if (message.Subscription == TradeZeroPortfolioSubscriptions.Order && message.Order != null)
			await ProcessOrder(message.Order, 0, cancellationToken);
		else if (message.Subscription == TradeZeroPortfolioSubscriptions.Position && message.Position != null)
			await ProcessPosition(message.Position, 0, cancellationToken);
	}

	private async ValueTask OnPnlReceived(string accountId, TradeZeroPnlMessage message, CancellationToken cancellationToken)
	{
		if (accountId.IsEmpty())
			return;

		var serverTime = message.Timestamp > 0 ? message.Timestamp.FromUnix(false) : DateTime.UtcNow;
		_accounts.TryGetValue(accountId, out var account);
		if (message.Target == TradeZeroPnlTargets.PnlReturn && message.PnlReturn != null)
		{
			await ProcessBalance(account, message.PnlReturn, 0, cancellationToken, serverTime);
			foreach (var position in message.PnlReturn.Positions ?? [])
				await ProcessPositionPnl(accountId, position, 0, serverTime, cancellationToken);
		}
		else if (message.Target == TradeZeroPnlTargets.AggCalcs && message.AggCalcs != null)
		{
			await ProcessBalance(account, message.AggCalcs, 0, cancellationToken, serverTime);
		}
		else if (message.Target == TradeZeroPnlTargets.Position && message.Position != null)
		{
			await ProcessPositionPnl(accountId, message.Position, 0, serverTime, cancellationToken);
		}
	}

	private async ValueTask ProcessOrder(TradeZeroOrder order, long originalTransactionId, CancellationToken cancellationToken)
	{
		var clientOrderId = order.GetClientOrderId();
		if (clientOrderId.IsEmpty())
			return;

		_orderAccounts.TryGetValue(clientOrderId, out var knownAccountId);
		var accountId = order.GetAccountId().IsEmpty(knownAccountId);
		if (!accountId.IsEmpty())
			_orderAccounts[clientOrderId] = accountId;
		var transactionId = _orderTransactions.TryGetValue(clientOrderId, out var knownTransactionId) ? knownTransactionId : 0;
		if (transactionId == 0 && long.TryParse(clientOrderId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTransactionId))
			transactionId = parsedTransactionId;

		var status = order.GetStatus();
		var state = status.ToOrderState();
		var securityType = order.SecurityType.ToSecurityType();
		var symbol = securityType == SecurityTypes.Option ? order.TradedSymbol.IsEmpty(order.Symbol) : order.Symbol;
		var serverTime = (order.LastUpdated ?? order.StartTime).ToUtc();
		TradeZeroOrderCondition condition = null;
		if ((order.PriceStop ?? 0) != 0 || !order.Route.IsEmpty())
			condition = new() { StopPrice = order.PriceStop is decimal stopPrice && stopPrice != 0 ? stopPrice : null, Route = order.Route };
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId == 0 ? transactionId : originalTransactionId,
			TransactionId = transactionId,
			OrderStringId = clientOrderId,
			PortfolioName = accountId,
			SecurityId = ToSecurityId(symbol),
			Side = order.Side.ToSide(),
			OrderState = state,
			OrderType = order.OrderType.ToOrderType(),
			OrderPrice = order.LimitPrice ?? 0,
			OrderVolume = order.OrderQuantity,
			Balance = order.LeavesQuantity ?? order.LvsQty,
			TimeInForce = order.TimeInForce.ToTimeInForce(),
			PositionEffect = order.OpenClose == TradeZeroOpenCloseTypes.Close ? OrderPositionEffects.CloseOnly : OrderPositionEffects.OpenOnly,
			Condition = condition,
			ServerTime = serverTime,
			Error = state == OrderStates.Failed ? new InvalidOperationException(order.Text.IsEmpty($"TradeZero order entered state {status}.")) : null,
		}, cancellationToken);

		var previousExecuted = _orderExecutions.TryGetValue(clientOrderId, out var knownExecuted) ? knownExecuted : 0;
		var executed = order.Executed ?? (previousExecuted + (order.GetLastQuantity() ?? 0));
		if (executed <= previousExecuted)
			return;
		_orderExecutions[clientOrderId] = executed;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originalTransactionId == 0 ? transactionId : originalTransactionId,
			OrderStringId = clientOrderId,
			TradeStringId = $"{clientOrderId}:{executed.ToString(CultureInfo.InvariantCulture)}",
			PortfolioName = accountId,
			SecurityId = ToSecurityId(symbol),
			Side = order.Side.ToSide(),
			TradePrice = order.LastPrice is decimal lastPrice && lastPrice != 0 ? lastPrice : order.PriceAvg,
			TradeVolume = executed - previousExecuted,
			ServerTime = serverTime,
		}, cancellationToken);
	}

	private ValueTask ProcessPosition(TradeZeroPosition position, long originalTransactionId, CancellationToken cancellationToken)
	{
		var securityType = position.SecurityType.ToSecurityType();
		var symbol = securityType == SecurityTypes.Option ? position.TradedSymbol.IsEmpty(position.Symbol) : position.Symbol;
		var quantity = Math.Abs(position.Shares ?? 0) * (position.Side == TradeZeroPositionSides.Short ? -1 : 1);
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = position.AccountId,
			SecurityId = ToSecurityId(symbol),
			OriginalTransactionId = originalTransactionId,
			ServerTime = position.UpdatedDate.ToUtc(),
		}
		.TryAdd(PositionChangeTypes.CurrentValue, quantity, true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.PriceAvg, true), cancellationToken);
	}

	private ValueTask ProcessBalance(TradeZeroAccount account, TradeZeroPnl pnl, long originalTransactionId, CancellationToken cancellationToken, DateTime? serverTime = null)
	{
		if (account == null)
			return default;

		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = account.Account,
			SecurityId = SecurityId.Money,
			OriginalTransactionId = originalTransactionId,
			ServerTime = serverTime.ToUtc(),
		}
		.TryAdd(PositionChangeTypes.CurrentValue, pnl?.AvailableCash ?? account.AvailableCash)
		.TryAdd(PositionChangeTypes.BuyOrdersMargin, account.BuyingPower)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, pnl?.TotalUnrealized)
		.TryAdd(PositionChangeTypes.RealizedPnL, pnl?.DayRealized ?? account.Realized)
		.TryAdd(PositionChangeTypes.VariationMargin, pnl?.DayPnl)
		.TryAdd(PositionChangeTypes.Leverage, pnl?.UsedLeverage ?? account.UsedLeverage)
		.TryAdd(PositionChangeTypes.Currency, CurrencyTypes.USD), cancellationToken);
	}

	private ValueTask ProcessPositionPnl(string accountId, TradeZeroPnlPosition position, long originalTransactionId, DateTime serverTime, CancellationToken cancellationToken)
	{
		var calculation = position.GetCalculation();
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = accountId,
			SecurityId = ToSecurityId(position.Symbol),
			OriginalTransactionId = originalTransactionId,
			ServerTime = serverTime.ToUtc(),
		}
		.TryAdd(PositionChangeTypes.UnrealizedPnL, calculation.UnrealizedPnL)
		.TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedPnl)
		.TryAdd(PositionChangeTypes.VariationMargin, calculation.DayUnrealizedPnL), cancellationToken);
	}
}
