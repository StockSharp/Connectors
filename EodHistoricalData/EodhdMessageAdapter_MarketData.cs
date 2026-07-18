namespace StockSharp.EodHistoricalData;

public partial class EodhdMessageAdapter
{
	private readonly record struct SelectedBar(DateTime OpenTime, decimal Open, decimal High,
		decimal Low, decimal Close, decimal Volume, decimal? OpenInterest);

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var board = lookupMsg.SecurityId.BoardCode;
		var native = lookupMsg.SecurityId.Native as string;
		var value = native.IsEmpty(lookupMsg.SecurityId.SecurityCode).IsEmpty(lookupMsg.Name);
		var exactNative = EodhdSecurityKey.TryParse(native, out var nativeKey);
		if (exactNative)
			value = nativeKey.Code;
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		var sent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		async Task Emit(SecurityMessage security)
		{
			var key = security?.SecurityId.Native as string;
			if (security == null || left <= 0 || !security.IsMatch(lookupMsg, securityTypes) ||
				key.IsEmpty() || !sent.Add(key))
			{
				return;
			}
			if (skip > 0)
			{
				skip--;
				return;
			}
			await SendOutMessageAsync(security, cancellationToken);
			left--;
		}

		var noBoard = board.IsEmpty();
		var noTypes = securityTypes.Count == 0;
		bool Requested(EodhdMarkets market)
		{
			if (!noBoard)
				return board.EqualsIgnoreCase(market.ToBoard());
			if (noTypes)
				return market != EodhdMarkets.Options;
			return market switch
			{
				EodhdMarkets.Forex => securityTypes.Contains(SecurityTypes.Currency),
				EodhdMarkets.Crypto => securityTypes.Contains(SecurityTypes.CryptoCurrency),
				EodhdMarkets.Options => securityTypes.Contains(SecurityTypes.Option),
				_ => securityTypes.Any(type => type is not SecurityTypes.Currency and
					not SecurityTypes.CryptoCurrency and not SecurityTypes.Option),
			};
		}

		var optionsRequested = Requested(EodhdMarkets.Options) || lookupMsg.OptionType != null ||
			lookupMsg.Strike != null || lookupMsg.ExpiryDate != null ||
			lookupMsg.UnderlyingSecurityId != default ||
			(exactNative && nativeKey.Market == EodhdMarkets.Options);
		if (optionsRequested && left > 0)
		{
			var exactOption = exactNative && nativeKey.Market == EodhdMarkets.Options;
			var underlying = exactOption ? nativeKey.Underlying : lookupMsg.GetUnderlyingCode();
			if (!exactOption && underlying.IsEmpty())
				underlying = value;
			if (exactOption || !underlying.IsEmpty())
			{
				for (var offset = 0; offset <= 10000 && left > 0;)
				{
					var requested = left >= 1000 ? 1000 : left + Math.Min(skip, 1000 - left);
					var pageSize = checked((int)Math.Max(1, requested));
					var page = await SafeRest().GetOptionContracts(new()
					{
						Contract = exactOption ? nativeKey.Code : null,
						UnderlyingSymbol = exactOption ? null : underlying,
						Expiry = lookupMsg.ExpiryDate,
						OptionType = lookupMsg.OptionType,
						Strike = lookupMsg.Strike,
						Sort = "exp_date",
						Offset = offset,
						Limit = Math.Max(1, pageSize),
					}, cancellationToken);
					var data = page?.Data ?? [];
					foreach (var item in data)
					{
						await Emit(item.ToSecurityMessage(lookupMsg.TransactionId));
						if (left <= 0)
							break;
					}
					if (data.Length < pageSize ||
						page?.Meta?.Total is { } total && offset + data.Length >= total)
					{
						break;
					}
					offset = checked(offset + data.Length);
					if (data.Length == 0)
						break;
					await IterationInterval.Delay(cancellationToken);
				}
			}
		}

		var referenceRequested = Requested(EodhdMarkets.Stocks) ||
			Requested(EodhdMarkets.Forex) || Requested(EodhdMarkets.Crypto);
		if (!value.IsEmpty() && left > 0 && referenceRequested)
		{
			var exchange = !board.IsEmpty() ? board.ToEodhdMarket() switch
			{
				EodhdMarkets.Forex => "FOREX",
				EodhdMarkets.Crypto => "CC",
				EodhdMarkets.Stocks => StockExchange,
				_ => null,
			} : null;
			var requested = left >= 500 ? 500 : left + Math.Min(skip, 500 - left);
			var searchLimit = checked((int)Math.Max(1, requested));
			foreach (var item in await SafeRest().Search(value, Math.Max(1, searchLimit),
				GetSearchType(securityTypes), exchange, cancellationToken) ?? [])
			{
				var security = item.ToSecurityMessage(lookupMsg.TransactionId);
				if (security != null && Requested(
					((EodhdSecurityKey.TryParse(security.SecurityId.Native as string, out var key))
						? key.Market : EodhdMarkets.Stocks)))
				{
					await Emit(security);
				}
				if (left <= 0)
					break;
			}
		}
		else if (value.IsEmpty() && left > 0)
		{
			foreach (var market in new[]
			{
				EodhdMarkets.Stocks,
				EodhdMarkets.Forex,
				EodhdMarkets.Crypto,
			})
			{
				if (!Requested(market) || left <= 0)
					continue;
				var exchange = market switch
				{
					EodhdMarkets.Forex => "FOREX",
					EodhdMarkets.Crypto => "CC",
					_ => StockExchange,
				};
				foreach (var item in await SafeRest().GetExchangeSymbols(exchange,
					IsDelisted, null, cancellationToken) ?? [])
				{
					await Emit(item.ToSecurityMessage(exchange, lookupMsg.TransactionId));
					if (left <= 0)
						break;
				}
			}
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
			await RemoveLiveSubscription(mdMsg.OriginalTransactionId, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var key = mdMsg.SecurityId.GetEodhdKey(StockExchange);
		var securityId = mdMsg.SecurityId.NormalizeEodhd(key);
		var remaining = mdMsg.Count;
		long sent;
		if (key.Market == EodhdMarkets.Options)
		{
			sent = await SendOptionLevel1(mdMsg.TransactionId, securityId, key,
				mdMsg.From?.ToUtc(), mdMsg.To?.ToUtc(), remaining, cancellationToken);
		}
		else
		{
			if (mdMsg.From != null || mdMsg.To != null)
				throw new NotSupportedException(
					"EODHD does not expose historical Level1 events for this market.");
			sent = await SendLevel1Snapshot(mdMsg.TransactionId, securityId, key,
				cancellationToken) ? 1 : 0;
		}
		if (remaining is > 0)
			remaining = Math.Max(0, remaining.Value - sent);
		if (mdMsg.IsHistoryOnly() || remaining == 0 || !CanStreamLevel1(key))
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		await AddLiveSubscription(mdMsg, securityId, key, remaining, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await RemoveLiveSubscription(mdMsg.OriginalTransactionId, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var key = mdMsg.SecurityId.GetEodhdKey(StockExchange);
		var securityId = mdMsg.SecurityId.NormalizeEodhd(key);
		if (key.Market is EodhdMarkets.Forex or EodhdMarkets.Options)
			throw new NotSupportedException("EODHD does not expose trades for this market.");
		var remaining = mdMsg.Count;
		var wantsHistory = mdMsg.From != null || mdMsg.To != null || mdMsg.IsHistoryOnly();
		if (wantsHistory)
		{
			if (!key.IsUsStock())
				throw new NotSupportedException("EODHD historical ticks are available for US stocks only.");
			var sent = await SendHistoricalTicks(mdMsg, securityId, key, cancellationToken);
			if (remaining is > 0)
				remaining = Math.Max(0, remaining.Value - sent);
		}

		if (mdMsg.IsHistoryOnly() || remaining == 0)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}
		if (!key.IsUsStock() && key.Market != EodhdMarkets.Crypto)
			throw new NotSupportedException(
				"EODHD live trades are available for US stocks and crypto only.");

		await AddLiveSubscription(mdMsg, securityId, key, remaining, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
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
			throw new NotSupportedException($"EODHD does not support {timeFrame} candles.");
		var key = mdMsg.SecurityId.GetEodhdKey(StockExchange);
		if (key.Market == EodhdMarkets.Options && timeFrame != TimeSpan.FromDays(1))
			throw new NotSupportedException("EODHD option history provides daily candles only.");
		var securityId = mdMsg.SecurityId.NormalizeEodhd(key);
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUtc();
		var from = (mdMsg.From ?? Extensions.EstimateFrom(to, timeFrame, mdMsg.Count)).ToUtc();
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.From), from,
				"The candle-history start time is after its end time.");

		SelectedBar[] bars;
		if (key.Market == EodhdMarkets.Options)
			bars = await GetOptionBars(key, from, to, mdMsg.Count, cancellationToken);
		else if (timeFrame < TimeSpan.FromDays(1))
		{
			bars = (await SafeRest().GetIntraday(key.ToRestTicker(), from, to, timeFrame,
				cancellationToken) ?? [])
				.Where(item => item?.Timestamp != null && item.Open != null && item.High != null &&
					item.Low != null && item.Close != null)
				.Select(item => new SelectedBar(Extensions.FromUnixSeconds(item.Timestamp.Value),
					item.Open.Value, item.High.Value, item.Low.Value, item.Close.Value,
					item.Volume.GetValueOrDefault(), null)).ToArray();
		}
		else
		{
			bars = (await SafeRest().GetEod(key.ToRestTicker(), from, to, timeFrame,
				cancellationToken) ?? [])
				.Where(item => item != null && Extensions.TryParseDate(item.Date, out _) &&
					item.Open != null && item.High != null && item.Low != null && item.Close != null)
				.Select(item => new SelectedBar(ParseDate(item.Date), item.Open.Value,
					item.High.Value, item.Low.Value, item.Close.Value,
					item.Volume.GetValueOrDefault(), null)).ToArray();
		}

		var left = mdMsg.Count ?? long.MaxValue;
		var emitted = new HashSet<long>();
		foreach (var bar in bars.OrderBy(item => item.OpenTime))
		{
			if (bar.OpenTime < from || bar.OpenTime > to || !emitted.Add(bar.OpenTime.Ticks))
				continue;
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = securityId,
				DataType = mdMsg.DataType2,
				OpenTime = bar.OpenTime,
				CloseTime = Extensions.GetCandleCloseTime(bar.OpenTime, timeFrame),
				OpenPrice = bar.Open,
				HighPrice = bar.High,
				LowPrice = bar.Low,
				ClosePrice = bar.Close,
				TotalVolume = bar.Volume,
				OpenInterest = bar.OpenInterest,
				State = CandleStates.Finished,
			}, cancellationToken);
			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnNewsSubscriptionAsync(MarketDataMessage mdMsg,
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

		var rawIdentity = (mdMsg.SecurityId.Native as string)
			.IsEmpty(mdMsg.SecurityId.SecurityCode);
		var hasSecurity = !rawIdentity.IsEmpty();
		var key = hasSecurity ? mdMsg.SecurityId.GetEodhdKey(StockExchange) : default;
		if (hasSecurity && key.Market == EodhdMarkets.Options)
			throw new NotSupportedException("EODHD news filters use underlying market symbols.");
		var securityId = hasSecurity ? mdMsg.SecurityId.NormalizeEodhd(key) : default;
		var from = mdMsg.From?.ToUtc();
		var to = mdMsg.To?.ToUtc();
		if (from != null && to != null && from > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.From), from,
				"The news-history start time is after its end time.");

		var target = Math.Min(mdMsg.Count ?? 100, int.MaxValue);
		var values = new List<EodhdNewsItem>();
		var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (var offset = 0; values.Count < target;)
		{
			var limit = checked((int)Math.Min(1000, target - values.Count));
			var page = await SafeRest().GetNews(hasSecurity ? key.ToRestTicker() : null,
				from, to, limit, offset, cancellationToken) ?? [];
			foreach (var item in page)
			{
				if (item != null && (item.Link.IsEmpty() || links.Add(item.Link)))
					values.Add(item);
			}
			if (page.Length < limit)
				break;
			offset = checked(offset + page.Length);
			await IterationInterval.Delay(cancellationToken);
		}

		foreach (var item in values.Select(item => new
		{
			Value = item,
			Time = Extensions.TryParseUtc(item.Date, out var time) ? time : (DateTime?)null,
		}).Where(item => item.Time != null).OrderBy(item => item.Time)
			.Take(checked((int)target)))
		{
			await SendOutMessageAsync(new NewsMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				ServerTime = item.Time.Value,
				Id = item.Value.Link,
				Headline = item.Value.Title,
				Story = item.Value.Content,
				Source = Extensions.GetNewsSource(item.Value.Link),
				Url = item.Value.Link,
				SecurityId = securityId,
			}, cancellationToken);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private async Task<bool> SendLevel1Snapshot(long transactionId, SecurityId securityId,
		EodhdSecurityKey key, CancellationToken cancellationToken)
	{
		var quote = await SafeRest().GetRealTime(key.ToRestTicker(), cancellationToken);
		if (quote?.Timestamp == null)
			return false;
		var time = Extensions.FromUnixSeconds(quote.Timestamp.Value);
		var message = new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = time,
		}
		.TryAdd(Level1Fields.OpenPrice, Positive(quote.Open))
		.TryAdd(Level1Fields.HighPrice, Positive(quote.High))
		.TryAdd(Level1Fields.LowPrice, Positive(quote.Low))
		.TryAdd(Level1Fields.LastTradePrice, Positive(quote.Close))
		.TryAdd(Level1Fields.LastTradeTime, Positive(quote.Close) != null ? time : null)
		.TryAdd(Level1Fields.SettlementPrice, Positive(quote.PreviousClose))
		.TryAdd(Level1Fields.Volume, NonNegative(quote.Volume))
		.TryAdd(Level1Fields.Change, quote.ChangePercent);
		if (message.Changes.Count == 0)
			return false;
		await SendOutMessageAsync(message, cancellationToken);
		return true;
	}

	private async Task<long> SendOptionLevel1(long transactionId, SecurityId securityId,
		EodhdSecurityKey key, DateTime? from, DateTime? to, long? count,
		CancellationToken cancellationToken)
	{
		if (from != null && to != null && from > to)
			throw new ArgumentOutOfRangeException(nameof(from), from,
				"The option-history start time is after its end time.");
		var target = checked((int)Math.Min(count ?? 1000, 1000));
		var page = await SafeRest().GetOptionEod(new()
		{
			Contract = key.Code,
			TradeTimeFrom = from,
			TradeTimeTo = to,
			Limit = Math.Max(1, target),
		}, cancellationToken);
		var values = (page?.Data ?? []).Where(item => item?.Attributes != null &&
			Extensions.TryParseUtc(item.Attributes.TradeTime, out _))
			.OrderBy(item => ParseUtc(item.Attributes.TradeTime)).Take(target).ToArray();
		foreach (var item in values)
		{
			await SendOutMessageAsync(CreateOptionLevel1(transactionId, securityId,
				item.Attributes), cancellationToken);
		}
		return values.LongLength;
	}

	private static Level1ChangeMessage CreateOptionLevel1(long transactionId,
		SecurityId securityId, EodhdOptionAttributes value)
	{
		var time = ParseUtc(value.TradeTime);
		return new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = time,
		}
		.TryAdd(Level1Fields.OpenPrice, Positive(value.Open))
		.TryAdd(Level1Fields.HighPrice, Positive(value.High))
		.TryAdd(Level1Fields.LowPrice, Positive(value.Low))
		.TryAdd(Level1Fields.LastTradePrice, Positive(value.Last))
		.TryAdd(Level1Fields.LastTradeVolume, NonNegative(value.LastSize))
		.TryAdd(Level1Fields.LastTradeTime, Positive(value.Last) != null ? time : null)
		.TryAdd(Level1Fields.SettlementPrice, Positive(value.Previous))
		.TryAdd(Level1Fields.BestBidPrice, Positive(value.Bid))
		.TryAdd(Level1Fields.BestBidVolume, NonNegative(value.BidSize))
		.TryAdd(Level1Fields.BestBidTime, ParseNullableUtc(value.BidDate))
		.TryAdd(Level1Fields.BestAskPrice, Positive(value.Ask))
		.TryAdd(Level1Fields.BestAskVolume, NonNegative(value.AskSize))
		.TryAdd(Level1Fields.BestAskTime, ParseNullableUtc(value.AskDate))
		.TryAdd(Level1Fields.Volume, NonNegative(value.Volume))
		.TryAdd(Level1Fields.OpenInterest, NonNegative(value.OpenInterest))
		.TryAdd(Level1Fields.ImpliedVolatility, NonNegative(value.Volatility))
		.TryAdd(Level1Fields.TheorPrice, Positive(value.Theoretical))
		.TryAdd(Level1Fields.Delta, value.Delta)
		.TryAdd(Level1Fields.Gamma, value.Gamma)
		.TryAdd(Level1Fields.Theta, value.Theta)
		.TryAdd(Level1Fields.Vega, value.Vega)
		.TryAdd(Level1Fields.Rho, value.Rho)
		.TryAdd(Level1Fields.Change, value.ChangePercent);
	}

	private async Task<long> SendHistoricalTicks(MarketDataMessage mdMsg, SecurityId securityId,
		EodhdSecurityKey key, CancellationToken cancellationToken)
	{
		var from = mdMsg.From?.ToUtc();
		var to = mdMsg.To?.ToUtc();
		if (from != null && to != null && from > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.From), from,
				"The tick-history start time is after its end time.");
		var limit = checked((int)Math.Min(mdMsg.Count ?? 10000, 100000));
		var ticks = await SafeRest().GetTicks(key.ToRestTicker(), from, to,
			Math.Max(1, limit), cancellationToken);
		if (ticks?.Prices == null || ticks.Timestamps == null)
			return 0;
		if (ticks.Prices.Length != ticks.Timestamps.Length)
			throw new InvalidDataException("EODHD tick price and timestamp columns differ in length.");

		var indices = Enumerable.Range(0, ticks.Prices.Length)
			.OrderBy(index => ticks.Timestamps[index])
			.ThenBy(index => ticks.Sequences != null && index < ticks.Sequences.Length
				? ticks.Sequences[index] : long.MinValue);
		long sent = 0;
		foreach (var index in indices)
		{
			var time = Extensions.FromUnixMilliseconds(ticks.Timestamps[index]);
			if (from != null && time < from || to != null && time > to)
				continue;
			await SendOutMessageAsync(new ExecutionMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = securityId,
				DataTypeEx = DataType.Ticks,
				ServerTime = time,
				TradeId = ticks.Sequences != null && index < ticks.Sequences.Length
					? ticks.Sequences[index] : null,
				TradePrice = ticks.Prices[index],
				TradeVolume = ticks.Shares != null && index < ticks.Shares.Length
					? NonNegative(ticks.Shares[index]) : null,
			}, cancellationToken);
			if (++sent >= limit)
				break;
		}
		return sent;
	}

	private async Task<SelectedBar[]> GetOptionBars(EodhdSecurityKey key, DateTime from,
		DateTime to, long? count, CancellationToken cancellationToken)
	{
		var result = new List<SelectedBar>();
		var target = Math.Min(count ?? 10000, 10000);
		for (var offset = 0; offset <= 10000 && result.Count < target;)
		{
			var limit = checked((int)Math.Min(1000, target - result.Count));
			var page = await SafeRest().GetOptionEod(new()
			{
				Contract = key.Code,
				TradeTimeFrom = from,
				TradeTimeTo = to,
				Offset = offset,
				Limit = Math.Max(1, limit),
			}, cancellationToken);
			var data = page?.Data ?? [];
			foreach (var item in data)
			{
				var value = item?.Attributes;
				if (value == null || !Extensions.TryParseUtc(value.TradeTime, out var time) ||
					value.Open == null || value.High == null || value.Low == null ||
					value.Last == null)
				{
					continue;
				}
				result.Add(new(time, value.Open.Value, value.High.Value, value.Low.Value,
					value.Last.Value, value.Volume.GetValueOrDefault(), value.OpenInterest));
			}
			if (data.Length < limit ||
				page?.Meta?.Total is { } total && offset + data.Length >= total)
			{
				break;
			}
			offset = checked(offset + data.Length);
			if (data.Length == 0)
				break;
			await IterationInterval.Delay(cancellationToken);
		}
		return [.. result];
	}

	private static bool CanStreamLevel1(EodhdSecurityKey key)
		=> key.IsUsStock() || key.Market is EodhdMarkets.Forex or EodhdMarkets.Crypto;

	private static string GetSearchType(ICollection<SecurityTypes> securityTypes)
	{
		if (securityTypes.Count != 1)
			return "all";
		return securityTypes.First() switch
		{
			SecurityTypes.Stock => "stock",
			SecurityTypes.Etf => "etf",
			SecurityTypes.Fund => "fund",
			SecurityTypes.Bond => "bond",
			SecurityTypes.Index => "index",
			SecurityTypes.CryptoCurrency => "crypto",
			_ => "all",
		};
	}

	private static DateTime ParseUtc(string value)
		=> Extensions.TryParseUtc(value, out var result) ? result :
			throw new InvalidDataException($"Invalid EODHD timestamp '{value}'.");

	private static DateTime ParseDate(string value)
		=> Extensions.TryParseDate(value, out var result) ? result :
			throw new InvalidDataException($"Invalid EODHD date '{value}'.");

	private static DateTime? ParseNullableUtc(string value)
		=> Extensions.TryParseUtc(value, out var result) ? result : null;

	private static decimal? Positive(decimal? value) => value is > 0 ? value : null;
	private static decimal? NonNegative(decimal? value) => value is >= 0 ? value : null;
}
