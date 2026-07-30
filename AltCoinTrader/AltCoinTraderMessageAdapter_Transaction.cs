namespace StockSharp.AltCoinTrader;

public partial class AltCoinTraderMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var market = GetMarket(regMsg.SecurityId);
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException(
				"AltCoinTrader order volume must be positive.");
		if (regMsg.VisibleVolume is > 0 &&
			regMsg.VisibleVolume != volume)
			throw new NotSupportedException(
				"AltCoinTrader does not expose iceberg orders.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException(
				"AltCoinTrader does not expose GTD orders.");
		if (regMsg.PostOnly == true)
			throw new NotSupportedException(
				"AltCoinTrader does not expose post-only orders.");

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (
			OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(
					orderType,
					regMsg.TransactionId));
		if (orderType == OrderTypes.Limit &&
			regMsg.Price <= 0)
			throw new InvalidOperationException(
				"AltCoinTrader limit orders require " +
					"a positive price.");
		if (orderType == OrderTypes.Market &&
			regMsg.TimeInForce is not null)
			throw new NotSupportedException(
				"AltCoinTrader market orders do not expose " +
					"time-in-force options.");

		var clientOrderId =
			AltCoinTraderExtensions.CreateClientOrderId(
				regMsg.TransactionId);
		AltCoinTraderOrder result;
		if (orderType == OrderTypes.Limit)
		{
			var value = regMsg.Price * volume;
			if (market.MinimumOrderValue is decimal minimum &&
				value < minimum)
				throw new InvalidOperationException(
					$"AltCoinTrader order value {value} is below " +
						$"the {minimum} minimum for " +
						$"'{market.Symbol}'.");
			result = await RestClient.PlaceLimitOrderAsync(
				new AltCoinTraderLimitOrderRequest
				{
					Market = market.Symbol,
					Side = regMsg.Side.ToAltCoinTrader(),
					Price = regMsg.Price.ToWire(),
					Quantity = volume.ToWire(),
					TimeInForce =
						regMsg.TimeInForce.ToAltCoinTrader(),
					ClientOrderId = clientOrderId,
				},
				cancellationToken);
		}
		else
		{
			string amount = null;
			string quantity = null;
			if (regMsg.Side == Sides.Buy)
			{
				var ticker = await RestClient.GetTickerAsync(
					market.Symbol,
					cancellationToken);
				var price = ticker?.AskPrice ??
					ticker?.LastPrice;
				if (price is not > 0)
					throw new InvalidDataException(
						"AltCoinTrader returned no price " +
							"for converting a market buy " +
							"to quote currency.");
				var quoteAmount = decimal.Round(
					volume * price.Value,
					market.PricePrecision.Max(0).Min(28),
					MidpointRounding.ToPositiveInfinity);
				if (market.MinimumOrderValue is decimal minimum &&
					quoteAmount < minimum)
					throw new InvalidOperationException(
						$"AltCoinTrader market-buy value " +
							$"{quoteAmount} is below the " +
							$"{minimum} minimum for " +
							$"'{market.Symbol}'.");
				amount = quoteAmount.ToWire();
			}
			else
			{
				quantity = volume.ToWire();
			}

			result = await RestClient.PlaceMarketOrderAsync(
				new AltCoinTraderMarketOrderRequest
				{
					Market = market.Symbol,
					Side = regMsg.Side.ToAltCoinTrader(),
					Quantity = quantity,
					Amount = amount,
					ClientOrderId = clientOrderId,
				},
				cancellationToken);
		}

		if (result?.OrderId.IsEmpty() != false)
			throw new InvalidDataException(
				"AltCoinTrader accepted an order without " +
					"returning its identifier.");

		TrackOrder(
			result.OrderId,
			new()
			{
				TransactionId = regMsg.TransactionId,
				SecurityCode = market.SecurityCode,
				Side = regMsg.Side,
				OrderType = orderType,
				Volume = volume,
				Price = regMsg.Price,
				TimeInForce = regMsg.TimeInForce ??
					TimeInForce.PutInQueue,
			});
		await SendOrderAsync(
			result,
			regMsg.TransactionId,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var orderId = ResolveOrderId(
			cancelMsg.OrderId,
			cancelMsg.OrderStringId);
		var result = await RestClient.CancelOrderAsync(
			orderId,
			cancellationToken);
		await SendOrderAsync(
			result,
			cancelMsg.TransactionId,
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
			"AltCoinTrader does not provide an atomic " +
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
				"AltCoinTrader spot cancellation " +
					"cannot close positions.");

		var market = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(cancelMsg.SecurityId);
		var orders = await RestClient.GetOpenOrdersAsync(
			market?.Symbol,
			cancellationToken) ?? [];

		foreach (var order in orders.Where(order =>
			order?.OrderId.IsEmpty() == false &&
			(cancelMsg.Side is null ||
				order.Side.ToSide() == cancelMsg.Side)))
		{
			var result = await RestClient.CancelOrderAsync(
				order.OrderId,
				cancellationToken);
			await SendOrderAsync(
				result,
				cancelMsg.TransactionId,
				cancellationToken);
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

		if (!lookupMsg.IsSubscribe)
		{
			if (_portfolioSubscriptionId ==
				lookupMsg.OriginalTransactionId)
				_portfolioSubscriptionId = 0;
			return;
		}

		var portfolioName = GetPortfolioName();
		if (lookupMsg.PortfolioName.IsEmpty() ||
			lookupMsg.PortfolioName.EqualsIgnoreCase(
				portfolioName))
		{
			await SendOutMessageAsync(
				new PortfolioMessage
				{
					PortfolioName = portfolioName,
					BoardCode = BoardCodes.AltCoinTrader,
					OriginalTransactionId =
						lookupMsg.TransactionId,
				},
				cancellationToken);
			await SendPortfolioSnapshotAsync(
				lookupMsg.TransactionId,
				cancellationToken);
		}

		await SendSubscriptionResultAsync(
			lookupMsg,
			cancellationToken);

		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(
				lookupMsg.TransactionId,
				cancellationToken);
			return;
		}

		_portfolioSubscriptionId = lookupMsg.TransactionId;
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
			await CompleteOrderStatusAsync(
				statusMsg,
				cancellationToken);
			return;
		}

		var maximum = (statusMsg.Count ?? 100)
			.Min(200).Max(1).To<int>();
		await SendOrderSnapshotAsync(
			statusMsg,
			maximum,
			cancellationToken);
		await SendSubscriptionResultAsync(
			statusMsg,
			cancellationToken);

		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(
				statusMsg.TransactionId,
				cancellationToken);
			return;
		}

		_orderStatusSubscriptionId = statusMsg.TransactionId;
	}

	private async ValueTask SendPortfolioSnapshotAsync(
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var balances = await RestClient.GetBalancesAsync(
			cancellationToken);

		foreach (var balance in balances ?? [])
			await SendBalanceAsync(
				balance,
				originalTransactionId,
				cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(
		OrderStatusMessage statusMsg,
		int maximum,
		CancellationToken cancellationToken)
	{
		var requestedOrderId = statusMsg.HasOrderId()
			? ResolveOrderId(
				statusMsg.OrderId,
				statusMsg.OrderStringId)
			: null;
		if (requestedOrderId is not null)
		{
			var order = await RestClient.GetOrderAsync(
				requestedOrderId,
				cancellationToken);
			if (MatchesOrder(order, statusMsg))
				await SendOrderAsync(
					order,
					statusMsg.TransactionId,
					cancellationToken);
			return;
		}

		var markets = GetStatusMarkets(statusMsg);
		var singleMarket = markets.Length == 1
			? markets[0].Symbol
			: null;
		var orders = new List<AltCoinTraderOrder>(
			await RestClient.GetOpenOrdersAsync(
				singleMarket,
				cancellationToken) ?? []);
		if (statusMsg.IsHistoryOnly() ||
			statusMsg.From is not null ||
			statusMsg.To is not null)
			orders.AddRange(
				await RestClient.GetOrdersAsync(
					singleMarket,
					null,
					statusMsg.From,
					statusMsg.To,
					maximum,
					1,
					cancellationToken) ?? []);

		foreach (var order in orders
			.Where(order => MatchesOrder(order, statusMsg))
			.GroupBy(
				static order => order.OrderId,
				StringComparer.Ordinal)
			.Select(static group => group
				.OrderByDescending(GetOrderTimestamp)
				.First())
			.OrderBy(GetOrderTimestamp)
			.TakeLast(maximum))
			await SendOrderAsync(
				order,
				statusMsg.TransactionId,
				cancellationToken);

		if (statusMsg.IsHistoryOnly() ||
			statusMsg.From is not null)
		{
			var trades = await RestClient.GetPrivateTradesAsync(
				singleMarket,
				statusMsg.From,
				statusMsg.To,
				maximum,
				1,
				cancellationToken);

			foreach (var trade in (trades ?? [])
				.Where(trade =>
					MatchesTrade(trade, markets))
				.OrderBy(static trade => trade.Timestamp))
				await SendPrivateTradeAsync(
					trade,
					statusMsg.TransactionId,
					false,
					cancellationToken);
		}
	}

	private async ValueTask PollPrivateStateAsync(
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var orders = await RestClient.GetOpenOrdersAsync(
			null,
			cancellationToken);

		foreach (var order in orders ?? [])
			await SendOrderAsync(
				order,
				originalTransactionId,
				cancellationToken);

		var trades = await RestClient.GetPrivateTradesAsync(
			null,
			DateTime.UtcNow.AddMinutes(-2),
			DateTime.UtcNow,
			200,
			1,
			cancellationToken);

		foreach (var trade in trades ?? [])
			await SendPrivateTradeAsync(
				trade,
				originalTransactionId,
				true,
				cancellationToken);
	}

	private ValueTask SendBalanceAsync(
		AltCoinTraderBalance balance,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (balance?.Currency.IsEmpty() != false)
			return default;
		return SendOutMessageAsync(
			new PositionChangeMessage
			{
				PortfolioName = GetPortfolioName(),
				SecurityId = new()
				{
					SecurityCode = balance.Currency
						.ToUpperInvariant(),
					BoardCode = BoardCodes.AltCoinTrader,
				},
				ServerTime = CurrentTime,
				OriginalTransactionId =
					originalTransactionId,
			}
			.TryAdd(
				PositionChangeTypes.CurrentValue,
				balance.Available,
				true)
			.TryAdd(
				PositionChangeTypes.BlockedValue,
				balance.Reserved,
				true),
			cancellationToken);
	}

	private ValueTask SendOrderAsync(
		AltCoinTraderOrder order,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (order?.OrderId.IsEmpty() != false ||
			order.Market.IsEmpty())
			return default;
		var market = GetMarket(order.Market);
		if (market is null)
			return default;

		var tracked = GetTrackedOrder(order.OrderId);
		if (tracked is null)
		{
			tracked = new()
			{
				TransactionId = order.TransactionId ?? 0,
				SecurityCode = market.SecurityCode,
				Side = order.Side.ToSide(),
				OrderType = order.Type.ToOrderType(),
				Volume = order.Quantity,
				Price = order.Price ?? 0,
				TimeInForce =
					order.TimeInForce.ToTimeInForce(),
			};
			TrackOrder(order.OrderId, tracked);
		}

		var execution = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = GetOrderTime(order),
			PortfolioName = GetPortfolioName(),
			Side = order.Side.ToSide(),
			OrderVolume = order.Quantity > 0
				? order.Quantity
				: tracked.Volume,
			Balance = order.Remaining,
			OrderPrice = order.Price ?? tracked.Price,
			OrderType = order.Type.IsEmpty()
				? tracked.OrderType
				: order.Type.ToOrderType(),
			OrderState = order.Status.ToOrderState(),
			OrderStringId = order.OrderId,
			TransactionId = tracked.TransactionId,
			OriginalTransactionId = originalTransactionId,
			TimeInForce = order.TimeInForce.IsEmpty()
				? tracked.TimeInForce
				: order.TimeInForce.ToTimeInForce(),
			PostOnly = false,
		};
		if (long.TryParse(
			order.OrderId,
			NumberStyles.None,
			CultureInfo.InvariantCulture,
			out var numericOrderId))
			execution.OrderId = numericOrderId;
		return SendOutMessageAsync(
			execution,
			cancellationToken);
	}

	private async ValueTask SendPrivateTradeAsync(
		AltCoinTraderUserTrade trade,
		long originalTransactionId,
		bool onlyNew,
		CancellationToken cancellationToken)
	{
		if (trade?.TradeId.IsEmpty() != false ||
			trade.Market.IsEmpty())
			return;
		var added = AddTrade(
			trade.Market,
			trade.TradeId,
			true);
		if (onlyNew && !added)
			return;

		var market = GetMarket(trade.Market);
		if (market is null)
			return;
		var tracked = GetTrackedOrder(trade.OrderId);
		var execution = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = market.ToStockSharp(),
			ServerTime = trade.Timestamp > 0
				? trade.Timestamp.FromAltCoinTraderSeconds()
				: CurrentTime,
			PortfolioName = GetPortfolioName(),
			Side = trade.Side.ToSide(),
			OrderStringId = trade.OrderId,
			TradeStringId = trade.TradeId,
			TradePrice = trade.Price,
			TradeVolume = trade.ExecutionQuantity.Abs(),
			Commission = trade.Fee,
			CommissionCurrency = market.Quote,
			TransactionId = tracked?.TransactionId ??
				trade.TransactionId ?? 0,
			OriginalTransactionId = originalTransactionId,
		};
		if (long.TryParse(
			trade.OrderId,
			NumberStyles.None,
			CultureInfo.InvariantCulture,
			out var numericOrderId))
			execution.OrderId = numericOrderId;
		if (long.TryParse(
			trade.TradeId,
			NumberStyles.None,
			CultureInfo.InvariantCulture,
			out var numericTradeId))
			execution.TradeId = numericTradeId;
		await SendOutMessageAsync(
			execution,
			cancellationToken);
	}

	private async ValueTask OnPrivateOrderAsync(
		AltCoinTraderOrder order,
		CancellationToken cancellationToken)
	{
		if (_orderStatusSubscriptionId != 0)
			await SendOrderAsync(
				order,
				_orderStatusSubscriptionId,
				cancellationToken);
	}

	private async ValueTask OnPrivateFillAsync(
		AltCoinTraderUserTrade trade,
		CancellationToken cancellationToken)
	{
		if (_orderStatusSubscriptionId != 0)
			await SendPrivateTradeAsync(
				trade,
				_orderStatusSubscriptionId,
				true,
				cancellationToken);
	}

	private async ValueTask OnPrivateBalancesAsync(
		AltCoinTraderBalance[] balances,
		CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId == 0)
			return;

		foreach (var balance in balances ?? [])
			await SendBalanceAsync(
				balance,
				_portfolioSubscriptionId,
				cancellationToken);
	}

	private AltCoinTraderMarket[] GetStatusMarkets(
		OrderStatusMessage filter)
	{
		var ids = new List<SecurityId>();
		if (!filter.SecurityId.SecurityCode.IsEmpty())
			ids.Add(filter.SecurityId);
		ids.AddRange(filter.SecurityIds.Where(
			static id => !id.SecurityCode.IsEmpty()));
		return
		[
			.. ids
				.Select(GetMarket)
				.Distinct(),
		];
	}

	private bool MatchesOrder(
		AltCoinTraderOrder order,
		OrderStatusMessage filter)
	{
		if (order?.OrderId.IsEmpty() != false ||
			order.Market.IsEmpty())
			return false;
		var time = GetOrderTime(order);
		if (filter.From is DateTime from &&
			time < from.ToUtc() ||
			filter.To is DateTime to &&
			time > to.ToUtc())
			return false;
		if (filter.Side is Sides side &&
			order.Side.ToSide() != side)
			return false;
		var state = order.Status.ToOrderState();
		if (filter.States.Length > 0 &&
			!filter.States.Contains(state))
			return false;
		if (filter.Volume is decimal volume &&
			order.Quantity != volume)
			return false;
		if (!filter.PortfolioName.IsEmpty() &&
			!filter.PortfolioName.EqualsIgnoreCase(
				GetPortfolioName()))
			return false;
		var markets = GetStatusMarkets(filter);
		return markets.Length == 0 ||
			markets.Any(market =>
				market.Symbol.EqualsIgnoreCase(order.Market));
	}

	private static bool MatchesTrade(
		AltCoinTraderUserTrade trade,
		IReadOnlyCollection<AltCoinTraderMarket> markets)
		=> trade?.Market.IsEmpty() == false &&
			(markets.Count == 0 ||
				markets.Any(market =>
					market.Symbol.EqualsIgnoreCase(
						trade.Market)));

	private DateTime GetOrderTime(AltCoinTraderOrder order)
	{
		var timestamp = GetOrderTimestamp(order);
		return timestamp > 0
			? timestamp.FromAltCoinTraderSeconds()
			: CurrentTime;
	}

	private static long GetOrderTimestamp(
		AltCoinTraderOrder order)
		=> order?.UpdatedAt > 0
			? order.UpdatedAt
			: order?.CreatedAt ?? 0;

	private async ValueTask CompleteOrderStatusAsync(
		OrderStatusMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(
			message,
			cancellationToken);
		await SendSubscriptionFinishedAsync(
			message.TransactionId,
			cancellationToken);
	}
}
