namespace StockSharp.DXtrade;

public partial class DXtradeMessageAdapter
{
	private readonly SynchronizedDictionary<(string symbol, string tf), long> _candleTransIds = [];

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		var instruments = await _httpClient.GetInstruments(lookupMsg.SecurityId.SecurityCode, lookupMsg.SecurityType?.ToNative(), cancellationToken);

		foreach (var instrument in instruments)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var secMsg = new SecurityMessage
			{
				SecurityId = instrument.Symbol.ToStockSharp(),
				Name = instrument.Description,
				SecurityType = instrument.Type.ToSecurityType(),
				PriceStep = instrument.PriceIncrement?.ToDecimal(),
				VolumeStep = instrument.LotSize?.ToDecimal(),
				Multiplier = instrument.Multiplier?.ToDecimal(),
				Currency = instrument.Currency.FromMicexCurrencyName(this.AddErrorLog),
				Class = instrument.Product,
				OriginalTransactionId = lookupMsg.TransactionId,
			}.TryFillUnderlyingId(instrument.Underlying);

			if (!secMsg.IsMatch(lookupMsg, secTypes))
				continue;

			await SendOutMessageAsync(secMsg, cancellationToken);

			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToNative();

		if (mdMsg.IsSubscribe)
		{
			var account = await EnsureGetAccount(cancellationToken);
			var timeFrame = mdMsg.GetTimeFrame().ToNative();
			var left = mdMsg.Count ?? long.MaxValue;

			if (mdMsg.From is DateTime from)
			{
				var to = mdMsg.To ?? DateTime.UtcNow;
				var maxCandles = 10000;

				while (from < to)
				{
					var candles = await _httpClient.GetCandles(account, symbol, timeFrame, from.ToTimeStamp(), to.ToTimeStamp(), maxCandles, cancellationToken);
					var needBreak = true;
					var last = from;

					foreach (var candle in candles.OrderBy(c => c.Time).Where(c => c.Time >= from))
					{
						if (candle.Time > to)
						{
							needBreak = true;
							break;
						}

						await ProcessCandle(mdMsg.TransactionId, candle, CandleStates.Finished, cancellationToken);

						needBreak = false;
						last = candle.Time;

						if (--left <= 0)
						{
							needBreak = true;
							break;
						}
					}

					if (needBreak || candles.Length < maxCandles)
						break;

					from = last;
				}
			}

			if (!mdMsg.IsHistoryOnly())
			{
				_candleTransIds.Add((symbol, timeFrame), mdMsg.TransactionId);

				await _publicClient.SubscribeCandles(
					mdMsg.TransactionId,
					account,
					symbol,
					timeFrame,
					DateTime.UtcNow.ToTimeStamp(),
					default,
					cancellationToken);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await _publicClient.Unsubscribe(
				mdMsg.TransactionId,
				mdMsg.OriginalTransactionId,
				cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var symbol = mdMsg.SecurityId.ToNative();

		if (mdMsg.IsSubscribe)
		{
			var account = await EnsureGetAccount(cancellationToken);
			var from = mdMsg.From;

			if (from is not null)
			{
				var to = mdMsg.To ?? DateTime.UtcNow;

				var quotes = await _httpClient.GetQuotes(account, symbol, from.Value.ToTimeStamp(), to.ToTimeStamp(), default, cancellationToken);

				foreach (var quote in quotes)
					await ProcessQuote(mdMsg.TransactionId, quote, cancellationToken);
			}

			if (!mdMsg.IsHistoryOnly())
			{
				await _publicClient.SubscribeQuotes(
					mdMsg.TransactionId,
					account,
					symbol,
					default,
					default,
					cancellationToken);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await _publicClient.Unsubscribe(
				mdMsg.TransactionId,
				mdMsg.OriginalTransactionId,
				cancellationToken);
		}
	}

	private ValueTask ProcessCandle(long transId, Candle candle, CandleStates state, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OpenPrice = candle.Open?.ToDecimal() ?? 0,
			HighPrice = candle.High?.ToDecimal() ?? 0,
			LowPrice = candle.Low?.ToDecimal() ?? 0,
			ClosePrice = candle.Close?.ToDecimal() ?? 0,
			TotalVolume = candle.Volume?.ToDecimal() ?? 0,
			OpenTime = candle.Time,
			OriginalTransactionId = transId,
			State = state,
		}, cancellationToken);
	}

	private ValueTask ProcessQuote(long transId, Quote quote, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = quote.Symbol.ToStockSharp(),
			ServerTime = quote.Time,
			OriginalTransactionId = transId,
		}
		.TryAdd(Level1Fields.BestBidPrice, quote.Bid?.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, quote.Ask?.ToDecimal())
		, cancellationToken);
	}

	private ValueTask SessionOnQuoteReceived(Quote quote, CancellationToken cancellationToken)
	{
		return ProcessQuote(0, quote, cancellationToken);
	}

	private ValueTask SessionOnCandleReceived(Candle candle, CancellationToken cancellationToken)
	{
		if (_candleTransIds.TryGetValue((candle.Symbol, candle.CandleType), out var transId))
			return ProcessCandle(transId, candle, CandleStates.Active, cancellationToken);
		return default;
	}
}
