namespace StockSharp.Nado;

using Native;

public partial class NadoMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(regMsg.PortfolioName);
		var market = GetMarket(regMsg.SecurityId);
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		var condition = regMsg.Condition as NadoOrderCondition ?? new();
		var execution = condition.ExecutionType;
		if (regMsg.PostOnly == true)
			execution = NadoOrderExecutionTypes.PostOnly;
		else if (condition.ExecutionType == NadoOrderExecutionTypes.Default)
			execution = regMsg.TimeInForce.ToNado(orderType == OrderTypes.Market);
		condition.ExecutionType = execution;
		if (orderType == OrderTypes.Market &&
			execution == NadoOrderExecutionTypes.PostOnly)
			throw new InvalidOperationException(
				"Nado market orders cannot be post-only.");
		var volume = regMsg.Volume.Abs();
		var price = orderType == OrderTypes.Market
			? GetProtectivePrice(market, regMsg.Side)
			: regMsg.Price;
		ValidateOrder(market, volume, price, condition);
		var expiry = GetOrderExpiry(regMsg.TillDate);
		var payload = CreatePlaceOrderPayload(market, regMsg.TransactionId,
			regMsg.Side, volume, price, expiry, execution, condition);
		await PlaceOrderAsync(payload, regMsg.TransactionId, market, regMsg.Side,
			volume, price, orderType, expiry, condition, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(replaceMsg.PortfolioName);
		var market = GetMarket(replaceMsg.SecurityId);
		var digest = ResolveDigest(replaceMsg.OldOrderStringId,
			replaceMsg.OldOrderId, replaceMsg.UserOrderId);
		var orderType = replaceMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		var condition = replaceMsg.Condition as NadoOrderCondition ?? new();
		var execution = condition.ExecutionType;
		if (replaceMsg.PostOnly == true)
			execution = NadoOrderExecutionTypes.PostOnly;
		else if (condition.ExecutionType == NadoOrderExecutionTypes.Default)
			execution = replaceMsg.TimeInForce.ToNado(
				orderType == OrderTypes.Market);
		condition.ExecutionType = execution;
		var volume = replaceMsg.Volume.Abs();
		var price = orderType == OrderTypes.Market
			? GetProtectivePrice(market, replaceMsg.Side)
			: replaceMsg.Price;
		ValidateOrder(market, volume, price, condition);
		var expiry = GetOrderExpiry(replaceMsg.TillDate);
		var cancel = CreateCancelTransaction(market.ProductId, digest);
		var place = CreatePlaceOrderPayload(market, replaceMsg.TransactionId,
			replaceMsg.Side, volume, price, expiry, execution, condition);
		var response = await RestClient.CancelAndPlaceAsync(new()
		{
			CancelTransaction = cancel,
			CancelSignature = Signer.SignCancellation(cancel, ChainId,
				_contracts.EndpointAddress),
			PlaceOrder = place,
			IsPlaceRequiresUnfilled = false,
		}, cancellationToken) ?? throw new InvalidDataException(
			"Nado returned no replacement order information.");
		if (!response.Error.IsEmpty())
			throw new InvalidOperationException(
				"Nado rejected the replacement order: " + response.Error);
		await AcceptPlacedOrderAsync(response.Digest, replaceMsg.TransactionId,
			market, replaceMsg.Side, volume, price, orderType, expiry, condition,
			place.Order, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		var digest = ResolveDigest(cancelMsg.OrderStringId, cancelMsg.OrderId,
			cancelMsg.UserOrderId);
		var tracked = GetTrackedOrder(digest);
		var market = !cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? GetMarket(cancelMsg.SecurityId)
			: tracked is not null
				? GetMarket(tracked.ProductId)
				: throw new InvalidOperationException(
					"Nado cancellation requires the order security.");
		var transaction = CreateCancelTransaction(market.ProductId, digest);
		var response = await RestClient.CancelOrdersAsync(new()
		{
			Transaction = transaction,
			Signature = Signer.SignCancellation(transaction, ChainId,
				_contracts.EndpointAddress),
		}, cancellationToken);
		var cancelled = response?.Orders?.FirstOrDefault(order =>
			order?.Digest.Equals(digest, StringComparison.OrdinalIgnoreCase) == true) ??
			tracked;
		if (cancelled is not null)
			TrackOrder(digest, GetTransactionId(digest), cancelled);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = DateTime.UtcNow,
			PortfolioName = _portfolioName,
			OrderStringId = digest,
			OrderState = OrderStates.Done,
			Balance = 0m,
			OriginalTransactionId = cancelMsg.TransactionId,
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
				"Nado bulk cancellation does not close positions.");
		if (cancelMsg.Side is not null || cancelMsg.IsStop is not null)
		{
			await CancelFilteredOrdersAsync(cancelMsg, cancellationToken);
			return;
		}

		var productIds = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? GetMarkets().Select(static market => market.ProductId).ToArray()
			: [GetMarket(cancelMsg.SecurityId).ProductId];
		if (productIds.Length == 0)
			return;
		var transaction = new NadoCancelProductsTransaction
		{
			Sender = _subaccount,
			ProductIds = productIds,
			Nonce = NadoSigner.CreateOrderNonce(),
		};
		await RestClient.CancelProductOrdersAsync(new()
		{
			Transaction = transaction,
			Signature = Signer.SignProductCancellation(transaction, ChainId,
				_contracts.EndpointAddress),
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
			_portfolioSubscriptionId = 0;
			return;
		}
		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = _portfolioName,
			BoardCode = BoardCodes.Nado,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId,
			cancellationToken);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId,
				cancellationToken);
			return;
		}
		_portfolioSubscriptionId = lookupMsg.TransactionId;
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
			_orderStatusSubscriptionId = 0;
			return;
		}
		await SendOrderSnapshotAsync(statusMsg, cancellationToken);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}
		_orderStatusSubscriptionId = statusMsg.TransactionId;
	}

	private async ValueTask PlaceOrderAsync(NadoPlaceOrderPayload payload,
		long transactionId, NadoMarket market, Sides side, decimal volume,
		decimal price, OrderTypes orderType, DateTime expiry,
		NadoOrderCondition condition, CancellationToken cancellationToken)
	{
		var response = await RestClient.PlaceOrderAsync(payload,
			cancellationToken) ?? throw new InvalidDataException(
				"Nado returned no placed-order information.");
		if (!response.Error.IsEmpty())
			throw new InvalidOperationException(
				"Nado rejected the order: " + response.Error);
		await AcceptPlacedOrderAsync(response.Digest, transactionId, market,
			side, volume, price, orderType, expiry, condition, payload.Order,
			cancellationToken);
	}

	private async ValueTask AcceptPlacedOrderAsync(string digest,
		long transactionId, NadoMarket market, Sides side, decimal volume,
		decimal price, OrderTypes orderType, DateTime expiry,
		NadoOrderCondition condition, NadoSignedOrder signedOrder,
		CancellationToken cancellationToken)
	{
		digest = digest.ThrowIfEmpty(nameof(digest));
		var order = new NadoOrder
		{
			ProductId = market.ProductId,
			Sender = signedOrder.Sender,
			Price = signedOrder.Price,
			Amount = signedOrder.Amount,
			Expiration = signedOrder.Expiration,
			Nonce = signedOrder.Nonce,
			UnfilledAmount = signedOrder.Amount,
			Digest = digest,
			PlacedAt = (long)(DateTime.UtcNow - DateTime.UnixEpoch)
				.TotalSeconds,
			OrderType = condition.ExecutionType.ToWire(),
			Appendix = signedOrder.Appendix,
		};
		TrackOrder(digest, transactionId, order, orderType);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = DateTime.UtcNow,
			PortfolioName = _portfolioName,
			Side = side,
			OrderVolume = volume,
			Balance = volume,
			OrderPrice = price,
			OrderType = orderType,
			OrderState = OrderStates.Pending,
			OrderStringId = digest,
			TransactionId = transactionId,
			OriginalTransactionId = transactionId,
			ExpiryDate = expiry,
			TimeInForce = condition.ExecutionType.ToStockSharp(),
			PositionEffect = condition.IsReduceOnly
				? OrderPositionEffects.CloseOnly
				: null,
			Condition = condition,
		}, cancellationToken);
	}

	private NadoPlaceOrderPayload CreatePlaceOrderPayload(NadoMarket market,
		long transactionId, Sides side, decimal volume, decimal price,
		DateTime expiry, NadoOrderExecutionTypes execution,
		NadoOrderCondition condition)
	{
		var appendix = NadoSigner.PackAppendix(execution,
			condition.IsReduceOnly, condition.IsIsolated,
			condition.IsolatedMargin, condition.BuilderId,
			condition.BuilderFeeRate);
		var amount = side == Sides.Buy ? volume : -volume;
		var order = new NadoSignedOrder
		{
			Sender = _subaccount,
			Price = price.ToX18("order price"),
			Amount = amount.ToX18("order amount"),
			Expiration = ((long)(expiry - DateTime.UnixEpoch).TotalSeconds)
				.ToString(CultureInfo.InvariantCulture),
			Nonce = NadoSigner.CreateOrderNonce(),
			Appendix = appendix,
		};
		return new()
		{
			Id = transactionId > 0 ? transactionId : null,
			ProductId = market.ProductId,
			Order = order,
			Signature = Signer.SignOrder(order, market.ProductId, ChainId),
			IsSpotLeverage = market.Type == NadoProductTypes.Spot
				? condition.IsSpotLeverage
				: null,
			IsBorrowMargin = condition.IsIsolated
				? condition.IsBorrowMargin
				: null,
		};
	}

	private NadoCancelTransaction CreateCancelTransaction(int productId,
		string digest)
		=> new()
		{
			Sender = _subaccount,
			ProductIds = [productId],
			Digests = [digest.ThrowIfEmpty(nameof(digest))],
			Nonce = NadoSigner.CreateOrderNonce(),
		};

	private async ValueTask CancelFilteredOrdersAsync(
		OrderGroupCancelMessage message, CancellationToken cancellationToken)
	{
		var productIds = message.SecurityId.SecurityCode.IsEmpty()
			? GetMarkets().Select(static market => market.ProductId).ToArray()
			: [GetMarket(message.SecurityId).ProductId];
		var response = await RestClient.GetOrdersAsync(_subaccount, productIds,
			cancellationToken);
		var orders = (response?.Products ?? [])
			.SelectMany(static product => product?.Orders ?? [])
			.Where(static order => order is not null)
			.Where(order => message.Side is null ||
				(order.Amount.TryParseAmount() is decimal amount &&
					(amount >= 0 ? Sides.Buy : Sides.Sell) == message.Side))
			.Where(order => message.IsStop is null || message.IsStop == false)
			.ToArray();
		if (orders.Length == 0)
			return;
		var transaction = new NadoCancelTransaction
		{
			Sender = _subaccount,
			ProductIds = [.. orders.Select(static order => order.ProductId)],
			Digests = [.. orders.Select(static order => order.Digest)],
			Nonce = NadoSigner.CreateOrderNonce(),
		};
		await RestClient.CancelOrdersAsync(new()
		{
			Transaction = transaction,
			Signature = Signer.SignCancellation(transaction, ChainId,
				_contracts.EndpointAddress),
		}, cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshotAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		var account = await RestClient.GetSubaccountAsync(_subaccount,
			cancellationToken) ?? throw new InvalidDataException(
				"Nado returned no subaccount information.");
		var time = DateTime.UtcNow;
		UpdateServerTime(time);
		var initial = account.Healths?.FirstOrDefault();
		if (initial is not null)
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = new()
				{
					SecurityCode = "USDT0",
					BoardCode = BoardCodes.Nado,
				},
				ServerTime = time,
				OriginalTransactionId = transactionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue,
				initial.Health.TryParseX18(), true)
			.TryAdd(PositionChangeTypes.BeginValue,
				initial.Assets.TryParseX18(), true)
			.TryAdd(PositionChangeTypes.BlockedValue,
				initial.Liabilities.TryParseX18(), true), cancellationToken);

		var current = new HashSet<int>();
		foreach (var balance in account.SpotBalances ?? [])
		{
			if (balance?.Balance is null || GetMarket(balance.ProductId) is null)
				continue;
			current.Add(balance.ProductId);
			await SendSpotBalanceAsync(balance, transactionId, time,
				cancellationToken);
		}
		foreach (var balance in account.PerpetualBalances ?? [])
		{
			if (balance?.Balance is null || GetMarket(balance.ProductId) is null)
				continue;
			current.Add(balance.ProductId);
			await SendPerpetualBalanceAsync(balance, transactionId, time,
				cancellationToken);
		}
		await SendMissingPositionsAsync(current, transactionId, time,
			cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		var productIds = GetOrderProductIds(statusMsg);
		var openResponse = await RestClient.GetOrdersAsync(_subaccount,
			productIds, cancellationToken);
		var open = (openResponse?.Products ?? [])
			.SelectMany(static product => product?.Orders ?? [])
			.Where(static order => order is not null && !order.Digest.IsEmpty())
			.ToArray();
		var openDigests = open.Select(static order => order.Digest)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var limit = (statusMsg.Count ?? HistoryLimit).Min(HistoryLimit).Max(1)
			.To<int>();
		var history = await RestClient.GetOrderHistoryAsync(_subaccount,
			productIds, limit, statusMsg.To, null, cancellationToken);
		var messages = open.Select(order => CreateOrderMessage(order,
			statusMsg.TransactionId)).Concat(history.Select(order =>
			CreateOrderMessage(order, openDigests.Contains(order.Digest),
				statusMsg.TransactionId)))
			.Where(static message => message is not null)
			.Where(message => IsOrderMatch(message, statusMsg))
			.GroupBy(static message => message.OrderStringId,
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.OrderByDescending(
				message => message.ServerTime).First())
			.OrderBy(static message => message.ServerTime)
			.Skip(Math.Max(0, statusMsg.Skip ?? 0).To<int>())
			.Take(limit)
			.ToArray();
		foreach (var message in messages)
		{
			UpdateServerTime(message.ServerTime);
			await SendOutMessageAsync(message, cancellationToken);
		}

		var orderByDigest = history
			.GroupBy(static order => order.Digest, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(static group => group.Key, static group => group.First(),
				StringComparer.OrdinalIgnoreCase);
		var matches = await RestClient.GetMatchesAsync(_subaccount, productIds,
			limit, statusMsg.To, null, cancellationToken);
		var transactions = (matches.Transactions ?? [])
			.Where(static transaction => transaction?.SubmissionIndex.IsEmpty() ==
				false && !transaction.Timestamp.IsEmpty())
			.ToArray();
		foreach (var match in (matches.Matches ?? [])
			.Where(static match => match is not null)
			.Where(match => orderByDigest.ContainsKey(match.Digest))
			.OrderBy(match => GetMatchTimestamp(match.SubmissionIndex,
				transactions) ?? string.Empty)
			.Skip(Math.Max(0, statusMsg.Skip ?? 0).To<int>())
			.Take(limit))
		{
			var order = orderByDigest[match.Digest];
			var trade = CreateMatchMessage(match, order, transactions,
				statusMsg.TransactionId);
			if (trade is not null && IsTradeMatch(trade, statusMsg))
			{
				UpdateServerTime(trade.ServerTime);
				await SendOutMessageAsync(trade, cancellationToken);
			}
		}
	}

	private async ValueTask OnFillAsync(NadoFillEvent fill,
		CancellationToken cancellationToken)
	{
		if (_subaccount.IsEmpty() ||
			!fill.Subaccount.Equals(_subaccount, StringComparison.OrdinalIgnoreCase) ||
			!TryAcceptFill(fill.SubmissionIndex, fill.OrderDigest))
			return;
		var market = GetMarket(fill.ProductId);
		if (market is null)
			return;
		var transactionId = GetTransactionId(fill.OrderDigest);
		var originalId = _orderStatusSubscriptionId != 0
			? _orderStatusSubscriptionId
			: transactionId;
		if (originalId == 0)
			return;
		var time = fill.Timestamp.FromNadoNanoseconds();
		UpdateServerTime(time);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = fill.IsBid ? Sides.Buy : Sides.Sell,
			OrderStringId = fill.OrderDigest,
			TradeStringId = fill.SubmissionIndex,
			TradePrice = fill.Price.ParseX18("fill price"),
			TradeVolume = fill.FilledQuantity.ParseAmount("fill quantity").Abs(),
			Commission = fill.Fee.TryParseAmount(),
			CommissionCurrency = market.QuoteAsset,
			TransactionId = transactionId,
			OriginalTransactionId = originalId,
		}, cancellationToken);
	}

	private ValueTask OnPositionChangeAsync(NadoPositionChangeEvent position,
		CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId == 0 ||
			!position.Subaccount.Equals(_subaccount,
				StringComparison.OrdinalIgnoreCase))
			return default;
		var market = GetMarket(position.ProductId);
		if (market is null)
			return default;
		var time = position.Timestamp.FromNadoNanoseconds();
		var amount = position.Amount.ParseAmount("position amount");
		UpdateServerTime(time);
		using (_sync.EnterScope())
			_knownPositions.Add(position.ProductId);
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = _portfolioSubscriptionId,
			Side = amount > 0 ? Sides.Buy : amount < 0 ? Sides.Sell : null,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, amount.Abs(), true)
		.TryAdd(PositionChangeTypes.AveragePrice,
			GetAveragePrice(amount, position.QuoteAmount.TryParseAmount()), true),
			cancellationToken);
	}

	private async ValueTask OnOrderUpdateAsync(NadoOrderUpdateEvent update,
		CancellationToken cancellationToken)
	{
		var market = GetMarket(update.ProductId);
		if (market is null)
			return;
		var transactionId = GetTransactionId(update.Digest);
		var originalId = _orderStatusSubscriptionId != 0
			? _orderStatusSubscriptionId
			: transactionId;
		if (originalId == 0)
			return;
		var tracked = GetTrackedOrder(update.Digest);
		if (tracked is null && update.Reason == NadoOrderUpdateReasons.Placed)
			try
			{
				tracked = await RestClient.GetOrderAsync(update.ProductId,
					update.Digest, cancellationToken);
				TrackOrder(update.Digest, transactionId, tracked);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				this.AddWarningLog("Cannot refresh Nado order {0}: {1}",
					update.Digest, error.Message);
			}
		var time = update.Timestamp.FromNadoNanoseconds();
		var amount = update.Amount.TryParseAmount()?.Abs();
		var message = tracked is null
			? new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = market.Symbol.ToStockSharp(),
				ServerTime = time,
				PortfolioName = _portfolioName,
				OrderStringId = update.Digest,
				OrderState = update.Reason == NadoOrderUpdateReasons.Placed
					? OrderStates.Active
					: amount is > 0 ? OrderStates.Active : OrderStates.Done,
				Balance = amount,
				TransactionId = transactionId,
				OriginalTransactionId = originalId,
			}
			: CreateOrderMessage(tracked, originalId);
		if (message is null)
			return;
		message.ServerTime = time;
		message.Balance = update.Reason == NadoOrderUpdateReasons.Cancelled
			? 0m
			: amount ?? message.Balance;
		message.OrderState = update.Reason == NadoOrderUpdateReasons.Cancelled ||
			update.Reason == NadoOrderUpdateReasons.Filled && message.Balance == 0
			? OrderStates.Done
			: OrderStates.Active;
		UpdateServerTime(time);
		await SendOutMessageAsync(message, cancellationToken);
	}

	private ValueTask SendSpotBalanceAsync(NadoSpotBalance balance,
		long transactionId, DateTime time,
		CancellationToken cancellationToken)
	{
		var market = GetMarket(balance.ProductId);
		if (market is null)
			return default;
		var amount = balance.Balance.Amount.ParseAmount("spot balance");
		using (_sync.EnterScope())
			_knownPositions.Add(balance.ProductId);
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
			Side = amount > 0 ? Sides.Buy : amount < 0 ? Sides.Sell : null,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, amount.Abs(), true),
			cancellationToken);
	}

	private ValueTask SendPerpetualBalanceAsync(NadoPerpetualBalance balance,
		long transactionId, DateTime time,
		CancellationToken cancellationToken)
	{
		var market = GetMarket(balance.ProductId);
		if (market is null)
			return default;
		var amount = balance.Balance.Amount.ParseAmount("perpetual balance");
		var quote = balance.Balance.QuoteBalance.TryParseAmount();
		using (_sync.EnterScope())
			_knownPositions.Add(balance.ProductId);
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
			Side = amount > 0 ? Sides.Buy : amount < 0 ? Sides.Sell : null,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, amount.Abs(), true)
		.TryAdd(PositionChangeTypes.AveragePrice,
			GetAveragePrice(amount, quote), true), cancellationToken);
	}

	private async ValueTask SendMissingPositionsAsync(HashSet<int> current,
		long transactionId, DateTime time,
		CancellationToken cancellationToken)
	{
		int[] missing;
		using (_sync.EnterScope())
		{
			missing = [.. _knownPositions.Where(id => !current.Contains(id))];
			_knownPositions.Clear();
			_knownPositions.UnionWith(current);
		}
		foreach (var productId in missing)
		{
			var market = GetMarket(productId);
			if (market is null)
				continue;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = market.Symbol.ToStockSharp(),
				ServerTime = time,
				OriginalTransactionId = transactionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, 0m, true),
				cancellationToken);
		}
	}

	private ExecutionMessage CreateOrderMessage(NadoOrder order,
		long originalTransactionId)
	{
		if (order is null || order.ProductId <= 0 || order.Digest.IsEmpty())
			return null;
		var market = GetMarket(order.ProductId);
		if (market is null)
			return null;
		var amount = order.Amount.ParseAmount("order amount");
		var balance = order.UnfilledAmount.TryParseAmount()?.Abs();
		var condition = NadoSigner.UnpackAppendix(order.Appendix);
		var expiry = order.Expiration.FromNadoOrderTime();
		var time = order.PlacedAt > 0
			? order.PlacedAt.FromNadoSeconds()
			: GetNonceTime(order.Nonce);
		var transactionId = GetTransactionId(order.Digest);
		TrackOrder(order.Digest, transactionId, order);
		return new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = amount >= 0 ? Sides.Buy : Sides.Sell,
			OrderVolume = amount.Abs(),
			Balance = balance,
			OrderPrice = order.Price.ParseX18("order price"),
			OrderType = GetOrderType(order.Digest),
			OrderState = balance is 0 ? OrderStates.Done : OrderStates.Active,
			OrderStringId = order.Digest,
			TransactionId = transactionId,
			OriginalTransactionId = originalTransactionId,
			ExpiryDate = expiry,
			TimeInForce = condition.ExecutionType.ToStockSharp(),
			PositionEffect = condition.IsReduceOnly
				? OrderPositionEffects.CloseOnly
				: null,
			Condition = condition,
		};
	}

	private ExecutionMessage CreateOrderMessage(NadoArchiveOrder order,
		bool isOpen, long originalTransactionId)
	{
		if (order is null || order.ProductId <= 0 || order.Digest.IsEmpty())
			return null;
		var market = GetMarket(order.ProductId);
		if (market is null)
			return null;
		var amount = order.Amount.ParseAmount("archive order amount");
		var filled = order.BaseFilled.TryParseAmount()?.Abs() ?? 0m;
		var volume = amount.Abs();
		var condition = NadoSigner.UnpackAppendix(order.Appendix);
		var expiry = order.Expiration.FromNadoOrderTime();
		var transactionId = GetTransactionId(order.Digest);
		var quoteFilled = order.QuoteFilled.TryParseAmount();
		var fee = order.Fee.TryParseAmount();
		var time = !order.LastFillTimestamp.IsEmpty() &&
			order.LastFillTimestamp != "0"
			? order.LastFillTimestamp.FromNadoSeconds()
			: GetNonceTime(order.Nonce);
		return new()
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = amount >= 0 ? Sides.Buy : Sides.Sell,
			OrderVolume = volume,
			Balance = isOpen ? (volume - filled).Max(0m) : 0m,
			OrderPrice = order.Price.ParseX18("archive order price"),
			AveragePrice = filled > 0 && quoteFilled is decimal quote
				? (quote + (fee ?? 0m)).Abs() / filled
				: null,
			OrderType = GetOrderType(order.Digest),
			OrderState = isOpen ? OrderStates.Active : OrderStates.Done,
			OrderStringId = order.Digest,
			TransactionId = transactionId,
			OriginalTransactionId = originalTransactionId,
			ExpiryDate = expiry,
			Commission = fee,
			TimeInForce = condition.ExecutionType.ToStockSharp(),
			PositionEffect = condition.IsReduceOnly
				? OrderPositionEffects.CloseOnly
				: null,
			Condition = condition,
		};
	}

	private ExecutionMessage CreateMatchMessage(NadoArchiveMatch match,
		NadoArchiveOrder order, NadoArchiveTransaction[] transactions,
		long originalTransactionId)
	{
		var market = GetMarket(order.ProductId);
		if (market is null || match.Order is null)
			return null;
		var baseFilled = match.BaseFilled.ParseAmount("match base amount").Abs();
		if (baseFilled <= 0)
			return null;
		var quoteFilled = match.QuoteFilled.ParseAmount("match quote amount");
		var fee = match.Fee.TryParseAmount() ?? 0m;
		var value = GetMatchTimestamp(match.SubmissionIndex, transactions);
		var timestamp = !value.IsEmpty()
			? value.FromNadoSeconds()
			: GetNonceTime(match.Order.Nonce);
		var amount = match.Order.Amount.ParseAmount("match order amount");
		return new()
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = timestamp,
			PortfolioName = _portfolioName,
			Side = amount >= 0 ? Sides.Buy : Sides.Sell,
			OrderStringId = match.Digest,
			TradeStringId = match.SubmissionIndex + ":" + match.Digest,
			TradePrice = (quoteFilled + fee).Abs() / baseFilled,
			TradeVolume = baseFilled,
			Commission = fee,
			CommissionCurrency = market.QuoteAsset,
			TransactionId = GetTransactionId(match.Digest),
			OriginalTransactionId = originalTransactionId,
		};
	}

	private static string GetMatchTimestamp(string submissionIndex,
		NadoArchiveTransaction[] transactions)
		=> transactions?.LastOrDefault(transaction =>
			transaction.SubmissionIndex.Equals(submissionIndex,
				StringComparison.Ordinal))?.Timestamp;

	private void ValidateOrder(NadoMarket market, decimal volume, decimal price,
		NadoOrderCondition condition)
	{
		var minimum = market.BookInfo.MinimumSize.ParseAmount("minimum size");
		var volumeStep = market.BookInfo.SizeIncrement.ParseAmount(
			"size increment");
		var priceStep = market.BookInfo.PriceIncrement.ParseX18(
			"price increment");
		if (volume < minimum || !IsMultipleOf(volume, volumeStep))
			throw new InvalidOperationException(
				"Nado order volume must be at least " + minimum.ToString(
					CultureInfo.InvariantCulture) + " and a multiple of " +
				volumeStep.ToString(CultureInfo.InvariantCulture) + ".");
		if (price <= 0 || !IsMultipleOf(price, priceStep))
			throw new InvalidOperationException(
				"Nado order price must be a positive multiple of " +
				priceStep.ToString(CultureInfo.InvariantCulture) + ".");
		if (!Enum.IsDefined(condition.ExecutionType))
			throw new InvalidOperationException(
				"Nado order execution type is invalid.");
		if (condition.IsIsolated && market.Type != NadoProductTypes.Perpetual)
			throw new InvalidOperationException(
				"Nado isolated margin is available only for perpetual markets.");
	}

	private decimal GetProtectivePrice(NadoMarket market, Sides side)
	{
		var state = GetPriceState(market.ProductId);
		var reference = side == Sides.Buy
			? state?.Ask ?? state?.Oracle ?? state?.Index ?? state?.Last
			: state?.Bid ?? state?.Oracle ?? state?.Index ?? state?.Last;
		if (reference is not > 0)
			throw new InvalidOperationException(
				"Nado current price is unavailable for a protected market order.");
		var tick = market.BookInfo.PriceIncrement.ParseX18("price increment");
		var multiplier = side == Sides.Buy
			? 1m + MarketOrderSlippage / 100m
			: 1m - MarketOrderSlippage / 100m;
		var scaled = reference.Value * multiplier / tick;
		return (side == Sides.Buy ? Math.Ceiling(scaled) : Math.Floor(scaled)) *
			tick;
	}

	private DateTime GetOrderExpiry(DateTime? tillDate)
	{
		var now = DateTime.UtcNow;
		var expiry = (tillDate ?? now.Add(OrderExpiry)).EnsureNadoUtc();
		if (expiry <= now || expiry > now.AddDays(30))
			throw new InvalidOperationException(
				"Nado order expiry must be in the future and no more than 30 days away.");
		return expiry;
	}

	private int[] GetOrderProductIds(OrderStatusMessage statusMsg)
	{
		var result = new HashSet<int>();
		if (!statusMsg.SecurityId.SecurityCode.IsEmpty())
			result.Add(GetMarket(statusMsg.SecurityId).ProductId);
		foreach (var securityId in statusMsg.SecurityIds)
			if (!securityId.SecurityCode.IsEmpty())
				result.Add(GetMarket(securityId).ProductId);
		return result.Count > 0
			? [.. result]
			: [.. GetMarkets().Select(static market => market.ProductId)];
	}

	private static bool IsOrderMatch(ExecutionMessage order,
		OrderStatusMessage filter)
	{
		if (filter.OrderId is not null)
			return false;
		if (!filter.OrderStringId.IsEmpty() &&
			!filter.OrderStringId.Equals(order.OrderStringId,
				StringComparison.OrdinalIgnoreCase))
			return false;
		if (filter.Side is Sides side && order.Side != side)
			return false;
		if (filter.Volume is decimal volume && order.OrderVolume != volume)
			return false;
		if (filter.States.Length > 0 &&
			(order.OrderState is not OrderStates state ||
				!filter.States.Contains(state)))
			return false;
		if (filter.From is DateTime from &&
			order.ServerTime < from.EnsureNadoUtc())
			return false;
		if (filter.To is DateTime to && order.ServerTime > to.EnsureNadoUtc())
			return false;
		return true;
	}

	private static bool IsTradeMatch(ExecutionMessage trade,
		OrderStatusMessage filter)
	{
		if (!filter.OrderStringId.IsEmpty() &&
			!filter.OrderStringId.Equals(trade.OrderStringId,
				StringComparison.OrdinalIgnoreCase))
			return false;
		if (filter.Side is Sides side && trade.Side != side)
			return false;
		if (filter.From is DateTime from &&
			trade.ServerTime < from.EnsureNadoUtc())
			return false;
		if (filter.To is DateTime to && trade.ServerTime > to.EnsureNadoUtc())
			return false;
		return true;
	}

	private void ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(_portfolioName))
			throw new InvalidOperationException(
				"Unknown Nado portfolio '" + portfolioName + "'.");
	}

	private long ChainId
		=> long.TryParse(_contracts?.ChainId, NumberStyles.None,
			CultureInfo.InvariantCulture, out var value) && value > 0
			? value
			: throw new InvalidOperationException("Nado chain ID is unavailable.");

	private static string ResolveDigest(string orderStringId, long? orderId,
		string userOrderId)
	{
		if (orderId is not null)
			throw new InvalidOperationException(
				"Nado orders use hexadecimal digest identifiers, not numeric IDs.");
		return (orderStringId.IsEmpty() ? userOrderId : orderStringId)
			.ThrowIfEmpty(nameof(orderStringId)).Trim();
	}

	private static DateTime GetNonceTime(string nonce)
	{
		if (!BigInteger.TryParse(nonce, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var value) || value < 0)
			return DateTime.UtcNow;
		var milliseconds = value >> 20;
		if (milliseconds < 0 || milliseconds > long.MaxValue)
			return DateTime.UtcNow;
		return DateTime.UnixEpoch.AddMilliseconds((long)milliseconds)
			.AddSeconds(-90);
	}

	private static decimal? GetAveragePrice(decimal amount, decimal? quote)
		=> amount == 0 || quote is null ? null : (-quote.Value / amount).Abs();

	private static bool IsMultipleOf(decimal value, decimal step)
		=> step > 0 && value % step == 0;
}
