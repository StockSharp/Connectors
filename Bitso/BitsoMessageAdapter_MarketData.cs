namespace StockSharp.Bitso;

public partial class BitsoMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var requestedBook = lookupMsg.SecurityId.SecurityCode.IsEmpty()
			? null
			: lookupMsg.SecurityId.SecurityCode.NormalizeBook();
		MarketDefinition[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values];

		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var market in markets.OrderBy(static market => market.Book,
			StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.Bitso))
				continue;
			if (!requestedBook.IsEmpty() &&
				!requestedBook.EqualsIgnoreCase(market.Book))
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
			}.TryAdd(Level1Fields.State, SecurityStates.Trading), cancellationToken);
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
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"Bitso does not expose historical Level1 events.");

		var book = GetMarket(mdMsg.SecurityId).Book;
		await SendLevel1SnapshotAsync(book, mdMsg.TransactionId, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var ordersKey = new StreamKey(BitsoWsChannels.Orders, book);
		var tradesKey = new StreamKey(BitsoWsChannels.Trades, book);
		bool subscribeOrders;
		bool subscribeTrades;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new() { Book = book });
			subscribeOrders = AddReference(_streamReferences, ordersKey);
			subscribeTrades = AddReference(_streamReferences, tradesKey);
		}
		try
		{
			if (subscribeOrders)
				await WsClient.SubscribeOrdersAsync(book, cancellationToken);
			if (subscribeTrades)
				await WsClient.SubscribeTradesAsync(book, cancellationToken);
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
			await UnsubscribeDepthAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"Bitso does not expose historical order-book events.");

		var book = GetMarket(mdMsg.SecurityId).Book;
		var depth = (mdMsg.MaxDepth ?? 20).Min(20).Max(1);
		var snapshot = await RestClient.GetOrderBookAsync(new()
		{
			Book = book,
			IsAggregate = true,
		}, cancellationToken);
		await SendDepthAsync(book, snapshot.UpdatedAt.ToUtcDateTime(CurrentTime),
			ToQuotes(snapshot.Bids, false, depth), ToQuotes(snapshot.Asks, true, depth),
			mdMsg.TransactionId, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var key = new StreamKey(BitsoWsChannels.Orders, book);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Book = book,
				Depth = depth,
			});
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await WsClient.SubscribeOrdersAsync(book, cancellationToken);
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
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var book = GetMarket(mdMsg.SecurityId).Book;
		var count = (mdMsg.Count ?? 100).Min(100).Max(1).To<int>();
		var from = mdMsg.From?.ToUniversalTime();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var trades = await RestClient.GetTradesAsync(new()
		{
			Book = book,
			Limit = count,
		}, cancellationToken);
		foreach (var trade in (trades ?? []).Where(trade =>
		{
			var time = trade.CreatedAt.ToUtcDateTime(DateTime.MinValue);
			return time != DateTime.MinValue &&
				(from is null || time >= from.Value) && time <= to;
		}).OrderBy(trade => trade.CreatedAt.ToUtcDateTime(DateTime.MinValue)))
			await SendPublicTradeAsync(book, trade, mdMsg.TransactionId,
				cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		var key = new StreamKey(BitsoWsChannels.Trades, book);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new() { Book = book });
			subscribe = AddReference(_streamReferences, key);
		}
		try
		{
			if (subscribe)
				await WsClient.SubscribeTradesAsync(book, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		catch
		{
			await UnsubscribeTicksAsync(mdMsg.TransactionId, cancellationToken);
			throw;
		}
	}

	private SecurityMessage CreateSecurity(MarketDefinition market,
		long originalTransactionId)
		=> new()
		{
			SecurityId = market.Book.ToStockSharp(),
			Name = $"{market.Major}/{market.Minor}",
			ShortName = $"{market.Major}/{market.Minor}",
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = market.Minor.ToCurrency(),
			PriceStep = market.PriceStep > 0 ? market.PriceStep : null,
			MinVolume = market.MinimumAmount > 0 ? market.MinimumAmount : null,
			MaxVolume = market.MaximumAmount > 0 ? market.MaximumAmount : null,
			OriginalTransactionId = originalTransactionId,
		};

	private async ValueTask SendLevel1SnapshotAsync(string book, long transactionId,
		CancellationToken cancellationToken)
	{
		var ticker = await RestClient.GetTickerAsync(new() { Book = book },
			cancellationToken);
		await SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = book.ToStockSharp(),
			ServerTime = ticker.CreatedAt.ToUtcDateTime(CurrentTime),
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, ticker.Bid)
		.TryAdd(Level1Fields.BestAskPrice, ticker.Ask)
		.TryAdd(Level1Fields.LastTradePrice, ticker.Last)
		.TryAdd(Level1Fields.HighPrice, ticker.High)
		.TryAdd(Level1Fields.LowPrice, ticker.Low)
		.TryAdd(Level1Fields.Volume, ticker.Volume)
		.TryAdd(Level1Fields.VWAP, ticker.VolumeWeightedPrice)
		.TryAdd(Level1Fields.Change, ticker.Change24Hours), cancellationToken);
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		bool releaseOrders = false;
		bool releaseTrades = false;
		using (_sync.EnterScope())
			if (_level1Subscriptions.Remove(transactionId, out subscription))
			{
				releaseOrders = ReleaseReference(_streamReferences,
					new(BitsoWsChannels.Orders, subscription.Book));
				releaseTrades = ReleaseReference(_streamReferences,
					new(BitsoWsChannels.Trades, subscription.Book));
			}
		if (releaseOrders)
			await WsClient.ReleaseOrdersAsync(subscription.Book, cancellationToken);
		if (releaseTrades)
			await WsClient.ReleaseTradesAsync(subscription.Book, cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_depthSubscriptions.Remove(transactionId, out subscription))
				release = ReleaseReference(_streamReferences,
					new(BitsoWsChannels.Orders, subscription.Book));
		if (release)
			await WsClient.ReleaseOrdersAsync(subscription.Book, cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var release = false;
		using (_sync.EnterScope())
			if (_tickSubscriptions.Remove(transactionId, out subscription))
				release = ReleaseReference(_streamReferences,
					new(BitsoWsChannels.Trades, subscription.Book));
		if (release)
			await WsClient.ReleaseTradesAsync(subscription.Book, cancellationToken);
	}

	private async ValueTask OnWebSocketTradeAsync(string book, BitsoWsTrade trade,
		CancellationToken cancellationToken)
	{
		if (book.IsEmpty() || trade is null)
			return;
		book = book.NormalizeBook();
		if (!AddPublicTrade(book, trade.TradeId))
			return;
		KeyValuePair<long, MarketSubscription>[] tickSubscriptions;
		KeyValuePair<long, MarketSubscription>[] level1Subscriptions;
		using (_sync.EnterScope())
		{
			tickSubscriptions = [.. _tickSubscriptions.Where(pair =>
				pair.Value.Book.EqualsIgnoreCase(book))];
			level1Subscriptions = [.. _level1Subscriptions.Where(pair =>
				pair.Value.Book.EqualsIgnoreCase(book))];
		}
		var serverTime = trade.CreatedAt > 0
			? trade.CreatedAt.FromMilliseconds()
			: CurrentTime;
		foreach (var pair in tickSubscriptions)
			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = book.ToStockSharp(),
				ServerTime = serverTime,
				OriginalTransactionId = pair.Key,
				TradeStringId = trade.TradeId,
				TradePrice = trade.Price,
				TradeVolume = trade.Amount,
				OriginSide = trade.TakerSide == 0 ? Sides.Buy : Sides.Sell,
			}, cancellationToken);
		foreach (var pair in level1Subscriptions)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = book.ToStockSharp(),
				ServerTime = serverTime,
				OriginalTransactionId = pair.Key,
			}.TryAdd(Level1Fields.LastTradePrice, trade.Price), cancellationToken);
	}

	private async ValueTask OnWebSocketOrdersAsync(string book, BitsoWsOrders orders,
		long sent, CancellationToken cancellationToken)
	{
		if (book.IsEmpty() || orders is null)
			return;
		book = book.NormalizeBook();
		KeyValuePair<long, DepthSubscription>[] depthSubscriptions;
		KeyValuePair<long, MarketSubscription>[] level1Subscriptions;
		using (_sync.EnterScope())
		{
			depthSubscriptions = [.. _depthSubscriptions.Where(pair =>
				pair.Value.Book.EqualsIgnoreCase(book))];
			level1Subscriptions = [.. _level1Subscriptions.Where(pair =>
				pair.Value.Book.EqualsIgnoreCase(book))];
		}
		var serverTime = sent > 0 ? sent.FromMilliseconds() : CurrentTime;
		foreach (var pair in depthSubscriptions)
			await SendDepthAsync(book, serverTime,
				ToQuotes(orders.Bids, false, pair.Value.Depth),
				ToQuotes(orders.Asks, true, pair.Value.Depth), pair.Key,
				cancellationToken);

		var bids = ToQuotes(orders.Bids, false, 1);
		var asks = ToQuotes(orders.Asks, true, 1);
		foreach (var pair in level1Subscriptions)
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = book.ToStockSharp(),
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

	private ValueTask SendDepthAsync(string book, DateTime serverTime,
		QuoteChange[] bids, QuoteChange[] asks, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = book.ToStockSharp(),
			ServerTime = serverTime,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = bids,
			Asks = asks,
		}, cancellationToken);

	private ValueTask SendPublicTradeAsync(string book, BitsoPublicTrade trade,
		long transactionId, CancellationToken cancellationToken)
	{
		_ = AddPublicTrade(book, trade.TradeId);
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = book.ToStockSharp(),
			ServerTime = trade.CreatedAt.ToUtcDateTime(CurrentTime),
			OriginalTransactionId = transactionId,
			TradeStringId = trade.TradeId,
			TradePrice = trade.Price,
			TradeVolume = trade.Amount,
			OriginSide = trade.MakerSide == BitsoSides.Buy ? Sides.Sell : Sides.Buy,
		}, cancellationToken);
	}

	private static QuoteChange[] ToQuotes(IEnumerable<BitsoOrderBookLevel> levels,
		bool isAsk, int depth)
	{
		var grouped = (levels ?? []).Where(static level => level is not null &&
			level.Price > 0 && level.Amount > 0)
			.GroupBy(static level => level.Price)
			.Select(static group => new QuoteChange(group.Key,
				group.Sum(static level => level.Amount)));
		return [.. (isAsk
			? grouped.OrderBy(static quote => quote.Price)
			: grouped.OrderByDescending(static quote => quote.Price)).Take(depth)];
	}

	private static QuoteChange[] ToQuotes(IEnumerable<BitsoWsOrder> levels,
		bool isAsk, int depth)
	{
		var grouped = (levels ?? []).Where(static level => level is not null &&
			level.Price > 0 && level.Amount > 0)
			.GroupBy(static level => level.Price)
			.Select(static group => new QuoteChange(group.Key,
				group.Sum(static level => level.Amount ?? 0m)));
		return [.. (isAsk
			? grouped.OrderBy(static quote => quote.Price)
			: grouped.OrderByDescending(static quote => quote.Price)).Take(depth)];
	}

	private async ValueTask CompleteMarketSubscriptionAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
