namespace StockSharp.HitBtc;

partial class HitBtcMessageAdapter
{
	private readonly SynchronizedSet<string> _orderBooks = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly SynchronizedDictionary<long, DateTime?> _tickSubscriptionEndDates = new();

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		var transId = lookupMsg.TransactionId;
		await _pusherClient.RequestSymbolsAsync(transId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transactionId = mdMsg.TransactionId;
		var currency = mdMsg.SecurityId.ToCurrency();

		await SendSubscriptionReplyAsync(transactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			await _pusherClient.SubscribeTickerAsync(currency, transactionId, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _pusherClient.UnSubscribeTickerAsync(currency, transactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transactionId = mdMsg.TransactionId;
		var currency = mdMsg.SecurityId.ToCurrency();

		await SendSubscriptionReplyAsync(transactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			await _pusherClient.SubscribeOrderBookAsync(currency, transactionId, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _pusherClient.UnSubscribeOrderBookAsync(currency, transactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transactionId = mdMsg.TransactionId;
		var currency = mdMsg.SecurityId.ToCurrency();

		await SendSubscriptionReplyAsync(transactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.To != null)
			{
				_tickSubscriptionEndDates.Add(transactionId, mdMsg.To);

				var trades = await _httpClient.GetTradesAsync(currency, "ASC", "timestamp", (long?)mdMsg.From?.ToUnix(), (long?)mdMsg.To?.ToUnix(), mdMsg.Count, null, cancellationToken);

				await ProcessTicksAsync(transactionId, mdMsg.SecurityId, trades, cancellationToken);
			}
			else
				await _pusherClient.SubscribeTradesAsync(currency, transactionId, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await _pusherClient.UnSubscribeTradesAsync(currency, transactionId, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transactionId = mdMsg.TransactionId;
		var currency = mdMsg.SecurityId.ToCurrency();

		await SendSubscriptionReplyAsync(transactionId, cancellationToken);

		var tf = mdMsg.GetTimeFrame();
		var tfName = tf.ToNative();

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.To == null)
				await _pusherClient.SubscribeCandlesAsync(currency, tfName, transactionId, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _pusherClient.UnSubscribeCandlesAsync(currency, tfName, transactionId, cancellationToken);
	}

	private ValueTask SessionOnTickerChanged(Ticker ticker, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ticker.Symbol.ToStockSharp(),
			ServerTime = ticker.Time,
		}
		.TryAdd(Level1Fields.OpenPrice, ticker.Open?.ToDecimal())
		.TryAdd(Level1Fields.HighPrice, ticker.High?.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, ticker.Low?.ToDecimal())
		.TryAdd(Level1Fields.LastTradePrice, ticker.Last?.ToDecimal())
		.TryAdd(Level1Fields.BestBidPrice, ticker.Bid?.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, ticker.Ask?.ToDecimal())
		.TryAdd(Level1Fields.Volume, ticker.Volume?.ToDecimal()), cancellationToken);
	}

	private async ValueTask SessionOnNewTrades(long transactionId, string symbol, IEnumerable<Trade> trades, CancellationToken cancellationToken)
	{
		await ProcessTicksAsync(transactionId, symbol.ToStockSharp(), trades, cancellationToken);

		if (transactionId != 0 && _tickSubscriptionEndDates.TryGetValue(transactionId, out var date))
		{
			if (date == null)
				await _pusherClient.SubscribeTradesAsync(symbol, transactionId, cancellationToken);
			else
				await SendSubscriptionFinishedAsync(transactionId, cancellationToken);
		}
	}

	private async ValueTask ProcessTicksAsync(long transactionId, SecurityId securityId, IEnumerable<Trade> trades, CancellationToken cancellationToken)
	{
		foreach (var trade in trades)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = securityId,
				TradeId = trade.Id,
				TradePrice = trade.Price,
				TradeVolume = trade.Quantity,
				OriginalTransactionId = transactionId,
				ServerTime = CurrentTime,
			}, cancellationToken);
		}
	}

	private ValueTask SessionOnOrderBookChanged(OrderBook book, CancellationToken cancellationToken)
	{
		var state = _orderBooks.TryAdd(book.Symbol) ? QuoteChangeStates.SnapshotComplete : QuoteChangeStates.Increment;

		QuoteChange ToChange(OrderBookEntry entry)
			=> new(entry.Price, entry.Size);

		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = book.Symbol.ToStockSharp(),
			Bids = book.Bids?.Select(ToChange).ToArray() ?? [],
			Asks = book.Asks?.Select(ToChange).ToArray() ?? [],
			State = state,
			ServerTime = CurrentTime,
		}, cancellationToken);
	}

	private ValueTask SessionOnNewCandle(string symbol, string timeFrame, Ohlc candle, CancellationToken cancellationToken)
	{
		return ProcessCandleAsync(symbol, timeFrame, candle, 0, cancellationToken);
	}

	private ValueTask ProcessCandleAsync(string symbol, string timeFrame, Ohlc candle, long originTransId, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = symbol.ToStockSharp(),
			TypedArg = timeFrame.ToTimeFrame(),
			OpenPrice = candle.Open,
			ClosePrice = candle.Close,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			TotalVolume = candle.Volume,
			OpenTime = candle.Time,
			State = CandleStates.Finished,
			OriginalTransactionId = originTransId,
		}, cancellationToken);
	}

	private async ValueTask SessionOnNewSymbols(long transactionId, IEnumerable<Symbol> symbols, CancellationToken cancellationToken)
	{
		foreach (var symbol in symbols)
		{
			await SendOutMessageAsync(new SecurityMessage
			{
				SecurityId = symbol.Id.ToStockSharp(),
				OriginalTransactionId = transactionId,
				SecurityType = SecurityTypes.CryptoCurrency,
				PriceStep = symbol.TickSize,
				VolumeStep = symbol.QuantityIncrement,
			}.TryFillUnderlyingId(symbol.BaseCurrency.ToUpperInvariant()), cancellationToken);

			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = symbol.Id.ToStockSharp(),
				ServerTime = CurrentTime,
			}
			.TryAdd(Level1Fields.CommissionTaker, symbol.TakeLiquidityRate)
			.TryAdd(Level1Fields.CommissionMaker, symbol.ProvideLiquidityRate), cancellationToken);
		}

		await SendSubscriptionFinishedAsync(transactionId, cancellationToken);
	}
}