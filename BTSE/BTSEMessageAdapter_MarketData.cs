namespace StockSharp.BTSE;

public partial class BTSEMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;
		var markets = new List<(BTSESections Section, BTSEMarketSummary Market)>();
		using (_sync.EnterScope())
		{
			if (IsSectionEnabled(BTSESections.Spot))
				markets.AddRange(_spotMarkets.Values.Select(static market =>
					(BTSESections.Spot, market)));
			if (IsSectionEnabled(BTSESections.Futures))
				markets.AddRange(_futuresMarkets.Values.Select(static market =>
					(BTSESections.Futures, market)));
		}

		foreach (var item in markets.OrderBy(static item => item.Section)
			.ThenBy(static item => item.Market.Symbol))
		{
			var market = item.Market;
			var isFutures = item.Section == BTSESections.Futures;
			var security = new SecurityMessage
			{
				SecurityId = market.Symbol.ToStockSharp(item.Section),
				Name = isFutures
					? $"{market.Base}/{market.Quote} Futures"
					: $"{market.Base}/{market.Quote}",
				SecurityType = isFutures
					? SecurityTypes.Future
					: SecurityTypes.CryptoCurrency,
				PriceStep = market.MinimumPriceIncrement,
				VolumeStep = market.MinimumSizeIncrement,
				MinVolume = market.MinimumOrderSize,
				MaxVolume = market.MaximumOrderSize,
				Multiplier = isFutures ? market.ContractSize : null,
				ExpiryDate = isFutures && market.ContractEnd is > 0
					? market.ContractEnd.Value.FromMilliseconds()
					: null,
				OriginalTransactionId = lookupMsg.TransactionId,
			};
			if (isFutures && !market.Base.IsEmpty())
				security.TryFillUnderlyingId(market.Base);

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

		var section = ResolveSection(mdMsg.SecurityId);
		var symbol = GetSymbol(mdMsg.SecurityId, section);
		await SendLevel1SnapshotAsync(section, symbol, mdMsg.TransactionId,
			cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var key = new StreamKey(section, "snapshotL1:" + symbol);
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

		try
		{
			if (subscribe)
				await GetBookWsClient(section).SubscribeLevel1Async(symbol,
					cancellationToken);
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

		var section = ResolveSection(mdMsg.SecurityId);
		var symbol = GetSymbol(mdMsg.SecurityId, section);
		var depth = (mdMsg.MaxDepth ?? 50).Min(50).Max(1);
		if (mdMsg.IsHistoryOnly())
		{
			var snapshot = await GetRestClient(section).GetDepthAsync(new()
			{
				Symbol = symbol,
				Depth = depth,
			}, cancellationToken);
			await SendDepthSnapshotAsync(section, snapshot, mdMsg.TransactionId, depth,
				cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var key = new StreamKey(section, "update:" + symbol + "_0");
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

		try
		{
			if (subscribe)
				await GetBookWsClient(section).SubscribeDepthAsync(symbol,
					cancellationToken);
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

		var section = ResolveSection(mdMsg.SecurityId);
		var symbol = GetSymbol(mdMsg.SecurityId, section);
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var from = mdMsg.From?.ToUniversalTime();
		var count = (mdMsg.Count ?? 1000).Min(10000).Max(1).To<int>();
		var trades = await LoadTradesAsync(section, symbol, from, to, count,
			cancellationToken);
		foreach (var trade in trades)
			await SendTradeAsync(section, trade, mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var topic = (section == BTSESections.Spot
			? "tradeHistoryApi:"
			: "tradeHistoryApiV3:") + symbol;
		var key = new StreamKey(section, topic);
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

		try
		{
			if (subscribe)
				await GetGeneralWsClient(section).SubscribeTradesAsync(symbol,
					cancellationToken);
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
			return;

		var section = ResolveSection(mdMsg.SecurityId);
		var symbol = GetSymbol(mdMsg.SecurityId, section);
		var timeFrame = mdMsg.GetTimeFrame();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var count = GetCandleCount(mdMsg, timeFrame, to);
		var from = mdMsg.From?.ToUniversalTime() ??
			to - TimeSpan.FromTicks(timeFrame.Ticks * count);
		var candles = await LoadCandlesAsync(section, symbol, timeFrame, from, to, count,
			cancellationToken);
		foreach (var candle in candles)
			await SendCandleAsync(section, symbol, candle, timeFrame, mdMsg.TransactionId,
				cancellationToken);

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private async ValueTask SendLevel1SnapshotAsync(BTSESections section, string symbol,
		long transactionId, CancellationToken cancellationToken)
	{
		var summaries = await GetRestClient(section).GetMarketsAsync(symbol,
			cancellationToken);
		var summary = summaries?.FirstOrDefault(market =>
			market.Symbol.EqualsIgnoreCase(symbol)) ?? GetMarket(section, symbol);
		RegisterMarkets(section, [summary]);

		var message = new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, summary.HighestBid)
		.TryAdd(Level1Fields.BestAskPrice, summary.LowestAsk)
		.TryAdd(Level1Fields.LastTradePrice, summary.Last)
		.TryAdd(Level1Fields.HighPrice, summary.High24Hours)
		.TryAdd(Level1Fields.LowPrice, summary.Low24Hours)
		.TryAdd(Level1Fields.Volume, summary.Size)
		.TryAdd(Level1Fields.Turnover, summary.Volume)
		.TryAdd(Level1Fields.OpenInterest, summary.OpenInterest);

		var prices = await GetRestClient(section).GetPricesAsync(symbol,
			cancellationToken);
		var price = prices?.FirstOrDefault(item => item.Symbol.EqualsIgnoreCase(symbol));
		if (price is not null)
			message
				.TryAdd(Level1Fields.Index, price.IndexPrice)
				.TryAdd(Level1Fields.SettlementPrice, price.MarkPrice);

		await SendOutMessageAsync(message, cancellationToken);
	}

	private async ValueTask<BTSEPublicTrade[]> LoadTradesAsync(BTSESections section,
		string symbol, DateTime? from, DateTime to, int maximum,
		CancellationToken cancellationToken)
	{
		var result = new List<BTSEPublicTrade>();
		if (from is null)
		{
			var recent = await GetRestClient(section).GetTradesAsync(new()
			{
				Symbol = symbol,
				EndTime = to.ToMilliseconds(),
				Count = maximum.Min(1000),
				IsIncludeOld = section == BTSESections.Futures ? false : null,
			}, cancellationToken);
			result.AddRange(recent ?? []);
		}
		else
		{
			var cursor = from.Value;
			while (cursor <= to && result.Count < maximum)
			{
				var end = cursor.AddDays(30).Min(to);
				var page = await GetRestClient(section).GetTradesAsync(new()
				{
					Symbol = symbol,
					StartTime = cursor.ToMilliseconds(),
					EndTime = end.ToMilliseconds(),
					Count = (maximum - result.Count).Min(1000),
					IsIncludeOld = section == BTSESections.Futures
						? cursor < DateTime.UtcNow.AddDays(-7)
						: null,
				}, cancellationToken);
				result.AddRange(page ?? []);
				if (end >= to)
					break;
				cursor = end.AddMilliseconds(1);
			}
		}

		return [.. result
			.Where(trade => trade.Timestamp > 0 &&
				(from is null || trade.Timestamp.FromMilliseconds() >= from.Value) &&
				trade.Timestamp.FromMilliseconds() <= to)
			.GroupBy(static trade => trade.TradeId != 0 ? trade.TradeId : trade.SerialId)
			.Select(static group => group.First())
			.OrderBy(static trade => trade.Timestamp)
			.TakeLast(maximum)];
	}

	private async ValueTask<BTSECandle[]> LoadCandlesAsync(BTSESections section,
		string symbol, TimeSpan timeFrame, DateTime from, DateTime to, int maximum,
		CancellationToken cancellationToken)
	{
		var result = new List<BTSECandle>();
		var cursor = to;
		while (cursor >= from && result.Count < maximum)
		{
			var page = await GetRestClient(section).GetCandlesAsync(new()
			{
				Symbol = symbol,
				Start = from.ToMilliseconds(),
				End = cursor.ToMilliseconds(),
				Resolution = timeFrame.ToResolution(),
			}, cancellationToken);
			if (page is not { Length: > 0 })
				break;
			result.AddRange(page.Where(candle => candle.Timestamp.FromSeconds() >= from &&
				candle.Timestamp.FromSeconds() <= to));
			var earliest = page.Min(static candle => candle.Timestamp).FromSeconds();
			if (earliest <= from || page.Length < 300)
				break;
			cursor = earliest.AddMilliseconds(-1);
		}

		return [.. result.GroupBy(static candle => candle.Timestamp)
			.Select(static group => group.First())
			.OrderBy(static candle => candle.Timestamp)
			.TakeLast(maximum)];
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences,
					new(subscription.Section, "snapshotL1:" + subscription.Symbol));
		if (unsubscribe)
			await GetBookWsClient(subscription.Section).UnsubscribeLevel1Async(
				subscription.Symbol, cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
			if (_depthSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences,
					new(subscription.Section, "update:" + subscription.Symbol + "_0"));
		if (unsubscribe)
			await GetBookWsClient(subscription.Section).UnsubscribeDepthAsync(
				subscription.Symbol, cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
			if (_tickSubscriptions.Remove(transactionId, out subscription))
			{
				var topic = (subscription.Section == BTSESections.Spot
					? "tradeHistoryApi:"
					: "tradeHistoryApiV3:") + subscription.Symbol;
				unsubscribe = ReleaseReference(_streamReferences,
					new(subscription.Section, topic));
			}
		if (unsubscribe)
			await GetGeneralWsClient(subscription.Section).UnsubscribeTradesAsync(
				subscription.Symbol, cancellationToken);
	}

	private async ValueTask OnTradeAsync(BTSESections section, BTSEPublicTrade trade,
		CancellationToken cancellationToken)
	{
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _tickSubscriptions
				.Where(pair => pair.Value.Section == section &&
					pair.Value.Symbol.EqualsIgnoreCase(trade.Symbol))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendTradeAsync(section, trade, id, cancellationToken);
	}

	private async ValueTask OnBookAsync(BTSESections section, BTSEWsBook book,
		CancellationToken cancellationToken)
	{
		if (book?.Symbol.IsEmpty() != false)
			return;

		if (book.Type.EqualsIgnoreCase("snapshotL1"))
		{
			long[] ids;
			using (_sync.EnterScope())
				ids = [.. _level1Subscriptions
					.Where(pair => pair.Value.Section == section &&
						pair.Value.Symbol.EqualsIgnoreCase(book.Symbol))
					.Select(static pair => pair.Key)];
			var bid = book.Bids?.FirstOrDefault();
			var ask = book.Asks?.FirstOrDefault();
			foreach (var id in ids)
				await SendOutMessageAsync(new Level1ChangeMessage
				{
					SecurityId = book.Symbol.ToStockSharp(section),
					ServerTime = book.Timestamp > 0
						? book.Timestamp.FromMilliseconds()
						: CurrentTime,
					OriginalTransactionId = id,
				}
				.TryAdd(Level1Fields.BestBidPrice, bid?.Price)
				.TryAdd(Level1Fields.BestBidVolume, bid?.Size)
				.TryAdd(Level1Fields.BestAskPrice, ask?.Price)
				.TryAdd(Level1Fields.BestAskVolume, ask?.Size), cancellationToken);
			return;
		}

		await ProcessDepthAsync(section, book, cancellationToken);
	}

	private async ValueTask ProcessDepthAsync(BTSESections section, BTSEWsBook book,
		CancellationToken cancellationToken)
	{
		await _depthProcessing.WaitAsync(cancellationToken);
		try
		{
			(long Id, DepthSubscription Subscription)[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _depthSubscriptions
					.Where(pair => pair.Value.Section == section &&
						pair.Value.Symbol.EqualsIgnoreCase(book.Symbol))
					.Select(static pair => (pair.Key, pair.Value))];

			var isSnapshot = book.Type.EqualsIgnoreCase("snapshot");
			var isGap = !isSnapshot && subscriptions.Any(item =>
				item.Subscription.LastSequence == 0 ||
				book.PreviousSequenceNumber != item.Subscription.LastSequence);
			if (isGap)
			{
				foreach (var item in subscriptions)
					item.Subscription.LastSequence = 0;
				await GetBookWsClient(section).ResubscribeDepthAsync(book.Symbol,
					cancellationToken);
				return;
			}

			foreach (var item in subscriptions)
			{
				item.Subscription.LastSequence = book.SequenceNumber;
				await SendOutMessageAsync(new QuoteChangeMessage
				{
					SecurityId = book.Symbol.ToStockSharp(section),
					ServerTime = book.Timestamp > 0
						? book.Timestamp.FromMilliseconds()
						: CurrentTime,
					OriginalTransactionId = item.Id,
					State = isSnapshot
						? QuoteChangeStates.SnapshotComplete
						: QuoteChangeStates.Increment,
					SeqNum = book.SequenceNumber,
					Bids = ToQuotes(book.Bids, false,
						isSnapshot ? item.Subscription.Depth : int.MaxValue),
					Asks = ToQuotes(book.Asks, true,
						isSnapshot ? item.Subscription.Depth : int.MaxValue),
				}, cancellationToken);
			}
		}
		finally
		{
			_depthProcessing.Release();
		}
	}

	private ValueTask SendTradeAsync(BTSESections section, BTSEPublicTrade trade,
		long transactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = trade.Symbol.ToStockSharp(section),
			ServerTime = trade.Timestamp > 0
				? trade.Timestamp.FromMilliseconds()
				: CurrentTime,
			OriginalTransactionId = transactionId,
			TradeId = trade.TradeId != 0 ? trade.TradeId : trade.SerialId,
			TradePrice = trade.Price,
			TradeVolume = trade.Size,
			OriginSide = trade.Side.ToStockSharpSide(),
		}, cancellationToken);

	private ValueTask SendDepthSnapshotAsync(BTSESections section, BTSEOrderBook snapshot,
		long transactionId, int depth, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = snapshot.Symbol.ToStockSharp(section),
			ServerTime = snapshot.Timestamp > 0
				? snapshot.Timestamp.FromMilliseconds()
				: CurrentTime,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = ToQuotes(snapshot.Bids, false, depth),
			Asks = ToQuotes(snapshot.Asks, true, depth),
		}, cancellationToken);

	private ValueTask SendCandleAsync(BTSESections section, string symbol,
		BTSECandle candle, TimeSpan timeFrame, long transactionId,
		CancellationToken cancellationToken)
	{
		var openTime = candle.Timestamp.FromSeconds();
		var closeTime = openTime + timeFrame;
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = symbol.ToStockSharp(section),
			OpenTime = openTime,
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

	private static QuoteChange[] ToQuotes(BTSEPriceLevel[] levels, bool isAscending,
		int depth)
	{
		var ordered = isAscending
			? (levels ?? []).OrderBy(static level => level.Price)
			: (levels ?? []).OrderByDescending(static level => level.Price);
		return [.. ordered.Take(depth)
			.Select(static level => new QuoteChange(level.Price, level.Size))];
	}

	private static QuoteChange[] ToQuotes(BTSEQuote[] levels, bool isAscending, int depth)
	{
		var ordered = isAscending
			? (levels ?? []).OrderBy(static level => level.Price)
			: (levels ?? []).OrderByDescending(static level => level.Price);
		return [.. ordered.Take(depth)
			.Select(static level => new QuoteChange(level.Price, level.Size))];
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
