namespace StockSharp.Weex;

public partial class WeexMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsureConnected();

		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		if (IsSectionEnabled(WeexSections.Spot) &&
			(securityTypes.Count == 0 || securityTypes.Contains(SecurityTypes.CryptoCurrency)))
		{
			var response = await RestClient.GetSpotExchangeInfoAsync(cancellationToken);
			foreach (var symbol in response?.Symbols ?? [])
			{
				if (symbol?.Symbol.IsEmpty() != false || !symbol.IsTradeEnabled || !symbol.IsDisplayEnabled)
					continue;

				var security = new SecurityMessage
				{
					SecurityId = symbol.Symbol.ToStockSharp(WeexSections.Spot),
					Name = symbol.Symbol,
					SecurityType = SecurityTypes.CryptoCurrency,
					OriginalTransactionId = lookupMsg.TransactionId,
					PriceStep = symbol.TickSize.ToDecimal(),
					VolumeStep = symbol.StepSize.ToDecimal(),
					MinVolume = symbol.MinTradeAmount.ToDecimal(),
				}.TryFillUnderlyingId(symbol.BaseAsset?.ToUpperInvariant());

				if (!security.IsMatch(lookupMsg, securityTypes))
					continue;
				await SendOutMessageAsync(security, cancellationToken);
				if (--left <= 0)
					break;
			}
		}

		if (left > 0 && IsSectionEnabled(WeexSections.Futures) &&
			(securityTypes.Count == 0 || securityTypes.Contains(SecurityTypes.Future)))
		{
			var response = await RestClient.GetFuturesExchangeInfoAsync(cancellationToken);
			foreach (var symbol in response?.Symbols ?? [])
			{
				if (symbol?.Symbol.IsEmpty() != false)
					continue;

				var security = new SecurityMessage
				{
					SecurityId = symbol.Symbol.ToStockSharp(WeexSections.Futures),
					Name = symbol.DisplaySymbol.IsEmpty(symbol.Symbol),
					SecurityType = SecurityTypes.Future,
					OriginalTransactionId = lookupMsg.TransactionId,
					PriceStep = symbol.PricePrecision.PrecisionToStep(),
					Decimals = symbol.PricePrecision,
					VolumeStep = symbol.QuantityPrecision.PrecisionToStep(),
					MinVolume = symbol.MinOrderSize,
					Multiplier = symbol.ContractValue,
				}.TryFillUnderlyingId(symbol.BaseAsset?.ToUpperInvariant());

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
		var ticker = await RestClient.GetTickerAsync(section, symbol, cancellationToken);
		if (ticker is not null)
			await SendTickerAsync(ticker, section, mdMsg.TransactionId, cancellationToken);

		var book = await RestClient.GetOrderBookAsync(section, symbol, 15, cancellationToken);
		await SendBestQuotesAsync(book, symbol, section, mdMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var key = (section, symbol);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new() { Symbol = symbol, Section = section });
			subscribe = AddReference(_tickerReferences, key);
		}
		if (subscribe)
			await GetMarketClient(section).SubscribeTickerAsync(symbol, cancellationToken);
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
		var book = await RestClient.GetOrderBookAsync(section, symbol, depth, cancellationToken);
		await SendBookAsync(book, symbol, section, QuoteChangeStates.SnapshotComplete,
			mdMsg.TransactionId, depth, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var key = (section, symbol, depth);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				Section = section,
				Depth = depth,
				LastSequence = book?.LastUpdateId ?? 0,
			});
			subscribe = AddReference(_depthReferences, key);
		}
		if (subscribe)
			await GetMarketClient(section).SubscribeDepthAsync(symbol, depth, cancellationToken);
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
		var to = mdMsg.To ?? DateTime.UtcNow;
		var limit = (mdMsg.Count ?? 100).Min(1000).Max(1).To<int>();
		var trades = (await RestClient.GetTradesAsync(section, symbol, limit, cancellationToken) ?? [])
			.Where(trade => (from is null || trade.Time.ToUtcTime() >= from) && trade.Time.ToUtcTime() <= to)
			.OrderBy(static trade => trade.Time)
			.ToArray();

		string lastTradeId = null;
		var lastTime = from ?? default;
		foreach (var trade in trades)
		{
			await SendTradeAsync(trade.Id, trade.Price, trade.Volume, trade.Time,
				trade.IsBuyerMaker ? Sides.Sell : Sides.Buy, symbol, section,
				mdMsg.TransactionId, cancellationToken);
			lastTradeId = trade.Id;
			lastTime = trade.Time.ToUtcTime();
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var key = (section, symbol);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				Section = section,
				LastTradeId = lastTradeId,
				LastTime = lastTime,
			});
			subscribe = AddReference(_tradeReferences, key);
		}
		if (subscribe)
			await GetMarketClient(section).SubscribeTradesAsync(symbol, cancellationToken);
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
		_ = timeFrame.ToNative();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var count = GetCandleCount(mdMsg, timeFrame, to);
		var candles = await RestClient.GetCandlesAsync(section, symbol, timeFrame,
			mdMsg.From, to, count, cancellationToken);

		var lastOpenTime = mdMsg.From ?? default;
		foreach (var candle in candles ?? [])
		{
			await SendCandleAsync(candle, symbol, section, timeFrame,
				mdMsg.TransactionId, cancellationToken);
			lastOpenTime = candle.OpenTime.ToUtcTime();
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var key = (section, symbol, timeFrame);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				Section = section,
				TimeFrame = timeFrame,
				LastOpenTime = lastOpenTime,
			});
			subscribe = AddReference(_candleReferences, key);
		}
		if (subscribe)
			await GetMarketClient(section).SubscribeCandlesAsync(symbol, timeFrame, cancellationToken);
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId, CancellationToken cancellationToken)
	{
		StreamSubscription subscription = null;
		bool unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_level1Subscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_tickerReferences, (subscription.Section, subscription.Symbol));
		}
		if (unsubscribe)
			await GetMarketClient(subscription.Section).UnsubscribeTickerAsync(subscription.Symbol, cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId, CancellationToken cancellationToken)
	{
		DepthSubscription subscription = null;
		bool unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_depthSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_depthReferences,
					(subscription.Section, subscription.Symbol, subscription.Depth));
		}
		if (unsubscribe)
			await GetMarketClient(subscription.Section).UnsubscribeDepthAsync(
				subscription.Symbol, subscription.Depth, cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId, CancellationToken cancellationToken)
	{
		TickSubscription subscription = null;
		bool unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_tickSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_tradeReferences, (subscription.Section, subscription.Symbol));
		}
		if (unsubscribe)
			await GetMarketClient(subscription.Section).UnsubscribeTradesAsync(subscription.Symbol, cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId, CancellationToken cancellationToken)
	{
		CandleSubscription subscription = null;
		bool unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_candleSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_candleReferences,
					(subscription.Section, subscription.Symbol, subscription.TimeFrame));
		}
		if (unsubscribe)
			await GetMarketClient(subscription.Section).UnsubscribeCandlesAsync(
				subscription.Symbol, subscription.TimeFrame, cancellationToken);
	}

	private async ValueTask OnSpotTickerAsync(WeexWsEnvelope<WeexWsSpotTicker> update,
		CancellationToken cancellationToken)
	{
		if (update?.Symbol.IsEmpty() != false || update.Data is null)
			return;
		var ticker = new WeexTicker
		{
			Symbol = update.Symbol,
			PriceChange = update.Data.PriceChange,
			PriceChangePercent = update.Data.PriceChangePercent,
			LastPrice = update.Data.LastPrice,
			BidPrice = update.Data.BidPrice,
			BidVolume = update.Data.BidVolume,
			AskPrice = update.Data.AskPrice,
			AskVolume = update.Data.AskVolume,
			OpenPrice = update.Data.OpenPrice,
			HighPrice = update.Data.HighPrice,
			LowPrice = update.Data.LowPrice,
			Volume = update.Data.Volume,
			QuoteVolume = update.Data.QuoteVolume,
		};
		await BroadcastTickerAsync(ticker, WeexSections.Spot, update.EventTime, cancellationToken);
	}

	private async ValueTask OnFuturesTickerAsync(WeexWsEnvelope<WeexWsFuturesTicker[]> update,
		CancellationToken cancellationToken)
	{
		foreach (var item in update?.Data ?? [])
		{
			var symbol = item.Symbol.IsEmpty(update.Symbol);
			if (symbol.IsEmpty())
				continue;
			await BroadcastTickerAsync(new WeexTicker
			{
				Symbol = symbol,
				PriceChange = item.PriceChange,
				PriceChangePercent = item.PriceChangePercent,
				LastPrice = item.LastPrice,
				OpenPrice = item.OpenPrice,
				HighPrice = item.HighPrice,
				LowPrice = item.LowPrice,
				Volume = item.Volume,
				QuoteVolume = item.QuoteVolume,
				MarkPrice = item.MarkPrice,
				IndexPrice = item.IndexPrice,
			}, WeexSections.Futures, update.EventTime, cancellationToken);
		}
	}

	private async ValueTask BroadcastTickerAsync(WeexTicker ticker, WeexSections section,
		long eventTime, CancellationToken cancellationToken)
	{
		(long Id, StreamSubscription State)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions
				.Where(pair => pair.Value.Section == section &&
					pair.Value.Symbol.EqualsIgnoreCase(ticker.Symbol))
				.Select(static pair => (pair.Key, pair.Value))];
		foreach (var subscription in subscriptions)
			await SendTickerAsync(ticker, section, subscription.Id, cancellationToken,
				eventTime > 0 ? eventTime.ToUtcTime() : null);
	}

	private async ValueTask OnDepthAsync(WeexSections section, WeexWsDepth update,
		CancellationToken cancellationToken)
	{
		if (update?.Symbol.IsEmpty() != false)
			return;

		(long Id, DepthSubscription State)[] subscriptions;
		bool isGap;
		var isSnapshot = update.DepthType.EqualsIgnoreCase("SNAPSHOT");
		using (_sync.EnterScope())
		{
			subscriptions = [.. _depthSubscriptions
				.Where(pair => pair.Value.Section == section &&
					pair.Value.Symbol.EqualsIgnoreCase(update.Symbol) &&
					(update.Level <= 0 || pair.Value.Depth == update.Level))
				.Select(static pair => (pair.Key, pair.Value))];
			isGap = !isSnapshot && subscriptions.Any(item => item.State.LastSequence > 0 &&
				update.FirstUpdateId > item.State.LastSequence + 1);
			if (!isGap)
			{
				foreach (var subscription in subscriptions)
					subscription.State.LastSequence = update.LastUpdateId;
			}
		}

		if (isGap)
		{
			foreach (var depth in subscriptions.Select(static item => item.State.Depth).Distinct())
				await GetMarketClient(section).ResubscribeDepthAsync(update.Symbol, depth, cancellationToken);
			return;
		}

		foreach (var subscription in subscriptions)
		{
			await SendOutMessageAsync(new QuoteChangeMessage
			{
				SecurityId = update.Symbol.ToStockSharp(section),
				ServerTime = update.EventTime > 0 ? update.EventTime.ToUtcTime() : CurrentTime,
				OriginalTransactionId = subscription.Id,
				State = isSnapshot ? QuoteChangeStates.SnapshotComplete : QuoteChangeStates.Increment,
				Bids = ToQuotes(update.Bids, subscription.State.Depth),
				Asks = ToQuotes(update.Asks, subscription.State.Depth),
				SeqNum = update.LastUpdateId,
			}, cancellationToken);
		}
	}

	private async ValueTask OnTradesAsync(WeexSections section,
		WeexWsEnvelope<WeexWsTrade[]> update, CancellationToken cancellationToken)
	{
		if (update?.Symbol.IsEmpty() != false)
			return;
		foreach (var trade in (update.Data ?? []).OrderBy(static item => item.Time))
		{
			(long Id, TickSubscription State)[] subscriptions;
			using (_sync.EnterScope())
			{
				var accepted = new List<(long, TickSubscription)>();
				foreach (var pair in _tickSubscriptions)
				{
					var state = pair.Value;
					if (state.Section != section || !state.Symbol.EqualsIgnoreCase(update.Symbol) ||
						(!state.LastTradeId.IsEmpty() && state.LastTradeId.EqualsIgnoreCase(trade.Id)) ||
						(state.LastTradeId.IsEmpty() && state.LastTime != default &&
							trade.Time.ToUtcTime() <= state.LastTime))
						continue;
					state.LastTradeId = trade.Id;
					state.LastTime = trade.Time.ToUtcTime();
					accepted.Add((pair.Key, state));
				}
				subscriptions = [.. accepted];
			}
			foreach (var subscription in subscriptions)
				await SendTradeAsync(trade.Id, trade.Price, trade.Volume, trade.Time,
					trade.IsBuyerMaker ? Sides.Sell : Sides.Buy, update.Symbol, section,
					subscription.Id, cancellationToken);
		}
	}

	private async ValueTask OnCandleAsync(WeexSections section,
		WeexWsEnvelope<WeexWsCandle[]> update, CancellationToken cancellationToken)
	{
		foreach (var candle in update?.Data ?? [])
		{
			var symbol = candle.Symbol.IsEmpty(update.Symbol);
			if (symbol.IsEmpty() || !WeexExtensions.TimeFrames.Values.Contains(candle.Interval))
				continue;
			var timeFrame = WeexExtensions.TimeFrames.First(pair => pair.Value == candle.Interval).Key;
			var openTime = candle.OpenTime.ToUtcTime();
			(long Id, CandleSubscription State)[] subscriptions;
			using (_sync.EnterScope())
			{
				subscriptions = [.. _candleSubscriptions
					.Where(pair => pair.Value.Section == section && pair.Value.TimeFrame == timeFrame &&
						pair.Value.Symbol.EqualsIgnoreCase(symbol) &&
						(pair.Value.LastOpenTime == default || openTime >= pair.Value.LastOpenTime))
					.Select(static pair => (pair.Key, pair.Value))];
				foreach (var subscription in subscriptions)
					subscription.State.LastOpenTime = openTime;
			}

			foreach (var subscription in subscriptions)
				await SendCandleAsync(candle, symbol, section, timeFrame,
					subscription.Id, cancellationToken);
		}
	}

	private ValueTask SendTickerAsync(WeexTicker ticker, WeexSections section, long transactionId,
		CancellationToken cancellationToken, DateTime? serverTime = null)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ticker.Symbol.ToStockSharp(section),
			ServerTime = serverTime ?? (ticker.CloseTime > 0 ? ticker.CloseTime.ToUtcTime() : CurrentTime),
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice.ToDecimal())
		.TryAdd(Level1Fields.BestBidPrice, ticker.BidPrice.ToDecimal())
		.TryAdd(Level1Fields.BestBidVolume, ticker.BidVolume.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, ticker.AskPrice.ToDecimal())
		.TryAdd(Level1Fields.BestAskVolume, ticker.AskVolume.ToDecimal())
		.TryAdd(Level1Fields.OpenPrice, ticker.OpenPrice.ToDecimal())
		.TryAdd(Level1Fields.HighPrice, ticker.HighPrice.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, ticker.LowPrice.ToDecimal())
		.TryAdd(Level1Fields.Volume, ticker.Volume.ToDecimal())
		.TryAdd(Level1Fields.Change, ticker.PriceChange.ToDecimal())
		.TryAdd(Level1Fields.TheorPrice, ticker.MarkPrice.ToDecimal())
		.TryAdd(Level1Fields.UnderlyingPrice, ticker.IndexPrice.ToDecimal()), cancellationToken);

	private ValueTask SendBestQuotesAsync(WeexOrderBook book, string symbol, WeexSections section,
		long transactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, book?.Bids?.FirstOrDefault()?.Price.ToDecimal())
		.TryAdd(Level1Fields.BestBidVolume, book?.Bids?.FirstOrDefault()?.Volume.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, book?.Asks?.FirstOrDefault()?.Price.ToDecimal())
		.TryAdd(Level1Fields.BestAskVolume, book?.Asks?.FirstOrDefault()?.Volume.ToDecimal()), cancellationToken);

	private ValueTask SendBookAsync(WeexOrderBook book, string symbol, WeexSections section,
		QuoteChangeStates state, long transactionId, int depth, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = CurrentTime,
			OriginalTransactionId = transactionId,
			State = state,
			Bids = ToQuotes(book?.Bids, depth),
			Asks = ToQuotes(book?.Asks, depth),
			SeqNum = book?.LastUpdateId ?? 0,
		}, cancellationToken);

	private ValueTask SendTradeAsync(string id, string price, string volume, long time, Sides side,
		string symbol, WeexSections section, long transactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = time > 0 ? time.ToUtcTime() : CurrentTime,
			TradeStringId = id,
			TradePrice = price.ToDecimal(),
			TradeVolume = volume.ToDecimal(),
			OriginSide = side,
			OriginalTransactionId = transactionId,
		}, cancellationToken);

	private ValueTask SendCandleAsync(WeexKline candle, string symbol, WeexSections section,
		TimeSpan timeFrame, long transactionId, CancellationToken cancellationToken)
		=> SendCandleAsync(candle.OpenTime, candle.CloseTime, candle.OpenPrice, candle.HighPrice,
			candle.LowPrice, candle.ClosePrice, candle.Volume, symbol, section, timeFrame,
			transactionId, cancellationToken);

	private ValueTask SendCandleAsync(WeexWsCandle candle, string symbol, WeexSections section,
		TimeSpan timeFrame, long transactionId, CancellationToken cancellationToken)
		=> SendCandleAsync(candle.OpenTime, candle.CloseTime, candle.OpenPrice, candle.HighPrice,
			candle.LowPrice, candle.ClosePrice, candle.Volume, symbol, section, timeFrame,
			transactionId, cancellationToken);

	private ValueTask SendCandleAsync(long openMilliseconds, long closeMilliseconds, string open,
		string high, string low, string close, string volume, string symbol, WeexSections section,
		TimeSpan timeFrame, long transactionId, CancellationToken cancellationToken)
	{
		var openTime = openMilliseconds.ToUtcTime();
		var closeTime = closeMilliseconds > 0 ? closeMilliseconds.ToUtcTime() : openTime + timeFrame;
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = symbol.ToStockSharp(section),
			TypedArg = timeFrame,
			OpenTime = openTime,
			CloseTime = closeTime,
			OpenPrice = open.ToDecimal() ?? 0m,
			HighPrice = high.ToDecimal() ?? 0m,
			LowPrice = low.ToDecimal() ?? 0m,
			ClosePrice = close.ToDecimal() ?? 0m,
			TotalVolume = volume.ToDecimal() ?? 0m,
			State = closeTime <= DateTime.UtcNow ? CandleStates.Finished : CandleStates.Active,
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}

	private static QuoteChange[] ToQuotes(WeexBookLevel[] levels, int depth)
		=> [.. (levels ?? [])
			.Take(depth)
			.Select(static level => (Price: level.Price.ToDecimal(), Volume: level.Volume.ToDecimal()))
			.Where(static item => item.Price is not null && item.Volume is not null)
			.Select(static item => new QuoteChange(item.Price.Value, item.Volume.Value))];

	private static int GetCandleCount(MarketDataMessage message, TimeSpan timeFrame, DateTime to)
	{
		if (message.Count is long count)
			return count.Min(1000).Max(1).To<int>();
		if (message.From is DateTime from)
			return ((to.Ticks - from.ToUniversalTime().Ticks) / timeFrame.Ticks + 1).Min(1000).Max(1).To<int>();
		return 500;
	}
}
