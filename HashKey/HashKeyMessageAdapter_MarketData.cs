namespace StockSharp.HashKey;

public partial class HashKeyMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var requestedBoard = lookupMsg.SecurityId.BoardCode;
		var requestedSymbol = lookupMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: NormalizeSymbol(lookupMsg.SecurityId.SecurityCode);
		var markets = new List<MarketDefinition>();
		using (_sync.EnterScope())
		{
			if (IsSectionEnabled(HashKeySections.Spot))
				markets.AddRange(_spotMarkets.Values);
			if (IsSectionEnabled(HashKeySections.Futures))
				markets.AddRange(_futuresMarkets.Values);
		}

		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var market in markets.OrderBy(static market => market.Section)
			.ThenBy(static market => market.Symbol, StringComparer.OrdinalIgnoreCase))
		{
			if (!requestedBoard.IsEmpty() &&
				!requestedBoard.EqualsIgnoreCase(market.Section.ToBoardCode()))
				continue;
			if (!requestedSymbol.IsEmpty() &&
				!requestedSymbol.EqualsIgnoreCase(market.Symbol))
				continue;

			var security = CreateSecurity(market, lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			if (skip-- > 0)
				continue;
			await SendOutMessageAsync(security, cancellationToken);
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = security.SecurityId,
				ServerTime = CurrentTime,
				OriginalTransactionId = lookupMsg.TransactionId,
			}.TryAdd(Level1Fields.State, market.Status.ToStockSharp()),
				cancellationToken);
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
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"HashKey does not expose historical Level1 events.");

		var section = ResolveSection(mdMsg.SecurityId);
		var symbol = GetSymbol(mdMsg.SecurityId, section);
		await SendLevel1SnapshotAsync(section, symbol, mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var bookKey = new StreamKey(HashKeyWsTopics.BestBidOffer, symbol, null);
		var realtimeKey = new StreamKey(HashKeyWsTopics.Realtimes, symbol, null);
		bool subscribeBook;
		bool subscribeRealtime;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				Section = section,
			});
			subscribeBook = AddReference(_streamReferences, bookKey);
			subscribeRealtime = AddReference(_streamReferences, realtimeKey);
		}
		try
		{
			if (subscribeBook)
				await PublicWsClient.SubscribeBookTickerAsync(symbol, cancellationToken);
			if (subscribeRealtime)
				await PublicWsClient.SubscribeRealtimeAsync(symbol, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeLevel1Async(mdMsg.TransactionId, cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeDepthAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"HashKey does not expose historical order-book events.");

		var section = ResolveSection(mdMsg.SecurityId);
		var symbol = GetSymbol(mdMsg.SecurityId, section);
		var depth = (mdMsg.MaxDepth ?? 100).Min(200).Max(1);
		var snapshot = await RestClient.GetDepthAsync(new()
		{
			Symbol = symbol,
			Limit = depth,
		}, cancellationToken);
		await SendDepthAsync(section, symbol, snapshot.Timestamp, snapshot.Bids,
			snapshot.Asks, mdMsg.TransactionId, depth, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var key = new StreamKey(HashKeyWsTopics.Depth, symbol, null);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				Section = section,
				Depth = depth,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await PublicWsClient.SubscribeDepthAsync(symbol, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeDepthAsync(mdMsg.TransactionId, cancellationToken);
			throw;
		}
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
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var section = ResolveSection(mdMsg.SecurityId);
		var symbol = GetSymbol(mdMsg.SecurityId, section);
		var count = (mdMsg.Count ?? 100).Min(1000).Max(1).To<int>();
		var trades = await RestClient.GetPublicTradesAsync(new()
		{
			Symbol = symbol,
			Limit = count,
		}, cancellationToken);
		var from = mdMsg.From?.ToUniversalTime();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		foreach (var trade in (trades ?? [])
			.Where(trade => trade.Timestamp > 0 &&
				(from is null || trade.Timestamp.FromMilliseconds() >= from.Value) &&
				trade.Timestamp.FromMilliseconds() <= to)
			.OrderBy(static trade => trade.Timestamp))
			await SendPublicTradeAsync(section, symbol, trade, mdMsg.TransactionId,
				cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var key = new StreamKey(HashKeyWsTopics.Trade, symbol, null);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				Section = section,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await PublicWsClient.SubscribeTradesAsync(symbol, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeTicksAsync(mdMsg.TransactionId, cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeCandlesAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var section = ResolveSection(mdMsg.SecurityId);
		var symbol = GetSymbol(mdMsg.SecurityId, section);
		var timeFrame = mdMsg.GetTimeFrame();
		var interval = timeFrame.ToHashKey();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var count = GetCandleCount(mdMsg, timeFrame, to);
		var from = mdMsg.From?.ToUniversalTime() ??
			to - TimeSpan.FromTicks(timeFrame.Ticks * count);
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.From), from,
				"The candle-history start time is after its end time.");

		var candles = await LoadCandlesAsync(symbol, interval, timeFrame, from, to,
			count, cancellationToken);
		foreach (var candle in candles)
			await SendCandleAsync(section, symbol, candle.OpenTime,
				candle.Open, candle.High, candle.Low, candle.Close, candle.Volume,
				timeFrame, mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var key = new StreamKey(HashKeyWsTopics.Kline, symbol, interval);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				Section = section,
				TimeFrame = timeFrame,
				Interval = interval,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await PublicWsClient.SubscribeKlineAsync(symbol, interval,
					cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeCandlesAsync(mdMsg.TransactionId, cancellationToken);
			throw;
		}
	}

	private SecurityMessage CreateSecurity(MarketDefinition market,
		long originalTransactionId)
	{
		var isFutures = market.Section == HashKeySections.Futures;
		var security = new SecurityMessage
		{
			SecurityId = market.Symbol.ToStockSharp(market.Section),
			Name = market.Name.IsEmpty(isFutures
				? $"{market.BaseAsset}/{market.QuoteAsset} Perpetual"
				: $"{market.BaseAsset}/{market.QuoteAsset}"),
			ShortName = $"{market.BaseAsset}/{market.QuoteAsset}",
			SecurityType = isFutures ? SecurityTypes.Future : SecurityTypes.CryptoCurrency,
			Currency = market.QuoteAsset.ToCurrency(),
			PriceStep = market.PriceStep,
			VolumeStep = market.VolumeStep,
			MinVolume = market.MinimumVolume,
			MaxVolume = market.MaximumVolume,
			Multiplier = market.Multiplier,
			OriginalTransactionId = originalTransactionId,
		};
		if (isFutures && !market.BaseAsset.IsEmpty())
			security.TryFillUnderlyingId(market.BaseAsset);
		return security;
	}

	private async ValueTask SendLevel1SnapshotAsync(HashKeySections section,
		string symbol, long transactionId, CancellationToken cancellationToken)
	{
		var ticker = (await RestClient.GetTickersAsync(new()
		{
			Symbol = symbol,
			InstrumentType = section == HashKeySections.Spot
				? HashKeyInstrumentTypes.Spot
				: HashKeyInstrumentTypes.Futures,
		}, cancellationToken) ?? []).FirstOrDefault(item =>
			item?.Symbol.EqualsIgnoreCase(symbol) == true);
		var book = (await RestClient.GetBookTickersAsync(new() { Symbol = symbol },
			cancellationToken) ?? []).FirstOrDefault(item =>
			item?.Symbol.EqualsIgnoreCase(symbol) == true);
		HashKeyMarkPrice mark = null;
		if (section == HashKeySections.Futures)
			mark = await RestClient.GetMarkPriceAsync(new() { Symbol = symbol },
				cancellationToken);
		var timestamp = new[] { ticker?.Timestamp ?? 0, book?.Timestamp ?? 0,
			mark?.Timestamp ?? 0 }.Max();
		await SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = timestamp.FromHashKeyMilliseconds(CurrentTime),
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, ticker?.Last)
		.TryAdd(Level1Fields.OpenPrice, ticker?.Open)
		.TryAdd(Level1Fields.HighPrice, ticker?.High)
		.TryAdd(Level1Fields.LowPrice, ticker?.Low)
		.TryAdd(Level1Fields.Volume, ticker?.Volume)
		.TryAdd(Level1Fields.Turnover, ticker?.QuoteVolume)
		.TryAdd(Level1Fields.BestBidPrice, book?.Bid ?? ticker?.Bid)
		.TryAdd(Level1Fields.BestBidVolume, book?.BidSize)
		.TryAdd(Level1Fields.BestAskPrice, book?.Ask ?? ticker?.Ask)
		.TryAdd(Level1Fields.BestAskVolume, book?.AskSize)
		.TryAdd(Level1Fields.SettlementPrice, mark?.Price), cancellationToken);
	}

	private async ValueTask<HashKeyCandle[]> LoadCandlesAsync(string symbol,
		string interval, TimeSpan timeFrame, DateTime from, DateTime to, int maximum,
		CancellationToken cancellationToken)
	{
		var result = new List<HashKeyCandle>();
		var cursor = from;
		while (cursor <= to && result.Count < maximum)
		{
			var pageLimit = Math.Min(1000, maximum - result.Count);
			var page = await RestClient.GetCandlesAsync(new()
			{
				Symbol = symbol,
				Interval = interval,
				Limit = pageLimit,
				StartTime = cursor.ToMilliseconds(),
				EndTime = to.ToMilliseconds(),
			}, cancellationToken);
			if (page is not { Length: > 0 })
				break;
			result.AddRange(page.Where(candle => candle.OpenTime > 0 &&
				candle.OpenTime.FromMilliseconds() >= from &&
				candle.OpenTime.FromMilliseconds() <= to));
			var next = page.Max(static candle => candle.OpenTime)
				.FromMilliseconds() + timeFrame;
			if (next <= cursor || page.Length < pageLimit)
				break;
			cursor = next;
		}
		return [.. result.GroupBy(static candle => candle.OpenTime)
			.Select(static group => group.First())
			.OrderBy(static candle => candle.OpenTime)
			.TakeLast(maximum)];
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		bool unsubscribeBook = false;
		bool unsubscribeRealtime = false;
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(transactionId, out subscription))
			{
				unsubscribeBook = ReleaseReference(_streamReferences,
					new(HashKeyWsTopics.BestBidOffer, subscription.Symbol, null));
				unsubscribeRealtime = ReleaseReference(_streamReferences,
					new(HashKeyWsTopics.Realtimes, subscription.Symbol, null));
			}
		if (unsubscribeBook)
			await PublicWsClient.UnsubscribeBookTickerAsync(subscription.Symbol,
				cancellationToken);
		if (unsubscribeRealtime)
			await PublicWsClient.UnsubscribeRealtimeAsync(subscription.Symbol,
				cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
			if (_depthSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences,
					new(HashKeyWsTopics.Depth, subscription.Symbol, null));
		if (unsubscribe)
			await PublicWsClient.UnsubscribeDepthAsync(subscription.Symbol,
				cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
			if (_tickSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences,
					new(HashKeyWsTopics.Trade, subscription.Symbol, null));
		if (unsubscribe)
			await PublicWsClient.UnsubscribeTradesAsync(subscription.Symbol,
				cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		CandleSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
			if (_candleSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences,
					new(HashKeyWsTopics.Kline, subscription.Symbol, subscription.Interval));
		if (unsubscribe)
			await PublicWsClient.UnsubscribeKlineAsync(subscription.Symbol,
				subscription.Interval, cancellationToken);
	}

	private async ValueTask OnBookTickerAsync(string symbol, HashKeyWsBookTicker ticker,
		CancellationToken cancellationToken)
	{
		if (symbol.IsEmpty() || ticker is null)
			return;
		MarketSubscription[] subscriptions;
		long[] ids;
		using (_sync.EnterScope())
		{
			var matches = _level1Subscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(symbol)).ToArray();
			ids = [.. matches.Select(static pair => pair.Key)];
			subscriptions = [.. matches.Select(static pair => pair.Value)];
		}
		for (var i = 0; i < ids.Length; i++)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = symbol.ToStockSharp(subscriptions[i].Section),
				ServerTime = ticker.Timestamp.FromHashKeyMilliseconds(CurrentTime),
				OriginalTransactionId = ids[i],
			}
			.TryAdd(Level1Fields.BestBidPrice, ticker.Bid)
			.TryAdd(Level1Fields.BestBidVolume, ticker.BidSize)
			.TryAdd(Level1Fields.BestAskPrice, ticker.Ask)
			.TryAdd(Level1Fields.BestAskVolume, ticker.AskSize), cancellationToken);
	}

	private async ValueTask OnRealtimeAsync(string symbol, HashKeyWsRealtime ticker,
		CancellationToken cancellationToken)
	{
		if (symbol.IsEmpty() || ticker is null)
			return;
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(symbol))];
		foreach (var pair in subscriptions)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = symbol.ToStockSharp(pair.Value.Section),
				ServerTime = ticker.Timestamp.FromHashKeyMilliseconds(CurrentTime),
				OriginalTransactionId = pair.Key,
			}
			.TryAdd(Level1Fields.LastTradePrice, ticker.Close)
			.TryAdd(Level1Fields.OpenPrice, ticker.Open)
			.TryAdd(Level1Fields.HighPrice, ticker.High)
			.TryAdd(Level1Fields.LowPrice, ticker.Low)
			.TryAdd(Level1Fields.Volume, ticker.Volume)
			.TryAdd(Level1Fields.Turnover, ticker.QuoteVolume), cancellationToken);
	}

	private async ValueTask OnDepthAsync(string symbol, HashKeyWsDepth depth,
		CancellationToken cancellationToken)
	{
		if (symbol.IsEmpty() || depth is null)
			return;
		KeyValuePair<long, DepthSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(symbol))];
		foreach (var pair in subscriptions)
			await SendDepthAsync(pair.Value.Section, symbol, depth.Timestamp, depth.Bids,
				depth.Asks, pair.Key, pair.Value.Depth, cancellationToken);
	}

	private async ValueTask OnTradeAsync(string symbol, HashKeyWsTrade trade,
		CancellationToken cancellationToken)
	{
		if (symbol.IsEmpty() || trade is null)
			return;
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _tickSubscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(symbol))];
		foreach (var pair in subscriptions)
			await SendPublicTradeAsync(pair.Value.Section, symbol, new()
			{
				Id = trade.Id,
				Timestamp = trade.Timestamp,
				Price = trade.Price,
				Quantity = trade.Quantity,
				IsBuyerMaker = trade.IsBuyerMaker,
			}, pair.Key, cancellationToken);
	}

	private async ValueTask OnKlineAsync(string symbol, string interval,
		HashKeyWsKline candle, CancellationToken cancellationToken)
	{
		if (symbol.IsEmpty() || interval.IsEmpty() || candle is null)
			return;
		KeyValuePair<long, CandleSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _candleSubscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(symbol) &&
				pair.Value.Interval.EqualsIgnoreCase(interval))];
		foreach (var pair in subscriptions)
			await SendCandleAsync(pair.Value.Section, symbol, candle.OpenTime,
				candle.Open, candle.High, candle.Low, candle.Close, candle.Volume,
				pair.Value.TimeFrame, pair.Key, cancellationToken);
	}

	private ValueTask SendDepthAsync(HashKeySections section, string symbol,
		long timestamp, HashKeyPriceLevel[] bids, HashKeyPriceLevel[] asks,
		long transactionId, int depth, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = timestamp.FromHashKeyMilliseconds(CurrentTime),
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = ToQuotes(bids, false, depth),
			Asks = ToQuotes(asks, true, depth),
		}, cancellationToken);

	private ValueTask SendPublicTradeAsync(HashKeySections section, string symbol,
		HashKeyPublicTrade trade, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = trade.Timestamp.FromHashKeyMilliseconds(CurrentTime),
			OriginalTransactionId = transactionId,
			TradeStringId = trade.Id,
			TradePrice = trade.Price,
			TradeVolume = trade.Quantity,
			OriginSide = trade.IsBuyerMaker ? Sides.Sell : Sides.Buy,
		}, cancellationToken);

	private ValueTask SendCandleAsync(HashKeySections section, string symbol,
		long openTimestamp, decimal open, decimal high, decimal low, decimal close,
		decimal volume, TimeSpan timeFrame, long transactionId,
		CancellationToken cancellationToken)
	{
		var openTime = openTimestamp.FromHashKeyMilliseconds(CurrentTime);
		var closeTime = openTime + timeFrame;
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = symbol.ToStockSharp(section),
			OpenTime = openTime,
			CloseTime = closeTime,
			OpenPrice = open,
			HighPrice = high,
			LowPrice = low,
			ClosePrice = close,
			TotalVolume = volume,
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = closeTime <= CurrentTime ? CandleStates.Finished : CandleStates.Active,
		}, cancellationToken);
	}

	private static QuoteChange[] ToQuotes(HashKeyPriceLevel[] levels, bool isAsk,
		int depth)
	{
		var ordered = isAsk
			? (levels ?? []).Where(static level => level is not null &&
				level.Price > 0 && level.Size > 0).OrderBy(static level => level.Price)
			: (levels ?? []).Where(static level => level is not null &&
				level.Price > 0 && level.Size > 0).OrderByDescending(static level => level.Price);
		return [.. ordered.Take(depth)
			.Select(static level => new QuoteChange(level.Price, level.Size))];
	}

	private static int GetCandleCount(MarketDataMessage message, TimeSpan timeFrame,
		DateTime to)
	{
		if (message.Count is long count)
			return count.Min(100000).Max(1).To<int>();
		if (message.From is DateTime from && to > from)
			return ((to - from.ToUniversalTime()).Ticks / timeFrame.Ticks + 1)
				.Min(100000L).Max(1L).To<int>();
		return 300;
	}

	private async ValueTask CompleteMarketSubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId, cancellationToken);
	}
}
