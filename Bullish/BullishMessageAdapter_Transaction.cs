namespace StockSharp.Bullish;

public partial class BullishMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var symbol = GetSymbol(regMsg.SecurityId);
		var section = ResolveSection(regMsg.SecurityId);
		var marketType = GetMarketType(symbol);
		var tradingAccountId = ResolveTradingAccount(regMsg.PortfolioName);
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException("Order volume must be positive.");

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market or OrderTypes.Conditional))
			throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		var condition = regMsg.Condition as BullishOrderCondition ?? new BullishOrderCondition();
		if (marketType.EqualsIgnoreCase("OPTION") && orderType != OrderTypes.Limit)
			throw new NotSupportedException("Bullish options support limit and post-only orders only.");
		if (condition.IsBorrowAllowed && section != BullishSections.Spot)
			throw new InvalidOperationException("Bullish borrowing applies only to spot orders.");
		if (condition.IsMarketMakerProtection && !marketType.EqualsIgnoreCase("OPTION"))
			throw new InvalidOperationException(
				"Bullish market-maker protection applies only to option orders.");
		if (orderType != OrderTypes.Market && regMsg.Price <= 0)
			throw new InvalidOperationException("A positive limit price is required.");
		if (orderType == OrderTypes.Conditional && condition.StopPrice is not > 0)
			throw new InvalidOperationException(
				"Bullish stop-limit orders require BullishOrderCondition.StopPrice.");

		var nativeType = regMsg.PostOnly == true ? "POST_ONLY" : orderType switch
		{
			OrderTypes.Market => "MARKET",
			OrderTypes.Conditional => "STOP_LIMIT",
			_ => "LIMIT",
		};
		var timeInForce = condition.IsAuction ? "GTX" : regMsg.TimeInForce switch
		{
			TimeInForce.CancelBalance => "IOC",
			TimeInForce.MatchOrCancel => "FOK",
			_ => "GTC",
		};
		var clientOrderId = CreateClientOrderId(regMsg.TransactionId, regMsg.UserOrderId);
		var result = await RestClient.PlaceOrderAsync(new()
		{
			CommandType = "V3CreateOrder",
			Symbol = symbol,
			Type = nativeType,
			Side = regMsg.Side == Sides.Buy ? "BUY" : "SELL",
			Quantity = volume.ToWire(),
			Price = orderType == OrderTypes.Market ? null : regMsg.Price.ToWire(),
			StopPrice = orderType == OrderTypes.Conditional
				? condition.StopPrice.Value.ToWire()
				: null,
			TimeInForce = timeInForce,
			IsBorrowAllowed = condition.IsBorrowAllowed,
			IsMarketMakerProtection = condition.IsMarketMakerProtection,
			ClientOrderId = clientOrderId,
			TradingAccountId = tradingAccountId,
		}, cancellationToken);
		if (result?.OrderId.IsEmpty() != false)
			throw new InvalidDataException("Bullish accepted the order without returning an order ID.");

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = CurrentTime,
			PortfolioName = GetPortfolioName(tradingAccountId),
			Side = regMsg.Side,
			OrderVolume = volume,
			Balance = volume,
			OrderPrice = orderType == OrderTypes.Market ? 0m : regMsg.Price,
			OrderType = orderType,
			OrderState = OrderStates.Active,
			OrderStringId = result.OrderId,
			TransactionId = regMsg.TransactionId,
			OriginalTransactionId = regMsg.TransactionId,
			TimeInForce = regMsg.TimeInForce,
			PostOnly = regMsg.PostOnly,
			Condition = condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var symbol = GetSymbol(replaceMsg.SecurityId);
		var section = ResolveSection(replaceMsg.SecurityId);
		var tradingAccountId = ResolveTradingAccount(replaceMsg.PortfolioName);
		var orderId = replaceMsg.OldOrderStringId;
		if (orderId.IsEmpty() && replaceMsg.OldOrderId is long numericOrderId)
			orderId = numericOrderId.ToString(CultureInfo.InvariantCulture);
		if (orderId.IsEmpty())
			throw new InvalidOperationException("Bullish replacement requires an exchange order ID.");
		if (replaceMsg.OrderType is OrderTypes.Market or OrderTypes.Conditional)
			throw new NotSupportedException("Bullish can amend active limit or post-only orders only.");
		if (replaceMsg.Price <= 0)
			throw new InvalidOperationException("Replacement price must be positive.");
		var volume = replaceMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException("Replacement volume must be positive.");

		var clientOrderId = CreateClientOrderId(replaceMsg.TransactionId,
			replaceMsg.UserOrderId);
		var result = await RestClient.AmendOrderAsync(new()
		{
			CommandType = "V1AmendOrder",
			OrderId = orderId,
			Symbol = symbol,
			Type = replaceMsg.PostOnly == true ? "POST_ONLY" : "LIMIT",
			Price = replaceMsg.Price.ToWire(),
			ClientOrderId = clientOrderId,
			Quantity = volume.ToWire(),
			TradingAccountId = tradingAccountId,
		}, cancellationToken);
		if (result?.OrderId.IsEmpty() != false)
			throw new InvalidDataException("Bullish amended the order without returning an order ID.");

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = CurrentTime,
			PortfolioName = GetPortfolioName(tradingAccountId),
			Side = replaceMsg.Side,
			OrderVolume = volume,
			Balance = volume,
			OrderPrice = replaceMsg.Price,
			OrderType = OrderTypes.Limit,
			OrderState = OrderStates.Active,
			OrderStringId = result.OrderId,
			TransactionId = replaceMsg.TransactionId,
			OriginalTransactionId = replaceMsg.TransactionId,
			TimeInForce = replaceMsg.TimeInForce,
			PostOnly = replaceMsg.PostOnly,
			Condition = replaceMsg.Condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var symbol = GetSymbol(cancelMsg.SecurityId);
		var section = ResolveSection(cancelMsg.SecurityId);
		var tradingAccountId = ResolveTradingAccount(cancelMsg.PortfolioName);
		var orderId = cancelMsg.OrderStringId;
		if (orderId.IsEmpty() && cancelMsg.OrderId is long numericOrderId)
			orderId = numericOrderId.ToString(CultureInfo.InvariantCulture);
		if (orderId.IsEmpty())
			throw new InvalidOperationException("Bullish cancellation requires an exchange order ID.");

		await RestClient.CancelOrderAsync(new()
		{
			CommandType = "V3CancelOrder",
			OrderId = orderId,
			Symbol = symbol,
			TradingAccountId = tradingAccountId,
		}, cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = CurrentTime,
			PortfolioName = GetPortfolioName(tradingAccountId),
			OrderStringId = orderId,
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
				"Bullish Trading API does not expose a close-all-positions command.");
		var tradingAccountId = ResolveTradingAccount(cancelMsg.PortfolioName);
		var hasSecurity = !cancelMsg.SecurityId.SecurityCode.IsEmpty();
		var symbol = hasSecurity ? GetSymbol(cancelMsg.SecurityId) : null;
		if (!hasSecurity)
		{
			if (cancelMsg.Side is not null)
				throw new NotSupportedException(
					"Bullish account-wide cancellation cannot be filtered by side.");
			await RestClient.CancelAllOrdersAsync(new()
			{
				CommandType = "V1CancelAllOrders",
				TradingAccountId = tradingAccountId,
			}, cancellationToken);
			return;
		}

		var section = ResolveSection(cancelMsg.SecurityId);
		if (cancelMsg.SecurityTypes is { Length: > 0 } && !cancelMsg.SecurityTypes.Contains(
			section == BullishSections.Spot ? SecurityTypes.CryptoCurrency :
				GetMarketType(symbol).ToSecurityType()))
			return;
		if (cancelMsg.Side is null)
		{
			await RestClient.CancelAllByMarketAsync(new()
			{
				CommandType = "V1CancelAllOrdersByMarket",
				Symbol = symbol,
				TradingAccountId = tradingAccountId,
			}, cancellationToken);
			return;
		}

		foreach (var order in await RestClient.GetOpenOrdersAsync(tradingAccountId, symbol,
			cancellationToken) ?? [])
		{
			if (order.Side.ToStockSharpSide() != cancelMsg.Side.Value)
				continue;
			await RestClient.CancelOrderAsync(new()
			{
				CommandType = "V3CancelOrder",
				OrderId = order.OrderId,
				Symbol = symbol,
				TradingAccountId = tradingAccountId,
			}, cancellationToken);
		}
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
			return;
		}
		_portfolioSubscriptionId = lookupMsg.TransactionId;
		BullishTradingAccount[] accounts;
		using (_sync.EnterScope())
			accounts = [.. _accounts.Values];
		foreach (var account in accounts)
			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = GetPortfolioName(account.TradingAccountId),
				BoardCode = BoardCodes.Bullish,
				OriginalTransactionId = lookupMsg.TransactionId,
			}, cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
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
			return;
		}
		var account = statusMsg.PortfolioName.IsEmpty()
			? null
			: ResolveTradingAccount(statusMsg.PortfolioName);
		var symbol = statusMsg.SecurityId.SecurityCode?.ToUpperInvariant();
		await SendOrderSnapshotAsync(statusMsg.TransactionId, account, symbol, statusMsg.From,
			statusMsg.To, cancellationToken);
		_orderStatusSubscriptionId = statusMsg.TransactionId;
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshotAsync(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var accounts = await RestClient.GetTradingAccountsAsync(cancellationToken) ?? [];
		foreach (var account in accounts)
		{
			if (account?.TradingAccountId.IsEmpty() != false)
				continue;
			using (_sync.EnterScope())
				_accounts[account.TradingAccountId] = account;
			await SendTradingAccountAsync(account, originalTransactionId, cancellationToken);
			foreach (var asset in await RestClient.GetAssetAccountsAsync(account.TradingAccountId,
				cancellationToken) ?? [])
				await SendAssetAccountAsync(asset, originalTransactionId, cancellationToken);
			if (IsSectionEnabled(BullishSections.Derivatives))
			{
				foreach (var position in await RestClient.GetDerivativePositionsAsync(
					account.TradingAccountId, cancellationToken) ?? [])
					await SendPositionAsync(position, originalTransactionId, cancellationToken);
			}
		}
	}

	private async ValueTask SendOrderSnapshotAsync(long originalTransactionId,
		string requestedAccount, string symbol, DateTime? from, DateTime? to,
		CancellationToken cancellationToken)
	{
		string[] accountIds;
		using (_sync.EnterScope())
			accountIds = requestedAccount.IsEmpty() ? [.. _accounts.Keys] : [requestedAccount];
		foreach (var accountId in accountIds)
		{
			var orders = (await RestClient.GetOpenOrdersAsync(accountId, symbol,
				cancellationToken) ?? [])
				.Concat(await RestClient.GetOrderHistoryAsync(accountId, symbol, from, to,
					cancellationToken) ?? [])
				.Where(static order => order?.OrderId.IsEmpty() == false)
				.GroupBy(static order => order.OrderId)
				.Select(static group => group.First())
				.OrderBy(static order => order.CreatedAtTimestamp.ToUtcTime(order.CreatedAtDateTime));
			foreach (var order in orders)
			{
				order.TradingAccountId ??= accountId;
				await SendOrderAsync(order, originalTransactionId, cancellationToken);
			}

			foreach (var fill in await RestClient.GetFillsAsync(accountId, symbol, from, to,
				cancellationToken) ?? [])
			{
				fill.TradingAccountId ??= accountId;
				await SendFillAsync(fill, originalTransactionId, cancellationToken);
			}
		}
	}

	private ValueTask OnOrderAsync(BullishOrder order, CancellationToken cancellationToken)
		=> SendOrderAsync(order, _orderStatusSubscriptionId, cancellationToken);

	private ValueTask OnFillAsync(BullishTrade fill, CancellationToken cancellationToken)
		=> SendFillAsync(fill, _orderStatusSubscriptionId, cancellationToken);

	private ValueTask OnAssetAccountAsync(BullishAssetAccount account,
		CancellationToken cancellationToken)
		=> SendAssetAccountAsync(account, _portfolioSubscriptionId, cancellationToken);

	private ValueTask OnTradingAccountAsync(BullishTradingAccount account,
		CancellationToken cancellationToken)
		=> SendTradingAccountAsync(account, _portfolioSubscriptionId, cancellationToken);

	private ValueTask OnPositionAsync(BullishDerivativePosition position,
		CancellationToken cancellationToken)
		=> SendPositionAsync(position, _portfolioSubscriptionId, cancellationToken);

	private ValueTask SendTradingAccountAsync(BullishTradingAccount account,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (account?.TradingAccountId.IsEmpty() != false)
			return default;
		using (_sync.EnterScope())
			_accounts[account.TradingAccountId] = account;
		var collateral = account.TotalCollateralUsd.ToDecimal();
		var liabilities = account.TotalLiabilitiesUsd.ToDecimal() ??
			account.TotalBorrowedUsd.ToDecimal();
		var message = this.CreatePortfolioChangeMessage(GetPortfolioName(account.TradingAccountId));
		message.ServerTime = account.UpdatedAtTimestamp.ToUtcTime();
		message.OriginalTransactionId = originalTransactionId;
		message
			.TryAdd(PositionChangeTypes.CurrentValue,
				collateral is decimal total && liabilities is decimal debt ? total - debt : collateral)
			.TryAdd(PositionChangeTypes.CurrentPrice, collateral)
			.TryAdd(PositionChangeTypes.BlockedValue, account.InitialMarginUsd.ToDecimal());
		return SendOutMessageAsync(message, cancellationToken);
	}

	private ValueTask SendAssetAccountAsync(BullishAssetAccount account,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (account?.TradingAccountId.IsEmpty() != false || account.AssetSymbol.IsEmpty())
			return default;
		var available = account.AvailableQuantity.ToDecimal() ?? 0m;
		var locked = account.LockedQuantity.ToDecimal() ?? 0m;
		var loaned = account.LoanedQuantity.ToDecimal() ?? 0m;
		var borrowed = account.BorrowedQuantity.ToDecimal() ?? 0m;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(account.TradingAccountId),
			SecurityId = account.AssetSymbol.ToStockSharp(BullishSections.Spot),
			ServerTime = account.UpdatedAtTimestamp.ToUtcTime(account.UpdatedAtDateTime),
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, available + locked + loaned - borrowed, true)
		.TryAdd(PositionChangeTypes.BlockedValue, locked, true), cancellationToken);
	}

	private ValueTask SendPositionAsync(BullishDerivativePosition position,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (position?.TradingAccountId.IsEmpty() != false || position.Symbol.IsEmpty())
			return default;
		var quantity = position.Quantity.ToDecimal() ?? 0m;
		var absolute = quantity.Abs();
		var averagePrice = absolute > 0m && position.EntryNotional.ToDecimal() is decimal entry
			? entry / absolute
			: (decimal?)null;
		var currentPrice = absolute > 0m && position.Notional.ToDecimal() is decimal notional
			? notional / absolute
			: (decimal?)null;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(position.TradingAccountId),
			SecurityId = position.Symbol.ToStockSharp(BullishSections.Derivatives),
			ServerTime = position.UpdatedAtTimestamp.ToUtcTime(position.UpdatedAtDateTime),
			OriginalTransactionId = originalTransactionId,
			Side = position.Side.IsEmpty() ? null : position.Side.ToStockSharpSide(),
		}
		.TryAdd(PositionChangeTypes.CurrentValue, absolute, true)
		.TryAdd(PositionChangeTypes.AveragePrice, averagePrice, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, currentPrice, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.MarkToMarketPnl.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedPnl.ToDecimal(), true),
			cancellationToken);
	}

	private ValueTask SendOrderAsync(BullishOrder order, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (order?.TradingAccountId.IsEmpty() != false || order.Symbol.IsEmpty() ||
			order.OrderId.IsEmpty())
			return default;
		var section = ResolveSection(order.Symbol);
		var volume = order.Quantity.ToDecimal();
		var filled = order.FilledQuantity.ToDecimal() ?? 0m;
		var condition = new BullishOrderCondition
		{
			StopPrice = order.StopPrice.ToDecimal(),
			IsBorrowAllowed = order.IsBorrowAllowed,
			IsAuction = order.TimeInForce.EqualsIgnoreCase("GTX"),
		};
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToStockSharp(section),
			ServerTime = order.CreatedAtTimestamp.ToUtcTime(order.CreatedAtDateTime),
			PortfolioName = GetPortfolioName(order.TradingAccountId),
			Side = order.Side.ToStockSharpSide(),
			OrderVolume = volume,
			Balance = volume is null ? null : (volume.Value - filled).Max(0m),
			OrderPrice = order.Price.ToDecimal() ?? 0m,
			AveragePrice = order.AverageFillPrice.ToDecimal(),
			OrderType = order.Type.ToStockSharpOrderType(),
			OrderState = order.Status.ToBullishOrderState(),
			OrderStringId = order.OrderId,
			TransactionId = ParseTransactionId(order.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
			TimeInForce = order.TimeInForce.ToStockSharpTimeInForce(),
			PostOnly = order.Type.EqualsIgnoreCase("POST_ONLY"),
			Condition = condition,
		}, cancellationToken);
	}

	private ValueTask SendFillAsync(BullishTrade fill, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (fill?.TradingAccountId.IsEmpty() != false || fill.Symbol.IsEmpty())
			return default;
		var section = ResolveSection(fill.Symbol);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = fill.Symbol.ToStockSharp(section),
			ServerTime = fill.CreatedAtTimestamp.ToUtcTime(fill.CreatedAtDateTime),
			PortfolioName = GetPortfolioName(fill.TradingAccountId),
			Side = fill.Side.ToStockSharpSide(),
			OrderStringId = fill.OrderId,
			TradeStringId = fill.TradeId,
			TradePrice = fill.Price.ToDecimal(),
			TradeVolume = fill.Quantity.ToDecimal(),
			Commission = fill.QuoteFee.ToDecimal() ?? fill.BaseFee.ToDecimal(),
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}
}
