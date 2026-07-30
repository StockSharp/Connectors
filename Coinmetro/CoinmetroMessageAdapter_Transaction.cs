namespace StockSharp.Coinmetro;

public partial class CoinmetroMessageAdapter
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
				"Coinmetro order volume must be positive.");
		if (market.MinimumAmount > 0 &&
			volume < market.MinimumAmount)
			throw new InvalidOperationException(
				$"Coinmetro order amount {volume} is below the " +
					$"{market.MinimumAmount} minimum.");
		if (regMsg.VisibleVolume is > 0 &&
			regMsg.VisibleVolume != volume)
			throw new NotSupportedException(
				"Coinmetro does not document iceberg orders.");
		if (regMsg.PostOnly == true)
			throw new NotSupportedException(
				"Coinmetro does not document post-only orders.");

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (
			OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(
					orderType, regMsg.TransactionId));
		if (orderType == OrderTypes.Limit &&
			regMsg.Price <= 0)
			throw new InvalidOperationException(
				"Coinmetro limit orders require a positive price.");
		if (regMsg.TimeInForce is not (
			null or
			TimeInForce.PutInQueue or
			TimeInForce.MatchOrCancel or
			TimeInForce.CancelBalance))
			throw new NotSupportedException(
				"Coinmetro supports GTC, IOC, FOK and GTD only.");

		var result = await RestClient.PlaceOrderAsync(
			market,
			regMsg.Side,
			orderType,
			volume,
			regMsg.Price,
			regMsg.TimeInForce,
			regMsg.TillDate,
			GetMarkets(),
			cancellationToken);
		if (result?.Id.IsEmpty() != false)
			throw new InvalidDataException(
				"Coinmetro accepted an order without returning " +
					"its identifier.");
		TrackOrder(result.Id, new()
		{
			TransactionId = regMsg.TransactionId,
			Pair = market.Pair,
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
			GetMarkets(),
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
			"Coinmetro does not provide an atomic order-replace " +
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
				"Coinmetro spot cancellation cannot close positions.");
		var market = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(cancelMsg.SecurityId);

		foreach (var order in await RestClient.GetActiveOrdersAsync(
			GetMarkets(), cancellationToken) ?? [])
		{
			if (market is not null &&
				!order.Pair.EqualsIgnoreCase(market.Pair) ||
				cancelMsg.Side is Sides side &&
					order.Side != side)
				continue;
			var result = await RestClient.CancelOrderAsync(
				order.Id,
				GetMarkets(),
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
			lookupMsg.TransactionId, cancellationToken);

		EnsurePrivateReady();
		if (!lookupMsg.IsSubscribe)
		{
			if (_portfolioSubscriptionId ==
				lookupMsg.OriginalTransactionId)
			{
				_portfolioSubscriptionId = 0;
				await ReleasePrivateReferenceAsync(
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
					BoardCode = BoardCodes.Coinmetro,
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
				"Coinmetro portfolio subscription already exists.");
		_portfolioSubscriptionId = lookupMsg.TransactionId;
		try
		{
			await AddPrivateReferenceAsync(cancellationToken);
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
				await ReleasePrivateReferenceAsync(
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
				"Coinmetro order-status subscription already exists.");
		_orderStatusSubscriptionId = statusMsg.TransactionId;
		try
		{
			await AddPrivateReferenceAsync(cancellationToken);
			await SendSubscriptionResultAsync(
				statusMsg, cancellationToken);
		}
		catch
		{
			_orderStatusSubscriptionId = 0;
			throw;
		}
	}

	private async ValueTask AddPrivateReferenceAsync(
		CancellationToken cancellationToken)
	{
		var subscribe = false;
		using (_sync.EnterScope())
			subscribe = _privateReferences++ == 0;
		if (!subscribe)
			return;
		try
		{
			await WsClient.SubscribePrivateAsync(
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_privateReferences--;
			throw;
		}
	}

	private async ValueTask ReleasePrivateReferenceAsync(
		CancellationToken cancellationToken)
	{
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_privateReferences <= 0)
				return;
			unsubscribe = --_privateReferences == 0;
		}
		if (unsubscribe)
			await WsClient.UnsubscribePrivateAsync(
				cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshotAsync(
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		foreach (var wallet in await RestClient.GetWalletsAsync(
			cancellationToken) ?? [])
			await SendBalanceAsync(
				wallet,
				originalTransactionId,
				cancellationToken);
	}

	private ValueTask SendBalanceAsync(
		CoinmetroWallet wallet,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (wallet?.Currency.IsEmpty() != false)
			return default;
		return SendOutMessageAsync(
			new PositionChangeMessage
			{
				PortfolioName = GetPortfolioName(),
				SecurityId = new()
				{
					SecurityCode = wallet.Currency,
					BoardCode = BoardCodes.Coinmetro,
				},
				ServerTime = CurrentTime,
				OriginalTransactionId =
					originalTransactionId,
			}
			.TryAdd(
				PositionChangeTypes.CurrentValue,
				wallet.Available,
				true)
			.TryAdd(
				PositionChangeTypes.BlockedValue,
				wallet.Reserved,
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
				GetMarkets(),
				cancellationToken);
			if (MatchesOrder(order, statusMsg))
				await SendOrderAsync(
					order,
					statusMsg.TransactionId,
					cancellationToken);
		}
		else
		{
			var maximum = (statusMsg.Count ?? 1000)
				.Max(1).Min(10000).To<int>();

			foreach (var order in
				(await RestClient.GetActiveOrdersAsync(
					GetMarkets(),
					cancellationToken) ?? [])
				.Where(order =>
					MatchesOrder(order, statusMsg))
				.OrderBy(static order => order.CreatedAt)
				.TakeLast(maximum))
				await SendOrderAsync(
					order,
					statusMsg.TransactionId,
					cancellationToken);
		}

		var from = statusMsg.From?.ToUniversalTime();
		var to = statusMsg.To?.ToUniversalTime();
		var fills = await RestClient.GetFillsAsync(
			from, cancellationToken) ?? [];

		foreach (var fill in fills.Where(fill =>
			(from is null || fill.Time >= from) &&
			(to is null || fill.Time <= to) &&
			MatchesFill(fill, statusMsg)))
			await SendFillAsync(
				fill,
				statusMsg.TransactionId,
				cancellationToken);
	}

	private async ValueTask PollOrdersAsync(
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		foreach (var order in await RestClient.GetActiveOrdersAsync(
			GetMarkets(), cancellationToken) ?? [])
			await SendOrderAsync(
				order,
				originalTransactionId,
				cancellationToken);

		DateTime? from = _lastPrivatePoll == default
			? null
			: _lastPrivatePoll - PrivatePollingInterval;

		foreach (var fill in await RestClient.GetFillsAsync(
			from, cancellationToken) ?? [])
			await SendFillAsync(
				fill,
				originalTransactionId,
				cancellationToken);
	}

	private async ValueTask SendOrderAsync(
		CoinmetroOrder order,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (order?.Id.IsEmpty() != false ||
			order.Pair.IsEmpty())
			return;
		var market = GetMarket(order.Pair);
		if (market is null)
			return;
		var tracked = GetTrackedOrder(order.Id);
		tracked ??= new()
		{
			Pair = market.Pair,
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
			ServerTime = order.CompletedAt ??
				(order.CreatedAt == default
					? CurrentTime
					: order.CreatedAt),
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
			TransactionId = tracked.TransactionId,
			OriginalTransactionId = originalTransactionId,
			TimeInForce = order.TimeInForce,
			Commission = order.Fees > 0
				? order.Fees
				: null,
			CommissionCurrency = market.QuoteCurrency,
		};
		if (long.TryParse(
			order.Id,
			NumberStyles.None,
			CultureInfo.InvariantCulture,
			out var numericOrderId))
			execution.OrderId = numericOrderId;
		await SendOutMessageAsync(execution, cancellationToken);

		foreach (var fill in order.Fills)
			await SendFillAsync(
				fill,
				originalTransactionId,
				cancellationToken);
	}

	private ValueTask SendFillAsync(
		CoinmetroFill fill,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (fill?.Id.IsEmpty() != false ||
			fill.Pair.IsEmpty() ||
			!AddTrade(fill.Pair, fill.Id))
			return default;
		var market = GetMarket(fill.Pair);
		if (market is null)
			return default;
		var tracked = GetTrackedOrder(fill.OrderId);
		var execution = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = market.ToStockSharp(),
			ServerTime = fill.Time,
			PortfolioName = GetPortfolioName(),
			Side = fill.Side,
			OrderStringId = fill.OrderId,
			TradeStringId = fill.Id,
			TradePrice = fill.Price,
			TradeVolume = fill.Volume.Abs(),
			TransactionId = tracked?.TransactionId ?? 0,
			OriginalTransactionId = originalTransactionId,
		};
		if (long.TryParse(
			fill.OrderId,
			NumberStyles.None,
			CultureInfo.InvariantCulture,
			out var orderId))
			execution.OrderId = orderId;
		if (long.TryParse(
			fill.Id,
			NumberStyles.None,
			CultureInfo.InvariantCulture,
			out var tradeId))
			execution.TradeId = tradeId;
		return SendOutMessageAsync(execution, cancellationToken);
	}

	private bool MatchesOrder(
		CoinmetroOrder order,
		OrderStatusMessage filter)
	{
		if (order?.Id.IsEmpty() != false)
			return false;
		if (filter.From is DateTime from &&
			order.CreatedAt < from.ToUniversalTime() ||
			filter.To is DateTime to &&
			order.CreatedAt > to.ToUniversalTime())
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
				market.Pair.EqualsIgnoreCase(order.Pair));
	}

	private bool MatchesFill(
		CoinmetroFill fill,
		OrderStatusMessage filter)
	{
		if (fill is null)
			return false;
		if (filter.Side is Sides side &&
			fill.Side != side)
			return false;
		var markets = GetStatusMarkets(filter);
		return markets.Length == 0 ||
			markets.Any(market =>
				market.Pair.EqualsIgnoreCase(fill.Pair));
	}

	private CoinmetroMarket[] GetStatusMarkets(
		OrderStatusMessage filter)
	{
		var ids = new List<SecurityId>();
		if (!filter.SecurityId.SecurityCode.IsEmpty())
			ids.Add(filter.SecurityId);
		ids.AddRange(filter.SecurityIds.Where(
			static id => !id.SecurityCode.IsEmpty()));
		return [.. ids.Select(GetMarket).Distinct()];
	}

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
