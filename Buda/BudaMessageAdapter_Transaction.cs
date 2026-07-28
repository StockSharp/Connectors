namespace StockSharp.Buda;

public partial class BudaMessageAdapter
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
				"Buda.com order volume must be positive.");
		if (market.MinimumOrderAmount > 0 &&
			volume < market.MinimumOrderAmount)
			throw new InvalidOperationException(
				$"Buda.com order amount {volume} is below the " +
					$"{market.MinimumOrderAmount} minimum.");
		if (regMsg.VisibleVolume is > 0 &&
			regMsg.VisibleVolume != volume)
			throw new NotSupportedException(
				"Buda.com does not document iceberg orders.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException(
				"Buda.com GTD expiration is not exposed by this " +
					"connector.");

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (
			OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(
					orderType, regMsg.TransactionId));
		if (orderType == OrderTypes.Limit &&
			regMsg.Price <= 0)
			throw new InvalidOperationException(
				"Buda.com limit orders require a positive price.");
		if (regMsg.PostOnly == true &&
			orderType != OrderTypes.Limit)
			throw new NotSupportedException(
				"Buda.com post-only execution is available only " +
					"for limit orders.");

		var result = await RestClient.PlaceOrderAsync(
			new()
			{
				MarketId = market.Id,
				Side = regMsg.Side,
				OrderType = orderType,
				TimeInForce = regMsg.TimeInForce,
				PostOnly = regMsg.PostOnly == true,
				Price = regMsg.Price,
				Amount = volume,
				ClientId = regMsg.UserOrderId.IsEmpty()
					? $"ss-{regMsg.TransactionId}"
					: regMsg.UserOrderId,
			},
			cancellationToken);
		if (result?.Id.IsEmpty() != false)
			throw new InvalidDataException(
				"Buda.com accepted an order without returning " +
					"its identifier.");
		TrackOrder(result.Id, new()
		{
			TransactionId = regMsg.TransactionId,
			SecurityCode = market.SecurityCode,
			Side = regMsg.Side,
			OrderType = orderType,
			Volume = volume,
			Price = regMsg.Price,
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
		var result = await RestClient.CancelOrderAsync(
			ResolveOrderId(
				cancelMsg.OrderId,
				cancelMsg.OrderStringId),
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
			"Buda.com does not provide an atomic order-replace " +
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
				"Buda.com spot cancellation cannot close positions.");
		var market = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(cancelMsg.SecurityId);
		foreach (var order in
			await RestClient.CancelAllOrdersAsync(
				market?.Id,
				cancelMsg.Side,
				cancellationToken) ?? [])
			await SendOrderAsync(
				order,
				cancelMsg.TransactionId,
				cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			lookupMsg.TransactionId, cancellationToken);
		EnsurePrivateReady();
		if (!lookupMsg.IsSubscribe)
		{
			if (_portfolioSubscriptionId ==
				lookupMsg.OriginalTransactionId)
			{
				_portfolioSubscriptionId = 0;
				if (!_pubSubKey.IsEmpty())
					await WsClient.UnsubscribeAsync(
						$"balances@{_pubSubKey}",
						cancellationToken);
			}
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
					BoardCode = BoardCodes.Buda,
					OriginalTransactionId =
						lookupMsg.TransactionId,
				},
				cancellationToken);
			await SendPortfolioSnapshotAsync(
				lookupMsg.TransactionId, cancellationToken);
		}
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
				"Buda.com portfolio subscription already exists.");
		_portfolioSubscriptionId = lookupMsg.TransactionId;
		try
		{
			if (!_pubSubKey.IsEmpty())
				await WsClient.SubscribeAsync(
					$"balances@{_pubSubKey}",
					cancellationToken);
			await SendSubscriptionResultAsync(
				lookupMsg, cancellationToken);
		}
		catch
		{
			_portfolioSubscriptionId = 0;
			throw;
		}
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
			{
				_orderStatusSubscriptionId = 0;
				if (!_pubSubKey.IsEmpty())
					await WsClient.UnsubscribeAsync(
						$"orders@{_pubSubKey}",
						cancellationToken);
			}
			return;
		}
		if (statusMsg.Count is <= 0)
		{
			await CompleteOrderStatusAsync(
				statusMsg, cancellationToken);
			return;
		}

		await SendOrderSnapshotAsync(
			statusMsg, cancellationToken);
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
				"Buda.com order-status subscription already exists.");
		_orderStatusSubscriptionId = statusMsg.TransactionId;
		try
		{
			if (!_pubSubKey.IsEmpty())
				await WsClient.SubscribeAsync(
					$"orders@{_pubSubKey}",
					cancellationToken);
			await SendSubscriptionResultAsync(
				statusMsg, cancellationToken);
		}
		catch
		{
			_orderStatusSubscriptionId = 0;
			throw;
		}
	}

	private async ValueTask SendPortfolioSnapshotAsync(
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		foreach (var balance in await RestClient.GetBalancesAsync(
			cancellationToken) ?? [])
			await SendBalanceAsync(
				balance,
				originalTransactionId,
				cancellationToken);
	}

	private ValueTask SendBalanceAsync(
		BudaBalance balance,
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
					SecurityCode = balance.Currency,
					BoardCode = BoardCodes.Buda,
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
				balance.Blocked,
				true),
			cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(
		OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		if (statusMsg.HasOrderId())
		{
			var order = await RestClient.GetOrderAsync(
				ResolveOrderId(
					statusMsg.OrderId,
					statusMsg.OrderStringId),
				cancellationToken);
			if (MatchesOrder(order, statusMsg))
				await SendOrderAsync(
					order,
					statusMsg.TransactionId,
					cancellationToken);
			return;
		}

		var markets = GetStatusMarkets(statusMsg);
		if (markets.Length == 0)
			markets = GetMarkets();
		var maximum = (statusMsg.Count ?? 100)
			.Max(1).Min(100).To<int>();
		var orders = new List<BudaOrder>();
		foreach (var market in markets)
			orders.AddRange(await RestClient.GetOrdersAsync(
				market.Id,
				null,
				maximum,
				cancellationToken) ?? []);
		foreach (var order in orders
			.Where(order => MatchesOrder(order, statusMsg))
			.GroupBy(
				static order => order.Id,
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
	}

	private async ValueTask PollOrdersAsync(
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		foreach (var market in GetMarkets())
		{
			foreach (var order in await RestClient.GetOrdersAsync(
				market.Id,
				null,
				100,
				cancellationToken) ?? [])
			{
				if (order.State == OrderStates.Active)
					await SendOrderAsync(
						order,
						originalTransactionId,
						cancellationToken);
			}
		}
	}

	private ValueTask SendOrderAsync(
		BudaOrder order,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (order?.Id.IsEmpty() != false ||
			order.MarketId.IsEmpty())
			return default;
		var market = GetMarket(order.MarketId);
		if (market is null)
			return default;
		var tracked = GetTrackedOrder(order.Id);
		tracked ??= new()
		{
			SecurityCode = market.SecurityCode,
			Side = order.Side,
			OrderType = order.OrderType,
			Volume = order.OriginalAmount,
			Price = order.Price,
		};
		TrackOrder(order.Id, tracked);
		var execution = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = order.CreatedAt?.ToUniversalTime() ??
				CurrentTime,
			PortfolioName = GetPortfolioName(),
			Side = order.Side,
			OrderVolume = order.OriginalAmount > 0
				? order.OriginalAmount
				: tracked.Volume,
			Balance = order.RemainingAmount,
			OrderPrice = order.Price > 0
				? order.Price
				: tracked.Price,
			OrderType = order.OrderType,
			OrderState = order.State == OrderStates.None
				? OrderStates.Active
				: order.State,
			OrderStringId = order.Id,
			UserOrderId = order.ClientId,
			TransactionId = tracked.TransactionId,
			OriginalTransactionId = originalTransactionId,
			TimeInForce = order.TimeInForce,
			PostOnly = order.PostOnly,
			Commission = order.PaidFee > 0
				? order.PaidFee
				: null,
			CommissionCurrency = order.FeeCurrency,
		};
		if (long.TryParse(
			order.Id,
			NumberStyles.None,
			CultureInfo.InvariantCulture,
			out var numericOrderId))
			execution.OrderId = numericOrderId;
		return SendOutMessageAsync(execution, cancellationToken);
	}

	private BudaMarket[] GetStatusMarkets(
		OrderStatusMessage filter)
	{
		var ids = new List<SecurityId>();
		if (!filter.SecurityId.SecurityCode.IsEmpty())
			ids.Add(filter.SecurityId);
		ids.AddRange(filter.SecurityIds.Where(
			static id => !id.SecurityCode.IsEmpty()));
		return [.. ids.Select(GetMarket).Distinct()];
	}

	private bool MatchesOrder(
		BudaOrder order,
		OrderStatusMessage filter)
	{
		if (order?.Id.IsEmpty() != false)
			return false;
		var time = order.CreatedAt?.ToUniversalTime() ??
			DateTime.MinValue;
		if (filter.From is DateTime from &&
			time < from.ToUniversalTime() ||
			filter.To is DateTime to &&
			time > to.ToUniversalTime())
			return false;
		if (filter.Side is Sides side &&
			order.Side != side)
			return false;
		if (filter.States.Length > 0 &&
			!filter.States.Contains(order.State))
			return false;
		if (filter.Volume is decimal volume &&
			order.OriginalAmount != volume)
			return false;
		if (!filter.PortfolioName.IsEmpty() &&
			!filter.PortfolioName.EqualsIgnoreCase(
				GetPortfolioName()))
			return false;
		var markets = GetStatusMarkets(filter);
		return markets.Length == 0 ||
			markets.Any(market =>
				market.Id.EqualsIgnoreCase(order.MarketId));
	}

	private static DateTime GetOrderTimestamp(BudaOrder order)
		=> order?.CreatedAt?.ToUniversalTime() ??
			DateTime.MinValue;

	private async ValueTask CompleteOrderStatusAsync(
		OrderStatusMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(
			message, cancellationToken);
		await SendSubscriptionFinishedAsync(
			message.TransactionId, cancellationToken);
	}
}
