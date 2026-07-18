namespace StockSharp.Bullish;

public partial class BullishMessageAdapter
{
	private async ValueTask RefreshMarketsAsync(CancellationToken cancellationToken)
	{
		var markets = await RestClient.GetMarketsAsync(cancellationToken) ?? [];
		using (_sync.EnterScope())
		{
			_markets.Clear();
			foreach (var market in markets.Where(static market =>
				market?.Symbol.IsEmpty() == false && market.MarketType.IsEmpty() == false))
				_markets[market.Symbol] = market;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;
		BullishMarket[] markets;
		using (_sync.EnterScope())
			markets = [.. _markets.Values];

		foreach (var market in markets.OrderBy(static market => market.Symbol))
		{
			var section = market.MarketType.ToSectionByMarketType();
			if (!IsSectionEnabled(section) || !market.IsMarketEnabled)
				continue;
			var securityType = market.MarketType.ToSecurityType();
			if (securityTypes.Count > 0 && !securityTypes.Contains(securityType))
				continue;

			var underlying = market.UnderlyingBaseSymbol.IsEmpty(market.BaseSymbol)?.ToUpperInvariant();
			var security = new SecurityMessage
			{
				SecurityId = market.Symbol.ToStockSharp(section),
				Name = market.Symbol,
				SecurityType = securityType,
				OriginalTransactionId = lookupMsg.TransactionId,
				PriceStep = market.TickSize.ToDecimal() ?? market.PricePrecision.PrecisionToStep(),
				Decimals = market.PricePrecision,
				VolumeStep = market.QuantityPrecision.PrecisionToStep(),
				MinVolume = market.MinimumQuantity.ToDecimal(),
				MaxVolume = market.MaximumQuantity.ToDecimal(),
				Multiplier = section == BullishSections.Derivatives
					? market.ContractMultiplier ?? 1m
					: null,
				ExpiryDate = market.ExpiryDateTime.ToUtcDateTime(),
				Strike = market.OptionStrikePrice.ToDecimal(),
				OptionType = market.OptionType.ToOptionType(),
			}.TryFillUnderlyingId(underlying);
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

		var symbol = GetSymbol(mdMsg.SecurityId);
		var section = ResolveSection(mdMsg.SecurityId);
		var isTickStream = GetMarketType(symbol).EqualsIgnoreCase("PERPETUAL");
		if (isTickStream)
		{
			var tick = await RestClient.GetTickAsync(symbol, cancellationToken);
			await SendLevel1Async(tick, symbol, section, mdMsg.TransactionId,
				cancellationToken);
		}
		else
		{
			var book = await RestClient.GetOrderBookAsync(symbol, cancellationToken);
			SetBookSnapshot(symbol, book);
			var trade = (await RestClient.GetRecentTradesAsync(symbol, cancellationToken) ?? [])
				.OrderByDescending(static item => item.CreatedAtTimestamp.ToUtcTime(
					item.CreatedAtDateTime))
				.FirstOrDefault();
			await SendLevel1SnapshotAsync(book, trade, symbol, section, mdMsg.TransactionId,
				cancellationToken);
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		var subscribeTick = false;
		var subscribeBook = false;
		var subscribeTrades = false;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				Section = section,
				IsTickStream = isTickStream,
			});
			if (isTickStream)
				subscribeTick = AddReference(_streamReferences,
					(BullishWsKinds.Tick, "tick", symbol));
			else
			{
				subscribeBook = AddReference(_streamReferences,
					(BullishWsKinds.OrderBook, "l2Orderbook", symbol));
				subscribeTrades = AddReference(_streamReferences,
					(BullishWsKinds.Trades, "anonymousTrades", symbol));
			}
		}
		if (subscribeTick)
			await _tickClient.SubscribeAsync("tick", symbol, cancellationToken);
		if (subscribeBook)
			await _bookClient.SubscribeAsync("l2Orderbook", symbol, cancellationToken);
		if (subscribeTrades)
			await _tradeClient.SubscribeAsync("anonymousTrades", symbol, cancellationToken);
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

		var symbol = GetSymbol(mdMsg.SecurityId);
		var section = ResolveSection(mdMsg.SecurityId);
		var depth = NormalizeDepth(mdMsg.MaxDepth);
		var book = await RestClient.GetOrderBookAsync(symbol, cancellationToken);
		SetBookSnapshot(symbol, book);
		await SendBookAsync(book?.Bids, book?.Asks, symbol, section, mdMsg.TransactionId,
			book?.Timestamp.ToUtcTime(book.DateTime) ?? CurrentTime, depth, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		bool subscribe;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				Section = section,
				Depth = depth,
			});
			subscribe = AddReference(_streamReferences,
				(BullishWsKinds.OrderBook, "l2Orderbook", symbol));
		}
		if (subscribe)
			await _bookClient.SubscribeAsync("l2Orderbook", symbol, cancellationToken);
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

		var symbol = GetSymbol(mdMsg.SecurityId);
		var section = ResolveSection(mdMsg.SecurityId);
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var count = (mdMsg.Count ?? 100).Min(1000).Max(1).To<int>();
		var trades = await LoadTradesAsync(symbol, mdMsg.From?.ToUniversalTime(), to, count,
			cancellationToken);
		string lastTradeId = null;
		var lastTime = mdMsg.From?.ToUniversalTime() ?? default;
		foreach (var trade in trades.OrderBy(static trade => trade.CreatedAtTimestamp.ToUtcTime(
			trade.CreatedAtDateTime)))
		{
			var time = trade.CreatedAtTimestamp.ToUtcTime(trade.CreatedAtDateTime);
			await SendTradeAsync(trade, symbol, section, mdMsg.TransactionId, cancellationToken);
			lastTradeId = trade.TradeId;
			lastTime = time;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		bool subscribe;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				Section = section,
				LastTradeId = lastTradeId,
				LastTime = lastTime,
			});
			subscribe = AddReference(_streamReferences,
				(BullishWsKinds.Trades, "anonymousTrades", symbol));
		}
		if (subscribe)
			await _tradeClient.SubscribeAsync("anonymousTrades", symbol, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			await UnsubscribeCandlesAsync(mdMsg.OriginalTransactionId, cancellationToken);
			return;
		}

		var symbol = GetSymbol(mdMsg.SecurityId);
		var section = ResolveSection(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		_ = timeFrame.ToBullishBucket();
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var count = GetCandleCount(mdMsg, timeFrame, to);
		var from = (mdMsg.From?.ToUniversalTime() ?? to - TimeSpan.FromTicks(
			timeFrame.Ticks * Math.Max(0, count - 1))).Align(timeFrame);
		var candles = await LoadCandlesAsync(symbol, timeFrame, from, to, count,
			cancellationToken);
		BullishCandle last = null;
		foreach (var candle in candles.OrderBy(static candle =>
			candle.CreatedAtTimestamp.ToUtcTime(candle.CreatedAtDateTime)))
		{
			var openTime = candle.CreatedAtTimestamp.ToUtcTime(candle.CreatedAtDateTime);
			await SendCandleAsync(candle, symbol, section, timeFrame, mdMsg.TransactionId,
				openTime + timeFrame <= DateTime.UtcNow ? CandleStates.Finished : CandleStates.Active,
				cancellationToken);
			last = candle;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		bool subscribe;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Symbol = symbol,
				Section = section,
				TimeFrame = timeFrame,
				OpenTime = last?.CreatedAtTimestamp.ToUtcTime(last.CreatedAtDateTime) ?? default,
				OpenPrice = last?.Open.ToDecimal() ?? 0m,
				HighPrice = last?.High.ToDecimal() ?? 0m,
				LowPrice = last?.Low.ToDecimal() ?? 0m,
				ClosePrice = last?.Close.ToDecimal() ?? 0m,
				TotalVolume = last?.Volume.ToDecimal() ?? 0m,
				IsInitialized = last is not null,
			});
			subscribe = AddReference(_streamReferences,
				(BullishWsKinds.Trades, "anonymousTrades", symbol));
		}
		if (subscribe)
			await _tradeClient.SubscribeAsync("anonymousTrades", symbol, cancellationToken);
	}

	private async ValueTask<BullishTrade[]> LoadTradesAsync(string symbol, DateTime? from,
		DateTime to, int count, CancellationToken cancellationToken)
	{
		if (from is null)
			return [.. (await RestClient.GetRecentTradesAsync(symbol, cancellationToken) ?? [])
				.Where(trade => trade.CreatedAtTimestamp.ToUtcTime(trade.CreatedAtDateTime) <= to)
				.OrderByDescending(trade => trade.CreatedAtTimestamp.ToUtcTime(trade.CreatedAtDateTime))
				.Take(count)];

		var result = new List<BullishTrade>();
		var earliest = to - TimeSpan.FromDays(90);
		var cursor = from.Value > earliest ? from.Value : earliest;
		while (cursor <= to && result.Count < count)
		{
			var candidate = cursor + TimeSpan.FromDays(7);
			var windowTo = candidate < to ? candidate : to;
			result.AddRange(await RestClient.GetHistoricalTradesAsync(symbol, cursor, windowTo,
				cancellationToken) ?? []);
			if (windowTo >= to)
				break;
			cursor = windowTo.AddMilliseconds(1);
		}
		return [.. result
			.Where(static trade => trade?.TradeId.IsEmpty() == false)
			.GroupBy(static trade => trade.TradeId)
			.Select(static group => group.First())
			.OrderBy(trade => trade.CreatedAtTimestamp.ToUtcTime(trade.CreatedAtDateTime))
			.Take(count)];
	}

	private async ValueTask<BullishCandle[]> LoadCandlesAsync(string symbol, TimeSpan timeFrame,
		DateTime from, DateTime to, int count, CancellationToken cancellationToken)
	{
		var result = new List<BullishCandle>();
		var cursor = from;
		while (cursor <= to && result.Count < count)
		{
			var candidate = cursor + TimeSpan.FromTicks(timeFrame.Ticks * 24);
			var windowTo = candidate < to ? candidate : to;
			result.AddRange(await RestClient.GetCandlesAsync(symbol, timeFrame, cursor, windowTo,
				cancellationToken) ?? []);
			if (windowTo >= to)
				break;
			cursor = windowTo + timeFrame;
		}
		return [.. result
			.Where(static candle => candle is not null)
			.GroupBy(candle => candle.CreatedAtTimestamp.ToUtcTime(candle.CreatedAtDateTime))
			.Select(static group => group.First())
			.OrderBy(candle => candle.CreatedAtTimestamp.ToUtcTime(candle.CreatedAtDateTime))
			.Take(count)];
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		Level1Subscription subscription = null;
		var unsubscribeTick = false;
		var unsubscribeBook = false;
		var unsubscribeTrades = false;
		using (_sync.EnterScope())
		{
			if (_level1Subscriptions.Remove(transactionId, out subscription))
			{
				if (subscription.IsTickStream)
					unsubscribeTick = ReleaseReference(_streamReferences,
						(BullishWsKinds.Tick, "tick", subscription.Symbol));
				else
				{
					unsubscribeBook = ReleaseReference(_streamReferences,
						(BullishWsKinds.OrderBook, "l2Orderbook", subscription.Symbol));
					unsubscribeTrades = ReleaseReference(_streamReferences,
						(BullishWsKinds.Trades, "anonymousTrades", subscription.Symbol));
				}
			}
		}
		if (unsubscribeTick)
			await _tickClient.UnsubscribeAsync("tick", subscription.Symbol, cancellationToken);
		if (unsubscribeBook)
			await _bookClient.UnsubscribeAsync("l2Orderbook", subscription.Symbol,
				cancellationToken);
		if (unsubscribeTrades)
			await _tradeClient.UnsubscribeAsync("anonymousTrades", subscription.Symbol,
				cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_depthSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences,
					(BullishWsKinds.OrderBook, "l2Orderbook", subscription.Symbol));
		}
		if (unsubscribe)
			await _bookClient.UnsubscribeAsync("l2Orderbook", subscription.Symbol,
				cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		TickSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_tickSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences,
					(BullishWsKinds.Trades, "anonymousTrades", subscription.Symbol));
		}
		if (unsubscribe)
			await _tradeClient.UnsubscribeAsync("anonymousTrades", subscription.Symbol,
				cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		CandleSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_candleSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseReference(_streamReferences,
					(BullishWsKinds.Trades, "anonymousTrades", subscription.Symbol));
		}
		if (unsubscribe)
			await _tradeClient.UnsubscribeAsync("anonymousTrades", subscription.Symbol,
				cancellationToken);
	}

	private async ValueTask OnTickAsync(BullishTick tick,
		CancellationToken cancellationToken)
	{
		var symbol = tick?.Symbol?.ToUpperInvariant();
		if (symbol.IsEmpty())
			return;
		var section = ResolveSection(symbol);
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _level1Subscriptions
				.Where(pair => pair.Value.IsTickStream && pair.Value.Section == section &&
					pair.Value.Symbol.EqualsIgnoreCase(symbol))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendLevel1Async(tick, symbol, section, id, cancellationToken);
	}

	private async ValueTask OnTradesAsync(BullishWsTradesData message,
		CancellationToken cancellationToken)
	{
		foreach (var trade in (message?.Trades ?? []).OrderBy(static trade =>
			trade.CreatedAtTimestamp.ToUtcTime(trade.CreatedAtDateTime)))
		{
			var symbol = trade.Symbol.IsEmpty(message.Symbol)?.ToUpperInvariant();
			if (symbol.IsEmpty())
				continue;
			var section = ResolveSection(symbol);
			var time = trade.CreatedAtTimestamp.ToUtcTime(trade.CreatedAtDateTime);
			long[] level1Ids;
			long[] tickIds;
			CandleEmission[] candleEmissions;
			using (_sync.EnterScope())
			{
				level1Ids = [.. _level1Subscriptions
					.Where(pair => pair.Value.Section == section &&
						pair.Value.Symbol.EqualsIgnoreCase(symbol))
					.Select(static pair => pair.Key)];
				var acceptedTicks = new List<long>();
				foreach (var pair in _tickSubscriptions)
				{
					var state = pair.Value;
					if (state.Section != section || !state.Symbol.EqualsIgnoreCase(symbol) ||
						(!state.LastTradeId.IsEmpty() &&
							state.LastTradeId.EqualsIgnoreCase(trade.TradeId)) ||
						(state.LastTime != default && time < state.LastTime))
						continue;
					state.LastTradeId = trade.TradeId;
					state.LastTime = time;
					acceptedTicks.Add(pair.Key);
				}
				tickIds = [.. acceptedTicks];
				candleEmissions = UpdateCandles(symbol, section, time,
					trade.Price.ToDecimal() ?? 0m, trade.Quantity.ToDecimal() ?? 0m);
			}

			foreach (var id in level1Ids)
				await SendOutMessageAsync(new Level1ChangeMessage
					{
						SecurityId = symbol.ToStockSharp(section),
						ServerTime = time,
						OriginalTransactionId = id,
					}
					.TryAdd(Level1Fields.LastTradePrice, trade.Price.ToDecimal())
					.TryAdd(Level1Fields.LastTradeVolume, trade.Quantity.ToDecimal())
					.TryAdd(Level1Fields.LastTradeOrigin, trade.Side.ToStockSharpSide()),
					cancellationToken);

			foreach (var id in tickIds)
				await SendTradeAsync(trade, symbol, section, id, cancellationToken);
			foreach (var emission in candleEmissions)
				await SendCandleAsync(emission.OpenTime, emission.OpenPrice, emission.HighPrice,
					emission.LowPrice, emission.ClosePrice, emission.TotalVolume, emission.Symbol,
					emission.Section, emission.TimeFrame, emission.TransactionId, emission.State,
					cancellationToken);
		}
	}

	private CandleEmission[] UpdateCandles(string symbol, BullishSections section, DateTime time,
		decimal price, decimal volume)
	{
		var emissions = new List<CandleEmission>();
		foreach (var pair in _candleSubscriptions)
		{
			var state = pair.Value;
			if (state.Section != section || !state.Symbol.EqualsIgnoreCase(symbol))
				continue;
			var openTime = time.Align(state.TimeFrame);
			if (state.IsInitialized && openTime < state.OpenTime)
				continue;
			if (state.IsInitialized && openTime > state.OpenTime)
			{
				emissions.Add(ToEmission(pair.Key, state, CandleStates.Finished));
				state.IsInitialized = false;
			}
			if (!state.IsInitialized)
			{
				state.OpenTime = openTime;
				state.OpenPrice = price;
				state.HighPrice = price;
				state.LowPrice = price;
				state.ClosePrice = price;
				state.TotalVolume = volume;
				state.IsInitialized = true;
			}
			else
			{
				state.HighPrice = state.HighPrice.Max(price);
				state.LowPrice = state.LowPrice.Min(price);
				state.ClosePrice = price;
				state.TotalVolume += volume;
			}
			emissions.Add(ToEmission(pair.Key, state, CandleStates.Active));
		}
		return [.. emissions];
	}

	private static CandleEmission ToEmission(long id, CandleSubscription state,
		CandleStates candleState)
		=> new(id, state.Symbol, state.Section, state.TimeFrame, state.OpenTime,
			state.OpenPrice, state.HighPrice, state.LowPrice, state.ClosePrice,
			state.TotalVolume, candleState);

	private async ValueTask OnDepthAsync(string messageType, BullishWsLevel2Data data,
		CancellationToken cancellationToken)
	{
		var symbol = data?.Symbol?.ToUpperInvariant();
		if (symbol.IsEmpty())
			return;
		var section = ResolveSection(symbol);
		var hasGap = false;
		using (_sync.EnterScope())
		{
			if (!_books.TryGetValue(symbol, out var state))
			{
				state = new();
				_books.Add(symbol, state);
			}
			var isSnapshot = messageType.EqualsIgnoreCase("snapshot");
			if (!isSnapshot && state.Sequence > 0 && data.SequenceRange.First > state.Sequence + 1)
				hasGap = true;
			else if (isSnapshot || data.SequenceRange.Last > state.Sequence)
			{
				if (isSnapshot)
				{
					state.Bids.Clear();
					state.Asks.Clear();
				}
				ApplyLevels(state.Bids, data.Bids);
				ApplyLevels(state.Asks, data.Asks);
				state.Sequence = data.SequenceRange.Last;
				state.ServerTime = data.Timestamp.ToUtcTime(data.DateTime);
			}
		}

		if (hasGap)
		{
			await SendOutErrorAsync(new InvalidDataException(
				$"Bullish {symbol} order-book sequence gap; refreshing the REST snapshot."),
				cancellationToken);
			SetBookSnapshot(symbol, await RestClient.GetOrderBookAsync(symbol, cancellationToken));
		}

		(long Id, int Depth)[] subscriptions;
		long[] level1Subscriptions;
		using (_sync.EnterScope())
		{
			subscriptions = [.. _depthSubscriptions
				.Where(pair => pair.Value.Section == section &&
					pair.Value.Symbol.EqualsIgnoreCase(symbol))
				.Select(static pair => (pair.Key, pair.Value.Depth))];
			level1Subscriptions = [.. _level1Subscriptions
				.Where(pair => !pair.Value.IsTickStream && pair.Value.Section == section &&
					pair.Value.Symbol.EqualsIgnoreCase(symbol))
				.Select(static pair => pair.Key)];
		}
		foreach (var subscription in subscriptions)
		{
			var snapshot = GetBookSnapshot(symbol, subscription.Depth);
			await SendBookAsync(snapshot.Bids, snapshot.Asks, symbol, section,
				subscription.Id, snapshot.ServerTime, subscription.Depth, cancellationToken);
		}
		if (level1Subscriptions.Length > 0)
		{
			var snapshot = GetBookSnapshot(symbol, 1);
			foreach (var transactionId in level1Subscriptions)
				await SendBookLevel1Async(snapshot.Bids, snapshot.Asks, symbol, section,
					transactionId, snapshot.ServerTime, cancellationToken);
		}
	}

	private void SetBookSnapshot(string symbol, BullishOrderBook book)
	{
		if (book is null)
			return;
		using (_sync.EnterScope())
		{
			if (!_books.TryGetValue(symbol, out var state))
			{
				state = new();
				_books.Add(symbol, state);
			}
			state.Bids.Clear();
			state.Asks.Clear();
			ApplyLevels(state.Bids, book.Bids);
			ApplyLevels(state.Asks, book.Asks);
			state.Sequence = book.SequenceNumber;
			state.ServerTime = book.Timestamp.ToUtcTime(book.DateTime);
		}
	}

	private (BullishBookLevel[] Bids, BullishBookLevel[] Asks, DateTime ServerTime)
		GetBookSnapshot(string symbol, int depth)
	{
		using (_sync.EnterScope())
		{
			if (!_books.TryGetValue(symbol, out var state))
				return ([], [], CurrentTime);
			return (
				[.. state.Bids.Take(depth).Select(static pair => new BullishBookLevel
				{
					Price = pair.Key.ToWire(),
					Quantity = pair.Value.ToWire(),
				})],
				[.. state.Asks.Take(depth).Select(static pair => new BullishBookLevel
				{
					Price = pair.Key.ToWire(),
					Quantity = pair.Value.ToWire(),
				})],
				state.ServerTime == default ? CurrentTime : state.ServerTime);
		}
	}

	private static void ApplyLevels(SortedDictionary<decimal, decimal> target,
		BullishBookLevel[] levels)
	{
		foreach (var level in levels ?? [])
		{
			if (level?.Price.ToDecimal() is not decimal price ||
				level.Quantity.ToDecimal() is not decimal quantity)
				continue;
			if (quantity == 0m)
				target.Remove(price);
			else
				target[price] = quantity;
		}
	}

	private ValueTask SendLevel1Async(BullishTick tick, string symbol, BullishSections section,
		long transactionId, CancellationToken cancellationToken)
	{
		if (tick is null)
			return default;
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = tick.CreatedAtTimestamp.ToUtcTime(tick.CreatedAtDateTime),
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, tick.Last.ToDecimal())
		.TryAdd(Level1Fields.LastTradeVolume, tick.LastTradeQuantity.ToDecimal())
		.TryAdd(Level1Fields.OpenPrice, tick.Open.ToDecimal())
		.TryAdd(Level1Fields.HighPrice, tick.High.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, tick.Low.ToDecimal())
		.TryAdd(Level1Fields.Volume, tick.BaseVolume.ToDecimal())
		.TryAdd(Level1Fields.BestBidPrice, tick.BestBid.ToDecimal())
		.TryAdd(Level1Fields.BestBidVolume, tick.BidVolume.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, tick.BestAsk.ToDecimal())
		.TryAdd(Level1Fields.BestAskVolume, tick.AskVolume.ToDecimal())
		.TryAdd(Level1Fields.SettlementPrice, tick.MarkPrice.ToDecimal())
		.TryAdd(Level1Fields.OpenInterest, tick.OpenInterest.ToDecimal()), cancellationToken);
	}

	private ValueTask SendLevel1SnapshotAsync(BullishOrderBook book, BullishTrade trade,
		string symbol, BullishSections section, long transactionId,
		CancellationToken cancellationToken)
	{
		var bookTime = book is null
			? default
			: book.Timestamp.ToUtcTime(book.DateTime);
		var tradeTime = trade is null
			? default
			: trade.CreatedAtTimestamp.ToUtcTime(trade.CreatedAtDateTime);
		var serverTime = tradeTime > bookTime ? tradeTime : bookTime;
		if (serverTime == default)
			serverTime = CurrentTime;
		var bid = book?.Bids?.FirstOrDefault();
		var ask = book?.Asks?.FirstOrDefault();
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = serverTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, bid?.Price.ToDecimal())
		.TryAdd(Level1Fields.BestBidVolume, bid?.Quantity.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, ask?.Price.ToDecimal())
		.TryAdd(Level1Fields.BestAskVolume, ask?.Quantity.ToDecimal())
		.TryAdd(Level1Fields.LastTradePrice, trade?.Price.ToDecimal())
		.TryAdd(Level1Fields.LastTradeVolume, trade?.Quantity.ToDecimal())
		.TryAdd(Level1Fields.LastTradeOrigin,
			trade?.Side.IsEmpty() == false ? trade.Side.ToStockSharpSide() : null),
			cancellationToken);
	}

	private ValueTask SendBookLevel1Async(BullishBookLevel[] bids, BullishBookLevel[] asks,
		string symbol, BullishSections section, long transactionId, DateTime serverTime,
		CancellationToken cancellationToken)
	{
		var bid = bids?.FirstOrDefault();
		var ask = asks?.FirstOrDefault();
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = serverTime,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.BestBidPrice, bid?.Price.ToDecimal())
		.TryAdd(Level1Fields.BestBidVolume, bid?.Quantity.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, ask?.Price.ToDecimal())
		.TryAdd(Level1Fields.BestAskVolume, ask?.Quantity.ToDecimal()), cancellationToken);
	}

	private ValueTask SendBookAsync(BullishBookLevel[] bids, BullishBookLevel[] asks,
		string symbol, BullishSections section, long transactionId, DateTime serverTime, int depth,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = serverTime,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = ToQuotes(bids, depth),
			Asks = ToQuotes(asks, depth),
		}, cancellationToken);

	private ValueTask SendTradeAsync(BullishTrade trade, string symbol, BullishSections section,
		long transactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = symbol.ToStockSharp(section),
			ServerTime = trade.CreatedAtTimestamp.ToUtcTime(trade.CreatedAtDateTime),
			OriginalTransactionId = transactionId,
			TradeStringId = trade.TradeId,
			TradePrice = trade.Price.ToDecimal(),
			TradeVolume = trade.Quantity.ToDecimal(),
			OriginSide = trade.Side.ToStockSharpSide(),
		}, cancellationToken);

	private ValueTask SendCandleAsync(BullishCandle candle, string symbol,
		BullishSections section, TimeSpan timeFrame, long transactionId, CandleStates state,
		CancellationToken cancellationToken)
		=> SendCandleAsync(candle.CreatedAtTimestamp.ToUtcTime(candle.CreatedAtDateTime),
			candle.Open.ToDecimal() ?? 0m, candle.High.ToDecimal() ?? 0m,
			candle.Low.ToDecimal() ?? 0m, candle.Close.ToDecimal() ?? 0m,
			candle.Volume.ToDecimal() ?? 0m, symbol, section, timeFrame, transactionId, state,
			cancellationToken);

	private ValueTask SendCandleAsync(DateTime openTime, decimal open, decimal high, decimal low,
		decimal close, decimal volume, string symbol, BullishSections section, TimeSpan timeFrame,
		long transactionId, CandleStates state, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = symbol.ToStockSharp(section),
			OpenTime = openTime,
			CloseTime = openTime + timeFrame,
			OpenPrice = open,
			HighPrice = high,
			LowPrice = low,
			ClosePrice = close,
			TotalVolume = volume,
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = state,
		}, cancellationToken);

	private static QuoteChange[] ToQuotes(BullishBookLevel[] levels, int depth)
		=> [.. (levels ?? []).Take(depth).Select(static level => new QuoteChange(
			level.Price.ToDecimal() ?? 0m, level.Quantity.ToDecimal() ?? 0m))];

	private static int GetCandleCount(MarketDataMessage message, TimeSpan timeFrame, DateTime to)
	{
		if (message.Count is long count)
			return count.Min(1000).Max(1).To<int>();
		if (message.From is DateTime from && to > from)
			return ((to - from).Ticks / timeFrame.Ticks + 1).Min(1000L).Max(1L).To<int>();
		return 25;
	}
}
