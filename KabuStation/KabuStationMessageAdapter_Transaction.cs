namespace StockSharp.KabuStation;

public partial class KabuStationMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var security = ResolveSecurity(regMsg.SecurityId, regMsg.SecurityType);
		var condition = regMsg.Condition as KabuStationOrderCondition;
		var result = security.SecurityType switch
		{
			SecurityTypes.Stock => await _rest.PlaceStockOrder(CreateStockOrder(regMsg, security, condition), cancellationToken),
			SecurityTypes.Future => await _rest.PlaceFutureOrder(CreateDerivativeOrder(regMsg, security, condition), cancellationToken),
			SecurityTypes.Option => await _rest.PlaceOptionOrder(CreateDerivativeOrder(regMsg, security, condition), cancellationToken),
			_ => throw new NotSupportedException($"kabu Station cannot submit {security.SecurityType} orders."),
		};
		if (result.Result != 0)
			throw new InvalidOperationException($"kabu Station rejected the order with result {result.Result}.");

		var orderId = result.OrderId.ThrowIfEmpty(nameof(result.OrderId));
		_orders[orderId] = new()
		{
			TransactionId = regMsg.TransactionId,
			SecurityId = regMsg.SecurityId,
			Security = security,
			PortfolioName = regMsg.PortfolioName.IsEmpty(GetPortfolioName()),
			Side = regMsg.Side,
			OrderType = regMsg.OrderType ?? OrderTypes.Limit,
			Price = regMsg.Price,
			Volume = regMsg.Volume,
		};

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			OrderStringId = orderId,
			SecurityId = regMsg.SecurityId,
			PortfolioName = regMsg.PortfolioName.IsEmpty(GetPortfolioName()),
			OrderType = regMsg.OrderType,
			Side = regMsg.Side,
			OrderPrice = regMsg.Price,
			OrderVolume = regMsg.Volume,
			Balance = regMsg.Volume,
			OrderState = OrderStates.Active,
			ServerTime = CurrentTime,
			Condition = condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var orderId = cancelMsg.OrderStringId;
		if (orderId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));
		var result = await _rest.CancelOrder(orderId, cancellationToken);
		if (result.Result != 0)
			throw new InvalidOperationException($"kabu Station rejected the cancellation with result {result.Result}.");
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

		await SendOrderSnapshot(statusMsg.TransactionId, null, cancellationToken);
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

		await SendPortfolioSnapshot(lookupMsg.TransactionId, cancellationToken);
		if (!lookupMsg.IsHistoryOnly())
			_portfolioSubscriptionId = lookupMsg.TransactionId;
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private KabuStationStockOrderRequest CreateStockOrder(OrderRegisterMessage message,
		KabuStationSecurityInfo security, KabuStationOrderCondition condition)
	{
		var cashMargin = condition?.CashMargin ?? KabuStationCashMargins.Cash;
		var exchange = condition?.Exchange is { } explicitExchange
			? (int)explicitExchange
			: security.Exchange == (int)KabuStationExchanges.Tokyo
				? (int)DefaultStockOrderExchange
				: security.Exchange;
		if (exchange is 2 or 23 or 24)
			throw new ArgumentOutOfRangeException(nameof(condition), exchange, "A stock order requires a stock exchange route.");

		var isStop = condition?.StopPrice is > 0;
		var orderType = message.OrderType ?? OrderTypes.Limit;
		if (!isStop && message.TimeInForce == TimeInForce.MatchOrCancel)
			throw new NotSupportedException("kabu Station cash and margin stock orders do not expose FOK.");
		var frontOrderType = isStop
			? 30
			: orderType == OrderTypes.Market
				? message.TimeInForce == TimeInForce.CancelBalance ? 17 : 10
				: message.TimeInForce == TimeInForce.CancelBalance ? 27 : 20;
		var closePositions = condition?.ClosePositionId.IsEmpty() == false
			? new[] { new KabuStationClosePosition { HoldId = condition.ClosePositionId, Quantity = message.Volume.To<long>() } }
			: null;

		return new()
		{
			Symbol = security.Symbol,
			Exchange = exchange,
			Side = message.Side.ToKabuSide(),
			CashMargin = (int)cashMargin,
			MarginTradeType = cashMargin == KabuStationCashMargins.Cash
				? null
				: (int)(condition?.MarginTradeType ?? KabuStationMarginTradeTypes.Standard),
			DeliveryType = cashMargin switch
			{
				KabuStationCashMargins.Cash when message.Side == Sides.Buy => 2,
				KabuStationCashMargins.MarginClose => 2,
				_ => 0,
			},
			FundType = cashMargin == KabuStationCashMargins.Cash
				? message.Side == Sides.Buy ? "02" : "  "
				: "11",
			AccountType = (int)(condition?.AccountType ?? DefaultAccountType),
			Quantity = message.Volume.To<long>(),
			ClosePositionOrder = cashMargin == KabuStationCashMargins.MarginClose && closePositions == null ? 0 : null,
			ClosePositions = closePositions,
			FrontOrderType = frontOrderType,
			Price = isStop || orderType == OrderTypes.Market ? 0 : message.Price,
			ExpireDay = (condition?.ExpireDate ?? message.TillDate).ToApiDate(),
			StopOrder = isStop ? new()
			{
				TriggerPrice = condition.StopPrice.Value,
				Comparison = (int)(condition.TriggerComparison ?? (message.Side == Sides.Buy
					? KabuStationTriggerComparisons.AtOrAbove
					: KabuStationTriggerComparisons.AtOrBelow)),
				AfterHitOrderType = (int)(condition.StopLimitPrice is > 0
					? KabuStationAfterHitOrderTypes.Limit
					: KabuStationAfterHitOrderTypes.Market),
				AfterHitPrice = condition.StopLimitPrice ?? 0,
			} : null,
		};
	}

	private KabuStationDerivativeOrderRequest CreateDerivativeOrder(OrderRegisterMessage message,
		KabuStationSecurityInfo security, KabuStationOrderCondition condition)
	{
		if (security.Exchange is not (2 or 23 or 24))
			throw new ArgumentOutOfRangeException(nameof(security), security.Exchange, "A derivative order requires an Osaka session exchange.");
		var isStop = condition?.StopPrice is > 0;
		var orderType = message.OrderType ?? OrderTypes.Limit;
		var closePositions = condition?.ClosePositionId.IsEmpty() == false
			? new[] { new KabuStationClosePosition { HoldId = condition.ClosePositionId, Quantity = message.Volume.To<long>() } }
			: null;
		var tradeType = condition?.DerivativeTradeType ??
			(closePositions == null ? KabuStationDerivativeTradeTypes.Open : KabuStationDerivativeTradeTypes.Close);
		var nativeTimeInForce = condition?.NativeTimeInForce ??
			(isStop && condition?.StopLimitPrice is not > 0 || orderType == OrderTypes.Market
				? KabuStationTimeInForces.Fak
				: message.TimeInForce.ToKabuTimeInForce());

		return new()
		{
			Symbol = security.Symbol,
			Exchange = (int)(condition?.Exchange ?? (KabuStationExchanges)security.Exchange),
			TradeType = (int)tradeType,
			TimeInForce = (int)nativeTimeInForce,
			Side = message.Side.ToKabuSide(),
			Quantity = message.Volume.To<long>(),
			ClosePositionOrder = tradeType == KabuStationDerivativeTradeTypes.Close && closePositions == null ? 0 : null,
			ClosePositions = closePositions,
			FrontOrderType = isStop ? 30 : orderType == OrderTypes.Market ? 120 : 20,
			Price = isStop || orderType == OrderTypes.Market ? 0 : message.Price,
			ExpireDay = (condition?.ExpireDate ?? message.TillDate).ToApiDate(),
			StopOrder = isStop ? new()
			{
				TriggerPrice = condition.StopPrice.Value,
				Comparison = (int)(condition.TriggerComparison ?? (message.Side == Sides.Buy
					? KabuStationTriggerComparisons.AtOrAbove
					: KabuStationTriggerComparisons.AtOrBelow)),
				AfterHitOrderType = (int)(condition.StopLimitPrice is > 0
					? KabuStationAfterHitOrderTypes.Limit
					: KabuStationAfterHitOrderTypes.Market),
				AfterHitPrice = condition.StopLimitPrice ?? 0,
			} : null,
		};
	}

	private async ValueTask SendOrderSnapshot(long originId, DateTime? updatedFrom,
		CancellationToken cancellationToken)
	{
		foreach (var order in await _rest.GetOrders(updatedFrom, cancellationToken))
			await ProcessOrder(order, originId, cancellationToken);
		_lastOrderRefresh = DateTime.UtcNow;
	}

	private async ValueTask ProcessOrder(KabuStationOrder order, long originId,
		CancellationToken cancellationToken)
	{
		if (order?.Id.IsEmpty() != false)
			return;

		_orders.TryGetValue(order.Id, out var tracker);
		var security = tracker?.Security ?? FindSecurity(order.Symbol, order.Exchange, order.SecurityType);
		var securityId = tracker?.SecurityId ?? security.ToSecurityId();
		var transactionId = tracker?.TransactionId ?? 0;
		var lastDetail = order.Details?.OrderBy(detail => detail.SequenceNumber).LastOrDefault();
		var serverTime = KabuStationExtensions.ParseJapanTime(lastDetail?.TransactionTime)
			?? KabuStationExtensions.ParseJapanTime(order.ReceivedTime)
			?? CurrentTime;
		var state = order.ToOrderState();

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originId,
			TransactionId = transactionId,
			OrderStringId = order.Id,
			SecurityId = securityId,
			PortfolioName = tracker?.PortfolioName.IsEmpty(GetPortfolioName()),
			OrderType = tracker?.OrderType ?? (order.Price is > 0 ? OrderTypes.Limit : OrderTypes.Market),
			Side = order.Side.ToSide(),
			TimeInForce = KabuStationExtensions.ToStockSharpTimeInForce(order.TimeInForce),
			OrderPrice = order.Price ?? 0,
			OrderVolume = order.OrderQuantity,
			Balance = Math.Max(0, (order.OrderQuantity ?? 0) - (order.CumulativeQuantity ?? 0)),
			OrderState = state,
			ServerTime = serverTime,
			Error = state == OrderStates.Failed
				? new InvalidOperationException("kabu Station reported an order-processing error.")
				: null,
			Condition = new KabuStationOrderCondition
			{
				CashMargin = order.CashMargin is { } cash ? (KabuStationCashMargins)cash : null,
				MarginTradeType = order.MarginTradeType is { } margin ? (KabuStationMarginTradeTypes)margin : null,
				AccountType = order.AccountType is { } account ? (KabuStationAccountTypes)account : null,
				ExpireDate = KabuStationExtensions.ParseApiDate(order.ExpireDay),
			},
		}, cancellationToken);

		foreach (var detail in order.Details?.Where(detail => detail.RecordType == 8 && !detail.ExecutionId.IsEmpty()) ?? [])
		{
			if (!_tradeIds.TryAdd($"{originId}:{detail.ExecutionId}"))
				continue;
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = originId,
				OrderStringId = order.Id,
				TradeStringId = detail.ExecutionId,
				SecurityId = securityId,
				PortfolioName = tracker?.PortfolioName.IsEmpty(GetPortfolioName()),
				Side = order.Side.ToSide(),
				TradePrice = detail.Price,
				TradeVolume = detail.Quantity,
				Commission = (detail.Commission ?? 0) + (detail.CommissionTax ?? 0),
				ServerTime = KabuStationExtensions.ParseJapanTime(detail.TransactionTime) ?? serverTime,
			}, cancellationToken);
		}
	}

	private async ValueTask SendPortfolioSnapshot(long originId, CancellationToken cancellationToken)
	{
		var portfolioName = GetPortfolioName();
		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = originId,
			PortfolioName = portfolioName,
			BoardCode = BoardCodes.Tse,
		}, cancellationToken);

		var cash = await TryGetWallet(_rest.GetCashWallet, cancellationToken);
		var margin = await TryGetWallet(_rest.GetMarginWallet, cancellationToken);
		var futures = await TryGetWallet(_rest.GetFutureWallet, cancellationToken);
		var options = await TryGetWallet(_rest.GetOptionWallet, cancellationToken);
		await SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = portfolioName,
			SecurityId = SecurityId.Money,
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, cash?.StockAccountWallet, true)
		.TryAdd(PositionChangeTypes.BuyOrdersMargin, margin?.MarginAccountWallet, true)
		.TryAdd(PositionChangeTypes.VariationMargin, futures?.MarginRequirement, true)
		.TryAdd(PositionChangeTypes.BlockedValue, options?.MarginRequirement, true), cancellationToken);

		foreach (var position in await _rest.GetPositions(cancellationToken))
		{
			var type = position.NativeSecurityType == 0
				? position.Exchange is 2 or 23 or 24 ? SecurityTypes.Future : SecurityTypes.Stock
				: position.NativeSecurityType.ToSecurityType();
			var security = FindSecurity(position.Symbol, position.Exchange, type);
			var quantity = position.HoldQuantity ?? position.LeavesQuantity ?? 0;
			if (position.Side == "1")
				quantity = -quantity;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originId,
				PortfolioName = portfolioName,
				SecurityId = security.ToSecurityId(),
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, quantity, true)
			.TryAdd(PositionChangeTypes.AveragePrice, position.Price, true)
			.TryAdd(PositionChangeTypes.CurrentPrice, position.CurrentPrice, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, position.ProfitLoss, true)
			.TryAdd(PositionChangeTypes.Commission, (position.Commission ?? 0) + (position.CommissionTax ?? 0), true),
				cancellationToken);
		}

		_lastPortfolioRefresh = DateTime.UtcNow;
	}

	private async Task<T> TryGetWallet<T>(Func<CancellationToken, Task<T>> loader,
		CancellationToken cancellationToken) where T : class
	{
		try
		{
			return await loader(cancellationToken);
		}
		catch (KabuStationApiException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or
			HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
		{
			this.AddDebugLog("kabu Station wallet is unavailable: {0}", ex.Message);
			return null;
		}
	}

	private KabuStationSecurityInfo FindSecurity(string symbol, int exchange, SecurityTypes securityType)
	{
		var boardCode = KabuStationExtensions.ToBoardCode(exchange);
		if (_securityInfos.TryGetValue(GetSecurityKey(symbol, boardCode), out var security))
			return security;
		security = new()
		{
			Symbol = symbol,
			Exchange = exchange,
			BoardCode = boardCode,
			SecurityType = securityType,
			NativeSecurityType = securityType.ToNativeSecurityType(),
		};
		CacheSecurity(security);
		return security;
	}

	private string GetPortfolioName()
		=> _portfolioName ??= IsDemo ? "KabuStation-Demo" : nameof(KabuStation);
}
