namespace StockSharp.ManifestTrade;

public partial class ManifestTradeMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(regMsg.PortfolioName);
		var market = GetMarket(regMsg.SecurityId);
		ValidateOrderRequest(regMsg.OrderType ?? OrderTypes.Limit,
			regMsg.Condition, regMsg.PostOnly, regMsg.TimeInForce,
			regMsg.UserOrderId);
		if (!market.IsDirectTradingSupported)
			throw new NotSupportedException(
				"Direct Manifest Trade orders currently require legacy SPL " +
				"tokens; this Token-2022 market is read-only.");
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException(
				"Manifest Trade order volume must be positive.");
		var baseValue = volume.ToBaseUnits(market.BaseToken.Decimals);
		if (baseValue <= 0 || baseValue > ulong.MaxValue)
			throw new InvalidOperationException(
				"Manifest Trade order volume does not fit into base atoms.");
		var baseAtoms = (ulong)baseValue;
		await RefreshMarketAsync(market, cancellationToken);
		var computeUnitPrice = await GetComputeUnitPriceAsync(market,
			cancellationToken);
		TransactionInstruction[] instructions;
		var price = regMsg.Price;
		if (orderType == OrderTypes.Market)
		{
			if (regMsg.Price != 0)
				throw new InvalidOperationException(
					"A Manifest Trade market order must not specify a price.");
			var quote = market.GetQuote(regMsg.Side, baseAtoms,
				SlippageTolerance);
			instructions = ManifestTradeInstructionBuilder.BuildMarketOrder(
				market, quote, regMsg.Side, RpcClient.WalletAddress,
				checked((uint)ComputeUnitLimit), computeUnitPrice);
			price = new BigInteger(quote.QuoteAtoms).FromBaseUnits(
				market.QuoteToken.Decimals) / volume;
		}
		else
		{
			if (regMsg.Price <= 0)
				throw new InvalidOperationException(
					"A Manifest Trade limit order price must be positive.");
			var encoded = ManifestTradeExtensions.EncodePrice(regMsg.Price,
				market.BaseToken.Decimals, market.QuoteToken.Decimals);
			var seat = GetWalletSeat(market);
			var required = regMsg.Side == Sides.Sell
				? baseAtoms
				: ManifestTradeExtensions.RequiredQuoteAtoms(encoded.RawPrice,
					baseAtoms);
			var available = regMsg.Side == Sides.Sell
				? seat?.BaseWithdrawableAtoms ?? 0
				: seat?.QuoteWithdrawableAtoms ?? 0;
			var deposit = required > available ? required - available : 0;
			var nativeType = regMsg.PostOnly == true
				? ManifestTradeOrderTypes.PostOnly
				: regMsg.TimeInForce == TimeInForce.CancelBalance
					? ManifestTradeOrderTypes.ImmediateOrCancel
					: ManifestTradeOrderTypes.Limit;
			instructions = ManifestTradeInstructionBuilder.BuildLimitOrder(
				market, RpcClient.WalletAddress, regMsg.Side, baseAtoms,
				encoded.Mantissa, encoded.Exponent, nativeType,
				ManifestTradeExtensions.NoExpirationSlot, seat is null,
				seat?.Index, deposit, checked((uint)ComputeUnitLimit),
				computeUnitPrice);
		}
		var signature = await RpcClient.SendTransactionAsync(instructions,
			cancellationToken);
		var tracked = new TrackedOrder
		{
			TransactionId = regMsg.TransactionId,
			Signature = signature,
			Market = market,
			Side = regMsg.Side,
			Volume = volume,
			Balance = volume,
			Price = price,
			OrderType = orderType,
			SubmittedTime = CurrentTime,
			State = OrderStates.Active,
		};
		using (_sync.EnterScope())
			_trackedOrders[signature] = tracked;
		await SendTrackedOrderAsync(tracked, regMsg.TransactionId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(replaceMsg.PortfolioName);
		var previous = ResolveTrackedOrder(replaceMsg.OldOrderStringId,
			replaceMsg.OldOrderId, "replacement");
		if (previous.OrderType == OrderTypes.Market ||
			(replaceMsg.OrderType ?? OrderTypes.Limit) != OrderTypes.Limit)
			throw new NotSupportedException(
				"Manifest Trade can replace active limit orders only.");
		if (previous.Sequence is null)
			throw new InvalidOperationException(
				"The original Manifest order has not received its on-chain " +
				"sequence yet.");
		ValidateOrderRequest(OrderTypes.Limit, replaceMsg.Condition,
			replaceMsg.PostOnly, replaceMsg.TimeInForce,
			replaceMsg.UserOrderId);
		var market = previous.Market;
		if (!replaceMsg.SecurityId.SecurityCode.IsEmpty() &&
			!ReferenceEquals(GetMarket(replaceMsg.SecurityId), market))
			throw new InvalidOperationException(
				"A Manifest replacement must stay on the original market.");
		var volume = replaceMsg.Volume.Abs();
		if (volume <= 0 || replaceMsg.Price <= 0)
			throw new InvalidOperationException(
				"Manifest replacement price and volume must be positive.");
		var baseValue = volume.ToBaseUnits(market.BaseToken.Decimals);
		if (baseValue <= 0 || baseValue > ulong.MaxValue)
			throw new InvalidOperationException(
				"Manifest replacement volume does not fit into base atoms.");
		await RefreshMarketAsync(market, cancellationToken);
		var onChain = FindOrder(market, previous.Sequence.Value,
			RpcClient.WalletAddress) ?? throw new InvalidOperationException(
				"The original Manifest order is no longer active.");
		var encoded = ManifestTradeExtensions.EncodePrice(replaceMsg.Price,
			market.BaseToken.Decimals, market.QuoteToken.Decimals);
		var seat = GetWalletSeat(market) ?? throw new InvalidDataException(
			"The Manifest trader seat disappeared while replacing an order.");
		var required = replaceMsg.Side == Sides.Sell
			? (ulong)baseValue
			: ManifestTradeExtensions.RequiredQuoteAtoms(encoded.RawPrice,
				(ulong)baseValue);
		var released = previous.Side == Sides.Sell
			? onChain.BaseAtoms
			: ManifestTradeExtensions.RequiredQuoteAtoms(onChain.RawPrice,
				onChain.BaseAtoms);
		var available = replaceMsg.Side == Sides.Sell
			? seat.BaseWithdrawableAtoms
			: seat.QuoteWithdrawableAtoms;
		available = checked(available +
			(previous.Side == replaceMsg.Side ? released : 0));
		var deposit = required > available ? required - available : 0;
		var nativeType = replaceMsg.PostOnly == true
			? ManifestTradeOrderTypes.PostOnly
			: replaceMsg.TimeInForce == TimeInForce.CancelBalance
				? ManifestTradeOrderTypes.ImmediateOrCancel
				: ManifestTradeOrderTypes.Limit;
		var computeUnitPrice = await GetComputeUnitPriceAsync(market,
			cancellationToken);
		var instructions = ManifestTradeInstructionBuilder.BuildReplace(
			market, RpcClient.WalletAddress, replaceMsg.Side, (ulong)baseValue,
			encoded.Mantissa, encoded.Exponent, nativeType,
			ManifestTradeExtensions.NoExpirationSlot, seat.Index, deposit,
			previous.Sequence.Value, onChain.Index,
			checked((uint)ComputeUnitLimit), computeUnitPrice);
		var signature = await RpcClient.SendTransactionAsync(instructions,
			cancellationToken);
		var replacement = new TrackedOrder
		{
			TransactionId = replaceMsg.TransactionId,
			Signature = signature,
			Market = market,
			Side = replaceMsg.Side,
			Volume = volume,
			Balance = volume,
			Price = replaceMsg.Price,
			OrderType = OrderTypes.Limit,
			SubmittedTime = CurrentTime,
			State = OrderStates.Active,
		};
		using (_sync.EnterScope())
		{
			_trackedOrders[signature] = replacement;
			_pendingActions[signature] = new()
			{
				PreviousOrder = previous,
				ReplacementOrder = replacement,
			};
		}
		await SendTrackedOrderAsync(replacement, replaceMsg.TransactionId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		var tracked = ResolveTrackedOrder(cancelMsg.OrderStringId,
			cancelMsg.OrderId, "cancellation");
		if (tracked.Sequence is null)
			throw new InvalidOperationException(
				"The Manifest order has not received its on-chain sequence yet.");
		await RefreshMarketAsync(tracked.Market, cancellationToken);
		var order = FindOrder(tracked.Market, tracked.Sequence.Value,
			RpcClient.WalletAddress) ?? throw new InvalidOperationException(
				"The Manifest order is no longer active.");
		var seat = GetWalletSeat(tracked.Market) ?? throw new
			InvalidDataException("The Manifest trader seat is missing.");
		var computeUnitPrice = await GetComputeUnitPriceAsync(tracked.Market,
			cancellationToken);
		var instructions = ManifestTradeInstructionBuilder.BuildCancel(
			tracked.Market, RpcClient.WalletAddress, tracked.Sequence.Value,
			order.Index, seat.Index, checked((uint)ComputeUnitLimit),
			computeUnitPrice);
		var signature = await RpcClient.SendTransactionAsync(instructions,
			cancellationToken);
		using (_sync.EnterScope())
			_pendingActions[signature] = new() { PreviousOrder = tracked };
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"Manifest Trade group cancellation does not close positions.");
		TrackedOrder[] orders;
		using (_sync.EnterScope())
			orders = [.. _trackedOrders.Values.Where(order =>
				order.State == OrderStates.Active && order.Sequence is not null &&
				(cancelMsg.SecurityId.SecurityCode.IsEmpty() ||
					order.Market.SecurityCode.EqualsIgnoreCase(
						cancelMsg.SecurityId.SecurityCode)) &&
				(cancelMsg.Side is null || order.Side == cancelMsg.Side))];
		foreach (var order in orders)
		{
			await RefreshMarketAsync(order.Market, cancellationToken);
			var current = FindOrder(order.Market, order.Sequence.Value,
				RpcClient.WalletAddress);
			var seat = GetWalletSeat(order.Market);
			if (current is null || seat is null)
				continue;
			var fee = await GetComputeUnitPriceAsync(order.Market,
				cancellationToken);
			var signature = await RpcClient.SendTransactionAsync(
				ManifestTradeInstructionBuilder.BuildCancel(order.Market,
					RpcClient.WalletAddress, order.Sequence.Value, current.Index,
					seat.Index, checked((uint)ComputeUnitLimit), fee),
				cancellationToken);
			using (_sync.EnterScope())
				_pendingActions[signature] = new() { PreviousOrder = order };
		}
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
			BoardCode = BoardCodes.ManifestTrade,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await RefreshAllMarketsAsync(cancellationToken);
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
				"Manifest Trade uses string sequence or transaction IDs.");
		if (!statusMsg.UserId.IsEmpty())
			throw new NotSupportedException(
				"Manifest Trade core orders have no client-side user ID.");
		if (statusMsg.SecurityIds.Length > 0)
			throw new NotSupportedException(
				"Use the primary security filter for Manifest order status.");
		var subscription = new OrderSubscription
		{
			OrderStringId = statusMsg.OrderStringId,
			SecurityId = statusMsg.SecurityId,
			Side = statusMsg.Side,
			States = statusMsg.States,
			From = statusMsg.From?.ToUniversalTime(),
			To = statusMsg.To?.ToUniversalTime(),
			Skip = Math.Max(0, statusMsg.Skip ?? 0).Min(int.MaxValue).To<int>(),
			Maximum = (statusMsg.Count ?? 1000).Min(10000).Max(1).To<int>(),
		};
		await RefreshAllMarketsAsync(cancellationToken);
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
		KeyValuePair<string, PendingOrderAction>[] actions;
		TrackedOrder[] pendingOrders;
		long[] portfolioTargets;
		KeyValuePair<long, OrderSubscription>[] orderTargets;
		using (_sync.EnterScope())
		{
			actions = [.. _pendingActions];
			pendingOrders = [.. _trackedOrders.Values.Where(static order =>
				order.Receipt is null)];
			portfolioTargets = [.. _portfolioSubscriptions];
			orderTargets = [.. _orderSubscriptions];
		}
		foreach (var action in actions)
		{
			var receipt = await RpcClient.GetReceiptAsync(action.Key,
				cancellationToken);
			if (receipt is not null)
				await ApplyPendingActionAsync(action.Key, action.Value, receipt,
					cancellationToken);
		}
		foreach (var order in pendingOrders)
		{
			if (order.Receipt is not null)
				continue;
			var receipt = await RpcClient.GetReceiptAsync(order.Signature,
				cancellationToken);
			if (receipt is not null)
				await ApplyOrderReceiptAsync(order, receipt, cancellationToken);
		}
		if (portfolioTargets.Length > 0 || orderTargets.Length > 0)
			await RefreshAllMarketsAsync(cancellationToken);
		if (portfolioTargets.Length > 0)
		{
			var balances = await LoadBalancesAsync(cancellationToken);
			foreach (var target in portfolioTargets)
				await SendPortfolioSnapshotAsync(target, false, balances,
					cancellationToken);
		}
		foreach (var target in orderTargets)
			await SendOrderSnapshotAsync(target.Value, target.Key, false,
				cancellationToken);
	}

	private async ValueTask ApplyOrderReceiptAsync(TrackedOrder order,
		ManifestTradeTransactionReceipt receipt,
		CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
			order.Receipt = receipt;
		if (!receipt.IsSuccessful)
		{
			using (_sync.EnterScope())
			{
				order.State = OrderStates.Failed;
				order.Balance = 0m;
			}
			await SendTrackedOrderAsync(order, order.TransactionId,
				cancellationToken);
			return;
		}
		await ProcessPrivateEventsAsync(order.Signature, receipt.LogMessages,
			receipt, cancellationToken);
		await RefreshMarketAsync(order.Market, cancellationToken);
		var current = order.Sequence is ulong sequence
			? FindOrder(order.Market, sequence, RpcClient.WalletAddress)
			: null;
		using (_sync.EnterScope())
		{
			if (current is not null)
			{
				order.OrderIndex = current.Index;
				order.Balance = new BigInteger(current.BaseAtoms).FromBaseUnits(
					order.Market.BaseToken.Decimals);
				order.State = OrderStates.Active;
			}
			else
			{
				order.Balance = 0m;
				order.State = OrderStates.Done;
			}
		}
		await SendTrackedOrderAsync(order, order.TransactionId,
			cancellationToken);
	}

	private async ValueTask ApplyPendingActionAsync(string signature,
		PendingOrderAction action, ManifestTradeTransactionReceipt receipt,
		CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
			_pendingActions.Remove(signature);
		if (!receipt.IsSuccessful)
		{
			if (action.ReplacementOrder is { } failed)
			{
				using (_sync.EnterScope())
				{
					failed.Receipt = receipt;
					failed.State = OrderStates.Failed;
					failed.Balance = 0m;
				}
				await SendTrackedOrderAsync(failed, failed.TransactionId,
					cancellationToken);
			}
			return;
		}
		using (_sync.EnterScope())
		{
			action.PreviousOrder.State = OrderStates.Done;
			action.PreviousOrder.Balance = 0m;
		}
		await SendTrackedOrderAsync(action.PreviousOrder,
			action.PreviousOrder.TransactionId, cancellationToken);
		if (action.ReplacementOrder is { } replacement)
			await ApplyOrderReceiptAsync(replacement, receipt,
				cancellationToken);
	}

	private async ValueTask ProcessPrivateEventsAsync(string signature,
		string[] logs, ManifestTradeTransactionReceipt receipt,
		CancellationToken cancellationToken)
	{
		TrackedOrder submitted;
		using (_sync.EnterScope())
			_trackedOrders.TryGetValue(signature, out submitted);
		if (submitted is not null)
		{
			var placement = ManifestTradeExtensions.DecodePlaceEvents(logs)
				.FirstOrDefault(item => item.MarketAddress.Equals(
					submitted.Market.MarketAddress, StringComparison.Ordinal) &&
					item.Trader.Equals(RpcClient.WalletAddress,
						StringComparison.Ordinal));
			if (placement is not null)
			{
				using (_sync.EnterScope())
				{
					submitted.Sequence = placement.Sequence;
					submitted.OrderIndex = placement.OrderIndex ==
						ManifestTradeExtensions.NilIndex
						? null
						: placement.OrderIndex;
				}
			}
		}
		var time = receipt?.BlockTime ?? DateTime.UtcNow;
		foreach (var fill in ManifestTradeExtensions.DecodeFillEvents(signature,
			logs, time))
		{
			TrackedOrder tracked;
			var sequence = fill.Taker.Equals(RpcClient.WalletAddress,
				StringComparison.Ordinal)
				? fill.TakerSequence
				: fill.Maker.Equals(RpcClient.WalletAddress,
					StringComparison.Ordinal)
					? fill.MakerSequence
					: (ulong?)null;
			if (sequence is null)
				continue;
			using (_sync.EnterScope())
				tracked = _trackedOrders.Values.FirstOrDefault(order =>
					ReferenceEquals(order.Market,
						_marketsByAddress.GetValueOrDefault(fill.MarketAddress)) &&
					(order.Sequence == sequence ||
						order.Signature.Equals(signature,
							StringComparison.Ordinal)));
			if (tracked is null)
				continue;
			var trade = ToTrade(tracked.Market, fill);
			if (trade is null)
				continue;
			var key = $"{trade.Id}:{sequence.Value}";
			using (_sync.EnterScope())
			{
				if (!_seenPrivateExecutions.Add(key))
					continue;
				tracked.Balance = (tracked.Balance - trade.Volume).Max(0m);
			}
			await SendTrackedTradeAsync(tracked, trade,
				cancellationToken);
		}
		foreach (var cancellation in
			ManifestTradeExtensions.DecodeCancelEvents(logs))
		{
			TrackedOrder tracked;
			using (_sync.EnterScope())
				tracked = _trackedOrders.Values.FirstOrDefault(order =>
					order.Sequence == cancellation.Sequence &&
					order.Market.MarketAddress.Equals(
						cancellation.MarketAddress, StringComparison.Ordinal));
			if (tracked is null)
				continue;
			using (_sync.EnterScope())
			{
				tracked.State = OrderStates.Done;
				tracked.Balance = 0m;
			}
		}
	}

	private async ValueTask ReconcileOrdersAsync(ManifestTradeMarket market,
		CancellationToken cancellationToken)
	{
		TrackedOrder[] orders;
		using (_sync.EnterScope())
			orders = [.. _trackedOrders.Values.Where(order =>
				ReferenceEquals(order.Market, market) &&
				order.State == OrderStates.Active && order.Sequence is not null &&
				order.Receipt is not null && market.Slot >= order.Receipt.Slot)];
		foreach (var order in orders)
		{
			var current = FindOrder(market, order.Sequence.Value,
				RpcClient.WalletAddress);
			var balance = current is null ? 0m :
				new BigInteger(current.BaseAtoms).FromBaseUnits(
					market.BaseToken.Decimals);
			var changed = false;
			using (_sync.EnterScope())
			{
				var state = current is null
					? OrderStates.Done
					: OrderStates.Active;
				changed = order.State != state || order.Balance != balance;
				order.State = state;
				order.Balance = balance;
				order.OrderIndex = current?.Index;
			}
			if (changed)
				await SendTrackedOrderAsync(order, order.TransactionId,
					cancellationToken);
		}
	}

	private async ValueTask<
		(string Code, string Identity, int Decimals, BigInteger Current,
			BigInteger Blocked)[]> LoadBalancesAsync(
		CancellationToken cancellationToken)
	{
		ManifestTradeToken[] tokens;
		ManifestTradeMarket[] markets;
		using (_sync.EnterScope())
		{
			tokens = [.. _tokens.Values.GroupBy(static token => token.Mint,
				StringComparer.Ordinal).Select(static group => group.First())];
			markets = [.. _marketsByAddress.Values];
		}
		var values = tokens.ToDictionary(static token => token.Mint,
			static token => (Token: token, Current: BigInteger.Zero,
				Blocked: BigInteger.Zero), StringComparer.Ordinal);
		for (var offset = 0; offset < tokens.Length; offset += 100)
		{
			var chunk = tokens.Skip(offset).Take(100).ToArray();
			var addresses = chunk.Select(token =>
				ManifestTradeExtensions.AssociatedTokenAddress(
					RpcClient.WalletAddress, token.Mint, token.TokenProgram))
				.ToArray();
			var accounts = await RpcClient.GetAccountsAsync(addresses,
				cancellationToken);
			for (var index = 0; index < chunk.Length; index++)
			{
				var token = chunk[index];
				var amount = index < accounts.Length && accounts[index] is not null
					? DecodeTokenAmount(accounts[index], token.Mint)
					: 0;
				var item = values[token.Mint];
				values[token.Mint] = (item.Token, amount, item.Blocked);
			}
		}
		foreach (var market in markets)
		{
			var seat = GetWalletSeat(market);
			if (seat is null)
				continue;
			BigInteger lockedBase = 0;
			BigInteger lockedQuote = 0;
			foreach (var order in market.Asks.Where(order =>
				order.TraderIndex == seat.Index))
				lockedBase += order.BaseAtoms;
			foreach (var order in market.Bids.Where(order =>
				order.TraderIndex == seat.Index))
				lockedQuote += ManifestTradeExtensions.RequiredQuoteAtoms(
					order.RawPrice, order.BaseAtoms);
			AddVenueBalance(values, market.BaseToken,
				seat.BaseWithdrawableAtoms + lockedBase, lockedBase);
			AddVenueBalance(values, market.QuoteToken,
				seat.QuoteWithdrawableAtoms + lockedQuote, lockedQuote);
		}
		var result = values.Values.Select(static item =>
			(item.Token.Mint.Equals(ManifestTradeExtensions.WrappedSolMint,
				StringComparison.Ordinal) ? "WSOL" : item.Token.Symbol,
				item.Token.Mint, item.Token.Decimals,
				item.Current, item.Blocked)).ToList();
		result.Add(("SOL", ManifestTradeExtensions.SystemProgramAddress, 9,
			await RpcClient.GetBalanceAsync(cancellationToken), BigInteger.Zero));
		return [.. result];
	}

	private static void AddVenueBalance(
		IDictionary<string, (ManifestTradeToken Token, BigInteger Current,
			BigInteger Blocked)> values, ManifestTradeToken token,
		BigInteger current, BigInteger blocked)
	{
		var item = values[token.Mint];
		values[token.Mint] = (item.Token, item.Current + current,
			item.Blocked + blocked);
	}

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		bool isForced, CancellationToken cancellationToken)
		=> await SendPortfolioSnapshotAsync(target, isForced,
			await LoadBalancesAsync(cancellationToken), cancellationToken);

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		bool isForced,
		(string Code, string Identity, int Decimals, BigInteger Current,
			BigInteger Blocked)[] balances,
		CancellationToken cancellationToken)
	{
		foreach (var item in balances)
		{
			var current = item.Current.FromBaseUnits(item.Decimals);
			var blocked = item.Blocked.FromBaseUnits(item.Decimals);
			var fingerprint = new BalanceFingerprint(current, blocked);
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
					BoardCode = BoardCodes.ManifestTrade,
				},
				ServerTime = CurrentTime,
				OriginalTransactionId = target,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, current, true)
			.TryAdd(PositionChangeTypes.BlockedValue, blocked, true),
				cancellationToken);
		}
	}

	private async ValueTask SendOrderSnapshotAsync(
		OrderSubscription subscription, long target, bool isForced,
		CancellationToken cancellationToken)
	{
		TrackedOrder[] tracked;
		ManifestTradeMarket[] markets;
		using (_sync.EnterScope())
		{
			tracked = [.. _trackedOrders.Values.Where(order =>
				Matches(subscription, order)).OrderBy(static order =>
					order.SubmittedTime)];
			markets = [.. _marketsByAddress.Values];
		}
		var skipped = 0;
		var delivered = 0;
		foreach (var order in tracked)
		{
			if (subscription.States is { Length: > 0 } states &&
				!states.Contains(order.State))
				continue;
			if (skipped++ < subscription.Skip)
				continue;
			if (delivered++ >= subscription.Maximum)
				return;
			var key = $"{target}:{order.Signature}";
			var id = GetOrderStringId(order);
			var fingerprint = new OrderFingerprint(order.State, order.Balance,
				id, order.IsTradeSent);
			using (_sync.EnterScope())
			{
				if (!isForced && _orderFingerprints.TryGetValue(key,
					out var previous) && previous == fingerprint)
					continue;
				_orderFingerprints[key] = fingerprint;
			}
			await SendTrackedOrderAsync(order, target, cancellationToken);
		}
		if (subscription.States is { Length: > 0 } requestedStates &&
			!requestedStates.Contains(OrderStates.Active))
			return;
		var known = tracked.Where(static order => order.Sequence is not null)
			.Select(static order => (order.Market.MarketAddress,
				order.Sequence.Value)).ToHashSet();
		foreach (var market in markets)
		{
			if (!subscription.SecurityId.SecurityCode.IsEmpty() &&
				!subscription.SecurityId.SecurityCode.EqualsIgnoreCase(
					market.SecurityCode))
				continue;
			var seat = GetWalletSeat(market);
			if (seat is null)
				continue;
			foreach (var order in market.Bids.Concat(market.Asks).Where(order =>
				order.TraderIndex == seat.Index &&
				!known.Contains((market.MarketAddress, order.Sequence))))
			{
				var id = GetOrderStringId(market, order.Sequence);
				if (!subscription.OrderStringId.IsEmpty() &&
					!subscription.OrderStringId.Equals(id,
						StringComparison.Ordinal))
					continue;
				var side = order.IsBid ? Sides.Buy : Sides.Sell;
				if (subscription.Side is Sides requestedSide &&
					side != requestedSide)
					continue;
				if (skipped++ < subscription.Skip)
					continue;
				if (delivered++ >= subscription.Maximum)
					return;
				var volume = new BigInteger(order.BaseAtoms).FromBaseUnits(
					market.BaseToken.Decimals);
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					HasOrderInfo = true,
					SecurityId = market.ToStockSharp(),
					ServerTime = CurrentTime,
					PortfolioName = GetPortfolioName(),
					Side = side,
					OrderVolume = volume,
					Balance = volume,
					OrderPrice = ManifestTradeExtensions.RawPriceToTokenPrice(
						order.RawPrice, market.BaseToken.Decimals,
						market.QuoteToken.Decimals),
					OrderType = OrderTypes.Limit,
					OrderState = OrderStates.Active,
					OrderStringId = id,
					OriginalTransactionId = target,
				}, cancellationToken);
			}
		}
	}

	private ValueTask SendTrackedOrderAsync(TrackedOrder order, long target,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Market.ToStockSharp(),
			ServerTime = order.Receipt?.BlockTime ?? CurrentTime,
			PortfolioName = GetPortfolioName(),
			Side = order.Side,
			OrderVolume = order.Volume,
			Balance = order.Balance,
			OrderPrice = order.Price,
			OrderType = order.OrderType,
			OrderState = order.State,
			OrderStringId = GetOrderStringId(order),
			TransactionId = order.TransactionId,
			OriginalTransactionId = target,
			Commission = order.Receipt is null
				? null
				: order.Receipt.Fee / 1_000_000_000m,
			CommissionCurrency = "SOL",
		}, cancellationToken);

	private ValueTask SendTrackedTradeAsync(TrackedOrder order,
		ManifestTradeTrade trade, CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
			order.IsTradeSent = true;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = order.Market.ToStockSharp(),
			ServerTime = trade.Time,
			PortfolioName = GetPortfolioName(),
			Side = order.Side,
			OrderStringId = GetOrderStringId(order),
			TradeStringId = trade.Id,
			TradePrice = trade.Price,
			TradeVolume = trade.Volume,
			TransactionId = order.TransactionId,
			OriginalTransactionId = order.TransactionId,
		}, cancellationToken);
	}

	private static string GetOrderStringId(TrackedOrder order)
		=> order.Sequence is ulong sequence
			? GetOrderStringId(order.Market, sequence)
			: order.Signature;

	private static string GetOrderStringId(ManifestTradeMarket market,
		ulong sequence)
		=> $"{market.MarketAddress}:{sequence.ToString(
			CultureInfo.InvariantCulture)}";

	private TrackedOrder ResolveTrackedOrder(string stringId, long? numericId,
		string operation)
	{
		if (stringId.IsEmpty() && numericId is null)
			throw new InvalidOperationException(
				$"An order identifier is required for Manifest {operation}.");
		TrackedOrder order;
		using (_sync.EnterScope())
		{
			order = _trackedOrders.Values.FirstOrDefault(candidate =>
				(!stringId.IsEmpty() &&
					(candidate.Signature.Equals(stringId,
						StringComparison.Ordinal) ||
					 GetOrderStringId(candidate).Equals(stringId,
						StringComparison.Ordinal))) ||
				(numericId is long id && candidate.Sequence == (ulong)id));
		}
		return order ?? throw new InvalidOperationException(
			$"The Manifest order for {operation} was not found in this session.");
	}

	private ManifestTradeSeat GetWalletSeat(ManifestTradeMarket market)
		=> market.Seats.FirstOrDefault(seat => seat.Trader.Equals(
			RpcClient.WalletAddress, StringComparison.Ordinal));

	private static ManifestTradeOrder FindOrder(ManifestTradeMarket market,
		ulong sequence, string trader)
	{
		var seat = market.Seats.FirstOrDefault(item => item.Trader.Equals(
			trader, StringComparison.Ordinal));
		return seat is null ? null : market.Bids.Concat(market.Asks)
			.FirstOrDefault(order => order.Sequence == sequence &&
				order.TraderIndex == seat.Index);
	}

	private async ValueTask<ulong> GetComputeUnitPriceAsync(
		ManifestTradeMarket market, CancellationToken cancellationToken)
		=> ComputeUnitPrice == 0
			? await RpcClient.GetPriorityFeeAsync(
			[
				market.MarketAddress,
				market.BaseVault,
				market.QuoteVault,
			], cancellationToken)
			: checked((ulong)ComputeUnitPrice);

	private async ValueTask RefreshAllMarketsAsync(
		CancellationToken cancellationToken)
	{
		ManifestTradeMarket[] markets;
		using (_sync.EnterScope())
			markets = [.. _marketsByAddress.Values];
		foreach (var market in markets)
			await RefreshMarketAsync(market, cancellationToken);
	}

	private static void ValidateOrderRequest(OrderTypes orderType,
		OrderCondition condition, bool? isPostOnly, TimeInForce? timeInForce,
		string userOrderId)
	{
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				$"Manifest Trade does not support {orderType} orders.");
		if (condition is not null)
			throw new NotSupportedException(
				"Manifest Trade core orders do not support conditions.");
		if (!userOrderId.IsEmpty())
			throw new NotSupportedException(
				"Manifest Trade core orders do not store client-order IDs.");
		if (orderType == OrderTypes.Market && isPostOnly == true)
			throw new NotSupportedException(
				"A Manifest market order cannot be post-only.");
		if (orderType == OrderTypes.Market && timeInForce is not null)
			throw new NotSupportedException(
				"Time-in-force is implicit for Manifest market swaps.");
		if (orderType == OrderTypes.Limit && timeInForce is not null and
			not TimeInForce.PutInQueue and not TimeInForce.CancelBalance)
			throw new NotSupportedException(
				$"Manifest Trade does not support {timeInForce} time-in-force.");
	}

	private static bool Matches(OrderSubscription subscription,
		TrackedOrder order)
	{
		if (!subscription.OrderStringId.IsEmpty() &&
			!subscription.OrderStringId.Equals(GetOrderStringId(order),
				StringComparison.Ordinal) &&
			!subscription.OrderStringId.Equals(order.Signature,
				StringComparison.Ordinal))
			return false;
		if (!subscription.SecurityId.SecurityCode.IsEmpty() &&
			!subscription.SecurityId.SecurityCode.EqualsIgnoreCase(
				order.Market.SecurityCode))
			return false;
		if (subscription.Side is Sides side && order.Side != side)
			return false;
		return (subscription.From is null ||
				order.SubmittedTime >= subscription.From) &&
			(subscription.To is null || order.SubmittedTime <= subscription.To);
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

	private static ulong DecodeTokenAmount(ManifestTradeRpcAccount account,
		string expectedMint)
	{
		var data = ManifestTradeExtensions.DecodeAccountData(account);
		if (data.Length < 72)
			throw new InvalidDataException(
				"SPL token account data is truncated.");
		var mint = new PublicKey(data.AsSpan(0, 32)).Key;
		if (!mint.Equals(expectedMint, StringComparison.Ordinal))
			throw new InvalidDataException(
				$"SPL token account belongs to mint '{mint}', not " +
				$"'{expectedMint}'.");
		return BinaryPrimitives.ReadUInt64LittleEndian(
			data.AsSpan(64, sizeof(ulong)));
	}
}
