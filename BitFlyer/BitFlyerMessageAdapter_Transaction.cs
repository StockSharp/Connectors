namespace StockSharp.BitFlyer;

public partial class BitFlyerMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var market = GetMarket(regMsg.SecurityId);
		ValidateOrderRequest(regMsg);
		var volume = regMsg.Volume.Abs();
		var minutesToExpire = GetMinutesToExpire(regMsg.TillDate);
		var timeInForce = regMsg.TimeInForce.ToBitFlyer();
		var condition = regMsg.Condition as BitFlyerOrderCondition;

		TrackedOrder tracked;
		if ((regMsg.OrderType ?? OrderTypes.Limit) == OrderTypes.Conditional)
		{
			var nativeCondition = GetParentCondition(regMsg.Price, condition);
			var response = await RestClient.PlaceParentOrderAsync(new()
			{
				Method = BitFlyerOrderMethods.Simple,
				MinutesToExpire = minutesToExpire,
				TimeInForce = timeInForce,
				Parameters =
				[
					new()
					{
						ProductCode = market.ProductCode,
						Type = nativeCondition,
						Side = regMsg.Side.ToBitFlyer(),
						Price = nativeCondition ==
							BitFlyerConditionTypes.StopLimit
								? regMsg.Price
								: null,
						Size = volume,
						TriggerPrice = nativeCondition is
							BitFlyerConditionTypes.Stop or
							BitFlyerConditionTypes.StopLimit
								? condition.TriggerPrice
								: null,
						Offset = nativeCondition == BitFlyerConditionTypes.Trail
							? condition.TrailingOffset
							: null,
					},
				],
			}, cancellationToken);
			if (response?.AcceptanceId.IsEmpty() != false)
				throw new InvalidDataException(
					"bitFlyer accepted a parent order without an identifier.");
			tracked = new()
			{
				TransactionId = regMsg.TransactionId,
				ProductCode = market.ProductCode,
				AcceptanceId = response.AcceptanceId,
				Side = regMsg.Side,
				OrderType = OrderTypes.Conditional,
				Volume = volume,
				Price = regMsg.Price,
				TimeInForce = regMsg.TimeInForce,
				IsParent = true,
				Condition = condition?.Clone() as BitFlyerOrderCondition,
			};
		}
		else
		{
			var orderType = regMsg.OrderType ?? OrderTypes.Limit;
			var response = await RestClient.PlaceChildOrderAsync(new()
			{
				ProductCode = market.ProductCode,
				Type = orderType == OrderTypes.Market
					? BitFlyerChildOrderTypes.Market
					: BitFlyerChildOrderTypes.Limit,
				Side = regMsg.Side.ToBitFlyer(),
				Price = orderType == OrderTypes.Limit ? regMsg.Price : null,
				Size = volume,
				MinutesToExpire = minutesToExpire,
				TimeInForce = timeInForce,
			}, cancellationToken);
			if (response?.AcceptanceId.IsEmpty() != false)
				throw new InvalidDataException(
					"bitFlyer accepted a child order without an identifier.");
			tracked = new()
			{
				TransactionId = regMsg.TransactionId,
				ProductCode = market.ProductCode,
				AcceptanceId = response.AcceptanceId,
				Side = regMsg.Side,
				OrderType = orderType,
				Volume = volume,
				Price = regMsg.Price,
				TimeInForce = regMsg.TimeInForce,
				IsParent = false,
			};
		}

		TrackOrder(tracked, tracked.AcceptanceId);
		await SendTrackedOrderStateAsync(tracked, OrderStates.Active,
			tracked.Volume, regMsg.TransactionId, CurrentTime, null,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		_ = replaceMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"bitFlyer does not expose an atomic order-replace operation.");
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var identifier = ResolveOrderIdentifier(cancelMsg.OrderId,
			cancelMsg.OrderStringId, "cancellation");
		var tracked = GetTrackedOrder(identifier);
		var market = tracked is not null
			? GetMarket(tracked.ProductCode)
			: GetMarket(cancelMsg.SecurityId);
		var isParent = tracked?.IsParent ?? IsParentOrderId(identifier);
		var exchangeIdentifier = tracked?.NativeOrderId.IsEmpty() == false
			? tracked.NativeOrderId
			: tracked?.AcceptanceId.IsEmpty() == false
				? tracked.AcceptanceId
				: identifier;
		if (isParent)
		{
			await RestClient.CancelParentOrderAsync(new()
			{
				ProductCode = market.ProductCode,
				OrderId = IsNativeParentOrderId(exchangeIdentifier)
					? exchangeIdentifier
					: null,
				AcceptanceId = IsNativeParentOrderId(exchangeIdentifier)
					? null
					: exchangeIdentifier,
			}, cancellationToken);
		}
		else
		{
			await RestClient.CancelChildOrderAsync(new()
			{
				ProductCode = market.ProductCode,
				OrderId = IsNativeChildOrderId(exchangeIdentifier)
					? exchangeIdentifier
					: null,
				AcceptanceId = IsNativeChildOrderId(exchangeIdentifier)
					? null
					: exchangeIdentifier,
			}, cancellationToken);
		}
		if (tracked is not null)
			await SendTrackedOrderStateAsync(tracked, OrderStates.Done, 0m,
				cancelMsg.TransactionId, CurrentTime, null, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"bitFlyer bulk cancellation cannot close positions.");

		MarketDefinition[] markets;
		if (!cancelMsg.SecurityId.SecurityCode.IsEmpty())
			markets = [GetMarket(cancelMsg.SecurityId)];
		else
			using (_sync.EnterScope())
				markets = [.. _markets.Values];

		foreach (var market in markets)
		{
			if (cancelMsg.Side is null)
			{
				await RestClient.CancelAllAsync(new()
				{
					ProductCode = market.ProductCode,
				}, cancellationToken);
			}
			else
			{
				var childOrders = await RestClient.GetChildOrdersAsync(new()
				{
					ProductCode = market.ProductCode,
					Count = 500,
					State = BitFlyerOrderStates.Active,
				}, cancellationToken);
				foreach (var order in (childOrders ?? []).Where(order =>
					order is not null && order.Side.ToStockSharp() == cancelMsg.Side))
					await RestClient.CancelChildOrderAsync(new()
					{
						ProductCode = market.ProductCode,
						OrderId = order.OrderId,
					}, cancellationToken);
			}

			var parentOrders = await RestClient.GetParentOrdersAsync(new()
			{
				ProductCode = market.ProductCode,
				Count = 500,
				State = BitFlyerOrderStates.Active,
			}, cancellationToken);
			foreach (var order in (parentOrders ?? []).Where(order => order is not null &&
				(cancelMsg.Side is null ||
					order.Side.ToStockSharp() == cancelMsg.Side)))
				await RestClient.CancelParentOrderAsync(new()
				{
					ProductCode = market.ProductCode,
					OrderId = order.OrderId,
				}, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsurePrivateReady();
		if (!lookupMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_portfolioSubscriptions.Remove(lookupMsg.OriginalTransactionId);
			return;
		}

		var portfolio = GetPortfolioName();
		if (lookupMsg.PortfolioName.IsEmpty() ||
			lookupMsg.PortfolioName.EqualsIgnoreCase(portfolio))
		{
			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = portfolio,
				BoardCode = BoardCodes.BitFlyer,
				OriginalTransactionId = lookupMsg.TransactionId,
			}, cancellationToken);
			await SendPortfolioSnapshotAsync(lookupMsg.TransactionId,
				cancellationToken);
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_portfolioSubscriptions.Add(lookupMsg.TransactionId);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		EnsurePrivateReady();
		if (!statusMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_orderSubscriptions.Remove(statusMsg.OriginalTransactionId);
			return;
		}
		if (statusMsg.Count is <= 0)
		{
			await CompleteOrderStatusAsync(statusMsg, cancellationToken);
			return;
		}

		var identifier = statusMsg.HasOrderId()
			? ResolveOrderIdentifier(statusMsg.OrderId, statusMsg.OrderStringId,
				"lookup")
			: null;
		var tracked = GetTrackedOrder(identifier);
		var markets = GetOrderLookupMarkets(statusMsg, tracked);
		var maximum = (statusMsg.Count ?? 500).Min(500).Max(1).To<int>();
		foreach (var market in markets)
		{
			if (tracked?.IsParent != true)
			{
				var childOrders = await RestClient.GetChildOrdersAsync(new()
				{
					ProductCode = market.ProductCode,
					Count = maximum,
					OrderId = identifier is not null &&
						IsNativeChildOrderId(identifier) ? identifier : null,
					AcceptanceId = identifier is not null &&
						!IsNativeChildOrderId(identifier) &&
						!long.TryParse(identifier, out _) ? identifier : null,
				}, cancellationToken);
				foreach (var order in (childOrders ?? [])
					.Where(order => IsMatchingOrder(order, identifier,
						statusMsg.Side, statusMsg.From, statusMsg.To)))
					await SendChildOrderAsync(order, statusMsg.TransactionId,
						cancellationToken);
			}

			if (tracked?.IsParent != false)
			{
				var parentOrders = await RestClient.GetParentOrdersAsync(new()
				{
					ProductCode = market.ProductCode,
					Count = maximum,
				}, cancellationToken);
				foreach (var order in (parentOrders ?? [])
					.Where(order => IsMatchingOrder(order, identifier,
						statusMsg.Side, statusMsg.From, statusMsg.To)))
					await SendParentOrderAsync(order, statusMsg.TransactionId,
						cancellationToken);
			}

			if (tracked?.IsParent != true)
			{
				var executions = await RestClient.GetAccountExecutionsAsync(new()
				{
					ProductCode = market.ProductCode,
					Count = maximum,
					OrderId = identifier is not null &&
						IsNativeChildOrderId(identifier) ? identifier : null,
					AcceptanceId = identifier is not null &&
						!IsNativeChildOrderId(identifier) &&
						!long.TryParse(identifier, out _) ? identifier : null,
				}, cancellationToken);
				foreach (var execution in (executions ?? []).Where(value =>
					value is not null &&
					(statusMsg.Side is null ||
						value.Side.ToStockSharp() == statusMsg.Side) &&
					IsWithin(value.ExecutionDate, statusMsg.From, statusMsg.To)))
					await SendAccountExecutionAsync(market.ProductCode, execution,
						statusMsg.TransactionId, cancellationToken);
			}
		}

		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_orderSubscriptions[statusMsg.TransactionId] = new()
			{
				ProductCode = markets.Length == 1 ? markets[0].ProductCode : null,
				OrderId = identifier,
				Side = statusMsg.Side,
			};
	}

	private void ValidateOrderRequest(OrderRegisterMessage message)
	{
		var type = message.OrderType ?? OrderTypes.Limit;
		if (type is not (OrderTypes.Limit or OrderTypes.Market or
			OrderTypes.Conditional))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(type,
					message.TransactionId));
		if (message.Volume <= 0)
			throw new InvalidOperationException(
				"bitFlyer order volume must be positive.");
		if (type == OrderTypes.Limit && message.Price <= 0)
			throw new InvalidOperationException(
				"bitFlyer limit orders require a positive price.");
		if (message.PostOnly == true)
			throw new NotSupportedException(
				"bitFlyer does not document a post-only child-order flag.");
		if (message.VisibleVolume is > 0 &&
			message.VisibleVolume != message.Volume.Abs())
			throw new NotSupportedException(
				"bitFlyer does not document iceberg child orders.");
		if (type == OrderTypes.Conditional &&
			message.Condition is not BitFlyerOrderCondition)
			throw new InvalidOperationException(
				"bitFlyer conditional orders require BitFlyerOrderCondition.");
	}

	private int? GetMinutesToExpire(DateTime? tillDate)
	{
		if (tillDate is null)
			return null;
		var utc = tillDate.Value.Kind switch
		{
			DateTimeKind.Utc => tillDate.Value,
			DateTimeKind.Unspecified => DateTime.SpecifyKind(tillDate.Value,
				DateTimeKind.Utc),
			_ => tillDate.Value.ToUniversalTime(),
		};
		var minutes = (int)Math.Ceiling((utc - CurrentTime.ToUniversalTime())
			.TotalMinutes);
		if (minutes <= 0 || minutes > 43200)
			throw new InvalidOperationException(
				"bitFlyer order expiry must be between one minute and 30 days.");
		return minutes;
	}

	private static BitFlyerConditionTypes GetParentCondition(decimal price,
		BitFlyerOrderCondition condition)
	{
		if (condition.TrailingOffset is > 0)
		{
			if (condition.TriggerPrice is not null || price > 0)
				throw new InvalidOperationException(
					"bitFlyer trailing orders cannot include a trigger or limit price.");
			return BitFlyerConditionTypes.Trail;
		}
		if (condition.TriggerPrice is not > 0)
			throw new InvalidOperationException(
				"bitFlyer stop orders require a positive trigger price.");
		return price > 0
			? BitFlyerConditionTypes.StopLimit
			: BitFlyerConditionTypes.Stop;
	}

	private MarketDefinition[] GetOrderLookupMarkets(OrderStatusMessage message,
		TrackedOrder tracked)
	{
		if (tracked is not null)
			return [GetMarket(tracked.ProductCode)];
		if (!message.SecurityId.SecurityCode.IsEmpty())
			return [GetMarket(message.SecurityId)];
		using (_sync.EnterScope())
			return [.. _markets.Values.OrderBy(static value => value.ProductCode,
				StringComparer.OrdinalIgnoreCase)];
	}

	private async ValueTask SendPortfolioSnapshotAsync(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var balances = await RestClient.GetBalancesAsync(cancellationToken);
		foreach (var balance in balances ?? [])
		{
			if (balance?.CurrencyCode.IsEmpty() != false)
				continue;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = GetPortfolioName(),
				SecurityId = balance.CurrencyCode.ToStockSharp(),
				ServerTime = CurrentTime,
				OriginalTransactionId = originalTransactionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, balance.Available, true)
			.TryAdd(PositionChangeTypes.BlockedValue,
				(balance.Amount - balance.Available).Max(0m), true),
				cancellationToken);
		}

		MarketDefinition[] derivativeMarkets;
		using (_sync.EnterScope())
			derivativeMarkets = [.. _markets.Values.Where(static market =>
				market.Type == BitFlyerMarketTypes.Fx)];
		if (derivativeMarkets.Length == 0)
			return;

		try
		{
			var collateral = await RestClient.GetCollateralAsync(cancellationToken);
			if (collateral is not null)
				await SendOutMessageAsync(new PositionChangeMessage
				{
					PortfolioName = GetPortfolioName(),
					SecurityId = SecurityId.Money,
					ServerTime = CurrentTime,
					OriginalTransactionId = originalTransactionId,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, collateral.Collateral, true)
				.TryAdd(PositionChangeTypes.BlockedValue,
					collateral.RequiredCollateral, true)
				.TryAdd(PositionChangeTypes.UnrealizedPnL,
					collateral.OpenPositionPnL, true)
				.TryAdd(PositionChangeTypes.VariationMargin,
					collateral.MarginCallAmount, true), cancellationToken);
		}
		catch (BitFlyerApiException error)
		{
			this.AddWarningLog("bitFlyer collateral snapshot is unavailable: {0}",
				error.Message);
		}

		foreach (var market in derivativeMarkets)
		{
			try
			{
				var positions = await RestClient.GetPositionsAsync(new()
				{
					ProductCode = market.ProductCode,
				}, cancellationToken);
				foreach (var group in (positions ?? []).Where(static position =>
					position is not null).GroupBy(static position =>
						position.ProductCode, StringComparer.OrdinalIgnoreCase))
					await SendPositionAsync(group.Key, group,
						originalTransactionId, cancellationToken);
			}
			catch (BitFlyerApiException error)
			{
				this.AddWarningLog(
					"bitFlyer position snapshot for {0} is unavailable: {1}",
					market.ProductCode, error.Message);
			}
		}
	}

	private ValueTask SendPositionAsync(string productCode,
		IEnumerable<BitFlyerPosition> positions, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var values = positions.ToArray();
		var totalSize = values.Sum(static position => position.Side ==
			BitFlyerSides.Buy ? position.Size : -position.Size);
		var grossSize = values.Sum(static position => position.Size.Abs());
		var averagePrice = grossSize > 0
			? values.Sum(static position => position.Price * position.Size.Abs()) /
				grossSize
			: 0m;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(),
			SecurityId = productCode.ToStockSharp(),
			ServerTime = CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, totalSize, true)
		.TryAdd(PositionChangeTypes.AveragePrice, averagePrice, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL,
			values.Sum(static position => position.PnL), true)
		.TryAdd(PositionChangeTypes.Commission,
			values.Sum(static position => position.Commission +
				position.SwapPoints + position.Sfd + position.FundingFees), true)
		.TryAdd(PositionChangeTypes.VariationMargin,
			values.Sum(static position => position.RequiredCollateral), true)
		.TryAdd(PositionChangeTypes.Leverage,
			grossSize > 0
				? values.Sum(static position => position.Leverage *
					position.Size.Abs()) / grossSize
				: null, true), cancellationToken);
	}

	private async ValueTask SendChildOrderAsync(BitFlyerChildOrder order,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (order?.AcceptanceId.IsEmpty() != false || order.ProductCode.IsEmpty())
			return;
		var tracked = GetTrackedOrder(order.AcceptanceId) ??
			GetTrackedOrder(order.OrderId) ?? new TrackedOrder
			{
				ProductCode = order.ProductCode.NormalizeProductCode(),
				AcceptanceId = order.AcceptanceId,
				NativeOrderId = order.OrderId,
				NumericId = order.Id > 0 ? order.Id : null,
				Side = order.Side.ToStockSharp(),
				OrderType = order.Type.ToStockSharp(),
				Volume = order.Size,
				Price = order.Price,
				TimeInForce = order.TimeInForce.ToStockSharp(),
			};
		tracked.AcceptanceId = order.AcceptanceId;
		tracked.NativeOrderId = order.OrderId;
		tracked.NumericId = order.Id > 0 ? order.Id : tracked.NumericId;
		TrackOrder(tracked, order.AcceptanceId, order.OrderId,
			order.Id.ToString(CultureInfo.InvariantCulture));
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.ProductCode.ToStockSharp(),
			ServerTime = order.OrderDate.ToUtcDateTime(CurrentTime),
			PortfolioName = GetPortfolioName(),
			Side = order.Side.ToStockSharp(),
			OrderVolume = order.Size,
			Balance = order.OutstandingSize,
			OrderPrice = order.Price,
			OrderType = order.Type.ToStockSharp(),
			OrderState = order.State.ToStockSharp(),
			OrderId = order.Id > 0 ? order.Id : null,
			OrderStringId = order.AcceptanceId,
			TransactionId = tracked.TransactionId,
			OriginalTransactionId = originalTransactionId,
			TimeInForce = order.TimeInForce.ToStockSharp(),
			Commission = order.TotalCommission != 0
				? order.TotalCommission
				: null,
			Error = order.State == BitFlyerOrderStates.Rejected
				? new InvalidOperationException("bitFlyer rejected the order.")
				: null,
		}, cancellationToken);
	}

	private async ValueTask SendParentOrderAsync(BitFlyerParentOrder order,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (order?.AcceptanceId.IsEmpty() != false || order.ProductCode.IsEmpty())
			return;
		var tracked = GetTrackedOrder(order.AcceptanceId) ??
			GetTrackedOrder(order.OrderId) ?? new TrackedOrder
			{
				ProductCode = order.ProductCode.NormalizeProductCode(),
				AcceptanceId = order.AcceptanceId,
				NativeOrderId = order.OrderId,
				NumericId = order.Id > 0 ? order.Id : null,
				Side = order.Side.ToStockSharp(),
				OrderType = OrderTypes.Conditional,
				Volume = order.Size,
				Price = order.Price,
				IsParent = true,
			};
		tracked.AcceptanceId = order.AcceptanceId;
		tracked.NativeOrderId = order.OrderId;
		tracked.NumericId = order.Id > 0 ? order.Id : tracked.NumericId;
		TrackOrder(tracked, order.AcceptanceId, order.OrderId,
			order.Id.ToString(CultureInfo.InvariantCulture));
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.ProductCode.ToStockSharp(),
			ServerTime = order.OrderDate.ToUtcDateTime(CurrentTime),
			PortfolioName = GetPortfolioName(),
			Side = order.Side.ToStockSharp(),
			OrderVolume = order.Size,
			Balance = order.OutstandingSize,
			OrderPrice = order.Price,
			OrderType = OrderTypes.Conditional,
			OrderState = order.State.ToStockSharp(),
			OrderId = order.Id > 0 ? order.Id : null,
			OrderStringId = order.AcceptanceId,
			TransactionId = tracked.TransactionId,
			OriginalTransactionId = originalTransactionId,
			Condition = tracked.Condition,
			Commission = order.TotalCommission != 0
				? order.TotalCommission
				: null,
			Error = order.State == BitFlyerOrderStates.Rejected
				? new InvalidOperationException("bitFlyer rejected the parent order.")
				: null,
		}, cancellationToken);
	}

	private ValueTask SendAccountExecutionAsync(string productCode,
		BitFlyerAccountExecution execution, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (execution is null || execution.Id <= 0 || execution.Price <= 0 ||
			execution.Size <= 0 || !AddAccountTrade(execution.Id))
			return default;
		var tracked = GetTrackedOrder(execution.AcceptanceId) ??
			GetTrackedOrder(execution.OrderId);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = productCode.ToStockSharp(),
			ServerTime = execution.ExecutionDate.ToUtcDateTime(CurrentTime),
			PortfolioName = GetPortfolioName(),
			Side = execution.Side.ToStockSharp(),
			OrderStringId = execution.AcceptanceId,
			TradeId = execution.Id,
			TradePrice = execution.Price,
			TradeVolume = execution.Size,
			Commission = execution.Commission != 0
				? execution.Commission
				: null,
			TransactionId = tracked?.TransactionId ?? 0,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private async ValueTask SendTrackedOrderStateAsync(TrackedOrder tracked,
		OrderStates state, decimal balance, long originalTransactionId,
		DateTime serverTime, Exception error,
		CancellationToken cancellationToken)
		=> await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = tracked.ProductCode.ToStockSharp(),
			ServerTime = serverTime,
			PortfolioName = GetPortfolioName(),
			Side = tracked.Side,
			OrderVolume = tracked.Volume,
			Balance = balance,
			OrderPrice = tracked.Price,
			OrderType = tracked.OrderType,
			OrderState = state,
			OrderId = tracked.NumericId,
			OrderStringId = tracked.AcceptanceId,
			TransactionId = tracked.TransactionId,
			OriginalTransactionId = originalTransactionId,
			TimeInForce = tracked.TimeInForce,
			Condition = tracked.Condition,
			Error = error,
		}, cancellationToken);

	private async ValueTask OnChildEventsAsync(BitFlyerChildOrderEvent[] events,
		CancellationToken cancellationToken)
	{
		foreach (var update in events ?? [])
		{
			if (update?.ProductCode.IsEmpty() != false)
				continue;
			var tracked = GetTrackedOrder(update.AcceptanceId) ??
				GetTrackedOrder(update.OrderId);
			if (tracked is null && update.Side is BitFlyerSides side)
				tracked = new()
				{
					ProductCode = update.ProductCode.NormalizeProductCode(),
					AcceptanceId = update.AcceptanceId,
					NativeOrderId = update.OrderId,
					Side = side.ToStockSharp(),
					OrderType = update.OrderType?.ToStockSharp() ??
						OrderTypes.Limit,
					Volume = update.Size ?? 0m,
					Price = update.Price ?? 0m,
				};
			if (tracked is null)
				continue;
			if (!update.AcceptanceId.IsEmpty())
				tracked.AcceptanceId = update.AcceptanceId;
			if (!update.OrderId.IsEmpty())
				tracked.NativeOrderId = update.OrderId;
			TrackOrder(tracked, update.AcceptanceId, update.OrderId);

			if (update.EventType == BitFlyerChildEventTypes.CancelFailed)
			{
				await SendOutErrorAsync(new InvalidOperationException(
					$"bitFlyer cancellation failed for '{tracked.AcceptanceId}': " +
					update.Reason), cancellationToken);
				continue;
			}
			var state = update.EventType switch
			{
				BitFlyerChildEventTypes.OrderFailed => OrderStates.Failed,
				BitFlyerChildEventTypes.Cancel or BitFlyerChildEventTypes.Expire =>
					OrderStates.Done,
				BitFlyerChildEventTypes.Execution when update.OutstandingSize is <= 0 =>
					OrderStates.Done,
				_ => OrderStates.Active,
			};
			var error = state == OrderStates.Failed
				? new InvalidOperationException(
					$"bitFlyer rejected the order: {update.Reason}")
				: null;
			foreach (var target in GetOrderTargets(tracked))
				await SendTrackedOrderStateAsync(tracked, state,
					update.OutstandingSize ?? (state == OrderStates.Done
						? 0m
						: tracked.Volume), target,
					update.EventDate.ToUtcDateTime(CurrentTime), error,
					cancellationToken);

			if (update.EventType != BitFlyerChildEventTypes.Execution ||
				update.ExecutionId is not > 0 || update.Price is not > 0 ||
				update.Size is not > 0 || !AddAccountTrade(update.ExecutionId.Value))
				continue;
			foreach (var target in GetOrderTargets(tracked))
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					SecurityId = tracked.ProductCode.ToStockSharp(),
					ServerTime = update.EventDate.ToUtcDateTime(CurrentTime),
					PortfolioName = GetPortfolioName(),
					Side = update.Side?.ToStockSharp() ?? tracked.Side,
					OrderStringId = tracked.AcceptanceId,
					TradeId = update.ExecutionId,
					TradePrice = update.Price,
					TradeVolume = update.Size,
					Commission = update.Commission,
					TransactionId = tracked.TransactionId,
					OriginalTransactionId = target,
				}, cancellationToken);
		}
	}

	private async ValueTask OnParentEventsAsync(BitFlyerParentOrderEvent[] events,
		CancellationToken cancellationToken)
	{
		foreach (var update in events ?? [])
		{
			if (update?.ProductCode.IsEmpty() != false)
				continue;
			var tracked = GetTrackedOrder(update.AcceptanceId) ??
				GetTrackedOrder(update.OrderId);
			if (tracked is null && update.Side is BitFlyerSides side)
				tracked = new()
				{
					ProductCode = update.ProductCode.NormalizeProductCode(),
					AcceptanceId = update.AcceptanceId,
					NativeOrderId = update.OrderId,
					Side = side.ToStockSharp(),
					OrderType = OrderTypes.Conditional,
					Volume = update.Size ?? 0m,
					Price = update.Price ?? 0m,
					IsParent = true,
				};
			if (tracked is null)
				continue;
			if (!update.AcceptanceId.IsEmpty())
				tracked.AcceptanceId = update.AcceptanceId;
			if (!update.OrderId.IsEmpty())
				tracked.NativeOrderId = update.OrderId;
			TrackOrder(tracked, update.AcceptanceId, update.OrderId);
			var state = update.EventType switch
			{
				BitFlyerParentEventTypes.OrderFailed => OrderStates.Failed,
				BitFlyerParentEventTypes.Cancel or
					BitFlyerParentEventTypes.Complete or
					BitFlyerParentEventTypes.Expire => OrderStates.Done,
				_ => OrderStates.Active,
			};
			var error = state == OrderStates.Failed
				? new InvalidOperationException(
					$"bitFlyer rejected the parent order: {update.Reason}")
				: null;
			foreach (var target in GetOrderTargets(tracked))
				await SendTrackedOrderStateAsync(tracked, state,
					state == OrderStates.Done ? 0m : tracked.Volume,
					target, update.EventDate.ToUtcDateTime(CurrentTime), error,
					cancellationToken);
		}
	}

	private long[] GetOrderTargets(TrackedOrder tracked)
	{
		var targets = new HashSet<long>();
		if (tracked.TransactionId > 0)
			targets.Add(tracked.TransactionId);
		using (_sync.EnterScope())
			foreach (var pair in _orderSubscriptions)
				if (MatchesOrderSubscription(pair.Value, tracked))
					targets.Add(pair.Key);
		if (targets.Count == 0)
			targets.Add(0);
		return [.. targets];
	}

	private static bool MatchesOrderSubscription(OrderSubscription subscription,
		TrackedOrder order)
		=> (subscription.ProductCode.IsEmpty() ||
			subscription.ProductCode.EqualsIgnoreCase(order.ProductCode)) &&
			(subscription.OrderId.IsEmpty() ||
			subscription.OrderId.EqualsIgnoreCase(order.AcceptanceId) ||
			subscription.OrderId.EqualsIgnoreCase(order.NativeOrderId) ||
			(order.NumericId is > 0 && subscription.OrderId.Equals(
				order.NumericId.Value.ToString(CultureInfo.InvariantCulture),
				StringComparison.Ordinal))) &&
			(subscription.Side is null || subscription.Side == order.Side);

	private static bool IsMatchingOrder(BitFlyerChildOrder order,
		string identifier, Sides? side, DateTime? from, DateTime? to)
		=> order is not null &&
			(identifier.IsEmpty() || identifier.EqualsIgnoreCase(order.OrderId) ||
				identifier.EqualsIgnoreCase(order.AcceptanceId) ||
				identifier.Equals(order.Id.ToString(CultureInfo.InvariantCulture),
					StringComparison.Ordinal)) &&
			(side is null || order.Side.ToStockSharp() == side) &&
			IsWithin(order.OrderDate, from, to);

	private static bool IsMatchingOrder(BitFlyerParentOrder order,
		string identifier, Sides? side, DateTime? from, DateTime? to)
		=> order is not null &&
			(identifier.IsEmpty() || identifier.EqualsIgnoreCase(order.OrderId) ||
				identifier.EqualsIgnoreCase(order.AcceptanceId) ||
				identifier.Equals(order.Id.ToString(CultureInfo.InvariantCulture),
					StringComparison.Ordinal)) &&
			(side is null || order.Side.ToStockSharp() == side) &&
			IsWithin(order.OrderDate, from, to);

	private static bool IsWithin(string timestamp, DateTime? from, DateTime? to)
	{
		var value = timestamp.ToUtcDateTime(DateTime.MinValue);
		return (from is null || value >= from.Value.ToUniversalTime()) &&
			(to is null || value <= to.Value.ToUniversalTime());
	}

	private static bool IsNativeChildOrderId(string value)
		=> value?.StartsWith("JOR", StringComparison.OrdinalIgnoreCase) == true;

	private static bool IsNativeParentOrderId(string value)
		=> value?.StartsWith("JCO", StringComparison.OrdinalIgnoreCase) == true ||
			value?.StartsWith("JCP", StringComparison.OrdinalIgnoreCase) == true;

	private static bool IsParentOrderId(string value)
		=> IsNativeParentOrderId(value);

	private async ValueTask CompleteOrderStatusAsync(OrderStatusMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
