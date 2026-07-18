namespace StockSharp.FinancialModelingPrep;

public partial class FmpMessageAdapter
{
	private readonly record struct ParsedBar(FmpBar Value, DateTime OpenTime);

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var board = lookupMsg.SecurityId.BoardCode;
		var native = lookupMsg.SecurityId.Native as string;
		var value = native.IsEmpty(lookupMsg.SecurityId.SecurityCode).IsEmpty(lookupMsg.Name);
		var exactNative = FmpSecurityKey.TryParse(native, out var nativeKey);
		if (exactNative)
			value = nativeKey.Symbol;
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		if (exactNative)
			left = Math.Min(left, 1);
		var sent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		async Task Emit(FmpSymbolItem item, FmpMarkets market)
		{
			if (item == null || left <= 0 || !item.Matches(value))
				return;
			var security = item.ToSecurityMessage(market, lookupMsg.TransactionId);
			var key = security?.SecurityId.Native as string;
			if (security == null || key.IsEmpty() ||
				!security.IsMatch(lookupMsg, securityTypes) || !sent.Add(key))
			{
				return;
			}
			if (market == FmpMarkets.Stocks)
			{
				var exchange = exactNative ? nativeKey.Exchange : StockExchange;
				if (!exchange.IsEmpty() && !security.Class.EqualsIgnoreCase(exchange))
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
		bool Requested(FmpMarkets market)
		{
			if (exactNative)
				return nativeKey.Market == market;
			if (!noBoard)
				return board.EqualsIgnoreCase(market.ToBoard());
			if (noTypes)
				return true;
			return market switch
			{
				FmpMarkets.Forex => securityTypes.Contains(SecurityTypes.Currency),
				FmpMarkets.Crypto => securityTypes.Contains(SecurityTypes.CryptoCurrency),
				FmpMarkets.Indices => securityTypes.Contains(SecurityTypes.Index),
				FmpMarkets.Commodities => securityTypes.Contains(SecurityTypes.Commodity),
				_ => securityTypes.Any(type => type is not SecurityTypes.Currency and
					not SecurityTypes.CryptoCurrency and not SecurityTypes.Index and
					not SecurityTypes.Commodity),
			};
		}

		var searchLimit = checked((int)Math.Clamp(Math.Min(skip, 250) +
			Math.Min(left, 250), 1, 250));
		if (!value.IsEmpty() && left > 0)
		{
			var requestedMarkets = Enum.GetValues<FmpMarkets>().Where(Requested).ToArray();
			var exchange = requestedMarkets.Length == 1 &&
				requestedMarkets[0] == FmpMarkets.Stocks
				? exactNative ? nativeKey.Exchange : StockExchange : null;
			foreach (var item in await SafeRest().SearchSymbol(value, searchLimit, exchange,
				cancellationToken) ?? [])
			{
				var market = item.GetMarket();
				if (Requested(market))
					await Emit(item, market);
				if (left <= 0)
					break;
			}
			if (left > 0)
			{
				foreach (var item in await SafeRest().SearchName(value, searchLimit, exchange,
					cancellationToken) ?? [])
				{
					var market = item.GetMarket();
					if (Requested(market))
						await Emit(item, market);
					if (left <= 0)
						break;
				}
			}
		}

		foreach (var market in new[]
		{
			FmpMarkets.Stocks,
			FmpMarkets.Forex,
			FmpMarkets.Crypto,
			FmpMarkets.Indices,
			FmpMarkets.Commodities,
		})
		{
			if (!Requested(market) || left <= 0)
			{
				continue;
			}
			if (market == FmpMarkets.Stocks)
			{
				var pageSize = checked((int)Math.Clamp(Math.Min(skip, 1000) +
					Math.Min(left, 1000), 1, 1000));
				for (var page = 0; left > 0; page++)
				{
					var values = await SafeRest().GetStockScreener(StockExchange, page,
						pageSize, cancellationToken) ?? [];
					foreach (var item in values)
					{
						await Emit(item, market);
						if (left <= 0)
							break;
					}
					if (values.Length < pageSize)
						break;
					await IterationInterval.Delay(cancellationToken);
				}
				continue;
			}
			foreach (var item in await SafeRest().GetSymbols(market,
				cancellationToken) ?? [])
			{
				await Emit(item, market);
				if (left <= 0)
					break;
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
		if (mdMsg.From != null || mdMsg.To != null)
			throw new NotSupportedException("FMP does not expose historical Level1 events.");

		var key = mdMsg.SecurityId.GetFmpKey(StockExchange);
		var securityId = mdMsg.SecurityId.NormalizeFmp(key);
		var remaining = mdMsg.Count;
		if (await SendLevel1Snapshot(mdMsg.TransactionId, securityId, key, cancellationToken) &&
			remaining is > 0)
		{
			remaining--;
		}

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
		if (mdMsg.From != null || mdMsg.To != null || mdMsg.IsHistoryOnly())
			throw new NotSupportedException("FMP REST does not expose historical tick trades.");

		var key = mdMsg.SecurityId.GetFmpKey(StockExchange);
		if (!CanStreamTicks(key))
			throw new NotSupportedException(
				"FMP tick streaming is available for US stocks and crypto only.");
		var securityId = mdMsg.SecurityId.NormalizeFmp(key);
		await AddLiveSubscription(mdMsg, securityId, key, mdMsg.Count, cancellationToken);
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
		var key = mdMsg.SecurityId.GetFmpKey(StockExchange);
		if (!key.Market.IsTimeFrameSupported(timeFrame))
			throw new NotSupportedException(
				$"FMP does not document {timeFrame} candles for {key.Market}.");
		var securityId = mdMsg.SecurityId.NormalizeFmp(key);
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUtc();
		var from = (mdMsg.From ?? Extensions.EstimateFrom(to, timeFrame, mdMsg.Count)).ToUtc();
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.From), from,
				"The candle-history start time is after its end time.");

		FmpBar[] values;
		if (timeFrame == TimeSpan.FromDays(1))
		{
			values = await SafeRest().GetEod(key.Symbol, from, to,
				key.Market == FmpMarkets.Stocks ? EodAdjustment : FmpEodAdjustments.Adjusted,
				cancellationToken) ?? [];
		}
		else
		{
			values = await SafeRest().GetIntraday(key.Symbol, from, to, timeFrame,
				key.Market == FmpMarkets.Stocks &&
				EodAdjustment == FmpEodAdjustments.NonSplitAdjusted, cancellationToken) ?? [];
		}

		var parsed = new List<ParsedBar>(values.Length);
		foreach (var value in values)
		{
			if (value == null)
				continue;
			var hasTime = timeFrame == TimeSpan.FromDays(1)
				? Extensions.TryParseDate(value.Date, out var openTime)
				: Extensions.TryParseIntradayUtc(value.Date, _intradayTimeZone, out openTime);
			if (hasTime)
				parsed.Add(new(value, openTime));
		}

		var left = mdMsg.Count ?? long.MaxValue;
		var emitted = new HashSet<long>();
		foreach (var item in parsed.OrderBy(item => item.OpenTime))
		{
			var open = item.Value.Open ?? item.Value.AdjustedOpen;
			var high = item.Value.High ?? item.Value.AdjustedHigh;
			var low = item.Value.Low ?? item.Value.AdjustedLow;
			var close = item.Value.Close ?? item.Value.AdjustedClose;
			if (left <= 0 || item.OpenTime < from || item.OpenTime > to ||
				!emitted.Add(item.OpenTime.Ticks) || open == null || high == null ||
				low == null || close == null)
			{
				continue;
			}
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = securityId,
				DataType = mdMsg.DataType2,
				OpenTime = item.OpenTime,
				CloseTime = Extensions.GetCandleCloseTime(item.OpenTime, timeFrame),
				OpenPrice = open.Value,
				HighPrice = high.Value,
				LowPrice = low.Value,
				ClosePrice = close.Value,
				TotalVolume = NonNegative(item.Value.Volume).GetValueOrDefault(),
				State = CandleStates.Finished,
			}, cancellationToken);
			left--;
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
		var market = hasSecurity ? mdMsg.SecurityId.GetFmpKey(StockExchange).Market :
			mdMsg.SecurityId.BoardCode.ToFmpMarket();
		if (market is FmpMarkets.Indices or FmpMarkets.Commodities)
			throw new NotSupportedException(
				"FMP has no dedicated index or commodity news endpoint.");
		var key = hasSecurity ? mdMsg.SecurityId.GetFmpKey(StockExchange) : default;
		var securityId = hasSecurity ? mdMsg.SecurityId.NormalizeFmp(key) : default;
		var from = mdMsg.From?.ToUtc();
		var to = mdMsg.To?.ToUtc();
		if (from != null && to != null && from > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.From), from,
				"The news-history start time is after its end time.");

		var target = Math.Min(mdMsg.Count ?? 100, int.MaxValue);
		var values = new List<FmpNewsItem>();
		var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var pageSize = checked((int)Math.Min(250, target));
		for (var page = 0; page <= 100 && values.Count < target; page++)
		{
			var result = await SafeRest().GetNews(market,
				hasSecurity ? key.Symbol : null, from, to, page, pageSize,
				cancellationToken) ?? [];
			foreach (var item in result)
			{
				if (item != null && (item.Url.IsEmpty() || links.Add(item.Url)))
					values.Add(item);
			}
			if (result.Length < pageSize)
				break;
			await IterationInterval.Delay(cancellationToken);
		}

		foreach (var item in values.Select(item => new
		{
			Value = item,
			Time = Extensions.TryParseNewsTime(item.PublishedDate, out var time)
				? time : (DateTime?)null,
		}).Where(item => item.Time != null).OrderBy(item => item.Time)
			.Take(checked((int)target)))
		{
			var itemSecurityId = securityId;
			if (!hasSecurity && !item.Value.Symbol.IsEmpty())
			{
				var symbol = Extensions.NormalizeSymbol(item.Value.Symbol, market);
				itemSecurityId = new()
				{
					SecurityCode = symbol,
					BoardCode = market.ToBoard(),
					Native = new FmpSecurityKey(market, symbol, null).ToNative(),
				};
			}
			await SendOutMessageAsync(new NewsMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				ServerTime = item.Time.Value,
				Id = item.Value.Url,
				Headline = item.Value.Title,
				Story = item.Value.Text,
				Source = item.Value.Publisher.IsEmpty(item.Value.Site)
					.IsEmpty(Extensions.GetNewsSource(item.Value.Url)),
				Url = item.Value.Url,
				SecurityId = itemSecurityId,
			}, cancellationToken);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private async Task<bool> SendLevel1Snapshot(long transactionId, SecurityId securityId,
		FmpSecurityKey key, CancellationToken cancellationToken)
	{
		var quotes = await SafeRest().GetQuote(key.Symbol, cancellationToken) ?? [];
		var quote = quotes.FirstOrDefault(value =>
			value?.Symbol.EqualsIgnoreCase(key.Symbol) == true) ?? quotes.FirstOrDefault();
		if (quote?.Timestamp is not > 0)
			return false;
		var time = Extensions.FromUnixTimestamp(quote.Timestamp.Value);
		var message = new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = time,
		}
		.TryAdd(Level1Fields.OpenPrice, Positive(quote.Open))
		.TryAdd(Level1Fields.HighPrice, Positive(quote.DayHigh))
		.TryAdd(Level1Fields.LowPrice, Positive(quote.DayLow))
		.TryAdd(Level1Fields.LastTradePrice, Positive(quote.Price))
		.TryAdd(Level1Fields.LastTradeTime, Positive(quote.Price) != null ? time : null)
		.TryAdd(Level1Fields.SettlementPrice, Positive(quote.PreviousClose))
		.TryAdd(Level1Fields.Volume, NonNegative(quote.Volume))
		.TryAdd(Level1Fields.HighPrice52Week, Positive(quote.YearHigh))
		.TryAdd(Level1Fields.LowPrice52Week, Positive(quote.YearLow))
		.TryAdd(Level1Fields.Change, quote.ChangePercentage);
		if (message.Changes.Count == 0)
			return false;
		await SendOutMessageAsync(message, cancellationToken);
		return true;
	}

	private static decimal? Positive(decimal? value)
		=> value is > 0 ? value : null;

	private static decimal? NonNegative(decimal? value)
		=> value is >= 0 ? value : null;
}
