namespace StockSharp.Bitvavo;

public partial class BitvavoMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var market = GetMarket(regMsg.SecurityId);
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException("Order volume must be positive.");
		var stockSharpType = regMsg.OrderType ?? OrderTypes.Limit;
		if (stockSharpType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(stockSharpType, 0));
		if (stockSharpType == OrderTypes.Limit && regMsg.Price <= 0)
			throw new InvalidOperationException("A positive limit price is required.");
		if (stockSharpType == OrderTypes.Market && regMsg.PostOnly == true)
			throw new InvalidOperationException("A market order cannot be post-only.");

		var condition = regMsg.Condition as BitvavoOrderCondition ??
			new BitvavoOrderCondition();
		if (condition.TriggerPrice is <= 0)
			throw new InvalidOperationException("Trigger price must be positive.");
		var orderType = condition.TriggerPrice is null
			? stockSharpType == OrderTypes.Market
				? BitvavoOrderTypes.Market
				: BitvavoOrderTypes.Limit
			: condition.IsTakeProfit
				? stockSharpType == OrderTypes.Market
					? BitvavoOrderTypes.TakeProfit
					: BitvavoOrderTypes.TakeProfitLimit
				: stockSharpType == OrderTypes.Market
					? BitvavoOrderTypes.StopLoss
					: BitvavoOrderTypes.StopLossLimit;
		var clientOrderId = CreateClientOrderId(regMsg.TransactionId,
			regMsg.UserOrderId);
		var result = await RestClient.PlaceOrderAsync(new()
		{
			Market = market,
			Side = regMsg.Side.ToBitvavo(),
			OrderType = orderType,
			OperatorId = OperatorId,
			ClientOrderId = clientOrderId,
			Amount = volume.ToWire(),
			Price = stockSharpType == OrderTypes.Limit ? regMsg.Price.ToWire() : null,
			TriggerAmount = condition.TriggerPrice?.ToWire(),
			TriggerType = condition.TriggerPrice is null
				? null
				: BitvavoTriggerTypes.Price,
			TriggerReference = condition.TriggerPrice is null
				? null
				: BitvavoTriggerReferences.LastTrade,
			TimeInForce = stockSharpType == OrderTypes.Limit
				? regMsg.TimeInForce.ToBitvavo()
				: null,
			IsPostOnly = stockSharpType == OrderTypes.Limit ? regMsg.PostOnly : null,
		}, cancellationToken);
		ValidateOrder(result, "accepted");
		result.ClientOrderId ??= clientOrderId;

		await SendOrderAsync(result, regMsg.TransactionId, regMsg.TransactionId,
			condition, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		if (replaceMsg.OrderType == OrderTypes.Market)
			throw new NotSupportedException(
				"Bitvavo can update active limit and trigger orders only.");
		if (replaceMsg.Price <= 0)
			throw new InvalidOperationException("Replacement price must be positive.");
		var volume = replaceMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException("Replacement volume must be positive.");

		var market = GetMarket(replaceMsg.SecurityId);
		var orderId = ResolveOrderId(replaceMsg.OldOrderId,
			replaceMsg.OldOrderStringId, "replacement");
		var condition = replaceMsg.Condition as BitvavoOrderCondition;
		if (condition?.TriggerPrice is <= 0)
			throw new InvalidOperationException("Trigger price must be positive.");
		var result = await RestClient.UpdateOrderAsync(new()
		{
			Market = market,
			OrderId = orderId,
			OperatorId = OperatorId,
			Amount = volume.ToWire(),
			Price = replaceMsg.Price.ToWire(),
			TriggerAmount = condition?.TriggerPrice?.ToWire(),
			TimeInForce = replaceMsg.TimeInForce.ToBitvavo(),
			IsPostOnly = replaceMsg.PostOnly,
		}, cancellationToken);
		ValidateOrder(result, "updated");

		await SendOrderAsync(result, replaceMsg.TransactionId,
			replaceMsg.TransactionId, condition, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var market = GetMarket(cancelMsg.SecurityId);
		var orderId = ResolveOrderId(cancelMsg.OrderId, cancelMsg.OrderStringId,
			"cancellation");
		var result = await RestClient.CancelOrderAsync(new()
		{
			Market = market,
			OrderId = orderId,
			OperatorId = OperatorId,
		}, cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = market.ToStockSharp(),
			ServerTime = CurrentTime,
			PortfolioName = GetPortfolioName(),
			OrderStringId = result?.OrderId ?? orderId,
			OrderState = OrderStates.Done,
			Balance = 0m,
			OriginalTransactionId = cancelMsg.TransactionId,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"Bitvavo spot bulk cancellation cannot close positions.");

		var market = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(cancelMsg.SecurityId);
		if (cancelMsg.Side is null)
		{
			await RestClient.CancelOrdersAsync(new()
			{
				Market = market,
				OperatorId = OperatorId,
			}, cancellationToken);
			return;
		}

		var orders = await RestClient.GetOpenOrdersAsync(new() { Market = market },
			cancellationToken);
		foreach (var order in (orders ?? []).Where(order =>
			order?.OrderId.IsEmpty() == false && order.Side is not null &&
			order.Side.Value.ToStockSharp() == cancelMsg.Side))
			await RestClient.CancelOrderAsync(new()
			{
				Market = order.Market,
				OrderId = order.OrderId,
				OperatorId = OperatorId,
			}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsurePrivateReady();
		if (!lookupMsg.IsSubscribe)
		{
			_portfolioSubscriptionId = 0;
			await ReleaseAccountSubscriptionAsync(cancellationToken);
			return;
		}

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = GetPortfolioName(),
			BoardCode = BoardCodes.Bitvavo,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId, cancellationToken);

		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
			return;
		}

		_portfolioSubscriptionId = lookupMsg.TransactionId;
		try
		{
			await EnsureAccountSubscriptionAsync(cancellationToken);
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		}
		catch
		{
			_portfolioSubscriptionId = 0;
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		EnsurePrivateReady();
		if (!statusMsg.IsSubscribe)
		{
			_orderStatusSubscriptionId = 0;
			await ReleaseAccountSubscriptionAsync(cancellationToken);
			return;
		}

		var market = statusMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(statusMsg.SecurityId);
		var limit = (statusMsg.Count ?? 1000).Min(10000).Max(1).To<int>();
		await SendOrderSnapshotAsync(statusMsg.TransactionId, market, statusMsg.From,
			statusMsg.To, limit, cancellationToken);

		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
			return;
		}

		_orderStatusSubscriptionId = statusMsg.TransactionId;
		try
		{
			await EnsureAccountSubscriptionAsync(cancellationToken);
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		}
		catch
		{
			_orderStatusSubscriptionId = 0;
			throw;
		}
	}

	private ValueTask EnsureAccountSubscriptionAsync(CancellationToken cancellationToken)
		=> WsClient.SubscribeAccountAsync(GetAllMarkets(), cancellationToken);

	private ValueTask ReleaseAccountSubscriptionAsync(CancellationToken cancellationToken)
		=> _portfolioSubscriptionId == 0 && _orderStatusSubscriptionId == 0 &&
			_wsClient is not null
			? _wsClient.UnsubscribeAccountAsync(cancellationToken)
			: default;

	private async ValueTask SendPortfolioSnapshotAsync(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var balances = await RestClient.GetBalancesAsync(new(), cancellationToken);
		foreach (var balance in balances ?? [])
			await SendBalanceAsync(balance, originalTransactionId, cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(long originalTransactionId,
		string market, DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
	{
		var openOrders = await RestClient.GetOpenOrdersAsync(new() { Market = market },
			cancellationToken);
		var historicalOrders = market.IsEmpty()
			? []
			: await LoadOrderHistoryAsync(market, from, to, limit, cancellationToken);
		var orders = (openOrders ?? []).Concat(historicalOrders)
			.Where(static order => order?.OrderId.IsEmpty() == false)
			.GroupBy(static order => order.OrderId, StringComparer.OrdinalIgnoreCase)
			.Select(group => group.OrderByDescending(GetOrderTime).First())
			.OrderBy(GetOrderTime)
			.TakeLast(limit);
		foreach (var order in orders)
			await SendOrderAsync(order, originalTransactionId, null, null,
				cancellationToken);

		if (!market.IsEmpty())
		{
			var fills = await LoadPrivateTradesAsync(market, from, to, limit,
				cancellationToken);
			foreach (var fill in fills.OrderBy(GetFillTime))
				await SendFillAsync(fill, originalTransactionId, false, cancellationToken);
		}
	}

	private async ValueTask<BitvavoOrder[]> LoadOrderHistoryAsync(string market,
		DateTime? from, DateTime? to, int maximum, CancellationToken cancellationToken)
	{
		var upperBound = (to ?? DateTime.UtcNow).ToUniversalTime();
		var lowerBound = from?.ToUniversalTime() ?? upperBound - TimeSpan.FromDays(1);
		var cursorEnd = upperBound;
		var result = new List<BitvavoOrder>();
		while (result.Count < maximum && cursorEnd >= lowerBound)
		{
			var windowStart = cursorEnd - TimeSpan.FromDays(1);
			if (windowStart < lowerBound)
				windowStart = lowerBound;
			var pageSize = (maximum - result.Count).Min(1000).Max(1);
			var page = await RestClient.GetOrdersAsync(new()
			{
				Market = market,
				Limit = pageSize,
				Start = windowStart.ToMilliseconds(),
				End = cursorEnd.ToMilliseconds(),
			}, cancellationToken);
			if (page is not { Length: > 0 })
			{
				cursorEnd = windowStart.AddMilliseconds(-1);
				continue;
			}
			result.AddRange(page.Where(order =>
			{
				var time = GetOrderTime(order);
				return time >= lowerBound && time <= upperBound;
			}));
			var earliest = page.Min(GetOrderTime);
			cursorEnd = page.Length >= pageSize && earliest > windowStart
				? earliest.AddMilliseconds(-1)
				: windowStart.AddMilliseconds(-1);
		}
		return [.. result.Where(static order => !order.OrderId.IsEmpty())
			.GroupBy(static order => order.OrderId, StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.First())
			.OrderBy(GetOrderTime)
			.TakeLast(maximum)];
	}

	private async ValueTask<BitvavoFill[]> LoadPrivateTradesAsync(string market,
		DateTime? from, DateTime? to, int maximum, CancellationToken cancellationToken)
	{
		var upperBound = (to ?? DateTime.UtcNow).ToUniversalTime();
		var lowerBound = from?.ToUniversalTime() ?? upperBound - TimeSpan.FromDays(1);
		var cursorEnd = upperBound;
		var result = new List<BitvavoFill>();
		while (result.Count < maximum && cursorEnd >= lowerBound)
		{
			var windowStart = cursorEnd - TimeSpan.FromDays(1);
			if (windowStart < lowerBound)
				windowStart = lowerBound;
			var pageSize = (maximum - result.Count).Min(1000).Max(1);
			var page = await RestClient.GetPrivateTradesAsync(new()
			{
				Market = market,
				Limit = pageSize,
				Start = windowStart.ToMilliseconds(),
				End = cursorEnd.ToMilliseconds(),
			}, cancellationToken);
			if (page is not { Length: > 0 })
			{
				cursorEnd = windowStart.AddMilliseconds(-1);
				continue;
			}
			result.AddRange(page.Where(fill =>
			{
				var time = GetFillTime(fill);
				return time >= lowerBound && time <= upperBound;
			}));
			var earliest = page.Min(GetFillTime);
			cursorEnd = page.Length >= pageSize && earliest > windowStart
				? earliest.AddMilliseconds(-1)
				: windowStart.AddMilliseconds(-1);
		}
		return [.. result.Where(fill => !fill.EffectiveId.IsEmpty())
			.GroupBy(fill => fill.EffectiveId, StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.First())
			.OrderBy(GetFillTime)
			.TakeLast(maximum)];
	}

	private async ValueTask OnOrderUpdateAsync(BitvavoOrder order,
		CancellationToken cancellationToken)
	{
		if (_orderStatusSubscriptionId != 0)
			await SendOrderAsync(order, _orderStatusSubscriptionId, null, null,
				cancellationToken);
	}

	private async ValueTask OnFillUpdateAsync(BitvavoFill fill,
		CancellationToken cancellationToken)
	{
		if (_orderStatusSubscriptionId != 0)
			await SendFillAsync(fill, _orderStatusSubscriptionId, true,
				cancellationToken);
		if (_portfolioSubscriptionId != 0)
			await SendPortfolioSnapshotAsync(_portfolioSubscriptionId, cancellationToken);
	}

	private ValueTask SendBalanceAsync(BitvavoBalance balance, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (balance?.Symbol.IsEmpty() != false)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(),
			SecurityId = balance.Symbol.ToStockSharp(),
			ServerTime = CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue,
			balance.Available + balance.InOrder, true)
		.TryAdd(PositionChangeTypes.BlockedValue, balance.InOrder, true),
			cancellationToken);
	}

	private ValueTask SendOrderAsync(BitvavoOrder order, long originalTransactionId,
		long? transactionId, BitvavoOrderCondition condition,
		CancellationToken cancellationToken)
	{
		if (order?.OrderId.IsEmpty() != false || order.Market.IsEmpty())
			return default;
		if (order.Side is null)
			throw new InvalidDataException(
				$"Bitvavo order '{order.OrderId}' has no side.");
		if (transactionId is > 0 && !order.ClientOrderId.IsEmpty())
			using (_sync.EnterScope())
				_transactionByClientOrderId[order.ClientOrderId] = transactionId.Value;
		condition ??= new BitvavoOrderCondition
		{
			TriggerPrice = order.TriggerAmount ?? order.TriggerPrice,
			IsTakeProfit = order.OrderType is BitvavoOrderTypes.TakeProfit or
				BitvavoOrderTypes.TakeProfitLimit,
		};
		var volume = order.Amount ?? order.QuoteAmount;
		var balance = order.Amount is not null
			? order.AmountRemaining
			: order.QuoteAmountRemaining;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Market.ToStockSharp(),
			ServerTime = GetOrderTime(order),
			PortfolioName = GetPortfolioName(),
			Side = order.Side.Value.ToStockSharp(),
			OrderVolume = volume,
			Balance = balance,
			OrderPrice = order.Price ?? 0m,
			AveragePrice = order.FilledAmount is > 0 && order.FilledQuoteAmount is decimal quote
				? quote / order.FilledAmount.Value
				: null,
			OrderType = order.OrderType.ToStockSharp(),
			OrderState = order.Status.ToStockSharp(),
			OrderStringId = order.OrderId,
			TransactionId = transactionId ?? GetTransactionId(order.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
			TimeInForce = order.TimeInForce.ToStockSharp(),
			PostOnly = order.IsPostOnly,
			Commission = order.FeePaid,
			CommissionCurrency = order.FeeCurrency,
			Condition = condition,
		}, cancellationToken);
	}

	private ValueTask SendFillAsync(BitvavoFill fill, long originalTransactionId,
		bool onlyNew, CancellationToken cancellationToken)
	{
		if (fill?.Market.IsEmpty() != false || fill.EffectiveId.IsEmpty())
			return default;
		using (_sync.EnterScope())
		{
			var added = _seenFillIds.Add(fill.EffectiveId);
			if (onlyNew && !added)
				return default;
		}
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = fill.Market.ToStockSharp(),
			ServerTime = GetFillTime(fill),
			PortfolioName = GetPortfolioName(),
			Side = fill.Side.ToStockSharp(),
			OrderStringId = fill.OrderId,
			TradeStringId = fill.EffectiveId,
			TradePrice = fill.Price,
			TradeVolume = fill.Amount,
			Commission = fill.Fee,
			CommissionCurrency = fill.FeeCurrency,
			TransactionId = GetTransactionId(fill.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private DateTime GetOrderTime(BitvavoOrder order)
		=> order.UpdatedNanoseconds is > 0
			? order.UpdatedNanoseconds.Value.FromNanoseconds()
			: order.CreatedNanoseconds is > 0
				? order.CreatedNanoseconds.Value.FromNanoseconds()
				: order.Updated is > 0
					? order.Updated.Value.FromMilliseconds()
					: order.Created is > 0
						? order.Created.Value.FromMilliseconds()
						: CurrentTime;

	private DateTime GetFillTime(BitvavoFill fill)
		=> fill.TimestampNanoseconds is > 0
			? fill.TimestampNanoseconds.Value.FromNanoseconds()
			: fill.Timestamp > 0 ? fill.Timestamp.FromMilliseconds() : CurrentTime;

	private static void ValidateOrder(BitvavoOrder order, string operation)
	{
		if (order?.OrderId.IsEmpty() != false)
			throw new InvalidDataException(
				$"Bitvavo {operation} the order without returning an order ID.");
	}

	private static string ResolveOrderId(long? numericOrderId, string stringOrderId,
		string operation)
	{
		if (!stringOrderId.IsEmpty())
			return stringOrderId;
		if (numericOrderId is > 0)
			return numericOrderId.Value.ToString(CultureInfo.InvariantCulture);
		throw new InvalidOperationException(
			$"Bitvavo {operation} requires an exchange order ID.");
	}
}
