namespace StockSharp.ByBit;

public partial class ByBitMessageAdapter
{
	private string PortfolioName => nameof(ByBit) + "_" + Key.ToId();

	private static readonly string[] _accTypes = ["UNIFIED"/*, "CONTRACT"*//*, "SPOT"*/];

	private long _userTradesTransId;
	private long _walletsTransId;

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var category = regMsg.SecurityId.ToCategory();

		var condition = (ByBitOrderCondition)regMsg.Condition;

		var price = regMsg.OrderType == OrderTypes.Market ? (decimal?)null : regMsg.Price;

		await _httpClient.CreateOrder(
			category, regMsg.SecurityId.ToNative(),
			regMsg.Side.ToNative(), regMsg.OrderType?.ToNative(),
			regMsg.Leverage, regMsg.TransactionId.To<string>(), price.To<string>(), regMsg.Volume.To<string>(),
			condition?.MarketUnit?.ToNative(), condition?.TriggerDirection?.ToNative(),
			condition?.TriggerPrice.To<string>(), condition?.TriggerBy?.ToNative(), condition?.IV.To<string>(),
			regMsg.TimeInForce.ToNative(regMsg.PostOnly),	condition?.PositionIdx?.ToNative(),
			condition?.TakeProfit.To<string>(), condition?.StopLoss.To<string>(),
			condition?.TpTriggerBy?.ToNative(), condition?.SlTriggerBy?.ToNative(),
			regMsg.PositionEffect?.ToNative(),	condition?.CloseOnTrigger,
			condition?.SmpType?.ToNative(), condition?.Mmp,	condition?.TpSlMode?.ToNative(),
			condition?.TpLimitPrice.To<string>(), condition?.SlLimitPrice.To<string>(),
			condition?.TpOrderType?.ToNative(), condition?.SlOrderType?.ToNative(),
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var secId = cancelMsg.SecurityId;
		await _httpClient.CancelOrder(secId.ToCategory(), secId.ToNative(), cancelMsg.OrderId.To<string>(), cancelMsg.OriginalTransactionId.To<string>(), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		var secId = replaceMsg.SecurityId;
		await _httpClient.AmendOrder(secId.ToCategory(), secId.ToNative(), replaceMsg.OldOrderId.To<string>(), replaceMsg.OriginalTransactionId.To<string>(), replaceMsg.Price.DefaultAsNull().To<string>(), replaceMsg.Volume.DefaultAsNull().To<string>(), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		if (!lookupMsg.IsSubscribe)
		{
			await _privateClient.UnsubscribeWallets(_walletsTransId, cancellationToken);
			await _privateClient.UnsubscribePositions(lookupMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = PortfolioName,
			BoardCode = BoardCodes.ByBit,
			OriginalTransactionId = lookupMsg.TransactionId
		}, cancellationToken);

		foreach (var accType in _accTypes)
		{
			try
			{
				var wallets = _httpClient.GetWallets(accType, default, default, cancellationToken);
				await ProcessWalletsAsync(lookupMsg.TransactionId, await wallets.ToArrayAsync(cancellationToken), cancellationToken);
			}
			catch (Exception ex)
			{
				this.AddErrorLog(ex);
			}
		}

		foreach (var section in _allSections.Where(s => s != ByBitSections.Spot))
		{
			try
			{
				var positions = _httpClient.GetPositions(section.ToNative(), lookupMsg.SecurityId?.ToNative(), default, section == ByBitSections.Linear ? "USDT" : default, lookupMsg.Count, cancellationToken);
				await ProcessPositionsAsync(lookupMsg.TransactionId, await positions.ToArrayAsync(cancellationToken), cancellationToken);
			}
			catch (Exception ex)
			{
				this.AddErrorLog(ex);
			}
		}

		if (!lookupMsg.IsHistoryOnly())
		{
			await _privateClient.SubscribeWallets(_walletsTransId = TransactionIdGenerator.GetNextId(), cancellationToken);
			await _privateClient.SubscribePositions(lookupMsg.TransactionId, cancellationToken);
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);

		if (!statusMsg.IsSubscribe)
		{
			await _privateClient.UnsubscribeExecutions(_userTradesTransId, cancellationToken);
			await _privateClient.UnsubscribeOrders(statusMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		foreach (var section in _allSections)
		{
			try
			{
				var orders = _httpClient.GetOrders(section.ToNative(), default, default, section == ByBitSections.Linear ? "USDT" : default, default, default, statusMsg.Count, cancellationToken);
				await ProcessOrdersAsync(statusMsg.TransactionId, await orders.ToArrayAsync(cancellationToken), cancellationToken);
			}
			catch (Exception ex)
			{
				this.AddErrorLog(ex);
			}
		}

		if (!statusMsg.IsHistoryOnly())
		{
			await _privateClient.SubscribeOrders(statusMsg.TransactionId, cancellationToken);
			await _privateClient.SubscribeExecutions(_userTradesTransId = TransactionIdGenerator.GetNextId(), cancellationToken);
		}

		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private async ValueTask ProcessWalletsAsync(long transactionId, IEnumerable<Wallet> wallets, CancellationToken cancellationToken)
	{
		var now = DateTime.UtcNow;

		foreach (var wallet in wallets)
		{
			var pfName = $"{PortfolioName}_{wallet.AccountType}";

			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = pfName,
				SecurityId = SecurityId.Money,
				ServerTime = now,
				OriginalTransactionId = transactionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, wallet.TotalAvailableBalance?.ToDecimal())
			.TryAdd(PositionChangeTypes.UnrealizedPnL, wallet.TotalPerpUPL?.ToDecimal())
			.TryAdd(PositionChangeTypes.RealizedPnL, wallet.TotalEquity?.ToDecimal())
			, cancellationToken);

			foreach (var coin in wallet.Coins)
			{
				await SendOutMessageAsync(new PositionChangeMessage
				{
					PortfolioName = pfName,
					SecurityId = coin.Coin.ToStockSharp(),
					ServerTime = now,
					OriginalTransactionId = transactionId,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, coin.WalletBalance?.ToDecimal())
				.TryAdd(PositionChangeTypes.UnrealizedPnL, coin.UnrealisedPnl?.ToDecimal())
				.TryAdd(PositionChangeTypes.RealizedPnL, coin.CumRealisedPnl?.ToDecimal())
				.TryAdd(PositionChangeTypes.VariationMargin, coin.TotalOrderIM?.ToDecimal())
				.TryAdd(PositionChangeTypes.BlockedValue, coin.Locked?.ToDecimal())
				, cancellationToken);
			}
		}
	}

	private async ValueTask ProcessOrdersAsync(long origTransId, IEnumerable<Order> orders, CancellationToken cancellationToken)
	{
		if (orders is null)
			throw new ArgumentNullException(nameof(orders));

		foreach (var order in orders)
		{
			if (!long.TryParse(order.OrderLinkId, out var transId))
				continue;

			var orderType = order.OrderType.ToOrderType();

			var orderState = order.OrderStatus.ToOrderState();

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				ServerTime = origTransId != 0 ? order.CreatedTime : order.UpdatedTime ?? DateTime.UtcNow,
				SecurityId = order.Symbol.ToStockSharp(),
				TransactionId = origTransId != 0 ? transId : 0,
				OriginalTransactionId = origTransId != 0 ? origTransId : transId,
				OrderStringId = order.OrderId,
				OrderVolume = order.Qty?.ToDecimal(),
				Balance = (order.Qty - order.CumExecQty)?.ToDecimal(),
				Commission = order.CumExecFee?.ToDecimal(),
				Side = order.Side?.ToSide() ?? default,
				OrderPrice = order.Price?.ToDecimal() ?? 0,
				TimeInForce = order.TimeInForce.ToTif(out var postOnly),
				PostOnly = postOnly,
				OrderType = orderType,
				Condition = new ByBitOrderCondition
				{
					PositionIdx = order.PositionIdx?.ToPositionIdx(),
					TriggerPrice = order.TriggerPrice?.ToDecimal(),
					TakeProfit = order.TakeProfit?.ToDecimal(),
					StopLoss = order.StopLoss?.ToDecimal(),
					TpTriggerBy = order.TpTriggerBy?.ToTriggerBy(),
					SlTriggerBy = order.SlTriggerBy?.ToTriggerBy(),
					TriggerDirection = order.TriggerDirection?.ToTriggerDirection(),
					TriggerBy = order.TriggerBy?.ToTriggerBy(),
					CloseOnTrigger = order.CloseOnTrigger,
					SmpType = order.SmpType?.ToSmpType(),
					TpSlMode = order.TpslMode?.ToTpSlMode(),
					TpLimitPrice = order.TpLimitPrice?.ToDecimal(),
					SlLimitPrice = order.SlLimitPrice?.ToDecimal()
				},
				PositionEffect = order.ReduceOnly == true ? OrderPositionEffects.CloseOnly : null,
				PortfolioName = PortfolioName,
				OrderState = orderState,
				Error = orderState == OrderStates.Failed ? new InvalidOperationException(order.RejectReason) : null,
			}, cancellationToken);
		}
	}

	private async ValueTask ProcessPositionsAsync(long transId, IEnumerable<Position> positions, CancellationToken cancellationToken)
	{
		if (positions is null)
			throw new ArgumentNullException(nameof(positions));

		foreach (var position in positions)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = PortfolioName,
				SecurityId = position.Symbol.ToStockSharp(),
				ServerTime = position.UpdatedTime ?? DateTime.UtcNow,
				OriginalTransactionId = transId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, (position.Side.ToSide() == Sides.Sell ? -1 : 1) * position.Size?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealisedPnl?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.RealizedPnL, position.CumRealisedPnl?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.AveragePrice, position.AvgPrice?.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.LiquidationPrice, position.LiqPrice?.ToDecimal())
			.TryAdd(PositionChangeTypes.Leverage, position.Leverage?.ToDecimal())
			, cancellationToken);
		}
	}

	private ValueTask SessionOnOrdersReceived(IEnumerable<Order> orders, CancellationToken cancellationToken)
	{
		return ProcessOrdersAsync(0, orders, cancellationToken);
	}

	private ValueTask SessionOnWalletsReceived(IEnumerable<Wallet> wallets, CancellationToken cancellationToken)
	{
		return ProcessWalletsAsync(0, wallets, cancellationToken);
	}

	private ValueTask SessionOnPositionsReceived(IEnumerable<Position> positions, CancellationToken cancellationToken)
	{
		return ProcessPositionsAsync(0, positions, cancellationToken);
	}

	private async ValueTask SessionOnExecutionsReceived(IEnumerable<WebSocketExecution> executions, CancellationToken cancellationToken)
	{
		foreach (var execution in executions)
		{
			if (!long.TryParse(execution.OrderLinkId, out var transId))
				continue;

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				ServerTime = execution.ExecTime ?? DateTime.UtcNow,
				TradeStringId = execution.ExecId,
				TradePrice = execution.ExecPrice?.ToDecimal(),
				TradeVolume = execution.ExecQty?.ToDecimal(),
				Commission = execution.ExecFee?.ToDecimal(),
				IsMarketMaker = execution.IsMaker,
				OriginalTransactionId = transId,
			}, cancellationToken);
		}
	}
}