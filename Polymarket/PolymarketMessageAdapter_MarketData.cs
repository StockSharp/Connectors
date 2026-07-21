namespace StockSharp.Polymarket;

public partial class PolymarketMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = Math.Max(0, lookupMsg.Count ?? long.MaxValue);
		foreach (var market in GetMarkets().OrderBy(static market =>
			market.SecurityCode, StringComparer.OrdinalIgnoreCase))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.Polymarket))
				continue;
			if (!lookupMsg.SecurityId.SecurityCode.IsEmpty() &&
				!lookupMsg.SecurityId.SecurityCode.Equals(market.SecurityCode,
					StringComparison.OrdinalIgnoreCase))
				continue;
			if (securityTypes.Count > 0 &&
				!securityTypes.Contains(SecurityTypes.Option))
				continue;
			var security = CreateSecurity(market, lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			if (skip > 0)
			{
				skip--;
				continue;
			}
			if (left <= 0)
				break;
			await SendOutMessageAsync(security, cancellationToken);
			left--;
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
		if (mdMsg.From is not null || mdMsg.To is not null)
			throw new NotSupportedException(
				"Polymarket does not publish historical Level1 changes.");
		var market = GetMarket(mdMsg.SecurityId);
		var book = await RestClient.GetBookAsync(market.TokenId,
			cancellationToken);
		var state = ApplyBook(book, market);
		await SendLevel1Async(market, state, mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		var subscribe = false;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				TokenId = market.TokenId,
			});
			subscribe = AddSocketReference(market.TokenId);
		}
		try
		{
			if (subscribe)
				await SocketClient.SubscribeMarketAsync(market.TokenId,
					cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_level1Subscriptions.Remove(mdMsg.TransactionId);
				ReleaseSocketReference(market.TokenId);
			}
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
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
		if (mdMsg.From is not null || mdMsg.To is not null)
			throw new NotSupportedException(
				"Polymarket does not publish historical order books.");
		var market = GetMarket(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? MarketDepth).Max(1).Min(MarketDepth);
		var book = await RestClient.GetBookAsync(market.TokenId,
			cancellationToken);
		var state = ApplyBook(book, market);
		await SendDepthAsync(market, state, mdMsg.TransactionId, depth,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		var subscribe = false;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				TokenId = market.TokenId,
				Depth = depth,
			});
			subscribe = AddSocketReference(market.TokenId);
		}
		try
		{
			if (subscribe)
				await SocketClient.SubscribeMarketAsync(market.TokenId,
					cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_depthSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseSocketReference(market.TokenId);
			}
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
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
		if (mdMsg.From is not null || mdMsg.To is not null)
			throw new NotSupportedException(
				"Polymarket CLOB exposes outcome price history, not historical " +
				"public trade ticks.");
		var market = GetMarket(mdMsg.SecurityId);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		var subscribe = false;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				TokenId = market.TokenId,
			});
			subscribe = AddSocketReference(market.TokenId);
		}
		try
		{
			if (subscribe)
				await SocketClient.SubscribeMarketAsync(market.TokenId,
					cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_tickSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseSocketReference(market.TokenId);
			}
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
			return;
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		var market = GetMarket(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		if (!_timeFrames.Contains(timeFrame))
			throw new NotSupportedException(
				$"Polymarket does not support the {timeFrame} candle interval.");
		var maximum = (mdMsg.Count ?? HistoryLimit).Min(HistoryLimit).Max(1)
			.To<int>();
		var now = ServerTime;
		var to = (mdMsg.To ?? now).EnsureUtc().Min(now);
		var from = mdMsg.From?.EnsureUtc() ?? new DateTime(
			Math.Max(DateTime.UnixEpoch.Ticks,
				to.Ticks - checked(timeFrame.Ticks * (long)maximum)),
			DateTimeKind.Utc);
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg),
				"Polymarket candle start time cannot be later than end time.");
		var fidelity = Math.Ceiling(timeFrame.TotalMinutes).To<int>()
			.Max(1).Min(1440);
		var response = await RestClient.GetPriceHistoryAsync(market.TokenId,
			from, to, fidelity, cancellationToken);
		var candles = AggregateCandles(response?.History, timeFrame, from, to)
			.TakeLast(maximum).ToArray();
		foreach (var candle in candles)
			await SendCandleAsync(market, candle, timeFrame,
				mdMsg.TransactionId, cancellationToken);
		await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
	}

	private static SecurityMessage CreateSecurity(PolymarketMarket market,
		long originalTransactionId)
		=> new SecurityMessage
		{
			SecurityId = market.ToStockSharp(),
			Name = market.Question.IsEmpty()
				? market.SecurityCode
				: market.Question + " — " + market.Outcome,
			ShortName = market.Outcome,
			Class = "PREDICTION",
			SecurityType = SecurityTypes.Option,
			BinaryOptionType = market.Outcome,
			Currency = CurrencyTypes.USD,
			ExpiryDate = market.ExpiryDate,
			PriceStep = market.PriceStep,
			VolumeStep = 0.01m,
			MinVolume = market.MinimumVolume,
			Multiplier = 1m,
			OriginalTransactionId = originalTransactionId,
		}.TryFillUnderlyingId(market.Slug);

	private PolymarketBookState ApplyBook(PolymarketOrderBook book,
		PolymarketMarket market)
	{
		ArgumentNullException.ThrowIfNull(book);
		if (!book.AssetId.IsEmpty() && !book.AssetId.Equals(market.TokenId,
			StringComparison.Ordinal))
			throw new InvalidDataException(
				"Polymarket returned an order book for another token.");
		PolymarketBookState state;
		using (_sync.EnterScope())
		{
			if (!_books.TryGetValue(market.TokenId, out state))
				_books[market.TokenId] = state = new();
			state.Apply(book);
			var tick = book.TickSize.TryParsePolymarketDecimal();
			if (tick is > 0)
				market.PriceStep = tick.Value;
			var last = book.LastTradePrice.TryParsePolymarketDecimal();
			if (last is > 0)
				market.ReferencePrice = last.Value;
		}
		UpdateServerTime(state.Time);
		return state;
	}

	private ValueTask SendDepthAsync(PolymarketMarket market,
		PolymarketBookState state, long transactionId, int depth,
		CancellationToken cancellationToken)
	{
		QuoteChange[] bids;
		QuoteChange[] asks;
		DateTime time;
		using (_sync.EnterScope())
		{
			bids = [.. state.Bids.Take(depth).Select(static pair =>
				new QuoteChange(pair.Key, pair.Value))];
			asks = [.. state.Asks.Take(depth).Select(static pair =>
				new QuoteChange(pair.Key, pair.Value))];
			time = state.Time == default ? ServerTime : state.Time;
		}
		UpdateServerTime(time);
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = bids,
			Asks = asks,
		}, cancellationToken);
	}

	private ValueTask SendLevel1Async(PolymarketMarket market,
		PolymarketBookState state, long transactionId,
		CancellationToken cancellationToken)
	{
		decimal? bidPrice;
		decimal? bidVolume;
		decimal? askPrice;
		decimal? askVolume;
		DateTime time;
		using (_sync.EnterScope())
		{
			var bid = state.Bids.FirstOrDefault();
			var ask = state.Asks.FirstOrDefault();
			bidPrice = bid.Key > 0 ? bid.Key : null;
			bidVolume = bid.Key > 0 ? bid.Value : null;
			askPrice = ask.Key > 0 ? ask.Key : null;
			askVolume = ask.Key > 0 ? ask.Value : null;
			time = state.Time == default ? ServerTime : state.Time;
		}
		return SendLevel1Async(market, time, transactionId, bidPrice,
			bidVolume, askPrice, askVolume, null, null, cancellationToken);
	}

	private ValueTask SendLevel1Async(PolymarketMarket market, DateTime time,
		long transactionId, decimal? bidPrice, decimal? bidVolume,
		decimal? askPrice, decimal? askVolume, decimal? lastPrice,
		decimal? lastVolume, CancellationToken cancellationToken)
	{
		UpdateServerTime(time);
		if (lastPrice is > 0)
			market.ReferencePrice = lastPrice.Value;
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, bidPrice)
		.TryAdd(Level1Fields.BestBidVolume, bidVolume)
		.TryAdd(Level1Fields.BestAskPrice, askPrice)
		.TryAdd(Level1Fields.BestAskVolume, askVolume)
		.TryAdd(Level1Fields.LastTradePrice, lastPrice)
		.TryAdd(Level1Fields.LastTradeVolume, lastVolume), cancellationToken);
	}

	private async ValueTask OnSocketEventAsync(PolymarketSocketEvent message,
		CancellationToken cancellationToken)
	{
		if (message?.EventType is not PolymarketSocketEventTypes eventType)
			return;
		switch (eventType)
		{
			case PolymarketSocketEventTypes.Book:
				await OnBookAsync(message, cancellationToken);
				break;
			case PolymarketSocketEventTypes.PriceChange:
				await OnPriceChangeAsync(message, cancellationToken);
				break;
			case PolymarketSocketEventTypes.LastTradePrice:
				await OnLastTradeAsync(message, cancellationToken);
				break;
			case PolymarketSocketEventTypes.BestBidAsk:
				await OnBestBidAskAsync(message, cancellationToken);
				break;
			case PolymarketSocketEventTypes.TickSizeChange:
				await OnTickSizeChangeAsync(message, cancellationToken);
				break;
			case PolymarketSocketEventTypes.Order:
				await OnUserOrderAsync(message, cancellationToken);
				break;
			case PolymarketSocketEventTypes.Trade:
				await OnUserTradeAsync(message, cancellationToken);
				break;
			case PolymarketSocketEventTypes.NewMarket:
			case PolymarketSocketEventTypes.MarketResolved:
				using (_sync.EnterScope())
					_nextMarketRefresh = CurrentTime;
				break;
			default:
				throw new InvalidDataException(
					$"Unsupported Polymarket WebSocket event '{eventType}'.");
		}
	}

	private async ValueTask OnBookAsync(PolymarketSocketEvent message,
		CancellationToken cancellationToken)
	{
		var market = GetMarketByToken(message.AssetId);
		if (market is null)
			return;
		var state = ApplyBook(new()
		{
			Market = message.Market,
			AssetId = message.AssetId,
			Timestamp = message.Timestamp,
			Hash = message.Hash,
			Bids = message.Bids,
			Asks = message.Asks,
			MinimumOrderSize = message.MinimumOrderSize,
			TickSize = message.TickSize,
			IsNegativeRisk = message.IsNegativeRisk == true,
			LastTradePrice = message.LastTradePrice,
		}, market);
		await PublishBookAsync(market, state, cancellationToken);
	}

	private async ValueTask OnPriceChangeAsync(PolymarketSocketEvent message,
		CancellationToken cancellationToken)
	{
		var time = message.Timestamp.ParsePolymarketMilliseconds();
		foreach (var change in message.PriceChanges ?? [])
		{
			var market = GetMarketByToken(change?.AssetId);
			if (market is null)
				continue;
			PolymarketBookState state;
			using (_sync.EnterScope())
			{
				if (!_books.TryGetValue(market.TokenId, out state))
					continue;
				state.Apply(change, time);
			}
			await PublishBookAsync(market, state, cancellationToken);
		}
	}

	private async ValueTask PublishBookAsync(PolymarketMarket market,
		PolymarketBookState state, CancellationToken cancellationToken)
	{
		PolymarketMarketSubscription[] level1;
		PolymarketDepthSubscription[] depths;
		using (_sync.EnterScope())
		{
			level1 = [.. _level1Subscriptions.Values.Where(subscription =>
				subscription.TokenId == market.TokenId)];
			depths = [.. _depthSubscriptions.Values.Where(subscription =>
				subscription.TokenId == market.TokenId)];
		}
		foreach (var subscription in level1)
			await SendLevel1Async(market, state, subscription.TransactionId,
				cancellationToken);
		foreach (var subscription in depths)
			await SendDepthAsync(market, state, subscription.TransactionId,
				subscription.Depth, cancellationToken);
	}

	private async ValueTask OnLastTradeAsync(PolymarketSocketEvent message,
		CancellationToken cancellationToken)
	{
		var market = GetMarketByToken(message.AssetId);
		if (market is null)
			return;
		var price = message.Price.ParsePolymarketDecimal("trade price");
		var volume = message.Size.TryParsePolymarketDecimal();
		var time = message.Timestamp.ParsePolymarketMilliseconds();
		UpdateServerTime(time);
		PolymarketMarketSubscription[] ticks;
		PolymarketMarketSubscription[] level1;
		using (_sync.EnterScope())
		{
			ticks = [.. _tickSubscriptions.Values.Where(subscription =>
				subscription.TokenId == market.TokenId)];
			level1 = [.. _level1Subscriptions.Values.Where(subscription =>
				subscription.TokenId == market.TokenId)];
		}
		var tradeId = !message.TransactionHash.IsEmpty()
			? message.TransactionHash + ":" + market.TokenId
			: "ws:" + market.TokenId + ":" + message.Timestamp + ":" +
				message.Price + ":" + message.Size + ":" + message.Side;
		foreach (var subscription in ticks)
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = market.ToStockSharp(),
				ServerTime = time,
				TradeStringId = tradeId,
				TradePrice = price,
				TradeVolume = volume,
				OriginSide = message.Side.ToStockSharp(),
				OriginalTransactionId = subscription.TransactionId,
			}, cancellationToken);
		foreach (var subscription in level1)
			await SendLevel1Async(market, time, subscription.TransactionId,
				null, null, null, null, price, volume, cancellationToken);
	}

	private async ValueTask OnBestBidAskAsync(PolymarketSocketEvent message,
		CancellationToken cancellationToken)
	{
		var market = GetMarketByToken(message.AssetId);
		if (market is null)
			return;
		var time = message.Timestamp.ParsePolymarketMilliseconds();
		PolymarketMarketSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions.Values.Where(subscription =>
				subscription.TokenId == market.TokenId)];
		foreach (var subscription in subscriptions)
			await SendLevel1Async(market, time, subscription.TransactionId,
				message.BestBid.TryParsePolymarketDecimal(), null,
				message.BestAsk.TryParsePolymarketDecimal(), null, null, null,
				cancellationToken);
	}

	private async ValueTask OnTickSizeChangeAsync(
		PolymarketSocketEvent message, CancellationToken cancellationToken)
	{
		var market = GetMarketByToken(message.AssetId);
		if (market is null)
			return;
		var tick = message.NewTickSize.ParsePolymarketDecimal("tick size");
		if (tick <= 0)
			throw new InvalidDataException(
				"Polymarket returned a non-positive tick size.");
		market.PriceStep = tick;
		PolymarketMarketSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions.Values.Where(subscription =>
				subscription.TokenId == market.TokenId)];
		foreach (var subscription in subscriptions)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = market.ToStockSharp(),
				ServerTime = message.Timestamp.ParsePolymarketMilliseconds(),
				OriginalTransactionId = subscription.TransactionId,
			}.TryAdd(Level1Fields.PriceStep, tick), cancellationToken);
	}

	private static IEnumerable<PolymarketCandle> AggregateCandles(
		PolymarketPricePoint[] points, TimeSpan timeFrame, DateTime from,
		DateTime to)
		=> (points ?? [])
			.Where(static point => point is not null && point.Timestamp > 0 &&
				point.Price is >= 0 and <= 1)
			.Select(point => new
			{
				Point = point,
				Time = DateTime.UnixEpoch.AddSeconds(point.Timestamp),
			})
			.Where(item => item.Time >= from && item.Time <= to)
			.OrderBy(static item => item.Time)
			.GroupBy(item => new DateTime(item.Time.Ticks -
				item.Time.Ticks % timeFrame.Ticks, DateTimeKind.Utc))
			.Select(static group => new PolymarketCandle
			{
				OpenTime = group.Key,
				Open = group.First().Point.Price,
				High = group.Max(static item => item.Point.Price),
				Low = group.Min(static item => item.Point.Price),
				Close = group.Last().Point.Price,
			});

	private ValueTask SendCandleAsync(PolymarketMarket market,
		PolymarketCandle candle, TimeSpan timeFrame, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = market.ToStockSharp(),
			OpenTime = candle.OpenTime,
			CloseTime = candle.OpenTime + timeFrame,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = 0m,
			TotalTicks = 0,
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = CandleStates.Finished,
		}, cancellationToken);

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		PolymarketMarketSubscription removed = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(transactionId, out removed))
				unsubscribe = ReleaseSocketReference(removed.TokenId);
		if (unsubscribe)
			await SocketClient.UnsubscribeMarketAsync(removed.TokenId,
				cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		PolymarketDepthSubscription removed = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
			if (_depthSubscriptions.Remove(transactionId, out removed))
				unsubscribe = ReleaseSocketReference(removed.TokenId);
		if (unsubscribe)
			await SocketClient.UnsubscribeMarketAsync(removed.TokenId,
				cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		PolymarketMarketSubscription removed = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
			if (_tickSubscriptions.Remove(transactionId, out removed))
				unsubscribe = ReleaseSocketReference(removed.TokenId);
		if (unsubscribe)
			await SocketClient.UnsubscribeMarketAsync(removed.TokenId,
				cancellationToken);
	}

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
