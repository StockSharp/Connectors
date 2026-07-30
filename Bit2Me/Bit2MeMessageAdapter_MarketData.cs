namespace StockSharp.Bit2Me;

public partial class Bit2MeMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);

		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var requestedSymbol = lookupMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(lookupMsg.SecurityId).Symbol;
		Bit2MeMarket[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values];

		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var market in markets.OrderBy(static value => value.Symbol,
			StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.Bit2Me))
				continue;
			if (!requestedSymbol.IsEmpty() &&
				!requestedSymbol.EqualsIgnoreCase(market.Symbol))
				continue;
			var security = CreateSecurity(market, lookupMsg.TransactionId);
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
			}.TryAdd(Level1Fields.State, market.Status.ToStockSharp()),
				cancellationToken);
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
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"Bit2Me does not expose historical Level1 events.");

		var symbol = GetMarket(mdMsg.SecurityId).Symbol;
		await SendLevel1SnapshotAsync(symbol, mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var bookKey = new StreamKey("order-book", symbol);
		var tradesKey = new StreamKey("public-trades", symbol);
		bool subscribeBook;
		bool subscribeTrades;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId,
				new() { Symbol = symbol });
			subscribeBook = AddReference(_streamReferences, bookKey);
			subscribeTrades = AddReference(_streamReferences, tradesKey);
		}
		try
		{
			if (subscribeBook)
				await WsClient.SubscribeOrderBookAsync(symbol,
					cancellationToken);
			if (subscribeTrades)
				await WsClient.SubscribeTradesAsync(symbol,
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
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"Bit2Me does not expose historical order-book events.");

		var symbol = GetMarket(mdMsg.SecurityId).Symbol;
		var depth = (mdMsg.MaxDepth ?? 100).Min(100).Max(1);
		var snapshot = await RestClient.GetOrderBookAsync(symbol,
			cancellationToken);
		await SendDepthAsync(symbol, snapshot, mdMsg.TransactionId, depth,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var key = new StreamKey("order-book", symbol);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				Depth = depth,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await WsClient.SubscribeOrderBookAsync(symbol,
					cancellationToken);
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
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var symbol = GetMarket(mdMsg.SecurityId).Symbol;
		var count = (mdMsg.Count ?? 50).Min(50).Max(1).To<int>();
		var from = mdMsg.From?.ToUniversalTime();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var trades = await RestClient.GetPublicTradesAsync(symbol, count,
			cancellationToken);

		foreach (var trade in (trades ?? []).Where(trade =>
		{
			var time = trade.Timestamp > 0
				? trade.Timestamp.FromMilliseconds()
				: DateTime.MinValue;
			return time != DateTime.MinValue &&
				(from is null || time >= from.Value) && time <= to;
		}).OrderBy(static trade => trade.Timestamp).TakeLast(count))
			await SendPublicTradeAsync(symbol, trade, mdMsg.TransactionId,
				cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var key = new StreamKey("public-trades", symbol);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId,
				new() { Symbol = symbol });
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await WsClient.SubscribeTradesAsync(symbol,
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
			return;
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var symbol = GetMarket(mdMsg.SecurityId).Symbol;
		var timeFrame = mdMsg.GetTimeFrame();
		if (!Bit2MeExtensions.TimeFrames.Contains(timeFrame))
			throw new NotSupportedException(
				$"Bit2Me does not support the {timeFrame} candle interval.");

		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var requested = mdMsg.Count?.Min(1000).Max(1).To<int>() ??
			GetCandleCount(mdMsg, timeFrame, to);
		var from = mdMsg.From?.ToUniversalTime() ??
			to - TimeSpan.FromTicks(timeFrame.Ticks * requested);
		var candles = await RestClient.GetCandlesAsync(new()
		{
			Symbol = symbol,
			Interval = timeFrame.ToBit2MeInterval(),
			StartTime = from.ToMilliseconds(),
			EndTime = to.ToMilliseconds(),
			Limit = requested,
		}, cancellationToken);

		foreach (var candle in (candles ?? [])
			.OrderBy(static candle => candle.Timestamp)
			.TakeLast(requested))
			await SendCandleAsync(symbol, candle, timeFrame,
				mdMsg.TransactionId, cancellationToken);

		await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
	}

	private SecurityMessage CreateSecurity(Bit2MeMarket market,
		long originalTransactionId)
	{
		var (baseCurrency, quoteCurrency) = market.Symbol.SplitSymbol();
		return new()
		{
			SecurityId = market.Symbol.ToStockSharp(),
			Name = $"{baseCurrency}/{quoteCurrency}",
			ShortName = $"{baseCurrency}/{quoteCurrency}",
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = quoteCurrency.ToCurrency(),
			PriceStep = market.TickSize > 0
				? market.TickSize
				: Bit2MeExtensions.GetStep(market.PricePrecision),
			VolumeStep = Bit2MeExtensions.GetStep(market.AmountPrecision),
			MinVolume = market.MinimumAmount > 0
				? market.MinimumAmount
				: null,
			MaxVolume = market.MaximumAmount > 0
				? market.MaximumAmount
				: null,
			OriginalTransactionId = originalTransactionId,
		};
	}

	private async ValueTask SendLevel1SnapshotAsync(string symbol,
		long transactionId, CancellationToken cancellationToken)
	{
		var tickers = await RestClient.GetTickersAsync(symbol,
			cancellationToken);
		var ticker = tickers?.FirstOrDefault(value =>
			value.Symbol.EqualsIgnoreCase(symbol)) ?? tickers?.FirstOrDefault();
		if (ticker is null)
			throw new InvalidDataException(
				$"Bit2Me returned no ticker for '{symbol}'.");
		await SendOutMessageAsync(CreateLevel1Message(ticker, transactionId),
			cancellationToken);
	}

	private Level1ChangeMessage CreateLevel1Message(Bit2MeTicker ticker,
		long transactionId)
	{
		Bit2MeMarketStatuses? status = null;
		using (_sync.EnterScope())
			if (_markets.TryGetValue(ticker.Symbol.NormalizeSymbol(),
				out var market))
				status = market.Status;
		return new Level1ChangeMessage
		{
			SecurityId = ticker.Symbol.ToStockSharp(),
			ServerTime = ticker.Timestamp > 0
				? ticker.Timestamp.FromMilliseconds()
				: CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, ticker.Bid)
		.TryAdd(Level1Fields.BestAskPrice, ticker.Ask)
		.TryAdd(Level1Fields.LastTradePrice, ticker.Close)
		.TryAdd(Level1Fields.OpenPrice, ticker.Open)
		.TryAdd(Level1Fields.HighPrice, ticker.High)
		.TryAdd(Level1Fields.LowPrice, ticker.Low)
		.TryAdd(Level1Fields.Volume, ticker.BaseVolume)
		.TryAdd(Level1Fields.Turnover, ticker.QuoteVolume)
		.TryAdd(Level1Fields.Change, ticker.Percentage)
		.TryAdd(Level1Fields.State, status?.ToStockSharp());
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var releaseBook = false;
		var releaseTrades = false;
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(transactionId, out subscription))
			{
				releaseBook = ReleaseReference(_streamReferences,
					new("order-book", subscription.Symbol));
				releaseTrades = ReleaseReference(_streamReferences,
					new("public-trades", subscription.Symbol));
			}
		if (subscription is null || _wsClient is null)
			return;
		if (releaseBook)
			await _wsClient.UnsubscribeOrderBookAsync(subscription.Symbol,
				cancellationToken);
		if (releaseTrades)
			await _wsClient.UnsubscribeTradesAsync(subscription.Symbol,
				cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_depthSubscriptions.Remove(transactionId, out subscription))
				release = ReleaseReference(_streamReferences,
					new("order-book", subscription.Symbol));
		if (release && _wsClient is not null)
			await _wsClient.UnsubscribeOrderBookAsync(subscription.Symbol,
				cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_tickSubscriptions.Remove(transactionId, out subscription))
				release = ReleaseReference(_streamReferences,
					new("public-trades", subscription.Symbol));
		if (release && _wsClient is not null)
			await _wsClient.UnsubscribeTradesAsync(subscription.Symbol,
				cancellationToken);
	}

	private async ValueTask OnWebSocketOrderBookAsync(string symbol,
		Bit2MeOrderBook book, CancellationToken cancellationToken)
	{
		if (book is null)
			return;
		symbol = symbol.NormalizeSymbol();
		book.Symbol = symbol;
		KeyValuePair<long, DepthSubscription>[] depthSubscriptions;
		KeyValuePair<long, MarketSubscription>[] level1Subscriptions;
		using (_sync.EnterScope())
		{
			depthSubscriptions = [.. _depthSubscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(symbol))];
			level1Subscriptions = [.. _level1Subscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(symbol))];
		}

		foreach (var pair in depthSubscriptions)
			await SendDepthAsync(symbol, book, pair.Key, pair.Value.Depth,
				cancellationToken);

		var bids = ToQuotes(book.Bids, false, 1);
		var asks = ToQuotes(book.Asks, true, 1);
		var serverTime = book.Timestamp > 0
			? book.Timestamp.FromMilliseconds()
			: CurrentTime;

		foreach (var pair in level1Subscriptions)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = symbol.ToStockSharp(),
				ServerTime = serverTime,
				OriginalTransactionId = pair.Key,
			}
			.TryAdd(Level1Fields.BestBidPrice,
				bids.Length == 0 ? null : bids[0].Price)
			.TryAdd(Level1Fields.BestBidVolume,
				bids.Length == 0 ? null : bids[0].Volume)
			.TryAdd(Level1Fields.BestAskPrice,
				asks.Length == 0 ? null : asks[0].Price)
			.TryAdd(Level1Fields.BestAskVolume,
				asks.Length == 0 ? null : asks[0].Volume), cancellationToken);
	}

	private async ValueTask OnWebSocketTradeAsync(string symbol,
		Bit2MeWsTrade trade, CancellationToken cancellationToken)
	{
		if (trade is null)
			return;
		symbol = symbol.NormalizeSymbol();
		if (!AddPublicTrade(symbol, trade))
			return;
		KeyValuePair<long, MarketSubscription>[] tickSubscriptions;
		KeyValuePair<long, MarketSubscription>[] level1Subscriptions;
		using (_sync.EnterScope())
		{
			tickSubscriptions = [.. _tickSubscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(symbol))];
			level1Subscriptions = [.. _level1Subscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(symbol))];
		}
		var serverTime = trade.Timestamp > 0
			? trade.Timestamp.FromMilliseconds()
			: CurrentTime;
		var tradeId = CreatePublicTradeId(trade);

		foreach (var pair in tickSubscriptions)
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = symbol.ToStockSharp(),
				ServerTime = serverTime,
				OriginalTransactionId = pair.Key,
				TradeStringId = tradeId,
				TradePrice = trade.Price,
				TradeVolume = trade.Amount,
				OriginSide = trade.Side.ToStockSharp(),
			}, cancellationToken);

		foreach (var pair in level1Subscriptions)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = symbol.ToStockSharp(),
				ServerTime = serverTime,
				OriginalTransactionId = pair.Key,
			}.TryAdd(Level1Fields.LastTradePrice, trade.Price),
				cancellationToken);
	}

	private ValueTask SendDepthAsync(string symbol, Bit2MeOrderBook book,
		long transactionId, int depth, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(),
			ServerTime = book.Timestamp > 0
				? book.Timestamp.FromMilliseconds()
				: CurrentTime,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			SeqNum = book.Nonce,
			Bids = ToQuotes(book.Bids, false, depth),
			Asks = ToQuotes(book.Asks, true, depth),
		}, cancellationToken);

	private ValueTask SendPublicTradeAsync(string symbol,
		Bit2MePublicTrade trade, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = symbol.ToStockSharp(),
			ServerTime = trade.Timestamp > 0
				? trade.Timestamp.FromMilliseconds()
				: CurrentTime,
			OriginalTransactionId = transactionId,
			TradeStringId =
				$"{trade.Timestamp}-{trade.Side}-{trade.Price}-{trade.Amount}",
			TradePrice = trade.Price,
			TradeVolume = trade.Amount,
			OriginSide = trade.Side.ToStockSharp(),
		}, cancellationToken);

	private ValueTask SendCandleAsync(string symbol, Bit2MeCandle candle,
		TimeSpan timeFrame, long transactionId,
		CancellationToken cancellationToken)
	{
		var openTime = candle.Timestamp.FromMilliseconds();
		var closeTime = openTime + timeFrame;
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = symbol.ToStockSharp(),
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

	private static QuoteChange[] ToQuotes(Bit2MePriceLevel[] levels,
		bool isAsk, int depth)
	{
		var grouped = (levels ?? [])
			.Where(static level =>
				level is not null && level.Price > 0 && level.Volume > 0)
			.GroupBy(static level => level.Price)
			.Select(static group =>
				new QuoteChange(group.Key,
					group.Sum(static level => level.Volume)));
		return [.. (isAsk
			? grouped.OrderBy(static quote => quote.Price)
			: grouped.OrderByDescending(static quote => quote.Price))
			.Take(depth)];
	}

	private static string CreatePublicTradeId(Bit2MeWsTrade trade)
		=> $"{trade.Timestamp}-{trade.Side}-{trade.Price}-{trade.Amount}";

	private static int GetCandleCount(MarketDataMessage message,
		TimeSpan timeFrame, DateTime to)
	{
		if (message.From is not DateTime from)
			return 1000;
		var count = (long)Math.Ceiling(
			(to - from.ToUniversalTime()).Ticks /
			(double)timeFrame.Ticks) + 1;
		return count.Max(1).Min(1000).To<int>();
	}

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
