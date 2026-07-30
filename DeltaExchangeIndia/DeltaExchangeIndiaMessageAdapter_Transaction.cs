namespace StockSharp.DeltaExchangeIndia;

public partial class DeltaExchangeIndiaMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		ValidatePortfolio(regMsg.PortfolioName);
		var product = GetProduct(regMsg.SecurityId);
		var volume = regMsg.Volume.Abs();
		var size = ValidateSize(volume);
		if (!product.IsActive)
			throw new InvalidOperationException(
				$"Delta Exchange India product " +
					$"'{product.Symbol}' is not operational.");
		if (regMsg.VisibleVolume is > 0 &&
			regMsg.VisibleVolume != volume)
			throw new NotSupportedException(
				"Delta Exchange India API does not document " +
					"iceberg orders.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException(
				"Delta Exchange India API does not expose " +
					"absolute order expiry.");
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
				"Delta Exchange India limit orders require a " +
					"positive price.");
		if (orderType == OrderTypes.Market &&
			regMsg.PostOnly == true)
			throw new InvalidOperationException(
				"A market order cannot be post-only.");
		var condition =
			regMsg.Condition as DeltaExchangeIndiaOrderCondition;
		var stopPrice = orderType == OrderTypes.Conditional
			? condition?.StopPrice
			: null;
		if (orderType == OrderTypes.Conditional &&
			stopPrice is not > 0)
			throw new InvalidOperationException(
				"Delta Exchange India conditional orders require " +
					"a positive stop price.");
		var clientOrderId = regMsg.UserOrderId.IsEmpty()
			? $"ss-{regMsg.TransactionId}"
			: regMsg.UserOrderId.Trim();
		if (clientOrderId.Length > 32)
			throw new InvalidOperationException(
				"Delta Exchange India client order ID cannot " +
					"exceed 32 characters.");
		var result = await RestClient.PlaceOrderAsync(
			product.Id,
			size,
			regMsg.Side,
			orderType == OrderTypes.Conditional
				? OrderTypes.Limit
				: orderType,
			orderType == OrderTypes.Market
				? null
				: regMsg.Price,
			regMsg.TimeInForce,
			regMsg.PostOnly == true,
			condition?.IsReduceOnly == true ||
				regMsg.PositionEffect ==
					OrderPositionEffects.CloseOnly,
			stopPrice,
			clientOrderId,
			cancellationToken);
		if (result?.Id is not > 0)
			throw new InvalidDataException(
				"Delta Exchange India accepted an order without " +
					"returning its identifier.");
		TrackOrder(result.Id, new()
		{
			TransactionId = regMsg.TransactionId,
			ProductId = product.Id,
			Symbol = product.Symbol,
			Side = regMsg.Side,
			OrderType = orderType,
			Volume = volume,
			Price = regMsg.Price,
		});
		await SendOrderAsync(
			result,
			regMsg.TransactionId,
			true,
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
		var product = ResolveOrderProduct(
			replaceMsg.SecurityId, orderId);
		var volume = replaceMsg.Volume.Abs();
		var condition =
			replaceMsg.Condition as
				DeltaExchangeIndiaOrderCondition;
		var result = await RestClient.EditOrderAsync(
			orderId,
			product.Id,
			ValidateSize(volume),
			replaceMsg.Price > 0
				? replaceMsg.Price
				: null,
			replaceMsg.PostOnly == true,
			condition?.StopPrice,
			cancellationToken);
		TrackOrder(result.Id, new()
		{
			TransactionId = replaceMsg.TransactionId,
			ProductId = product.Id,
			Symbol = product.Symbol,
			Side = replaceMsg.Side,
			OrderType =
				replaceMsg.OrderType ?? OrderTypes.Limit,
			Volume = volume,
			Price = replaceMsg.Price,
		});
		await SendOrderAsync(
			result,
			replaceMsg.TransactionId,
			true,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		var (orderId, clientOrderId) = ResolveOrderIdentity(
			cancelMsg.OrderId,
			cancelMsg.OrderStringId);
		var product = ResolveOrderProduct(
			cancelMsg.SecurityId, orderId);
		var result = await RestClient.CancelOrderAsync(
			orderId,
			clientOrderId,
			product.Id,
			cancellationToken);
		await SendOrderAsync(
			result,
			cancelMsg.TransactionId,
			true,
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
				"Delta Exchange India bulk cancellation does not " +
					"close positions.");
		var product =
			cancelMsg.SecurityId.SecurityCode.IsEmpty()
				? null
				: GetProduct(cancelMsg.SecurityId);
		if (cancelMsg.Side is null &&
			cancelMsg.IsStop is null)
		{
			foreach (var order in
				await RestClient.CancelAllOrdersAsync(
					product?.Id,
					cancellationToken) ?? [])
				await SendOrderAsync(
					order,
					cancelMsg.TransactionId,
					true,
					cancellationToken);

			return;
		}

		foreach (var order in
			await RestClient.GetOpenOrdersAsync(
				product?.Id, cancellationToken) ?? [])
		{
			if (cancelMsg.Side is Sides side &&
				order.Side != side)
				continue;
			if (cancelMsg.IsStop is bool isStop &&
				(order.OrderType == OrderTypes.Conditional) != isStop)
				continue;
			var orderProduct =
				GetProduct(order.ProductId) ??
				GetProduct(order.Symbol);
			if (orderProduct is null)
				continue;
			var result = await RestClient.CancelOrderAsync(
				order.Id,
				null,
				orderProduct.Id,
				cancellationToken);
			await SendOrderAsync(
				result,
				cancelMsg.TransactionId,
				true,
				cancellationToken);
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
				await PrivateWsClient.UnsubscribeAsync(
					"positions",
					"all",
					cancellationToken);
			}
			return;
		}
		await SendOutMessageAsync(
			new PortfolioMessage
			{
				PortfolioName = GetPortfolioName(),
				BoardCode = BoardCodes.DeltaExchangeIndia,
				OriginalTransactionId =
					lookupMsg.TransactionId,
			},
			cancellationToken);
		await SendPortfolioSnapshotAsync(
			lookupMsg.TransactionId,
			true,
			cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await CompletePortfolioLookupAsync(
				lookupMsg, cancellationToken);
			return;
		}

		if (_portfolioSubscriptionId != 0)
			throw new InvalidOperationException(
				"Delta Exchange India portfolio subscription " +
					"already exists.");
		_portfolioSubscriptionId = lookupMsg.TransactionId;
		try
		{
			await PrivateWsClient.SubscribeAsync(
				"positions", "all", cancellationToken);
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
				await PrivateWsClient.UnsubscribeAsync(
					"orders", "all", cancellationToken);
				await PrivateWsClient.UnsubscribeAsync(
					"v2/user_trades",
					"all",
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
		long? requestedId = null;
		if (statusMsg.HasOrderId())
			requestedId = ResolveOrderIdentity(
				statusMsg.OrderId,
				statusMsg.OrderStringId).OrderId;
		DeltaProduct product = null;
		if (!statusMsg.SecurityId.SecurityCode.IsEmpty())
			product = GetProduct(statusMsg.SecurityId);
		await SendOrderSnapshotAsync(
			statusMsg.TransactionId,
			product,
			statusMsg.From?.ToUniversalTime(),
			statusMsg.To?.ToUniversalTime(),
			true,
			cancellationToken,
			statusMsg,
			requestedId);
		if (statusMsg.IsHistoryOnly())
		{
			await CompleteOrderStatusAsync(
				statusMsg, cancellationToken);
			return;
		}

		if (_orderStatusSubscriptionId != 0)
			throw new InvalidOperationException(
				"Delta Exchange India order-status subscription " +
					"already exists.");
		_orderStatusSubscriptionId = statusMsg.TransactionId;
		try
		{
			await PrivateWsClient.SubscribeAsync(
				"orders", "all", cancellationToken);
			try
			{
				await PrivateWsClient.SubscribeAsync(
					"v2/user_trades",
					"all",
					cancellationToken);
			}
			catch
			{
				await PrivateWsClient.UnsubscribeAsync(
					"orders", "all", cancellationToken);
				throw;
			}
			await SendSubscriptionResultAsync(
				statusMsg, cancellationToken);
		}
		catch
		{
			_orderStatusSubscriptionId = 0;
			throw;
		}
	}

	private async ValueTask SendPortfolioSnapshotAsync(
		long originalTransactionId,
		bool force,
		CancellationToken cancellationToken)
	{
		var balancesTask = RestClient.GetBalancesAsync(
			cancellationToken).AsTask();
		var positionsTask = RestClient.GetPositionsAsync(
			cancellationToken).AsTask();
		await Task.WhenAll(balancesTask, positionsTask);

		foreach (var balance in await balancesTask ?? [])
			await SendBalanceAsync(
				balance,
				originalTransactionId,
				cancellationToken);

		foreach (var position in await positionsTask ?? [])
			await SendPositionAsync(
				position,
				originalTransactionId,
				cancellationToken);

		_ = force;
	}

	private ValueTask SendBalanceAsync(
		DeltaBalance balance,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (balance?.Asset.IsEmpty() != false)
			return default;
		return SendOutMessageAsync(
			new PositionChangeMessage
			{
				PortfolioName = GetPortfolioName(),
				SecurityId = new()
				{
					SecurityCode =
						balance.Asset.ToUpperInvariant(),
					BoardCode =
						BoardCodes.DeltaExchangeIndia,
				},
				ServerTime = CurrentTime,
				OriginalTransactionId =
					originalTransactionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue,
				balance.Current,
				true)
			.TryAdd(PositionChangeTypes.BlockedValue,
				balance.Blocked,
				true),
			cancellationToken);
	}

	private ValueTask SendPositionAsync(
		DeltaPosition position,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (position is null)
			return default;
		var product =
			GetProduct(position.ProductId) ??
			GetProduct(position.Symbol);
		if (product is null)
			return default;
		var side = position.Size == 0
			? (Sides?)null
			: position.Size > 0
				? Sides.Buy
				: Sides.Sell;
		return SendOutMessageAsync(
			new PositionChangeMessage
			{
				PortfolioName = GetPortfolioName(),
				SecurityId = product.ToStockSharp(),
				ServerTime = CurrentTime,
				OriginalTransactionId =
					originalTransactionId,
				Side = side,
			}
			.TryAdd(PositionChangeTypes.CurrentValue,
				position.Size.Abs(),
				true)
			.TryAdd(PositionChangeTypes.AveragePrice,
				position.EntryPrice,
				true)
			.TryAdd(PositionChangeTypes.LiquidationPrice,
				position.LiquidationPrice,
				true)
			.TryAdd(PositionChangeTypes.BlockedValue,
				position.Margin,
				true)
			.TryAdd(PositionChangeTypes.RealizedPnL,
				position.RealizedPnl,
				true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL,
				position.UnrealizedPnl,
				true),
			cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(
		long originalTransactionId,
		DeltaProduct product,
		DateTime? from,
		DateTime? to,
		bool force,
		CancellationToken cancellationToken,
		OrderStatusMessage filter = null,
		long? requestedId = null)
	{
		if (requestedId is > 0)
		{
			var requested = await RestClient.GetOrderAsync(
				requestedId.Value, cancellationToken);
			if (IsOrderMatch(requested, filter))
				await SendOrderAsync(
					requested,
					originalTransactionId,
					force,
					cancellationToken);
		}
		else
		{
			var openTask = RestClient.GetOpenOrdersAsync(
				product?.Id, cancellationToken).AsTask();
			var historyTask = RestClient.GetOrderHistoryAsync(
				product?.Id,
				from,
				to,
				50,
				cancellationToken).AsTask();
			await Task.WhenAll(openTask, historyTask);

			foreach (var order in
				(await openTask ?? [])
				.Concat(await historyTask ?? [])
				.GroupBy(static order => order.Id)
				.Select(static group => group
					.OrderByDescending(order => order.UpdatedAt)
					.First())
				.Where(order => IsOrderMatch(order, filter))
				.OrderBy(static order => order.CreatedAt)
				.TakeLast((filter?.Count ?? 50)
					.Max(1).Min(50).To<int>()))
				await SendOrderAsync(
					order,
					originalTransactionId,
					force,
					cancellationToken);
		}

		foreach (var fill in
			await RestClient.GetFillsAsync(
				product?.Id,
				from,
				to,
				50,
				cancellationToken) ?? [])
		{
			if (IsFillMatch(fill, filter, requestedId))
				await SendFillAsync(
					fill,
					originalTransactionId,
					cancellationToken);
		}
	}

	private ValueTask SendOrderAsync(
		DeltaOrder order,
		long originalTransactionId,
		bool force,
		CancellationToken cancellationToken)
	{
		if (order?.Id is not > 0)
			return default;
		var product =
			GetProduct(order.ProductId) ??
			GetProduct(order.Symbol);
		var tracked = GetTrackedOrder(order.Id);
		product ??= tracked is null
			? null
			: GetProduct(tracked.ProductId);
		if (product is null)
			return default;
		tracked ??= new()
		{
			TransactionId =
				ParseTransactionId(order.ClientOrderId),
			ProductId = product.Id,
			Symbol = product.Symbol,
			Side = order.Side,
			OrderType = order.OrderType,
			Volume = order.Volume,
			Price = order.Price,
		};
		TrackOrder(order.Id, tracked);
		var type = order.OrderType == OrderTypes.Conditional
			? OrderTypes.Conditional
			: tracked.OrderType;
		return SendOutMessageAsync(
			new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = product.ToStockSharp(),
				ServerTime = order.UpdatedAt == default
					? order.CreatedAt == default
						? CurrentTime
						: order.CreatedAt
					: order.UpdatedAt,
				PortfolioName = GetPortfolioName(),
				Side = order.Side,
				OrderVolume = order.Volume > 0
					? order.Volume
					: tracked.Volume,
				Balance = order.Balance,
				OrderPrice = order.Price > 0
					? order.Price
					: tracked.Price,
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
				TransactionId = tracked.TransactionId,
				OriginalTransactionId =
					originalTransactionId,
				TimeInForce = order.TimeInForce,
				Condition = type == OrderTypes.Conditional
					? new DeltaExchangeIndiaOrderCondition
					{
						StopPrice = order.StopPrice,
						IsReduceOnly = order.ReduceOnly,
					}
					: null,
			},
			cancellationToken);
	}

	private ValueTask SendFillAsync(
		DeltaFill fill,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (fill is null ||
			fill.Id.IsEmpty() ||
			!AddTrade("private", fill.Id))
			return default;
		var product =
			GetProduct(fill.ProductId) ??
			GetProduct(fill.Symbol) ??
			GetProduct(
				GetTrackedOrder(fill.OrderId)?.ProductId ?? 0);
		if (product is null)
			return default;
		var tracked = GetTrackedOrder(fill.OrderId);
		return SendOutMessageAsync(
			new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				SecurityId = product.ToStockSharp(),
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
				TradeStringId = fill.Id,
				TradePrice = fill.Price,
				TradeVolume = fill.Volume.Abs(),
				TransactionId = tracked?.TransactionId ??
					ParseTransactionId(fill.ClientOrderId),
				OriginalTransactionId =
					originalTransactionId,
				Commission = fill.Commission != 0
					? fill.Commission
					: null,
				CommissionCurrency = fill.CommissionCurrency,
			},
			cancellationToken);
	}

	private bool IsOrderMatch(
		DeltaOrder order,
		OrderStatusMessage filter)
	{
		if (order is null)
			return false;
		if (filter is null)
			return true;
		if (filter.From is DateTime from &&
			order.CreatedAt < from.ToUniversalTime() ||
			filter.To is DateTime to &&
			order.CreatedAt > to.ToUniversalTime())
			return false;
		if (filter.Side is Sides side &&
			order.Side != side)
			return false;
		if (filter.States.Length > 0 &&
			!filter.States.Contains(order.State))
			return false;
		if (filter.Volume is decimal volume &&
			order.Volume != volume)
			return false;
		if (!filter.SecurityId.SecurityCode.IsEmpty() &&
			!filter.SecurityId.SecurityCode.EqualsIgnoreCase(
				order.Symbol) &&
			GetProduct(filter.SecurityId)?.Id != order.ProductId)
			return false;
		return true;
	}

	private bool IsFillMatch(
		DeltaFill fill,
		OrderStatusMessage filter,
		long? requestedId)
	{
		if (fill is null)
			return false;
		if (requestedId is > 0 && fill.OrderId != requestedId)
			return false;
		if (filter is null)
			return true;
		if (filter.From is DateTime from &&
			fill.Time < from.ToUniversalTime() ||
			filter.To is DateTime to &&
			fill.Time > to.ToUniversalTime())
			return false;
		if (filter.Side is Sides side &&
			fill.Side != side)
			return false;
		if (!filter.SecurityId.SecurityCode.IsEmpty())
		{
			var product = GetProduct(filter.SecurityId);
			if (fill.ProductId > 0 &&
				fill.ProductId != product.Id ||
				!fill.Symbol.IsEmpty() &&
				!fill.Symbol.EqualsIgnoreCase(product.Symbol))
				return false;
		}
		return true;
	}

	private DeltaProduct ResolveOrderProduct(
		SecurityId securityId,
		long? orderId)
	{
		if (!securityId.SecurityCode.IsEmpty())
			return GetProduct(securityId);
		if (orderId is > 0)
		{
			var tracked = GetTrackedOrder(orderId.Value);
			if (tracked is not null)
				return GetProduct(tracked.ProductId);
		}
		throw new InvalidOperationException(
			"Delta Exchange India order operation requires the " +
				"product when the order was not registered by " +
				"this adapter instance.");
	}

	private void ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(GetPortfolioName()))
			throw new InvalidOperationException(
				$"Unknown Delta Exchange India portfolio " +
					$"'{portfolioName}'.");
	}

	private static int ValidateSize(decimal volume)
	{
		if (volume <= 0 ||
			volume != decimal.Truncate(volume) ||
			volume > int.MaxValue)
			throw new ArgumentOutOfRangeException(
				nameof(volume),
				volume,
				"Delta Exchange India order size must be a " +
					"positive whole number of contracts.");
		return decimal.ToInt32(volume);
	}

	private static long ResolveOrderId(
		long? numericOrderId,
		string stringOrderId)
		=> ResolveOrderIdentity(
			numericOrderId, stringOrderId).OrderId ??
			throw new InvalidOperationException(
				"Delta Exchange India edit requires a numeric " +
					"exchange order ID.");

	private static (
		long? OrderId,
		string ClientOrderId) ResolveOrderIdentity(
		long? numericOrderId,
		string stringOrderId)
	{
		if (numericOrderId is > 0)
			return (numericOrderId, null);
		if (stringOrderId.IsEmpty())
			throw new InvalidOperationException(
				"Delta Exchange India operation requires an " +
					"exchange or client order ID.");
		stringOrderId = stringOrderId.Trim();
		return long.TryParse(
			stringOrderId,
			NumberStyles.None,
			CultureInfo.InvariantCulture,
			out var parsed)
				? (parsed, null)
				: (null, stringOrderId);
	}

	private static long ParseTransactionId(string clientOrderId)
		=> clientOrderId?.StartsWithIgnoreCase("ss-") == true &&
			long.TryParse(
				clientOrderId[3..],
				NumberStyles.None,
				CultureInfo.InvariantCulture,
				out var value)
					? value
					: 0;

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
