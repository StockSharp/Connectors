namespace StockSharp.BloFin;

public partial class BloFinMessageAdapter
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

		var condition = regMsg.Condition as BloFinOrderCondition ?? new BloFinOrderCondition();
		var policy = condition.Policy;
		if (regMsg.PostOnly == true)
			policy = BloFinOrderPolicies.PostOnly;
		else if (regMsg.TimeInForce == TimeInForce.CancelBalance)
			policy = BloFinOrderPolicies.ImmediateOrCancel;
		else if (regMsg.TimeInForce == TimeInForce.MatchOrCancel)
			policy = BloFinOrderPolicies.FillOrKill;
		if (orderType == OrderTypes.Market && policy != BloFinOrderPolicies.Regular)
			throw new InvalidOperationException("BloFin execution policies apply only to limit orders.");

		if (condition.Leverage is decimal leverage)
		{
			if (leverage <= 0)
				throw new InvalidOperationException("Leverage must be positive.");
			var leverageResult = await RestClient.SetLeverageAsync(new()
			{
				InstrumentId = symbol,
				Leverage = leverage.ToWire(),
				MarginMode = condition.MarginMode,
				PositionSide = condition.PositionSide,
			}, cancellationToken);
			if (leverageResult is null)
				throw new InvalidDataException("BloFin accepted the leverage change without a result.");
		}

		var clientOrderId = CreateClientOrderId(regMsg.TransactionId, regMsg.UserOrderId);
		var result = (await RestClient.PlaceOrderAsync(new()
		{
			InstrumentId = symbol,
			MarginMode = condition.MarginMode,
			PositionSide = condition.PositionSide,
			Side = regMsg.Side == Sides.Buy ? BloFinSides.Buy : BloFinSides.Sell,
			OrderType = policy.ToBloFin(orderType),
			Price = orderType == OrderTypes.Limit ? regMsg.Price.ToWire() : null,
			Size = volume.ToWire(),
			IsReduceOnly = condition.IsReduceOnly ||
				regMsg.PositionEffect == OrderPositionEffects.CloseOnly ? true : null,
			ClientOrderId = clientOrderId,
			TakeProfitTriggerPrice = ToTriggerPrice(condition.TakeProfitTriggerPrice),
			TakeProfitOrderPrice = ToAttachedOrderPrice(condition.TakeProfitTriggerPrice,
				condition.TakeProfitOrderPrice),
			TakeProfitTriggerPriceType = condition.TakeProfitTriggerPrice is null
				? null
				: condition.TriggerPriceType,
			StopLossTriggerPrice = ToTriggerPrice(condition.StopLossTriggerPrice),
			StopLossOrderPrice = ToAttachedOrderPrice(condition.StopLossTriggerPrice,
				condition.StopLossOrderPrice),
			StopLossTriggerPriceType = condition.StopLossTriggerPrice is null
				? null
				: condition.TriggerPriceType,
		}, cancellationToken) ?? []).FirstOrDefault();
		ThrowIfOperationFailed(result, "place order");
		if (result.OrderId.IsEmpty())
			throw new InvalidDataException("BloFin accepted the order without returning an order ID.");

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(),
			ServerTime = CurrentTime,
			PortfolioName = _portfolioName,
			Side = regMsg.Side,
			OrderVolume = volume,
			Balance = volume,
			OrderPrice = orderType == OrderTypes.Market ? 0m : regMsg.Price,
			OrderType = orderType,
			OrderState = OrderStates.Active,
			OrderStringId = result.OrderId,
			TransactionId = regMsg.TransactionId,
			OriginalTransactionId = regMsg.TransactionId,
			TimeInForce = regMsg.TimeInForce,
			PostOnly = policy == BloFinOrderPolicies.PostOnly,
			PositionEffect = condition.IsReduceOnly
				? OrderPositionEffects.CloseOnly
				: regMsg.PositionEffect,
			Condition = condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		_ = replaceMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"BloFin OpenAPI does not expose an endpoint for amending regular orders.");
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var symbol = GetSymbol(cancelMsg.SecurityId);
		var orderId = cancelMsg.OrderStringId;
		if (orderId.IsEmpty() && cancelMsg.OrderId is long numericOrderId)
			orderId = numericOrderId.ToString(CultureInfo.InvariantCulture);
		if (orderId.IsEmpty())
			throw new InvalidOperationException("BloFin cancellation requires an exchange order ID.");

		var result = await RestClient.CancelOrderAsync(new()
		{
			InstrumentId = symbol,
			OrderId = orderId,
		}, cancellationToken);
		ThrowIfOperationFailed(result, $"cancel order {orderId}");

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(),
			ServerTime = CurrentTime,
			PortfolioName = _portfolioName,
			OrderStringId = orderId,
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
				"BloFin batch cancellation does not close open positions.");
		if (cancelMsg.SecurityTypes is { Length: > 0 } &&
			!cancelMsg.SecurityTypes.Contains(SecurityTypes.Future))
			return;

		var symbol = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetSymbol(cancelMsg.SecurityId);
		var pending = await RestClient.GetPendingOrdersAsync(new()
		{
			InstrumentId = symbol,
			Limit = 100,
		}, cancellationToken) ?? [];
		var requests = pending
			.Where(order => order?.OrderId.IsEmpty() == false &&
				(cancelMsg.Side is null || order.Side.ToStockSharpSide() == cancelMsg.Side))
			.Select(static order => new BloFinCancelOrderRequest
			{
				InstrumentId = order.InstrumentId,
				OrderId = order.OrderId,
			})
			.ToArray();
		for (var offset = 0; offset < requests.Length; offset += 20)
		{
			var results = await RestClient.CancelOrdersAsync(
				[.. requests.Skip(offset).Take(20)], cancellationToken) ?? [];
			foreach (var result in results)
				ThrowIfOperationFailed(result, $"cancel order {result?.OrderId}");
		}
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
			await PrivateWsClient.UnsubscribeAccountAsync(cancellationToken);
			await PrivateWsClient.UnsubscribePositionsAsync(cancellationToken);
			return;
		}

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = _portfolioName,
			BoardCode = BoardCodes.BloFin,
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
		await PrivateWsClient.SubscribeAccountAsync(cancellationToken);
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
			await PrivateWsClient.UnsubscribeOrdersAsync(cancellationToken);
			return;
		}

		var symbol = statusMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetSymbol(statusMsg.SecurityId);
		var limit = (statusMsg.Count ?? 100).Min(100).Max(1).To<int>();
		await SendOrderSnapshotAsync(statusMsg.TransactionId, symbol, statusMsg.From,
			statusMsg.To, limit, cancellationToken);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
			return;
		}
		_orderStatusSubscriptionId = statusMsg.TransactionId;
		await PrivateWsClient.SubscribeOrdersAsync(cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshotAsync(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var account = await RestClient.GetAccountAsync(cancellationToken);
		if (account is not null)
			await OnAccountAsync(account, originalTransactionId, cancellationToken);
		foreach (var position in await RestClient.GetPositionsAsync(null, cancellationToken) ?? [])
			await SendPositionAsync(position, originalTransactionId, cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(long originalTransactionId, string symbol,
		DateTime? from, DateTime? to, int limit, CancellationToken cancellationToken)
	{
		var query = new BloFinOrdersQuery
		{
			InstrumentId = symbol,
			Begin = from?.ToUnixMilliseconds(),
			End = to?.ToUnixMilliseconds(),
			Limit = limit,
		};
		var pending = await RestClient.GetPendingOrdersAsync(query, cancellationToken) ?? [];
		var history = await RestClient.GetOrderHistoryAsync(query, cancellationToken) ?? [];
		foreach (var order in pending.Concat(history)
			.Where(static order => order?.OrderId.IsEmpty() == false)
			.GroupBy(static order => order.OrderId)
			.Select(static group => group.First())
			.OrderBy(static order => order.UpdateTime))
			await SendOrderAsync(order, originalTransactionId, cancellationToken);

		foreach (var fill in (await RestClient.GetFillsAsync(new()
		{
			InstrumentId = symbol,
			Begin = from?.ToUnixMilliseconds(),
			End = to?.ToUnixMilliseconds(),
			Limit = limit,
		}, cancellationToken) ?? []).OrderBy(static fill => fill.Timestamp))
			await SendFillAsync(fill, originalTransactionId, false, cancellationToken);
	}

	private async ValueTask OnOrderAsync(BloFinOrder order,
		CancellationToken cancellationToken)
	{
		await SendOrderAsync(order, _orderStatusSubscriptionId, cancellationToken);
		if (order?.OrderId.IsEmpty() != false || order.FilledSize.ToDecimal() is not > 0)
			return;
		foreach (var fill in await RestClient.GetFillsAsync(new()
		{
			InstrumentId = order.InstrumentId,
			OrderId = order.OrderId,
			Limit = 100,
		}, cancellationToken) ?? [])
			await SendFillAsync(fill, _orderStatusSubscriptionId, true, cancellationToken);
	}

	private ValueTask OnPositionAsync(BloFinPosition position,
		CancellationToken cancellationToken)
		=> SendPositionAsync(position, _portfolioSubscriptionId, cancellationToken);

	private ValueTask OnAccountAsync(BloFinAccount account,
		CancellationToken cancellationToken)
		=> OnAccountAsync(account, _portfolioSubscriptionId, cancellationToken);

	private async ValueTask OnAccountAsync(BloFinAccount account, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		foreach (var balance in account?.Details ?? [])
			await SendBalanceAsync(balance, account.Timestamp, originalTransactionId,
				cancellationToken);
	}

	private ValueTask SendBalanceAsync(BloFinBalance balance, long accountTimestamp,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (balance?.Currency.IsEmpty() != false)
			return default;
		var total = balance.Equity.ToDecimal() ?? balance.Balance.ToDecimal();
		var available = balance.Available.ToDecimal() ?? balance.AvailableEquity.ToDecimal();
		decimal? blocked = total is decimal totalValue && available is decimal availableValue
			? (totalValue - availableValue).Max(0m)
			: balance.Frozen.ToDecimal() ?? balance.OrderFrozen.ToDecimal();
		var timestamp = balance.Timestamp > 0 ? balance.Timestamp : accountTimestamp;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = balance.Currency.ToStockSharp(),
			ServerTime = timestamp > 0 ? timestamp.ToUtcTime() : CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, total, true)
		.TryAdd(PositionChangeTypes.BlockedValue, blocked, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL,
			(balance.UnrealizedPnl.ToDecimal() ?? 0m) +
			(balance.IsolatedUnrealizedPnl.ToDecimal() ?? 0m), true), cancellationToken);
	}

	private ValueTask SendPositionAsync(BloFinPosition position, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (position?.InstrumentId.IsEmpty() != false)
			return default;
		var signedValue = position.Positions.ToDecimal() ?? 0m;
		var side = position.PositionSide switch
		{
			BloFinPositionSides.Long => Sides.Buy,
			BloFinPositionSides.Short => Sides.Sell,
			BloFinPositionSides.Net when signedValue < 0 => Sides.Sell,
			_ => Sides.Buy,
		};
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = position.InstrumentId.ToStockSharp(),
			DepoName = position.PositionId,
			ServerTime = (position.UpdateTime > 0 ? position.UpdateTime : position.CreateTime) is var time &&
				time > 0 ? time.ToUtcTime() : CurrentTime,
			OriginalTransactionId = originalTransactionId,
			Side = side,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, signedValue.Abs(), true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.CurrentPrice, position.MarkPrice.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnl.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedPnl.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.LiquidationPrice, position.LiquidationPrice.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.Leverage, position.Leverage.ToDecimal(), true), cancellationToken);
	}

	private ValueTask SendOrderAsync(BloFinOrder order, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (order?.InstrumentId.IsEmpty() != false || order.OrderId.IsEmpty())
			return default;
		var volume = order.Size.ToDecimal();
		var filled = order.FilledSize.ToDecimal() ?? 0m;
		var policy = order.OrderType switch
		{
			BloFinApiOrderTypes.ImmediateOrCancel => BloFinOrderPolicies.ImmediateOrCancel,
			BloFinApiOrderTypes.FillOrKill => BloFinOrderPolicies.FillOrKill,
			BloFinApiOrderTypes.PostOnly => BloFinOrderPolicies.PostOnly,
			_ => BloFinOrderPolicies.Regular,
		};
		var condition = new BloFinOrderCondition
		{
			MarginMode = order.MarginMode,
			PositionSide = order.PositionSide,
			Policy = policy,
			Leverage = order.Leverage.ToDecimal(),
			IsReduceOnly = order.IsReduceOnly,
			TakeProfitTriggerPrice = order.TakeProfitTriggerPrice.ToDecimal(),
			TakeProfitOrderPrice = order.TakeProfitOrderPrice.ToDecimal(),
			StopLossTriggerPrice = order.StopLossTriggerPrice.ToDecimal(),
			StopLossOrderPrice = order.StopLossOrderPrice.ToDecimal(),
			TriggerPriceType = order.TakeProfitTriggerPriceType ??
				order.StopLossTriggerPriceType ?? BloFinTriggerPriceTypes.Last,
		};
		var orderType = order.OrderType == BloFinApiOrderTypes.Market
			? OrderTypes.Market
			: OrderTypes.Limit;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.InstrumentId.ToStockSharp(),
			ServerTime = (order.UpdateTime > 0 ? order.UpdateTime : order.CreateTime) is var time &&
				time > 0 ? time.ToUtcTime() : CurrentTime,
			PortfolioName = _portfolioName,
			Side = order.Side.ToStockSharpSide(),
			OrderVolume = volume,
			Balance = volume is null ? null : (volume.Value - filled).Max(0m),
			OrderPrice = order.Price.ToDecimal() ?? 0m,
			AveragePrice = order.AveragePrice.ToDecimal(),
			OrderType = orderType,
			OrderState = order.State.ToStockSharpOrderState(),
			OrderStringId = order.OrderId,
			TransactionId = ParseTransactionId(order.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
			TimeInForce = policy == BloFinOrderPolicies.FillOrKill
				? TimeInForce.MatchOrCancel
				: policy == BloFinOrderPolicies.ImmediateOrCancel ? TimeInForce.CancelBalance : null,
			PostOnly = policy == BloFinOrderPolicies.PostOnly,
			PositionEffect = condition.IsReduceOnly ? OrderPositionEffects.CloseOnly : null,
			Commission = order.Fee.ToDecimal(),
			Condition = condition,
		}, cancellationToken);
	}

	private ValueTask SendFillAsync(BloFinFill fill, long originalTransactionId, bool onlyNew,
		CancellationToken cancellationToken)
	{
		if (fill?.InstrumentId.IsEmpty() != false || fill.TradeId.IsEmpty())
			return default;
		using (_sync.EnterScope())
		{
			var added = _seenFillIds.Add(fill.TradeId);
			if (onlyNew && !added)
				return default;
		}
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = fill.InstrumentId.ToStockSharp(),
			ServerTime = fill.Timestamp > 0 ? fill.Timestamp.ToUtcTime() : CurrentTime,
			PortfolioName = _portfolioName,
			Side = fill.Side.ToStockSharpSide(),
			OrderStringId = fill.OrderId,
			TradeStringId = fill.TradeId,
			TradePrice = fill.Price.ToDecimal(),
			TradeVolume = fill.Size.ToDecimal(),
			Commission = fill.Fee.ToDecimal(),
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private static string ToTriggerPrice(decimal? price)
		=> price is > 0 ? price.Value.ToWire() : null;

	private static string ToAttachedOrderPrice(decimal? triggerPrice, decimal? orderPrice)
		=> triggerPrice is > 0 ? (orderPrice ?? -1m).ToWire() : null;

	private static void ThrowIfOperationFailed(BloFinOperationResult result, string operation)
	{
		if (result is null)
			throw new InvalidDataException($"BloFin returned no result for {operation}.");
		if (!result.Code.IsEmpty() && result.Code != "0")
			throw new InvalidOperationException(
				$"BloFin failed to {operation} ({result.Code}): {result.Message}".Trim());
	}
}
