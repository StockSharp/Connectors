namespace StockSharp.CoinCatch;

public partial class CoinCatchMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var requestedSymbol = lookupMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(lookupMsg.SecurityId).SecurityCode;
		CoinCatchSymbol[] markets;
		using (_sync.EnterScope())
			markets = [.. _marketsBySecurity.Values];

		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var market in markets.OrderBy(
			static value => value.SecurityCode,
			StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					ProductType.ToBoardCode()))
				continue;
			if (!requestedSymbol.IsEmpty() &&
				!requestedSymbol.EqualsIgnoreCase(market.SecurityCode))
				continue;
			var security = CreateSecurity(
				market, lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			if (skip-- > 0)
				continue;
			await SendOutMessageAsync(security, cancellationToken);
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = security.SecurityId,
				ServerTime = CurrentTime,
				OriginalTransactionId = lookupMsg.TransactionId,
			}.TryAdd(Level1Fields.State,
				market.Status.ToSecurityState()), cancellationToken);
			if (--left <= 0)
				break;
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeLevel1Async(mdMsg.OriginalTransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"CoinCatch does not expose historical Level1 events.");

		var market = GetMarket(mdMsg.SecurityId);
		await SendLevel1SnapshotAsync(market, mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}

		var key = new StreamKey("ticker", market.Symbol);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				NativeSymbol = market.Symbol,
				SecurityCode = market.SecurityCode,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await WsClient.SubscribeTickerAsync(market.Symbol,
					cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeLevel1Async(mdMsg.TransactionId,
				cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeDepthAsync(mdMsg.OriginalTransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"CoinCatch does not expose historical order-book events.");

		var market = GetMarket(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? 150).Min(150).Max(1);
		var snapshot = await RestClient.GetOrderBookAsync(
			market.Symbol, depth, cancellationToken);
		await SendDepthAsync(market.SecurityCode, snapshot,
			mdMsg.TransactionId, depth, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}

		var channel = depth <= 5 ? "books5" : "books15";
		var key = new StreamKey(channel, market.Symbol);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				NativeSymbol = market.Symbol,
				SecurityCode = market.SecurityCode,
				Depth = depth,
				Channel = channel,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await WsClient.SubscribeOrderBookAsync(
					market.Symbol, depth, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeDepthAsync(mdMsg.TransactionId,
				cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeTicksAsync(mdMsg.OriginalTransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}

		var market = GetMarket(mdMsg.SecurityId);
		var from = mdMsg.From?.ToUtc();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUtc();
		if (from < to - TimeSpan.FromDays(7))
			from = to - TimeSpan.FromDays(7);
		var count = (mdMsg.Count ?? (from is null ? 100 : 1000))
			.Min(1000).Max(1).To<int>();
		var trades = await RestClient.GetTradesAsync(
			market.Symbol, count, from, to, cancellationToken);
		foreach (var trade in (trades ?? [])
			.Where(trade => trade.Timestamp > 0 &&
				(from is null ||
					trade.Timestamp.FromCoinCatchTime() >= from.Value) &&
				trade.Timestamp.FromCoinCatchTime() <= to)
			.OrderBy(static trade => trade.Timestamp)
			.TakeLast(count))
		{
			if (!AddTrade(market.Symbol, trade.TradeId, false))
				continue;
			await SendPublicTradeAsync(market.SecurityCode, trade,
				mdMsg.TransactionId, cancellationToken);
		}

		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}

		var key = new StreamKey("trade", market.Symbol);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				NativeSymbol = market.Symbol,
				SecurityCode = market.SecurityCode,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await WsClient.SubscribeTradesAsync(market.Symbol,
					cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeTicksAsync(mdMsg.TransactionId,
				cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeCandlesAsync(mdMsg.OriginalTransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}

		var market = GetMarket(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		var granularity = timeFrame.ToCoinCatchGranularity();
		var interval = timeFrame.ToCoinCatchWebSocketChannel();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUtc();
		var requested = mdMsg.Count?.Min(10000).Max(1).To<int>() ??
			GetCandleCount(mdMsg, timeFrame, to);
		var from = mdMsg.From?.ToUtc() ??
			to - TimeSpan.FromTicks(timeFrame.Ticks * requested);
		await SendCandleHistoryAsync(market, timeFrame, granularity,
			from, to, requested, mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg,
				cancellationToken);
			return;
		}

		var key = new StreamKey(interval, market.Symbol);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				NativeSymbol = market.Symbol,
				SecurityCode = market.SecurityCode,
				TimeFrame = timeFrame,
				Interval = interval,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await WsClient.SubscribeCandlesAsync(
					market.Symbol, interval, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeCandlesAsync(mdMsg.TransactionId,
				cancellationToken);
			throw;
		}
	}

	private SecurityMessage CreateSecurity(CoinCatchSymbol market,
		long originalTransactionId)
		=> new()
		{
			SecurityId = market.ToStockSharp(ProductType),
			Name = market.SecurityCode,
			ShortName = market.SecurityCode,
			SecurityType = ProductType.IsFutures()
				? SecurityTypes.Future
				: SecurityTypes.CryptoCurrency,
			Currency = market.QuoteCoin.ToCurrency(),
			PriceStep = market.PriceStep,
			VolumeStep = market.VolumeStep,
			MinVolume = market.MinimumTradeAmount,
			MaxVolume = market.MaximumTradeAmount,
			OriginalTransactionId = originalTransactionId,
		};

	private async ValueTask SendLevel1SnapshotAsync(CoinCatchSymbol market,
		long transactionId, CancellationToken cancellationToken)
	{
		var ticker = await RestClient.GetTickerAsync(
			market.Symbol, cancellationToken);
		if (ticker is null)
			throw new InvalidDataException(
				$"CoinCatch returned no ticker for '{market.Symbol}'.");
		await SendOutMessageAsync(CreateLevel1Message(
			market, ticker, transactionId), cancellationToken);
	}

	private Level1ChangeMessage CreateLevel1Message(CoinCatchSymbol market,
		CoinCatchTicker ticker, long transactionId)
		=> new Level1ChangeMessage
		{
			SecurityId = market.ToStockSharp(ProductType),
			ServerTime = ticker.Timestamp > 0
				? ticker.Timestamp.FromCoinCatchTime()
				: CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, ticker.BidPrice)
		.TryAdd(Level1Fields.BestBidVolume, ticker.BidSize)
		.TryAdd(Level1Fields.BestAskPrice, ticker.AskPrice)
		.TryAdd(Level1Fields.BestAskVolume, ticker.AskSize)
		.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice)
		.TryAdd(Level1Fields.OpenPrice, ticker.OpenPrice)
		.TryAdd(Level1Fields.HighPrice, ticker.HighPrice)
		.TryAdd(Level1Fields.LowPrice, ticker.LowPrice)
		.TryAdd(Level1Fields.Volume, ticker.BaseVolume)
		.TryAdd(Level1Fields.Change, ticker.Change)
		.TryAdd(Level1Fields.Index, ticker.IndexPrice)
		.TryAdd(Level1Fields.OpenInterest, ticker.OpenInterest)
		.TryAdd(Level1Fields.State, market.Status.ToSecurityState());

	private async ValueTask SendCandleHistoryAsync(CoinCatchSymbol market,
		TimeSpan timeFrame, string granularity, DateTime from, DateTime to,
		int requested, long transactionId,
		CancellationToken cancellationToken)
	{
		var left = requested;
		var cursor = from;
		while (cursor <= to && left > 0)
		{
			var limit = left.Min(1000);
			var batchTo = cursor +
				TimeSpan.FromTicks(timeFrame.Ticks * (limit - 1L));
			if (batchTo > to)
				batchTo = to;
			var candles = await RestClient.GetCandlesAsync(
				market.Symbol, granularity, cursor, batchTo, limit,
				cancellationToken);
			var batch = (candles ?? [])
				.Where(candle =>
					candle.Timestamp.FromCoinCatchTime() >= cursor &&
					candle.Timestamp.FromCoinCatchTime() <= batchTo)
				.OrderBy(static candle => candle.Timestamp)
				.Take(limit)
				.ToArray();
			foreach (var candle in batch)
			{
				await SendCandleAsync(
					market.SecurityCode, timeFrame, candle,
					transactionId, cancellationToken);
				left--;
			}
			if (left <= 0 || batchTo >= to)
				break;
			var next = batch.Length > 0
				? batch[^1].Timestamp.FromCoinCatchTime() + timeFrame
				: batchTo + timeFrame;
			if (next <= cursor)
				break;
			cursor = next;
		}
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(
				transactionId, out subscription))
				release = ReleaseReference(_streamReferences,
					new("ticker", subscription.NativeSymbol));
		if (release && _wsClient is not null)
			await _wsClient.UnsubscribeTickerAsync(
				subscription.NativeSymbol, cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_depthSubscriptions.Remove(
				transactionId, out subscription))
				release = ReleaseReference(_streamReferences,
					new(subscription.Channel,
						subscription.NativeSymbol));
		if (release && _wsClient is not null)
			await _wsClient.UnsubscribeOrderBookAsync(
				subscription.NativeSymbol, subscription.Depth,
				cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_tickSubscriptions.Remove(
				transactionId, out subscription))
				release = ReleaseReference(_streamReferences,
					new("trade", subscription.NativeSymbol));
		if (release && _wsClient is not null)
			await _wsClient.UnsubscribeTradesAsync(
				subscription.NativeSymbol, cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		CandleSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_candleSubscriptions.Remove(
				transactionId, out subscription))
				release = ReleaseReference(_streamReferences,
					new(subscription.Interval,
						subscription.NativeSymbol));
		if (release && _wsClient is not null)
			await _wsClient.UnsubscribeCandlesAsync(
				subscription.NativeSymbol, subscription.Interval,
				cancellationToken);
	}

	private async ValueTask OnWebSocketTickerAsync(CoinCatchTicker ticker,
		CancellationToken cancellationToken)
	{
		if (ticker?.Symbol.IsEmpty() != false)
			return;
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions.Where(pair =>
				IsSameMarket(
					pair.Value.NativeSymbol, ticker.Symbol))];
		var market = GetMarket(ticker.Symbol);
		if (market is null)
			return;
		foreach (var pair in subscriptions)
			await SendOutMessageAsync(CreateLevel1Message(
				market, ticker, pair.Key), cancellationToken);
	}

	private async ValueTask OnWebSocketOrderBookAsync(
		CoinCatchOrderBook book, CancellationToken cancellationToken)
	{
		if (book?.Symbol.IsEmpty() != false)
			return;
		KeyValuePair<long, DepthSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions.Where(pair =>
				IsSameMarket(
					pair.Value.NativeSymbol, book.Symbol))];
		foreach (var pair in subscriptions)
			await SendDepthAsync(pair.Value.SecurityCode, book, pair.Key,
				pair.Value.Depth, cancellationToken);
	}

	private async ValueTask OnWebSocketTradeAsync(CoinCatchTrade trade,
		CancellationToken cancellationToken)
	{
		if (trade?.Symbol.IsEmpty() != false)
			return;
		var tradeKey = trade.TradeId.IsEmpty()
			? string.Join(":",
				trade.Timestamp.ToString(CultureInfo.InvariantCulture),
				trade.Price.ToWire(),
				trade.Size.ToWire())
			: trade.TradeId;
		if (!AddTrade(trade.Symbol, tradeKey, false))
			return;
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _tickSubscriptions.Where(pair =>
				IsSameMarket(
					pair.Value.NativeSymbol, trade.Symbol))];
		foreach (var pair in subscriptions)
			await SendPublicTradeAsync(pair.Value.SecurityCode, trade,
				pair.Key, cancellationToken);
	}

	private async ValueTask OnWebSocketCandleAsync(string symbol,
		string interval, CoinCatchCandle candle,
		CancellationToken cancellationToken)
	{
		KeyValuePair<long, CandleSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _candleSubscriptions.Where(pair =>
				IsSameMarket(pair.Value.NativeSymbol, symbol) &&
				pair.Value.Interval.EqualsIgnoreCase(interval))];
		foreach (var pair in subscriptions)
			await SendCandleAsync(
				pair.Value.SecurityCode, pair.Value.TimeFrame, candle,
				pair.Key, cancellationToken);
	}

	private ValueTask SendDepthAsync(string securityCode,
		CoinCatchOrderBook book, long transactionId, int depth,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = ToSecurityId(securityCode),
			ServerTime = book.Timestamp > 0
				? book.Timestamp.FromCoinCatchTime()
				: CurrentTime,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = ToQuotes(book.Bids, false, depth),
			Asks = ToQuotes(book.Asks, true, depth),
		}, cancellationToken);

	private ValueTask SendPublicTradeAsync(string securityCode,
		CoinCatchTrade trade, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = ToSecurityId(securityCode),
			ServerTime = trade.Timestamp > 0
				? trade.Timestamp.FromCoinCatchTime()
				: CurrentTime,
			OriginalTransactionId = transactionId,
			TradeStringId = trade.TradeId,
			TradePrice = trade.Price,
			TradeVolume = trade.Size.Abs(),
			OriginSide = trade.Side.ToSide(),
		}, cancellationToken);

	private ValueTask SendCandleAsync(string securityCode,
		TimeSpan timeFrame, CoinCatchCandle candle, long transactionId,
		CancellationToken cancellationToken)
	{
		var openTime = candle.Timestamp.FromCoinCatchTime();
		var closeTime = openTime + timeFrame;
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = ToSecurityId(securityCode),
			OpenTime = openTime,
			CloseTime = closeTime,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.BaseVolume,
			TotalPrice = candle.QuoteVolume,
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = closeTime <= CurrentTime
				? CandleStates.Finished
				: CandleStates.Active,
		}, cancellationToken);
	}

	private static QuoteChange[] ToQuotes(CoinCatchQuote[] levels,
		bool isAsk, int depth)
	{
		var grouped = (levels ?? [])
			.Where(static level =>
				level.Price > 0 && level.Size > 0)
			.GroupBy(static level => level.Price)
			.Select(static group => new QuoteChange(
				group.Key, group.Sum(static level => level.Size)));
		return [.. (isAsk
			? grouped.OrderBy(static quote => quote.Price)
			: grouped.OrderByDescending(static quote => quote.Price))
			.Take(depth)];
	}

	private static int GetCandleCount(MarketDataMessage message,
		TimeSpan timeFrame, DateTime to)
	{
		if (message.From is not DateTime from)
			return 1000;
		var count = (long)Math.Ceiling(
			(to - from.ToUtc()).Ticks /
			(double)timeFrame.Ticks) + 1;
		return count.Max(1).Min(10000).To<int>();
	}

	private SecurityId ToSecurityId(string securityCode)
		=> new()
		{
			SecurityCode = securityCode,
			BoardCode = ProductType.ToBoardCode(),
		};

	private static bool IsSameMarket(string left, string right)
		=> !left.IsEmpty() && !right.IsEmpty() &&
			left.ToCoinCatchWebSocketSymbol().EqualsIgnoreCase(
				right.ToCoinCatchWebSocketSymbol());

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
