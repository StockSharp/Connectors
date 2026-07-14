namespace StockSharp.Bitbank;

public partial class BitbankMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var ticker in await _httpClient.GetTickersAsync(cancellationToken))
		{
			var secMsg = ticker.Pair.ToStockSharp().FillDefaultCryptoFields();
			secMsg.OriginalTransactionId = lookupMsg.TransactionId;

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
		var currency = mdMsg.SecurityId.ToSymbol();

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			await _pusherClient.SubscribeTickerAsync(currency, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _pusherClient.UnSubscribeTickerAsync(currency, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var currency = mdMsg.SecurityId.ToSymbol();

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.From != null)
			{
				var date = mdMsg.From.Value;
				var to = mdMsg.To ?? DateTime.UtcNow;

				while (date <= to)
				{
					var trades = await _httpClient.GetTransactionsAsync(currency, date, cancellationToken);

					foreach (var tick in trades.OrderBy(t => t.ExecutedAt))
					{
						await ProcessTickAsync(mdMsg.TransactionId, mdMsg.SecurityId, tick, cancellationToken);
					}

					date = date.AddDays(1);
				}
			}

			if (!mdMsg.IsHistoryOnly())
				await _pusherClient.SubscribeTradesAsync(currency, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await _pusherClient.UnSubscribeTradesAsync(currency, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var currency = mdMsg.SecurityId.ToSymbol();

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			await _pusherClient.SubscribeOrderBookAsync(currency, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _pusherClient.UnSubscribeOrderBookAsync(currency, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var currency = mdMsg.SecurityId.ToSymbol();

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var tf = mdMsg.GetTimeFrame();
		var tfName = tf.ToNative();

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.To != null)
			{
				var date = mdMsg.From.Value;

				while (date <= mdMsg.To.Value)
				{
					var candles = await _httpClient.GetCandlesAsync(currency, tfName, date, cancellationToken);

					foreach (var candle in candles.OrderBy(c => c.Time))
					{
						await ProcessCandleAsync(candle, mdMsg.SecurityId, tf, mdMsg.TransactionId, cancellationToken);
					}

					date = date.AddDays(1);
				}
			}

			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
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
			OpenTime = candle.Time.FromUnix(false),
			State = CandleStates.Finished,
			OriginalTransactionId = originTransId,
		}, cancellationToken);
	}

	private ValueTask SessionOnTickerChanged(string currencyPair, Ticker ticker, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = currencyPair.ToStockSharp(),
			ServerTime = ticker.Time,
		}
		.TryAdd(Level1Fields.HighPrice, ticker.High?.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, ticker.Low?.ToDecimal())
		.TryAdd(Level1Fields.LastTradePrice, ticker.Last?.ToDecimal())
		.TryAdd(Level1Fields.BestBidPrice, ticker.Buy?.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, ticker.Sell?.ToDecimal())
		.TryAdd(Level1Fields.Volume, ticker.Volume?.ToDecimal()), cancellationToken);
	}

	private ValueTask SessionOnNewTrade(string currencyPair, Trade trade, CancellationToken cancellationToken)
	{
		var secId = currencyPair.ToStockSharp();

		return ProcessTickAsync(0L, secId, trade, cancellationToken);
	}

	private ValueTask ProcessTickAsync(long originalTransId, SecurityId secId, Trade trade, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = secId,
			TradeId = trade.Id,
			TradePrice = trade.Price.ToDecimal(),
			TradeVolume = trade.Amount.ToDecimal(),
			ServerTime = trade.ExecutedAt,
			OriginSide = trade.Side.ToSide(),
			OriginalTransactionId = originalTransId,
		}, cancellationToken);
	}

	private ValueTask SessionOnOrderBookChanged(string currencyPair, OrderBook book, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = currencyPair.ToStockSharp(),
			Bids = [.. book.Bids.Select(e => new QuoteChange((decimal)e.Price, (decimal)e.Size))],
			Asks = [.. book.Asks.Select(e => new QuoteChange((decimal)e.Price, (decimal)e.Size))],
			ServerTime = book.Timestamp,
		}, cancellationToken);
	}
}
