namespace StockSharp.Bitfinex;

public partial class BitfinexMessageAdapter
{
	private const string _exchangePf = "exchange";
	private const string _tradingPf = "trading";

	private long _orderStatusId;
	private readonly SynchronizedDictionary<long, long> _replaceTransIds = [];
	private readonly SynchronizedDictionary<long, long> _cancelTransIds = [];

	private string PortfolioName => nameof(Bitfinex) + "_" + Key.ToId();

	private string EncodeAccountId(string name)
	{
		return PortfolioName + "-" + name;
	}

	private string DecodeAccountId(string name)
	{
		return name.Remove(PortfolioName + "-", true);
	}

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var condition = (BitfinexOrderCondition)regMsg.Condition;

		switch (regMsg.OrderType)
		{
			case null:
			case OrderTypes.Limit:
			case OrderTypes.Market:
				break;
			case OrderTypes.Conditional:
			{
				if (!condition.IsWithdraw)
					break;

				var withdrawId = await _httpClient.Withdraw(regMsg.SecurityId.SecurityCode, DecodeAccountId(regMsg.PortfolioName), regMsg.Volume, condition.WithdrawInfo, cancellationToken);

				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OrderId = withdrawId,
					ServerTime = CurrentTime,
					OriginalTransactionId = regMsg.TransactionId,
					OrderState = OrderStates.Done,
					HasOrderInfo = true,
				}, cancellationToken);

				//ProcessPortfolioLookup(null);
				return;
			}
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		var volume = regMsg.Volume;

		if (regMsg.Side == Sides.Sell)
			volume = 0 - volume;

		var price = (decimal?)regMsg.Price;

		if (price == 0)
			price = null;

		await _pusherClient.RegisterOrder(regMsg.TransactionId, regMsg.SecurityId.ToCurrency(),
			regMsg.OrderType.ToNative(regMsg.TimeInForce, regMsg.PortfolioName.EndsWithIgnoreCase(_exchangePf), condition),
			price, volume, condition?.TrailingPrice, condition?.StopPrice, condition?.OcoPrice, condition.ToFlags(regMsg),
			regMsg.TillDate.ToTif(), regMsg.Leverage, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		if (cancelMsg.OrderId == null)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.TransactionId));

		_cancelTransIds[cancelMsg.OrderId.Value] = cancelMsg.TransactionId;
		return _pusherClient.CancelOrder(cancelMsg.OrderId.Value, cancellationToken);

		//_httpClient.CancelOrder(cancelMsg.OrderId.Value);
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		return _pusherClient.CancelAllOrders(cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		if (replaceMsg.OldOrderId == null)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(replaceMsg.TransactionId));

		var volume = replaceMsg.Volume;

		if (replaceMsg.Side == Sides.Sell)
			volume = 0 - volume;

		var condition = (BitfinexOrderCondition)replaceMsg.Condition;

		_replaceTransIds[replaceMsg.OldOrderId.Value] = replaceMsg.TransactionId;

		return _pusherClient.ReplaceOrder(replaceMsg.OldOrderId.Value, volume, null, replaceMsg.Price,
			condition?.TrailingPrice, condition?.StopPrice, condition.ToFlags(replaceMsg), replaceMsg.TillDate.ToTif(),
			replaceMsg.Leverage, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		if (lookupMsg == null)
			throw new ArgumentNullException(nameof(lookupMsg));

		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		if (!lookupMsg.IsSubscribe)
			return;

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = PortfolioName,
			BoardCode = BoardCodes.Bitfinex,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = GetPortfolio(true),
			BoardCode = BoardCodes.Bitfinex,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = GetPortfolio(false),
			BoardCode = BoardCodes.Bitfinex,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);

		if (!statusMsg.IsSubscribe)
			return;

		_orderStatusId = statusMsg.TransactionId;

		if (!statusMsg.IsHistoryOnly())
			_pusherClient.SubscribeAccount(CancelOnDisconnect);

		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private ValueTask SessionOnNewPosition(string pair, string status, decimal amount, decimal beginPrice, decimal marginFunding, int? marginFundingType, decimal? pnl, decimal? pnlPerc, decimal? priceLiq, decimal? leverage, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new PositionChangeMessage
		{
			SecurityId = pair.ToStockSharp(),
			ServerTime = CurrentTime,
			PortfolioName = PortfolioName,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, amount, true)
		.TryAdd(PositionChangeTypes.BlockedValue, marginFunding, true)
		.TryAdd(PositionChangeTypes.RealizedPnL, pnl, true)
		.TryAdd(PositionChangeTypes.Leverage, leverage, true)
		.TryAdd(PositionChangeTypes.AveragePrice, beginPrice, true)
	    .TryAdd(PositionChangeTypes.LiquidationPrice, priceLiq, true), cancellationToken);
	}

	private ValueTask SessionOnNewWallet(string name, string currency, decimal balance, decimal unsettledInterest, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new PositionChangeMessage
		{
			ServerTime = CurrentTime,
			SecurityId = new SecurityId
			{
				SecurityCode = currency,
				BoardCode = BoardCodes.Bitfinex,
			},
			PortfolioName = EncodeAccountId(name),
		}
		.TryAdd(PositionChangeTypes.CurrentValue, balance, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, unsettledInterest, true), cancellationToken);
	}

	private string GetPortfolio(bool? exchange)
	{
		return exchange == null ? null : EncodeAccountId(exchange.Value ? _exchangePf : _tradingPf);
	}

	private ValueTask SessionOnNewOwnTrade(long tradeId, string pair, long timestamp, long orderId,
		decimal tradeAmount, decimal tradePrice, string orderType, decimal? orderPrice,
		int maker, decimal? fee, string feeCurrency, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			HasOrderInfo = true,
			SecurityId = pair.ToStockSharp(),
			ServerTime = timestamp.FromUnix(false),
			DataTypeEx = DataType.Transactions,
			Side = tradeAmount > 0 ? Sides.Buy : Sides.Sell,
			OrderId = orderId,
			OrderPrice = orderPrice ?? 0,
			OrderType = orderType.ToOrderType(out var tif, out var exchange, out _),
			PortfolioName = GetPortfolio(exchange),
			TimeInForce = tif,
			TradeId = tradeId,
			TradePrice = tradePrice,
			TradeVolume = tradeAmount.Abs(),
			Commission = fee,
			OriginSide = maker == 1 ? (tradeAmount > 0 ? Sides.Buy : Sides.Sell) : (tradeAmount > 0 ? Sides.Sell : Sides.Buy),
		}, cancellationToken);
	}

	private ValueTask SessionOnOrderChanged(string eventType, long orderId, long? groupId, long? clientOrderId,
		string pair, long createdAt, long updatedAt, decimal amount, decimal amountOrigin,
		string type, string prevType, string status,
		Tuple<decimal?, decimal?, decimal?, decimal?> priceInfo,
		//decimal? price, decimal? avgPrice, decimal? trailPrice, decimal? auxLimitPrice,
		long? linkedOrder, int? flags, CancellationToken cancellationToken)
	{
		var execMsg = new ExecutionMessage
		{
			HasOrderInfo = true,
			SecurityId = pair.ToStockSharp(),
			DataTypeEx = DataType.Transactions,
			Side = amountOrigin > 0 ? Sides.Buy : Sides.Sell,
			OrderId = orderId,
			OrderPrice = priceInfo.Item1 ?? 0,
			OrderVolume = amountOrigin.Abs(),
			Balance = amount.Abs(),
			OrderType = type.ToOrderType(out var tif, out var exchange, out var isTrailing),
			PortfolioName = GetPortfolio(exchange),
			TimeInForce = tif,
			OrderState = status.ToOrderState(),
		};

		long serverTime;

		switch (eventType)
		{
			case "os": //order snapshot
				serverTime = createdAt;
				execMsg.TransactionId = clientOrderId ?? orderId;
				execMsg.OriginalTransactionId = _orderStatusId;
				break;
			case "on": //new order
				serverTime = createdAt;
				execMsg.OriginalTransactionId = clientOrderId ?? 0;
				break;
			case "ou": //order update
				serverTime = updatedAt;
				execMsg.OriginalTransactionId = _replaceTransIds.TryGetAndRemove2(orderId) ?? clientOrderId ?? 0;
				break;
			case "oc": //order cancel
				serverTime = updatedAt;
				execMsg.OriginalTransactionId = _cancelTransIds.TryGetAndRemove2(orderId) ?? clientOrderId ?? 0;
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(eventType), eventType, LocalizedStrings.InvalidValue);
		}

		execMsg.ServerTime = serverTime.FromUnix(false);

		var condition = new BitfinexOrderCondition();
		var conditionRequired = false;

		if (execMsg.OrderType == OrderTypes.Conditional)
		{
			condition = new BitfinexOrderCondition
			{
				IsTrailing = isTrailing,
				TrailingPrice = priceInfo.Item3,
				StopPrice = priceInfo.Item4,
			};

			conditionRequired = true;
		}

		if (flags > 0)
		{
			if (flags.Value.IsHidden())
				execMsg.VisibleVolume = 0;

			if (flags.Value.IsPostOnly())
				execMsg.PostOnly = true;

			if (flags.Value.IsClose())
				condition.Close = true;

			if (flags.Value.IsReduceOnly())
				execMsg.PositionEffect = OrderPositionEffects.CloseOnly;

			if (flags.Value.IsOneCancelOther())
				condition.OneCancelOther = true;

			conditionRequired = true;
		}

		if (conditionRequired)
			execMsg.Condition = condition;

		return SendOutMessageAsync(execMsg, cancellationToken);
	}

	private ValueTask SessionOnOrderError(bool isRegister, long createdAt, long? orderId, long? transactionId, string error, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			ServerTime = createdAt.FromUnix(false),
			HasOrderInfo = true,
			OrderState = OrderStates.Failed,
			OriginalTransactionId = isRegister ? (transactionId ?? 0) : (orderId == null ? 0 : _cancelTransIds.TryGetAndRemove2(orderId.Value) ?? _replaceTransIds.TryGetAndRemove(orderId.Value)),
			OrderId = isRegister ? null : orderId,
			Error = new InvalidOperationException(error),
		}, cancellationToken);
	}
}
