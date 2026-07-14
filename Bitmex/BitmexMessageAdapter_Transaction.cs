namespace StockSharp.Bitmex;

public partial class BitmexMessageAdapter
{
	private long _ownTradesTransId;
	private long _posTransId;

	private string PortfolioName => nameof(Bitmex) + "_" + Key.ToId();

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var condition = (BitmexOrderCondition)regMsg.Condition;

		switch (regMsg.OrderType)
		{
			case null:
			case OrderTypes.Limit:
			case OrderTypes.Market:
				break;
			case OrderTypes.Conditional:
			{
				if (!condition.IsWithdraw)
				{
					break;
					//throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
				}

				if (condition.WithdrawInfo.Comment.IsEmpty())
				{
					var withdrawId = await _httpClient.Withdraw(regMsg.SecurityId.SecurityCode, regMsg.Volume, condition.WithdrawInfo, cancellationToken);

					await SendOutMessageAsync(new ExecutionMessage
					{
						DataTypeEx = DataType.Transactions,
						OrderStringId = withdrawId,
						ServerTime = CurrentTime,
						OriginalTransactionId = regMsg.TransactionId,
						OrderState = OrderStates.Done,
						HasOrderInfo = true,
					}, cancellationToken);
				}
				else
					await _httpClient.WithdrawConfirm(condition.WithdrawInfo.Comment, cancellationToken);

				//ProcessPortfolioLookup(null);
				return;
			}
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		var price = regMsg.OrderType == OrderTypes.Market ? (decimal?)null : regMsg.Price;

		var order = await _httpClient.RegisterOrder(regMsg.SecurityId.SecurityCode, regMsg.OrderType.ToNative(condition),
			regMsg.Side.ToNative(), price, regMsg.Volume, regMsg.VisibleVolume, condition?.StopPrice,
			regMsg.TimeInForce.ToNative(regMsg.TillDate), regMsg.TransactionId.To<string>(),
			condition?.ClOrdLinkId, condition?.PegOffsetValue, condition?.PegPriceType?.To<string>(), condition?.ExecInst.ToNative(),
			condition?.ContingencyType?.To<string>(), /*regMsg.Comment*/"Sent from StockSharp", cancellationToken);

		var state = order.OrdStatus.ToOrderState();

		if(state != OrderStates.Failed)
			return; // order info will be received via websocket. otherwise, this response and websocket one might be unsynchronized.

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OrderStringId = order.OrderId,
			ServerTime = order.TransactTime ?? order.Timestamp,
			OriginalTransactionId = regMsg.TransactionId,
			OrderState = state,
			Balance = state == OrderStates.Done ? 0 : null,
			HasOrderInfo = true,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var order = await _httpClient.CancelOrder(cancelMsg.OriginalTransactionId == 0 ? null : cancelMsg.OriginalTransactionId.To<string>(), cancelMsg.OrderStringId, cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			ServerTime = CurrentTime,
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = cancelMsg.TransactionId,
			OrderState = OrderStates.Done,
			Balance = order.GetBalance(),
			HasOrderInfo = true,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		await _httpClient.AmendOrder(
			replaceMsg.OriginalTransactionId == 0 ? null : replaceMsg.OriginalTransactionId.To<string>(),
			replaceMsg.OldOrderStringId,
			replaceMsg.TransactionId.To<string>(),
			replaceMsg.Price == 0 ? null : replaceMsg.Price,
			replaceMsg.Volume,
			cancellationToken
		);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var cancelledOrders = await _httpClient.CancelAllOrder(cancelMsg.SecurityId.SecurityCode, cancelMsg.Side?.ToNative(), null, cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			ServerTime = CurrentTime,
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = cancelMsg.TransactionId,
			OrderState = OrderStates.Done,
			HasOrderInfo = true,
		}, cancellationToken);

		foreach (var order in cancelledOrders)
		{
			if (!long.TryParse(order.ClOrdId, out var transId))
				return;

			await SendOutMessageAsync(new ExecutionMessage
			{
				ServerTime = CurrentTime,
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = transId,
				OrderState = OrderStates.Done,
				Balance = order.GetBalance(),
				HasOrderInfo = true,
			}, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		if (!lookupMsg.IsSubscribe)
		{
			await _pusherClient.UnSubscribeAccount(lookupMsg.OriginalTransactionId, _posTransId, cancellationToken);
			_posTransId = default;
			return;
		}

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = PortfolioName,
			BoardCode = BoardCodes.Bitmex,
			OriginalTransactionId = lookupMsg.TransactionId
		}, cancellationToken);

		try
		{
			var commissions = await _httpClient.GetCommission(cancellationToken);

			foreach (var commission in commissions)
			{
				await SendOutMessageAsync(new Level1ChangeMessage
				{
					ServerTime = CurrentTime,
					SecurityId = commission.Key.ToStockSharp(),
				}
				.TryAdd(Level1Fields.CommissionMaker, commission.Value.MakerFee?.ToDecimal())
				.TryAdd(Level1Fields.CommissionTaker, commission.Value.TakerFee?.ToDecimal()), cancellationToken);
			}
		}
		catch// (Exception ex)
		{
			// sub accounts throws error
			//this.AddErrorLog(ex);
		}

		var positions = await _httpClient.GetPositions(lookupMsg.SecurityId is not null ? new { symbol = lookupMsg.SecurityId.Value } : default, default, cancellationToken);

		await ProcessPositions(positions, lookupMsg.TransactionId, cancellationToken);

		if (!lookupMsg.IsHistoryOnly())
		{
			_posTransId = TransactionIdGenerator.GetNextId();
			await _pusherClient.SubscribeAccount(lookupMsg.TransactionId, _posTransId, cancellationToken);
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private async ValueTask ProcessPositions(IEnumerable<Position> positions, long transactionId, CancellationToken cancellationToken)
	{
		foreach (var position in positions)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = PortfolioName,
				SecurityId = position.Symbol.ToStockSharp(),
				ServerTime = position.CurrentTimestamp ?? position.Timestamp,
				OriginalTransactionId = transactionId,
			}
			.TryAdd(PositionChangeTypes.AveragePrice, position.AvgEntryPrice?.ToDecimal())
			.TryAdd(PositionChangeTypes.CurrentValue, position.CurrentQty?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.Commission, position.Commission?.ToDecimal(), true), cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);

		if (!statusMsg.IsSubscribe)
		{
			await _pusherClient.UnSubscribeOrders(statusMsg.OriginalTransactionId, _ownTradesTransId, cancellationToken);
			_ownTradesTransId = default;
			return;
		}

		try
		{
			var all = statusMsg.States?.Contains(OrderStates.Done) == true;

			// TODO ������-�� ����� � ������ ��������� �������� ��������
			var filter = all ? null : new { open = true };

			var orders = await _httpClient.GetOrders(default, filter, default, default, default, default, cancellationToken);

			foreach (var order in orders)
			{
				if (!long.TryParse(order.ClOrdId, out var transId))
					transId = TransactionIdGenerator.GetNextId();

				await ProcessOrder(order, transId, statusMsg.TransactionId, cancellationToken);
			}
		}
		catch (Exception ex)
		{
			this.AddErrorLog(ex);
		}

		if (!statusMsg.IsHistoryOnly())
		{
			_ownTradesTransId = TransactionIdGenerator.GetNextId();
			await _pusherClient.SubscribeOrders(statusMsg.TransactionId, _ownTradesTransId, cancellationToken);
		}

		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private ValueTask ProcessOrder(Order order, long transId, long origTransId, CancellationToken cancellationToken)
	{
		var orderType = order.OrdType.ToOrderType(out var condition);

		if (orderType != OrderTypes.Conditional &&
		    (order.StopPx != null || !order.ContingencyType.IsEmpty() || order.PegOffsetValue != null
		     || !order.PegPriceType.IsEmpty() || !order.ClOrdLinkId.IsEmpty() || !order.ExecInst.IsEmpty()))
		{
			condition = new BitmexOrderCondition();
		}

		if (condition != null)
		{
			condition.StopPrice = order.StopPx?.ToDecimal();
			condition.ContingencyType = order.ContingencyType?.To<BitmexOrderContingencyTypes?>();
			condition.PegOffsetValue = order.PegOffsetValue?.ToDecimal();
			condition.PegPriceType = order.PegPriceType?.To<BitmexOrderPegPriceTypes?>();
			condition.ClOrdLinkId = order.ClOrdLinkId;
			condition.ExecInst = order.ExecInst.ToExecInst();
		}

		var orderState = order.OrdStatus.ToOrderState();

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = transId != 0 ? order.TransactTime ?? order.Timestamp : order.Timestamp,
			SecurityId = order.Symbol.ToStockSharp(),
			TransactionId = transId,
			OriginalTransactionId = origTransId,
			OrderStringId = order.OrderId,
			OrderVolume = order.OrderQty?.ToDecimal(),
			VisibleVolume = order.DisplayQty?.ToDecimal(),
			Balance = order.GetBalance(),
			Side = order.Side?.ToSide() ?? default,
			OrderPrice = order.Price?.ToDecimal() ?? 0,
			TimeInForce = order.TimeInForce.ToTimeInForce(out var expiryDate),
			ExpiryDate = expiryDate,
			OrderType = orderType,
			Condition = condition,
			PortfolioName = PortfolioName,
			OrderState = orderState,
			Error = orderState == OrderStates.Failed ? new InvalidOperationException(order.OrdRejReason) : null,
			Comment = order.Text,
		}, cancellationToken);
	}

	private async ValueTask SessionOnOrderChanged(string action, IEnumerable<Order> orders, CancellationToken cancellationToken)
	{
		foreach (var order in orders)
		{
			if (!long.TryParse(order.ClOrdId, out var transId))
				continue;

			await ProcessOrder(order, 0, transId, cancellationToken);
		}
	}

	private async ValueTask SessionOnNewExecutions(string action, IEnumerable<Execution> executions, CancellationToken cancellationToken)
	{
		foreach (var execution in executions)
		{
			if (!long.TryParse(execution.ClOrdId, out var transId))
				continue;

			if (execution.LastPx != null)
			{
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OriginalTransactionId = transId,
					OrderStringId = execution.OrderId,
					SecurityId = execution.Symbol.ToStockSharp(),
					TradeStringId = execution.TrdMatchId,
					TradePrice = execution.LastPx?.ToDecimal(),
					TradeVolume = execution.LastQty?.ToDecimal(),
					ServerTime = execution.TransactTime ?? execution.Timestamp,
				}, cancellationToken);
			}
		}
	}

	private async ValueTask SessionOnMarginsChanged(string action, IEnumerable<Margin> margins, CancellationToken cancellationToken)
	{
		foreach (var margin in margins)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				SecurityId = margin.Currency.ToStockSharp(),
				PortfolioName = PortfolioName,
				ServerTime = margin.Timestamp,
			}
			.TryAdd(PositionChangeTypes.RealizedPnL, margin.RealisedPnl?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, margin.UnrealisedPnl?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.CurrentValue, margin.Amount?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.VariationMargin, margin.AvailableMargin?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.Commission, margin.Commission?.ToDecimal(), true), cancellationToken);
		}
	}

	private ValueTask SessionOnPositionsChanged(string action, IEnumerable<Position> positions, CancellationToken cancellationToken)
	{
		return ProcessPositions(positions, 0, cancellationToken);
	}
}