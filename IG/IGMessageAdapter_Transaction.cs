namespace StockSharp.IG;

using StockSharp.IG.Native;

public partial class IgMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsureAccount(regMsg.PortfolioName);
		if (regMsg.Volume <= 0)
			throw new InvalidOperationException("IG order size must be positive.");
		var condition = regMsg.Condition as IgOrderCondition ?? new IgOrderCondition();
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		var kind = orderType == OrderTypes.Market ? IgDealKinds.Position : IgDealKinds.WorkingOrder;
		IgDealReference result;
		if (kind == IgDealKinds.Position)
		{
			result = await _rest.CreatePosition(new()
			{
				Epic = regMsg.SecurityId.ToEpic(),
				Expiry = condition.Expiry.IsEmpty("DFB"),
				Direction = regMsg.Side.ToNativeSide(),
				Size = regMsg.Volume,
				OrderType = "MARKET",
				TimeInForce = regMsg.TimeInForce == TimeInForce.MatchOrCancel ? "EXECUTE_AND_ELIMINATE" : "FILL_OR_KILL",
				GuaranteedStop = condition.GuaranteedStop,
				StopLevel = condition.StopLevel,
				StopDistance = condition.StopDistance,
				TrailingStop = condition.TrailingStop,
				TrailingStopIncrement = condition.TrailingStopIncrement,
				ForceOpen = condition.ForceOpen,
				LimitLevel = condition.LimitLevel,
				LimitDistance = condition.LimitDistance,
				CurrencyCode = condition.CurrencyCode.IsEmpty(_session.Currency),
			}, cancellationToken);
		}
		else
		{
			if (regMsg.Price <= 0)
				throw new InvalidOperationException("IG working orders require a positive trigger level.");
			result = await _rest.CreateWorkingOrder(new()
			{
				Epic = regMsg.SecurityId.ToEpic(),
				Expiry = condition.Expiry.IsEmpty("DFB"),
				Direction = regMsg.Side.ToNativeSide(),
				Size = regMsg.Volume,
				Level = regMsg.Price,
				Type = orderType == OrderTypes.Conditional ? "STOP" : "LIMIT",
				CurrencyCode = condition.CurrencyCode.IsEmpty(_session.Currency),
				TimeInForce = regMsg.TillDate is null ? "GOOD_TILL_CANCELLED" : "GOOD_TILL_DATE",
				GoodTillDate = regMsg.TillDate?.ToUniversalTime().ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture),
				GuaranteedStop = condition.GuaranteedStop,
				ForceOpen = condition.ForceOpen,
				StopDistance = condition.StopDistance,
				LimitDistance = condition.LimitDistance,
			}, cancellationToken);
		}

		var dealReference = result?.DealReference.ThrowIfEmpty(nameof(IgDealReference.DealReference));
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
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			OrderStringId = dealReference,
			SecurityId = regMsg.SecurityId,
			PortfolioName = _session.AccountId,
			Side = regMsg.Side,
			OrderType = orderType,
			OrderPrice = regMsg.Price,
			OrderVolume = regMsg.Volume,
			Balance = regMsg.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = DateTime.UtcNow,
			TimeInForce = regMsg.TimeInForce,
			ExpiryDate = regMsg.TillDate,
			Condition = condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		EnsureAccount(replaceMsg.PortfolioName);
		var dealId = GetDealId(replaceMsg.OldOrderStringId, replaceMsg.OldOrderId, replaceMsg.OriginalTransactionId);
		var kind = await ResolveDealKind(dealId, cancellationToken);
		var condition = replaceMsg.Condition as IgOrderCondition ?? new IgOrderCondition();
		IgDealReference result;
		if (kind == IgDealKinds.WorkingOrder)
		{
			if (replaceMsg.Price <= 0)
				throw new InvalidOperationException("IG working orders require a positive trigger level.");
			var existing = (await _rest.GetWorkingOrders(cancellationToken)).WorkingOrders?
				.FirstOrDefault(o => o.Data?.DealId.EqualsIgnoreCase(dealId) == true)?.Data
				?? throw new InvalidOperationException($"IG working order '{dealId}' was not found.");
			if (existing.Size != null && existing.Size != replaceMsg.Volume)
				throw new NotSupportedException("IG does not permit changing a working order's size in place.");
			if (!existing.Direction.IsEmpty() && existing.Direction.ToSide() != replaceMsg.Side)
				throw new NotSupportedException("IG does not permit changing a working order's direction in place.");
			result = await _rest.EditWorkingOrder(dealId, new()
			{
				TimeInForce = replaceMsg.TillDate is null ? "GOOD_TILL_CANCELLED" : "GOOD_TILL_DATE",
				GoodTillDate = replaceMsg.TillDate?.ToUniversalTime().ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture),
				StopDistance = condition.StopDistance,
				LimitDistance = condition.LimitDistance,
				Type = replaceMsg.OrderType == OrderTypes.Conditional ? "STOP" : "LIMIT",
				Level = replaceMsg.Price,
			}, cancellationToken);
		}
		else
		{
			result = await _rest.EditPosition(dealId, new()
			{
				StopLevel = condition.StopLevel,
				LimitLevel = condition.LimitLevel,
				TrailingStop = condition.TrailingStop,
				TrailingStopDistance = condition.StopDistance,
				TrailingStopIncrement = condition.TrailingStopIncrement,
			}, cancellationToken);
		}
		TrackFollowUp(result, replaceMsg.TransactionId, replaceMsg.SecurityId, replaceMsg.PortfolioName,
			replaceMsg.Side, replaceMsg.OrderType ?? OrderTypes.Limit, replaceMsg.Price, replaceMsg.Volume,
			kind, condition, dealId);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureAccount(cancelMsg.PortfolioName);
		var dealId = GetDealId(cancelMsg.OrderStringId, cancelMsg.OrderId, cancelMsg.OriginalTransactionId);
		var kind = await ResolveDealKind(dealId, cancellationToken);
		IgDealReference result;
		Sides side;
		decimal volume;
		SecurityId securityId;
		if (kind == IgDealKinds.WorkingOrder)
		{
			var working = (await _rest.GetWorkingOrders(cancellationToken)).WorkingOrders?
				.FirstOrDefault(o => o.Data?.DealId.EqualsIgnoreCase(dealId) == true);
			side = working?.Data?.Direction.ToSide() ?? cancelMsg.Side ?? Sides.Buy;
			volume = working?.Data?.Size ?? 0;
			securityId = (working?.Data?.Epic).IsEmpty(cancelMsg.SecurityId.SecurityCode).ToSecurityId();
			result = await _rest.DeleteWorkingOrder(dealId, cancellationToken);
		}
		else
		{
			var position = (await _rest.GetPositions(cancellationToken)).Positions?
				.FirstOrDefault(p => p.Position?.DealId.EqualsIgnoreCase(dealId) == true)
				?? throw new InvalidOperationException($"IG position '{dealId}' was not found.");
			side = position.Position.Direction.ToSide().Invert();
			volume = position.Position.Size ?? 0;
			securityId = position.Market.Epic.ToSecurityId();
			result = await _rest.ClosePosition(new()
			{
				DealId = dealId,
				Direction = side.ToNativeSide(),
				Size = volume,
				OrderType = "MARKET",
				TimeInForce = "FILL_OR_KILL",
			}, cancellationToken);
		}
		TrackFollowUp(result, cancelMsg.TransactionId, securityId, cancelMsg.PortfolioName, side,
			OrderTypes.Market, 0, volume, kind, new(), dealId);
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
		EnsureAccount(statusMsg.PortfolioName);
		await SendActivityHistory(statusMsg, cancellationToken);
		foreach (var position in (await _rest.GetPositions(cancellationToken)).Positions ?? [])
			await ProcessPositionOrder(position, statusMsg.TransactionId, cancellationToken);
		foreach (var order in (await _rest.GetWorkingOrders(cancellationToken)).WorkingOrders ?? [])
			await ProcessWorkingOrder(order, statusMsg.TransactionId, cancellationToken);
		if (statusMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
		else
		{
			_orderStatusSubscriptionId = statusMsg.TransactionId;
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		}
	}

	private async ValueTask SendActivityHistory(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		var count = (int)Math.Clamp(statusMsg.Count ?? 10000, 1, 10000);
		var activities = new List<IgActivity>(Math.Min(count, 500));
		string next = null;
		for (var page = 0; page < 100 && activities.Count < count; page++)
		{
			var response = await _rest.GetActivities(statusMsg.From, statusMsg.To,
				Math.Min(500, count - activities.Count), next, cancellationToken);
			var batch = response.Activities ?? [];
			activities.AddRange(batch.Take(count - activities.Count));
			next = response.Metadata?.Paging?.Next;
			if (batch.Length == 0 || next.IsEmpty())
				break;
		}
		foreach (var activity in activities.OrderBy(a => a.Date.ParseIgTime() ?? DateTimeOffset.MinValue))
			await ProcessActivity(activity, statusMsg.TransactionId, cancellationToken);
	}

	private async ValueTask ProcessActivity(IgActivity activity, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var details = activity?.Details;
		if (details == null || details.Epic.IsEmpty())
			return;
		var actions = details.Actions ?? [];
		var actionNames = actions.Select(a => a.ActionType).Where(a => !a.IsEmpty()).ToArray();
		var dealId = activity.DealId.IsEmpty(actions.Select(a => a.AffectedDealId).FirstOrDefault(id => !id.IsEmpty()));
		if (dealId.IsEmpty())
			dealId = details.DealReference;
		if (dealId.IsEmpty())
			return;
		var working = details.Type.EqualsIgnoreCase("WORKING_ORDER") || actionNames.Any(a =>
			a.ContainsIgnoreCase("WORKING_ORDER") || a.ContainsIgnoreCase("LIMIT_ORDER") || a.ContainsIgnoreCase("STOP_ORDER"));
		var kind = working ? IgDealKinds.WorkingOrder : IgDealKinds.Position;
		_dealKinds[dealId] = kind;
		var rejected = details.Status.EqualsIgnoreCase("REJECTED");
		var done = !working || rejected || actionNames.Any(a =>
			a.ContainsIgnoreCase("DELETED") || a.ContainsIgnoreCase("FILLED") || a.ContainsIgnoreCase("CLOSED"));
		var orderType = !working ? OrderTypes.Market : actionNames.Any(a => a.ContainsIgnoreCase("STOP_ORDER"))
			? OrderTypes.Conditional : OrderTypes.Limit;
		var side = details.Direction.ToSide();
		var eventTime = activity.Date.ParseIgTime() ?? DateTimeOffset.UtcNow;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId,
			OrderId = ParseNumericId(dealId),
			OrderStringId = dealId,
			SecurityId = details.Epic.ToSecurityId(),
			PortfolioName = _session.AccountId,
			Side = side,
			OrderType = orderType,
			OrderPrice = details.Level ?? 0,
			OrderVolume = details.Size ?? 0,
			Balance = done ? 0 : details.Size ?? 0,
			AveragePrice = !working ? details.Level : null,
			OrderState = rejected ? OrderStates.Failed : done ? OrderStates.Done : OrderStates.Active,
			ServerTime = eventTime.UtcDateTime,
			ExpiryDate = details.GoodTillDate.ParseIgTime()?.UtcDateTime,
			Condition = new IgOrderCondition
			{
				Expiry = details.Period.IsEmpty("DFB"),
				CurrencyCode = details.Currency,
				GuaranteedStop = details.GuaranteedStop,
				StopLevel = details.StopLevel,
				StopDistance = details.StopDistance ?? details.TrailingStopDistance,
				LimitLevel = details.LimitLevel,
				LimitDistance = details.LimitDistance,
				TrailingStop = details.TrailingStopDistance != null,
				TrailingStopIncrement = details.TrailingStep,
			},
			Error = rejected ? new InvalidOperationException(activity.Description.IsEmpty("IG activity was rejected.")) : null,
		}, cancellationToken);

		var isFill = !rejected && details.Level is > 0 && details.Size is > 0 &&
			actionNames.Any(a => a.ContainsIgnoreCase("FILLED") || a.ContainsIgnoreCase("POSITION_OPENED") ||
				a.ContainsIgnoreCase("POSITION_CLOSED") || a.ContainsIgnoreCase("POSITION_PARTIALLY_CLOSED"));
		var eventId = $"activity:{dealId}:{activity.Date}:{string.Join('|', actionNames)}";
		if (isFill && _tradeEvents.TryAdd(eventId))
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = originalTransactionId,
				OrderId = ParseNumericId(dealId),
				OrderStringId = dealId,
				TradeStringId = details.DealReference.IsEmpty(eventId),
				SecurityId = details.Epic.ToSecurityId(),
				PortfolioName = _session.AccountId,
				Side = side,
				TradePrice = details.Level.Value,
				TradeVolume = details.Size.Value,
				ServerTime = eventTime.UtcDateTime,
			}, cancellationToken);
		}
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
		var accounts = (await _rest.GetAccounts(cancellationToken)).Accounts ?? _session.Accounts ?? [];
		if (!lookupMsg.PortfolioName.IsEmpty())
			accounts = [.. accounts.Where(a => a.Id.EqualsIgnoreCase(lookupMsg.PortfolioName))];
		foreach (var account in accounts)
		{
			await SendOutMessageAsync(new PortfolioMessage
			{
				OriginalTransactionId = lookupMsg.TransactionId,
				PortfolioName = account.Id,
				BoardCode = "IG",
			}, cancellationToken);
			await ProcessBalance(account.Id, account.Currency, account.Balance, lookupMsg.TransactionId, cancellationToken);
		}
		if (accounts.Any(a => a.Id.EqualsIgnoreCase(_session.AccountId)))
		{
			foreach (var position in (await _rest.GetPositions(cancellationToken)).Positions ?? [])
				await ProcessPosition(position, lookupMsg.TransactionId, cancellationToken);
		}
		if (lookupMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
		else
		{
			_portfolioSubscriptionId = lookupMsg.TransactionId;
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		}
	}

	private void TrackFollowUp(IgDealReference result, long transactionId, SecurityId securityId, string portfolio,
		Sides side, OrderTypes orderType, decimal price, decimal volume, IgDealKinds kind,
		IgOrderCondition condition, string dealId)
	{
		var reference = result?.DealReference.ThrowIfEmpty(nameof(IgDealReference.DealReference));
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
	}

	private async Task<IgDealKinds> ResolveDealKind(string dealId, CancellationToken cancellationToken)
	{
		if (_dealKinds.TryGetValue(dealId, out var kind))
			return kind;
		if ((await _rest.GetWorkingOrders(cancellationToken)).WorkingOrders?
			.Any(o => o.Data?.DealId.EqualsIgnoreCase(dealId) == true) == true)
			return _dealKinds[dealId] = IgDealKinds.WorkingOrder;
		if ((await _rest.GetPositions(cancellationToken)).Positions?
			.Any(p => p.Position?.DealId.EqualsIgnoreCase(dealId) == true) == true)
			return _dealKinds[dealId] = IgDealKinds.Position;
		throw new InvalidOperationException($"IG deal '{dealId}' was not found among open positions or working orders.");
	}

	private async ValueTask ProcessWorkingOrder(IgWorkingOrder order, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var data = order?.Data;
		if (data?.DealId.IsEmpty() != false)
			return;
		_dealKinds[data.DealId] = IgDealKinds.WorkingOrder;
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
			OrderType = data.OrderType.EqualsIgnoreCase("STOP") ? OrderTypes.Conditional : OrderTypes.Limit,
			OrderPrice = data.Level ?? 0,
			OrderVolume = data.Size ?? 0,
			Balance = data.Size ?? 0,
			OrderState = OrderStates.Active,
			ServerTime = (data.CreatedDateUtc.ParseIgTime() ?? data.CreatedDate.ParseIgTime() ?? DateTimeOffset.UtcNow).UtcDateTime,
			TimeInForce = TimeInForce.PutInQueue,
			ExpiryDate = (data.GoodTillDateUtc.ParseIgTime() ?? data.GoodTillDate.ParseIgTime())?.UtcDateTime,
			Condition = new IgOrderCondition
			{
				Expiry = order.Market?.Expiry.IsEmpty("DFB"),
				CurrencyCode = data.CurrencyCode,
				StopDistance = data.StopDistance,
				LimitDistance = data.LimitDistance,
			},
		}, cancellationToken);
	}

	private async ValueTask ProcessPositionOrder(IgOpenPosition position, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var data = position?.Position;
		if (data?.DealId.IsEmpty() != false)
			return;
		_dealKinds[data.DealId] = IgDealKinds.Position;
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
			ServerTime = (data.CreatedDateUtc.ParseIgTime() ?? data.CreatedDate.ParseIgTime() ?? DateTimeOffset.UtcNow).UtcDateTime,
			Condition = new IgOrderCondition
			{
				Expiry = position.Market.Expiry.IsEmpty("DFB"),
				CurrencyCode = data.Currency,
				StopLevel = data.StopLevel,
				LimitLevel = data.LimitLevel,
				StopDistance = data.TrailingStopDistance,
				TrailingStop = data.TrailingStopDistance != null,
				TrailingStopIncrement = data.TrailingStep,
			},
		}, cancellationToken);
	}

	private ValueTask ProcessBalance(string accountId, string currency, IgBalance balance, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (balance == null)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = accountId,
			SecurityId = SecurityId.Money,
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.BeginValue, balance.Value, true)
		.TryAdd(PositionChangeTypes.CurrentValue, balance.Value + balance.ProfitLoss, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, balance.ProfitLoss, true)
		.TryAdd(PositionChangeTypes.BlockedValue, balance.Deposit, true)
		.TryAdd(PositionChangeTypes.BuyOrdersMargin, balance.Available, true)
		.TryAdd(PositionChangeTypes.Currency,
			Enum.TryParse<CurrencyTypes>(currency, true, out var parsed) ? parsed : null), cancellationToken);
	}

	private ValueTask ProcessPosition(IgOpenPosition position, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (position?.Position == null || position.Market?.Epic.IsEmpty() != false)
			return default;
		var size = position.Position.Size ?? 0;
		if (position.Position.Direction.EqualsIgnoreCase("SELL"))
			size = -size;
		var current = position.Position.Direction.EqualsIgnoreCase("SELL") ? position.Market.Offer : position.Market.Bid;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = _session.AccountId,
			SecurityId = position.Market.Epic.ToSecurityId(),
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, size, true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.Position.Level, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, current, true), cancellationToken);
	}

	private void OnAccountReceived(IgAccountUpdate update)
		=> RunStreamHandler(ProcessAccountUpdate(update, CancellationToken.None));

	private ValueTask ProcessAccountUpdate(IgAccountUpdate update, CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId == 0)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = _portfolioSubscriptionId,
			PortfolioName = update.AccountId,
			SecurityId = SecurityId.Money,
			ServerTime = update.Time.UtcDateTime,
		}
		.TryAdd(PositionChangeTypes.UnrealizedPnL, update.ProfitLoss, true)
		.TryAdd(PositionChangeTypes.BlockedValue, update.Deposit ?? update.UsedMargin, true)
		.TryAdd(PositionChangeTypes.BuyOrdersMargin, update.AvailableCash, true)
		.TryAdd(PositionChangeTypes.CurrentValue, update.AmountDue, true), cancellationToken);
	}

	private void OnTradeReceived(IgStreamingTradeUpdate update)
		=> RunStreamHandler(ProcessTradeStream(update, CancellationToken.None));

	private async ValueTask ProcessTradeStream(IgStreamingTradeUpdate update, CancellationToken cancellationToken)
	{
		if (update.Confirmation != null)
			await ProcessConfirmation(update.Confirmation, cancellationToken);
		if (update.WorkingOrder != null)
			await ProcessTradeUpdate(update.WorkingOrder, IgDealKinds.WorkingOrder, cancellationToken);
		if (update.Position != null)
		{
			await ProcessTradeUpdate(update.Position, IgDealKinds.Position, cancellationToken);
			if (_portfolioSubscriptionId != 0 && !update.Position.Epic.IsEmpty())
			{
				var size = update.Position.Size ?? 0;
				if (update.Position.Direction.EqualsIgnoreCase("SELL"))
					size = -size;
				if (update.Position.Status.EqualsIgnoreCase("DELETED") || update.Position.Status.EqualsIgnoreCase("CLOSED"))
					size = 0;
				await SendOutMessageAsync(new PositionChangeMessage
				{
					OriginalTransactionId = _portfolioSubscriptionId,
					PortfolioName = update.AccountId,
					SecurityId = update.Position.Epic.ToSecurityId(),
					ServerTime = update.Position.Timestamp.ParseIgTime()?.UtcDateTime ?? DateTime.UtcNow,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, size, true)
				.TryAdd(PositionChangeTypes.AveragePrice, update.Position.Level, true), cancellationToken);
			}
		}
	}

	private async ValueTask ProcessConfirmation(IgConfirmation confirmation, CancellationToken cancellationToken)
	{
		if (confirmation.DealReference.IsEmpty())
			return;
		_orderReferences.TryGetValue(confirmation.DealReference, out var tracker);
		if (tracker == null && _orderStatusSubscriptionId == 0)
			return;
		var confirmedDealId = confirmation.DealId.IsEmpty(
			confirmation.AffectedDeals?.Select(d => d.DealId).FirstOrDefault(id => !id.IsEmpty()));
		if (tracker != null && !confirmedDealId.IsEmpty())
		{
			tracker.DealId = confirmedDealId;
			_orderDeals[confirmedDealId] = tracker;
			_dealKinds[confirmedDealId] = tracker.Kind;
		}
		var rejected = confirmation.DealStatus.EqualsIgnoreCase("REJECTED");
		var origin = tracker?.TransactionId ?? _orderStatusSubscriptionId;
		var dealId = confirmedDealId.IsEmpty(confirmation.DealReference);
		var state = rejected ? OrderStates.Failed : tracker?.Kind == IgDealKinds.WorkingOrder &&
			!confirmation.Status.EqualsIgnoreCase("DELETED") ? OrderStates.Active : OrderStates.Done;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = origin,
			TransactionId = tracker?.TransactionId ?? 0,
			OrderId = ParseNumericId(dealId),
			OrderStringId = dealId,
			SecurityId = tracker?.SecurityId ?? confirmation.Epic.ToSecurityId(),
			PortfolioName = tracker?.Portfolio ?? _session.AccountId,
			Side = tracker?.Side ?? confirmation.Direction.ToSide(),
			OrderType = tracker?.OrderType ?? OrderTypes.Market,
			OrderPrice = tracker?.Price ?? confirmation.Level ?? 0,
			OrderVolume = tracker?.Volume ?? confirmation.Size ?? 0,
			Balance = state == OrderStates.Active ? tracker?.Volume ?? confirmation.Size ?? 0 : 0,
			AveragePrice = confirmation.Level,
			OrderState = state,
			ServerTime = DateTime.UtcNow,
			Condition = tracker?.Condition,
			Error = rejected ? new InvalidOperationException(
				$"IG rejected deal {confirmation.DealReference}: {confirmation.Reason.IsEmpty("unknown reason")}") : null,
		}, cancellationToken);

		if (!rejected && tracker?.Kind == IgDealKinds.Position && confirmation.Size is > 0 && confirmation.Level is > 0)
		{
			var eventId = $"confirm:{confirmation.DealReference}:{confirmation.Status}";
			if (_tradeEvents.TryAdd(eventId))
			{
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OriginalTransactionId = origin,
					OrderId = ParseNumericId(dealId),
					OrderStringId = dealId,
					TradeStringId = confirmation.DealReference,
					SecurityId = tracker.SecurityId,
					PortfolioName = tracker.Portfolio,
					Side = tracker.Side,
					TradePrice = confirmation.Level.Value,
					TradeVolume = confirmation.Size.Value,
					ServerTime = DateTime.UtcNow,
				}, cancellationToken);
			}
		}
	}

	private async ValueTask ProcessTradeUpdate(IgTradeUpdate update, IgDealKinds kind,
		CancellationToken cancellationToken)
	{
		if (update.DealId.IsEmpty())
			return;
		_dealKinds[update.DealId] = kind;
		_orderDeals.TryGetValue(update.DealId, out var tracker);
		if (tracker == null && !update.DealReference.IsEmpty())
			_orderReferences.TryGetValue(update.DealReference, out tracker);
		var origin = tracker?.TransactionId ?? _orderStatusSubscriptionId;
		if (origin == 0)
			return;
		var done = update.Status.EqualsIgnoreCase("DELETED") || update.Status.EqualsIgnoreCase("CLOSED");
		var state = update.DealStatus.EqualsIgnoreCase("REJECTED") ? OrderStates.Failed :
			kind == IgDealKinds.Position || done ? OrderStates.Done : OrderStates.Active;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = origin,
			TransactionId = tracker?.TransactionId ?? 0,
			OrderId = ParseNumericId(update.DealId),
			OrderStringId = update.DealId,
			SecurityId = tracker?.SecurityId ?? update.Epic.ToSecurityId(),
			PortfolioName = tracker?.Portfolio ?? _session.AccountId,
			Side = tracker?.Side ?? update.Direction.ToSide(),
			OrderType = tracker?.OrderType ?? (update.OrderType.EqualsIgnoreCase("STOP") ? OrderTypes.Conditional :
				kind == IgDealKinds.Position ? OrderTypes.Market : OrderTypes.Limit),
			OrderPrice = tracker?.Price ?? update.Level ?? 0,
			OrderVolume = tracker?.Volume ?? update.Size ?? 0,
			Balance = state == OrderStates.Active ? tracker?.Volume ?? update.Size ?? 0 : 0,
			AveragePrice = kind == IgDealKinds.Position ? update.Level : null,
			OrderState = state,
			ServerTime = update.Timestamp.ParseIgTime()?.UtcDateTime ?? DateTime.UtcNow,
			ExpiryDate = update.GoodTillDate.ParseIgTime()?.UtcDateTime,
			Condition = tracker?.Condition ?? new IgOrderCondition
			{
				Expiry = update.Expiry.IsEmpty("DFB"),
				CurrencyCode = update.Currency,
				StopLevel = update.StopLevel,
				LimitLevel = update.LimitLevel,
				StopDistance = update.TrailingStopDistance,
				TrailingStop = update.TrailingStopDistance != null,
				TrailingStopIncrement = update.TrailingStep,
			},
			Error = state == OrderStates.Failed ? new InvalidOperationException($"IG rejected deal {update.DealId}.") : null,
		}, cancellationToken);

		if (kind == IgDealKinds.Position && update.Level is > 0 && update.Size is > 0 &&
			(update.Status.EqualsIgnoreCase("OPEN") || done))
		{
			var eventId = $"opu:{update.DealId}:{update.Status}:{update.Timestamp}:{update.Size}:{update.Level}";
			if (_tradeEvents.TryAdd(eventId))
			{
				var tradeSide = tracker?.Side ?? update.Direction.ToSide();
				if (tracker == null && done)
					tradeSide = tradeSide.Invert();
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OriginalTransactionId = origin,
					OrderId = ParseNumericId(update.DealId),
					OrderStringId = update.DealId,
					TradeStringId = update.DealReference.IsEmpty(eventId),
					SecurityId = tracker?.SecurityId ?? update.Epic.ToSecurityId(),
					PortfolioName = tracker?.Portfolio ?? _session.AccountId,
					Side = tradeSide,
					TradePrice = update.Level.Value,
					TradeVolume = update.Size.Value,
					ServerTime = update.Timestamp.ParseIgTime()?.UtcDateTime ?? DateTime.UtcNow,
				}, cancellationToken);
			}
		}
	}

	private void EnsureAccount(string portfolioName)
	{
		if (!portfolioName.IsEmpty() && !portfolioName.EqualsIgnoreCase(_session.AccountId))
			throw new InvalidOperationException($"IG streaming is connected to account '{_session.AccountId}', not '{portfolioName}'.");
	}

	private static string GetDealId(string stringId, long? numericId, long transactionId)
	{
		var result = stringId.IsEmpty(numericId?.ToString(CultureInfo.InvariantCulture));
		if (result.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(transactionId));
		return result;
	}

	private static long? ParseNumericId(string value)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
}
