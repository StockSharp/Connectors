namespace StockSharp.DukasCopyLive;

public partial class DukasCopyLiveMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		if (regMsg.Volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(regMsg.Volume), regMsg.Volume,
				"JForex order amount must be positive and is expressed in millions.");

		var orderType = regMsg.OrderType ?? OrderTypes.Limit;
		if (orderType is not (OrderTypes.Market or OrderTypes.Limit or OrderTypes.Conditional))
			throw new NotSupportedException($"JForex does not support StockSharp order type '{orderType}'.");
		if (orderType != OrderTypes.Market && regMsg.Price <= 0)
			throw new InvalidOperationException("JForex pending orders require a positive price.");

		var condition = regMsg.Condition as DukasCopyLiveOrderCondition ?? new();
		var nativeCommand = condition.NativeCommand.ToNative(regMsg.Side, orderType);
		var isMarket = nativeCommand is "BUY" or "SELL";
		var result = await GetClient().PlaceOrder(new()
		{
			Label = $"ss_{regMsg.TransactionId}",
			Symbol = regMsg.SecurityId.SecurityCode.NormalizeDukasSymbol(),
			OrderCommand = nativeCommand,
			Amount = regMsg.Volume,
			Price = isMarket ? 0 : regMsg.Price,
			Slippage = regMsg.Slippage ?? condition.Slippage ?? -1,
			StopLossPrice = condition.StopLoss ?? 0,
			TakeProfitPrice = condition.TakeProfit ?? 0,
			GoodTillTime = !isMarket && regMsg.TillDate != null
				? new DateTimeOffset(regMsg.TillDate.Value.ToUniversalTime()).ToUnixTimeMilliseconds()
				: 0,
			Comment = condition.Comment,
		}, cancellationToken);

		if (result.Id.IsEmpty())
			throw new InvalidOperationException("JForex returned no order identifier.");

		_orders[result.Id] = new()
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

		await SendOrder(result, regMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		var orderId = replaceMsg.OldOrderStringId.ThrowIfEmpty(nameof(replaceMsg.OldOrderStringId));
		if (replaceMsg.Volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(replaceMsg.Volume), replaceMsg.Volume,
				"JForex replacement amount must be positive.");

		_orders.TryGetValue(orderId, out var tracker);
		var condition = replaceMsg.Condition as DukasCopyLiveOrderCondition ?? tracker?.Condition ?? new();
		var order = await GetClient().ReplaceOrder(new()
		{
			OrderId = orderId,
			Amount = replaceMsg.Volume,
			Price = replaceMsg.Price > 0 ? replaceMsg.Price : null,
			StopLossPrice = condition.StopLoss,
			TakeProfitPrice = condition.TakeProfit,
			GoodTillTime = replaceMsg.TillDate == null
				? null
				: new DateTimeOffset(replaceMsg.TillDate.Value.ToUniversalTime()).ToUnixTimeMilliseconds(),
		}, cancellationToken);

		if (tracker != null)
		{
			tracker.Price = replaceMsg.Price;
			tracker.Volume = replaceMsg.Volume;
			tracker.OrderType = replaceMsg.OrderType ?? tracker.OrderType;
			tracker.Condition = condition;
		}

		await SendOrder(order, replaceMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		var orderId = cancelMsg.OrderStringId.ThrowIfEmpty(nameof(cancelMsg.OrderStringId));
		return GetClient().CancelOrder(orderId, cancellationToken).AsValueTask();
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);
		if (!statusMsg.IsSubscribe)
		{
			if (_orderStatusSubscriptionId == statusMsg.OriginalTransactionId)
				_orderStatusSubscriptionId = 0;
			return;
		}

		foreach (var order in await GetClient().GetOrders(cancellationToken))
			await SendOrder(order, statusMsg.TransactionId, cancellationToken);

		if (statusMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(statusMsg.TransactionId, cancellationToken);
			return;
		}

		_orderStatusSubscriptionId = statusMsg.TransactionId;
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
				_portfolioSubscriptionId = 0;
			return;
		}

		var account = await GetClient().GetAccount(cancellationToken);
		if (account != null)
		{
			_portfolioName = account.AccountId.IsEmpty(account.UserName).IsEmpty("DUKASCOPY");
			await SendAccount(account, lookupMsg.TransactionId, true, cancellationToken);
		}

		var openOrders = (await GetClient().GetOrders(cancellationToken))
			.Where(order => order.State.EqualsIgnoreCase("FILLED") && !order.Symbol.IsEmpty()).ToArray();
		_positionOrders.Clear();
		foreach (var order in openOrders)
			_positionOrders[order.Id] = order;
		foreach (var group in openOrders.GroupBy(order => order.Symbol, StringComparer.OrdinalIgnoreCase))
			await SendPosition(group.Key, group, lookupMsg.TransactionId, cancellationToken);

		if (lookupMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
			return;
		}

		_portfolioSubscriptionId = lookupMsg.TransactionId;
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private ValueTask ProcessOrderUpdate(DukasCopyLiveOrder order, CancellationToken cancellationToken)
		=> order == null ? default : ProcessOrderUpdateCore(order, cancellationToken);

	private async ValueTask ProcessOrderUpdateCore(DukasCopyLiveOrder order,
		CancellationToken cancellationToken)
	{
		if (_orderStatusSubscriptionId != 0 || _orders.ContainsKey(order.Id))
			await SendOrder(order, 0, cancellationToken);

		if (_portfolioSubscriptionId != 0 && !order.Symbol.IsEmpty())
		{
			if (order.State.EqualsIgnoreCase("FILLED"))
				_positionOrders[order.Id] = order;
			else
				_positionOrders.Remove(order.Id);

			var active = _positionOrders.SyncGet(items => items.Values
				.Where(item => item.Symbol.EqualsIgnoreCase(order.Symbol)).ToArray());
			await SendPosition(order.Symbol, active, _portfolioSubscriptionId, cancellationToken);
		}
	}

	private async ValueTask SendOrder(DukasCopyLiveOrder order, long lookupTransactionId,
		CancellationToken cancellationToken)
	{
		if (order?.Id.IsEmpty() != false)
			return;

		_orders.TryGetValue(order.Id, out var tracker);
		var originalId = lookupTransactionId != 0
			? lookupTransactionId
			: tracker?.TransactionId ?? _orderStatusSubscriptionId;
		if (originalId == 0)
			return;

		var side = tracker?.Side ?? order.Command.ToSide();
		var orderType = tracker?.OrderType ?? order.Command.ToOrderType();
		var state = order.State.ToOrderState();
		var volume = order.RequestedAmount > 0 ? order.RequestedAmount :
			order.Amount > 0 ? order.Amount : tracker?.Volume ?? 0;
		var filled = Math.Max(0, order.FilledAmount);
		var condition = tracker?.Condition ?? new DukasCopyLiveOrderCondition
		{
			StopLoss = order.StopLossPrice > 0 ? order.StopLossPrice : null,
			TakeProfit = order.TakeProfitPrice > 0 ? order.TakeProfitPrice : null,
			Comment = order.Comment,
		};

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = originalId,
			TransactionId = tracker?.TransactionId ?? 0,
			OrderStringId = order.Id,
			SecurityId = tracker?.SecurityId ?? order.Symbol.ToSecurityId(),
			PortfolioName = tracker?.PortfolioName.IsEmpty(_portfolioName).IsEmpty("DUKASCOPY"),
			Side = side,
			OrderType = orderType,
			OrderPrice = order.OpenPrice > 0 ? order.OpenPrice : tracker?.Price ?? 0,
			OrderVolume = volume,
			Balance = Math.Max(0, volume - filled),
			OrderState = state,
			ServerTime = Math.Max(order.CreationTime, Math.Max(order.FillTime, order.CloseTime)).ToUtc(),
			ExpiryDate = order.GoodTillTime > 0 ? order.GoodTillTime.ToUtc() : null,
			Condition = condition,
			PnL = order.ProfitLoss,
			Error = state == OrderStates.Failed
				? new InvalidOperationException(order.Message.IsEmpty("JForex rejected the order."))
				: null,
		}, cancellationToken);

		var previousFilled = _filledAmounts.TryGetValue(order.Id, out var value) ? value : 0;
		if (filled > previousFilled)
		{
			_filledAmounts[order.Id] = filled;
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = originalId,
				OrderStringId = order.Id,
				TradeStringId = $"{order.Id}:{filled.ToString(CultureInfo.InvariantCulture)}",
				SecurityId = tracker?.SecurityId ?? order.Symbol.ToSecurityId(),
				PortfolioName = tracker?.PortfolioName.IsEmpty(_portfolioName).IsEmpty("DUKASCOPY"),
				Side = side,
				TradePrice = order.OpenPrice,
				TradeVolume = filled - previousFilled,
				PnL = order.ProfitLoss,
				ServerTime = order.FillTime.ToUtc(),
			}, cancellationToken);
		}
	}

	private ValueTask ProcessAccountUpdate(DukasCopyLiveAccount account,
		CancellationToken cancellationToken)
		=> account == null || _portfolioSubscriptionId == 0
			? default
			: SendAccount(account, _portfolioSubscriptionId, false, cancellationToken);

	private async ValueTask SendAccount(DukasCopyLiveAccount account, long originalTransactionId,
		bool sendPortfolio, CancellationToken cancellationToken)
	{
		var portfolio = account.AccountId.IsEmpty(account.UserName).IsEmpty("DUKASCOPY");
		_portfolioName = portfolio;
		if (sendPortfolio)
		{
			await SendOutMessageAsync(new PortfolioMessage
			{
				OriginalTransactionId = originalTransactionId,
				PortfolioName = portfolio,
				BoardCode = DukasCopyLiveExtensions.BoardCode,
				Currency = account.Currency.ToCurrency(),
			}, cancellationToken);
		}

		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = portfolio,
			SecurityId = SecurityId.Money,
			ServerTime = DateTime.UtcNow,
		}
		.Add(PositionChangeTypes.BeginValue, account.Balance)
		.Add(PositionChangeTypes.CurrentValue, account.Equity)
		.Add(PositionChangeTypes.BlockedValue, account.UsedMargin)
		.Add(PositionChangeTypes.CurrentPrice, account.UseOfLeverage), cancellationToken);
	}

	private ValueTask SendPosition(string symbol, IEnumerable<DukasCopyLiveOrder> orders, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var items = orders?.Where(order => order != null).ToArray() ?? [];
		if (symbol.IsEmpty())
			return default;

		var signedAmount = items.Sum(order => order.FilledAmount *
			(order.Command.ToSide() == Sides.Buy ? 1 : -1));
		var grossAmount = items.Sum(order => Math.Abs(order.FilledAmount));
		var averagePrice = grossAmount > 0
			? items.Sum(order => Math.Abs(order.FilledAmount) * order.OpenPrice) / grossAmount
			: 0;

		return SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = _portfolioName.IsEmpty("DUKASCOPY"),
			SecurityId = symbol.ToSecurityId(),
			ServerTime = DateTime.UtcNow,
		}
		.Add(PositionChangeTypes.CurrentValue, signedAmount)
		.TryAdd(PositionChangeTypes.AveragePrice, averagePrice > 0 ? averagePrice : null, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, items.Sum(order => order.ProfitLoss), true),
			cancellationToken);
	}
}
