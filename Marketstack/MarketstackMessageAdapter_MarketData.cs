namespace StockSharp.Marketstack;

public partial class MarketstackMessageAdapter
{
	private readonly record struct ParsedBar(MarketstackBar Value, DateTime OpenTime);

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var board = lookupMsg.SecurityId.BoardCode;
		var securityTypes = lookupMsg.GetSecurityTypes();
		if ((!board.IsEmpty() && !board.EqualsIgnoreCase(Extensions.BoardCode)) ||
			(securityTypes.Count > 0 && !securityTypes.Contains(SecurityTypes.Stock)) ||
			lookupMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}

		var native = lookupMsg.SecurityId.Native as string;
		var isExact = MarketstackSecurityKey.TryParse(native, out var exactKey);
		var value = (isExact ? exactKey.Symbol :
			native.IsEmpty(lookupMsg.SecurityId.SecurityCode).IsEmpty(lookupMsg.Name))?.Trim();
		var exchange = isExact ? exactKey.Mic : StockExchange;
		var offset = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		if (isExact)
			left = Math.Min(left, 1);
		var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		while (left > 0)
		{
			var limit = checked((int)Math.Clamp(left, 1, 1000));
			var page = await SafeRest().GetTickers(value, exchange, offset, limit,
				cancellationToken);
			var values = page?.Data ?? [];
			foreach (var ticker in values)
			{
				if (ticker == null || !ticker.Matches(value) ||
					(isExact && !ticker.Matches(exactKey)))
				{
					continue;
				}
				var security = ticker.ToSecurityMessage(lookupMsg.TransactionId);
				var key = security?.SecurityId.Native as string;
				if (security == null || key.IsEmpty() || !emitted.Add(key) ||
					!security.IsMatch(lookupMsg, securityTypes))
				{
					continue;
				}
				await SendOutMessageAsync(security, cancellationToken);
				if (--left <= 0)
					break;
			}

			var received = page?.Pagination?.Count > 0
				? page.Pagination.Count : values.LongLength;
			if (received <= 0)
				break;
			offset = checked(offset + received);
			if (page?.Pagination?.Total is > 0 &&
				offset >= page.Pagination.Total || values.Length < limit)
			{
				break;
			}
			await IterationInterval.Delay(cancellationToken);
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
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}
		if (mdMsg.From != null || mdMsg.To != null)
			throw new NotSupportedException(
				"Marketstack does not expose historical Level1 events.");

		var key = mdMsg.SecurityId.GetMarketstackKey(StockExchange);
		var securityId = mdMsg.SecurityId.NormalizeMarketstack(key);
		var response = await SafeRest().GetStockPrice(key.Symbol,
			key.ExchangeCode.IsEmpty(key.Mic), cancellationToken);
		var values = (response?.Data ?? []).Where(value => value.Matches(key)).ToArray();
		if (values.Length == 0)
			throw new InvalidOperationException(
				$"Marketstack returned no realtime price for '{key.Symbol}'.");
		if (key.ExchangeCode.IsEmpty() && key.Mic.IsEmpty() &&
			values.Select(value => value.ExchangeCode).Where(value => !value.IsEmpty())
				.Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
		{
			throw new InvalidOperationException(
				$"Marketstack symbol '{key.Symbol}' is ambiguous. Use a security returned " +
				"by lookup or configure StockExchange.");
		}

		var parsed = values
			.Select(value => new
			{
				Price = Extensions.ParseDecimal(value.Price),
				HasTime = Extensions.TryParseUtc(value.TradeLast, out var time),
				Time = time,
			})
			.Where(value => value.Price != null && value.HasTime)
			.OrderByDescending(value => value.Time)
			.FirstOrDefault();
		if (parsed == null)
			throw new InvalidDataException(
				$"Marketstack returned an invalid realtime observation for '{key.Symbol}'.");

		await SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = mdMsg.TransactionId,
			SecurityId = securityId,
			ServerTime = parsed.Time,
		}
		.TryAdd(Level1Fields.LastTradePrice, parsed.Price)
		.TryAdd(Level1Fields.LastTradeTime, parsed.Time), cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
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
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var timeFrame = mdMsg.GetTimeFrame();
		if (!Extensions.TimeFrames.Contains(timeFrame))
			throw new NotSupportedException(
				$"Marketstack does not document {timeFrame} candles.");
		var key = mdMsg.SecurityId.GetMarketstackKey(StockExchange);
		var securityId = mdMsg.SecurityId.NormalizeMarketstack(key);
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUtc();
		var from = (mdMsg.From ?? Extensions.EstimateFrom(to, timeFrame, mdMsg.Count)).ToUtc();
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.From), from,
				"The candle-history start time is after its end time.");

		const int limit = 1000;
		var offset = 0L;
		var bars = new List<MarketstackBar>();
		while (true)
		{
			var page = timeFrame == TimeSpan.FromDays(1)
				? await SafeRest().GetEod(key.Symbol, key.Mic, from, to, offset, limit,
					cancellationToken)
				: await SafeRest().GetIntraday(key.Symbol, key.Mic, from, to, timeFrame,
					IsAfterHours, offset, limit, cancellationToken);
			var values = page?.Data ?? [];
			bars.AddRange(values.Where(value => value != null));
			var received = page?.Pagination?.Count > 0
				? page.Pagination.Count : values.LongLength;
			if (received <= 0)
				break;
			offset = checked(offset + received);
			if (page?.Pagination?.Total is > 0 && offset >= page.Pagination.Total ||
				values.Length < limit)
			{
				break;
			}
			await IterationInterval.Delay(cancellationToken);
		}

		var parsed = new List<ParsedBar>(bars.Count);
		foreach (var bar in bars)
		{
			if (bar.Matches(key) && Extensions.TryParseUtc(bar.Date, out var openTime) &&
				openTime >= from && openTime <= to)
			{
				parsed.Add(new(bar, openTime));
			}
		}
		if (key.Mic.IsEmpty() && parsed.Select(value => value.Value.Exchange)
			.Where(value => !value.IsEmpty()).Distinct(StringComparer.OrdinalIgnoreCase)
			.Skip(1).Any())
		{
			throw new InvalidOperationException(
				$"Marketstack symbol '{key.Symbol}' is ambiguous. Use a security returned " +
				"by lookup or configure StockExchange.");
		}

		var ordered = parsed.OrderBy(value => value.OpenTime)
			.GroupBy(value => value.OpenTime)
			.Select(group => group.First())
			.ToList();
		var start = 0;
		var take = ordered.Count;
		if (mdMsg.Count is long requested)
		{
			take = checked((int)Math.Min(requested, ordered.Count));
			if (mdMsg.From == null)
				start = ordered.Count - take;
		}

		for (var i = start; i < start + take; i++)
		{
			var item = ordered[i];
			if (!item.Value.TryGetOhlc(PriceAdjustment, out var open, out var high,
				out var low, out var close, out var volume))
			{
				continue;
			}
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = securityId,
				DataType = mdMsg.DataType2,
				OpenTime = item.OpenTime,
				CloseTime = item.OpenTime + timeFrame,
				OpenPrice = open,
				HighPrice = high,
				LowPrice = low,
				ClosePrice = close,
				TotalVolume = volume,
				State = CandleStates.Finished,
			}, cancellationToken);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}
}
