namespace StockSharp.MatchTrader;

public partial class MatchTraderMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var types = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var instrument in _instruments.CachedValues.DistinctBy(i => i.Symbol).OrderBy(i => i.Symbol))
		{
			var security = new SecurityMessage
			{
				SecurityId = ToSecurityId(instrument.Symbol),
				Name = instrument.Description.IsEmpty(instrument.Alias),
				SecurityType = ToSecurityType(instrument.Type),
				PriceStep = instrument.SizeOfOnePoint > 0 ? instrument.SizeOfOnePoint :
					instrument.PricePrecision > 0 ? 1m / (decimal)Math.Pow(10, instrument.PricePrecision) : null,
				VolumeStep = instrument.VolumeStep > 0 ? instrument.VolumeStep : null,
				MinVolume = instrument.VolumeMin > 0 ? instrument.VolumeMin : null,
				MaxVolume = instrument.VolumeMax > 0 ? instrument.VolumeMax : null,
				Multiplier = instrument.ContractSize > 0 ? instrument.ContractSize : null,
				Currency = instrument.QuoteCurrency?.FromMicexCurrencyName(this.AddErrorLog),
				Class = instrument.Type,
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
			var instrument = ResolveInstrument(mdMsg.SecurityId);
			_level1Subscriptions[mdMsg.TransactionId] = instrument.Symbol;
			await RefreshQuotes(cancellationToken, mdMsg.TransactionId);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_level1Subscriptions.Remove(mdMsg.OriginalTransactionId);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			_candleSubscriptions.Remove(mdMsg.OriginalTransactionId);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}

		var instrument = ResolveInstrument(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		var interval = ToInterval(timeFrame);
		var to = new DateTimeOffset((mdMsg.To ?? DateTime.UtcNow).ToUniversalTime());
		var from = new DateTimeOffset((mdMsg.From ?? to.UtcDateTime.Subtract(timeFrame.Multiply(500)))
			.ToUniversalTime());
		var last = await SendCandleRange(mdMsg.TransactionId, instrument.Symbol, interval, timeFrame,
			from, to, mdMsg.Count ?? long.MaxValue, cancellationToken);

		if (!mdMsg.IsHistoryOnly())
		{
			_candleSubscriptions[mdMsg.TransactionId] = new()
			{
				Symbol = instrument.Symbol,
				TimeFrame = timeFrame,
				Interval = interval,
				LastTime = last ?? to.Subtract(timeFrame),
			};
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private Task RefreshQuotes(CancellationToken cancellationToken)
		=> RefreshQuotes(cancellationToken, 0);

	private async Task RefreshQuotes(CancellationToken cancellationToken, long onlyTransactionId)
	{
		var subscriptions = _level1Subscriptions.ToArray();
		if (onlyTransactionId != 0)
			subscriptions = [.. subscriptions.Where(p => p.Key == onlyTransactionId)];
		if (subscriptions.Length == 0)
			return;

		var quotes = await _client.GetQuotes(subscriptions.Select(p => p.Value), cancellationToken);
		foreach (var quote in quotes ?? [])
		{
			foreach (var pair in subscriptions.Where(p => p.Value.EqualsIgnoreCase(quote.Symbol) ||
				p.Value.EqualsIgnoreCase(quote.Alias)))
			{
				var time = quote.TimestampMilliseconds > 0
					? DateTimeOffset.FromUnixTimeMilliseconds(quote.TimestampMilliseconds)
					: quote.TimestampSeconds > 0
						? DateTimeOffset.FromUnixTimeSeconds(quote.TimestampSeconds)
						: DateTimeOffset.UtcNow;
				await SendOutMessageAsync(new Level1ChangeMessage
				{
					SecurityId = ToSecurityId(pair.Value),
					ServerTime = time.UtcDateTime,
					OriginalTransactionId = pair.Key,
				}
				.TryAdd(Level1Fields.BestBidPrice, quote.Bid)
				.TryAdd(Level1Fields.BestAskPrice, quote.Ask)
				.TryAdd(Level1Fields.HighPrice, quote.High)
				.TryAdd(Level1Fields.LowPrice, quote.Low)
				.TryAdd(Level1Fields.Change, quote.Change), cancellationToken);
			}
		}
	}

	private async Task RefreshCandles(CancellationToken cancellationToken)
	{
		var now = DateTimeOffset.UtcNow;
		foreach (var pair in _candleSubscriptions.ToArray())
		{
			var from = pair.Value.LastTime;
			var last = await SendCandleRange(pair.Key, pair.Value.Symbol, pair.Value.Interval,
				pair.Value.TimeFrame, from, now, 1000, cancellationToken);
			if (last is { } value)
				pair.Value.LastTime = value;
		}
	}

	private async Task<DateTimeOffset?> SendCandleRange(long subscriptionId, string symbol,
		string interval, TimeSpan timeFrame, DateTimeOffset from, DateTimeOffset to, long limit,
		CancellationToken cancellationToken)
	{
		var response = await _client.GetCandles(symbol, interval, from, to, cancellationToken);
		DateTimeOffset? last = null;
		foreach (var candle in (response?.Candles ?? []).OrderBy(c => c.Time).Take((int)Math.Min(limit, int.MaxValue)))
		{
			var openTime = DateTimeOffset.FromUnixTimeMilliseconds(candle.Time);
			last = openTime;
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				SecurityId = ToSecurityId(symbol),
				OpenTime = openTime.UtcDateTime,
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				TypedArg = timeFrame,
				State = openTime + timeFrame <= DateTimeOffset.UtcNow
					? CandleStates.Finished : CandleStates.Active,
				OriginalTransactionId = subscriptionId,
			}, cancellationToken);
		}
		return last;
	}

	private static SecurityTypes ToSecurityType(string type)
	{
		if (type.ContainsIgnoreCase("forex")) return SecurityTypes.Currency;
		if (type.ContainsIgnoreCase("crypto")) return SecurityTypes.CryptoCurrency;
		if (type.ContainsIgnoreCase("index")) return SecurityTypes.Index;
		if (type.ContainsIgnoreCase("future")) return SecurityTypes.Future;
		return SecurityTypes.Stock;
	}

	private static string ToInterval(TimeSpan timeFrame)
	{
		if (timeFrame == TimeSpan.FromMinutes(1)) return "M1";
		if (timeFrame == TimeSpan.FromMinutes(5)) return "M5";
		if (timeFrame == TimeSpan.FromMinutes(15)) return "M15";
		if (timeFrame == TimeSpan.FromMinutes(30)) return "M30";
		if (timeFrame == TimeSpan.FromHours(1)) return "H1";
		if (timeFrame == TimeSpan.FromHours(4)) return "H4";
		if (timeFrame == TimeSpan.FromDays(1)) return "D1";
		if (timeFrame == TimeSpan.FromDays(7)) return "W1";
		if (timeFrame == TimeSpan.FromDays(30)) return "MN1";
		throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
			"Match-Trader does not support this candle interval.");
	}
}
