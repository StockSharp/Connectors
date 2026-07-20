namespace StockSharp.StandX;

public partial class StandXMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = Math.Max(0, lookupMsg.Count ?? long.MaxValue);
		foreach (var instrument in GetInstruments().OrderBy(
			static item => item.Symbol, StringComparer.OrdinalIgnoreCase))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(BoardCodes.StandX))
				continue;
			if (!lookupMsg.SecurityId.SecurityCode.IsEmpty() &&
				!lookupMsg.SecurityId.SecurityCode.EqualsIgnoreCase(
					instrument.Symbol))
				continue;
			if (securityTypes.Count > 0 &&
				!securityTypes.Contains(SecurityTypes.Future))
				continue;
			var security = CreateSecurity(instrument, lookupMsg.TransactionId);
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
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.From is not null || mdMsg.To is not null)
			throw new NotSupportedException(
				"StandX does not publish historical Level1 changes.");
		var instrument = GetInstrument(mdMsg.SecurityId);
		var market = await RestClient.GetMarketAsync(instrument.Symbol,
			cancellationToken);
		await SendMarketAsync(market, mdMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var key = new StreamKey(StandXChannels.Price, instrument.Symbol);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = instrument.Symbol,
			});
			subscribe = AddStreamReference(key);
		}
		if (subscribe)
			await MarketSocket.SubscribeAsync(key.Channel, key.Symbol,
				cancellationToken);
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
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}
		if (mdMsg.From is not null || mdMsg.To is not null)
			throw new NotSupportedException(
				"StandX does not publish historical order-book snapshots.");
		var instrument = GetInstrument(mdMsg.SecurityId);
		var depth = (mdMsg.MaxDepth ?? MarketDepth).Max(1).Min(MarketDepth);
		var book = await RestClient.GetOrderBookAsync(instrument.Symbol,
			cancellationToken);
		await SendBookAsync(book, 0, mdMsg.TransactionId, depth,
			cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var key = new StreamKey(StandXChannels.DepthBook, instrument.Symbol);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = instrument.Symbol,
				Depth = depth,
			});
			subscribe = AddStreamReference(key);
		}
		if (subscribe)
			await MarketSocket.SubscribeAsync(key.Channel, key.Symbol,
				cancellationToken);
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
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}
		var instrument = GetInstrument(mdMsg.SecurityId);
		var from = mdMsg.From?.ToUniversalTime() ?? DateTime.MinValue;
		var to = (mdMsg.To ?? ServerTime).ToUniversalTime();
		var limit = (mdMsg.Count ?? 100).Min(1000).Max(1).To<int>();
		var trades = (await RestClient.GetRecentTradesAsync(instrument.Symbol,
			cancellationToken) ?? [])
			.Where(trade => trade?.Time.ToStandXTime() is DateTime time &&
				time >= from && time <= to)
			.OrderBy(static trade => trade.Time.ToStandXTime())
			.TakeLast(limit)
			.ToArray();
		for (var index = 0; index < trades.Length; index++)
			await SendRecentTradeAsync(trades[index], mdMsg.TransactionId, index,
				cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var key = new StreamKey(StandXChannels.PublicTrade, instrument.Symbol);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = instrument.Symbol,
			});
			subscribe = AddStreamReference(key);
		}
		if (subscribe)
			await MarketSocket.SubscribeAsync(key.Channel, key.Symbol,
				cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			using (_sync.EnterScope())
				_candleSubscriptions.Remove(mdMsg.OriginalTransactionId);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}
		var instrument = GetInstrument(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		_ = timeFrame.ToStandXResolution();
		var to = (mdMsg.To ?? ServerTime).ToUniversalTime();
		var count = GetCandleCount(mdMsg, timeFrame, to);
		var from = mdMsg.From?.ToUniversalTime() ??
			to - TimeSpan.FromTicks(checked(timeFrame.Ticks * (long)count));
		var candles = await LoadCandlesAsync(instrument.Symbol, from, to,
			timeFrame, count, cancellationToken);
		foreach (var candle in candles)
			await SendCandleAsync(instrument.Symbol, candle, timeFrame,
				mdMsg.TransactionId, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		using (_sync.EnterScope())
			_candleSubscriptions.Add(mdMsg.TransactionId, new()
			{
				TransactionId = mdMsg.TransactionId,
				Symbol = instrument.Symbol,
				TimeFrame = timeFrame,
				LastOpenTime = candles.LastOrDefault()?.OpenTime ?? default,
			});
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_level1Subscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseStreamReference(new(StandXChannels.Price,
					subscription.Symbol));
		}
		if (unsubscribe)
			await MarketSocket.UnsubscribeAsync(StandXChannels.Price,
				subscription.Symbol, cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_depthSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseStreamReference(new(StandXChannels.DepthBook,
					subscription.Symbol));
		}
		if (unsubscribe)
			await MarketSocket.UnsubscribeAsync(StandXChannels.DepthBook,
				subscription.Symbol, cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		var unsubscribe = false;
		using (_sync.EnterScope())
		{
			if (_tickSubscriptions.Remove(transactionId, out subscription))
				unsubscribe = ReleaseStreamReference(new(
					StandXChannels.PublicTrade, subscription.Symbol));
		}
		if (unsubscribe)
			await MarketSocket.UnsubscribeAsync(StandXChannels.PublicTrade,
				subscription.Symbol, cancellationToken);
	}

	private async ValueTask OnPriceAsync(StandXSymbolPrice price,
		CancellationToken cancellationToken)
	{
		if (price?.Symbol.IsEmpty() != false)
			return;
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _level1Subscriptions
				.Where(pair => pair.Value.Symbol.EqualsIgnoreCase(price.Symbol))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendPriceAsync(price, id, cancellationToken);
	}

	private async ValueTask OnBookAsync(StandXOrderBook book, long sequence,
		CancellationToken cancellationToken)
	{
		if (book?.Symbol.IsEmpty() != false)
			return;
		(long Id, int Depth)[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions
				.Where(pair => pair.Value.Symbol.EqualsIgnoreCase(book.Symbol))
				.Select(static pair => (pair.Key, pair.Value.Depth))];
		foreach (var subscription in subscriptions)
			await SendBookAsync(book, sequence, subscription.Id,
				subscription.Depth, cancellationToken);
	}

	private async ValueTask OnPublicTradeAsync(StandXPublicTrade trade,
		CancellationToken cancellationToken)
	{
		if (trade?.Symbol.IsEmpty() != false)
			return;
		long[] ids;
		using (_sync.EnterScope())
			ids = [.. _tickSubscriptions
				.Where(pair => pair.Value.Symbol.EqualsIgnoreCase(trade.Symbol))
				.Select(static pair => pair.Key)];
		foreach (var id in ids)
			await SendPublicTradeAsync(trade, id, cancellationToken);
	}

	private ValueTask SendMarketAsync(StandXMarket market, long transactionId,
		CancellationToken cancellationToken)
	{
		if (market?.Symbol.IsEmpty() != false)
			throw new InvalidDataException("StandX returned an invalid market.");
		var time = market.Time.ToStandXTime() ?? ServerTime;
		var bid = market.BestBidPrice.ToDecimal() ??
			market.Spread?.ElementAtOrDefault(0).ToDecimal();
		var ask = market.BestAskPrice.ToDecimal() ??
			market.Spread?.ElementAtOrDefault(1).ToDecimal();
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = market.Symbol.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, market.LastPrice.ToDecimal())
		.TryAdd(Level1Fields.TheorPrice, market.MarkPrice.ToDecimal())
		.TryAdd(Level1Fields.Index, market.IndexPrice.ToDecimal())
		.TryAdd(Level1Fields.BestBidPrice, bid)
		.TryAdd(Level1Fields.BestAskPrice, ask)
		.TryAdd(Level1Fields.BestBidTime, bid is null ? null : time)
		.TryAdd(Level1Fields.BestAskTime, ask is null ? null : time)
		.TryAdd(Level1Fields.OpenPrice, market.OpenPrice24h.ToDecimal())
		.TryAdd(Level1Fields.HighPrice, market.HighPrice24h.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, market.LowPrice24h.ToDecimal())
		.TryAdd(Level1Fields.Volume, market.Volume24h.ToDecimal())
		.TryAdd(Level1Fields.Turnover, market.QuoteVolume24h.ToDecimal())
		.TryAdd(Level1Fields.OpenInterest, market.OpenInterest.ToDecimal())
		.TryAdd(Level1Fields.State, SecurityStates.Trading), cancellationToken);
	}

	private ValueTask SendPriceAsync(StandXSymbolPrice price,
		long transactionId, CancellationToken cancellationToken)
	{
		var time = price.Time.ToStandXTime() ?? ServerTime;
		var bid = price.BestBidPrice.ToDecimal() ??
			price.Spread?.ElementAtOrDefault(0).ToDecimal();
		var ask = price.BestAskPrice.ToDecimal() ??
			price.Spread?.ElementAtOrDefault(1).ToDecimal();
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = price.Symbol.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, price.LastPrice.ToDecimal())
		.TryAdd(Level1Fields.TheorPrice, price.MarkPrice.ToDecimal())
		.TryAdd(Level1Fields.Index, price.IndexPrice.ToDecimal())
		.TryAdd(Level1Fields.BestBidPrice, bid)
		.TryAdd(Level1Fields.BestAskPrice, ask)
		.TryAdd(Level1Fields.BestBidTime, bid is null ? null : time)
		.TryAdd(Level1Fields.BestAskTime, ask is null ? null : time)
		.TryAdd(Level1Fields.State, SecurityStates.Trading), cancellationToken);
	}

	private ValueTask SendBookAsync(StandXOrderBook book, long sequence,
		long transactionId, int depth, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = book.Symbol.ToStockSharp(),
			ServerTime = book.Timestamp is > 0
				? book.Timestamp.Value.ToStandXTime()
				: ServerTime,
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			SeqNum = sequence,
			Bids = ToQuotes(book.Bids, depth, true),
			Asks = ToQuotes(book.Asks, depth, false),
		}, cancellationToken);

	private ValueTask SendPublicTradeAsync(StandXPublicTrade trade,
		long transactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = trade.Symbol.ToStockSharp(),
			ServerTime = trade.Timestamp.ToStandXTime(),
			OriginalTransactionId = transactionId,
			TradeId = trade.Id,
			TradePrice = trade.Price.ParseRequiredDecimal("trade price"),
			TradeVolume = trade.Quantity.ParseRequiredDecimal("trade quantity"),
			OriginSide = trade.Side.ToStockSharp(),
		}, cancellationToken);

	private ValueTask SendRecentTradeAsync(StandXRecentTrade trade,
		long transactionId, int index, CancellationToken cancellationToken)
	{
		var time = trade.Time.ToStandXTime() ?? throw new InvalidDataException(
			"StandX returned a recent trade with invalid time.");
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = trade.Symbol.ToStockSharp(),
			ServerTime = time,
			OriginalTransactionId = transactionId,
			TradeStringId = "REST-" + time.Ticks.ToString(
				CultureInfo.InvariantCulture) + "-" + index.ToString(
					CultureInfo.InvariantCulture),
			TradePrice = trade.Price.ParseRequiredDecimal("trade price"),
			TradeVolume = trade.Quantity.ParseRequiredDecimal("trade quantity"),
			OriginSide = trade.IsBuyerTaker ? Sides.Buy : Sides.Sell,
		}, cancellationToken);
	}

	private async ValueTask<StandXCandle[]> LoadCandlesAsync(string symbol,
		DateTime from, DateTime to, TimeSpan timeFrame, int count,
		CancellationToken cancellationToken)
	{
		var response = await RestClient.GetCandlesAsync(symbol, from, to,
			timeFrame, count, cancellationToken);
		return [.. response.ToCandles()
			.Where(candle => candle.OpenTime >= from && candle.OpenTime <= to)
			.OrderBy(static candle => candle.OpenTime)
			.TakeLast(count)];
	}

	private async ValueTask PollCandleAsync(CandleSubscription subscription,
		CancellationToken cancellationToken)
	{
		var to = ServerTime;
		var from = to - TimeSpan.FromTicks(subscription.TimeFrame.Ticks * 3);
		var candles = await LoadCandlesAsync(subscription.Symbol, from, to,
			subscription.TimeFrame, 3, cancellationToken);
		foreach (var candle in candles.Where(candle =>
			candle.OpenTime >= subscription.LastOpenTime))
			await SendCandleAsync(subscription.Symbol, candle,
				subscription.TimeFrame, subscription.TransactionId,
				cancellationToken);
		if (candles.LastOrDefault() is { } last)
			using (_sync.EnterScope())
				subscription.LastOpenTime = last.OpenTime;
	}

	private ValueTask SendCandleAsync(string symbol, StandXCandle candle,
		TimeSpan timeFrame, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = symbol.ToStockSharp(),
			OpenTime = candle.OpenTime,
			CloseTime = candle.OpenTime + timeFrame,
			OpenPrice = candle.OpenPrice,
			HighPrice = candle.HighPrice,
			LowPrice = candle.LowPrice,
			ClosePrice = candle.ClosePrice,
			TotalVolume = candle.Volume,
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = candle.OpenTime + timeFrame <= ServerTime
				? CandleStates.Finished
				: CandleStates.Active,
		}, cancellationToken);

	private static SecurityMessage CreateSecurity(StandXSymbolInfo instrument,
		long transactionId)
		=> new SecurityMessage
		{
			SecurityId = instrument.Symbol.ToStockSharp(),
			Name = $"{instrument.BaseAsset}/{instrument.QuoteAsset} perpetual",
			ShortName = instrument.Symbol,
			Class = "PERPETUAL",
			SecurityType = SecurityTypes.Future,
			Currency = CurrencyTypes.USD,
			PriceStep = StandXExtensions.GetStep(instrument.PriceTickDecimals,
				"price"),
			VolumeStep = StandXExtensions.GetStep(
				instrument.QuantityTickDecimals, "quantity"),
			MinVolume = instrument.MinimumOrderQuantity.ToDecimal(),
			MaxVolume = instrument.MaximumOrderQuantity.ToDecimal(),
			Multiplier = 1m,
			OriginalTransactionId = transactionId,
		}.TryFillUnderlyingId(instrument.BaseAsset);

	private static QuoteChange[] ToQuotes(string[][] levels, int depth,
		bool isBids)
	{
		var quotes = new List<QuoteChange>();
		foreach (var level in levels ?? [])
		{
			if (level is not { Length: >= 2 })
				throw new InvalidDataException(
					"StandX returned a malformed order-book level.");
			var price = level[0].ParseRequiredDecimal("book price");
			var volume = level[1].ParseRequiredDecimal("book quantity");
			if (price <= 0 || volume < 0)
				throw new InvalidDataException(
					"StandX returned a non-positive price or negative book quantity.");
			if (volume > 0)
				quotes.Add(new(price, volume));
		}
		var ordered = isBids
			? quotes.OrderByDescending(static quote => quote.Price)
			: quotes.OrderBy(static quote => quote.Price);
		return [.. ordered.Take(depth)];
	}

	private static int GetCandleCount(MarketDataMessage message,
		TimeSpan timeFrame, DateTime to)
	{
		if (message.Count is long count)
			return count.Min(5000).Max(1).To<int>();
		if (message.From is DateTime from && to > from.ToUniversalTime())
			return ((to - from.ToUniversalTime()).Ticks / timeFrame.Ticks + 1)
				.Min(5000L).Max(1L).To<int>();
		return 500;
	}
}
