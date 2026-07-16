namespace StockSharp.SierraChartDtc;

using StockSharp.SierraChartDtc.Native;

public partial class SierraChartDtcMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsureTradingSupported();
		if (regMsg.Volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Volume));

		var condition = regMsg.Condition as SierraChartDtcOrderCondition ?? new();
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		var nativeType = orderType.ToNative(condition.StopPrice, regMsg.Price);
		var (price1, price2) = GetNativePrices(nativeType, regMsg.Price, condition.StopPrice);
		var clientOrderId = regMsg.TransactionId.ToString(CultureInfo.InvariantCulture);
		var account = regMsg.PortfolioName.IsEmpty(TradeAccount);
		var tracker = new OrderTracker
		{
			TransactionId = regMsg.TransactionId,
			ClientOrderId = clientOrderId,
			SecurityId = regMsg.SecurityId,
			PortfolioName = account.IsEmpty("DTC"),
			Side = regMsg.Side,
			OrderType = orderType,
			Price = regMsg.Price,
			Volume = regMsg.Volume,
			Condition = condition,
		};
		_ordersByClient[clientOrderId] = tracker;
		_ordersByTransaction[regMsg.TransactionId] = tracker;

		try
		{
			await GetClient().Send(new DtcSubmitOrder
			{
				Symbol = regMsg.SecurityId.SecurityCode,
				Exchange = regMsg.SecurityId.BoardCode.EqualsIgnoreCase("DTC")
					? null : regMsg.SecurityId.BoardCode,
				TradeAccount = account,
				ClientOrderId = clientOrderId,
				OrderType = nativeType,
				Side = regMsg.Side.ToNative(),
				Price1 = price1,
				Price2 = price2,
				Quantity = regMsg.Volume,
				TimeInForce = regMsg.TimeInForce.ToNative(regMsg.TillDate),
				GoodTillTime = regMsg.TillDate?.ToUniversalTime(),
				IsAutomated = condition.IsAutomated,
				IsParent = condition.IsParent,
				FreeFormText = condition.FreeFormText,
				OpenOrClose = regMsg.PositionEffect switch
				{
					OrderPositionEffects.OpenOnly => DtcOpenCloses.Open,
					OrderPositionEffects.CloseOnly => DtcOpenCloses.Close,
					_ when condition.IsOpenPosition => DtcOpenCloses.Open,
					_ => DtcOpenCloses.Unset,
				},
				MaxShowQuantity = condition.MaxShowVolume ?? 0,
			}, cancellationToken);
		}
		catch
		{
			_ordersByClient.Remove(clientOrderId);
			_ordersByTransaction.Remove(regMsg.TransactionId);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		EnsureTradingSupported();
		if (_capabilities?.IsCancelReplaceSupported == false)
			throw new NotSupportedException("The connected DTC server does not support native cancel/replace.");
		if (replaceMsg.Volume < 0)
			throw new ArgumentOutOfRangeException(nameof(replaceMsg.Volume));

		var tracker = GetOrderTracker(replaceMsg.OldOrderStringId, replaceMsg.OriginalTransactionId);
		var condition = replaceMsg.Condition as SierraChartDtcOrderCondition ?? tracker.Condition ?? new();
		var orderType = replaceMsg.OrderType ?? tracker.OrderType;
		var nativeType = orderType.ToNative(condition.StopPrice, replaceMsg.Price);
		var (price1, price2) = GetNativePrices(nativeType, replaceMsg.Price, condition.StopPrice);
		await GetClient().Send(new DtcReplaceOrder
		{
			ServerOrderId = tracker.ServerOrderId.IsEmpty(replaceMsg.OldOrderStringId),
			ClientOrderId = tracker.ClientOrderId,
			Price1 = price1,
			Price2 = price2,
			Quantity = replaceMsg.Volume,
			IsPrice1Set = price1 != null,
			IsPrice2Set = price2 != null,
			TimeInForce = replaceMsg.TimeInForce.ToNative(replaceMsg.TillDate),
			GoodTillTime = replaceMsg.TillDate?.ToUniversalTime(),
			TradeAccount = replaceMsg.PortfolioName.IsEmpty(tracker.PortfolioName).IsEmpty(TradeAccount),
		}, cancellationToken);

		tracker.OrderType = orderType;
		tracker.Price = replaceMsg.Price;
		if (replaceMsg.Volume > 0)
			tracker.Volume = replaceMsg.Volume;
		tracker.Condition = condition;
		_ordersByTransaction[replaceMsg.TransactionId] = tracker;
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsureTradingSupported();
		var tracker = GetOrderTracker(cancelMsg.OrderStringId, cancelMsg.OriginalTransactionId);
		await GetClient().Send(new DtcCancelOrder
		{
			ServerOrderId = tracker.ServerOrderId.IsEmpty(cancelMsg.OrderStringId),
			ClientOrderId = tracker.ClientOrderId,
			TradeAccount = cancelMsg.PortfolioName.IsEmpty(tracker.PortfolioName).IsEmpty(TradeAccount),
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		if (!statusMsg.IsSubscribe)
		{
			if (_orderStatusSubscriptionId == statusMsg.OriginalTransactionId)
				_orderStatusSubscriptionId = 0;
			return;
		}
		EnsureTradingSupported();

		var requestId = GetRequestId();
		var context = new OrderStatusContext { Message = statusMsg };
		_orderStatusRequests.Add(requestId, context);
		try
		{
			await GetClient().Send(new DtcOpenOrdersRequest
			{
				RequestId = requestId,
				IsAllOrders = statusMsg.OrderId == null && statusMsg.OrderStringId.IsEmpty(),
				ServerOrderId = statusMsg.OrderStringId.IsEmpty(statusMsg.OrderId?.ToString(CultureInfo.InvariantCulture)),
				TradeAccount = statusMsg.PortfolioName.IsEmpty(TradeAccount),
			}, cancellationToken);

			var from = statusMsg.From?.ToUniversalTime();
			var days = from == null ? 0 : Math.Max(1,
				(int)Math.Min(int.MaxValue, Math.Ceiling((DateTime.UtcNow - from.Value).TotalDays)));
			await GetClient().Send(new DtcHistoricalFillsRequest
			{
				RequestId = requestId,
				ServerOrderId = statusMsg.OrderStringId.IsEmpty(statusMsg.OrderId?.ToString(CultureInfo.InvariantCulture)),
				Days = days,
				TradeAccount = statusMsg.PortfolioName.IsEmpty(TradeAccount),
				From = from,
			}, cancellationToken);
		}
		catch
		{
			_orderStatusRequests.Remove(requestId);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		if (!lookupMsg.IsSubscribe)
		{
			if (_portfolioSubscriptionId == lookupMsg.OriginalTransactionId)
				_portfolioSubscriptionId = 0;
			return;
		}
		EnsureTradingSupported();

		var requestId = GetRequestId();
		var account = lookupMsg.PortfolioName.IsEmpty(TradeAccount);
		_portfolioRequests.Add(requestId, new() { Message = lookupMsg });
		try
		{
			await GetClient().Send(new DtcTradeAccountsRequest { RequestId = requestId }, cancellationToken);
			await GetClient().Send(new DtcCurrentPositionsRequest
			{
				RequestId = requestId,
				TradeAccount = account,
			}, cancellationToken);
			await GetClient().Send(new DtcAccountBalanceRequest
			{
				RequestId = requestId,
				TradeAccount = account,
			}, cancellationToken);
		}
		catch
		{
			_portfolioRequests.Remove(requestId);
			throw;
		}
	}

	private async ValueTask ProcessOrder(DtcOrderUpdate order, CancellationToken cancellationToken)
	{
		var tracker = FindOrCreateTracker(order);
		var contextTransactionId = _orderStatusRequests.TryGetValue(order.RequestId, out var context)
			? context.Message.TransactionId : 0;
		var originalTransactionId = contextTransactionId != 0
			? contextTransactionId
			: tracker.TransactionId != 0 ? tracker.TransactionId : _orderStatusSubscriptionId;
		var serverTime = order.LatestTransactionTime ?? order.OrderReceivedTime ?? CurrentTime;
		var orderPrice = order.OrderType == DtcOrderTypes.StopLimit
			? order.Price2 : order.OrderType == DtcOrderTypes.Stop ? 0 : order.Price1;
		var condition = tracker.Condition ?? new();
		if (order.OrderType is DtcOrderTypes.Stop or DtcOrderTypes.StopLimit)
			condition.StopPrice = order.Price1;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId,
			TransactionId = tracker.TransactionId,
			OrderId = order.ServerOrderId.ToLongId(),
			OrderStringId = order.ServerOrderId,
			SecurityId = tracker.SecurityId,
			PortfolioName = order.TradeAccount.IsEmpty(tracker.PortfolioName).IsEmpty("DTC"),
			Side = order.Side.ToStockSharp(),
			OrderType = order.OrderType.ToStockSharp(),
			OrderPrice = orderPrice ?? 0,
			OrderVolume = order.Quantity,
			Balance = order.RemainingQuantity,
			AveragePrice = order.AverageFillPrice,
			OrderState = order.Status.ToStockSharp(),
			ServerTime = serverTime,
			TimeInForce = order.TimeInForce.ToStockSharp(),
			ExpiryDate = order.GoodTillTime,
			Condition = condition,
			PositionEffect = order.OpenOrClose switch
			{
				DtcOpenCloses.Open => OrderPositionEffects.OpenOnly,
				DtcOpenCloses.Close => OrderPositionEffects.CloseOnly,
				_ => null,
			},
			Error = order.Status == DtcOrderStatuses.Rejected
				? new InvalidOperationException(order.InfoText.IsEmpty("The DTC server rejected the order."))
				: null,
		}, cancellationToken);

		if (!order.LastFillExecutionId.IsEmpty() && order.LastFillQuantity is > 0 &&
			order.LastFillPrice is > 0 && _reportedFills.TryAdd(order.LastFillExecutionId))
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = originalTransactionId,
				OrderId = order.ServerOrderId.ToLongId(),
				OrderStringId = order.ServerOrderId,
				TradeId = order.LastFillExecutionId.ToLongId(),
				TradeStringId = order.LastFillExecutionId,
				SecurityId = tracker.SecurityId,
				PortfolioName = order.TradeAccount.IsEmpty(tracker.PortfolioName).IsEmpty("DTC"),
				Side = order.Side.ToStockSharp(),
				TradePrice = order.LastFillPrice,
				TradeVolume = order.LastFillQuantity,
				ServerTime = order.LastFillTime ?? serverTime,
			}, cancellationToken);
		}

		if (context != null && IsLast(order.TotalMessages, order.MessageNumber, order.IsNoOrders))
		{
			context.IsOrdersComplete = true;
			await CompleteOrderStatus(order.RequestId, context, cancellationToken);
		}
	}

	private async ValueTask ProcessHistoricalFill(DtcHistoricalFill fill,
		CancellationToken cancellationToken)
	{
		_orderStatusRequests.TryGetValue(fill.RequestId, out var context);
		var tracker = !fill.ServerOrderId.IsEmpty() && _ordersByServer.TryGetValue(fill.ServerOrderId, out var found)
			? found
			: null;
		if (!fill.IsNoFills && !fill.ExecutionId.IsEmpty() && _reportedFills.TryAdd(fill.ExecutionId))
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = context?.Message.TransactionId ?? _orderStatusSubscriptionId,
				OrderId = fill.ServerOrderId.ToLongId(),
				OrderStringId = fill.ServerOrderId,
				TradeId = fill.ExecutionId.ToLongId(),
				TradeStringId = fill.ExecutionId,
				SecurityId = tracker?.SecurityId ?? ToSecurityId(fill.Symbol, fill.Exchange),
				PortfolioName = fill.TradeAccount.IsEmpty(tracker?.PortfolioName).IsEmpty("DTC"),
				Side = fill.Side.ToStockSharp(),
				TradePrice = fill.Price,
				TradeVolume = fill.Quantity,
				ServerTime = fill.Time,
			}, cancellationToken);
		}

		if (context != null && IsLast(fill.TotalMessages, fill.MessageNumber, fill.IsNoFills))
		{
			context.IsFillsComplete = true;
			await CompleteOrderStatus(fill.RequestId, context, cancellationToken);
		}
	}

	private async ValueTask ProcessTradeAccount(DtcTradeAccount account,
		CancellationToken cancellationToken)
	{
		_portfolioRequests.TryGetValue(account.RequestId, out var context);
		var portfolioName = account.Account.IsEmpty(TradeAccount).IsEmpty("DTC");
		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = context?.Message.TransactionId ?? _portfolioSubscriptionId,
			PortfolioName = portfolioName,
			BoardCode = "DTC",
		}, cancellationToken);
		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = context?.Message.TransactionId ?? _portfolioSubscriptionId,
			PortfolioName = portfolioName,
			SecurityId = SecurityId.Money,
			ServerTime = CurrentTime,
		}.Add(PositionChangeTypes.State,
			account.IsTradingDisabled ? PortfolioStates.Blocked : PortfolioStates.Active), cancellationToken);

		if (context != null && IsLast(account.TotalMessages, account.MessageNumber, account.Account.IsEmpty()))
		{
			context.IsAccountsComplete = true;
			await CompletePortfolio(account.RequestId, context, cancellationToken);
		}
	}

	private async ValueTask ProcessPosition(DtcPositionUpdate position,
		CancellationToken cancellationToken)
	{
		_portfolioRequests.TryGetValue(position.RequestId, out var context);
		if (!position.IsNoPositions && !position.Symbol.IsEmpty())
		{
			var portfolioName = position.TradeAccount.IsEmpty(TradeAccount).IsEmpty("DTC");
			await SendOutMessageAsync(new PortfolioMessage
			{
				OriginalTransactionId = context?.Message.TransactionId ?? _portfolioSubscriptionId,
				PortfolioName = portfolioName,
				BoardCode = "DTC",
			}, cancellationToken);
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = context?.Message.TransactionId ?? _portfolioSubscriptionId,
				PortfolioName = portfolioName,
				SecurityId = ToSecurityId(position.Symbol, position.Exchange),
				DepoName = position.PositionIdentifier,
				ServerTime = position.EntryTime ?? CurrentTime,
			}
			.Add(PositionChangeTypes.CurrentValue, position.Quantity)
			.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, position.OpenProfitLoss, true), cancellationToken);
		}

		if (context != null && IsLast(position.TotalMessages, position.MessageNumber, position.IsNoPositions))
		{
			context.IsPositionsComplete = true;
			await CompletePortfolio(position.RequestId, context, cancellationToken);
		}
	}

	private async ValueTask ProcessBalance(DtcAccountBalance balance,
		CancellationToken cancellationToken)
	{
		_portfolioRequests.TryGetValue(balance.RequestId, out var context);
		if (!balance.IsNoBalances)
		{
			var portfolioName = balance.TradeAccount.IsEmpty(TradeAccount).IsEmpty("DTC");
			await SendOutMessageAsync(new PortfolioMessage
			{
				OriginalTransactionId = context?.Message.TransactionId ?? _portfolioSubscriptionId,
				PortfolioName = portfolioName,
				BoardCode = "DTC",
			}, cancellationToken);
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = context?.Message.TransactionId ?? _portfolioSubscriptionId,
				PortfolioName = portfolioName,
				SecurityId = SecurityId.Money,
				ServerTime = balance.TransactionTime ?? CurrentTime,
			}
			.Add(PositionChangeTypes.State,
				balance.IsTradingDisabled ? PortfolioStates.Blocked : PortfolioStates.Active)
			.Add(PositionChangeTypes.CurrentValue, balance.CashBalance)
			.TryAdd(PositionChangeTypes.BlockedValue,
				Math.Max(0, balance.CashBalance - balance.AvailableFunds), true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, balance.OpenProfitLoss, true)
			.TryAdd(PositionChangeTypes.RealizedPnL, balance.DailyProfitLoss, true)
			.TryAdd(PositionChangeTypes.Currency, balance.Currency.ToCurrency()), cancellationToken);
		}

		if (context != null && IsLast(balance.TotalMessages, balance.MessageNumber, balance.IsNoBalances))
		{
			context.IsBalancesComplete = true;
			await CompletePortfolio(balance.RequestId, context, cancellationToken);
		}
	}

	private async ValueTask ProcessReject(DtcReject reject, CancellationToken cancellationToken)
	{
		var error = new InvalidOperationException(reject.Text.IsEmpty($"The DTC server rejected {reject.Type}."));
		if (reject.Type == DtcMessageTypes.SecurityDefinitionReject &&
			_securityLookups.TryGetAndRemove(reject.RequestId, out var security))
		{
			await SendSubscriptionReplyAsync(security.Message.TransactionId, cancellationToken, error);
			return;
		}

		if (reject.Type is DtcMessageTypes.MarketDataReject or DtcMessageTypes.MarketDepthReject &&
			_symbolsById.TryGetValue(reject.SymbolId, out var symbol))
		{
			long[] ids;
			lock (symbol.SyncRoot)
			{
				var subscriptions = reject.Type == DtcMessageTypes.MarketDepthReject
					? symbol.DepthSubscriptions : symbol.MarketSubscriptions;
				ids = [.. subscriptions];
				subscriptions.Clear();
				if (reject.Type == DtcMessageTypes.MarketDepthReject)
				{
					symbol.Bids.Clear();
					symbol.Asks.Clear();
				}
			}
			foreach (var id in ids)
			{
				_marketSubscriptions.Remove(id);
				await SendSubscriptionReplyAsync(id, cancellationToken, error);
			}
			return;
		}

		if (_orderStatusRequests.TryGetValue(reject.RequestId, out var orderContext))
		{
			if (reject.Type == DtcMessageTypes.OpenOrdersReject)
				orderContext.IsOrdersComplete = true;
			else if (reject.Type == DtcMessageTypes.HistoricalOrderFillsReject)
				orderContext.IsFillsComplete = true;
			await SendOutErrorAsync(error, cancellationToken);
			await CompleteOrderStatus(reject.RequestId, orderContext, cancellationToken);
			return;
		}

		if (_portfolioRequests.TryGetValue(reject.RequestId, out var portfolioContext))
		{
			if (reject.Type == DtcMessageTypes.CurrentPositionsReject)
				portfolioContext.IsPositionsComplete = true;
			else if (reject.Type == DtcMessageTypes.AccountBalanceReject)
				portfolioContext.IsBalancesComplete = true;
			await SendOutErrorAsync(error, cancellationToken);
			await CompletePortfolio(reject.RequestId, portfolioContext, cancellationToken);
			return;
		}

		await SendOutErrorAsync(error, cancellationToken);
	}

	private async ValueTask CompleteOrderStatus(int requestId, OrderStatusContext context,
		CancellationToken cancellationToken)
	{
		if (!context.IsOrdersComplete || !context.IsFillsComplete ||
			!_orderStatusRequests.Remove(requestId))
			return;
		if (context.Message.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(context.Message.TransactionId, cancellationToken);
		else
		{
			_orderStatusSubscriptionId = context.Message.TransactionId;
			await SendSubscriptionResultAsync(context.Message, cancellationToken);
		}
	}

	private async ValueTask CompletePortfolio(int requestId, PortfolioContext context,
		CancellationToken cancellationToken)
	{
		if (!context.IsAccountsComplete || !context.IsPositionsComplete || !context.IsBalancesComplete ||
			!_portfolioRequests.Remove(requestId))
			return;
		if (context.Message.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(context.Message.TransactionId, cancellationToken);
		else
		{
			_portfolioSubscriptionId = context.Message.TransactionId;
			await SendSubscriptionResultAsync(context.Message, cancellationToken);
		}
	}

	private OrderTracker FindOrCreateTracker(DtcOrderUpdate order)
	{
		OrderTracker tracker = null;
		if (!order.ClientOrderId.IsEmpty())
			_ordersByClient.TryGetValue(order.ClientOrderId, out tracker);
		if (tracker == null && !order.ServerOrderId.IsEmpty())
			_ordersByServer.TryGetValue(order.ServerOrderId, out tracker);
		if (tracker == null)
		{
			var transactionId = long.TryParse(order.ClientOrderId, NumberStyles.Integer,
				CultureInfo.InvariantCulture, out var id) ? id : 0;
			tracker = new()
			{
				TransactionId = transactionId,
				ClientOrderId = order.ClientOrderId,
				ServerOrderId = order.ServerOrderId,
				SecurityId = ToSecurityId(order.Symbol, order.Exchange),
				PortfolioName = order.TradeAccount.IsEmpty("DTC"),
				Side = order.Side.ToStockSharp(),
				OrderType = order.OrderType.ToStockSharp(),
				Price = order.Price2 ?? order.Price1 ?? 0,
				Volume = order.Quantity ?? 0,
				Condition = new(),
			};
		}
		if (!order.ServerOrderId.IsEmpty())
		{
			tracker.ServerOrderId = order.ServerOrderId;
			_ordersByServer[order.ServerOrderId] = tracker;
		}
		if (!order.ClientOrderId.IsEmpty())
			_ordersByClient[order.ClientOrderId] = tracker;
		if (tracker.TransactionId != 0)
			_ordersByTransaction[tracker.TransactionId] = tracker;
		return tracker;
	}

	private OrderTracker GetOrderTracker(string serverOrderId, long transactionId)
	{
		if (!serverOrderId.IsEmpty() && _ordersByServer.TryGetValue(serverOrderId, out var byServer))
			return byServer;
		if (transactionId != 0 && _ordersByTransaction.TryGetValue(transactionId, out var byTransaction))
			return byTransaction;
		throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(transactionId));
	}

	private void EnsureTradingSupported()
	{
		if (_capabilities?.IsTradingSupported == false)
			throw new NotSupportedException("The connected DTC server does not advertise trading support.");
	}

	private static (decimal? Price1, decimal? Price2) GetNativePrices(DtcOrderTypes type,
		decimal limitPrice, decimal? stopPrice)
		=> type switch
		{
			DtcOrderTypes.Market => (null, null),
			DtcOrderTypes.Limit when limitPrice > 0 => (limitPrice, null),
			DtcOrderTypes.Stop when stopPrice is > 0 => (stopPrice, null),
			DtcOrderTypes.StopLimit when stopPrice is > 0 && limitPrice > 0 => (stopPrice, limitPrice),
			DtcOrderTypes.Limit => throw new InvalidOperationException("DTC limit orders require a positive limit price."),
			DtcOrderTypes.Stop => throw new InvalidOperationException("DTC stop orders require a positive stop price."),
			DtcOrderTypes.StopLimit => throw new InvalidOperationException("DTC stop-limit orders require positive stop and limit prices."),
			_ => throw new NotSupportedException($"DTC order type '{type}' is not supported."),
		};

	private static bool IsLast(int totalMessages, int messageNumber, bool isEmpty)
		=> isEmpty || totalMessages <= 0 || messageNumber >= totalMessages;
}
