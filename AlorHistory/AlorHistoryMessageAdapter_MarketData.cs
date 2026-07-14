namespace StockSharp.AlorHistory;

public partial class AlorHistoryMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;
		var batch = (int)100L.Min(left);

		foreach (var exchange in _exchanges)
		{
			await foreach (var sec in _httpClient.GetSecurities(
					query: lookupMsg.SecurityId.SecurityCode,
					limit: batch,
					cficode: lookupMsg.CfiCode,
					exchange: exchange,
					format: "heavy",
					includeOptions: secTypes.Contains(SecurityTypes.Option),
					cancellationToken: cancellationToken)
				.WithEnforcedCancellation(cancellationToken))
			{
				var secMsg = new SecurityMessage
				{
					SecurityId = new() { SecurityCode = sec.Symbol, BoardCode = sec.Board, Isin = sec.ISIN },
					ShortName = sec.ShortName,
					Name = sec.Description,
					FaceValue = sec.FaceValue?.ToDecimal(),
					CfiCode = sec.CfiCode,
					PriceStep = sec.PriceStep?.ToDecimal(),
					VolumeStep = sec.MinStep?.ToDecimal(),
					Decimals = sec.RoundTo,
					MinVolume = sec.LotSize?.ToDecimal(),
					Multiplier = sec.PriceMultiplier?.ToDecimal(),
					Currency = sec.Currency.FromMicexCurrencyName(this.AddErrorLog),
					SecurityType = sec.Type.ToSecurityType(),
					OriginalTransactionId = lookupMsg.TransactionId,
					ExpiryDate = sec.Cancellation,
					Strike = sec.StrikePrice?.ToDecimal(),
					OptionType = sec.OptionSide.ToOptionType(),
				};

				if (!sec.UnderlyingSymbol.IsEmpty())
					secMsg.TryFillUnderlyingId(sec.UnderlyingSymbol);

				if (!secMsg.IsMatch(lookupMsg, secTypes))
					continue;

				await SendOutMessageAsync(secMsg, cancellationToken);

				if (--left <= 0)
					break;
			}

			if (left <= 0)
				break;
		}

		await SendSubscriptionFinishedAsync(lookupMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var transId = mdMsg.TransactionId;
		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (!mdMsg.IsSubscribe)
			return;

		var secId = mdMsg.SecurityId;
		var secCode = secId.SecurityCode;
		var exchange = secId.BoardCode;

		if (exchange.IsEmpty() || !_exchanges.Contains(exchange, StringComparer.OrdinalIgnoreCase))
			exchange = _exchanges.FirstOrDefault() ?? "MOEX";

		var tf = mdMsg.GetTimeFrame();
		var from = mdMsg.From?.ToUniversalTime();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var left = mdMsg.Count ?? long.MaxValue;

		if (from is not null)
		{
			var tfNative = tf.ToNative();
			var step = tf.Multiply(10000);

			while (from < to)
			{
				var last = from.Value + step;
				var needBreak = false;

				if (last > to)
				{
					last = to;
					needBreak = true;
				}

				var candles = _httpClient.GetCandles(secCode, exchange, tfNative, (long)from.Value.ToUnix(), (long)last.ToUnix(), cancellationToken);

				await foreach (var candle in candles)
				{
					cancellationToken.ThrowIfCancellationRequested();

					if (candle.Time < from)
						continue;

					if (candle.Time > to)
					{
						needBreak = true;
						break;
					}

					await SendOutMessageAsync(new TimeFrameCandleMessage
					{
						OpenPrice = candle.Open?.ToDecimal() ?? 0,
						ClosePrice = candle.Close?.ToDecimal() ?? 0,
						HighPrice = candle.High?.ToDecimal() ?? 0,
						LowPrice = candle.Low?.ToDecimal() ?? 0,
						TotalVolume = candle.Volume?.ToDecimal() ?? 0,
						OpenTime = candle.Time,
						State = CandleStates.Finished,
						OriginalTransactionId = transId,
					}, cancellationToken);

					if (--left <= 0)
					{
						needBreak = true;
						break;
					}

					from = candle.Time;
				}

				if (needBreak)
					break;
			}
		}

		await SendSubscriptionFinishedAsync(transId, cancellationToken);
	}
}
