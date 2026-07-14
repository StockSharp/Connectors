namespace StockSharp.Alor;

public partial class AlorMessageAdapter
{
	private readonly SynchronizedDictionary<long, (SecurityId secId, string portfolioName, OrderTypes? orderType, string side, decimal price, string exchange)> _orderInfo = [];
	private readonly CachedSynchronizedSet<string> _pfNames = [];
	private readonly SynchronizedSet<long> _orderStatusIds = [];
	private readonly SynchronizedDictionary<long, string> _pfNameIds = [];

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var secId = regMsg.SecurityId;
		var sec = await EnsureGetSecurity(secId, cancellationToken);
		var side = regMsg.Side.ToNative();
		_orderInfo.Add(regMsg.TransactionId, (secId, regMsg.PortfolioName, regMsg.OrderType, side, regMsg.Price, sec.Exchange));

		if (regMsg.OrderType == OrderTypes.Conditional)
		{
			await _orderSocketClient.CreateStopOrder(
				regMsg.TransactionId,
				true,
				secId.SecurityCode,
				sec.Exchange,
				secId.BoardCode,
				regMsg.PortfolioName,
				regMsg.Side.ToNative(),
				regMsg.Volume,
				regMsg.TransactionId.To<string>(),
				regMsg.TimeInForce.ToNative(regMsg.ExpiryDate),
				regMsg.Price.DefaultAsNull(),
				regMsg.VisibleVolume,
				default,
				regMsg.GetCondition(out var condition),
				condition.TriggerPrice,
				(long?)regMsg.ExpiryDate?.ToUnix(),
				cancellationToken);
		}
		else
		{
			await _orderSocketClient.CreateLimitOrder(
				regMsg.TransactionId,
				true,
				secId.SecurityCode,
				sec.Exchange,
				secId.BoardCode,
				regMsg.PortfolioName,
				regMsg.Side.ToNative(),
				regMsg.Volume,
				regMsg.TransactionId.To<string>(),
				regMsg.TimeInForce.ToNative(regMsg.ExpiryDate),
				regMsg.Price.DefaultAsNull(),
				regMsg.VisibleVolume,
				default,
				cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		if (replaceMsg.OldOrderId is null)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(replaceMsg.OriginalTransactionId));

		var (secId, _, type, _, _, exchange) = _orderInfo[replaceMsg.OriginalTransactionId];

		if (type == OrderTypes.Conditional)
		{
			return _orderSocketClient.CreateStopOrder(
				replaceMsg.TransactionId,
				false,
				secId.SecurityCode,
				exchange,
				secId.BoardCode,
				replaceMsg.PortfolioName,
				replaceMsg.Side.ToNative(),
				replaceMsg.Volume,
				replaceMsg.TransactionId.To<string>(),
				replaceMsg.TimeInForce.ToNative(replaceMsg.ExpiryDate),
				replaceMsg.Price.DefaultAsNull(),
				replaceMsg.VisibleVolume,
				default,
				replaceMsg.GetCondition(out var condition),
				condition.TriggerPrice,
				(long?)replaceMsg.ExpiryDate?.ToUnix(),
				cancellationToken);
		}
		else
		{
			return _orderSocketClient.CreateLimitOrder(
				replaceMsg.TransactionId,
				false,
				secId.SecurityCode,
				exchange,
				secId.BoardCode,
				replaceMsg.PortfolioName,
				replaceMsg.Side.ToNative(),
				replaceMsg.Volume,
				replaceMsg.TransactionId.To<string>(),
				replaceMsg.TimeInForce.ToNative(replaceMsg.ExpiryDate),
				replaceMsg.Price.DefaultAsNull(),
				replaceMsg.VisibleVolume,
				default,
				cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage message, CancellationToken cancellationToken)
	{
		if (message.OrderId is not long orderId)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(message.OriginalTransactionId));

		var (_, pfName, type, _, price, exchange) = _orderInfo[message.OriginalTransactionId];

		await _orderSocketClient.CancelOrder(
			message.TransactionId,
			type == OrderTypes.Conditional ? (price != default) : null,
			pfName,
			exchange,
			orderId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		if (lookupMsg is null)
			throw new ArgumentNullException(nameof(lookupMsg));

		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		if (lookupMsg.IsSubscribe)
		{
			foreach (var pfName in _pfNames.Cache)
			{
				foreach (var exchange in _exchanges)
				{
					foreach (var position in await _httpClient.GetPositions(exchange, pfName, cancellationToken))
					{
						await OnPosition(lookupMsg.TransactionId, position, cancellationToken);
						_pfNames.Add(position.Portfolio);
					}
				}
			}

			if (!lookupMsg.IsHistoryOnly())
			{
				foreach (var pfName in _pfNames.Cache)
				{
					long getId()
					{
						var id = AddSubTransId(lookupMsg.TransactionId);
						_pfNameIds.Add(id, pfName);
						return id;
					}

					foreach (var exchange in _exchanges)
					{
						await _dataSocketClient.SubscribePositions(pfName, exchange, getId(), cancellationToken);
						await _dataSocketClient.SubscribeSummaries(pfName, exchange, getId(), cancellationToken);
						await _dataSocketClient.SubscribeRisks(pfName, exchange, getId(), cancellationToken);
					}

					//await _dataSocketClient.SubscribeSpectraRisks(pfName, BoardCodes.Moex, getId(), cancellationToken);
				}
			}

			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		}
		else
		{
			foreach (var subId in GetSubTransId(lookupMsg.OriginalTransactionId))
			{
				_pfNameIds.Remove(subId);
				await _dataSocketClient.UnSubscribe(subId, cancellationToken);
			}
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		if (statusMsg is null)
			throw new ArgumentNullException(nameof(statusMsg));

		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);

		if (statusMsg.IsSubscribe)
		{
			if (!statusMsg.IsHistoryOnly())
			{
				foreach (var pfName in _pfNames.Cache)
				{
					foreach (var exchange in _exchanges)
					{
						long getId()
						{
							var id = AddSubTransId(statusMsg.TransactionId);
							_orderStatusIds.Add(id);
							return id;
						}

						await _dataSocketClient.SubscribeOrders(pfName, exchange, getId(), cancellationToken);
						await _dataSocketClient.SubscribeStopOrders(pfName, exchange, getId(), cancellationToken);
						await _dataSocketClient.SubscribeOwnTrades(pfName, exchange, getId(), cancellationToken);
					}
				}
			}

			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		}
		else
		{
			foreach (var subId in GetSubTransId(statusMsg.OriginalTransactionId))
			{
				await _dataSocketClient.UnSubscribe(subId, cancellationToken);
				_orderStatusIds.Remove(subId);
			}
		}
	}

	private ValueTask OnSpectraRisk(long id, SpectraRisk obj, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = GetParentId(id),
			SecurityId = SecurityId.Money,
			ServerTime = CurrentTime,
			PortfolioName = obj.Portfolio,
		}
		.TryAdd(PositionChangeTypes.VariationMargin, obj.VarMargin?.ToDecimal())
		.TryAdd(PositionChangeTypes.Commission, obj.Fee?.ToDecimal())
		.TryAdd(PositionChangeTypes.CurrentPrice, obj.MoneyAmount?.ToDecimal())
		.TryAdd(PositionChangeTypes.BlockedValue, obj.MoneyBlocked?.ToDecimal())
		.TryAdd(PositionChangeTypes.BeginValue, obj.MoneyOld?.ToDecimal())
		.TryAdd(PositionChangeTypes.CurrentValue, obj.MoneyFree?.ToDecimal())
		, cancellationToken);
	}

	private ValueTask OnRisk(long id, Risk obj, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = GetParentId(id),
			SecurityId = SecurityId.Money,
			ServerTime = CurrentTime,
			PortfolioName = obj.Portfolio,
		}
		.TryAdd(PositionChangeTypes.VariationMargin, obj.CorrectedMargin?.ToDecimal())
		.TryAdd(PositionChangeTypes.Commission, obj.CorrectedMargin?.ToDecimal())
		//.TryAdd(PositionChangeTypes.CurrentPrice, obj.MoneyAmount?.ToDecimal())
		//.TryAdd(PositionChangeTypes.BlockedValue, obj.MoneyBlocked?.ToDecimal())
		//.TryAdd(PositionChangeTypes.BeginValue, obj.MoneyOld?.ToDecimal())
		//.TryAdd(PositionChangeTypes.CurrentValue, obj.MoneyFree?.ToDecimal())
		, cancellationToken);
	}

	private ValueTask OnPosition(long id, Position obj, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = GetParentId(id),
			SecurityId = new() { SecurityCode = obj.Symbol, BoardCode = obj.Exchange },
			ServerTime = CurrentTime,
			PortfolioName = obj.Portfolio,
		}
		.TryAdd(PositionChangeTypes.AveragePrice, obj.AvgPrice?.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.CurrentValue, obj.Qty?.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, obj.UnrealisedPl?.ToDecimal(), true)
		, cancellationToken);
	}

	private ValueTask OnPortfolio(long id, Portfolio obj, CancellationToken cancellationToken)
	{
		if (!_pfNameIds.TryGetValue(id, out var pfName))
			return default;

		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = GetParentId(id),
			SecurityId = SecurityId.Money,
			ServerTime = CurrentTime,
			PortfolioName = pfName,
		}
		.TryAdd(PositionChangeTypes.VariationMargin, obj.InitialMargin?.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, obj.Profit?.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.Commission, obj.Commission?.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.LiquidationPrice, obj.PortfolioLiquidationValue?.ToDecimal(), true)
		, cancellationToken);
	}

	private ValueTask OnOwnTrade(long id, OwnTrade obj, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = GetParentId(id),
			ServerTime = obj.Date,
			TradeId = obj.Id,
			OrderId = obj.Order,
			TradePrice = obj.Price.ToDecimal() ?? 0,
			TradeVolume = obj.Qty,
		}, cancellationToken);
	}

	private SecurityId GetSecurityId(ISymbolObject obj)
	{
		if (_secMapByBrokSymbol.TryGetValue(obj.BrokerSymbol, out var secId))
			return secId;

		return new() { SecurityCode = obj.Symbol, BoardCode = obj.Exchange };
	}

	private ValueTask OnOrder(long id, Order obj, CancellationToken cancellationToken)
	{
		if (!long.TryParse(obj.Comment, out var transId))
			return default;

		var isLookup = _orderStatusIds.Contains(id);

		var orderType = obj.Type.ToOrderType();
		var secId = GetSecurityId(obj);
		var price = orderType == OrderTypes.Market ? 0 : obj.Price?.ToDecimal() ?? 0;

		long originId;

		if (isLookup)
		{
			originId = GetParentId(id);
			_orderInfo.Add(transId, (secId, obj.Portfolio, orderType, obj.Side, price, obj.Exchange));
		}
		else
		{
			originId = transId;
			transId = 0;
		}

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			TransactionId = transId,
			OriginalTransactionId = originId,
			ServerTime = isLookup ? obj.TransTime : obj.UpdateTime,
			ExpiryDate = obj.EndTime,
			SecurityId = secId,
			PortfolioName = obj.Portfolio,
			OrderId = obj.Id,
			OrderPrice = price,
			OrderVolume = obj.Qty,
			Balance = obj.Qty - obj.Filled,
			Side = obj.Side.ToSide(),
			OrderType = orderType,
			OrderState = obj.Status.ToOrderState(),
		}, cancellationToken);
	}

	private ValueTask OnStopOrder(long id, Order obj, CancellationToken cancellationToken)
	{
		var isLookup = _orderStatusIds.Contains(id);

		var secId = GetSecurityId(obj);
		var price = obj.Price?.ToDecimal() ?? 0;
		var transId = 0L;

		if (isLookup)
		{
			transId = TransactionIdGenerator.GetNextId();
			_orderInfo.Add(transId, (secId, obj.Portfolio, OrderTypes.Conditional, obj.Side, price, obj.Exchange));
		}

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			TransactionId = transId,
			OriginalTransactionId = isLookup ? GetParentId(id) : id,
			ServerTime = isLookup ? obj.TransTime : obj.UpdateTime,
			ExpiryDate = obj.EndTime,
			SecurityId = secId,
			PortfolioName = obj.Portfolio,
			OrderId = obj.Id,
			OrderPrice = price,
			OrderVolume = obj.Qty,
			Balance = obj.Qty - obj.FilledQtyBatch,
			Side = obj.Side.ToSide(),
			OrderType = OrderTypes.Conditional,
			OrderState = obj.Status.ToOrderState(),
			Condition = new AlorOrderCondition
			{
				TriggerPrice = obj.StopPrice?.ToDecimal() ?? 0,
			}
		}, cancellationToken);
	}

	private ValueTask OnTransError(long transId, Exception error, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			ServerTime = CurrentTime,
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = transId,
			Error = error,
		}, cancellationToken);
	}

	private ValueTask OnOrderCreated(long transId, long orderId, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			ServerTime = CurrentTime,
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = transId,
			OrderId = orderId,
			OrderState = OrderStates.Active,
			HasOrderInfo = true,
		}, cancellationToken);
	}
}