namespace StockSharp.Bitvavo;

public partial class BitvavoMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;
		BitvavoMarket[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values.OrderBy(static market => market.Market)];

		foreach (var market in markets)
		{
			var security = new SecurityMessage
			{
				SecurityId = market.Market.ToStockSharp(),
				Name = $"{market.Base}/{market.Quote}",
				SecurityType = SecurityTypes.CryptoCurrency,
				PriceStep = market.TickSize,
				VolumeStep = GetVolumeStep(market.QuantityDecimals),
				MinVolume = market.MinimumBaseAmount,
				MaxVolume = market.MaximumBaseAmount,
				OriginalTransactionId = lookupMsg.TransactionId,
			};
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

		var market = GetMarket(mdMsg.SecurityId);
		await SendLevel1SnapshotAsync(market, mdMsg.TransactionId, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		EnsureRealtimeReady();
		var tickerStream = "ticker:" + market;
		var ticker24Stream = "ticker24h:" + market;
		var subscribeTicker = false;
		var subscribeTicker24 = false;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new() { Market = market });
			subscribeTicker = AddReference(_streamReferences, tickerStream);
			subscribeTicker24 = AddReference(_streamReferences, ticker24Stream);
		}

		try
		{
			if (subscribeTicker)
				await WsClient.SubscribeTickerAsync(market, cancellationToken);
			if (subscribeTicker24)
				await WsClient.SubscribeTicker24Async(market, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeLevel1Async(mdMsg.TransactionId, cancellationToken);
			throw;
		}
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

		var market = GetMarket(mdMsg.SecurityId);
		var depth = NormalizeDepth(mdMsg.MaxDepth);
		if (mdMsg.IsHistoryOnly())
		{
			var snapshot = await RestClient.GetDepthAsync(market,
				new() { Depth = depth }, cancellationToken);
			await SendDepthSnapshotAsync(market, snapshot, mdMsg.TransactionId, depth,
				cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		EnsureRealtimeReady();
		var stream = "book:" + market;
		bool subscribe;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Market = market,
				Depth = depth,
			});
			subscribe = AddReference(_streamReferences, stream);
		}

		try
		{
			if (subscribe)
				await WsClient.SubscribeBookAsync(market, cancellationToken);
			var snapshot = await RestClient.GetDepthAsync(market,
				new() { Depth = depth }, cancellationToken);

			await _depthProcessing.WaitAsync(cancellationToken);
			try
			{
				DepthSubscription subscription;
				BitvavoOrderBook[] pending;
				using (_sync.EnterScope())
				{
					if (!_depthSubscriptions.TryGetValue(mdMsg.TransactionId,
						out subscription))
						return;
					pending = [.. subscription.Pending
						.OrderBy(static update => update.Nonce)];
					subscription.Pending.Clear();
					subscription.LastNonce = snapshot.Nonce;
					subscription.IsSnapshotReady = true;
				}

				await SendDepthSnapshotAsync(market, snapshot, mdMsg.TransactionId, depth,
					cancellationToken);
				foreach (var update in pending)
					await ProcessDepthUpdateAsync(mdMsg.TransactionId, subscription, update,
						cancellationToken);
			}
			finally
			{
				_depthProcessing.Release();
			}

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

		var market = GetMarket(mdMsg.SecurityId);
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var from = mdMsg.From?.ToUniversalTime();
		var count = (mdMsg.Count ?? 1000).Min(10000).Max(1).To<int>();
		var trades = await LoadTradesAsync(market, from, to, count, cancellationToken);
		foreach (var trade in trades)
			await SendTradeAsync(market, trade, mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		EnsureRealtimeReady();
		var stream = "trades:" + market;
		bool subscribe;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new() { Market = market });
			subscribe = AddReference(_streamReferences, stream);
		}

		try
		{
			if (subscribe)
				await WsClient.SubscribeTradesAsync(market, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeTicksAsync(mdMsg.TransactionId, cancellationToken);
			throw;
		}
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

		var market = GetMarket(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		if (!BitvavoExtensions.TimeFrames.Contains(timeFrame))
			throw new NotSupportedException(
				$"Bitvavo does not support the {timeFrame} candle interval.");
		if (!mdMsg.IsHistoryOnly() &&
			!BitvavoExtensions.StreamingTimeFrames.Contains(timeFrame))
			throw new NotSupportedException(
				$"Bitvavo WebSocket does not stream the {timeFrame} candle interval.");

		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var count = GetCandleCount(mdMsg, timeFrame, to);
		var from = mdMsg.From?.ToUniversalTime() ??
			to - TimeSpan.FromTicks(timeFrame.Ticks * count);
		var candles = await LoadCandlesAsync(market, timeFrame, from, to, count,
			cancellationToken);
		foreach (var candle in candles)
			await SendCandleAsync(market, candle, timeFrame, mdMsg.TransactionId,
				cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		EnsureRealtimeReady();
		var interval = timeFrame.ToBitvavoInterval();
		var stream = $"candles:{interval}:{market}";
		bool subscribe;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Market = market,
				TimeFrame = timeFrame,
			});
			subscribe = AddReference(_streamReferences, stream);
		}

		try
		{
			if (subscribe)
				await WsClient.SubscribeCandlesAsync(market, interval,
					cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeCandlesAsync(mdMsg.TransactionId, cancellationToken);
			throw;
		}
	}

	private async ValueTask SendLevel1SnapshotAsync(string market, long transactionId,
		CancellationToken cancellationToken)
	{
		var ticker = await RestClient.GetTickerAsync(market, cancellationToken);
		if (ticker is null)
			throw new InvalidDataException(
				$"Bitvavo returned no ticker data for '{market}'.");
		await SendOutMessageAsync(CreateLevel1Message(ticker, transactionId),
			cancellationToken);
	}

	private Level1ChangeMessage CreateLevel1Message(BitvavoTicker ticker,
		long transactionId)
	{
		BitvavoMarketStatuses? status;
		using (_sync.EnterScope())
			status = _markets.TryGetValue(ticker.Market, out var market)
				? market.Status
				: null;
		return new Level1ChangeMessage
		{
			SecurityId = ticker.Market.ToStockSharp(),
			ServerTime = ticker.Timestamp is > 0
				? ticker.Timestamp.Value.FromMilliseconds()
				: CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, ticker.Bid)
		.TryAdd(Level1Fields.BestBidVolume, ticker.BidSize)
		.TryAdd(Level1Fields.BestAskPrice, ticker.Ask)
		.TryAdd(Level1Fields.BestAskVolume, ticker.AskSize)
		.TryAdd(Level1Fields.LastTradePrice, ticker.Last)
		.TryAdd(Level1Fields.OpenPrice, ticker.Open)
		.TryAdd(Level1Fields.HighPrice, ticker.High)
		.TryAdd(Level1Fields.LowPrice, ticker.Low)
		.TryAdd(Level1Fields.Volume, ticker.Volume)
		.TryAdd(Level1Fields.Turnover, ticker.QuoteVolume)
		.TryAdd(Level1Fields.State, status.ToStockSharp());
	}

	private async ValueTask<BitvavoPublicTrade[]> LoadTradesAsync(string market,
		DateTime? from, DateTime to, int count, CancellationToken cancellationToken)
	{
		var result = new List<BitvavoPublicTrade>();
		var lowerBound = from ?? to - TimeSpan.FromDays(1);
		var cursorEnd = to;
		while (result.Count < count && cursorEnd >= lowerBound)
		{
			var windowStart = cursorEnd - TimeSpan.FromDays(1);
			if (windowStart < lowerBound)
				windowStart = lowerBound;
			var pageSize = (count - result.Count).Min(1000).Max(1);
			var page = await RestClient.GetPublicTradesAsync(market, new()
			{
				Limit = pageSize,
				Start = windowStart.ToMilliseconds(),
				End = cursorEnd.ToMilliseconds(),
			}, cancellationToken);
			if (page is not { Length: > 0 })
			{
				cursorEnd = windowStart.AddMilliseconds(-1);
				continue;
			}
			result.AddRange(page.Where(trade =>
			{
				var time = GetTradeTime(trade);
				return time >= lowerBound && time <= to;
			}));
			var earliest = page.Min(GetTradeTime);
			cursorEnd = page.Length >= pageSize && earliest > windowStart
				? earliest.AddMilliseconds(-1)
				: windowStart.AddMilliseconds(-1);
		}

		return [.. result.Where(static trade => !trade.Id.IsEmpty())
			.GroupBy(static trade => trade.Id, StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.First())
			.OrderBy(GetTradeTime)
			.TakeLast(count)];
	}

	private async ValueTask<BitvavoCandle[]> LoadCandlesAsync(string market,
		TimeSpan timeFrame, DateTime from, DateTime to, int count,
		CancellationToken cancellationToken)
	{
		var result = new List<BitvavoCandle>();
		var lowerBound = from.ToUniversalTime();
		var cursorEnd = to.ToUniversalTime();
		while (result.Count < count && cursorEnd >= lowerBound)
		{
			var pageSize = (count - result.Count).Min(1440).Max(1);
			var pageStart = cursorEnd - TimeSpan.FromTicks(timeFrame.Ticks * pageSize);
			if (pageStart < lowerBound)
				pageStart = lowerBound;
			var page = await RestClient.GetCandlesAsync(market, new()
			{
				Interval = timeFrame.ToBitvavoInterval(),
				Limit = pageSize,
				Start = pageStart.ToAlignedMilliseconds(timeFrame),
				End = cursorEnd.ToAlignedMilliseconds(timeFrame),
			}, cancellationToken);
			if (page is not { Length: > 0 })
				break;
			result.AddRange(page.Where(candle =>
			{
				var openTime = candle.Timestamp.FromMilliseconds();
				return openTime >= lowerBound && openTime <= to;
			}));
			var earliest = page.Min(static candle => candle.Timestamp)
				.FromMilliseconds();
			if (earliest <= lowerBound || page.Length < pageSize)
				break;
			cursorEnd = earliest - timeFrame;
		}

		return [.. result.GroupBy(static candle => candle.Timestamp)
			.Select(static group => group.First())
			.OrderBy(static candle => candle.Timestamp)
			.TakeLast(count)];
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var unsubscribeTicker = false;
		var unsubscribeTicker24 = false;
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(transactionId, out subscription))
			{
				unsubscribeTicker = ReleaseReference(_streamReferences,
					"ticker:" + subscription.Market);
				unsubscribeTicker24 = ReleaseReference(_streamReferences,
					"ticker24h:" + subscription.Market);
			}
		if (_wsClient is null || subscription is null)
			return;
		if (unsubscribeTicker)
			await _wsClient.UnsubscribeTickerAsync(subscription.Market, cancellationToken);
		if (unsubscribeTicker24)
			await _wsClient.UnsubscribeTicker24Async(subscription.Market,
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
					"book:" + subscription.Market);
		if (unsubscribe && _wsClient is not null)
			await _wsClient.UnsubscribeBookAsync(subscription.Market, cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
			if (_tickSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences,
					"trades:" + subscription.Market);
		if (unsubscribe && _wsClient is not null)
			await _wsClient.UnsubscribeTradesAsync(subscription.Market, cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		CandleSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
			if (_candleSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences,
					$"candles:{subscription.TimeFrame.ToBitvavoInterval()}:{subscription.Market}");
		if (unsubscribe && _wsClient is not null)
			await _wsClient.UnsubscribeCandlesAsync(subscription.Market,
				subscription.TimeFrame.ToBitvavoInterval(), cancellationToken);
	}

	private async ValueTask OnTickerAsync(BitvavoWsTicker ticker,
		CancellationToken cancellationToken)
	{
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _level1Subscriptions
				.Where(pair => pair.Value.Market.EqualsIgnoreCase(ticker.Market))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = ticker.Market.ToStockSharp(),
				ServerTime = CurrentTime,
				OriginalTransactionId = id,
			}
			.TryAdd(Level1Fields.BestBidPrice, ticker.BestBid)
			.TryAdd(Level1Fields.BestBidVolume, ticker.BestBidSize)
			.TryAdd(Level1Fields.BestAskPrice, ticker.BestAsk)
			.TryAdd(Level1Fields.BestAskVolume, ticker.BestAskSize)
			.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice), cancellationToken);
	}

	private async ValueTask OnTicker24Async(BitvavoTicker ticker,
		CancellationToken cancellationToken)
	{
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _level1Subscriptions
				.Where(pair => pair.Value.Market.EqualsIgnoreCase(ticker.Market))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendOutMessageAsync(CreateLevel1Message(ticker, id), cancellationToken);
	}

	private async ValueTask OnTradeAsync(BitvavoPublicTrade trade,
		CancellationToken cancellationToken)
	{
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _tickSubscriptions
				.Where(pair => pair.Value.Market.EqualsIgnoreCase(trade.Market))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendTradeAsync(trade.Market, trade, id, cancellationToken);
	}

	private async ValueTask OnCandlesAsync(BitvavoWsCandles update,
		CancellationToken cancellationToken)
	{
		var timeFrame = update.Interval.ToTimeFrame();
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _candleSubscriptions
				.Where(pair => pair.Value.Market.EqualsIgnoreCase(update.Market) &&
					pair.Value.TimeFrame == timeFrame)
				.Select(static pair => pair.Key)];
		foreach (var candle in update.Candles ?? [])
			foreach (var id in ids)
				await SendCandleAsync(update.Market, candle, timeFrame, id,
					cancellationToken);
	}

	private async ValueTask OnBookAsync(BitvavoOrderBook update,
		CancellationToken cancellationToken)
	{
		await _depthProcessing.WaitAsync(cancellationToken);
		try
		{
			(long Id, DepthSubscription Subscription)[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _depthSubscriptions
					.Where(pair => pair.Value.Market.EqualsIgnoreCase(update.Market))
					.Select(static pair => (pair.Key, pair.Value))];
			foreach (var item in subscriptions)
			{
				if (!item.Subscription.IsSnapshotReady)
				{
					using (_sync.EnterScope())
						if (_depthSubscriptions.ContainsKey(item.Id))
							item.Subscription.Pending.Add(update);
					continue;
				}
				await ProcessDepthUpdateAsync(item.Id, item.Subscription, update,
					cancellationToken);
			}
		}
		finally
		{
			_depthProcessing.Release();
		}
	}

	private async ValueTask ProcessDepthUpdateAsync(long transactionId,
		DepthSubscription subscription, BitvavoOrderBook update,
		CancellationToken cancellationToken)
	{
		if (update.Nonce <= subscription.LastNonce)
			return;
		if (update.Nonce != subscription.LastNonce + 1)
		{
			await ResynchronizeDepthAsync(transactionId, subscription, cancellationToken);
			if (update.Nonce <= subscription.LastNonce ||
				update.Nonce != subscription.LastNonce + 1)
				return;
		}

		subscription.LastNonce = update.Nonce;
		await SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = subscription.Market.ToStockSharp(),
			ServerTime = GetBookTime(update),
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.Increment,
			SeqNum = update.Nonce,
			Bids = ToQuotes(update.Bids, false, int.MaxValue),
			Asks = ToQuotes(update.Asks, true, int.MaxValue),
		}, cancellationToken);
	}

	private async ValueTask ResynchronizeDepthAsync(long transactionId,
		DepthSubscription subscription, CancellationToken cancellationToken)
	{
		var snapshot = await RestClient.GetDepthAsync(subscription.Market,
			new() { Depth = subscription.Depth }, cancellationToken);
		subscription.LastNonce = snapshot.Nonce;
		subscription.IsSnapshotReady = true;
		await SendDepthSnapshotAsync(subscription.Market, snapshot, transactionId,
			subscription.Depth, cancellationToken);
	}

	private async ValueTask ResynchronizeAllDepthsAsync(CancellationToken cancellationToken)
	{
		await _depthProcessing.WaitAsync(cancellationToken);
		try
		{
			(long Id, DepthSubscription Subscription)[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _depthSubscriptions
					.Select(static pair => (pair.Key, pair.Value))];
			foreach (var item in subscriptions)
				await ResynchronizeDepthAsync(item.Id, item.Subscription,
					cancellationToken);
		}
		finally
		{
			_depthProcessing.Release();
		}
	}

	private ValueTask SendDepthSnapshotAsync(string market, BitvavoOrderBook snapshot,
		long transactionId, int depth, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = GetBookTime(snapshot),
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			SeqNum = snapshot.Nonce,
			Bids = ToQuotes(snapshot.Bids, false, depth),
			Asks = ToQuotes(snapshot.Asks, true, depth),
		}, cancellationToken);

	private ValueTask SendTradeAsync(string market, BitvavoPublicTrade trade,
		long transactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = market.ToStockSharp(),
			ServerTime = GetTradeTime(trade),
			OriginalTransactionId = transactionId,
			TradeStringId = trade.Id,
			TradePrice = trade.Price,
			TradeVolume = trade.Amount,
			OriginSide = trade.Side.ToStockSharp(),
		}, cancellationToken);

	private ValueTask SendCandleAsync(string market, BitvavoCandle candle,
		TimeSpan timeFrame, long transactionId, CancellationToken cancellationToken)
	{
		var openTime = candle.Timestamp.FromMilliseconds();
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

	private static QuoteChange[] ToQuotes(BitvavoPriceLevel[] levels,
		bool isAscending, int depth)
	{
		var ordered = isAscending
			? (levels ?? []).OrderBy(static level => level.Price)
			: (levels ?? []).OrderByDescending(static level => level.Price);
		return [.. ordered.Take(depth)
			.Select(static level => new QuoteChange(level.Price, level.Size))];
	}

	private DateTime GetBookTime(BitvavoOrderBook book)
		=> book.TimestampNanoseconds is > 0
			? book.TimestampNanoseconds.Value.FromNanoseconds()
			: CurrentTime;

	private DateTime GetTradeTime(BitvavoPublicTrade trade)
		=> trade.TimestampNanoseconds is > 0
			? trade.TimestampNanoseconds.Value.FromNanoseconds()
			: trade.Timestamp > 0 ? trade.Timestamp.FromMilliseconds() : CurrentTime;

	private static decimal? GetVolumeStep(int? decimals)
	{
		if (decimals is null or < 0 or > 28)
			return null;
		var step = 1m;
		for (var i = 0; i < decimals.Value; i++)
			step /= 10m;
		return step;
	}

	private static int GetCandleCount(MarketDataMessage message, TimeSpan timeFrame,
		DateTime to)
	{
		if (message.Count is long count)
			return count.Min(10000).Max(1).To<int>();
		if (message.From is DateTime from && to > from)
			return ((to - from.ToUniversalTime()).Ticks / timeFrame.Ticks + 1)
				.Min(10000L).Max(1L).To<int>();
		return 300;
	}
}
