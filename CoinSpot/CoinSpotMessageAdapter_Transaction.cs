namespace StockSharp.CoinSpot;

public partial class CoinSpotMessageAdapter
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
				"CoinSpot order volume must be positive.");
		if (regMsg.VisibleVolume is > 0 &&
			regMsg.VisibleVolume != volume)
			throw new NotSupportedException(
				"CoinSpot does not document iceberg orders.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException(
				"CoinSpot does not document GTD orders.");
		if (regMsg.PostOnly == true)
			throw new NotSupportedException(
				"CoinSpot does not document post-only orders.");
		if (regMsg.TimeInForce is not null &&
			regMsg.TimeInForce != TimeInForce.PutInQueue)
			throw new NotSupportedException(
				"CoinSpot does not expose time-in-force options.");

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (
			OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(
					orderType, regMsg.TransactionId));
		if (orderType == OrderTypes.Limit &&
			regMsg.Price <= 0)
			throw new InvalidOperationException(
				"CoinSpot limit orders require a positive price.");

		var result = await RestClient.PlaceOrderAsync(
			new()
			{
				Coin = market.BaseUnit,
				Market = market.QuoteUnit,
				Side = regMsg.Side,
				OrderType = orderType,
				Amount = volume,
				Price = regMsg.Price,
			},
			cancellationToken);
		if (result?.Id.IsEmpty() != false)
			throw new InvalidDataException(
				"CoinSpot accepted an order without returning " +
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
			new()
			{
				Id = result.Id,
				Coin = market.BaseUnit,
				Market = market.SecurityCode,
				Amount = result.Amount > 0
					? result.Amount
					: volume,
				Rate = result.Rate > 0
					? result.Rate
					: regMsg.Price,
				CreatedAt = CurrentTime,
				Side = regMsg.Side,
				State = OrderStates.Active,
				OrderType = orderType,
			},
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
		var side = tracked?.Side;
		if (side is null)
			side = (await RestClient.GetOrderAsync(
				orderId, cancellationToken))?.Side;
		if (side is null)
			throw new InvalidOperationException(
				$"CoinSpot order '{orderId}' cannot be cancelled " +
					"because its side is unknown.");
		await RestClient.CancelOrderAsync(
			side.Value, orderId, cancellationToken);
		if (tracked is not null)
			await SendOrderAsync(
				new()
				{
					Id = orderId,
					Market = tracked.SecurityCode,
					Amount = tracked.Volume,
					Rate = tracked.Price,
					CompletedAt = CurrentTime,
					Side = tracked.Side,
					State = OrderStates.Done,
					OrderType = tracked.OrderType,
				},
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
			"CoinSpot does not provide an atomic order-replace " +
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
				"CoinSpot spot cancellation cannot close positions.");
		var coin = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(cancelMsg.SecurityId).BaseUnit;
		if (cancelMsg.Side is null or Sides.Buy)
			await RestClient.CancelAllAsync(
				Sides.Buy, coin, cancellationToken);
		if (cancelMsg.Side is null or Sides.Sell)
			await RestClient.CancelAllAsync(
				Sides.Sell, coin, cancellationToken);
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
					BoardCode = BoardCodes.CoinSpot,
					OriginalTransactionId =
						lookupMsg.TransactionId,
				},
				cancellationToken);
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

		await SendOrderSnapshotAsync(statusMsg, cancellationToken);
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
		foreach (var balance in await RestClient.GetBalancesAsync(
			cancellationToken) ?? [])
		{
			if (balance.Currency.IsEmpty())
				continue;
			await SendOutMessageAsync(
				new PositionChangeMessage
				{
					PortfolioName = GetPortfolioName(),
					SecurityId = new()
					{
						SecurityCode = balance.Currency,
						BoardCode = BoardCodes.CoinSpot,
					},
					ServerTime = CurrentTime,
					OriginalTransactionId =
						originalTransactionId,
				}
				.TryAdd(
					PositionChangeTypes.CurrentValue,
					balance.Balance,
					true)
				.TryAdd(
					PositionChangeTypes.BlockedValue,
					balance.Blocked,
					true),
				cancellationToken);
		}
	}

	private async ValueTask SendOrderSnapshotAsync(
		OrderStatusMessage statusMsg,
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
				await SendOrderAsync(
					order,
					statusMsg.TransactionId,
					cancellationToken);
			return;
		}

		var markets = GetStatusMarkets(statusMsg);
		var market = markets.Length == 1
			? markets[0]
			: null;
		var orders = new List<CoinSpotOrder>();
		orders.AddRange(await RestClient.GetOpenOrdersAsync(
			market?.BaseUnit,
			market?.QuoteUnit,
			cancellationToken) ?? []);
		if (statusMsg.IsHistoryOnly() ||
			statusMsg.From is not null ||
			statusMsg.To is not null)
			orders.AddRange(await RestClient.GetHistoryOrdersAsync(
				market?.BaseUnit,
				market?.QuoteUnit,
				statusMsg.From,
				statusMsg.To,
				(statusMsg.Count ?? 100).Min(500).Max(1).To<int>(),
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
			.TakeLast((statusMsg.Count ?? 100)
				.Min(500).Max(1).To<int>()))
			await SendOrderAsync(
				order,
				statusMsg.TransactionId,
				cancellationToken);
	}

	private async ValueTask PollPrivateStateAsync(
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		foreach (var order in await RestClient.GetOpenOrdersAsync(
			null, null, cancellationToken) ?? [])
			await SendOrderAsync(
				order,
				originalTransactionId,
				cancellationToken);
	}

	private ValueTask SendOrderAsync(
		CoinSpotOrder order,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (order?.Id.IsEmpty() != false)
			return default;
		var tracked = GetTrackedOrder(order.Id);
		var market = GetMarket(order.Market);
		if (market is null && !order.Coin.IsEmpty())
		{
			var quote = order.Market.IsEmpty()
				? "AUD"
				: order.Market;
			market = GetMarket(
				CoinSpotExtensions.CreateSecurityCode(
					order.Coin, quote));
		}
		if (market is null && tracked is not null)
			market = GetMarket(tracked.SecurityCode);
		if (market is null)
			return default;

		tracked ??= new()
		{
			SecurityCode = market.SecurityCode,
			Side = order.Side,
			OrderType = order.OrderType,
			Volume = order.Amount,
			Price = order.Rate,
		};
		TrackOrder(order.Id, tracked);
		var execution = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = GetOrderTime(order),
			PortfolioName = GetPortfolioName(),
			Side = order.Side,
			OrderVolume = order.Amount > 0
				? order.Amount
				: tracked.Volume,
			Balance = order.RemainingVolume,
			OrderPrice = order.Rate > 0
				? order.Rate
				: tracked.Price,
			OrderType = order.OrderType,
			OrderState = order.State,
			OrderStringId = order.Id,
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

	private CoinSpotMarket[] GetStatusMarkets(
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
		CoinSpotOrder order,
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
			order.Side != side)
			return false;
		if (filter.States.Length > 0 &&
			!filter.States.Contains(order.State))
			return false;
		if (filter.Volume is decimal volume &&
			order.Amount != volume)
			return false;
		if (!filter.PortfolioName.IsEmpty() &&
			!filter.PortfolioName.EqualsIgnoreCase(
				GetPortfolioName()))
			return false;
		var markets = GetStatusMarkets(filter);
		return markets.Length == 0 ||
			markets.Any(market =>
				market.SecurityCode.EqualsIgnoreCase(order.Market));
	}

	private DateTime GetOrderTime(CoinSpotOrder order)
		=> (order?.CompletedAt ??
			order?.CreatedAt)?.ToUniversalTime() ??
			CurrentTime;

	private static DateTime GetOrderTimestamp(
		CoinSpotOrder order)
		=> (order?.CompletedAt ??
			order?.CreatedAt)?.ToUniversalTime() ??
			DateTime.MinValue;

	private async ValueTask CompleteOrderStatusAsync(
		OrderStatusMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(
			message, cancellationToken);
		await SendSubscriptionFinishedAsync(
			message.TransactionId, cancellationToken);
	}
}
