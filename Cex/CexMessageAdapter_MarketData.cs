namespace StockSharp.Cex;

public partial class CexMessageAdapter
{
	private readonly Dictionary<SecurityId, long?> _tradesSubscriptions = [];
	private readonly Dictionary<Tuple<SecurityId, TimeSpan>, long> _candleTransactionIds = [];
	private readonly SynchronizedSet<string> _orderBooks = new(StringComparer.InvariantCultureIgnoreCase);

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
				SecurityId = new[] { symbol.Symbol1, symbol.Symbol2 }.ToStockSharp(),
				OriginalTransactionId = lookupMsg.TransactionId,
				MinVolume = symbol.MinLotSize,
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
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var secId = mdMsg.SecurityId;
		var currency = secId.ToCcy();

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		_msgsByTransId.Add(mdMsg.TransactionId, Tuple.Create(mdMsg.Type, secId));

		if (mdMsg.IsSubscribe)
		{
			await _pusherClient.SubscribeOrderBookAsync(currency, mdMsg.MaxDepth ?? 0, mdMsg.TransactionId, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _pusherClient.UnSubscribeOrderBookAsync(currency, mdMsg.TransactionId, mdMsg.OriginalTransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var secId = mdMsg.SecurityId;
		var currency = secId.ToCcy();

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			var latestId = _tradesSubscriptions.TryGetValue(secId);

			if (mdMsg.To != null)
			{
				while (true)
				{
					var trades = await _httpClient.GetTradeHistoryAsync(currency, latestId, cancellationToken);

					var needBreak = true;

					foreach (var trade in trades)
					{
						latestId = trade.Id;
						needBreak = false;

						var time = trade.Time;

						if (mdMsg.From != null && mdMsg.From.Value > time)
							continue;

						if (mdMsg.To != null && mdMsg.To.Value < time)
						{
							needBreak = true;
							break;
						}

						await ProcessTickAsync(mdMsg.TransactionId, secId, trade, cancellationToken);
					}

					if (needBreak)
						break;
				}

				_tradesSubscriptions[secId] = latestId;
			}
			else
				_tradesSubscriptions[secId] = await ProcessTicksSubscriptionAsync(secId, latestId, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var secId = mdMsg.SecurityId;
		var currency = secId.ToCcy();

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		_msgsByTransId.Add(mdMsg.TransactionId, Tuple.Create(mdMsg.Type, secId));

		var tf = mdMsg.GetTimeFrame();
		var tfName = tf.ToNative();

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.To != null)
			{
				var date = (mdMsg.From ?? mdMsg.To.Value).Date;

				if (mdMsg.From == null)
					date = date.AddDays(-2);

				var end = DateTime.UtcNow.Date;

				while (date <= end)
				{
					var dict = await _httpClient.GetCandlesAsync(currency, date, cancellationToken);

					if (!dict.TryGetValue("data" + tfName, out var candles))
					{
						date = date.AddDays(1);
						continue;
					}

					var needBreak = false;

					foreach (var candle in candles)
					{
						if (mdMsg.To != null && candle.Time.FromUnix() > mdMsg.To)
						{
							needBreak = true;
							break;
						}

						await ProcessCandleAsync(candle, mdMsg.SecurityId, tf, mdMsg.TransactionId, cancellationToken);
					}

					if (needBreak)
						break;

					date = date.AddDays(1);
				}

				await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			}
			else
			{
				_candleTransactionIds.Add(Tuple.Create(secId, tf), mdMsg.TransactionId);
				await _pusherClient.SubscribeCandlesAsync(currency, tfName, mdMsg.TransactionId, cancellationToken);

				await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			}
		}
		else
		{
			_candleTransactionIds.Remove(Tuple.Create(secId, tf));
			await _pusherClient.UnSubscribeCandlesAsync(currency, tfName, cancellationToken);
		}
	}

	private ValueTask ProcessTickAsync(long originTransId, SecurityId secId, Native.Model.Trade trade, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = secId,
			TradeId = trade.Id,
			TradePrice = (decimal)trade.Price,
			TradeVolume = trade.Amount.ToDecimal(),
			ServerTime = trade.Time,
			OriginSide = trade.Type.ToSide(),
			OriginalTransactionId = originTransId,
		}, cancellationToken);
	}

	private async ValueTask ProcessSubscriptionsAsync(CancellationToken cancellationToken)
	{
		foreach (var pair in _tradesSubscriptions.ToArray())
		{
			_tradesSubscriptions[pair.Key] = await ProcessTicksSubscriptionAsync(pair.Key, pair.Value, cancellationToken);
		}
	}

	private async ValueTask<long?> ProcessTicksSubscriptionAsync(SecurityId secId, long? latestId, CancellationToken cancellationToken)
	{
		var trades = await _httpClient.GetTradeHistoryAsync(secId.ToCcy(), latestId + 1, cancellationToken);

		foreach (var trade in trades)
		{
			if (latestId != null && trade.Id <= latestId.Value)
				continue;

			await ProcessTickAsync(0, secId, trade, cancellationToken);

			latestId = trade.Id;
		}

		return latestId;
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

	private ValueTask SessionOnOrderBookSnapshot(long transactionId, OrderBook book, CancellationToken cancellationToken)
	{
		return ProcessOrderBookAsync(book, book.Timestamp ?? CurrentTime, cancellationToken);
	}

	private ValueTask SessionOnOrderBookChanged(OrderBook book, CancellationToken cancellationToken)
	{
		return ProcessOrderBookAsync(book, book.Time ?? CurrentTime, cancellationToken);
	}

	private ValueTask ProcessOrderBookAsync(OrderBook book, DateTime time, CancellationToken cancellationToken)
	{
		var state = _orderBooks.TryAdd(book.Symbol) ? QuoteChangeStates.SnapshotComplete : QuoteChangeStates.Increment;

		QuoteChange ToChange(OrderBookEntry entry)
			=> new(entry.Price, entry.Size);

		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = book.Symbol.ToStockSharp(),
			Bids = [.. book.Bids?.Select(ToChange) ?? []],
			Asks = [.. book.Asks?.Select(ToChange) ?? []],
			State = state,
			ServerTime = time,
		}, cancellationToken);
	}

	private ValueTask SessionOnNewCandle(string currencyPair, string timeFrame, Ohlc candle, CancellationToken cancellationToken)
	{
		var secId = currencyPair.ToStockSharp();
		var tf = timeFrame.ToTimeFrame();

		if (!_candleTransactionIds.TryGetValue(Tuple.Create(secId, tf), out var transId))
			return default;

		return ProcessCandleAsync(candle, secId, tf, transId, cancellationToken);
	}

	private ValueTask SessionOnOhlcv24Changed(string currencyPair, Ohlcv24 ohlcv, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			ServerTime = CurrentTime,
			SecurityId = currencyPair.ToStockSharp(),
		}
		.TryAdd(Level1Fields.OpenPrice, ohlcv.Open.ToDecimal())
		.TryAdd(Level1Fields.HighPrice, ohlcv.High.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, ohlcv.Low.ToDecimal())
		.TryAdd(Level1Fields.ClosePrice, ohlcv.Close.ToDecimal())
		.TryAdd(Level1Fields.Volume, ohlcv.Volume.ToDecimal()), cancellationToken);
	}
}
