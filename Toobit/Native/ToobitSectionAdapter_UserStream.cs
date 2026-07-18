namespace StockSharp.Toobit.Native;

sealed partial class ToobitSectionAdapter
{
	private async ValueTask OnBalanceAsync(ToobitUserBalanceEvent update,
		CancellationToken cancellationToken)
	{
		var serverTime = update.EventTime.ToUtcTime() ?? CurrentTime;
		foreach (var balance in update.Balances ?? [])
		{
			await SendSpotBalanceAsync(balance.Asset, balance.Free, balance.Locked,
				serverTime, 0, cancellationToken);
		}
	}

	private ValueTask OnPositionAsync(ToobitUserPositionEvent update,
		CancellationToken cancellationToken)
	{
		if (!_isFutures || update.Symbol.IsEmpty())
			return default;

		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = update.Symbol.ToStockSharp(BoardCode),
			ServerTime = update.EventTime.ToUtcTime() ?? CurrentTime,
			Side = update.Side == ToobitPositionSides.Long ? Sides.Buy : Sides.Sell,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, update.Position.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.AveragePrice, update.AveragePrice.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.Leverage, update.Leverage.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, update.UnrealizedPnl.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.LiquidationPrice, update.LiquidationPrice.ToDecimal(), true),
		cancellationToken);
	}

	private ValueTask OnOrderAsync(ToobitUserOrderEvent update,
		CancellationToken cancellationToken)
	{
		if (update.Symbol.IsEmpty())
			return default;

		var transactionId = ToobitExtensions.ExtractTransactionId(update.ClientOrderId) ?? 0;
		var total = update.Quantity.ToDecimal();
		var executed = update.ExecutedQuantity.ToDecimal() ?? 0m;
		var type = ToOrderType(update.Type, update.PriceType, null, out var condition);
		var close = update.IsClose == true ||
			update.Side is ToobitOrderSides.BuyClose or ToobitOrderSides.SellClose;

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = update.Symbol.ToStockSharp(BoardCode),
			ServerTime = update.UpdateTime.ToUtcTime()
				?? update.EventTime.ToUtcTime()
				?? update.CreationTime.ToUtcTime()
				?? CurrentTime,
			PortfolioName = _portfolioName,
			Side = update.Side.ToStockSharp(),
			OrderVolume = total,
			Balance = total is decimal volume ? (volume - executed).Max(0m) : null,
			OrderPrice = update.Price.ToDecimal() ?? 0m,
			OrderType = type,
			OrderState = update.Status.ToStockSharp(),
			OrderId = update.OrderId.ToLongId(),
			OrderStringId = update.ClientOrderId,
			TransactionId = transactionId,
			OriginalTransactionId = transactionId,
			TimeInForce = update.TimeInForce.ToStockSharp(),
			PostOnly = update.TimeInForce == ToobitTimeInForce.PostOnly,
			Condition = condition,
			PositionEffect = close ? OrderPositionEffects.CloseOnly : OrderPositionEffects.Default,
		}, cancellationToken);
	}

	private ValueTask OnUserTradeAsync(ToobitUserTradeEvent update,
		CancellationToken cancellationToken)
	{
		if (update.Symbol.IsEmpty())
			return default;

		var transactionId = ToobitExtensions.ExtractTransactionId(update.ClientOrderId) ?? 0;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = update.Symbol.ToStockSharp(BoardCode),
			ServerTime = update.Time.ToUtcTime() ?? update.EventTime.ToUtcTime() ?? CurrentTime,
			PortfolioName = _portfolioName,
			Side = update.Side.ToStockSharp(),
			OrderId = update.OrderId.ToLongId(),
			OrderStringId = update.ClientOrderId,
			TradeId = update.TicketId.ToLongId(),
			TradeStringId = update.TicketId,
			TradePrice = update.Price.ToDecimal(),
			TradeVolume = update.Quantity.ToDecimal(),
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}
}
