namespace StockSharp.MatchTrader;

public partial class MatchTraderMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsureAccount(regMsg.PortfolioName);
		if (regMsg.Volume <= 0)
			throw new InvalidOperationException("Match-Trader order volume must be positive.");
		var instrument = ResolveInstrument(regMsg.SecurityId);
		var condition = regMsg.Condition as MatchTraderOrderCondition ?? new();
		MatchTraderOperationResponse result;

		if (condition.IsWithdraw)
		{
			var positionId = condition.PositionId.ThrowIfEmpty(nameof(condition.PositionId));
			var position = (await _client.GetPositions(cancellationToken))
				.FirstOrDefault(p => p.Id.EqualsIgnoreCase(positionId))
				?? throw new InvalidOperationException($"Match-Trader position '{positionId}' was not found.");
			var partial = regMsg.Volume < position.Volume;
			result = await _client.ClosePosition(new()
			{
				PositionId = position.Id,
				Instrument = position.Symbol,
				Side = position.Side,
				Volume = partial ? regMsg.Volume : position.Volume,
				IsMobile = false,
			}, partial, cancellationToken);
		}
		else if ((regMsg.OrderType ?? OrderTypes.Limit) == OrderTypes.Market)
		{
			result = await _client.OpenPosition(new()
			{
				Instrument = instrument.Symbol,
				Side = ToNative(regMsg.Side),
				Volume = regMsg.Volume,
				StopLoss = condition.StopLoss ?? 0,
				TakeProfit = condition.TakeProfit ?? 0,
				IsMobile = false,
			}, cancellationToken);
		}
		else
		{
			if (regMsg.Price <= 0)
				throw new InvalidOperationException("Match-Trader pending orders require a positive price.");
			result = await _client.CreatePendingOrder(new()
			{
				Instrument = instrument.Symbol,
				Side = ToNative(regMsg.Side),
				Volume = regMsg.Volume,
				Price = regMsg.Price,
				Type = (regMsg.OrderType ?? OrderTypes.Limit) == OrderTypes.Conditional
					? "STOP" : "LIMIT",
				StopLoss = condition.StopLoss ?? 0,
				TakeProfit = condition.TakeProfit ?? 0,
				IsMobile = false,
			}, cancellationToken);
		}

		EnsureSuccess(result);
		var orderId = result.OrderId.ThrowIfEmpty(nameof(result.OrderId));
		_orderTransactions[orderId] = regMsg.TransactionId;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			TransactionId = regMsg.TransactionId,
			OrderStringId = orderId,
			SecurityId = ToSecurityId(instrument.Symbol),
			PortfolioName = PortfolioName,
			Side = regMsg.Side,
			OrderType = regMsg.OrderType ?? OrderTypes.Limit,
			OrderPrice = regMsg.Price,
			OrderVolume = regMsg.Volume,
			Balance = regMsg.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = DateTime.UtcNow,
			Condition = condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsureAccount(cancelMsg.PortfolioName);
		var id = cancelMsg.OrderStringId;
		if (id.IsEmpty() && cancelMsg.OriginalTransactionId != 0)
			id = _orderTransactions.ToArray().FirstOrDefault(p =>
				p.Value == cancelMsg.OriginalTransactionId).Key;
		id = id.ThrowIfEmpty(nameof(cancelMsg.OrderStringId));
		var order = (await _client.GetOrders(cancellationToken))
			.FirstOrDefault(o => o.Id.EqualsIgnoreCase(id))
			?? throw new InvalidOperationException($"Match-Trader pending order '{id}' was not found.");
		var result = await _client.CancelOrder(new()
		{
			Instrument = order.Symbol,
			Id = order.Id,
			Side = order.Side,
			Type = order.Type,
			IsMobile = false,
		}, cancellationToken);
		EnsureSuccess(result);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = cancelMsg.TransactionId,
			OrderStringId = order.Id,
			SecurityId = ToSecurityId(order.Symbol),
			PortfolioName = PortfolioName,
			OrderState = OrderStates.Done,
			ServerTime = DateTime.UtcNow,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		if (!statusMsg.IsSubscribe)
		{
			if (_orderStatusSubscriptionId == statusMsg.OriginalTransactionId)
				_orderStatusSubscriptionId = 0;
			return;
		}
		EnsureAccount(statusMsg.PortfolioName);
		await SendOrders(statusMsg.TransactionId, true, cancellationToken, statusMsg.From, statusMsg.To);
		if (statusMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
		else
		{
			_orderStatusSubscriptionId = statusMsg.TransactionId;
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		if (!lookupMsg.IsSubscribe)
		{
			if (_portfolioSubscriptionId == lookupMsg.OriginalTransactionId)
				_portfolioSubscriptionId = 0;
			return;
		}
		EnsureAccount(lookupMsg.PortfolioName);
		await SendPortfolio(lookupMsg.TransactionId, cancellationToken);
		if (lookupMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
		else
		{
			_portfolioSubscriptionId = lookupMsg.TransactionId;
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		}
	}

	private Task SendOrders(long originalTransactionId, bool includeHistory,
		CancellationToken cancellationToken)
		=> SendOrders(originalTransactionId, includeHistory, cancellationToken, null, null);

	private async Task SendOrders(long originalTransactionId, bool includeHistory,
		CancellationToken cancellationToken, DateTime? from, DateTime? to)
	{
		foreach (var order in await _client.GetOrders(cancellationToken))
		{
			var transactionId = _orderTransactions.TryGetValue(order.Id, out var tracked) ? tracked : 0;
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				OriginalTransactionId = transactionId == 0 ? originalTransactionId : transactionId,
				TransactionId = transactionId,
				OrderStringId = order.Id,
				SecurityId = ToSecurityId(order.Symbol),
				PortfolioName = PortfolioName,
				Side = ToSide(order.Side),
				OrderType = order.Type.EqualsIgnoreCase("STOP") ? OrderTypes.Conditional : OrderTypes.Limit,
				OrderPrice = order.ActivationPrice,
				OrderVolume = order.Volume,
				Balance = order.Volume,
				OrderState = OrderStates.Active,
				ServerTime = AsUtc(order.CreationTime),
			}, cancellationToken);
		}

		foreach (var position in await _client.GetPositions(cancellationToken))
		{
			var transactionId = _orderTransactions.TryGetValue(position.Id, out var tracked) ? tracked : 0;
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				OriginalTransactionId = transactionId == 0 ? originalTransactionId : transactionId,
				TransactionId = transactionId,
				OrderStringId = position.Id,
				SecurityId = ToSecurityId(position.Symbol),
				PortfolioName = PortfolioName,
				Side = ToSide(position.Side),
				OrderType = OrderTypes.Market,
				OrderPrice = position.OpenPrice,
				OrderVolume = position.Volume,
				Balance = 0,
				AveragePrice = position.OpenPrice,
				OrderState = OrderStates.Done,
				ServerTime = PositionTime(position),
			}, cancellationToken);
		}

		if (!includeHistory)
			return;
		var historyTo = new DateTimeOffset((to ?? DateTime.UtcNow).ToUniversalTime());
		var historyFrom = new DateTimeOffset((from ?? historyTo.UtcDateTime.AddDays(-30)).ToUniversalTime());
		foreach (var operation in await _client.GetClosedPositions(historyFrom, historyTo,
			cancellationToken))
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = originalTransactionId,
				OrderStringId = operation.Id,
				TradeStringId = operation.Uid.IsEmpty(operation.ClosingOrderId),
				SecurityId = ToSecurityId(operation.Symbol),
				PortfolioName = PortfolioName,
				Side = ToSide(operation.Side),
				TradePrice = operation.ClosePrice,
				TradeVolume = operation.Volume,
				ServerTime = AsUtc(operation.CloseTime),
				PnL = operation.NetProfit,
			}, cancellationToken);
		}
	}

	private async Task SendPortfolio(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = PortfolioName,
			BoardCode = BoardCodes.Fxcm,
		}, cancellationToken);

		var balance = await _client.GetBalance(cancellationToken);
		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = PortfolioName,
			SecurityId = SecurityId.Money,
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.BeginValue, balance.Balance, true)
		.TryAdd(PositionChangeTypes.CurrentValue, balance.Equity, true)
		.TryAdd(PositionChangeTypes.BlockedValue, balance.Margin, true)
		.TryAdd(PositionChangeTypes.BuyOrdersMargin, balance.FreeMargin, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, balance.NetProfit, true)
		.TryAdd(PositionChangeTypes.Currency,
			Enum.TryParse<CurrencyTypes>(balance.Currency, true, out var currency) ? currency : null),
			cancellationToken);

		foreach (var position in await _client.GetPositions(cancellationToken))
		{
			var volume = ToSide(position.Side) == Sides.Sell ? -position.Volume : position.Volume;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = PortfolioName,
				SecurityId = ToSecurityId(position.Symbol),
				ServerTime = PositionTime(position),
			}
			.TryAdd(PositionChangeTypes.CurrentValue, volume, true)
			.TryAdd(PositionChangeTypes.AveragePrice, position.OpenPrice, true)
			.TryAdd(PositionChangeTypes.CurrentPrice, position.CurrentPrice, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, position.NetProfit, true),
				cancellationToken);
		}
	}

	private void EnsureAccount(string portfolioName)
	{
		if (_account == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		if (!portfolioName.IsEmpty() && !portfolioName.EqualsIgnoreCase(_account.TradingAccountId) &&
			!portfolioName.EqualsIgnoreCase(_account.Uuid))
			throw new InvalidOperationException($"Match-Trader account '{portfolioName}' is unavailable.");
	}

	private static void EnsureSuccess(MatchTraderOperationResponse response)
	{
		if (response == null || !response.Status.EqualsIgnoreCase("OK"))
			throw new InvalidOperationException(
				$"Match-Trader rejected the operation: {response?.ErrorMessage.IsEmpty(response?.NativeCode)}");
	}

	private static string ToNative(Sides side) => side == Sides.Buy ? "BUY" : "SELL";
	private static Sides ToSide(string side) => side.EqualsIgnoreCase("BUY") ? Sides.Buy : Sides.Sell;
	private static DateTime AsUtc(DateTime? time)
		=> time is { } value ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : DateTime.UtcNow;
	private static DateTime PositionTime(MatchTraderPosition position)
		=> position.OpenTimeMilliseconds > 0
			? DateTimeOffset.FromUnixTimeMilliseconds(position.OpenTimeMilliseconds).UtcDateTime
			: AsUtc(position.OpenTime);
}
