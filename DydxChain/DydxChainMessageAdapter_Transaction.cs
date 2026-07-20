namespace StockSharp.DydxChain;

public partial class DydxChainMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(regMsg.PortfolioName);
		var market = GetMarket(regMsg.SecurityId);
		var volume = regMsg.Volume.Abs();
		var condition = regMsg.Condition switch
		{
			null => new DydxChainOrderCondition(),
			DydxChainOrderCondition value => value,
			_ => throw new ArgumentException(
				"The order condition is not a dYdX Chain condition.",
				nameof(regMsg)),
		};
		ValidateVolume(market, volume);

		var requestedType = regMsg.OrderType ?? OrderTypes.Limit;
		var isConditional = condition.OrderKind is
			DydxChainOrderKinds.StopLoss or DydxChainOrderKinds.TakeProfit;
		var isTwap = condition.OrderKind == DydxChainOrderKinds.Twap;
		if (!isConditional && !isTwap &&
			requestedType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(requestedType, 0));

		var isMarket = requestedType == OrderTypes.Market ||
			isConditional && regMsg.Price <= 0;
		if (isTwap)
			isMarket = false;
		var isPostOnly = condition.IsPostOnly || regMsg.PostOnly == true;
		if (isPostOnly && (isMarket || isTwap))
			throw new NotSupportedException(
				"dYdX market and TWAP orders cannot be post-only.");

		var price = regMsg.Price;
		if (isMarket || isTwap && price <= 0)
			price = CalculateMarketLimitPrice(market, regMsg.Side);
		else
			ValidatePrice(market, price, "order price");
		if (isConditional)
		{
			if (condition.TriggerPrice is not decimal triggerPrice)
				throw new InvalidOperationException(
					"dYdX conditional orders require a trigger price.");
			ValidatePrice(market, triggerPrice, "trigger price");
		}

		var flags = isTwap
			? DydxChainOrderFlags.Twap
			: isConditional
				? DydxChainOrderFlags.Conditional
				: isMarket || regMsg.TimeInForce is
					TimeInForce.CancelBalance or TimeInForce.MatchOrCancel
					? DydxChainOrderFlags.ShortTerm
					: DydxChainOrderFlags.LongTerm;
		var effectiveType = isMarket ? OrderTypes.Market : OrderTypes.Limit;
		var timeInForce = isTwap
			? DydxChainProtoTimeInForces.Unspecified
			: regMsg.TimeInForce.ToDydxChain(isPostOnly, effectiveType);
		var clientId = AllocateClientId(regMsg.TransactionId,
			regMsg.UserOrderId);
		var expiration = GetExpiration(condition.ExpirationTime ??
			regMsg.TillDate, flags);

		try
		{
			await _transactionSync.WaitAsync(cancellationToken);
			try
			{
				await RefreshChainTipAsync(cancellationToken);
				var goodTilBlock = flags == DydxChainOrderFlags.ShortTerm
					? checked(CurrentHeight + (uint)ShortTermBlockWindow)
					: 0u;
				if (flags == DydxChainOrderFlags.ShortTerm &&
					CurrentHeight == 0)
					throw new InvalidOperationException(
						"The current dYdX block height is unavailable.");
				var placeOrder = new DydxChainPlaceOrder
				{
					Address = Signer.WalletAddress,
					SubaccountNumber = checked((uint)SubaccountNumber),
					ClientId = clientId,
					ClobPairId = market.ClobPairId.ParseUInt32(
						"CLOB pair ID"),
					OrderFlags = flags,
					GoodTilBlock = goodTilBlock,
					GoodTilBlockTime = flags ==
						DydxChainOrderFlags.ShortTerm
							? 0
							: expiration.ToUnixSeconds32(),
					Side = regMsg.Side.ToDydxChain(),
					Quantums = volume.ToQuantums(market),
					Subticks = price.ToSubticks(market),
					TimeInForce = timeInForce,
					IsReduceOnly = condition.IsReduceOnly ||
						regMsg.PositionEffect == OrderPositionEffects.CloseOnly,
					ClientMetadata = isMarket ? 1u : 0u,
					ConditionType = condition.OrderKind switch
					{
						DydxChainOrderKinds.StopLoss =>
							DydxChainProtoConditionTypes.StopLoss,
						DydxChainOrderKinds.TakeProfit =>
							DydxChainProtoConditionTypes.TakeProfit,
						_ => DydxChainProtoConditionTypes.Unspecified,
					},
					ConditionalTriggerSubticks = condition.TriggerPrice is
						decimal trigger ? trigger.ToSubticks(market) : 0,
					TwapParameters = isTwap
						? CreateTwapParameters(condition)
						: null,
				};
				var account = await ApiClient.GetAccountInfoAsync(
					Signer.WalletAddress, cancellationToken);
				var transaction = Signer.SignPlaceOrder(placeOrder,
					DydxChainExtensions.ChainId, account.AccountNumber,
					account.Sequence, checked((ulong)GasLimit));
				var broadcast = await BroadcastTransactionAsync(transaction,
					cancellationToken);
				var identity = new DydxChainOrderIdentity
				{
					ClientId = clientId,
					ClobPairId = placeOrder.ClobPairId,
					OrderFlags = flags,
					Ticker = market.Ticker,
					TransactionId = regMsg.TransactionId,
				};
				using (_sync.EnterScope())
					_ordersByClientId[clientId] = identity;

				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					HasOrderInfo = true,
					SecurityId = market.Ticker.ToStockSharp(),
					ServerTime = ServerTime,
					PortfolioName = _portfolioName,
					Side = regMsg.Side,
					OrderVolume = volume,
					Balance = volume,
					OrderPrice = isMarket ? 0m : price,
					OrderType = isConditional || isTwap
						? OrderTypes.Conditional
						: requestedType,
					OrderState = OrderStates.Pending,
					OrderStringId = broadcast.Hash,
					UserOrderId = clientId.ToString(
						CultureInfo.InvariantCulture),
					TransactionId = regMsg.TransactionId,
					OriginalTransactionId = regMsg.TransactionId,
					TimeInForce = regMsg.TimeInForce,
					PostOnly = isPostOnly,
					ExpiryDate = flags == DydxChainOrderFlags.ShortTerm
						? null
						: expiration,
					PositionEffect = placeOrder.IsReduceOnly
						? OrderPositionEffects.CloseOnly
						: null,
					Condition = condition,
				}, cancellationToken);
			}
			finally
			{
				_transactionSync.Release();
			}
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (_transactionByClientId.TryGetValue(clientId,
					out var transactionId) &&
					transactionId == regMsg.TransactionId)
					_transactionByClientId.Remove(clientId);
				_ordersByClientId.Remove(clientId);
			}
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(replaceMsg.PortfolioName);
		await CancelOrderAsync(new OrderCancelMessage
		{
			TransactionId = replaceMsg.TransactionId,
			PortfolioName = replaceMsg.PortfolioName,
			SecurityId = replaceMsg.SecurityId,
			OrderId = replaceMsg.OldOrderId,
			OrderStringId = replaceMsg.OldOrderStringId,
			UserOrderId = replaceMsg.UserOrderId,
		}, cancellationToken);
		await RegisterOrderAsync(new OrderRegisterMessage
		{
			TransactionId = replaceMsg.TransactionId,
			PortfolioName = replaceMsg.PortfolioName,
			SecurityId = replaceMsg.SecurityId,
			Side = replaceMsg.Side,
			Price = replaceMsg.Price,
			Volume = replaceMsg.Volume,
			OrderType = replaceMsg.OrderType,
			TimeInForce = replaceMsg.TimeInForce,
			PostOnly = replaceMsg.PostOnly,
			TillDate = replaceMsg.TillDate,
			Condition = replaceMsg.Condition,
			PositionEffect = replaceMsg.PositionEffect,
			UserOrderId = replaceMsg.UserOrderId,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsureTradingReady();
		ValidatePortfolio(cancelMsg.PortfolioName);
		var identity = await ResolveOrderIdentityAsync(cancelMsg.OrderId,
			cancelMsg.OrderStringId, cancelMsg.UserOrderId,
			cancelMsg.SecurityId, cancellationToken);
		await CancelIdentityAsync(identity, cancelMsg.TransactionId,
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
				"dYdX bulk cancellation does not close positions.");
		if (cancelMsg.SecurityTypes is { Length: > 0 } &&
			!cancelMsg.SecurityTypes.Contains(SecurityTypes.Future))
			return;
		var ticker = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(cancelMsg.SecurityId).Ticker;
		var orders = await ApiClient.GetOrdersAsync(Signer.WalletAddress,
			SubaccountNumber, HistoryLimit, cancellationToken);
		foreach (var order in orders.Where(order => order is not null &&
			IsActive(order.Status) &&
			(ticker.IsEmpty() || order.Ticker.Equals(ticker,
				StringComparison.OrdinalIgnoreCase)) &&
			(cancelMsg.Side is null || order.Side?.ToStockSharp() ==
				cancelMsg.Side) &&
			(cancelMsg.IsStop is null || IsConditional(order) ==
				cancelMsg.IsStop)))
		{
			var identity = RememberOrder(order);
			await CancelIdentityAsync(identity, cancelMsg.TransactionId,
				cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureAccountReady();
		ValidatePortfolio(lookupMsg.PortfolioName);
		if (!lookupMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_portfolioSubscriptions.Remove(
					lookupMsg.OriginalTransactionId);
			return;
		}
		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = _portfolioName,
			BoardCode = BoardCodes.DydxChain,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId,
			cancellationToken);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_portfolioSubscriptions.Add(lookupMsg.TransactionId);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId,
			cancellationToken);
		EnsureAccountReady();
		ValidatePortfolio(statusMsg.PortfolioName);
		if (!statusMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_orderStatusSubscriptions.Remove(
					statusMsg.OriginalTransactionId);
			return;
		}
		if (statusMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}
		var subscription = CreateOrderStatusSubscription(statusMsg);
		await SendOrderSnapshotAsync(subscription, statusMsg.TransactionId,
			cancellationToken);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_orderStatusSubscriptions[statusMsg.TransactionId] = subscription;
	}

	private async ValueTask<DydxChainBroadcastResult>
		BroadcastTransactionAsync(byte[] transaction,
		CancellationToken cancellationToken)
	{
		var expectedHash = Convert.ToHexString(SHA256.HashData(transaction));
		var response = await ApiClient.BroadcastAsync(transaction,
			cancellationToken);
		if (response.Code != 0)
			throw new InvalidOperationException(
				$"dYdX rejected the transaction ({response.Codespace}:" +
				$"{response.Code}): {response.Log}");
		response.Hash = NormalizeTransactionHash(response.Hash.IsEmpty()
			? expectedHash
			: response.Hash);
		if (!response.Hash.Equals(expectedHash,
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"dYdX returned a transaction hash that does not match the " +
				"signed bytes.");
		return response;
	}

	private async ValueTask CancelIdentityAsync(
		DydxChainOrderIdentity identity, long transactionId,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(identity);
		var market = GetMarket(identity.ClobPairId) ?? throw new
			InvalidOperationException(
				$"Unknown dYdX CLOB pair '{identity.ClobPairId}'.");
		await _transactionSync.WaitAsync(cancellationToken);
		try
		{
			await RefreshChainTipAsync(cancellationToken);
			var isShortTerm = identity.OrderFlags ==
				DydxChainOrderFlags.ShortTerm;
			var cancellation = new DydxChainCancelOrder
			{
				Address = Signer.WalletAddress,
				SubaccountNumber = checked((uint)SubaccountNumber),
				ClientId = identity.ClientId,
				ClobPairId = identity.ClobPairId,
				OrderFlags = identity.OrderFlags,
				GoodTilBlock = isShortTerm
					? checked(CurrentHeight + (uint)ShortTermBlockWindow)
					: 0u,
				GoodTilBlockTime = isShortTerm
					? 0u
					: (ServerTime + StatefulOrderLifetime).ToUnixSeconds32(),
			};
			var account = await ApiClient.GetAccountInfoAsync(
				Signer.WalletAddress, cancellationToken);
			var bytes = Signer.SignCancelOrder(cancellation,
				DydxChainExtensions.ChainId, account.AccountNumber,
				account.Sequence, checked((ulong)GasLimit));
			_ = await BroadcastTransactionAsync(bytes, cancellationToken);
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = market.Ticker.ToStockSharp(),
				ServerTime = ServerTime,
				PortfolioName = _portfolioName,
				OrderStringId = identity.OrderId,
				UserOrderId = identity.ClientId.ToString(
					CultureInfo.InvariantCulture),
				OrderState = OrderStates.Pending,
				TransactionId = identity.TransactionId,
				OriginalTransactionId = transactionId,
			}, cancellationToken);
		}
		finally
		{
			_transactionSync.Release();
		}
	}

	private async ValueTask<DydxChainOrderIdentity> ResolveOrderIdentityAsync(
		long? numericOrderId, string orderId, string userOrderId,
		SecurityId securityId, CancellationToken cancellationToken)
	{
		DydxChainOrderIdentity identity = null;
		uint? clientId = null;
		if (!orderId.IsEmpty())
		{
			orderId = orderId.Trim();
			using (_sync.EnterScope())
				_ordersById.TryGetValue(orderId, out identity);
			if (identity is null)
			{
				var order = await ApiClient.TryGetOrderAsync(orderId,
					cancellationToken) ?? throw new InvalidOperationException(
						$"dYdX order '{orderId}' was not found.");
				identity = RememberOrder(order);
			}
		}
		if (identity is null && !userOrderId.IsEmpty())
			clientId = ParseClientId(userOrderId, "client order ID");
		if (identity is null && numericOrderId is long numeric)
		{
			if (numeric is < 0 or > uint.MaxValue)
				throw new InvalidOperationException(
					"A numeric dYdX order identifier must fit uint32 client ID.");
			clientId = checked((uint)numeric);
		}
		if (identity is null && clientId is uint value)
		{
			using (_sync.EnterScope())
				_ordersByClientId.TryGetValue(value, out identity);
			if (identity is null)
			{
				var orders = await ApiClient.GetOrdersAsync(
					Signer.WalletAddress, SubaccountNumber, HistoryLimit,
					cancellationToken);
				var order = orders.FirstOrDefault(item => item is not null &&
					item.ClientId == value.ToString(
						CultureInfo.InvariantCulture));
				if (order is not null)
					identity = RememberOrder(order);
			}
		}
		if (identity is null)
			throw new InvalidOperationException(
				"dYdX cancellation requires an exchange order ID or uint32 " +
				"client order ID.");
		if (!securityId.SecurityCode.IsEmpty() &&
			!GetMarket(securityId).Ticker.Equals(identity.Ticker,
				StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"The dYdX cancellation security does not match the order.");
		return identity;
	}

	private DydxChainOrderIdentity RememberOrder(DydxChainOrder update)
	{
		ArgumentNullException.ThrowIfNull(update);
		if (update.Id.IsEmpty())
			throw new InvalidDataException("dYdX order has no exchange ID.");
		DydxChainOrder order;
		using (_sync.EnterScope())
		{
			if (_knownOrders.TryGetValue(update.Id, out order))
				MergeOrder(order, update);
			else
			{
				order = update;
				_knownOrders.Add(order.Id, order);
			}
		}
		var clientId = ParseClientId(order.ClientId, "client order ID");
		var clobPairId = order.ClobPairId.ParseUInt32("CLOB pair ID");
		var market = GetMarket(clobPairId) ?? throw new InvalidDataException(
			$"dYdX order refers to unknown CLOB pair '{clobPairId}'.");
		var ticker = order.Ticker.IsEmpty()
			? market.Ticker
			: order.Ticker.NormalizeTicker();
		var identity = new DydxChainOrderIdentity
		{
			OrderId = order.Id,
			ClientId = clientId,
			ClobPairId = clobPairId,
			OrderFlags = ParseOrderFlags(order.OrderFlags),
			Ticker = ticker,
			TransactionId = GetTransactionId(clientId),
		};
		using (_sync.EnterScope())
		{
			_ordersById[identity.OrderId] = identity;
			_ordersByClientId[identity.ClientId] = identity;
		}
		return identity;
	}

	private uint AllocateClientId(long transactionId, string userOrderId)
	{
		if (transactionId <= 0)
			throw new ArgumentOutOfRangeException(nameof(transactionId));
		uint clientId;
		if (!userOrderId.IsEmpty())
			clientId = ParseClientId(userOrderId, "client order ID");
		else
		{
			var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
				transactionId.ToString(CultureInfo.InvariantCulture)));
			clientId = BitConverter.ToUInt32(hash, 0);
			if (clientId == 0)
				clientId = 1;
		}
		using (_sync.EnterScope())
		{
			if (_transactionByClientId.ContainsKey(clientId))
				throw new InvalidOperationException(
					$"dYdX client order ID '{clientId}' is already in use.");
			_transactionByClientId.Add(clientId, transactionId);
		}
		return clientId;
	}

	private static uint ParseClientId(string value, string field)
	{
		if (value.IsEmpty() || !uint.TryParse(value.Trim(), NumberStyles.None,
			CultureInfo.InvariantCulture, out var result))
			throw new InvalidDataException(
				$"dYdX {field} must be a uint32 value.");
		return result;
	}

	private DateTime GetExpiration(DateTime? requested,
		DydxChainOrderFlags flags)
	{
		if (flags == DydxChainOrderFlags.ShortTerm)
			return ServerTime;
		var now = ServerTime;
		var expiration = (requested ?? now + StatefulOrderLifetime).EnsureUtc();
		if (expiration <= now.AddSeconds(30))
			throw new InvalidOperationException(
				"dYdX stateful order expiration must be at least 30 seconds " +
				"in the future.");
		if (expiration > now.AddDays(95))
			throw new InvalidOperationException(
				"dYdX stateful order expiration cannot exceed 95 days.");
		_ = expiration.ToUnixSeconds32();
		return expiration;
	}

	private static DydxChainTwapParameters CreateTwapParameters(
		DydxChainOrderCondition condition)
	{
		var duration = condition.TwapDuration.TotalSeconds;
		var interval = condition.TwapInterval.TotalSeconds;
		if (duration != Math.Truncate(duration) ||
			interval != Math.Truncate(interval) ||
			duration is < 300 or > 86400 || interval is < 30 or > 3600 ||
			duration % interval != 0)
			throw new InvalidOperationException(
				"dYdX TWAP duration must be 5 minutes to 24 hours; interval " +
				"must be 30 seconds to one hour and divide the duration.");
		var tolerance = condition.TwapPriceTolerance * 10000m;
		if (tolerance < 0 || tolerance >= 1_000_000m ||
			tolerance != decimal.Truncate(tolerance))
			throw new InvalidOperationException(
				"dYdX TWAP price tolerance must resolve to 0..999999 ppm.");
		return new()
		{
			Duration = checked((uint)duration),
			Interval = checked((uint)interval),
			PriceTolerance = checked((uint)tolerance),
		};
	}

	private decimal CalculateMarketLimitPrice(DydxChainMarket market,
		Sides side)
	{
		var oracle = GetOraclePrice(market.Ticker) ??
			market.OraclePrice.TryParseDecimal();
		if (oracle is not > 0)
			throw new InvalidOperationException(
				$"The dYdX oracle price for '{market.Ticker}' is unavailable.");
		var factor = MarketOrderSlippage / 100m;
		var result = side == Sides.Buy
			? oracle.Value * (1m + factor)
			: oracle.Value * (1m - factor);
		if (result <= 0)
			throw new InvalidOperationException(
				"The calculated dYdX market-order limit price is invalid.");
		var tickSize = market.TickSize.ParseDecimal("tick size");
		var ticks = result / tickSize;
		var aligned = (side == Sides.Buy
			? decimal.Ceiling(ticks)
			: decimal.Floor(ticks)) * tickSize;
		if (aligned <= 0)
			throw new InvalidOperationException(
				"The aligned dYdX market-order limit price is invalid.");
		return aligned;
	}

	private static void ValidateVolume(DydxChainMarket market, decimal volume)
	{
		var step = market.StepSize.ParseDecimal("step size");
		if (volume <= 0 || volume % step != 0)
			throw new InvalidOperationException(
				$"dYdX order volume must be a positive multiple of {step}.");
	}

	private static void ValidatePrice(DydxChainMarket market, decimal price,
		string field)
	{
		var step = market.TickSize.ParseDecimal("tick size");
		if (price <= 0 || price % step != 0)
			throw new InvalidOperationException(
				$"dYdX {field} must be a positive multiple of {step}.");
	}

	private static string NormalizeTransactionHash(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			value = value[2..];
		if (value.Length != 64 || value.Any(static ch => !Uri.IsHexDigit(ch)))
			throw new InvalidDataException(
				$"dYdX returned invalid transaction hash '{value}'.");
		return value.ToUpperInvariant();
	}

	private static DydxChainOrderFlags ParseOrderFlags(string value)
	{
		var flags = value.ParseUInt32("order flags");
		return flags switch
		{
			0 => DydxChainOrderFlags.ShortTerm,
			32 => DydxChainOrderFlags.Conditional,
			64 => DydxChainOrderFlags.LongTerm,
			128 => DydxChainOrderFlags.Twap,
			256 => DydxChainOrderFlags.TwapSuborder,
			_ => throw new InvalidDataException(
				$"dYdX returned unsupported order flags '{flags}'."),
		};
	}

	private static bool IsActive(DydxChainOrderStatuses status)
		=> status is DydxChainOrderStatuses.BestEffortOpened or
			DydxChainOrderStatuses.Open or
			DydxChainOrderStatuses.Untriggered;

	private static bool IsConditional(DydxChainOrder order)
		=> order.Type is DydxChainOrderTypes.StopLimit or
			DydxChainOrderTypes.StopMarket or
			DydxChainOrderTypes.TakeProfit or
			DydxChainOrderTypes.TakeProfitMarket or
			DydxChainOrderTypes.TrailingStop or
			DydxChainOrderTypes.Twap;

	private async ValueTask SendPortfolioSnapshotAsync(long target,
		CancellationToken cancellationToken)
	{
		var snapshot = await ApiClient.GetSubaccountAsync(
			Signer.WalletAddress, SubaccountNumber, cancellationToken);
		await SendSubaccountBalanceAsync(snapshot, target, cancellationToken);
		await SendPerpetualPositionSnapshotAsync(
			snapshot.OpenPerpetualPositions, [target], cancellationToken);
		foreach (var position in snapshot.AssetPositions ?? [])
			await SendAssetPositionAsync(position, target, cancellationToken);
	}

	private async ValueTask SendAccountSnapshotToSubscribersAsync(
		CancellationToken cancellationToken)
	{
		long[] portfolioTargets;
		KeyValuePair<long, OrderStatusSubscription>[] orderTargets;
		using (_sync.EnterScope())
		{
			portfolioTargets = [.. _portfolioSubscriptions];
			orderTargets = [.. _orderStatusSubscriptions];
		}
		if (portfolioTargets.Length > 0)
		{
			var snapshot = await ApiClient.GetSubaccountAsync(
				Signer.WalletAddress, SubaccountNumber, cancellationToken);
			foreach (var target in portfolioTargets)
				await SendSubaccountBalanceAsync(snapshot, target,
					cancellationToken);
			await SendPerpetualPositionSnapshotAsync(
				snapshot.OpenPerpetualPositions, portfolioTargets,
				cancellationToken);
			foreach (var target in portfolioTargets)
				foreach (var position in snapshot.AssetPositions ?? [])
					await SendAssetPositionAsync(position, target,
						cancellationToken);
		}
		if (orderTargets.Length > 0)
		{
			var orders = await ApiClient.GetOrdersAsync(Signer.WalletAddress,
				SubaccountNumber, HistoryLimit, cancellationToken);
			var fills = await ApiClient.GetFillsAsync(Signer.WalletAddress,
				SubaccountNumber, HistoryLimit, cancellationToken);
			foreach (var target in orderTargets)
			{
				foreach (var order in orders.Where(item => item is not null &&
					Matches(target.Value, item)))
					await SendOrderAsync(order, target.Key, cancellationToken);
				foreach (var fill in fills.Where(item => item is not null &&
					Matches(target.Value, item)))
					await SendFillAsync(fill, target.Key, false,
						cancellationToken);
			}
		}
	}

	private async ValueTask OnSubaccountSnapshotAsync(
		DydxChainSubaccountSnapshot snapshot,
		CancellationToken cancellationToken)
	{
		if (snapshot is null)
			return;
		if (!snapshot.BlockHeight.IsEmpty())
			UpdateServer(DateTime.UtcNow,
				snapshot.BlockHeight.ParseUInt32("subaccount block height"));
		long[] portfolioTargets;
		using (_sync.EnterScope())
			portfolioTargets = [.. _portfolioSubscriptions];
		foreach (var target in portfolioTargets)
			await SendSubaccountBalanceAsync(snapshot, target,
				cancellationToken);
		await SendPerpetualPositionSnapshotAsync(
			snapshot.OpenPerpetualPositions, portfolioTargets,
			cancellationToken);
		foreach (var target in portfolioTargets)
			foreach (var position in snapshot.AssetPositions ?? [])
				await SendAssetPositionAsync(position, target, cancellationToken);
		foreach (var order in snapshot.Orders ?? [])
			await DispatchOrderAsync(order, cancellationToken);
	}

	private async ValueTask OnSubaccountUpdateAsync(
		DydxChainSubaccountUpdate update,
		CancellationToken cancellationToken)
	{
		if (update is null)
			return;
		if (!update.BlockHeight.IsEmpty())
			UpdateServer(DateTime.UtcNow,
				update.BlockHeight.ParseUInt32("subaccount block height"));
		long[] portfolioTargets;
		using (_sync.EnterScope())
			portfolioTargets = [.. _portfolioSubscriptions];
		foreach (var target in portfolioTargets)
		{
			foreach (var position in update.PerpetualPositions ?? [])
				await SendPerpetualPositionAsync(position, target,
					cancellationToken);
			foreach (var position in update.AssetPositions ?? [])
				await SendAssetPositionAsync(position, target,
					cancellationToken);
		}
		foreach (var order in update.Orders ?? [])
			await DispatchOrderAsync(order, cancellationToken);
		foreach (var fill in update.Fills ?? [])
			await DispatchFillAsync(fill, cancellationToken);
	}

	private ValueTask SendSubaccountBalanceAsync(
		DydxChainSubaccountSnapshot snapshot, long target,
		CancellationToken cancellationToken)
	{
		if (target == 0)
			return default;
		var equity = snapshot.Equity.TryParseDecimal();
		var free = snapshot.FreeCollateral.TryParseDecimal();
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = "USDC".ToStockSharp(),
			ServerTime = ServerTime,
			OriginalTransactionId = target,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, equity, true)
		.TryAdd(PositionChangeTypes.BlockedValue,
			equity is decimal total && free is decimal available
				? (total - available).Max(0m)
				: null, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, free, true),
			cancellationToken);
	}

	private async ValueTask SendPerpetualPositionSnapshotAsync(
		DydxChainPerpetualPosition[] positions, long[] targets,
		CancellationToken cancellationToken)
	{
		var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var position in positions ?? [])
			if (position?.Market.IsEmpty() == false &&
				position.Status == DydxChainPositionStatuses.Open)
				current.Add(position.Market.NormalizeTicker());
		string[] missing;
		using (_sync.EnterScope())
		{
			missing = [.. _knownPositionTickers.Where(ticker =>
				!current.Contains(ticker))];
			_knownPositionTickers.Clear();
			_knownPositionTickers.UnionWith(current);
		}
		foreach (var target in targets)
		{
			foreach (var position in positions ?? [])
				await SendPerpetualPositionAsync(position, target,
					cancellationToken);
			foreach (var ticker in missing)
				await SendOutMessageAsync(new PositionChangeMessage
				{
					PortfolioName = _portfolioName,
					SecurityId = ticker.ToStockSharp(),
					ServerTime = ServerTime,
					OriginalTransactionId = target,
				}
				.TryAdd(PositionChangeTypes.CurrentValue, 0m, true),
					cancellationToken);
		}
	}

	private ValueTask SendPerpetualPositionAsync(
		DydxChainPerpetualPosition position, long target,
		CancellationToken cancellationToken)
	{
		if (position?.Market.IsEmpty() != false || target == 0)
			return default;
		var ticker = position.Market.NormalizeTicker();
		var size = position.Size.TryParseDecimal()?.Abs();
		if (position.Status != DydxChainPositionStatuses.Open)
			size = 0m;
		using (_sync.EnterScope())
			if (size is > 0)
				_knownPositionTickers.Add(ticker);
			else
				_knownPositionTickers.Remove(ticker);
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = ticker.ToStockSharp(),
			ServerTime = ServerTime,
			OriginalTransactionId = target,
			Side = position.Side == DydxChainPositionSides.Long
				? Sides.Buy
				: Sides.Sell,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, size, true)
		.TryAdd(PositionChangeTypes.AveragePrice,
			position.EntryPrice.TryParseDecimal(), true)
		.TryAdd(PositionChangeTypes.RealizedPnL,
			position.RealizedPnl.TryParseDecimal(), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL,
			position.UnrealizedPnl.TryParseDecimal(), true)
		.TryAdd(PositionChangeTypes.VariationMargin,
			position.NetFunding.TryParseDecimal(), true), cancellationToken);
	}

	private ValueTask SendAssetPositionAsync(DydxChainAssetPosition position,
		long target, CancellationToken cancellationToken)
	{
		if (position?.Symbol.IsEmpty() != false || target == 0)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = position.Symbol.Trim().ToUpperInvariant().ToStockSharp(),
			ServerTime = ServerTime,
			OriginalTransactionId = target,
			Side = position.Side == DydxChainPositionSides.Long
				? Sides.Buy
				: Sides.Sell,
		}
		.TryAdd(PositionChangeTypes.CurrentValue,
			position.Size.TryParseDecimal()?.Abs(), true), cancellationToken);
	}

	private OrderStatusSubscription CreateOrderStatusSubscription(
		OrderStatusMessage message)
	{
		var tickers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (!message.SecurityId.SecurityCode.IsEmpty())
			tickers.Add(GetMarket(message.SecurityId).Ticker);
		foreach (var securityId in message.SecurityIds)
			if (!securityId.SecurityCode.IsEmpty())
				tickers.Add(GetMarket(securityId).Ticker);
		string clientId = null;
		if (!message.UserOrderId.IsEmpty())
			clientId = ParseClientId(message.UserOrderId,
				"client order ID").ToString(CultureInfo.InvariantCulture);
		if (message.OrderId is long numeric)
		{
			if (numeric is < 0 or > uint.MaxValue)
				throw new InvalidOperationException(
					"dYdX numeric order filter must fit uint32 client ID.");
			var numericClientId = checked((uint)numeric).ToString(
				CultureInfo.InvariantCulture);
			if (!clientId.IsEmpty() && clientId != numericClientId)
				throw new InvalidOperationException(
					"Conflicting dYdX client order ID filters.");
			clientId = numericClientId;
		}
		return new()
		{
			OrderId = message.OrderStringId?.Trim(),
			ClientId = clientId,
			Tickers = [.. tickers],
			Side = message.Side,
			Volume = message.Volume,
			States = message.States ?? [],
			From = message.From?.EnsureUtc(),
			To = message.To?.EnsureUtc(),
			Skip = Math.Max(0, message.Skip ?? 0).Min(int.MaxValue).To<int>(),
			Maximum = (message.Count ?? HistoryLimit).Min(HistoryLimit).Max(1)
				.To<int>(),
		};
	}

	private async ValueTask SendOrderSnapshotAsync(
		OrderStatusSubscription subscription, long target,
		CancellationToken cancellationToken)
	{
		var orders = await ApiClient.GetOrdersAsync(Signer.WalletAddress,
			SubaccountNumber, HistoryLimit, cancellationToken);
		var skipped = 0;
		var delivered = 0;
		foreach (var order in orders.Where(static item => item is not null)
			.Where(item => Matches(subscription, item))
			.OrderBy(GetOrderTime))
		{
			if (skipped++ < subscription.Skip)
				continue;
			if (delivered++ >= subscription.Maximum)
				break;
			await SendOrderAsync(order, target, cancellationToken);
		}

		var fills = await ApiClient.GetFillsAsync(Signer.WalletAddress,
			SubaccountNumber, HistoryLimit, cancellationToken);
		skipped = 0;
		delivered = 0;
		foreach (var fill in fills.Where(static item => item is not null)
			.Where(item => Matches(subscription, item))
			.OrderBy(static item => item.CreatedAt,
				StringComparer.Ordinal))
		{
			if (skipped++ < subscription.Skip)
				continue;
			if (delivered++ >= subscription.Maximum)
				break;
			await SendFillAsync(fill, target, false, cancellationToken);
		}
	}

	private async ValueTask DispatchOrderAsync(DydxChainOrder update,
		CancellationToken cancellationToken)
	{
		if (update?.Id.IsEmpty() != false)
			return;
		DydxChainOrder order;
		using (_sync.EnterScope())
		{
			if (_knownOrders.TryGetValue(update.Id, out var known))
			{
				MergeOrder(known, update);
				order = known;
			}
			else
				order = update;
		}
		if (order.ClobPairId.IsEmpty() || order.ClientId.IsEmpty() ||
			order.Side is null || order.Type is null || order.Size.IsEmpty())
		{
			var full = await ApiClient.TryGetOrderAsync(order.Id,
				cancellationToken);
			if (full is null)
				return;
			using (_sync.EnterScope())
			{
				if (_knownOrders.TryGetValue(full.Id, out var known))
				{
					MergeOrder(known, full);
					order = known;
				}
				else
					order = full;
			}
		}
		var identity = RememberOrder(order);
		var targets = new HashSet<long>();
		if (identity.TransactionId > 0)
			targets.Add(identity.TransactionId);
		using (_sync.EnterScope())
			foreach (var subscription in _orderStatusSubscriptions)
				if (Matches(subscription.Value, order))
					targets.Add(subscription.Key);
		foreach (var target in targets)
			await SendOrderAsync(order, target, cancellationToken);
	}

	private async ValueTask DispatchFillAsync(DydxChainFill fill,
		CancellationToken cancellationToken)
	{
		if (fill?.Id.IsEmpty() != false || !TryAcceptFill(fill.Id))
			return;
		var targets = new HashSet<long>();
		using (_sync.EnterScope())
		{
			if (!fill.OrderId.IsEmpty() &&
				_ordersById.TryGetValue(fill.OrderId, out var identity) &&
				identity.TransactionId > 0)
				targets.Add(identity.TransactionId);
			foreach (var subscription in _orderStatusSubscriptions)
				if (Matches(subscription.Value, fill))
					targets.Add(subscription.Key);
		}
		foreach (var target in targets)
			await SendFillCoreAsync(fill, target, cancellationToken);
	}

	private async ValueTask SendOrderAsync(DydxChainOrder order, long target,
		CancellationToken cancellationToken)
	{
		if (target == 0)
			return;
		var identity = RememberOrder(order);
		DydxChainOrder complete;
		using (_sync.EnterScope())
			complete = _knownOrders[identity.OrderId];
		var message = CreateOrderMessage(complete, identity, target);
		UpdateServer(message.ServerTime);
		await SendOutMessageAsync(message, cancellationToken);
	}

	private ExecutionMessage CreateOrderMessage(DydxChainOrder order,
		DydxChainOrderIdentity identity, long target)
	{
		var side = order.Side ?? throw new InvalidDataException(
			"dYdX order has no side.");
		var type = order.Type ?? throw new InvalidDataException(
			"dYdX order has no type.");
		var volume = order.Size.ParseDecimal("order size", true);
		var filled = order.TotalFilled.TryParseDecimal() ?? 0m;
		var time = GetOrderTime(order);
		var condition = CreateOrderCondition(order);
		var isMarket = type is DydxChainOrderTypes.Market or
			DydxChainOrderTypes.StopMarket or
			DydxChainOrderTypes.TakeProfitMarket;
		return new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = identity.Ticker.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = side.ToStockSharp(),
			OrderVolume = volume,
			Balance = (volume - filled).Max(0m),
			OrderPrice = isMarket
				? 0m
				: order.Price.ParseDecimal("order price"),
			OrderType = type.ToStockSharp(),
			OrderState = order.Status.ToStockSharp(),
			OrderStringId = order.Id,
			UserOrderId = order.ClientId,
			TransactionId = identity.TransactionId,
			OriginalTransactionId = target,
			TimeInForce = order.TimeInForce.ToStockSharp(),
			PostOnly = order.IsPostOnly ?? false,
			ExpiryDate = order.GoodTilBlockTime.IsEmpty()
				? null
				: order.GoodTilBlockTime.ParseUtcTime("order expiration"),
			PositionEffect = order.IsReduceOnly == true
				? OrderPositionEffects.CloseOnly
				: null,
			Condition = condition,
			Error = order.Status == DydxChainOrderStatuses.Error
				? new InvalidOperationException(order.RemovalReason.IsEmpty()
					? "dYdX rejected the order."
					: order.RemovalReason)
				: null,
		};
	}

	private async ValueTask SendFillAsync(DydxChainFill fill, long target,
		bool isOnlyNew, CancellationToken cancellationToken)
	{
		var isNew = TryAcceptFill(fill?.Id);
		if (isOnlyNew && !isNew)
			return;
		await SendFillCoreAsync(fill, target, cancellationToken);
	}

	private ValueTask SendFillCoreAsync(DydxChainFill fill, long target,
		CancellationToken cancellationToken)
	{
		if (fill?.Id.IsEmpty() != false || target == 0)
			return default;
		var ticker = fill.Ticker.IsEmpty()
			? GetMarket(fill.ClobPairId.ParseUInt32("fill CLOB pair ID"))?.Ticker
			: fill.Ticker.NormalizeTicker();
		if (ticker.IsEmpty())
			throw new InvalidDataException(
				"dYdX fill refers to an unknown market.");
		var time = fill.CreatedAt.ParseUtcTime("fill time");
		UpdateServer(time);
		long transactionId = 0;
		if (!fill.OrderId.IsEmpty())
			using (_sync.EnterScope())
				if (_ordersById.TryGetValue(fill.OrderId, out var identity))
					transactionId = identity.TransactionId;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = ticker.ToStockSharp(),
			ServerTime = time,
			PortfolioName = _portfolioName,
			Side = fill.Side.ToStockSharp(),
			OrderStringId = fill.OrderId,
			TradeStringId = fill.Id,
			TradePrice = fill.Price.ParseDecimal("fill price"),
			TradeVolume = fill.Size.ParseDecimal("fill size", true),
			Commission = fill.Fee.TryParseDecimal(),
			CommissionCurrency = "USDC",
			TransactionId = transactionId,
			OriginalTransactionId = target,
		}, cancellationToken);
	}

	private DydxChainOrderCondition CreateOrderCondition(DydxChainOrder order)
	{
		var kind = order.Type switch
		{
			DydxChainOrderTypes.StopLimit or
			DydxChainOrderTypes.StopMarket => DydxChainOrderKinds.StopLoss,
			DydxChainOrderTypes.TakeProfit or
			DydxChainOrderTypes.TakeProfitMarket =>
				DydxChainOrderKinds.TakeProfit,
			DydxChainOrderTypes.Twap or
			DydxChainOrderTypes.TwapSuborder => DydxChainOrderKinds.Twap,
			_ => DydxChainOrderKinds.Regular,
		};
		var condition = new DydxChainOrderCondition
		{
			OrderKind = kind,
			TriggerPrice = order.TriggerPrice.TryParseDecimal(),
			IsReduceOnly = order.IsReduceOnly ?? false,
			IsPostOnly = order.IsPostOnly ?? false,
			ExpirationTime = order.GoodTilBlockTime.IsEmpty()
				? null
				: order.GoodTilBlockTime.ParseUtcTime("order expiration"),
		};
		if (kind == DydxChainOrderKinds.Twap)
		{
			if (order.Duration.TryParseDecimal() is decimal duration)
				condition.TwapDuration = TimeSpan.FromSeconds((double)duration);
			if (order.Interval.TryParseDecimal() is decimal interval)
				condition.TwapInterval = TimeSpan.FromSeconds((double)interval);
			if (order.PriceTolerance.TryParseDecimal() is decimal tolerance)
				condition.TwapPriceTolerance = tolerance / 10000m;
		}
		return condition;
	}

	private bool Matches(OrderStatusSubscription subscription,
		DydxChainOrder order)
	{
		if (!subscription.OrderId.IsEmpty() &&
			!subscription.OrderId.Equals(order.Id,
				StringComparison.OrdinalIgnoreCase))
			return false;
		if (!subscription.ClientId.IsEmpty() &&
			!subscription.ClientId.Equals(order.ClientId,
				StringComparison.Ordinal))
			return false;
		var ticker = GetOrderTicker(order);
		if (subscription.Tickers is { Length: > 0 } &&
			!subscription.Tickers.Contains(ticker,
				StringComparer.OrdinalIgnoreCase))
			return false;
		if (subscription.Side is Sides side &&
			order.Side?.ToStockSharp() != side)
			return false;
		if (subscription.Volume is decimal volume &&
			order.Size.TryParseDecimal() != volume)
			return false;
		var state = order.Status.ToStockSharp();
		if (subscription.States is { Length: > 0 } &&
			!subscription.States.Contains(state))
			return false;
		var time = GetOrderTime(order);
		return (subscription.From is null || time >= subscription.From) &&
			(subscription.To is null || time <= subscription.To);
	}

	private bool Matches(OrderStatusSubscription subscription,
		DydxChainFill fill)
	{
		if (!subscription.OrderId.IsEmpty() &&
			!subscription.OrderId.Equals(fill.OrderId,
				StringComparison.OrdinalIgnoreCase))
			return false;
		if (!subscription.ClientId.IsEmpty())
		{
			DydxChainOrderIdentity identity = null;
			if (!fill.OrderId.IsEmpty())
				using (_sync.EnterScope())
					_ordersById.TryGetValue(fill.OrderId, out identity);
			if (identity is null ||
				identity.ClientId.ToString(CultureInfo.InvariantCulture) !=
				subscription.ClientId)
				return false;
		}
		var ticker = fill.Ticker.IsEmpty()
			? GetMarket(fill.ClobPairId.ParseUInt32("fill CLOB pair ID"))?.Ticker
			: fill.Ticker.NormalizeTicker();
		if (subscription.Tickers is { Length: > 0 } &&
			!subscription.Tickers.Contains(ticker,
				StringComparer.OrdinalIgnoreCase))
			return false;
		if (subscription.Side is Sides side &&
			fill.Side.ToStockSharp() != side)
			return false;
		if (subscription.Volume is decimal volume &&
			fill.Size.TryParseDecimal() != volume)
			return false;
		var time = fill.CreatedAt.ParseUtcTime("fill time");
		return (subscription.From is null || time >= subscription.From) &&
			(subscription.To is null || time <= subscription.To);
	}

	private string GetOrderTicker(DydxChainOrder order)
		=> order.Ticker.IsEmpty()
			? GetMarket(order.ClobPairId.ParseUInt32("CLOB pair ID"))?.Ticker
				?? throw new InvalidDataException(
					"dYdX order refers to an unknown market.")
			: order.Ticker.NormalizeTicker();

	private DateTime GetOrderTime(DydxChainOrder order)
	{
		var value = order.UpdatedAt.IsEmpty()
			? order.CreatedAt
			: order.UpdatedAt;
		return value.IsEmpty()
			? ServerTime
			: value.ParseUtcTime("order time");
	}

	private static void MergeOrder(DydxChainOrder target,
		DydxChainOrder update)
	{
		if (!update.SubaccountId.IsEmpty())
			target.SubaccountId = update.SubaccountId;
		if (!update.ClientId.IsEmpty())
			target.ClientId = update.ClientId;
		if (!update.ClobPairId.IsEmpty())
			target.ClobPairId = update.ClobPairId;
		if (update.Side is DydxChainOrderSides side)
			target.Side = side;
		if (!update.Size.IsEmpty())
			target.Size = update.Size;
		if (!update.TotalFilled.IsEmpty())
			target.TotalFilled = update.TotalFilled;
		if (!update.TotalOptimisticFilled.IsEmpty())
			target.TotalOptimisticFilled = update.TotalOptimisticFilled;
		if (!update.Price.IsEmpty())
			target.Price = update.Price;
		if (update.Type is DydxChainOrderTypes type)
			target.Type = type;
		if (update.IsReduceOnly is bool isReduceOnly)
			target.IsReduceOnly = isReduceOnly;
		if (!update.OrderFlags.IsEmpty())
			target.OrderFlags = update.OrderFlags;
		if (!update.GoodTilBlock.IsEmpty())
			target.GoodTilBlock = update.GoodTilBlock;
		if (!update.GoodTilBlockTime.IsEmpty())
			target.GoodTilBlockTime = update.GoodTilBlockTime;
		if (!update.CreatedAtHeight.IsEmpty())
			target.CreatedAtHeight = update.CreatedAtHeight;
		if (!update.CreatedAt.IsEmpty())
			target.CreatedAt = update.CreatedAt;
		if (!update.ClientMetadata.IsEmpty())
			target.ClientMetadata = update.ClientMetadata;
		if (!update.TriggerPrice.IsEmpty())
			target.TriggerPrice = update.TriggerPrice;
		if (!update.Duration.IsEmpty())
			target.Duration = update.Duration;
		if (!update.Interval.IsEmpty())
			target.Interval = update.Interval;
		if (!update.PriceTolerance.IsEmpty())
			target.PriceTolerance = update.PriceTolerance;
		if (update.TimeInForce is DydxChainTimeInForces timeInForce)
			target.TimeInForce = timeInForce;
		target.Status = update.Status;
		if (update.IsPostOnly is bool isPostOnly)
			target.IsPostOnly = isPostOnly;
		if (!update.Ticker.IsEmpty())
			target.Ticker = update.Ticker;
		if (!update.UpdatedAt.IsEmpty())
			target.UpdatedAt = update.UpdatedAt;
		if (!update.UpdatedAtHeight.IsEmpty())
			target.UpdatedAtHeight = update.UpdatedAtHeight;
		if (update.SubaccountNumber != 0)
			target.SubaccountNumber = update.SubaccountNumber;
		if (!update.RemovalReason.IsEmpty())
			target.RemovalReason = update.RemovalReason;
	}
}
