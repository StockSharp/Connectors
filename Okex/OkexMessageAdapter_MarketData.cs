namespace StockSharp.Okex;

public partial class OkexMessageAdapter
{
	private readonly SynchronizedDictionary<(SecurityId, TimeSpan), long> _candleTransactions = [];
	private readonly SecurityTypes[] _instTypes = [SecurityTypes.Future, SecurityTypes.CryptoCurrency, SecurityTypes.Swap];

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken token)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, token);
		
		var secTypes = lookupMsg.GetSecurityTypes();

		if (secTypes.IsEmpty())
			secTypes.AddRange(_instTypes);

		var left = lookupMsg.Count ?? long.MaxValue;

		string underlying;

		if (secTypes.Contains(SecurityTypes.Option))
		{
			if(secTypes.Count != 1 || lookupMsg.GetUnderlyingCode().IsEmpty())
				throw new InvalidOperationException($"you must lookup options with a single {nameof(SecurityLookupMessage)}.{nameof(SecurityLookupMessage.SecurityType)}={SecurityTypes.Option}, and {nameof(SecurityLookupMessage)}.{nameof(SecurityLookupMessage.UnderlyingSecurityId)}=<UNDERLYING_CODE>");

			underlying = lookupMsg.UnderlyingSecurityId.ToNative();
			secTypes.Clear();
			secTypes.Add(SecurityTypes.Option);
		}
		else
		{
			underlying = null;
		}

		foreach (var secType in secTypes)
		{
			token.ThrowIfCancellationRequested();

			var instruments = await _httpClient.GetInstrumentsAsync(secType.ToNative(), secType == SecurityTypes.Option, underlying, token);

			foreach (var instrument in instruments)
			{
				token.ThrowIfCancellationRequested();

				var secMsg = new SecurityMessage
				{
					SecurityId = instrument.Id.ToStockSharp(),
					OriginalTransactionId = lookupMsg.TransactionId,
					SecurityType = instrument.InstType.ToSecurityType(),
					PriceStep = instrument.TickSize,
					MinVolume = instrument.MinSize,
					VolumeStep = instrument.MinSize,
					IssueDate = (instrument.Listing ?? 0).TryFromUnix(false),
					ExpiryDate = (instrument.Delivery ?? 0).TryFromUnix(false),
					OptionType = instrument.OptionType.To<OptionTypes?>(),
					Strike = instrument.StrikePrice,
				}.TryFillUnderlyingId(instrument.Underlying?.ToStockSharp().SecurityCode);

				if (!secMsg.IsMatch(lookupMsg, secTypes))
					continue;

				await SendOutMessageAsync(secMsg, token);

				if (--left <= 0)
					break;
			}

			if (left <= 0)
				break;
		}

		//if (!lookupMsg.IsHistoryOnly())
		//	await _publicPusherClient.SubscribeInstruments(lookupMsg.TransactionId, token);

		await SendSubscriptionResultAsync(lookupMsg, token);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var instrumentId = mdMsg.SecurityId.ToNative();

		var tf = mdMsg.GetTimeFrame();
		var tfNative = tf.ToNative(false) ?? throw SubscriptionResponseMessage.NotSupported;

		var key = (mdMsg.SecurityId, tf);

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.From is not null)
			{
				var from = mdMsg.From.Value;
				var to = mdMsg.To ?? DateTime.UtcNow;
				var left = mdMsg.Count ?? long.MaxValue;
				var bar = tf.ToNative();
				//var isHistory = instrument.InstType.ToSecurityType().IsHistoryCandlesSupported();

				while (from < to)
				{
					var needBreak = false;
					var end = from + tf.Multiply(100);
					var candles = (await _httpClient.GetCandlesAsync(instrumentId, /*isHistory*/true, bar, from, end, cancellationToken)).ToArray();

					foreach (var candle in candles.OrderBy(c => c.Time))
					{
						cancellationToken.ThrowIfCancellationRequested();

						var time = candle.Time.FromUnix(false);

						if (time < from)
							continue;

						if (time > to)
						{
							needBreak = true;
							break;
						}

						await ProcessCandle(candle, mdMsg.SecurityId, tf, mdMsg.TransactionId, cancellationToken);

						if (--left <= 0)
						{
							needBreak = true;
							break;
						}
					}

					if (needBreak)
						break;

					await IterationInterval.Delay(cancellationToken);
					from = end;
				}
			}

			if (!mdMsg.IsHistoryOnly())
			{
				_candleTransactions[key] = mdMsg.TransactionId;
				await _businessPusherClient.SubscribeCandles(mdMsg.TransactionId, instrumentId, tf.ToNative(), cancellationToken);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_candleTransactions.Remove(key);
			await _businessPusherClient.UnsubscribeCandles(mdMsg.OriginalTransactionId, instrumentId, tf.ToNative(), cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var instrumentId = mdMsg.SecurityId.ToNative();

		if (mdMsg.IsSubscribe)
		{
			if (!mdMsg.IsHistoryOnly())
				await _publicPusherClient.SubscribeLevel1(mdMsg.TransactionId, instrumentId, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _publicPusherClient.UnsubscribeLevel1(mdMsg.OriginalTransactionId, instrumentId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var instrumentId = mdMsg.SecurityId.ToNative();

		if (mdMsg.IsSubscribe)
		{
			if (!mdMsg.IsHistoryOnly())
				await _businessPusherClient.SubscribeTicks(mdMsg.TransactionId, instrumentId, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _businessPusherClient.UnsubscribeTicks(mdMsg.OriginalTransactionId, instrumentId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var instrumentId = mdMsg.SecurityId.ToNative();

		if (mdMsg.IsSubscribe)
		{
			if (!mdMsg.IsHistoryOnly())
				await _publicPusherClient.SubscribeDepth(mdMsg.TransactionId, instrumentId, mdMsg.MaxDepth, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _publicPusherClient.UnsubscribeDepth(mdMsg.OriginalTransactionId, instrumentId, mdMsg.MaxDepth, cancellationToken);
	}

	private ValueTask ProcessCandle(Ohlc candle, SecurityId securityId, TimeSpan timeFrame, long originTransId, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = securityId,
			TypedArg = timeFrame,
			OpenPrice = candle.Open,
			ClosePrice = candle.Close,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			TotalVolume = candle.Volume,
			OpenTime = candle.Time.FromUnix(false),
			State = candle.Comfirm.ToCandleState(),
			OriginalTransactionId = originTransId
		}, cancellationToken);
	}

	private ValueTask SessionOnLevel1Received(Ticker ticker, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ticker.InstrumentId.ToStockSharp(),
			ServerTime = ticker.Timestamp,
		}
		.TryAdd(Level1Fields.OpenPrice,      ticker.Open24h)
		.TryAdd(Level1Fields.HighPrice,      ticker.High24h)
		.TryAdd(Level1Fields.LowPrice,       ticker.Low24h)
		.TryAdd(Level1Fields.LastTradePrice, ticker.Last)
		.TryAdd(Level1Fields.BestBidPrice,   ticker.BestBidPrice)
		.TryAdd(Level1Fields.BestBidVolume,  ticker.BestBidSize)
		.TryAdd(Level1Fields.BestAskPrice,   ticker.BestAskPrice)
		.TryAdd(Level1Fields.BestAskVolume,  ticker.BestAskSize)
		.TryAdd(Level1Fields.Volume,         ticker.Vol24h > 0 ? ticker.Vol24h : ticker.VolCcy24h), cancellationToken);
	}

	private ValueTask SessionOnTickReceived(OkexTick tick, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = tick.InstrumentId.ToStockSharp(),
			TradeId = tick.Id.To<long>(),
			TradePrice = tick.Price,
			TradeVolume = tick.Size,
			ServerTime = tick.Time,
			OriginSide = tick.Side.ToSide(),
		}, cancellationToken);
	}

	private ValueTask SessionOnOrderBookReceived(string instrumentId, QuoteChangeStates? state, OrderBook book, CancellationToken cancellationToken)
	{
		var secId = instrumentId.ToStockSharp();

		static QuoteChange ToChange(OrderBookEntry entry)
			=> new(entry.Price, entry.Size, entry.OrdersCount);

		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = secId,
			Bids = book.Bids?.Select(ToChange).ToArray() ?? [],
			Asks = book.Asks?.Select(ToChange).ToArray() ?? [],
			State = state,
			ServerTime = book.Timestamp,
		}, cancellationToken);
	}

	private ValueTask SessionOnCandleReceived(string instrumentId, TimeSpan timeFrame, Ohlc candle, CancellationToken cancellationToken)
	{
		var secId = instrumentId.ToStockSharp();

		if (!_candleTransactions.TryGetValue((secId, timeFrame), out var transId))
			return default;

		return ProcessCandle(candle, secId, timeFrame, transId, cancellationToken);
	}
}
