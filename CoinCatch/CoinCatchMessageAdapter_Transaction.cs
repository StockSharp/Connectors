namespace StockSharp.CoinCatch;

public partial class CoinCatchMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var market = GetMarket(regMsg.SecurityId);
		if (regMsg.OrderType == OrderTypes.Conditional)
			throw new NotSupportedException(
				"CoinCatch trigger orders use the separate plan-order " +
					"API and are not exposed by this adapter.");
		var orderType = regMsg.OrderType.ToCoinCatch();
		if (regMsg.VisibleVolume is > 0 &&
			regMsg.VisibleVolume != regMsg.Volume)
			throw new NotSupportedException(
				"CoinCatch does not document iceberg orders.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException(
				"CoinCatch does not document GTD orders.");
		if (regMsg.Volume <= 0)
			throw new ArgumentOutOfRangeException(
				nameof(regMsg.Volume), regMsg.Volume,
				"CoinCatch order size must be positive.");
		if (orderType == "limit" && regMsg.Price <= 0)
			throw new ArgumentOutOfRangeException(
				nameof(regMsg.Price), regMsg.Price,
				"CoinCatch limit-order price must be positive.");
		if (regMsg.PostOnly == true && orderType != "limit")
			throw new NotSupportedException(
				"CoinCatch post-only execution is available only for " +
					"limit orders.");

		var condition = regMsg.Condition as CoinCatchOrderCondition;
		var reduceOnly = ProductType.IsFutures() &&
			condition?.ReduceOnly == true;
		var side = ProductType.IsFutures()
			? regMsg.Side.ToCoinCatchFutures(reduceOnly)
			: regMsg.Side.ToCoinCatch();
		var timeInForce = regMsg.PostOnly == true
			? "post_only"
			: regMsg.TimeInForce.ToCoinCatch();
		var clientOrderId = CoinCatchExtensions.CreateClientOrderId(
			regMsg.TransactionId, regMsg.UserOrderId);
		var marginCoin = GetMarginCoin(market);
		var result = await RestClient.PlaceOrderAsync(
			market.Symbol,
			marginCoin,
			side,
			orderType,
			timeInForce,
			orderType == "limit" ? regMsg.Price : null,
			regMsg.Volume,
			clientOrderId,
			reduceOnly,
			cancellationToken);
		if (result?.OrderId.IsEmpty() != false)
			throw new InvalidDataException(
				"CoinCatch returned no order identifier.");

		using (_sync.EnterScope())
		{
			_orderTransactions[result.OrderId] =
				regMsg.TransactionId;
			_orderSymbols[result.OrderId] = market.Symbol;
		}
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(ProductType),
			ServerTime = CurrentTime,
			TransactionId = regMsg.TransactionId,
			OriginalTransactionId = regMsg.TransactionId,
			OrderStringId = result.OrderId,
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
			? GetMarket(ResolveOrderSymbol(orderId))
			: GetMarket(cancelMsg.SecurityId);
		await RestClient.CancelOrderAsync(
			market.Symbol, GetMarginCoin(market), orderId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		_ = replaceMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"CoinCatch does not provide an atomic order-replace " +
				"operation.");
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
				"CoinCatch bulk cancellation cannot close positions.");
		if (cancelMsg.SecurityId != default)
		{
			var market = GetMarket(cancelMsg.SecurityId);
			await RestClient.CancelSymbolOrdersAsync(
				market.Symbol, GetMarginCoin(market),
				cancellationToken);
			return;
		}
		var orders = await RestClient.GetOpenOrdersAsync(
			null, cancellationToken);

		foreach (var symbol in orders
			.Where(order => !order.Symbol.IsEmpty() &&
				(cancelMsg.Side is null ||
					order.Side.ToSide() == cancelMsg.Side))
			.Select(static order => order.Symbol)
			.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			var market = GetMarket(symbol);
			if (market is null)
				continue;
			await RestClient.CancelSymbolOrdersAsync(
				market.Symbol, GetMarginCoin(market),
				cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			lookupMsg.TransactionId, cancellationToken);

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
			BoardCode = ProductType.ToBoardCode(),
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await SendBalancesAsync(
			await RestClient.GetBalancesAsync(cancellationToken),
			lookupMsg.TransactionId, cancellationToken);
		if (ProductType.IsFutures())
			await SendPositionsAsync(
				await RestClient.GetPositionsAsync(cancellationToken),
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
				"CoinCatch portfolio subscription already exists.");
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
			statusMsg.TransactionId, cancellationToken);

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

		var market = statusMsg.SecurityId == default
			? null
			: GetMarket(statusMsg.SecurityId);
		CoinCatchOrder[] orders;
		if (statusMsg.OrderId is not null ||
			!statusMsg.OrderStringId.IsEmpty())
		{
			var orderId = ResolveOrderId(
				statusMsg.OrderId, statusMsg.OrderStringId);
			market ??= GetMarket(ResolveOrderSymbol(orderId));
			orders = await RestClient.GetOrderAsync(
				market.Symbol, orderId, cancellationToken);
		}
		else
		{
			var openOrders = await RestClient.GetOpenOrdersAsync(
				market?.Symbol, cancellationToken);
			if (market is not null &&
				(statusMsg.IsHistoryOnly() ||
					statusMsg.From is not null ||
					statusMsg.To is not null))
			{
				var history =
					await RestClient.GetHistoryOrdersAsync(
						market.Symbol,
						statusMsg.From,
						statusMsg.To,
						(statusMsg.Count ?? 100)
							.Min(100).Max(1).To<int>(),
						cancellationToken);
				orders = [.. openOrders.Concat(history)
					.GroupBy(static order => order.OrderId,
						StringComparer.Ordinal)
					.Select(static group => group
						.OrderByDescending(static order =>
							order.UpdateTime)
						.First())];
			}
			else
				orders = openOrders;
		}

		var from = statusMsg.From?.ToUtc();
		var to = statusMsg.To?.ToUtc();
		orders = [.. (orders ?? [])
			.Where(order =>
				(statusMsg.Side is null ||
					order.Side.ToSide() == statusMsg.Side) &&
				(from is null || GetOrderTime(order) >= from.Value) &&
				(to is null || GetOrderTime(order) <= to.Value))
			.OrderBy(GetOrderTime)
			.TakeLast((statusMsg.Count ?? 100)
				.Min(500).Max(1).To<int>())];
		await SendOrdersAsync(
			orders, statusMsg.TransactionId, cancellationToken);

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
				"CoinCatch order-status subscription already exists.");
		_orderStatusSubscriptionId = statusMsg.TransactionId;
		await SendSubscriptionResultAsync(
			statusMsg, cancellationToken);
	}

	private async ValueTask PollPrivateStateAsync(
		CancellationToken cancellationToken)
	{
		if (!await _pollSync.WaitAsync(0, cancellationToken))
			return;
		try
		{
			if (_portfolioSubscriptionId != 0)
			{
				await SendBalancesAsync(
					await RestClient.GetBalancesAsync(
						cancellationToken),
					_portfolioSubscriptionId, cancellationToken);
				if (ProductType.IsFutures())
					await SendPositionsAsync(
						await RestClient.GetPositionsAsync(
							cancellationToken),
						_portfolioSubscriptionId,
						cancellationToken);
			}
			if (_orderStatusSubscriptionId != 0)
				await SendOrdersAsync(
					await RestClient.GetOpenOrdersAsync(
						null, cancellationToken),
					_orderStatusSubscriptionId, cancellationToken);
		}
		catch (Exception error)
		{
			await SendOutErrorAsync(error, cancellationToken);
		}
		finally
		{
			_pollSync.Release();
		}
	}

	private async ValueTask SendBalancesAsync(
		IEnumerable<CoinCatchBalance> balances, long transactionId,
		CancellationToken cancellationToken)
	{
		foreach (var balance in balances ?? [])
		{
			if (balance?.Coin.IsEmpty() != false)
				continue;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = GetPortfolioName(),
				SecurityId = new()
				{
					SecurityCode =
						balance.Coin.ToUpperInvariant(),
					BoardCode = ProductType.ToBoardCode(),
				},
				ServerTime = balance.UpdateTime > 0
					? balance.UpdateTime.FromCoinCatchTime()
					: CurrentTime,
				OriginalTransactionId = transactionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue,
				balance.CurrentValue, true)
			.TryAdd(PositionChangeTypes.BlockedValue,
				balance.Blocked, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL,
				balance.UnrealizedProfit, true),
				cancellationToken);
		}
	}

	private async ValueTask SendPositionsAsync(
		IEnumerable<CoinCatchPosition> positions, long transactionId,
		CancellationToken cancellationToken)
	{
		foreach (var position in positions ?? [])
		{
			if (position?.Symbol.IsEmpty() != false)
				continue;
			var market = GetMarket(position.Symbol);
			if (market is null)
				continue;
			var current = position.Total;
			if (position.Side.EqualsIgnoreCase("short"))
				current = -current;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = GetPortfolioName(),
				SecurityId = market.ToStockSharp(ProductType),
				ServerTime = position.UpdateTime > 0
					? position.UpdateTime.FromCoinCatchTime()
					: CurrentTime,
				OriginalTransactionId = transactionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, current, true)
			.TryAdd(PositionChangeTypes.BlockedValue,
				position.Locked, true)
			.TryAdd(PositionChangeTypes.AveragePrice,
				position.AveragePrice, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL,
				position.UnrealizedProfit, true)
			.TryAdd(PositionChangeTypes.LiquidationPrice,
				position.LiquidationPrice, true)
			.TryAdd(PositionChangeTypes.Leverage,
				position.Leverage, true),
				cancellationToken);
		}
	}

	private async ValueTask SendOrdersAsync(
		IEnumerable<CoinCatchOrder> orders, long transactionId,
		CancellationToken cancellationToken)
	{
		foreach (var order in orders ?? [])
		{
			if (order?.Symbol.IsEmpty() != false ||
				order.OrderId.IsEmpty())
				continue;
			var market = GetMarket(order.Symbol);
			if (market is null)
				continue;
			var ownTransactionId =
				CoinCatchExtensions.ParseTransactionId(
					order.ClientOrderId);
			using (_sync.EnterScope())
			{
				if (ownTransactionId == 0)
					_orderTransactions.TryGetValue(
						order.OrderId, out ownTransactionId);
				if (ownTransactionId != 0)
					_orderTransactions[order.OrderId] =
						ownTransactionId;
				_orderSymbols[order.OrderId] = order.Symbol;
			}
			var execution = new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = market.ToStockSharp(ProductType),
				ServerTime = GetOrderTime(order) is var time &&
					time > DateTime.MinValue
						? time
						: CurrentTime,
				TransactionId = ownTransactionId,
				OriginalTransactionId = transactionId,
				OrderStringId = order.OrderId,
				OrderType = order.ToOrderType(),
				OrderPrice = order.Price ??
					order.AveragePrice ?? 0,
				OrderVolume = order.Quantity,
				Balance = order.RemainingQuantity,
				Side = order.Side.ToSide(),
				TimeInForce =
					order.TimeInForce.ToTimeInForce(),
				PortfolioName = GetPortfolioName(),
				OrderState = order.Status.ToOrderState(),
				Commission = order.Fee,
			};
			if (long.TryParse(order.OrderId, NumberStyles.None,
				CultureInfo.InvariantCulture,
				out var numericOrderId))
				execution.OrderId = numericOrderId;
			await SendOutMessageAsync(execution, cancellationToken);
		}
	}

	private static DateTime GetOrderTime(CoinCatchOrder order)
	{
		var timestamp = order?.UpdateTime > 0
			? order.UpdateTime
			: order?.CreateTime ?? 0;
		return timestamp > 0
			? timestamp.FromCoinCatchTime()
			: DateTime.MinValue;
	}

	private string ResolveOrderSymbol(string orderId)
	{
		using (_sync.EnterScope())
			return _orderSymbols.TryGetValue(orderId, out var symbol)
				? symbol
				: throw new InvalidOperationException(
					"CoinCatch operation requires the order security.");
	}

	private string GetMarginCoin(CoinCatchSymbol market)
		=> ProductType == CoinCatchProductTypes.CoinFutures
			? market.BaseCoin
			: market.QuoteCoin;
}
