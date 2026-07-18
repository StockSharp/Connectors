namespace StockSharp.Bitunix;

public partial class BitunixMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;
		BitunixSpotPair[] spotPairs;
		BitunixFuturesProduct[] futuresProducts;
		using (_sync.EnterScope())
		{
			spotPairs = [.. _spotPairs.Values];
			futuresProducts = [.. _futuresProducts.Values];
		}

		if (IsSectionEnabled(BitunixSections.Spot) &&
			(securityTypes.Count == 0 || securityTypes.Contains(SecurityTypes.CryptoCurrency)))
		{
			foreach (var pair in spotPairs.OrderBy(static pair => pair.Symbol))
			{
				if (pair.IsOpenValue != 1)
					continue;
				var symbol = pair.Symbol.ToUpperInvariant();
				var security = new SecurityMessage
				{
					SecurityId = symbol.ToStockSharp(BitunixSections.Spot),
					Name = $"{pair.Base}/{pair.Quote}",
					SecurityType = SecurityTypes.CryptoCurrency,
					OriginalTransactionId = lookupMsg.TransactionId,
					PriceStep = pair.QuotePrecision.PrecisionToStep(),
					Decimals = pair.QuotePrecision,
					VolumeStep = pair.BasePrecision.PrecisionToStep(),
					MinVolume = pair.MinimumVolume,
				}.TryFillUnderlyingId(pair.Base);
				if (!security.IsMatch(lookupMsg, securityTypes))
					continue;
				await SendOutMessageAsync(security, cancellationToken);
				if (--left <= 0)
					break;
			}
		}

		if (left > 0 && IsSectionEnabled(BitunixSections.Futures) &&
			(securityTypes.Count == 0 || securityTypes.Contains(SecurityTypes.Future)))
		{
			foreach (var product in futuresProducts.OrderBy(static product => product.Symbol))
			{
				if (!product.Status.EqualsIgnoreCase("OPEN") || product.IsApiSupported == false)
					continue;
				var security = new SecurityMessage
				{
					SecurityId = product.Symbol.ToStockSharp(BitunixSections.Futures),
					Name = $"{product.Base}/{product.Quote} Perpetual",
					SecurityType = SecurityTypes.Future,
					OriginalTransactionId = lookupMsg.TransactionId,
					PriceStep = product.QuotePrecision.PrecisionToStep(),
					Decimals = product.QuotePrecision,
					VolumeStep = product.BasePrecision.PrecisionToStep(),
					MinVolume = product.MinimumTradeVolume,
					MaxVolume = product.MaximumLimitOrderVolume,
				}.TryFillUnderlyingId(product.Base);
				if (!security.IsMatch(lookupMsg, securityTypes))
					continue;
				await SendOutMessageAsync(security, cancellationToken);
				if (--left <= 0)
					break;
			}
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

		var section = ResolveSection(mdMsg.SecurityId);
		var symbol = GetSymbol(mdMsg.SecurityId);
		if (section == BitunixSections.Spot)
			await SendSpotLevel1SnapshotAsync(symbol, mdMsg.TransactionId, cancellationToken);
		else
		{
			var ticker = (await FuturesRestClient.GetFuturesTickersAsync(symbol,
				cancellationToken) ?? []).FirstOrDefault();
			if (ticker is null)
				throw new InvalidDataException($"Bitunix returned no futures ticker for '{symbol}'.");
			await SendFuturesTickerAsync(ticker, mdMsg.TransactionId, cancellationToken);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var subscribe = new List<StreamKey>();
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId,
				new() { Symbol = symbol, Section = section });
			if (section == BitunixSections.Futures)
			{
				foreach (var key in new[]
				{
					new StreamKey("ticker", symbol),
					new StreamKey("price", symbol),
				})
				{
					if (AddReference(_streamReferences, key))
						subscribe.Add(key);
				}
			}
		}
		foreach (var key in subscribe)
			await ChangeStreamAsync(key, true, cancellationToken);
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

		var section = ResolveSection(mdMsg.SecurityId);
		var symbol = GetSymbol(mdMsg.SecurityId);
		var depth = NormalizeDepth(mdMsg.MaxDepth);
		if (section == BitunixSections.Spot)
		{
			var book = await SpotRestClient.GetSpotDepthAsync(symbol, GetSpotPrecision(symbol),
				cancellationToken);
			await SendSpotBookAsync(symbol, book, mdMsg.TransactionId, depth, cancellationToken);
		}
		else
		{
			var book = await FuturesRestClient.GetFuturesDepthAsync(symbol, depth,
				cancellationToken);
			await SendFuturesBookAsync(symbol, book?.Bids, book?.Asks, 0,
				mdMsg.TransactionId, depth, cancellationToken);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var key = new StreamKey("depth", symbol, depth.ToString(CultureInfo.InvariantCulture));
		var subscribe = false;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				Section = section,
				Depth = depth,
			});
			if (section == BitunixSections.Futures)
				subscribe = AddReference(_streamReferences, key);
		}
		if (subscribe)
			await ChangeStreamAsync(key, true, cancellationToken);
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

		var section = ResolveSection(mdMsg.SecurityId);
		if (section == BitunixSections.Spot)
			throw new NotSupportedException(
				"Bitunix spot OpenAPI does not expose public trades or a push trade channel.");
		if (mdMsg.IsHistoryOnly())
			throw new NotSupportedException(
				"Bitunix futures OpenAPI exposes live trades through WebSocket but no public trade history endpoint.");

		var symbol = GetSymbol(mdMsg.SecurityId);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		var key = new StreamKey("trades", symbol);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId,
				new() { Symbol = symbol, Section = BitunixSections.Futures });
			subscribe = AddReference(_streamReferences, key);
		}
		if (subscribe)
			await ChangeStreamAsync(key, true, cancellationToken);
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

		var section = ResolveSection(mdMsg.SecurityId);
		var symbol = GetSymbol(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var maximum = section == BitunixSections.Spot ? 500 : 200;
		var count = (mdMsg.Count ?? maximum).Min(maximum).Max(1).To<int>();
		var from = mdMsg.From?.ToUniversalTime() ??
			to - TimeSpan.FromTicks(timeFrame.Ticks * count);

		if (section == BitunixSections.Spot)
		{
			var candles = await SpotRestClient.GetSpotCandlesAsync(symbol,
				timeFrame.ToSpotInterval(), new DateTimeOffset(to).ToUnixTimeSeconds(), count,
				cancellationToken) ?? [];
			foreach (var candle in candles
				.Where(candle => IsInRange(candle.Time.UtcDateTime, from, to))
				.OrderBy(static candle => candle.Time))
				await SendSpotCandleAsync(symbol, candle, timeFrame, mdMsg.TransactionId,
					cancellationToken);
		}
		else
		{
			var candles = await FuturesRestClient.GetFuturesCandlesAsync(symbol,
				timeFrame.ToFuturesInterval(),
				new DateTimeOffset(from).ToUnixTimeMilliseconds(),
				new DateTimeOffset(to).ToUnixTimeMilliseconds(), count, cancellationToken) ?? [];
			foreach (var candle in candles
				.Where(candle => IsInRange(candle.Time.FromMilliseconds(), from, to))
				.OrderBy(static candle => candle.Time))
				await SendFuturesCandleAsync(symbol, candle, timeFrame, mdMsg.TransactionId,
					cancellationToken);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var key = new StreamKey("candles", symbol, timeFrame.ToWsInterval());
		var subscribe = false;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				Section = section,
				TimeFrame = timeFrame,
			});
			if (section == BitunixSections.Futures)
				subscribe = AddReference(_streamReferences, key);
		}
		if (subscribe)
			await ChangeStreamAsync(key, true, cancellationToken);
	}

	private async ValueTask ChangeStreamAsync(StreamKey key, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		if (_futuresWsClient is null)
			return;

		var depth = key.Argument?.To<int>() ?? 15;
		if (isSubscribe)
		{
			switch (key.Topic)
			{
				case "ticker": await _futuresWsClient.SubscribeTickerAsync(key.Symbol, cancellationToken); break;
				case "price": await _futuresWsClient.SubscribePriceAsync(key.Symbol, cancellationToken); break;
				case "depth": await _futuresWsClient.SubscribeDepthAsync(key.Symbol, depth, cancellationToken); break;
				case "trades": await _futuresWsClient.SubscribeTradesAsync(key.Symbol, cancellationToken); break;
				case "candles": await _futuresWsClient.SubscribeCandlesAsync(key.Symbol, key.Argument, cancellationToken); break;
				default: throw new ArgumentOutOfRangeException(nameof(key), key, null);
			}
		}
		else
		{
			switch (key.Topic)
			{
				case "ticker": await _futuresWsClient.UnsubscribeTickerAsync(key.Symbol, cancellationToken); break;
				case "price": await _futuresWsClient.UnsubscribePriceAsync(key.Symbol, cancellationToken); break;
				case "depth": await _futuresWsClient.UnsubscribeDepthAsync(key.Symbol, depth, cancellationToken); break;
				case "trades": await _futuresWsClient.UnsubscribeTradesAsync(key.Symbol, cancellationToken); break;
				case "candles": await _futuresWsClient.UnsubscribeCandlesAsync(key.Symbol, key.Argument, cancellationToken); break;
				default: throw new ArgumentOutOfRangeException(nameof(key), key, null);
			}
		}
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription;
		var unsubscribe = new List<StreamKey>();
		using (_sync.EnterScope())
		{
			if (!_level1Subscriptions.Remove(transactionId, out subscription))
				return;
			if (subscription.Section == BitunixSections.Futures)
			{
				foreach (var key in new[]
				{
					new StreamKey("ticker", subscription.Symbol),
					new StreamKey("price", subscription.Symbol),
				})
				{
					if (ReleaseReference(_streamReferences, key))
						unsubscribe.Add(key);
				}
			}
		}
		foreach (var key in unsubscribe)
			await ChangeStreamAsync(key, false, cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription subscription;
		StreamKey key = default;
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (!_depthSubscriptions.Remove(transactionId, out subscription))
				return;
			if (subscription.Section == BitunixSections.Futures)
			{
				key = new("depth", subscription.Symbol,
					subscription.Depth.ToString(CultureInfo.InvariantCulture));
				unsubscribe = ReleaseReference(_streamReferences, key);
			}
		}
		if (unsubscribe)
			await ChangeStreamAsync(key, false, cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription;
		StreamKey key;
		bool unsubscribe;
		using (_sync.EnterScope())
		{
			if (!_tickSubscriptions.Remove(transactionId, out subscription))
				return;
			key = new("trades", subscription.Symbol);
			unsubscribe = ReleaseReference(_streamReferences, key);
		}
		if (unsubscribe)
			await ChangeStreamAsync(key, false, cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		CandleSubscription subscription;
		StreamKey key = default;
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (!_candleSubscriptions.Remove(transactionId, out subscription))
				return;
			if (subscription.Section == BitunixSections.Futures)
			{
				key = new("candles", subscription.Symbol, subscription.TimeFrame.ToWsInterval());
				unsubscribe = ReleaseReference(_streamReferences, key);
			}
		}
		if (unsubscribe)
			await ChangeStreamAsync(key, false, cancellationToken);
	}

	private async ValueTask PollSpotMarketDataAsync(CancellationToken cancellationToken)
	{
		(long Id, MarketSubscription Subscription)[] level1;
		(long Id, DepthSubscription Subscription)[] depths;
		(long Id, CandleSubscription Subscription)[] candles;
		using (_sync.EnterScope())
		{
			level1 = [.. _level1Subscriptions
				.Where(static pair => pair.Value.Section == BitunixSections.Spot)
				.Select(static pair => (pair.Key, pair.Value))];
			depths = [.. _depthSubscriptions
				.Where(static pair => pair.Value.Section == BitunixSections.Spot)
				.Select(static pair => (pair.Key, pair.Value))];
			candles = [.. _candleSubscriptions
				.Where(static pair => pair.Value.Section == BitunixSections.Spot)
				.Select(static pair => (pair.Key, pair.Value))];
		}

		foreach (var group in level1.GroupBy(static item => item.Subscription.Symbol,
			StringComparer.OrdinalIgnoreCase))
		{
			await PollSafelyAsync(async () =>
			{
				var symbol = group.Key;
				var last = await SpotRestClient.GetSpotLastPriceAsync(symbol, cancellationToken);
				var book = await SpotRestClient.GetSpotDepthAsync(symbol, GetSpotPrecision(symbol),
					cancellationToken);
				foreach (var item in group)
					await SendSpotLevel1Async(symbol, last.ToDecimal(), book, item.Id,
						cancellationToken);
			}, cancellationToken);
		}

		foreach (var group in depths.GroupBy(static item =>
			(item.Subscription.Symbol, item.Subscription.Depth)))
		{
			await PollSafelyAsync(async () =>
			{
				var book = await SpotRestClient.GetSpotDepthAsync(group.Key.Symbol,
					GetSpotPrecision(group.Key.Symbol), cancellationToken);
				foreach (var item in group)
					await SendSpotBookAsync(group.Key.Symbol, book, item.Id,
						item.Subscription.Depth, cancellationToken);
			}, cancellationToken);
		}

		foreach (var group in candles.GroupBy(static item =>
			(item.Subscription.Symbol, item.Subscription.TimeFrame)))
		{
			await PollSafelyAsync(async () =>
			{
				var candle = await SpotRestClient.GetSpotCurrentCandleAsync(group.Key.Symbol,
					group.Key.TimeFrame.ToSpotInterval(), cancellationToken);
				if (candle is null)
					return;
				foreach (var item in group)
					await SendSpotCandleAsync(group.Key.Symbol, candle, group.Key.TimeFrame,
						item.Id, cancellationToken);
			}, cancellationToken);
		}
	}

	private async ValueTask PollSafelyAsync(Func<ValueTask> action,
		CancellationToken cancellationToken)
	{
		try
		{
			await action();
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested)
		{
			await SendOutErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask SendSpotLevel1SnapshotAsync(string symbol,
		long originalTransactionId, CancellationToken cancellationToken)
	{
		var last = await SpotRestClient.GetSpotLastPriceAsync(symbol, cancellationToken);
		var book = await SpotRestClient.GetSpotDepthAsync(symbol, GetSpotPrecision(symbol),
			cancellationToken);
		await SendSpotLevel1Async(symbol, last.ToDecimal(), book, originalTransactionId,
			cancellationToken);
	}

	private string GetSpotPrecision(string symbol)
	{
		BitunixSpotPair pair;
		using (_sync.EnterScope())
			_spotPairs.TryGetValue(symbol, out pair);
		if (pair is null)
			throw new InvalidOperationException($"Unknown Bitunix spot symbol '{symbol}'.");
		return pair.Precisions?.FirstOrDefault(static precision => !precision.IsEmpty())
			.IsEmpty(pair.QuotePrecision.PrecisionToStep().ToWire());
	}

	private async ValueTask OnFuturesTickerAsync(string symbol, BitunixWsTicker ticker,
		long time, CancellationToken cancellationToken)
	{
		(long Id, MarketSubscription Subscription)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions.Where(pair =>
				pair.Value.Section == BitunixSections.Futures &&
				pair.Value.Symbol.EqualsIgnoreCase(symbol)).Select(static pair => (pair.Key, pair.Value))];
		foreach (var (id, _) in subscriptions)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = symbol.ToStockSharp(BitunixSections.Futures),
				ServerTime = time > 0 ? time.FromMilliseconds() : CurrentTime,
				OriginalTransactionId = id,
			}.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice)
			 .TryAdd(Level1Fields.OpenPrice, ticker.Open)
			 .TryAdd(Level1Fields.HighPrice, ticker.High)
			 .TryAdd(Level1Fields.LowPrice, ticker.Low)
			 .TryAdd(Level1Fields.Volume, ticker.BaseVolume)
			 .TryAdd(Level1Fields.Change, ticker.ChangePercent), cancellationToken);
	}

	private async ValueTask OnFuturesPriceAsync(string symbol, BitunixWsPrice price,
		long time, CancellationToken cancellationToken)
	{
		(long Id, MarketSubscription Subscription)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions.Where(pair =>
				pair.Value.Section == BitunixSections.Futures &&
				pair.Value.Symbol.EqualsIgnoreCase(symbol)).Select(static pair => (pair.Key, pair.Value))];
		foreach (var (id, _) in subscriptions)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = symbol.ToStockSharp(BitunixSections.Futures),
				ServerTime = time > 0 ? time.FromMilliseconds() : CurrentTime,
				OriginalTransactionId = id,
			}.TryAdd(Level1Fields.Index, price.IndexPrice)
			 .TryAdd(Level1Fields.SettlementPrice, price.MarkPrice), cancellationToken);
	}

	private async ValueTask OnFuturesDepthAsync(string symbol, string channel,
		BitunixWsDepth depth, long time, CancellationToken cancellationToken)
	{
		var depthText = channel?.StartsWith("depth_book", StringComparison.OrdinalIgnoreCase) == true
			? channel["depth_book".Length..]
			: null;
		if (!int.TryParse(depthText, NumberStyles.Integer, CultureInfo.InvariantCulture,
			out var channelDepth))
			return;

		(long Id, DepthSubscription Subscription)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions.Where(pair =>
				pair.Value.Section == BitunixSections.Futures &&
				pair.Value.Symbol.EqualsIgnoreCase(symbol) && pair.Value.Depth == channelDepth)
				.Select(static pair => (pair.Key, pair.Value))];
		foreach (var (id, subscription) in subscriptions)
			await SendFuturesBookAsync(symbol, depth.Bids, depth.Asks, time, id,
				subscription.Depth, cancellationToken);
	}

	private async ValueTask OnFuturesTradeAsync(string symbol, BitunixWsTrade trade,
		long time, CancellationToken cancellationToken)
	{
		(long Id, MarketSubscription Subscription)[] ticks;
		(long Id, MarketSubscription Subscription)[] level1;
		using (_sync.EnterScope())
		{
			ticks = [.. _tickSubscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(symbol)).Select(static pair => (pair.Key, pair.Value))];
			level1 = [.. _level1Subscriptions.Where(pair =>
				pair.Value.Section == BitunixSections.Futures &&
				pair.Value.Symbol.EqualsIgnoreCase(symbol)).Select(static pair => (pair.Key, pair.Value))];
		}
		foreach (var (id, _) in ticks)
			await SendFuturesTradeAsync(symbol, trade, time, id, cancellationToken);
		foreach (var (id, _) in level1)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = symbol.ToStockSharp(BitunixSections.Futures),
				ServerTime = trade.Time?.UtcDateTime ??
					(time > 0 ? time.FromMilliseconds() : CurrentTime),
				OriginalTransactionId = id,
			}.TryAdd(Level1Fields.LastTradePrice, trade.Price)
			 .TryAdd(Level1Fields.LastTradeVolume, trade.Volume), cancellationToken);
	}

	private async ValueTask OnFuturesCandleAsync(string symbol, string channel,
		BitunixWsCandle candle, long time, CancellationToken cancellationToken)
	{
		var timeFrame = channel.FromWsInterval();
		(long Id, CandleSubscription Subscription)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _candleSubscriptions.Where(pair =>
				pair.Value.Section == BitunixSections.Futures &&
				pair.Value.Symbol.EqualsIgnoreCase(symbol) && pair.Value.TimeFrame == timeFrame)
				.Select(static pair => (pair.Key, pair.Value))];
		var eventTime = time > 0 ? time.FromMilliseconds() : CurrentTime;
		var openTime = eventTime.Floor(timeFrame);
		foreach (var (id, _) in subscriptions)
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				SecurityId = symbol.ToStockSharp(BitunixSections.Futures),
				OpenTime = openTime,
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.BaseVolume,
				State = DateTime.UtcNow >= openTime + timeFrame
					? CandleStates.Finished
					: CandleStates.Active,
				OriginalTransactionId = id,
			}, cancellationToken);
	}

	private ValueTask SendSpotLevel1Async(string symbol, decimal? lastPrice,
		BitunixSpotDepth book, long originalTransactionId, CancellationToken cancellationToken)
	{
		var bestBid = book?.Bids?.FirstOrDefault();
		var bestAsk = book?.Asks?.FirstOrDefault();
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(BitunixSections.Spot),
			ServerTime = book?.Time.UtcDateTime ?? CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}.TryAdd(Level1Fields.LastTradePrice, lastPrice)
		 .TryAdd(Level1Fields.BestBidPrice, bestBid?.Price)
		 .TryAdd(Level1Fields.BestBidVolume, bestBid?.Volume)
		 .TryAdd(Level1Fields.BestAskPrice, bestAsk?.Price)
		 .TryAdd(Level1Fields.BestAskVolume, bestAsk?.Volume), cancellationToken);
	}

	private ValueTask SendFuturesTickerAsync(BitunixFuturesTicker ticker,
		long originalTransactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ticker.Symbol.ToStockSharp(BitunixSections.Futures),
			ServerTime = CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}.TryAdd(Level1Fields.LastTradePrice,
			ticker.LastPrice != 0 ? ticker.LastPrice : ticker.Last)
		 .TryAdd(Level1Fields.OpenPrice, ticker.Open)
		 .TryAdd(Level1Fields.HighPrice, ticker.High)
		 .TryAdd(Level1Fields.LowPrice, ticker.Low)
		 .TryAdd(Level1Fields.Volume, ticker.BaseVolume)
		 .TryAdd(Level1Fields.SettlementPrice, ticker.MarkPrice), cancellationToken);

	private ValueTask SendSpotBookAsync(string symbol, BitunixSpotDepth book,
		long originalTransactionId, int depth, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(BitunixSections.Spot),
			ServerTime = book?.Time.UtcDateTime ?? CurrentTime,
			Bids = [.. (book?.Bids ?? []).Take(depth).Select(static level =>
				new QuoteChange(level.Price, level.Volume))],
			Asks = [.. (book?.Asks ?? []).Take(depth).Select(static level =>
				new QuoteChange(level.Price, level.Volume))],
			State = QuoteChangeStates.SnapshotComplete,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);

	private ValueTask SendFuturesBookAsync(string symbol, IEnumerable<BitunixBookLevel> bids,
		IEnumerable<BitunixBookLevel> asks, long time, long originalTransactionId, int depth,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(BitunixSections.Futures),
			ServerTime = time > 0 ? time.FromMilliseconds() : CurrentTime,
			Bids = [.. (bids ?? []).Take(depth).Select(static level =>
				new QuoteChange(level.Price, level.Volume))],
			Asks = [.. (asks ?? []).Take(depth).Select(static level =>
				new QuoteChange(level.Price, level.Volume))],
			State = QuoteChangeStates.SnapshotComplete,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);

	private ValueTask SendFuturesTradeAsync(string symbol, BitunixWsTrade trade, long time,
		long originalTransactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = symbol.ToStockSharp(BitunixSections.Futures),
			ServerTime = trade.Time?.UtcDateTime ??
				(time > 0 ? time.FromMilliseconds() : CurrentTime),
			TradeId = Interlocked.Increment(ref _tradeIdSeed),
			TradePrice = trade.Price,
			TradeVolume = trade.Volume,
			OriginSide = trade.Side.ToSide(),
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);

	private ValueTask SendSpotCandleAsync(string symbol, BitunixSpotCandle candle,
		TimeSpan timeFrame, long originalTransactionId, CancellationToken cancellationToken)
	{
		var openTime = candle.Time.UtcDateTime;
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = symbol.ToStockSharp(BitunixSections.Spot),
			OpenTime = openTime,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.Volume,
			State = DateTime.UtcNow >= openTime + timeFrame
				? CandleStates.Finished
				: CandleStates.Active,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private ValueTask SendFuturesCandleAsync(string symbol, BitunixFuturesCandle candle,
		TimeSpan timeFrame, long originalTransactionId, CancellationToken cancellationToken)
	{
		var openTime = candle.Time.FromMilliseconds();
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = symbol.ToStockSharp(BitunixSections.Futures),
			OpenTime = openTime,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.BaseVolume,
			State = DateTime.UtcNow >= openTime + timeFrame
				? CandleStates.Finished
				: CandleStates.Active,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private static bool IsInRange(DateTime time, DateTime from, DateTime to)
		=> time >= from && time <= to;
}
