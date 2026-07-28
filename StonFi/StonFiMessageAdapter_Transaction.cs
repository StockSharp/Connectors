namespace StockSharp.StonFi;

public partial class StonFiMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(regMsg.PortfolioName);
		var market = GetMarket(regMsg.SecurityId);
		if (regMsg.OrderType is not (null or OrderTypes.Market))
			throw new NotSupportedException(
				"STON.fi AMM supports immediate market swaps only.");
		if (regMsg.Side is not (Sides.Buy or Sides.Sell))
			throw new ArgumentOutOfRangeException(nameof(regMsg.Side));
		if (regMsg.Condition is not null)
			throw new NotSupportedException(
				"STON.fi does not expose conditional orders.");
		if (regMsg.PostOnly == true)
			throw new NotSupportedException(
				"Post-only is not applicable to an AMM swap.");
		if (regMsg.TimeInForce is not null)
			throw new NotSupportedException(
				"Time-in-force is not applicable to an immediate swap.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException(
				"An expiry cannot be attached to a STON.fi swap.");
		if (!regMsg.UserOrderId.IsEmpty())
			throw new NotSupportedException(
				"A TON swap is identified by its query id and transaction " +
					"hash; a client order id cannot be embedded in it.");
		if (regMsg.TransactionId <= 0)
			throw new InvalidOperationException(
				"STON.fi transaction id must be positive.");

		var requestedVolume = regMsg.Volume.Abs();
		if (requestedVolume <= 0)
			throw new InvalidOperationException(
				"STON.fi swap volume must be positive.");
		var baseUnits = requestedVolume.ToBaseUnits(
			market.Asset0.GetDecimals());
		if (baseUnits <= 0)
			throw new InvalidOperationException(
				"STON.fi swap volume rounds to zero base-asset units.");

		var isBuy = regMsg.Side == Sides.Buy;
		var offer = isBuy ? market.Asset1 : market.Asset0;
		var ask = isBuy ? market.Asset0 : market.Asset1;
		var quote = await RestClient.SimulateSwapAsync(offer.Address,
			ask.Address, baseUnits, SlippageTolerance / 100m,
			market.Pool.Address, isBuy, cancellationToken);
		ValidateSimulation(market, quote, offer, ask);
		var offerUnits = quote.OfferUnits.ParseInteger("offer_units");
		var askUnits = quote.AskUnits.ParseInteger("ask_units");
		if (offerUnits <= 0 || askUnits <= 0)
			throw new InvalidDataException(
				"STON.fi simulation returned non-positive swap amounts.");
		StonAssetInfo walletAsset = null;
		if (!offer.IsNative())
		{
			walletAsset = await RestClient.GetWalletAssetAsync(
				TonClient.WalletAddress, offer.Address,
				cancellationToken);
			var balance = walletAsset.Balance.IsEmpty()
				? BigInteger.Zero
				: walletAsset.Balance.ParseInteger("balance");
			if (balance < offerUnits)
				throw new InvalidOperationException(
					$"Insufficient {offer.GetSymbol()} balance: {balance} " +
						$"base units available, {offerUnits} required.");
		}
		var queryId = checked((ulong)regMsg.TransactionId);
		var broadcast = await TonClient.SendSwapAsync(quote, offer,
			walletAsset, queryId, cancellationToken);
		var volume = (isBuy ? askUnits : offerUnits)
			.FromBaseUnits(market.Asset0.GetDecimals());
		var turnover = (isBuy ? offerUnits : askUnits)
			.FromBaseUnits(market.Asset1.GetDecimals());
		if (volume <= 0 || turnover <= 0)
			throw new InvalidDataException(
				"STON.fi simulation produces a non-positive execution.");
		decimal? commission = quote.FeeUnits.IsEmpty()
			? null
			: quote.FeeUnits.ParseInteger("fee_units")
				.FromBaseUnits(offer.GetDecimals());
		var tracked = new TrackedSwap
		{
			TransactionId = regMsg.TransactionId,
			QueryId = queryId,
			ExternalMessageHash = broadcast.ExternalMessageHash,
			TransactionHash = broadcast.ExternalMessageHash,
			Market = market,
			Quote = quote,
			Side = regMsg.Side,
			RequestedVolume = requestedVolume,
			Volume = volume,
			Price = turnover / volume,
			Commission = commission,
			CommissionCurrency = offer.GetSymbol(),
			SubmittedTime = DateTime.UtcNow,
			State = OrderStates.Active,
		};
		using (_sync.EnterScope())
			_trackedSwaps[tracked.ExternalMessageHash] = tracked;
		await SendSwapOrderAsync(tracked, regMsg.TransactionId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		_ = replaceMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"A broadcast STON.fi swap cannot be replaced.");
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		_ = cancelMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"STON.fi has no cancellable order book; a broadcast TON message " +
				"cannot be cancelled through the DEX API.");
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		_ = cancelMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"STON.fi has no open-order group to cancel.");
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
			{
				_portfolioSubscriptions.Remove(
					lookupMsg.OriginalTransactionId);
				RemoveFingerprintPrefix(_balanceFingerprints,
					lookupMsg.OriginalTransactionId);
			}
			return;
		}
		ValidatePortfolio(lookupMsg.PortfolioName);
		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = GetPortfolioName(),
			BoardCode = BoardCodes.StonFi,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId, true,
			cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(lookupMsg,
				cancellationToken);
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId,
				cancellationToken);
			return;
		}
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
		EnsureConnected();
		if (!statusMsg.IsSubscribe)
		{
			RemoveOrderSubscription(statusMsg.OriginalTransactionId);
			return;
		}
		if (statusMsg.Count is <= 0)
		{
			await CompleteOrderStatusAsync(statusMsg, cancellationToken);
			return;
		}
		ValidatePortfolio(statusMsg.PortfolioName);
		if (statusMsg.OrderId is not null)
			throw new NotSupportedException(
				"STON.fi swaps use transaction hashes, not numeric order " +
					"identifiers.");
		if (!statusMsg.UserId.IsEmpty())
			throw new NotSupportedException(
				"STON.fi has no exchange-side user identifier.");
		if (statusMsg.SecurityIds.Length > 0)
			throw new NotSupportedException(
				"Use the primary security filter for STON.fi order status.");
		var subscription = new OrderSubscription
		{
			TransactionHash = statusMsg.OrderStringId?.Trim(),
			SecurityId = statusMsg.SecurityId,
			Side = statusMsg.Side,
			States = statusMsg.States,
			From = statusMsg.From?.ToUniversalTime(),
			To = statusMsg.To?.ToUniversalTime(),
			Skip = Math.Max(0, statusMsg.Skip ?? 0).Min(int.MaxValue)
				.To<int>(),
			Maximum = (statusMsg.Count ?? 1000).Min(10000).Max(1)
				.To<int>(),
		};
		await LoadOperationHistoryAsync(subscription.From,
			subscription.To, cancellationToken);
		await SendOrderSnapshotAsync(subscription, statusMsg.TransactionId,
			true, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await CompleteOrderStatusAsync(statusMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_orderSubscriptions[statusMsg.TransactionId] = subscription;
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private async ValueTask PollPrivateAsync(
		CancellationToken cancellationToken)
	{
		long[] portfolioTargets;
		KeyValuePair<long, OrderSubscription>[] orderTargets;
		TrackedSwap[] active;
		using (_sync.EnterScope())
		{
			portfolioTargets = [.. _portfolioSubscriptions];
			orderTargets = [.. _orderSubscriptions];
			active = [.. _trackedSwaps.Values.Where(static swap =>
				swap.State == OrderStates.Active)];
		}
		foreach (var swap in active)
			await RefreshSwapAsync(swap, cancellationToken);
		if (portfolioTargets.Length > 0)
		{
			var balances = await LoadBalancesAsync(cancellationToken);
			foreach (var target in portfolioTargets)
				await SendPortfolioSnapshotAsync(target, false, balances,
					cancellationToken);
		}
		foreach (var item in orderTargets)
			await SendOrderSnapshotAsync(item.Value, item.Key, false,
				cancellationToken);
	}

	private async ValueTask RefreshSwapAsync(TrackedSwap swap,
		CancellationToken cancellationToken)
	{
		var status = await RestClient.GetSwapStatusAsync(
			swap.Quote.RouterAddress, TonClient.WalletAddress,
			swap.QueryId, cancellationToken);
		OrderStates? state = null;
		if (status?.Type.EqualsIgnoreCase("Found") == true)
		{
			if (!BigInteger.TryParse(status.ExitCode, NumberStyles.Integer,
				CultureInfo.InvariantCulture, out var exitCode))
				throw new InvalidDataException(
					$"STON.fi swap status contains invalid exit code " +
						$"'{status.ExitCode}'.");
			state = exitCode == 0
				? OrderStates.Done
				: OrderStates.Failed;
		}
		else if (DateTime.UtcNow - swap.SubmittedTime >=
			TransactionTimeout)
			state = OrderStates.Failed;
		if (state is null)
			return;

		var sendOrder = false;
		var sendTrade = false;
		using (_sync.EnterScope())
		{
			if (status?.TransactionHash is { Length: > 0 })
				swap.TransactionHash = Convert.ToHexString(
					status.TransactionHash).ToLowerInvariant();
			swap.ExecutionTime = DateTime.UtcNow;
			sendOrder = swap.State != state;
			swap.State = state.Value;
			if (swap.State == OrderStates.Done && !swap.IsTradeSent)
			{
				swap.IsTradeSent = true;
				sendTrade = true;
			}
		}
		if (sendOrder)
			await SendSwapOrderAsync(swap, swap.TransactionId,
				cancellationToken);
		if (sendTrade)
			await SendSwapTradeAsync(swap, swap.TransactionId,
				cancellationToken);
	}

	private async ValueTask<(StonAssetInfo Asset,
		BigInteger Balance)[]> LoadBalancesAsync(
		CancellationToken cancellationToken)
	{
		StonAssetInfo[] assets;
		using (_sync.EnterScope())
			assets =
			[
				.. _markets.Values
					.SelectMany(static market => new[]
					{
						market.Asset0,
						market.Asset1,
					})
					.GroupBy(static asset =>
						asset.Address.NormalizeTonAddress(),
						StringComparer.OrdinalIgnoreCase)
					.Select(static group => group.First())
			];
		var result = new List<(StonAssetInfo, BigInteger)>();
		foreach (var asset in assets)
		{
			var walletAsset = await RestClient.GetWalletAssetAsync(
				TonClient.WalletAddress, asset.Address,
				cancellationToken);
			var balance = walletAsset.Balance.IsEmpty()
				? BigInteger.Zero
				: walletAsset.Balance.ParseInteger("balance");
			if (balance < 0)
				throw new InvalidDataException(
					$"STON.fi returned a negative " +
						$"'{asset.GetSymbol()}' balance.");
			result.Add((asset, balance));
		}
		return [.. result];
	}

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		bool isForced, CancellationToken cancellationToken)
		=> await SendPortfolioSnapshotAsync(target, isForced,
			await LoadBalancesAsync(cancellationToken), cancellationToken);

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		bool isForced,
		(StonAssetInfo Asset, BigInteger Balance)[] balances,
		CancellationToken cancellationToken)
	{
		foreach (var item in balances)
		{
			var current = item.Balance.FromBaseUnits(
				item.Asset.GetDecimals());
			if (current == 0 && !item.Asset.IsNative())
				continue;
			var key = $"{target}:" +
				item.Asset.Address.NormalizeTonAddress();
			var fingerprint = new BalanceFingerprint(current);
			using (_sync.EnterScope())
			{
				if (!isForced && _balanceFingerprints.TryGetValue(key,
					out var previous) && previous == fingerprint)
					continue;
				_balanceFingerprints[key] = fingerprint;
			}
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = GetPortfolioName(),
				SecurityId = new()
				{
					SecurityCode = item.Asset.GetSymbol(),
					BoardCode = BoardCodes.StonFi,
				},
				ServerTime = DateTime.UtcNow,
				OriginalTransactionId = target,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, current, true)
			.TryAdd(PositionChangeTypes.BlockedValue, 0m, true),
				cancellationToken);
		}
	}

	private async ValueTask LoadOperationHistoryAsync(DateTime? from,
		DateTime? to, CancellationToken cancellationToken)
	{
		var until = to ?? DateTime.UtcNow;
		var since = from ?? until.AddDays(-7);
		if (until - since > TimeSpan.FromDays(30))
			since = until.AddDays(-30);
		var operations = await RestClient.GetOperationsAsync(
			TonClient.WalletAddress, since, until, cancellationToken);
		foreach (var operation in operations)
		{
			if (operation is null ||
				!operation.OperationType.EqualsIgnoreCase("swap") ||
				operation.PoolAddress.IsEmpty() ||
				operation.PoolTransactionHash.IsEmpty())
				continue;
			StonMarket market;
			using (_sync.EnterScope())
				_marketsByPool.TryGetValue(
					operation.PoolAddress.NormalizeTonAddress(),
					out market);
			if (market is null)
				continue;
			var asset0 = operation.Asset0Address;
			var asset1 = operation.Asset1Address;
			if (!asset0.SameTonAddress(market.Asset0.Address) ||
				!asset1.SameTonAddress(market.Asset1.Address))
				continue;
			var amount0 = operation.Asset0Amount.ParseInteger(
				"asset0_amount");
			var amount1 = operation.Asset1Amount.ParseInteger(
				"asset1_amount");
			Sides side;
			BigInteger volumeUnits;
			BigInteger quoteUnits;
			if (amount0 < 0 && amount1 > 0)
			{
				side = Sides.Sell;
				volumeUnits = BigInteger.Abs(amount0);
				quoteUnits = amount1;
			}
			else if (amount0 > 0 && amount1 < 0)
			{
				side = Sides.Buy;
				volumeUnits = amount0;
				quoteUnits = BigInteger.Abs(amount1);
			}
			else
				continue;
			var volume = volumeUnits.FromBaseUnits(
				market.Asset0.GetDecimals());
			var turnover = quoteUnits.FromBaseUnits(
				market.Asset1.GetDecimals());
			if (volume <= 0 || turnover <= 0)
				continue;
			if (!DateTime.TryParse(operation.PoolTimestamp,
				CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal |
					DateTimeStyles.AdjustToUniversal, out var time))
				time = until;
			var hash = operation.PoolTransactionHash.Trim()
				.ToLowerInvariant();
			using (_sync.EnterScope())
			{
				if (_trackedSwaps.ContainsKey(hash))
					continue;
				_trackedSwaps[hash] = new()
				{
					ExternalMessageHash = hash,
					TransactionHash = hash,
					Market = market,
					Side = side,
					RequestedVolume = volume,
					Volume = volume,
					Price = turnover / volume,
					SubmittedTime = time,
					ExecutionTime = time,
					State = operation.Success
						? OrderStates.Done
						: OrderStates.Failed,
					IsTradeSent = operation.Success,
				};
			}
		}
	}

	private async ValueTask SendOrderSnapshotAsync(
		OrderSubscription subscription, long target, bool isForced,
		CancellationToken cancellationToken)
	{
		TrackedSwap[] swaps;
		using (_sync.EnterScope())
			swaps =
			[
				.. _trackedSwaps.Values.Where(swap =>
						Matches(subscription, swap))
					.OrderBy(static swap => swap.SubmittedTime)
			];
		var skipped = 0;
		var delivered = 0;
		foreach (var swap in swaps)
		{
			if (subscription.States is { Length: > 0 } states &&
				!states.Contains(swap.State))
				continue;
			if (skipped++ < subscription.Skip)
				continue;
			if (delivered++ >= subscription.Maximum)
				break;
			var key = $"{target}:{swap.ExternalMessageHash}";
			var sendOrder = false;
			var sendTrade = false;
			using (_sync.EnterScope())
			{
				var known = _orderFingerprints.TryGetValue(key,
					out var previous);
				sendOrder = isForced || !known ||
					previous.State != swap.State;
				sendTrade = swap.State == OrderStates.Done &&
					(!known || !previous.IsTradeSent);
				_orderFingerprints[key] = new(swap.State,
					(known && previous.IsTradeSent) || sendTrade);
			}
			if (sendOrder)
				await SendSwapOrderAsync(swap, target,
					cancellationToken);
			if (sendTrade)
				await SendSwapTradeAsync(swap, target,
					cancellationToken);
		}
	}

	private ValueTask SendSwapOrderAsync(TrackedSwap swap, long target,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = swap.Market.ToStockSharp(),
			ServerTime = GetSwapTime(swap),
			PortfolioName = GetPortfolioName(),
			Side = swap.Side,
			OrderVolume = swap.RequestedVolume,
			Balance = swap.State == OrderStates.Active
				? swap.RequestedVolume
				: 0m,
			OrderPrice = swap.Price,
			OrderType = OrderTypes.Market,
			OrderState = swap.State,
			OrderStringId = swap.TransactionHash,
			TransactionId = swap.TransactionId,
			OriginalTransactionId = target,
			Commission = swap.Commission,
			CommissionCurrency = swap.CommissionCurrency,
		}, cancellationToken);

	private ValueTask SendSwapTradeAsync(TrackedSwap swap, long target,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = swap.Market.ToStockSharp(),
			ServerTime = GetSwapTime(swap),
			PortfolioName = GetPortfolioName(),
			Side = swap.Side,
			OrderStringId = swap.TransactionHash,
			TradeStringId = swap.TransactionHash,
			TradePrice = swap.Price,
			TradeVolume = swap.Volume,
			TransactionId = swap.TransactionId,
			OriginalTransactionId = target,
			Commission = swap.Commission,
			CommissionCurrency = swap.CommissionCurrency,
		}, cancellationToken);

	private static DateTime GetSwapTime(TrackedSwap swap)
		=> swap.ExecutionTime == default
			? swap.SubmittedTime
			: swap.ExecutionTime;

	private static bool Matches(OrderSubscription subscription,
		TrackedSwap swap)
	{
		if (!subscription.TransactionHash.IsEmpty() &&
			!subscription.TransactionHash.EqualsIgnoreCase(
				swap.TransactionHash) &&
			!subscription.TransactionHash.EqualsIgnoreCase(
				swap.ExternalMessageHash))
			return false;
		if (!subscription.SecurityId.SecurityCode.IsEmpty() &&
			!subscription.SecurityId.SecurityCode.EqualsIgnoreCase(
				swap.Market.SecurityCode))
			return false;
		if (subscription.Side is Sides side && swap.Side != side)
			return false;
		return (subscription.From is null ||
				swap.SubmittedTime >= subscription.From) &&
			(subscription.To is null ||
				swap.SubmittedTime <= subscription.To);
	}

	private void RemoveOrderSubscription(long target)
	{
		using (_sync.EnterScope())
		{
			_orderSubscriptions.Remove(target);
			RemoveFingerprintPrefix(_orderFingerprints, target);
		}
	}

	private static void RemoveFingerprintPrefix<TValue>(
		IDictionary<string, TValue> values, long target)
	{
		var prefix = target.ToString(CultureInfo.InvariantCulture) + ":";
		foreach (var key in values.Keys.Where(key =>
			key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
			values.Remove(key);
	}

	private async ValueTask CompleteOrderStatusAsync(
		OrderStatusMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
