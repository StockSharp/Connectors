namespace StockSharp.CoinW;

public partial class CoinWMessageAdapter
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
		var condition = regMsg.Condition as CoinWOrderCondition;
		string orderId;
		DateTime serverTime;

		if (section == CoinWSections.Spot)
		{
			if (regMsg.OrderType == OrderTypes.Conditional || regMsg.Condition is not null)
				throw new NotSupportedException("CoinW spot API does not expose conditional orders.");
			var isMarket = regMsg.OrderType == OrderTypes.Market;
			if (!isMarket && regMsg.OrderType is not null and not OrderTypes.Limit)
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, 0));
			if (!isMarket && regMsg.Price <= 0)
				throw new InvalidOperationException("Limit order price must be positive.");
			if (regMsg.PostOnly == true || regMsg.TimeInForce is TimeInForce.CancelBalance or TimeInForce.MatchOrCancel)
				throw new NotSupportedException("CoinW spot order placement does not expose time-in-force controls.");

			var result = await RestClient.PlaceSpotOrderAsync(new()
			{
				Symbol = symbol,
				Side = regMsg.Side == Sides.Buy ? "0" : "1",
				Amount = volume.ToWire(),
				Price = isMarket ? null : regMsg.Price.ToWire(),
				IsMarket = isMarket,
				ClientOrderId = clientOrderId,
			}, cancellationToken);
			orderId = result?.OrderId.ThrowIfEmpty(nameof(result.OrderId));
			serverTime = CurrentTime;
		}
		else if (regMsg.PositionEffect == OrderPositionEffects.CloseOnly)
		{
			if (condition?.PositionId.IsEmpty() != false)
				throw new InvalidOperationException("CoinW futures closing requires PositionId in CoinWOrderCondition.");
			if (condition.CloseRate is decimal closeRate && (closeRate <= 0 || closeRate > 1))
				throw new InvalidOperationException("CoinW close rate must be greater than 0 and not greater than 1.");
			var isMarket = regMsg.OrderType == OrderTypes.Market;
			if (!isMarket && regMsg.OrderType is not null and not OrderTypes.Limit)
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, 0));
			if (!isMarket && regMsg.Price <= 0)
				throw new InvalidOperationException("Limit close price must be positive.");

			var result = await RestClient.CloseFuturesPositionAsync(new()
			{
				PositionId = condition.PositionId,
				PositionType = isMarket ? "execute" : "plan",
				Contracts = condition.CloseRate is null
					? (volume / GetContractSize(symbol)).ToWire()
					: null,
				CloseRate = condition.CloseRate?.ToWire(),
				Price = isMarket ? null : regMsg.Price.ToWire(),
			}, cancellationToken);
			orderId = result?.Value.ThrowIfEmpty(nameof(result.Value));
			serverTime = result.Timestamp > 0 ? result.Timestamp.ToUtcTime() : CurrentTime;
		}
		else
		{
			condition ??= new CoinWOrderCondition();
			if (condition.Leverage <= 0)
				throw new InvalidOperationException("CoinW leverage must be positive.");
			var isConditional = regMsg.OrderType == OrderTypes.Conditional || condition.TriggerPrice is not null;
			var isMarket = regMsg.OrderType == OrderTypes.Market ||
				(isConditional && regMsg.Price <= 0);
			if (!isConditional && !isMarket && regMsg.OrderType is not null and not OrderTypes.Limit)
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, 0));
			if (isConditional && (condition.TriggerPrice is not decimal triggerPrice || triggerPrice <= 0))
				throw new InvalidOperationException("Conditional CoinW futures order requires a positive trigger price.");
			if (!isMarket && regMsg.Price <= 0)
				throw new InvalidOperationException("Limit order price must be positive.");
			if (regMsg.TimeInForce is TimeInForce.CancelBalance or TimeInForce.MatchOrCancel)
				throw new NotSupportedException("CoinW futures API does not document IOC or FOK placement.");

			var positionType = isConditional ? "planTrigger" :
				regMsg.PostOnly == true ? "PostOnly" :
				isMarket ? "execute" : "plan";
			var result = await RestClient.PlaceFuturesOrderAsync(new()
			{
				Instrument = GetFuturesNativeSymbol(symbol),
				Direction = regMsg.Side == Sides.Buy ? "long" : "short",
				Leverage = condition.Leverage,
				QuantityUnit = (int)condition.QuantityUnit,
				Quantity = volume.ToWire(),
				PositionModel = (int)condition.MarginMode,
				PositionType = positionType,
				Price = isMarket ? null : regMsg.Price.ToWire(),
				StopLossPrice = condition.StopLossPrice?.ToWire(),
				TakeProfitPrice = condition.TakeProfitPrice?.ToWire(),
				TriggerPrice = condition.TriggerPrice?.ToWire(),
				TriggerType = isConditional ? isMarket ? 1 : 0 : null,
				ClientOrderId = clientOrderId,
			}, cancellationToken);
			orderId = result?.Value.ThrowIfEmpty(nameof(result.Value));
			serverTime = result.Timestamp > 0 ? result.Timestamp.ToUtcTime() : CurrentTime;
		}

		RememberOrderSide(orderId, regMsg.Side);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = serverTime,
			PortfolioName = _portfolioName,
			Side = regMsg.Side,
			OrderVolume = volume,
			Balance = volume,
			OrderPrice = regMsg.OrderType == OrderTypes.Market ? 0m : regMsg.Price,
			OrderType = regMsg.OrderType ?? OrderTypes.Limit,
			OrderState = OrderStates.Active,
			OrderId = ParseLong(orderId),
			OrderStringId = clientOrderId,
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
		=> throw new NotSupportedException("CoinW does not expose in-place order modification.");

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		var section = ResolveSection(cancelMsg.SecurityId);
		EnsurePrivateReady(section);
		var orderId = cancelMsg.OrderId?.ToString(CultureInfo.InvariantCulture)
			.IsEmpty(cancelMsg.OrderStringId);
		if (orderId.IsEmpty())
			throw new InvalidOperationException("CoinW cancellation requires an exchange order ID.");
		var symbol = cancelMsg.SecurityId.SecurityCode.ThrowIfEmpty(nameof(cancelMsg.SecurityId.SecurityCode)).ToUpperInvariant();
		if (section == CoinWSections.Spot)
			_ = await RestClient.CancelSpotOrderAsync(new() { OrderId = orderId }, cancellationToken);
		else
			await RestClient.CancelFuturesOrderAsync(new() { OrderId = orderId }, cancellationToken);

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
		if (symbol.IsEmpty())
			throw new InvalidOperationException("CoinW bulk cancellation requires a security.");
		var section = ResolveSection(cancelMsg.SecurityId);

		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
		{
			if (section != CoinWSections.Futures)
				throw new NotSupportedException("CoinW position closing is available only for futures.");
			await RestClient.CloseAllFuturesPositionsAsync(GetFuturesNativeSymbol(symbol), cancellationToken);
			foreach (var position in await RestClient.GetFuturesPositionsAsync(cancellationToken))
			{
				if (ResolveFuturesSymbol(position.NativeSymbol).EqualsIgnoreCase(symbol))
					await SendFuturesPositionAsync(position, cancelMsg.TransactionId, cancellationToken, 0m);
			}
		}

		if (cancelMsg.SecurityTypes is { Length: > 0 } && !cancelMsg.SecurityTypes.Contains(
			section == CoinWSections.Spot ? SecurityTypes.CryptoCurrency : SecurityTypes.Future))
			return;
		if (section == CoinWSections.Spot && cancelMsg.Side is null)
		{
			await RestClient.CancelAllSpotOrdersAsync(symbol, cancellationToken);
			return;
		}
		if (section == CoinWSections.Spot)
		{
			foreach (var order in await RestClient.GetSpotOpenOrdersAsync(new() { Symbol = symbol }, cancellationToken))
			{
				if (cancelMsg.Side is Sides side && order.Side.ToStockSharpSide() != side)
					continue;
				_ = await RestClient.CancelSpotOrderAsync(new() { OrderId = order.OrderId }, cancellationToken);
			}
		}
		else
		{
			foreach (var order in await RestClient.GetFuturesOpenOrdersAsync(GetFuturesNativeSymbol(symbol), 100,
				cancellationToken))
			{
				if (cancelMsg.Side is Sides side && order.Direction.ToStockSharpSide() != side)
					continue;
				await RestClient.CancelFuturesOrderAsync(new() { OrderId = order.OrderId }, cancellationToken);
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
			return;
		}
		_portfolioSubscriptionId = lookupMsg.TransactionId;
		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = _portfolioName,
			BoardCode = BoardCodes.CoinW,
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
			? (CoinWSections?)null
			: ResolveSection(statusMsg.SecurityId);
		var limit = (statusMsg.Count ?? 100).Min(1000).Max(1).To<int>();
		await SendOrderSnapshotAsync(statusMsg.TransactionId, section, symbol,
			(statusMsg.From, statusMsg.To), limit, cancellationToken);
		_orderStatusSubscriptionId = statusMsg.TransactionId;
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshotAsync(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (IsSectionEnabled(CoinWSections.Spot))
		{
			foreach (var balance in await RestClient.GetSpotBalancesAsync(cancellationToken))
				await SendSpotBalanceAsync(balance, originalTransactionId, cancellationToken);
		}
		if (IsSectionEnabled(CoinWSections.Futures))
		{
			var assets = await RestClient.GetFuturesAssetsAsync("USDT", cancellationToken);
			if (assets is not null)
				await SendFuturesAssetsAsync("USDT", assets, originalTransactionId, cancellationToken);
			foreach (var position in await RestClient.GetFuturesPositionsAsync(cancellationToken))
				await SendFuturesPositionAsync(position, originalTransactionId, cancellationToken);
		}
	}

	private async ValueTask SendOrderSnapshotAsync(long originalTransactionId, CoinWSections? section,
		string symbol, (DateTime? From, DateTime? To)? range, int limit,
		CancellationToken cancellationToken)
	{
		var sections = section is null ? Sections.ToArray() : [section.Value];
		foreach (var current in sections)
		{
			if (current == CoinWSections.Spot)
			{
				var symbols = symbol.IsEmpty() ? GetKnownSymbols(CoinWSections.Spot) : [symbol];
				foreach (var currentSymbol in symbols)
				{
					var from = range?.From?.ToUnixMilliseconds();
					var to = range?.To?.ToUnixMilliseconds();
					var orders = new List<CoinWSpotOrder>();
					orders.AddRange(await RestClient.GetSpotOpenOrdersAsync(new()
					{
						Symbol = currentSymbol,
						From = from,
						To = to,
					}, cancellationToken));
					orders.AddRange(await RestClient.GetSpotOrderHistoryAsync(new()
					{
						Symbol = currentSymbol,
						From = from,
						To = to,
					}, cancellationToken));
					foreach (var order in orders
						.Where(static item => item?.OrderId.IsEmpty() == false)
						.GroupBy(static item => item.OrderId, StringComparer.OrdinalIgnoreCase)
						.Select(static group => group.OrderByDescending(static item => item.Time).First())
						.OrderBy(static item => item.Time).Take(limit))
					{
						order.Symbol = order.Symbol.IsEmpty(currentSymbol);
						await SendSpotOrderAsync(order, originalTransactionId, cancellationToken);
					}
					foreach (var trade in (await RestClient.GetSpotUserTradesAsync(new()
					{
						Symbol = currentSymbol,
						From = from,
						To = to,
						Limit = limit,
					}, cancellationToken)).OrderBy(static item => item.Time).Take(limit))
					{
						trade.Symbol = trade.Symbol.IsEmpty(currentSymbol);
						await SendSpotTradeAsync(trade, originalTransactionId, cancellationToken);
					}
				}
			}
			else
			{
				var symbols = symbol.IsEmpty() ? GetKnownSymbols(CoinWSections.Futures) : [symbol];
				foreach (var currentSymbol in symbols)
				{
					var native = GetFuturesNativeSymbol(currentSymbol);
					var orders = new List<CoinWFuturesOrder>();
					orders.AddRange(await RestClient.GetFuturesOpenOrdersAsync(native, limit, cancellationToken));
					foreach (var originType in new[] { "plan", "execute", "planTrigger" })
						orders.AddRange(await RestClient.GetFuturesOrderHistoryAsync(new()
						{
							Instrument = native,
							OriginType = originType,
							PageSize = limit,
						}, cancellationToken));
					foreach (var order in orders
						.Where(static item => item?.OrderId.IsEmpty() == false)
						.GroupBy(static item => item.OrderId, StringComparer.OrdinalIgnoreCase)
						.Select(static group => group.OrderByDescending(static item => item.UpdatedTime).First())
						.Where(item => range?.From is null || GetFuturesOrderTime(item) >= range.Value.From)
						.Where(item => range?.To is null || GetFuturesOrderTime(item) <= range.Value.To)
						.OrderBy(GetFuturesOrderTime).Take(limit))
						await SendFuturesOrderAsync(order, originalTransactionId, cancellationToken);
				}
			}
		}
	}

	private async ValueTask OnBalanceAsync(CoinWSections section, CoinWWsBalanceUpdate[] updates,
		CancellationToken cancellationToken)
	{
		foreach (var update in updates ?? [])
		{
			if (update?.Asset.IsEmpty() != false)
				continue;
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = update.Asset.ToStockSharp(section),
				ServerTime = update.Time > 0 ? update.Time.ToUtcTime() : CurrentTime,
				OriginalTransactionId = _portfolioSubscriptionId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, update.Available.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.BlockedValue, update.Held.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.CurrentPrice, update.Margin.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, update.UnrealizedPnl.ToDecimal(), true), cancellationToken);
		}
	}

	private async ValueTask OnOrderAsync(CoinWSections section, CoinWWsOrderUpdate[] updates,
		CancellationToken cancellationToken)
	{
		foreach (var update in updates ?? [])
		{
			var symbol = section == CoinWSections.Spot
				? NormalizeSpotSymbol(update.Symbol)
				: ResolveFuturesSymbol(update.Symbol);
			if (symbol.IsEmpty() || update.OrderId.IsEmpty())
				continue;
			var side = ResolveOrderSide(update.OrderId, update.Side);
			if (side is null)
			{
				this.AddWarningLog("CoinW omitted the side for external market order {0}; the ambiguous update was skipped.",
					update.OrderId);
				continue;
			}
			RememberOrderSide(update.OrderId, side);
			var volume = section == CoinWSections.Futures
				? ResolveFuturesVolume(update.Volume, update.Contracts, update.ContractSize, symbol,
					update.QuantityUnit)
				: update.Volume.ToDecimal();
			var remaining = update.RemainingVolume.ToDecimal();
			var executed = section == CoinWSections.Futures
				? ResolveFuturesVolume(update.ExecutedVolume, update.ExecutedContracts,
					update.ContractSize, symbol, update.QuantityUnit)
				: update.ExecutedVolume.ToDecimal();
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = symbol.ToStockSharp(section),
				ServerTime = update.Time > 0 ? update.Time.ToUtcTime() : CurrentTime,
				PortfolioName = _portfolioName,
				Side = side.Value,
				OrderVolume = volume,
				Balance = remaining ?? (volume is null ? null : (volume.Value - (executed ?? 0m)).Max(0m)),
				OrderPrice = update.Price.ToDecimal() ?? 0m,
				AveragePrice = update.AveragePrice.ToDecimal(),
				OrderType = ToOrderType(update.OrderType),
				OrderState = section == CoinWSections.Spot
					? ToSpotWsOrderState(update.Status)
					: update.Status.ToFuturesOrderState(),
				OrderId = ParseLong(update.OrderId),
				OrderStringId = update.ClientOrderId,
				TransactionId = ParseTransactionId(update.ClientOrderId),
				OriginalTransactionId = _orderStatusSubscriptionId,
				Commission = update.Fee.ToDecimal(),
			}, cancellationToken);
		}
	}

	private async ValueTask OnPositionAsync(CoinWWsPositionUpdate[] updates,
		CancellationToken cancellationToken)
	{
		foreach (var update in updates ?? [])
		{
			var symbol = ResolveFuturesSymbol(update.Symbol);
			if (symbol.IsEmpty())
				continue;
			var volume = ResolveFuturesVolume(update.Volume, update.Contracts, update.ContractSize, symbol);
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = _portfolioName,
				SecurityId = symbol.ToStockSharp(CoinWSections.Futures),
				ServerTime = update.Time > 0 ? update.Time.ToUtcTime() : CurrentTime,
				OriginalTransactionId = _portfolioSubscriptionId,
				Side = update.Side.ToStockSharpSide(),
			}
			.TryAdd(PositionChangeTypes.CurrentValue, update.Status.EqualsIgnoreCase("close") ? 0m : volume, true)
			.TryAdd(PositionChangeTypes.AveragePrice, update.OpenPrice.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.CurrentPrice, update.IndexPrice.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.CurrentValueInLots, update.Contracts.ToDecimal(), true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, update.UnrealizedPnl.ToDecimal(), true), cancellationToken);
		}
	}

	private async ValueTask OnFillAsync(CoinWWsFillUpdate[] updates,
		CancellationToken cancellationToken)
	{
		foreach (var update in updates ?? [])
		{
			var symbol = ResolveFuturesSymbol(update.Symbol);
			if (symbol.IsEmpty())
				continue;
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				SecurityId = symbol.ToStockSharp(CoinWSections.Futures),
				ServerTime = update.Time > 0 ? update.Time.ToUtcTime() : CurrentTime,
				PortfolioName = _portfolioName,
				Side = update.Side.ToStockSharpSide(),
				OrderId = ParseLong(update.OrderId),
				TradeStringId = update.TradeId,
				TradePrice = update.Price.ToDecimal(),
				TradeVolume = ResolveFuturesVolume(update.Volume, update.Contracts, update.ContractSize, symbol),
				Commission = update.Fee.ToDecimal(),
				PnL = update.RealizedPnl.ToDecimal(),
				OriginalTransactionId = _orderStatusSubscriptionId,
			}, cancellationToken);
		}
	}

	private ValueTask SendSpotBalanceAsync(CoinWSpotBalance balance, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (balance?.Asset.IsEmpty() != false)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = balance.Asset.ToStockSharp(CoinWSections.Spot),
			ServerTime = CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, balance.Available.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.BlockedValue, balance.Held.ToDecimal(), true), cancellationToken);
	}

	private ValueTask SendFuturesAssetsAsync(string asset, CoinWFuturesAssets balance,
		long originalTransactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = asset.ToStockSharp(CoinWSections.Futures),
			ServerTime = balance.Time > 0 ? balance.Time.ToUtcTime() : CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, balance.Available.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.BlockedValue, balance.Frozen.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.CurrentPrice, balance.Margin.ToDecimal(), true), cancellationToken);

	private ValueTask SendFuturesPositionAsync(CoinWFuturesPosition position, long originalTransactionId,
		CancellationToken cancellationToken, decimal? forcedValue = null)
	{
		var symbol = ResolveFuturesSymbol(position?.NativeSymbol);
		if (symbol.IsEmpty())
			return default;
		var volume = forcedValue ?? ResolveFuturesVolume(position.Quantity, position.CurrentContracts,
			position.ContractSize, symbol, position.QuantityUnit);
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = symbol.ToStockSharp(CoinWSections.Futures),
			ServerTime = position.UpdatedTime > 0 ? position.UpdatedTime.ToUtcTime() : CurrentTime,
			OriginalTransactionId = originalTransactionId,
			Side = position.Direction.ToStockSharpSide(),
		}
		.TryAdd(PositionChangeTypes.CurrentValue, volume, true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.OpenPrice.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.CurrentPrice, position.IndexPrice.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.CurrentValueInLots, position.CurrentContracts.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnl.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedPnl.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.LiquidationPrice, position.LiquidationPrice.ToDecimal(), true), cancellationToken);
	}

	private ValueTask SendSpotOrderAsync(CoinWSpotOrder order, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var symbol = NormalizeSpotSymbol(order?.Symbol);
		if (symbol.IsEmpty() || order.OrderId.IsEmpty())
			return default;
		var side = order.Side.ToStockSharpSide();
		RememberOrderSide(order.OrderId, side);
		var volume = order.Volume.ToDecimal();
		var executed = order.ExecutedVolume.ToDecimal();
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(CoinWSections.Spot),
			ServerTime = order.Time > 0 ? order.Time.ToUtcTime() : CurrentTime,
			PortfolioName = _portfolioName,
			Side = side,
			OrderVolume = volume,
			Balance = volume is null ? null : (volume.Value - (executed ?? 0m)).Max(0m),
			OrderPrice = order.Price.ToDecimal() ?? 0m,
			AveragePrice = CalculateAverage(order.ExecutedValue, order.ExecutedVolume),
			OrderType = OrderTypes.Limit,
			OrderState = order.Status.ToSpotOrderState(),
			OrderId = ParseLong(order.OrderId),
			OrderStringId = order.ClientOrderId,
			TransactionId = ParseTransactionId(order.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private ValueTask SendFuturesOrderAsync(CoinWFuturesOrder order, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var symbol = ResolveFuturesSymbol(order?.NativeSymbol);
		if (symbol.IsEmpty() || order.OrderId.IsEmpty())
			return default;
		var side = order.Direction.ToStockSharpSide();
		RememberOrderSide(order.OrderId, side);
		var volume = ResolveFuturesVolume(order.Quantity, order.TotalContracts, order.ContractSize, symbol,
			order.QuantityUnit);
		var executed = ResolveFuturesVolume(null, order.ExecutedContracts, order.ContractSize, symbol,
			order.QuantityUnit);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(CoinWSections.Futures),
			ServerTime = GetFuturesOrderTime(order),
			PortfolioName = _portfolioName,
			Side = side,
			OrderVolume = volume,
			Balance = volume is null ? null : (volume.Value - (executed ?? 0m)).Max(0m),
			OrderPrice = order.Price.ToDecimal() ?? 0m,
			AveragePrice = order.AveragePrice.ToDecimal(),
			OrderType = ToOrderType(order.OriginalType),
			OrderState = order.OrderStatus.IsEmpty(order.Status).ToFuturesOrderState(),
			OrderId = ParseLong(order.OrderId),
			OrderStringId = order.ClientOrderId,
			TransactionId = ParseTransactionId(order.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
			Commission = order.Fee.ToDecimal(),
			Condition = order.TriggerPrice.IsEmpty() ? null : new CoinWOrderCondition
			{
				TriggerPrice = order.TriggerPrice.ToDecimal(),
			},
		}, cancellationToken);
	}

	private ValueTask SendSpotTradeAsync(CoinWSpotTrade trade, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var symbol = NormalizeSpotSymbol(trade?.Symbol);
		if (symbol.IsEmpty())
			return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = symbol.ToStockSharp(CoinWSections.Spot),
			ServerTime = trade.Time > 0 ? trade.Time.ToUtcTime() : CurrentTime,
			PortfolioName = _portfolioName,
			Side = trade.Side.ToStockSharpSide(),
			OrderId = ParseLong(trade.OrderId),
			TradeStringId = trade.TradeId,
			TradePrice = trade.Price.ToDecimal(),
			TradeVolume = trade.Volume.ToDecimal(),
			Commission = trade.Fee.ToDecimal(),
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private string[] GetKnownSymbols(CoinWSections section)
	{
		using (_sync.EnterScope())
			return section == CoinWSections.Spot
				? [.. _spotPairIds.Keys]
				: [.. _futuresNativeSymbols.Keys];
	}

	private string ResolveFuturesSymbol(string nativeSymbol)
	{
		if (nativeSymbol.IsEmpty())
			return null;
		using (_sync.EnterScope())
			return _futuresSymbols.TryGetValue(nativeSymbol, out var symbol)
				? symbol
				: nativeSymbol.ToCoinWFuturesSecurityCode();
	}

	private static string NormalizeSpotSymbol(string symbol)
		=> symbol?.Replace('-', '_').ToUpperInvariant();

	private decimal? ResolveFuturesVolume(string quantity, string contracts, string contractSize,
		string symbol, int? quantityUnit = null)
	{
		var size = contractSize.ToDecimal() ?? GetContractSize(symbol);
		if (contracts.ToDecimal() is decimal pieces)
			return pieces * size;
		if (quantity.ToDecimal() is not decimal volume)
			return null;
		return quantityUnit == (int)CoinWFuturesQuantityUnits.Contracts ? volume * size : volume;
	}

	private void RememberOrderSide(string orderId, Sides? side)
	{
		if (orderId.IsEmpty() || side is null)
			return;
		using (_sync.EnterScope())
			_orderSides[orderId] = side.Value;
	}

	private Sides? ResolveOrderSide(string orderId, string side)
	{
		if (!side.IsEmpty())
			return side.ToStockSharpSide();
		using (_sync.EnterScope())
			return _orderSides.TryGetValue(orderId, out var value) ? value : null;
	}

	private static OrderTypes ToOrderType(string value)
		=> value?.ToLowerInvariant() switch
		{
			"execute" or "market" => OrderTypes.Market,
			"plantrigger" or "trigger" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	private static OrderStates ToSpotWsOrderState(string value)
		=> value?.ToUpperInvariant() switch
		{
			"RECEIVED" or "OPEN" or "PARTIAL" or "PARTIALLY_FILLED" => OrderStates.Active,
			"DONE" or "FILLED" or "CANCELLED" or "CANCELED" => OrderStates.Done,
			"REJECTED" or "FAILED" => OrderStates.Failed,
			_ => OrderStates.None,
		};

	private static DateTime GetFuturesOrderTime(CoinWFuturesOrder order)
		=> (order.UpdatedTime > 0 ? order.UpdatedTime : order.CreatedTime) is var time && time > 0
			? time.ToUtcTime()
			: DateTime.UtcNow;

	private static long? ParseLong(string value)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;

	private static decimal? CalculateAverage(string value, string quantity)
		=> value.ToDecimal() is decimal total && quantity.ToDecimal() is decimal amount && amount != 0m
			? total / amount
			: null;
}
