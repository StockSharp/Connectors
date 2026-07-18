namespace StockSharp.Deepcoin;

public partial class DeepcoinMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var symbol = GetSymbol(regMsg.SecurityId);
		var productType = ResolveProductType(symbol);
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException("Order volume must be positive.");
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
			throw new InvalidOperationException("Limit order price must be positive.");

		var condition = regMsg.Condition as DeepcoinOrderCondition ?? new DeepcoinOrderCondition();
		var policy = condition.Policy;
		if (regMsg.PostOnly == true)
			policy = DeepcoinOrderPolicies.PostOnly;
		else if (regMsg.TimeInForce == TimeInForce.CancelBalance)
			policy = DeepcoinOrderPolicies.ImmediateOrCancel;
		else if (regMsg.TimeInForce == TimeInForce.MatchOrCancel)
			throw new NotSupportedException("Deepcoin V2 does not expose fill-or-kill orders.");
		if (orderType == OrderTypes.Market && policy != DeepcoinOrderPolicies.Regular)
			throw new InvalidOperationException("Deepcoin execution policies apply only to limit orders.");

		var tradingMode = ResolveTradingMode(productType, condition.MarginMode);
		var positionMode = condition.IsSplitPosition
			? DeepcoinPositionModes.Split
			: DeepcoinPositionModes.Merge;
		if (productType == DeepcoinProductTypes.Swap && condition.Leverage is int leverage)
		{
			if (leverage <= 0)
				throw new InvalidOperationException("Leverage must be positive.");
			var leverageResult = await RestClient.SetLeverageAsync(new()
			{
				InstrumentId = symbol,
				Leverage = leverage,
				TradingMode = tradingMode,
				PositionMode = positionMode,
			}, cancellationToken);
			ThrowIfLeverageFailed(leverageResult);
		}

		var isReduceOnly = condition.IsReduceOnly ||
			regMsg.PositionEffect == OrderPositionEffects.CloseOnly;
		if (productType == DeepcoinProductTypes.Swap && condition.IsSplitPosition &&
			isReduceOnly && condition.ClosePositionId.IsEmpty())
			throw new InvalidOperationException(
				"A Deepcoin position ID is required to close a split position.");

		var clientOrderId = CreateClientOrderId(regMsg.TransactionId, regMsg.UserOrderId);
		var result = await RestClient.PlaceOrderAsync(new()
		{
			InstrumentId = symbol,
			TradingMode = tradingMode,
			ClientOrderId = clientOrderId,
			Side = regMsg.Side == Sides.Buy ? DeepcoinSides.Buy : DeepcoinSides.Sell,
			PositionSide = productType == DeepcoinProductTypes.Swap
				? condition.PositionSide
				: null,
			PositionMode = productType == DeepcoinProductTypes.Swap ? positionMode : null,
			ClosePositionId = productType == DeepcoinProductTypes.Swap
				? condition.ClosePositionId
				: null,
			OrderType = policy.ToDeepcoin(orderType),
			Size = volume.ToWire(),
			Price = orderType == OrderTypes.Limit ? regMsg.Price.ToWire() : null,
			IsReduceOnly = isReduceOnly ? true : null,
			TargetCurrency = productType == DeepcoinProductTypes.Spot &&
				orderType == OrderTypes.Market
					? condition.IsQuoteVolume
						? DeepcoinTargetCurrencies.QuoteCurrency
						: DeepcoinTargetCurrencies.BaseCurrency
					: null,
			TakeProfitTriggerPrice = ToTriggerPrice(condition.TakeProfitTriggerPrice),
			StopLossTriggerPrice = ToTriggerPrice(condition.StopLossTriggerPrice),
		}, cancellationToken);
		ThrowIfOperationFailed(result, "place order");
		if (result.OrderId.IsEmpty())
			throw new InvalidDataException("Deepcoin accepted the order without returning an order ID.");

		RegisterInstrument(symbol);
		using (_sync.EnterScope())
			_orderInstruments[result.OrderId] = symbol;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(),
			ServerTime = CurrentTime,
			PortfolioName = GetPortfolioName(productType),
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
			PostOnly = policy == DeepcoinOrderPolicies.PostOnly,
			PositionEffect = isReduceOnly
				? OrderPositionEffects.CloseOnly
				: regMsg.PositionEffect,
			Condition = condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		if (replaceMsg.OrderType == OrderTypes.Market)
			throw new NotSupportedException("Deepcoin can amend active limit orders only.");
		if (replaceMsg.Price <= 0)
			throw new InvalidOperationException("Replacement price must be positive.");
		var volume = replaceMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException("Replacement volume must be positive.");

		var orderId = replaceMsg.OldOrderStringId;
		if (orderId.IsEmpty() && replaceMsg.OldOrderId is long numericOrderId)
			orderId = numericOrderId.ToString(CultureInfo.InvariantCulture);
		if (orderId.IsEmpty())
			throw new InvalidOperationException("Deepcoin replacement requires an exchange order ID.");

		var condition = replaceMsg.Condition as DeepcoinOrderCondition;
		var result = await RestClient.AmendOrderAsync(new()
		{
			OrderId = orderId,
			NewPrice = replaceMsg.Price,
			NewSize = volume,
			NewTakeProfitTriggerPrice = condition?.TakeProfitTriggerPrice,
			NewStopLossTriggerPrice = condition?.StopLossTriggerPrice,
		}, cancellationToken);
		ThrowIfOperationFailed(result, "amend order");
		var resultOrderId = result.OrderId.IsEmpty() ? orderId : result.OrderId;
		var symbol = GetSymbol(replaceMsg.SecurityId);
		var productType = ResolveProductType(symbol);
		using (_sync.EnterScope())
			_orderInstruments[resultOrderId] = symbol;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(),
			ServerTime = CurrentTime,
			PortfolioName = GetPortfolioName(productType),
			Side = replaceMsg.Side,
			OrderVolume = volume,
			Balance = volume,
			OrderPrice = replaceMsg.Price,
			OrderType = OrderTypes.Limit,
			OrderState = OrderStates.Active,
			OrderStringId = resultOrderId,
			TransactionId = replaceMsg.TransactionId,
			OriginalTransactionId = replaceMsg.TransactionId,
			TimeInForce = replaceMsg.TimeInForce,
			PostOnly = replaceMsg.PostOnly,
			PositionEffect = replaceMsg.PositionEffect,
			Condition = replaceMsg.Condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var symbol = GetSymbol(cancelMsg.SecurityId);
		var orderId = cancelMsg.OrderStringId;
		if (orderId.IsEmpty() && cancelMsg.OrderId is long numericOrderId)
			orderId = numericOrderId.ToString(CultureInfo.InvariantCulture);
		if (orderId.IsEmpty())
			throw new InvalidOperationException("Deepcoin cancellation requires an exchange order ID.");

		var result = await RestClient.CancelOrderAsync(new()
		{
			InstrumentId = symbol,
			OrderId = orderId,
		}, cancellationToken);
		ThrowIfOperationFailed(result, "cancel order");

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(),
			ServerTime = CurrentTime,
			PortfolioName = GetPortfolioName(ResolveProductType(symbol)),
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
				"Deepcoin batch cancellation does not close open positions.");
		if (cancelMsg.SecurityTypes is { Length: > 0 } &&
			!cancelMsg.SecurityTypes.Any(static type =>
				type is SecurityTypes.CryptoCurrency or SecurityTypes.Future))
			return;

		var symbol = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetSymbol(cancelMsg.SecurityId);
		var pending = await LoadPendingOrdersAsync(symbol, 10000, cancellationToken);
		var orderIds = pending
			.Where(order => order?.OrderId.IsEmpty() == false &&
				(cancelMsg.Side is null || order.Side.ToStockSharpSide() == cancelMsg.Side))
			.Select(static order => order.OrderId)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		for (var offset = 0; offset < orderIds.Length; offset += 50)
		{
			var result = await RestClient.BatchCancelAsync(new()
			{
				OrderIds = [.. orderIds.Skip(offset).Take(50)],
			}, cancellationToken);
			if (result?.Errors is { Length: > 0 })
			{
				var error = result.Errors[0];
				throw new InvalidOperationException(
					$"Deepcoin failed to cancel order {error.OrderId} ({error.Code}): {error.Message}".Trim());
			}
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
			await PrivateWsClient.UnsubscribeTableAsync(DeepcoinPrivateTables.Account,
				cancellationToken);
			await PrivateWsClient.UnsubscribeTableAsync(DeepcoinPrivateTables.Position,
				cancellationToken);
			return;
		}

		await EnsureInstrumentMapsAsync(cancellationToken);
		foreach (var productType in new[] { DeepcoinProductTypes.Spot, DeepcoinProductTypes.Swap })
			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = GetPortfolioName(productType),
				BoardCode = BoardCodes.Deepcoin,
				OriginalTransactionId = lookupMsg.TransactionId,
			}, cancellationToken);
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
			return;
		}
		_portfolioSubscriptionId = lookupMsg.TransactionId;
		await PrivateWsClient.SubscribeTableAsync(DeepcoinPrivateTables.Account,
			cancellationToken);
		await PrivateWsClient.SubscribeTableAsync(DeepcoinPrivateTables.Position,
			cancellationToken);
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
			await PrivateWsClient.UnsubscribeTableAsync(DeepcoinPrivateTables.Order,
				cancellationToken);
			await PrivateWsClient.UnsubscribeTableAsync(DeepcoinPrivateTables.Trade,
				cancellationToken);
			return;
		}

		await EnsureInstrumentMapsAsync(cancellationToken);
		var symbol = statusMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetSymbol(statusMsg.SecurityId);
		var limit = (statusMsg.Count ?? 100).Min(1000).Max(1).To<int>();
		await SendOrderSnapshotAsync(statusMsg.TransactionId, symbol, statusMsg.From,
			statusMsg.To, limit, cancellationToken);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
			return;
		}
		_orderStatusSubscriptionId = statusMsg.TransactionId;
		await PrivateWsClient.SubscribeTableAsync(DeepcoinPrivateTables.Order,
			cancellationToken);
		await PrivateWsClient.SubscribeTableAsync(DeepcoinPrivateTables.Trade,
			cancellationToken);
	}

	private async ValueTask EnsureInstrumentMapsAsync(CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
		{
			if (_instrumentMapsLoaded)
				return;
		}

		foreach (var productType in new[] { DeepcoinProductTypes.Spot, DeepcoinProductTypes.Swap })
		{
			foreach (var instrument in await RestClient.GetInstrumentsAsync(productType, null,
				cancellationToken) ?? [])
				RegisterInstrument(instrument?.InstrumentId);
		}
		using (_sync.EnterScope())
			_instrumentMapsLoaded = true;
	}

	private async ValueTask SendPortfolioSnapshotAsync(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		foreach (var productType in new[] { DeepcoinProductTypes.Spot, DeepcoinProductTypes.Swap })
		{
			foreach (var balance in await RestClient.GetBalancesAsync(productType,
				cancellationToken) ?? [])
				await SendBalanceAsync(productType, balance, originalTransactionId, cancellationToken);
			foreach (var position in await RestClient.GetPositionsAsync(productType, null,
				cancellationToken) ?? [])
				await SendPositionAsync(position, originalTransactionId, cancellationToken);
		}
	}

	private async ValueTask SendOrderSnapshotAsync(long originalTransactionId, string symbol,
		DateTime? from, DateTime? to, int limit, CancellationToken cancellationToken)
	{
		var productTypes = symbol.IsEmpty()
			? new[] { DeepcoinProductTypes.Spot, DeepcoinProductTypes.Swap }
			: [ResolveProductType(symbol)];
		var orders = new List<DeepcoinOrder>();
		orders.AddRange(await LoadPendingOrdersAsync(symbol, limit, cancellationToken));
		foreach (var productType in productTypes)
			orders.AddRange(await LoadOrderHistoryAsync(productType, symbol, limit,
				cancellationToken));

		var fromUtc = from?.ToUniversalTime();
		var toUtc = to?.ToUniversalTime();
		foreach (var order in orders
			.Where(static item => item?.OrderId.IsEmpty() == false)
			.GroupBy(static item => item.OrderId, StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.OrderByDescending(GetOrderTime).First())
			.Where(item => IsWithin(GetOrderTime(item), fromUtc, toUtc))
			.OrderBy(GetOrderTime)
			.TakeLast(limit))
			await SendOrderAsync(order, originalTransactionId, cancellationToken);

		var fills = new List<DeepcoinFill>();
		foreach (var productType in productTypes)
			fills.AddRange(await LoadFillsAsync(productType, symbol, fromUtc, toUtc, limit,
				cancellationToken));
		foreach (var fill in fills
			.Where(static item => item?.TradeId.IsEmpty() == false)
			.GroupBy(static item => item.BillId.IsEmpty() ? item.TradeId : item.BillId,
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.First())
			.OrderBy(static item => item.Timestamp.ToInt64())
			.TakeLast(limit))
			await SendFillAsync(fill, originalTransactionId, false, cancellationToken);
	}

	private async ValueTask<DeepcoinOrder[]> LoadPendingOrdersAsync(string symbol, int maximum,
		CancellationToken cancellationToken)
	{
		var result = new List<DeepcoinOrder>();
		for (var page = 1; result.Count < maximum; page++)
		{
			var limit = (maximum - result.Count).Min(100).Max(1);
			var items = await RestClient.GetPendingOrdersAsync(new()
			{
				InstrumentId = symbol,
				Page = page,
				Limit = limit,
			}, cancellationToken) ?? [];
			result.AddRange(items);
			if (items.Length < limit)
				break;
		}
		return [.. result.Take(maximum)];
	}

	private async ValueTask<DeepcoinOrder[]> LoadOrderHistoryAsync(
		DeepcoinProductTypes productType, string symbol, int maximum,
		CancellationToken cancellationToken)
	{
		var result = new List<DeepcoinOrder>();
		string cursor = null;
		while (result.Count < maximum)
		{
			var limit = (maximum - result.Count).Min(100).Max(1);
			var items = await RestClient.GetOrderHistoryAsync(new()
			{
				ProductType = productType,
				InstrumentId = symbol,
				EndId = cursor,
				Limit = limit,
			}, cancellationToken) ?? [];
			result.AddRange(items);
			var next = items.LastOrDefault()?.OrderId;
			if (items.Length < limit || next.IsEmpty() || next.EqualsIgnoreCase(cursor))
				break;
			cursor = next;
		}
		return [.. result.Take(maximum)];
	}

	private async ValueTask<DeepcoinFill[]> LoadFillsAsync(DeepcoinProductTypes productType,
		string symbol, DateTime? from, DateTime? to, int maximum,
		CancellationToken cancellationToken)
	{
		var result = new List<DeepcoinFill>();
		string cursor = null;
		while (result.Count < maximum)
		{
			var limit = (maximum - result.Count).Min(100).Max(1);
			var items = await RestClient.GetFillsAsync(new()
			{
				ProductType = productType,
				InstrumentId = symbol,
				Before = cursor,
				StartTime = from?.ToUnixMilliseconds(),
				EndTime = to?.ToUnixMilliseconds(),
				Limit = limit,
			}, cancellationToken) ?? [];
			result.AddRange(items);
			var next = items.LastOrDefault()?.BillId;
			if (items.Length < limit || next.IsEmpty() || next.EqualsIgnoreCase(cursor))
				break;
			cursor = next;
		}
		return [.. result.Take(maximum)];
	}

	private ValueTask OnPrivateAssetAsync(DeepcoinPrivateAsset asset,
		CancellationToken cancellationToken)
	{
		if (asset?.Currency.IsEmpty() != false)
			return default;
		var blocked = (asset.Balance - asset.Available).Max(0m);
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(DeepcoinProductTypes.Swap),
			SecurityId = asset.Currency.ToStockSharp(),
			ServerTime = CurrentTime,
			OriginalTransactionId = _portfolioSubscriptionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, asset.Balance, true)
		.TryAdd(PositionChangeTypes.BlockedValue, blocked, true)
		.TryAdd(PositionChangeTypes.RealizedPnL, asset.CloseProfit, true), cancellationToken);
	}

	private ValueTask OnPrivateOrderAsync(DeepcoinPrivateOrder order,
		CancellationToken cancellationToken)
	{
		if (order?.OrderId.IsEmpty() != false)
			return default;
		var isSwap = order.PositionDirection != DeepcoinLegacyPositionDirections.Net;
		var instrumentId = ResolvePrivateInstrument(order.InstrumentId, isSwap);
		if (instrumentId.IsEmpty())
			return SendOutErrorAsync(new InvalidDataException(
				$"Deepcoin private order symbol '{order.InstrumentId}' is unknown."), cancellationToken);
		var productType = ResolveProductType(instrumentId);
		using (_sync.EnterScope())
			_orderInstruments[order.OrderId] = instrumentId;
		var positionSide = order.PositionDirection == DeepcoinLegacyPositionDirections.Short
			? DeepcoinPositionSides.Short
			: DeepcoinPositionSides.Long;
		var condition = new DeepcoinOrderCondition
		{
			MarginMode = order.IsCrossMarginValue == 1
				? DeepcoinMarginModes.Cross
				: productType == DeepcoinProductTypes.Spot
					? DeepcoinMarginModes.Cash
					: DeepcoinMarginModes.Isolated,
			PositionSide = positionSide,
			Leverage = order.Leverage is > 0 and <= int.MaxValue
				? decimal.ToInt32(order.Leverage)
				: null,
			IsReduceOnly = order.OffsetFlag != "0",
		};
		var orderType = order.PriceType == "1" ? OrderTypes.Market : OrderTypes.Limit;
		var serverTime = order.UpdateMilliseconds > 0
			? order.UpdateMilliseconds.ToUtcTime()
			: (order.UpdateTime > 0 ? order.UpdateTime : order.InsertTime).ToUtcTime();
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = instrumentId.ToStockSharp(),
			ServerTime = serverTime,
			PortfolioName = GetPortfolioName(productType),
			Side = order.Direction.ToStockSharpSide(),
			OrderVolume = order.Volume,
			Balance = (order.Volume - order.FilledVolume).Max(0m),
			OrderPrice = orderType == OrderTypes.Market ? 0m : order.Price,
			AveragePrice = order.AveragePrice > 0 ? order.AveragePrice : null,
			OrderType = orderType,
			OrderState = order.State.ToStockSharpOrderState(),
			OrderStringId = order.OrderId,
			TransactionId = ParseTransactionId(order.LocalId),
			OriginalTransactionId = _orderStatusSubscriptionId,
			PositionEffect = condition.IsReduceOnly ? OrderPositionEffects.CloseOnly : null,
			Condition = condition,
		}, cancellationToken);
	}

	private ValueTask OnPrivatePositionAsync(DeepcoinPrivatePosition position,
		CancellationToken cancellationToken)
	{
		var instrumentId = ResolvePrivateInstrument(position?.InstrumentId, true);
		if (instrumentId.IsEmpty())
			return default;
		var side = position.Direction == DeepcoinLegacyPositionDirections.Short
			? Sides.Sell
			: Sides.Buy;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(DeepcoinProductTypes.Swap),
			SecurityId = instrumentId.ToStockSharp(),
			ServerTime = position.UpdateTime > 0 ? position.UpdateTime.ToUtcTime() : CurrentTime,
			OriginalTransactionId = _portfolioSubscriptionId,
			Side = side,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, position.Position.Abs(), true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice, true)
		.TryAdd(PositionChangeTypes.RealizedPnL, position.CloseProfit, true)
		.TryAdd(PositionChangeTypes.Leverage, position.Leverage, true), cancellationToken);
	}

	private ValueTask OnPrivateTradeAsync(DeepcoinPrivateTrade trade,
		CancellationToken cancellationToken)
	{
		if (trade?.TradeId.IsEmpty() != false)
			return default;
		string instrumentId;
		using (_sync.EnterScope())
			_orderInstruments.TryGetValue(trade.OrderId ?? string.Empty, out instrumentId);
		instrumentId ??= ResolvePrivateInstrument(trade.InstrumentId, true);
		if (instrumentId.IsEmpty())
			return SendOutErrorAsync(new InvalidDataException(
				$"Deepcoin private trade symbol '{trade.InstrumentId}' is unknown."), cancellationToken);
		using (_sync.EnterScope())
		{
			if (!_seenFillIds.Add(trade.TradeId))
				return default;
		}
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = instrumentId.ToStockSharp(),
			ServerTime = trade.TradeTime > 0 ? trade.TradeTime.ToUtcTime() : CurrentTime,
			PortfolioName = GetPortfolioName(ResolveProductType(instrumentId)),
			Side = trade.Direction.ToStockSharpSide(),
			OrderStringId = trade.OrderId,
			TradeStringId = trade.TradeId,
			TradePrice = trade.Price,
			TradeVolume = trade.Volume,
			Commission = trade.Fee,
			OriginalTransactionId = _orderStatusSubscriptionId,
		}, cancellationToken);
	}

	private ValueTask SendBalanceAsync(DeepcoinProductTypes productType, DeepcoinBalance balance,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (balance?.Currency.IsEmpty() != false)
			return default;
		var total = balance.Equity.ToDecimal() ?? balance.Balance.ToDecimal();
		var blocked = balance.FrozenBalance.ToDecimal();
		if (blocked is null && total is decimal totalValue &&
			balance.AvailableBalance.ToDecimal() is decimal available)
			blocked = (totalValue - available).Max(0m);
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(productType),
			SecurityId = balance.Currency.ToStockSharp(),
			ServerTime = CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, total, true)
		.TryAdd(PositionChangeTypes.BlockedValue, blocked, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, balance.UnrealizedProfit.ToDecimal(), true),
			cancellationToken);
	}

	private ValueTask SendPositionAsync(DeepcoinPosition position, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (position?.InstrumentId.IsEmpty() != false)
			return default;
		RegisterInstrument(position.InstrumentId);
		var side = position.PositionSide == DeepcoinPositionSides.Short ? Sides.Sell : Sides.Buy;
		var serverTime = position.UpdateTime.ToUtcTime() ??
			position.CreateTime.ToUtcTime() ?? CurrentTime;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(position.ProductType),
			SecurityId = position.InstrumentId.ToStockSharp(),
			DepoName = position.PositionId,
			ServerTime = serverTime,
			OriginalTransactionId = originalTransactionId,
			Side = side,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, position.Position.ToDecimal()?.Abs(), true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.CurrentPrice, position.LastPrice.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedProfit.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.LiquidationPrice, position.LiquidationPrice.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.Leverage, position.Leverage.ToDecimal(), true), cancellationToken);
	}

	private ValueTask SendOrderAsync(DeepcoinOrder order, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (order?.InstrumentId.IsEmpty() != false || order.OrderId.IsEmpty())
			return default;
		RegisterInstrument(order.InstrumentId);
		using (_sync.EnterScope())
			_orderInstruments[order.OrderId] = order.InstrumentId;
		var volume = order.Size.ToDecimal();
		var filled = order.AccumulatedFillSize.ToDecimal() ?? 0m;
		var policy = order.OrderType switch
		{
			DeepcoinApiOrderTypes.PostOnly => DeepcoinOrderPolicies.PostOnly,
			DeepcoinApiOrderTypes.ImmediateOrCancel => DeepcoinOrderPolicies.ImmediateOrCancel,
			_ => DeepcoinOrderPolicies.Regular,
		};
		var condition = new DeepcoinOrderCondition
		{
			MarginMode = order.TradingMode switch
			{
				DeepcoinTradingModes.Cash => DeepcoinMarginModes.Cash,
				DeepcoinTradingModes.Isolated => DeepcoinMarginModes.Isolated,
				_ => DeepcoinMarginModes.Cross,
			},
			PositionSide = order.PositionSide ?? DeepcoinPositionSides.Long,
			IsSplitPosition = order.PositionMode == DeepcoinPositionModes.Split,
			Policy = policy,
			Leverage = int.TryParse(order.Leverage, NumberStyles.Integer,
				CultureInfo.InvariantCulture, out var leverage) ? leverage : null,
			IsReduceOnly = order.IsReduceOnly == true,
			IsQuoteVolume = order.TargetCurrency == DeepcoinTargetCurrencies.QuoteCurrency,
			TakeProfitTriggerPrice = order.TakeProfitTriggerPrice.ToDecimal(),
			StopLossTriggerPrice = order.StopLossTriggerPrice.ToDecimal(),
		};
		var orderType = order.OrderType == DeepcoinApiOrderTypes.Market
			? OrderTypes.Market
			: OrderTypes.Limit;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.InstrumentId.ToStockSharp(),
			ServerTime = GetOrderTime(order),
			PortfolioName = GetPortfolioName(order.ProductType),
			Side = order.Side.ToStockSharpSide(),
			OrderVolume = volume,
			Balance = volume is null ? null : (volume.Value - filled).Max(0m),
			OrderPrice = order.Price.ToDecimal() ?? 0m,
			AveragePrice = order.AveragePrice.ToDecimal(),
			OrderType = orderType,
			OrderState = order.State.ToStockSharpOrderState(),
			OrderStringId = order.OrderId,
			TransactionId = ParseTransactionId(order.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
			TimeInForce = policy == DeepcoinOrderPolicies.ImmediateOrCancel
				? TimeInForce.CancelBalance
				: null,
			PostOnly = policy == DeepcoinOrderPolicies.PostOnly,
			PositionEffect = condition.IsReduceOnly ? OrderPositionEffects.CloseOnly : null,
			Commission = order.Fee.ToDecimal(),
			Condition = condition,
		}, cancellationToken);
	}

	private ValueTask SendFillAsync(DeepcoinFill fill, long originalTransactionId, bool onlyNew,
		CancellationToken cancellationToken)
	{
		if (fill?.InstrumentId.IsEmpty() != false || fill.TradeId.IsEmpty())
			return default;
		var key = fill.BillId.IsEmpty() ? fill.TradeId : fill.BillId;
		using (_sync.EnterScope())
		{
			var added = _seenFillIds.Add(key);
			if (onlyNew && !added)
				return default;
			if (!fill.OrderId.IsEmpty())
				_orderInstruments[fill.OrderId] = fill.InstrumentId;
		}
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = fill.InstrumentId.ToStockSharp(),
			ServerTime = fill.Timestamp.ToUtcTime() ?? CurrentTime,
			PortfolioName = GetPortfolioName(fill.ProductType),
			Side = fill.Side.ToStockSharpSide(),
			OrderStringId = fill.OrderId,
			TradeStringId = fill.TradeId,
			TradePrice = fill.Price.ToDecimal(),
			TradeVolume = fill.Size.ToDecimal(),
			Commission = fill.Fee.ToDecimal(),
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private static DeepcoinTradingModes ResolveTradingMode(DeepcoinProductTypes productType,
		DeepcoinMarginModes marginMode)
	{
		var result = marginMode switch
		{
			DeepcoinMarginModes.Auto => productType == DeepcoinProductTypes.Spot
				? DeepcoinTradingModes.Cash
				: DeepcoinTradingModes.Cross,
			DeepcoinMarginModes.Cash => DeepcoinTradingModes.Cash,
			DeepcoinMarginModes.Cross => DeepcoinTradingModes.Cross,
			DeepcoinMarginModes.Isolated => DeepcoinTradingModes.Isolated,
			_ => throw new ArgumentOutOfRangeException(nameof(marginMode), marginMode, null),
		};
		if (productType == DeepcoinProductTypes.Swap && result == DeepcoinTradingModes.Cash)
			throw new InvalidOperationException("Cash mode is not valid for Deepcoin perpetuals.");
		return result;
	}

	private static string ToTriggerPrice(decimal? price)
		=> price is > 0 ? price.Value.ToWire() : null;

	private static DateTime GetOrderTime(DeepcoinOrder order)
		=> order?.UpdateTime.ToUtcTime() ?? order?.CreateTime.ToUtcTime() ?? DateTime.MinValue;

	private static bool IsWithin(DateTime value, DateTime? from, DateTime? to)
		=> (from is null || value >= from) && (to is null || value <= to);

	private static void ThrowIfOperationFailed(DeepcoinOperationResult result, string operation)
	{
		if (result is null)
			throw new InvalidDataException($"Deepcoin returned no result for {operation}.");
		if (!result.Code.IsEmpty() && result.Code != "0")
			throw new InvalidOperationException(
				$"Deepcoin failed to {operation} ({result.Code}): {result.Message}".Trim());
	}

	private static void ThrowIfLeverageFailed(DeepcoinSetLeverageResult result)
	{
		if (result is null)
			throw new InvalidDataException("Deepcoin returned no result for set leverage.");
		if (!result.Code.IsEmpty() && result.Code != "0")
			throw new InvalidOperationException(
				$"Deepcoin failed to set leverage ({result.Code}): {result.Message}".Trim());
	}
}
