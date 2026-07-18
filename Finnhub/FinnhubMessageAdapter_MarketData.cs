namespace StockSharp.Finnhub;

public partial class FinnhubMessageAdapter
{
	private readonly record struct HistoricalTick(DateTime Time, decimal Price, decimal? Volume);

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var board = lookupMsg.SecurityId.BoardCode;
		var value = (lookupMsg.SecurityId.Native as string)
			.IsEmpty(lookupMsg.SecurityId.SecurityCode).IsEmpty(lookupMsg.Name);
		var skip = lookupMsg.Skip ?? 0;
		var left = lookupMsg.Count ?? long.MaxValue;
		var sent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		async Task Emit(SecurityMessage security)
		{
			if (security == null || left <= 0 ||
				!security.IsMatch(lookupMsg, securityTypes) ||
				!sent.Add($"{security.SecurityId.BoardCode}:{security.SecurityId.Native}"))
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
		var stocksRequested = board.EqualsIgnoreCase(Extensions.StockBoard) || noBoard &&
			(noTypes || securityTypes.Any(type => type is not SecurityTypes.Currency and
				not SecurityTypes.CryptoCurrency));
		var forexRequested = board.EqualsIgnoreCase(Extensions.ForexBoard) || noBoard &&
			(noTypes || securityTypes.Contains(SecurityTypes.Currency));
		var cryptoRequested = board.EqualsIgnoreCase(Extensions.CryptoBoard) || noBoard &&
			(noTypes || securityTypes.Contains(SecurityTypes.CryptoCurrency));

		if (stocksRequested && left > 0)
		{
			if (value.IsEmpty())
			{
				foreach (var symbol in await SafeRest().GetStockSymbols(StockExchange, StockMic,
					cancellationToken) ?? [])
				{
					await Emit(symbol?.ToSecurityMessage(lookupMsg.TransactionId));
					if (left <= 0)
						break;
				}
			}
			else
			{
				var response = await SafeRest().Search(value, StockExchange, cancellationToken);
				foreach (var symbol in response?.Result ?? [])
				{
					await Emit(symbol?.ToSecurityMessage(lookupMsg.TransactionId));
					if (left <= 0)
						break;
				}
			}
		}

		if (forexRequested && left > 0)
		{
			foreach (var symbol in await SafeRest().GetForexSymbols(ForexExchange,
				cancellationToken) ?? [])
			{
				if (symbol == null || !symbol.Matches(value))
					continue;
				await Emit(symbol.ToSecurityMessage(FinnhubMarkets.Forex, ForexExchange,
					lookupMsg.TransactionId));
				if (left <= 0)
					break;
			}
		}

		if (cryptoRequested && left > 0)
		{
			foreach (var symbol in await SafeRest().GetCryptoSymbols(CryptoExchange,
				cancellationToken) ?? [])
			{
				if (symbol == null || !symbol.Matches(value))
					continue;
				await Emit(symbol.ToSecurityMessage(FinnhubMarkets.Crypto, CryptoExchange,
					lookupMsg.TransactionId));
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
			throw new NotSupportedException("Finnhub does not expose historical Level1 events.");

		var market = mdMsg.SecurityId.GetFinnhubMarket(ForexExchange, CryptoExchange);
		var symbol = mdMsg.SecurityId.GetFinnhubSymbol();
		var securityId = mdMsg.SecurityId.NormalizeFinnhub(market, symbol);
		var remaining = mdMsg.Count;
		if (mdMsg.IsHistoryOnly() && market != FinnhubMarkets.Stocks)
			throw new NotSupportedException(
				"Finnhub does not expose forex or crypto Level1 snapshots through REST.");
		var snapshotSent = market == FinnhubMarkets.Stocks &&
			await SendLevel1Snapshot(mdMsg.TransactionId, securityId, symbol, cancellationToken);
		if (snapshotSent && remaining is > 0)
			remaining--;

		if (mdMsg.IsHistoryOnly() || remaining == 0)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		await AddLiveSubscription(mdMsg, securityId, symbol, remaining, cancellationToken);
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

		var market = mdMsg.SecurityId.GetFinnhubMarket(ForexExchange, CryptoExchange);
		var symbol = mdMsg.SecurityId.GetFinnhubSymbol();
		var securityId = mdMsg.SecurityId.NormalizeFinnhub(market, symbol);
		var remaining = mdMsg.Count;
		if (mdMsg.From != null || mdMsg.To != null || mdMsg.IsHistoryOnly())
		{
			if (market != FinnhubMarkets.Stocks)
				throw new NotSupportedException(
					"Finnhub REST tick history is available only for exchange-listed securities.");
			var sent = await SendHistoricalTicks(mdMsg, securityId, symbol, remaining,
				cancellationToken);
			if (remaining != null)
				remaining = Math.Max(0, remaining.Value - sent);
		}

		if (mdMsg.IsHistoryOnly() || remaining == 0)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		await AddLiveSubscription(mdMsg, securityId, symbol, remaining, cancellationToken);
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
			throw new NotSupportedException($"Finnhub does not support {timeFrame} candles.");
		var market = mdMsg.SecurityId.GetFinnhubMarket(ForexExchange, CryptoExchange);
		var symbol = mdMsg.SecurityId.GetFinnhubSymbol();
		var securityId = mdMsg.SecurityId.NormalizeFinnhub(market, symbol);
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUtc();
		var from = (mdMsg.From ?? Extensions.EstimateFrom(to, timeFrame, mdMsg.Count)).ToUtc();
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.From), from,
				"The candle-history start time is after its end time.");

		var left = mdMsg.Count ?? long.MaxValue;
		var emitted = new HashSet<long>();
		var current = from;
		while (current <= to && left > 0)
		{
			var chunkTo = timeFrame < TimeSpan.FromDays(1) && to - current > TimeSpan.FromDays(30)
				? current.AddDays(30) : to;
			var candles = await SafeRest().GetCandles(market, symbol,
				timeFrame.ToNativeResolution(), current, chunkTo, cancellationToken);
			if (candles != null && !candles.Status.EqualsIgnoreCase("no_data"))
			{
				var count = GetCandleCount(candles);
				foreach (var index in Enumerable.Range(0, count)
					.OrderBy(index => candles.Timestamp[index]))
				{
					var openTime = Extensions.FromUnixSeconds(candles.Timestamp[index]);
					if (openTime < from || openTime > to || !emitted.Add(candles.Timestamp[index]))
						continue;
					await SendOutMessageAsync(new TimeFrameCandleMessage
					{
						OriginalTransactionId = mdMsg.TransactionId,
						SecurityId = securityId,
						DataType = mdMsg.DataType2,
						OpenTime = openTime,
						CloseTime = Extensions.GetCandleCloseTime(openTime, timeFrame),
						OpenPrice = candles.Open[index],
						HighPrice = candles.High[index],
						LowPrice = candles.Low[index],
						ClosePrice = candles.Close[index],
						TotalVolume = candles.Volume[index],
						State = CandleStates.Finished,
					}, cancellationToken);
					if (--left <= 0)
						break;
				}
			}

			if (chunkTo >= to)
				break;
			current = chunkTo.AddSeconds(1);
			await IterationInterval.Delay(cancellationToken);
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

		var symbol = (mdMsg.SecurityId.Native as string).IsEmpty(mdMsg.SecurityId.SecurityCode);
		FinnhubNewsItem[] news;
		if (!symbol.IsEmpty())
		{
			var market = mdMsg.SecurityId.GetFinnhubMarket(ForexExchange, CryptoExchange);
			if (market != FinnhubMarkets.Stocks)
				throw new NotSupportedException("Finnhub company news accepts stock symbols only.");
			symbol = mdMsg.SecurityId.GetFinnhubSymbol();
			var to = (mdMsg.To ?? DateTime.UtcNow).ToUtc();
			var from = (mdMsg.From ?? to.AddDays(-30)).ToUtc();
			if (from > to)
				throw new ArgumentOutOfRangeException(nameof(mdMsg.From), from,
					"The news-history start time is after its end time.");
			news = await SafeRest().GetCompanyNews(symbol, from, to, cancellationToken) ?? [];
		}
		else
		{
			news = await SafeRest().GetMarketNews(NewsCategory, null, cancellationToken) ?? [];
		}

		var fromFilter = mdMsg.From?.ToUtc();
		var toFilter = mdMsg.To?.ToUtc();
		var ordered = news.Where(item => item?.Timestamp is > 0)
			.OrderBy(item => item.Timestamp)
			.ToArray();
		if (mdMsg.From == null && mdMsg.Count is > 0 && ordered.LongLength > mdMsg.Count.Value)
			ordered = ordered.TakeLast(checked((int)Math.Min(mdMsg.Count.Value, int.MaxValue))).ToArray();
		var left = mdMsg.Count ?? 100;
		foreach (var item in ordered)
		{
			var serverTime = Extensions.FromUnixSeconds(item.Timestamp.Value);
			if (fromFilter != null && serverTime < fromFilter.Value)
				continue;
			if (toFilter != null && serverTime > toFilter.Value)
				break;
			var securityId = default(SecurityId);
			if (!symbol.IsEmpty())
				securityId = mdMsg.SecurityId.NormalizeFinnhub(FinnhubMarkets.Stocks, symbol);
			await SendOutMessageAsync(new NewsMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				ServerTime = serverTime,
				Id = item.Id?.ToString(CultureInfo.InvariantCulture),
				Headline = item.Headline,
				Story = item.Summary,
				Source = item.Source,
				Url = item.Url,
				SecurityId = securityId,
			}, cancellationToken);
			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private async Task<bool> SendLevel1Snapshot(long transactionId, SecurityId securityId,
		string symbol, CancellationToken cancellationToken)
	{
		var quote = await SafeRest().GetQuote(symbol, cancellationToken);
		var bidAsk = IsBidAskEnabled
			? await SafeRest().GetBidAsk(symbol, cancellationToken) : null;
		var quoteTime = quote?.Timestamp is > 0
			? Extensions.FromUnixSeconds(quote.Timestamp.Value) : (DateTime?)null;
		var bidAskTime = bidAsk?.Timestamp is > 0
			? Extensions.FromUnixMilliseconds(bidAsk.Timestamp.Value) : (DateTime?)null;
		var serverTime = quoteTime;
		if (bidAskTime != null && (serverTime == null || bidAskTime > serverTime))
			serverTime = bidAskTime;
		if (serverTime == null)
			return false;

		var message = new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = serverTime.Value,
		}
		.TryAdd(Level1Fields.LastTradePrice, Positive(quote?.Current))
		.TryAdd(Level1Fields.LastTradeTime, Positive(quote?.Current) != null ? quoteTime : null)
		.TryAdd(Level1Fields.OpenPrice, Positive(quote?.Open))
		.TryAdd(Level1Fields.HighPrice, Positive(quote?.High))
		.TryAdd(Level1Fields.LowPrice, Positive(quote?.Low))
		.TryAdd(Level1Fields.SettlementPrice, Positive(quote?.PreviousClose))
		.TryAdd(Level1Fields.Change, quote?.PercentChange)
		.TryAdd(Level1Fields.BestBidPrice, Positive(bidAsk?.BidPrice))
		.TryAdd(Level1Fields.BestBidVolume, Positive(bidAsk?.BidVolume))
		.TryAdd(Level1Fields.BestBidTime, Positive(bidAsk?.BidPrice) != null ? bidAskTime : null)
		.TryAdd(Level1Fields.BestAskPrice, Positive(bidAsk?.AskPrice))
		.TryAdd(Level1Fields.BestAskVolume, Positive(bidAsk?.AskVolume))
		.TryAdd(Level1Fields.BestAskTime, Positive(bidAsk?.AskPrice) != null ? bidAskTime : null);
		if (message.Changes.Count == 0)
			return false;
		await SendOutMessageAsync(message, cancellationToken);
		return true;
	}

	private async Task<long> SendHistoricalTicks(MarketDataMessage mdMsg,
		SecurityId securityId, string symbol, long? requestedCount,
		CancellationToken cancellationToken)
	{
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUtc();
		if (mdMsg.From is { } requestedFrom)
		{
			var from = requestedFrom.ToUtc();
			if (from > to)
				throw new ArgumentOutOfRangeException(nameof(mdMsg.From), from,
					"The tick-history start time is after its end time.");
			return await SendForwardTicks(mdMsg.TransactionId, securityId, symbol, from, to,
				requestedCount, cancellationToken);
		}

		var target = Math.Min(requestedCount is > 0 ? requestedCount.Value : 1000,
			int.MaxValue);
		var collected = new List<HistoricalTick>();
		for (var days = 0; days < 31 && collected.Count < target; days++)
		{
			var date = to.Date.AddDays(-days);
			var needed = target - collected.Count;
			await CollectLatestTicks(symbol, date, to, collected, needed, cancellationToken);
			await IterationInterval.Delay(cancellationToken);
		}

		var selected = collected.OrderBy(item => item.Time)
			.TakeLast(checked((int)target)).ToArray();
		foreach (var tick in selected)
			await SendTick(mdMsg.TransactionId, securityId, tick, cancellationToken);
		return selected.LongLength;
	}

	private async Task<long> SendForwardTicks(long transactionId, SecurityId securityId,
		string symbol, DateTime from, DateTime to, long? requestedCount,
		CancellationToken cancellationToken)
	{
		var left = requestedCount ?? long.MaxValue;
		var sent = 0L;
		for (var date = from.Date; date <= to.Date && left > 0; date = date.AddDays(1))
		{
			var skip = 0L;
			while (left > 0)
			{
				var limit = (int)Math.Min(25000, left);
				var response = await SafeRest().GetTicks(symbol, date, limit, skip,
					cancellationToken);
				var count = GetTickCount(response);
				if (count == 0)
					break;
				for (var index = 0; index < count && left > 0; index++)
				{
					var tick = ToHistoricalTick(response, index);
					if (tick.Time < from || tick.Time > to)
						continue;
					await SendTick(transactionId, securityId, tick, cancellationToken);
					sent++;
					left--;
				}
				skip += response.Count is > 0 ? response.Count.Value : count;
				if (count < limit || skip >= (response.Total ?? skip))
					break;
				await IterationInterval.Delay(cancellationToken);
			}
		}
		return sent;
	}

	private async Task CollectLatestTicks(string symbol, DateTime date, DateTime to,
		List<HistoricalTick> output, long limit, CancellationToken cancellationToken)
	{
		var initialCount = output.Count;
		var probe = await SafeRest().GetTicks(symbol, date, 1, 0, cancellationToken);
		var cursor = probe?.Total ?? probe?.Count ?? 0;
		while (cursor > 0 && output.Count - initialCount < limit)
		{
			var pageSize = (int)Math.Min(25000, cursor);
			var skip = Math.Max(0, cursor - pageSize);
			var response = await SafeRest().GetTicks(symbol, date, pageSize, skip,
				cancellationToken);
			var count = GetTickCount(response);
			if (count == 0)
				break;
			for (var index = count - 1;
				index >= 0 && output.Count - initialCount < limit; index--)
			{
				var tick = ToHistoricalTick(response, index);
				if (tick.Time <= to)
					output.Add(tick);
			}
			cursor = skip;
			await IterationInterval.Delay(cancellationToken);
		}
	}

	private ValueTask SendTick(long transactionId, SecurityId securityId, HistoricalTick tick,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			DataTypeEx = DataType.Ticks,
			ServerTime = tick.Time,
			TradePrice = tick.Price,
			TradeVolume = tick.Volume,
		}, cancellationToken);

	private static HistoricalTick ToHistoricalTick(FinnhubTicks ticks, int index)
		=> new(Extensions.FromUnixMilliseconds(ticks.Timestamp[index]), ticks.Price[index],
			Positive(ticks.Volume[index]));

	private static int GetTickCount(FinnhubTicks ticks)
		=> ticks == null ? 0 :
			Math.Min(ticks.Timestamp?.Length ?? 0,
				Math.Min(ticks.Price?.Length ?? 0, ticks.Volume?.Length ?? 0));

	private static int GetCandleCount(FinnhubCandles candles)
		=> candles == null ? 0 :
			new[]
			{
				candles.Timestamp?.Length ?? 0,
				candles.Open?.Length ?? 0,
				candles.High?.Length ?? 0,
				candles.Low?.Length ?? 0,
				candles.Close?.Length ?? 0,
				candles.Volume?.Length ?? 0,
			}.Min();

	private static decimal? Positive(decimal? value)
		=> value is > 0 ? value : null;
}
