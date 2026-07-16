namespace StockSharp.Ctp;

public partial class CtpMessageAdapter
{
	private sealed record OrderIdentity(string InstrumentId, string ExchangeId, string OrderRef, string OrderSystemId, int FrontId, int SessionId, long TransactionId)
	{
		public string StringId => OrderSystemId.IsEmpty() ? $"{FrontId}:{SessionId}:{OrderRef}" : $"{ExchangeId}:{OrderSystemId.Trim()}";
	}

	private readonly SynchronizedDictionary<string, long> _orderRefTransactions = new(StringComparer.Ordinal);
	private readonly SynchronizedDictionary<int, long> _registerRequests = [];
	private readonly SynchronizedDictionary<int, long> _cancelRequests = [];
	private readonly SynchronizedDictionary<string, int> _cancelOrderRefs = new(StringComparer.Ordinal);
	private readonly SynchronizedDictionary<string, OrderIdentity> _ordersByStringId = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, OrderIdentity> _ordersByTransaction = [];
	private readonly SynchronizedDictionary<int, long> _requestOrigins = [];
	private OrderStatusMessage _orderLookup;
	private int _orderLookupPending;
	private long _orderStatusSubscriptionId;
	private PortfolioLookupMessage _portfolioLookup;
	private int _portfolioLookupPending;

	private string PortfolioName => InvestorId.IsEmpty() ? Login : InvestorId;

	/// <inheritdoc />
	protected override ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var condition = regMsg.Condition as CtpOrderCondition;
		if (regMsg.SecurityId.SecurityCode.IsEmpty() || regMsg.SecurityId.BoardCode.IsEmpty())
			throw new ArgumentException("CTP instrument ID and exchange ID must be specified.", nameof(regMsg));
		if (regMsg.Volume != decimal.Truncate(regMsg.Volume) || regMsg.Volume > int.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(regMsg), regMsg.Volume, "CTP order volume must fit a 32-bit whole number of contracts.");
		var volume = regMsg.Volume.To<int>();
		if (volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg), regMsg.Volume, "CTP order volume must be a positive whole number of contracts.");

		var requestId = NextNativeRequest();
		var orderRef = _client.NextOrderRef();
		var timeCondition = condition?.TimeCondition ?? regMsg.TimeInForce switch
		{
			TimeInForce.MatchOrCancel or TimeInForce.CancelBalance => CtpTimeConditions.ImmediateOrCancel,
			_ => CtpTimeConditions.GoodForDay,
		};
		var volumeCondition = condition?.VolumeCondition ?? (regMsg.TimeInForce == TimeInForce.MatchOrCancel ? CtpVolumeConditions.Complete : CtpVolumeConditions.Any);
		var stopPrice = condition?.StopPrice ?? 0;
		if (regMsg.OrderType == OrderTypes.Conditional && stopPrice <= 0)
			throw new ArgumentException("A CTP conditional order requires a positive stop price in CtpOrderCondition.", nameof(regMsg));
		var contingentCondition = condition?.ContingentCondition ?? (stopPrice > 0 ? CtpContingentConditions.Touch : CtpContingentConditions.Immediately);

		_orderRefTransactions[orderRef] = regMsg.TransactionId;
		_registerRequests[requestId] = regMsg.TransactionId;
		var provisionalIdentity = new OrderIdentity(regMsg.SecurityId.SecurityCode, regMsg.SecurityId.BoardCode, orderRef, string.Empty, 0, 0, regMsg.TransactionId);
		_ordersByTransaction[regMsg.TransactionId] = provisionalIdentity;
		_ordersByStringId[orderRef] = provisionalIdentity;
		try
		{
			_client.InsertOrder(new CtpNativeOrderRequest
			{
				RequestId = requestId,
				InstrumentId = regMsg.SecurityId.SecurityCode,
				ExchangeId = regMsg.SecurityId.BoardCode,
				OrderRef = orderRef,
				PriceType = (int)(condition?.PriceType ?? (regMsg.OrderType == OrderTypes.Market ? CtpOrderPriceTypes.AnyPrice : CtpOrderPriceTypes.LimitPrice)),
				Direction = (int)(regMsg.Side == Sides.Buy ? CtpDirections.Buy : CtpDirections.Sell),
				OffsetFlag = (int)(condition?.Offset ?? CtpOffsetFlags.Open),
				HedgeFlag = (int)(condition?.Hedge ?? CtpHedgeFlags.Speculation),
				LimitPrice = regMsg.OrderType == OrderTypes.Market ? 0 : (double)regMsg.Price,
				Volume = volume,
				TimeCondition = (int)timeCondition,
				GoodTillDate = timeCondition == CtpTimeConditions.GoodTillDate ? condition?.GoodTillDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? string.Empty : string.Empty,
				VolumeCondition = (int)volumeCondition,
				MinimumVolume = condition?.MinimumVolume ?? (volumeCondition == CtpVolumeConditions.Complete ? volume : 1),
				ContingentCondition = (int)contingentCondition,
				StopPrice = (double)stopPrice,
				ForceCloseReason = (int)(condition?.ForceCloseReason ?? CtpForceCloseReasons.None),
			});
		}
		catch
		{
			_orderRefTransactions.Remove(orderRef);
			_registerRequests.Remove(requestId);
			_ordersByTransaction.Remove(regMsg.TransactionId);
			_ordersByStringId.Remove(orderRef);
			throw;
		}

		return default;
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		OrderIdentity identity = null;
		if (cancelMsg.OriginalTransactionId != 0 && _ordersByTransaction.TryGetValue(cancelMsg.OriginalTransactionId, out var byTransaction))
			identity = byTransaction;
		if (identity == null && !cancelMsg.OrderStringId.IsEmpty() && _ordersByStringId.TryGetValue(cancelMsg.OrderStringId, out var byStringId))
			identity = byStringId;
		if (identity == null)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		var requestId = NextNativeRequest();
		_cancelRequests[requestId] = cancelMsg.TransactionId;
		_cancelOrderRefs[identity.OrderRef] = requestId;
		try
		{
			_client.CancelOrder(new CtpNativeCancelRequest
			{
				RequestId = requestId,
				InstrumentId = identity.InstrumentId,
				ExchangeId = identity.ExchangeId,
				OrderRef = identity.OrderRef,
				OrderSystemId = identity.OrderSystemId,
				FrontId = identity.FrontId,
				SessionId = identity.SessionId,
			});
		}
		catch
		{
			_cancelRequests.Remove(requestId);
			_cancelOrderRefs.Remove(identity.OrderRef);
			throw;
		}

		return default;
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
		if (_orderLookup != null)
			throw new InvalidOperationException("Only one CTP order lookup can be active at a time.");

		_orderLookup = statusMsg.TypedClone();
		_orderLookupPending = 2;
		if (!statusMsg.IsHistoryOnly())
			_orderStatusSubscriptionId = statusMsg.TransactionId;

		try
		{
			var ordersRequest = TrackRequest(statusMsg.TransactionId);
			await SendQuery(() => _client.QueryOrders(ordersRequest), cancellationToken);
			var tradesRequest = TrackRequest(statusMsg.TransactionId);
			await SendQuery(() => _client.QueryTrades(tradesRequest), cancellationToken);
		}
		catch
		{
			_orderLookup = null;
			_orderLookupPending = 0;
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		if (!lookupMsg.IsSubscribe)
			return;
		if (_portfolioLookup != null)
			throw new InvalidOperationException("Only one CTP portfolio lookup can be active at a time.");

		_portfolioLookup = lookupMsg.TypedClone();
		_portfolioLookupPending = 2;
		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = lookupMsg.TransactionId,
			PortfolioName = PortfolioName,
			BoardCode = AssociatedBoards[0],
		}, cancellationToken);

		try
		{
			var positionsRequest = TrackRequest(lookupMsg.TransactionId);
			await SendQuery(() => _client.QueryPositions(positionsRequest), cancellationToken);
			var accountRequest = TrackRequest(lookupMsg.TransactionId);
			await SendQuery(() => _client.QueryAccount(accountRequest), cancellationToken);
		}
		catch
		{
			_portfolioLookup = null;
			_portfolioLookupPending = 0;
			throw;
		}
	}

	private int TrackRequest(long originId)
	{
		var requestId = NextNativeRequest();
		_requestOrigins[requestId] = originId;
		return requestId;
	}

	private void OnNativeOrder(CtpNativeOrder? order, CtpNativeError? error, int requestId, bool isLast, bool isQuery)
		=> Enqueue(cancellationToken => ProcessNativeOrder(order, error, requestId, isLast, isQuery, cancellationToken));

	private async ValueTask ProcessNativeOrder(CtpNativeOrder? native, CtpNativeError? error, int requestId, bool isLast, bool isQuery, CancellationToken cancellationToken)
	{
		var queryOriginId = isQuery ? _requestOrigins.TryGetValue2(requestId) ?? 0 : 0;
		if (error is { Id: not 0 } nativeError)
		{
			var transactionId = _orderRefTransactions.TryGetValue2(nativeError.OrderRef) ?? _registerRequests.TryGetValue2(requestId) ?? 0;
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				OriginalTransactionId = transactionId != 0 ? transactionId : queryOriginId,
				OrderStringId = nativeError.OrderRef,
				SecurityId = nativeError.InstrumentId.ToSecurityId(string.Empty),
				PortfolioName = PortfolioName,
				OrderState = OrderStates.Failed,
				ServerTime = CurrentTime,
				Error = nativeError.ToException("order"),
			}, cancellationToken);
			_registerRequests.Remove(requestId);
			if (transactionId != 0)
			{
				_orderRefTransactions.Remove(nativeError.OrderRef);
				_ordersByTransaction.Remove(transactionId);
				_ordersByStringId.Remove(nativeError.OrderRef);
			}
		}

		if (native is { } order)
		{
			var transactionId = _orderRefTransactions.TryGetValue2(order.OrderRef) ?? 0;
			var identity = new OrderIdentity(order.InstrumentId, order.ExchangeId, order.OrderRef, order.OrderSystemId, order.FrontId, order.SessionId, transactionId);
			_ordersByStringId[identity.StringId] = identity;
			_ordersByStringId[order.OrderRef] = identity;
			if (!order.OrderSystemId.IsEmpty())
				_ordersByStringId[order.OrderSystemId.Trim()] = identity;
			if (transactionId != 0)
				_ordersByTransaction[transactionId] = identity;

			var state = order.ToOrderState();
			var orderError = state == OrderStates.Failed
				? new InvalidOperationException(order.StatusMessage.IsEmpty() ? $"CTP rejected order {identity.StringId}." : order.StatusMessage)
				: null;
			var originId = isQuery
				? queryOriginId
				: transactionId != 0 ? transactionId : _orderStatusSubscriptionId;
			var date = order.InsertDate.IsEmpty() ? order.TradingDay : order.InsertDate;
			var time = !order.UpdateTime.IsEmpty() ? order.UpdateTime : !order.CancelTime.IsEmpty() ? order.CancelTime : order.InsertTime;
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				OriginalTransactionId = originId,
				TransactionId = isQuery ? transactionId : 0,
				OrderStringId = identity.StringId,
				SecurityId = order.InstrumentId.ToSecurityId(order.ExchangeId),
				PortfolioName = PortfolioName,
				Side = order.Direction.ToSide(),
				OrderType = order.ContingentCondition != (int)CtpContingentConditions.Immediately
					? OrderTypes.Conditional
					: order.PriceType == (int)CtpOrderPriceTypes.LimitPrice ? OrderTypes.Limit : OrderTypes.Market,
				OrderPrice = (decimal)order.LimitPrice,
				OrderVolume = order.VolumeOriginal,
				Balance = order.VolumeLeft,
				OrderState = state,
				ServerTime = date.ToCtpTime(time),
				Condition = order.ToCondition(),
				Error = orderError,
			}, cancellationToken);

			_registerRequests.Remove(order.RequestId);
			if (state == OrderStates.Done && _cancelOrderRefs.TryGetAndRemove(order.OrderRef, out var cancelRequest))
				_cancelRequests.Remove(cancelRequest);
		}

		if (isQuery && isLast)
			await CompleteOrderLookup(requestId, cancellationToken);
	}

	private void OnNativeTrade(CtpNativeTrade? trade, CtpNativeError? error, int requestId, bool isLast, bool isQuery)
		=> Enqueue(cancellationToken => ProcessNativeTrade(trade, error, requestId, isLast, isQuery, cancellationToken));

	private async ValueTask ProcessNativeTrade(CtpNativeTrade? native, CtpNativeError? error, int requestId, bool isLast, bool isQuery, CancellationToken cancellationToken)
	{
		var queryOriginId = isQuery ? _requestOrigins.TryGetValue2(requestId) ?? 0 : 0;
		if (error is { Id: not 0 } nativeError)
			await SendOutErrorAsync(nativeError.ToException("trade query"), cancellationToken);

		if (native is { } trade)
		{
			var transactionId = _orderRefTransactions.TryGetValue2(trade.OrderRef) ?? 0;
			var originId = isQuery
				? queryOriginId
				: transactionId != 0 ? transactionId : _orderStatusSubscriptionId;
			var orderStringId = trade.OrderSystemId.IsEmpty() ? trade.OrderRef : $"{trade.ExchangeId}:{trade.OrderSystemId.Trim()}";
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = originId,
				TransactionId = isQuery ? transactionId : 0,
				OrderStringId = orderStringId,
				TradeStringId = trade.TradeId.IsEmpty() ? trade.SequenceNumber.ToString(CultureInfo.InvariantCulture) : trade.TradeId.Trim(),
				SecurityId = trade.InstrumentId.ToSecurityId(trade.ExchangeId),
				PortfolioName = PortfolioName,
				Side = trade.Direction.ToSide(),
				TradePrice = (decimal)trade.Price,
				TradeVolume = trade.Volume,
				ServerTime = (trade.TradeDate.IsEmpty() ? trade.TradingDay : trade.TradeDate).ToCtpTime(trade.TradeTime),
			}, cancellationToken);
		}

		if (isQuery && isLast)
			await CompleteOrderLookup(requestId, cancellationToken);
	}

	private async ValueTask CompleteOrderLookup(int requestId, CancellationToken cancellationToken)
	{
		_requestOrigins.Remove(requestId);
		if (_orderLookup == null || --_orderLookupPending != 0)
			return;

		var lookup = _orderLookup;
		_orderLookup = null;
		await SendSubscriptionResultAsync(lookup, cancellationToken);
	}

	private void OnNativePosition(CtpNativePosition? position, CtpNativeError? error, int requestId, bool isLast)
		=> Enqueue(cancellationToken => ProcessNativePosition(position, error, requestId, isLast, cancellationToken));

	private async ValueTask ProcessNativePosition(CtpNativePosition? native, CtpNativeError? error, int requestId, bool isLast, CancellationToken cancellationToken)
	{
		if (error is { Id: not 0 } nativeError)
			await SendOutErrorAsync(nativeError.ToException("position query"), cancellationToken);

		if (native is { } position)
		{
			var isShort = position.Direction == (int)CtpPositionDirections.Short;
			var positionSide = position.Direction switch
			{
				(int)CtpPositionDirections.Long => Sides.Buy,
				(int)CtpPositionDirections.Short => Sides.Sell,
				_ => (Sides?)null,
			};
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = _requestOrigins.TryGetValue2(requestId) ?? 0,
				PortfolioName = PortfolioName,
				SecurityId = position.InstrumentId.ToSecurityId(position.ExchangeId),
				Side = positionSide,
				DepoName = $"{(char)position.HedgeFlag}:{(char)position.PositionDate}",
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, position.Position, true)
			.TryAdd(PositionChangeTypes.CurrentValueInLots, position.Position, true)
			.TryAdd(PositionChangeTypes.BlockedValue, isShort ? position.ShortFrozen : position.LongFrozen, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, (decimal)position.PositionProfit, true)
			.TryAdd(PositionChangeTypes.RealizedPnL, (decimal)position.CloseProfit, true)
			.TryAdd(PositionChangeTypes.VariationMargin, (decimal)position.UseMargin, true)
			.TryAdd(PositionChangeTypes.Commission, (decimal)position.Commission, true)
			.TryAdd(PositionChangeTypes.SettlementPrice, position.SettlementPrice > 0 ? (decimal?)position.SettlementPrice : null, true), cancellationToken);
		}

		if (isLast)
			await CompletePortfolioLookup(requestId, cancellationToken);
	}

	private void OnNativeAccount(CtpNativeAccount? account, CtpNativeError? error, int requestId, bool isLast)
		=> Enqueue(cancellationToken => ProcessNativeAccount(account, error, requestId, isLast, cancellationToken));

	private async ValueTask ProcessNativeAccount(CtpNativeAccount? native, CtpNativeError? error, int requestId, bool isLast, CancellationToken cancellationToken)
	{
		if (error is { Id: not 0 } nativeError)
			await SendOutErrorAsync(nativeError.ToException("account query"), cancellationToken);

		if (native is { } account)
		{
			var currency = account.CurrencyId.IsEmpty() ? CurrencyTypes.CNY : account.CurrencyId.To<CurrencyTypes?>() ?? CurrencyTypes.CNY;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = _requestOrigins.TryGetValue2(requestId) ?? 0,
				PortfolioName = PortfolioName,
				SecurityId = SecurityId.Money,
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.BeginValue, (decimal)account.PreBalance, true)
			.TryAdd(PositionChangeTypes.CurrentValue, (decimal)account.Balance, true)
			.TryAdd(PositionChangeTypes.CurrentPrice, (decimal)account.Available, true)
			.TryAdd(PositionChangeTypes.BlockedValue, (decimal)(account.FrozenCash + account.FrozenMargin + account.FrozenCommission), true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, (decimal)account.PositionProfit, true)
			.TryAdd(PositionChangeTypes.RealizedPnL, (decimal)account.CloseProfit, true)
			.TryAdd(PositionChangeTypes.VariationMargin, (decimal)account.CurrentMargin, true)
			.TryAdd(PositionChangeTypes.Commission, (decimal)account.Commission, true)
			.TryAdd(PositionChangeTypes.Currency, currency), cancellationToken);
		}

		if (isLast)
			await CompletePortfolioLookup(requestId, cancellationToken);
	}

	private async ValueTask CompletePortfolioLookup(int requestId, CancellationToken cancellationToken)
	{
		_requestOrigins.Remove(requestId);
		if (_portfolioLookup == null || --_portfolioLookupPending != 0)
			return;

		var lookup = _portfolioLookup;
		_portfolioLookup = null;
		await SendSubscriptionResultAsync(lookup, cancellationToken);
		await SendSubscriptionFinishedAsync(lookup.TransactionId, cancellationToken);
	}

	private async ValueTask ProcessNativeError(CtpChannels channel, CtpNativeError error, CancellationToken cancellationToken)
	{
		if (_cancelRequests.TryGetAndRemove(error.RequestId, out var transactionId))
		{
			if (!error.OrderRef.IsEmpty())
				_cancelOrderRefs.Remove(error.OrderRef);
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				OriginalTransactionId = transactionId,
				OrderStringId = error.OrderRef,
				SecurityId = error.InstrumentId.ToSecurityId(string.Empty),
				OrderState = OrderStates.Failed,
				ServerTime = CurrentTime,
				Error = error.ToException("cancel order"),
			}, cancellationToken);
			return;
		}

		await SendOutErrorAsync(error.ToException(channel.ToString()), cancellationToken);
	}

	private void ClearTransactionState()
	{
		_orderRefTransactions.Clear();
		_registerRequests.Clear();
		_cancelRequests.Clear();
		_cancelOrderRefs.Clear();
		_ordersByStringId.Clear();
		_ordersByTransaction.Clear();
		_requestOrigins.Clear();
		_orderLookup = null;
		_orderLookupPending = 0;
		_orderStatusSubscriptionId = 0;
		_portfolioLookup = null;
		_portfolioLookupPending = 0;
	}
}
