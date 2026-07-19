namespace StockSharp.BTSE;

public partial class BTSEMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		var section = ResolveSection(regMsg.SecurityId);
		EnsurePrivateReady(section);
		var symbol = GetSymbol(regMsg.SecurityId, section);
		var volume = regMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException("Order volume must be positive.");
		if (section == BTSESections.Futures && volume != decimal.Truncate(volume))
			throw new InvalidOperationException(
				"BTSE futures order volume must be an integer number of contracts.");

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		if (orderType == OrderTypes.Limit && regMsg.Price <= 0)
			throw new InvalidOperationException("A positive limit price is required.");
		if (orderType == OrderTypes.Market && regMsg.PostOnly == true)
			throw new InvalidOperationException("A market order cannot be post-only.");

		var condition = regMsg.Condition as BTSEOrderCondition ?? new BTSEOrderCondition();
		var isReduceOnly = condition.IsReduceOnly ||
			regMsg.PositionEffect == OrderPositionEffects.CloseOnly;
		if (section == BTSESections.Spot && isReduceOnly)
			throw new InvalidOperationException(
				"Reduce-only orders require a BTSE futures market.");
		if (condition.TriggerPrice is <= 0)
			throw new InvalidOperationException("Trigger price must be positive.");

		var result = (await GetRestClient(section).PlaceOrderAsync(new()
		{
			Symbol = symbol,
			Size = volume,
			Price = orderType == OrderTypes.Limit ? regMsg.Price : null,
			Side = regMsg.Side.ToBTSE(),
			Type = orderType == OrderTypes.Market
				? BTSEOrderTypes.Market
				: BTSEOrderTypes.Limit,
			TransactionType = condition.TriggerPrice is not null
				? BTSETransactionTypes.Stop
				: null,
			TriggerPrice = condition.TriggerPrice,
			TimeInForce = orderType == OrderTypes.Limit
				? regMsg.TimeInForce.ToBTSE()
				: null,
			IsPostOnly = orderType == OrderTypes.Limit ? regMsg.PostOnly : null,
			IsReduceOnly = section == BTSESections.Futures ? isReduceOnly : null,
			ClientOrderId = BTSEExtensions.CreateClientOrderId(regMsg.TransactionId,
				regMsg.UserOrderId),
		}, cancellationToken))?.FirstOrDefault();
		ValidateOrderResult(result, "accepted");

		await SendOrderAsync(section, result, regMsg.TransactionId, regMsg.TransactionId,
			condition, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		var section = ResolveSection(replaceMsg.SecurityId);
		EnsurePrivateReady(section);
		if (replaceMsg.OrderType == OrderTypes.Market)
			throw new NotSupportedException("BTSE can amend active priced orders only.");
		if (replaceMsg.Price <= 0)
			throw new InvalidOperationException("Replacement price must be positive.");
		var volume = replaceMsg.Volume.Abs();
		if (volume <= 0)
			throw new InvalidOperationException("Replacement volume must be positive.");
		if (section == BTSESections.Futures && volume != decimal.Truncate(volume))
			throw new InvalidOperationException(
				"BTSE futures order volume must be an integer number of contracts.");

		var symbol = GetSymbol(replaceMsg.SecurityId, section);
		var orderId = ResolveOrderId(replaceMsg.OldOrderId,
			replaceMsg.OldOrderStringId, "replacement");
		var condition = replaceMsg.Condition as BTSEOrderCondition;
		var result = (await GetRestClient(section).AmendOrderAsync(new()
		{
			Symbol = symbol,
			OrderId = orderId,
			OrderPrice = replaceMsg.Price,
			OrderSize = volume,
			TriggerPrice = condition?.TriggerPrice,
		}, cancellationToken))?.FirstOrDefault();
		ValidateOrderResult(result, "amended");

		await SendOrderAsync(section, result, replaceMsg.TransactionId,
			replaceMsg.TransactionId, condition, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		var section = ResolveSection(cancelMsg.SecurityId);
		EnsurePrivateReady(section);
		var symbol = GetSymbol(cancelMsg.SecurityId, section);
		var orderId = ResolveOrderId(cancelMsg.OrderId, cancelMsg.OrderStringId,
			"cancellation");
		var result = (await GetRestClient(section).CancelOrderAsync(new()
		{
			Symbol = symbol,
			OrderId = orderId,
		}, cancellationToken))?.FirstOrDefault();
		if (result is null)
		{
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
			return;
		}
		ValidateOrderResult(result, "cancelled", true);
		await SendOrderAsync(section, result, cancelMsg.TransactionId, null, null,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException(
				"BTSE bulk cancellation does not close futures positions.");

		BTSESections? requestedSection = cancelMsg.SecurityId.BoardCode.IsEmpty()
			? null
			: cancelMsg.SecurityId.BoardCode.ToSection();
		var hasSymbol = !cancelMsg.SecurityId.SecurityCode.IsEmpty();
		if (hasSymbol && requestedSection is null)
			requestedSection = ResolveSection(cancelMsg.SecurityId);

		foreach (var section in Sections.Where(section =>
			requestedSection is null || requestedSection == section))
		{
			EnsurePrivateReady(section);
			var symbol = hasSymbol ? GetSymbol(cancelMsg.SecurityId, section) : null;
			var orders = await GetRestClient(section).GetOpenOrdersAsync(new()
			{
				Symbol = symbol,
			}, cancellationToken) ?? [];
			var matching = orders.Where(order => order?.OrderId.IsEmpty() == false &&
				(cancelMsg.Side is null || order.Side.ToStockSharpSide() == cancelMsg.Side))
				.ToArray();

			if (cancelMsg.Side is null)
			{
				var symbols = symbol.IsEmpty()
					? matching.Select(static order => order.Symbol)
						.Where(static market => !market.IsEmpty())
						.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
					: [symbol];
				foreach (var market in symbols)
					await GetRestClient(section).CancelOrderAsync(new()
					{
						Symbol = market,
					}, cancellationToken);
			}
			else
			{
				foreach (var order in matching)
					await GetRestClient(section).CancelOrderAsync(new()
					{
						Symbol = order.Symbol,
						OrderId = order.OrderId,
					}, cancellationToken);
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
		EnsurePrivateReady();

		foreach (var section in Sections)
			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = GetPortfolioName(section),
				BoardCode = section.ToBoardCode(),
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
		try
		{
			await RefreshPrivateSubscriptionsAsync(cancellationToken);
		}
		catch
		{
			_portfolioSubscriptionId = 0;
			throw;
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
		EnsurePrivateReady();
		if ((!statusMsg.OrderStringId.IsEmpty() || statusMsg.OrderId is not null) &&
			statusMsg.SecurityId.BoardCode.IsEmpty() && Sections.Distinct().Count() > 1)
			throw new InvalidOperationException(
				"SecurityId.BoardCode is required when querying one BTSE order with both sections enabled.");

		BTSESections? requestedSection = statusMsg.SecurityId.BoardCode.IsEmpty()
			? null
			: statusMsg.SecurityId.BoardCode.ToSection();
		var symbol = statusMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetSymbol(statusMsg.SecurityId,
				requestedSection ?? ResolveSection(statusMsg.SecurityId));
		var orderId = statusMsg.OrderStringId;
		if (orderId.IsEmpty() && statusMsg.OrderId is long numericOrderId)
			orderId = numericOrderId.ToString(CultureInfo.InvariantCulture);
		var maximum = (statusMsg.Count ?? 1000).Min(10000).Max(1).To<int>();
		await SendOrderSnapshotAsync(statusMsg.TransactionId, symbol, statusMsg.From,
			statusMsg.To, maximum, cancellationToken, requestedSection, orderId, null);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
			return;
		}

		_orderStatusSubscriptionId = statusMsg.TransactionId;
		try
		{
			await RefreshPrivateSubscriptionsAsync(cancellationToken);
		}
		catch
		{
			_orderStatusSubscriptionId = 0;
			throw;
		}
	}

	private async ValueTask RefreshPrivateSubscriptionsAsync(
		CancellationToken cancellationToken)
	{
		foreach (var section in Sections)
		{
			var client = GetGeneralWsClient(section);
			if (_orderStatusSubscriptionId != 0)
			{
				await client.SubscribeOrdersAsync(cancellationToken);
				await client.SubscribeFillsAsync(cancellationToken);
			}
			else
			{
				await client.UnsubscribeOrdersAsync(cancellationToken);
				await client.UnsubscribeFillsAsync(cancellationToken);
			}

			if (section == BTSESections.Futures)
			{
				if (_portfolioSubscriptionId != 0)
					await client.SubscribePositionsAsync(cancellationToken);
				else
					await client.UnsubscribePositionsAsync(cancellationToken);
			}
		}
	}

	private async ValueTask SendPortfolioSnapshotAsync(long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (_spotRestClient?.IsCredentialsAvailable == true)
			foreach (var balance in await _spotRestClient.GetSpotWalletAsync(
				cancellationToken) ?? [])
				await SendSpotBalanceAsync(balance, originalTransactionId,
					cancellationToken);

		if (_futuresRestClient?.IsCredentialsAvailable != true)
			return;

		var balances = new Dictionary<string, (decimal Available, decimal Blocked)>(
			StringComparer.OrdinalIgnoreCase);
		foreach (var wallet in await _futuresRestClient.GetFuturesWalletAsync(
			cancellationToken) ?? [])
		{
			foreach (var asset in wallet?.Assets ?? [])
			{
				if (asset?.Currency.IsEmpty() != false)
					continue;
				balances.TryGetValue(asset.Currency, out var value);
				balances[asset.Currency] = (value.Available + asset.Balance, value.Blocked);
			}
			foreach (var asset in wallet?.AssetsInUse ?? [])
			{
				if (asset?.Currency.IsEmpty() != false)
					continue;
				balances.TryGetValue(asset.Currency, out var value);
				balances[asset.Currency] = (value.Available, value.Blocked + asset.Balance);
			}
		}
		foreach (var balance in balances)
			await SendFuturesBalanceAsync(balance.Key, balance.Value.Available,
				balance.Value.Blocked, originalTransactionId, cancellationToken);

		foreach (var position in await _futuresRestClient.GetPositionsAsync(new(),
			cancellationToken) ?? [])
			await SendPositionAsync(position, originalTransactionId, cancellationToken);
	}

	private async ValueTask SendOrderSnapshotAsync(long originalTransactionId, string symbol,
		DateTime? from, DateTime? to, int maximum, CancellationToken cancellationToken,
		BTSESections? requestedSection = null, string orderId = null,
		string clientOrderId = null)
	{
		foreach (var section in Sections.Where(section =>
			requestedSection is null || requestedSection == section))
		{
			var restClient = GetRestClient(section);
			if (!orderId.IsEmpty() || !clientOrderId.IsEmpty())
			{
				var order = await restClient.GetOrderAsync(new()
				{
					Symbol = symbol,
					OrderId = orderId,
					ClientOrderId = clientOrderId,
				}, cancellationToken);
				if (order is not null)
					await SendOrderAsync(section, order, originalTransactionId, null, null,
						cancellationToken);
			}
			else
			{
				var orders = await restClient.GetOpenOrdersAsync(new()
				{
					Symbol = symbol,
				}, cancellationToken);
				foreach (var order in (orders ?? [])
					.Where(static order => order?.OrderId.IsEmpty() == false)
					.OrderBy(static order => order.Timestamp)
					.TakeLast(maximum))
					await SendOrderAsync(section, order, originalTransactionId, null, null,
						cancellationToken);
			}

			var trades = await restClient.GetTradeHistoryAsync(new()
			{
				Symbol = symbol,
				StartTime = from?.ToUniversalTime().ToMilliseconds(),
				EndTime = to?.ToUniversalTime().ToMilliseconds(),
				Count = maximum.Min(500),
				OrderId = orderId,
				ClientOrderId = clientOrderId,
				IsIncludeOld = section == BTSESections.Futures && from is DateTime fromTime
					? fromTime.ToUniversalTime() < DateTime.UtcNow.AddDays(-7)
					: section == BTSESections.Futures ? false : null,
			}, cancellationToken);
			foreach (var trade in (trades ?? [])
				.Where(trade => trade.Timestamp > 0 &&
					(from is null || trade.Timestamp.FromMilliseconds() >=
						from.Value.ToUniversalTime()) &&
					(to is null || trade.Timestamp.FromMilliseconds() <=
						to.Value.ToUniversalTime()))
				.OrderBy(static trade => trade.Timestamp)
				.TakeLast(maximum))
				await SendFillAsync(section, trade, originalTransactionId, false,
					cancellationToken);
		}
	}

	private async ValueTask OnOrderUpdateAsync(BTSESections section, BTSEWsOrder order,
		CancellationToken cancellationToken)
	{
		if (_orderStatusSubscriptionId == 0)
			return;
		await SendOrderAsync(section, order, _orderStatusSubscriptionId,
			cancellationToken);
	}

	private ValueTask OnFillUpdateAsync(BTSESections section, BTSEWsFill fill,
		CancellationToken cancellationToken)
		=> _orderStatusSubscriptionId == 0
			? default
			: SendFillAsync(section, fill, _orderStatusSubscriptionId,
				cancellationToken);

	private ValueTask OnPositionUpdateAsync(BTSESections section,
		BTSEWsPosition position, CancellationToken cancellationToken)
		=> section != BTSESections.Futures || _portfolioSubscriptionId == 0
			? default
			: SendPositionAsync(position, _portfolioSubscriptionId, cancellationToken);

	private ValueTask SendSpotBalanceAsync(BTSESpotBalance balance,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (balance?.Currency.IsEmpty() != false)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(BTSESections.Spot),
			SecurityId = balance.Currency.ToStockSharp(BTSESections.Spot),
			ServerTime = CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, balance.Total, true)
		.TryAdd(PositionChangeTypes.BlockedValue,
			(balance.Total - balance.Available).Max(0m), true), cancellationToken);
	}

	private ValueTask SendFuturesBalanceAsync(string currency, decimal available,
		decimal blocked, long originalTransactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(BTSESections.Futures),
			SecurityId = currency.ToStockSharp(BTSESections.Futures),
			ServerTime = CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, available + blocked, true)
		.TryAdd(PositionChangeTypes.BlockedValue, blocked, true), cancellationToken);

	private ValueTask SendPositionAsync(BTSEFuturesPosition position,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (position?.Symbol.IsEmpty() != false)
			return default;
		var side = (position.PositionDirection.IsEmpty(position.Side)).ToStockSharpSide();
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(BTSESections.Futures),
			SecurityId = position.Symbol.ToStockSharp(BTSESections.Futures),
			DepoName = position.PositionId,
			ServerTime = position.Timestamp > 0
				? position.Timestamp.FromMilliseconds()
				: CurrentTime,
			OriginalTransactionId = originalTransactionId,
			Side = position.Size == 0 ? null : side,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, position.Size.Abs(), true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.EntryPrice, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, position.MarkPrice, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnL, true)
		.TryAdd(PositionChangeTypes.LiquidationPrice, position.LiquidationPrice, true)
		.TryAdd(PositionChangeTypes.Leverage,
			position.IsolatedLeverage ?? position.CurrentLeverage, true), cancellationToken);
	}

	private ValueTask SendPositionAsync(BTSEWsPosition position,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		var symbol = ResolvePositionSymbol(position?.MarketName);
		if (symbol.IsEmpty())
			return default;
		var side = (position.PositionDirection.IsEmpty(position.OrderModeName))
			.ToStockSharpSide();
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = GetPortfolioName(BTSESections.Futures),
			SecurityId = symbol.ToStockSharp(BTSESections.Futures),
			DepoName = position.PositionId,
			ServerTime = CurrentTime,
			OriginalTransactionId = originalTransactionId,
			Side = position.TotalContracts == 0 ? null : side,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, position.TotalContracts.Abs(), true)
		.TryAdd(PositionChangeTypes.AveragePrice,
			position.EntryPrice != 0 ? position.EntryPrice : position.AverageFilledPrice, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, position.MarkPrice, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnL, true)
		.TryAdd(PositionChangeTypes.LiquidationPrice, position.LiquidationPrice, true)
		.TryAdd(PositionChangeTypes.Leverage, position.CurrentLeverage, true),
			cancellationToken);
	}

	private ValueTask SendOrderAsync(BTSESections section, BTSEOrderResult order,
		long originalTransactionId, long? transactionId, BTSEOrderCondition condition,
		CancellationToken cancellationToken)
	{
		if (order?.OrderId.IsEmpty() != false || order.Symbol.IsEmpty())
			return default;
		var volume = GetOrderVolume(section, order.OriginalOrderBaseSize,
			order.OriginalOrderQuoteSize, order.OriginalOrderSize,
			order.CurrentOrderBaseSize, order.CurrentOrderQuoteSize,
			order.CurrentOrderSize);
		var balance = GetOrderVolume(section, order.RemainingOrderBaseSize,
			order.RemainingOrderQuoteSize, order.RemainingSize, null, null, null);
		var state = order.Status.ToStockSharpOrderState(order.OrderState);
		condition ??= new BTSEOrderCondition { TriggerPrice = order.TriggerPrice };
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToStockSharp(section),
			ServerTime = order.Timestamp > 0
				? order.Timestamp.FromMilliseconds()
				: CurrentTime,
			PortfolioName = GetPortfolioName(section),
			Side = order.Side.ToStockSharpSide(),
			OrderVolume = volume,
			Balance = state == OrderStates.Done ? 0m : balance,
			OrderPrice = order.Price ?? 0m,
			AveragePrice = order.AverageFilledPrice,
			OrderType = order.OrderType.ToStockSharpOrderType(),
			OrderState = state,
			OrderStringId = order.OrderId,
			TransactionId = transactionId ??
				BTSEExtensions.ParseTransactionId(order.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
			TimeInForce = (order.TimeInForceSnakeCase ?? order.TimeInForceCamelCase)
				.ToStockSharpTimeInForce(),
			PostOnly = order.IsPostOnly,
			Condition = condition,
			Error = state == OrderStates.Failed
				? new InvalidOperationException(order.Message ??
					$"BTSE order failed with status {order.Status}.")
				: null,
		}, cancellationToken);
	}

	private ValueTask SendOrderAsync(BTSESections section, BTSEWsOrder order,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (order?.OrderId.IsEmpty() != false || order.Symbol.IsEmpty())
			return default;
		var volume = GetOrderVolume(section, order.OriginalOrderBaseSize,
			order.OriginalOrderQuoteSize, order.OriginalOrderSize,
			order.CurrentOrderBaseSize, order.CurrentOrderQuoteSize,
			order.CurrentOrderSize);
		var balance = GetOrderVolume(section,
			order.RemainingOrderBaseSize ?? order.RemainingBaseSize,
			order.RemainingOrderQuoteSize ?? order.RemainingQuoteSize,
			order.RemainingSize, null, null, null);
		var state = order.Status.ToStockSharpOrderState();
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToStockSharp(section),
			ServerTime = order.Timestamp > 0
				? order.Timestamp.FromMilliseconds()
				: CurrentTime,
			PortfolioName = GetPortfolioName(section),
			Side = order.Side.ToStockSharpSide(),
			OrderVolume = volume,
			Balance = state == OrderStates.Done ? 0m : balance,
			OrderPrice = order.Price ?? 0m,
			AveragePrice = order.AverageFilledPrice,
			OrderType = (order.OrderType != 0 ? order.OrderType : order.Type ?? 76)
				.ToStockSharpOrderType(),
			OrderState = state,
			OrderStringId = order.OrderId,
			TransactionId = BTSEExtensions.ParseTransactionId(order.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
			TimeInForce = order.TimeInForce.ToStockSharpTimeInForce(),
			PostOnly = order.IsPostOnly,
			Condition = new BTSEOrderCondition { TriggerPrice = order.TriggerPrice },
			Error = state == OrderStates.Failed
				? new InvalidOperationException(
					$"BTSE order failed with status {order.Status}.")
				: null,
		}, cancellationToken);
	}

	private ValueTask SendFillAsync(BTSESections section, BTSEPrivateTrade fill,
		long originalTransactionId, bool onlyNew,
		CancellationToken cancellationToken)
	{
		if (fill?.Symbol.IsEmpty() != false || fill.FilledSize <= 0)
			return default;
		var fillId = $"{section}:{fill.TradeId}:{fill.SerialId}";
		using (_sync.EnterScope())
		{
			var added = _seenFillIds.Add(fillId);
			if (onlyNew && !added)
				return default;
		}
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = fill.Symbol.ToStockSharp(section),
			ServerTime = fill.Timestamp > 0
				? fill.Timestamp.FromMilliseconds()
				: CurrentTime,
			PortfolioName = GetPortfolioName(section),
			Side = fill.Side.ToStockSharpSide(),
			OrderStringId = fill.OrderId,
			TradeId = fill.SerialId > 0 ? fill.SerialId : null,
			TradeStringId = fill.TradeId,
			TradePrice = fill.FilledPrice,
			TradeVolume = fill.FilledSize,
			Commission = fill.FeeAmount,
			CommissionCurrency = fill.FeeCurrency,
			TransactionId = BTSEExtensions.ParseTransactionId(fill.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private ValueTask SendFillAsync(BTSESections section, BTSEWsFill fill,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (fill?.Symbol.IsEmpty() != false || fill.Size <= 0)
			return default;
		var fillId = $"{section}:{fill.TradeId}:{fill.SerialId}";
		using (_sync.EnterScope())
			if (!_seenFillIds.Add(fillId))
				return default;
		long? serialId = long.TryParse(fill.SerialId, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var numericSerialId)
			? numericSerialId
			: null;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = fill.Symbol.ToStockSharp(section),
			ServerTime = fill.Timestamp > 0
				? fill.Timestamp.FromMilliseconds()
				: CurrentTime,
			PortfolioName = GetPortfolioName(section),
			Side = fill.Side.ToStockSharpSide(),
			OrderStringId = fill.OrderId,
			TradeId = serialId,
			TradeStringId = fill.TradeId,
			TradePrice = fill.Price,
			TradeVolume = fill.Size,
			Commission = fill.FeeAmount,
			CommissionCurrency = fill.FeeCurrency,
			IsMarketMaker = fill.IsMaker,
			TransactionId = BTSEExtensions.ParseTransactionId(fill.ClientOrderId),
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private static decimal? GetOrderVolume(BTSESections section,
		decimal? primaryBase, decimal? primaryQuote, decimal? futures,
		decimal? fallbackBase, decimal? fallbackQuote, decimal? fallbackFutures)
		=> section == BTSESections.Futures
			? futures ?? fallbackFutures
			: primaryBase ?? fallbackBase ?? primaryQuote ?? fallbackQuote;

	private static void ValidateOrderResult(BTSEOrderResult result, string operation,
		bool allowDone = false)
	{
		if (result?.OrderId.IsEmpty() != false)
			throw new InvalidDataException(
				$"BTSE {operation} the order without returning an order ID.");
		var state = result.Status.ToStockSharpOrderState(result.OrderState);
		if (state == OrderStates.Failed || !allowDone && state == OrderStates.Done &&
			result.Status is not 4)
			throw new InvalidOperationException(result.Message ??
				$"BTSE order operation failed with status {result.Status}.");
	}

	private static string ResolveOrderId(long? numericOrderId, string stringOrderId,
		string operation)
	{
		if (!stringOrderId.IsEmpty())
			return stringOrderId;
		if (numericOrderId is > 0)
			return numericOrderId.Value.ToString(CultureInfo.InvariantCulture);
		throw new InvalidOperationException(
			$"BTSE {operation} requires an exchange order ID.");
	}
}
