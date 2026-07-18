namespace StockSharp.CoinW;

public partial class CoinWMessageAdapter
{
	private async ValueTask RefreshSpotMappingsAsync(CancellationToken cancellationToken)
	{
		var tickers = await RestClient.GetSpotTickersAsync(cancellationToken);
		using (_sync.EnterScope())
		{
			_spotPairIds.Clear();
			_spotSymbols.Clear();
			foreach (var ticker in tickers)
			{
				if (ticker?.Symbol.IsEmpty() != false || ticker.PairId.IsEmpty())
					continue;
				var symbol = ticker.Symbol.ToUpperInvariant();
				_spotPairIds[symbol] = ticker.PairId;
				_spotSymbols[ticker.PairId] = symbol;
			}
		}
	}

	private async ValueTask RefreshFuturesMappingsAsync(CancellationToken cancellationToken)
	{
		var instruments = await RestClient.GetFuturesInstrumentsAsync(cancellationToken);
		using (_sync.EnterScope())
		{
			_futuresNativeSymbols.Clear();
			_futuresSymbols.Clear();
			_futuresContractSizes.Clear();
			foreach (var instrument in instruments)
			{
				if (instrument?.NativeSymbol.IsEmpty() != false)
					continue;
				var symbol = instrument.NativeSymbol.ToCoinWFuturesSecurityCode(instrument.QuoteAsset.IsEmpty("USDT"));
				_futuresNativeSymbols[symbol] = instrument.NativeSymbol;
				_futuresSymbols[instrument.NativeSymbol] = symbol;
				_futuresContractSizes[symbol] = instrument.ContractSize > 0 ? instrument.ContractSize : 1m;
			}
		}
	}

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		if (IsSectionEnabled(CoinWSections.Spot) &&
			(securityTypes.Count == 0 || securityTypes.Contains(SecurityTypes.CryptoCurrency)))
		{
			var symbols = await RestClient.GetSpotSymbolsAsync(cancellationToken);
			var tickers = await RestClient.GetSpotTickersAsync(cancellationToken);
			using (_sync.EnterScope())
			{
				foreach (var ticker in tickers)
				{
					if (ticker?.Symbol.IsEmpty() != false || ticker.PairId.IsEmpty())
						continue;
					var code = ticker.Symbol.ToUpperInvariant();
					_spotPairIds[code] = ticker.PairId;
					_spotSymbols[ticker.PairId] = code;
				}
			}

			foreach (var symbol in symbols)
			{
				if (symbol?.Symbol.IsEmpty() != false || symbol.State == 0)
					continue;
				var code = symbol.Symbol.ToUpperInvariant();
				var security = new SecurityMessage
				{
					SecurityId = code.ToStockSharp(CoinWSections.Spot),
					Name = code,
					SecurityType = SecurityTypes.CryptoCurrency,
					OriginalTransactionId = lookupMsg.TransactionId,
					PriceStep = symbol.PricePrecision.PrecisionToStep(),
					Decimals = symbol.PricePrecision,
					VolumeStep = symbol.VolumePrecision.PrecisionToStep(),
					MinVolume = symbol.MinVolume.ToDecimal(),
				}.TryFillUnderlyingId(symbol.BaseAsset?.ToUpperInvariant());
				if (!security.IsMatch(lookupMsg, securityTypes))
					continue;
				await SendOutMessageAsync(security, cancellationToken);
				if (--left <= 0)
					break;
			}
		}

		if (left > 0 && IsSectionEnabled(CoinWSections.Futures) &&
			(securityTypes.Count == 0 || securityTypes.Contains(SecurityTypes.Future)))
		{
			var instruments = await RestClient.GetFuturesInstrumentsAsync(cancellationToken);
			foreach (var instrument in instruments)
			{
				if (instrument?.NativeSymbol.IsEmpty() != false)
					continue;
				var symbol = instrument.NativeSymbol.ToCoinWFuturesSecurityCode(instrument.QuoteAsset.IsEmpty("USDT"));
				using (_sync.EnterScope())
				{
					_futuresNativeSymbols[symbol] = instrument.NativeSymbol;
					_futuresSymbols[instrument.NativeSymbol] = symbol;
					_futuresContractSizes[symbol] = instrument.ContractSize > 0 ? instrument.ContractSize : 1m;
				}
				var multiplier = instrument.ContractSize > 0 ? instrument.ContractSize : 1m;
				var security = new SecurityMessage
				{
					SecurityId = symbol.ToStockSharp(CoinWSections.Futures),
					Name = instrument.NativeSymbol,
					SecurityType = SecurityTypes.Future,
					OriginalTransactionId = lookupMsg.TransactionId,
					PriceStep = instrument.PricePrecision.PrecisionToStep(),
					Decimals = instrument.PricePrecision,
					VolumeStep = multiplier,
					MinVolume = instrument.MinContracts * multiplier,
					Multiplier = multiplier,
				}.TryFillUnderlyingId(instrument.BaseAsset?.ToUpperInvariant());
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

		var symbol = mdMsg.SecurityId.SecurityCode.ThrowIfEmpty(nameof(mdMsg.SecurityId.SecurityCode)).ToUpperInvariant();
		var section = ResolveSection(mdMsg.SecurityId);
		if (section == CoinWSections.Spot)
		{
			var ticker = (await RestClient.GetSpotTickersAsync(cancellationToken))
				.FirstOrDefault(item => item.Symbol.EqualsIgnoreCase(symbol));
			if (ticker is not null)
				await SendSpotTickerAsync(ticker, mdMsg.TransactionId, cancellationToken);
			await SendSpotBestQuotesAsync(await RestClient.GetSpotOrderBookAsync(symbol, 5, cancellationToken),
				symbol, mdMsg.TransactionId, cancellationToken);
		}
		else
		{
			var native = GetFuturesNativeSymbol(symbol);
			var ticker = (await RestClient.GetFuturesTickersAsync(cancellationToken))
				.FirstOrDefault(item => item.Symbol.EqualsIgnoreCase(native) ||
					item.BaseAsset.EqualsIgnoreCase(native));
			if (ticker is not null)
				await SendFuturesTickerAsync(ticker, symbol, mdMsg.TransactionId, cancellationToken);
			await SendFuturesBestQuotesAsync(await RestClient.GetFuturesOrderBookAsync(native, cancellationToken),
				symbol, mdMsg.TransactionId, cancellationToken);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var streamSymbol = GetStreamSymbol(section, symbol);
		bool subscribe;
		bool subscribeDepth;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new() { Symbol = symbol, Section = section });
			subscribe = AddReference(_tickerReferences, (section, streamSymbol));
			subscribeDepth = AddReference(_depthReferences, (section, streamSymbol));
		}
		if (subscribe)
			await GetMarketClient(section).SubscribeTickerAsync(streamSymbol, cancellationToken);
		if (subscribeDepth)
			await GetMarketClient(section).SubscribeDepthAsync(streamSymbol, 20, cancellationToken);
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

		var symbol = mdMsg.SecurityId.SecurityCode.ThrowIfEmpty(nameof(mdMsg.SecurityId.SecurityCode)).ToUpperInvariant();
		var section = ResolveSection(mdMsg.SecurityId);
		var depth = NormalizeDepth(mdMsg.MaxDepth);
		long sequence = 0;
		if (section == CoinWSections.Spot)
		{
			var book = await RestClient.GetSpotOrderBookAsync(symbol, depth, cancellationToken);
			sequence = (book?.Bids ?? []).Concat(book?.Asks ?? [])
				.Select(static level => level.Sequence).DefaultIfEmpty().Max();
			await SendBookAsync(book?.Bids, book?.Asks, symbol, section, mdMsg.TransactionId,
				QuoteChangeStates.SnapshotComplete, sequence, CurrentTime, depth, cancellationToken);
		}
		else
		{
			var book = await RestClient.GetFuturesOrderBookAsync(GetFuturesNativeSymbol(symbol), cancellationToken);
			await SendBookAsync(book?.Bids, book?.Asks, symbol, section, mdMsg.TransactionId,
				QuoteChangeStates.SnapshotComplete, 0,
				book?.Time > 0 ? book.Time.ToUtcTime() : CurrentTime, depth, cancellationToken);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var streamSymbol = GetStreamSymbol(section, symbol);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				Section = section,
				Depth = depth,
				LastSequence = sequence,
			});
			subscribe = AddReference(_depthReferences, (section, streamSymbol));
		}
		if (subscribe)
			await GetMarketClient(section).SubscribeDepthAsync(streamSymbol, depth, cancellationToken);
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

		var symbol = mdMsg.SecurityId.SecurityCode.ThrowIfEmpty(nameof(mdMsg.SecurityId.SecurityCode)).ToUpperInvariant();
		var section = ResolveSection(mdMsg.SecurityId);
		var from = mdMsg.From;
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var lastTime = from ?? default;
		string lastId = null;
		var limit = (mdMsg.Count ?? 100).Min(1000).Max(1).To<int>();
		if (section == CoinWSections.Spot)
		{
			var trades = await RestClient.GetSpotTradesAsync(symbol, from, to, cancellationToken);
			foreach (var trade in trades.OrderBy(static item => item.Time.ToCoinWUtcTime()).TakeLast(limit))
			{
				var time = trade.Time.ToCoinWUtcTime();
				if (time < (from ?? DateTime.MinValue) || time > to)
					continue;
				await SendTradeAsync(trade.Id, trade.Price, trade.Volume, time, trade.Side.ToStockSharpSide(),
					symbol, section, mdMsg.TransactionId, cancellationToken);
				lastId = trade.Id;
				lastTime = time;
			}
		}
		else
		{
			var trades = await RestClient.GetFuturesTradesAsync(GetFuturesNativeSymbol(symbol), cancellationToken);
			foreach (var trade in trades.OrderBy(static item => item.Time).TakeLast(limit))
			{
				var time = trade.Time.ToUtcTime();
				if (time < (from ?? DateTime.MinValue) || time > to)
					continue;
				await SendTradeAsync(trade.Id, trade.Price, trade.Volume, time, trade.Direction.ToStockSharpSide(),
					symbol, section, mdMsg.TransactionId, cancellationToken);
				lastId = trade.Id;
				lastTime = time;
			}
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var streamSymbol = GetStreamSymbol(section, symbol);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				Section = section,
				LastTradeId = lastId,
				LastTime = lastTime,
			});
			subscribe = AddReference(_tradeReferences, (section, streamSymbol));
		}
		if (subscribe)
			await GetMarketClient(section).SubscribeTradesAsync(streamSymbol, cancellationToken);
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

		var symbol = mdMsg.SecurityId.SecurityCode.ThrowIfEmpty(nameof(mdMsg.SecurityId.SecurityCode)).ToUpperInvariant();
		var section = ResolveSection(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		_ = timeFrame.ToCoinWInterval();
		if (section == CoinWSections.Futures)
			_ = timeFrame.ToCoinWFuturesGranularity();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var count = GetCandleCount(mdMsg, timeFrame, to);
		var lastOpen = mdMsg.From ?? default;
		if (section == CoinWSections.Spot)
		{
			var candles = await RestClient.GetSpotCandlesAsync(symbol, timeFrame, mdMsg.From, to, cancellationToken);
			foreach (var candle in candles.OrderBy(static item => item.OpenTime))
			{
				await SendCandleAsync(candle.OpenTime.ToUtcTime(), candle.OpenPrice, candle.HighPrice,
					candle.LowPrice, candle.ClosePrice, candle.Volume, symbol, section, timeFrame,
					mdMsg.TransactionId, cancellationToken);
				lastOpen = candle.OpenTime.ToUtcTime();
			}
		}
		else
		{
			var candles = await RestClient.GetFuturesCandlesAsync(GetFuturesNativeSymbol(symbol), timeFrame,
				mdMsg.From, to, count, cancellationToken);
			foreach (var candle in candles.OrderBy(static item => item.OpenTime))
			{
				await SendCandleAsync(candle.OpenTime.ToUtcTime(), candle.OpenPrice, candle.HighPrice,
					candle.LowPrice, candle.ClosePrice, candle.Volume, symbol, section, timeFrame,
					mdMsg.TransactionId, cancellationToken);
				lastOpen = candle.OpenTime.ToUtcTime();
			}
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var streamSymbol = GetStreamSymbol(section, symbol);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				Section = section,
				TimeFrame = timeFrame,
				LastOpenTime = lastOpen,
			});
			subscribe = AddReference(_candleReferences, (section, streamSymbol, timeFrame));
		}
		if (subscribe)
			await GetMarketClient(section).SubscribeCandlesAsync(streamSymbol, timeFrame, cancellationToken);
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId, CancellationToken cancellationToken)
	{
		StreamSubscription subscription = null;
		bool unsubscribe = false;
		bool unsubscribeDepth = false;
		string streamSymbol = null;
		using (_sync.EnterScope())
		{
			if (_level1Subscriptions.Remove(transactionId, out subscription))
			{
				streamSymbol = subscription.Section == CoinWSections.Spot
					? _spotPairIds.GetValueOrDefault(subscription.Symbol)
					: _futuresNativeSymbols.GetValueOrDefault(subscription.Symbol).IsEmpty(subscription.Symbol);
				unsubscribe = ReleaseReference(_tickerReferences, (subscription.Section, streamSymbol));
				unsubscribeDepth = ReleaseReference(_depthReferences, (subscription.Section, streamSymbol));
			}
		}
		if (unsubscribe)
			await GetMarketClient(subscription.Section).UnsubscribeTickerAsync(streamSymbol, cancellationToken);
		if (unsubscribeDepth)
			await GetMarketClient(subscription.Section).UnsubscribeDepthAsync(streamSymbol, 20, cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId, CancellationToken cancellationToken)
	{
		DepthSubscription subscription = null;
		bool unsubscribe = false;
		string streamSymbol = null;
		using (_sync.EnterScope())
		{
			if (_depthSubscriptions.Remove(transactionId, out subscription))
			{
				streamSymbol = subscription.Section == CoinWSections.Spot
					? _spotPairIds.GetValueOrDefault(subscription.Symbol)
					: _futuresNativeSymbols.GetValueOrDefault(subscription.Symbol).IsEmpty(subscription.Symbol);
				unsubscribe = ReleaseReference(_depthReferences, (subscription.Section, streamSymbol));
			}
		}
		if (unsubscribe)
			await GetMarketClient(subscription.Section).UnsubscribeDepthAsync(streamSymbol,
				subscription.Depth, cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId, CancellationToken cancellationToken)
	{
		TickSubscription subscription = null;
		bool unsubscribe = false;
		string streamSymbol = null;
		using (_sync.EnterScope())
		{
			if (_tickSubscriptions.Remove(transactionId, out subscription))
			{
				streamSymbol = subscription.Section == CoinWSections.Spot
					? _spotPairIds.GetValueOrDefault(subscription.Symbol)
					: _futuresNativeSymbols.GetValueOrDefault(subscription.Symbol).IsEmpty(subscription.Symbol);
				unsubscribe = ReleaseReference(_tradeReferences, (subscription.Section, streamSymbol));
			}
		}
		if (unsubscribe)
			await GetMarketClient(subscription.Section).UnsubscribeTradesAsync(streamSymbol, cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId, CancellationToken cancellationToken)
	{
		CandleSubscription subscription = null;
		bool unsubscribe = false;
		string streamSymbol = null;
		using (_sync.EnterScope())
		{
			if (_candleSubscriptions.Remove(transactionId, out subscription))
			{
				streamSymbol = subscription.Section == CoinWSections.Spot
					? _spotPairIds.GetValueOrDefault(subscription.Symbol)
					: _futuresNativeSymbols.GetValueOrDefault(subscription.Symbol).IsEmpty(subscription.Symbol);
				unsubscribe = ReleaseReference(_candleReferences,
					(subscription.Section, streamSymbol, subscription.TimeFrame));
			}
		}
		if (unsubscribe)
			await GetMarketClient(subscription.Section).UnsubscribeCandlesAsync(streamSymbol,
				subscription.TimeFrame, cancellationToken);
	}

	private async ValueTask OnTickerAsync(CoinWSections section, CoinWWsTickerUpdate update,
		CancellationToken cancellationToken)
	{
		var symbol = ResolveStreamSymbol(section, update?.PairCode);
		if (symbol.IsEmpty())
			return;
		long[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions
				.Where(pair => pair.Value.Section == section && pair.Value.Symbol.EqualsIgnoreCase(symbol))
				.Select(static pair => pair.Key)];
		foreach (var id in subscriptions)
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = symbol.ToStockSharp(section),
				ServerTime = CurrentTime,
				OriginalTransactionId = id,
			}
			.TryAdd(Level1Fields.LastTradePrice, update.LastPrice.ToDecimal())
			.TryAdd(Level1Fields.BestBidPrice, update.BidPrice.ToDecimal())
			.TryAdd(Level1Fields.BestAskPrice, update.AskPrice.ToDecimal())
			.TryAdd(Level1Fields.OpenPrice, update.OpenPrice.ToDecimal())
			.TryAdd(Level1Fields.HighPrice, update.HighPrice.ToDecimal())
			.TryAdd(Level1Fields.LowPrice, update.LowPrice.ToDecimal())
			.TryAdd(Level1Fields.Volume, update.Volume.ToDecimal()), cancellationToken);
		}
	}

	private async ValueTask OnDepthAsync(CoinWSections section, CoinWWsDepthUpdate update,
		CancellationToken cancellationToken)
	{
		var symbol = ResolveStreamSymbol(section, update?.PairCode);
		if (symbol.IsEmpty())
			return;
		(long Id, DepthSubscription State)[] depthSubscriptions;
		long[] level1Subscriptions;
		bool isGap;
		using (_sync.EnterScope())
		{
			depthSubscriptions = [.. _depthSubscriptions
				.Where(pair => pair.Value.Section == section && pair.Value.Symbol.EqualsIgnoreCase(symbol))
				.Select(static pair => (pair.Key, pair.Value))];
			level1Subscriptions = [.. _level1Subscriptions
				.Where(pair => pair.Value.Section == section && pair.Value.Symbol.EqualsIgnoreCase(symbol))
				.Select(static pair => pair.Key)];
			isGap = section == CoinWSections.Spot && !update.IsSnapshot &&
				depthSubscriptions.Any(item => item.State.LastSequence > 0 && update.FirstSequence > item.State.LastSequence + 1);
			if (!isGap)
			{
				foreach (var subscription in depthSubscriptions)
					subscription.State.LastSequence = update.LastSequence;
			}
		}
		if (isGap)
		{
			await GetMarketClient(section).ResubscribeDepthAsync(update.PairCode,
				depthSubscriptions.FirstOrDefault().State?.Depth ?? 20, cancellationToken);
			return;
		}

		var serverTime = update.Time > 0 ? update.Time.ToUtcTime() : CurrentTime;
		foreach (var subscription in depthSubscriptions)
			await SendBookAsync(update.Bids, update.Asks, symbol, section, subscription.Id,
				update.IsSnapshot ? QuoteChangeStates.SnapshotComplete : QuoteChangeStates.Increment,
				update.LastSequence, serverTime, subscription.State.Depth, cancellationToken);
		foreach (var id in level1Subscriptions)
			await SendBestQuotesAsync(update.Bids, update.Asks, symbol, section, id, serverTime, cancellationToken);
	}

	private async ValueTask OnTradesAsync(CoinWSections section, CoinWWsTradeUpdate[] updates,
		CancellationToken cancellationToken)
	{
		foreach (var update in (updates ?? []).OrderBy(static item => item.Time))
		{
			var symbol = ResolveStreamSymbol(section, update.PairCode);
			if (symbol.IsEmpty())
				continue;
			var time = update.Time > 0 ? update.Time.ToUtcTime() : CurrentTime;
			long[] subscriptions;
			using (_sync.EnterScope())
			{
				var accepted = new List<long>();
				foreach (var pair in _tickSubscriptions)
				{
					var state = pair.Value;
					if (state.Section != section || !state.Symbol.EqualsIgnoreCase(symbol) ||
						(!state.LastTradeId.IsEmpty() && state.LastTradeId.EqualsIgnoreCase(update.Id)) ||
						(state.LastTime != default && time < state.LastTime))
						continue;
					state.LastTradeId = update.Id;
					state.LastTime = time;
					accepted.Add(pair.Key);
				}
				subscriptions = [.. accepted];
			}
			foreach (var id in subscriptions)
				await SendTradeAsync(update.Id, update.Price, update.Volume, time,
					update.Side.ToStockSharpSide(), symbol, section, id, cancellationToken);
		}
	}

	private async ValueTask OnCandleAsync(CoinWSections section, CoinWWsCandleUpdate update,
		CancellationToken cancellationToken)
	{
		var symbol = ResolveStreamSymbol(section, update?.PairCode);
		if (symbol.IsEmpty())
			return;
		var timeFrame = update.Interval.FromCoinWWebSocketInterval(section);
		if (timeFrame is null)
			return;
		var openTime = update.OpenTime.ToUtcTime();
		long[] subscriptions;
		using (_sync.EnterScope())
		{
			subscriptions = [.. _candleSubscriptions
				.Where(pair => pair.Value.Section == section && pair.Value.TimeFrame == timeFrame.Value &&
					pair.Value.Symbol.EqualsIgnoreCase(symbol) &&
					(pair.Value.LastOpenTime == default || openTime >= pair.Value.LastOpenTime))
				.Select(static pair => pair.Key)];
			foreach (var id in subscriptions)
				_candleSubscriptions[id].LastOpenTime = openTime;
		}
		foreach (var id in subscriptions)
			await SendCandleAsync(openTime, update.OpenPrice, update.HighPrice, update.LowPrice,
				update.ClosePrice, update.Volume, symbol, section, timeFrame.Value, id, cancellationToken);
	}

	private ValueTask SendSpotTickerAsync(CoinWSpotTicker ticker, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ticker.Symbol.ToStockSharp(CoinWSections.Spot),
			ServerTime = CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice.ToDecimal())
		.TryAdd(Level1Fields.BestBidPrice, ticker.BidPrice.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, ticker.AskPrice.ToDecimal())
		.TryAdd(Level1Fields.HighPrice, ticker.HighPrice.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, ticker.LowPrice.ToDecimal())
		.TryAdd(Level1Fields.Volume, ticker.QuoteVolume.ToDecimal()), cancellationToken);

	private ValueTask SendFuturesTickerAsync(CoinWFuturesTicker ticker, string symbol, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(CoinWSections.Futures),
			ServerTime = ticker.Time > 0 ? ticker.Time.ToUtcTime() : CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice.ToDecimal())
		.TryAdd(Level1Fields.HighPrice, ticker.HighPrice.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, ticker.LowPrice.ToDecimal())
		.TryAdd(Level1Fields.Volume, ticker.Volume.ToDecimal())
		.TryAdd(Level1Fields.UnderlyingPrice, ticker.IndexPrice.ToDecimal()), cancellationToken);

	private ValueTask SendSpotBestQuotesAsync(CoinWSpotOrderBook book, string symbol, long transactionId,
		CancellationToken cancellationToken)
		=> SendBestQuotesAsync(book?.Bids, book?.Asks, symbol, CoinWSections.Spot,
			transactionId, CurrentTime, cancellationToken);

	private ValueTask SendFuturesBestQuotesAsync(CoinWFuturesOrderBook book, string symbol, long transactionId,
		CancellationToken cancellationToken)
		=> SendBestQuotesAsync(book?.Bids, book?.Asks, symbol, CoinWSections.Futures,
			transactionId, book?.Time > 0 ? book.Time.ToUtcTime() : CurrentTime, cancellationToken);

	private ValueTask SendBestQuotesAsync(CoinWBookLevel[] bids, CoinWBookLevel[] asks, string symbol,
		CoinWSections section, long transactionId, DateTime serverTime, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = serverTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, bids?.FirstOrDefault()?.Price.ToDecimal())
		.TryAdd(Level1Fields.BestBidVolume, bids?.FirstOrDefault()?.Volume.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, asks?.FirstOrDefault()?.Price.ToDecimal())
		.TryAdd(Level1Fields.BestAskVolume, asks?.FirstOrDefault()?.Volume.ToDecimal()), cancellationToken);

	private ValueTask SendBookAsync(CoinWBookLevel[] bids, CoinWBookLevel[] asks, string symbol,
		CoinWSections section, long transactionId, QuoteChangeStates state, long sequence,
		DateTime serverTime, int depth, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = serverTime,
			OriginalTransactionId = transactionId,
			State = state,
			Bids = ToQuotes(bids, depth),
			Asks = ToQuotes(asks, depth),
			SeqNum = sequence,
		}, cancellationToken);

	private ValueTask SendTradeAsync(string id, string price, string volume, DateTime time, Sides side,
		string symbol, CoinWSections section, long transactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = time,
			OriginalTransactionId = transactionId,
			TradeStringId = id,
			TradePrice = price.ToDecimal(),
			TradeVolume = volume.ToDecimal(),
			OriginSide = side,
		}, cancellationToken);

	private ValueTask SendCandleAsync(DateTime openTime, string open, string high, string low,
		string close, string volume, string symbol, CoinWSections section, TimeSpan timeFrame,
		long transactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = symbol.ToStockSharp(section),
			OpenTime = openTime,
			CloseTime = openTime + timeFrame,
			OpenPrice = open.ToDecimal() ?? 0m,
			HighPrice = high.ToDecimal() ?? 0m,
			LowPrice = low.ToDecimal() ?? 0m,
			ClosePrice = close.ToDecimal() ?? 0m,
			TotalVolume = volume.ToDecimal() ?? 0m,
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = DateTime.UtcNow >= openTime + timeFrame ? CandleStates.Finished : CandleStates.Active,
		}, cancellationToken);

	private static QuoteChange[] ToQuotes(CoinWBookLevel[] levels, int depth)
		=> [.. (levels ?? []).Take(depth).Select(static level => new QuoteChange(
			level.Price.ToDecimal() ?? 0m, level.Volume.ToDecimal() ?? 0m))];

	private static int GetCandleCount(MarketDataMessage message, TimeSpan timeFrame, DateTime to)
	{
		if (message.Count is long count)
			return count.Min(1500).Max(1).To<int>();
		if (message.From is DateTime from && to > from)
			return ((to - from).Ticks / timeFrame.Ticks + 1).Min(1500L).Max(1L).To<int>();
		return 500;
	}
}
