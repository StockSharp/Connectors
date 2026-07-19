namespace StockSharp.CoinDCX;

public partial class CoinDCXMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var requestedMarket = lookupMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: lookupMsg.SecurityId.SecurityCode.NormalizeMarket();
		MarketDefinition[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values];

		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var market in markets.OrderBy(static value => value.Market,
			StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.CoinDCX))
				continue;
			if (!requestedMarket.IsEmpty() &&
				!requestedMarket.EqualsIgnoreCase(market.Market))
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
			}.TryAdd(Level1Fields.State, market.IsTrading
				? SecurityStates.Trading
				: SecurityStates.Stoped), cancellationToken);
			if (--left <= 0)
				break;
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeLevel1Async(mdMsg.OriginalTransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"CoinDCX does not expose historical Level1 events.");

		var market = GetMarket(mdMsg.SecurityId);
		await SendLevel1SnapshotAsync(market, mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var tradesKey = new StreamKey(StreamTypes.Trades, market.Pair, default);
		var depthKey = new StreamKey(StreamTypes.Depth, market.Pair, default);
		bool subscribeTrades;
		bool subscribeDepth;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				Market = market.Market,
				Pair = market.Pair,
			});
			subscribeTrades = AddReference(_streamReferences, tradesKey);
			subscribeDepth = AddReference(_streamReferences, depthKey);
			if (subscribeDepth)
				_orderBooks[market.Pair] = new();
		}
		try
		{
			if (subscribeTrades)
				await SocketClient.SubscribeTradesAsync(market.Pair,
					cancellationToken);
			if (subscribeDepth)
				await SocketClient.SubscribeDepthAsync(market.Pair,
					cancellationToken);
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
			await UnsubscribeDepthAsync(mdMsg.OriginalTransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"CoinDCX does not expose historical order-book events.");

		var market = GetMarket(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? 50).Min(50).Max(1);
		var snapshot = await RestClient.GetOrderBookAsync(market.Pair,
			cancellationToken);
		await SendDepthAsync(market.Market,
			snapshot.Timestamp.FromMilliseconds(CurrentTime),
			ToQuotes(snapshot.Bids, false, depth),
			ToQuotes(snapshot.Asks, true, depth), mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var key = new StreamKey(StreamTypes.Depth, market.Pair, default);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Market = market.Market,
				Pair = market.Pair,
				Depth = depth,
			});
			subscribe = AddReference(_streamReferences, key);
			if (subscribe)
				_orderBooks[market.Pair] = new();
		}
		try
		{
			if (subscribe)
				await SocketClient.SubscribeDepthAsync(market.Pair,
					cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeDepthAsync(mdMsg.TransactionId, cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeTicksAsync(mdMsg.OriginalTransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var market = GetMarket(mdMsg.SecurityId);
		var count = (mdMsg.Count ?? 100).Min(500).Max(1).To<int>();
		var from = mdMsg.From?.ToUniversalTime();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var trades = await RestClient.GetTradesAsync(market.Pair, count,
			cancellationToken);
		foreach (var trade in (trades ?? []).Where(trade => trade is not null &&
			(from is null || trade.Timestamp.FromMilliseconds(DateTime.MinValue) >= from) &&
			trade.Timestamp.FromMilliseconds(DateTime.MaxValue) <= to)
			.OrderBy(static trade => trade.Timestamp))
			await SendPublicTradeAsync(market.Market, market.Pair, trade.Timestamp,
				trade.Price, trade.Quantity, trade.IsBuyerMaker, mdMsg.TransactionId,
				cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var key = new StreamKey(StreamTypes.Trades, market.Pair, default);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Market = market.Market,
				Pair = market.Pair,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await SocketClient.SubscribeTradesAsync(market.Pair,
					cancellationToken);
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
			await UnsubscribeCandlesAsync(mdMsg.OriginalTransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var market = GetMarket(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		var interval = timeFrame.ToCoinDCXInterval();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var count = GetCandleCount(mdMsg, timeFrame, to);
		var from = mdMsg.From?.ToUniversalTime() ??
			to - TimeSpan.FromTicks(timeFrame.Ticks * count);
		var candles = await RestClient.GetCandlesAsync(market.Pair, interval,
			from, to, count, cancellationToken);
		foreach (var candle in (candles ?? []).OrderBy(static value => value.Timestamp))
			await SendCandleAsync(market.Market, candle, timeFrame,
				mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var key = new StreamKey(StreamTypes.Candles, market.Pair, timeFrame);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Market = market.Market,
				Pair = market.Pair,
				TimeFrame = timeFrame,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await SocketClient.SubscribeCandlesAsync(market.Pair, interval,
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
		=> new()
		{
			SecurityId = market.Market.ToStockSharp(),
			Name = $"{market.BaseAsset}/{market.QuoteAsset}",
			ShortName = $"{market.BaseAsset}/{market.QuoteAsset}",
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = market.QuoteAsset.ToCurrency(),
			PriceStep = market.PriceStep > 0 ? market.PriceStep : null,
			VolumeStep = market.QuantityStep > 0 ? market.QuantityStep : null,
			MinVolume = market.MinimumQuantity > 0
				? market.MinimumQuantity
				: null,
			MaxVolume = market.MaximumQuantity > 0
				? market.MaximumQuantity
				: null,
			OriginalTransactionId = originalTransactionId,
		};

	private async ValueTask SendLevel1SnapshotAsync(MarketDefinition market,
		long transactionId, CancellationToken cancellationToken)
	{
		var ticker = (await RestClient.GetTickersAsync(cancellationToken))
			.FirstOrDefault(value => value?.Market.EqualsIgnoreCase(market.Market) == true);
		if (ticker is null)
			throw new InvalidDataException(
				$"CoinDCX returned no ticker for '{market.Market}'.");
		await SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.Market.ToStockSharp(),
			ServerTime = ticker.Timestamp < 10_000_000_000L
				? DateTimeOffset.FromUnixTimeSeconds(ticker.Timestamp).UtcDateTime
				: ticker.Timestamp.FromMilliseconds(CurrentTime),
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, ticker.Bid)
		.TryAdd(Level1Fields.BestAskPrice, ticker.Ask)
		.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice)
		.TryAdd(Level1Fields.HighPrice, ticker.High)
		.TryAdd(Level1Fields.LowPrice, ticker.Low)
		.TryAdd(Level1Fields.Volume, ticker.Volume)
		.TryAdd(Level1Fields.Change, ticker.Change24Hours), cancellationToken);
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		bool releaseTrades = false;
		bool releaseDepth = false;
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(transactionId, out subscription))
			{
				releaseTrades = ReleaseReference(_streamReferences,
					new(StreamTypes.Trades, subscription.Pair, default));
				releaseDepth = ReleaseReference(_streamReferences,
					new(StreamTypes.Depth, subscription.Pair, default));
				if (releaseDepth)
					_orderBooks.Remove(subscription.Pair);
			}
		if (releaseTrades)
			await SocketClient.ReleaseTradesAsync(subscription.Pair,
				cancellationToken);
		if (releaseDepth)
			await SocketClient.ReleaseDepthAsync(subscription.Pair,
				cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_depthSubscriptions.Remove(transactionId, out subscription))
			{
				release = ReleaseReference(_streamReferences,
					new(StreamTypes.Depth, subscription.Pair, default));
				if (release)
					_orderBooks.Remove(subscription.Pair);
			}
		if (release)
			await SocketClient.ReleaseDepthAsync(subscription.Pair,
				cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_tickSubscriptions.Remove(transactionId, out subscription))
				release = ReleaseReference(_streamReferences,
					new(StreamTypes.Trades, subscription.Pair, default));
		if (release)
			await SocketClient.ReleaseTradesAsync(subscription.Pair,
				cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		CandleSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_candleSubscriptions.Remove(transactionId, out subscription))
				release = ReleaseReference(_streamReferences,
					new(StreamTypes.Candles, subscription.Pair,
						subscription.TimeFrame));
		if (release)
			await SocketClient.ReleaseCandlesAsync(subscription.Pair,
				subscription.TimeFrame.ToCoinDCXInterval(), cancellationToken);
	}

	private async ValueTask OnSocketTradeAsync(CoinDCXWebSocketTrade trade,
		CancellationToken cancellationToken)
	{
		if (trade?.Pair.IsEmpty() != false || trade.Price <= 0 ||
			trade.Quantity <= 0 || !AddPublicTrade(trade.Pair, trade.Timestamp,
				trade.Price, trade.Quantity))
			return;
		var market = GetMarketByPair(trade.Pair);
		KeyValuePair<long, MarketSubscription>[] tickSubscriptions;
		KeyValuePair<long, MarketSubscription>[] level1Subscriptions;
		using (_sync.EnterScope())
		{
			tickSubscriptions = [.. _tickSubscriptions.Where(pair =>
				pair.Value.Pair.EqualsIgnoreCase(market.Pair))];
			level1Subscriptions = [.. _level1Subscriptions.Where(pair =>
				pair.Value.Pair.EqualsIgnoreCase(market.Pair))];
		}
		foreach (var pair in tickSubscriptions)
			await SendPublicTradeAsync(market.Market, market.Pair, trade.Timestamp,
				trade.Price, trade.Quantity, trade.BuyerMaker != 0, pair.Key,
				cancellationToken, false);
		foreach (var pair in level1Subscriptions)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = market.Market.ToStockSharp(),
				ServerTime = trade.Timestamp.FromMilliseconds(CurrentTime),
				OriginalTransactionId = pair.Key,
			}.TryAdd(Level1Fields.LastTradePrice, trade.Price), cancellationToken);
	}

	private async ValueTask OnSocketDepthAsync(CoinDCXWebSocketDepth update,
		bool isSnapshot, CancellationToken cancellationToken)
	{
		if (update?.Pair.IsEmpty() != false)
			return;
		var market = GetMarketByPair(update.Pair);
		KeyValuePair<long, DepthSubscription>[] depthSubscriptions;
		KeyValuePair<long, MarketSubscription>[] level1Subscriptions;
		CoinDCXBookLevel[] bids;
		CoinDCXBookLevel[] asks;
		using (_sync.EnterScope())
		{
			if (!_orderBooks.TryGetValue(market.Pair, out var state))
				return;
			if (isSnapshot)
			{
				state.Bids.Clear();
				state.Asks.Clear();
				ApplyLevels(state.Bids, update.Bids);
				ApplyLevels(state.Asks, update.Asks);
				state.IsInitialized = true;
			}
			else
			{
				if (!state.IsInitialized)
					return;
				if (update.Version > 0 && state.Version > 0 &&
					update.Version <= state.Version)
					return;
				ApplyLevels(state.Bids, update.Bids);
				ApplyLevels(state.Asks, update.Asks);
			}
			state.Version = update.Version;
			bids = [.. state.Bids.Select(static pair => new CoinDCXBookLevel
			{
				Price = pair.Key,
				Volume = pair.Value,
			})];
			asks = [.. state.Asks.Select(static pair => new CoinDCXBookLevel
			{
				Price = pair.Key,
				Volume = pair.Value,
			})];
			depthSubscriptions = [.. _depthSubscriptions.Where(pair =>
				pair.Value.Pair.EqualsIgnoreCase(market.Pair))];
			level1Subscriptions = [.. _level1Subscriptions.Where(pair =>
				pair.Value.Pair.EqualsIgnoreCase(market.Pair))];
		}

		var serverTime = (update.EventTimestamp > 0
			? update.EventTimestamp
			: update.Timestamp).FromMilliseconds(CurrentTime);
		foreach (var pair in depthSubscriptions)
			await SendDepthAsync(market.Market, serverTime,
				ToQuotes(bids, false, pair.Value.Depth),
				ToQuotes(asks, true, pair.Value.Depth), pair.Key,
				cancellationToken);

		var bestBid = bids.FirstOrDefault();
		var bestAsk = asks.FirstOrDefault();
		foreach (var pair in level1Subscriptions)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = market.Market.ToStockSharp(),
				ServerTime = serverTime,
				OriginalTransactionId = pair.Key,
			}
			.TryAdd(Level1Fields.BestBidPrice, bestBid?.Price)
			.TryAdd(Level1Fields.BestBidVolume, bestBid?.Volume)
			.TryAdd(Level1Fields.BestAskPrice, bestAsk?.Price)
			.TryAdd(Level1Fields.BestAskVolume, bestAsk?.Volume), cancellationToken);
	}

	private async ValueTask OnSocketCandleAsync(CoinDCXWebSocketCandle candle,
		CancellationToken cancellationToken)
	{
		if (candle?.Pair.IsEmpty() != false || candle.Interval.IsEmpty())
			return;
		var market = GetMarketByPair(candle.Pair);
		var timeFrame = candle.Interval.ToTimeFrame();
		KeyValuePair<long, CandleSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _candleSubscriptions.Where(pair =>
				pair.Value.Pair.EqualsIgnoreCase(market.Pair) &&
				pair.Value.TimeFrame == timeFrame)];
		foreach (var pair in subscriptions)
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				SecurityId = market.Market.ToStockSharp(),
				OpenTime = candle.OpenTimestamp.FromMilliseconds(CurrentTime),
				CloseTime = candle.CloseTimestamp.FromMilliseconds(CurrentTime),
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				TotalTicks = candle.TradeCount.Min(int.MaxValue).Max(0).To<int>(),
				TypedArg = timeFrame,
				OriginalTransactionId = pair.Key,
				State = candle.IsFinished
					? CandleStates.Finished
					: CandleStates.Active,
			}, cancellationToken);
	}

	private ValueTask SendDepthAsync(string market, DateTime serverTime,
		QuoteChange[] bids, QuoteChange[] asks, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = serverTime,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = bids,
			Asks = asks,
		}, cancellationToken);

	private ValueTask SendPublicTradeAsync(string market, string pair,
		long timestamp, decimal price, decimal quantity, bool isBuyerMaker,
		long transactionId, CancellationToken cancellationToken,
		bool addToDeduplication = true)
	{
		if (addToDeduplication)
			_ = AddPublicTrade(pair, timestamp, price, quantity);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = market.ToStockSharp(),
			ServerTime = timestamp.FromMilliseconds(CurrentTime),
			OriginalTransactionId = transactionId,
			TradeStringId = $"{timestamp}-{price}-{quantity}",
			TradePrice = price,
			TradeVolume = quantity,
			OriginSide = isBuyerMaker ? Sides.Sell : Sides.Buy,
		}, cancellationToken);
	}

	private ValueTask SendCandleAsync(string market, CoinDCXCandle candle,
		TimeSpan timeFrame, long transactionId,
		CancellationToken cancellationToken)
	{
		var openTime = candle.Timestamp.FromMilliseconds(CurrentTime);
		var closeTime = openTime + timeFrame;
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = market.ToStockSharp(),
			OpenTime = openTime,
			CloseTime = closeTime,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.Volume,
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = closeTime <= CurrentTime
				? CandleStates.Finished
				: CandleStates.Active,
		}, cancellationToken);
	}

	private static QuoteChange[] ToQuotes(IEnumerable<CoinDCXBookLevel> levels,
		bool isAsk, int depth)
	{
		var filtered = (levels ?? []).Where(static level => level is not null &&
			level.Price > 0 && level.Volume > 0);
		return [.. (isAsk
			? filtered.OrderBy(static level => level.Price)
			: filtered.OrderByDescending(static level => level.Price))
			.Take(depth).Select(static level =>
				new QuoteChange(level.Price, level.Volume))];
	}

	private static void ApplyLevels(SortedDictionary<decimal, decimal> target,
		IEnumerable<CoinDCXBookLevel> levels)
	{
		foreach (var level in levels ?? [])
		{
			if (level is null || level.Price <= 0)
				continue;
			if (level.Volume <= 0)
				target.Remove(level.Price);
			else
				target[level.Price] = level.Volume;
		}
	}

	private static int GetCandleCount(MarketDataMessage message,
		TimeSpan timeFrame, DateTime to)
	{
		if (message.Count is long count)
			return count.Min(1000).Max(1).To<int>();
		if (message.From is DateTime from && to > from)
			return ((to - from.ToUniversalTime()).Ticks / timeFrame.Ticks + 1)
				.Min(1000L).Max(1L).To<int>();
		return 300;
	}

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
