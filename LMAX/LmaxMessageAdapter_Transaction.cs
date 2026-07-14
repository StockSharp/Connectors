namespace StockSharp.LMAX;

partial class LmaxMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var transactionId = regMsg.TransactionId.To<string>();
		var instrumentId = GetInstrumentId(regMsg.SecurityId);

		var condition = regMsg.Condition as LmaxOrderCondition;
		var hasStopPrice = condition?.StopPrice != null;

		var request = new PlaceOrderRequest
		{
			InstructionId = transactionId,
			InstrumentId = instrumentId,
			Type = regMsg.OrderType.ToLmax(hasStopPrice),
			Side = regMsg.Side.ToLmax(),
			Quantity = regMsg.Volume.ToString(),
			Price = regMsg.Price.DefaultAsNull()?.ToString(),
			StopPrice = condition?.StopPrice?.ToString(),
			TimeInForce = regMsg.TimeInForce.ToLmax(regMsg.TillDate),
		};

		var response = await _httpClient.PlaceOrderAsync(request, cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = regMsg.SecurityId,
			OriginalTransactionId = regMsg.TransactionId,
			OrderStringId = response.OrderId,
			OrderState = OrderStates.Active,
			ServerTime = response.Timestamp,
			HasOrderInfo = true,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var instrumentId = GetInstrumentId(cancelMsg.SecurityId);

		var request = new CancelOrderRequest
		{
			CancelInstructionId = cancelMsg.TransactionId.To<string>(),
			InstructionId = cancelMsg.OriginalTransactionId.To<string>(),
			InstrumentId = instrumentId,
		};

		var response = await _httpClient.CancelOrderAsync(request, cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = cancelMsg.SecurityId,
			OriginalTransactionId = cancelMsg.TransactionId,
			OrderStringId = response.OrderId,
			OrderState = OrderStates.Done,
			ServerTime = response.Timestamp,
			HasOrderInfo = true,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var request = new CancelAllOrdersRequest
		{
			CancelInstructionId = cancelMsg.TransactionId.To<string>(),
		};

		await _httpClient.CancelAllOrdersAsync(request, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		var instrumentId = GetInstrumentId(replaceMsg.SecurityId);

		var request = new CancelAndReplaceOrderRequest
		{
			CancelInstructionId = replaceMsg.TransactionId.To<string>(),
			InstructionId = replaceMsg.OriginalTransactionId.To<string>(),
			InstrumentId = instrumentId,
			Quantity = replaceMsg.Volume.ToString(),
			Price = replaceMsg.Price.DefaultAsNull()?.ToString(),
		};

		var response = await _httpClient.CancelAndReplaceOrderAsync(request, cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = replaceMsg.SecurityId,
			OriginalTransactionId = replaceMsg.TransactionId,
			OrderStringId = response.OrderId,
			OrderState = OrderStates.Active,
			ServerTime = response.Timestamp,
			HasOrderInfo = true,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);

		if (!statusMsg.IsSubscribe)
			return;

		var response = await _httpClient.GetWorkingOrdersAsync(cancellationToken: cancellationToken);

		foreach (var order in response.Orders ?? [])
		{
			if (!long.TryParse(order.InstructionId, out var transId))
				continue;

			var secId = GetSecurityId(order.InstrumentId);

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				SecurityId = secId,
				TransactionId = transId,
				OriginalTransactionId = statusMsg.TransactionId,
				OrderStringId = order.OrderId,
				OrderType = order.OrderType.ToOrderType(),
				OrderPrice = order.LimitPrice?.ToDecimal() ?? default,
				OrderVolume = order.Quantity?.ToDecimal()?.Abs(),
				Balance = order.UnfilledQuantity?.ToDecimal()?.Abs(),
				Side = order.Quantity?.ToDecimal() > 0 ? Sides.Buy : Sides.Sell,
				TimeInForce = order.TimeInForce.ToTimeInForce(),
				OrderState = OrderStates.Active,
				ServerTime = order.Timestamp,
				HasOrderInfo = true,
			}, cancellationToken);
		}

		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		if (!lookupMsg.IsSubscribe)
			return;

		// Get wallets
		var walletsResponse = await _httpClient.GetWalletBalancesAsync(cancellationToken);

		foreach (var wallet in walletsResponse.Wallets ?? [])
		{
			await SendOutMessageAsync(
				this
					.CreatePortfolioChangeMessage(walletsResponse.AccountId)
					.TryAdd(PositionChangeTypes.CurrentPrice, wallet.Balance?.ToDecimal())
					.TryAdd(PositionChangeTypes.BlockedValue, wallet.Margin?.ToDecimal())
					.TryAdd(PositionChangeTypes.UnrealizedPnL, wallet.UnrealisedPnl?.ToDecimal()),
				cancellationToken);
		}

		// Get positions
		var positionsResponse = await _httpClient.GetInstrumentPositionsAsync(cancellationToken);

		foreach (var position in positionsResponse.Positions ?? [])
		{
			var secId = GetSecurityId(position.InstrumentId);

			await SendOutMessageAsync(
				this
					.CreatePositionChangeMessage(positionsResponse.AccountId, secId)
					.TryAdd(PositionChangeTypes.CurrentValue, position.OpenQuantity?.ToDecimal())
					.TryAdd(PositionChangeTypes.CurrentPrice, position.OpenCost?.ToDecimal())
					.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealisedPnl?.ToDecimal()),
				cancellationToken);
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	// WebSocket event handlers

	private ValueTask OnOrderReceived(WsOrderMessage msg, CancellationToken cancellationToken)
	{
		if (!long.TryParse(msg.InstructionId, out var transId))
			return default;

		var secId = GetSecurityId(msg.InstrumentId);
		var quantity = msg.Quantity?.ToDecimal();
		var unfilledQty = msg.UnfilledQuantity?.ToDecimal();
		var cancelledQty = msg.CancelledQuantity?.ToDecimal();

		var state = cancelledQty > 0 ? OrderStates.Done :
					unfilledQty == 0 ? OrderStates.Done : OrderStates.Active;

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = secId,
			OriginalTransactionId = transId,
			OrderStringId = msg.OrderId,
			OrderType = msg.OrderType.ToOrderType(),
			OrderPrice = msg.LimitPrice?.ToDecimal() ?? default,
			OrderVolume = quantity,
			Balance = unfilledQty,
			Side = quantity > 0 ? Sides.Buy : Sides.Sell,
			TimeInForce = msg.TimeInForce.ToTimeInForce(),
			Commission = msg.Commission?.ToDecimal(),
			OrderState = state,
			ServerTime = msg.Timestamp,
			HasOrderInfo = true,
		}, cancellationToken);
	}

	private ValueTask OnExecutionReceived(WsExecutionMessage msg, CancellationToken cancellationToken)
	{
		if (!long.TryParse(msg.InstructionId, out var transId))
			return default;

		var secId = GetSecurityId(msg.InstrumentId);

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = secId,
			OriginalTransactionId = transId,
			TradeStringId = msg.TradeId,
			OrderStringId = msg.OrderId,
			TradePrice = msg.Price?.ToDecimal(),
			TradeVolume = msg.Quantity?.ToDecimal()?.Abs(),
			Side = msg.Side.ToSide() ?? default,
			Commission = msg.Commission?.ToDecimal(),
			ServerTime = msg.Timestamp,
		}, cancellationToken);
	}

	private ValueTask OnPositionReceived(WsPositionMessage msg, CancellationToken cancellationToken)
	{
		var secId = GetSecurityId(msg.InstrumentId);

		return SendOutMessageAsync(
			this
				.CreatePositionChangeMessage(msg.AccountId, secId)
				.TryAdd(PositionChangeTypes.CurrentValue, msg.OpenQuantity?.ToDecimal())
				.TryAdd(PositionChangeTypes.CurrentPrice, msg.OpenCost?.ToDecimal())
				.TryAdd(PositionChangeTypes.UnrealizedPnL, msg.UnrealisedPnl?.ToDecimal()),
			cancellationToken);
	}

	private ValueTask OnWalletReceived(WsWalletMessage msg, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(
			this
				.CreatePortfolioChangeMessage(msg.AccountId)
				.TryAdd(PositionChangeTypes.CurrentPrice, msg.Balance?.ToDecimal())
				.TryAdd(PositionChangeTypes.BlockedValue, msg.Margin?.ToDecimal())
				.TryAdd(PositionChangeTypes.UnrealizedPnL, msg.UnrealisedPnl?.ToDecimal()),
			cancellationToken);
	}

	private ValueTask OnRejectionReceived(WsRejectionMessage msg, CancellationToken cancellationToken)
	{
		if (!long.TryParse(msg.InstructionId, out var transId))
			return default;

		var secId = GetSecurityId(msg.InstrumentId);

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = secId,
			OriginalTransactionId = transId,
			OrderState = OrderStates.Failed,
			Error = new InvalidOperationException($"{msg.RejectionReason}: {msg.Message}"),
			ServerTime = msg.Timestamp,
			HasOrderInfo = true,
		}, cancellationToken);
	}
}