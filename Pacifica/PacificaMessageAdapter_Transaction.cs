namespace StockSharp.Pacifica;

using Native;

public partial class PacificaMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(regMsg.PortfolioName);
		var market = GetMarket(regMsg.SecurityId);
		var volume = regMsg.Volume.Abs();
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		var condition = regMsg.Condition as PacificaOrderCondition ?? new();
		var isReduceOnly = condition.IsReduceOnly ||
			regMsg.PositionEffect == OrderPositionEffects.CloseOnly;
		var price = orderType == OrderTypes.Limit ? regMsg.Price : (decimal?)null;
		ValidateOrder(market, volume, price, condition);
		var clientOrderId = CreateClientOrderId(regMsg.UserOrderId);
		using (_sync.EnterScope())
		{
			if (_transactionByClientOrderId.ContainsKey(clientOrderId))
				throw new InvalidOperationException(
					"Pacifica client order ID '" + clientOrderId +
					"' is already in use.");
			_transactionByClientOrderId.Add(clientOrderId,
				regMsg.TransactionId);
		}
		try
		{
			PacificaActionResponse response;
			if (orderType == OrderTypes.Limit)
			{
				var timeInForce = regMsg.TimeInForce.ToPacifica(
					regMsg.PostOnly == true, orderType);
				response = await Socket.CreateOrderAsync(new()
				{
					Symbol = market.Symbol,
					Price = price.Value.ToWire(),
					IsReduceOnly = isReduceOnly,
					Amount = volume.ToWire(),
					Side = regMsg.Side.ToPacifica(),
					TimeInForce = timeInForce,
					ClientOrderId = clientOrderId,
					BuilderCode = NormalizeBuilderCode(condition.BuilderCode),
					TakeProfit = CreateStop(condition.TakeProfitPrice,
						condition.TakeProfitLimitPrice, condition.TriggerPriceType),
					StopLoss = CreateStop(condition.StopLossPrice,
						condition.StopLossLimitPrice, condition.TriggerPriceType),
				}, Signer, DateTime.UtcNow, cancellationToken);
			}
			else
			{
				if (regMsg.PostOnly == true)
					throw new NotSupportedException(
						"Pacifica market orders cannot be post-only.");
				_ = regMsg.TimeInForce.ToPacifica(false, orderType);
				var slippage = condition.SlippagePercent ?? MarketOrderSlippage;
				if (slippage is <= 0 or > 100)
					throw new InvalidOperationException(
						"Pacifica slippage must be greater than zero and at most 100%.");
				response = await Socket.CreateMarketOrderAsync(new()
				{
					Symbol = market.Symbol,
					IsReduceOnly = isReduceOnly,
					Amount = volume.ToWire(),
					Side = regMsg.Side.ToPacifica(),
					SlippagePercent = slippage.ToWire(),
					ClientOrderId = clientOrderId,
					BuilderCode = NormalizeBuilderCode(condition.BuilderCode),
					TakeProfit = CreateStop(condition.TakeProfitPrice,
						condition.TakeProfitLimitPrice, condition.TriggerPriceType),
					StopLoss = CreateStop(condition.StopLossPrice,
						condition.StopLossLimitPrice, condition.TriggerPriceType),
				}, Signer, DateTime.UtcNow, cancellationToken);
			}
			await SendActionOrderAsync(response, regMsg.TransactionId,
				regMsg.TransactionId, market.Symbol, clientOrderId, regMsg.Side,
				volume, price ?? 0m, orderType, condition, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_transactionByClientOrderId.Remove(clientOrderId);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(replaceMsg.PortfolioName);
		var market = GetMarket(replaceMsg.SecurityId);
		var orderType = replaceMsg.OrderType ?? OrderTypes.Limit;
		if (orderType != OrderTypes.Limit)
			throw new NotSupportedException(
				"Pacifica can edit limit orders only.");
		if (replaceMsg.PostOnly == false)
			throw new NotSupportedException(
				"Pacifica edits recreate the order as post-only.");
		var volume = replaceMsg.Volume.Abs();
		var condition = replaceMsg.Condition as PacificaOrderCondition ?? new();
		ValidateOrder(market, volume, replaceMsg.Price, condition);
		ResolveOrderIdentity(replaceMsg.OldOrderId,
			replaceMsg.OldOrderStringId, null, "replacement",
			out var orderId, out var clientOrderId);
		var response = await Socket.EditOrderAsync(new()
		{
			Symbol = market.Symbol,
			Price = replaceMsg.Price.ToWire(),
			Amount = volume.ToWire(),
			OrderId = orderId,
			ClientOrderId = clientOrderId,
		}, Signer, DateTime.UtcNow, cancellationToken);
		var resultingClientOrderId = response.Data?.ClientOrderId ?? clientOrderId;
		if (!resultingClientOrderId.IsEmpty())
			using (_sync.EnterScope())
				_transactionByClientOrderId[resultingClientOrderId] =
					replaceMsg.TransactionId;
		await SendActionOrderAsync(response, replaceMsg.TransactionId,
			replaceMsg.TransactionId, market.Symbol, resultingClientOrderId,
			replaceMsg.Side, volume, replaceMsg.Price, OrderTypes.Limit,
			condition, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		var market = GetMarket(cancelMsg.SecurityId);
		ResolveOrderIdentity(cancelMsg.OrderId, cancelMsg.OrderStringId,
			cancelMsg.UserOrderId, "cancellation", out var orderId,
			out var clientOrderId);
		var response = await Socket.CancelOrderAsync(new()
		{
			Symbol = market.Symbol,
			OrderId = orderId,
			ClientOrderId = clientOrderId,
		}, Signer, DateTime.UtcNow, cancellationToken);
		var time = response.Timestamp.ToPacificaTimeOrNow();
		UpdateServerTime(time);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			OrderId = response.Data?.OrderId ?? orderId,
			UserOrderId = response.Data?.ClientOrderId ?? clientOrderId,
			OrderState = OrderStates.Done,
			Balance = 0m,
			OriginalTransactionId = cancelMsg.TransactionId,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"Pacifica bulk cancellation does not close positions.");
		if (cancelMsg.SecurityTypes is { Length: > 0 } &&
			!cancelMsg.SecurityTypes.Contains(SecurityTypes.Future))
			return;
		var symbol = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(cancelMsg.SecurityId).Symbol;
		if (cancelMsg.Side is null && cancelMsg.IsStop is null)
		{
			await Socket.CancelAllOrdersAsync(new()
			{
				IsAllSymbols = symbol.IsEmpty(),
				IsExcludeReduceOnly = false,
				Symbol = symbol,
			}, Signer, DateTime.UtcNow, cancellationToken);
			return;
		}
		var orders = await RestClient.GetOrdersAsync(Signer.Account,
			cancellationToken);
		foreach (var order in orders.Where(order => order is not null &&
			(order.Symbol.Equals(symbol, StringComparison.Ordinal) || symbol.IsEmpty()) &&
			(cancelMsg.Side is null || order.Side.ToStockSharp() == cancelMsg.Side) &&
			(cancelMsg.IsStop is null || IsConditional(order.OrderType) ==
				cancelMsg.IsStop)))
			await Socket.CancelOrderAsync(new()
			{
				Symbol = order.Symbol,
				OrderId = order.OrderId,
			}, Signer, DateTime.UtcNow, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureAccountReady();
		ValidatePortfolio(lookupMsg.PortfolioName);
		if (!lookupMsg.IsSubscribe)
		{
			_portfolioSubscriptionId = 0;
			await Socket.UnsubscribeAsync(AccountStream(PacificaSources.AccountInfo),
				cancellationToken);
			await Socket.UnsubscribeAsync(AccountStream(
				PacificaSources.AccountPositions), cancellationToken);
			return;
		}
		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = _portfolioName,
			BoardCode = BoardCodes.Pacifica,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId,
			cancellationToken);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId,
				cancellationToken);
			return;
		}
		_portfolioSubscriptionId = lookupMsg.TransactionId;
		try
		{
			await Socket.SubscribeAsync(AccountStream(PacificaSources.AccountInfo),
				cancellationToken);
			await Socket.SubscribeAsync(AccountStream(
				PacificaSources.AccountPositions), cancellationToken);
		}
		catch
		{
			_portfolioSubscriptionId = 0;
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId,
			cancellationToken);
		EnsureAccountReady();
		ValidatePortfolio(statusMsg.PortfolioName);
		if (!statusMsg.IsSubscribe)
		{
			_orderStatusSubscriptionId = 0;
			if (!Signer.IsSigningAvailable)
			{
				await Socket.UnsubscribeAsync(AccountStream(
					PacificaSources.AccountOrderUpdates), cancellationToken);
				await Socket.UnsubscribeAsync(AccountStream(
					PacificaSources.AccountTrades), cancellationToken);
			}
			return;
		}
		await SendOrderSnapshotAsync(statusMsg, cancellationToken);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}
		_orderStatusSubscriptionId = statusMsg.TransactionId;
		try
		{
			await Socket.SubscribeAsync(AccountStream(
				PacificaSources.AccountOrderUpdates), cancellationToken);
			await Socket.SubscribeAsync(AccountStream(
				PacificaSources.AccountTrades), cancellationToken);
		}
		catch
		{
			_orderStatusSubscriptionId = 0;
			throw;
		}
	}

	private async ValueTask SendPortfolioSnapshotAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		var account = await RestClient.GetAccountAsync(Signer.Account,
			cancellationToken);
		if (account is not null)
			await SendAccountAsync(account, transactionId, cancellationToken);
		var positions = await RestClient.GetPositionsAsync(Signer.Account,
			cancellationToken);
		await SendPositionSnapshotAsync(positions, transactionId,
			cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		var symbols = GetOrderSymbols(statusMsg);
		var limit = (statusMsg.Count ?? HistoryLimit)
			.Min(HistoryLimit).Max(1).To<int>();
		var open = await RestClient.GetOrdersAsync(Signer.Account,
			cancellationToken);
		var history = await LoadOrderHistoryAsync(limit, cancellationToken);
		var messages = (open ?? []).Concat(history)
			.Where(static order => order is not null && order.OrderId > 0)
			.Select(order => CreateOrderMessage(order, statusMsg.TransactionId))
			.Where(static message => message is not null)
			.Where(message => IsOrderMatch(message, statusMsg, symbols))
			.GroupBy(static message => message.OrderId)
			.Select(static group => group.OrderByDescending(
				message => message.ServerTime).First())
			.OrderBy(static message => message.ServerTime)
			.Skip(Math.Max(0, statusMsg.Skip ?? 0).To<int>())
			.Take(limit)
			.ToArray();
		foreach (var message in messages)
		{
			UpdateServerTime(message.ServerTime);
			await SendOutMessageAsync(message, cancellationToken);
		}

		var querySymbol = symbols.Count == 1 ? symbols.First() : null;
		var trades = await LoadTradeHistoryAsync(querySymbol, statusMsg.From,
			statusMsg.To, limit, cancellationToken);
		foreach (var trade in trades
			.Where(trade => IsTradeMatch(trade, statusMsg, symbols))
			.OrderBy(static trade => trade.CreatedAt)
			.Skip(Math.Max(0, statusMsg.Skip ?? 0).To<int>())
			.Take(limit))
			await SendAccountTradeAsync(trade, statusMsg.TransactionId, false,
				cancellationToken);
	}

	private async ValueTask<PacificaOrder[]> LoadOrderHistoryAsync(int maximum,
		CancellationToken cancellationToken)
	{
		var result = new List<PacificaOrder>();
		string cursor = null;
		while (result.Count < maximum)
		{
			var pageLimit = (maximum - result.Count).Min(HistoryLimit).Max(1);
			var page = await RestClient.GetOrderHistoryAsync(Signer.Account,
				pageLimit, cursor, cancellationToken);
			result.AddRange((page.Data ?? []).Where(static order => order is not null));
			if (!page.IsMore || page.NextCursor.IsEmpty() ||
				page.NextCursor.Equals(cursor, StringComparison.Ordinal))
				break;
			cursor = page.NextCursor;
		}
		return [.. result.Take(maximum)];
	}

	private async ValueTask<PacificaAccountTrade[]> LoadTradeHistoryAsync(
		string symbol, DateTime? from, DateTime? to, int maximum,
		CancellationToken cancellationToken)
	{
		var result = new List<PacificaAccountTrade>();
		string cursor = null;
		while (result.Count < maximum)
		{
			var pageLimit = (maximum - result.Count).Min(HistoryLimit).Max(1);
			var page = await RestClient.GetTradeHistoryAsync(Signer.Account,
				symbol, from, to, pageLimit, cursor, cancellationToken);
			result.AddRange((page.Data ?? []).Where(static trade => trade is not null));
			if (!page.IsMore || page.NextCursor.IsEmpty() ||
				page.NextCursor.Equals(cursor, StringComparison.Ordinal))
				break;
			cursor = page.NextCursor;
		}
		return [.. result
			.GroupBy(static trade => trade.HistoryId)
			.Select(static group => group.First())
			.Take(maximum)];
	}

	private ValueTask OnAccountInfoAsync(PacificaAccountInfoUpdate account,
		CancellationToken cancellationToken)
		=> SendAccountAsync(account, _portfolioSubscriptionId, cancellationToken);

	private async ValueTask OnPositionsAsync(PacificaPositionUpdate[] positions,
		CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId == 0)
			return;
		if (positions is not { Length: > 0 })
		{
			var snapshot = await RestClient.GetPositionsAsync(Signer.Account,
				cancellationToken);
			await SendPositionSnapshotAsync(snapshot, _portfolioSubscriptionId,
				cancellationToken);
			return;
		}
		var current = new HashSet<string>(StringComparer.Ordinal);
		foreach (var position in positions)
		{
			if (position?.Symbol.IsEmpty() != false)
				continue;
			current.Add(position.Symbol);
			await SendPositionAsync(position, _portfolioSubscriptionId,
				cancellationToken);
		}
		await SendMissingPositionsAsync(current, _portfolioSubscriptionId,
			positions.Select(static position => position?.Timestamp ?? 0)
				.DefaultIfEmpty().Max().ToPacificaTimeOrNow(), cancellationToken);
	}

	private async ValueTask OnOrdersAsync(PacificaOrder[] orders,
		CancellationToken cancellationToken)
	{
		foreach (var order in orders ?? [])
		{
			var transactionId = GetTransactionId(order?.ClientOrderId);
			var originalTransactionId = _orderStatusSubscriptionId != 0
				? _orderStatusSubscriptionId
				: transactionId;
			if (originalTransactionId != 0)
				await SendOrderAsync(order, originalTransactionId,
					cancellationToken);
		}
	}

	private async ValueTask OnAccountTradesAsync(PacificaAccountTrade[] trades,
		CancellationToken cancellationToken)
	{
		foreach (var trade in trades ?? [])
		{
			var transactionId = GetTransactionId(trade?.ClientOrderId);
			var originalTransactionId = _orderStatusSubscriptionId != 0
				? _orderStatusSubscriptionId
				: transactionId;
			if (originalTransactionId != 0)
				await SendAccountTradeAsync(trade, originalTransactionId, true,
					cancellationToken);
		}
	}

	private async ValueTask SendAccountAsync(PacificaAccountInfo account,
		long transactionId, CancellationToken cancellationToken)
	{
		if (account is null || transactionId == 0)
			return;
		var time = account.UpdatedAt.ToPacificaTimeOrNow();
		UpdateServerTime(time);
		await SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = "USD".ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(PositionChangeTypes.BeginValue,
			account.Balance.TryParseDecimal(), true)
		.TryAdd(PositionChangeTypes.CurrentValue,
			account.AccountEquity.TryParseDecimal(), true)
		.TryAdd(PositionChangeTypes.BlockedValue,
			account.TotalMarginUsed.TryParseDecimal(), true)
		.TryAdd(PositionChangeTypes.CurrentPrice,
			account.AvailableToSpend.TryParseDecimal(), true), cancellationToken);
		foreach (var balance in account.SpotBalances ?? [])
			if (balance?.Symbol.IsEmpty() == false)
				await SendOutMessageAsync(new PositionChangeMessage
				{
					PortfolioName = _portfolioName,
					SecurityId = balance.Symbol.ToStockSharp(),
					ServerTime = time,
					OriginalTransactionId = transactionId,
				}
				.TryAdd(PositionChangeTypes.CurrentValue,
					balance.Amount.TryParseDecimal(), true)
				.TryAdd(PositionChangeTypes.BlockedValue,
					balance.PendingBalance.TryParseDecimal(), true),
					cancellationToken);
	}

	private async ValueTask SendAccountAsync(PacificaAccountInfoUpdate account,
		long transactionId, CancellationToken cancellationToken)
	{
		if (account is null || transactionId == 0)
			return;
		var time = account.Timestamp.ToPacificaTimeOrNow();
		UpdateServerTime(time);
		await SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = "USD".ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(PositionChangeTypes.BeginValue,
			account.Balance.TryParseDecimal(), true)
		.TryAdd(PositionChangeTypes.CurrentValue,
			account.AccountEquity.TryParseDecimal(), true)
		.TryAdd(PositionChangeTypes.BlockedValue,
			account.TotalMarginUsed.TryParseDecimal(), true)
		.TryAdd(PositionChangeTypes.CurrentPrice,
			account.AvailableToSpend.TryParseDecimal(), true), cancellationToken);
		foreach (var balance in account.SpotBalances ?? [])
			if (balance?.Symbol.IsEmpty() == false)
				await SendOutMessageAsync(new PositionChangeMessage
				{
					PortfolioName = _portfolioName,
					SecurityId = balance.Symbol.ToStockSharp(),
					ServerTime = time,
					OriginalTransactionId = transactionId,
				}
				.TryAdd(PositionChangeTypes.CurrentValue,
					balance.Amount.TryParseDecimal(), true)
				.TryAdd(PositionChangeTypes.BlockedValue,
					balance.PendingBalance.TryParseDecimal(), true),
					cancellationToken);
	}

	private async ValueTask SendPositionSnapshotAsync(
		PacificaPosition[] positions, long transactionId,
		CancellationToken cancellationToken)
	{
		var current = new HashSet<string>(StringComparer.Ordinal);
		foreach (var position in positions ?? [])
		{
			if (position?.Symbol.IsEmpty() != false)
				continue;
			current.Add(position.Symbol);
			await SendPositionAsync(position, transactionId, cancellationToken);
		}
		await SendMissingPositionsAsync(current, transactionId, ServerTime,
			cancellationToken);
	}

	private async ValueTask SendMissingPositionsAsync(HashSet<string> current,
		long transactionId, DateTime time, CancellationToken cancellationToken)
	{
		string[] missing;
		using (_sync.EnterScope())
		{
			missing = [.. _knownPositionSymbols.Where(symbol =>
				!current.Contains(symbol))];
			_knownPositionSymbols.Clear();
			_knownPositionSymbols.UnionWith(current);
		}
		foreach (var symbol in missing)
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = symbol.ToStockSharp(),
				ServerTime = time,
				OriginalTransactionId = transactionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, 0m, true),
				cancellationToken);
	}

	private ValueTask SendPositionAsync(PacificaPosition position,
		long transactionId, CancellationToken cancellationToken)
	{
		if (position?.Symbol.IsEmpty() != false || transactionId == 0)
			return default;
		var time = position.UpdatedAt.ToPacificaTimeOrNow();
		UpdateServerTime(time);
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = position.Symbol.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
			Side = position.Side.ToStockSharp(),
		}
		.TryAdd(PositionChangeTypes.CurrentValue,
			position.Amount.TryParseDecimal()?.Abs(), true)
		.TryAdd(PositionChangeTypes.AveragePrice,
			position.EntryPrice.TryParseDecimal(), true)
		.TryAdd(PositionChangeTypes.OrdersMargin,
			position.Margin.TryParseDecimal(), true)
		.TryAdd(PositionChangeTypes.VariationMargin,
			position.Funding.TryParseDecimal(), true)
		.TryAdd(PositionChangeTypes.LiquidationPrice,
			position.LiquidationPrice.TryParseDecimal(), true), cancellationToken);
	}

	private ValueTask SendPositionAsync(PacificaPositionUpdate position,
		long transactionId, CancellationToken cancellationToken)
	{
		if (position?.Symbol.IsEmpty() != false || transactionId == 0)
			return default;
		var time = position.Timestamp.ToPacificaTimeOrNow();
		UpdateServerTime(time);
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = position.Symbol.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
			Side = position.Side.ToStockSharp(),
		}
		.TryAdd(PositionChangeTypes.CurrentValue,
			position.Amount.TryParseDecimal()?.Abs(), true)
		.TryAdd(PositionChangeTypes.AveragePrice,
			position.EntryPrice.TryParseDecimal(), true)
		.TryAdd(PositionChangeTypes.OrdersMargin,
			position.Margin.TryParseDecimal(), true)
		.TryAdd(PositionChangeTypes.VariationMargin,
			position.Funding.TryParseDecimal(), true)
		.TryAdd(PositionChangeTypes.LiquidationPrice,
			position.LiquidationPrice.TryParseDecimal(), true), cancellationToken);
	}

	private async ValueTask SendOrderAsync(PacificaOrder order,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		var message = CreateOrderMessage(order, originalTransactionId);
		if (message is null)
			return;
		UpdateServerTime(message.ServerTime);
		await SendOutMessageAsync(message, cancellationToken);
	}

	private ExecutionMessage CreateOrderMessage(PacificaOrder order,
		long originalTransactionId)
	{
		if (order?.Symbol.IsEmpty() != false || order.OrderId <= 0)
			return null;
		var volume = order.InitialAmount.TryParseDecimal() ??
			order.Amount.TryParseDecimal();
		var filled = order.FilledAmount.TryParseDecimal() ?? 0m;
		var cancelled = order.CancelledAmount.TryParseDecimal() ?? 0m;
		var state = order.Status?.ToStockSharp() ??
			(order.EventType.IsFailure() ? OrderStates.Failed :
			order.EventType is PacificaOrderEvents.Cancel or
				PacificaOrderEvents.ForceCancel or PacificaOrderEvents.Expired
					? OrderStates.Done
					: OrderStates.Active);
		var time = (order.UpdatedAt > 0 ? order.UpdatedAt : order.CreatedAt)
			.ToPacificaTimeOrNow();
		var condition = new PacificaOrderCondition
		{
			IsReduceOnly = order.IsReduceOnly,
			TriggerPriceType = order.TriggerPriceType ??
				PacificaTriggerPriceTypes.LastTradePrice,
		};
		return new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = order.Side.ToStockSharp(),
			OrderVolume = volume,
			Balance = volume is decimal total
				? (total - filled - cancelled).Max(0m)
				: null,
			OrderPrice = order.InitialPrice.TryParseDecimal() ??
				order.Price.TryParseDecimal() ?? 0m,
			AveragePrice = order.AverageFilledPrice.TryParseDecimal(),
			OrderType = order.OrderType.ToStockSharp(),
			OrderState = state,
			OrderId = order.OrderId,
			UserOrderId = order.ClientOrderId,
			TransactionId = GetTransactionId(order.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
			PositionEffect = order.IsReduceOnly
				? OrderPositionEffects.CloseOnly
				: null,
			Condition = condition,
			Error = state == OrderStates.Failed
				? new InvalidOperationException(order.Reason.IsEmpty()
					? "Pacifica rejected the order."
					: order.Reason)
				: null,
		};
	}

	private async ValueTask SendActionOrderAsync(PacificaActionResponse response,
		long originalTransactionId, long transactionId, string fallbackSymbol,
		string fallbackClientOrderId, Sides side, decimal volume, decimal price,
		OrderTypes orderType, PacificaOrderCondition condition,
		CancellationToken cancellationToken)
	{
		if (response?.Data is null)
			throw new InvalidDataException(
				"Pacifica trading response contains no order data.");
		var time = response.Timestamp.ToPacificaTimeOrNow();
		UpdateServerTime(time);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = (response.Data.Symbol.IsEmpty()
				? fallbackSymbol
				: response.Data.Symbol).ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = side,
			OrderVolume = volume,
			Balance = volume,
			OrderPrice = price,
			OrderType = orderType,
			OrderState = OrderStates.Pending,
			OrderId = response.Data.OrderId,
			UserOrderId = response.Data.ClientOrderId.IsEmpty()
				? fallbackClientOrderId
				: response.Data.ClientOrderId,
			TransactionId = transactionId,
			OriginalTransactionId = originalTransactionId,
			PositionEffect = condition.IsReduceOnly
				? OrderPositionEffects.CloseOnly
				: null,
			Condition = condition,
		}, cancellationToken);
	}

	private ValueTask SendAccountTradeAsync(PacificaAccountTrade trade,
		long originalTransactionId, bool onlyNew,
		CancellationToken cancellationToken)
	{
		if (trade?.Symbol.IsEmpty() != false || trade.HistoryId <= 0)
			return default;
		var isNew = TryAcceptAccountTrade(trade.HistoryId);
		if (onlyNew && !isNew)
			return default;
		var time = trade.CreatedAt.ToPacificaTimeOrNow();
		UpdateServerTime(time);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = trade.Symbol.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = trade.Side.ToStockSharp(),
			OrderId = trade.OrderId,
			UserOrderId = trade.ClientOrderId,
			TradeId = trade.HistoryId,
			TradePrice = trade.Price.ParseDecimal("account trade price"),
			TradeVolume = trade.Amount.ParseDecimal("account trade amount"),
			Commission = trade.Fee.TryParseDecimal(),
			CommissionCurrency = "USD",
			PnL = trade.ProfitLoss.TryParseDecimal(),
			PositionEffect = trade.Side.ToPositionEffect(),
			TransactionId = GetTransactionId(trade.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private void ValidateOrder(PacificaMarket market, decimal volume,
		decimal? price, PacificaOrderCondition condition)
	{
		var lotSize = market.LotSize.ParseDecimal("lot size");
		if (volume <= 0 || !volume.IsMultipleOf(lotSize))
			throw new InvalidOperationException(
				"Pacifica order volume must be a positive multiple of " +
				lotSize.ToWire() + ".");
		ValidatePrice(market, price, "order price", price is not null);
		ValidatePrice(market, condition.TakeProfitPrice,
			"take-profit trigger price", false);
		ValidatePrice(market, condition.TakeProfitLimitPrice,
			"take-profit limit price", false);
		ValidatePrice(market, condition.StopLossPrice,
			"stop-loss trigger price", false);
		ValidatePrice(market, condition.StopLossLimitPrice,
			"stop-loss limit price", false);
		if (condition.TakeProfitLimitPrice is not null &&
			condition.TakeProfitPrice is null)
			throw new InvalidOperationException(
				"Pacifica take-profit limit price requires a trigger price.");
		if (condition.StopLossLimitPrice is not null &&
			condition.StopLossPrice is null)
			throw new InvalidOperationException(
				"Pacifica stop-loss limit price requires a trigger price.");
		_ = NormalizeBuilderCode(condition.BuilderCode);

		var referencePrice = price ?? GetPrice(market.Symbol)?.MarkPrice.TryParseDecimal();
		if (referencePrice is not > 0)
			throw new InvalidOperationException(
				"Pacifica current mark price is unavailable for notional validation.");
		var notional = volume * referencePrice.Value;
		if (market.MinimumOrderNotional.TryParseDecimal() is decimal minimum &&
			notional < minimum)
			throw new InvalidOperationException(
				"Pacifica order notional must be at least " + minimum.ToWire() + " USD.");
		if (market.MaximumOrderNotional.TryParseDecimal() is decimal maximum &&
			notional > maximum)
			throw new InvalidOperationException(
				"Pacifica order notional cannot exceed " + maximum.ToWire() + " USD.");
	}

	private static void ValidatePrice(PacificaMarket market, decimal? value,
		string fieldName, bool isRequired)
	{
		if (value is null)
		{
			if (isRequired)
				throw new InvalidOperationException(
					"Pacifica " + fieldName + " is required.");
			return;
		}
		var price = value.Value;
		var tickSize = market.TickSize.ParseDecimal("tick size");
		if (price <= 0 || !price.IsMultipleOf(tickSize))
			throw new InvalidOperationException(
				"Pacifica " + fieldName + " must be a positive multiple of " +
				tickSize.ToWire() + ".");
		if (market.MinimumPrice.TryParseDecimal() is decimal minimum &&
			price < minimum)
			throw new InvalidOperationException(
				"Pacifica " + fieldName + " must be at least " +
				minimum.ToWire() + ".");
		if (market.MaximumPrice.TryParseDecimal() is decimal maximum &&
			price > maximum)
			throw new InvalidOperationException(
				"Pacifica " + fieldName + " cannot exceed " +
				maximum.ToWire() + ".");
	}

	private static PacificaStopConfiguration CreateStop(decimal? triggerPrice,
		decimal? limitPrice, PacificaTriggerPriceTypes triggerPriceType)
		=> triggerPrice is null
			? null
			: new()
			{
				StopPrice = triggerPrice.Value.ToWire(),
				LimitPrice = limitPrice?.ToWire(),
				ClientOrderId = Guid.NewGuid().ToString("D"),
				TriggerPriceType = triggerPriceType,
			};

	private static string NormalizeBuilderCode(string value)
	{
		value = value?.Trim();
		if (value.IsEmpty())
			return null;
		if (value.Length is < 3 or > 16 || !value.All(char.IsLetterOrDigit))
			throw new InvalidOperationException(
				"Pacifica builder code must contain 3 to 16 alphanumeric characters.");
		return value;
	}

	private static string CreateClientOrderId(string userOrderId)
	{
		if (userOrderId.IsEmpty())
			return Guid.NewGuid().ToString("D");
		if (!Guid.TryParse(userOrderId.Trim(), out var value))
			throw new InvalidOperationException(
				"Pacifica client order ID must be a full UUID.");
		return value.ToString("D");
	}

	private static void ResolveOrderIdentity(long? numericOrderId,
		string stringOrderId, string userOrderId, string operation,
		out long? orderId, out string clientOrderId)
	{
		orderId = numericOrderId is > 0 ? numericOrderId : null;
		clientOrderId = null;
		if (orderId is null && !stringOrderId.IsEmpty())
		{
			var value = stringOrderId.Trim();
			if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
				out var parsed) && parsed > 0)
				orderId = parsed;
			else if (Guid.TryParse(value, out var clientId))
				clientOrderId = clientId.ToString("D");
			else
				throw new InvalidOperationException(
					"Pacifica " + operation +
					" order identifier must be a positive number or UUID.");
		}
		if (orderId is null && clientOrderId.IsEmpty() && !userOrderId.IsEmpty())
			clientOrderId = CreateClientOrderId(userOrderId);
		if (orderId is null && clientOrderId.IsEmpty())
			throw new InvalidOperationException(
				"Pacifica " + operation +
				" requires an exchange order ID or client order UUID.");
	}

	private HashSet<string> GetOrderSymbols(OrderStatusMessage statusMsg)
	{
		var symbols = new HashSet<string>(StringComparer.Ordinal);
		if (!statusMsg.SecurityId.SecurityCode.IsEmpty())
			symbols.Add(GetMarket(statusMsg.SecurityId).Symbol);
		foreach (var securityId in statusMsg.SecurityIds)
			if (!securityId.SecurityCode.IsEmpty())
				symbols.Add(GetMarket(securityId).Symbol);
		return symbols;
	}

	private static bool IsOrderMatch(ExecutionMessage order,
		OrderStatusMessage filter, HashSet<string> symbols)
	{
		if (symbols.Count > 0 && !symbols.Contains(order.SecurityId.SecurityCode))
			return false;
		if (filter.OrderId is long orderId && order.OrderId != orderId)
			return false;
		if (!filter.OrderStringId.IsEmpty() &&
			!filter.OrderStringId.Equals(order.OrderId?.ToString(
				CultureInfo.InvariantCulture), StringComparison.Ordinal))
			return false;
		if (!filter.UserOrderId.IsEmpty() &&
			!filter.UserOrderId.Equals(order.UserOrderId,
				StringComparison.OrdinalIgnoreCase))
			return false;
		if (filter.Side is Sides side && order.Side != side)
			return false;
		if (filter.Volume is decimal volume && order.OrderVolume != volume)
			return false;
		if (filter.States.Length > 0 &&
			(order.OrderState is not OrderStates state ||
				!filter.States.Contains(state)))
			return false;
		if (filter.From is DateTime from && order.ServerTime < from.EnsureUtc())
			return false;
		if (filter.To is DateTime to && order.ServerTime > to.EnsureUtc())
			return false;
		return true;
	}

	private static bool IsTradeMatch(PacificaAccountTrade trade,
		OrderStatusMessage filter, HashSet<string> symbols)
	{
		if (symbols.Count > 0 && !symbols.Contains(trade.Symbol))
			return false;
		if (filter.OrderId is long orderId && trade.OrderId != orderId)
			return false;
		if (!filter.OrderStringId.IsEmpty() &&
			!filter.OrderStringId.Equals(trade.OrderId.ToString(
				CultureInfo.InvariantCulture), StringComparison.Ordinal))
			return false;
		if (!filter.UserOrderId.IsEmpty() &&
			!filter.UserOrderId.Equals(trade.ClientOrderId,
				StringComparison.OrdinalIgnoreCase))
			return false;
		if (filter.Side is Sides side && trade.Side.ToStockSharp() != side)
			return false;
		var time = trade.CreatedAt.ToPacificaTimeOrNow();
		if (filter.From is DateTime from && time < from.EnsureUtc())
			return false;
		if (filter.To is DateTime to && time > to.EnsureUtc())
			return false;
		return true;
	}

	private void ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(_portfolioName))
			throw new InvalidOperationException(
				"Unknown Pacifica portfolio '" + portfolioName + "'.");
	}

	private static bool IsConditional(PacificaOrderTypes orderType)
		=> orderType is not (PacificaOrderTypes.Limit or PacificaOrderTypes.Market);
}
