namespace StockSharp.Digifinex;

public partial class DigifinexMessageAdapter
{
	private readonly SynchronizedSet<string> _orderBooks = new(StringComparer.InvariantCultureIgnoreCase);
	private const int _maxHistCount = 500;

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var symbol in (await _httpClient.GetSpotSymbols(cancellationToken)).Concat(await _httpClient.GetMarginSymbols(cancellationToken)))
		{
			var secMsg = new SecurityMessage
			{
				SecurityId = symbol.Code.ToStockSharp(),
				Decimals = symbol.PricePrecision,
				MinVolume = symbol.MinimumAmount.ToDecimal(),
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityType = SecurityTypes.CryptoCurrency,
			}.TryFillUnderlyingId(symbol.BaseAsset);

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
		var symbol = mdMsg.SecurityId.ToCurrency();
		var transId = mdMsg.TransactionId;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			await _pusherClient.SubscribeTicker(transId, symbol, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _pusherClient.UnSubscribeTicker(mdMsg.OriginalTransactionId, transId, symbol, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var symbol = mdMsg.SecurityId.ToCurrency();
		var transId = mdMsg.TransactionId;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			var left = mdMsg.Count ?? long.MaxValue;

			if (mdMsg.From is DateTime from)
			{
				var to = mdMsg.To ?? DateTime.UtcNow;

				foreach (var trade in (await _httpClient.GetTrades(symbol, _maxHistCount, cancellationToken))
					.Where(t =>
					{
						var time = t.GetTime();

						if (time < from)
							return false;

						if (time > to)
							return false;

						return true;
					}))
				{
					await ProcessTradeAsync(mdMsg.SecurityId, trade, cancellationToken);

					if (--left <= 0)
						break;
				}
			}

			if (!mdMsg.IsHistoryOnly())
				await _pusherClient.SubscribeTrades(transId, symbol, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _pusherClient.UnSubscribeTrades(mdMsg.OriginalTransactionId, transId, symbol, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var symbol = mdMsg.SecurityId.ToCurrency();
		var transId = mdMsg.TransactionId;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			await _pusherClient.SubscribeOrderBook(transId, symbol, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _pusherClient.UnSubscribeOrderBook(mdMsg.OriginalTransactionId, transId, symbol, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var symbol = mdMsg.SecurityId.ToCurrency();
		var transId = mdMsg.TransactionId;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

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
					var end = last + tf.Multiply(_maxHistCount);
					var candles = await _httpClient.GetCandles(symbol, tfName, (long)last.ToUnix(), (long)end.ToUnix(), cancellationToken);

					foreach (var candle in candles)
					{
						await ProcessCandleAsync(candle, mdMsg.SecurityId, tf, mdMsg.TransactionId, cancellationToken);

						if (--left <= 0)
							break;
					}

					if (left <= 0)
						break;

					last = end + tf;
				}
			}

			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		}
	}

	private ValueTask ProcessTradeAsync(SecurityId secId, Trade trade, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = secId,
			ServerTime = trade.GetTime(),
			TradeId = trade.Id,
			TradePrice = (decimal)trade.Price,
			TradeVolume = (decimal)trade.Amount,
			OriginSide = trade.Type.ToSide(out _),
		}, cancellationToken);
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
			OpenTime = candle.Time.FromUnix(),
			State = CandleStates.Finished,
			OriginalTransactionId = originTransId,
		}, cancellationToken);
	}

	private ValueTask SessionOnOrderBookChanged(string symbol, OrderBook book, CancellationToken cancellationToken)
	{
		var state = _orderBooks.TryAdd(symbol) ? QuoteChangeStates.SnapshotComplete : QuoteChangeStates.Increment;

		static QuoteChange ToChange(OrderBookEntry entry)
			=> new(entry.Price, entry.Size);

		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(),
			Bids = book.Bids?.Select(ToChange).ToArray() ?? [],
			Asks = book.Asks?.Select(ToChange).ToArray() ?? [],
			State = state,
			ServerTime = CurrentTime,
		}, cancellationToken);
	}

	private async ValueTask SessionOnNewTrades(string symbol, IEnumerable<Trade> trades, CancellationToken cancellationToken)
	{
		var secId = symbol.ToStockSharp();

		foreach (var trade in trades)
		{
			await ProcessTradeAsync(secId, trade, cancellationToken);
		}
	}

	private ValueTask SessionOnTickerReceived(Ticker ticker, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ticker.Symbol.ToStockSharp(),
			ServerTime = ticker.Timestamp,
		}
		.TryAdd(Level1Fields.OpenPrice, ticker.Open24h?.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, ticker.Low24h?.ToDecimal())
		.TryAdd(Level1Fields.LastTradePrice, ticker.Last?.ToDecimal())
		.TryAdd(Level1Fields.LastTradeVolume, ticker.LastQty?.ToDecimal())
		.TryAdd(Level1Fields.Volume, ticker.BaseVolume24h?.ToDecimal())
		.TryAdd(Level1Fields.BestBidPrice, ticker.BestBid?.ToDecimal())
		.TryAdd(Level1Fields.BestBidVolume, ticker.BestBidSize?.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, ticker.BestAsk?.ToDecimal())
		.TryAdd(Level1Fields.BestAskVolume, ticker.BestAskSize?.ToDecimal())
		, cancellationToken);
	}
}
