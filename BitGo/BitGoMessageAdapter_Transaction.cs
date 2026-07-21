namespace StockSharp.BitGo;

public partial class BitGoMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsureConnected();
		ValidatePortfolio(regMsg.PortfolioName);
		if (regMsg.PostOnly == true)
			throw new NotSupportedException(
				"BitGo Prime REST orders do not expose post-only execution.");
		var product = GetProduct(regMsg.SecurityId);
		var condition = regMsg.Condition as BitGoOrderCondition ?? new();
		if (condition.FundingType == BitGoFundingTypes.Margin &&
			!product.IsMarginTradeSupported)
			throw new NotSupportedException(
				"BitGo margin trading is disabled for " + product.Name + ".");
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Volume));
		var type = ResolveOrderType(regMsg, condition);
		ValidateOrder(type, regMsg, condition);
		var timeInForce = ResolveTimeInForce(type, regMsg,
			out var duration);
		var parameters = CreateParameters(type, condition,
			out var twapDuration, out var twapInterval);
		if (twapDuration is not null)
			duration = twapDuration;
		var scheduledDate = condition.ScheduledDate?.EnsureUtc();
		if (scheduledDate is not null && type is not
			(BitGoOrderTypes.Twap or BitGoOrderTypes.SteadyPace))
			throw new NotSupportedException(
				"BitGo scheduling is supported only for TWAP and Steady Pace orders.");
		if (scheduledDate is DateTime scheduled && scheduled <= DateTime.UtcNow)
			throw new ArgumentOutOfRangeException(
				nameof(condition.ScheduledDate), condition.ScheduledDate,
				"BitGo scheduled date must be in the future.");
		var clientOrderId = "ss-" + regMsg.TransactionId.ToString(
			CultureInfo.InvariantCulture);
		var request = new BitGoOrderRequest
		{
			ClientOrderId = clientOrderId,
			Product = product.Name,
			Type = type,
			FundingType = condition.FundingType,
			Side = regMsg.Side.ToBitGo(),
			Quantity = volume.ToInvariant(),
			QuantityCurrency = product.BaseCurrency.IsEmpty()
				? product.BaseCurrencyId
				: product.BaseCurrency,
			LimitPrice = type == BitGoOrderTypes.Limit ||
				(type == BitGoOrderTypes.Stop && regMsg.Price > 0) ||
				(type is BitGoOrderTypes.Twap or BitGoOrderTypes.SteadyPace &&
					regMsg.Price > 0)
					? regMsg.Price.ToInvariant()
					: null,
			TriggerPrice = type == BitGoOrderTypes.Stop
				? condition.TriggerPrice?.ToInvariant()
				: null,
			Duration = duration,
			Interval = twapInterval,
			TimeInForce = timeInForce,
			ScheduledDate = scheduledDate?.ToBitGoTime(),
			Parameters = parameters,
		};
		var response = await RestClient.PlaceOrderAsync(AccountId, request,
			cancellationToken) ?? throw new InvalidDataException(
			"BitGo returned an empty place-order response.");
		var orderId = response.GetId();
		if (orderId.IsEmpty())
			throw new InvalidDataException(
				"BitGo accepted an order without an identifier.");
		TrackOrder(orderId, clientOrderId, regMsg.TransactionId,
			product.ToStockSharp(), regMsg.Side, type);
		await ProcessOrderAsync(response, regMsg.TransactionId, null,
			cancellationToken);
		SchedulePoll();
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
		=> throw new NotSupportedException(
			"BitGo Prime REST requires canceling the old order and placing a new one.");

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureConnected();
		ValidatePortfolio(cancelMsg.PortfolioName);
		var orderId = ResolveOrderId(cancelMsg.OrderStringId, cancelMsg.OrderId,
			cancelMsg.OriginalTransactionId);
		using (_sync.EnterScope())
			_pendingCancels[orderId] = cancelMsg.TransactionId;
		try
		{
			_ = await RestClient.CancelOrderAsync(AccountId, orderId,
				cancellationToken);
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
				"BitGo group cancellation cannot close margin positions.");
		var filter = new OrderSubscription
		{
			SecurityId = cancelMsg.SecurityId,
			Side = cancelMsg.Side,
			Limit = HistoryLimit,
		};
		foreach (var order in await LoadOrdersAsync(filter, cancellationToken))
		{
			if (order.Status is not (BitGoOrderStatuses.PendingOpen or
				BitGoOrderStatuses.Open or BitGoOrderStatuses.PendingCancel or
				BitGoOrderStatuses.Scheduled))
				continue;
			var orderMessage = CreateOrderMessage(order);
			if (!IsOrderMatch(orderMessage, filter))
				continue;
			var isStop = order.Type == BitGoOrderTypes.Stop;
			if (cancelMsg.IsStop is bool requestedStop && requestedStop != isStop)
				continue;
			var orderId = order.GetId();
			using (_sync.EnterScope())
				_pendingCancels[orderId] = cancelMsg.TransactionId;
			try
			{
				_ = await RestClient.CancelOrderAsync(AccountId, orderId,
					cancellationToken);
			}
			catch
			{
				using (_sync.EnterScope())
					_pendingCancels.Remove(orderId);
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
			BoardCode = BoardCodes.BitGo,
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

	private static BitGoOrderTypes ResolveOrderType(
		OrderRegisterMessage message, BitGoOrderCondition condition)
	{
		if (condition.NativeType is BitGoOrderTypes native)
			return native;
		if (message.OrderType == OrderTypes.Conditional ||
			condition.TriggerPrice is not null)
			return BitGoOrderTypes.Stop;
		return (message.OrderType ?? OrderTypes.Limit) switch
		{
			OrderTypes.Market => BitGoOrderTypes.Market,
			OrderTypes.Limit => BitGoOrderTypes.Limit,
			var type => throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(type, 0)),
		};
	}

	private static void ValidateOrder(BitGoOrderTypes type,
		OrderRegisterMessage message, BitGoOrderCondition condition)
	{
		if (type == BitGoOrderTypes.Limit && message.Price <= 0)
			throw new ArgumentOutOfRangeException(nameof(message.Price));
		if (type == BitGoOrderTypes.Stop)
		{
			if (condition.TriggerPrice is not > 0)
				throw new ArgumentOutOfRangeException(
					nameof(condition.TriggerPrice), condition.TriggerPrice,
					"BitGo stop orders require a positive trigger price.");
			if (message.Price < 0)
				throw new ArgumentOutOfRangeException(nameof(message.Price));
			if (message.Price > 0 && message.Side == Sides.Buy &&
				condition.TriggerPrice > message.Price)
				throw new ArgumentOutOfRangeException(nameof(message.Price),
					"A BitGo buy stop-limit trigger cannot exceed its limit price.");
			if (message.Price > 0 && message.Side == Sides.Sell &&
				condition.TriggerPrice < message.Price)
				throw new ArgumentOutOfRangeException(nameof(message.Price),
					"A BitGo sell stop-limit trigger cannot be below its limit price.");
		}
		if (type == BitGoOrderTypes.Twap)
		{
			if (condition.TwapDuration is not { } duration ||
				duration <= TimeSpan.Zero)
				throw new ArgumentOutOfRangeException(
					nameof(condition.TwapDuration), condition.TwapDuration,
					"BitGo TWAP orders require a positive duration.");
			if (condition.IsTimeSliced &&
				(condition.TwapInterval is not { } interval ||
					interval <= TimeSpan.Zero))
				throw new ArgumentOutOfRangeException(
					nameof(condition.TwapInterval), condition.TwapInterval,
					"Time-sliced BitGo TWAP orders require a positive interval.");
		}
		if (type == BitGoOrderTypes.SteadyPace &&
			condition.SteadyPaceInterval is not > 0)
			throw new ArgumentOutOfRangeException(
				nameof(condition.SteadyPaceInterval),
				condition.SteadyPaceInterval,
				"BitGo Steady Pace orders require a positive interval.");
		if (type == BitGoOrderTypes.SteadyPace &&
			condition.SubOrderSize is not > 0)
			throw new ArgumentOutOfRangeException(nameof(condition.SubOrderSize),
				condition.SubOrderSize,
				"BitGo Steady Pace orders require a positive sub-order size.");
		if (condition.Variance is decimal variance &&
			variance is < 0 or > 1)
			throw new ArgumentOutOfRangeException(nameof(condition.Variance),
				variance, "BitGo Steady Pace variance must be from zero to one.");
	}

	private static BitGoTimeInForces? ResolveTimeInForce(BitGoOrderTypes type,
		OrderRegisterMessage message, out int? duration)
	{
		duration = null;
		if (type is BitGoOrderTypes.Twap or BitGoOrderTypes.SteadyPace)
		{
			if (message.TimeInForce is not null || message.TillDate is not null)
				throw new NotSupportedException(
					"BitGo algorithmic orders do not accept time in force.");
			return null;
		}
		if (message.TillDate is DateTime till)
		{
			if (type != BitGoOrderTypes.Limit)
				throw new NotSupportedException(
					"BitGo GTD is supported only for limit orders.");
			till = till.EnsureUtc();
			if (till <= DateTime.UtcNow)
				throw new ArgumentOutOfRangeException(nameof(message.TillDate),
					message.TillDate, "BitGo expiry must be in the future.");
			duration = ToPositiveMinutes(till - DateTime.UtcNow,
				nameof(message.TillDate));
			return BitGoTimeInForces.GoodTillDate;
		}
		var result = message.TimeInForce switch
		{
			TimeInForce.PutInQueue => BitGoTimeInForces.GoodTillCanceled,
			TimeInForce.CancelBalance => BitGoTimeInForces.ImmediateOrCancel,
			TimeInForce.MatchOrCancel => BitGoTimeInForces.FillOrKill,
			null when type == BitGoOrderTypes.Market =>
				BitGoTimeInForces.ImmediateOrCancel,
			null => BitGoTimeInForces.GoodTillCanceled,
			_ => throw new NotSupportedException(
				"Unsupported BitGo time in force: " + message.TimeInForce + "."),
		};
		if (type == BitGoOrderTypes.Market && result is not
			(BitGoTimeInForces.ImmediateOrCancel or BitGoTimeInForces.FillOrKill))
			throw new NotSupportedException(
				"BitGo market orders support only IOC and FOK.");
		if (type == BitGoOrderTypes.Stop && result !=
			BitGoTimeInForces.GoodTillCanceled)
			throw new NotSupportedException(
				"BitGo stop orders support only GTC.");
		return result;
	}

	private static BitGoAlgorithmParameters CreateParameters(
		BitGoOrderTypes type, BitGoOrderCondition condition,
		out int? duration, out int? interval)
	{
		duration = null;
		interval = null;
		if (type == BitGoOrderTypes.Twap)
		{
			duration = ToPositiveMinutes(condition.TwapDuration.Value,
				nameof(condition.TwapDuration));
			if (condition.IsTimeSliced)
				interval = ToPositiveMinutes(condition.TwapInterval.Value,
					nameof(condition.TwapInterval));
			return new()
			{
				IsTimeSliced = condition.IsTimeSliced,
				BoundsControl = condition.IsTimeSliced
					? null
					: condition.BoundsControl,
			};
		}
		if (type == BitGoOrderTypes.SteadyPace)
			return new()
			{
				Interval = condition.SteadyPaceInterval,
				IntervalUnit = condition.IntervalUnit,
				SubOrderSize = condition.SubOrderSize?.ToInvariant(),
				Variance = condition.Variance?.ToInvariant(),
			};
		return null;
	}

	private static int ToPositiveMinutes(TimeSpan value, string name)
	{
		if (value <= TimeSpan.Zero || value.TotalMinutes > int.MaxValue)
			throw new ArgumentOutOfRangeException(name, value,
				"BitGo duration must be a positive whole-minute value.");
		return checked((int)Math.Ceiling(value.TotalMinutes));
	}

	private async ValueTask SendPortfolioSnapshotAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		var balances = await RestClient.GetBalancesAsync(AccountId,
			IsIncludeUnsettledInAvailable, cancellationToken);
		await SendBalancesAsync(balances, transactionId, cancellationToken);
	}

	private async ValueTask SendBalancesAsync(BitGoBalance[] balances,
		long transactionId, CancellationToken cancellationToken)
	{
		var time = DateTime.UtcNow;
		UpdateServerTime(time);
		foreach (var balance in balances.Where(static value =>
			value is not null && !value.Currency.IsEmpty()))
		{
			var held = balance.HeldBalance.ToBitGoDecimal() ?? 0m;
			held += balance.UnsettledHeldBalance.ToBitGoDecimal() ?? 0m;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = new()
				{
					SecurityCode = balance.Currency.ToUpperInvariant(),
					BoardCode = BoardCodes.BitGo,
					Native = balance.CurrencyId,
				},
				ServerTime = time,
				OriginalTransactionId = transactionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue,
				balance.Balance.ToBitGoDecimal(), true)
			.TryAdd(PositionChangeTypes.BlockedValue, held, true),
				cancellationToken);
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
			await ProcessOrderAsync(order, transactionId, null, cancellationToken);
		var ids = selectedOrders.Select(static order => order.GetId())
			.Where(static id => !id.IsEmpty())
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (var trade in await LoadTradesAsync(filter, cancellationToken))
			if (ids.Contains(trade.OrderId))
				await ProcessTradeAsync(trade, transactionId, null,
					cancellationToken);
	}

	private async ValueTask<BitGoOrder[]> LoadOrdersAsync(
		OrderSubscription filter, CancellationToken cancellationToken)
	{
		if (!filter.OrderId.IsEmpty())
		{
			try
			{
				var order = await RestClient.GetOrderAsync(AccountId,
					filter.OrderId, cancellationToken);
				return order is null ? [] : [order];
			}
			catch (BitGoApiException error) when (
				error.StatusCode == HttpStatusCode.NotFound)
			{
				return [];
			}
		}
		var result = new List<BitGoOrder>();
		const int pageSize = 100;
		for (var offset = 0; offset < 100000; offset += pageSize)
		{
			var page = await RestClient.GetOrdersAsync(AccountId, new()
			{
				From = filter.From,
				To = filter.To,
				Offset = offset,
				Limit = pageSize,
			}, cancellationToken);
			result.AddRange(page.Where(static order => order is not null));
			if (page.Length < pageSize || result.Count >=
				filter.Skip + filter.Limit)
				break;
		}
		return [.. result
			.GroupBy(static order => order.GetId(),
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.OrderByDescending(GetOrderTime).First())
			.OrderBy(GetOrderTime)];
	}

	private async ValueTask<BitGoTrade[]> LoadTradesAsync(
		OrderSubscription filter, CancellationToken cancellationToken)
	{
		var result = new List<BitGoTrade>();
		const int pageSize = 100;
		for (var offset = 0; offset < 100000; offset += pageSize)
		{
			var page = await RestClient.GetTradesAsync(AccountId, new()
			{
				OrderId = filter.OrderId,
				From = filter.From,
				To = filter.To,
				Offset = offset,
				Limit = pageSize,
			}, cancellationToken);
			result.AddRange(page.Where(static trade => trade is not null));
			if (page.Length < pageSize || result.Count >=
				filter.Skip + filter.Limit * 10)
				break;
		}
		return [.. result
			.GroupBy(static trade => trade.Id,
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.First())
			.OrderBy(static trade => GetTradeTime(trade))];
	}

	private async ValueTask PollPrivateAsync(
		CancellationToken cancellationToken)
	{
		long[] portfolios;
		KeyValuePair<long, OrderSubscription>[] orders;
		using (_sync.EnterScope())
		{
			portfolios = [.. _portfolioSubscriptions];
			orders = [.. _orderSubscriptions];
		}
		if (portfolios.Length > 0)
		{
			var balances = await RestClient.GetBalancesAsync(AccountId,
				IsIncludeUnsettledInAvailable, cancellationToken);
			foreach (var transactionId in portfolios)
				await SendBalancesAsync(balances, transactionId,
					cancellationToken);
		}
		foreach (var (transactionId, filter) in orders)
			await SendOrderSnapshotAsync(filter, transactionId,
				cancellationToken);
	}

	private async ValueTask OnOrderReceivedAsync(BitGoOrderUpdate update,
		CancellationToken cancellationToken)
	{
		await ProcessOrderAsync(update, 0, null, cancellationToken);
		if (update.ExecType == BitGoExecutionTypes.Trade)
			await ProcessSocketTradeAsync(update, cancellationToken);
	}

	private async ValueTask ProcessOrderAsync(BitGoOrder order,
		long directTarget, OrderSubscription filter,
		CancellationToken cancellationToken)
	{
		if (order?.GetId().IsEmpty() != false || order.Product.IsEmpty())
			return;
		var message = CreateOrderMessage(order);
		if (filter is not null && !IsOrderMatch(message, filter))
			return;
		var targets = directTarget != 0
			? [directTarget]
			: GetOrderTargets(message);
		foreach (var target in targets)
		{
			var fingerprint = target.ToString(CultureInfo.InvariantCulture) +
				"|" + order.GetId() + "|" + order.Status + "|" +
				order.FilledQuantity + "|" + order.CumulativeQuantity + "|" +
				order.LeavesQuantity + "|" + GetOrderTime(order).Ticks;
			using (_sync.EnterScope())
				if (!_seenOrderUpdates.Add(fingerprint))
					continue;
			var clone = (ExecutionMessage)message.Clone();
			clone.OriginalTransactionId = target;
			await SendOutMessageAsync(clone, cancellationToken);
		}
		if (order.Status is BitGoOrderStatuses.Canceled or
			BitGoOrderStatuses.Completed or BitGoOrderStatuses.Error)
			using (_sync.EnterScope())
				_pendingCancels.Remove(order.GetId());
	}

	private async ValueTask ProcessSocketTradeAsync(BitGoOrderUpdate update,
		CancellationToken cancellationToken)
	{
		var tradeId = update.TradeId;
		var price = update.FillPrice.ToBitGoDecimal();
		var volume = update.FillQuantity.ToBitGoDecimal();
		if (tradeId.IsEmpty() || price is not > 0 || volume is not > 0)
			throw new InvalidDataException(
				"BitGo order fill is missing its ID, price, or quantity.");
		var order = CreateOrderMessage(update);
		var tracked = GetTrackedOrder(update.GetId(), update.ClientOrderId);
		var targets = GetTradeTargets(order, tracked);
		var time = update.Time.ToBitGoTime() ??
			update.LastFillDate.ToBitGoTime() ?? DateTime.UtcNow;
		UpdateServerTime(time);
		foreach (var target in targets)
			await SendTradeAsync(update.GetId(), tradeId, order.SecurityId,
				update.Side.ToStockSharp(), price.Value, volume.Value, time,
				tracked?.TransactionId ?? ParseTransactionId(update.ClientOrderId),
				target, cancellationToken);
	}

	private async ValueTask ProcessTradeAsync(BitGoTrade trade,
		long directTarget, OrderSubscription filter,
		CancellationToken cancellationToken)
	{
		if (trade?.Id.IsEmpty() != false || trade.OrderId.IsEmpty() ||
			trade.Product.IsEmpty())
			return;
		var product = GetProduct(trade.Product);
		if (product is null)
			return;
		var tracked = GetTrackedOrder(trade.OrderId, null);
		var order = new ExecutionMessage
		{
			SecurityId = product.ToStockSharp(),
			Side = trade.Side.ToStockSharp(),
			OrderStringId = trade.OrderId,
		};
		if (filter is not null && !IsOrderMatch(order, filter))
			return;
		var price = trade.Price.ToBitGoDecimal();
		var volume = trade.Quantity.ToBitGoDecimal();
		if (price is not > 0 || volume is not > 0)
			return;
		var targets = directTarget != 0
			? [directTarget]
			: GetTradeTargets(order, tracked);
		var time = GetTradeTime(trade);
		UpdateServerTime(time);
		foreach (var target in targets)
			await SendTradeAsync(trade.OrderId, trade.Id,
				product.ToStockSharp(), trade.Side.ToStockSharp(), price.Value,
				volume.Value, time, tracked?.TransactionId ?? 0, target,
				cancellationToken);
	}

	private async ValueTask SendTradeAsync(string orderId, string tradeId,
		SecurityId securityId, Sides side, decimal price, decimal volume,
		DateTime time, long transactionId, long target,
		CancellationToken cancellationToken)
	{
		var key = target.ToString(CultureInfo.InvariantCulture) + "|" + tradeId;
		using (_sync.EnterScope())
			if (!_seenTrades.Add(key))
				return;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = securityId,
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = side,
			OrderStringId = orderId,
			TradeStringId = tradeId,
			TradePrice = price,
			TradeVolume = volume,
			TransactionId = transactionId,
			OriginalTransactionId = target,
		}, cancellationToken);
	}

	private ExecutionMessage CreateOrderMessage(BitGoOrder order)
	{
		var product = GetProduct(order.Product) ?? throw new
			InvalidDataException("Unknown BitGo order product '" +
				order.Product + "'.");
		var orderId = order.GetId();
		var tracked = GetTrackedOrder(orderId, order.ClientOrderId);
		var transactionId = tracked?.TransactionId ??
			ParseTransactionId(order.ClientOrderId);
		var securityId = product.ToStockSharp();
		var side = order.Side.ToStockSharp();
		TrackOrder(orderId, order.ClientOrderId, transactionId, securityId,
			side, order.Type);
		var time = GetOrderTime(order);
		UpdateServerTime(time);
		var volume = GetBaseOrderVolume(order, product);
		var filled = order.FilledQuantity.ToBitGoDecimal() ??
			order.CumulativeQuantity.ToBitGoDecimal();
		var balance = order.LeavesQuantity.ToBitGoDecimal();
		if (balance is null && volume is decimal total && filled is decimal done)
			balance = (total - done).Max(0m);
		var condition = CreateCondition(order);
		DateTime? expiry = null;
		if (order.TimeInForce == BitGoTimeInForces.GoodTillDate &&
			order.Duration is int minutes)
			expiry = (order.CreationDate.ToBitGoTime() ?? time)
				.AddMinutes(minutes);
		var state = order.Status.ToStockSharp();
		var reason = order.ReasonDescription;
		if (reason.IsEmpty() && order.Reason is not null)
			reason = order.Reason.ToString();
		return new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = securityId,
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = side,
			OrderType = order.Type.ToStockSharp(),
			OrderPrice = order.LimitPrice.ToBitGoDecimal() ?? 0m,
			OrderVolume = volume,
			Balance = balance,
			AveragePrice = order.AveragePrice.ToBitGoDecimal(),
			OrderState = state,
			OrderStringId = orderId,
			TransactionId = transactionId,
			TimeInForce = order.TimeInForce.ToStockSharp(),
			ExpiryDate = expiry,
			Condition = condition,
			Error = state == OrderStates.Failed
				? new InvalidOperationException(reason.IsEmpty()
					? "BitGo reported an order error."
					: reason)
				: null,
		};
	}

	private static decimal? GetBaseOrderVolume(BitGoOrder order,
		BitGoProduct product)
	{
		if (string.Equals(order.QuantityCurrency, product.BaseCurrency,
			StringComparison.OrdinalIgnoreCase) ||
			string.Equals(order.QuantityCurrency, product.BaseCurrencyId,
				StringComparison.OrdinalIgnoreCase))
			return order.Quantity.ToBitGoDecimal();
		var filled = order.FilledQuantity.ToBitGoDecimal() ??
			order.CumulativeQuantity.ToBitGoDecimal();
		var leaves = order.LeavesQuantity.ToBitGoDecimal();
		return filled is decimal done && leaves is decimal left
			? done + left
			: filled;
	}

	private static BitGoOrderCondition CreateCondition(BitGoOrder order)
	{
		if (order.Type is not (BitGoOrderTypes.Stop or BitGoOrderTypes.Twap or
			BitGoOrderTypes.SteadyPace) &&
			order.FundingType == BitGoFundingTypes.Funded &&
			order.ScheduledDate.IsEmpty())
			return null;
		return new()
		{
			NativeType = order.Type,
			FundingType = order.FundingType,
			TriggerPrice = order.TriggerPrice.ToBitGoDecimal(),
			TwapDuration = order.Type == BitGoOrderTypes.Twap &&
				order.Duration is int duration
					? order is BitGoOrderUpdate
						? TimeSpan.FromSeconds(duration)
						: TimeSpan.FromMinutes(duration)
					: null,
			IsTimeSliced = order.IsTimeSliced == true,
			TwapInterval = order.TwapInterval is int interval
				? TimeSpan.FromMinutes(interval)
				: null,
			BoundsControl = order.BoundsControl ??
				BitGoBoundsControls.Standard,
			SteadyPaceInterval = order.Interval,
			IntervalUnit = order.IntervalUnit ?? BitGoIntervalUnits.Minute,
			SubOrderSize = order.SubOrderSize.ToBitGoDecimal(),
			Variance = order.Variance.ToBitGoDecimal(),
			ScheduledDate = order.ScheduledDate.ToBitGoTime(),
		};
	}

	private long[] GetOrderTargets(ExecutionMessage message)
	{
		var targets = new HashSet<long>();
		var tracked = GetTrackedOrder(message.OrderStringId, null);
		if (tracked?.TransactionId is > 0)
			targets.Add(tracked.TransactionId);
		using (_sync.EnterScope())
		{
			if (_pendingCancels.TryGetValue(message.OrderStringId,
				out var cancelTransactionId))
				targets.Add(cancelTransactionId);
			foreach (var (transactionId, filter) in _orderSubscriptions)
				if (IsOrderMatch(message, filter))
					targets.Add(transactionId);
		}
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
			Skip = Math.Max(0L, message.Skip ?? 0).Min(100000L).To<int>(),
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

	private static DateTime GetOrderTime(BitGoOrder order)
		=> order.Time.ToBitGoTime() ?? order.LastFillDate.ToBitGoTime() ??
			order.CompletionDate.ToBitGoTime() ??
			order.CreationDate.ToBitGoTime() ?? DateTime.UtcNow;

	private static DateTime GetTradeTime(BitGoTrade trade)
		=> trade.Time.ToBitGoTime() ?? trade.CreationDate.ToBitGoTime() ??
			DateTime.UtcNow;

	private static long ParseTransactionId(string clientOrderId)
		=> clientOrderId?.StartsWith("ss-", StringComparison.Ordinal) == true &&
			long.TryParse(clientOrderId.AsSpan(3), NumberStyles.None,
				CultureInfo.InvariantCulture, out var transactionId)
				? transactionId
				: 0;

	private string ResolveOrderId(string orderStringId, long? orderId,
		long originalTransactionId)
	{
		if (!orderStringId.IsEmpty())
			return orderStringId.Trim();
		if (orderId is long numeric)
			return numeric.ToString(CultureInfo.InvariantCulture);
		var tracked = GetTrackedOrder(originalTransactionId);
		if (tracked?.OrderId.IsEmpty() == false)
			return tracked.OrderId;
		throw new InvalidOperationException(
			LocalizedStrings.OrderNoExchangeId.Put(originalTransactionId));
	}
}
