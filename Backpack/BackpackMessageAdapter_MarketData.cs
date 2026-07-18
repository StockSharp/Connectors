namespace StockSharp.Backpack;

public partial class BackpackMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;
		BackpackMarket[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values.OrderBy(static market => market.Symbol)];

		foreach (var market in markets)
		{
			var isPerpetual = IsPerpetual(market);
			var filters = market.Filters;
			var security = new SecurityMessage
			{
				SecurityId = market.Symbol.ToStockSharp(),
				Name = isPerpetual
					? $"{market.BaseSymbol}/{market.QuoteSymbol} Perpetual"
					: $"{market.BaseSymbol}/{market.QuoteSymbol}",
				SecurityType = isPerpetual
					? SecurityTypes.Future
					: market.RealWorldAssetMarketType ==
						BackpackRealWorldAssetMarketTypes.Stock
							? SecurityTypes.Stock
							: SecurityTypes.CryptoCurrency,
				PriceStep = filters?.Price?.TickSize,
				VolumeStep = filters?.Quantity?.StepSize,
				MinVolume = filters?.Quantity?.MinimumQuantity,
				MaxVolume = filters?.Quantity?.MaximumQuantity,
				OriginalTransactionId = lookupMsg.TransactionId,
			};
			if (isPerpetual)
				security.TryFillUnderlyingId(market.BaseSymbol);

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
		var market = GetMarket(symbol);
		await SendLevel1SnapshotAsync(symbol, IsPerpetual(market), mdMsg.TransactionId,
			cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		EnsureRealtimeReady();
		var streams = GetLevel1Streams(symbol, IsPerpetual(market));
		var subscribe = new List<string>();
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				IsPerpetual = IsPerpetual(market),
			});
			foreach (var stream in streams)
				if (AddReference(_streamReferences, stream))
					subscribe.Add(stream);
		}

		try
		{
			foreach (var stream in subscribe)
				await ChangePublicStreamAsync(stream, true, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeLevel1Async(mdMsg.TransactionId, cancellationToken);
			throw;
		}
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
		var isPerpetual = IsPerpetual(GetMarket(symbol));
		if (mdMsg.IsHistoryOnly())
		{
			var snapshot = await RestClient.GetDepthAsync(new()
			{
				Symbol = symbol,
				Limit = depth,
			}, cancellationToken);
			await SendDepthSnapshotAsync(symbol, snapshot, mdMsg.TransactionId, depth,
				cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		EnsureRealtimeReady();
		var stream = "depth.200ms." + symbol;
		bool subscribe;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				IsPerpetual = isPerpetual,
				Depth = depth,
			});
			subscribe = AddReference(_streamReferences, stream);
		}

		try
		{
			if (subscribe)
				await WsClient.SubscribeDepthAsync(symbol, cancellationToken);

			var snapshot = await RestClient.GetDepthAsync(new()
			{
				Symbol = symbol,
				Limit = depth,
			}, cancellationToken);

			await _depthProcessing.WaitAsync(cancellationToken);
			try
			{
				DepthSubscription subscription;
				BackpackWsDepth[] pending;
				using (_sync.EnterScope())
				{
					if (!_depthSubscriptions.TryGetValue(mdMsg.TransactionId,
						out subscription))
						return;
					pending = [.. subscription.Pending
						.OrderBy(static update => update.FirstUpdateId)];
					subscription.Pending.Clear();
					subscription.LastUpdateId = snapshot.LastUpdateId;
					subscription.IsSnapshotReady = true;
				}

				await SendDepthSnapshotAsync(symbol, snapshot, mdMsg.TransactionId, depth,
					cancellationToken);
				foreach (var update in pending)
					await ProcessDepthUpdateAsync(mdMsg.TransactionId, subscription, update,
						cancellationToken);
			}
			finally
			{
				_depthProcessing.Release();
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeDepthAsync(mdMsg.TransactionId, cancellationToken);
			throw;
		}
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
		var isPerpetual = IsPerpetual(GetMarket(symbol));
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var from = mdMsg.From?.ToUniversalTime();
		var count = (mdMsg.Count ?? 1000).Min(10000).Max(1).To<int>();
		var trades = await LoadTradesAsync(symbol, from, to, count, cancellationToken);
		foreach (var trade in trades)
			await SendTradeAsync(symbol, trade, mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		EnsureRealtimeReady();
		var stream = "trade." + symbol;
		bool subscribe;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				IsPerpetual = isPerpetual,
			});
			subscribe = AddReference(_streamReferences, stream);
		}

		try
		{
			if (subscribe)
				await WsClient.SubscribeTradesAsync(symbol, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeTicksAsync(mdMsg.TransactionId, cancellationToken);
			throw;
		}
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
		var isPerpetual = IsPerpetual(GetMarket(symbol));
		var timeFrame = mdMsg.GetTimeFrame();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var count = GetCandleCount(mdMsg, timeFrame, to);
		var from = mdMsg.From?.ToUniversalTime() ??
			to - TimeSpan.FromTicks(timeFrame.Ticks * count);
		var candles = await LoadCandlesAsync(symbol, timeFrame, from, to, count,
			cancellationToken);
		foreach (var candle in candles)
			await SendCandleAsync(symbol, candle, timeFrame, mdMsg.TransactionId,
				cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		EnsureRealtimeReady();
		var stream = $"kline.{timeFrame.ToBackpackInterval()}.{symbol}";
		bool subscribe;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				IsPerpetual = isPerpetual,
				TimeFrame = timeFrame,
			});
			subscribe = AddReference(_streamReferences, stream);
		}

		try
		{
			if (subscribe)
				await WsClient.SubscribeKlinesAsync(symbol, timeFrame, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeCandlesAsync(mdMsg.TransactionId, cancellationToken);
			throw;
		}
	}

	private async ValueTask SendLevel1SnapshotAsync(string symbol, bool isPerpetual,
		long transactionId, CancellationToken cancellationToken)
	{
		var ticker = await RestClient.GetTickerAsync(symbol, cancellationToken);
		var depth = await RestClient.GetDepthAsync(new()
		{
			Symbol = symbol,
			Limit = 5,
		}, cancellationToken);
		var bid = depth.Bids?.OrderByDescending(static level => level.Price).FirstOrDefault();
		var ask = depth.Asks?.OrderBy(static level => level.Price).FirstOrDefault();
		var message = new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(),
			ServerTime = depth.Timestamp > 0
				? depth.Timestamp.ToUtcMicroseconds()
				: CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, bid?.Price)
		.TryAdd(Level1Fields.BestBidVolume, bid?.Quantity)
		.TryAdd(Level1Fields.BestAskPrice, ask?.Price)
		.TryAdd(Level1Fields.BestAskVolume, ask?.Quantity)
		.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice)
		.TryAdd(Level1Fields.OpenPrice, ticker.FirstPrice)
		.TryAdd(Level1Fields.HighPrice, ticker.High)
		.TryAdd(Level1Fields.LowPrice, ticker.Low)
		.TryAdd(Level1Fields.Volume, ticker.Volume)
		.TryAdd(Level1Fields.Turnover, ticker.QuoteVolume)
		.TryAdd(Level1Fields.Change, ticker.PriceChange);

		if (isPerpetual)
		{
			var markPrice = (await RestClient.GetMarkPricesAsync(symbol, cancellationToken))
				.FirstOrDefault(item => item.Symbol.EqualsIgnoreCase(symbol));
			if (markPrice is not null)
				AddMarkPrice(message, markPrice.MarkPrice, markPrice.IndexPrice);
		}

		await SendOutMessageAsync(message, cancellationToken);
	}

	private async ValueTask<BackpackTrade[]> LoadTradesAsync(string symbol, DateTime? from,
		DateTime to, int count, CancellationToken cancellationToken)
	{
		if (from is null)
		{
			var recent = await RestClient.GetTradesAsync(new()
			{
				Symbol = symbol,
				Limit = count.Min(1000),
			}, cancellationToken);
			return [.. (recent ?? [])
				.Where(trade => trade.Timestamp.ToUtcMicroseconds() <= to)
				.OrderBy(static trade => trade.Timestamp)
				.TakeLast(count)];
		}

		var result = new List<BackpackTrade>();
		for (var offset = 0; result.Count < count; offset += 1000)
		{
			var page = await RestClient.GetHistoricalTradesAsync(new()
			{
				Symbol = symbol,
				Limit = (count - result.Count).Min(1000),
				Offset = offset,
			}, cancellationToken);
			if (page is not { Length: > 0 })
				break;
			result.AddRange(page.Where(trade =>
			{
				var time = trade.Timestamp.ToUtcMicroseconds();
				return time >= from.Value && time <= to;
			}));
			if (page.Length < 1000 || page.Min(static trade => trade.Timestamp)
					.ToUtcMicroseconds() < from.Value)
				break;
		}

		return [.. result.GroupBy(static trade => trade.Id)
			.Select(static group => group.First())
			.OrderBy(static trade => trade.Timestamp)
			.TakeLast(count)];
	}

	private async ValueTask<BackpackKline[]> LoadCandlesAsync(string symbol,
		TimeSpan timeFrame, DateTime from, DateTime to, int count,
		CancellationToken cancellationToken)
	{
		var candles = await RestClient.GetKlinesAsync(new()
		{
			Symbol = symbol,
			Interval = timeFrame.ToBackpackInterval(),
			StartTime = from.ToUnixSeconds(),
			EndTime = to.ToUnixSeconds(),
		}, cancellationToken);
		return [.. (candles ?? [])
			.Where(candle => candle.Start.ToBackpackTime() >= from &&
				candle.Start.ToBackpackTime() <= to)
			.GroupBy(static candle => candle.Start)
			.Select(static group => group.First())
			.OrderBy(static candle => candle.Start.ToBackpackTime())
			.TakeLast(count)];
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var unsubscribe = new List<string>();
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(transactionId, out subscription))
				foreach (var stream in GetLevel1Streams(subscription.Symbol,
					subscription.IsPerpetual))
					if (ReleaseReference(_streamReferences, stream))
						unsubscribe.Add(stream);
		if (_wsClient is not null)
			foreach (var stream in unsubscribe)
				await ChangePublicStreamAsync(stream, false, cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
			if (_depthSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences,
					"depth.200ms." + subscription.Symbol);
		if (unsubscribe && _wsClient is not null)
			await _wsClient.UnsubscribeDepthAsync(subscription.Symbol, cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
			if (_tickSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences,
					"trade." + subscription.Symbol);
		if (unsubscribe && _wsClient is not null)
			await _wsClient.UnsubscribeTradesAsync(subscription.Symbol, cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		CandleSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
			if (_candleSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences,
					$"kline.{subscription.TimeFrame.ToBackpackInterval()}.{subscription.Symbol}");
		if (unsubscribe && _wsClient is not null)
			await _wsClient.UnsubscribeKlinesAsync(subscription.Symbol,
				subscription.TimeFrame, cancellationToken);
	}

	private async ValueTask OnBookTickerAsync(BackpackWsBookTicker ticker,
		CancellationToken cancellationToken)
	{
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _level1Subscriptions
				.Where(pair => pair.Value.Symbol.EqualsIgnoreCase(ticker.Symbol))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = ticker.Symbol.ToStockSharp(),
				ServerTime = ticker.EventTime > 0
					? ticker.EventTime.ToUtcMicroseconds()
					: CurrentTime,
				OriginalTransactionId = id,
				SeqNum = ticker.UpdateId,
			}
			.TryAdd(Level1Fields.BestBidPrice, ticker.BidPrice)
			.TryAdd(Level1Fields.BestBidVolume, ticker.BidQuantity)
			.TryAdd(Level1Fields.BestAskPrice, ticker.AskPrice)
			.TryAdd(Level1Fields.BestAskVolume, ticker.AskQuantity), cancellationToken);
	}

	private async ValueTask OnTickerAsync(BackpackWsTicker ticker,
		CancellationToken cancellationToken)
	{
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _level1Subscriptions
				.Where(pair => pair.Value.Symbol.EqualsIgnoreCase(ticker.Symbol))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = ticker.Symbol.ToStockSharp(),
				ServerTime = ticker.EventTime > 0
					? ticker.EventTime.ToUtcMicroseconds()
					: CurrentTime,
				OriginalTransactionId = id,
			}
			.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice)
			.TryAdd(Level1Fields.OpenPrice, ticker.FirstPrice)
			.TryAdd(Level1Fields.HighPrice, ticker.High)
			.TryAdd(Level1Fields.LowPrice, ticker.Low)
			.TryAdd(Level1Fields.Volume, ticker.Volume)
			.TryAdd(Level1Fields.Turnover, ticker.QuoteVolume), cancellationToken);
	}

	private async ValueTask OnMarkPriceAsync(BackpackWsMarkPrice markPrice,
		CancellationToken cancellationToken)
	{
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _level1Subscriptions
				.Where(pair => pair.Value.Symbol.EqualsIgnoreCase(markPrice.Symbol))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
		{
			var message = new Level1ChangeMessage
			{
				SecurityId = markPrice.Symbol.ToStockSharp(),
				ServerTime = markPrice.EventTime > 0
					? markPrice.EventTime.ToUtcMicroseconds()
					: CurrentTime,
				OriginalTransactionId = id,
			};
			AddMarkPrice(message, markPrice.MarkPrice, markPrice.IndexPrice);
			await SendOutMessageAsync(message, cancellationToken);
		}
	}

	private async ValueTask OnTradeAsync(BackpackWsTrade trade,
		CancellationToken cancellationToken)
	{
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _tickSubscriptions
				.Where(pair => pair.Value.Symbol.EqualsIgnoreCase(trade.Symbol))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = trade.Symbol.ToStockSharp(),
				ServerTime = trade.EventTime > 0
					? trade.EventTime.ToUtcMicroseconds()
					: CurrentTime,
				OriginalTransactionId = id,
				TradeId = trade.TradeId,
				TradePrice = trade.Price,
				TradeVolume = trade.Quantity,
				OriginSide = trade.IsBuyerMaker ? Sides.Sell : Sides.Buy,
			}, cancellationToken);
	}

	private async ValueTask OnKlineAsync(string stream, BackpackWsKline candle,
		CancellationToken cancellationToken)
	{
		var parts = stream.Split('.', 3);
		if (parts.Length != 3)
			throw new InvalidDataException(
				$"Invalid Backpack Exchange kline stream '{stream}'.");
		var timeFrame = parts[1].ToTimeFrame();
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _candleSubscriptions
				.Where(pair => pair.Value.Symbol.EqualsIgnoreCase(candle.Symbol) &&
					pair.Value.TimeFrame == timeFrame)
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				SecurityId = candle.Symbol.ToStockSharp(),
				OpenTime = candle.StartTime.ToBackpackTime(),
				CloseTime = candle.CloseTime.ToBackpackTime(),
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				TypedArg = timeFrame,
				OriginalTransactionId = id,
				State = candle.IsClosed ? CandleStates.Finished : CandleStates.Active,
			}, cancellationToken);
	}

	private async ValueTask OnDepthAsync(BackpackWsDepth update,
		CancellationToken cancellationToken)
	{
		await _depthProcessing.WaitAsync(cancellationToken);
		try
		{
			(long Id, DepthSubscription Subscription)[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _depthSubscriptions
					.Where(pair => pair.Value.Symbol.EqualsIgnoreCase(update.Symbol))
					.Select(static pair => (pair.Key, pair.Value))];

			foreach (var item in subscriptions)
			{
				if (!item.Subscription.IsSnapshotReady)
				{
					using (_sync.EnterScope())
						if (_depthSubscriptions.ContainsKey(item.Id))
							item.Subscription.Pending.Add(update);
					continue;
				}
				await ProcessDepthUpdateAsync(item.Id, item.Subscription, update,
					cancellationToken);
			}
		}
		finally
		{
			_depthProcessing.Release();
		}
	}

	private async ValueTask ProcessDepthUpdateAsync(long transactionId,
		DepthSubscription subscription, BackpackWsDepth update,
		CancellationToken cancellationToken)
	{
		if (update.LastUpdateId <= subscription.LastUpdateId)
			return;
		if (update.FirstUpdateId > subscription.LastUpdateId + 1)
		{
			await ResynchronizeDepthAsync(transactionId, subscription, cancellationToken);
			if (update.LastUpdateId <= subscription.LastUpdateId)
				return;
			if (update.FirstUpdateId > subscription.LastUpdateId + 1)
				return;
		}
		if (update.FirstUpdateId > subscription.LastUpdateId + 1 ||
			update.LastUpdateId < subscription.LastUpdateId + 1)
			return;

		subscription.LastUpdateId = update.LastUpdateId;
		await SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = subscription.Symbol.ToStockSharp(),
			ServerTime = update.EventTime > 0
				? update.EventTime.ToUtcMicroseconds()
				: CurrentTime,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.Increment,
			SeqNum = update.LastUpdateId,
			Bids = ToQuotes(update.Bids, false, int.MaxValue),
			Asks = ToQuotes(update.Asks, true, int.MaxValue),
		}, cancellationToken);
	}

	private async ValueTask ResynchronizeDepthAsync(long transactionId,
		DepthSubscription subscription, CancellationToken cancellationToken)
	{
		var snapshot = await RestClient.GetDepthAsync(new()
		{
			Symbol = subscription.Symbol,
			Limit = subscription.Depth,
		}, cancellationToken);
		subscription.LastUpdateId = snapshot.LastUpdateId;
		subscription.IsSnapshotReady = true;
		await SendDepthSnapshotAsync(subscription.Symbol, snapshot, transactionId,
			subscription.Depth, cancellationToken);
	}

	private async ValueTask ResynchronizeAllDepthsAsync(CancellationToken cancellationToken)
	{
		await _depthProcessing.WaitAsync(cancellationToken);
		try
		{
			(long Id, DepthSubscription Subscription)[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _depthSubscriptions
					.Select(static pair => (pair.Key, pair.Value))];
			foreach (var item in subscriptions)
				await ResynchronizeDepthAsync(item.Id, item.Subscription, cancellationToken);
		}
		finally
		{
			_depthProcessing.Release();
		}
	}

	private ValueTask SendDepthSnapshotAsync(string symbol, BackpackDepth snapshot,
		long transactionId, int depth, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(),
			ServerTime = snapshot.Timestamp > 0
				? snapshot.Timestamp.ToUtcMicroseconds()
				: CurrentTime,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			SeqNum = snapshot.LastUpdateId,
			Bids = ToQuotes(snapshot.Bids, false, depth),
			Asks = ToQuotes(snapshot.Asks, true, depth),
		}, cancellationToken);

	private static QuoteChange[] ToQuotes(BackpackPriceLevel[] levels, bool isAscending,
		int depth)
	{
		var ordered = isAscending
			? (levels ?? []).OrderBy(static level => level.Price)
			: (levels ?? []).OrderByDescending(static level => level.Price);
		return [.. ordered.Take(depth)
			.Select(static level => new QuoteChange(level.Price, level.Quantity))];
	}

	private ValueTask SendTradeAsync(string symbol, BackpackTrade trade, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = symbol.ToStockSharp(),
			ServerTime = trade.Timestamp.ToUtcMicroseconds(),
			OriginalTransactionId = transactionId,
			TradeId = trade.Id,
			TradePrice = trade.Price,
			TradeVolume = trade.Quantity,
			OriginSide = trade.IsBuyerMaker ? Sides.Sell : Sides.Buy,
		}, cancellationToken);

	private ValueTask SendCandleAsync(string symbol, BackpackKline candle,
		TimeSpan timeFrame, long transactionId, CancellationToken cancellationToken)
	{
		var closeTime = candle.End.ToBackpackTime();
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = symbol.ToStockSharp(),
			OpenTime = candle.Start.ToBackpackTime(),
			CloseTime = closeTime,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.Volume,
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = closeTime <= CurrentTime
				? CandleStates.Finished
				: CandleStates.Active,
		}, cancellationToken);
	}

	private static void AddMarkPrice(Level1ChangeMessage message, decimal markPrice,
		decimal? indexPrice)
	{
		message
			.TryAdd(Level1Fields.SettlementPrice, markPrice)
			.TryAdd(Level1Fields.Index, indexPrice);
	}

	private static string[] GetLevel1Streams(string symbol, bool isPerpetual)
		=> isPerpetual
			? ["ticker." + symbol, "bookTicker." + symbol, "markPrice." + symbol]
			: ["ticker." + symbol, "bookTicker." + symbol];

	private ValueTask ChangePublicStreamAsync(string stream, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		var separator = stream.LastIndexOf('.');
		if (separator <= 0 || separator >= stream.Length - 1)
			throw new InvalidDataException(
				$"Invalid Backpack Exchange stream '{stream}'.");
		var symbol = stream[(separator + 1)..];
		if (stream.StartsWith("bookTicker.", StringComparison.OrdinalIgnoreCase))
			return isSubscribe
				? WsClient.SubscribeBookTickerAsync(symbol, cancellationToken)
				: WsClient.UnsubscribeBookTickerAsync(symbol, cancellationToken);
		if (stream.StartsWith("ticker.", StringComparison.OrdinalIgnoreCase))
			return isSubscribe
				? WsClient.SubscribeTickerAsync(symbol, cancellationToken)
				: WsClient.UnsubscribeTickerAsync(symbol, cancellationToken);
		if (stream.StartsWith("markPrice.", StringComparison.OrdinalIgnoreCase))
			return isSubscribe
				? WsClient.SubscribeMarkPriceAsync(symbol, cancellationToken)
				: WsClient.UnsubscribeMarkPriceAsync(symbol, cancellationToken);
		throw new InvalidDataException(
			$"Unsupported Backpack Exchange public stream '{stream}'.");
	}

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
