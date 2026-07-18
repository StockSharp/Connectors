namespace StockSharp.Orats;

public partial class OratsMessageAdapter
{
	private readonly record struct ParsedStrike(OratsStrike Value, DateTime Time);
	private readonly record struct ParsedDaily(OratsDaily Value, DateTime OpenTime,
		DateTime CloseTime);

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

		var securityTypes = lookupMsg.GetSecurityTypes();
		var board = lookupMsg.SecurityId.BoardCode;
		var native = lookupMsg.SecurityId.Native as string;
		var hasExactKey = OratsSecurityKey.TryParse(native, out var exactKey) ||
			OratsSecurityKey.TryParseOcc(native.IsEmpty(lookupMsg.SecurityId.SecurityCode),
				out exactKey);
		var value = hasExactKey ? exactKey.Root :
			native.IsEmpty(lookupMsg.SecurityId.SecurityCode).IsEmpty(lookupMsg.Name);
		value = value?.Trim().ToUpperInvariant();
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		bool MatchesLookup(SecurityMessage security)
		{
			var criteria = lookupMsg;
			var expectedId = lookupMsg.SecurityId;
			if (expectedId.Native == null && !expectedId.SecurityCode.IsEmpty() &&
				!expectedId.BoardCode.IsEmpty() &&
				security.SecurityId.SecurityCode.EqualsIgnoreCase(expectedId.SecurityCode) &&
				security.SecurityId.BoardCode.EqualsIgnoreCase(expectedId.BoardCode))
			{
				criteria = (SecurityLookupMessage)lookupMsg.Clone();
				criteria.SecurityId = security.SecurityId;
			}
			return security.IsMatch(criteria, securityTypes);
		}

		async Task<bool> Emit(SecurityMessage security)
		{
			var key = security?.SecurityId.Native as string;
			if (security == null || left <= 0 || key.IsEmpty() || !emitted.Add(key) ||
				!MatchesLookup(security))
			{
				return false;
			}
			if (skip > 0)
			{
				skip--;
				return false;
			}
			await SendOutMessageAsync(security, cancellationToken);
			left--;
			return true;
		}

		var noBoard = board.IsEmpty();
		var noTypes = securityTypes.Count == 0;
		bool Requested(OratsMarkets market)
		{
			if (!noBoard)
				return board.EqualsIgnoreCase(market.ToBoard());
			if (noTypes)
				return market == OratsMarkets.Stocks;
			return market == OratsMarkets.Options
				? securityTypes.Contains(SecurityTypes.Option)
				: securityTypes.Contains(SecurityTypes.Stock);
		}

		var optionsRequested = Requested(OratsMarkets.Options) ||
			lookupMsg.OptionType != null || lookupMsg.Strike != null ||
			lookupMsg.ExpiryDate != null || lookupMsg.UnderlyingSecurityId != default ||
			(hasExactKey && exactKey.Market == OratsMarkets.Options);
		if (optionsRequested && left > 0)
		{
			var exactOption = hasExactKey && exactKey.Market == OratsMarkets.Options;
			var underlying = exactOption ? exactKey.Root : lookupMsg.GetUnderlyingCode();
			if (!exactOption && underlying.IsEmpty())
				underlying = value;
			if (!underlying.IsEmpty())
			{
				var found = false;
				var marketDate = DateTime.UtcNow.ToMarketDate(_marketTimeZone);
				var canUseCurrent = exactOption
					? exactKey.Expiration.Date >= marketDate
					: lookupMsg.ExpiryDate == null ||
						lookupMsg.ExpiryDate.Value.Date >= marketDate;
				if (exactOption && canUseCurrent)
				{
					var response = await SafeRest().GetCurrentOptions(
						exactKey.ToApiSymbol(), DataMode, cancellationToken);
					foreach (var item in response?.Data ?? [])
					{
						if (!item.TryGetKey(out var key) || key != exactKey)
							continue;
						var security = key.ToSecurityMessage(lookupMsg.TransactionId);
						found |= MatchesLookup(security);
						await Emit(security);
					}
				}
				else if (!exactOption && canUseCurrent)
				{
					var response = await SafeRest().GetCurrentChain(underlying,
						DataMode, cancellationToken);
					foreach (var row in response?.Data ?? [])
					{
						foreach (var optionType in new[] { OptionTypes.Call, OptionTypes.Put })
						{
							var key = row.GetOptionKey(optionType);
							if (key == default)
								continue;
							var security = key.ToSecurityMessage(
								lookupMsg.TransactionId);
							found |= MatchesLookup(security);
							await Emit(security);
							if (left <= 0)
								break;
						}
						if (left <= 0)
							break;
					}
				}

				if (!found && left > 0 && (exactOption ||
					lookupMsg.ExpiryDate != null && lookupMsg.Strike != null))
				{
					var expiration = exactOption ? exactKey.Expiration :
						lookupMsg.ExpiryDate.Value;
					var strike = exactOption ? exactKey.Strike : lookupMsg.Strike.Value;
					var response = await SafeRest().GetHistoricalOption(underlying,
						expiration, strike, null, cancellationToken);
					foreach (var optionType in new[] { OptionTypes.Call, OptionTypes.Put })
					{
						if (exactOption && exactKey.OptionType != optionType ||
							lookupMsg.OptionType != null && lookupMsg.OptionType != optionType)
						{
							continue;
						}
						var key = (response?.Data ?? [])
							.Select(row => row.GetOptionKey(optionType))
							.FirstOrDefault(candidate => candidate != default &&
								candidate.Expiration.Date == expiration.Date &&
								candidate.Strike == strike);
						if (key != default && exactOption)
							key = exactKey;
						if (key != default)
							await Emit(key.ToSecurityMessage(lookupMsg.TransactionId));
					}
				}
			}
		}

		if (left > 0 && Requested(OratsMarkets.Stocks) &&
			(!hasExactKey || exactKey.Market == OratsMarkets.Stocks))
		{
			var response = await SafeRest().GetTickers(value, cancellationToken);
			foreach (var ticker in response?.Data ?? [])
			{
				if (ticker.Matches(value))
					await Emit(ticker.ToSecurityMessage(lookupMsg.TransactionId));
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
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var key = mdMsg.SecurityId.GetOratsKey();
		var securityId = mdMsg.SecurityId.NormalizeOrats(key);
		var from = mdMsg.From?.ToUtc();
		var to = mdMsg.To?.ToUtc();
		ValidateRange(from, to);

		if (from == null && to == null)
		{
			var response = await SafeRest().GetCurrentOptions(
				key.Market == OratsMarkets.Options ? key.ToApiSymbol() : key.Root,
				DataMode, cancellationToken);
			foreach (var value in response?.Data ?? [])
			{
				if (!Matches(value, key))
					continue;
				var message = key.Market == OratsMarkets.Options
					? CreateOptionLevel1(mdMsg.TransactionId, securityId, key, value)
					: CreateStockLevel1(mdMsg.TransactionId, securityId, value);
				if (message?.Changes.Count > 0)
					await SendOutMessageAsync(message, cancellationToken);
				break;
			}
		}
		else if (key.Market == OratsMarkets.Options)
		{
			var values = await LoadOptionHistory(key, from, to, mdMsg.Count,
				cancellationToken);
			foreach (var item in values)
			{
				var message = CreateOptionLevel1(mdMsg.TransactionId, securityId,
					key, item.Value, item.Time);
				if (message.Changes.Count > 0)
					await SendOutMessageAsync(message, cancellationToken);
			}
		}
		else
		{
			var values = await LoadDailies(key.Root, from, to, mdMsg.Count,
				cancellationToken);
			foreach (var item in values)
			{
				var message = CreateDailyLevel1(mdMsg.TransactionId, securityId, item);
				if (message?.Changes.Count > 0)
					await SendOutMessageAsync(message, cancellationToken);
			}
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg,
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

		var key = mdMsg.SecurityId.GetOratsKey();
		var securityId = mdMsg.SecurityId.NormalizeOrats(key);
		var from = mdMsg.From?.ToUtc();
		var to = mdMsg.To?.ToUtc();
		ValidateRange(from, to);

		if (from == null && to == null)
		{
			var response = await SafeRest().GetCurrentOptions(
				key.Market == OratsMarkets.Options ? key.ToApiSymbol() : key.Root,
				DataMode, cancellationToken);
			foreach (var value in response?.Data ?? [])
			{
				if (!Matches(value, key))
					continue;
				var time = GetObservationTime(value.QuoteDate, value.UpdatedAt,
					value.TradeDate);
				var depth = key.Market == OratsMarkets.Options
					? CreateDepth(mdMsg.TransactionId, securityId, time,
						value.BidPrice, value.BidSize, value.AskPrice, value.AskSize)
					: CreateDepth(mdMsg.TransactionId, securityId, time,
						value.StockBid, value.BidSize, value.StockAsk, value.AskSize);
				if (depth != null)
					await SendOutMessageAsync(depth, cancellationToken);
				break;
			}
		}
		else
		{
			if (key.Market != OratsMarkets.Options)
			{
				throw new NotSupportedException(
					"ORATS historical stock dailies do not contain bid/ask quotes.");
			}
			foreach (var item in await LoadOptionHistory(key, from, to, mdMsg.Count,
				cancellationToken))
			{
				var isCall = key.OptionType == OptionTypes.Call;
				var depth = CreateDepth(mdMsg.TransactionId, securityId, item.Time,
					isCall ? item.Value.CallBidPrice : item.Value.PutBidPrice,
					isCall ? item.Value.CallBidSize : item.Value.PutBidSize,
					isCall ? item.Value.CallAskPrice : item.Value.PutAskPrice,
					isCall ? item.Value.CallAskSize : item.Value.PutAskSize);
				if (depth != null)
					await SendOutMessageAsync(depth, cancellationToken);
			}
		}

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
		if (timeFrame != TimeSpan.FromDays(1))
			throw new NotSupportedException("ORATS JSON daily prices provide one-day candles only.");
		var key = mdMsg.SecurityId.GetOratsKey();
		if (key.Market != OratsMarkets.Stocks)
		{
			throw new NotSupportedException(
				"ORATS end-of-day option strikes are observations, not OHLC candles.");
		}
		var securityId = mdMsg.SecurityId.NormalizeOrats(key);
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUtc();
		var from = (mdMsg.From ?? Extensions.EstimateFrom(to, mdMsg.Count)).ToUtc();
		ValidateRange(from, to);

		foreach (var item in await LoadDailies(key.Root, from, to, mdMsg.Count,
			cancellationToken))
		{
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
				CloseTime = item.CloseTime,
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

	private async Task<ParsedStrike[]> LoadOptionHistory(OratsSecurityKey key,
		DateTime? from, DateTime? to, long? count, CancellationToken cancellationToken)
	{
		var singleDate = GetSingleMarketDate(from, to);
		var response = await SafeRest().GetHistoricalOption(key.Root, key.Expiration,
			key.Strike, singleDate, cancellationToken);
		var values = new List<ParsedStrike>();
		foreach (var row in response?.Data ?? [])
		{
			var rowKey = row.GetOptionKey(key.OptionType.Value);
			if (rowKey == default || rowKey.Expiration.Date != key.Expiration.Date ||
				rowKey.Strike != key.Strike)
				continue;
			var time = GetObservationTime(row.QuoteDate, row.UpdatedAt, row.TradeDate);
			if (time == default || from != null && time < from || to != null && time > to)
				continue;
			values.Add(new(row, time));
		}
		return SelectHistory(values.OrderBy(item => item.Time).ToArray(), count,
			from == null);
	}

	private async Task<ParsedDaily[]> LoadDailies(string ticker, DateTime? from,
		DateTime? to, long? count, CancellationToken cancellationToken)
	{
		var response = await SafeRest().GetDailies(ticker,
			GetSingleMarketDate(from, to), cancellationToken);
		var values = new List<ParsedDaily>();
		foreach (var row in response?.Data ?? [])
		{
			if (row?.Ticker.EqualsIgnoreCase(ticker) != true ||
				!Extensions.TryParseDate(row.TradeDate, out var tradeDate))
			{
				continue;
			}
			var localDate = tradeDate.Date;
			var openTime = (localDate + SessionStart).FromMarketTime(_marketTimeZone);
			var closeTime = (localDate + SessionEnd).FromMarketTime(_marketTimeZone);
			if (from != null && openTime < from || to != null && openTime > to)
				continue;
			values.Add(new(row, openTime, closeTime));
		}
		return SelectHistory(values.OrderBy(item => item.OpenTime).ToArray(), count,
			from == null);
	}

	private static T[] SelectHistory<T>(T[] values, long? count, bool takeLast)
	{
		if (count is not > 0 || values.LongLength <= count.Value)
			return values;
		var take = checked((int)Math.Min(count.Value, int.MaxValue));
		return takeLast ? values[^take..] : values[..take];
	}

	private DateTime? GetSingleMarketDate(DateTime? from, DateTime? to)
	{
		if (from == null || to == null)
			return null;
		var fromDate = from.Value.ToMarketDate(_marketTimeZone);
		var toDate = to.Value.ToMarketDate(_marketTimeZone);
		return fromDate == toDate ? fromDate : null;
	}

	private static bool Matches(OratsSnapshot value, OratsSecurityKey key)
	{
		if (value == null)
			return false;
		if (key.Market == OratsMarkets.Stocks)
			return value.OptionSymbol.IsEmpty() && value.Ticker.EqualsIgnoreCase(key.Root);
		return value.TryGetKey(out var actual) && actual == key;
	}

	private Level1ChangeMessage CreateStockLevel1(long transactionId,
		SecurityId securityId, OratsSnapshot value)
	{
		var time = GetObservationTime(value.QuoteDate, value.UpdatedAt, value.TradeDate);
		if (time == default)
			return null;
		return new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = time,
		}
		.TryAdd(Level1Fields.LastTradePrice, Positive(value.StockPrice))
		.TryAdd(Level1Fields.LastTradeTime,
			Positive(value.StockPrice) != null ? time : null)
		.TryAdd(Level1Fields.BestBidPrice, Positive(value.StockBid))
		.TryAdd(Level1Fields.BestBidVolume, NonNegative(value.BidSize))
		.TryAdd(Level1Fields.BestBidTime, Positive(value.StockBid) != null ? time : null)
		.TryAdd(Level1Fields.BestAskPrice, Positive(value.StockAsk))
		.TryAdd(Level1Fields.BestAskVolume, NonNegative(value.AskSize))
		.TryAdd(Level1Fields.BestAskTime, Positive(value.StockAsk) != null ? time : null)
		.TryAdd(Level1Fields.Volume, NonNegative(value.Volume));
	}

	private Level1ChangeMessage CreateOptionLevel1(long transactionId,
		SecurityId securityId, OratsSecurityKey key, OratsSnapshot value)
	{
		var time = GetObservationTime(value.QuoteDate, value.UpdatedAt, value.TradeDate);
		if (time == default)
			return null;
		var isCall = key.OptionType == OptionTypes.Call;
		return new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = time,
		}
		.TryAdd(Level1Fields.BestBidPrice, Positive(value.BidPrice))
		.TryAdd(Level1Fields.BestBidVolume, NonNegative(value.BidSize))
		.TryAdd(Level1Fields.BestBidTime, Positive(value.BidPrice) != null ? time : null)
		.TryAdd(Level1Fields.BestAskPrice, Positive(value.AskPrice))
		.TryAdd(Level1Fields.BestAskVolume, NonNegative(value.AskSize))
		.TryAdd(Level1Fields.BestAskTime, Positive(value.AskPrice) != null ? time : null)
		.TryAdd(Level1Fields.Volume, NonNegative(value.Volume))
		.TryAdd(Level1Fields.OpenInterest, NonNegative(value.OpenInterest))
		.TryAdd(Level1Fields.ImpliedVolatility, NonNegative(value.MidIv))
		.TryAdd(Level1Fields.TheorPrice, Positive(value.OptionValue))
		.TryAdd(Level1Fields.UnderlyingPrice,
			Positive(value.SpotPrice ?? value.StockPrice))
		.TryAdd(Level1Fields.Delta, AdjustDelta(value.Delta, key.OptionType.Value))
		.TryAdd(Level1Fields.Gamma, value.Gamma)
		.TryAdd(Level1Fields.Theta, isCall ? value.Theta : null)
		.TryAdd(Level1Fields.Vega, value.Vega)
		.TryAdd(Level1Fields.Rho, isCall ? value.Rho : null);
	}

	private Level1ChangeMessage CreateOptionLevel1(long transactionId,
		SecurityId securityId, OratsSecurityKey key, OratsStrike value, DateTime time)
	{
		var isCall = key.OptionType == OptionTypes.Call;
		return new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = time,
		}
		.TryAdd(Level1Fields.BestBidPrice,
			Positive(isCall ? value.CallBidPrice : value.PutBidPrice))
		.TryAdd(Level1Fields.BestBidVolume,
			NonNegative(isCall ? value.CallBidSize : value.PutBidSize))
		.TryAdd(Level1Fields.BestBidTime,
			Positive(isCall ? value.CallBidPrice : value.PutBidPrice) != null ? time : null)
		.TryAdd(Level1Fields.BestAskPrice,
			Positive(isCall ? value.CallAskPrice : value.PutAskPrice))
		.TryAdd(Level1Fields.BestAskVolume,
			NonNegative(isCall ? value.CallAskSize : value.PutAskSize))
		.TryAdd(Level1Fields.BestAskTime,
			Positive(isCall ? value.CallAskPrice : value.PutAskPrice) != null ? time : null)
		.TryAdd(Level1Fields.Volume,
			NonNegative(isCall ? value.CallVolume : value.PutVolume))
		.TryAdd(Level1Fields.OpenInterest,
			NonNegative(isCall ? value.CallOpenInterest : value.PutOpenInterest))
		.TryAdd(Level1Fields.ImpliedVolatility,
			NonNegative(isCall ? value.CallMidIv : value.PutMidIv))
		.TryAdd(Level1Fields.TheorPrice,
			Positive(isCall ? value.CallValue : value.PutValue))
		.TryAdd(Level1Fields.UnderlyingPrice,
			Positive(value.SpotPrice ?? value.StockPrice))
		.TryAdd(Level1Fields.Delta, AdjustDelta(value.Delta, key.OptionType.Value))
		.TryAdd(Level1Fields.Gamma, value.Gamma)
		.TryAdd(Level1Fields.Theta, isCall ? value.Theta : null)
		.TryAdd(Level1Fields.Vega, value.Vega)
		.TryAdd(Level1Fields.Rho, isCall ? value.Rho : null);
	}

	private Level1ChangeMessage CreateDailyLevel1(long transactionId,
		SecurityId securityId, ParsedDaily item)
	{
		if (!item.Value.TryGetOhlc(PriceAdjustment, out var open, out var high,
			out var low, out var close, out var volume))
		{
			return null;
		}
		return new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = item.CloseTime,
		}
		.TryAdd(Level1Fields.OpenPrice, Positive(open))
		.TryAdd(Level1Fields.HighPrice, Positive(high))
		.TryAdd(Level1Fields.LowPrice, Positive(low))
		.TryAdd(Level1Fields.LastTradePrice, Positive(close))
		.TryAdd(Level1Fields.LastTradeTime, Positive(close) != null ? item.CloseTime : null)
		.TryAdd(Level1Fields.Volume, NonNegative(volume));
	}

	private static QuoteChangeMessage CreateDepth(long transactionId,
		SecurityId securityId, DateTime time, decimal? bid, decimal? bidSize,
		decimal? ask, decimal? askSize)
	{
		if (time == default || bid is not > 0 && ask is not > 0)
			return null;
		return new()
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = time,
			Bids = bid is > 0 ? [new QuoteChange(bid.Value,
				NonNegative(bidSize) ?? 0)] : [],
			Asks = ask is > 0 ? [new QuoteChange(ask.Value,
				NonNegative(askSize) ?? 0)] : [],
			State = QuoteChangeStates.SnapshotComplete,
		};
	}

	private DateTime GetObservationTime(string quoteDate, string updatedAt,
		string tradeDate)
	{
		if (Extensions.TryParseUtc(quoteDate, out var time) ||
			Extensions.TryParseUtc(updatedAt, out time))
		{
			return time;
		}
		if (!Extensions.TryParseDate(tradeDate, out var date))
			return default;
		return (date.Date + SessionEnd).FromMarketTime(_marketTimeZone);
	}

	private static decimal? AdjustDelta(decimal? delta, OptionTypes optionType)
		=> delta == null ? null : optionType == OptionTypes.Put ? delta - 1m : delta;

	private static decimal? Positive(decimal? value)
		=> value is > 0 ? value : null;

	private static decimal? Positive(decimal value)
		=> value > 0 ? value : null;

	private static decimal? NonNegative(decimal? value)
		=> value is >= 0 ? value : null;

	private static decimal? NonNegative(decimal value)
		=> value >= 0 ? value : null;

	private static void ValidateRange(DateTime? from, DateTime? to)
	{
		if (from != null && to != null && from > to)
		{
			throw new ArgumentOutOfRangeException(nameof(from), from,
				"The ORATS history start time is after its end time.");
		}
	}
}
