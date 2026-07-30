namespace StockSharp.IIFL;

public partial class IIFLMessageAdapter
{
	private sealed class IIFLCandle
	{
		public DateTimeOffset Time { get; init; }
		public decimal Open { get; init; }
		public decimal High { get; init; }
		public decimal Low { get; init; }
		public decimal Close { get; init; }
		public decimal Volume { get; init; }
	}

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);

		EnsureConnected();
		var board = lookupMsg.SecurityId.BoardCode?.Trim()
			.ToUpperInvariant();
		var requested = lookupMsg.SecurityId.SecurityCode?.Trim();
		string[] exchanges;
		if (board.IsEmpty())
			exchanges = IIFLExtensions.Exchanges;
		else
		{
			if (!AssociatedBoards.Any(board.EqualsIgnoreCase))
				throw new InvalidOperationException(
					$"Board '{board}' is not associated with IIFL.");
			var native = board.ToIIFLExchange();
			exchanges = board is "NSE" or "BSE"
				? [native, "INDICES"]
				: [native];
		}

		foreach (var exchange in exchanges)
			await LoadExchangeAsync(exchange, cancellationToken);

		IIFLInstrument[] instruments;
		using (_sync.EnterScope())
			instruments = _instrumentDetails.Values
				.DistinctBy(static instrument =>
					NativeKey(instrument.Exchange,
						instrument.InstrumentId))
				.ToArray();

		var maximum = lookupMsg.Count is > 0
			? lookupMsg.Count.Value
			: long.MaxValue;
		var sent = 0L;

		foreach (var instrument in instruments)
		{
			var securityId = instrument.ToSecurityId();
			if (!board.IsEmpty() &&
				!securityId.BoardCode.EqualsIgnoreCase(board))
				continue;
			if (!requested.IsEmpty() &&
				!securityId.SecurityCode.EqualsIgnoreCase(requested) &&
				!instrument.InstrumentId.EqualsIgnoreCase(requested) &&
				instrument.Name?.Contains(requested,
					StringComparison.OrdinalIgnoreCase) != true)
				continue;
			await SendSecurityAsync(instrument,
				lookupMsg.TransactionId, cancellationToken);
			if (++sent >= maximum)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private ValueTask SendSecurityAsync(IIFLInstrument instrument,
		long target, CancellationToken cancellationToken)
	{
		var securityId = instrument.ToSecurityId();
		DateTime? expiry = null;
		if (!instrument.Expiry.IsEmpty())
		{
			var parsed = new JValue(instrument.Expiry)
				.ToIIFLTime(default);
			if (parsed != default)
				expiry = parsed.UtcDateTime;
		}
		return SendOutMessageAsync(new SecurityMessage
		{
			OriginalTransactionId = target,
			SecurityId = securityId,
			Name = instrument.Name
				.IsEmpty(instrument.UnderlyingName)
				.IsEmpty(securityId.SecurityCode),
			ShortName = securityId.SecurityCode,
			Class = instrument.Series
				.IsEmpty(instrument.Exchange),
			SecurityType = instrument.ToSecurityType(),
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
			ExpiryDate = expiry,
			OptionType = instrument.OptionType.ToOptionType(),
			Strike = instrument.Strike,
			UnderlyingSecurityId =
				instrument.UnderlyingSymbol.IsEmpty()
					? default
					: new()
					{
						SecurityCode = instrument.UnderlyingSymbol,
						BoardCode = securityId.BoardCode,
					},
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
			IIFLInstrumentRef removed;
			using (_sync.EnterScope())
			{
				if (!_level1Subscriptions.Remove(
					mdMsg.OriginalTransactionId, out removed))
					return;
			}
			await UnsubscribeFeedAsync(removed,
				IsDerivative(removed), cancellationToken);
			return;
		}
		var instrument = await ResolveInstrumentAsync(mdMsg.SecurityId,
			cancellationToken);
		await SendQuoteSnapshotAsync(instrument, mdMsg.TransactionId,
			true, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}

		using (_sync.EnterScope())
			_level1Subscriptions[mdMsg.TransactionId] = instrument;
		try
		{
			await SubscribeFeedAsync(instrument,
				IsDerivative(instrument), cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_level1Subscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
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
			IIFLInstrumentRef removed;
			using (_sync.EnterScope())
			{
				if (!_depthSubscriptions.Remove(
					mdMsg.OriginalTransactionId, out removed))
					return;
			}
			await UnsubscribeFeedAsync(removed, false,
				cancellationToken);
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
		try
		{
			await SubscribeFeedAsync(instrument, false,
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_depthSubscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
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
			IIFLInstrumentRef removed;
			using (_sync.EnterScope())
			{
				if (!_tickSubscriptions.Remove(
					mdMsg.OriginalTransactionId, out removed))
					return;
			}
			await UnsubscribeFeedAsync(removed, false,
				cancellationToken);
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
		try
		{
			await SubscribeFeedAsync(instrument, false,
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_tickSubscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
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
		_ = timeFrame.ToIIFLInterval();
		var from = mdMsg.From?.ToUniversalTime() ??
			CurrentTime.AddDays(-1);
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
		IIFLInstrumentRef instrument, long target, bool openInterest,
		CancellationToken cancellationToken)
	{
		var response = await RestClient.GetQuotesAsync([instrument],
			cancellationToken);
		var quote = response.ToIIFLObjects().FirstOrDefault();
		if (quote is not null)
			await SendLevel1Async(instrument, ToMarketFeed(quote),
				target, cancellationToken);
		if (openInterest && IsDerivative(instrument))
		{
			var interest = await RestClient.GetOpenInterestAsync(
				instrument, cancellationToken);
			var value = interest.UnwrapIIFLResult() as JObject ??
				interest.ToIIFLObjects().FirstOrDefault();
			if (value is not null)
				await SendOpenInterestAsync(instrument,
					new(
						value.FindIIFLDecimal("openInterest") ?? 0,
						value.FindIIFLDecimal("dayHighOi") ?? 0,
						value.FindIIFLDecimal("dayLowOi") ?? 0,
						value.FindIIFLDecimal("previousOi") ?? 0),
					target, cancellationToken);
		}
	}

	private async ValueTask SendDepthSnapshotAsync(
		IIFLInstrumentRef instrument, long target,
		CancellationToken cancellationToken)
	{
		var response = await RestClient.GetDepthAsync(instrument,
			cancellationToken);
		var value = response.UnwrapIIFLResult() as JObject ??
			response.ToIIFLObjects().FirstOrDefault();
		if (value is null)
			return;
		var depth = value.FindIIFL("marketDepth") as JObject ?? value;
		var bids = ToDepthLevels(depth.FindIIFL("bids") as JArray);
		var asks = ToDepthLevels(depth.FindIIFL("asks") as JArray);
		await SendDepthAsync(instrument, bids, asks, CurrentTime,
			target, cancellationToken);
	}

	private static IIFLDepthLevel[] ToDepthLevels(JArray values)
		=> values?.OfType<JObject>()
			.Select(static value => new IIFLDepthLevel(
				value.FindIIFLDecimal("price") ?? 0,
				value.FindIIFLDecimal("quantity") ?? 0,
				(int)(value.FindIIFLDecimal("orders") ?? 0)))
			.Where(static value =>
				value.Price > 0 && value.Volume > 0)
			.ToArray() ?? [];

	private static IIFLMarketFeed ToMarketFeed(JObject value)
	{
		var time = value.FindIIFL("tickTimestamp", "timestamp")
			.ToIIFLTime(DateTimeOffset.UtcNow);
		var bidPrice = value.FindIIFLDecimal("bestBidPrice") ?? 0;
		var bidVolume = value.FindIIFLDecimal(
			"bestBidQuantity") ?? 0;
		var askPrice = value.FindIIFLDecimal("bestAskPrice",
			"besAskPrice") ?? 0;
		var askVolume = value.FindIIFLDecimal(
			"bestAskQuantity", "besAskQuantity") ?? 0;
		return new()
		{
			LastPrice = value.FindIIFLDecimal("ltp") ?? 0,
			LastVolume = value.FindIIFLDecimal(
				"lastTradedQuantity") ?? 0,
			Volume = value.FindIIFLDecimal("tradedVolume") ?? 0,
			Open = value.FindIIFLDecimal("open") ?? 0,
			High = value.FindIIFLDecimal("high") ?? 0,
			Low = value.FindIIFLDecimal("low") ?? 0,
			Close = value.FindIIFLDecimal("close") ?? 0,
			AveragePrice = value.FindIIFLDecimal(
				"averageTradedPrice") ?? 0,
			BestBidPrice = bidPrice,
			BestBidVolume = bidVolume,
			BestAskPrice = askPrice,
			BestAskVolume = askVolume,
			TotalBidVolume = value.FindIIFLDecimal(
				"totalBidQuantity") ?? 0,
			TotalAskVolume = value.FindIIFLDecimal(
				"totalAskQuantity") ?? 0,
			Time = time,
			Bids = bidPrice > 0 && bidVolume > 0
				? [new(bidPrice, bidVolume, 0)]
				: [],
			Asks = askPrice > 0 && askVolume > 0
				? [new(askPrice, askVolume, 0)]
				: [],
		};
	}

	private async ValueTask ProcessMarketFeedAsync(
		IIFLInstrumentRef instrument, IIFLMarketFeed feed,
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
		if (tickTargets.Length == 0 ||
			feed.LastPrice <= 0 || feed.LastVolume <= 0)
			return;
		var key = NativeKey(instrument.Exchange,
			instrument.InstrumentId);
		var isNew = false;
		using (_sync.EnterScope())
		{
			var signature = (feed.Time, feed.LastPrice,
				feed.LastVolume);
			if (!_lastTicks.TryGetValue(key, out var previous) ||
				previous != signature)
			{
				_lastTicks[key] = signature;
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
				ServerTime = feed.Time.UtcDateTime,
			}, cancellationToken);
	}

	private async ValueTask ProcessOpenInterestAsync(
		IIFLInstrumentRef instrument, IIFLOpenInterest value,
		CancellationToken cancellationToken)
	{
		foreach (var target in FindTargets(_level1Subscriptions,
			instrument))
			await SendOpenInterestAsync(target.Instrument, value,
				target.Id, cancellationToken);
	}

	private ValueTask SendLevel1Async(IIFLInstrumentRef instrument,
		IIFLMarketFeed value, long target,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = target,
			SecurityId = ToSecurityId(instrument),
			ServerTime = value.Time.UtcDateTime,
		}
		.TryAdd(Level1Fields.LastTradePrice, value.LastPrice, true)
		.TryAdd(Level1Fields.LastTradeVolume, value.LastVolume, true)
		.TryAdd(Level1Fields.Volume, value.Volume, true)
		.TryAdd(Level1Fields.OpenPrice, value.Open, true)
		.TryAdd(Level1Fields.HighPrice, value.High, true)
		.TryAdd(Level1Fields.LowPrice, value.Low, true)
		.TryAdd(Level1Fields.ClosePrice, value.Close, true)
		.TryAdd(Level1Fields.AveragePrice, value.AveragePrice, true)
		.TryAdd(Level1Fields.BestBidPrice, value.BestBidPrice, true)
		.TryAdd(Level1Fields.BestBidVolume, value.BestBidVolume, true)
		.TryAdd(Level1Fields.BestAskPrice, value.BestAskPrice, true)
		.TryAdd(Level1Fields.BestAskVolume, value.BestAskVolume, true)
		.TryAdd(Level1Fields.BidsVolume, value.TotalBidVolume, true)
		.TryAdd(Level1Fields.AsksVolume, value.TotalAskVolume, true),
			cancellationToken);

	private ValueTask SendOpenInterestAsync(
		IIFLInstrumentRef instrument, IIFLOpenInterest value,
		long target, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = target,
			SecurityId = ToSecurityId(instrument),
			ServerTime = CurrentTime,
		}
		.TryAdd(Level1Fields.OpenInterest, value.Current, true),
			cancellationToken);

	private ValueTask SendDepthAsync(IIFLInstrumentRef instrument,
		IIFLDepthLevel[] bids, IIFLDepthLevel[] asks,
		DateTimeOffset time, long target,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			OriginalTransactionId = target,
			SecurityId = ToSecurityId(instrument),
			ServerTime = time.UtcDateTime,
			Bids = bids.Select(static value =>
				new QuoteChange(value.Price, value.Volume)).ToArray(),
			Asks = asks.Select(static value =>
				new QuoteChange(value.Price, value.Volume)).ToArray(),
			State = QuoteChangeStates.SnapshotComplete,
		}, cancellationToken);

	private async ValueTask<IIFLCandle[]> LoadCandlesAsync(
		IIFLInstrumentRef instrument, TimeSpan timeFrame,
		DateTime from, DateTime to,
		CancellationToken cancellationToken)
	{
		var response = await RestClient.GetCandlesAsync(instrument,
			timeFrame, from, to, cancellationToken);
		return ExtractCandleObjects(response)
			.Select(ToCandle)
			.Where(static value => value is not null)
			.OrderBy(static value => value.Time)
			.ToArray();
	}

	private static IEnumerable<JObject> ExtractCandleObjects(
		JToken value)
	{
		if (value is JObject obj)
		{
			if (obj.FindIIFL("initialTimestamp", "timestamp",
				"time") is not null)
				yield return obj;

			foreach (var property in obj.Properties())
				foreach (var nested in ExtractCandleObjects(
					property.Value))
					yield return nested;
		}
		else if (value is JArray array)
		{
			foreach (var item in array)
				foreach (var nested in ExtractCandleObjects(item))
					yield return nested;
		}
	}

	private static IIFLCandle ToCandle(JObject value)
	{
		var time = value.FindIIFL("initialTimestamp", "timestamp",
			"time").ToIIFLTime(default);
		if (time == default)
			return null;
		return new()
		{
			Time = time,
			Open = value.FindIIFLDecimal("open") ?? 0,
			High = value.FindIIFLDecimal("high") ?? 0,
			Low = value.FindIIFLDecimal("low") ?? 0,
			Close = value.FindIIFLDecimal("close") ?? 0,
			Volume = value.FindIIFLDecimal("volume") ?? 0,
		};
	}

	private ValueTask SendCandleAsync(IIFLCandle candle,
		IIFLInstrumentRef instrument, TimeSpan timeFrame, long target,
		CandleStates state, CancellationToken cancellationToken)
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
		IIFLInstrumentRef[] instruments;
		IIFLInstrumentRef[] depth;
		using (_sync.EnterScope())
		{
			instruments = _level1Subscriptions.Values
				.Concat(_tickSubscriptions.Values)
				.GroupBy(static value =>
					NativeKey(value.Exchange, value.InstrumentId))
				.Select(static group => group.First())
				.ToArray();
			depth = _depthSubscriptions.Values
				.GroupBy(static value =>
					NativeKey(value.Exchange, value.InstrumentId))
				.Select(static group => group.First())
				.ToArray();
		}

		foreach (var batch in instruments.Chunk(100))
		{
			var response = await RestClient.GetQuotesAsync(batch,
				cancellationToken);

			foreach (var quote in response.ToIIFLObjects())
			{
				var exchange = quote.FindIIFLString("exchange");
				var instrumentId = quote.FindIIFLString(
					"instrumentId");
				if (exchange.IsEmpty() || instrumentId.IsEmpty())
					continue;
				var instrument = instruments.FirstOrDefault(value =>
					value.Exchange.EqualsIgnoreCase(exchange) &&
					value.InstrumentId.EqualsIgnoreCase(instrumentId));
				if (instrument.InstrumentId.IsEmpty())
					continue;
				await ProcessMarketFeedAsync(instrument,
					ToMarketFeed(quote), cancellationToken);
			}
		}

		foreach (var instrument in depth)
		{
			var targets = FindTargets(_depthSubscriptions, instrument);

			foreach (var target in targets.Take(1))
			{
				var response = await RestClient.GetDepthAsync(
					instrument, cancellationToken);
				var value = response.UnwrapIIFLResult() as JObject ??
					response.ToIIFLObjects().FirstOrDefault();
				if (value is null)
					continue;
				var marketDepth =
					value.FindIIFL("marketDepth") as JObject ?? value;
				var bids = ToDepthLevels(
					marketDepth.FindIIFL("bids") as JArray);
				var asks = ToDepthLevels(
					marketDepth.FindIIFL("asks") as JArray);

				foreach (var depthTarget in targets)
					await SendDepthAsync(depthTarget.Instrument,
						bids, asks, CurrentTime, depthTarget.Id,
						cancellationToken);
			}
		}
	}

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
