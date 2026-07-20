namespace StockSharp.StandX;

public partial class StandXMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var instrument = GetInstrument(regMsg.SecurityId);
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException(
				"StandX order volume must be positive.");
		var volumeStep = StandXExtensions.GetStep(
			instrument.QuantityTickDecimals, "quantity");
		if (volume % volumeStep != 0)
			throw new InvalidOperationException(
				$"StandX order volume must be a multiple of {volumeStep}.");
		if (instrument.MinimumOrderQuantity.ToDecimal() is decimal minimum &&
			volume < minimum)
			throw new InvalidOperationException(
				$"StandX order volume must be at least {minimum}.");
		if (instrument.MaximumOrderQuantity.ToDecimal() is decimal maximum &&
			volume > maximum)
			throw new InvalidOperationException(
				$"StandX order volume cannot exceed {maximum}.");

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
			throw new InvalidOperationException(
				"StandX limit order price must be positive.");
		if (orderType == OrderTypes.Limit)
		{
			var priceStep = StandXExtensions.GetStep(
				instrument.PriceTickDecimals, "price");
			if (regMsg.Price % priceStep != 0)
				throw new InvalidOperationException(
					$"StandX order price must be a multiple of {priceStep}.");
		}

		var condition = regMsg.Condition as StandXOrderCondition ?? new();
		if (condition.MarginMode is StandXMarginModes marginMode &&
			!Enum.IsDefined(marginMode))
			throw new InvalidOperationException(
				"StandX margin mode is invalid.");
		if (condition.Leverage is int leverage)
		{
			if (leverage <= 0)
				throw new InvalidOperationException(
					"StandX leverage must be positive.");
			if (instrument.MaximumLeverage.ToDecimal() is decimal maximumLeverage &&
				leverage > maximumLeverage)
				throw new InvalidOperationException(
					$"StandX leverage cannot exceed {maximumLeverage}.");
		}
		if (condition.TakeProfitPrice is <= 0 ||
			condition.StopLossPrice is <= 0)
			throw new InvalidOperationException(
				"StandX take-profit and stop-loss prices must be positive.");

		StandXTimeInForces timeInForce;
		if (regMsg.PostOnly == true)
		{
			if (orderType != OrderTypes.Limit)
				throw new InvalidOperationException(
					"StandX post-only policy applies only to limit orders.");
			timeInForce = StandXTimeInForces.AddLiquidityOnly;
		}
		else if (regMsg.TimeInForce == TimeInForce.MatchOrCancel)
		{
			throw new NotSupportedException(
				"StandX does not expose a fill-or-kill policy.");
		}
		else if (orderType == OrderTypes.Market ||
			regMsg.TimeInForce == TimeInForce.CancelBalance)
		{
			timeInForce = StandXTimeInForces.ImmediateOrCancel;
		}
		else
		{
			timeInForce = StandXTimeInForces.GoodTillCanceled;
		}

		var clientOrderId = CreateClientOrderId(regMsg.TransactionId,
			regMsg.UserOrderId);
		using (_sync.EnterScope())
		{
			if (_transactionByClientOrderId.ContainsKey(clientOrderId))
				throw new InvalidOperationException(
					$"StandX client order ID '{clientOrderId}' is already in use.");
			_transactionByClientOrderId.Add(clientOrderId,
				regMsg.TransactionId);
		}
		try
		{
			await OrderSocket.PlaceOrderAsync(new()
			{
				Symbol = instrument.Symbol,
				Side = regMsg.Side.ToStandX(),
				OrderType = orderType == OrderTypes.Market
					? StandXApiOrderTypes.Market
					: StandXApiOrderTypes.Limit,
				Quantity = volume.ToWire(),
				Price = orderType == OrderTypes.Limit
					? regMsg.Price.ToWire()
					: null,
				TimeInForce = timeInForce,
				IsReduceOnly = condition.IsReduceOnly ||
					regMsg.PositionEffect == OrderPositionEffects.CloseOnly,
				ClientOrderId = clientOrderId,
				MarginMode = condition.MarginMode?.ToStandX(),
				Leverage = condition.Leverage,
				TakeProfitPrice = condition.TakeProfitPrice?.ToWire(),
				StopLossPrice = condition.StopLossPrice?.ToWire(),
			}, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_transactionByClientOrderId.Remove(clientOrderId);
			throw;
		}

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = instrument.Symbol.ToStockSharp(),
			ServerTime = ServerTime,
			PortfolioName = _portfolioName,
			Side = regMsg.Side,
			OrderVolume = volume,
			Balance = volume,
			OrderPrice = orderType == OrderTypes.Market ? 0m : regMsg.Price,
			OrderType = orderType,
			OrderState = OrderStates.Pending,
			UserOrderId = clientOrderId,
			TransactionId = regMsg.TransactionId,
			OriginalTransactionId = regMsg.TransactionId,
			TimeInForce = timeInForce.ToStockSharp(),
			PostOnly = timeInForce == StandXTimeInForces.AddLiquidityOnly,
			PositionEffect = condition.IsReduceOnly
				? OrderPositionEffects.CloseOnly
				: regMsg.PositionEffect,
			Condition = condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		_ = replaceMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"StandX API does not expose an order-amend operation.");
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		long? orderId = cancelMsg.OrderId;
		if (orderId is null && !cancelMsg.OrderStringId.IsEmpty() &&
			long.TryParse(cancelMsg.OrderStringId, NumberStyles.None,
				CultureInfo.InvariantCulture, out var parsed))
			orderId = parsed;
		var clientOrderId = cancelMsg.UserOrderId;
		if (orderId is null && clientOrderId.IsEmpty() &&
			!cancelMsg.OrderStringId.IsEmpty())
			clientOrderId = cancelMsg.OrderStringId;
		if (orderId is null && clientOrderId.IsEmpty())
			throw new InvalidOperationException(
				"StandX cancellation requires an order or client order ID.");
		await OrderSocket.CancelOrderAsync(new()
		{
			OrderId = orderId,
			ClientOrderId = orderId is null ? clientOrderId : null,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"StandX batch cancellation does not close positions.");
		if (cancelMsg.SecurityTypes is { Length: > 0 } &&
			!cancelMsg.SecurityTypes.Contains(SecurityTypes.Future))
			return;
		var symbol = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetInstrument(cancelMsg.SecurityId).Symbol;
		var openOrders = (await RestClient.GetOpenOrdersAsync(symbol, 1200,
			cancellationToken)).Result ?? [];
		foreach (var order in openOrders.Where(order => order is not null &&
			(cancelMsg.Side is null || order.Side.ToStockSharp() == cancelMsg.Side)))
			await OrderSocket.CancelOrderAsync(new() { OrderId = order.Id },
				cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsurePrivateReady();
		if (!lookupMsg.IsSubscribe)
		{
			_portfolioSubscriptionId = 0;
			await MarketSocket.UnsubscribeAsync(StandXChannels.Balance, null,
				cancellationToken);
			await MarketSocket.UnsubscribeAsync(StandXChannels.Position, null,
				cancellationToken);
			return;
		}

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = _portfolioName,
			BoardCode = BoardCodes.StandX,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId,
			cancellationToken);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId,
				cancellationToken);
			return;
		}
		_portfolioSubscriptionId = lookupMsg.TransactionId;
		await MarketSocket.SubscribeAsync(StandXChannels.Balance, null,
			cancellationToken);
		await MarketSocket.SubscribeAsync(StandXChannels.Position, null,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId,
			cancellationToken);
		EnsurePrivateReady();
		if (!statusMsg.IsSubscribe)
		{
			_orderStatusSubscriptionId = 0;
			await MarketSocket.UnsubscribeAsync(StandXChannels.Order, null,
				cancellationToken);
			await MarketSocket.UnsubscribeAsync(StandXChannels.Trade, null,
				cancellationToken);
			return;
		}
		var symbol = statusMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetInstrument(statusMsg.SecurityId).Symbol;
		var limit = (statusMsg.Count ?? 500).Min(500).Max(1).To<int>();
		await SendOrderSnapshotAsync(statusMsg.TransactionId, symbol,
			statusMsg.From, statusMsg.To, limit, cancellationToken);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}
		_orderStatusSubscriptionId = statusMsg.TransactionId;
		await MarketSocket.SubscribeAsync(StandXChannels.Order, null,
			cancellationToken);
		await MarketSocket.SubscribeAsync(StandXChannels.Trade, null,
			cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshotAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		await SendBalanceAsync(await RestClient.GetBalanceAsync(cancellationToken),
			transactionId, cancellationToken);
		foreach (var position in await RestClient.GetPositionsAsync(null,
			cancellationToken) ?? [])
			await SendPositionAsync(position, transactionId, cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(long transactionId,
		string symbol, DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
	{
		var open = (await RestClient.GetOpenOrdersAsync(symbol,
			limit.Min(1200), cancellationToken)).Result ?? [];
		var history = (await RestClient.GetOrdersAsync(symbol, from, to, limit,
			cancellationToken)).Result ?? [];
		foreach (var order in open.Concat(history)
			.Where(static order => order is not null && order.Id > 0)
			.GroupBy(static order => order.Id)
			.Select(static group => group.OrderByDescending(item =>
				item.UpdatedAt.ToStandXTime()).First())
			.OrderBy(static order => order.UpdatedAt.ToStandXTime()))
			await SendOrderAsync(order, transactionId, cancellationToken);

		foreach (var trade in ((await RestClient.GetUserTradesAsync(symbol,
			from, to, limit, cancellationToken)).Result ?? [])
			.Where(static trade => trade is not null && trade.Id > 0)
			.GroupBy(static trade => trade.Id)
			.Select(static group => group.First())
			.OrderBy(static trade => trade.CreatedAt.ToStandXTime()))
			await SendUserTradeAsync(trade, transactionId, false,
				cancellationToken);
	}

	private ValueTask OnOrderAsync(StandXOrder order,
		CancellationToken cancellationToken)
		=> SendOrderAsync(order, _orderStatusSubscriptionId, cancellationToken);

	private ValueTask OnPositionAsync(StandXPosition position,
		CancellationToken cancellationToken)
		=> SendPositionAsync(position, _portfolioSubscriptionId,
			cancellationToken);

	private ValueTask OnWalletBalanceAsync(StandXWalletBalance balance,
		CancellationToken cancellationToken)
		=> SendWalletBalanceAsync(balance, _portfolioSubscriptionId,
			cancellationToken);

	private ValueTask OnUserTradeAsync(StandXUserTrade trade,
		CancellationToken cancellationToken)
		=> SendUserTradeAsync(trade, _orderStatusSubscriptionId, true,
			cancellationToken);

	private ValueTask SendBalanceAsync(StandXBalance balance,
		long transactionId, CancellationToken cancellationToken)
	{
		if (balance is null || transactionId == 0)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = "DUSD".ToCurrencySecurity(),
			ServerTime = ServerTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue,
			balance.Equity.ToDecimal() ?? balance.TotalBalance.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.BlockedValue,
			balance.Locked.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL,
			balance.UnrealizedProfitLoss.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.RealizedPnL,
			balance.FrozenProfitLoss.ToDecimal(), true), cancellationToken);
	}

	private ValueTask SendWalletBalanceAsync(StandXWalletBalance balance,
		long transactionId, CancellationToken cancellationToken)
	{
		if (balance?.Token.IsEmpty() != false || transactionId == 0)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = balance.Token.ToCurrencySecurity(),
			ServerTime = balance.UpdatedAt.ToStandXTime() ?? ServerTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue,
			balance.Total.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.BlockedValue,
			balance.Locked.ToDecimal(), true), cancellationToken);
	}

	private ValueTask SendPositionAsync(StandXPosition position,
		long transactionId, CancellationToken cancellationToken)
	{
		if (position?.Symbol.IsEmpty() != false || transactionId == 0)
			return default;
		var signedQuantity = position.Quantity.ToDecimal() ?? 0m;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = position.Symbol.ToStockSharp(),
			DepoName = position.Id.ToString(CultureInfo.InvariantCulture),
			ServerTime = position.Time.ToStandXTime() ??
				position.UpdatedAt.ToStandXTime() ?? ServerTime,
			OriginalTransactionId = transactionId,
			Side = signedQuantity < 0 ? Sides.Sell : Sides.Buy,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, signedQuantity.Abs(), true)
		.TryAdd(PositionChangeTypes.AveragePrice,
			position.EntryPrice.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.CurrentPrice,
			position.MarkPrice.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL,
			position.UnrealizedProfitLoss.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.RealizedPnL,
			position.RealizedProfitLoss.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.LiquidationPrice,
			position.LiquidationPrice.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.Leverage,
			position.Leverage.ToDecimal(), true), cancellationToken);
	}

	private ValueTask SendOrderAsync(StandXOrder order, long transactionId,
		CancellationToken cancellationToken)
	{
		if (order?.Symbol.IsEmpty() != false || order.Id <= 0)
			return default;
		var volume = order.Quantity.ToDecimal();
		var filled = order.FilledQuantity.ToDecimal() ?? 0m;
		var state = order.Status.ToStockSharp();
		var condition = new StandXOrderCondition
		{
			Leverage = order.Leverage.ToDecimal() is decimal leverage
				? decimal.ToInt32(decimal.Truncate(leverage))
				: null,
			IsReduceOnly = order.IsReduceOnly,
		};
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToStockSharp(),
			ServerTime = order.UpdatedAt.ToStandXTime() ??
				order.CreatedAt.ToStandXTime() ?? ServerTime,
			PortfolioName = _portfolioName,
			Side = order.Side.ToStockSharp(),
			OrderVolume = volume,
			Balance = volume is null ? null : (volume.Value - filled).Max(0m),
			OrderPrice = order.Price.ToDecimal() ?? 0m,
			AveragePrice = order.AveragePrice.ToDecimal(),
			OrderType = order.OrderType.ToStockSharp(),
			OrderState = state,
			OrderId = order.Id,
			UserOrderId = order.ClientOrderId,
			TransactionId = GetTransactionId(order.ClientOrderId),
			OriginalTransactionId = transactionId,
			TimeInForce = order.TimeInForce.ToStockSharp(),
			PostOnly = order.TimeInForce ==
				StandXTimeInForces.AddLiquidityOnly,
			PositionEffect = order.IsReduceOnly
				? OrderPositionEffects.CloseOnly
				: null,
			Condition = condition,
			Error = state == OrderStates.Failed
				? new InvalidOperationException(order.Remark.IsEmpty()
					? "StandX rejected the order."
					: order.Remark)
				: null,
		}, cancellationToken);
	}

	private ValueTask SendUserTradeAsync(StandXUserTrade trade,
		long transactionId, bool onlyNew, CancellationToken cancellationToken)
	{
		if (trade?.Symbol.IsEmpty() != false || trade.Id <= 0 ||
			transactionId == 0)
			return default;
		var isNew = TryAcceptUserTrade(trade.Id);
		if (onlyNew && !isNew)
			return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = trade.Symbol.ToStockSharp(),
			ServerTime = trade.UpdatedAt.ToStandXTime() ??
				trade.CreatedAt.ToStandXTime() ?? ServerTime,
			PortfolioName = _portfolioName,
			Side = trade.Side.ToStockSharp(),
			OrderId = trade.OrderId,
			TradeId = trade.Id,
			TradePrice = trade.Price.ParseRequiredDecimal("fill price"),
			TradeVolume = trade.Quantity.ParseRequiredDecimal("fill quantity"),
			Commission = trade.FeeQuantity.ToDecimal(),
			CommissionCurrency = trade.FeeAsset,
			PnL = trade.ProfitLoss.ToDecimal(),
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}
}
