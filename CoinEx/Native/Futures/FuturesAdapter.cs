namespace StockSharp.CoinEx.Native.Futures;

using System.Runtime.CompilerServices;

using StockSharp.CoinEx.Native.Futures.Model;

class FuturesAdapter : NativeAdapter
{
	private readonly HttpClient _httpClient;
	private readonly SocketClient _socketClient;
	private long _bestTransId;

	public FuturesAdapter(Authenticator authenticator, IdGenerator transIdGen, int attempts, WorkingTime workingTime)
		: base(authenticator, transIdGen, BoardCodes.CoinExFT)
	{
		_httpClient = new(authenticator) { Parent = this };
		_socketClient = new(authenticator, attempts, transIdGen, workingTime) { Parent = this };

		SubscribePusherClient();
	}

	protected override void DisposeManaged()
	{
		UnsubscribePusherClient();

		_httpClient.Dispose();
		_socketClient.Dispose();

		base.DisposeManaged();
	}

	protected override string PortfolioName => base.PortfolioName + $"_{nameof(Futures)}";

	private void SubscribePusherClient()
	{
		_socketClient.StateChanged += SendOutConnectionStateAsync;
		_socketClient.Error += SendOutErrorAsync;
		//_socketClient.SubscriptionReply += SendSubscriptionReplyAsync;
		_socketClient.TickersReceived += SessionOnTickersReceived;
		_socketClient.TicksReceived += SessionOnTicksReceived;
		_socketClient.BestReceived += SessionOnBestReceived;
		_socketClient.OrderBookReceived += SessionOnOrderBookReceived;
		_socketClient.BalanceReceived += SessionOnBalanceReceived;
		_socketClient.PositionReceived += SessionOnPositionReceived;
		_socketClient.OrderReceived += SessionOnOrderReceived;
		_socketClient.DealReceived += SessionOnDealReceived;
	}

	private void UnsubscribePusherClient()
	{
		_socketClient.StateChanged -= SendOutConnectionStateAsync;
		_socketClient.Error -= SendOutErrorAsync;
		//_socketClient.SubscriptionReply -= SendSubscriptionReplyAsync;
		_socketClient.TickersReceived -= SessionOnTickersReceived;
		_socketClient.TicksReceived -= SessionOnTicksReceived;
		_socketClient.BestReceived -= SessionOnBestReceived;
		_socketClient.OrderBookReceived -= SessionOnOrderBookReceived;
		_socketClient.BalanceReceived -= SessionOnBalanceReceived;
		_socketClient.PositionReceived -= SessionOnPositionReceived;
		_socketClient.OrderReceived -= SessionOnOrderReceived;
		_socketClient.DealReceived -= SessionOnDealReceived;
	}

	/// <inheritdoc />
	public override ValueTask ConnectAsync(CancellationToken cancellationToken)
		=> _socketClient.Connect(cancellationToken);

	/// <inheritdoc />
	public override void Disconnect()
		=> _socketClient.Disconnect();

	/// <inheritdoc />
	public override ValueTask Time(TimeMessage timeMsg, CancellationToken cancellationToken)
		=> _socketClient.SendPing(GetNextId(), cancellationToken);

	public override async IAsyncEnumerable<SecurityMessage> SecurityLookup(SecurityLookupMessage lookupMsg, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		foreach (var symbol in await _httpClient.GetSymbols(cancellationToken))
		{
			yield return new SecurityMessage
			{
				SecurityId = GetSecId(symbol.Market),
				MinVolume = symbol.MinAmount?.ToDecimal(),
				OriginalTransactionId = lookupMsg.TransactionId,
				Decimals = symbol.BaseCurrencyPrecision,
				SecurityType = SecurityTypes.Future,
			}.TryFillUnderlyingId(symbol.BaseCurrency);
		}
	}

	public override async ValueTask Level1(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToSymbol();

		if (mdMsg.IsSubscribe)
		{
			await _socketClient.SubscribeTicker(mdMsg.TransactionId, symbol, cancellationToken);
			await _socketClient.SubscribeBest(_bestTransId = GetNextId(), symbol, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await _socketClient.UnSubscribeBest(mdMsg.TransactionId, mdMsg.OriginalTransactionId, symbol, cancellationToken);
			await _socketClient.UnSubscribeTicker(GetNextId(), _bestTransId, symbol, cancellationToken);
		}
	}

	public override async ValueTask OrderBook(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToSymbol();

		if (mdMsg.IsSubscribe)
		{
			await _socketClient.SubscribeOrderBook(mdMsg.TransactionId, symbol, mdMsg.MaxDepth ?? 10, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _socketClient.UnSubscribeOrderBook(mdMsg.TransactionId, mdMsg.OriginalTransactionId, symbol, cancellationToken);
	}

	public override async ValueTask Ticks(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToSymbol();

		if (mdMsg.IsSubscribe)
		{
			await _socketClient.SubscribeTicks(mdMsg.TransactionId, symbol, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _socketClient.UnSubscribeTicks(mdMsg.TransactionId, mdMsg.OriginalTransactionId, symbol, cancellationToken);
	}

	public override async ValueTask TFCandles(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToSymbol();

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.From is DateTime from)
			{
				var to = mdMsg.To ?? DateTime.UtcNow;
				var left = mdMsg.Count ?? long.MaxValue;

				var candles = await _httpClient.GetCandles(symbol, mdMsg.GetTimeFrame().ToNative(), 1000, cancellationToken);

				foreach (var candle in candles.OrderBy(c => c.Time))
				{
					if (candle.Time < from)
						continue;

					if (candle.Time > to)
						break;

					await SendOutMessageAsync(new TimeFrameCandleMessage
					{
						OpenPrice = (decimal)candle.Open,
						ClosePrice = (decimal)candle.Close,
						HighPrice = (decimal)candle.High,
						LowPrice = (decimal)candle.Low,
						TotalVolume = (decimal)candle.Volume,
						OpenTime = candle.Time,
						State = CandleStates.Finished,
						OriginalTransactionId = mdMsg.TransactionId,
					}, cancellationToken);

					if (--left <= 0)
						break;
				}
			}

			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		}
	}

	public override async ValueTask RegisterOrder(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var symbol = regMsg.SecurityId.ToSymbol();

		switch (regMsg.OrderType)
		{
			case null:
			case OrderTypes.Limit:
			case OrderTypes.Market:
				break;
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		var isMarket = regMsg.OrderType == OrderTypes.Market;
		var price = regMsg.OrderType == OrderTypes.Market ? (decimal?)null : regMsg.Price;

		var order = await _httpClient.RegisterOrder(regMsg.TransactionId, symbol, regMsg.Side.ToNative(), regMsg.ToOrderType(), price, regMsg.Volume, regMsg.VisibleVolume is not null, cancellationToken);

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			OrderId = order.Id,
			ServerTime = CurrentTime,
			OriginalTransactionId = regMsg.TransactionId,
			OrderState = isMarket ? OrderStates.Done : OrderStates.Active,
			Balance = isMarket ? 0 : null,
			HasOrderInfo = true,
		}, cancellationToken);
	}

	public override async ValueTask CancelOrder(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		if (cancelMsg.OrderId == null)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		await _httpClient.CancelOrder(cancelMsg.SecurityId.ToSymbol(), cancelMsg.OrderId.Value, cancellationToken);
	}

	public override async ValueTask ReplaceOrder(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		if (replaceMsg.OldOrderId == null)
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(replaceMsg.OriginalTransactionId));

		await _httpClient.ModifyOrder(replaceMsg.SecurityId.ToSymbol(), replaceMsg.OldOrderId.Value, replaceMsg.MarginMode is not null, replaceMsg.ToOrderType(), replaceMsg.Price, replaceMsg.Volume, cancellationToken);
	}

	public override async ValueTask PortfolioLookup(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		if (lookupMsg == null)
			throw new ArgumentNullException(nameof(lookupMsg));

		if (!lookupMsg.IsSubscribe)
		{
			await _socketClient.UnSubscribeBalance(lookupMsg.TransactionId, lookupMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		await SendOutMessageAsync(new PortfolioMessage
		{
			PortfolioName = PortfolioName,
			BoardCode = BoardCodes.CoinEx,
			OriginalTransactionId = lookupMsg.TransactionId
		}, cancellationToken);

		var balances = await _httpClient.GetBalance(cancellationToken);

		if (balances is not null)
			await ProcessBalancesAsync(lookupMsg.TransactionId, balances, cancellationToken);

		var positions = await _httpClient.GetPositions(100, cancellationToken);

		foreach (var position in positions)
			await ProcessPositionAsync(lookupMsg.TransactionId, position, cancellationToken);

		if (!lookupMsg.IsHistoryOnly())
			await _socketClient.SubscribeBalance(lookupMsg.TransactionId, cancellationToken);
	}

	public override async ValueTask OrderStatus(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		if (statusMsg == null)
			throw new ArgumentNullException(nameof(statusMsg));

		if (!statusMsg.IsSubscribe)
			await _socketClient.UnSubscribeOrders(statusMsg.TransactionId, statusMsg.OriginalTransactionId, cancellationToken);

		foreach (var order in await _httpClient.GetOpenOrders(100, cancellationToken))
		{
			if (!long.TryParse(order.ClientId, out var transId))
				continue;

			await ProcessOrderAsync(transId, statusMsg.TransactionId, order, OrderStates.Active, cancellationToken);
		}

		if (!statusMsg.IsHistoryOnly())
		{
			await _socketClient.SubscribeDeals(statusMsg.TransactionId, cancellationToken);
			await _socketClient.SubscribeOrders(GetNextId(), cancellationToken);
		}
	}

	private ValueTask ProcessOrderAsync(long transId, long originTransId, Order order, OrderStates state, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Transactions,
			HasOrderInfo = true,
			ServerTime = transId != 0 ? order.CreatedAt : order.UpdatedAt,
			SecurityId = GetSecId(order.Symbol),
			TransactionId = transId,
			OriginalTransactionId = originTransId,
			OrderType = order.Type.ToOrderType(out var postOnly, out var tif),
			PostOnly = postOnly,
			TimeInForce = tif,
			OrderId = order.Id,
			OrderVolume = order.Volume?.ToDecimal(),
			Balance = (decimal)order.Left,
			Side = order.Type.ToSide(),
			OrderPrice = order.Price?.ToDecimal() ?? 0,
			PortfolioName = PortfolioName,
			OrderState = state,
			Commission = order.Fee?.ToDecimal(),
		}, cancellationToken);
	}

	private async ValueTask ProcessBalancesAsync(long transId, IEnumerable<Balance> balances, CancellationToken cancellationToken)
	{
		foreach (var balance in balances)
		{
			await SendOutMessageAsync(new PositionChangeMessage
			{
				PortfolioName = PortfolioName,
				SecurityId = GetSecId(balance.Currency),
				ServerTime = CurrentTime,
				OriginalTransactionId = transId,
			}
			.TryAdd(PositionChangeTypes.CurrentValue, (decimal?)balance.Available, true)
			.TryAdd(PositionChangeTypes.BlockedValue, (decimal?)balance.Frozen, true)
			.TryAdd(PositionChangeTypes.UnrealizedPnL, (decimal?)balance.UnrealizedPnl, true)
			.TryAdd(PositionChangeTypes.VariationMargin, (decimal?)balance.Margin, true)
			, cancellationToken);
		}
	}

	private ValueTask SessionOnBalanceReceived(IEnumerable<Balance> balances, CancellationToken cancellationToken)
	{
		return ProcessBalancesAsync(0, balances, cancellationToken);
	}

	private ValueTask ProcessPositionAsync(long transId, Position position, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new PositionChangeMessage
		{
			PortfolioName = PortfolioName,
			SecurityId = GetSecId(position.Symbol),
			ServerTime = CurrentTime,
			OriginalTransactionId = transId,
		}
		.TryAdd(PositionChangeTypes.CurrentValue, (decimal?)position.CmlPositionValue, true)
		.TryAdd(PositionChangeTypes.UnrealizedPnL, (decimal?)position.UnrealizedPnl)
		.TryAdd(PositionChangeTypes.RealizedPnL, (decimal?)position.RealizedPnl)
		.TryAdd(PositionChangeTypes.AveragePrice, (decimal?)position.AvgEntryPrice)
		.TryAdd(PositionChangeTypes.VariationMargin, (decimal?)position.MarginAvailable)
		.TryAdd(PositionChangeTypes.LiquidationPrice, (decimal?)position.LiquidationPrice)
		.TryAdd(PositionChangeTypes.SettlementPrice, (decimal?)position.SettlePrice)
		, cancellationToken);
	}

	private ValueTask SessionOnPositionReceived(Position position, CancellationToken cancellationToken)
	{
		return ProcessPositionAsync(0, position, cancellationToken);
	}

	private ValueTask SessionOnOrderReceived(Order order, string eventType, CancellationToken cancellationToken)
	{
		if (!long.TryParse(order.ClientId, out var transId))
			return default;

		return ProcessOrderAsync(0, transId, order, eventType.ToOrderState(), cancellationToken);
	}

	private ValueTask SessionOnDealReceived(Deal deal, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage()
		{
			DataTypeEx = DataType.Transactions,
			ServerTime = deal.CreatedAt,
			TradeId = deal.DealId,
			OrderId = deal.OrderId,
			OriginalTransactionId = long.TryParse(deal.ClientId, out var transId) ? transId : 0,
			TradePrice = (decimal)deal.Price,
			TradeVolume = (decimal)deal.Amount,
			Commission = deal.Fee?.ToDecimal(),
			CommissionCurrency = deal.FeeCcy,
			IsMarketMaker = deal.Role.EqualsIgnoreCase("maker"),
		}, cancellationToken);
	}

	private async ValueTask SessionOnTickersReceived(IEnumerable<Ticker> tickers, CancellationToken cancellationToken)
	{
		var timestamp = CurrentTime;

		foreach (var ticker in tickers)
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = GetSecId(ticker.Market),
				ServerTime = timestamp,
			}
			.TryAdd(Level1Fields.OpenPrice, (decimal?)ticker.Open)
			.TryAdd(Level1Fields.HighPrice, (decimal?)ticker.High)
			.TryAdd(Level1Fields.LowPrice, (decimal?)ticker.Low)
			.TryAdd(Level1Fields.ClosePrice, (decimal?)ticker.Close)
			.TryAdd(Level1Fields.Volume, (decimal?)ticker.Volume)
			.TryAdd(Level1Fields.LastTradePrice, (decimal?)ticker.Last)
			.TryAdd(Level1Fields.BidsVolume, (decimal?)ticker.VolumeBuy)
			.TryAdd(Level1Fields.AsksVolume, (decimal?)ticker.VolumeSell)
			, cancellationToken);
		}
	}

	private async ValueTask SessionOnTicksReceived(string symbol, IEnumerable<Tick> trades, CancellationToken cancellationToken)
	{
		foreach (var trade in trades)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = GetSecId(symbol),
				ServerTime = trade.Time,
				TradeId = trade.Id,
				TradePrice = trade.Price,
				TradeVolume = trade.Amount,
				OriginSide = trade.Side.ToSide(),
			}, cancellationToken);
		}
	}

	private ValueTask SessionOnBestReceived(Best best, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			ServerTime = best.UpdatedAt,
			SecurityId = GetSecId(best.Market),
		}
		.TryAdd(Level1Fields.BestBidPrice, (decimal?)best.BidPrice)
		.TryAdd(Level1Fields.BestBidVolume, (decimal?)best.BidSize)
		.TryAdd(Level1Fields.BestAskPrice, (decimal?)best.AskPrice)
		.TryAdd(Level1Fields.BestAskVolume, (decimal?)best.AskSize)
		, cancellationToken);
	}

	private ValueTask SessionOnOrderBookReceived(string symbol, bool isSnapshot, OrderBook book, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			ServerTime = book.Time,
			SecurityId = GetSecId(symbol),
			State = isSnapshot ? QuoteChangeStates.SnapshotComplete : QuoteChangeStates.Increment,
			Bids = book.Bids?.Select(e => new QuoteChange((decimal)e.Price, (decimal)e.Size)).ToArray() ?? [],
			Asks = book.Asks?.Select(e => new QuoteChange((decimal)e.Price, (decimal)e.Size)).ToArray() ?? [],
		}, cancellationToken);
	}
}