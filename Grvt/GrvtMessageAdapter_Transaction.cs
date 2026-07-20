namespace StockSharp.Grvt;

public partial class GrvtMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady(true);
		ValidatePortfolio(regMsg.PortfolioName);
		var instrument = GetInstrument(GetInstrumentCode(regMsg.SecurityId));
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException(
				"GRVT order volume must be positive.");
		var minimumVolume = instrument.MinimumSize.ParseRequiredDecimal(
			"minimum order size");
		if (volume < minimumVolume || volume % minimumVolume != 0)
			throw new InvalidOperationException(
				$"GRVT order volume must be a multiple of {minimumVolume}.");
		if (instrument.MaximumPositionSize.ToDecimal() is decimal maximum &&
			volume > maximum)
			throw new InvalidOperationException(
				$"GRVT order volume exceeds the maximum {maximum}.");

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market or
			OrderTypes.Conditional))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		var condition = regMsg.Condition as GrvtOrderCondition ?? new();
		var isConditional = orderType == OrderTypes.Conditional;
		if (isConditional && (condition.TriggerType == GrvtTriggerTypes.None ||
			condition.ActivationPrice is not > 0))
			throw new InvalidOperationException(
				"A GRVT conditional order requires a TP/SL type and a " +
				"positive activation price.");
		if (!isConditional && condition.TriggerType != GrvtTriggerTypes.None)
			throw new InvalidOperationException(
				"Use the conditional StockSharp order type for a GRVT trigger.");
		var isMarket = orderType == OrderTypes.Market ||
			isConditional && condition.IsMarket;
		if (!isMarket && regMsg.Price <= 0)
			throw new InvalidOperationException(
				"A positive GRVT limit price is required.");
		var priceStep = instrument.TickSize.ParseRequiredDecimal("tick size");
		if (!isMarket && regMsg.Price % priceStep != 0)
			throw new InvalidOperationException(
				$"GRVT order price must be a multiple of {priceStep}.");
		if (!isMarket && instrument.MinimumNotional.ToDecimal() is decimal minimum &&
			regMsg.Price * volume < minimum)
			throw new InvalidOperationException(
				$"GRVT order notional must be at least {minimum} " +
				$"{instrument.Quote}.");

		var timeInForce = regMsg.TimeInForce.ToGrvt(isMarket);
		if (regMsg.PostOnly == true && (isMarket ||
			timeInForce != GrvtTimeInForces.GoodTillTime))
			throw new InvalidOperationException(
				"A GRVT post-only order must be a GTT limit order.");
		var clientOrderId = GrvtExtensions.CreateClientOrderId(
			regMsg.TransactionId, regMsg.UserOrderId);
		var order = new GrvtOrder
		{
			SubAccountId = RestClient.SubAccountId,
			IsMarket = isMarket,
			TimeInForce = timeInForce,
			IsPostOnly = regMsg.PostOnly == true,
			IsReduceOnly = condition.IsReduceOnly,
			Legs =
			[
				new()
				{
					Instrument = instrument.Instrument,
					Size = volume.ToWire(),
					LimitPrice = isMarket ? null : regMsg.Price.ToWire(),
					IsBuyingAsset = regMsg.Side == Sides.Buy,
				},
			],
			Metadata = new()
			{
				ClientOrderId = clientOrderId,
				Trigger = isConditional
					? new()
					{
						TriggerType = condition.TriggerType.ToNative(),
						TakeProfitStopLoss = new()
						{
							TriggerBy = condition.TriggerBy.ToNative(),
							TriggerPrice = condition.ActivationPrice.Value
								.ToWire(),
							IsClosePosition = condition.IsClosePosition,
							IsSplitPosition = false,
						},
					}
					: null,
			},
		};
		var expiration = GetOrderExpiration(regMsg.TillDate, timeInForce);
		order.Signature = _signer.SignOrder(order, instrument, expiration);
		var created = await RestClient.CreateOrderAsync(new() { Order = order },
			cancellationToken);
		if (created?.OrderId.IsEmpty() != false)
			throw new InvalidDataException(
				"GRVT accepted an order without returning an order ID.");
		await SendOrderAsync(created, regMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
		=> throw new NotSupportedException(
			"GRVT does not expose a single-order amendment endpoint. " +
			"Cancel the active order and submit a newly signed order.");

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady(false);
		ValidatePortfolio(cancelMsg.PortfolioName);
		var orderId = cancelMsg.OrderStringId;
		string clientOrderId = null;
		if (orderId.IsEmpty() && cancelMsg.OriginalTransactionId > 0)
			clientOrderId = GrvtExtensions.CreateClientOrderId(
				cancelMsg.OriginalTransactionId, null);
		if (orderId.IsEmpty() && clientOrderId.IsEmpty())
			throw new InvalidOperationException(
				"GRVT cancellation requires an exchange order ID or the " +
				"original transaction ID.");
		var result = await RestClient.CancelOrderAsync(new()
		{
			SubAccountId = RestClient.SubAccountId,
			OrderId = orderId,
			ClientOrderId = clientOrderId,
			TimeToLiveMilliseconds = "5000",
		}, cancellationToken);
		if (result?.IsAcknowledged != true)
			throw new InvalidDataException(
				"GRVT did not acknowledge the cancellation request.");
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady(false);
		ValidatePortfolio(cancelMsg.PortfolioName);
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"GRVT bulk cancellation does not close positions.");
		if (cancelMsg.Side is not null)
			throw new NotSupportedException(
				"GRVT bulk cancellation cannot be filtered by order side.");
		GrvtInstrument instrument = null;
		if (!cancelMsg.SecurityId.SecurityCode.IsEmpty())
			instrument = GetInstrument(GetInstrumentCode(cancelMsg.SecurityId));
		var result = await RestClient.CancelAllOrdersAsync(new()
		{
			SubAccountId = RestClient.SubAccountId,
			Kinds = instrument is null ? null : [instrument.Kind],
			Base = instrument is null ? null : [instrument.Base],
			Quote = instrument is null ? null : [instrument.Quote],
		}, cancellationToken);
		if (result?.IsAcknowledged != true)
			throw new InvalidDataException(
				"GRVT did not acknowledge the bulk cancellation request.");
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsurePrivateReady(false);
		if (!lookupMsg.IsSubscribe)
		{
			_portfolioSubscriptionId = 0;
			return;
		}
		_portfolioSubscriptionId = lookupMsg.TransactionId;
		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = PortfolioName,
			BoardCode = BoardCodes.Grvt,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId,
			cancellationToken);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId,
			cancellationToken);
		EnsurePrivateReady(false);
		if (!statusMsg.IsSubscribe)
		{
			_orderStatusSubscriptionId = 0;
			return;
		}
		ValidatePortfolio(statusMsg.PortfolioName);
		await SendOrderSnapshotAsync(statusMsg.TransactionId, statusMsg,
			statusMsg.Count, cancellationToken);
		_orderStatusSubscriptionId = statusMsg.TransactionId;
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private DateTime GetOrderExpiration(DateTime? tillDate,
		GrvtTimeInForces timeInForce)
	{
		var now = ServerTime;
		var expiration = tillDate?.ToUniversalTime() ??
			(timeInForce == GrvtTimeInForces.GoodTillTime
				? now.AddDays(7)
				: now.AddMinutes(5));
		if (expiration <= now)
			throw new InvalidOperationException(
				"GRVT order expiration must be in the future.");
		if (expiration > now.AddDays(30))
			throw new InvalidOperationException(
				"GRVT order signatures cannot expire more than 30 days ahead.");
		return expiration;
	}

	private async ValueTask SendPortfolioSnapshotAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		var request = new GrvtSubAccountSummaryRequest
		{
			SubAccountId = RestClient.SubAccountId,
		};
		var summary = await RestClient.GetSubAccountSummaryAsync(request,
			cancellationToken);
		await SendSubAccountAsync(summary, transactionId, cancellationToken);
		var positions = await RestClient.GetPositionsAsync(new()
		{
			SubAccountId = RestClient.SubAccountId,
		}, cancellationToken) ?? [];
		foreach (var position in positions)
			await SendPositionAsync(position, transactionId, cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(long transactionId,
		OrderStatusMessage statusMsg, long? requestedCount,
		CancellationToken cancellationToken)
	{
		GrvtInstrument instrument = null;
		if (statusMsg?.SecurityId.SecurityCode.IsEmpty() == false)
			instrument = GetInstrument(GetInstrumentCode(statusMsg.SecurityId));
		var openOrders = await RestClient.GetOpenOrdersAsync(new()
		{
			SubAccountId = RestClient.SubAccountId,
			Kinds = instrument is null ? null : [instrument.Kind],
			Base = instrument is null ? null : [instrument.Base],
			Quote = instrument is null ? null : [instrument.Quote],
		}, cancellationToken) ?? [];
		var maximum = GetHistoryLimit(requestedCount, 1000);
		var history = new List<GrvtOrder>();
		string cursor = null;
		do
		{
			var page = await RestClient.GetOrderHistoryAsync(new()
			{
				SubAccountId = RestClient.SubAccountId,
				Kinds = instrument is null ? null : [instrument.Kind],
				Base = instrument is null ? null : [instrument.Base],
				Quote = instrument is null ? null : [instrument.Quote],
				StartTime = statusMsg?.From?.ToUniversalTime()
					.ToGrvtNanoseconds(),
				EndTime = statusMsg?.To?.ToUniversalTime()
					.ToGrvtNanoseconds(),
				Limit = (maximum - history.Count).Min(1000).Max(1),
				Cursor = cursor,
			}, cancellationToken);
			history.AddRange(page?.Result ?? []);
			cursor = page?.Next;
		}
		while (!cursor.IsEmpty() && history.Count < maximum);

		foreach (var order in openOrders.Concat(history)
			.Where(static item => item?.OrderId.IsEmpty() == false)
			.GroupBy(static item => item.OrderId,
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.First())
			.OrderBy(static item => item.State?.UpdateTime.ToGrvtTime() ??
				DateTime.MinValue)
			.Take(maximum))
			await SendOrderAsync(order, transactionId, cancellationToken);

		var fills = new List<GrvtFill>();
		cursor = null;
		do
		{
			var page = await RestClient.GetFillHistoryAsync(new()
			{
				SubAccountId = RestClient.SubAccountId,
				Kinds = instrument is null ? null : [instrument.Kind],
				Base = instrument is null ? null : [instrument.Base],
				Quote = instrument is null ? null : [instrument.Quote],
				StartTime = statusMsg?.From?.ToUniversalTime()
					.ToGrvtNanoseconds(),
				EndTime = statusMsg?.To?.ToUniversalTime()
					.ToGrvtNanoseconds(),
				Limit = (maximum - fills.Count).Min(1000).Max(1),
				Cursor = cursor,
			}, cancellationToken);
			fills.AddRange(page?.Result ?? []);
			cursor = page?.Next;
		}
		while (!cursor.IsEmpty() && fills.Count < maximum);
		foreach (var fill in fills
			.Where(static item => item?.TradeId.IsEmpty() == false)
			.GroupBy(static item => item.TradeId,
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.First())
			.OrderBy(static item => item.EventTime.ToGrvtTime())
			.Take(maximum))
			await SendFillAsync(fill, transactionId, cancellationToken);
	}

	private ValueTask OnOrderAsync(string selector, GrvtOrder order,
		CancellationToken cancellationToken)
	{
		_ = selector;
		return _orderStatusSubscriptionId == 0
			? default
			: SendOrderAsync(order, _orderStatusSubscriptionId,
				cancellationToken);
	}

	private ValueTask OnFillAsync(string selector, GrvtFill fill,
		CancellationToken cancellationToken)
	{
		_ = selector;
		return _orderStatusSubscriptionId == 0
			? default
			: SendFillAsync(fill, _orderStatusSubscriptionId,
				cancellationToken);
	}

	private ValueTask OnPositionAsync(string selector, GrvtPosition position,
		CancellationToken cancellationToken)
	{
		_ = selector;
		return _portfolioSubscriptionId == 0
			? default
			: SendPositionAsync(position, _portfolioSubscriptionId,
				cancellationToken);
	}

	private async ValueTask SendSubAccountAsync(GrvtSubAccount account,
		long transactionId, CancellationToken cancellationToken)
	{
		if (account?.SubAccountId.IsEmpty() != false)
			return;
		var message = this.CreatePortfolioChangeMessage(PortfolioName);
		message.ServerTime = account.EventTime.ToGrvtTime();
		message.OriginalTransactionId = transactionId;
		message
			.TryAdd(PositionChangeTypes.CurrentValue,
				account.TotalEquity.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.BlockedValue,
				account.InitialMargin.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL,
				account.UnrealizedPnl.ToDecimal(), true);
		await SendOutMessageAsync(message, cancellationToken);

		foreach (var balance in account.SpotBalances ?? [])
		{
			if (balance?.Currency.IsEmpty() != false)
				continue;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = PortfolioName,
				SecurityId = balance.Currency.ToStockSharp(),
				ServerTime = account.EventTime.ToGrvtTime(),
				OriginalTransactionId = transactionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue,
				balance.Balance.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.CurrentPrice,
				balance.IndexPrice.ToDecimal(), true), cancellationToken);
		}
	}

	private ValueTask SendPositionAsync(GrvtPosition position,
		long transactionId, CancellationToken cancellationToken)
	{
		if (position?.Instrument.IsEmpty() != false)
			return default;
		var size = position.Size.ParseRequiredDecimal("position size");
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = PortfolioName,
			SecurityId = position.Instrument.ToStockSharp(),
			ServerTime = position.EventTime.ToGrvtTime(),
			OriginalTransactionId = transactionId,
			Side = size == 0 ? null : size > 0 ? Sides.Buy : Sides.Sell,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, size.Abs(), true)
		.TryAdd(PositionChangeTypes.AveragePrice,
			position.EntryPrice.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.CurrentPrice,
			position.MarkPrice.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL,
			position.UnrealizedPnl.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.RealizedPnL,
			position.RealizedPnl.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.Leverage,
			position.Leverage.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.LiquidationPrice,
			position.EstimatedLiquidationPrice.ToDecimal(), true),
			cancellationToken);
	}

	private ValueTask SendOrderAsync(GrvtOrder order, long transactionId,
		CancellationToken cancellationToken)
	{
		if (order?.Legs is not { Length: 1 } || order.Metadata is null)
		{
			this.AddWarningLog(
				"A multi-leg or metadata-free GRVT order cannot be represented " +
				"by the StockSharp single-security order model.");
			return default;
		}
		var leg = order.Legs[0];
		var volume = leg.Size.ToDecimal();
		var traded = order.State?.TradedSize?.FirstOrDefault().ToDecimal() ?? 0m;
		var balance = order.State?.BookSize?.FirstOrDefault().ToDecimal() ??
			(volume is decimal total ? (total - traded).Max(0m) : null);
		var state = order.State?.Status.ToStockSharp() ?? OrderStates.Active;
		var trigger = order.Metadata.Trigger;
		var condition = new GrvtOrderCondition
		{
			TriggerType = trigger?.TriggerType.ToStockSharp() ??
				GrvtTriggerTypes.None,
			TriggerBy = trigger?.TakeProfitStopLoss?.TriggerBy.ToStockSharp() ??
				GrvtTriggerPrices.Mark,
			ActivationPrice = trigger?.TakeProfitStopLoss?.TriggerPrice.ToDecimal(),
			IsMarket = order.IsMarket == true,
			IsClosePosition = trigger?.TakeProfitStopLoss?.IsClosePosition == true,
			IsReduceOnly = order.IsReduceOnly == true,
		};
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = leg.Instrument.ToStockSharp(),
			ServerTime = order.State?.UpdateTime.IsEmpty() == false
				? order.State.UpdateTime.ToGrvtTime()
				: ServerTime,
			PortfolioName = PortfolioName,
			Side = leg.IsBuyingAsset ? Sides.Buy : Sides.Sell,
			OrderVolume = volume,
			Balance = balance,
			OrderPrice = leg.LimitPrice.ToDecimal() ?? 0m,
			AveragePrice = order.State?.AverageFillPrice?.FirstOrDefault()
				.ToDecimal(),
			OrderType = trigger is not null
				? OrderTypes.Conditional
				: order.IsMarket == true ? OrderTypes.Market : OrderTypes.Limit,
			OrderState = state,
			OrderStringId = order.OrderId,
			TransactionId = order.Metadata.ClientOrderId.ToTransactionId(),
			OriginalTransactionId = transactionId,
			TimeInForce = order.TimeInForce.ToStockSharp(),
			PostOnly = order.IsPostOnly,
			Condition = condition,
			Error = state == OrderStates.Failed
				? new InvalidOperationException(
					$"GRVT rejected the order: {order.State.RejectReason}.")
				: null,
		}, cancellationToken);
	}

	private ValueTask SendFillAsync(GrvtFill fill, long transactionId,
		CancellationToken cancellationToken)
	{
		if (fill?.Instrument.IsEmpty() != false)
			return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = fill.Instrument.ToStockSharp(),
			ServerTime = fill.EventTime.ToGrvtTime(),
			PortfolioName = PortfolioName,
			Side = fill.IsBuyer ? Sides.Buy : Sides.Sell,
			OrderStringId = fill.OrderId,
			TradeStringId = fill.TradeId,
			TradePrice = fill.Price.ToDecimal(),
			TradeVolume = fill.Size.ToDecimal(),
			Commission = fill.Fee.ToDecimal(),
			TransactionId = fill.ClientOrderId.ToTransactionId(),
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}
}
