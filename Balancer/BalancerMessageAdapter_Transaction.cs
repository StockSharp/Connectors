namespace StockSharp.Balancer;

public partial class BalancerMessageAdapter
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
				"Balancer swaps are immediate AMM market orders.");
		if (regMsg.Condition is not null)
			throw new NotSupportedException(
				"Balancer pool contracts do not expose conditional orders.");
		if (regMsg.PostOnly == true)
			throw new NotSupportedException(
				"Post-only is not applicable to AMM swaps.");
		if (regMsg.TimeInForce is not null)
			throw new NotSupportedException(
				"Time-in-force is not applicable to AMM swaps.");
		if (!regMsg.UserOrderId.IsEmpty())
			throw new NotSupportedException(
				"An on-chain swap identifier is its transaction hash; a " +
				"client-order ID cannot be embedded in a pool swap.");
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException(
				"Balancer swap volume must be positive.");
		var swapType = regMsg.Side == Sides.Sell
			? BalancerSwapTypes.ExactIn
			: BalancerSwapTypes.ExactOut;
		var preliminary = await ApiClient.GetQuoteAsync(_deployment, market,
			swapType, volume, cancellationToken);
		var inputToken = regMsg.Side == Sides.Sell
			? market.BaseToken
			: market.QuoteToken;
		var approvalAmount = GetApprovalAmount(preliminary, regMsg.Side);
		await EnsureSwapApprovalAsync(market, inputToken, approvalAmount,
			cancellationToken);

		var quote = await ApiClient.GetQuoteAsync(_deployment, market,
			swapType, volume, cancellationToken);
		var finalApprovalAmount = GetApprovalAmount(quote, regMsg.Side);
		if (finalApprovalAmount > approvalAmount)
			await EnsureSwapApprovalAsync(market, inputToken,
				finalApprovalAmount, cancellationToken);
		var transaction = RpcClient.CreateSwapTransaction(market, swapType,
			quote, SlippageTolerance,
			DateTime.UtcNow.AddMinutes(2));
		var hash = await RpcClient.SendTransactionAsync(transaction,
			cancellationToken);
		var price = quote.Price;
		var tracked = new TrackedSwap
		{
			TransactionId = regMsg.TransactionId,
			TransactionHash = hash,
			Market = market,
			Side = regMsg.Side,
			Volume = volume,
			Price = price,
			SubmittedTime = CurrentTime,
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
			"A broadcast Balancer transaction cannot be replaced through " +
			"the protocol API.");
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		_ = cancelMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"Balancer has no cancellable order book. Pending EVM nonce " +
			"replacement is a wallet operation and is not emulated as a " +
			"protocol cancellation.");
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		_ = cancelMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"Balancer has no open-order group to cancel.");
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
			BoardCode = BoardCodes.Balancer,
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
				"Balancer orders use EVM transaction hashes, not numeric " +
				"order identifiers.");
		if (!statusMsg.UserId.IsEmpty())
			throw new NotSupportedException(
				"Balancer has no exchange-side user identifier.");
		if (statusMsg.SecurityIds.Length > 0)
			throw new NotSupportedException(
				"Use the primary security filter for Balancer order status.");
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
			From = statusMsg.From?.EnsureUtc(),
			To = statusMsg.To?.EnsureUtc(),
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

	private async ValueTask EnsureApprovalAsync(BalancerToken token,
		string spender, BigInteger amount,
		CancellationToken cancellationToken)
	{
		if (amount <= 0)
			throw new InvalidOperationException(
				"Balancer approval amount must be positive.");
		var allowance = await RpcClient.GetAllowanceAsync(token, spender,
			cancellationToken);
		if (allowance >= amount)
			return;
		if (allowance > 0)
			await BroadcastAndConfirmAsync(
				RpcClient.CreateApprovalTransaction(token, spender,
					BigInteger.Zero),
				"approval reset", cancellationToken);
		await BroadcastAndConfirmAsync(
			RpcClient.CreateApprovalTransaction(token, spender, amount),
			"token approval", cancellationToken);
	}

	private async ValueTask EnsureSwapApprovalAsync(BalancerMarket market,
		BalancerToken token, BigInteger amount,
		CancellationToken cancellationToken)
	{
		await EnsureApprovalAsync(token, RpcClient.GetSpender(market), amount,
			cancellationToken);
		if (market.Pool.ProtocolVersion != 3)
			return;
		var router = RpcClient.GetPermit2Spender(market);
		var allowance = await RpcClient.GetPermit2AllowanceAsync(token, router,
			cancellationToken);
		if (allowance >= amount)
			return;
		await BroadcastAndConfirmAsync(
			RpcClient.CreatePermit2ApprovalTransaction(token, router),
			"Permit2 approval", cancellationToken);
	}

	private BigInteger GetApprovalAmount(BalancerQuote quote, Sides side)
	{
		ArgumentNullException.ThrowIfNull(quote);
		if (side == Sides.Sell)
			return quote.InputAmount;
		var basisPoints = new BigInteger(SlippageTolerance * 100m);
		return (quote.InputAmount * (10_000 + basisPoints) + 9_999) / 10_000;
	}

	private async ValueTask BroadcastAndConfirmAsync(
		BalancerTransaction transaction, string operation,
		CancellationToken cancellationToken)
	{
		var hash = await RpcClient.SendTransactionAsync(transaction,
			cancellationToken);
		var receipt = await RpcClient.WaitForReceiptAsync(hash,
			ReceiptTimeout, cancellationToken);
		if (!IsSuccessful(receipt))
			throw new InvalidOperationException(
				$"Balancer {operation} transaction '{hash}' reverted.");
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
		BalancerRpcReceipt receipt, CancellationToken cancellationToken)
	{
		var state = IsSuccessful(receipt)
			? OrderStates.Done
			: OrderStates.Failed;
		SwapExecution? execution = null;
		if (state == OrderStates.Done)
		{
			try
			{
				execution = ReadSwapExecution(swap, receipt);
			}
			catch (Exception error) when (error is InvalidDataException or
				OverflowException)
			{
				this.AddWarningLog(
					"Balancer fill decoding failed for {0}; using the " +
					"submitted quote: {1}", swap.TransactionHash,
					error.Message);
				execution = new(swap.Price, swap.Volume);
			}
		}
		var isOrderChanged = false;
		var isTradeRequired = false;
		using (_sync.EnterScope())
		{
			swap.Receipt = receipt;
			if (execution is SwapExecution fill)
			{
				swap.Price = fill.Price;
				swap.Volume = fill.Volume;
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
		(BalancerToken Token, BigInteger Amount)[]> LoadBalancesAsync(
		CancellationToken cancellationToken)
	{
		BalancerToken[] tokens;
		using (_sync.EnterScope())
			tokens = [.. _tokens.Values.GroupBy(static token => token.Address,
					StringComparer.OrdinalIgnoreCase)
				.Select(static group => group.First())];
		var result = new List<(BalancerToken, BigInteger)>();
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
		(BalancerToken Token, BigInteger Amount)[] balances,
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
					BoardCode = BoardCodes.Balancer,
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
		BalancerRpcReceipt receipt, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = swap.Market.ToStockSharp(),
			ServerTime = CurrentTime,
			PortfolioName = GetPortfolioName(),
			Side = swap.Side,
			OrderVolume = swap.Volume,
			Balance = swap.State == OrderStates.Active ? swap.Volume : 0m,
			OrderPrice = swap.Price,
			OrderType = OrderTypes.Market,
			OrderState = swap.State,
			OrderStringId = swap.TransactionHash,
			TransactionId = swap.TransactionId,
			OriginalTransactionId = target,
			Commission = GetCommission(receipt),
			CommissionCurrency = _deployment.NativeSymbol,
		}, cancellationToken);

	private ValueTask SendSwapTradeAsync(TrackedSwap swap, long target,
		BalancerRpcReceipt receipt, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = swap.Market.ToStockSharp(),
			ServerTime = CurrentTime,
			PortfolioName = GetPortfolioName(),
			Side = swap.Side,
			OrderStringId = swap.TransactionHash,
			TradeStringId = swap.TransactionHash,
			TradePrice = swap.Price,
			TradeVolume = swap.Volume,
			TransactionId = swap.TransactionId,
			OriginalTransactionId = target,
			Commission = GetCommission(receipt),
			CommissionCurrency = _deployment.NativeSymbol,
		}, cancellationToken);

	private static decimal? GetCommission(BalancerRpcReceipt receipt)
	{
		if (receipt?.GasUsed.IsEmpty() != false ||
			receipt.EffectiveGasPrice.IsEmpty())
			return null;
		var cost = receipt.GasUsed.ParseInteger() *
			receipt.EffectiveGasPrice.ParseInteger();
		return cost.FromBaseUnits(18);
	}

	private SwapExecution ReadSwapExecution(TrackedSwap swap,
		BalancerRpcReceipt receipt)
	{
		var baseAmount = 0m;
		var quoteAmount = 0m;
		var isFound = false;
		foreach (var log in receipt.Logs ?? [])
		{
			try
			{
				var raw = BalancerExtensions.DecodeSwap(log, _deployment);
				if (raw is null || !raw.TryCreateTrade(swap.Market,
					DateTime.UnixEpoch, out var trade) || trade.Side != swap.Side)
					continue;
				baseAmount += trade.Volume;
				quoteAmount += trade.Price * trade.Volume;
				isFound = true;
			}
			catch (Exception error) when (error is InvalidDataException or
				ArgumentException or OverflowException)
			{
			}
		}
		if (!isFound)
			throw new InvalidDataException(
				$"Successful Balancer transaction " +
				$"'{swap.TransactionHash}' contains no matching " +
				"Swap event.");
		if (baseAmount <= 0 || quoteAmount <= 0)
			throw new InvalidDataException(
				$"Balancer transaction '{swap.TransactionHash}' " +
				"contains non-positive execution amounts.");
		return new(quoteAmount / baseAmount, baseAmount);
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
		if (subscription.Volume is decimal volume && swap.Volume != volume)
			return false;
		return (subscription.From is null ||
				swap.SubmittedTime >= subscription.From) &&
			(subscription.To is null || swap.SubmittedTime <=
				subscription.To);
	}

	private static bool IsSuccessful(BalancerRpcReceipt receipt)
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
