namespace StockSharp.MetaApi;

public partial class MetaApiMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsurePortfolio(regMsg.PortfolioName);
		var request = regMsg.ToTradeRequest();
		var response = await Rest.TradeAsync(PortfolioName, request,
			cancellationToken);
		EnsureTradeSuccess(response);
		var orderId = response.OrderId.IsEmpty(response.PositionId)
			.ThrowIfEmpty(nameof(MetaApiTradeResponse.OrderId));
		using (_sync.EnterScope())
		{
			_orderTransactions[orderId] = regMsg.TransactionId;
			_transactionOrders[regMsg.TransactionId] = orderId;
		}

		var isMarket = request.ActionType is "ORDER_TYPE_BUY" or "ORDER_TYPE_SELL";
		var order = new MetaApiOrder
		{
			Id = orderId,
			PositionId = response.PositionId,
			Type = request.ActionType,
			State = isMarket ? "ORDER_STATE_FILLED" : "ORDER_STATE_PLACED",
			Symbol = request.Symbol,
			Time = DateTime.UtcNow,
			OpenPrice = request.OpenPrice,
			Volume = request.Volume ?? 0,
			CurrentVolume = isMarket ? 0 : request.Volume,
			StopLoss = request.StopLoss,
			TakeProfit = request.TakeProfit,
			ExpirationTime = request.Expiration?.Time,
			Comment = request.Comment,
			ClientId = request.ClientId,
			Magic = request.Magic,
		};
		using (_sync.EnterScope())
			_orders[orderId] = order;
		await SendOrderAsync(order, regMsg.TransactionId, true, cancellationToken);
		this.AddInfoLog("MetaApi submitted {0} order {1} for {2} {3} {4}.",
			request.ActionType, orderId, regMsg.Side, regMsg.Volume, request.Symbol);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		EnsurePortfolio(replaceMsg.PortfolioName);
		var orderId = ResolveNativeId(replaceMsg.OldOrderStringId,
			replaceMsg.OldOrderId, replaceMsg.OriginalTransactionId);
		MetaApiOrder order;
		using (_sync.EnterScope())
			_orders.TryGetValue(orderId, out order);
		if (order is null)
		{
			order = (await Rest.GetOrdersAsync(PortfolioName, cancellationToken) ?? [])
				.FirstOrDefault(item => item.Id.EqualsIgnoreCase(orderId))
				?? throw new InvalidOperationException(
					$"MetaApi active order '{orderId}' was not found.");
		}
		if (!replaceMsg.SecurityId.SecurityCode.IsEmpty() &&
			!replaceMsg.SecurityId.ToSymbol().EqualsIgnoreCase(order.Symbol))
			throw new NotSupportedException("MetaApi cannot change an order symbol in place.");
		if (replaceMsg.Volume <= 0 || replaceMsg.Price <= 0)
			throw new InvalidOperationException(
				"MetaApi order modification requires positive price and volume.");
		if (replaceMsg.Volume != order.Volume)
			throw new NotSupportedException(
				"MetaApi cannot change pending-order volume in place.");
		var condition = replaceMsg.Condition as MetaApiOrderCondition;
		var response = await Rest.TradeAsync(PortfolioName, new()
		{
			ActionType = "ORDER_MODIFY",
			OrderId = orderId,
			OpenPrice = replaceMsg.Price,
			StopLoss = condition?.StopLoss,
			TakeProfit = condition?.TakeProfit,
			StopLossUnits = condition?.StopLoss is null ? null : "ABSOLUTE_PRICE",
			TakeProfitUnits = condition?.TakeProfit is null ? null : "ABSOLUTE_PRICE",
			Expiration = new()
			{
				Type = replaceMsg.TillDate is null
					? "ORDER_TIME_GTC" : "ORDER_TIME_SPECIFIED",
				Time = replaceMsg.TillDate?.ToUniversalTime(),
			},
		}, cancellationToken);
		EnsureTradeSuccess(response);

		order.OpenPrice = replaceMsg.Price;
		order.Volume = replaceMsg.Volume;
		order.CurrentVolume = replaceMsg.Volume;
		order.StopLoss = condition?.StopLoss;
		order.TakeProfit = condition?.TakeProfit;
		order.ExpirationTime = replaceMsg.TillDate?.ToUniversalTime();
		order.State = "ORDER_STATE_PLACED";
		using (_sync.EnterScope())
		{
			_orders[orderId] = order;
			_orderTransactions[orderId] = replaceMsg.TransactionId;
			_transactionOrders[replaceMsg.TransactionId] = orderId;
		}
		await SendOrderAsync(order, replaceMsg.TransactionId, true,
			cancellationToken);
		this.AddInfoLog("MetaApi modified order {0}.", orderId);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePortfolio(cancelMsg.PortfolioName);
		var nativeId = ResolveNativeId(cancelMsg.OrderStringId, cancelMsg.OrderId,
			cancelMsg.OriginalTransactionId);
		MetaApiOrder order;
		MetaApiPosition position;
		using (_sync.EnterScope())
		{
			_orders.TryGetValue(nativeId, out order);
			_positions.TryGetValue(nativeId, out position);
			_cancelTransactions[nativeId] = cancelMsg.TransactionId;
		}

		try
		{
			MetaApiTradeRequest request;
			if (position is not null)
			{
				var partial = cancelMsg.Volume is > 0 &&
					cancelMsg.Volume < position.Volume;
				request = new()
				{
					ActionType = partial ? "POSITION_PARTIAL" : "POSITION_CLOSE_ID",
					PositionId = nativeId,
					Volume = partial ? cancelMsg.Volume : null,
				};
			}
			else
			{
				if (order is null)
				{
					order = (await Rest.GetOrdersAsync(PortfolioName,
						cancellationToken) ?? [])
						.FirstOrDefault(item => item.Id.EqualsIgnoreCase(nativeId))
						?? throw new InvalidOperationException(
							$"MetaApi active order or position '{nativeId}' was not found.");
				}
				request = new() { ActionType = "ORDER_CANCEL", OrderId = nativeId };
			}

			EnsureTradeSuccess(await Rest.TradeAsync(PortfolioName, request,
				cancellationToken));
			if (order is not null)
			{
				order.State = "ORDER_STATE_CANCELED";
				order.DoneTime = DateTime.UtcNow;
				order.CurrentVolume = 0;
				await SendOrderAsync(order, cancelMsg.TransactionId, true,
					cancellationToken);
			}
			this.AddInfoLog("MetaApi executed {0} for {1}.", request.ActionType,
				nativeId);
		}
		catch
		{
			using (_sync.EnterScope())
				_cancelTransactions.Remove(nativeId);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		if (!statusMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
			{
				if (_orderStatusSubscriptionId == statusMsg.OriginalTransactionId)
					_orderStatusSubscriptionId = 0;
			}
			return;
		}
		EnsurePortfolio(statusMsg.PortfolioName);
		if (!statusMsg.IsHistoryOnly())
		{
			using (_sync.EnterScope())
				_orderStatusSubscriptionId = statusMsg.TransactionId;
		}
		try
		{
			await SendOrderSnapshotAsync(statusMsg, cancellationToken);
			if (statusMsg.IsHistoryOnly())
				await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
					cancellationToken);
			else
				await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (_orderStatusSubscriptionId == statusMsg.TransactionId)
					_orderStatusSubscriptionId = 0;
			}
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		if (!lookupMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
			{
				if (_portfolioSubscriptionId == lookupMsg.OriginalTransactionId)
				{
					_portfolioSubscriptionId = 0;
				}
			}
			return;
		}
		EnsurePortfolio(lookupMsg.PortfolioName);
		if (!lookupMsg.IsHistoryOnly())
		{
			using (_sync.EnterScope())
			{
				_portfolioSubscriptionId = lookupMsg.TransactionId;
			}
		}
		try
		{
			await SendPortfolioSnapshotAsync(lookupMsg.TransactionId,
				cancellationToken);
			if (lookupMsg.IsHistoryOnly())
				await SendSubscriptionFinishedAsync(lookupMsg.TransactionId,
					cancellationToken);
			else
				await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (_portfolioSubscriptionId == lookupMsg.TransactionId)
				{
					_portfolioSubscriptionId = 0;
				}
			}
			throw;
		}
	}

	private async Task SendOrderSnapshotAsync(OrderStatusMessage filter,
		CancellationToken cancellationToken)
	{
		var active = await Rest.GetOrdersAsync(PortfolioName, cancellationToken) ?? [];
		var maximum = (int)Math.Clamp(filter.Count ?? 1000, 1, 10000);
		var skip = (int)Math.Clamp(filter.Skip ?? 0, 0, int.MaxValue);
		var fetchLimit = (int)Math.Min(10000L, (long)maximum + skip);
		var from = (filter.From ?? DateTime.UnixEpoch).ToUniversalTime();
		var to = (filter.To ?? DateTime.UtcNow).ToUniversalTime();
		if (from > to)
			throw new ArgumentException("MetaApi order history start time exceeds end time.");
		var history = new List<MetaApiOrder>();
		var deals = new List<MetaApiDeal>();
		for (var offset = 0; history.Count < fetchLimit; offset += 1000)
		{
			var pageLimit = Math.Min(1000, fetchLimit - history.Count);
			var page = await Rest.GetHistoryOrdersAsync(PortfolioName, from, to,
				offset, pageLimit, cancellationToken) ?? [];
			history.AddRange(page);
			if (page.Length < pageLimit)
				break;
		}
		for (var offset = 0; deals.Count < fetchLimit; offset += 1000)
		{
			var pageLimit = Math.Min(1000, fetchLimit - deals.Count);
			var page = await Rest.GetDealsAsync(PortfolioName, from, to, offset,
				pageLimit, cancellationToken) ?? [];
			deals.AddRange(page);
			if (page.Length < pageLimit)
				break;
		}

		var orders = active.Concat(history)
			.Where(item => item?.Id.IsEmpty() == false)
			.GroupBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.OrderByDescending(item =>
				item.DoneTime ?? item.Time).First())
			.Where(item => IsOrderMatch(item, filter))
			.OrderByDescending(static item => item.DoneTime ?? item.Time)
			.Skip(skip).Take(maximum).ToArray();
		foreach (var order in orders)
		{
			using (_sync.EnterScope())
				_orders[order.Id] = order;
			await SendOrderAsync(order, filter.TransactionId, true,
				cancellationToken);
		}
		var selectedIds = orders.Select(static item => item.Id)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (var deal in deals.Where(item => selectedIds.Contains(item.OrderId))
			.OrderBy(static item => item.Time))
			await SendDealAsync(deal, filter.TransactionId, true, cancellationToken);
		this.AddDebugLog("MetaApi order lookup returned {0} orders and {1} deals.",
			orders.Length, deals.Count);
	}

	private async Task SendPortfolioSnapshotAsync(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var account = await Rest.GetAccountInformationAsync(PortfolioName,
			cancellationToken);
		var positions = await Rest.GetPositionsAsync(PortfolioName,
			cancellationToken) ?? [];
		using (_sync.EnterScope())
		{
			_accountInformation = account;
			_positions.Clear();
			foreach (var position in positions)
				if (position?.Id.IsEmpty() == false)
					_positions[position.Id] = position;
			_receivedPositions = true;
		}
		await SendPortfolioAsync(originalTransactionId, account,
			cancellationToken);
		await SendAllPositionsAsync(originalTransactionId, true, cancellationToken);
	}

	private async ValueTask UpdateAccountInformationAsync(
		MetaApiAccountInformation account, CancellationToken cancellationToken)
	{
		if (account is null)
			return;
		long transactionId;
		using (_sync.EnterScope())
		{
			_accountInformation = account;
			transactionId = _portfolioSubscriptionId;
		}
		if (transactionId != 0)
			await SendPortfolioAsync(transactionId, account, cancellationToken);
	}

	private async ValueTask UpdateAccountMetricsAsync(decimal? equity,
		decimal? margin, decimal? freeMargin, decimal? marginLevel,
		CancellationToken cancellationToken)
	{
		MetaApiAccountInformation account;
		long transactionId;
		using (_sync.EnterScope())
		{
			account = _accountInformation;
			if (account is null)
				return;
			if (equity is not null)
				account.Equity = equity.Value;
			if (margin is not null)
				account.Margin = margin.Value;
			if (freeMargin is not null)
				account.FreeMargin = freeMargin.Value;
			if (marginLevel is not null)
				account.MarginLevel = marginLevel;
			transactionId = _portfolioSubscriptionId;
		}
		if (transactionId != 0)
			await SendPortfolioAsync(transactionId, account, cancellationToken);
	}

	private async ValueTask SendPortfolioAsync(long originalTransactionId,
		MetaApiAccountInformation account, CancellationToken cancellationToken)
	{
		if (account is null)
			return;
		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = PortfolioName,
			BoardCode = MetaApiExtensions.BoardCode,
			Currency = account.Currency.ToCurrency(),
		}, cancellationToken);
		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = PortfolioName,
			SecurityId = SecurityId.Money,
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.BeginValue, account.Balance, true)
		.TryAdd(PositionChangeTypes.CurrentValue, account.Equity, true)
		.TryAdd(PositionChangeTypes.BlockedValue, account.Margin, true)
		.TryAdd(PositionChangeTypes.BuyOrdersMargin, account.FreeMargin, true)
		.TryAdd(PositionChangeTypes.Leverage, account.Leverage, true)
		.TryAdd(PositionChangeTypes.Currency, account.Currency.ToCurrency()),
			cancellationToken);
	}

	private async ValueTask ReplaceOrdersAsync(MetaApiOrder[] orders,
		CancellationToken cancellationToken)
	{
		long transactionId;
		using (_sync.EnterScope())
		{
			_orders.Clear();
			foreach (var order in orders)
				if (order?.Id.IsEmpty() == false)
					_orders[order.Id] = order;
			_receivedOrders = true;
			transactionId = _orderStatusSubscriptionId;
		}
		if (transactionId == 0)
			return;
		foreach (var order in orders)
			await SendOrderAsync(order, transactionId, false, cancellationToken);
	}

	private async ValueTask ReplacePositionsAsync(MetaApiPosition[] positions,
		CancellationToken cancellationToken)
	{
		long transactionId;
		using (_sync.EnterScope())
		{
			_positions.Clear();
			foreach (var position in positions)
				if (position?.Id.IsEmpty() == false)
					_positions[position.Id] = position;
			_receivedPositions = true;
			transactionId = _portfolioSubscriptionId;
		}
		if (transactionId != 0)
			await SendAllPositionsAsync(transactionId, false, cancellationToken);
	}

	private async ValueTask ProcessTerminalUpdateAsync(
		MetaApiSynchronizationPacket packet,
		CancellationToken cancellationToken)
	{
		await UpdateAccountInformationAsync(packet.AccountInformation,
			cancellationToken);

		var updatedPositions = packet.UpdatedPositions ?? [];
		var removedPositionIds = packet.RemovedPositionIds ?? [];
		var affectedSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		long portfolioTransactionId;
		using (_sync.EnterScope())
		{
			foreach (var position in updatedPositions)
			{
				if (position?.Id.IsEmpty() == false)
				{
					_positions[position.Id] = position;
					if (!position.Symbol.IsEmpty())
						affectedSymbols.Add(position.Symbol);
				}
			}
			foreach (var id in removedPositionIds)
			{
				if (_positions.Remove(id, out var removed))
				{
					if (!removed.Symbol.IsEmpty())
						affectedSymbols.Add(removed.Symbol);
				}
			}
			portfolioTransactionId = _portfolioSubscriptionId;
		}
		if (portfolioTransactionId != 0)
		{
			foreach (var symbol in affectedSymbols)
				await SendPositionAsync(symbol, portfolioTransactionId, false,
					cancellationToken);
		}

		var updatedOrders = packet.UpdatedOrders ?? [];
		var historyOrders = packet.HistoryOrders ?? [];
		var completedOrderIds = packet.CompletedOrderIds ?? [];
		long orderTransactionId;
		using (_sync.EnterScope())
		{
			foreach (var order in updatedOrders.Concat(historyOrders))
				if (order?.Id.IsEmpty() == false)
					_orders[order.Id] = order;
			foreach (var id in completedOrderIds)
			{
				if (_orders.TryGetValue(id, out var order) &&
					order.State.ToOrderState() == OrderStates.Active)
				{
					order.State = "ORDER_STATE_CANCELED";
					order.CurrentVolume = 0;
					order.DoneTime = DateTime.UtcNow;
				}
			}
			orderTransactionId = _orderStatusSubscriptionId;
		}
		foreach (var order in updatedOrders.Concat(historyOrders))
		{
			if (ShouldReportOrder(order?.Id, orderTransactionId))
				await SendOrderAsync(order, orderTransactionId, false,
					cancellationToken);
		}
		foreach (var id in completedOrderIds)
		{
			MetaApiOrder order;
			using (_sync.EnterScope())
				_orders.TryGetValue(id, out order);
			if (order is not null && ShouldReportOrder(id, orderTransactionId))
				await SendOrderAsync(order, orderTransactionId, false,
					cancellationToken);
		}
		await ProcessDealsAsync(packet.Deals ?? [],
			cancellationToken);
	}

	private async ValueTask ProcessDealsAsync(MetaApiDeal[] deals,
		CancellationToken cancellationToken)
	{
		long transactionId;
		using (_sync.EnterScope())
			transactionId = _orderStatusSubscriptionId;
		foreach (var deal in deals)
		{
			if (ShouldReportDeal(deal, transactionId))
				await SendDealAsync(deal, transactionId, false, cancellationToken);
		}
	}

	private bool ShouldReportOrder(string orderId, long subscriptionId)
	{
		if (subscriptionId != 0 || orderId.IsEmpty())
			return subscriptionId != 0;
		using (_sync.EnterScope())
			return _orderTransactions.ContainsKey(orderId) ||
				_cancelTransactions.ContainsKey(orderId);
	}

	private bool ShouldReportDeal(MetaApiDeal deal, long subscriptionId)
	{
		if (subscriptionId != 0)
			return true;
		if (deal is null)
			return false;
		using (_sync.EnterScope())
			return (!deal.OrderId.IsEmpty() &&
					_orderTransactions.ContainsKey(deal.OrderId)) ||
				(!deal.PositionId.IsEmpty() &&
					_cancelTransactions.ContainsKey(deal.PositionId));
	}

	private async ValueTask SendOrderAsync(MetaApiOrder order,
		long originalTransactionId, bool isForced, CancellationToken cancellationToken)
	{
		if (order?.Id.IsEmpty() != false || order.Symbol.IsEmpty())
			return;
		var signature = new OrderSignature(
			order.State,
			order.OpenPrice,
			order.CurrentPrice,
			order.Volume,
			order.CurrentVolume,
			order.StopLoss,
			order.TakeProfit,
			order.DoneTime);
		long transactionId;
		long cancelTransactionId;
		using (_sync.EnterScope())
		{
			if (!isForced && _orderSignatures.TryGetValue(order.Id, out var previous) &&
				previous.Equals(signature))
				return;
			_orderSignatures[order.Id] = signature;
			if (!_orderTransactions.TryGetValue(order.Id, out transactionId))
			{
				transactionId = TransactionIdGenerator.GetNextId();
				_orderTransactions[order.Id] = transactionId;
				_transactionOrders[transactionId] = order.Id;
			}
			_cancelTransactions.TryGetValue(order.Id, out cancelTransactionId);
			if (order.State.ToOrderState() is OrderStates.Done or OrderStates.Failed)
				_cancelTransactions.Remove(order.Id);
		}
		if (originalTransactionId == 0)
			originalTransactionId = cancelTransactionId != 0
				? cancelTransactionId : transactionId;

		var state = order.State.ToOrderState();
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId,
			TransactionId = transactionId,
			OrderId = order.Id.ToNumericId(),
			OrderStringId = order.Id,
			SecurityId = order.Symbol.ToSecurityId(),
			PortfolioName = PortfolioName,
			Side = order.Type.ToSide(),
			OrderType = order.Type.ToOrderType(),
			OrderPrice = order.OpenPrice ?? 0,
			OrderVolume = order.Volume,
			Balance = state == OrderStates.Active
				? order.CurrentVolume ?? order.Volume : 0,
			AveragePrice = order.CurrentPrice,
			OrderState = state,
			ServerTime = NormalizeTime(order.DoneTime ?? order.Time),
			ExpiryDate = order.ExpirationTime,
			Comment = order.Comment,
			Condition = new MetaApiOrderCondition
			{
				StopLoss = order.StopLoss,
				TakeProfit = order.TakeProfit,
				Magic = order.Magic,
				Comment = order.Comment,
				ClientId = order.ClientId,
			},
			Error = state == OrderStates.Failed
				? new InvalidOperationException(order.Reason.IsEmpty(
					$"MetaApi rejected order {order.Id}.")) : null,
		}, cancellationToken);
	}

	private ValueTask SendDealAsync(MetaApiDeal deal, long originalTransactionId,
		bool isForced, CancellationToken cancellationToken)
	{
		if (deal?.Id.IsEmpty() != false || deal.Symbol.IsEmpty() ||
			deal.Type?.Contains("BUY", StringComparison.OrdinalIgnoreCase) != true &&
			deal.Type?.Contains("SELL", StringComparison.OrdinalIgnoreCase) != true)
			return default;
		long transactionId;
		long cancelTransactionId;
		using (_sync.EnterScope())
		{
			if (!isForced && !_reportedDeals.Add(deal.Id))
				return default;
			_reportedDeals.Add(deal.Id);
			if (!_orderTransactions.TryGetValue(deal.OrderId, out transactionId))
			{
				transactionId = TransactionIdGenerator.GetNextId();
				if (!deal.OrderId.IsEmpty())
				{
					_orderTransactions[deal.OrderId] = transactionId;
					_transactionOrders[transactionId] = deal.OrderId;
				}
			}
			_cancelTransactions.TryGetValue(deal.PositionId, out cancelTransactionId);
			if (cancelTransactionId != 0)
				_cancelTransactions.Remove(deal.PositionId);
		}
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originalTransactionId == 0
				? cancelTransactionId == 0 ? transactionId : cancelTransactionId
				: originalTransactionId,
			TransactionId = transactionId,
			OrderId = deal.OrderId.ToNumericId(),
			OrderStringId = deal.OrderId,
			TradeId = deal.Id.ToNumericId(),
			TradeStringId = deal.Id,
			SecurityId = deal.Symbol.ToSecurityId(),
			PortfolioName = PortfolioName,
			Side = deal.Type.ToSide(),
			TradePrice = deal.Price,
			TradeVolume = deal.Volume,
			ServerTime = NormalizeTime(deal.Time),
			Commission = deal.Commission,
			PnL = deal.Profit,
		}, cancellationToken);
	}

	private async ValueTask SendAllPositionsAsync(long originalTransactionId,
		bool isForced, CancellationToken cancellationToken)
	{
		string[] symbols;
		using (_sync.EnterScope())
			symbols = [.. _positions.Values.Select(static item => item.Symbol)
				.Where(static symbol => !symbol.IsEmpty())
				.Concat(_positionSignatures.Keys)
				.Distinct(StringComparer.OrdinalIgnoreCase)];
		foreach (var symbol in symbols)
			await SendPositionAsync(symbol, originalTransactionId, isForced,
				cancellationToken);
	}

	private async ValueTask SendPositionAsync(string symbol, long originalTransactionId,
		bool isForced, CancellationToken cancellationToken)
	{
		MetaApiPosition[] positions;
		using (_sync.EnterScope())
			positions = [.. _positions.Values.Where(item =>
				item.Symbol.EqualsIgnoreCase(symbol))];
		var netVolume = positions.Sum(static item =>
			item.Type.ToSide() == Sides.Sell ? -item.Volume : item.Volume);
		var pricedPositions = netVolume > 0
			? positions.Where(static item => item.Type.ToSide() == Sides.Buy).ToArray()
			: netVolume < 0
				? positions.Where(static item => item.Type.ToSide() == Sides.Sell).ToArray()
				: [];
		var pricedVolume = pricedPositions.Sum(static item => Math.Abs(item.Volume));
		var averagePrice = pricedVolume > 0
			? pricedPositions.Sum(static item =>
				item.OpenPrice * Math.Abs(item.Volume)) / pricedVolume : 0;
		var currentPrice = pricedVolume > 0
			? pricedPositions.Sum(static item =>
				item.CurrentPrice * Math.Abs(item.Volume)) / pricedVolume : 0;
		var unrealized = positions.Sum(static item =>
			item.UnrealizedProfit ?? item.Profit ?? 0);
		var realized = positions.Sum(static item => item.RealizedProfit ?? 0);
		var commission = positions.Sum(static item => item.Commission ?? 0);
		var signature = new PositionSignature(netVolume, averagePrice, currentPrice,
			unrealized, realized, commission);
		using (_sync.EnterScope())
		{
			if (!isForced && _positionSignatures.TryGetValue(symbol, out var previous) &&
				previous.Equals(signature))
				return;
			_positionSignatures[symbol] = signature;
		}
		var time = positions.Select(static item => item.UpdateTime ?? item.Time)
			.DefaultIfEmpty(DateTime.UtcNow).Max();
		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = PortfolioName,
			SecurityId = symbol.ToSecurityId(),
			ServerTime = NormalizeTime(time),
		}
		.TryAdd(PositionChangeTypes.CurrentValue, netVolume, true)
		.TryAdd(PositionChangeTypes.AveragePrice, averagePrice, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, currentPrice, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, unrealized, true)
		.TryAdd(PositionChangeTypes.RealizedPnL, realized, true)
		.TryAdd(PositionChangeTypes.Commission, commission, true),
			cancellationToken);
	}

	private string ResolveNativeId(string stringId, long? numericId,
		long originalTransactionId)
	{
		if (!stringId.IsEmpty())
			return stringId;
		using (_sync.EnterScope())
		{
			if (_transactionOrders.TryGetValue(originalTransactionId, out var mapped))
				return mapped;
			if (numericId is > 0)
			{
				var value = numericId.Value.ToString(CultureInfo.InvariantCulture);
				var matches = _orders.Keys.Concat(_positions.Keys)
					.Where(id => id.ToNumericId() == numericId)
					.Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
				return matches.Length == 1 ? matches[0] : value;
			}
		}
		throw new InvalidOperationException(
			LocalizedStrings.OrderNoExchangeId.Put(originalTransactionId));
	}

	private static bool IsOrderMatch(MetaApiOrder order, OrderStatusMessage filter)
	{
		var time = (order.DoneTime ?? order.Time).ToUniversalTime();
		if (filter.From is { } from && time < from.ToUniversalTime() ||
			filter.To is { } to && time > to.ToUniversalTime())
			return false;
		if (!filter.OrderStringId.IsEmpty() &&
			!order.Id.EqualsIgnoreCase(filter.OrderStringId))
			return false;
		if (filter.OrderId is > 0 && order.Id.ToNumericId() != filter.OrderId)
			return false;
		if (!filter.SecurityId.SecurityCode.IsEmpty() &&
			!order.Symbol.EqualsIgnoreCase(filter.SecurityId.SecurityCode))
			return false;
		if (filter.SecurityIds.Length > 0 && !filter.SecurityIds.Any(id =>
			id.SecurityCode.EqualsIgnoreCase(order.Symbol)))
			return false;
		if (filter.Side is not null && order.Type.ToSide() != filter.Side)
			return false;
		if (filter.States.Length > 0 && !filter.States.Contains(order.State.ToOrderState()))
			return false;
		return true;
	}

	private static void EnsureTradeSuccess(MetaApiTradeResponse response)
	{
		if (response is null)
			throw new InvalidDataException("MetaApi returned no trade response.");
		if (response.StringCode is "TRADE_RETCODE_DONE" or
			"TRADE_RETCODE_PLACED" or "TRADE_RETCODE_DONE_PARTIAL" ||
			response.NumericCode is 10008 or 10009 or 10010)
			return;
		throw new InvalidOperationException(
			$"MetaApi trade failed ({response.NumericCode}/{response.StringCode}): " +
			response.Message.IsEmpty("unknown reason"));
	}
}
