namespace StockSharp.ProBit;

public partial class ProBitMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		if (securityTypes.Count > 0 && !securityTypes.Contains(SecurityTypes.CryptoCurrency))
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}

		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var market in await RestClient.GetMarketsAsync(cancellationToken))
		{
			if (market?.Id.IsEmpty() != false || market.IsClosed)
				continue;
			var code = market.Id.ToUpperInvariant();
			var priceStep = market.PriceIncrement.ToDecimal();
			var security = new SecurityMessage
			{
				SecurityId = code.ToStockSharp(),
				Name = code,
				SecurityType = SecurityTypes.CryptoCurrency,
				OriginalTransactionId = lookupMsg.TransactionId,
				PriceStep = priceStep,
				Decimals = priceStep?.GetCachedDecimals(),
				VolumeStep = market.QuantityPrecision.PrecisionToStep(),
				MinVolume = market.MinQuantity.ToDecimal(),
				MaxVolume = market.MaxQuantity.ToDecimal(),
			}.TryFillUnderlyingId(market.BaseCurrencyId?.ToUpperInvariant());
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
		var ticker = (await RestClient.GetTickersAsync(symbol, cancellationToken)).FirstOrDefault();
		var book = await RestClient.GetOrderBookAsync(symbol, cancellationToken);
		InitializeBook(symbol, book);
		await SendLevel1SnapshotAsync(symbol, ticker, mdMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}
		await EnsureStreamTradeCursorAsync(symbol, cancellationToken);

		bool subscribeTicker;
		bool subscribeTrades;
		bool subscribeBook;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new() { Symbol = symbol });
			subscribeTicker = AddReference(_streamReferences, ("ticker", symbol));
			subscribeTrades = AddReference(_streamReferences, ("recent_trades", symbol));
			subscribeBook = AddReference(_streamReferences, ("order_books_l0", symbol));
		}
		if (subscribeTicker)
			await WebSocketClient.SubscribeMarketAsync(symbol, "ticker", cancellationToken);
		if (subscribeTrades)
			await WebSocketClient.SubscribeMarketAsync(symbol, "recent_trades", cancellationToken);
		if (subscribeBook)
			await WebSocketClient.SubscribeMarketAsync(symbol, "order_books_l0", cancellationToken);
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
		InitializeBook(symbol, await RestClient.GetOrderBookAsync(symbol, cancellationToken));
		await SendBookAsync(symbol, mdMsg.TransactionId, CurrentTime, depth, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		bool subscribe;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				Depth = depth,
			});
			subscribe = AddReference(_streamReferences, ("order_books_l0", symbol));
		}
		if (subscribe)
			await WebSocketClient.SubscribeMarketAsync(symbol, "order_books_l0", cancellationToken);
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
		var from = mdMsg.From?.ToUniversalTime();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var limit = (mdMsg.Count ?? 100).Min(1000).Max(1).To<int>();
		var trades = await RestClient.GetTradesAsync(symbol, from, to, limit, cancellationToken);
		string lastTradeId = null;
		var lastTime = from ?? default;
		foreach (var trade in trades.OrderBy(static trade => trade.Time.ToUtcTime()))
		{
			var time = trade.Time.ToUtcTime();
			if (time < (from ?? DateTime.MinValue) || time > to)
				continue;
			await SendTradeAsync(trade, symbol, mdMsg.TransactionId, cancellationToken);
			lastTradeId = trade.Id;
			lastTime = time;
		}
		SeedStreamTradeCursor(symbol, lastTradeId, lastTime);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}
		await EnsureStreamTradeCursorAsync(symbol, cancellationToken);

		bool subscribe;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				LastTradeId = lastTradeId,
				LastTime = lastTime,
			});
			subscribe = AddReference(_streamReferences, ("recent_trades", symbol));
		}
		if (subscribe)
			await WebSocketClient.SubscribeMarketAsync(symbol, "recent_trades", cancellationToken);
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
		_ = timeFrame.ToProBitInterval();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var count = GetCandleCount(mdMsg, timeFrame, to);
		var from = (mdMsg.From ?? to - TimeSpan.FromTicks(timeFrame.Ticks * count)).ToUniversalTime();
		var candles = await RestClient.GetCandlesAsync(symbol, timeFrame, from, to, count,
			cancellationToken);
		ProBitCandle last = null;
		foreach (var candle in candles.OrderBy(static candle => candle.StartTime.ToUtcTime()))
		{
			var openTime = candle.StartTime.ToUtcTime();
			if (openTime < from || openTime > to)
				continue;
			await SendCandleAsync(openTime, candle.Open.ToDecimal() ?? 0m,
				candle.High.ToDecimal() ?? 0m, candle.Low.ToDecimal() ?? 0m,
				candle.Close.ToDecimal() ?? 0m, candle.BaseVolume.ToDecimal() ?? 0m,
				symbol, timeFrame, mdMsg.TransactionId,
				openTime + timeFrame <= DateTime.UtcNow ? CandleStates.Finished : CandleStates.Active,
				cancellationToken);
			last = candle;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		await EnsureStreamTradeCursorAsync(symbol, cancellationToken);

		bool subscribe;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				TimeFrame = timeFrame,
				OpenTime = last?.StartTime.ToUtcTime() ?? default,
				OpenPrice = last?.Open.ToDecimal() ?? 0m,
				HighPrice = last?.High.ToDecimal() ?? 0m,
				LowPrice = last?.Low.ToDecimal() ?? 0m,
				ClosePrice = last?.Close.ToDecimal() ?? 0m,
				TotalVolume = last?.BaseVolume.ToDecimal() ?? 0m,
				IsInitialized = last is not null,
			});
			subscribe = AddReference(_streamReferences, ("recent_trades", symbol));
		}
		if (subscribe)
			await WebSocketClient.SubscribeMarketAsync(symbol, "recent_trades", cancellationToken);
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		StreamSubscription subscription = null;
		var ticker = false;
		var trades = false;
		var book = false;
		using (_sync.EnterScope())
		{
			if (_level1Subscriptions.Remove(transactionId, out subscription))
			{
				ticker = ReleaseReference(_streamReferences, ("ticker", subscription.Symbol));
				trades = ReleaseReference(_streamReferences, ("recent_trades", subscription.Symbol));
				book = ReleaseReference(_streamReferences, ("order_books_l0", subscription.Symbol));
			}
		}
		if (subscription is null)
			return;
		if (ticker)
			await WebSocketClient.UnsubscribeMarketAsync(subscription.Symbol, "ticker", cancellationToken);
		if (trades)
			await WebSocketClient.UnsubscribeMarketAsync(subscription.Symbol, "recent_trades", cancellationToken);
		if (book)
			await WebSocketClient.UnsubscribeMarketAsync(subscription.Symbol, "order_books_l0", cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_depthSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences,
					("order_books_l0", subscription.Symbol));
		}
		if (unsubscribe)
			await WebSocketClient.UnsubscribeMarketAsync(subscription.Symbol, "order_books_l0",
				cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		TickSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_tickSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences,
					("recent_trades", subscription.Symbol));
		}
		if (unsubscribe)
			await WebSocketClient.UnsubscribeMarketAsync(subscription.Symbol, "recent_trades",
				cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		CandleSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_candleSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences,
					("recent_trades", subscription.Symbol));
		}
		if (unsubscribe)
			await WebSocketClient.UnsubscribeMarketAsync(subscription.Symbol, "recent_trades",
				cancellationToken);
	}

	private async ValueTask OnMarketDataAsync(ProBitWsMarketDataMessage message,
		CancellationToken cancellationToken)
	{
		var symbol = message?.MarketId?.ToUpperInvariant();
		if (symbol.IsEmpty())
			return;

		if (message.Ticker is not null)
			await OnTickerAsync(symbol, message.Ticker, cancellationToken);
		if (message.OrderBooks is { Length: > 0 })
			await OnBookAsync(symbol, message.OrderBooks, cancellationToken);
		if (message.RecentTrades is { Length: > 0 })
			await OnTradesAsync(symbol, message.RecentTrades, cancellationToken);
	}

	private async ValueTask OnTickerAsync(string symbol, ProBitTicker ticker,
		CancellationToken cancellationToken)
	{
		long[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions
				.Where(pair => pair.Value.Symbol.EqualsIgnoreCase(symbol))
				.Select(static pair => pair.Key)];
		var time = ticker.Time.IsEmpty() ? CurrentTime : ticker.Time.ToUtcTime();
		foreach (var id in subscriptions)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = symbol.ToStockSharp(),
				ServerTime = time,
				OriginalTransactionId = id,
			}
			.TryAdd(Level1Fields.LastTradePrice, ticker.Last.ToDecimal())
			.TryAdd(Level1Fields.HighPrice, ticker.High.ToDecimal())
			.TryAdd(Level1Fields.LowPrice, ticker.Low.ToDecimal())
			.TryAdd(Level1Fields.Change, ticker.Change.ToDecimal())
			.TryAdd(Level1Fields.Volume, ticker.BaseVolume.ToDecimal()), cancellationToken);
	}

	private async ValueTask OnBookAsync(string symbol, ProBitBookLevel[] levels,
		CancellationToken cancellationToken)
	{
		(long Id, int Depth)[] depthSubscriptions;
		long[] level1Subscriptions;
		using (_sync.EnterScope())
		{
			ApplyBookUpdates(symbol, levels);
			depthSubscriptions = [.. _depthSubscriptions
				.Where(pair => pair.Value.Symbol.EqualsIgnoreCase(symbol))
				.Select(static pair => (pair.Key, pair.Value.Depth))];
			level1Subscriptions = [.. _level1Subscriptions
				.Where(pair => pair.Value.Symbol.EqualsIgnoreCase(symbol))
				.Select(static pair => pair.Key)];
		}
		var time = CurrentTime;
		foreach (var subscription in depthSubscriptions)
			await SendBookAsync(symbol, subscription.Id, time, subscription.Depth, cancellationToken);
		foreach (var id in level1Subscriptions)
			await SendBestQuotesAsync(symbol, id, time, cancellationToken);
	}

	private async ValueTask OnTradesAsync(string symbol, ProBitTrade[] trades,
		CancellationToken cancellationToken)
	{
		foreach (var trade in trades.OrderBy(static trade => trade.Time.ToUtcTime()))
		{
			var time = trade.Time.ToUtcTime();
			long[] level1Ids;
			long[] tickIds;
			CandleEmission[] candleEmissions;
			using (_sync.EnterScope())
			{
				if (!AcceptStreamTrade(symbol, trade.Id, time))
					continue;
				level1Ids = [.. _level1Subscriptions
					.Where(pair => pair.Value.Symbol.EqualsIgnoreCase(symbol))
					.Select(static pair => pair.Key)];
				var acceptedTicks = new List<long>();
				foreach (var pair in _tickSubscriptions)
				{
					var state = pair.Value;
					if (!state.Symbol.EqualsIgnoreCase(symbol) ||
						(!state.LastTradeId.IsEmpty() && state.LastTradeId.EqualsIgnoreCase(trade.Id)) ||
						(state.LastTime != default && time < state.LastTime))
						continue;
					state.LastTradeId = trade.Id;
					state.LastTime = time;
					acceptedTicks.Add(pair.Key);
				}
				tickIds = [.. acceptedTicks];
				candleEmissions = UpdateCandles(symbol, time,
					trade.Price.ToDecimal() ?? 0m, trade.Quantity.ToDecimal() ?? 0m);
			}

			var side = trade.Side.ToStockSharpSide();
			foreach (var id in level1Ids)
				await SendOutMessageAsync(new Level1ChangeMessage
				{
					SecurityId = symbol.ToStockSharp(),
					ServerTime = time,
					OriginalTransactionId = id,
				}
				.TryAdd(Level1Fields.LastTradePrice, trade.Price.ToDecimal())
				.TryAdd(Level1Fields.LastTradeVolume, trade.Quantity.ToDecimal())
				.TryAdd(Level1Fields.LastTradeOrigin, side), cancellationToken);

			foreach (var id in tickIds)
				await SendTradeAsync(trade, symbol, id, cancellationToken);

			foreach (var emission in candleEmissions)
				await SendCandleAsync(emission.OpenTime, emission.OpenPrice, emission.HighPrice,
					emission.LowPrice, emission.ClosePrice, emission.TotalVolume, emission.Symbol,
					emission.TimeFrame, emission.TransactionId, emission.State, cancellationToken);
		}
	}

	private bool AcceptStreamTrade(string symbol, string tradeId, DateTime time)
	{
		if (_streamTradeCursors.TryGetValue(symbol, out var cursor))
		{
			if (time < cursor.LastTime ||
				time == cursor.LastTime && tradeId.EqualsIgnoreCase(cursor.LastTradeId))
				return false;
			cursor.LastTime = time;
			cursor.LastTradeId = tradeId;
		}
		else
		{
			_streamTradeCursors.Add(symbol, new()
			{
				Symbol = symbol,
				LastTradeId = tradeId,
				LastTime = time,
			});
		}
		return true;
	}

	private async ValueTask EnsureStreamTradeCursorAsync(string symbol,
		CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
			if (_streamTradeCursors.ContainsKey(symbol))
				return;

		var trade = (await RestClient.GetTradesAsync(symbol, null, null, 1,
			cancellationToken)).OrderByDescending(static item => item.Time.ToUtcTime())
			.FirstOrDefault();
		if (trade is not null)
			SeedStreamTradeCursor(symbol, trade.Id, trade.Time.ToUtcTime());
	}

	private void SeedStreamTradeCursor(string symbol, string tradeId, DateTime time)
	{
		if (tradeId.IsEmpty() || time == default)
			return;
		using (_sync.EnterScope())
		{
			if (_streamTradeCursors.TryGetValue(symbol, out var cursor))
			{
				if (time < cursor.LastTime)
					return;
				cursor.LastTradeId = tradeId;
				cursor.LastTime = time;
			}
			else
			{
				_streamTradeCursors.Add(symbol, new()
				{
					Symbol = symbol,
					LastTradeId = tradeId,
					LastTime = time,
				});
			}
		}
	}

	private CandleEmission[] UpdateCandles(string symbol, DateTime time, decimal price,
		decimal volume)
	{
		var emissions = new List<CandleEmission>();
		foreach (var pair in _candleSubscriptions)
		{
			var state = pair.Value;
			if (!state.Symbol.EqualsIgnoreCase(symbol))
				continue;
			var openTime = time.Align(state.TimeFrame);
			if (state.IsInitialized && openTime < state.OpenTime)
				continue;
			if (state.IsInitialized && openTime > state.OpenTime)
			{
				emissions.Add(ToEmission(pair.Key, state, CandleStates.Finished));
				state.IsInitialized = false;
			}
			if (!state.IsInitialized)
			{
				state.OpenTime = openTime;
				state.OpenPrice = price;
				state.HighPrice = price;
				state.LowPrice = price;
				state.ClosePrice = price;
				state.TotalVolume = volume;
				state.IsInitialized = true;
			}
			else
			{
				state.HighPrice = state.HighPrice.Max(price);
				state.LowPrice = state.LowPrice.Min(price);
				state.ClosePrice = price;
				state.TotalVolume += volume;
			}
			emissions.Add(ToEmission(pair.Key, state, CandleStates.Active));
		}
		return [.. emissions];
	}

	private static CandleEmission ToEmission(long id, CandleSubscription state,
		CandleStates candleState)
		=> new(id, state.Symbol, state.TimeFrame, state.OpenTime, state.OpenPrice,
			state.HighPrice, state.LowPrice, state.ClosePrice, state.TotalVolume, candleState);

	private void InitializeBook(string symbol, IEnumerable<ProBitBookLevel> levels)
	{
		using (_sync.EnterScope())
		{
			var state = new BookState();
			_books[symbol] = state;
			ApplyBookUpdates(symbol, levels);
		}
	}

	private void ApplyBookUpdates(string symbol, IEnumerable<ProBitBookLevel> levels)
	{
		if (!_books.TryGetValue(symbol, out var state))
			_books.Add(symbol, state = new());
		foreach (var level in levels ?? [])
		{
			var price = level?.Price.ToDecimal();
			var volume = level?.Quantity.ToDecimal();
			if (price is not > 0 || volume is null)
				continue;
			var side = level.Side.EqualsIgnoreCase("buy") ? state.Bids : state.Asks;
			if (volume <= 0)
				side.Remove(price.Value);
			else
				side[price.Value] = volume.Value;
		}
	}

	private ValueTask SendLevel1SnapshotAsync(string symbol, ProBitTicker ticker,
		long transactionId, CancellationToken cancellationToken)
	{
		var best = GetBestQuotes(symbol);
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(),
			ServerTime = ticker?.Time.IsEmpty() == false ? ticker.Time.ToUtcTime() : CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, ticker?.Last.ToDecimal())
		.TryAdd(Level1Fields.HighPrice, ticker?.High.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, ticker?.Low.ToDecimal())
		.TryAdd(Level1Fields.Change, ticker?.Change.ToDecimal())
		.TryAdd(Level1Fields.Volume, ticker?.BaseVolume.ToDecimal())
		.TryAdd(Level1Fields.BestBidPrice, best.BidPrice)
		.TryAdd(Level1Fields.BestBidVolume, best.BidVolume)
		.TryAdd(Level1Fields.BestAskPrice, best.AskPrice)
		.TryAdd(Level1Fields.BestAskVolume, best.AskVolume), cancellationToken);
	}

	private ValueTask SendBestQuotesAsync(string symbol, long transactionId, DateTime serverTime,
		CancellationToken cancellationToken)
	{
		var best = GetBestQuotes(symbol);
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(),
			ServerTime = serverTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, best.BidPrice)
		.TryAdd(Level1Fields.BestBidVolume, best.BidVolume)
		.TryAdd(Level1Fields.BestAskPrice, best.AskPrice)
		.TryAdd(Level1Fields.BestAskVolume, best.AskVolume), cancellationToken);
	}

	private ValueTask SendBookAsync(string symbol, long transactionId, DateTime serverTime,
		int depth, CancellationToken cancellationToken)
	{
		QuoteChange[] bids;
		QuoteChange[] asks;
		using (_sync.EnterScope())
		{
			if (!_books.TryGetValue(symbol, out var state))
				state = new();
			bids = [.. state.Bids.Take(depth).Select(static level =>
				new QuoteChange(level.Key, level.Value))];
			asks = [.. state.Asks.Take(depth).Select(static level =>
				new QuoteChange(level.Key, level.Value))];
		}
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(),
			ServerTime = serverTime,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = bids,
			Asks = asks,
		}, cancellationToken);
	}

	private (decimal? BidPrice, decimal? BidVolume, decimal? AskPrice, decimal? AskVolume)
		GetBestQuotes(string symbol)
	{
		using (_sync.EnterScope())
		{
			if (!_books.TryGetValue(symbol, out var state))
				return default;
			var bid = state.Bids.Count > 0 ? state.Bids.First() : default;
			var ask = state.Asks.Count > 0 ? state.Asks.First() : default;
			return (state.Bids.Count > 0 ? bid.Key : null,
				state.Bids.Count > 0 ? bid.Value : null,
				state.Asks.Count > 0 ? ask.Key : null,
				state.Asks.Count > 0 ? ask.Value : null);
		}
	}

	private ValueTask SendTradeAsync(ProBitTrade trade, string symbol, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = symbol.ToStockSharp(),
			ServerTime = trade.Time.ToUtcTime(),
			OriginalTransactionId = transactionId,
			TradeStringId = trade.Id,
			TradePrice = trade.Price.ToDecimal(),
			TradeVolume = trade.Quantity.ToDecimal(),
			OriginSide = trade.Side.ToStockSharpSide(),
		}, cancellationToken);

	private ValueTask SendCandleAsync(DateTime openTime, decimal open, decimal high, decimal low,
		decimal close, decimal volume, string symbol, TimeSpan timeFrame, long transactionId,
		CandleStates state, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = symbol.ToStockSharp(),
			OpenTime = openTime,
			CloseTime = openTime + timeFrame,
			OpenPrice = open,
			HighPrice = high,
			LowPrice = low,
			ClosePrice = close,
			TotalVolume = volume,
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = state,
		}, cancellationToken);

	private static int GetCandleCount(MarketDataMessage message, TimeSpan timeFrame, DateTime to)
	{
		if (message.Count is long count)
			return count.Min(1000).Max(1).To<int>();
		if (message.From is DateTime from && to > from)
			return ((to - from).Ticks / timeFrame.Ticks + 1).Min(1000L).Max(1L).To<int>();
		return 1000;
	}
}
