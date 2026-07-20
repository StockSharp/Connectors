namespace StockSharp.Meteora;

public partial class MeteoraMessageAdapter
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
				"Direct trading is unavailable for disabled pools or Token-2022 " +
				"mints with transfer extensions.");
		if (regMsg.Condition is not null)
			throw new NotSupportedException(
				"Meteora DLMM does not expose conditional orders.");
		if (!regMsg.UserOrderId.IsEmpty())
			throw new NotSupportedException(
				"Meteora order identities are on-chain addresses or signatures.");
		var orderType = regMsg.OrderType ??
			(regMsg.Price > 0 ? OrderTypes.Limit : OrderTypes.Market);
		if (orderType is not (OrderTypes.Market or OrderTypes.Limit))
			throw new NotSupportedException(
				$"Meteora does not support '{orderType}' orders.");
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException(
				"Meteora order volume must be positive.");
		var baseUnits = volume.ToBaseUnits(market.TokenX.Decimals);
		if (baseUnits <= 0)
			throw new InvalidOperationException(
				"Meteora order volume rounds to zero base units.");
		await RefreshMarketAsync(market, cancellationToken);
		var priorityFee = await GetPriorityFeeAsync(market, cancellationToken);
		if (orderType == OrderTypes.Market)
		{
			if (regMsg.PostOnly == true)
				throw new NotSupportedException(
					"Post-only is not applicable to an immediate DLMM swap.");
			if (regMsg.TimeInForce is TimeInForce.PutInQueue or
				TimeInForce.MatchOrCancel)
				throw new NotSupportedException(
					"Meteora swaps execute immediately and do not rest in a queue.");
			var quote = market.GetQuote(regMsg.Side, baseUnits,
				SlippageTolerance);
			var plan = MeteoraInstructionBuilder.BuildSwap(market, quote,
				RpcClient.WalletAddress, checked((uint)ComputeUnitLimit),
				priorityFee);
			var signature = await RpcClient.SendTransactionAsync(
				plan.Instructions, cancellationToken, null);
			var quoteAmount = quote.QuoteAmount.FromBaseUnits(
				market.TokenY.Decimals);
			await TrackOrderAsync(new()
			{
				TransactionId = regMsg.TransactionId,
				PlacementSignature = signature,
				OrderAddress = signature,
				Market = market,
				Side = regMsg.Side,
				OrderType = OrderTypes.Market,
				Volume = volume,
				Balance = volume,
				Price = quoteAmount / volume,
				SubmittedTime = CurrentTime,
				State = OrderStates.Active,
			}, regMsg.TransactionId, cancellationToken);
			return;
		}

		if (!market.IsLimitOrderPool)
			throw new NotSupportedException(
				"The selected Meteora pool does not support native limit orders.");
		if (regMsg.Price <= 0)
			throw new InvalidOperationException(
				"A positive Meteora limit price is required.");
		if (regMsg.TimeInForce is not (null or TimeInForce.PutInQueue))
			throw new NotSupportedException(
				"Meteora native limit orders are good-till-cancelled.");
		var binId = market.PriceToBinId(regMsg.Price, regMsg.Side);
		var arrayAddress = MeteoraExtensions.BinArrayAddress(market.PoolAddress,
			MeteoraExtensions.GetBinArrayIndex(binId));
		var isInitialized = await RpcClient.GetAccountAsync(arrayAddress,
			cancellationToken) is not null;
		var limitPlan = MeteoraInstructionBuilder.BuildPlaceLimitOrder(market,
			regMsg.Side, baseUnits, regMsg.Price, RpcClient.WalletAddress,
			isInitialized, checked((uint)ComputeUnitLimit), priorityFee);
		var placementSignature = await RpcClient.SendTransactionAsync(
			limitPlan.Instructions, cancellationToken, limitPlan.AdditionalSigner);
		var actualPrice = MeteoraExtensions.ToHumanPrice(
			MeteoraExtensions.GetRawPrice(market.BinStep,
				limitPlan.BinId.Value), market.TokenX.Decimals,
			market.TokenY.Decimals);
		await TrackOrderAsync(new()
		{
			TransactionId = regMsg.TransactionId,
			PlacementSignature = placementSignature,
			OrderAddress = limitPlan.OrderAddress,
			Market = market,
			Side = regMsg.Side,
			OrderType = OrderTypes.Limit,
			Volume = volume,
			Balance = volume,
			Price = actualPrice,
			SubmittedTime = CurrentTime,
			State = OrderStates.Active,
			BinIds = [limitPlan.BinId.Value],
		}, regMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(replaceMsg.PortfolioName);
		var original = ResolveTrackedOrder(replaceMsg.OldOrderStringId,
			replaceMsg.OriginalTransactionId);
		if (original.OrderType != OrderTypes.Limit ||
			original.State != OrderStates.Active)
			throw new InvalidOperationException(
				"Only an active Meteora native limit order can be replaced.");
		if (replaceMsg.Price <= 0 || replaceMsg.Volume <= 0)
			throw new InvalidOperationException(
				"Replacement price and volume must be positive.");
		var market = GetMarket(replaceMsg.SecurityId);
		if (!ReferenceEquals(market, original.Market))
			throw new InvalidOperationException(
				"A Meteora replacement must remain in the original pool.");
		await RefreshMarketAsync(market, cancellationToken);
		var priorityFee = await GetPriorityFeeAsync(market, cancellationToken);
		var baseUnits = replaceMsg.Volume.Abs().ToBaseUnits(
			market.TokenX.Decimals);
		var newBinId = market.PriceToBinId(replaceMsg.Price, replaceMsg.Side);
		var newArrayAddress = MeteoraExtensions.BinArrayAddress(
			market.PoolAddress, MeteoraExtensions.GetBinArrayIndex(newBinId));
		var isInitialized = await RpcClient.GetAccountAsync(newArrayAddress,
			cancellationToken) is not null;
		var cancelPlan = MeteoraInstructionBuilder.BuildCancelLimitOrder(market,
			original.OrderAddress, original.BinIds, RpcClient.WalletAddress,
			checked((uint)ComputeUnitLimit), priorityFee);
		var placePlan = MeteoraInstructionBuilder.BuildPlaceLimitOrder(market,
			replaceMsg.Side, baseUnits, replaceMsg.Price,
			RpcClient.WalletAddress, isInitialized,
			checked((uint)ComputeUnitLimit), priorityFee);
		var instructions = cancelPlan.Instructions.Concat(
			placePlan.Instructions.Skip(2)).ToArray();
		var signature = await RpcClient.SendTransactionAsync(instructions,
			cancellationToken, placePlan.AdditionalSigner);
		using (_sync.EnterScope())
			original.CancelSignature = signature;
		var actualPrice = MeteoraExtensions.ToHumanPrice(
			MeteoraExtensions.GetRawPrice(market.BinStep,
				placePlan.BinId.Value), market.TokenX.Decimals,
			market.TokenY.Decimals);
		await TrackOrderAsync(new()
		{
			TransactionId = replaceMsg.TransactionId,
			PlacementSignature = signature,
			OrderAddress = placePlan.OrderAddress,
			Market = market,
			Side = replaceMsg.Side,
			OrderType = OrderTypes.Limit,
			Volume = replaceMsg.Volume.Abs(),
			Balance = replaceMsg.Volume.Abs(),
			Price = actualPrice,
			SubmittedTime = CurrentTime,
			State = OrderStates.Active,
			BinIds = [placePlan.BinId.Value],
		}, replaceMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		var order = ResolveTrackedOrder(cancelMsg.OrderStringId,
			cancelMsg.OriginalTransactionId);
		await CancelTrackedOrderAsync(order, cancelMsg.TransactionId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		TrackedOrder[] orders;
		using (_sync.EnterScope())
			orders = [.. _trackedOrders.Values.Where(order =>
				order.OrderType == OrderTypes.Limit &&
				order.State == OrderStates.Active &&
				(cancelMsg.SecurityId.SecurityCode.IsEmpty() ||
					order.Market.SecurityCode.EqualsIgnoreCase(
						cancelMsg.SecurityId.SecurityCode)) &&
				(cancelMsg.Side is null || order.Side == cancelMsg.Side))];
		foreach (var order in orders)
			await CancelTrackedOrderAsync(order, cancelMsg.TransactionId,
				cancellationToken);
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
			BoardCode = BoardCodes.Meteora,
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
				"Meteora orders use Solana addresses, not numeric identifiers.");
		if (!statusMsg.UserId.IsEmpty())
			throw new NotSupportedException(
				"Meteora has no exchange-side user identifier.");
		if (statusMsg.SecurityIds.Length > 0)
			throw new NotSupportedException(
				"Use the primary security filter for Meteora order status.");
		await RefreshIndexedLimitOrdersAsync(cancellationToken);
		var subscription = new OrderSubscription
		{
			OrderAddress = statusMsg.OrderStringId.IsEmpty()
				? null
				: statusMsg.OrderStringId.Trim(),
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

	private async ValueTask CancelTrackedOrderAsync(TrackedOrder order,
		long target, CancellationToken cancellationToken)
	{
		if (order.OrderType != OrderTypes.Limit ||
			order.State != OrderStates.Active)
			throw new InvalidOperationException(
				"Only an active Meteora native limit order can be canceled.");
		if (order.BinIds.Length == 0)
			throw new InvalidOperationException(
				"The limit order has no indexed bin distribution.");
		await RefreshMarketAsync(order.Market, cancellationToken);
		var priorityFee = await GetPriorityFeeAsync(order.Market,
			cancellationToken);
		var plan = MeteoraInstructionBuilder.BuildCancelLimitOrder(order.Market,
			order.OrderAddress, order.BinIds, RpcClient.WalletAddress,
			checked((uint)ComputeUnitLimit), priorityFee);
		var signature = await RpcClient.SendTransactionAsync(plan.Instructions,
			cancellationToken, null);
		using (_sync.EnterScope())
			order.CancelSignature = signature;
		await SendOrderAsync(order, target, cancellationToken);
	}

	private async ValueTask PollPrivateAsync(
		CancellationToken cancellationToken)
	{
		long[] portfolioTargets;
		KeyValuePair<long, OrderSubscription>[] orderTargets;
		TrackedOrder[] active;
		using (_sync.EnterScope())
		{
			portfolioTargets = [.. _portfolioSubscriptions];
			orderTargets = [.. _orderSubscriptions];
			active = [.. _trackedOrders.Values.Where(static order =>
				order.State == OrderStates.Active)];
		}
		if (portfolioTargets.Length > 0)
		{
			var balances = await LoadBalancesAsync(cancellationToken);
			foreach (var target in portfolioTargets)
				await SendPortfolioSnapshotAsync(target, false, balances,
					cancellationToken);
		}
		foreach (var order in active)
			await RefreshReceiptAsync(order, cancellationToken);
		await RefreshIndexedLimitOrdersAsync(cancellationToken);
		foreach (var target in orderTargets)
			await SendOrderSnapshotAsync(target.Value, target.Key, false,
				cancellationToken);
	}

	private async ValueTask RefreshReceiptAsync(TrackedOrder order,
		CancellationToken cancellationToken)
	{
		if (order.Receipt is null)
		{
			var receipt = await RpcClient.GetReceiptAsync(order.PlacementSignature,
				cancellationToken);
			if (receipt is not null)
			{
				order.Receipt = receipt;
				if (!receipt.IsSuccessful)
				{
					order.State = OrderStates.Failed;
					order.Balance = 0m;
					await SendOrderAsync(order, order.TransactionId,
						cancellationToken);
					return;
				}
				if (order.OrderType == OrderTypes.Market)
				{
					var execution = ReadSwapExecution(order, receipt);
					order.Price = execution.Price;
					order.Volume = execution.Volume;
					order.ExecutedVolume = execution.Volume;
					order.Balance = 0m;
					order.State = OrderStates.Done;
					await SendOrderAsync(order, order.TransactionId,
						cancellationToken);
					await SendLimitFillAsync(order, order.TransactionId,
						cancellationToken);
				}
			}
		}
		if (!order.CancelSignature.IsEmpty())
		{
			var cancellation = await RpcClient.GetReceiptAsync(
				order.CancelSignature, cancellationToken);
			if (cancellation is { IsSuccessful: false })
			{
				using (_sync.EnterScope())
					order.CancelSignature = null;
				await SendOutErrorAsync(new InvalidOperationException(
					$"Meteora cancellation '{cancellation.Signature}' failed."),
					cancellationToken);
			}
		}
	}

	private async ValueTask RefreshIndexedLimitOrdersAsync(
		CancellationToken cancellationToken)
	{
		if (_apiClient is null || !RpcClient.IsWalletAvailable)
		{
			await RefreshOnChainLimitOrdersAsync(cancellationToken);
			return;
		}
		MeteoraMarket[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values.Where(static market =>
				market.IsLimitOrderPool)];
		foreach (var market in markets)
		{
			try
			{
				var open = await _apiClient.GetOpenOrdersAsync(
					RpcClient.WalletAddress, market.PoolAddress, 1000,
					cancellationToken);
				foreach (var item in open?.Data ?? [])
					if (item is not null)
						await ApplyOpenLimitOrderAsync(market, item,
							cancellationToken);
				var closed = await _apiClient.GetClosedOrdersAsync(
					RpcClient.WalletAddress, market.PoolAddress, 1000,
					cancellationToken);
				foreach (var item in closed?.Data ?? [])
					if (item is not null)
						await ApplyClosedLimitOrderAsync(market, item,
							cancellationToken);
			}
			catch (Exception error) when (
				!cancellationToken.IsCancellationRequested)
			{
				this.AddDebugLog("Meteora limit-order index refresh failed for " +
					"{0}: {1}", market.PoolAddress, error.Message);
			}
		}
	}

	private async ValueTask ApplyOpenLimitOrderAsync(MeteoraMarket market,
		MeteoraApiOpenOrder item, CancellationToken cancellationToken)
	{
		var side = item.IsAskSide ? Sides.Sell : Sides.Buy;
		var input = ParseAmount(item.InputAmount);
		var expectedOutput = ParseAmount(item.ExpectedOutputAmount);
		var filledInput = ParseAmount(item.FilledInputAmount);
		var filledOutput = ParseAmount(item.FilledOutputAmount);
		var volume = item.IsAskSide ? input : expectedOutput;
		var executed = item.IsAskSide ? filledInput : filledOutput;
		var price = item.IsAskSide
			? SafePrice(filledOutput, filledInput,
				SafePrice(expectedOutput, input, market.CurrentPrice))
			: SafePrice(filledInput, filledOutput,
				SafePrice(input, expectedOutput, market.CurrentPrice));
		var order = GetOrCreateIndexedOrder(item.Address, market, side, volume,
			price, item.OpenedAtSignature, item.OpenedAt,
			[.. item.Bins.Select(static bin => bin.BinId)]);
		order.ExecutedVolume = executed.Min(order.Volume).Max(0m);
		order.Balance = (order.Volume - order.ExecutedVolume).Max(0m);
		order.State = OrderStates.Active;
		if (price > 0)
			order.Price = price;
		await SendLimitFillAsync(order, order.TransactionId, cancellationToken);
	}

	private async ValueTask ApplyClosedLimitOrderAsync(MeteoraMarket market,
		MeteoraApiClosedOrder item, CancellationToken cancellationToken)
	{
		var side = item.IsAskSide ? Sides.Sell : Sides.Buy;
		var filledInput = ParseAmount(item.FilledInputAmount);
		var receivedOutput = ParseAmount(item.ReceivedOutputAmount);
		var expectedOutput = ParseAmount(item.ExpectedOutputAmount);
		var depositedX = ParseAmount(item.TotalDepositX);
		var executed = item.IsAskSide ? filledInput : receivedOutput;
		var volume = item.IsAskSide ? depositedX : expectedOutput;
		if (volume <= 0)
			volume = executed;
		var price = item.IsAskSide
			? SafePrice(receivedOutput, filledInput, market.CurrentPrice)
			: SafePrice(filledInput, receivedOutput, market.CurrentPrice);
		var order = GetOrCreateIndexedOrder(item.Address, market, side,
			volume, price, item.OpenedAtSignature, item.OpenedAt, []);
		order.ExecutedVolume = executed.Min(order.Volume).Max(0m);
		order.Balance = (order.Volume - order.ExecutedVolume).Max(0m);
		order.State = OrderStates.Done;
		if (price > 0)
			order.Price = price;
		await SendOrderAsync(order, order.TransactionId, cancellationToken);
		await SendLimitFillAsync(order, order.TransactionId, cancellationToken);
	}

	private async ValueTask RefreshOnChainLimitOrdersAsync(
		CancellationToken cancellationToken)
	{
		TrackedOrder[] orders;
		using (_sync.EnterScope())
			orders = [.. _trackedOrders.Values.Where(static order =>
				order.OrderType == OrderTypes.Limit &&
				order.State == OrderStates.Active && order.Receipt is not null)];
		foreach (var order in orders)
		{
			if (await RpcClient.GetAccountAsync(order.OrderAddress,
				cancellationToken) is not null)
				continue;
			order.State = OrderStates.Done;
			order.Balance = 0m;
			await SendOrderAsync(order, order.TransactionId, cancellationToken);
		}
	}

	private TrackedOrder GetOrCreateIndexedOrder(string address,
		MeteoraMarket market, Sides side, decimal volume, decimal price,
		string signature, long openedAt, int[] bins)
	{
		address = address.NormalizePublicKey();
		using (_sync.EnterScope())
		{
			if (_trackedOrders.TryGetValue(address, out var existing))
			{
				if (existing.BinIds.Length == 0 && bins.Length > 0)
					existing.BinIds = bins;
				return existing;
			}
			var order = new TrackedOrder
			{
				OrderAddress = address,
				PlacementSignature = signature,
				Market = market,
				Side = side,
				OrderType = OrderTypes.Limit,
				Volume = volume,
				Balance = volume,
				Price = price,
				SubmittedTime = openedAt > 0
					? openedAt.FromUnix()
					: DateTime.UtcNow,
				State = OrderStates.Active,
				BinIds = bins,
			};
			_trackedOrders[address] = order;
			return order;
		}
	}

	private async ValueTask<
		(string Code, string Identity, int Decimals, BigInteger Amount)[]>
		LoadBalancesAsync(CancellationToken cancellationToken)
	{
		MeteoraToken[] tokens;
		using (_sync.EnterScope())
			tokens = [.. _tokens.Values.GroupBy(static token => token.Mint,
				StringComparer.Ordinal).Select(static group => group.First())];
		var result = new List<
			(string Code, string Identity, int Decimals, BigInteger Amount)>
		{
			("SOL", MeteoraExtensions.SystemProgramAddress, 9,
				await RpcClient.GetBalanceAsync(cancellationToken)),
		};
		for (var offset = 0; offset < tokens.Length; offset += 100)
		{
			var chunk = tokens.Skip(offset).Take(100).ToArray();
			var addresses = chunk.Select(token =>
				MeteoraExtensions.AssociatedTokenAddress(RpcClient.WalletAddress,
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
					BoardCode = BoardCodes.Meteora,
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
		TrackedOrder[] orders;
		using (_sync.EnterScope())
			orders = [.. _trackedOrders.Values.Where(order =>
				Matches(subscription, order)).OrderBy(static order =>
					order.SubmittedTime)];
		var skipped = 0;
		var delivered = 0;
		foreach (var order in orders)
		{
			if (subscription.States is { Length: > 0 } states &&
				!states.Contains(order.State))
				continue;
			if (skipped++ < subscription.Skip)
				continue;
			if (delivered++ >= subscription.Maximum)
				break;
			var key = $"{target}:{order.OrderAddress}";
			var fingerprint = new OrderFingerprint(order.State, order.Balance,
				order.ExecutedVolume);
			var isChanged = false;
			using (_sync.EnterScope())
			{
				isChanged = isForced ||
					!_orderFingerprints.TryGetValue(key, out var previous) ||
					previous != fingerprint;
				_orderFingerprints[key] = fingerprint;
			}
			if (isChanged)
				await SendOrderAsync(order, target, cancellationToken);
			await SendLimitFillAsync(order, target, cancellationToken);
		}
	}

	private ValueTask SendOrderAsync(TrackedOrder order, long target,
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
			OrderStringId = order.OrderAddress,
			TransactionId = order.TransactionId,
			OriginalTransactionId = target,
			Commission = GetCommission(order.Receipt),
			CommissionCurrency = "SOL",
		}, cancellationToken);

	private async ValueTask SendLimitFillAsync(TrackedOrder order, long target,
		CancellationToken cancellationToken)
	{
		decimal delta;
		using (_sync.EnterScope())
		{
			delta = order.ExecutedVolume - order.ReportedExecutedVolume;
			if (delta <= 0)
				return;
			order.ReportedExecutedVolume = order.ExecutedVolume;
		}
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = order.Market.ToStockSharp(),
			ServerTime = order.Receipt?.BlockTime ?? CurrentTime,
			PortfolioName = GetPortfolioName(),
			Side = order.Side,
			OrderStringId = order.OrderAddress,
			TradeStringId = order.OrderAddress + ":" +
				order.ExecutedVolume.ToString(CultureInfo.InvariantCulture),
			TradePrice = order.Price,
			TradeVolume = delta,
			TransactionId = order.TransactionId,
			OriginalTransactionId = target,
			Commission = GetCommission(order.Receipt),
			CommissionCurrency = "SOL",
		}, cancellationToken);
	}

	private static decimal? GetCommission(MeteoraTransactionReceipt receipt)
		=> receipt is null ? null : receipt.Fee / 1_000_000_000m;

	private static SwapExecution ReadSwapExecution(TrackedOrder order,
		MeteoraTransactionReceipt receipt)
	{
		var transaction = new MeteoraRpcTransaction
		{
			BlockTime = receipt.BlockTime is DateTime time
				? checked((long)(time.ToUniversalTime() -
					DateTime.UnixEpoch).TotalSeconds)
				: null,
			Meta = new()
			{
				LogMessages = receipt.LogMessages,
				InnerInstructions = receipt.InnerInstructions,
			},
		};
		var events = MeteoraExtensions.DecodeEvents(order.PlacementSignature,
			transaction, receipt.BlockTime ?? DateTime.UtcNow).Where(item =>
				item.PoolAddress.Equals(order.Market.PoolAddress,
					StringComparison.Ordinal) &&
				item.IsSwapForY == (order.Side == Sides.Sell)).ToArray();
		if (events.Length == 0)
			throw new InvalidDataException(
				$"Successful Meteora transaction '{order.PlacementSignature}' " +
				"contains no matching swap event.");
		BigInteger baseAmount = 0;
		BigInteger quoteAmount = 0;
		foreach (var item in events)
		{
			var consumedInput = new BigInteger(item.InputAmount) -
				item.InputAmountLeft;
			if (item.IsSwapForY)
			{
				baseAmount += consumedInput;
				quoteAmount += item.OutputAmount;
			}
			else
			{
				baseAmount += item.OutputAmount;
				quoteAmount += consumedInput;
			}
		}
		var volume = baseAmount.FromBaseUnits(order.Market.TokenX.Decimals);
		var quote = quoteAmount.FromBaseUnits(order.Market.TokenY.Decimals);
		if (volume <= 0 || quote <= 0)
			throw new InvalidDataException(
				$"Meteora transaction '{order.PlacementSignature}' contains " +
				"non-positive execution amounts.");
		return new(quote / volume, volume);
	}

	private async ValueTask TrackOrderAsync(TrackedOrder order, long target,
		CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
			_trackedOrders[order.OrderAddress] = order;
		await SendOrderAsync(order, target, cancellationToken);
	}

	private TrackedOrder ResolveTrackedOrder(string address,
		long originalTransactionId)
	{
		using (_sync.EnterScope())
		{
			if (!address.IsEmpty() && _trackedOrders.TryGetValue(
				address.Trim(), out var byAddress))
				return byAddress;
			var byTransaction = _trackedOrders.Values.FirstOrDefault(order =>
				order.TransactionId == originalTransactionId);
			return byTransaction ?? throw new InvalidOperationException(
				$"Unknown Meteora order '{address ??
					originalTransactionId.ToString(CultureInfo.InvariantCulture)}'.");
		}
	}

	private async ValueTask<ulong> GetPriorityFeeAsync(MeteoraMarket market,
		CancellationToken cancellationToken)
		=> ComputeUnitPrice == 0
			? await RpcClient.GetPriorityFeeAsync(
			[
				market.PoolAddress,
				market.TokenVaultX,
				market.TokenVaultY,
			], cancellationToken)
			: checked((ulong)ComputeUnitPrice);

	private static bool Matches(OrderSubscription subscription,
		TrackedOrder order)
	{
		if (!subscription.OrderAddress.IsEmpty() &&
			!subscription.OrderAddress.Equals(order.OrderAddress,
				StringComparison.Ordinal))
			return false;
		if (!subscription.SecurityId.SecurityCode.IsEmpty() &&
			!subscription.SecurityId.SecurityCode.EqualsIgnoreCase(
				order.Market.SecurityCode))
			return false;
		if (subscription.Side is Sides side && order.Side != side)
			return false;
		if (subscription.Volume is decimal volume && order.Volume != volume)
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

	private static ulong DecodeTokenAmount(MeteoraRpcAccount account,
		string expectedMint)
	{
		var data = MeteoraExtensions.DecodeAccountData(account);
		if (data.Length < 72)
			throw new InvalidDataException("SPL token account data is truncated.");
		var mint = new PublicKey(data.AsSpan(0, 32)).Key;
		if (!mint.Equals(expectedMint, StringComparison.Ordinal))
			throw new InvalidDataException(
				$"SPL token account belongs to mint '{mint}', not '{expectedMint}'.");
		return BinaryPrimitives.ReadUInt64LittleEndian(
			data.AsSpan(64, sizeof(ulong)));
	}

	private static decimal ParseAmount(string value)
		=> decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result) && result >= 0
			? result
			: 0m;

	private static decimal SafePrice(decimal numerator, decimal denominator,
		decimal fallback)
		=> numerator > 0 && denominator > 0 ? numerator / denominator : fallback;
}
