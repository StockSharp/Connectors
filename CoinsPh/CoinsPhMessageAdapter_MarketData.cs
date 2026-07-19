namespace StockSharp.CoinsPh;

public partial class CoinsPhMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var requestedSymbol = lookupMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: lookupMsg.SecurityId.SecurityCode.NormalizeSymbol();
		MarketDefinition[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values];

		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var market in markets.OrderBy(static value => value.Symbol,
			StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.CoinsPh))
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
			}.TryAdd(Level1Fields.State, market.IsTrading
				? SecurityStates.Trading
				: SecurityStates.Stoped), cancellationToken);
			if (--left <= 0)
				break;
		}
		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
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
				"Coins.ph does not expose historical Level1 events.");

		var market = GetMarket(mdMsg.SecurityId);
		await SendLevel1SnapshotAsync(market, mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var key = new StreamKey(StreamTypes.Ticker, market.Symbol, default);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = market.Symbol,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await PublicSocketClient.SubscribeTickerAsync(market.Symbol,
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
	protected override async ValueTask OnMarketDepthSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
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
				"Coins.ph does not expose historical order-book events.");

		var market = GetMarket(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? 50).Min(200).Max(1);
		var snapshot = await RestClient.GetOrderBookAsync(new()
		{
			Symbol = market.Symbol,
			Limit = GetNativeDepth(depth),
		}, cancellationToken);
		await SendDepthAsync(market.Symbol, CurrentTime,
			ToQuotes(snapshot.Bids, false, depth),
			ToQuotes(snapshot.Asks, true, depth), mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var key = new StreamKey(StreamTypes.Depth, market.Symbol, default);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = market.Symbol,
				Depth = depth,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await PublicSocketClient.SubscribeDepthAsync(market.Symbol,
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
	protected override async ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
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
		var count = (mdMsg.Count ?? 100).Min(1000).Max(1).To<int>();
		var from = mdMsg.From?.ToUniversalTime();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var trades = await RestClient.GetTradesAsync(new()
		{
			Symbol = market.Symbol,
			Limit = count,
		}, cancellationToken);
		foreach (var trade in (trades ?? []).Where(trade => trade is not null &&
			(from is null || trade.Timestamp.FromMilliseconds(DateTime.MinValue) >= from) &&
			trade.Timestamp.FromMilliseconds(DateTime.MaxValue) <= to)
			.OrderBy(static trade => trade.Timestamp))
			await SendPublicTradeAsync(market.Symbol, trade.TradeId,
				trade.Timestamp, trade.Price, trade.Quantity, trade.IsBuyerMaker,
				mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var key = new StreamKey(StreamTypes.Trades, market.Symbol, default);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = market.Symbol,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await PublicSocketClient.SubscribeTradesAsync(market.Symbol,
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
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeCandlesAsync(mdMsg.OriginalTransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var market = GetMarket(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		var interval = timeFrame.ToCoinsPhInterval();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var count = GetCandleCount(mdMsg, timeFrame, to);
		var from = mdMsg.From?.ToUniversalTime() ??
			to - TimeSpan.FromTicks(timeFrame.Ticks * count);
		var candles = await RestClient.GetKlinesAsync(new()
		{
			Symbol = market.Symbol,
			Interval = interval,
			StartTime = new DateTimeOffset(from).ToUnixTimeMilliseconds(),
			EndTime = new DateTimeOffset(to).ToUnixTimeMilliseconds(),
			Limit = count,
		}, cancellationToken);
		foreach (var candle in (candles ?? []).OrderBy(static value => value.OpenTime))
			await SendCandleAsync(market.Symbol, candle, timeFrame,
				mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var key = new StreamKey(StreamTypes.Klines, market.Symbol, timeFrame);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = market.Symbol,
				TimeFrame = timeFrame,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await PublicSocketClient.SubscribeKlinesAsync(market.Symbol,
					interval, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeCandlesAsync(mdMsg.TransactionId, cancellationToken);
			throw;
		}
	}

	private SecurityMessage CreateSecurity(MarketDefinition market,
		long originalTransactionId)
		=> new()
		{
			SecurityId = market.Symbol.ToStockSharp(),
			Name = $"{market.BaseAsset}/{market.QuoteAsset}",
			ShortName = $"{market.BaseAsset}/{market.QuoteAsset}",
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = market.QuoteAsset.ToCurrency(),
			PriceStep = market.PriceStep > 0 ? market.PriceStep : null,
			VolumeStep = market.QuantityStep > 0 ? market.QuantityStep : null,
			MinVolume = market.MinimumQuantity > 0
				? market.MinimumQuantity
				: null,
			MaxVolume = market.MaximumQuantity > 0
				? market.MaximumQuantity
				: null,
			OriginalTransactionId = originalTransactionId,
		};

	private async ValueTask SendLevel1SnapshotAsync(MarketDefinition market,
		long transactionId, CancellationToken cancellationToken)
	{
		var ticker = await RestClient.GetTickerAsync(market.Symbol,
			cancellationToken);
		if (ticker is null)
			throw new InvalidDataException(
				$"Coins.ph returned no ticker for '{market.Symbol}'.");
		await SendOutMessageAsync(CreateLevel1Message(market.Symbol,
			ticker.CloseTime.FromMilliseconds(CurrentTime), transactionId,
			ticker.LastPrice, ticker.LastQuantity, ticker.BidPrice,
			ticker.BidQuantity, ticker.AskPrice, ticker.AskQuantity,
			ticker.OpenPrice, ticker.HighPrice, ticker.LowPrice, ticker.Volume,
			ticker.PriceChange), cancellationToken);
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(transactionId, out subscription))
				release = ReleaseReference(_streamReferences,
					new(StreamTypes.Ticker, subscription.Symbol, default));
		if (release)
			await PublicSocketClient.ReleaseTickerAsync(subscription.Symbol,
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
					new(StreamTypes.Depth, subscription.Symbol, default));
		if (release)
			await PublicSocketClient.ReleaseDepthAsync(subscription.Symbol,
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
					new(StreamTypes.Trades, subscription.Symbol, default));
		if (release)
			await PublicSocketClient.ReleaseTradesAsync(subscription.Symbol,
				cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		CandleSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_candleSubscriptions.Remove(transactionId, out subscription))
				release = ReleaseReference(_streamReferences,
					new(StreamTypes.Klines, subscription.Symbol,
						subscription.TimeFrame));
		if (release)
			await PublicSocketClient.ReleaseKlinesAsync(subscription.Symbol,
				subscription.TimeFrame.ToCoinsPhInterval(), cancellationToken);
	}

	private async ValueTask OnSocketTradeAsync(CoinsPhPublicSocketMessage trade,
		CancellationToken cancellationToken)
	{
		if (trade?.Symbol.IsEmpty() != false || trade.Price <= 0 ||
			trade.Quantity <= 0 || !AddPublicTrade(trade.Symbol, trade.TradeId))
			return;
		var market = GetMarket(trade.Symbol);
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _tickSubscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(market.Symbol))];
		foreach (var pair in subscriptions)
			await SendPublicTradeAsync(market.Symbol, trade.TradeId,
				(trade.TradeTime > 0 ? trade.TradeTime : trade.EventTime),
				trade.Price, trade.Quantity, trade.IsBuyerMaker, pair.Key,
				cancellationToken, false);
	}

	private async ValueTask OnSocketTickerAsync(CoinsPhPublicSocketMessage ticker,
		CancellationToken cancellationToken)
	{
		if (ticker?.Symbol.IsEmpty() != false)
			return;
		var market = GetMarket(ticker.Symbol);
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(market.Symbol))];
		foreach (var pair in subscriptions)
			await SendOutMessageAsync(CreateLevel1Message(market.Symbol,
				ticker.EventTime.FromMilliseconds(CurrentTime), pair.Key,
				ticker.LastPrice, ticker.LastQuantity, ticker.BidPrice,
				ticker.BidQuantity, ticker.AskPrice, ticker.AskQuantity,
				ticker.OpenPrice, ticker.HighPrice, ticker.LowPrice,
				ticker.Volume, ticker.LastPrice - ticker.OpenPrice),
				cancellationToken);
	}

	private async ValueTask OnSocketDepthAsync(CoinsPhPublicSocketMessage update,
		CancellationToken cancellationToken)
	{
		if (update?.Symbol.IsEmpty() != false)
			return;
		var market = GetMarket(update.Symbol);
		KeyValuePair<long, DepthSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(market.Symbol))];
		var serverTime = update.EventTime.FromMilliseconds(CurrentTime);
		foreach (var pair in subscriptions)
			await SendDepthAsync(market.Symbol, serverTime,
				ToQuotes(update.Bids, false, pair.Value.Depth),
				ToQuotes(update.Asks, true, pair.Value.Depth), pair.Key,
				cancellationToken);
	}

	private async ValueTask OnSocketKlineAsync(CoinsPhPublicSocketMessage update,
		CancellationToken cancellationToken)
	{
		var candle = update?.Kline;
		if (candle?.Symbol.IsEmpty() != false || candle.Interval.IsEmpty())
			return;
		var market = GetMarket(candle.Symbol);
		var timeFrame = candle.Interval.ToTimeFrame();
		KeyValuePair<long, CandleSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _candleSubscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(market.Symbol) &&
				pair.Value.TimeFrame == timeFrame)];
		foreach (var pair in subscriptions)
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				SecurityId = market.Symbol.ToStockSharp(),
				OpenTime = candle.OpenTime.FromMilliseconds(CurrentTime),
				CloseTime = candle.CloseTime.FromMilliseconds(CurrentTime),
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				TotalTicks = candle.TradeCount.Min(int.MaxValue).Max(0).To<int>(),
				TypedArg = timeFrame,
				OriginalTransactionId = pair.Key,
				State = candle.IsClosed
					? CandleStates.Finished
					: CandleStates.Active,
			}, cancellationToken);
	}

	private Level1ChangeMessage CreateLevel1Message(string symbol,
		DateTime serverTime, long transactionId, decimal lastPrice,
		decimal lastQuantity, decimal bidPrice, decimal bidQuantity,
		decimal askPrice, decimal askQuantity, decimal openPrice,
		decimal highPrice, decimal lowPrice, decimal volume, decimal change)
		=> new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(),
			ServerTime = serverTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, lastPrice)
		.TryAdd(Level1Fields.LastTradeVolume, lastQuantity)
		.TryAdd(Level1Fields.BestBidPrice, bidPrice)
		.TryAdd(Level1Fields.BestBidVolume, bidQuantity)
		.TryAdd(Level1Fields.BestAskPrice, askPrice)
		.TryAdd(Level1Fields.BestAskVolume, askQuantity)
		.TryAdd(Level1Fields.OpenPrice, openPrice)
		.TryAdd(Level1Fields.HighPrice, highPrice)
		.TryAdd(Level1Fields.LowPrice, lowPrice)
		.TryAdd(Level1Fields.Volume, volume)
		.TryAdd(Level1Fields.Change, change);

	private ValueTask SendDepthAsync(string symbol, DateTime serverTime,
		QuoteChange[] bids, QuoteChange[] asks, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(),
			ServerTime = serverTime,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = bids,
			Asks = asks,
		}, cancellationToken);

	private ValueTask SendPublicTradeAsync(string symbol, long tradeId,
		long timestamp, decimal price, decimal quantity, bool isBuyerMaker,
		long transactionId, CancellationToken cancellationToken,
		bool addToDeduplication = true)
	{
		if (addToDeduplication)
			_ = AddPublicTrade(symbol, tradeId);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = symbol.ToStockSharp(),
			ServerTime = timestamp.FromMilliseconds(CurrentTime),
			OriginalTransactionId = transactionId,
			TradeId = tradeId,
			TradePrice = price,
			TradeVolume = quantity,
			OriginSide = isBuyerMaker ? Sides.Sell : Sides.Buy,
		}, cancellationToken);
	}

	private ValueTask SendCandleAsync(string symbol, CoinsPhKline candle,
		TimeSpan timeFrame, long transactionId,
		CancellationToken cancellationToken)
	{
		var openTime = candle.OpenTime.FromMilliseconds(CurrentTime);
		var closeTime = candle.CloseTime.FromMilliseconds(openTime + timeFrame);
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
			TotalTicks = candle.TradeCount.Min(int.MaxValue).Max(0).To<int>(),
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = closeTime <= CurrentTime
				? CandleStates.Finished
				: CandleStates.Active,
		}, cancellationToken);
	}

	private static QuoteChange[] ToQuotes(IEnumerable<CoinsPhBookLevel> levels,
		bool isAsk, int depth)
	{
		var filtered = (levels ?? []).Where(static level => level is not null &&
			level.Price > 0 && level.Quantity > 0);
		return [.. (isAsk
			? filtered.OrderBy(static level => level.Price)
			: filtered.OrderByDescending(static level => level.Price))
			.Take(depth).Select(static level =>
				new QuoteChange(level.Price, level.Quantity))];
	}

	private static int GetNativeDepth(int depth)
		=> depth switch
		{
			<= 5 => 5,
			<= 10 => 10,
			<= 20 => 20,
			<= 50 => 50,
			<= 100 => 100,
			_ => 200,
		};

	private static int GetCandleCount(MarketDataMessage message,
		TimeSpan timeFrame, DateTime to)
	{
		if (message.Count is long count)
			return count.Min(1000).Max(1).To<int>();
		if (message.From is DateTime from && to > from)
			return ((to - from.ToUniversalTime()).Ticks / timeFrame.Ticks + 1)
				.Min(1000L).Max(1L).To<int>();
		return 300;
	}

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
