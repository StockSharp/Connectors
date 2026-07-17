namespace StockSharp.XOpenHub;

public partial class XOpenHubMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var types = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var native in _symbols.Values.OrderBy(s => s.Symbol))
		{
			var security = new SecurityMessage
			{
				SecurityId = ToSecurityId(native.Symbol),
				Name = native.Description,
				SecurityType = ToSecurityType(native),
				PriceStep = native.Digits > 0 ? 1m / (decimal)Math.Pow(10, native.Digits) :
					native.TickSize is > 0 ? native.TickSize : null,
				VolumeStep = native.VolumeStep > 0 ? native.VolumeStep : null,
				MinVolume = native.MinVolume > 0 ? native.MinVolume : null,
				MaxVolume = native.MaxVolume > 0 ? native.MaxVolume : null,
				Multiplier = native.ContractSize > 0 ? native.ContractSize : null,
				Currency = native.Currency?.FromMicexCurrencyName(this.AddErrorLog),
				Class = native.Category.IsEmpty(native.Group),
				ExpiryDate = native.Expiration is > 0
					? DateTimeOffset.FromUnixTimeMilliseconds(native.Expiration.Value).UtcDateTime
					: null,
				OriginalTransactionId = lookupMsg.TransactionId,
			};
			if (!security.IsMatch(lookupMsg, types))
				continue;
			await SendOutMessageAsync(security, cancellationToken);
			if (--left <= 0)
				break;
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (mdMsg.IsSubscribe)
		{
			var symbol = ResolveSymbol(mdMsg.SecurityId);
			_level1Subscriptions[mdMsg.TransactionId] = symbol.Symbol;
			await SendTick(mdMsg.TransactionId, new()
			{
				Symbol = symbol.Symbol,
				Ask = symbol.Ask,
				Bid = symbol.Bid,
				High = symbol.High,
				Low = symbol.Low,
				Timestamp = symbol.Time,
			}, cancellationToken);
			if (_level1Subscriptions.ToArray().Count(p =>
				p.Value.EqualsIgnoreCase(symbol.Symbol)) == 1)
				await _stream.SubscribeTicks(symbol.Symbol, cancellationToken);
		}
		else if (_level1Subscriptions.TryGetValue(mdMsg.OriginalTransactionId, out var symbol))
		{
			_level1Subscriptions.Remove(mdMsg.OriginalTransactionId);
			if (!_level1Subscriptions.ToArray().Any(p => p.Value.EqualsIgnoreCase(symbol)))
				await _stream.UnsubscribeTicks(symbol, cancellationToken);
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			if (_candleSubscriptions.TryGetValue(mdMsg.OriginalTransactionId, out var subscription))
			{
				_candleSubscriptions.Remove(mdMsg.OriginalTransactionId);
				if (!_candleSubscriptions.ToArray().Any(p =>
					p.Value.Symbol.EqualsIgnoreCase(subscription.Symbol)))
					await _stream.UnsubscribeCandles(subscription.Symbol, cancellationToken);
			}
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}

		var symbol = ResolveSymbol(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		if (!mdMsg.IsHistoryOnly() && timeFrame != TimeSpan.FromMinutes(1))
			throw new NotSupportedException(
				"X Open Hub publishes live candles only at the native one-minute interval.");

		var period = ToPeriod(timeFrame);
		var to = new DateTimeOffset((mdMsg.To ?? DateTime.UtcNow).ToUniversalTime());
		var from = new DateTimeOffset((mdMsg.From ??
			to.UtcDateTime.Subtract(timeFrame.Multiply(500))).ToUniversalTime());
		var data = await _command.GetChartRange(symbol.Symbol, period, from, to, cancellationToken);
		var divider = (decimal)Math.Pow(10, data?.Digits ?? symbol.Digits);
		var left = mdMsg.Count ?? long.MaxValue;
		foreach (var rate in (data?.Rates ?? []).OrderBy(r => r.Time))
		{
			var openTime = DateTimeOffset.FromUnixTimeMilliseconds(rate.Time);
			var open = rate.Open / divider;
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				SecurityId = ToSecurityId(symbol.Symbol),
				OpenTime = openTime.UtcDateTime,
				OpenPrice = open,
				HighPrice = (rate.Open + rate.HighDelta) / divider,
				LowPrice = (rate.Open + rate.LowDelta) / divider,
				ClosePrice = (rate.Open + rate.CloseDelta) / divider,
				TotalVolume = rate.Volume,
				TypedArg = timeFrame,
				State = openTime + timeFrame <= DateTimeOffset.UtcNow
					? CandleStates.Finished : CandleStates.Active,
				OriginalTransactionId = mdMsg.TransactionId,
			}, cancellationToken);
			if (--left <= 0)
				break;
		}

		if (!mdMsg.IsHistoryOnly())
		{
			_candleSubscriptions[mdMsg.TransactionId] = new()
			{
				Symbol = symbol.Symbol,
				TimeFrame = timeFrame,
			};
			if (_candleSubscriptions.ToArray().Count(p =>
				p.Value.Symbol.EqualsIgnoreCase(symbol.Symbol)) == 1)
				await _stream.SubscribeCandles(symbol.Symbol, cancellationToken);
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask ProcessTick(XApiTick tick, CancellationToken cancellationToken)
	{
		if (_symbols.TryGetValue(tick.Symbol, out var symbol))
		{
			symbol.Ask = tick.Ask;
			symbol.Bid = tick.Bid;
			symbol.High = tick.High;
			symbol.Low = tick.Low;
			symbol.Time = tick.Timestamp;
		}
		foreach (var pair in _level1Subscriptions.ToArray()
			.Where(p => p.Value.EqualsIgnoreCase(tick.Symbol)))
			await SendTick(pair.Key, tick, cancellationToken);
	}

	private ValueTask SendTick(long transactionId, XApiTick tick,
		CancellationToken cancellationToken)
	{
		var time = tick.Timestamp > 0
			? DateTimeOffset.FromUnixTimeMilliseconds(tick.Timestamp).UtcDateTime
			: DateTime.UtcNow;
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ToSecurityId(tick.Symbol),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, tick.Bid)
		.TryAdd(Level1Fields.BestBidVolume, tick.BidVolume)
		.TryAdd(Level1Fields.BestAskPrice, tick.Ask)
		.TryAdd(Level1Fields.BestAskVolume, tick.AskVolume)
		.TryAdd(Level1Fields.HighPrice, tick.High)
		.TryAdd(Level1Fields.LowPrice, tick.Low), cancellationToken);
	}

	private async ValueTask ProcessCandle(XApiStreamCandle candle,
		CancellationToken cancellationToken)
	{
		foreach (var pair in _candleSubscriptions.ToArray().Where(p =>
			p.Value.Symbol.EqualsIgnoreCase(candle.Symbol)))
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				SecurityId = ToSecurityId(candle.Symbol),
				OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(candle.Time).UtcDateTime,
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				TypedArg = pair.Value.TimeFrame,
				State = CandleStates.Finished,
				OriginalTransactionId = pair.Key,
			}, cancellationToken);
		}
	}

	private static SecurityTypes ToSecurityType(XApiSymbol symbol)
	{
		if (symbol.IsCurrencyPair)
			return SecurityTypes.Currency;
		var value = symbol.Category.IsEmpty(symbol.Group);
		if (value.ContainsIgnoreCase("crypto")) return SecurityTypes.CryptoCurrency;
		if (value.ContainsIgnoreCase("index")) return SecurityTypes.Index;
		if (value.ContainsIgnoreCase("future")) return SecurityTypes.Future;
		if (value.ContainsIgnoreCase("commodity")) return SecurityTypes.Commodity;
		if (value.ContainsIgnoreCase("ETF")) return SecurityTypes.Etf;
		return SecurityTypes.Stock;
	}

	private static int ToPeriod(TimeSpan timeFrame)
	{
		var minutes = timeFrame.TotalMinutes;
		if (minutes is 1 or 5 or 15 or 30 or 60 or 240 or 1440 or 10080 or 43200)
			return (int)minutes;
		throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
			"X Open Hub does not support this candle interval.");
	}
}
