namespace StockSharp.Public;

partial class PublicMessageAdapter
{
	private const string BoardCode = "PUBLIC";

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		var securityTypes = message.GetSecurityTypes();
		var left = message.Count ?? long.MaxValue;
		var query = message.SecurityId.SecurityCode;

		if (securityTypes.Contains(SecurityTypes.Option) && !query.IsEmpty() && left > 0)
		{
			var underlying = message.GetUnderlyingCode().IsEmpty(query);
			var account = ResolveAccount(null);
			var expirations = message.ExpiryDate is DateTime expiry
				? [expiry.ToUniversalTime()]
				: (await _client.GetOptionExpirations(account.AccountId, underlying, cancellationToken))?.Expirations
					?.Select(ParseDate).ToArray() ?? [];

			foreach (var expiration in expirations)
			{
				var chain = await _client.GetOptionChain(account.AccountId, underlying, expiration, cancellationToken);
				foreach (var item in (chain?.Calls ?? []).Select(q => (quote: q, type: OptionTypes.Call))
					.Concat((chain?.Puts ?? []).Select(q => (quote: q, type: OptionTypes.Put))))
				{
					var quote = item.quote;
					if (quote?.Instrument?.Symbol.IsEmpty() != false)
						continue;

					var security = new SecurityMessage
					{
						OriginalTransactionId = message.TransactionId,
						SecurityId = new() { SecurityCode = quote.Instrument.Symbol, BoardCode = BoardCode },
						Name = quote.Instrument.Symbol,
						SecurityType = SecurityTypes.Option,
						OptionType = item.type,
						Strike = quote.OptionDetails?.StrikePrice,
						ExpiryDate = expiration,
						Multiplier = 100,
					}.TryFillUnderlyingId(underlying);

					if (!security.IsMatch(message, securityTypes))
						continue;
					await SendOutMessageAsync(security, cancellationToken);
					if (--left <= 0)
						break;
				}
				if (left <= 0)
					break;
			}
		}

		if (left > 0 && securityTypes.Any(t => t != SecurityTypes.Option))
		{
			var nativeTypes = securityTypes.Where(t => t != SecurityTypes.Option).Select(t => t.ToNative(query)).Distinct().ToArray();
			foreach (var instrument in await _client.GetInstruments(nativeTypes, cancellationToken))
			{
				if (instrument?.Instrument?.Symbol.IsEmpty() != false || (!query.IsEmpty() && !instrument.Instrument.Symbol.Contains(query, StringComparison.OrdinalIgnoreCase)))
					continue;

				var security = ToSecurityMessage(instrument, message.TransactionId);
				if (!security.IsMatch(message, securityTypes))
					continue;
				await SendOutMessageAsync(security, cancellationToken);
				if (--left <= 0)
					break;
			}
		}

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (message.IsSubscribe)
		{
			var subscription = new Level1Subscription(message.SecurityId, message.SecurityType.ToNative(message.SecurityId.SecurityCode));
			_level1Subscriptions[message.TransactionId] = subscription;
			await SendSubscriptionResultAsync(message, cancellationToken);
			var quotes = await _client.GetQuotes(ResolveAccount(null).AccountId,
				[new() { Symbol = message.SecurityId.SecurityCode, Type = subscription.InstrumentType }], cancellationToken);
			if (quotes.FirstOrDefault() is { } quote)
				await ProcessQuote(quote, [new(message.TransactionId, subscription)], cancellationToken);
		}
		else
			_level1Subscriptions.Remove(message.OriginalTransactionId);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
			return;

		var timeFrame = message.GetTimeFrame();
		var aggregation = ToAggregation(timeFrame);
		var period = ToPeriod(message.From, message.To, message.Count, timeFrame);
		var response = await _client.GetBars(
			message.SecurityId.SecurityCode,
			message.SecurityType.ToNative(message.SecurityId.SecurityCode),
			period,
			aggregation,
			message.IsRegularTradingHours == true ? PublicTradingSessionToggles.RegularHours : PublicTradingSessionToggles.AllSessions,
			cancellationToken);

		var bars = new[]
		{
			response?.PreMarketOvernight, response?.PreMarket, response?.RegularMarket,
			response?.AfterMarket, response?.PostMarketOvernight,
		}
			.WhereNotNull()
			.SelectMany(s => s.Bars ?? [])
			.Select(b => (bar: b, time: ParseDateTime(b.Timestamp)))
			.Where(p => message.From is not DateTime from || p.time >= from.ToUniversalTime())
			.Where(p => message.To is not DateTime to || p.time <= to.ToUniversalTime())
			.DistinctBy(p => p.time)
			.OrderBy(p => p.time);

		if (message.Count is > 0 and <= int.MaxValue)
			bars = bars.TakeLast((int)message.Count.Value).OrderBy(p => p.time);

		foreach (var (bar, openTime) in bars)
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = message.TransactionId,
				SecurityId = message.SecurityId,
				OpenTime = openTime,
				CloseTime = openTime + timeFrame,
				OpenPrice = bar.Open,
				HighPrice = bar.High,
				LowPrice = bar.Low,
				ClosePrice = bar.Close,
				TotalVolume = bar.Volume,
				State = CandleStates.Finished,
			}, cancellationToken);
		}

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	private static SecurityMessage ToSecurityMessage(PublicInstrument instrument, long originalTransactionId)
	{
		var type = instrument.Instrument.Type;
		var security = new SecurityMessage
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = new() { SecurityCode = instrument.Instrument.Symbol, BoardCode = BoardCode },
			Name = instrument.Instrument.Symbol,
			SecurityType = type.ToSecurityType(),
			PriceStep = type == PublicInstrumentTypes.Crypto && instrument.Details?.CryptoPricePrecision is int pricePrecision
				? GetStep(pricePrecision)
				: instrument.OptionPriceIncrements?.IncrementBelowThree,
			VolumeStep = type == PublicInstrumentTypes.Crypto && instrument.Details?.CryptoQuantityPrecision is int quantityPrecision
				? GetStep(quantityPrecision)
				: null,
		};
		return security;
	}

	private async ValueTask ProcessQuote(PublicQuote quote, KeyValuePair<long, Level1Subscription>[] subscriptions, CancellationToken cancellationToken)
	{
		if (quote?.Instrument?.Symbol.IsEmpty() != false || quote.Outcome != PublicQuoteOutcomes.Success)
			return;

		var serverTime = new[] { quote.LastTimestamp, quote.BidTimestamp, quote.AskTimestamp }
			.Where(t => t is not null).Select(t => t.Value.ToUniversalTime()).DefaultIfEmpty(DateTime.UtcNow).Max();
		foreach (var subscription in subscriptions.Where(p => p.Value.SecurityId.SecurityCode.EqualsIgnoreCase(quote.Instrument.Symbol) && p.Value.InstrumentType == quote.Instrument.Type))
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = subscription.Key,
				SecurityId = subscription.Value.SecurityId,
				ServerTime = serverTime,
			}
			.TryAdd(Level1Fields.LastTradePrice, quote.Last)
			.TryAdd(Level1Fields.BestBidPrice, quote.Bid)
			.TryAdd(Level1Fields.BestBidVolume, quote.BidSize)
			.TryAdd(Level1Fields.BestAskPrice, quote.Ask)
			.TryAdd(Level1Fields.BestAskVolume, quote.AskSize)
			.TryAdd(Level1Fields.Volume, quote.Volume)
			.TryAdd(Level1Fields.OpenInterest, quote.OpenInterest)
			.TryAdd(Level1Fields.ClosePrice, quote.PreviousClose)
			.TryAdd(Level1Fields.Change, quote.OneDayChange?.Change)
			.TryAdd(Level1Fields.Delta, quote.OptionDetails?.Greeks?.Delta)
			.TryAdd(Level1Fields.Gamma, quote.OptionDetails?.Greeks?.Gamma)
			.TryAdd(Level1Fields.Theta, quote.OptionDetails?.Greeks?.Theta)
			.TryAdd(Level1Fields.Vega, quote.OptionDetails?.Greeks?.Vega)
			.TryAdd(Level1Fields.Rho, quote.OptionDetails?.Greeks?.Rho)
			.TryAdd(Level1Fields.ImpliedVolatility, quote.OptionDetails?.Greeks?.ImpliedVolatility), cancellationToken);
		}
	}

	private static PublicBarAggregations ToAggregation(TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromMinutes(1) ? PublicBarAggregations.OneMinute
			: timeFrame == TimeSpan.FromMinutes(5) ? PublicBarAggregations.FiveMinutes
			: timeFrame == TimeSpan.FromMinutes(10) ? PublicBarAggregations.TenMinutes
			: timeFrame == TimeSpan.FromMinutes(15) ? PublicBarAggregations.FifteenMinutes
			: timeFrame == TimeSpan.FromMinutes(30) ? PublicBarAggregations.ThirtyMinutes
			: timeFrame == TimeSpan.FromHours(1) ? PublicBarAggregations.OneHour
			: timeFrame == TimeSpan.FromDays(1) ? PublicBarAggregations.OneDay
			: timeFrame == TimeSpan.FromDays(7) ? PublicBarAggregations.OneWeek
			: timeFrame == TimeSpan.FromDays(30) ? PublicBarAggregations.OneMonth
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

	private static PublicBarPeriods ToPeriod(DateTime? from, DateTime? to, long? count, TimeSpan timeFrame)
	{
		var end = to?.ToUniversalTime() ?? DateTime.UtcNow;
		var start = from?.ToUniversalTime();
		if (start is null && count is > 0 && count <= TimeSpan.MaxValue.Ticks / timeFrame.Ticks)
			start = end - TimeSpan.FromTicks(count.Value * timeFrame.Ticks);
		var span = end - (start ?? end.AddYears(-1));
		return span <= TimeSpan.FromDays(1) ? PublicBarPeriods.Day
			: span <= TimeSpan.FromDays(7) ? PublicBarPeriods.Week
			: span <= TimeSpan.FromDays(31) ? PublicBarPeriods.Month
			: span <= TimeSpan.FromDays(93) ? PublicBarPeriods.Quarter
			: span <= TimeSpan.FromDays(186) ? PublicBarPeriods.HalfYear
			: span <= TimeSpan.FromDays(366) ? PublicBarPeriods.Year
			: span <= TimeSpan.FromDays(365 * 5 + 2) ? PublicBarPeriods.FiveYears
			: span <= TimeSpan.FromDays(365 * 10 + 3) ? PublicBarPeriods.TenYears
			: PublicBarPeriods.All;
	}

	private static DateTime ParseDate(string value)
		=> DateTime.SpecifyKind(DateTime.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture), DateTimeKind.Utc);

	private static DateTime ParseDateTime(string value)
		=> DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

	private static decimal GetStep(int precision)
	{
		var step = 1m;
		for (var i = 0; i < precision; i++)
			step /= 10m;
		return step;
	}
}
