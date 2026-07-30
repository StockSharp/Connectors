namespace StockSharp.BtcTurk;

public partial class BtcTurkMessageAdapter
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
			: GetMarket(lookupMsg.SecurityId).SecurityCode;
		BtcTurkMarket[] markets;
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
					BoardCodes.BtcTurk))
				continue;
			if (!requestedSymbol.IsEmpty() &&
				!requestedSymbol.EqualsIgnoreCase(market.SecurityCode))
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
				"BtcTurk does not expose historical Level1 events.");

		var market = GetMarket(mdMsg.SecurityId);
		await SendLevel1SnapshotAsync(market, mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var key = new StreamKey("ticker", market.NativeSymbol);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				NativeSymbol = market.NativeSymbol,
				SecurityCode = market.SecurityCode,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await WsClient.SubscribeTickerAsync(market.NativeSymbol,
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
				"BtcTurk does not expose historical order-book events.");

		var market = GetMarket(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? 100).Min(100).Max(1);
		var snapshot = await RestClient.GetOrderBookAsync(market.NativeSymbol,
			depth, cancellationToken);
		await SendDepthAsync(market.SecurityCode, snapshot,
			mdMsg.TransactionId, depth, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var key = new StreamKey("orderbook", market.NativeSymbol);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				NativeSymbol = market.NativeSymbol,
				SecurityCode = market.SecurityCode,
				Depth = depth,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await WsClient.SubscribeOrderBookAsync(market.NativeSymbol,
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

		var market = GetMarket(mdMsg.SecurityId);
		var count = (mdMsg.Count ?? 50).Min(50).Max(1).To<int>();
		var from = mdMsg.From?.ToUtc();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUtc();
		var trades = await RestClient.GetPublicTradesAsync(
			market.NativeSymbol, count, cancellationToken);
		var snapshotIds = new HashSet<string>(
			StringComparer.OrdinalIgnoreCase);

		foreach (var trade in (trades ?? []).Where(trade =>
		{
			var time = trade.Timestamp > 0
				? trade.Timestamp.FromUnixMilliseconds()
				: DateTime.MinValue;
			return time != DateTime.MinValue &&
				(from is null || time >= from.Value) && time <= to;
		}).OrderBy(static trade => trade.Timestamp).TakeLast(count))
		{
			if (trade.Id.IsEmpty() || !snapshotIds.Add(trade.Id))
				continue;
			_ = AddPublicTrade(market.NativeSymbol, trade.Id);
			await SendPublicTradeAsync(market.SecurityCode, trade,
				mdMsg.TransactionId, cancellationToken);
		}

		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var key = new StreamKey("trade", market.NativeSymbol);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				NativeSymbol = market.NativeSymbol,
				SecurityCode = market.SecurityCode,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await WsClient.SubscribeTradesAsync(market.NativeSymbol,
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

		var market = GetMarket(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		if (!BtcTurkExtensions.TimeFrames.Contains(timeFrame))
			throw new NotSupportedException(
				$"BtcTurk does not support the {timeFrame} candle interval.");

		var to = (mdMsg.To ?? DateTime.UtcNow).ToUtc();
		var requested = mdMsg.Count?.Min(5000).Max(1).To<int>() ??
			GetCandleCount(mdMsg, timeFrame, to);
		var from = mdMsg.From?.ToUtc() ??
			to - TimeSpan.FromTicks(timeFrame.Ticks * requested);
		var candles = await RestClient.GetKlinesAsync(new()
		{
			Symbol = market.NativeSymbol,
			Resolution = timeFrame.ToBtcTurkResolution(),
			From = from.ToUnixSeconds(),
			To = to.ToUnixSeconds(),
		}, cancellationToken);
		await SendCandlesAsync(market.SecurityCode, candles, timeFrame,
			mdMsg.TransactionId, requested, cancellationToken);
		await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
	}

	private SecurityMessage CreateSecurity(BtcTurkMarket market,
		long originalTransactionId)
	{
		var filter = market.PriceFilter;
		return new()
		{
			SecurityId = market.SecurityCode.ToStockSharp(),
			Name = market.SecurityCode,
			ShortName = market.SecurityCode,
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = market.Denominator.ToCurrency(),
			PriceStep = filter?.TickSize is > 0
				? filter.TickSize
				: BtcTurkExtensions.GetStep(market.DenominatorScale),
			VolumeStep = BtcTurkExtensions.GetStep(market.NumeratorScale),
			MinVolume = filter?.MinimumAmount is > 0
				? filter.MinimumAmount
				: null,
			MaxVolume = filter?.MaximumAmount is > 0
				? filter.MaximumAmount
				: market.MaximumOrderAmount,
			OriginalTransactionId = originalTransactionId,
		};
	}

	private async ValueTask SendLevel1SnapshotAsync(BtcTurkMarket market,
		long transactionId, CancellationToken cancellationToken)
	{
		var tickers = await RestClient.GetTickersAsync(market.NativeSymbol,
			cancellationToken);
		var ticker = tickers?.FirstOrDefault(value =>
			value.Pair.EqualsIgnoreCase(market.NativeSymbol)) ??
			tickers?.FirstOrDefault();
		if (ticker is null)
			throw new InvalidDataException(
				$"BtcTurk returned no ticker for '{market.NativeSymbol}'.");
		await SendOutMessageAsync(CreateLevel1Message(market, ticker,
			transactionId), cancellationToken);
	}

	private Level1ChangeMessage CreateLevel1Message(BtcTurkMarket market,
		BtcTurkTicker ticker, long transactionId)
		=> new Level1ChangeMessage
		{
			SecurityId = market.SecurityCode.ToStockSharp(),
			ServerTime = ticker.Timestamp > 0
				? ticker.Timestamp.FromUnixMilliseconds()
				: CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, ticker.Bid)
		.TryAdd(Level1Fields.BestAskPrice, ticker.Ask)
		.TryAdd(Level1Fields.LastTradePrice, ticker.Last)
		.TryAdd(Level1Fields.OpenPrice, ticker.Open)
		.TryAdd(Level1Fields.HighPrice, ticker.High)
		.TryAdd(Level1Fields.LowPrice, ticker.Low)
		.TryAdd(Level1Fields.Volume, ticker.Volume)
		.TryAdd(Level1Fields.AveragePrice, ticker.Average)
		.TryAdd(Level1Fields.Change, ticker.DailyPercent)
		.TryAdd(Level1Fields.State, market.Status.ToStockSharp());

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(transactionId, out subscription))
				release = ReleaseReference(_streamReferences,
					new("ticker", subscription.NativeSymbol));
		if (release && _wsClient is not null)
			await _wsClient.UnsubscribeTickerAsync(subscription.NativeSymbol,
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
					new("orderbook", subscription.NativeSymbol));
		if (release && _wsClient is not null)
			await _wsClient.UnsubscribeOrderBookAsync(
				subscription.NativeSymbol, cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_tickSubscriptions.Remove(transactionId, out subscription))
				release = ReleaseReference(_streamReferences,
					new("trade", subscription.NativeSymbol));
		if (release && _wsClient is not null)
			await _wsClient.UnsubscribeTradesAsync(subscription.NativeSymbol,
				cancellationToken);
	}

	private async ValueTask OnWebSocketOrderBookAsync(
		BtcTurkWsOrderBook book, CancellationToken cancellationToken)
	{
		if (book?.PairSymbol.IsEmpty() != false)
			return;
		KeyValuePair<long, DepthSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions.Where(pair =>
				pair.Value.NativeSymbol.EqualsIgnoreCase(book.PairSymbol))];

		foreach (var pair in subscriptions)
			await SendDepthAsync(pair.Value.SecurityCode, book, pair.Key,
				pair.Value.Depth, cancellationToken);
	}

	private async ValueTask OnWebSocketTradeAsync(BtcTurkWsTrade trade,
		CancellationToken cancellationToken)
	{
		if (trade?.PairSymbol.IsEmpty() != false || trade.Id.IsEmpty() ||
			!AddPublicTrade(trade.PairSymbol, trade.Id))
			return;
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _tickSubscriptions.Where(pair =>
				pair.Value.NativeSymbol.EqualsIgnoreCase(trade.PairSymbol))];

		foreach (var pair in subscriptions)
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = pair.Value.SecurityCode.ToStockSharp(),
				ServerTime = trade.Timestamp > 0
					? trade.Timestamp.FromUnixMilliseconds()
					: CurrentTime,
				OriginalTransactionId = pair.Key,
				TradeStringId = trade.Id,
				TradePrice = trade.Price,
				TradeVolume = trade.Amount.Abs(),
				OriginSide = trade.Side.ToStockSharp(),
			}, cancellationToken);
	}

	private async ValueTask OnWebSocketTickerAsync(BtcTurkWsTicker ticker,
		CancellationToken cancellationToken)
	{
		if (ticker?.PairSymbol.IsEmpty() != false)
			return;
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions.Where(pair =>
				pair.Value.NativeSymbol.EqualsIgnoreCase(ticker.PairSymbol))];

		foreach (var pair in subscriptions)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = pair.Value.SecurityCode.ToStockSharp(),
				ServerTime = CurrentTime,
				OriginalTransactionId = pair.Key,
			}
			.TryAdd(Level1Fields.BestBidPrice, ticker.Bid)
			.TryAdd(Level1Fields.BestBidVolume, ticker.BidVolume)
			.TryAdd(Level1Fields.BestAskPrice, ticker.Ask)
			.TryAdd(Level1Fields.BestAskVolume, ticker.AskVolume)
			.TryAdd(Level1Fields.LastTradePrice, ticker.Last)
			.TryAdd(Level1Fields.OpenPrice, ticker.Open)
			.TryAdd(Level1Fields.HighPrice, ticker.High)
			.TryAdd(Level1Fields.LowPrice, ticker.Low)
			.TryAdd(Level1Fields.Volume, ticker.Volume)
			.TryAdd(Level1Fields.AveragePrice, ticker.Average)
			.TryAdd(Level1Fields.Change, ticker.DailyPercent),
				cancellationToken);
	}

	private ValueTask SendDepthAsync(string securityCode,
		BtcTurkOrderBook book, long transactionId, int depth,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = securityCode.ToStockSharp(),
			ServerTime = book.Timestamp > 0
				? book.Timestamp.FromUnixMilliseconds()
				: CurrentTime,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = ToQuotes(book.Bids, false, depth),
			Asks = ToQuotes(book.Asks, true, depth),
		}, cancellationToken);

	private ValueTask SendDepthAsync(string securityCode,
		BtcTurkWsOrderBook book, long transactionId, int depth,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = securityCode.ToStockSharp(),
			ServerTime = CurrentTime,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			SeqNum = book.ChangeSet,
			Bids = ToQuotes(book.Bids, false, depth),
			Asks = ToQuotes(book.Asks, true, depth),
		}, cancellationToken);

	private ValueTask SendPublicTradeAsync(string securityCode,
		BtcTurkPublicTrade trade, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = securityCode.ToStockSharp(),
			ServerTime = trade.Timestamp > 0
				? trade.Timestamp.FromUnixMilliseconds()
				: CurrentTime,
			OriginalTransactionId = transactionId,
			TradeStringId = trade.Id,
			TradePrice = trade.Price,
			TradeVolume = trade.Amount.Abs(),
			OriginSide = trade.Side?.ToStockSharp(),
		}, cancellationToken);

	private async ValueTask SendCandlesAsync(string securityCode,
		BtcTurkKline candles, TimeSpan timeFrame, long transactionId,
		int maximum, CancellationToken cancellationToken)
	{
		if (candles is null ||
			!candles.Status.EqualsIgnoreCase("ok"))
			throw new InvalidDataException(
				"BtcTurk returned an invalid candle response.");
		var available = new[]
		{
			candles.Timestamps?.Length ?? 0,
			candles.Opens?.Length ?? 0,
			candles.Highs?.Length ?? 0,
			candles.Lows?.Length ?? 0,
			candles.Closes?.Length ?? 0,
			candles.Volumes?.Length ?? 0,
		}.Min();
		var count = available.Min(maximum);
		var start = available - count;

		for (var index = start; index < start + count; index++)
		{
			var openTime = candles.Timestamps[index].FromUnixSeconds();
			var closeTime = openTime + timeFrame;
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				SecurityId = securityCode.ToStockSharp(),
				OpenTime = openTime,
				CloseTime = closeTime,
				OpenPrice = candles.Opens[index],
				HighPrice = candles.Highs[index],
				LowPrice = candles.Lows[index],
				ClosePrice = candles.Closes[index],
				TotalVolume = candles.Volumes[index],
				TypedArg = timeFrame,
				OriginalTransactionId = transactionId,
				State = closeTime <= CurrentTime
					? CandleStates.Finished
					: CandleStates.Active,
			}, cancellationToken);
		}
	}

	private static QuoteChange[] ToQuotes(BtcTurkPriceLevel[] levels,
		bool isAsk, int depth)
		=> ToQuotes((levels ?? []).Select(static level =>
			(level.Price, level.Volume)), isAsk, depth);

	private static QuoteChange[] ToQuotes(BtcTurkWsPriceLevel[] levels,
		bool isAsk, int depth)
		=> ToQuotes((levels ?? []).Select(static level =>
			(level.Price, level.Volume)), isAsk, depth);

	private static QuoteChange[] ToQuotes(
		IEnumerable<(decimal Price, decimal Volume)> levels,
		bool isAsk, int depth)
	{
		var grouped = levels
			.Where(static level => level.Price > 0 && level.Volume > 0)
			.GroupBy(static level => level.Price)
			.Select(static group =>
				new QuoteChange(group.Key,
					group.Sum(static level => level.Volume)));
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
		return count.Max(1).Min(5000).To<int>();
	}

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
