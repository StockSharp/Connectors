namespace StockSharp.Alpaca;

partial class AlpacaMessageAdapter
{
	private string _accountName;

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var condition = regMsg.Condition as AlpacaOrderCondition;

		var order = await _tradingClient.CreateOrder(
			regMsg.TransactionId,
			regMsg.SecurityId.SecurityCode,
			regMsg.Volume,
			regMsg.Side.ToNative(),
			regMsg.GetOrderType(condition),
			regMsg.GetTif(),
			regMsg.Price.DefaultAsNull(),
			condition?.Price,
			condition?.Trail,
			condition?.TrailPercent,
			condition?.IgnoreRth,
			condition?.OrderClass?.ToNative(),
			cancellationToken);

		await ProcessOrderAsync(regMsg.TransactionId, order,
			msg => msg.ServerTime = order.UpdatedAt ?? CurrentTime, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		var orderId = replaceMsg.OldOrderStringId ?? throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(replaceMsg.OriginalTransactionId));

		var condition = replaceMsg.Condition as AlpacaOrderCondition;

		var order = await _tradingClient.ReplaceOrder(
			replaceMsg.TransactionId,
			orderId,
			replaceMsg.Volume,
			replaceMsg.GetTif(),
			replaceMsg.Price.DefaultAsNull(),
			condition?.Price,
			condition?.Trail,
			cancellationToken);

		await ProcessOrderAsync(replaceMsg.TransactionId, order,
			msg => msg.ServerTime = order.UpdatedAt ?? CurrentTime, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage message, CancellationToken cancellationToken)
	{
		var orderId = message.OrderStringId ?? throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(message.OriginalTransactionId));

		await _tradingClient.DeleteOrder(orderId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
		=> await _tradingClient.DeleteOrders(cancellationToken);

	private async Task<string> EnsureAccountName(CancellationToken cancellationToken)
	{
		if (_accountName.IsEmpty())
		{
			var account = await _tradingClient.GetAccount(cancellationToken);
			_accountName = account.AccountNumber;
		}

		return _accountName;
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		if (lookupMsg is null)
			throw new ArgumentNullException(nameof(lookupMsg));

		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		if (lookupMsg.IsSubscribe)
		{
			var account = await _tradingClient.GetAccount(cancellationToken);

			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = account.AccountNumber,
				SecurityId = SecurityId.Money,
				OriginalTransactionId = lookupMsg.TransactionId,
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, account.Cash?.ToDecimal())
			.TryAdd(PositionChangeTypes.Commission, account.AccruedFees?.ToDecimal())
			.TryAdd(PositionChangeTypes.RealizedPnL, account.Equity?.ToDecimal())
			.TryAdd(PositionChangeTypes.CurrentPrice, account.PositionMarketValue?.ToDecimal())
			.TryAdd(PositionChangeTypes.Currency, account.Currency.FromMicexCurrencyName(this.AddErrorLog))
			.TryAdd(PositionChangeTypes.AveragePrice, account.Sma?.ToDecimal())
			.TryAdd(PositionChangeTypes.BuyOrdersMargin, account.BuyingPower?.ToDecimal())
			.TryAdd(PositionChangeTypes.VariationMargin, account.MaintenanceMargin?.ToDecimal())
			.TryAdd(PositionChangeTypes.Leverage, (decimal?)account.Multiplier)
			, cancellationToken);

			var positions = await _tradingClient.GetPositions(cancellationToken);

			foreach (var position in positions)
			{
				await SendOutMessageAsync(new PositionChangeMessage
				{
					PortfolioName = account.AccountNumber,
					SecurityId = new() { SecurityCode = position.Symbol, BoardCode = position.Exchange },
					OriginalTransactionId = lookupMsg.TransactionId,
					ServerTime = CurrentTime,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, (position.Side.ToSide() == Sides.Sell ? -1 : 1) * position.Qty?.ToDecimal())
				.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPl?.ToDecimal())
				.TryAdd(PositionChangeTypes.CurrentPrice, position.CurrentPrice?.ToDecimal())
				.TryAdd(PositionChangeTypes.AveragePrice, position.AvgEntryPrice?.ToDecimal())
				, cancellationToken);
			}

			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		}
		else
		{
			//await _socketClient.UnSubscribe(message.TransactionId, message.OriginalTransactionId, cancellationToken);
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
			var orders = await _tradingClient.GetOrders(cancellationToken);
			var accountName = await EnsureAccountName(cancellationToken);

			foreach (var order in orders)
			{
				if (!long.TryParse(order.ClientOrderId, out var transId))
					continue;

				var secId = await EnsureGetSecId(order.AssetId, cancellationToken);

				await ProcessOrderAsync(statusMsg.TransactionId, order, msg =>
				{
					msg.TransactionId = transId;
					msg.SecurityId = secId;
					msg.PortfolioName = accountName;
					msg.ServerTime = order.CreatedAt;
					msg.OrderPrice = order.LimitPrice?.ToDecimal() ?? default;
					msg.OrderVolume = order.Qty?.ToDecimal();
					msg.Side = order.Side.ToSide() ?? default;
					msg.OrderType = order.Type.ToOrderType();
					msg.TimeInForce = order.TimeInForce.ToTif(out var till);
					msg.ExpiryDate = till;
					msg.Condition = new AlpacaOrderCondition
					{
						Price = order.StopPrice?.ToDecimal(),
						IgnoreRth = order.ExtendedHours,
						Trail = order.TrailPrice?.ToDecimal(),
						TrailPercent = order.TrailPercent?.ToDecimal(),
						OrderClass = order.OrderClass.IsEmpty() ? null : order.OrderClass.ToOrderClass(),
					};
				}, cancellationToken);
			}

			if (!statusMsg.IsHistoryOnly())
				await _socketTradingClient.SubscribeTrades(statusMsg.TransactionId, cancellationToken);

			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		}
		else
		{
			await _socketTradingClient.UnSubscribeTrades(statusMsg.OriginalTransactionId, cancellationToken);
		}
	}

	private ValueTask ProcessOrderAsync(long originTransId, Order order, Action<ExecutionMessage> handle, CancellationToken cancellationToken)
	{
		if (order is null)		throw new ArgumentNullException(nameof(order));
		if (handle is null)		throw new ArgumentNullException(nameof(handle));

		var msg = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originTransId,
			OrderStringId = order.Id,
			Balance = (order.Qty - order.FilledQty)?.ToDecimal(),
			OrderState = order.Status.ToOrderState(),
			AveragePrice = order.FilledAvgPrice?.ToDecimal(),
		};

		handle(msg);
		return SendOutMessageAsync(msg, cancellationToken);
	}

	private async ValueTask OnOrderReceived(OrderData data, CancellationToken cancellationToken)
	{
		var order = data.Order;

		if (long.TryParse(order.ClientOrderId, out var transId))
		{
			await ProcessOrderAsync(transId, order, msg => msg.ServerTime = data.Timestamp, cancellationToken);
		}

		if (data.PositionQty is not null)
		{
			try
			{
				await SendOutMessageAsync(new PositionChangeMessage
				{
					ServerTime = data.Timestamp,
					PortfolioName = await EnsureAccountName(cancellationToken),
					SecurityId = await EnsureGetSecId(order.AssetId, cancellationToken),
				}
				.TryAdd(PositionChangeTypes.CurrentValue, data.PositionQty.Value.ToDecimal()), cancellationToken);
			}
			catch (Exception ex)
			{
				this.AddErrorLog(ex);
			}
		}
	}
}