namespace StockSharp.RakutenRss;

[SupportedOSPlatform("windows")]
public partial class RakutenRssMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage message,
		CancellationToken cancellationToken)
	{
		EnsureConnected();
		ValidatePortfolio(message.PortfolioName);
		if (message.Volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(message.Volume), message.Volume,
				"MARKETSPEED II RSS requires a positive quantity.");
		if (message.OrderType is not null and not OrderTypes.Limit and not OrderTypes.Market)
			throw new NotSupportedException(
				$"MARKETSPEED II RSS does not support {message.OrderType} through this adapter.");
		if (message.OrderType != OrderTypes.Market && message.Price <= 0)
			throw new ArgumentOutOfRangeException(nameof(message.Price), message.Price,
				"A limit order requires a positive price.");
		var securityId = Normalize(message.SecurityId);
		var condition = message.Condition as RakutenRssOrderCondition ?? new();
		var route = condition.Route;
		if (securityId.IsDerivative() && route == RakutenRssOrderRoutes.Cash)
			route = RakutenRssOrderRoutes.DerivativeOpen;
		if (!securityId.IsDerivative() && route is RakutenRssOrderRoutes.DerivativeOpen or
			RakutenRssOrderRoutes.DerivativeClose)
			throw new InvalidOperationException("A derivative route requires an OSE derivative security.");
		var requestId = NextRequestId(message.TransactionId);
		try
		{
			await _client.PlaceOrder(new()
			{
				RequestId = requestId,
				SecurityCode = securityId.ToNativeCode(),
				Side = message.Side,
				OrderType = message.OrderType ?? OrderTypes.Limit,
				Quantity = message.Volume,
				Price = message.Price,
				Route = route,
				Execution = condition.Execution,
				AccountType = condition.AccountType,
				MarginType = condition.MarginType,
				FillCondition = condition.FillCondition,
				DerivativeTime = condition.DerivativeTime,
				UseSor = condition.UseSor,
				ValidTill = condition.ValidTill,
				OpenDate = condition.OpenDate,
				OpenPrice = condition.OpenPrice,
			});
		}
		catch
		{
			_requestTransactions.Remove(requestId);
			throw;
		}
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = message.TransactionId,
			TransactionId = message.TransactionId,
			PortfolioName = PortfolioName,
			SecurityId = securityId,
			Side = message.Side,
			OrderType = message.OrderType ?? OrderTypes.Limit,
			OrderPrice = message.Price,
			OrderVolume = message.Volume,
			Balance = message.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = CurrentTime,
			Condition = condition,
		}, cancellationToken);
		_lastAccountPoll = default;
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage message,
		CancellationToken cancellationToken)
	{
		EnsureConnected();
		ValidatePortfolio(message.PortfolioName);
		if (message.Volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(message.Volume));
		var orderId = await ResolveOrderId(message.OldOrderStringId,
			message.OriginalTransactionId);
		var condition = message.Condition as RakutenRssOrderCondition ?? new();
		var requestId = NextRequestId(message.TransactionId);
		try
		{
			await _client.ReplaceOrder(new()
			{
				RequestId = requestId,
				OrderId = orderId,
				OrderType = message.OrderType ?? OrderTypes.Limit,
				Quantity = message.Volume,
				Price = message.Price,
				IsDerivative = _orderDerivatives.TryGetValue(orderId, out var derivative) && derivative ||
					message.SecurityId.IsDerivative(),
				Execution = condition.Execution,
				DerivativeTime = condition.DerivativeTime,
				ValidTill = condition.ValidTill,
			});
		}
		catch
		{
			_requestTransactions.Remove(requestId);
			throw;
		}
		await SendCommandPending(message.TransactionId, orderId, message.SecurityId,
			message.Volume, message.Price, cancellationToken);
		_lastAccountPoll = default;
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage message,
		CancellationToken cancellationToken)
	{
		EnsureConnected();
		ValidatePortfolio(message.PortfolioName);
		var orderId = await ResolveOrderId(message.OrderStringId,
			message.OriginalTransactionId);
		var requestId = NextRequestId(message.TransactionId);
		try
		{
			await _client.CancelOrder(new()
			{
				RequestId = requestId,
				OrderId = orderId,
				IsDerivative = _orderDerivatives.TryGetValue(orderId, out var derivative) && derivative ||
					message.SecurityId.IsDerivative(),
			});
		}
		catch
		{
			_requestTransactions.Remove(requestId);
			throw;
		}
		await SendCommandPending(message.TransactionId, orderId, message.SecurityId,
			null, null, cancellationToken);
		_lastAccountPoll = default;
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		EnsureConnected();
		if (!message.IsSubscribe)
		{
			if (_orderStatusSubscriptionId == message.OriginalTransactionId)
				_orderStatusSubscriptionId = 0;
			return;
		}
		ValidatePortfolio(message.PortfolioName);
		await SendOrderSnapshot(message.TransactionId, true, cancellationToken);
		if (message.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
		else
		{
			_orderStatusSubscriptionId = message.TransactionId;
			await SendSubscriptionResultAsync(message, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		EnsureConnected();
		if (!message.IsSubscribe)
		{
			if (_portfolioSubscriptionId == message.OriginalTransactionId)
				_portfolioSubscriptionId = 0;
			return;
		}
		ValidatePortfolio(message.PortfolioName);
		await SendPortfolioSnapshot(message.TransactionId, true, cancellationToken);
		if (message.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
		else
		{
			_portfolioSubscriptionId = message.TransactionId;
			await SendSubscriptionResultAsync(message, cancellationToken);
		}
	}

	private async Task SendOrderSnapshot(long originalTransactionId, bool isLookup,
		CancellationToken cancellationToken)
	{
		await RefreshOrderIds();
		foreach (var order in (await _client.ReadOrders()).OrderBy(item => item.Time))
			await ProcessOrder(order, originalTransactionId, isLookup, cancellationToken);
		foreach (var trade in (await _client.ReadExecutions()).OrderBy(item => item.Time))
			await ProcessTrade(trade, originalTransactionId, isLookup, cancellationToken);
	}

	private async Task RefreshOrderIds()
	{
		foreach (var row in await _client.ReadOrderIds())
		{
			if (row.OrderId.IsEmpty() || !_requestTransactions.TryGetValue(row.RequestId,
				out var transactionId))
				continue;
			_transactionOrders[transactionId] = row.OrderId;
			if (row.Function?.Contains("Cancel", StringComparison.OrdinalIgnoreCase) == true)
				_cancelTransactions[row.OrderId] = transactionId;
			else if (row.Function?.Contains("Modify", StringComparison.OrdinalIgnoreCase) != true)
				_orderTransactions[row.OrderId] = transactionId;
			_orderDerivatives[row.OrderId] = row.Function?.Contains("FOP",
				StringComparison.OrdinalIgnoreCase) == true;
		}
	}

	private async ValueTask ProcessOrder(RakutenRssOrderRow order, long originalTransactionId,
		bool isLookup, CancellationToken cancellationToken)
	{
		if (order?.OrderId.IsEmpty() != false || order.Code.IsEmpty())
			return;
		_orderDerivatives[order.OrderId] = order.IsDerivative;
		var signature = $"{order.Status}|{order.Quantity}|{order.FilledQuantity}|" +
			$"{order.Price}|{order.Time:O}|{order.Error}";
		if (!isLookup && _orderSignatures.TryGetValue(order.OrderId, out var previous) &&
			previous == signature)
			return;
		_orderSignatures[order.OrderId] = signature;
		var transactionId = _orderTransactions.TryGetValue(order.OrderId,
			out var knownTransactionId) ? knownTransactionId : 0;
		var state = order.Status.ToOrderState();
		var balance = state is OrderStates.Done or OrderStates.Failed ? 0
			: Math.Max(0, order.Quantity - order.FilledQuantity);
		var originId = isLookup ? originalTransactionId
			: transactionId != 0 ? transactionId : originalTransactionId;
		if (!isLookup && state == OrderStates.Done &&
			_cancelTransactions.TryGetValue(order.OrderId, out var cancelTransactionId))
			originId = cancelTransactionId;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originId,
			TransactionId = isLookup ? transactionId : 0,
			OrderStringId = order.OrderId,
			PortfolioName = PortfolioName,
			SecurityId = order.Code.ToSecurityId(order.Market, order.IsDerivative),
			Side = order.Side.ToSide(),
			OrderType = order.Price <= 0 ? OrderTypes.Market : OrderTypes.Limit,
			OrderPrice = order.Price,
			OrderVolume = order.Quantity,
			Balance = balance,
			OrderState = state,
			ServerTime = order.Time,
			Error = state == OrderStates.Failed
				? new InvalidOperationException(order.Error.IsEmpty(order.Status)) : null,
		}, cancellationToken);
		if (state is OrderStates.Done or OrderStates.Failed)
			_cancelTransactions.Remove(order.OrderId);
	}

	private async ValueTask ProcessTrade(RakutenRssExecutionRow trade,
		long originalTransactionId, bool isLookup, CancellationToken cancellationToken)
	{
		if (trade == null || trade.Code.IsEmpty() || trade.Quantity <= 0 || trade.Price <= 0)
			return;
		var signature = $"{trade.Time:O}|{trade.Code}|{trade.Side}|{trade.Quantity}|{trade.Price}";
		var known = _tradeSignatures.Contains(signature);
		_tradeSignatures.Add(signature);
		if (known && !isLookup)
			return;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originalTransactionId,
			TradeStringId = signature,
			PortfolioName = PortfolioName,
			SecurityId = trade.Code.ToSecurityId(trade.Market, trade.IsDerivative),
			Side = trade.Side.ToSide(),
			TradePrice = trade.Price,
			TradeVolume = trade.Quantity,
			ServerTime = trade.Time,
		}, cancellationToken);
	}

	private async Task SendPortfolioSnapshot(long originalTransactionId, bool isLookup,
		CancellationToken cancellationToken)
	{
		var portfolio = await _client.ReadPortfolio();
		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = PortfolioName,
			BoardCode = BoardCodes.Tse,
		}, cancellationToken);
		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = PortfolioName,
			SecurityId = SecurityId.Money,
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, portfolio.BuyingPower, true)
		.TryAdd(PositionChangeTypes.VariationMargin, portfolio.MarginAvailable)
		.TryAdd(PositionChangeTypes.Leverage, portfolio.MarginRatio)
		.TryAdd(PositionChangeTypes.Currency, CurrencyTypes.JPY), cancellationToken);
		var previous = _positionIds.ToArray();
		_positionIds.Clear();
		foreach (var position in portfolio.Positions ?? [])
		{
			var securityId = position.Code.ToSecurityId(position.Market, position.IsDerivative);
			var key = $"{securityId.SecurityCode}@{securityId.BoardCode}:{position.Side}";
			_positionIds[key] = securityId;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = PortfolioName,
				SecurityId = securityId,
				Side = position.Side.IsEmpty() ? null : position.Side.ToSide(),
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, position.Quantity, true)
			.TryAdd(PositionChangeTypes.BlockedValue, position.BlockedQuantity)
			.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice)
			.TryAdd(PositionChangeTypes.CurrentPrice, position.CurrentPrice)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnL)
			.TryAdd(PositionChangeTypes.Currency, CurrencyTypes.JPY), cancellationToken);
		}
		if (!isLookup)
		{
			foreach (var pair in previous.Where(pair => !_positionIds.ContainsKey(pair.Key)))
			{
				await SendOutMessageAsync(new PositionChangeMessage
				{
					OriginalTransactionId = originalTransactionId,
					PortfolioName = PortfolioName,
					SecurityId = pair.Value,
					ServerTime = CurrentTime,
				}.TryAdd(PositionChangeTypes.CurrentValue, 0m, true), cancellationToken);
			}
		}
	}

	private async Task<string> ResolveOrderId(string orderId, long transactionId)
	{
		if (!orderId.IsEmpty())
			return orderId;
		if (_transactionOrders.TryGetValue(transactionId, out orderId))
			return orderId;
		await RefreshOrderIds();
		return _transactionOrders.TryGetValue(transactionId, out orderId)
			? orderId : throw new InvalidOperationException(
				$"The native order number for transaction {transactionId} is not available yet.");
	}

	private int NextRequestId(long transactionId)
	{
		var id = Interlocked.Increment(ref _requestId);
		if (id <= 0)
			throw new InvalidOperationException("MARKETSPEED II RSS request ID space is exhausted.");
		_requestTransactions[id] = transactionId;
		return id;
	}

	private string ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() && !portfolioName.EqualsIgnoreCase(PortfolioName))
			throw new InvalidOperationException(
				$"Portfolio '{portfolioName}' is not available in the MARKETSPEED II RSS session.");
		return PortfolioName;
	}

	private ValueTask SendCommandPending(long transactionId, string orderId,
		SecurityId securityId, decimal? volume, decimal? price,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = transactionId,
			OrderStringId = orderId,
			PortfolioName = PortfolioName,
			SecurityId = securityId,
			OrderVolume = volume,
			OrderPrice = price ?? 0,
			OrderState = OrderStates.Pending,
			ServerTime = CurrentTime,
		}, cancellationToken);
}
