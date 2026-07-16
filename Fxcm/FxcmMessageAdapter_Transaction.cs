namespace StockSharp.Fxcm;

public partial class FxcmMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		if (regMsg.Volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Volume), regMsg.Volume,
				"FXCM order amount must be positive.");
		if (regMsg.PortfolioName.IsEmpty())
			throw new InvalidOperationException("FXCM account id is required as PortfolioName.");

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Market or OrderTypes.Limit or OrderTypes.Conditional))
			throw new NotSupportedException($"FXCM does not support StockSharp order type '{orderType}'.");
		if (orderType != OrderTypes.Market && regMsg.Price <= 0)
			throw new InvalidOperationException("FXCM entry orders require a positive rate.");

		var condition = regMsg.Condition as FxcmOrderCondition ?? new();
		if (condition.TrailingStep is < 0)
			throw new InvalidOperationException("FXCM trailing step cannot be negative.");
		var timeInForce = regMsg.TimeInForce.ToNative(regMsg.TillDate);
		FxcmOrderResult result;

		if (orderType == OrderTypes.Market)
		{
			result = await GetRest().OpenTrade(new()
			{
				AccountId = regMsg.PortfolioName,
				Symbol = regMsg.SecurityId.SecurityCode.ThrowIfEmpty(nameof(regMsg.SecurityId.SecurityCode)),
				IsBuy = regMsg.Side == Sides.Buy,
				Amount = regMsg.Volume,
				Stop = condition.StopLoss,
				TrailingStep = condition.TrailingStep,
				Limit = condition.TakeProfit,
				IsInPips = condition.IsInPips,
				AtMarket = regMsg.Slippage,
				OrderType = regMsg.Slippage == null ? "AtMarket" : "MarketRange",
				TimeInForce = timeInForce,
			}, cancellationToken);
		}
		else
		{
			result = await GetRest().CreateEntryOrder(new()
			{
				AccountId = regMsg.PortfolioName,
				Symbol = regMsg.SecurityId.SecurityCode.ThrowIfEmpty(nameof(regMsg.SecurityId.SecurityCode)),
				IsBuy = regMsg.Side == Sides.Buy,
				Rate = regMsg.Price,
				Amount = regMsg.Volume,
				Stop = condition.StopLoss,
				TrailingStep = condition.TrailingStep,
				Limit = condition.TakeProfit,
				IsInPips = condition.IsInPips,
				Range = regMsg.Slippage,
				OrderType = regMsg.Slippage == null ? "Entry" : "RangeEntry",
				TimeInForce = timeInForce,
				Expiration = timeInForce == "GTD" ? regMsg.TillDate?.ToUniversalTime().ToString("yyyyMMdd",
					CultureInfo.InvariantCulture) : null,
			}, cancellationToken);
		}

		if (result?.OrderId <= 0)
			throw new InvalidOperationException("FXCM order placement returned no order id.");

		_orders[result.OrderId] = new()
		{
			TransactionId = regMsg.TransactionId,
			SecurityId = regMsg.SecurityId,
			PortfolioName = regMsg.PortfolioName,
			Side = regMsg.Side,
			OrderType = orderType,
			Price = regMsg.Price,
			Volume = regMsg.Volume,
			Condition = condition,
		};

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = regMsg.TransactionId,
			TransactionId = regMsg.TransactionId,
			OrderId = result.OrderId,
			OrderStringId = result.OrderId.ToString(CultureInfo.InvariantCulture),
			SecurityId = regMsg.SecurityId,
			PortfolioName = regMsg.PortfolioName,
			Side = regMsg.Side,
			OrderType = orderType,
			OrderPrice = regMsg.Price,
			OrderVolume = regMsg.Volume,
			Balance = regMsg.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = DateTime.UtcNow,
			TimeInForce = regMsg.TimeInForce,
			ExpiryDate = regMsg.TillDate,
			Condition = condition,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		if (replaceMsg.OldOrderId is not > 0)
			throw new InvalidOperationException("FXCM exchange order id is required for replacement.");
		if (replaceMsg.Price <= 0 || replaceMsg.Volume <= 0)
			throw new InvalidOperationException("FXCM replacement rate and amount must be positive.");

		var condition = replaceMsg.Condition as FxcmOrderCondition;
		await GetRest().ChangeOrder(new()
		{
			OrderId = replaceMsg.OldOrderId.Value,
			Rate = replaceMsg.Price,
			Range = replaceMsg.Slippage,
			Amount = replaceMsg.Volume,
			TrailingStep = condition?.TrailingStep,
		}, cancellationToken);

		if (_orders.TryGetValue(replaceMsg.OldOrderId.Value, out var tracker))
		{
			tracker.Price = replaceMsg.Price;
			tracker.Volume = replaceMsg.Volume;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		if (cancelMsg.OrderId is not > 0)
			throw new InvalidOperationException("FXCM exchange order or trade id is required for cancellation.");

		if (_positions.TryGetValue(cancelMsg.OrderId.Value, out var position))
		{
			await GetRest().CloseTrade(new()
			{
				TradeId = cancelMsg.OrderId.Value,
				Amount = position.Amount ?? throw new InvalidOperationException(
					"FXCM open position contains no amount."),
				OrderType = "AtMarket",
				TimeInForce = "FOK",
			}, cancellationToken);
		}
		else
		{
			await GetRest().DeleteOrder(cancelMsg.OrderId.Value, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		if (!statusMsg.IsSubscribe)
		{
			if (_orderStatusSubscriptionId == statusMsg.OriginalTransactionId)
			{
				_orderStatusSubscriptionId = 0;
				await RefreshModelSubscriptions(cancellationToken);
			}
			return;
		}

		foreach (var order in await GetRest().GetOrders(cancellationToken))
			await SendOrder(order, statusMsg.TransactionId, cancellationToken);

		var includePositions = statusMsg.IsHistoryOnly() || statusMsg.From != null || statusMsg.To != null ||
			statusMsg.Count != null;
		if (includePositions)
		{
			var left = statusMsg.Count ?? long.MaxValue;
			foreach (var position in (await GetRest().GetClosedPositions(cancellationToken))
				.OrderBy(p => p.Time.ToDateTime()))
			{
				var time = position.Time.ToDateTime();
				if (statusMsg.From != null && time < statusMsg.From)
					continue;
				if (statusMsg.To != null && time > statusMsg.To)
					continue;
				await SendPositionExecution(position, true, statusMsg.TransactionId, cancellationToken);
				if (--left <= 0)
					break;
			}
		}

		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
			return;
		}

		_orderStatusSubscriptionId = statusMsg.TransactionId;
		await RefreshModelSubscriptions(cancellationToken);
		await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		if (!lookupMsg.IsSubscribe)
		{
			if (_portfolioSubscriptionId == lookupMsg.OriginalTransactionId)
			{
				_portfolioSubscriptionId = 0;
				await RefreshModelSubscriptions(cancellationToken);
			}
			return;
		}

		var accounts = await GetRest().GetAccounts(cancellationToken);
		foreach (var account in accounts)
		{
			var portfolio = GetPortfolioName(account);
			if (!lookupMsg.PortfolioName.IsEmpty() && !portfolio.EqualsIgnoreCase(lookupMsg.PortfolioName) &&
				!account.AccountId.EqualsIgnoreCase(lookupMsg.PortfolioName))
				continue;
			_accounts[account.AccountId.IsEmpty(portfolio)] = account;
			await SendAccount(account, lookupMsg.TransactionId, true, cancellationToken);
		}

		_positions.Clear();
		foreach (var position in await GetRest().GetOpenPositions(cancellationToken))
		{
			if (position.TradeId is > 0)
				_positions[position.TradeId.Value] = position;
		}
		foreach (var group in GetPositionKeys())
			await SendPositionGroup(group.Portfolio, group.Symbol, lookupMsg.TransactionId, cancellationToken);

		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
			return;
		}

		_portfolioSubscriptionId = lookupMsg.TransactionId;
		await RefreshModelSubscriptions(cancellationToken);
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private ValueTask ProcessOrderUpdate(FxcmOrder order, CancellationToken cancellationToken)
	{
		if (order == null || (_orderStatusSubscriptionId == 0 && !_orders.ContainsKey(order.OrderId)))
			return default;
		return SendOrder(order, 0, cancellationToken);
	}

	private async ValueTask SendOrder(FxcmOrder order, long lookupTransactionId,
		CancellationToken cancellationToken)
	{
		if (order == null || order.OrderId <= 0 || order.IsTotal == true)
			return;

		_orders.TryGetValue(order.OrderId, out var tracker);
		var originalId = lookupTransactionId != 0
			? lookupTransactionId
			: tracker?.TransactionId ?? _orderStatusSubscriptionId;
		if (originalId == 0)
			return;

		var state = order.Action.EqualsIgnoreCase("D") ? OrderStates.Done : order.Status.ToOrderState();
		if (state == null)
			state = OrderStates.Pending;
		var isFailed = order.Status is 5 or 8;
		if (isFailed)
			state = OrderStates.Failed;
		var side = tracker?.Side ?? (order.IsBuy == false ? Sides.Sell : Sides.Buy);
		var volume = order.Amount ?? tracker?.Volume ?? 0;
		var price = tracker?.Price ?? (side == Sides.Buy ? order.Buy : order.Sell) ?? 0;
		var timeInForce = order.TimeInForce.ToTimeInForce(order.ExpireDate, out var expiry);
		var condition = tracker?.Condition ?? new FxcmOrderCondition
		{
			StopLoss = order.Stop,
			TakeProfit = order.Limit,
			TrailingStep = order.TrailingStep,
		};

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalId,
			TransactionId = lookupTransactionId == 0 ? tracker?.TransactionId ?? 0 : 0,
			OrderId = order.OrderId,
			OrderStringId = order.OrderId.ToString(CultureInfo.InvariantCulture),
			SecurityId = tracker?.SecurityId ?? order.Symbol.ToSecurityId(),
			PortfolioName = tracker?.PortfolioName ?? order.AccountName.IsEmpty(order.AccountId),
			Side = side,
			OrderType = tracker?.OrderType ?? (order.IsEntryOrder ? OrderTypes.Limit : OrderTypes.Market),
			OrderPrice = price,
			OrderVolume = volume,
			Balance = state is OrderStates.Active or OrderStates.Pending ? volume : order.Status == 3 ? volume : 0,
			OrderState = state,
			ServerTime = order.Time.ToDateTime() ?? DateTime.UtcNow,
			TimeInForce = timeInForce,
			ExpiryDate = expiry,
			Slippage = order.Range,
			Condition = condition,
			Error = isFailed ? new InvalidOperationException($"FXCM order failed with status {order.Status}.") : null,
		}, cancellationToken);

		if (order.TradeId is > 0 && order.Status == 9)
		{
			var fillKey = $"order:{order.OrderId}:{order.TradeId}";
			if (_reportedFills.TryAdd(fillKey))
			{
				await SendOutMessageAsync(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OriginalTransactionId = originalId,
					OrderId = order.OrderId,
					TradeId = order.TradeId,
					SecurityId = tracker?.SecurityId ?? order.Symbol.ToSecurityId(),
					PortfolioName = tracker?.PortfolioName ?? order.AccountName.IsEmpty(order.AccountId),
					Side = side,
					TradePrice = price,
					TradeVolume = volume,
					ServerTime = order.Time.ToDateTime() ?? DateTime.UtcNow,
				}, cancellationToken);
			}
		}
	}

	private async ValueTask ProcessPositionUpdate(FxcmPositionUpdate update,
		CancellationToken cancellationToken)
	{
		var position = update?.Position;
		if (position?.TradeId is not > 0 || position.IsTotal == true)
			return;

		var portfolio = GetPortfolioName(position);
		var symbol = position.Symbol;
		if (update.IsClosed || position.Action.EqualsIgnoreCase("D"))
			_positions.Remove(position.TradeId.Value);
		else
			_positions[position.TradeId.Value] = position;

		if (_orderStatusSubscriptionId != 0)
			await SendPositionExecution(position, update.IsClosed, _orderStatusSubscriptionId,
				cancellationToken);
		if (_portfolioSubscriptionId != 0 && !symbol.IsEmpty())
			await SendPositionGroup(portfolio, symbol, _portfolioSubscriptionId, cancellationToken);
	}

	private async ValueTask SendPositionExecution(FxcmPosition position, bool isClosed,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (position?.TradeId is not > 0 || position.Symbol.IsEmpty() || originalTransactionId == 0)
			return;

		var key = $"position:{position.TradeId}:{isClosed}:{position.Time}";
		if (!_reportedFills.TryAdd(key))
			return;
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originalTransactionId,
			TradeId = position.TradeId,
			SecurityId = position.Symbol.ToSecurityId(),
			PortfolioName = GetPortfolioName(position),
			Side = isClosed
				? position.IsBuy ? Sides.Sell : Sides.Buy
				: position.IsBuy ? Sides.Buy : Sides.Sell,
			TradePrice = isClosed ? position.ClosePrice ?? position.OpenPrice ?? 0 : position.OpenPrice ?? 0,
			TradeVolume = position.Amount ?? 0,
			PnL = isClosed ? position.GrossPnL : null,
			Commission = position.Commission,
			ServerTime = position.Time.ToDateTime() ?? DateTime.UtcNow,
		}, cancellationToken);
	}

	private ValueTask ProcessAccountUpdate(FxcmAccount account, CancellationToken cancellationToken)
	{
		if (account == null || account.IsTotal == true)
			return default;
		_accounts[account.AccountId.IsEmpty(GetPortfolioName(account))] = account;
		return _portfolioSubscriptionId == 0
			? default
			: SendAccount(account, _portfolioSubscriptionId, false, cancellationToken);
	}

	private async ValueTask SendAccount(FxcmAccount account, long originalTransactionId,
		bool sendPortfolio, CancellationToken cancellationToken)
	{
		var portfolio = GetPortfolioName(account);
		if (sendPortfolio)
		{
			await SendOutMessageAsync(new PortfolioMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = portfolio,
				BoardCode = FxcmExtensions.BoardCode,
			}, cancellationToken);
		}

		var blocked = account.Equity != null && account.UsableMargin != null
			? Math.Max(0, account.Equity.Value - account.UsableMargin.Value)
			: (decimal?)null;
		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = portfolio,
			SecurityId = SecurityId.Money,
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.BeginValue, account.Balance, true)
		.TryAdd(PositionChangeTypes.CurrentValue, account.Equity ?? account.Balance, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, account.GrossPnL, true)
		.TryAdd(PositionChangeTypes.RealizedPnL, account.DayPnL, true)
		.TryAdd(PositionChangeTypes.BlockedValue, blocked, true)
		.TryAdd(PositionChangeTypes.BuyOrdersMargin, account.UsableMargin, true), cancellationToken);
	}

	private (string Portfolio, string Symbol)[] GetPositionKeys()
		=> _positions.SyncGet(items => items.Values
			.Where(p => p != null && !p.Symbol.IsEmpty())
			.Select(p => (GetPortfolioName(p), p.Symbol))
			.Distinct()
			.ToArray());

	private ValueTask SendPositionGroup(string portfolio, string symbol, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var positions = _positions.SyncGet(items => items.Values.Where(p => p != null &&
			GetPortfolioName(p).EqualsIgnoreCase(portfolio) && p.Symbol.EqualsIgnoreCase(symbol)).ToArray());
		var signedVolume = positions.Sum(p => (p.Amount ?? 0) * (p.IsBuy ? 1 : -1));
		var grossVolume = positions.Sum(p => Math.Abs(p.Amount ?? 0));
		var averagePrice = grossVolume == 0 ? (decimal?)null :
			positions.Sum(p => Math.Abs(p.Amount ?? 0) * (p.OpenPrice ?? 0)) / grossVolume;
		var currentPrice = positions.Length == 0 ? null : positions
			.Select(p => p.ClosePrice).FirstOrDefault(price => price != null);

		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = portfolio,
			SecurityId = symbol.ToSecurityId(),
			ServerTime = DateTime.UtcNow,
		}
		.Add(PositionChangeTypes.CurrentValue, signedVolume)
		.TryAdd(PositionChangeTypes.AveragePrice, averagePrice, true)
		.TryAdd(PositionChangeTypes.CurrentPrice, currentPrice, true)
		.TryAdd(PositionChangeTypes.BlockedValue, positions.Sum(p => p.UsedMargin ?? 0), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, positions.Sum(p => p.VisiblePnL ?? p.GrossPnL ?? 0), true)
		.TryAdd(PositionChangeTypes.Commission, positions.Sum(p => p.Commission ?? 0), true), cancellationToken);
	}
}
