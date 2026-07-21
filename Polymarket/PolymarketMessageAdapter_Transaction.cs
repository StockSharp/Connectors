namespace StockSharp.Polymarket;

public partial class PolymarketMessageAdapter
{
	/// <inheritdoc />
	protected override ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		ValidatePortfolio(regMsg.PortfolioName);
		EnsureTradingReady();
		return PlaceOrderAsync(GetMarket(regMsg.SecurityId),
			regMsg.TransactionId, regMsg.Side, regMsg.Volume.Abs(), regMsg.Price,
			regMsg.OrderType ?? OrderTypes.Limit, regMsg.TimeInForce,
			regMsg.TillDate, regMsg.PostOnly == true, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		ValidatePortfolio(replaceMsg.PortfolioName);
		EnsureTradingReady();
		var oldOrderId = ResolveOrderId(replaceMsg.OldOrderStringId,
			replaceMsg.OldOrderId, replaceMsg.TransactionId);
		var cancellation = await RestClient.CancelOrderAsync(oldOrderId,
			cancellationToken);
		if (cancellation?.Canceled?.Any(id => id.Equals(oldOrderId,
			StringComparison.OrdinalIgnoreCase)) != true)
			throw new InvalidOperationException(
				"Polymarket did not confirm cancellation of the replaced order.");
		await PlaceOrderAsync(GetMarket(replaceMsg.SecurityId),
			replaceMsg.TransactionId, replaceMsg.Side,
			replaceMsg.Volume.Abs(), replaceMsg.Price,
			replaceMsg.OrderType ?? OrderTypes.Limit,
			replaceMsg.TimeInForce, replaceMsg.TillDate,
			replaceMsg.PostOnly == true, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		ValidatePortfolio(cancelMsg.PortfolioName);
		EnsureAccountReady();
		var orderId = ResolveOrderId(cancelMsg.OrderStringId, cancelMsg.OrderId,
			cancelMsg.TransactionId);
		var result = await RestClient.CancelOrderAsync(orderId,
			cancellationToken);
		if (result?.Canceled?.Any(id => id.Equals(orderId,
			StringComparison.OrdinalIgnoreCase)) != true)
			throw new InvalidOperationException(
				"Polymarket did not confirm cancellation of order '" + orderId +
				"'.");
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = cancelMsg.SecurityId,
			ServerTime = ServerTime,
			PortfolioName = _portfolioName,
			OrderStringId = orderId,
			OrderState = OrderStates.Done,
			TransactionId = GetOrderTransactionId(orderId,
				cancelMsg.TransactionId),
			OriginalTransactionId = cancelMsg.TransactionId,
		}, cancellationToken);
		SchedulePrivatePoll();
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		ValidatePortfolio(cancelMsg.PortfolioName);
		EnsureAccountReady();
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"Polymarket bulk cancellation does not redeem or close positions.");
		if (cancelMsg.IsStop == true)
			throw new NotSupportedException(
				"Polymarket CLOB does not expose stop orders.");
		var hasSecurity = !cancelMsg.SecurityId.SecurityCode.IsEmpty();
		if (hasSecurity && cancelMsg.Side is null && cancelMsg.IsStop is null)
		{
			var market = GetMarket(cancelMsg.SecurityId);
			await RestClient.CancelMarketAsync(market.ConditionId, market.TokenId,
				cancellationToken);
			SchedulePrivatePoll();
			return;
		}
		if (!hasSecurity && cancelMsg.Side is null && cancelMsg.IsStop is null)
		{
			await RestClient.CancelAllAsync(cancellationToken);
			SchedulePrivatePoll();
			return;
		}
		var marketFilter = hasSecurity ? GetMarket(cancelMsg.SecurityId) : null;
		var orders = await RestClient.GetOpenOrdersAsync(HistoryLimit,
			cancellationToken);
		foreach (var order in (orders ?? [])
			.Where(static order => order?.Id.IsEmpty() == false)
			.Where(order => marketFilter is null || order.AssetId.Equals(
				marketFilter.TokenId, StringComparison.Ordinal))
			.Where(order => cancelMsg.Side is null ||
				order.Side.ToStockSharp() == cancelMsg.Side))
			await RestClient.CancelOrderAsync(order.Id, cancellationToken);
		SchedulePrivatePoll();
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (_portfolioAddress.IsEmpty())
			throw new InvalidOperationException(
				"A Polymarket signer or funder address is required for positions.");
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
			BoardCode = BoardCodes.Polymarket,
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
		if (_authenticator.IsAvailable)
			await SocketClient.EnsureUserSubscriptionAsync(cancellationToken);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId,
			cancellationToken);
		EnsureAccountReady();
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
		var subscription = CreateOrderSubscription(statusMsg);
		var ordersTask = RestClient.GetOpenOrdersAsync(HistoryLimit,
			cancellationToken).AsTask();
		var tradesTask = RestClient.GetTradesAsync(HistoryLimit,
			cancellationToken).AsTask();
		await Task.WhenAll(ordersTask, tradesTask);
		await SendOrderSnapshotAsync(await ordersTask, await tradesTask,
			subscription, statusMsg.TransactionId, false, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_orderSubscriptions.Add(statusMsg.TransactionId, subscription);
		await SocketClient.EnsureUserSubscriptionAsync(cancellationToken);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private async ValueTask PlaceOrderAsync(PolymarketMarket market,
		long transactionId, Sides side, decimal volume, decimal price,
		OrderTypes orderType, TimeInForce? timeInForce, DateTime? tillDate,
		bool isPostOnly, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		if (!market.IsActive)
			throw new InvalidOperationException(
				$"Polymarket outcome '{market.SecurityCode}' is not accepting orders.");
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		PolymarketOrderTypes nativeType;
		if (orderType == OrderTypes.Market)
		{
			if (isPostOnly)
				throw new InvalidOperationException(
					"A Polymarket market order cannot be post-only.");
			price = await CalculateMarketablePriceAsync(market, side, volume,
				cancellationToken);
			nativeType = timeInForce == TimeInForce.MatchOrCancel
				? PolymarketOrderTypes.FillOrKill
				: PolymarketOrderTypes.FillAndKill;
			tillDate = null;
		}
		else
		{
			nativeType = timeInForce switch
			{
				TimeInForce.MatchOrCancel => PolymarketOrderTypes.FillOrKill,
				TimeInForce.CancelBalance => PolymarketOrderTypes.FillAndKill,
				_ when tillDate is not null =>
					PolymarketOrderTypes.GoodTillDate,
				_ => PolymarketOrderTypes.GoodTillCancelled,
			};
		}
		if (nativeType == PolymarketOrderTypes.GoodTillDate)
		{
			tillDate = tillDate?.EnsureUtc();
			if (tillDate <= ServerTime + TimeSpan.FromMinutes(1))
				throw new ArgumentOutOfRangeException(nameof(tillDate), tillDate,
					"Polymarket GTD expiry must be more than one minute in the future.");
		}
		else if (tillDate is not null)
			throw new InvalidOperationException(
				"Polymarket expiry is supported only for queued limit orders.");

		var request = Signer.CreateOrder(_orderVersion, market, side, price,
			volume, nativeType, tillDate, isPostOnly, _authenticator.ApiKey);
		PolymarketOrderResponse response;
		try
		{
			response = await RestClient.CreateOrderAsync(request,
				cancellationToken);
		}
		catch (PolymarketApiException error) when (IsVersionMismatch(
			error.Message))
		{
			response = await RetryOrderWithCurrentVersionAsync(market, side,
				price, volume, nativeType, tillDate, isPostOnly,
				cancellationToken);
		}
		if (response?.IsSuccess != true && IsVersionMismatch(response?.Error))
			response = await RetryOrderWithCurrentVersionAsync(market, side,
				price, volume, nativeType, tillDate, isPostOnly,
				cancellationToken);
		if (response?.IsSuccess != true || response.OrderId.IsEmpty())
			throw new InvalidOperationException(response?.ErrorMessage.IsEmpty() ==
				false ? response.ErrorMessage : response?.Error.IsEmpty() == false
					? response.Error :
				"Polymarket rejected the order without an error message.");
		var orderId = response.OrderId.NormalizeOrderId();
		TrackOrder(orderId, transactionId);
		var state = response.Status.ToStockSharpOrderState();
		if (state == OrderStates.Pending)
			state = OrderStates.Active;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = ServerTime,
			PortfolioName = _portfolioName,
			Side = side,
			OrderPrice = price,
			OrderVolume = volume,
			Balance = state == OrderStates.Done ? 0m : volume,
			OrderType = orderType,
			OrderState = state,
			OrderStringId = orderId,
			TransactionId = transactionId,
			OriginalTransactionId = transactionId,
			TimeInForce = nativeType.ToStockSharp(),
			ExpiryDate = tillDate,
		}, cancellationToken);
		SchedulePrivatePoll();
	}

	private async ValueTask<decimal> CalculateMarketablePriceAsync(
		PolymarketMarket market, Sides side, decimal volume,
		CancellationToken cancellationToken)
	{
		if (volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(volume));
		var book = await RestClient.GetBookAsync(market.TokenId,
			cancellationToken);
		var levels = (side == Sides.Buy ? book?.Asks : book?.Bids) ?? [];
		var ordered = side == Sides.Buy
			? levels.OrderBy(level => level.Price.ParsePolymarketDecimal(
				"ask price"))
			: levels.OrderByDescending(level => level.Price.ParsePolymarketDecimal(
				"bid price"));
		var remaining = volume;
		var boundary = 0m;
		foreach (var level in ordered)
		{
			var available = level.Size.ParsePolymarketDecimal("book size");
			if (available <= 0)
				continue;
			boundary = level.Price.ParsePolymarketDecimal("book price");
			remaining -= available;
			if (remaining <= 0)
				break;
		}
		if (remaining > 0 || boundary <= 0)
			throw new InvalidOperationException(
				$"Polymarket has insufficient liquidity for {volume} shares of " +
				market.SecurityCode + ".");
		return boundary;
	}

	private async ValueTask<PolymarketOrderResponse>
		RetryOrderWithCurrentVersionAsync(PolymarketMarket market, Sides side,
		decimal price, decimal volume, PolymarketOrderTypes orderType,
		DateTime? tillDate, bool isPostOnly,
		CancellationToken cancellationToken)
	{
		var version = await RestClient.GetVersionAsync(cancellationToken);
		var current = version?.Version ?? 0;
		if (current is not (2 or 3))
			throw new NotSupportedException(
				$"Polymarket CLOB order version {current} is unsupported.");
		_orderVersion = current;
		var request = Signer.CreateOrder(current, market, side, price, volume,
			orderType, tillDate, isPostOnly, _authenticator.ApiKey);
		return await RestClient.CreateOrderAsync(request, cancellationToken);
	}

	private static bool IsVersionMismatch(string message)
		=> message?.Contains("order_version_mismatch",
			StringComparison.OrdinalIgnoreCase) == true;

	private async ValueTask SendPortfolioSnapshotAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		var positionsTask = RestClient.GetPositionsAsync(_portfolioAddress,
			HistoryLimit, cancellationToken).AsTask();
		Task<PolymarketBalance> balanceTask = null;
		if (_authenticator.IsAvailable)
			balanceTask = RestClient.GetBalanceAsync(SignatureType,
				cancellationToken).AsTask();
		if (balanceTask is not null)
			await Task.WhenAll(positionsTask, balanceTask);
		else
			await positionsTask;
		var time = ServerTime;
		if (balanceTask is not null)
		{
			var balance = await balanceTask;
			if (!BigInteger.TryParse(balance?.Balance, NumberStyles.None,
				CultureInfo.InvariantCulture, out var rawBalance) || rawBalance < 0)
				throw new InvalidDataException(
					"Polymarket returned an invalid collateral balance.");
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = new()
				{
					SecurityCode = "pUSD",
					BoardCode = BoardCodes.Polymarket,
				},
				ServerTime = time,
				OriginalTransactionId = transactionId,
			}.TryAdd(PositionChangeTypes.CurrentValue,
				rawBalance.FromBaseUnits(6), true), cancellationToken);
		}

		var current = new HashSet<string>(StringComparer.Ordinal);
		foreach (var position in await positionsTask ?? [])
		{
			if (position?.AssetId.IsEmpty() != false)
				continue;
			var market = GetMarketByToken(position.AssetId) ??
				AddPositionMarket(position);
			current.Add(position.AssetId);
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = market.ToStockSharp(),
				ServerTime = time,
				OriginalTransactionId = transactionId,
			}.TryAdd(PositionChangeTypes.CurrentValue, position.Size, true)
			.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice, true)
			.TryAdd(PositionChangeTypes.CurrentPrice, position.CurrentPrice, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, position.CashPnl, true)
			.TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedPnl, true),
				cancellationToken);
		}
		string[] removed;
		using (_sync.EnterScope())
		{
			removed = [.. _knownPositions.Where(token => !current.Contains(token))];
			_knownPositions.Clear();
			_knownPositions.UnionWith(current);
		}
		foreach (var token in removed)
		{
			var market = GetMarketByToken(token);
			if (market is null)
				continue;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = market.ToStockSharp(),
				ServerTime = time,
				OriginalTransactionId = transactionId,
			}.TryAdd(PositionChangeTypes.CurrentValue, 0m, true),
				cancellationToken);
		}
	}

	private PolymarketMarket AddPositionMarket(PolymarketPosition position)
	{
		var code = PolymarketExtensions.ToSecurityCode(position.Slug,
			position.Outcome, position.AssetId);
		var market = new PolymarketMarket
		{
			SecurityCode = code,
			TokenId = position.AssetId,
			ConditionId = position.ConditionId,
			Question = position.Title,
			Slug = position.Slug,
			Outcome = position.Outcome,
			ExpiryDate = position.EndDate.TryParsePolymarketTime(),
			PriceStep = 0.01m,
			MinimumVolume = 0.01m,
			ReferencePrice = position.CurrentPrice,
			IsNegativeRisk = position.IsNegativeRisk,
			IsActive = false,
		};
		using (_sync.EnterScope())
		{
			if (_marketsByToken.TryGetValue(position.AssetId, out var existing))
				return existing;
			while (_markets.ContainsKey(market.SecurityCode))
				market.SecurityCode += ":" + position.AssetId[^Math.Min(8,
					position.AssetId.Length)..];
			_markets.Add(market.SecurityCode, market);
			_marketsByToken.Add(market.TokenId, market);
		}
		return market;
	}

	private async ValueTask PollPrivateAsync(
		CancellationToken cancellationToken)
	{
		long[] portfolios;
		KeyValuePair<long, PolymarketOrderSubscription>[] orders;
		using (_sync.EnterScope())
		{
			portfolios = [.. _portfolioSubscriptions];
			orders = [.. _orderSubscriptions];
		}
		if (portfolios.Length > 0)
		{
			foreach (var target in portfolios)
				await SendPortfolioSnapshotAsync(target, cancellationToken);
		}
		if (orders.Length == 0 || !_authenticator.IsAvailable)
			return;
		var openTask = RestClient.GetOpenOrdersAsync(HistoryLimit,
			cancellationToken).AsTask();
		var tradesTask = RestClient.GetTradesAsync(HistoryLimit,
			cancellationToken).AsTask();
		await Task.WhenAll(openTask, tradesTask);
		foreach (var pair in orders)
			await SendOrderSnapshotAsync(await openTask, await tradesTask,
				pair.Value, pair.Key, true, cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(
		PolymarketOpenOrder[] orders, PolymarketTrade[] trades,
		PolymarketOrderSubscription filter, long transactionId,
		bool isIncremental, CancellationToken cancellationToken)
	{
		var messages = new List<ExecutionMessage>();
		foreach (var order in orders ?? [])
		{
			var message = CreateOrderMessage(order, transactionId);
			if (message is not null && IsOrderMatch(message, filter))
				messages.Add(message);
		}
		foreach (var trade in trades ?? [])
		{
			if (trade?.Id.IsEmpty() != false)
				continue;
			var shouldSend = true;
			using (_sync.EnterScope())
			{
				if (isIncremental && _seenAccountTrades.Contains(trade.Id))
					shouldSend = false;
				_seenAccountTrades.Add(trade.Id);
			}
			if (!shouldSend)
				continue;
			var message = CreateTradeMessage(trade, transactionId);
			if (message is not null && IsTradeMatch(message, filter))
				messages.Add(message);
		}
		foreach (var message in messages
			.OrderBy(static message => message.ServerTime)
			.Skip(filter.Skip).Take(filter.Limit))
			await SendOutMessageAsync(message, cancellationToken);
	}

	private ExecutionMessage CreateOrderMessage(PolymarketOpenOrder order,
		long originalTransactionId)
	{
		if (order?.AssetId.IsEmpty() != false)
			return null;
		var market = GetMarketByToken(order.AssetId);
		if (market is null)
			return null;
		var volume = order.OriginalSize.ParsePolymarketDecimal("order size");
		var matched = order.SizeMatched.ParsePolymarketDecimal(
			"matched order size");
		var time = DateTime.UnixEpoch.AddSeconds(order.CreatedAt);
		return new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = order.Side.ToStockSharp(),
			OrderPrice = order.Price.ParsePolymarketDecimal("order price"),
			OrderVolume = volume,
			Balance = Math.Max(0m, volume - matched),
			OrderType = OrderTypes.Limit,
			OrderState = order.Status.ToStockSharpOrderState(),
			OrderStringId = order.Id,
			TransactionId = GetOrderTransactionId(order.Id,
				originalTransactionId),
			OriginalTransactionId = originalTransactionId,
			TimeInForce = order.OrderType.ToStockSharp(),
			ExpiryDate = ParseExpiration(order.Expiration),
		};
	}

	private ExecutionMessage CreateTradeMessage(PolymarketTrade trade,
		long originalTransactionId)
	{
		if (trade is null || IsFailed(trade.Status))
			return null;
		var makerOrder = trade.TraderSide == PolymarketTraderSides.Maker
			? FindUserMakerOrder(trade.MakerOrders)
			: null;
		var assetId = makerOrder?.AssetId ?? trade.AssetId;
		if (assetId.IsEmpty())
			return null;
		var market = GetMarketByToken(assetId);
		if (market is null)
			return null;
		var orderId = makerOrder?.OrderId ?? trade.TakerOrderId;
		var side = makerOrder?.Side ?? trade.Side;
		var price = makerOrder?.Price ?? trade.Price;
		var size = makerOrder?.MatchedAmount ?? trade.Size;
		return new()
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = market.ToStockSharp(),
			ServerTime = ParseApiTime(trade.MatchTime, trade.LastUpdate),
			PortfolioName = _portfolioName,
			Side = side.ToStockSharp(),
			OrderStringId = orderId,
			TradeStringId = trade.Id,
			TradePrice = price.ParsePolymarketDecimal("trade price"),
			TradeVolume = size.ParsePolymarketDecimal("trade size"),
			OriginalTransactionId = originalTransactionId,
		};
	}

	private async ValueTask OnUserOrderAsync(PolymarketSocketEvent item,
		CancellationToken cancellationToken)
	{
		var market = GetMarketByToken(item.AssetId);
		if (market is null || item.Id.IsEmpty())
			return;
		var volume = item.OriginalSize.ParsePolymarketDecimal("order size");
		var matched = item.SizeMatched.ParsePolymarketDecimal(
			"matched order size");
		var state = item.Status.ToStockSharpOrderState();
		if (item.Type == PolymarketSocketUserEventTypes.Cancellation)
			state = OrderStates.Done;
		var message = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = item.Timestamp.ParsePolymarketMilliseconds(),
			PortfolioName = _portfolioName,
			Side = item.Side.ToStockSharp(),
			OrderPrice = item.Price.ParsePolymarketDecimal("order price"),
			OrderVolume = volume,
			Balance = Math.Max(0m, volume - matched),
			OrderType = OrderTypes.Limit,
			OrderState = state,
			OrderStringId = item.Id,
			TransactionId = GetOrderTransactionId(item.Id, 0),
			TimeInForce = (item.OrderType ??
				PolymarketOrderTypes.GoodTillCancelled).ToStockSharp(),
			ExpiryDate = ParseExpiration(item.Expiration),
		};
		KeyValuePair<long, PolymarketOrderSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _orderSubscriptions];
		foreach (var subscription in subscriptions)
			if (IsOrderMatch(message, subscription.Value))
			{
				message.OriginalTransactionId = subscription.Key;
				await SendOutMessageAsync(message.Clone(), cancellationToken);
			}
		SchedulePrivatePoll();
	}

	private async ValueTask OnUserTradeAsync(PolymarketSocketEvent item,
		CancellationToken cancellationToken)
	{
		if (item.Id.IsEmpty() || item.Status is
			PolymarketSocketStatuses.Failed or
			PolymarketSocketStatuses.TradeStatusFailed)
			return;
		using (_sync.EnterScope())
			if (!_seenAccountTrades.Add(item.Id))
				return;
		var makerOrder = item.TraderSide == PolymarketTraderSides.Maker
			? FindUserMakerOrder(item.MakerOrders)
			: null;
		var assetId = makerOrder?.AssetId ?? item.AssetId;
		var market = GetMarketByToken(assetId);
		if (market is null)
			return;
		var orderId = makerOrder?.OrderId ?? item.TakerOrderId;
		var side = makerOrder?.Side ?? item.Side;
		var price = makerOrder?.Price ?? item.Price;
		var size = makerOrder?.MatchedAmount ?? item.Size;
		var message = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = market.ToStockSharp(),
			ServerTime = item.Timestamp.ParsePolymarketMilliseconds(),
			PortfolioName = _portfolioName,
			Side = side.ToStockSharp(),
			OrderStringId = orderId,
			TradeStringId = item.Id,
			TradePrice = price.ParsePolymarketDecimal("trade price"),
			TradeVolume = size.ParsePolymarketDecimal("trade size"),
		};
		KeyValuePair<long, PolymarketOrderSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _orderSubscriptions];
		foreach (var subscription in subscriptions)
			if (IsTradeMatch(message, subscription.Value))
			{
				message.OriginalTransactionId = subscription.Key;
				await SendOutMessageAsync(message.Clone(), cancellationToken);
			}
		SchedulePrivatePoll();
	}

	private PolymarketOrderSubscription CreateOrderSubscription(
		OrderStatusMessage message)
		=> new()
		{
			SecurityId = message.SecurityId,
			SecurityIds = message.SecurityIds,
			OrderId = message.OrderStringId,
			Side = message.Side,
			Volume = message.Volume,
			States = message.States,
			From = message.From?.EnsureUtc(),
			To = message.To?.EnsureUtc(),
			Skip = Math.Max(0, message.Skip ?? 0).To<int>(),
			Limit = (message.Count ?? HistoryLimit).Min(HistoryLimit).Max(1)
				.To<int>(),
		};

	private PolymarketMakerOrder FindUserMakerOrder(
		PolymarketMakerOrder[] orders)
		=> (orders ?? []).FirstOrDefault(order => order?.Owner.Equals(
			_authenticator.ApiKey, StringComparison.Ordinal) == true) ??
			(orders ?? []).FirstOrDefault();

	private PolymarketSocketMakerOrder FindUserMakerOrder(
		PolymarketSocketMakerOrder[] orders)
		=> (orders ?? []).FirstOrDefault(order => order?.Owner.Equals(
			_authenticator.ApiKey, StringComparison.Ordinal) == true) ??
			(orders ?? []).FirstOrDefault();

	private static bool IsFailed(PolymarketTradeStatuses status)
		=> status is PolymarketTradeStatuses.Failed or
			PolymarketTradeStatuses.TradeStatusFailed;

	private static DateTime ParseApiTime(string primary, string fallback)
	{
		var value = primary.IsEmpty() ? fallback : primary;
		if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
			out var timestamp) && timestamp > 0)
			return DateTime.UnixEpoch.AddSeconds(timestamp);
		return value.TryParsePolymarketTime() ?? DateTime.UtcNow;
	}

	private static DateTime? ParseExpiration(string value)
		=> long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
			out var timestamp) && timestamp > 0
				? DateTime.UnixEpoch.AddSeconds(timestamp)
				: null;

	private static string ResolveOrderId(string orderStringId, long? orderId,
		long transactionId)
	{
		if (!orderStringId.IsEmpty())
			return orderStringId.NormalizeOrderId();
		if (orderId is long numeric)
			return numeric.ToString(CultureInfo.InvariantCulture);
		throw new InvalidOperationException(
			"Polymarket order string ID is required for transaction " +
			transactionId.ToString(CultureInfo.InvariantCulture) + ".");
	}

	private static bool IsOrderMatch(ExecutionMessage message,
		PolymarketOrderSubscription filter)
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

	private static bool IsTradeMatch(ExecutionMessage message,
		PolymarketOrderSubscription filter)
		=> IsSecurityMatch(message.SecurityId, filter.SecurityId) &&
			(filter.SecurityIds?.Length is not > 0 || filter.SecurityIds.Any(
				securityId => IsSecurityMatch(message.SecurityId, securityId))) &&
			(filter.OrderId.IsEmpty() || filter.OrderId.Equals(
				message.OrderStringId, StringComparison.OrdinalIgnoreCase)) &&
			(filter.Side is null || message.Side == filter.Side) &&
			(filter.Volume is null || message.TradeVolume == filter.Volume) &&
			(filter.States?.Length is not > 0 ||
				filter.States.Contains(OrderStates.Done)) &&
			(filter.From is null || message.ServerTime >= filter.From) &&
			(filter.To is null || message.ServerTime <= filter.To);

	private static bool IsSecurityMatch(SecurityId securityId,
		SecurityId filter)
		=> (filter.SecurityCode.IsEmpty() || filter.SecurityCode.Equals(
			securityId.SecurityCode, StringComparison.OrdinalIgnoreCase)) &&
			(filter.BoardCode.IsEmpty() || filter.BoardCode.Equals(
				securityId.BoardCode, StringComparison.OrdinalIgnoreCase));

	private void SchedulePrivatePoll()
	{
		using (_sync.EnterScope())
			_nextPrivatePoll = CurrentTime;
	}
}
