namespace StockSharp.Benzinga;

public partial class BenzingaMessageAdapter
{
	private readonly record struct ParsedCandle(BenzingaCandle Value, DateTime OpenTime);

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		if (lookupMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}

		var raw = (lookupMsg.SecurityId.Native as string)
			.IsEmpty(lookupMsg.SecurityId.SecurityCode)?.Trim();
		if (BenzingaSecurityKey.TryParse(raw, out var native))
			raw = native.Symbol;
		if (raw.IsEmpty() && lookupMsg.SecurityId.Isin.IsEmpty())
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}
		if (!raw.IsEmpty() && raw.Any(character => character is '*' or '?' ||
			char.IsWhiteSpace(character) || char.IsControl(character)))
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}

		var response = await SafeRest().GetDelayedQuotes(raw, lookupMsg.SecurityId.Isin,
			cancellationToken);
		var types = lookupMsg.GetSecurityTypes();
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var quote in response?.Quotes ?? [])
		{
			if (left <= 0)
				break;
			var security = quote.ToSecurityMessage(lookupMsg.TransactionId,
				lookupMsg.SecurityId);
			if (security == null || !security.IsMatch(lookupMsg, types))
				continue;
			if (skip > 0)
			{
				skip--;
				continue;
			}
			await SendOutMessageAsync(security, cancellationToken);
			left--;
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await Complete(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From != null || mdMsg.To != null)
			throw new NotSupportedException("Benzinga does not expose historical Level1 events.");

		var response = await GetQuote(mdMsg.SecurityId, cancellationToken);
		if (response != null)
		{
			var security = response.ToSecurityMessage(mdMsg.TransactionId, mdMsg.SecurityId);
			if (security != null)
			{
				var message = CreateLevel1(mdMsg.TransactionId, security.SecurityId, response);
				if (message != null)
					await SendOutMessageAsync(message, cancellationToken);
			}
		}
		await Complete(mdMsg, cancellationToken);
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
		if (mdMsg.Count is <= 0)
		{
			await Complete(mdMsg, cancellationToken);
			return;
		}

		var timeFrame = mdMsg.GetTimeFrame();
		if (!BenzingaExtensions.TimeFrames.Contains(timeFrame))
			throw new NotSupportedException($"Benzinga does not support {timeFrame} candles.");
		var key = mdMsg.SecurityId.GetBenzingaKey();
		var securityId = mdMsg.SecurityId.NormalizeBenzinga(key);
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUtc();
		var limit = checked((int)Math.Min(mdMsg.Count ?? MaxBars, MaxBars));
		var from = mdMsg.From?.ToUtc() ?? GetDefaultFrom(to, timeFrame, limit);
		if (from > to)
		{
			throw new ArgumentOutOfRangeException(nameof(mdMsg.From), from,
				"The Benzinga candle-history start time is after its end time.");
		}

		var responses = await SafeRest().GetBars(key.Symbol, from, to,
			timeFrame.ToInterval(), BarsSession, cancellationToken) ?? [];
		var response = responses.FirstOrDefault(value =>
			value?.Symbol.EqualsIgnoreCase(key.Symbol) == true) ?? responses.FirstOrDefault();
		var parsed = new List<ParsedCandle>();
		foreach (var candle in response?.Candles ?? [])
		{
			if (candle == null || candle.Open == null || candle.High == null ||
				candle.Low == null || candle.Close == null)
			{
				continue;
			}
			var openTime = BenzingaExtensions.FromUnixMilliseconds(candle.Time);
			if (openTime == null && BenzingaExtensions.TryParseUtc(candle.DateTime, out var parsedTime))
				openTime = parsedTime;
			if (openTime != null && openTime >= from && openTime <= to)
				parsed.Add(new(candle, openTime.Value));
		}

		IEnumerable<ParsedCandle> selected = parsed
			.GroupBy(item => item.OpenTime.Ticks)
			.Select(group => group.Last())
			.OrderBy(item => item.OpenTime);
		if (mdMsg.From == null)
			selected = selected.TakeLast(limit);
		else
			selected = selected.Take(limit);

		foreach (var item in selected)
		{
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = securityId,
				DataType = mdMsg.DataType2,
				OpenTime = item.OpenTime,
				CloseTime = item.OpenTime + timeFrame,
				OpenPrice = item.Value.Open.Value,
				HighPrice = item.Value.High.Value,
				LowPrice = item.Value.Low.Value,
				ClosePrice = item.Value.Close.Value,
				TotalVolume = BenzingaExtensions.NonNegative(item.Value.Volume) ?? 0,
				State = CandleStates.Finished,
			}, cancellationToken);
		}
		await Complete(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnNewsSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			RemoveLiveSubscription(mdMsg.OriginalTransactionId);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await Complete(mdMsg, cancellationToken);
			return;
		}

		var raw = (mdMsg.SecurityId.Native as string)
			.IsEmpty(mdMsg.SecurityId.SecurityCode)?.Trim();
		var symbol = raw;
		var requestedSecurityId = default(SecurityId);
		if (!raw.IsEmpty())
		{
			var key = mdMsg.SecurityId.GetBenzingaKey();
			symbol = key.Symbol;
			requestedSecurityId = mdMsg.SecurityId.NormalizeBenzinga(key);
		}
		var from = mdMsg.From?.ToUtc();
		var to = mdMsg.To?.ToUtc();
		if (from != null && to != null && from > to)
		{
			throw new ArgumentOutOfRangeException(nameof(mdMsg.From), from,
				"The Benzinga news-history start time is after its end time.");
		}

		var remaining = mdMsg.Count;
		if (from != null || to != null || mdMsg.IsHistoryOnly())
		{
			var target = checked((int)Math.Min(remaining ?? MaxNewsItems, MaxNewsItems));
			var items = await GetNewsHistory(symbol, from, to, target, cancellationToken);
			foreach (var entry in items)
			{
				await SendNews(mdMsg.TransactionId, requestedSecurityId, entry.Item,
					entry.Time, cancellationToken);
				if (remaining is > 0 && --remaining == 0)
					break;
			}
		}

		if (mdMsg.IsHistoryOnly() || to != null || remaining == 0)
		{
			await Complete(mdMsg, cancellationToken);
			return;
		}

		var subscription = new LiveNewsSubscription
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = requestedSecurityId,
			Symbol = symbol,
			Channels = BenzingaExtensions.ParseChannels(NewsChannels),
			Remaining = remaining,
		};
		AddLiveSubscription(subscription);
		try
		{
			await EnsureNewsStream(cancellationToken);
		}
		catch
		{
			RemoveLiveSubscription(mdMsg.TransactionId);
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async Task<BenzingaDelayedQuote> GetQuote(SecurityId securityId,
		CancellationToken cancellationToken)
	{
		var raw = (securityId.Native as string).IsEmpty(securityId.SecurityCode)?.Trim();
		if (BenzingaSecurityKey.TryParse(raw, out var key))
			raw = key.Symbol;
		if (raw.IsEmpty() && securityId.Isin.IsEmpty())
			throw new ArgumentException("A Benzinga symbol or ISIN is required.", nameof(securityId));
		var response = await SafeRest().GetDelayedQuotes(raw, securityId.Isin,
			cancellationToken);
		return response?.Quotes.FirstOrDefault(quote =>
			quote?.Symbol.EqualsIgnoreCase(raw) == true) ?? response?.Quotes.FirstOrDefault();
	}

	private static Level1ChangeMessage CreateLevel1(long transactionId,
		SecurityId securityId, BenzingaDelayedQuote quote)
	{
		var bidTime = BenzingaExtensions.FromUnixMilliseconds(quote.BidTime);
		var askTime = BenzingaExtensions.FromUnixMilliseconds(quote.AskTime);
		var tradeTime = BenzingaExtensions.FromUnixMilliseconds(quote.LastTradeTime);
		var extendedTime = BenzingaExtensions.FromUnixMilliseconds(quote.ExtendedHoursTime);
		var closeTime = BenzingaExtensions.TryParseUtc(quote.CloseDate, out var parsedClose)
			? parsedClose : (DateTime?)null;
		var serverTime = new[] { bidTime, askTime, tradeTime, extendedTime, closeTime }
			.Where(time => time != null).Select(time => time.Value).DefaultIfEmpty().Max();
		if (serverTime == default)
			return null;

		var lastPrice = quote.LastTradePrice;
		var lastTime = tradeTime;
		if (quote.ExtendedHoursPrice is > 0 && extendedTime != null &&
			(lastTime == null || extendedTime > lastTime))
		{
			lastPrice = quote.ExtendedHoursPrice;
			lastTime = extendedTime;
		}

		var message = new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = serverTime,
		}
		.TryAdd(Level1Fields.BestBidPrice,
			BenzingaExtensions.Positive(quote.BidPrice))
		.TryAdd(Level1Fields.BestBidVolume,
			BenzingaExtensions.NonNegative(quote.BidSize))
		.TryAdd(Level1Fields.BestBidTime,
			quote.BidPrice is > 0 ? bidTime : null)
		.TryAdd(Level1Fields.BestAskPrice,
			BenzingaExtensions.Positive(quote.AskPrice))
		.TryAdd(Level1Fields.BestAskVolume,
			BenzingaExtensions.NonNegative(quote.AskSize))
		.TryAdd(Level1Fields.BestAskTime,
			quote.AskPrice is > 0 ? askTime : null)
		.TryAdd(Level1Fields.LastTradePrice,
			BenzingaExtensions.Positive(lastPrice))
		.TryAdd(Level1Fields.LastTradeTime,
			lastPrice is > 0 ? lastTime : null)
		.TryAdd(Level1Fields.ClosePrice,
			BenzingaExtensions.Positive(quote.Close))
		.TryAdd(Level1Fields.SettlementPrice,
			BenzingaExtensions.Positive(quote.PreviousClosePrice))
		.TryAdd(Level1Fields.Volume,
			BenzingaExtensions.NonNegative(quote.Size ?? quote.Volume))
		.TryAdd(Level1Fields.Change, quote.ChangePercent)
		.TryAdd(Level1Fields.HighPrice52Week,
			BenzingaExtensions.Positive(quote.FiftyTwoWeekHigh))
		.TryAdd(Level1Fields.LowPrice52Week,
			BenzingaExtensions.Positive(quote.FiftyTwoWeekLow))
		.TryAdd(Level1Fields.SharesOutstanding,
			BenzingaExtensions.NonNegative(quote.SharesOutstanding))
		.TryAdd(Level1Fields.SharesFloat,
			BenzingaExtensions.NonNegative(quote.SharesFloat))
		.TryAdd(Level1Fields.PriceEarnings,
			BenzingaExtensions.NonNegative(quote.PriceEarnings))
		.TryAdd(Level1Fields.Yield,
			BenzingaExtensions.NonNegative(quote.DividendYield))
		.TryAdd(Level1Fields.Dividend,
			BenzingaExtensions.NonNegative(quote.Dividend));
		return message.Changes.Count == 0 ? null : message;
	}

	private async Task<(BenzingaNewsItem Item, DateTime Time)[]> GetNewsHistory(string symbol,
		DateTime? from, DateTime? to, int target, CancellationToken cancellationToken)
	{
		var values = new List<(BenzingaNewsItem Item, DateTime Time)>();
		var ids = new HashSet<long>();
		var pageSize = Math.Min(100, Math.Max(1, target));
		var maximumPages = Math.Min(100001,
			Math.Max(10, (target + pageSize - 1) / pageSize + 10));
		for (var page = 0; page < maximumPages && values.Count < target; page++)
		{
			var response = await SafeRest().GetNews(symbol, NewsChannels, from, to,
				page, pageSize, cancellationToken) ?? [];
			var previousIdCount = ids.Count;
			foreach (var item in response)
			{
				if (item == null || item.Id is not { } id || !ids.Add(id) ||
					!TryGetNewsTime(null, item, out var time) ||
					from != null && time < from.Value || to != null && time > to.Value)
				{
					continue;
				}
				values.Add((item, time));
			}
			if (response.Length < pageSize || ids.Count == previousIdCount)
				break;
			if (values.Count < target)
				await IterationInterval.Delay(cancellationToken);
		}

		var ordered = values.OrderBy(item => item.Time);
		return (from == null ? ordered.TakeLast(target) : ordered.Take(target)).ToArray();
	}

	private async ValueTask SendNews(long transactionId, SecurityId requestedSecurityId,
		BenzingaNewsItem item, DateTime serverTime, CancellationToken cancellationToken)
	{
		var securityId = item.GetNewsSecurityId(requestedSecurityId);
		await SendOutMessageAsync(new NewsMessage
		{
			OriginalTransactionId = transactionId,
			ServerTime = serverTime,
			Id = (item.Id ?? item.OriginalId)?.ToString(CultureInfo.InvariantCulture),
			Headline = item.Title,
			Story = item.Body.IsEmpty(item.Teaser),
			Source = item.Author.IsEmpty("Benzinga"),
			Url = item.Url,
			BoardCode = BenzingaExtensions.BoardCode,
			SecurityId = securityId.SecurityCode.IsEmpty() ? null : securityId,
		}, cancellationToken);
	}

	private static DateTime GetDefaultFrom(DateTime to, TimeSpan timeFrame, int count)
	{
		var maximumTicks = TimeSpan.FromDays(365 * 20).Ticks;
		var requestedTicks = Math.Min(maximumTicks,
			(long)Math.Min(maximumTicks, timeFrame.Ticks * (double)Math.Max(100, count) * 4));
		if (timeFrame < TimeSpan.FromDays(1))
			requestedTicks = Math.Max(requestedTicks, TimeSpan.FromDays(7).Ticks);
		return to.AddTicks(-requestedTicks);
	}

	private async ValueTask Complete(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
	}
}
