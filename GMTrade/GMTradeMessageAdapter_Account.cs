namespace StockSharp.GMTrade;

using Native;

public partial class GMTradeMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureWallet();
		if (!lookupMsg.IsSubscribe)
		{
			if (_portfolioSubscriptionId == lookupMsg.OriginalTransactionId)
				_portfolioSubscriptionId = 0;
			var streamId = _positionStreamId;
			_positionStreamId = null;
			if (!streamId.IsEmpty())
				await MarketSocket.UnsubscribeAsync(streamId, cancellationToken);
			return;
		}
		ValidatePortfolio(lookupMsg.PortfolioName);
		if (_portfolioSubscriptionId != 0)
			throw new InvalidOperationException(
				"Only one live GMTrade portfolio subscription is supported.");
		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = _portfolioName,
			BoardCode = BoardCodes.GMTrade,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId, true,
			cancellationToken);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId,
				cancellationToken);
			return;
		}
		_portfolioSubscriptionId = lookupMsg.TransactionId;
		_positionStreamId = await MarketSocket.SubscribePositionsAsync(
			WalletAddress, cancellationToken);
		_nextBalancePoll = DateTime.UtcNow + BalancePollingInterval;
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId,
			cancellationToken);
		EnsureWallet();
		if (!statusMsg.IsSubscribe)
		{
			if (_orderStatusSubscriptionId == statusMsg.OriginalTransactionId)
				_orderStatusSubscriptionId = 0;
			var streamId = _orderStreamId;
			_orderStreamId = null;
			if (!streamId.IsEmpty())
				await MarketSocket.UnsubscribeAsync(streamId, cancellationToken);
			return;
		}
		if (statusMsg.Count is <= 0)
		{
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}
		ValidatePortfolio(statusMsg.PortfolioName);
		if (_orderStatusSubscriptionId != 0)
			throw new InvalidOperationException(
				"Only one live GMTrade order-status subscription is supported.");
		await SendOrderSnapshotAsync(statusMsg, cancellationToken);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}
		_orderStatusSubscriptionId = statusMsg.TransactionId;
		_orderStreamId = await MarketSocket.SubscribeOrdersAsync(WalletAddress,
			cancellationToken);
		_lastAccountTradePoll = ServerTime;
	}

	private void ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(_portfolioName) &&
			!portfolioName.Equals(WalletAddress, StringComparison.Ordinal))
			throw new InvalidOperationException(
				$"Unknown GMTrade portfolio '{portfolioName}'.");
	}

	private async ValueTask SendPortfolioSnapshotAsync(long transactionId,
		bool isForced, CancellationToken cancellationToken)
	{
		var user = await RestClient.GetUserAsync(WalletAddress,
			cancellationToken);
		await SendWalletBalancesAsync(transactionId, isForced,
			cancellationToken);
		foreach (var position in (user.Positions ?? [])
			.Where(static position => position is not null)
			.OrderBy(static position => position.MarketToken,
				StringComparer.Ordinal))
			await SendPositionAsync(position, transactionId, cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		var message = new OrderStatusMessage
		{
			TransactionId = transactionId,
			IsSubscribe = true,
			PortfolioName = _portfolioName,
			Count = HistoryLimit,
		};
		await SendOrderSnapshotAsync(message, cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		var user = await RestClient.GetUserAsync(WalletAddress,
			cancellationToken);
		var limit = (statusMsg.Count ?? 500).Min(HistoryLimit).Max(1).To<int>();
		foreach (var order in (user.Orders ?? [])
			.Where(static order => order is not null)
			.Where(order => Matches(statusMsg, order))
			.OrderBy(static order => order.Header?.UpdatedAt ?? 0)
			.TakeLast(limit))
			await SendOrderAsync(order, statusMsg.TransactionId,
				cancellationToken);

		var trades = await RestClient.GetTradesAsync(new()
		{
			User = WalletAddress,
			From = statusMsg.From?.EnsureUtc(),
			To = statusMsg.To?.EnsureUtc() ?? ServerTime,
		}, limit, cancellationToken);
		foreach (var trade in trades.Where(static trade => trade is not null)
			.Where(trade => Matches(statusMsg, trade))
			.OrderBy(static trade => trade.Timestamp.EnsureUtc())
			.TakeLast(limit))
		{
			await SendAccountTradeAsync(trade, statusMsg.TransactionId,
				cancellationToken);
			if (!statusMsg.IsHistoryOnly())
				TryAcceptTrade(_seenAccountTrades, trade.Id);
		}
	}

	private bool Matches(OrderStatusMessage filter, GMTradeOrder order)
	{
		if (!filter.OrderStringId.IsEmpty() &&
			!filter.OrderStringId.Equals(order.PublicKey,
				StringComparison.Ordinal))
			return false;
		if (!TryGetMarketByToken(order.MarketToken, out var market))
			return false;
		if (!filter.SecurityId.SecurityCode.IsEmpty() &&
			!filter.SecurityId.SecurityCode.EqualsIgnoreCase(market.Code))
			return false;
		var side = GetOrderSide(order);
		if (filter.Side is Sides requestedSide && side != requestedSide)
			return false;
		var state = GetOrderState(order);
		if (filter.States is { Length: > 0 } states && !states.Contains(state))
			return false;
		var time = GetOrderTime(order);
		return (filter.From is null || time >= filter.From.Value.EnsureUtc()) &&
			(filter.To is null || time <= filter.To.Value.EnsureUtc());
	}

	private bool Matches(OrderStatusMessage filter, GMTradeTrade trade)
	{
		if (!TryGetMarketByToken(trade.MarketToken, out var market))
			return false;
		if (!filter.SecurityId.SecurityCode.IsEmpty() &&
			!filter.SecurityId.SecurityCode.EqualsIgnoreCase(market.Code))
			return false;
		var side = trade.GetExecutionSide();
		if (filter.Side is Sides requestedSide && side != requestedSide)
			return false;
		return filter.OrderStringId.IsEmpty() ||
			filter.OrderStringId.Equals(trade.Order, StringComparison.Ordinal);
	}

	private async ValueTask SendWalletBalancesAsync(long transactionId,
		bool isForced, CancellationToken cancellationToken)
	{
		foreach (var balance in await RpcClient.GetBalancesAsync(
			cancellationToken))
		{
			if (!TryGetToken(balance.Mint, out var token))
				continue;
			var current = balance.Amount.FromTokenAmount(balance.Decimals,
				"wallet balance");
			var key = transactionId.ToString(CultureInfo.InvariantCulture) + ":" +
				balance.Mint;
			using (_sync.EnterScope())
			{
				if (!isForced && _balanceFingerprints.TryGetValue(key,
					out var previous) && previous == current)
					continue;
				_balanceFingerprints[key] = current;
			}
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = token.Symbol.ToCurrencySecurity(),
				ServerTime = ServerTime,
				OriginalTransactionId = transactionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, current, true)
			.TryAdd(PositionChangeTypes.BlockedValue, 0m, true),
				cancellationToken);
		}
	}

	private ValueTask OnPositionAsync(GMTradePosition position,
		CancellationToken cancellationToken)
		=> SendPositionAsync(position, _portfolioSubscriptionId,
			cancellationToken);

	private ValueTask OnOrderAsync(GMTradeOrder order,
		CancellationToken cancellationToken)
		=> SendOrderAsync(order, _orderStatusSubscriptionId,
			cancellationToken);

	private ValueTask SendPositionAsync(GMTradePosition position,
		long transactionId, CancellationToken cancellationToken)
	{
		if (position?.MarketToken.IsEmpty() != false || transactionId == 0 ||
			!TryGetMarketByToken(position.MarketToken, out var market))
			return default;
		var volume = position.IsInsert
			? position.SizeInTokens.FromTokenAmount(market.IndexDecimals,
				"position token size")
			: 0m;
		var sizeUsd = position.Size.IsEmpty()
			? 0m
			: position.Size.FromMarketUsd("position USD size");
		var averagePrice = volume > 0 ? sizeUsd / volume : (decimal?)null;
		var currentPrice = GetMidpoint(market);
		decimal? pnl = volume > 0 && averagePrice is decimal average &&
			currentPrice is decimal current
			? (position.Kind == GMTradePositionSides.Long
				? current - average
				: average - current) * volume
			: null;
		decimal? leverage = null;
		if (volume > 0 && sizeUsd > 0 &&
			TryGetCollateralValue(position, market) is decimal collateralValue &&
			collateralValue > 0)
			leverage = sizeUsd / collateralValue;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = market.ToStockSharp(),
			DepoName = position.PublicKey,
			ServerTime = ServerTime,
			OriginalTransactionId = transactionId,
			Side = position.Kind.ToStockSharp(),
		}
		.TryAdd(PositionChangeTypes.CurrentValue, volume, true)
		.TryAdd(PositionChangeTypes.AveragePrice, averagePrice, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, currentPrice, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, pnl, true)
		.TryAdd(PositionChangeTypes.Leverage, leverage, true),
			cancellationToken);
	}

	private decimal? TryGetCollateralValue(GMTradePosition position,
		GMTradeMarketInfo market)
	{
		if (!TryGetToken(position.CollateralToken, out var token) ||
			position.CollateralAmount.IsEmpty())
			return null;
		var amount = position.CollateralAmount.FromTokenAmount(token.Decimals,
			"position collateral");
		var source = market.Market.Meta.LongToken.PublicKey == token.Mint
			? market.Market.Meta.LongToken
			: market.Market.Meta.ShortToken.PublicKey == token.Mint
				? market.Market.Meta.ShortToken
				: null;
		var price = source?.Price?.Minimum.TryFromOraclePrice(token.Decimals) ??
			source?.Price?.Maximum.TryFromOraclePrice(token.Decimals);
		return price is null ? null : amount * price.Value;
	}

	private ValueTask SendOrderAsync(GMTradeOrder order, long transactionId,
		CancellationToken cancellationToken)
	{
		if (order?.PublicKey.IsEmpty() != false || transactionId == 0 ||
			order.Header is null || order.Parameters is null ||
			!TryGetMarketByToken(order.MarketToken, out var market))
			return default;
		var currentPrice = GetMidpoint(market);
		var sizeUsd = order.Parameters.Size.IsEmpty()
			? 0m
			: order.Parameters.Size.FromMarketUsd("order USD size");
		var volume = sizeUsd > 0 && currentPrice is > 0
			? sizeUsd / currentPrice.Value
			: (decimal?)null;
		var state = GetOrderState(order);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = GetOrderTime(order),
			PortfolioName = _portfolioName,
			Side = GetOrderSide(order),
			OrderVolume = volume,
			Balance = state == OrderStates.Active && order.IsInsert
				? volume
				: 0m,
			OrderPrice = GetOrderPrice(order, market),
			OrderType = order.Parameters.Kind.ToStockSharp(),
			OrderState = state,
			OrderId = order.Header.Id,
			OrderStringId = order.PublicKey,
			OriginalTransactionId = transactionId,
			PositionEffect = order.Parameters.Kind.IsDecrease()
				? OrderPositionEffects.CloseOnly
				: order.Parameters.Kind.IsIncrease()
					? OrderPositionEffects.OpenOnly
					: null,
		}, cancellationToken);
	}

	private ValueTask SendAccountTradeAsync(GMTradeTrade trade,
		long transactionId, CancellationToken cancellationToken)
	{
		if (transactionId == 0 || trade?.MarketToken.IsEmpty() != false ||
			!TryGetMarketByToken(trade.MarketToken, out var market))
			return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = market.ToStockSharp(),
			ServerTime = trade.Timestamp.EnsureUtc(),
			PortfolioName = _portfolioName,
			Side = trade.GetExecutionSide(),
			OrderStringId = trade.Order,
			TradeStringId = trade.Id,
			TradePrice = trade.ExecutionPrice.FromOraclePrice(
				market.IndexDecimals, "execution price"),
			TradeVolume = GetTradeVolume(trade, market.IndexDecimals),
			PnL = trade.ProfitLoss.IsEmpty()
				? null
				: trade.ProfitLoss.FromMarketUsd("trade profit and loss"),
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}

	private static Sides GetOrderSide(GMTradeOrder order)
	{
		var side = order.Parameters.Side.ToStockSharp();
		return order.Parameters.Kind.IsDecrease()
			? side.Invert()
			: side;
	}

	private static OrderStates GetOrderState(GMTradeOrder order)
		=> !order.IsInsert && order.Header.Status == GMTradeActionStates.Pending
			? OrderStates.Done
			: order.Header.Status.ToStockSharp();

	private DateTime GetOrderTime(GMTradeOrder order)
		=> order.Header.UpdatedAt > 0
			? order.Header.UpdatedAt.ToUtcTime()
			: ServerTime;

	private static decimal? GetMidpoint(GMTradeMarketInfo market)
	{
		var price = market.Market.Meta.IndexToken.Price;
		var minimum = price?.Minimum.TryFromOraclePrice(market.IndexDecimals);
		var maximum = price?.Maximum.TryFromOraclePrice(market.IndexDecimals);
		return minimum is decimal min && maximum is decimal max
			? (min + max) / 2m
			: minimum ?? maximum;
	}

	private static decimal GetOrderPrice(GMTradeOrder order,
		GMTradeMarketInfo market)
	{
		var parameters = order.Parameters;
		foreach (var value in new[]
		{
			parameters.TriggerPrice,
			parameters.AcceptablePrice,
		})
		{
			if (value.IsEmpty() || value == "0" || value ==
				"340282366920938463463374607431768211455")
				continue;
			return value.FromOraclePrice(market.IndexDecimals, "order price");
		}
		return parameters.Kind.ToStockSharp() == OrderTypes.Market
			? 0m
			: GetMidpoint(market) ?? 0m;
	}
}
