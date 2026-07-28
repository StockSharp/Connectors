namespace StockSharp.SSI;

public partial class SSIMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(
		OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsureTradingConfigured();
		var account = ResolveAccount(regMsg.PortfolioName);
		var quantity = ToQuantity(regMsg.Volume,
			nameof(regMsg.Volume));
		var condition = regMsg.Condition as SSIOrderCondition;
		var ssiType = ToSSIOrderType(regMsg.OrderType, condition);
		var limit = ssiType == "LO";
		if (limit && regMsg.Price <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Price),
				regMsg.Price, "SSI LO price must be positive.");
		var body = new JObject
		{
			["accountNo"] = account,
			["symbol"] = regMsg.SecurityId.SecurityCode
				.ThrowIfEmpty(nameof(regMsg.SecurityId))
				.Trim().ToUpperInvariant(),
			["side"] = regMsg.Side == Sides.Buy ? "B" : "S",
			["quantity"] = quantity,
			["price"] = (limit ? regMsg.Price : 0).ToString(
				CultureInfo.InvariantCulture),
			["orderType"] = ssiType,
			["clientRequestId"] = regMsg.TransactionId.ToString(
				CultureInfo.InvariantCulture),
			["deviceId"] = "StockSharp",
			["userAgent"] = "StockSharp.SSI/1.0",
		};
		var response = await RestClient.PlaceOrderAsync(body,
			cancellationToken);
		var orderId = FindString(response, "orderId")
			.ThrowIfEmpty("orderId");
		using (_sync.EnterScope())
			_orderTransactions[orderId] = regMsg.TransactionId;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			OrderStringId = orderId,
			SecurityId = Normalize(regMsg.SecurityId),
			PortfolioName = account,
			OrderType = regMsg.OrderType ?? OrderTypes.Limit,
			Side = regMsg.Side,
			OrderPrice = regMsg.Price,
			OrderVolume = regMsg.Volume,
			Balance = regMsg.Volume,
			OrderState = FindString(response, "orderStatus")
				.ToSSIOrderState(),
			ServerTime = CurrentTime,
			Condition = condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(
		OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		EnsureTradingConfigured();
		var account = ResolveAccount(replaceMsg.PortfolioName);
		var orderId = replaceMsg.OldOrderStringId;
		if (orderId.IsEmpty())
			throw new InvalidOperationException(
				LocalizedStrings.OrderNoExchangeId.Put(
					replaceMsg.OriginalTransactionId));
		if (replaceMsg.OldOrderPrice is null ||
			replaceMsg.OldOrderVolume is null)
			throw new InvalidOperationException(
				"SSI order replacement requires old price and volume " +
					"to determine the single changed field.");
		var priceChanged =
			replaceMsg.Price != replaceMsg.OldOrderPrice.Value;
		var volumeChanged =
			replaceMsg.Volume != replaceMsg.OldOrderVolume.Value;
		if (priceChanged == volumeChanged)
			throw new InvalidOperationException(
				"SSI can modify either price or quantity in one request.");
		var body = new JObject
		{
			["accountNo"] = account,
			["orderId"] = orderId,
			["clientModifyId"] = replaceMsg.TransactionId.ToString(
				CultureInfo.InvariantCulture),
			["deviceId"] = "StockSharp",
			["userAgent"] = "StockSharp.SSI/1.0",
		};
		if (priceChanged)
		{
			if (replaceMsg.Price <= 0)
				throw new ArgumentOutOfRangeException(
					nameof(replaceMsg.Price), replaceMsg.Price,
					"SSI replacement price must be positive.");
			body["price"] = replaceMsg.Price.ToString(
				CultureInfo.InvariantCulture);
		}
		else
			body["quantity"] = ToQuantity(replaceMsg.Volume,
				nameof(replaceMsg.Volume));
		await RestClient.ReplaceOrderAsync(body, cancellationToken);
		using (_sync.EnterScope())
			_orderTransactions[orderId] = replaceMsg.TransactionId;
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(
		OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsureTradingConfigured();
		var account = ResolveAccount(cancelMsg.PortfolioName);
		var orderId = cancelMsg.OrderStringId;
		if (orderId.IsEmpty())
			throw new InvalidOperationException(
				LocalizedStrings.OrderNoExchangeId.Put(
					cancelMsg.OriginalTransactionId));
		await RestClient.CancelOrderAsync(new JObject
		{
			["accountNo"] = account,
			["orderId"] = orderId,
			["clientCancelId"] = cancelMsg.TransactionId.ToString(
				CultureInfo.InvariantCulture),
			["deviceId"] = "StockSharp",
			["userAgent"] = "StockSharp.SSI/1.0",
		}, cancellationToken);
	}

	private void EnsureTradingConfigured()
	{
		EnsureConnected();
		if (PrivateKey.IsEmpty())
			throw new InvalidOperationException(
				"SSI RSA private key is not specified.");
		if (Otp.IsEmpty())
			throw new InvalidOperationException(
				"SSI trading OTP is not specified.");
	}

	private static string ToSSIOrderType(OrderTypes? orderType,
		SSIOrderCondition condition)
		=> condition?.Type switch
		{
			SSIOrderConditionTypes.ATO => "ATO",
			SSIOrderConditionTypes.ATC => "ATC",
			SSIOrderConditionTypes.MTL => "MTL",
			SSIOrderConditionTypes.MOK => "MOK",
			SSIOrderConditionTypes.MAK => "MAK",
			SSIOrderConditionTypes.PLO => "PLO",
			_ => orderType switch
			{
				null or OrderTypes.Limit => "LO",
				OrderTypes.Market => "MTL",
				_ => throw new NotSupportedException(
					$"SSI order type '{orderType}' is unsupported."),
			},
		};

	private static int ToQuantity(decimal volume, string name)
	{
		if (volume <= 0 || volume != decimal.Truncate(volume) ||
			volume > int.MaxValue)
			throw new ArgumentOutOfRangeException(name, volume,
				"SSI quantity must be a positive 32-bit integer.");
		return (int)volume;
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
					_orderSubscriptions.Remove(statusMsg.TransactionId);
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
			string[] removedAccounts;
			using (_sync.EnterScope())
			{
				if (!_portfolioSubscriptions.Remove(
					lookupMsg.OriginalTransactionId,
					out removedAccounts))
					return;
			}
			foreach (var account in removedAccounts)
				await UnsubscribeTopicAsync(PortfolioTopic(account),
					cancellationToken);
			return;
		}
		var accounts = await GetAccountsAsync(cancellationToken);
		foreach (var account in accounts)
			await SendPortfolioSnapshotAsync(account,
				lookupMsg.TransactionId, cancellationToken);
		if (!lookupMsg.IsHistoryOnly())
		{
			using (_sync.EnterScope())
				_portfolioSubscriptions[lookupMsg.TransactionId] =
					accounts;
			try
			{
				foreach (var account in accounts)
					await SubscribeTopicAsync(PortfolioTopic(account),
						cancellationToken);
			}
			catch
			{
				using (_sync.EnterScope())
					_portfolioSubscriptions.Remove(
						lookupMsg.TransactionId);
				foreach (var account in accounts)
					await UnsubscribeTopicAsync(
						PortfolioTopic(account),
						CancellationToken.None);
				throw;
			}
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private async ValueTask<string[]> GetAccountsAsync(
		CancellationToken cancellationToken)
	{
		if (!Account.IsEmpty())
			return [Account.Trim()];
		return (await RestClient.GetAccountsAsync(cancellationToken))
			.Select(static value => FindString(value, "accountNo"))
			.Where(static value => !value.IsEmpty())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	private async ValueTask PollPrivateAsync(
		CancellationToken cancellationToken)
	{
		(long Target, string[] Accounts)[] portfolioTargets;
		long[] orderTargets;
		using (_sync.EnterScope())
		{
			portfolioTargets =
			[
				.. _portfolioSubscriptions.Select(static pair =>
					(pair.Key, pair.Value))
			];
			orderTargets = _orderSubscriptions.ToArray();
		}
		foreach (var target in portfolioTargets)
			foreach (var portfolioAccount in target.Accounts)
				await SendPortfolioSnapshotAsync(portfolioAccount,
					target.Target, cancellationToken);
		if (orderTargets.Length == 0)
			return;
		var configuredAccount = ResolveAccount(null);
		foreach (var target in orderTargets)
			await SendOrderSnapshotAsync(configuredAccount, target, null,
				cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(string account,
		long target, OrderStatusMessage filter,
		CancellationToken cancellationToken)
	{
		var from = filter?.From?.ToUniversalTime() ??
			CurrentTime.Date;
		var to = filter?.To?.ToUniversalTime() ?? CurrentTime;
		foreach (var value in await RestClient.GetOrdersAsync(account,
			from, to, cancellationToken))
		{
			var order = value.ToSSIOrder();
			if (order.Account.IsEmpty())
				order = CopyWithAccount(order, account);
			if (!Matches(filter, order))
				continue;
			await SendOrderAsync(order, target, cancellationToken);
		}
	}

	private static SSIOrder CopyWithAccount(SSIOrder order,
		string account)
		=> new()
		{
			Account = account,
			ClientRequestId = order.ClientRequestId,
			OrderId = order.OrderId,
			Symbol = order.Symbol,
			Side = order.Side,
			OrderType = order.OrderType,
			Price = order.Price,
			AveragePrice = order.AveragePrice,
			Volume = order.Volume,
			FilledVolume = order.FilledVolume,
			CancelledVolume = order.CancelledVolume,
			Balance = order.Balance,
			Status = order.Status,
			Time = order.Time,
			Message = order.Message,
		};

	private static bool Matches(OrderStatusMessage filter,
		SSIOrder order)
	{
		if (filter is null)
			return true;
		if (!filter.OrderStringId.IsEmpty() &&
			!filter.OrderStringId.EqualsIgnoreCase(order.OrderId))
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

	private async ValueTask ProcessTradingStreamAsync(string topic,
		JObject value, CancellationToken cancellationToken)
	{
		if (topic?.StartsWith("portfolio.",
			StringComparison.OrdinalIgnoreCase) == true)
		{
			await ProcessPortfolioStreamAsync(value, cancellationToken);
			return;
		}
		var eventType = FindString(value, "eventType", "type");
		if (eventType.EqualsIgnoreCase("orderMatchEvent"))
		{
			await ProcessOrderMatchAsync(value.ToSSIOrderMatch(),
				cancellationToken);
			return;
		}
		var order = value.ToSSIOrder();
		if (order.OrderId.IsEmpty())
			return;
		long[] targets;
		using (_sync.EnterScope())
			targets = _orderSubscriptions.Count == 0
				? [0]
				: _orderSubscriptions.ToArray();
		foreach (var target in targets)
			await SendOrderAsync(order, target, cancellationToken);
	}

	private ValueTask SendOrderAsync(SSIOrder order, long target,
		CancellationToken cancellationToken)
	{
		long transactionId;
		using (_sync.EnterScope())
			_orderTransactions.TryGetValue(order.OrderId,
				out transactionId);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = target != 0
				? target
				: transactionId,
			OrderStringId = order.OrderId,
			PortfolioName = order.Account.IsEmpty()
				? Account
				: order.Account,
			SecurityId = ToSecurityId(order.Symbol),
			OrderType = order.OrderType.EqualsIgnoreCase("LO")
				? OrderTypes.Limit
				: OrderTypes.Market,
			Side = order.Side,
			OrderPrice = order.Price,
			OrderVolume = order.Volume,
			Balance = order.Balance,
			OrderState = order.Status.ToSSIOrderState(),
			ServerTime = order.Time.UtcDateTime,
		}, cancellationToken);
	}

	private async ValueTask ProcessOrderMatchAsync(SSIOrderMatch match,
		CancellationToken cancellationToken)
	{
		var id = match.Id.IsEmpty()
			? $"{match.OrderId}:{match.Time:O}:{match.Price}:" +
				$"{match.Volume}"
			: match.Id;
		using (_sync.EnterScope())
		{
			if (!_matchIds.Add(id))
				return;
		}
		long transactionId;
		long[] targets;
		using (_sync.EnterScope())
		{
			_orderTransactions.TryGetValue(match.OrderId,
				out transactionId);
			targets = _orderSubscriptions.Count == 0
				? [transactionId]
				: _orderSubscriptions.ToArray();
		}
		foreach (var target in targets)
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = target,
				TradeStringId = id,
				OrderStringId = match.OrderId,
				PortfolioName = match.Account.IsEmpty()
					? Account
					: match.Account,
				SecurityId = ToSecurityId(match.Symbol),
				Side = match.Side,
				TradePrice = match.Price,
				TradeVolume = match.Volume,
				ServerTime = match.Time.UtcDateTime,
			}, cancellationToken);
	}

	private async ValueTask ProcessPortfolioStreamAsync(JObject value,
		CancellationToken cancellationToken)
	{
		var account = FindString(value, "accountNo", "account");
		(long Target, string[] Accounts)[] targets;
		using (_sync.EnterScope())
			targets =
			[
				.. _portfolioSubscriptions
					.Where(pair => pair.Value.Any(
						account.EqualsIgnoreCase))
					.Select(static pair => (pair.Key, pair.Value))
			];
		foreach (var target in targets)
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = target.Target,
				PortfolioName = account,
				SecurityId = SecurityId.Money,
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue,
				FindDecimal(value, "cashBalance",
					"availableBalance"), true)
			.TryAdd(PositionChangeTypes.BeginValue,
				FindDecimal(value, "totalAsset"), true)
			.TryAdd(PositionChangeTypes.Currency, CurrencyTypes.VND),
				cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshotAsync(string account,
		long target, CancellationToken cancellationToken)
	{
		await SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = target,
			PortfolioName = account,
			BoardCode = BoardCodes.Hose,
		}, cancellationToken);
		var balance = await RestClient.GetBalanceAsync(account,
			cancellationToken);
		var equity = FindObject(balance, "equity");
		var derivative = FindObject(balance, "derivative");
		var money = equity ?? derivative ?? balance;
		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = target,
			PortfolioName = account,
			SecurityId = SecurityId.Money,
			ServerTime = CurrentTime,
		}
		.TryAdd(PositionChangeTypes.CurrentValue,
			FindDecimal(money, "availableCash", "accountBalance",
				"withdrawable", "cashBalance"), true)
		.TryAdd(PositionChangeTypes.BlockedValue,
			FindDecimal(money, "onHoldCash", "blockCash"), true)
		.TryAdd(PositionChangeTypes.VariationMargin,
			FindDecimal(money, "floatingPL", "totalPL"), true)
		.TryAdd(PositionChangeTypes.Currency, CurrencyTypes.VND),
			cancellationToken);
		var positions = await RestClient.GetPositionsAsync(account,
			cancellationToken);
		foreach (var position in FindArray(positions, "equity"))
		{
			var symbol = FindString(position, "symbol");
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
				FindDecimal(position, "quantity"), true)
			.TryAdd(PositionChangeTypes.BlockedValue,
				FindDecimal(position, "blockQuantity",
					"sellingQuantity"), true)
			.TryAdd(PositionChangeTypes.AveragePrice,
				FindDecimal(position, "costPrice"), true),
				cancellationToken);
		}
		var derivatives = FindObject(positions, "derivative");
		foreach (var position in FindArray(derivatives,
			"derOpenPositions", "openPositions"))
		{
			var symbol = FindString(position, "symbol");
			if (symbol.IsEmpty())
				continue;
			var net = FindDecimal(position, "net") ??
				(FindDecimal(position, "long") ?? 0) -
				(FindDecimal(position, "short") ?? 0);
			await SendOutMessageAsync(new PositionChangeMessage
			{
				OriginalTransactionId = target,
				PortfolioName = account,
				SecurityId = ToSecurityId(symbol),
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, net, true)
			.TryAdd(PositionChangeTypes.AveragePrice,
				net >= 0
					? FindDecimal(position, "bidAvgPrice")
					: FindDecimal(position, "askAvgPrice"), true)
			.TryAdd(PositionChangeTypes.CurrentPrice,
				FindDecimal(position, "tradePrice"), true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL,
				FindDecimal(position, "floatingPL"), true)
			.TryAdd(PositionChangeTypes.RealizedPnL,
				FindDecimal(position, "tradingPL"), true),
				cancellationToken);
		}
	}

	private static string OrderTopic(string account)
		=> $"order.{account}";

	private static string PortfolioTopic(string account)
		=> $"portfolio.{account}";

	private static JObject FindObject(JObject value,
		params string[] names)
	{
		value = value?.UnwrapSSIData() ?? value;
		foreach (var name in names)
		{
			var result = value?.GetValue(name,
				StringComparison.OrdinalIgnoreCase) as JObject;
			if (result is not null)
				return result;
		}
		return null;
	}

	private static JObject[] FindArray(JObject value,
		params string[] names)
	{
		value = value?.UnwrapSSIData() ?? value;
		foreach (var name in names)
		{
			if (value?.GetValue(name,
				StringComparison.OrdinalIgnoreCase) is JArray array)
				return array.OfType<JObject>().ToArray();
		}
		return [];
	}

	private static string FindString(JObject value,
		params string[] names)
	{
		value = value?.UnwrapSSIData() ?? value;
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
		value = value?.UnwrapSSIData() ?? value;
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
}
