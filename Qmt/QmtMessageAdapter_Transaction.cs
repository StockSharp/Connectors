namespace StockSharp.Qmt;

using Native.Model;

partial class QmtMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage message,
		CancellationToken cancellationToken)
	{
		if (message.Condition != null)
			throw new NotSupportedException("The XtQuant order_stock API does not expose server-side conditional orders.");
		if (message.TimeInForce is not null and not TimeInForce.PutInQueue)
			throw new NotSupportedException("The XtQuant order_stock API does not expose time-in-force selection.");
		var orderType = message.OrderType ?? OrderTypes.Limit;
		if (orderType is not OrderTypes.Limit and not OrderTypes.Market)
			throw new NotSupportedException($"QMT order type {orderType} is not supported.");
		if (message.Volume <= 0 || message.Volume != decimal.Truncate(message.Volume))
			throw new ArgumentOutOfRangeException(nameof(message.Volume), message.Volume,
				"QMT stock order volume must be a positive whole number.");

		var client = EnsureClient();
		var account = message.PortfolioName.IsEmpty(client.Session.AccountId);
		var orderId = await client.PlaceOrderAsync(new()
		{
			ClientOrderId = message.TransactionId,
			AccountId = account,
			Symbol = message.SecurityId.ToQmtSymbol(),
			Side = message.Side == Sides.Buy ? "buy" : "sell",
			OrderType = orderType == OrderTypes.Limit ? "limit" : "market",
			Volume = checked((long)message.Volume),
			Price = orderType == OrderTypes.Limit ? message.Price : 0,
		}, cancellationToken);
		_orderTransactions[orderId] = message.TransactionId;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = message.TransactionId,
			OrderId = orderId,
			OrderStringId = orderId.ToString(CultureInfo.InvariantCulture),
			PortfolioName = account,
			SecurityId = message.SecurityId,
			Side = message.Side,
			OrderType = orderType,
			OrderPrice = message.Price,
			OrderVolume = message.Volume,
			Balance = message.Volume,
			OrderState = OrderStates.Pending,
			ServerTime = DateTime.UtcNow,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask CancelOrderAsync(OrderCancelMessage message,
		CancellationToken cancellationToken)
	{
		long orderId;
		if (message.OrderId is > 0)
			orderId = message.OrderId.Value;
		else if (!long.TryParse(message.OrderStringId, NumberStyles.None,
			CultureInfo.InvariantCulture, out orderId))
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(message.OriginalTransactionId));
		var account = message.PortfolioName.IsEmpty(EnsureClient().Session.AccountId);
		return new(EnsureClient().CancelOrderAsync(new()
		{
			ClientOrderId = message.TransactionId,
			AccountId = account,
			OrderId = orderId,
		}, cancellationToken));
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			if (_portfolioSubscriptionId == message.OriginalTransactionId)
			{
				_portfolioSubscriptionId = 0;
				_portfolioName = null;
			}
			return;
		}

		await SendPortfolioSnapshotAsync(message.TransactionId, message.PortfolioName, cancellationToken);

		if (!message.IsHistoryOnly())
		{
			_portfolioSubscriptionId = message.TransactionId;
			_portfolioName = message.PortfolioName;
			_lastPortfolioRefresh = DateTime.UtcNow;
		}
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private async ValueTask SendPortfolioSnapshotAsync(long originalTransactionId, string portfolioName,
		CancellationToken cancellationToken)
	{
		var response = await EnsureClient().GetAccountsAsync(cancellationToken);
		foreach (var account in response.Accounts ?? [])
		{
			if (!portfolioName.IsEmpty() && !account.AccountId.EqualsIgnoreCase(portfolioName))
				continue;
			await SendPortfolioAsync(account.AccountId, originalTransactionId, cancellationToken);
		}
		foreach (var asset in response.Assets ?? [])
		{
			if (!portfolioName.IsEmpty() && !asset.AccountId.EqualsIgnoreCase(portfolioName))
				continue;
			await SendAssetAsync(asset, originalTransactionId, cancellationToken);
		}
		foreach (var position in await EnsureClient().GetPositionsAsync(cancellationToken))
		{
			if (!portfolioName.IsEmpty() && !position.AccountId.EqualsIgnoreCase(portfolioName))
				continue;
			await SendPositionAsync(position, originalTransactionId, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			if (_orderStatusSubscriptionId == message.OriginalTransactionId)
				_orderStatusSubscriptionId = 0;
			return;
		}

		foreach (var order in await EnsureClient().GetOrdersAsync(cancellationToken))
		{
			if (!message.PortfolioName.IsEmpty() && !order.AccountId.EqualsIgnoreCase(message.PortfolioName))
				continue;
			await SendOrderAsync(order, message.TransactionId, true, cancellationToken);
		}
		foreach (var fill in await EnsureClient().GetFillsAsync(cancellationToken))
		{
			if (!message.PortfolioName.IsEmpty() && !fill.AccountId.EqualsIgnoreCase(message.PortfolioName))
				continue;
			await SendFillAsync(fill, message.TransactionId, cancellationToken);
		}

		if (!message.IsHistoryOnly())
			_orderStatusSubscriptionId = message.TransactionId;
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private ValueTask ProcessOrderAsync(QmtOrder order, CancellationToken cancellationToken)
		=> SendOrderAsync(order, 0, false, cancellationToken);

	private async ValueTask SendOrderAsync(QmtOrder order, long originalTransactionId, bool isLookup,
		CancellationToken cancellationToken)
	{
		if (order == null || order.OrderId <= 0 || order.Symbol.IsEmpty())
			return;

		var transactionId = order.ClientOrderId;
		if (transactionId == 0)
			transactionId = _orderTransactions.TryGetValue(order.OrderId, out var stored) ? stored : 0;
		else
			_orderTransactions[order.OrderId] = transactionId;
		var origin = isLookup
			? originalTransactionId
			: transactionId != 0 ? transactionId : _orderStatusSubscriptionId;
		if (!isLookup && origin == 0)
			return;

		var state = order.Status.ToOrderState();
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			OriginalTransactionId = origin,
			TransactionId = isLookup ? transactionId : 0,
			OrderId = order.OrderId,
			OrderStringId = order.OrderId.ToString(CultureInfo.InvariantCulture),
			OrderBoardId = order.OrderSystemId,
			PortfolioName = order.AccountId,
			SecurityId = order.Symbol.ToSecurityId(),
			Side = order.Side.ToSide(),
			OrderType = order.OrderType.ToOrderType(),
			OrderPrice = order.Price,
			OrderVolume = order.Volume,
			Balance = Math.Max(0, order.Volume - order.FilledVolume),
			AveragePrice = order.AveragePrice > 0 ? order.AveragePrice : null,
			OrderState = state,
			ServerTime = order.Time.ToUtc(),
			Error = state == OrderStates.Failed
				? new InvalidOperationException(order.StatusMessage.IsEmpty("QMT rejected the order."))
				: null,
		}, cancellationToken);
	}

	private ValueTask ProcessFillAsync(QmtFill fill, CancellationToken cancellationToken)
	{
		var transactionId = fill != null && _orderTransactions.TryGetValue(fill.OrderId, out var stored)
			? stored
			: 0;
		return SendFillAsync(fill, transactionId != 0 ? transactionId : _orderStatusSubscriptionId,
			cancellationToken);
	}

	private ValueTask SendFillAsync(QmtFill fill, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (fill == null || fill.TradeId.IsEmpty() || fill.Symbol.IsEmpty())
			return default;
		if (originalTransactionId == 0 || !_tradeIds.TryAdd(fill.TradeId, 0))
			return default;

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OriginalTransactionId = originalTransactionId,
			OrderId = fill.OrderId,
			OrderStringId = fill.OrderId.ToString(CultureInfo.InvariantCulture),
			OrderBoardId = fill.OrderSystemId,
			TradeStringId = fill.TradeId,
			PortfolioName = fill.AccountId,
			SecurityId = fill.Symbol.ToSecurityId(),
			Side = fill.Side.ToSide(),
			TradePrice = fill.Price,
			TradeVolume = fill.Volume,
			ServerTime = fill.Time.ToUtc(),
		}, cancellationToken);
	}

	private ValueTask ProcessAssetAsync(QmtAsset asset, CancellationToken cancellationToken)
		=> _portfolioSubscriptionId == 0
			? default
			: SendAssetAsync(asset, _portfolioSubscriptionId, cancellationToken);

	private async ValueTask SendAssetAsync(QmtAsset asset, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (asset == null || asset.AccountId.IsEmpty())
			return;
		await SendPortfolioAsync(asset.AccountId, originalTransactionId, cancellationToken);
		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = asset.AccountId,
			SecurityId = SecurityId.Money,
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.BeginValue, asset.TotalAsset, true)
		.TryAdd(PositionChangeTypes.CurrentValue, asset.Cash, true)
		.TryAdd(PositionChangeTypes.BlockedValue, asset.FrozenCash, true), cancellationToken);
	}

	private ValueTask ProcessPositionAsync(QmtPosition position, CancellationToken cancellationToken)
		=> _portfolioSubscriptionId == 0
			? default
			: SendPositionAsync(position, _portfolioSubscriptionId, cancellationToken);

	private async ValueTask SendPositionAsync(QmtPosition position, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (position == null || position.AccountId.IsEmpty() || position.Symbol.IsEmpty())
			return;
		await SendPortfolioAsync(position.AccountId, originalTransactionId, cancellationToken);
		await SendOutMessageAsync(new PositionChangeMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = position.AccountId,
			SecurityId = position.Symbol.ToSecurityId(),
			ServerTime = DateTime.UtcNow,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, position.Volume, true)
		.TryAdd(PositionChangeTypes.BlockedValue, Math.Max(0, position.Volume - position.AvailableVolume), true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice, true)
		.TryAdd(PositionChangeTypes.CurrentPrice,
			position.Volume > 0 ? position.MarketValue / position.Volume : null, true), cancellationToken);
	}

	private ValueTask SendPortfolioAsync(string accountId, long originalTransactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new PortfolioMessage
		{
			OriginalTransactionId = originalTransactionId,
			PortfolioName = accountId,
			BoardCode = BoardCodes.Sse,
		}, cancellationToken);
}
