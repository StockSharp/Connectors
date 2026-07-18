namespace StockSharp.Toobit.Native;

sealed partial class ToobitSectionAdapter
{
	public override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var order = await RegisterOrderInternalAsync(regMsg.SecurityId.ToNative(), regMsg.Side,
			regMsg.OrderType, regMsg.Price, regMsg.Volume.Abs(), regMsg.TimeInForce,
			regMsg.PostOnly, regMsg.PositionEffect, regMsg.Condition as ToobitOrderCondition,
			ToobitExtensions.CreateClientOrderId(regMsg.TransactionId, regMsg.UserOrderId),
			cancellationToken);

		await SendOrderAsync(order, regMsg.TransactionId, regMsg.TransactionId,
			regMsg.SecurityId.ToNative(), regMsg.Side, regMsg.OrderType, regMsg.Price,
			regMsg.Volume.Abs(), regMsg.PositionEffect, regMsg.Condition, cancellationToken);
	}

	public override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		var symbol = replaceMsg.SecurityId.ToNative();
		await CancelOrderInternalAsync(symbol, replaceMsg.OldOrderId,
			replaceMsg.OldOrderStringId, null, cancellationToken);

		var order = await RegisterOrderInternalAsync(symbol, replaceMsg.Side,
			replaceMsg.OrderType, replaceMsg.Price, replaceMsg.Volume.Abs(), replaceMsg.TimeInForce,
			replaceMsg.PostOnly, replaceMsg.PositionEffect, replaceMsg.Condition as ToobitOrderCondition,
			ToobitExtensions.CreateClientOrderId(replaceMsg.TransactionId, replaceMsg.UserOrderId),
			cancellationToken);

		await SendOrderAsync(order, replaceMsg.TransactionId, replaceMsg.TransactionId,
			symbol, replaceMsg.Side, replaceMsg.OrderType, replaceMsg.Price,
			replaceMsg.Volume.Abs(), replaceMsg.PositionEffect, replaceMsg.Condition, cancellationToken);
	}

	public override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		if (cancelMsg.OrderId is null && cancelMsg.OrderStringId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.TransactionId));

		var order = await CancelOrderInternalAsync(cancelMsg.SecurityId.ToNative(), cancelMsg.OrderId,
			cancelMsg.OrderStringId, null, cancellationToken);
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.IsEmpty(cancelMsg.SecurityId.SecurityCode).ToStockSharp(BoardCode),
			ServerTime = GetOrderTime(order),
			PortfolioName = _portfolioName,
			OrderId = order.OrderId.ToLongId() ?? cancelMsg.OrderId,
			OrderStringId = order.ClientOrderId.IsEmpty(cancelMsg.OrderStringId),
			OrderState = OrderStates.Done,
			Balance = 0m,
			OriginalTransactionId = cancelMsg.TransactionId,
		}, cancellationToken);
	}

	public override async ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg,
		CancellationToken cancellationToken)
	{
		EnsurePrivateReady();
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
			throw new NotSupportedException("Closing positions through group cancellation is not supported.");

		var orders = await RestClient.GetOpenOrdersAsync(_isFutures,
			cancelMsg.SecurityId.SecurityCode, cancellationToken);
		foreach (var order in orders ?? [])
		{
			if (order?.OrderId.IsEmpty() != false)
				continue;
			if (cancelMsg.Side is Sides side && order.Side?.ToStockSharp() != side)
				continue;
			if (cancelMsg.IsStop is bool isStop && IsConditional(order.Type) != isStop)
				continue;

			var canceled = await CancelOrderInternalAsync(order.Symbol, order.OrderId.ToLongId(),
				order.ClientOrderId, order.Type, cancellationToken);
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				HasOrderInfo = true,
				SecurityId = order.Symbol.ToStockSharp(BoardCode),
				ServerTime = GetOrderTime(canceled),
				PortfolioName = _portfolioName,
				OrderId = order.OrderId.ToLongId(),
				OrderStringId = order.ClientOrderId,
				OrderState = OrderStates.Done,
				Balance = 0m,
				OriginalTransactionId = cancelMsg.TransactionId,
			}, cancellationToken);
		}
	}

	public override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		if (!lookupMsg.IsSubscribe)
			return;

		EnsurePrivateReady();
		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = _portfolioName,
			BoardCode = BoardCode,
			OriginalTransactionId = lookupMsg.TransactionId,
		}, cancellationToken);

		if (_isFutures)
		{
			foreach (var balance in await RestClient.GetFuturesBalancesAsync(cancellationToken) ?? [])
				await SendFuturesBalanceAsync(balance, lookupMsg.TransactionId, cancellationToken);

			foreach (var position in await RestClient.GetPositionsAsync(null, cancellationToken) ?? [])
				await SendPositionAsync(position, lookupMsg.TransactionId, cancellationToken);
		}
		else
		{
			var account = await RestClient.GetSpotAccountAsync(cancellationToken);
			foreach (var balance in account.Balances ?? [])
				await SendSpotBalanceAsync(balance.Coin, balance.Free, balance.Locked,
					CurrentTime, lookupMsg.TransactionId, cancellationToken);
		}
	}

	public override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg,
		CancellationToken cancellationToken)
	{
		if (!statusMsg.IsSubscribe)
			return;

		EnsurePrivateReady();
		var symbol = statusMsg.SecurityId.SecurityCode;
		var limit = (statusMsg.Count ?? 1000).Min(1000).To<int>();
		var openOrders = await RestClient.GetOpenOrdersAsync(_isFutures, symbol, cancellationToken) ?? [];
		var history = await RestClient.GetOrderHistoryAsync(_isFutures, symbol,
			statusMsg.From, statusMsg.To, limit, cancellationToken) ?? [];
		var orders = openOrders.Concat(history)
			.Where(static order => order is not null && !order.OrderId.IsEmpty())
			.GroupBy(static order => order.OrderId, StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.OrderByDescending(GetOrderTime).First())
			.OrderBy(GetOrderTime)
			.Take(limit)
			.ToArray();

		foreach (var order in orders)
			await SendOrderStatusAsync(order, statusMsg.TransactionId, cancellationToken);

		var symbols = symbol.IsEmpty()
			? orders.Select(static order => order.Symbol).Where(static value => !value.IsEmpty())
				.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
			: [symbol];
		var tradesLeft = statusMsg.Count ?? long.MaxValue;
		foreach (var tradeSymbol in symbols)
		{
			if (tradesLeft <= 0)
				break;

			var trades = await RestClient.GetUserTradesAsync(_isFutures, tradeSymbol,
				statusMsg.From, statusMsg.To, tradesLeft.Min(1000).To<int>(), cancellationToken);
			foreach (var trade in (trades ?? []).OrderBy(static trade => trade.Time.ToUtcTime()))
			{
				await SendUserTradeAsync(trade, statusMsg.TransactionId, cancellationToken);
				if (--tradesLeft <= 0)
					break;
			}
		}
	}

	private async ValueTask<ToobitOrder> RegisterOrderInternalAsync(string symbol, Sides side,
		OrderTypes? orderType, decimal price, decimal volume, TimeInForce? timeInForce,
		bool? postOnly, OrderPositionEffects? positionEffect, ToobitOrderCondition condition,
		string clientOrderId, CancellationToken cancellationToken)
	{
		if (volume <= 0)
			throw new InvalidOperationException("Order volume must be positive.");

		if (!_isFutures)
		{
			if (orderType == OrderTypes.Conditional || condition is not null)
				throw new NotSupportedException("Toobit spot trigger orders are not supported by the v1 spot order endpoint.");

			var type = orderType switch
			{
				null or OrderTypes.Limit when postOnly == true => ToobitOrderTypes.LimitMaker,
				null or OrderTypes.Limit => ToobitOrderTypes.Limit,
				OrderTypes.Market => ToobitOrderTypes.Market,
				_ => throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(orderType, 0)),
			};
			var needsPrice = type is ToobitOrderTypes.Limit or ToobitOrderTypes.LimitMaker;
			if (needsPrice && price <= 0)
				throw new InvalidOperationException("Limit order price must be positive.");

			return await RestClient.RegisterSpotOrderAsync(new ToobitSpotOrderRequest
			{
				Symbol = symbol,
				Side = side.ToSpotNative(),
				Type = type,
				TimeInForce = type == ToobitOrderTypes.Limit ? timeInForce.ToNative() : null,
				Quantity = volume,
				Price = needsPrice ? price : null,
				ClientOrderId = clientOrderId,
			}, cancellationToken);
		}

		ToobitOrderTypes futuresType;
		ToobitPriceTypes priceType;
		decimal? orderPrice = null;
		decimal? stopPrice = null;
		ToobitTimeInForce? nativeTimeInForce = null;
		switch (orderType ?? OrderTypes.Limit)
		{
			case OrderTypes.Limit:
				if (price <= 0)
					throw new InvalidOperationException("Limit order price must be positive.");
				futuresType = ToobitOrderTypes.Limit;
				priceType = ToobitPriceTypes.Input;
				orderPrice = price;
				nativeTimeInForce = postOnly == true ? ToobitTimeInForce.PostOnly : timeInForce.ToNative();
				break;

			case OrderTypes.Market:
				if (postOnly == true)
					throw new InvalidOperationException("Market order cannot be post-only.");
				futuresType = ToobitOrderTypes.Limit;
				priceType = ToobitPriceTypes.Market;
				break;

			case OrderTypes.Conditional:
				if (condition is null)
					throw new InvalidOperationException("Conditional order requires ToobitOrderCondition.");
				if (postOnly == true)
					throw new InvalidOperationException("Trigger order cannot be post-only.");
				stopPrice = condition.ActivationPrice ?? (price > 0 ? price : null);
				if (stopPrice is null or <= 0)
					throw new InvalidOperationException("Trigger price must be positive.");
				futuresType = ToobitOrderTypes.Stop;
				priceType = condition.ClosePositionPrice is null ? ToobitPriceTypes.Market : ToobitPriceTypes.Input;
				orderPrice = condition.ClosePositionPrice;
				if (orderPrice is not null)
					nativeTimeInForce = timeInForce.ToNative();
				break;

			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(orderType, 0));
		}

		return await RestClient.RegisterFuturesOrderAsync(new ToobitFuturesOrderRequest
		{
			Symbol = symbol,
			Side = side.ToNative(positionEffect == OrderPositionEffects.CloseOnly),
			Type = futuresType,
			TimeInForce = nativeTimeInForce,
			PriceType = priceType,
			Quantity = volume,
			Price = orderPrice,
			StopPrice = stopPrice,
			ClientOrderId = clientOrderId,
		}, cancellationToken);
	}

	private ValueTask<ToobitOrder> CancelOrderInternalAsync(string symbol, long? orderId,
		string clientOrderId, ToobitOrderTypes? type, CancellationToken cancellationToken)
	{
		var stringOrderId = orderId?.ToString(CultureInfo.InvariantCulture);
		return _isFutures
			? RestClient.CancelFuturesOrderAsync(new ToobitOrderRequest
			{
				Symbol = symbol,
				OrderId = stringOrderId,
				ClientOrderId = clientOrderId,
				Type = type is ToobitOrderTypes.Stop or ToobitOrderTypes.StopProfitLoss
					? ToobitOrderTypes.Stop : type,
			}, cancellationToken)
			: RestClient.CancelSpotOrderAsync(new ToobitCancelSpotOrderRequest
			{
				OrderId = stringOrderId,
				ClientOrderId = clientOrderId,
			}, cancellationToken);
	}

	private ValueTask SendOrderAsync(ToobitOrder order, long transactionId, long originalTransactionId,
		string fallbackSymbol, Sides fallbackSide, OrderTypes? fallbackType, decimal fallbackPrice,
		decimal fallbackVolume, OrderPositionEffects? positionEffect, OrderCondition fallbackCondition,
		CancellationToken cancellationToken)
	{
		var orderType = ToOrderType(order.Type, order.PriceType, order.StopPrice.ToDecimal(), out var condition);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.IsEmpty(fallbackSymbol).ToStockSharp(BoardCode),
			ServerTime = GetOrderTime(order),
			PortfolioName = _portfolioName,
			Side = order.Side?.ToStockSharp() ?? fallbackSide,
			OrderVolume = order.OriginalQuantity.ToDecimal() ?? fallbackVolume,
			Balance = order.GetBalance() ?? fallbackVolume,
			OrderPrice = order.Price.ToDecimal() ?? fallbackPrice,
			OrderType = orderType ?? fallbackType ?? OrderTypes.Limit,
			OrderState = order.Status.ToStockSharp(),
			OrderId = order.OrderId.ToLongId(),
			OrderStringId = order.ClientOrderId,
			TransactionId = transactionId,
			OriginalTransactionId = originalTransactionId,
			TimeInForce = order.TimeInForce.ToStockSharp(),
			PostOnly = order.Type == ToobitOrderTypes.LimitMaker || order.TimeInForce == ToobitTimeInForce.PostOnly,
			Condition = condition ?? fallbackCondition,
			PositionEffect = positionEffect,
		}, cancellationToken);
	}

	private ValueTask SendOrderStatusAsync(ToobitOrder order, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (order.Side is not { } nativeSide)
			return default;

		var orderType = ToOrderType(order.Type, order.PriceType, order.StopPrice.ToDecimal(), out var condition);
		var transactionId = ToobitExtensions.ExtractTransactionId(order.ClientOrderId) ?? 0;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			SecurityId = order.Symbol.ToStockSharp(BoardCode),
			ServerTime = GetOrderTime(order),
			PortfolioName = _portfolioName,
			Side = nativeSide.ToStockSharp(),
			OrderVolume = order.OriginalQuantity.ToDecimal(),
			Balance = order.GetBalance() ?? 0m,
			OrderPrice = order.Price.ToDecimal() ?? 0m,
			OrderType = orderType,
			OrderState = order.Status.ToStockSharp(),
			OrderId = order.OrderId.ToLongId(),
			OrderStringId = order.ClientOrderId,
			TransactionId = transactionId,
			OriginalTransactionId = originalTransactionId,
			TimeInForce = order.TimeInForce.ToStockSharp(),
			PostOnly = order.Type == ToobitOrderTypes.LimitMaker || order.TimeInForce == ToobitTimeInForce.PostOnly,
			Condition = condition,
			PositionEffect = order.Side is ToobitOrderSides.BuyClose or ToobitOrderSides.SellClose
				? OrderPositionEffects.CloseOnly : OrderPositionEffects.Default,
		}, cancellationToken);
	}

	private ValueTask SendUserTradeAsync(ToobitUserTrade trade, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var side = trade.Side?.ToStockSharp()
			?? (trade.IsBuyer is bool isBuyer ? (isBuyer ? Sides.Buy : Sides.Sell) : (Sides?)null);
		if (side is null)
			return default;

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			SecurityId = trade.Symbol.ToStockSharp(BoardCode),
			ServerTime = trade.Time.ToUtcTime() ?? CurrentTime,
			PortfolioName = _portfolioName,
			Side = side.Value,
			OrderId = trade.OrderId.ToLongId(),
			TradeId = trade.TradeId.ToLongId() ?? trade.TicketId.ToLongId(),
			TradeStringId = trade.TradeId.IsEmpty(trade.TicketId),
			TradePrice = trade.Price.ToDecimal(),
			TradeVolume = trade.Quantity.ToDecimal(),
			Commission = trade.Commission.ToDecimal(),
			CommissionCurrency = trade.CommissionAsset,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private ValueTask SendSpotBalanceAsync(string asset, string free, string locked, DateTime serverTime,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		if (asset.IsEmpty())
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = asset.ToStockSharp(BoardCode),
			ServerTime = serverTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, free.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.BlockedValue, locked.ToDecimal(), true), cancellationToken);
	}

	private ValueTask SendFuturesBalanceAsync(ToobitFuturesBalance balance, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (balance?.Coin.IsEmpty() != false)
			return default;
		var total = balance.Balance.ToDecimal();
		var available = balance.AvailableBalance.ToDecimal();
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = balance.Coin.ToStockSharp(BoardCode),
			ServerTime = CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, total, true)
		.TryAdd(PositionChangeTypes.BlockedValue,
			total is decimal totalValue && available is decimal availableValue
				? (totalValue - availableValue).Max(0m) : null, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, balance.CrossUnrealizedPnl.ToDecimal(), true),
		cancellationToken);
	}

	private ValueTask SendPositionAsync(ToobitPosition position, long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (position?.Symbol.IsEmpty() != false)
			return default;
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = _portfolioName,
			SecurityId = position.Symbol.ToStockSharp(BoardCode),
			ServerTime = CurrentTime,
			OriginalTransactionId = originalTransactionId,
			Side = position.Side == ToobitPositionSides.Long ? Sides.Buy : Sides.Sell,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, position.Position.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.AveragePrice, position.AveragePrice.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.Leverage, position.Leverage.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, position.UnrealizedPnl.ToDecimal(), true)
		.TryAdd(PositionChangeTypes.LiquidationPrice, position.LiquidationPrice.ToDecimal(), true),
		cancellationToken);
	}

	private static DateTime GetOrderTime(ToobitOrder order)
		=> order.UpdateTime.ToUtcTime()
			?? order.TransactionTime.ToUtcTime()
			?? order.Time.ToUtcTime()
			?? DateTime.UtcNow;

	private static bool IsConditional(ToobitOrderTypes? type)
		=> type is ToobitOrderTypes.Stop or ToobitOrderTypes.StopLimit or ToobitOrderTypes.StopProfitLoss;

	private static OrderTypes? ToOrderType(ToobitOrderTypes? type, ToobitPriceTypes? priceType,
		decimal? stopPrice, out ToobitOrderCondition condition)
	{
		condition = null;
		if (IsConditional(type))
		{
			condition = new ToobitOrderCondition { ActivationPrice = stopPrice };
			return OrderTypes.Conditional;
		}
		if (type == ToobitOrderTypes.Market || priceType == ToobitPriceTypes.Market)
			return OrderTypes.Market;
		return type is null ? null : OrderTypes.Limit;
	}
}
