namespace StockSharp.Backpack;

public partial class BackpackMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var symbol = GetSymbol(regMsg.SecurityId);
		var market = GetMarket(symbol);
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException("Order volume must be positive.");
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
			throw new InvalidOperationException("Limit order price must be positive.");

		var condition = regMsg.Condition as BackpackOrderCondition ??
			new BackpackOrderCondition();
		var isPerpetual = IsPerpetual(market);
		var isReduceOnly = condition.IsReduceOnly ||
			regMsg.PositionEffect == OrderPositionEffects.CloseOnly;
		if (!isPerpetual && isReduceOnly)
			throw new InvalidOperationException(
				"Reduce-only orders require a Backpack Exchange perpetual market.");
		if (orderType != OrderTypes.Market && condition.IsQuoteVolume)
			throw new InvalidOperationException(
				"Quote-currency quantity is supported for market orders only.");
		if (orderType == OrderTypes.Market && regMsg.PostOnly == true)
			throw new InvalidOperationException("A market order cannot be post-only.");

		var result = await RestClient.PlaceOrderAsync(new()
		{
			ClientId = CreateClientOrderId(regMsg.TransactionId, regMsg.UserOrderId),
			OrderType = orderType == OrderTypes.Market
				? BackpackOrderTypes.Market
				: BackpackOrderTypes.Limit,
			IsPostOnly = orderType == OrderTypes.Limit ? regMsg.PostOnly : null,
			Price = orderType == OrderTypes.Limit ? regMsg.Price.ToWire() : null,
			Quantity = condition.IsQuoteVolume ? null : volume.ToWire(),
			QuoteQuantity = condition.IsQuoteVolume ? volume.ToWire() : null,
			IsReduceOnly = isPerpetual ? isReduceOnly : null,
			Side = regMsg.Side.ToBackpack(),
			Symbol = symbol,
			TimeInForce = orderType == OrderTypes.Limit
				? regMsg.TimeInForce.ToBackpack()
				: null,
		}, cancellationToken);
		if (result?.Id.IsEmpty() != false)
			throw new InvalidDataException(
				"Backpack Exchange accepted the order without returning an order ID.");

		await SendOrderAsync(result, regMsg.TransactionId, regMsg.TransactionId,
			condition, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		_ = replaceMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"Backpack Exchange does not provide an order-replacement endpoint.");
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var symbol = GetSymbol(cancelMsg.SecurityId);
		var orderId = ResolveOrderId(cancelMsg.OrderId, cancelMsg.OrderStringId,
			"cancellation");
		var result = await RestClient.CancelOrderAsync(new()
		{
			OrderId = orderId,
			Symbol = symbol,
		}, cancellationToken);
		if (result is null)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = symbol.ToStockSharp(),
				ServerTime = CurrentTime,
				PortfolioName = GetPortfolioName(),
				OrderStringId = orderId,
				OrderState = OrderStates.Done,
				Balance = 0m,
				OriginalTransactionId = cancelMsg.TransactionId,
			}, cancellationToken);
			return;
		}
		await SendOrderAsync(result, cancelMsg.TransactionId, null, null,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"Backpack Exchange bulk cancellation does not close positions.");

		var symbol = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetSymbol(cancelMsg.SecurityId);
		if (cancelMsg.Side is null && !symbol.IsEmpty())
		{
			await RestClient.CancelOrdersAsync(new() { Symbol = symbol }, cancellationToken);
			return;
		}

		var orders = await RestClient.GetOrdersAsync(new() { Symbol = symbol },
			cancellationToken);
		if (orders is null)
			return;
		foreach (var order in orders.Where(order => order?.Id.IsEmpty() == false &&
			(cancelMsg.Side is null || order.Side.ToStockSharp() == cancelMsg.Side)))
			await RestClient.CancelOrderAsync(new()
			{
				OrderId = order.Id,
				Symbol = order.Symbol,
			}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsurePrivateReady();
		if (!lookupMsg.IsSubscribe)
		{
			_portfolioSubscriptionId = 0;
			if (_wsClient is not null)
				await _wsClient.UnsubscribePositionsAsync(cancellationToken);
			return;
		}

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = GetPortfolioName(),
			BoardCode = BoardCodes.Backpack,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId, cancellationToken);

		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
			return;
		}

		_portfolioSubscriptionId = lookupMsg.TransactionId;
		try
		{
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
		EnsurePrivateReady();
		if (!statusMsg.IsSubscribe)
		{
			_orderStatusSubscriptionId = 0;
			if (_wsClient is not null)
				await _wsClient.UnsubscribeOrdersAsync(cancellationToken);
			return;
		}

		var symbol = statusMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetSymbol(statusMsg.SecurityId);
		var limit = (statusMsg.Count ?? 1000).Min(10000).Max(1).To<int>();
		await SendOrderSnapshotAsync(statusMsg.TransactionId, symbol, statusMsg.From,
			statusMsg.To, limit, cancellationToken);

		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
			return;
		}

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
		foreach (var balance in balances?.Entries ?? [])
			await SendBalanceAsync(balance, originalTransactionId, cancellationToken);

		var positions = await RestClient.GetPositionsAsync(new(), cancellationToken);
		foreach (var position in positions ?? [])
			await SendPositionAsync(position, originalTransactionId, CurrentTime,
				cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(long originalTransactionId, string symbol,
		DateTime? from, DateTime? to, int limit, CancellationToken cancellationToken)
	{
		var openOrders = await RestClient.GetOrdersAsync(new() { Symbol = symbol },
			cancellationToken);
		var historicalOrders = await LoadOrderHistoryAsync(symbol, from, to, limit,
			cancellationToken);
		var orders = (openOrders ?? []).Concat(historicalOrders)
			.Where(static order => order?.Id.IsEmpty() == false)
			.GroupBy(static order => order.Id, StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.OrderByDescending(GetOrderTime).First())
			.OrderBy(GetOrderTime)
			.TakeLast(limit);
		foreach (var order in orders)
			await SendOrderAsync(order, originalTransactionId, null, null,
				cancellationToken);

		var fills = await LoadFillsAsync(symbol, from, to, limit, cancellationToken);
		foreach (var fill in fills.OrderBy(GetFillTime))
			await SendFillAsync(fill, originalTransactionId, false, cancellationToken);
	}

	private async ValueTask<BackpackOrder[]> LoadOrderHistoryAsync(string symbol,
		DateTime? from, DateTime? to, int maximum, CancellationToken cancellationToken)
	{
		var result = new List<BackpackOrder>();
		for (var offset = 0; result.Count < maximum;)
		{
			var size = (maximum - result.Count).Min(1000).Max(1);
			var page = await RestClient.GetOrderHistoryAsync(new()
			{
				Symbol = symbol,
				Limit = size,
				Offset = offset,
			}, cancellationToken);
			if (page is not { Length: > 0 })
				break;
			result.AddRange(page.Where(order =>
			{
				var time = GetOrderTime(order);
				return (from is null || time >= from.Value.ToUniversalTime()) &&
					(to is null || time <= to.Value.ToUniversalTime());
			}));
			offset += page.Length;
			if (page.Length < size || from is DateTime fromTime &&
				page.Min(GetOrderTime) < fromTime.ToUniversalTime())
				break;
		}
		return [.. result.Take(maximum)];
	}

	private async ValueTask<BackpackFill[]> LoadFillsAsync(string symbol, DateTime? from,
		DateTime? to, int maximum, CancellationToken cancellationToken)
	{
		var result = new List<BackpackFill>();
		for (var offset = 0; result.Count < maximum;)
		{
			var size = (maximum - result.Count).Min(1000).Max(1);
			var page = await RestClient.GetFillsAsync(new()
			{
				Symbol = symbol,
				From = from?.ToUniversalTime().ToUnixMilliseconds(),
				To = to?.ToUniversalTime().ToUnixMilliseconds(),
				Limit = size,
				Offset = offset,
			}, cancellationToken);
			if (page is not { Length: > 0 })
				break;
			result.AddRange(page);
			offset += page.Length;
			if (page.Length < size)
				break;
		}
		return [.. result.GroupBy(static fill => fill.TradeId)
			.Select(static group => group.First())
			.Take(maximum)];
	}

	private ValueTask OnPositionUpdateAsync(BackpackWsPositionUpdate position,
		CancellationToken cancellationToken)
		=> SendPositionAsync(position, _portfolioSubscriptionId,
			position.EventTime > 0 ? position.EventTime.ToUtcMicroseconds() : CurrentTime,
			cancellationToken);

	private async ValueTask OnOrderUpdateAsync(BackpackWsOrderUpdate update,
		CancellationToken cancellationToken)
	{
		await SendOrderAsync(update, _orderStatusSubscriptionId, cancellationToken);
		if (update.Event == BackpackOrderEvents.Fill && update.TradeId is > 0 &&
			update.FillQuantity is > 0)
			await SendFillAsync(update, _orderStatusSubscriptionId, cancellationToken);
	}

	private ValueTask SendBalanceAsync(BackpackBalance balance, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (balance?.Asset.IsEmpty() != false)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(),
			SecurityId = balance.Asset.ToStockSharp(),
			ServerTime = CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue,
			balance.Available + balance.Locked + balance.Staked, true)
		.TryAdd(PositionChangeTypes.BlockedValue,
			balance.Locked + balance.Staked, true), cancellationToken);
	}

	private ValueTask SendPositionAsync(BackpackPosition position,
		long originalTransactionId, DateTime serverTime,
		CancellationToken cancellationToken)
	{
		if (position?.Symbol.IsEmpty() != false)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(),
			SecurityId = position.Symbol.ToStockSharp(),
			DepoName = position.PositionId,
			ServerTime = serverTime,
			OriginalTransactionId = originalTransactionId,
			Side = position.NetQuantity == 0
				? null
				: position.NetQuantity < 0 ? Sides.Sell : Sides.Buy,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, position.NetQuantity.Abs(), true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.EntryPrice, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, position.MarkPrice, true)
		.TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedPnL, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnL, true)
		.TryAdd(PositionChangeTypes.LiquidationPrice,
			position.EstimatedLiquidationPrice, true), cancellationToken);
	}

	private ValueTask SendPositionAsync(BackpackWsPositionUpdate position,
		long originalTransactionId, DateTime serverTime,
		CancellationToken cancellationToken)
	{
		if (position?.Symbol.IsEmpty() != false || originalTransactionId == 0)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(),
			SecurityId = position.Symbol.ToStockSharp(),
			DepoName = position.PositionId,
			ServerTime = serverTime,
			OriginalTransactionId = originalTransactionId,
			Side = position.NetQuantity == 0
				? null
				: position.NetQuantity < 0 ? Sides.Sell : Sides.Buy,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, position.NetQuantity.Abs(), true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.EntryPrice, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, position.MarkPrice, true)
		.TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedPnL, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnL, true),
			cancellationToken);
	}

	private ValueTask SendOrderAsync(BackpackOrder order, long originalTransactionId,
		long? transactionId, BackpackOrderCondition condition,
		CancellationToken cancellationToken)
	{
		if (order?.Id.IsEmpty() != false || order.Symbol.IsEmpty())
			return default;
		condition ??= new BackpackOrderCondition
		{
			IsReduceOnly = order.IsReduceOnly,
			IsQuoteVolume = order.Quantity is null && order.QuoteQuantity is not null,
		};
		var volume = order.Quantity ?? order.QuoteQuantity;
		var executed = order.Quantity is null
			? order.ExecutedQuoteQuantity
			: order.ExecutedQuantity;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToStockSharp(),
			ServerTime = GetOrderTime(order),
			PortfolioName = GetPortfolioName(),
			Side = order.Side.ToStockSharp(),
			OrderVolume = volume,
			Balance = volume is decimal value ? (value - executed).Max(0m) : null,
			OrderPrice = order.Price ?? 0m,
			AveragePrice = order.ExecutedQuantity > 0
				? order.ExecutedQuoteQuantity / order.ExecutedQuantity
				: null,
			OrderType = order.OrderType.ToStockSharp(),
			OrderState = order.Status.ToStockSharp(),
			OrderStringId = order.Id,
			TransactionId = transactionId ?? order.ClientId ?? 0,
			OriginalTransactionId = originalTransactionId,
			TimeInForce = order.TimeInForce.ToStockSharp(),
			PostOnly = order.IsPostOnly,
			PositionEffect = order.IsReduceOnly
				? OrderPositionEffects.CloseOnly
				: null,
			Condition = condition,
		}, cancellationToken);
	}

	private ValueTask SendOrderAsync(BackpackWsOrderUpdate order,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (order?.OrderId.IsEmpty() != false || order.Symbol.IsEmpty() ||
			originalTransactionId == 0)
			return default;
		var volume = order.Quantity ?? order.QuoteQuantity;
		var executed = order.Quantity is null
			? order.ExecutedQuoteQuantity
			: order.ExecutedQuantity;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToStockSharp(),
			ServerTime = order.EventTime > 0
				? order.EventTime.ToUtcMicroseconds()
				: CurrentTime,
			PortfolioName = GetPortfolioName(),
			Side = order.Side.ToStockSharp(),
			OrderVolume = volume,
			Balance = volume is decimal value ? (value - executed).Max(0m) : null,
			OrderPrice = order.Price ?? 0m,
			AveragePrice = order.ExecutedQuantity > 0
				? order.ExecutedQuoteQuantity / order.ExecutedQuantity
				: null,
			OrderType = order.OrderType == BackpackWsOrderTypes.Market
				? OrderTypes.Market
				: OrderTypes.Limit,
			OrderState = order.Status.ToStockSharp(),
			OrderStringId = order.OrderId,
			TransactionId = order.ClientId ?? 0,
			OriginalTransactionId = originalTransactionId,
			TimeInForce = order.TimeInForce.ToStockSharp(),
			PostOnly = order.IsPostOnly,
			PositionEffect = order.IsReduceOnly
				? OrderPositionEffects.CloseOnly
				: null,
			Commission = order.Fee,
			CommissionCurrency = order.FeeAsset,
			Error = order.Status == BackpackOrderStatuses.TriggerFailed
				? new InvalidOperationException(order.ExpiryReason ??
					"Backpack Exchange trigger order failed.")
				: null,
			Condition = new BackpackOrderCondition
			{
				IsReduceOnly = order.IsReduceOnly,
				IsQuoteVolume = order.Quantity is null && order.QuoteQuantity is not null,
			},
		}, cancellationToken);
	}

	private ValueTask SendFillAsync(BackpackFill fill, long originalTransactionId,
		bool onlyNew, CancellationToken cancellationToken)
	{
		if (fill?.Symbol.IsEmpty() != false || fill.TradeId <= 0)
			return default;
		var fillId = $"{fill.Symbol}:{fill.TradeId}";
		using (_sync.EnterScope())
		{
			var added = _seenFillIds.Add(fillId);
			if (onlyNew && !added)
				return default;
		}
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = fill.Symbol.ToStockSharp(),
			ServerTime = GetFillTime(fill),
			PortfolioName = GetPortfolioName(),
			Side = fill.Side.ToStockSharp(),
			OrderStringId = fill.OrderId,
			TradeId = fill.TradeId,
			TradePrice = fill.Price,
			TradeVolume = fill.Quantity,
			Commission = fill.Fee,
			CommissionCurrency = fill.FeeSymbol,
			TransactionId = ParseTransactionId(fill.ClientId),
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private ValueTask SendFillAsync(BackpackWsOrderUpdate fill,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		var fillId = $"{fill.Symbol}:{fill.TradeId.Value}";
		using (_sync.EnterScope())
			if (!_seenFillIds.Add(fillId))
				return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = fill.Symbol.ToStockSharp(),
			ServerTime = fill.EventTime > 0
				? fill.EventTime.ToUtcMicroseconds()
				: CurrentTime,
			PortfolioName = GetPortfolioName(),
			Side = fill.Side.ToStockSharp(),
			OrderStringId = fill.OrderId,
			TradeId = fill.TradeId,
			TradePrice = fill.FillPrice,
			TradeVolume = fill.FillQuantity,
			Commission = fill.Fee,
			CommissionCurrency = fill.FeeAsset,
			TransactionId = fill.ClientId ?? 0,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private static DateTime GetOrderTime(BackpackOrder order)
		=> order?.CreatedAt.ToBackpackTime() ?? DateTime.MinValue;

	private static DateTime GetFillTime(BackpackFill fill)
		=> fill?.Timestamp.ToBackpackTime() ?? DateTime.MinValue;

	private static long ParseTransactionId(string clientId)
		=> long.TryParse(clientId, NumberStyles.None, CultureInfo.InvariantCulture,
			out var value) ? value : 0;

	private static string ResolveOrderId(long? numericOrderId, string stringOrderId,
		string operation)
	{
		if (!stringOrderId.IsEmpty())
			return stringOrderId;
		if (numericOrderId is > 0)
			return numericOrderId.Value.ToString(CultureInfo.InvariantCulture);
		throw new InvalidOperationException(
			$"Backpack Exchange {operation} requires an exchange order ID.");
	}
}
