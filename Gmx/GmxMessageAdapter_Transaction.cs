namespace StockSharp.Gmx;

public partial class GmxMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(regMsg.PortfolioName);
		if (!regMsg.UserOrderId.IsEmpty())
			throw new NotSupportedException(
				"GMX API v2 does not accept client order identifiers.");
		if (regMsg.PostOnly == true)
			throw new NotSupportedException(
				"GMX API v2 does not expose post-only order preparation.");

		var market = GetMarket(regMsg.SecurityId);
		var condition = regMsg.Condition as GmxOrderCondition ?? new();
		if (regMsg.PositionEffect == OrderPositionEffects.OpenOnly &&
			condition.OrderKind is (GmxOrderKinds.TakeProfit or
				GmxOrderKinds.StopLoss))
			throw new InvalidOperationException(
				"GMX take-profit and stop-loss orders cannot be open-only.");
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Volume),
				regMsg.Volume, "GMX order volume must be positive.");

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		var reportedOrderType = condition.OrderKind == GmxOrderKinds.Regular
			? orderType
			: OrderTypes.Conditional;
		ValidateTimeInForce(condition.OrderKind == GmxOrderKinds.Regular
			? orderType
			: OrderTypes.Conditional, regMsg.TimeInForce, regMsg.TillDate);
		GmxPrepareOrderRequest request;
		OrderPositionEffects? positionEffect;
		decimal price;
		if (market.IsSpotOnly)
		{
			if (regMsg.PositionEffect is not null)
				throw new NotSupportedException(
					"GMX spot swaps do not have a position effect.");
			request = CreateSwapRequest(market, regMsg.Side, volume,
				regMsg.Price, orderType, condition);
			positionEffect = null;
			price = ResolveOrderPrice(market, regMsg.Price, orderType,
				condition);
		}
		else
		{
			var isDecrease = regMsg.PositionEffect ==
				OrderPositionEffects.CloseOnly || condition.OrderKind is
				GmxOrderKinds.TakeProfit or GmxOrderKinds.StopLoss;
			if (isDecrease && condition.CollateralToken.IsEmpty())
				condition.CollateralToken = await ResolvePositionCollateralAsync(
					market, regMsg.Side == Sides.Sell, cancellationToken);
			request = CreatePositionRequest(market, regMsg.Side, volume,
				regMsg.Price, orderType, condition, isDecrease);
			positionEffect = isDecrease
				? OrderPositionEffects.CloseOnly
				: OrderPositionEffects.OpenOnly;
			price = ResolveOrderPrice(market, regMsg.Price, orderType,
				condition);
		}

		var prepared = await ApiClient.PrepareOrderAsync(request,
			cancellationToken) ?? throw new InvalidDataException(
			"GMX returned no prepared order.");
		await SubmitPreparedAsync(prepared, new()
		{
			RequestId = prepared.RequestId,
			TransactionId = regMsg.TransactionId,
			Operation = GmxPendingOperations.Create,
			MarketAddress = market.MarketAddress,
			Side = regMsg.Side,
			Volume = volume,
			Price = price,
			OrderType = reportedOrderType,
			PositionEffect = positionEffect,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(replaceMsg.PortfolioName);
		var orderId = ResolveOrderId(replaceMsg.OldOrderStringId,
			replaceMsg.OldOrderId, replaceMsg.UserOrderId);
		var existing = (await ApiClient.GetOrdersAsync(_walletAddress,
			cancellationToken)).FirstOrDefault(order => order is not null &&
			order.Key.Equals(orderId, StringComparison.OrdinalIgnoreCase)) ??
			throw new InvalidOperationException(
				"GMX order '" + orderId + "' is no longer active.");
		var market = GetMarketByAddress(existing.MarketAddress) ??
			throw new InvalidDataException(
				"GMX order references an unknown market.");
		if (!replaceMsg.SecurityId.SecurityCode.IsEmpty() &&
			GetMarket(replaceMsg.SecurityId).MarketAddress != market.MarketAddress)
			throw new InvalidOperationException(
				"Replacement security does not match the GMX order.");

		var apiOrderType = ParseOrderType(existing.OrderType);
		var referencePrice = replaceMsg.Price > 0
			? replaceMsg.Price
			: GetOrderPrice(existing, market);
		var condition = replaceMsg.Condition as GmxOrderCondition;
		var newTriggerPrice = condition?.TriggerPrice ??
			(replaceMsg.Price > 0 ? replaceMsg.Price : null);
		var sizingPrice = newTriggerPrice ?? referencePrice;
		var newSize = replaceMsg.Volume > 0
			? apiOrderType is GmxApiOrderTypes.MarketSwap or
				GmxApiOrderTypes.LimitSwap
					? replaceMsg.Volume.Abs().ToGmxScaled(
						GetToken(existing.InitialCollateralTokenAddress).Decimals,
						"replacement swap size")
					: (replaceMsg.Volume.Abs() * sizingPrice).ToGmxScaled(
						30, "replacement position size")
			: null;
		if (newSize is null && newTriggerPrice is null && condition is null)
			throw new InvalidOperationException(
				"GMX replacement does not change the order.");

		var prepared = await ApiClient.PrepareEditAsync(new()
		{
			OrderIds = [orderId],
			NewSize = newSize,
			NewTriggerPrice = newTriggerPrice?.ToGmxScaled(30,
				"replacement trigger price"),
			IsNewAutoCancel = condition?.IsAutoCancel,
			Mode = "express",
			From = _walletAddress,
		}, cancellationToken) ?? throw new InvalidDataException(
			"GMX returned no prepared order update.");
		var currentVolume = GetOrderVolume(existing, market);
		await SubmitPreparedAsync(prepared, new()
		{
			RequestId = prepared.RequestId,
			TransactionId = replaceMsg.TransactionId,
			Operation = GmxPendingOperations.Edit,
			OrderIds = [orderId],
			MarketAddress = market.MarketAddress,
			Side = GetOrderSide(existing, market, apiOrderType),
			Volume = replaceMsg.Volume > 0
				? replaceMsg.Volume.Abs()
				: currentVolume,
			Price = newTriggerPrice ?? referencePrice,
			OrderType = apiOrderType.ToStockSharp(),
			PositionEffect = apiOrderType.IsDecrease()
				? OrderPositionEffects.CloseOnly
				: apiOrderType is GmxApiOrderTypes.MarketSwap or
					GmxApiOrderTypes.LimitSwap
						? null
						: OrderPositionEffects.OpenOnly,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		var orderId = ResolveOrderId(cancelMsg.OrderStringId,
			cancelMsg.OrderId, cancelMsg.UserOrderId);
		var order = (await ApiClient.GetOrdersAsync(_walletAddress,
			cancellationToken)).FirstOrDefault(item => item is not null &&
			item.Key.Equals(orderId, StringComparison.OrdinalIgnoreCase)) ??
			throw new InvalidOperationException(
				"GMX order '" + orderId + "' is no longer active.");
		var market = GetMarketByAddress(order.MarketAddress) ??
			throw new InvalidDataException(
				"GMX order references an unknown market.");
		var orderType = ParseOrderType(order.OrderType);
		var prepared = await ApiClient.PrepareCancelAsync(new()
		{
			OrderIds = [orderId],
			Mode = "express",
			From = _walletAddress,
		}, cancellationToken) ?? throw new InvalidDataException(
			"GMX returned no prepared cancellation.");
		await SubmitPreparedAsync(prepared, new()
		{
			RequestId = prepared.RequestId,
			TransactionId = cancelMsg.TransactionId,
			Operation = GmxPendingOperations.Cancel,
			OrderIds = [orderId],
			MarketAddress = market.MarketAddress,
			Side = GetOrderSide(order, market, orderType),
			Volume = GetOrderVolume(order, market),
			Price = GetOrderPrice(order, market),
			OrderType = orderType.ToStockSharp(),
			PositionEffect = orderType.IsDecrease()
				? OrderPositionEffects.CloseOnly
				: null,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"GMX bulk cancellation does not close positions.");
		if (cancelMsg.SecurityTypes is { Length: > 0 } &&
			!cancelMsg.SecurityTypes.Contains(SecurityTypes.Future) &&
			!cancelMsg.SecurityTypes.Contains(SecurityTypes.CryptoCurrency))
			return;

		var marketAddress = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(cancelMsg.SecurityId).MarketAddress;
		var orders = (await ApiClient.GetOrdersAsync(_walletAddress,
			cancellationToken)).Where(static order => order?.Key.IsEmpty() == false)
			.Where(order => marketAddress.IsEmpty() ||
				order.MarketAddress.Equals(marketAddress,
					StringComparison.OrdinalIgnoreCase))
			.Where(order => cancelMsg.Side is null ||
				GetOrderSide(order, GetMarketByAddress(order.MarketAddress),
					ParseOrderType(order.OrderType)) == cancelMsg.Side)
			.Where(order => cancelMsg.IsStop is null ||
				IsConditional(ParseOrderType(order.OrderType)) ==
					cancelMsg.IsStop.Value)
			.ToArray();
		if (orders.Length == 0)
			return;

		var orderIds = orders.Select(static order => order.Key).ToArray();
		var prepared = await ApiClient.PrepareCancelAsync(new()
		{
			OrderIds = orderIds,
			Mode = "express",
			From = _walletAddress,
		}, cancellationToken) ?? throw new InvalidDataException(
			"GMX returned no prepared bulk cancellation.");
		var first = orders[0];
		var firstMarket = GetMarketByAddress(first.MarketAddress) ??
			throw new InvalidDataException(
				"GMX order references an unknown market.");
		var firstType = ParseOrderType(first.OrderType);
		await SubmitPreparedAsync(prepared, new()
		{
			RequestId = prepared.RequestId,
			TransactionId = cancelMsg.TransactionId,
			Operation = GmxPendingOperations.Cancel,
			OrderIds = orderIds,
			Orders = [.. orders.Select(CreatePendingOrder)],
			MarketAddress = firstMarket.MarketAddress,
			Side = GetOrderSide(first, firstMarket, firstType),
			Volume = GetOrderVolume(first, firstMarket),
			Price = GetOrderPrice(first, firstMarket),
			OrderType = firstType.ToStockSharp(),
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureAccountReady();
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
			BoardCode = BoardCodes.Gmx,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId,
			cancellationToken);
		if (!lookupMsg.IsHistoryOnly())
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
		EnsureAccountReady();
		ValidatePortfolio(statusMsg.PortfolioName);
		if (!statusMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_orderSubscriptions.Remove(statusMsg.OriginalTransactionId);
			return;
		}

		var subscription = CreateOrderStatusSubscription(statusMsg);
		await SendOrderSnapshotAsync(subscription, statusMsg.TransactionId,
			!statusMsg.IsHistoryOnly(), cancellationToken);
		if (!statusMsg.IsHistoryOnly())
			using (_sync.EnterScope())
				_orderSubscriptions.Add(statusMsg.TransactionId, subscription);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private GmxPrepareOrderRequest CreatePositionRequest(GmxMarket market,
		Sides side, decimal volume, decimal price, OrderTypes orderType,
		GmxOrderCondition condition, bool isDecrease)
	{
		if (orderType is not (OrderTypes.Market or OrderTypes.Limit or
			OrderTypes.Conditional))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		if (condition.OrderKind == GmxOrderKinds.StopMarket && isDecrease)
			throw new InvalidOperationException(
				"GMX stop-market orders can only increase a position.");
		if (condition.OrderKind is (GmxOrderKinds.TakeProfit or
			GmxOrderKinds.StopLoss) && !isDecrease)
			throw new InvalidOperationException(
				"GMX take-profit and stop-loss orders must decrease a position.");
		if (isDecrease && condition.OrderKind == GmxOrderKinds.Regular &&
			orderType != OrderTypes.Market)
			throw new InvalidOperationException(
				"A non-market GMX decrease requires TakeProfit or StopLoss.");

		var simpleOrderType = condition.OrderKind switch
		{
			GmxOrderKinds.StopMarket => "stop-market",
			GmxOrderKinds.TakeProfit => "take-profit",
			GmxOrderKinds.StopLoss => "stop-loss",
			GmxOrderKinds.Twap => "twap",
			GmxOrderKinds.Regular when orderType == OrderTypes.Market => "market",
			GmxOrderKinds.Regular when orderType == OrderTypes.Limit => "limit",
			_ => throw new InvalidOperationException(
				"GMX conditional orders require an advanced order kind."),
		};
		var triggerPrice = simpleOrderType is "limit" or "stop-market" or
			"take-profit" or "stop-loss"
				? condition.TriggerPrice ?? price
				: (decimal?)null;
		if (triggerPrice is <= 0)
			throw new ArgumentOutOfRangeException(nameof(condition.TriggerPrice),
				triggerPrice, "GMX conditional orders require a trigger price.");

		var sizingPrice = triggerPrice ?? GetReferencePrice(market);
		var sizeUsd = volume * sizingPrice;
		if (isDecrease)
		{
			if (sizeUsd <= 0)
				throw new ArgumentOutOfRangeException(nameof(sizeUsd));
		}
		else
			ValidatePositionSize(market, sizeUsd);
		var isLong = isDecrease ? side == Sides.Sell : side == Sides.Buy;
		var request = new GmxPrepareOrderRequest
		{
			Kind = isDecrease ? "decrease" : "increase",
			Symbol = market.Symbol,
			Direction = isLong ? "long" : "short",
			OrderType = simpleOrderType,
			Size = sizeUsd.ToGmxScaled(30, "position size"),
			TriggerPrice = triggerPrice?.ToGmxScaled(30, "trigger price"),
			Slippage = GetSlippageBasisPoints(condition),
			Mode = "express",
			From = _walletAddress,
			GasPaymentToken = NormalizeTokenSymbol(condition.GasPaymentToken),
			ReferralCode = condition.ReferralCode.IsEmpty()
				? null
				: condition.ReferralCode.Trim(),
			UiFeeReceiver = condition.UiFeeReceiver.IsEmpty()
				? null
				: condition.UiFeeReceiver.NormalizeGmxAddress("UI fee receiver"),
		};
		if (condition.OrderKind == GmxOrderKinds.Twap)
			request.TwapConfiguration = CreateTwapConfiguration(condition);

		if (isDecrease)
		{
			request.CollateralToken = NormalizeTokenSymbol(
				condition.CollateralToken, DefaultCollateralToken);
			request.ReceiveToken = NormalizeTokenSymbol(condition.ReceiveToken,
				request.CollateralToken);
			request.IsKeepLeverage = condition.IsKeepLeverage;
		}
		else
		{
			var collateral = GetToken(condition.CollateralToken.IsEmpty()
				? DefaultCollateralToken
				: condition.CollateralToken);
			var amount = condition.CollateralAmount ??
				GetDerivedCollateralAmount(market, collateral, sizeUsd);
			ValidateCollateral(market, collateral, amount);
			request.CollateralToken = collateral.Symbol;
			request.CollateralToPay = new()
			{
				Token = collateral.Symbol,
				Amount = amount.ToGmxScaled(collateral.Decimals,
					"collateral amount"),
			};
			request.ProtectionOrders = CreateProtectionOrders(condition, sizeUsd,
				isLong, sizingPrice);
		}
		return request;
	}

	private GmxPrepareOrderRequest CreateSwapRequest(GmxMarket market,
		Sides side, decimal volume, decimal price, OrderTypes orderType,
		GmxOrderCondition condition)
	{
		if (condition.OrderKind is not (GmxOrderKinds.Regular or
			GmxOrderKinds.Twap))
			throw new NotSupportedException(
				"GMX swaps support market, limit, and TWAP order preparation.");
		var simpleOrderType = condition.OrderKind == GmxOrderKinds.Twap
			? "twap"
			: orderType switch
			{
				OrderTypes.Market => "market",
				OrderTypes.Limit => "limit",
				_ => throw new NotSupportedException(
					LocalizedStrings.OrderUnsupportedType.Put(orderType, 0)),
			};
		var triggerPrice = simpleOrderType == "limit"
			? condition.TriggerPrice ?? price
			: (decimal?)null;
		if (triggerPrice is <= 0)
			throw new ArgumentOutOfRangeException(nameof(condition.TriggerPrice),
				triggerPrice, "GMX limit swaps require a trigger price.");

		var payToken = GetToken(condition.CollateralToken.IsEmpty()
			? side == Sides.Buy ? market.ShortToken.Symbol : market.LongToken.Symbol
			: condition.CollateralToken);
		var receiveToken = GetToken(condition.ReceiveToken.IsEmpty()
			? side == Sides.Buy ? market.LongToken.Symbol : market.ShortToken.Symbol
			: condition.ReceiveToken);
		if (payToken.Address.Equals(receiveToken.Address,
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"GMX swap payment and receive tokens must differ.");
		var rawAmount = volume.ToGmxScaled(payToken.Decimals, "swap size");
		var request = new GmxPrepareOrderRequest
		{
			Kind = "swap",
			Symbol = market.Symbol,
			OrderType = simpleOrderType,
			Size = rawAmount,
			TriggerPrice = triggerPrice?.ToGmxScaled(30, "swap trigger price"),
			Slippage = GetSlippageBasisPoints(condition),
			CollateralToken = payToken.Symbol,
			CollateralToPay = new() { Amount = rawAmount, Token = payToken.Symbol },
			ReceiveToken = receiveToken.Symbol,
			Mode = "express",
			From = _walletAddress,
			GasPaymentToken = NormalizeTokenSymbol(condition.GasPaymentToken),
			ReferralCode = condition.ReferralCode.IsEmpty()
				? null
				: condition.ReferralCode.Trim(),
			UiFeeReceiver = condition.UiFeeReceiver.IsEmpty()
				? null
				: condition.UiFeeReceiver.NormalizeGmxAddress("UI fee receiver"),
		};
		if (condition.OrderKind == GmxOrderKinds.Twap)
			request.TwapConfiguration = CreateTwapConfiguration(condition);
		return request;
	}

	private async ValueTask SubmitPreparedAsync(
		GmxPrepareOrderResponse prepared, PendingRequest pending,
		CancellationToken cancellationToken)
	{
		if (prepared?.Payload is null || prepared.RequestId.IsEmpty() ||
			prepared.Payload.BatchParameters is null ||
			prepared.Payload.RelayParameters is null)
			throw new InvalidDataException(
				"GMX prepare response is incomplete.");
		if (!string.Equals(prepared.RequestId, pending.RequestId,
			StringComparison.Ordinal))
			throw new InvalidDataException(
				"GMX prepare response request ID changed unexpectedly.");
		var signature = Signer.Sign(prepared, Network);
		var submitted = await ApiClient.SubmitOrderAsync(new()
		{
			Mode = prepared.Mode,
			RequestId = prepared.RequestId,
			Signature = signature,
			From = _walletAddress,
			IdempotencyKey = prepared.IdempotencyKey,
			Eip712Data = new()
			{
				BatchParameters = prepared.Payload.BatchParameters,
				RelayParameters = prepared.Payload.RelayParameters,
			},
		}, cancellationToken) ?? throw new InvalidDataException(
			"GMX returned no submission result.");
		if (!string.Equals(submitted.RequestId, prepared.RequestId,
			StringComparison.Ordinal))
			throw new InvalidDataException(
				"GMX submit response contains an unexpected request ID.");
		pending.LastStatus = submitted.Status;
		using (_sync.EnterScope())
			_pendingRequests.Add(pending.RequestId, pending);
		await SendPendingExecutionAsync(pending, submitted.Status,
			GetSubmissionError(submitted.Error), DateTime.UtcNow,
			cancellationToken);
	}

	private async ValueTask RefreshPendingRequestsAsync(
		CancellationToken cancellationToken)
	{
		PendingRequest[] requests;
		using (_sync.EnterScope())
			requests = [.. _pendingRequests.Values];
		foreach (var request in requests)
		{
			var status = await ApiClient.GetOrderStatusAsync(request.RequestId,
				cancellationToken) ?? throw new InvalidDataException(
				"GMX returned no order status.");
			if (!string.Equals(status.RequestId, request.RequestId,
				StringComparison.Ordinal))
				throw new InvalidDataException(
					"GMX returned an unexpected order status request ID.");
			foreach (var orderId in status.OrderKeys ?? [])
				TrackOrder(orderId, request.TransactionId);
			if ((status.OrderKeys?.Length ?? 0) > 0)
				request.OrderIds = status.OrderKeys;
			var time = ParseApiTime(status.UpdatedAt) ?? DateTime.UtcNow;
			UpdateServerTime(time);
			if (!string.Equals(status.Status, request.LastStatus,
				StringComparison.OrdinalIgnoreCase) || IsTerminalStatus(status.Status))
			{
				request.LastStatus = status.Status;
				await SendPendingExecutionAsync(request, status.Status,
					GetSubmissionError(status.Error, status.CancellationReason), time,
					cancellationToken);
			}
			if (IsTerminalStatus(status.Status))
				using (_sync.EnterScope())
					_pendingRequests.Remove(request.RequestId);
		}
	}

	private async ValueTask SendPendingExecutionAsync(PendingRequest request,
		string status, Exception error, DateTime time,
		CancellationToken cancellationToken)
	{
		if (request.Orders is { Length: > 0 })
		{
			foreach (var order in request.Orders)
				await SendPendingExecutionAsync(request, order, status, error, time,
					cancellationToken);
			return;
		}
		var market = GetMarketByAddress(request.MarketAddress);
		if (market is null)
			return;
		var ids = request.OrderIds is { Length: > 0 }
			? request.OrderIds
			: [request.RequestId];
		foreach (var orderId in ids)
		{
			TrackOrder(orderId, request.TransactionId);
			await SendPendingExecutionAsync(request, new()
			{
				OrderId = orderId,
				MarketAddress = request.MarketAddress,
				Side = request.Side,
				Volume = request.Volume,
				Price = request.Price,
				OrderType = request.OrderType,
				PositionEffect = request.PositionEffect,
			}, status, error, time, cancellationToken);
		}
	}

	private ValueTask SendPendingExecutionAsync(PendingRequest request,
		PendingOrder order, string status, Exception error, DateTime time,
		CancellationToken cancellationToken)
	{
		var market = GetMarketByAddress(order.MarketAddress);
		if (market is null)
			return default;
		TrackOrder(order.OrderId, request.TransactionId);
		var state = ToOrderState(request, status);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = order.Side,
			OrderVolume = order.Volume,
			Balance = state is OrderStates.Done or OrderStates.Failed
				? 0m
				: order.Volume,
			OrderPrice = order.Price,
			OrderType = order.OrderType,
			OrderState = state,
			OrderStringId = order.OrderId,
			TransactionId = request.TransactionId,
			OriginalTransactionId = request.TransactionId,
			PositionEffect = order.PositionEffect,
			Error = error,
		}, cancellationToken);
	}

	private static OrderStates ToOrderState(PendingRequest request,
		string status)
		=> status?.ToLowerInvariant() switch
		{
			"relay_failed" or "relay_reverted" or "cancelled" =>
				request.Operation == GmxPendingOperations.Cancel
					? OrderStates.Done
					: OrderStates.Failed,
			"created" => request.Operation == GmxPendingOperations.Cancel
				? OrderStates.Pending
				: OrderStates.Active,
			"executed" => request.Operation switch
			{
				GmxPendingOperations.Cancel => OrderStates.Done,
				GmxPendingOperations.Edit => OrderStates.Active,
				_ when request.OrderType == OrderTypes.Market => OrderStates.Done,
				_ => OrderStates.Active,
			},
			_ => OrderStates.Pending,
		};

	private static bool IsTerminalStatus(string status)
		=> status?.ToLowerInvariant() is "executed" or "cancelled" or
			"relay_failed" or "relay_reverted";

	private static Exception GetSubmissionError(GmxOrderError error,
		string reason = null)
	{
		var message = error?.Message;
		if (message.IsEmpty())
			message = reason;
		if (message.IsEmpty())
			return null;
		return new InvalidOperationException(error?.Code.IsEmpty() == false
			? error.Code + ": " + message
			: message);
	}

	private decimal ResolveOrderPrice(GmxMarket market, decimal price,
		OrderTypes orderType, GmxOrderCondition condition)
		=> condition.TriggerPrice is > 0
			? condition.TriggerPrice.Value
			: orderType == OrderTypes.Limit && price > 0
				? price
				: GetReferencePrice(market);

	private decimal GetReferencePrice(GmxMarket market)
	{
		var price = market.Ticker?.MarkPrice.TryParseGmxUsd() ??
			market.Ticker?.MaximumPrice.TryParseGmxUsd() ??
			market.Ticker?.MinimumPrice.TryParseGmxUsd();
		return price is > 0
			? price.Value
			: throw new InvalidOperationException(
				"GMX current price is unavailable for '" + market.Symbol + "'.");
	}

	private decimal GetDerivedCollateralAmount(GmxMarket market,
		GmxToken collateral, decimal sizeUsd)
	{
		if (DefaultLeverage > market.MaximumLeverage)
			throw new InvalidOperationException(
				"GMX leverage " + DefaultLeverage + " exceeds the " +
				market.Symbol + " maximum of " + market.MaximumLeverage + ".");
		var tokenPrice = GetTokenPrice(collateral);
		return sizeUsd / DefaultLeverage / tokenPrice;
	}

	private async ValueTask<string> ResolvePositionCollateralAsync(
		GmxMarket market, bool isLong, CancellationToken cancellationToken)
	{
		var addresses = (await ApiClient.GetPositionsAsync(_walletAddress,
			cancellationToken))
			.Where(position => position is not null && position.IsLong == isLong &&
				position.MarketAddress.Equals(market.MarketAddress,
					StringComparison.OrdinalIgnoreCase) &&
				position.SizeInUsd.TryParseGmxUsd() is > 0)
			.Select(static position => position.CollateralTokenAddress)
			.Where(static address => !address.IsEmpty())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (addresses.Length == 0)
			throw new InvalidOperationException(
				"No matching GMX position is open for '" + market.Symbol + "'.");
		if (addresses.Length > 1)
			throw new InvalidOperationException(
				"Multiple GMX position collateral tokens match '" +
				market.Symbol + "'; specify CollateralToken explicitly.");
		return GetToken(addresses[0]).Symbol;
	}

	private static decimal GetTokenPrice(GmxToken token)
	{
		var minimum = token.Prices?.MinimumPrice.TryParseGmxUsd();
		var maximum = token.Prices?.MaximumPrice.TryParseGmxUsd();
		if (minimum is > 0 && maximum is > 0)
			return (minimum.Value + maximum.Value) / 2m;
		if (minimum is > 0)
			return minimum.Value;
		if (maximum is > 0)
			return maximum.Value;
		if (token.IsStable)
			return 1m;
		throw new InvalidOperationException(
			"GMX current token price is unavailable for '" + token.Symbol + "'.");
	}

	private static void ValidatePositionSize(GmxMarket market, decimal sizeUsd)
	{
		if (sizeUsd <= 0 || market.MinimumPositionUsd > 0 &&
			sizeUsd < market.MinimumPositionUsd)
			throw new ArgumentOutOfRangeException(nameof(sizeUsd), sizeUsd,
				"GMX position value must be at least " +
				market.MinimumPositionUsd + " USD.");
	}

	private static void ValidateCollateral(GmxMarket market, GmxToken token,
		decimal amount)
	{
		if (amount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount), amount,
				"GMX collateral must be positive.");
		var value = amount * GetTokenPrice(token);
		if (market.MinimumCollateralUsd > 0 &&
			value < market.MinimumCollateralUsd)
			throw new ArgumentOutOfRangeException(nameof(amount), amount,
				"GMX collateral value must be at least " +
				market.MinimumCollateralUsd + " USD.");
	}

	private static GmxProtectionOrder[] CreateProtectionOrders(
		GmxOrderCondition condition, decimal sizeUsd, bool isLong,
		decimal referencePrice)
	{
		if (condition.OrderKind == GmxOrderKinds.Twap &&
			(condition.TakeProfitPrice is not null ||
			condition.StopLossPrice is not null))
			throw new InvalidOperationException(
				"GMX cannot combine TWAP with TP/SL sidecars.");
		var result = new List<GmxProtectionOrder>();
		if (condition.TakeProfitPrice is decimal takeProfit)
		{
			if (takeProfit <= 0 || isLong && takeProfit <= referencePrice ||
				!isLong && takeProfit >= referencePrice)
				throw new ArgumentOutOfRangeException(
					nameof(condition.TakeProfitPrice), takeProfit,
					"GMX take-profit price must be favorable to the position.");
			result.Add(new()
			{
				Type = "take-profit",
				TriggerPrice = takeProfit.ToGmxScaled(30,
					"take-profit price"),
				Size = sizeUsd.ToGmxScaled(30, "take-profit size"),
			});
		}
		if (condition.StopLossPrice is decimal stopLoss)
		{
			if (stopLoss <= 0 || isLong && stopLoss >= referencePrice ||
				!isLong && stopLoss <= referencePrice)
				throw new ArgumentOutOfRangeException(
					nameof(condition.StopLossPrice), stopLoss,
					"GMX stop-loss price must be adverse to the position.");
			result.Add(new()
			{
				Type = "stop-loss",
				TriggerPrice = stopLoss.ToGmxScaled(30,
					"stop-loss price"),
				Size = sizeUsd.ToGmxScaled(30, "stop-loss size"),
			});
		}
		return result.Count == 0 ? null : [.. result];
	}

	private static GmxTwapConfiguration CreateTwapConfiguration(
		GmxOrderCondition condition)
	{
		if (condition.TwapParts is < 2 or > 30)
			throw new ArgumentOutOfRangeException(nameof(condition.TwapParts),
				condition.TwapParts, "GMX TWAP parts must be between 2 and 30.");
		if (condition.TwapDuration <= TimeSpan.Zero ||
			condition.TwapDuration.TotalSeconds > int.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(condition.TwapDuration),
				condition.TwapDuration, "GMX TWAP duration is invalid.");
		return new()
		{
			Duration = checked((int)condition.TwapDuration.TotalSeconds),
			Parts = condition.TwapParts,
		};
	}

	private int GetSlippageBasisPoints(GmxOrderCondition condition)
	{
		var value = condition.Slippage ?? Slippage;
		if (value is < 0 or > 50)
			throw new ArgumentOutOfRangeException(nameof(condition.Slippage),
				value, "GMX slippage must be between zero and 50%.");
		return checked((int)decimal.Round(value * 100m, 0,
			MidpointRounding.AwayFromZero));
	}

	private string NormalizeTokenSymbol(string value, string fallback = null)
	{
		if (value.IsEmpty())
			value = fallback;
		return value.IsEmpty() ? null : GetToken(value).Symbol;
	}

	private static string ResolveOrderId(string stringId, long? numericId,
		string userOrderId)
	{
		if (numericId is not null)
			throw new InvalidOperationException(
				"GMX orders use bytes32 string identifiers.");
		if (!userOrderId.IsEmpty())
			throw new NotSupportedException(
				"GMX API v2 does not expose client order identifiers.");
		return stringId.ThrowIfEmpty(nameof(stringId)).Trim();
	}

	private static GmxApiOrderTypes ParseOrderType(int value)
		=> Enum.IsDefined(typeof(GmxApiOrderTypes), value)
				? (GmxApiOrderTypes)value
				: throw new InvalidDataException(
					"GMX returned unsupported order type " + value + ".");

	private static bool IsConditional(GmxApiOrderTypes orderType)
		=> orderType is GmxApiOrderTypes.LimitSwap or
			GmxApiOrderTypes.LimitIncrease or GmxApiOrderTypes.LimitDecrease or
			GmxApiOrderTypes.StopLossDecrease or GmxApiOrderTypes.StopIncrease;

	private static DateTime? ParseApiTime(string value)
		=> DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var result)
				? result
				: null;

	private async ValueTask RefreshAccountSubscriptionsAsync(
		CancellationToken cancellationToken)
	{
		long[] portfolioSubscriptions;
		KeyValuePair<long, OrderStatusSubscription>[] orderSubscriptions;
		using (_sync.EnterScope())
		{
			portfolioSubscriptions = [.. _portfolioSubscriptions];
			orderSubscriptions = [.. _orderSubscriptions];
		}

		if (portfolioSubscriptions.Length > 0)
		{
			var balancesTask = ApiClient.GetWalletBalancesAsync(_walletAddress,
				cancellationToken).AsTask();
			var positionsTask = ApiClient.GetPositionsAsync(_walletAddress,
				cancellationToken).AsTask();
			await Task.WhenAll(balancesTask, positionsTask);
			var balances = await balancesTask;
			var positions = await positionsTask;
			var missingPositions = UpdateKnownPositions(positions);
			foreach (var transactionId in portfolioSubscriptions)
				await SendPortfolioSnapshotAsync(balances, positions,
					transactionId, missingPositions, cancellationToken);
		}

		if (orderSubscriptions.Length > 0)
		{
			var ordersTask = ApiClient.GetOrdersAsync(_walletAddress,
				cancellationToken).AsTask();
			var tradesTask = ApiClient.SearchTradesAsync(new()
			{
				Address = _walletAddress,
				FromTimestamp = DateTime.UtcNow.AddHours(-1).ToGmxSeconds(),
				Limit = 250,
			}, cancellationToken).AsTask();
			await Task.WhenAll(ordersTask, tradesTask);
			var orders = await ordersTask;
			var newActions = new List<GmxTradeAction>();
			foreach (var action in (await tradesTask)?.Trades ?? [])
			{
				if (action?.Id.IsEmpty() != false)
					continue;
				using (_sync.EnterScope())
					if (_seenAccountTrades.Add(action.Id))
						newActions.Add(action);
			}
			foreach (var (transactionId, subscription) in orderSubscriptions)
			{
				foreach (var order in orders ?? [])
				{
					var message = CreateOrderMessage(order, transactionId);
					if (message is not null && IsOrderMatch(message, subscription))
						await SendOutMessageAsync(message, cancellationToken);
				}
				foreach (var action in newActions)
					foreach (var message in CreateTradeActionMessages(action,
						transactionId))
						if (IsOrderMatch(message, subscription))
							await SendOutMessageAsync(message, cancellationToken);
			}
		}
	}

	private async ValueTask SendPortfolioSnapshotAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		var balancesTask = ApiClient.GetWalletBalancesAsync(_walletAddress,
			cancellationToken).AsTask();
		var positionsTask = ApiClient.GetPositionsAsync(_walletAddress,
			cancellationToken).AsTask();
		await Task.WhenAll(balancesTask, positionsTask);
		await SendPortfolioSnapshotAsync(await balancesTask, await positionsTask,
			transactionId, null, cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshotAsync(
		GmxWalletBalance[] balances, GmxPosition[] positions, long transactionId,
		string[] missingPositions, CancellationToken cancellationToken)
	{
		var time = DateTime.UtcNow;
		UpdateServerTime(time);
		foreach (var balance in balances ?? [])
		{
			if (balance?.Symbol.IsEmpty() != false ||
				balance.Decimals is < 0 or > 36)
				continue;
			var value = balance.Balance.ParseGmxScaled(balance.Decimals,
				"wallet balance");
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = new()
				{
					SecurityCode = balance.Symbol,
					BoardCode = BoardCodes.Gmx,
				},
				ServerTime = time,
				OriginalTransactionId = transactionId,
			}.TryAdd(PositionChangeTypes.CurrentValue, value, true),
				cancellationToken);
		}

		var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var position in positions ?? [])
		{
			var market = GetMarketByAddress(position?.MarketAddress);
			if (market is null || position.Key.IsEmpty())
				continue;
			var key = PositionKey(market, position.IsLong, position.Key);
			current.Add(key);
			var positionTime = ParseUnixSeconds(position.IncreasedAtTime) ?? time;
			UpdateServerTime(positionTime);
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = market.ToStockSharp(),
				DepoName = position.Key,
				ServerTime = positionTime,
				OriginalTransactionId = transactionId,
				Side = position.IsLong ? Sides.Buy : Sides.Sell,
			}
			.TryAdd(PositionChangeTypes.CurrentValue,
				position.SizeInTokens.TryParseGmxScaled(
					market.IndexToken.Decimals), true)
			.TryAdd(PositionChangeTypes.AveragePrice,
				position.EntryPrice.TryParseGmxUsd(), true)
			.TryAdd(PositionChangeTypes.CurrentPrice,
				position.MarkPrice.TryParseGmxUsd(), true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL,
				position.PnlAfterFees.TryParseGmxUsd(true) ??
					position.Pnl.TryParseGmxUsd(true), true)
			.TryAdd(PositionChangeTypes.Leverage,
				position.Leverage.TryParseGmxScaled(4), true)
			.TryAdd(PositionChangeTypes.LiquidationPrice,
				position.LiquidationPrice.TryParseGmxUsd(), true),
				cancellationToken);
		}

		missingPositions ??= UpdateKnownPositions(current);
		foreach (var key in missingPositions)
		{
			var sideSeparator = key.IndexOf(':');
			var idSeparator = sideSeparator < 0
				? -1
				: key.IndexOf(':', sideSeparator + 1);
			if (sideSeparator <= 0 || idSeparator <= sideSeparator + 1 ||
				idSeparator + 1 >= key.Length)
				continue;
			var market = GetMarketByAddress(key[..sideSeparator]);
			if (market is null)
				continue;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = market.ToStockSharp(),
				DepoName = key[(idSeparator + 1)..],
				ServerTime = time,
				OriginalTransactionId = transactionId,
				Side = key[(sideSeparator + 1)..idSeparator] == "L"
					? Sides.Buy
					: Sides.Sell,
			}.TryAdd(PositionChangeTypes.CurrentValue, 0m, true),
				cancellationToken);
		}
	}

	private async ValueTask SendOrderSnapshotAsync(
		OrderStatusSubscription subscription, long transactionId, bool isMarkSeen,
		CancellationToken cancellationToken)
	{
		var ordersTask = ApiClient.GetOrdersAsync(_walletAddress,
			cancellationToken).AsTask();
		var actionsTask = ReadAccountActionsAsync(subscription.From,
			subscription.To, (subscription.Skip + subscription.Limit)
				.Max(1).Min(HistoryLimit), cancellationToken).AsTask();
		await Task.WhenAll(ordersTask, actionsTask);

		var messages = new List<ExecutionMessage>();
		foreach (var order in await ordersTask)
		{
			var message = CreateOrderMessage(order, transactionId);
			if (message is not null && IsOrderMatch(message, subscription))
				messages.Add(message);
		}
		foreach (var action in await actionsTask)
		{
			if (isMarkSeen && !action.Id.IsEmpty())
				using (_sync.EnterScope())
					_seenAccountTrades.Add(action.Id);
			foreach (var message in CreateTradeActionMessages(action,
				transactionId))
				if (IsOrderMatch(message, subscription))
					messages.Add(message);
		}
		foreach (var message in messages
			.OrderByDescending(static message => message.ServerTime)
			.Skip(subscription.Skip).Take(subscription.Limit)
			.OrderBy(static message => message.ServerTime))
		{
			UpdateServerTime(message.ServerTime);
			await SendOutMessageAsync(message, cancellationToken);
		}
	}

	private async ValueTask<GmxTradeAction[]> ReadAccountActionsAsync(
		DateTime? from, DateTime? to, int count,
		CancellationToken cancellationToken)
	{
		var result = new List<GmxTradeAction>();
		string cursor = null;
		while (result.Count < count)
		{
			var response = await ApiClient.SearchTradesAsync(new()
			{
				Address = _walletAddress,
				FromTimestamp = from?.ToGmxSeconds(),
				ToTimestamp = to?.ToGmxSeconds(),
				Limit = (count - result.Count).Min(1000),
				Cursor = cursor,
			}, cancellationToken);
			result.AddRange((response?.Trades ?? []).Where(static action =>
				action?.Id.IsEmpty() == false));
			if (response?.IsMoreAvailable != true || response.NextCursor.IsEmpty() ||
				response.NextCursor.Equals(cursor, StringComparison.Ordinal))
				break;
			cursor = response.NextCursor;
		}
		return [.. result.OrderBy(static action => action.Timestamp)
			.TakeLast(count)];
	}

	private ExecutionMessage CreateOrderMessage(GmxOrder order,
		long originalTransactionId)
	{
		if (order?.Key.IsEmpty() != false)
			return null;
		var market = GetMarketByAddress(order.MarketAddress);
		if (market is null)
			return null;
		var orderType = ParseOrderType(order.OrderType);
		var time = ParseUnixSeconds(order.UpdatedAtTime) ?? DateTime.UtcNow;
		var volume = GetOrderVolume(order, market);
		using (_sync.EnterScope())
			_knownOrders[order.Key] = order;
		return new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = GetOrderSide(order, market, orderType),
			OrderVolume = volume,
			Balance = volume,
			OrderPrice = GetOrderPrice(order, market),
			OrderType = orderType.ToStockSharp(),
			OrderState = OrderStates.Active,
			OrderStringId = order.Key,
			TransactionId = GetOriginalTransactionId(order.Key, 0),
			OriginalTransactionId = originalTransactionId,
			TimeInForce = TimeInForce.PutInQueue,
			PositionEffect = orderType.IsDecrease()
				? OrderPositionEffects.CloseOnly
				: orderType is GmxApiOrderTypes.MarketSwap or
					GmxApiOrderTypes.LimitSwap
						? null
						: OrderPositionEffects.OpenOnly,
			Condition = CreateOrderCondition(order, market, orderType),
			Error = order.IsFrozen
				? new InvalidOperationException("GMX order is frozen.")
				: null,
		};
	}

	private ExecutionMessage[] CreateTradeActionMessages(GmxTradeAction action,
		long originalTransactionId)
	{
		if (action?.Id.IsEmpty() != false || action.OrderKey.IsEmpty())
			return [];
		var market = GetMarketByAddress(action.MarketAddress);
		if (market is null)
			return [];
		var orderType = ParseOrderType(action.OrderType);
		var side = GetActionSide(action, market, orderType);
		var time = action.Timestamp > 0
			? action.Timestamp.FromGmxSeconds()
			: DateTime.UtcNow;
		var price = GetActionPrice(action, market);
		var volume = GetActionVolume(action, market, orderType, price);
		var state = action.EventName switch
		{
			GmxTradeEventNames.OrderCreated or
			GmxTradeEventNames.OrderUpdated => OrderStates.Active,
			GmxTradeEventNames.OrderFrozen => OrderStates.Active,
			_ => OrderStates.Done,
		};
		var transactionId = GetOriginalTransactionId(action.OrderKey, 0);
		var order = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = side,
			OrderVolume = volume,
			Balance = state == OrderStates.Active ? volume : 0m,
			OrderPrice = price,
			AveragePrice = action.EventName == GmxTradeEventNames.OrderExecuted
				? price
				: null,
			OrderType = orderType.ToStockSharp(),
			OrderState = state,
			OrderStringId = action.OrderKey,
			TransactionId = transactionId,
			OriginalTransactionId = originalTransactionId,
			PositionEffect = orderType.IsDecrease()
				? OrderPositionEffects.CloseOnly
				: orderType is GmxApiOrderTypes.MarketSwap or
					GmxApiOrderTypes.LimitSwap
						? null
						: OrderPositionEffects.OpenOnly,
			Error = !action.Reason.IsEmpty() &&
				action.EventName is (GmxTradeEventNames.OrderCancelled or
					GmxTradeEventNames.OrderFrozen)
					? new InvalidOperationException(action.Reason)
					: null,
		};
		if (action.EventName != GmxTradeEventNames.OrderExecuted)
			return [order];

		var collateral = TryGetToken(action.InitialCollateralTokenAddress);
		var commission = collateral is null
			? null
			: action.PositionFeeAmount.TryParseGmxScaled(collateral.Decimals);
		var trade = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = side,
			OrderStringId = action.OrderKey,
			TradeStringId = action.Id,
			TradePrice = price,
			TradeVolume = volume,
			Commission = commission,
			CommissionCurrency = commission is null ? null : collateral.Symbol,
			PnL = action.PnlUsd.TryParseGmxUsd(true),
			TransactionId = transactionId,
			OriginalTransactionId = originalTransactionId,
		};
		return [order, trade];
	}

	private GmxOrderCondition CreateOrderCondition(GmxOrder order,
		GmxMarket market, GmxApiOrderTypes orderType)
		=> new()
		{
			OrderKind = orderType switch
			{
				GmxApiOrderTypes.StopIncrease => GmxOrderKinds.StopMarket,
				GmxApiOrderTypes.LimitDecrease => GmxOrderKinds.TakeProfit,
				GmxApiOrderTypes.StopLossDecrease => GmxOrderKinds.StopLoss,
				_ => GmxOrderKinds.Regular,
			},
			TriggerPrice = order.TriggerPrice.ParseGmxInteger("trigger price") > 0
				? order.TriggerPrice.ParseGmxContractPrice(
					market.IndexToken.Decimals, "trigger price")
				: null,
			CollateralToken = TryGetToken(
				order.InitialCollateralTokenAddress)?.Symbol,
			IsAutoCancel = order.IsAutoCancel,
		};

	private decimal GetOrderPrice(GmxOrder order, GmxMarket market)
	{
		if (order.TriggerPrice.ParseGmxInteger("trigger price") > 0)
			return order.TriggerPrice.ParseGmxContractPrice(
				market.IndexToken.Decimals, "trigger price");
		if (order.AcceptablePrice.ParseGmxInteger("acceptable price") > 0)
			return order.AcceptablePrice.ParseGmxContractPrice(
				market.IndexToken.Decimals, "acceptable price");
		return GetReferencePrice(market);
	}

	private decimal GetOrderVolume(GmxOrder order, GmxMarket market)
	{
		var orderType = ParseOrderType(order.OrderType);
		if (orderType is GmxApiOrderTypes.MarketSwap or
			GmxApiOrderTypes.LimitSwap)
		{
			var token = GetToken(order.InitialCollateralTokenAddress);
			return order.InitialCollateralDeltaAmount.ParseGmxScaled(token.Decimals,
				"swap order amount");
		}
		var price = GetOrderPrice(order, market);
		return price > 0
			? order.SizeDeltaUsd.ParseGmxUsd("order size") / price
			: 0m;
	}

	private static Sides GetOrderSide(GmxOrder order, GmxMarket market,
		GmxApiOrderTypes orderType)
	{
		if (market is null)
			throw new InvalidDataException(
				"GMX order references an unknown market.");
		if (orderType is GmxApiOrderTypes.MarketSwap or
			GmxApiOrderTypes.LimitSwap)
		{
			if (order.InitialCollateralTokenAddress.Equals(
				market.ShortToken.Address, StringComparison.OrdinalIgnoreCase))
				return Sides.Buy;
			if (order.InitialCollateralTokenAddress.Equals(
				market.LongToken.Address, StringComparison.OrdinalIgnoreCase))
				return Sides.Sell;
		}
		return orderType.ToStockSharpSide(order.IsLong);
	}

	private Sides GetActionSide(GmxTradeAction action, GmxMarket market,
		GmxApiOrderTypes orderType)
	{
		if (orderType is GmxApiOrderTypes.MarketSwap or
			GmxApiOrderTypes.LimitSwap)
		{
			if (action.InitialCollateralTokenAddress.Equals(
				market.ShortToken.Address, StringComparison.OrdinalIgnoreCase))
				return Sides.Buy;
			if (action.InitialCollateralTokenAddress.Equals(
				market.LongToken.Address, StringComparison.OrdinalIgnoreCase))
				return Sides.Sell;
		}
		return orderType.ToStockSharpSide(action.IsLong ?? true);
	}

	private decimal GetActionPrice(GmxTradeAction action, GmxMarket market)
	{
		foreach (var raw in new[]
		{
			action.ExecutionPrice,
			action.TriggerPrice,
			action.AcceptablePrice,
		})
			if (raw.TryParseGmxScaled(0) is > 0)
				return raw.ParseGmxContractPrice(market.IndexToken.Decimals,
					"trade price");
		return GetReferencePrice(market);
	}

	private decimal GetActionVolume(GmxTradeAction action, GmxMarket market,
		GmxApiOrderTypes orderType, decimal price)
	{
		if (orderType is GmxApiOrderTypes.MarketSwap or
			GmxApiOrderTypes.LimitSwap)
		{
			var token = TryGetToken(action.InitialCollateralTokenAddress);
			return token is null
				? 0m
				: action.InitialCollateralDeltaAmount.TryParseGmxScaled(
					token.Decimals) ?? 0m;
		}
		var tokens = action.SizeDeltaInTokens.TryParseGmxScaled(
			market.IndexToken.Decimals);
		return tokens is > 0
			? tokens.Value
			: price > 0
				? (action.SizeDeltaUsd.TryParseGmxUsd() ?? 0m) / price
				: 0m;
	}

	private GmxToken TryGetToken(string address)
	{
		if (address.IsEmpty())
			return null;
		using (_sync.EnterScope())
			return _tokensByAddress.TryGetValue(address, out var token)
				? token
				: null;
	}

	private PendingOrder CreatePendingOrder(GmxOrder order)
	{
		var market = GetMarketByAddress(order.MarketAddress) ??
			throw new InvalidDataException(
				"GMX order references an unknown market.");
		var orderType = ParseOrderType(order.OrderType);
		return new()
		{
			OrderId = order.Key,
			MarketAddress = market.MarketAddress,
			Side = GetOrderSide(order, market, orderType),
			Volume = GetOrderVolume(order, market),
			Price = GetOrderPrice(order, market),
			OrderType = orderType.ToStockSharp(),
			PositionEffect = orderType.IsDecrease()
				? OrderPositionEffects.CloseOnly
				: null,
		};
	}

	private string[] UpdateKnownPositions(GmxPosition[] positions)
	{
		var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var position in positions ?? [])
		{
			var market = GetMarketByAddress(position?.MarketAddress);
			if (market is not null && position.Key.IsEmpty() == false)
				current.Add(PositionKey(market, position.IsLong, position.Key));
		}
		return UpdateKnownPositions(current);
	}

	private string[] UpdateKnownPositions(HashSet<string> current)
	{
		using (_sync.EnterScope())
		{
			var missing = _knownPositions.Where(key => !current.Contains(key))
				.ToArray();
			_knownPositions.Clear();
			_knownPositions.UnionWith(current);
			return missing;
		}
	}

	private OrderStatusSubscription CreateOrderStatusSubscription(
		OrderStatusMessage statusMsg)
	{
		if (statusMsg.OrderId is not null)
			throw new InvalidOperationException(
				"GMX orders use bytes32 string identifiers.");
		if (!statusMsg.UserOrderId.IsEmpty())
			throw new NotSupportedException(
				"GMX API v2 does not expose client order identifiers.");
		var from = statusMsg.From?.EnsureGmxUtc();
		var to = statusMsg.To?.EnsureGmxUtc();
		if (from is DateTime start && to is DateTime end && start > end)
			throw new ArgumentOutOfRangeException(nameof(statusMsg),
				"GMX order-history start time cannot be later than end time.");
		var markets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (!statusMsg.SecurityId.SecurityCode.IsEmpty())
			markets.Add(GetMarket(statusMsg.SecurityId).Symbol);
		foreach (var securityId in statusMsg.SecurityIds)
			if (!securityId.SecurityCode.IsEmpty())
				markets.Add(GetMarket(securityId).Symbol);
		return new()
		{
			Symbols = [.. markets],
			OrderStringId = statusMsg.OrderStringId,
			Side = statusMsg.Side,
			Volume = statusMsg.Volume,
			States = statusMsg.States ?? [],
			From = from,
			To = to,
			Skip = Math.Max(0, statusMsg.Skip ?? 0).To<int>(),
			Limit = (statusMsg.Count ?? HistoryLimit).Min(HistoryLimit)
				.Max(1).To<int>(),
		};
	}

	private static bool IsOrderMatch(ExecutionMessage message,
		OrderStatusSubscription filter)
	{
		if (filter.Symbols is { Length: > 0 } &&
			!filter.Symbols.Contains(message.SecurityId.SecurityCode,
				StringComparer.OrdinalIgnoreCase))
			return false;
		if (!filter.OrderStringId.IsEmpty() &&
			!filter.OrderStringId.Equals(message.OrderStringId,
				StringComparison.OrdinalIgnoreCase))
			return false;
		if (filter.Side is Sides side && message.Side != side)
			return false;
		var volume = message.OrderVolume ?? message.TradeVolume;
		if (filter.Volume is decimal expected && volume?.Abs() != expected.Abs())
			return false;
		if (filter.States is { Length: > 0 } &&
			(message.OrderState is not OrderStates state ||
				!filter.States.Contains(state)))
			return false;
		if (filter.From is DateTime from && message.ServerTime < from)
			return false;
		if (filter.To is DateTime to && message.ServerTime > to)
			return false;
		return true;
	}

	private static DateTime? ParseUnixSeconds(string value)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
			out var timestamp) && timestamp > 0
				? timestamp.FromGmxSeconds()
				: null;

	private static void ValidateTimeInForce(OrderTypes orderType,
		TimeInForce? timeInForce, DateTime? tillDate)
	{
		if (tillDate is not null)
			throw new NotSupportedException(
				"GMX API v2 does not expose order expiration in prepare requests.");
		if (timeInForce is null)
			return;
		if (orderType == OrderTypes.Market &&
			timeInForce != TimeInForce.CancelBalance ||
			orderType != OrderTypes.Market &&
			timeInForce != TimeInForce.PutInQueue)
			throw new NotSupportedException(
				"GMX market orders are IOC and resting orders are GTC.");
	}

	private static string PositionKey(GmxMarket market, bool isLong,
		string positionId)
		=> market.MarketAddress + ":" + (isLong ? "L" : "S") + ":" +
			positionId.ThrowIfEmpty(nameof(positionId));
}
