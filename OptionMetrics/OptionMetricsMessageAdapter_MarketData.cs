namespace StockSharp.OptionMetrics;

public partial class OptionMetricsMessageAdapter
{
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

		var master = SafeSecurityMaster();
		var catalog = SafeCatalog();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var board = lookupMsg.SecurityId.BoardCode;
		var native = lookupMsg.SecurityId.Native as string;
		var hasExactKey = IvyDbSecurityKey.TryParse(native, out var exactKey);
		var value = hasExactKey ? exactKey.Symbol :
			native.IsEmpty(lookupMsg.SecurityId.SecurityCode).IsEmpty(lookupMsg.Name);
		value = value?.Trim();
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		async Task Emit(SecurityMessage security, SecurityLookupMessage criteria)
		{
			var key = security?.SecurityId.Native as string;
			if (security == null || left <= 0 || key.IsEmpty() || !emitted.Add(key))
			{
				return;
			}
			var expected = criteria.SecurityId;
			if (expected.Native == null && !expected.SecurityCode.IsEmpty() &&
				!expected.BoardCode.IsEmpty() &&
				security.SecurityId.BoardCode.EqualsIgnoreCase(expected.BoardCode) &&
				(security.SecurityId.SecurityCode.EqualsIgnoreCase(expected.SecurityCode) ||
					security.SecurityId.SecurityCode.Replace(" ", string.Empty)
						.EqualsIgnoreCase(expected.SecurityCode.Replace(" ", string.Empty))))
			{
				criteria = (SecurityLookupMessage)criteria.Clone();
				criteria.SecurityId = security.SecurityId;
			}
			if (!security.IsMatch(criteria, securityTypes))
				return;
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
		bool Requested(IvyDbMarkets market)
		{
			if (!noBoard)
				return board.EqualsIgnoreCase(market.ToBoard());
			if (noTypes)
				return market == IvyDbMarkets.Stocks;
			return market == IvyDbMarkets.Options
				? securityTypes.Contains(SecurityTypes.Option)
				: securityTypes.Any(type => type != SecurityTypes.Option);
		}

		var optionsRequested = Requested(IvyDbMarkets.Options) ||
			lookupMsg.OptionType != null || lookupMsg.Strike != null ||
			lookupMsg.ExpiryDate != null || lookupMsg.UnderlyingSecurityId != default ||
			(hasExactKey && exactKey.Market == IvyDbMarkets.Options);
		if (optionsRequested && left > 0)
		{
			long? underlyingId = null;
			var symbolFilter = value;
			var exactOption = hasExactKey && exactKey.Market == IvyDbMarkets.Options;
			if (exactOption)
				underlyingId = exactKey.SecurityId;
			else if (lookupMsg.UnderlyingSecurityId != default)
			{
				var underlying = lookupMsg.UnderlyingSecurityId.GetIvyDbRequest(master);
				if (underlying.Market != IvyDbMarkets.Stocks || underlying.Key == null)
					throw new InvalidOperationException("IvyDB option lookup requires an underlying security.");
				underlyingId = underlying.Key.Value.SecurityId;
			}
			else if (master.FindAll(value) is { Count: 1 } underlyings)
			{
				underlyingId = underlyings[0].SecurityId;
				symbolFilter = null;
			}
			else if (master.FindAll(value).Count > 1)
			{
				throw new InvalidOperationException(
					$"IvyDB underlying alias '{value}' is ambiguous. Use UnderlyingSecurityId from lookup.");
			}

			DateTime? lookupDate = exactOption ? exactKey.Expiration : lookupMsg.ExpiryDate;
			if (lookupDate == null && Extensions.TryGetOccExpiration(symbolFilter,
				out var symbolExpiration))
			{
				lookupDate = symbolExpiration;
			}
			var file = catalog.GetLatest(IvyDbFileKinds.OptionPrice, lookupDate?.Date);
			if (file != null)
			{
				var criteria = lookupMsg;
				if (symbolFilter.IsEmpty() && !lookupMsg.SecurityId.SecurityCode.IsEmpty())
				{
					criteria = (SecurityLookupMessage)lookupMsg.Clone();
					criteria.SecurityId = default;
				}
				await foreach (var row in catalog.ReadOptionPrices(file, cancellationToken))
				{
					if (exactOption && (row.SecurityId != exactKey.SecurityId ||
						row.OptionId != exactKey.OptionId) ||
						!exactOption && underlyingId != null && row.SecurityId != underlyingId ||
						!symbolFilter.IsEmpty() && !row.MatchesSymbol(symbolFilter) ||
						lookupMsg.OptionType != null && row.OptionType != lookupMsg.OptionType ||
						lookupMsg.Strike != null && row.Strike != lookupMsg.Strike ||
						lookupMsg.ExpiryDate != null &&
							row.Expiration.Date != lookupMsg.ExpiryDate.Value.Date)
					{
						continue;
					}
					await Emit(row.ToSecurityMessage(master, lookupMsg.TransactionId), criteria);
					if (left <= 0)
						break;
				}
			}
		}

		if (left > 0 && Requested(IvyDbMarkets.Stocks) &&
			(!hasExactKey || exactKey.Market == IvyDbMarkets.Stocks))
		{
			IEnumerable<IvyDbSecurityRow> securities = master.Securities;
			var criteria = lookupMsg;
			if (hasExactKey)
				securities = master.Find(exactKey.SecurityId) is { } exact ? [exact] : [];
			else if (master.FindAll(value) is { Count: > 0 } matches)
			{
				securities = matches;
				if (matches.Any(row => !IvyDbSecurityMaster.GetSymbol(row)
					.EqualsIgnoreCase(value)))
				{
					criteria = (SecurityLookupMessage)lookupMsg.Clone();
					criteria.SecurityId = default;
					criteria.Name = null;
				}
			}
			foreach (var row in securities)
			{
				await Emit(row.ToSecurityMessage(master, lookupMsg.TransactionId), criteria);
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
			await Complete(mdMsg, cancellationToken);
			return;
		}

		var request = mdMsg.SecurityId.GetIvyDbRequest(SafeSecurityMaster());
		var left = mdMsg.Count ?? long.MaxValue;
		if (request.Market == IvyDbMarkets.Options)
		{
			foreach (var file in GetRequestFiles(IvyDbFileKinds.OptionPrice, mdMsg,
				GetOptionExpiration(request), 1))
			{
				await foreach (var row in SafeCatalog().ReadOptionPrices(file, cancellationToken))
				{
					if (!request.Matches(row))
						continue;
					var time = GetMarketTime(row.Date, OptionSnapshotTime);
					if (!InRange(time, mdMsg))
						continue;
					var message = CreateOptionLevel1(mdMsg.TransactionId,
						row.ToKey().ToSecurityId(), time, row);
					if (message == null)
						continue;
					await SendOutMessageAsync(message, cancellationToken);
					if (--left <= 0)
						break;
				}
				if (left <= 0)
					break;
			}
		}
		else
		{
			var key = request.Key.Value;
			var latest = PriceAdjustment == IvyDbPriceAdjustments.Raw
				? new IvyDbAdjustmentFactors(1m, 1m)
				: await SafeCatalog().FindLatestFactorsAsync(key.SecurityId, cancellationToken);
			foreach (var file in GetRequestFiles(IvyDbFileKinds.SecurityPrice, mdMsg, null, 1))
			{
				await foreach (var row in SafeCatalog().ReadSecurityPrices(file, cancellationToken))
				{
					if (row.SecurityId != key.SecurityId)
						continue;
					var time = GetMarketTime(row.Date, SessionEnd);
					if (!InRange(time, mdMsg))
						continue;
					var message = CreateSecurityLevel1(mdMsg.TransactionId,
						mdMsg.SecurityId.NormalizeIvyDb(key), time, row, latest);
					if (message == null)
						continue;
					await SendOutMessageAsync(message, cancellationToken);
					if (--left <= 0)
						break;
				}
				if (left <= 0)
					break;
			}
		}

		await Complete(mdMsg, cancellationToken);
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
			await Complete(mdMsg, cancellationToken);
			return;
		}

		var request = mdMsg.SecurityId.GetIvyDbRequest(SafeSecurityMaster());
		var left = mdMsg.Count ?? long.MaxValue;
		if (request.Market == IvyDbMarkets.Options)
		{
			foreach (var file in GetRequestFiles(IvyDbFileKinds.OptionPrice, mdMsg,
				GetOptionExpiration(request), 1))
			{
				await foreach (var row in SafeCatalog().ReadOptionPrices(file, cancellationToken))
				{
					if (!request.Matches(row))
						continue;
					var time = GetMarketTime(row.Date, OptionSnapshotTime);
					if (!InRange(time, mdMsg))
						continue;
					var depth = CreateDepth(mdMsg.TransactionId, row.ToKey().ToSecurityId(),
						time, Positive(row.Bid), Positive(row.Ask));
					if (depth == null)
						continue;
					await SendOutMessageAsync(depth, cancellationToken);
					if (--left <= 0)
						break;
				}
				if (left <= 0)
					break;
			}
		}
		else
		{
			var key = request.Key.Value;
			var latest = PriceAdjustment == IvyDbPriceAdjustments.Raw
				? new IvyDbAdjustmentFactors(1m, 1m)
				: await SafeCatalog().FindLatestFactorsAsync(key.SecurityId, cancellationToken);
			foreach (var file in GetRequestFiles(IvyDbFileKinds.SecurityPrice, mdMsg, null, 1))
			{
				await foreach (var row in SafeCatalog().ReadSecurityPrices(file, cancellationToken))
				{
					if (row.SecurityId != key.SecurityId)
						continue;
					var time = GetMarketTime(row.Date, SessionEnd);
					if (!InRange(time, mdMsg))
						continue;
					var bid = row.BidOrLow is < 0
						? ((decimal?)Math.Abs(row.BidOrLow.Value)).AdjustPrice(
							row, latest, PriceAdjustment)
						: null;
					var ask = row.AskOrHigh is < 0
						? ((decimal?)Math.Abs(row.AskOrHigh.Value)).AdjustPrice(
							row, latest, PriceAdjustment)
						: null;
					var depth = CreateDepth(mdMsg.TransactionId,
						mdMsg.SecurityId.NormalizeIvyDb(key), time, bid, ask);
					if (depth == null)
						continue;
					await SendOutMessageAsync(depth, cancellationToken);
					if (--left <= 0)
						break;
				}
				if (left <= 0)
					break;
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
		if (timeFrame != TimeSpan.FromDays(1))
			throw new NotSupportedException("IvyDB Security Price files provide daily candles only.");

		var request = mdMsg.SecurityId.GetIvyDbRequest(SafeSecurityMaster());
		if (request.Market != IvyDbMarkets.Stocks || request.Key == null)
			throw new NotSupportedException("IvyDB Option Price rows are not OHLC candles.");
		var key = request.Key.Value;
		var latest = PriceAdjustment == IvyDbPriceAdjustments.Raw
			? new IvyDbAdjustmentFactors(1m, 1m)
			: await SafeCatalog().FindLatestFactorsAsync(key.SecurityId, cancellationToken);
		var left = mdMsg.Count ?? long.MaxValue;

		foreach (var file in GetRequestFiles(IvyDbFileKinds.SecurityPrice, mdMsg, null, 500))
		{
			await foreach (var row in SafeCatalog().ReadSecurityPrices(file, cancellationToken))
			{
				if (row.SecurityId != key.SecurityId || row.Open is not > 0 ||
					row.BidOrLow is not > 0 || row.AskOrHigh is not > 0 || row.Close is not > 0)
				{
					continue;
				}
				var openTime = GetMarketTime(row.Date, SessionStart);
				if (!InRange(openTime, mdMsg))
					continue;
				await SendOutMessageAsync(new TimeFrameCandleMessage
				{
					OriginalTransactionId = mdMsg.TransactionId,
					SecurityId = mdMsg.SecurityId.NormalizeIvyDb(key),
					DataType = mdMsg.DataType2,
					OpenTime = openTime,
					CloseTime = GetMarketTime(row.Date, SessionEnd),
					OpenPrice = row.Open.AdjustPrice(row, latest, PriceAdjustment).Value,
					HighPrice = row.AskOrHigh.AdjustPrice(row, latest, PriceAdjustment).Value,
					LowPrice = row.BidOrLow.AdjustPrice(row, latest, PriceAdjustment).Value,
					ClosePrice = row.Close.AdjustPrice(row, latest, PriceAdjustment).Value,
					TotalVolume = Math.Max(0, row.Volume.GetValueOrDefault()),
					State = CandleStates.Finished,
				}, cancellationToken);
				if (--left <= 0)
					break;
			}
			if (left <= 0)
				break;
		}

		await Complete(mdMsg, cancellationToken);
	}

	private IvyDbDataFile[] GetRequestFiles(IvyDbFileKinds kind, MarketDataMessage mdMsg,
		DateTime? maximumDate, long defaultLatestCount)
	{
		var from = mdMsg.From?.ToMarketDate(_marketTimeZone);
		var to = mdMsg.To?.ToMarketDate(_marketTimeZone);
		if (maximumDate != null && (to == null || maximumDate.Value.Date < to.Value.Date))
			to = maximumDate.Value.Date;
		if (from != null && to != null && from.Value.Date > to.Value.Date)
		{
			throw new ArgumentOutOfRangeException(nameof(mdMsg.From), mdMsg.From,
				"The IvyDB history start time is after its end time.");
		}
		long? latestCount = from == null ? mdMsg.Count ?? defaultLatestCount : null;
		return SafeCatalog().GetFiles(kind, from, to, latestCount);
	}

	private static DateTime? GetOptionExpiration(IvyDbSecurityRequest request)
	{
		if (request.Key?.Expiration is { } expiration)
			return expiration;
		return Extensions.TryGetOccExpiration(request.Symbol, out expiration)
			? expiration : null;
	}

	private DateTime GetMarketTime(DateTime date, TimeSpan time)
		=> (date.Date + time).FromMarketTime(_marketTimeZone);

	private static bool InRange(DateTime time, MarketDataMessage message)
		=> (message.From == null || time >= message.From.Value.ToUtc()) &&
			(message.To == null || time <= message.To.Value.ToUtc());

	private static Level1ChangeMessage CreateOptionLevel1(long transactionId,
		SecurityId securityId, DateTime time, IvyDbOptionPriceRow row)
	{
		var bid = Positive(row.Bid);
		var ask = Positive(row.Ask);
		var message = new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = time,
		}
		.TryAdd(Level1Fields.BestBidPrice, bid)
		.TryAdd(Level1Fields.BestBidTime, bid != null ? time : null)
		.TryAdd(Level1Fields.BestAskPrice, ask)
		.TryAdd(Level1Fields.BestAskTime, ask != null ? time : null)
		.TryAdd(Level1Fields.SpreadMiddle, bid != null && ask != null
			? (bid.Value + ask.Value) / 2m : null)
		.TryAdd(Level1Fields.Volume, NonNegative(row.Volume))
		.TryAdd(Level1Fields.OpenInterest, NonNegative(row.OpenInterest))
		.TryAdd(Level1Fields.ImpliedVolatility, row.ImpliedVolatility)
		.TryAdd(Level1Fields.Delta, row.Delta)
		.TryAdd(Level1Fields.Gamma, row.Gamma)
		.TryAdd(Level1Fields.Vega, row.Vega)
		.TryAdd(Level1Fields.Theta, row.Theta);
		return message.Changes.Count == 0 ? null : message;
	}

	private Level1ChangeMessage CreateSecurityLevel1(long transactionId,
		SecurityId securityId, DateTime time, IvyDbSecurityPriceRow row,
		IvyDbAdjustmentFactors latest)
	{
		var open = Positive(row.Open).AdjustPrice(row, latest, PriceAdjustment);
		var low = row.BidOrLow is > 0
			? row.BidOrLow.AdjustPrice(row, latest, PriceAdjustment) : null;
		var high = row.AskOrHigh is > 0
			? row.AskOrHigh.AdjustPrice(row, latest, PriceAdjustment) : null;
		var bid = row.BidOrLow is < 0
			? ((decimal?)Math.Abs(row.BidOrLow.Value)).AdjustPrice(
				row, latest, PriceAdjustment) : null;
		var ask = row.AskOrHigh is < 0
			? ((decimal?)Math.Abs(row.AskOrHigh.Value)).AdjustPrice(
				row, latest, PriceAdjustment) : null;
		var close = row.Close is > 0
			? row.Close.AdjustPrice(row, latest, PriceAdjustment) : null;
		var midpoint = row.Close is < 0
			? ((decimal?)Math.Abs(row.Close.Value)).AdjustPrice(
				row, latest, PriceAdjustment) : null;
		var message = new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = time,
		}
		.TryAdd(Level1Fields.OpenPrice, open)
		.TryAdd(Level1Fields.HighPrice, high)
		.TryAdd(Level1Fields.LowPrice, low)
		.TryAdd(Level1Fields.ClosePrice, close)
		.TryAdd(Level1Fields.LastTradePrice, close)
		.TryAdd(Level1Fields.LastTradeTime, close != null ? time : null)
		.TryAdd(Level1Fields.BestBidPrice, bid)
		.TryAdd(Level1Fields.BestBidTime, bid != null ? time : null)
		.TryAdd(Level1Fields.BestAskPrice, ask)
		.TryAdd(Level1Fields.BestAskTime, ask != null ? time : null)
		.TryAdd(Level1Fields.SpreadMiddle, midpoint)
		.TryAdd(Level1Fields.Volume, NonNegative(row.Volume));
		return message.Changes.Count == 0 ? null : message;
	}

	private static QuoteChangeMessage CreateDepth(long transactionId,
		SecurityId securityId, DateTime time, decimal? bid, decimal? ask)
	{
		if (bid is not > 0 && ask is not > 0)
			return null;
		return new()
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = time,
			Bids = bid is > 0 ? [new QuoteChange(bid.Value, 0)] : [],
			Asks = ask is > 0 ? [new QuoteChange(ask.Value, 0)] : [],
			State = QuoteChangeStates.SnapshotComplete,
		};
	}

	private async ValueTask Complete(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
	}

	private static decimal? Positive(decimal? value)
		=> value is > 0 ? value : null;

	private static decimal? NonNegative(long? value)
		=> value is >= 0 ? value.Value : null;
}
