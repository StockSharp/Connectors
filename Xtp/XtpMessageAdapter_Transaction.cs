namespace StockSharp.Xtp;

public partial class XtpMessageAdapter
{
	private readonly SynchronizedDictionary<uint, long> _clientTransactions = [];
	private readonly SynchronizedDictionary<ulong, long> _orderTransactions = [];
	private readonly SynchronizedDictionary<ulong, long> _cancelTransactions = [];
	private readonly SynchronizedDictionary<int, long> _requestOrigins = [];
	private int _nativeClientOrderId;
	private int _nativeRequestId;
	private OrderStatusMessage _orderLookup;
	private int _orderLookupPending;
	private long _orderStatusSubscriptionId;
	private PortfolioLookupMessage _portfolioLookup;
	private int _portfolioLookupPending;

	/// <inheritdoc />
	protected override ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var condition = regMsg.Condition as XtpOrderCondition;
		var clientOrderId = unchecked((uint)Interlocked.Increment(ref _nativeClientOrderId));
		_clientTransactions[clientOrderId] = regMsg.TransactionId;

		var priceType = condition?.PriceType ?? regMsg.TimeInForce switch
		{
			TimeInForce.MatchOrCancel => XtpPriceTypes.AllOrCancel,
			TimeInForce.CancelBalance when regMsg.SecurityId.ToXtpMarket() == XtpMarket.Shanghai => XtpPriceTypes.Best5OrCancel,
			TimeInForce.CancelBalance => XtpPriceTypes.BestOrCancel,
			_ when regMsg.OrderType == OrderTypes.Market && regMsg.SecurityId.ToXtpMarket() == XtpMarket.Shanghai => XtpPriceTypes.Best5OrCancel,
			_ when regMsg.OrderType == OrderTypes.Market => XtpPriceTypes.BestOrCancel,
			_ => XtpPriceTypes.Limit,
		};

		try
		{
			var orderId = _client.InsertOrder(new XtpNativeOrderRequest
			{
				ClientOrderId = clientOrderId,
				Ticker = regMsg.SecurityId.SecurityCode,
				Market = (int)regMsg.SecurityId.ToXtpMarket(),
				Price = regMsg.OrderType == OrderTypes.Market ? 0 : (double)regMsg.Price,
				StopPrice = (double)(condition?.StopPrice ?? 0),
				Volume = regMsg.Volume.To<long>(),
				PriceType = (int)priceType,
				Side = (int)(condition?.NativeSide ?? (regMsg.Side == Sides.Buy ? XtpOrderSides.Buy : XtpOrderSides.Sell)),
				PositionEffect = (int)(condition?.PositionEffect ?? XtpPositionEffects.None),
				BusinessType = (int)(condition?.BusinessType ?? XtpBusinessTypes.Cash),
			});
			_orderTransactions[orderId] = regMsg.TransactionId;
		}
		catch
		{
			_clientTransactions.Remove(clientOrderId);
			throw;
		}

		return default;
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		ulong orderId;
		if (!ulong.TryParse(cancelMsg.OrderStringId, NumberStyles.None, CultureInfo.InvariantCulture, out orderId))
		{
			if (cancelMsg.OrderId is not long numericOrderId || numericOrderId <= 0)
				throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));
			orderId = (ulong)numericOrderId;
		}

		var cancelId = _client.CancelOrder(orderId);
		_cancelTransactions[cancelId] = cancelMsg.TransactionId;
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
			throw new InvalidOperationException("Only one XTP order lookup can be active at a time.");

		_orderLookup = statusMsg.TypedClone();
		_orderLookupPending = 2;
		if (!statusMsg.IsHistoryOnly())
			_orderStatusSubscriptionId = statusMsg.TransactionId;

		try
		{
			var ordersRequest = NextRequest(statusMsg.TransactionId);
			var tradesRequest = NextRequest(statusMsg.TransactionId);
			_client.QueryOrders(ordersRequest);
			_client.QueryTrades(tradesRequest);
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
			throw new InvalidOperationException("Only one XTP portfolio lookup can be active at a time.");

		_portfolioLookup = lookupMsg.TypedClone();
		_portfolioLookupPending = 2;
		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = lookupMsg.TransactionId,
			PortfolioName = Login,
			BoardCode = BoardCodes.Sse,
		}, cancellationToken);

		try
		{
			var positionsRequest = NextRequest(lookupMsg.TransactionId);
			var assetsRequest = NextRequest(lookupMsg.TransactionId);
			_client.QueryPositions(positionsRequest);
			_client.QueryAssets(assetsRequest);
		}
		catch
		{
			_portfolioLookup = null;
			_portfolioLookupPending = 0;
			throw;
		}
	}

	private int NextRequest(long originId)
	{
		var requestId = Interlocked.Increment(ref _nativeRequestId);
		_requestOrigins[requestId] = originId;
		return requestId;
	}

	private void OnNativeOrder(XtpNativeOrder? order, XtpNativeError error, int requestId, bool isLast, bool isQuery)
		=> Enqueue(cancellationToken => ProcessNativeOrder(order, error, requestId, isLast, isQuery, cancellationToken));

	private async ValueTask ProcessNativeOrder(XtpNativeOrder? native, XtpNativeError error, int requestId, bool isLast, bool isQuery, CancellationToken cancellationToken)
	{
		var originId = isQuery ? _requestOrigins.TryGetValue2(requestId) ?? 0 : 0;
		if (error.Id != 0)
			await SendOutErrorAsync(error.ToException("order"), cancellationToken);

		if (native is { } order)
		{
			var transactionId = _clientTransactions.TryGetValue2(order.ClientOrderId) ?? 0;
			if (transactionId == 0)
				transactionId = _orderTransactions.TryGetValue2(order.OrderId) ?? 0;
			if (transactionId != 0)
				_orderTransactions[order.OrderId] = transactionId;
			else if (!isQuery)
				originId = _orderStatusSubscriptionId;

			var state = error.Id != 0 ? OrderStates.Failed : order.Status.ToOrderState();
			var orderError = error.Id != 0
				? error.ToException("order")
				: state == OrderStates.Failed
					? new InvalidOperationException($"XTP rejected order {order.OrderId}.")
					: null;
			var averagePrice = order.TradedVolume > 0 && order.TradeAmount > 0 ? (decimal?)(order.TradeAmount / order.TradedVolume) : null;
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				OriginalTransactionId = isQuery ? originId : transactionId != 0 ? transactionId : originId,
				TransactionId = isQuery ? transactionId : 0,
				OrderStringId = order.OrderId.ToString(CultureInfo.InvariantCulture),
				SecurityId = order.Ticker.ToSecurityId(order.Market),
				PortfolioName = Login,
				Side = order.Side.ToSide(),
				OrderType = order.PriceType == (int)XtpPriceTypes.Limit ? OrderTypes.Limit : OrderTypes.Market,
				OrderPrice = (decimal)order.Price,
				OrderVolume = order.Volume,
				Balance = order.Balance,
				AveragePrice = averagePrice,
				OrderState = state,
				ServerTime = (order.UpdateTime > 0 ? order.UpdateTime : order.InsertTime).ToXtpTime(),
				Condition = new XtpOrderCondition
				{
					PriceType = Enum.IsDefined((XtpPriceTypes)order.PriceType) ? (XtpPriceTypes)order.PriceType : null,
					NativeSide = Enum.IsDefined((XtpOrderSides)order.Side) ? (XtpOrderSides)order.Side : null,
					PositionEffect = Enum.IsDefined((XtpPositionEffects)order.PositionEffect) ? (XtpPositionEffects)order.PositionEffect : XtpPositionEffects.None,
					BusinessType = Enum.IsDefined((XtpBusinessTypes)order.BusinessType) ? (XtpBusinessTypes)order.BusinessType : XtpBusinessTypes.Cash,
				},
				Error = orderError,
			}, cancellationToken);
		}

		if (isQuery && isLast)
			await CompleteOrderLookup(requestId, cancellationToken);
	}

	private void OnNativeTrade(XtpNativeTrade? trade, XtpNativeError error, int requestId, bool isLast, bool isQuery)
		=> Enqueue(cancellationToken => ProcessNativeTrade(trade, error, requestId, isLast, isQuery, cancellationToken));

	private async ValueTask ProcessNativeTrade(XtpNativeTrade? native, XtpNativeError error, int requestId, bool isLast, bool isQuery, CancellationToken cancellationToken)
	{
		var originId = isQuery ? _requestOrigins.TryGetValue2(requestId) ?? 0 : 0;
		if (error.Id != 0)
			await SendOutErrorAsync(error.ToException("trade"), cancellationToken);

		if (native is { } trade)
		{
			var transactionId = _clientTransactions.TryGetValue2(trade.ClientOrderId) ?? 0;
			if (transactionId == 0)
				transactionId = _orderTransactions.TryGetValue2(trade.OrderId) ?? 0;
			if (transactionId == 0 && !isQuery)
				originId = _orderStatusSubscriptionId;

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = isQuery ? originId : transactionId != 0 ? transactionId : originId,
				TransactionId = isQuery ? transactionId : 0,
				OrderStringId = trade.OrderId.ToString(CultureInfo.InvariantCulture),
				TradeStringId = trade.TradeId.IsEmpty() ? trade.ReportIndex.ToString(CultureInfo.InvariantCulture) : trade.TradeId,
				SecurityId = trade.Ticker.ToSecurityId(trade.Market),
				PortfolioName = Login,
				Side = trade.Side.ToSide(),
				TradePrice = (decimal)trade.Price,
				TradeVolume = trade.Volume,
				ServerTime = trade.Time.ToXtpTime(),
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

	private void OnNativeCancelError(ulong orderId, ulong cancelOrderId, XtpNativeError error)
		=> Enqueue(cancellationToken => ProcessNativeCancelError(orderId, cancelOrderId, error, cancellationToken));

	private ValueTask ProcessNativeCancelError(ulong orderId, ulong cancelOrderId, XtpNativeError error, CancellationToken cancellationToken)
	{
		var transactionId = _cancelTransactions.TryGetValue2(cancelOrderId) ?? 0;
		_cancelTransactions.Remove(cancelOrderId);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = transactionId,
			OrderStringId = orderId.ToString(CultureInfo.InvariantCulture),
			OrderState = OrderStates.Failed,
			ServerTime = CurrentTime,
			Error = error.ToException("cancel order"),
		}, cancellationToken);
	}

	private void OnNativePosition(XtpNativePosition? position, XtpNativeError error, int requestId, bool isLast)
		=> Enqueue(cancellationToken => ProcessNativePosition(position, error, requestId, isLast, cancellationToken));

	private async ValueTask ProcessNativePosition(XtpNativePosition? native, XtpNativeError error, int requestId, bool isLast, CancellationToken cancellationToken)
	{
		if (error.Id != 0)
			await SendOutErrorAsync(error.ToException("position query"), cancellationToken);

		if (native is { } position)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = Login,
				SecurityId = position.Ticker.ToSecurityId(position.Market),
				Side = position.Direction == 2 ? Sides.Sell : Sides.Buy,
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, position.Volume, true)
			.TryAdd(PositionChangeTypes.BlockedValue, Math.Max(0, position.Volume - position.SellableVolume), true)
			.TryAdd(PositionChangeTypes.AveragePrice, (decimal)position.AveragePrice, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, (decimal)position.UnrealizedPnl, true)
			.TryAdd(PositionChangeTypes.CurrentPrice, position.Volume > 0 ? (decimal?)(position.MarketValue / position.Volume) : null, true), cancellationToken);
		}

		if (isLast)
			await CompletePortfolioLookup(requestId, cancellationToken);
	}

	private void OnNativeAsset(XtpNativeAsset? asset, XtpNativeError error, int requestId, bool isLast)
		=> Enqueue(cancellationToken => ProcessNativeAsset(asset, error, requestId, isLast, cancellationToken));

	private async ValueTask ProcessNativeAsset(XtpNativeAsset? native, XtpNativeError error, int requestId, bool isLast, CancellationToken cancellationToken)
	{
		if (error.Id != 0)
			await SendOutErrorAsync(error.ToException("asset query"), cancellationToken);

		if (native is { } asset)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = Login,
				SecurityId = SecurityId.Money,
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, (decimal)asset.TotalAsset, true)
			.TryAdd(PositionChangeTypes.CurrentPrice, (decimal)asset.BuyingPower, true)
			.TryAdd(PositionChangeTypes.BlockedValue, (decimal)asset.FrozenCash, true)
			.TryAdd(PositionChangeTypes.RealizedPnL, (decimal)asset.RealizedPnl, true), cancellationToken);
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

	private void ClearTransactionState()
	{
		_clientTransactions.Clear();
		_orderTransactions.Clear();
		_cancelTransactions.Clear();
		_requestOrigins.Clear();
		_orderLookup = null;
		_orderLookupPending = 0;
		_orderStatusSubscriptionId = 0;
		_portfolioLookup = null;
		_portfolioLookupPending = 0;
	}
}
