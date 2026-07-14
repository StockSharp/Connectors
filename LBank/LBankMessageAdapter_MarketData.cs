namespace StockSharp.LBank;

partial class LBankMessageAdapter
{
	private readonly SynchronizedPairSet<(SecurityId, TimeSpan), long> _candlesTransactions = new();

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
				SecurityId = symbol.Code.ToStockSharp(),
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
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var pair = mdMsg.SecurityId.ToCurrency();

		if (mdMsg.IsSubscribe)
		{
			if (!mdMsg.IsHistoryOnly())
				await _pusherClient.SubscribeTicker(mdMsg.IsSubscribe, pair, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var pair = mdMsg.SecurityId.ToCurrency();

		if (mdMsg.IsSubscribe)
		{
			if (!mdMsg.IsHistoryOnly())
				await _pusherClient.SubscribeOrderBook(mdMsg.IsSubscribe, pair, mdMsg.MaxDepth ?? SupportedOrderBookDepths.Max(), cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var secId = mdMsg.SecurityId;
		var pair = secId.ToCurrency();

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.To != null)
			{
				const int size = 600;

				foreach (var trade in (await _httpClient.GetTradesAsync(pair, size, (long)mdMsg.From.Value.ToUnix(), cancellationToken: cancellationToken))
					.Where(t =>
					{
						var time = t.Time;

						if (time < mdMsg.From.Value)
							return false;

						if (time > mdMsg.To.Value)
							return false;

						return true;
					}))
				{
					await SendOutMessageAsync(new ExecutionMessage
					{
						DataTypeEx = DataType.Ticks,
						SecurityId = secId,
						ServerTime = trade.Time,
						TradePrice = (decimal)trade.Price,
						TradeVolume = (decimal)trade.Amount,
						OriginSide = trade.Type.ToSide(),
					}, cancellationToken);
				}
			}

			if (!mdMsg.IsHistoryOnly())
				await _pusherClient.SubscribeTrades(mdMsg.IsSubscribe, pair, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await _pusherClient.SubscribeTrades(mdMsg.IsSubscribe, pair, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var secId = mdMsg.SecurityId;
		var pair = secId.ToCurrency();
		var tf = mdMsg.GetTimeFrame();

		if (mdMsg.IsSubscribe)
		{
			var to = mdMsg.To;

			if (to != null)
			{
				var from = mdMsg.From.Value;

				while (true)
				{
					var candles = (await _httpClient.GetCandlesAsync(pair, tf.ToNative(false), 2880, (long)from.ToUnix(), cancellationToken)).ToArray();

					if (candles.Length == 0)
						break;

					var hasNew = false;
					var needBreak = false;

					foreach (var candle in candles.OrderBy(c => c.Time))
					{
						var time = candle.Time.FromUnix();

						if (time > to)
						{
							needBreak = true;
							break;
						}
						else if (time < from)
							continue;

						hasNew = true;
						from = time;
						await ProcessCandleAsync(candle, secId, tf, mdMsg.TransactionId, cancellationToken);
					}

					if (needBreak || !hasNew)
						break;

					from += tf;
				}
			}

			if (!mdMsg.IsHistoryOnly())
			{
				_candlesTransactions[(secId, tf)] = mdMsg.TransactionId;
				await _pusherClient.SubscribeCandles(mdMsg.IsSubscribe, pair, tf.ToNative(true), cancellationToken);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_candlesTransactions.RemoveByValue(mdMsg.OriginalTransactionId);
			await _pusherClient.SubscribeCandles(mdMsg.IsSubscribe, pair, tf.ToNative(true), cancellationToken);
		}
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
			State = CandleStates.Active,
			OriginalTransactionId = originTransId,
		}, cancellationToken);
	}

	private ValueTask SessionOnTickerChanged(string pair, DateTime time, SocketTicker ticker, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = pair.ToStockSharp(),
			ServerTime = time,
		}
		.TryAdd(Level1Fields.Turnover, (decimal?)ticker.Turnover)
		.TryAdd(Level1Fields.LastTradeOrigin, ticker.Dir.IsEmpty() ? null : ticker.Dir.ToSide())
		.TryAdd(Level1Fields.LastTradePrice, (decimal?)ticker.Latest)
		.TryAdd(Level1Fields.HighPrice, (decimal?)ticker.High)
		.TryAdd(Level1Fields.LowPrice, (decimal?)ticker.Low)
		.TryAdd(Level1Fields.Change, (decimal?)ticker.Change)
		.TryAdd(Level1Fields.Volume, (decimal?)ticker.Vol), cancellationToken);
	}

	private ValueTask SessionOnNewCandle(string pair, DateTime time, SocketOhlc candle, CancellationToken cancellationToken)
	{
		var secId = pair.ToStockSharp();
		var tf = candle.Slot.ToTimeFrame();

		if (!_candlesTransactions.TryGetValue((secId, tf), out var transId))
			return default;

		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = secId,
			TypedArg = tf,
			OpenPrice = (decimal)candle.Open,
			ClosePrice = (decimal)candle.Close,
			HighPrice = (decimal)candle.High,
			LowPrice = (decimal)candle.Low,
			TotalVolume = (decimal)candle.Volume,
			OpenTime = candle.Time,
			State = CandleStates.Active,
			OriginalTransactionId = transId,
			TotalTicks = candle.TradesCount,
		}, cancellationToken);
	}

	private ValueTask SessionOnOrderBookChanged(string pair, DateTime time, OrderBook book, CancellationToken cancellationToken)
	{
		var secId = pair.ToStockSharp();

		QuoteChange ToQuoteChange(OrderBookEntry entry)
			=> new((decimal)entry.Price, (decimal)entry.Size);

		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = secId,
			ServerTime = time,
			Bids = book.Bids.Select(ToQuoteChange).ToArray(),
			Asks = book.Asks.Select(ToQuoteChange).ToArray(),
		}, cancellationToken);
	}

	private ValueTask SessionOnNewTrade(string pair, DateTime time, SocketTrade trade, CancellationToken cancellationToken)
	{
		var secId = pair.ToStockSharp();

		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = secId,
			ServerTime = time,
			TradePrice = (decimal)trade.Price,
			TradeVolume = (decimal)trade.Volume,
			OriginSide = trade.Direction.ToSide(),
		}, cancellationToken);
	}
}
