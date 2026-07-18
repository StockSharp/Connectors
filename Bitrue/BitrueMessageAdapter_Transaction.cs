namespace StockSharp.Bitrue;

public partial class BitrueMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		await EnsureInstrumentsAsync(cancellationToken);
		var section = ResolveSection(regMsg.SecurityId);
		EnsurePrivateReady(section);
		var symbol = GetSymbol(regMsg.SecurityId, section);
		ValidateSymbol(symbol, section);
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException("Order volume must be positive.");
		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
			throw new InvalidOperationException("A positive limit price is required.");

		var condition = regMsg.Condition as BitrueOrderCondition ?? new BitrueOrderCondition();
		var clientOrderId = CreateClientOrderId(regMsg.TransactionId, regMsg.UserOrderId);
		string orderId;
		var isReduceOnly = condition.IsReduceOnly ||
			regMsg.PositionEffect == OrderPositionEffects.CloseOnly;

		if (section == BitrueSections.Spot)
		{
			if (regMsg.PostOnly == true || regMsg.TimeInForce is TimeInForce.CancelBalance or
				TimeInForce.MatchOrCancel)
			throw new NotSupportedException(
				"Bitrue spot OpenAPI supports GTC limit and market orders only.");
			if (isReduceOnly)
				throw new NotSupportedException(
					"Reduce-only semantics are available for Bitrue futures only.");

			var result = await SpotRestClient.PlaceOrderAsync(new()
			{
				Symbol = symbol,
				Side = regMsg.Side == Sides.Buy ? BitrueSides.Buy : BitrueSides.Sell,
				OrderType = orderType == OrderTypes.Market
					? BitrueOrderTypes.Market
					: BitrueOrderTypes.Limit,
				TimeInForce = orderType == OrderTypes.Limit
					? BitrueTimeInForces.GoodTillCanceled
					: null,
				Quantity = volume.ToWire(),
				Price = orderType == OrderTypes.Limit ? regMsg.Price.ToWire() : null,
				ClientOrderId = clientOrderId,
			}, cancellationToken);
			orderId = result?.GetOrderId();
			if (orderId.IsEmpty() || orderId == "0")
				throw new InvalidDataException(
					"Bitrue spot accepted the order without returning an order ID.");
			using (_sync.EnterScope())
				_spotOrderSymbols.Add(symbol);
		}
		else
		{
			if (condition.Leverage is < 1 or > 125)
				throw new ArgumentOutOfRangeException(nameof(condition.Leverage), condition.Leverage,
					"Bitrue futures leverage must be between 1 and 125.");
			BitrueFuturesContract contract;
			using (_sync.EnterScope())
				_futuresContracts.TryGetValue(symbol, out contract);
			if (contract is null || contract.Multiplier <= 0)
				throw new InvalidOperationException(
					$"Bitrue futures contract '{symbol}' has no valid multiplier.");

			await FuturesRestClient.SetLeverageAsync(new()
			{
				ContractName = symbol,
				Leverage = condition.Leverage,
			}, cancellationToken);
			var result = await FuturesRestClient.PlaceOrderAsync(new()
			{
				ContractName = symbol,
				ClientOrderId = clientOrderId,
				Side = regMsg.Side == Sides.Buy ? BitrueSides.Buy : BitrueSides.Sell,
				OrderType = orderType.ToBitrue(regMsg.PostOnly == true, regMsg.TimeInForce),
				PositionType = condition.MarginMode == MarginModes.Isolated
					? BitrueFuturesPositionTypes.Isolated
					: BitrueFuturesPositionTypes.Cross,
				Action = isReduceOnly
					? BitrueFuturesActions.Close
					: BitrueFuturesActions.Open,
				Volume = volume,
				Amount = volume * contract.Multiplier,
				Price = orderType == OrderTypes.Limit ? regMsg.Price : 0m,
				Leverage = condition.Leverage,
			}, cancellationToken);
			if (result?.OrderId <= 0)
				throw new InvalidDataException(
					"Bitrue futures accepted the order without returning an order ID.");
			orderId = result.OrderId.ToString(CultureInfo.InvariantCulture);
			using (_sync.EnterScope())
				_futuresOrderSymbols.Add(symbol);
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
			PositionEffect = isReduceOnly ? OrderPositionEffects.CloseOnly : regMsg.PositionEffect,
			Condition = condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		await EnsureInstrumentsAsync(cancellationToken);
		var section = ResolveSection(cancelMsg.SecurityId);
		EnsurePrivateReady(section);
		var symbol = GetSymbol(cancelMsg.SecurityId, section);
		ValidateSymbol(symbol, section);
		var orderId = cancelMsg.OrderStringId;
		if (orderId.IsEmpty() && cancelMsg.OrderId is long numericOrderId)
			orderId = numericOrderId.ToString(CultureInfo.InvariantCulture);
		if (orderId.IsEmpty())
			throw new InvalidOperationException(
				"Bitrue cancellation requires an exchange order ID.");

		if (section == BitrueSections.Spot)
		{
			await SpotRestClient.CancelOrderAsync(new()
			{
				Symbol = symbol,
				OrderId = long.TryParse(orderId, NumberStyles.None,
					CultureInfo.InvariantCulture, out var numericId) ? numericId : null,
				ClientOrderId = long.TryParse(orderId, NumberStyles.None,
					CultureInfo.InvariantCulture, out _) ? null : orderId,
			}, cancellationToken);
			using (_sync.EnterScope())
				_spotOrderSymbols.Add(symbol);
		}
		else
		{
			await FuturesRestClient.CancelOrderAsync(new()
			{
				ContractName = symbol,
				OrderId = long.TryParse(orderId, NumberStyles.None,
					CultureInfo.InvariantCulture, out var numericId) ? numericId : null,
				ClientOrderId = long.TryParse(orderId, NumberStyles.None,
					CultureInfo.InvariantCulture, out _) ? null : orderId,
			}, cancellationToken);
			using (_sync.EnterScope())
				_futuresOrderSymbols.Add(symbol);
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
				"Bitrue group cancellation does not close open futures positions.");
		if (cancelMsg.SecurityTypes is { Length: > 0 } &&
			!cancelMsg.SecurityTypes.Any(static type =>
				type is SecurityTypes.CryptoCurrency or SecurityTypes.Future))
			return;

		await EnsureInstrumentsAsync(cancellationToken);
		BitrueSections? requestedSection = cancelMsg.SecurityId.BoardCode.IsEmpty()
			? null
			: ResolveSection(cancelMsg.SecurityId);
		if (requestedSection is null && !cancelMsg.SecurityId.SecurityCode.IsEmpty())
			requestedSection = ResolveSection(cancelMsg.SecurityId);
		var requestedSymbol = cancelMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetSymbol(cancelMsg.SecurityId, requestedSection.Value);
		var cancellationCount = 0;

		foreach (var section in Sections.Distinct().Where(section =>
			requestedSection is null || requestedSection == section))
		{
			EnsurePrivateReady(section);
			string[] symbols;
			using (_sync.EnterScope())
				symbols = requestedSymbol.IsEmpty()
					? section == BitrueSections.Spot
						? [.. _spotOrderSymbols]
						: [.. _futuresOrderSymbols]
					: [requestedSymbol];

			foreach (var symbol in symbols.Distinct(StringComparer.OrdinalIgnoreCase))
			{
				if (section == BitrueSections.Spot)
				{
					foreach (var order in await SpotRestClient.GetOpenOrdersAsync(symbol,
						cancellationToken) ?? [])
					{
						if (order is null || cancelMsg.Side is Sides side &&
							order.Side.ToStockSharp() != side)
							continue;
						await SpotRestClient.CancelOrderAsync(new()
						{
							Symbol = symbol,
							OrderId = order.OrderId,
						}, cancellationToken);
						cancellationCount++;
					}
				}
				else
				{
					foreach (var order in await FuturesRestClient.GetOpenOrdersAsync(symbol,
						cancellationToken) ?? [])
					{
						if (order is null || cancelMsg.Side is Sides side &&
							order.Side.ToStockSharp() != side)
							continue;
						await FuturesRestClient.CancelOrderAsync(new()
						{
							ContractName = symbol,
							OrderId = order.OrderId,
						}, cancellationToken);
						cancellationCount++;
					}
				}
			}
		}

		if (requestedSymbol.IsEmpty() && cancellationCount == 0)
		{
			bool hasKnownSymbols;
			using (_sync.EnterScope())
				hasKnownSymbols = _spotOrderSymbols.Count > 0 || _futuresOrderSymbols.Count > 0;
			if (!hasKnownSymbols)
				throw new InvalidOperationException(
					"Bitrue group cancellation requires a symbol until this connector session has observed an order symbol.");
		}
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsureConnected();
		foreach (var section in Sections.Distinct())
			EnsurePrivateReady(section);

		if (!lookupMsg.IsSubscribe)
		{
			_portfolioSubscriptionId = 0;
			await RefreshPrivateSubscriptionsAsync(cancellationToken);
			return;
		}

		await EnsureInstrumentsAsync(cancellationToken);
		foreach (var section in Sections.Distinct())
		{
			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = GetPortfolioName(section),
				BoardCode = section == BitrueSections.Spot
					? BoardCodes.Bitrue
					: BoardCodes.BitrueFutures,
				OriginalTransactionId = lookupMsg.TransactionId,
			}, cancellationToken);
		}
		await SendPortfolioSnapshotAsync(lookupMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
			return;
		}

		_portfolioSubscriptionId = lookupMsg.TransactionId;
		await RefreshPrivateSubscriptionsAsync(cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		EnsureConnected();
		foreach (var enabledSection in Sections.Distinct())
			EnsurePrivateReady(enabledSection);

		if (!statusMsg.IsSubscribe)
		{
			_orderStatusSubscriptionId = 0;
			await RefreshPrivateSubscriptionsAsync(cancellationToken);
			return;
		}

		await EnsureInstrumentsAsync(cancellationToken);
		BitrueSections? section = statusMsg.SecurityId.BoardCode.IsEmpty()
			? null
			: ResolveSection(statusMsg.SecurityId);
		if (section is null && !statusMsg.SecurityId.SecurityCode.IsEmpty())
			section = ResolveSection(statusMsg.SecurityId);
		var symbol = statusMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetSymbol(statusMsg.SecurityId, section.Value);
		if (!symbol.IsEmpty())
		{
			ValidateSymbol(symbol, section.Value);
			using (_sync.EnterScope())
			{
				if (section == BitrueSections.Futures || symbol.StartsWith("E-",
					StringComparison.OrdinalIgnoreCase))
					_futuresOrderSymbols.Add(symbol);
				else
					_spotOrderSymbols.Add(symbol);
			}
		}

		var limit = (statusMsg.Count ?? 100).Min(1000).Max(1).To<int>();
		await SendOrderSnapshotAsync(statusMsg.TransactionId, symbol, section,
			statusMsg.From, statusMsg.To, limit, cancellationToken,
			statusMsg.OrderStringId, statusMsg.OrderId);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
			return;
		}

		_orderStatusSubscriptionId = statusMsg.TransactionId;
		await RefreshPrivateSubscriptionsAsync(cancellationToken);
	}

	private async ValueTask RefreshPrivateSubscriptionsAsync(
		CancellationToken cancellationToken)
	{
		if (_spotPrivateWsClient is not null)
		{
			if (_orderStatusSubscriptionId != 0)
				await _spotPrivateWsClient.SubscribeSpotOrdersAsync(cancellationToken);
			else
				await _spotPrivateWsClient.UnsubscribeSpotOrdersAsync(cancellationToken);
			if (_portfolioSubscriptionId != 0)
				await _spotPrivateWsClient.SubscribeSpotBalancesAsync(cancellationToken);
			else
				await _spotPrivateWsClient.UnsubscribeSpotBalancesAsync(cancellationToken);
		}
		if (_futuresPrivateWsClient is not null)
		{
			if (_portfolioSubscriptionId != 0 || _orderStatusSubscriptionId != 0)
				await _futuresPrivateWsClient.SubscribeFuturesAccountAsync(cancellationToken);
			else
				await _futuresPrivateWsClient.UnsubscribeFuturesAccountAsync(cancellationToken);
		}
	}

	private async ValueTask SendPortfolioSnapshotAsync(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (IsSectionEnabled(BitrueSections.Spot))
		{
			var account = await SpotRestClient.GetAccountAsync(cancellationToken);
			foreach (var balance in account?.Balances ?? [])
				await SendSpotBalanceAsync(balance.Asset, balance.Free.ToDecimal(),
					balance.Locked.ToDecimal(), account.UpdateTime, originalTransactionId,
					cancellationToken);
		}

		if (IsSectionEnabled(BitrueSections.Futures))
		{
			var accountData = await FuturesRestClient.GetAccountAsync(cancellationToken);
			foreach (var account in accountData?.Accounts ?? [])
			{
				await SendFuturesBalanceAsync(account.MarginCoin, account.Equity,
					account.Locked, account.RealizedProfit, account.UnrealizedProfit, 0,
					originalTransactionId, cancellationToken);
				foreach (var group in account.PositionGroups ?? [])
				{
					foreach (var position in group?.Positions ?? [])
						await SendFuturesPositionAsync(group.ContractName, position,
							originalTransactionId, cancellationToken);
				}
			}
		}
	}

	private async ValueTask SendOrderSnapshotAsync(long originalTransactionId, string symbol,
		BitrueSections? requestedSection, DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken, string requestedOrderId = null,
		long? requestedNumericOrderId = null, bool onlyNewFills = false)
	{
		var fromUtc = from?.ToUniversalTime();
		var toUtc = to?.ToUniversalTime();
		var orderId = requestedOrderId;
		if (orderId.IsEmpty() && requestedNumericOrderId is long numericOrderId)
			orderId = numericOrderId.ToString(CultureInfo.InvariantCulture);

		if ((requestedSection is null or BitrueSections.Spot) &&
			IsSectionEnabled(BitrueSections.Spot))
		{
			string[] symbols;
			using (_sync.EnterScope())
				symbols = symbol.IsEmpty() ? [.. _spotOrderSymbols] : [symbol];
			if (symbols.Length == 0 && !onlyNewFills)
				this.AddWarningLog(
					"Bitrue spot order lookup requires a symbol; no previously used symbols are known.");
			foreach (var spotSymbol in symbols.Distinct(StringComparer.OrdinalIgnoreCase))
			{
				var orders = new List<BitrueSpotOrder>();
				if (!orderId.IsEmpty())
				{
					var order = await SpotRestClient.GetOrderAsync(new()
					{
						Symbol = spotSymbol,
						OrderId = long.TryParse(orderId, NumberStyles.None,
							CultureInfo.InvariantCulture, out var numericId) ? numericId : null,
						ClientOrderId = long.TryParse(orderId, NumberStyles.None,
							CultureInfo.InvariantCulture, out _) ? null : orderId,
					}, cancellationToken);
					if (order is not null)
						orders.Add(order);
				}
				else
				{
					orders.AddRange(await SpotRestClient.GetOpenOrdersAsync(spotSymbol,
						cancellationToken) ?? []);
					orders.AddRange(await SpotRestClient.GetOrdersAsync(new()
					{
						Symbol = spotSymbol,
						StartTime = fromUtc?.ToUnixMilliseconds(),
						EndTime = toUtc?.ToUnixMilliseconds(),
						Limit = limit,
					}, cancellationToken) ?? []);
				}

				foreach (var order in orders
					.Where(static item => item is not null && item.OrderId > 0)
					.GroupBy(static item => item.OrderId)
					.Select(static group => group.OrderByDescending(GetSpotOrderTime).First())
					.Where(item => IsWithin(GetSpotOrderTime(item), fromUtc, toUtc))
					.OrderBy(GetSpotOrderTime)
					.TakeLast(limit))
					await SendSpotOrderAsync(order, originalTransactionId, cancellationToken);

				foreach (var fill in (await SpotRestClient.GetFillsAsync(new()
				{
					Symbol = spotSymbol,
					StartTime = fromUtc?.ToUnixMilliseconds(),
					EndTime = toUtc?.ToUnixMilliseconds(),
					Limit = limit,
				}, cancellationToken) ?? [])
					.Where(fill => fill is not null &&
						(orderId.IsEmpty() || fill.OrderId.ToString(CultureInfo.InvariantCulture) ==
							orderId))
					.OrderBy(static fill => fill.Timestamp)
					.TakeLast(limit))
					await SendSpotFillAsync(fill, originalTransactionId, onlyNewFills,
						cancellationToken);
			}
		}

		if ((requestedSection is null or BitrueSections.Futures) &&
			IsSectionEnabled(BitrueSections.Futures))
		{
			string[] symbols;
			using (_sync.EnterScope())
				symbols = symbol.IsEmpty() ? [.. _futuresOrderSymbols] : [symbol];
			if (symbols.Length == 0 && !onlyNewFills)
				this.AddWarningLog(
					"Bitrue futures order lookup requires a symbol; no previously used symbols are known.");
			foreach (var futuresSymbol in symbols.Distinct(StringComparer.OrdinalIgnoreCase))
			{
				var orders = orderId.IsEmpty()
					? await FuturesRestClient.GetOpenOrdersAsync(futuresSymbol, cancellationToken)
					: await FuturesRestClient.GetOrderAsync(new()
					{
						ContractName = futuresSymbol,
						OrderId = long.TryParse(orderId, NumberStyles.None,
							CultureInfo.InvariantCulture, out var numericId) ? numericId : null,
						ClientOrderId = long.TryParse(orderId, NumberStyles.None,
							CultureInfo.InvariantCulture, out _) ? null : orderId,
					}, cancellationToken);
				foreach (var order in (orders ?? [])
					.Where(static item => item is not null && item.OrderId > 0)
					.Where(item => IsWithin(GetFuturesOrderTime(item), fromUtc, toUtc))
					.OrderBy(GetFuturesOrderTime)
					.TakeLast(limit))
					await SendFuturesOrderAsync(futuresSymbol, order, originalTransactionId,
						cancellationToken);

				foreach (var fill in (await FuturesRestClient.GetFillsAsync(new()
				{
					ContractName = futuresSymbol,
					StartTime = fromUtc?.ToUnixMilliseconds(),
					EndTime = toUtc?.ToUnixMilliseconds(),
					Limit = limit.Min(1000),
				}, cancellationToken) ?? [])
					.Where(fill => fill is not null &&
						(orderId.IsEmpty() || fill.GetOrderId().ToString(
							CultureInfo.InvariantCulture) == orderId))
					.OrderBy(static fill => fill.Timestamp)
					.TakeLast(limit))
					await SendFuturesFillAsync(fill, originalTransactionId, onlyNewFills,
						cancellationToken);
			}
		}
	}

	private async ValueTask OnSpotPrivateOrderAsync(BitrueSpotPrivateOrder order,
		CancellationToken cancellationToken)
	{
		if (order?.Symbol.IsEmpty() != false || order.OrderId <= 0)
			return;
		using (_sync.EnterScope())
			_spotOrderSymbols.Add(order.Symbol);
		var volume = order.Quantity.ToDecimal();
		var executed = order.ExecutedQuantity.ToDecimal() ?? 0m;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToStockSharp(BitrueSections.Spot),
			ServerTime = (order.EventTime > 0 ? order.EventTime : order.CreationTime) > 0
				? (order.EventTime > 0 ? order.EventTime : order.CreationTime).ToUtcTime()
				: CurrentTime,
			PortfolioName = GetPortfolioName(BitrueSections.Spot),
			Side = order.Side.ToStockSharp(),
			OrderVolume = volume,
			Balance = volume is null ? null : (volume.Value - executed).Max(0m),
			OrderPrice = order.OrderType == BitrueSpotWsOrderTypes.Market
				? 0m
				: order.Price.ToDecimal() ?? 0m,
			OrderType = order.OrderType.ToStockSharp(),
			OrderState = order.Status.ToStockSharp(),
			OrderStringId = order.OrderId.ToString(CultureInfo.InvariantCulture),
			TransactionId = ParseTransactionId(order.ClientOrderId),
			OriginalTransactionId = _orderStatusSubscriptionId,
		}, cancellationToken);

		if (order.TradeId < 0 || order.LastQuantity.ToDecimal() is not > 0m)
			return;
		await SendSpotPrivateFillAsync(order, cancellationToken);
	}

	private async ValueTask OnSpotPrivateBalanceAsync(
		BitrueSpotPrivateBalanceEnvelope envelope, CancellationToken cancellationToken)
	{
		foreach (var balance in envelope?.Balances ?? [])
			await SendSpotBalanceAsync(balance.Asset, balance.Free.ToDecimal(),
				balance.Locked.ToDecimal(), envelope.EventTime, _portfolioSubscriptionId,
				cancellationToken);
	}

	private async ValueTask OnFuturesPrivateOrderAsync(
		BitrueFuturesPrivateOrderEnvelope envelope, CancellationToken cancellationToken)
	{
		var order = envelope?.Order;
		var symbol = ResolveFuturesPrivateSymbol(order?.Symbol);
		if (order is null || symbol.IsEmpty() || order.OrderId <= 0)
			return;
		using (_sync.EnterScope())
			_futuresOrderSymbols.Add(symbol);
		var volume = order.Quantity.ToDecimal();
		var executed = order.ExecutedQuantity.ToDecimal() ?? 0m;
		var condition = new BitrueOrderCondition
		{
			IsReduceOnly = order.IsReduceOnly,
		};
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(BitrueSections.Futures),
			ServerTime = (order.TradeTime > 0 ? order.TradeTime : envelope.EventTime) > 0
				? (order.TradeTime > 0 ? order.TradeTime : envelope.EventTime).ToUtcTime()
				: CurrentTime,
			PortfolioName = GetPortfolioName(BitrueSections.Futures),
			Side = order.Side.ToStockSharp(),
			OrderVolume = volume,
			Balance = volume is null ? null : (volume.Value - executed).Max(0m),
			OrderPrice = order.OrderType == BitrueOrderTypes.Market
				? 0m
				: order.Price.ToDecimal() ?? 0m,
			AveragePrice = order.AveragePrice.ToDecimal(),
			OrderType = order.OrderType.ToStockSharp(),
			OrderState = order.Status.ToStockSharp(),
			OrderStringId = order.OrderId.ToString(CultureInfo.InvariantCulture),
			TransactionId = ParseTransactionId(order.ClientOrderId),
			OriginalTransactionId = _orderStatusSubscriptionId,
			TimeInForce = order.OrderType == BitrueOrderTypes.ImmediateOrCancel
				? TimeInForce.CancelBalance
				: order.OrderType == BitrueOrderTypes.FillOrKill
					? TimeInForce.MatchOrCancel
					: null,
			PostOnly = order.OrderType == BitrueOrderTypes.PostOnly,
			PositionEffect = order.IsReduceOnly ? OrderPositionEffects.CloseOnly : null,
			Commission = order.Commission.ToDecimal(),
			CommissionCurrency = order.CommissionAsset,
			Condition = condition,
		}, cancellationToken);

		if (order.TradeId < 0 || order.LastQuantity.ToDecimal() is not > 0m)
			return;
		await SendFuturesPrivateFillAsync(symbol, order, envelope.EventTime,
			cancellationToken);
	}

	private async ValueTask OnFuturesPrivateAccountAsync(
		BitrueFuturesPrivateAccountEnvelope envelope, CancellationToken cancellationToken)
	{
		foreach (var balance in envelope?.Account?.Balances ?? [])
		{
			var current = (balance.CrossWallet.ToDecimal() ?? 0m) +
				(balance.IsolatedWallet.ToDecimal() ?? 0m);
			await SendFuturesBalanceAsync(balance.Asset, current,
				balance.Locked.ToDecimal() ?? 0m, 0m, 0m, envelope.EventTime,
				_portfolioSubscriptionId, cancellationToken);
		}
		foreach (var position in envelope?.Account?.Positions ?? [])
			await SendFuturesPrivatePositionAsync(position, envelope.EventTime,
				cancellationToken);
	}

	private ValueTask SendSpotBalanceAsync(string asset, decimal? free, decimal? locked,
		long timestamp, long originalTransactionId, CancellationToken cancellationToken)
	{
		if (asset.IsEmpty())
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(BitrueSections.Spot),
			SecurityId = asset.ToStockSharp(BitrueSections.Spot),
			ServerTime = timestamp > 0 ? timestamp.ToUtcTime() : CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, (free ?? 0m) + (locked ?? 0m), true)
		.TryAdd(PositionChangeTypes.BlockedValue, locked, true), cancellationToken);
	}

	private ValueTask SendFuturesBalanceAsync(string asset, decimal current, decimal locked,
		decimal realizedProfit, decimal unrealizedProfit, long timestamp,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (asset.IsEmpty())
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(BitrueSections.Futures),
			SecurityId = asset.ToStockSharp(BitrueSections.Futures),
			ServerTime = timestamp > 0 ? timestamp.ToUtcTime() : CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, current, true)
		.TryAdd(PositionChangeTypes.BlockedValue, locked, true)
		.TryAdd(PositionChangeTypes.RealizedPnL, realizedProfit, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, unrealizedProfit, true),
			cancellationToken);
	}

	private ValueTask SendFuturesPositionAsync(string symbol, BitrueFuturesPosition position,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (symbol.IsEmpty() || position is null)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(BitrueSections.Futures),
			SecurityId = symbol.ToStockSharp(BitrueSections.Futures),
			DepoName = position.PositionId > 0
				? position.PositionId.ToString(CultureInfo.InvariantCulture)
				: null,
			ServerTime = CurrentTime,
			OriginalTransactionId = originalTransactionId,
			Side = position.Side.ToStockSharp(),
		}
		.TryAdd(PositionChangeTypes.CurrentValue, position.Volume.Abs(), true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice, true)
		.TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedProfit, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedProfit, true)
		.TryAdd(PositionChangeTypes.LiquidationPrice, position.LiquidationPrice, true)
		.TryAdd(PositionChangeTypes.Leverage, position.Leverage, true), cancellationToken);
	}

	private ValueTask SendFuturesPrivatePositionAsync(BitrueFuturesPrivatePosition position,
		long timestamp, CancellationToken cancellationToken)
	{
		var symbol = ResolveFuturesPrivateSymbol(position?.Symbol);
		if (position is null || symbol.IsEmpty())
			return default;
		var quantity = position.Quantity.ToDecimal() ?? 0m;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(BitrueSections.Futures),
			SecurityId = symbol.ToStockSharp(BitrueSections.Futures),
			ServerTime = timestamp > 0 ? timestamp.ToUtcTime() : CurrentTime,
			OriginalTransactionId = _portfolioSubscriptionId,
			Side = position.PositionSide == BitrueFuturesPositionSides.Short || quantity < 0
				? Sides.Sell
				: Sides.Buy,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, quantity.Abs(), true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.EntryPrice.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.CurrentPrice, position.LastPrice.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.RealizedPnL, position.RealizedProfit.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedProfit.ToDecimal(), true),
			cancellationToken);
	}

	private ValueTask SendSpotOrderAsync(BitrueSpotOrder order, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var volume = order.OriginalQuantity.ToDecimal();
		var executed = order.ExecutedQuantity.ToDecimal() ?? 0m;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToStockSharp(BitrueSections.Spot),
			ServerTime = GetSpotOrderTime(order),
			PortfolioName = GetPortfolioName(BitrueSections.Spot),
			Side = order.Side.ToStockSharp(),
			OrderVolume = volume,
			Balance = volume is null ? null : (volume.Value - executed).Max(0m),
			OrderPrice = order.OrderType == BitrueOrderTypes.Market
				? 0m
				: order.Price.ToDecimal() ?? 0m,
			OrderType = order.OrderType.ToStockSharp(),
			OrderState = order.Status.ToStockSharp(),
			OrderStringId = order.OrderId.ToString(CultureInfo.InvariantCulture),
			TransactionId = ParseTransactionId(order.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
			TimeInForce = order.TimeInForce == BitrueTimeInForces.ImmediateOrCancel
				? TimeInForce.CancelBalance
				: order.TimeInForce == BitrueTimeInForces.FillOrKill
					? TimeInForce.MatchOrCancel
					: null,
		}, cancellationToken);
	}

	private ValueTask SendFuturesOrderAsync(string fallbackSymbol, BitrueFuturesOrder order,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		var symbol = order.ContractName.IsEmpty(fallbackSymbol);
		var condition = new BitrueOrderCondition
		{
			IsReduceOnly = order.Action == BitrueFuturesActions.Close,
		};
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = symbol.ToStockSharp(BitrueSections.Futures),
			ServerTime = GetFuturesOrderTime(order),
			PortfolioName = GetPortfolioName(BitrueSections.Futures),
			Side = order.Side.ToStockSharp(),
			OrderVolume = order.OriginalQuantity,
			Balance = (order.OriginalQuantity - order.ExecutedQuantity).Max(0m),
			OrderPrice = order.OrderType == BitrueOrderTypes.Market ? 0m : order.Price,
			AveragePrice = order.AveragePrice > 0 ? order.AveragePrice : null,
			OrderType = order.OrderType.ToStockSharp(),
			OrderState = order.Status.ToStockSharp(),
			OrderStringId = order.OrderId.ToString(CultureInfo.InvariantCulture),
			TransactionId = ParseTransactionId(order.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
			TimeInForce = order.OrderType == BitrueOrderTypes.ImmediateOrCancel
				? TimeInForce.CancelBalance
				: order.OrderType == BitrueOrderTypes.FillOrKill
					? TimeInForce.MatchOrCancel
					: null,
			PostOnly = order.OrderType == BitrueOrderTypes.PostOnly,
			PositionEffect = condition.IsReduceOnly ? OrderPositionEffects.CloseOnly : null,
			Condition = condition,
		}, cancellationToken);
	}

	private ValueTask SendSpotFillAsync(BitrueSpotFill fill, long originalTransactionId,
		bool onlyNew, CancellationToken cancellationToken)
	{
		if (fill?.Symbol.IsEmpty() != false || fill.TradeId < 0)
			return default;
		var fillId = "S:" + fill.TradeId.ToString(CultureInfo.InvariantCulture);
		if (!RememberFill(fillId, onlyNew))
			return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = fill.Symbol.ToStockSharp(BitrueSections.Spot),
			ServerTime = fill.Timestamp > 0 ? fill.Timestamp.ToUtcTime() : CurrentTime,
			PortfolioName = GetPortfolioName(BitrueSections.Spot),
			Side = fill.IsBuyer ? Sides.Buy : Sides.Sell,
			OrderStringId = fill.OrderId.ToString(CultureInfo.InvariantCulture),
			TradeId = fill.TradeId,
			TradePrice = fill.Price.ToDecimal(),
			TradeVolume = fill.Quantity.ToDecimal(),
			Commission = fill.Commission.ToDecimal(),
			CommissionCurrency = fill.CommissionAsset,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private ValueTask SendFuturesFillAsync(BitrueFuturesFill fill,
		long originalTransactionId, bool onlyNew, CancellationToken cancellationToken)
	{
		if (fill?.ContractName.IsEmpty() != false || fill.TradeId < 0)
			return default;
		var fillId = "F:" + fill.TradeId.ToString(CultureInfo.InvariantCulture);
		if (!RememberFill(fillId, onlyNew))
			return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = fill.ContractName.ToStockSharp(BitrueSections.Futures),
			ServerTime = fill.Timestamp > 0 ? fill.Timestamp.ToUtcTime() : CurrentTime,
			PortfolioName = GetPortfolioName(BitrueSections.Futures),
			Side = fill.Side.ToStockSharp(),
			OrderStringId = fill.GetOrderId().ToString(CultureInfo.InvariantCulture),
			TradeId = fill.TradeId,
			TradePrice = fill.Price,
			TradeVolume = fill.Quantity,
			Commission = fill.Fee,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private ValueTask SendSpotPrivateFillAsync(BitrueSpotPrivateOrder order,
		CancellationToken cancellationToken)
	{
		var fillId = "S:" + order.TradeId.ToString(CultureInfo.InvariantCulture);
		if (!RememberFill(fillId, true))
			return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = order.Symbol.ToStockSharp(BitrueSections.Spot),
			ServerTime = order.TradeTime > 0 ? order.TradeTime.ToUtcTime() : CurrentTime,
			PortfolioName = GetPortfolioName(BitrueSections.Spot),
			Side = order.Side.ToStockSharp(),
			OrderStringId = order.OrderId.ToString(CultureInfo.InvariantCulture),
			TradeId = order.TradeId,
			TradePrice = order.LastPrice.ToDecimal(),
			TradeVolume = order.LastQuantity.ToDecimal(),
			Commission = order.Commission.ToDecimal(),
			CommissionCurrency = order.CommissionAsset,
			OriginalTransactionId = _orderStatusSubscriptionId,
		}, cancellationToken);
	}

	private ValueTask SendFuturesPrivateFillAsync(string symbol,
		BitrueFuturesPrivateOrder order, long fallbackTimestamp,
		CancellationToken cancellationToken)
	{
		var fillId = "F:" + order.TradeId.ToString(CultureInfo.InvariantCulture);
		if (!RememberFill(fillId, true))
			return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = symbol.ToStockSharp(BitrueSections.Futures),
			ServerTime = (order.TradeTime > 0 ? order.TradeTime : fallbackTimestamp) > 0
				? (order.TradeTime > 0 ? order.TradeTime : fallbackTimestamp).ToUtcTime()
				: CurrentTime,
			PortfolioName = GetPortfolioName(BitrueSections.Futures),
			Side = order.Side.ToStockSharp(),
			OrderStringId = order.OrderId.ToString(CultureInfo.InvariantCulture),
			TradeId = order.TradeId,
			TradePrice = order.LastPrice.ToDecimal(),
			TradeVolume = order.LastQuantity.ToDecimal(),
			Commission = order.Commission.ToDecimal(),
			CommissionCurrency = order.CommissionAsset,
			OriginalTransactionId = _orderStatusSubscriptionId,
		}, cancellationToken);
	}

	private bool RememberFill(string fillId, bool onlyNew)
	{
		using (_sync.EnterScope())
		{
			var added = _seenFillIds.Add(fillId);
			return !onlyNew || added;
		}
	}

	private string ResolveFuturesPrivateSymbol(string symbol)
	{
		if (symbol.IsEmpty())
			return null;
		using (_sync.EnterScope())
		{
			if (_futuresContracts.ContainsKey(symbol))
				return symbol;
			_futuresPrivateSymbols.TryGetValue(symbol.ToPrivateWsSymbol(), out var result);
			return result;
		}
	}

	private static DateTime GetSpotOrderTime(BitrueSpotOrder order)
		=> (order.UpdateTime > 0 ? order.UpdateTime : order.CreationTime) > 0
			? (order.UpdateTime > 0 ? order.UpdateTime : order.CreationTime).ToUtcTime()
			: DateTime.UtcNow;

	private static DateTime GetFuturesOrderTime(BitrueFuturesOrder order)
		=> order.TransactionTime > 0 ? order.TransactionTime.ToUtcTime() : DateTime.UtcNow;

	private static bool IsWithin(DateTime value, DateTime? from, DateTime? to)
		=> (from is null || value >= from) && (to is null || value <= to);
}
