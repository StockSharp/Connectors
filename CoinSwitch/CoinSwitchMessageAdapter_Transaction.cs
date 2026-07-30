namespace StockSharp.CoinSwitch;

public partial class CoinSwitchMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var market = GetMarket(regMsg.SecurityId);
		if (regMsg.Volume <= 0)
			throw new ArgumentOutOfRangeException(
				nameof(regMsg.Volume),
				regMsg.Volume,
				"CoinSwitch order volume must be positive.");
		if (regMsg.VisibleVolume is > 0 &&
			regMsg.VisibleVolume != regMsg.Volume)
			throw new NotSupportedException(
				"CoinSwitch API does not document iceberg orders.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException(
				"CoinSwitch API does not document GTD orders.");

		string orderId;
		switch (ProductType)
		{
			case CoinSwitchProductTypes.Spot:
				orderId = await RegisterSpotOrderAsync(
					regMsg, market, cancellationToken);
				break;

			case CoinSwitchProductTypes.Futures:
				orderId = await RegisterFuturesOrderAsync(
					regMsg, market, cancellationToken);
				break;

			case CoinSwitchProductTypes.Options:
				orderId = await RegisterHftOrderAsync(
					regMsg, market, cancellationToken);
				break;

			default:
				throw new ArgumentOutOfRangeException(
					nameof(ProductType),
					ProductType,
					LocalizedStrings.InvalidValue);
		}

		TrackOrder(
			orderId,
			market.NativeSymbol,
			regMsg.TransactionId);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToSecurityId(),
			ServerTime = CurrentTime,
			TransactionId = regMsg.TransactionId,
			OriginalTransactionId = regMsg.TransactionId,
			OrderStringId = orderId,
			OrderType = regMsg.OrderType ?? OrderTypes.Limit,
			OrderPrice = regMsg.Price,
			OrderVolume = regMsg.Volume,
			Balance = regMsg.Volume,
			Side = regMsg.Side,
			TimeInForce = regMsg.TimeInForce,
			PortfolioName = GetPortfolioName(),
			OrderState = OrderStates.Active,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var orderId = ResolveOrderId(
			cancelMsg.OrderId, cancelMsg.OrderStringId);
		var market = cancelMsg.SecurityId == default
			? GetMarket(GetOrderSymbol(orderId)) ??
				throw new InvalidOperationException(
					"CoinSwitch cancellation requires a security ID.")
			: GetMarket(cancelMsg.SecurityId);

		switch (ProductType)
		{
			case CoinSwitchProductTypes.Spot:
				await RestClient.CancelSpotOrderAsync(
					orderId, cancellationToken);
				break;

			case CoinSwitchProductTypes.Futures:
				await RestClient.CancelFuturesOrderAsync(
					orderId, cancellationToken);
				break;

			case CoinSwitchProductTypes.Options:
				await RestClient.CancelHftOrderAsync(
					market.NativeSymbol,
					orderId,
					cancellationToken);
				break;

			default:
				throw new ArgumentOutOfRangeException(
					nameof(ProductType),
					ProductType,
					LocalizedStrings.InvalidValue);
		}
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		_ = replaceMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"CoinSwitch API does not provide a documented atomic " +
				"order-replace operation.");
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		if (cancelMsg.Mode.HasFlag(
			OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"CoinSwitch bulk cancellation cannot close positions.");
		var market = cancelMsg.SecurityId == default
			? null
			: GetMarket(cancelMsg.SecurityId);

		switch (ProductType)
		{
			case CoinSwitchProductTypes.Spot:
				foreach (var order in
					await RestClient.GetSpotOrdersAsync(
						true,
						market?.NativeSymbol,
						500,
						null,
						null,
						cancellationToken))
				{
					if (order.OrderId.IsEmpty() ||
						(cancelMsg.Side is not null &&
							order.Side.ToSide() != cancelMsg.Side))
						continue;
					await RestClient.CancelSpotOrderAsync(
						order.OrderId, cancellationToken);
				}
				break;

			case CoinSwitchProductTypes.Futures:
				foreach (var order in
					await RestClient.GetFuturesOrdersAsync(
						true,
						market?.NativeSymbol,
						500,
						null,
						null,
						cancellationToken))
				{
					if (order.OrderId.IsEmpty() ||
						(cancelMsg.Side is not null &&
							order.Side.ToSide() != cancelMsg.Side))
						continue;
					await RestClient.CancelFuturesOrderAsync(
						order.OrderId, cancellationToken);
				}
				break;

			case CoinSwitchProductTypes.Options:
				foreach (var order in
					await RestClient.GetHftOrdersAsync(
						market?.NativeSymbol,
						null,
						500,
						cancellationToken))
				{
					if (order.OrderId.IsEmpty() ||
						(cancelMsg.Side is not null &&
							order.Side.ToSide() != cancelMsg.Side))
						continue;
					var orderMarket =
						GetMarket(order.Symbol) ?? market;
					if (orderMarket is null)
						continue;
					await RestClient.CancelHftOrderAsync(
						orderMarket.NativeSymbol,
						order.OrderId,
						cancellationToken);
				}
				break;

			default:
				throw new ArgumentOutOfRangeException(
					nameof(ProductType),
					ProductType,
					LocalizedStrings.InvalidValue);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			lookupMsg.TransactionId,
			cancellationToken);

		EnsurePrivateReady();
		var portfolioName = GetPortfolioName();
		if (!lookupMsg.PortfolioName.IsEmpty() &&
			!lookupMsg.PortfolioName.EqualsIgnoreCase(portfolioName))
		{
			await SendSubscriptionResultAsync(
				lookupMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(
				lookupMsg.TransactionId, cancellationToken);
			return;
		}
		if (!lookupMsg.IsSubscribe)
		{
			if (_portfolioSubscriptionId ==
				lookupMsg.OriginalTransactionId)
				_portfolioSubscriptionId = 0;
			return;
		}

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = portfolioName,
			BoardCode = BoardCodes.CoinSwitch,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await SendPortfolioSnapshotAsync(
			lookupMsg.TransactionId, cancellationToken);

		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(
				lookupMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(
				lookupMsg.TransactionId, cancellationToken);
			return;
		}

		if (_portfolioSubscriptionId != 0)
			throw new InvalidOperationException(
				"CoinSwitch portfolio subscription already exists.");
		_portfolioSubscriptionId = lookupMsg.TransactionId;
		await SendSubscriptionResultAsync(
			lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			statusMsg.TransactionId,
			cancellationToken);

		EnsurePrivateReady();
		if (!statusMsg.IsSubscribe)
		{
			if (_orderStatusSubscriptionId ==
				statusMsg.OriginalTransactionId)
				_orderStatusSubscriptionId = 0;
			return;
		}
		if (statusMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(
				statusMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(
				statusMsg.TransactionId, cancellationToken);
			return;
		}

		await SendOrderSnapshotAsync(
			statusMsg,
			statusMsg.TransactionId,
			cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(
				statusMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(
				statusMsg.TransactionId, cancellationToken);
			return;
		}

		if (_orderStatusSubscriptionId != 0)
			throw new InvalidOperationException(
				"CoinSwitch order-status subscription already exists.");
		_orderStatusSubscriptionId = statusMsg.TransactionId;
		await SendSubscriptionResultAsync(
			statusMsg, cancellationToken);
	}

	private async ValueTask<string> RegisterSpotOrderAsync(
		OrderRegisterMessage message,
		CoinSwitchMarket market,
		CancellationToken cancellationToken)
	{
		if (message.OrderType is not (null or OrderTypes.Limit))
			throw new NotSupportedException(
				"CoinSwitch PRO spot order entry documents only " +
					"limit orders.");
		if (message.Price <= 0)
			throw new ArgumentOutOfRangeException(
				nameof(message.Price),
				message.Price,
				"CoinSwitch spot limit price must be positive.");
		if (message.PostOnly == true)
			throw new NotSupportedException(
				"CoinSwitch spot API does not document post-only orders.");

		var order = await RestClient.PlaceSpotOrderAsync(new()
		{
			Side = message.Side.ToCoinSwitch(),
			Symbol = market.NativeSymbol,
			Type = "LIMIT",
			Price = message.Price,
			Quantity = message.Volume,
			Exchange = SpotExchange,
			ClientOrderId = message.UserOrderId.IsEmpty()
				? "ss-" + message.TransactionId.ToString(
					CultureInfo.InvariantCulture)
				: message.UserOrderId,
		}, cancellationToken);
		return order?.OrderId.ThrowIfEmpty(
			nameof(order.OrderId));
	}

	private async ValueTask<string> RegisterFuturesOrderAsync(
		OrderRegisterMessage message,
		CoinSwitchMarket market,
		CancellationToken cancellationToken)
	{
		var condition = message.Condition as CoinSwitchOrderCondition;
		string orderType;
		decimal? trigger = null;
		switch (message.OrderType)
		{
			case null:
			case OrderTypes.Limit:
				orderType = "LIMIT";
				if (message.Price <= 0)
					throw new ArgumentOutOfRangeException(
						nameof(message.Price),
						message.Price,
						"CoinSwitch futures limit price must be positive.");
				break;

			case OrderTypes.Market:
				orderType = "MARKET";
				break;

			case OrderTypes.Conditional:
				if (condition?.TriggerPrice is not > 0)
					throw new InvalidOperationException(
						"CoinSwitch futures trigger price must be positive.");
				orderType = "STOP_MARKET";
				trigger = condition.TriggerPrice;
				break;

			default:
				throw new NotSupportedException(
					LocalizedStrings.OrderUnsupportedType.Put(
						message.OrderType,
						message.TransactionId));
		}
		if (message.PostOnly == true)
			throw new NotSupportedException(
				"CoinSwitch futures API does not document post-only " +
					"order entry.");

		var order = await RestClient.PlaceFuturesOrderAsync(new()
		{
			Exchange = "EXCHANGE_2",
			Symbol = market.NativeSymbol,
			Side = message.Side.ToCoinSwitch(),
			OrderType = orderType,
			Price = orderType == "LIMIT" ? message.Price : null,
			Quantity = message.Volume,
			TriggerPrice = trigger,
			ReduceOnly = condition?.ReduceOnly,
		}, cancellationToken);
		return order?.OrderId.ThrowIfEmpty(
			nameof(order.OrderId));
	}

	private async ValueTask<string> RegisterHftOrderAsync(
		OrderRegisterMessage message,
		CoinSwitchMarket market,
		CancellationToken cancellationToken)
	{
		if (message.OrderType is not (
			null or OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				"CoinSwitch HFT options supports limit and market " +
					"orders through this connector.");
		var isMarket = message.OrderType == OrderTypes.Market;
		if (!isMarket && message.Price <= 0)
			throw new ArgumentOutOfRangeException(
				nameof(message.Price),
				message.Price,
				"CoinSwitch option limit price must be positive.");
		var condition = message.Condition as CoinSwitchOrderCondition;
		var result = await RestClient.PlaceHftOrderAsync(new()
		{
			Category = "option",
			Symbol = market.NativeSymbol,
			Side = message.Side == Sides.Buy ? "Buy" : "Sell",
			OrderType = isMarket ? "Market" : "Limit",
			Quantity = message.Volume.ToWire(),
			Price = isMarket ? null : message.Price.ToWire(),
			PositionIndex = 0,
			TimeInForce = message.TimeInForce switch
			{
				TimeInForce.MatchOrCancel => "IOC",
				TimeInForce.CancelBalance => "FOK",
				_ => message.PostOnly == true
					? "PostOnly"
					: "GTC",
			},
			ReduceOnly = condition?.ReduceOnly,
			OrderLinkId = message.UserOrderId.IsEmpty()
				? "ss-" + message.TransactionId.ToString(
					CultureInfo.InvariantCulture)
				: message.UserOrderId,
		}, cancellationToken);
		return result?.OrderId.ThrowIfEmpty(
			nameof(result.OrderId));
	}

	private async ValueTask SendPortfolioSnapshotAsync(
		long transactionId,
		CancellationToken cancellationToken)
	{
		switch (ProductType)
		{
			case CoinSwitchProductTypes.Spot:
				foreach (var balance in
					await RestClient.GetSpotBalancesAsync(
						cancellationToken))
					await SendBalanceAsync(
						balance.Currency ?? balance.Name,
						balance.Available,
						balance.Blocked,
						null,
						transactionId,
						cancellationToken);
				break;

			case CoinSwitchProductTypes.Futures:
				var balances =
					await RestClient.GetFuturesBalancesAsync(
						cancellationToken);
				foreach (var asset in
					balances?.BaseAssetBalances ?? [])
				{
					var balance = asset.Balance;
					if (balance is null)
						continue;
					await SendBalanceAsync(
						asset.Asset,
						balance.Available,
						balance.Blocked,
						null,
						transactionId,
						cancellationToken);
				}
				break;

			case CoinSwitchProductTypes.Options:
				foreach (var wallet in
					await RestClient.GetHftWalletsAsync(
						cancellationToken))
					foreach (var balance in wallet.Coins ?? [])
						await SendBalanceAsync(
							balance.Coin,
							balance.Available ??
								balance.WalletBalance ?? 0,
							balance.Blocked ?? 0,
							balance.UnrealizedPnL,
							transactionId,
							cancellationToken);
				break;

			default:
				throw new ArgumentOutOfRangeException(
					nameof(ProductType),
					ProductType,
					LocalizedStrings.InvalidValue);
		}
	}

	private ValueTask SendBalanceAsync(
		string currency,
		decimal current,
		decimal blocked,
		decimal? unrealizedPnL,
		long transactionId,
		CancellationToken cancellationToken)
	{
		if (currency.IsEmpty())
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(),
			SecurityId = new()
			{
				SecurityCode = currency.Trim().ToUpperInvariant(),
				BoardCode = BoardCodes.CoinSwitch,
			},
			ServerTime = CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(
			PositionChangeTypes.CurrentValue,
			current,
			true)
		.TryAdd(
			PositionChangeTypes.BlockedValue,
			blocked,
			true)
		.TryAdd(
			PositionChangeTypes.UnrealizedPnL,
			unrealizedPnL,
			true),
			cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(
		OrderStatusMessage message,
		long transactionId,
		CancellationToken cancellationToken)
	{
		var market = message.SecurityId == default
			? null
			: GetMarket(message.SecurityId);
		var orderId =
			message.OrderId is not null ||
			!message.OrderStringId.IsEmpty()
				? ResolveOrderId(
					message.OrderId, message.OrderStringId)
				: null;
		var limit = (message.Count ?? 100).Min(500)
			.Max(1).To<int>();

		switch (ProductType)
		{
			case CoinSwitchProductTypes.Spot:
				CoinSwitchSpotOrder[] spotOrders;
				if (!orderId.IsEmpty())
				spotOrders =
				[
					await RestClient.GetSpotOrderAsync(
						orderId, cancellationToken),
				];
				else
					spotOrders =
						await RestClient.GetSpotOrdersAsync(
							null,
							market?.NativeSymbol,
							limit,
							message.From,
							message.To,
							cancellationToken);
				foreach (var order in FilterSpotOrders(
					spotOrders,
					message,
					limit))
					await SendSpotOrderAsync(
						order,
						transactionId,
						cancellationToken);
				break;

			case CoinSwitchProductTypes.Futures:
				CoinSwitchFuturesOrder[] futuresOrders;
				if (!orderId.IsEmpty())
					futuresOrders =
					[
						await RestClient.GetFuturesOrderAsync(
							orderId, cancellationToken),
					];
				else
				{
					var open =
						await RestClient.GetFuturesOrdersAsync(
							true,
							market?.NativeSymbol,
							limit,
							message.From,
							message.To,
							cancellationToken);
					var closed =
						await RestClient.GetFuturesOrdersAsync(
							false,
							market?.NativeSymbol,
							limit,
							message.From,
							message.To,
							cancellationToken);
					futuresOrders = [.. open.Concat(closed)
						.Where(static order => order is not null)
						.GroupBy(
							static order => order.OrderId,
							StringComparer.Ordinal)
						.Select(static group => group
							.OrderByDescending(static order =>
								order.UpdatedTime)
							.First())];
				}
				foreach (var order in FilterFuturesOrders(
					futuresOrders,
					message,
					limit))
					await SendFuturesOrderAsync(
						order,
						transactionId,
						cancellationToken);
				break;

			case CoinSwitchProductTypes.Options:
				var optionOrders =
					await RestClient.GetHftOrdersAsync(
						market?.NativeSymbol,
						orderId,
						limit,
						cancellationToken);
				foreach (var order in FilterHftOrders(
					optionOrders,
					message,
					limit))
					await SendHftOrderAsync(
						order,
						transactionId,
						cancellationToken);
				break;

			default:
				throw new ArgumentOutOfRangeException(
					nameof(ProductType),
					ProductType,
					LocalizedStrings.InvalidValue);
		}
	}

	private async ValueTask SendSpotOrderAsync(
		CoinSwitchSpotOrder order,
		long transactionId,
		CancellationToken cancellationToken)
	{
		if (order?.OrderId.IsEmpty() != false)
			return;
		var market = GetMarket(order.Symbol) ??
			GetMarket(GetOrderSymbol(order.OrderId));
		if (market is null)
			return;
		TrackOrder(
			order.OrderId,
			market.NativeSymbol,
			GetOrderTransaction(order.OrderId));
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToSecurityId(),
			ServerTime = GetTimestamp(
				order.UpdatedTime, order.CreatedTime),
			TransactionId =
				GetOrderTransaction(order.OrderId),
			OriginalTransactionId = transactionId,
			OrderStringId = order.OrderId,
			UserOrderId = order.ClientOrderId,
			OrderType = OrderTypes.Limit,
			OrderPrice = order.Price,
			OrderVolume = order.OriginalQuantity,
			Balance = order.RemainingQuantity,
			Side = order.Side.ToSide(),
			PortfolioName = GetPortfolioName(),
			OrderState = order.Status.ToSpotOrderState(),
		}, cancellationToken);
	}

	private async ValueTask SendFuturesOrderAsync(
		CoinSwitchFuturesOrder order,
		long transactionId,
		CancellationToken cancellationToken)
	{
		if (order?.OrderId.IsEmpty() != false)
			return;
		var market = GetMarket(order.Symbol) ??
			GetMarket(GetOrderSymbol(order.OrderId));
		if (market is null)
			return;
		TrackOrder(
			order.OrderId,
			market.NativeSymbol,
			GetOrderTransaction(order.OrderId));
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToSecurityId(),
			ServerTime = GetTimestamp(
				order.UpdatedTime, order.CreatedTime),
			TransactionId =
				GetOrderTransaction(order.OrderId),
			OriginalTransactionId = transactionId,
			OrderStringId = order.OrderId,
			OrderType = order.OrderType.ToOrderType(),
			OrderPrice = order.Price,
			OrderVolume = order.Quantity,
			Balance = order.RemainingQuantity,
			Side = order.Side.ToSide(),
			PortfolioName = GetPortfolioName(),
			OrderState = order.Status.ToFuturesOrderState(),
			Commission = order.ExecutionFee,
		}, cancellationToken);
	}

	private async ValueTask SendHftOrderAsync(
		CoinSwitchHftOrder order,
		long transactionId,
		CancellationToken cancellationToken)
	{
		if (order?.OrderId.IsEmpty() != false)
			return;
		var market = GetMarket(order.Symbol) ??
			GetMarket(GetOrderSymbol(order.OrderId));
		if (market is null)
			return;
		var ownTransactionId =
			ParseTransactionId(order.OrderLinkId);
		if (ownTransactionId == 0)
			ownTransactionId =
				GetOrderTransaction(order.OrderId);
		TrackOrder(
			order.OrderId,
			market.NativeSymbol,
			ownTransactionId);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToSecurityId(),
			ServerTime = GetTimestamp(
				order.UpdatedTime, order.CreatedTime),
			TransactionId = ownTransactionId,
			OriginalTransactionId = transactionId,
			OrderStringId = order.OrderId,
			UserOrderId = order.OrderLinkId,
			OrderType = order.OrderType.ToOrderType(),
			OrderPrice = order.Price ?? order.AveragePrice ?? 0,
			OrderVolume = order.Quantity ?? 0,
			Balance = order.RemainingQuantity,
			Side = order.Side.ToSide(),
			TimeInForce = order.TimeInForce.ToTimeInForce(),
			PortfolioName = GetPortfolioName(),
			OrderState = order.Status.ToHftOrderState(),
			Commission = order.Commission,
		}, cancellationToken);
	}

	private async ValueTask PollSubscriptionsAsync(
		CancellationToken cancellationToken)
	{
		await PollMarketDataAsync(cancellationToken);
		if (_portfolioSubscriptionId != 0)
			await SendPortfolioSnapshotAsync(
				_portfolioSubscriptionId,
				cancellationToken);
		if (_orderStatusSubscriptionId != 0)
			await SendOrderSnapshotAsync(
				new OrderStatusMessage
				{
					IsSubscribe = true,
					Count = 500,
				},
				_orderStatusSubscriptionId,
				cancellationToken);
	}

	private static IEnumerable<CoinSwitchSpotOrder> FilterSpotOrders(
		IEnumerable<CoinSwitchSpotOrder> orders,
		OrderStatusMessage message,
		int limit)
		=> (orders ?? [])
			.Where(static order => order is not null)
			.Where(order =>
				message.Side is null ||
				order.Side.ToSide() == message.Side)
			.Where(order => IsInRange(
				GetTimestamp(
					order.UpdatedTime, order.CreatedTime),
				message.From,
				message.To))
			.OrderBy(static order => order.UpdatedTime)
			.TakeLast(limit);

	private static IEnumerable<CoinSwitchFuturesOrder>
		FilterFuturesOrders(
			IEnumerable<CoinSwitchFuturesOrder> orders,
			OrderStatusMessage message,
			int limit)
		=> (orders ?? [])
			.Where(static order => order is not null)
			.Where(order =>
				message.Side is null ||
				order.Side.ToSide() == message.Side)
			.Where(order => IsInRange(
				GetTimestamp(
					order.UpdatedTime, order.CreatedTime),
				message.From,
				message.To))
			.OrderBy(static order => order.UpdatedTime)
			.TakeLast(limit);

	private static IEnumerable<CoinSwitchHftOrder> FilterHftOrders(
		IEnumerable<CoinSwitchHftOrder> orders,
		OrderStatusMessage message,
		int limit)
		=> (orders ?? [])
			.Where(static order => order is not null)
			.Where(order =>
				message.Side is null ||
				order.Side.ToSide() == message.Side)
			.Where(order => IsInRange(
				GetTimestamp(
					order.UpdatedTime, order.CreatedTime),
				message.From,
				message.To))
			.OrderBy(static order => order.UpdatedTime)
			.TakeLast(limit);

	private static DateTime GetTimestamp(
		long primary,
		long fallback)
	{
		var timestamp = primary > 0 ? primary : fallback;
		return timestamp > 0
			? timestamp.FromCoinSwitchMilliseconds()
			: DateTime.UtcNow;
	}

	private static bool IsInRange(
		DateTime value,
		DateTime? from,
		DateTime? to)
		=> (from is null || value >= from.Value.ToUtc()) &&
			(to is null || value <= to.Value.ToUtc());

	private static long ParseTransactionId(string clientOrderId)
		=> clientOrderId?.StartsWith(
			"ss-",
			StringComparison.OrdinalIgnoreCase) == true &&
			long.TryParse(
				clientOrderId[3..],
				NumberStyles.None,
				CultureInfo.InvariantCulture,
				out var transactionId)
					? transactionId
					: 0;
}
