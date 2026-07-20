namespace StockSharp.Injective;

public partial class InjectiveMessageAdapter
{
	private static readonly TimeSpan _estimatedBlockInterval =
		TimeSpan.FromMilliseconds(650);

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		ValidatePortfolio(regMsg.PortfolioName);
		EnsureTradingReady();
		await PlaceOrderAsync(GetMarket(regMsg.SecurityId), regMsg.TransactionId,
			regMsg.Side, regMsg.Volume.Abs(), regMsg.Price,
			regMsg.OrderType ?? OrderTypes.Limit, regMsg.TimeInForce,
			regMsg.PostOnly == true, regMsg.PositionEffect,
			regMsg.Condition as InjectiveOrderCondition ?? new(),
			regMsg.TillDate, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		ValidatePortfolio(replaceMsg.PortfolioName);
		EnsureTradingReady();
		var market = GetMarket(replaceMsg.SecurityId);
		var oldOrder = await ResolveOrderAsync(replaceMsg.OldOrderStringId,
			replaceMsg.OldOrderId, market, cancellationToken);
		await CancelKnownOrderAsync(oldOrder, replaceMsg.TransactionId,
			cancellationToken);
		await PlaceOrderAsync(market, replaceMsg.TransactionId, replaceMsg.Side,
			replaceMsg.Volume.Abs(), replaceMsg.Price,
			replaceMsg.OrderType ?? OrderTypes.Limit, replaceMsg.TimeInForce,
			replaceMsg.PostOnly == true, replaceMsg.PositionEffect,
			replaceMsg.Condition as InjectiveOrderCondition ?? new(),
			replaceMsg.TillDate, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		ValidatePortfolio(cancelMsg.PortfolioName);
		EnsureTradingReady();
		var market = cancelMsg.SecurityId.SecurityCode.IsEmpty() &&
			cancelMsg.SecurityId.Native is not string
			? null : GetMarket(cancelMsg.SecurityId);
		var order = await ResolveOrderAsync(cancelMsg.OrderStringId,
			cancelMsg.OrderId, market, cancellationToken);
		await CancelKnownOrderAsync(order, cancelMsg.TransactionId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		ValidatePortfolio(cancelMsg.PortfolioName);
		EnsureTradingReady();
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"Injective bulk cancellation does not close positions.");
		var market = cancelMsg.SecurityId.SecurityCode.IsEmpty() &&
			cancelMsg.SecurityId.Native is not string
			? null : GetMarket(cancelMsg.SecurityId);
		var kinds = market is null
			? new[]
			{
				InjectiveMarketKinds.Spot,
				InjectiveMarketKinds.Derivative,
			}
			: [market.Kind];
		foreach (var kind in kinds)
		{
			var orders = await RestClient.GetOrdersAsync(kind,
				_subaccountId, market?.MarketId, false, null, null, HistoryLimit,
				cancellationToken);
			foreach (var order in orders.Where(item => item is not null &&
				IsOrderActive(item) &&
				(cancelMsg.Side is null ||
					GetOrderSide(item) == cancelMsg.Side) &&
				(cancelMsg.IsStop is null ||
					item.ToStockSharpOrderType() == OrderTypes.Conditional ==
					cancelMsg.IsStop.Value)))
				await CancelKnownOrderAsync(order, cancelMsg.TransactionId,
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
				_portfolioSubscriptions.Remove(lookupMsg.OriginalTransactionId);
			return;
		}
		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = PortfolioName,
			BoardCode = BoardCodes.Injective,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		var snapshot = await RestClient.GetPortfolioAsync(Signer.WalletAddress,
			cancellationToken);
		await SendPortfolioSnapshotAsync(snapshot?.Portfolio,
			lookupMsg.TransactionId, cancellationToken);
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
		EnsureAccountReady();
		ValidatePortfolio(statusMsg.PortfolioName);
		if (!statusMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_orderSubscriptions.Remove(statusMsg.OriginalTransactionId);
			return;
		}
		if (statusMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}
		var subscription = CreateOrderSubscription(statusMsg);
		var orders = await GetOrderSnapshotAsync(subscription, cancellationToken);
		foreach (var order in orders)
			await SendOrderAsync(order, statusMsg.TransactionId,
				cancellationToken);
		await SendHistoricalExecutionsAsync(subscription,
			statusMsg.TransactionId, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_orderSubscriptions.Add(statusMsg.TransactionId, subscription);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private async ValueTask PlaceOrderAsync(InjectiveMarket market,
		long transactionId, Sides side, decimal volume, decimal price,
		OrderTypes orderType, TimeInForce? timeInForce, bool isPostOnly,
		OrderPositionEffects? positionEffect, InjectiveOrderCondition condition,
		DateTime? tillDate, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		ArgumentNullException.ThrowIfNull(condition);
		if (!IsMarketActive(market.Status))
			throw new InvalidOperationException(
				$"Injective market '{market.Code}' is not active.");
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market or
			OrderTypes.Conditional))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		if (volume <= 0 || market.VolumeStep <= 0 ||
			volume % market.VolumeStep != 0)
			throw new ArgumentOutOfRangeException(nameof(volume), volume,
				$"Injective order volume must be a positive multiple of " +
				$"{market.VolumeStep}.");
		var isMarket = orderType == OrderTypes.Market ||
			orderType == OrderTypes.Conditional && price <= 0;
		if (!isMarket && (price <= 0 || market.PriceStep <= 0 ||
			price % market.PriceStep != 0))
			throw new ArgumentOutOfRangeException(nameof(price), price,
				$"Injective order price must be a positive multiple of " +
				$"{market.PriceStep}.");
		if (isPostOnly && isMarket)
			throw new InvalidOperationException(
				"An Injective market order cannot be post-only.");
		if (!isMarket && timeInForce is TimeInForce.CancelBalance or
			TimeInForce.MatchOrCancel)
			throw new NotSupportedException(
				"Injective v2 limit orders support only good-till-block and " +
				"post-only execution.");
		if (isMarket && timeInForce == TimeInForce.PutInQueue)
			throw new NotSupportedException(
				"An Injective market order cannot remain in the order book.");

		var trigger = condition.TriggerPrice;
		if (orderType == OrderTypes.Conditional && trigger is not > 0)
			throw new InvalidOperationException(
				"An Injective conditional order requires a trigger price.");
		if (trigger is decimal triggerPrice &&
			(triggerPrice <= 0 || triggerPrice % market.PriceStep != 0))
			throw new ArgumentOutOfRangeException(nameof(condition.TriggerPrice),
				triggerPrice, "Injective trigger price must be a positive " +
				$"multiple of {market.PriceStep}.");
		var isReduceOnly = condition.IsReduceOnly ||
			positionEffect == OrderPositionEffects.CloseOnly;
		if (market.Kind == InjectiveMarketKinds.Spot && isReduceOnly)
			throw new InvalidOperationException(
				"Injective spot orders cannot be reduce-only.");
		if (isMarket)
			price = await ResolveProtectionPriceAsync(market, side,
				cancellationToken);
		if (market.MinimumNotional > 0 && volume * price < market.MinimumNotional)
			throw new ArgumentOutOfRangeException(nameof(volume), volume,
				$"Injective order notional must be at least " +
				$"{market.MinimumNotional} {market.QuoteSymbol}.");
		var leverage = condition.Leverage ??
			(market.InitialMarginRatio > 0
				? 1m / market.InitialMarginRatio : 1m);
		if (leverage <= 0 || leverage > 1000)
			throw new ArgumentOutOfRangeException(nameof(condition.Leverage),
				leverage, "Injective leverage must be above zero and at most 1000.");
		var expirationBlock = GetExpirationBlock(tillDate);
		var request = new InjectivePlaceOrder
		{
			Market = market,
			Side = side,
			IsMarket = isMarket,
			IsPostOnly = isPostOnly,
			IsReduceOnly = isReduceOnly,
			IsTakeProfit = condition.IsTakeProfit,
			Price = price,
			Quantity = volume,
			Margin = market.Kind == InjectiveMarketKinds.Derivative &&
				!isReduceOnly ? volume * price / leverage : 0m,
			TriggerPrice = trigger,
			ClientId = transactionId.ToString(CultureInfo.InvariantCulture),
			ExpirationBlock = expirationBlock,
		};
		string transactionHash;
		await _transactionSync.WaitAsync(cancellationToken);
		try
		{
			var account = await GetSigningAccountAsync(cancellationToken);
			var transaction = Signer.SignPlaceOrder(request,
				Environment.ChainId(), account.AccountNumber, account.Sequence,
				checked((ulong)GasLimit), FeeAmount, FeeDenom);
			var response = await BroadcastAsync(transaction, cancellationToken);
			AdvanceSequence();
			transactionHash = response.TxHash;
		}
		catch
		{
			InvalidateSequence();
			throw;
		}
		finally
		{
			_transactionSync.Release();
		}
		await SendPendingOrderAsync(request, transactionHash, transactionId,
			cancellationToken);
	}

	private async ValueTask CancelKnownOrderAsync(InjectiveOrder order,
		long transactionId, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(order);
		var market = GetMarket(order.MarketId) ?? throw new InvalidOperationException(
			$"Unknown Injective order market '{order.MarketId}'.");
		await _transactionSync.WaitAsync(cancellationToken);
		try
		{
			var account = await GetSigningAccountAsync(cancellationToken);
			var transaction = Signer.SignCancelOrder(new InjectiveCancelOrder
			{
				Market = market,
				OrderHash = order.OrderHash,
				ClientId = order.OrderHash.IsEmpty() ? order.Cid : null,
				OrderMask = 1,
			}, Environment.ChainId(), account.AccountNumber, account.Sequence,
				checked((ulong)GasLimit), FeeAmount, FeeDenom);
			await BroadcastAsync(transaction, cancellationToken);
			AdvanceSequence();
		}
		catch
		{
			InvalidateSequence();
			throw;
		}
		finally
		{
			_transactionSync.Release();
		}
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToInjectiveSecurityId(),
			ServerTime = ServerTime,
			PortfolioName = PortfolioName,
			DepoName = _subaccountId,
			OrderStringId = order.OrderHash,
			OrderState = OrderStates.Done,
			TransactionId = transactionId,
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}

	private async ValueTask<InjectiveTransactionResponse> BroadcastAsync(
		byte[] transaction, CancellationToken cancellationToken)
	{
		var response = (await RestClient.BroadcastAsync(transaction,
			cancellationToken))?.TransactionResponse ??
			throw new InvalidDataException(
				"Injective returned no transaction response.");
		if (response.Code != 0)
			throw new InvalidOperationException(
				$"Injective rejected transaction {response.TxHash}: " +
				(response.RawLog ?? response.Codespace ??
					$"code {response.Code}"));
		if (response.TxHash.IsEmpty())
			throw new InvalidDataException(
				"Injective returned no transaction hash.");
		if (DateTime.TryParse(response.Timestamp, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var time))
			UpdateServerTime(time);
		return response;
	}

	private async ValueTask<(ulong AccountNumber, ulong Sequence)>
		GetSigningAccountAsync(CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
			if (_accountNumber is ulong accountNumber &&
				_nextSequence is ulong sequence)
				return (accountNumber, sequence);
		var response = await RestClient.GetAccountAsync(Signer.WalletAddress,
			cancellationToken);
		var account = response?.Account?.BaseAccount ??
			throw new InvalidDataException(
				"Injective returned no Ethereum base account.");
		if (!ulong.TryParse(account.AccountNumber, NumberStyles.None,
			CultureInfo.InvariantCulture, out var parsedAccount) ||
			!ulong.TryParse(account.Sequence, NumberStyles.None,
				CultureInfo.InvariantCulture, out var parsedSequence))
			throw new InvalidDataException(
				"Injective returned invalid account number or sequence.");
		using (_sync.EnterScope())
		{
			_accountNumber = parsedAccount;
			_nextSequence = parsedSequence;
		}
		return (parsedAccount, parsedSequence);
	}

	private void AdvanceSequence()
	{
		using (_sync.EnterScope())
			if (_nextSequence is ulong sequence)
				_nextSequence = checked(sequence + 1);
	}

	private void InvalidateSequence()
	{
		using (_sync.EnterScope())
		{
			_accountNumber = null;
			_nextSequence = null;
		}
	}

	private async ValueTask<decimal> ResolveProtectionPriceAsync(
		InjectiveMarket market, Sides side, CancellationToken cancellationToken)
	{
		var snapshot = await RestClient.GetOrderBookAsync(market, 1,
			cancellationToken);
		var level = side == Sides.Buy
			? snapshot?.Orderbook?.Sells?.FirstOrDefault()
			: snapshot?.Orderbook?.Buys?.FirstOrDefault();
		var price = level is null
			? GetLastPrice(market.MarketId)
			: market.ToPrice(level.Price);
		if (price is not > 0)
			throw new InvalidOperationException(
				$"No current Injective price is available for '{market.Code}'.");
		var multiplier = side == Sides.Buy
			? 1m + MarketOrderSlippage / 100m
			: 1m - MarketOrderSlippage / 100m;
		var adjusted = price.Value * multiplier;
		var steps = side == Sides.Buy
			? decimal.Ceiling(adjusted / market.PriceStep)
			: decimal.Floor(adjusted / market.PriceStep);
		var protectionPrice = steps * market.PriceStep;
		if (protectionPrice <= 0)
			throw new InvalidOperationException(
				$"Injective protection price for '{market.Code}' is zero.");
		return protectionPrice;
	}

	private long GetExpirationBlock(DateTime? tillDate)
	{
		var current = CurrentHeight;
		if (current <= 0)
			throw new InvalidOperationException(
				"Injective current block height is unavailable.");
		if (tillDate is not DateTime requested)
			return checked(current + BlockLifetime);
		var expiry = requested.ToUniversalTime();
		var seconds = (expiry - ServerTime).TotalSeconds;
		if (seconds < 2)
			throw new ArgumentOutOfRangeException(nameof(tillDate), tillDate,
				"Injective order expiry must be at least two seconds in the future.");
		var blocks = checked((long)Math.Ceiling(seconds /
			_estimatedBlockInterval.TotalSeconds));
		if (blocks > 100_000)
			throw new ArgumentOutOfRangeException(nameof(tillDate), tillDate,
				"Injective order expiry cannot exceed 100000 estimated blocks.");
		return checked(current + blocks);
	}

	private async ValueTask SendPendingOrderAsync(InjectivePlaceOrder order,
		string transactionHash, long transactionId,
		CancellationToken cancellationToken)
	{
		var condition = new InjectiveOrderCondition
		{
			TriggerPrice = order.TriggerPrice,
			IsTakeProfit = order.IsTakeProfit,
			IsReduceOnly = order.IsReduceOnly,
			Leverage = order.Market.Kind == InjectiveMarketKinds.Derivative &&
				!order.IsReduceOnly && order.Margin > 0
				? order.Quantity * order.Price / order.Margin : null,
		};
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Market.ToInjectiveSecurityId(),
			ServerTime = ServerTime,
			PortfolioName = PortfolioName,
			DepoName = _subaccountId,
			Side = order.Side,
			OrderVolume = order.Quantity,
			Balance = order.Quantity,
			OrderPrice = order.Price,
			OrderType = order.TriggerPrice is not null
				? OrderTypes.Conditional
				: order.IsMarket ? OrderTypes.Market : OrderTypes.Limit,
			OrderState = OrderStates.Pending,
			OrderBoardId = transactionHash,
			TransactionId = transactionId,
			OriginalTransactionId = transactionId,
			TimeInForce = order.IsMarket
				? TimeInForce.CancelBalance : TimeInForce.PutInQueue,
			PositionEffect = order.IsReduceOnly
				? OrderPositionEffects.CloseOnly : null,
			Condition = condition,
		}, cancellationToken);
	}

	private OrderSubscription CreateOrderSubscription(OrderStatusMessage message)
	{
		var market = message.SecurityId.SecurityCode.IsEmpty() &&
			message.SecurityId.Native is not string
			? null : GetMarket(message.SecurityId);
		return new()
		{
			Market = market,
			OrderId = !message.OrderStringId.IsEmpty()
				? message.OrderStringId.Trim()
				: message.OrderId?.ToString(CultureInfo.InvariantCulture),
			Side = message.Side,
			States = message.States ?? [],
			From = message.From?.ToUniversalTime(),
			To = message.To?.ToUniversalTime(),
			Skip = Math.Max(0, message.Skip ?? 0).To<int>(),
			Limit = (message.Count ?? HistoryLimit).Min(HistoryLimit).Max(1)
				.To<int>(),
		};
	}

	private async ValueTask<InjectiveOrder[]> GetOrderSnapshotAsync(
		OrderSubscription subscription, CancellationToken cancellationToken)
	{
		var markets = subscription.Market is null
			? GetMarkets() : [subscription.Market];
		var result = new Dictionary<string, InjectiveOrder>(
			StringComparer.OrdinalIgnoreCase);
		foreach (var group in markets.GroupBy(static market => market.Kind))
		{
			var marketId = subscription.Market?.MarketId;
			var activeTask = RestClient.GetOrdersAsync(group.Key, _subaccountId,
				marketId, false, subscription.From, subscription.To,
				subscription.Limit, cancellationToken).AsTask();
			var historyTask = RestClient.GetOrdersAsync(group.Key, _subaccountId,
				marketId, true, subscription.From, subscription.To,
				subscription.Limit, cancellationToken).AsTask();
			await Task.WhenAll(activeTask, historyTask);
			foreach (var order in (await activeTask).Concat(await historyTask)
				.Where(item => item is not null && IsOrderMatch(item, subscription)))
			{
				var key = order.OrderHash.IsEmpty() ? order.Cid : order.OrderHash;
				if (!key.IsEmpty())
					result[key] = order;
			}
		}
		return [.. result.Values.OrderBy(static order => order.UpdatedAt)
			.Skip(subscription.Skip).Take(subscription.Limit)];
	}

	private async ValueTask SendHistoricalExecutionsAsync(
		OrderSubscription subscription,
		long transactionId, CancellationToken cancellationToken)
	{
		if (subscription.Market is InjectiveMarket selectedMarket)
		{
			var trades = await RestClient.GetTradesAsync(selectedMarket,
				_subaccountId, subscription.From, subscription.To,
				subscription.Limit, cancellationToken);
			await SendHistoricalExecutionsAsync(trades, selectedMarket,
				transactionId, cancellationToken);
			return;
		}
		foreach (var kind in new[]
		{
			InjectiveMarketKinds.Spot,
			InjectiveMarketKinds.Derivative,
		})
		{
			var trades = await RestClient.GetAccountTradesAsync(kind,
				_subaccountId,
				subscription.From, subscription.To, subscription.Limit,
				cancellationToken);
			await SendHistoricalExecutionsAsync(trades, null, transactionId,
				cancellationToken);
		}
	}

	private async ValueTask SendHistoricalExecutionsAsync(
		IEnumerable<InjectiveTrade> trades, InjectiveMarket selectedMarket,
		long transactionId, CancellationToken cancellationToken)
	{
		foreach (var trade in trades.Where(static item => item is not null)
			.OrderBy(static item => item.ExecutedAt))
		{
			var market = selectedMarket ?? GetMarket(trade.MarketId);
			if (market is null)
				continue;
			var key = "history:" + transactionId.ToString(
				CultureInfo.InvariantCulture) + ':' + trade.TradeId;
			using (_sync.EnterScope())
				if (!_seenTrades.Add(key))
					continue;
			await SendTradeAsync(market, trade, transactionId, true,
				cancellationToken);
		}
	}

	private async ValueTask<InjectiveOrder> ResolveOrderAsync(string stringId,
		long? numericId, InjectiveMarket market,
		CancellationToken cancellationToken)
	{
		var id = !stringId.IsEmpty() ? stringId.Trim() :
			numericId?.ToString(CultureInfo.InvariantCulture);
		id = id.ThrowIfEmpty("order ID");
		using (_sync.EnterScope())
			if (_knownOrders.TryGetValue(id, out var known) &&
				(market is null || known.MarketId.Equals(market.MarketId,
					StringComparison.OrdinalIgnoreCase)))
				return known;
		var probe = new OrderSubscription
		{
			Market = market,
			OrderId = id,
			Limit = HistoryLimit,
			States = [],
		};
		return (await GetOrderSnapshotAsync(probe, cancellationToken))
			.FirstOrDefault() ?? throw new InvalidOperationException(
				$"Injective order '{id}' was not found.");
	}

	private async ValueTask SendPortfolioSnapshotAsync(
		InjectivePortfolio portfolio, long transactionId,
		CancellationToken cancellationToken)
	{
		if (portfolio is null)
			throw new InvalidDataException(
				"Injective returned no portfolio snapshot.");
		var usdValues = (portfolio.BankBalances ?? [])
			.Where(static balance => balance?.UsdValue.IsEmpty() == false)
			.Select(static balance => balance.UsdValue.ParseInjectiveDecimal(
				"bank USD value"))
			.Concat((portfolio.Subaccounts ?? [])
				.Where(static balance =>
					balance?.Deposit?.TotalBalanceUsd.IsEmpty() == false)
				.Select(static balance =>
					balance.Deposit.TotalBalanceUsd.ParseInjectiveDecimal(
						"subaccount USD value"))).ToArray();
		if (usdValues.Length > 0)
			await SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = PortfolioName,
			SecurityId = SecurityId.Money,
			DepoName = _subaccountId,
			ServerTime = ServerTime,
			OriginalTransactionId = transactionId,
			}.TryAdd(PositionChangeTypes.CurrentValue, usdValues.Sum(), true),
				cancellationToken);
		foreach (var balance in portfolio.BankBalances ?? [])
			await SendBalanceAsync(balance?.Denom, balance?.Amount,
				transactionId, cancellationToken);
		foreach (var balance in (portfolio.Subaccounts ?? []).Where(item =>
			item?.SubaccountId.Equals(_subaccountId,
				StringComparison.OrdinalIgnoreCase) == true))
			await SendBalanceAsync(balance.Denom,
				balance.Deposit?.TotalBalance, transactionId, cancellationToken,
				balance.Deposit?.AvailableBalance);
		foreach (var item in portfolio.PositionsWithUpnl ?? [])
			if (item?.Position is not null)
				await SendPositionAsync(item.Position, transactionId,
					cancellationToken, item.UnrealizedPnl);
	}

	private async ValueTask SendBalanceAsync(string denom, string amount,
		long transactionId, CancellationToken cancellationToken,
		string available = null)
	{
		if (denom.IsEmpty() || amount.IsEmpty())
			return;
		InjectiveTokenMeta token;
		using (_sync.EnterScope())
			_tokensByDenom.TryGetValue(denom, out token);
		var decimals = token?.Decimals ?? (denom == "inj" ? 18 : 0);
		var divisor = InjectiveExtensions.Pow10(decimals);
		var current = amount.ParseInjectiveDecimal("portfolio balance") / divisor;
		decimal? availableValue = available.IsEmpty() ? null :
			available.ParseInjectiveDecimal("available balance") / divisor;
		await SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = PortfolioName,
			SecurityId = new()
			{
				SecurityCode = token?.Symbol ?? denom,
				BoardCode = BoardCodes.Injective,
			},
			DepoName = _subaccountId,
			ServerTime = ServerTime,
			OriginalTransactionId = transactionId,
		}.TryAdd(PositionChangeTypes.CurrentValue, current, true)
		.TryAdd(PositionChangeTypes.BlockedValue,
			availableValue is decimal free ? (current - free).Max(0m) : null,
			true), cancellationToken);
	}

	private ValueTask SendPositionAsync(InjectivePosition position,
		long transactionId, CancellationToken cancellationToken,
		string unrealizedPnl = null)
	{
		var market = GetMarket(position?.MarketId);
		if (market is null)
			return default;
		var time = position.UpdatedAt > 0
			? position.UpdatedAt.FromInjectiveMilliseconds() : ServerTime;
		var quantity = market.ToQuantity(position.Quantity);
		var side = position.Direction.ToStockSharpSide();
		var upnl = unrealizedPnl.IsEmpty() ? position.Upnl : unrealizedPnl;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = PortfolioName,
			SecurityId = market.ToInjectiveSecurityId(),
			DepoName = _subaccountId,
			ServerTime = time,
			OriginalTransactionId = transactionId,
			Side = side,
		}.TryAdd(PositionChangeTypes.CurrentValue, quantity.Abs(), true)
		.TryAdd(PositionChangeTypes.AveragePrice,
			market.ToPrice(position.EntryPrice), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL,
			upnl.IsEmpty() ? null : market.ToQuote(upnl), true)
		.TryAdd(PositionChangeTypes.LiquidationPrice,
			position.LiquidationPrice.IsEmpty() ? null :
				market.ToPrice(position.LiquidationPrice), true), cancellationToken);
	}

	private async ValueTask SendOrderAsync(InjectiveOrder order,
		long transactionId, CancellationToken cancellationToken)
	{
		var market = GetMarket(order?.MarketId);
		if (market is null)
			return;
		RememberOrder(order);
		var volume = market.ToQuantity(order.Quantity);
		var balance = !order.UnfilledQuantity.IsEmpty()
			? market.ToQuantity(order.UnfilledQuantity)
			: (volume - (!order.FilledQuantity.IsEmpty()
				? market.ToQuantity(order.FilledQuantity) : 0m)).Max(0m);
		var timeValue = order.UpdatedAt > 0 ? order.UpdatedAt : order.CreatedAt;
		var condition = new InjectiveOrderCondition
		{
			TriggerPrice = order.TriggerPrice.IsEmpty() ? null :
				market.ToPrice(order.TriggerPrice),
			IsTakeProfit = order.OrderType?.Contains("take",
				StringComparison.OrdinalIgnoreCase) == true,
			IsReduceOnly = order.IsReduceOnly,
		};
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToInjectiveSecurityId(),
			ServerTime = timeValue > 0
				? timeValue.FromInjectiveMilliseconds() : ServerTime,
			PortfolioName = PortfolioName,
			DepoName = _subaccountId,
			Side = GetOrderSide(order),
			OrderVolume = volume,
			Balance = balance,
			OrderPrice = market.ToPrice(order.Price),
			OrderType = order.ToStockSharpOrderType(),
			OrderState = order.State.ToStockSharpOrderState(order.IsActive),
			OrderStringId = order.OrderHash,
			TransactionId = ParseTransactionId(order.Cid),
			OriginalTransactionId = transactionId,
			PositionEffect = order.IsReduceOnly
				? OrderPositionEffects.CloseOnly : null,
			Condition = condition,
		}, cancellationToken);
	}

	private async ValueTask OnOrderAsync(InjectiveOrderUpdate update,
		CancellationToken cancellationToken)
	{
		var order = update?.Order;
		if (order?.MarketId.IsEmpty() != false)
			return;
		RememberOrder(order);
		KeyValuePair<long, OrderSubscription>[] keyed;
		using (_sync.EnterScope())
			keyed = [.. _orderSubscriptions];
		foreach (var item in keyed.Where(item => IsOrderMatch(order, item.Value)))
			await SendOrderAsync(order, item.Key, cancellationToken);
	}

	private async ValueTask OnPositionAsync(InjectivePosition position,
		CancellationToken cancellationToken)
	{
		long[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _portfolioSubscriptions];
		foreach (var transactionId in subscriptions)
			await SendPositionAsync(position, transactionId, cancellationToken);
	}

	private async ValueTask OnPortfolioUpdateAsync(
		InjectivePortfolioUpdate update, CancellationToken cancellationToken)
	{
		if (update?.Denom.IsEmpty() != false || update.Amount.IsEmpty())
			return;
		long[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _portfolioSubscriptions];
		foreach (var transactionId in subscriptions)
			await SendBalanceAsync(update.Denom, update.Amount, transactionId,
				cancellationToken);
	}

	private void RememberOrder(InjectiveOrder order)
	{
		if (order is null)
			return;
		using (_sync.EnterScope())
		{
			if (!order.OrderHash.IsEmpty())
				_knownOrders[order.OrderHash] = order;
			if (!order.Cid.IsEmpty())
				_knownOrders[order.Cid] = order;
		}
	}

	private static bool IsOrderMatch(InjectiveOrder order,
		OrderSubscription subscription)
	{
		if (order is null || subscription is null)
			return false;
		if (subscription.Market is not null &&
			!order.MarketId.Equals(subscription.Market.MarketId,
				StringComparison.OrdinalIgnoreCase))
			return false;
		if (!subscription.OrderId.IsEmpty() &&
			!subscription.OrderId.Equals(order.OrderHash,
				StringComparison.OrdinalIgnoreCase) &&
			!subscription.OrderId.Equals(order.Cid,
				StringComparison.OrdinalIgnoreCase))
			return false;
		if (subscription.Side is Sides side && GetOrderSide(order) != side)
			return false;
		var state = order.State.ToStockSharpOrderState(order.IsActive);
		if (subscription.States is { Length: > 0 } &&
			!subscription.States.Contains(state))
			return false;
		var timestamp = order.UpdatedAt > 0 ? order.UpdatedAt : order.CreatedAt;
		if (timestamp > 0)
		{
			var time = timestamp.FromInjectiveMilliseconds();
			if (subscription.From is DateTime from && time < from)
				return false;
			if (subscription.To is DateTime to && time > to)
				return false;
		}
		return true;
	}

	private static bool IsOrderActive(InjectiveOrder order)
		=> order.State.ToStockSharpOrderState(order.IsActive) ==
			OrderStates.Active;

	private static Sides GetOrderSide(InjectiveOrder order)
		=> (order.OrderSide ?? order.Direction).ToStockSharpSide();
}
