namespace StockSharp.Osmosis;

public partial class OsmosisMessageAdapter
{
	private const ulong _simulationGasLimit = 1_000_000;
	private const ulong _maximumGasLimit = 10_000_000;

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(regMsg.PortfolioName);
		var market = GetMarket(regMsg.SecurityId);
		if (regMsg.OrderType is not (null or OrderTypes.Market))
			throw new NotSupportedException(
				"Osmosis swaps are immediate market operations.");
		if (regMsg.Condition is not null)
			throw new NotSupportedException(
				"Osmosis does not expose conditional AMM orders.");
		if (regMsg.PostOnly == true)
			throw new NotSupportedException(
				"Post-only is not applicable to an Osmosis swap.");
		if (regMsg.TimeInForce is not null)
			throw new NotSupportedException(
				"Time-in-force is not applicable to an Osmosis swap.");
		if (!regMsg.UserOrderId.IsEmpty())
			throw new NotSupportedException(
				"The Cosmos transaction hash is the Osmosis order identifier.");
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException(
				"Osmosis order volume must be positive.");
		var baseAmount = volume.ToBaseUnits(market.BaseToken.Decimals);

		await _transactionGate.WaitAsync(cancellationToken);
		try
		{
			var quote = regMsg.Side == Sides.Sell
				? await ApiClient.GetExactInputQuoteAsync(
					market.BaseToken.Denomination,
					market.QuoteToken.Denomination, baseAmount,
					cancellationToken)
				: await ApiClient.GetExactOutputQuoteAsync(
					market.QuoteToken.Denomination,
					market.BaseToken.Denomination, baseAmount,
					cancellationToken);
			var slippageBasisPoints = decimal.Round(
				SlippageTolerance * 100m, 0,
				MidpointRounding.AwayFromZero).To<int>();
			var limitAmount = regMsg.Side == Sides.Sell
				? quote.OutputAmount * (10_000 - slippageBasisPoints) / 10_000
				: DivideRoundUp(quote.InputAmount *
					(10_000 + slippageBasisPoints), 10_000);
			if (limitAmount <= 0)
				throw new InvalidOperationException(
					"The configured slippage makes the protected swap amount zero.");

			var accountResponse = await ApiClient.GetAccountAsync(
				Signer.WalletAddress, cancellationToken);
			var account = ValidateAccount(accountResponse?.Account);
			var accountNumber = account.AccountNumber.ParseUnsigned(
				"account number");
			var sequence = account.Sequence.ParseUnsigned("account sequence");
			var baseFee = await LoadBaseFeeAsync(cancellationToken);
			var provisionalFee = CalculateFee(_simulationGasLimit, baseFee);
			var inputDenomination = regMsg.Side == Sides.Sell
				? market.BaseToken.Denomination
				: market.QuoteToken.Denomination;
			var outputDenomination = regMsg.Side == Sides.Sell
				? market.QuoteToken.Denomination
				: market.BaseToken.Denomination;
			var simulationBytes = Signer.SignSwap(quote, inputDenomination,
				outputDenomination, limitAmount, _chainId, accountNumber,
				sequence, _simulationGasLimit, provisionalFee);
			var simulation = await ApiClient.SimulateAsync(new()
			{
				TransactionBytes = Convert.ToBase64String(simulationBytes),
			}, cancellationToken);
			var gasUsed = simulation?.GasInfo?.GasUsed.ParseUnsigned(
				"simulated gas used") ?? throw new InvalidDataException(
					"Osmosis simulation returned no gas information.");
			if (gasUsed == 0)
				throw new InvalidDataException(
					"Osmosis simulation returned zero gas use.");
			var gasLimit = checked((ulong)decimal.Ceiling(
				gasUsed * GasAdjustment));
			if (gasLimit == 0 || gasLimit > _maximumGasLimit)
				throw new InvalidDataException(
					$"Osmosis adjusted gas limit '{gasLimit}' is unsafe.");
			baseFee = await LoadBaseFeeAsync(cancellationToken);
			var fee = CalculateFee(gasLimit, baseFee);
			var balances = await ApiClient.GetBalancesAsync(
				Signer.WalletAddress, cancellationToken);
			var requiredInput = regMsg.Side == Sides.Sell
				? quote.InputAmount
				: limitAmount;
			ValidateFunds(balances, inputDenomination, requiredInput, fee);

			var transactionBytes = Signer.SignSwap(quote, inputDenomination,
				outputDenomination, limitAmount, _chainId, accountNumber,
				sequence, gasLimit, fee);
			var expectedHash = Convert.ToHexString(
				SHA256.HashData(transactionBytes));
			var broadcast = await ApiClient.BroadcastAsync(new()
			{
				TransactionBytes = Convert.ToBase64String(transactionBytes),
				Mode = OsmosisBroadcastModes.Sync,
			}, cancellationToken);
			var response = broadcast?.Transaction ?? throw new
				InvalidDataException(
					"Osmosis returned no transaction broadcast result.");
			if (response.Code != 0)
				throw new InvalidOperationException(
					$"Osmosis rejected the transaction with code " +
					$"{response.Code}: {response.RawLog}");
			var transactionHash = response.TransactionHash.IsEmpty()
				? expectedHash
				: response.TransactionHash.NormalizeTransactionHash();
			if (!transactionHash.Equals(expectedHash,
				StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException(
					"Osmosis returned a transaction hash that does not match " +
					"the signed transaction bytes.");

			var quoteVolume = (regMsg.Side == Sides.Sell
				? quote.OutputAmount
				: quote.InputAmount).FromBaseUnits(market.QuoteToken.Decimals);
			var tracked = new TrackedSwap
			{
				TransactionId = regMsg.TransactionId,
				TransactionHash = transactionHash,
				Market = market,
				Side = regMsg.Side,
				Volume = volume,
				QuoteVolume = quoteVolume,
				Price = quoteVolume / volume,
				Commission = fee.FromBaseUnits(6),
				SubmittedTime = CurrentTime,
				State = OrderStates.Active,
			};
			using (_sync.EnterScope())
				_trackedSwaps.Add(transactionHash, tracked);
			await SendSwapOrderAsync(tracked, regMsg.TransactionId,
				cancellationToken);
		}
		finally
		{
			_transactionGate.Release();
		}
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		_ = replaceMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"A broadcast Osmosis swap cannot be replaced.");
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		_ = cancelMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"Osmosis swaps are irreversible after broadcast.");
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		_ = cancelMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"Osmosis has no cancellable order group.");
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
			BoardCode = BoardCodes.Osmosis,
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
				"Osmosis orders use transaction hashes, not numeric IDs.");
		if (!statusMsg.UserId.IsEmpty())
			throw new NotSupportedException(
				"Osmosis has no exchange-side user identifier.");
		if (statusMsg.SecurityIds.Length > 0)
			throw new NotSupportedException(
				"Use the primary security filter for Osmosis order status.");
		var subscription = new OrderSubscription
		{
			TransactionHash = statusMsg.OrderStringId.IsEmpty()
				? null
				: statusMsg.OrderStringId.NormalizeTransactionHash(),
			SecurityId = statusMsg.SecurityId,
			Side = statusMsg.Side,
			Volume = statusMsg.Volume,
			States = statusMsg.States,
			From = statusMsg.From?.ToUniversalTime(),
			To = statusMsg.To?.ToUniversalTime(),
			Skip = Math.Max(0, statusMsg.Skip ?? 0).Min(int.MaxValue).To<int>(),
			Maximum = (statusMsg.Count ?? 1000).Min(10_000).Max(1).To<int>(),
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

	private OsmosisAccount ValidateAccount(OsmosisAccount account)
	{
		ArgumentNullException.ThrowIfNull(account);
		var value = account.BaseAccount ?? account;
		if (value.Address.IsEmpty() ||
			!value.Address.NormalizeOsmosisAddress().Equals(
				Signer.WalletAddress, StringComparison.Ordinal))
			throw new InvalidDataException(
				"Osmosis returned an account for a different wallet.");
		_ = value.AccountNumber.ParseUnsigned("account number");
		_ = value.Sequence.ParseUnsigned("account sequence");
		return value;
	}

	private async ValueTask<decimal> LoadBaseFeeAsync(
		CancellationToken cancellationToken)
	{
		var response = await ApiClient.GetBaseFeeAsync(cancellationToken);
		var fee = response?.BaseFee.ParseDecimal("EIP base fee") ?? throw new
			InvalidDataException("Osmosis returned no current EIP base fee.");
		if (fee <= 0)
			throw new InvalidDataException(
				"Osmosis returned a non-positive EIP base fee.");
		return fee;
	}

	private BigInteger CalculateFee(ulong gasLimit, decimal baseFee)
	{
		var fee = decimal.Ceiling(gasLimit * baseFee * BaseFeeMultiplier);
		if (fee <= 0)
			throw new InvalidDataException(
				"The calculated Osmosis transaction fee is not positive.");
		return new BigInteger(fee);
	}

	private static BigInteger DivideRoundUp(BigInteger value,
		BigInteger divisor)
	{
		if (value <= 0 || divisor <= 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		return (value + divisor - 1) / divisor;
	}

	private static void ValidateFunds(OsmosisBalancesResponse response,
		string inputDenomination, BigInteger requiredInput, BigInteger fee)
	{
		ArgumentNullException.ThrowIfNull(response);
		var inputBalance = FindBalance(response, inputDenomination);
		var gasBalance = FindBalance(response,
			OsmosisExtensions.NativeDenomination);
		var requiredForInput = requiredInput +
			(inputDenomination == OsmosisExtensions.NativeDenomination
				? fee
				: BigInteger.Zero);
		if (inputBalance < requiredForInput)
			throw new InvalidOperationException(
				$"Insufficient Osmosis '{inputDenomination}' balance. " +
				$"Required {requiredForInput}, available {inputBalance}.");
		if (inputDenomination != OsmosisExtensions.NativeDenomination &&
			gasBalance < fee)
			throw new InvalidOperationException(
				$"Insufficient OSMO gas balance. Required {fee}, available " +
				$"{gasBalance}.");
	}

	private static BigInteger FindBalance(OsmosisBalancesResponse response,
		string denomination)
	{
		var coin = (response.Balances ?? []).FirstOrDefault(item =>
			item?.Denomination == denomination);
		return coin is null
			? BigInteger.Zero
			: coin.Amount.ParseAmount("wallet balance");
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
			var balances = await ApiClient.GetBalancesAsync(
				Signer.WalletAddress, cancellationToken);
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
		var response = await ApiClient.TryGetTransactionAsync(
			swap.TransactionHash, cancellationToken);
		if (response?.Transaction is not OsmosisTransactionResult transaction)
			return;
		var state = transaction.Code == 0
			? OrderStates.Done
			: OrderStates.Failed;
		var completionTime = transaction.Timestamp.IsEmpty()
			? CurrentTime
			: transaction.Timestamp.ParseUtcTime("transaction time");
		decimal? quoteVolume = null;
		if (state == OrderStates.Done &&
			TryReadExecution(swap, transaction, out var executionQuote))
			quoteVolume = executionQuote;
		var isChanged = false;
		var isTradeRequired = false;
		using (_sync.EnterScope())
		{
			isChanged = swap.State != state;
			swap.State = state;
			swap.CompletionTime = completionTime;
			swap.FailureReason = state == OrderStates.Failed
				? transaction.RawLog
				: null;
			if (quoteVolume is > 0)
			{
				swap.QuoteVolume = quoteVolume.Value;
				swap.Price = quoteVolume.Value / swap.Volume;
			}
			if (state == OrderStates.Done && !swap.IsTradeSent)
			{
				swap.IsTradeSent = true;
				isTradeRequired = true;
			}
		}
		if (isChanged)
			await SendSwapOrderAsync(swap, swap.TransactionId,
				cancellationToken);
		if (isTradeRequired)
			await SendSwapTradeAsync(swap, swap.TransactionId,
				cancellationToken);
	}

	private static bool TryReadExecution(TrackedSwap swap,
		OsmosisTransactionResult transaction, out decimal quoteVolume)
	{
		quoteVolume = 0m;
		BigInteger? input = null;
		BigInteger? output = null;
		var inputDenomination = swap.Side == Sides.Sell
			? swap.Market.BaseToken.Denomination
			: swap.Market.QuoteToken.Denomination;
		var outputDenomination = swap.Side == Sides.Sell
			? swap.Market.QuoteToken.Denomination
			: swap.Market.BaseToken.Denomination;
		foreach (var item in transaction.Events ?? [])
		{
			if (item?.Type != "token_swapped")
				continue;
			var inputText = FindAttribute(item, "tokens_in");
			var outputText = FindAttribute(item, "tokens_out");
			try
			{
				if (!inputText.IsEmpty())
				{
					var coin = inputText.ParseCoin("execution input");
					if (coin.Denomination == inputDenomination)
						input = (input ?? BigInteger.Zero) + coin.Amount;
				}
				if (!outputText.IsEmpty())
				{
					var coin = outputText.ParseCoin("execution output");
					if (coin.Denomination == outputDenomination)
						output = (output ?? BigInteger.Zero) + coin.Amount;
				}
			}
			catch (InvalidDataException)
			{
				return false;
			}
		}
		var quoteAmount = swap.Side == Sides.Sell ? output : input;
		if (quoteAmount is not BigInteger amount || amount <= 0)
			return false;
		quoteVolume = amount.FromBaseUnits(swap.Market.QuoteToken.Decimals);
		return quoteVolume > 0;
	}

	private static string FindAttribute(OsmosisTransactionEvent item,
		string key)
		=> (item.Attributes ?? []).FirstOrDefault(attribute =>
			attribute?.Key == key)?.Value;

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		bool isForced, CancellationToken cancellationToken)
		=> await SendPortfolioSnapshotAsync(target, isForced,
			await ApiClient.GetBalancesAsync(Signer.WalletAddress,
				cancellationToken), cancellationToken);

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		bool isForced, OsmosisBalancesResponse response,
		CancellationToken cancellationToken)
	{
		OsmosisToken[] tokens;
		using (_sync.EnterScope())
			tokens = [.. _tokens.Values];
		foreach (var token in tokens.OrderBy(static item => item.Symbol,
			StringComparer.OrdinalIgnoreCase))
		{
			var amount = FindBalance(response, token.Denomination);
			var current = amount.FromBaseUnits(token.Decimals);
			var fingerprint = new BalanceFingerprint(current, 0m);
			var key = $"{target}:{token.Denomination}";
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
					SecurityCode = GetTokenSecurityCode(token, tokens),
					BoardCode = BoardCodes.Osmosis,
				},
				ServerTime = CurrentTime,
				OriginalTransactionId = target,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, current, true)
			.TryAdd(PositionChangeTypes.BlockedValue, 0m, true),
				cancellationToken);
		}
	}

	private static string GetTokenSecurityCode(OsmosisToken token,
		OsmosisToken[] tokens)
	{
		if (tokens.Count(candidate => candidate.Symbol.Equals(
			token.Symbol, StringComparison.OrdinalIgnoreCase)) == 1)
			return token.Symbol;
		var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
			token.Denomination)));
		return $"{token.Symbol}-{hash[..8]}";
	}

	private async ValueTask SendOrderSnapshotAsync(
		OrderSubscription subscription, long target, bool isForced,
		CancellationToken cancellationToken)
	{
		TrackedSwap[] swaps;
		using (_sync.EnterScope())
			swaps = [.. _trackedSwaps.Values.Where(swap =>
				Matches(subscription, swap)).OrderBy(static swap =>
					swap.SubmittedTime)];
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
			ServerTime = swap.CompletionTime ?? swap.SubmittedTime,
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
			Commission = swap.Commission,
			CommissionCurrency = "OSMO",
		}, cancellationToken);

	private ValueTask SendSwapTradeAsync(TrackedSwap swap, long target,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = swap.Market.ToStockSharp(),
			ServerTime = swap.CompletionTime ?? CurrentTime,
			PortfolioName = GetPortfolioName(),
			Side = swap.Side,
			OrderStringId = swap.TransactionHash,
			TradeStringId = swap.TransactionHash,
			TradePrice = swap.Price,
			TradeVolume = swap.Volume,
			TransactionId = swap.TransactionId,
			OriginalTransactionId = target,
			Commission = swap.Commission,
			CommissionCurrency = "OSMO",
		}, cancellationToken);

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
			(subscription.To is null || swap.SubmittedTime <= subscription.To);
	}

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
}
