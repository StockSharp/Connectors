namespace StockSharp.BitpandaFusion;

public partial class BitpandaFusionMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsureConnected();
		if (regMsg.Volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Volume), regMsg.Volume,
				"Bitpanda Fusion order volume must be positive.");
		if (regMsg.PostOnly == true)
			throw new NotSupportedException(
				"Bitpanda Fusion does not document a post-only order flag.");
		if (regMsg.VisibleVolume is > 0 && regMsg.VisibleVolume != regMsg.Volume)
			throw new NotSupportedException(
				"Bitpanda Fusion does not document iceberg orders.");

		var pair = GetPair(regMsg.SecurityId);
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		var condition = regMsg.Condition as BitpandaFusionOrderCondition ?? new();
		if (condition.TriggerPrice is <= 0)
			throw new InvalidOperationException(
				"Bitpanda Fusion trigger price must be positive.");
		var nativeType = condition.TriggerPrice is not null
			? orderType == OrderTypes.Market ||
				(orderType == OrderTypes.Conditional && regMsg.Price <= 0)
				? BitpandaFusionOrderTypes.StopMarket
				: BitpandaFusionOrderTypes.StopLimit
			: orderType switch
			{
				OrderTypes.Market => BitpandaFusionOrderTypes.Market,
				OrderTypes.Limit => BitpandaFusionOrderTypes.Limit,
				_ => throw new InvalidOperationException(
					"Bitpanda Fusion conditional orders require a trigger price."),
			};
		if (nativeType is BitpandaFusionOrderTypes.Limit or
			BitpandaFusionOrderTypes.StopLimit && regMsg.Price <= 0)
		{
			throw new InvalidOperationException(
				"Bitpanda Fusion limit orders require a positive price.");
		}
		var tillDate = regMsg.TillDate?.ToBitpandaFusionUtc();
		if (tillDate is not null && tillDate <= DateTime.UtcNow)
			throw new InvalidOperationException(
				"Bitpanda Fusion order expiry must be in the future.");

		var tracked = new TrackedOrder
		{
			TransactionId = regMsg.TransactionId,
			Pair = pair,
			Side = regMsg.Side,
			OrderType = nativeType.ToStockSharp(),
			Volume = regMsg.Volume,
			Price = nativeType is BitpandaFusionOrderTypes.Limit or
				BitpandaFusionOrderTypes.StopLimit ? regMsg.Price : 0m,
			TriggerPrice = condition.TriggerPrice,
			TimeInForce = regMsg.TimeInForce,
			TillDate = tillDate,
		};
		var order = await RestClient.CreateOrderAsync(new()
		{
			Pair = pair,
			Side = regMsg.Side.ToBitpandaFusion(),
			Type = nativeType,
			Quantity = regMsg.Volume,
			LimitPrice = nativeType is BitpandaFusionOrderTypes.Limit or
				BitpandaFusionOrderTypes.StopLimit ? regMsg.Price : null,
			TriggerPrice = condition.TriggerPrice,
			TimeInForce = regMsg.TimeInForce.ToBitpandaFusion(tillDate, nativeType),
			EndTime = tillDate is null ? null : new DateTimeOffset(tillDate.Value),
		}, cancellationToken);
		if (order?.Id.IsEmpty() != false)
			throw new InvalidDataException(
				"Bitpanda Fusion accepted an order without returning its identifier.");

		TrackOrder(order.Id, tracked);
		await SendOrderAsync(order, regMsg.TransactionId, tracked,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		_ = replaceMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"Bitpanda Fusion does not expose native order replacement.");
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsureConnected();
		var id = ResolveOrderId(cancelMsg.OrderId, cancelMsg.OrderStringId);
		var order = await RestClient.CancelOrderAsync(id, cancellationToken);
		var tracked = GetTrackedOrder(id);
		if (order is not null)
		{
			await SendOrderAsync(order, cancelMsg.TransactionId, tracked,
				cancellationToken);
			return;
		}

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = tracked?.Pair.IsEmpty() == false
				? tracked.Pair.ToStockSharp()
				: cancelMsg.SecurityId,
			ServerTime = CurrentTime,
			PortfolioName = GetPortfolioName(),
			Side = tracked?.Side ?? cancelMsg.Side ?? default,
			OrderVolume = tracked?.Volume,
			Balance = 0m,
			OrderPrice = tracked?.Price ?? 0m,
			OrderType = tracked?.OrderType,
			OrderState = OrderStates.Done,
			OrderStringId = id,
			TransactionId = tracked?.TransactionId ?? cancelMsg.OriginalTransactionId,
			OriginalTransactionId = cancelMsg.TransactionId,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		_ = cancelMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"Bitpanda Fusion does not expose bulk order cancellation.");
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!lookupMsg.IsSubscribe)
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}
		if (!lookupMsg.PortfolioName.IsEmpty() &&
			!lookupMsg.PortfolioName.EqualsIgnoreCase(GetPortfolioName()))
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId,
				cancellationToken);
			return;
		}

		var portfolio = GetPortfolioName();
		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = portfolio,
			BoardCode = BoardCodes.BitpandaFusion,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		foreach (var balance in await RestClient.GetBalancesAsync(null,
			cancellationToken) ?? [])
		{
			if (balance?.Symbol.IsEmpty() != false)
				continue;
			var available = balance.Available.ToDecimalOrZero();
			var locked = balance.Locked.ToDecimalOrZero();
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = portfolio,
				SecurityId = balance.Symbol.ToUpperInvariant().ToStockSharp(),
				ServerTime = CurrentTime,
				OriginalTransactionId = lookupMsg.TransactionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, available + locked, true)
			.TryAdd(PositionChangeTypes.BlockedValue, locked, true),
				cancellationToken);
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		await SendSubscriptionFinishedAsync(lookupMsg.TransactionId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!statusMsg.IsSubscribe)
		{
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
			return;
		}
		if (statusMsg.Count is <= 0)
		{
			await CompleteOrderStatusAsync(statusMsg, cancellationToken);
			return;
		}
		if (!statusMsg.PortfolioName.IsEmpty() &&
			!statusMsg.PortfolioName.EqualsIgnoreCase(GetPortfolioName()))
		{
			await CompleteOrderStatusAsync(statusMsg, cancellationToken);
			return;
		}

		var specificId = statusMsg.HasOrderId()
			? ResolveOrderId(statusMsg.OrderId, statusMsg.OrderStringId)
			: null;
		if (!specificId.IsEmpty())
		{
			var order = await RestClient.GetOrderAsync(specificId, cancellationToken);
			if (order is not null && MatchesOrder(order, statusMsg))
				await SendOrderAsync(order, statusMsg.TransactionId,
					GetTrackedOrder(order.Id), cancellationToken);
			await SendTradesAsync(statusMsg, specificId, cancellationToken);
			await CompleteOrderStatusAsync(statusMsg, cancellationToken);
			return;
		}

		var pairFilter = CreatePairFilter(statusMsg);
		var skip = Math.Max(0, statusMsg.Skip ?? 0);
		var needed = statusMsg.Count is long count
			? count > long.MaxValue - skip ? long.MaxValue : count + skip
			: long.MaxValue;
		var orders = await LoadOrdersAsync(new()
		{
			Pair = pairFilter,
			From = statusMsg.From?.ToBitpandaFusionUtc(),
			To = statusMsg.To?.ToBitpandaFusionUtc(),
		}, needed, cancellationToken);
		var selected = orders
			.Where(order => MatchesOrder(order, statusMsg))
			.OrderBy(GetOrderTime)
			.Skip(skip.Min(int.MaxValue).To<int>());
		if (statusMsg.Count is long requested)
			selected = selected.Take(requested.Min(int.MaxValue).To<int>());
		foreach (var order in selected)
			await SendOrderAsync(order, statusMsg.TransactionId,
				GetTrackedOrder(order.Id), cancellationToken);

		await SendTradesAsync(statusMsg, null, cancellationToken);
		await CompleteOrderStatusAsync(statusMsg, cancellationToken);
	}

	private async ValueTask<List<BitpandaFusionOrder>> LoadOrdersAsync(
		BitpandaFusionOrdersFilter initial, long maximum,
		CancellationToken cancellationToken)
	{
		var result = new List<BitpandaFusionOrder>();
		var cursors = new HashSet<string>(StringComparer.Ordinal);
		var cursor = initial.Cursor;
		while (result.Count < maximum)
		{
			var left = maximum == long.MaxValue
				? 100
				: Math.Min(100, maximum - result.Count).To<int>();
			var page = await RestClient.GetOrdersAsync(new()
			{
				Pair = initial.Pair,
				Status = initial.Status,
				From = initial.From,
				To = initial.To,
				Limit = left,
				Cursor = cursor,
			}, cancellationToken);
			var values = page?.Data ?? [];
			result.AddRange(values.Where(static order => order is not null));
			var next = page?.Meta?.NextCursor;
			if (page?.Meta?.IsNextPageAvailable != true || next.IsEmpty() ||
				!cursors.Add(next) || values.Length == 0)
				break;
			cursor = next;
		}
		return result;
	}

	private async ValueTask SendTradesAsync(OrderStatusMessage statusMsg,
		string orderId, CancellationToken cancellationToken)
	{
		var maximum = statusMsg.Count ?? long.MaxValue;
		var pairFilter = CreatePairFilter(statusMsg);
		var cursor = default(string);
		var cursors = new HashSet<string>(StringComparer.Ordinal);
		var sent = 0L;
		while (sent < maximum)
		{
			var limit = maximum == long.MaxValue
				? 100
				: Math.Min(100, maximum - sent).To<int>();
			var page = await RestClient.GetTradesAsync(new()
			{
				Pair = pairFilter,
				OrderId = orderId,
				From = statusMsg.From?.ToBitpandaFusionUtc(),
				To = statusMsg.To?.ToBitpandaFusionUtc(),
				Limit = limit,
				Cursor = cursor,
			}, cancellationToken);
			var values = page?.Data ?? [];
			foreach (var trade in values.Where(trade =>
				MatchesTrade(trade, statusMsg)).OrderBy(GetTradeTime))
			{
				await SendTradeAsync(trade, statusMsg.TransactionId, false,
					cancellationToken);
				if (++sent >= maximum)
					return;
			}
			var next = page?.Meta?.NextCursor;
			if (page?.Meta?.IsNextPageAvailable != true || next.IsEmpty() ||
				!cursors.Add(next) || values.Length == 0)
				break;
			cursor = next;
		}
	}

	private async ValueTask SendOrderAsync(BitpandaFusionOrder order,
		long originalTransactionId, TrackedOrder tracked,
		CancellationToken cancellationToken)
	{
		if (order?.Id.IsEmpty() != false)
			return;
		tracked ??= GetTrackedOrder(order.Id);
		var pair = order.Pair.IsEmpty(tracked?.Pair);
		if (pair.IsEmpty())
			throw new InvalidDataException(
				$"Bitpanda Fusion order '{order.Id}' has no trading pair.");
		pair = NormalizePair(pair);
		var volume = order.Quantity.ToNullableDecimal() ?? tracked?.Volume ?? 0m;
		var filled = order.FilledQuantity.ToDecimalOrZero();
		var side = tracked?.Side ?? order.Side.ToStockSharp();
		var orderType = tracked?.OrderType ?? order.Type.ToStockSharp();
		var triggerPrice = order.TriggerPrice.ToNullableDecimal() ??
			tracked?.TriggerPrice;
		var state = order.Status.ToStockSharp();
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = pair.ToStockSharp(),
			ServerTime = GetOrderTime(order),
			PortfolioName = GetPortfolioName(),
			Side = side,
			OrderVolume = volume,
			Balance = Math.Max(0m, volume - filled),
			OrderPrice = order.LimitPrice.ToNullableDecimal() ?? tracked?.Price ?? 0m,
			AveragePrice = order.FilledAveragePrice.ToNullableDecimal(),
			OrderType = orderType,
			OrderState = state,
			OrderStringId = order.Id,
			TransactionId = tracked?.TransactionId ?? 0,
			OriginalTransactionId = originalTransactionId,
			TimeInForce = tracked?.TimeInForce ?? order.TimeInForce.ToStockSharp(),
			Commission = order.Fee?.Amount.ToNullableDecimal(),
			CommissionCurrency = order.Fee?.Currency,
			Condition = new BitpandaFusionOrderCondition
			{
				TriggerPrice = triggerPrice,
			},
			Error = state == OrderStates.Failed
				? new InvalidOperationException(
					$"Bitpanda Fusion rejected order '{order.Id}'.")
				: null,
		}, cancellationToken);
	}

	private ValueTask SendTradeAsync(BitpandaFusionTrade trade,
		long originalTransactionId, bool onlyNew,
		CancellationToken cancellationToken)
	{
		if (trade?.Id.IsEmpty() != false || trade.Pair.IsEmpty())
			return default;
		var added = AddTradeId(trade.Id);
		if (onlyNew && !added)
			return default;
		var tracked = GetTrackedOrder(trade.OrderId);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = NormalizePair(trade.Pair).ToStockSharp(),
			ServerTime = GetTradeTime(trade),
			PortfolioName = GetPortfolioName(),
			Side = trade.Side.ToStockSharp(),
			OrderStringId = trade.OrderId,
			TradeStringId = trade.Id,
			TradePrice = trade.Price.ToNullableDecimal(),
			TradeVolume = trade.Quantity.ToNullableDecimal() ??
				trade.Amount.ToNullableDecimal(),
			Commission = trade.Fee?.Amount.ToNullableDecimal(),
			CommissionCurrency = trade.Fee?.Currency.IsEmpty(trade.Fee?.Symbol),
			TransactionId = tracked?.TransactionId ?? 0,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private bool MatchesOrder(BitpandaFusionOrder order, OrderStatusMessage message)
	{
		if (order is null || order.Id.IsEmpty() || order.Pair.IsEmpty())
			return false;
		if (!MatchesSecurity(order.Pair, message))
			return false;
		if (message.States.Length > 0 &&
			!message.States.Contains(order.Status.ToStockSharp()))
			return false;
		if (message.Side is Sides side && order.Side.ToStockSharp() != side)
			return false;
		var volume = order.Quantity.ToNullableDecimal();
		if (message.Volume is decimal requestedVolume && volume != requestedVolume)
			return false;
		var time = GetOrderFilterTime(order);
		return (message.From is null ||
			time >= message.From.Value.ToBitpandaFusionUtc()) &&
			(message.To is null ||
			time <= message.To.Value.ToBitpandaFusionUtc());
	}

	private bool MatchesTrade(BitpandaFusionTrade trade, OrderStatusMessage message)
	{
		if (trade is null || trade.Id.IsEmpty() || trade.Pair.IsEmpty())
			return false;
		if (!MatchesSecurity(trade.Pair, message))
			return false;
		if (message.Side is Sides side && trade.Side.ToStockSharp() != side)
			return false;
		var time = GetTradeTime(trade);
		return (message.From is null ||
			time >= message.From.Value.ToBitpandaFusionUtc()) &&
			(message.To is null ||
			time <= message.To.Value.ToBitpandaFusionUtc());
	}

	private bool MatchesSecurity(string pair, OrderStatusMessage message)
	{
		var requested = new List<SecurityId>();
		if (!message.SecurityId.SecurityCode.IsEmpty())
			requested.Add(message.SecurityId);
		requested.AddRange(message.SecurityIds.Where(static id =>
			!id.SecurityCode.IsEmpty()));
		return requested.Count == 0 || requested.Any(id =>
			NormalizePair(id.SecurityCode).EqualsIgnoreCase(NormalizePair(pair)));
	}

	private string CreatePairFilter(OrderStatusMessage message)
	{
		var securityIds = new List<SecurityId>();
		if (!message.SecurityId.SecurityCode.IsEmpty())
			securityIds.Add(message.SecurityId);
		securityIds.AddRange(message.SecurityIds.Where(static id =>
			!id.SecurityCode.IsEmpty()));
		return securityIds.Count == 0
			? null
			: securityIds.Select(GetPair)
				.Distinct(StringComparer.OrdinalIgnoreCase).JoinComma();
	}

	private DateTime GetOrderTime(BitpandaFusionOrder order)
		=> (order.UpdatedAt ?? order.ExecutedAt ?? order.CreatedAt)?.UtcDateTime ??
			CurrentTime;

	private DateTime GetOrderFilterTime(BitpandaFusionOrder order)
		=> (order.CreatedAt ?? order.UpdatedAt ?? order.ExecutedAt)?.UtcDateTime ??
			CurrentTime;

	private DateTime GetTradeTime(BitpandaFusionTrade trade)
		=> trade.ExecutedAt == default ? CurrentTime : trade.ExecutedAt.UtcDateTime;

	private async ValueTask CompleteOrderStatusAsync(OrderStatusMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
	}
}
