namespace StockSharp.Xt;

public partial class XtMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		var symbol = GetSymbol(regMsg.SecurityId);
		var section = ResolveSection(regMsg.SecurityId);
		EnsurePrivateReady(section);
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException("Order volume must be positive.");
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
			throw new InvalidOperationException("Limit order price must be positive.");

		var condition = regMsg.Condition as XtOrderCondition ?? new XtOrderCondition();
		var policy = condition.Policy;
		if (regMsg.PostOnly == true)
			policy = XtOrderPolicies.PostOnly;
		else if (regMsg.TimeInForce == TimeInForce.CancelBalance)
			policy = XtOrderPolicies.ImmediateOrCancel;
		else if (regMsg.TimeInForce == TimeInForce.MatchOrCancel)
			policy = XtOrderPolicies.FillOrKill;
		if (orderType == OrderTypes.Market && policy != XtOrderPolicies.Regular)
			throw new InvalidOperationException("XT.COM execution policies apply only to limit orders.");

		var clientOrderId = CreateClientOrderId(regMsg.TransactionId, regMsg.UserOrderId);
		XtOrderResult result;
		if (section == XtSections.Spot)
		{
			if (orderType == OrderTypes.Market && regMsg.Side == Sides.Buy &&
				condition.QuoteAmount is not > 0)
				throw new InvalidOperationException(
					"XT.COM spot market buys require QuoteAmount in XtOrderCondition.");
			await EnsureSpotPrivateSymbolAsync(symbol, cancellationToken);
			result = await RestClient.PlaceSpotOrderAsync(new()
			{
				Symbol = symbol,
				Side = regMsg.Side == Sides.Buy ? "BUY" : "SELL",
				Type = orderType == OrderTypes.Market ? "MARKET" : "LIMIT",
				ClientOrderId = clientOrderId,
				TimeInForce = policy.ToWire(),
				Quantity = orderType == OrderTypes.Market && regMsg.Side == Sides.Buy ? null : volume.ToWire(),
				Price = orderType == OrderTypes.Limit ? regMsg.Price.ToWire() : null,
				QuoteQuantity = orderType == OrderTypes.Market && regMsg.Side == Sides.Buy
					? condition.QuoteAmount.Value.ToWire()
					: null,
			}, cancellationToken);
		}
		else
		{
			if (condition.PositionSide == XtPositionSides.Both)
				throw new InvalidOperationException(
					"XT.COM futures orders require PositionSide Long or Short.");
			if (volume != decimal.Truncate(volume))
				throw new InvalidOperationException(
					"XT.COM futures order volume is expressed in whole contracts.");
			if (regMsg.PositionEffect == OrderPositionEffects.CloseOnly)
				throw new NotSupportedException(
					"XT.COM does not expose an atomic reduce-only flag for regular futures orders.");
			RememberPrivateSymbol(section, symbol);
			result = await RestClient.PlaceFuturesOrderAsync(new()
			{
				ClientOrderId = clientOrderId,
				Symbol = symbol,
				PositionSide = condition.PositionSide.ToWire(),
				Side = regMsg.Side == Sides.Buy ? "BUY" : "SELL",
				Type = orderType == OrderTypes.Market ? "MARKET" : "LIMIT",
				TimeInForce = policy.ToWire(),
				Quantity = volume.ToWire(),
				Price = orderType == OrderTypes.Limit ? regMsg.Price.ToWire() : null,
			}, cancellationToken);
		}
		if (result is null)
			throw new InvalidDataException("XT.COM returned an empty order result.");
		if (section == XtSections.Spot && result.OrderId <= 0)
			throw new InvalidDataException("XT.COM Spot accepted the order without returning an order ID.");

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = CurrentTime,
			PortfolioName = _portfolioName,
			Side = regMsg.Side,
			OrderVolume = volume,
			Balance = volume,
			OrderPrice = orderType == OrderTypes.Market ? 0m : regMsg.Price,
			OrderType = orderType,
			OrderState = OrderStates.Active,
			OrderId = result.OrderId > 0 ? result.OrderId : null,
			OrderStringId = result.ClientOrderId.IsEmpty(clientOrderId),
			TransactionId = regMsg.TransactionId,
			OriginalTransactionId = regMsg.TransactionId,
			TimeInForce = regMsg.TimeInForce,
			PostOnly = policy == XtOrderPolicies.PostOnly,
			PositionEffect = regMsg.PositionEffect,
			Condition = regMsg.Condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
		=> throw new NotSupportedException("XT.COM does not expose in-place order modification.");

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		var symbol = GetSymbol(cancelMsg.SecurityId);
		var section = ResolveSection(cancelMsg.SecurityId);
		EnsurePrivateReady(section);
		long orderId;
		if (cancelMsg.OrderId is long numericOrderId)
			orderId = numericOrderId;
		else if (!long.TryParse(cancelMsg.OrderStringId, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out orderId))
			throw new InvalidOperationException("XT.COM cancellation requires an exchange order ID.");
		RememberPrivateSymbol(section, symbol);
		if (section == XtSections.Spot)
			await EnsureSpotPrivateSymbolAsync(symbol, cancellationToken);
		await RestClient.CancelOrderAsync(section, orderId, cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = CurrentTime,
			PortfolioName = _portfolioName,
			OrderId = orderId,
			OrderStringId = cancelMsg.OrderStringId,
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
			throw new NotSupportedException("XT.COM Open API does not expose a close-all-positions endpoint.");
		var symbol = GetSymbol(cancelMsg.SecurityId);
		var section = ResolveSection(cancelMsg.SecurityId);
		if (cancelMsg.SecurityTypes is { Length: > 0 } && !cancelMsg.SecurityTypes.Contains(
			section == XtSections.Spot ? SecurityTypes.CryptoCurrency : SecurityTypes.Future))
			return;
		RememberPrivateSymbol(section, symbol);
		if (section == XtSections.Spot)
			await EnsureSpotPrivateSymbolAsync(symbol, cancellationToken);

		if (cancelMsg.Side is null)
		{
			await RestClient.CancelAllOrdersAsync(section, symbol, cancellationToken);
			return;
		}

		foreach (var order in await RestClient.GetOpenOrdersAsync(section, symbol, cancellationToken))
		{
			if (order.Side.ToStockSharpSide() != cancelMsg.Side.Value)
				continue;
			await RestClient.CancelOrderAsync(section, order.OrderId, cancellationToken);
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
		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = _portfolioName,
			BoardCode = BoardCodes.Xt,
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
		var symbol = statusMsg.SecurityId.SecurityCode?.ToUpperInvariant();
		var section = statusMsg.SecurityId == default || statusMsg.SecurityId.BoardCode.IsEmpty()
			? (XtSections?)null
			: ResolveSection(statusMsg.SecurityId);
		if (!symbol.IsEmpty() && section is XtSections requestedSection)
		{
			RememberPrivateSymbol(requestedSection, symbol);
			if (requestedSection == XtSections.Spot)
				await EnsureSpotPrivateSymbolAsync(symbol, cancellationToken);
		}
		var limit = (statusMsg.Count ?? 100).Min(200).Max(1).To<int>();
		await SendOrderSnapshotAsync(statusMsg.TransactionId, section, symbol,
			statusMsg.From, statusMsg.To, limit, cancellationToken);
		_orderStatusSubscriptionId = statusMsg.TransactionId;
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshotAsync(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (IsSectionEnabled(XtSections.Spot))
		{
			foreach (var balance in await RestClient.GetSpotBalancesAsync(cancellationToken))
				await SendBalanceAsync(balance, XtSections.Spot, null, originalTransactionId,
					CurrentTime, cancellationToken);
		}

		if (IsSectionEnabled(XtSections.Futures))
		{
			var balances = await RestClient.GetFuturesBalancesAsync(cancellationToken);
			foreach (var balance in balances)
				await SendBalanceAsync(balance, XtSections.Futures, null, originalTransactionId,
					CurrentTime, cancellationToken);
			foreach (var position in await RestClient.GetFuturesPositionsAsync(null, cancellationToken))
				await SendPositionAsync(position, originalTransactionId, cancellationToken);
		}
	}

	private async ValueTask SendOrderSnapshotAsync(long originalTransactionId,
		XtSections? requestedSection, string requestedSymbol, DateTime? from, DateTime? to,
		int limit, CancellationToken cancellationToken)
	{
		(XtSections Section, string Symbol)[] symbols;
		if (!requestedSymbol.IsEmpty())
		{
			var symbolSection = requestedSection ?? ResolveSection(requestedSymbol);
			symbols = [(symbolSection, requestedSymbol)];
		}
		else
		{
			symbols = [.. Enumerator.GetValues<XtSections>()
				.Where(section => IsSectionEnabled(section) &&
					(requestedSection is null || requestedSection == section))
				.Select(static section => (section, (string)null))];
		}

		foreach (var item in symbols)
		{
			if (!IsSectionEnabled(item.Section))
				continue;
			var orders = (await RestClient.GetOpenOrdersAsync(item.Section, item.Symbol,
				cancellationToken))
				.Concat(await RestClient.GetOrderHistoryAsync(item.Section, item.Symbol, from, to,
					limit, cancellationToken))
				.GroupBy(static order => order.OrderId)
				.Select(static group => group.First())
				.OrderBy(static order => order.UpdateTime)
				.TakeLast(limit);
			foreach (var order in orders)
				await SendOrderAsync(order, item.Section, originalTransactionId, cancellationToken);

			foreach (var fill in await RestClient.GetFillsAsync(item.Section, item.Symbol, from, to,
				limit, cancellationToken))
				await SendFillAsync(fill, item.Section, fill.Symbol.IsEmpty(item.Symbol), originalTransactionId,
					cancellationToken);
		}
	}

	private async ValueTask OnOrderAsync(XtSections section, XtWsOrderMessage message,
		CancellationToken cancellationToken)
	{
		if (message?.Data is null)
			return;
		message.Data.Symbol ??= message.Symbol;
		RememberPrivateSymbol(section, message.Data.Symbol);
		await SendOrderAsync(message.Data, section, _orderStatusSubscriptionId, cancellationToken);
	}

	private async ValueTask OnFillAsync(XtSections section, XtWsFillMessage message,
		CancellationToken cancellationToken)
	{
		if (message?.Data is null)
			return;
		var symbol = message.Data.Symbol.IsEmpty(message.Symbol);
		RememberPrivateSymbol(section, symbol);
		await SendFillAsync(message.Data, section, symbol, _orderStatusSubscriptionId,
			cancellationToken);
	}

	private async ValueTask OnBalanceAsync(XtSections section, XtWsBalanceMessage message,
		CancellationToken cancellationToken)
	{
		var data = message?.Data;
		if (data is null)
			return;
		var time = data.Timestamp > 0 ? data.Timestamp.ToUtcTime() :
			message.Timestamp > 0 ? message.Timestamp.ToUtcTime() : CurrentTime;
		var depoName = data.Type.EqualsIgnoreCase("CROSS") || data.Type.IsEmpty()
			? null
			: $"{data.Symbol}:{data.Type}";
		foreach (var balance in data.Balances ?? [])
			await SendBalanceAsync(balance, section, depoName, _portfolioSubscriptionId, time,
				cancellationToken);
	}

	private ValueTask OnPositionAsync(XtWsPositionMessage message,
		CancellationToken cancellationToken)
	{
		if (message?.Data is null)
			return default;
		message.Data.Symbol ??= message.Symbol;
		RememberPrivateSymbol(XtSections.Futures, message.Data.Symbol);
		return SendPositionAsync(message.Data, _portfolioSubscriptionId, cancellationToken);
	}

	private ValueTask SendBalanceAsync(XtBalance balance, XtSections section,
		string depoName, long originalTransactionId, DateTime serverTime,
		CancellationToken cancellationToken)
	{
		if (balance?.Coin.IsEmpty() != false)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = balance.Coin.ToStockSharp(section),
			DepoName = depoName,
			ServerTime = serverTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, balance.Free.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.BlockedValue, balance.Frozen.ToDecimal(), true), cancellationToken);
	}

	private ValueTask SendPositionAsync(XtPosition position, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (position?.Symbol.IsEmpty() != false)
			return default;
		var value = position.NetSize.ToDecimal();
		if (value is null)
			value = (position.LongSize.ToDecimal() ?? 0m) - (position.ShortSize.ToDecimal() ?? 0m);
		var side = position.PositionSide.ToStockSharpSide();
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = position.Symbol.ToStockSharp(XtSections.Futures),
			DepoName = position.PositionId,
			ServerTime = position.UpdateTime > 0 ? position.UpdateTime.ToUtcTime() : CurrentTime,
			OriginalTransactionId = originalTransactionId,
			Side = side,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, value?.Abs(), true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.CurrentPrice, position.MarkPrice.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnl.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.LiquidationPrice, position.LiquidationPrice.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.Leverage, position.Leverage.ToDecimal(), true), cancellationToken);
	}

	private ValueTask SendOrderAsync(XtOrder order, XtSections section,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (order?.Symbol.IsEmpty() != false || order.OrderId <= 0)
			return default;
		var volume = order.OriginalSize.ToDecimal() ?? order.Size.ToDecimal();
		var filled = order.FilledSize.ToDecimal() ?? 0m;
		var orderType = order.Type.EqualsIgnoreCase("MARKET") ||
			order.Type.EqualsIgnoreCase("MARKET_QTY") ? OrderTypes.Market : OrderTypes.Limit;
		var condition = section == XtSections.Futures
			? new XtOrderCondition
			{
				PositionSide = order.PositionSide?.ToUpperInvariant() switch
				{
					"LONG" => XtPositionSides.Long,
					"SHORT" => XtPositionSides.Short,
					_ => XtPositionSides.Both,
				},
				Policy = order.TimeInForce?.ToUpperInvariant() switch
				{
					"IOC" => XtOrderPolicies.ImmediateOrCancel,
					"FOK" => XtOrderPolicies.FillOrKill,
					"GTX" => XtOrderPolicies.PostOnly,
					_ => XtOrderPolicies.Regular,
				},
			}
			: null;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToStockSharp(section),
			ServerTime = (order.UpdateTime > 0 ? order.UpdateTime : order.CreateTime) is var time && time > 0
				? time.ToUtcTime()
				: CurrentTime,
			PortfolioName = _portfolioName,
			Side = order.Side.ToStockSharpSide(),
			OrderVolume = volume,
			Balance = volume is null ? null : (volume.Value - filled).Max(0m),
			OrderPrice = order.Price.ToDecimal() ?? 0m,
			AveragePrice = order.AveragePrice.ToDecimal() ??
				CalculateAverage(order.FilledAmount, order.FilledSize),
			OrderType = orderType,
			OrderState = order.Status.ToXtOrderState(),
			OrderId = order.OrderId,
			OrderStringId = order.ClientOrderId,
			TransactionId = ParseTransactionId(order.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
			TimeInForce = order.TimeInForce.EqualsIgnoreCase("FOK") ? TimeInForce.MatchOrCancel :
				order.IsImmediateOrCancel
					? TimeInForce.CancelBalance
					: null,
			PostOnly = order.TimeInForce.EqualsIgnoreCase("GTX"),
			Condition = condition,
		}, cancellationToken);
	}

	private ValueTask SendFillAsync(XtFill fill, XtSections section, string symbol,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (fill is null || symbol.IsEmpty())
			return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = fill.Timestamp > 0 ? fill.Timestamp.ToUtcTime() : CurrentTime,
			PortfolioName = _portfolioName,
			Side = fill.Side.ToStockSharpSide(),
			OrderId = fill.OrderId,
			TradeId = fill.Id > 0 ? fill.Id : null,
			TradePrice = fill.Price.ToDecimal(),
			TradeVolume = fill.Size.ToDecimal(),
			Commission = fill.Fee.ToDecimal(),
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private static decimal? CalculateAverage(string amount, string size)
		=> amount.ToDecimal() is decimal total && size.ToDecimal() is decimal volume && volume != 0m
			? total / volume
			: null;
}
