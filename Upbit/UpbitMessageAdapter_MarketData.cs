namespace StockSharp.Upbit;

partial class UpbitMessageAdapter
{
	private const int _maxCandles = 200;
	private const string _candleTimeFormat = "yyyy-MM-dd HH:mm:ss";

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var symbol in await _httpClient.GetSymbolsAsync(cancellationToken))
		{
			var secMsg = new SecurityMessage
			{
				SecurityId = symbol.Market.ToStockSharp(),
				OriginalTransactionId = lookupMsg.TransactionId,
			}.FillDefaultCryptoFields();

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
		var transId = mdMsg.TransactionId;
		var symbol = mdMsg.SecurityId.ToSymbol();

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			await _pusherClient.SubscribeTickerAsync(transId, symbol, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _pusherClient.UnSubscribeTickerAsync(transId, symbol, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;
		var symbol = mdMsg.SecurityId.ToSymbol();

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			await _pusherClient.SubscribeTradesAsync(transId, symbol, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _pusherClient.UnSubscribeTradesAsync(transId, symbol, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;
		var symbol = mdMsg.SecurityId.ToSymbol();

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			await _pusherClient.SubscribeOrderBookAsync(transId, symbol, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _pusherClient.UnSubscribeOrderBookAsync(transId, symbol, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;
		var secId = mdMsg.SecurityId;
		var symbol = secId.ToSymbol();

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (!mdMsg.IsSubscribe)
			return;

		var tf = mdMsg.GetTimeFrame();
		var tfName = tf.ToNative();

		string resolution = null;

		if (int.TryParse(tfName, out var num))
		{
			tfName = "minutes";
			resolution = num.To<string>();
		}

		var from = mdMsg.From;
		var to = mdMsg.To ?? CurrentTime;
		var left = mdMsg.Count ?? long.MaxValue;

		// neither a start bound nor a count means the newest candles are enough
		var isSinglePage = from is null && mdMsg.Count is null;

		var received = new List<Ohlc>();

		// Upbit returns the newest candles preceding the exclusive 'to' bound,
		// so the requested range is walked backwards page by page
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var candles = (await _httpClient.GetCandlesAsync(symbol, tfName, resolution, to.FromDateTime(_candleTimeFormat), _maxCandles, cancellationToken))
				.OrderByDescending(c => c.DateTimeUtc)
				.ToArray();

			if (candles.Length == 0)
				break;

			var isFinished = candles.Length < _maxCandles;

			foreach (var candle in candles)
			{
				if (from != null && candle.DateTimeUtc < from.Value)
				{
					isFinished = true;
					break;
				}

				received.Add(candle);

				if (--left <= 0)
				{
					isFinished = true;
					break;
				}
			}

			if (isFinished || isSinglePage)
				break;

			to = candles[^1].DateTimeUtc;
		}

		for (var i = received.Count - 1; i >= 0; i--)
			await ProcessCandleAsync(received[i], secId, tf, transId, cancellationToken);

		await SendSubscriptionFinishedAsync(transId, cancellationToken);
	}

	private ValueTask ProcessCandleAsync(Ohlc candle, SecurityId securityId, TimeSpan timeFrame, long originTransId, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = securityId,
			TypedArg = timeFrame,
			OpenPrice = candle.Open.ToDecimal() ?? 0,
			ClosePrice = candle.Close.ToDecimal() ?? 0,
			HighPrice = candle.High.ToDecimal() ?? 0,
			LowPrice = candle.Low.ToDecimal() ?? 0,
			TotalVolume = candle.Volume.ToDecimal() ?? 0,
			OpenTime = candle.DateTimeUtc,
			State = CandleStates.Finished,
			OriginalTransactionId = originTransId,
		}, cancellationToken);
	}

	private ValueTask SessionOnTickerChanged(Ticker ticker, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ticker.Code.ToStockSharp(),
			ServerTime = ticker.Timestamp,
		}
		.TryAdd(Level1Fields.OpenPrice, (decimal?)ticker.OpeningPrice)
		.TryAdd(Level1Fields.HighPrice, (decimal?)ticker.HighPrice)
		.TryAdd(Level1Fields.LowPrice, (decimal?)ticker.LowPrice)
		.TryAdd(Level1Fields.Change, (decimal?)ticker.ChangePrice)
		.TryAdd(Level1Fields.LastTradeVolume, (decimal?)ticker.TradeVolume)
		.TryAdd(Level1Fields.LastTradePrice, (decimal?)ticker.TradePrice)
		.TryAdd(Level1Fields.LastTradeTime, ticker.TradeTimestamp)
		.TryAdd(Level1Fields.LastTradeOrigin, ticker.AskBid.ToOriginSide())
		.TryAdd(Level1Fields.Volume, ticker.AccTradeVolume24H?.ToDecimal()), cancellationToken);
	}

	private ValueTask SessionOnOrderBookChanged(OrderBook book, CancellationToken cancellationToken)
	{
		var bids = new List<QuoteChange>();
		var asks = new List<QuoteChange>();

		foreach (var unit in book.Units)
		{
			var bidPrice = (decimal)unit.BidPrice;
			var askPrice = (decimal)unit.AskPrice;

			var bidSize = (decimal)unit.BidSize;
			var askSize = (decimal)unit.AskSize;

			bids.Add(new QuoteChange(bidPrice, bidSize));
			asks.Add(new QuoteChange(askPrice, askSize));
		}

		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = book.Code.ToStockSharp(),
			ServerTime = book.Timestamp,
			Bids = [.. bids],
			Asks = [.. asks],
		}, cancellationToken);
	}

	private ValueTask SessionOnNewTrade(Native.Model.Trade trade, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = trade.Code.ToStockSharp(),
			ServerTime = trade.TradeTimestamp,
			TradeId = trade.SequentialId,
			TradePrice = (decimal)trade.TradePrice,
			TradeVolume = (decimal)trade.TradeVolume,
			OriginSide = trade.AskBid.ToOriginSide(),
		}, cancellationToken);
	}
}
