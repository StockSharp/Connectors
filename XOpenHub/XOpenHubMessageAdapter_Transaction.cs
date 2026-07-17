namespace StockSharp.XOpenHub;

public partial class XOpenHubMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsureAccount(regMsg.PortfolioName);
		if (regMsg.Volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Volume), regMsg.Volume,
				"X Open Hub order volume must be positive.");

		var symbol = ResolveSymbol(regMsg.SecurityId);
		var condition = regMsg.Condition as XOpenHubOrderCondition ?? new();
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		XApiTradeTransactionInfo request;
		var isClosing = condition.IsWithdraw;

		if (isClosing)
		{
			var position = await FindPosition(condition.PositionId, cancellationToken);
			if (!position.Symbol.EqualsIgnoreCase(symbol.Symbol))
				throw new InvalidOperationException(
					$"X Open Hub position {condition.PositionId} belongs to '{position.Symbol}', not '{symbol.Symbol}'.");
			if (regMsg.Volume > position.Volume)
				throw new InvalidOperationException(
					$"Close volume {regMsg.Volume} exceeds position volume {position.Volume}.");
			request = new()
			{
				Command = position.Command,
				Type = 2,
				Symbol = position.Symbol,
				Volume = regMsg.Volume,
				Price = position.Command == 0 ? symbol.Bid : symbol.Ask,
				Order = position.Position > 0 ? position.Position : position.Order,
				StopLoss = position.StopLoss,
				TakeProfit = position.TakeProfit,
				Offset = condition.Offset,
				CustomComment = condition.Comment,
			};
			orderType = OrderTypes.Market;
		}
		else
		{
			if (orderType is not (OrderTypes.Market or OrderTypes.Limit or OrderTypes.Conditional))
				throw new NotSupportedException(
					$"X Open Hub does not support StockSharp order type '{orderType}'.");
			if (orderType != OrderTypes.Market && regMsg.Price <= 0)
				throw new InvalidOperationException(
					"X Open Hub pending orders require a positive price.");

			request = new()
			{
				Command = ToCommand(regMsg.Side, orderType),
				Type = 0,
				Symbol = symbol.Symbol,
				Volume = regMsg.Volume,
				Price = orderType == OrderTypes.Market
					? regMsg.Side == Sides.Buy ? symbol.Ask : symbol.Bid
					: regMsg.Price,
				StopLoss = condition.StopLoss ?? 0,
				TakeProfit = condition.TakeProfit ?? 0,
				Expiration = regMsg.TillDate is { } till
					? new DateTimeOffset(till.ToUniversalTime()).ToUnixTimeMilliseconds() : 0,
				Offset = condition.Offset,
				CustomComment = condition.Comment,
			};
		}

		await EnableTransactionStream(cancellationToken);
		var result = await _command.TradeTransaction(request, cancellationToken);
		if (result?.Order <= 0)
			throw new InvalidOperationException("X Open Hub returned no trade transaction identifier.");
		_orderTransactions[result.Order] = regMsg.TransactionId;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			TransactionId = regMsg.TransactionId,
			OrderId = result.Order,
			SecurityId = ToSecurityId(symbol.Symbol),
			PortfolioName = PortfolioName,
			Side = regMsg.Side,
			OrderType = orderType,
			OrderPrice = request.Price,
			OrderVolume = regMsg.Volume,
			Balance = regMsg.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = DateTime.UtcNow,
			TimeInForce = regMsg.TimeInForce,
			ExpiryDate = regMsg.TillDate,
			Condition = condition,
		}, cancellationToken);

		var status = await _command.WaitTradeTransaction(result.Order, cancellationToken);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			TransactionId = regMsg.TransactionId,
			OrderId = status.Order > 0 ? status.Order : result.Order,
			SecurityId = ToSecurityId(symbol.Symbol),
			PortfolioName = PortfolioName,
			Side = regMsg.Side,
			OrderType = orderType,
			OrderPrice = status.Price > 0 ? status.Price : request.Price,
			OrderVolume = regMsg.Volume,
			Balance = !isClosing && orderType != OrderTypes.Market ? regMsg.Volume : 0,
			OrderState = !isClosing && orderType != OrderTypes.Market
				? OrderStates.Active : OrderStates.Done,
			ServerTime = DateTime.UtcNow,
			TimeInForce = regMsg.TimeInForce,
			ExpiryDate = regMsg.TillDate,
			Condition = condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		EnsureAccount(replaceMsg.PortfolioName);
		if (replaceMsg.OldOrderId is not > 0)
			throw new InvalidOperationException(
				"X Open Hub exchange order id is required for replacement.");
		if (replaceMsg.Price <= 0 || replaceMsg.Volume <= 0)
			throw new InvalidOperationException(
				"X Open Hub replacement price and volume must be positive.");

		var order = (await _command.GetTrades(true, cancellationToken) ?? [])
			.FirstOrDefault(t => t.Order == replaceMsg.OldOrderId.Value && t.Type == 1)
			?? throw new InvalidOperationException(
				$"X Open Hub pending order {replaceMsg.OldOrderId.Value} was not found.");
		var condition = replaceMsg.Condition as XOpenHubOrderCondition ?? new();
		var result = await _command.TradeTransaction(new()
		{
			Command = order.Command,
			Type = 3,
			Symbol = order.Symbol,
			Volume = replaceMsg.Volume,
			Price = replaceMsg.Price,
			StopLoss = condition.StopLoss ?? order.StopLoss,
			TakeProfit = condition.TakeProfit ?? order.TakeProfit,
			Order = order.Order,
			Expiration = replaceMsg.TillDate is { } till
				? new DateTimeOffset(till.ToUniversalTime()).ToUnixTimeMilliseconds()
				: order.Expiration ?? 0,
			Offset = condition.Offset,
			CustomComment = condition.Comment.IsEmpty(order.CustomComment),
		}, cancellationToken);
		if (result?.Order <= 0)
			throw new InvalidOperationException("X Open Hub returned no replacement transaction id.");
		_orderTransactions[order.Order] = replaceMsg.TransactionId;
		await _command.WaitTradeTransaction(result.Order, cancellationToken);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = replaceMsg.TransactionId,
			TransactionId = replaceMsg.TransactionId,
			OrderId = order.Order,
			SecurityId = ToSecurityId(order.Symbol),
			PortfolioName = PortfolioName,
			Side = ToSide(order.Command),
			OrderType = ToOrderType(order.Command),
			OrderPrice = replaceMsg.Price,
			OrderVolume = replaceMsg.Volume,
			Balance = replaceMsg.Volume,
			OrderState = OrderStates.Active,
			ServerTime = DateTime.UtcNow,
			ExpiryDate = replaceMsg.TillDate,
			Condition = condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsureAccount(cancelMsg.PortfolioName);
		var orderId = ResolveOrderId(cancelMsg.OrderId, cancelMsg.OrderStringId,
			cancelMsg.OriginalTransactionId);
		var order = (await _command.GetTrades(true, cancellationToken) ?? [])
			.FirstOrDefault(t => t.Order == orderId && t.Type == 1)
			?? throw new InvalidOperationException(
				$"X Open Hub pending order {orderId} was not found.");

		var result = await _command.TradeTransaction(new()
		{
			Command = order.Command,
			Type = 4,
			Symbol = order.Symbol,
			Volume = order.Volume,
			Price = order.OpenPrice,
			StopLoss = order.StopLoss,
			TakeProfit = order.TakeProfit,
			Order = order.Order,
			Expiration = order.Expiration ?? 0,
			CustomComment = order.CustomComment,
		}, cancellationToken);
		if (result?.Order <= 0)
			throw new InvalidOperationException("X Open Hub returned no cancellation transaction id.");
		await _command.WaitTradeTransaction(result.Order, cancellationToken);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = cancelMsg.TransactionId,
			OrderId = order.Order,
			SecurityId = ToSecurityId(order.Symbol),
			PortfolioName = PortfolioName,
			Side = ToSide(order.Command),
			OrderType = ToOrderType(order.Command),
			OrderPrice = order.OpenPrice,
			OrderVolume = order.Volume,
			Balance = 0,
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
			{
				_orderStatusSubscriptionId = 0;
				await UpdateTransactionSubscriptions(cancellationToken);
			}
			return;
		}

		EnsureAccount(statusMsg.PortfolioName);
		foreach (var trade in await _command.GetTrades(true, cancellationToken) ?? [])
			await SendTradeExecution(trade, statusMsg.TransactionId, false, cancellationToken);

		var includeHistory = statusMsg.IsHistoryOnly() || statusMsg.From != null ||
			statusMsg.To != null || statusMsg.Count != null;
		if (includeHistory)
		{
			var to = new DateTimeOffset((statusMsg.To ?? DateTime.UtcNow).ToUniversalTime());
			var from = new DateTimeOffset((statusMsg.From ??
				to.UtcDateTime.AddDays(-30)).ToUniversalTime());
			var left = statusMsg.Count ?? long.MaxValue;
			foreach (var trade in (await _command.GetTradesHistory(from, to, cancellationToken) ?? [])
				.OrderBy(t => t.CloseTime ?? t.OpenTime))
			{
				await SendTradeExecution(trade, statusMsg.TransactionId, true, cancellationToken);
				if (--left <= 0)
					break;
			}
		}

		if (statusMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
		else
		{
			_orderStatusSubscriptionId = statusMsg.TransactionId;
			await UpdateTransactionSubscriptions(cancellationToken);
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
			{
				_portfolioSubscriptionId = 0;
				await UpdateTransactionSubscriptions(cancellationToken);
			}
			return;
		}

		EnsureAccount(lookupMsg.PortfolioName);
		await SendPortfolio(lookupMsg.TransactionId, cancellationToken);
		if (lookupMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
		else
		{
			_portfolioSubscriptionId = lookupMsg.TransactionId;
			await UpdateTransactionSubscriptions(cancellationToken);
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		}
	}

	private async Task EnableTransactionStream(CancellationToken cancellationToken)
	{
		await _stream.SetTrades(true, cancellationToken);
		await _stream.SetTradeStatus(true, cancellationToken);
	}

	private async Task UpdateTransactionSubscriptions(CancellationToken cancellationToken)
	{
		await _stream.SetBalance(_portfolioSubscriptionId != 0, cancellationToken);
		var enabled = _orderStatusSubscriptionId != 0 || _portfolioSubscriptionId != 0;
		await _stream.SetTrades(enabled, cancellationToken);
		await _stream.SetTradeStatus(_orderStatusSubscriptionId != 0, cancellationToken);
	}

	private async Task<XApiTrade> FindPosition(string positionId,
		CancellationToken cancellationToken)
	{
		if (!long.TryParse(positionId.ThrowIfEmpty(nameof(positionId)),
			NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
			throw new InvalidOperationException(
				$"X Open Hub position id '{positionId}' is not numeric.");
		return (await _command.GetTrades(true, cancellationToken) ?? [])
			.FirstOrDefault(t => t.Type == 0 && (t.Position == id || t.Order == id))
			?? throw new InvalidOperationException($"X Open Hub position {id} was not found.");
	}

	private long ResolveOrderId(long? orderId, string orderStringId, long originalTransactionId)
	{
		if (orderId is > 0)
			return orderId.Value;
		if (long.TryParse(orderStringId, NumberStyles.Integer, CultureInfo.InvariantCulture,
			out var parsed) && parsed > 0)
			return parsed;
		if (originalTransactionId != 0)
		{
			var tracked = _orderTransactions.ToArray().FirstOrDefault(p =>
				p.Value == originalTransactionId).Key;
			if (tracked > 0)
				return tracked;
		}
		throw new InvalidOperationException("X Open Hub exchange order id is required.");
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

		var margin = await _command.GetMarginLevel(cancellationToken);
		await SendBalance(margin.Balance, margin.Credit, margin.Equity, margin.Margin,
			margin.FreeMargin, margin.MarginLevel, margin.Currency, originalTransactionId,
			cancellationToken);

		_positions.Clear();
		foreach (var position in (await _command.GetTrades(true, cancellationToken) ?? [])
			.Where(t => t.Type == 0 && !t.Closed))
			_positions[GetPositionKey(position)] = position;
		foreach (var symbol in _positions.Values.Select(p => p.Symbol)
			.Distinct(StringComparer.OrdinalIgnoreCase))
			await SendPosition(symbol, originalTransactionId, cancellationToken);
	}

	private ValueTask ProcessBalance(XApiStreamBalance balance,
		CancellationToken cancellationToken)
		=> _portfolioSubscriptionId == 0 ? default : SendBalance(balance.Balance,
			balance.Credit, balance.Equity, balance.Margin, balance.FreeMargin,
			balance.MarginLevel, null, _portfolioSubscriptionId, cancellationToken);

	private ValueTask SendBalance(decimal balance, decimal credit, decimal equity,
		decimal margin, decimal freeMargin, decimal marginLevel, string currency,
		long originalTransactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = PortfolioName,
			SecurityId = SecurityId.Money,
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.BeginValue, balance, true)
		.TryAdd(PositionChangeTypes.CurrentValue, equity, true)
		.TryAdd(PositionChangeTypes.BlockedValue, margin, true)
		.TryAdd(PositionChangeTypes.BuyOrdersMargin, freeMargin, true)
		.TryAdd(PositionChangeTypes.Leverage, marginLevel == 0 ? null : marginLevel, true)
		.TryAdd(PositionChangeTypes.Currency,
			Enum.TryParse<CurrencyTypes>(currency, true, out var value) ? value : null),
			cancellationToken);

	private async ValueTask ProcessTrade(XApiTrade trade, CancellationToken cancellationToken)
	{
		if (trade.Command > 5 || trade.Symbol.IsEmpty())
			return;

		if (trade.Type == 0 && !trade.Closed)
			_positions[GetPositionKey(trade)] = trade;
		else if (trade.Closed || trade.Type == 2)
		{
			var key = trade.Position > 0 ? trade.Position : trade.Order;
			_positions.Remove(key);
		}

		var originalTransactionId = _orderTransactions.TryGetValue(trade.Order, out var tracked)
			? tracked : _orderStatusSubscriptionId;
		if (originalTransactionId != 0)
			await SendTradeExecution(trade, originalTransactionId, true, cancellationToken);
		if (_portfolioSubscriptionId != 0)
			await SendPosition(trade.Symbol, _portfolioSubscriptionId, cancellationToken);
	}

	private ValueTask ProcessTradeStatus(XApiTradeStatus status,
		CancellationToken cancellationToken)
	{
		if (status.RequestStatus is not (0 or 4) ||
			!_orderTransactions.TryGetValue(status.Order, out var transactionId))
			return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = transactionId,
			OrderId = status.Order,
			OrderState = OrderStates.Failed,
			Error = new InvalidOperationException(
				$"X Open Hub rejected trade transaction {status.Order}: {status.Message.IsEmpty("No reason supplied")}"),
			ServerTime = DateTime.UtcNow,
		}, cancellationToken);
	}

	private async Task SendTradeExecution(XApiTrade trade, long originalTransactionId,
		bool includeFill, CancellationToken cancellationToken)
	{
		if (trade.Command > 5 || trade.Symbol.IsEmpty())
			return;
		var transactionId = _orderTransactions.TryGetValue(trade.Order, out var tracked)
			? tracked : 0;
		var isPending = trade.Type == 1 && !trade.Closed &&
			!trade.State.EqualsIgnoreCase("Deleted");
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = transactionId == 0 ? originalTransactionId : transactionId,
			TransactionId = transactionId,
			OrderId = trade.Order,
			SecurityId = ToSecurityId(trade.Symbol),
			PortfolioName = PortfolioName,
			Side = ToSide(trade.Command),
			OrderType = ToOrderType(trade.Command),
			OrderPrice = trade.OpenPrice,
			OrderVolume = trade.Volume,
			Balance = isPending ? trade.Volume : 0,
			AveragePrice = trade.OpenPrice > 0 ? trade.OpenPrice : null,
			OrderState = isPending ? OrderStates.Active : OrderStates.Done,
			ServerTime = FromUnixMilliseconds(trade.CloseTime ?? trade.OpenTime),
			ExpiryDate = trade.Expiration is > 0
				? DateTimeOffset.FromUnixTimeMilliseconds(trade.Expiration.Value).UtcDateTime : null,
			Condition = new XOpenHubOrderCondition
			{
				StopLoss = trade.StopLoss == 0 ? null : trade.StopLoss,
				TakeProfit = trade.TakeProfit == 0 ? null : trade.TakeProfit,
				PositionId = trade.Position > 0
					? trade.Position.ToString(CultureInfo.InvariantCulture) : null,
				Comment = trade.CustomComment,
			},
		}, cancellationToken);

		var tradeId = trade.Transaction > 0 ? trade.Transaction : trade.Position;
		if (!includeFill || isPending || tradeId <= 0 || _reportedTrades.Contains(tradeId))
			return;
		_reportedTrades.Add(tradeId);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = transactionId == 0 ? originalTransactionId : transactionId,
			OrderId = trade.Order,
			TradeId = tradeId,
			SecurityId = ToSecurityId(trade.Symbol),
			PortfolioName = PortfolioName,
			Side = ToSide(trade.Command),
			TradePrice = trade.Closed || trade.Type == 2
				? trade.ClosePrice : trade.OpenPrice,
			TradeVolume = trade.Volume,
			ServerTime = FromUnixMilliseconds(trade.CloseTime ?? trade.OpenTime),
			PnL = trade.Profit,
			Commission = trade.Commission,
		}, cancellationToken);
	}

	private ValueTask SendPosition(string symbol, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var positions = _positions.Values.Where(p => p.Symbol.EqualsIgnoreCase(symbol)).ToArray();
		var volume = positions.Sum(p => ToSide(p.Command) == Sides.Buy ? p.Volume : -p.Volume);
		var absoluteVolume = positions.Sum(p => p.Volume);
		var averagePrice = absoluteVolume > 0
			? positions.Sum(p => p.OpenPrice * p.Volume) / absoluteVolume : (decimal?)null;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = PortfolioName,
			SecurityId = ToSecurityId(symbol),
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, volume, true)
		.TryAdd(PositionChangeTypes.AveragePrice, averagePrice, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, positions.Sum(p => p.Profit ?? 0), true),
			cancellationToken);
	}

	private void EnsureAccount(string portfolioName)
	{
		if (_command == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		if (!portfolioName.IsEmpty() && !portfolioName.EqualsIgnoreCase(PortfolioName) &&
			!portfolioName.EqualsIgnoreCase(Login))
			throw new InvalidOperationException(
				$"X Open Hub account '{portfolioName}' is unavailable.");
	}

	private static long GetPositionKey(XApiTrade trade)
		=> trade.Position > 0 ? trade.Position : trade.Order;

	private static int ToCommand(Sides side, OrderTypes orderType)
		=> orderType switch
		{
			OrderTypes.Market => side == Sides.Buy ? 0 : 1,
			OrderTypes.Limit => side == Sides.Buy ? 2 : 3,
			OrderTypes.Conditional => side == Sides.Buy ? 4 : 5,
			_ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType, null),
		};

	private static Sides ToSide(int command)
		=> command is 0 or 2 or 4 ? Sides.Buy : Sides.Sell;

	private static OrderTypes ToOrderType(int command)
		=> command is 0 or 1 ? OrderTypes.Market
			: command is 2 or 3 ? OrderTypes.Limit : OrderTypes.Conditional;

	private static DateTime FromUnixMilliseconds(long value)
		=> value > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime : DateTime.UtcNow;
}
