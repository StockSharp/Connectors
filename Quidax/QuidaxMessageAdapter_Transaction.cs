namespace StockSharp.Quidax;

public partial class QuidaxMessageAdapter
{
	private static readonly string[] _activeOrderStates =
	[
		"wait",
		"partial_active",
		"pending_cancel",
	];

	private static readonly string[] _allOrderStates =
	[
		"wait",
		"partial_active",
		"pending_cancel",
		"done",
		"cancel",
		"expired",
		"partially_filled_before_cancelled",
	];

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
				"Quidax order volume must be positive.");
		if (regMsg.VisibleVolume is > 0 &&
			regMsg.VisibleVolume != volume)
			throw new NotSupportedException(
				"Quidax does not document iceberg orders.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException(
				"Quidax does not document GTD orders.");
		if (regMsg.PostOnly == true)
			throw new NotSupportedException(
				"Quidax does not document post-only orders.");
		if (regMsg.TimeInForce is not null &&
			regMsg.TimeInForce != TimeInForce.PutInQueue)
			throw new NotSupportedException(
				"Quidax does not expose time-in-force options.");

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (
			OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(
					orderType,
					regMsg.TransactionId));
		if (orderType == OrderTypes.Limit &&
			regMsg.Price <= 0)
			throw new InvalidOperationException(
				"Quidax limit orders require a positive price.");
		if (orderType == OrderTypes.Limit &&
			market.MinimumOrderValue is decimal minimum &&
			regMsg.Price * volume < minimum)
			throw new InvalidOperationException(
				$"Quidax order value {regMsg.Price * volume} " +
					$"is below the {minimum} minimum for " +
					$"'{market.Id}'.");

		var result = await RestClient.PlaceOrderAsync(
			new QuidaxPlaceOrderRequest
			{
				Market = market.Id,
				Side = regMsg.Side.ToQuidax(),
				OrderType = orderType == OrderTypes.Market
					? "market"
					: "limit",
				Price = orderType == OrderTypes.Limit
					? regMsg.Price.ToWire()
					: null,
				Volume = volume.ToWire(),
			},
			cancellationToken);
		if (result?.Id.IsEmpty() != false)
			throw new InvalidDataException(
				"Quidax accepted an order without returning " +
					"its identifier.");

		TrackOrder(result.Id, new()
		{
			TransactionId = regMsg.TransactionId,
			SecurityCode = market.SecurityCode,
			Side = regMsg.Side,
			OrderType = orderType,
			Volume = volume,
			Price = regMsg.Price,
		});
		await SendOrderAsync(
			result,
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
			cancelMsg.OrderId,
			cancelMsg.OrderStringId);
		var result = await RestClient.CancelOrderAsync(
			orderId,
			cancellationToken);
		await SendOrderAsync(
			result,
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
			"Quidax does not provide an atomic order-replace " +
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
				"Quidax spot cancellation cannot close positions.");
		var market = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(cancelMsg.SecurityId);
		var orders = new List<QuidaxOrder>();

		foreach (var state in _activeOrderStates)
			orders.AddRange(await RestClient.GetOrdersAsync(
				market?.Id,
				state,
				100,
				cancellationToken) ?? []);

		foreach (var order in orders
			.Where(order =>
				order?.Id.IsEmpty() == false &&
				(cancelMsg.Side is null ||
					order.Side.ToSide() == cancelMsg.Side))
			.GroupBy(
				static order => order.Id,
				StringComparer.Ordinal)
			.Select(static group => group.First()))
		{
			var result = await RestClient.CancelOrderAsync(
				order.Id,
				cancellationToken);
			await SendOrderAsync(
				result,
				cancelMsg.TransactionId,
				cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			lookupMsg.TransactionId,
			cancellationToken);

		EnsurePrivateReady();
		if (!lookupMsg.IsSubscribe)
		{
			if (_portfolioSubscriptionId ==
				lookupMsg.OriginalTransactionId)
				_portfolioSubscriptionId = 0;
			return;
		}

		var portfolioName = GetPortfolioName();
		if (lookupMsg.PortfolioName.IsEmpty() ||
			lookupMsg.PortfolioName.EqualsIgnoreCase(
				portfolioName))
		{
			await SendOutMessageAsync(
				new PortfolioMessage
				{
					PortfolioName = portfolioName,
					BoardCode = BoardCodes.Quidax,
					OriginalTransactionId =
						lookupMsg.TransactionId,
				},
				cancellationToken);
			await SendPortfolioSnapshotAsync(
				lookupMsg.TransactionId,
				cancellationToken);
		}

		await SendSubscriptionResultAsync(
			lookupMsg,
			cancellationToken);

		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(
				lookupMsg.TransactionId,
				cancellationToken);
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
			statusMsg.TransactionId,
			cancellationToken);

		EnsurePrivateReady();
		if (!statusMsg.IsSubscribe)
		{
			if (_orderStatusSubscriptionId ==
				statusMsg.OriginalTransactionId)
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
			.Min(100).Max(1).To<int>();
		await SendOrderSnapshotAsync(
			statusMsg,
			maximum,
			cancellationToken);
		await SendSubscriptionResultAsync(
			statusMsg,
			cancellationToken);

		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(
				statusMsg.TransactionId,
				cancellationToken);
			return;
		}

		_orderStatusSubscriptionId = statusMsg.TransactionId;
	}

	private async ValueTask SendPortfolioSnapshotAsync(
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var wallets = await RestClient.GetWalletsAsync(
			cancellationToken);

		foreach (var wallet in wallets ?? [])
			await SendWalletAsync(
				wallet,
				originalTransactionId,
				cancellationToken);
	}

	private ValueTask SendWalletAsync(
		QuidaxWallet wallet,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (wallet?.Currency.IsEmpty() != false)
			return default;
		return SendOutMessageAsync(
			new PositionChangeMessage
			{
				PortfolioName = GetPortfolioName(),
				SecurityId = new()
				{
					SecurityCode = wallet.Currency
						.ToUpperInvariant(),
					BoardCode = BoardCodes.Quidax,
				},
				ServerTime = CurrentTime,
				OriginalTransactionId =
					originalTransactionId,
			}
			.TryAdd(
				PositionChangeTypes.CurrentValue,
				wallet.Available,
				true)
			.TryAdd(
				PositionChangeTypes.BlockedValue,
				wallet.Locked + wallet.Staked,
				true),
			cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(
		OrderStatusMessage statusMsg,
		int maximum,
		CancellationToken cancellationToken)
	{
		if (statusMsg.HasOrderId())
		{
			var order = await RestClient.GetOrderAsync(
				ResolveOrderId(
					statusMsg.OrderId,
					statusMsg.OrderStringId),
				cancellationToken);
			if (MatchesOrder(order, statusMsg))
				await SendOrderAndTradesAsync(
					order,
					statusMsg.TransactionId,
					false,
					cancellationToken);
			return;
		}

		var markets = GetStatusMarkets(statusMsg);
		var market = markets.Length == 1
			? markets[0].Id
			: null;
		var orders = new List<QuidaxOrder>();
		var states = statusMsg.IsHistoryOnly() ||
			statusMsg.From is not null ||
			statusMsg.To is not null
				? _allOrderStates
				: _activeOrderStates;

		foreach (var state in states)
			orders.AddRange(await RestClient.GetOrdersAsync(
				market,
				state,
				maximum,
				cancellationToken) ?? []);

		foreach (var order in orders
			.Where(order => MatchesOrder(order, statusMsg))
			.GroupBy(
				static order => order.Id,
				StringComparer.Ordinal)
			.Select(static group => group
				.OrderByDescending(GetOrderTimestamp)
				.First())
			.OrderBy(GetOrderTimestamp)
			.TakeLast(maximum))
			await SendOrderAndTradesAsync(
				order,
				statusMsg.TransactionId,
				false,
				cancellationToken);
	}

	private async ValueTask PollPrivateStateAsync(
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var orders = new List<QuidaxOrder>();

		foreach (var state in _activeOrderStates)
			orders.AddRange(await RestClient.GetOrdersAsync(
				null,
				state,
				100,
				cancellationToken) ?? []);

		foreach (var order in orders
			.GroupBy(
				static order => order.Id,
				StringComparer.Ordinal)
			.Select(static group => group.First())
			.OrderBy(GetOrderTimestamp))
			await SendOrderAndTradesAsync(
				order,
				originalTransactionId,
				true,
				cancellationToken);

		var trades = await RestClient.GetPrivateTradesAsync(
			null,
			100,
			cancellationToken);

		foreach (var trade in trades ?? [])
			await SendPrivateTradeAsync(
				trade,
				null,
				originalTransactionId,
				true,
				cancellationToken);
	}

	private async ValueTask SendOrderAndTradesAsync(
		QuidaxOrder order,
		long originalTransactionId,
		bool onlyNewTrades,
		CancellationToken cancellationToken)
	{
		await SendOrderAsync(
			order,
			originalTransactionId,
			cancellationToken);

		foreach (var trade in order?.Trades ?? [])
			await SendPrivateTradeAsync(
				trade,
				order,
				originalTransactionId,
				onlyNewTrades,
				cancellationToken);
	}

	private ValueTask SendOrderAsync(
		QuidaxOrder order,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (order?.Id.IsEmpty() != false)
			return default;
		var tracked = GetTrackedOrder(order.Id);
		var market = GetMarket(order.Market?.Id);
		if (market is null && tracked is not null)
			market = GetMarket(new SecurityId
			{
				SecurityCode = tracked.SecurityCode,
				BoardCode = BoardCodes.Quidax,
			});
		if (market is null)
			return default;

		tracked ??= new()
		{
			TransactionId = 0,
			SecurityCode = market.SecurityCode,
			Side = order.Side.ToSide(),
			OrderType = order.OrderType.ToOrderType(),
			Volume = order.OriginalVolume,
			Price = order.Price?.Amount ?? 0,
		};
		TrackOrder(order.Id, tracked);
		var execution = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = GetOrderTime(order),
			PortfolioName = GetPortfolioName(),
			Side = order.Side.ToSide(),
			OrderVolume = order.OriginalVolume > 0
				? order.OriginalVolume
				: tracked.Volume,
			Balance = order.RemainingVolume,
			OrderPrice = order.Price?.Amount ??
				tracked.Price,
			AveragePrice = order.AveragePrice?.Amount,
			OrderType = order.OrderType.IsEmpty()
				? tracked.OrderType
				: order.OrderType.ToOrderType(),
			OrderState = order.Status.ToOrderState(),
			OrderStringId = order.Id,
			UserOrderId = order.Reference,
			TransactionId = tracked.TransactionId,
			OriginalTransactionId = originalTransactionId,
			TimeInForce = TimeInForce.PutInQueue,
			PostOnly = false,
		};
		if (long.TryParse(
			order.Id,
			NumberStyles.None,
			CultureInfo.InvariantCulture,
			out var numericOrderId))
			execution.OrderId = numericOrderId;
		return SendOutMessageAsync(execution, cancellationToken);
	}

	private async ValueTask SendPrivateTradeAsync(
		QuidaxTrade trade,
		QuidaxOrder order,
		long originalTransactionId,
		bool onlyNew,
		CancellationToken cancellationToken)
	{
		var tradeId = trade?.EffectiveId;
		if (tradeId.IsEmpty())
			return;
		var marketId = order?.Market?.Id ??
			trade.Market;
		var market = GetMarket(marketId);
		if (market is null)
			return;
		var added = AddTrade(market.Id, tradeId, true);
		if (onlyNew && !added)
			return;
		var orderId = order?.Id ?? trade.OrderId;
		var tracked = GetTrackedOrder(orderId);
		var execution = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = market.ToStockSharp(),
			ServerTime = GetTradeTime(trade),
			PortfolioName = GetPortfolioName(),
			Side = (order?.Side ??
				trade.EffectiveSide).ToSide(),
			OrderStringId = orderId,
			TradeStringId = tradeId,
			TradePrice = trade.Price,
			TradeVolume = trade.EffectiveVolume.Abs(),
			TransactionId = tracked?.TransactionId ?? 0,
			OriginalTransactionId = originalTransactionId,
		};
		if (long.TryParse(
			orderId,
			NumberStyles.None,
			CultureInfo.InvariantCulture,
			out var numericOrderId))
			execution.OrderId = numericOrderId;
		if (long.TryParse(
			tradeId,
			NumberStyles.None,
			CultureInfo.InvariantCulture,
			out var numericTradeId))
			execution.TradeId = numericTradeId;
		await SendOutMessageAsync(execution, cancellationToken);
	}

	private QuidaxMarket[] GetStatusMarkets(
		OrderStatusMessage filter)
	{
		var ids = new List<SecurityId>();
		if (!filter.SecurityId.SecurityCode.IsEmpty())
			ids.Add(filter.SecurityId);
		ids.AddRange(filter.SecurityIds.Where(
			static id => !id.SecurityCode.IsEmpty()));
		return [.. ids.Select(GetMarket).Distinct()];
	}

	private bool MatchesOrder(
		QuidaxOrder order,
		OrderStatusMessage filter)
	{
		if (order?.Id.IsEmpty() != false)
			return false;
		var time = GetOrderTime(order);
		if (filter.From is DateTime from &&
			time < from.ToUniversalTime() ||
			filter.To is DateTime to &&
			time > to.ToUniversalTime())
			return false;
		if (filter.Side is Sides side &&
			order.Side.ToSide() != side)
			return false;
		var state = order.Status.ToOrderState();
		if (filter.States.Length > 0 &&
			!filter.States.Contains(state))
			return false;
		if (filter.Volume is decimal volume &&
			order.OriginalVolume != volume)
			return false;
		if (!filter.PortfolioName.IsEmpty() &&
			!filter.PortfolioName.EqualsIgnoreCase(
				GetPortfolioName()))
			return false;
		var markets = GetStatusMarkets(filter);
		return markets.Length == 0 ||
			markets.Any(market =>
				market.Id.EqualsIgnoreCase(order.Market?.Id));
	}

	private DateTime GetOrderTime(QuidaxOrder order)
		=> (order?.UpdatedAt ??
			order?.DoneAt ??
			order?.CreatedAt)?.ToUniversalTime() ??
			CurrentTime;

	private static DateTime GetOrderTimestamp(
		QuidaxOrder order)
		=> (order?.UpdatedAt ??
			order?.DoneAt ??
			order?.CreatedAt)?.ToUniversalTime() ??
			DateTime.MinValue;

	private async ValueTask CompleteOrderStatusAsync(
		OrderStatusMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(
			message, cancellationToken);
		await SendSubscriptionFinishedAsync(
			message.TransactionId,
			cancellationToken);
	}
}
