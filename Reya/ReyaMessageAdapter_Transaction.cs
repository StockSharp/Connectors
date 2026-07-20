namespace StockSharp.Reya;

public partial class ReyaMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		ValidatePortfolio(regMsg.PortfolioName);
		var market = GetMarket(regMsg.SecurityId);
		EnsureTradingReady(market);
		await PlaceOrderAsync(market, regMsg.TransactionId, regMsg.Side,
			regMsg.Volume.Abs(), regMsg.Price,
			regMsg.OrderType ?? OrderTypes.Limit, regMsg.TimeInForce,
			regMsg.PostOnly == true, regMsg.TillDate,
			regMsg.PositionEffect, regMsg.Condition as ReyaOrderCondition ?? new(),
			regMsg.UserOrderId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		ValidatePortfolio(replaceMsg.PortfolioName);
		var market = GetMarket(replaceMsg.SecurityId);
		EnsureTradingReady(market);
		var oldOrderId = ResolveOrderId(replaceMsg.OldOrderStringId,
			replaceMsg.OldOrderId);
		var cancelled = await CancelOrderCoreAsync(market, oldOrderId, null,
			cancellationToken);
		await SendCancelledOrderAsync(market, oldOrderId, cancelled,
			replaceMsg.TransactionId, cancellationToken);
		await PlaceOrderAsync(market, replaceMsg.TransactionId, replaceMsg.Side,
			replaceMsg.Volume.Abs(), replaceMsg.Price,
			replaceMsg.OrderType ?? OrderTypes.Limit, replaceMsg.TimeInForce,
			replaceMsg.PostOnly == true, replaceMsg.TillDate,
			replaceMsg.PositionEffect,
			replaceMsg.Condition as ReyaOrderCondition ?? new(),
			replaceMsg.UserOrderId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		ValidatePortfolio(cancelMsg.PortfolioName);
		var orderId = ResolveOrderId(cancelMsg.OrderStringId, cancelMsg.OrderId);
		var clientOrderId = ParseClientOrderId(cancelMsg.UserOrderId);
		if (!orderId.IsEmpty())
			clientOrderId = null;
		var symbol = !cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? GetMarket(cancelMsg.SecurityId).Symbol
			: GetOrderSymbol(orderId);
		if (symbol.IsEmpty())
			throw new InvalidOperationException(
				"Reya cancellation requires the order security.");
		var market = GetMarket(symbol) ?? throw new InvalidOperationException(
			"Unknown Reya order security '" + symbol + "'.");
		EnsureTradingReady(market);
		var cancelled = await CancelOrderCoreAsync(market, orderId,
			clientOrderId, cancellationToken);
		await SendCancelledOrderAsync(market, orderId, cancelled,
			cancelMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureAccountReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"Reya bulk cancellation does not close positions.");
		_ = Signer;
		if (cancelMsg.SecurityTypes is { Length: > 0 } &&
			!cancelMsg.SecurityTypes.Contains(SecurityTypes.Future) &&
			!cancelMsg.SecurityTypes.Contains(SecurityTypes.CryptoCurrency))
			return;

		var symbol = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(cancelMsg.SecurityId).Symbol;
		var orders = await RestClient.GetOpenOrdersAsync(_ownerAddress,
			cancellationToken);
		foreach (var order in orders
			.Where(static order => order?.OrderId.IsEmpty() == false)
			.Where(order => symbol.IsEmpty() || order.Symbol.Equals(symbol,
				StringComparison.Ordinal))
			.Where(order => cancelMsg.Side is null ||
				order.Side.ToStockSharp() == cancelMsg.Side)
			.Where(order => cancelMsg.IsStop is null ||
				(order.OrderType != ReyaOrderTypes.Limit) == cancelMsg.IsStop))
		{
			var market = GetMarket(order.Symbol) ?? throw new InvalidDataException(
				"Reya order refers to unknown symbol '" + order.Symbol + "'.");
			await CancelOrderCoreAsync(market, order.OrderId, null,
				cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureAccountReady();
		ValidatePortfolio(lookupMsg.PortfolioName);
		var channels = PortfolioChannels();
		if (!lookupMsg.IsSubscribe)
		{
			bool removed;
			using (_sync.EnterScope())
				removed = _portfolioSubscriptions.Remove(
					lookupMsg.OriginalTransactionId);
			if (removed)
				await ReleaseAccountChannelsAsync(channels, cancellationToken);
			return;
		}

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = _portfolioName,
			BoardCode = BoardCodes.Reya,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId,
			cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}

		using (_sync.EnterScope())
			_portfolioSubscriptions.Add(lookupMsg.TransactionId);
		try
		{
			await AddAccountChannelsAsync(channels, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_portfolioSubscriptions.Remove(lookupMsg.TransactionId);
			throw;
		}
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
		var channels = OrderChannels();
		if (!statusMsg.IsSubscribe)
		{
			bool removed;
			using (_sync.EnterScope())
				removed = _orderSubscriptions.Remove(
					statusMsg.OriginalTransactionId);
			if (removed)
				await ReleaseAccountChannelsAsync(channels, cancellationToken);
			return;
		}

		var subscription = CreateOrderStatusSubscription(statusMsg);
		await SendOrderSnapshotAsync(subscription, statusMsg.TransactionId,
			cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
			return;
		}

		using (_sync.EnterScope())
			_orderSubscriptions.Add(statusMsg.TransactionId, subscription);
		try
		{
			await AddAccountChannelsAsync(channels, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_orderSubscriptions.Remove(statusMsg.TransactionId);
			throw;
		}
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private async ValueTask PlaceOrderAsync(ReyaMarket market,
		long transactionId, Sides side, decimal volume, decimal price,
		OrderTypes orderType, TimeInForce? timeInForce, bool isPostOnly,
		DateTime? tillDate, OrderPositionEffects? positionEffect,
		ReyaOrderCondition condition, string userOrderId,
		CancellationToken cancellationToken)
	{
		if (isPostOnly)
			throw new NotSupportedException(
				"Reya API v2 does not expose post-only orders.");
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market or
			OrderTypes.Conditional))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		if (orderType == OrderTypes.Conditional && market.IsSpot)
			throw new NotSupportedException(
				"Reya trigger orders are available for perpetual markets only.");
		condition.IsReduceOnly |= positionEffect == OrderPositionEffects.CloseOnly;
		var clientOrderId = ResolveClientOrderId(transactionId, userOrderId);

		ReyaCreateOrderRequest request;
		var actualPrice = price;
		var stockSharpTimeInForce = timeInForce;
		if (orderType == OrderTypes.Conditional)
		{
			condition.IsReduceOnly = true;
			if (condition.TriggerPrice is not > 0)
				throw new InvalidOperationException(
					"Reya conditional orders require a positive trigger price.");
			request = CreateTriggerRequest(market, side, condition, clientOrderId);
			stockSharpTimeInForce = TimeInForce.PutInQueue;
			actualPrice = condition.TriggerPrice.Value;
		}
		else
		{
			if (orderType == OrderTypes.Market)
			{
				actualPrice = GetProtectivePrice(market, side);
				stockSharpTimeInForce = TimeInForce.CancelBalance;
			}
			var apiTimeInForce = ToReyaTimeInForce(stockSharpTimeInForce);
			ValidateOrder(market, volume, actualPrice, apiTimeInForce,
				condition.IsReduceOnly);
			request = CreateLimitRequest(market, clientOrderId, side, volume,
				actualPrice, apiTimeInForce, tillDate, condition.IsReduceOnly);
		}

		var response = await RestClient.CreateOrderAsync(request,
			cancellationToken) ?? throw new InvalidDataException(
				"Reya returned no order creation response.");
		TrackOrder(response.OrderId, response.ClientOrderId ??
			request.ClientOrderId, transactionId, market.Symbol);
		var cumulative = response.CumulativeQuantity.TryParseReyaDecimal() ??
			response.ExecutedQuantity.TryParseReyaDecimal() ?? 0m;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = ServerTime,
			PortfolioName = _portfolioName,
			DepoName = request.AccountId.ToString(CultureInfo.InvariantCulture),
			Side = side,
			OrderVolume = orderType == OrderTypes.Conditional ? null : volume,
			Balance = orderType == OrderTypes.Conditional
				? null
				: (volume - cumulative).Max(0m),
			OrderPrice = actualPrice,
			OrderType = orderType,
			OrderState = response.Status.ToStockSharp(),
			OrderStringId = response.OrderId,
			UserOrderId = (response.ClientOrderId ?? request.ClientOrderId)?
				.ToString(CultureInfo.InvariantCulture),
			TransactionId = transactionId,
			OriginalTransactionId = transactionId,
			TimeInForce = stockSharpTimeInForce,
			ExpiryDate = request.ExpiresAfter is long expiry
				? DateTime.UnixEpoch.AddSeconds(expiry)
				: null,
			PositionEffect = condition.IsReduceOnly
				? OrderPositionEffects.CloseOnly
				: null,
			Condition = condition,
		}, cancellationToken);
	}

	private ReyaCreateOrderRequest CreateLimitRequest(ReyaMarket market,
		long clientOrderId, Sides side, decimal volume, decimal price,
		ReyaTimeInForces timeInForce, DateTime? tillDate, bool isReduceOnly)
	{
		var accountId = GetAccountId(market);
		var nonce = market.IsSpot
			? new BigInteger(Signer.CreateSpotNonce())
			: Signer.CreateOrderNonce(accountId, market.MarketId);
		var deadline = ResolveDeadline(market, timeInForce, tillDate);
		var gatewayType = market.IsSpot
			? ReyaGatewayOrderTypes.SpotLimit
			: timeInForce == ReyaTimeInForces.GoodTillCancelled
				? ReyaGatewayOrderTypes.Limit
				: isReduceOnly
					? ReyaGatewayOrderTypes.ReduceOnlyMarket
					: ReyaGatewayOrderTypes.Market;
		var inputs = Signer.EncodeLimitInputs(side == Sides.Buy, price, volume);
		var signature = Signer.SignOrder(accountId, market.MarketId, ExchangeId,
			market.IsSpot ? [] : [PoolAccountId.ParseReyaInteger("pool account ID")],
			gatewayType, inputs, deadline, nonce);
		return new()
		{
			ExchangeId = ExchangeId,
			Symbol = market.Symbol,
			AccountId = accountId,
			IsBuy = side == Sides.Buy,
			LimitPrice = price.ToReyaDecimal(),
			Quantity = volume.ToReyaDecimal(),
			OrderType = ReyaOrderTypes.Limit,
			TimeInForce = timeInForce,
			IsReduceOnly = !market.IsSpot &&
				timeInForce == ReyaTimeInForces.ImmediateOrCancel
					? isReduceOnly
					: null,
			Signature = signature,
			Nonce = nonce.ToString(CultureInfo.InvariantCulture),
			SignerWallet = Signer.Address,
			ExpiresAfter = timeInForce == ReyaTimeInForces.ImmediateOrCancel ||
				market.IsSpot
					? checked((long)deadline)
					: null,
			ClientOrderId = clientOrderId,
		};
	}

	private ReyaCreateOrderRequest CreateTriggerRequest(ReyaMarket market,
		Sides side, ReyaOrderCondition condition, long clientOrderId)
	{
		var accountId = GetAccountId(market);
		var nonce = Signer.CreateOrderNonce(accountId, market.MarketId);
		var triggerType = condition.TriggerType switch
		{
			ReyaTriggerOrderTypes.StopLoss => ReyaOrderTypes.StopLoss,
			ReyaTriggerOrderTypes.TakeProfit => ReyaOrderTypes.TakeProfit,
			_ => throw new ArgumentOutOfRangeException(nameof(condition),
				condition.TriggerType, null),
		};
		var gatewayType = condition.TriggerType == ReyaTriggerOrderTypes.StopLoss
			? ReyaGatewayOrderTypes.StopLoss
			: ReyaGatewayOrderTypes.TakeProfit;
		var inputs = Signer.EncodeTriggerInputs(side == Sides.Buy,
			condition.TriggerPrice.Value);
		var deadline = BigInteger.Pow(10, 18);
		var signature = Signer.SignOrder(accountId, market.MarketId, ExchangeId,
			[PoolAccountId.ParseReyaInteger("pool account ID")], gatewayType,
			inputs, deadline, nonce);
		return new()
		{
			ExchangeId = ExchangeId,
			Symbol = market.Symbol,
			AccountId = accountId,
			IsBuy = side == Sides.Buy,
			LimitPrice = side == Sides.Buy
				? "100000000000000000000"
				: "0",
			OrderType = triggerType,
			TriggerPrice = condition.TriggerPrice.Value.ToReyaDecimal(),
			Signature = signature,
			Nonce = nonce.ToString(CultureInfo.InvariantCulture),
			SignerWallet = Signer.Address,
			ClientOrderId = clientOrderId,
		};
	}

	private BigInteger ResolveDeadline(ReyaMarket market,
		ReyaTimeInForces timeInForce, DateTime? tillDate)
	{
		var now = DateTime.UtcNow;
		if (timeInForce == ReyaTimeInForces.ImmediateOrCancel)
			return now.AddSeconds(10).ToReyaSeconds();
		if (!market.IsSpot)
			return BigInteger.Pow(10, 18);
		var expiry = (tillDate ?? now.AddDays(1)).EnsureReyaUtc();
		if (expiry <= now || expiry > now.AddDays(1))
			throw new InvalidOperationException(
				"Reya spot GTC expiry must be in the future and no more than 24 hours away.");
		return expiry.ToReyaSeconds();
	}

	private async ValueTask<ReyaCancelOrderResponse> CancelOrderCoreAsync(
		ReyaMarket market, string orderId, long? clientOrderId,
		CancellationToken cancellationToken)
	{
		ReyaCancelOrderRequest request;
		if (market.IsSpot)
		{
			if (orderId.IsEmpty() && clientOrderId is null)
				throw new InvalidOperationException(
					"Reya spot cancellation requires an order or client order ID.");
			var accountId = GetAccountId(market);
			var numericOrderId = orderId.IsEmpty()
				? BigInteger.Zero
				: orderId.ParseReyaInteger("spot order ID");
			var numericClientId = clientOrderId ?? 0;
			var nonce = Signer.CreateSpotNonce();
			var deadline = DateTime.UtcNow.AddSeconds(10).ToReyaSeconds();
			request = new()
			{
				OrderId = orderId,
				ClientOrderId = clientOrderId,
				AccountId = accountId,
				Symbol = market.Symbol,
				Signature = Signer.SignSpotCancellation(accountId,
					market.MarketId, numericOrderId, numericClientId, nonce,
					deadline),
				Nonce = nonce.ToString(CultureInfo.InvariantCulture),
				ExpiresAfter = deadline,
			};
		}
		else
		{
			orderId = orderId.ThrowIfEmpty(nameof(orderId));
			request = new()
			{
				OrderId = orderId,
				Signature = Signer.SignPerpetualCancellation(orderId),
			};
		}
		var response = await RestClient.CancelOrderAsync(request,
			cancellationToken) ?? throw new InvalidDataException(
				"Reya returned no cancellation response.");
		TrackOrder(response.OrderId, response.ClientOrderId,
			GetTransactionId(orderId, clientOrderId), market.Symbol);
		return response;
	}

	private ValueTask SendCancelledOrderAsync(ReyaMarket market,
		string requestedOrderId, ReyaCancelOrderResponse response,
		long originalTransactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = ServerTime,
			PortfolioName = _portfolioName,
			DepoName = GetAccountId(market).ToString(
				CultureInfo.InvariantCulture),
			OrderStringId = response.OrderId.IsEmpty()
				? requestedOrderId
				: response.OrderId,
			UserOrderId = (response.ClientOrderId ?? GetClientOrderId(
				response.OrderId.IsEmpty() ? requestedOrderId : response.OrderId))?
					.ToString(CultureInfo.InvariantCulture),
			OrderState = response.Status.ToStockSharp(),
			Balance = 0m,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);

	private async ValueTask SendPortfolioSnapshotAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		var positionTask = RestClient.GetPositionsAsync(_ownerAddress,
			cancellationToken).AsTask();
		var balanceTask = RestClient.GetBalancesAsync(_ownerAddress,
			cancellationToken).AsTask();
		await Task.WhenAll(positionTask, balanceTask);
		await SendPositionSnapshotAsync(await positionTask, transactionId,
			cancellationToken);
		await SendBalanceSnapshotAsync(await balanceTask, transactionId,
			cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(
		OrderStatusSubscription subscription, long transactionId,
		CancellationToken cancellationToken)
	{
		var openTask = RestClient.GetOpenOrdersAsync(_ownerAddress,
			cancellationToken).AsTask();
		var perpetualTask = RestClient.GetWalletPerpetualExecutionsAsync(
			_ownerAddress, subscription.From, subscription.To,
			cancellationToken).AsTask();
		var spotTask = RestClient.GetWalletSpotExecutionsAsync(_ownerAddress,
			subscription.From, subscription.To, cancellationToken).AsTask();
		await Task.WhenAll(openTask, perpetualTask, spotTask);

		var orders = (await openTask)
			.Where(static order => order is not null)
			.Select(order => CreateOrderMessage(order, transactionId))
			.Where(message => IsOrderMatch(message, subscription))
			.OrderByDescending(static message => message.ServerTime)
			.Skip(subscription.Skip)
			.Take(subscription.Limit)
			.OrderBy(static message => message.ServerTime)
			.ToArray();
		foreach (var message in orders)
			await SendOutMessageAsync(message, cancellationToken);

		foreach (var trade in ((await perpetualTask)?.Data ?? [])
			.Where(static trade => trade is not null)
			.Where(trade => IsTradeMatch(trade.Symbol,
				trade.Side.ToStockSharp(),
				trade.Timestamp.FromReyaMillisecondsOrNow(), null, null,
				subscription))
			.OrderByDescending(static trade => trade.Timestamp)
			.Skip(subscription.Skip)
			.Take(subscription.Limit)
			.OrderBy(static trade => trade.Timestamp))
			await SendAccountTradeAsync(trade, transactionId, false,
				cancellationToken);
		foreach (var trade in ((await spotTask)?.Data ?? [])
			.Where(static trade => trade is not null)
			.Where(trade => IsTradeMatch(trade.Symbol,
				GetAccountTradeSide(trade),
				trade.Timestamp.FromReyaMillisecondsOrNow(),
				GetAccountOrderId(trade), GetClientOrderId(
					GetAccountOrderId(trade))?.ToString(
						CultureInfo.InvariantCulture), subscription))
			.OrderByDescending(static trade => trade.Timestamp)
			.Skip(subscription.Skip)
			.Take(subscription.Limit)
			.OrderBy(static trade => trade.Timestamp))
			await SendAccountTradeAsync(trade, transactionId, false,
				cancellationToken);
	}

	private ExecutionMessage CreateOrderMessage(ReyaOrder order,
		long originalTransactionId)
	{
		var market = GetMarket(order.Symbol) ?? throw new InvalidDataException(
			"Reya order refers to unknown symbol '" + order.Symbol + "'.");
		var quantity = order.Quantity.TryParseReyaDecimal();
		var cumulative = order.CumulativeQuantity.TryParseReyaDecimal() ??
			order.ExecutedQuantity.TryParseReyaDecimal() ?? 0m;
		var transactionId = GetTransactionId(order.OrderId, null);
		TrackOrder(order.OrderId, null, transactionId, order.Symbol);
		var condition = new ReyaOrderCondition
		{
			IsReduceOnly = order.IsReduceOnly == true ||
				order.OrderType != ReyaOrderTypes.Limit,
			TriggerPrice = order.TriggerPrice.TryParseReyaDecimal(),
			TriggerType = order.OrderType == ReyaOrderTypes.TakeProfit
				? ReyaTriggerOrderTypes.TakeProfit
				: ReyaTriggerOrderTypes.StopLoss,
		};
		return new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = (order.LastUpdatedAt > 0
				? order.LastUpdatedAt
				: order.CreatedAt).FromReyaMillisecondsOrNow(),
			PortfolioName = _portfolioName,
			DepoName = order.AccountId.ToString(CultureInfo.InvariantCulture),
			Side = order.Side.ToStockSharp(),
			OrderVolume = quantity,
			Balance = quantity is decimal value
				? (value - cumulative).Max(0m)
				: null,
			OrderPrice = order.LimitPrice.TryParseReyaDecimal() ?? 0m,
			OrderType = order.OrderType.ToStockSharp(),
			OrderState = order.Status.ToStockSharp(),
			OrderStringId = order.OrderId,
			UserOrderId = GetClientOrderId(order.OrderId)?.ToString(
				CultureInfo.InvariantCulture),
			TransactionId = transactionId,
			OriginalTransactionId = originalTransactionId,
			TimeInForce = order.TimeInForce switch
			{
				ReyaTimeInForces.ImmediateOrCancel => TimeInForce.CancelBalance,
				ReyaTimeInForces.GoodTillCancelled => TimeInForce.PutInQueue,
				_ => null,
			},
			PositionEffect = condition.IsReduceOnly
				? OrderPositionEffects.CloseOnly
				: null,
			Condition = condition,
		};
	}

	private async ValueTask SendPositionSnapshotAsync(ReyaPosition[] positions,
		long transactionId, CancellationToken cancellationToken)
	{
		var current = new HashSet<string>(StringComparer.Ordinal);
		foreach (var position in positions ?? [])
		{
			if (position?.Symbol.IsEmpty() != false)
				continue;
			current.Add(PositionKey(position.AccountId, position.Symbol));
			await SendPositionAsync(position, transactionId, cancellationToken);
		}
		await SendMissingPositionsAsync(current, _knownPositions, transactionId,
			cancellationToken);
	}

	private ValueTask SendPositionAsync(ReyaPosition position,
		long transactionId, CancellationToken cancellationToken)
	{
		if (position?.Symbol.IsEmpty() != false || transactionId == 0)
			return default;
		var key = PositionKey(position.AccountId, position.Symbol);
		using (_sync.EnterScope())
			_knownPositions.Add(key);
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = position.Symbol.ToStockSharp(),
			DepoName = position.AccountId.ToString(CultureInfo.InvariantCulture),
			ServerTime = ServerTime,
			OriginalTransactionId = transactionId,
			Side = position.Side.ToStockSharp(),
		}
		.TryAdd(PositionChangeTypes.CurrentValue,
			position.Quantity.TryParseReyaDecimal()?.Abs(), true)
		.TryAdd(PositionChangeTypes.AveragePrice,
			position.AverageEntryPrice.TryParseReyaDecimal(), true),
			cancellationToken);
	}

	private async ValueTask SendBalanceSnapshotAsync(
		ReyaAccountBalance[] balances, long transactionId,
		CancellationToken cancellationToken)
	{
		var current = new HashSet<string>(StringComparer.Ordinal);
		foreach (var balance in balances ?? [])
		{
			if (balance?.Asset.IsEmpty() != false)
				continue;
			current.Add(PositionKey(balance.AccountId, balance.Asset));
			await SendBalanceAsync(balance, transactionId, cancellationToken);
		}
		await SendMissingPositionsAsync(current, _knownBalances, transactionId,
			cancellationToken);
	}

	private ValueTask SendBalanceAsync(ReyaAccountBalance balance,
		long transactionId, CancellationToken cancellationToken)
	{
		if (balance?.Asset.IsEmpty() != false || transactionId == 0)
			return default;
		var key = PositionKey(balance.AccountId, balance.Asset);
		using (_sync.EnterScope())
			_knownBalances.Add(key);
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = balance.Asset.ToStockSharp(),
			DepoName = balance.AccountId.ToString(CultureInfo.InvariantCulture),
			ServerTime = ServerTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue,
			balance.RealBalance.TryParseReyaDecimal(), true), cancellationToken);
	}

	private async ValueTask SendMissingPositionsAsync(HashSet<string> current,
		HashSet<string> known, long transactionId,
		CancellationToken cancellationToken)
	{
		string[] missing;
		long[] subscriptions;
		using (_sync.EnterScope())
		{
			missing = [.. known.Where(key => !current.Contains(key))];
			known.Clear();
			known.UnionWith(current);
			subscriptions = [.. _portfolioSubscriptions
				.Append(transactionId).Distinct()];
		}
		foreach (var key in missing)
		{
			var separator = key.IndexOf(':');
			if (separator <= 0 || separator + 1 >= key.Length)
				continue;
			foreach (var subscriptionId in subscriptions)
				await SendOutMessageAsync(new PositionChangeMessage
				{
					PortfolioName = _portfolioName,
					SecurityId = key[(separator + 1)..].ToStockSharp(),
					DepoName = key[..separator],
					ServerTime = ServerTime,
					OriginalTransactionId = subscriptionId,
				}.TryAdd(PositionChangeTypes.CurrentValue, 0m, true),
					cancellationToken);
		}
	}

	private ValueTask SendAccountTradeAsync(ReyaPerpetualExecution trade,
		long originalTransactionId, bool isOnlyNew,
		CancellationToken cancellationToken)
	{
		if (trade?.Symbol.IsEmpty() != false || trade.SequenceNumber <= 0)
			return default;
		var key = GetAccountTradeKey(trade);
		var isNew = TryAcceptTrade(_seenAccountTrades, key);
		if (isOnlyNew && !isNew)
			return default;
		var market = GetMarket(trade.Symbol);
		var time = trade.Timestamp.FromReyaMillisecondsOrNow();
		UpdateServerTime(time);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = trade.Symbol.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			DepoName = trade.AccountId.ToString(CultureInfo.InvariantCulture),
			Side = trade.Side.ToStockSharp(),
			TradeId = trade.SequenceNumber,
			TradePrice = trade.Price.ParseReyaDecimal("account execution price"),
			TradeVolume = trade.Quantity.ParseReyaDecimal(
				"account execution quantity"),
			Commission = trade.Fee.TryParseReyaDecimal(),
			CommissionCurrency = market?.QuoteAsset,
			PnL = trade.RealizedPnL.TryParseReyaDecimal(),
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private ValueTask SendAccountTradeAsync(ReyaSpotExecution trade,
		long originalTransactionId, bool isOnlyNew,
		CancellationToken cancellationToken)
	{
		if (trade?.Symbol.IsEmpty() != false || trade.SequenceNumber <= 0)
			return default;
		var key = GetAccountTradeKey(trade);
		var isNew = TryAcceptTrade(_seenAccountTrades, key);
		if (isOnlyNew && !isNew)
			return default;
		var side = GetAccountTradeSide(trade);
		var orderId = GetAccountOrderId(trade);
		var transactionId = GetTransactionId(orderId, null);
		var market = GetMarket(trade.Symbol);
		var time = trade.Timestamp.FromReyaMillisecondsOrNow();
		UpdateServerTime(time);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = trade.Symbol.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			DepoName = GetAccountTradeAccountId(trade).ToString(
				CultureInfo.InvariantCulture),
			Side = side,
			OrderStringId = orderId,
			UserOrderId = GetClientOrderId(orderId)?.ToString(
				CultureInfo.InvariantCulture),
			TradeId = trade.SequenceNumber,
			TradePrice = trade.Price.ParseReyaDecimal("account execution price"),
			TradeVolume = trade.Quantity.ParseReyaDecimal(
				"account execution quantity"),
			Commission = trade.Fee.TryParseReyaDecimal(),
			CommissionCurrency = market?.QuoteAsset,
			TransactionId = transactionId,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private async ValueTask OnPositionsAsync(
		ReyaSocketEnvelope<ReyaPosition[]> envelope,
		CancellationToken cancellationToken)
	{
		if (envelope is null)
			return;
		UpdateServerTime(envelope.Timestamp ?? 0);
		long[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _portfolioSubscriptions];
		foreach (var position in envelope.Data ?? [])
			foreach (var transactionId in subscriptions)
				await SendPositionAsync(position, transactionId, cancellationToken);
	}

	private async ValueTask OnBalancesAsync(
		ReyaSocketEnvelope<ReyaAccountBalance[]> envelope,
		CancellationToken cancellationToken)
	{
		if (envelope is null)
			return;
		UpdateServerTime(envelope.Timestamp ?? 0);
		long[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _portfolioSubscriptions];
		foreach (var balance in envelope.Data ?? [])
			foreach (var transactionId in subscriptions)
				await SendBalanceAsync(balance, transactionId, cancellationToken);
	}

	private async ValueTask OnOrdersAsync(ReyaSocketEnvelope<ReyaOrder[]> envelope,
		CancellationToken cancellationToken)
	{
		if (envelope is null)
			return;
		UpdateServerTime(envelope.Timestamp ?? 0);
		KeyValuePair<long, OrderStatusSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _orderSubscriptions];
		foreach (var order in envelope.Data ?? [])
		{
			if (order?.OrderId.IsEmpty() != false)
				continue;
			foreach (var (transactionId, subscription) in subscriptions)
			{
				var message = CreateOrderMessage(order, transactionId);
				if (IsOrderMatch(message, subscription))
					await SendOutMessageAsync(message, cancellationToken);
			}
		}
	}

	private async ValueTask OnWalletPerpetualExecutionsAsync(
		ReyaSocketEnvelope<ReyaPerpetualExecution[]> envelope,
		CancellationToken cancellationToken)
	{
		if (envelope is null)
			return;
		UpdateServerTime(envelope.Timestamp ?? 0);
		KeyValuePair<long, OrderStatusSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _orderSubscriptions];
		foreach (var trade in envelope.Data ?? [])
		{
			if (trade?.Symbol.IsEmpty() != false || trade.SequenceNumber <= 0 ||
				!TryAcceptTrade(_seenAccountTrades, GetAccountTradeKey(trade)))
				continue;
			var side = trade.Side.ToStockSharp();
			var time = trade.Timestamp.FromReyaMillisecondsOrNow();
			foreach (var (transactionId, subscription) in subscriptions)
				if (IsTradeMatch(trade.Symbol, side, time, null, null,
					subscription))
					await SendAccountTradeAsync(trade, transactionId, false,
						cancellationToken);
		}
	}

	private async ValueTask OnWalletSpotExecutionsAsync(
		ReyaSocketEnvelope<ReyaSpotExecution[]> envelope,
		CancellationToken cancellationToken)
	{
		if (envelope is null)
			return;
		UpdateServerTime(envelope.Timestamp ?? 0);
		KeyValuePair<long, OrderStatusSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _orderSubscriptions];
		foreach (var trade in envelope.Data ?? [])
		{
			if (trade?.Symbol.IsEmpty() != false || trade.SequenceNumber <= 0 ||
				!TryAcceptTrade(_seenAccountTrades, GetAccountTradeKey(trade)))
				continue;
			var side = GetAccountTradeSide(trade);
			var time = trade.Timestamp.FromReyaMillisecondsOrNow();
			var orderId = GetAccountOrderId(trade);
			var userOrderId = GetClientOrderId(orderId)?.ToString(
				CultureInfo.InvariantCulture);
			foreach (var (transactionId, subscription) in subscriptions)
				if (IsTradeMatch(trade.Symbol, side, time, orderId,
					userOrderId, subscription))
					await SendAccountTradeAsync(trade, transactionId, false,
						cancellationToken);
		}
	}

	private async ValueTask AddAccountChannelsAsync(string[] channels,
		CancellationToken cancellationToken)
	{
		string[] added;
		using (_sync.EnterScope())
			added = AddChannelReferencesUnsafe(channels);
		try
		{
			await SubscribeChannelsAsync(added, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				ReleaseChannelReferencesUnsafe(channels);
			throw;
		}
	}

	private async ValueTask ReleaseAccountChannelsAsync(string[] channels,
		CancellationToken cancellationToken)
	{
		string[] removed;
		using (_sync.EnterScope())
			removed = ReleaseChannelReferencesUnsafe(channels);
		await UnsubscribeChannelsAsync(removed, cancellationToken);
	}

	private string[] PortfolioChannels()
		=>
		[
			WalletChannel("positions"),
			WalletChannel("accountBalances"),
		];

	private string[] OrderChannels()
		=>
		[
			WalletChannel("orderChanges"),
			WalletChannel("perpExecutions"),
			WalletChannel("spotExecutions"),
		];

	private string WalletChannel(string resource)
		=> "/v2/wallet/" + _ownerAddress + "/" + resource;

	private OrderStatusSubscription CreateOrderStatusSubscription(
		OrderStatusMessage statusMsg)
	{
		var from = statusMsg.From?.EnsureReyaUtc();
		var to = statusMsg.To?.EnsureReyaUtc();
		if (from is DateTime start && to is DateTime end && start > end)
			throw new ArgumentOutOfRangeException(nameof(statusMsg),
				"Reya order-history start time cannot be later than end time.");
		return new()
		{
			Symbols = GetOrderSymbols(statusMsg),
			OrderId = statusMsg.OrderId,
			OrderStringId = statusMsg.OrderStringId,
			UserOrderId = statusMsg.UserOrderId,
			Side = statusMsg.Side,
			Volume = statusMsg.Volume,
			States = statusMsg.States ?? [],
			From = from,
			To = to,
			Skip = Math.Max(0, statusMsg.Skip ?? 0).To<int>(),
			Limit = (statusMsg.Count ?? HistoryLimit).Min(HistoryLimit).Max(1)
				.To<int>(),
		};
	}

	private string[] GetOrderSymbols(OrderStatusMessage statusMsg)
	{
		var result = new HashSet<string>(StringComparer.Ordinal);
		if (!statusMsg.SecurityId.SecurityCode.IsEmpty())
			result.Add(GetMarket(statusMsg.SecurityId).Symbol);
		foreach (var securityId in statusMsg.SecurityIds)
			if (!securityId.SecurityCode.IsEmpty())
				result.Add(GetMarket(securityId).Symbol);
		return [.. result];
	}

	private static bool IsOrderMatch(ExecutionMessage order,
		OrderStatusSubscription filter)
	{
		if (filter.Symbols is { Length: > 0 } &&
			!filter.Symbols.Contains(order.SecurityId.SecurityCode,
				StringComparer.Ordinal))
			return false;
		if (filter.OrderId is long orderId &&
			!orderId.ToString(CultureInfo.InvariantCulture).Equals(
				order.OrderStringId, StringComparison.Ordinal))
			return false;
		if (!filter.OrderStringId.IsEmpty() &&
			!filter.OrderStringId.Equals(order.OrderStringId,
				StringComparison.Ordinal))
			return false;
		if (!filter.UserOrderId.IsEmpty() &&
			!filter.UserOrderId.Equals(order.UserOrderId,
				StringComparison.Ordinal))
			return false;
		if (filter.Side is Sides side && order.Side != side)
			return false;
		if (filter.Volume is decimal volume &&
			order.OrderVolume?.Abs() != volume.Abs())
			return false;
		if (filter.States is { Length: > 0 } &&
			(order.OrderState is not OrderStates state ||
				!filter.States.Contains(state)))
			return false;
		if (filter.From is DateTime from &&
			order.ServerTime < from.EnsureReyaUtc())
			return false;
		if (filter.To is DateTime to && order.ServerTime > to.EnsureReyaUtc())
			return false;
		return true;
	}

	private static bool IsTradeMatch(string symbol, Sides side, DateTime time,
		string orderId, string userOrderId,
		OrderStatusSubscription filter)
	{
		if (filter.Symbols is { Length: > 0 } &&
			!filter.Symbols.Contains(symbol, StringComparer.Ordinal))
			return false;
		if (filter.OrderId is long numericOrderId &&
			!numericOrderId.ToString(CultureInfo.InvariantCulture).Equals(orderId,
				StringComparison.Ordinal))
			return false;
		if (!filter.OrderStringId.IsEmpty() &&
			!filter.OrderStringId.Equals(orderId, StringComparison.Ordinal))
			return false;
		if (!filter.UserOrderId.IsEmpty() &&
			!filter.UserOrderId.Equals(userOrderId, StringComparison.Ordinal))
			return false;
		if (filter.Side is Sides expectedSide && side != expectedSide)
			return false;
		return (filter.From is null || time >= filter.From) &&
			(filter.To is null || time <= filter.To);
	}

	private Sides GetAccountTradeSide(ReyaSpotExecution trade)
	{
		var isMaker = IsOwnedAccount(trade.MakerAccountId) &&
			!IsOwnedAccount(trade.AccountId);
		return isMaker
			? trade.Side == ReyaSides.Buy ? Sides.Sell : Sides.Buy
			: trade.Side.ToStockSharp();
	}

	private string GetAccountOrderId(ReyaSpotExecution trade)
		=> IsOwnedAccount(trade.MakerAccountId) &&
			!IsOwnedAccount(trade.AccountId)
				? trade.MakerOrderId
				: trade.OrderId;

	private BigInteger GetAccountTradeAccountId(ReyaSpotExecution trade)
		=> IsOwnedAccount(trade.MakerAccountId) &&
			!IsOwnedAccount(trade.AccountId)
				? trade.MakerAccountId
				: trade.AccountId;

	private static string GetAccountTradeKey(ReyaPerpetualExecution trade)
		=> "P:" + trade.AccountId.ToString(CultureInfo.InvariantCulture) + ":" +
			trade.Symbol + ":" + trade.SequenceNumber.ToString(
				CultureInfo.InvariantCulture);

	private string GetAccountTradeKey(ReyaSpotExecution trade)
		=> "S:" + GetAccountTradeAccountId(trade).ToString(
			CultureInfo.InvariantCulture) + ":" + trade.Symbol + ":" +
			trade.SequenceNumber.ToString(CultureInfo.InvariantCulture);

	private void ValidateOrder(ReyaMarket market, decimal volume, decimal price,
		ReyaTimeInForces timeInForce, bool isReduceOnly)
	{
		if (volume < market.MinimumQuantity ||
			!IsMultipleOf(volume, market.QuantityStep))
			throw new InvalidOperationException(
				"Reya order volume must be at least " +
				market.MinimumQuantity.ToReyaDecimal() + " and a multiple of " +
				market.QuantityStep.ToReyaDecimal() + ".");
		if (price <= 0 || !IsMultipleOf(price, market.PriceStep))
			throw new InvalidOperationException(
				"Reya order price must be positive and a multiple of " +
				market.PriceStep.ToReyaDecimal() + ".");
		if (isReduceOnly && (market.IsSpot ||
			timeInForce == ReyaTimeInForces.GoodTillCancelled))
			throw new InvalidOperationException(
				"Reya reduce-only is available for perpetual IOC orders only.");
	}

	private decimal GetProtectivePrice(ReyaMarket market, Sides side)
	{
		var state = GetPriceState(market.Symbol);
		var reference = state?.PoolPrice ?? state?.OraclePrice;
		if (reference is not > 0)
			throw new InvalidOperationException(
				"Reya current price is unavailable for a protected market order.");
		var multiplier = side == Sides.Buy
			? 1m + MarketOrderSlippage / 100m
			: 1m - MarketOrderSlippage / 100m;
		var scaled = reference.Value * multiplier / market.PriceStep;
		return (side == Sides.Buy ? Math.Ceiling(scaled) : Math.Floor(scaled)) *
			market.PriceStep;
	}

	private static bool IsMultipleOf(decimal value, decimal step)
		=> step > 0 && value % step == 0;

	private static ReyaTimeInForces ToReyaTimeInForce(
		TimeInForce? timeInForce)
		=> timeInForce switch
		{
			null or TimeInForce.PutInQueue =>
				ReyaTimeInForces.GoodTillCancelled,
			TimeInForce.CancelBalance => ReyaTimeInForces.ImmediateOrCancel,
			TimeInForce.MatchOrCancel => throw new NotSupportedException(
				"Reya API v2 does not expose fill-or-kill orders."),
			_ => throw new ArgumentOutOfRangeException(nameof(timeInForce),
				timeInForce, null),
		};

	private bool IsOwnedAccount(BigInteger accountId)
	{
		if (_configuredAccountId == accountId)
			return true;
		using (_sync.EnterScope())
			return _accounts.Values.Contains(accountId);
	}

	private static string ResolveOrderId(string stringId, long? numericId)
	{
		if (!stringId.IsEmpty())
			return stringId.Trim();
		if (numericId is long id && id > 0)
			return id.ToString(CultureInfo.InvariantCulture);
		return null;
	}

	private static long ResolveClientOrderId(long transactionId,
		string userOrderId)
	{
		if (ParseClientOrderId(userOrderId) is long clientOrderId)
			return clientOrderId;
		return transactionId > 0
			? transactionId
			: throw new ArgumentOutOfRangeException(nameof(transactionId),
				transactionId, "Reya client order ID must be positive.");
	}

	private static long? ParseClientOrderId(string userOrderId)
	{
		if (userOrderId.IsEmpty())
			return null;
		if (!long.TryParse(userOrderId, NumberStyles.None,
			CultureInfo.InvariantCulture, out var result) || result <= 0)
			throw new InvalidOperationException(
				"Reya user order ID must be a positive 64-bit integer.");
		return result;
	}

	private static string PositionKey(BigInteger accountId, string symbol)
		=> accountId.ToString(CultureInfo.InvariantCulture) + ":" +
			symbol.ThrowIfEmpty(nameof(symbol));
}
