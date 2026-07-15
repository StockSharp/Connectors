namespace StockSharp.Moomoo;

partial class MoomooMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage message, CancellationToken cancellationToken)
	{
		var account = ResolveAccount(message.PortfolioName);
		var condition = message.Condition as MoomooOrderCondition;
		var stopPrice = condition?.StopPrice;
		var orderId = await _client.PlaceOrder(
			account,
			message.Side.ToNative(message.PositionEffect),
			message.OrderType.ToNative(stopPrice),
			message.SecurityId.SecurityCode,
			message.Volume,
			message.Price,
			stopPrice,
			ToNativeTimeInForce(message.TimeInForce, message.TillDate),
			message.TillDate,
			(condition?.Session ?? MoomooSessions.Regular).ToNative(),
			message.TransactionId.ToString(CultureInfo.InvariantCulture),
			cancellationToken);

		_orderTransactions[orderId] = message.TransactionId;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = message.TransactionId,
			OrderStringId = orderId,
			PortfolioName = GetPortfolioName(account),
			SecurityId = message.SecurityId,
			Side = message.Side,
			OrderType = message.OrderType,
			OrderPrice = message.Price,
			OrderVolume = message.Volume,
			Balance = message.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = DateTime.UtcNow,
			Condition = condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage message, CancellationToken cancellationToken)
	{
		var orderId = message.OldOrderStringId ?? throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(message.OriginalTransactionId));
		var stopPrice = (message.Condition as MoomooOrderCondition)?.StopPrice;
		var newOrderId = await _client.ModifyOrder(
			ResolveAccount(message.PortfolioName),
			orderId,
			TrdCommon.ModifyOrderOp.ModifyOrderOp_Normal,
			message.Volume,
			message.Price,
			stopPrice,
			cancellationToken);
		_orderTransactions[newOrderId] = message.TransactionId;
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage message, CancellationToken cancellationToken)
	{
		var orderId = message.OrderStringId ?? throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(message.OriginalTransactionId));
		await _client.ModifyOrder(
			ResolveAccount(message.PortfolioName),
			orderId,
			TrdCommon.ModifyOrderOp.ModifyOrderOp_Cancel,
			0,
			0,
			null,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
			return;

		foreach (var account in ResolveAccounts(message.PortfolioName))
		{
			var funds = await _client.GetFunds(account, cancellationToken);
			await SendOutMessageAsync(new PortfolioMessage
			{
				OriginalTransactionId = message.TransactionId,
				PortfolioName = GetPortfolioName(account),
				BoardCode = BoardCodes.Nasdaq,
				Currency = funds?.HasCurrency == true ? ((TrdCommon.Currency)funds.Currency).ToCurrency() ?? CurrencyTypes.USD : CurrencyTypes.USD,
			}, cancellationToken);

			if (funds is not null)
				await ProcessFunds(account, funds, message.TransactionId, cancellationToken);
			foreach (var position in await _client.GetPositions(account, cancellationToken))
				await ProcessPosition(account, position, message.TransactionId, cancellationToken);
		}

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
			return;

		foreach (var account in ResolveAccounts(message.PortfolioName))
		{
			if (message.From is not null || message.To is not null || message.IsHistoryOnly())
			{
				foreach (var order in await _client.GetOrders(account, true, message.From, message.To, cancellationToken))
					await ProcessOrder(account, order, message.TransactionId, cancellationToken);
				foreach (var fill in await _client.GetFills(account, true, message.From, message.To, cancellationToken))
					await ProcessFill(account, fill, message.TransactionId, cancellationToken);
			}

			if (!message.IsHistoryOnly())
			{
				foreach (var order in await _client.GetOrders(account, false, null, null, cancellationToken))
					await ProcessOrder(account, order, message.TransactionId, cancellationToken);
				foreach (var fill in await _client.GetFills(account, false, null, null, cancellationToken))
					await ProcessFill(account, fill, message.TransactionId, cancellationToken);
			}
		}

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private ValueTask ProcessOrderPush(TrdUpdateOrder.Response response, CancellationToken cancellationToken)
	{
		if (response.RetType != 0)
			return SendOutErrorAsync(new InvalidOperationException(response.RetMsg.IsEmpty($"Moomoo order push failed with code {response.RetType}.")), cancellationToken);
		if (!response.HasS2C)
			return default;
		var account = ResolveAccount(response.S2C.Header.AccID);
		return ProcessOrder(account, response.S2C.Order, 0, cancellationToken);
	}

	private ValueTask ProcessFillPush(TrdUpdateOrderFill.Response response, CancellationToken cancellationToken)
	{
		if (response.RetType != 0)
			return SendOutErrorAsync(new InvalidOperationException(response.RetMsg.IsEmpty($"Moomoo fill push failed with code {response.RetType}.")), cancellationToken);
		if (!response.HasS2C)
			return default;
		var account = ResolveAccount(response.S2C.Header.AccID);
		return ProcessFill(account, response.S2C.OrderFill, 0, cancellationToken);
	}

	private ValueTask ProcessFunds(TrdCommon.TrdAcc account, TrdCommon.Funds funds, long originalTransactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = GetPortfolioName(account),
			SecurityId = SecurityId.Money,
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, (decimal)funds.Cash, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, (decimal)funds.TotalAssets, true)
		.TryAdd(PositionChangeTypes.BuyOrdersMargin, (decimal)funds.Power, true)
		.TryAdd(PositionChangeTypes.BlockedValue, (decimal)funds.FrozenCash, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, funds.HasUnrealizedPL ? (decimal)funds.UnrealizedPL : null)
		.TryAdd(PositionChangeTypes.RealizedPnL, funds.HasRealizedPL ? (decimal)funds.RealizedPL : null), cancellationToken);

	private ValueTask ProcessPosition(TrdCommon.TrdAcc account, TrdCommon.Position position, long originalTransactionId, CancellationToken cancellationToken)
	{
		var quantity = (decimal)position.Qty;
		if ((TrdCommon.PositionSide)position.PositionSide == TrdCommon.PositionSide.PositionSide_Short)
			quantity = -quantity;

		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = GetPortfolioName(account),
			SecurityId = new() { SecurityCode = position.Code, BoardCode = BoardCodes.Nasdaq },
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, quantity, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, (decimal)position.Price, true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.HasAverageCostPrice ? (decimal)position.AverageCostPrice : position.HasCostPrice ? (decimal)position.CostPrice : null)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.HasUnrealizedPL ? (decimal)position.UnrealizedPL : (decimal)position.PlVal)
		.TryAdd(PositionChangeTypes.RealizedPnL, position.HasRealizedPL ? (decimal)position.RealizedPL : null), cancellationToken);
	}

	private async ValueTask ProcessOrder(TrdCommon.TrdAcc account, TrdCommon.Order order, long originalTransactionId, CancellationToken cancellationToken)
	{
		var orderId = GetOrderId(order);
		var transactionId = _orderTransactions.TryGetValue2(orderId) ?? ParseTransactionId(order.Remark);
		if (transactionId != 0)
			_orderTransactions[orderId] = transactionId;
		var state = ((TrdCommon.OrderStatus)order.OrderStatus).ToOrderState();

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId == 0 ? transactionId : originalTransactionId,
			TransactionId = originalTransactionId == 0 ? 0 : transactionId,
			OrderStringId = orderId,
			PortfolioName = GetPortfolioName(account),
			SecurityId = new() { SecurityCode = order.Code, BoardCode = BoardCodes.Nasdaq },
			Side = ((TrdCommon.TrdSide)order.TrdSide).ToSide(),
			OrderType = ((TrdCommon.OrderType)order.OrderType).ToOrderType(),
			OrderPrice = order.HasPrice ? (decimal)order.Price : 0,
			OrderVolume = (decimal)order.Qty,
			Balance = ((decimal)order.Qty - (order.HasFillQty ? (decimal)order.FillQty : 0)).Max(0),
			OrderState = state,
			ServerTime = GetServerTime(order.UpdateTimestamp, order.HasUpdateTimestamp),
			TimeInForce = order.HasTimeInForce ? ToTimeInForce((TrdCommon.TimeInForce)order.TimeInForce) : null,
			ExpiryDate = order.HasExpireTime ? ParseOpenDTime(order.ExpireTime) : null,
			Condition = order.HasAuxPrice || order.HasSession ? new MoomooOrderCondition
			{
				StopPrice = (decimal)order.AuxPrice,
				Session = ToSession((Common.Session)order.Session),
			} : null,
			Error = state == OrderStates.Failed ? new InvalidOperationException(order.LastErrMsg.IsEmpty(order.OrderStatus.ToString(CultureInfo.InvariantCulture))) : null,
		}, cancellationToken);
	}

	private ValueTask ProcessFill(TrdCommon.TrdAcc account, TrdCommon.OrderFill fill, long originalTransactionId, CancellationToken cancellationToken)
	{
		var fillId = fill.FillIDEx.IsEmpty(fill.FillID.ToString(CultureInfo.InvariantCulture));
		if (_processedFills.Contains(fillId))
			return default;
		_processedFills.Add(fillId);

		var orderId = fill.OrderIDEx.IsEmpty(fill.OrderID.ToString(CultureInfo.InvariantCulture));
		var transactionId = _orderTransactions.TryGetValue2(orderId) ?? 0;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originalTransactionId == 0 ? transactionId : originalTransactionId,
			OrderStringId = orderId,
			TradeStringId = fillId,
			PortfolioName = GetPortfolioName(account),
			SecurityId = new() { SecurityCode = fill.Code, BoardCode = BoardCodes.Nasdaq },
			Side = ((TrdCommon.TrdSide)fill.TrdSide).ToSide(),
			TradePrice = (decimal)fill.Price,
			TradeVolume = (decimal)fill.Qty,
			ServerTime = GetServerTime(fill.CreateTimestamp, fill.HasCreateTimestamp),
		}, cancellationToken);
	}

	private TrdCommon.TrdAcc ResolveAccount(string portfolioName)
	{
		if (!portfolioName.IsEmpty())
			return _accounts.FirstOrDefault(a => GetPortfolioName(a).EqualsIgnoreCase(portfolioName) || a.CardNum.EqualsIgnoreCase(portfolioName))
				?? throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
		return _accounts.FirstOrDefault() ?? throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
	}

	private TrdCommon.TrdAcc ResolveAccount(ulong accountId)
		=> _accounts.FirstOrDefault(a => a.AccID == accountId) ?? throw new InvalidOperationException(LocalizedStrings.AccountNotFound);

	private IEnumerable<TrdCommon.TrdAcc> ResolveAccounts(string portfolioName)
		=> portfolioName.IsEmpty() ? _accounts : [ResolveAccount(portfolioName)];

	private static string GetPortfolioName(TrdCommon.TrdAcc account)
		=> account.AccID.ToString(CultureInfo.InvariantCulture);

	private static string GetOrderId(TrdCommon.Order order)
		=> order.OrderIDEx.IsEmpty(order.OrderID.ToString(CultureInfo.InvariantCulture));

	private static long ParseTransactionId(string value)
		=> long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var transactionId) ? transactionId : 0;

	private static TrdCommon.TimeInForce ToNativeTimeInForce(TimeInForce? timeInForce, DateTime? expiry)
		=> expiry is not null ? TrdCommon.TimeInForce.TimeInForce_GTD : timeInForce switch
		{
			TimeInForce.PutInQueue => TrdCommon.TimeInForce.TimeInForce_GTC,
			TimeInForce.CancelBalance => TrdCommon.TimeInForce.TimeInForce_IOC,
			TimeInForce.MatchOrCancel => throw new NotSupportedException(LocalizedStrings.NotSupported.Put(timeInForce)),
			_ => TrdCommon.TimeInForce.TimeInForce_DAY,
		};

	private static TimeInForce ToTimeInForce(TrdCommon.TimeInForce timeInForce)
		=> timeInForce switch
		{
			TrdCommon.TimeInForce.TimeInForce_IOC => TimeInForce.CancelBalance,
			_ => TimeInForce.PutInQueue,
		};

	private static MoomooSessions ToSession(Common.Session session)
		=> session switch
		{
			Common.Session.Session_RTH => MoomooSessions.Regular,
			Common.Session.Session_ETH => MoomooSessions.Extended,
			Common.Session.Session_OVERNIGHT => MoomooSessions.Overnight,
			_ => MoomooSessions.All,
		};

	private static DateTime? ParseOpenDTime(string value)
		=> DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var result) ? result : null;
}
