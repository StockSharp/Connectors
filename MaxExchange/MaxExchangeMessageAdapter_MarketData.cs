namespace StockSharp.MaxExchange;

public partial class MaxExchangeMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			lookupMsg.TransactionId, cancellationToken);

		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var requestedSymbol = lookupMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: GetMarket(lookupMsg.SecurityId).SecurityCode;
		MaxExchangeSymbol[] markets;
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
					BoardCodes.MaxExchange))
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
				market.IsMaintenance
					? SecurityStates.Stoped
					: SecurityStates.Trading),
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
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId, cancellationToken);

		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeLevel1Async(
				mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"MaxExchange does not expose historical Level1 events.");

		var market = GetMarket(mdMsg.SecurityId);
		if (mdMsg.IsHistoryOnly())
		{
			await SendLevel1SnapshotAsync(
				market, mdMsg.TransactionId, cancellationToken);
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}

		var key = new StreamKey("ticker", market.Pair, 0);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				NativeSymbol = market.Pair,
				SecurityCode = market.SecurityCode,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await WsClient.SubscribeTickerAsync(
					market.Pair, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeLevel1Async(
				mdMsg.TransactionId, cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId, cancellationToken);

		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeDepthAsync(
				mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"MaxExchange does not expose historical order-book events.");

		var market = GetMarket(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? 50).Min(50).Max(1);
		var streamDepth = MaxExchangeRestClient.NormalizeDepth(depth);
		if (mdMsg.IsHistoryOnly())
		{
			var snapshot = await RestClient.GetOrderBookAsync(
				market.Pair, streamDepth, cancellationToken);
			await SendDepthAsync(market.SecurityCode, snapshot,
				mdMsg.TransactionId, depth, cancellationToken);
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}

		var key = new StreamKey(
			"orderbook", market.Pair, streamDepth);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				NativeSymbol = market.Pair,
				SecurityCode = market.SecurityCode,
				Depth = depth,
				StreamDepth = streamDepth,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await WsClient.SubscribeOrderBookAsync(
					market.Pair, streamDepth, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeDepthAsync(
				mdMsg.TransactionId, cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId, cancellationToken);

		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeTicksAsync(
				mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}

		var market = GetMarket(mdMsg.SecurityId);
		if (mdMsg.From is not null || mdMsg.To is not null ||
			mdMsg.Count is not null || mdMsg.IsHistoryOnly())
		{
			var count = (mdMsg.Count ?? 100)
				.Min(1000).Max(1).To<int>();
			var from = mdMsg.From?.ToUtc();
			var to = (mdMsg.To ?? DateTime.UtcNow).ToUtc();
			var trades = await RestClient.GetPublicTradesAsync(
				market.Pair, cancellationToken);

			foreach (var trade in (trades ?? [])
				.Where(trade =>
				{
					var time = trade.Timestamp > 0
						? ToTimestamp(trade.Timestamp)
						: DateTime.MinValue;
					return time != DateTime.MinValue &&
						(from is null || time >= from.Value) &&
						time <= to;
				})
				.OrderBy(static trade => trade.Timestamp)
				.TakeLast(count))
			{
				var tradeId = CreatePublicTradeId(trade);
				if (!AddTrade(market.Pair, tradeId, false))
					continue;
				await SendPublicTradeAsync(
					market.SecurityCode, trade,
					tradeId, mdMsg.TransactionId,
					cancellationToken);
			}
		}

		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}

		var key = new StreamKey("trades", market.Pair, 0);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				NativeSymbol = market.Pair,
				SecurityCode = market.SecurityCode,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await WsClient.SubscribeTradesAsync(
					market.Pair, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeTicksAsync(
				mdMsg.TransactionId, cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId, cancellationToken);

		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeCandlesAsync(
				mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}

		var market = GetMarket(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		if (!MaxExchangeExtensions.TimeFrames.Contains(timeFrame))
			throw new NotSupportedException(
				$"MaxExchange does not support the {timeFrame} " +
					"candle interval.");

		var resolution = timeFrame.ToMaxExchangeResolution();
		if (mdMsg.From is not null || mdMsg.To is not null ||
			mdMsg.Count is not null || mdMsg.IsHistoryOnly())
		{
			var to = (mdMsg.To ?? DateTime.UtcNow).ToUtc();
			var requested = mdMsg.Count?.Min(5000).Max(1).To<int>() ??
				GetCandleCount(mdMsg, timeFrame, to);
			var from = mdMsg.From?.ToUtc() ??
				SubtractSafely(to, timeFrame, requested);
			try
			{
				var candles = await RestClient.GetCandlesAsync(
					market.Pair, resolution, from, to,
					cancellationToken);

				foreach (var candle in (candles ?? [])
					.Where(candle =>
					{
						var time = ToTimestamp(candle.Timestamp);
						return time >= from && time <= to;
					})
					.OrderBy(static candle => candle.Timestamp)
					.TakeLast(requested))
					await SendCandleAsync(
						market.SecurityCode, candle, timeFrame,
						mdMsg.TransactionId, cancellationToken);
			}
			catch (HttpRequestException) when (
				!mdMsg.IsHistoryOnly())
			{
				this.AddWarningLog(
					"MAX Exchange REST candle history is " +
						"unavailable; continuing with live WebSocket.");
			}
		}

		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}

		var key = new StreamKey(
			"kline:" + resolution, market.Pair, 0);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				NativeSymbol = market.Pair,
				SecurityCode = market.SecurityCode,
				TimeFrame = timeFrame,
				Resolution = resolution,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await WsClient.SubscribeKlineAsync(
					market.Pair, resolution, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeCandlesAsync(
				mdMsg.TransactionId, cancellationToken);
			throw;
		}
	}

	private SecurityMessage CreateSecurity(MaxExchangeSymbol market,
		long originalTransactionId)
		=> new()
		{
			SecurityId = market.ToStockSharp(),
			Name = market.SecurityCode,
			ShortName = market.SecurityCode,
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = market.Quote.ToCurrency(),
			PriceStep = MaxExchangeExtensions.GetStep(
				market.QuotePrecision),
			VolumeStep = MaxExchangeExtensions.GetStep(
				market.AmountPrecision),
			MinVolume = market.MinimumAmount is > 0
				? market.MinimumAmount
				: null,
			MaxVolume = market.MaximumAmount is > 0
				? market.MaximumAmount
				: null,
			OriginalTransactionId = originalTransactionId,
		};

	private async ValueTask SendLevel1SnapshotAsync(
		MaxExchangeSymbol market, long transactionId,
		CancellationToken cancellationToken)
	{
		var ticker = await RestClient.GetTickerAsync(
			market.Pair, cancellationToken);
		if (ticker is null)
			throw new InvalidDataException(
				$"MaxExchange returned no ticker for '{market.Pair}'.");
		await SendOutMessageAsync(
			CreateLevel1Message(market, ticker, transactionId),
			cancellationToken);
	}

	private Level1ChangeMessage CreateLevel1Message(
		MaxExchangeSymbol market, MaxExchangeTicker ticker, long transactionId)
		=> new Level1ChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = ticker.Timestamp > 0
				? ticker.Timestamp.FromMaxExchangeMilliseconds()
				: CurrentTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice)
		.TryAdd(Level1Fields.BestBidPrice, ticker.BidPrice)
		.TryAdd(Level1Fields.BestBidVolume, ticker.BidVolume)
		.TryAdd(Level1Fields.BestAskPrice, ticker.AskPrice)
		.TryAdd(Level1Fields.BestAskVolume, ticker.AskVolume)
		.TryAdd(Level1Fields.HighPrice, ticker.HighPrice)
		.TryAdd(Level1Fields.LowPrice, ticker.LowPrice)
		.TryAdd(Level1Fields.Volume, ticker.Volume)
		.TryAdd(Level1Fields.Change, ticker.PriceChange)
		.TryAdd(Level1Fields.LastTradeOrigin,
			ticker.IsBuyer is bool isBuyer
				? isBuyer ? Sides.Buy : Sides.Sell
				: null)
		.TryAdd(Level1Fields.State,
			market.IsMaintenance
				? SecurityStates.Stoped
				: SecurityStates.Trading);

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(
				transactionId, out subscription))
				release = ReleaseReference(_streamReferences,
					new("ticker", subscription.NativeSymbol, 0));
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
					new("orderbook", subscription.NativeSymbol,
						subscription.StreamDepth));
		if (release && _wsClient is not null)
			await _wsClient.UnsubscribeOrderBookAsync(
				subscription.NativeSymbol,
				subscription.StreamDepth,
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
					new("trades", subscription.NativeSymbol, 0));
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
					new("kline:" + subscription.Resolution,
						subscription.NativeSymbol, 0));
		if (release && _wsClient is not null)
			await _wsClient.UnsubscribeKlineAsync(
				subscription.NativeSymbol,
				subscription.Resolution,
				cancellationToken);
	}

	private async ValueTask OnWebSocketTickerAsync(
		MaxExchangeTicker ticker, CancellationToken cancellationToken)
	{
		if (ticker?.Pair.IsEmpty() != false)
			return;
		var market = GetMarket(ticker.Pair);
		if (market is null)
			return;
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions.Where(pair =>
				pair.Value.NativeSymbol.EqualsIgnoreCase(ticker.Pair))];

		foreach (var pair in subscriptions)
			await SendOutMessageAsync(
				CreateLevel1Message(market, ticker, pair.Key),
				cancellationToken);
	}

	private async ValueTask OnWebSocketOrderBookAsync(
		MaxExchangeOrderBook book, CancellationToken cancellationToken)
	{
		if (book?.Pair.IsEmpty() != false)
			return;
		KeyValuePair<long, DepthSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions.Where(pair =>
				pair.Value.NativeSymbol.EqualsIgnoreCase(book.Pair) &&
				(book.Limit <= 0 ||
					pair.Value.StreamDepth == book.Limit))];

		foreach (var pair in subscriptions)
			await SendDepthAsync(
				pair.Value.SecurityCode, book, pair.Key,
				pair.Value.Depth, cancellationToken);
	}

	private async ValueTask OnWebSocketTradesAsync(
		MaxExchangeTradePush push, CancellationToken cancellationToken)
	{
		if (push?.Pair.IsEmpty() != false)
			return;
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _tickSubscriptions.Where(pair =>
				pair.Value.NativeSymbol.EqualsIgnoreCase(push.Pair))];

		for (var index = 0; index < (push.Data?.Length ?? 0); index++)
		{
			var trade = push.Data[index];
			var tradeId = push.EventId.IsEmpty()
				? CreatePublicTradeId(trade)
				: $"{push.EventId}-{index}";
			if (!AddTrade(push.Pair, tradeId, false))
				continue;

			foreach (var pair in subscriptions)
				await SendPublicTradeAsync(
					pair.Value.SecurityCode, trade, tradeId,
					pair.Key, cancellationToken);
		}
	}

	private async ValueTask OnWebSocketKlineAsync(
		MaxExchangeKlineEvent push,
		CancellationToken cancellationToken)
	{
		if (push?.Market.IsEmpty() != false || push.Kline is null)
			return;
		KeyValuePair<long, CandleSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _candleSubscriptions.Where(pair =>
				pair.Value.NativeSymbol.EqualsIgnoreCase(push.Market) &&
				pair.Value.Resolution.EqualsIgnoreCase(
					push.Kline.Resolution))];

		foreach (var pair in subscriptions)
			await SendCandleAsync(
				pair.Value.SecurityCode,
				new()
				{
					Timestamp = push.Kline.StartTime,
					Open = push.Kline.Open,
					High = push.Kline.High,
					Low = push.Kline.Low,
					Close = push.Kline.Close,
					Volume = push.Kline.Volume,
					IsFinished = push.Kline.IsFinished,
				},
				pair.Value.TimeFrame,
				pair.Key,
				cancellationToken);
	}

	private ValueTask SendDepthAsync(string securityCode,
		MaxExchangeOrderBook book, long transactionId, int depth,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = securityCode.ToMaxExchangeSecurityId(),
			ServerTime = book.Timestamp > 0
				? ToTimestamp(book.Timestamp)
				: CurrentTime,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = ToQuotes(book.Bids, false, depth),
			Asks = ToQuotes(book.Asks, true, depth),
		}, cancellationToken);

	private ValueTask SendPublicTradeAsync(string securityCode,
		MaxExchangeTrade trade, string tradeId, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = securityCode.ToMaxExchangeSecurityId(),
			ServerTime = trade.Timestamp > 0
				? ToTimestamp(trade.Timestamp)
				: CurrentTime,
			OriginalTransactionId = transactionId,
			TradeStringId = tradeId,
			TradePrice = trade.Price,
			TradeVolume = trade.Amount.Abs(),
			OriginSide = trade.IsBuyer is bool isBuyer
				? isBuyer ? Sides.Buy : Sides.Sell
				: null,
		}, cancellationToken);

	private ValueTask SendCandleAsync(string securityCode,
		MaxExchangeCandle candle, TimeSpan timeFrame, long transactionId,
		CancellationToken cancellationToken)
	{
		var openTime = ToTimestamp(candle.Timestamp);
		var closeTime = openTime + timeFrame;
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = securityCode.ToMaxExchangeSecurityId(),
			OpenTime = openTime,
			CloseTime = closeTime,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.Volume,
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = candle.IsFinished || closeTime <= CurrentTime
				? CandleStates.Finished
				: CandleStates.Active,
		}, cancellationToken);
	}

	private static QuoteChange[] ToQuotes(
		decimal[][] levels, bool isAsk, int depth)
	{
		var grouped = (levels ?? [])
			.Where(static level =>
				level is { Length: >= 2 } &&
				level[0] > 0 && level[1] > 0)
			.GroupBy(static level => level[0])
			.Select(static group => new QuoteChange(
				group.Key,
				group.Sum(static level => level[1])));
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

	private static DateTime SubtractSafely(DateTime to,
		TimeSpan timeFrame, int count)
	{
		var ticks = (decimal)timeFrame.Ticks * count;
		var maximum = to.Ticks - DateTime.UnixEpoch.Ticks;
		return ticks >= maximum
			? DateTime.UnixEpoch
			: to - TimeSpan.FromTicks((long)ticks);
	}

	private static DateTime ToTimestamp(long timestamp)
		=> timestamp > 100_000_000_000
			? timestamp.FromMaxExchangeMilliseconds()
			: timestamp.FromMaxExchangeSeconds();

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(
			message.TransactionId, cancellationToken);
	}
}
