namespace StockSharp.Deepcoin;

public partial class DeepcoinMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var productType in new[] { DeepcoinProductTypes.Spot, DeepcoinProductTypes.Swap })
		{
			foreach (var instrument in (await RestClient.GetInstrumentsAsync(productType, null,
				cancellationToken) ?? [])
				.Where(static item => item?.InstrumentId.IsEmpty() == false)
				.OrderBy(static item => item.InstrumentId))
			{
				if (instrument.State != DeepcoinInstrumentStates.Live)
					continue;

				RegisterInstrument(instrument.InstrumentId);
				var isSpot = instrument.ProductType == DeepcoinProductTypes.Spot;
				var security = new SecurityMessage
				{
					SecurityId = instrument.InstrumentId.ToStockSharp(),
					Name = isSpot
						? $"{instrument.BaseCurrency}/{instrument.QuoteCurrency}"
						: $"{instrument.BaseCurrency}/{instrument.QuoteCurrency} Perpetual",
					SecurityType = isSpot ? SecurityTypes.CryptoCurrency : SecurityTypes.Future,
					OriginalTransactionId = lookupMsg.TransactionId,
					PriceStep = instrument.TickSize.ToDecimal(),
					VolumeStep = instrument.LotSize.ToDecimal(),
					MinVolume = instrument.MinimumSize.ToDecimal(),
					MaxVolume = instrument.MaximumLimitSize.ToDecimal(),
					Multiplier = isSpot ? null : instrument.ContractValue.ToDecimal(),
				}.TryFillUnderlyingId(instrument.BaseCurrency?.ToUpperInvariant());
				if (!security.IsMatch(lookupMsg, securityTypes))
					continue;
				await SendOutMessageAsync(security, cancellationToken);
				if (--left <= 0)
					break;
			}
			if (left <= 0)
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
		var productType = ResolveProductType(symbol);
		RegisterInstrument(symbol);
		var ticker = (await RestClient.GetTickersAsync(productType, cancellationToken) ?? [])
			.FirstOrDefault(item => item?.InstrumentId.EqualsIgnoreCase(symbol) == true);
		if (ticker is null)
			throw new InvalidDataException($"Deepcoin returned no ticker for '{symbol}'.");
		await SendTickerAsync(ticker, mdMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var key = new StreamKey(productType, DeepcoinWsTopics.Market, symbol, default);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				InstrumentId = symbol,
				ProductType = productType,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		if (subscribe)
			await GetPublicClient(productType).SubscribeTickerAsync(symbol, cancellationToken);
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
		var productType = ResolveProductType(symbol);
		var depth = NormalizeDepth(mdMsg.MaxDepth);
		RegisterInstrument(symbol);
		var book = await RestClient.GetBookAsync(symbol, depth, cancellationToken);
		if (book is null)
			throw new InvalidDataException($"Deepcoin returned no order book for '{symbol}'.");
		await SendBookAsync(symbol, book.Bids, book.Asks, QuoteChangeStates.SnapshotComplete,
			CurrentTime, mdMsg.TransactionId, depth, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var key = new StreamKey(productType, DeepcoinWsTopics.Book, symbol, default);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				InstrumentId = symbol,
				ProductType = productType,
				Depth = depth,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		if (subscribe)
			await GetPublicClient(productType).SubscribeBookAsync(symbol, cancellationToken);
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
		var productType = ResolveProductType(symbol);
		var from = mdMsg.From?.ToUniversalTime() ?? DateTime.MinValue;
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var limit = (mdMsg.Count ?? 100).Min(500).Max(1).To<int>();
		RegisterInstrument(symbol);
		foreach (var trade in (await RestClient.GetTradesAsync(symbol, limit, cancellationToken) ?? [])
			.Where(static item => item?.TradeId.IsEmpty() == false)
			.GroupBy(static item => item.TradeId)
			.Select(static group => group.First())
			.Where(trade => trade.Timestamp.ToUtcTime() is DateTime time && time >= from && time <= to)
			.OrderBy(static trade => trade.Timestamp.ToInt64()))
			await SendTradeAsync(trade, mdMsg.TransactionId, cancellationToken);

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var key = new StreamKey(productType, DeepcoinWsTopics.Trade, symbol, default);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				InstrumentId = symbol,
				ProductType = productType,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		if (subscribe)
			await GetPublicClient(productType).SubscribeTradesAsync(symbol, cancellationToken);
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
		var productType = ResolveProductType(symbol);
		var timeFrame = mdMsg.GetTimeFrame();
		var interval = timeFrame.ToDeepcoinRestInterval();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var requestedCount = GetCandleCount(mdMsg, timeFrame, to);
		var from = mdMsg.From?.ToUniversalTime() ??
			to - TimeSpan.FromTicks(timeFrame.Ticks * requestedCount);
		RegisterInstrument(symbol);
		var candles = await LoadCandlesAsync(symbol, interval, from, to, requestedCount,
			cancellationToken);
		foreach (var candle in candles)
			await SendCandleAsync(symbol, candle, timeFrame, mdMsg.TransactionId, cancellationToken);

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var key = new StreamKey(productType, DeepcoinWsTopics.Kline, symbol, timeFrame);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				InstrumentId = symbol,
				ProductType = productType,
				TimeFrame = timeFrame,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		if (subscribe)
			await GetPublicClient(productType).SubscribeCandlesAsync(symbol, timeFrame,
				cancellationToken);
	}

	private async ValueTask<DeepcoinCandle[]> LoadCandlesAsync(string symbol,
		DeepcoinRestCandleIntervals interval, DateTime from, DateTime to, int requestedCount,
		CancellationToken cancellationToken)
	{
		var candles = new List<DeepcoinCandle>();
		var timestamps = new HashSet<long>();
		var cursor = to.ToUnixMilliseconds() + 1;
		while (candles.Count < requestedCount)
		{
			var limit = (requestedCount - candles.Count).Min(300).Max(1);
			var page = await RestClient.GetCandlesAsync(new()
			{
				InstrumentId = symbol,
				Interval = interval,
				EndTime = cursor,
				Limit = limit,
			}, cancellationToken) ?? [];
			if (page.Length == 0)
				break;
			foreach (var candle in page)
			{
				var time = candle.Timestamp.ToUtcTime();
				if (time >= from && time <= to && timestamps.Add(candle.Timestamp))
					candles.Add(candle);
			}
			var earliest = page.Min(static candle => candle.Timestamp);
			if (earliest.ToUtcTime() <= from || earliest >= cursor || page.Length < limit)
				break;
			cursor = earliest;
		}
		return [.. candles.OrderBy(static candle => candle.Timestamp).TakeLast(requestedCount)];
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_level1Subscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences, new(subscription.ProductType,
					DeepcoinWsTopics.Market, subscription.InstrumentId, default));
		}
		if (unsubscribe)
			await GetPublicClient(subscription.ProductType).UnsubscribeTickerAsync(
				subscription.InstrumentId, cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_depthSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences, new(subscription.ProductType,
					DeepcoinWsTopics.Book, subscription.InstrumentId, default));
		}
		if (unsubscribe)
			await GetPublicClient(subscription.ProductType).UnsubscribeBookAsync(
				subscription.InstrumentId, cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_tickSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences, new(subscription.ProductType,
					DeepcoinWsTopics.Trade, subscription.InstrumentId, default));
		}
		if (unsubscribe)
			await GetPublicClient(subscription.ProductType).UnsubscribeTradesAsync(
				subscription.InstrumentId, cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		CandleSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_candleSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences, new(subscription.ProductType,
					DeepcoinWsTopics.Kline, subscription.InstrumentId, subscription.TimeFrame));
		}
		if (unsubscribe)
			await GetPublicClient(subscription.ProductType).UnsubscribeCandlesAsync(
				subscription.InstrumentId, subscription.TimeFrame, cancellationToken);
	}

	private async ValueTask OnTickerAsync(DeepcoinProductTypes productType, string instrumentId,
		DeepcoinWsTicker ticker, CancellationToken cancellationToken)
	{
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _level1Subscriptions
				.Where(pair => pair.Value.ProductType == productType &&
					pair.Value.InstrumentId.EqualsIgnoreCase(instrumentId))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendTickerAsync(instrumentId, ticker, id, cancellationToken);
	}

	private async ValueTask OnBookAsync(DeepcoinProductTypes productType, string instrumentId,
		DeepcoinWsBook book, QuoteChangeStates state, long timestamp,
		CancellationToken cancellationToken)
	{
		(long Id, int Depth)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions
				.Where(pair => pair.Value.ProductType == productType &&
					pair.Value.InstrumentId.EqualsIgnoreCase(instrumentId))
				.Select(static pair => (pair.Key, pair.Value.Depth))];
		foreach (var subscription in subscriptions)
			await SendBookAsync(instrumentId, book.Bids, book.Asks, state,
				timestamp > 0 ? timestamp.ToUtcTime() : CurrentTime, subscription.Id,
				state == QuoteChangeStates.Increment ? int.MaxValue : subscription.Depth,
				cancellationToken);
	}

	private async ValueTask OnTradeAsync(DeepcoinProductTypes productType, string instrumentId,
		DeepcoinWsTrade trade, CancellationToken cancellationToken)
	{
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _tickSubscriptions
				.Where(pair => pair.Value.ProductType == productType &&
					pair.Value.InstrumentId.EqualsIgnoreCase(instrumentId))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendTradeAsync(instrumentId, trade, id, cancellationToken);
	}

	private async ValueTask OnCandleAsync(DeepcoinProductTypes productType, string instrumentId,
		DeepcoinWsCandleIntervals period, DeepcoinCandle candle,
		CancellationToken cancellationToken)
	{
		(long Id, TimeSpan TimeFrame)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _candleSubscriptions
				.Where(pair => pair.Value.ProductType == productType &&
					pair.Value.InstrumentId.EqualsIgnoreCase(instrumentId) &&
					pair.Value.TimeFrame.ToDeepcoinWsInterval() == period)
				.Select(static pair => (pair.Key, pair.Value.TimeFrame))];
		foreach (var subscription in subscriptions)
			await SendCandleAsync(instrumentId, candle, subscription.TimeFrame,
				subscription.Id, cancellationToken);
	}

	private ValueTask SendTickerAsync(DeepcoinTicker ticker, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ticker.InstrumentId.ToStockSharp(),
			ServerTime = ticker.Timestamp.ToUtcTime() ?? CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice.ToDecimal())
		.TryAdd(Level1Fields.LastTradeVolume, ticker.LastSize.ToDecimal())
		.TryAdd(Level1Fields.OpenPrice, ticker.OpenPrice.ToDecimal())
		.TryAdd(Level1Fields.HighPrice, ticker.HighPrice.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, ticker.LowPrice.ToDecimal())
		.TryAdd(Level1Fields.Volume, ticker.BaseVolume.ToDecimal())
		.TryAdd(Level1Fields.Turnover, ticker.QuoteVolume.ToDecimal())
		.TryAdd(Level1Fields.BestBidPrice, ticker.BidPrice.ToDecimal())
		.TryAdd(Level1Fields.BestBidVolume, ticker.BidSize.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, ticker.AskPrice.ToDecimal())
		.TryAdd(Level1Fields.BestAskVolume, ticker.AskSize.ToDecimal()), cancellationToken);

	private ValueTask SendTickerAsync(string instrumentId, DeepcoinWsTicker ticker,
		long transactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = instrumentId.ToStockSharp(),
			ServerTime = ticker.Timestamp > 0 ? ticker.Timestamp.ToUtcTime() : CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice)
		.TryAdd(Level1Fields.OpenPrice, ticker.OpenPrice)
		.TryAdd(Level1Fields.HighPrice, ticker.HighPrice)
		.TryAdd(Level1Fields.LowPrice, ticker.LowPrice)
		.TryAdd(Level1Fields.Volume, ticker.BaseVolume)
		.TryAdd(Level1Fields.Turnover, ticker.QuoteVolume)
		.TryAdd(Level1Fields.BestBidPrice, ticker.BidPrice)
		.TryAdd(Level1Fields.BestAskPrice, ticker.AskPrice)
		.TryAdd(Level1Fields.TheorPrice, ticker.MarkPrice)
		.TryAdd(Level1Fields.UnderlyingPrice, ticker.UnderlyingPrice)
		.TryAdd(Level1Fields.MinPrice, ticker.MinimumPrice)
		.TryAdd(Level1Fields.MaxPrice, ticker.MaximumPrice), cancellationToken);

	private ValueTask SendBookAsync(string instrumentId, DeepcoinBookLevel[] bids,
		DeepcoinBookLevel[] asks, QuoteChangeStates state, DateTime serverTime,
		long transactionId, int depth, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = instrumentId.ToStockSharp(),
			ServerTime = serverTime,
			OriginalTransactionId = transactionId,
			State = state,
			Bids = ToQuotes(bids, depth),
			Asks = ToQuotes(asks, depth),
		}, cancellationToken);

	private ValueTask SendTradeAsync(DeepcoinTrade trade, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = trade.InstrumentId.ToStockSharp(),
			ServerTime = trade.Timestamp.ToUtcTime() ?? CurrentTime,
			OriginalTransactionId = transactionId,
			TradeStringId = trade.TradeId,
			TradePrice = trade.Price.ToDecimal(),
			TradeVolume = trade.Size.ToDecimal(),
			OriginSide = trade.Side.ToStockSharpSide(),
		}, cancellationToken);

	private ValueTask SendTradeAsync(string instrumentId, DeepcoinWsTrade trade,
		long transactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = instrumentId.ToStockSharp(),
			ServerTime = trade.Timestamp > 0 ? trade.Timestamp.ToUtcTime() : CurrentTime,
			OriginalTransactionId = transactionId,
			TradeStringId = trade.TradeId,
			TradePrice = trade.Price,
			TradeVolume = trade.Volume,
			OriginSide = trade.Direction.ToStockSharpSide(),
		}, cancellationToken);

	private ValueTask SendCandleAsync(string instrumentId, DeepcoinCandle candle,
		TimeSpan timeFrame, long transactionId, CancellationToken cancellationToken)
	{
		var openTime = candle.Timestamp.ToUtcTime();
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = instrumentId.ToStockSharp(),
			OpenTime = openTime,
			CloseTime = openTime + timeFrame,
			OpenPrice = candle.Open.ToDecimal() ?? 0m,
			HighPrice = candle.High.ToDecimal() ?? 0m,
			LowPrice = candle.Low.ToDecimal() ?? 0m,
			ClosePrice = candle.Close.ToDecimal() ?? 0m,
			TotalVolume = candle.BaseVolume.ToDecimal() ?? 0m,
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = openTime + timeFrame <= CurrentTime
				? CandleStates.Finished
				: CandleStates.Active,
		}, cancellationToken);
	}

	private static QuoteChange[] ToQuotes(DeepcoinBookLevel[] levels, int depth)
		=> [.. (levels ?? []).Take(depth).Select(static level => new QuoteChange(
			level.Price.ToDecimal() ?? 0m, level.Size.ToDecimal() ?? 0m))];

	private static int GetCandleCount(MarketDataMessage message, TimeSpan timeFrame, DateTime to)
	{
		if (message.Count is long count)
			return count.Min(10000).Max(1).To<int>();
		if (message.From is DateTime from && to > from)
			return ((to - from.ToUniversalTime()).Ticks / timeFrame.Ticks + 1)
				.Min(10000L).Max(1L).To<int>();
		return 300;
	}
}
