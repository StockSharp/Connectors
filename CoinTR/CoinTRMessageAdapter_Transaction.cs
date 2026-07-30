namespace StockSharp.CoinTR;

public partial class CoinTRMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var market = GetMarket(regMsg.SecurityId);
		var condition = regMsg.Condition as CoinTROrderCondition;
		if (regMsg.VisibleVolume is > 0 &&
			regMsg.VisibleVolume != regMsg.Volume)
			throw new NotSupportedException(
				"CoinTR spot API does not document iceberg orders.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException(
				"CoinTR spot API does not document GTD orders.");
		string orderType;
		string triggerPrice = null;
		string triggerType = null;

		switch (regMsg.OrderType)
		{
			case null:
			case OrderTypes.Limit:
			case OrderTypes.Market:
				orderType = regMsg.OrderType.ToCoinTR();
				break;

			case OrderTypes.Conditional:
				if (condition?.TriggerPrice is not > 0)
					throw new InvalidOperationException(
						"CoinTR trigger price must be positive.");
				orderType = regMsg.Price > 0 ? "limit" : "market";
				triggerPrice = condition.TriggerPrice.Value.ToWire();
				triggerType = "tpsl";
				break;

			default:
				throw new NotSupportedException(
					LocalizedStrings.OrderUnsupportedType.Put(
						regMsg.OrderType, regMsg.TransactionId));
		}

		if (regMsg.Volume <= 0)
			throw new ArgumentOutOfRangeException(
				nameof(regMsg.Volume), regMsg.Volume,
				"CoinTR order size must be positive.");
		if (orderType == "limit" && regMsg.Price <= 0)
			throw new ArgumentOutOfRangeException(
				nameof(regMsg.Price), regMsg.Price,
				"CoinTR limit-order price must be positive.");
		if (regMsg.PostOnly == true && orderType != "limit")
			throw new NotSupportedException(
				"CoinTR post-only execution is available only for " +
					"limit orders.");

		var clientOrderId = CoinTRExtensions.CreateClientOrderId(
			regMsg.TransactionId, regMsg.UserOrderId);
		var result = await RestClient.PlaceOrderAsync(
			new CoinTRPlaceOrderRequest
			{
				Symbol = market.Symbol,
				Side = regMsg.Side.ToCoinTR(),
				OrderType = orderType,
				Force = regMsg.PostOnly == true
					? "post_only"
					: regMsg.TimeInForce.ToCoinTR(),
				Price = orderType == "limit"
					? regMsg.Price.ToWire()
					: null,
				Size = regMsg.Volume.ToWire(),
				ClientOrderId = clientOrderId,
				TriggerPrice = triggerPrice,
				TriggerType = triggerType,
			},
			cancellationToken);
		if (result?.OrderId.IsEmpty() != false)
			throw new InvalidDataException(
				"CoinTR returned no order identifier.");

		using (_sync.EnterScope())
		{
			_orderTransactions[result.OrderId] = regMsg.TransactionId;
			_orderSymbols[result.OrderId] = market.Symbol;
		}
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
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
		var market = GetMarket(cancelMsg.SecurityId);
		var orderId = ResolveOrderId(
			cancelMsg.OrderId, cancelMsg.OrderStringId);
		await RestClient.CancelOrderAsync(
			market.Symbol, orderId, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		_ = replaceMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"CoinTR spot API does not provide an atomic order-replace " +
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
				"CoinTR spot bulk cancellation cannot close positions.");
		var symbol = cancelMsg.SecurityId == default
			? null
			: GetMarket(cancelMsg.SecurityId).Symbol;
		var orders = await RestClient.GetOpenOrdersAsync(
			symbol, cancellationToken) ?? [];

		foreach (var group in orders
			.Where(order => !order.Symbol.IsEmpty() &&
				!order.OrderId.IsEmpty() &&
				(cancelMsg.Side is null ||
					order.Side.ToSide() == cancelMsg.Side))
			.GroupBy(order => order.Symbol,
				StringComparer.OrdinalIgnoreCase))
		{
			var ids = group.Select(order => order.OrderId).ToArray();

			for (var index = 0; index < ids.Length; index += 50)
				await RestClient.BatchCancelOrdersAsync(
					group.Key, ids.Skip(index).Take(50),
					cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
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
			{
				await WsClient.UnsubscribeBalancesAsync(
					cancellationToken);
				_portfolioSubscriptionId = 0;
			}
			return;
		}

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = portfolioName,
			BoardCode = BoardCodes.CoinTR,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await SendBalancesAsync(
			await RestClient.GetAssetsAsync(cancellationToken) ?? [],
			lookupMsg.TransactionId, cancellationToken);

		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(lookupMsg,
				cancellationToken);
			await SendSubscriptionFinishedAsync(
				lookupMsg.TransactionId, cancellationToken);
			return;
		}

		if (_portfolioSubscriptionId != 0)
			throw new InvalidOperationException(
				"CoinTR portfolio subscription already exists.");
		_portfolioSubscriptionId = lookupMsg.TransactionId;
		try
		{
			await WsClient.SubscribeBalancesAsync(cancellationToken);
			await SendSubscriptionResultAsync(lookupMsg,
				cancellationToken);
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
		await SendSubscriptionReplyAsync(statusMsg.TransactionId,
			cancellationToken);

		EnsurePrivateReady();
		if (!statusMsg.IsSubscribe)
		{
			if (_orderStatusSubscriptionId ==
				statusMsg.OriginalTransactionId)
			{
				await WsClient.UnsubscribeFillsAsync(cancellationToken);
				await WsClient.UnsubscribeOrdersAsync(cancellationToken);
				_orderStatusSubscriptionId = 0;
			}
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
		CoinTROrder[] orders;
		if (statusMsg.OrderId is not null ||
			!statusMsg.OrderStringId.IsEmpty())
		{
			orders = await RestClient.GetOrderAsync(
				ResolveOrderId(
					statusMsg.OrderId, statusMsg.OrderStringId),
				cancellationToken) ?? [];
		}
		else
		{
			var symbol = market?.Symbol;
			var openOrders = await RestClient.GetOpenOrdersAsync(
				symbol, cancellationToken) ?? [];
			if (statusMsg.IsHistoryOnly() ||
				statusMsg.From is not null ||
				statusMsg.To is not null)
			{
				var history = await RestClient.GetHistoryOrdersAsync(
					symbol, statusMsg.From, statusMsg.To,
					(statusMsg.Count ?? 100).Min(100).Max(1).To<int>(),
					cancellationToken) ?? [];
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
		orders = [.. orders.Where(order =>
			(statusMsg.Side is null ||
				order.Side.ToSide() == statusMsg.Side) &&
			(from is null || GetOrderTime(order) >= from.Value) &&
			(to is null || GetOrderTime(order) <= to.Value))
			.OrderBy(GetOrderTime)
			.TakeLast((statusMsg.Count ?? 100).Min(100).Max(1)
				.To<int>())];
		await SendOrdersAsync(
			orders, statusMsg.TransactionId, cancellationToken);
		if (market is not null &&
			(statusMsg.IsHistoryOnly() || statusMsg.From is not null))
			await SendFillsAsync(
				await RestClient.GetFillsAsync(
					market.Symbol, statusMsg.From, statusMsg.To,
					cancellationToken) ?? [],
				statusMsg.TransactionId, cancellationToken);

		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(statusMsg,
				cancellationToken);
			await SendSubscriptionFinishedAsync(
				statusMsg.TransactionId, cancellationToken);
			return;
		}

		if (_orderStatusSubscriptionId != 0)
			throw new InvalidOperationException(
				"CoinTR order-status subscription already exists.");
		_orderStatusSubscriptionId = statusMsg.TransactionId;
		try
		{
			await WsClient.SubscribeOrdersAsync(cancellationToken);
			await WsClient.SubscribeFillsAsync(cancellationToken);
			await SendSubscriptionResultAsync(statusMsg,
				cancellationToken);
		}
		catch
		{
			_orderStatusSubscriptionId = 0;
			throw;
		}
	}

	private ValueTask OnWebSocketBalancesAsync(CoinTRBalance[] balances,
		CancellationToken cancellationToken)
		=> SendBalancesAsync(balances, _portfolioSubscriptionId,
			cancellationToken);

	private ValueTask OnWebSocketOrdersAsync(CoinTROrder[] orders,
		CancellationToken cancellationToken)
		=> SendOrdersAsync(orders, _orderStatusSubscriptionId,
			cancellationToken);

	private async ValueTask OnWebSocketFillsAsync(CoinTRFill[] fills,
		CancellationToken cancellationToken)
		=> await SendFillsAsync(
			fills, _orderStatusSubscriptionId, cancellationToken);

	private async ValueTask SendFillsAsync(
		IEnumerable<CoinTRFill> fills, long transactionId,
		CancellationToken cancellationToken)
	{
		foreach (var fill in fills ?? [])
		{
			if (fill?.Symbol.IsEmpty() != false ||
				fill.TradeId.IsEmpty() ||
				!AddTrade(fill.Symbol, fill.TradeId, true))
				continue;
			var market = GetMarket(fill.Symbol);
			if (market is null)
				continue;
			var fee = GetFee(fill.FeeDetail);
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				SecurityId = market.ToStockSharp(),
				ServerTime = (fill.UpdateTime > 0
					? fill.UpdateTime
					: fill.CreateTime).FromCoinTRTime(),
				OriginalTransactionId = transactionId,
				OrderStringId = fill.OrderId,
				TradeStringId = fill.TradeId,
				TradePrice = fill.AveragePrice,
				TradeVolume = fill.Size,
				OriginSide = fill.Side.ToSide(),
				PortfolioName = GetPortfolioName(),
				Commission = fee.Fee,
				CommissionCurrency = fee.Currency,
			}, cancellationToken);
		}
	}

	private static DateTime GetOrderTime(CoinTROrder order)
	{
		var timestamp = order?.UpdateTime > 0
			? order.UpdateTime
			: order?.CreateTime ?? 0;
		return timestamp > 0
			? timestamp.FromCoinTRTime()
			: DateTime.MinValue;
	}

	private async ValueTask SendBalancesAsync(
		IEnumerable<CoinTRBalance> balances, long transactionId,
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
					SecurityCode = balance.Coin.ToUpperInvariant(),
					BoardCode = BoardCodes.CoinTR,
				},
				ServerTime = balance.UpdateTime > 0
					? balance.UpdateTime.FromCoinTRTime()
					: CurrentTime,
				OriginalTransactionId = transactionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue,
				balance.Available, true)
			.TryAdd(PositionChangeTypes.BlockedValue,
				balance.Frozen + balance.Locked, true),
				cancellationToken);
		}
	}

	private async ValueTask SendOrdersAsync(
		IEnumerable<CoinTROrder> orders, long transactionId,
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
				CoinTRExtensions.ParseTransactionId(
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
				SecurityId = market.ToStockSharp(),
				ServerTime = (order.UpdateTime > 0
					? order.UpdateTime
					: order.CreateTime) is var timestamp && timestamp > 0
						? timestamp.FromCoinTRTime()
						: CurrentTime,
				TransactionId = ownTransactionId,
				OriginalTransactionId = transactionId,
				OrderStringId = order.OrderId,
				OrderType = order.ToOrderType(),
				OrderPrice = order.Price ??
					order.AveragePrice ?? 0,
				OrderVolume = order.Size,
				Balance = order.Size is decimal size &&
					!(order.OrderType.EqualsIgnoreCase("market") &&
						order.Side.EqualsIgnoreCase("buy"))
					? (size - (order.BaseVolume ?? 0)).Max(0)
					: null,
				Side = order.Side.ToSide(),
				TimeInForce = order.Force.ToTimeInForce(),
				PortfolioName = GetPortfolioName(),
				OrderState = order.Status.ToOrderState(),
			};
			if (long.TryParse(order.OrderId, NumberStyles.None,
				CultureInfo.InvariantCulture, out var numericOrderId))
				execution.OrderId = numericOrderId;
			await SendOutMessageAsync(execution, cancellationToken);
		}
	}

	private static (decimal? Fee, string Currency) GetFee(
		JToken feeDetail)
	{
		if (feeDetail is null)
			return default;
		var candidates = feeDetail.Type == JTokenType.Array
			? feeDetail.Children()
			: feeDetail["feeCoin"] is not null
				? [feeDetail]
				: feeDetail.Children<JProperty>()
					.Select(static property => property.Value);

		foreach (var candidate in candidates)
		{
			var fee = candidate["totalFee"]?.Value<decimal?>();
			var currency = (string)(candidate["feeCoin"] ??
				candidate["feeCoinCode"]);
			if (fee is not null || !currency.IsEmpty())
				return (fee, currency);
		}

		return default;
	}
}
