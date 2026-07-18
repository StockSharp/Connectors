namespace StockSharp.ThetaData;

public partial class ThetaDataMessageAdapter
{
	private readonly record struct ParsedQuote(ThetaQuote Value, DateTime Time);
	private readonly record struct ParsedTrade(ThetaTrade Value, DateTime Time);
	private readonly record struct ParsedPrice(ThetaPrice Value, DateTime Time);
	private readonly record struct ParsedBar(ThetaBar Value, DateTime OpenTime);
	private readonly record struct ParsedEod(ThetaEod Value, DateTime OpenTime);

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

		var types = lookupMsg.GetSecurityTypes();
		var board = lookupMsg.SecurityId.BoardCode;
		var native = lookupMsg.SecurityId.Native as string;
		var hasExactKey = ThetaSecurityKey.TryParse(native, out var exactKey);
		var value = (hasExactKey ? exactKey.Root :
			native.IsEmpty(lookupMsg.SecurityId.SecurityCode).IsEmpty(lookupMsg.Name))?.Trim();
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		async Task Emit(SecurityMessage security)
		{
			var id = security?.SecurityId.Native as string;
			if (security == null || id.IsEmpty() || left <= 0 ||
				!security.IsMatch(lookupMsg, types) || !emitted.Add(id))
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

		bool Requested(ThetaDataMarkets market)
		{
			if (hasExactKey)
				return exactKey.Market == market;
			if (!board.IsEmpty())
				return board.EqualsIgnoreCase(market.ToBoard());
			if (types.Count == 0)
				return market is ThetaDataMarkets.Stocks or ThetaDataMarkets.Indices;
			return market switch
			{
				ThetaDataMarkets.Stocks => types.Contains(SecurityTypes.Stock),
				ThetaDataMarkets.Options => types.Contains(SecurityTypes.Option),
				ThetaDataMarkets.Indices => types.Contains(SecurityTypes.Index),
				_ => false,
			};
		}

		foreach (var market in new[] { ThetaDataMarkets.Stocks, ThetaDataMarkets.Indices })
		{
			if (!Requested(market) || left <= 0)
				continue;
			foreach (var symbol in await SafeRest().GetSymbols(market, cancellationToken))
			{
				if (symbol?.Symbol.IsEmpty() != false ||
					(!value.IsEmpty() && (hasExactKey
						? !symbol.Symbol.EqualsIgnoreCase(value)
						: !symbol.Symbol.ContainsIgnoreCase(value))))
				{
					continue;
				}
				await Emit(symbol.ToSecurityMessage(market, lookupMsg.TransactionId));
				if (left <= 0)
					break;
			}
		}

		if (Requested(ThetaDataMarkets.Options) && left > 0)
		{
			var root = hasExactKey ? exactKey.Root : lookupMsg.GetUnderlyingCode();
			if (root.IsEmpty())
			{
				if (Extensions.TryGetOptionKey(value, out var optionKey))
				{
					exactKey = optionKey;
					hasExactKey = true;
					root = optionKey.Root;
				}
				else
					root = value;
			}

			if (!root.IsEmpty())
			{
				var marketDate = DateTime.UtcNow.ToMarketDate(_marketTimeZone);
				var queryDate = hasExactKey && exactKey.Market == ThetaDataMarkets.Options &&
					exactKey.Expiration.Date < marketDate
					? exactKey.Expiration.Date : marketDate;
				if (lookupMsg.ExpiryDate is { } expiry && expiry.Date < queryDate)
					queryDate = expiry.Date;

				var contracts = new List<ThetaOptionContract>();
				for (var attempt = 0; attempt < 8; attempt++)
				{
					var values = await SafeRest().GetOptionContracts(root, queryDate.AddDays(-attempt),
						cancellationToken);
					contracts.AddRange(values.Where(item => item != null));
					if (values.Length > 0)
						break;
				}

				foreach (var contract in contracts)
				{
					if (!contract.TryGetKey(out var key) ||
						(hasExactKey && exactKey.Market == ThetaDataMarkets.Options && key != exactKey) ||
						(!lookupMsg.IncludeExpired && key.Expiration.Date < marketDate))
					{
						continue;
					}
					await Emit(contract.ToSecurityMessage(lookupMsg.TransactionId));
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
		if (await FinishEmptySubscription(mdMsg, cancellationToken))
			return;

		var key = mdMsg.SecurityId.GetThetaKey();
		var securityId = mdMsg.SecurityId.NormalizeTheta(key);
		var emitted = 0L;
		var hasHistoryRange = mdMsg.From != null || mdMsg.To != null || mdMsg.IsHistoryOnly();
		if (hasHistoryRange)
		{
			var (from, to) = GetHistoryRange(mdMsg);
			if (key.Market == ThetaDataMarkets.Indices)
			{
				var values = await LoadPrices(key, from, to, cancellationToken);
				foreach (var item in SelectHistory(values, item => item.Time, mdMsg))
				{
					var message = CreatePriceLevel1(mdMsg.TransactionId, securityId,
						item.Time, item.Value.Price);
					if (message == null)
						continue;
					await SendOutMessageAsync(message, cancellationToken);
					emitted++;
				}
			}
			else
			{
				var values = await LoadQuotes(key, from, to, cancellationToken);
				foreach (var item in SelectHistory(values, item => item.Time, mdMsg))
				{
					var message = CreateQuoteLevel1(mdMsg.TransactionId, securityId,
						item.Time, item.Value.Bid, item.Value.BidSize,
						item.Value.Ask, item.Value.AskSize);
					if (message == null)
						continue;
					await SendOutMessageAsync(message, cancellationToken);
					emitted++;
				}
			}
		}
		else
			emitted = await SendLevel1Snapshot(mdMsg.TransactionId, securityId, key,
				cancellationToken) ? 1 : 0;

		var remaining = Subtract(mdMsg.Count, emitted);
		if (mdMsg.IsHistoryOnly() || remaining == 0 ||
			key.Market == ThetaDataMarkets.Stocks && StockVenue == ThetaDataStockVenues.UtpCta)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		await AddLiveSubscription(mdMsg, securityId, key, remaining, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await RemoveLiveSubscription(mdMsg.OriginalTransactionId, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		if (await FinishEmptySubscription(mdMsg, cancellationToken))
			return;

		var key = mdMsg.SecurityId.GetThetaKey();
		if (key.Market == ThetaDataMarkets.Indices)
			throw new NotSupportedException("ThetaData index feeds do not publish a bid/ask book.");
		var securityId = mdMsg.SecurityId.NormalizeTheta(key);
		var emitted = 0L;
		var hasHistoryRange = mdMsg.From != null || mdMsg.To != null || mdMsg.IsHistoryOnly();
		if (hasHistoryRange)
		{
			var (from, to) = GetHistoryRange(mdMsg);
			var values = await LoadQuotes(key, from, to, cancellationToken);
			foreach (var item in SelectHistory(values, item => item.Time, mdMsg))
			{
				var message = CreateDepth(mdMsg.TransactionId, securityId, item.Time,
					item.Value.Bid, item.Value.BidSize, item.Value.Ask, item.Value.AskSize);
				if (message == null)
					continue;
				await SendOutMessageAsync(message, cancellationToken);
				emitted++;
			}
		}
		else
			emitted = await SendDepthSnapshot(mdMsg.TransactionId, securityId, key,
				cancellationToken) ? 1 : 0;

		var remaining = Subtract(mdMsg.Count, emitted);
		if (mdMsg.IsHistoryOnly() || remaining == 0 ||
			key.Market == ThetaDataMarkets.Stocks && StockVenue == ThetaDataStockVenues.UtpCta)
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
		if (await FinishEmptySubscription(mdMsg, cancellationToken))
			return;

		var key = mdMsg.SecurityId.GetThetaKey();
		if (key.Market == ThetaDataMarkets.Indices)
			throw new NotSupportedException(
				"ThetaData index prices are not exchange trade ticks.");
		var securityId = mdMsg.SecurityId.NormalizeTheta(key);
		var emitted = 0L;
		var hasHistoryRange = mdMsg.From != null || mdMsg.To != null || mdMsg.IsHistoryOnly();
		if (hasHistoryRange)
		{
			var (from, to) = GetHistoryRange(mdMsg);
			var values = await LoadTrades(key, from, to, cancellationToken);
			foreach (var item in SelectHistory(values, item => item.Time, mdMsg))
			{
				var message = CreateTick(mdMsg.TransactionId, securityId, item.Time,
					item.Value.Price, item.Value.Size, item.Value.Sequence, item.Value.Condition);
				if (message == null)
					continue;
				await SendOutMessageAsync(message, cancellationToken);
				emitted++;
			}
		}

		var remaining = Subtract(mdMsg.Count, emitted);
		if (mdMsg.IsHistoryOnly() || remaining == 0)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}
		if (key.Market == ThetaDataMarkets.Stocks && StockVenue == ThetaDataStockVenues.UtpCta)
		{
			throw new NotSupportedException(
				"ThetaData stock WebSocket streams Nasdaq Basic only. Select NasdaqBasic " +
				"or request historical SIP trades.");
		}

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
		if (await FinishEmptySubscription(mdMsg, cancellationToken))
			return;

		var timeFrame = mdMsg.GetTimeFrame();
		if (!Extensions.TimeFrames.Contains(timeFrame))
			throw new NotSupportedException($"ThetaData does not document {timeFrame} candles.");
		var key = mdMsg.SecurityId.GetThetaKey();
		var securityId = mdMsg.SecurityId.NormalizeTheta(key);
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUtc();
		var from = (mdMsg.From ?? Extensions.EstimateFrom(to, timeFrame, mdMsg.Count)).ToUtc();
		EnsureRange(from, to);

		if (timeFrame == TimeSpan.FromDays(1))
		{
			var values = await SafeRest().GetEod(key, from.ToMarketDate(_marketTimeZone),
				to.ToMarketDate(_marketTimeZone), cancellationToken);
			var parsed = new List<ParsedEod>();
			foreach (var value in values)
			{
				if (value == null || !TryGetEodOpenTime(value, out var openTime) ||
					openTime < from.AddDays(-1) || openTime > to)
				{
					continue;
				}
				parsed.Add(new(value, openTime));
			}
			foreach (var item in SelectHistory(parsed, item => item.OpenTime, mdMsg, true))
			{
				if (!TryGetOhlc(item.Value, out var open, out var high, out var low,
					out var close))
				{
					continue;
				}
				await SendOutMessageAsync(new TimeFrameCandleMessage
				{
					OriginalTransactionId = mdMsg.TransactionId,
					SecurityId = securityId,
					DataType = mdMsg.DataType2,
					OpenTime = item.OpenTime,
					CloseTime = GetDailyCloseTime(item.OpenTime),
					OpenPrice = open,
					HighPrice = high,
					LowPrice = low,
					ClosePrice = close,
					TotalVolume = item.Value.Volume.GetValueOrDefault(),
					TotalTicks = ToCount(item.Value.Count),
					State = CandleStates.Finished,
				}, cancellationToken);
			}
		}
		else
		{
			var parsed = new List<ParsedBar>();
			foreach (var session in GetSessions(from, to))
			{
				var values = await SafeRest().GetBars(key, session.Date, timeFrame,
					session.Start, session.End, StockVenue, cancellationToken);
				foreach (var value in values)
				{
					if (value != null && Extensions.TryParseMarketTime(value.Timestamp,
						_marketTimeZone, out var openTime) && openTime >= from && openTime <= to)
					{
						parsed.Add(new(value, openTime));
					}
				}
				await IterationInterval.Delay(cancellationToken);
			}
			foreach (var item in SelectHistory(parsed, item => item.OpenTime, mdMsg, true))
			{
				if (!TryGetOhlc(item.Value, out var open, out var high, out var low,
					out var close))
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
					TotalVolume = item.Value.Volume.GetValueOrDefault(),
					TotalTicks = ToCount(item.Value.Count),
					State = CandleStates.Finished,
				}, cancellationToken);
			}
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private async Task<List<ParsedQuote>> LoadQuotes(ThetaSecurityKey key, DateTime from,
		DateTime to, CancellationToken cancellationToken)
	{
		var result = new List<ParsedQuote>();
		foreach (var session in GetSessions(from, to))
		{
			var values = await SafeRest().GetHistoryQuotes(key, session.Date,
				session.Start, session.End, StockVenue, cancellationToken);
			foreach (var value in values)
			{
				if (value != null && Extensions.TryParseMarketTime(value.Timestamp,
					_marketTimeZone, out var time) && time >= from && time <= to)
				{
					result.Add(new(value, time));
				}
			}
			await IterationInterval.Delay(cancellationToken);
		}
		return result;
	}

	private async Task<List<ParsedTrade>> LoadTrades(ThetaSecurityKey key, DateTime from,
		DateTime to, CancellationToken cancellationToken)
	{
		var result = new List<ParsedTrade>();
		foreach (var session in GetSessions(from, to))
		{
			var values = await SafeRest().GetHistoryTrades(key, session.Date,
				session.Start, session.End, StockVenue, cancellationToken);
			foreach (var value in values)
			{
				if (value != null && Extensions.TryParseMarketTime(value.Timestamp,
					_marketTimeZone, out var time) && time >= from && time <= to)
				{
					result.Add(new(value, time));
				}
			}
			await IterationInterval.Delay(cancellationToken);
		}
		return result;
	}

	private async Task<List<ParsedPrice>> LoadPrices(ThetaSecurityKey key, DateTime from,
		DateTime to, CancellationToken cancellationToken)
	{
		var result = new List<ParsedPrice>();
		foreach (var session in GetSessions(from, to))
		{
			var values = await SafeRest().GetHistoryPrices(key, session.Date,
				session.Start, session.End, cancellationToken);
			foreach (var value in values)
			{
				if (value != null && Extensions.TryParseMarketTime(value.Timestamp,
					_marketTimeZone, out var time) && time >= from && time <= to)
				{
					result.Add(new(value, time));
				}
			}
			await IterationInterval.Delay(cancellationToken);
		}
		return result;
	}

	private async Task<bool> SendLevel1Snapshot(long transactionId, SecurityId securityId,
		ThetaSecurityKey key, CancellationToken cancellationToken)
	{
		if (key.Market == ThetaDataMarkets.Indices)
		{
			var item = (await SafeRest().GetSnapshotPrices(key, cancellationToken))
				.Where(value => value != null && Extensions.TryParseMarketTime(value.Timestamp,
					_marketTimeZone, out _))
				.Select(value => new ParsedPrice(value,
					ParseMarketTime(value.Timestamp)))
				.OrderBy(value => value.Time).LastOrDefault();
			var message = CreatePriceLevel1(transactionId, securityId, item.Time,
				item.Value?.Price);
			if (message == null)
				return false;
			await SendOutMessageAsync(message, cancellationToken);
			return true;
		}

		var quote = (await SafeRest().GetSnapshotQuotes(key, StockVenue, cancellationToken))
			.Where(value => value != null && Extensions.TryParseMarketTime(value.Timestamp,
				_marketTimeZone, out _))
			.Select(value => new ParsedQuote(value, ParseMarketTime(value.Timestamp)))
			.OrderBy(value => value.Time).LastOrDefault();
		var result = CreateQuoteLevel1(transactionId, securityId, quote.Time,
			quote.Value?.Bid, quote.Value?.BidSize, quote.Value?.Ask, quote.Value?.AskSize);
		if (result == null)
			return false;
		await SendOutMessageAsync(result, cancellationToken);
		return true;
	}

	private async Task<bool> SendDepthSnapshot(long transactionId, SecurityId securityId,
		ThetaSecurityKey key, CancellationToken cancellationToken)
	{
		var quote = (await SafeRest().GetSnapshotQuotes(key, StockVenue, cancellationToken))
			.Where(value => value != null && Extensions.TryParseMarketTime(value.Timestamp,
				_marketTimeZone, out _))
			.Select(value => new ParsedQuote(value, ParseMarketTime(value.Timestamp)))
			.OrderBy(value => value.Time).LastOrDefault();
		var message = CreateDepth(transactionId, securityId, quote.Time,
			quote.Value?.Bid, quote.Value?.BidSize, quote.Value?.Ask, quote.Value?.AskSize);
		if (message == null)
			return false;
		await SendOutMessageAsync(message, cancellationToken);
		return true;
	}

	private async Task<bool> FinishEmptySubscription(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		if (mdMsg.Count is > 0 or null)
			return false;
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
		return true;
	}

	private (DateTime From, DateTime To) GetHistoryRange(MarketDataMessage mdMsg)
	{
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUtc();
		var from = mdMsg.From?.ToUtc() ??
			(to.ToMarketDate(_marketTimeZone) + SessionStart).FromMarketTime(_marketTimeZone);
		if (from > to)
			from = to;
		EnsureRange(from, to);
		return (from, to);
	}

	private IEnumerable<(DateTime Date, TimeSpan Start, TimeSpan End)> GetSessions(
		DateTime from, DateTime to)
	{
		EnsureRange(from, to);
		var fromLocal = TimeZoneInfo.ConvertTimeFromUtc(from.ToUtc(), _marketTimeZone);
		var toLocal = TimeZoneInfo.ConvertTimeFromUtc(to.ToUtc(), _marketTimeZone);
		for (var date = fromLocal.Date; date <= toLocal.Date; date = date.AddDays(1))
		{
			var start = date == fromLocal.Date && fromLocal.TimeOfDay > SessionStart
				? fromLocal.TimeOfDay : SessionStart;
			var end = date == toLocal.Date && toLocal.TimeOfDay < SessionEnd
				? toLocal.TimeOfDay : SessionEnd;
			if (start <= end)
				yield return (date, start, end);
		}
	}

	private static T[] SelectHistory<T>(IEnumerable<T> source, Func<T, DateTime> getTime,
		MarketDataMessage mdMsg, bool isDistinct = false)
	{
		var ordered = source.OrderBy(getTime);
		if (isDistinct)
			ordered = ordered.GroupBy(item => getTime(item).Ticks).Select(group => group.First())
				.OrderBy(getTime);
		var values = ordered.ToArray();
		if (mdMsg.Count is not long requested || requested >= values.LongLength)
			return values;
		var take = checked((int)Math.Max(0, requested));
		return mdMsg.From == null ? values.Skip(values.Length - take).ToArray() :
			values.Take(take).ToArray();
	}

	private static Level1ChangeMessage CreateQuoteLevel1(long transactionId,
		SecurityId securityId, DateTime time, decimal? bid, decimal? bidSize,
		decimal? ask, decimal? askSize)
	{
		if (time == default || bid is not > 0 && ask is not > 0)
			return null;
		var message = new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = time,
		};
		if (bid is > 0)
			message.TryAdd(Level1Fields.BestBidPrice, bid)
				.TryAdd(Level1Fields.BestBidVolume, NonNegative(bidSize))
				.TryAdd(Level1Fields.BestBidTime, time);
		if (ask is > 0)
			message.TryAdd(Level1Fields.BestAskPrice, ask)
				.TryAdd(Level1Fields.BestAskVolume, NonNegative(askSize))
				.TryAdd(Level1Fields.BestAskTime, time);
		return message;
	}

	private static Level1ChangeMessage CreatePriceLevel1(long transactionId,
		SecurityId securityId, DateTime time, decimal? price)
		=> time == default || price is not > 0 ? null : new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = time,
		}.TryAdd(Level1Fields.LastTradePrice, price)
			.TryAdd(Level1Fields.LastTradeTime, time);

	private static QuoteChangeMessage CreateDepth(long transactionId, SecurityId securityId,
		DateTime time, decimal? bid, decimal? bidSize, decimal? ask, decimal? askSize)
	{
		if (time == default || bid is not > 0 && ask is not > 0)
			return null;
		return new()
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = time,
			Bids = bid is > 0 ? [new QuoteChange(bid.Value, NonNegative(bidSize) ?? 0)] : [],
			Asks = ask is > 0 ? [new QuoteChange(ask.Value, NonNegative(askSize) ?? 0)] : [],
			State = QuoteChangeStates.SnapshotComplete,
		};
	}

	private static ExecutionMessage CreateTick(long transactionId, SecurityId securityId,
		DateTime time, decimal? price, decimal? volume, long? sequence, int? condition)
		=> time == default || price is not > 0 ? null : new()
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			DataTypeEx = DataType.Ticks,
			ServerTime = time,
			TradePrice = price,
			TradeVolume = NonNegative(volume),
			SeqNum = sequence.GetValueOrDefault(),
			TradeStatus = condition.GetValueOrDefault(),
		};

	private bool TryGetEodOpenTime(ThetaEod value, out DateTime openTime)
	{
		openTime = default;
		if (!Extensions.TryParseMarketTime(value.LastTrade, _marketTimeZone, out var reportTime) &&
			!Extensions.TryParseMarketTime(value.Created, _marketTimeZone, out reportTime))
		{
			return false;
		}
		openTime = (reportTime.ToMarketDate(_marketTimeZone) + SessionStart)
			.FromMarketTime(_marketTimeZone);
		return true;
	}

	private DateTime GetDailyCloseTime(DateTime openTime)
		=> (openTime.ToMarketDate(_marketTimeZone) + SessionEnd)
			.FromMarketTime(_marketTimeZone);

	private DateTime ParseMarketTime(string value)
	{
		if (!Extensions.TryParseMarketTime(value, _marketTimeZone, out var result))
			throw new InvalidDataException($"Invalid ThetaData market timestamp '{value}'.");
		return result;
	}

	private static bool TryGetOhlc(ThetaBar value, out decimal open, out decimal high,
		out decimal low, out decimal close)
	{
		open = value.Open.GetValueOrDefault();
		high = value.High.GetValueOrDefault();
		low = value.Low.GetValueOrDefault();
		close = value.Close.GetValueOrDefault();
		return value.Open != null && value.High != null && value.Low != null &&
			value.Close != null;
	}

	private static bool TryGetOhlc(ThetaEod value, out decimal open, out decimal high,
		out decimal low, out decimal close)
	{
		open = value.Open.GetValueOrDefault();
		high = value.High.GetValueOrDefault();
		low = value.Low.GetValueOrDefault();
		close = value.Close.GetValueOrDefault();
		return value.Open != null && value.High != null && value.Low != null &&
			value.Close != null;
	}

	private static long? Subtract(long? count, long emitted)
		=> count == null ? null : Math.Max(0, count.Value - emitted);

	private static decimal? NonNegative(decimal? value)
		=> value is >= 0 ? value : null;

	private static int? ToCount(long? value)
		=> value == null ? null : checked((int)Math.Clamp(value.Value, 0, int.MaxValue));

	private static void EnsureRange(DateTime from, DateTime to)
	{
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(from), from,
				"The history start time is after its end time.");
	}
}
