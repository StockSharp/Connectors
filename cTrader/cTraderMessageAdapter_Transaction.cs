namespace StockSharp.cTrader;

public partial class cTraderMessageAdapter
{
	private readonly SynchronizedDictionary<long, long> _posValues = new();

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var condition = (cTraderOrderCondition)regMsg.Condition;

		var req = new ProtoOANewOrderReq
		{
			CtidTraderAccountId = regMsg.PortfolioName.ToAccountId(),
			ClientOrderId = regMsg.TransactionId.To<string>(),
			TradeSide = regMsg.Side.ToNative(),
			OrderType = regMsg.OrderType.ToNative(condition),
			Volume = regMsg.Volume.ToMonetary(),
			SymbolId = regMsg.SecurityId.NativeAsInt,
		};

		if (regMsg.Price > 0)
			req.LimitPrice = (double)regMsg.Price;

		if (regMsg.Slippage is decimal slippage)
			req.BaseSlippagePrice = (double)slippage;

		if (!regMsg.Comment.IsEmpty())
			req.Comment = regMsg.Comment;

		if (condition?.TakeProfit is decimal takeProfit)
			req.TakeProfit = (double)takeProfit;

		if (condition?.StopLoss is decimal stopLoss)
			req.StopLoss = (double)stopLoss;

		if (regMsg.TillDate is DateTime expiration)
			req.ExpirationTimestamp = (long)expiration.ToUnix(false);

		if (regMsg.TimeInForce is TimeInForce tif)
			req.TimeInForce = tif.ToNative();

		await _client.SendMessage(req, regMsg.TransactionId.To<string>(), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		if (replaceMsg.OldOrderId is null)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(replaceMsg.OriginalTransactionId));

		var condition = (cTraderOrderCondition)replaceMsg.Condition;

		var req = new ProtoOAAmendOrderReq
		{
			CtidTraderAccountId = replaceMsg.PortfolioName.ToAccountId(),
			OrderId = replaceMsg.OldOrderId.Value,
			Volume = replaceMsg.Volume.ToMonetary(),
		};

		if (replaceMsg.Price > 0)
			req.LimitPrice = (double)replaceMsg.Price;

		if (condition?.TakeProfit is decimal takeProfit)
			req.TakeProfit = (double)takeProfit;

		if (condition?.StopLoss is decimal stopLoss)
			req.StopLoss = (double)stopLoss;

		if (replaceMsg.TillDate is DateTime expiration)
			req.ExpirationTimestamp = (long)expiration.ToUnix(false);

		await _client.SendMessage(req, replaceMsg.TransactionId.To<string>(), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		if (cancelMsg.OrderId is null)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		var req = new ProtoOACancelOrderReq
		{
			CtidTraderAccountId = cancelMsg.PortfolioName.ToAccountId(),
			OrderId = cancelMsg.OrderId.Value,
		};

		await _client.SendMessage(req, cancelMsg.TransactionId.To<string>(), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		if (lookupMsg is null)
			throw new ArgumentNullException(nameof(lookupMsg));

		if (lookupMsg.IsSubscribe)
		{
			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = lookupMsg.PortfolioName.IsEmpty(_accountId.ToPortfolioName()),
				OriginalTransactionId = lookupMsg.TransactionId,
			}, cancellationToken);

			await _client.SendMessage(new ProtoOAReconcileReq
			{
				CtidTraderAccountId = lookupMsg.PortfolioName.TryToAccountId() ?? _accountId,
			}, lookupMsg.TransactionId.To<string>(), cancellationToken);
		}
		else
		{
			await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		if (statusMsg is null)
			throw new ArgumentNullException(nameof(statusMsg));

		if (statusMsg.IsSubscribe)
		{
			var now = DateTime.UtcNow;

			var req = new ProtoOAOrderListReq
			{
				CtidTraderAccountId = statusMsg.PortfolioName.TryToAccountId() ?? _accountId,
				FromTimestamp = (long)(statusMsg.From ?? now.AddDays(-1)).ToUnix(false),
				ToTimestamp = (long)(statusMsg.To ?? now).ToUnix(false)
			};

			_subscriptions[statusMsg.TransactionId] = (statusMsg.TypedClone(), true);
			await _client.SendMessage(req, statusMsg.TransactionId.To<string>(), cancellationToken);
		}
		else
		{
			await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		}
	}

	private async Task OnOrderListResponse(long transId, ProtoOAOrderListRes msg)
	{
		if (!_subscriptions.TryGetValue(transId, out var t))
			return;

		if (t.isFirstTime)
		{
			await SendSubscriptionReplyAsync(transId, CancellationToken.None);

			_subscriptions[transId] = (t.subscription, false);
		}

		var statusMsg = (OrderStatusMessage)t.subscription;
		var left = statusMsg.Count ?? long.MaxValue;
		var pfName = msg.CtidTraderAccountId.ToPortfolioName();

		foreach (var order in msg.Order)
		{
			if (!long.TryParse(order.ClientOrderId, out var orderTransId))
				continue;

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = transId,
				SecurityId = new() { NativeAsInt = order.TradeData.SymbolId },
				ServerTime = order.TradeData.OpenTimestamp.FromUnix(false),
				TransactionId = orderTransId,
				PortfolioName = pfName,
				HasOrderInfo = true,
				OrderId = order.OrderId,
				Balance = order.HasExecutedVolume && order.TradeData.HasVolume ? (order.TradeData.Volume - order.ExecutedVolume).FromMonetary() : null,
				OrderVolume = order.TradeData.HasVolume ? order.TradeData.Volume.FromMonetary() : null,
				Side = order.TradeData.TradeSide.FromNative(),
				OrderType = order.HasOrderType ? order.OrderType.FromNative() : null,
				OrderPrice = order.HasLimitPrice ? (decimal)order.LimitPrice : 0,
				OrderState = order.HasOrderStatus ? order.OrderStatus.FromNative() : null,
				AveragePrice = order.HasExecutionPrice ? (decimal)order.ExecutionPrice : null,
				Comment = order.TradeData.HasComment ? order.TradeData.Comment : null,
				ExpiryDate = order.HasExpirationTimestamp ? order.ExpirationTimestamp.FromUnix(false) : null,
				TimeInForce = order.TimeInForce.FromNative(),
				Condition = new cTraderOrderCondition
				{
					StopLoss = order.HasStopLoss ? (decimal)order.StopLoss : null,
					TakeProfit = order.HasTakeProfit ? (decimal)order.TakeProfit : null,
				},
			}, CancellationToken.None);

			if (--left <= 0)
				break;
		}

		if (statusMsg.Count is not null)
			statusMsg.Count = left;

		// TODO
		//if (msg.HasMore)
		//	return;

		_subscriptions.Remove(transId);
		await SendSubscriptionOnlineAsync(transId, CancellationToken.None);
	}

	private async Task OnOrderErrorEvent(long transId, ProtoOAOrderErrorEvent msg)
	{
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = transId,
			HasOrderInfo = true,
			OrderState = OrderStates.Failed,
			Error = new InvalidOperationException(msg.Description),
		}, CancellationToken.None);
	}

	private async Task OnExecutionEvent(long transId, ProtoOAExecutionEvent msg)
	{
		if (msg.Deal is ProtoOADeal deal)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = transId,
				SecurityId = new() { NativeAsInt = deal.SymbolId },
				ServerTime = deal.HasCreateTimestamp ? deal.CreateTimestamp.FromUnix(false) : deal.UtcLastUpdateTimestamp.FromUnix(false),
				HasOrderInfo = true,
				OrderId = deal.OrderId.DefaultAsNull(),
				OrderState = deal.HasDealStatus ? deal.DealStatus.FromNative() : null,
				Balance = deal.HasVolume && deal.HasFilledVolume ? (deal.Volume - deal.FilledVolume).FromMonetary() : null,
				TradeId = deal.DealId.DefaultAsNull(),
				Commission = deal.HasCommission ? deal.Commission.FromMonetary() : null,
				TradePrice = deal.HasExecutionPrice ? (decimal)deal.ExecutionPrice : null,
				TradeVolume = deal.HasVolume ? deal.Volume.FromMonetary() : null,
			}, CancellationToken.None);
		}

		if (msg.Order is ProtoOAOrder order)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = transId,
				SecurityId = new SecurityId { NativeAsInt = order.TradeData.SymbolId },
				ServerTime = order.HasUtcLastUpdateTimestamp ? order.UtcLastUpdateTimestamp.FromUnix(false) : DateTime.UtcNow,
				HasOrderInfo = true,
				OrderId = order.HasOrderId ? order.OrderId : null,
				OrderState = order.HasOrderStatus ? order.OrderStatus.FromNative() : null,
				Balance = order.TradeData.HasVolume && order.HasExecutedVolume ? (order.TradeData.Volume - order.ExecutedVolume).FromMonetary() : null,
				OrderType = order.HasOrderType ? order.OrderType.FromNative() : null,
				Side = order.TradeData.HasTradeSide ? order.TradeData.TradeSide.FromNative() : default,
				TimeInForce = order.HasTimeInForce ? order.TimeInForce.FromNative() : null,
				Comment = order.TradeData.HasComment ? order.TradeData.Comment : null,
			}, CancellationToken.None);
		}

		if (msg.Position is ProtoOAPosition pos)
		{
			var symbol = pos.TradeData.SymbolId;

			var currValue = _posValues.TryGetValue(symbol) + pos.GetPosSize();
			_posValues[symbol] = currValue;

			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = msg.CtidTraderAccountId.ToPortfolioName(),
				SecurityId = new SecurityId { NativeAsInt = symbol },
				ServerTime = pos.UtcLastUpdateTimestamp.DefaultAsNull()?.FromUnix(false) ?? DateTime.UtcNow,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, currValue.FromMonetary())
			.TryAdd(PositionChangeTypes.Commission, pos.Commission.FromMonetary())
			.TryAdd(PositionChangeTypes.AveragePrice, pos.Price.ToDecimal())
			, CancellationToken.None);
		}

		if (msg.HasErrorCode)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = transId,
				HasOrderInfo = true,
				OrderState = OrderStates.Failed,
				Error = new InvalidOperationException(msg.ErrorCode),
			}, CancellationToken.None);
		}
	}

	private async Task OnGetPositionUnrealizedPnlResponse(long transId, ProtoOAGetPositionUnrealizedPnLRes msg)
	{
		var now = DateTime.UtcNow;

		foreach (var pnl in msg.PositionUnrealizedPnL)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = msg.CtidTraderAccountId.ToPortfolioName(),
				SecurityId = SecurityId.Money,
				OriginalTransactionId = transId,
				ServerTime = now,
			}
			.TryAdd(PositionChangeTypes.UnrealizedPnL, pnl.NetUnrealizedPnL.FromMonetary())
			, CancellationToken.None);
		}
	}

	private async Task OnReconcileResponse(long transId, ProtoOAReconcileRes msg)
	{
		await SendSubscriptionReplyAsync(transId, CancellationToken.None);

		var pfName = msg.CtidTraderAccountId.ToPortfolioName();

		foreach (var g in msg.Position
			.Where(p => p.PositionStatus == ProtoOAPositionStatus.PositionStatusOpen)
			.GroupBy(p => p.TradeData.SymbolId))
		{
			var lastTime = g.Select(p => p.UtcLastUpdateTimestamp).Where(t => t != 0).OrderByDescending().FirstOr();

			var volume = g.Sum(p => p.GetPosSize());

			_posValues[g.Key] = volume;

			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = pfName,
				SecurityId = new() { NativeAsInt = g.Key },
				OriginalTransactionId = transId,
				ServerTime = lastTime?.FromUnix(false) ?? DateTime.UtcNow,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, volume.FromMonetary(), true)
			.TryAdd(PositionChangeTypes.Commission, g.Sum(p => p.Commission).FromMonetary())
			.TryAdd(PositionChangeTypes.AveragePrice, volume == 0 ? null : (g.Average(p => p.Price * p.TradeData.Volume) / volume).ToDecimal())
			.TryAdd(PositionChangeTypes.OrdersMargin, g.Sum(p => (long)p.UsedMargin).FromMonetary())
			, CancellationToken.None);
		}

		await SendSubscriptionOnlineAsync(transId, CancellationToken.None);
	}

	private void OnDealOffsetListResponse(ProtoOADealOffsetListRes msg) { }
	private void OnOrderListByPositionIdResponse(ProtoOAOrderListByPositionIdRes msg) { }
	private void OnOrderDetailsResponse(ProtoOAOrderDetailsRes msg) { }
	private void OnDealListByPositionIdResponse(ProtoOADealListByPositionIdRes msg) { }
	private void OnGetDynamicLeverageResponse(ProtoOAGetDynamicLeverageByIDRes msg) { }
	private void OnMarginCallTriggerEvent(ProtoOAMarginCallTriggerEvent msg) { }
	private void OnMarginCallUpdateEvent(ProtoOAMarginCallUpdateEvent msg) { }
	private void OnMarginCallUpdateResponse(ProtoOAMarginCallUpdateRes msg) { }
	private void OnMarginCallListResponse(ProtoOAMarginCallListRes msg) { }
	private void OnMarginChangedEvent(ProtoOAMarginChangedEvent msg) { }
	private void OnExpectedMarginResponse(ProtoOAExpectedMarginRes msg) { }
	private void OnTrailingSlChangedEvent(ProtoOATrailingSLChangedEvent msg) { }
	private void OnDealListResponse(ProtoOADealListRes msg) { }
	private void OnTraderUpdateEvent(ProtoOATraderUpdatedEvent msg) { }
	private void OnTraderResponse(ProtoOATraderRes msg) { }
	private void OnCashFlowHistoryListResponse(ProtoOACashFlowHistoryListRes msg) { }
}