namespace StockSharp.Swissquote;

public partial class SwissquoteMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		if (regMsg.Volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Volume), regMsg.Volume,
				LocalizedStrings.InvalidValue);
		var condition = regMsg.Condition as SwissquoteOrderCondition ?? new();
		var portfolio = regMsg.PortfolioName.IsEmpty(SafekeepingAccountId)
			.ThrowIfEmpty(nameof(SafekeepingAccountId));
		var clientOrderId = Guid.NewGuid().ToString("N")[..20];
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		var request = CreateOrderRequest(clientOrderId, portfolio, regMsg.SecurityId, regMsg.Side,
			regMsg.PositionEffect, regMsg.Volume, regMsg.Price, orderType, regMsg.TimeInForce,
			regMsg.TillDate?.UtcKind(), condition);
		var response = await GetRest().SubmitOrder(request, IsBestEffort, IsDryRun, cancellationToken);
		var tracker = new OrderTracker
		{
			TransactionId = regMsg.TransactionId,
			ClientOrderId = clientOrderId,
			BrokerOrderId = response.ExtendedOrder?.OrderIdentification,
			SecurityId = regMsg.SecurityId,
			PortfolioName = portfolio,
			Side = regMsg.Side,
			OrderType = orderType,
			Volume = regMsg.Volume,
			Price = regMsg.Price,
			TimeInForce = regMsg.TimeInForce,
			ExpiryDate = regMsg.TillDate?.UtcKind(),
			Condition = condition,
		};
		_orders[clientOrderId] = tracker;
		CacheBrokerOrder(tracker);
		await ProcessOrder(response, regMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		var clientOrderId = GetClientOrderId(cancelMsg.OrderStringId, cancelMsg.OrderId,
			cancelMsg.OriginalTransactionId);
		var response = await GetRest().CancelOrder(clientOrderId, cancellationToken);
		if (response != null)
			await ProcessOrder(response, cancelMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		if (!statusMsg.IsSubscribe)
		{
			if (_orderStatusSubscriptionId == statusMsg.OriginalTransactionId)
				_orderStatusSubscriptionId = 0;
			return;
		}

		await RefreshOrders(statusMsg.TransactionId, cancellationToken, statusMsg.PortfolioName,
			statusMsg.From?.UtcKind(), statusMsg.To?.UtcKind(), statusMsg.Count, true);
		_lastTransactionRefresh = DateTime.UtcNow;
		if (statusMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
		else
		{
			_orderStatusSubscriptionId = statusMsg.TransactionId;
			_lastOrderRefresh = DateTime.UtcNow;
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		if (!lookupMsg.IsSubscribe)
		{
			if (_portfolioSubscriptionId == lookupMsg.OriginalTransactionId)
				_portfolioSubscriptionId = 0;
			return;
		}

		await SendPortfolioSnapshot(lookupMsg.TransactionId, lookupMsg.PortfolioName,
			cancellationToken);
		if (lookupMsg.IsHistoryOnly())
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
		else
		{
			_portfolioSubscriptionId = lookupMsg.TransactionId;
			_lastPortfolioRefresh = DateTime.UtcNow;
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		}
	}

	private SwissquoteOrderRequest CreateOrderRequest(string clientOrderId, string portfolio,
		SecurityId securityId, Sides side, OrderPositionEffects? positionEffect, decimal volume,
		decimal price, OrderTypes orderType, TimeInForce? timeInForce, DateTime? expiryDate,
		SwissquoteOrderCondition condition)
	{
		var isDigitalAsset = condition.IsDigitalAsset ||
			securityId.SecurityCode?.StartsWith("Crypto:", StringComparison.OrdinalIgnoreCase) == true;
		var stopPrice = condition.StopPrice;
		var executionType = orderType.ToExecutionType(price, stopPrice);
		if (executionType is "limit" or "stopLimit" && price <= 0)
			throw new InvalidOperationException("Swissquote limit orders require a positive limit price.");
		if (executionType is "stop" or "stopLimit" && stopPrice is not > 0)
			throw new InvalidOperationException("Swissquote stop orders require a positive stop price.");
		if (isDigitalAsset && executionType is not ("limit" or "stopLimit"))
			throw new NotSupportedException(
				"Swissquote OpenWealth supports limit and stop-limit orders for digital assets.");

		var identification = condition.InstrumentIdentification;
		if (identification.IsEmpty())
			identification = securityId.Isin.IsEmpty(securityId.Native?.ToString())
				.IsEmpty(securityId.SecurityCode);
		identification = identification.ThrowIfEmpty(nameof(condition.InstrumentIdentification));
		var identificationType = condition.InstrumentIdentificationType;
		if (isDigitalAsset)
		{
			if (!identification.StartsWith("Crypto:", StringComparison.OrdinalIgnoreCase))
				identification = "Crypto: " + identification;
			identificationType = SwissquoteInstrumentIdentificationTypes.OtherProprietaryIdentification;
		}
		else if (identificationType == SwissquoteInstrumentIdentificationTypes.Auto)
		{
			identificationType = !securityId.Isin.IsEmpty() &&
				identification.EqualsIgnoreCase(securityId.Isin)
				? SwissquoteInstrumentIdentificationTypes.Isin
				: identification.Contains('_') || identification.StartsWith("ISO4217:",
					StringComparison.OrdinalIgnoreCase)
					? SwissquoteInstrumentIdentificationTypes.OtherProprietaryIdentification
					: SwissquoteInstrumentIdentificationTypes.TickerSymbol;
		}

		var mic = condition.MarketIdentificationCode;
		if (mic.IsEmpty() && securityId.BoardCode?.Length == 4 &&
			!securityId.BoardCode.EqualsIgnoreCase("SWISSQUOTE"))
			mic = securityId.BoardCode;
		if (!mic.IsEmpty() && mic.Length != 4)
			throw new InvalidOperationException("Swissquote MIC must contain four characters.");
		if (!isDigitalAsset && mic.IsEmpty() && !identification.Contains('_'))
			throw new InvalidOperationException(
				"Swissquote securities orders require a four-character MIC or a native stockKey identifier.");
		var currency = condition.Currency.IsEmpty(AccountCurrency)
			.ThrowIfEmpty(nameof(condition.Currency)).ToUpperInvariant();

		var accounts = CreateOrderAccounts(portfolio, isDigitalAsset);
		var quantity = volume.ToNativeDecimal();
		return new()
		{
			ClientOrderIdentification = clientOrderId,
			BulkOrderDetails = new()
			{
				Side = side.ToNativeSide(condition.IsOpenPosition, positionEffect),
				OrderQuantity = new()
				{
					Amount = quantity,
					Type = condition.QuantityType.ToNative(),
				},
				NumberOfAllocations = 1,
				FinancialInstrumentDetails = new()
				{
					FinancialInstrumentIdentification = new()
					{
						Identification = identification,
						Type = identificationType.ToNative(),
					},
					UnderlyingSymbol = condition.UnderlyingSymbol,
					OptionType = condition.OptionType?.ToString().ToLowerInvariant(),
					OptionStyle = condition.OptionStyle?.ToNative(),
					OptionExpirationType = condition.OptionExpirationType?.ToNative(),
					StrikePrice = condition.StrikePrice?.ToNativeDecimal(),
					MaturityDate = condition.MaturityDate?
						.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
					Multiplier = condition.Multiplier?.ToNativeDecimal(),
				},
				PlaceOfTrade = mic.IsEmpty() ? null : new() { MarketIdentificationCode = mic.ToUpperInvariant() },
				Currency = currency,
				CashAccountCurrency = isDigitalAsset ? null : condition.CashAccountCurrency
					.IsEmpty(AccountCurrency).ThrowIfEmpty(nameof(condition.CashAccountCurrency))
					.ToUpperInvariant(),
				ExecutionType = executionType,
				LimitPrice = executionType is "limit" or "stopLimit" ? price.ToNativeDecimal() : null,
				StopPrice = executionType is "stop" or "stopLimit" ? stopPrice.Value.ToNativeDecimal() : null,
				TimeInForce = timeInForce.ToNativeTimeInForce(expiryDate, isDigitalAsset),
				ExpiryDateTime = expiryDate?.ToUniversalTime()
					.ToString("O", CultureInfo.InvariantCulture),
			},
			RequestedAllocationList =
			[
				new()
				{
					Accounts = accounts,
					ClientAllocationIdentification = clientOrderId,
					Amount = quantity,
				},
			],
		};
	}

	private SwissquoteAccountReference[] CreateOrderAccounts(string portfolio, bool isDigitalAsset)
	{
		if (portfolio.Length > 8)
			throw new InvalidOperationException("Swissquote order account identifiers cannot exceed eight characters.");
		if (isDigitalAsset)
			return [new() { Identification = portfolio, Type = "other" }];
		var cashAccount = CashAccountId.ThrowIfEmpty(nameof(CashAccountId));
		if (cashAccount.Length > 8)
			throw new InvalidOperationException("Swissquote order account identifiers cannot exceed eight characters.");
		return [.. new[] { portfolio, cashAccount }
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Select(id => new SwissquoteAccountReference { Identification = id, Type = "other" })];
	}

	private ValueTask RefreshOrders(long originalTransactionId, bool isTransactionRefresh,
		CancellationToken cancellationToken)
		=> RefreshOrders(originalTransactionId, cancellationToken, null, null, null, null,
			isTransactionRefresh);

	private async ValueTask RefreshOrders(long originalTransactionId,
		CancellationToken cancellationToken, string portfolioName, DateTime? from,
		DateTime? to, long? count, bool isTransactionRefresh)
	{
		var left = count ?? long.MaxValue;
		foreach (var order in await GetRest().GetOrders(cancellationToken))
		{
			if (!portfolioName.IsEmpty() &&
				!GetOrderPortfolio(order).EqualsIgnoreCase(portfolioName))
				continue;
			await ProcessOrder(order, originalTransactionId, cancellationToken);
			if (--left <= 0)
				return;
		}
		if (!isTransactionRefresh)
			return;

		var transactionAccount = portfolioName.IsEmpty(SafekeepingAccountId);
		if (transactionAccount.IsEmpty())
		{
			transactionAccount = (await GetKnownAccounts(cancellationToken))
				.FirstOrDefault(IsSafekeepingAccount)?.AccountIdentification;
		}
		if (transactionAccount.IsEmpty())
			return;

		var now = GetSwissDate();
		var firstDate = (from ?? now).Date;
		var lastDate = (to ?? now).Date;
		if (lastDate < firstDate)
			throw new ArgumentOutOfRangeException(nameof(to), to, LocalizedStrings.InvalidValue);
		if ((lastDate - firstDate).TotalDays > 366)
			throw new InvalidOperationException(
				"Swissquote transaction lookup is limited to 367 daily requests per subscription.");

		for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
		{
			foreach (var transaction in await GetRest().GetTransactions(transactionAccount,
				date, GetSwissOffset(date), cancellationToken))
			{
				if (await ProcessTransaction(transaction, originalTransactionId, cancellationToken) &&
					--left <= 0)
					return;
			}
		}
	}

	private async ValueTask SendPortfolioSnapshot(long originalTransactionId,
		string requestedPortfolio, CancellationToken cancellationToken)
	{
		var accounts = await GetKnownAccounts(cancellationToken);
		if (!requestedPortfolio.IsEmpty())
			accounts = [.. accounts.Where(account =>
				account.AccountIdentification.EqualsIgnoreCase(requestedPortfolio))];
		if (accounts.Length == 0 && !requestedPortfolio.IsEmpty())
			throw new InvalidOperationException($"Swissquote account '{requestedPortfolio}' was not found.");

		foreach (var accountInfo in accounts)
		{
			var portfolio = accountInfo.AccountIdentification;
			await SendOutMessageAsync(new PortfolioMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = portfolio,
				BoardCode = "SWISSQUOTE",
				Currency = accountInfo.AccountReferenceCurrency.ToCurrency(),
			}, cancellationToken);

			if (IsSafekeepingAccount(accountInfo))
			{
				var date = GetSwissDate();
				var account = await GetRest().GetPositions(portfolio, date,
					GetSwissOffset(date), cancellationToken);
				foreach (var position in account.PositionList ?? [])
					await ProcessPosition(position, portfolio, originalTransactionId, cancellationToken);
			}

			var currency = accountInfo.AccountReferenceCurrency.IsEmpty(AccountCurrency);
			if (!currency.IsEmpty())
				currency = currency.ToUpperInvariant();
			if (!currency.IsEmpty())
			{
				var capacity = await GetRest().GetTradingCapacity(portfolio, currency,
					cancellationToken);
				var buyingPower = capacity?.BuyingPower?.TotalBuyingPowerAmount;
				var value = buyingPower?.Value.ToDecimal();
				if (value != null)
				{
					await SendOutMessageAsync(new PositionChangeMessage
					{
						OriginalTransactionId = originalTransactionId,
						PortfolioName = portfolio,
						SecurityId = SecurityId.Money,
						ServerTime = DateTime.UtcNow,
					}.Add(PositionChangeTypes.CurrentValue,
						ApplySign(value.Value, buyingPower.CreditDebitIndicator)), cancellationToken);
				}
			}
		}
	}

	private async ValueTask ProcessPosition(SwissquotePosition position, string portfolio,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		var instrument = position?.FinancialInstrument?.FinancialInstrumentIdentification;
		var quantity = position?.AmountOrUnits?.Amount.ToDecimal();
		if (instrument?.Identification.IsEmpty() != false || quantity == null)
			return;
		var marketPrice = position.Prices?.FirstOrDefault(price =>
			price?.PriceType.EqualsIgnoreCase("marketPrice") == true)?.Amount.ToDecimal();
		var averagePrice = position.Prices?.FirstOrDefault(price =>
			price?.PriceType.EqualsIgnoreCase("dealPrice") == true ||
			price?.PriceType.EqualsIgnoreCase("costPrice") == true)?.Amount.ToDecimal();
		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = portfolio,
			SecurityId = instrument.ToSecurityId(),
			ServerTime = DateTime.UtcNow,
		}
		.Add(PositionChangeTypes.CurrentValue,
			ApplySign(quantity.Value, position.AmountOrUnits.CreditDebitIndicator))
		.TryAdd(PositionChangeTypes.CurrentPrice, marketPrice, true)
		.TryAdd(PositionChangeTypes.AveragePrice, averagePrice, true), cancellationToken);
	}

	private async ValueTask ProcessOrder(SwissquoteCompleteOrder order,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		var extended = order?.ExtendedOrder;
		var state = order?.OrderState;
		var clientOrderId = extended?.ClientOrderIdentification;
		if (clientOrderId.IsEmpty() || extended?.BulkOrderDetails == null || state == null)
			return;
		_orders.TryGetValue(clientOrderId, out var tracker);
		if (tracker != null && !extended.OrderIdentification.IsEmpty())
		{
			tracker.BrokerOrderId = extended.OrderIdentification;
			CacheBrokerOrder(tracker);
		}
		var details = extended.BulkOrderDetails;
		var volume = details.OrderQuantity?.Amount.ToDecimal() ?? tracker?.Volume ?? 0;
		var filled = state.ExecutedQuantity.ToDecimal() ?? 0;
		var remaining = state.RemainingQuantity.ToDecimal() ?? Math.Max(0, volume - filled);
		var orderState = state.Status.ToOrderState();
		var errorText = order.GetError();
		var serverTime = state.StatusDateTime.ToDateTime() ?? extended.OrderDateTime.ToDateTime() ??
			order.StatementDateTime.ToDateTime() ?? DateTime.UtcNow;
		var securityId = tracker?.SecurityId ?? details.FinancialInstrumentDetails?
			.FinancialInstrumentIdentification.ToSecurityId(details.PlaceOfTrade?.MarketIdentificationCode);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalTransactionId,
			TransactionId = tracker?.TransactionId ?? 0,
			OrderId = ParseId(extended.OrderIdentification),
			OrderStringId = clientOrderId,
			OrderBoardId = extended.OrderIdentification,
			SecurityId = securityId ?? default,
			PortfolioName = tracker?.PortfolioName ?? GetOrderPortfolio(order),
			Side = tracker?.Side ?? details.Side.ToSide(),
			OrderType = tracker?.OrderType ?? details.ExecutionType.ToOrderType(),
			OrderPrice = tracker?.Price ?? details.LimitPrice.ToDecimal() ?? 0,
			OrderVolume = volume,
			Balance = remaining,
			OrderState = orderState,
			ServerTime = serverTime,
			TimeInForce = tracker?.TimeInForce ?? details.TimeInForce.ToTimeInForce(),
			ExpiryDate = tracker?.ExpiryDate ?? details.ExpiryDateTime.ToDateTime(),
			Condition = tracker?.Condition ?? ToCondition(details),
			Error = orderState == OrderStates.Failed
				? new InvalidOperationException(errorText.IsEmpty("Swissquote rejected the order."))
				: null,
		}, cancellationToken);

		if (tracker != null)
			tracker.ReportedFilled = Math.Max(tracker.ReportedFilled, filled);
	}

	private async ValueTask<bool> ProcessTransaction(SwissquoteTransaction transaction,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (transaction?.TransactionIdentification.IsEmpty() != false ||
			!transaction.TransactionType.EqualsIgnoreCase("securitiesExchange"))
			return false;
		var movement = transaction.MovementList?.FirstOrDefault(item =>
			item?.MovementType.EqualsIgnoreCase("security") == true &&
			item.FinancialInstrument?.Identification.IsEmpty() == false);
		var volume = movement?.Amount.ToDecimal();
		var price = transaction.Prices?.FirstOrDefault(item =>
			item?.PriceType.EqualsIgnoreCase("dealPrice") == true)?.Amount.ToDecimal();
		if (movement == null || volume is not > 0 || price is not > 0)
			return false;
		var tradeKey = transaction.TransactionIdentification +
			(transaction.IsReversal ? ":reversal" : string.Empty);
		if (!_reportedTrades.TryAdd(tradeKey))
			return false;

		OrderTracker tracker = null;
		if (!transaction.OrderIdentification.IsEmpty() &&
			_brokerOrders.TryGetValue(transaction.OrderIdentification, out var clientOrderId))
			_orders.TryGetValue(clientOrderId, out tracker);
		var serverTime = GetTransactionTime(transaction) ?? DateTime.UtcNow;
		var side = transaction.TransactionSubtype.ContainsIgnoreCase("buy")
			? Sides.Buy
			: transaction.TransactionSubtype.ContainsIgnoreCase("sell")
				? Sides.Sell
				: movement.CreditDebitIndicator.EqualsIgnoreCase("credit") ? Sides.Buy : Sides.Sell;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originalTransactionId,
			TransactionId = tracker?.TransactionId ?? 0,
			OrderId = ParseId(transaction.OrderIdentification),
			OrderStringId = tracker?.ClientOrderId ?? transaction.OrderIdentification,
			OrderBoardId = transaction.OrderIdentification,
			TradeId = ParseId(transaction.TransactionIdentification),
			TradeStringId = transaction.TransactionIdentification,
			SecurityId = tracker?.SecurityId ?? movement.FinancialInstrument.ToSecurityId(
				transaction.PlaceOfTrade?.MarketIdentificationCode),
			PortfolioName = tracker?.PortfolioName ?? movement.AccountDetails?.AccountIdentification
				.IsEmpty(SafekeepingAccountId),
			Side = side,
			TradePrice = price,
			TradeVolume = volume,
			ServerTime = serverTime,
			IsCancellation = transaction.IsReversal,
		}, cancellationToken);
		return true;
	}

	private string GetClientOrderId(string stringId, long? numericId, long originalTransactionId)
	{
		var id = stringId.IsEmpty(numericId?.ToString(CultureInfo.InvariantCulture));
		if (!id.IsEmpty() && _brokerOrders.TryGetValue(id, out var clientId))
			return clientId;
		if (!id.IsEmpty() && _orders.ContainsKey(id))
			return id;
		var tracker = _orders.Values.FirstOrDefault(item =>
			item.TransactionId == originalTransactionId);
		if (tracker != null)
			return tracker.ClientOrderId;
		return id.ThrowIfEmpty(LocalizedStrings.OrderNoExchangeId.Put(originalTransactionId));
	}

	private void CacheBrokerOrder(OrderTracker tracker)
	{
		if (tracker?.BrokerOrderId.IsEmpty() == false)
			_brokerOrders[tracker.BrokerOrderId] = tracker.ClientOrderId;
	}

	private static string GetOrderPortfolio(SwissquoteCompleteOrder order)
		=> order?.ExtendedOrder?.AllocationList?
			.SelectMany(allocation => allocation?.RequestedAllocation?.Accounts ?? [])
			.Select(account => account?.Identification)
			.FirstOrDefault(id => !id.IsEmpty());

	private static SwissquoteOrderCondition ToCondition(SwissquoteBulkOrderDetails details)
	{
		var instrument = details?.FinancialInstrumentDetails;
		return new()
		{
			StopPrice = details?.StopPrice.ToDecimal(),
			InstrumentIdentification = instrument?.FinancialInstrumentIdentification?.Identification,
			InstrumentIdentificationType = instrument?.FinancialInstrumentIdentification?.Type
				.ToIdentificationType() ?? SwissquoteInstrumentIdentificationTypes.Auto,
			MarketIdentificationCode = details?.PlaceOfTrade?.MarketIdentificationCode,
			Currency = details?.Currency,
			CashAccountCurrency = details?.CashAccountCurrency,
			QuantityType = details?.OrderQuantity?.Type.EqualsIgnoreCase("nominal") == true
				? SwissquoteQuantityTypes.Nominal : SwissquoteQuantityTypes.UnitsNumber,
			IsDigitalAsset = instrument?.FinancialInstrumentIdentification?.Identification
				.StartsWith("Crypto:", StringComparison.OrdinalIgnoreCase) == true,
			UnderlyingSymbol = instrument?.UnderlyingSymbol,
			OptionType = instrument?.OptionType.ToOptionType(),
			OptionStyle = instrument?.OptionStyle?.ToUpperInvariant() switch
			{
				"AMER" => SwissquoteOptionStyles.American,
				"EUR" => SwissquoteOptionStyles.European,
				"BERM" => SwissquoteOptionStyles.Bermudan,
				_ => null,
			},
			OptionExpirationType = instrument?.OptionExpirationType?.ToUpperInvariant() switch
			{
				"DAILY" => SwissquoteOptionExpirationTypes.Daily,
				"WEEKLY" => SwissquoteOptionExpirationTypes.Weekly,
				"MONTHLY" => SwissquoteOptionExpirationTypes.Monthly,
				"END_OF_THE_MONTH" => SwissquoteOptionExpirationTypes.EndOfMonth,
				"QUARTERLY" => SwissquoteOptionExpirationTypes.Quarterly,
				_ => null,
			},
			StrikePrice = instrument?.StrikePrice.ToDecimal(),
			MaturityDate = instrument?.MaturityDate.ToDate(),
			Multiplier = instrument?.Multiplier.ToDecimal(),
		};
	}

	private static DateTime? GetTransactionTime(SwissquoteTransaction transaction)
	{
		var time = transaction.DateTimeList?.FirstOrDefault(item =>
			item?.DateType.EqualsIgnoreCase("transactionDate") == true)?.DateTime.ToDateTime()
			?? transaction.DateTimeList?.FirstOrDefault(item =>
				item?.DateType.EqualsIgnoreCase("bookingDate") == true)?.DateTime.ToDateTime();
		if (time != null)
			return time;
		var date = transaction.DateList?.FirstOrDefault(item =>
			item?.DateType.EqualsIgnoreCase("transactionDate") == true)?.Date.ToDate()
			?? transaction.DateList?.FirstOrDefault()?.Date.ToDate();
		return date;
	}

	private static decimal ApplySign(decimal value, string creditDebitIndicator)
		=> creditDebitIndicator.EqualsIgnoreCase("debit") ? -Math.Abs(value) : Math.Abs(value);

	private static long? ParseId(string value)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
			? result : null;
}
