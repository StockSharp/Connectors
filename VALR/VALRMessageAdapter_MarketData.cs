namespace StockSharp.VALR;

public partial class VALRMessageAdapter
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
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.Valr))
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
			}.TryAdd(Level1Fields.State, SecurityStates.Trading),
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
				"VALR does not expose historical Level1 events.");

		var market = GetMarket(mdMsg.SecurityId);
		var summary = await RestClient.GetMarketSummaryAsync(market.Symbol,
			cancellationToken);
		if (summary is null)
			throw new InvalidDataException(
				$"VALR returned no market summary for '{market.Symbol}'.");
		await SendMarketSummaryAsync(market, summary, mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		EnsureStreamingReady();
		var key = new StreamKey(StreamTypes.MarketSummary, market.Symbol);
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
				await TradeSocketClient.SubscribeMarketSummaryAsync(market.Symbol,
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
				"VALR does not expose historical order-book events.");

		var market = GetMarket(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? 100).Min(1000).Max(1);
		var book = await RestClient.GetOrderBookAsync(market.Symbol,
			cancellationToken);
		if (book is null)
			throw new InvalidDataException(
				$"VALR returned no order book for '{market.Symbol}'.");
		await SendBookAsync(market, book, depth, mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		EnsureStreamingReady();
		var key = new StreamKey(StreamTypes.OrderBook, market.Symbol);
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
				await TradeSocketClient.SubscribeOrderBookAsync(market.Symbol,
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
		var maximum = (mdMsg.Count ?? 100).Min(100).Max(1).To<int>();
		var from = mdMsg.From?.ToUniversalTime();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var trades = await RestClient.GetPublicTradesAsync(market.Symbol, new()
		{
			Limit = maximum,
			StartTime = from,
			EndTime = mdMsg.To is null ? null : to,
		}, cancellationToken);
		foreach (var trade in (trades ?? [])
			.Where(trade => trade is not null &&
				(from is null || trade.TradedAt.ToVALRTime(DateTime.MinValue) >= from) &&
				trade.TradedAt.ToVALRTime(DateTime.MaxValue) <= to)
			.OrderBy(static trade => trade.TradedAt,
				StringComparer.Ordinal).TakeLast(maximum))
			await SendPublicTradeAsync(market, trade, mdMsg.TransactionId,
				cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		EnsureStreamingReady();
		var key = new StreamKey(StreamTypes.Trade, market.Symbol);
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
				await TradeSocketClient.SubscribeTradesAsync(market.Symbol,
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
		var period = timeFrame.ToVALRPeriod();
		if (!mdMsg.IsHistoryOnly() && timeFrame != TimeSpan.FromMinutes(1))
			throw new NotSupportedException(
				"VALR realtime candle streams publish one-minute buckets only. Other documented intervals are available through REST history.");
		var now = DateTime.UtcNow;
		var to = (mdMsg.To ?? now).ToUniversalTime();
		if (to > now)
			to = now;
		var count = GetCandleCount(mdMsg, timeFrame, to);
		var from = mdMsg.From?.ToUniversalTime() ??
			to - TimeSpan.FromTicks(timeFrame.Ticks * count);
		var candles = await DownloadCandlesAsync(market, period, timeFrame,
			from, to, count, cancellationToken);
		foreach (var candle in candles)
			await SendCandleAsync(market, candle, timeFrame,
				mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		EnsureStreamingReady();
		var key = new StreamKey(StreamTypes.Candle, market.Symbol);
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
				await TradeSocketClient.SubscribeCandlesAsync(market.Symbol,
					cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeCandlesAsync(mdMsg.TransactionId,
				cancellationToken);
			throw;
		}
	}

	private SecurityMessage CreateSecurity(MarketDefinition market,
		long originalTransactionId)
	{
		var shortName = market.Name.IsEmpty()
			? $"{market.BaseAsset}/{market.QuoteAsset}"
			: market.Name;
		var message = new SecurityMessage
		{
			SecurityId = market.Symbol.ToStockSharp(),
			Name = market.IsFuture ? $"{shortName} Perpetual" : shortName,
			ShortName = shortName,
			SecurityType = market.IsFuture
				? SecurityTypes.Future
				: SecurityTypes.CryptoCurrency,
			UnderlyingSecurityType = market.IsFuture
				? SecurityTypes.CryptoCurrency
				: null,
			Currency = market.QuoteAsset.ToCurrency(),
			PriceStep = market.PriceStep > 0 ? market.PriceStep : null,
			VolumeStep = market.VolumeStep > 0 ? market.VolumeStep : null,
			MinVolume = market.MinimumVolume > 0
				? market.MinimumVolume
				: null,
			MaxVolume = market.MaximumVolume > 0
				? market.MaximumVolume
				: null,
			OriginalTransactionId = originalTransactionId,
		};
		return market.IsFuture
			? message.TryFillUnderlyingId(market.BaseAsset)
			: message;
	}

	private async ValueTask<VALRCandle[]> DownloadCandlesAsync(
		MarketDefinition market, int period, TimeSpan timeFrame, DateTime from,
		DateTime to, int count, CancellationToken cancellationToken)
	{
		var values = new SortedDictionary<DateTime, VALRCandle>();
		var cursor = to;
		while (values.Count < count && cursor >= from)
		{
			var pageCount = (count - values.Count).Min(300).Max(1);
			var pageFrom = cursor - TimeSpan.FromTicks(
				timeFrame.Ticks * (pageCount - 1L));
			if (pageFrom < from)
				pageFrom = from;
			var page = await RestClient.GetCandlesAsync(market.Symbol, new()
			{
				PeriodSeconds = period,
				StartTime = new DateTimeOffset(pageFrom).ToUnixTimeSeconds(),
				EndTime = new DateTimeOffset(cursor).ToUnixTimeSeconds(),
				Limit = pageCount,
			}, cancellationToken);
			foreach (var candle in page ?? [])
			{
				if (candle is null)
					continue;
				var openTime = candle.StartTime.ToVALRTime(DateTime.MinValue);
				if (openTime >= from && openTime <= to)
					values[openTime] = candle;
			}
			if (pageFrom <= from || page is not { Length: > 0 })
				break;
			cursor = pageFrom.AddSeconds(-1);
		}
		return [.. values.Values.TakeLast(count)];
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(transactionId, out subscription))
				release = ReleaseReference(_streamReferences,
					new(StreamTypes.MarketSummary, subscription.Symbol));
		if (release && _tradeSocketClient is not null)
			await TradeSocketClient.UnsubscribeMarketSummaryAsync(
				subscription.Symbol, cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_depthSubscriptions.Remove(transactionId, out subscription))
				release = ReleaseReference(_streamReferences,
					new(StreamTypes.OrderBook, subscription.Symbol));
		if (release && _tradeSocketClient is not null)
			await TradeSocketClient.UnsubscribeOrderBookAsync(subscription.Symbol,
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
					new(StreamTypes.Trade, subscription.Symbol));
		if (release && _tradeSocketClient is not null)
			await TradeSocketClient.UnsubscribeTradesAsync(subscription.Symbol,
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
					new(StreamTypes.Candle, subscription.Symbol));
		if (release && _tradeSocketClient is not null)
			await TradeSocketClient.UnsubscribeCandlesAsync(subscription.Symbol,
				cancellationToken);
	}

	private async ValueTask OnSocketMarketSummaryAsync(
		VALRSocketMarketSummary update, CancellationToken cancellationToken)
	{
		if (update?.Data is null || update.CurrencyPair.IsEmpty())
			return;
		var market = GetMarket(update.CurrencyPair);
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(market.Symbol))];
		foreach (var subscription in subscriptions)
			await SendMarketSummaryAsync(market, update.Data, subscription.Key,
				cancellationToken);
	}

	private async ValueTask OnSocketOrderBookAsync(VALRSocketOrderBook update,
		CancellationToken cancellationToken)
	{
		if (update?.Data is null || update.CurrencyPair.IsEmpty())
			return;
		var market = GetMarket(update.CurrencyPair);
		KeyValuePair<long, DepthSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(market.Symbol))];
		foreach (var subscription in subscriptions)
			await SendBookAsync(market, update.Data, subscription.Value.Depth,
				subscription.Key, cancellationToken);
	}

	private async ValueTask OnSocketTradeAsync(VALRSocketTrade update,
		CancellationToken cancellationToken)
	{
		if (update?.Data is null || update.CurrencyPair.IsEmpty())
			return;
		var market = GetMarket(update.CurrencyPair);
		KeyValuePair<long, MarketSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _tickSubscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(market.Symbol))];
		foreach (var subscription in subscriptions)
			await SendPublicTradeAsync(market, update.Data, subscription.Key,
				cancellationToken);
	}

	private async ValueTask OnSocketCandleAsync(VALRSocketCandle update,
		CancellationToken cancellationToken)
	{
		if (update?.Data is null || update.CurrencyPair.IsEmpty())
			return;
		var market = GetMarket(update.CurrencyPair);
		KeyValuePair<long, CandleSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _candleSubscriptions.Where(pair =>
				pair.Value.Symbol.EqualsIgnoreCase(market.Symbol))];
		foreach (var subscription in subscriptions)
			await SendCandleAsync(market, update.Data,
				subscription.Value.TimeFrame, subscription.Key, cancellationToken);
	}

	private ValueTask SendMarketSummaryAsync(MarketDefinition market,
		VALRMarketSummary summary, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = summary.Created.ToVALRTime(CurrentTime),
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.HighPrice, summary.HighPrice)
		.TryAdd(Level1Fields.LowPrice, summary.LowPrice)
		.TryAdd(Level1Fields.LastTradePrice, summary.LastTradedPrice)
		.TryAdd(Level1Fields.BestBidPrice, summary.BidPrice)
		.TryAdd(Level1Fields.BestAskPrice, summary.AskPrice)
		.TryAdd(Level1Fields.SettlementPrice,
			summary.MarkPrice > 0 ? summary.MarkPrice : null)
		.TryAdd(Level1Fields.Volume, summary.BaseVolume)
		.TryAdd(Level1Fields.Change, summary.ChangeFromPrevious),
			cancellationToken);

	private ValueTask SendBookAsync(MarketDefinition market,
		VALROrderBook book, int depth, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = book.LastChange.ToVALRTime(CurrentTime),
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = ToQuotes(book.Bids, false, depth),
			Asks = ToQuotes(book.Asks, true, depth),
			SeqNum = book.SequenceNumber,
		}, cancellationToken);

	private ValueTask SendPublicTradeAsync(MarketDefinition market,
		VALRPublicTrade trade, long transactionId,
		CancellationToken cancellationToken)
	{
		if (trade is null || trade.Price <= 0 || trade.Quantity <= 0)
			return default;
		var timestamp = trade.TradedAt.ToVALRTime(CurrentTime);
		var identity = !trade.Id.IsEmpty()
			? trade.Id
			: trade.SequenceId > 0
				? trade.SequenceId.ToString(CultureInfo.InvariantCulture)
				: $"{market.Symbol}:{timestamp.Ticks}:{trade.Price.ToWire()}:{trade.Quantity.ToWire()}:{trade.TakerSide}";
		if (!AddPublicTrade(identity, transactionId))
			return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = timestamp,
			OriginalTransactionId = transactionId,
			TradeId = trade.SequenceId > 0 ? trade.SequenceId : null,
			TradeStringId = identity,
			TradePrice = trade.Price,
			TradeVolume = trade.Quantity,
			OriginSide = trade.TakerSide.ToStockSharp(),
		}, cancellationToken);
	}

	private ValueTask SendCandleAsync(MarketDefinition market,
		VALRCandle candle, TimeSpan timeFrame, long transactionId,
		CancellationToken cancellationToken)
	{
		var openTime = candle.StartTime.ToVALRTime(CurrentTime);
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = market.Symbol.ToStockSharp(),
			OpenTime = openTime,
			CloseTime = openTime + timeFrame,
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.Volume,
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = CandleStates.Finished,
		}, cancellationToken);
	}

	private static QuoteChange[] ToQuotes(IEnumerable<VALROrderBookLevel> levels,
		bool isAsk, int depth)
	{
		var values = (levels ?? []).Where(static level =>
			level is not null && level.Price > 0 && level.Quantity > 0);
		return [.. (isAsk
			? values.OrderBy(static level => level.Price)
			: values.OrderByDescending(static level => level.Price))
			.Take(depth).Select(static level =>
				new QuoteChange(level.Price, level.Quantity))];
	}

	private static int GetCandleCount(MarketDataMessage message,
		TimeSpan timeFrame, DateTime to)
	{
		if (message.Count is long count)
			return count.Min(10000).Max(1).To<int>();
		if (message.From is DateTime from && to > from)
			return ((to - from.ToUniversalTime()).Ticks / timeFrame.Ticks + 1)
				.Min(10000L).Max(1L).To<int>();
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
