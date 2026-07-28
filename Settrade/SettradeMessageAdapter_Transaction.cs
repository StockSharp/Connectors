namespace StockSharp.Settrade;

public partial class SettradeMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsureConnected();
		var account = ResolveAccount(regMsg.PortfolioName);
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not OrderTypes.Limit and not OrderTypes.Market
			and not OrderTypes.Conditional)
			throw new NotSupportedException(
				$"Settrade order type '{orderType}' is unsupported.");
		if (orderType != OrderTypes.Market && regMsg.Price <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Price),
				regMsg.Price, "Settrade limit price must be positive.");
		if (Pin.IsEmpty())
			throw new InvalidOperationException(
				"Settrade trading PIN is not specified.");
		var quantity = ToQuantity(regMsg.Volume, nameof(regMsg.Volume),
			false);
		var condition = regMsg.Condition as SettradeOrderCondition;
		if (AccountType == SettradeAccountTypes.Equity &&
			orderType == OrderTypes.Conditional)
			throw new NotSupportedException(
				"Settrade equity investor API does not expose stop orders.");
		if (AccountType == SettradeAccountTypes.Derivatives &&
			orderType == OrderTypes.Conditional &&
			(condition?.StopCondition is null or
				SettradeStopConditions.None ||
				condition.StopPrice is not > 0))
			throw new InvalidOperationException(
				"A Settrade derivatives stop order requires a trigger " +
					"condition and positive stop price.");
		var priceType = orderType == OrderTypes.Market
			? "MP-MKT"
			: "Limit";
		var body = AccountType == SettradeAccountTypes.Equity
			? CreateEquityOrder(regMsg, condition, quantity, priceType)
			: CreateDerivativeOrder(regMsg, condition, quantity,
				priceType);
		var response = await RestClient.PlaceOrderAsync(account,
			AccountType, body, cancellationToken);
		var orderNo = FindString(response, "orderNo", "order_no", "id")
			.ThrowIfEmpty("order number");
		using (_sync.EnterScope())
			_orderTransactions[orderNo] = regMsg.TransactionId;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			OrderStringId = orderNo,
			SecurityId = Normalize(regMsg.SecurityId),
			PortfolioName = account,
			OrderType = orderType,
			Side = regMsg.Side,
			TimeInForce = regMsg.TimeInForce ??
				TimeInForce.PutInQueue,
			OrderPrice = regMsg.Price,
			OrderVolume = regMsg.Volume,
			Balance = regMsg.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = CurrentTime,
			Condition = condition,
		}, cancellationToken);
	}

	private JObject CreateEquityOrder(OrderRegisterMessage regMsg,
		SettradeOrderCondition condition, long quantity,
		string priceType)
	{
		var body = new JObject
		{
			["pin"] = Pin.UnSecure(),
			["side"] = regMsg.Side == Sides.Buy ? "Buy" : "Sell",
			["symbol"] = regMsg.SecurityId.SecurityCode,
			["trusteeIdType"] = condition?.IsNvdr == true
				? "NVDR"
				: "Local",
			["volume"] = quantity,
			["qtyOpen"] = ToQuantity(
				condition?.IcebergVolume ?? 0,
				nameof(condition.IcebergVolume), true),
			["price"] = priceType == "Limit" ? regMsg.Price : 0,
			["priceType"] = priceType,
			["validityType"] = regMsg.TimeInForce
				.ToSettradeValidity(condition?.ValidTillDate),
			["clientType"] = "Individual",
			["bypassWarning"] = condition?.BypassWarning,
			["validTillDate"] = condition?.ValidTillDate?
				.ToUniversalTime().ToString("yyyy-MM-dd",
					CultureInfo.InvariantCulture),
		};
		RemoveNulls(body);
		return body;
	}

	private JObject CreateDerivativeOrder(OrderRegisterMessage regMsg,
		SettradeOrderCondition condition, long quantity,
		string priceType)
	{
		var body = new JObject
		{
			["pin"] = Pin.UnSecure(),
			["symbol"] = regMsg.SecurityId.SecurityCode,
			["side"] = regMsg.Side == Sides.Buy ? "Long" : "Short",
			["position"] = (condition?.Position ??
				SettradeOrderPositions.Auto).ToString(),
			["priceType"] = priceType,
			["price"] = priceType == "Limit" ? regMsg.Price : 0,
			["volume"] = quantity,
			["icebergVol"] = condition?.IcebergVolume is decimal iceberg
				? ToQuantity(iceberg, nameof(condition.IcebergVolume),
					true)
				: null,
			["validityType"] = regMsg.TimeInForce
				.ToSettradeValidity(condition?.ValidTillDate),
			["validityDateCondition"] = condition?.ValidTillDate?
				.ToUniversalTime().ToString("yyyy-MM-dd",
					CultureInfo.InvariantCulture),
			["stopCondition"] = ToStopCondition(
				condition?.StopCondition ??
					SettradeStopConditions.None),
			["stopSymbol"] = condition?.StopSymbol,
			["stopPrice"] = condition?.StopPrice,
			["triggerSession"] = condition?.TriggerSession,
			["bypassWarning"] = condition?.BypassWarning,
		};
		RemoveNulls(body);
		return body;
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		EnsureConnected();
		if (Pin.IsEmpty())
			throw new InvalidOperationException(
				"Settrade trading PIN is not specified.");
		var account = ResolveAccount(replaceMsg.PortfolioName);
		var orderNo = replaceMsg.OldOrderStringId;
		if (orderNo.IsEmpty())
			throw new InvalidOperationException(
				LocalizedStrings.OrderNoExchangeId.Put(
					replaceMsg.OriginalTransactionId));
		var condition = replaceMsg.Condition as SettradeOrderCondition;
		var body = new JObject
		{
			["newPrice"] = replaceMsg.OrderType == OrderTypes.Market
				? 0
				: replaceMsg.Price,
			["newVolume"] = ToQuantity(replaceMsg.Volume,
				nameof(replaceMsg.Volume), false),
			["bypassWarning"] = condition?.BypassWarning,
		};
		if (AccountType == SettradeAccountTypes.Equity)
		{
			body["pin"] = Pin.UnSecure();
			body["newTrusteeIdType"] =
				condition?.IsNvdr == true ? "NVDR" : "Local";
			body["newIcebergVolume"] =
				condition?.IcebergVolume is decimal iceberg
					? ToQuantity(iceberg,
						nameof(condition.IcebergVolume), true)
					: null;
		}
		else
			body["pin"] = Pin.UnSecure();
		RemoveNulls(body);
		await RestClient.ChangeOrderAsync(account, AccountType,
			orderNo, body, cancellationToken);
		using (_sync.EnterScope())
			_orderTransactions[orderNo] = replaceMsg.TransactionId;
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsureConnected();
		if (Pin.IsEmpty())
			throw new InvalidOperationException(
				"Settrade trading PIN is not specified.");
		var account = ResolveAccount(cancelMsg.PortfolioName);
		var orderNo = cancelMsg.OrderStringId;
		if (orderNo.IsEmpty())
			throw new InvalidOperationException(
				LocalizedStrings.OrderNoExchangeId.Put(
					cancelMsg.OriginalTransactionId));
		await RestClient.CancelOrderAsync(account, AccountType,
			orderNo, Pin, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(
		OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		var account = ResolveAccount(statusMsg.PortfolioName);
		if (!statusMsg.IsSubscribe)
		{
			var removed = false;
			using (_sync.EnterScope())
				removed = _orderSubscriptions.Remove(
					statusMsg.OriginalTransactionId);
			if (removed)
				await UnsubscribeTopicAsync(OrderTopic(account),
					cancellationToken);
			return;
		}
		await SendOrderSnapshotAsync(account, statusMsg.TransactionId,
			statusMsg, cancellationToken);
		if (!statusMsg.IsHistoryOnly())
		{
			using (_sync.EnterScope())
				_orderSubscriptions.Add(statusMsg.TransactionId);
			try
			{
				await SubscribeTopicAsync(OrderTopic(account),
					cancellationToken);
			}
			catch
			{
				using (_sync.EnterScope())
					_orderSubscriptions.Remove(
						statusMsg.TransactionId);
				throw;
			}
		}
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!lookupMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_portfolioSubscriptions.Remove(
					lookupMsg.OriginalTransactionId);
			return;
		}
		var account = ResolveAccount(null);
		await SendPortfolioSnapshotAsync(account,
			lookupMsg.TransactionId, cancellationToken);
		if (!lookupMsg.IsHistoryOnly())
			using (_sync.EnterScope())
				_portfolioSubscriptions.Add(lookupMsg.TransactionId);
		await SendSubscriptionResultAsync(lookupMsg,
			cancellationToken);
	}

	private async ValueTask PollPrivateAsync(
		CancellationToken cancellationToken)
	{
		long[] portfolioTargets;
		long[] orderTargets;
		using (_sync.EnterScope())
		{
			portfolioTargets = _portfolioSubscriptions.ToArray();
			orderTargets = _orderSubscriptions.ToArray();
		}
		if (portfolioTargets.Length == 0 && orderTargets.Length == 0)
			return;
		var account = ResolveAccount(null);
		foreach (var target in portfolioTargets)
			await SendPortfolioSnapshotAsync(account, target,
				cancellationToken);
		foreach (var target in orderTargets)
			await SendOrderSnapshotAsync(account, target, null,
				cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(string account,
		long target, OrderStatusMessage filter,
		CancellationToken cancellationToken)
	{
		foreach (var value in await RestClient.GetOrdersAsync(account,
			AccountType, cancellationToken))
		{
			var order = value.ToSettradeOrder();
			if (!Matches(filter, order))
				continue;
			await SendOrderAsync(order, target, cancellationToken);
		}
		foreach (var trade in await RestClient.GetTradesAsync(account,
			AccountType, cancellationToken))
			await SendTradeAsync(trade, target, cancellationToken);
	}

	private static bool Matches(OrderStatusMessage filter,
		SettradeOrder order)
	{
		if (filter is null)
			return true;
		if (!filter.OrderStringId.IsEmpty() &&
			!filter.OrderStringId.EqualsIgnoreCase(order.OrderNo))
			return false;
		if (!filter.SecurityId.SecurityCode.IsEmpty() &&
			!filter.SecurityId.SecurityCode.EqualsIgnoreCase(
				order.Symbol))
			return false;
		if (filter.From is DateTime from &&
			order.Time < from.ToUniversalTime())
			return false;
		if (filter.To is DateTime to &&
			order.Time > to.ToUniversalTime())
			return false;
		return true;
	}

	private async ValueTask ProcessStreamOrderAsync(SettradeOrder order,
		CancellationToken cancellationToken)
	{
		long[] targets;
		using (_sync.EnterScope())
			targets = _orderSubscriptions.ToArray();
		foreach (var target in targets)
			await SendOrderAsync(order, target, cancellationToken);
	}

	private ValueTask SendOrderAsync(SettradeOrder order, long target,
		CancellationToken cancellationToken)
	{
		long transactionId;
		using (_sync.EnterScope())
			_orderTransactions.TryGetValue(order.OrderNo,
				out transactionId);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = target != 0 ? target : transactionId,
			OrderStringId = order.OrderNo,
			PortfolioName = order.AccountNo.IsEmpty()
				? Account
				: order.AccountNo,
			SecurityId = ToSecurityId(order.Symbol),
			OrderType = order.PriceType.EqualsIgnoreCase("Limit")
				? OrderTypes.Limit
				: OrderTypes.Market,
			Side = order.Side.ToSide(),
			TimeInForce = order.Validity.ToTimeInForce(),
			OrderPrice = order.Price,
			OrderVolume = order.Volume,
			Balance = order.BalanceVolume,
			OrderState = order.Status.ToOrderState(),
			ServerTime = order.Time,
		}, cancellationToken);
	}

	private async ValueTask SendTradeAsync(JObject value, long target,
		CancellationToken cancellationToken)
	{
		var tradeId = FindString(value, "tradeNo", "tradeId",
			"dealNo", "id");
		var orderNo = FindString(value, "orderNo", "order_no");
		var identity = $"{target}:{tradeId}:{orderNo}:" +
			$"{FindString(value, "tradeTime", "transactionTime", "time")}";
		using (_sync.EnterScope())
		{
			if (!_tradeIds.Add(identity))
				return;
		}
		var symbol = FindString(value, "symbol", "seriesId");
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = target,
			TradeStringId = tradeId,
			OrderStringId = orderNo,
			PortfolioName = FindString(value, "accountNo",
				"account_no") ?? Account,
			SecurityId = ToSecurityId(symbol),
			Side = FindString(value, "side", "buySell",
				"longShort").ToSide(),
			TradePrice = FindDecimal(value, "price", "tradePrice",
				"matchPrice"),
			TradeVolume = FindDecimal(value, "volume", "qty",
				"tradeVolume", "matchQty"),
			ServerTime = FindTime(value, "tradeTime",
				"transactionTime", "time", "createdAt") ?? CurrentTime,
		}, cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshotAsync(string account,
		long target, CancellationToken cancellationToken)
	{
		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = target,
			PortfolioName = account,
			BoardCode = BoardCode,
		}, cancellationToken);
		var info = await RestClient.GetAccountInfoAsync(account,
			AccountType, cancellationToken);
		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = target,
			PortfolioName = account,
			SecurityId = SecurityId.Money,
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue,
			FindDecimal(info, "cashBalance", "cash_balance",
				"equity", "totalCashBalance"), true)
		.TryAdd(PositionChangeTypes.BeginValue,
			FindDecimal(info, "equity", "netAssetValue"), true)
		.TryAdd(PositionChangeTypes.BuyOrdersMargin,
			FindDecimal(info, "creditLine", "credit_line",
				"lineAvailable", "excessEquity"), true)
		.TryAdd(PositionChangeTypes.BlockedValue,
			FindDecimal(info, "totalMr", "total_mr", "totalMM"), true)
		.TryAdd(PositionChangeTypes.Currency, CurrencyTypes.THB),
			cancellationToken);
		foreach (var position in await RestClient.GetPortfoliosAsync(
			account, AccountType, cancellationToken))
		{
			var symbol = FindString(position, "symbol", "seriesId");
			if (symbol.IsEmpty())
				continue;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = target,
				PortfolioName = account,
				SecurityId = ToSecurityId(symbol),
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue,
				FindDecimal(position, "actualVolume", "volume",
					"qty", "quantity", "netPosition"), true)
			.TryAdd(PositionChangeTypes.BlockedValue,
				FindDecimal(position, "blockedVolume",
					"pendingVolume"), true)
			.TryAdd(PositionChangeTypes.AveragePrice,
				FindDecimal(position, "averagePrice", "avgPrice",
					"costPrice"), true)
			.TryAdd(PositionChangeTypes.CurrentPrice,
				FindDecimal(position, "marketPrice", "lastPrice"), true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL,
				FindDecimal(position, "unrealizedProfit",
					"unrealizedPnl", "profitLoss"), true),
				cancellationToken);
		}
	}

	private string OrderTopic(string account)
		=> $"proto/ua/_broker/{account}/_front/" +
			(AccountType == SettradeAccountTypes.Equity
				? "ordereqv3"
				: "orderdvv3");

	private static long ToQuantity(decimal value, string name,
		bool zeroAllowed)
	{
		if (value != decimal.Truncate(value) ||
			value < (zeroAllowed ? 0 : 1) ||
			value > long.MaxValue)
			throw new ArgumentOutOfRangeException(name, value,
				$"Settrade quantity must be an integral value " +
					$"{(zeroAllowed ? "not less than zero" :
						"greater than zero")}.");
		return (long)value;
	}

	private static string ToStopCondition(
		SettradeStopConditions condition)
		=> condition switch
		{
			SettradeStopConditions.None => null,
			SettradeStopConditions.LastPaidOrHigher =>
				"LAST_PAID_OR_HIGHER",
			SettradeStopConditions.LastPaidOrLower =>
				"LAST_PAID_OR_LOWER",
			SettradeStopConditions.AskOrHigher => "ASK_OR_HIGHER",
			SettradeStopConditions.AskOrLower => "ASK_OR_LOWER",
			SettradeStopConditions.BidOrHigher => "BID_OR_HIGHER",
			SettradeStopConditions.BidOrLower => "BID_OR_LOWER",
			_ => throw new ArgumentOutOfRangeException(
				nameof(condition), condition, null),
		};

	private static void RemoveNulls(JObject value)
	{
		foreach (var property in value.Properties()
			.Where(static property =>
				property.Value.Type == JTokenType.Null)
			.ToArray())
			property.Remove();
	}

	private static JObject Unwrap(JObject value)
		=> value?.GetValue("data",
			StringComparison.OrdinalIgnoreCase) as JObject ?? value;

	private static string FindString(JObject value,
		params string[] names)
	{
		value = Unwrap(value);
		foreach (var name in names)
		{
			var token = value?.GetValue(name,
				StringComparison.OrdinalIgnoreCase);
			if (token is not null &&
				token.Type is not JTokenType.Null)
				return token.Value<string>();
		}
		return null;
	}

	private static decimal? FindDecimal(JObject value,
		params string[] names)
	{
		value = Unwrap(value);
		foreach (var name in names)
		{
			var token = value?.GetValue(name,
				StringComparison.OrdinalIgnoreCase);
			if (token is null ||
				token.Type is JTokenType.Null or JTokenType.Undefined)
				continue;
			if (token.Type is JTokenType.Integer or JTokenType.Float)
				return token.Value<decimal>();
			if (decimal.TryParse(token.Value<string>(),
				NumberStyles.Any, CultureInfo.InvariantCulture,
				out var result))
				return result;
		}
		return null;
	}

	private static DateTime? FindTime(JObject value,
		params string[] names)
	{
		value = Unwrap(value);
		foreach (var name in names)
		{
			var token = value?.GetValue(name,
				StringComparison.OrdinalIgnoreCase);
			if (token is null ||
				token.Type is JTokenType.Null or JTokenType.Undefined)
				continue;
			if (token.Type == JTokenType.Date)
				return token.Value<DateTime>().ToUniversalTime();
			if (token.Type == JTokenType.Integer)
			{
				var timestamp = token.Value<long>();
				return timestamp > 10_000_000_000
					? DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
						.UtcDateTime
					: DateTimeOffset.FromUnixTimeSeconds(timestamp)
						.UtcDateTime;
			}
			if (DateTime.TryParse(token.Value<string>(),
				CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal |
					DateTimeStyles.AdjustToUniversal, out var parsed))
				return parsed;
		}
		return null;
	}
}
