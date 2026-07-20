namespace StockSharp.Raydium;

public partial class RaydiumMessageAdapter
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
				"Raydium swaps are immediate AMM market orders.");
		if (regMsg.Condition is not null)
			throw new NotSupportedException(
				"Raydium does not expose conditional orders.");
		if (regMsg.PostOnly == true)
			throw new NotSupportedException(
				"Post-only is not applicable to AMM swaps.");
		if (regMsg.TimeInForce is not null)
			throw new NotSupportedException(
				"Time-in-force is not applicable to AMM swaps.");
		if (!regMsg.UserOrderId.IsEmpty())
			throw new NotSupportedException(
				"A Raydium order identifier is its Solana transaction signature; " +
				"no client-order ID is stored on-chain.");
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException(
				"Raydium swap volume must be positive.");
		var baseUnits = volume.ToBaseUnits(market.TokenA.Decimals);
		if (baseUnits <= 0)
			throw new InvalidOperationException(
				"Raydium swap volume rounds to zero base units.");

		var quote = await _apiClient.GetQuoteAsync(market, regMsg.Side,
			baseUnits, GetSlippageBasisPoints(), cancellationToken);
		var computeUnitPrice = ComputeUnitPrice == 0
			? await _apiClient.GetPriorityFeeAsync(PriorityFeeLevel,
				cancellationToken)
			: ComputeUnitPrice;
		var routePools = await LoadRoutePoolsAsync(quote, cancellationToken);
		var swapAccounts = await ResolveSwapAccountsAsync(quote,
			cancellationToken);
		var transactions = await _apiClient.BuildSwapAsync(quote,
			RpcClient.WalletAddress, computeUnitPrice, IsNativeSolUsed,
			swapAccounts.InputAccount, swapAccounts.OutputAccount,
			cancellationToken);
		string signature = null;
		ulong setupFee = 0;
		for (var index = 0; index < transactions.Length; index++)
		{
			signature = await RpcClient.SendSerializedTransactionAsync(
				transactions[index].Transaction, cancellationToken);
			if (index + 1 >= transactions.Length)
				continue;
			var receipt = await WaitForReceiptAsync(signature,
				cancellationToken);
			if (!receipt.IsSuccessful)
				throw new InvalidOperationException(
					$"Raydium setup transaction '{signature}' failed.");
			setupFee = checked(setupFee + receipt.Fee);
		}
		var tracked = new TrackedSwap
		{
			TransactionId = regMsg.TransactionId,
			Signature = signature,
			Market = market,
			RoutePools = routePools,
			Side = regMsg.Side,
			Volume = volume,
			Price = GetQuotePrice(quote),
			SubmittedTime = CurrentTime,
			State = OrderStates.Active,
			SetupFee = setupFee,
		};
		using (_sync.EnterScope())
			_trackedSwaps[signature] = tracked;
		await SendSwapOrderAsync(tracked, regMsg.TransactionId, null,
			cancellationToken);
	}

	private async ValueTask<RaydiumPool[]> LoadRoutePoolsAsync(
		RaydiumQuote quote, CancellationToken cancellationToken)
	{
		var routeIds = quote.Data.RoutePlan.Select(static route =>
			route.PoolId.NormalizePublicKey()).Distinct(StringComparer.Ordinal)
			.ToArray();
		var result = new Dictionary<string, RaydiumPool>(StringComparer.Ordinal);
		using (_sync.EnterScope())
			foreach (var id in routeIds)
				if (_marketsByPool.TryGetValue(id, out var market))
				{
					var pool = market.Pools.FirstOrDefault(item =>
						item.PoolAddress.Equals(id, StringComparison.Ordinal));
					if (pool is not null)
						result[id] = pool;
				}
		var missing = routeIds.Where(id => !result.ContainsKey(id)).ToArray();
		for (var offset = 0; offset < missing.Length; offset += 100)
			foreach (var keys in await _apiClient.GetPoolKeysAsync(
				missing.Skip(offset).Take(100), cancellationToken))
			{
				var pool = new RaydiumPool
				{
					PoolAddress = keys.Id.NormalizePublicKey(),
					ProgramAddress = keys.ProgramId.NormalizePublicKey(),
					VaultA = keys.Vault.A.NormalizePublicKey(),
					VaultB = keys.Vault.B.NormalizePublicKey(),
					TokenA = CreateToken(keys.MintA, null),
					TokenB = CreateToken(keys.MintB, null),
				};
				result[pool.PoolAddress] = pool;
			}
		if (routeIds.Any(id => !result.ContainsKey(id)))
			throw new InvalidDataException(
				"Raydium API returned incomplete route-pool metadata.");
		foreach (var route in quote.Data.RoutePlan)
		{
			var pool = result[route.PoolId.NormalizePublicKey()];
			var mints = new[] { pool.TokenA.Mint, pool.TokenB.Mint };
			if (!mints.Contains(route.InputMint, StringComparer.Ordinal) ||
				!mints.Contains(route.OutputMint, StringComparer.Ordinal))
				throw new InvalidDataException(
					$"Raydium route metadata for pool '{pool.PoolAddress}' is " +
					"inconsistent.");
		}
		return [.. routeIds.Select(id => result[id])];
	}

	private async ValueTask<(string InputAccount, string OutputAccount)>
		ResolveSwapAccountsAsync(RaydiumQuote quote,
		CancellationToken cancellationToken)
	{
		var inputToken = quote.Side == Sides.Sell
			? quote.Market.TokenA
			: quote.Market.TokenB;
		var outputToken = quote.Side == Sides.Sell
			? quote.Market.TokenB
			: quote.Market.TokenA;
		var isInputNative = IsNativeSolUsed && inputToken.Mint.Equals(
			RaydiumExtensions.WrappedSolMint, StringComparison.Ordinal);
		var isOutputNative = IsNativeSolUsed && outputToken.Mint.Equals(
			RaydiumExtensions.WrappedSolMint, StringComparison.Ordinal);
		var inputAddress = isInputNative ? null :
			RaydiumExtensions.AssociatedTokenAddress(RpcClient.WalletAddress,
				inputToken.Mint, inputToken.TokenProgram);
		var outputAddress = isOutputNative ? null :
			RaydiumExtensions.AssociatedTokenAddress(RpcClient.WalletAddress,
				outputToken.Mint, outputToken.TokenProgram);
		var addresses = new[] { inputAddress, outputAddress }.Where(
			static address => !address.IsEmpty()).Distinct(StringComparer.Ordinal)
			.ToArray();
		var accounts = addresses.Length == 0
			? []
			: await RpcClient.GetAccountsAsync(addresses, cancellationToken);
		RaydiumRpcAccount FindAccount(string address)
		{
			if (address.IsEmpty())
				return null;
			var index = Array.IndexOf(addresses, address);
			return index >= 0 && index < accounts.Length ? accounts[index] : null;
		}
		var input = FindAccount(inputAddress);
		if (!isInputNative)
		{
			if (input is null)
				throw new InvalidOperationException(
					$"The wallet has no associated token account for " +
					$"'{inputToken.Symbol}'.");
			RaydiumExtensions.ValidateVaultAccount(input, inputToken,
				inputAddress);
		}
		var output = FindAccount(outputAddress);
		if (!isOutputNative && output is not null)
			RaydiumExtensions.ValidateVaultAccount(output, outputToken,
				outputAddress);
		return (inputAddress, output is null ? null : outputAddress);
	}

	private async ValueTask<RaydiumTransactionReceipt> WaitForReceiptAsync(
		string signature, CancellationToken cancellationToken)
	{
		for (var attempt = 0; attempt < 60; attempt++)
		{
			var receipt = await RpcClient.GetReceiptAsync(signature,
				cancellationToken);
			if (receipt is not null)
				return receipt;
			await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
		}
		throw new TimeoutException(
			$"Raydium transaction '{signature}' was not confirmed in time.");
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		_ = replaceMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"A broadcast Solana transaction cannot be replaced through Raydium.");
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		_ = cancelMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"Raydium has no cancellable order book.");
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		_ = cancelMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"Raydium has no open-order group to cancel.");
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
			BoardCode = BoardCodes.Raydium,
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
				"Raydium orders use Solana transaction signatures, not numeric " +
				"identifiers.");
		if (!statusMsg.UserId.IsEmpty())
			throw new NotSupportedException(
				"Raydium has no exchange-side user identifier.");
		if (statusMsg.SecurityIds.Length > 0)
			throw new NotSupportedException(
				"Use the primary security filter for Raydium order status.");
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
		RaydiumTransactionReceipt receipt, CancellationToken cancellationToken)
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
		RaydiumToken[] tokens;
		using (_sync.EnterScope())
			tokens = [.. _tokens.Values.GroupBy(static token => token.Mint,
				StringComparer.Ordinal).Select(static group => group.First())];
		var result = new List<
			(string Code, string Identity, int Decimals, BigInteger Amount)>
		{
			("SOL", RaydiumExtensions.SystemProgramAddress, 9,
				await RpcClient.GetBalanceAsync(cancellationToken)),
		};
		var duplicateSymbols = tokens.GroupBy(static token => token.Symbol,
			StringComparer.OrdinalIgnoreCase).Where(static group =>
				group.Count() > 1).Select(static group => group.Key).ToHashSet(
					StringComparer.OrdinalIgnoreCase);
		for (var offset = 0; offset < tokens.Length; offset += 100)
		{
			var chunk = tokens.Skip(offset).Take(100).ToArray();
			var addresses = chunk.Select(token =>
				RaydiumExtensions.AssociatedTokenAddress(RpcClient.WalletAddress,
					token.Mint, token.TokenProgram)).ToArray();
			var accounts = await RpcClient.GetAccountsAsync(addresses,
				cancellationToken);
			for (var index = 0; index < chunk.Length; index++)
			{
				var token = chunk[index];
				var amount = index < accounts.Length && accounts[index] is not null
					? DecodeTokenAmount(accounts[index], token.Mint)
					: 0;
				var code = duplicateSymbols.Contains(token.Symbol)
					? $"{token.Symbol}-{token.Mint[..6].ToUpperInvariant()}"
					: token.Symbol;
				result.Add((code, token.Mint, token.Decimals, amount));
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
					BoardCode = BoardCodes.Raydium,
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
		RaydiumTransactionReceipt receipt,
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
			Commission = GetCommission(swap, receipt),
			CommissionCurrency = "SOL",
		}, cancellationToken);

	private ValueTask SendSwapTradeAsync(TrackedSwap swap, long target,
		RaydiumTransactionReceipt receipt,
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
			Commission = GetCommission(swap, receipt),
			CommissionCurrency = "SOL",
		}, cancellationToken);

	private static decimal? GetCommission(TrackedSwap swap,
		RaydiumTransactionReceipt receipt)
		=> receipt is null
			? null
			: (swap.SetupFee + receipt.Fee) / 1_000_000_000m;

	private static SwapExecution ReadSwapExecution(TrackedSwap swap,
		RaydiumTransactionReceipt receipt)
	{
		var transaction = receipt.Transaction;
		var meta = transaction?.Meta;
		if (meta is null || transaction.Transaction?.Message?.AccountKeys is null)
			throw new InvalidDataException(
				$"Successful Raydium transaction '{swap.Signature}' has no " +
				"balance metadata.");
		var keys = transaction.Transaction.Message.AccountKeys
			.Concat(meta.LoadedAddresses?.Writable ?? [])
			.Concat(meta.LoadedAddresses?.ReadOnly ?? []).ToArray();
		var expectedBaseSign = swap.Side == Sides.Sell ? 1 : -1;
		var expectedQuoteSign = -expectedBaseSign;
		BigInteger baseAmount = 0;
		BigInteger quoteAmount = 0;
		foreach (var pool in swap.RoutePools)
		{
			ReadRouteTokenDelta(pool.VaultA, pool.TokenA, keys, meta,
				swap.Market.TokenA.Mint, expectedBaseSign, ref baseAmount);
			ReadRouteTokenDelta(pool.VaultB, pool.TokenB, keys, meta,
				swap.Market.TokenA.Mint, expectedBaseSign, ref baseAmount);
			ReadRouteTokenDelta(pool.VaultA, pool.TokenA, keys, meta,
				swap.Market.TokenB.Mint, expectedQuoteSign, ref quoteAmount);
			ReadRouteTokenDelta(pool.VaultB, pool.TokenB, keys, meta,
				swap.Market.TokenB.Mint, expectedQuoteSign, ref quoteAmount);
		}
		if (baseAmount <= 0 || quoteAmount <= 0)
			throw new InvalidDataException(
				$"Successful Raydium transaction '{swap.Signature}' contains " +
				"no matching endpoint-token balance changes.");
		var volume = baseAmount.FromBaseUnits(swap.Market.TokenA.Decimals);
		var quote = quoteAmount.FromBaseUnits(swap.Market.TokenB.Decimals);
		if (volume <= 0 || quote <= 0)
			throw new InvalidDataException(
				$"Raydium transaction '{swap.Signature}' contains non-positive " +
				"execution amounts.");
		return new(quote / volume, volume);
	}

	private static void ReadRouteTokenDelta(string vault, RaydiumToken token,
		string[] accountKeys, RaydiumRpcTransactionMeta meta,
		string expectedMint, int expectedSign, ref BigInteger amount)
	{
		if (!token.Mint.Equals(expectedMint, StringComparison.Ordinal))
			return;
		var index = Array.IndexOf(accountKeys, vault);
		if (index < 0 || !RaydiumExtensions.TryGetDelta(meta, index,
			token.Mint, out var delta) || delta.Sign != expectedSign)
			return;
		amount = BigInteger.Max(amount, BigInteger.Abs(delta));
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

	private static ulong DecodeTokenAmount(RaydiumRpcAccount account,
		string expectedMint)
	{
		var data = RaydiumExtensions.DecodeAccountData(account);
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
