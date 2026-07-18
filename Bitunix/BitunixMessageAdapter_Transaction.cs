namespace StockSharp.Bitunix;

public partial class BitunixMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		var section = ResolveSection(regMsg.SecurityId);
		EnsurePrivateReady(section);
		var symbol = GetSymbol(regMsg.SecurityId);
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException("Order volume must be positive.");
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
			throw new InvalidOperationException("A positive limit price is required.");

		var condition = regMsg.Condition as BitunixOrderCondition ?? new BitunixOrderCondition();
		string orderId;
		if (section == BitunixSections.Spot)
		{
			if (regMsg.PostOnly == true || regMsg.TimeInForce is TimeInForce.CancelBalance or
				TimeInForce.MatchOrCancel)
				throw new NotSupportedException(
					"Bitunix spot OpenAPI supports limit and market orders without time-in-force controls.");
			var result = await SpotRestClient.PlaceSpotOrderAsync(new()
			{
				Side = regMsg.Side == Sides.Buy ? 2 : 1,
				Type = orderType == OrderTypes.Market ? 2 : 1,
				Volume = volume.ToWire(),
				Price = orderType == OrderTypes.Market ? "0" : regMsg.Price.ToWire(),
				Symbol = symbol,
			}, cancellationToken);
			if (result is null || result.OrderId.IsEmpty())
				throw new InvalidDataException("Bitunix spot API returned no order ID.");
			if (!result.PlaceStatus.IsEmpty() && result.PlaceStatus != "1")
				throw new InvalidOperationException(
					$"Bitunix spot order was rejected with placeStatus={result.PlaceStatus}.");
			orderId = result.OrderId;
			using (_sync.EnterScope())
				_spotOrderSymbols.Add(symbol);
		}
		else
		{
			var product = GetFuturesProduct(symbol);
			if (condition.Leverage < product.MinimumLeverage ||
				condition.Leverage > product.MaximumLeverage)
				throw new ArgumentOutOfRangeException(nameof(condition.Leverage), condition.Leverage,
					$"Leverage must be between {product.MinimumLeverage} and {product.MaximumLeverage}.");

			await FuturesRestClient.ChangeFuturesMarginModeAsync(new()
			{
				Symbol = symbol,
				MarginCoin = product.Quote,
				MarginMode = condition.MarginMode == MarginModes.Isolated ? "ISOLATION" : "CROSS",
			}, cancellationToken);
			await FuturesRestClient.ChangeFuturesLeverageAsync(new()
			{
				Symbol = symbol,
				MarginCoin = product.Quote,
				Leverage = condition.Leverage,
			}, cancellationToken);

			var isClose = condition.IsReduceOnly ||
				regMsg.PositionEffect == OrderPositionEffects.CloseOnly;
			var result = await FuturesRestClient.PlaceFuturesOrderAsync(new()
			{
				MarginCoin = product.Quote,
				Symbol = symbol,
				Quantity = volume.ToWire(),
				Price = orderType == OrderTypes.Market ? null : regMsg.Price.ToWire(),
				Side = regMsg.Side == Sides.Buy ? "BUY" : "SELL",
				TradeSide = isClose ? "CLOSE" : "OPEN",
				OrderType = orderType == OrderTypes.Market ? "MARKET" : "LIMIT",
				Effect = regMsg.PostOnly == true ? "POST_ONLY" : regMsg.TimeInForce switch
				{
					TimeInForce.CancelBalance => "IOC",
					TimeInForce.MatchOrCancel => "FOK",
					_ => "GTC",
				},
				ClientId = CreateClientOrderId(regMsg.TransactionId, regMsg.UserOrderId),
				IsReduceOnly = isClose,
			}, cancellationToken);
			orderId = result?.OrderId.ThrowIfEmpty("Bitunix futures order ID");
		}

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = CurrentTime,
			PortfolioName = GetPortfolioName(section),
			Side = regMsg.Side,
			OrderVolume = volume,
			Balance = volume,
			OrderPrice = orderType == OrderTypes.Market ? 0m : regMsg.Price,
			OrderType = orderType,
			OrderState = OrderStates.Active,
			OrderStringId = orderId,
			TransactionId = regMsg.TransactionId,
			OriginalTransactionId = regMsg.TransactionId,
			TimeInForce = regMsg.TimeInForce,
			PostOnly = regMsg.PostOnly,
			PositionEffect = regMsg.PositionEffect,
			Condition = condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		var section = ResolveSection(replaceMsg.SecurityId);
		EnsurePrivateReady(section);
		if (section == BitunixSections.Spot)
			throw new NotSupportedException(
				"Bitunix spot OpenAPI does not expose atomic order replacement.");
		if (replaceMsg.OrderType == OrderTypes.Market)
			throw new NotSupportedException("Bitunix can amend active futures limit orders only.");
		if (replaceMsg.Price <= 0)
			throw new InvalidOperationException("Replacement price must be positive.");
		var volume = replaceMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException("Replacement volume must be positive.");

		var orderId = replaceMsg.OldOrderStringId;
		if (orderId.IsEmpty() && replaceMsg.OldOrderId is long numericOrderId)
			orderId = numericOrderId.ToString(CultureInfo.InvariantCulture);
		if (orderId.IsEmpty())
			throw new InvalidOperationException("Bitunix replacement requires an exchange order ID.");

		var symbol = GetSymbol(replaceMsg.SecurityId);
		var product = GetFuturesProduct(symbol);
		var result = await FuturesRestClient.ModifyFuturesOrderAsync(new()
		{
			OrderId = orderId,
			MarginCoin = product.Quote,
			Quantity = volume.ToWire(),
			Price = replaceMsg.Price.ToWire(),
		}, cancellationToken);
		var resultOrderId = result?.OrderId.IsEmpty() == false ? result.OrderId : orderId;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(BitunixSections.Futures),
			ServerTime = CurrentTime,
			PortfolioName = GetPortfolioName(BitunixSections.Futures),
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
		var section = ResolveSection(cancelMsg.SecurityId);
		EnsurePrivateReady(section);
		var symbol = GetSymbol(cancelMsg.SecurityId);
		var orderId = cancelMsg.OrderStringId;
		if (orderId.IsEmpty() && cancelMsg.OrderId is long numericOrderId)
			orderId = numericOrderId.ToString(CultureInfo.InvariantCulture);
		if (orderId.IsEmpty())
			throw new InvalidOperationException("Bitunix cancellation requires an exchange order ID.");

		if (section == BitunixSections.Spot)
		{
			await SpotRestClient.CancelSpotOrdersAsync(new()
			{
				OrderIdList = [new() { OrderId = orderId, Symbol = symbol }],
			}, cancellationToken);
			using (_sync.EnterScope())
				_spotOrderSymbols.Add(symbol);
		}
		else
		{
			var product = GetFuturesProduct(symbol);
			var result = await FuturesRestClient.CancelFuturesOrdersAsync(new()
			{
				MarginCoin = product.Quote,
				Symbol = symbol,
				OrderList = [new() { OrderId = orderId }],
			}, cancellationToken);
			ThrowIfCancellationFailed(result, orderId);
		}

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = CurrentTime,
			PortfolioName = GetPortfolioName(section),
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
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"Bitunix OpenAPI does not expose a single cross-section close-all-positions command.");

		var hasSecurity = !cancelMsg.SecurityId.SecurityCode.IsEmpty();
		var requestedSection = !cancelMsg.SecurityId.BoardCode.IsEmpty()
			? cancelMsg.SecurityId.BoardCode.ToSection()
			: (BitunixSections?)null;

		foreach (var section in Sections.Where(section =>
			requestedSection is null || requestedSection == section))
		{
			EnsurePrivateReady(section);
			var symbol = hasSecurity ? GetSymbol(cancelMsg.SecurityId) : null;
			if (section == BitunixSections.Spot)
			{
				string[] symbols;
				using (_sync.EnterScope())
					symbols = symbol.IsEmpty() ? [.. _spotOrderSymbols] : [symbol];
				foreach (var spotSymbol in symbols)
				{
					var orders = await SpotRestClient.GetSpotPendingOrdersAsync(spotSymbol,
						cancellationToken) ?? [];
					var references = orders
						.Where(order => cancelMsg.Side is null || order.Side.ToSide() == cancelMsg.Side)
						.Select(order => new BitunixSpotOrderReference
						{
							OrderId = order.OrderId,
							Symbol = spotSymbol,
						}).ToArray();
					if (references.Length > 0)
						await SpotRestClient.CancelSpotOrdersAsync(new() { OrderIdList = references },
							cancellationToken);
				}
			}
			else
			{
				var pending = await FuturesRestClient.GetFuturesPendingOrdersAsync(new()
				{
					Symbol = symbol,
					Limit = 100,
				}, cancellationToken);
				foreach (var group in (pending?.Orders ?? [])
					.Where(order => cancelMsg.Side is null || order.Side.ToSide() == cancelMsg.Side)
					.GroupBy(static order => order.Symbol))
				{
					var product = GetFuturesProduct(group.Key);
					if (cancelMsg.Side is null)
					{
						var result = await FuturesRestClient.CancelAllFuturesOrdersAsync(new()
						{
							MarginCoin = product.Quote,
							Symbol = group.Key,
						}, cancellationToken);
						ThrowIfCancellationFailed(result, null);
					}
					else
					{
						var result = await FuturesRestClient.CancelFuturesOrdersAsync(new()
						{
							MarginCoin = product.Quote,
							Symbol = group.Key,
							OrderList = [.. group.Select(static order =>
								new BitunixFuturesOrderReference { OrderId = order.OrderId })],
						}, cancellationToken);
						ThrowIfCancellationFailed(result, null);
					}
				}
			}
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!lookupMsg.IsSubscribe)
		{
			_portfolioSubscriptionId = 0;
			await RefreshPrivateSubscriptionsAsync(cancellationToken);
			return;
		}
		if (_spotRestClient?.IsCredentialsAvailable != true &&
			_futuresRestClient?.IsCredentialsAvailable != true)
			throw new InvalidOperationException(
				"Bitunix API key and secret are required for portfolio data.");

		foreach (var section in Sections)
		{
			var available = section == BitunixSections.Spot
				? _spotRestClient?.IsCredentialsAvailable == true
				: _futuresRestClient?.IsCredentialsAvailable == true;
			if (!available)
				continue;
			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = GetPortfolioName(section),
				BoardCode = section.ToBoardCode(),
				OriginalTransactionId = lookupMsg.TransactionId,
			}, cancellationToken);
		}

		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		if (!lookupMsg.IsHistoryOnly())
		{
			_portfolioSubscriptionId = lookupMsg.TransactionId;
			await RefreshPrivateSubscriptionsAsync(cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!statusMsg.IsSubscribe)
		{
			_orderStatusSubscriptionId = 0;
			await RefreshPrivateSubscriptionsAsync(cancellationToken);
			return;
		}
		if (_spotRestClient?.IsCredentialsAvailable != true &&
			_futuresRestClient?.IsCredentialsAvailable != true)
			throw new InvalidOperationException(
				"Bitunix API key and secret are required for order data.");

		BitunixSections? section = statusMsg.SecurityId.BoardCode.IsEmpty()
			? null
			: statusMsg.SecurityId.BoardCode.ToSection();
		var symbol = statusMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetSymbol(statusMsg.SecurityId);
		if (section == BitunixSections.Spot && !symbol.IsEmpty())
		{
			using (_sync.EnterScope())
				_spotOrderSymbols.Add(symbol);
		}

		await SendOrderSnapshotAsync(statusMsg.TransactionId, symbol, statusMsg.From,
			statusMsg.To, cancellationToken, section);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		if (!statusMsg.IsHistoryOnly())
		{
			_orderStatusSubscriptionId = statusMsg.TransactionId;
			await RefreshPrivateSubscriptionsAsync(cancellationToken);
		}
	}

	private async ValueTask SendPortfolioSnapshotAsync(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (_spotRestClient?.IsCredentialsAvailable == true)
			await SendSpotPortfolioSnapshotAsync(originalTransactionId, cancellationToken);

		if (_futuresRestClient?.IsCredentialsAvailable == true)
		{
			string[] marginCoins;
			using (_sync.EnterScope())
				marginCoins = [.. _futuresProducts.Values
					.Select(static product => product.Quote)
					.Where(static coin => !coin.IsEmpty())
					.Distinct(StringComparer.OrdinalIgnoreCase)];
			if (marginCoins.Length == 0)
				marginCoins = ["USDT"];

			foreach (var marginCoin in marginCoins)
			{
				var account = await FuturesRestClient.GetFuturesAccountAsync(marginCoin,
					cancellationToken);
				if (account is not null)
					await SendFuturesAccountAsync(account, 0, originalTransactionId,
						cancellationToken);
			}

			foreach (var position in await FuturesRestClient.GetFuturesPositionsAsync(null,
				cancellationToken) ?? [])
				await SendFuturesPositionAsync(position, 0, originalTransactionId,
					cancellationToken);
		}
	}

	private async ValueTask SendSpotPortfolioSnapshotAsync(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		foreach (var balance in await SpotRestClient.GetSpotBalancesAsync(cancellationToken) ?? [])
			await SendSpotBalanceAsync(balance, originalTransactionId, cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(long originalTransactionId, string symbol,
		DateTime? from, DateTime? to, CancellationToken cancellationToken,
		BitunixSections? requestedSection = null, bool onlyNewFills = false)
	{
		var startTime = from?.ToUniversalTime();
		var endTime = to?.ToUniversalTime();

		if ((requestedSection is null or BitunixSections.Spot) &&
			_spotRestClient?.IsCredentialsAvailable == true)
		{
			string[] symbols;
			using (_sync.EnterScope())
				symbols = symbol.IsEmpty() ? [.. _spotOrderSymbols] : [symbol];
			if (symbols.Length == 0 && !onlyNewFills)
				this.AddWarningLog(
					"Bitunix spot order lookup requires a symbol; no previously used spot symbols are known.");

			foreach (var spotSymbol in symbols)
			{
				var orders = new List<BitunixSpotOrder>(
					await SpotRestClient.GetSpotPendingOrdersAsync(spotSymbol, cancellationToken) ?? []);
				var history = await SpotRestClient.GetSpotOrderHistoryAsync(new()
				{
					Page = 1,
					PageSize = 100,
					StartTime = startTime?.ToString("O", CultureInfo.InvariantCulture),
					EndTime = endTime?.ToString("O", CultureInfo.InvariantCulture),
					Symbol = spotSymbol,
				}, cancellationToken);
				orders.AddRange(history?.Orders ?? []);

				foreach (var order in orders
					.Where(static order => order?.OrderId.IsEmpty() == false)
					.GroupBy(static order => order.OrderId)
					.Select(static group => group.First())
					.OrderBy(static order => order.CreateTime))
				{
					await SendSpotOrderAsync(order, originalTransactionId, cancellationToken);
					if (order.FilledVolume <= 0)
						continue;
					var fill = await SpotRestClient.GetSpotFillAsync(order.OrderId, spotSymbol,
						cancellationToken);
					if (fill is not null)
						await SendSpotFillAsync(order, fill, originalTransactionId, onlyNewFills,
							cancellationToken);
				}
			}
		}

		if ((requestedSection is null or BitunixSections.Futures) &&
			_futuresRestClient?.IsCredentialsAvailable == true)
		{
			var orderQuery = new BitunixFuturesOrdersQuery
			{
				Symbol = symbol,
				StartTime = startTime is null
					? null
					: new DateTimeOffset(startTime.Value).ToUnixTimeMilliseconds(),
				EndTime = endTime is null
					? null
					: new DateTimeOffset(endTime.Value).ToUnixTimeMilliseconds(),
				Skip = 0,
				Limit = 100,
			};
			var pending = await FuturesRestClient.GetFuturesPendingOrdersAsync(orderQuery,
				cancellationToken);
			var history = await FuturesRestClient.GetFuturesOrderHistoryAsync(orderQuery,
				cancellationToken);
			var orders = (pending?.Orders ?? []).Concat(history?.Orders ?? [])
				.Where(static order => order?.OrderId.IsEmpty() == false)
				.GroupBy(static order => order.OrderId)
				.Select(static group => group.First())
				.OrderBy(static order => order.CreateTime);
			foreach (var order in orders)
				await SendFuturesOrderAsync(order, originalTransactionId, cancellationToken);

			var trades = await FuturesRestClient.GetFuturesTradeHistoryAsync(new()
			{
				Symbol = symbol,
				StartTime = orderQuery.StartTime,
				EndTime = orderQuery.EndTime,
				Skip = 0,
				Limit = 100,
			}, cancellationToken);
			foreach (var trade in (trades?.Trades ?? []).OrderBy(static trade => trade.CreateTime))
				await SendFuturesTradeAsync(trade, originalTransactionId, onlyNewFills,
					cancellationToken);
		}
	}

	private async ValueTask PollSpotPrivateDataAsync(CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId != 0)
			await SendSpotPortfolioSnapshotAsync(_portfolioSubscriptionId, cancellationToken);
		if (_orderStatusSubscriptionId == 0)
			return;
		bool hasSymbols;
		using (_sync.EnterScope())
			hasSymbols = _spotOrderSymbols.Count > 0;
		if (hasSymbols)
			await SendOrderSnapshotAsync(_orderStatusSubscriptionId, null, null, null,
				cancellationToken, BitunixSections.Spot, true);
	}

	private ValueTask OnFuturesOrderAsync(BitunixWsOrder order, long time,
		CancellationToken cancellationToken)
	{
		var status = order.Status.IsEmpty(order.OrderStatus);
		var state = status.ToFuturesOrderState();
		if (state == OrderStates.None)
			state = order.Event.EqualsIgnoreCase("CLOSE") ? OrderStates.Done : OrderStates.Active;
		var serverTime = order.UpdateTime?.UtcDateTime ?? order.CreateTime?.UtcDateTime ??
			(time > 0 ? time.FromMilliseconds() : CurrentTime);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToStockSharp(BitunixSections.Futures),
			ServerTime = serverTime,
			PortfolioName = GetPortfolioName(BitunixSections.Futures),
			OrderStringId = order.OrderId,
			OriginalTransactionId = _orderStatusSubscriptionId,
			OrderPrice = order.Price,
			OrderVolume = order.Quantity,
			Balance = state == OrderStates.Done ? 0m : order.Quantity,
			Side = order.Side.ToSide(),
			OrderType = order.Type.ToOrderType(),
			OrderState = state,
			TimeInForce = order.Effect.ToTimeInForce(),
			PostOnly = order.Effect.EqualsIgnoreCase("POST_ONLY"),
			PositionEffect = order.IsReductionOnly ? OrderPositionEffects.CloseOnly : null,
			Commission = order.Fee,
		}, cancellationToken);
	}

	private ValueTask OnFuturesBalanceAsync(BitunixWsBalance balance, long time,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(BitunixSections.Futures),
			SecurityId = balance.Coin.ToStockSharp(BitunixSections.Futures),
			ServerTime = time > 0 ? time.FromMilliseconds() : CurrentTime,
			OriginalTransactionId = _portfolioSubscriptionId,
		}.TryAdd(PositionChangeTypes.CurrentValue,
			balance.Available + balance.Frozen + balance.Margin, true)
		 .TryAdd(PositionChangeTypes.BlockedValue, balance.Frozen + balance.Margin, true),
			cancellationToken);

	private ValueTask OnFuturesPositionAsync(BitunixWsPosition position, long time,
		CancellationToken cancellationToken)
	{
		var value = position.Side.EqualsIgnoreCase("SHORT") ||
			position.Side.EqualsIgnoreCase("SELL")
			? -position.Quantity
			: position.Quantity;
		var averagePrice = position.Quantity != 0
			? position.EntryValue / position.Quantity.Abs()
			: 0m;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(BitunixSections.Futures),
			SecurityId = position.Symbol.ToStockSharp(BitunixSections.Futures),
			ServerTime = position.UpdateTime?.UtcDateTime ?? position.CreateTime?.UtcDateTime ??
				(time > 0 ? time.FromMilliseconds() : CurrentTime),
			OriginalTransactionId = _portfolioSubscriptionId,
		}.TryAdd(PositionChangeTypes.CurrentValue, value, true)
		 .TryAdd(PositionChangeTypes.AveragePrice, averagePrice, true)
		 .TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedPnl, true)
		 .TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnl, true)
		 .TryAdd(PositionChangeTypes.Leverage, position.Leverage, true), cancellationToken);
	}

	private ValueTask SendSpotBalanceAsync(BitunixSpotBalance balance,
		long originalTransactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(BitunixSections.Spot),
			SecurityId = balance.Coin.ToStockSharp(BitunixSections.Spot),
			ServerTime = CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}.TryAdd(PositionChangeTypes.CurrentValue, balance.Balance, true)
		 .TryAdd(PositionChangeTypes.BlockedValue, balance.Locked, true), cancellationToken);

	private ValueTask SendFuturesAccountAsync(BitunixFuturesAccount account, long time,
		long originalTransactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(BitunixSections.Futures),
			SecurityId = account.MarginCoin.ToStockSharp(BitunixSections.Futures),
			ServerTime = time > 0 ? time.FromMilliseconds() : CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}.TryAdd(PositionChangeTypes.CurrentValue,
			account.Available + account.Frozen + account.Margin, true)
		 .TryAdd(PositionChangeTypes.BlockedValue, account.Frozen + account.Margin, true)
		 .TryAdd(PositionChangeTypes.UnrealizedPnL,
			account.CrossUnrealizedPnl + account.IsolationUnrealizedPnl, true), cancellationToken);

	private ValueTask SendFuturesPositionAsync(BitunixFuturesPosition position, long time,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		var value = position.Side.EqualsIgnoreCase("SHORT") ||
			position.Side.EqualsIgnoreCase("SELL")
			? -position.Quantity
			: position.Quantity;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(BitunixSections.Futures),
			SecurityId = position.Symbol.ToStockSharp(BitunixSections.Futures),
			ServerTime = position.UpdateTime > 0
				? position.UpdateTime.FromMilliseconds()
				: time > 0 ? time.FromMilliseconds() : CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}.TryAdd(PositionChangeTypes.CurrentValue, value, true)
		 .TryAdd(PositionChangeTypes.AveragePrice, position.AverageOpenPrice, true)
		 .TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedPnl, true)
		 .TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnl, true)
		 .TryAdd(PositionChangeTypes.Leverage, position.Leverage, true), cancellationToken);
	}

	private ValueTask SendSpotOrderAsync(BitunixSpotOrder order, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var volume = order.Volume > 0
			? order.Volume
			: order.Price > 0 ? order.Amount / order.Price : 0m;
		var remaining = order.RemainingVolume > 0
			? order.RemainingVolume
			: (volume - order.FilledVolume).Max(0m);
		var type = order.Type.IsEmpty(order.OrderType);
		var time = order.UpdateTime != default ? order.UpdateTime : order.CreateTime;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToStockSharp(BitunixSections.Spot),
			ServerTime = time != default ? time.UtcDateTime : CurrentTime,
			PortfolioName = GetPortfolioName(BitunixSections.Spot),
			OrderStringId = order.OrderId,
			OriginalTransactionId = originalTransactionId,
			OrderPrice = order.Price,
			OrderVolume = volume,
			Balance = remaining,
			Side = order.Side.ToSide(),
			OrderType = type.ToOrderType(),
			OrderState = order.Status.ToSpotOrderState(),
			AveragePrice = order.AveragePrice,
			Commission = order.Fee,
			CommissionCurrency = order.FeeCoin,
		}, cancellationToken);
	}

	private async ValueTask SendSpotFillAsync(BitunixSpotOrder order, BitunixSpotFill fill,
		long originalTransactionId, bool onlyNew, CancellationToken cancellationToken)
	{
		var fillId = fill.Id.IsEmpty(
			$"{order.OrderId}:{fill.Time.UtcTicks}:{fill.Price.ToWire()}:{fill.Volume.ToWire()}");
		if (!RememberFill(_seenSpotFillIds, fillId, onlyNew))
			return;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = order.Symbol.ToStockSharp(BitunixSections.Spot),
			ServerTime = fill.Time != default ? fill.Time.UtcDateTime : CurrentTime,
			PortfolioName = GetPortfolioName(BitunixSections.Spot),
			OrderStringId = order.OrderId,
			TradeStringId = fill.Id,
			OriginalTransactionId = originalTransactionId,
			TradePrice = fill.Price,
			TradeVolume = fill.Volume,
			Side = order.Side.ToSide(),
			IsMarketMaker = fill.Role == "1" || fill.Role.EqualsIgnoreCase("MAKER"),
			Commission = fill.Fee,
			CommissionCurrency = fill.FeeCoin,
		}, cancellationToken);
	}

	private ValueTask SendFuturesOrderAsync(BitunixFuturesOrder order,
		long originalTransactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToStockSharp(BitunixSections.Futures),
			ServerTime = (order.UpdateTime > 0 ? order.UpdateTime : order.CreateTime) > 0
				? (order.UpdateTime > 0 ? order.UpdateTime : order.CreateTime).FromMilliseconds()
				: CurrentTime,
			PortfolioName = GetPortfolioName(BitunixSections.Futures),
			OrderStringId = order.OrderId,
			TransactionId = ParseTransactionId(order.ClientId),
			OriginalTransactionId = originalTransactionId,
			OrderPrice = order.Price,
			OrderVolume = order.Quantity,
			Balance = (order.Quantity - order.TradedQuantity).Max(0m),
			Side = order.Side.ToSide(),
			OrderType = order.OrderType.ToOrderType(),
			OrderState = order.Status.ToFuturesOrderState(),
			AveragePrice = order.AveragePrice,
			TimeInForce = order.Effect.ToTimeInForce(),
			PostOnly = order.Effect.EqualsIgnoreCase("POST_ONLY"),
			PositionEffect = order.IsReduceOnly ? OrderPositionEffects.CloseOnly : null,
			Commission = order.Fee,
			CommissionCurrency = order.MarginCoin,
		}, cancellationToken);

	private async ValueTask SendFuturesTradeAsync(BitunixFuturesTrade trade,
		long originalTransactionId, bool onlyNew, CancellationToken cancellationToken)
	{
		var tradeId = trade.TradeId.IsEmpty(
			$"{trade.OrderId}:{trade.CreateTime}:{trade.Price.ToWire()}:{trade.Quantity.ToWire()}");
		if (!RememberFill(_seenFuturesFillIds, tradeId, onlyNew))
			return;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = trade.Symbol.ToStockSharp(BitunixSections.Futures),
			ServerTime = trade.CreateTime > 0 ? trade.CreateTime.FromMilliseconds() : CurrentTime,
			PortfolioName = GetPortfolioName(BitunixSections.Futures),
			OrderStringId = trade.OrderId,
			TradeStringId = trade.TradeId,
			TransactionId = ParseTransactionId(trade.ClientId),
			OriginalTransactionId = originalTransactionId,
			TradePrice = trade.Price,
			TradeVolume = trade.Quantity,
			Side = trade.Side.ToSide(),
			IsMarketMaker = trade.RoleType.EqualsIgnoreCase("MAKER"),
			Commission = trade.Fee,
			CommissionCurrency = trade.MarginCoin,
		}, cancellationToken);
	}

	private bool RememberFill(HashSet<string> fills, string fillId, bool onlyNew)
	{
		using (_sync.EnterScope())
		{
			var added = fills.Add(fillId);
			return !onlyNew || added;
		}
	}

	private BitunixFuturesProduct GetFuturesProduct(string symbol)
	{
		BitunixFuturesProduct product;
		using (_sync.EnterScope())
			_futuresProducts.TryGetValue(symbol, out product);
		return product ?? throw new InvalidOperationException(
			$"Unknown Bitunix futures symbol '{symbol}'.");
	}

	private static void ThrowIfCancellationFailed(BitunixFuturesOrderResult result,
		string orderId)
	{
		var failure = result?.Failures?.FirstOrDefault();
		if (failure is null)
			return;
		throw new InvalidOperationException(
			$"Bitunix failed to cancel order {orderId}: {failure.Code} {failure.Message}".Trim());
	}
}
