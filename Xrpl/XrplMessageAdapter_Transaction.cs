namespace StockSharp.Xrpl;

public partial class XrplMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(regMsg.PortfolioName);
		var market = GetMarket(regMsg.SecurityId);
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		ValidateOrder(regMsg.Volume, regMsg.Price, orderType,
			regMsg.PostOnly, regMsg.VisibleVolume);
		var price = regMsg.Price;
		if (orderType == OrderTypes.Market)
			price = await GetProtectedMarketPriceAsync(market,
				regMsg.Side, cancellationToken);
		var tracked = await SubmitOfferAsync(market, regMsg.Side,
			price, regMsg.Volume, orderType, regMsg.TimeInForce,
			regMsg.PostOnly, regMsg.TillDate, null,
			regMsg.TransactionId, cancellationToken);
		await SendTrackedOrderAsync(tracked, regMsg.TransactionId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		var tracked = ResolveOrder(cancelMsg.OrderId,
			cancelMsg.OrderStringId, cancelMsg.OriginalTransactionId);
		await _transactionGate.WaitAsync(cancellationToken);
		try
		{
			var state = await RpcClient.GetAccountStateAsync(
				Signer.WalletAddress, cancellationToken);
			var ledger = await RpcClient.GetLedgerAsync(null,
				cancellationToken);
			var fee = await RpcClient.GetFeeDropsAsync(FeeMultiplier,
				cancellationToken);
			var signed = Signer.SignCancel(tracked.OfferSequence,
				state.Sequence, checked(ledger.Index +
					(uint)LastLedgerOffset), fee);
			var result = await RpcClient.SubmitAsync(signed.Blob,
				cancellationToken);
			EnsureAccepted(result, signed.Hash);
			tracked.CancelHash = result.Hash.IsEmpty()
				? signed.Hash
				: result.Hash.ToUpperInvariant();
			tracked.UpdateTime = DateTime.UtcNow;
			tracked.Commission += fee / 1_000_000m;
		}
		finally
		{
			_transactionGate.Release();
		}
		await SendTrackedOrderAsync(tracked, cancelMsg.TransactionId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(replaceMsg.PortfolioName);
		var previous = ResolveOrder(replaceMsg.OldOrderId,
			replaceMsg.OldOrderStringId,
			replaceMsg.OriginalTransactionId);
		var market = GetMarket(replaceMsg.SecurityId);
		var orderType = replaceMsg.OrderType ?? OrderTypes.Limit;
		ValidateOrder(replaceMsg.Volume, replaceMsg.Price, orderType,
			replaceMsg.PostOnly, replaceMsg.VisibleVolume);
		var price = replaceMsg.Price;
		if (orderType == OrderTypes.Market)
			price = await GetProtectedMarketPriceAsync(market,
				replaceMsg.Side, cancellationToken);
		var tracked = await SubmitOfferAsync(market, replaceMsg.Side,
			price, replaceMsg.Volume, orderType,
			replaceMsg.TimeInForce, replaceMsg.PostOnly,
			replaceMsg.TillDate, previous.OfferSequence,
			replaceMsg.TransactionId, cancellationToken);
		previous.State = OrderStates.Done;
		previous.Balance = 0;
		previous.UpdateTime = tracked.Time;
		await SendTrackedOrderAsync(previous,
			replaceMsg.TransactionId, cancellationToken);
		await SendTrackedOrderAsync(tracked,
			replaceMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		if (cancelMsg.Mode.HasFlag(
			OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"XRPL spot cancellation cannot close positions.");
		TrackedOrder[] orders;
		using (_sync.EnterScope())
			orders =
			[
				.. _trackedOrders.Values.Where(order =>
					order.State == OrderStates.Active &&
					(cancelMsg.SecurityId == default ||
						cancelMsg.SecurityId.SecurityCode.IsEmpty() ||
						order.Market.SecurityCode.EqualsIgnoreCase(
							cancelMsg.SecurityId.SecurityCode)) &&
					(cancelMsg.Side is null ||
						order.Side == cancelMsg.Side))
			];

		foreach (var order in orders)
		{
			await _transactionGate.WaitAsync(cancellationToken);
			try
			{
				var state = await RpcClient.GetAccountStateAsync(
					Signer.WalletAddress, cancellationToken);
				var ledger = await RpcClient.GetLedgerAsync(null,
					cancellationToken);
				var fee = await RpcClient.GetFeeDropsAsync(
					FeeMultiplier, cancellationToken);
				var signed = Signer.SignCancel(order.OfferSequence,
					state.Sequence, checked(ledger.Index +
						(uint)LastLedgerOffset), fee);
				var result = await RpcClient.SubmitAsync(signed.Blob,
					cancellationToken);
				EnsureAccepted(result, signed.Hash);
				order.CancelHash = result.Hash.IsEmpty()
					? signed.Hash
					: result.Hash.ToUpperInvariant();
				order.UpdateTime = DateTime.UtcNow;
				order.Commission += fee / 1_000_000m;
			}
			finally
			{
				_transactionGate.Release();
			}
			await SendTrackedOrderAsync(order,
				cancelMsg.TransactionId, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);

		EnsureConnected();
		if (!lookupMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_portfolioSubscriptions.Remove(
					lookupMsg.OriginalTransactionId);
			return;
		}
		ValidatePortfolio(lookupMsg.PortfolioName);
		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = GetPortfolioName(),
			BoardCode = BoardCodes.Xrpl,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId, true,
			cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(lookupMsg,
				cancellationToken);
			await SendSubscriptionFinishedAsync(
				lookupMsg.TransactionId, cancellationToken);
			return;
		}

		using (_sync.EnterScope())
			_portfolioSubscriptions.Add(lookupMsg.TransactionId);
		await SendSubscriptionResultAsync(lookupMsg,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId,
			cancellationToken);

		EnsureConnected();
		if (!statusMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_orderSubscriptions.Remove(
					statusMsg.OriginalTransactionId);
			return;
		}
		if (statusMsg.Count is <= 0)
		{
			await CompleteOrderStatusAsync(statusMsg,
				cancellationToken);
			return;
		}
		ValidatePortfolio(statusMsg.PortfolioName);
		var subscription = new OrderSubscription
		{
			Hash = statusMsg.OrderStringId?.Trim(),
			SecurityId = statusMsg.SecurityId,
			Side = statusMsg.Side,
			States = statusMsg.States,
			From = statusMsg.From?.ToUniversalTime(),
			To = statusMsg.To?.ToUniversalTime(),
			Skip = Math.Max(0, statusMsg.Skip ?? 0)
				.Min(int.MaxValue).To<int>(),
			Maximum = (statusMsg.Count ?? 1000)
				.Min(1000).Max(1).To<int>(),
		};
		await RefreshOrdersAsync(cancellationToken);
		await SendOrderSnapshotAsync(subscription,
			statusMsg.TransactionId, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await CompleteOrderStatusAsync(statusMsg,
				cancellationToken);
			return;
		}

		using (_sync.EnterScope())
			_orderSubscriptions[statusMsg.TransactionId] =
				subscription;
		await SendSubscriptionResultAsync(statusMsg,
			cancellationToken);
	}

	private async ValueTask<TrackedOrder> SubmitOfferAsync(
		XrplMarket market, Sides side, decimal price, decimal volume,
		OrderTypes orderType, TimeInForce? timeInForce, bool? postOnly,
		DateTime? expiration, uint? offerSequence, long transactionId,
		CancellationToken cancellationToken)
	{
		await _transactionGate.WaitAsync(cancellationToken);
		try
		{
			var state = await RpcClient.GetAccountStateAsync(
				Signer.WalletAddress, cancellationToken);
			var ledger = await RpcClient.GetLedgerAsync(null,
				cancellationToken);
			var fee = await RpcClient.GetFeeDropsAsync(FeeMultiplier,
				cancellationToken);
			var signed = Signer.SignOffer(market, side, price, volume,
				orderType, timeInForce, postOnly, expiration,
				state.Sequence, checked(ledger.Index +
					(uint)LastLedgerOffset), fee, offerSequence);
			var result = await RpcClient.SubmitAsync(signed.Blob,
				cancellationToken);
			EnsureAccepted(result, signed.Hash);
			var tracked = new TrackedOrder
			{
				TransactionId = transactionId,
				Hash = result.Hash.IsEmpty()
					? signed.Hash
					: result.Hash.ToUpperInvariant(),
				OfferSequence = signed.Sequence,
				Market = market,
				Side = side,
				OrderType = orderType,
				TimeInForce = orderType == OrderTypes.Market
					? Messages.TimeInForce.CancelBalance
					: timeInForce,
				Price = price,
				Volume = volume,
				Balance = volume,
				Commission = fee / 1_000_000m,
				Time = DateTime.UtcNow,
				UpdateTime = DateTime.UtcNow,
				State = OrderStates.Active,
			};
			using (_sync.EnterScope())
				_trackedOrders[GetOrderKey(tracked.OfferSequence)] =
					tracked;
			return tracked;
		}
		finally
		{
			_transactionGate.Release();
		}
	}

	private async ValueTask<decimal> GetProtectedMarketPriceAsync(
		XrplMarket market, Sides side,
		CancellationToken cancellationToken)
	{
		var book = await RpcClient.GetBookAsync(market, 1,
			cancellationToken);
		var factor = MarketOrderProtection / 100m;
		return side == Sides.Buy
			? (book.Asks.FirstOrDefault()?.Price ??
				throw new InvalidOperationException(
					$"XRPL market '{market.SecurityCode}' has no asks.")) *
				(1m + factor)
			: (book.Bids.FirstOrDefault()?.Price ??
				throw new InvalidOperationException(
					$"XRPL market '{market.SecurityCode}' has no bids.")) *
				(1m - factor);
	}

	private static void ValidateOrder(decimal volume, decimal price,
		OrderTypes orderType, bool? postOnly, decimal? visibleVolume)
	{
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				$"XRPL does not support order type '{orderType}'.");
		if (volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(volume),
				volume, "XRPL order volume must be positive.");
		if (orderType == OrderTypes.Limit && price <= 0)
			throw new ArgumentOutOfRangeException(nameof(price),
				price, "XRPL limit-order price must be positive.");
		if (postOnly == true && orderType != OrderTypes.Limit)
			throw new NotSupportedException(
				"XRPL passive execution applies only to limit orders.");
		if (visibleVolume is > 0 && visibleVolume != volume)
			throw new NotSupportedException(
				"XRPL does not expose iceberg offers.");
	}

	private static void EnsureAccepted(XrplSubmitResult result,
		string expectedHash)
	{
		ArgumentNullException.ThrowIfNull(result);
		var code = result.EngineResult;
		if (code.IsEmpty() ||
			!(code.StartsWith("tes", StringComparison.Ordinal) ||
				code.EqualsIgnoreCase("terQUEUED")))
			throw new InvalidOperationException(
				$"XRPL rejected transaction '{expectedHash}' " +
					$"({code ?? "unknown"}): " +
					(result.Message ?? "request rejected"));
		if (!result.Hash.IsEmpty() &&
			!result.Hash.EqualsIgnoreCase(expectedHash))
			throw new InvalidDataException(
				"XRPL submit returned a different transaction hash.");
	}

	private TrackedOrder ResolveOrder(long? orderId, string orderStringId,
		long originalTransactionId)
	{
		TrackedOrder order = null;
		using (_sync.EnterScope())
		{
			if (orderId is long numeric &&
				numeric is > 0 and <= uint.MaxValue)
				_trackedOrders.TryGetValue(
					GetOrderKey((uint)numeric), out order);
			if (order is null && !orderStringId.IsEmpty())
			{
				if (uint.TryParse(orderStringId, NumberStyles.None,
					CultureInfo.InvariantCulture, out var sequence))
					_trackedOrders.TryGetValue(
						GetOrderKey(sequence), out order);
				order ??= _trackedOrders.Values.FirstOrDefault(item =>
					item.Hash.EqualsIgnoreCase(orderStringId) ||
					item.CancelHash.EqualsIgnoreCase(orderStringId));
			}
			if (order is null && originalTransactionId != 0)
				order = _trackedOrders.Values.FirstOrDefault(item =>
					item.TransactionId == originalTransactionId);
		}
		return order ?? throw new InvalidOperationException(
			"XRPL offer cannot be resolved from the cancellation request.");
	}

	private async ValueTask PollPrivateAsync(
		CancellationToken cancellationToken)
	{
		long[] portfolios;
		KeyValuePair<long, OrderSubscription>[] subscriptions;
		using (_sync.EnterScope())
		{
			portfolios = [.. _portfolioSubscriptions];
			subscriptions = [.. _orderSubscriptions];
		}

		foreach (var target in portfolios)
			await SendPortfolioSnapshotAsync(target, false,
				cancellationToken);

		await RefreshOrdersAsync(cancellationToken);

		foreach (var item in subscriptions)
			await SendOrderSnapshotAsync(item.Value, item.Key,
				cancellationToken);
	}

	private async ValueTask RefreshOrdersAsync(
		CancellationToken cancellationToken)
	{
		XrplMarket[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values];
		var offers = await RpcClient.GetAccountOffersAsync(
			Signer.WalletAddress, markets, cancellationToken);
		var activeBySequence = offers.ToDictionary(
			static offer => offer.Sequence);

		foreach (var offer in offers)
		{
			TrackedOrder tracked;
			decimal fill = 0;
			using (_sync.EnterScope())
			{
				if (!_trackedOrders.TryGetValue(
					GetOrderKey(offer.Sequence), out tracked))
				{
					tracked = new()
					{
						Hash = offer.Sequence.ToString(
							CultureInfo.InvariantCulture),
						OfferSequence = offer.Sequence,
						Market = offer.Market,
						Side = offer.Side,
						OrderType = OrderTypes.Limit,
						TimeInForce =
							Messages.TimeInForce.PutInQueue,
						Price = offer.Price,
						Volume = offer.Volume,
						Balance = offer.Balance,
						Time = DateTime.UtcNow,
						UpdateTime = DateTime.UtcNow,
						State = OrderStates.Active,
					};
					_trackedOrders.Add(
						GetOrderKey(offer.Sequence), tracked);
				}
				else
				{
					fill = Math.Max(0,
						tracked.Balance - offer.Balance);
					tracked.Balance = offer.Balance;
					tracked.UpdateTime = DateTime.UtcNow;
					tracked.State = OrderStates.Active;
				}
			}
			if (fill > 0)
				await SendTrackedTradeAsync(tracked, fill,
					CancellationToken.None);
		}

		TrackedOrder[] trackedOrders;
		using (_sync.EnterScope())
			trackedOrders =
			[
				.. _trackedOrders.Values.Where(static order =>
					order.State == OrderStates.Active)
			];

		foreach (var tracked in trackedOrders)
		{
			if (activeBySequence.ContainsKey(tracked.OfferSequence))
				continue;
			var hash = tracked.CancelHash.IsEmpty()
				? tracked.Hash
				: tracked.CancelHash;
			if (hash.IsEmpty() ||
				hash.Length != 64 ||
				!hash.All(Uri.IsHexDigit))
				continue;
			XrplTransactionStatus status;
			try
			{
				status = await RpcClient.GetTransactionAsync(hash,
					cancellationToken);
			}
			catch (InvalidOperationException error) when (
				error.Message.Contains("txnNotFound",
					StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			if (!status.Validated)
				continue;
			tracked.UpdateTime = status.Time ?? DateTime.UtcNow;
			if (!status.Result.EqualsIgnoreCase("tesSUCCESS"))
			{
				tracked.State = OrderStates.Failed;
				tracked.FailureReason = status.Result;
			}
			else
			{
				tracked.State = OrderStates.Done;
				tracked.Balance = 0;
			}
		}
	}

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		bool isForced, CancellationToken cancellationToken)
	{
		var balances = await RpcClient.GetBalancesAsync(
			Signer.WalletAddress, cancellationToken);

		foreach (var balance in balances)
		{
			var securityId = ToPositionSecurityId(balance.Asset);
			var key = $"{target}:{balance.Asset.Key}";
			var fingerprint = new BalanceFingerprint(balance.Current);
			using (_sync.EnterScope())
			{
				if (!isForced &&
					_balanceFingerprints.TryGetValue(key,
						out var previous) &&
					previous == fingerprint)
					continue;
				_balanceFingerprints[key] = fingerprint;
			}
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = GetPortfolioName(),
				SecurityId = securityId,
				ServerTime = DateTime.UtcNow,
				OriginalTransactionId = target,
			}
			.TryAdd(PositionChangeTypes.CurrentValue,
				balance.Current, true)
			.TryAdd(PositionChangeTypes.BlockedValue, 0m, true),
				cancellationToken);
		}
	}

	private SecurityId ToPositionSecurityId(XrplAsset asset)
	{
		if (asset.IsXrp)
			return new()
			{
				SecurityCode = "XRP",
				BoardCode = BoardCodes.Xrpl,
			};
		XrplAsset[] known;
		using (_sync.EnterScope())
			known =
			[
				.. _markets.Values.SelectMany(static market =>
					new[] { market.Base, market.Quote })
					.Where(candidate =>
						candidate.Symbol.EqualsIgnoreCase(
							asset.Symbol))
					.DistinctBy(static candidate => candidate.Key)
			];
		return new()
		{
			SecurityCode = known.Length <= 1
				? asset.Symbol
				: $"{asset.Symbol}@{asset.Issuer[..8]}",
			BoardCode = BoardCodes.Xrpl,
		};
	}

	private async ValueTask SendOrderSnapshotAsync(
		OrderSubscription subscription, long target,
		CancellationToken cancellationToken)
	{
		TrackedOrder[] orders;
		using (_sync.EnterScope())
			orders =
			[
				.. _trackedOrders.Values.Where(order =>
						Matches(subscription, order))
					.OrderBy(static order => order.Time)
			];
		var skipped = 0;
		var delivered = 0;

		foreach (var order in orders)
		{
			if (subscription.States is { Length: > 0 } states &&
				!states.Contains(order.State))
				continue;
			if (skipped++ < subscription.Skip)
				continue;
			if (delivered++ >= subscription.Maximum)
				break;
			await SendTrackedOrderAsync(order, target,
				cancellationToken);
		}
	}

	private ValueTask SendTrackedOrderAsync(TrackedOrder order,
		long target, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Market.ToStockSharp(),
			ServerTime = order.UpdateTime == default
				? order.Time
				: order.UpdateTime,
			PortfolioName = GetPortfolioName(),
			Side = order.Side,
			OrderId = order.OfferSequence,
			OrderStringId = order.Hash,
			OrderPrice = order.Price,
			OrderVolume = order.Volume,
			Balance = order.Balance,
			OrderType = order.OrderType,
			TimeInForce = order.TimeInForce,
			OrderState = order.State,
			TransactionId = order.TransactionId,
			OriginalTransactionId = target,
			Commission = order.Commission,
			CommissionCurrency = "XRP",
			Error = order.FailureReason.IsEmpty()
				? null
				: new InvalidOperationException(
					order.FailureReason),
		}, cancellationToken);

	private ValueTask SendTrackedTradeAsync(TrackedOrder order,
		decimal volume, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = order.Market.ToStockSharp(),
			ServerTime = DateTime.UtcNow,
			PortfolioName = GetPortfolioName(),
			Side = order.Side,
			OrderId = order.OfferSequence,
			OrderStringId = order.Hash,
			TradeStringId = $"{order.OfferSequence}:" +
				$"{order.Volume - order.Balance}",
			TradePrice = order.Price,
			TradeVolume = volume,
			TransactionId = order.TransactionId,
			CommissionCurrency = "XRP",
		}, cancellationToken);

	private static bool Matches(OrderSubscription subscription,
		TrackedOrder order)
	{
		if (!subscription.Hash.IsEmpty() &&
			!subscription.Hash.EqualsIgnoreCase(order.Hash) &&
			!subscription.Hash.EqualsIgnoreCase(
				order.OfferSequence.ToString(
					CultureInfo.InvariantCulture)))
			return false;
		if (!subscription.SecurityId.SecurityCode.IsEmpty() &&
			!subscription.SecurityId.SecurityCode.EqualsIgnoreCase(
				order.Market.SecurityCode))
			return false;
		if (subscription.Side is Sides side && order.Side != side)
			return false;
		return (subscription.From is null ||
				order.Time >= subscription.From) &&
			(subscription.To is null ||
				order.Time <= subscription.To);
	}

	private static string GetOrderKey(uint sequence)
		=> sequence.ToString(CultureInfo.InvariantCulture);

	private async ValueTask CompleteOrderStatusAsync(
		OrderStatusMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
