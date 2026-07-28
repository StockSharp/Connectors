namespace StockSharp.Samco;

public partial class SamcoMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		await EnsureInstrumentsAsync(cancellationToken);
		var board = lookupMsg.SecurityId.BoardCode?.Trim()
			.ToUpperInvariant();
		var requested = lookupMsg.SecurityId.SecurityCode?.Trim();
		SamcoInstrument[] instruments;
		using (_sync.EnterScope())
			instruments = [.. _instrumentDetails.Values];
		var maximum = lookupMsg.Count is > 0
			? lookupMsg.Count.Value
			: long.MaxValue;
		var sent = 0L;
		foreach (var instrument in instruments)
		{
			if (!board.IsEmpty() &&
				!instrument.Exchange.EqualsIgnoreCase(board))
				continue;
			if (!requested.IsEmpty() &&
				!instrument.TradingSymbol.EqualsIgnoreCase(requested) &&
				!instrument.Name.EqualsIgnoreCase(requested) &&
				!instrument.SymbolCode.EqualsIgnoreCase(requested) &&
				instrument.TradingSymbol?.Contains(requested,
					StringComparison.OrdinalIgnoreCase) != true &&
				instrument.Name?.Contains(requested,
					StringComparison.OrdinalIgnoreCase) != true)
				continue;
			await SendSecurityAsync(instrument,
				lookupMsg.TransactionId, cancellationToken);
			if (++sent >= maximum)
				break;
		}
		await SendSubscriptionResultAsync(lookupMsg,
			cancellationToken);
	}

	private ValueTask SendSecurityAsync(SamcoInstrument instrument,
		long target, CancellationToken cancellationToken)
	{
		var securityId = instrument.ToSecurityId();
		var type = instrument.ToSecurityType();
		return SendOutMessageAsync(new SecurityMessage
		{
			OriginalTransactionId = target,
			SecurityId = securityId,
			Name = instrument.Name
				.IsEmpty(instrument.TradingSymbol)
				.IsEmpty(instrument.SymbolCode),
			ShortName = securityId.SecurityCode,
			Class = instrument.Instrument
				.IsEmpty(instrument.ExchangeSegment)
				.IsEmpty(instrument.Exchange),
			SecurityType = type,
			Currency = CurrencyTypes.INR,
			PriceStep = instrument.Tick > 0
				? instrument.Tick
				: null,
			VolumeStep = instrument.Lot > 0
				? instrument.Lot
				: 1,
			MinVolume = instrument.Lot > 0
				? instrument.Lot
				: null,
			ExpiryDate = instrument.ToExpiry(),
			OptionType = instrument.ToOptionType(),
			Strike = instrument.Strike,
		}, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			SamcoInstrumentRef removed;
			using (_sync.EnterScope())
			{
				if (!_level1Subscriptions.Remove(
					mdMsg.OriginalTransactionId, out removed))
					return;
			}
			await UnsubscribeFeedAsync(removed, cancellationToken);
			return;
		}
		var instrument = await ResolveInstrumentAsync(mdMsg.SecurityId,
			cancellationToken);
		await SendQuoteSnapshotAsync(instrument, mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_level1Subscriptions[mdMsg.TransactionId] = instrument;
		await SubscribeFeedAsync(instrument, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			SamcoInstrumentRef removed;
			using (_sync.EnterScope())
			{
				if (!_depthSubscriptions.Remove(
					mdMsg.OriginalTransactionId, out removed))
					return;
			}
			await UnsubscribeFeedAsync(removed, cancellationToken);
			return;
		}
		var instrument = await ResolveInstrumentAsync(mdMsg.SecurityId,
			cancellationToken);
		await SendDepthSnapshotAsync(instrument, mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_depthSubscriptions[mdMsg.TransactionId] = instrument;
		await SubscribeFeedAsync(instrument, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			SamcoInstrumentRef removed;
			using (_sync.EnterScope())
			{
				if (!_tickSubscriptions.Remove(
					mdMsg.OriginalTransactionId, out removed))
					return;
			}
			await UnsubscribeFeedAsync(removed, cancellationToken);
			return;
		}
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		var instrument = await ResolveInstrumentAsync(mdMsg.SecurityId,
			cancellationToken);
		using (_sync.EnterScope())
			_tickSubscriptions[mdMsg.TransactionId] = instrument;
		await SubscribeFeedAsync(instrument, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_candleSubscriptions.Remove(
					mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		var instrument = await ResolveInstrumentAsync(mdMsg.SecurityId,
			cancellationToken);
		var timeFrame = mdMsg.GetTimeFrame();
		_ = timeFrame.ToSamcoInterval();
		var from = mdMsg.From?.ToUniversalTime() ??
			CurrentTime.AddDays(timeFrame == TimeSpan.FromDays(1)
				? -30
				: -1);
		var to = mdMsg.To?.ToUniversalTime() ?? CurrentTime;
		var candles = await LoadCandlesAsync(instrument, timeFrame,
			from, to, cancellationToken);
		var count = mdMsg.Count is > 0
			? (int)Math.Min(mdMsg.Count.Value, int.MaxValue)
			: int.MaxValue;
		var selected = candles.TakeLast(count).ToArray();
		foreach (var candle in selected)
			await SendCandleAsync(candle, instrument, timeFrame,
				mdMsg.TransactionId,
				candle.Time + timeFrame <= CurrentTime
					? CandleStates.Finished
					: CandleStates.Active,
				cancellationToken);
		if (mdMsg.IsHistoryOnly() ||
			mdMsg.To is DateTime end &&
			end.ToUniversalTime() <= CurrentTime)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_candleSubscriptions[mdMsg.TransactionId] = new()
			{
				Instrument = instrument,
				TimeFrame = timeFrame,
				LastTime = selected.LastOrDefault()?.Time.UtcDateTime ??
					from,
			};
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask SendQuoteSnapshotAsync(
		SamcoInstrumentRef instrument, long target,
		CancellationToken cancellationToken)
	{
		var quote = await RestClient.GetQuoteAsync(instrument,
			cancellationToken);
		var feed = quote.ToSamcoFeed();
		if (feed is not null)
			await SendLevel1Async(instrument, feed, target,
				cancellationToken);
	}

	private async ValueTask SendDepthSnapshotAsync(
		SamcoInstrumentRef instrument, long target,
		CancellationToken cancellationToken)
	{
		var depth = await RestClient.GetDepthAsync(instrument,
			cancellationToken);
		var feed = depth.ToSamcoFeed();
		if (feed is not null)
			await SendDepthAsync(instrument, feed.Bids, feed.Asks,
				feed.Time, target, cancellationToken);
	}

	private async ValueTask ProcessFeedAsync(
		SamcoInstrumentRef instrument, SamcoFeed feed,
		CancellationToken cancellationToken)
	{
		foreach (var target in FindTargets(_level1Subscriptions,
			instrument))
			await SendLevel1Async(target.Instrument, feed, target.Id,
				cancellationToken);
		foreach (var target in FindTargets(_depthSubscriptions,
			instrument))
			await SendDepthAsync(target.Instrument, feed.Bids,
				feed.Asks, feed.Time, target.Id, cancellationToken);

		var tickTargets = FindTargets(_tickSubscriptions, instrument);
		if (tickTargets.Length == 0 || feed.LastPrice <= 0 ||
			feed.LastVolume <= 0)
			return;
		var time = feed.LastTradeTime == default
			? feed.Time
			: feed.LastTradeTime;
		var isNew = false;
		using (_sync.EnterScope())
		{
			var signature = (time, feed.LastPrice, feed.LastVolume);
			if (!_lastTicks.TryGetValue(instrument.Key,
				out var previous) || previous != signature)
			{
				_lastTicks[instrument.Key] = signature;
				isNew = true;
			}
		}
		if (!isNew)
			return;
		foreach (var target in tickTargets)
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				OriginalTransactionId = target.Id,
				SecurityId = ToSecurityId(target.Instrument),
				TradePrice = feed.LastPrice,
				TradeVolume = feed.LastVolume,
				ServerTime = time.UtcDateTime,
			}, cancellationToken);
	}

	private ValueTask SendLevel1Async(SamcoInstrumentRef instrument,
		SamcoFeed value, long target,
		CancellationToken cancellationToken)
	{
		var bid = value.Bids?.FirstOrDefault() ?? default;
		var ask = value.Asks?.FirstOrDefault() ?? default;
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = target,
			SecurityId = ToSecurityId(instrument),
			ServerTime = (value.Time == default
				? DateTimeOffset.UtcNow
				: value.Time).UtcDateTime,
		}
		.TryAdd(Level1Fields.LastTradePrice, value.LastPrice, true)
		.TryAdd(Level1Fields.LastTradeVolume, value.LastVolume, true)
		.TryAdd(Level1Fields.Volume, value.Volume, true)
		.TryAdd(Level1Fields.OpenPrice, value.Open, true)
		.TryAdd(Level1Fields.HighPrice, value.High, true)
		.TryAdd(Level1Fields.LowPrice, value.Low, true)
		.TryAdd(Level1Fields.ClosePrice, value.Close, true)
		.TryAdd(Level1Fields.AveragePrice, value.AveragePrice, true)
		.TryAdd(Level1Fields.BestBidPrice, bid.Price, true)
		.TryAdd(Level1Fields.BestBidVolume, bid.Volume, true)
		.TryAdd(Level1Fields.BestAskPrice, ask.Price, true)
		.TryAdd(Level1Fields.BestAskVolume, ask.Volume, true)
		.TryAdd(Level1Fields.BidsVolume, value.TotalBidVolume, true)
		.TryAdd(Level1Fields.AsksVolume, value.TotalAskVolume, true)
		.TryAdd(Level1Fields.OpenInterest, value.OpenInterest, true)
		.TryAdd(Level1Fields.MinPrice, value.LowerLimit, true)
		.TryAdd(Level1Fields.MaxPrice, value.UpperLimit, true)
		.TryAdd(Level1Fields.HighPrice52Week, value.YearHigh, true)
		.TryAdd(Level1Fields.LowPrice52Week, value.YearLow, true),
			cancellationToken);
	}

	private ValueTask SendDepthAsync(SamcoInstrumentRef instrument,
		SamcoDepthLevel[] bids, SamcoDepthLevel[] asks,
		DateTimeOffset time, long target,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = target,
			SecurityId = ToSecurityId(instrument),
			ServerTime = (time == default
				? DateTimeOffset.UtcNow
				: time).UtcDateTime,
			Bids = bids?.Select(static value =>
				new QuoteChange(value.Price, value.Volume)
				{
					OrdersCount = value.Orders,
				}).ToArray() ?? [],
			Asks = asks?.Select(static value =>
				new QuoteChange(value.Price, value.Volume)
				{
					OrdersCount = value.Orders,
				}).ToArray() ?? [],
			State = QuoteChangeStates.SnapshotComplete,
		}, cancellationToken);

	private async ValueTask<SamcoCandle[]> LoadCandlesAsync(
		SamcoInstrumentRef instrument, TimeSpan timeFrame,
		DateTime from, DateTime to,
		CancellationToken cancellationToken)
	{
		var response = await RestClient.GetCandlesAsync(instrument,
			timeFrame, from, to, cancellationToken);
		var name = timeFrame == TimeSpan.FromDays(1)
			? "historicalCandleData"
			: "intradayCandleData";
		return response.Get(name) is JArray values
			? values.OfType<JObject>()
				.Select(value => new SamcoCandle
				{
					Time = value.Get("dateTime", "date")
						.ToSamcoTime(default),
					Open = value.Decimal("open") ?? 0,
					High = value.Decimal("high") ?? 0,
					Low = value.Decimal("low") ?? 0,
					Close = value.Decimal("close") ?? 0,
					Volume = value.Decimal("volume") ?? 0,
				})
				.Where(static candle => candle.Time != default)
				.OrderBy(static candle => candle.Time)
				.ToArray()
			: [];
	}

	private ValueTask SendCandleAsync(SamcoCandle candle,
		SamcoInstrumentRef instrument, TimeSpan timeFrame,
		long target, CandleStates state,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OriginalTransactionId = target,
			SecurityId = ToSecurityId(instrument),
			OpenTime = candle.Time.UtcDateTime,
			CloseTime = (candle.Time + timeFrame).UtcDateTime,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.Volume,
			TypedArg = timeFrame,
			State = state,
		}, cancellationToken);

	private async ValueTask PollCandlesAsync(
		CancellationToken cancellationToken)
	{
		(long Id, CandleSubscription Value)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions =
			[
				.. _candleSubscriptions.Select(static pair =>
					(pair.Key, pair.Value))
			];
		foreach (var subscription in subscriptions)
		{
			var from = subscription.Value.LastTime == default
				? CurrentTime.Subtract(
					subscription.Value.TimeFrame * 2)
				: subscription.Value.LastTime;
			var candles = await LoadCandlesAsync(
				subscription.Value.Instrument,
				subscription.Value.TimeFrame, from, CurrentTime,
				cancellationToken);
			foreach (var candle in candles.Where(candle =>
				candle.Time.UtcDateTime >=
					subscription.Value.LastTime))
				await SendCandleAsync(candle,
					subscription.Value.Instrument,
					subscription.Value.TimeFrame, subscription.Id,
					candle.Time + subscription.Value.TimeFrame <=
						CurrentTime
							? CandleStates.Finished
							: CandleStates.Active,
					cancellationToken);
			var last = candles.LastOrDefault();
			if (last is not null)
				subscription.Value.LastTime = last.Time.UtcDateTime;
		}
	}

	private async ValueTask PollMarketDataAsync(
		CancellationToken cancellationToken)
	{
		SamcoInstrumentRef[] instruments;
		using (_sync.EnterScope())
			instruments = _level1Subscriptions.Values
				.Concat(_depthSubscriptions.Values)
				.Concat(_tickSubscriptions.Values)
				.GroupBy(static value => value.Key,
					StringComparer.OrdinalIgnoreCase)
				.Select(static group => group.First())
				.ToArray();
		foreach (var instrument in instruments)
		{
			var quote = await RestClient.GetQuoteAsync(instrument,
				cancellationToken);
			var feed = quote.ToSamcoFeed();
			if (feed is null)
				continue;
			if (_depthSubscriptions.Values.Any(value =>
				value.Key.EqualsIgnoreCase(instrument.Key)))
			{
				var depth = await RestClient.GetDepthAsync(instrument,
					cancellationToken);
				var depthFeed = depth.ToSamcoFeed();
				if (depthFeed is not null)
					feed = depthFeed;
			}
			await ProcessFeedAsync(instrument, feed,
				cancellationToken);
		}
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
