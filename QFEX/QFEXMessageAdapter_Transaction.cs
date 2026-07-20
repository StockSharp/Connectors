namespace StockSharp.QFEX;

using Native;

public partial class QFEXMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		ValidatePortfolio(regMsg.PortfolioName);
		var market = GetMarket(regMsg.SecurityId);
		var volume = regMsg.Volume.Abs();
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		var isPostOnly = regMsg.PostOnly == true;
		var nativeOrderType = orderType.ToQFEX(isPostOnly);
		var timeInForce = regMsg.TimeInForce.ToQFEX(orderType);
		var condition = regMsg.Condition as QFEXOrderCondition ?? new();
		ValidateOrder(market, volume,
			orderType == OrderTypes.Market ? null : regMsg.Price,
			condition.TakeProfitPrice, condition.StopLossPrice,
			nativeOrderType, timeInForce);
		if (isPostOnly && timeInForce !=
			QFEXTimeInForces.GoodTillCancelled)
			throw new InvalidOperationException(
				"QFEX add-liquidity-only orders require GTC.");

		var clientOrderId = CreateClientOrderId(regMsg.TransactionId,
			regMsg.UserOrderId);
		using (_sync.EnterScope())
		{
			if (_transactionByClientOrderId.ContainsKey(clientOrderId))
				throw new InvalidOperationException(
					"QFEX client order ID '" + clientOrderId +
					"' is already in use.");
			_transactionByClientOrderId.Add(clientOrderId,
				regMsg.TransactionId);
		}
		QFEXOrder order;
		try
		{
			order = await TradeSocket.PlaceOrderAsync(new()
			{
				Symbol = market.Symbol,
				Side = regMsg.Side.ToQFEX(),
				OrderType = nativeOrderType,
				TimeInForce = timeInForce,
				Quantity = volume,
				Price = orderType == OrderTypes.Market ? null : regMsg.Price,
				IsReduceOnly = condition.IsReduceOnly ||
					regMsg.PositionEffect == OrderPositionEffects.CloseOnly,
				TakeProfit = condition.TakeProfitPrice,
				StopLoss = condition.StopLossPrice,
				ClientOrderId = clientOrderId,
			}, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_transactionByClientOrderId.Remove(clientOrderId);
			throw;
		}
		await SendOrderAsync(order, regMsg.TransactionId,
			regMsg.TransactionId, condition, market.Symbol, cancellationToken);
		if (order.Status.IsFailed())
			using (_sync.EnterScope())
				_transactionByClientOrderId.Remove(clientOrderId);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		ValidatePortfolio(replaceMsg.PortfolioName);
		var market = GetMarket(replaceMsg.SecurityId);
		var orderId = ResolveOrderId(replaceMsg.OldOrderId,
			replaceMsg.OldOrderStringId, "replacement");
		var volume = replaceMsg.Volume.Abs();
		var orderType = replaceMsg.OrderType ?? OrderTypes.Limit;
		var nativeOrderType = orderType.ToQFEX(
			replaceMsg.PostOnly == true);
		var condition = replaceMsg.Condition as QFEXOrderCondition ?? new();
		ValidateOrder(market, volume,
			orderType == OrderTypes.Market ? null : replaceMsg.Price,
			condition.TakeProfitPrice, condition.StopLossPrice,
			nativeOrderType, null);
		var order = await TradeSocket.ModifyOrderAsync(new()
		{
			Symbol = market.Symbol,
			OrderId = orderId,
			Price = orderType == OrderTypes.Market
				? null
				: replaceMsg.Price,
			Quantity = volume,
			TakeProfit = condition.TakeProfitPrice,
			StopLoss = condition.StopLossPrice,
			Side = replaceMsg.Side.ToQFEX(),
			OrderType = nativeOrderType,
		}, cancellationToken);
		await SendOrderAsync(order, replaceMsg.TransactionId,
			replaceMsg.TransactionId, condition, market.Symbol,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		var market = GetMarket(cancelMsg.SecurityId);
		string orderId;
		QFEXCancelOrderIdTypes idType;
		if (!cancelMsg.OrderStringId.IsEmpty() ||
			cancelMsg.OrderId is not null)
		{
			orderId = ResolveOrderId(cancelMsg.OrderId,
				cancelMsg.OrderStringId, "cancellation");
			idType = QFEXCancelOrderIdTypes.OrderId;
		}
		else if (!cancelMsg.UserOrderId.IsEmpty())
		{
			orderId = cancelMsg.UserOrderId.Trim();
			idType = QFEXCancelOrderIdTypes.ClientOrderId;
		}
		else
		{
			throw new InvalidOperationException(
				"QFEX cancellation requires an exchange or client order ID.");
		}
		var order = await TradeSocket.CancelOrderAsync(new()
		{
			Symbol = market.Symbol,
			OrderId = orderId,
			OrderIdType = idType,
		}, cancellationToken);
		await SendOrderAsync(order, cancelMsg.TransactionId,
			cancelMsg.TransactionId, null, market.Symbol, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"QFEX batch cancellation does not close positions.");
		if (cancelMsg.SecurityTypes is { Length: > 0 } &&
			!cancelMsg.SecurityTypes.Contains(SecurityTypes.Future))
			return;
		var symbol = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(cancelMsg.SecurityId).Symbol;
		if (symbol.IsEmpty() && cancelMsg.Side is null &&
			cancelMsg.IsStop is null)
		{
			await TradeSocket.CancelAllOrdersAsync(cancellationToken);
			return;
		}
		var orders = await TradeSocket.GetOpenOrdersAsync(symbol, HistoryLimit,
			cancellationToken);
		foreach (var order in orders.Where(order =>
			order?.OrderId.IsEmpty() == false &&
			(cancelMsg.Side is null ||
				order.Side.ToStockSharp() == cancelMsg.Side) &&
			(cancelMsg.IsStop is null ||
				IsConditional(order.OrderType) == cancelMsg.IsStop)))
		{
			var result = await TradeSocket.CancelOrderAsync(new()
			{
				Symbol = order.Symbol,
				OrderId = order.OrderId,
				OrderIdType = QFEXCancelOrderIdTypes.OrderId,
			}, cancellationToken);
			await SendOrderAsync(result, cancelMsg.TransactionId, null, null,
				order.Symbol, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsurePrivateReady();
		ValidatePortfolio(lookupMsg.PortfolioName);
		if (!lookupMsg.IsSubscribe)
		{
			_portfolioSubscriptionId = 0;
			await TradeSocket.UnsubscribeAsync(QFEXTradeChannels.Balances,
				cancellationToken);
			await TradeSocket.UnsubscribeAsync(QFEXTradeChannels.Positions,
				cancellationToken);
			return;
		}
		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = _portfolioName,
			BoardCode = BoardCodes.QFEX,
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
		try
		{
			await TradeSocket.SubscribeAsync(QFEXTradeChannels.Balances,
				cancellationToken);
			await TradeSocket.SubscribeAsync(QFEXTradeChannels.Positions,
				cancellationToken);
		}
		catch
		{
			_portfolioSubscriptionId = 0;
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId,
			cancellationToken);
		EnsurePrivateReady();
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

	private async ValueTask SendPortfolioSnapshotAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		var portfolio = await RestClient.GetPortfolioAsync(cancellationToken);
		await SendBalanceAsync(portfolio?.Balance, transactionId,
			cancellationToken);
		foreach (var position in portfolio?.Positions ?? [])
			await SendPositionAsync(position, transactionId, cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(
		OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		var symbols = GetOrderSymbols(statusMsg);
		var querySymbol = symbols.Count == 1 ? symbols.First() : null;
		var limit = (statusMsg.Count ?? HistoryLimit)
			.Min(HistoryLimit).Max(1).To<int>();
		var open = await TradeSocket.GetOpenOrdersAsync(querySymbol, limit,
			cancellationToken);
		var history = await RestClient.GetHistoricOrdersAsync(querySymbol,
			statusMsg.UserOrderId, statusMsg.From, statusMsg.To, limit,
			cancellationToken);
		var messages = open
			.Where(static order => order is not null)
			.Select(order => CreateOrderMessage(order,
				statusMsg.TransactionId, null, null, null))
			.Concat((history?.Data ?? [])
				.Where(static order => order is not null)
				.Select(order => CreateOrderMessage(order,
					statusMsg.TransactionId)))
			.Where(static message => message is not null)
			.Where(message => IsOrderMatch(message, statusMsg, symbols))
			.GroupBy(static message => message.OrderStringId,
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.OrderByDescending(
				message => message.ServerTime).First())
			.OrderBy(static message => message.ServerTime)
			.Skip(Math.Max(0, statusMsg.Skip ?? 0).To<int>())
			.Take(limit)
			.ToArray();
		foreach (var message in messages)
		{
			UpdateServerTime(message.ServerTime);
			await SendOutMessageAsync(message, cancellationToken);
		}

		var orderId = GetOrderId(statusMsg);
		var trades = await RestClient.GetUserTradesAsync(querySymbol, orderId,
			statusMsg.From, statusMsg.To, limit, cancellationToken);
		var filteredTrades = (trades?.Data ?? [])
			.Where(static trade => trade is not null)
			.Where(trade => IsTradeMatch(trade, statusMsg, symbols, orderId))
			.GroupBy(static trade => trade.TradeId,
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.First())
			.OrderBy(static trade => trade.Timestamp)
			.Skip(Math.Max(0, statusMsg.Skip ?? 0).To<int>())
			.Take(limit);
		foreach (var trade in filteredTrades)
			await SendUserTradeAsync(trade, statusMsg.TransactionId, false,
				cancellationToken);
	}

	private ValueTask OnOrderAsync(QFEXOrder order,
		CancellationToken cancellationToken)
	{
		var transactionId = GetTransactionId(order?.ClientOrderId);
		var originalTransactionId = _orderStatusSubscriptionId != 0
			? _orderStatusSubscriptionId
			: transactionId;
		return originalTransactionId == 0
			? default
			: SendOrderAsync(order, originalTransactionId, null, null, null,
				cancellationToken);
	}

	private ValueTask OnFillAsync(QFEXFill fill,
		CancellationToken cancellationToken)
	{
		var transactionId = GetTransactionId(fill?.ClientOrderId);
		var originalTransactionId = _orderStatusSubscriptionId != 0
			? _orderStatusSubscriptionId
			: transactionId;
		return originalTransactionId == 0
			? default
			: SendFillAsync(fill, originalTransactionId, true,
				cancellationToken);
	}

	private ValueTask OnBalanceAsync(QFEXBalance balance,
		CancellationToken cancellationToken)
		=> SendBalanceAsync(balance, _portfolioSubscriptionId,
			cancellationToken);

	private ValueTask OnPositionAsync(QFEXPosition position,
		CancellationToken cancellationToken)
		=> SendPositionAsync(position, _portfolioSubscriptionId,
			cancellationToken);

	private ValueTask SendBalanceAsync(QFEXBalance balance,
		long transactionId, CancellationToken cancellationToken)
	{
		if (balance is null || transactionId == 0)
			return default;
		var current = balance.Deposit + balance.RealizedProfitLoss +
			balance.UnrealizedProfitLoss + balance.NetFunding - balance.Fees;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = GetMarginAsset().ToCurrencySecurity(),
			DepoName = balance.Id,
			ClientCode = balance.UserId,
			ServerTime = ServerTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(PositionChangeTypes.BeginValue, balance.Deposit, true)
		.TryAdd(PositionChangeTypes.CurrentValue, current, true)
		.TryAdd(PositionChangeTypes.BlockedValue,
			balance.OrderMargin + balance.PositionMargin, true)
		.TryAdd(PositionChangeTypes.RealizedPnL,
			balance.RealizedProfitLoss, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL,
			balance.UnrealizedProfitLoss, true)
		.TryAdd(PositionChangeTypes.VariationMargin,
			balance.NetFunding, true)
		.TryAdd(PositionChangeTypes.Commission, balance.Fees, true),
			cancellationToken);
	}

	private ValueTask SendPositionAsync(QFEXPosition position,
		long transactionId, CancellationToken cancellationToken)
	{
		if (position?.Symbol.IsEmpty() != false || transactionId == 0)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = position.Symbol.ToStockSharp(),
			DepoName = position.Id,
			ServerTime = ServerTime,
			OriginalTransactionId = transactionId,
			Side = position.Position == 0
				? null
				: position.Position < 0 ? Sides.Sell : Sides.Buy,
		}
		.TryAdd(PositionChangeTypes.CurrentValue,
			position.Position.Abs(), true)
		.TryAdd(PositionChangeTypes.AveragePrice,
			position.AveragePrice, true)
		.TryAdd(PositionChangeTypes.RealizedPnL,
			position.RealizedProfitLoss, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL,
			position.UnrealizedProfitLoss, true)
		.TryAdd(PositionChangeTypes.VariationMargin,
			position.NetFunding, true)
		.TryAdd(PositionChangeTypes.Leverage, position.Leverage, true)
		.TryAdd(PositionChangeTypes.OrdersCount, position.OpenOrders, true)
		.TryAdd(PositionChangeTypes.OrdersMargin,
			position.InitialMargin, true), cancellationToken);
	}

	private async ValueTask SendOrderAsync(QFEXOrder order,
		long originalTransactionId, long? transactionId,
		QFEXOrderCondition condition, string fallbackSymbol,
		CancellationToken cancellationToken)
	{
		var message = CreateOrderMessage(order, originalTransactionId,
			transactionId, condition, fallbackSymbol);
		if (message is null)
			return;
		UpdateServerTime(message.ServerTime);
		await SendOutMessageAsync(message, cancellationToken);
	}

	private ExecutionMessage CreateOrderMessage(QFEXOrder order,
		long originalTransactionId, long? transactionId,
		QFEXOrderCondition condition, string fallbackSymbol)
	{
		if (order is null)
			return null;
		var symbol = order.Symbol.IsEmpty() ? fallbackSymbol : order.Symbol;
		if (symbol.IsEmpty() ||
			order.OrderId.IsEmpty() && order.ClientOrderId.IsEmpty())
			return null;
		var state = order.Status.ToStockSharp();
		condition ??= new QFEXOrderCondition
		{
			TakeProfitPrice = order.TakeProfit,
			StopLossPrice = order.StopLoss,
		};
		return new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(),
			ServerTime = order.UpdateTime > 0
				? order.UpdateTime.ToQFEXTime()
				: ServerTime,
			PortfolioName = _portfolioName,
			Side = order.Side.ToStockSharp(),
			OrderVolume = order.Quantity,
			Balance = order.QuantityRemaining.Max(0m),
			OrderPrice = order.Price,
			OrderType = order.OrderType.ToStockSharp(),
			OrderState = state,
			OrderStringId = order.OrderId,
			UserOrderId = order.ClientOrderId,
			TransactionId = transactionId ??
				GetTransactionId(order.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
			TimeInForce = order.TimeInForce.ToStockSharp(),
			PostOnly = order.OrderType ==
				QFEXOrderTypes.AddLiquidityOnly,
			PositionEffect = condition.IsReduceOnly
				? OrderPositionEffects.CloseOnly
				: null,
			Condition = condition,
			Error = state == OrderStates.Failed
				? new InvalidOperationException(
					"QFEX rejected the order with status '" +
					order.Status + "'.")
				: null,
		};
	}

	private ExecutionMessage CreateOrderMessage(QFEXHistoricOrder order,
		long originalTransactionId)
	{
		if (order?.OrderId.IsEmpty() != false || order.Symbol.IsEmpty())
			return null;
		var state = order.Status.ToStockSharp();
		return new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToStockSharp(),
			ServerTime = order.StatusTime.ToQFEXTime("order status"),
			PortfolioName = _portfolioName,
			Side = order.Side.ToStockSharp(),
			OrderVolume = order.Quantity,
			Balance = (order.Quantity - order.FilledQuantity).Max(0m),
			OrderPrice = order.Price,
			AveragePrice = order.AveragePrice,
			OrderType = order.OrderType.ToStockSharp(),
			OrderState = state,
			OrderStringId = order.OrderId,
			UserOrderId = order.ClientOrderId,
			TransactionId = GetTransactionId(order.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
			TimeInForce = order.TimeInForce.ToStockSharp(),
			PostOnly = order.OrderType ==
				QFEXOrderTypes.AddLiquidityOnly,
			Condition = new QFEXOrderCondition(),
			Error = state == OrderStates.Failed
				? new InvalidOperationException(
					"QFEX rejected the order with status '" +
					order.Status + "'.")
				: null,
		};
	}

	private ValueTask SendFillAsync(QFEXFill fill,
		long originalTransactionId, bool onlyNew,
		CancellationToken cancellationToken)
	{
		if (fill?.TradeId.IsEmpty() != false || fill.Symbol.IsEmpty())
			return default;
		var isNew = TryAcceptAccountTrade(fill.TradeId);
		if (onlyNew && !isNew)
			return default;
		var time = fill.Timestamp.ToQFEXTime();
		UpdateServerTime(time);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = fill.Symbol.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = fill.Side.ToStockSharp(),
			OrderStringId = fill.OrderId,
			UserOrderId = fill.ClientOrderId,
			TradeStringId = fill.TradeId,
			TradePrice = fill.Price,
			TradeVolume = fill.Quantity,
			Commission = fill.Fee,
			CommissionCurrency = GetMarketCurrency(fill.Symbol),
			PnL = fill.RealizedProfitLoss,
			TransactionId = GetTransactionId(fill.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private ValueTask SendUserTradeAsync(QFEXUserTrade trade,
		long originalTransactionId, bool onlyNew,
		CancellationToken cancellationToken)
	{
		if (trade?.TradeId.IsEmpty() != false || trade.Symbol.IsEmpty())
			return default;
		var isNew = TryAcceptAccountTrade(trade.TradeId);
		if (onlyNew && !isNew)
			return default;
		var time = trade.Timestamp.ToQFEXTime();
		UpdateServerTime(time);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = trade.Symbol.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = trade.Side.ToStockSharp(),
			OrderStringId = trade.OrderId,
			TradeStringId = trade.TradeId,
			TradePrice = trade.Price,
			TradeVolume = trade.Quantity,
			Commission = trade.Fee,
			CommissionCurrency = GetMarketCurrency(trade.Symbol),
			PnL = trade.RealizedProfitLoss,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private void ValidateOrder(QFEXReferenceDataSymbol market, decimal volume,
		decimal? price, decimal? takeProfit, decimal? stopLoss,
		QFEXOrderTypes orderType, QFEXTimeInForces? timeInForce)
	{
		if (market.Status != QFEXSymbolStatuses.Active)
			throw new InvalidOperationException(
				"QFEX market '" + market.Symbol + "' is not active.");
		var volumeStep = market.LotSize.ParseDecimal("lot size");
		if (volume <= 0 || !volume.IsMultipleOf(volumeStep))
			throw new InvalidOperationException(
				"QFEX order volume must be a positive multiple of " +
				volumeStep.ToString(CultureInfo.InvariantCulture) + ".");
		if (market.MinimumQuantity.TryParseDecimal() is decimal minimumVolume &&
			volume < minimumVolume)
			throw new InvalidOperationException(
				"QFEX order volume must be at least " +
				minimumVolume.ToString(CultureInfo.InvariantCulture) + ".");
		if (market.MaximumQuantity.TryParseDecimal() is decimal maximumVolume &&
			volume > maximumVolume)
			throw new InvalidOperationException(
				"QFEX order volume cannot exceed " +
				maximumVolume.ToString(CultureInfo.InvariantCulture) + ".");
		if (market.OrderTypes is { Length: > 0 } &&
			!market.OrderTypes.Contains(orderType))
			throw new NotSupportedException(
				"QFEX market '" + market.Symbol +
				"' does not support order type '" + orderType + "'.");
		if (timeInForce is QFEXTimeInForces tif &&
			market.TimeInForces is { Length: > 0 } &&
			!market.TimeInForces.Contains(tif))
			throw new NotSupportedException(
				"QFEX market '" + market.Symbol +
				"' does not support time in force '" + tif + "'.");
		ValidatePrice(market, price, "order price", price is not null);
		ValidatePrice(market, takeProfit, "take-profit price", false);
		ValidatePrice(market, stopLoss, "stop-loss price", false);
	}

	private static void ValidatePrice(QFEXReferenceDataSymbol market,
		decimal? value, string fieldName, bool isRequired)
	{
		if (value is null)
		{
			if (isRequired)
				throw new InvalidOperationException(
					"QFEX " + fieldName + " is required.");
			return;
		}
		var price = value.Value;
		var priceStep = market.TickSize.ParseDecimal("tick size");
		if (price <= 0 || !price.IsMultipleOf(priceStep))
			throw new InvalidOperationException(
				"QFEX " + fieldName +
				" must be a positive multiple of " +
				priceStep.ToString(CultureInfo.InvariantCulture) + ".");
		if (market.MinimumPrice.TryParseDecimal() is decimal minimum &&
			price < minimum)
			throw new InvalidOperationException(
				"QFEX " + fieldName + " must be at least " +
				minimum.ToString(CultureInfo.InvariantCulture) + ".");
		if (market.MaximumPrice.TryParseDecimal() is decimal maximum &&
			price > maximum)
			throw new InvalidOperationException(
				"QFEX " + fieldName + " cannot exceed " +
				maximum.ToString(CultureInfo.InvariantCulture) + ".");
	}

	private HashSet<string> GetOrderSymbols(OrderStatusMessage statusMsg)
	{
		var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (!statusMsg.SecurityId.SecurityCode.IsEmpty())
			symbols.Add(GetMarket(statusMsg.SecurityId).Symbol);
		foreach (var securityId in statusMsg.SecurityIds)
			if (!securityId.SecurityCode.IsEmpty())
				symbols.Add(GetMarket(securityId).Symbol);
		return symbols;
	}

	private static bool IsOrderMatch(ExecutionMessage order,
		OrderStatusMessage filter, HashSet<string> symbols)
	{
		if (symbols.Count > 0 &&
			!symbols.Contains(order.SecurityId.SecurityCode))
			return false;
		var orderId = GetOrderId(filter);
		if (!orderId.IsEmpty() &&
			!order.OrderStringId.EqualsIgnoreCase(orderId))
			return false;
		if (!filter.UserOrderId.IsEmpty() &&
			!order.UserOrderId.EqualsIgnoreCase(filter.UserOrderId))
			return false;
		if (filter.Side is Sides side && order.Side != side)
			return false;
		if (filter.Volume is decimal volume &&
			order.OrderVolume != volume)
			return false;
		if (filter.States.Length > 0 &&
			(order.OrderState is not OrderStates state ||
				!filter.States.Contains(state)))
			return false;
		if (filter.From is DateTime from &&
			order.ServerTime < from.EnsureUtc())
			return false;
		if (filter.To is DateTime to &&
			order.ServerTime > to.EnsureUtc())
			return false;
		return true;
	}

	private static bool IsTradeMatch(QFEXUserTrade trade,
		OrderStatusMessage filter, HashSet<string> symbols, string orderId)
	{
		if (symbols.Count > 0 && !symbols.Contains(trade.Symbol))
			return false;
		if (!orderId.IsEmpty() &&
			!trade.OrderId.EqualsIgnoreCase(orderId))
			return false;
		if (filter.Side is Sides side &&
			trade.Side.ToStockSharp() != side)
			return false;
		var time = trade.Timestamp.ToQFEXTime();
		if (filter.From is DateTime from && time < from.EnsureUtc())
			return false;
		if (filter.To is DateTime to && time > to.EnsureUtc())
			return false;
		return true;
	}

	private void ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(_portfolioName))
			throw new InvalidOperationException(
				"Unknown QFEX portfolio '" + portfolioName + "'.");
	}

	private string GetMarginAsset()
		=> GetMarkets().Select(static market => market.MarginAsset)
			.FirstOrDefault(static asset => !asset.IsEmpty()) ?? "USD";

	private string GetMarketCurrency(string symbol)
		=> TryGetMarket(symbol, out var market)
			? market.MarginAsset.IsEmpty()
				? market.QuoteAsset
				: market.MarginAsset
			: GetMarginAsset();

	private static string ResolveOrderId(long? numericOrderId,
		string stringOrderId, string operation)
	{
		if (!stringOrderId.IsEmpty())
			return stringOrderId.Trim();
		if (numericOrderId is > 0)
			return numericOrderId.Value.ToString(CultureInfo.InvariantCulture);
		throw new InvalidOperationException(
			"QFEX " + operation + " requires an exchange order ID.");
	}

	private static string GetOrderId(OrderStatusMessage message)
	{
		if (!message.OrderStringId.IsEmpty())
			return message.OrderStringId.Trim();
		return message.OrderId is > 0
			? message.OrderId.Value.ToString(CultureInfo.InvariantCulture)
			: null;
	}

	private static bool IsConditional(QFEXOrderTypes orderType)
		=> orderType is QFEXOrderTypes.TakeProfit or
			QFEXOrderTypes.StopLoss or QFEXOrderTypes.StopMarket;
}
