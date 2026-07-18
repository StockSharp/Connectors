namespace StockSharp.BloFin;

public partial class BloFinMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var instrument in (await RestClient.GetInstrumentsAsync(null, cancellationToken) ?? [])
			.Where(static item => item?.InstrumentId.IsEmpty() == false)
			.OrderBy(static item => item.InstrumentId))
		{
			if (instrument.State != BloFinInstrumentStates.Live ||
				instrument.InstrumentType != BloFinInstrumentTypes.Swap)
				continue;

			var security = new SecurityMessage
			{
				SecurityId = instrument.InstrumentId.ToStockSharp(),
				Name = $"{instrument.BaseCurrency}/{instrument.QuoteCurrency} Perpetual",
				SecurityType = SecurityTypes.Future,
				OriginalTransactionId = lookupMsg.TransactionId,
				PriceStep = instrument.TickSize.ToDecimal(),
				VolumeStep = instrument.LotSize.ToDecimal(),
				MinVolume = instrument.MinSize.ToDecimal(),
				MaxVolume = instrument.MaxLimitSize.ToDecimal(),
				Multiplier = instrument.ContractValue.ToDecimal(),
				ExpiryDate = instrument.ExpireTime > 0
					? instrument.ExpireTime.ToUtcTime()
					: null,
			}.TryFillUnderlyingId(instrument.BaseCurrency?.ToUpperInvariant());
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			await SendOutMessageAsync(security, cancellationToken);
			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeLevel1Async(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var symbol = GetSymbol(mdMsg.SecurityId);
		var ticker = (await RestClient.GetTickersAsync(symbol, cancellationToken) ?? [])
			.FirstOrDefault();
		if (ticker is null)
			throw new InvalidDataException($"BloFin returned no ticker for '{symbol}'.");
		await SendTickerAsync(ticker, mdMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var key = new StreamKey("tickers", symbol);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new() { InstrumentId = symbol });
			subscribe = AddReference(_streamReferences, key);
		}
		if (subscribe)
			await PublicWsClient.SubscribeTickerAsync(symbol, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeDepthAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var symbol = GetSymbol(mdMsg.SecurityId);
		var depth = NormalizeDepth(mdMsg.MaxDepth);
		var isFiveLevels = depth <= 5;
		var book = (await RestClient.GetBookAsync(symbol, depth.Min(100), cancellationToken) ?? [])
			.FirstOrDefault();
		if (book is null)
			throw new InvalidDataException($"BloFin returned no order book for '{symbol}'.");
		await SendBookAsync(book, QuoteChangeStates.SnapshotComplete, symbol,
			mdMsg.TransactionId, depth, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var channel = isFiveLevels ? "books5" : "books";
		var key = new StreamKey(channel, symbol);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				InstrumentId = symbol,
				Depth = depth,
				IsFiveLevels = isFiveLevels,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		if (subscribe)
			await PublicWsClient.SubscribeBookAsync(symbol, isFiveLevels, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeTicksAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var symbol = GetSymbol(mdMsg.SecurityId);
		var from = mdMsg.From?.ToUniversalTime() ?? DateTime.MinValue;
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var limit = (mdMsg.Count ?? 100).Min(100).Max(1).To<int>();
		foreach (var trade in (await RestClient.GetTradesAsync(symbol, limit, cancellationToken) ?? [])
			.Where(trade => trade.Timestamp > 0 && trade.Timestamp.ToUtcTime() >= from &&
				trade.Timestamp.ToUtcTime() <= to)
			.OrderBy(static trade => trade.Timestamp))
			await SendTradeAsync(trade, mdMsg.TransactionId, cancellationToken);

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var key = new StreamKey("trades", symbol);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new() { InstrumentId = symbol });
			subscribe = AddReference(_streamReferences, key);
		}
		if (subscribe)
			await PublicWsClient.SubscribeTradesAsync(symbol, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeCandlesAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var symbol = GetSymbol(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		var interval = timeFrame.ToBloFinInterval();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var requestedCount = GetCandleCount(mdMsg, timeFrame, to);
		var from = mdMsg.From?.ToUniversalTime() ??
			to - TimeSpan.FromTicks(timeFrame.Ticks * requestedCount);
		var candles = await LoadCandlesAsync(symbol, interval, from, to, requestedCount,
			cancellationToken);
		foreach (var candle in candles)
			await SendCandleAsync(symbol, candle, timeFrame, mdMsg.TransactionId, cancellationToken);

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var channel = "candle" + interval.ToBloFin();
		var key = new StreamKey(channel, symbol);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				InstrumentId = symbol,
				TimeFrame = timeFrame,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		if (subscribe)
			await PublicWsClient.SubscribeCandlesAsync(symbol, interval, cancellationToken);
	}

	private async ValueTask<BloFinCandle[]> LoadCandlesAsync(string symbol,
		BloFinCandleIntervals interval,
		DateTime from, DateTime to, int requestedCount, CancellationToken cancellationToken)
	{
		var candles = new List<BloFinCandle>();
		var timestamps = new HashSet<long>();
		var cursor = to.ToUnixMilliseconds() + 1;
		while (candles.Count < requestedCount)
		{
			var limit = (requestedCount - candles.Count).Min(1440).Max(1);
			var page = await RestClient.GetCandlesAsync(new()
			{
				InstrumentId = symbol,
				Bar = interval,
				After = cursor,
				Limit = limit,
			}, cancellationToken) ?? [];
			if (page.Length == 0)
				break;
			foreach (var candle in page)
			{
				var time = candle.Timestamp.ToUtcTime();
				if (time >= from && time <= to && timestamps.Add(candle.Timestamp))
					candles.Add(candle);
			}
			var earliest = page.Min(static candle => candle.Timestamp);
			if (earliest <= from.ToUnixMilliseconds() || earliest >= cursor || page.Length < limit)
				break;
			cursor = earliest;
		}
		return [.. candles.OrderBy(static candle => candle.Timestamp).TakeLast(requestedCount)];
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_level1Subscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences,
					new("tickers", subscription.InstrumentId));
		}
		if (unsubscribe)
			await PublicWsClient.UnsubscribeTickerAsync(subscription.InstrumentId, cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_depthSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences, new(
					subscription.IsFiveLevels ? "books5" : "books", subscription.InstrumentId));
		}
		if (unsubscribe)
			await PublicWsClient.UnsubscribeBookAsync(subscription.InstrumentId,
				subscription.IsFiveLevels, cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_tickSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences,
					new("trades", subscription.InstrumentId));
		}
		if (unsubscribe)
			await PublicWsClient.UnsubscribeTradesAsync(subscription.InstrumentId, cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		CandleSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_candleSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences, new(
					"candle" + subscription.TimeFrame.ToBloFinInterval().ToBloFin(),
					subscription.InstrumentId));
		}
		if (unsubscribe)
			await PublicWsClient.UnsubscribeCandlesAsync(subscription.InstrumentId,
				subscription.TimeFrame.ToBloFinInterval(), cancellationToken);
	}

	private async ValueTask OnTickerAsync(BloFinTicker ticker,
		CancellationToken cancellationToken)
	{
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _level1Subscriptions
				.Where(pair => pair.Value.InstrumentId.EqualsIgnoreCase(ticker.InstrumentId))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendTickerAsync(ticker, id, cancellationToken);
	}

	private async ValueTask OnBookAsync(string channel, string instrumentId, BloFinBook book,
		QuoteChangeStates state, CancellationToken cancellationToken)
	{
		(long Id, string Symbol, int Depth)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions
				.Where(pair => (pair.Value.IsFiveLevels ? "books5" : "books") == channel)
				.Where(pair => pair.Value.InstrumentId.EqualsIgnoreCase(instrumentId))
				.Select(static pair => (pair.Key, pair.Value.InstrumentId, pair.Value.Depth))];
		foreach (var subscription in subscriptions)
			await SendBookAsync(book, state, subscription.Symbol, subscription.Id,
				state == QuoteChangeStates.Increment ? int.MaxValue : subscription.Depth,
				cancellationToken);
	}

	private async ValueTask OnTradeAsync(BloFinTrade trade,
		CancellationToken cancellationToken)
	{
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _tickSubscriptions
				.Where(pair => pair.Value.InstrumentId.EqualsIgnoreCase(trade.InstrumentId))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendTradeAsync(trade, id, cancellationToken);
	}

	private async ValueTask OnCandleAsync(string channel, string instrumentId, BloFinCandle candle,
		CancellationToken cancellationToken)
	{
		(long Id, string Symbol, TimeSpan TimeFrame)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _candleSubscriptions
				.Where(pair => ("candle" + pair.Value.TimeFrame.ToBloFinInterval().ToBloFin()) == channel)
				.Where(pair => pair.Value.InstrumentId.EqualsIgnoreCase(instrumentId))
				.Select(static pair => (pair.Key, pair.Value.InstrumentId, pair.Value.TimeFrame))];
		foreach (var subscription in subscriptions)
			await SendCandleAsync(subscription.Symbol, candle, subscription.TimeFrame,
				subscription.Id, cancellationToken);
	}

	private ValueTask OnFundingRateAsync(BloFinFundingRate rate,
		CancellationToken cancellationToken)
	{
		_ = rate;
		_ = cancellationToken;
		return default;
	}

	private ValueTask SendTickerAsync(BloFinTicker ticker, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ticker.InstrumentId.ToStockSharp(),
			ServerTime = ticker.Timestamp > 0 ? ticker.Timestamp.ToUtcTime() : CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice.ToDecimal())
		.TryAdd(Level1Fields.LastTradeVolume, ticker.LastSize.ToDecimal())
		.TryAdd(Level1Fields.OpenPrice, ticker.OpenPrice.ToDecimal())
		.TryAdd(Level1Fields.HighPrice, ticker.HighPrice.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, ticker.LowPrice.ToDecimal())
		.TryAdd(Level1Fields.Volume, ticker.BaseVolume.ToDecimal())
		.TryAdd(Level1Fields.BestBidPrice, ticker.BidPrice.ToDecimal())
		.TryAdd(Level1Fields.BestBidVolume, ticker.BidSize.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, ticker.AskPrice.ToDecimal())
		.TryAdd(Level1Fields.BestAskVolume, ticker.AskSize.ToDecimal()), cancellationToken);

	private ValueTask SendBookAsync(BloFinBook book, QuoteChangeStates state, string symbol,
		long transactionId, int depth, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(),
			ServerTime = book.Timestamp > 0 ? book.Timestamp.ToUtcTime() : CurrentTime,
			OriginalTransactionId = transactionId,
			State = state,
			SeqNum = book.SequenceId,
			Bids = ToQuotes(book.Bids, depth),
			Asks = ToQuotes(book.Asks, depth),
		}, cancellationToken);

	private ValueTask SendTradeAsync(BloFinTrade trade, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = trade.InstrumentId.ToStockSharp(),
			ServerTime = trade.Timestamp > 0 ? trade.Timestamp.ToUtcTime() : CurrentTime,
			OriginalTransactionId = transactionId,
			TradeStringId = trade.TradeId,
			TradePrice = trade.Price.ToDecimal(),
			TradeVolume = trade.Size.ToDecimal(),
			OriginSide = trade.Side.ToStockSharpSide(),
		}, cancellationToken);

	private ValueTask SendCandleAsync(string symbol, BloFinCandle candle, TimeSpan timeFrame,
		long transactionId, CancellationToken cancellationToken)
	{
		var openTime = candle.Timestamp.ToUtcTime();
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = symbol.ToStockSharp(),
			OpenTime = openTime,
			CloseTime = openTime + timeFrame,
			OpenPrice = candle.Open.ToDecimal() ?? 0m,
			HighPrice = candle.High.ToDecimal() ?? 0m,
			LowPrice = candle.Low.ToDecimal() ?? 0m,
			ClosePrice = candle.Close.ToDecimal() ?? 0m,
			TotalVolume = candle.BaseVolume.ToDecimal() ?? 0m,
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = candle.IsFinished ? CandleStates.Finished : CandleStates.Active,
		}, cancellationToken);
	}

	private static QuoteChange[] ToQuotes(BloFinBookLevel[] levels, int depth)
		=> [.. (levels ?? []).Take(depth).Select(static level => new QuoteChange(
			level.Price.ToDecimal() ?? 0m, level.Size.ToDecimal() ?? 0m))];

	private static int GetCandleCount(MarketDataMessage message, TimeSpan timeFrame, DateTime to)
	{
		if (message.Count is long count)
			return count.Min(10000).Max(1).To<int>();
		if (message.From is DateTime from && to > from)
			return ((to - from.ToUniversalTime()).Ticks / timeFrame.Ticks + 1)
				.Min(10000L).Max(1L).To<int>();
		return 1440;
	}
}
