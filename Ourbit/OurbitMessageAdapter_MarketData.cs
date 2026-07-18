namespace StockSharp.Ourbit;

public partial class OurbitMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;
		OurbitSpotSymbol[] spotSymbols;
		OurbitFuturesProduct[] futuresProducts;
		using (_sync.EnterScope())
		{
			spotSymbols = [.. _spotSymbols.Values];
			futuresProducts = [.. _futuresProducts.Values];
		}

		if (IsSectionEnabled(OurbitSections.Spot) &&
			(securityTypes.Count == 0 || securityTypes.Contains(SecurityTypes.CryptoCurrency)))
		{
			foreach (var symbol in spotSymbols.OrderBy(static symbol => symbol.Symbol))
			{
				if (!symbol.Status.EqualsIgnoreCase("ENABLED"))
					continue;
				var security = new SecurityMessage
				{
					SecurityId = symbol.Symbol.ToStockSharp(OurbitSections.Spot),
					Name = symbol.Symbol,
					SecurityType = SecurityTypes.CryptoCurrency,
					OriginalTransactionId = lookupMsg.TransactionId,
					PriceStep = symbol.QuotePrecision.PrecisionToStep(),
					Decimals = symbol.QuotePrecision,
					VolumeStep = symbol.BaseAssetPrecision.PrecisionToStep(),
				}.TryFillUnderlyingId(symbol.BaseAsset);
				if (!security.IsMatch(lookupMsg, securityTypes))
					continue;
				await SendOutMessageAsync(security, cancellationToken);
				if (--left <= 0)
					break;
			}
		}

		if (left > 0 && IsSectionEnabled(OurbitSections.Futures) &&
			(securityTypes.Count == 0 || securityTypes.Contains(SecurityTypes.Future)))
		{
			foreach (var product in futuresProducts.OrderBy(static product => product.Symbol))
			{
				if (product.State != 0 || product.Type != 1)
					continue;
				var security = new SecurityMessage
				{
					SecurityId = product.Symbol.ToStockSharp(OurbitSections.Futures),
					Name = product.EnglishName.IsEmpty(product.Symbol),
					SecurityType = SecurityTypes.Future,
					OriginalTransactionId = lookupMsg.TransactionId,
					PriceStep = product.PriceUnit,
					Decimals = product.PriceScale,
					VolumeStep = product.VolumeUnit,
					MinVolume = product.MinimumVolume,
					MaxVolume = product.MaximumVolume,
					Multiplier = product.ContractSize,
				}.TryFillUnderlyingId(product.BaseCurrency);
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
		var symbol = GetSymbol(mdMsg.SecurityId, section);
		if (section == OurbitSections.Spot)
		{
			var ticker = await SpotRestClient.GetTickerAsync(symbol, cancellationToken);
			await SendSpotTickerAsync(symbol, ticker, mdMsg.TransactionId, cancellationToken);
		}
		else
		{
			var ticker = (await FuturesRestClient.GetTickersAsync(cancellationToken) ?? [])
				.FirstOrDefault(item => item.Symbol.EqualsIgnoreCase(symbol));
			if (ticker is null)
				throw new InvalidDataException($"Ourbit returned no futures ticker for '{symbol}'.");
			await SendFuturesTickerAsync(ticker, mdMsg.TransactionId, cancellationToken);
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var keys = section == OurbitSections.Spot
			? new[] { new StreamKey(section, "ticker", symbol), new StreamKey(section, "trades", symbol) }
			: [new StreamKey(section, "ticker", symbol)];
		var subscribe = new List<StreamKey>();
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new() { Symbol = symbol, Section = section });
			foreach (var key in keys)
			{
				if (AddReference(_streamReferences, key))
					subscribe.Add(key);
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
		var symbol = GetSymbol(mdMsg.SecurityId, section);
		var depth = section == OurbitSections.Spot
			? NormalizeSpotDepth(mdMsg.MaxDepth)
			: NormalizeFuturesDepth(mdMsg.MaxDepth);
		if (section == OurbitSections.Spot)
		{
			var book = await SpotRestClient.GetDepthAsync(symbol, depth, cancellationToken);
			await SendSpotBookAsync(symbol, book?.Bids, book?.Asks, book?.Timestamp ?? 0,
				mdMsg.TransactionId, depth, cancellationToken);
		}
		else
		{
			var book = await LoadFuturesBookAsync(symbol, cancellationToken);
			await SendFuturesBookAsync(symbol, book.Bids, book.Asks, book.Timestamp,
				mdMsg.TransactionId, depth, QuoteChangeStates.SnapshotComplete, cancellationToken);
			using (_sync.EnterScope())
				_futuresBooks[symbol] = new() { Version = book.Version };
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}
		var key = new StreamKey(section, "depth", symbol,
			section == OurbitSections.Spot ? depth.ToString(CultureInfo.InvariantCulture) : null);
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
		var symbol = GetSymbol(mdMsg.SecurityId, section);
		var from = mdMsg.From?.ToUniversalTime();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var count = (mdMsg.Count ?? 100).Min(section == OurbitSections.Spot ? 1000 : 50)
			.Max(1).To<int>();
		if (section == OurbitSections.Spot)
		{
			var trades = (await SpotRestClient.GetTradesAsync(symbol, count, cancellationToken) ?? [])
				.Where(trade => IsInRange(trade.Time.FromMilliseconds(), from, to))
				.OrderBy(static trade => trade.Time);
			foreach (var trade in trades)
				await SendSpotTradeAsync(symbol, trade, mdMsg.TransactionId, cancellationToken);
		}
		else
		{
			var trades = (await FuturesRestClient.GetTradesAsync(symbol, cancellationToken) ?? [])
				.Where(trade => IsInRange(trade.Time.FromMilliseconds(), from, to))
				.OrderBy(static trade => trade.Time)
				.TakeLast(count);
			foreach (var trade in trades)
				await SendFuturesTradeAsync(symbol, trade, mdMsg.TransactionId, cancellationToken);
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}
		var key = new StreamKey(section, "trades", symbol);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new() { Symbol = symbol, Section = section });
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
		var symbol = GetSymbol(mdMsg.SecurityId, section);
		var timeFrame = mdMsg.GetTimeFrame();
		var wsInterval = timeFrame.ToWsInterval();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var count = (mdMsg.Count ?? 500).Min(1000).Max(1).To<int>();
		var from = mdMsg.From?.ToUniversalTime() ?? to - TimeSpan.FromTicks(timeFrame.Ticks * count);
		if (section == OurbitSections.Spot)
		{
			var candles = await SpotRestClient.GetCandlesAsync(symbol, timeFrame.ToSpotInterval(),
				new DateTimeOffset(from).ToUnixTimeMilliseconds(),
				new DateTimeOffset(to).ToUnixTimeMilliseconds(), count, cancellationToken) ?? [];
			foreach (var candle in candles.OrderBy(static candle => candle.OpenTime))
				await SendSpotCandleAsync(symbol, candle, timeFrame, mdMsg.TransactionId,
					cancellationToken);
		}
		else
		{
			var response = await FuturesRestClient.GetCandlesAsync(symbol, wsInterval,
				new DateTimeOffset(to).ToUnixTimeSeconds(), cancellationToken);
			var candles = (response?.ToCandles() ?? [])
				.Where(candle => IsInRange(ToWireTime(candle.Time), from, to))
				.OrderBy(static candle => candle.Time)
				.TakeLast(count);
			foreach (var candle in candles)
				await SendFuturesCandleAsync(symbol, candle, timeFrame, mdMsg.TransactionId,
					cancellationToken);
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}
		var key = new StreamKey(section, "candles", symbol, wsInterval);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				Section = section,
				TimeFrame = timeFrame,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		if (subscribe)
			await ChangeStreamAsync(key, true, cancellationToken);
	}

	private async ValueTask ChangeStreamAsync(StreamKey key, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		if (key.Section == OurbitSections.Spot)
		{
			var channel = key.Topic switch
			{
				"ticker" => $"spot@public.bookTicker.v3.api@{key.Symbol}",
				"trades" => $"spot@public.deals.v3.api@{key.Symbol}",
				"depth" => $"spot@public.limit.depth.v3.api@{key.Symbol}@{key.Argument}",
				"candles" => $"spot@public.kline.v3.api@{key.Symbol}@{key.Argument}",
				_ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
			};
			if (isSubscribe)
				await _spotWsClient.SubscribePublicAsync(channel, cancellationToken);
			else
				await _spotWsClient.UnsubscribePublicAsync(channel, cancellationToken);
			return;
		}

		if (isSubscribe)
		{
			switch (key.Topic)
			{
				case "ticker": await _futuresWsClient.SubscribeTickerAsync(key.Symbol, cancellationToken); break;
				case "trades": await _futuresWsClient.SubscribeTradesAsync(key.Symbol, cancellationToken); break;
				case "depth": await _futuresWsClient.SubscribeDepthAsync(key.Symbol, cancellationToken); break;
				case "candles": await _futuresWsClient.SubscribeCandlesAsync(key.Symbol, key.Argument, cancellationToken); break;
				default: throw new ArgumentOutOfRangeException(nameof(key), key, null);
			}
		}
		else
		{
			switch (key.Topic)
			{
				case "ticker": await _futuresWsClient.UnsubscribeTickerAsync(key.Symbol, cancellationToken); break;
				case "trades": await _futuresWsClient.UnsubscribeTradesAsync(key.Symbol, cancellationToken); break;
				case "depth": await _futuresWsClient.UnsubscribeDepthAsync(key.Symbol, cancellationToken); break;
				case "candles": await _futuresWsClient.UnsubscribeCandlesAsync(key.Symbol, key.Argument, cancellationToken); break;
				default: throw new ArgumentOutOfRangeException(nameof(key), key, null);
			}
		}
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var unsubscribe = new List<StreamKey>();
		using (_sync.EnterScope())
		{
			if (!_level1Subscriptions.Remove(transactionId, out subscription))
				return;
			var keys = subscription.Section == OurbitSections.Spot
				? new[] { new StreamKey(subscription.Section, "ticker", subscription.Symbol),
					new StreamKey(subscription.Section, "trades", subscription.Symbol) }
				: [new StreamKey(subscription.Section, "ticker", subscription.Symbol)];
			foreach (var key in keys)
			{
				if (ReleaseReference(_streamReferences, key))
					unsubscribe.Add(key);
			}
		}
		foreach (var key in unsubscribe)
			await ChangeStreamAsync(key, false, cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription subscription;
		StreamKey key;
		bool unsubscribe;
		using (_sync.EnterScope())
		{
			if (!_depthSubscriptions.Remove(transactionId, out subscription))
				return;
			key = new(subscription.Section, "depth", subscription.Symbol,
				subscription.Section == OurbitSections.Spot
					? subscription.Depth.ToString(CultureInfo.InvariantCulture)
					: null);
			unsubscribe = ReleaseReference(_streamReferences, key);
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
			key = new(subscription.Section, "trades", subscription.Symbol);
			unsubscribe = ReleaseReference(_streamReferences, key);
		}
		if (unsubscribe)
			await ChangeStreamAsync(key, false, cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		CandleSubscription subscription;
		StreamKey key;
		bool unsubscribe;
		using (_sync.EnterScope())
		{
			if (!_candleSubscriptions.Remove(transactionId, out subscription))
				return;
			key = new(subscription.Section, "candles", subscription.Symbol,
				subscription.TimeFrame.ToWsInterval());
			unsubscribe = ReleaseReference(_streamReferences, key);
		}
		if (unsubscribe)
			await ChangeStreamAsync(key, false, cancellationToken);
	}

	private async ValueTask OnSpotTickerAsync(string symbol, OurbitSpotWsBookTicker ticker, long time,
		CancellationToken cancellationToken)
	{
		(long Id, MarketSubscription Subscription)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions.Where(pair =>
				pair.Value.Section == OurbitSections.Spot && pair.Value.Symbol.EqualsIgnoreCase(symbol))
				.Select(static pair => (pair.Key, pair.Value))];
		foreach (var (id, _) in subscriptions)
			await SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(OurbitSections.Spot),
			ServerTime = time > 0 ? time.FromMilliseconds() : CurrentTime,
			OriginalTransactionId = id,
		}.TryAdd(Level1Fields.BestBidPrice, ticker.BidPrice)
		 .TryAdd(Level1Fields.BestBidVolume, ticker.BidVolume)
		 .TryAdd(Level1Fields.BestAskPrice, ticker.AskPrice)
		 .TryAdd(Level1Fields.BestAskVolume, ticker.AskVolume), cancellationToken);
	}

	private async ValueTask OnSpotDepthAsync(string symbol, OurbitSpotWsDepth depth, long time,
		CancellationToken cancellationToken)
	{
		(long Id, DepthSubscription Subscription)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions.Where(pair =>
				pair.Value.Section == OurbitSections.Spot && pair.Value.Symbol.EqualsIgnoreCase(symbol))
				.Select(static pair => (pair.Key, pair.Value))];
		foreach (var (id, subscription) in subscriptions)
			await SendSpotBookAsync(symbol, depth.Bids, depth.Asks, time, id,
				subscription.Depth, cancellationToken);
	}

	private async ValueTask OnSpotTradeAsync(string symbol, OurbitSpotWsTrade trade,
		CancellationToken cancellationToken)
	{
		(long Id, MarketSubscription Subscription)[] ticks;
		(long Id, MarketSubscription Subscription)[] level1;
		using (_sync.EnterScope())
		{
			ticks = [.. _tickSubscriptions.Where(pair => pair.Value.Section == OurbitSections.Spot &&
				pair.Value.Symbol.EqualsIgnoreCase(symbol)).Select(static pair => (pair.Key, pair.Value))];
			level1 = [.. _level1Subscriptions.Where(pair => pair.Value.Section == OurbitSections.Spot &&
				pair.Value.Symbol.EqualsIgnoreCase(symbol)).Select(static pair => (pair.Key, pair.Value))];
		}
		foreach (var (id, _) in ticks)
			await SendSpotTradeAsync(symbol, trade, id, cancellationToken);
		foreach (var (id, _) in level1)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = symbol.ToStockSharp(OurbitSections.Spot),
				ServerTime = trade.Time.FromMilliseconds(),
				OriginalTransactionId = id,
			}.TryAdd(Level1Fields.LastTradePrice, trade.Price)
			 .TryAdd(Level1Fields.LastTradeVolume, trade.Volume), cancellationToken);
	}

	private async ValueTask OnSpotCandleAsync(string symbol, OurbitSpotWsKline candle, long time,
		CancellationToken cancellationToken)
	{
		_ = time;
		var timeFrame = candle.Interval.FromWsInterval();
		(long Id, CandleSubscription Subscription)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _candleSubscriptions.Where(pair =>
				pair.Value.Section == OurbitSections.Spot && pair.Value.Symbol.EqualsIgnoreCase(symbol) &&
				pair.Value.TimeFrame == timeFrame).Select(static pair => (pair.Key, pair.Value))];
		foreach (var (id, _) in subscriptions)
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				SecurityId = symbol.ToStockSharp(OurbitSections.Spot),
				OpenTime = candle.OpenTime.FromSeconds(),
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				State = DateTime.UtcNow >= candle.CloseTime.FromSeconds()
					? CandleStates.Finished
					: CandleStates.Active,
				OriginalTransactionId = id,
			}, cancellationToken);
	}

	private async ValueTask OnFuturesTickerAsync(OurbitFuturesTicker ticker,
		CancellationToken cancellationToken)
	{
		(long Id, MarketSubscription Subscription)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions.Where(pair =>
				pair.Value.Section == OurbitSections.Futures &&
				pair.Value.Symbol.EqualsIgnoreCase(ticker.Symbol)).Select(static pair => (pair.Key, pair.Value))];
		foreach (var (id, _) in subscriptions)
			await SendFuturesTickerAsync(ticker, id, cancellationToken);
	}

	private async ValueTask OnFuturesDepthAsync(string symbol, OurbitFuturesDepth depth,
		CancellationToken cancellationToken)
	{
		(long Id, DepthSubscription Subscription)[] subscriptions;
		var shouldRestore = false;
		using (_sync.EnterScope())
		{
			if (!_futuresBooks.TryGetValue(symbol, out var state))
				return;
			if (depth.Version <= state.Version)
				return;
			if (state.Version != 0 && depth.Version != state.Version + 1)
			{
				if (!state.IsRestoring)
				{
					state.IsRestoring = true;
					shouldRestore = true;
				}
				subscriptions = [];
			}
			else
			{
				state.Version = depth.Version;
				subscriptions = [.. _depthSubscriptions.Where(pair =>
					pair.Value.Section == OurbitSections.Futures &&
					pair.Value.Symbol.EqualsIgnoreCase(symbol)).Select(static pair => (pair.Key, pair.Value))];
			}
		}
		if (shouldRestore)
		{
			await RestoreFuturesBookAsync(symbol, cancellationToken);
			return;
		}
		foreach (var (id, subscription) in subscriptions)
			await SendFuturesBookAsync(symbol, depth.Bids, depth.Asks, depth.Timestamp, id,
				subscription.Depth, QuoteChangeStates.Increment, cancellationToken);
	}

	private async ValueTask RestoreFuturesBookAsync(string symbol,
		CancellationToken cancellationToken)
	{
		try
		{
			var book = await LoadFuturesBookAsync(symbol, cancellationToken);
			(long Id, DepthSubscription Subscription)[] subscriptions;
			using (_sync.EnterScope())
			{
				if (_futuresBooks.TryGetValue(symbol, out var state))
				{
					state.Version = book.Version;
					state.IsRestoring = false;
				}
				subscriptions = [.. _depthSubscriptions.Where(pair =>
					pair.Value.Section == OurbitSections.Futures &&
					pair.Value.Symbol.EqualsIgnoreCase(symbol)).Select(static pair => (pair.Key, pair.Value))];
			}
			foreach (var (id, subscription) in subscriptions)
				await SendFuturesBookAsync(symbol, book.Bids, book.Asks, book.Timestamp, id,
					subscription.Depth, QuoteChangeStates.SnapshotComplete, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (_futuresBooks.TryGetValue(symbol, out var state))
					state.IsRestoring = false;
			}
			throw;
		}
	}

	private async ValueTask OnFuturesTradeAsync(string symbol, OurbitFuturesTrade trade,
		CancellationToken cancellationToken)
	{
		(long Id, MarketSubscription Subscription)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _tickSubscriptions.Where(pair =>
				pair.Value.Section == OurbitSections.Futures &&
				pair.Value.Symbol.EqualsIgnoreCase(symbol)).Select(static pair => (pair.Key, pair.Value))];
		foreach (var (id, _) in subscriptions)
			await SendFuturesTradeAsync(symbol, trade, id, cancellationToken);
	}

	private async ValueTask OnFuturesCandleAsync(string symbol, OurbitFuturesWsCandle candle,
		CancellationToken cancellationToken)
	{
		var timeFrame = candle.Interval.FromWsInterval();
		(long Id, CandleSubscription Subscription)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _candleSubscriptions.Where(pair =>
				pair.Value.Section == OurbitSections.Futures &&
				pair.Value.Symbol.EqualsIgnoreCase(symbol) && pair.Value.TimeFrame == timeFrame)
				.Select(static pair => (pair.Key, pair.Value))];
		var openTime = ToWireTime(candle.Time);
		foreach (var (id, _) in subscriptions)
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				SecurityId = symbol.ToStockSharp(OurbitSections.Futures),
				OpenTime = openTime,
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				State = DateTime.UtcNow >= openTime + timeFrame ? CandleStates.Finished : CandleStates.Active,
				OriginalTransactionId = id,
			}, cancellationToken);
	}

	private ValueTask SendSpotTickerAsync(string symbol, OurbitSpotTicker ticker,
		long originalTransactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(OurbitSections.Spot),
			ServerTime = ticker.CloseTime > 0 ? ticker.CloseTime.FromMilliseconds() : CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice)
		 .TryAdd(Level1Fields.BestBidPrice, ticker.BidPrice)
		 .TryAdd(Level1Fields.BestBidVolume, ticker.BidVolume)
		 .TryAdd(Level1Fields.BestAskPrice, ticker.AskPrice)
		 .TryAdd(Level1Fields.BestAskVolume, ticker.AskVolume)
		 .TryAdd(Level1Fields.OpenPrice, ticker.OpenPrice)
		 .TryAdd(Level1Fields.HighPrice, ticker.HighPrice)
		 .TryAdd(Level1Fields.LowPrice, ticker.LowPrice)
		 .TryAdd(Level1Fields.Volume, ticker.Volume)
		 .TryAdd(Level1Fields.Change, ticker.PriceChange), cancellationToken);

	private ValueTask SendFuturesTickerAsync(OurbitFuturesTicker ticker,
		long originalTransactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ticker.Symbol.ToStockSharp(OurbitSections.Futures),
			ServerTime = ticker.Timestamp > 0 ? ticker.Timestamp.FromMilliseconds() : CurrentTime,
			OriginalTransactionId = originalTransactionId,
		}.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice)
		 .TryAdd(Level1Fields.BestBidPrice, ticker.BidPrice)
		 .TryAdd(Level1Fields.BestAskPrice, ticker.AskPrice)
		 .TryAdd(Level1Fields.HighPrice, ticker.HighPrice)
		 .TryAdd(Level1Fields.LowPrice, ticker.LowPrice)
		 .TryAdd(Level1Fields.Volume, ticker.Volume)
		 .TryAdd(Level1Fields.OpenInterest, ticker.OpenInterest)
		 .TryAdd(Level1Fields.Change, ticker.Change)
		 .TryAdd(Level1Fields.Index, ticker.IndexPrice)
		 .TryAdd(Level1Fields.SettlementPrice, ticker.MarkPrice), cancellationToken);

	private ValueTask SendSpotTradeAsync(string symbol, OurbitSpotTrade trade,
		long originalTransactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = symbol.ToStockSharp(OurbitSections.Spot),
			ServerTime = trade.Time.FromMilliseconds(),
			TradeStringId = trade.Id,
			TradeId = trade.Id.IsEmpty() ? Interlocked.Increment(ref _tradeIdSeed) : null,
			TradePrice = trade.Price,
			TradeVolume = trade.Volume,
			OriginSide = trade.IsBuyerMaker ? Sides.Sell : Sides.Buy,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);

	private ValueTask SendSpotTradeAsync(string symbol, OurbitSpotWsTrade trade,
		long originalTransactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = symbol.ToStockSharp(OurbitSections.Spot),
			ServerTime = trade.Time.FromMilliseconds(),
			TradeId = Interlocked.Increment(ref _tradeIdSeed),
			TradePrice = trade.Price,
			TradeVolume = trade.Volume,
			OriginSide = trade.Side == 1 ? Sides.Buy : Sides.Sell,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);

	private ValueTask SendFuturesTradeAsync(string symbol, OurbitFuturesTrade trade,
		long originalTransactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = symbol.ToStockSharp(OurbitSections.Futures),
			ServerTime = trade.Time.FromMilliseconds(),
			TradeId = Interlocked.Increment(ref _tradeIdSeed),
			TradePrice = trade.Price,
			TradeVolume = trade.Volume,
			OriginSide = trade.Side == 1 ? Sides.Buy : Sides.Sell,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);

	private ValueTask SendSpotBookAsync(string symbol, IEnumerable<OurbitSpotBookLevel> bids,
		IEnumerable<OurbitSpotBookLevel> asks, long time, long originalTransactionId, int depth,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(OurbitSections.Spot),
			ServerTime = time > 0 ? time.FromMilliseconds() : CurrentTime,
			Bids = [.. (bids ?? []).Take(depth).Select(static level =>
				new QuoteChange(level.Price, level.Volume))],
			Asks = [.. (asks ?? []).Take(depth).Select(static level =>
				new QuoteChange(level.Price, level.Volume))],
			State = QuoteChangeStates.SnapshotComplete,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);

	private ValueTask SendSpotBookAsync(string symbol, IEnumerable<OurbitSpotWsBookLevel> bids,
		IEnumerable<OurbitSpotWsBookLevel> asks, long time, long originalTransactionId, int depth,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(OurbitSections.Spot),
			ServerTime = time > 0 ? time.FromMilliseconds() : CurrentTime,
			Bids = [.. (bids ?? []).Take(depth).Select(static level =>
				new QuoteChange(level.Price, level.Volume))],
			Asks = [.. (asks ?? []).Take(depth).Select(static level =>
				new QuoteChange(level.Price, level.Volume))],
			State = QuoteChangeStates.SnapshotComplete,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);

	private ValueTask SendFuturesBookAsync(string symbol,
		IEnumerable<OurbitFuturesBookLevel> bids, IEnumerable<OurbitFuturesBookLevel> asks,
		long time, long originalTransactionId, int depth, QuoteChangeStates state,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(OurbitSections.Futures),
			ServerTime = time > 0 ? time.FromMilliseconds() : CurrentTime,
			Bids = [.. (bids ?? []).Take(depth).Select(static level =>
				new QuoteChange(level.Price, level.Volume, level.OrderCount))],
			Asks = [.. (asks ?? []).Take(depth).Select(static level =>
				new QuoteChange(level.Price, level.Volume, level.OrderCount))],
			State = state,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);

	private ValueTask SendSpotCandleAsync(string symbol, OurbitSpotKline candle,
		TimeSpan timeFrame, long originalTransactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = symbol.ToStockSharp(OurbitSections.Spot),
			OpenTime = candle.OpenTime.FromMilliseconds(),
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.Volume,
			State = DateTime.UtcNow >= candle.CloseTime.FromMilliseconds()
				? CandleStates.Finished
				: CandleStates.Active,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);

	private ValueTask SendFuturesCandleAsync(string symbol, OurbitFuturesCandle candle,
		TimeSpan timeFrame, long originalTransactionId, CancellationToken cancellationToken)
	{
		var openTime = ToWireTime(candle.Time);
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = symbol.ToStockSharp(OurbitSections.Futures),
			OpenTime = openTime,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.Volume,
			State = DateTime.UtcNow >= openTime + timeFrame ? CandleStates.Finished : CandleStates.Active,
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private async ValueTask<OurbitFuturesDepth> LoadFuturesBookAsync(string symbol,
		CancellationToken cancellationToken)
	{
		OurbitFuturesProduct product;
		using (_sync.EnterScope())
			_futuresProducts.TryGetValue(symbol, out product);
		if (product is null)
			throw new InvalidOperationException($"Unknown Ourbit futures symbol '{symbol}'.");
		var step = product.DepthSteps?.FirstOrDefault().IsEmpty(product.PriceUnit.ToWire());
		return await FuturesRestClient.GetDepthAsync(symbol, step, cancellationToken)
			?? throw new InvalidDataException($"Ourbit returned no order book for '{symbol}'.");
	}

	private static bool IsInRange(DateTime time, DateTime? from, DateTime to)
		=> (from is null || time >= from.Value) && time <= to;

	private static DateTime ToWireTime(long value)
		=> value >= 100000000000L ? value.FromMilliseconds() : value.FromSeconds();
}
