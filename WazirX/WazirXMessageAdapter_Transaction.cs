namespace StockSharp.WazirX;

public partial class WazirXMessageAdapter
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
				"WazirX order volume must be positive.");
		if (market.MinimumVolume > 0 &&
			volume < market.MinimumVolume)
			throw new InvalidOperationException(
				$"WazirX order volume {volume} is below the " +
					$"{market.MinimumVolume} minimum.");
		if (market.MaximumVolume > 0 &&
			volume > market.MaximumVolume)
			throw new InvalidOperationException(
				$"WazirX order volume {volume} exceeds the " +
					$"{market.MaximumVolume} maximum.");
		if (regMsg.VisibleVolume is > 0 &&
			regMsg.VisibleVolume != volume)
			throw new NotSupportedException(
				"WazirX does not document iceberg orders.");
		if (regMsg.PostOnly == true)
			throw new NotSupportedException(
				"WazirX spot API does not document post-only " +
					"orders.");
		if (regMsg.TimeInForce is not (
			null or TimeInForce.PutInQueue))
			throw new NotSupportedException(
				"WazirX spot API supports GTC orders only.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException(
				"WazirX spot API does not document expiring " +
					"orders.");

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (
			OrderTypes.Limit or OrderTypes.Conditional))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(
					orderType, regMsg.TransactionId));
		if (regMsg.Price <= 0)
			throw new InvalidOperationException(
				"WazirX limit orders require a positive price.");
		decimal? stopPrice = null;
		if (orderType == OrderTypes.Conditional)
		{
			if (!market.SupportsStopLimit)
				throw new NotSupportedException(
					$"WazirX market '{market.Symbol}' does not " +
						"advertise stop-limit orders.");
			stopPrice = (
				regMsg.Condition as WazirXOrderCondition)
				?.StopPrice;
			if (stopPrice is not > 0)
				throw new InvalidOperationException(
					"WazirX stop-limit orders require a " +
						"positive trigger price.");
		}

		var clientOrderId = regMsg.UserOrderId.IsEmpty()
			? $"ss-{regMsg.TransactionId}"
			: regMsg.UserOrderId.Trim();
		if (clientOrderId.Length > 64)
			throw new InvalidOperationException(
				"WazirX client order ID cannot exceed 64 " +
					"characters.");
		var result = await RestClient.PlaceOrderAsync(
			market,
			regMsg.Side,
			orderType,
			volume,
			regMsg.Price,
			stopPrice,
			clientOrderId,
			cancellationToken);
		if (result?.Id is not > 0)
			throw new InvalidDataException(
				"WazirX accepted an order without returning " +
					"its identifier.");
		TrackOrder(result.Id, new()
		{
			TransactionId = regMsg.TransactionId,
			Symbol = market.Symbol,
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
		var (orderId, clientOrderId) = ResolveOrderIdentity(
			cancelMsg.OrderId,
			cancelMsg.OrderStringId);
		var market = ResolveOrderMarket(
			cancelMsg.SecurityId, orderId);
		var result = await RestClient.CancelOrderAsync(
			market.Symbol,
			orderId,
			clientOrderId,
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
			"WazirX does not provide an atomic spot " +
				"order-replace operation.");
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
				"WazirX spot cancellation cannot close " +
					"positions.");
		var market = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(cancelMsg.SecurityId);
		if (market is not null && cancelMsg.Side is null)
		{
			foreach (var order in
				await RestClient.CancelAllOrdersAsync(
					market.Symbol, cancellationToken) ?? [])
				await SendOrderAsync(
					order,
					cancelMsg.TransactionId,
					cancellationToken);

			return;
		}

		foreach (var order in
			await RestClient.GetOpenOrdersAsync(
				market?.Symbol,
				null,
				null,
				cancellationToken) ?? [])
		{
			if (cancelMsg.Side is Sides side &&
				order.Side != side)
				continue;
			var result = await RestClient.CancelOrderAsync(
				order.Symbol,
				order.Id,
				null,
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
			lookupMsg.TransactionId, cancellationToken);

		EnsurePrivateReady();
		const string stream = "outboundAccountPosition";
		if (!lookupMsg.IsSubscribe)
		{
			if (_portfolioSubscriptionId ==
				lookupMsg.OriginalTransactionId)
			{
				_portfolioSubscriptionId = 0;
				await ReleasePrivateStreamAsync(
					stream, cancellationToken);
			}
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
					BoardCode = BoardCodes.WazirX,
					OriginalTransactionId =
						lookupMsg.TransactionId,
				},
				cancellationToken);
			await SendPortfolioSnapshotAsync(
				lookupMsg.TransactionId, cancellationToken);
		}
		if (lookupMsg.IsHistoryOnly())
		{
			await CompletePortfolioLookupAsync(
				lookupMsg, cancellationToken);
			return;
		}

		if (_portfolioSubscriptionId != 0)
			throw new InvalidOperationException(
				"WazirX portfolio subscription already exists.");
		_portfolioSubscriptionId = lookupMsg.TransactionId;
		try
		{
			await AddPrivateStreamAsync(
				stream, cancellationToken);
			await SendSubscriptionResultAsync(
				lookupMsg, cancellationToken);
		}
		catch
		{
			_portfolioSubscriptionId = 0;
			throw;
		}
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
			{
				_orderStatusSubscriptionId = 0;
				await ReleasePrivateStreamAsync(
					"orderUpdate", cancellationToken);
				await ReleasePrivateStreamAsync(
					"ownTrade", cancellationToken);
			}
			return;
		}
		if (statusMsg.Count is <= 0)
		{
			await CompleteOrderStatusAsync(
				statusMsg, cancellationToken);
			return;
		}

		await SendOrderSnapshotAsync(
			statusMsg, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await CompleteOrderStatusAsync(
				statusMsg, cancellationToken);
			return;
		}

		if (_orderStatusSubscriptionId != 0)
			throw new InvalidOperationException(
				"WazirX order-status subscription already " +
					"exists.");
		_orderStatusSubscriptionId = statusMsg.TransactionId;
		try
		{
			await AddPrivateStreamAsync(
				"orderUpdate", cancellationToken);
			try
			{
				await AddPrivateStreamAsync(
					"ownTrade", cancellationToken);
			}
			catch
			{
				await ReleasePrivateStreamAsync(
					"orderUpdate", cancellationToken);
				throw;
			}
			await SendSubscriptionResultAsync(
				statusMsg, cancellationToken);
		}
		catch
		{
			_orderStatusSubscriptionId = 0;
			throw;
		}
	}

	private async ValueTask AddPrivateStreamAsync(
		string stream,
		CancellationToken cancellationToken)
	{
		await EnsureAuthKeyAsync(cancellationToken);
		var reference = "private:" + stream;
		var subscribe = AddReference(reference);
		try
		{
			if (subscribe)
				await WsClient.SubscribeAsync(
					stream, true, cancellationToken);
		}
		catch
		{
			ReleaseReference(reference);
			throw;
		}
	}

	private async ValueTask ReleasePrivateStreamAsync(
		string stream,
		CancellationToken cancellationToken)
	{
		var reference = "private:" + stream;
		if (ReleaseReference(reference))
			await WsClient.UnsubscribeAsync(
				stream, true, cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshotAsync(
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		foreach (var balance in
			await RestClient.GetBalancesAsync(
				cancellationToken) ?? [])
			await SendBalanceAsync(
				balance,
				originalTransactionId,
				cancellationToken);
	}

	private ValueTask SendBalanceAsync(
		WazirXBalance balance,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (balance?.Asset.IsEmpty() != false)
			return default;
		return SendOutMessageAsync(
			new PositionChangeMessage
			{
				PortfolioName = GetPortfolioName(),
				SecurityId = new()
				{
					SecurityCode = balance.Asset,
					BoardCode = BoardCodes.WazirX,
				},
				ServerTime = CurrentTime,
				OriginalTransactionId =
					originalTransactionId,
			}
			.TryAdd(
				PositionChangeTypes.CurrentValue,
				balance.Available,
				true)
			.TryAdd(
				PositionChangeTypes.BlockedValue,
				balance.Locked + balance.ReservedFee,
				true),
			cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(
		OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		var maximum = (statusMsg.Count ?? 1000)
			.Max(1).Min(1000).To<int>();
		long? requestedOrderId = null;
		if (statusMsg.HasOrderId())
		{
			var identity = ResolveOrderIdentity(
				statusMsg.OrderId,
				statusMsg.OrderStringId);
			requestedOrderId = identity.OrderId;
			var order = await RestClient.GetOrderAsync(
				identity.OrderId,
				identity.ClientOrderId,
				cancellationToken);
			if (MatchesOrder(order, statusMsg))
				await SendOrderAsync(
					order,
					statusMsg.TransactionId,
					cancellationToken);
		}
		else
		{
			var orders = new Dictionary<long, WazirXOrder>();

			foreach (var order in
				await RestClient.GetOpenOrdersAsync(
					null,
					statusMsg.From?.ToUniversalTime(),
					statusMsg.To?.ToUniversalTime(),
					cancellationToken) ?? [])
				orders[order.Id] = order;

			foreach (var market in GetStatusMarkets(statusMsg))
			{
				foreach (var order in
					await RestClient.GetAllOrdersAsync(
						market.Symbol,
						statusMsg.From?.ToUniversalTime(),
						statusMsg.To?.ToUniversalTime(),
						maximum,
						cancellationToken) ?? [])
					orders[order.Id] = order;
			}

			foreach (var order in orders.Values
				.Where(order => MatchesOrder(order, statusMsg))
				.OrderBy(static order => order.CreatedAt)
				.TakeLast(maximum))
				await SendOrderAsync(
					order,
					statusMsg.TransactionId,
					cancellationToken);
		}

		foreach (var trade in
			await RestClient.GetUserTradesAsync(
				GetStatusMarkets(statusMsg)
					.FirstOrDefault()?.Symbol,
				requestedOrderId,
				requestedOrderId is null ? 1 : null,
				statusMsg.From?.ToUniversalTime(),
				statusMsg.To?.ToUniversalTime(),
				maximum,
				cancellationToken) ?? [])
		{
			if (MatchesTrade(trade, statusMsg))
				await SendUserTradeAsync(
					trade,
					statusMsg.TransactionId,
					cancellationToken);
		}
	}

	private async ValueTask PollOrdersAsync(
		long originalTransactionId,
		DateTime from,
		CancellationToken cancellationToken)
	{
		DateTime? since = from == default
			? null
			: from.ToUniversalTime();

		foreach (var order in
			await RestClient.GetOpenOrdersAsync(
				null, since, null, cancellationToken) ?? [])
			await SendOrderAsync(
				order,
				originalTransactionId,
				cancellationToken);

		foreach (var trade in
			await RestClient.GetUserTradesAsync(
				null,
				null,
				_lastPrivateTradeId > 0
					? _lastPrivateTradeId + 1
					: 1,
				since,
				null,
				1000,
				cancellationToken) ?? [])
			await SendUserTradeAsync(
				trade,
				originalTransactionId,
				cancellationToken);
	}

	private ValueTask SendOrderAsync(
		WazirXOrder order,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (order?.Id is not > 0 || order.Symbol.IsEmpty())
			return default;
		var market = GetMarket(order.Symbol);
		if (market is null)
			return default;
		var tracked = GetTrackedOrder(order.Id);
		tracked ??= new()
		{
			TransactionId = ParseTransactionId(
				order.ClientOrderId),
			Symbol = market.Symbol,
			Side = order.Side,
			OrderType = order.OrderType,
			Volume = order.OriginalVolume,
			Price = order.Price,
		};
		TrackOrder(order.Id, tracked);
		return SendOutMessageAsync(
			new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = market.ToStockSharp(),
				ServerTime = order.UpdatedAt == default
					? order.CreatedAt == default
						? CurrentTime
						: order.CreatedAt
					: order.UpdatedAt,
				PortfolioName = GetPortfolioName(),
				Side = order.Side,
				OrderVolume = order.OriginalVolume > 0
					? order.OriginalVolume
					: tracked.Volume,
				Balance = order.RemainingVolume,
				OrderPrice = order.Price > 0
					? order.Price
					: tracked.Price,
				OrderType = order.OrderType,
				OrderState = order.State == OrderStates.None
					? OrderStates.Active
					: order.State,
				OrderId = order.Id,
				OrderStringId = order.Id.ToString(
					CultureInfo.InvariantCulture),
				TransactionId = tracked.TransactionId,
				OriginalTransactionId =
					originalTransactionId,
				TimeInForce = TimeInForce.PutInQueue,
				Condition = order.OrderType ==
					OrderTypes.Conditional
						? new WazirXOrderCondition
						{
							StopPrice = order.StopPrice,
						}
						: null,
			},
			cancellationToken);
	}

	private ValueTask SendUserTradeAsync(
		WazirXUserTrade trade,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (trade?.Id is not > 0 ||
			trade.OrderId <= 0 ||
			trade.Symbol.IsEmpty() ||
			!AddTrade("private", trade.Id))
			return default;
		_lastPrivateTradeId = Math.Max(
			_lastPrivateTradeId, trade.Id);
		var market = GetMarket(trade.Symbol);
		if (market is null)
			return default;
		var tracked = GetTrackedOrder(trade.OrderId);
		return SendOutMessageAsync(
			new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				SecurityId = market.ToStockSharp(),
				ServerTime = trade.Time == default
					? CurrentTime
					: trade.Time,
				PortfolioName = GetPortfolioName(),
				Side = trade.Side,
				OrderId = trade.OrderId,
				OrderStringId = trade.OrderId.ToString(
					CultureInfo.InvariantCulture),
				TradeId = trade.Id,
				TradePrice = trade.Price,
				TradeVolume = trade.Volume.Abs(),
				TransactionId = tracked?.TransactionId ??
					ParseTransactionId(trade.ClientOrderId),
				OriginalTransactionId =
					originalTransactionId,
				Commission = trade.Fee > 0
					? trade.Fee
					: null,
				CommissionCurrency = trade.FeeCurrency,
			},
			cancellationToken);
	}

	private bool MatchesOrder(
		WazirXOrder order,
		OrderStatusMessage filter)
	{
		if (order?.Id is not > 0)
			return false;
		if (filter.From is DateTime from &&
			order.CreatedAt < from.ToUniversalTime() ||
			filter.To is DateTime to &&
			order.CreatedAt > to.ToUniversalTime())
			return false;
		if (filter.Side is Sides side &&
			order.Side != side)
			return false;
		if (filter.States.Length > 0 &&
			!filter.States.Contains(order.State))
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
				market.Symbol.EqualsIgnoreCase(order.Symbol));
	}

	private bool MatchesTrade(
		WazirXUserTrade trade,
		OrderStatusMessage filter)
	{
		if (trade is null)
			return false;
		if (filter.From is DateTime from &&
			trade.Time < from.ToUniversalTime() ||
			filter.To is DateTime to &&
			trade.Time > to.ToUniversalTime())
			return false;
		if (filter.Side is Sides side &&
			trade.Side != side)
			return false;
		var markets = GetStatusMarkets(filter);
		return markets.Length == 0 ||
			markets.Any(market =>
				market.Symbol.EqualsIgnoreCase(trade.Symbol));
	}

	private WazirXMarket[] GetStatusMarkets(
		OrderStatusMessage filter)
	{
		var ids = new List<SecurityId>();
		if (!filter.SecurityId.SecurityCode.IsEmpty())
			ids.Add(filter.SecurityId);
		ids.AddRange(filter.SecurityIds.Where(
			static id => !id.SecurityCode.IsEmpty()));
		return [.. ids.Select(GetMarket).Distinct()];
	}

	private WazirXMarket ResolveOrderMarket(
		SecurityId securityId,
		long? orderId)
	{
		if (!securityId.SecurityCode.IsEmpty())
			return GetMarket(securityId);
		if (orderId is > 0)
		{
			var tracked = GetTrackedOrder(orderId.Value);
			if (tracked is not null)
				return GetMarket(tracked.Symbol);
		}
		throw new InvalidOperationException(
			"WazirX cancellation requires the market symbol " +
				"when the order was not registered by this " +
				"adapter instance.");
	}

	private static (
		long? OrderId,
		string ClientOrderId) ResolveOrderIdentity(
			long? numericOrderId,
			string stringOrderId)
	{
		if (numericOrderId is > 0)
			return (numericOrderId, null);
		if (stringOrderId.IsEmpty())
			throw new InvalidOperationException(
				"WazirX operation requires an exchange or " +
					"client order ID.");
		stringOrderId = stringOrderId.Trim();
		return long.TryParse(
			stringOrderId,
			NumberStyles.None,
			CultureInfo.InvariantCulture,
			out var parsed)
				? (parsed, null)
				: (null, stringOrderId);
	}

	private static long ParseTransactionId(
		string clientOrderId)
		=> clientOrderId?.StartsWithIgnoreCase("ss-") == true &&
			long.TryParse(
				clientOrderId[3..],
				NumberStyles.None,
				CultureInfo.InvariantCulture,
				out var value)
					? value
					: 0;

	private async ValueTask CompletePortfolioLookupAsync(
		PortfolioLookupMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(
			message, cancellationToken);
		await SendSubscriptionFinishedAsync(
			message.TransactionId, cancellationToken);
	}

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
