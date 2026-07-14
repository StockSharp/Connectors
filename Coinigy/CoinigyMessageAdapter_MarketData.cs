namespace StockSharp.Coinigy;

public partial class CoinigyMessageAdapter
{
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
				SecurityId = new SecurityId
				{
					SecurityCode = symbol.MarketName,
					BoardCode = symbol.ExchCode.ToBoardCode(),
				},
				SecurityType = SecurityTypes.CryptoCurrency,
				PriceStep = symbol.QuotePricePrecision.GetPriceStep(),
				VolumeStep = symbol.QuoteQuantityPrecision.GetPriceStep(),
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
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		if (!mdMsg.SecurityId.BoardCode.IsSupportedExchange())
		{
			await SendSubscriptionNotSupportedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		mdMsg.SecurityId.ToCurrency(out var baseCurr, out var quoteCurr, out var exchange);

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			await _pusherClient.SubscribeTradesAsync(baseCurr, quoteCurr, exchange, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			await _pusherClient.UnSubscribeTradesAsync(baseCurr, quoteCurr, exchange, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		if (!mdMsg.SecurityId.BoardCode.IsSupportedExchange())
		{
			await SendSubscriptionNotSupportedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		mdMsg.SecurityId.ToCurrency(out var baseCurr, out var quoteCurr, out var exchange);

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			await _pusherClient.SubscribeOrderBookAsync(baseCurr, quoteCurr, exchange, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _pusherClient.UnSubscribeOrderBookAsync(baseCurr, quoteCurr, exchange, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		if (!mdMsg.SecurityId.BoardCode.IsSupportedExchange())
		{
			await SendSubscriptionNotSupportedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		mdMsg.SecurityId.ToCurrency(out var baseCurr, out var quoteCurr, out var exchange);

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var tf = mdMsg.GetTimeFrame();
		var tfName = tf.ToNative();

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.From is DateTime from)
			{
				var to = mdMsg.To ?? DateTime.UtcNow;
				var left = mdMsg.Count ?? long.MaxValue;

				var candles = await _httpClient.GetCandlesAsync(baseCurr, quoteCurr, exchange, tfName, from, to, cancellationToken);

				foreach (var candle in candles.OrderBy(c => c.TimeStart))
				{
					var time = candle.TimeStart;

					if (time < from)
						continue;

					if (time > to)
						break;

					await ProcessCandleAsync(candle, mdMsg.SecurityId, tf, mdMsg.TransactionId, cancellationToken);

					if (--left <= 0)
						break;
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
			OpenTime = candle.TimeStart,
			State = CandleStates.Finished,
			OriginalTransactionId = originTransId,
		}, cancellationToken);
	}
}
