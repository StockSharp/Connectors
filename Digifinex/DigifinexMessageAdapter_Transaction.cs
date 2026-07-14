namespace StockSharp.Digifinex;

public partial class DigifinexMessageAdapter
{
	private string PortfolioName => nameof(Digifinex) + "_" + Key.ToId();

	private const string _marginMarket = "margin";
	private static readonly string[] _markets = ["spot", _marginMarket];

	private static string GetMarket(bool isMargin)
	{
		return _markets[isMargin ? 1 : 0];
	}

	private readonly SynchronizedDictionary<string, (long transId, decimal bal, bool isMargin)> _orderInfo = new(StringComparer.InvariantCultureIgnoreCase);

	/// <inheritdoc />
	protected override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var currency = regMsg.SecurityId.ToCurrency();

		switch (regMsg.OrderType)
		{
			case null:
			case OrderTypes.Limit:
			case OrderTypes.Market:
				break;
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		var isMargin = regMsg.MarginMode is not null;
		var isMarket = regMsg.OrderType == OrderTypes.Market;
		var orderId = await _httpClient.RegisterOrder(GetMarket(isMargin), currency, regMsg.Side.ToNative(isMarket), isMarket ? null : regMsg.Price, regMsg.Volume, regMsg.TimeInForce == TimeInForce.MatchOrCancel, cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OrderStringId = orderId,
			ServerTime = CurrentTime,
			OriginalTransactionId = regMsg.TransactionId,
			OrderState = OrderStates.Active,
			HasOrderInfo = true,
		}, cancellationToken);

		_orderInfo.Add(orderId, (regMsg.TransactionId, regMsg.Volume, isMargin));

		await PortfolioLookupAsync(null, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		if (cancelMsg.OrderStringId.IsEmpty())
			throw new ArgumentException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId), nameof(cancelMsg));

		if (!_orderInfo.TryGetValue(cancelMsg.OrderStringId, out var info))
			throw new ArgumentException(LocalizedStrings.NoInfoAboutOrder.Put(cancelMsg.OrderStringId), nameof(cancelMsg));

		await _httpClient.CancelOrder(GetMarket(info.isMargin), cancelMsg.OrderStringId, cancellationToken);

		await OrderStatusAsync(null, cancellationToken);
		await PortfolioLookupAsync(null, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		if (lookupMsg != null)
		{
			await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

			if (!lookupMsg.IsSubscribe)
				return;

			await SendOutMessageAsync(new PortfolioMessage
			{
				PortfolioName = PortfolioName,
				BoardCode = BoardCodes.Digifinex,
				OriginalTransactionId = lookupMsg.TransactionId
			}, cancellationToken);

			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
		}

		foreach (var asset in await _httpClient.GetSpotAssets(cancellationToken))
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = PortfolioName,
				SecurityId = asset.Currency.ToStockSharp(),
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, (decimal?)asset.Free, true)
			.TryAdd(PositionChangeTypes.BlockedValue, (decimal?)(asset.Total - asset.Free), true), cancellationToken);
		}

		foreach (var position in await _httpClient.GetPositions(cancellationToken))
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = PortfolioName,
				SecurityId = position.Symbol.ToStockSharp(),
				ServerTime = CurrentTime,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, (decimal?)position.Amount, true)
			.TryAdd(PositionChangeTypes.Leverage, (decimal?)position.LeverageRatio, true)
			.TryAdd(PositionChangeTypes.AveragePrice, (decimal?)position.EntryPrice)
			.TryAdd(PositionChangeTypes.LiquidationPrice, (decimal?)position.LiquidationPrice)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, (decimal?)position.UnrealizedPnl), cancellationToken);
		}

		_lastTimeBalanceCheck = CurrentTime;
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		if (statusMsg == null)
		{
			var portfolioRefresh = false;

			var uuids = _orderInfo.Keys.ToIgnoreCaseSet();

			foreach (var pair in await GetOpenOrders(cancellationToken))
			{
				var order = pair.Key;
				var balance = order.GetBalance();

				if (!_orderInfo.TryGetValue(order.Id, out var info))
				{
					var transId = TransactionIdGenerator.GetNextId();

					_orderInfo.Add(order.Id, (transId, balance, pair.Value));

					await ProcessOrderAsync(order, transId, 0, cancellationToken);
					portfolioRefresh = true;
				}
				else
				{
					uuids.Remove(order.Id);

					if (balance == info.bal)
						continue;

					info.bal = balance;
					_orderInfo[order.Id] = info;

					await ProcessOrderAsync(order, 0, info.transId, cancellationToken);
					portfolioRefresh = true;
				}
			}

			foreach (var uuid in uuids)
			{
				var (transId, _, isMargin) = _orderInfo.GetAndRemove(uuid);
				var orders = await _httpClient.GetOrdersInfo(GetMarket(isMargin), [uuid], cancellationToken);

				foreach (var order in orders)
				{
					await ProcessOrderAsync(order, 0, transId, cancellationToken);
				}

				portfolioRefresh = true;
			}

			if (portfolioRefresh)
				await PortfolioLookupAsync(null, cancellationToken);
		}
		else
		{
			await SendSubscriptionReplyAsync(statusMsg.TransactionId, cancellationToken);

			if (!statusMsg.IsSubscribe)
				return;

			foreach (var pair in await GetOpenOrders(cancellationToken))
			{
				var order = pair.Key;
				var transId = TransactionIdGenerator.GetNextId();

				_orderInfo.Add(order.Id, (transId, order.GetBalance(), pair.Value));
				await ProcessOrderAsync(order, transId, statusMsg.TransactionId, cancellationToken);
			}

			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
		}
	}

	private async ValueTask<IDictionary<Order, bool>> GetOpenOrders(CancellationToken cancellationToken)
	{
		var openOrders = new Dictionary<Order, bool>();

		foreach (var m in _markets)
		{
			var isMargin = m == _marginMarket;

			foreach (var order in await _httpClient.GetOpenOrders(m, default, cancellationToken))
				openOrders.Add(order, isMargin);
		}

		return openOrders;
	}

	private ValueTask ProcessOrderAsync(Order order, long transId, long origTransId, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = transId != 0 ? order.CreatedDate : (order.FinishedDate ?? CurrentTime),
			SecurityId = order.Symbol.ToStockSharp(),
			TransactionId = transId,
			OriginalTransactionId = origTransId,
			OrderStringId = order.Id,
			OrderVolume = order.Amount.ToDecimal(),
			Balance = order.GetBalance(),
			Side = order.Type.ToSide(out var isMarket),
			OrderType = isMarket ? OrderTypes.Market : OrderTypes.Limit,
			OrderPrice = (decimal)order.Price,
			PortfolioName = PortfolioName,
			OrderState = order.Status.ToOrderState(),
			MarginMode = order.Kind.EqualsIgnoreCase(_marginMarket) ? MarginModes.Cross : null,
		}, cancellationToken);

		//ProcessOwnTradesAsync(order.Trades, transId == 0 ? origTransId : transId, cancellationToken);
	}

	private async ValueTask ProcessOwnTradesAsync(IEnumerable<OwnTrade> trades, long origTransId, CancellationToken cancellationToken)
	{
		foreach (var trade in trades)
		{
			var side = trade.Side.ToSide(out _);

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = origTransId,
				ServerTime = trade.Timestamp,
				TradeId = trade.Id,
				TradePrice = (decimal)trade.Price,
				TradeVolume = (decimal)trade.Amount,
				Commission = (decimal?)trade.Fee,
				CommissionCurrency = trade.FeeCurrency,
				OriginSide = trade.IsMaker == true ? side : side.Invert(),
			}, cancellationToken);
		}
	}
}