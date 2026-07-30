namespace StockSharp.BitoPro;

public partial class BitoProMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var market = GetMarket(regMsg.SecurityId);
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException(
				"BitoPro order volume must be positive.");
		if (regMsg.VisibleVolume is > 0 &&
			regMsg.VisibleVolume != volume)
			throw new NotSupportedException(
				"BitoPro does not document iceberg orders.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException(
				"BitoPro does not document GTD orders.");
		if (regMsg.TimeInForce is TimeInForce.MatchOrCancel or
			TimeInForce.CancelBalance)
			throw new NotSupportedException(
				"BitoPro supports only GTC and post-only orders.");

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market or
			OrderTypes.Conditional))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(
					orderType, regMsg.TransactionId));
		var condition = regMsg.Condition as BitoProOrderCondition;
		var isConditional = orderType == OrderTypes.Conditional ||
			condition?.TriggerPrice is not null;
		if (isConditional && condition?.TriggerPrice is not > 0)
			throw new InvalidOperationException(
				"BitoPro stop-limit orders require a positive " +
					"trigger price.");
		if ((orderType == OrderTypes.Limit || isConditional) &&
			regMsg.Price <= 0)
			throw new InvalidOperationException(
				"BitoPro limit orders require a positive price.");
		if (regMsg.PostOnly == true &&
			(orderType != OrderTypes.Limit || isConditional))
			throw new NotSupportedException(
				"BitoPro post-only execution is available only " +
					"for limit orders.");

		var wireAmount = volume;
		if (orderType == OrderTypes.Market &&
			regMsg.Side == Sides.Buy)
		{
			var ticker = await RestClient.GetTickerAsync(
				market.Pair, cancellationToken);
			if (ticker?.LastPrice is not > 0)
				throw new InvalidDataException(
					"BitoPro returned no price for converting a " +
						"market-buy amount to quote currency.");
			wireAmount *= ticker.LastPrice.Value;
			wireAmount = decimal.Round(
				wireAmount,
				market.QuotePrecision.Max(0).Min(28),
				MidpointRounding.ToZero);
			if (wireAmount <= 0)
				throw new InvalidOperationException(
					"BitoPro market-buy quote amount rounds to zero.");
		}

		var clientId = BitoProExtensions.CreateClientId(
			regMsg.TransactionId);
		var result = await RestClient.PlaceOrderAsync(
			market.Pair,
			new BitoProPlaceOrderRequest
			{
				Action = regMsg.Side.ToBitoPro(),
				Amount = wireAmount.ToWire(),
				Price = orderType == OrderTypes.Market
					? null
					: regMsg.Price.ToWire(),
				Timestamp = DateTime.UtcNow.ToBitoProMilliseconds(),
				Type = isConditional
					? "STOP_LIMIT"
					: orderType == OrderTypes.Market
						? "MARKET"
						: "LIMIT",
				StopPrice = isConditional
					? condition.TriggerPrice.Value.ToWire()
					: null,
				Condition = isConditional
					? condition.TriggerOnGreaterOrEqual ? ">=" : "<="
					: null,
				TimeInForce = regMsg.PostOnly == true
					? "POST_ONLY"
					: "GTC",
				ClientId = clientId,
			},
			cancellationToken);
		if (result?.OrderId.IsEmpty() != false)
			throw new InvalidDataException(
				"BitoPro accepted an order without returning " +
					"its identifier.");

		var tracked = new TrackedOrder
		{
			TransactionId = regMsg.TransactionId,
			SecurityCode = market.SecurityCode,
			Side = regMsg.Side,
			OrderType = isConditional
				? OrderTypes.Conditional
				: orderType,
			Volume = volume,
			Price = regMsg.Price,
			TriggerPrice = condition?.TriggerPrice,
		};
		TrackOrder(result.OrderId, tracked);
		await SendTrackedOrderAsync(
			result.OrderId,
			tracked,
			OrderStates.Active,
			volume,
			regMsg.TransactionId,
			regMsg.PostOnly == true,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var orderId = ResolveOrderId(
			cancelMsg.OrderId, cancelMsg.OrderStringId);
		var tracked = GetTrackedOrder(orderId);
		var market = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? tracked is null
				? throw new InvalidOperationException(
					"BitoPro cancellation requires a security ID " +
						"for an untracked order.")
				: GetMarket(new SecurityId
				{
					SecurityCode = tracked.SecurityCode,
					BoardCode = BoardCodes.BitoPro,
				})
			: GetMarket(cancelMsg.SecurityId);
		await RestClient.CancelOrderAsync(
			market.Pair, orderId, cancellationToken);
		using (_sync.EnterScope())
			_knownActiveOrderIds.Remove(orderId);
		if (tracked is not null)
			await SendTrackedOrderAsync(
				orderId,
				tracked,
				OrderStates.Done,
				0,
				cancelMsg.TransactionId,
				false,
				cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		_ = replaceMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"BitoPro does not provide an atomic order-replace " +
				"operation.");
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		if (cancelMsg.Mode.HasFlag(
			OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"BitoPro spot bulk cancellation cannot close positions.");
		var market = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(cancelMsg.SecurityId);
		if (cancelMsg.Side is null)
		{
			await RestClient.CancelAllOrdersAsync(
				market?.Pair, cancellationToken);
			using (_sync.EnterScope())
				_knownActiveOrderIds.Clear();
			return;
		}

		var orders = await RestClient.GetOpenOrdersAsync(
			market?.Pair, cancellationToken) ?? [];

		foreach (var order in orders.Where(order =>
			order?.Id.IsEmpty() == false &&
			order.Action.ToSide() == cancelMsg.Side))
		{
			await RestClient.CancelOrderAsync(
				order.Pair, order.Id, cancellationToken);
			using (_sync.EnterScope())
				_knownActiveOrderIds.Remove(order.Id);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			lookupMsg.TransactionId, cancellationToken);

		EnsurePrivateReady();
		if (!lookupMsg.IsSubscribe)
		{
			_portfolioSubscriptionId = 0;
			return;
		}
		var portfolioName = GetPortfolioName();
		if (lookupMsg.PortfolioName.IsEmpty() ||
			lookupMsg.PortfolioName.EqualsIgnoreCase(portfolioName))
		{
			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = portfolioName,
				BoardCode = BoardCodes.BitoPro,
				OriginalTransactionId = lookupMsg.TransactionId,
			}, cancellationToken);
			await SendPortfolioSnapshotAsync(
				lookupMsg.TransactionId, cancellationToken);
		}
		await SendSubscriptionResultAsync(
			lookupMsg, cancellationToken);

		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(
				lookupMsg.TransactionId, cancellationToken);
			return;
		}

		_portfolioSubscriptionId = lookupMsg.TransactionId;
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			statusMsg.TransactionId, cancellationToken);

		EnsurePrivateReady();
		if (!statusMsg.IsSubscribe)
		{
			_orderStatusSubscriptionId = 0;
			return;
		}
		if (statusMsg.Count is <= 0)
		{
			await CompleteOrderStatusAsync(
				statusMsg, cancellationToken);
			return;
		}

		var maximum = (statusMsg.Count ?? 100)
			.Min(1000).Max(1).To<int>();
		await SendOrderSnapshotAsync(
			statusMsg, maximum, cancellationToken);
		await SendSubscriptionResultAsync(
			statusMsg, cancellationToken);

		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(
				statusMsg.TransactionId, cancellationToken);
			return;
		}

		_orderStatusSubscriptionId = statusMsg.TransactionId;
	}

	private async ValueTask SendPortfolioSnapshotAsync(
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var balances = await RestClient.GetBalancesAsync(
			cancellationToken);

		foreach (var balance in balances ?? [])
			await SendBalanceAsync(
				balance, originalTransactionId, cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(
		OrderStatusMessage statusMsg, int maximum,
		CancellationToken cancellationToken)
	{
		var requestedOrderId = statusMsg.HasOrderId()
			? ResolveOrderId(
				statusMsg.OrderId, statusMsg.OrderStringId)
			: null;
		var markets = GetStatusMarkets(statusMsg);
		if (requestedOrderId is not null)
		{
			if (markets.Length == 0)
			{
				var tracked = GetTrackedOrder(requestedOrderId);
				if (tracked is null)
					throw new InvalidOperationException(
						"BitoPro order lookup requires a security ID " +
							"for an untracked order.");
				markets =
				[
					GetMarket(new SecurityId
					{
						SecurityCode = tracked.SecurityCode,
						BoardCode = BoardCodes.BitoPro,
					}),
				];
			}

			foreach (var market in markets)
			{
				var order = await RestClient.GetOrderAsync(
					market.Pair, requestedOrderId, cancellationToken);
				if (MatchesOrder(order, statusMsg))
					await SendOrderAsync(
						order, statusMsg.TransactionId,
						cancellationToken);
			}

			return;
		}

		var orders = new List<BitoProOrder>(
			await RestClient.GetOpenOrdersAsync(
				markets.Length == 1 ? markets[0].Pair : null,
				cancellationToken) ?? []);
		if (statusMsg.IsHistoryOnly() ||
			statusMsg.From is not null ||
			statusMsg.To is not null)
		{
			if (markets.Length == 0)
			{
				using (_sync.EnterScope())
					markets = [.. _marketsBySecurity.Values];
			}

			foreach (var market in markets)
				orders.AddRange(await RestClient.GetOrdersAsync(
					market.Pair,
					statusMsg.From,
					statusMsg.To,
					maximum,
					cancellationToken) ?? []);
		}

		foreach (var order in orders
			.Where(order => MatchesOrder(order, statusMsg))
			.GroupBy(static order => order.Id, StringComparer.Ordinal)
			.Select(static group => group
				.OrderByDescending(GetOrderTimestamp)
				.First())
			.OrderBy(GetOrderTimestamp)
			.TakeLast(maximum))
			await SendOrderAsync(
				order, statusMsg.TransactionId, cancellationToken);

		if (statusMsg.IsHistoryOnly() ||
			statusMsg.From is not null)
		{
			foreach (var market in markets)
				await SendPrivateTradesAsync(
					market,
					await RestClient.GetPrivateTradesAsync(
						market.Pair,
						statusMsg.From,
						statusMsg.To,
						maximum,
						cancellationToken) ?? [],
					statusMsg.TransactionId,
					false,
					cancellationToken);
		}
	}

	private async ValueTask PollOrderUpdatesAsync(
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var openOrders = await RestClient.GetOpenOrdersAsync(
			null, cancellationToken) ?? [];
		var currentIds = openOrders
			.Where(static order => order?.Id.IsEmpty() == false)
			.Select(static order => order.Id)
			.ToHashSet(StringComparer.Ordinal);
		string[] removed;
		using (_sync.EnterScope())
		{
			removed = [.. _knownActiveOrderIds.Where(
				id => !currentIds.Contains(id))];
			_knownActiveOrderIds.Clear();
			_knownActiveOrderIds.AddRange(currentIds);
		}

		foreach (var order in openOrders.OrderBy(GetOrderTimestamp))
			await SendOrderAsync(
				order, originalTransactionId, cancellationToken);

		foreach (var orderId in removed)
		{
			var tracked = GetTrackedOrder(orderId);
			if (tracked is null)
				continue;
			var market = GetMarket(new SecurityId
			{
				SecurityCode = tracked.SecurityCode,
				BoardCode = BoardCodes.BitoPro,
			});
			try
			{
				await SendOrderAsync(
					await RestClient.GetOrderAsync(
						market.Pair, orderId, cancellationToken),
					originalTransactionId,
					cancellationToken);
			}
			catch (HttpRequestException)
			{
				await SendTrackedOrderAsync(
					orderId,
					tracked,
					OrderStates.Done,
					0,
					originalTransactionId,
					false,
					cancellationToken);
			}
		}

		BitoProSymbol[] trackedMarkets;
		using (_sync.EnterScope())
			trackedMarkets = [.. _trackedOrders.Values
				.Select(order => _marketsBySecurity.TryGetValue(
					order.SecurityCode, out var market)
						? market
						: null)
				.Where(static market => market is not null)
				.Distinct()];

		foreach (var market in trackedMarkets)
			await SendPrivateTradesAsync(
				market,
				await RestClient.GetPrivateTradesAsync(
					market.Pair,
					DateTime.UtcNow.AddMinutes(-5),
					DateTime.UtcNow,
					100,
					cancellationToken) ?? [],
				originalTransactionId,
				true,
				cancellationToken);
	}

	private ValueTask SendBalanceAsync(BitoProBalance balance,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (balance?.Currency.IsEmpty() != false)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(),
			SecurityId = new()
			{
				SecurityCode = balance.Currency.ToUpperInvariant(),
				BoardCode = BoardCodes.BitoPro,
			},
			ServerTime = CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue,
			balance.Available, true)
		.TryAdd(PositionChangeTypes.BlockedValue,
			(balance.Amount - balance.Available).Max(0), true),
			cancellationToken);
	}

	private ValueTask SendOrderAsync(BitoProOrder order,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (order?.Id.IsEmpty() != false || order.Pair.IsEmpty())
			return default;
		var market = GetMarket(order.Pair);
		if (market is null)
			return default;
		var tracked = GetTrackedOrder(order.Id);
		if (tracked is null)
		{
			tracked = new()
			{
				TransactionId = order.ClientId ?? 0,
				SecurityCode = market.SecurityCode,
				Side = order.Action.ToSide(),
				OrderType = order.ToOrderType(),
				Volume = order.OriginalAmount,
				Price = order.Price,
				TriggerPrice = order.StopPrice,
			};
			TrackOrder(order.Id, tracked);
		}
		var state = order.Status.ToOrderState();
		if (state is OrderStates.Done or OrderStates.Failed)
		{
			using (_sync.EnterScope())
				_knownActiveOrderIds.Remove(order.Id);
		}
		var execution = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = GetOrderTime(order),
			PortfolioName = GetPortfolioName(),
			Side = order.Action.ToSide(),
			OrderVolume = tracked.Volume > 0
				? tracked.Volume
				: order.OriginalAmount,
			Balance = order.RemainingAmount,
			OrderPrice = order.Price,
			OrderType = order.ToOrderType(),
			OrderState = state,
			OrderStringId = order.Id,
			TransactionId = tracked.TransactionId,
			OriginalTransactionId = originalTransactionId,
			PostOnly = order.TimeInForce.EqualsIgnoreCase("POST_ONLY"),
			Condition = order.StopPrice is > 0
				? new BitoProOrderCondition
				{
					TriggerPrice = order.StopPrice,
					TriggerOnGreaterOrEqual =
						order.Condition != "<=",
				}
				: null,
		};
		if (long.TryParse(order.Id, NumberStyles.None,
			CultureInfo.InvariantCulture, out var numericOrderId))
			execution.OrderId = numericOrderId;
		return SendOutMessageAsync(execution, cancellationToken);
	}

	private ValueTask SendTrackedOrderAsync(string orderId,
		TrackedOrder tracked, OrderStates state, decimal balance,
		long originalTransactionId, bool postOnly,
		CancellationToken cancellationToken)
	{
		var execution = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = tracked.SecurityCode.ToBitoProSecurityId(),
			ServerTime = CurrentTime,
			PortfolioName = GetPortfolioName(),
			Side = tracked.Side,
			OrderVolume = tracked.Volume,
			Balance = balance,
			OrderPrice = tracked.Price,
			OrderType = tracked.OrderType,
			OrderState = state,
			OrderStringId = orderId,
			TransactionId = tracked.TransactionId,
			OriginalTransactionId = originalTransactionId,
			PostOnly = postOnly,
			Condition = tracked.TriggerPrice is decimal triggerPrice
				? new BitoProOrderCondition
				{
					TriggerPrice = triggerPrice,
				}
				: null,
		};
		if (long.TryParse(orderId, NumberStyles.None,
			CultureInfo.InvariantCulture, out var numericOrderId))
			execution.OrderId = numericOrderId;
		return SendOutMessageAsync(execution, cancellationToken);
	}

	private async ValueTask SendPrivateTradesAsync(
		BitoProSymbol market,
		IEnumerable<BitoProPrivateTrade> trades,
		long originalTransactionId,
		bool onlyNew,
		CancellationToken cancellationToken)
	{
		foreach (var trade in (trades ?? [])
			.Where(static trade =>
				trade?.TradeId.IsEmpty() == false)
			.OrderBy(GetTradeTimestamp))
		{
			var added = AddTrade(
				market.Pair, trade.TradeId, true);
			if (onlyNew && !added)
				continue;
			var tracked = GetTrackedOrder(trade.OrderId);
			var execution = new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				SecurityId = market.ToStockSharp(),
				ServerTime = GetTradeTime(trade),
				PortfolioName = GetPortfolioName(),
				Side = trade.Action.ToSide(),
				OrderStringId = trade.OrderId,
				TradeStringId = trade.TradeId,
				TradePrice = trade.Price,
				TradeVolume = trade.BaseAmount,
				Commission = trade.Fee,
				CommissionCurrency = trade.FeeSymbol,
				TransactionId = tracked?.TransactionId ?? 0,
				OriginalTransactionId = originalTransactionId,
			};
			if (long.TryParse(trade.OrderId, NumberStyles.None,
				CultureInfo.InvariantCulture, out var orderId))
				execution.OrderId = orderId;
			if (long.TryParse(trade.TradeId, NumberStyles.None,
				CultureInfo.InvariantCulture, out var tradeId))
				execution.TradeId = tradeId;
			await SendOutMessageAsync(execution, cancellationToken);
		}
	}

	private BitoProSymbol[] GetStatusMarkets(OrderStatusMessage filter)
	{
		var ids = new List<SecurityId>();
		if (!filter.SecurityId.SecurityCode.IsEmpty())
			ids.Add(filter.SecurityId);
		ids.AddRange(filter.SecurityIds.Where(
			static id => !id.SecurityCode.IsEmpty()));
		return [.. ids
			.Select(GetMarket)
			.Distinct()];
	}

	private bool MatchesOrder(
		BitoProOrder order, OrderStatusMessage filter)
	{
		if (order?.Id.IsEmpty() != false || order.Pair.IsEmpty())
			return false;
		var time = GetOrderTime(order);
		if (filter.From is DateTime from &&
			time < from.ToUtc() ||
			filter.To is DateTime to &&
			time > to.ToUtc())
			return false;
		if (filter.Side is Sides side &&
			order.Action.ToSide() != side)
			return false;
		var state = order.Status.ToOrderState();
		if (filter.States.Length > 0 &&
			!filter.States.Contains(state))
			return false;
		if (filter.Volume is decimal volume &&
			order.OriginalAmount != volume)
			return false;
		if (!filter.PortfolioName.IsEmpty() &&
			!filter.PortfolioName.EqualsIgnoreCase(GetPortfolioName()))
			return false;
		var markets = GetStatusMarkets(filter);
		return markets.Length == 0 ||
			markets.Any(market =>
				market.Pair.EqualsIgnoreCase(order.Pair));
	}

	private DateTime GetOrderTime(BitoProOrder order)
	{
		var timestamp = GetOrderTimestamp(order);
		return timestamp > 0
			? timestamp.FromBitoProMilliseconds()
			: CurrentTime;
	}

	private static long GetOrderTimestamp(BitoProOrder order)
		=> order?.UpdatedTimestamp > 0
			? order.UpdatedTimestamp
			: order?.CreatedTimestamp > 0
				? order.CreatedTimestamp
				: order?.Timestamp ?? 0;

	private DateTime GetTradeTime(BitoProPrivateTrade trade)
	{
		var timestamp = GetTradeTimestamp(trade);
		return timestamp > 0
			? timestamp.FromBitoProMilliseconds()
			: CurrentTime;
	}

	private static long GetTradeTimestamp(BitoProPrivateTrade trade)
		=> trade?.CreatedTimestamp > 0
			? trade.CreatedTimestamp
			: trade?.Timestamp ?? 0;

	private async ValueTask CompleteOrderStatusAsync(
		OrderStatusMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(
			message.TransactionId, cancellationToken);
	}
}
