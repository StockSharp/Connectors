namespace StockSharp.SunIo;

public partial class SunIoMessageAdapter
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
				"This connector signs native TRX source transactions. Use a sell " +
				"order on a TRX/token market.");
		if (regMsg.OrderType is not (null or OrderTypes.Market))
			throw new NotSupportedException(
				"SUN.io swaps are immediate market operations.");
		if (regMsg.Condition is not null and not SunIoOrderCondition)
			throw new InvalidOperationException(
				"SUN.io orders accept only SunIoOrderCondition.");
		var condition = (SunIoOrderCondition)regMsg.Condition;
		var tolerance = condition?.SlippageToleranceBasisPoints ??
			decimal.Round(SlippageTolerance * 100m, 0,
				MidpointRounding.AwayFromZero).To<int>();
		if (tolerance is < 1 or > 9_999)
			throw new ArgumentOutOfRangeException(
				nameof(condition.SlippageToleranceBasisPoints));
		var deadlineInterval = condition?.DeadlineInterval ?? DeadlineInterval;
		if (deadlineInterval < TimeSpan.FromMinutes(1) ||
			deadlineInterval > TimeSpan.FromDays(1))
			throw new ArgumentOutOfRangeException(
				nameof(condition.DeadlineInterval));
		if (regMsg.PostOnly == true)
			throw new NotSupportedException(
				"Post-only is not applicable to a SUN.io swap.");
		if (regMsg.TimeInForce is not null)
			throw new NotSupportedException(
				"Time-in-force is not applicable to a SUN.io swap.");
		if (!regMsg.UserOrderId.IsEmpty())
			throw new NotSupportedException(
				"The TRON transaction hash is the SUN.io order identifier.");

		var volume = regMsg.Volume.Abs();
		var amount = volume.ToRawAmount(6);
		if (amount > long.MaxValue)
			throw new OverflowException(
				"SUN.io TRX input exceeds the TRON transaction range.");
		var feeLimitRaw = FeeLimit.ToRawAmount(6);
		if (feeLimitRaw > long.MaxValue)
			throw new OverflowException(
				"SUN.io fee limit exceeds the TRON transaction range.");

		await _transactionGate.WaitAsync(cancellationToken);
		try
		{
			var account = await ApiClient.GetAccountAsync(Signer.WalletAddress,
				cancellationToken);
			if (account.Balance < (long)amount)
				throw new InvalidOperationException(
					$"Insufficient TRX balance. Required {volume}, available " +
					$"{account.Balance / SunIoExtensions.TrxScale}.");
			var route = SelectBestRoute(await ApiClient.GetRoutesAsync(
				SunIoExtensions.NativeTrxAddress, market.Token.Address, amount,
				cancellationToken), SunIoExtensions.NativeTrxAddress,
				market.Token.Address, amount);
			var output = route.RawOutputAmount.ParseInteger(
				"route output amount");
			var minimumOutput = BigInteger.Max(1,
				output * (10_000 - tolerance) / 10_000);
			var nowSeconds = (long)(DateTime.UtcNow - DateTime.UnixEpoch)
				.TotalSeconds;
			var deadline = checked(nowSeconds +
				(long)deadlineInterval.TotalSeconds);
			var parameter = SunIoAbiEncoder.EncodeSwap(route, amount,
				minimumOutput, Signer.WalletAddress, deadline);
			var selector = SunIoAbiEncoder.FunctionSelector(
				SunIoExtensions.SwapFunctionSignature);
			var trigger = await ApiClient.TriggerSwapAsync(new()
			{
				OwnerAddress = Signer.WalletAddress,
				ContractAddress = SmartRouterAddress,
				FunctionSelector = SunIoExtensions.SwapFunctionSignature,
				Parameter = parameter,
				FeeLimit = (long)feeLimitRaw,
				CallValue = (long)amount,
				IsVisible = true,
			}, cancellationToken);
			if (trigger.Result?.IsSuccess != true)
				throw new InvalidOperationException(
					"TRON rejected the SUN.io simulation: " +
					SunIoApiClient.DecodeNodeMessage(trigger.Result?.Message));
			var transaction = trigger.Transaction ?? throw new
				InvalidDataException(
					"TRON returned no unsigned SUN.io transaction.");
			ValidateUnsignedTransaction(transaction, selector + parameter,
				(long)amount, (long)feeLimitRaw);
			transaction.Signatures =
			[
				Signer.SignTransaction(transaction.RawDataHex,
					transaction.TransactionId),
			];
			var broadcast = await ApiClient.BroadcastAsync(transaction,
				cancellationToken);
			if (broadcast?.IsSuccess != true)
				throw new InvalidOperationException(
					$"TRON broadcast failed: {broadcast?.Code} " +
					SunIoApiClient.DecodeNodeMessage(broadcast?.Message));
			var transactionHash = transaction.TransactionId
				.NormalizeTransactionHash();
			if (!broadcast.TransactionId.IsEmpty() &&
				!broadcast.TransactionId.NormalizeTransactionHash().Equals(
					transactionHash, StringComparison.Ordinal))
				throw new InvalidDataException(
					"TRON broadcast returned a different transaction ID.");
			var quoteVolume = route.RawOutputAmount.FromRawAmount(
				market.Token.Decimals, "route output amount");
			var tracked = new TrackedSwap
			{
				TransactionId = regMsg.TransactionId,
				TransactionHash = transactionHash,
				Market = market,
				Side = Sides.Sell,
				Volume = volume,
				QuoteVolume = quoteVolume,
				Price = quoteVolume / volume,
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
			"A broadcast SUN.io transaction cannot be replaced through the " +
			"protocol.");
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		_ = cancelMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"SUN.io swaps are irreversible after broadcast and cannot be " +
			"cancelled.");
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		_ = cancelMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"SUN.io has no cancellable order group.");
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
			BoardCode = BoardCodes.SunIo,
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
				"SUN.io orders use transaction hashes, not numeric IDs.");
		if (!statusMsg.UserId.IsEmpty())
			throw new NotSupportedException(
				"SUN.io has no exchange-side user identifier.");
		if (statusMsg.SecurityIds.Length > 0)
			throw new NotSupportedException(
				"Use the primary security filter for SUN.io order status.");
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

	private void ValidateUnsignedTransaction(SunIoTronTransaction transaction,
		string expectedData, long callValue, long feeLimit)
	{
		if (transaction.RawData is null || transaction.RawDataHex.IsEmpty() ||
			transaction.TransactionId.IsEmpty() ||
			transaction.RawData.Contracts is not { Length: 1 })
			throw new InvalidDataException(
				"TRON returned an incomplete unsigned transaction.");
		var contract = transaction.RawData.Contracts[0];
		var value = contract.Parameter?.Value;
		if (!transaction.IsVisible ||
			contract.Type != SunIoContractTypes.TriggerSmartContract ||
			contract.Parameter?.TypeUrl !=
				SunIoExtensions.TriggerContractTypeUrl ||
			contract.PermissionId is int permissionId && permissionId != 0 ||
			value is null || !SunIoSigner.AreSameAddresses(value.OwnerAddress,
				Signer.WalletAddress) ||
			!SunIoSigner.AreSameAddresses(value.ContractAddress,
				SmartRouterAddress) || value.CallValue != callValue ||
			!value.Data.Equals(expectedData,
				StringComparison.OrdinalIgnoreCase) ||
			transaction.RawData.FeeLimit != feeLimit)
			throw new InvalidDataException(
				"TRON changed the requested SUN.io contract transaction.");
		var now = (long)(DateTime.UtcNow - DateTime.UnixEpoch)
			.TotalMilliseconds;
		if (!IsHex(transaction.RawData.ReferenceBlockBytes, 4) ||
			!IsHex(transaction.RawData.ReferenceBlockHash, 16) ||
			transaction.RawData.Timestamp <= 0 ||
			transaction.RawData.Timestamp > now + TimeSpan.FromMinutes(2)
				.TotalMilliseconds ||
			transaction.RawData.Expiration <= transaction.RawData.Timestamp ||
			transaction.RawData.Expiration <= now ||
			transaction.RawData.Expiration > now + TimeSpan.FromHours(1)
			.TotalMilliseconds)
			throw new InvalidDataException(
				"TRON returned an invalid transaction validity window.");
	}

	private static bool IsHex(string value, int length)
		=> value?.Length == length &&
			value.All(static character => Uri.IsHexDigit(character));

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
		var page = await ApiClient.GetRouterTransactionsAsync(
			swap.Market.Token.Address, Signer.WalletAddress,
			swap.SubmittedTime - TimeSpan.FromMinutes(5), DateTime.UtcNow,
			100, null, cancellationToken);
		var indexed = (page.Items ?? []).FirstOrDefault(item =>
			item?.TransactionId.EqualsIgnoreCase(swap.TransactionHash) == true);
		if (indexed is not null)
		{
			await ApplyIndexedTransactionAsync(swap, indexed,
				cancellationToken);
			return;
		}
		var info = await ApiClient.TryGetTransactionInfoAsync(
			swap.TransactionHash, cancellationToken);
		if (info is null)
			return;
		var result = info.Receipt?.Result;
		if (result is not null and not SunIoReceiptResults.Success)
		{
			var isChanged = false;
			using (_sync.EnterScope())
			{
				isChanged = swap.State != OrderStates.Failed;
				swap.State = OrderStates.Failed;
				swap.Commission = info.Fee / SunIoExtensions.TrxScale;
				swap.FailureReason =
					SunIoApiClient.DecodeNodeMessage(info.ResultMessage);
			}
			if (isChanged)
				await SendSwapOrderAsync(swap, swap.TransactionId,
					cancellationToken);
			return;
		}
		if (info.Receipt is null)
			return;
		var output = swap.QuoteVolume;
		var contractResult = info.ContractResults?.FirstOrDefault();
		if (!contractResult.IsEmpty())
		{
			var raw = SunIoAbiEncoder.DecodeLastUnsignedArrayValue(
				contractResult);
			output = raw.ToString(CultureInfo.InvariantCulture).FromRawAmount(
				swap.Market.Token.Decimals, "confirmed swap output");
		}
		var sendTrade = false;
		using (_sync.EnterScope())
		{
			swap.QuoteVolume = output;
			swap.Price = output / swap.Volume;
			swap.Commission = info.Fee / SunIoExtensions.TrxScale;
			swap.State = OrderStates.Done;
			if (!swap.IsTradeSent)
			{
				swap.IsTradeSent = true;
				sendTrade = true;
			}
		}
		await SendSwapOrderAsync(swap, swap.TransactionId,
			cancellationToken);
		if (sendTrade)
			await SendSwapTradeAsync(swap, swap.TransactionId,
				cancellationToken);
	}

	private async ValueTask ApplyIndexedTransactionAsync(TrackedSwap swap,
		SunIoRouterTransaction transaction,
		CancellationToken cancellationToken)
	{
		if (!TryCreateTrade(swap.Market, transaction, out var trade))
			throw new InvalidDataException(
				"SUN.io indexed the transaction with unexpected swap assets.");
		var sendTrade = false;
		using (_sync.EnterScope())
		{
			swap.Price = trade.Price;
			swap.QuoteVolume = trade.QuoteVolume;
			swap.Transaction = transaction;
			swap.State = OrderStates.Done;
			if (!swap.IsTradeSent)
			{
				swap.IsTradeSent = true;
				sendTrade = true;
			}
		}
		await SendSwapOrderAsync(swap, swap.TransactionId,
			cancellationToken);
		if (sendTrade)
			await SendSwapTradeAsync(swap, swap.TransactionId,
				cancellationToken);
	}

	private async ValueTask<(string Code, SecurityId SecurityId,
		decimal Current)[]> LoadBalancesAsync(
		CancellationToken cancellationToken)
	{
		var result = new List<(string, SecurityId, decimal)>();
		var account = await ApiClient.GetAccountAsync(Signer.WalletAddress,
			cancellationToken);
		if (account.Balance < 0)
			throw new InvalidDataException(
				"TRON returned a negative account balance.");
		result.Add(("TRX", new()
		{
			SecurityCode = "TRX",
			BoardCode = BoardCodes.SunIo,
		}, account.Balance / SunIoExtensions.TrxScale));
		SunIoMarket[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values];
		foreach (var market in markets)
		{
			var raw = await ApiClient.GetTokenBalanceAsync(market.Token.Address,
				Signer.WalletAddress, cancellationToken);
			var current = raw.ToString(CultureInfo.InvariantCulture)
				.FromRawAmount(market.Token.Decimals, "TRC-20 balance");
			result.Add((market.BalanceCode, new()
			{
				SecurityCode = market.BalanceCode,
				BoardCode = BoardCodes.SunIo,
			}, current));
		}
		return [.. result];
	}

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		bool isForced, CancellationToken cancellationToken)
		=> await SendPortfolioSnapshotAsync(target, isForced,
			await LoadBalancesAsync(cancellationToken), cancellationToken);

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		bool isForced, (string Code, SecurityId SecurityId, decimal Current)[]
		balances, CancellationToken cancellationToken)
	{
		foreach (var balance in balances)
		{
			var fingerprint = new BalanceFingerprint(balance.Current, 0m);
			var key = $"{target}:{balance.Code}";
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
				SecurityId = balance.SecurityId,
				ServerTime = CurrentTime,
				OriginalTransactionId = target,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, balance.Current, true)
			.TryAdd(PositionChangeTypes.BlockedValue, 0m, true),
				cancellationToken);
		}
	}

	private async ValueTask SendOrderSnapshotAsync(
		OrderSubscription subscription, long target, bool isForced,
		CancellationToken cancellationToken)
	{
		var remote = await LoadWalletSwapsAsync(subscription.From,
			subscription.To, cancellationToken);
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
		DateTime? from, DateTime? to, CancellationToken cancellationToken)
	{
		var result = new List<TrackedSwap>();
		string offset = null;
		for (var page = 0; result.Count < HistoryMaximum; page++)
		{
			var response = await ApiClient.GetRouterTransactionsAsync(null,
				Signer.WalletAddress, from, to, 100, offset,
				cancellationToken);
			var items = response.Items ?? [];
			foreach (var transaction in items)
				if (TryCreateWalletSwap(transaction, out var swap))
					result.Add(swap);
			if (items.Length == 0 || response.Meta?.IsMoreAvailable != true)
				break;
			var next = items[^1].Offset;
			if (next.IsEmpty() || next.Equals(offset,
				StringComparison.Ordinal))
				break;
			offset = next;
			if (page >= HistoryMaximum / 100)
				break;
		}
		return [.. result.GroupBy(static swap => swap.TransactionHash,
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.OrderByDescending(swap =>
				swap.SubmittedTime).First())];
	}

	private bool TryCreateWalletSwap(SunIoRouterTransaction transaction,
		out TrackedSwap swap)
	{
		swap = null;
		try
		{
			SunIoMarket[] markets;
			using (_sync.EnterScope())
				markets = [.. _markets.Values];
			foreach (var market in markets)
			{
				if (!TryCreateTrade(market, transaction, out var trade))
					continue;
				if (!trade.UserAddress.EqualsIgnoreCase(Signer.WalletAddress))
					return false;
				swap = new()
				{
					TransactionHash = trade.TransactionHash,
					Market = market,
					Side = trade.Side,
					Volume = trade.Volume,
					QuoteVolume = trade.QuoteVolume,
					Price = trade.Price,
					SubmittedTime = trade.Time,
					State = OrderStates.Done,
					Transaction = transaction,
				};
				return true;
			}
			return false;
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
			ServerTime = swap.Transaction?.SwapTime.IsEmpty() == false
				? swap.Transaction.SwapTime.ParseApiTime()
				: swap.SubmittedTime,
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
			CommissionCurrency = "TRX",
		}, cancellationToken);

	private ValueTask SendSwapTradeAsync(TrackedSwap swap, long target,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = swap.Market.ToStockSharp(),
			ServerTime = swap.Transaction?.SwapTime.IsEmpty() == false
				? swap.Transaction.SwapTime.ParseApiTime()
				: CurrentTime,
			PortfolioName = GetPortfolioName(),
			Side = swap.Side,
			OrderStringId = swap.TransactionHash,
			TradeStringId = swap.TransactionHash,
			TradePrice = swap.Price,
			TradeVolume = swap.Volume,
			TransactionId = swap.TransactionId,
			OriginalTransactionId = target,
			Commission = swap.Commission,
			CommissionCurrency = "TRX",
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
		if (subscription.Side is Sides side && side != swap.Side)
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
