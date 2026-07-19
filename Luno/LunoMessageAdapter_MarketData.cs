namespace StockSharp.Luno;

public partial class LunoMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var requestedSymbol = lookupMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: lookupMsg.SecurityId.SecurityCode.NormalizeSymbol();
		MarketDefinition[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values];

		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var market in markets.OrderBy(static value => value.Symbol,
			StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.Luno))
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
				"Luno does not expose historical Level1 events.");

		var market = GetMarket(mdMsg.SecurityId);
		var ticker = await RestClient.GetTickerAsync(market.Symbol,
			cancellationToken);
		await SendTickerAsync(market, ticker, mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var key = new StreamKey(StreamTypes.Level1, market.Symbol);
		bool acquire;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = market.Symbol,
			});
			acquire = AddReference(_streamReferences, key);
		}
		try
		{
			if (acquire)
				await AcquireMarketStreamAsync(market.Symbol, cancellationToken);
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
				"Luno does not expose historical order-book events.");

		var market = GetMarket(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? 100).Min(100).Max(1);
		var book = await RestClient.GetOrderBookAsync(market.Symbol,
			cancellationToken);
		await SendBookAsync(market, book, depth, mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var key = new StreamKey(StreamTypes.OrderBook, market.Symbol);
		bool acquire;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = market.Symbol,
				Depth = depth,
			});
			acquire = AddReference(_streamReferences, key);
		}
		try
		{
			if (acquire)
				await AcquireMarketStreamAsync(market.Symbol, cancellationToken);
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
		var now = DateTime.UtcNow;
		var oldest = now.AddHours(-24);
		var from = mdMsg.From?.ToUniversalTime();
		if (from < oldest)
			from = oldest;
		var to = (mdMsg.To ?? now).ToUniversalTime();
		var maximum = (mdMsg.Count ?? 100).Min(100).Max(1).To<int>();
		var trades = await RestClient.GetPublicTradesAsync(new()
		{
			Pair = market.Symbol,
			Since = from is null
				? null
				: new DateTimeOffset(from.Value).ToUnixTimeMilliseconds(),
		}, cancellationToken);
		foreach (var trade in trades.Where(trade => trade is not null &&
			trade.Timestamp.ToLunoTime(DateTime.MinValue) <= to)
			.OrderBy(static trade => trade.Timestamp).TakeLast(maximum))
			await SendPublicTradeAsync(market, trade, mdMsg.TransactionId,
				cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var key = new StreamKey(StreamTypes.Trade, market.Symbol);
		bool acquire;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = market.Symbol,
			});
			acquire = AddReference(_streamReferences, key);
		}
		try
		{
			if (acquire)
				await AcquireMarketStreamAsync(market.Symbol, cancellationToken);
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
		EnsurePrivateReady();
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
		var duration = timeFrame.ToLunoDuration();
		var now = DateTime.UtcNow;
		var to = (mdMsg.To ?? now).ToUniversalTime();
		if (to > now)
			to = now;
		var count = GetCandleCount(mdMsg, timeFrame, to);
		var from = mdMsg.From?.ToUniversalTime() ??
			to - TimeSpan.FromTicks(timeFrame.Ticks * count);
		var candles = await DownloadCandlesAsync(market, duration, timeFrame,
			from, to, count, cancellationToken);
		foreach (var candle in candles)
			await SendCandleAsync(market, candle, timeFrame,
				mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var key = new StreamKey(StreamTypes.Candle, market.Symbol);
		bool acquire;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = market.Symbol,
				TimeFrame = timeFrame,
			});
			acquire = AddReference(_streamReferences, key);
		}
		try
		{
			if (acquire)
				await AcquireMarketStreamAsync(market.Symbol, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeCandlesAsync(mdMsg.TransactionId,
				cancellationToken);
			throw;
		}
	}

	private SecurityMessage CreateSecurity(MarketDefinition market,
		long originalTransactionId)
		=> new()
		{
			SecurityId = market.Symbol.ToStockSharp(),
			Name = $"{market.BaseAsset}/{market.QuoteAsset}",
			ShortName = $"{market.BaseAsset}/{market.QuoteAsset}",
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = market.QuoteAsset.ToCurrency(),
			PriceStep = market.PriceStep > 0 ? market.PriceStep : null,
			VolumeStep = market.VolumeStep > 0 ? market.VolumeStep : null,
			MinVolume = market.MinimumVolume > 0
				? market.MinimumVolume
				: null,
			MaxVolume = market.MaximumVolume > 0
				? market.MaximumVolume
				: null,
			OriginalTransactionId = originalTransactionId,
		};

	private async ValueTask<LunoCandle[]> DownloadCandlesAsync(
		MarketDefinition market, int duration, TimeSpan timeFrame, DateTime from,
		DateTime to, int count, CancellationToken cancellationToken)
	{
		var values = new SortedDictionary<DateTime, LunoCandle>();
		var cursor = from;
		while (values.Count < count && cursor <= to)
		{
			var page = await RestClient.GetCandlesAsync(new()
			{
				Pair = market.Symbol,
				Since = new DateTimeOffset(cursor).ToUnixTimeMilliseconds(),
				Duration = duration,
			}, cancellationToken);
			foreach (var candle in page)
			{
				if (candle is null)
					continue;
				var openTime = candle.Timestamp.ToLunoTime(DateTime.MinValue);
				if (openTime >= from && openTime <= to)
					values[openTime] = candle;
			}
			if (page.Length == 0)
				break;
			var last = page.Max(static candle => candle.Timestamp)
				.ToLunoTime(cursor);
			var next = last + timeFrame;
			if (next <= cursor || page.Length < 1000)
				break;
			cursor = next;
		}
		return [.. values.Values.TakeLast(count)];
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(transactionId, out subscription))
				release = ReleaseReference(_streamReferences,
					new(StreamTypes.Level1, subscription.Symbol));
		if (release)
			await ReleaseMarketStreamAsync(subscription.Symbol, cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_depthSubscriptions.Remove(transactionId, out subscription))
				release = ReleaseReference(_streamReferences,
					new(StreamTypes.OrderBook, subscription.Symbol));
		if (release)
			await ReleaseMarketStreamAsync(subscription.Symbol, cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_tickSubscriptions.Remove(transactionId, out subscription))
				release = ReleaseReference(_streamReferences,
					new(StreamTypes.Trade, subscription.Symbol));
		if (release)
			await ReleaseMarketStreamAsync(subscription.Symbol, cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		CandleSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_candleSubscriptions.Remove(transactionId, out subscription))
				release = ReleaseReference(_streamReferences,
					new(StreamTypes.Candle, subscription.Symbol));
		if (release)
			await ReleaseMarketStreamAsync(subscription.Symbol, cancellationToken);
	}

	private async ValueTask OnMarketStreamStateAsync(
		LunoMarketStreamState state, CancellationToken cancellationToken)
	{
		if (state?.Pair.IsEmpty() != false)
			return;
		var market = GetMarket(state.Pair);
		KeyValuePair<long, MarketSubscription>[] level1Subscriptions;
		KeyValuePair<long, DepthSubscription>[] depthSubscriptions;
		KeyValuePair<long, MarketSubscription>[] tickSubscriptions;
		KeyValuePair<long, CandleSubscription>[] candleSubscriptions;
		using (_sync.EnterScope())
		{
			level1Subscriptions = [.. _level1Subscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(state.Pair))];
			depthSubscriptions = [.. _depthSubscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(state.Pair))];
			tickSubscriptions = [.. _tickSubscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(state.Pair))];
			candleSubscriptions = [.. _candleSubscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(state.Pair))];
		}

		foreach (var subscription in level1Subscriptions)
			await SendStreamLevel1Async(market, state, subscription.Key,
				cancellationToken);
		foreach (var subscription in depthSubscriptions)
			await SendStreamBookAsync(market, state, subscription.Value.Depth,
				subscription.Key, cancellationToken);
		foreach (var trade in state.Trades ?? [])
		{
			foreach (var subscription in tickSubscriptions)
				await SendStreamTradeAsync(market, trade, subscription.Key,
					cancellationToken);
			foreach (var subscription in candleSubscriptions)
				await UpdateLiveCandleAsync(market, trade, subscription.Key,
					subscription.Value, cancellationToken);
		}
	}

	private ValueTask SendTickerAsync(MarketDefinition market,
		LunoTicker ticker, long transactionId,
		CancellationToken cancellationToken)
	{
		if (ticker is null)
			throw new InvalidDataException(
				$"Luno returned no ticker for '{market.Symbol}'.");
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = ticker.Timestamp.ToLunoTime(CurrentTime),
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, ticker.Bid)
		.TryAdd(Level1Fields.BestAskPrice, ticker.Ask)
		.TryAdd(Level1Fields.LastTradePrice, ticker.LastTrade)
		.TryAdd(Level1Fields.Volume, ticker.RollingVolume)
		.TryAdd(Level1Fields.State, ticker.Status.ToStockSharp()),
			cancellationToken);
	}

	private ValueTask SendStreamLevel1Async(MarketDefinition market,
		LunoMarketStreamState state, long transactionId,
		CancellationToken cancellationToken)
	{
		var lastTrade = state.Trades?.LastOrDefault();
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = state.Timestamp,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, state.Bids?.FirstOrDefault()?.Price)
		.TryAdd(Level1Fields.BestBidVolume, state.Bids?.FirstOrDefault()?.Volume)
		.TryAdd(Level1Fields.BestAskPrice, state.Asks?.FirstOrDefault()?.Price)
		.TryAdd(Level1Fields.BestAskVolume, state.Asks?.FirstOrDefault()?.Volume)
		.TryAdd(Level1Fields.LastTradePrice, lastTrade?.Price)
		.TryAdd(Level1Fields.LastTradeVolume, lastTrade?.Volume)
		.TryAdd(Level1Fields.State, state.Status.ToStockSharp()),
			cancellationToken);
	}

	private ValueTask SendBookAsync(MarketDefinition market, LunoOrderBook book,
		int depth, long transactionId, CancellationToken cancellationToken)
	{
		if (book is null)
			throw new InvalidDataException(
				$"Luno returned no order book for '{market.Symbol}'.");
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = book.Timestamp.ToLunoTime(CurrentTime),
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = ToQuotes(book.Bids, false, depth),
			Asks = ToQuotes(book.Asks, true, depth),
		}, cancellationToken);
	}

	private ValueTask SendStreamBookAsync(MarketDefinition market,
		LunoMarketStreamState state, int depth, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = state.Timestamp,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			SeqNum = state.Sequence,
			Bids = ToQuotes(state.Bids, depth),
			Asks = ToQuotes(state.Asks, depth),
		}, cancellationToken);

	private ValueTask SendPublicTradeAsync(MarketDefinition market,
		LunoPublicTrade trade, long transactionId,
		CancellationToken cancellationToken)
	{
		if (trade is null || trade.Sequence <= 0 || trade.Price <= 0 ||
			trade.Volume <= 0 || !AddPublicTrade(
				trade.Sequence.ToString(CultureInfo.InvariantCulture), transactionId))
			return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = trade.Timestamp.ToLunoTime(CurrentTime),
			OriginalTransactionId = transactionId,
			TradeId = trade.Sequence,
			TradePrice = trade.Price,
			TradeVolume = trade.Volume,
			OriginSide = trade.IsBuy ? Sides.Buy : Sides.Sell,
		}, cancellationToken);
	}

	private ValueTask SendStreamTradeAsync(MarketDefinition market,
		LunoStreamTrade trade, long transactionId,
		CancellationToken cancellationToken)
	{
		var identity = trade.Sequence > 0
			? trade.Sequence.ToString(CultureInfo.InvariantCulture)
			: $"{trade.MakerOrderId}:{trade.TakerOrderId}:{trade.Timestamp.Ticks}";
		if (trade.Price <= 0 || trade.Volume <= 0 ||
			!AddPublicTrade(identity, transactionId))
			return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = trade.Timestamp,
			OriginalTransactionId = transactionId,
			TradeId = trade.Sequence > 0 ? trade.Sequence : null,
			TradeStringId = trade.Sequence > 0 ? null : identity,
			TradePrice = trade.Price,
			TradeVolume = trade.Volume,
			OriginSide = trade.TakerSide,
		}, cancellationToken);
	}

	private ValueTask SendCandleAsync(MarketDefinition market, LunoCandle candle,
		TimeSpan timeFrame, long transactionId,
		CancellationToken cancellationToken)
	{
		var openTime = candle.Timestamp.ToLunoTime(CurrentTime);
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = market.Symbol.ToStockSharp(),
			OpenTime = openTime,
			CloseTime = openTime + timeFrame,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.Volume,
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = CandleStates.Finished,
		}, cancellationToken);
	}

	private async ValueTask UpdateLiveCandleAsync(MarketDefinition market,
		LunoStreamTrade trade, long transactionId,
		CandleSubscription subscription, CancellationToken cancellationToken)
	{
		TimeFrameCandleMessage finished = null;
		TimeFrameCandleMessage active;
		using (_sync.EnterScope())
		{
			var openTime = trade.Timestamp.Align(subscription.TimeFrame);
			if (subscription.OpenTime != default && openTime < subscription.OpenTime)
				return;
			if (subscription.OpenTime == default)
				StartCandle(subscription, openTime, trade.Price, trade.Volume);
			else if (openTime > subscription.OpenTime)
			{
				finished = CreateLiveCandle(market, transactionId, subscription,
					CandleStates.Finished);
				StartCandle(subscription, openTime, trade.Price, trade.Volume);
			}
			else
			{
				subscription.High = subscription.High.Max(trade.Price);
				subscription.Low = subscription.Low.Min(trade.Price);
				subscription.Close = trade.Price;
				subscription.Volume += trade.Volume;
			}
			active = CreateLiveCandle(market, transactionId, subscription,
				CandleStates.Active);
		}
		if (finished is not null)
			await SendOutMessageAsync(finished, cancellationToken);
		await SendOutMessageAsync(active, cancellationToken);
	}

	private static void StartCandle(CandleSubscription subscription,
		DateTime openTime, decimal price, decimal volume)
	{
		subscription.OpenTime = openTime;
		subscription.Open = price;
		subscription.High = price;
		subscription.Low = price;
		subscription.Close = price;
		subscription.Volume = volume;
	}

	private static TimeFrameCandleMessage CreateLiveCandle(
		MarketDefinition market, long transactionId,
		CandleSubscription subscription, CandleStates state)
		=> new()
		{
			SecurityId = market.Symbol.ToStockSharp(),
			OpenTime = subscription.OpenTime,
			CloseTime = subscription.OpenTime + subscription.TimeFrame,
			OpenPrice = subscription.Open,
			HighPrice = subscription.High,
			LowPrice = subscription.Low,
			ClosePrice = subscription.Close,
			TotalVolume = subscription.Volume,
			TypedArg = subscription.TimeFrame,
			OriginalTransactionId = transactionId,
			State = state,
		};

	private static QuoteChange[] ToQuotes(IEnumerable<LunoOrderBookLevel> levels,
		bool isAsk, int depth)
	{
		var values = (levels ?? []).Where(static level =>
			level is not null && level.Price > 0 && level.Volume > 0);
		return [.. (isAsk
			? values.OrderBy(static level => level.Price)
			: values.OrderByDescending(static level => level.Price))
			.Take(depth).Select(static level =>
				new QuoteChange(level.Price, level.Volume))];
	}

	private static QuoteChange[] ToQuotes(
		IEnumerable<LunoStreamPriceLevel> levels, int depth)
		=> [.. (levels ?? []).Where(static level => level is not null &&
			level.Price > 0 && level.Volume > 0).Take(depth).Select(static level =>
				new QuoteChange(level.Price, level.Volume))];

	private static int GetCandleCount(MarketDataMessage message,
		TimeSpan timeFrame, DateTime to)
	{
		if (message.Count is long count)
			return count.Min(10000).Max(1).To<int>();
		if (message.From is DateTime from && to > from)
			return ((to - from.ToUniversalTime()).Ticks / timeFrame.Ticks + 1)
				.Min(10000L).Max(1L).To<int>();
		return 1000;
	}

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
