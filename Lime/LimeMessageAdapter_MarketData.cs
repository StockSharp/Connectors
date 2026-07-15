namespace StockSharp.Lime;

public partial class LimeMessageAdapter
{
	private const string _boardCode = "LIME";

	private static SecurityId ToSecurityId(string symbol)
		=> new() { SecurityCode = symbol, BoardCode = _boardCode };

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var limit = lookupMsg.Count is long count ? checked((int)count.Min(1000).Max(1)) : 100;
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = (long)limit;
		var query = lookupMsg.SecurityId.SecurityCode;

		if (securityTypes.Contains(SecurityTypes.Option) && !query.IsEmpty())
		{
			var underlying = lookupMsg.GetUnderlyingCode().IsEmpty(query);
			foreach (var series in await _httpClient.GetOptionSeries(underlying, cancellationToken) ?? [])
			{
				var expirations = lookupMsg.ExpiryDate is DateTime expiry
					? [expiry.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)]
					: series.Expirations ?? [];
				foreach (var expiration in expirations)
				{
					var chain = await _httpClient.GetOptionChain(underlying, expiration, series.Series, cancellationToken);
					foreach (var option in chain?.Chain ?? [])
					{
						var security = new SecurityMessage
						{
							SecurityId = ToSecurityId(option.Symbol),
							SecurityType = SecurityTypes.Option,
							OptionType = option.Type.ToOptionType(),
							Strike = option.Strike,
							ExpiryDate = DateTime.ParseExact(expiration, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
							Multiplier = chain.ContractSize,
							OriginalTransactionId = lookupMsg.TransactionId,
						}.TryFillUnderlyingId(underlying);

						if (!security.IsMatch(lookupMsg, securityTypes))
							continue;
						await SendOutMessageAsync(security, cancellationToken);
						if (--left <= 0)
							break;
					}
					if (left <= 0)
						break;
				}
				if (left <= 0)
					break;
			}
		}

		if (left > 0 && securityTypes.Any(type => type != SecurityTypes.Option))
		{
			var result = await _httpClient.LookupSecurities(query, checked((int)left.Min(1000)), cancellationToken);
			foreach (var item in result?.Trades ?? [])
			{
				var security = new SecurityMessage
				{
					SecurityId = ToSecurityId(item.Symbol),
					SecurityType = SecurityTypes.Stock,
					Name = item.Description,
					ShortName = item.Symbol,
					OriginalTransactionId = lookupMsg.TransactionId,
				};

				if (!security.IsMatch(lookupMsg, securityTypes))
					continue;
				await SendOutMessageAsync(security, cancellationToken);
				if (--left <= 0)
					break;
			}
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;

		var quote = await _httpClient.GetQuote(mdMsg.SecurityId.SecurityCode, cancellationToken);
		if (quote != null)
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				ServerTime = quote.Date > 0 ? quote.Date.ToDateTime() : DateTime.UtcNow,
			}
			.TryAdd(Level1Fields.BestBidPrice, quote.Bid)
			.TryAdd(Level1Fields.BestBidVolume, quote.BidSize)
			.TryAdd(Level1Fields.BestAskPrice, quote.Ask)
			.TryAdd(Level1Fields.BestAskVolume, quote.AskSize)
			.TryAdd(Level1Fields.LastTradePrice, quote.Last)
			.TryAdd(Level1Fields.LastTradeVolume, quote.LastSize)
			.TryAdd(Level1Fields.Volume, quote.Volume)
			.TryAdd(Level1Fields.OpenPrice, quote.Open)
			.TryAdd(Level1Fields.HighPrice, quote.High)
			.TryAdd(Level1Fields.LowPrice, quote.Low)
			.TryAdd(Level1Fields.ClosePrice, quote.Close)
			.TryAdd(Level1Fields.HighPrice52Week, quote.Week52High)
			.TryAdd(Level1Fields.LowPrice52Week, quote.Week52Low)
			.TryAdd(Level1Fields.Change, quote.Change)
			.TryAdd(Level1Fields.OpenInterest, quote.OpenInterest)
			.TryAdd(Level1Fields.ImpliedVolatility, quote.ImpliedVolatility)
			.TryAdd(Level1Fields.Delta, quote.Delta)
			.TryAdd(Level1Fields.Gamma, quote.Gamma)
			.TryAdd(Level1Fields.Theta, quote.Theta)
			.TryAdd(Level1Fields.Vega, quote.Vega)
			.TryAdd(Level1Fields.Rho, quote.Rho), cancellationToken);
		}

		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;

		var timeFrame = mdMsg.GetTimeFrame();
		var period = timeFrame.ToNative();
		var to = mdMsg.To?.ToUniversalTime() ?? DateTime.UtcNow;
		var maxRange = period switch
		{
			LimePeriods.Minute => TimeSpan.FromDays(7),
			LimePeriods.Minute5 or LimePeriods.Minute15 or LimePeriods.Minute30 or LimePeriods.Hour => TimeSpan.FromDays(30),
			LimePeriods.Day => TimeSpan.FromDays(365),
			LimePeriods.Week => TimeSpan.FromDays(365 * 5),
			_ => throw new ArgumentOutOfRangeException(nameof(period), period, LocalizedStrings.InvalidValue),
		};
		var from = mdMsg.From?.ToUniversalTime() ?? (mdMsg.Count is long count
			? to - TimeSpan.FromTicks(checked(timeFrame.Ticks * count.Min(int.MaxValue).Max(1)))
			: to - maxRange);
		if (to - from > maxRange)
			from = to - maxRange;

		foreach (var candle in (await _httpClient.GetHistory(mdMsg.SecurityId.SecurityCode, period, from, to, cancellationToken) ?? []).OrderBy(candle => candle.Timestamp))
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				TypedArg = timeFrame,
				OpenTime = candle.Timestamp.ToDateTime(),
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				State = CandleStates.Finished,
			}, cancellationToken);
		}

		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}
}
