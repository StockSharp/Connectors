namespace StockSharp.MStock;

public partial class MStockMessageAdapter
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
		if (!board.IsEmpty() &&
			!AssociatedBoards.Any(board.EqualsIgnoreCase))
			throw new InvalidOperationException(
				$"Board '{board}' is not associated with m.Stock.");
		var requested = lookupMsg.SecurityId.SecurityCode?.Trim();
		MStockInstrument[] instruments;
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
				!instrument.Symbol.EqualsIgnoreCase(requested) &&
				!instrument.Token.EqualsIgnoreCase(requested) &&
				instrument.TradingSymbol?.Contains(requested,
					StringComparison.OrdinalIgnoreCase) != true &&
				instrument.Symbol?.Contains(requested,
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

	private ValueTask SendSecurityAsync(MStockInstrument instrument,
		long target, CancellationToken cancellationToken)
	{
		var securityId = instrument.ToSecurityId();
		var type = instrument.ToSecurityType();
		var expiry = instrument.ToExpiry();
		return SendOutMessageAsync(new SecurityMessage
		{
			OriginalTransactionId = target,
			SecurityId = securityId,
			Name = instrument.Symbol
				.IsEmpty(instrument.TradingSymbol)
				.IsEmpty(instrument.Token),
			ShortName = securityId.SecurityCode,
			Class = instrument.InstrumentType
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
			ExpiryDate = expiry,
			OptionType = instrument.ToOptionType(),
			Strike = instrument.StrikePrice,
			UnderlyingSecurityId =
				type is SecurityTypes.Option or SecurityTypes.Future &&
				!instrument.Symbol.IsEmpty()
					? new()
					{
						SecurityCode = instrument.Symbol,
						BoardCode = instrument.Exchange,
					}
					: default,
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
			MStockInstrumentRef removed;
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
		try
		{
			await SubscribeFeedAsync(instrument, cancellationToken);
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
			MStockInstrumentRef removed;
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
		try
		{
			await SubscribeFeedAsync(instrument, cancellationToken);
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
			MStockInstrumentRef removed;
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
		try
		{
			await SubscribeFeedAsync(instrument, cancellationToken);
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
		_ = timeFrame.ToMStockInterval();
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
		MStockInstrumentRef instrument, long target,
		CancellationToken cancellationToken)
	{
		var response = await RestClient.GetQuotesAsync([instrument],
			cancellationToken);
		var quote = response.ToMStockObjects().FirstOrDefault();
		if (quote is not null)
			await SendLevel1Async(instrument, ToFeed(quote),
				target, cancellationToken);
	}

	private async ValueTask SendDepthSnapshotAsync(
		MStockInstrumentRef instrument, long target,
		CancellationToken cancellationToken)
	{
		var response = await RestClient.GetQuotesAsync([instrument],
			cancellationToken);
		var quote = response.ToMStockObjects().FirstOrDefault();
		if (quote is null)
			return;
		var feed = ToFeed(quote);
		await SendDepthAsync(instrument, feed.Bids, feed.Asks,
			feed.Time, target, cancellationToken);
	}

	private static MStockFeed ToFeed(JObject value)
	{
		var depth = value.Get("depth", "marketDepth") as JObject;
		var bids = ToDepthLevels(depth?.Get("buy", "bids") as JArray);
		var asks = ToDepthLevels(depth?.Get("sell", "asks") as JArray);
		if (bids.Length == 0)
		{
			var price = value.Decimal("bestBidPrice",
				"bestBid");
			var volume = value.Decimal("bestBidQuantity",
				"bestBidQty");
			if (price is > 0 && volume is > 0)
				bids = [new(price.Value, volume.Value, 0)];
		}
		if (asks.Length == 0)
		{
			var price = value.Decimal("bestAskPrice",
				"bestAsk");
			var volume = value.Decimal("bestAskQuantity",
				"bestAskQty");
			if (price is > 0 && volume is > 0)
				asks = [new(price.Value, volume.Value, 0)];
		}
		return new()
		{
			Mode = 3,
			Exchange = value.String("exchange"),
			Token = value.String("symbolToken", "symboltoken"),
			Time = value.Get("exchangeTimestamp", "lastTradeTime",
				"timestamp").ToMStockTime(DateTimeOffset.UtcNow),
			LastTradeTime = value.Get("lastTradeTime",
				"exchangeTimestamp", "timestamp")
					.ToMStockTime(DateTimeOffset.UtcNow),
			LastPrice = value.Decimal("ltp", "lastTradedPrice") ?? 0,
			LastVolume = value.Decimal("lastTradeQty",
				"lastTradedQuantity") ?? 0,
			AveragePrice = value.Decimal("avgPrice",
				"averagePrice") ?? 0,
			Volume = value.Decimal("tradeVolume", "volume",
				"volumeTradeForTheDay") ?? 0,
			TotalBidVolume = value.Decimal("totalBuyQty",
				"totalBuyQuantity") ?? bids.Sum(
					static level => level.Volume),
			TotalAskVolume = value.Decimal("totalSellQty",
				"totalSellQuantity") ?? asks.Sum(
					static level => level.Volume),
			Open = value.Decimal("open") ?? 0,
			High = value.Decimal("high") ?? 0,
			Low = value.Decimal("low") ?? 0,
			Close = value.Decimal("close") ?? 0,
			OpenInterest = value.Decimal("opnInterest",
				"openInterest") ?? 0,
			OpenInterestChange = value.Decimal(
				"netChangeOpenInterest", "openInterestChange") ?? 0,
			UpperLimit = value.Decimal("upperCircuit",
				"upperCircuitLimit") ?? 0,
			LowerLimit = value.Decimal("lowerCircuit",
				"lowerCircuitLimit") ?? 0,
			YearHigh = value.Decimal("fiftyTwoWeekHigh",
				"52WeekHigh") ?? 0,
			YearLow = value.Decimal("fiftyTwoWeekLow",
				"52WeekLow") ?? 0,
			Bids = bids,
			Asks = asks,
		};
	}

	private static MStockDepthLevel[] ToDepthLevels(JArray values)
		=> values?.OfType<JObject>()
			.Select(static value => new MStockDepthLevel(
				value.Decimal("price") ?? 0,
				value.Decimal("quantity", "qty") ?? 0,
				(int)(value.Decimal("orders", "numberOfOrders") ??
					0)))
			.Where(static value =>
				value.Price > 0 && value.Volume > 0)
			.Take(5)
			.ToArray() ?? [];

	private async ValueTask ProcessFeedAsync(
		MStockInstrumentRef instrument, MStockFeed feed,
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
		var key = NativeKey(instrument.Exchange, instrument.Token);
		var time = feed.LastTradeTime == default
			? feed.Time
			: feed.LastTradeTime;
		var isNew = false;
		using (_sync.EnterScope())
		{
			var signature = (time, feed.LastPrice, feed.LastVolume);
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
				ServerTime = time.UtcDateTime,
			}, cancellationToken);
	}

	private ValueTask SendLevel1Async(MStockInstrumentRef instrument,
		MStockFeed value, long target,
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

	private ValueTask SendDepthAsync(MStockInstrumentRef instrument,
		MStockDepthLevel[] bids, MStockDepthLevel[] asks,
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

	private async ValueTask<MStockCandle[]> LoadCandlesAsync(
		MStockInstrumentRef instrument, TimeSpan timeFrame,
		DateTime from, DateTime to,
		CancellationToken cancellationToken)
	{
		var response = await RestClient.GetCandlesAsync(instrument,
			timeFrame, from, to, cancellationToken);
		var data = response.UnwrapMStockData();
		var values = data is JObject obj
			? obj.Get("candles") as JArray
			: data as JArray;
		return values?.OfType<JArray>()
			.Select(ToCandle)
			.Where(static candle => candle is not null)
			.OrderBy(static candle => candle.Time)
			.ToArray() ?? [];
	}

	private static MStockCandle ToCandle(JArray value)
	{
		if (value.Count < 6)
			return null;
		var time = value[0].ToMStockTime(default);
		if (time == default)
			return null;
		return new()
		{
			Time = time,
			Open = value[1].ToMStockDecimal() ?? 0,
			High = value[2].ToMStockDecimal() ?? 0,
			Low = value[3].ToMStockDecimal() ?? 0,
			Close = value[4].ToMStockDecimal() ?? 0,
			Volume = value[5].ToMStockDecimal() ?? 0,
		};
	}

	private ValueTask SendCandleAsync(MStockCandle candle,
		MStockInstrumentRef instrument, TimeSpan timeFrame,
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
		MStockInstrumentRef[] instruments;
		using (_sync.EnterScope())
			instruments = _level1Subscriptions.Values
				.Concat(_depthSubscriptions.Values)
				.Concat(_tickSubscriptions.Values)
				.GroupBy(static value => value.Key,
					StringComparer.OrdinalIgnoreCase)
				.Select(static group => group.First())
				.ToArray();

		foreach (var batch in instruments.Chunk(50))
		{
			var response = await RestClient.GetQuotesAsync(batch,
				cancellationToken);

			foreach (var quote in response.ToMStockObjects())
			{
				var exchange = quote.String("exchange");
				var token = quote.String(
					"symbolToken", "symboltoken");
				MStockInstrumentRef instrument = default;
				if (!exchange.IsEmpty() && !token.IsEmpty())
					instrument = batch.FirstOrDefault(value =>
						value.Exchange.EqualsIgnoreCase(exchange) &&
						value.Token.EqualsIgnoreCase(token));
				if (instrument.Token.IsEmpty())
				{
					var symbol = quote.String(
						"tradingSymbol", "tradingsymbol");
					instrument = batch.FirstOrDefault(value =>
						value.TradingSymbol.EqualsIgnoreCase(symbol));
				}
				if (instrument.Token.IsEmpty())
					continue;
				await ProcessFeedAsync(instrument, ToFeed(quote),
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
