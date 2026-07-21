namespace StockSharp.Kalshi;

public partial class KalshiMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		if (securityTypes.Count > 0 &&
			!securityTypes.Contains(SecurityTypes.Option))
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}
		if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
			!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.Kalshi))
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}

		KalshiMarket[] markets;
		var code = lookupMsg.SecurityId.SecurityCode;
		if (!code.IsEmpty())
		{
			try
			{
				markets = [await RestClient.GetMarketAsync(code, cancellationToken)];
			}
			catch (KalshiApiException error) when (
				error.StatusCode == HttpStatusCode.NotFound)
			{
				markets = [];
			}
		}
		else
		{
			var requested = SecurityLookupLimit;
			if (lookupMsg.Count is long count)
			{
				var boundedCount = Math.Min((long)SecurityLookupLimit,
					Math.Max(1L, count));
				var boundedSkip = Math.Min((long)SecurityLookupLimit,
					Math.Max(0L, lookupMsg.Skip ?? 0));
				requested = Math.Min((long)SecurityLookupLimit,
					boundedCount + boundedSkip).To<int>();
			}
			markets = await RestClient.GetMarketsAsync(requested,
				cancellationToken);
		}

		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = Math.Max(0, lookupMsg.Count ?? long.MaxValue);
		foreach (var market in markets
			.Where(static market => market?.Ticker.IsEmpty() == false)
			.OrderBy(static market => market.Ticker,
				StringComparer.OrdinalIgnoreCase))
		{
			cancellationToken.ThrowIfCancellationRequested();
			AddMarket(market);
			var security = CreateSecurity(market, lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			if (skip > 0)
			{
				skip--;
				continue;
			}
			if (left <= 0)
				break;
			await SendOutMessageAsync(security, cancellationToken);
			left--;
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
		if (mdMsg.From is not null || mdMsg.To is not null)
			throw new NotSupportedException(
				"Kalshi does not publish historical Level1 changes.");
		var market = await GetMarketAsync(mdMsg.SecurityId, cancellationToken);
		market = await RestClient.GetMarketAsync(market.Ticker, cancellationToken);
		AddMarket(market);
		await SendLevel1Async(market, mdMsg.TransactionId, DateTime.UtcNow,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Ticker = market.Ticker,
			});
		try
		{
			await SocketClient.SubscribeAsync(KalshiSocketChannels.Ticker,
				market.Ticker, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_level1Subscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
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
		if (mdMsg.From is not null || mdMsg.To is not null)
			throw new NotSupportedException(
				"Kalshi does not publish historical order books.");
		var market = await GetMarketAsync(mdMsg.SecurityId, cancellationToken);
		var depth = (mdMsg.MaxDepth ?? MarketDepth).Max(1).Min(MarketDepth);
		var book = await RestClient.GetOrderBookAsync(market.Ticker, depth,
			cancellationToken);
		var state = ApplyRestBook(market, book, DateTime.UtcNow);
		await SendDepthAsync(market, state, mdMsg.TransactionId, depth,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Ticker = market.Ticker,
				Depth = depth,
			});
		try
		{
			await SocketClient.SubscribeAsync(
				KalshiSocketChannels.OrderBookDelta, market.Ticker,
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_depthSubscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
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
		var market = await GetMarketAsync(mdMsg.SecurityId, cancellationToken);
		var maximum = (mdMsg.Count ?? HistoryLimit).Min(HistoryLimit).Max(1)
			.To<int>();
		if (mdMsg.IsHistoryOnly() || mdMsg.From is not null || mdMsg.To is not null)
		{
			var from = mdMsg.From?.EnsureUtc();
			var to = mdMsg.To?.EnsureUtc();
			if (from is DateTime start && to is DateTime end && start > end)
				throw new ArgumentOutOfRangeException(nameof(mdMsg),
					"Kalshi trade-history start time cannot be later than end time.");
			var trades = await RestClient.GetTradesAsync(market.Ticker, from, to,
				maximum, cancellationToken);
			foreach (var trade in trades
				.Where(static trade => trade?.TradeId.IsEmpty() == false)
				.OrderBy(static trade => trade.CreatedTime.TryParseKalshiTime() ??
					DateTime.UnixEpoch))
				await SendTickAsync(market, trade, mdMsg.TransactionId,
					cancellationToken);
		}
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		using (_sync.EnterScope())
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Ticker = market.Ticker,
			});
		try
		{
			await SocketClient.SubscribeAsync(KalshiSocketChannels.Trade,
				market.Ticker, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_tickSubscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
			return;
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		var market = await GetMarketAsync(mdMsg.SecurityId, cancellationToken);
		var timeFrame = mdMsg.GetTimeFrame();
		if (!_timeFrames.Contains(timeFrame))
			throw new NotSupportedException(
				$"Kalshi does not support the {timeFrame} candle interval.");
		var maximum = (mdMsg.Count ?? HistoryLimit).Min(HistoryLimit).Max(1)
			.To<int>();
		var now = DateTime.UtcNow;
		var to = (mdMsg.To ?? now).EnsureUtc().Min(now);
		var from = mdMsg.From?.EnsureUtc() ?? new DateTime(
			Math.Max(DateTime.UnixEpoch.Ticks,
				to.Ticks - checked(timeFrame.Ticks * (long)maximum)),
			DateTimeKind.Utc);
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg),
				"Kalshi candle start time cannot be later than end time.");
		var period = timeFrame.TotalMinutes.To<int>();
		var candles = await RestClient.GetCandlesticksAsync(market.Ticker,
			from, to, period, cancellationToken);
		foreach (var candle in candles.OrderBy(static candle => candle.EndTime)
			.TakeLast(maximum))
		{
			var open = candle?.Price?.Open.TryParseKalshiDecimal();
			var high = candle?.Price?.High.TryParseKalshiDecimal();
			var low = candle?.Price?.Low.TryParseKalshiDecimal();
			var close = candle?.Price?.Close.TryParseKalshiDecimal();
			if (open is null || high is null || low is null || close is null ||
				candle.EndTime <= 0)
				continue;
			var closeTime = candle.EndTime.FromKalshiSeconds();
			UpdateServerTime(closeTime);
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				SecurityId = market.ToStockSharp(),
				OpenTime = closeTime - timeFrame,
				CloseTime = closeTime,
				OpenPrice = open.Value,
				HighPrice = high.Value,
				LowPrice = low.Value,
				ClosePrice = close.Value,
				TotalVolume = candle.Volume.TryParseKalshiDecimal() ?? 0m,
				OpenInterest = candle.OpenInterest.TryParseKalshiDecimal(),
				TypedArg = timeFrame,
				OriginalTransactionId = mdMsg.TransactionId,
				State = CandleStates.Finished,
			}, cancellationToken);
		}
		await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
	}

	private static SecurityMessage CreateSecurity(KalshiMarket market,
		long originalTransactionId)
		=> new SecurityMessage
		{
			SecurityId = market.ToStockSharp(),
			Name = market.Title.IsEmpty() ? market.YesSubTitle.IsEmpty()
				? market.Ticker
				: market.YesSubTitle
				: market.Title,
			ShortName = market.YesSubTitle.IsEmpty() ? "YES" : market.YesSubTitle,
			Class = market.EventTicker,
			SecurityType = SecurityTypes.Option,
			BinaryOptionType = market.YesSubTitle.IsEmpty()
				? "YES"
				: market.YesSubTitle,
			Currency = CurrencyTypes.USD,
			ExpiryDate = market.LatestExpirationTime.TryParseKalshiTime() ??
				market.CloseTime.TryParseKalshiTime(),
			PriceStep = market.GetPriceStep(),
			VolumeStep = 0.01m,
			MinVolume = 0.01m,
			Multiplier = market.NotionalValue.TryParseKalshiDecimal() ?? 1m,
			OriginalTransactionId = originalTransactionId,
		}.TryFillUnderlyingId(market.EventTicker);

	private KalshiBookState ApplyRestBook(KalshiMarket market,
		KalshiOrderBook book, DateTime time)
	{
		KalshiBookState state;
		using (_sync.EnterScope())
		{
			if (!_books.TryGetValue(market.Ticker, out state))
				_books[market.Ticker] = state = new();
			state.ApplyRest(book, time);
		}
		UpdateServerTime(time);
		return state;
	}

	private ValueTask SendDepthAsync(KalshiMarket market,
		KalshiBookState state, long transactionId, int depth,
		CancellationToken cancellationToken)
	{
		QuoteChange[] bids;
		QuoteChange[] asks;
		DateTime time;
		using (_sync.EnterScope())
		{
			bids = [.. state.Bids.Take(depth).Select(static pair =>
				new QuoteChange(pair.Key, pair.Value))];
			asks = [.. state.Asks.Take(depth).Select(static pair =>
				new QuoteChange(pair.Key, pair.Value))];
			time = state.Time == default ? ServerTime : state.Time;
		}
		UpdateServerTime(time);
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = bids,
			Asks = asks,
		}, cancellationToken);
	}

	private ValueTask SendLevel1Async(KalshiMarket market, long transactionId,
		DateTime time, CancellationToken cancellationToken)
	{
		UpdateServerTime(time);
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, market.YesBid.TryParseKalshiDecimal())
		.TryAdd(Level1Fields.BestBidVolume,
			market.YesBidSize.TryParseKalshiDecimal())
		.TryAdd(Level1Fields.BestAskPrice, market.YesAsk.TryParseKalshiDecimal())
		.TryAdd(Level1Fields.BestAskVolume,
			market.YesAskSize.TryParseKalshiDecimal())
		.TryAdd(Level1Fields.LastTradePrice,
			market.LastPrice.TryParseKalshiDecimal())
		.TryAdd(Level1Fields.Volume, market.Volume.TryParseKalshiDecimal())
		.TryAdd(Level1Fields.OpenInterest,
			market.OpenInterest.TryParseKalshiDecimal())
		.TryAdd(Level1Fields.PriceStep, market.GetPriceStep()), cancellationToken);
	}

	private ValueTask SendTickAsync(KalshiMarket market, KalshiTrade trade,
		long transactionId, CancellationToken cancellationToken)
	{
		var time = trade.CreatedTime.TryParseKalshiTime() ?? DateTime.UtcNow;
		UpdateServerTime(time);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = market.ToStockSharp(),
			ServerTime = time,
			TradeStringId = trade.TradeId,
			TradePrice = trade.YesPrice.ParseKalshiDecimal("trade price"),
			TradeVolume = trade.Volume.ParseKalshiDecimal("trade volume"),
			OriginSide = trade.TakerBookSide.ToStockSharp(),
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}

	private async ValueTask OnSocketEventAsync(KalshiSocketEvent item,
		CancellationToken cancellationToken)
	{
		if (item?.Type is not KalshiSocketEventTypes eventType ||
			item.Message is null)
			return;
		switch (eventType)
		{
			case KalshiSocketEventTypes.OrderBookSnapshot:
				await OnSocketBookSnapshotAsync(item, cancellationToken);
				break;
			case KalshiSocketEventTypes.OrderBookDelta:
				await OnSocketBookDeltaAsync(item, cancellationToken);
				break;
			case KalshiSocketEventTypes.Ticker:
				await OnSocketTickerAsync(item, cancellationToken);
				break;
			case KalshiSocketEventTypes.Trade:
				await OnSocketTradeAsync(item, cancellationToken);
				break;
			case KalshiSocketEventTypes.UserOrder:
				await OnUserOrderAsync(item, cancellationToken);
				break;
			case KalshiSocketEventTypes.Fill:
				await OnUserFillAsync(item, cancellationToken);
				break;
			case KalshiSocketEventTypes.MarketPosition:
				await OnMarketPositionAsync(item, cancellationToken);
				break;
			default:
				throw new InvalidDataException(
					$"Unsupported Kalshi WebSocket event '{eventType}'.");
		}
	}

	private async ValueTask OnSocketBookSnapshotAsync(KalshiSocketEvent item,
		CancellationToken cancellationToken)
	{
		var market = GetCachedMarket(item.Message.Ticker);
		if (market is null)
			return;
		KalshiBookState state;
		var time = item.Message.Timestamp?.FromKalshiMilliseconds() ??
			DateTime.UtcNow;
		using (_sync.EnterScope())
		{
			if (!_books.TryGetValue(market.Ticker, out state))
				_books[market.Ticker] = state = new();
			state.ApplySocket(item.Message.Yes, item.Message.No, time,
				item.Sequence ?? 0);
		}
		await PublishBookAsync(market, state, cancellationToken);
	}

	private async ValueTask OnSocketBookDeltaAsync(KalshiSocketEvent item,
		CancellationToken cancellationToken)
	{
		var message = item.Message;
		var market = GetCachedMarket(message.Ticker);
		if (market is null || message.Side is not KalshiMarketSides side)
			return;
		var time = message.Timestamp?.FromKalshiMilliseconds() ?? DateTime.UtcNow;
		KalshiBookState state;
		var isApplied = false;
		using (_sync.EnterScope())
		{
			if (_books.TryGetValue(market.Ticker, out state))
				isApplied = state.ApplyDelta(side, message.Price, message.Delta,
					time, item.Sequence ?? 0);
		}
		if (!isApplied)
		{
			var book = await RestClient.GetOrderBookAsync(market.Ticker,
				MarketDepth, cancellationToken);
			state = ApplyRestBook(market, book, time);
		}
		await PublishBookAsync(market, state, cancellationToken);
	}

	private async ValueTask PublishBookAsync(KalshiMarket market,
		KalshiBookState state, CancellationToken cancellationToken)
	{
		DepthSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions.Values.Where(subscription =>
				subscription.Ticker.Equals(market.Ticker,
					StringComparison.OrdinalIgnoreCase))];
		foreach (var subscription in subscriptions)
			await SendDepthAsync(market, state, subscription.TransactionId,
				subscription.Depth, cancellationToken);
	}

	private async ValueTask OnSocketTickerAsync(KalshiSocketEvent item,
		CancellationToken cancellationToken)
	{
		var message = item.Message;
		var market = GetCachedMarket(message.Ticker);
		if (market is null)
			return;
		market.YesBid = message.YesBid;
		market.YesBidSize = message.YesBidSize;
		market.YesAsk = message.YesAsk;
		market.YesAskSize = message.YesAskSize;
		market.LastPrice = message.Price;
		market.Volume = message.Volume;
		market.OpenInterest = message.OpenInterest;
		var time = message.Timestamp?.FromKalshiMilliseconds() ?? DateTime.UtcNow;
		MarketSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions.Values.Where(subscription =>
				subscription.Ticker.Equals(market.Ticker,
					StringComparison.OrdinalIgnoreCase))];
		foreach (var subscription in subscriptions)
			await SendLevel1Async(market, subscription.TransactionId, time,
				cancellationToken);
	}

	private async ValueTask OnSocketTradeAsync(KalshiSocketEvent item,
		CancellationToken cancellationToken)
	{
		var message = item.Message;
		var market = GetCachedMarket(message.Ticker);
		if (market is null || message.TradeId.IsEmpty())
			return;
		var time = message.Timestamp?.FromKalshiMilliseconds() ?? DateTime.UtcNow;
		UpdateServerTime(time);
		MarketSubscription[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _tickSubscriptions.Values.Where(subscription =>
				subscription.Ticker.Equals(market.Ticker,
					StringComparison.OrdinalIgnoreCase))];
		foreach (var subscription in subscriptions)
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = market.ToStockSharp(),
				ServerTime = time,
				TradeStringId = message.TradeId,
				TradePrice = message.YesPrice.ParseKalshiDecimal("trade price"),
				TradeVolume = message.TradeVolume.ParseKalshiDecimal("trade volume"),
				OriginSide = message.TakerBookSide?.ToStockSharp(),
				OriginalTransactionId = subscription.TransactionId,
			}, cancellationToken);
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription removed = null;
		using (_sync.EnterScope())
			_level1Subscriptions.Remove(transactionId, out removed);
		if (removed is not null)
			await SocketClient.UnsubscribeAsync(KalshiSocketChannels.Ticker,
				removed.Ticker, cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription removed = null;
		using (_sync.EnterScope())
			_depthSubscriptions.Remove(transactionId, out removed);
		if (removed is not null)
			await SocketClient.UnsubscribeAsync(
				KalshiSocketChannels.OrderBookDelta, removed.Ticker,
				cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription removed = null;
		using (_sync.EnterScope())
			_tickSubscriptions.Remove(transactionId, out removed);
		if (removed is not null)
			await SocketClient.UnsubscribeAsync(KalshiSocketChannels.Trade,
				removed.Ticker, cancellationToken);
	}

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
