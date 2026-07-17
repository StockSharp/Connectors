namespace StockSharp.LemonMarkets;

public partial class LemonMarketsMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		var accountId = ResolvePortfolio(regMsg.PortfolioName);
		if (regMsg.Volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg), regMsg.Volume,
				"lemon.markets requires a positive cash amount for buys or share quantity for sells.");
		if ((regMsg.OrderType ?? OrderTypes.Market) != OrderTypes.Market || regMsg.Price != 0)
			throw new NotSupportedException(
				"The current lemon.markets Brokerage API exposes market orders only.");
		if ((regMsg.TimeInForce ?? TimeInForce.PutInQueue) != TimeInForce.PutInQueue ||
			regMsg.TillDate != null)
			throw new NotSupportedException(
				"The lemon.markets Brokerage API does not expose a time-in-force or expiry instruction.");

		var instrument = await ResolveInstrument(regMsg.SecurityId.SecurityCode, cancellationToken);
		if (instrument.Type.ToSecurityType() == null)
			throw new NotSupportedException(
				$"lemon.markets instrument type '{instrument.Type}' is not supported by this connector.");
		var now = CurrentTime;
		var activeHalt = (instrument.ActiveTradingHalts ?? []).FirstOrDefault(halt =>
			halt != null && halt.ValidFrom.NormalizeUtc() <= now &&
			(halt.ValidTo == null || halt.ValidTo.Value.NormalizeUtc() > now) &&
			(halt.Side.IsEmpty() || halt.Side.ToSide() == regMsg.Side));
		if (activeHalt != null)
			throw new InvalidOperationException(
				$"lemon.markets has an active {activeHalt.Side.IsEmpty("two-sided")} trading halt for {instrument.Isin}.");

		var condition = regMsg.Condition as LemonMarketsOrderCondition;
		var feeAmount = condition?.FeeAmount;
		var feePercent = condition?.FeePercent;
		if (feeAmount == null && feePercent == null)
			feeAmount = DefaultFeeAmount;
		if (feeAmount != null && feePercent != null)
			throw new ArgumentException(
				"lemon.markets fixed and percentage fees are mutually exclusive.", nameof(regMsg));
		if (feeAmount is < 0 || feePercent is < 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg),
				"lemon.markets order fees cannot be negative.");

		var securitiesAccount = ResolveSecuritiesAccount(condition?.SecuritiesAccountId, true);
		var request = new LemonCreateOrderRequest
		{
			Side = regMsg.Side == Sides.Sell ? "sell" : "buy",
			Type = "market",
			Instrument = instrument.Isin,
			Amount = regMsg.Side == Sides.Buy ? regMsg.Volume.ToNativeNumber() : null,
			Quantity = regMsg.Side == Sides.Sell ? regMsg.Volume.ToNativeNumber() : null,
			Fees = new LemonFeesRequest
			{
				BaseAmount = feeAmount?.ToNativeNumber(),
				Percent = feePercent?.ToNativeNumber(),
			},
			Currency = "EUR",
			SecuritiesAccount = securitiesAccount,
			Actor = CreateActor(),
		};
		var idempotencyKey = $"{_idempotencyPrefix}-{regMsg.TransactionId.ToString(CultureInfo.InvariantCulture)}";
		var order = await _client.CreateOrder(accountId, request, idempotencyKey,
			cancellationToken);
		if (order?.Id.IsEmpty() != false)
			throw new InvalidOperationException(
				"lemon.markets accepted the create request without returning an order ID.");

		CompleteOrder(order, regMsg, request, now);
		_orderTransactions[order.Id] = regMsg.TransactionId;
		_transactionOrders[regMsg.TransactionId] = order.Id;
		await ProcessOrder(order, regMsg.TransactionId, false, true, cancellationToken);

		var state = GetOrderStatus(order).ToOrderState();
		if (state == OrderStates.Failed)
			return;
		if (order.Sca?.IsRequired == true)
			throw new NotSupportedException(
				$"lemon.markets order {order.Id} requires a customer SCA response. Confirm it through the customer authentication flow.");

		var consent = condition?.IsAppropriatenessConsentAccepted ??
			IsAppropriatenessConsentAccepted;
		if (order.AppropriatenessCheck?.IsRequired == true && !consent)
			throw new InvalidOperationException(
				$"lemon.markets order {order.Id} requires an appropriateness acknowledgement. Enable consent only after obtaining it from the customer.");

		if (!GetOrderStatus(order).EqualsIgnoreCase("created"))
			return;
		var confirmed = await _client.ConfirmOrder(accountId, order.Id,
			new LemonConfirmOrderRequest
			{
				Actor = CreateActor(),
				IsAppropriatenessConsentAccepted = order.AppropriatenessCheck?.IsRequired == true
					? true : null,
			}, cancellationToken);
		if (confirmed == null)
			throw new InvalidOperationException(
				$"lemon.markets returned no confirmation result for order {order.Id}.");
		CompleteOrder(confirmed, regMsg, request, now);
		await ProcessOrder(confirmed, regMsg.TransactionId, false, true, cancellationToken);
		_lastEventRefresh = CurrentTime;
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		var accountId = ResolvePortfolio(cancelMsg.PortfolioName);
		var orderId = cancelMsg.OrderStringId;
		if (orderId.IsEmpty() &&
			_transactionOrders.TryGetValue(cancelMsg.OriginalTransactionId, out var mappedOrderId))
			orderId = mappedOrderId;
		if (orderId.IsEmpty())
			throw new InvalidOperationException(
				LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		_cancelTransactions[orderId] = cancelMsg.TransactionId;
		try
		{
			var order = await _client.CancelOrder(accountId, orderId,
				new LemonCancelOrderRequest { Actor = CreateActor() }, cancellationToken);
			if (order == null)
				throw new InvalidOperationException(
					$"lemon.markets returned no cancellation result for order {orderId}.");
			await ProcessOrder(order, cancelMsg.TransactionId, false, true, cancellationToken);
			_lastEventRefresh = default;
		}
		catch
		{
			_cancelTransactions.Remove(orderId);
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
		var snapshotStarted = CurrentTime;
		await SendOrderSnapshot(statusMsg.TransactionId, statusMsg, true, cancellationToken);
		if (statusMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
		else
		{
			_orderStatusSubscriptionId = statusMsg.TransactionId;
			_eventSince = snapshotStarted;
			_lastEventRefresh = snapshotStarted;
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
		var skip = Math.Max(0, filter?.Skip ?? 0);
		var limit = Math.Max(0, filter?.Count ?? int.MaxValue);
		var accountId = ResolvePortfolio(filter?.PortfolioName);
		var securitiesAccount = ResolveSecuritiesAccount(null, false);
		var ordersTask = _client.GetOrders(accountId, securitiesAccount, int.MaxValue,
			cancellationToken);
		var tradesTask = _client.GetTrades(accountId, securitiesAccount, int.MaxValue,
			cancellationToken);
		await Task.WhenAll(ordersTask, tradesTask);
		var orders = await ordersTask ?? [];
		var trades = await tradesTask ?? [];
		foreach (var trade in trades)
			RegisterTradeValue(trade);

		var selectedOrderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var order in orders.OrderByDescending(GetOrderTime))
		{
			if (!IsOrderMatch(order, filter))
				continue;
			if (skip > 0)
			{
				skip--;
				continue;
			}
			if (limit <= 0)
				break;
			selectedOrderIds.Add(order.Id);
			await ProcessOrder(order, originalTransactionId, isLookup, true, cancellationToken);
			limit--;
		}

		foreach (var trade in trades.Where(trade => trade != null &&
			selectedOrderIds.Contains(trade.Order)).OrderBy(trade => trade.ExecutedAt))
			await ProcessTrade(trade, originalTransactionId, isLookup, cancellationToken);
		_lastEventRefresh = CurrentTime;
	}

	private async ValueTask ProcessOrder(LemonOrder order, long originalTransactionId,
		bool isLookup, bool isForced, CancellationToken cancellationToken)
	{
		if (order?.Id.IsEmpty() != false)
			return;
		var status = GetOrderStatus(order);
		var state = status.ToOrderState();
		var volume = Math.Abs(order.Side.ToSide() == Sides.Buy
			? order.Amount ?? 0 : order.Quantity ?? 0);
		var executed = _executedValues.TryGetValue(order.Id, out var executedValue)
			? executedValue : 0;
		var balance = state is OrderStates.Done or OrderStates.Failed
			? 0 : Math.Max(0, volume - executed);
		var updateTime = GetOrderTime(order);
		var signature = $"{status}|{volume.ToString(CultureInfo.InvariantCulture)}|" +
			$"{executed.ToString(CultureInfo.InvariantCulture)}|{order.Fee}|{updateTime:O}";
		if (!isForced && _orderSignatures.TryGetValue(order.Id, out var previous) &&
			previous == signature)
			return;
		_orderSignatures[order.Id] = signature;

		var transactionId = _orderTransactions.TryGetValue(order.Id, out var knownTransactionId)
			? knownTransactionId : 0;
		var originId = isLookup ? originalTransactionId
			: transactionId != 0 ? transactionId : originalTransactionId;
		if (!isLookup && status.EqualsIgnoreCase("canceled") &&
			_cancelTransactions.TryGetValue(order.Id, out var cancelTransactionId))
			originId = cancelTransactionId;

		var rejection = order.History?.Where(change => change?.Reason.IsEmpty() == false)
			.OrderByDescending(change => change.Timestamp).FirstOrDefault()?.Reason;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originId,
			TransactionId = isLookup ? transactionId : 0,
			OrderStringId = order.Id,
			PortfolioName = ResolvePortfolio(null),
			SecurityId = order.Instrument.ToLemonSecurityId(),
			Side = order.Side.ToSide(),
			OrderType = OrderTypes.Market,
			OrderVolume = volume,
			Balance = balance,
			OrderState = state,
			TimeInForce = TimeInForce.PutInQueue,
			ServerTime = updateTime,
			Commission = order.Fee,
			CommissionCurrency = order.Currency,
			Condition = CreateCondition(order),
			Error = state == OrderStates.Failed
				? new InvalidOperationException(rejection.IsEmpty(
					$"lemon.markets order entered state {status}.")) : null,
		}, cancellationToken);

		if (state is OrderStates.Done or OrderStates.Failed)
			_cancelTransactions.Remove(order.Id);
	}

	private async ValueTask ProcessTrade(LemonTrade trade, long originalTransactionId,
		bool isLookup, CancellationToken cancellationToken)
	{
		if (trade?.Id.IsEmpty() != false || trade.Order.IsEmpty() || trade.Quantity == 0)
			return;
		RegisterTradeValue(trade);
		var wasReported = _reportedTrades.Contains(trade.Id);
		_reportedTrades.Add(trade.Id);
		if (wasReported && !isLookup)
			return;

		var transactionId = _orderTransactions.TryGetValue(trade.Order,
			out var knownTransactionId) ? knownTransactionId : 0;
		var originId = isLookup ? originalTransactionId
			: transactionId != 0 ? transactionId : originalTransactionId;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originId,
			OrderStringId = trade.Order,
			TradeStringId = trade.Id,
			PortfolioName = ResolvePortfolio(null),
			SecurityId = trade.Instrument.ToLemonSecurityId(),
			Side = trade.Side.ToSide(),
			TradePrice = trade.Price,
			TradeVolume = Math.Abs(trade.Quantity),
			Commission = trade.Fee,
			CommissionCurrency = trade.Currency,
			ServerTime = trade.ExecutedAt == default
				? CurrentTime : trade.ExecutedAt.NormalizeUtc(),
		}, cancellationToken);
	}

	private void RegisterTradeValue(LemonTrade trade)
	{
		if (trade?.Id.IsEmpty() != false || trade.Order.IsEmpty() ||
			_processedTrades.Contains(trade.Id))
			return;
		_processedTrades.Add(trade.Id);
		var value = Math.Abs(trade.Side.ToSide() == Sides.Buy ? trade.Amount : trade.Quantity);
		_executedValues[trade.Order] = (_executedValues.TryGetValue(trade.Order, out var current)
			? current : 0) + value;
	}

	private async Task RefreshEvents(CancellationToken cancellationToken)
	{
		var pollStarted = CurrentTime;
		var events = (await _client.GetEvents(ResolvePortfolio(null), _eventSince,
			cancellationToken) ?? [])
			.Where(item => item?.Id.IsEmpty() == false && !_processedEvents.Contains(item.Id))
			.OrderBy(item => item.CreatedAt)
			.ToArray();
		var tradeIds = events.Select(item => item.Context?.Trade)
			.Where(id => !id.IsEmpty()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		var orderIds = events.Select(item => item.Context?.Order)
			.Where(id => !id.IsEmpty()).ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (var tradeId in tradeIds)
		{
			var trade = await _client.GetTrade(_account.Id, tradeId, cancellationToken);
			await ProcessTrade(trade, _orderStatusSubscriptionId, false, cancellationToken);
			if (trade?.Order.IsEmpty() == false)
				orderIds.Add(trade.Order);
		}
		foreach (var orderId in orderIds)
		{
			var order = await _client.GetOrder(_account.Id, orderId, cancellationToken);
			await ProcessOrder(order, _orderStatusSubscriptionId, false, false,
				cancellationToken);
		}
		foreach (var item in events)
			_processedEvents.Add(item.Id);
		var newest = events.Length == 0 ? pollStarted
			: events.Max(item => item.CreatedAt.NormalizeUtc());
		_eventSince = (newest < pollStarted ? pollStarted : newest) - TimeSpan.FromSeconds(30);
		_lastEventRefresh = CurrentTime;
	}

	private async Task SendPortfolioSnapshot(long originalTransactionId, string portfolioName,
		bool isLookup, CancellationToken cancellationToken)
	{
		var accountId = ResolvePortfolio(portfolioName);
		var securitiesAccount = ResolveSecuritiesAccount(null, false);
		var financialsTask = _client.GetFinancials(accountId, cancellationToken);
		var positionsTask = _client.GetPositions(accountId, securitiesAccount, cancellationToken);
		await Task.WhenAll(financialsTask, positionsTask);
		var financials = await financialsTask
			?? throw new InvalidOperationException("lemon.markets returned no account financials.");
		var positions = await positionsTask ?? [];
		var currency = financials.Currency.ToCurrency();

		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = accountId,
			BoardCode = LemonMarketsExtensions.BoardCode,
			Currency = currency,
		}, cancellationToken);
		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = accountId,
			SecurityId = SecurityId.Money,
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, financials.Balance, true)
		.TryAdd(PositionChangeTypes.BlockedValue, financials.Blocked, true)
		.TryAdd(PositionChangeTypes.BuyOrdersMargin, financials.BuyingPower, true)
		.TryAdd(PositionChangeTypes.BeginValue, financials.FundsToWithdraw, true)
		.TryAdd(PositionChangeTypes.Currency, currency), cancellationToken);

		var currentIsins = positions.Where(position => position?.Instrument?.Isin.IsEmpty() == false)
			.Select(position => position.Instrument.Isin)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var previousIsins = _positionIsins.CopyAndClear();
		foreach (var isin in currentIsins)
			_positionIsins.Add(isin);
		if (!isLookup)
		{
			foreach (var isin in previousIsins.Where(isin => !currentIsins.Contains(isin)))
			{
				await SendOutMessageAsync(new PositionChangeMessage
				{
					OriginalTransactionId = originalTransactionId,
					PortfolioName = accountId,
					SecurityId = isin.ToLemonSecurityId(),
					ServerTime = CurrentTime,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, 0m, true), cancellationToken);
			}
		}

		foreach (var position in positions)
		{
			if (position?.Instrument?.Isin.IsEmpty() != false)
				continue;
			var currentPrice = position.LatestPrice?.Amount ?? position.LatestBid;
			var serverTime = position.LatestPrice?.UpdatedAt ??
				position.LatestPrice?.ValuationDate ?? CurrentTime;
			var pnl = currentPrice is decimal price
				? (price - position.BuyIn) * position.Quantity : (decimal?)null;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = accountId,
				SecurityId = position.Instrument.Isin.ToLemonSecurityId(),
				ServerTime = serverTime == default ? CurrentTime : serverTime.NormalizeUtc(),
			}
			.TryAdd(PositionChangeTypes.CurrentValue, position.Quantity, true)
			.TryAdd(PositionChangeTypes.AveragePrice, position.BuyIn, true)
			.TryAdd(PositionChangeTypes.CurrentPrice, currentPrice)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, pnl)
			.TryAdd(PositionChangeTypes.Currency, position.Currency.ToCurrency()),
				cancellationToken);
		}
		_lastPortfolioRefresh = CurrentTime;
		_lastConnectionCheck = CurrentTime;
	}

	private static bool IsOrderMatch(LemonOrder order, OrderStatusMessage filter)
	{
		if (order?.Id.IsEmpty() != false)
			return false;
		if (filter == null)
			return true;
		if (!filter.OrderStringId.IsEmpty() && !filter.OrderStringId.EqualsIgnoreCase(order.Id))
			return false;
		if (filter.SecurityId != default &&
			!filter.SecurityId.SecurityCode.EqualsIgnoreCase(order.Instrument))
			return false;
		if (filter.SecurityIds.Length > 0 && !filter.SecurityIds.Any(id =>
			id.SecurityCode.EqualsIgnoreCase(order.Instrument)))
			return false;
		if (filter.Side is Sides side && side != order.Side.ToSide())
			return false;
		var volume = Math.Abs(order.Side.ToSide() == Sides.Buy
			? order.Amount ?? 0 : order.Quantity ?? 0);
		if (filter.Volume is decimal filterVolume && filterVolume != volume)
			return false;
		var state = GetOrderStatus(order).ToOrderState();
		if (filter.States.Length > 0 && !filter.States.Contains(state))
			return false;
		var time = GetOrderTime(order);
		if (filter.From is DateTime from && time < from.NormalizeUtc())
			return false;
		return filter.To is not DateTime to || time <= to.NormalizeUtc();
	}

	private static LemonMarketsOrderCondition CreateCondition(LemonOrder order)
		=> order == null ? null : new()
		{
			FeeAmount = order.Fees?.BaseAmount,
			FeePercent = order.Fees?.Percent,
			SecuritiesAccountId = order.SecuritiesAccount,
		};

	private LemonActorRequest CreateActor()
		=> PersonId.IsEmpty() ? null : new() { Type = "person", Person = PersonId };

	private static string GetOrderStatus(LemonOrder order)
		=> order?.History?.Where(change => change?.Status.IsEmpty() == false)
			.OrderByDescending(change => change.Timestamp).FirstOrDefault()?.Status
			?? "created";

	private static DateTime GetOrderTime(LemonOrder order)
	{
		var time = order?.History?.Where(change => change != null && change.Timestamp != default)
			.OrderByDescending(change => change.Timestamp).FirstOrDefault()?.Timestamp ?? default;
		return time == default ? DateTime.UtcNow : time.NormalizeUtc();
	}

	private static void CompleteOrder(LemonOrder order, OrderRegisterMessage request,
		LemonCreateOrderRequest nativeRequest, DateTime createdAt)
	{
		order.Side = order.Side.IsEmpty(nativeRequest.Side);
		order.Type = order.Type.IsEmpty("market");
		order.Instrument = order.Instrument.IsEmpty(nativeRequest.Instrument);
		order.Currency = order.Currency.IsEmpty(nativeRequest.Currency);
		order.SecuritiesAccount = order.SecuritiesAccount.IsEmpty(nativeRequest.SecuritiesAccount);
		if (request.Side == Sides.Buy && order.Amount == null)
			order.Amount = request.Volume;
		if (request.Side == Sides.Sell && order.Quantity == null)
			order.Quantity = request.Volume;
		order.History ??=
		[
			new LemonOrderStatusChange
			{
				Status = "created",
				Timestamp = createdAt.NormalizeUtc(),
			},
		];
	}
}
