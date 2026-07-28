namespace StockSharp.Chainflip;

public partial class ChainflipMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var market = GetMarket(regMsg.SecurityId);
		if (regMsg.OrderType is not (null or OrderTypes.Market))
			throw new NotSupportedException(
				"Chainflip vault execution supports immediate market swaps " +
					"only.");
		if (regMsg.Side is not (Sides.Buy or Sides.Sell))
			throw new ArgumentOutOfRangeException(nameof(regMsg.Side));
		if (regMsg.Condition is not null)
			throw new NotSupportedException(
				"Chainflip vault swaps do not expose conditional orders.");
		if (regMsg.PostOnly == true)
			throw new NotSupportedException(
				"Post-only is not applicable to a cross-chain swap.");
		if (regMsg.TimeInForce is not null)
			throw new NotSupportedException(
				"Time-in-force is expressed by the configured Chainflip " +
					"retry duration.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException(
				"Use RetryDurationBlocks instead of an order expiry.");
		if (!regMsg.UserOrderId.IsEmpty())
			throw new NotSupportedException(
				"A Chainflip swap is identified by its source-chain " +
					"transaction hash.");

		var source = regMsg.Side == Sides.Sell
			? market.BaseAsset
			: market.QuoteAsset;
		var destination = regMsg.Side == Sides.Sell
			? market.QuoteAsset
			: market.BaseAsset;
		var evm = EnsureTradingReady(source);
		ValidatePortfolio(regMsg.PortfolioName);
		var requestedVolume = regMsg.Volume.Abs();
		if (requestedVolume <= 0)
			throw new InvalidOperationException(
				"Chainflip swap volume must be positive.");
		var sourceAmount = regMsg.Side == Sides.Sell
			? requestedVolume.ToBaseUnits(source.Decimals)
			: await GetBuySourceAmountAsync(market, requestedVolume,
				cancellationToken);
		if (sourceAmount <= 0)
			throw new InvalidOperationException(
				"Chainflip swap volume rounds to zero source units.");
		await ValidateBalanceAsync(evm, source, sourceAmount,
			cancellationToken);
		var quote = await HttpClient.GetQuoteAsync(source, destination,
			sourceAmount, true, cancellationToken);
		var destinationAddress = GetDestinationAddress(destination);
		var response = await HttpClient.BuildVaultSwapAsync(quote, source,
			destination, evm.WalletAddress, destinationAddress,
			SlippageTolerance, RetryDurationBlocks, cancellationToken);
		var wasApproved = await EnsureApprovalAsync(evm, source, response.To,
			sourceAmount, cancellationToken);
		if (wasApproved)
		{
			if (regMsg.Side == Sides.Buy)
				sourceAmount = await GetBuySourceAmountAsync(market,
					requestedVolume, cancellationToken);
			await ValidateBalanceAsync(evm, source, sourceAmount,
				cancellationToken);
			quote = await HttpClient.GetQuoteAsync(source, destination,
				sourceAmount, true, cancellationToken);
			response = await HttpClient.BuildVaultSwapAsync(quote, source,
				destination, evm.WalletAddress, destinationAddress,
				SlippageTolerance, RetryDurationBlocks,
				cancellationToken);
			_ = await EnsureApprovalAsync(evm, source, response.To,
				sourceAmount, cancellationToken);
		}

		var destinationAmount = quote.EgressAmount.ParseInteger();
		var transaction = new ChainflipTransaction
		{
			To = response.To.NormalizeAddress(),
			Data = response.Calldata.NormalizeData(),
			Value = response.Value.ParseInteger(),
		};
		var hash = await evm.SendTransactionAsync(transaction,
			cancellationToken);
		var price = GetSwapPrice(market, regMsg.Side, sourceAmount,
			destinationAmount);
		var tracked = new TrackedSwap
		{
			TransactionId = regMsg.TransactionId,
			TransactionHash = hash,
			Market = market,
			SourceAsset = source,
			DestinationAsset = destination,
			Side = regMsg.Side,
			SourceAmount = sourceAmount,
			ExpectedDestinationAmount = destinationAmount,
			RequestedVolume = requestedVolume,
			Volume = GetSwapVolume(market, regMsg.Side, sourceAmount,
				destinationAmount),
			Price = price,
			SubmittedTime = DateTime.UtcNow,
			State = OrderStates.Active,
		};
		using (_sync.EnterScope())
			_trackedSwaps[hash] = tracked;
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
			"A broadcast Chainflip vault transaction cannot be replaced " +
				"through the swap API.");
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		_ = cancelMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"Chainflip vault swaps are fire-and-forget and cannot be " +
				"cancelled after broadcast.");
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		_ = cancelMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"Chainflip has no cancellable open-order group.");
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
			BoardCode = BoardCodes.Chainflip,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId, true,
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
				"Chainflip swaps use transaction hashes, not numeric order " +
					"identifiers.");
		if (!statusMsg.UserId.IsEmpty())
			throw new NotSupportedException(
				"Chainflip vault swaps have no exchange-side user identifier.");
		if (statusMsg.SecurityIds.Length > 0)
			throw new NotSupportedException(
				"Use the primary security filter for Chainflip order status.");
		var hash = statusMsg.OrderStringId.IsEmpty()
			? null
			: statusMsg.OrderStringId.NormalizeHash();
		var subscription = new OrderSubscription
		{
			TransactionHash = hash,
			SecurityId = statusMsg.SecurityId,
			Side = statusMsg.Side,
			Volume = statusMsg.Volume,
			States = statusMsg.States,
			From = statusMsg.From?.ToUniversalTime(),
			To = statusMsg.To?.ToUniversalTime(),
			Skip = Math.Max(0, statusMsg.Skip ?? 0)
				.Min(int.MaxValue).To<int>(),
			Maximum = (statusMsg.Count ?? 1000)
				.Min(10000).Max(1).To<int>(),
		};
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

	private async ValueTask<BigInteger> GetBuySourceAmountAsync(
		ChainflipMarket market, decimal requestedVolume,
		CancellationToken cancellationToken)
	{
		var prices = await StateClient.GetPricesAsync(market, null,
			cancellationToken);
		if (requestedVolume <= 0 || prices.Ask <= 0)
			throw new InvalidOperationException(
				"Chainflip market-buy estimate must be positive.");
		var amount = (requestedVolume * prices.Ask).ToBaseUnitsCeiling(
			market.QuoteAsset.Decimals);
		return amount > 0
			? amount
			: throw new InvalidOperationException(
				"Chainflip market-buy estimate rounds to zero source units.");
	}

	private static async ValueTask ValidateBalanceAsync(
		ChainflipEvmClient client, ChainflipAsset source,
		BigInteger required, CancellationToken cancellationToken)
	{
		var actual = await client.GetBalanceAsync(source, cancellationToken);
		if (actual < required)
			throw new InvalidOperationException(
				$"Insufficient {source.Symbol} on {source.Chain}: {actual} " +
					$"base units available, {required} required.");
	}

	private async ValueTask<bool> EnsureApprovalAsync(
		ChainflipEvmClient client, ChainflipAsset source,
		string spender, BigInteger amount,
		CancellationToken cancellationToken)
	{
		if (source.IsNative)
			return false;
		if (amount <= 0)
			throw new InvalidOperationException(
				"Chainflip approval amount must be positive.");
		var allowance = await client.GetAllowanceAsync(source, spender,
			cancellationToken);
		if (allowance >= amount)
			return false;
		if (!IsAutoApprove)
			throw new InvalidOperationException(
				$"Token '{source.Symbol}' allowance for the Chainflip vault " +
					"is insufficient. Approve it manually or enable automatic " +
					"approval.");
		if (allowance > 0)
			await BroadcastAndConfirmAsync(client,
				client.CreateApprovalTransaction(source, spender,
					BigInteger.Zero), "approval reset", cancellationToken);
		await BroadcastAndConfirmAsync(client,
			client.CreateApprovalTransaction(source, spender, amount),
			"token approval", cancellationToken);
		return true;
	}

	private async ValueTask BroadcastAndConfirmAsync(
		ChainflipEvmClient client, ChainflipTransaction transaction,
		string operation, CancellationToken cancellationToken)
	{
		var hash = await client.SendTransactionAsync(transaction,
			cancellationToken);
		var receipt = await client.WaitForReceiptAsync(hash, ReceiptTimeout,
			cancellationToken);
		if (!IsSuccessful(receipt))
			throw new InvalidOperationException(
				$"Chainflip {operation} transaction '{hash}' reverted.");
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
		if (portfolioTargets.Length > 0)
		{
			var balances = await LoadBalancesAsync(cancellationToken);
			foreach (var target in portfolioTargets)
				await SendPortfolioSnapshotAsync(target, false, balances,
					cancellationToken);
		}
		foreach (var swap in active)
			await RefreshSwapAsync(swap, cancellationToken);
		foreach (var target in orderTargets)
			await SendOrderSnapshotAsync(target.Value, target.Key, false,
				cancellationToken);
	}

	private async ValueTask RefreshSwapAsync(TrackedSwap swap,
		CancellationToken cancellationToken)
	{
		var client = GetEvmClient(swap.SourceAsset.Chain) ?? throw new
			InvalidOperationException(
				$"No EVM client is configured for '{swap.SourceAsset.Chain}'.");
		var receipt = swap.Receipt ?? await client.GetReceiptAsync(
			swap.TransactionHash, cancellationToken);
		if (receipt is null)
			return;
		if (!IsSuccessful(receipt))
		{
			await ApplySwapStateAsync(swap, receipt, null,
				OrderStates.Failed, cancellationToken);
			return;
		}
		ChainflipSwapStatus status;
		try
		{
			status = await HttpClient.GetStatusAsync(swap.TransactionHash,
				cancellationToken);
		}
		catch (ChainflipApiException error) when (
			error.StatusCode == HttpStatusCode.NotFound)
		{
			using (_sync.EnterScope())
				swap.Receipt = receipt;
			return;
		}
		ValidateStatus(swap, status);
		var state = status.State?.ToUpperInvariant() switch
		{
			"COMPLETED" => OrderStates.Done,
			"FAILED" => OrderStates.Failed,
			"WAITING" or "RECEIVING" or "SWAPPING" or "SENDING" or
				"SENT" => OrderStates.Active,
			_ => throw new InvalidDataException(
				$"Unknown Chainflip swap state '{status.State}'."),
		};
		await ApplySwapStateAsync(swap, receipt, status, state,
			cancellationToken);
	}

	private async ValueTask ApplySwapStateAsync(TrackedSwap swap,
		ChainflipEvmReceipt receipt, ChainflipSwapStatus status,
		OrderStates state, CancellationToken cancellationToken)
	{
		var client = GetEvmClient(swap.SourceAsset.Chain);
		var executionTime = status?.LastStateChainUpdateAt is long updated
			? updated.ToUtcTime()
			: await client.GetBlockTimeAsync(
				receipt.BlockNumber.ParseInteger(), cancellationToken);
		var destinationAmount = swap.ExpectedDestinationAmount;
		if (state == OrderStates.Done)
		{
			var actual = status?.Swap?.SwappedOutputAmount;
			if (actual.IsEmpty())
				actual = status?.SwapEgress?.Amount;
			destinationAmount = actual.ParseInteger();
			if (destinationAmount <= 0)
				throw new InvalidDataException(
					"A completed Chainflip swap has no positive output " +
						"amount.");
		}
		var isOrderChanged = false;
		var isTradeRequired = false;
		using (_sync.EnterScope())
		{
			swap.Receipt = receipt;
			swap.Status = status;
			swap.ExecutionTime = executionTime;
			if (state == OrderStates.Done)
			{
				swap.Volume = GetSwapVolume(swap.Market, swap.Side,
					swap.SourceAmount, destinationAmount);
				swap.Price = GetSwapPrice(swap.Market, swap.Side,
					swap.SourceAmount, destinationAmount);
			}
			isOrderChanged = swap.State != state;
			swap.State = state;
			if (state == OrderStates.Done && !swap.IsTradeSent)
			{
				swap.IsTradeSent = true;
				isTradeRequired = true;
			}
		}
		if (isOrderChanged)
			await SendSwapOrderAsync(swap, swap.TransactionId,
				cancellationToken);
		if (isTradeRequired)
			await SendSwapTradeAsync(swap, swap.TransactionId,
				cancellationToken);
	}

	private static void ValidateStatus(TrackedSwap swap,
		ChainflipSwapStatus status)
	{
		ArgumentNullException.ThrowIfNull(status);
		if (!status.SourceChain.EqualsIgnoreCase(swap.SourceAsset.Chain) ||
			!status.SourceAsset.EqualsIgnoreCase(swap.SourceAsset.Symbol) ||
			!status.DestinationChain.EqualsIgnoreCase(
				swap.DestinationAsset.Chain) ||
			!status.DestinationAsset.EqualsIgnoreCase(
				swap.DestinationAsset.Symbol))
			throw new InvalidDataException(
				"Chainflip status assets do not match the tracked swap.");
	}

	private async ValueTask<(ChainflipAsset Asset,
		BigInteger Amount)[]> LoadBalancesAsync(
		CancellationToken cancellationToken)
	{
		if (_ethereumClient?.IsWalletConfigured != true ||
			_arbitrumClient?.IsWalletConfigured != true)
			throw new InvalidOperationException(
				"An EVM wallet is required for Chainflip balances.");
		var assets = ChainflipExtensions.Assets.Where(static asset =>
			asset.IsEvm).ToArray();
		var result = new List<(ChainflipAsset, BigInteger)>();
		foreach (var asset in assets)
		{
			var client = GetEvmClient(asset.Chain);
			result.Add((asset, await client.GetBalanceAsync(asset,
				cancellationToken)));
		}
		return [.. result];
	}

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		bool isForced, CancellationToken cancellationToken)
		=> await SendPortfolioSnapshotAsync(target, isForced,
			await LoadBalancesAsync(cancellationToken), cancellationToken);

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		bool isForced,
		(ChainflipAsset Asset, BigInteger Amount)[] balances,
		CancellationToken cancellationToken)
	{
		foreach (var item in balances)
		{
			var current = item.Amount.FromBaseUnits(item.Asset.Decimals);
			var fingerprint = new BalanceFingerprint(current, 0m);
			var code = $"{item.Asset.Symbol}@{item.Asset.Chain}"
				.ToUpperInvariant();
			var key = $"{target}:{code}";
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
					SecurityCode = code,
					BoardCode = BoardCodes.Chainflip,
				},
				ServerTime = CurrentTime,
				OriginalTransactionId = target,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, current, true)
			.TryAdd(PositionChangeTypes.BlockedValue, 0m, true),
				cancellationToken);
		}
	}

	private async ValueTask SendOrderSnapshotAsync(
		OrderSubscription subscription, long target, bool isForced,
		CancellationToken cancellationToken)
	{
		TrackedSwap[] swaps;
		using (_sync.EnterScope())
			swaps = [.. _trackedSwaps.Values.Where(swap =>
					Matches(subscription, swap))
				.OrderBy(static swap => swap.SubmittedTime)];
		var skipped = 0;
		var delivered = 0;
		foreach (var swap in swaps)
		{
			if (swap.State == OrderStates.Active)
				await RefreshSwapAsync(swap, cancellationToken);
			if (subscription.States is { Length: > 0 } states &&
				!states.Contains(swap.State))
				continue;
			if (skipped++ < subscription.Skip)
				continue;
			if (delivered++ >= subscription.Maximum)
				break;
			var key = $"{target}:{swap.TransactionHash}";
			var isOrderRequired = false;
			var isTradeRequired = false;
			using (_sync.EnterScope())
			{
				var isKnown = _orderFingerprints.TryGetValue(key,
					out var previous);
				isOrderRequired = isForced || !isKnown ||
					previous.State != swap.State;
				isTradeRequired = swap.State == OrderStates.Done &&
					(!isKnown || !previous.IsTradeSent);
				_orderFingerprints[key] = new(swap.State,
					(isKnown && previous.IsTradeSent) || isTradeRequired);
			}
			if (isOrderRequired)
				await SendSwapOrderAsync(swap, target, cancellationToken);
			if (isTradeRequired)
				await SendSwapTradeAsync(swap, target, cancellationToken);
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
			Commission = GetCommission(swap.Receipt),
			CommissionCurrency = "ETH",
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
			TradeStringId = swap.Status?.SwapId ??
				swap.TransactionHash,
			TradePrice = swap.Price,
			TradeVolume = swap.Volume,
			TransactionId = swap.TransactionId,
			OriginalTransactionId = target,
			Commission = GetCommission(swap.Receipt),
			CommissionCurrency = "ETH",
		}, cancellationToken);

	private static DateTime GetSwapTime(TrackedSwap swap)
		=> swap.ExecutionTime == default
			? swap.SubmittedTime
			: swap.ExecutionTime;

	private static decimal? GetCommission(ChainflipEvmReceipt receipt)
	{
		if (receipt?.GasUsed.IsEmpty() != false ||
			receipt.EffectiveGasPrice.IsEmpty())
			return null;
		var cost = receipt.GasUsed.ParseInteger() *
			receipt.EffectiveGasPrice.ParseInteger();
		return cost.FromBaseUnits(18);
	}

	internal static decimal GetSwapPrice(ChainflipMarket market,
		Sides side, BigInteger sourceAmount,
		BigInteger destinationAmount)
	{
		ArgumentNullException.ThrowIfNull(market);
		var volume = GetSwapVolume(market, side, sourceAmount,
			destinationAmount);
		var quote = (side == Sides.Sell
			? destinationAmount.FromBaseUnits(
				market.QuoteAsset.Decimals)
			: sourceAmount.FromBaseUnits(market.QuoteAsset.Decimals));
		if (volume <= 0 || quote <= 0)
			throw new InvalidDataException(
				"Chainflip returned non-positive swap amounts.");
		return quote / volume;
	}

	private static decimal GetSwapVolume(ChainflipMarket market,
		Sides side, BigInteger sourceAmount,
		BigInteger destinationAmount)
		=> (side == Sides.Sell
			? sourceAmount.FromBaseUnits(market.BaseAsset.Decimals)
			: destinationAmount.FromBaseUnits(market.BaseAsset.Decimals));

	private static bool Matches(OrderSubscription subscription,
		TrackedSwap swap)
	{
		if (!subscription.TransactionHash.IsEmpty() &&
			!subscription.TransactionHash.EqualsIgnoreCase(
				swap.TransactionHash))
			return false;
		if (!subscription.SecurityId.SecurityCode.IsEmpty() &&
			!subscription.SecurityId.SecurityCode.EqualsIgnoreCase(
				swap.Market.SecurityCode))
			return false;
		if (subscription.Side is Sides side && swap.Side != side)
			return false;
		if (subscription.Volume is decimal volume &&
			swap.RequestedVolume != volume)
			return false;
		return (subscription.From is null ||
				swap.SubmittedTime >= subscription.From) &&
			(subscription.To is null ||
				swap.SubmittedTime <= subscription.To);
	}

	private static bool IsSuccessful(ChainflipEvmReceipt receipt)
		=> receipt?.Status.IsEmpty() == false &&
			receipt.Status.ParseInteger() == BigInteger.One;

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
