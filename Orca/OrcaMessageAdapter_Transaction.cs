namespace StockSharp.Orca;

public partial class OrcaMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(regMsg.PortfolioName);
		var market = GetMarket(regMsg.SecurityId);
		if (!market.IsDirectTradingSupported)
			throw new NotSupportedException(
				"Direct trading is unavailable for adaptive-fee, transfer-fee, " +
				"or transfer-hook Orca pools.");
		if (regMsg.OrderType is not (null or OrderTypes.Market))
			throw new NotSupportedException(
				"Orca swaps are immediate AMM market orders.");
		if (regMsg.Condition is not null)
			throw new NotSupportedException(
				"Orca does not expose conditional orders.");
		if (regMsg.PostOnly == true)
			throw new NotSupportedException(
				"Post-only is not applicable to AMM swaps.");
		if (regMsg.TimeInForce is not null)
			throw new NotSupportedException(
				"Time-in-force is not applicable to AMM swaps.");
		if (!regMsg.UserOrderId.IsEmpty())
			throw new NotSupportedException(
				"An Orca order identifier is its Solana transaction signature; " +
				"no client-order ID is stored on-chain.");
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException(
				"Orca swap volume must be positive.");
		var baseUnits = volume.ToBaseUnits(market.TokenA.Decimals);
		if (baseUnits <= 0)
			throw new InvalidOperationException(
				"Orca swap volume rounds to zero base units.");

		await RefreshMarketAsync(market, cancellationToken);
		var quote = market.GetQuote(regMsg.Side, baseUnits,
			SlippageTolerance);
		var computeUnitPrice = ComputeUnitPrice == 0
			? await RpcClient.GetPriorityFeeAsync(
			[
				market.PoolAddress,
				market.TokenVaultA,
				market.TokenVaultB,
			], cancellationToken)
			: checked((ulong)ComputeUnitPrice);
		var instructions = OrcaInstructionBuilder.Build(market, quote,
			RpcClient.WalletAddress, checked((uint)ComputeUnitLimit),
			computeUnitPrice);
		var signature = await RpcClient.SendTransactionAsync(instructions,
			cancellationToken);
		var quoteAmount = quote.QuoteAmount.FromBaseUnits(
			market.TokenB.Decimals);
		var tracked = new TrackedSwap
		{
			TransactionId = regMsg.TransactionId,
			Signature = signature,
			Market = market,
			Side = regMsg.Side,
			Volume = volume,
			Price = quoteAmount / volume,
			SubmittedTime = CurrentTime,
			State = OrderStates.Active,
		};
		using (_sync.EnterScope())
			_trackedSwaps[signature] = tracked;
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
			"A broadcast Solana transaction cannot be replaced through Orca.");
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		_ = cancelMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"Orca has no cancellable order book.");
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		_ = cancelMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"Orca has no open-order group to cancel.");
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
				_portfolioSubscriptions.Remove(lookupMsg.OriginalTransactionId);
				RemoveFingerprintPrefix(_balanceFingerprints,
					lookupMsg.OriginalTransactionId);
			}
			return;
		}
		ValidatePortfolio(lookupMsg.PortfolioName);
		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = GetPortfolioName(),
			BoardCode = BoardCodes.Orca,
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
				"Orca orders use Solana transaction signatures, not numeric " +
				"identifiers.");
		if (!statusMsg.UserId.IsEmpty())
			throw new NotSupportedException(
				"Orca has no exchange-side user identifier.");
		if (statusMsg.SecurityIds.Length > 0)
			throw new NotSupportedException(
				"Use the primary security filter for Orca order status.");
		var signature = statusMsg.OrderStringId.IsEmpty()
			? null
			: NormalizeTransactionSignature(statusMsg.OrderStringId);
		var subscription = new OrderSubscription
		{
			Signature = signature,
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
		var receipt = await RpcClient.GetReceiptAsync(swap.Signature,
			cancellationToken);
		if (receipt is not null)
			await ApplyReceiptAsync(swap, receipt, cancellationToken);
	}

	private async ValueTask ApplyReceiptAsync(TrackedSwap swap,
		OrcaTransactionReceipt receipt, CancellationToken cancellationToken)
	{
		var state = receipt.IsSuccessful ? OrderStates.Done : OrderStates.Failed;
		SwapExecution? execution = null;
		if (state == OrderStates.Done)
		{
			try
			{
				execution = ReadSwapExecution(swap, receipt);
			}
			catch (InvalidDataException error)
			{
				await SendOutErrorAsync(error, cancellationToken);
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
			if (state == OrderStates.Done && execution is not null &&
				!swap.IsTradeSent)
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
		(string Code, string Identity, int Decimals, BigInteger Amount)[]>
		LoadBalancesAsync(CancellationToken cancellationToken)
	{
		OrcaToken[] tokens;
		using (_sync.EnterScope())
			tokens = [.. _tokens.Values.GroupBy(static token => token.Mint,
				StringComparer.Ordinal).Select(static group => group.First())];
		var result = new List<
			(string Code, string Identity, int Decimals, BigInteger Amount)>
		{
			("SOL", OrcaExtensions.SystemProgramAddress, 9,
				await RpcClient.GetBalanceAsync(cancellationToken)),
		};
		for (var offset = 0; offset < tokens.Length; offset += 100)
		{
			var chunk = tokens.Skip(offset).Take(100).ToArray();
			var addresses = chunk.Select(token =>
				OrcaExtensions.AssociatedTokenAddress(RpcClient.WalletAddress,
					token.Mint, token.TokenProgram)).ToArray();
			var accounts = await RpcClient.GetAccountsAsync(addresses,
				cancellationToken);
			for (var index = 0; index < chunk.Length; index++)
			{
				var token = chunk[index];
				var amount = index < accounts.Length && accounts[index] is not null
					? DecodeTokenAmount(accounts[index], token.Mint)
					: 0;
				result.Add((token.Symbol, token.Mint, token.Decimals, amount));
			}
		}
		return [.. result];
	}

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		bool isForced, CancellationToken cancellationToken)
		=> await SendPortfolioSnapshotAsync(target, isForced,
			await LoadBalancesAsync(cancellationToken), cancellationToken);

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		bool isForced,
		(string Code, string Identity, int Decimals, BigInteger Amount)[] balances,
		CancellationToken cancellationToken)
	{
		foreach (var item in balances)
		{
			var current = item.Amount.FromBaseUnits(item.Decimals);
			var fingerprint = new BalanceFingerprint(current, 0m);
			var key = $"{target}:{item.Identity}";
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
					SecurityCode = item.Code,
					BoardCode = BoardCodes.Orca,
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
				Matches(subscription, swap)).OrderBy(static swap =>
					swap.SubmittedTime)];
		var skipped = 0;
		var delivered = 0;
		foreach (var swap in swaps)
		{
			var receipt = swap.State == OrderStates.Active
				? await RpcClient.GetReceiptAsync(swap.Signature,
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
			var key = $"{target}:{swap.Signature}";
			var isOrderRequired = false;
			var isTradeRequired = false;
			using (_sync.EnterScope())
			{
				var isKnown = _orderFingerprints.TryGetValue(key,
					out var previous);
				isOrderRequired = isForced || !isKnown ||
					previous.State != swap.State;
				isTradeRequired = swap.State == OrderStates.Done &&
					swap.IsTradeSent && (!isKnown || !previous.IsTradeSent);
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
		OrcaTransactionReceipt receipt,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = swap.Market.ToStockSharp(),
			ServerTime = receipt?.BlockTime ?? CurrentTime,
			PortfolioName = GetPortfolioName(),
			Side = swap.Side,
			OrderVolume = swap.Volume,
			Balance = swap.State == OrderStates.Active ? swap.Volume : 0m,
			OrderPrice = swap.Price,
			OrderType = OrderTypes.Market,
			OrderState = swap.State,
			OrderStringId = swap.Signature,
			TransactionId = swap.TransactionId,
			OriginalTransactionId = target,
			Commission = GetCommission(receipt),
			CommissionCurrency = "SOL",
		}, cancellationToken);

	private ValueTask SendSwapTradeAsync(TrackedSwap swap, long target,
		OrcaTransactionReceipt receipt,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = swap.Market.ToStockSharp(),
			ServerTime = receipt?.BlockTime ?? CurrentTime,
			PortfolioName = GetPortfolioName(),
			Side = swap.Side,
			OrderStringId = swap.Signature,
			TradeStringId = swap.Signature,
			TradePrice = swap.Price,
			TradeVolume = swap.Volume,
			TransactionId = swap.TransactionId,
			OriginalTransactionId = target,
			Commission = GetCommission(receipt),
			CommissionCurrency = "SOL",
		}, cancellationToken);

	private static decimal? GetCommission(OrcaTransactionReceipt receipt)
		=> receipt is null ? null : receipt.Fee / 1_000_000_000m;

	private static SwapExecution ReadSwapExecution(TrackedSwap swap,
		OrcaTransactionReceipt receipt)
	{
		var isAToB = swap.Side == Sides.Sell;
		var time = receipt.BlockTime ?? DateTime.UtcNow;
		var events = OrcaExtensions.DecodeEvents(swap.Signature,
			receipt.LogMessages, time).Where(orcaEvent =>
				orcaEvent.PoolAddress.Equals(swap.Market.PoolAddress,
					StringComparison.Ordinal) &&
				orcaEvent.IsAToB == isAToB).ToArray();
		if (events.Length == 0)
			throw new InvalidDataException(
				$"Successful Orca transaction '{swap.Signature}' contains " +
				"no matching trade event.");
		BigInteger baseAmount = 0;
		BigInteger quoteAmount = 0;
		foreach (var orcaEvent in events)
		{
			if (isAToB)
			{
				baseAmount += orcaEvent.InputAmount;
				quoteAmount += new BigInteger(orcaEvent.OutputAmount) -
					orcaEvent.OutputTransferFee;
			}
			else
			{
				baseAmount += new BigInteger(orcaEvent.OutputAmount) -
					orcaEvent.OutputTransferFee;
				quoteAmount += orcaEvent.InputAmount;
			}
		}
		var volume = baseAmount.FromBaseUnits(swap.Market.TokenA.Decimals);
		var quote = quoteAmount.FromBaseUnits(swap.Market.TokenB.Decimals);
		if (volume <= 0 || quote <= 0)
			throw new InvalidDataException(
				$"Orca transaction '{swap.Signature}' contains non-positive " +
				"execution amounts.");
		return new(quote / volume, volume);
	}

	private static bool Matches(OrderSubscription subscription,
		TrackedSwap swap)
	{
		if (!subscription.Signature.IsEmpty() &&
			!subscription.Signature.Equals(swap.Signature,
				StringComparison.Ordinal))
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

	private static ulong DecodeTokenAmount(OrcaRpcAccount account,
		string expectedMint)
	{
		var data = OrcaExtensions.DecodeAccountData(account);
		if (data.Length < 72)
			throw new InvalidDataException("SPL token account data is truncated.");
		var mint = new PublicKey(data.AsSpan(0, 32)).Key;
		if (!mint.Equals(expectedMint, StringComparison.Ordinal))
			throw new InvalidDataException(
				$"SPL token account belongs to mint '{mint}', not " +
				$"'{expectedMint}'.");
		return BinaryPrimitives.ReadUInt64LittleEndian(
			data.AsSpan(64, sizeof(ulong)));
	}

	private static string NormalizeTransactionSignature(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		byte[] bytes;
		try
		{
			bytes = Encoders.Base58.DecodeData(value);
		}
		catch (Exception error) when (error is FormatException or
			ArgumentException)
		{
			throw new InvalidOperationException(
				$"Invalid Solana transaction signature '{value}'.", error);
		}
		if (bytes.Length != 64)
			throw new InvalidOperationException(
				$"Invalid Solana transaction signature '{value}'.");
		return value;
	}
}
