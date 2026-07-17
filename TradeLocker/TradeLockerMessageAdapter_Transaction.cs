namespace StockSharp.TradeLocker;

public partial class TradeLockerMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsureAccount(regMsg.PortfolioName);
		if (regMsg.Volume <= 0)
			throw new InvalidOperationException("TradeLocker order quantity must be positive.");

		var instrument = ResolveInstrument(regMsg.SecurityId);
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		var type = orderType switch
		{
			OrderTypes.Market => "market",
			OrderTypes.Limit => "limit",
			OrderTypes.Conditional => "stop",
			_ => throw new NotSupportedException($"TradeLocker order type '{orderType}' is not supported."),
		};
		if (orderType != OrderTypes.Market && regMsg.Price <= 0)
			throw new InvalidOperationException("TradeLocker limit and stop orders require a price.");

		var condition = regMsg.Condition as TradeLockerOrderCondition;
		var orderId = await _client.PlaceOrder(new()
		{
			TradableInstrumentId = instrument.TradableId,
			RouteId = GetRoute(instrument, "TRADE"),
			Quantity = regMsg.Volume,
			Side = regMsg.Side == Sides.Buy ? "buy" : "sell",
			Type = type,
			Validity = orderType == OrderTypes.Market ? "IOC" : "GTC",
			Price = orderType == OrderTypes.Limit ? regMsg.Price : null,
			StopPrice = orderType == OrderTypes.Conditional ? regMsg.Price : null,
			StopLoss = condition?.StopLoss,
			StopLossType = condition?.StopLoss == null ? null : "absolute",
			TakeProfit = condition?.TakeProfit,
			TakeProfitType = condition?.TakeProfit == null ? null : "absolute",
			StrategyId = condition?.StrategyId,
		}, cancellationToken);

		if (orderId <= 0)
			throw new InvalidOperationException("TradeLocker did not return an order identifier.");
		_orderTransactions[orderId] = regMsg.TransactionId;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			TransactionId = regMsg.TransactionId,
			OrderId = orderId,
			SecurityId = ToSecurityId(instrument),
			PortfolioName = _account.Id.ToString(CultureInfo.InvariantCulture),
			Side = regMsg.Side,
			OrderType = orderType,
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
		var orderId = cancelMsg.OrderId ?? 0;
		if (orderId <= 0 && cancelMsg.OriginalTransactionId != 0)
			orderId = _orderTransactions.ToArray().FirstOrDefault(p =>
				p.Value == cancelMsg.OriginalTransactionId).Key;
		if (orderId <= 0)
			throw new InvalidOperationException("TradeLocker order identifier is required for cancellation.");
		await _client.CancelOrder(orderId, cancellationToken);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = cancelMsg.TransactionId,
			OrderId = orderId,
			PortfolioName = _account.Id.ToString(CultureInfo.InvariantCulture),
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
		await SendOrders(statusMsg.TransactionId, true, cancellationToken);
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

	private async Task SendOrders(long originalTransactionId, bool includeHistory,
		CancellationToken cancellationToken)
	{
		var orders = new List<TradeLockerOrder>();
		orders.AddRange(await _client.GetOrders(false, cancellationToken));
		if (includeHistory)
			orders.AddRange(await _client.GetOrders(true, cancellationToken));

		foreach (var order in orders.GroupBy(o => o.Id).Select(g => g.Last()))
		{
			if (!_instruments.TryGetValue(order.TradableInstrumentId, out var instrument))
				continue;
			var transactionId = _orderTransactions.TryGetValue(order.Id, out var tracked)
				? tracked : 0;
			var state = ToOrderState(order.Status, order.IsOpen);
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				OriginalTransactionId = transactionId == 0 ? originalTransactionId : transactionId,
				TransactionId = transactionId,
				OrderId = order.Id,
				SecurityId = ToSecurityId(instrument),
				PortfolioName = _account.Id.ToString(CultureInfo.InvariantCulture),
				Side = order.Side.EqualsIgnoreCase("buy") ? Sides.Buy : Sides.Sell,
				OrderType = ToOrderType(order.Type),
				OrderPrice = order.Price > 0 ? order.Price : order.StopPrice,
				OrderVolume = order.Quantity,
				Balance = Math.Max(0, order.Quantity - order.FilledQuantity),
				AveragePrice = order.AveragePrice == 0 ? null : order.AveragePrice,
				OrderState = state,
				ServerTime = FromUnixMilliseconds(order.LastModified, order.CreatedDate).UtcDateTime,
				TimeInForce = order.Validity.EqualsIgnoreCase("IOC")
					? TimeInForce.CancelBalance : TimeInForce.PutInQueue,
			}, cancellationToken);

			var previousFilled = _filledQuantities.TryGetValue(order.Id, out var value) ? value : 0;
			if (order.FilledQuantity > previousFilled)
			{
				var increment = order.FilledQuantity - previousFilled;
				_filledQuantities[order.Id] = order.FilledQuantity;
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OriginalTransactionId = transactionId == 0 ? originalTransactionId : transactionId,
					OrderId = order.Id,
					SecurityId = ToSecurityId(instrument),
					PortfolioName = _account.Id.ToString(CultureInfo.InvariantCulture),
					Side = order.Side.EqualsIgnoreCase("buy") ? Sides.Buy : Sides.Sell,
					TradePrice = order.AveragePrice,
					TradeVolume = increment,
					ServerTime = FromUnixMilliseconds(order.LastModified, order.CreatedDate).UtcDateTime,
				}, cancellationToken);
			}
		}
	}

	private async Task SendPortfolio(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var portfolio = _account.Id.ToString(CultureInfo.InvariantCulture);
		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = portfolio,
			BoardCode = BoardCodes.Fxcm,
		}, cancellationToken);

		var state = (await _client.GetAccountState(cancellationToken)).FirstOrDefault();
		if (state != null)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = portfolio,
				SecurityId = SecurityId.Money,
				ServerTime = DateTime.UtcNow,
			}
			.TryAdd(PositionChangeTypes.BeginValue, _account.Balance, true)
			.TryAdd(PositionChangeTypes.CurrentValue, state.Balance, true)
			.TryAdd(PositionChangeTypes.BuyOrdersMargin, state.AvailableFunds, true)
			.TryAdd(PositionChangeTypes.BlockedValue,
				Math.Max(0, state.Balance - state.AvailableFunds), true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, state.OpenNetPnL, true)
			.TryAdd(PositionChangeTypes.Currency,
				Enum.TryParse<CurrencyTypes>(_account.Currency, true, out var currency) ? currency : null),
				cancellationToken);
		}

		foreach (var position in await _client.GetPositions(cancellationToken))
		{
			if (!_instruments.TryGetValue(position.TradableInstrumentId, out var instrument))
				continue;
			var quantity = position.Side.EqualsIgnoreCase("sell")
				? -position.Quantity : position.Quantity;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = portfolio,
				SecurityId = ToSecurityId(instrument),
				ServerTime = FromUnixMilliseconds(position.OpenDate).UtcDateTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, quantity, true)
			.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnL, true),
				cancellationToken);
		}
	}

	private void EnsureAccount(string portfolioName)
	{
		if (_account == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(_account.Id.ToString(CultureInfo.InvariantCulture)) &&
			!portfolioName.EqualsIgnoreCase(_account.Name))
			throw new InvalidOperationException($"TradeLocker account '{portfolioName}' is unavailable.");
	}

	private static OrderStates ToOrderState(string status, bool isOpen)
	{
		if (status.ContainsIgnoreCase("reject") || status.ContainsIgnoreCase("refus") ||
			status.ContainsIgnoreCase("unplaced"))
			return OrderStates.Failed;
		if (isOpen || status.ContainsIgnoreCase("new") || status.ContainsIgnoreCase("accept") ||
			status.ContainsIgnoreCase("partial"))
			return OrderStates.Active;
		return OrderStates.Done;
	}

	private static OrderTypes ToOrderType(string type)
		=> type.EqualsIgnoreCase("market") ? OrderTypes.Market
			: type.EqualsIgnoreCase("stop") ? OrderTypes.Conditional : OrderTypes.Limit;

	private static DateTimeOffset FromUnixMilliseconds(long primary, long fallback = 0)
	{
		var value = primary > 0 ? primary : fallback;
		return value > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(value) : DateTimeOffset.UtcNow;
	}
}
