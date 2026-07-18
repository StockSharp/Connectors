namespace StockSharp.CboeDataShop;

public partial class CboeDataShopMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		var securityTypes = lookupMsg.GetSecurityTypes();
		var code = lookupMsg.SecurityId.SecurityCode;
		var skip = lookupMsg.Skip ?? 0;
		var left = lookupMsg.Count ?? long.MaxValue;
		var allowsStocks = securityTypes.Count == 0 || securityTypes.Contains(SecurityTypes.Stock);
		if (allowsStocks && left > 0)
		{
			foreach (var symbol in await SafeClient().GetSymbols(cancellationToken))
			{
				if (symbol?.Name.IsEmpty() != false)
					continue;
				if (!code.IsEmpty() && !symbol.Name.EqualsIgnoreCase(code) &&
					!symbol.CompanyName.ContainsIgnoreCase(code))
				{
					continue;
				}

				var security = symbol.ToSecurityMessage(lookupMsg.TransactionId);
				if (!security.IsMatch(lookupMsg, securityTypes))
					continue;
				if (skip > 0)
				{
					skip--;
					continue;
				}
				await SendOutMessageAsync(security, cancellationToken);
				if (--left <= 0)
					break;
			}
		}

		var nativeCode = lookupMsg.SecurityId.Native as string;
		var exactOption = CboeOptionKey.TryParse(nativeCode.IsEmpty(code), out var optionKey);
		var underlying = lookupMsg.GetUnderlyingCode();
		var optionsRequested = securityTypes.Contains(SecurityTypes.Option) ||
			!underlying.IsEmpty() || exactOption;
		if (optionsRequested && left > 0)
		{
			if (exactOption)
				underlying = optionKey.Root;
			else if (underlying.IsEmpty() && securityTypes.Contains(SecurityTypes.Option))
				underlying = code;

			if (!underlying.IsEmpty())
			{
				var date = await GetLatestTradingDate(exactOption
					? optionKey.Expiry.Min(Extensions.GetEasternDate(DateTime.UtcNow))
					: Extensions.GetEasternDate(DateTime.UtcNow), cancellationToken);
				var response = await SafeClient().GetOptionQuotes(new CboeOptionQuoteQuery
				{
					Symbol = underlying,
					Root = exactOption ? optionKey.Root : null,
					OptionType = exactOption
						? optionKey.OptionType.ToNative()
						: lookupMsg.OptionType?.ToNative(),
					Date = date,
					MinimumExpiry = exactOption ? optionKey.Expiry : lookupMsg.ExpiryDate,
					MaximumExpiry = exactOption ? optionKey.Expiry : lookupMsg.ExpiryDate,
					MinimumStrike = exactOption ? optionKey.Strike : lookupMsg.Strike,
					MaximumStrike = exactOption ? optionKey.Strike : lookupMsg.Strike,
				}, cancellationToken);

				foreach (var option in response?.Options ?? [])
				{
					if (option?.Option.IsEmpty() != false ||
						(exactOption && !option.Option.EqualsIgnoreCase(optionKey.Code)))
					{
						continue;
					}
					var security = option.ToSecurityMessage(lookupMsg.TransactionId);
					if (!security.IsMatch(lookupMsg, securityTypes))
						continue;
					if (skip > 0)
					{
						skip--;
						continue;
					}
					await SendOutMessageAsync(security, cancellationToken);
					if (--left <= 0)
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
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var nativeCode = (mdMsg.SecurityId.Native as string)
			.IsEmpty(mdMsg.SecurityId.SecurityCode)
			.ThrowIfEmpty(nameof(mdMsg.SecurityId.SecurityCode));
		var isOption = IsOption(mdMsg.SecurityId, nativeCode, out var optionKey);
		var securityId = mdMsg.SecurityId.Normalize(isOption, nativeCode);
		var dates = await GetRequestDates(mdMsg, cancellationToken);
		var sent = 0;

		foreach (var date in dates)
		{
			if (isOption)
			{
				var option = await GetOptionQuote(optionKey, date, cancellationToken);
				if (option == null)
					continue;
				await SendOutMessageAsync(CreateLevel1(mdMsg.TransactionId,
					securityId, date, option), cancellationToken);
			}
			else
			{
				var quote = (await SafeClient().GetUnderlyingQuotes(new CboeUnderlyingQuoteQuery
				{
					Symbols = nativeCode,
					Date = date,
				}, cancellationToken)).FirstOrDefault(item =>
					item?.Symbol.EqualsIgnoreCase(nativeCode) == true);
				if (quote == null)
					continue;
				await SendOutMessageAsync(CreateLevel1(mdMsg.TransactionId,
					securityId, date, quote), cancellationToken);
			}
			sent++;
		}

		if (sent == 0)
			this.AddWarningLog("Cboe returned no entitled quote data for {0}.", nativeCode);
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
			throw new NotSupportedException(
				"Cboe All Access OHLCV candles are available only at the native daily frequency.");

		var nativeCode = (mdMsg.SecurityId.Native as string)
			.IsEmpty(mdMsg.SecurityId.SecurityCode)
			.ThrowIfEmpty(nameof(mdMsg.SecurityId.SecurityCode));
		var isOption = IsOption(mdMsg.SecurityId, nativeCode, out var optionKey);
		var securityId = mdMsg.SecurityId.Normalize(isOption, nativeCode);
		foreach (var date in await GetRequestDates(mdMsg, cancellationToken))
		{
			if (isOption)
			{
				var option = await GetOptionQuote(optionKey, date, cancellationToken);
				if (option?.HasOhlc != true)
					continue;
				await SendOutMessageAsync(new TimeFrameCandleMessage
				{
					OriginalTransactionId = mdMsg.TransactionId,
					SecurityId = securityId,
					DataType = mdMsg.DataType2,
					TypedArg = timeFrame,
					OpenTime = date.ToUtcDate(),
					OpenPrice = option.OptionOpen.Value,
					HighPrice = option.OptionHigh.Value,
					LowPrice = option.OptionLow.Value,
					ClosePrice = option.OptionClose.Value,
					TotalVolume = option.OptionVolume ?? 0,
					OpenInterest = option.OpenInterest,
					State = CandleStates.Finished,
				}, cancellationToken);
			}
			else
			{
				var quote = (await SafeClient().GetUnderlyingQuotes(new CboeUnderlyingQuoteQuery
				{
					Symbols = nativeCode,
					Date = date,
				}, cancellationToken)).FirstOrDefault(item =>
					item?.Symbol.EqualsIgnoreCase(nativeCode) == true);
				if (quote?.UnderlyingOpen == null || quote.UnderlyingHigh == null ||
					quote.UnderlyingLow == null || quote.UnderlyingClose == null)
				{
					continue;
				}
				await SendOutMessageAsync(new TimeFrameCandleMessage
				{
					OriginalTransactionId = mdMsg.TransactionId,
					SecurityId = securityId,
					DataType = mdMsg.DataType2,
					TypedArg = timeFrame,
					OpenTime = date.ToUtcDate(),
					OpenPrice = quote.UnderlyingOpen.Value,
					HighPrice = quote.UnderlyingHigh.Value,
					LowPrice = quote.UnderlyingLow.Value,
					ClosePrice = quote.UnderlyingClose.Value,
					TotalVolume = quote.UnderlyingVolume ?? 0,
					State = CandleStates.Finished,
				}, cancellationToken);
			}
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg,
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

		var nativeCode = (mdMsg.SecurityId.Native as string)
			.IsEmpty(mdMsg.SecurityId.SecurityCode)
			.ThrowIfEmpty(nameof(mdMsg.SecurityId.SecurityCode));
		var isOption = IsOption(mdMsg.SecurityId, nativeCode, out var optionKey);
		var securityId = mdMsg.SecurityId.Normalize(isOption, nativeCode);
		var trades = new List<(DateTime date, CboeTrade trade)>();
		var dates = await GetTickDates(mdMsg, cancellationToken);
		IEnumerable<DateTime> orderedDates = mdMsg.From == null && mdMsg.Count != null
			? dates.Reverse()
			: dates;
		foreach (var date in orderedDates)
		{
			long sequenceNumber = 0;
			while (true)
			{
				var query = new CboeTradeQuery
				{
					Symbol = isOption ? optionKey.Code : nativeCode,
					Root = isOption ? optionKey.Root : null,
					Expiry = isOption ? optionKey.Expiry : null,
					Strike = isOption ? optionKey.Strike : null,
					OptionType = isOption ? optionKey.OptionType.ToNative() : null,
					MinimumTime = GetBoundaryTime(mdMsg.From, date),
					MaximumTime = GetBoundaryTime(mdMsg.To, date),
					SequenceNumber = sequenceNumber,
					Limit = 10000,
					Date = date,
				};
				CboeTrade[] page = isOption
					? await SafeClient().GetOptionTrades(query, cancellationToken)
					: await SafeClient().GetUnderlyingTrades(query, cancellationToken);
				if (page.Length == 0)
					break;

				var validPage = page.WhereNotNull().ToArray();
				if (validPage.Length == 0)
					break;
				foreach (var trade in validPage)
				{
					if (trade.CancelState == CboeCancelStates.None &&
						trade.Price != null && trade.Size != null)
						trades.Add((date, trade));
				}
				if (page.Length < query.Limit)
					break;
				var next = validPage.Max(item => item.SequenceNumber);
				if (next < sequenceNumber || next == long.MaxValue)
					break;
				sequenceNumber = next + 1;
			}
			if (mdMsg.From == null && mdMsg.Count is long requested &&
				trades.Count >= requested)
			{
				break;
			}
		}

		IEnumerable<(DateTime date, CboeTrade trade)> selected = trades
			.OrderBy(item => item.date.ToServerTime(item.trade.Timestamp))
			.ThenBy(item => item.trade.SequenceNumber);
		if (mdMsg.Count is long count)
		{
			var take = checked((int)Math.Min(count.Max(0), int.MaxValue));
			selected = mdMsg.From == null ? selected.TakeLast(take) : selected.Take(take);
		}

		foreach (var item in selected)
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = securityId,
				ServerTime = item.date.ToServerTime(item.trade.Timestamp),
				TradeId = item.trade.SequenceNumber,
				TradePrice = item.trade.Price.Value,
				TradeVolume = item.trade.Size.Value,
				OriginSide = item.trade.TradeLocation.ToOriginSide(),
			}, cancellationToken);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private static Level1ChangeMessage CreateLevel1(long transactionId, SecurityId securityId,
		DateTime date, CboeUnderlyingSnapshot quote)
	{
		var time = date.ToServerTime(quote.Timestamp);
		return new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = time,
		}
		.TryAdd(Level1Fields.BestBidPrice, quote.UnderlyingBid)
		.TryAdd(Level1Fields.BestBidVolume, quote.UnderlyingBidSize)
		.TryAdd(Level1Fields.BestBidTime, quote.UnderlyingBid != null ? time : null)
		.TryAdd(Level1Fields.BestAskPrice, quote.UnderlyingAsk)
		.TryAdd(Level1Fields.BestAskVolume, quote.UnderlyingAskSize)
		.TryAdd(Level1Fields.BestAskTime, quote.UnderlyingAsk != null ? time : null)
		.TryAdd(Level1Fields.SpreadMiddle, quote.UnderlyingMid)
		.TryAdd(Level1Fields.LastTradePrice, quote.UnderlyingLastTradePrice)
		.TryAdd(Level1Fields.LastTradeVolume, quote.UnderlyingLastTradeSize)
		.TryAdd(Level1Fields.LastTradeTime, quote.UnderlyingLastTradePrice != null ? time : null)
		.TryAdd(Level1Fields.OpenPrice, quote.UnderlyingOpen)
		.TryAdd(Level1Fields.HighPrice, quote.UnderlyingHigh)
		.TryAdd(Level1Fields.LowPrice, quote.UnderlyingLow)
		.TryAdd(Level1Fields.ClosePrice, quote.UnderlyingClose)
		.TryAdd(Level1Fields.Volume, quote.UnderlyingVolume)
		.TryAdd(Level1Fields.ImpliedVolatility, quote.Iv30 / 100m);
	}

	private static Level1ChangeMessage CreateLevel1(long transactionId, SecurityId securityId,
		DateTime date, CboeOptionQuote option)
	{
		var time = date.ToServerTime(option.Timestamp);
		return new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = time,
		}
		.TryAdd(Level1Fields.BestBidPrice, option.OptionBid)
		.TryAdd(Level1Fields.BestBidVolume, option.OptionBidSize)
		.TryAdd(Level1Fields.BestBidTime, option.OptionBid != null ? time : null)
		.TryAdd(Level1Fields.BestAskPrice, option.OptionAsk)
		.TryAdd(Level1Fields.BestAskVolume, option.OptionAskSize)
		.TryAdd(Level1Fields.BestAskTime, option.OptionAsk != null ? time : null)
		.TryAdd(Level1Fields.SpreadMiddle, option.OptionMid)
		.TryAdd(Level1Fields.LastTradePrice, option.OptionLastTradePrice)
		.TryAdd(Level1Fields.LastTradeTime, option.OptionLastTradePrice != null ? time : null)
		.TryAdd(Level1Fields.OpenPrice, option.OptionOpen)
		.TryAdd(Level1Fields.HighPrice, option.OptionHigh)
		.TryAdd(Level1Fields.LowPrice, option.OptionLow)
		.TryAdd(Level1Fields.ClosePrice, option.OptionClose)
		.TryAdd(Level1Fields.Volume, option.OptionVolume)
		.TryAdd(Level1Fields.TradesCount, option.OptionTradeCount)
		.TryAdd(Level1Fields.OpenInterest, option.OpenInterest)
		.TryAdd(Level1Fields.TheorPrice, option.CboeTheoreticalPrice)
		.TryAdd(Level1Fields.ImpliedVolatility,
			option.ImpliedVolatility ?? option.MidImpliedVolatility)
		.TryAdd(Level1Fields.Delta, option.Delta)
		.TryAdd(Level1Fields.Gamma, option.Gamma)
		.TryAdd(Level1Fields.Vega, option.Vega)
		.TryAdd(Level1Fields.Theta, option.Theta)
		.TryAdd(Level1Fields.Rho, option.Rho);
	}

	private async Task<CboeOptionQuote> GetOptionQuote(CboeOptionKey key, DateTime date,
		CancellationToken cancellationToken)
	{
		var response = await SafeClient().GetOptionQuotes(new CboeOptionQuoteQuery
		{
			Symbol = key.Root,
			Root = key.Root,
			OptionType = key.OptionType.ToNative(),
			Date = date,
			MinimumExpiry = key.Expiry,
			MaximumExpiry = key.Expiry,
			MinimumStrike = key.Strike,
			MaximumStrike = key.Strike,
		}, cancellationToken);
		return response?.Options?.FirstOrDefault(option =>
			option?.Option.EqualsIgnoreCase(key.Code) == true);
	}

	private static bool IsOption(SecurityId securityId, string nativeCode, out CboeOptionKey key)
	{
		if (CboeOptionKey.TryParse(nativeCode, out key))
			return true;
		if (securityId.BoardCode.EqualsIgnoreCase(_optionBoardCode))
			throw new ArgumentException(
				$"Cboe option code '{nativeCode}' is not a valid OSI symbol.", nameof(securityId));
		return false;
	}

	private async Task<DateTime[]> GetRequestDates(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		var to = Extensions.GetEasternDate((mdMsg.To ?? DateTime.UtcNow).ToUniversalTime());
		DateTime from;
		if (mdMsg.From != null)
			from = Extensions.GetEasternDate(mdMsg.From.Value.ToUniversalTime());
		else if (mdMsg.Count is long count)
			from = to.AddDays(-(Math.Min(count.Max(1), 9120) * 2 + 10));
		else
			from = to.AddDays(-14);

		if (from > to)
			throw new ArgumentOutOfRangeException(
				nameof(mdMsg.From), from, "The start date is after the end date.");
		IEnumerable<DateTime> dates = await SafeClient().GetTradingDays(new CboeTradingDaysQuery
		{
			StartDate = from,
			EndDate = to,
		}, cancellationToken);

		if (mdMsg.Count is long value)
		{
			var take = checked((int)Math.Min(value.Max(0), int.MaxValue));
			dates = mdMsg.From == null ? dates.TakeLast(take) : dates.Take(take);
		}
		else if (mdMsg.From == null)
			dates = dates.TakeLast(1);
		return dates.ToArray();
	}

	private async Task<DateTime[]> GetTickDates(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		var to = Extensions.GetEasternDate((mdMsg.To ?? DateTime.UtcNow).ToUniversalTime());
		var from = mdMsg.From == null
			? to.AddDays(mdMsg.Count == null ? -14 : -30)
			: Extensions.GetEasternDate(mdMsg.From.Value.ToUniversalTime());
		if (from > to)
			throw new ArgumentOutOfRangeException(
				nameof(mdMsg.From), from, "The start date is after the end date.");
		var dates = await SafeClient().GetTradingDays(new CboeTradingDaysQuery
		{
			StartDate = from,
			EndDate = to,
		}, cancellationToken);
		return mdMsg.From == null && mdMsg.Count == null ? dates.TakeLast(1).ToArray() : dates;
	}

	private async Task<DateTime> GetLatestTradingDate(DateTime to,
		CancellationToken cancellationToken)
	{
		var dates = await SafeClient().GetTradingDays(new CboeTradingDaysQuery
		{
			StartDate = to.AddDays(-14),
			EndDate = to,
		}, cancellationToken);
		return dates.LastOrDefault() is var date && date != default
			? date
			: throw new InvalidOperationException("Cboe returned no recent trading days.");
	}

	private static string GetBoundaryTime(DateTime? value, DateTime date)
	{
		if (value == null || Extensions.GetEasternDate(value.Value.ToUniversalTime()) != date)
			return null;
		return Extensions.ToEasternTime(value.Value.ToUniversalTime());
	}
}
