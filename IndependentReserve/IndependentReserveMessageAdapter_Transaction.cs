namespace StockSharp.IndependentReserve;

public partial class IndependentReserveMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		ValidatePortfolio(regMsg.PortfolioName);
		var market = GetMarket(regMsg.SecurityId);
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType,
					regMsg.TransactionId));
		var volume = regMsg.Volume.Abs();
		ValidateVolume(volume, market);
		if (regMsg.VisibleVolume is > 0 && regMsg.VisibleVolume != volume)
			throw new NotSupportedException(
				"Independent Reserve does not document iceberg orders.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException(
				"Independent Reserve does not support GTD orders.");

		var condition = regMsg.Condition as IndependentReserveOrderCondition ??
			new();
		if (condition.AllowedSlippagePercent is < 0 or > 100)
			throw new InvalidOperationException(
				"Independent Reserve slippage must be between 0 and 100 percent.");
		if (orderType == OrderTypes.Limit)
		{
			ValidatePrice(regMsg.Price, market);
			if (condition.IsVolumeInQuoteCurrency ||
				condition.AllowedSlippagePercent is not null)
				throw new InvalidOperationException(
					"Independent Reserve quote-volume and slippage parameters apply to market orders only.");
		}
		else
		{
			if (regMsg.PostOnly == true)
				throw new InvalidOperationException(
					"A market order cannot be post-only.");
			if (regMsg.TimeInForce is TimeInForce.CancelBalance or
				TimeInForce.MatchOrCancel)
				throw new NotSupportedException(
					"Independent Reserve market orders do not accept time-in-force.");
		}

		var clientOrderId = CreateClientOrderId(regMsg.TransactionId,
			regMsg.UserOrderId);
		var tracked = new TrackedOrder
		{
			TransactionId = regMsg.TransactionId,
			Symbol = market.Symbol,
			ClientOrderId = clientOrderId,
			Side = regMsg.Side,
			OrderType = orderType,
			Volume = volume,
			Price = regMsg.Price,
			IsPostOnly = regMsg.PostOnly == true,
			TimeInForce = regMsg.TimeInForce,
			Condition = condition.Clone() as IndependentReserveOrderCondition,
		};
		TrackOrder(tracked, clientOrderId,
			regMsg.TransactionId.ToString(CultureInfo.InvariantCulture));

		IndependentReserveOrder result;
		if (orderType == OrderTypes.Limit)
		{
			result = await RestClient.PlaceLimitOrderAsync(new()
			{
				PrimaryCurrencyCode = market.PrimaryCurrency,
				SecondaryCurrencyCode = market.SecondaryCurrency,
				OrderType = regMsg.Side.ToIndependentReserve(false),
				Price = regMsg.Price,
				Volume = volume,
				ClientId = clientOrderId,
				TimeInForce = regMsg.TimeInForce.ToIndependentReserve(
					regMsg.PostOnly == true),
			}, cancellationToken);
		}
		else
		{
			result = await RestClient.PlaceMarketOrderAsync(new()
			{
				PrimaryCurrencyCode = market.PrimaryCurrency,
				SecondaryCurrencyCode = market.SecondaryCurrency,
				OrderType = regMsg.Side.ToIndependentReserve(true),
				Volume = volume,
				ClientId = clientOrderId,
				AllowedSlippagePercent = condition.AllowedSlippagePercent,
				VolumeCurrencyType = condition.IsVolumeInQuoteCurrency
					? IndependentReserveVolumeCurrencyTypes.Secondary
					: IndependentReserveVolumeCurrencyTypes.Primary,
			}, cancellationToken);
		}

		if (result is null || result.OrderGuid == Guid.Empty)
			throw new InvalidDataException(
				"Independent Reserve accepted an order without returning its identifier.");
		tracked.ExchangeOrderId = result.OrderGuid.ToString("D");
		TrackOrder(tracked, tracked.ExchangeOrderId, clientOrderId);
		await SendOrderAsync(result, regMsg.TransactionId, true,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		var identifier = ResolveOrderIdentifier(cancelMsg.OrderId,
			cancelMsg.OrderStringId, "cancellation");
		var tracked = GetTrackedOrder(identifier);
		var orderId = ParseOrderGuid(tracked?.ExchangeOrderId ?? identifier,
			"cancellation");
		var result = await RestClient.CancelOrderAsync(new()
		{
			OrderGuid = orderId.ToString("D"),
		}, cancellationToken);
		await SendOrderAsync(result, cancelMsg.TransactionId, true,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"Independent Reserve spot cancellation does not close positions.");
		if (cancelMsg.IsStop == true)
			throw new NotSupportedException(
				"Independent Reserve does not expose stop orders through this API.");
		var market = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(cancelMsg.SecurityId);

		for (var iteration = 0; iteration < 100; iteration++)
		{
			var page = await RestClient.GetOpenOrdersAsync(new()
			{
				PrimaryCurrencyCode = market?.PrimaryCurrency,
				SecondaryCurrencyCode = market?.SecondaryCurrency,
				PageIndex = 1,
				PageSize = 100,
			}, cancellationToken);
			var orders = (page?.Data ?? []).Where(order => order is not null &&
				(cancelMsg.Side is null || order.OrderType.ToStockSharp() ==
					cancelMsg.Side)).ToArray();
			if (orders.Length == 0)
				break;
			foreach (var order in orders)
			{
				var canceled = await RestClient.CancelOrderAsync(new()
				{
					OrderGuid = order.OrderGuid.ToString("D"),
				}, cancellationToken);
				await SendOrderAsync(canceled, cancelMsg.TransactionId, true,
					cancellationToken);
			}
			if ((page?.Data?.Length ?? 0) < 100)
				break;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsurePrivateReady();
		if (!lookupMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_portfolioSubscriptions.Remove(lookupMsg.OriginalTransactionId);
			return;
		}
		ValidatePortfolio(lookupMsg.PortfolioName);
		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = GetPortfolioName(),
			BoardCode = BoardCodes.IndependentReserve,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId, true,
			cancellationToken);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_portfolioSubscriptions.Add(lookupMsg.TransactionId);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId,
			cancellationToken);
		EnsurePrivateReady();
		if (!statusMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_orderSubscriptions.Remove(statusMsg.OriginalTransactionId);
			return;
		}
		if (statusMsg.Count is <= 0)
		{
			await CompleteOrderStatusAsync(statusMsg, cancellationToken);
			return;
		}
		ValidatePortfolio(statusMsg.PortfolioName);
		var symbol = statusMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(statusMsg.SecurityId).Symbol;
		string orderId = null;
		if (statusMsg.HasOrderId())
			orderId = ResolveOrderIdentifier(statusMsg.OrderId,
				statusMsg.OrderStringId, "lookup");
		symbol ??= GetTrackedOrder(orderId)?.Symbol;
		var maximum = (statusMsg.Count ?? 100).Min(5000).Max(1).To<int>();
		await SendOrderSnapshotAsync(statusMsg.TransactionId, symbol, orderId,
			statusMsg.Side, statusMsg.From, statusMsg.To, maximum, true,
			cancellationToken);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_orderSubscriptions[statusMsg.TransactionId] = new()
			{
				Symbol = symbol,
				OrderId = orderId,
				Side = statusMsg.Side,
			};
	}

	private async ValueTask SendOrderSnapshotAsync(long transactionId,
		string symbol, string orderId, Sides? side, DateTime? from, DateTime? to,
		int maximum, bool force, CancellationToken cancellationToken)
	{
		if (!orderId.IsEmpty())
		{
			var lookup = CreateOrderLookup(orderId);
			var order = await RestClient.GetOrderAsync(lookup, cancellationToken);
			if (MatchesOrder(symbol, side, from, to, order))
				await SendOrderAsync(order, transactionId, force,
					cancellationToken);
			var trades = await RestClient.GetTradesByOrderAsync(new()
			{
				OrderGuid = lookup.OrderGuid,
				ClientId = lookup.ClientId,
				PageIndex = 1,
				PageSize = maximum.Min(50),
			}, cancellationToken);
			foreach (var trade in trades?.Data ?? [])
				if (MatchesTrade(symbol, side, from, to, trade))
					await SendUserTradeAsync(trade, transactionId,
						cancellationToken);
			return;
		}

		MarketDefinition market = null;
		if (!symbol.IsEmpty())
			market = GetMarket(symbol);
		var sent = 0;
		var openPageSize = maximum.Min(100);
		for (var pageIndex = 1; sent < maximum; pageIndex++)
		{
			var page = await RestClient.GetOpenOrdersAsync(new()
			{
				PrimaryCurrencyCode = market?.PrimaryCurrency,
				SecondaryCurrencyCode = market?.SecondaryCurrency,
				PageIndex = pageIndex,
				PageSize = openPageSize,
			}, cancellationToken);
			foreach (var order in page?.Data ?? [])
			{
				if (!MatchesOrder(symbol, side, from, to, order))
					continue;
				await SendOrderAsync(order, transactionId, force,
					cancellationToken);
				if (++sent >= maximum)
					break;
			}
			if ((page?.Data?.Length ?? 0) < openPageSize)
				break;
		}

		var closedPageSize = (maximum - sent).Min(5000).Max(1);
		for (var pageIndex = 1; sent < maximum; pageIndex++)
		{
			var page = await RestClient.GetClosedOrdersAsync(new()
			{
				PrimaryCurrencyCode = market?.PrimaryCurrency,
				SecondaryCurrencyCode = market?.SecondaryCurrency,
				PageIndex = pageIndex,
				PageSize = closedPageSize,
				IsIncludeTotals = false,
				FromTimestampUtc = from?.ToUniversalTime().ToApiTime(),
			}, cancellationToken);
			foreach (var order in page?.Data ?? [])
			{
				if (!MatchesOrder(symbol, side, from, to, order))
					continue;
				await SendOrderAsync(order, transactionId, force,
					cancellationToken);
				if (++sent >= maximum)
					break;
			}
			if ((page?.Data?.Length ?? 0) < closedPageSize)
				break;
		}

		var tradesSent = 0;
		for (var pageIndex = 1; tradesSent < maximum; pageIndex++)
		{
			var page = await RestClient.GetTradesAsync(new()
			{
				PageIndex = pageIndex,
				PageSize = (maximum - tradesSent).Min(50),
				FromTimestampUtc = from?.ToUniversalTime().ToApiTime(),
				ToTimestampUtc = to?.ToUniversalTime().ToApiTime(),
				IsIncludeTotals = false,
			}, cancellationToken);
			foreach (var trade in page?.Data ?? [])
			{
				if (!MatchesTrade(symbol, side, from, to, trade))
					continue;
				await SendUserTradeAsync(trade, transactionId,
					cancellationToken);
				tradesSent++;
			}
			if ((page?.Data?.Length ?? 0) < 50)
				break;
		}
	}

	private async ValueTask SendPortfolioSnapshotAsync(long transactionId,
		bool force, CancellationToken cancellationToken)
	{
		var accounts = await RestClient.GetAccountsAsync(cancellationToken);
		foreach (var account in accounts ?? [])
			await SendBalanceAsync(account, transactionId, force,
				cancellationToken);
	}

	private async ValueTask RefreshPrivateSnapshotsAsync(
		CancellationToken cancellationToken)
	{
		if (!await _privateRefreshGate.WaitAsync(0, cancellationToken))
			return;
		try
		{
			long[] portfolioSubscriptions;
			KeyValuePair<long, OrderSubscription>[] orderSubscriptions;
			using (_sync.EnterScope())
			{
				portfolioSubscriptions = [.. _portfolioSubscriptions];
				orderSubscriptions = [.. _orderSubscriptions];
			}
			foreach (var subscription in portfolioSubscriptions)
				await SendPortfolioSnapshotAsync(subscription, false,
					cancellationToken);
			foreach (var subscription in orderSubscriptions)
				await SendOrderSnapshotAsync(subscription.Key,
					subscription.Value.Symbol, subscription.Value.OrderId,
					subscription.Value.Side, null, null, 100, false,
					cancellationToken);
		}
		finally
		{
			_privateRefreshGate.Release();
		}
	}

	private ValueTask SendBalanceAsync(IndependentReserveAccount account,
		long transactionId, bool force, CancellationToken cancellationToken)
	{
		if (account?.CurrencyCode.IsEmpty() != false ||
			account.Status != IndependentReserveAccountStatuses.Active)
			return default;
		var fingerprint = new BalanceFingerprint(account.TotalBalance,
			account.AvailableBalance);
		var key = $"{transactionId}:{account.CurrencyCode}";
		using (_sync.EnterScope())
		{
			if (!force && _balanceFingerprints.TryGetValue(key, out var previous) &&
				previous == fingerprint)
				return default;
			_balanceFingerprints[key] = fingerprint;
		}
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(),
			SecurityId = new SecurityId
			{
				SecurityCode = account.CurrencyCode.ToUpperInvariant(),
				BoardCode = BoardCodes.IndependentReserve,
			},
			ServerTime = CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, account.TotalBalance, true)
		.TryAdd(PositionChangeTypes.BlockedValue,
			(account.TotalBalance - account.AvailableBalance).Max(0m), true),
			cancellationToken);
	}

	private ValueTask SendOrderAsync(IndependentReserveOrder order,
		long transactionId, bool force, CancellationToken cancellationToken)
	{
		if (order is null || order.OrderGuid == Guid.Empty ||
			order.PrimaryCurrencyCode.IsEmpty() ||
			order.SecondaryCurrencyCode.IsEmpty())
			return default;
		var orderId = order.OrderGuid.ToString("D");
		var fingerprint = new OrderFingerprint(order.Status, order.VolumeFilled,
			order.VolumeOrdered, order.AveragePrice);
		if (!ShouldSendOrder(orderId, transactionId, fingerprint, force))
			return default;
		var tracked = GetTrackedOrder(orderId) ?? GetTrackedOrder(order.ClientId) ??
			CreateTrackedOrder(order);
		tracked.ExchangeOrderId = orderId;
		TrackOrder(tracked, orderId, order.ClientId);
		var volume = order.VolumeOrdered > 0
			? order.VolumeOrdered
			: tracked.Volume;
		var balance = order.Status.ToStockSharp() == OrderStates.Active
			? (volume - order.VolumeFilled).Max(0m)
			: 0m;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = IndependentReserveExtensions.ToStockSharp(
				order.PrimaryCurrencyCode, order.SecondaryCurrencyCode),
			ServerTime = order.CreatedTimestampUtc.EnsureUtc(),
			PortfolioName = GetPortfolioName(),
			Side = order.Type.ToStockSharp(),
			OrderVolume = volume,
			Balance = balance,
			OrderPrice = order.Price ?? tracked.Price,
			AveragePrice = order.AveragePrice,
			OrderType = order.Type.ToStockSharpOrderType(),
			OrderState = order.Status.ToStockSharp(),
			OrderStringId = orderId,
			UserOrderId = order.ClientId.IsEmpty()
				? tracked.ClientOrderId
				: order.ClientId,
			TransactionId = tracked.TransactionId,
			OriginalTransactionId = transactionId,
			PostOnly = tracked.IsPostOnly,
			TimeInForce = tracked.TimeInForce,
			Condition = tracked.Condition,
		}, cancellationToken);
	}

	private ValueTask SendOrderAsync(IndependentReserveHistoryOrder order,
		long transactionId, bool force, CancellationToken cancellationToken)
	{
		if (order is null || order.OrderGuid == Guid.Empty ||
			order.PrimaryCurrencyCode.IsEmpty() ||
			order.SecondaryCurrencyCode.IsEmpty())
			return default;
		var orderId = order.OrderGuid.ToString("D");
		var volume = order.Original?.Volume > 0
			? order.Original.Volume
			: order.Volume;
		var outstanding = order.Original?.Outstanding ?? order.Outstanding ??
			(order.Status.ToStockSharp() == OrderStates.Active ? volume : 0m);
		var filled = (volume - outstanding).Max(0m);
		var fingerprint = new OrderFingerprint(order.Status, filled, volume,
			order.AveragePrice);
		if (!ShouldSendOrder(orderId, transactionId, fingerprint, force))
			return default;
		var tracked = GetTrackedOrder(orderId) ?? GetTrackedOrder(order.ClientId) ??
			CreateTrackedOrder(order, volume);
		tracked.ExchangeOrderId = orderId;
		TrackOrder(tracked, orderId, order.ClientId);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = IndependentReserveExtensions.ToStockSharp(
				order.PrimaryCurrencyCode, order.SecondaryCurrencyCode),
			ServerTime = order.CreatedTimestampUtc.EnsureUtc(),
			PortfolioName = GetPortfolioName(),
			Side = order.OrderType.ToStockSharp(),
			OrderVolume = volume,
			Balance = order.Status.ToStockSharp() == OrderStates.Active
				? outstanding.Max(0m)
				: 0m,
			OrderPrice = order.Price ?? tracked.Price,
			AveragePrice = order.AveragePrice,
			OrderType = order.OrderType.ToStockSharpOrderType(),
			OrderState = order.Status.ToStockSharp(),
			OrderStringId = orderId,
			UserOrderId = order.ClientId.IsEmpty()
				? tracked.ClientOrderId
				: order.ClientId,
			TransactionId = tracked.TransactionId,
			OriginalTransactionId = transactionId,
			PostOnly = order.TimeInForce == IndependentReserveTimeInForce.Moc,
			TimeInForce = order.TimeInForce?.ToStockSharp(),
			Condition = CreateCondition(order.Original?.VolumeCurrencyType ??
				IndependentReserveVolumeCurrencyTypes.Primary),
		}, cancellationToken);
	}

	private bool ShouldSendOrder(string orderId, long transactionId,
		OrderFingerprint fingerprint, bool force)
	{
		var key = $"{transactionId}:{orderId}";
		using (_sync.EnterScope())
		{
			if (!force && _orderFingerprints.TryGetValue(key, out var previous) &&
				previous == fingerprint)
				return false;
			_orderFingerprints[key] = fingerprint;
			return true;
		}
	}

	private TrackedOrder CreateTrackedOrder(IndependentReserveOrder order)
		=> new()
		{
			TransactionId = ParseTransactionId(order.ClientId),
			Symbol = IndependentReserveExtensions.ToSymbol(
				order.PrimaryCurrencyCode, order.SecondaryCurrencyCode),
			ExchangeOrderId = order.OrderGuid.ToString("D"),
			ClientOrderId = order.ClientId,
			Side = order.Type.ToStockSharp(),
			OrderType = order.Type.ToStockSharpOrderType(),
			Volume = order.VolumeOrdered,
			Price = order.Price ?? 0m,
			Condition = CreateCondition(order.VolumeCurrencyType),
		};

	private TrackedOrder CreateTrackedOrder(
		IndependentReserveHistoryOrder order, decimal volume)
		=> new()
		{
			TransactionId = ParseTransactionId(order.ClientId),
			Symbol = IndependentReserveExtensions.ToSymbol(
				order.PrimaryCurrencyCode, order.SecondaryCurrencyCode),
			ExchangeOrderId = order.OrderGuid.ToString("D"),
			ClientOrderId = order.ClientId,
			Side = order.OrderType.ToStockSharp(),
			OrderType = order.OrderType.ToStockSharpOrderType(),
			Volume = volume,
			Price = order.Price ?? 0m,
			IsPostOnly = order.TimeInForce == IndependentReserveTimeInForce.Moc,
			TimeInForce = order.TimeInForce?.ToStockSharp(),
			Condition = CreateCondition(order.Original?.VolumeCurrencyType ??
				IndependentReserveVolumeCurrencyTypes.Primary),
		};

	private ValueTask SendUserTradeAsync(IndependentReserveUserTrade trade,
		long transactionId, CancellationToken cancellationToken)
	{
		if (trade is null || trade.TradeGuid == Guid.Empty ||
			trade.OrderGuid == Guid.Empty || trade.Price <= 0 || trade.Volume <= 0 ||
			trade.PrimaryCurrencyCode.IsEmpty() ||
			trade.SecondaryCurrencyCode.IsEmpty() ||
			!AddAccountTrade(trade.TradeGuid, transactionId))
			return default;
		var orderId = trade.OrderGuid.ToString("D");
		var tracked = GetTrackedOrder(orderId);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = IndependentReserveExtensions.ToStockSharp(
				trade.PrimaryCurrencyCode, trade.SecondaryCurrencyCode),
			ServerTime = trade.TradeTimestampUtc.EnsureUtc(),
			PortfolioName = GetPortfolioName(),
			Side = trade.OrderType.ToStockSharp(),
			OrderStringId = orderId,
			TradeStringId = trade.TradeGuid.ToString("D"),
			TradePrice = trade.Price,
			TradeVolume = trade.Volume,
			TransactionId = tracked?.TransactionId ?? 0,
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}

	private async ValueTask OnOwnSocketEventAsync(
		IndependentReserveSocketEnvelope envelope,
		CancellationToken cancellationToken)
	{
		var payload = envelope.Data?.Payload;
		if (payload is null || envelope.Event is not (
			IndependentReserveSocketEvents.NewOrder or
			IndependentReserveSocketEvents.OrderChanged or
			IndependentReserveSocketEvents.OrderCanceled or
			IndependentReserveSocketEvents.Trade))
			return;

		var trackedOrders = new HashSet<TrackedOrder>();
		foreach (var identifier in GetSocketOrderIdentifiers(payload))
		{
			var tracked = GetTrackedOrder(identifier);
			if (tracked is not null)
				trackedOrders.Add(tracked);
		}
		foreach (var tracked in trackedOrders)
		{
			try
			{
				var lookup = CreateOrderLookup(tracked.ExchangeOrderId.IsEmpty()
					? tracked.ClientOrderId
					: tracked.ExchangeOrderId);
				var order = await RestClient.GetOrderAsync(lookup,
					cancellationToken);
				KeyValuePair<long, OrderSubscription>[] subscriptions;
				using (_sync.EnterScope())
					subscriptions = [.. _orderSubscriptions.Where(pair =>
						MatchesOrderSubscription(pair.Value, tracked.Symbol,
							order.OrderGuid.ToString("D"), tracked.ClientOrderId,
							order.Type.ToStockSharp()))];
				foreach (var subscription in subscriptions)
				{
					await SendOrderAsync(order, subscription.Key, false,
						cancellationToken);
					if (envelope.Event != IndependentReserveSocketEvents.Trade)
						continue;
					var trades = await RestClient.GetTradesByOrderAsync(new()
					{
						OrderGuid = order.OrderGuid.ToString("D"),
						PageIndex = 1,
						PageSize = 50,
					}, cancellationToken);
					foreach (var trade in trades?.Data ?? [])
						await SendUserTradeAsync(trade, subscription.Key,
							cancellationToken);
				}
			}
			catch (Exception error)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
		}
	}

	private static IEnumerable<string> GetSocketOrderIdentifiers(
		IndependentReserveSocketPayload payload)
	{
		if (payload.OrderGuid is Guid orderId && orderId != Guid.Empty)
			yield return orderId.ToString("D");
		if (payload.BidGuid is Guid bidId && bidId != Guid.Empty)
			yield return bidId.ToString("D");
		if (payload.OfferGuid is Guid offerId && offerId != Guid.Empty)
			yield return offerId.ToString("D");
		if (!payload.ClientId.IsEmpty())
			yield return payload.ClientId;
		if (!payload.BidClientId.IsEmpty())
			yield return payload.BidClientId;
		if (!payload.OfferClientId.IsEmpty())
			yield return payload.OfferClientId;
	}

	private static IndependentReserveOrderLookupRequest CreateOrderLookup(
		string identifier)
	{
		identifier = identifier.ThrowIfEmpty(nameof(identifier)).Trim();
		return Guid.TryParse(identifier, out var orderId)
			? new() { OrderGuid = orderId.ToString("D") }
			: new() { ClientId = identifier };
	}

	private static bool MatchesOrder(string symbol, Sides? side, DateTime? from,
		DateTime? to, IndependentReserveOrder order)
		=> order is not null &&
			(symbol.IsEmpty() || symbol.EqualsIgnoreCase(
				IndependentReserveExtensions.ToSymbol(order.PrimaryCurrencyCode,
					order.SecondaryCurrencyCode))) &&
			(side is null || order.Type.ToStockSharp() == side) &&
			IsInRange(order.CreatedTimestampUtc, from, to);

	private static bool MatchesOrder(string symbol, Sides? side, DateTime? from,
		DateTime? to, IndependentReserveHistoryOrder order)
		=> order is not null &&
			(symbol.IsEmpty() || symbol.EqualsIgnoreCase(
				IndependentReserveExtensions.ToSymbol(order.PrimaryCurrencyCode,
					order.SecondaryCurrencyCode))) &&
			(side is null || order.OrderType.ToStockSharp() == side) &&
			IsInRange(order.CreatedTimestampUtc, from, to);

	private static bool MatchesTrade(string symbol, Sides? side, DateTime? from,
		DateTime? to, IndependentReserveUserTrade trade)
		=> trade is not null &&
			(symbol.IsEmpty() || symbol.EqualsIgnoreCase(
				IndependentReserveExtensions.ToSymbol(trade.PrimaryCurrencyCode,
					trade.SecondaryCurrencyCode))) &&
			(side is null || trade.OrderType.ToStockSharp() == side) &&
			IsInRange(trade.TradeTimestampUtc, from, to);

	private static bool MatchesOrderSubscription(OrderSubscription subscription,
		string symbol, string orderId, string clientOrderId, Sides side)
		=> (subscription.Symbol.IsEmpty() ||
			subscription.Symbol.EqualsIgnoreCase(symbol)) &&
			(subscription.OrderId.IsEmpty() ||
				subscription.OrderId.EqualsIgnoreCase(orderId) ||
				subscription.OrderId.EqualsIgnoreCase(clientOrderId) ||
				GetIdentifierMatchesTracked(subscription.OrderId, orderId)) &&
			(subscription.Side is null || subscription.Side == side);

	private static bool GetIdentifierMatchesTracked(string requested,
		string exchangeOrderId)
		=> Guid.TryParse(requested, out var requestedId) &&
			Guid.TryParse(exchangeOrderId, out var exchangeId) &&
			requestedId == exchangeId;

	private static bool IsInRange(DateTime timestamp, DateTime? from,
		DateTime? to)
	{
		timestamp = timestamp.EnsureUtc();
		return (from is null || timestamp >= from.Value.ToUniversalTime()) &&
			(to is null || timestamp <= to.Value.ToUniversalTime());
	}

	private static IndependentReserveOrderCondition CreateCondition(
		IndependentReserveVolumeCurrencyTypes volumeCurrencyType)
		=> volumeCurrencyType == IndependentReserveVolumeCurrencyTypes.Secondary
			? new() { IsVolumeInQuoteCurrency = true }
			: null;

	private static string CreateClientOrderId(long transactionId,
		string userOrderId)
	{
		var value = userOrderId.IsEmpty()
			? $"s{transactionId.ToString(CultureInfo.InvariantCulture)}"
			: userOrderId.Trim();
		if (value.Length > 36)
			throw new InvalidOperationException(
				"Independent Reserve client order IDs must not exceed 36 characters.");
		return value;
	}

	private static long ParseTransactionId(string clientOrderId)
	{
		if (clientOrderId.IsEmpty())
			return 0;
		var value = clientOrderId.StartsWith('s')
			? clientOrderId[1..]
			: clientOrderId;
		return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
			out var transactionId)
			? transactionId
			: 0;
	}

	private static Guid ParseOrderGuid(string value, string operation)
		=> Guid.TryParse(value, out var result)
			? result
			: throw new InvalidOperationException(
				$"Independent Reserve {operation} requires a GUID exchange order ID.");

	private static void ValidateVolume(decimal volume, MarketDefinition market)
	{
		if (volume <= 0)
			throw new InvalidOperationException(
				"Independent Reserve order volume must be positive.");
		ValidateStep(volume, market.VolumeStep, "volume", market.Symbol);
	}

	private static void ValidatePrice(decimal price, MarketDefinition market)
	{
		if (price <= 0)
			throw new InvalidOperationException(
				$"Independent Reserve limit price must be positive for '{market.Symbol}'.");
		ValidateStep(price, market.PriceStep, "price", market.Symbol);
	}

	private static void ValidateStep(decimal value, decimal step, string name,
		string symbol)
	{
		if (step > 0 && value % step != 0)
			throw new InvalidOperationException(
				$"Independent Reserve {name} must be aligned to step {step} for '{symbol}'.");
	}

	private async ValueTask CompleteOrderStatusAsync(OrderStatusMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
