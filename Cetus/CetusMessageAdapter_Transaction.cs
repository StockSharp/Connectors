namespace StockSharp.Cetus;

public partial class CetusMessageAdapter
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
				"Cetus swaps are immediate market operations.");
		if (regMsg.Condition is not null)
			throw new NotSupportedException(
				"Cetus does not expose conditional CLMM orders.");
		if (regMsg.PostOnly == true)
			throw new NotSupportedException(
				"Post-only is not applicable to a Cetus swap.");
		if (regMsg.TimeInForce is not null)
			throw new NotSupportedException(
				"Time-in-force is not applicable to a Cetus swap.");
		if (!regMsg.UserOrderId.IsEmpty())
			throw new NotSupportedException(
				"The Sui transaction digest is the Cetus order identifier.");
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException(
				"Cetus order volume must be positive.");
		var baseAmount = volume.ToBaseUnits(market.BaseToken.Decimals);
		if (baseAmount == 0)
			throw new InvalidOperationException(
				"Cetus order volume rounded to zero base units.");

		await _transactionGate.WaitAsync(cancellationToken);
		try
		{
			var quote = regMsg.Side == Sides.Sell
				? await ApiClient.GetExactInputQuoteAsync(market,
					market.BaseToken.CoinType, market.QuoteToken.CoinType,
					baseAmount, cancellationToken)
				: await ApiClient.GetExactOutputQuoteAsync(market,
					market.QuoteToken.CoinType, market.BaseToken.CoinType,
					baseAmount, cancellationToken);
			var slippageBasisPoints = decimal.Round(
				SlippageTolerance * 100m, 0,
				MidpointRounding.AwayFromZero).To<int>();
			var amountLimit = regMsg.Side == Sides.Sell
				? quote.OutputAmount.ApplyMinimumSlippage(slippageBasisPoints)
				: quote.InputAmount.ApplyMaximumSlippage(slippageBasisPoints);
			if (amountLimit == 0)
				throw new InvalidOperationException(
					"The configured slippage makes the protected swap amount zero.");

			var prepared = await SuiClient.PrepareSwapAsync(market, quote,
				amountLimit, _globalConfig, _clock, cancellationToken);
			var receipt = await SuiClient.ExecuteSwapAsync(prepared, quote,
				amountLimit, cancellationToken);
			if (!receipt.IsSuccessful)
				throw new InvalidOperationException(
					$"Cetus swap failed: {receipt.Error}");
			var swap = receipt.Swap ?? throw new InvalidDataException(
				"Successful Cetus execution returned no SwapEvent.");
			var execution = ReadSwapExecution(market, swap);
			if (execution.Side != regMsg.Side)
				throw new InvalidDataException(
					"Cetus execution returned the opposite swap direction.");
			var tracked = new TrackedSwap
			{
				TransactionId = regMsg.TransactionId,
				TransactionDigest = receipt.TransactionDigest,
				Market = market,
				Side = execution.Side,
				Volume = execution.Volume,
				Price = execution.Price,
				SubmittedTime = receipt.Time,
				State = OrderStates.Done,
				Receipt = receipt,
			};
			using (_sync.EnterScope())
				_trackedSwaps.Add(tracked.TransactionDigest, tracked);
			await SendSwapOrderAsync(tracked, regMsg.TransactionId,
				cancellationToken);
			await SendSwapTradeAsync(tracked, regMsg.TransactionId,
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
			"An executed Cetus swap cannot be replaced.");
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		_ = cancelMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"Cetus swaps are final after Sui execution.");
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		_ = cancelMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"Cetus has no cancellable order group.");
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
			BoardCode = BoardCodes.Cetus,
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
				"Cetus orders use Sui transaction digests, not numeric IDs.");
		if (!statusMsg.UserId.IsEmpty())
			throw new NotSupportedException(
				"Cetus has no exchange-side user identifier.");
		if (statusMsg.SecurityIds.Length > 0)
			throw new NotSupportedException(
				"Use the primary security filter for Cetus order status.");
		var subscription = new OrderSubscription
		{
			TransactionDigest = statusMsg.OrderStringId.IsEmpty()
				? null
				: statusMsg.OrderStringId.NormalizeTransactionDigest(),
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

	private async ValueTask PollPrivateAsync(
		CancellationToken cancellationToken)
	{
		long[] portfolioTargets;
		KeyValuePair<long, OrderSubscription>[] orderTargets;
		using (_sync.EnterScope())
		{
			portfolioTargets = [.. _portfolioSubscriptions];
			orderTargets = [.. _orderSubscriptions];
		}
		if (portfolioTargets.Length > 0)
		{
			var balances = await SuiClient.GetBalancesAsync(cancellationToken);
			foreach (var target in portfolioTargets)
				await SendPortfolioSnapshotAsync(target, false, balances,
					cancellationToken);
		}
		foreach (var target in orderTargets)
			await SendOrderSnapshotAsync(target.Value, target.Key, false,
				cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		bool isForced, CancellationToken cancellationToken)
		=> await SendPortfolioSnapshotAsync(target, isForced,
			await SuiClient.GetBalancesAsync(cancellationToken),
			cancellationToken);

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		bool isForced, Balance[] balances,
		CancellationToken cancellationToken)
	{
		CetusToken[] tokens;
		using (_sync.EnterScope())
			tokens = [.. _tokens.Values];
		foreach (var token in tokens.OrderBy(static item => item.Symbol,
			StringComparer.OrdinalIgnoreCase))
		{
			var balance = balances.FirstOrDefault(item =>
				!item.CoinType.IsEmpty() &&
				item.CoinType.NormalizeCoinType() == token.CoinType);
			var current = (balance?.Balance_ ?? 0UL).FromBaseUnits(token.Decimals);
			var fingerprint = new BalanceFingerprint(current, 0m);
			var key = $"{target}:{token.CoinType}";
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
					BoardCode = BoardCodes.Cetus,
				},
				ServerTime = DateTime.UtcNow,
				OriginalTransactionId = target,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, current, true)
			.TryAdd(PositionChangeTypes.BlockedValue, 0m, true),
				cancellationToken);
		}
	}

	private static string GetTokenSecurityCode(CetusToken token,
		CetusToken[] tokens)
	{
		if (tokens.Count(candidate => candidate.Symbol.Equals(
			token.Symbol, StringComparison.OrdinalIgnoreCase)) == 1)
			return token.Symbol;
		var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
			token.CoinType)));
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
			var key = $"{target}:{swap.TransactionDigest}";
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
			ServerTime = swap.SubmittedTime,
			PortfolioName = GetPortfolioName(),
			Side = swap.Side,
			OrderVolume = swap.Volume,
			Balance = 0m,
			OrderPrice = swap.Price,
			OrderType = OrderTypes.Market,
			OrderState = swap.State,
			OrderStringId = swap.TransactionDigest,
			TransactionId = swap.TransactionId,
			OriginalTransactionId = target,
			Commission = GetCommission(swap.Receipt?.GasUsed),
			CommissionCurrency = "SUI",
		}, cancellationToken);

	private ValueTask SendSwapTradeAsync(TrackedSwap swap, long target,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = swap.Market.ToStockSharp(),
			ServerTime = swap.SubmittedTime,
			PortfolioName = GetPortfolioName(),
			Side = swap.Side,
			OrderStringId = swap.TransactionDigest,
			TradeStringId = swap.TransactionDigest,
			TradePrice = swap.Price,
			TradeVolume = swap.Volume,
			TransactionId = swap.TransactionId,
			OriginalTransactionId = target,
			Commission = GetCommission(swap.Receipt?.GasUsed),
			CommissionCurrency = "SUI",
		}, cancellationToken);

	private static decimal? GetCommission(GasCostSummary gas)
	{
		if (gas is null)
			return null;
		var value = (BigInteger)gas.ComputationCost + gas.StorageCost -
			gas.StorageRebate;
		if (value <= 0)
			return null;
		return (decimal)value / 1_000_000_000m;
	}

	private static bool Matches(OrderSubscription subscription,
		TrackedSwap swap)
	{
		if (!subscription.TransactionDigest.IsEmpty() &&
			!subscription.TransactionDigest.Equals(
				swap.TransactionDigest, StringComparison.Ordinal))
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
