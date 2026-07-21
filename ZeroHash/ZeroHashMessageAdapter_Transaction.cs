namespace StockSharp.ZeroHash;

public partial class ZeroHashMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsureConnected();
		ValidatePortfolio(regMsg.PortfolioName);
		var instrument = GetInstrument(regMsg.SecurityId);
		var condition = regMsg.Condition as ZeroHashOrderCondition ?? new();
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Volume));
		var stockSharpType = regMsg.OrderType ?? OrderTypes.Limit;
		var triggerPrice = condition.TriggerPrice;
		ZeroHashOrderTypes nativeType;
		if (stockSharpType == OrderTypes.Conditional || triggerPrice is not null)
		{
			if (triggerPrice is not > 0)
				throw new ArgumentOutOfRangeException(
					nameof(condition.TriggerPrice), triggerPrice,
					"Zero Hash conditional orders require a positive trigger price.");
			nativeType = regMsg.Price > 0
				? ZeroHashOrderTypes.StopLimit
				: ZeroHashOrderTypes.Stop;
		}
		else
		{
			nativeType = stockSharpType switch
			{
				OrderTypes.Market => ZeroHashOrderTypes.MarketToLimit,
				OrderTypes.Limit => ZeroHashOrderTypes.Limit,
				_ => throw new NotSupportedException(
					LocalizedStrings.OrderUnsupportedType.Put(stockSharpType, 0)),
			};
		}
		if (nativeType is ZeroHashOrderTypes.Limit or
			ZeroHashOrderTypes.StopLimit && regMsg.Price <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Price));
		if (regMsg.PostOnly == true && nativeType != ZeroHashOrderTypes.Limit)
			throw new NotSupportedException(
				"Zero Hash post-only is supported only for plain limit orders.");

		var timeInForce = GetTimeInForce(regMsg, nativeType,
			out var goodTillTime);
		var priceScale = instrument.GetPriceScale();
		var quantityScale = instrument.GetQuantityScale();
		var clientOrderId = "ss-" + regMsg.TransactionId.ToString(
			CultureInfo.InvariantCulture);
		var response = await RestClient.InsertOrderAsync(new()
		{
			Account = Account.Trim(),
			User = User.Trim(),
			Side = regMsg.Side.ToZeroHash(),
			Type = nativeType,
			TimeInForce = timeInForce,
			Symbol = instrument.Symbol,
			OrderQuantity = ZeroHashExtensions.ScaleValue(volume,
				quantityScale, nameof(regMsg.Volume)),
			Price = nativeType is ZeroHashOrderTypes.Limit or
				ZeroHashOrderTypes.StopLimit
					? ZeroHashExtensions.ScaleValue(regMsg.Price, priceScale,
						nameof(regMsg.Price))
					: null,
			StopPrice = triggerPrice is > 0
				? ZeroHashExtensions.ScaleValue(triggerPrice.Value, priceScale,
					nameof(condition.TriggerPrice))
				: null,
			ClientOrderId = clientOrderId,
			GoodTillTime = goodTillTime?.ToZeroHashTime(),
			IsParticipateOnly = regMsg.PostOnly == true,
			IsAllOrNone = condition.IsAllOrNone,
			IsBestLimit = condition.IsBestLimit,
			IsStrictLimit = condition.IsStrictLimit,
			IsIgnorePriceValidityChecks =
				condition.IsIgnorePriceValidityChecks,
			SelfMatchPreventionInstruction =
				condition.SelfMatchPreventionInstruction,
			OrderCapacity = condition.OrderCapacity,
			TriggerMethod = triggerPrice is null ? null : condition.TriggerMethod,
			ManualOrderIndicator = ZeroHashManualOrderIndicators.Automated,
		}, cancellationToken) ?? throw new InvalidDataException(
			"Zero Hash returned an empty insert-order response.");
		if (response.OrderId.IsEmpty())
			throw new InvalidDataException(
				"Zero Hash accepted an order without an identifier.");
		TrackOrder(response.OrderId, clientOrderId, regMsg.TransactionId,
			instrument.ToStockSharp(), regMsg.Side, nativeType);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = instrument.ToStockSharp(),
			ServerTime = ServerTime,
			PortfolioName = _portfolioName,
			Side = regMsg.Side,
			OrderType = stockSharpType,
			OrderPrice = regMsg.Price,
			OrderVolume = volume,
			Balance = volume,
			OrderState = OrderStates.Pending,
			OrderStringId = response.OrderId,
			TransactionId = regMsg.TransactionId,
			OriginalTransactionId = regMsg.TransactionId,
			TimeInForce = regMsg.TimeInForce,
			ExpiryDate = goodTillTime,
			PostOnly = regMsg.PostOnly,
			Condition = condition.Clone(),
		}, cancellationToken);
		SchedulePoll();
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
		=> throw new NotSupportedException(
			"Zero Hash cancel/replace is available over FIX, but not through the documented CLOB REST API.");

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureConnected();
		ValidatePortfolio(cancelMsg.PortfolioName);
		var orderId = ResolveOrderId(cancelMsg.OrderStringId,
			cancelMsg.OrderId, cancelMsg.TransactionId);
		var tracked = GetTrackedOrder(orderId, null);
		var securityId = !cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? cancelMsg.SecurityId
			: tracked?.SecurityId ?? default;
		var instrument = GetInstrument(securityId);
		using (_sync.EnterScope())
			_pendingCancels[orderId] = cancelMsg.TransactionId;
		try
		{
			_ = await RestClient.CancelOrderAsync(new()
			{
				OrderId = orderId,
				Symbol = instrument.Symbol,
				User = User.Trim(),
			}, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_pendingCancels.Remove(orderId);
			throw;
		}
		SchedulePoll();
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureConnected();
		ValidatePortfolio(cancelMsg.PortfolioName);
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"Zero Hash bulk cancellation cannot close positions.");
		var response = await RestClient.GetOpenOrdersAsync(new()
		{
			Accounts = [AccountCode],
			User = User.Trim(),
			Symbols = cancelMsg.SecurityId.SecurityCode.IsEmpty()
				? null
				: [GetInstrument(cancelMsg.SecurityId).Symbol],
		}, cancellationToken) ?? throw new InvalidDataException(
			"Zero Hash returned an empty open-orders response.");
		foreach (var order in response.Orders ?? [])
		{
			if (order?.Id.IsEmpty() != false || order.Symbol.IsEmpty())
				continue;
			if (cancelMsg.Side is Sides side &&
				order.Side?.ToStockSharp() != side)
				continue;
			var isStop = order.Type is ZeroHashOrderTypes.Stop or
				ZeroHashOrderTypes.StopLimit;
			if (cancelMsg.IsStop is bool requestedStop &&
				requestedStop != isStop)
				continue;
			using (_sync.EnterScope())
				_pendingCancels[order.Id] = cancelMsg.TransactionId;
			try
			{
				_ = await RestClient.CancelOrderAsync(new()
				{
					OrderId = order.Id,
					Symbol = order.Symbol,
					User = User.Trim(),
				}, cancellationToken);
			}
			catch
			{
				using (_sync.EnterScope())
					_pendingCancels.Remove(order.Id);
				throw;
			}
		}
		SchedulePoll();
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		ValidatePortfolio(lookupMsg.PortfolioName);
		if (!lookupMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_portfolioSubscriptions.Remove(lookupMsg.OriginalTransactionId);
			return;
		}
		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = _portfolioName,
			BoardCode = BoardCodes.ZeroHash,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId,
			cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_portfolioSubscriptions.Add(lookupMsg.TransactionId);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		ValidatePortfolio(statusMsg.PortfolioName);
		if (!statusMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_orderSubscriptions.Remove(statusMsg.OriginalTransactionId);
			return;
		}
		if (statusMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}
		var filter = CreateOrderSubscription(statusMsg);
		await SendOrderSnapshotAsync(filter, statusMsg.TransactionId,
			cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_orderSubscriptions.Add(statusMsg.TransactionId, filter);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private static ZeroHashTimeInForces GetTimeInForce(
		OrderRegisterMessage message, ZeroHashOrderTypes nativeType,
		out DateTime? goodTillTime)
	{
		goodTillTime = null;
		if (message.TillDate is DateTime expiry)
		{
			expiry = expiry.EnsureUtc();
			if (expiry <= DateTime.UtcNow)
				throw new ArgumentOutOfRangeException(nameof(message.TillDate),
					message.TillDate, "Zero Hash expiry must be in the future.");
			goodTillTime = expiry;
			return ZeroHashTimeInForces.GoodTillTime;
		}
		return message.TimeInForce switch
		{
			TimeInForce.PutInQueue => ZeroHashTimeInForces.GoodTillCanceled,
			TimeInForce.CancelBalance => ZeroHashTimeInForces.ImmediateOrCancel,
			TimeInForce.MatchOrCancel => ZeroHashTimeInForces.FillOrKill,
			null when nativeType == ZeroHashOrderTypes.MarketToLimit =>
				ZeroHashTimeInForces.ImmediateOrCancel,
			null => ZeroHashTimeInForces.GoodTillCanceled,
			_ => throw new NotSupportedException(
				"Unsupported Zero Hash time in force: " + message.TimeInForce + "."),
		};
	}

	private async ValueTask SendPortfolioSnapshotAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		var response = await RestClient.GetBalancesAsync(new()
		{
			Name = Account.Trim(),
			User = User.Trim(),
		}, cancellationToken) ?? throw new InvalidDataException(
			"Zero Hash returned an empty account-balance response.");
		await SendBalancesAsync(response.Balances ?? [], transactionId,
			cancellationToken);
	}

	private async ValueTask SendBalancesAsync(ZeroHashBalance[] balances,
		long transactionId, CancellationToken cancellationToken)
	{
		foreach (var balance in balances.Where(static item =>
			item is not null && !item.Asset.IsEmpty()))
		{
			var time = balance.UpdateTime.TryParseZeroHashTime() ?? DateTime.UtcNow;
			UpdateServerTime(time);
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = new()
				{
					SecurityCode = balance.Asset.ToUpperInvariant(),
					BoardCode = BoardCodes.ZeroHash,
				},
				ServerTime = time,
				OriginalTransactionId = transactionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue,
				balance.Value.ToDecimalInvariant(), true)
			.TryAdd(PositionChangeTypes.BlockedValue,
				balance.OpenOrders.ToDecimalInvariant(), true), cancellationToken);
		}
	}

	private async ValueTask SendOrderSnapshotAsync(OrderSubscription filter,
		long transactionId, CancellationToken cancellationToken)
	{
		var selectedOrders = (await LoadOrdersAsync(filter, cancellationToken))
			.Where(order => IsOrderMatch(CreateOrderMessage(order), filter))
			.Skip(filter.Skip)
			.Take(filter.Limit)
			.ToArray();
		foreach (var order in selectedOrders)
			await ProcessOrderAsync(order, transactionId, null,
				cancellationToken);
		var selectedIds = selectedOrders
			.Select(static order => order.Id)
			.Where(static id => !id.IsEmpty())
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (var execution in await LoadExecutionsAsync(filter,
			cancellationToken))
			if (execution.Order?.Id is { } orderId &&
				selectedIds.Contains(orderId))
				await ProcessExecutionAsync(execution, transactionId, null,
					cancellationToken);
	}

	private async ValueTask<ZeroHashOrder[]> LoadOrdersAsync(
		OrderSubscription filter, CancellationToken cancellationToken)
	{
		var (from, to) = GetHistoryRange(filter);
		var orders = new List<ZeroHashOrder>();
		var token = string.Empty;
		var tokens = new HashSet<string>(StringComparer.Ordinal);
		for (var page = 0; page < 1000; page++)
		{
			var response = await RestClient.SearchOrdersAsync(new()
			{
				Accounts = [AccountCode],
				User = User.Trim(),
				OrderId = filter.OrderId,
				Symbol = filter.SecurityId.SecurityCode,
				Side = filter.Side?.ToZeroHash(),
				StartTime = from.ToZeroHashTime(),
				EndTime = to.ToZeroHashTime(),
				PageSize = 100,
				PageToken = token,
			}, cancellationToken) ?? throw new InvalidDataException(
				"Zero Hash returned an empty order-search response.");
			orders.AddRange((response.Orders ?? []).Where(static order =>
				order is not null));
			if (response.NextPageToken.IsEmpty())
				break;
			if (!tokens.Add(response.NextPageToken))
				throw new InvalidDataException(
					"Zero Hash repeated an order-search page token.");
			token = response.NextPageToken;
		}
		return [.. orders
			.GroupBy(static order => order.Id ?? order.ClientOrderId,
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.OrderByDescending(GetOrderTime).First())
			.OrderBy(GetOrderTime)];
	}

	private async ValueTask<ZeroHashExecution[]> LoadExecutionsAsync(
		OrderSubscription filter, CancellationToken cancellationToken)
	{
		var (from, to) = GetHistoryRange(filter);
		var executions = new List<ZeroHashExecution>();
		var token = string.Empty;
		var tokens = new HashSet<string>(StringComparer.Ordinal);
		for (var page = 0; page < 1000; page++)
		{
			var response = await RestClient.SearchExecutionsAsync(new()
			{
				Accounts = [AccountCode],
				User = User.Trim(),
				OrderId = filter.OrderId,
				Symbol = filter.SecurityId.SecurityCode,
				StartTime = from.ToZeroHashTime(),
				EndTime = to.ToZeroHashTime(),
				IsNewestFirst = false,
				Types = [ZeroHashExecutionTypes.PartialFill,
					ZeroHashExecutionTypes.Fill],
				PageSize = 100,
				PageToken = token,
			}, cancellationToken) ?? throw new InvalidDataException(
				"Zero Hash returned an empty execution-search response.");
			executions.AddRange((response.Executions ?? []).Where(static execution =>
				execution is not null));
			if (response.IsEndOfFile || response.NextPageToken.IsEmpty())
				break;
			if (!tokens.Add(response.NextPageToken))
				throw new InvalidDataException(
					"Zero Hash repeated an execution-search page token.");
			token = response.NextPageToken;
		}
		return [.. executions
			.GroupBy(static execution => execution.Id ?? execution.TradeId,
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.First())
			.OrderBy(static execution =>
				execution.TransactionTime.TryParseZeroHashTime() ??
				DateTime.UnixEpoch)];
	}

	private static (DateTime from, DateTime to) GetHistoryRange(
		OrderSubscription filter)
	{
		var to = (filter.To ?? DateTime.UtcNow).EnsureUtc();
		var from = (filter.From ?? to.AddDays(-14)).EnsureUtc();
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(filter),
				"Zero Hash history start cannot be after its end.");
		if (to - from > TimeSpan.FromDays(14))
			throw new ArgumentOutOfRangeException(nameof(filter),
				"Zero Hash order search retains at most 14 days.");
		return (from, to);
	}

	private async ValueTask PollPrivateAsync(
		CancellationToken cancellationToken)
	{
		long[] portfolioSubscriptions;
		KeyValuePair<long, OrderSubscription>[] orderSubscriptions;
		using (_sync.EnterScope())
		{
			portfolioSubscriptions = [.. _portfolioSubscriptions];
			orderSubscriptions = [.. _orderSubscriptions];
		}
		if (portfolioSubscriptions.Length > 0)
		{
			var response = await RestClient.GetBalancesAsync(new()
			{
				Name = Account.Trim(),
				User = User.Trim(),
			}, cancellationToken) ?? throw new InvalidDataException(
				"Zero Hash returned an empty account-balance response.");
			foreach (var transactionId in portfolioSubscriptions)
				await SendBalancesAsync(response.Balances ?? [], transactionId,
					cancellationToken);
		}
		foreach (var (transactionId, filter) in orderSubscriptions)
			await SendOrderSnapshotAsync(filter, transactionId,
				cancellationToken);
	}

	private async ValueTask ProcessOrderAsync(ZeroHashOrder order,
		long directTarget, OrderSubscription filter,
		CancellationToken cancellationToken)
	{
		if (order?.Id.IsEmpty() != false || order.Symbol.IsEmpty() ||
			order.Side is null)
			return;
		var message = CreateOrderMessage(order);
		if (filter is not null && !IsOrderMatch(message, filter))
			return;
		var targets = directTarget != 0
			? [directTarget]
			: GetOrderTargets(message);
		foreach (var target in targets)
		{
			var fingerprint = target.ToString(CultureInfo.InvariantCulture) + "|" +
				order.Id + "|" + order.State + "|" + order.CumulativeQuantity +
				"|" + order.LeavesQuantity + "|" + order.LastTransactionTime;
			using (_sync.EnterScope())
				if (!_seenOrderUpdates.Add(fingerprint))
					continue;
			var clone = (ExecutionMessage)message.Clone();
			clone.OriginalTransactionId = target;
			await SendOutMessageAsync(clone, cancellationToken);
		}
		if (order.State == ZeroHashOrderStates.Canceled)
			using (_sync.EnterScope())
				_pendingCancels.Remove(order.Id);
	}

	private async ValueTask ProcessExecutionAsync(ZeroHashExecution execution,
		long directTarget, OrderSubscription filter,
		CancellationToken cancellationToken)
	{
		if (execution?.Order is not { } order)
			return;
		await ProcessOrderAsync(order, directTarget, filter, cancellationToken);
		if (execution.Type is not (ZeroHashExecutionTypes.PartialFill or
			ZeroHashExecutionTypes.Fill))
			return;
		var orderMessage = CreateOrderMessage(order);
		if (filter is not null && !IsOrderMatch(orderMessage, filter))
			return;
		var instrument = GetInstrument(order.Symbol);
		if (instrument is null || order.Side is null)
			return;
		var priceScale = GetScale(order.PriceScale,
			instrument.GetPriceScale());
		var quantityScale = GetScale(order.FractionalQuantityScale,
			instrument.GetQuantityScale());
		var price = ZeroHashExtensions.UnscaleValue(execution.LastPrice,
			priceScale);
		var volume = ZeroHashExtensions.UnscaleValue(execution.LastQuantity,
			quantityScale);
		if (price is not > 0 || volume is not > 0)
			return;
		var tradeId = execution.TradeId ?? execution.Id;
		if (tradeId.IsEmpty())
			throw new InvalidDataException(
				"Zero Hash returned a fill without an execution identifier.");
		var tracked = GetTrackedOrder(order.Id, order.ClientOrderId);
		var targets = directTarget != 0
			? [directTarget]
			: GetTradeTargets(orderMessage, tracked);
		var side = order.Side.Value.ToStockSharp();
		var time = execution.TransactionTime.TryParseZeroHashTime() ??
			GetOrderTime(order);
		UpdateServerTime(time);
		foreach (var target in targets)
		{
			var key = target.ToString(CultureInfo.InvariantCulture) + "|" + tradeId;
			using (_sync.EnterScope())
				if (!_seenExecutions.Add(key))
					continue;
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				SecurityId = instrument.ToStockSharp(),
				ServerTime = time,
				PortfolioName = _portfolioName,
				Side = side,
				OriginSide = execution.IsAggressor == true
					? side
					: execution.IsAggressor == false ? side.Invert() : null,
				OrderStringId = order.Id,
				TradeStringId = tradeId,
				TradePrice = price,
				TradeVolume = volume,
				Commission = execution.CommissionNotional.ToDecimalInvariant(),
				TransactionId = tracked?.TransactionId ??
					ParseTransactionId(order.ClientOrderId),
				OriginalTransactionId = target,
			}, cancellationToken);
		}
	}

	private async ValueTask ProcessCancelRejectAsync(
		ZeroHashCancelReject rejection, CancellationToken cancellationToken)
	{
		if (rejection?.OrderId.IsEmpty() != false)
			return;
		long transactionId;
		using (_sync.EnterScope())
		{
			if (!_pendingCancels.Remove(rejection.OrderId, out transactionId))
				transactionId = 0;
		}
		var tracked = GetTrackedOrder(rejection.OrderId,
			rejection.ClientOrderId);
		var text = rejection.Text.IsEmpty()
			? rejection.RejectReason
			: rejection.RejectReason.IsEmpty() ? rejection.Text :
				rejection.RejectReason + ": " + rejection.Text;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = tracked?.SecurityId ?? default,
			ServerTime = rejection.TransactionTime.TryParseZeroHashTime() ??
				DateTime.UtcNow,
			PortfolioName = _portfolioName,
			Side = tracked?.Side ?? default,
			OrderStringId = rejection.OrderId,
			OrderState = OrderStates.Active,
			TransactionId = tracked?.TransactionId ?? 0,
			OriginalTransactionId = transactionId,
			Error = new InvalidOperationException(text.IsEmpty()
				? "Zero Hash rejected the cancel request."
				: text),
		}, cancellationToken);
	}

	private ExecutionMessage CreateOrderMessage(ZeroHashOrder order)
	{
		if (order.Side is null)
			throw new InvalidDataException(
				"Zero Hash returned an order without a side.");
		var instrument = GetInstrument(order.Symbol) ?? throw new
			InvalidDataException("Unknown Zero Hash order symbol '" +
				order.Symbol + "'.");
		var priceScale = GetScale(order.PriceScale,
			instrument.GetPriceScale());
		var quantityScale = GetScale(order.FractionalQuantityScale,
			instrument.GetQuantityScale());
		var time = GetOrderTime(order);
		UpdateServerTime(time);
		var tracked = GetTrackedOrder(order.Id, order.ClientOrderId);
		var transactionId = tracked?.TransactionId ??
			ParseTransactionId(order.ClientOrderId);
		TrackOrder(order.Id, order.ClientOrderId, transactionId,
			instrument.ToStockSharp(), order.Side?.ToStockSharp(), order.Type);
		var state = order.State?.ToStockSharp() ?? OrderStates.Pending;
		var triggerPrice = ZeroHashExtensions.UnscaleValue(order.StopPrice,
			priceScale);
		return new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = instrument.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = order.Side.Value.ToStockSharp(),
			OrderType = order.Type?.ToStockSharp() ?? OrderTypes.Limit,
			OrderPrice = ZeroHashExtensions.UnscaleValue(order.Price,
				priceScale) ?? 0m,
			OrderVolume = ZeroHashExtensions.UnscaleValue(order.OrderQuantity,
				quantityScale),
			Balance = ZeroHashExtensions.UnscaleValue(order.LeavesQuantity,
				quantityScale),
			AveragePrice = ZeroHashExtensions.UnscaleValue(order.AveragePrice,
				priceScale),
			OrderState = state,
			OrderStringId = order.Id,
			TransactionId = transactionId,
			TimeInForce = order.TimeInForce.ToStockSharp(),
			ExpiryDate = order.GoodTillTime.TryParseZeroHashTime(),
			PostOnly = order.IsParticipateOnly,
			Commission = order.CommissionNotional.ToDecimalInvariant(),
			Condition = triggerPrice is > 0
				? new ZeroHashOrderCondition { TriggerPrice = triggerPrice }
				: null,
			Error = state == OrderStates.Failed
				? new InvalidOperationException(
					"Zero Hash reported a rejected order.")
				: null,
		};
	}

	private long[] GetOrderTargets(ExecutionMessage message)
	{
		var targets = new HashSet<long>();
		var tracked = GetTrackedOrder(message.OrderStringId, null);
		if (tracked?.TransactionId is > 0)
			targets.Add(tracked.TransactionId);
		using (_sync.EnterScope())
			foreach (var (transactionId, filter) in _orderSubscriptions)
				if (IsOrderMatch(message, filter))
					targets.Add(transactionId);
		return [.. targets];
	}

	private long[] GetTradeTargets(ExecutionMessage order,
		TrackedOrder tracked)
	{
		var targets = new HashSet<long>();
		if (tracked?.TransactionId is > 0)
			targets.Add(tracked.TransactionId);
		using (_sync.EnterScope())
			foreach (var (transactionId, filter) in _orderSubscriptions)
				if (IsOrderMatch(order, filter))
					targets.Add(transactionId);
		return [.. targets];
	}

	private OrderSubscription CreateOrderSubscription(OrderStatusMessage message)
		=> new()
		{
			SecurityId = message.SecurityId,
			SecurityIds = message.SecurityIds,
			OrderId = !message.OrderStringId.IsEmpty()
				? message.OrderStringId
				: message.OrderId?.ToString(CultureInfo.InvariantCulture),
			Side = message.Side,
			Volume = message.Volume,
			States = message.States,
			From = message.From?.EnsureUtc(),
			To = message.To?.EnsureUtc(),
			Skip = Math.Max(0L, message.Skip ?? 0).Min(1000L).To<int>(),
			Limit = (message.Count ?? HistoryLimit).Min(HistoryLimit).Max(1)
				.To<int>(),
		};

	private static bool IsOrderMatch(ExecutionMessage message,
		OrderSubscription filter)
	{
		if (!IsSecurityMatch(message.SecurityId, filter.SecurityId))
			return false;
		if (filter.SecurityIds?.Length > 0 && !filter.SecurityIds.Any(
			securityId => IsSecurityMatch(message.SecurityId, securityId)))
			return false;
		if (!filter.OrderId.IsEmpty() && !filter.OrderId.Equals(
			message.OrderStringId, StringComparison.OrdinalIgnoreCase))
			return false;
		if (filter.Side is Sides side && message.Side != side)
			return false;
		if (filter.Volume is decimal volume && message.OrderVolume != volume)
			return false;
		if (filter.States?.Length > 0 &&
			(message.OrderState is not OrderStates state ||
				!filter.States.Contains(state)))
			return false;
		if (filter.From is DateTime from && message.ServerTime < from)
			return false;
		if (filter.To is DateTime to && message.ServerTime > to)
			return false;
		return true;
	}

	private static bool IsSecurityMatch(SecurityId securityId,
		SecurityId filter)
		=> (filter.SecurityCode.IsEmpty() || filter.SecurityCode.Equals(
			securityId.SecurityCode, StringComparison.OrdinalIgnoreCase)) &&
			(filter.BoardCode.IsEmpty() || filter.BoardCode.Equals(
				securityId.BoardCode, StringComparison.OrdinalIgnoreCase));

	private static DateTime GetOrderTime(ZeroHashOrder order)
		=> order.LastTransactionTime.TryParseZeroHashTime() ??
			order.InsertTime.TryParseZeroHashTime() ??
			order.CreateTime.TryParseZeroHashTime() ?? DateTime.UtcNow;

	private static decimal GetScale(string value, decimal fallback)
		=> decimal.TryParse(value, NumberStyles.Number,
			CultureInfo.InvariantCulture, out var scale) && scale > 0
				? scale
				: fallback;

	private static long ParseTransactionId(string clientOrderId)
		=> clientOrderId?.StartsWith("ss-", StringComparison.Ordinal) == true &&
			long.TryParse(clientOrderId.AsSpan(3), NumberStyles.None,
				CultureInfo.InvariantCulture, out var transactionId)
				? transactionId
				: 0;

	private static string ResolveOrderId(string orderStringId, long? orderId,
		long transactionId)
	{
		if (!orderStringId.IsEmpty())
			return orderStringId.Trim();
		if (orderId is long numeric)
			return numeric.ToString(CultureInfo.InvariantCulture);
		throw new InvalidOperationException(
			"Zero Hash order string ID is required for transaction " +
			transactionId.ToString(CultureInfo.InvariantCulture) + ".");
	}
}
