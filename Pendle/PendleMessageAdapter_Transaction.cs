namespace StockSharp.Pendle;

public partial class PendleMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(regMsg.PortfolioName);
		var security = GetSecurity(regMsg.SecurityId);
		if (regMsg.OrderType is not (null or OrderTypes.Market))
			throw new NotSupportedException(
				"Pendle universal convert supports immediate market swaps " +
					"only.");
		if (regMsg.Side is not (Sides.Buy or Sides.Sell))
			throw new ArgumentOutOfRangeException(nameof(regMsg.Side));
		if (regMsg.Condition is not null)
			throw new NotSupportedException(
				"Pendle universal convert does not expose conditional orders.");
		if (regMsg.PostOnly == true)
			throw new NotSupportedException(
				"Post-only is not applicable to an immediate swap.");
		if (regMsg.TimeInForce is not null)
			throw new NotSupportedException(
				"Time-in-force is not applicable to an immediate swap.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException(
				"An expiry cannot be attached to a Pendle conversion request.");
		if (!regMsg.UserOrderId.IsEmpty())
			throw new NotSupportedException(
				"An on-chain swap is identified by its transaction hash; a " +
				"client-order identifier cannot be embedded in it.");

		var requestedVolume = regMsg.Volume.Abs();
		if (requestedVolume <= 0)
			throw new InvalidOperationException(
				"Pendle swap volume must be positive.");
		var requestedAssetAmount = requestedVolume.ToBaseUnits(
			security.Token.Decimals);
		if (requestedAssetAmount <= 0)
			throw new InvalidOperationException(
				"Pendle swap volume rounds to zero asset units.");

		var sourceToken = regMsg.Side == Sides.Sell
			? security.Token
			: security.Market.UnderlyingToken;
		var destinationToken = regMsg.Side == Sides.Sell
			? security.Market.UnderlyingToken
			: security.Token;
		var sourceAmount = regMsg.Side == Sides.Sell
			? requestedAssetAmount
			: GetBuySourceAmount(await GetLevel1Async(security,
				cancellationToken), requestedVolume, sourceToken.Decimals);
		await ValidateBalanceAsync(sourceToken, sourceAmount,
			cancellationToken);
		var response = await HttpClient.BuildConvertAsync(sourceToken.Address,
			destinationToken.Address, sourceAmount, RpcClient.WalletAddress,
			SlippageTolerance / 100m, cancellationToken);
		var built = ValidateConvert(response, sourceToken, destinationToken,
			sourceAmount);
		await RpcClient.VerifyContractAsync(built.Router, "Pendle router",
			cancellationToken);
		var wasApproved = await EnsureApprovalAsync(sourceToken, built.Router,
			sourceAmount, cancellationToken);
		if (wasApproved)
		{
			if (regMsg.Side == Sides.Buy)
				sourceAmount = GetBuySourceAmount(
					await GetLevel1Async(security, cancellationToken),
					requestedVolume, sourceToken.Decimals);
			await ValidateBalanceAsync(sourceToken, sourceAmount,
				cancellationToken);
			response = await HttpClient.BuildConvertAsync(sourceToken.Address,
				destinationToken.Address, sourceAmount,
				RpcClient.WalletAddress, SlippageTolerance / 100m,
				cancellationToken);
			built = ValidateConvert(response, sourceToken, destinationToken,
				sourceAmount);
			await RpcClient.VerifyContractAsync(built.Router,
				"Pendle router", cancellationToken);
			_ = await EnsureApprovalAsync(sourceToken, built.Router,
				sourceAmount, cancellationToken);
		}

		var hash = await RpcClient.SendTransactionAsync(built.Transaction,
			cancellationToken);
		var price = GetSwapPrice(security, regMsg.Side, sourceAmount,
			built.DestinationAmount);
		var tracked = new TrackedSwap
		{
			TransactionId = regMsg.TransactionId,
			TransactionHash = hash,
			Security = security,
			Side = regMsg.Side,
			SourceToken = sourceToken,
			DestinationToken = destinationToken,
			SourceAmount = sourceAmount,
			ExpectedDestinationAmount = built.DestinationAmount,
			RequestedVolume = requestedVolume,
			Volume = regMsg.Side == Sides.Sell
				? sourceAmount.FromBaseUnits(sourceToken.Decimals)
				: built.DestinationAmount.FromBaseUnits(
					destinationToken.Decimals),
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
			"A broadcast Pendle transaction cannot be replaced through the " +
				"universal convert API.");
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		_ = cancelMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"Pendle universal convert has no cancellable order book. Pending EVM " +
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
			"Pendle universal convert has no open-order group to cancel.");
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
			BoardCode = BoardCodes.Pendle,
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
				"Pendle swaps use EVM transaction hashes, not numeric order " +
				"identifiers.");
		if (!statusMsg.UserId.IsEmpty())
			throw new NotSupportedException(
				"Pendle has no exchange-side user identifier.");
		if (statusMsg.SecurityIds.Length > 0)
			throw new NotSupportedException(
				"Use the primary security filter for Pendle order status.");
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

	private static BigInteger GetBuySourceAmount(PendleLevel1 quote,
		decimal requestedVolume, int sourceDecimals)
	{
		ArgumentNullException.ThrowIfNull(quote);
		if (requestedVolume <= 0 || quote.Ask <= 0)
			throw new InvalidOperationException(
				"Pendle market-buy estimate must be positive.");
		var amount = (requestedVolume * quote.Ask).ToBaseUnitsCeiling(
			sourceDecimals);
		return amount > 0
			? amount
			: throw new InvalidOperationException(
				"Pendle market-buy estimate rounds to zero source units.");
	}

	private async ValueTask ValidateBalanceAsync(PendleToken sourceToken,
		BigInteger required, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(sourceToken);
		var actual = await RpcClient.GetBalanceAsync(sourceToken,
			cancellationToken);
		if (actual < required)
			throw new InvalidOperationException(
				$"Insufficient {sourceToken.Symbol} balance: {actual} base " +
					$"units available, {required} required.");
	}

	internal (PendleTransaction Transaction, BigInteger DestinationAmount,
		string Router) ValidateConvert(PendleConvertResponse response,
		PendleToken source, PendleToken destination, BigInteger sourceAmount)
	{
		ArgumentNullException.ThrowIfNull(response);
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(destination);
		if (sourceAmount <= 0)
			throw new ArgumentOutOfRangeException(nameof(sourceAmount));
		if (!response.Action.EqualsIgnoreCase("swap") ||
			response.Inputs is not { Length: 1 } inputs ||
			!inputs[0].Token.NormalizeAddress().EqualsIgnoreCase(
				source.Address) ||
			inputs[0].Amount.ParseInteger() != sourceAmount)
			throw new InvalidDataException(
				"Pendle conversion input does not match the request.");
		if (response.Routes is not { Length: 1 } routes ||
			routes[0]?.Transaction is null)
			throw new InvalidDataException(
				"Pendle conversion did not return exactly one executable " +
					"route.");
		var route = routes[0];
		if (route.Outputs is not { Length: 1 } outputs ||
			!outputs[0].Token.NormalizeAddress().EqualsIgnoreCase(
				destination.Address))
			throw new InvalidDataException(
				"Pendle conversion output does not match the request.");
		var destinationAmount = outputs[0].Amount.ParseInteger();
		if (destinationAmount <= 0)
			throw new InvalidDataException(
				"Pendle conversion returned a non-positive output amount.");
		foreach (var approval in response.RequiredApprovals ?? [])
		{
			if (approval is null ||
				!approval.Token.NormalizeAddress().EqualsIgnoreCase(
					source.Address) ||
				approval.Amount.ParseInteger() <= 0)
				throw new InvalidDataException(
					"Pendle conversion returned an unexpected approval.");
		}
		var data = route.Transaction;
		var from = data.From.NormalizeAddress();
		if (!from.EqualsIgnoreCase(RpcClient.WalletAddress))
			throw new InvalidDataException(
				"Pendle transaction sender does not match the configured " +
					"wallet.");
		var target = data.To.NormalizeAddress();
		var value = data.Value.IsEmpty()
			? BigInteger.Zero
			: data.Value.ParseInteger();
		var expectedValue = source.Address.IsNativeToken()
			? sourceAmount
			: BigInteger.Zero;
		if (value != expectedValue)
			throw new InvalidDataException(
				"Pendle transaction native value does not match its input.");
		if (data.Data.IsEmpty())
			throw new InvalidDataException(
				"Pendle API returned no transaction calldata.");
		return (new()
		{
			To = target,
			Data = data.Data.NormalizeData(),
			Value = value,
			SuggestedGas = BigInteger.Zero,
		}, destinationAmount, target);
	}

	private async ValueTask<bool> EnsureApprovalAsync(PendleToken token,
		string spender, BigInteger amount,
		CancellationToken cancellationToken)
	{
		if (amount <= 0)
			throw new InvalidOperationException(
				"Pendle approval amount must be positive.");
		var allowance = await RpcClient.GetAllowanceAsync(token, spender,
			cancellationToken);
		if (allowance >= amount)
			return false;
		if (!IsAutoApprove)
			throw new InvalidOperationException(
				$"Token '{token.Symbol}' allowance for the Pendle router is " +
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
		PendleTransaction transaction, string operation,
		CancellationToken cancellationToken)
	{
		var hash = await RpcClient.SendTransactionAsync(transaction,
			cancellationToken);
		var receipt = await RpcClient.WaitForReceiptAsync(hash,
			ReceiptTimeout, cancellationToken);
		if (!IsSuccessful(receipt))
			throw new InvalidOperationException(
				$"Pendle {operation} transaction '{hash}' reverted.");
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
		PendleRpcReceipt receipt, CancellationToken cancellationToken)
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
		(PendleToken Token, BigInteger Amount)[]> LoadBalancesAsync(
		CancellationToken cancellationToken)
	{
		PendleToken[] tokens;
		using (_sync.EnterScope())
			tokens = [.. _tokens.Values.GroupBy(static token => token.Address,
					StringComparer.OrdinalIgnoreCase)
				.Select(static group => group.First())];
		var result = new List<(PendleToken, BigInteger)>();
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
		(PendleToken Token, BigInteger Amount)[] balances,
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
					BoardCode = BoardCodes.Pendle,
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
		PendleRpcReceipt receipt, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = swap.Security.ToStockSharp(),
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
		PendleRpcReceipt receipt, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = swap.Security.ToStockSharp(),
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

	private static decimal? GetCommission(PendleRpcReceipt receipt)
	{
		if (receipt?.GasUsed.IsEmpty() != false ||
			receipt.EffectiveGasPrice.IsEmpty())
			return null;
		var cost = receipt.GasUsed.ParseInteger() *
			receipt.EffectiveGasPrice.ParseInteger();
		return cost.FromBaseUnits(18);
	}

	private static decimal GetSwapPrice(PendleSecurity security, Sides side,
		BigInteger sourceAmount, BigInteger destinationAmount)
	{
		var volume = (side == Sides.Sell
			? sourceAmount.FromBaseUnits(security.Token.Decimals)
			: destinationAmount.FromBaseUnits(security.Token.Decimals));
		var quote = (side == Sides.Sell
			? destinationAmount.FromBaseUnits(
				security.Market.UnderlyingToken.Decimals)
			: sourceAmount.FromBaseUnits(
				security.Market.UnderlyingToken.Decimals));
		if (volume <= 0 || quote <= 0)
			throw new InvalidDataException(
				"Pendle returned non-positive swap amounts.");
		return quote / volume;
	}

	private PendleSwapExecution ReadSwapExecution(TrackedSwap swap,
		PendleRpcReceipt receipt)
	{
		var sourceAmount = swap.SourceToken.Address.IsNativeToken()
			? swap.SourceAmount
			: BigInteger.Zero;
		var destinationAmount = swap.DestinationToken.Address.IsNativeToken()
			? swap.ExpectedDestinationAmount
			: BigInteger.Zero;
		foreach (var log in receipt.Logs ?? [])
		{
			if (log?.IsRemoved != false || log.Address.IsEmpty() ||
				log.Topics is not { Length: >= 3 } topics ||
				!topics[0].EqualsIgnoreCase(PendleExtensions.TransferTopic))
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
			var from = PendleExtensions.ReadTopicAddress(topics[1]);
			var to = PendleExtensions.ReadTopicAddress(topics[2]);
			var amount = log.Data.ParseInteger();
			if (amount < 0)
				throw new InvalidDataException(
					"A Pendle Transfer event contains a negative amount.");
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
				$"Successful Pendle transaction '{swap.TransactionHash}' " +
				"contains no positive wallet execution amounts.");
		if (sourceAmount != swap.SourceAmount)
			throw new InvalidDataException(
				$"Pendle transaction '{swap.TransactionHash}' spent an " +
				"unexpected source-token amount.");
		var price = GetSwapPrice(swap.Security, swap.Side, sourceAmount,
			destinationAmount);
		var volume = (swap.Side == Sides.Sell
			? sourceAmount.FromBaseUnits(swap.Security.Token.Decimals)
			: destinationAmount.FromBaseUnits(
				swap.Security.Token.Decimals));
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
				swap.Security.SecurityCode))
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

	private static bool IsSuccessful(PendleRpcReceipt receipt)
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
