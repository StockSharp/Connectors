namespace StockSharp.IndependentReserve;

public partial class IndependentReserveMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		string requestedSymbol = null;
		if (!lookupMsg.SecurityId.SecurityCode.IsEmpty())
		{
			var parts = lookupMsg.SecurityId.SecurityCode.SplitSymbol();
			requestedSymbol = IndependentReserveExtensions.ToSymbol(parts.primary,
				parts.secondary);
		}
		MarketDefinition[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values];

		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;
		foreach (var market in markets.OrderBy(static value => value.Symbol,
			StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.IndependentReserve))
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
				"Independent Reserve does not expose historical Level1 events.");

		var market = GetMarket(mdMsg.SecurityId);
		var summary = await RestClient.GetMarketSummaryAsync(new()
		{
			PrimaryCurrencyCode = market.PrimaryCurrency,
			SecondaryCurrencyCode = market.SecondaryCurrency,
		}, cancellationToken);
		await SendSummaryAsync(market, summary, mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = market.Symbol,
			});
			GetOrCreateBookState(market);
		}
		try
		{
			await AcquireChannelsAsync(
			[
				GetTickerChannel(market.PrimaryCurrency),
				GetOrderBookChannel(market.PrimaryCurrency),
			], cancellationToken);
			await EnsureBookSnapshotAsync(market, cancellationToken);
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
				"Independent Reserve does not expose historical order-book events.");

		var market = GetMarket(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? 100).Min(1000).Max(1);
		if (mdMsg.IsHistoryOnly())
		{
			var snapshot = await RestClient.GetOrderBookAsync(CreateMarketRequest(
				market), cancellationToken);
			await SendRestBookAsync(market, snapshot, depth, mdMsg.TransactionId,
				cancellationToken);
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = market.Symbol,
				Depth = depth,
			});
			GetOrCreateBookState(market);
		}
		try
		{
			await AcquireChannelsAsync(
				[GetOrderBookChannel(market.PrimaryCurrency)], cancellationToken);
			await EnsureBookSnapshotAsync(market, cancellationToken);
			await SendCurrentBookAsync(market.Symbol, depth, mdMsg.TransactionId,
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
		var maximum = (mdMsg.Count ?? 50).Min(50).Max(1).To<int>();
		var from = mdMsg.From?.ToUniversalTime();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var response = await RestClient.GetRecentTradesAsync(new()
		{
			PrimaryCurrencyCode = market.PrimaryCurrency,
			SecondaryCurrencyCode = market.SecondaryCurrency,
			Count = maximum,
		}, cancellationToken);
		foreach (var trade in (response?.Trades ?? []).Where(trade =>
			trade is not null &&
			(from is null || trade.TradeTimestampUtc?.EnsureUtc() >= from) &&
			(trade.TradeTimestampUtc?.EnsureUtc() ?? DateTime.MinValue) <= to)
			.OrderBy(static trade => trade.TradeTimestampUtc))
			await SendPublicTradeAsync(market, trade, mdMsg.TransactionId,
				cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		using (_sync.EnterScope())
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = market.Symbol,
			});
		try
		{
			await AcquireChannelsAsync(
				[GetTickerChannel(market.PrimaryCurrency)], cancellationToken);
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
		if (timeFrame != TimeSpan.FromHours(1))
			throw new NotSupportedException(
				"Independent Reserve exposes one-hour trade summaries only.");
		var now = DateTime.UtcNow;
		var to = (mdMsg.To ?? now).ToUniversalTime().Min(now);
		var maximum = GetCandleCount(mdMsg, timeFrame, to).Min(240);
		var from = mdMsg.From?.ToUniversalTime() ??
			to - TimeSpan.FromHours(maximum);
		var hours = Math.Ceiling((now - from).TotalHours).To<int>()
			.Max(1).Min(240);
		var response = await RestClient.GetTradeHistoryAsync(new()
		{
			PrimaryCurrencyCode = market.PrimaryCurrency,
			SecondaryCurrencyCode = market.SecondaryCurrency,
			Hours = hours,
		}, cancellationToken);
		foreach (var candle in (response?.Items ?? []).Where(item =>
			item is not null && item.StartTimestampUtc.EnsureUtc() >= from &&
			item.StartTimestampUtc.EnsureUtc() <= to)
			.OrderBy(static item => item.StartTimestampUtc).TakeLast(maximum))
			await SendCandleAsync(market, candle, mdMsg.TransactionId,
				cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(mdMsg, cancellationToken);
			return;
		}

		using (_sync.EnterScope())
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = market.Symbol,
				TimeFrame = timeFrame,
			});
		try
		{
			await AcquireChannelsAsync(
				[GetTickerChannel(market.PrimaryCurrency)], cancellationToken);
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
		=> new()
		{
			SecurityId = IndependentReserveExtensions.ToStockSharp(
				market.PrimaryCurrency, market.SecondaryCurrency),
			Name = market.Name.IsEmpty()
				? market.Symbol
				: $"{market.Name} / {market.SecondaryCurrency}",
			ShortName = market.Symbol,
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = market.SecondaryCurrency.ToCurrency(),
			PriceStep = market.PriceStep > 0 ? market.PriceStep : null,
			VolumeStep = market.VolumeStep > 0 ? market.VolumeStep : null,
			OriginalTransactionId = originalTransactionId,
		};

	private static IndependentReserveMarketRequest CreateMarketRequest(
		MarketDefinition market)
		=> new()
		{
			PrimaryCurrencyCode = market.PrimaryCurrency,
			SecondaryCurrencyCode = market.SecondaryCurrency,
		};

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		using (_sync.EnterScope())
			_level1Subscriptions.Remove(transactionId, out subscription);
		if (subscription is null)
			return;
		var market = GetMarket(subscription.Symbol);
		await ReleaseChannelsAsync(
		[
			GetTickerChannel(market.PrimaryCurrency),
			GetOrderBookChannel(market.PrimaryCurrency),
		], cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription subscription = null;
		using (_sync.EnterScope())
			_depthSubscriptions.Remove(transactionId, out subscription);
		if (subscription is null)
			return;
		var market = GetMarket(subscription.Symbol);
		await ReleaseChannelsAsync(
			[GetOrderBookChannel(market.PrimaryCurrency)], cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		using (_sync.EnterScope())
			_tickSubscriptions.Remove(transactionId, out subscription);
		if (subscription is null)
			return;
		var market = GetMarket(subscription.Symbol);
		await ReleaseChannelsAsync(
			[GetTickerChannel(market.PrimaryCurrency)], cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		CandleSubscription subscription = null;
		using (_sync.EnterScope())
			_candleSubscriptions.Remove(transactionId, out subscription);
		if (subscription is null)
			return;
		var market = GetMarket(subscription.Symbol);
		await ReleaseChannelsAsync(
			[GetTickerChannel(market.PrimaryCurrency)], cancellationToken);
	}

	private BookState GetOrCreateBookState(MarketDefinition market)
	{
		if (!_books.TryGetValue(market.Symbol, out var state))
			_books.Add(market.Symbol, state = new()
			{
				Symbol = market.Symbol,
			});
		return state;
	}

	private async ValueTask EnsureBookSnapshotAsync(MarketDefinition market,
		CancellationToken cancellationToken)
	{
		bool isInitialized;
		using (_sync.EnterScope())
			isInitialized = GetOrCreateBookState(market).IsInitialized;
		if (isInitialized)
			return;
		var snapshot = await RestClient.GetOrderBookAsync(CreateMarketRequest(
			market), cancellationToken);
		InitializeBook(market, snapshot);
	}

	private void InitializeBook(MarketDefinition market,
		IndependentReserveOrderBook snapshot)
	{
		if (snapshot is null)
			throw new InvalidDataException(
				$"Independent Reserve returned no order book for '{market.Symbol}'.");
		using (_sync.EnterScope())
		{
			var state = GetOrCreateBookState(market);
			state.Bids.Clear();
			state.Asks.Clear();
			foreach (var item in snapshot.BuyOrders ?? [])
				AddSnapshotOrder(state.Bids, item);
			foreach (var item in snapshot.SellOrders ?? [])
				AddSnapshotOrder(state.Asks, item);
			state.Timestamp = snapshot.CreatedTimestampUtc.EnsureUtc();
			foreach (var update in state.Buffer.Where(value =>
				value.Time > state.Timestamp).OrderBy(static value => value.Nonce))
				ApplyBookEvent(state, market, update.Event, update.Payload,
					update.Time, update.Nonce);
			state.Buffer.Clear();
			state.IsInitialized = true;
		}
	}

	private static void AddSnapshotOrder(IDictionary<Guid, BookOrder> target,
		IndependentReserveOrderBookItem item)
	{
		if (item is null || item.Id == Guid.Empty || item.Price <= 0 ||
			item.Volume <= 0)
			return;
		target[item.Id] = new()
		{
			Id = item.Id,
			Price = item.Price,
			Volume = item.Volume,
		};
	}

	private async ValueTask OnBookEventAsync(
		IndependentReserveSocketEnvelope envelope,
		CancellationToken cancellationToken)
	{
		var payload = envelope.Data?.Payload;
		if (payload is null)
			return;
		var primary = GetChannelPrimary(envelope.Channel);
		var timestamp = envelope.Time.ToIndependentReserveTime(CurrentTime);
		foreach (var market in GetMarketsByPrimary(primary))
		{
			KeyValuePair<long, DepthSubscription>[] depthSubscriptions;
			KeyValuePair<long, MarketSubscription>[] level1Subscriptions;
			using (_sync.EnterScope())
			{
				if (!_books.TryGetValue(market.Symbol, out var state))
					continue;
				if (!state.IsInitialized)
				{
					state.Buffer.Add(new()
					{
						Event = envelope.Event,
						Payload = payload,
						Time = timestamp,
						Nonce = envelope.Nonce,
					});
					continue;
				}
				ApplyBookEvent(state, market, envelope.Event, payload, timestamp,
					envelope.Nonce);
				depthSubscriptions = [.. _depthSubscriptions.Where(pair =>
					pair.Value.Symbol.EqualsIgnoreCase(market.Symbol))];
				level1Subscriptions = [.. _level1Subscriptions.Where(pair =>
					pair.Value.Symbol.EqualsIgnoreCase(market.Symbol))];
			}

			foreach (var subscription in depthSubscriptions)
				await SendCurrentBookAsync(market.Symbol,
					subscription.Value.Depth, subscription.Key, cancellationToken);
			foreach (var subscription in level1Subscriptions)
				await SendBookLevel1Async(market.Symbol, subscription.Key,
					cancellationToken);
		}
	}

	private static void ApplyBookEvent(BookState state, MarketDefinition market,
		IndependentReserveSocketEvents type,
		IndependentReserveSocketPayload payload, DateTime timestamp, long nonce)
	{
		var id = payload.OrderGuid ?? Guid.Empty;
		if (id == Guid.Empty)
			return;
		switch (type)
		{
			case IndependentReserveSocketEvents.NewOrder:
				if (payload.OrderType is null || payload.Volume is not > 0)
					return;
				var price = payload.Price.GetPrice(market.SecondaryCurrency);
				if (price is not > 0)
					return;
				var order = new BookOrder
				{
					Id = id,
					Price = price.Value,
					Volume = payload.Volume.Value,
				};
				(payload.OrderType.Value.IsBuy()
					? state.Bids
					: state.Asks)[id] = order;
				break;
			case IndependentReserveSocketEvents.OrderChanged:
				if (state.Bids.TryGetValue(id, out var bid))
					UpdateBookOrder(state.Bids, bid, payload.Volume);
				else if (state.Asks.TryGetValue(id, out var ask))
					UpdateBookOrder(state.Asks, ask, payload.Volume);
				break;
			case IndependentReserveSocketEvents.OrderCanceled:
				state.Bids.Remove(id);
				state.Asks.Remove(id);
				break;
		}
		state.Timestamp = timestamp;
		state.Nonce = nonce;
	}

	private static void UpdateBookOrder(IDictionary<Guid, BookOrder> orders,
		BookOrder order, decimal? volume)
	{
		if (volume is not > 0)
			orders.Remove(order.Id);
		else
			order.Volume = volume.Value;
	}

	private async ValueTask OnTradeEventAsync(
		IndependentReserveSocketEnvelope envelope,
		CancellationToken cancellationToken)
	{
		var trade = envelope.Data?.Payload;
		if (trade?.TradeGuid is not Guid tradeId || tradeId == Guid.Empty ||
			trade.Volume is not > 0 || trade.Price is null)
			return;
		var primary = GetChannelPrimary(envelope.Channel);
		var timestamp = (trade.TradeDate ??
			envelope.Time.ToIndependentReserveTime(CurrentTime)).EnsureUtc();
		foreach (var market in GetMarketsByPrimary(primary))
		{
			var price = trade.Price.GetPrice(market.SecondaryCurrency);
			if (price is not > 0)
				continue;
			KeyValuePair<long, MarketSubscription>[] tickSubscriptions;
			KeyValuePair<long, MarketSubscription>[] level1Subscriptions;
			KeyValuePair<long, CandleSubscription>[] candleSubscriptions;
			using (_sync.EnterScope())
			{
				tickSubscriptions = [.. _tickSubscriptions.Where(pair =>
					pair.Value.Symbol.EqualsIgnoreCase(market.Symbol))];
				level1Subscriptions = [.. _level1Subscriptions.Where(pair =>
					pair.Value.Symbol.EqualsIgnoreCase(market.Symbol))];
				candleSubscriptions = [.. _candleSubscriptions.Where(pair =>
					pair.Value.Symbol.EqualsIgnoreCase(market.Symbol))];
			}
			foreach (var subscription in tickSubscriptions)
				await SendSocketTradeAsync(market, tradeId, timestamp, price.Value,
					trade.Volume.Value, trade.Side, subscription.Key,
					cancellationToken);
			foreach (var subscription in level1Subscriptions)
				await SendTradeLevel1Async(market, timestamp, price.Value,
					trade.Volume.Value, subscription.Key, cancellationToken);
			foreach (var subscription in candleSubscriptions)
				await UpdateLiveCandleAsync(market, timestamp, price.Value,
					trade.Volume.Value, subscription.Key, subscription.Value,
					cancellationToken);
		}
	}

	private ValueTask SendSummaryAsync(MarketDefinition market,
		IndependentReserveMarketSummary summary, long transactionId,
		CancellationToken cancellationToken)
	{
		if (summary is null)
			throw new InvalidDataException(
				$"Independent Reserve returned no summary for '{market.Symbol}'.");
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = IndependentReserveExtensions.ToStockSharp(
				market.PrimaryCurrency, market.SecondaryCurrency),
			ServerTime = summary.CreatedTimestampUtc.EnsureUtc(),
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, summary.BestBidPrice)
		.TryAdd(Level1Fields.BestAskPrice, summary.BestAskPrice)
		.TryAdd(Level1Fields.LastTradePrice, summary.LastPrice)
		.TryAdd(Level1Fields.HighPrice, summary.DayHighestPrice)
		.TryAdd(Level1Fields.LowPrice, summary.DayLowestPrice)
		.TryAdd(Level1Fields.AveragePrice, summary.DayAveragePrice)
		.TryAdd(Level1Fields.Volume, summary.DayVolume)
		.TryAdd(Level1Fields.State, SecurityStates.Trading), cancellationToken);
	}

	private ValueTask SendRestBookAsync(MarketDefinition market,
		IndependentReserveOrderBook book, int depth, long transactionId,
		CancellationToken cancellationToken)
	{
		if (book is null)
			throw new InvalidDataException(
				$"Independent Reserve returned no order book for '{market.Symbol}'.");
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = IndependentReserveExtensions.ToStockSharp(
				market.PrimaryCurrency, market.SecondaryCurrency),
			ServerTime = book.CreatedTimestampUtc.EnsureUtc(),
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = AggregateBook(book.BuyOrders, true, depth),
			Asks = AggregateBook(book.SellOrders, false, depth),
		}, cancellationToken);
	}

	private ValueTask SendCurrentBookAsync(string symbol, int depth,
		long transactionId, CancellationToken cancellationToken)
	{
		QuoteChange[] bids;
		QuoteChange[] asks;
		DateTime timestamp;
		long nonce;
		using (_sync.EnterScope())
		{
			if (!_books.TryGetValue(symbol, out var state) ||
				!state.IsInitialized)
				return default;
			bids = AggregateBook(state.Bids.Values, true, depth);
			asks = AggregateBook(state.Asks.Values, false, depth);
			timestamp = state.Timestamp;
			nonce = state.Nonce;
		}
		var market = GetMarket(symbol);
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = IndependentReserveExtensions.ToStockSharp(
				market.PrimaryCurrency, market.SecondaryCurrency),
			ServerTime = timestamp,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			SeqNum = nonce,
			Bids = bids,
			Asks = asks,
		}, cancellationToken);
	}

	private ValueTask SendBookLevel1Async(string symbol, long transactionId,
		CancellationToken cancellationToken)
	{
		BookOrder bid;
		BookOrder ask;
		DateTime timestamp;
		using (_sync.EnterScope())
		{
			if (!_books.TryGetValue(symbol, out var state) ||
				!state.IsInitialized)
				return default;
			bid = state.Bids.Values.OrderByDescending(static value => value.Price)
				.FirstOrDefault();
			ask = state.Asks.Values.OrderBy(static value => value.Price)
				.FirstOrDefault();
			timestamp = state.Timestamp;
		}
		var market = GetMarket(symbol);
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = IndependentReserveExtensions.ToStockSharp(
				market.PrimaryCurrency, market.SecondaryCurrency),
			ServerTime = timestamp,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, bid?.Price)
		.TryAdd(Level1Fields.BestBidVolume, bid?.Volume)
		.TryAdd(Level1Fields.BestAskPrice, ask?.Price)
		.TryAdd(Level1Fields.BestAskVolume, ask?.Volume), cancellationToken);
	}

	private ValueTask SendTradeLevel1Async(MarketDefinition market,
		DateTime timestamp, decimal price, decimal volume, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = IndependentReserveExtensions.ToStockSharp(
				market.PrimaryCurrency, market.SecondaryCurrency),
			ServerTime = timestamp,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, price)
		.TryAdd(Level1Fields.LastTradeVolume, volume), cancellationToken);

	private ValueTask SendPublicTradeAsync(MarketDefinition market,
		IndependentReservePublicTrade trade, long transactionId,
		CancellationToken cancellationToken)
	{
		if (trade is null || trade.TradeId == Guid.Empty || trade.Price <= 0 ||
			trade.Volume <= 0 || !AddPublicTrade(trade.TradeId, transactionId))
			return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = IndependentReserveExtensions.ToStockSharp(
				market.PrimaryCurrency, market.SecondaryCurrency),
			ServerTime = trade.TradeTimestampUtc?.EnsureUtc() ?? CurrentTime,
			OriginalTransactionId = transactionId,
			TradeStringId = trade.TradeId.ToString("D"),
			TradePrice = trade.Price,
			TradeVolume = trade.Volume,
			OriginSide = trade.Taker?.ToStockSharp(),
		}, cancellationToken);
	}

	private ValueTask SendSocketTradeAsync(MarketDefinition market, Guid tradeId,
		DateTime timestamp, decimal price, decimal volume,
		IndependentReserveSocketSides? side, long transactionId,
		CancellationToken cancellationToken)
	{
		if (!AddPublicTrade(tradeId, transactionId))
			return default;
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = IndependentReserveExtensions.ToStockSharp(
				market.PrimaryCurrency, market.SecondaryCurrency),
			ServerTime = timestamp,
			OriginalTransactionId = transactionId,
			TradeStringId = tradeId.ToString("D"),
			TradePrice = price,
			TradeVolume = volume,
			OriginSide = side?.ToStockSharp(),
		}, cancellationToken);
	}

	private ValueTask SendCandleAsync(MarketDefinition market,
		IndependentReserveHistoryItem candle, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = IndependentReserveExtensions.ToStockSharp(
				market.PrimaryCurrency, market.SecondaryCurrency),
			OpenTime = candle.StartTimestampUtc.EnsureUtc(),
			CloseTime = candle.EndTimestampUtc.EnsureUtc(),
			OpenPrice = candle.Open,
			HighPrice = candle.High,
			LowPrice = candle.Low,
			ClosePrice = candle.Close,
			TotalVolume = candle.PrimaryVolume,
			TotalTicks = candle.NumberOfTrades.Min(int.MaxValue).To<int>(),
			TypedArg = TimeSpan.FromHours(1),
			OriginalTransactionId = transactionId,
			State = CandleStates.Finished,
		}, cancellationToken);

	private async ValueTask UpdateLiveCandleAsync(MarketDefinition market,
		DateTime timestamp, decimal price, decimal volume, long transactionId,
		CandleSubscription subscription, CancellationToken cancellationToken)
	{
		TimeFrameCandleMessage finished = null;
		TimeFrameCandleMessage active;
		using (_sync.EnterScope())
		{
			var openTime = timestamp.AlignHour();
			if (subscription.OpenTime != default && openTime < subscription.OpenTime)
				return;
			if (subscription.OpenTime == default)
				StartCandle(subscription, openTime, price, volume);
			else if (openTime > subscription.OpenTime)
			{
				finished = CreateLiveCandle(market, transactionId, subscription,
					CandleStates.Finished);
				StartCandle(subscription, openTime, price, volume);
			}
			else
			{
				subscription.High = subscription.High.Max(price);
				subscription.Low = subscription.Low.Min(price);
				subscription.Close = price;
				subscription.Volume += volume;
			}
			active = CreateLiveCandle(market, transactionId, subscription,
				CandleStates.Active);
		}
		if (finished is not null)
			await SendOutMessageAsync(finished, cancellationToken);
		await SendOutMessageAsync(active, cancellationToken);
	}

	private static void StartCandle(CandleSubscription subscription,
		DateTime openTime, decimal price, decimal volume)
	{
		subscription.OpenTime = openTime;
		subscription.Open = price;
		subscription.High = price;
		subscription.Low = price;
		subscription.Close = price;
		subscription.Volume = volume;
	}

	private static TimeFrameCandleMessage CreateLiveCandle(
		MarketDefinition market, long transactionId,
		CandleSubscription subscription, CandleStates state)
		=> new()
		{
			SecurityId = IndependentReserveExtensions.ToStockSharp(
				market.PrimaryCurrency, market.SecondaryCurrency),
			OpenTime = subscription.OpenTime,
			CloseTime = subscription.OpenTime + subscription.TimeFrame,
			OpenPrice = subscription.Open,
			HighPrice = subscription.High,
			LowPrice = subscription.Low,
			ClosePrice = subscription.Close,
			TotalVolume = subscription.Volume,
			TypedArg = subscription.TimeFrame,
			OriginalTransactionId = transactionId,
			State = state,
		};

	private static QuoteChange[] AggregateBook(
		IEnumerable<IndependentReserveOrderBookItem> orders, bool isBid, int depth)
		=> AggregateBook((orders ?? []).Where(static item => item is not null &&
			item.Price > 0 && item.Volume > 0).Select(static item => new BookOrder
			{
				Id = item.Id,
				Price = item.Price,
				Volume = item.Volume,
			}), isBid, depth);

	private static QuoteChange[] AggregateBook(IEnumerable<BookOrder> orders,
		bool isBid, int depth)
	{
		var levels = (orders ?? []).Where(static item => item.Price > 0 &&
			item.Volume > 0).GroupBy(static item => item.Price).Select(static group =>
				new QuoteChange(group.Key, group.Sum(static item => item.Volume)));
		return [.. (isBid
			? levels.OrderByDescending(static item => item.Price)
			: levels.OrderBy(static item => item.Price)).Take(depth)];
	}

	private void MarkBooksUninitialized()
	{
		using (_sync.EnterScope())
			foreach (var state in _books.Values)
			{
				state.Bids.Clear();
				state.Asks.Clear();
				state.Buffer.Clear();
				state.IsInitialized = false;
				state.Nonce = 0;
			}
	}

	private async ValueTask RefreshBooksAsync(
		CancellationToken cancellationToken)
	{
		MarketDefinition[] markets;
		using (_sync.EnterScope())
		{
			var symbols = _level1Subscriptions.Values.Select(static value =>
				value.Symbol).Concat(_depthSubscriptions.Values.Select(static value =>
					value.Symbol)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
			markets = [.. symbols.Select(symbol => _markets[symbol])];
		}
		foreach (var market in markets)
		{
			var snapshot = await RestClient.GetOrderBookAsync(CreateMarketRequest(
				market), cancellationToken);
			InitializeBook(market, snapshot);
		}
	}

	private static int GetCandleCount(MarketDataMessage message,
		TimeSpan timeFrame, DateTime to)
	{
		if (message.Count is long count)
			return count.Min(240).Max(1).To<int>();
		if (message.From is DateTime from && to > from)
			return ((to - from.ToUniversalTime()).Ticks / timeFrame.Ticks + 1)
				.Min(240L).Max(1L).To<int>();
		return 240;
	}

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
