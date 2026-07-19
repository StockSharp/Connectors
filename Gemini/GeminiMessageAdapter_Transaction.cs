namespace StockSharp.Gemini;

public partial class GeminiMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateWebSocketReady();
		var symbol = GetSymbol(regMsg.SecurityId);
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException("Order volume must be positive.");
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
			throw new InvalidOperationException("A positive limit price is required.");
		if (orderType == OrderTypes.Market && regMsg.PostOnly == true)
			throw new InvalidOperationException("A market order cannot be post-only.");

		var condition = regMsg.Condition as GeminiOrderCondition ??
			new GeminiOrderCondition();
		if (condition.StopPrice is <= 0)
			throw new InvalidOperationException("Stop price must be positive.");
		var clientOrderId = CreateClientOrderId(regMsg.TransactionId,
			regMsg.UserOrderId);
		var wireOrderType = orderType == OrderTypes.Market
			? GeminiWsOrderTypes.Market
			: GeminiWsOrderTypes.Limit;
		using (_sync.EnterScope())
			_pendingOrders[clientOrderId] = new()
			{
				Symbol = symbol,
				Side = regMsg.Side.ToGemini(),
				OrderType = wireOrderType,
				Price = orderType == OrderTypes.Limit ? regMsg.Price : 0m,
				StopPrice = condition.StopPrice ?? 0m,
				Volume = volume,
				TimeInForce = regMsg.TimeInForce,
				IsPostOnly = regMsg.PostOnly,
			};
		await EnsureTradingOrderStreamAsync(cancellationToken);
		try
		{
			await WsClient.PlaceOrderAsync(new()
			{
				Symbol = symbol.ToLowerInvariant(),
				Side = regMsg.Side.ToGemini(),
				OrderType = wireOrderType,
				TimeInForce = regMsg.TimeInForce.ToGemini(regMsg.PostOnly == true),
				Price = orderType == OrderTypes.Limit ? regMsg.Price.ToWire() : null,
				StopPrice = condition.StopPrice?.ToWire(),
				Quantity = volume.ToWire(),
				ClientOrderId = clientOrderId,
			}, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_pendingOrders.Remove(clientOrderId);
				_transactionByClientOrderId.Remove(clientOrderId);
			}
			throw;
		}
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		_ = replaceMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"The current Gemini WebSocket API does not expose order replacement.");
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateWebSocketReady();
		await EnsureTradingOrderStreamAsync(cancellationToken);
		var orderId = ResolveOrderId(cancelMsg.OrderId, cancelMsg.OrderStringId,
			"cancellation");
		await WsClient.CancelOrderAsync(orderId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateWebSocketReady();
		await EnsureTradingOrderStreamAsync(cancellationToken);
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"Gemini bulk cancellation cannot close positions.");
		if (!cancelMsg.SecurityId.SecurityCode.IsEmpty() || cancelMsg.Side is not null)
			throw new NotSupportedException(
				"Gemini WebSocket bulk cancellation applies to all account orders and " +
				"does not accept symbol or side filters.");
		await WsClient.CancelAllOrdersAsync(cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsurePrivateRestReady();
		if (!lookupMsg.IsSubscribe)
		{
			_portfolioSubscriptionId = 0;
			if (_wsClient?.IsPrivateAvailable == true)
			{
				await _wsClient.UnsubscribeBalancesAsync(cancellationToken);
				await _wsClient.UnsubscribePositionsAsync(cancellationToken);
			}
			return;
		}

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = GetPortfolioName(),
			BoardCode = BoardCodes.Gemini,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId, cancellationToken);

		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
			return;
		}

		EnsurePrivateWebSocketReady();
		_portfolioSubscriptionId = lookupMsg.TransactionId;
		try
		{
			await WsClient.SubscribeBalancesAsync(cancellationToken);
			await WsClient.SubscribePositionsAsync(cancellationToken);
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		}
		catch
		{
			_portfolioSubscriptionId = 0;
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		EnsurePrivateRestReady();
		if (!statusMsg.IsSubscribe)
		{
			_orderStatusSubscriptionId = 0;
			if (_wsClient?.IsPrivateAvailable == true &&
				!_isTradingOrderStreamRequired)
				await _wsClient.UnsubscribeOrdersAsync(cancellationToken);
			return;
		}

		var symbol = statusMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetSymbol(statusMsg.SecurityId);
		var limit = (statusMsg.Count ?? 500).Min(500).Max(1).To<int>();
		await SendOrderSnapshotAsync(statusMsg.TransactionId, symbol, statusMsg.From,
			statusMsg.To, limit, cancellationToken);

		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
			return;
		}

		EnsurePrivateWebSocketReady();
		_orderStatusSubscriptionId = statusMsg.TransactionId;
		try
		{
			await WsClient.SubscribeOrdersAsync(cancellationToken);
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		}
		catch
		{
			_orderStatusSubscriptionId = 0;
			throw;
		}
	}

	private async ValueTask SendPortfolioSnapshotAsync(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var balances = await RestClient.GetBalancesAsync(cancellationToken);
		foreach (var balance in balances ?? [])
			await SendBalanceAsync(balance, originalTransactionId, cancellationToken);
		var positions = await RestClient.GetPositionsAsync(cancellationToken);
		foreach (var position in positions?.Positions ?? [])
			await SendPositionAsync(position, originalTransactionId, cancellationToken);
	}

	private async ValueTask EnsureTradingOrderStreamAsync(
		CancellationToken cancellationToken)
	{
		if (_isTradingOrderStreamRequired)
			return;
		await WsClient.SubscribeOrdersAsync(cancellationToken);
		_isTradingOrderStreamRequired = true;
	}

	private async ValueTask SendOrderSnapshotAsync(long originalTransactionId,
		string symbol, DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
	{
		var fromUtc = from?.ToUniversalTime();
		var toUtc = (to ?? DateTime.UtcNow).ToUniversalTime();
		var timestamp = fromUtc?.ToMilliseconds();
		var active = await RestClient.GetActiveOrdersAsync(cancellationToken);
		var history = await RestClient.GetOrderHistoryAsync(symbol, timestamp, limit,
			cancellationToken);
		var orders = (active ?? []).Concat(history ?? [])
			.Where(order => order?.OrderId.IsEmpty() == false &&
				(symbol.IsEmpty() || order.Symbol.EqualsIgnoreCase(symbol)) &&
				GetOrderTime(order) <= toUtc &&
				(fromUtc is null || GetOrderTime(order) >= fromUtc.Value))
			.GroupBy(static order => order.OrderId, StringComparer.OrdinalIgnoreCase)
			.Select(group => group.OrderByDescending(GetOrderTime).First())
			.OrderBy(GetOrderTime).TakeLast(limit);
		foreach (var order in orders)
			await SendOrderAsync(order, originalTransactionId, cancellationToken);

		var fills = await RestClient.GetMyTradesAsync(symbol, timestamp, limit,
			cancellationToken);
		foreach (var fill in (fills ?? [])
			.Where(fill => GetFillTime(fill) <= toUtc &&
				(fromUtc is null || GetFillTime(fill) >= fromUtc.Value))
			.OrderBy(GetFillTime).TakeLast(limit))
			await SendFillAsync(fill, symbol, originalTransactionId, false,
				cancellationToken);
	}

	private ValueTask SendBalanceAsync(GeminiBalance balance,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (balance?.Currency.IsEmpty() != false)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(),
			SecurityId = balance.Currency.ToStockSharp(),
			ServerTime = balance.TimestampNanoseconds > 0
				? balance.TimestampNanoseconds.FromNanoseconds()
				: CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, balance.Amount, true)
		.TryAdd(PositionChangeTypes.BlockedValue,
			Math.Max(0m, balance.Amount - balance.Available), true), cancellationToken);
	}

	private ValueTask SendPositionAsync(GeminiOpenPosition position,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (position?.Symbol.IsEmpty() != false)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(),
			SecurityId = position.Symbol.ToStockSharp(),
			ServerTime = CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, position.Quantity, true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.AverageCost, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, position.MarkPrice, true)
		.TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedPnL, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnL, true),
			cancellationToken);
	}

	private ValueTask SendOrderAsync(GeminiOrder order, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (order?.OrderId.IsEmpty() != false || order.Symbol.IsEmpty())
			return default;
		var condition = new GeminiOrderCondition { StopPrice = order.StopPrice };
		var transactionId = GetTransactionId(order.ClientOrderId);
		if (transactionId > 0 && !order.ClientOrderId.IsEmpty())
			using (_sync.EnterScope())
				_transactionByClientOrderId[order.ClientOrderId] = transactionId;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToStockSharp(),
			ServerTime = GetOrderTime(order),
			PortfolioName = GetPortfolioName(),
			Side = order.Side.ToStockSharp(),
			OrderVolume = order.OriginalAmount,
			Balance = order.RemainingAmount,
			OrderPrice = order.Price,
			AveragePrice = order.AverageExecutionPrice > 0
				? order.AverageExecutionPrice
				: null,
			OrderType = order.OrderType.ToStockSharp(),
			OrderState = order.IsLive ? OrderStates.Active : OrderStates.Done,
			OrderStringId = order.OrderId,
			TransactionId = transactionId,
			OriginalTransactionId = originalTransactionId,
			TimeInForce = GetTimeInForce(order.Options),
			PostOnly = HasOption(order.Options, GeminiOrderOptions.MakerOrCancel),
			Condition = condition,
		}, cancellationToken);
	}

	private ValueTask SendFillAsync(GeminiMyTrade fill, string fallbackSymbol,
		long originalTransactionId, bool onlyNew, CancellationToken cancellationToken)
	{
		if (fill is null || fill.TradeId <= 0)
			return default;
		using (_sync.EnterScope())
		{
			var added = _seenFillIds.Add(fill.TradeId);
			if (onlyNew && !added)
				return default;
		}
		var symbol = fill.Symbol.IsEmpty() ? fallbackSymbol : fill.Symbol;
		if (symbol.IsEmpty())
			return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = symbol.ToStockSharp(),
			ServerTime = GetFillTime(fill),
			PortfolioName = GetPortfolioName(),
			Side = fill.Side.ToStockSharp(),
			OrderStringId = fill.OrderId,
			TradeId = fill.TradeId,
			TradePrice = fill.Price,
			TradeVolume = fill.Amount,
			Commission = fill.FeeAmount,
			CommissionCurrency = fill.FeeCurrency,
			TransactionId = GetTransactionId(fill.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private async ValueTask OnOrderUpdateAsync(GeminiWsOrderUpdate update,
		CancellationToken cancellationToken)
	{
		StreamOrderState order;
		using (_sync.EnterScope())
		{
			if (!_streamOrders.TryGetValue(update.OrderId, out order))
			{
				order = new() { OrderId = update.OrderId };
				_streamOrders.Add(update.OrderId, order);
			}
			if (!update.Symbol.IsEmpty())
				order.Symbol = update.Symbol;
			if (!update.ClientOrderId.IsEmpty())
				order.ClientOrderId = update.ClientOrderId;
			if (!order.ClientOrderId.IsEmpty() &&
				_pendingOrders.TryGetValue(order.ClientOrderId, out var pending))
			{
				if (order.Symbol.IsEmpty())
					order.Symbol = pending.Symbol;
				order.Side ??= pending.Side;
				order.OrderType ??= pending.OrderType;
				if (order.Price <= 0)
					order.Price = pending.Price;
				if (order.StopPrice <= 0)
					order.StopPrice = pending.StopPrice;
				if (order.OriginalQuantity <= 0)
					order.OriginalQuantity = pending.Volume;
				order.TimeInForce ??= pending.TimeInForce;
				order.IsPostOnly ??= pending.IsPostOnly;
			}
			if (update.Side is not null)
				order.Side = update.Side;
			if (update.OrderType is not null)
				order.OrderType = update.OrderType;
			order.Status = update.Status;
			if (update.Price is > 0)
				order.Price = update.Price.Value;
			if (update.StopPrice is > 0)
				order.StopPrice = update.StopPrice.Value;
			if (update.OriginalQuantity is > 0)
				order.OriginalQuantity = update.OriginalQuantity.Value;
			if (update.RemainingQuantity is not null)
				order.RemainingQuantity = update.RemainingQuantity.Value;
			if (order.Status is GeminiWsOrderStatuses.Filled or
				GeminiWsOrderStatuses.Canceled or GeminiWsOrderStatuses.Rejected)
				_pendingOrders.Remove(order.ClientOrderId);
		}

		var transactionId = GetTransactionId(order.ClientOrderId);
		var originalTransactionId = _orderStatusSubscriptionId != 0
			? _orderStatusSubscriptionId
			: transactionId;
		if (originalTransactionId == 0 || order.Symbol.IsEmpty() || order.Side is null)
			return;
		var condition = new GeminiOrderCondition
		{
			StopPrice = order.StopPrice > 0 ? order.StopPrice : null,
		};
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToStockSharp(),
			ServerTime = GetOrderUpdateTime(update),
			PortfolioName = GetPortfolioName(),
			Side = order.Side.Value.ToStockSharp(),
			OrderVolume = order.OriginalQuantity,
			Balance = order.RemainingQuantity,
			OrderPrice = order.Price,
			OrderType = order.OrderType.ToStockSharp(),
			OrderState = order.Status.ToStockSharp(),
			OrderId = order.OrderId,
			TransactionId = transactionId,
			OriginalTransactionId = originalTransactionId,
			TimeInForce = order.TimeInForce,
			PostOnly = order.IsPostOnly,
			Condition = condition,
			Error = order.Status == GeminiWsOrderStatuses.Rejected
				? new InvalidOperationException(update.Reason ?? "Gemini rejected the order.")
				: null,
		}, cancellationToken);

		if (update.TradeId <= 0 || update.ExecutedQuantity is not > 0 ||
			update.Status is not (GeminiWsOrderStatuses.Filled or
				GeminiWsOrderStatuses.PartiallyFilled))
			return;
		using (_sync.EnterScope())
			if (!_seenFillIds.Add(update.TradeId))
				return;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = order.Symbol.ToStockSharp(),
			ServerTime = GetOrderUpdateTime(update),
			PortfolioName = GetPortfolioName(),
			Side = order.Side.Value.ToStockSharp(),
			OrderId = order.OrderId,
			TradeId = update.TradeId,
			TradePrice = update.LastPrice,
			TradeVolume = update.ExecutedQuantity,
			Commission = update.Fee,
			TransactionId = transactionId,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private async ValueTask OnBalanceUpdateAsync(GeminiWsBalanceUpdate update,
		CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId == 0)
			return;
		var serverTime = (update.UpdateTimeNanoseconds > 0
			? update.UpdateTimeNanoseconds
			: update.EventTimeNanoseconds).FromNanoseconds();
		foreach (var balance in update.Balances ?? [])
			if (!balance.Asset.IsEmpty())
				await SendOutMessageAsync(new PositionChangeMessage
				{
					PortfolioName = GetPortfolioName(),
					SecurityId = balance.Asset.ToStockSharp(),
					ServerTime = serverTime,
					OriginalTransactionId = _portfolioSubscriptionId,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, balance.Confirmed, true)
				.TryAdd(PositionChangeTypes.BlockedValue,
					Math.Max(0m, balance.Confirmed - balance.Available), true),
					cancellationToken);
	}

	private async ValueTask OnPositionUpdateAsync(GeminiWsPositionReport update,
		CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId == 0)
			return;
		var serverTime = (update.UpdateTimeNanoseconds > 0
			? update.UpdateTimeNanoseconds
			: update.EventTimeNanoseconds).FromNanoseconds();
		foreach (var position in update.Positions ?? [])
		{
			if (position.Symbol.IsEmpty())
				continue;
			var amount = (position.Amounts ?? []).FirstOrDefault(value =>
				value.Name.EqualsIgnoreCase("position"));
			if (amount is null)
				continue;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = GetPortfolioName(),
				SecurityId = position.Symbol.ToStockSharp(),
				ServerTime = serverTime,
				OriginalTransactionId = _portfolioSubscriptionId,
			}.TryAdd(PositionChangeTypes.CurrentValue, amount.Value, true),
				cancellationToken);
		}
	}

	private DateTime GetOrderTime(GeminiOrder order)
		=> order.TimestampMilliseconds > 0
			? order.TimestampMilliseconds.FromMilliseconds()
			: CurrentTime;

	private DateTime GetFillTime(GeminiMyTrade fill)
		=> fill.TimestampMilliseconds > 0
			? fill.TimestampMilliseconds.FromMilliseconds()
			: CurrentTime;

	private DateTime GetOrderUpdateTime(GeminiWsOrderUpdate update)
	{
		var time = update.TransactionTimeNanoseconds > 0
			? update.TransactionTimeNanoseconds
			: update.EventTimeNanoseconds;
		return time > 0 ? time.FromNanoseconds() : CurrentTime;
	}

	private static bool HasOption(IEnumerable<GeminiOrderOptions> options,
		GeminiOrderOptions expected)
		=> (options ?? []).Contains(expected);

	private static TimeInForce? GetTimeInForce(IEnumerable<GeminiOrderOptions> options)
		=> HasOption(options, GeminiOrderOptions.ImmediateOrCancel)
			? TimeInForce.CancelBalance
			: HasOption(options, GeminiOrderOptions.FillOrKill)
				? TimeInForce.MatchOrCancel
			: null;

	private static string ResolveOrderId(long? numericOrderId, string stringOrderId,
		string operation)
	{
		if (!stringOrderId.IsEmpty())
			return stringOrderId;
		if (numericOrderId is > 0)
			return numericOrderId.Value.ToString(CultureInfo.InvariantCulture);
		throw new InvalidOperationException(
			$"Gemini {operation} requires an exchange order ID.");
	}
}
