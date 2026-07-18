namespace StockSharp.Weex;

public partial class WeexMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		var symbol = regMsg.SecurityId.SecurityCode.ThrowIfEmpty(nameof(regMsg.SecurityId.SecurityCode)).ToUpperInvariant();
		var section = ResolveSection(regMsg.SecurityId);
		EnsurePrivateReady(section);
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException("Order volume must be positive.");

		var clientOrderId = CreateClientOrderId(regMsg.TransactionId, regMsg.UserOrderId);
		var condition = regMsg.Condition as WeexOrderCondition;
		WeexOrderActionResult result;
		if (section == WeexSections.Spot)
		{
			if (regMsg.OrderType == OrderTypes.Conditional || regMsg.Condition is not null)
				throw new NotSupportedException("WEEX Spot V3 does not expose conditional orders.");
			var type = regMsg.OrderType switch
			{
				null or OrderTypes.Limit => WeexOrderTypes.Limit,
				OrderTypes.Market => WeexOrderTypes.Market,
				_ => throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, 0)),
			};
			if (type == WeexOrderTypes.Limit && regMsg.Price <= 0)
				throw new InvalidOperationException("Limit order price must be positive.");
			if (type == WeexOrderTypes.Market && regMsg.PostOnly == true)
				throw new InvalidOperationException("Market order cannot be post-only.");

			result = await RestClient.RegisterSpotOrderAsync(new WeexSpotOrderRequest
			{
				Symbol = symbol,
				Side = regMsg.Side.ToNative(),
				Type = type,
				TimeInForce = type == WeexOrderTypes.Limit ? ToTimeInForce(regMsg.TimeInForce, regMsg.PostOnly) : null,
				Quantity = volume.ToWire(),
				Price = type == WeexOrderTypes.Limit ? regMsg.Price.ToWire() : null,
				ClientOrderId = clientOrderId,
			}, cancellationToken);
		}
		else if (regMsg.OrderType == OrderTypes.Conditional || condition?.ActivationPrice is not null)
		{
			if (condition?.ActivationPrice is not decimal triggerPrice || triggerPrice <= 0)
				throw new InvalidOperationException("Conditional order requires a positive trigger price.");
			if (condition.ClosePositionPrice is decimal closePrice && closePrice <= 0)
				throw new InvalidOperationException("Conditional limit price must be positive.");

			var isLimit = condition.ClosePositionPrice is not null;
			var type = (condition.Type, isLimit) switch
			{
				(WeexOrderConditionTypes.StopLoss, true) => WeexOrderTypes.Stop,
				(WeexOrderConditionTypes.StopLoss, false) => WeexOrderTypes.StopMarket,
				(WeexOrderConditionTypes.TakeProfit, true) => WeexOrderTypes.TakeProfit,
				(WeexOrderConditionTypes.TakeProfit, false) => WeexOrderTypes.TakeProfitMarket,
				_ => throw new ArgumentOutOfRangeException(nameof(condition.Type), condition.Type, LocalizedStrings.InvalidValue),
			};

			var workingType = ToWorkingType(condition.TriggerPriceType);
			result = await RestClient.RegisterAlgoOrderAsync(new WeexAlgoOrderRequest
			{
				Symbol = symbol,
				Side = regMsg.Side.ToNative(),
				PositionSide = ResolvePositionSide(regMsg.Side, regMsg.PositionEffect, condition.PositionSide),
				Type = type,
				Quantity = volume.ToWire(),
				Price = condition.ClosePositionPrice?.ToWire(),
				TriggerPrice = triggerPrice.ToWire(),
				ClientAlgoId = clientOrderId,
				TakeProfitWorkingType = condition.Type == WeexOrderConditionTypes.TakeProfit ? workingType : null,
				StopLossWorkingType = condition.Type == WeexOrderConditionTypes.StopLoss ? workingType : null,
			}, cancellationToken);
		}
		else
		{
			var type = regMsg.OrderType switch
			{
				null or OrderTypes.Limit => WeexOrderTypes.Limit,
				OrderTypes.Market => WeexOrderTypes.Market,
				_ => throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, 0)),
			};
			if (type == WeexOrderTypes.Limit && regMsg.Price <= 0)
				throw new InvalidOperationException("Limit order price must be positive.");
			if (type == WeexOrderTypes.Market && regMsg.PostOnly == true)
				throw new InvalidOperationException("Market order cannot be post-only.");

			result = await RestClient.RegisterFuturesOrderAsync(new WeexFuturesOrderRequest
			{
				Symbol = symbol,
				Side = regMsg.Side.ToNative(),
				PositionSide = ResolvePositionSide(regMsg.Side, regMsg.PositionEffect, condition?.PositionSide),
				Type = type,
				TimeInForce = type == WeexOrderTypes.Limit ? ToTimeInForce(regMsg.TimeInForce, regMsg.PostOnly) : null,
				Quantity = volume.ToWire(),
				Price = type == WeexOrderTypes.Limit ? regMsg.Price.ToWire() : null,
				ClientOrderId = clientOrderId,
				TakeProfitPrice = condition?.TakeProfitPrice?.ToWire(),
				StopLossPrice = condition?.StopLossPrice?.ToWire(),
				TakeProfitWorkingType = condition?.TakeProfitPrice is null ? null : ToWorkingType(condition.TriggerPriceType),
				StopLossWorkingType = condition?.StopLossPrice is null ? null : ToWorkingType(condition.TriggerPriceType),
			}, cancellationToken);
		}

		ValidateAction(result, "place order");
		RememberOrderSection(result.OrderId, section);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = result.TransactionTime > 0 ? result.TransactionTime.ToUtcTime() : CurrentTime,
			PortfolioName = _portfolioName,
			Side = regMsg.Side,
			OrderVolume = volume,
			Balance = volume,
			OrderPrice = condition?.ClosePositionPrice ??
				(regMsg.OrderType == OrderTypes.Market ? 0m : regMsg.Price),
			OrderType = regMsg.OrderType ?? OrderTypes.Limit,
			OrderState = OrderStates.Active,
			OrderId = ParseLong(result.OrderId),
			OrderStringId = result.ClientOrderId.IsEmpty(clientOrderId),
			TransactionId = regMsg.TransactionId,
			OriginalTransactionId = regMsg.TransactionId,
			TimeInForce = regMsg.TimeInForce,
			PostOnly = regMsg.PostOnly,
			PositionEffect = regMsg.PositionEffect,
			Condition = regMsg.Condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
		=> throw new NotSupportedException("WEEX V3 does not expose in-place order modification.");

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		var section = ResolveSection(cancelMsg.SecurityId);
		EnsurePrivateReady(section);
		var orderId = cancelMsg.OrderId?.ToString(CultureInfo.InvariantCulture);
		if (orderId.IsEmpty())
			throw new InvalidOperationException("WEEX order cancellation requires a numeric exchange order ID.");
		var symbol = cancelMsg.SecurityId.SecurityCode.ThrowIfEmpty(nameof(cancelMsg.SecurityId.SecurityCode)).ToUpperInvariant();
		var isConditional = section == WeexSections.Futures &&
			(cancelMsg.OrderType == OrderTypes.Conditional || cancelMsg.Condition is not null);
		var result = await RestClient.CancelOrderAsync(section, symbol, orderId,
			isConditional, cancellationToken);
		ValidateAction(result, "cancel order");

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = CurrentTime,
			PortfolioName = _portfolioName,
			OrderId = cancelMsg.OrderId,
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
		var symbol = cancelMsg.SecurityId.SecurityCode?.ToUpperInvariant();
		var sections = cancelMsg.SecurityId == default || cancelMsg.SecurityId.BoardCode.IsEmpty()
			? Sections.ToArray()
			: [ResolveSection(cancelMsg.SecurityId)];

		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
		{
			if (!sections.Contains(WeexSections.Futures))
				throw new NotSupportedException("WEEX position closing is available only for futures.");
			foreach (var position in await RestClient.GetPositionsAsync(symbol, cancellationToken) ?? [])
			{
				if (cancelMsg.Side is Sides side && position.Side.ToStockSharpNullable() != side)
					continue;
				var results = await RestClient.ClosePositionsAsync(position.Symbol, position.Id,
					cancellationToken) ?? [];
				foreach (var result in results.Where(static item => !item.IsSuccess))
					throw new InvalidOperationException($"WEEX close position failed: {result.ErrorMessage}");
				await SendPositionAsync(position, cancelMsg.TransactionId, cancellationToken, 0m);
			}
		}

		foreach (var section in sections)
		{
			if (cancelMsg.IsStop is not true)
				await CancelOrderSetAsync(section, symbol, false, cancelMsg, cancellationToken);
			if (section == WeexSections.Futures && cancelMsg.IsStop is not false)
				await CancelOrderSetAsync(section, symbol, true, cancelMsg, cancellationToken);
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
			BoardCode = BoardCodes.Weex,
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
			? (WeexSections?)null
			: ResolveSection(statusMsg.SecurityId);
		var limit = (statusMsg.Count ?? 100).Min(1000).Max(1).To<int>();
		await SendOrderSnapshotAsync(statusMsg.TransactionId, section, symbol,
			(statusMsg.From, statusMsg.To), limit, cancellationToken);
		_orderStatusSubscriptionId = statusMsg.TransactionId;
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private async ValueTask CancelOrderSetAsync(WeexSections section, string symbol, bool isConditional,
		OrderGroupCancelMessage message, CancellationToken cancellationToken)
	{
		if (message.Side is null && message.SecurityTypes is not { Length: > 0 })
		{
			var results = await RestClient.CancelAllOrdersAsync(section, symbol, isConditional,
				cancellationToken) ?? [];
			foreach (var result in results)
				ValidateAction(result, "cancel all orders");
			return;
		}

		if (message.SecurityTypes is { Length: > 0 } && !message.SecurityTypes.Contains(
			section == WeexSections.Spot ? SecurityTypes.CryptoCurrency : SecurityTypes.Future))
			return;

		if (isConditional)
		{
			foreach (var order in await RestClient.GetOpenAlgoOrdersAsync(symbol, cancellationToken) ?? [])
			{
				if (message.Side is Sides side && order.Side.ToStockSharp() != side)
					continue;
				ValidateAction(await RestClient.CancelOrderAsync(section, order.Symbol, order.OrderId,
					true, cancellationToken), "cancel conditional order");
			}
			return;
		}

		foreach (var order in await RestClient.GetOpenOrdersAsync(section, symbol, cancellationToken) ?? [])
		{
			if (message.Side is Sides side && order.Side.ToStockSharp() != side)
				continue;
			ValidateAction(await RestClient.CancelOrderAsync(section, order.Symbol, order.OrderId,
				false, cancellationToken), "cancel order");
		}
	}

	private async ValueTask SendPortfolioSnapshotAsync(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (IsSectionEnabled(WeexSections.Spot))
		{
			var account = await RestClient.GetSpotAccountAsync(cancellationToken);
			foreach (var balance in account?.Balances ?? [])
				await SendSpotBalanceAsync(balance, originalTransactionId, cancellationToken);
		}

		if (IsSectionEnabled(WeexSections.Futures))
		{
			foreach (var balance in await RestClient.GetFuturesBalancesAsync(cancellationToken) ?? [])
				await SendFuturesBalanceAsync(balance, originalTransactionId, cancellationToken);
			foreach (var position in await RestClient.GetPositionsAsync(null, cancellationToken) ?? [])
				await SendPositionAsync(position, originalTransactionId, cancellationToken);
		}
	}

	private async ValueTask SendOrderSnapshotAsync(long originalTransactionId, WeexSections? section,
		string symbol, (DateTime? From, DateTime? To)? range, int limit,
		CancellationToken cancellationToken)
	{
		var sections = section is null ? Sections.ToArray() : [section.Value];
		foreach (var current in sections)
		{
			var orders = new List<WeexOrder>();
			orders.AddRange(await RestClient.GetOpenOrdersAsync(current, symbol, cancellationToken) ?? []);
			orders.AddRange(await RestClient.GetOrderHistoryAsync(current, symbol,
				range?.From, range?.To, limit, cancellationToken) ?? []);

			foreach (var order in orders
				.Where(static item => item?.OrderId.IsEmpty() == false)
				.GroupBy(static item => item.OrderId, StringComparer.OrdinalIgnoreCase)
				.Select(static group => group.OrderByDescending(GetOrderTime).First())
				.Where(item => range?.From is null || GetOrderTime(item) >= range.Value.From)
				.Where(item => range?.To is null || GetOrderTime(item) <= range.Value.To)
				.OrderBy(GetOrderTime)
				.Take(limit))
			{
				await SendOrderAsync(order, current, ParseTransactionId(order.ClientOrderId),
					originalTransactionId, cancellationToken);
			}

			if (current == WeexSections.Futures)
			{
				var algoOrders = new List<WeexAlgoOrder>();
				algoOrders.AddRange(await RestClient.GetOpenAlgoOrdersAsync(symbol, cancellationToken) ?? []);
				algoOrders.AddRange(await RestClient.GetAlgoOrderHistoryAsync(symbol,
					range?.From, range?.To, limit, cancellationToken) ?? []);
				foreach (var order in algoOrders
					.Where(static item => item?.OrderId.IsEmpty() == false)
					.GroupBy(static item => item.OrderId, StringComparer.OrdinalIgnoreCase)
					.Select(static group => group.OrderByDescending(GetAlgoOrderTime).First())
					.OrderBy(GetAlgoOrderTime)
					.Take(limit))
				{
					await SendAlgoOrderAsync(order, ParseTransactionId(order.ClientOrderId),
						originalTransactionId, cancellationToken);
				}
			}

			foreach (var trade in (await RestClient.GetUserTradesAsync(current, symbol,
				range?.From, range?.To, limit, cancellationToken) ?? [])
				.Where(item => range?.From is null || item.Time.ToUtcTime() >= range.Value.From)
				.Where(item => range?.To is null || item.Time.ToUtcTime() <= range.Value.To)
				.OrderBy(static item => item.Time))
			{
				await SendUserTradeAsync(trade, current, originalTransactionId, cancellationToken);
			}
		}
	}

	private async ValueTask OnAccountAsync(WeexSections section, WeexWsAccountEntry[] balances,
		CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId == 0)
			return;
		foreach (var balance in balances ?? [])
		{
			if (balance?.Asset.IsEmpty() != false)
				continue;
			var current = balance.Equity.ToDecimal() ?? balance.Amount.ToDecimal() ?? balance.Available.ToDecimal();
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = balance.Asset.ToStockSharp(section),
				ServerTime = CurrentTime,
				OriginalTransactionId = _portfolioSubscriptionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, current, true)
			.TryAdd(PositionChangeTypes.BlockedValue, balance.Frozen.ToDecimal(), true), cancellationToken);
		}
	}

	private async ValueTask OnOrderAsync(WeexSections section, WeexWsOrder[] orders,
		CancellationToken cancellationToken)
	{
		if (_orderStatusSubscriptionId == 0)
			return;
		foreach (var order in orders ?? [])
			await SendWsOrderAsync(order, section, _orderStatusSubscriptionId, cancellationToken);
	}

	private async ValueTask OnFillAsync(WeexSections section, WeexWsFill[] fills,
		CancellationToken cancellationToken)
	{
		if (_orderStatusSubscriptionId == 0)
			return;
		foreach (var fill in fills ?? [])
		{
			if (fill?.Symbol.IsEmpty() != false)
				continue;
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				SecurityId = fill.Symbol.ToStockSharp(section),
				ServerTime = fill.CreatedTime > 0 ? fill.CreatedTime.ToUtcTime() : CurrentTime,
				PortfolioName = _portfolioName,
				Side = fill.Side.ToStockSharp(),
				OrderId = ParseLong(fill.OrderId),
				TradeStringId = fill.Id,
				TradeVolume = fill.Quantity.ToDecimal(),
				TradePrice = CalculateFillPrice(fill.Value, fill.Quantity),
				Commission = fill.Fee.ToDecimal(),
				CommissionCurrency = section == WeexSections.Spot
					? fill.BaseAsset
					: fill.Asset,
				PnL = fill.RealizedPnl.ToDecimal(),
				OriginalTransactionId = _orderStatusSubscriptionId,
			}, cancellationToken);
		}
	}

	private async ValueTask OnPositionAsync(WeexWsPosition[] positions,
		CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId == 0)
			return;
		foreach (var position in positions ?? [])
		{
			if (position?.Symbol.IsEmpty() != false)
				continue;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = position.Symbol.ToStockSharp(WeexSections.Futures),
				ServerTime = position.UpdatedTime > 0 ? position.UpdatedTime.ToUtcTime() : CurrentTime,
				OriginalTransactionId = _portfolioSubscriptionId,
				Side = position.Side.ToStockSharpNullable(),
			}
			.TryAdd(PositionChangeTypes.CurrentValue, position.Size.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.AveragePrice,
				CalculateFillPrice(position.OpenValue, position.Size), true), cancellationToken);
		}
	}

	private ValueTask SendOrderAsync(WeexOrder order, WeexSections section, long transactionId,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (order is null || order.Symbol.IsEmpty())
			return default;
		RememberOrderSection(order.OrderId, section);
		var volume = order.Quantity.ToDecimal();
		var executed = order.ExecutedQuantity.ToDecimal();
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToStockSharp(section),
			ServerTime = GetOrderTime(order),
			PortfolioName = _portfolioName,
			Side = order.Side.ToStockSharp(),
			OrderVolume = volume,
			Balance = volume is null ? null : (volume.Value - (executed ?? 0m)).Max(0m),
			OrderPrice = order.Price.ToDecimal() ?? 0m,
			AveragePrice = order.AveragePrice.ToDecimal(),
			OrderType = ToOrderType(order.Type),
			OrderState = ToOrderState(order.Status),
			OrderId = ParseLong(order.OrderId),
			OrderStringId = order.ClientOrderId,
			TransactionId = transactionId,
			OriginalTransactionId = originalTransactionId,
			TimeInForce = ToTimeInForce(order.TimeInForce),
			PositionEffect = order.IsReduceOnly ? OrderPositionEffects.CloseOnly : null,
			Condition = order.IsConditional ? CreateCondition(order.Type, order.TriggerPrice,
				order.Price, order.PositionSide) : null,
		}, cancellationToken);
	}

	private ValueTask SendAlgoOrderAsync(WeexAlgoOrder order, long transactionId,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (order is null || order.Symbol.IsEmpty())
			return default;
		RememberOrderSection(order.OrderId, WeexSections.Futures);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToStockSharp(WeexSections.Futures),
			ServerTime = GetAlgoOrderTime(order),
			PortfolioName = _portfolioName,
			Side = order.Side.ToStockSharp(),
			OrderVolume = order.Quantity.ToDecimal(),
			Balance = order.Quantity.ToDecimal(),
			OrderPrice = order.Price.ToDecimal() ?? 0m,
			AveragePrice = order.AveragePrice.ToDecimal(),
			OrderType = OrderTypes.Conditional,
			OrderState = ToOrderState(order.Status),
			OrderId = ParseLong(order.OrderId),
			OrderStringId = order.ClientOrderId,
			TransactionId = transactionId,
			OriginalTransactionId = originalTransactionId,
			TimeInForce = ToTimeInForce(order.TimeInForce),
			PositionEffect = order.IsReduceOnly ? OrderPositionEffects.CloseOnly : null,
			Condition = CreateCondition(order.Type, order.TriggerPrice, order.Price, order.PositionSide),
		}, cancellationToken);
	}

	private ValueTask SendWsOrderAsync(WeexWsOrder order, WeexSections section,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (order is null || order.Symbol.IsEmpty())
			return default;
		RememberOrderSection(order.OrderId, section);
		var volume = order.Quantity.ToDecimal();
		var executed = order.CumulativeFillSize.ToDecimal();
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToStockSharp(section),
			ServerTime = (order.UpdatedTime > 0 ? order.UpdatedTime : order.CreatedTime).ToUtcTime(),
			PortfolioName = _portfolioName,
			Side = order.Side.ToStockSharp(),
			OrderVolume = volume,
			Balance = volume is null ? null : (volume.Value - (executed ?? 0m)).Max(0m),
			OrderPrice = order.Price.ToDecimal() ?? 0m,
			AveragePrice = order.LatestFillPrice.ToDecimal(),
			OrderType = ToOrderType(order.Type),
			OrderState = ToOrderState(order.Status),
			OrderId = ParseLong(order.OrderId),
			OrderStringId = order.ClientOrderId,
			TransactionId = ParseTransactionId(order.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
			TimeInForce = ToTimeInForce(order.TimeInForce),
			PositionEffect = order.IsReduceOnly ? OrderPositionEffects.CloseOnly : null,
			Condition = !order.TriggerPrice.IsEmpty()
				? CreateCondition(order.Type, order.TriggerPrice, order.Price, order.PositionSide)
				: null,
		}, cancellationToken);
	}

	private ValueTask SendUserTradeAsync(WeexUserTrade trade, WeexSections section,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (trade is null || trade.Symbol.IsEmpty())
			return default;
		var side = trade.Side?.ToStockSharp() ?? (trade.IsBuyer == true ? Sides.Buy : Sides.Sell);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = trade.Symbol.ToStockSharp(section),
			ServerTime = trade.Time > 0 ? trade.Time.ToUtcTime() : CurrentTime,
			PortfolioName = _portfolioName,
			Side = side,
			OrderId = ParseLong(trade.OrderId),
			TradeStringId = trade.Id,
			TradePrice = trade.Price.ToDecimal(),
			TradeVolume = trade.Quantity.ToDecimal(),
			Commission = trade.Commission.ToDecimal(),
			CommissionCurrency = trade.CommissionAsset,
			PnL = trade.RealizedPnl.ToDecimal(),
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private ValueTask SendSpotBalanceAsync(WeexSpotBalance balance, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (balance?.Asset.IsEmpty() != false)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = balance.Asset.ToStockSharp(WeexSections.Spot),
			ServerTime = CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, balance.Available.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.BlockedValue, balance.Locked.ToDecimal(), true), cancellationToken);
	}

	private ValueTask SendFuturesBalanceAsync(WeexFuturesBalance balance, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (balance?.Asset.IsEmpty() != false)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = balance.Asset.ToStockSharp(WeexSections.Futures),
			ServerTime = CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, balance.Balance.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.BlockedValue, balance.Frozen.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, balance.UnrealizedPnl.ToDecimal(), true), cancellationToken);
	}

	private ValueTask SendPositionAsync(WeexPosition position, long originalTransactionId,
		CancellationToken cancellationToken, decimal? forcedValue = null)
	{
		if (position?.Symbol.IsEmpty() != false)
			return default;
		var size = forcedValue ?? position.Size.ToDecimal();
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = position.Symbol.ToStockSharp(WeexSections.Futures),
			ServerTime = position.UpdateTime > 0 ? position.UpdateTime.ToUtcTime() : CurrentTime,
			OriginalTransactionId = originalTransactionId,
			Side = position.Side.ToStockSharpNullable(),
		}
		.TryAdd(PositionChangeTypes.CurrentValue, size, true)
		.TryAdd(PositionChangeTypes.AveragePrice, CalculateFillPrice(position.OpenValue, position.Size), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnl.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.LiquidationPrice, position.LiquidationPrice.ToDecimal(), true), cancellationToken);
	}

	private void RememberOrderSection(string orderId, WeexSections section)
	{
		if (orderId.IsEmpty())
			return;
		using (_sync.EnterScope())
			_orderSections[orderId] = section;
	}

	private static void ValidateAction(WeexOrderActionResult result, string operation)
	{
		if (result is null)
			throw new InvalidDataException($"WEEX {operation} returned an empty response.");
		if (result.IsSuccess == false || !result.ErrorCode.IsEmpty())
			throw new InvalidOperationException($"WEEX {operation} failed: {result.ErrorCode}: {result.ErrorMessage}");
	}

	private static WeexPositionSides ResolvePositionSide(Sides side,
		OrderPositionEffects? effect, WeexPositionSides? requested)
		=> requested ?? (effect == OrderPositionEffects.CloseOnly
			? side == Sides.Buy ? WeexPositionSides.Short : WeexPositionSides.Long
			: side == Sides.Buy ? WeexPositionSides.Long : WeexPositionSides.Short);

	private static WeexTimeInForce ToTimeInForce(TimeInForce? timeInForce, bool? isPostOnly)
		=> isPostOnly == true ? WeexTimeInForce.PostOnly : timeInForce switch
		{
			TimeInForce.CancelBalance => WeexTimeInForce.Ioc,
			TimeInForce.MatchOrCancel => WeexTimeInForce.Fok,
			_ => WeexTimeInForce.Gtc,
		};

	private static TimeInForce? ToTimeInForce(string value)
		=> value?.ToUpperInvariant() switch
		{
			"IOC" => TimeInForce.CancelBalance,
			"FOK" => TimeInForce.MatchOrCancel,
			"GTC" or "POST_ONLY" => TimeInForce.PutInQueue,
			_ => null,
		};

	private static WeexWorkingTypes ToWorkingType(WeexTriggerPriceTypes type)
		=> type == WeexTriggerPriceTypes.MarkPrice ? WeexWorkingTypes.MarkPrice : WeexWorkingTypes.ContractPrice;

	private static OrderTypes ToOrderType(string value)
		=> value?.ToUpperInvariant() switch
		{
			"MARKET" => OrderTypes.Market,
			"STOP" or "STOP_MARKET" or "TAKE_PROFIT" or "TAKE_PROFIT_MARKET" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	private static OrderStates ToOrderState(string value)
		=> value?.ToUpperInvariant() switch
		{
			"NEW" or "OPEN" or "PENDING" or "UNTRIGGERED" or "PARTIAL_FILL" or
				"PARTIALLY_FILLED" or "CANCELING" => OrderStates.Active,
			"FILLED" or "FULL_FILL" or "CANCELED" or "CANCELLED" or "EXPIRED" or
				"TRIGGERED" => OrderStates.Done,
			"REJECTED" or "FAILED" => OrderStates.Failed,
			_ => OrderStates.None,
		};

	private static WeexOrderCondition CreateCondition(string type, string triggerPrice,
		string closePrice, WeexPositionSides? positionSide)
		=> new()
		{
			Type = type?.StartsWith("TAKE_PROFIT", StringComparison.OrdinalIgnoreCase) == true
				? WeexOrderConditionTypes.TakeProfit
				: WeexOrderConditionTypes.StopLoss,
			ActivationPrice = triggerPrice.ToDecimal(),
			ClosePositionPrice = type?.EndsWith("_MARKET", StringComparison.OrdinalIgnoreCase) == true
				? null
				: closePrice.ToDecimal(),
			PositionSide = positionSide,
		};

	private static DateTime GetOrderTime(WeexOrder order)
		=> (order.UpdateTime > 0 ? order.UpdateTime : order.Time) is var time && time > 0
			? time.ToUtcTime()
			: DateTime.UtcNow;

	private static DateTime GetAlgoOrderTime(WeexAlgoOrder order)
		=> (order.UpdateTime > 0 ? order.UpdateTime : order.Time) is var time && time > 0
			? time.ToUtcTime()
			: DateTime.UtcNow;

	private static long? ParseLong(string value)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;

	private static decimal? CalculateFillPrice(string value, string quantity)
		=> value.ToDecimal() is decimal total && quantity.ToDecimal() is decimal amount && amount != 0m
			? total / amount
			: null;
}
