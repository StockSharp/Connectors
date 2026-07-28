namespace StockSharp.NovaDax;

public partial class NovaDaxMessageAdapter
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
				"NovaDAX order volume must be positive.");
		if (regMsg.VisibleVolume is > 0 &&
			regMsg.VisibleVolume != volume)
			throw new NotSupportedException(
				"NovaDAX does not document iceberg orders.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException(
				"NovaDAX does not document GTD orders.");
		if (regMsg.TimeInForce is
			TimeInForce.CancelBalance or
			TimeInForce.MatchOrCancel)
			throw new NotSupportedException(
				"NovaDAX does not expose IOC or FOK order options.");
		if (regMsg.PostOnly == true)
			throw new NotSupportedException(
				"NovaDAX does not expose post-only orders.");

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (
			OrderTypes.Limit or
			OrderTypes.Market or
			OrderTypes.Conditional))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(
					orderType, regMsg.TransactionId));
		var condition = regMsg.Condition as NovaDaxOrderCondition;
		if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
			throw new InvalidOperationException(
				"NovaDAX limit orders require a positive price.");
		if (orderType == OrderTypes.Conditional && regMsg.Price < 0)
			throw new InvalidOperationException(
				"NovaDAX conditional order price cannot be negative.");
		if (orderType == OrderTypes.Conditional &&
			condition?.StopPrice is not > 0)
			throw new InvalidOperationException(
				"NovaDAX conditional orders require a stop price.");

		var clientId = NovaDaxExtensions.CreateClientId(
			regMsg.TransactionId);
		string quoteValue = null;
		if (orderType == OrderTypes.Market &&
			regMsg.Side == Sides.Buy)
		{
			var ticker = await RestClient.GetTickerAsync(
				market.Pair, cancellationToken);
			if (ticker?.LastPrice is not > 0)
				throw new InvalidDataException(
					"NovaDAX returned no price for a market buy.");
			quoteValue =
				(volume * ticker.LastPrice.Value).ToWire();
		}
		var nativeType = orderType switch
		{
			OrderTypes.Market => "MARKET",
			OrderTypes.Conditional when regMsg.Price > 0 =>
				"STOP_LIMIT",
			OrderTypes.Conditional => "STOP_MARKET",
			_ => "LIMIT",
		};
		var result = await RestClient.PlaceOrderAsync(
			market.Pair,
			new NovaDaxPlaceOrderRequest
			{
				Symbol = market.Pair,
				Type = nativeType,
				Side = regMsg.Side.ToNovaDax(),
				Amount = orderType == OrderTypes.Market &&
					regMsg.Side == Sides.Buy
						? null
						: volume.ToWire(),
				Value = quoteValue,
				Price = orderType == OrderTypes.Market ||
					nativeType == "STOP_MARKET"
					? null
					: regMsg.Price.ToWire(),
				StopPrice = condition?.StopPrice?.ToWire(),
				Operator = condition is null
					? null
					: condition.Operator ==
						NovaDaxStopOperators.LessOrEqual
							? "LTE"
							: "GTE",
				ClientOrderId = clientId,
			},
			cancellationToken);
		if (result?.OrderId.IsEmpty() != false)
			throw new InvalidDataException(
				"NovaDAX accepted an order without returning " +
					"its identifier.");

		var tracked = new TrackedOrder
		{
			TransactionId = regMsg.TransactionId,
			SecurityCode = market.SecurityCode,
			Side = regMsg.Side,
			OrderType = orderType,
			Volume = volume,
			Price = regMsg.Price,
			TriggerPrice = condition?.StopPrice,
		};
		TrackOrder(result.OrderId, tracked);
		await SendTrackedOrderAsync(
			result.OrderId,
			tracked,
			OrderStates.Active,
			volume,
			regMsg.TransactionId,
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
					"NovaDax cancellation requires a security ID " +
						"for an untracked order.")
				: GetMarket(new SecurityId
				{
					SecurityCode = tracked.SecurityCode,
					BoardCode = BoardCodes.NovaDax,
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
			"NovaDax does not provide an atomic order-replace " +
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
				"NovaDax spot bulk cancellation cannot close positions.");
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

		var orders = await GetOpenOrdersAsync(
			market is null
				? GetAllMarkets()
				: [market],
			cancellationToken);
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
				BoardCode = BoardCodes.NovaDax,
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
						"NovaDax order lookup requires a security ID " +
							"for an untracked order.");
				markets =
				[
					GetMarket(new SecurityId
					{
						SecurityCode = tracked.SecurityCode,
						BoardCode = BoardCodes.NovaDax,
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

		if (markets.Length == 0)
			markets = GetAllMarkets();
		var orders = new List<NovaDaxOrder>(
			await GetOpenOrdersAsync(markets, cancellationToken));
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
		var openOrders = await GetOpenOrdersAsync(
			GetAllMarkets(), cancellationToken);
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
				BoardCode = BoardCodes.NovaDax,
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
					cancellationToken);
			}
		}

		NovaDaxSymbol[] trackedMarkets;
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

	private ValueTask SendBalanceAsync(NovaDaxBalance balance,
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
				BoardCode = BoardCodes.NovaDax,
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

	private ValueTask SendOrderAsync(NovaDaxOrder order,
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
			PostOnly = false,
			TimeInForce = Messages.TimeInForce.PutInQueue,
			Condition = order.StopPrice is > 0
				? new NovaDaxOrderCondition
				{
					StopPrice = order.StopPrice,
					Operator = order.Operator
						.EqualsIgnoreCase("LTE")
							? NovaDaxStopOperators.LessOrEqual
							: NovaDaxStopOperators.GreaterOrEqual,
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
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var execution = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = tracked.SecurityCode.ToNovaDaxSecurityId(),
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
			PostOnly = false,
			Condition = tracked.TriggerPrice is > 0
				? new NovaDaxOrderCondition
				{
					StopPrice = tracked.TriggerPrice,
				}
				: null,
		};
		if (long.TryParse(orderId, NumberStyles.None,
			CultureInfo.InvariantCulture, out var numericOrderId))
			execution.OrderId = numericOrderId;
		return SendOutMessageAsync(execution, cancellationToken);
	}

	private async ValueTask SendPrivateTradesAsync(
		NovaDaxSymbol market,
		IEnumerable<NovaDaxPrivateTrade> trades,
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
			if (trade.Action.EqualsIgnoreCase("self-trade") &&
				tracked is null)
				continue;
			var execution = new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				SecurityId = market.ToStockSharp(),
				ServerTime = GetTradeTime(trade),
				PortfolioName = GetPortfolioName(),
				Side = trade.Action.EqualsIgnoreCase("self-trade")
					? tracked.Side
					: trade.Action.ToSide(),
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

	private NovaDaxSymbol[] GetStatusMarkets(OrderStatusMessage filter)
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

	private NovaDaxSymbol[] GetAllMarkets()
	{
		using (_sync.EnterScope())
			return [.. _marketsBySecurity.Values];
	}

	private async ValueTask<NovaDaxOrder[]> GetOpenOrdersAsync(
		IEnumerable<NovaDaxSymbol> markets,
		CancellationToken cancellationToken)
	{
		var orders = new List<NovaDaxOrder>();
		foreach (var market in (markets ?? [])
			.Where(static market => market is not null)
			.Distinct())
			orders.AddRange(
				await RestClient.GetOpenOrdersAsync(
					market.Pair, cancellationToken) ?? []);
		return [.. orders];
	}

	private bool MatchesOrder(
		NovaDaxOrder order, OrderStatusMessage filter)
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

	private DateTime GetOrderTime(NovaDaxOrder order)
	{
		var timestamp = GetOrderTimestamp(order);
		return timestamp > 0
			? timestamp.FromNovaDaxMilliseconds()
			: CurrentTime;
	}

	private static long GetOrderTimestamp(NovaDaxOrder order)
		=> order?.UpdatedTimestamp > 0
			? order.UpdatedTimestamp
			: order?.CreatedTimestamp > 0
				? order.CreatedTimestamp
				: order?.Timestamp ?? 0;

	private DateTime GetTradeTime(NovaDaxPrivateTrade trade)
	{
		var timestamp = GetTradeTimestamp(trade);
		return timestamp > 0
			? timestamp.FromNovaDaxMilliseconds()
			: CurrentTime;
	}

	private static long GetTradeTimestamp(NovaDaxPrivateTrade trade)
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
