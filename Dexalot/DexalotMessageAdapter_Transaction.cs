namespace StockSharp.Dexalot;

public partial class DexalotMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(regMsg.PortfolioName);
		var pair = GetPair(regMsg.SecurityId);
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(
					orderType, regMsg.TransactionId));
		if (regMsg.Volume <= 0)
			throw new ArgumentOutOfRangeException(
				nameof(regMsg.Volume), regMsg.Volume,
				"Dexalot order volume must be positive.");
		if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
			throw new ArgumentOutOfRangeException(
				nameof(regMsg.Price), regMsg.Price,
				"Dexalot limit-order price must be positive.");
		if (regMsg.PostOnly == true && orderType != OrderTypes.Limit)
			throw new NotSupportedException(
				"Dexalot post-only execution applies only to limit orders.");
		if (regMsg.VisibleVolume is > 0 &&
			regMsg.VisibleVolume != regMsg.Volume)
			throw new NotSupportedException(
				"Dexalot does not expose iceberg orders.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException(
				"Dexalot does not expose good-till-date orders.");
		_ = regMsg.Volume.ToBaseUnits(pair.BaseDecimals);
		if (orderType == OrderTypes.Limit)
			_ = regMsg.Price.ToBaseUnits(pair.QuoteDecimals);
		await ValidateAvailableBalanceAsync(pair, regMsg.Side, orderType,
			regMsg.Price, regMsg.Volume, cancellationToken);
		var hash = await EvmClient.SendOrderAsync(_tradePairsAddress, pair,
			regMsg.TransactionId, regMsg.Side, orderType,
			regMsg.ToDexalotType2(), (int)SelfTradePrevention,
			regMsg.Price, regMsg.Volume, cancellationToken);
		var receipt = await EvmClient.WaitForReceiptAsync(hash,
			ReceiptTimeout, cancellationToken);
		EnsureSuccessfulReceipt(receipt, hash);
		var orderEvent = EvmClient.ParseOrderEvent(receipt, pair,
			_tradePairsAddress) ?? throw new InvalidDataException(
				$"Dexalot transaction '{hash}' emitted no order status.");
		var time = await EvmClient.GetBlockTimeAsync(receipt.BlockNumber,
			cancellationToken);
		var tracked = new TrackedOrder
		{
			TransactionId = regMsg.TransactionId,
			TransactionHash = hash,
			OrderId = orderEvent.OrderId,
			ClientOrderId = orderEvent.ClientOrderId,
			Pair = pair,
			Side = regMsg.Side,
			OrderType = orderType,
			TimeInForce = regMsg.PostOnly == true
				? TimeInForce.PutInQueue
				: regMsg.TimeInForce,
			Price = orderEvent.Price,
			Volume = orderEvent.Quantity,
			FilledVolume = orderEvent.FilledVolume,
			Commission = GetGasCommission(receipt),
			CommissionCurrency = "ALOT",
			Time = time,
			UpdateTime = time,
			State = ToOrderState(orderEvent.Status),
		};
		using (_sync.EnterScope())
			_trackedOrders[tracked.OrderId] = tracked;
		await SendTrackedOrderAsync(tracked, regMsg.TransactionId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		var orderId = ResolveOrderId(cancelMsg.OrderStringId,
			cancelMsg.OriginalTransactionId);
		TrackedOrder tracked;
		using (_sync.EnterScope())
			_trackedOrders.TryGetValue(orderId, out tracked);
		var pair = tracked?.Pair ?? GetPair(cancelMsg.SecurityId);
		await CancelOrderCoreAsync(orderId, pair, cancelMsg.TransactionId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(replaceMsg.PortfolioName);
		if (replaceMsg.Price <= 0 || replaceMsg.Volume <= 0)
			throw new ArgumentOutOfRangeException(
				nameof(replaceMsg.Volume),
				"Dexalot replacement price and volume must be positive.");
		var pair = GetPair(replaceMsg.SecurityId);
		var orderId = ResolveOrderId(replaceMsg.OldOrderStringId,
			replaceMsg.OriginalTransactionId);
		var hash = await EvmClient.ReplaceOrderAsync(_tradePairsAddress,
			pair, replaceMsg.TransactionId, orderId, replaceMsg.Price,
			replaceMsg.Volume, cancellationToken);
		var receipt = await EvmClient.WaitForReceiptAsync(hash,
			ReceiptTimeout, cancellationToken);
		EnsureSuccessfulReceipt(receipt, hash);
		var orderEvent = EvmClient.ParseOrderEvent(receipt, pair,
			_tradePairsAddress) ?? throw new InvalidDataException(
				$"Dexalot replacement transaction '{hash}' emitted no new " +
					"order status.");
		var time = await EvmClient.GetBlockTimeAsync(receipt.BlockNumber,
			cancellationToken);
		TrackedOrder old;
		using (_sync.EnterScope())
		{
			_trackedOrders.TryGetValue(orderId, out old);
			if (old is not null)
			{
				old.State = OrderStates.Done;
				old.UpdateTime = time;
			}
		}
		if (old is not null)
			await SendTrackedOrderAsync(old, replaceMsg.TransactionId,
				cancellationToken);
		var tracked = new TrackedOrder
		{
			TransactionId = replaceMsg.TransactionId,
			TransactionHash = hash,
			OrderId = orderEvent.OrderId,
			ClientOrderId = orderEvent.ClientOrderId,
			Pair = pair,
			Side = orderEvent.Side,
			OrderType = orderEvent.OrderType,
			TimeInForce = ToTimeInForce(orderEvent.Type2),
			Price = orderEvent.Price,
			Volume = orderEvent.Quantity,
			FilledVolume = orderEvent.FilledVolume,
			Commission = GetGasCommission(receipt),
			CommissionCurrency = "ALOT",
			Time = time,
			UpdateTime = time,
			State = ToOrderState(orderEvent.Status),
		};
		using (_sync.EnterScope())
			_trackedOrders[tracked.OrderId] = tracked;
		await SendTrackedOrderAsync(tracked, replaceMsg.TransactionId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"Dexalot spot cancellation cannot close positions.");
		TrackedOrder[] orders;
		using (_sync.EnterScope())
			orders = [.. _trackedOrders.Values.Where(order =>
				order.State == OrderStates.Active &&
				(cancelMsg.SecurityId == default ||
					cancelMsg.SecurityId.SecurityCode.IsEmpty() ||
					order.Pair.Pair.EqualsIgnoreCase(
						cancelMsg.SecurityId.SecurityCode)) &&
				(cancelMsg.Side is null ||
					order.Side == cancelMsg.Side))];

		foreach (var order in orders)
			await CancelOrderCoreAsync(order.OrderId, order.Pair,
				cancelMsg.TransactionId, cancellationToken);
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
				_portfolioSubscriptions.Remove(
					lookupMsg.OriginalTransactionId);
			return;
		}
		ValidatePortfolio(lookupMsg.PortfolioName);
		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = GetPortfolioName(),
			BoardCode = BoardCodes.Dexalot,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId,
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
			using (_sync.EnterScope())
				_orderSubscriptions.Remove(
					statusMsg.OriginalTransactionId);
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
				"Dexalot uses bytes32 string order identifiers.");
		if (!statusMsg.UserId.IsEmpty())
			throw new NotSupportedException(
				"Dexalot does not expose an exchange-side numeric user id.");
		var subscription = new OrderSubscription
		{
			OrderId = statusMsg.OrderStringId.IsEmpty()
				? null
				: NormalizeOrderId(statusMsg.OrderStringId),
			SecurityId = statusMsg.SecurityId,
			Side = statusMsg.Side,
			States = statusMsg.States,
			From = statusMsg.From?.ToUniversalTime(),
			To = statusMsg.To?.ToUniversalTime(),
			Skip = Math.Max(0, statusMsg.Skip ?? 0)
				.Min(int.MaxValue).To<int>(),
			Maximum = (statusMsg.Count ?? 1000)
				.Min(1000).Max(1).To<int>(),
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

	private async ValueTask CancelOrderCoreAsync(string orderId,
		DexalotPair pair, long target,
		CancellationToken cancellationToken)
	{
		var hash = await EvmClient.CancelOrderAsync(_tradePairsAddress,
			orderId, cancellationToken);
		var receipt = await EvmClient.WaitForReceiptAsync(hash,
			ReceiptTimeout, cancellationToken);
		EnsureSuccessfulReceipt(receipt, hash);
		var time = await EvmClient.GetBlockTimeAsync(receipt.BlockNumber,
			cancellationToken);
		TrackedOrder tracked;
		using (_sync.EnterScope())
		{
			_trackedOrders.TryGetValue(orderId, out tracked);
			if (tracked is not null)
			{
				tracked.TransactionHash = hash;
				tracked.State = OrderStates.Done;
				tracked.UpdateTime = time;
				tracked.Commission = GetGasCommission(receipt);
				tracked.CommissionCurrency = "ALOT";
			}
		}
		if (tracked is not null)
			await SendTrackedOrderAsync(tracked, target,
				cancellationToken);
		else
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = pair.ToStockSharp(),
				ServerTime = time,
				PortfolioName = GetPortfolioName(),
				OrderStringId = orderId,
				OriginalTransactionId = target,
				OrderState = OrderStates.Done,
				Commission = GetGasCommission(receipt),
				CommissionCurrency = "ALOT",
			}, cancellationToken);
	}

	private async ValueTask ValidateAvailableBalanceAsync(
		DexalotPair pair, Sides side, OrderTypes orderType, decimal price,
		decimal volume, CancellationToken cancellationToken)
	{
		var symbol = side == Sides.Sell ? pair.Base : pair.Quote;
		var decimals = side == Sides.Sell
			? pair.BaseDecimals
			: pair.QuoteDecimals;
		var required = side == Sides.Sell
			? volume.ToBaseUnits(decimals)
			: orderType == OrderTypes.Limit
				? (price * volume).ToBaseUnits(decimals)
				: BigInteger.Zero;
		if (required == 0)
			return;
		var balance = await EvmClient.GetBalanceAsync(_portfolioAddress,
			EvmClient.WalletAddress, symbol, cancellationToken);
		if (balance.Available < required)
			throw new InvalidOperationException(
				$"Insufficient available {symbol} in the Dexalot portfolio: " +
					$"{balance.Available.FromBaseUnits(decimals)} available, " +
					$"{required.FromBaseUnits(decimals)} required.");
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
			var balances = await LoadBalancesAsync(cancellationToken);

			foreach (var target in portfolioTargets)
				await SendPortfolioSnapshotAsync(target, balances,
					cancellationToken);
		}

		foreach (var target in orderTargets)
			await SendOrderSnapshotAsync(target.Value, target.Key, false,
				cancellationToken);

		if (_trackedOrders.Count > 0 && RestClient.CanReadPrivateData)
			await SendRecentFillsAsync(orderTargets, cancellationToken);
	}

	private async ValueTask<(string Symbol, int Decimals,
		BigInteger Total, BigInteger Available)[]> LoadBalancesAsync(
		CancellationToken cancellationToken)
	{
		DexalotPair[] pairs;
		using (_sync.EnterScope())
			pairs = [.. _pairs.Values];
		var assets = pairs
			.SelectMany(static pair => new[]
			{
				(Symbol: pair.Base, Decimals: pair.BaseDecimals),
				(Symbol: pair.Quote, Decimals: pair.QuoteDecimals),
			})
			.GroupBy(static item => item.Symbol,
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.First())
			.OrderBy(static item => item.Symbol,
				StringComparer.OrdinalIgnoreCase)
			.ToArray();
		var result = new List<(string, int, BigInteger, BigInteger)>();

		foreach (var asset in assets)
		{
			var balance = await EvmClient.GetBalanceAsync(
				_portfolioAddress, EvmClient.WalletAddress, asset.Symbol,
				cancellationToken);
			result.Add((asset.Symbol, asset.Decimals, balance.Total,
				balance.Available));
		}

		return [.. result];
	}

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		CancellationToken cancellationToken)
		=> await SendPortfolioSnapshotAsync(target,
			await LoadBalancesAsync(cancellationToken), cancellationToken);

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		(string Symbol, int Decimals, BigInteger Total,
			BigInteger Available)[] balances,
		CancellationToken cancellationToken)
	{
		foreach (var balance in balances)
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = GetPortfolioName(),
				SecurityId = new()
				{
					SecurityCode = balance.Symbol,
					BoardCode = BoardCodes.Dexalot,
				},
				ServerTime = CurrentTime,
				OriginalTransactionId = target,
			}
			.TryAdd(PositionChangeTypes.CurrentValue,
				balance.Total.FromBaseUnits(balance.Decimals), true)
			.TryAdd(PositionChangeTypes.BlockedValue,
				(balance.Total - balance.Available)
					.FromBaseUnits(balance.Decimals), true),
				cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(
		OrderSubscription subscription, long target, bool isForced,
		CancellationToken cancellationToken)
	{
		DexalotOrder[] remote = [];
		if (RestClient.CanReadPrivateData)
		{
			var pair = subscription.SecurityId.SecurityCode;
			remote = await RestClient.GetOrdersAsync(pair,
				subscription.From, subscription.To, subscription.Maximum,
				cancellationToken) ?? [];

			foreach (var order in remote)
				UpdateTrackedOrder(order);
		}
		TrackedOrder[] tracked;
		using (_sync.EnterScope())
			tracked = [.. _trackedOrders.Values
				.Where(order => Matches(subscription, order))
				.OrderBy(static order => order.Time)];
		var skipped = 0;
		var delivered = 0;

		foreach (var order in tracked)
		{
			if (skipped++ < subscription.Skip)
				continue;
			if (delivered++ >= subscription.Maximum)
				break;
			var key = $"{target}:{order.OrderId}";
			var fingerprint = new OrderFingerprint(order.State,
				order.FilledVolume);
			using (_sync.EnterScope())
			{
				if (!isForced && _orderFingerprints.TryGetValue(key,
					out var previous) && previous == fingerprint)
					continue;
				_orderFingerprints[key] = fingerprint;
			}
			await SendTrackedOrderAsync(order, target, cancellationToken);
		}

		var trackedIds = tracked.Select(static order => order.OrderId)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		foreach (var order in remote.Where(order =>
			!order.OrderId.IsEmpty() &&
			!trackedIds.Contains(order.OrderId)))
		{
			if (!TryCreateRemoteOrder(order, out var converted) ||
				!Matches(subscription, converted))
				continue;
			if (skipped++ < subscription.Skip)
				continue;
			if (delivered++ >= subscription.Maximum)
				break;
			await SendTrackedOrderAsync(converted, target,
				cancellationToken);
		}
	}

	private void UpdateTrackedOrder(DexalotOrder remote)
	{
		if (remote?.OrderId.IsEmpty() != false)
			return;
		TrackedOrder tracked;
		using (_sync.EnterScope())
			_trackedOrders.TryGetValue(remote.OrderId, out tracked);
		if (tracked is null)
			return;
		try
		{
			tracked.State = remote.Status.ToOrderState();
			tracked.FilledVolume = remote.QuantityFilled.ParseDecimal(
				nameof(remote.QuantityFilled));
			tracked.UpdateTime = remote.UpdateTime ?? remote.Time ??
				DateTime.UtcNow;
			if (!remote.TotalFee.IsEmpty())
				tracked.Commission = remote.TotalFee.ParseDecimal(
					nameof(remote.TotalFee));
		}
		catch (Exception error)
		{
			this.AddWarningLog(
				"Cannot apply Dexalot REST order '{0}': {1}",
				remote.OrderId, error.Message);
		}
	}

	private bool TryCreateRemoteOrder(DexalotOrder remote,
		out TrackedOrder order)
	{
		order = null;
		if (remote?.OrderId.IsEmpty() != false ||
			remote.Pair.IsEmpty())
			return false;
		DexalotPair pair;
		using (_sync.EnterScope())
			_pairs.TryGetValue(remote.Pair, out pair);
		if (pair is null)
			return false;
		try
		{
			order = new()
			{
				OrderId = NormalizeOrderId(remote.OrderId),
				ClientOrderId = remote.ClientOrderId,
				TransactionHash = remote.TransactionHash,
				Pair = pair,
				Side = remote.Side.ToSide(),
				OrderType = remote.Type1.ToOrderType(),
				TimeInForce = remote.Type2.ToTimeInForce(),
				Price = remote.Price.ParseDecimal(nameof(remote.Price)),
				Volume = remote.Quantity.ParseDecimal(
					nameof(remote.Quantity)),
				FilledVolume = remote.QuantityFilled.ParseDecimal(
					nameof(remote.QuantityFilled)),
				Commission = remote.TotalFee.IsEmpty()
					? null
					: remote.TotalFee.ParseDecimal(
						nameof(remote.TotalFee)),
				Time = remote.Time ?? remote.UpdateTime ?? DateTime.UtcNow,
				UpdateTime = remote.UpdateTime ?? remote.Time ??
					DateTime.UtcNow,
				State = remote.Status.ToOrderState(),
			};
			return true;
		}
		catch (Exception error)
		{
			this.AddWarningLog(
				"Cannot parse Dexalot REST order '{0}': {1}",
				remote.OrderId, error.Message);
			order = null;
			return false;
		}
	}

	private async ValueTask SendRecentFillsAsync(
		KeyValuePair<long, OrderSubscription>[] orderTargets,
		CancellationToken cancellationToken)
	{
		TrackedOrder[] tracked;
		using (_sync.EnterScope())
			tracked = [.. _trackedOrders.Values];
		if (tracked.Length == 0)
			return;
		var from = tracked.Min(static order => order.Time)
			.AddMinutes(-1);
		var fills = await RestClient.GetFillsAsync(from, DateTime.UtcNow,
			100, cancellationToken) ?? [];

		foreach (var fill in fills)
		{
			var owner = tracked.FirstOrDefault(order =>
				order.OrderId.EqualsIgnoreCase(fill.OrderId));
			if (owner is null)
				continue;
			var targets = orderTargets
				.Where(item => Matches(item.Value, owner))
				.Select(static item => item.Key)
				.Append(owner.TransactionId)
				.Distinct()
				.ToArray();

			foreach (var target in targets)
				await SendFillAsync(owner, fill, target,
					cancellationToken);
		}
	}

	private async ValueTask SendFillAsync(TrackedOrder order,
		DexalotFill fill, long target,
		CancellationToken cancellationToken)
	{
		var id = fill.ExecutionId?.ToString()
			.ThrowIfEmpty("Dexalot execution identifier");
		if (!TryTrackDelivery(target, $"OT:{id}"))
			return;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = order.Pair.ToStockSharp(),
			ServerTime = fill.Time,
			PortfolioName = GetPortfolioName(),
			Side = fill.Side.ToSide(),
			OrderStringId = order.OrderId,
			TradeStringId = id,
			TradePrice = fill.Price.ParseDecimal(nameof(fill.Price)),
			TradeVolume = fill.Quantity.ParseDecimal(nameof(fill.Quantity)),
			OriginalTransactionId = target,
			Commission = fill.Fee.IsEmpty()
				? null
				: fill.Fee.ParseDecimal(nameof(fill.Fee)),
			CommissionCurrency = fill.FeeUnit,
		}, cancellationToken);
	}

	private ValueTask SendTrackedOrderAsync(TrackedOrder order, long target,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Pair.ToStockSharp(),
			ServerTime = order.UpdateTime == default
				? order.Time
				: order.UpdateTime,
			PortfolioName = GetPortfolioName(),
			Side = order.Side,
			OrderType = order.OrderType,
			TimeInForce = order.TimeInForce,
			OrderStringId = order.OrderId,
			OrderPrice = order.Price,
			OrderVolume = order.Volume,
			Balance = order.State == OrderStates.Active
				? (order.Volume - order.FilledVolume).Max(0)
				: 0m,
			OrderState = order.State,
			TransactionId = order.TransactionId,
			OriginalTransactionId = target,
			Commission = order.Commission,
			CommissionCurrency = order.CommissionCurrency,
		}, cancellationToken);

	private string ResolveOrderId(string orderId, long transactionId)
	{
		if (!orderId.IsEmpty())
			return NormalizeOrderId(orderId);
		using (_sync.EnterScope())
		{
			var order = _trackedOrders.Values.FirstOrDefault(item =>
				item.TransactionId == transactionId);
			if (order is not null)
				return order.OrderId;
		}
		throw new InvalidOperationException(
			LocalizedStrings.OrderNoExchangeId.Put(transactionId));
	}

	private static bool Matches(OrderSubscription subscription,
		TrackedOrder order)
	{
		if (!subscription.OrderId.IsEmpty() &&
			!subscription.OrderId.EqualsIgnoreCase(order.OrderId))
			return false;
		if (!subscription.SecurityId.SecurityCode.IsEmpty() &&
			!subscription.SecurityId.SecurityCode.EqualsIgnoreCase(
				order.Pair.Pair))
			return false;
		if (subscription.Side is Sides side && order.Side != side)
			return false;
		if (subscription.States is { Length: > 0 } states &&
			!states.Contains(order.State))
			return false;
		return (subscription.From is null || order.Time >=
				subscription.From) &&
			(subscription.To is null || order.Time <= subscription.To);
	}

	private static string NormalizeOrderId(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (!value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
			value.Length != 66 ||
			value[2..].Any(static ch => !Uri.IsHexDigit(ch)))
			throw new ArgumentException(
				$"Invalid Dexalot order id '{value}'.", nameof(value));
		return "0x" + value[2..].ToLowerInvariant();
	}

	private static OrderStates ToOrderState(int value)
		=> value switch
		{
			0 or 2 => OrderStates.Active,
			1 => OrderStates.Failed,
			3 or 4 or 5 or 6 => OrderStates.Done,
			_ => throw new InvalidDataException(
				$"Unknown Dexalot order status '{value}'."),
		};

	private static TimeInForce ToTimeInForce(int value)
		=> value switch
		{
			1 => TimeInForce.MatchOrCancel,
			2 => TimeInForce.CancelBalance,
			_ => TimeInForce.PutInQueue,
		};

	private static decimal? GetGasCommission(DexalotReceipt receipt)
	{
		if (receipt?.GasUsed.IsEmpty() != false ||
			receipt.EffectiveGasPrice.IsEmpty())
			return null;
		return (receipt.GasUsed.ParseInteger() *
			receipt.EffectiveGasPrice.ParseInteger()).FromBaseUnits(18);
	}

	private static void EnsureSuccessfulReceipt(DexalotReceipt receipt,
		string hash)
	{
		if (receipt?.Status.IsEmpty() != false ||
			receipt.Status.ParseInteger() != BigInteger.One)
			throw new InvalidOperationException(
				$"Dexalot transaction '{hash}' reverted.");
	}

	private async ValueTask CompleteOrderStatusAsync(
		OrderStatusMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
