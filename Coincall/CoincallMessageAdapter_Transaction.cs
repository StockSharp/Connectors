namespace StockSharp.Coincall;

public partial class CoincallMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		ValidatePortfolio(regMsg.PortfolioName);
		var instrument = GetInstrument(regMsg.SecurityId);
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException(
				"Coincall order quantity must be positive.");
		if (instrument.MinVolume is > 0 &&
			volume < instrument.MinVolume)
			throw new InvalidOperationException(
				$"Coincall order quantity {volume} is below the " +
					$"{instrument.MinVolume} minimum.");
		if (regMsg.VisibleVolume is > 0 &&
			regMsg.VisibleVolume != volume)
			throw new NotSupportedException(
				"Coincall does not document iceberg orders.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException(
				"Coincall does not expose absolute order expiry.");
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (
			OrderTypes.Limit or
			OrderTypes.Market or
			OrderTypes.Conditional))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(
					orderType, regMsg.TransactionId));
		if (orderType != OrderTypes.Market &&
			regMsg.Price <= 0)
			throw new InvalidOperationException(
				"Coincall limit orders require a positive price.");
		if (orderType == OrderTypes.Market &&
			regMsg.PostOnly == true)
			throw new InvalidOperationException(
				"A market order cannot be post-only.");
		if (regMsg.TimeInForce is not (
			null or
			TimeInForce.PutInQueue or
			TimeInForce.MatchOrCancel or
			TimeInForce.CancelBalance))
			throw new NotSupportedException(
				"Coincall supports GTC, IOC and FOK only.");
		var condition = regMsg.Condition as CoincallOrderCondition;
		var triggerPrice = orderType == OrderTypes.Conditional
			? condition?.TriggerPrice
			: null;
		if (orderType == OrderTypes.Conditional &&
			triggerPrice is not > 0)
			throw new InvalidOperationException(
				"Coincall conditional orders require a positive " +
					"trigger price.");
		var orderId = await RestClient.PlaceOrderAsync(
			instrument.Symbol,
			regMsg.TransactionId,
			regMsg.Side,
			orderType == OrderTypes.Conditional
				? OrderTypes.Limit
				: orderType,
			volume,
			orderType == OrderTypes.Market
				? null
				: regMsg.Price,
			regMsg.TimeInForce,
			regMsg.PostOnly == true,
			condition?.ReduceOnly == true ||
				regMsg.PositionEffect ==
					OrderPositionEffects.CloseOnly,
			triggerPrice,
			cancellationToken);
		using (_sync.EnterScope())
			_orderTransactions[orderId] = regMsg.TransactionId;
		await SendOrderAsync(
			new()
			{
				Id = orderId,
				ClientOrderId = regMsg.TransactionId,
				Symbol = instrument.Symbol,
				Time = CurrentTime,
				Quantity = volume,
				RemainingQuantity = volume,
				Price = regMsg.Price,
				Side = regMsg.Side,
				OrderType = orderType,
				State = OrderStates.Active,
				TimeInForce = regMsg.TimeInForce,
				ReduceOnly = condition?.ReduceOnly == true,
				TriggerPrice = triggerPrice,
			},
			regMsg.TransactionId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		ValidatePortfolio(replaceMsg.PortfolioName);
		var orderId = ResolveOrderId(
			replaceMsg.OldOrderId,
			replaceMsg.OldOrderStringId);
		var instrument = GetInstrument(replaceMsg.SecurityId);
		var volume = replaceMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException(
				"Coincall order quantity must be positive.");
		var resultId = await RestClient.ModifyOrderAsync(
			orderId,
			instrument.Symbol,
			volume,
			replaceMsg.Price > 0
				? replaceMsg.Price
				: null,
			cancellationToken);
		using (_sync.EnterScope())
			_orderTransactions[resultId] =
				replaceMsg.TransactionId;
		await SendOrderAsync(
			new()
			{
				Id = resultId,
				ClientOrderId = replaceMsg.TransactionId,
				Symbol = instrument.Symbol,
				Time = CurrentTime,
				Quantity = volume,
				RemainingQuantity = volume,
				Price = replaceMsg.Price,
				Side = replaceMsg.Side,
				OrderType =
					replaceMsg.OrderType ?? OrderTypes.Limit,
				State = OrderStates.Active,
				TimeInForce = replaceMsg.TimeInForce,
			},
			replaceMsg.TransactionId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		var orderId = ResolveOrderId(
			cancelMsg.OrderId,
			cancelMsg.OrderStringId);
		await RestClient.CancelOrderAsync(
			orderId, null, cancellationToken);
		var order = await RestClient.GetOrderAsync(
			orderId, null, cancellationToken);
		if (order is not null)
			await SendOrderAsync(
				order,
				cancelMsg.TransactionId,
				cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		if (cancelMsg.Mode.HasFlag(
			OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"Coincall bulk cancellation does not close " +
					"positions.");
		var instrument =
			cancelMsg.SecurityId.SecurityCode.IsEmpty()
				? null
				: GetInstrument(cancelMsg.SecurityId);
		if (instrument is not null &&
			cancelMsg.Side is null &&
			cancelMsg.IsStop is null)
		{
			await RestClient.CancelAllOrdersAsync(
				instrument.Symbol, cancellationToken);
			return;
		}
		foreach (var order in await RestClient.GetOpenOrdersAsync(
			instrument?.Symbol, cancellationToken) ?? [])
		{
			if (instrument is not null &&
				!order.Symbol.EqualsIgnoreCase(
					instrument.Symbol) ||
				cancelMsg.Side is Sides side &&
					order.Side != side ||
				cancelMsg.IsStop is bool isStop &&
					(order.TriggerPrice is > 0) != isStop)
				continue;
			await RestClient.CancelOrderAsync(
				order.Id, null, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			lookupMsg.TransactionId, cancellationToken);
		EnsurePrivateReady();
		ValidatePortfolio(lookupMsg.PortfolioName);
		if (!lookupMsg.IsSubscribe)
		{
			if (_portfolioSubscriptionId ==
				lookupMsg.OriginalTransactionId)
			{
				_portfolioSubscriptionId = 0;
				await ReleasePrivateSubscriptionAsync(
					cancellationToken);
			}
			return;
		}
		await SendOutMessageAsync(
			new PortfolioMessage
			{
				PortfolioName = GetPortfolioName(),
				BoardCode = ProductType.ToBoardCode(),
				OriginalTransactionId =
					lookupMsg.TransactionId,
			},
			cancellationToken);
		await SendPortfolioSnapshotAsync(
			lookupMsg.TransactionId,
			cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await CompletePortfolioLookupAsync(
				lookupMsg, cancellationToken);
			return;
		}
		if (_portfolioSubscriptionId != 0)
			throw new InvalidOperationException(
				"Coincall portfolio subscription already exists.");
		_portfolioSubscriptionId = lookupMsg.TransactionId;
		try
		{
			await AddPrivateSubscriptionAsync(cancellationToken);
			await SendSubscriptionResultAsync(
				lookupMsg, cancellationToken);
		}
		catch
		{
			_portfolioSubscriptionId = 0;
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			statusMsg.TransactionId, cancellationToken);
		EnsurePrivateReady();
		ValidatePortfolio(statusMsg.PortfolioName);
		if (!statusMsg.IsSubscribe)
		{
			if (_orderStatusSubscriptionId ==
				statusMsg.OriginalTransactionId)
			{
				_orderStatusSubscriptionId = 0;
				await ReleasePrivateSubscriptionAsync(
					cancellationToken);
			}
			return;
		}
		if (statusMsg.Count is <= 0)
		{
			await CompleteOrderStatusAsync(
				statusMsg, cancellationToken);
			return;
		}
		long? requestedOrderId = null;
		if (statusMsg.HasOrderId())
			requestedOrderId = ResolveOrderId(
				statusMsg.OrderId,
				statusMsg.OrderStringId);
		await SendOrderSnapshotAsync(
			statusMsg.TransactionId,
			requestedOrderId,
			statusMsg.From?.ToUniversalTime(),
			(statusMsg.Count ?? 100).Max(1).Min(500).To<int>(),
			cancellationToken,
			statusMsg);
		if (statusMsg.IsHistoryOnly())
		{
			await CompleteOrderStatusAsync(
				statusMsg, cancellationToken);
			return;
		}
		if (_orderStatusSubscriptionId != 0)
			throw new InvalidOperationException(
				"Coincall order-status subscription already " +
					"exists.");
		_orderStatusSubscriptionId = statusMsg.TransactionId;
		try
		{
			await AddPrivateSubscriptionAsync(cancellationToken);
			await SendSubscriptionResultAsync(
				statusMsg, cancellationToken);
		}
		catch
		{
			_orderStatusSubscriptionId = 0;
			throw;
		}
	}

	private async ValueTask AddPrivateSubscriptionAsync(
		CancellationToken cancellationToken)
	{
		var subscribe = false;
		using (_sync.EnterScope())
			subscribe = _privateSubscriptionReferences++ == 0;
		if (!subscribe)
			return;
		try
		{
			await WsClient.SubscribePrivateAsync(
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_privateSubscriptionReferences--;
			throw;
		}
	}

	private async ValueTask ReleasePrivateSubscriptionAsync(
		CancellationToken cancellationToken)
	{
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_privateSubscriptionReferences <= 0)
				return;
			unsubscribe =
				--_privateSubscriptionReferences == 0;
		}
		if (unsubscribe)
			await WsClient.UnsubscribePrivateAsync(
				cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshotAsync(
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var accountsTask = RestClient.GetAccountsAsync(
			cancellationToken).AsTask();
		var positionsTask = RestClient.GetPositionsAsync(
			cancellationToken).AsTask();
		await Task.WhenAll(accountsTask, positionsTask);
		foreach (var account in await accountsTask ?? [])
			await SendAccountAsync(
				account,
				originalTransactionId,
				cancellationToken);
		foreach (var position in await positionsTask ?? [])
			await SendPositionAsync(
				position,
				originalTransactionId,
				cancellationToken);
	}

	private ValueTask SendAccountAsync(
		CoincallAccount account,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (account?.Currency.IsEmpty() != false)
			return default;
		return SendOutMessageAsync(
			new PositionChangeMessage
			{
				PortfolioName = GetPortfolioName(),
				SecurityId = new()
				{
					SecurityCode =
						account.Currency.ToUpperInvariant(),
					BoardCode = ProductType.ToBoardCode(),
				},
				ServerTime = CurrentTime,
				OriginalTransactionId =
					originalTransactionId,
			}
			.TryAdd(
				PositionChangeTypes.CurrentValue,
				account.Equity,
				true)
			.TryAdd(
				PositionChangeTypes.CurrentPrice,
				account.Available,
				true)
			.TryAdd(
				PositionChangeTypes.BlockedValue,
				account.Margin,
				true)
			.TryAdd(
				PositionChangeTypes.UnrealizedPnL,
				account.UnrealizedPnl,
				true),
			cancellationToken);
	}

	private ValueTask SendPositionAsync(
		CoincallPosition position,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var instrument = GetInstrument(position?.Symbol);
		if (instrument is null || position is null)
			return default;
		return SendOutMessageAsync(
			new PositionChangeMessage
			{
				PortfolioName = GetPortfolioName(),
				SecurityId = instrument.ToStockSharp(),
				ServerTime = position.Time == default
					? CurrentTime
					: position.Time,
				OriginalTransactionId =
					originalTransactionId,
				Side = position.Quantity == 0
					? null
					: position.Side,
			}
			.TryAdd(
				PositionChangeTypes.CurrentValue,
				position.Quantity.Abs(),
				true)
			.TryAdd(
				PositionChangeTypes.AveragePrice,
				position.AveragePrice,
				true)
			.TryAdd(
				PositionChangeTypes.CurrentPrice,
				position.MarkPrice,
				true)
			.TryAdd(
				PositionChangeTypes.LiquidationPrice,
				position.LiquidationPrice,
				true)
			.TryAdd(
				PositionChangeTypes.BlockedValue,
				position.InitialMargin,
				true)
			.TryAdd(
				PositionChangeTypes.UnrealizedPnL,
				position.UnrealizedPnl,
				true)
			.TryAdd(
				PositionChangeTypes.Leverage,
				position.Leverage,
				true),
			cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(
		long originalTransactionId,
		long? requestedOrderId,
		DateTime? from,
		int limit,
		CancellationToken cancellationToken,
		OrderStatusMessage filter = null)
	{
		if (requestedOrderId is > 0)
		{
			var order = await RestClient.GetOrderAsync(
				requestedOrderId, null, cancellationToken);
			if (IsOrderMatch(order, filter))
				await SendOrderAsync(
					order,
					originalTransactionId,
					cancellationToken);
		}
		else
		{
			var openTask = RestClient.GetOpenOrdersAsync(
				filter?.SecurityId.SecurityCode,
				cancellationToken).AsTask();
			var historyTask = RestClient.GetOrderHistoryAsync(
				from,
				filter?.To?.ToUniversalTime(),
				limit,
				cancellationToken).AsTask();
			await Task.WhenAll(openTask, historyTask);
			foreach (var order in
				(await openTask ?? [])
				.Concat(await historyTask ?? [])
				.GroupBy(static order => order.Id)
				.Select(static group => group
					.OrderByDescending(order => order.Time)
					.First())
				.Where(order => IsOrderMatch(order, filter))
				.OrderBy(static order => order.Time)
				.TakeLast(limit))
				await SendOrderAsync(
					order,
					originalTransactionId,
					cancellationToken);
		}
		foreach (var fill in await RestClient.GetFillsAsync(
			from,
			filter?.To?.ToUniversalTime(),
			limit,
			cancellationToken) ?? [])
		{
			if (IsFillMatch(fill, filter, requestedOrderId))
				await SendFillAsync(
					fill,
					originalTransactionId,
					cancellationToken);
		}
	}

	private ValueTask SendOrderAsync(
		CoincallOrder order,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (order?.Id is not > 0)
			return default;
		var instrument = GetInstrument(order.Symbol);
		if (instrument is null)
			return default;
		long transactionId;
		using (_sync.EnterScope())
		{
			if (!_orderTransactions.TryGetValue(
				order.Id, out transactionId))
			{
				transactionId =
					order.ClientOrderId is > 0
						? order.ClientOrderId.Value
						: 0;
				_orderTransactions[order.Id] = transactionId;
			}
		}
		var type = order.TriggerPrice is > 0
			? OrderTypes.Conditional
			: order.OrderType;
		return SendOutMessageAsync(
			new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = instrument.ToStockSharp(),
				ServerTime = order.Time == default
					? CurrentTime
					: order.Time,
				PortfolioName = GetPortfolioName(),
				Side = order.Side,
				OrderVolume = order.Quantity,
				Balance = order.RemainingQuantity,
				OrderPrice = order.Price,
				AveragePrice = order.AveragePrice > 0
					? order.AveragePrice
					: null,
				OrderType = type,
				OrderState = order.State == OrderStates.None
					? OrderStates.Active
					: order.State,
				OrderId = order.Id,
				OrderStringId = order.Id.ToString(
					CultureInfo.InvariantCulture),
				TransactionId = transactionId,
				OriginalTransactionId =
					originalTransactionId,
				TimeInForce = order.TimeInForce,
				Commission = order.Fee,
				Condition = type == OrderTypes.Conditional
					? new CoincallOrderCondition
					{
						TriggerPrice = order.TriggerPrice,
						ReduceOnly = order.ReduceOnly,
					}
					: null,
			},
			cancellationToken);
	}

	private ValueTask SendFillAsync(
		CoincallFill fill,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (fill?.Id is not > 0 ||
			!AddTrade(
				"private",
				fill.Id.ToString(
					CultureInfo.InvariantCulture)))
			return default;
		var instrument = GetInstrument(fill.Symbol);
		if (instrument is null)
			return default;
		long transactionId;
		using (_sync.EnterScope())
			transactionId = _orderTransactions.TryGetValue(
				fill.OrderId, out var value)
					? value
					: fill.ClientOrderId ?? 0;
		return SendOutMessageAsync(
			new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				SecurityId = instrument.ToStockSharp(),
				ServerTime = fill.Time == default
					? CurrentTime
					: fill.Time,
				PortfolioName = GetPortfolioName(),
				Side = fill.Side,
				OrderId = fill.OrderId > 0
					? fill.OrderId
					: null,
				OrderStringId = fill.OrderId > 0
					? fill.OrderId.ToString(
						CultureInfo.InvariantCulture)
					: null,
				TradeId = fill.Id,
				TradeStringId = fill.Id.ToString(
					CultureInfo.InvariantCulture),
				TradePrice = fill.Price,
				TradeVolume = fill.Quantity.Abs(),
				Commission = fill.Fee,
				TransactionId = transactionId,
				OriginalTransactionId =
					originalTransactionId,
			},
			cancellationToken);
	}

	private bool IsOrderMatch(
		CoincallOrder order,
		OrderStatusMessage filter)
	{
		if (order?.Id is not > 0)
			return false;
		if (filter is null)
			return true;
		if (filter.From is DateTime from &&
			order.Time < from.ToUniversalTime() ||
			filter.To is DateTime to &&
			order.Time > to.ToUniversalTime() ||
			filter.Side is Sides side &&
			order.Side != side ||
			filter.States.Length > 0 &&
			!filter.States.Contains(order.State) ||
			filter.Volume is decimal volume &&
			order.Quantity != volume ||
			!filter.PortfolioName.IsEmpty() &&
			!filter.PortfolioName.EqualsIgnoreCase(
				GetPortfolioName()))
			return false;
		var symbols = GetFilterSymbols(filter);
		return symbols.Length == 0 ||
			symbols.Contains(
				order.Symbol,
				StringComparer.OrdinalIgnoreCase);
	}

	private bool IsFillMatch(
		CoincallFill fill,
		OrderStatusMessage filter,
		long? requestedOrderId)
	{
		if (fill is null ||
			requestedOrderId is > 0 &&
				fill.OrderId != requestedOrderId)
			return false;
		if (filter is null)
			return true;
		if (filter.Side is Sides side &&
			fill.Side != side)
			return false;
		var symbols = GetFilterSymbols(filter);
		return symbols.Length == 0 ||
			symbols.Contains(
				fill.Symbol,
				StringComparer.OrdinalIgnoreCase);
	}

	private static string[] GetFilterSymbols(
		OrderStatusMessage filter)
	{
		var symbols = new List<string>();
		if (!filter.SecurityId.SecurityCode.IsEmpty())
			symbols.Add(filter.SecurityId.SecurityCode);
		symbols.AddRange(filter.SecurityIds
			.Select(static id => id.SecurityCode)
			.Where(static code => !code.IsEmpty()));
		return [.. symbols.Distinct(
			StringComparer.OrdinalIgnoreCase)];
	}

	private void ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(GetPortfolioName()))
			throw new InvalidOperationException(
				$"Unknown Coincall portfolio '{portfolioName}'.");
	}

	private static long ResolveOrderId(
		long? orderId,
		string orderStringId)
	{
		if (orderId is > 0)
			return orderId.Value;
		if (long.TryParse(
			orderStringId,
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out var parsed) &&
			parsed > 0)
			return parsed;
		throw new InvalidOperationException(
			"Coincall requires a numeric order identifier.");
	}

	private async ValueTask CompletePortfolioLookupAsync(
		PortfolioLookupMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(
			message, cancellationToken);
		await SendSubscriptionFinishedAsync(
			message.TransactionId, cancellationToken);
	}

	private async ValueTask CompleteOrderStatusAsync(
		OrderStatusMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(
			message, cancellationToken);
		await SendSubscriptionFinishedAsync(
			message.TransactionId, cancellationToken);
	}
}
