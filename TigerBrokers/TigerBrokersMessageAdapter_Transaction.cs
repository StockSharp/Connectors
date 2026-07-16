namespace StockSharp.TigerBrokers;

public partial class TigerBrokersMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var instrument = ResolveInstrument(regMsg.SecurityId);
		var condition = regMsg.Condition as TigerBrokersOrderCondition;
		var (quantity, scale) = regMsg.Volume.ToScaledQuantity();
		var orderType = (regMsg.OrderType ?? OrderTypes.Limit).ToNative(condition);
		var model = new PlaceOrderModel
		{
			Account = regMsg.PortfolioName.IsEmpty(Account),
			SecType = instrument.SecurityType,
			Market = instrument.Market,
			Currency = instrument.Currency.ToTigerCurrency(),
			Symbol = instrument.Symbol,
			Right = instrument.Right,
			Strike = instrument.Strike?.ToString(CultureInfo.InvariantCulture),
			Expiry = instrument.ExpiryDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
			Action = regMsg.Side.ToNative(),
			OrderType = orderType,
			TotalQuantity = quantity,
			TotalQuantityScale = scale,
			LimitPrice = orderType is TigerOrderType.LMT or TigerOrderType.STP_LMT ? (double)regMsg.Price : null,
			AuxPrice = orderType is TigerOrderType.STP or TigerOrderType.STP_LMT ? (double?)condition?.StopPrice : null,
			TrailingPercent = orderType == TigerOrderType.TRAIL ? (double?)condition?.TrailingPercent : null,
			TimeInForce = regMsg.TimeInForce.ToNative(regMsg.TillDate),
			ExpireTime = regMsg.TillDate.ToUnixMilliseconds(),
			OutsideRth = condition?.OutsideRegularTradingHours == true,
			TradingSessionType = (condition?.Session ?? TigerSessions.Regular).ToNative(),
			Exchange = instrument.Exchange,
			Multiplier = (double)(instrument.Multiplier ?? 0),
			UserMark = condition?.UserMark.IsEmpty() == false
				? condition.UserMark
				: regMsg.TransactionId.ToString(CultureInfo.InvariantCulture),
		};
		var response = await _client.PlaceOrder(model, cancellationToken);
		var orderId = response.Data?.Id ?? response.Data?.Orders?.FirstOrDefault()?.Id ?? 0;
		if (orderId <= 0)
			throw new InvalidOperationException("Tiger OpenAPI place order did not return an order identifier.");
		_orderTransactions[orderId] = regMsg.TransactionId;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			OrderId = orderId,
			SecurityId = regMsg.SecurityId,
			PortfolioName = model.Account,
			Side = regMsg.Side,
			OrderType = regMsg.OrderType,
			OrderPrice = regMsg.Price,
			OrderVolume = regMsg.Volume,
			Balance = regMsg.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = DateTime.UtcNow,
			TimeInForce = regMsg.TimeInForce,
			ExpiryDate = regMsg.TillDate,
			Condition = condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		var orderId = replaceMsg.OldOrderId ?? 0;
		if (orderId <= 0 && !long.TryParse(replaceMsg.OldOrderStringId, NumberStyles.Integer, CultureInfo.InvariantCulture, out orderId))
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(replaceMsg.OriginalTransactionId));
		var (quantity, scale) = replaceMsg.Volume.ToScaledQuantity();
		var condition = replaceMsg.Condition as TigerBrokersOrderCondition;
		await _client.ModifyOrder(new ModifyOrderModel
		{
			Account = replaceMsg.PortfolioName.IsEmpty(Account),
			Id = orderId,
			TotalQuantity = quantity,
			TotalQuantityScale = scale,
			LimitPrice = replaceMsg.OrderType == OrderTypes.Market ? null : (double)replaceMsg.Price,
			AuxPrice = (double?)condition?.StopPrice,
			TrailingPercent = (double?)condition?.TrailingPercent,
		}, cancellationToken);
		_orderTransactions[orderId] = replaceMsg.TransactionId;
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var orderId = cancelMsg.OrderId ?? 0;
		if (orderId <= 0 && !long.TryParse(cancelMsg.OrderStringId, NumberStyles.Integer, CultureInfo.InvariantCulture, out orderId))
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));
		await _client.CancelOrder(new CancelOrderModel
		{
			Account = cancelMsg.PortfolioName.IsEmpty(Account),
			Id = orderId,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		if (!statusMsg.IsSubscribe)
		{
			_orderStatusSubscriptionId = 0;
			return;
		}

		foreach (var order in await _client.GetOrders(statusMsg.PortfolioName.IsEmpty(Account), statusMsg.From, statusMsg.To, cancellationToken))
			await ProcessOrder(order, statusMsg.TransactionId, true, cancellationToken);
		foreach (var transaction in await _client.GetTransactions(statusMsg.PortfolioName.IsEmpty(Account), statusMsg.From, statusMsg.To, cancellationToken))
			await ProcessTransaction(transaction, statusMsg.TransactionId, cancellationToken);

		if (!statusMsg.IsHistoryOnly())
			_orderStatusSubscriptionId = statusMsg.TransactionId;
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		if (!lookupMsg.IsSubscribe)
		{
			_portfolioSubscriptionId = 0;
			return;
		}

		var account = lookupMsg.PortfolioName.IsEmpty(Account);
		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = lookupMsg.TransactionId,
			PortfolioName = account,
			BoardCode = "TIGER_US",
		}, cancellationToken);
		await SendPortfolio(account, lookupMsg.TransactionId, cancellationToken);
		if (!lookupMsg.IsHistoryOnly())
			_portfolioSubscriptionId = lookupMsg.TransactionId;
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private async ValueTask SendPortfolio(string account, long originalTransactionId, CancellationToken cancellationToken)
	{
		var assets = await _client.GetAssets(account, cancellationToken);
		foreach (var segment in assets.Data?.Segments ?? [])
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = account,
				SecurityId = SecurityId.Money,
				ServerTime = assets.Data.UpdateTimestamp.FromUnixMilliseconds(),
			}
			.TryAdd(PositionChangeTypes.CurrentValue, (decimal)segment.NetLiquidation, true)
			.TryAdd(PositionChangeTypes.BeginValue, (decimal)segment.CashBalance, true)
			.TryAdd(PositionChangeTypes.BlockedValue, (decimal)segment.MaintainMargin, true)
			.TryAdd(PositionChangeTypes.BuyOrdersMargin, (decimal)segment.BuyingPower, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, (decimal)segment.UnrealizedPL, true)
			.TryAdd(PositionChangeTypes.RealizedPnL, (decimal)segment.RealizedPL, true), cancellationToken);
		}

		foreach (var position in await _client.GetPositions(account, cancellationToken))
			await ProcessPosition(position, originalTransactionId, cancellationToken);
	}

	private async ValueTask ProcessOrder(TradeOrder order, long originId, bool isLookup, CancellationToken cancellationToken)
	{
		if (order == null || order.Id <= 0)
			return;
		var transactionId = ParseTransactionId(order.UserMark, order.Id);
		var state = order.Status.ToString().ToOrderState();
		var volume = order.TotalQuantity.FromScaledQuantity(order.TotalQuantityScale);
		var filled = order.FilledQuantity.FromScaledQuantity(order.FilledQuantityScale);
		var instrument = CreateInstrument(order.Symbol, order.Identifier, order.Market, order.SecType, order.Currency,
			order.Expiry, order.Strike, order.Right, order.Multiplier);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = isLookup ? originId : transactionId == 0 ? _orderStatusSubscriptionId : transactionId,
			TransactionId = isLookup ? transactionId : 0,
			OrderId = order.Id,
			SecurityId = instrument.ToSecurityId(),
			PortfolioName = order.Account,
			Side = order.Action.ToSide(),
			OrderType = order.OrderType.ToOrderType(),
			OrderPrice = (decimal)order.LimitPrice,
			OrderVolume = volume,
			Balance = Math.Max(0, volume - filled),
			AveragePrice = (decimal)order.AvgFillPrice,
			OrderState = state,
			ServerTime = (order.UpdateTime > 0 ? order.UpdateTime : order.OpenTime).FromUnixMilliseconds(),
			TimeInForce = order.TimeInForce.ToTimeInForce(),
			ExpiryDate = order.ExpireTime > 0 ? order.ExpireTime.FromUnixMilliseconds() : null,
			Condition = new TigerBrokersOrderCondition
			{
				StopPrice = order.AuxPrice > 0 ? (decimal)order.AuxPrice : null,
				TrailingPercent = order.TrailingPercent > 0 ? (decimal)order.TrailingPercent : null,
				OutsideRegularTradingHours = order.OutsideRth,
				Session = order.TradingSessionType.ToSession(),
				UserMark = order.UserMark,
			},
			Error = state == OrderStates.Failed ? new InvalidOperationException(order.Remark.IsEmpty(order.AttrDesc)) : null,
		}, cancellationToken);
	}

	private ValueTask ProcessTransaction(OrderTransactions transaction, long originId, CancellationToken cancellationToken)
	{
		if (transaction == null || transaction.Id <= 0 || _trades.Contains(transaction.Id))
			return default;

		_trades.Add(transaction.Id);
		var instrument = CreateInstrument(transaction.Symbol, null, transaction.Market, transaction.SecType,
			transaction.Currency, transaction.Expiry, transaction.Strike, transaction.Right, 0);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originId,
			OrderId = transaction.OrderId,
			TradeId = transaction.Id,
			SecurityId = instrument.ToSecurityId(),
			PortfolioName = transaction.AccountId,
			Side = transaction.Action.ToSide(),
			TradePrice = (decimal)transaction.FilledPrice,
			TradeVolume = transaction.FilledQuantity,
			ServerTime = transaction.TransactionTime.FromUnixMilliseconds(),
		}, cancellationToken);
	}

	private async ValueTask OnOrderReceived(OrderStatusData order, CancellationToken cancellationToken)
	{
		if (order == null || order.Id <= 0)
			return;
		var transactionId = ParseTransactionId(order.UserMark, order.Id);
		var originId = transactionId == 0 ? _orderStatusSubscriptionId : transactionId;
		if (originId == 0)
			return;
		var volume = order.TotalQuantity.FromScaledQuantity(order.TotalQuantityScale);
		var filled = order.FilledQuantity.FromScaledQuantity(order.FilledQuantityScale);
		var instrument = CreateInstrument(order.Symbol, order.Identifier, order.Market, order.SecType,
			order.Currency, order.Expiry, order.Strike, order.Right, order.Multiplier);
		var state = order.Status.ToOrderState();

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originId,
			OrderId = order.Id,
			SecurityId = instrument.ToSecurityId(),
			PortfolioName = order.Account,
			Side = order.Action.ToSide(),
			OrderType = order.OrderType.ToOrderType(),
			OrderPrice = (decimal)order.LimitPrice,
			OrderVolume = volume,
			Balance = Math.Max(0, volume - filled),
			AveragePrice = (decimal)order.AvgFillPrice,
			OrderState = state,
			ServerTime = ((long)order.Timestamp).FromUnixMilliseconds(),
			Condition = new TigerBrokersOrderCondition
			{
				StopPrice = order.StopPrice > 0 ? (decimal)order.StopPrice : null,
				OutsideRegularTradingHours = order.OutsideRth,
				UserMark = order.UserMark,
			},
			Error = state == OrderStates.Failed ? new InvalidOperationException(order.ErrorMsg) : null,
		}, cancellationToken);
	}

	private ValueTask OnOrderTransactionReceived(OrderTransactionData transaction, CancellationToken cancellationToken)
	{
		if (transaction == null || transaction.Id <= 0 || _trades.Contains(transaction.Id))
			return default;

		_trades.Add(transaction.Id);
		_orderTransactions.TryGetValue(transaction.OrderId, out var transactionId);
		var originId = transactionId == 0 ? _orderStatusSubscriptionId : transactionId;
		if (originId == 0)
			return default;
		var instrument = CreateInstrument(transaction.Symbol, transaction.Identifier, transaction.Market,
			transaction.SecType, transaction.Currency, null, null, null, transaction.Multiplier);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originId,
			OrderId = transaction.OrderId,
			TradeId = transaction.Id,
			SecurityId = instrument.ToSecurityId(),
			PortfolioName = transaction.Account,
			Side = transaction.Action.ToSide(),
			TradePrice = (decimal)transaction.FilledPrice,
			TradeVolume = transaction.FilledQuantity,
			ServerTime = ((long)transaction.TransactTime).FromUnixMilliseconds(),
		}, cancellationToken);
	}

	private ValueTask OnPositionReceived(PositionData position, CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId == 0)
			return default;
		var instrument = CreateInstrument(position.Symbol, position.Identifier, position.Market, position.SecType,
			position.Currency, position.Expiry, position.Strike, position.Right, position.Multiplier);
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = _portfolioSubscriptionId,
			PortfolioName = position.Account,
			SecurityId = instrument.ToSecurityId(),
			ServerTime = ((long)position.Timestamp).FromUnixMilliseconds(),
		}
		.TryAdd(PositionChangeTypes.CurrentValue, position.Position.FromScaledQuantity(position.PositionScale), true)
		.TryAdd(PositionChangeTypes.AveragePrice, (decimal)position.AverageCost, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, (decimal)position.LatestPrice, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, (decimal)position.UnrealizedPnl, true), cancellationToken);
	}

	private ValueTask OnAssetReceived(AssetData asset, CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId == 0)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = _portfolioSubscriptionId,
			PortfolioName = asset.Account,
			SecurityId = SecurityId.Money,
			ServerTime = ((long)asset.Timestamp).FromUnixMilliseconds(),
		}
		.TryAdd(PositionChangeTypes.BeginValue, (decimal)asset.CashBalance, true)
		.TryAdd(PositionChangeTypes.CurrentValue, (decimal)asset.NetLiquidation, true)
		.TryAdd(PositionChangeTypes.BlockedValue, (decimal)asset.MaintMarginReq, true)
		.TryAdd(PositionChangeTypes.BuyOrdersMargin, (decimal)asset.BuyingPower, true), cancellationToken);
	}

	private ValueTask ProcessPosition(PositionDetail position, long originId, CancellationToken cancellationToken)
	{
		if (position == null)
			return default;
		var instrument = CreateInstrument(position.Symbol, position.Identifier, position.Market, position.SecType,
			position.Currency, position.Expiry, double.IsNaN(position.Strike) ? null : position.Strike.ToString(CultureInfo.InvariantCulture),
			position.Right, position.Multiplier);
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originId,
			PortfolioName = position.Account,
			SecurityId = instrument.ToSecurityId(),
			ServerTime = position.UpdateTimestamp.FromUnixMilliseconds(),
		}
		.TryAdd(PositionChangeTypes.CurrentValue, (decimal)position.PositionQty, true)
		.TryAdd(PositionChangeTypes.AveragePrice, (decimal)position.AverageCost, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, double.IsNaN(position.LatestPrice) ? null : (decimal)position.LatestPrice, true)
		.TryAdd(PositionChangeTypes.RealizedPnL, double.IsNaN(position.RealizedPnl) ? null : (decimal)position.RealizedPnl, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, double.IsNaN(position.UnrealizedPnl) ? null : (decimal)position.UnrealizedPnl, true), cancellationToken);
	}

	private long ParseTransactionId(string userMark, long orderId)
	{
		if (long.TryParse(userMark, NumberStyles.Integer, CultureInfo.InvariantCulture, out var transactionId))
			_orderTransactions[orderId] = transactionId;
		else
			_orderTransactions.TryGetValue(orderId, out transactionId);
		return transactionId;
	}

	private TigerInstrument CreateInstrument(string symbol, string identifier, string market, string securityType,
		string currency, string expiry, string strike, string right, double multiplier)
	{
		var type = Enum.TryParse<SecType>(securityType, true, out var parsedType) ? parsedType : SecType.STK;
		var parsedMarket = Enum.TryParse<Market>(market, true, out var marketValue) ? marketValue : Market.US;
		var parsedStrike = decimal.TryParse(strike, NumberStyles.Any, CultureInfo.InvariantCulture, out var strikeValue)
			? strikeValue : (decimal?)null;
		var subscriptionSymbol = identifier.IsEmpty(symbol);
		var instrument = new TigerInstrument
		{
			Symbol = symbol,
			SubscriptionSymbol = subscriptionSymbol,
			Market = parsedMarket,
			SecurityType = type,
			Currency = currency,
			ExpiryDate = expiry.ParseTigerDate(),
			Strike = parsedStrike,
			Right = right,
			Multiplier = multiplier > 0 ? (decimal)multiplier : null,
		};
		_instruments[subscriptionSymbol] = instrument;
		return instrument;
	}
}
