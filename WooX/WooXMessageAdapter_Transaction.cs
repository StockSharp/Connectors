namespace StockSharp.WooX;

public partial class WooXMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var symbol = GetSymbol(regMsg.SecurityId);
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException("Order volume must be positive.");
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
			throw new InvalidOperationException("Limit order price must be positive.");

		var condition = regMsg.Condition as WooXOrderCondition ?? new WooXOrderCondition();
		var policy = condition.Policy;
		if (regMsg.PostOnly == true)
			policy = WooXOrderPolicies.PostOnly;
		else if (regMsg.TimeInForce == TimeInForce.CancelBalance)
			policy = WooXOrderPolicies.ImmediateOrCancel;
		else if (regMsg.TimeInForce == TimeInForce.MatchOrCancel)
			policy = WooXOrderPolicies.FillOrKill;
		if (orderType == OrderTypes.Market && policy != WooXOrderPolicies.Regular)
			throw new InvalidOperationException(
				"WOO X execution policies apply to limit orders only.");

		var isPerpetual = symbol.StartsWith("PERP_", StringComparison.OrdinalIgnoreCase);
		if (!isPerpetual && (condition.IsReduceOnly || condition.Leverage is not null ||
			condition.MarginMode == WooXMarginModes.Isolated ||
			condition.PositionSide != WooXPositionSides.Both))
			throw new InvalidOperationException(
				"Futures margin, leverage, position-side, and reduce-only settings require a WOO X perpetual symbol.");
		if (isPerpetual && condition.Leverage is int leverage)
		{
			if (leverage <= 0)
				throw new InvalidOperationException("Leverage must be positive.");
			await RestClient.SetFuturesLeverageAsync(new()
			{
				Symbol = symbol,
				MarginMode = condition.MarginMode,
				PositionSide = condition.PositionSide,
				Leverage = leverage,
			}, cancellationToken);
		}

		var isReduceOnly = isPerpetual && (condition.IsReduceOnly ||
			regMsg.PositionEffect == OrderPositionEffects.CloseOnly);
		var isQuoteVolume = orderType == OrderTypes.Market && condition.IsQuoteVolume;
		var clientOrderId = CreateClientOrderId(regMsg.TransactionId, regMsg.UserOrderId);
		var result = await RestClient.PlaceOrderAsync(new()
		{
			Symbol = symbol,
			ClientOrderId = clientOrderId,
			MarginMode = isPerpetual ? condition.MarginMode : null,
			OrderType = policy.ToWooX(orderType),
			Price = orderType == OrderTypes.Limit ? regMsg.Price : null,
			Quantity = isQuoteVolume ? null : volume,
			Amount = isQuoteVolume ? volume : null,
			IsReduceOnly = isPerpetual ? isReduceOnly : null,
			VisibleQuantity = orderType == OrderTypes.Limit ? regMsg.VisibleVolume : null,
			Side = regMsg.Side.ToWooX(),
			PositionSide = isPerpetual ? condition.PositionSide : null,
		}, cancellationToken);
		if (result.OrderId <= 0)
			throw new InvalidDataException("WOO X accepted the order without returning an order ID.");

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(),
			ServerTime = result.Timestamp <= 0
				? CurrentTime
				: result.Timestamp.ToWooXTime(),
			PortfolioName = GetPortfolioName(),
			Side = regMsg.Side,
			OrderVolume = volume,
			Balance = volume,
			OrderPrice = orderType == OrderTypes.Market ? 0m : regMsg.Price,
			OrderType = orderType,
			OrderState = OrderStates.Active,
			OrderId = result.OrderId,
			TransactionId = regMsg.TransactionId,
			OriginalTransactionId = regMsg.TransactionId,
			TimeInForce = regMsg.TimeInForce,
			PostOnly = policy == WooXOrderPolicies.PostOnly,
			PositionEffect = isReduceOnly ? OrderPositionEffects.CloseOnly : regMsg.PositionEffect,
			Condition = condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		if (replaceMsg.OrderType == OrderTypes.Market)
			throw new NotSupportedException("WOO X can edit active limit orders only.");
		if (replaceMsg.Price <= 0)
			throw new InvalidOperationException("Replacement price must be positive.");
		var volume = replaceMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException("Replacement volume must be positive.");
		var orderId = ResolveOrderId(replaceMsg.OldOrderId, replaceMsg.OldOrderStringId,
			"replacement");
		var result = await RestClient.EditOrderAsync(orderId, new()
		{
			Price = replaceMsg.Price.ToWire(),
			Quantity = volume.ToWire(),
		}, cancellationToken);
		if (result.Data?.IsSuccess == false)
			throw new InvalidOperationException(
				$"WOO X order edit failed: {result.Data.Status}".Trim());

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = GetSymbol(replaceMsg.SecurityId).ToStockSharp(),
			ServerTime = result.Timestamp > 0 ? result.Timestamp.ToWooXTime() : CurrentTime,
			PortfolioName = GetPortfolioName(),
			Side = replaceMsg.Side,
			OrderVolume = volume,
			Balance = volume,
			OrderPrice = replaceMsg.Price,
			OrderType = OrderTypes.Limit,
			OrderState = OrderStates.Active,
			OrderId = orderId,
			TransactionId = replaceMsg.TransactionId,
			OriginalTransactionId = replaceMsg.TransactionId,
			TimeInForce = replaceMsg.TimeInForce,
			PostOnly = replaceMsg.PostOnly,
			PositionEffect = replaceMsg.PositionEffect,
			Condition = replaceMsg.Condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var symbol = GetSymbol(cancelMsg.SecurityId);
		var orderId = ResolveOrderId(cancelMsg.OrderId, cancelMsg.OrderStringId,
			"cancellation");
		await RestClient.CancelOrderAsync(new()
		{
			OrderId = orderId,
			Symbol = symbol,
		}, cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(),
			ServerTime = CurrentTime,
			PortfolioName = GetPortfolioName(),
			OrderId = orderId,
			OrderState = OrderStates.Done,
			Balance = 0m,
			OriginalTransactionId = cancelMsg.TransactionId,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"WOO X order cancellation does not close open positions.");
		if (cancelMsg.Side is not null)
		{
			var orders = await LoadOrdersAsync(cancelMsg.SecurityId.SecurityCode.IsEmpty()
				? null
				: GetSymbol(cancelMsg.SecurityId), "INCOMPLETE", null, null, 500,
				cancellationToken);
			foreach (var order in orders.Where(order => order.Side.ToStockSharp() == cancelMsg.Side))
				await RestClient.CancelOrderAsync(new()
				{
					OrderId = order.OrderId,
					Symbol = order.Symbol,
				}, cancellationToken);
			return;
		}
		if (!cancelMsg.SecurityId.SecurityCode.IsEmpty())
			await RestClient.CancelSymbolOrdersAsync(new()
			{
				Symbol = GetSymbol(cancelMsg.SecurityId),
			}, cancellationToken);
		else
			await RestClient.CancelAllOrdersAsync(cancellationToken);
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
			await PrivateWsClient.UnsubscribeBalancesAsync(cancellationToken);
			await PrivateWsClient.UnsubscribePositionsAsync(cancellationToken);
			return;
		}

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = GetPortfolioName(),
			BoardCode = BoardCodes.WooX,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
			return;
		}
		_portfolioSubscriptionId = lookupMsg.TransactionId;
		await PrivateWsClient.SubscribeBalancesAsync(cancellationToken);
		await PrivateWsClient.SubscribePositionsAsync(cancellationToken);
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
			await PrivateWsClient.UnsubscribeExecutionsAsync(cancellationToken);
			return;
		}

		var symbol = statusMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetSymbol(statusMsg.SecurityId);
		var limit = (statusMsg.Count ?? 100).Min(5000).Max(1).To<int>();
		await SendOrderSnapshotAsync(statusMsg.TransactionId, symbol, statusMsg.From,
			statusMsg.To, limit, cancellationToken);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
			return;
		}
		_orderStatusSubscriptionId = statusMsg.TransactionId;
		await PrivateWsClient.SubscribeExecutionsAsync(cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshotAsync(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var balances = await RestClient.GetBalancesAsync(cancellationToken);
		foreach (var balance in balances.Data?.Holding ?? [])
			await SendBalanceAsync(balance, balances.Timestamp, originalTransactionId,
				cancellationToken);
		var positions = await RestClient.GetPositionsAsync(cancellationToken);
		foreach (var position in positions.Data?.Positions ?? [])
			await SendPositionAsync(position, originalTransactionId, cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(long originalTransactionId, string symbol,
		DateTime? from, DateTime? to, int limit, CancellationToken cancellationToken)
	{
		var orders = await LoadOrdersAsync(symbol, null, from, to, limit, cancellationToken);
		foreach (var order in orders.OrderBy(GetOrderTime))
			await SendOrderAsync(order, originalTransactionId, cancellationToken);

		var fills = await LoadTradesAsync(symbol, from, to, limit, cancellationToken);
		foreach (var fill in fills.OrderBy(static item => item.ExecutedTimestamp.ToUtcSeconds()))
			await SendFillAsync(fill, originalTransactionId, false, cancellationToken);
	}

	private async ValueTask<WooXOrder[]> LoadOrdersAsync(string symbol, string status,
		DateTime? from, DateTime? to, int maximum, CancellationToken cancellationToken)
	{
		var result = new List<WooXOrder>();
		for (var page = 1; result.Count < maximum; page++)
		{
			var size = (maximum - result.Count).Min(500).Max(1);
			var response = await RestClient.GetOrdersAsync(new()
			{
				Symbol = symbol,
				Status = status,
				StartTime = from?.ToUnixMilliseconds(),
				EndTime = to?.ToUnixMilliseconds(),
				Page = page,
				Size = size,
			}, cancellationToken);
			var rows = response.Rows ?? [];
			result.AddRange(rows);
			if (rows.Length < size || response.Meta is { } meta &&
				meta.CurrentPage * meta.RecordsPerPage >= meta.Total)
				break;
		}
		return [.. result.Where(static order => order?.OrderId > 0)
			.GroupBy(static order => order.OrderId)
			.Select(static group => group.OrderByDescending(GetOrderTime).First())
			.Take(maximum)];
	}

	private async ValueTask<WooXTrade[]> LoadTradesAsync(string symbol, DateTime? from,
		DateTime? to, int maximum, CancellationToken cancellationToken)
	{
		var result = new List<WooXTrade>();
		for (var page = 1; result.Count < maximum; page++)
		{
			var size = (maximum - result.Count).Min(500).Max(1);
			var response = await RestClient.GetTradesAsync(new()
			{
				Symbol = symbol,
				StartTime = from?.ToUnixMilliseconds(),
				EndTime = to?.ToUnixMilliseconds(),
				Page = page,
				Size = size,
			}, cancellationToken);
			var rows = response.Rows ?? [];
			result.AddRange(rows);
			if (rows.Length < size || response.Meta is { } meta &&
				meta.CurrentPage * meta.RecordsPerPage >= meta.Total)
				break;
		}
		return [.. result.Where(static fill => fill?.Id > 0)
			.GroupBy(static fill => fill.Id)
			.Select(static group => group.First())
			.Take(maximum)];
	}

	private ValueTask OnPrivateBalanceAsync(WooXBalance balance, long timestamp,
		CancellationToken cancellationToken)
		=> SendBalanceAsync(balance, timestamp, _portfolioSubscriptionId, cancellationToken);

	private ValueTask OnPrivatePositionAsync(WooXPosition position, long timestamp,
		CancellationToken cancellationToken)
	{
		if (position.Timestamp <= 0 && timestamp > 0)
			position.Timestamp = timestamp / 1000m;
		return SendPositionAsync(position, _portfolioSubscriptionId, cancellationToken);
	}

	private async ValueTask OnPrivateExecutionAsync(WooXWsExecutionReport report,
		long timestamp, CancellationToken cancellationToken)
	{
		if (report.MessageType != 0)
		{
			await SendOutErrorAsync(new InvalidOperationException(
				$"WOO X rejected an order operation ({report.MessageType}): {report.Reason}".Trim()),
				cancellationToken);
			return;
		}
		await SendExecutionReportOrderAsync(report, timestamp, cancellationToken);
		if (report.TradeId > 0 && report.ExecutedQuantity > 0)
			await SendExecutionReportFillAsync(report, timestamp, cancellationToken);
	}

	private ValueTask SendBalanceAsync(WooXBalance balance, decimal fallbackTimestamp,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (balance?.Token.IsEmpty() != false)
			return default;
		var available = balance.AvailableBalance ?? (balance.Holding - balance.Frozen);
		var timestamp = balance.UpdatedTime > 0
			? balance.UpdatedTime.ToWooXTime()
			: balance.Timestamp > 0
				? balance.Timestamp.ToUtcTime()
				: fallbackTimestamp > 0 ? fallbackTimestamp.ToWooXTime() : CurrentTime;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(),
			SecurityId = balance.Token.ToStockSharp(),
			ServerTime = timestamp,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, balance.Holding, true)
		.TryAdd(PositionChangeTypes.BlockedValue,
			(balance.Holding - available).Max(balance.Frozen).Max(0m), true)
		.TryAdd(PositionChangeTypes.RealizedPnL, balance.PnL24Hours, true), cancellationToken);
	}

	private ValueTask SendPositionAsync(WooXPosition position, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (position?.Symbol.IsEmpty() != false)
			return default;
		var side = position.PositionSide switch
		{
			WooXPositionSides.Long => Sides.Buy,
			WooXPositionSides.Short => Sides.Sell,
			_ when position.Holding < 0 => Sides.Sell,
			_ => Sides.Buy,
		};
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(),
			SecurityId = position.Symbol.ToStockSharp(),
			DepoName = $"{position.MarginMode}:{position.PositionSide}",
			ServerTime = position.Timestamp > 0 ? position.Timestamp.ToWooXTime() : CurrentTime,
			OriginalTransactionId = originalTransactionId,
			Side = side,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, position.Holding.Abs(), true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.AverageOpenPrice, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, position.MarkPrice, true)
		.TryAdd(PositionChangeTypes.RealizedPnL, position.PnL24Hours, true)
		.TryAdd(PositionChangeTypes.LiquidationPrice, position.EstimatedLiquidationPrice, true)
		.TryAdd(PositionChangeTypes.Leverage, position.Leverage, true), cancellationToken);
	}

	private ValueTask SendOrderAsync(WooXOrder order, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (order?.Symbol.IsEmpty() != false || order.OrderId <= 0)
			return default;
		var volume = order.Quantity ?? order.Amount;
		var condition = new WooXOrderCondition
		{
			MarginMode = order.MarginMode ?? WooXMarginModes.Cross,
			PositionSide = order.PositionSide ?? WooXPositionSides.Both,
			Policy = order.Type switch
			{
				WooXOrderTypes.ImmediateOrCancel => WooXOrderPolicies.ImmediateOrCancel,
				WooXOrderTypes.FillOrKill => WooXOrderPolicies.FillOrKill,
				WooXOrderTypes.PostOnly => WooXOrderPolicies.PostOnly,
				_ => WooXOrderPolicies.Regular,
			},
			Leverage = order.Leverage,
			IsReduceOnly = order.IsReduceOnly,
			IsQuoteVolume = order.Quantity is null && order.Amount is not null,
		};
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToStockSharp(),
			ServerTime = GetOrderTime(order),
			PortfolioName = GetPortfolioName(),
			Side = order.Side.ToStockSharp(),
			OrderVolume = volume,
			Balance = volume is decimal value ? (value - order.Executed).Max(0m) : null,
			OrderPrice = order.Price,
			AveragePrice = order.AverageExecutedPrice,
			OrderType = order.Type.ToStockSharp(),
			OrderState = order.Status.ToStockSharp(),
			OrderId = order.OrderId,
			TransactionId = ParseTransactionId(order.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
			TimeInForce = order.Type.ToStockSharpTimeInForce(),
			PostOnly = order.Type == WooXOrderTypes.PostOnly,
			PositionEffect = order.IsReduceOnly ? OrderPositionEffects.CloseOnly : null,
			Commission = order.TotalFee,
			Condition = condition,
		}, cancellationToken);
	}

	private ValueTask SendFillAsync(WooXTrade fill, long originalTransactionId, bool onlyNew,
		CancellationToken cancellationToken)
	{
		if (fill?.Symbol.IsEmpty() != false || fill.Id <= 0)
			return default;
		var fillId = fill.Id.ToString(CultureInfo.InvariantCulture);
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
			ServerTime = fill.ExecutedTimestamp.ToUtcSeconds(),
			PortfolioName = GetPortfolioName(),
			Side = fill.Side.ToStockSharp(),
			OrderId = fill.OrderId,
			TradeId = fill.Id,
			TradePrice = fill.Price,
			TradeVolume = fill.Quantity,
			Commission = fill.Fee,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private ValueTask SendExecutionReportOrderAsync(WooXWsExecutionReport report,
		long fallbackTimestamp, CancellationToken cancellationToken)
	{
		var condition = new WooXOrderCondition
		{
			MarginMode = report.MarginMode ?? WooXMarginModes.Cross,
			PositionSide = report.PositionSide ?? WooXPositionSides.Both,
			Policy = report.Type switch
			{
				WooXOrderTypes.ImmediateOrCancel => WooXOrderPolicies.ImmediateOrCancel,
				WooXOrderTypes.FillOrKill => WooXOrderPolicies.FillOrKill,
				WooXOrderTypes.PostOnly => WooXOrderPolicies.PostOnly,
				_ => WooXOrderPolicies.Regular,
			},
			Leverage = report.Leverage,
			IsReduceOnly = report.IsReduceOnly,
		};
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = report.Symbol.ToStockSharp(),
			ServerTime = (report.Timestamp > 0 ? report.Timestamp : fallbackTimestamp).ToUtcTime(),
			PortfolioName = GetPortfolioName(),
			Side = report.Side.ToStockSharp(),
			OrderVolume = report.Quantity,
			Balance = (report.Quantity - report.TotalExecutedQuantity).Max(0m),
			OrderPrice = report.Price,
			AveragePrice = report.AveragePrice,
			OrderType = report.Type.ToStockSharp(),
			OrderState = report.Status.ToStockSharp(),
			OrderId = report.OrderId,
			TransactionId = ParseTransactionId(report.ClientOrderId),
			OriginalTransactionId = _orderStatusSubscriptionId,
			TimeInForce = report.Type.ToStockSharpTimeInForce(),
			PostOnly = report.Type == WooXOrderTypes.PostOnly,
			PositionEffect = report.IsReduceOnly ? OrderPositionEffects.CloseOnly : null,
			Commission = report.TotalFee,
			Error = report.Status == WooXOrderStatuses.Rejected
				? new InvalidOperationException(report.Reason)
				: null,
			Condition = condition,
		}, cancellationToken);
	}

	private ValueTask SendExecutionReportFillAsync(WooXWsExecutionReport report,
		long fallbackTimestamp, CancellationToken cancellationToken)
	{
		var fillId = report.TradeId.ToString(CultureInfo.InvariantCulture);
		using (_sync.EnterScope())
			if (!_seenFillIds.Add(fillId))
				return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = report.Symbol.ToStockSharp(),
			ServerTime = (report.Timestamp > 0 ? report.Timestamp : fallbackTimestamp).ToUtcTime(),
			PortfolioName = GetPortfolioName(),
			Side = report.Side.ToStockSharp(),
			OrderId = report.OrderId,
			TradeId = report.TradeId,
			TradePrice = report.ExecutedPrice,
			TradeVolume = report.ExecutedQuantity,
			Commission = report.Fee,
			OriginalTransactionId = _orderStatusSubscriptionId,
		}, cancellationToken);
	}

	private static DateTime GetOrderTime(WooXOrder order)
		=> !order.UpdatedTime.IsEmpty()
			? order.UpdatedTime.ToUtcSeconds()
			: !order.CreatedTime.IsEmpty() ? order.CreatedTime.ToUtcSeconds() : DateTime.MinValue;

	private static long ResolveOrderId(long? numericOrderId, string stringOrderId,
		string operation)
	{
		if (numericOrderId is > 0)
			return numericOrderId.Value;
		if (long.TryParse(stringOrderId, NumberStyles.None, CultureInfo.InvariantCulture,
			out var parsed) && parsed > 0)
			return parsed;
		throw new InvalidOperationException(
			$"WOO X {operation} requires a numeric exchange order ID.");
	}
}
