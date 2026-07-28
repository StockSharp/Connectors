namespace StockSharp.JQuants;

public partial class JQuantsMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		var types = lookupMsg.GetSecurityTypes();
		var requested = (lookupMsg.SecurityId.Native as string)
			.IsEmpty(lookupMsg.SecurityId.SecurityCode)
			.IsEmpty(lookupMsg.Name);
		if (requested?.Length > 2 && requested[1] == ':')
			requested = requested[2..];
		var exactCode = requested?.All(char.IsDigit) == true
			? requested
			: null;
		var instruments = new List<JQuantsInstrument>();
		if (types.Count == 0 ||
			types.Contains(SecurityTypes.Stock) ||
			types.Contains(SecurityTypes.Fund))
		{
			var values = await RestClient.GetEquitiesAsync(
				exactCode, null, cancellationToken);
			instruments.AddRange(values
				.Select(static value => value.ToEquity())
				.Where(static value => !value.Code.IsEmpty()));
		}
		if (types.Contains(SecurityTypes.Future))
			instruments.AddRange(await LoadDerivativeInstrumentsAsync(
				JQuantsInstrumentKinds.Future, requested, exactCode,
				cancellationToken));
		if (types.Contains(SecurityTypes.Option))
			instruments.AddRange(await LoadDerivativeInstrumentsAsync(
				JQuantsInstrumentKinds.Option, requested, exactCode,
				cancellationToken));
		Remember(instruments);

		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var instrument in instruments
			.GroupBy(static value => value.NativeId,
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.First())
			.Where(value => Matches(value, requested))
			.OrderBy(static value => value.Code,
				StringComparer.OrdinalIgnoreCase))
		{
			var message = CreateSecurity(instrument,
				lookupMsg.TransactionId);
			if (!message.IsMatch(lookupMsg, types))
				continue;
			if (skip-- > 0)
				continue;
			await SendOutMessageAsync(message, cancellationToken);
			if (--left <= 0)
				break;
		}
		await SendSubscriptionResultAsync(lookupMsg,
			cancellationToken);
	}

	private async ValueTask<JQuantsInstrument[]>
		LoadDerivativeInstrumentsAsync(
			JQuantsInstrumentKinds kind, string code,
			string exactCode,
			CancellationToken cancellationToken)
	{
		var date = PreviousWeekday(CurrentTime.Date);
		JObject[] values = [];
		for (var attempt = 0;
			attempt < 3 && values.Length == 0;
			attempt++, date = PreviousWeekday(date.AddDays(-1)))
			values = kind == JQuantsInstrumentKinds.Future
				? await RestClient.GetFuturesAsync(date,
					cancellationToken)
				: await RestClient.GetOptionsAsync(exactCode, date,
					cancellationToken);
		return values
			.Select(value => value.ToDerivative(kind))
			.Where(value => !value.Code.IsEmpty() &&
				(code.IsEmpty() ||
					value.Code.Contains(code,
						StringComparison.OrdinalIgnoreCase)))
			.ToArray();
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		var instrument = ResolveInstrument(mdMsg.SecurityId);
		var to = (mdMsg.To ?? CurrentTime).ToUniversalTime();
		var from = (mdMsg.From ?? to.AddDays(-10))
			.ToUniversalTime();
		var bars = await LoadBarsAsync(instrument,
			TimeSpan.FromDays(1), from, to, cancellationToken);
		var latest = bars.LastOrDefault();
		if (latest is not null)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = ToSecurityId(instrument),
				ServerTime = latest.Time.UtcDateTime,
			}
			.TryAdd(Level1Fields.OpenPrice, latest.Open, true)
			.TryAdd(Level1Fields.HighPrice, latest.High, true)
			.TryAdd(Level1Fields.LowPrice, latest.Low, true)
			.TryAdd(Level1Fields.ClosePrice, latest.Close, true)
			.TryAdd(Level1Fields.LastTradePrice, latest.Close, true)
			.TryAdd(Level1Fields.Volume, latest.Volume, true)
			.TryAdd(Level1Fields.OpenInterest,
				latest.OpenInterest, true), cancellationToken);
		await CompleteMarketSubscriptionAsync(mdMsg,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		var instrument = ResolveInstrument(mdMsg.SecurityId);
		if (instrument.Kind != JQuantsInstrumentKinds.Equity)
			throw new NotSupportedException(
				"J-Quants tick data is available for equities only.");
		var from = (mdMsg.From ?? CurrentTime.Date.AddDays(-1))
			.ToUniversalTime();
		var to = (mdMsg.To ?? CurrentTime).ToUniversalTime();
		var maximum = mdMsg.Count is > 0
			? (int)Math.Min(mdMsg.Count.Value, int.MaxValue)
			: int.MaxValue;
		var trades = new List<JQuantsTrade>();
		for (var date = from.Date;
			date <= to.Date && trades.Count < maximum;
			date = date.AddDays(1))
		{
			if (date.DayOfWeek is DayOfWeek.Saturday or
				DayOfWeek.Sunday)
				continue;
			trades.AddRange(await RestClient.GetTradesAsync(
				instrument.Code, date, cancellationToken));
		}
		foreach (var trade in trades
			.Where(trade => trade.Time.UtcDateTime >= from &&
				trade.Time.UtcDateTime <= to)
			.OrderBy(static trade => trade.Time)
			.TakeLast(maximum))
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = ToSecurityId(instrument),
				TradeStringId = trade.Id,
				TradePrice = trade.Price,
				TradeVolume = trade.Volume,
				ServerTime = trade.Time.UtcDateTime,
			}, cancellationToken);
		await CompleteMarketSubscriptionAsync(mdMsg,
			cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		if (!mdMsg.IsSubscribe)
			return;
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		var instrument = ResolveInstrument(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		if (!AllTimeFrames.Contains(timeFrame))
			throw new NotSupportedException(
				$"J-Quants candle interval '{timeFrame}' is " +
					"unsupported.");
		if (instrument.Kind != JQuantsInstrumentKinds.Equity &&
			timeFrame != TimeSpan.FromDays(1))
			throw new NotSupportedException(
				"J-Quants derivatives provide daily candles only.");
		var to = (mdMsg.To ?? CurrentTime).ToUniversalTime();
		var maximum = mdMsg.Count is > 0
			? (int)Math.Min(mdMsg.Count.Value, int.MaxValue)
			: int.MaxValue;
		var from = (mdMsg.From ??
			to - timeFrame * Math.Min(maximum, 1000))
			.ToUniversalTime();
		var bars = await LoadBarsAsync(instrument, timeFrame,
			from, to, cancellationToken);
		foreach (var bar in bars
			.Where(bar => bar.Time.UtcDateTime >= from &&
				bar.Time.UtcDateTime <= to)
			.OrderBy(static bar => bar.Time)
			.TakeLast(maximum))
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = ToSecurityId(instrument),
				TypedArg = timeFrame,
				OpenTime = bar.Time.UtcDateTime,
				CloseTime = (bar.Time + timeFrame).UtcDateTime,
				OpenPrice = bar.Open,
				HighPrice = bar.High,
				LowPrice = bar.Low,
				ClosePrice = bar.Close,
				TotalVolume = bar.Volume,
				OpenInterest = bar.OpenInterest,
				State = CandleStates.Finished,
			}, cancellationToken);
		await CompleteMarketSubscriptionAsync(mdMsg,
			cancellationToken);
	}

	private async ValueTask<JQuantsBar[]> LoadBarsAsync(
		JQuantsInstrument instrument, TimeSpan timeFrame,
		DateTime from, DateTime to,
		CancellationToken cancellationToken)
	{
		if (instrument.Kind == JQuantsInstrumentKinds.Equity)
		{
			var values = timeFrame == TimeSpan.FromDays(1)
				? await RestClient.GetDailyBarsAsync(instrument.Code,
					from, to, cancellationToken)
				: await RestClient.GetMinuteBarsAsync(instrument.Code,
					from, to, cancellationToken);
			var bars = values
				.Select(value => value.ToBar(
					timeFrame != TimeSpan.FromDays(1)))
				.Where(static bar => bar.Time != default)
				.ToArray();
			return timeFrame == TimeSpan.FromDays(1)
				? bars.OrderBy(static bar => bar.Time).ToArray()
				: JQuantsExtensions.Aggregate(bars, timeFrame);
		}

		var result = new List<JQuantsBar>();
		for (var date = from.Date; date <= to.Date;
			date = date.AddDays(1))
		{
			if (date.DayOfWeek is DayOfWeek.Saturday or
				DayOfWeek.Sunday)
				continue;
			var values = instrument.Kind ==
				JQuantsInstrumentKinds.Future
					? await RestClient.GetFuturesAsync(date,
						cancellationToken)
					: await RestClient.GetOptionsAsync(
						instrument.Code, date, cancellationToken);
			result.AddRange(values
				.Where(value => value.String("Code")
					.EqualsIgnoreCase(instrument.Code))
				.Select(static value => value.ToBar(false))
				.Where(static bar => bar.Time != default));
		}
		return [.. result.OrderBy(static bar => bar.Time)];
	}

	private static SecurityMessage CreateSecurity(
		JQuantsInstrument instrument, long transactionId)
		=> new()
		{
			OriginalTransactionId = transactionId,
			SecurityId = ToSecurityId(instrument),
			Name = instrument.Name.IsEmpty(instrument.Code),
			ShortName = instrument.EnglishName
				.IsEmpty(instrument.Name)
				.IsEmpty(instrument.Code),
			Class = instrument.MarketName
				.IsEmpty(instrument.ProductCategory)
				.IsEmpty(instrument.Market),
			SecurityType = instrument.Kind switch
			{
				JQuantsInstrumentKinds.Future =>
					SecurityTypes.Future,
				JQuantsInstrumentKinds.Option =>
					SecurityTypes.Option,
				_ => SecurityTypes.Stock,
			},
			Currency = CurrencyTypes.JPY,
			UnderlyingSecurityId =
				instrument.Underlying.IsEmpty()
					? default
					: new()
					{
						SecurityCode = instrument.Underlying,
						BoardCode = BoardCodes.Tse,
					},
			Strike = instrument.Strike,
			OptionType = instrument.OptionType,
			ExpiryDate = instrument.Expiry,
		};

	private static bool Matches(JQuantsInstrument instrument,
		string requested)
		=> requested.IsEmpty() ||
			instrument.Code.Contains(requested,
				StringComparison.OrdinalIgnoreCase) ||
			instrument.Name?.Contains(requested,
				StringComparison.OrdinalIgnoreCase) == true ||
			instrument.EnglishName?.Contains(requested,
				StringComparison.OrdinalIgnoreCase) == true;

	private static DateTime PreviousWeekday(DateTime value)
	{
		while (value.DayOfWeek is DayOfWeek.Saturday or
			DayOfWeek.Sunday)
			value = value.AddDays(-1);
		return value;
	}

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message,
			cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
