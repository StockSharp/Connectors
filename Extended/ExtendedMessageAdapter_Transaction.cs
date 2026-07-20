namespace StockSharp.Extended;

using Native;

public partial class ExtendedMessageAdapter
{
	private const decimal _defaultTakerFee = 0.0005m;

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(regMsg.PortfolioName);
		var market = GetMarket(regMsg.SecurityId);
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market or
			OrderTypes.Conditional))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		var condition = regMsg.Condition as ExtendedOrderCondition ?? new();
		var volume = regMsg.Volume.Abs();
		var apiOrderType = orderType.ToExtended();
		var executionOrderType = orderType == OrderTypes.Conditional &&
			condition.ExecutionPriceType == ExtendedExecutionPriceTypes.Market
				? OrderTypes.Market
				: orderType == OrderTypes.Conditional
					? OrderTypes.Limit
					: orderType;
		var isPostOnly = regMsg.PostOnly == true;
		if (executionOrderType == OrderTypes.Market && isPostOnly)
			throw new NotSupportedException(
				"Extended market execution cannot be post-only.");
		var price = executionOrderType == OrderTypes.Market
			? GetProtectivePrice(market, regMsg.Side)
			: regMsg.Price;
		ValidateOrder(market, apiOrderType, volume, price, condition);
		var timeInForce = regMsg.TimeInForce.ToExtended(isPostOnly,
			executionOrderType);
		var isReduceOnly = condition.IsReduceOnly ||
			regMsg.PositionEffect == OrderPositionEffects.CloseOnly;
		var expiry = GetOrderExpiry(regMsg.TillDate);
		var request = Signer.CreateOrder(market, regMsg.Side.ToExtended(),
			apiOrderType, volume, price, timeInForce, isPostOnly, isReduceOnly,
			expiry, GetTakerFee(market.Name, condition), condition, null);
		await PlaceOrderAsync(request, regMsg.TransactionId, market.Name,
			regMsg.Side, volume, price, orderType, condition, expiry,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(replaceMsg.PortfolioName);
		var market = GetMarket(replaceMsg.SecurityId);
		var orderType = replaceMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market or
			OrderTypes.Conditional))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		var condition = replaceMsg.Condition as ExtendedOrderCondition ?? new();
		var apiOrderType = orderType.ToExtended();
		var executionOrderType = orderType == OrderTypes.Conditional &&
			condition.ExecutionPriceType == ExtendedExecutionPriceTypes.Market
				? OrderTypes.Market
				: orderType == OrderTypes.Conditional
					? OrderTypes.Limit
					: orderType;
		var isPostOnly = replaceMsg.PostOnly == true;
		if (executionOrderType == OrderTypes.Market && isPostOnly)
			throw new NotSupportedException(
				"Extended market execution cannot be post-only.");
		var volume = replaceMsg.Volume.Abs();
		var price = executionOrderType == OrderTypes.Market
			? GetProtectivePrice(market, replaceMsg.Side)
			: replaceMsg.Price;
		ValidateOrder(market, apiOrderType, volume, price, condition);
		var cancelId = await ResolveReplacementExternalIdAsync(replaceMsg,
			cancellationToken);
		var expiry = GetOrderExpiry(replaceMsg.TillDate);
		var request = Signer.CreateOrder(market, replaceMsg.Side.ToExtended(),
			apiOrderType, volume, price,
			replaceMsg.TimeInForce.ToExtended(isPostOnly, executionOrderType),
			isPostOnly, condition.IsReduceOnly ||
				replaceMsg.PositionEffect == OrderPositionEffects.CloseOnly,
			expiry, GetTakerFee(market.Name, condition), condition, cancelId);
		await PlaceOrderAsync(request, replaceMsg.TransactionId, market.Name,
			replaceMsg.Side, volume, price, orderType, condition, expiry,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		if (!cancelMsg.SecurityId.SecurityCode.IsEmpty())
			_ = GetMarket(cancelMsg.SecurityId);

		var orderId = cancelMsg.OrderId is > 0 ? cancelMsg.OrderId : null;
		var externalId = cancelMsg.OrderStringId;
		if (orderId is null && !externalId.IsEmpty() &&
			long.TryParse(externalId, NumberStyles.None, CultureInfo.InvariantCulture,
				out var parsed) && parsed > 0)
		{
			orderId = parsed;
			externalId = null;
		}
		if (orderId is null && externalId.IsEmpty())
			externalId = cancelMsg.UserOrderId;
		if (orderId is long numericId)
			await RestClient.CancelOrderAsync(numericId, cancellationToken);
		else if (!externalId.IsEmpty())
			await RestClient.CancelOrderAsync(externalId.Trim(), cancellationToken);
		else
			throw new InvalidOperationException(
				"Extended cancellation requires an order ID or external ID.");

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = cancelMsg.SecurityId,
			ServerTime = DateTime.UtcNow,
			PortfolioName = _portfolioName,
			OrderId = orderId,
			UserOrderId = externalId,
			OrderState = OrderStates.Done,
			Balance = 0m,
			OriginalTransactionId = cancelMsg.TransactionId,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"Extended bulk cancellation does not close positions.");
		if (cancelMsg.SecurityTypes is { Length: > 0 } &&
			!cancelMsg.SecurityTypes.Contains(SecurityTypes.Future) &&
			!cancelMsg.SecurityTypes.Contains(SecurityTypes.CryptoCurrency))
			return;

		var symbol = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(cancelMsg.SecurityId).Name;
		if (cancelMsg.Side is null && cancelMsg.IsStop is null)
		{
			await RestClient.MassCancelAsync(new ExtendedMassCancelRequest
			{
				Markets = symbol.IsEmpty() ? null : [symbol],
				IsCancelAll = symbol.IsEmpty() ? true : null,
			}, cancellationToken);
			return;
		}

		var orders = await RestClient.GetOpenOrdersAsync(symbol,
			cancellationToken);
		var orderIds = (orders ?? [])
			.Where(static order => order is not null && order.Id > 0)
			.Where(order => symbol.IsEmpty() ||
				order.Market.Equals(symbol, StringComparison.Ordinal))
			.Where(order => cancelMsg.Side is null ||
				order.Side.ToStockSharp() == cancelMsg.Side)
			.Where(order => cancelMsg.IsStop is null ||
				IsConditional(order.Type) == cancelMsg.IsStop)
			.Select(static order => order.Id)
			.Distinct()
			.ToArray();
		if (orderIds.Length > 0)
			await RestClient.MassCancelAsync(new ExtendedMassCancelRequest
			{
				OrderIds = orderIds,
			}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureAccountReady();
		ValidatePortfolio(lookupMsg.PortfolioName);
		if (!lookupMsg.IsSubscribe)
		{
			_portfolioSubscriptionId = 0;
			return;
		}

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = _portfolioName,
			BoardCode = BoardCodes.Extended,
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
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId,
			cancellationToken);
		EnsureAccountReady();
		ValidatePortfolio(statusMsg.PortfolioName);
		if (!statusMsg.IsSubscribe)
		{
			_orderStatusSubscriptionId = 0;
			return;
		}

		await SendOrderSnapshotAsync(statusMsg, cancellationToken);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}
		_orderStatusSubscriptionId = statusMsg.TransactionId;
	}

	private async ValueTask PlaceOrderAsync(ExtendedCreateOrderRequest request,
		long transactionId, string symbol, Sides side, decimal volume,
		decimal price, OrderTypes orderType, ExtendedOrderCondition condition,
		DateTime expiry, CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
		{
			if (_transactionByExternalId.ContainsKey(request.Id))
				throw new InvalidOperationException(
					"Extended external order ID is already in use.");
			_transactionByExternalId.Add(request.Id, transactionId);
		}
		try
		{
			var response = await RestClient.PlaceOrderAsync(request,
				cancellationToken) ?? throw new InvalidDataException(
					"Extended returned no placed-order information.");
			var externalId = response.ExternalId.IsEmpty()
				? request.Id
				: response.ExternalId;
			TrackOrder(response.Id, externalId, transactionId);
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = symbol.ToStockSharp(),
				ServerTime = DateTime.UtcNow,
				PortfolioName = _portfolioName,
				Side = side,
				OrderVolume = volume,
				Balance = volume,
				OrderPrice = price,
				OrderType = orderType,
				OrderState = OrderStates.Pending,
				OrderId = response.Id > 0 ? response.Id : null,
				UserOrderId = externalId,
				TransactionId = transactionId,
				OriginalTransactionId = transactionId,
				ExpiryDate = expiry,
				PositionEffect = condition.IsReduceOnly
					? OrderPositionEffects.CloseOnly
					: null,
				Condition = condition,
			}, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_transactionByExternalId.Remove(request.Id);
			throw;
		}
	}

	private async ValueTask SendPortfolioSnapshotAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		var balance = await RestClient.GetBalanceAsync(cancellationToken);
		if (balance is not null)
			await SendBalanceAsync(balance, transactionId, DateTime.UtcNow,
				cancellationToken);

		var positions = await RestClient.GetPositionsAsync(null,
			cancellationToken);
		await SendPositionSnapshotAsync(positions, transactionId,
			cancellationToken);

		var spotBalances = await RestClient.GetSpotBalancesAsync(cancellationToken);
		await SendSpotBalanceSnapshotAsync(spotBalances, transactionId,
			cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		var symbols = GetOrderSymbols(statusMsg);
		var skip = Math.Max(0, statusMsg.Skip ?? 0).To<int>();
		var limit = (statusMsg.Count ?? HistoryLimit)
			.Min(HistoryLimit).Max(1).To<int>();
		var maximum = checked(skip + limit).Min(HistoryLimit);
		var querySymbol = symbols.Count == 1 ? symbols.First() : null;
		var open = await RestClient.GetOpenOrdersAsync(querySymbol,
			cancellationToken);
		var history = await LoadOrderHistoryAsync(querySymbol, maximum,
			cancellationToken);
		var messages = (open ?? []).Concat(history)
			.Where(static order => order is not null && order.Id > 0)
			.Select(order => CreateOrderMessage(order, statusMsg.TransactionId))
			.Where(static message => message is not null)
			.Where(message => IsOrderMatch(message, statusMsg, symbols))
			.GroupBy(static message => message.OrderId)
			.Select(static group => group.OrderByDescending(
				message => message.ServerTime).First())
			.OrderBy(static message => message.ServerTime)
			.Skip(skip)
			.Take(limit)
			.ToArray();
		foreach (var message in messages)
		{
			UpdateServerTime(message.ServerTime);
			await SendOutMessageAsync(message, cancellationToken);
		}

		var trades = await LoadTradeHistoryAsync(querySymbol, maximum,
			cancellationToken);
		foreach (var trade in trades
			.Where(trade => IsTradeMatch(trade, statusMsg, symbols))
			.OrderBy(static trade => trade.CreatedTime)
			.Skip(skip)
			.Take(limit))
			await SendAccountTradeAsync(trade, statusMsg.TransactionId, false,
				cancellationToken);
	}

	private async ValueTask<ExtendedOrder[]> LoadOrderHistoryAsync(string symbol,
		int maximum, CancellationToken cancellationToken)
	{
		var result = new List<ExtendedOrder>();
		long? cursor = null;
		while (result.Count < maximum)
		{
			var pageLimit = (maximum - result.Count).Min(HistoryLimit).Max(1);
			var page = await RestClient.GetOrderHistoryAsync(symbol, pageLimit,
				cursor, cancellationToken);
			var data = (page.Data ?? [])
				.Where(static order => order is not null).ToArray();
			result.AddRange(data);
			var next = page.Pagination?.Cursor;
			if (data.Length < pageLimit || next is null || next == cursor)
				break;
			cursor = next;
		}
		return [.. result.Take(maximum)];
	}

	private async ValueTask<ExtendedAccountTrade[]> LoadTradeHistoryAsync(
		string symbol, int maximum, CancellationToken cancellationToken)
	{
		var result = new List<ExtendedAccountTrade>();
		long? cursor = null;
		while (result.Count < maximum)
		{
			var pageLimit = (maximum - result.Count).Min(HistoryLimit).Max(1);
			var page = await RestClient.GetTradesAsync(symbol, pageLimit, cursor,
				cancellationToken);
			var data = (page.Data ?? [])
				.Where(static trade => trade is not null).ToArray();
			result.AddRange(data);
			var next = page.Pagination?.Cursor;
			if (data.Length < pageLimit || next is null || next == cursor)
				break;
			cursor = next;
		}
		return [.. result
			.GroupBy(static trade => trade.Id)
			.Select(static group => group.First())
			.Take(maximum)];
	}

	private async ValueTask OnPositionsAsync(ExtendedPosition[] positions,
		bool isSnapshot, long timestamp, long sequence,
		CancellationToken cancellationToken)
	{
		_ = sequence;
		if (_portfolioSubscriptionId == 0)
			return;
		var current = new HashSet<string>(StringComparer.Ordinal);
		foreach (var position in positions ?? [])
		{
			if (position?.Market.IsEmpty() != false)
				continue;
			current.Add(position.Market);
			await SendPositionAsync(position, _portfolioSubscriptionId,
				cancellationToken);
		}
		if (isSnapshot)
			await SendMissingPositionsAsync(current, _knownPositionSymbols,
				_portfolioSubscriptionId, timestamp.ToExtendedTimeOrNow(),
				cancellationToken);
	}

	private async ValueTask OnOrdersAsync(ExtendedOrder[] orders,
		bool isSnapshot, long timestamp, long sequence,
		CancellationToken cancellationToken)
	{
		_ = isSnapshot;
		_ = timestamp;
		_ = sequence;
		foreach (var order in orders ?? [])
		{
			if (order is null)
				continue;
			var transactionId = GetTransactionId(order.ExternalId);
			if (transactionId == 0)
				transactionId = GetTransactionId(order.Id);
			var originalTransactionId = _orderStatusSubscriptionId != 0
				? _orderStatusSubscriptionId
				: transactionId;
			if (originalTransactionId != 0)
				await SendOrderAsync(order, originalTransactionId,
					cancellationToken);
		}
	}

	private async ValueTask OnAccountTradesAsync(ExtendedAccountTrade[] trades,
		bool isSnapshot, long timestamp, long sequence,
		CancellationToken cancellationToken)
	{
		_ = isSnapshot;
		_ = timestamp;
		_ = sequence;
		foreach (var trade in trades ?? [])
		{
			var transactionId = GetTransactionId(trade?.OrderId ?? 0);
			var originalTransactionId = _orderStatusSubscriptionId != 0
				? _orderStatusSubscriptionId
				: transactionId;
			if (originalTransactionId != 0)
				await SendAccountTradeAsync(trade, originalTransactionId, true,
					cancellationToken);
		}
	}

	private ValueTask OnBalanceAsync(ExtendedBalance balance, bool isSnapshot,
		long timestamp, long sequence, CancellationToken cancellationToken)
	{
		_ = isSnapshot;
		_ = sequence;
		return _portfolioSubscriptionId == 0
			? default
			: SendBalanceAsync(balance, _portfolioSubscriptionId,
				timestamp.ToExtendedTimeOrNow(), cancellationToken);
	}

	private async ValueTask OnSpotBalancesAsync(ExtendedSpotBalance[] balances,
		bool isSnapshot, long timestamp, long sequence,
		CancellationToken cancellationToken)
	{
		_ = sequence;
		if (_portfolioSubscriptionId == 0)
			return;
		var current = new HashSet<string>(StringComparer.Ordinal);
		foreach (var balance in balances ?? [])
		{
			if (balance?.Asset.IsEmpty() != false)
				continue;
			current.Add(balance.Asset);
			await SendSpotBalanceAsync(balance, _portfolioSubscriptionId,
				cancellationToken);
		}
		if (isSnapshot)
			await SendMissingPositionsAsync(current, _knownSpotBalanceSymbols,
				_portfolioSubscriptionId, timestamp.ToExtendedTimeOrNow(),
				cancellationToken);
	}

	private ValueTask SendBalanceAsync(ExtendedBalance balance,
		long transactionId, DateTime fallbackTime,
		CancellationToken cancellationToken)
	{
		if (balance is null || transactionId == 0)
			return default;
		var time = balance.UpdatedTime > 0
			? balance.UpdatedTime.ToExtendedTime()
			: fallbackTime;
		UpdateServerTime(time);
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = balance.CollateralName.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(PositionChangeTypes.BeginValue,
			balance.Balance.TryParseExtendedDecimal(), true)
		.TryAdd(PositionChangeTypes.CurrentValue,
			balance.Equity.TryParseExtendedDecimal(), true)
		.TryAdd(PositionChangeTypes.BlockedValue,
			balance.InitialMargin.TryParseExtendedDecimal(), true)
		.TryAdd(PositionChangeTypes.CurrentPrice,
			balance.AvailableForTrade.TryParseExtendedDecimal(), true)
		.TryAdd(PositionChangeTypes.VariationMargin,
			balance.UnrealizedPnL.TryParseExtendedDecimal(), true),
			cancellationToken);
	}

	private async ValueTask SendPositionSnapshotAsync(
		ExtendedPosition[] positions, long transactionId,
		CancellationToken cancellationToken)
	{
		var current = new HashSet<string>(StringComparer.Ordinal);
		foreach (var position in positions ?? [])
		{
			if (position?.Market.IsEmpty() != false)
				continue;
			current.Add(position.Market);
			await SendPositionAsync(position, transactionId, cancellationToken);
		}
		await SendMissingPositionsAsync(current, _knownPositionSymbols,
			transactionId, ServerTime, cancellationToken);
	}

	private ValueTask SendPositionAsync(ExtendedPosition position,
		long transactionId, CancellationToken cancellationToken)
	{
		if (position?.Market.IsEmpty() != false || transactionId == 0)
			return default;
		var time = (position.UpdatedAt > 0
			? position.UpdatedAt
			: position.CreatedAt).ToExtendedTimeOrNow();
		UpdateServerTime(time);
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = position.Market.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
			Side = position.Side.ToStockSharp(),
		}
		.TryAdd(PositionChangeTypes.CurrentValue,
			position.Status == ExtendedPositionStatuses.Closed
				? 0m
				: position.Size.TryParseExtendedDecimal()?.Abs(), true)
		.TryAdd(PositionChangeTypes.AveragePrice,
			position.OpenPrice.TryParseExtendedDecimal(), true)
		.TryAdd(PositionChangeTypes.CurrentPrice,
			position.MarkPrice.TryParseExtendedDecimal(), true)
		.TryAdd(PositionChangeTypes.OrdersMargin,
			position.Margin.TryParseExtendedDecimal(), true)
		.TryAdd(PositionChangeTypes.VariationMargin,
			position.UnrealizedPnL.TryParseExtendedDecimal(), true)
		.TryAdd(PositionChangeTypes.LiquidationPrice,
			position.LiquidationPrice.TryParseExtendedDecimal(), true),
			cancellationToken);
	}

	private async ValueTask SendSpotBalanceSnapshotAsync(
		ExtendedSpotBalance[] balances, long transactionId,
		CancellationToken cancellationToken)
	{
		var current = new HashSet<string>(StringComparer.Ordinal);
		foreach (var balance in balances ?? [])
		{
			if (balance?.Asset.IsEmpty() != false)
				continue;
			current.Add(balance.Asset);
			await SendSpotBalanceAsync(balance, transactionId, cancellationToken);
		}
		await SendMissingPositionsAsync(current, _knownSpotBalanceSymbols,
			transactionId, ServerTime, cancellationToken);
	}

	private ValueTask SendSpotBalanceAsync(ExtendedSpotBalance balance,
		long transactionId, CancellationToken cancellationToken)
	{
		if (balance?.Asset.IsEmpty() != false || transactionId == 0)
			return default;
		var time = balance.UpdatedAt.ToExtendedTimeOrNow();
		var total = balance.Balance.TryParseExtendedDecimal();
		var available = balance.AvailableToWithdraw.TryParseExtendedDecimal();
		UpdateServerTime(time);
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = balance.Asset.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, total, true)
		.TryAdd(PositionChangeTypes.BlockedValue,
			total is decimal value && available is decimal free
				? (value - free).Max(0m)
				: null, true)
		.TryAdd(PositionChangeTypes.AveragePrice,
			balance.AverageEntryPrice.TryParseExtendedDecimal(), true)
		.TryAdd(PositionChangeTypes.CurrentPrice,
			balance.IndexPrice.TryParseExtendedDecimal(), true), cancellationToken);
	}

	private async ValueTask SendMissingPositionsAsync(HashSet<string> current,
		HashSet<string> known, long transactionId, DateTime time,
		CancellationToken cancellationToken)
	{
		string[] missing;
		using (_sync.EnterScope())
		{
			missing = [.. known.Where(symbol => !current.Contains(symbol))];
			known.Clear();
			known.UnionWith(current);
		}
		foreach (var symbol in missing)
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = symbol.ToStockSharp(),
				ServerTime = time,
				OriginalTransactionId = transactionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, 0m, true),
				cancellationToken);
	}

	private async ValueTask SendOrderAsync(ExtendedOrder order,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		var message = CreateOrderMessage(order, originalTransactionId);
		if (message is null)
			return;
		UpdateServerTime(message.ServerTime);
		await SendOutMessageAsync(message, cancellationToken);
	}

	private ExecutionMessage CreateOrderMessage(ExtendedOrder order,
		long originalTransactionId)
	{
		if (order?.Market.IsEmpty() != false || order.Id <= 0)
			return null;
		var volume = order.Quantity.TryParseExtendedDecimal();
		var filled = order.FilledQuantity.TryParseExtendedDecimal() ?? 0m;
		var cancelled = order.CancelledQuantity.TryParseExtendedDecimal() ?? 0m;
		var time = (order.UpdatedTime > 0
			? order.UpdatedTime
			: order.CreatedTime).ToExtendedTimeOrNow();
		var transactionId = GetTransactionId(order.ExternalId);
		if (transactionId == 0)
			transactionId = GetTransactionId(order.Id);
		TrackOrder(order.Id, order.ExternalId, transactionId);
		var condition = CreateCondition(order);
		var state = order.Status.ToStockSharp();
		return new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Market.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = order.Side.ToStockSharp(),
			OrderVolume = volume,
			Balance = volume is decimal total
				? (total - filled - cancelled).Max(0m)
				: null,
			OrderPrice = order.Price.TryParseExtendedDecimal() ?? 0m,
			AveragePrice = order.AveragePrice.TryParseExtendedDecimal(),
			OrderType = order.Type.ToStockSharp(),
			OrderState = state,
			OrderId = order.Id,
			UserOrderId = order.ExternalId,
			TransactionId = transactionId,
			OriginalTransactionId = originalTransactionId,
			ExpiryDate = order.ExpiryTime?.ToExtendedTime(),
			Commission = order.PaidFee.TryParseExtendedDecimal(),
			PositionEffect = order.IsReduceOnly
				? OrderPositionEffects.CloseOnly
				: null,
			Condition = condition,
			Error = state == OrderStates.Failed
				? new InvalidOperationException(order.StatusReason.IsEmpty()
					? "Extended rejected the order."
					: order.StatusReason)
				: null,
		};
	}

	private ValueTask SendAccountTradeAsync(ExtendedAccountTrade trade,
		long originalTransactionId, bool onlyNew,
		CancellationToken cancellationToken)
	{
		if (trade?.Market.IsEmpty() != false || trade.Id <= 0)
			return default;
		var isNew = TryAcceptAccountTrade(trade.Id);
		if (onlyNew && !isNew)
			return default;
		var time = trade.CreatedTime.ToExtendedTimeOrNow();
		UpdateServerTime(time);
		var externalId = GetExternalId(trade.OrderId);
		var transactionId = GetTransactionId(trade.OrderId);
		TryGetMarket(trade.Market, out var market);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = trade.Market.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = trade.Side.ToStockSharp(),
			OrderId = trade.OrderId,
			UserOrderId = externalId,
			TradeId = trade.Id,
			TradePrice = trade.Price.ParseExtendedDecimal("account trade price"),
			TradeVolume = trade.Quantity.ParseExtendedDecimal(
				"account trade quantity"),
			Commission = trade.Fee.TryParseExtendedDecimal(),
			CommissionCurrency = market?.CollateralAssetName,
			TransactionId = transactionId,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private void ValidateOrder(ExtendedMarket market,
		ExtendedOrderTypes orderType, decimal volume, decimal price,
		ExtendedOrderCondition condition)
	{
		var config = market.TradingConfig;
		var minimum = config.MinimumOrderSize.ParseExtendedDecimal(
			"minimum order size");
		var volumeStep = config.MinimumOrderSizeChange.ParseExtendedDecimal(
			"minimum order size change");
		if (volume < minimum || !volume.IsMultipleOf(volumeStep))
			throw new InvalidOperationException(
				"Extended order volume must be at least " +
				minimum.ToExtendedWire() + " and a multiple of " +
				volumeStep.ToExtendedWire() + ".");
		ValidatePrice(market, price, "order price");

		if (orderType == ExtendedOrderTypes.Conditional)
		{
			if (condition.TriggerPrice is not decimal triggerPrice)
				throw new InvalidOperationException(
					"Extended conditional orders require a trigger price.");
			ValidatePrice(market, triggerPrice, "trigger price");
			if (!Enum.IsDefined(condition.TriggerPriceType) ||
				!Enum.IsDefined(condition.TriggerDirection) ||
				!Enum.IsDefined(condition.ExecutionPriceType))
				throw new InvalidOperationException(
					"Extended conditional order settings are invalid.");
		}
		else if (condition.TriggerPrice is not null)
			throw new InvalidOperationException(
				"Extended trigger price requires a conditional order type.");

		var notional = checked(volume * price);
		var isMarketExecution = orderType == ExtendedOrderTypes.Market ||
			orderType == ExtendedOrderTypes.Conditional &&
			condition.ExecutionPriceType == ExtendedExecutionPriceTypes.Market;
		var maximum = (isMarketExecution
			? config.MaximumMarketOrderValue
			: config.MaximumLimitOrderValue).TryParseExtendedDecimal();
		if (maximum is > 0 && notional > maximum)
			throw new InvalidOperationException(
				"Extended order value cannot exceed " +
				maximum.Value.ToExtendedWire() + ".");
	}

	private static void ValidatePrice(ExtendedMarket market, decimal price,
		string fieldName)
	{
		var tickSize = market.TradingConfig.MinimumPriceChange
			.ParseExtendedDecimal("minimum price change");
		if (price <= 0 || !price.IsMultipleOf(tickSize))
			throw new InvalidOperationException(
				"Extended " + fieldName + " must be a positive multiple of " +
				tickSize.ToExtendedWire() + ".");
	}

	private decimal GetProtectivePrice(ExtendedMarket market, Sides side)
	{
		var state = GetPriceState(market.Name);
		var reference = side == Sides.Buy
			? state?.BestAskPrice ?? state?.MarkPrice ?? state?.LastPrice ??
				state?.IndexPrice
			: state?.BestBidPrice ?? state?.MarkPrice ?? state?.LastPrice ??
				state?.IndexPrice;
		if (reference is not > 0)
			throw new InvalidOperationException(
				"Extended current price is unavailable for a protected market order.");
		var multiplier = side == Sides.Buy
			? 1m + MarketOrderSlippage / 100m
			: 1m - MarketOrderSlippage / 100m;
		var tick = market.TradingConfig.MinimumPriceChange
			.ParseExtendedDecimal("minimum price change");
		var scaled = reference.Value * multiplier / tick;
		var price = (side == Sides.Buy
			? Math.Ceiling(scaled)
			: Math.Floor(scaled)) * tick;
		if (price <= 0)
			throw new InvalidOperationException(
				"Extended protected market-order price is invalid.");
		return price;
	}

	private decimal GetTakerFee(string symbol, ExtendedOrderCondition condition)
	{
		if (condition.TakerFee is decimal configured)
			return configured;
		using (_sync.EnterScope())
			return _takerFees.TryGetValue(symbol, out var fee)
				? fee
				: _defaultTakerFee;
	}

	private DateTime GetOrderExpiry(DateTime? tillDate)
	{
		var now = DateTime.UtcNow;
		var expiry = (tillDate ?? now.Add(OrderExpiry)).EnsureExtendedUtc();
		if (expiry <= now)
			throw new InvalidOperationException(
				"Extended order expiry must be in the future.");
		if (expiry > now.AddDays(30))
			throw new InvalidOperationException(
				"Extended order expiry cannot be more than 30 days ahead.");
		return expiry;
	}

	private async ValueTask<string> ResolveReplacementExternalIdAsync(
		OrderReplaceMessage message, CancellationToken cancellationToken)
	{
		long? orderId = message.OldOrderId is > 0 ? message.OldOrderId : null;
		var externalId = message.OldOrderStringId?.Trim();
		if (orderId is null && !externalId.IsEmpty() &&
			long.TryParse(externalId, NumberStyles.None, CultureInfo.InvariantCulture,
				out var parsed) && parsed > 0)
		{
			orderId = parsed;
			externalId = null;
		}
		if (orderId is long numericId)
		{
			externalId = GetExternalId(numericId);
			if (externalId.IsEmpty())
			{
				var order = await RestClient.GetOrderAsync(numericId,
					cancellationToken) ?? throw new InvalidDataException(
						"Extended returned no order for replacement.");
				externalId = order.ExternalId;
				TrackOrder(order.Id, order.ExternalId,
					GetTransactionId(order.ExternalId));
			}
		}
		return externalId.ThrowIfEmpty(nameof(message.OldOrderStringId));
	}

	private static ExtendedOrderCondition CreateCondition(ExtendedOrder order)
	{
		var condition = new ExtendedOrderCondition
		{
			IsReduceOnly = order.IsReduceOnly,
		};
		if (order.Trigger is { } trigger)
		{
			condition.TriggerPrice = trigger.TriggerPrice.TryParseExtendedDecimal();
			condition.TriggerPriceType = trigger.TriggerPriceType ??
				ExtendedTriggerPriceTypes.Mark;
			condition.TriggerDirection = trigger.Direction ??
				ExtendedTriggerDirections.Up;
			condition.ExecutionPriceType = trigger.ExecutionPriceType ??
				ExtendedExecutionPriceTypes.Limit;
		}
		return condition;
	}

	private HashSet<string> GetOrderSymbols(OrderStatusMessage statusMsg)
	{
		var symbols = new HashSet<string>(StringComparer.Ordinal);
		if (!statusMsg.SecurityId.SecurityCode.IsEmpty())
			symbols.Add(GetMarket(statusMsg.SecurityId).Name);
		foreach (var securityId in statusMsg.SecurityIds)
			if (!securityId.SecurityCode.IsEmpty())
				symbols.Add(GetMarket(securityId).Name);
		return symbols;
	}

	private static bool IsOrderMatch(ExecutionMessage order,
		OrderStatusMessage filter, HashSet<string> symbols)
	{
		if (symbols.Count > 0 && !symbols.Contains(order.SecurityId.SecurityCode))
			return false;
		if (filter.OrderId is long orderId && order.OrderId != orderId)
			return false;
		if (!filter.OrderStringId.IsEmpty() &&
			!filter.OrderStringId.Equals(order.OrderId?.ToString(
				CultureInfo.InvariantCulture), StringComparison.Ordinal) &&
			!filter.OrderStringId.Equals(order.UserOrderId,
				StringComparison.Ordinal))
			return false;
		if (!filter.UserOrderId.IsEmpty() &&
			!filter.UserOrderId.Equals(order.UserOrderId,
				StringComparison.Ordinal))
			return false;
		if (filter.Side is Sides side && order.Side != side)
			return false;
		if (filter.Volume is decimal volume && order.OrderVolume != volume)
			return false;
		if (filter.States.Length > 0 &&
			(order.OrderState is not OrderStates state ||
				!filter.States.Contains(state)))
			return false;
		if (filter.From is DateTime from &&
			order.ServerTime < from.EnsureExtendedUtc())
			return false;
		if (filter.To is DateTime to &&
			order.ServerTime > to.EnsureExtendedUtc())
			return false;
		return true;
	}

	private static bool IsTradeMatch(ExtendedAccountTrade trade,
		OrderStatusMessage filter, HashSet<string> symbols)
	{
		if (symbols.Count > 0 && !symbols.Contains(trade.Market))
			return false;
		if (filter.OrderId is long orderId && trade.OrderId != orderId)
			return false;
		if (!filter.OrderStringId.IsEmpty() &&
			!filter.OrderStringId.Equals(trade.OrderId.ToString(
				CultureInfo.InvariantCulture), StringComparison.Ordinal))
			return false;
		if (filter.Side is Sides side && trade.Side.ToStockSharp() != side)
			return false;
		var time = trade.CreatedTime.ToExtendedTimeOrNow();
		if (filter.From is DateTime from && time < from.EnsureExtendedUtc())
			return false;
		if (filter.To is DateTime to && time > to.EnsureExtendedUtc())
			return false;
		return true;
	}

	private void ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(_portfolioName))
			throw new InvalidOperationException(
				"Unknown Extended portfolio '" + portfolioName + "'.");
	}

	private static bool IsConditional(ExtendedOrderTypes orderType)
		=> orderType is ExtendedOrderTypes.Conditional or
			ExtendedOrderTypes.TakeProfitStopLoss or ExtendedOrderTypes.Twap;
}
