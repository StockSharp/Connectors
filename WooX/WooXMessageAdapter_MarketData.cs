namespace StockSharp.WooX;

public partial class WooXMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsureConnected();
		await EnsureSymbolsAsync(cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;
		WooXSymbol[] symbols;
		using (_sync.EnterScope())
			symbols = [.. _symbols.Values.OrderBy(static item => item.Symbol)];

		foreach (var item in symbols)
		{
			if (item.IsTradingValue == 0 && !item.Status.EqualsIgnoreCase("TRADING"))
				continue;
			var isPerpetual = item.Symbol.StartsWith("PERP_", StringComparison.OrdinalIgnoreCase);
			var (baseAsset, quoteAsset) = SplitSymbol(item.Symbol);
			var security = new SecurityMessage
			{
				SecurityId = item.Symbol.ToStockSharp(),
				Name = isPerpetual
					? $"{baseAsset}/{quoteAsset} Perpetual"
					: $"{baseAsset}/{quoteAsset}",
				SecurityType = isPerpetual ? SecurityTypes.Future : SecurityTypes.CryptoCurrency,
				OriginalTransactionId = lookupMsg.TransactionId,
				PriceStep = item.QuoteTick,
				VolumeStep = item.BaseTick,
				MinVolume = item.BaseMinimum,
				MaxVolume = item.BaseMaximum,
				Multiplier = isPerpetual ? item.BaseAssetMultiplier : null,
			}.TryFillUnderlyingId(baseAsset);
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
		await SendLevel1SnapshotAsync(symbol, mdMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}
		EnsureRealtimeReady();

		var topics = symbol.StartsWith("PERP_", StringComparison.OrdinalIgnoreCase)
			? new[] { WooXWsTopics.Ticker, WooXWsTopics.BestBidOffer,
				WooXWsTopics.IndexPrice, WooXWsTopics.MarkPrice }
			: new[] { WooXWsTopics.Ticker, WooXWsTopics.BestBidOffer };
		var subscribe = new List<WooXWsTopics>();
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new() { Symbol = symbol });
			foreach (var topic in topics)
				if (AddReference(_streamReferences, new(topic, symbol, default)))
					subscribe.Add(topic);
		}
		foreach (var topic in subscribe)
			await ChangeLevel1StreamAsync(topic, symbol, true, cancellationToken);
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
		var book = await RestClient.GetOrderBookAsync(symbol, depth, cancellationToken);
		await SendBookAsync(symbol, book.Bids, book.Asks, book.Timestamp > 0
			? book.Timestamp.ToWooXTime()
			: CurrentTime, mdMsg.TransactionId, depth, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}
		EnsureRealtimeReady();

		var key = new StreamKey(WooXWsTopics.OrderBook, symbol, default);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				Depth = depth,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		if (subscribe)
			await PublicWsClient.SubscribeBookAsync(symbol, cancellationToken);
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
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var from = mdMsg.From?.ToUniversalTime();
		var count = (mdMsg.Count ?? 100).Min(10000).Max(1).To<int>();
		var trades = await LoadMarketTradesAsync(symbol, from, to, count, cancellationToken);
		foreach (var trade in trades)
			await SendTradeAsync(trade, mdMsg.TransactionId, cancellationToken);

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}
		EnsureRealtimeReady();

		var key = new StreamKey(WooXWsTopics.Trade, symbol, default);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new() { Symbol = symbol });
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
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var count = GetCandleCount(mdMsg, timeFrame, to);
		var from = mdMsg.From?.ToUniversalTime() ??
			to - TimeSpan.FromTicks(timeFrame.Ticks * count);
		var candles = await LoadCandlesAsync(symbol, timeFrame, from, to, count,
			cancellationToken);
		foreach (var candle in candles)
			await SendCandleAsync(candle, timeFrame, mdMsg.TransactionId, cancellationToken);

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}
		EnsureRealtimeReady();

		var key = new StreamKey(WooXWsTopics.Candle, symbol, timeFrame);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				TimeFrame = timeFrame,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		if (subscribe)
			await PublicWsClient.SubscribeCandlesAsync(symbol, timeFrame, cancellationToken);
	}

	private async ValueTask EnsureSymbolsAsync(CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
			if (_symbolsLoaded)
				return;
		var response = await RestClient.GetSymbolsAsync(cancellationToken);
		RegisterSymbols(response.Rows);
		using (_sync.EnterScope())
			_symbolsLoaded = true;
	}

	private async ValueTask SendLevel1SnapshotAsync(string symbol, long transactionId,
		CancellationToken cancellationToken)
	{
		var book = await RestClient.GetOrderBookAsync(symbol, 1, cancellationToken);
		var trades = await RestClient.GetMarketTradesAsync(symbol, 1, cancellationToken);
		var candles = await RestClient.GetKlinesAsync(new()
		{
			Symbol = symbol,
			Interval = "1d",
			Limit = 1,
		}, cancellationToken);
		var trade = trades.Rows?.FirstOrDefault();
		var candle = candles.Rows?.FirstOrDefault();
		var bid = book.Bids?.FirstOrDefault();
		var ask = book.Asks?.FirstOrDefault();
		var serverTime = book.Timestamp > 0 ? book.Timestamp.ToWooXTime() : CurrentTime;
		var message = new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(),
			ServerTime = serverTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, bid?.Price)
		.TryAdd(Level1Fields.BestBidVolume, bid?.Quantity)
		.TryAdd(Level1Fields.BestAskPrice, ask?.Price)
		.TryAdd(Level1Fields.BestAskVolume, ask?.Quantity)
		.TryAdd(Level1Fields.LastTradePrice, trade?.Price)
		.TryAdd(Level1Fields.LastTradeVolume, trade?.Quantity)
		.TryAdd(Level1Fields.OpenPrice, candle?.Open)
		.TryAdd(Level1Fields.HighPrice, candle?.High)
		.TryAdd(Level1Fields.LowPrice, candle?.Low)
		.TryAdd(Level1Fields.Volume, candle?.Volume)
		.TryAdd(Level1Fields.Turnover, candle?.Amount);

		if (symbol.StartsWith("PERP_", StringComparison.OrdinalIgnoreCase))
		{
			var future = (await RestClient.GetFuturesAsync(cancellationToken)).Rows?
				.FirstOrDefault(item => item.Symbol.EqualsIgnoreCase(symbol));
			if (future is not null)
				message
					.TryAdd(Level1Fields.Index, future.IndexPrice)
					.TryAdd(Level1Fields.SettlementPrice, future.MarkPrice)
					.TryAdd(Level1Fields.OpenInterest, future.OpenInterest);
		}
		await SendOutMessageAsync(message, cancellationToken);
	}

	private async ValueTask<WooXMarketTrade[]> LoadMarketTradesAsync(string symbol,
		DateTime? from, DateTime to, int count, CancellationToken cancellationToken)
	{
		if (from is null)
		{
			var response = await RestClient.GetMarketTradesAsync(symbol, count.Min(1000),
				cancellationToken);
			return [.. (response.Rows ?? [])
				.Where(item => item.ExecutedTimestamp.ToUtcSeconds() <= to)
				.OrderBy(static item => item.ExecutedTimestamp.ToUtcSeconds())
				.TakeLast(count)];
		}

		var result = new List<WooXMarketTrade>();
		for (var page = 1; result.Count < count; page++)
		{
			var size = (count - result.Count).Min(100).Max(1);
			var response = await RestClient.GetHistoricalTradesAsync(new()
			{
				Symbol = symbol,
				StartTime = from.Value.ToUnixMilliseconds(),
				Page = page,
				Size = size,
			}, cancellationToken);
			var rows = response.Data?.Rows ?? [];
			result.AddRange(rows.Where(item =>
			{
				var time = item.ExecutedTimestamp.ToUtcSeconds();
				return time >= from.Value && time <= to;
			}));
			if (rows.Length < size || response.Data?.Meta is { } meta &&
				meta.CurrentPage * meta.RecordsPerPage >= meta.Total)
				break;
		}
		return [.. result.OrderBy(static item => item.ExecutedTimestamp.ToUtcSeconds())
			.TakeLast(count)];
	}

	private async ValueTask<WooXCandle[]> LoadCandlesAsync(string symbol, TimeSpan timeFrame,
		DateTime from, DateTime to, int count, CancellationToken cancellationToken)
	{
		var interval = timeFrame.ToWooXInterval();
		if (count <= 1000 && from >= to - TimeSpan.FromTicks(timeFrame.Ticks * 1000))
		{
			var response = await RestClient.GetKlinesAsync(new()
			{
				Symbol = symbol,
				Interval = interval,
				Limit = count,
			}, cancellationToken);
			return [.. (response.Rows ?? [])
				.Where(item => item.StartTimestamp.ToUtcTime() >= from &&
					item.StartTimestamp.ToUtcTime() <= to)
				.OrderBy(static item => item.StartTimestamp)
				.TakeLast(count)];
		}

		var result = new List<WooXCandle>();
		for (var page = 1; result.Count < count; page++)
		{
			var size = (count - result.Count).Min(1000).Max(1);
			var response = await RestClient.GetHistoricalKlinesAsync(new()
			{
				Symbol = symbol,
				Interval = interval,
				StartTime = from.ToUnixMilliseconds(),
				EndTime = to.ToUnixMilliseconds(),
				Page = page,
				Size = size,
			}, cancellationToken);
			var rows = response.Data?.Rows ?? [];
			result.AddRange(rows.Where(item => item.StartTimestamp.ToUtcTime() >= from &&
				item.StartTimestamp.ToUtcTime() <= to));
			if (rows.Length < size || response.Data?.Meta is { } meta &&
				meta.CurrentPage * meta.RecordsPerPage >= meta.Total)
				break;
		}
		return [.. result.GroupBy(static item => item.StartTimestamp)
			.Select(static group => group.First())
			.OrderBy(static item => item.StartTimestamp)
			.TakeLast(count)];
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var unsubscribe = new List<WooXWsTopics>();
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(transactionId, out subscription))
			{
				var topics = subscription.Symbol.StartsWith("PERP_", StringComparison.OrdinalIgnoreCase)
					? new[] { WooXWsTopics.Ticker, WooXWsTopics.BestBidOffer,
						WooXWsTopics.IndexPrice, WooXWsTopics.MarkPrice }
					: new[] { WooXWsTopics.Ticker, WooXWsTopics.BestBidOffer };
				foreach (var topic in topics)
					if (ReleaseReference(_streamReferences,
						new(topic, subscription.Symbol, default)))
						unsubscribe.Add(topic);
			}
		if (_publicWsClient is not null)
			foreach (var topic in unsubscribe)
				await ChangeLevel1StreamAsync(topic, subscription.Symbol, false,
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
					new(WooXWsTopics.OrderBook, subscription.Symbol, default));
		if (unsubscribe && _publicWsClient is not null)
			await _publicWsClient.UnsubscribeBookAsync(subscription.Symbol, cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
			if (_tickSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences,
					new(WooXWsTopics.Trade, subscription.Symbol, default));
		if (unsubscribe && _publicWsClient is not null)
			await _publicWsClient.UnsubscribeTradesAsync(subscription.Symbol, cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		CandleSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
			if (_candleSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences,
					new(WooXWsTopics.Candle, subscription.Symbol, subscription.TimeFrame));
		if (unsubscribe && _publicWsClient is not null)
			await _publicWsClient.UnsubscribeCandlesAsync(subscription.Symbol,
				subscription.TimeFrame, cancellationToken);
	}

	private async ValueTask OnTickerAsync(WooXWsTicker ticker, long timestamp,
		CancellationToken cancellationToken)
	{
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _level1Subscriptions
				.Where(pair => pair.Value.Symbol.EqualsIgnoreCase(ticker.Symbol))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendTickerAsync(ticker, timestamp, id, cancellationToken);
	}

	private async ValueTask OnBestBidOfferAsync(WooXWsBestBidOffer bestBidOffer,
		long timestamp, CancellationToken cancellationToken)
	{
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _level1Subscriptions
				.Where(pair => pair.Value.Symbol.EqualsIgnoreCase(bestBidOffer.Symbol))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = bestBidOffer.Symbol.ToStockSharp(),
				ServerTime = timestamp > 0 ? timestamp.ToUtcTime() : CurrentTime,
				OriginalTransactionId = id,
			}
			.TryAdd(Level1Fields.BestBidPrice, bestBidOffer.BidPrice)
			.TryAdd(Level1Fields.BestBidVolume, bestBidOffer.BidSize)
			.TryAdd(Level1Fields.BestAskPrice, bestBidOffer.AskPrice)
			.TryAdd(Level1Fields.BestAskVolume, bestBidOffer.AskSize), cancellationToken);
	}

	private async ValueTask OnReferencePriceAsync(WooXWsTopics topic,
		WooXWsReferencePrice price, long timestamp, CancellationToken cancellationToken)
	{
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _level1Subscriptions
				.Where(pair => pair.Value.Symbol.EqualsIgnoreCase(price.Symbol))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
		{
			var message = new Level1ChangeMessage
			{
				SecurityId = price.Symbol.ToStockSharp(),
				ServerTime = timestamp > 0 ? timestamp.ToUtcTime() : CurrentTime,
				OriginalTransactionId = id,
			};
			message.TryAdd(topic == WooXWsTopics.IndexPrice
				? Level1Fields.Index
				: Level1Fields.SettlementPrice, price.Price);
			await SendOutMessageAsync(message, cancellationToken);
		}
	}

	private async ValueTask OnBookAsync(WooXWsBook book, long timestamp,
		CancellationToken cancellationToken)
	{
		(long Id, int Depth)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions
				.Where(pair => pair.Value.Symbol.EqualsIgnoreCase(book.Symbol))
				.Select(static pair => (pair.Key, pair.Value.Depth))];
		foreach (var subscription in subscriptions)
			await SendBookAsync(book.Symbol, book.Bids, book.Asks,
				timestamp > 0 ? timestamp.ToUtcTime() : CurrentTime,
				subscription.Id, subscription.Depth, cancellationToken);
	}

	private async ValueTask OnTradeAsync(WooXWsTrade trade, long timestamp,
		CancellationToken cancellationToken)
	{
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _tickSubscriptions
				.Where(pair => pair.Value.Symbol.EqualsIgnoreCase(trade.Symbol))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendTradeAsync(trade, timestamp, id, cancellationToken);
	}

	private async ValueTask OnCandleAsync(WooXWsCandle candle, long timestamp,
		CancellationToken cancellationToken)
	{
		var timeFrame = candle.Interval.ToTimeFrame();
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _candleSubscriptions
				.Where(pair => pair.Value.Symbol.EqualsIgnoreCase(candle.Symbol) &&
					pair.Value.TimeFrame == timeFrame)
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendCandleAsync(candle, timeFrame, timestamp, id, cancellationToken);
	}

	private ValueTask SendTickerAsync(WooXWsTicker ticker, long timestamp, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ticker.Symbol.ToStockSharp(),
			ServerTime = (ticker.LastTimestamp > 0 ? ticker.LastTimestamp : timestamp).ToUtcTime(),
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, ticker.Close)
		.TryAdd(Level1Fields.OpenPrice, ticker.Open)
		.TryAdd(Level1Fields.HighPrice, ticker.High)
		.TryAdd(Level1Fields.LowPrice, ticker.Low)
		.TryAdd(Level1Fields.Volume, ticker.Volume)
		.TryAdd(Level1Fields.Turnover, ticker.Amount), cancellationToken);

	private ValueTask SendBookAsync(string symbol, WooXNamedPriceLevel[] bids,
		WooXNamedPriceLevel[] asks, DateTime serverTime, long transactionId, int depth,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(),
			ServerTime = serverTime,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = [.. (bids ?? []).Take(depth)
				.Select(static level => new QuoteChange(level.Price, level.Quantity))],
			Asks = [.. (asks ?? []).Take(depth)
				.Select(static level => new QuoteChange(level.Price, level.Quantity))],
		}, cancellationToken);

	private ValueTask SendBookAsync(string symbol, WooXPriceLevel[] bids,
		WooXPriceLevel[] asks, DateTime serverTime, long transactionId, int depth,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(),
			ServerTime = serverTime,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = [.. (bids ?? []).Take(depth)
				.Select(static level => new QuoteChange(level.Price, level.Quantity))],
			Asks = [.. (asks ?? []).Take(depth)
				.Select(static level => new QuoteChange(level.Price, level.Quantity))],
		}, cancellationToken);

	private ValueTask SendTradeAsync(WooXMarketTrade trade, long transactionId,
		CancellationToken cancellationToken)
	{
		var time = trade.ExecutedTimestamp.ToUtcSeconds();
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = trade.Symbol.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
			TradeStringId = $"{trade.ExecutedTimestamp}-{trade.Side}-{trade.Price.ToWire()}-" +
				$"{trade.Quantity.ToWire()}",
			TradePrice = trade.Price,
			TradeVolume = trade.Quantity,
			OriginSide = trade.Side.ToStockSharp(),
		}, cancellationToken);
	}

	private ValueTask SendTradeAsync(WooXWsTrade trade, long timestamp, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = trade.Symbol.ToStockSharp(),
			ServerTime = timestamp > 0 ? timestamp.ToUtcTime() : CurrentTime,
			OriginalTransactionId = transactionId,
			TradeStringId = $"{timestamp}-{Interlocked.Increment(ref _publicTradeId)}",
			TradePrice = trade.Price,
			TradeVolume = trade.Size,
			OriginSide = trade.Side.ToStockSharp(),
		}, cancellationToken);

	private ValueTask SendCandleAsync(WooXCandle candle, TimeSpan timeFrame, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = candle.Symbol.ToStockSharp(),
			OpenTime = candle.StartTimestamp.ToUtcTime(),
			CloseTime = candle.EndTimestamp.ToUtcTime(),
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.Volume,
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = candle.EndTimestamp.ToUtcTime() <= CurrentTime
				? CandleStates.Finished
				: CandleStates.Active,
		}, cancellationToken);

	private ValueTask SendCandleAsync(WooXWsCandle candle, TimeSpan timeFrame, long timestamp,
		long transactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = candle.Symbol.ToStockSharp(),
			OpenTime = candle.StartTime.ToUtcTime(),
			CloseTime = candle.EndTime.ToUtcTime(),
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.Volume,
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = timestamp >= candle.EndTime ? CandleStates.Finished : CandleStates.Active,
		}, cancellationToken);

	private static int GetCandleCount(MarketDataMessage message, TimeSpan timeFrame, DateTime to)
	{
		if (message.Count is long count)
			return count.Min(10000).Max(1).To<int>();
		if (message.From is DateTime from && to > from)
			return ((to - from.ToUniversalTime()).Ticks / timeFrame.Ticks + 1)
				.Min(10000L).Max(1L).To<int>();
		return 300;
	}

	private ValueTask ChangeLevel1StreamAsync(WooXWsTopics topic, string symbol,
		bool isSubscribe, CancellationToken cancellationToken)
		=> topic switch
		{
			WooXWsTopics.Ticker => isSubscribe
				? PublicWsClient.SubscribeTickerAsync(symbol, cancellationToken)
				: PublicWsClient.UnsubscribeTickerAsync(symbol, cancellationToken),
			WooXWsTopics.BestBidOffer => isSubscribe
				? PublicWsClient.SubscribeBestBidOfferAsync(symbol, cancellationToken)
				: PublicWsClient.UnsubscribeBestBidOfferAsync(symbol, cancellationToken),
			WooXWsTopics.IndexPrice => isSubscribe
				? PublicWsClient.SubscribeIndexPriceAsync(symbol, cancellationToken)
				: PublicWsClient.UnsubscribeIndexPriceAsync(symbol, cancellationToken),
			WooXWsTopics.MarkPrice => isSubscribe
				? PublicWsClient.SubscribeMarkPriceAsync(symbol, cancellationToken)
				: PublicWsClient.UnsubscribeMarkPriceAsync(symbol, cancellationToken),
			_ => throw new ArgumentOutOfRangeException(nameof(topic), topic, null),
		};

	private static (string BaseAsset, string QuoteAsset) SplitSymbol(string symbol)
	{
		var value = symbol.StartsWith("SPOT_", StringComparison.OrdinalIgnoreCase)
			? symbol[5..]
			: symbol.StartsWith("PERP_", StringComparison.OrdinalIgnoreCase)
				? symbol[5..]
				: symbol;
		var separator = value.LastIndexOf('_');
		return separator > 0
			? (value[..separator], value[(separator + 1)..])
			: (value, string.Empty);
	}
}
