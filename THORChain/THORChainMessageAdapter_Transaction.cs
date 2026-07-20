namespace StockSharp.THORChain;

public partial class THORChainMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(regMsg.PortfolioName);
		var market = GetMarket(regMsg.SecurityId);
		if (regMsg.Side != Sides.Sell)
			throw new NotSupportedException(
				"This connector signs native RUNE source transactions. Use a " +
				"sell order on a RUNE destination-asset market.");
		if (regMsg.OrderType is not (null or OrderTypes.Market))
			throw new NotSupportedException(
				"THORChain native swaps are immediate market operations.");
		if (regMsg.Condition is not THORChainOrderCondition condition)
			throw new InvalidOperationException(
				"THORChainOrderCondition with a destination address is " +
				"required.");
		if (condition.DestinationAddress.IsEmpty())
			throw new InvalidOperationException(
				"A destination-chain address is required for a THORChain swap.");
		if (condition.StreamingInterval is < 0 or > 100_000)
			throw new ArgumentOutOfRangeException(
				nameof(condition.StreamingInterval));
		if (condition.StreamingQuantity is < 0 or > 100_000)
			throw new ArgumentOutOfRangeException(
				nameof(condition.StreamingQuantity));
		if (condition.LiquidityToleranceBasisPoints is < 1 or > 10_000)
			throw new ArgumentOutOfRangeException(
				nameof(condition.LiquidityToleranceBasisPoints));
		if (regMsg.PostOnly == true)
			throw new NotSupportedException(
				"Post-only is not applicable to a THORChain swap.");
		if (regMsg.TimeInForce is not null)
			throw new NotSupportedException(
				"Time-in-force is not applicable to a THORChain swap.");
		if (!regMsg.UserOrderId.IsEmpty())
			throw new NotSupportedException(
				"The Cosmos transaction hash is the THORChain order " +
				"identifier; a client order ID cannot be embedded.");

		var volume = regMsg.Volume.Abs();
		var amount = volume.ToProtocolAmount();
		var tolerance = condition.LiquidityToleranceBasisPoints ??
			decimal.Round(SlippageTolerance * 100m, 0,
				MidpointRounding.AwayFromZero).To<int>();

		await _transactionGate.WaitAsync(cancellationToken);
		try
		{
			var account = await ApiClient.GetAccountAsync(
				Signer.WalletAddress, cancellationToken);
			ValidateAccount(account?.Account);
			var balanceResponse = await ApiClient.GetBalancesAsync(
				Signer.WalletAddress, cancellationToken);
			var network = await ApiClient.GetNetworkAsync(cancellationToken);
			var balance = GetRuneBalance(balanceResponse);
			var commission = network.NativeTransactionFeeRune
				.FromProtocolAmount("native transaction fee");
			if (balance < volume + commission)
				throw new InvalidOperationException(
					$"Insufficient RUNE balance. Required {volume + commission}, " +
					$"available {balance}.");

			var quote = await ApiClient.GetQuoteAsync(
				THORChainExtensions.RuneAsset, market.Asset, amount,
				condition.DestinationAddress, condition.RefundAddress,
				condition.StreamingInterval, condition.StreamingQuantity,
				tolerance, cancellationToken);
			ValidateTradingQuote(quote, market);
			var expectedOutput = quote.ExpectedOutput.FromProtocolAmount(
				"quote expected output");
			var accountNumber = ParseUnsigned(account.Account.AccountNumber,
				"account number");
			var sequence = ParseUnsigned(account.Account.Sequence,
				"account sequence");
			var transactionBytes = Signer.SignDeposit(amount, quote.Memo,
				_chainId, accountNumber, sequence);
			var expectedHash = Convert.ToHexString(
				SHA256.HashData(transactionBytes));
			var broadcast = await ApiClient.BroadcastAsync(new()
			{
				TransactionBytes = Convert.ToBase64String(transactionBytes),
				Mode = THORChainBroadcastModes.Sync,
			}, cancellationToken);
			var response = broadcast?.Transaction ?? throw new
				InvalidDataException(
					"THORNode returned no transaction broadcast result.");
			if (response.Code != 0)
				throw new InvalidOperationException(
					$"THORChain rejected the transaction with code " +
					$"{response.Code}: {response.RawLog}");
			var transactionHash = response.TransactionHash.IsEmpty()
				? expectedHash
				: response.TransactionHash.NormalizeTransactionHash();
			if (!transactionHash.Equals(expectedHash,
				StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException(
					"THORNode returned a transaction hash that does not match " +
					"the signed transaction bytes.");

			var tracked = new TrackedSwap
			{
				TransactionId = regMsg.TransactionId,
				TransactionHash = transactionHash,
				Market = market,
				Volume = volume,
				Price = expectedOutput / volume,
				QuoteVolume = expectedOutput,
				Commission = commission,
				DestinationAddress = condition.DestinationAddress.Trim(),
				Memo = quote.Memo,
				SubmittedTime = CurrentTime,
				State = OrderStates.Active,
			};
			using (_sync.EnterScope())
				_trackedSwaps[transactionHash] = tracked;
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
			"A broadcast THORChain inbound transaction cannot be replaced " +
			"through the protocol.");
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		_ = cancelMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"THORChain swaps are irreversible after the native transaction " +
			"is broadcast and cannot be cancelled.");
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		_ = cancelMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"THORChain has no cancellable order group.");
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
			BoardCode = BoardCodes.THORChain,
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
				"THORChain orders use transaction hashes, not numeric IDs.");
		if (!statusMsg.UserId.IsEmpty())
			throw new NotSupportedException(
				"THORChain has no exchange-side user identifier.");
		if (statusMsg.SecurityIds.Length > 0)
			throw new NotSupportedException(
				"Use the primary security filter for THORChain order status.");
		var hash = statusMsg.OrderStringId.IsEmpty()
			? null
			: statusMsg.OrderStringId.NormalizeTransactionHash();
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
			Maximum = (statusMsg.Count ?? HistoryMaximum)
				.Min(HistoryMaximum).Max(1).To<int>(),
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

	private void ValidateAccount(THORChainAccount account)
	{
		ArgumentNullException.ThrowIfNull(account);
		if (account.Type != THORChainAccountTypes.BaseAccount)
			throw new NotSupportedException(
				$"Unsupported THORChain account type '{account.Type}'.");
		if (!account.Address.NormalizeThorAddress().Equals(
			Signer.WalletAddress, StringComparison.Ordinal))
			throw new InvalidDataException(
				"THORNode returned an account for a different wallet.");
		_ = ParseUnsigned(account.AccountNumber, "account number");
		_ = ParseUnsigned(account.Sequence, "account sequence");
	}

	private static void ValidateTradingQuote(THORChainQuote quote,
		THORChainMarket market)
	{
		ArgumentNullException.ThrowIfNull(quote);
		var nowSeconds = (long)(DateTime.UtcNow - DateTime.UnixEpoch)
			.TotalSeconds;
		if (quote.Expiry <= nowSeconds + 20)
			throw new InvalidDataException(
				"THORChain returned an expired or nearly expired quote.");
		if (quote.Memo.IsEmpty())
			throw new InvalidDataException(
				"THORChain returned no transaction memo for the swap.");
		if (quote.ExpectedOutput.ParseInteger("quote expected output") <= 0)
			throw new InvalidDataException(
				"THORChain returned a non-positive expected output.");
		if (quote.Fees?.Asset.IsEmpty() != false ||
			!quote.Fees.Asset.EqualsIgnoreCase(market.Asset))
			throw new InvalidDataException(
				"THORChain returned swap fees in an unexpected asset.");
	}

	private static ulong ParseUnsigned(string value, string field)
	{
		if (!ulong.TryParse(value, NumberStyles.None,
			CultureInfo.InvariantCulture, out var result))
			throw new InvalidDataException(
				$"THORChain returned invalid {field} '{value}'.");
		return result;
	}

	private static decimal GetRuneBalance(THORChainBalancesResponse response)
	{
		ArgumentNullException.ThrowIfNull(response);
		var coin = (response.Balances ?? []).FirstOrDefault(item =>
			item?.Denomination.EqualsIgnoreCase(
				THORChainExtensions.RuneDenomination) == true);
		return coin is null
			? 0m
			: coin.Amount.FromProtocolAmount("RUNE balance");
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
			var balance = GetRuneBalance(await ApiClient.GetBalancesAsync(
				Signer.WalletAddress, cancellationToken));
			foreach (var target in portfolioTargets)
				await SendPortfolioSnapshotAsync(target, false, balance,
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
		var page = await ApiClient.GetActionsAsync(null, null,
			swap.TransactionHash, 1, 0, cancellationToken);
		var action = page?.Actions?.FirstOrDefault();
		if (action is not null)
		{
			await ApplyActionAsync(swap, action, cancellationToken);
			return;
		}
		var transaction = await ApiClient.TryGetTransactionAsync(
			swap.TransactionHash, cancellationToken);
		if (transaction?.Transaction is { Code: not 0 } failed)
		{
			var isChanged = false;
			using (_sync.EnterScope())
			{
				isChanged = swap.State != OrderStates.Failed;
				swap.State = OrderStates.Failed;
				swap.FailureReason = failed.RawLog;
			}
			if (isChanged)
				await SendSwapOrderAsync(swap, swap.TransactionId,
					cancellationToken);
		}
	}

	private async ValueTask ApplyActionAsync(TrackedSwap swap,
		THORChainAction action, CancellationToken cancellationToken)
	{
		var state = action.Status switch
		{
			THORChainActionStatuses.Pending => OrderStates.Active,
			THORChainActionStatuses.Success => OrderStates.Done,
			THORChainActionStatuses.Refund => OrderStates.Failed,
			_ => OrderStates.Active,
		};
		decimal? output = null;
		if (TryFindCoin(action.Outputs, swap.Market.Asset, out _,
			out var outputCoin))
			output = outputCoin.Amount.FromProtocolAmount("swap output");
		var isOrderChanged = false;
		var isTradeRequired = false;
		using (_sync.EnterScope())
		{
			if (output is > 0)
			{
				swap.QuoteVolume = output.Value;
				swap.Price = output.Value / swap.Volume;
			}
			swap.Action = action;
			swap.FailureReason = action.Metadata?.Refund?.Reason ??
				action.Metadata?.Failed?.Reason;
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

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		bool isForced, CancellationToken cancellationToken)
		=> await SendPortfolioSnapshotAsync(target, isForced,
			GetRuneBalance(await ApiClient.GetBalancesAsync(
				Signer.WalletAddress, cancellationToken)), cancellationToken);

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		bool isForced, decimal current,
		CancellationToken cancellationToken)
	{
		var fingerprint = new BalanceFingerprint(current, 0m);
		var key = $"{target}:RUNE";
		using (_sync.EnterScope())
		{
			if (!isForced && _balanceFingerprints.TryGetValue(key,
				out var previous) && previous == fingerprint)
				return;
			_balanceFingerprints[key] = fingerprint;
		}
		await SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(),
			SecurityId = new()
			{
				SecurityCode = "RUNE",
				BoardCode = BoardCodes.THORChain,
			},
			ServerTime = CurrentTime,
			OriginalTransactionId = target,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, current, true)
		.TryAdd(PositionChangeTypes.BlockedValue, 0m, true),
			cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(
		OrderSubscription subscription, long target, bool isForced,
		CancellationToken cancellationToken)
	{
		var remote = await LoadWalletSwapsAsync(cancellationToken);
		TrackedSwap[] local;
		using (_sync.EnterScope())
			local = [.. _trackedSwaps.Values];
		var combined = remote.ToDictionary(static swap =>
			swap.TransactionHash, StringComparer.OrdinalIgnoreCase);
		foreach (var swap in local)
			combined[swap.TransactionHash] = swap;
		var swaps = combined.Values.Where(swap => Matches(subscription, swap))
			.OrderBy(static swap => swap.SubmittedTime).ToArray();
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

	private async ValueTask<TrackedSwap[]> LoadWalletSwapsAsync(
		CancellationToken cancellationToken)
	{
		var result = new List<TrackedSwap>();
		for (var offset = 0; offset < HistoryMaximum; offset += 50)
		{
			var page = await ApiClient.GetActionsAsync(null,
				Signer.WalletAddress, null, 50, offset, cancellationToken);
			var actions = page?.Actions ?? [];
			foreach (var action in actions)
				if (TryCreateWalletSwap(action, out var swap))
					result.Add(swap);
			if (actions.Length < 50)
				break;
		}
		return [.. result.GroupBy(static swap => swap.TransactionHash,
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.OrderByDescending(swap =>
				swap.SubmittedTime).First())];
	}

	private bool TryCreateWalletSwap(THORChainAction action,
		out TrackedSwap swap)
	{
		swap = null;
		try
		{
			if (action is null || action.Type != THORChainActionTypes.Swap ||
				!TryFindCoin(action.Inputs, THORChainExtensions.RuneAsset,
					out var inputTransaction, out var inputCoin) ||
				!inputTransaction.Address.EqualsIgnoreCase(
					Signer.WalletAddress))
				return false;
			THORChainMarket market = null;
			THORChainCoinAmount outputCoin = null;
			THORChainActionTransaction outputTransaction = null;
			using (_sync.EnterScope())
				foreach (var candidate in _marketsByAsset.Values)
					if (TryFindCoin(action.Outputs, candidate.Asset,
						out outputTransaction, out outputCoin))
					{
						market = candidate;
						break;
					}
			if (market is null)
				return false;
			var volume = inputCoin.Amount.FromProtocolAmount("swap input");
			var quoteVolume = outputCoin is null
				? 0m
				: outputCoin.Amount.FromProtocolAmount("swap output");
			var poolPrice = THORChainExtensions.ParseDecimal(
				market.Pool.AssetPrice, "pool asset price");
			var state = action.Status switch
			{
				THORChainActionStatuses.Pending => OrderStates.Active,
				THORChainActionStatuses.Success => OrderStates.Done,
				THORChainActionStatuses.Refund => OrderStates.Failed,
				_ => OrderStates.Active,
			};
			swap = new()
			{
				TransactionHash = inputTransaction.TransactionId
					.NormalizeTransactionHash(),
				Market = market,
				Volume = volume,
				QuoteVolume = quoteVolume,
				Price = quoteVolume > 0 ? quoteVolume / volume : 1m / poolPrice,
				DestinationAddress = outputTransaction?.Address,
				Memo = action.Metadata?.Swap?.Memo,
				SubmittedTime = action.Date.ParseActionTime(),
				State = state,
				FailureReason = action.Metadata?.Refund?.Reason ??
					action.Metadata?.Failed?.Reason,
				Action = action,
			};
			return true;
		}
		catch (Exception error) when (error is InvalidDataException or
			FormatException or OverflowException)
		{
			return false;
		}
	}

	private ValueTask SendSwapOrderAsync(TrackedSwap swap, long target,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = swap.Market.ToStockSharp(),
			ServerTime = swap.Action?.Date.IsEmpty() == false
				? swap.Action.Date.ParseActionTime()
				: swap.SubmittedTime,
			PortfolioName = GetPortfolioName(),
			Side = Sides.Sell,
			OrderVolume = swap.Volume,
			Balance = swap.State == OrderStates.Active ? swap.Volume : 0m,
			OrderPrice = swap.Price,
			OrderType = OrderTypes.Market,
			OrderState = swap.State,
			OrderStringId = swap.TransactionHash,
			TransactionId = swap.TransactionId,
			OriginalTransactionId = target,
			Commission = swap.Commission,
			CommissionCurrency = "RUNE",
		}, cancellationToken);

	private ValueTask SendSwapTradeAsync(TrackedSwap swap, long target,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = swap.Market.ToStockSharp(),
			ServerTime = swap.Action?.Date.IsEmpty() == false
				? swap.Action.Date.ParseActionTime()
				: CurrentTime,
			PortfolioName = GetPortfolioName(),
			Side = Sides.Sell,
			OrderStringId = swap.TransactionHash,
			TradeStringId = swap.TransactionHash,
			TradePrice = swap.Price,
			TradeVolume = swap.Volume,
			TransactionId = swap.TransactionId,
			OriginalTransactionId = target,
			Commission = swap.Commission,
			CommissionCurrency = "RUNE",
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
		if (subscription.Side is Sides side && side != Sides.Sell)
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
