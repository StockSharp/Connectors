namespace StockSharp.Grvt;

public partial class GrvtMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId,
			cancellationToken);
		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;
		GrvtInstrument[] instruments;
		using (_sync.EnterScope())
			instruments = [.. _instruments.Values];

		foreach (var instrument in instruments.OrderBy(static item =>
			item.Instrument, StringComparer.OrdinalIgnoreCase))
		{
			var securityType = instrument.Kind.ToSecurityType();
			if (securityTypes.Count > 0 && !securityTypes.Contains(securityType))
				continue;
			var priceStep = instrument.TickSize.ParseRequiredDecimal("tick size");
			var volumeStep = instrument.MinimumSize.ParseRequiredDecimal(
				"minimum size");
			var security = new SecurityMessage
			{
				SecurityId = instrument.Instrument.ToStockSharp(),
				Name = instrument.Instrument,
				SecurityType = securityType,
				Currency = instrument.Quote.ToCurrency(),
				PriceStep = priceStep,
				Decimals = priceStep.GetCachedDecimals(),
				VolumeStep = volumeStep,
				MinVolume = volumeStep,
				MaxVolume = instrument.MaximumPositionSize.ToDecimal(),
				Multiplier = securityType == SecurityTypes.Future ? 1m : null,
				ExpiryDate = instrument.TryGetExpiry(),
				Strike = instrument.TryGetStrike(),
				OptionType = instrument.Kind.ToOptionType(),
				OriginalTransactionId = lookupMsg.TransactionId,
			}.TryFillUnderlyingId(instrument.Base.ToUpperInvariant());
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			await SendOutMessageAsync(security, cancellationToken);
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

		var instrument = GetInstrument(GetInstrumentCode(mdMsg.SecurityId));
		var ticker = await RestClient.GetTickerAsync(instrument.Instrument,
			cancellationToken);
		await SendTickerAsync(ticker, mdMsg.TransactionId, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var stream = GetTickerStream(instrument.Instrument);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Add(mdMsg.TransactionId, new()
			{
				Instrument = instrument.Instrument,
			});
			subscribe = AddStreamReference(stream);
		}
		try
		{
			if (subscribe)
				await MarketSocket.SubscribeAsync(stream.Stream, stream.Selector,
					cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_level1Subscriptions.Remove(mdMsg.TransactionId);
				ReleaseStreamReference(stream);
			}
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

		var instrument = GetInstrument(GetInstrumentCode(mdMsg.SecurityId));
		var depth = GetRequestedDepth(mdMsg.MaxDepth);
		var book = await RestClient.GetBookAsync(instrument.Instrument,
			MarketDepth, cancellationToken);
		await SendBookAsync(book, mdMsg.TransactionId, depth, cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var stream = GetBookStream(instrument.Instrument);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_depthSubscriptions.Add(mdMsg.TransactionId, new()
			{
				Instrument = instrument.Instrument,
				Depth = depth,
			});
			subscribe = AddStreamReference(stream);
		}
		try
		{
			if (subscribe)
				await MarketSocket.SubscribeAsync(stream.Stream, stream.Selector,
					cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_depthSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseStreamReference(stream);
			}
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

		var instrument = GetInstrument(GetInstrumentCode(mdMsg.SecurityId));
		var limit = GetHistoryLimit(mdMsg.Count, 100);
		var trades = await LoadTradesAsync(instrument.Instrument,
			mdMsg.From?.ToUniversalTime(), mdMsg.To?.ToUniversalTime(), limit,
			cancellationToken);
		var subscription = new TickSubscription
		{
			Instrument = instrument.Instrument,
		};
		foreach (var trade in trades.OrderBy(static item =>
			item.EventTime.ToGrvtTime()))
		{
			var time = trade.EventTime.ToGrvtTime();
			subscription.TryAccept(trade.TradeId, time);
			await SendTradeAsync(trade, mdMsg.TransactionId, cancellationToken);
		}
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var stream = GetTradeStream(instrument.Instrument);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_tickSubscriptions.Add(mdMsg.TransactionId, subscription);
			subscribe = AddStreamReference(stream);
		}
		try
		{
			if (subscribe)
				await MarketSocket.SubscribeAsync(stream.Stream, stream.Selector,
					cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_tickSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseStreamReference(stream);
			}
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
		{
			await UnsubscribeCandlesAsync(mdMsg.OriginalTransactionId,
				cancellationToken);
			return;
		}

		var instrument = GetInstrument(GetInstrumentCode(mdMsg.SecurityId));
		var timeFrame = mdMsg.GetTimeFrame();
		var interval = timeFrame.ToGrvtInterval();
		var to = (mdMsg.To ?? ServerTime).ToUniversalTime();
		var limit = GetCandleCount(mdMsg, timeFrame, to);
		var from = mdMsg.From?.ToUniversalTime() ??
			to.AddTicks(-timeFrame.Ticks * Math.Max(0, limit - 1));
		var candles = await LoadCandlesticksAsync(instrument.Instrument,
			interval, from, to, limit, cancellationToken);
		var subscription = new CandleSubscription
		{
			Instrument = instrument.Instrument,
			TimeFrame = timeFrame,
		};
		foreach (var candle in candles.OrderBy(static item =>
			item.OpenTime.ToGrvtTime()))
		{
			var openTime = candle.OpenTime.ToGrvtTime();
			subscription.LastOpenTime = openTime;
			await SendCandleAsync(candle, mdMsg.TransactionId, timeFrame,
				candle.CloseTime.ToGrvtTime() <= ServerTime
					? CandleStates.Finished
					: CandleStates.Active,
				cancellationToken);
		}
		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var stream = GetCandleStream(instrument.Instrument, timeFrame);
		bool subscribe;
		using (_sync.EnterScope())
		{
			_candleSubscriptions.Add(mdMsg.TransactionId, subscription);
			subscribe = AddStreamReference(stream);
		}
		try
		{
			if (subscribe)
				await MarketSocket.SubscribeAsync(stream.Stream, stream.Selector,
					cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_candleSubscriptions.Remove(mdMsg.TransactionId);
				ReleaseStreamReference(stream);
			}
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask<GrvtTrade[]> LoadTradesAsync(string instrument,
		DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
	{
		if (from is null && to is null)
			return await RestClient.GetRecentTradesAsync(instrument, limit,
				cancellationToken) ?? [];
		var result = new List<GrvtTrade>();
		string cursor = null;
		do
		{
			var page = await RestClient.GetTradeHistoryAsync(new()
			{
				Instrument = instrument,
				StartTime = from?.ToGrvtNanoseconds(),
				EndTime = to?.ToGrvtNanoseconds(),
				Limit = (limit - result.Count).Min(1000).Max(1),
				Cursor = cursor,
			}, cancellationToken);
			result.AddRange(page?.Result ?? []);
			cursor = page?.Next;
		}
		while (!cursor.IsEmpty() && result.Count < limit);
		return [.. result
			.Where(static item => item?.TradeId.IsEmpty() == false)
			.GroupBy(static item => item.TradeId,
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.First())
			.OrderBy(static item => item.EventTime.ToGrvtTime())
			.Take(limit)];
	}

	private async ValueTask<GrvtCandlestick[]> LoadCandlesticksAsync(
		string instrument, GrvtCandlestickIntervals interval, DateTime from,
		DateTime to, int limit, CancellationToken cancellationToken)
	{
		var result = new List<GrvtCandlestick>();
		string cursor = null;
		do
		{
			var page = await RestClient.GetCandlesticksAsync(new()
			{
				Instrument = instrument,
				Interval = interval,
				Type = GrvtCandlestickTypes.Trade,
				StartTime = from.ToGrvtNanoseconds(),
				EndTime = to.ToGrvtNanoseconds(),
				Limit = (limit - result.Count).Min(1000).Max(1),
				Cursor = cursor,
			}, cancellationToken);
			result.AddRange(page?.Result ?? []);
			cursor = page?.Next;
		}
		while (!cursor.IsEmpty() && result.Count < limit);
		return [.. result
			.Where(static item => item is not null)
			.GroupBy(static item => item.OpenTime)
			.Select(static group => group.First())
			.OrderBy(static item => item.OpenTime.ToGrvtTime())
			.Take(limit)];
	}

	private async ValueTask UnsubscribeLevel1Async(long transactionId,
		CancellationToken cancellationToken)
	{
		MarketSubscription subscription = null;
		bool unsubscribe = false;
		StreamSubscription stream = default;
		using (_sync.EnterScope())
		{
			if (_level1Subscriptions.Remove(transactionId, out subscription))
			{
				stream = GetTickerStream(subscription.Instrument);
				unsubscribe = ReleaseStreamReference(stream);
			}
		}
		if (unsubscribe)
			await MarketSocket.UnsubscribeAsync(stream.Stream, stream.Selector,
				cancellationToken);
	}

	private async ValueTask UnsubscribeDepthAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		DepthSubscription subscription = null;
		bool unsubscribe = false;
		StreamSubscription stream = default;
		using (_sync.EnterScope())
		{
			if (_depthSubscriptions.Remove(transactionId, out subscription))
			{
				stream = GetBookStream(subscription.Instrument);
				unsubscribe = ReleaseStreamReference(stream);
			}
		}
		if (unsubscribe)
			await MarketSocket.UnsubscribeAsync(stream.Stream, stream.Selector,
				cancellationToken);
	}

	private async ValueTask UnsubscribeTicksAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		TickSubscription subscription = null;
		bool unsubscribe = false;
		StreamSubscription stream = default;
		using (_sync.EnterScope())
		{
			if (_tickSubscriptions.Remove(transactionId, out subscription))
			{
				stream = GetTradeStream(subscription.Instrument);
				unsubscribe = ReleaseStreamReference(stream);
			}
		}
		if (unsubscribe)
			await MarketSocket.UnsubscribeAsync(stream.Stream, stream.Selector,
				cancellationToken);
	}

	private async ValueTask UnsubscribeCandlesAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		CandleSubscription subscription = null;
		bool unsubscribe = false;
		StreamSubscription stream = default;
		using (_sync.EnterScope())
		{
			if (_candleSubscriptions.Remove(transactionId, out subscription))
			{
				stream = GetCandleStream(subscription.Instrument,
					subscription.TimeFrame);
				unsubscribe = ReleaseStreamReference(stream);
			}
		}
		if (unsubscribe)
			await MarketSocket.UnsubscribeAsync(stream.Stream, stream.Selector,
				cancellationToken);
	}

	private async ValueTask OnTickerAsync(string selector, GrvtTicker ticker,
		CancellationToken cancellationToken)
	{
		_ = selector;
		if (ticker?.Instrument.IsEmpty() != false)
			return;
		long[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _level1Subscriptions
				.Where(pair => pair.Value.Instrument.EqualsIgnoreCase(
					ticker.Instrument))
				.Select(static pair => pair.Key)];
		foreach (var transactionId in subscriptions)
			await SendTickerAsync(ticker, transactionId, cancellationToken);
	}

	private async ValueTask OnBookAsync(string selector, GrvtOrderBook book,
		CancellationToken cancellationToken)
	{
		_ = selector;
		if (book?.Instrument.IsEmpty() != false)
			return;
		KeyValuePair<long, DepthSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions.Where(pair =>
				pair.Value.Instrument.EqualsIgnoreCase(book.Instrument))];
		foreach (var (transactionId, subscription) in subscriptions)
			await SendBookAsync(book, transactionId, subscription.Depth,
				cancellationToken);
	}

	private async ValueTask OnPublicTradeAsync(string selector, GrvtTrade trade,
		CancellationToken cancellationToken)
	{
		_ = selector;
		if (trade?.Instrument.IsEmpty() != false || trade.TradeId.IsEmpty())
			return;
		var time = trade.EventTime.ToGrvtTime();
		long[] subscriptions;
		using (_sync.EnterScope())
		{
			var accepted = new List<long>();
			foreach (var (transactionId, subscription) in _tickSubscriptions)
			{
				if (subscription.Instrument.EqualsIgnoreCase(trade.Instrument) &&
					subscription.TryAccept(trade.TradeId, time))
					accepted.Add(transactionId);
			}
			subscriptions = [.. accepted];
		}
		foreach (var transactionId in subscriptions)
			await SendTradeAsync(trade, transactionId, cancellationToken);
	}

	private async ValueTask OnCandlestickAsync(string selector,
		GrvtCandlestick candle, CancellationToken cancellationToken)
	{
		if (candle?.Instrument.IsEmpty() != false)
			return;
		var openTime = candle.OpenTime.ToGrvtTime();
		KeyValuePair<long, CandleSubscription>[] subscriptions;
		using (_sync.EnterScope())
		{
			var accepted = new List<KeyValuePair<long, CandleSubscription>>();
			foreach (var pair in _candleSubscriptions)
			{
				var stream = GetCandleStream(pair.Value.Instrument,
					pair.Value.TimeFrame);
				if (!stream.Selector.EqualsIgnoreCase(selector) ||
					openTime < pair.Value.LastOpenTime)
					continue;
				pair.Value.LastOpenTime = openTime;
				accepted.Add(pair);
			}
			subscriptions = [.. accepted];
		}
		foreach (var (transactionId, subscription) in subscriptions)
			await SendCandleAsync(candle, transactionId,
				subscription.TimeFrame,
				candle.CloseTime.ToGrvtTime() <= ServerTime
					? CandleStates.Finished
					: CandleStates.Active,
				cancellationToken);
	}

	private ValueTask SendTickerAsync(GrvtTicker ticker, long transactionId,
		CancellationToken cancellationToken)
	{
		if (ticker?.Instrument.IsEmpty() != false)
			return default;
		var buyVolume = ticker.BuyVolume24hBase.ToDecimal();
		var sellVolume = ticker.SellVolume24hBase.ToDecimal();
		decimal? volume = buyVolume is null && sellVolume is null
			? null
			: (buyVolume ?? 0) + (sellVolume ?? 0);
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = ticker.Instrument.ToStockSharp(),
			ServerTime = ticker.EventTime.IsEmpty()
				? ServerTime
				: ticker.EventTime.ToGrvtTime(),
			OriginalTransactionId = transactionId,
		}
		.TryAdd(Level1Fields.LastTradePrice, ticker.LastPrice.ToDecimal())
		.TryAdd(Level1Fields.LastTradeVolume, ticker.LastSize.ToDecimal())
		.TryAdd(Level1Fields.BestBidPrice, ticker.BestBidPrice.ToDecimal())
		.TryAdd(Level1Fields.BestBidVolume, ticker.BestBidSize.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, ticker.BestAskPrice.ToDecimal())
		.TryAdd(Level1Fields.BestAskVolume, ticker.BestAskSize.ToDecimal())
		.TryAdd(Level1Fields.OpenPrice, ticker.OpenPrice.ToDecimal())
		.TryAdd(Level1Fields.HighPrice, ticker.HighPrice.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, ticker.LowPrice.ToDecimal())
		.TryAdd(Level1Fields.OpenInterest, ticker.OpenInterest.ToDecimal())
		.TryAdd(Level1Fields.Volume, volume)
		.TryAdd(Level1Fields.TheorPrice, ticker.MarkPrice.ToDecimal())
		.TryAdd(Level1Fields.Index, ticker.IndexPrice.ToDecimal()),
			cancellationToken);
	}

	private ValueTask SendBookAsync(GrvtOrderBook book, long transactionId,
		int depth, CancellationToken cancellationToken)
	{
		if (book?.Instrument.IsEmpty() != false)
			return default;
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = book.Instrument.ToStockSharp(),
			ServerTime = book.EventTime.ToGrvtTime(),
			OriginalTransactionId = transactionId,
			State = QuoteChangeStates.SnapshotComplete,
			Bids = ToQuotes(book.Bids, depth),
			Asks = ToQuotes(book.Asks, depth),
		}, cancellationToken);
	}

	private ValueTask SendTradeAsync(GrvtTrade trade, long transactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = trade.Instrument.ToStockSharp(),
			ServerTime = trade.EventTime.ToGrvtTime(),
			OriginalTransactionId = transactionId,
			TradeStringId = trade.TradeId,
			TradePrice = trade.Price.ParseRequiredDecimal("trade price"),
			TradeVolume = trade.Size.ParseRequiredDecimal("trade size"),
			OriginSide = trade.IsTakerBuyer ? Sides.Buy : Sides.Sell,
		}, cancellationToken);

	private ValueTask SendCandleAsync(GrvtCandlestick candle,
		long transactionId, TimeSpan timeFrame, CandleStates state,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(new TimeFrameCandleMessage
		{
			SecurityId = candle.Instrument.ToStockSharp(),
			OpenTime = candle.OpenTime.ToGrvtTime(),
			CloseTime = candle.CloseTime.ToGrvtTime(),
			OpenPrice = candle.Open.ParseRequiredDecimal("candle open"),
			HighPrice = candle.High.ParseRequiredDecimal("candle high"),
			LowPrice = candle.Low.ParseRequiredDecimal("candle low"),
			ClosePrice = candle.Close.ParseRequiredDecimal("candle close"),
			TotalVolume = candle.BaseVolume.ParseRequiredDecimal(
				"candle volume"),
			TypedArg = timeFrame,
			OriginalTransactionId = transactionId,
			State = state,
		}, cancellationToken);

	private static QuoteChange[] ToQuotes(GrvtBookLevel[] levels, int depth)
		=> [.. (levels ?? []).Take(depth).Select(static level =>
			new QuoteChange(
				level.Price.ParseRequiredDecimal("order-book price"),
				level.Size.ParseRequiredDecimal("order-book size"),
				level.NumberOfOrders))];

	private static int GetCandleCount(MarketDataMessage message,
		TimeSpan timeFrame, DateTime to)
	{
		if (message.Count is long count)
			return count.Min(1000).Max(1).To<int>();
		if (message.From is DateTime from && to > from)
			return ((to - from.ToUniversalTime()).Ticks / timeFrame.Ticks + 1)
				.Min(1000L).Max(1L).To<int>();
		return 100;
	}
}
