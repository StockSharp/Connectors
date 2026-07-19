namespace StockSharp.HashKey;

public partial class HashKeyMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var section = ResolveSection(regMsg.SecurityId);
		var symbol = GetSymbol(regMsg.SecurityId, section);
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException("HashKey order volume must be positive.");
		if (section == HashKeySections.Futures && volume != decimal.Truncate(volume))
			throw new InvalidOperationException(
				"HashKey futures volume must be an integer number of contracts.");
		if (regMsg.VisibleVolume is > 0 && regMsg.VisibleVolume != volume)
			throw new NotSupportedException("HashKey does not document iceberg order entry.");
		if (regMsg.TillDate is not null)
			throw new NotSupportedException("HashKey does not document GTD order entry.");

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		var condition = regMsg.Condition as HashKeyOrderCondition ?? new();
		var isConditional = orderType == OrderTypes.Conditional ||
			condition.StopPrice is not null;
		if (condition.StopPrice is <= 0)
			throw new InvalidOperationException("HashKey stop price must be positive.");
		if (section == HashKeySections.Spot && isConditional)
			throw new NotSupportedException(
				"HashKey Global spot API does not document stop orders.");
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market or
			OrderTypes.Conditional))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		if (!isConditional && orderType == OrderTypes.Limit && regMsg.Price <= 0)
			throw new InvalidOperationException("HashKey limit orders require a positive price.");
		if (orderType == OrderTypes.Market && regMsg.PostOnly == true)
			throw new InvalidOperationException("A market order cannot be post-only.");

		var clientOrderId = HashKeyExtensions.CreateClientOrderId(regMsg.TransactionId,
			regMsg.UserOrderId);
		var isClose = regMsg.PositionEffect == OrderPositionEffects.CloseOnly;
		var tracked = new TrackedOrder
		{
			TransactionId = regMsg.TransactionId,
			Section = section,
			Symbol = symbol,
			ClientOrderId = clientOrderId,
			Side = regMsg.Side,
			OrderType = isConditional ? OrderTypes.Conditional : orderType,
			Volume = volume,
			Price = regMsg.Price,
			StopPrice = condition.StopPrice,
			TimeInForce = regMsg.TimeInForce,
			PositionEffect = regMsg.PositionEffect,
			IsPostOnly = regMsg.PostOnly == true,
		};

		if (section == HashKeySections.Spot)
		{
			var nativeType = orderType == OrderTypes.Market
				? HashKeyOrderTypes.Market
				: regMsg.PostOnly == true
					? HashKeyOrderTypes.LimitMaker
					: HashKeyOrderTypes.Limit;
			var timeInForce = orderType == OrderTypes.Market
				? HashKeyTimeInForces.ImmediateOrCancel
				: regMsg.TimeInForce.ToHashKey(regMsg.PostOnly == true);
			var result = await RestClient.CreateSpotOrderAsync(new()
			{
				Symbol = symbol,
				Side = regMsg.Side.ToHashKey(section, false),
				Type = nativeType,
				Quantity = volume,
				Price = orderType == OrderTypes.Limit ? regMsg.Price : null,
				ClientOrderId = clientOrderId,
				TimeInForce = timeInForce,
				SelfTradePreventionMode = condition.IsExpireMaker
					? HashKeySelfTradePreventionModes.ExpireMaker
					: HashKeySelfTradePreventionModes.ExpireTaker,
			}, cancellationToken);
			if (result?.OrderId.IsEmpty() != false)
				throw new InvalidDataException(
					"HashKey accepted a spot order without returning its identifier.");
			TrackOrder(result.OrderId, tracked);
			await SendSpotOrderAsync(result, regMsg.TransactionId, tracked, null,
				cancellationToken);
			return;
		}

		var futuresType = isConditional ? HashKeyOrderTypes.Stop : HashKeyOrderTypes.Limit;
		var priceType = orderType == OrderTypes.Market ||
			(isConditional && regMsg.Price <= 0)
			? HashKeyPriceTypes.Market
			: HashKeyPriceTypes.Input;
		var futuresResult = await RestClient.CreateFuturesOrderAsync(new()
		{
			Symbol = symbol,
			Side = regMsg.Side.ToHashKey(section, isClose),
			Type = futuresType,
			Quantity = volume.To<long>(),
			Price = priceType == HashKeyPriceTypes.Input ? regMsg.Price : null,
			PriceType = priceType,
			StopPrice = condition.StopPrice,
			TimeInForce = regMsg.TimeInForce.ToHashKey(regMsg.PostOnly == true),
			ClientOrderId = clientOrderId,
			SelfTradePreventionMode = condition.IsExpireMaker
				? HashKeySelfTradePreventionModes.ExpireMaker
				: HashKeySelfTradePreventionModes.ExpireTaker,
		}, cancellationToken);
		if (futuresResult?.OrderId.IsEmpty() != false)
			throw new InvalidDataException(
				"HashKey accepted a futures order without returning its identifier.");
		TrackOrder(futuresResult.OrderId, tracked);
		await SendFuturesOrderAsync(futuresResult, regMsg.TransactionId, tracked, null,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		_ = replaceMsg;
		_ = cancellationToken;
		throw new NotSupportedException(
			"HashKey Global does not expose native order replacement.");
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var orderId = ResolveOrderId(cancelMsg.OrderId, cancelMsg.OrderStringId);
		var tracked = GetTrackedOrder(orderId);
		var section = tracked?.Section ?? ResolveSection(cancelMsg.SecurityId);
		var symbol = tracked?.Symbol ?? GetSymbol(cancelMsg.SecurityId, section);
		if (section == HashKeySections.Spot)
		{
			var result = await RestClient.CancelSpotOrderAsync(new()
			{
				OrderId = orderId,
			}, cancellationToken);
			await SendSpotOrderAsync(result, cancelMsg.TransactionId, tracked,
				OrderStates.Done, cancellationToken);
			return;
		}

		var nativeType = tracked?.OrderType == OrderTypes.Conditional ||
			cancelMsg.OrderType == OrderTypes.Conditional
			? HashKeyOrderTypes.Stop
			: HashKeyOrderTypes.Limit;
		var futuresResult = await RestClient.CancelFuturesOrderAsync(new()
		{
			Symbol = symbol,
			OrderId = orderId,
			Type = nativeType,
		}, cancellationToken);
		await SendFuturesOrderAsync(futuresResult, cancelMsg.TransactionId, tracked,
			OrderStates.Done, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(
		OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"HashKey bulk cancellation does not close futures positions.");
		HashKeySections? requestedSection = cancelMsg.SecurityId.BoardCode.IsEmpty()
			? null
			: cancelMsg.SecurityId.BoardCode.ToSection();
		var hasSymbol = !cancelMsg.SecurityId.SecurityCode.IsEmpty();
		if (hasSymbol && requestedSection is null)
			requestedSection = ResolveSection(cancelMsg.SecurityId);
		foreach (var section in Sections.Where(section =>
			requestedSection is null || requestedSection == section))
		{
			var symbol = hasSymbol ? GetSymbol(cancelMsg.SecurityId, section) : null;
			_ = await RestClient.CancelAllAsync(section, new()
			{
				Symbol = symbol,
				Side = cancelMsg.Side is Sides side
					? side == Sides.Buy ? HashKeyOrderSides.Buy : HashKeyOrderSides.Sell
					: null,
				Limit = 200,
			}, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(
		PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsurePrivateReady();
		if (!lookupMsg.IsSubscribe)
		{
			_portfolioSubscriptionId = 0;
			return;
		}

		var requested = lookupMsg.PortfolioName;
		foreach (var section in Sections)
		{
			var portfolio = GetPortfolioName(section);
			if (!requested.IsEmpty() && !requested.EqualsIgnoreCase(portfolio))
				continue;
			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = portfolio,
				BoardCode = section.ToBoardCode(),
				OriginalTransactionId = lookupMsg.TransactionId,
			}, cancellationToken);
		}
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId, cancellationToken,
			requested);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId,
				cancellationToken);
			return;
		}
		_portfolioSubscriptionId = lookupMsg.TransactionId;
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
		if (statusMsg.Count is <= 0)
		{
			await CompleteOrderStatusAsync(statusMsg, cancellationToken);
			return;
		}

		HashKeySections? requestedSection = statusMsg.SecurityId.BoardCode.IsEmpty()
			? null
			: statusMsg.SecurityId.BoardCode.ToSection();
		var symbol = statusMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetSymbol(statusMsg.SecurityId,
				requestedSection ?? ResolveSection(statusMsg.SecurityId));
		var orderId = statusMsg.HasOrderId()
			? ResolveOrderId(statusMsg.OrderId, statusMsg.OrderStringId)
			: null;
		if (!orderId.IsEmpty() && requestedSection is null)
		{
			requestedSection = GetTrackedOrder(orderId)?.Section ??
				(symbol.IsEmpty() ? null : ResolveSection(statusMsg.SecurityId));
			if (requestedSection is null && Sections.Distinct().Count() > 1)
				throw new InvalidOperationException(
					"SecurityId.BoardCode is required when querying one HashKey order " +
					"that was not registered through this adapter.");
		}
		var maximum = (statusMsg.Count ?? 1000).Min(1000).Max(1).To<int>();
		await SendOrderSnapshotAsync(statusMsg.TransactionId, symbol,
			statusMsg.From, statusMsg.To, maximum, cancellationToken,
			requestedSection, orderId, statusMsg);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId,
				cancellationToken);
			return;
		}
		_orderStatusSubscriptionId = statusMsg.TransactionId;
	}

	private async ValueTask SendPortfolioSnapshotAsync(long originalTransactionId,
		CancellationToken cancellationToken, string requestedPortfolio = null)
	{
		if (IsSectionEnabled(HashKeySections.Spot) &&
			(requestedPortfolio.IsEmpty() || requestedPortfolio.EqualsIgnoreCase(
				GetPortfolioName(HashKeySections.Spot))))
		{
			var account = await RestClient.GetSpotAccountAsync(cancellationToken);
			foreach (var balance in account?.Balances ?? [])
				await SendSpotBalanceAsync(balance, originalTransactionId,
					cancellationToken);
		}

		if (!IsSectionEnabled(HashKeySections.Futures) ||
			(!requestedPortfolio.IsEmpty() && !requestedPortfolio.EqualsIgnoreCase(
				GetPortfolioName(HashKeySections.Futures))))
			return;
		foreach (var balance in await RestClient.GetFuturesBalancesAsync(
			cancellationToken) ?? [])
			await SendFuturesBalanceAsync(balance, originalTransactionId,
				cancellationToken);
		foreach (var position in await RestClient.GetFuturesPositionsAsync(null,
			cancellationToken) ?? [])
			await SendFuturesPositionAsync(position, originalTransactionId,
				cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(long originalTransactionId,
		string symbol, DateTime? from, DateTime? to, int maximum,
		CancellationToken cancellationToken, HashKeySections? requestedSection = null,
		string orderId = null, OrderStatusMessage filter = null)
	{
		foreach (var section in Sections.Where(section =>
			requestedSection is null || requestedSection == section))
		{
			if (!orderId.IsEmpty())
			{
				var tracked = GetTrackedOrder(orderId);
				if (section == HashKeySections.Spot)
				{
					var order = await RestClient.GetSpotOrderAsync(new()
					{
						OrderId = orderId,
					}, cancellationToken);
					if (MatchesOrder(order, filter, from, to))
						await SendSpotOrderAsync(order, originalTransactionId, tracked,
							null, cancellationToken);
				}
				else
				{
					var order = await RestClient.GetFuturesOrderAsync(new()
					{
						OrderId = orderId,
						Type = tracked?.OrderType == OrderTypes.Conditional
							? HashKeyOrderTypes.Stop
							: HashKeyOrderTypes.Limit,
					}, cancellationToken);
					if (MatchesOrder(order, filter, from, to))
						await SendFuturesOrderAsync(order, originalTransactionId, tracked,
							null, cancellationToken);
				}
			}
			else if (section == HashKeySections.Spot)
			{
				var orders = new List<HashKeySpotOrder>();
				orders.AddRange(await RestClient.GetSpotOpenOrdersAsync(new()
				{
					Symbol = symbol,
					Limit = maximum,
				}, cancellationToken) ?? []);
				orders.AddRange(await RestClient.GetSpotHistoryOrdersAsync(new()
				{
					Symbol = symbol,
					StartTime = from?.ToUniversalTime().ToMilliseconds(),
					EndTime = to?.ToUniversalTime().ToMilliseconds(),
					Limit = maximum,
				}, cancellationToken) ?? []);
				foreach (var order in orders.Where(order =>
					MatchesOrder(order, filter, from, to))
					.GroupBy(static order => order.OrderId)
					.Select(static group => group.OrderByDescending(order =>
						order.UpdateTime ?? order.TransactionTime ?? order.Time ?? 0).First())
					.OrderBy(static order => order.UpdateTime ?? order.Time ?? 0)
					.TakeLast(maximum))
					await SendSpotOrderAsync(order, originalTransactionId,
						GetTrackedOrder(order.OrderId), null, cancellationToken);
			}
			else
			{
				var orders = new List<HashKeyFuturesOrder>();
				foreach (var marketSymbol in GetQuerySymbols(HashKeySections.Futures,
					symbol))
					foreach (var type in new[]
					{
						HashKeyOrderTypes.Limit,
						HashKeyOrderTypes.Stop,
					})
					{
						orders.AddRange(await RestClient.GetFuturesOpenOrdersAsync(new()
						{
							Symbol = marketSymbol,
							Type = type,
							Limit = maximum.Min(500),
						}, cancellationToken) ?? []);
						orders.AddRange(await RestClient.GetFuturesHistoryOrdersAsync(new()
						{
							Symbol = marketSymbol,
							Type = type,
							StartTime = from?.ToUniversalTime().ToMilliseconds(),
							EndTime = to?.ToUniversalTime().ToMilliseconds(),
							Limit = maximum.Min(500),
						}, cancellationToken) ?? []);
					}
				foreach (var order in orders.Where(order =>
					MatchesOrder(order, filter, from, to))
					.GroupBy(static order => order.OrderId)
					.Select(static group => group.OrderByDescending(order =>
						order.UpdateTime).First())
					.OrderBy(static order => order.UpdateTime)
					.TakeLast(maximum))
					await SendFuturesOrderAsync(order, originalTransactionId,
						GetTrackedOrder(order.OrderId), null, cancellationToken);
			}

			await SendTradeSnapshotAsync(section, symbol, orderId, from, to, maximum,
				originalTransactionId, filter, cancellationToken);
		}
	}

	private async ValueTask SendTradeSnapshotAsync(HashKeySections section,
		string symbol, string orderId, DateTime? from, DateTime? to, int maximum,
		long originalTransactionId, OrderStatusMessage filter,
		CancellationToken cancellationToken)
	{
		var minimumTime = DateTime.UtcNow.AddDays(-30);
		var effectiveFrom = from?.ToUniversalTime().Max(minimumTime);
		if (section == HashKeySections.Spot)
		{
			var trades = await RestClient.GetSpotAccountTradesAsync(new()
			{
				Symbol = symbol,
				StartTime = effectiveFrom?.ToMilliseconds(),
				EndTime = to?.ToUniversalTime().ToMilliseconds(),
				Limit = maximum,
			}, cancellationToken);
			foreach (var trade in (trades ?? []).Where(trade =>
				(orderId.IsEmpty() || trade.OrderId.EqualsIgnoreCase(orderId)) &&
				MatchesTrade(trade, filter, from, to))
				.OrderBy(static trade => trade.Time).TakeLast(maximum))
				await SendSpotTradeAsync(trade, originalTransactionId, false,
					cancellationToken);
			return;
		}

		var futuresTrades = new List<HashKeyFuturesTrade>();
		foreach (var marketSymbol in GetQuerySymbols(HashKeySections.Futures, symbol))
			futuresTrades.AddRange(await RestClient.GetFuturesTradesAsync(new()
			{
				Symbol = marketSymbol,
				StartTime = effectiveFrom?.ToMilliseconds(),
				EndTime = to?.ToUniversalTime().ToMilliseconds(),
				Limit = maximum,
			}, cancellationToken) ?? []);
		foreach (var trade in futuresTrades.Where(trade =>
			(orderId.IsEmpty() || trade.OrderId.EqualsIgnoreCase(orderId)) &&
			MatchesTrade(trade, filter, from, to))
			.OrderBy(static trade => trade.Time).TakeLast(maximum))
			await SendFuturesTradeAsync(trade, originalTransactionId, false,
				cancellationToken);
	}

	private string[] GetQuerySymbols(HashKeySections section, string symbol)
	{
		if (!symbol.IsEmpty())
			return [symbol];
		using (_sync.EnterScope())
			return [.. (section == HashKeySections.Spot
				? _spotMarkets.Keys
				: _futuresMarkets.Keys)];
	}

	private ValueTask SendSpotBalanceAsync(HashKeyBalance balance,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (balance?.Asset.IsEmpty() != false)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(HashKeySections.Spot),
			SecurityId = balance.Asset.ToStockSharp(HashKeySections.Spot),
			ServerTime = CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, balance.Total, true)
		.TryAdd(PositionChangeTypes.BlockedValue, balance.Locked, true),
			cancellationToken);
	}

	private ValueTask SendFuturesBalanceAsync(HashKeyFuturesBalance balance,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (balance?.Asset.IsEmpty() != false)
			return default;
		var blocked = (balance.PositionMargin + balance.OrderMargin).Max(0m);
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(HashKeySections.Futures),
			SecurityId = balance.Asset.ToStockSharp(HashKeySections.Futures),
			ServerTime = CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, balance.Balance, true)
		.TryAdd(PositionChangeTypes.BlockedValue, blocked, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, balance.UnrealizedPnL, true),
			cancellationToken);
	}

	private ValueTask SendFuturesPositionAsync(HashKeyFuturesPosition position,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (position?.Symbol.IsEmpty() != false)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(HashKeySections.Futures),
			SecurityId = NormalizeSymbol(position.Symbol)
				.ToStockSharp(HashKeySections.Futures),
			DepoName = position.Side.ToWire(),
			ServerTime = position.UpdateTime.FromHashKeyMilliseconds(CurrentTime),
			OriginalTransactionId = originalTransactionId,
			Side = position.Position == 0
				? null
				: position.Side == HashKeyPositionSides.Long ? Sides.Buy : Sides.Sell,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, position.Position.Abs(), true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, position.LastPrice, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnL, true)
		.TryAdd(PositionChangeTypes.LiquidationPrice, position.LiquidationPrice, true)
		.TryAdd(PositionChangeTypes.Leverage, position.Leverage, true),
			cancellationToken);
	}

	private ValueTask SendSpotOrderAsync(HashKeySpotOrder order,
		long originalTransactionId, TrackedOrder tracked, OrderStates? forcedState,
		CancellationToken cancellationToken)
	{
		if (order?.OrderId.IsEmpty() != false)
			return default;
		tracked ??= GetTrackedOrder(order.OrderId);
		var symbol = order.Symbol.IsEmpty(tracked?.Symbol);
		if (symbol.IsEmpty())
			throw new InvalidDataException(
				$"HashKey spot order '{order.OrderId}' has no symbol.");
		symbol = NormalizeSymbol(symbol);
		var state = forcedState ?? order.Status.ToStockSharp();
		var volume = order.OriginalQuantity > 0
			? order.OriginalQuantity
			: tracked?.Volume ?? 0m;
		var executed = order.ExecutedQuantity;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(HashKeySections.Spot),
			ServerTime = (order.UpdateTime ?? order.TransactionTime ?? order.Time ?? 0)
				.FromHashKeyMilliseconds(CurrentTime),
			PortfolioName = GetPortfolioName(HashKeySections.Spot),
			Side = tracked?.Side ?? order.Side.ToStockSharp(),
			OrderVolume = volume,
			Balance = state == OrderStates.Done ? 0m : (volume - executed).Max(0m),
			OrderPrice = order.Price != 0 ? order.Price : tracked?.Price ?? 0m,
			AveragePrice = order.AveragePrice == 0 ? null : order.AveragePrice,
			OrderType = tracked?.OrderType ?? order.Type.ToStockSharp(),
			OrderState = state,
			OrderStringId = order.OrderId,
			TransactionId = tracked?.TransactionId ??
				HashKeyExtensions.ParseTransactionId(order.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
			TimeInForce = tracked?.TimeInForce ?? order.TimeInForce.ToStockSharp(),
			PostOnly = tracked?.IsPostOnly ?? order.Type == HashKeyOrderTypes.LimitMaker,
			Commission = order.TotalFee != 0 ? order.TotalFee :
				order.Fee == 0 ? null : order.Fee,
			CommissionCurrency = order.FeeAsset,
			Condition = tracked?.StopPrice is decimal stopPrice
				? new HashKeyOrderCondition { StopPrice = stopPrice }
				: null,
			Error = state == OrderStates.Failed
				? new InvalidOperationException(order.CancelReason.IsEmpty(
					$"HashKey rejected spot order '{order.OrderId}'."))
				: null,
		}, cancellationToken);
	}

	private ValueTask SendFuturesOrderAsync(HashKeyFuturesOrder order,
		long originalTransactionId, TrackedOrder tracked, OrderStates? forcedState,
		CancellationToken cancellationToken)
	{
		if (order?.OrderId.IsEmpty() != false)
			return default;
		tracked ??= GetTrackedOrder(order.OrderId);
		var symbol = order.Symbol.IsEmpty(tracked?.Symbol);
		if (symbol.IsEmpty())
			throw new InvalidDataException(
				$"HashKey futures order '{order.OrderId}' has no symbol.");
		symbol = NormalizeSymbol(symbol);
		var state = forcedState ?? order.Status.ToStockSharp();
		var volume = order.OriginalQuantity > 0
			? order.OriginalQuantity
			: tracked?.Volume ?? 0m;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(HashKeySections.Futures),
			ServerTime = (order.UpdateTime > 0 ? order.UpdateTime : order.Time)
				.FromHashKeyMilliseconds(CurrentTime),
			PortfolioName = GetPortfolioName(HashKeySections.Futures),
			Side = tracked?.Side ?? order.Side.ToStockSharp(),
			PositionEffect = tracked?.PositionEffect ?? order.Side.ToPositionEffect(),
			OrderVolume = volume,
			Balance = state == OrderStates.Done
				? 0m
				: (volume - order.ExecutedQuantity).Max(0m),
			OrderPrice = order.Price != 0 ? order.Price : tracked?.Price ?? 0m,
			AveragePrice = order.AveragePrice == 0 ? null : order.AveragePrice,
			OrderType = tracked?.OrderType ?? order.Type.ToStockSharp(),
			OrderState = state,
			OrderStringId = order.OrderId,
			TransactionId = tracked?.TransactionId ??
				HashKeyExtensions.ParseTransactionId(order.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
			TimeInForce = tracked?.TimeInForce ?? order.TimeInForce.ToStockSharp(),
			PostOnly = tracked?.IsPostOnly ?? order.TimeInForce ==
				HashKeyTimeInForces.LimitMaker,
			Condition = order.StopPrice > 0 || tracked?.StopPrice is not null
				? new HashKeyOrderCondition
				{
					StopPrice = order.StopPrice > 0 ? order.StopPrice : tracked?.StopPrice,
				}
				: null,
			Error = state == OrderStates.Failed
				? new InvalidOperationException(order.CancelReason.IsEmpty(
					$"HashKey rejected futures order '{order.OrderId}'."))
				: null,
		}, cancellationToken);
	}

	private ValueTask SendSpotTradeAsync(HashKeySpotAccountTrade trade,
		long originalTransactionId, bool onlyNew,
		CancellationToken cancellationToken)
	{
		if (trade?.Id.IsEmpty() != false || trade.Symbol.IsEmpty())
			return default;
		var added = AddTradeId(trade.Id);
		if (onlyNew && !added)
			return default;
		var tracked = GetTrackedOrder(trade.OrderId);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = NormalizeSymbol(trade.Symbol).ToStockSharp(HashKeySections.Spot),
			ServerTime = trade.Time.FromHashKeyMilliseconds(CurrentTime),
			PortfolioName = GetPortfolioName(HashKeySections.Spot),
			Side = trade.IsBuyer ? Sides.Buy : Sides.Sell,
			OrderStringId = trade.OrderId,
			TradeStringId = trade.Id,
			TradePrice = trade.Price,
			TradeVolume = trade.Quantity,
			Commission = trade.Fee?.Amount != 0
				? trade.Fee.Amount
				: trade.Commission == 0 ? null : trade.Commission,
			CommissionCurrency = trade.Fee?.Asset.IsEmpty(trade.CommissionAsset),
			IsMarketMaker = trade.IsMaker,
			TransactionId = tracked?.TransactionId ??
				HashKeyExtensions.ParseTransactionId(trade.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private ValueTask SendFuturesTradeAsync(HashKeyFuturesTrade trade,
		long originalTransactionId, bool onlyNew,
		CancellationToken cancellationToken)
	{
		if (trade?.TradeId.IsEmpty() != false || trade.Symbol.IsEmpty())
			return default;
		var added = AddTradeId(trade.TradeId);
		if (onlyNew && !added)
			return default;
		var tracked = GetTrackedOrder(trade.OrderId);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = NormalizeSymbol(trade.Symbol)
				.ToStockSharp(HashKeySections.Futures),
			ServerTime = trade.Time.FromHashKeyMilliseconds(CurrentTime),
			PortfolioName = GetPortfolioName(HashKeySections.Futures),
			Side = trade.Side.ToStockSharp(),
			PositionEffect = trade.Side.ToPositionEffect(),
			OrderStringId = trade.OrderId,
			TradeStringId = trade.TradeId,
			TradePrice = trade.Price,
			TradeVolume = trade.Quantity,
			Commission = trade.Commission == 0 ? null : trade.Commission,
			CommissionCurrency = trade.CommissionAsset,
			IsMarketMaker = trade.IsMaker,
			PnL = trade.RealizedPnL,
			TransactionId = tracked?.TransactionId ?? 0,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private async ValueTask OnAccountUpdateAsync(HashKeyWsAccountUpdate update,
		CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId == 0 || update is null)
			return;
		var section = update.Event switch
		{
			HashKeyPrivateEventTypes.SpotAccount => HashKeySections.Spot,
			HashKeyPrivateEventTypes.FuturesAccount => HashKeySections.Futures,
			_ => (HashKeySections?)null,
		};
		if (section is not HashKeySections actualSection ||
			!IsSectionEnabled(actualSection))
			return;
		foreach (var balance in update.Balances ?? [])
		{
			if (balance?.Asset.IsEmpty() != false)
				continue;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = GetPortfolioName(actualSection),
				SecurityId = balance.Asset.ToStockSharp(actualSection),
				ServerTime = update.EventTime.FromHashKeyMilliseconds(CurrentTime),
				OriginalTransactionId = _portfolioSubscriptionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, balance.Free + balance.Locked, true)
			.TryAdd(PositionChangeTypes.BlockedValue, balance.Locked, true),
				cancellationToken);
		}
	}

	private ValueTask OnOrderUpdateAsync(HashKeyWsOrderUpdate order,
		CancellationToken cancellationToken)
	{
		if (_orderStatusSubscriptionId == 0 || order?.OrderId.IsEmpty() != false ||
			order.Symbol.IsEmpty())
			return default;
		var section = order.Event == HashKeyPrivateEventTypes.FuturesOrder
			? HashKeySections.Futures
			: HashKeySections.Spot;
		if (!IsSectionEnabled(section))
			return default;
		var tracked = GetTrackedOrder(order.OrderId);
		var state = order.Status.ToStockSharp();
		var volume = order.Quantity > 0 ? order.Quantity : tracked?.Volume ?? 0m;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = NormalizeSymbol(order.Symbol).ToStockSharp(section),
			ServerTime = order.EventTime.FromHashKeyMilliseconds(CurrentTime),
			PortfolioName = GetPortfolioName(section),
			Side = tracked?.Side ?? order.Side.ToStockSharp(),
			PositionEffect = section == HashKeySections.Futures
				? tracked?.PositionEffect ?? order.Side.ToPositionEffect()
				: null,
			OrderVolume = volume,
			Balance = state == OrderStates.Done
				? 0m
				: (volume - order.ExecutedQuantity).Max(0m),
			OrderPrice = order.Price != 0 ? order.Price : tracked?.Price ?? 0m,
			AveragePrice = order.AveragePrice == 0 ? null : order.AveragePrice,
			OrderType = tracked?.OrderType ?? order.Type.ToStockSharp(),
			OrderState = state,
			OrderStringId = order.OrderId,
			TransactionId = tracked?.TransactionId ??
				HashKeyExtensions.ParseTransactionId(order.ClientOrderId),
			OriginalTransactionId = _orderStatusSubscriptionId,
			TimeInForce = tracked?.TimeInForce ?? order.TimeInForce.ToStockSharp(),
			PostOnly = tracked?.IsPostOnly ?? order.Type == HashKeyOrderTypes.LimitMaker,
			Error = state == OrderStates.Failed
				? new InvalidOperationException(order.RejectReason.IsEmpty(
					$"HashKey rejected order '{order.OrderId}'."))
				: null,
		}, cancellationToken);
	}

	private ValueTask OnTicketAsync(HashKeyWsTicket ticket,
		CancellationToken cancellationToken)
	{
		if (_orderStatusSubscriptionId == 0 || ticket?.TicketId.IsEmpty() != false ||
			ticket.Symbol.IsEmpty() || !AddTradeId(ticket.TicketId))
			return default;
		var symbol = NormalizeSymbol(ticket.Symbol);
		HashKeySections section;
		using (_sync.EnterScope())
			section = _futuresMarkets.ContainsKey(symbol)
				? HashKeySections.Futures
				: HashKeySections.Spot;
		if (!IsSectionEnabled(section))
			return default;
		var tracked = GetTrackedOrder(ticket.OrderId);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = ticket.MatchTime.FromHashKeyMilliseconds(CurrentTime),
			PortfolioName = GetPortfolioName(section),
			Side = tracked?.Side ?? ticket.Side.ToStockSharp(),
			PositionEffect = section == HashKeySections.Futures
				? tracked?.PositionEffect
				: null,
			OrderStringId = ticket.OrderId,
			TradeStringId = ticket.TicketId,
			TradePrice = ticket.Price,
			TradeVolume = ticket.Quantity,
			IsMarketMaker = ticket.IsMaker,
			TransactionId = tracked?.TransactionId ??
				HashKeyExtensions.ParseTransactionId(ticket.ClientOrderId),
			OriginalTransactionId = _orderStatusSubscriptionId,
		}, cancellationToken);
	}

	private ValueTask OnPositionUpdateAsync(HashKeyWsPosition position,
		CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId == 0 || position?.Symbol.IsEmpty() != false ||
			!IsSectionEnabled(HashKeySections.Futures))
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(HashKeySections.Futures),
			SecurityId = NormalizeSymbol(position.Symbol)
				.ToStockSharp(HashKeySections.Futures),
			DepoName = position.Side.ToWire(),
			ServerTime = (position.UpdateTime > 0
				? position.UpdateTime
				: position.EventTime).FromHashKeyMilliseconds(CurrentTime),
			OriginalTransactionId = _portfolioSubscriptionId,
			Side = position.Position == 0
				? null
				: position.Side == HashKeyPositionSides.Long ? Sides.Buy : Sides.Sell,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, position.Position.Abs(), true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnL, true)
		.TryAdd(PositionChangeTypes.LiquidationPrice, position.LiquidationPrice, true)
		.TryAdd(PositionChangeTypes.Leverage, position.Leverage, true),
			cancellationToken);
	}

	private bool MatchesOrder(HashKeySpotOrder order, OrderStatusMessage filter,
		DateTime? from, DateTime? to)
	{
		if (order is null || order.OrderId.IsEmpty() || order.Symbol.IsEmpty())
			return false;
		var time = (order.UpdateTime ?? order.TransactionTime ?? order.Time ?? 0)
			.FromHashKeyMilliseconds(CurrentTime);
		return MatchesFilter(HashKeySections.Spot, order.Symbol, order.Side.ToStockSharp(),
			order.Status.ToStockSharp(), order.OriginalQuantity, time, filter, from, to);
	}

	private bool MatchesOrder(HashKeyFuturesOrder order, OrderStatusMessage filter,
		DateTime? from, DateTime? to)
	{
		if (order is null || order.OrderId.IsEmpty() || order.Symbol.IsEmpty())
			return false;
		var time = (order.UpdateTime > 0 ? order.UpdateTime : order.Time)
			.FromHashKeyMilliseconds(CurrentTime);
		return MatchesFilter(HashKeySections.Futures, order.Symbol,
			order.Side.ToStockSharp(), order.Status.ToStockSharp(),
			order.OriginalQuantity, time, filter, from, to);
	}

	private bool MatchesTrade(HashKeySpotAccountTrade trade, OrderStatusMessage filter,
		DateTime? from, DateTime? to)
		=> trade is not null && !trade.Symbol.IsEmpty() &&
			MatchesFilter(HashKeySections.Spot, trade.Symbol,
				trade.IsBuyer ? Sides.Buy : Sides.Sell, null, null,
				trade.Time.FromHashKeyMilliseconds(CurrentTime), filter, from, to);

	private bool MatchesTrade(HashKeyFuturesTrade trade, OrderStatusMessage filter,
		DateTime? from, DateTime? to)
		=> trade is not null && !trade.Symbol.IsEmpty() &&
			MatchesFilter(HashKeySections.Futures, trade.Symbol,
				trade.Side.ToStockSharp(), null, null,
				trade.Time.FromHashKeyMilliseconds(CurrentTime), filter, from, to);

	private bool MatchesFilter(HashKeySections section, string symbol, Sides side,
		OrderStates? state, decimal? volume, DateTime time, OrderStatusMessage filter,
		DateTime? from, DateTime? to)
	{
		if (from is DateTime fromTime && time < fromTime.ToUniversalTime() ||
			to is DateTime toTime && time > toTime.ToUniversalTime())
			return false;
		if (filter is null)
			return true;
		if (filter.Side is Sides requestedSide && requestedSide != side)
			return false;
		if (state is OrderStates actualState && filter.States.Length > 0 &&
			!filter.States.Contains(actualState))
			return false;
		if (filter.Volume is decimal requestedVolume && volume is decimal actualVolume &&
			requestedVolume != actualVolume)
			return false;
		if (!filter.PortfolioName.IsEmpty() && !filter.PortfolioName.EqualsIgnoreCase(
			GetPortfolioName(section)))
			return false;
		var requested = new List<SecurityId>();
		if (!filter.SecurityId.SecurityCode.IsEmpty())
			requested.Add(filter.SecurityId);
		requested.AddRange(filter.SecurityIds.Where(static id =>
			!id.SecurityCode.IsEmpty()));
		return requested.Count == 0 || requested.Any(id =>
			(id.BoardCode.IsEmpty() || id.BoardCode.EqualsIgnoreCase(section.ToBoardCode())) &&
			NormalizeSymbol(id.SecurityCode).EqualsIgnoreCase(NormalizeSymbol(symbol)));
	}

	private async ValueTask CompleteOrderStatusAsync(OrderStatusMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
	}
}
