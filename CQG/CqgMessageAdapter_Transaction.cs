namespace StockSharp.CQG;

public partial class CqgMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		await _tradeReady.Task.WaitAsync(cancellationToken);
		var contract = await ResolveContract(regMsg.SecurityId.ToCqgSymbol(), cancellationToken);
		var accountId = await ResolveAccount(regMsg.PortfolioName, cancellationToken);
		var condition = regMsg.Condition as CqgOrderCondition;
		var nativeType = condition?.NativeOrderType ?? regMsg.OrderType switch
		{
			OrderTypes.Market => CqgOrderTypes.Market,
			OrderTypes.Conditional => condition?.StopPrice is > 0 && regMsg.Price > 0
				? CqgOrderTypes.StopLimit : CqgOrderTypes.Stop,
			_ => CqgOrderTypes.Limit,
		};
		ValidateOrder(regMsg.Volume, regMsg.Price, nativeType, condition);
		var clientOrderId = CreateClientOrderId(regMsg.TransactionId);
		var order = new CqgOrder
		{
			AccountId = accountId,
			WhenUtcTimestamp = DateTime.UtcNow.ToTimestamp(),
			ContractId = contract.Metadata.ContractId,
			ClOrderId = clientOrderId,
			OrderType = (uint)nativeType,
			Duration = (uint)(condition?.Duration ?? ToDuration(regMsg)),
			Side = regMsg.Side == Sides.Buy ? 1u : 2u,
			Qty = regMsg.Volume.ToCqgDecimal(),
		};
		if (nativeType is CqgOrderTypes.Limit or CqgOrderTypes.StopLimit)
			order.ScaledLimitPrice = regMsg.Price.ToScaledPrice(contract.Metadata.CorrectPriceScale);
		if (nativeType is CqgOrderTypes.Stop or CqgOrderTypes.StopLimit)
			order.ScaledStopPrice = condition.StopPrice.Value.ToScaledPrice(contract.Metadata.CorrectPriceScale);
		if (regMsg.TillDate != null)
			order.GoodThruDate = (long)(regMsg.TillDate.Value.ToUniversalTime() - _client.BaseTime).TotalMilliseconds;
		ApplyCondition(order, condition, contract.Metadata.CorrectPriceScale);

		var requestId = NextRequestId();
		_requestTransactions[requestId] = regMsg.TransactionId;
		_clientOrderTransactions[clientOrderId] = regMsg.TransactionId;
		var request = new OrderRequest
		{
			RequestId = requestId,
			IsAutomated = true,
			NewOrder = new() { Order = order },
		};
		var message = new ClientMsg();
		message.OrderRequests.Add(request);
		await _client.Send(message, cancellationToken);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			TransactionId = regMsg.TransactionId,
			SecurityId = regMsg.SecurityId,
			PortfolioName = GetAccountName(accountId),
			Side = regMsg.Side,
			OrderType = regMsg.OrderType,
			OrderPrice = regMsg.Price,
			OrderVolume = regMsg.Volume,
			Balance = regMsg.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = DateTime.UtcNow,
			TimeInForce = regMsg.TimeInForce,
			ExpiryDate = regMsg.TillDate,
			Condition = regMsg.Condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		await _tradeReady.Task.WaitAsync(cancellationToken);
		var record = FindOrder(replaceMsg.OldOrderStringId, replaceMsg.OldOrderId, replaceMsg.OriginalTransactionId);
		var contract = await ResolveContract(replaceMsg.SecurityId.ToCqgSymbol(), cancellationToken);
		if (replaceMsg.Volume <= 0)
			throw new InvalidOperationException("CQG replacement quantity must be positive.");
		var clientOrderId = CreateClientOrderId(replaceMsg.TransactionId);
		var modify = new ModifyOrder
		{
			OrderId = record.OrderId,
			AccountId = record.AccountId,
			OrigClOrderId = record.ClientOrderId,
			ClOrderId = clientOrderId,
			WhenUtcTimestamp = DateTime.UtcNow.ToTimestamp(),
			Qty = replaceMsg.Volume.ToCqgDecimal(),
		};
		if (replaceMsg.Price > 0)
			modify.ScaledLimitPrice = replaceMsg.Price.ToScaledPrice(contract.Metadata.CorrectPriceScale);
		if ((replaceMsg.Condition as CqgOrderCondition)?.StopPrice is > 0)
			modify.ScaledStopPrice = ((CqgOrderCondition)replaceMsg.Condition).StopPrice.Value
				.ToScaledPrice(contract.Metadata.CorrectPriceScale);
		var requestId = NextRequestId();
		_requestTransactions[requestId] = replaceMsg.TransactionId;
		_clientOrderTransactions[clientOrderId] = replaceMsg.TransactionId;
		var message = new ClientMsg();
		message.OrderRequests.Add(new OrderRequest { RequestId = requestId, IsAutomated = true, ModifyOrder = modify });
		await _client.Send(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		await _tradeReady.Task.WaitAsync(cancellationToken);
		var record = FindOrder(cancelMsg.OrderStringId, cancelMsg.OrderId, cancelMsg.OriginalTransactionId);
		var requestId = NextRequestId();
		_requestTransactions[requestId] = cancelMsg.TransactionId;
		var clientOrderId = CreateClientOrderId(cancelMsg.TransactionId);
		_clientOrderTransactions[clientOrderId] = cancelMsg.TransactionId;
		var message = new ClientMsg();
		message.OrderRequests.Add(new OrderRequest
		{
			RequestId = requestId,
			IsAutomated = true,
			CancelOrder = new()
			{
				OrderId = record.OrderId,
				AccountId = record.AccountId,
				OrigClOrderId = record.ClientOrderId,
				ClOrderId = clientOrderId,
				WhenUtcTimestamp = DateTime.UtcNow.ToTimestamp(),
			},
		});
		await _client.Send(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		if (!statusMsg.IsSubscribe)
		{
			if (_orderStatusSubscriptionId == statusMsg.OriginalTransactionId)
				_orderStatusSubscriptionId = 0;
			return;
		}
		_orderStatusSubscriptionId = statusMsg.TransactionId;
		if (statusMsg.From != null || statusMsg.To != null)
		{
			var requestId = NextRequestId();
			_historicalOrderRequests[requestId] = statusMsg.TransactionId;
			if (statusMsg.IsHistoryOnly())
				_historyOnlyInformationRequests.Add(requestId);
			var minimum = DateTime.UtcNow.AddDays(-30);
			var from = statusMsg.From?.ToUniversalTime() ?? minimum;
			if (from < minimum)
				from = minimum;
			var historicalRequest = new HistoricalOrdersRequest
			{
				FromDate = (long)(from - _client.BaseTime).TotalMilliseconds,
				MaxOrderStatusCount = (uint)Math.Clamp(statusMsg.Count ?? 10000, 1, 100000),
			};
			if (statusMsg.To != null)
				historicalRequest.ToDate = (long)(statusMsg.To.Value.ToUniversalTime() - _client.BaseTime).TotalMilliseconds;
			var information = new ClientMsg();
			information.InformationRequests.Add(new InformationRequest
			{
				Id = requestId,
				HistoricalOrdersRequest = historicalRequest,
			});
			await _client.Send(information, cancellationToken);
			if (statusMsg.IsHistoryOnly())
			{
				await SendSubscriptionResultAsync(statusMsg, cancellationToken);
				return;
			}
		}
		var id = NextRequestId();
		_tradeSubscriptionTransactions[id] = statusMsg.TransactionId;
		if (statusMsg.IsHistoryOnly())
		{
			_historyOnlyTradeSubscriptions.Add(id);
			_tradeCompletionScopes[id] = [1];
		}
		var subscription = new TradeSubscription { Id = id, Subscribe = true, PublicationType = 4 };
		subscription.SubscriptionScopes.Add(1);
		var message = new ClientMsg();
		message.TradeSubscriptions.Add(subscription);
		await _client.Send(message, cancellationToken);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		if (!lookupMsg.IsSubscribe)
		{
			if (_portfolioSubscriptionId == lookupMsg.OriginalTransactionId)
				_portfolioSubscriptionId = 0;
			return;
		}
		var report = await SendInformationRequest(new InformationRequest
		{
			Id = NextRequestId(),
			AccountsRequest = new(),
		}, cancellationToken);
		if (report.StatusCode >= 100 || report.AccountsReport == null)
			throw new InvalidOperationException($"CQG accounts request failed ({report.StatusCode}): {report.TextMessage}");
		CacheAccounts(report.AccountsReport);
		foreach (var account in _accountNames.Where(p => MatchesPortfolio(p.Key, p.Value, lookupMsg.PortfolioName)))
			await SendOutMessageAsync(new PortfolioMessage
			{
				OriginalTransactionId = lookupMsg.TransactionId,
				PortfolioName = account.Value,
				BoardCode = "CQG",
			}, cancellationToken);
		_portfolioSubscriptionId = lookupMsg.TransactionId;
		var id = NextRequestId();
		_tradeSubscriptionTransactions[id] = lookupMsg.TransactionId;
		if (lookupMsg.IsHistoryOnly())
		{
			_historyOnlyTradeSubscriptions.Add(id);
			_tradeCompletionScopes[id] = [2, 4];
		}
		var summaryParameters = new AccountSummaryParameters();
		summaryParameters.RequestedFields.Add([4, 6, 8, 9, 15, 16, 17]);
		var subscription = new TradeSubscription
		{
			Id = id,
			Subscribe = true,
			PublicationType = 4,
			AccountSummaryParameters = summaryParameters,
		};
		subscription.SubscriptionScopes.Add([2, 4]);
		var message = new ClientMsg();
		message.TradeSubscriptions.Add(subscription);
		await _client.Send(message, cancellationToken);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private void CacheAccounts(AccountsReport report)
	{
		foreach (var account in report.Brokerages.SelectMany(b => b.SalesSeries).SelectMany(s => s.Accounts)
			.Where(a => !a.IsUnauthorized))
		{
			var name = account.BrokerageAccountNumber.IsEmpty(account.Name).IsEmpty(account.AccountId.ToString(CultureInfo.InvariantCulture));
			_accountNames[account.AccountId] = name;
		}
	}

	private async Task<int> ResolveAccount(string requestedPortfolio, CancellationToken cancellationToken)
	{
		if (_accountNames.Count == 0)
		{
			var report = await SendInformationRequest(new InformationRequest { Id = NextRequestId(), AccountsRequest = new() }, cancellationToken);
			if (report.StatusCode >= 100 || report.AccountsReport == null)
				throw new InvalidOperationException($"CQG accounts request failed ({report.StatusCode}): {report.TextMessage}");
			CacheAccounts(report.AccountsReport);
		}
		var portfolio = requestedPortfolio.IsEmpty(Portfolio);
		if (!portfolio.IsEmpty())
		{
			foreach (var pair in _accountNames)
				if (pair.Value.EqualsIgnoreCase(portfolio) || pair.Key.ToString(CultureInfo.InvariantCulture).EqualsIgnoreCase(portfolio))
					return pair.Key;
			throw new InvalidOperationException($"CQG account '{portfolio}' is not authorized for this session.");
		}
		if (_accountNames.Count == 1)
			return _accountNames.Keys.First();
		throw new InvalidOperationException("CQG Portfolio must be specified when the session has more than one authorized account.");
	}

	private string GetAccountName(int accountId)
		=> _accountNames.TryGetValue(accountId, out var name) ? name : accountId.ToString(CultureInfo.InvariantCulture);

	private bool MatchesPortfolio(int accountId, string account, string requested)
	{
		var id = accountId.ToString(CultureInfo.InvariantCulture);
		return (Portfolio.IsEmpty() || account.EqualsIgnoreCase(Portfolio) || id.EqualsIgnoreCase(Portfolio)) &&
			(requested.IsEmpty() || account.EqualsIgnoreCase(requested) || id.EqualsIgnoreCase(requested));
	}

	private async ValueTask ProcessOrderReject(OrderRequestReject reject, CancellationToken cancellationToken)
	{
		_requestTransactions.TryGetAndRemove(reject.RequestId, out var transactionId);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = transactionId,
			TransactionId = transactionId,
			OrderState = OrderStates.Failed,
			ServerTime = DateTime.UtcNow,
			Error = new InvalidOperationException($"CQG order request rejected: {reject.TextMessage}"),
		}, cancellationToken);
	}

	private async ValueTask ProcessOrderStatus(CqgOrderStatus status, CancellationToken cancellationToken,
		long historicalTransactionId = 0)
	{
		foreach (var metadata in status.ContractMetadata)
			CacheContract(metadata);
		var details = status.Order;
		var record = _orders.SafeAdd(status.ChainOrderId, _ => new OrderRecord
		{
			OrderId = status.OrderId,
			AccountId = status.AccountId,
		});
		if (details != null)
		{
			record.Details = details;
			record.ClientOrderId = details.ClOrderId;
			record.AccountId = details.AccountId;
		}
		record.OrderId = status.OrderId;
		if (record.Details == null)
			return;
		details = record.Details;
		if (_clientOrderTransactions.TryGetValue(details.ClOrderId, out var transactionId))
			record.TransactionId = transactionId;
		else
		{
			foreach (var transaction in status.TransactionStatuses)
				if (_clientOrderTransactions.TryGetValue(transaction.ClOrderId, out transactionId))
					record.TransactionId = transactionId;
		}
		var originalTransactionId = historicalTransactionId != 0 ? historicalTransactionId :
			record.TransactionId != 0 ? record.TransactionId : GetOriginalTransactionId(status.SubscriptionIds);
		if (originalTransactionId == 0)
			return;
		if (!_contractsById.TryGetValue(details.ContractId, out var contract))
			return;
		var orderType = details.OrderType switch
		{
			1 => OrderTypes.Market,
			2 => OrderTypes.Limit,
			_ => OrderTypes.Conditional,
		};
		var condition = new CqgOrderCondition
		{
			NativeOrderType = (CqgOrderTypes)details.OrderType,
			StopPrice = details.HasScaledStopPrice ? details.ScaledStopPrice.ToPrice(contract.Metadata.CorrectPriceScale) : null,
			Duration = (CqgOrderDurations)details.Duration,
			VisibleVolume = details.VisibleQty?.ToDecimal(),
			TriggerVolume = details.TriggerQty?.ToDecimal(),
			TrailOffset = details.HasScaledTrailOffset ? details.ScaledTrailOffset.ToPrice(contract.Metadata.CorrectPriceScale) : null,
		};
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId,
			TransactionId = record.TransactionId,
			OrderStringId = status.OrderId,
			SecurityId = contract.SecurityId,
			PortfolioName = GetAccountName(status.AccountId),
			Side = details.Side == 1 ? Sides.Buy : Sides.Sell,
			OrderType = orderType,
			OrderPrice = details.HasScaledLimitPrice ? details.ScaledLimitPrice.ToPrice(contract.Metadata.CorrectPriceScale) : 0,
			OrderVolume = details.Qty.ToDecimal(),
			Balance = status.RemainingQty?.ToDecimal(),
			AveragePrice = status.HasAvgFillPriceCorrect ? (decimal)status.AvgFillPriceCorrect : null,
			OrderState = ToOrderState(status.Status),
			ServerTime = status.StatusUtcTimestamp.ToDateTime(),
			Condition = condition,
			Error = status.Status == 2 ? new InvalidOperationException(status.RejectMessage.IsEmpty("CQG order rejected.")) : null,
		}, cancellationToken);

		foreach (var transaction in status.TransactionStatuses.Where(t => t.Status == 11))
		{
			if (!_fills.TryAdd($"{status.ChainOrderId}:{transaction.TransId}"))
				continue;
			if (transaction.Trades.Count > 0)
			{
				foreach (var trade in transaction.Trades)
				{
					if (!_contractsById.TryGetValue(trade.ContractId, out var tradeContract))
						continue;
					await SendOutMessageAsync(new ExecutionMessage
					{
						DataTypeEx = DataType.Transactions,
						OriginalTransactionId = originalTransactionId,
						TransactionId = record.TransactionId,
						OrderStringId = status.OrderId,
						TradeStringId = trade.TradeId,
						SecurityId = tradeContract.SecurityId,
						PortfolioName = GetAccountName(status.AccountId),
						Side = trade.Side == 1 ? Sides.Buy : Sides.Sell,
						TradePrice = (decimal)trade.PriceCorrect,
						TradeVolume = trade.Qty.ToDecimal(),
						ServerTime = trade.TradeUtcTimestamp.ToDateTime(),
					}, cancellationToken);
				}
			}
			else if (transaction.FillQty != null)
			{
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OriginalTransactionId = originalTransactionId,
					TransactionId = record.TransactionId,
					OrderStringId = status.OrderId,
					TradeStringId = transaction.TransId.ToString(CultureInfo.InvariantCulture),
					SecurityId = contract.SecurityId,
					PortfolioName = GetAccountName(status.AccountId),
					Side = details.Side == 1 ? Sides.Buy : Sides.Sell,
					TradePrice = transaction.HasCorrectFillPrice ? (decimal)transaction.CorrectFillPrice :
						transaction.ScaledFillPrice.ToPrice(contract.Metadata.CorrectPriceScale),
					TradeVolume = transaction.FillQty.ToDecimal(),
					ServerTime = transaction.TransUtcTimestamp.ToDateTime(),
				}, cancellationToken);
			}
		}
	}

	private long GetOriginalTransactionId(IEnumerable<uint> subscriptionIds)
	{
		foreach (var id in subscriptionIds)
			if (_tradeSubscriptionTransactions.TryGetValue(id, out var transactionId))
				return transactionId;
		return _orderStatusSubscriptionId;
	}

	private async ValueTask ProcessPosition(PositionStatus status, CancellationToken cancellationToken)
	{
		var originalTransactionId = GetPortfolioTransactionId(status.SubscriptionIds);
		if (originalTransactionId == 0 || !_contractsById.TryGetValue(status.ContractId, out var contract))
			return;
		var prefix = $"{status.AccountId}:{status.ContractId}:";
		if (status.IsSnapshot && _positionSnapshots.TryAdd(prefix))
		{
			foreach (var key in _positions.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray())
				_positions.Remove(key);
		}
		else if (!status.IsSnapshot)
			_positionSnapshots.Remove(prefix);
		foreach (var position in status.OpenPositions)
		{
			var key = $"{prefix}{position.Id}";
			if (position.Qty.ToDecimal() == 0)
				_positions.Remove(key);
			else
				_positions[key] = position;
		}
		var openPositions = _positions.Where(p => p.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			.Select(p => p.Value).ToArray();
		var quantity = openPositions.Sum(p => (p.IsShort ? -1 : 1) * p.Qty.ToDecimal());
		var totalVolume = openPositions.Sum(p => p.Qty.ToDecimal());
		var average = totalVolume == 0 ? (decimal?)null :
			openPositions.Sum(p => (decimal)p.PriceCorrect * p.Qty.ToDecimal()) / totalVolume;
		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = GetAccountName(status.AccountId),
			SecurityId = contract.SecurityId,
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, quantity, true)
		.TryAdd(PositionChangeTypes.AveragePrice, average, true), cancellationToken);
	}

	private async ValueTask ProcessAccountSummary(AccountSummaryStatus summary, CancellationToken cancellationToken)
	{
		var originalTransactionId = GetPortfolioTransactionId(summary.SubscriptionIds);
		if (originalTransactionId == 0 || !summary.HasAccountId)
			return;
		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = GetAccountName(summary.AccountId),
			SecurityId = SecurityId.Money,
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, summary.HasCurrentBalance ? (decimal)summary.CurrentBalance : null, true)
		.TryAdd(PositionChangeTypes.BuyOrdersMargin, summary.HasPurchasingPower ? (decimal)summary.PurchasingPower : null, true)
		.TryAdd(PositionChangeTypes.BlockedValue, summary.HasTotalMargin ? (decimal)summary.TotalMargin : null, true)
		.TryAdd(PositionChangeTypes.RealizedPnL, summary.HasProfitLoss ? (decimal)summary.ProfitLoss : null, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, summary.HasUnrealizedProfitLoss ? (decimal)summary.UnrealizedProfitLoss : null, true)
		.TryAdd(PositionChangeTypes.Currency, System.Enum.TryParse<CurrencyTypes>(summary.Currency, true, out var currency) ? currency : null),
			cancellationToken);
	}

	private long GetPortfolioTransactionId(IEnumerable<uint> subscriptionIds)
	{
		foreach (var id in subscriptionIds)
			if (_tradeSubscriptionTransactions.TryGetValue(id, out var transactionId))
				return transactionId;
		return _portfolioSubscriptionId;
	}

	private async ValueTask ProcessTradeSnapshotCompletion(TradeSnapshotCompletion completion,
		CancellationToken cancellationToken)
	{
		if (!_historyOnlyTradeSubscriptions.Contains(completion.SubscriptionId) ||
			!_tradeCompletionScopes.TryGetValue(completion.SubscriptionId, out var pendingScopes))
			return;
		foreach (var scope in completion.SubscriptionScopes)
			pendingScopes.Remove(scope);
		if (pendingScopes.Count != 0 ||
			!_tradeSubscriptionTransactions.TryGetAndRemove(completion.SubscriptionId, out var transactionId))
			return;
		_historyOnlyTradeSubscriptions.Remove(completion.SubscriptionId);
		_tradeCompletionScopes.Remove(completion.SubscriptionId);
		await SendSubscriptionFinishedAsync(transactionId, cancellationToken);
	}

	private OrderRecord FindOrder(string stringId, long? numericId, long originalTransactionId)
	{
		var id = !stringId.IsEmpty() ? stringId : numericId?.ToString(CultureInfo.InvariantCulture);
		if (!id.IsEmpty())
		{
			foreach (var record in _orders.Values)
				if (record.OrderId.EqualsIgnoreCase(id))
					return record;
		}
		foreach (var record in _orders.Values)
			if (record.TransactionId == originalTransactionId)
				return record;
		throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(originalTransactionId));
	}

	private static string CreateClientOrderId(long transactionId)
		=> $"S{transactionId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}";

	private static CqgOrderDurations ToDuration(OrderRegisterMessage message)
		=> message.TillDate != null ? CqgOrderDurations.GoodTillDate : message.TimeInForce switch
		{
			TimeInForce.CancelBalance => CqgOrderDurations.ImmediateOrCancel,
			TimeInForce.MatchOrCancel => CqgOrderDurations.FillOrKill,
			_ => CqgOrderDurations.Day,
		};

	private static void ApplyCondition(CqgOrder order, CqgOrderCondition condition, double scale)
	{
		if (condition == null)
			return;
		if (condition.Instructions.HasFlag(CqgExecutionInstructions.AllOrNone))
			order.ExecInstructions.Add(1);
		if (condition.Instructions.HasFlag(CqgExecutionInstructions.Iceberg))
			order.ExecInstructions.Add(2);
		if (condition.Instructions.HasFlag(CqgExecutionInstructions.QuantityTriggered))
			order.ExecInstructions.Add(3);
		if (condition.Instructions.HasFlag(CqgExecutionInstructions.Trailing))
			order.ExecInstructions.Add(4);
		if (condition.Instructions.HasFlag(CqgExecutionInstructions.MarketIfTouched))
			order.ExecInstructions.Add(6);
		if (condition.Instructions.HasFlag(CqgExecutionInstructions.PostOnly))
			order.ExecInstructions.Add(8);
		if (condition.VisibleVolume is > 0)
			order.VisibleQty = condition.VisibleVolume.Value.ToCqgDecimal();
		if (condition.TriggerVolume is > 0)
			order.TriggerQty = condition.TriggerVolume.Value.ToCqgDecimal();
		if (condition.TrailOffset is > 0)
			order.ScaledTrailOffset = condition.TrailOffset.Value.ToScaledPrice(scale);
	}

	private static void ValidateOrder(decimal volume, decimal price, CqgOrderTypes type, CqgOrderCondition condition)
	{
		if (volume <= 0)
			throw new InvalidOperationException("CQG order quantity must be positive.");
		if (type is CqgOrderTypes.Limit or CqgOrderTypes.StopLimit && price <= 0)
			throw new InvalidOperationException($"CQG {type} orders require a positive limit price.");
		if (type is CqgOrderTypes.Stop or CqgOrderTypes.StopLimit && condition?.StopPrice is not > 0)
			throw new InvalidOperationException($"CQG {type} orders require StopPrice.");
		if (condition?.Instructions.HasFlag(CqgExecutionInstructions.Iceberg) == true && condition.VisibleVolume is not > 0)
			throw new InvalidOperationException("CQG iceberg orders require VisibleVolume.");
	}

	private static OrderStates ToOrderState(uint status)
		=> status switch
		{
			2 or 14 or 17 => OrderStates.Failed,
			4 or 7 or 8 or 15 => OrderStates.Done,
			3 or 9 or 12 or 13 or 16 or 18 => OrderStates.Active,
			_ => OrderStates.Pending,
		};
}
