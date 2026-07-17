namespace StockSharp.TradeLocker;

public partial class TradeLockerMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var types = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var instrument in _instruments.CachedValues.OrderBy(i => i.Name))
		{
			var details = await _client.GetInstrument(instrument.TradableId,
				GetRoute(instrument, "INFO"), cancellationToken);
			var security = new SecurityMessage
			{
				SecurityId = ToSecurityId(instrument),
				Name = instrument.Description,
				SecurityType = ToSecurityType(instrument.Type),
				PriceStep = details?.PricePrecision > 0
					? 1m / (decimal)Math.Pow(10, details.PricePrecision)
					: null,
				VolumeStep = details?.LotStep,
				Multiplier = details?.LotSize,
				Currency = details?.QuoteCurrency?.FromMicexCurrencyName(this.AddErrorLog),
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
			_level1Subscriptions[mdMsg.TransactionId] = instrument.TradableId;
			await SendQuote(mdMsg.TransactionId, instrument, cancellationToken);
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
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}

		var instrument = ResolveInstrument(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		var resolution = ToResolution(timeFrame);
		var to = new DateTimeOffset(mdMsg.To ?? DateTime.UtcNow, TimeSpan.Zero);
		var from = new DateTimeOffset(mdMsg.From ?? to.UtcDateTime.Subtract(timeFrame.Multiply(500)),
			TimeSpan.Zero);
		var left = mdMsg.Count ?? long.MaxValue;
		var bars = await _client.GetHistory(instrument.TradableId, GetRoute(instrument, "INFO"),
			resolution, from, to, cancellationToken);

		foreach (var bar in bars.OrderBy(b => b.Time))
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				SecurityId = ToSecurityId(instrument),
				OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(bar.Time).UtcDateTime,
				OpenPrice = bar.Open,
				HighPrice = bar.High,
				LowPrice = bar.Low,
				ClosePrice = bar.Close,
				TotalVolume = bar.Volume,
				TypedArg = timeFrame,
				State = CandleStates.Finished,
				OriginalTransactionId = mdMsg.TransactionId,
			}, cancellationToken);
			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async Task RefreshLevel1(CancellationToken cancellationToken)
	{
		foreach (var pair in _level1Subscriptions.ToArray())
		{
			if (_instruments.TryGetValue(pair.Value, out var instrument))
				await SendQuote(pair.Key, instrument, cancellationToken);
		}
	}

	private async Task SendQuote(long subscriptionId, TradeLockerInstrument instrument,
		CancellationToken cancellationToken)
	{
		var quote = await _client.GetQuote(instrument.TradableId, GetRoute(instrument, "INFO"),
			cancellationToken);
		if (quote == null)
			return;

		await SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ToSecurityId(instrument),
			ServerTime = DateTime.UtcNow,
			OriginalTransactionId = subscriptionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, quote.BidPrice)
		.TryAdd(Level1Fields.BestBidVolume, quote.BidSize)
		.TryAdd(Level1Fields.BestAskPrice, quote.AskPrice)
		.TryAdd(Level1Fields.BestAskVolume, quote.AskSize), cancellationToken);
	}

	private static SecurityTypes ToSecurityType(string type)
	{
		if (type.ContainsIgnoreCase("forex") || type.ContainsIgnoreCase("currency"))
			return SecurityTypes.Currency;
		if (type.ContainsIgnoreCase("crypto"))
			return SecurityTypes.CryptoCurrency;
		if (type.ContainsIgnoreCase("index"))
			return SecurityTypes.Index;
		if (type.ContainsIgnoreCase("future"))
			return SecurityTypes.Future;
		return SecurityTypes.Stock;
	}

	private static string ToResolution(TimeSpan timeFrame)
	{
		if (timeFrame == TimeSpan.FromMinutes(1)) return "1m";
		if (timeFrame == TimeSpan.FromMinutes(5)) return "5m";
		if (timeFrame == TimeSpan.FromMinutes(15)) return "15m";
		if (timeFrame == TimeSpan.FromMinutes(30)) return "30m";
		if (timeFrame == TimeSpan.FromHours(1)) return "1H";
		if (timeFrame == TimeSpan.FromHours(4)) return "4H";
		if (timeFrame == TimeSpan.FromDays(1)) return "1D";
		if (timeFrame == TimeSpan.FromDays(7)) return "1W";
		throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
			"TradeLocker does not support this candle resolution.");
	}
}
