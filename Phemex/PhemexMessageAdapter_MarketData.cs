namespace StockSharp.Phemex;

public partial class PhemexMessageAdapter
{
	private async ValueTask RefreshSymbolMappingsAsync(CancellationToken cancellationToken)
	{
		var mappings = new List<(string Symbol, PhemexSections Section)>();
		if (IsSectionEnabled(PhemexSections.Spot))
			mappings.AddRange((await RestClient.GetSpotSymbolsAsync(cancellationToken))
				.Where(static item => item?.Symbol.IsEmpty() == false)
				.Select(static item => (item.Symbol.ToUpperInvariant(), PhemexSections.Spot)));
		if (IsSectionEnabled(PhemexSections.Futures))
			mappings.AddRange((await RestClient.GetFuturesSymbolsAsync(cancellationToken))
				.Where(static item => item?.Symbol.IsEmpty() == false)
				.Select(static item => (item.Symbol.ToUpperInvariant(), PhemexSections.Futures)));
		using (_sync.EnterScope())
		{
			_symbolSections.Clear();
			foreach (var mapping in mappings)
				_symbolSections[new(mapping.Symbol)] = mapping.Section;
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

		if (IsSectionEnabled(PhemexSections.Spot) &&
			(securityTypes.Count == 0 || securityTypes.Contains(SecurityTypes.CryptoCurrency)))
		{
			var symbols = await RestClient.GetSpotSymbolsAsync(cancellationToken);
			foreach (var symbol in symbols)
			{
				if (symbol?.Symbol.IsEmpty() != false || !symbol.IsEnabled)
					continue;
				var code = symbol.Symbol.ToUpperInvariant();
				using (_sync.EnterScope())
					_symbolSections[new(code)] = PhemexSections.Spot;
				var security = new SecurityMessage
				{
					SecurityId = code.ToStockSharp(PhemexSections.Spot),
					Name = symbol.Name.IsEmpty(code),
					SecurityType = SecurityTypes.CryptoCurrency,
					OriginalTransactionId = lookupMsg.TransactionId,
					PriceStep = symbol.QuotePrecision.PrecisionToStep(),
					Decimals = symbol.QuotePrecision,
					VolumeStep = symbol.BasePrecision.PrecisionToStep(),
					MinVolume = symbol.MinTradeSize.ToDecimal(),
				}.TryFillUnderlyingId(symbol.BaseCurrency?.ToUpperInvariant());
				if (!security.IsMatch(lookupMsg, securityTypes))
					continue;
				await SendOutMessageAsync(security, cancellationToken);
				if (--left <= 0)
					break;
			}
		}

		if (left > 0 && IsSectionEnabled(PhemexSections.Futures) &&
			(securityTypes.Count == 0 || securityTypes.Contains(SecurityTypes.Future)))
		{
			var symbols = await RestClient.GetFuturesSymbolsAsync(cancellationToken);
			foreach (var symbol in symbols)
			{
				if (symbol?.Symbol.IsEmpty() != false ||
					!symbol.Status.EqualsIgnoreCase("LISTED") &&
					!symbol.Status.EqualsIgnoreCase("TRADING"))
					continue;
				var code = symbol.Symbol.ToUpperInvariant();
				using (_sync.EnterScope())
					_symbolSections[new(code)] = PhemexSections.Futures;
				var security = new SecurityMessage
				{
					SecurityId = code.ToStockSharp(PhemexSections.Futures),
					Name = symbol.Name.IsEmpty(code),
					SecurityType = SecurityTypes.Future,
					OriginalTransactionId = lookupMsg.TransactionId,
					PriceStep = symbol.QuoteStep.ToDecimal() ?? symbol.QuotePrecision.PrecisionToStep(),
					Decimals = symbol.QuotePrecision,
					VolumeStep = symbol.BaseStep.ToDecimal() ?? symbol.BasePrecision.PrecisionToStep(),
					MinVolume = symbol.MinSizeLimit.ToDecimal(),
					Multiplier = 1m,
				}.TryFillUnderlyingId(symbol.BaseCurrency?.ToUpperInvariant());
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

		var symbol = GetSymbol(mdMsg.SecurityId);
		var section = ResolveSection(mdMsg.SecurityId);
		var ticker = (await RestClient.GetTickersAsync(section, symbol, cancellationToken)).FirstOrDefault();
		var book = (await RestClient.GetBookTickersAsync(section, symbol, cancellationToken)).FirstOrDefault();
		await SendLevel1SnapshotAsync(symbol, section, ticker, book, mdMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var subscribeTrade = false;
		var subscribeDepth = false;
		var subscribeIndex = false;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new() { Symbol = symbol, Section = section });
			subscribeTrade = AddReference(_streamReferences, (section, "TRADE", symbol));
			subscribeDepth = AddReference(_streamReferences, (section, "DEPTH", symbol));
			subscribeIndex = AddReference(_streamReferences, (section, "INDEX", symbol));
		}
		if (subscribeTrade)
			await SubscribePublicAsync("TRADE", symbol, cancellationToken);
		if (subscribeDepth)
			await SubscribePublicAsync("DEPTH", symbol, cancellationToken);
		if (subscribeIndex)
			await SubscribePublicAsync("INDEX", symbol, cancellationToken);
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
		var section = ResolveSection(mdMsg.SecurityId);
		var depth = NormalizeDepth(mdMsg.MaxDepth);
		var book = await RestClient.GetDepthAsync(symbol, depth, cancellationToken);
		await SendBookAsync(book?.Bids, book?.Asks, symbol, section, mdMsg.TransactionId,
			book?.UpdateTime > 0 ? book.UpdateTime.ToUtcTime() : CurrentTime, depth, cancellationToken);
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
				Section = section,
				Depth = depth,
			});
			subscribe = AddReference(_streamReferences, (section, "DEPTH", symbol));
		}
		if (subscribe)
			await SubscribePublicAsync("DEPTH", symbol, cancellationToken);
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
		var section = ResolveSection(mdMsg.SecurityId);
		var from = mdMsg.From?.ToUniversalTime();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var limit = (mdMsg.Count ?? 100).Min(500).Max(10).To<int>();
		var trades = await RestClient.GetTradesAsync(symbol, limit, cancellationToken);
		string lastTradeId = null;
		var lastTime = from ?? default;
		foreach (var trade in trades.OrderBy(static trade => trade.Timestamp.ToUtcTime()))
		{
			var time = trade.Timestamp.ToUtcTime();
			if (time < (from ?? DateTime.MinValue) || time > to)
				continue;
			await SendTradeAsync(trade.TradeId, trade.Price, trade.Size, time,
				trade.Side.ToStockSharpSide(), symbol, section, mdMsg.TransactionId, cancellationToken);
			lastTradeId = trade.TradeId;
			lastTime = time;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

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
			subscribe = AddReference(_streamReferences, (section, "TRADE", symbol));
		}
		if (subscribe)
			await SubscribePublicAsync("TRADE", symbol, cancellationToken);
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
		var section = ResolveSection(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		_ = timeFrame.ToPhemexInterval();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var count = GetCandleCount(mdMsg, timeFrame, to);
		var candles = await RestClient.GetKlinesAsync(symbol, timeFrame, to, count, cancellationToken);
		PhemexKline last = null;
		foreach (var candle in candles.OrderBy(static candle => candle.Time))
		{
			var openTime = candle.Time.ToUtcTime();
			if (openTime < (mdMsg.From?.ToUniversalTime() ?? DateTime.MinValue) || openTime > to)
				continue;
			await SendCandleAsync(openTime, candle.Open.ToDecimal() ?? 0m, candle.High.ToDecimal() ?? 0m,
				candle.Low.ToDecimal() ?? 0m, candle.Close.ToDecimal() ?? 0m,
				candle.Volume.ToDecimal() ?? 0m, symbol, section, timeFrame, mdMsg.TransactionId,
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

		bool subscribe;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				Section = section,
				TimeFrame = timeFrame,
				OpenTime = last?.Time.ToUtcTime() ?? default,
				OpenPrice = last?.Open.ToDecimal() ?? 0m,
				HighPrice = last?.High.ToDecimal() ?? 0m,
				LowPrice = last?.Low.ToDecimal() ?? 0m,
				ClosePrice = last?.Close.ToDecimal() ?? 0m,
				TotalVolume = last?.Volume.ToDecimal() ?? 0m,
				IsInitialized = last is not null,
			});
			subscribe = AddReference(_streamReferences, (section, "TRADE", symbol));
		}
		if (subscribe)
			await SubscribePublicAsync("TRADE", symbol, cancellationToken);
	}

	private ValueTask SubscribePublicAsync(string topic, string symbol,
		CancellationToken cancellationToken)
		=> _marketClient.SubscribeAsync(topic, symbol, topic.EqualsIgnoreCase("DEPTH") ? 100 : null,
			cancellationToken);

	private ValueTask UnsubscribePublicAsync(string topic, string symbol,
		CancellationToken cancellationToken)
		=> _marketClient.UnsubscribeAsync(topic, symbol, topic.EqualsIgnoreCase("DEPTH") ? 100 : null,
			cancellationToken);

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		StreamSubscription subscription = null;
		var trade = false;
		var depth = false;
		var index = false;
		using (_sync.EnterScope())
		{
			if (_level1Subscriptions.Remove(transactionId, out subscription))
			{
				trade = ReleaseReference(_streamReferences, (subscription.Section, "TRADE", subscription.Symbol));
				depth = ReleaseReference(_streamReferences, (subscription.Section, "DEPTH", subscription.Symbol));
				index = ReleaseReference(_streamReferences, (subscription.Section, "INDEX", subscription.Symbol));
			}
		}
		if (subscription is null)
			return;
		if (trade)
			await UnsubscribePublicAsync("TRADE", subscription.Symbol, cancellationToken);
		if (depth)
			await UnsubscribePublicAsync("DEPTH", subscription.Symbol, cancellationToken);
		if (index)
			await UnsubscribePublicAsync("INDEX", subscription.Symbol, cancellationToken);
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
					(subscription.Section, "DEPTH", subscription.Symbol));
		}
		if (unsubscribe)
			await UnsubscribePublicAsync("DEPTH", subscription.Symbol, cancellationToken);
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
					(subscription.Section, "TRADE", subscription.Symbol));
		}
		if (unsubscribe)
			await UnsubscribePublicAsync("TRADE", subscription.Symbol, cancellationToken);
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
					(subscription.Section, "TRADE", subscription.Symbol));
		}
		if (unsubscribe)
			await UnsubscribePublicAsync("TRADE", subscription.Symbol, cancellationToken);
	}

	private async ValueTask OnTradesAsync(PhemexWsTradeMessage message,
		CancellationToken cancellationToken)
	{
		foreach (var trade in (message?.Data ?? []).OrderBy(static trade => trade.Timestamp))
		{
			var symbol = trade.Symbol.IsEmpty(message.Symbol)?.ToUpperInvariant();
			if (symbol.IsEmpty())
				continue;
			var section = ResolveSection(symbol);
			var time = trade.Timestamp > 0 ? trade.Timestamp.ToUtcTime() : CurrentTime;
			var side = trade.Side.ToStockSharpSide();
			long[] level1Ids;
			long[] tickIds;
			CandleEmission[] candleEmissions;
			using (_sync.EnterScope())
			{
				level1Ids = [.. _level1Subscriptions
					.Where(pair => pair.Value.Section == section && pair.Value.Symbol.EqualsIgnoreCase(symbol))
					.Select(static pair => pair.Key)];
				var acceptedTicks = new List<long>();
				foreach (var pair in _tickSubscriptions)
				{
					var state = pair.Value;
					if (state.Section != section || !state.Symbol.EqualsIgnoreCase(symbol) ||
						(!state.LastTradeId.IsEmpty() && state.LastTradeId.EqualsIgnoreCase(trade.TradeId)) ||
						(state.LastTime != default && time < state.LastTime))
						continue;
					state.LastTradeId = trade.TradeId;
					state.LastTime = time;
					acceptedTicks.Add(pair.Key);
				}
				tickIds = [.. acceptedTicks];
				candleEmissions = UpdateCandles(symbol, section, time,
					trade.Price.ToDecimal() ?? 0m, trade.Size.ToDecimal() ?? 0m);
			}

			foreach (var id in level1Ids)
				await SendOutMessageAsync(new Level1ChangeMessage
				{
					SecurityId = symbol.ToStockSharp(section),
					ServerTime = time,
					OriginalTransactionId = id,
				}
				.TryAdd(Level1Fields.LastTradePrice, trade.Price.ToDecimal())
				.TryAdd(Level1Fields.LastTradeVolume, trade.Size.ToDecimal())
				.TryAdd(Level1Fields.LastTradeOrigin, side), cancellationToken);

			foreach (var id in tickIds)
				await SendTradeAsync(trade.TradeId, trade.Price, trade.Size, time, side,
					symbol, section, id, cancellationToken);

			foreach (var emission in candleEmissions)
				await SendCandleAsync(emission.OpenTime, emission.OpenPrice, emission.HighPrice,
					emission.LowPrice, emission.ClosePrice, emission.TotalVolume, emission.Symbol,
					emission.Section, emission.TimeFrame, emission.TransactionId, emission.State,
					cancellationToken);
		}
	}

	private CandleEmission[] UpdateCandles(string symbol, PhemexSections section, DateTime time,
		decimal price, decimal volume)
	{
		var emissions = new List<CandleEmission>();
		foreach (var pair in _candleSubscriptions)
		{
			var state = pair.Value;
			if (state.Section != section || !state.Symbol.EqualsIgnoreCase(symbol))
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

	private static CandleEmission ToEmission(long id, CandleSubscription state, CandleStates candleState)
		=> new(id, state.Symbol, state.Section, state.TimeFrame, state.OpenTime, state.OpenPrice,
			state.HighPrice, state.LowPrice, state.ClosePrice, state.TotalVolume, candleState);

	private async ValueTask OnDepthAsync(PhemexWsDepthMessage message,
		CancellationToken cancellationToken)
	{
		var symbol = message?.Symbol?.ToUpperInvariant();
		if (symbol.IsEmpty() || message.Data is null)
			return;
		var section = ResolveSection(symbol);
		(long Id, int Depth)[] depthSubscriptions;
		long[] level1Subscriptions;
		using (_sync.EnterScope())
		{
			depthSubscriptions = [.. _depthSubscriptions
				.Where(pair => pair.Value.Section == section && pair.Value.Symbol.EqualsIgnoreCase(symbol))
				.Select(static pair => (pair.Key, pair.Value.Depth))];
			level1Subscriptions = [.. _level1Subscriptions
				.Where(pair => pair.Value.Section == section && pair.Value.Symbol.EqualsIgnoreCase(symbol))
				.Select(static pair => pair.Key)];
		}
		var time = message.Timestamp > 0 ? message.Timestamp.ToUtcTime() : CurrentTime;
		foreach (var subscription in depthSubscriptions)
			await SendBookAsync(message.Data.Bids, message.Data.Asks, symbol, section, subscription.Id,
				time, subscription.Depth, cancellationToken);
		foreach (var id in level1Subscriptions)
			await SendBestQuotesAsync(message.Data.Bids, message.Data.Asks, symbol, section, id,
				time, cancellationToken);
	}

	private async ValueTask OnIndexAsync(PhemexWsIndexMessage message,
		CancellationToken cancellationToken)
	{
		foreach (var index in message?.Data ?? [])
		{
			var symbol = index.Symbol.IsEmpty(message.Symbol)?.ToUpperInvariant();
			if (symbol.IsEmpty())
				continue;
			var section = ResolveSection(symbol);
			long[] ids;
			using (_sync.EnterScope())
				ids = [.. _level1Subscriptions
					.Where(pair => pair.Value.Section == section &&
						pair.Value.Symbol.EqualsIgnoreCase(symbol))
					.Select(static pair => pair.Key)];
			var time = index.UpdateTime > 0 ? index.UpdateTime.ToUtcTime() :
				message.Timestamp > 0 ? message.Timestamp.ToUtcTime() : CurrentTime;
			foreach (var id in ids)
				await SendOutMessageAsync(new Level1ChangeMessage
				{
					SecurityId = symbol.ToStockSharp(section),
					ServerTime = time,
					OriginalTransactionId = id,
				}
				.TryAdd(Level1Fields.LastTradePrice, index.LastPrice.ToDecimal())
				.TryAdd(Level1Fields.OpenPrice, index.Open.ToDecimal())
				.TryAdd(Level1Fields.HighPrice, index.High.ToDecimal())
				.TryAdd(Level1Fields.LowPrice, index.Low.ToDecimal())
				.TryAdd(Level1Fields.Volume, index.Volume.ToDecimal())
				.TryAdd(Level1Fields.BestBidPrice, index.BidPrice.ToDecimal())
				.TryAdd(Level1Fields.BestAskPrice, index.AskPrice.ToDecimal())
				.TryAdd(Level1Fields.UnderlyingPrice, index.IndexPrice.ToDecimal())
				.TryAdd(Level1Fields.SettlementPrice, index.MarkPrice.ToDecimal()), cancellationToken);
		}
	}

	private ValueTask SendLevel1SnapshotAsync(string symbol, PhemexSections section,
		PhemexTicker ticker, PhemexBookTicker book, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = ticker?.Time > 0 ? ticker.Time.ToUtcTime() :
				book?.Timestamp.ToUtcTime() ?? CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, ticker?.Close.ToDecimal())
		.TryAdd(Level1Fields.OpenPrice, ticker?.Open.ToDecimal())
		.TryAdd(Level1Fields.HighPrice, ticker?.High.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, ticker?.Low.ToDecimal())
		.TryAdd(Level1Fields.Volume, ticker?.Volume.ToDecimal())
		.TryAdd(Level1Fields.UnderlyingPrice, ticker?.IndexPrice.ToDecimal())
		.TryAdd(Level1Fields.SettlementPrice, ticker?.MarkPrice.ToDecimal())
		.TryAdd(Level1Fields.BestBidPrice, book?.BidPrice.ToDecimal())
		.TryAdd(Level1Fields.BestBidVolume, book?.BidSize.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, book?.AskPrice.ToDecimal())
		.TryAdd(Level1Fields.BestAskVolume, book?.AskSize.ToDecimal()), cancellationToken);

	private ValueTask SendBestQuotesAsync(PhemexBookLevel[] bids, PhemexBookLevel[] asks,
		string symbol, PhemexSections section, long transactionId, DateTime serverTime,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = serverTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, bids?.FirstOrDefault()?.Price.ToDecimal())
		.TryAdd(Level1Fields.BestBidVolume, bids?.FirstOrDefault()?.Size.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, asks?.FirstOrDefault()?.Price.ToDecimal())
		.TryAdd(Level1Fields.BestAskVolume, asks?.FirstOrDefault()?.Size.ToDecimal()), cancellationToken);

	private ValueTask SendBookAsync(PhemexBookLevel[] bids, PhemexBookLevel[] asks,
		string symbol, PhemexSections section, long transactionId, DateTime serverTime, int depth,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = serverTime,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = ToQuotes(bids, depth),
			Asks = ToQuotes(asks, depth),
		}, cancellationToken);

	private ValueTask SendTradeAsync(string id, string price, string volume, DateTime time,
		Sides side, string symbol, PhemexSections section, long transactionId,
		CancellationToken cancellationToken)
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

	private ValueTask SendCandleAsync(DateTime openTime, decimal open, decimal high, decimal low,
		decimal close, decimal volume, string symbol, PhemexSections section, TimeSpan timeFrame,
		long transactionId, CandleStates state, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = symbol.ToStockSharp(section),
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

	private static QuoteChange[] ToQuotes(PhemexBookLevel[] levels, int depth)
		=> [.. (levels ?? []).Take(depth).Select(static level => new QuoteChange(
			level.Price.ToDecimal() ?? 0m, level.Size.ToDecimal() ?? 0m))];

	private static string GetSymbol(SecurityId securityId)
		=> securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode)).ToUpperInvariant();

	private static int GetCandleCount(MarketDataMessage message, TimeSpan timeFrame, DateTime to)
	{
		if (message.Count is long count)
			return count.Min(1000).Max(1).To<int>();
		if (message.From is DateTime from && to > from)
			return ((to - from).Ticks / timeFrame.Ticks + 1).Min(1000L).Max(1L).To<int>();
		return 1000;
	}
}
