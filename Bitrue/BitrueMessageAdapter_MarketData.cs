namespace StockSharp.Bitrue;

public partial class BitrueMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsureConnected();
		await EnsureInstrumentsAsync(cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		if (IsSectionEnabled(BitrueSections.Spot))
		{
			BitrueSpotSymbol[] symbols;
			using (_sync.EnterScope())
				symbols = [.. _spotSymbols.Values.OrderBy(static item => item.Symbol)];
			foreach (var symbol in symbols)
			{
				var priceFilter = symbol.Filters?.FirstOrDefault(static filter =>
					filter.FilterType.EqualsIgnoreCase("PRICE_FILTER"));
				var volumeFilter = symbol.Filters?.FirstOrDefault(static filter =>
					filter.FilterType.EqualsIgnoreCase("LOT_SIZE"));
				var security = new SecurityMessage
				{
					SecurityId = symbol.Symbol.ToStockSharp(BitrueSections.Spot),
					Name = $"{symbol.BaseAsset?.ToUpperInvariant()}/{symbol.QuoteAsset?.ToUpperInvariant()}",
					SecurityType = SecurityTypes.CryptoCurrency,
					OriginalTransactionId = lookupMsg.TransactionId,
					PriceStep = priceFilter?.TickSize.ToDecimal(),
					VolumeStep = volumeFilter?.StepSize.ToDecimal(),
					MinVolume = volumeFilter?.MinimumQuantity.ToDecimal(),
					MaxVolume = volumeFilter?.MaximumQuantity.ToDecimal(),
				}.TryFillUnderlyingId(symbol.BaseAsset?.ToUpperInvariant());
				if (!security.IsMatch(lookupMsg, securityTypes))
					continue;
				await SendOutMessageAsync(security, cancellationToken);
				if (--left <= 0)
					break;
			}
		}

		if (left > 0 && IsSectionEnabled(BitrueSections.Futures))
		{
			BitrueFuturesContract[] contracts;
			using (_sync.EnterScope())
				contracts = [.. _futuresContracts.Values.OrderBy(static item => item.Symbol)];
			foreach (var contract in contracts)
			{
				var security = new SecurityMessage
				{
					SecurityId = contract.Symbol.ToStockSharp(BitrueSections.Futures),
					Name = $"{contract.MultiplierCoin?.ToUpperInvariant()}/USDT Perpetual",
					SecurityType = SecurityTypes.Future,
					OriginalTransactionId = lookupMsg.TransactionId,
					PriceStep = contract.PricePrecision.PriceStepFromPrecision(),
					VolumeStep = 1m,
					MinVolume = contract.MinimumOrderVolume,
					MaxVolume = contract.MaximumLimitVolume,
					Multiplier = contract.Multiplier,
				}.TryFillUnderlyingId(contract.MultiplierCoin?.ToUpperInvariant());
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

		await EnsureInstrumentsAsync(cancellationToken);
		var section = ResolveSection(mdMsg.SecurityId);
		var symbol = GetSymbol(mdMsg.SecurityId, section);
		ValidateSymbol(symbol, section);
		if (section == BitrueSections.Spot)
		{
			var ticker = (await SpotRestClient.GetTickersAsync(symbol, cancellationToken) ?? [])
				.FirstOrDefault(item => item?.Symbol.EqualsIgnoreCase(symbol) == true);
			if (ticker is null)
				throw new InvalidDataException($"Bitrue spot returned no ticker for '{symbol}'.");
			await SendSpotTickerAsync(ticker, mdMsg.TransactionId, cancellationToken);
		}
		else
		{
			var ticker = await FuturesRestClient.GetTickerAsync(symbol, cancellationToken);
			if (ticker is null)
				throw new InvalidDataException($"Bitrue futures returned no ticker for '{symbol}'.");
			await SendFuturesTickerAsync(symbol, ticker, mdMsg.TransactionId, cancellationToken);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var key = new StreamKey(section, BitrueWsTopics.Ticker, symbol, default);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				Section = section,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		if (subscribe && section == BitrueSections.Futures)
			await GetPublicClient(section).SubscribeTickerAsync(symbol, cancellationToken);
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

		await EnsureInstrumentsAsync(cancellationToken);
		var section = ResolveSection(mdMsg.SecurityId);
		var symbol = GetSymbol(mdMsg.SecurityId, section);
		ValidateSymbol(symbol, section);
		var depth = NormalizeDepth(mdMsg.MaxDepth, section);
		var book = section == BitrueSections.Spot
			? await SpotRestClient.GetBookAsync(symbol, depth, cancellationToken)
			: await FuturesRestClient.GetBookAsync(symbol, depth, cancellationToken);
		if (book is null)
			throw new InvalidDataException($"Bitrue returned no order book for '{symbol}'.");
		await SendBookAsync(section, symbol, book.Bids, book.Asks,
			book.Timestamp > 0 ? book.Timestamp.ToUtcTime() : CurrentTime,
			mdMsg.TransactionId, depth, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var key = new StreamKey(section, BitrueWsTopics.Depth, symbol, default);
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
			await GetPublicClient(section).SubscribeDepthAsync(symbol, cancellationToken);
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

		await EnsureInstrumentsAsync(cancellationToken);
		var section = ResolveSection(mdMsg.SecurityId);
		var symbol = GetSymbol(mdMsg.SecurityId, section);
		ValidateSymbol(symbol, section);
		var from = mdMsg.From?.ToUniversalTime() ?? DateTime.MinValue;
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var limit = (mdMsg.Count ?? (section == BitrueSections.Spot ? 100 : 300))
			.Min(section == BitrueSections.Spot ? 1000 : 300).Max(1).To<int>();

		if (section == BitrueSections.Spot)
		{
			var trades = await SpotRestClient.GetTradesAsync(symbol, limit, cancellationToken) ?? [];
			foreach (var trade in trades.Where(static trade => trade is not null)
				.Where(trade => trade.Timestamp.ToUtcTime() >= from &&
					trade.Timestamp.ToUtcTime() <= to)
				.OrderBy(static trade => trade.Timestamp))
				await SendSpotTradeAsync(symbol, trade, mdMsg.TransactionId, cancellationToken);
			var latestTrade = trades.Where(static trade => trade is not null)
				.Select(static trade => (long?)trade.TradeId).Max();
			if (latestTrade is long latestTradeId)
			{
				using (_sync.EnterScope())
					_spotLastTradeIds[symbol] = latestTradeId;
			}
		}
		else
		{
			var trades = await GetPublicClient(section).RequestTradesAsync(symbol, limit,
				cancellationToken);
			foreach (var trade in trades.Where(static trade => trade is not null)
				.Where(trade => trade.Timestamp <= 0 ||
					trade.Timestamp.ToUtcTime() >= from && trade.Timestamp.ToUtcTime() <= to))
				await SendFuturesTradeAsync(symbol, trade, mdMsg.TransactionId,
					cancellationToken);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var key = new StreamKey(section, BitrueWsTopics.Trades, symbol, default);
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
		if (subscribe && section == BitrueSections.Futures)
			await GetPublicClient(section).SubscribeTradesAsync(symbol, cancellationToken);
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

		await EnsureInstrumentsAsync(cancellationToken);
		var section = ResolveSection(mdMsg.SecurityId);
		var symbol = GetSymbol(mdMsg.SecurityId, section);
		ValidateSymbol(symbol, section);
		var timeFrame = mdMsg.GetTimeFrame();
		if (section == BitrueSections.Spot)
			_ = timeFrame.ToBitrueSpotInterval();
		else
			_ = timeFrame.ToBitrueFuturesInterval();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var requestedCount = GetCandleCount(mdMsg, timeFrame, to);
		var from = mdMsg.From?.ToUniversalTime() ??
			to - TimeSpan.FromTicks(timeFrame.Ticks * requestedCount);

		if (section == BitrueSections.Spot)
		{
			foreach (var candle in await LoadSpotCandlesAsync(symbol, timeFrame, from, to,
				requestedCount, cancellationToken))
				await SendSpotCandleAsync(symbol, candle, timeFrame, mdMsg.TransactionId,
					cancellationToken);
		}
		else
		{
			foreach (var candle in await LoadFuturesCandlesAsync(symbol, timeFrame, from, to,
				requestedCount, cancellationToken))
				await SendFuturesCandleAsync(symbol, candle, timeFrame, mdMsg.TransactionId,
					cancellationToken);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var key = new StreamKey(section, BitrueWsTopics.Candles, symbol, timeFrame);
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
		if (subscribe && section == BitrueSections.Futures)
			await GetPublicClient(section).SubscribeCandlesAsync(symbol, timeFrame,
				cancellationToken);
	}

	private async ValueTask EnsureInstrumentsAsync(CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
		{
			if (_instrumentsLoaded)
				return;
		}

		var spotSymbols = IsSectionEnabled(BitrueSections.Spot)
			? (await SpotRestClient.GetExchangeInfoAsync(cancellationToken))?.Symbols ?? []
			: [];
		var futuresContracts = IsSectionEnabled(BitrueSections.Futures)
			? await FuturesRestClient.GetContractsAsync(cancellationToken) ?? []
			: [];
		using (_sync.EnterScope())
		{
			foreach (var symbol in spotSymbols.Where(static symbol =>
				symbol?.Symbol.IsEmpty() == false && symbol.Status == BitrueSpotSymbolStatuses.Trading))
				_spotSymbols[symbol.Symbol] = symbol;
			foreach (var contract in futuresContracts.Where(static contract =>
				contract?.Symbol.IsEmpty() == false && contract.Status == 1 &&
				contract.ContractType.EqualsIgnoreCase("E")))
			{
				_futuresContracts[contract.Symbol] = contract;
				_futuresPrivateSymbols[contract.Symbol.ToPrivateWsSymbol()] = contract.Symbol;
			}
			_instrumentsLoaded = true;
		}
	}

	private void ValidateSymbol(string symbol, BitrueSections section)
	{
		bool exists;
		using (_sync.EnterScope())
			exists = section == BitrueSections.Spot
				? _spotSymbols.ContainsKey(symbol)
				: _futuresContracts.ContainsKey(symbol);
		if (!exists)
			throw new InvalidOperationException(
				$"Bitrue {section} symbol '{symbol}' is not active.");
	}

	private async ValueTask<BitrueSpotCandle[]> LoadSpotCandlesAsync(string symbol,
		TimeSpan timeFrame, DateTime from, DateTime to, int maximum,
		CancellationToken cancellationToken)
	{
		var result = new List<BitrueSpotCandle>();
		var timestamps = new HashSet<long>();
		var cursor = to.ToUnixMilliseconds() + 1;
		while (result.Count < maximum)
		{
			var limit = (maximum - result.Count).Min(1440).Max(1);
			var response = await SpotRestClient.GetCandlesAsync(new()
			{
				Symbol = symbol,
				Interval = timeFrame.ToBitrueSpotInterval(),
				EndIndex = cursor,
				Limit = limit,
			}, cancellationToken);
			var page = response?.Data ?? [];
			if (page.Length == 0)
				break;
			foreach (var candle in page)
			{
				var timestamp = candle.GetTimestamp();
				var time = timestamp.ToUtcTime();
				if (time >= from && time <= to && timestamps.Add(timestamp))
					result.Add(candle);
			}
			var earliest = page.Min(static candle => candle.GetTimestamp());
			if (earliest.ToUtcTime() <= from || earliest >= cursor || page.Length < limit)
				break;
			cursor = earliest;
		}
		return [.. result.OrderBy(static candle => candle.GetTimestamp()).TakeLast(maximum)];
	}

	private async ValueTask<BitrueFuturesCandle[]> LoadFuturesCandlesAsync(string symbol,
		TimeSpan timeFrame, DateTime from, DateTime to, int maximum,
		CancellationToken cancellationToken)
	{
		var result = new List<BitrueFuturesCandle>();
		var timestamps = new HashSet<long>();
		var cursor = new DateTimeOffset(to).ToUnixTimeSeconds() + 1;
		while (result.Count < maximum)
		{
			var limit = (maximum - result.Count).Min(300).Max(1);
			var page = await GetPublicClient(BitrueSections.Futures).RequestCandlesAsync(
				symbol, timeFrame, cursor, limit, cancellationToken);
			if (page.Length == 0)
				break;
			foreach (var candle in page)
			{
				var timestamp = candle.GetTimestamp();
				var time = timestamp.ToUtcTime();
				if (time >= from && time <= to && timestamps.Add(timestamp))
					result.Add(candle);
			}
			var earliest = page.Min(static candle => candle.GetTimestamp());
			var earliestSeconds = Math.Abs(earliest) < 100_000_000_000L
				? earliest
				: earliest / 1000;
			if (earliest.ToUtcTime() <= from || earliestSeconds >= cursor || page.Length < limit)
				break;
			cursor = earliestSeconds;
		}
		return [.. result.OrderBy(static candle => candle.GetTimestamp()).TakeLast(maximum)];
	}

	private async ValueTask PollSpotMarketDataAsync(CancellationToken cancellationToken)
	{
		MarketSubscription[] level1;
		MarketSubscription[] ticks;
		CandleSubscription[] candles;
		using (_sync.EnterScope())
		{
			level1 = [.. _level1Subscriptions.Values
				.Where(static item => item.Section == BitrueSections.Spot)
				.GroupBy(static item => item.Symbol, StringComparer.OrdinalIgnoreCase)
				.Select(static group => group.First())];
			ticks = [.. _tickSubscriptions.Values
				.Where(static item => item.Section == BitrueSections.Spot)
				.GroupBy(static item => item.Symbol, StringComparer.OrdinalIgnoreCase)
				.Select(static group => group.First())];
			candles = [.. _candleSubscriptions.Values
				.Where(static item => item.Section == BitrueSections.Spot)
				.GroupBy(static item => (item.Symbol, item.TimeFrame))
				.Select(static group => group.First())];
		}

		foreach (var subscription in level1)
		{
			var ticker = (await SpotRestClient.GetTickersAsync(subscription.Symbol,
				cancellationToken) ?? []).FirstOrDefault(item =>
					item?.Symbol.EqualsIgnoreCase(subscription.Symbol) == true);
			if (ticker is not null)
				await PublishSpotTickerAsync(ticker, cancellationToken);
		}

		foreach (var subscription in ticks)
		{
			var trades = await SpotRestClient.GetTradesAsync(subscription.Symbol, 100,
				cancellationToken) ?? [];
			long lastId;
			using (_sync.EnterScope())
				_spotLastTradeIds.TryGetValue(subscription.Symbol, out lastId);
			foreach (var trade in trades.Where(trade => trade is not null &&
				trade.TradeId > lastId)
				.OrderBy(static trade => trade.TradeId))
				await PublishSpotTradeAsync(subscription.Symbol, trade, cancellationToken);
			var latestTrade = trades.Where(static trade => trade is not null)
				.Select(static trade => (long?)trade.TradeId).Max();
			if (latestTrade is long latestTradeId)
			{
				using (_sync.EnterScope())
					_spotLastTradeIds[subscription.Symbol] = Math.Max(
						latestTradeId, lastId);
			}
		}

		foreach (var subscription in candles)
		{
			var response = await SpotRestClient.GetCandlesAsync(new()
			{
				Symbol = subscription.Symbol,
				Interval = subscription.TimeFrame.ToBitrueSpotInterval(),
				EndIndex = DateTime.UtcNow.ToUnixMilliseconds() + 1,
				Limit = 2,
			}, cancellationToken);
			foreach (var candle in response?.Data ?? [])
				await PublishSpotCandleAsync(subscription.Symbol, subscription.TimeFrame,
					candle, cancellationToken);
		}
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_level1Subscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences, new(subscription.Section,
					BitrueWsTopics.Ticker, subscription.Symbol, default));
		}
		if (unsubscribe && subscription.Section == BitrueSections.Futures)
			await GetPublicClient(subscription.Section).UnsubscribeTickerAsync(
				subscription.Symbol, cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_depthSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences, new(subscription.Section,
					BitrueWsTopics.Depth, subscription.Symbol, default));
		}
		if (unsubscribe)
			await GetPublicClient(subscription.Section).UnsubscribeDepthAsync(
				subscription.Symbol, cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_tickSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences, new(subscription.Section,
					BitrueWsTopics.Trades, subscription.Symbol, default));
		}
		if (unsubscribe && subscription.Section == BitrueSections.Futures)
			await GetPublicClient(subscription.Section).UnsubscribeTradesAsync(
				subscription.Symbol, cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		CandleSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_candleSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences, new(subscription.Section,
					BitrueWsTopics.Candles, subscription.Symbol, subscription.TimeFrame));
		}
		if (unsubscribe && subscription.Section == BitrueSections.Futures)
			await GetPublicClient(subscription.Section).UnsubscribeCandlesAsync(
				subscription.Symbol, subscription.TimeFrame, cancellationToken);
	}

	private async ValueTask OnBookAsync(BitrueSections section, string symbol,
		BitrueWsBook book, long timestamp, CancellationToken cancellationToken)
	{
		(long Id, int Depth)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions
				.Where(pair => pair.Value.Section == section &&
					pair.Value.Symbol.EqualsIgnoreCase(symbol))
				.Select(static pair => (pair.Key, pair.Value.Depth))];
		foreach (var subscription in subscriptions)
			await SendBookAsync(section, symbol, book.Bids, book.Asks,
				timestamp > 0 ? timestamp.ToUtcTime() : CurrentTime,
				subscription.Id, subscription.Depth, cancellationToken);
	}

	private async ValueTask OnFuturesTickerAsync(string symbol, BitrueWsTicker ticker,
		long timestamp, CancellationToken cancellationToken)
	{
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _level1Subscriptions
				.Where(pair => pair.Value.Section == BitrueSections.Futures &&
					pair.Value.Symbol.EqualsIgnoreCase(symbol))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendFuturesTickerAsync(symbol, ticker, timestamp, id, cancellationToken);
	}

	private async ValueTask OnFuturesTradeAsync(string symbol, BitrueWsTrade trade,
		long timestamp, CancellationToken cancellationToken)
	{
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _tickSubscriptions
				.Where(pair => pair.Value.Section == BitrueSections.Futures &&
					pair.Value.Symbol.EqualsIgnoreCase(symbol))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendFuturesTradeAsync(symbol, trade, id, cancellationToken,
				timestamp);
	}

	private async ValueTask OnFuturesCandleAsync(string symbol, TimeSpan timeFrame,
		BitrueFuturesCandle candle, long timestamp, CancellationToken cancellationToken)
	{
		_ = timestamp;
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _candleSubscriptions
				.Where(pair => pair.Value.Section == BitrueSections.Futures &&
					pair.Value.Symbol.EqualsIgnoreCase(symbol) &&
					pair.Value.TimeFrame == timeFrame)
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendFuturesCandleAsync(symbol, candle, timeFrame, id, cancellationToken);
	}

	private async ValueTask PublishSpotTickerAsync(BitrueSpotTicker ticker,
		CancellationToken cancellationToken)
	{
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _level1Subscriptions
				.Where(pair => pair.Value.Section == BitrueSections.Spot &&
					pair.Value.Symbol.EqualsIgnoreCase(ticker.Symbol))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendSpotTickerAsync(ticker, id, cancellationToken);
	}

	private async ValueTask PublishSpotTradeAsync(string symbol, BitrueSpotTrade trade,
		CancellationToken cancellationToken)
	{
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _tickSubscriptions
				.Where(pair => pair.Value.Section == BitrueSections.Spot &&
					pair.Value.Symbol.EqualsIgnoreCase(symbol))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendSpotTradeAsync(symbol, trade, id, cancellationToken);
	}

	private async ValueTask PublishSpotCandleAsync(string symbol, TimeSpan timeFrame,
		BitrueSpotCandle candle, CancellationToken cancellationToken)
	{
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _candleSubscriptions
				.Where(pair => pair.Value.Section == BitrueSections.Spot &&
					pair.Value.Symbol.EqualsIgnoreCase(symbol) &&
					pair.Value.TimeFrame == timeFrame)
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendSpotCandleAsync(symbol, candle, timeFrame, id, cancellationToken);
	}

	private ValueTask SendSpotTickerAsync(BitrueSpotTicker ticker, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ticker.Symbol.ToStockSharp(BitrueSections.Spot),
			ServerTime = ticker.CloseTime > 0 ? ticker.CloseTime.ToUtcTime() : CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice.ToDecimal())
		.TryAdd(Level1Fields.LastTradeVolume, ticker.LastQuantity.ToDecimal())
		.TryAdd(Level1Fields.BestBidPrice, ticker.BidPrice.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, ticker.AskPrice.ToDecimal())
		.TryAdd(Level1Fields.OpenPrice, ticker.OpenPrice.ToDecimal())
		.TryAdd(Level1Fields.HighPrice, ticker.HighPrice.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, ticker.LowPrice.ToDecimal())
		.TryAdd(Level1Fields.Volume, ticker.Volume.ToDecimal())
		.TryAdd(Level1Fields.Turnover, ticker.QuoteVolume.ToDecimal()), cancellationToken);

	private ValueTask SendFuturesTickerAsync(string symbol, BitrueFuturesTicker ticker,
		long transactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(BitrueSections.Futures),
			ServerTime = ticker.Timestamp > 0 ? ticker.Timestamp.ToUtcTime() : CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, ticker.Last)
		.TryAdd(Level1Fields.BestBidPrice, ticker.Bid)
		.TryAdd(Level1Fields.BestAskPrice, ticker.Ask)
		.TryAdd(Level1Fields.HighPrice, ticker.High)
		.TryAdd(Level1Fields.LowPrice, ticker.Low)
		.TryAdd(Level1Fields.Volume, ticker.Volume), cancellationToken);

	private ValueTask SendFuturesTickerAsync(string symbol, BitrueWsTicker ticker,
		long timestamp, long transactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(BitrueSections.Futures),
			ServerTime = timestamp > 0 ? timestamp.ToUtcTime() : CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, ticker.Close)
		.TryAdd(Level1Fields.OpenPrice, ticker.Open)
		.TryAdd(Level1Fields.HighPrice, ticker.High)
		.TryAdd(Level1Fields.LowPrice, ticker.Low)
		.TryAdd(Level1Fields.Volume, ticker.Volume)
		.TryAdd(Level1Fields.Turnover, ticker.Turnover), cancellationToken);

	private ValueTask SendBookAsync(BitrueSections section, string symbol,
		BitruePriceLevel[] bids, BitruePriceLevel[] asks, DateTime serverTime,
		long transactionId, int depth, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = serverTime,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = ToQuotes(bids, depth),
			Asks = ToQuotes(asks, depth),
		}, cancellationToken);

	private ValueTask SendSpotTradeAsync(string symbol, BitrueSpotTrade trade,
		long transactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = symbol.ToStockSharp(BitrueSections.Spot),
			ServerTime = trade.Timestamp.ToUtcTime(),
			OriginalTransactionId = transactionId,
			TradeId = trade.TradeId,
			TradePrice = trade.Price.ToDecimal(),
			TradeVolume = trade.Quantity.ToDecimal(),
			OriginSide = trade.IsBuyerMaker ? Sides.Sell : Sides.Buy,
		}, cancellationToken);

	private ValueTask SendFuturesTradeAsync(string symbol, BitrueWsTrade trade,
		long transactionId, CancellationToken cancellationToken, long fallbackTimestamp = 0)
	{
		var timestamp = trade.Timestamp > 0 ? trade.Timestamp : fallbackTimestamp;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = symbol.ToStockSharp(BitrueSections.Futures),
			ServerTime = timestamp > 0 ? timestamp.ToUtcTime() : CurrentTime,
			OriginalTransactionId = transactionId,
			TradeId = Interlocked.Increment(ref _publicTradeId),
			TradePrice = trade.Price,
			TradeVolume = trade.Volume,
			OriginSide = trade.Side.ToStockSharp(),
		}, cancellationToken);
	}

	private ValueTask SendSpotCandleAsync(string symbol, BitrueSpotCandle candle,
		TimeSpan timeFrame, long transactionId, CancellationToken cancellationToken)
	{
		var openTime = candle.GetTimestamp().ToUtcTime();
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = symbol.ToStockSharp(BitrueSections.Spot),
			OpenTime = openTime,
			CloseTime = openTime + timeFrame,
			OpenPrice = candle.Open.ToDecimal() ?? 0m,
			HighPrice = candle.High.ToDecimal() ?? 0m,
			LowPrice = candle.Low.ToDecimal() ?? 0m,
			ClosePrice = candle.Close.ToDecimal() ?? 0m,
			TotalVolume = candle.Volume.ToDecimal() ?? 0m,
			TotalPrice = candle.Turnover.ToDecimal() ?? 0m,
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = openTime + timeFrame <= CurrentTime
				? CandleStates.Finished
				: CandleStates.Active,
		}, cancellationToken);
	}

	private ValueTask SendFuturesCandleAsync(string symbol, BitrueFuturesCandle candle,
		TimeSpan timeFrame, long transactionId, CancellationToken cancellationToken)
	{
		var openTime = candle.GetTimestamp().ToUtcTime();
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = symbol.ToStockSharp(BitrueSections.Futures),
			OpenTime = openTime,
			CloseTime = openTime + timeFrame,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.Volume,
			TotalPrice = candle.Turnover,
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = openTime + timeFrame <= CurrentTime
				? CandleStates.Finished
				: CandleStates.Active,
		}, cancellationToken);
	}

	private static QuoteChange[] ToQuotes(BitruePriceLevel[] levels, int depth)
		=> [.. (levels ?? []).Take(depth).Select(static level =>
			new QuoteChange(level.Price, level.Volume))];

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
