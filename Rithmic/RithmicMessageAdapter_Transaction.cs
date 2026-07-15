namespace StockSharp.Rithmic;

using Rti;

public partial class RithmicMessageAdapter
{
	private readonly SynchronizedDictionary<string, long> _basketToTransId = [];

	private void OnOrderMessage(int templateId, byte[] data)
	{
		switch (templateId)
		{
			case TemplateId.RithmicOrderNotification:
				ProcessRithmicOrderNotification(data);
				break;
			case TemplateId.ExchangeOrderNotification:
				ProcessExchangeOrderNotification(data);
				break;
			case TemplateId.ResponseNewOrder:
			case TemplateId.ResponseModifyOrder:
			case TemplateId.ResponseCancelOrder:
			case TemplateId.ResponseSubscribeForOrderUpdates:
				break;
			case TemplateId.ResponseHeartbeat:
				break;
			case TemplateId.ForcedLogout:
				this.AddErrorLog("Forced logout from Order Plant");
				break;
			default:
				this.AddDebugLog($"Order msg: template_id={templateId}");
				break;
		}
	}

	private void OnPnLMessage(int templateId, byte[] data)
	{
		switch (templateId)
		{
			case TemplateId.InstrumentPnLPositionUpdate:
				ProcessInstrumentPnLUpdate(data);
				break;
			case TemplateId.AccountPnLPositionUpdate:
				break;
			case TemplateId.ResponseHeartbeat:
				break;
			default:
				this.AddDebugLog($"PnL msg: template_id={templateId}");
				break;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var rq = new RequestNewOrder
		{
			TemplateId = TemplateId.RequestNewOrder,
			FcmId = _fcmId,
			IbId = _ibId,
			AccountId = regMsg.PortfolioName.IsEmpty() ? _accountId : regMsg.PortfolioName,
			Symbol = regMsg.SecurityId.SecurityCode,
			Exchange = regMsg.SecurityId.BoardCode,
			Quantity = (int)regMsg.Volume,
			TransactionType = regMsg.Side.ToTransactionType(),
			Duration = regMsg.TimeInForce.ToDuration(),
			PriceType = (regMsg.OrderType ?? OrderTypes.Limit).ToPriceType(),
			ManualOrAuto = RequestNewOrder.Types.OrderPlacement.Auto,
		};

		if (regMsg.OrderType == OrderTypes.Limit)
			rq.Price = (double)regMsg.Price;

		if (regMsg.OrderType == OrderTypes.Conditional && regMsg.Price != 0)
			rq.TriggerPrice = (double)regMsg.Price;

		if (!_tradeRoute.IsEmpty())
			rq.TradeRoute = _tradeRoute;

		rq.UserMsg.Add(regMsg.TransactionId.To<string>());

		await _orderClient.SendAsync(rq, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var rq = new RequestCancelOrder
		{
			TemplateId = TemplateId.RequestCancelOrder,
			FcmId = _fcmId,
			IbId = _ibId,
			AccountId = cancelMsg.PortfolioName.IsEmpty() ? _accountId : cancelMsg.PortfolioName,
			BasketId = cancelMsg.OrderStringId,
			ManualOrAuto = RequestCancelOrder.Types.OrderPlacement.Auto,
		};
		rq.UserMsg.Add(cancelMsg.TransactionId.To<string>());

		await _orderClient.SendAsync(rq, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		var rq = new RequestModifyOrder
		{
			TemplateId = TemplateId.RequestModifyOrder,
			FcmId = _fcmId,
			IbId = _ibId,
			AccountId = replaceMsg.PortfolioName.IsEmpty() ? _accountId : replaceMsg.PortfolioName,
			BasketId = replaceMsg.OldOrderStringId,
			Quantity = (int)replaceMsg.Volume,
			Price = (double)replaceMsg.Price,
			ManualOrAuto = RequestModifyOrder.Types.OrderPlacement.Auto,
		};
		rq.UserMsg.Add(replaceMsg.TransactionId.To<string>());

		await _orderClient.SendAsync(rq, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);

		if (!statusMsg.IsSubscribe)
			return;

		// subscribe for order updates
		var rq = new RequestSubscribeForOrderUpdates
		{
			TemplateId = TemplateId.RequestSubscribeForOrderUpdates,
			FcmId = _fcmId,
			IbId = _ibId,
			AccountId = _accountId,
		};
		rq.UserMsg.Add(statusMsg.TransactionId.To<string>());

		await _orderClient.SendAsync(rq, cancellationToken);

		// request show orders (snapshots)
		var showRq = new RequestShowOrders
		{
			TemplateId = TemplateId.RequestShowOrders,
			FcmId = _fcmId,
			IbId = _ibId,
			AccountId = _accountId,
		};
		showRq.UserMsg.Add(statusMsg.TransactionId.To<string>());

		await _orderClient.SendAsync(showRq, cancellationToken);

		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		if (!lookupMsg.IsSubscribe)
			return;

		if (!_accountId.IsEmpty())
		{
			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = _accountId,
				OriginalTransactionId = lookupMsg.TransactionId,
			}, cancellationToken);
		}

		// subscribe for PnL updates
		if (_pnlClient?.IsConnected == true)
		{
			var rq = new RequestPnLPositionUpdates
			{
				TemplateId = TemplateId.RequestPnLPositionUpdates,
				Request = RequestPnLPositionUpdates.Types.Request.Subscribe,
				FcmId = _fcmId,
				IbId = _ibId,
				AccountId = _accountId,
			};
			rq.UserMsg.Add(lookupMsg.TransactionId.To<string>());

			await _pnlClient.SendAsync(rq, cancellationToken);
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private async void ProcessRithmicOrderNotification(byte[] data)
	{
		var msg = RithmicOrderNotification.Parser.ParseFrom(data);

		var basketId = msg.HasBasketId ? msg.BasketId : null;
		if (basketId.IsEmpty())
			return;

		long transId;

		if (msg.UserTag.Length > 0 && long.TryParse(msg.UserTag, out var t))
			transId = t;
		else if (!_basketToTransId.TryGetValue(basketId, out transId))
			transId = TransactionIdGenerator.GetNextId();

		if (!_basketToTransId.ContainsKey(basketId))
			_basketToTransId[basketId] = transId;

		var state = msg.HasNotifyType ? msg.NotifyType.ToOrderState() : OrderStates.None;

		var execMsg = new ExecutionMessage
		{
			HasOrderInfo = true,
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = transId,
			OrderStringId = basketId,
			OrderState = state,
			ServerTime = msg.HasSsboe ? msg.Ssboe.ToDateTime(msg.HasUsecs ? msg.Usecs : 0) : CurrentTime,
		};

		if (msg.HasSymbol)
			execMsg.SecurityId = new SecurityId { SecurityCode = msg.Symbol, BoardCode = msg.HasExchange ? msg.Exchange : string.Empty };

		if (msg.HasAccountId)
			execMsg.PortfolioName = msg.AccountId;

		if (msg.HasQuantity)
			execMsg.OrderVolume = msg.Quantity;

		if (msg.HasPrice)
			execMsg.OrderPrice = (decimal)msg.Price;

		if (msg.HasTransactionType)
			execMsg.Side = msg.TransactionType.ToSide();

		if (msg.HasPriceType)
			execMsg.OrderType = msg.PriceType.ToOrderType();

		if (msg.HasTotalUnfilledSize)
			execMsg.Balance = msg.TotalUnfilledSize;

		if (msg.HasAvgFillPrice)
			execMsg.AveragePrice = (decimal)msg.AvgFillPrice;

		if (state == OrderStates.Failed)
			execMsg.Error = new InvalidOperationException(msg.HasText ? msg.Text : "Order failed");

		await SendOutMessageAsync(execMsg, CancellationToken.None);
	}

	private async void ProcessExchangeOrderNotification(byte[] data)
	{
		var msg = ExchangeOrderNotification.Parser.ParseFrom(data);

		var basketId = msg.HasBasketId ? msg.BasketId : null;
		if (basketId.IsEmpty())
			return;

		if (!_basketToTransId.TryGetValue(basketId, out var transId))
		{
			transId = TransactionIdGenerator.GetNextId();
			_basketToTransId[basketId] = transId;
		}

		var time = msg.HasSsboe ? msg.Ssboe.ToDateTime(msg.HasUsecs ? msg.Usecs : 0) : CurrentTime;

		// Process fill
		if (msg.HasNotifyType && msg.NotifyType == ExchangeOrderNotification.Types.NotifyType.Fill)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = transId,
				OrderStringId = basketId,
				ServerTime = time,
				TradePrice = msg.HasFillPrice ? (decimal)msg.FillPrice : null,
				TradeVolume = msg.HasFillSize ? msg.FillSize : null,
				TradeStringId = msg.HasFillId ? msg.FillId : null,
			}, CancellationToken.None);

			// also send balance update
			if (msg.HasTotalUnfilledSize)
			{
				await SendOutMessageAsync(new ExecutionMessage
				{
					HasOrderInfo = true,
					DataTypeEx = DataType.Transactions,
					OriginalTransactionId = transId,
					OrderStringId = basketId,
					ServerTime = time,
					Balance = msg.TotalUnfilledSize,
					OrderState = msg.TotalUnfilledSize == 0 ? OrderStates.Done : OrderStates.Active,
				}, CancellationToken.None);
			}
		}
		else if (msg.HasNotifyType && msg.NotifyType == ExchangeOrderNotification.Types.NotifyType.Reject)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				HasOrderInfo = true,
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = transId,
				OrderStringId = basketId,
				ServerTime = time,
				OrderState = OrderStates.Failed,
				Error = new InvalidOperationException(msg.HasText ? msg.Text : "Order rejected"),
			}, CancellationToken.None);
		}
		else if (msg.HasNotifyType && msg.NotifyType == ExchangeOrderNotification.Types.NotifyType.Cancel)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				HasOrderInfo = true,
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = transId,
				OrderStringId = basketId,
				ServerTime = time,
				OrderState = OrderStates.Done,
				Balance = 0,
			}, CancellationToken.None);
		}
	}

	private async void ProcessInstrumentPnLUpdate(byte[] data)
	{
		var msg = InstrumentPnLPositionUpdate.Parser.ParseFrom(data);

		if (!msg.HasSymbol || !msg.HasAccountId)
			return;

		var secId = new SecurityId
		{
			SecurityCode = msg.Symbol,
			BoardCode = msg.HasExchange ? msg.Exchange : string.Empty,
		};

		var posMsg = new PositionChangeMessage
		{
			SecurityId = secId,
			PortfolioName = msg.AccountId,
			ServerTime = msg.HasSsboe ? msg.Ssboe.ToDateTime(msg.HasUsecs ? msg.Usecs : 0) : CurrentTime,
		};

		if (msg.HasBuyQty && msg.HasSellQty)
			posMsg.TryAdd(PositionChangeTypes.CurrentValue, (decimal)(msg.BuyQty - msg.SellQty), true);

		if (msg.HasAvgOpenFillPrice)
			posMsg.TryAdd(PositionChangeTypes.AveragePrice, (decimal)msg.AvgOpenFillPrice, true);

		if (msg.HasDayPnl)
			posMsg.TryAdd(PositionChangeTypes.RealizedPnL, (decimal)msg.DayPnl, true);

		if (msg.HasDayOpenPnl)
			posMsg.TryAdd(PositionChangeTypes.UnrealizedPnL, (decimal)msg.DayOpenPnl, true);

		await SendOutMessageAsync(posMsg, CancellationToken.None);
	}
}
