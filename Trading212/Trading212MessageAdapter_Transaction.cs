namespace StockSharp.Trading212;

public partial class Trading212MessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		ResolvePortfolio(regMsg.PortfolioName);
		if (regMsg.Volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Volume), regMsg.Volume,
				"Trading 212 requires a positive order quantity.");
		if ((regMsg.TimeInForce ?? TimeInForce.PutInQueue) != TimeInForce.PutInQueue)
			throw new NotSupportedException("Trading 212 does not expose IOC or FOK through the Public API.");

		var instrument = await ResolveInstrument(regMsg.SecurityId.SecurityCode, cancellationToken);
		var securityType = instrument.Type.ToSecurityType();
		if (securityType is not SecurityTypes.Stock and not SecurityTypes.Etf)
			throw new NotSupportedException(
				$"Trading 212 Public API order routing is supported only for stocks and ETFs, not {instrument.Type}.");

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not OrderTypes.Limit and not OrderTypes.Market and not OrderTypes.Conditional)
			throw new NotSupportedException($"Trading 212 does not support {orderType} orders.");
		var condition = regMsg.Condition as Trading212OrderCondition ?? new();
		if (condition.IsExtendedHours && orderType != OrderTypes.Market)
			throw new NotSupportedException(
				"Trading 212 exposes extended-hours execution only on its market-order endpoint.");
		if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Price), regMsg.Price,
				"A positive limit price is required.");
		if (orderType == OrderTypes.Conditional && condition.StopPrice is not > 0)
			throw new ArgumentOutOfRangeException(nameof(condition.StopPrice), condition.StopPrice,
				"A positive stop price is required for Trading 212 conditional orders.");

		var quantity = regMsg.Side == Sides.Sell ? -regMsg.Volume : regMsg.Volume;
		var validity = orderType == OrderTypes.Market ? null : regMsg.TillDate.ToNativeTimeValidity();
		Trading212Order order;
		switch (orderType)
		{
			case OrderTypes.Market:
				if (regMsg.TillDate != null)
					throw new NotSupportedException(
						"The Trading 212 market-order endpoint does not accept an expiry instruction.");
				order = await _client.PlaceMarketOrder(new()
				{
					Ticker = instrument.Ticker,
					Quantity = quantity,
					IsExtendedHours = condition.IsExtendedHours,
				}, cancellationToken);
				break;
			case OrderTypes.Limit:
				order = await _client.PlaceLimitOrder(new()
				{
					Ticker = instrument.Ticker,
					Quantity = quantity,
					LimitPrice = regMsg.Price,
					TimeValidity = validity,
				}, cancellationToken);
				break;
			default:
				if (regMsg.Price > 0)
				{
					order = await _client.PlaceStopLimitOrder(new()
					{
						Ticker = instrument.Ticker,
						Quantity = quantity,
						StopPrice = condition.StopPrice.Value,
						LimitPrice = regMsg.Price,
						TimeValidity = validity,
					}, cancellationToken);
				}
				else
				{
					order = await _client.PlaceStopOrder(new()
					{
						Ticker = instrument.Ticker,
						Quantity = quantity,
						StopPrice = condition.StopPrice.Value,
						TimeValidity = validity,
					}, cancellationToken);
				}
				break;
		}

		if (order == null || order.Id <= 0)
			throw new InvalidOperationException("Trading 212 accepted the request without returning an order ID.");
		CompleteOrder(order, regMsg, instrument, condition, validity);
		_orderTransactions[order.Id] = regMsg.TransactionId;
		_activeOrders.Add(order.Id);
		_ordersAwaitingHistory.Add(order.Id);
		await ProcessOrder(order, regMsg.TransactionId, false, true, null, cancellationToken);
		_lastOrderRefresh = CurrentTime;
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		ResolvePortfolio(cancelMsg.PortfolioName);
		var orderId = cancelMsg.OrderId;
		if (orderId == null && long.TryParse(cancelMsg.OrderStringId, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var parsed))
			orderId = parsed;
		if (orderId is not > 0)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		_cancelTransactions[orderId.Value] = cancelMsg.TransactionId;
		_activeOrders.Add(orderId.Value);
		_ordersAwaitingHistory.Add(orderId.Value);
		try
		{
			await _client.CancelOrder(orderId.Value, cancellationToken);
			_lastOrderRefresh = default;
		}
		catch
		{
			_cancelTransactions.Remove(orderId.Value);
			_ordersAwaitingHistory.Remove(orderId.Value);
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
			if (_orderStatusSubscriptionId == statusMsg.OriginalTransactionId)
				_orderStatusSubscriptionId = 0;
			return;
		}

		ResolvePortfolio(statusMsg.PortfolioName);
		await SendOrderSnapshot(statusMsg.TransactionId, statusMsg, true, cancellationToken);
		if (statusMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
		else
		{
			_orderStatusSubscriptionId = statusMsg.TransactionId;
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
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
			{
				_portfolioSubscriptionId = 0;
				_portfolioFilter = null;
			}
			return;
		}

		ResolvePortfolio(lookupMsg.PortfolioName);
		await SendPortfolioSnapshot(lookupMsg.TransactionId, lookupMsg.PortfolioName, true,
			cancellationToken);
		if (lookupMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
		else
		{
			_portfolioSubscriptionId = lookupMsg.TransactionId;
			_portfolioFilter = lookupMsg.PortfolioName;
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		}
	}

	private async Task SendOrderSnapshot(long originalTransactionId, OrderStatusMessage filter,
		bool isLookup, CancellationToken cancellationToken)
	{
		var limit = Math.Max(0, filter?.Count ?? (isLookup ? 50 : long.MaxValue));
		var skip = Math.Max(0, filter?.Skip ?? 0);
		var sentOrders = new HashSet<long>();
		var skippedOrders = new HashSet<long>();
		foreach (var order in await _client.GetOrders(cancellationToken) ?? [])
		{
			if (!IsOrderMatch(order, filter) || !sentOrders.Add(order.Id))
				continue;
			if (skip > 0)
			{
				skip--;
				skippedOrders.Add(order.Id);
				continue;
			}
			if (limit <= 0)
				break;
			await ProcessOrder(order, originalTransactionId, isLookup, isLookup, null, cancellationToken);
			limit--;
		}

		var ticker = GetTickerFilter(filter);
		var page = await _client.GetHistoricalOrders(ticker, 50, cancellationToken);
		var visitedPages = new HashSet<string>(StringComparer.Ordinal);
		while (page != null)
		{
			var groups = (page.Items ?? [])
				.Where(item => item?.Order?.Id > 0)
				.GroupBy(item => item.Order.Id);
			foreach (var group in groups)
			{
				var order = group
					.Select(item => item.Order)
					.OrderByDescending(item => Math.Abs(item.FilledQuantity ?? 0))
					.First();
				if (!IsOrderMatch(order, filter))
					continue;
				var isNewOrder = sentOrders.Add(order.Id);
				var isIncluded = !isNewOrder || limit > 0;
				if (isNewOrder && skip > 0)
				{
					skip--;
					skippedOrders.Add(order.Id);
					continue;
				}
				var eventTime = group
					.Select(item => item.Fill?.FilledAt)
					.Where(time => time != null && time.Value != default)
					.OrderByDescending(time => time)
					.FirstOrDefault();
				if (isNewOrder && limit > 0)
				{
					await ProcessOrder(order, originalTransactionId, isLookup, isLookup,
						eventTime, cancellationToken);
					limit--;
				}
				else if (!isNewOrder && !skippedOrders.Contains(order.Id))
					await ProcessOrder(order, originalTransactionId, isLookup, false,
						eventTime, cancellationToken);
				if (isIncluded && !skippedOrders.Contains(order.Id))
				{
					foreach (var item in group)
						await ProcessFill(order, item.Fill, originalTransactionId, isLookup,
							cancellationToken);
				}
				if (isIncluded && order.Status.ToOrderState() is (OrderStates.Done or OrderStates.Failed))
					_ordersAwaitingHistory.Remove(order.Id);
			}

			if (!isLookup || page.NextPagePath.IsEmpty() ||
				!visitedPages.Add(page.NextPagePath) || limit <= 0)
				break;
			page = await _client.GetHistoricalOrders(page.NextPagePath, cancellationToken);
		}
		_lastOrderRefresh = CurrentTime;
	}

	private async ValueTask ProcessOrder(Trading212Order order, long originalTransactionId,
		bool isLookup, bool isForced, DateTime? eventTime, CancellationToken cancellationToken)
	{
		if (order == null || order.Id <= 0)
			return;
		var state = order.Status.ToOrderState();
		var quantity = Math.Abs(order.Quantity ?? 0);
		var filled = Math.Abs(order.FilledQuantity ?? 0);
		var signature = $"{order.Status}|{quantity.ToString(CultureInfo.InvariantCulture)}|" +
			$"{filled.ToString(CultureInfo.InvariantCulture)}|{order.LimitPrice}|{order.StopPrice}";
		if (!isForced && _orderSignatures.TryGetValue(order.Id, out var previous) && previous == signature)
			return;
		_orderSignatures[order.Id] = signature;

		var transactionId = _orderTransactions.TryGetValue(order.Id, out var knownTransactionId)
			? knownTransactionId : 0;
		if (state is OrderStates.Pending or OrderStates.Active)
		{
			if (transactionId != 0 || _cancelTransactions.ContainsKey(order.Id))
				_activeOrders.Add(order.Id);
			else
				_activeOrders.Remove(order.Id);
		}
		else
			_activeOrders.Remove(order.Id);
		var originId = isLookup ? originalTransactionId
			: transactionId != 0 ? transactionId : originalTransactionId;
		if (!isLookup && order.Status is not null &&
			(order.Status.EqualsIgnoreCase("CANCELLED") || order.Status.EqualsIgnoreCase("REJECTED")) &&
			_cancelTransactions.TryGetValue(order.Id, out var cancelTransactionId))
			originId = cancelTransactionId;

		var serverTime = eventTime is DateTime updateTime && updateTime != default
			? updateTime.NormalizeUtc()
			: order.CreatedAt == default ? CurrentTime : order.CreatedAt.NormalizeUtc();
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originId,
			TransactionId = transactionId,
			OrderId = order.Id,
			OrderStringId = order.Id.ToString(CultureInfo.InvariantCulture),
			PortfolioName = ResolvePortfolio(null),
			SecurityId = GetSecurityId(order.Ticker, order.Instrument),
			Side = order.Side.ToSide(),
			OrderType = order.Type.ToOrderType(),
			OrderPrice = order.LimitPrice ?? 0,
			OrderVolume = quantity,
			Balance = Math.Max(0, quantity - filled),
			OrderState = state,
			TimeInForce = TimeInForce.PutInQueue,
			ServerTime = serverTime,
			Condition = CreateCondition(order),
			Error = state == OrderStates.Failed
				? new InvalidOperationException($"Trading 212 order entered state {order.Status}.") : null,
		}, cancellationToken);

		if (state is OrderStates.Done or OrderStates.Failed)
			_cancelTransactions.Remove(order.Id);
	}

	private ValueTask ProcessFill(Trading212Order order, Trading212Fill fill,
		long originalTransactionId, bool isLookup, CancellationToken cancellationToken)
	{
		if (order == null || fill == null || fill.Id <= 0 ||
			!fill.Type.EqualsIgnoreCase("TRADE") || fill.Quantity == 0)
			return default;
		var wasReported = _reportedFills.Contains(fill.Id);
		_reportedFills.Add(fill.Id);
		if (wasReported && !isLookup)
			return default;

		var transactionId = _orderTransactions.TryGetValue(order.Id, out var knownTransactionId)
			? knownTransactionId : 0;
		var originId = isLookup ? originalTransactionId
			: transactionId != 0 ? transactionId : originalTransactionId;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originId,
			OrderId = order.Id,
			OrderStringId = order.Id.ToString(CultureInfo.InvariantCulture),
			TradeId = fill.Id,
			TradeStringId = fill.Id.ToString(CultureInfo.InvariantCulture),
			PortfolioName = ResolvePortfolio(null),
			SecurityId = GetSecurityId(order.Ticker, order.Instrument),
			Side = order.Side.ToSide(),
			TradePrice = fill.Price,
			TradeVolume = Math.Abs(fill.Quantity),
			Commission = fill.WalletImpact?.Taxes?.Sum(tax => tax?.Quantity ?? 0),
			PnL = fill.WalletImpact?.RealisedProfitLoss,
			ServerTime = fill.FilledAt == default ? CurrentTime : fill.FilledAt.NormalizeUtc(),
		}, cancellationToken);
	}

	private async Task SendPortfolioSnapshot(long originalTransactionId, string portfolioName,
		bool isLookup, CancellationToken cancellationToken)
	{
		var account = ResolvePortfolio(portfolioName);
		var summaryTask = _client.GetAccountSummary(cancellationToken);
		var positionsTask = _client.GetPositions(cancellationToken);
		await Task.WhenAll(summaryTask, positionsTask);
		_account = await summaryTask
			?? throw new InvalidOperationException("Trading 212 returned no account summary.");
		var positions = await positionsTask ?? [];
		var currency = _account.Currency.ToCurrency();

		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = account,
			BoardCode = Trading212Extensions.BoardCode,
			Currency = currency,
		}, cancellationToken);
		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = account,
			SecurityId = SecurityId.Money,
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, _account.TotalValue, true)
		.TryAdd(PositionChangeTypes.BlockedValue, _account.Cash?.ReservedForOrders, true)
		.TryAdd(PositionChangeTypes.BuyOrdersMargin, _account.Cash?.AvailableToTrade, true)
		.TryAdd(PositionChangeTypes.RealizedPnL, _account.Investments?.RealizedProfitLoss, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, _account.Investments?.UnrealizedProfitLoss, true)
		.TryAdd(PositionChangeTypes.Currency, currency), cancellationToken);

		var currentTickers = positions
			.Where(position => position?.Instrument?.Ticker.IsEmpty() == false)
			.Select(position => position.Instrument.Ticker)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var previousTickers = _positionTickers.CopyAndClear();
		foreach (var ticker in currentTickers)
			_positionTickers.Add(ticker);
		if (!isLookup)
		{
			foreach (var ticker in previousTickers.Where(ticker => !currentTickers.Contains(ticker)))
			{
				await SendOutMessageAsync(new PositionChangeMessage
				{
					OriginalTransactionId = originalTransactionId,
					PortfolioName = account,
					SecurityId = GetSecurityId(ticker),
					ServerTime = CurrentTime,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, 0m, true)
				.TryAdd(PositionChangeTypes.BlockedValue, 0m, true), cancellationToken);
			}
		}

		foreach (var position in positions)
		{
			if (position?.Instrument?.Ticker.IsEmpty() != false)
				continue;
			var blocked = Math.Max(0,
				Math.Abs(position.Quantity) - Math.Abs(position.QuantityAvailableForTrading));
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = account,
				SecurityId = GetSecurityId(position.Instrument.Ticker, position.Instrument),
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, position.Quantity, true)
			.TryAdd(PositionChangeTypes.BlockedValue, blocked, true)
			.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePricePaid, true)
			.TryAdd(PositionChangeTypes.CurrentPrice, position.CurrentPrice, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, position.WalletImpact?.UnrealizedProfitLoss, true)
			.TryAdd(PositionChangeTypes.Currency, position.Instrument.Currency.ToCurrency()),
				cancellationToken);
		}
		_lastPortfolioRefresh = CurrentTime;
		_lastConnectionCheck = CurrentTime;
	}

	private static bool IsOrderMatch(Trading212Order order, OrderStatusMessage filter)
	{
		if (order == null || order.Id <= 0)
			return false;
		if (filter == null)
			return true;
		if (filter.OrderId is long numericId && numericId != order.Id)
			return false;
		if (!filter.OrderStringId.IsEmpty() &&
			!filter.OrderStringId.EqualsIgnoreCase(order.Id.ToString(CultureInfo.InvariantCulture)))
			return false;
		var ticker = order.Ticker.IsEmpty(order.Instrument?.Ticker);
		if (filter.SecurityId != default &&
			!filter.SecurityId.SecurityCode.EqualsIgnoreCase(ticker))
			return false;
		if (filter.SecurityIds.Length > 0 && !filter.SecurityIds.Any(id =>
			id.SecurityCode.EqualsIgnoreCase(ticker)))
			return false;
		if (filter.Side is Sides side && side != order.Side.ToSide())
			return false;
		if (filter.Volume is decimal volume && volume != Math.Abs(order.Quantity ?? 0))
			return false;
		var state = order.Status.ToOrderState();
		if (filter.States.Length > 0 && !filter.States.Contains(state))
			return false;
		var createdAt = order.CreatedAt == default ? DateTime.UtcNow : order.CreatedAt.NormalizeUtc();
		if (filter.From is DateTime from && createdAt < from.NormalizeUtc())
			return false;
		return filter.To is not DateTime to || createdAt <= to.NormalizeUtc();
	}

	private static string GetTickerFilter(OrderStatusMessage filter)
	{
		if (filter?.SecurityId.SecurityCode.IsEmpty() == false)
			return filter.SecurityId.SecurityCode;
		return filter?.SecurityIds.Length == 1 ? filter.SecurityIds[0].SecurityCode : null;
	}

	private static Trading212OrderCondition CreateCondition(Trading212Order order)
		=> order?.StopPrice is > 0 || order?.IsExtendedHours == true
			? new()
			{
				StopPrice = order.StopPrice,
				IsExtendedHours = order.IsExtendedHours,
			}
			: null;

	private static void CompleteOrder(Trading212Order order, OrderRegisterMessage request,
		Trading212TradableInstrument instrument, Trading212OrderCondition condition, string validity)
	{
		order.Ticker = order.Ticker.IsEmpty(instrument.Ticker);
		order.Side = order.Side.IsEmpty(request.Side == Sides.Sell ? "SELL" : "BUY");
		if (order.Quantity is null or 0)
			order.Quantity = request.Side == Sides.Sell ? -request.Volume : request.Volume;
		order.CreatedAt = order.CreatedAt == default ? DateTime.UtcNow : order.CreatedAt.NormalizeUtc();
		order.Status = order.Status.IsEmpty("CONFIRMED");
		order.TimeInForce = order.TimeInForce.IsEmpty(validity);
		order.IsExtendedHours |= condition.IsExtendedHours;
		order.Type = order.Type.IsEmpty((request.OrderType ?? OrderTypes.Limit) switch
		{
			OrderTypes.Market => "MARKET",
			OrderTypes.Conditional when request.Price > 0 => "STOP_LIMIT",
			OrderTypes.Conditional => "STOP",
			_ => "LIMIT",
		});
		if ((order.LimitPrice is null or <= 0) && request.Price > 0)
			order.LimitPrice = request.Price;
		if (order.StopPrice is null or <= 0)
			order.StopPrice = condition.StopPrice;
	}
}
