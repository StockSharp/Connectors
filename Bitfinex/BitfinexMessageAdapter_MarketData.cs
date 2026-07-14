namespace StockSharp.Bitfinex;

public partial class BitfinexMessageAdapter
{
	private readonly SynchronizedDictionary<(SecurityId, TimeSpan), long> _candlesTransactions = [];

	private const int _maxCandlesFetch = 5000;
	private const int _maxTicksFetch = 5000;

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var symbol in await _httpClient.GetSymbolDetails(cancellationToken))
		{
			var secMsg = new SecurityMessage
			{
				SecurityId = symbol.Pair.ToStockSharp(),
				SecurityType = SecurityTypes.CryptoCurrency,
				Decimals = 8/*symbol.PricePrecision*/,
				VolumeStep = 0.00000001m,
				MinVolume = symbol.MinimumOrderSize.ToDecimal(),
				OriginalTransactionId = lookupMsg.TransactionId,
			};

			if (!secMsg.IsMatch(lookupMsg, secTypes))
				continue;

			await SendOutMessageAsync(secMsg, cancellationToken);

			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToCurrency();

		if (mdMsg.IsSubscribe)
		{
			await _pusherClient.SubscribeTicker(symbol, mdMsg.TransactionId, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _pusherClient.UnSubscribeTicker(symbol, mdMsg.OriginalTransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToCurrency();

		if (mdMsg.IsSubscribe)
		{
			await _pusherClient.SubscribeOrderBook(symbol, mdMsg.MaxDepth, mdMsg.TransactionId, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _pusherClient.UnSubscribeOrderBook(symbol, mdMsg.OriginalTransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToCurrency();

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.From is DateTime from)
			{
				var to = mdMsg.To ?? DateTime.UtcNow;
				var left = mdMsg.Count ?? long.MaxValue;

				var ids = new HashSet<long>();

				while (true)
				{
					var trades = (await _httpClient.GetTrades(symbol, (long)from.ToUnix(false), null, _maxTicksFetch, cancellationToken)).ToArray();

					if (trades.IsEmpty())
					{
						from = from.AddDays(1);

						if (from > to)
							break;

						await IterationInterval.Delay(cancellationToken);
						continue;
					}

					var needFinish = false;
					var hasNew = false;

					foreach (var trade in trades.OrderBy(t => t.Time))
					{
						if (trade.Id == null)
							continue;

						var timestamp = trade.Time.FromUnix(false);

						if (timestamp > to)
						{
							needFinish = true;
							break;
						}

						if (timestamp <= from)
							continue;

						if (!ids.Add(trade.Id.Value))
							continue;

						hasNew = true;
						await ProcessTick(mdMsg.TransactionId, mdMsg.SecurityId, trade, cancellationToken);
						from = timestamp;

						if (--left <= 0)
						{
							needFinish = true;
							break;
						}
					}

					if (needFinish)
						break;
					else if (!hasNew)
						from += TimeSpan.FromMilliseconds(1);

					await IterationInterval.Delay(cancellationToken);
				}
			}

			if (!mdMsg.IsHistoryOnly())
				await _pusherClient.SubscribeTrades(symbol, mdMsg.TransactionId, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await _pusherClient.UnSubscribeTrades(symbol, mdMsg.OriginalTransactionId, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToCurrency();

		var tf = mdMsg.GetTimeFrame();
		var tfName = tf.ToNative();

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.From is DateTime from)
			{
				var to = mdMsg.To ?? DateTime.UtcNow;
				var left = mdMsg.Count ?? long.MaxValue;

				var last = from;

				while (last < to)
				{
					var candles = (await _httpClient.GetCandles(symbol, tfName, (long)last.ToUnix(false), null, _maxCandlesFetch, cancellationToken)).ToArray();

					if (candles.IsEmpty())
					{
						last = last.AddDays(1);

						await IterationInterval.Delay(cancellationToken);
						continue;
					}

					var needFinish = false;
					var hasNew = false;

					foreach (var ohlc in candles.OrderBy(t => t.Time))
					{
						var timestamp = ohlc.Time.FromUnix(false);

						if (timestamp > to)
						{
							needFinish = true;
							break;
						}

						if (timestamp <= last)
							continue;

						hasNew = true;
						await ProcessCandle(mdMsg.TransactionId, mdMsg.SecurityId, tf, ohlc, CandleStates.Active, cancellationToken);
						last = timestamp;

						if (--left <= 0)
						{
							needFinish = true;
							break;
						}
					}

					if (!hasNew || needFinish)
						break;

					last += tf;
					await IterationInterval.Delay(cancellationToken);
				}
			}

			if (!mdMsg.IsHistoryOnly())
			{
				_candlesTransactions[(mdMsg.SecurityId, tf)] = mdMsg.TransactionId;
				await _pusherClient.SubscribeCandles(symbol, tfName, mdMsg.TransactionId, cancellationToken);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await _pusherClient.UnSubscribeCandles(symbol, tfName, mdMsg.OriginalTransactionId, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnOrderLogSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToCurrency();

		if (mdMsg.IsSubscribe)
		{
			await _pusherClient.SubscribeOrderLog(symbol, mdMsg.MaxDepth, mdMsg.TransactionId, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _pusherClient.UnSubscribeOrderLog(symbol, mdMsg.OriginalTransactionId, cancellationToken);
	}

	private async ValueTask SessionOnNewTrade(string pair, Trade trade, CancellationToken cancellationToken)
	{
		await ProcessTick(0, pair.ToStockSharp(), trade, cancellationToken);
	}

	private async ValueTask ProcessTick(long originTransId, SecurityId secId, Trade trade, CancellationToken cancellationToken)
	{
		var time = trade.Time.FromUnix(false);

		// SEQ is different from canonical ID. Websocket server uses SEQ strings to push trades with low latency.
		// After a te message you receive shortly a tu message that contains the real trade ID.

		if (trade.Id == null)
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = secId,
				ServerTime = time,
			}
			.Add(Level1Fields.LastTradePrice, trade.Price)
			.Add(Level1Fields.LastTradeVolume, trade.Amount.Abs())
			.Add(Level1Fields.LastTradeOrigin, trade.Amount > 0 ? Sides.Buy : Sides.Sell), cancellationToken);
		}
		else
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = secId,
				ServerTime = time,
				TradeId = trade.Id,
				TradePrice = trade.Price.ToDecimal(),
				TradeVolume = trade.Amount.Abs().ToDecimal(),
				OriginSide = trade.Amount > 0 ? Sides.Buy : Sides.Sell,
				OriginalTransactionId = originTransId,
			}, cancellationToken);
		}
	}

	private ValueTask SessionOnOrderBookSnaphot(string pair, IEnumerable<Tuple<decimal, int, decimal>> changes, CancellationToken cancellationToken)
	{
		var bids = new List<QuoteChange>();
		var asks = new List<QuoteChange>();

		foreach (var change in changes)
		{
			var isBid = change.Item3 > 0;

			(isBid ? bids : asks).Add(new QuoteChange(change.Item1, change.Item3.Abs()));
		}

		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = pair.ToStockSharp(),
			Bids = [.. bids],
			Asks = [.. asks],
			State = QuoteChangeStates.SnapshotComplete,
			ServerTime = CurrentTime,
		}, cancellationToken);
	}

	private ValueTask SessionOnOrderBookIncrement(string pair, decimal price, int count, decimal amount, CancellationToken cancellationToken)
	{
		var quote = new QuoteChange(price, count == 0 ? 0 : amount.Abs());

		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = pair.ToStockSharp(),
			Bids = amount > 0 ? [quote] : [],
			Asks = amount <= 0 ? [quote] : [],
			State = QuoteChangeStates.Increment,
			ServerTime = CurrentTime,
		}, cancellationToken);
	}

	private ValueTask SessionOnTickerChanged(string pair, Ticker ticker, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = pair.ToStockSharp(),
			ServerTime = CurrentTime,
		}
		.TryAdd(Level1Fields.BestBidPrice, ticker.Bid?.ToDecimal())
		.TryAdd(Level1Fields.BestBidVolume, ticker.BidSize?.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, ticker.Ask?.ToDecimal())
		.TryAdd(Level1Fields.BestAskVolume, ticker.AskSize?.ToDecimal())
		.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice?.ToDecimal())
		.TryAdd(Level1Fields.Volume, ticker.Volume?.ToDecimal())
		.TryAdd(Level1Fields.HighPrice, ticker.High?.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, ticker.Low?.ToDecimal())
		.TryAdd(Level1Fields.Change, ticker.DailyChange?.ToDecimal()), cancellationToken);
	}

	private ValueTask SessionOnNewOrderLog(string pair, OrderLog ol, CancellationToken cancellationToken)
	{
		var price = ol.Price.ToDecimal() ?? 0;

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.OrderLog,
			SecurityId = pair.ToStockSharp(),
			ServerTime = CurrentTime,
			OrderId = ol.Id,
			OrderPrice = price,
			OrderVolume = ol.Amount.Abs().ToDecimal() ?? 0,
			Side = ol.Amount > 0 ? Sides.Buy : Sides.Sell,
			OrderState = price == 0 ? OrderStates.Done : OrderStates.Active,
		}, cancellationToken);
	}

	private ValueTask SessionOnNewCandle(string symbol, string timeFrame, Ohlc candle, CancellationToken cancellationToken)
	{
		var secId = symbol.ToStockSharp();
		var tf = timeFrame.ToTimeFrame();

		if (!_candlesTransactions.TryGetValue((secId, tf), out var transId))
			return default;

		return ProcessCandle(transId, secId, tf, candle, CandleStates.Active, cancellationToken);
	}

	private ValueTask ProcessCandle(long originTransId, SecurityId secId, TimeSpan timeframe, Ohlc candle, CandleStates state, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = secId,
			TypedArg = timeframe,
			OpenTime = candle.Time.FromUnix(false),
			OpenPrice = candle.Open.ToDecimal() ?? 0,
			HighPrice = candle.High.ToDecimal() ?? 0,
			LowPrice = candle.Low.ToDecimal() ?? 0,
			ClosePrice = candle.Close.ToDecimal() ?? 0,
			TotalVolume = candle.Volume.ToDecimal() ?? 0,
			OriginalTransactionId = originTransId,
			State = state,
		}, cancellationToken);
	}
}
