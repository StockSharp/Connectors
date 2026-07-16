namespace StockSharp.CapitalCom;

public partial class CapitalComMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsureAccount(regMsg.PortfolioName);
		if (regMsg.Volume <= 0)
			throw new InvalidOperationException("Capital.com order size must be positive.");

		var condition = regMsg.Condition as CapitalComOrderCondition ?? new();
		ValidateCondition(condition);
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		var kind = orderType == OrderTypes.Market
			? CapitalComDealKinds.Position
			: CapitalComDealKinds.WorkingOrder;
		CapitalComDealReference result;

		if (kind == CapitalComDealKinds.Position)
		{
			var request = new CapitalComCreatePositionRequest
			{
				Epic = regMsg.SecurityId.ToEpic(),
				Direction = regMsg.Side.ToNativeSide(),
				Size = regMsg.Volume,
			};
			ApplyProtection(request, condition);
			result = await _rest.CreatePosition(request, cancellationToken);
		}
		else
		{
			if (regMsg.Price <= 0)
				throw new InvalidOperationException(
					"Capital.com working orders require a positive trigger level.");

			var request = new CapitalComCreateWorkingOrderRequest
			{
				Epic = regMsg.SecurityId.ToEpic(),
				Direction = regMsg.Side.ToNativeSide(),
				Size = regMsg.Volume,
				Level = regMsg.Price,
				Type = orderType == OrderTypes.Conditional ? "STOP" : "LIMIT",
				GoodTillDate = FormatGoodTillDate(regMsg.TillDate),
			};
			ApplyProtection(request, condition);
			result = await _rest.CreateWorkingOrder(request, cancellationToken);
		}

		var dealReference = result?.DealReference.ThrowIfEmpty(
			nameof(CapitalComDealReference.DealReference));
		var tracker = new OrderTracker
		{
			TransactionId = regMsg.TransactionId,
			SecurityId = regMsg.SecurityId,
			Portfolio = _session.AccountId,
			Side = regMsg.Side,
			OrderType = orderType,
			Price = regMsg.Price,
			Volume = regMsg.Volume,
			Kind = kind,
			Condition = condition,
			DealReference = dealReference,
		};
		_orderReferences[dealReference] = tracker;

		await SendPendingOrder(tracker, regMsg.TimeInForce, regMsg.TillDate, cancellationToken);
		var confirmation = await WaitForConfirmation(dealReference, cancellationToken);
		if (confirmation != null)
			await ProcessConfirmation(confirmation, tracker, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		EnsureAccount(replaceMsg.PortfolioName);
		var dealId = ResolveDealId(replaceMsg.OldOrderStringId, replaceMsg.OldOrderId,
			replaceMsg.OriginalTransactionId);
		var kind = await ResolveDealKind(dealId, cancellationToken);
		var condition = replaceMsg.Condition as CapitalComOrderCondition ?? new();
		ValidateCondition(condition);
		CapitalComDealReference result;

		if (kind == CapitalComDealKinds.WorkingOrder)
		{
			if (replaceMsg.Price <= 0)
				throw new InvalidOperationException(
					"Capital.com working orders require a positive trigger level.");

			var existing = (await _rest.GetWorkingOrders(cancellationToken)).WorkingOrders?
				.FirstOrDefault(o => o.Data?.DealId.EqualsIgnoreCase(dealId) == true)?.Data
				?? throw new InvalidOperationException(
					$"Capital.com working order '{dealId}' was not found.");
			if (existing.Size != null && existing.Size != replaceMsg.Volume)
				throw new NotSupportedException(
					"Capital.com does not permit changing a working order's size in place.");
			if (!existing.Direction.IsEmpty() && existing.Direction.ToSide() != replaceMsg.Side)
				throw new NotSupportedException(
					"Capital.com does not permit changing a working order's direction in place.");
			var requestedType = replaceMsg.OrderType == OrderTypes.Conditional ? "STOP" : "LIMIT";
			if (!existing.OrderType.IsEmpty() && !existing.OrderType.EqualsIgnoreCase(requestedType))
				throw new NotSupportedException(
					"Capital.com does not permit changing a working order's type in place.");

			var request = new CapitalComEditWorkingOrderRequest
			{
				Level = replaceMsg.Price,
				GoodTillDate = FormatGoodTillDate(replaceMsg.TillDate),
			};
			ApplyProtection(request, condition);
			result = await _rest.EditWorkingOrder(dealId, request, cancellationToken);
		}
		else
		{
			var existing = (await _rest.GetPositions(cancellationToken)).Positions?
				.FirstOrDefault(p => p.Position?.DealId.EqualsIgnoreCase(dealId) == true)?.Position
				?? throw new InvalidOperationException($"Capital.com position '{dealId}' was not found.");
			if (existing.Size != null && existing.Size != replaceMsg.Volume)
				throw new NotSupportedException(
					"Capital.com does not permit changing a position's size through the update endpoint.");
			if (!existing.Direction.IsEmpty() && existing.Direction.ToSide() != replaceMsg.Side)
				throw new NotSupportedException(
					"Capital.com does not permit changing a position's direction in place.");

			var request = new CapitalComEditPositionRequest();
			ApplyProtection(request, condition);
			result = await _rest.EditPosition(dealId, request, cancellationToken);
		}

		var tracker = TrackFollowUp(result, replaceMsg.TransactionId, replaceMsg.SecurityId,
			replaceMsg.PortfolioName, replaceMsg.Side, replaceMsg.OrderType ?? OrderTypes.Limit,
			replaceMsg.Price, replaceMsg.Volume, kind, condition, dealId);
		var confirmation = await WaitForConfirmation(tracker.DealReference, cancellationToken);
		if (confirmation != null)
			await ProcessConfirmation(confirmation, tracker, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsureAccount(cancelMsg.PortfolioName);
		var dealId = ResolveDealId(cancelMsg.OrderStringId, cancelMsg.OrderId,
			cancelMsg.OriginalTransactionId);
		var kind = await ResolveDealKind(dealId, cancellationToken);
		CapitalComDealReference result;
		Sides side;
		decimal volume;
		SecurityId securityId;

		if (kind == CapitalComDealKinds.WorkingOrder)
		{
			var working = (await _rest.GetWorkingOrders(cancellationToken)).WorkingOrders?
				.FirstOrDefault(o => o.Data?.DealId.EqualsIgnoreCase(dealId) == true)
				?? throw new InvalidOperationException(
					$"Capital.com working order '{dealId}' was not found.");
			side = working.Data.Direction.ToSide();
			volume = working.Data.Size ?? 0;
			securityId = working.Data.Epic.ToSecurityId();
			result = await _rest.DeleteWorkingOrder(dealId, cancellationToken);
		}
		else
		{
			var position = (await _rest.GetPositions(cancellationToken)).Positions?
				.FirstOrDefault(p => p.Position?.DealId.EqualsIgnoreCase(dealId) == true)
				?? throw new InvalidOperationException($"Capital.com position '{dealId}' was not found.");
			side = position.Position.Direction.ToSide().Invert();
			volume = position.Position.Size ?? 0;
			securityId = position.Market.Epic.ToSecurityId();
			result = await _rest.ClosePosition(dealId, cancellationToken);
		}

		var tracker = TrackFollowUp(result, cancelMsg.TransactionId, securityId,
			cancelMsg.PortfolioName, side, OrderTypes.Market, 0, volume, kind, new(), dealId);
		var confirmation = await WaitForConfirmation(tracker.DealReference, cancellationToken);
		if (confirmation != null)
			await ProcessConfirmation(confirmation, tracker, cancellationToken);
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

		EnsureAccount(statusMsg.PortfolioName);
		await SendActivityHistory(statusMsg, cancellationToken);
		await SendCurrentOrders(statusMsg.TransactionId, cancellationToken);

		if (statusMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
		else
		{
			_orderStatusSubscriptionId = statusMsg.TransactionId;
			_lastOrderRefresh = DateTime.UtcNow;
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		}
	}

	private async ValueTask SendActivityHistory(OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		var limit = (int)Math.Clamp(statusMsg.Count ?? 10000, 1, 10000);
		var activities = new List<CapitalComActivity>();
		var from = statusMsg.From is null
			? (DateTimeOffset?)null
			: new DateTimeOffset(statusMsg.From.Value.ToUniversalTime());
		var to = statusMsg.To is null
			? (DateTimeOffset?)null
			: new DateTimeOffset(statusMsg.To.Value.ToUniversalTime());

		if (from != null && to != null && to > from + TimeSpan.FromDays(1))
		{
			for (var cursor = from.Value; cursor < to && activities.Count < limit;)
			{
				var end = cursor + TimeSpan.FromDays(1);
				if (end > to)
					end = to.Value;
				var response = await _rest.GetActivities(cursor, end, cancellationToken);
				activities.AddRange((response.Activities ?? []).Take(limit - activities.Count));
				cursor = end;
			}
		}
		else
		{
			var response = await _rest.GetActivities(from, to, cancellationToken);
			activities.AddRange((response.Activities ?? []).Take(limit));
		}

		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var activity in activities.OrderBy(a =>
			a.DateUtc.ParseCapitalComTime() ?? a.Date.ParseCapitalComTime() ?? DateTimeOffset.MinValue))
		{
			var key = $"{activity.DealId}|{activity.DateUtc}|{activity.Type}|{activity.Status}|{activity.Source}";
			if (seen.Add(key))
				await ProcessActivity(activity, statusMsg.TransactionId, cancellationToken);
		}
	}

	private async ValueTask ProcessActivity(CapitalComActivity activity, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (activity?.DealId.IsEmpty() != false || activity.Epic.IsEmpty())
			return;

		CapitalComDealKinds kind;
		if (activity.Type.EqualsIgnoreCase("WORKING_ORDER"))
			kind = CapitalComDealKinds.WorkingOrder;
		else if (activity.Type.EqualsIgnoreCase("POSITION"))
			kind = CapitalComDealKinds.Position;
		else
			return;

		_dealKinds[activity.DealId] = kind;
		var details = activity.Details;
		var failed = activity.Status.ContainsIgnoreCase("REJECT");
		var finalWorkingState = activity.Status.EqualsIgnoreCase("EXECUTED") ||
			activity.Status.EqualsIgnoreCase("EXPIRED") ||
			activity.Status.EqualsIgnoreCase("CANCELLED");
		var state = failed
			? OrderStates.Failed
			: kind == CapitalComDealKinds.WorkingOrder && !finalWorkingState
				? OrderStates.Active
				: OrderStates.Done;
		var side = details?.Direction.ToSide() ?? Sides.Buy;
		var eventTime = activity.DateUtc.ParseCapitalComTime() ??
			activity.Date.ParseCapitalComTime() ?? DateTimeOffset.UtcNow;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId,
			OrderId = ParseNumericId(activity.DealId),
			OrderStringId = activity.DealId,
			SecurityId = activity.Epic.ToSecurityId(),
			PortfolioName = _session.AccountId,
			Side = side,
			OrderType = kind == CapitalComDealKinds.Position ? OrderTypes.Market : OrderTypes.Limit,
			OrderPrice = details?.Level ?? 0,
			OrderVolume = details?.Size ?? 0,
			Balance = state == OrderStates.Active ? details?.Size ?? 0 : 0,
			AveragePrice = kind == CapitalComDealKinds.Position ? details?.Level : null,
			OrderState = state,
			ServerTime = eventTime.UtcDateTime,
			ExpiryDate = details?.GoodTillDate.ParseCapitalComTime()?.UtcDateTime,
			Condition = details == null ? null : ToCondition(details),
			Error = failed
				? new InvalidOperationException(
					$"Capital.com activity for deal '{activity.DealId}' was rejected.")
				: null,
		}, cancellationToken);

		if (!failed && kind == CapitalComDealKinds.Position && details?.Size is > 0 &&
			details.Level is > 0)
		{
			var eventId = $"activity:{activity.DealId}:{activity.DateUtc}:{details.Size}:{details.Level}";
			if (_tradeEvents.TryAdd(eventId))
			{
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OriginalTransactionId = originalTransactionId,
					OrderId = ParseNumericId(activity.DealId),
					OrderStringId = activity.DealId,
					TradeStringId = details.DealReference.IsEmpty(eventId),
					SecurityId = activity.Epic.ToSecurityId(),
					PortfolioName = _session.AccountId,
					Side = side,
					TradePrice = details.Level.Value,
					TradeVolume = details.Size.Value,
					ServerTime = eventTime.UtcDateTime,
				}, cancellationToken);
			}
		}
	}

	private async ValueTask SendCurrentOrders(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		foreach (var position in (await _rest.GetPositions(cancellationToken)).Positions ?? [])
			await ProcessPositionOrder(position, originalTransactionId, cancellationToken);
		foreach (var order in (await _rest.GetWorkingOrders(cancellationToken)).WorkingOrders ?? [])
			await ProcessWorkingOrder(order, originalTransactionId, cancellationToken);
	}

	private async ValueTask ProcessWorkingOrder(CapitalComWorkingOrder order,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		var data = order?.Data;
		if (data?.DealId.IsEmpty() != false || data.Epic.IsEmpty())
			return;

		_dealKinds[data.DealId] = CapitalComDealKinds.WorkingOrder;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId,
			OrderId = ParseNumericId(data.DealId),
			OrderStringId = data.DealId,
			SecurityId = data.Epic.ToSecurityId(),
			PortfolioName = _session.AccountId,
			Side = data.Direction.ToSide(),
			OrderType = data.OrderType.EqualsIgnoreCase("STOP")
				? OrderTypes.Conditional
				: OrderTypes.Limit,
			OrderPrice = data.Level ?? 0,
			OrderVolume = data.Size ?? 0,
			Balance = data.Size ?? 0,
			OrderState = OrderStates.Active,
			ServerTime = (data.CreatedDateUtc.ParseCapitalComTime() ??
				data.CreatedDate.ParseCapitalComTime() ?? DateTimeOffset.UtcNow).UtcDateTime,
			TimeInForce = TimeInForce.PutInQueue,
			ExpiryDate = (data.GoodTillDateUtc.ParseCapitalComTime() ??
				data.GoodTillDate.ParseCapitalComTime())?.UtcDateTime,
			Condition = ToCondition(data),
		}, cancellationToken);
	}

	private async ValueTask ProcessPositionOrder(CapitalComOpenPosition position,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		var data = position?.Position;
		if (data?.DealId.IsEmpty() != false || position.Market?.Epic.IsEmpty() != false)
			return;

		_dealKinds[data.DealId] = CapitalComDealKinds.Position;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId,
			OrderId = ParseNumericId(data.DealId),
			OrderStringId = data.DealId,
			SecurityId = position.Market.Epic.ToSecurityId(),
			PortfolioName = _session.AccountId,
			Side = data.Direction.ToSide(),
			OrderType = OrderTypes.Market,
			OrderPrice = data.Level ?? 0,
			OrderVolume = data.Size ?? 0,
			Balance = 0,
			AveragePrice = data.Level,
			OrderState = OrderStates.Done,
			ServerTime = (data.CreatedDateUtc.ParseCapitalComTime() ??
				data.CreatedDate.ParseCapitalComTime() ?? DateTimeOffset.UtcNow).UtcDateTime,
			Condition = ToCondition(data),
		}, cancellationToken);
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

		await SendPortfolioSnapshot(lookupMsg.TransactionId, lookupMsg.PortfolioName, cancellationToken);
		if (lookupMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
		else
		{
			_portfolioSubscriptionId = lookupMsg.TransactionId;
			_lastPortfolioRefresh = DateTime.UtcNow;
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		}
	}

	private async ValueTask SendPortfolioSnapshot(long originalTransactionId, string portfolioName,
		CancellationToken cancellationToken)
	{
		var accounts = (await _rest.GetAccounts(cancellationToken)).Accounts ?? [];
		if (!portfolioName.IsEmpty())
			accounts = [.. accounts.Where(a => a.AccountId.EqualsIgnoreCase(portfolioName))];

		foreach (var account in accounts)
		{
			await SendOutMessageAsync(new PortfolioMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = account.AccountId,
				BoardCode = "CAPITALCOM",
			}, cancellationToken);
			await ProcessBalance(account, originalTransactionId, cancellationToken);
		}

		if (portfolioName.IsEmpty() || portfolioName.EqualsIgnoreCase(_session.AccountId))
		{
			foreach (var position in (await _rest.GetPositions(cancellationToken)).Positions ?? [])
				await ProcessPosition(position, originalTransactionId, cancellationToken);
		}
	}

	private ValueTask ProcessBalance(CapitalComAccount account, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var balance = account?.Balance;
		if (balance == null)
			return default;

		var blocked = balance.Value != null && balance.Available != null
			? Math.Max(0, balance.Value.Value - balance.Available.Value)
			: (decimal?)null;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = account.AccountId,
			SecurityId = SecurityId.Money,
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.BeginValue, balance.Deposit, true)
		.TryAdd(PositionChangeTypes.CurrentValue, balance.Value, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, balance.ProfitLoss, true)
		.TryAdd(PositionChangeTypes.BlockedValue, blocked, true)
		.TryAdd(PositionChangeTypes.BuyOrdersMargin, balance.Available, true)
		.TryAdd(PositionChangeTypes.Currency,
			Enum.TryParse<CurrencyTypes>(account.Currency, true, out var currency) ? currency : null),
			cancellationToken);
	}

	private ValueTask ProcessPosition(CapitalComOpenPosition position, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (position?.Position == null || position.Market?.Epic.IsEmpty() != false)
			return default;

		var size = position.Position.Size ?? 0;
		if (position.Position.Direction.EqualsIgnoreCase("SELL"))
			size = -size;
		var current = position.Position.Direction.EqualsIgnoreCase("SELL")
			? position.Market.Offer
			: position.Market.Bid;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = _session.AccountId,
			SecurityId = position.Market.Epic.ToSecurityId(),
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, size, true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.Position.Level, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, current, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.Position.UnrealizedProfitLoss, true),
			cancellationToken);
	}

	private ValueTask SendPendingOrder(OrderTracker tracker, TimeInForce? timeInForce,
		DateTime? tillDate, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = tracker.TransactionId,
			TransactionId = tracker.TransactionId,
			OrderStringId = tracker.DealReference,
			SecurityId = tracker.SecurityId,
			PortfolioName = tracker.Portfolio,
			Side = tracker.Side,
			OrderType = tracker.OrderType,
			OrderPrice = tracker.Price,
			OrderVolume = tracker.Volume,
			Balance = tracker.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = DateTime.UtcNow,
			TimeInForce = timeInForce,
			ExpiryDate = tillDate,
			Condition = tracker.Condition,
		}, cancellationToken);

	private async Task<CapitalComConfirmation> WaitForConfirmation(string dealReference,
		CancellationToken cancellationToken)
	{
		for (var attempt = 0; attempt < 6; attempt++)
		{
			try
			{
				return await _rest.GetConfirmation(dealReference, cancellationToken);
			}
			catch (CapitalComApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
			{
				if (attempt == 5)
					return null;
				await Task.Delay(TimeSpan.FromMilliseconds(150 * (attempt + 1)), cancellationToken);
			}
		}
		return null;
	}

	private async ValueTask ProcessConfirmation(CapitalComConfirmation confirmation,
		OrderTracker tracker, CancellationToken cancellationToken)
	{
		var dealId = confirmation.DealId.IsEmpty(
			confirmation.AffectedDeals?.Select(d => d.DealId).FirstOrDefault(id => !id.IsEmpty()))
			.IsEmpty(confirmation.DealReference).IsEmpty(tracker.DealReference);
		tracker.DealId = dealId;
		_orderDeals[dealId] = tracker;
		_dealKinds[dealId] = tracker.Kind;

		var failed = confirmation.DealStatus.EqualsIgnoreCase("REJECTED");
		var isDeleted = confirmation.Status.EqualsIgnoreCase("DELETED") ||
			confirmation.Status.EqualsIgnoreCase("CANCELLED") ||
			confirmation.Status.EqualsIgnoreCase("CLOSED");
		var state = failed
			? OrderStates.Failed
			: tracker.Kind == CapitalComDealKinds.WorkingOrder && !isDeleted
				? OrderStates.Active
				: OrderStates.Done;
		var serverTime = confirmation.Date.ParseCapitalComTime()?.UtcDateTime ?? DateTime.UtcNow;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = tracker.TransactionId,
			TransactionId = tracker.TransactionId,
			OrderId = ParseNumericId(dealId),
			OrderStringId = dealId,
			SecurityId = tracker.SecurityId,
			PortfolioName = tracker.Portfolio,
			Side = tracker.Side,
			OrderType = tracker.OrderType,
			OrderPrice = tracker.Price > 0 ? tracker.Price : confirmation.Level ?? 0,
			OrderVolume = tracker.Volume > 0 ? tracker.Volume : confirmation.Size ?? 0,
			Balance = state == OrderStates.Active
				? tracker.Volume > 0 ? tracker.Volume : confirmation.Size ?? 0
				: 0,
			AveragePrice = confirmation.Level,
			OrderState = state,
			ServerTime = serverTime,
			Condition = tracker.Condition,
			Error = failed
				? new InvalidOperationException(
					$"Capital.com rejected deal {tracker.DealReference}: " +
					confirmation.Reason.IsEmpty("unknown reason"))
				: null,
		}, cancellationToken);

		if (!failed && tracker.Kind == CapitalComDealKinds.Position &&
			confirmation.Size is > 0 && confirmation.Level is > 0)
		{
			var eventId = $"confirm:{tracker.DealReference}:{confirmation.Status}";
			if (_tradeEvents.TryAdd(eventId))
			{
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OriginalTransactionId = tracker.TransactionId,
					OrderId = ParseNumericId(dealId),
					OrderStringId = dealId,
					TradeStringId = tracker.DealReference,
					SecurityId = tracker.SecurityId,
					PortfolioName = tracker.Portfolio,
					Side = tracker.Side,
					TradePrice = confirmation.Level.Value,
					TradeVolume = confirmation.Size.Value,
					ServerTime = serverTime,
				}, cancellationToken);
			}
		}
	}

	private OrderTracker TrackFollowUp(CapitalComDealReference result, long transactionId,
		SecurityId securityId, string portfolio, Sides side, OrderTypes orderType, decimal price,
		decimal volume, CapitalComDealKinds kind, CapitalComOrderCondition condition, string dealId)
	{
		var reference = result?.DealReference.ThrowIfEmpty(
			nameof(CapitalComDealReference.DealReference));
		var tracker = new OrderTracker
		{
			TransactionId = transactionId,
			SecurityId = securityId,
			Portfolio = portfolio.IsEmpty(_session.AccountId),
			Side = side,
			OrderType = orderType,
			Price = price,
			Volume = volume,
			Kind = kind,
			Condition = condition,
			DealReference = reference,
			DealId = dealId,
		};
		_orderReferences[reference] = tracker;
		_orderDeals[dealId] = tracker;
		return tracker;
	}

	private async Task<CapitalComDealKinds> ResolveDealKind(string dealId,
		CancellationToken cancellationToken)
	{
		if (_dealKinds.TryGetValue(dealId, out var kind))
			return kind;
		if ((await _rest.GetWorkingOrders(cancellationToken)).WorkingOrders?
			.Any(o => o.Data?.DealId.EqualsIgnoreCase(dealId) == true) == true)
			return _dealKinds[dealId] = CapitalComDealKinds.WorkingOrder;
		if ((await _rest.GetPositions(cancellationToken)).Positions?
			.Any(p => p.Position?.DealId.EqualsIgnoreCase(dealId) == true) == true)
			return _dealKinds[dealId] = CapitalComDealKinds.Position;
		throw new InvalidOperationException(
			$"Capital.com deal '{dealId}' was not found among open positions or working orders.");
	}

	private string ResolveDealId(string stringId, long? numericId, long transactionId)
	{
		var result = stringId.IsEmpty(numericId?.ToString(CultureInfo.InvariantCulture));
		if (result.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(transactionId));
		if (_orderReferences.TryGetValue(result, out var tracker) && !tracker.DealId.IsEmpty())
			return tracker.DealId;
		return result;
	}

	private void EnsureAccount(string portfolioName)
	{
		if (!portfolioName.IsEmpty() && !portfolioName.EqualsIgnoreCase(_session.AccountId))
			throw new InvalidOperationException(
				$"Capital.com session is connected to account '{_session.AccountId}', not '{portfolioName}'.");
	}

	private static void ValidateCondition(CapitalComOrderCondition condition)
	{
		if (condition.IsGuaranteedStop && condition.IsTrailingStop)
			throw new InvalidOperationException(
				"Capital.com guaranteed and trailing stops cannot be enabled together.");
		if (new[] { condition.StopLevel, condition.StopDistance, condition.StopAmount }.Count(v => v != null) > 1)
			throw new InvalidOperationException(
				"Specify only one of stop level, stop distance or stop amount for Capital.com.");
		if (new[] { condition.ProfitLevel, condition.ProfitDistance, condition.ProfitAmount }.Count(v => v != null) > 1)
			throw new InvalidOperationException(
				"Specify only one of profit level, profit distance or profit amount for Capital.com.");
		if (condition.IsTrailingStop && condition.StopDistance == null)
			throw new InvalidOperationException(
				"Capital.com trailing stops require a stop distance.");
	}

	private static void ApplyProtection(CapitalComProtectionRequest request,
		CapitalComOrderCondition condition)
	{
		request.IsGuaranteedStop = condition.IsGuaranteedStop;
		request.IsTrailingStop = condition.IsTrailingStop;
		request.StopLevel = condition.StopLevel;
		request.StopDistance = condition.StopDistance;
		request.StopAmount = condition.StopAmount;
		request.ProfitLevel = condition.ProfitLevel;
		request.ProfitDistance = condition.ProfitDistance;
		request.ProfitAmount = condition.ProfitAmount;
	}

	private static CapitalComOrderCondition ToCondition(CapitalComPosition position)
		=> new()
		{
			IsGuaranteedStop = position.IsGuaranteedStop,
			IsTrailingStop = position.IsTrailingStop,
			StopLevel = position.StopLevel,
			StopDistance = position.StopDistance,
			StopAmount = position.StopAmount,
			ProfitLevel = position.ProfitLevel,
			ProfitDistance = position.ProfitDistance,
			ProfitAmount = position.ProfitAmount,
		};

	private static CapitalComOrderCondition ToCondition(CapitalComWorkingOrderData order)
		=> new()
		{
			IsGuaranteedStop = order.IsGuaranteedStop,
			IsTrailingStop = order.IsTrailingStop,
			StopLevel = order.StopLevel,
			StopDistance = order.StopDistance,
			StopAmount = order.StopAmount,
			ProfitLevel = order.ProfitLevel,
			ProfitDistance = order.ProfitDistance,
			ProfitAmount = order.ProfitAmount,
		};

	private static CapitalComOrderCondition ToCondition(CapitalComActivityDetails details)
		=> new()
		{
			IsGuaranteedStop = details.IsGuaranteedStop,
			IsTrailingStop = details.IsTrailingStop,
			StopLevel = details.StopLevel,
			StopDistance = details.StopDistance,
			StopAmount = details.StopAmount,
			ProfitLevel = details.ProfitLevel,
			ProfitDistance = details.ProfitDistance,
			ProfitAmount = details.ProfitAmount,
		};

	private static string FormatGoodTillDate(DateTime? tillDate)
		=> tillDate?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

	private static long? ParseNumericId(string value)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
			? result
			: null;
}
