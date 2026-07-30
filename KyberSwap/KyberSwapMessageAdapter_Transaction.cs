namespace StockSharp.KyberSwap;

public partial class KyberSwapMessageAdapter
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
				"KyberSwap Aggregator supports immediate market swaps only.");
		if (regMsg.Side is not (Sides.Buy or Sides.Sell))
			throw new ArgumentOutOfRangeException(nameof(regMsg.Side));
		if (regMsg.Condition is not null)
			throw new NotSupportedException(
				"KyberSwap Aggregator does not expose conditional orders.");
		if (regMsg.PostOnly == true)
			throw new NotSupportedException(
				"Post-only is not applicable to an immediate swap.");
		if (regMsg.TimeInForce is not null)
			throw new NotSupportedException(
				"Time-in-force is not applicable to an immediate swap.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException(
				"An expiry cannot be attached to a KyberSwap route.");
		if (!regMsg.UserOrderId.IsEmpty())
			throw new NotSupportedException(
				"An on-chain swap is identified by its transaction hash; a " +
				"client-order identifier cannot be embedded in it.");

		var requestedVolume = regMsg.Volume.Abs();
		if (requestedVolume <= 0)
			throw new InvalidOperationException(
				"KyberSwap volume must be positive.");
		var requestedBaseAmount = requestedVolume.ToBaseUnits(
			market.BaseToken.Decimals);
		if (requestedBaseAmount <= 0)
			throw new InvalidOperationException(
				"KyberSwap volume rounds to zero base units.");

		var sourceToken = regMsg.Side == Sides.Sell
			? market.BaseToken
			: market.QuoteToken;
		var destinationToken = regMsg.Side == Sides.Sell
			? market.QuoteToken
			: market.BaseToken;
		var quote = await GetOrderQuoteAsync(market, regMsg.Side,
			requestedBaseAmount, cancellationToken);
		var balance = await RpcClient.GetBalanceAsync(sourceToken,
			cancellationToken);
		if (balance < quote.InputAmount)
			throw new InvalidOperationException(
				$"Insufficient {sourceToken.Symbol} balance: {balance} base " +
					$"units available, {quote.InputAmount} required.");
		var route = await HttpClient.GetRouteAsync(sourceToken.Address,
			destinationToken.Address, quote.InputAmount,
			RpcClient.WalletAddress, cancellationToken);
		_ = ValidateRoute(route, sourceToken, destinationToken,
			quote.InputAmount);
		var router = route.RouterAddress.NormalizeAddress();
		await RpcClient.VerifyContractAsync(router, "router",
			cancellationToken);
		var wasApproved = await EnsureApprovalAsync(sourceToken, router,
			quote.InputAmount, cancellationToken);
		if (wasApproved)
		{
			quote = await GetOrderQuoteAsync(market, regMsg.Side,
				requestedBaseAmount, cancellationToken);
			route = await HttpClient.GetRouteAsync(sourceToken.Address,
				destinationToken.Address, quote.InputAmount,
				RpcClient.WalletAddress, cancellationToken);
			_ = ValidateRoute(route, sourceToken, destinationToken,
				quote.InputAmount);
			router = route.RouterAddress.NormalizeAddress();
			await RpcClient.VerifyContractAsync(router, "router",
				cancellationToken);
			_ = await EnsureApprovalAsync(sourceToken, router,
				quote.InputAmount, cancellationToken);
		}

		var built = await HttpClient.BuildRouteAsync(route.RouteSummary,
			RpcClient.WalletAddress, SlippageTolerance * 100m,
			DateTime.UtcNow + TransactionLifetime, cancellationToken);
		var destinationAmount = ValidateBuildResponse(built, route,
			quote.InputAmount);
		var transaction = ToTransaction(built, router);
		var hash = await RpcClient.SendTransactionAsync(transaction,
			cancellationToken);
		var price = GetSwapPrice(market, regMsg.Side, quote.InputAmount,
			destinationAmount);
		var tracked = new TrackedSwap
		{
			TransactionId = regMsg.TransactionId,
			TransactionHash = hash,
			Market = market,
			Side = regMsg.Side,
			SourceToken = sourceToken,
			DestinationToken = destinationToken,
			SourceAmount = quote.InputAmount,
			RequestedVolume = requestedVolume,
			Volume = requestedVolume,
			Price = price,
			SubmittedTime = DateTime.UtcNow,
			State = OrderStates.Active,
		};
		using (_sync.EnterScope())
			_trackedSwaps[hash] = tracked;
		await SendSwapOrderAsync(tracked, regMsg.TransactionId, null,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		_ = replaceMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"A broadcast KyberSwap transaction cannot be replaced through " +
				"the Aggregator API.");
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		_ = cancelMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"KyberSwap Aggregator has no cancellable order book. Pending EVM " +
				"nonce replacement is a wallet operation and is not emulated as " +
				"a protocol cancellation.");
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		_ = cancelMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"KyberSwap Aggregator has no open-order group to cancel.");
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
			BoardCode = BoardCodes.KyberSwap,
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
				"KyberSwap swaps use EVM transaction hashes, not numeric order " +
				"identifiers.");
		if (!statusMsg.UserId.IsEmpty())
			throw new NotSupportedException(
				"KyberSwap Aggregator has no exchange-side user identifier.");
		if (statusMsg.SecurityIds.Length > 0)
			throw new NotSupportedException(
				"Use the primary security filter for KyberSwap order status.");
		var hash = statusMsg.OrderStringId.IsEmpty()
			? null
			: NormalizeTransactionHash(statusMsg.OrderStringId);
		var subscription = new OrderSubscription
		{
			TransactionHash = hash,
			SecurityId = statusMsg.SecurityId,
			Side = statusMsg.Side,
			Volume = statusMsg.Volume,
			States = statusMsg.States,
			From = statusMsg.From?.ToUniversalTime(),
			To = statusMsg.To?.ToUniversalTime(),
			Skip = Math.Max(0, statusMsg.Skip ?? 0).Min(int.MaxValue).To<int>(),
			Maximum = (statusMsg.Count ?? 1000).Min(10000).Max(1).To<int>(),
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

	private async ValueTask<KyberSwapQuote> GetOrderQuoteAsync(
		KyberSwapMarket market, Sides side, BigInteger requestedBaseAmount,
		CancellationToken cancellationToken)
	{
		if (side == Sides.Sell)
			return await GetQuoteAsync(market.BaseToken, market.QuoteToken,
				requestedBaseAmount, cancellationToken);

		var forward = await GetQuoteAsync(market.BaseToken,
			market.QuoteToken, requestedBaseAmount, cancellationToken);
		var reverse = await GetQuoteAsync(market.QuoteToken,
			market.BaseToken, forward.OutputAmount, cancellationToken);
		var adjustedInput = (reverse.InputAmount * requestedBaseAmount +
			reverse.OutputAmount - 1) / reverse.OutputAmount;
		if (adjustedInput <= 0)
			throw new InvalidDataException(
				"KyberSwap returned an invalid market-buy input estimate.");
		if (adjustedInput != reverse.InputAmount)
			reverse = await GetQuoteAsync(market.QuoteToken,
				market.BaseToken, adjustedInput, cancellationToken);
		return reverse;
	}

	internal static BigInteger ValidateBuildResponse(KyberSwapBuildData built,
		KyberSwapRouteData route, BigInteger requestedAmount)
	{
		ArgumentNullException.ThrowIfNull(built);
		ArgumentNullException.ThrowIfNull(route);
		if (built.AmountIn.ParseInteger() != requestedAmount)
			throw new InvalidDataException(
				"KyberSwap built a transaction for an unexpected input " +
					"amount.");
		var destinationAmount = built.AmountOut.ParseInteger();
		if (destinationAmount <= 0 ||
			built.Gas.ParseInteger() <= 0)
			throw new InvalidDataException(
				"KyberSwap returned a non-positive swap output or gas " +
					"estimate.");
		if (!built.RouterAddress.NormalizeAddress().EqualsIgnoreCase(
			route.RouterAddress.NormalizeAddress()))
			throw new InvalidDataException(
				"KyberSwap build response changed the route's router.");
		return destinationAmount;
	}

	internal static KyberSwapTransaction ToTransaction(
		KyberSwapBuildData data, string expectedRouter)
	{
		ArgumentNullException.ThrowIfNull(data);
		expectedRouter = expectedRouter.NormalizeAddress();
		if (!data.RouterAddress.NormalizeAddress().EqualsIgnoreCase(
			expectedRouter))
			throw new InvalidDataException(
				"KyberSwap returned an unexpected transaction router.");
		var value = data.TransactionValue.ParseInteger();
		if (value != BigInteger.Zero)
			throw new InvalidDataException(
				"A wrapped-token KyberSwap transaction must not transfer " +
					"native value.");
		var gas = data.Gas.ParseInteger();
		if (gas <= 0)
			throw new InvalidDataException(
				"KyberSwap returned a non-positive transaction gas limit.");
		return new()
		{
			To = expectedRouter,
			Data = data.Data.NormalizeData(),
			Value = value,
			SuggestedGas = gas,
		};
	}

	private async ValueTask<bool> EnsureApprovalAsync(KyberSwapToken token,
		string spender, BigInteger amount,
		CancellationToken cancellationToken)
	{
		if (amount <= 0)
			throw new InvalidOperationException(
				"KyberSwap approval amount must be positive.");
		var allowance = await RpcClient.GetAllowanceAsync(token, spender,
			cancellationToken);
		if (allowance >= amount)
			return false;
		if (!IsAutoApprove)
			throw new InvalidOperationException(
				$"Token '{token.Symbol}' allowance for the KyberSwap router is " +
				"insufficient. Approve it manually or enable automatic " +
				"approval.");
		if (allowance > 0)
			await BroadcastAndConfirmAsync(
				RpcClient.CreateApprovalTransaction(token, spender,
					BigInteger.Zero), "approval reset", cancellationToken);
		await BroadcastAndConfirmAsync(
			RpcClient.CreateApprovalTransaction(token, spender, amount),
			"token approval", cancellationToken);
		return true;
	}

	private async ValueTask BroadcastAndConfirmAsync(
		KyberSwapTransaction transaction, string operation,
		CancellationToken cancellationToken)
	{
		var hash = await RpcClient.SendTransactionAsync(transaction,
			cancellationToken);
		var receipt = await RpcClient.WaitForReceiptAsync(hash,
			ReceiptTimeout, cancellationToken);
		if (!IsSuccessful(receipt))
			throw new InvalidOperationException(
				$"KyberSwap {operation} transaction '{hash}' reverted.");
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
		var receipt = await RpcClient.GetReceiptAsync(swap.TransactionHash,
			cancellationToken);
		if (receipt is null)
			return;
		await ApplyReceiptAsync(swap, receipt, cancellationToken);
	}

	private async ValueTask ApplyReceiptAsync(TrackedSwap swap,
		KyberSwapRpcReceipt receipt, CancellationToken cancellationToken)
	{
		var state = IsSuccessful(receipt)
			? OrderStates.Done
			: OrderStates.Failed;
		var receiptTime = await RpcClient.GetBlockTimeAsync(
			receipt.BlockNumber.ParseInteger(), cancellationToken);
		var execution = state == OrderStates.Done
			? ReadSwapExecution(swap, receipt)
			: null;
		var isOrderChanged = false;
		var isTradeRequired = false;
		using (_sync.EnterScope())
		{
			swap.Receipt = receipt;
			swap.ExecutionTime = receiptTime;
			if (execution is not null)
			{
				swap.Price = execution.Price;
				swap.Volume = execution.Volume;
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
			await SendSwapOrderAsync(swap, swap.TransactionId, receipt,
				cancellationToken);
		if (isTradeRequired)
			await SendSwapTradeAsync(swap, swap.TransactionId, receipt,
				cancellationToken);
	}

	private async ValueTask<
		(KyberSwapToken Token, BigInteger Amount)[]> LoadBalancesAsync(
		CancellationToken cancellationToken)
	{
		KyberSwapToken[] tokens;
		using (_sync.EnterScope())
			tokens = [.. _tokens.Values.GroupBy(static token => token.Address,
					StringComparer.OrdinalIgnoreCase)
				.Select(static group => group.First())];
		var result = new List<(KyberSwapToken, BigInteger)>();

		foreach (var token in tokens)
			result.Add((token, await RpcClient.GetBalanceAsync(token,
				cancellationToken)));

		return [.. result];
	}

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		bool isForced, CancellationToken cancellationToken)
		=> await SendPortfolioSnapshotAsync(target, isForced,
			await LoadBalancesAsync(cancellationToken), cancellationToken);

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		bool isForced,
		(KyberSwapToken Token, BigInteger Amount)[] balances,
		CancellationToken cancellationToken)
	{
		foreach (var item in balances)
		{
			var current = item.Amount.FromBaseUnits(item.Token.Decimals);
			var fingerprint = new BalanceFingerprint(current, 0m);
			var key = $"{target}:{item.Token.Address}";
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
					SecurityCode = item.Token.Symbol,
					BoardCode = BoardCodes.KyberSwap,
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
			var receipt = swap.State == OrderStates.Active
				? await RpcClient.GetReceiptAsync(swap.TransactionHash,
					cancellationToken)
				: swap.Receipt;
			if (receipt is not null)
				await ApplyReceiptAsync(swap, receipt, cancellationToken);
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
				await SendSwapOrderAsync(swap, target, swap.Receipt,
					cancellationToken);
			if (isTradeRequired)
				await SendSwapTradeAsync(swap, target, swap.Receipt,
					cancellationToken);
		}
	}

	private ValueTask SendSwapOrderAsync(TrackedSwap swap, long target,
		KyberSwapRpcReceipt receipt, CancellationToken cancellationToken)
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
			Commission = GetCommission(receipt),
			CommissionCurrency = Chain.GetNativeSymbol(),
		}, cancellationToken);

	private ValueTask SendSwapTradeAsync(TrackedSwap swap, long target,
		KyberSwapRpcReceipt receipt, CancellationToken cancellationToken)
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
			Commission = GetCommission(receipt),
			CommissionCurrency = Chain.GetNativeSymbol(),
		}, cancellationToken);

	private static DateTime GetSwapTime(TrackedSwap swap)
		=> swap.ExecutionTime == default
			? swap.SubmittedTime
			: swap.ExecutionTime;

	private static decimal? GetCommission(KyberSwapRpcReceipt receipt)
	{
		if (receipt?.GasUsed.IsEmpty() != false ||
			receipt.EffectiveGasPrice.IsEmpty())
			return null;
		var cost = receipt.GasUsed.ParseInteger() *
			receipt.EffectiveGasPrice.ParseInteger();
		return cost.FromBaseUnits(18);
	}

	private static decimal GetSwapPrice(KyberSwapMarket market, Sides side,
		BigInteger sourceAmount, BigInteger destinationAmount)
	{
		var volume = (side == Sides.Sell
			? sourceAmount.FromBaseUnits(market.BaseToken.Decimals)
			: destinationAmount.FromBaseUnits(market.BaseToken.Decimals));
		var quote = (side == Sides.Sell
			? destinationAmount.FromBaseUnits(market.QuoteToken.Decimals)
			: sourceAmount.FromBaseUnits(market.QuoteToken.Decimals));
		if (volume <= 0 || quote <= 0)
			throw new InvalidDataException(
				"KyberSwap returned non-positive swap amounts.");
		return quote / volume;
	}

	private KyberSwapExecution ReadSwapExecution(TrackedSwap swap,
		KyberSwapRpcReceipt receipt)
	{
		var sourceAmount = BigInteger.Zero;
		var destinationAmount = BigInteger.Zero;

		foreach (var log in receipt.Logs ?? [])
		{
			if (log?.IsRemoved != false || log.Address.IsEmpty() ||
				log.Topics is not { Length: >= 3 } topics ||
				!topics[0].EqualsIgnoreCase(KyberSwapExtensions.TransferTopic))
				continue;
			string tokenAddress;
			try
			{
				tokenAddress = log.Address.NormalizeAddress();
			}
			catch (ArgumentException)
			{
				continue;
			}
			var isSource = tokenAddress.EqualsIgnoreCase(
				swap.SourceToken.Address);
			var isDestination = tokenAddress.EqualsIgnoreCase(
				swap.DestinationToken.Address);
			if (!isSource && !isDestination)
				continue;
			var from = KyberSwapExtensions.ReadTopicAddress(topics[1]);
			var to = KyberSwapExtensions.ReadTopicAddress(topics[2]);
			var amount = log.Data.ParseInteger();
			if (amount < 0)
				throw new InvalidDataException(
					"A KyberSwap Transfer event contains a negative amount.");
			if (isSource)
			{
				if (from.EqualsIgnoreCase(RpcClient.WalletAddress))
					sourceAmount += amount;
				if (to.EqualsIgnoreCase(RpcClient.WalletAddress))
					sourceAmount -= amount;
			}
			if (isDestination)
			{
				if (to.EqualsIgnoreCase(RpcClient.WalletAddress))
					destinationAmount += amount;
				if (from.EqualsIgnoreCase(RpcClient.WalletAddress))
					destinationAmount -= amount;
			}
		}

		if (sourceAmount <= 0 || destinationAmount <= 0)
			throw new InvalidDataException(
				$"Successful KyberSwap transaction '{swap.TransactionHash}' " +
				"contains no positive wallet execution amounts.");
		if (sourceAmount != swap.SourceAmount)
			throw new InvalidDataException(
				$"KyberSwap transaction '{swap.TransactionHash}' spent an " +
				"unexpected source-token amount.");
		var price = GetSwapPrice(swap.Market, swap.Side, sourceAmount,
			destinationAmount);
		var volume = (swap.Side == Sides.Sell
			? sourceAmount.FromBaseUnits(swap.Market.BaseToken.Decimals)
			: destinationAmount.FromBaseUnits(
				swap.Market.BaseToken.Decimals));
		return new()
		{
			Price = price,
			Volume = volume,
		};
	}

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

	private static bool IsSuccessful(KyberSwapRpcReceipt receipt)
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

	private static string NormalizeTransactionHash(string value)
		=> value.NormalizeHash();
}
