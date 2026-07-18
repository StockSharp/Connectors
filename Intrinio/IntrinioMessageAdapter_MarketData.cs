namespace StockSharp.Intrinio;

public partial class IntrinioMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);

		var client = SafeRest();
		var value = (lookupMsg.SecurityId.Native as string)
			.IsEmpty(lookupMsg.SecurityId.SecurityCode);
		var board = lookupMsg.SecurityId.BoardCode;
		var securityTypes = lookupMsg.GetSecurityTypes();
		var skip = lookupMsg.Skip ?? 0;
		var left = lookupMsg.Count ?? long.MaxValue;
		var sent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		async Task Emit(SecurityMessage security)
		{
			if (left <= 0 || security == null ||
				!security.IsMatch(lookupMsg, securityTypes) ||
				!sent.Add($"{security.SecurityId.BoardCode}:{security.SecurityId.SecurityCode}"))
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

		var exactOption = IntrinioOptionKey.TryParse(value, out var optionKey);
		var optionsRequested = board.EqualsIgnoreCase(OptionBoard) || exactOption ||
			securityTypes.Contains(SecurityTypes.Option) || !lookupMsg.GetUnderlyingCode().IsEmpty();
		var equitiesRequested = !board.EqualsIgnoreCase(OptionBoard) && !exactOption &&
			(securityTypes.Count == 0 || securityTypes.Any(type => type != SecurityTypes.Option));

		if (equitiesRequested && left > 0)
		{
			if (!value.IsEmpty() && board.EqualsIgnoreCase(EquityBoard))
			{
				try
				{
					await Emit((await client.GetSecurity(value, cancellationToken))
						?.ToSecurityMessage(lookupMsg.TransactionId));
				}
				catch (IntrinioApiException error) when (error.StatusCode == HttpStatusCode.NotFound)
				{
				}
			}

			if (left > 0 && !value.IsEmpty())
			{
				var response = await client.SearchSecurities(new()
				{
					Query = value,
					PageSize = ToPageSize(left),
				}, cancellationToken);
				foreach (var security in response?.Securities ?? [])
				{
					await Emit(security?.ToSecurityMessage(lookupMsg.TransactionId));
					if (left <= 0)
						break;
				}
			}
			else if (left > 0)
			{
				string nextPage = null;
				var pages = new HashSet<string>(StringComparer.Ordinal);
				do
				{
					var response = await client.GetSecurities(new()
					{
						IsActive = true,
						IsDelisted = false,
						CompositeMic = "USCOMP",
						IsPrimaryListing = true,
						PageSize = ToPageSize(left),
						NextPage = nextPage,
					}, cancellationToken);
					foreach (var security in response?.Securities ?? [])
					{
						await Emit(security?.ToSecurityMessage(lookupMsg.TransactionId));
						if (left <= 0)
							break;
					}
					nextPage = response?.NextPage;
					EnsureNewPage(pages, nextPage, "securities");
				}
				while (!nextPage.IsEmpty() && left > 0);
			}
		}

		if (optionsRequested && left > 0)
		{
			var underlying = lookupMsg.GetUnderlyingCode();
			if (exactOption)
				underlying = optionKey.Root;
			else if (underlying.IsEmpty())
				underlying = value;
			if (!underlying.IsEmpty())
			{
				string nextPage = null;
				var pages = new HashSet<string>(StringComparer.Ordinal);
				do
				{
					var response = await client.GetOptions(underlying, new()
					{
						Type = exactOption ? optionKey.OptionType.ToString().ToLowerInvariant() : null,
						Strike = exactOption ? optionKey.Strike : null,
						Expiration = exactOption
							? optionKey.Expiry.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
							: null,
						PageSize = ToPageSize(left),
						NextPage = nextPage,
					}, cancellationToken);
					foreach (var option in response?.Options ?? [])
					{
						if (option == null || (exactOption &&
							(!IntrinioOptionKey.TryParse(option.Code, out var returnedKey) ||
							!returnedKey.Code.EqualsIgnoreCase(optionKey.Code))))
						{
							continue;
						}
						await Emit(option.ToSecurityMessage(lookupMsg.TransactionId));
						if (left <= 0)
							break;
					}
					nextPage = response?.NextPage;
					EnsureNewPage(pages, nextPage, "options");
				}
				while (!nextPage.IsEmpty() && left > 0);
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
			await SafeRealtime().UnsubscribeAsync(mdMsg.OriginalTransactionId, cancellationToken);
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
			throw new NotSupportedException("Intrinio does not expose historical quote events through REST.");

		var isOption = TryGetOption(mdMsg.SecurityId, out var optionKey);
		if (mdMsg.Count == 1)
		{
			if (isOption)
				await SendOptionSnapshot(mdMsg, optionKey, cancellationToken);
			else
				await SendEquitySnapshot(mdMsg, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}
		if (mdMsg.IsHistoryOnly())
			throw new NotSupportedException(
				"Intrinio does not expose historical quote events through REST.");

		await SubscribeRealtime(mdMsg, isOption, optionKey, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await SafeRealtime().UnsubscribeAsync(mdMsg.OriginalTransactionId, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var isOption = TryGetOption(mdMsg.SecurityId, out var optionKey);
		if (mdMsg.From != null || mdMsg.To != null || mdMsg.IsHistoryOnly())
		{
			var to = (mdMsg.To ?? DateTime.UtcNow).ToUtc();
			var from = (mdMsg.From ?? to - TimeSpan.FromDays(7)).ToUtc();
			if (from > to)
				throw new ArgumentOutOfRangeException(nameof(mdMsg.From), from,
					"The trade-history start time is after its end time.");
			if (DateTime.UtcNow - from > TimeSpan.FromDays(7) || to - from > TimeSpan.FromDays(7))
				throw new NotSupportedException(
					"Intrinio REST trade history is limited to the latest seven days and a seven-day range.");

			if (isOption)
				await SendOptionTrades(mdMsg, optionKey, from, to, cancellationToken);
			else
				await SendEquityTrades(mdMsg, from, to, cancellationToken);
		}

		if (!mdMsg.IsHistoryOnly())
			await SubscribeRealtime(mdMsg, isOption, optionKey, cancellationToken);

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
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
		if (!AllTimeFrames.Contains(timeFrame))
			throw new NotSupportedException($"Intrinio does not support {timeFrame} candles.");
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUtc();
		var from = (mdMsg.From ?? Extensions.EstimateFrom(to, timeFrame, mdMsg.Count)).ToUtc();
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.From), from,
				"The candle-history start time is after its end time.");

		if (TryGetOption(mdMsg.SecurityId, out var optionKey))
			await SendOptionCandles(mdMsg, optionKey, timeFrame, from, to, cancellationToken);
		else
			await SendEquityCandles(mdMsg, timeFrame, from, to, cancellationToken);

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

		var securityCode = mdMsg.SecurityId.SecurityCode;
		var request = new IntrinioNewsRequest
		{
			PageSize = ToPageSize(mdMsg.Count ?? 1000),
			Company = securityCode,
			StartDate = mdMsg.From?.ToUtc(),
			EndDate = mdMsg.To?.ToUtc(),
		};
		var items = new List<IntrinioNewsItem>();
		var pages = new HashSet<string>(StringComparer.Ordinal);
		do
		{
			var response = await SafeRest().GetNews(request, cancellationToken);
			items.AddRange((response?.News ?? []).WhereNotNull());
			request.NextPage = response?.NextPage;
			EnsureNewPage(pages, request.NextPage, "news");
		}
		while (!request.NextPage.IsEmpty() &&
			(mdMsg.Count == null || items.Count < mdMsg.Count));

		var ordered = items
			.Where(item => item.PublicationDate != null)
			.OrderBy(item => item.PublicationDate)
			.ToArray();
		foreach (var item in SelectHistory(ordered, mdMsg.Count, mdMsg.From != null))
		{
			var securityId = mdMsg.SecurityId;
			if (!securityCode.IsEmpty())
				securityId = securityId.NormalizeIntrinio(securityCode, false, securityCode);
			await SendOutMessageAsync(new NewsMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				ServerTime = item.PublicationDate.Value.ToUtc(),
				Id = item.Id,
				Headline = item.Title,
				Story = item.Summary,
				Source = item.Source,
				Url = item.Url,
				SecurityId = securityId,
			}, cancellationToken);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private async Task SubscribeRealtime(MarketDataMessage mdMsg, bool isOption,
		IntrinioOptionKey optionKey, CancellationToken cancellationToken)
	{
		var symbol = isOption
			? optionKey.StreamCode
			: mdMsg.SecurityId.SecurityCode.ThrowIfEmpty(nameof(mdMsg.SecurityId.SecurityCode)).ToUpperInvariant();
		var securityId = mdMsg.SecurityId.NormalizeIntrinio(
			isOption ? optionKey.Code : symbol,
			isOption,
			(mdMsg.SecurityId.Native as string).IsEmpty(isOption ? optionKey.Code : symbol));
		await SafeRealtime().SubscribeAsync(new()
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = securityId,
			Symbol = symbol,
			DataType = mdMsg.DataType2,
			IsOption = isOption,
		}, cancellationToken);
	}

	private async Task SendEquitySnapshot(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		var code = mdMsg.SecurityId.SecurityCode.ThrowIfEmpty(nameof(mdMsg.SecurityId.SecurityCode));
		var identifier = GetRestIdentifier(mdMsg.SecurityId, code);
		var source = EquityProvider.ToRealtimeSource();
		var price = await SafeRest().GetRealtimePrice(identifier,
			new() { Source = source }, cancellationToken)
			?? throw new InvalidOperationException($"Intrinio returned no current price for '{code}'.");
		IntrinioSecurityQuote quote = null;
		var quoteSource = EquityProvider.ToQuoteSource();
		if (!quoteSource.IsEmpty())
		{
			try
			{
				quote = await SafeRest().GetQuote(identifier,
					new() { IsActiveOnly = true, Source = quoteSource }, cancellationToken);
			}
			catch (IntrinioApiException error) when (error.StatusCode is
				HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
			{
				this.AddWarningLog("Intrinio composite quote is unavailable for {0}: {1}",
					code, error.Message);
			}
		}

		var serverTime = MaxTime(price.UpdatedOn, price.LastTime,
			price.BidTime, price.AskTime, quote?.LastTime)
			?? throw new InvalidOperationException(
				$"Intrinio returned an untimed current price for '{code}'.");
		var securityId = mdMsg.SecurityId.NormalizeIntrinio(code, false, identifier);
		await SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = mdMsg.TransactionId,
			SecurityId = securityId,
			ServerTime = serverTime,
		}
		.TryAdd(Level1Fields.LastTradePrice, price.LastPrice ?? quote?.Last)
		.TryAdd(Level1Fields.LastTradeVolume, price.LastSize)
		.TryAdd(Level1Fields.LastTradeTime, price.LastTime?.ToUtc())
		.TryAdd(Level1Fields.BestBidPrice, price.BidPrice)
		.TryAdd(Level1Fields.BestBidVolume, price.BidSize)
		.TryAdd(Level1Fields.BestBidTime, price.BidTime?.ToUtc())
		.TryAdd(Level1Fields.BestAskPrice, price.AskPrice)
		.TryAdd(Level1Fields.BestAskVolume, price.AskSize)
		.TryAdd(Level1Fields.BestAskTime, price.AskTime?.ToUtc())
		.TryAdd(Level1Fields.OpenPrice, price.OpenPrice ?? quote?.Open)
		.TryAdd(Level1Fields.HighPrice, price.HighPrice ?? quote?.High)
		.TryAdd(Level1Fields.LowPrice, price.LowPrice ?? quote?.Low)
		.TryAdd(Level1Fields.ClosePrice, price.ClosePrice)
		.TryAdd(Level1Fields.Volume, price.MarketVolume ?? price.ExchangeVolume ?? quote?.MarketVolume)
		.TryAdd(Level1Fields.MarketPriceYesterday, price.EodClosePrice ?? quote?.PreviousClose)
		.TryAdd(Level1Fields.HighPrice52Week, quote?.FiftyTwoWeekHigh)
		.TryAdd(Level1Fields.LowPrice52Week, quote?.FiftyTwoWeekLow)
		.TryAdd(Level1Fields.PriceEarnings, quote?.PriceEarnings)
		.TryAdd(Level1Fields.Change, quote?.ChangePercent), cancellationToken);
	}

	private async Task SendOptionSnapshot(MarketDataMessage mdMsg,
		IntrinioOptionKey optionKey, CancellationToken cancellationToken)
	{
		var identifier = GetRestIdentifier(mdMsg.SecurityId, optionKey.StreamCode);
		var response = await SafeRest().GetOptionRealtime(identifier, new()
		{
			Source = IsDelayedOptions.ToOptionRestSource(),
			StockPriceSource = EquityProvider.ToQuoteSource(),
			Model = "black_scholes",
			IsShowExtendedPrice = true,
		}, cancellationToken) ?? throw new InvalidOperationException(
			$"Intrinio returned no current option price for '{optionKey.Code}'.");
		var price = response.Price ?? throw new InvalidOperationException(
			$"Intrinio returned no timed option price for '{optionKey.Code}'.");
		var serverTime = MaxTime(price.LastTimestamp, price.BidTimestamp, price.AskTimestamp)
			?? throw new InvalidOperationException(
				$"Intrinio returned an untimed current option price for '{optionKey.Code}'.");
		var securityId = mdMsg.SecurityId.NormalizeIntrinio(optionKey.Code, true, identifier);
		await SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = mdMsg.TransactionId,
			SecurityId = securityId,
			ServerTime = serverTime,
		}
		.TryAdd(Level1Fields.LastTradePrice, price.Last)
		.TryAdd(Level1Fields.LastTradeVolume, price.LastSize)
		.TryAdd(Level1Fields.LastTradeTime, price.LastTimestamp?.ToUtc())
		.TryAdd(Level1Fields.BestBidPrice, price.Bid)
		.TryAdd(Level1Fields.BestBidVolume, price.BidSize)
		.TryAdd(Level1Fields.BestBidTime, price.BidTimestamp?.ToUtc())
		.TryAdd(Level1Fields.BestAskPrice, price.Ask)
		.TryAdd(Level1Fields.BestAskVolume, price.AskSize)
		.TryAdd(Level1Fields.BestAskTime, price.AskTimestamp?.ToUtc())
		.TryAdd(Level1Fields.Volume, price.Volume)
		.TryAdd(Level1Fields.OpenInterest, price.OpenInterest)
		.TryAdd(Level1Fields.OpenPrice, response.ExtendedPrice?.TradeOpen)
		.TryAdd(Level1Fields.HighPrice, response.ExtendedPrice?.TradeHigh)
		.TryAdd(Level1Fields.LowPrice, response.ExtendedPrice?.TradeLow)
		.TryAdd(Level1Fields.ClosePrice, response.ExtendedPrice?.TradeClose)
		.TryAdd(Level1Fields.TheorPrice, response.ExtendedPrice?.Mark)
		.TryAdd(Level1Fields.ImpliedVolatility, response.Stats?.ImpliedVolatility)
		.TryAdd(Level1Fields.Delta, response.Stats?.Delta)
		.TryAdd(Level1Fields.Gamma, response.Stats?.Gamma)
		.TryAdd(Level1Fields.Theta, response.Stats?.Theta)
		.TryAdd(Level1Fields.Vega, response.Stats?.Vega)
		.TryAdd(Level1Fields.UnderlyingPrice, response.Stats?.UnderlyingPrice), cancellationToken);
	}

	private async Task SendEquityTrades(MarketDataMessage mdMsg, DateTime from,
		DateTime to, CancellationToken cancellationToken)
	{
		var code = mdMsg.SecurityId.SecurityCode.ThrowIfEmpty(nameof(mdMsg.SecurityId.SecurityCode));
		var identifier = GetRestIdentifier(mdMsg.SecurityId, code);
		var request = new IntrinioTradesRequest
		{
			Source = EquityProvider.ToTradeSource(),
			StartDate = from,
			StartTime = from.ToIntrinioTime(),
			EndDate = to,
			EndTime = to.ToIntrinioTime(),
			Timezone = "UTC",
			IsDarkpoolOnly = false,
			PageSize = 10000,
		};
		var trades = new List<IntrinioSecurityTrade>();
		var pages = new HashSet<string>(StringComparer.Ordinal);
		do
		{
			var response = await SafeRest().GetSecurityTrades(identifier, request, cancellationToken);
			trades.AddRange((response?.Trades ?? []).WhereNotNull());
			request.NextPage = response?.NextPage;
			EnsureNewPage(pages, request.NextPage, "equity trades");
		}
		while (!request.NextPage.IsEmpty());

		var securityId = mdMsg.SecurityId.NormalizeIntrinio(code, false, identifier);
		var ordered = trades.Where(trade => trade.Timestamp != null && trade.Price != null)
			.OrderBy(trade => trade.Timestamp).ToArray();
		foreach (var trade in SelectHistory(ordered, mdMsg.Count, mdMsg.From != null))
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = securityId,
				ServerTime = trade.Timestamp.Value.ToUtc(),
				TradePrice = trade.Price,
				TradeVolume = trade.Size,
			}, cancellationToken);
		}
	}

	private async Task SendOptionTrades(MarketDataMessage mdMsg,
		IntrinioOptionKey optionKey, DateTime from, DateTime to,
		CancellationToken cancellationToken)
	{
		var identifier = GetRestIdentifier(mdMsg.SecurityId, optionKey.StreamCode);
		var request = new IntrinioOptionTradesRequest
		{
			Source = IsDelayedOptions.ToOptionRestSource(),
			StartDate = from,
			StartTime = from.ToIntrinioTime(),
			EndDate = to,
			EndTime = to.ToIntrinioTime(),
			Timezone = "UTC",
			PageSize = 10000,
		};
		var trades = new List<IntrinioOptionTrade>();
		var pages = new HashSet<string>(StringComparer.Ordinal);
		do
		{
			var response = await SafeRest().GetOptionTrades(identifier, request, cancellationToken);
			trades.AddRange((response?.Trades ?? []).WhereNotNull());
			request.NextPage = response?.NextPage;
			EnsureNewPage(pages, request.NextPage, "option trades");
		}
		while (!request.NextPage.IsEmpty());

		var securityId = mdMsg.SecurityId.NormalizeIntrinio(optionKey.Code, true, identifier);
		var ordered = trades.Where(trade => trade.Timestamp != null && trade.Price != null)
			.OrderBy(trade => trade.Timestamp).ToArray();
		foreach (var trade in SelectHistory(ordered, mdMsg.Count, mdMsg.From != null))
		{
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = securityId,
				ServerTime = trade.Timestamp.Value.ToUtc(),
				TradePrice = trade.Price,
				TradeVolume = trade.Size,
				SeqNum = ToSequence(trade.SequenceId),
			}, cancellationToken);
		}
	}

	private async Task SendEquityCandles(MarketDataMessage mdMsg, TimeSpan timeFrame,
		DateTime from, DateTime to, CancellationToken cancellationToken)
	{
		var code = mdMsg.SecurityId.SecurityCode.ThrowIfEmpty(nameof(mdMsg.SecurityId.SecurityCode));
		var identifier = GetRestIdentifier(mdMsg.SecurityId, code);
		var candles = new List<(DateTime time, decimal open, decimal high,
			decimal low, decimal close, decimal volume)>();
		if (timeFrame.IsIntraday())
		{
			var request = new IntrinioSecurityIntervalsRequest
			{
				IntervalSize = timeFrame.ToIntrinioInterval(),
				Source = EquityProvider.ToIntervalSource(),
				StartDate = from,
				StartTime = from.ToIntrinioTime(),
				EndDate = to,
				EndTime = to.ToIntrinioTime(),
				Timezone = "UTC",
				PageSize = 10000,
				IsSplitAdjusted = IsAdjusted,
				IsIncludeQuoteOnlyBars = false,
			};
			var pages = new HashSet<string>(StringComparer.Ordinal);
			do
			{
				var response = await SafeRest().GetSecurityIntervals(identifier, request, cancellationToken);
				foreach (var interval in response?.Intervals ?? [])
				{
					if (interval?.Time == null || interval.Open == null || interval.High == null ||
						interval.Low == null || interval.Close == null)
					{
						continue;
					}
					candles.Add((interval.Time.Value.ToUtc(), interval.Open.Value,
						interval.High.Value, interval.Low.Value, interval.Close.Value,
						interval.Volume ?? 0));
				}
				request.NextPage = response?.NextPage;
				EnsureNewPage(pages, request.NextPage, "equity intervals");
			}
			while (!request.NextPage.IsEmpty());
		}
		else
		{
			var request = new IntrinioStockPricesRequest
			{
				StartDate = from,
				EndDate = to,
				Frequency = timeFrame.ToIntrinioFrequency(),
				PageSize = 10000,
			};
			var pages = new HashSet<string>(StringComparer.Ordinal);
			do
			{
				var response = await SafeRest().GetStockPrices(identifier, request, cancellationToken);
				foreach (var price in response?.StockPrices ?? [])
				{
					if (price?.Date == null)
						continue;
					var open = IsAdjusted ? price.AdjustedOpen : price.Open;
					var high = IsAdjusted ? price.AdjustedHigh : price.High;
					var low = IsAdjusted ? price.AdjustedLow : price.Low;
					var close = IsAdjusted ? price.AdjustedClose : price.Close;
					var volume = IsAdjusted ? price.AdjustedVolume : price.Volume;
					if (open == null || high == null || low == null || close == null)
						continue;
					candles.Add((DateTime.SpecifyKind(price.Date.Value.Date, DateTimeKind.Utc),
						open.Value, high.Value, low.Value, close.Value, volume ?? 0));
				}
				request.NextPage = response?.NextPage;
				EnsureNewPage(pages, request.NextPage, "equity end-of-day prices");
			}
			while (!request.NextPage.IsEmpty());
		}

		var securityId = mdMsg.SecurityId.NormalizeIntrinio(code, false, identifier);
		var ordered = candles.Where(candle => candle.time >= from && candle.time <= to)
			.OrderBy(candle => candle.time).ToArray();
		foreach (var candle in SelectHistory(ordered, mdMsg.Count, mdMsg.From != null))
		{
			await SendCandle(mdMsg, securityId, timeFrame, candle.time,
				candle.open, candle.high, candle.low, candle.close,
				candle.volume, null, cancellationToken);
		}
	}

	private async Task SendOptionCandles(MarketDataMessage mdMsg,
		IntrinioOptionKey optionKey, TimeSpan timeFrame, DateTime from, DateTime to,
		CancellationToken cancellationToken)
	{
		var identifier = GetRestIdentifier(mdMsg.SecurityId, optionKey.StreamCode);
		var securityId = mdMsg.SecurityId.NormalizeIntrinio(optionKey.Code, true, identifier);
		if (timeFrame.IsIntraday())
		{
			var response = await SafeRest().GetOptionIntervals(identifier, new()
			{
				IntervalSize = timeFrame.ToIntrinioInterval(),
				Source = IsDelayedOptions.ToOptionRestSource(),
				PageSize = ToPageSize(mdMsg.Count ?? 10000),
				EndTime = to.ToString("O", CultureInfo.InvariantCulture),
			}, cancellationToken);
			var intervals = (response?.Intervals ?? [])
				.Where(interval => interval?.OpenTime != null && interval.Open != null &&
					interval.High != null && interval.Low != null && interval.Close != null)
				.OrderBy(interval => interval.OpenTime)
				.Where(interval => interval.OpenTime.Value.ToUtc() >= from &&
					interval.OpenTime.Value.ToUtc() <= to)
				.ToArray();
			foreach (var interval in SelectHistory(intervals, mdMsg.Count, mdMsg.From != null))
			{
				await SendCandle(mdMsg, securityId, timeFrame,
					interval.OpenTime.Value.ToUtc(), interval.Open.Value, interval.High.Value,
					interval.Low.Value, interval.Close.Value, interval.Volume ?? 0,
					null, cancellationToken);
			}
			return;
		}
		if (timeFrame != TimeSpan.FromDays(1))
			throw new NotSupportedException(
				"Intrinio option end-of-day prices are available as daily candles only.");

		var request = new IntrinioOptionPricesEodRequest
		{
			StartDate = from,
			EndDate = to,
			IsRecalculateStats = false,
		};
		var prices = new List<IntrinioOptionPriceEod>();
		var pages = new HashSet<string>(StringComparer.Ordinal);
		do
		{
			var response = await SafeRest().GetOptionPricesEod(identifier, request, cancellationToken);
			prices.AddRange((response?.Prices ?? []).WhereNotNull());
			request.NextPage = response?.NextPage;
			EnsureNewPage(pages, request.NextPage, "option end-of-day prices");
		}
		while (!request.NextPage.IsEmpty());

		var parsed = new List<(DateTime time, IntrinioOptionPriceEod price)>();
		foreach (var price in prices)
		{
			if (price.Open == null || price.High == null || price.Low == null || price.Close == null ||
				!DateTime.TryParseExact(price.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
					DateTimeStyles.None, out var date))
			{
				continue;
			}
			parsed.Add((DateTime.SpecifyKind(date.Date, DateTimeKind.Utc), price));
		}
		var ordered = parsed.Where(item => item.time >= from && item.time <= to)
			.OrderBy(item => item.time).ToArray();
		foreach (var item in SelectHistory(ordered, mdMsg.Count, mdMsg.From != null))
		{
			await SendCandle(mdMsg, securityId, timeFrame, item.time,
				item.price.Open.Value, item.price.High.Value, item.price.Low.Value,
				item.price.Close.Value, item.price.Volume ?? 0,
				item.price.OpenInterest, cancellationToken);
		}
	}

	private ValueTask SendCandle(MarketDataMessage mdMsg, SecurityId securityId,
		TimeSpan timeFrame, DateTime openTime, decimal open, decimal high,
		decimal low, decimal close, decimal volume, decimal? openInterest,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OriginalTransactionId = mdMsg.TransactionId,
			SecurityId = securityId,
			DataType = mdMsg.DataType2,
			TypedArg = timeFrame,
			OpenTime = openTime,
			OpenPrice = open,
			HighPrice = high,
			LowPrice = low,
			ClosePrice = close,
			TotalVolume = volume,
			OpenInterest = openInterest,
			State = CandleStates.Finished,
		}, cancellationToken);

	private static bool TryGetOption(SecurityId securityId, out IntrinioOptionKey optionKey)
	{
		var isOptionBoard = securityId.BoardCode.EqualsIgnoreCase(OptionBoard);
		if (IntrinioOptionKey.TryParse(securityId.SecurityCode, out optionKey))
			return true;
		if (isOptionBoard)
			throw new FormatException(
				$"Intrinio option security code '{securityId.SecurityCode}' is not a valid OSI identifier.");
		return false;
	}

	private static string GetRestIdentifier(SecurityId securityId, string fallback)
		=> (securityId.Native as string).IsEmpty(fallback);

	private static int ToPageSize(long left)
		=> checked((int)Math.Min(Math.Max(left, 1), 10000));

	private static void EnsureNewPage(HashSet<string> pages, string nextPage,
		string resource)
	{
		if (!nextPage.IsEmpty() && !pages.Add(nextPage))
			throw new InvalidOperationException(
				$"Intrinio repeated the {resource} pagination token '{nextPage}'.");
	}

	private static T[] SelectHistory<T>(T[] ordered, long? count, bool hasFrom)
	{
		if (count == null || count >= ordered.LongLength)
			return ordered;
		var take = checked((int)Math.Min(Math.Max(count.Value, 0), int.MaxValue));
		return hasFrom ? [.. ordered.Take(take)] : [.. ordered.TakeLast(take)];
	}

	private static DateTime? MaxTime(params DateTime?[] values)
	{
		DateTime? result = null;
		foreach (var value in values)
		{
			if (value == null)
				continue;
			var utc = value.Value.ToUtc();
			if (result == null || utc > result)
				result = utc;
		}
		return result;
	}

	private static long ToSequence(decimal? value)
	{
		if (value is not (>= 0 and <= long.MaxValue))
			return 0;
		return decimal.ToInt64(decimal.Truncate(value.Value));
	}
}
