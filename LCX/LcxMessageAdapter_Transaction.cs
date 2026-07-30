namespace StockSharp.LCX;

public partial class LcxMessageAdapter
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
				"LCX order volume must be positive.");
		if (market.MinimumAmount > 0 &&
			volume < market.MinimumAmount)
			throw new InvalidOperationException(
				$"LCX order amount {volume} is below the " +
					$"{market.MinimumAmount} minimum.");
		if (market.MaximumAmount > 0 &&
			volume > market.MaximumAmount)
			throw new InvalidOperationException(
				$"LCX order amount {volume} exceeds the " +
					$"{market.MaximumAmount} maximum.");
		if (regMsg.VisibleVolume is > 0 &&
			regMsg.VisibleVolume != volume)
			throw new NotSupportedException(
				"LCX does not document iceberg orders.");
		if (regMsg.PostOnly == true)
			throw new NotSupportedException(
				"LCX does not document post-only orders.");

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (
			OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(
					orderType, regMsg.TransactionId));
		if (orderType == OrderTypes.Limit &&
			regMsg.Price <= 0)
			throw new InvalidOperationException(
				"LCX limit orders require a positive price.");
		if (regMsg.TimeInForce is not (
			null or TimeInForce.PutInQueue))
			throw new NotSupportedException(
				"LCX spot orders support GTC only.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException(
				"LCX does not document expiring spot orders.");

		var clientOrderId = CreateClientOrderId(
			regMsg.TransactionId, regMsg.UserOrderId);
		var result = await RestClient.PlaceOrderAsync(
			market,
			regMsg.Side,
			orderType,
			volume,
			regMsg.Price,
			clientOrderId,
			cancellationToken);
		if (result?.Id.IsEmpty() != false)
			throw new InvalidDataException(
				"LCX accepted an order without returning its " +
					"identifier.");
		TrackOrder(result.Id, new()
		{
			TransactionId = regMsg.TransactionId,
			Symbol = market.Symbol,
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
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var orderId = ResolveOrderId(
			replaceMsg.OldOrderId,
			replaceMsg.OldOrderStringId);
		var volume = replaceMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException(
				"LCX replacement volume must be positive.");
		if (replaceMsg.Price <= 0)
			throw new InvalidOperationException(
				"LCX replacement price must be positive.");
		var result = await RestClient.ModifyOrderAsync(
			orderId,
			volume,
			replaceMsg.Price,
			cancellationToken);
		if (result?.Id.IsEmpty() != false)
			throw new InvalidDataException(
				"LCX modified an order without returning its " +
					"identifier.");
		var tracked = GetTrackedOrder(orderId);
		if (tracked is not null)
			TrackOrder(result.Id, new()
			{
				TransactionId = replaceMsg.TransactionId,
				Symbol = tracked.Symbol,
				Side = tracked.Side,
				OrderType = tracked.OrderType,
				Volume = volume,
				Price = replaceMsg.Price,
			});
		await SendOrderAsync(
			result,
			replaceMsg.TransactionId,
			cancellationToken);
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
				"LCX spot cancellation cannot close positions.");
		var markets = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? []
			: new[] { GetMarket(cancelMsg.SecurityId) };
		var pair = markets.Length == 1
			? markets[0].Symbol
			: null;

		foreach (var order in
			await RestClient.GetOpenOrdersAsync(
				pair, null, null, cancellationToken) ?? [])
		{
			if (cancelMsg.Side is Sides side &&
				order.Side != side)
				continue;
			var result = await RestClient.CancelOrderAsync(
				order.Id, cancellationToken);
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
				await ReleasePrivateStreamAsync(
					"user_wallets", cancellationToken);
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
					BoardCode = BoardCodes.Lcx,
					OriginalTransactionId =
						lookupMsg.TransactionId,
				},
				cancellationToken);
			await SendPortfolioSnapshotAsync(
				lookupMsg.TransactionId, cancellationToken);
		}
		if (lookupMsg.IsHistoryOnly())
		{
			await CompletePortfolioLookupAsync(
				lookupMsg, cancellationToken);
			return;
		}

		if (_portfolioSubscriptionId != 0)
			throw new InvalidOperationException(
				"LCX portfolio subscription already exists.");
		_portfolioSubscriptionId = lookupMsg.TransactionId;
		try
		{
			await AddPrivateStreamAsync(
				"user_wallets", cancellationToken);
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
				await ReleasePrivateStreamAsync(
					"user_orders", cancellationToken);
				await ReleasePrivateStreamAsync(
					"user_trades", cancellationToken);
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
			await CompleteOrderStatusAsync(
				statusMsg, cancellationToken);
			return;
		}

		if (_orderStatusSubscriptionId != 0)
			throw new InvalidOperationException(
				"LCX order-status subscription already exists.");
		_orderStatusSubscriptionId = statusMsg.TransactionId;
		try
		{
			await AddPrivateStreamAsync(
				"user_orders", cancellationToken);
			try
			{
				await AddPrivateStreamAsync(
					"user_trades", cancellationToken);
			}
			catch
			{
				await ReleasePrivateStreamAsync(
					"user_orders", cancellationToken);
				throw;
			}
			await SendSubscriptionResultAsync(
				statusMsg, cancellationToken);
		}
		catch
		{
			_orderStatusSubscriptionId = 0;
			throw;
		}
	}

	private async ValueTask AddPrivateStreamAsync(
		string type,
		CancellationToken cancellationToken)
	{
		var stream = "private:" + type;
		var subscribe = AddReference(stream);
		try
		{
			if (subscribe)
				await WsClient.SubscribeAsync(
					type, null, true, cancellationToken);
		}
		catch
		{
			ReleaseReference(stream);
			throw;
		}
	}

	private async ValueTask ReleasePrivateStreamAsync(
		string type,
		CancellationToken cancellationToken)
	{
		var stream = "private:" + type;
		if (ReleaseReference(stream))
			await WsClient.UnsubscribeAsync(
				type, null, true, cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshotAsync(
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		foreach (var balance in
			await RestClient.GetBalancesAsync(
				cancellationToken) ?? [])
			await SendBalanceAsync(
				balance,
				originalTransactionId,
				cancellationToken);
	}

	private ValueTask SendBalanceAsync(
		LcxBalance balance,
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
					BoardCode = BoardCodes.Lcx,
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
		}
		else
		{
			var orders = new Dictionary<string, LcxOrder>(
				StringComparer.OrdinalIgnoreCase);

			foreach (var symbol in GetStatusSymbols(statusMsg))
			{
				foreach (var order in
					await RestClient.GetOpenOrdersAsync(
						symbol,
						statusMsg.From?.ToUniversalTime(),
						statusMsg.To?.ToUniversalTime(),
						cancellationToken) ?? [])
					orders[order.Id] = order;

				foreach (var order in
					await RestClient.GetOrderHistoryAsync(
						symbol,
						statusMsg.From?.ToUniversalTime(),
						statusMsg.To?.ToUniversalTime(),
						cancellationToken) ?? [])
					orders[order.Id] = order;
			}

			var maximum = (statusMsg.Count ?? 1000)
				.Max(1).Min(10000).To<int>();

			foreach (var order in orders.Values
				.Where(order => MatchesOrder(order, statusMsg))
				.OrderBy(static order => order.CreatedAt)
				.TakeLast(maximum))
				await SendOrderAsync(
					order,
					statusMsg.TransactionId,
					cancellationToken);
		}

		foreach (var symbol in GetStatusSymbols(statusMsg))
		{
			foreach (var trade in
				await RestClient.GetUserTradesAsync(
					symbol,
					statusMsg.From?.ToUniversalTime(),
					statusMsg.To?.ToUniversalTime(),
					cancellationToken) ?? [])
			{
				if (MatchesTrade(trade, statusMsg))
					await SendUserTradeAsync(
						trade,
						statusMsg.TransactionId,
						cancellationToken);
			}
		}
	}

	private async ValueTask PollOrdersAsync(
		long originalTransactionId,
		DateTime from,
		CancellationToken cancellationToken)
	{
		DateTime? since = from == default
			? null
			: from.ToUniversalTime();

		foreach (var order in
			await RestClient.GetOpenOrdersAsync(
				null, since, null, cancellationToken) ?? [])
			await SendOrderAsync(
				order,
				originalTransactionId,
				cancellationToken);

		foreach (var order in
			await RestClient.GetOrderHistoryAsync(
				null, since, null, cancellationToken) ?? [])
			await SendOrderAsync(
				order,
				originalTransactionId,
				cancellationToken);

		foreach (var trade in
			await RestClient.GetUserTradesAsync(
				null, since, null, cancellationToken) ?? [])
			await SendUserTradeAsync(
				trade,
				originalTransactionId,
				cancellationToken);
	}

	private ValueTask SendOrderAsync(
		LcxOrder order,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (order?.Id.IsEmpty() != false ||
			order.Symbol.IsEmpty())
			return default;
		var market = GetMarket(order.Symbol);
		if (market is null)
			return default;
		var tracked = GetTrackedOrder(order.Id);
		tracked ??= new()
		{
			Symbol = market.Symbol,
			Side = order.Side,
			OrderType = order.OrderType,
			Volume = order.Amount,
			Price = order.Price,
		};
		TrackOrder(order.Id, tracked);
		var execution = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = order.UpdatedAt == default
				? order.CreatedAt == default
					? CurrentTime
					: order.CreatedAt
				: order.UpdatedAt,
			PortfolioName = GetPortfolioName(),
			Side = order.Side,
			OrderVolume = order.Amount > 0
				? order.Amount
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
			TimeInForce = TimeInForce.PutInQueue,
			Commission = order.Fee > 0
				? order.Fee
				: null,
		};
		if (long.TryParse(
			order.Id,
			NumberStyles.None,
			CultureInfo.InvariantCulture,
			out var numericOrderId))
			execution.OrderId = numericOrderId;
		return SendOutMessageAsync(execution, cancellationToken);
	}

	private ValueTask SendUserTradeAsync(
		LcxUserTrade trade,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (trade?.Id.IsEmpty() != false ||
			trade.Symbol.IsEmpty() ||
			!AddTrade("private", trade.Id))
			return default;
		var market = GetMarket(trade.Symbol);
		if (market is null)
			return default;
		var tracked = GetTrackedOrder(trade.OrderId);
		var execution = new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = market.ToStockSharp(),
			ServerTime = trade.Time == default
				? CurrentTime
				: trade.Time,
			PortfolioName = GetPortfolioName(),
			Side = trade.Side,
			OrderStringId = trade.OrderId,
			TradeStringId = trade.Id,
			TradePrice = trade.Price,
			TradeVolume = trade.Volume.Abs(),
			TransactionId = tracked?.TransactionId ?? 0,
			OriginalTransactionId = originalTransactionId,
			Commission = trade.Fee > 0
				? trade.Fee
				: null,
			CommissionCurrency = trade.FeeCurrency,
		};
		if (long.TryParse(
			trade.OrderId,
			NumberStyles.None,
			CultureInfo.InvariantCulture,
			out var orderId))
			execution.OrderId = orderId;
		if (long.TryParse(
			trade.Id,
			NumberStyles.None,
			CultureInfo.InvariantCulture,
			out var tradeId))
			execution.TradeId = tradeId;
		return SendOutMessageAsync(execution, cancellationToken);
	}

	private bool MatchesOrder(
		LcxOrder order,
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
			order.Amount != volume)
			return false;
		if (!filter.PortfolioName.IsEmpty() &&
			!filter.PortfolioName.EqualsIgnoreCase(
				GetPortfolioName()))
			return false;
		var markets = GetStatusMarkets(filter);
		return markets.Length == 0 ||
			markets.Any(market =>
				market.Symbol.EqualsIgnoreCase(order.Symbol));
	}

	private bool MatchesTrade(
		LcxUserTrade trade,
		OrderStatusMessage filter)
	{
		if (trade is null)
			return false;
		if (filter.From is DateTime from &&
			trade.Time < from.ToUniversalTime() ||
			filter.To is DateTime to &&
			trade.Time > to.ToUniversalTime())
			return false;
		if (filter.Side is Sides side &&
			trade.Side != side)
			return false;
		var markets = GetStatusMarkets(filter);
		return markets.Length == 0 ||
			markets.Any(market =>
				market.Symbol.EqualsIgnoreCase(trade.Symbol));
	}

	private string[] GetStatusSymbols(
		OrderStatusMessage filter)
	{
		var markets = GetStatusMarkets(filter);
		return markets.Length == 0
			? [null]
			: [.. markets.Select(static market => market.Symbol)];
	}

	private LcxMarket[] GetStatusMarkets(
		OrderStatusMessage filter)
	{
		var ids = new List<SecurityId>();
		if (!filter.SecurityId.SecurityCode.IsEmpty())
			ids.Add(filter.SecurityId);
		ids.AddRange(filter.SecurityIds.Where(
			static id => !id.SecurityCode.IsEmpty()));
		return [.. ids.Select(GetMarket).Distinct()];
	}

	private async ValueTask CompletePortfolioLookupAsync(
		PortfolioLookupMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(
			message, cancellationToken);
		await SendSubscriptionFinishedAsync(
			message.TransactionId, cancellationToken);
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

	private static string CreateClientOrderId(
		long transactionId,
		string userOrderId)
	{
		if (!userOrderId.IsEmpty())
		{
			if (!Guid.TryParseExact(
				userOrderId, "D", out var supplied))
				throw new InvalidOperationException(
					"LCX client order ID must be a UUID.");
			return supplied.ToString("D");
		}
		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
			$"StockSharp-LCX-{transactionId}"));
		var bytes = hash[..16];
		bytes[6] = (byte)((bytes[6] & 0x0f) | 0x40);
		bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
		return new Guid(bytes).ToString("D");
	}
}
